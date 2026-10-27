import { DurableObject } from 'cloudflare:workers';
import { GameEngine, type EngineSnapshot, type MoveResult } from './game/GameEngine.js';
import {
  PLAYER_COLORS_BY_COUNT,
  type BoardPosition, type EndReason, type GameConfig, type PieceType, type PlayerColor,
} from './types/game.js';
import { accounts, createRoom, type Env } from './index.js';
import type { PushMessage } from './push.js';
import { asPosition } from './validate.js';
import type { GameResult, RatingChange } from './ratings.js';

// ─────────────────────────────────────────────────────────────────────────────
// Room — one Durable Object per game, addressed by its 6-character code.
//
// Holds the authoritative GameEngine, the players' WebSockets and their seats.
// Uses WebSocket hibernation: between moves the object can be evicted from memory
// at no cost, so everything needed is persisted to storage after each change.
//
// Client → server messages: { type, requestId?, ...fields }
// Server → client messages: { type, ...fields }; requests are answered with
//   { type: 'ack', requestId, ok, error? }
// ─────────────────────────────────────────────────────────────────────────────

interface RoomMeta {
  code: string;
  hostUserId: string;
  isPublic: boolean;
  /** Made by Find Match (random opponents): only these games are ranked */
  matched?: boolean;
  createdAt: number;
  /** Lobby closes at this time if the game hasn't started (the host can extend it) */
  expiresAt?: number;
  /** Last time anyone did anything, for the idle cap */
  lastActivityAt?: number;
  /** When the last socket closed, for clearing finished rooms */
  emptySince?: number | null;
  placementStartedAt?: number | null;
  /** Last placement, ready or move, for the Live idle limit */
  lastPlayAt?: number;
  /** When the game ended; the room closes a few minutes later even if players stay on the result screen */
  gameOverAt?: number | null;
  /** Seats kept for particular players (a rematch room): nobody else can join */
  reserved?: Record<string, PlayerColor> | null;
  /** After the game: who wants a rematch, until when, and the new room once everyone agrees */
  rematch?: Rematch | null;
  /** Colours knocked out, in order (for finishing places) */
  eliminated?: PlayerColor[];
  /** Players who walked out (gone too long, or let a Take your time move lapse) */
  walkouts?: string[];
  /** Each colour's rating change once the result is recorded */
  ratings?: Record<string, RatingChange> | null;
  resultSent?: boolean;
  /** Take your time: the turn (by its start time) whose "running low" warning was sent */
  lowWarnedTurn?: number | null;
}

interface Rematch {
  deadline: number;
  votes: Record<string, boolean>; // userId → yes / no
  code: string | null;            // the new room, once started
  failed: boolean;                // not happening (someone said no or left, or time ran out)
  seats?: Record<string, PlayerColor>; // each player's colour in the new room
}

interface Attachment {
  userId: string;
  name: string;
  color: PlayerColor;
  /** The app handles private setup (game:placementsRevealed); older apps see placements live */
  reveal?: boolean;
}

interface GameOverInfo {
  winner: PlayerColor | null;
  reason: EndReason;
}

const MINUTE = 60 * 1000;
const HOUR = 60 * MINUTE;
const DISCONNECT_GRACE_MS = 5 * MINUTE;      // Live: seat is held this long for a dropped player
const SLOW_MOVE_MS = 24 * HOUR;              // Take your time: each move (and placement) may take this long
const LOBBY_LIVE_MS = 10 * MINUTE;           // Live lobby closes if nobody joins
const LOBBY_SLOW_MS = 24 * HOUR;             // Take your time lobby
const LOBBY_EXTEND_MS = 10 * MINUTE;         // each Extend tap
const LOBBY_LIVE_MAX_MS = HOUR;              // Live lobby can be extended up to an hour in total
const HOST_GONE_MS = 2 * MINUTE;             // lobby closes this long after the host leaves it
const FINISHED_EMPTY_MS = MINUTE;            // finished rooms are cleared a minute after the last player leaves
const IDLE_CAP_MS = 7 * 24 * HOUR;           // any room nobody touches for a week is cleared
const LIVE_IDLE_MS = 30 * MINUTE;            // Live: nobody places or moves for this long (app left open) ends the game
const GAME_OVER_LINGER_MS = 5 * MINUTE;      // finished games close this long after they end, connected or not
const REMATCH_MS = 15 * 1000;                // players have this long after a game to agree to a rematch
const LOW_CLOCK_MS = 2 * HOUR;               // Take your time: warn the player on move when this much of their day is left


export class Room extends DurableObject<Env> {
  private meta: RoomMeta | null = null;
  private engine: GameEngine | null = null;
  private seats: Record<string, PlayerColor> = {}; // userId → color
  private gameOver: GameOverInfo | null = null;
  private turnStartedAt: number | null = null;     // for timed and Take your time games
  private seq = 0;                                  // number of logged events
  private readonly timeScale: number;               // local tests shrink every timer (ROOM_TIME_SCALE)

  constructor(ctx: DurableObjectState, env: Env) {
    super(ctx, env);
    this.timeScale = Number(env.ROOM_TIME_SCALE) > 0 ? Number(env.ROOM_TIME_SCALE) : 1;
    ctx.blockConcurrencyWhile(async () => {
      const stored = await ctx.storage.get(['meta', 'engine', 'seats', 'gameOver', 'turnStartedAt', 'seq']);
      this.meta = (stored.get('meta') as RoomMeta) ?? null;
      const snap = stored.get('engine') as EngineSnapshot | undefined;
      this.engine = snap ? GameEngine.fromSnapshot(snap) : null;
      this.seats = (stored.get('seats') as Record<string, PlayerColor>) ?? {};
      this.gameOver = (stored.get('gameOver') as GameOverInfo) ?? null;
      this.turnStartedAt = (stored.get('turnStartedAt') as number) ?? null;
      this.seq = (stored.get('seq') as number) ?? 0;
    });
  }

  // ── HTTP entry (from the Worker) ──────────────────────────────────────────────

  async fetch(request: Request): Promise<Response> {
    const url = new URL(request.url);

    if (url.pathname === '/init' && request.method === 'POST') {
      if (this.meta) return Response.json({ error: 'exists' }, { status: 409 });
      const body = await request.json() as { code: string; hostUserId: string; isPublic: boolean; config: Partial<GameConfig>; reserved?: Record<string, PlayerColor>; matched?: boolean };
      const now = Date.now();
      this.engine = new GameEngine(body.code, sanitizeConfig(body.config));
      const lobbyMs = this.engine.config.pace === 'slow' ? this.t(LOBBY_SLOW_MS) : this.t(LOBBY_LIVE_MS);
      this.meta = { code: body.code, hostUserId: body.hostUserId, isPublic: body.isPublic, createdAt: now, expiresAt: now + lobbyMs, lastActivityAt: now, emptySince: null, reserved: body.reserved ?? null, matched: body.matched === true };
      // Robot seats are filled now; the host's device plays them
      for (const color of this.engine.config.aiColors ?? []) {
        const robotId = `ai:${color}`;
        this.engine.addPlayer(robotId, 'Robot', color, 'ai', this.engine.config.aiDifficulty);
        this.engine.setPlayerConnected(color, true);
        this.seats[robotId] = color;
      }
      await this.persist();
      return Response.json({ code: body.code, config: this.engine.config });
    }

    if (url.pathname === '/info') {
      if (!this.meta || !this.engine) return Response.json({ error: 'Room not found' }, { status: 404 });
      const state = this.engine.getState();
      const asker = url.searchParams.get('userId');
      const yourColor = asker ? this.seats[asker] ?? null : null;
      return Response.json({
        yourColor,
        isHost: asker != null && asker === this.meta.hostUserId,
        yourTurn: yourColor != null && state.phase === 'playing' && state.currentTurn === yourColor,
        pace: state.config.pace ?? 'live',
        moveDeadline: this.slowDeadline() ?? this.clockDeadline(),
        lobbyClosesAt: state.phase === 'waiting' ? this.meta.expiresAt ?? null : null,
        code: this.meta.code,
        isPublic: this.meta.isPublic,
        phase: state.phase,
        config: state.config,
        players: state.players.map(p => ({ name: p.username, color: p.color, connected: p.isConnected })),
        openSeats: state.config.playerCount - state.players.length,
        currentTurn: state.currentTurn,
        turnNumber: state.turnNumber,
        lastMove: state.moveHistory[state.moveHistory.length - 1] ?? null,
        // During setup only the asker's own pieces (none without an asker), like the room itself
        pieces: state.board.flat(2).filter(p => p && (state.phase !== 'placement' || p.color === yourColor)).map(p => `${p!.color[0]}${p!.type}@${p!.position.x},${p!.position.y},${p!.position.z}`),
      });
    }

    if (url.pathname === '/connect') {
      if (request.headers.get('Upgrade') !== 'websocket') return new Response('Expected WebSocket', { status: 426 });
      if (!this.meta || !this.engine) return new Response('Room not found', { status: 404 });
      const userId = request.headers.get('X-User-Id')!;
      const name = request.headers.get('X-User-Name') ?? 'Player';
      return this.seatPlayer(userId, name, request.headers.get('X-Reveal') === '1');
    }

    return new Response('Not found', { status: 404 });
  }

  // ── Joining ──────────────────────────────────────────────────────────────────

  private async seatPlayer(userId: string, name: string, reveal = false): Promise<Response> {
    const engine = this.engine!;
    let color = this.seats[userId];
    const rejoining = color !== undefined;

    if (!rejoining) {
      const taken = new Set(Object.values(this.seats));
      const reserved = this.meta!.reserved;
      color = reserved ? reserved[userId] : PLAYER_COLORS_BY_COUNT[engine.config.playerCount].find(c => !taken.has(c))!;
      if (!color || engine.getState().phase !== 'waiting') {
        return new Response('Room is full', { status: 409 });
      }
      engine.addPlayer(userId, name, color);
      this.seats[userId] = color;
      // Remember the game on the player's account, so they can find it after an app restart
      this.ctx.waitUntil(accounts(this.env, 'game-add', { roomCode: this.meta!.code, userIds: [userId] }).catch(() => undefined));
    }

    // Only one live socket per player: a reconnect replaces the old one
    for (const old of this.ctx.getWebSockets(userId)) {
      try { old.close(4000, 'Replaced by a new connection'); } catch { /* already closed */ }
    }

    const pair = new WebSocketPair();
    const [client, server] = [pair[0], pair[1]];
    this.ctx.acceptWebSocket(server, [userId]);
    server.serializeAttachment({ userId, name, color, reveal } satisfies Attachment);
    engine.setPlayerConnected(color, true);
    this.meta!.emptySince = null;

    // Everyone seated: move on to piece placement
    if (engine.getState().phase === 'waiting' && engine.getState().players.length === engine.config.playerCount) {
      engine.startPlacement();
      this.meta!.placementStartedAt = Date.now();
      this.meta!.lastPlayAt = Date.now();
      this.notifyAway(Object.keys(this.seats), { kind: 'started', title: 'Your game is ready', body: 'Everyone has joined. Time to place your pieces!' }, userId);
    } else if (!rejoining) {
      this.notifyAway([this.meta!.hostUserId], { kind: 'joined', title: 'Someone joined your game', body: `${name} joined your game` }, userId);
    }
    await this.persist();

    this.send(server, { type: 'room:joined', roomCode: this.meta!.code, yourColor: color, isHost: userId === this.meta!.hostUserId, state: this.stateForClients({ userId, name, color, reveal }) });
    this.broadcast({ type: rejoining ? 'room:playerReconnected' : 'room:playerJoined', color, name, state: this.stateForClients() }, server);
    return new Response(null, { status: 101, webSocket: client });
  }

  // ── Messages ─────────────────────────────────────────────────────────────────

  async webSocketMessage(ws: WebSocket, raw: string | ArrayBuffer): Promise<void> {
    const me = ws.deserializeAttachment() as Attachment;
    let msg: Record<string, unknown>;
    try {
      msg = JSON.parse(typeof raw === 'string' ? raw : new TextDecoder().decode(raw));
    } catch {
      return this.send(ws, { type: 'error', error: 'Bad JSON' });
    }
    const requestId = msg.requestId;
    const reply = (ok: boolean, error?: string, extra: object = {}) => {
      if (!ok) console.log(`[room ${this.meta?.code}] ${me.color} ${String(msg.type)} refused: ${error} ${JSON.stringify(msg)}`);
      this.send(ws, { type: 'ack', requestId, ok, ...(error ? { error } : {}), ...extra });
    };

    const engine = this.engine;
    if (!engine) return reply(false, 'Room not found');

    // The host's device acts for robot seats: { ..., as: 'Black' }
    const robots = engine.config.aiColors ?? [];
    if (msg.as !== undefined) {
      if (me.userId !== this.meta?.hostUserId || !robots.includes(msg.as as PlayerColor)) return reply(false, 'You can only act for robot seats in your own game');
      me.color = msg.as as PlayerColor;
    }

    switch (msg.type) {
      case 'ping':
        return this.send(ws, { type: 'pong', t: Date.now() });

      case 'room:state':
        return reply(true, undefined, { state: this.stateForClients(me) });

      case 'room:replay': {
        // Resend the game events a reconnecting player missed, in order
        const since = Math.max(0, Number(msg.since) || 0);
        const events = await this.ctx.storage.list<string>({ prefix: 'ev:', start: eventKey(since + 1) });
        for (const text of events.values()) {
          if (!this.mayReceive(me, text)) continue; // other players' setup stays hidden
          try { ws.send(text); } catch { /* socket already gone */ }
        }
        return reply(true, undefined, { seq: this.seq });
      }

      case 'room:extend': {
        // Host keeps the lobby open longer
        const meta = this.meta!;
        if (me.userId !== meta.hostUserId) return reply(false, 'Only the host can keep the room open');
        if (engine.getState().phase !== 'waiting') return reply(false, 'The game has already started');
        const cap = meta.createdAt + (engine.config.pace === 'slow' ? 7 * this.t(LOBBY_SLOW_MS) : this.t(LOBBY_LIVE_MAX_MS));
        meta.expiresAt = Math.min(cap, Math.max(meta.expiresAt ?? Date.now(), Date.now()) + (engine.config.pace === 'slow' ? this.t(LOBBY_SLOW_MS) : this.t(LOBBY_EXTEND_MS)));
        await this.persist();
        reply(true, undefined, { expiresAt: meta.expiresAt });
        return this.broadcast({ type: 'room:extended', expiresAt: meta.expiresAt, state: this.stateForClients() });
      }

      case 'room:rematch': {
        const rematch = this.meta?.rematch;
        if (!this.gameOver || !rematch) return reply(false, 'No rematch on offer');
        if (me.userId.startsWith('ai:') || msg.as !== undefined) return reply(false, 'Robots always say yes');
        if (rematch.code) return reply(true, undefined, { rematch: this.rematchInfo() });
        if (rematch.failed || Date.now() >= rematch.deadline) return reply(false, 'Too late for a rematch');
        rematch.votes[me.userId] = msg.accept === true;
        reply(true);
        return this.settleRematch();
      }

      case 'game:placePiece': {
        const position = asPosition(msg.position);
        if (!position || typeof msg.pieceType !== 'string') return reply(false, 'Bad placement');
        const result = engine.placePiece(me.color, msg.pieceType as PieceType, position);
        if (!result.success) return reply(false, result.error);
        this.meta!.lastPlayAt = Date.now();
        await this.persist();
        reply(true);
        await this.logEvent({ type: 'game:piecePlaced', color: me.color, pieceType: msg.pieceType, position });
        return this.broadcast({ type: 'game:stateUpdate', reason: 'placement', state: this.stateForClients() });
      }

      case 'game:unplacePiece': {
        // Setup: a piece taken back off the board onto its owner's bench
        const position = asPosition(msg.position);
        if (!position) return reply(false, 'Bad position');
        const result = engine.unplacePiece(me.color, position);
        if (!result.success) return reply(false, result.error);
        this.meta!.lastPlayAt = Date.now();
        await this.persist();
        reply(true);
        await this.logEvent({ type: 'game:pieceUnplaced', color: me.color, pieceType: result.pieceType, position });
        return this.broadcast({ type: 'game:stateUpdate', reason: 'placement', state: this.stateForClients() });
      }

      case 'game:ready': {
        if (engine.getState().phase !== 'placement') return reply(false, 'Not in placement phase');
        if (!engine.completePlacement(me.color)) return reply(false, `Place all your pieces first (${engine.piecesLeftToPlace(me.color)} left)`);
        this.meta!.lastPlayAt = Date.now();
        await this.logEvent({ type: 'game:playerReady', color: me.color });
        if (engine.allPlacementsComplete()) {
          // Setup was private: now show everyone where everyone's pieces went
          await this.logEvent({ type: 'game:placementsRevealed', pieces: this.allPieces() });
          engine.startGame();
          this.startTurnClock();
          await this.logEvent({ type: 'game:phaseChanged', phase: 'playing' });
          this.notifyTurn();
        }
        await this.persist();
        reply(true);
        return this.broadcast({ type: 'game:stateUpdate', reason: 'ready', state: this.stateForClients() });
      }

      case 'game:move': {
        const from = asPosition(msg.from), to = asPosition(msg.to);
        if (!from || !to) return reply(false, 'Bad move');
        if (this.chargeClock() || this.slowDeadlinePassed()) return reply(false, 'Out of time');
        const result = engine.executeMove(me.color, from, to);
        if (!result.success) {
          reply(false, result.error);
          return this.send(ws, { type: 'game:moveRejected', from, to, error: result.error });
        }
        reply(true);
        return this.afterResult(result, { type: 'game:moveValidated', color: me.color, from, to });
      }

      case 'game:promotePawn': {
        const position = asPosition(msg.position);
        if (!position) return reply(false, 'Bad position');
        const result = engine.promotePawn(me.color, position, msg.pieceType as PieceType);
        if (!result.success) return reply(false, result.error);
        reply(true);
        return this.afterResult(result, { type: 'game:pawnPromoted', color: me.color, position, pieceType: msg.pieceType });
      }

      case 'game:resign': {
        const result = engine.resign(me.color);
        reply(true);
        return this.afterResult(result, { type: 'game:resigned', color: me.color });
      }

      case 'game:offerDraw': {
        if (engine.getState().phase !== 'playing') return reply(false, 'Game not in progress');
        if (engine.getState().currentTurn !== me.color) return reply(false, 'You can offer a draw on your turn');
        engine.offerDraw(me.color);
        await this.persist();
        reply(true);
        return this.broadcast({ type: 'game:drawOffered', fromColor: me.color }, ws);
      }

      case 'game:respondDraw': {
        if (engine.getState().phase !== 'playing') return reply(false, 'Game not in progress');
        const result = engine.respondDraw(me.color, msg.accept === true);
        if (!result) return reply(false, 'No draw offer to answer');
        reply(true);
        return this.afterResult(result, { type: msg.accept === true ? 'game:drawAccepted' : 'game:drawDeclined', color: me.color });
      }

      default:
        return reply(false, `Unknown message type: ${String(msg.type)}`);
    }
  }

  /** Persist and broadcast the outcome of any rule-engine action */
  /**
   * Push a notification to players who aren't in the room right now (robots and the player
   * who caused it are skipped). Sent through the Accounts object, which knows their devices
   * and notification settings.
   */
  private notifyAway(userIds: string[], message: Omit<PushMessage, 'roomCode'>, except?: string): void {
    if (!this.meta) return;
    const away = userIds.filter(id => id !== except && !id.startsWith('ai:') && this.ctx.getWebSockets(id).length === 0);
    if (away.length === 0) return;
    this.ctx.waitUntil(accounts(this.env, 'notify', { userIds: away, message: { ...message, roomCode: this.meta.code } }).catch(() => undefined));
  }

  /** "Your move" for the player whose turn it is, if they're away */
  private notifyTurn(): void {
    const state = this.engine?.getState();
    if (!state || state.phase !== 'playing' || !state.currentTurn) return;
    const mover = Object.keys(this.seats).find(id => this.seats[id] === state.currentTurn);
    if (!mover) return;
    const others = state.players.filter(p => p.color !== state.currentTurn && !p.isEliminated).map(p => p.username);
    const vs = others.length === 1 ? ` against ${others[0]}` : '';
    this.notifyAway([mover], { kind: 'turn', title: 'Your move', body: `It's your turn${vs}` });
  }

  private async afterResult(result: MoveResult, event: Record<string, unknown>, walkout?: PlayerColor): Promise<void> {
    if (this.meta) {
      this.meta.lastPlayAt = Date.now();
      if (result.eliminatedPlayer && !(this.meta.eliminated ??= []).includes(result.eliminatedPlayer)) this.meta.eliminated.push(result.eliminatedPlayer);
      if (walkout) this.markWalkout(walkout);
    }
    if (result.gameOver) {
      this.gameOver = result.gameOver;
      this.turnStartedAt = null;
      if (this.meta) this.meta.gameOverAt = Date.now();
      this.offerRematch();
      this.forgetRoom();
    } else {
      this.startTurnClock();
    }
    await this.persist();
    await this.logEvent(event);
    if (result.chaosRotation) await this.logEvent({ type: 'game:chaosRotation', rotation: result.chaosRotation });
    if (result.requiresPromotion) this.broadcast({ type: 'game:pawnPromotion', ...result.requiresPromotion });
    if (result.eliminatedPlayer && !result.gameOver) {
      await this.logEvent({ type: 'game:playerEliminated', color: result.eliminatedPlayer, conqueredBy: result.conqueredBy ?? null });
    }
    if (result.gameOver) await this.logEvent({ type: 'game:over', ...result.gameOver });
    else this.notifyTurn();
    this.broadcast({ type: 'game:stateUpdate', reason: event.type, state: this.stateForClients() });
    if (result.gameOver) this.ctx.waitUntil(this.sendResult());
  }

  // ── Disconnects, timers and cleanup ───────────────────────────────────────────

  async webSocketClose(ws: WebSocket): Promise<void> {
    await this.dropped(ws);
  }

  async webSocketError(ws: WebSocket): Promise<void> {
    await this.dropped(ws);
  }

  private async dropped(ws: WebSocket): Promise<void> {
    const me = ws.deserializeAttachment() as Attachment | null;
    if (!me || !this.engine) return;
    // A replaced socket closing must not mark the player offline
    if (this.ctx.getWebSockets(me.userId).some(other => other !== ws)) return;
    this.engine.setPlayerConnected(me.color, false);
    if (this.ctx.getWebSockets().every(other => other === ws)) this.meta!.emptySince = Date.now();
    await this.persist();
    const graceMs = this.engine.config.pace === 'slow' ? null : this.t(DISCONNECT_GRACE_MS);
    this.broadcast({ type: 'room:playerLeft', color: me.color, name: me.name, graceMs, pace: this.engine.config.pace, state: this.stateForClients() }, ws);
    // Leaving the result screen counts as not wanting a rematch
    if (this.meta?.rematch && !this.meta.rematch.code && !this.meta.rematch.failed) await this.settleRematch(ws);
  }

  // ── Results and ratings ───────────────────────────────────────────────────────

  private markWalkout(color: PlayerColor): void {
    const userId = Object.keys(this.seats).find(id => this.seats[id] === color);
    if (!userId || userId.startsWith('ai:') || !this.meta) return;
    if (!(this.meta.walkouts ??= []).includes(userId)) this.meta.walkouts.push(userId);
  }

  /**
   * Finishing places: the winner, then anyone still in, then those knocked out, latest first.
   * With no winner (a draw) everyone still in shares first place.
   */
  private finishingPlaces(): Record<string, number> {
    const state = this.engine!.getState();
    const winner = this.gameOver?.winner ?? null;
    const out = this.meta?.eliminated ?? [];
    const groups: PlayerColor[][] = [];
    if (winner) groups.push([winner]);
    groups.push(state.players.map(p => p.color).filter(c => c !== winner && !out.includes(c)));
    for (const c of [...out].reverse()) if (c !== winner) groups.push([c]);
    const places: Record<string, number> = {};
    let place = 1;
    for (const g of groups) {
      if (g.length === 0) continue;
      for (const c of g) places[c] = place;
      place += g.length;
    }
    return places;
  }

  /** Tell the accounts store how the game went, then show everyone their rating change */
  private async sendResult(): Promise<void> {
    const meta = this.meta, engine = this.engine;
    if (!meta || !engine || !this.gameOver || meta.resultSent) return;
    meta.resultSent = true;
    await this.ctx.storage.put('meta', meta);
    const state = engine.getState();
    const places = this.finishingPlaces();
    const walkouts = meta.walkouts ?? [];
    const result: GameResult = {
      roomCode: meta.code,
      rated: this.gameOver.reason !== 'abandoned' && this.isRanked(),
      players: Object.entries(this.seats).filter(([id]) => !id.startsWith('ai:')).map(([userId, color]) => {
        const mine = state.moveHistory.filter(m => m.isCapture && m.playerColor === color);
        const takenTypes: Record<string, number> = {};
        for (const m of mine) if (m.capturedPieceType) takenTypes[m.capturedPieceType] = (takenTypes[m.capturedPieceType] ?? 0) + 1;
        return {
          userId, color, place: places[color] ?? state.players.length, walkout: walkouts.includes(userId),
          taken: mine.length,
          lost: state.moveHistory.filter(m => m.isCapture && m.capturedColor === color).length,
          takenTypes,
        };
      }),
      boardSize: engine.config.boardSize,
      playerCount: engine.config.playerCount,
      pace: engine.config.pace ?? 'live',
      endReason: this.gameOver.reason,
      moves: state.moveHistory.length,
      durationMs: Date.now() - (meta.placementStartedAt ?? meta.createdAt),
    };
    try {
      const { status, data } = await accounts(this.env, 'game-result', { result });
      if (status !== 200 || !this.meta) return;
      const byUser = (data.changes ?? {}) as Record<string, RatingChange>;
      const ratings: Record<string, RatingChange> = {};
      for (const [userId, change] of Object.entries(byUser)) if (this.seats[userId]) ratings[this.seats[userId]] = change;
      if (Object.keys(ratings).length === 0) return;
      this.meta.ratings = ratings;
      await this.ctx.storage.put('meta', this.meta);
      this.broadcast({ type: 'game:rated', ratings });
    } catch (e) {
      console.log(`[room ${meta.code}] result not recorded: ${String(e)}`);
    }
  }

  // ── Rematch ──────────────────────────────────────────────────────────────────

  private offerRematch(): void {
    if (!this.meta || this.meta.rematch) return;
    this.meta.rematch = { deadline: Date.now() + this.t(REMATCH_MS), votes: {}, code: null, failed: false };
  }

  /** Players still on the result screen who haven't said no */
  private rematchCandidates(leaving?: WebSocket): string[] {
    const votes = this.meta?.rematch?.votes ?? {};
    return Object.keys(this.seats).filter(id => !id.startsWith('ai:') && votes[id] !== false
      && this.ctx.getWebSockets(id).some(ws => ws !== leaving));
  }

  /**
   * Start the rematch once everyone still here has said yes (at least two players), or call it
   * off once that can't happen. Then tell everyone where things stand.
   */
  private async settleRematch(leaving?: WebSocket): Promise<void> {
    const rematch = this.meta?.rematch;
    const engine = this.engine;
    if (!rematch || !engine || rematch.code || rematch.failed) return;
    const players = this.rematchCandidates(leaving);
    if (players.length < 2) rematch.failed = true;
    else if (players.every(id => rematch.votes[id] === true)) {
      rematch.seats = this.rematchSeats(players);
      const robots = PLAYER_COLORS_BY_COUNT[engine.config.playerCount].filter(c => !Object.values(rematch.seats!).includes(c));
      const host = Object.keys(rematch.seats).find(id => rematch.seats![id] === 'White')!;
      try {
        const room = await createRoom(this.env, host, { ...engine.config, aiColors: robots }, false, rematch.seats);
        rematch.code = room.code;
      } catch (e) {
        console.log(`[room ${this.meta?.code}] rematch room failed: ${String(e)}`);
        rematch.failed = true;
      }
    }
    await this.persist();
    this.broadcast({ type: 'room:rematch', rematch: this.rematchInfo() }, leaving);
  }

  /**
   * Colours for the new game. Robots keep their seats, and the human seats pass along one
   * place, so a different player is White (hosts and moves first). A player who left is
   * replaced by a robot.
   */
  private rematchSeats(players: string[]): Record<string, PlayerColor> {
    const colors = PLAYER_COLORS_BY_COUNT[this.engine!.config.playerCount];
    const robots = this.engine!.config.aiColors ?? [];
    const order = [...players].sort((a, b) => colors.indexOf(this.seats[a]) - colors.indexOf(this.seats[b]));
    const humanSeats = colors.filter(c => !robots.includes(c)).slice(0, order.length); // always starts with White
    const seats: Record<string, PlayerColor> = {};
    order.forEach((id, i) => { seats[id] = humanSeats[(i - 1 + order.length) % order.length]; });
    return seats;
  }

  /** What the result screen shows: who's in, who's out, the countdown, and the new room */
  private rematchInfo() {
    const rematch = this.meta?.rematch;
    if (!rematch) return null;
    const colorsOf = (ids: string[]) => ids.map(id => this.seats[id]).filter(Boolean);
    const humans = Object.keys(this.seats).filter(id => !id.startsWith('ai:'));
    return {
      deadline: rematch.deadline,
      code: rematch.code,
      failed: rematch.failed,
      accepted: colorsOf(humans.filter(id => rematch.votes[id] === true)),
      declined: colorsOf(humans.filter(id => rematch.votes[id] === false)),
      present: colorsOf(humans.filter(id => this.ctx.getWebSockets(id).length > 0)),
      newColors: rematch.seats ? Object.fromEntries(Object.entries(rematch.seats).map(([id, c]) => [this.seats[id], c])) : null,
    };
  }

  async alarm(): Promise<void> {
    const engine = this.engine;
    const meta = this.meta;
    if (!engine || !meta) return;
    const now = Date.now();
    const slow = engine.config.pace === 'slow';

    // Lobby: nobody joined in time, or the host walked away
    if (engine.getState().phase === 'waiting') {
      const host = engine.getState().players.find(p => p.userId === meta.hostUserId);
      if (now >= (meta.expiresAt ?? meta.createdAt + this.t(LOBBY_LIVE_MS))) return this.closeRoom('expired');
      if (host && !host.isConnected && host.disconnectedAt && now - host.disconnectedAt >= this.t(HOST_GONE_MS)) return this.closeRoom('host_left');
    }

    // Placement that never finished: a Live player who never came back, or a day gone by
    if (engine.getState().phase === 'placement') {
      const stalled = slow
        ? meta.placementStartedAt != null && now - meta.placementStartedAt >= this.t(SLOW_MOVE_MS)
        : engine.getState().players.some(p => p.type !== 'ai' && !p.isConnected && p.disconnectedAt != null && now - p.disconnectedAt >= this.t(DISCONNECT_GRACE_MS));
      if (stalled) {
        // Whoever held up setup walked out: a Live player who never came back, or anyone not ready after a day
        for (const p of engine.getState().players) {
          if (p.type === 'ai') continue;
          if (slow ? !p.placementComplete : (!p.isConnected && p.disconnectedAt != null && now - p.disconnectedAt >= this.t(DISCONNECT_GRACE_MS))) this.markWalkout(p.color);
        }
        await this.endAbandoned();
      }
    }

    // Timed games: the player on move ran out of time
    if (this.chargeClock()) {
      const loser = engine.getState().currentTurn!;
      const result = engine.forfeitByTimeout(loser);
      await this.afterResult(result, { type: 'game:timeout', color: loser });
    }

    // Take your time: a nudge when the player on move is down to their last couple of hours
    this.warnLowClock(now);

    // Take your time: a player who let a day pass without moving is out
    if (this.slowDeadlinePassed()) {
      const loser = engine.getState().currentTurn!;
      const result = engine.resign(loser);
      if (result.gameOver) result.gameOver.reason = 'timeout';
      await this.afterResult(result, { type: 'game:timeout', color: loser }, loser);
    }

    // Live: seats held too long for players who never came back
    const state = engine.getState();
    if (state.phase === 'playing' && !slow) {
      for (const p of state.players) {
        if (p.type !== 'ai' && !p.isConnected && !p.isEliminated && p.disconnectedAt && now - p.disconnectedAt >= this.t(DISCONNECT_GRACE_MS)) {
          if (engine.getState().phase !== 'playing') break;
          const result = engine.resign(p.color);
          await this.afterResult(result, { type: 'game:abandoned', color: p.color }, p.color);
        }
      }
    }

    // Live: everyone stayed connected but nobody placed or moved for a long time (app left open)
    const idleState = engine.getState();
    if (!slow && !this.gameOver && (idleState.phase === 'placement' || idleState.phase === 'playing')
        && now - (meta.lastPlayAt ?? meta.createdAt) >= this.t(LIVE_IDLE_MS)) {
      const mover = idleState.players.find(p => p.color === idleState.currentTurn);
      if (idleState.phase === 'playing' && mover && mover.type !== 'ai' && !mover.isEliminated) {
        const result = engine.forfeitByTimeout(mover.color);
        await this.afterResult(result, { type: 'game:timeout', color: mover.color });
      } else {
        await this.endAbandoned();
      }
    }

    // Only robots left in the game: nobody is playing, so end it
    const live = engine.getState();
    if (live.phase === 'playing' && !live.players.some(p => p.type !== 'ai' && !p.isEliminated)) await this.endAbandoned();

    // Rematch countdown ran out
    if (meta.rematch && !meta.rematch.code && !meta.rematch.failed && now >= meta.rematch.deadline) {
      meta.rematch.failed = true;
      await this.ctx.storage.put('meta', meta);
      this.broadcast({ type: 'room:rematch', rematch: this.rematchInfo() });
    }

    // Rooms that finished before gameOverAt existed: start their closing time now
    if (this.gameOver && meta.gameOverAt == null) {
      meta.gameOverAt = now;
      await this.ctx.storage.put('meta', meta);
    }

    // A finished game closes a few minutes after it ends, even if players sit on the result screen
    if (this.gameOver && meta.gameOverAt != null && now - meta.gameOverAt >= this.t(GAME_OVER_LINGER_MS)) {
      return this.closeRoom('finished');
    }

    // Clear finished rooms shortly after everyone leaves, and any room left idle for a week
    if (this.ctx.getWebSockets().length === 0) {
      // Rooms from before emptySince existed: count them as empty from now
      if (meta.emptySince == null) {
        meta.emptySince = now;
        await this.ctx.storage.put('meta', meta);
      }
      const finishedAndEmpty = this.gameOver && meta.emptySince != null && now - meta.emptySince >= this.t(FINISHED_EMPTY_MS);
      const idle = now - (meta.lastActivityAt ?? meta.createdAt) >= this.t(IDLE_CAP_MS);
      if (finishedAndEmpty || idle) {
        this.forgetRoom();
        await this.ctx.storage.deleteAll();
        this.meta = null;
        this.engine = null;
        return;
      }
    }
    await this.scheduleAlarm();
  }

  /** The game can't go on: end it with no winner */
  private async endAbandoned(): Promise<void> {
    if (!this.engine || this.gameOver) return;
    this.engine.abandon();
    this.gameOver = { winner: null, reason: 'abandoned' as EndReason };
    this.turnStartedAt = null;
    this.meta!.gameOverAt = Date.now();
    this.offerRematch();
    this.forgetRoom();
    await this.persist();
    await this.logEvent({ type: 'game:over', ...this.gameOver });
    this.ctx.waitUntil(this.sendResult());
  }

  /** Take this room off its players' active games lists */
  private forgetRoom(): void {
    if (!this.meta) return;
    this.ctx.waitUntil(accounts(this.env, 'game-remove', { roomCode: this.meta.code }).catch(() => undefined));
  }

  /** Close a lobby that never started: tell anyone still in it, then forget the room */
  private async closeRoom(reason: 'expired' | 'host_left' | 'finished'): Promise<void> {
    this.forgetRoom();
    this.broadcast({ type: 'room:closed', reason });
    const why = reason === 'expired' ? 'Room closed: nobody joined' : reason === 'host_left' ? 'Room closed: the host left' : 'Room closed: game over';
    for (const ws of this.ctx.getWebSockets()) {
      try { ws.close(4001, why); } catch { /* already closed */ }
    }
    await this.ctx.storage.deleteAll();
    this.meta = null;
    this.engine = null;
  }

  /** The next time something in this room needs to happen. Only future times: past ones would fire every second. */
  private nextDeadline(): number {
    const engine = this.engine!;
    const meta = this.meta!;
    const now = Date.now();
    const state = engine.getState();
    const slow = engine.config.pace === 'slow';
    const times = [(meta.lastActivityAt ?? meta.createdAt) + this.t(IDLE_CAP_MS)];

    if (state.phase === 'waiting') {
      times.push(meta.expiresAt ?? meta.createdAt + this.t(LOBBY_LIVE_MS));
      const host = state.players.find(p => p.userId === meta.hostUserId);
      if (host && !host.isConnected && host.disconnectedAt) times.push(host.disconnectedAt + this.t(HOST_GONE_MS));
    }
    if (state.phase === 'placement' && slow && meta.placementStartedAt != null) times.push(meta.placementStartedAt + this.t(SLOW_MOVE_MS));
    if ((state.phase === 'placement' || state.phase === 'playing') && !slow) {
      for (const p of state.players) {
        if (!p.isConnected && !p.isEliminated && p.type !== 'ai' && p.disconnectedAt) times.push(p.disconnectedAt + this.t(DISCONNECT_GRACE_MS));
      }
    }
    const clock = this.clockDeadline();
    if (clock) times.push(clock);
    const move = this.slowDeadline();
    if (move) {
      times.push(move);
      if (meta.lowWarnedTurn !== this.turnStartedAt) times.push(move - this.t(LOW_CLOCK_MS));
    }
    if (this.gameOver && meta.emptySince != null) times.push(meta.emptySince + this.t(FINISHED_EMPTY_MS));
    if (this.gameOver && meta.gameOverAt != null) times.push(meta.gameOverAt + this.t(GAME_OVER_LINGER_MS));
    if (meta.rematch && !meta.rematch.code && !meta.rematch.failed) times.push(meta.rematch.deadline);
    if (this.gameOver && meta.emptySince == null && this.ctx.getWebSockets().length === 0) times.push(now + this.t(FINISHED_EMPTY_MS));
    if (!slow && !this.gameOver && (state.phase === 'placement' || state.phase === 'playing')) {
      times.push((meta.lastPlayAt ?? meta.createdAt) + this.t(LIVE_IDLE_MS));
    }

    const future = times.filter(t => t > now);
    return future.length ? Math.min(...future) : now + this.t(IDLE_CAP_MS);
  }

  private async scheduleAlarm(): Promise<void> {
    if (!this.engine || !this.meta) return;
    await this.ctx.storage.setAlarm(Math.max(Date.now() + 1000, this.nextDeadline()));
  }

  // ── Chess clock (timed games) ─────────────────────────────────────────────────

  private startTurnClock(): void {
    const engine = this.engine;
    const clocked = engine && (engine.config.timedMode || engine.config.pace === 'slow');
    this.turnStartedAt = clocked && engine!.getState().phase === 'playing' ? Date.now() : null;
  }

  /** Charge elapsed time to the player on move; true if they have run out */
  private chargeClock(): boolean {
    const engine = this.engine;
    if (!engine || !engine.config.timedMode || this.turnStartedAt === null) return false;
    const now = Date.now();
    const { timedOut } = engine.tickTimer(now - this.turnStartedAt);
    this.turnStartedAt = now;
    return timedOut !== null;
  }

  private clockDeadline(): number | null {
    if (!this.engine || this.turnStartedAt === null) return null;
    const state = this.engine.getState();
    const mover = state.players.find(p => p.color === state.currentTurn);
    return mover?.remainingTimeMs != null ? this.turnStartedAt + mover.remainingTimeMs : null;
  }

  /** Take your time: when the player on move must have moved by. Robot turns wait for the host, so they have none. */
  private slowDeadline(): number | null {
    const engine = this.engine;
    if (!engine || engine.config.pace !== 'slow' || this.turnStartedAt === null) return null;
    const state = engine.getState();
    if (state.phase !== 'playing') return null;
    const mover = state.players.find(p => p.color === state.currentTurn);
    if (!mover || mover.type === 'ai') return null;
    return this.turnStartedAt + this.t(SLOW_MOVE_MS);
  }

  /** Push "your clock is running low" once per turn, if the player on move is away */
  private warnLowClock(now: number): void {
    const deadline = this.slowDeadline();
    const meta = this.meta, state = this.engine?.getState();
    if (!deadline || !meta || !state || meta.lowWarnedTurn === this.turnStartedAt) return;
    if (now < deadline - this.t(LOW_CLOCK_MS) || now >= deadline) return;
    meta.lowWarnedTurn = this.turnStartedAt;
    this.ctx.waitUntil(this.ctx.storage.put('meta', meta));
    const mover = Object.keys(this.seats).find(id => this.seats[id] === state.currentTurn);
    if (!mover) return;
    const hours = Math.max(1, Math.round((deadline - now) / HOUR));
    this.notifyAway([mover], { kind: 'turn', title: 'Your clock is running low', body: `About ${hours} hour${hours === 1 ? '' : 's'} left to make your move, or you'll lose this game` });
  }

  private slowDeadlinePassed(): boolean {
    const deadline = this.slowDeadline();
    return deadline !== null && Date.now() >= deadline;
  }

  // ── Helpers ──────────────────────────────────────────────────────────────────

  private t(ms: number): number {
    return ms * this.timeScale;
  }

  /** Broadcast a game event and keep it so a player who missed it can catch up */
  private async logEvent(message: Record<string, unknown>): Promise<void> {
    this.seq++;
    const text = JSON.stringify({ ...message, seq: this.seq });
    await this.ctx.storage.put({ [eventKey(this.seq)]: text, seq: this.seq });
    for (const ws of this.ctx.getWebSockets()) {
      if (!this.mayReceive(ws.deserializeAttachment() as Attachment | null, text)) continue;
      try { ws.send(text); } catch { /* socket already gone */ }
    }
  }

  /** Ranked: a Find Match game (random opponents, so no rating farming between friends) on a clock without Chaos */
  private isRanked(): boolean {
    return this.meta?.matched === true && this.engine != null && isRankedConfig(this.engine.config);
  }

  // ── Private setup ────────────────────────────────────────────────────────────
  // While pieces are being placed nobody sees anyone else's: placements go only to the player
  // who made them (and the host, for the robot seats their device plays). When everyone is
  // ready, game:placementsRevealed shows the whole board.

  /** The colours whose setup this player may see: their own, and the robots' if they host */
  private ownColors(viewer: Attachment | null): PlayerColor[] {
    if (!viewer) return [];
    // An older app sees everything as before, but never in a ranked game, where a client could leave
    // reveal off just to look at the opponent's setup
    if (!viewer.reveal && !this.meta?.matched) return [...PLAYER_COLORS_BY_COUNT[6]];
    const robots = viewer.userId === this.meta?.hostUserId ? (this.engine?.config.aiColors ?? []) : [];
    return [viewer.color, ...robots];
  }

  /** Placement events (piece placed or taken back) only reach their own side */
  private mayReceive(viewer: Attachment | null, text: string): boolean {
    if (!text.includes('"game:piecePlaced"') && !text.includes('"game:pieceUnplaced"')) return true;
    try {
      const event = JSON.parse(text) as { type: string; color: PlayerColor };
      if (event.type !== 'game:piecePlaced' && event.type !== 'game:pieceUnplaced') return true;
      return this.ownColors(viewer).includes(event.color);
    } catch { return true; }
  }

  /** Every piece on the board, for the reveal */
  private allPieces(): { color: PlayerColor; pieceType: string; position: BoardPosition }[] {
    const pieces: { color: PlayerColor; pieceType: string; position: BoardPosition }[] = [];
    for (const plane of this.engine!.getState().board)
      for (const row of plane)
        for (const cell of row)
          if (cell) pieces.push({ color: cell.color, pieceType: cell.type, position: cell.position });
    return pieces;
  }

  /** The room state; during setup, other players' pieces are left off the board for the viewer */
  private stateForClients(viewer?: Attachment | null) {
    const raw = this.engine!.getState();
    let state = raw;
    if (raw.phase === 'placement' && viewer !== undefined) {
      const mine = this.ownColors(viewer);
      state = { ...raw, board: raw.board.map(plane => plane.map(row => row.map(cell => cell && mine.includes(cell.color) ? cell : null))) };
    }
    return {
      ...state,
      ranked: this.isRanked(),
      winner: this.gameOver?.winner ?? null,
      endReason: this.gameOver?.reason ?? null,
      drawOfferFrom: this.engine!.drawOfferFrom,
      seq: this.seq,
      lobbyClosesAt: state.phase === 'waiting' ? this.meta?.expiresAt ?? null : null,
      moveDeadline: this.slowDeadline() ?? this.clockDeadline(),
      rematch: this.rematchInfo(),
      ratings: this.meta?.ratings ?? null,
    };
  }

  private async persist(): Promise<void> {
    if (this.meta) this.meta.lastActivityAt = Date.now();
    await this.ctx.storage.put({
      meta: this.meta,
      engine: this.engine?.toSnapshot() ?? null,
      seats: this.seats,
      gameOver: this.gameOver,
      turnStartedAt: this.turnStartedAt,
    });
    await this.scheduleAlarm();
  }

  private send(ws: WebSocket, message: object): void {
    try { ws.send(JSON.stringify(message)); } catch { /* socket already gone */ }
  }

  private broadcast(message: object, except?: WebSocket): void {
    const text = JSON.stringify(message);
    // During setup a message carrying the room state is redone for each player (private setup)
    const perViewer = 'state' in message && this.engine?.getState().phase === 'placement';
    for (const ws of this.ctx.getWebSockets()) {
      if (ws === except) continue;
      const own = perViewer ? JSON.stringify({ ...message, state: this.stateForClients(ws.deserializeAttachment() as Attachment | null) }) : text;
      try { ws.send(own); } catch { /* socket already gone */ }
    }
  }
}

function eventKey(seq: number): string {
  return 'ev:' + String(seq).padStart(8, '0');
}

/** Only accept known settings, within the game's limits */
/**
 * The settings a ranked game needs: a clock (Speed round, or Take your time's day per move) and
 * no Chaos Mode. Only Find Match games are ranked at all (RoomMeta.matched); everything else is
 * still played and kept in win/loss records, just unranked.
 */
export function isRankedConfig(config: Partial<GameConfig>): boolean {
  return (config.timedMode === true || config.pace === 'slow') && config.chaosMode !== true;
}

export function sanitizeConfig(input: Partial<GameConfig> = {}): Partial<GameConfig> {
  const config: Partial<GameConfig> = {};
  if (input.boardSize === '4x4x4' || input.boardSize === '5x5x5' || input.boardSize === '6x6x6' || input.boardSize === '8x8x8') config.boardSize = input.boardSize;
  if ([2, 4, 6].includes(Number(input.playerCount))) config.playerCount = Number(input.playerCount);
  if (typeof input.chaosMode === 'boolean') config.chaosMode = input.chaosMode;
  if (Number.isInteger(input.chaosInterval)) config.chaosInterval = Math.min(25, Math.max(3, Number(input.chaosInterval)));
  if (typeof input.timedMode === 'boolean') config.timedMode = input.timedMode;
  if (Number.isFinite(input.timePerPlayerSeconds)) config.timePerPlayerSeconds = Math.min(1800, Math.max(60, Number(input.timePerPlayerSeconds)));
  // Board sizes the player count needs (matches the game's setup screen)
  if (config.playerCount === 4 && (config.boardSize === '4x4x4' || config.boardSize === '5x5x5')) config.boardSize = '6x6x6';
  if (config.playerCount === 6) config.boardSize = '8x8x8';
  if (['easy', 'medium', 'hard'].includes(input.aiDifficulty as string)) config.aiDifficulty = input.aiDifficulty;
  if (input.pace === 'live' || input.pace === 'slow') config.pace = input.pace;
  if (config.pace === 'slow') config.timedMode = false; // the 24 hour move window is the clock
  // Robot seats: any seats but White (the host), and at least one seat left for a human opponent
  if (Array.isArray(input.aiColors)) {
    const seats = PLAYER_COLORS_BY_COUNT[config.playerCount ?? 2];
    const robots = seats.filter(c => c !== 'White' && input.aiColors!.includes(c));
    if (robots.length < seats.length - 1) config.aiColors = robots;
  }
  return config;
}
