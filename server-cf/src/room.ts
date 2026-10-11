import { DurableObject } from 'cloudflare:workers';
import { GameEngine, type EngineSnapshot, type MoveResult } from './game/GameEngine.js';
import {
  PLAYER_COLORS_BY_COUNT,
  type BoardPosition, type EndReason, type GameConfig, type PieceType, type PlayerColor,
} from './types/game.js';
import type { Env } from './index.js';

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
  createdAt: number;
  /** Lobby closes at this time if the game hasn't started (the host can extend it) */
  expiresAt?: number;
  /** Last time anyone did anything, for the idle cap */
  lastActivityAt?: number;
  /** When the last socket closed, for clearing finished rooms */
  emptySince?: number | null;
  placementStartedAt?: number | null;
}

interface Attachment {
  userId: string;
  name: string;
  color: PlayerColor;
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
      const body = await request.json() as { code: string; hostUserId: string; isPublic: boolean; config: Partial<GameConfig> };
      const now = Date.now();
      this.engine = new GameEngine(body.code, sanitizeConfig(body.config));
      const lobbyMs = this.engine.config.pace === 'slow' ? this.t(LOBBY_SLOW_MS) : this.t(LOBBY_LIVE_MS);
      this.meta = { code: body.code, hostUserId: body.hostUserId, isPublic: body.isPublic, createdAt: now, expiresAt: now + lobbyMs, lastActivityAt: now, emptySince: null };
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
      return Response.json({
        code: this.meta.code,
        isPublic: this.meta.isPublic,
        phase: state.phase,
        config: state.config,
        players: state.players.map(p => ({ name: p.username, color: p.color, connected: p.isConnected })),
        openSeats: state.config.playerCount - state.players.length,
        currentTurn: state.currentTurn,
        turnNumber: state.turnNumber,
        lastMove: state.moveHistory[state.moveHistory.length - 1] ?? null,
        pieces: state.board.flat(2).filter(Boolean).map(p => `${p!.color[0]}${p!.type}@${p!.position.x},${p!.position.y},${p!.position.z}`),
      });
    }

    if (url.pathname === '/connect') {
      if (request.headers.get('Upgrade') !== 'websocket') return new Response('Expected WebSocket', { status: 426 });
      if (!this.meta || !this.engine) return new Response('Room not found', { status: 404 });
      const userId = request.headers.get('X-User-Id')!;
      const name = request.headers.get('X-User-Name') ?? 'Player';
      return this.seatPlayer(userId, name);
    }

    return new Response('Not found', { status: 404 });
  }

  // ── Joining ──────────────────────────────────────────────────────────────────

  private async seatPlayer(userId: string, name: string): Promise<Response> {
    const engine = this.engine!;
    let color = this.seats[userId];
    const rejoining = color !== undefined;

    if (!rejoining) {
      const taken = new Set(Object.values(this.seats));
      color = PLAYER_COLORS_BY_COUNT[engine.config.playerCount].find(c => !taken.has(c))!;
      if (!color || engine.getState().phase !== 'waiting') {
        return new Response('Room is full', { status: 409 });
      }
      engine.addPlayer(userId, name, color);
      this.seats[userId] = color;
    }

    // Only one live socket per player: a reconnect replaces the old one
    for (const old of this.ctx.getWebSockets(userId)) {
      try { old.close(4000, 'Replaced by a new connection'); } catch { /* already closed */ }
    }

    const pair = new WebSocketPair();
    const [client, server] = [pair[0], pair[1]];
    this.ctx.acceptWebSocket(server, [userId]);
    server.serializeAttachment({ userId, name, color } satisfies Attachment);
    engine.setPlayerConnected(color, true);
    this.meta!.emptySince = null;

    // Everyone seated: move on to piece placement
    if (engine.getState().phase === 'waiting' && engine.getState().players.length === engine.config.playerCount) {
      engine.startPlacement();
      this.meta!.placementStartedAt = Date.now();
    }
    await this.persist();

    this.send(server, { type: 'room:joined', roomCode: this.meta!.code, yourColor: color, isHost: userId === this.meta!.hostUserId, state: this.stateForClients() });
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
        return reply(true, undefined, { state: this.stateForClients() });

      case 'room:replay': {
        // Resend the game events a reconnecting player missed, in order
        const since = Math.max(0, Number(msg.since) || 0);
        const events = await this.ctx.storage.list<string>({ prefix: 'ev:', start: eventKey(since + 1) });
        for (const text of events.values()) {
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

      case 'game:placePiece': {
        const result = engine.placePiece(me.color, msg.pieceType as PieceType, msg.position as BoardPosition);
        if (!result.success) return reply(false, result.error);
        await this.persist();
        reply(true);
        await this.logEvent({ type: 'game:piecePlaced', color: me.color, pieceType: msg.pieceType, position: msg.position });
        return this.broadcast({ type: 'game:stateUpdate', reason: 'placement', state: this.stateForClients() });
      }

      case 'game:ready': {
        if (engine.getState().phase !== 'placement') return reply(false, 'Not in placement phase');
        engine.completePlacement(me.color);
        await this.logEvent({ type: 'game:playerReady', color: me.color });
        if (engine.allPlacementsComplete()) {
          engine.startGame();
          this.startTurnClock();
          await this.logEvent({ type: 'game:phaseChanged', phase: 'playing' });
        }
        await this.persist();
        reply(true);
        return this.broadcast({ type: 'game:stateUpdate', reason: 'ready', state: this.stateForClients() });
      }

      case 'game:move': {
        if (this.chargeClock() || this.slowDeadlinePassed()) return reply(false, 'Out of time');
        const result = engine.executeMove(me.color, msg.from as BoardPosition, msg.to as BoardPosition);
        if (!result.success) {
          reply(false, result.error);
          return this.send(ws, { type: 'game:moveRejected', from: msg.from, to: msg.to, error: result.error });
        }
        reply(true);
        return this.afterResult(result, { type: 'game:moveValidated', color: me.color, from: msg.from, to: msg.to });
      }

      case 'game:promotePawn': {
        const result = engine.promotePawn(me.color, msg.position as BoardPosition, msg.pieceType as PieceType);
        if (!result.success) return reply(false, result.error);
        reply(true);
        return this.afterResult(result, { type: 'game:pawnPromoted', color: me.color, position: msg.position, pieceType: msg.pieceType });
      }

      case 'game:resign': {
        const result = engine.resign(me.color);
        reply(true);
        return this.afterResult(result, { type: 'game:resigned', color: me.color });
      }

      case 'game:offerDraw': {
        if (engine.getState().phase !== 'playing') return reply(false, 'Game not in progress');
        engine.offerDraw(me.color);
        await this.persist();
        reply(true);
        return this.broadcast({ type: 'game:drawOffered', fromColor: me.color }, ws);
      }

      case 'game:respondDraw': {
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
  private async afterResult(result: MoveResult, event: Record<string, unknown>): Promise<void> {
    if (result.gameOver) {
      this.gameOver = result.gameOver;
      this.turnStartedAt = null;
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
    this.broadcast({ type: 'game:stateUpdate', reason: event.type, state: this.stateForClients() });
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
      if (stalled) await this.endAbandoned();
    }

    // Timed games: the player on move ran out of time
    if (this.chargeClock()) {
      const loser = engine.getState().currentTurn!;
      const result = engine.forfeitByTimeout(loser);
      await this.afterResult(result, { type: 'game:timeout', color: loser });
    }

    // Take your time: a player who let a day pass without moving is out
    if (this.slowDeadlinePassed()) {
      const loser = engine.getState().currentTurn!;
      const result = engine.resign(loser);
      if (result.gameOver) result.gameOver.reason = 'timeout';
      await this.afterResult(result, { type: 'game:timeout', color: loser });
    }

    // Live: seats held too long for players who never came back
    const state = engine.getState();
    if (state.phase === 'playing' && !slow) {
      for (const p of state.players) {
        if (p.type !== 'ai' && !p.isConnected && !p.isEliminated && p.disconnectedAt && now - p.disconnectedAt >= this.t(DISCONNECT_GRACE_MS)) {
          if (engine.getState().phase !== 'playing') break;
          const result = engine.resign(p.color);
          await this.afterResult(result, { type: 'game:abandoned', color: p.color });
        }
      }
    }

    // Only robots left in the game: nobody is playing, so end it
    const live = engine.getState();
    if (live.phase === 'playing' && !live.players.some(p => p.type !== 'ai' && !p.isEliminated)) await this.endAbandoned();

    // Clear finished rooms shortly after everyone leaves, and any room left idle for a week
    if (this.ctx.getWebSockets().length === 0) {
      const finishedAndEmpty = this.gameOver && meta.emptySince != null && now - meta.emptySince >= this.t(FINISHED_EMPTY_MS);
      const idle = now - (meta.lastActivityAt ?? meta.createdAt) >= this.t(IDLE_CAP_MS);
      if (finishedAndEmpty || idle) {
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
    await this.persist();
    await this.logEvent({ type: 'game:over', ...this.gameOver });
  }

  /** Close a lobby that never started: tell anyone still in it, then forget the room */
  private async closeRoom(reason: 'expired' | 'host_left'): Promise<void> {
    this.broadcast({ type: 'room:closed', reason });
    for (const ws of this.ctx.getWebSockets()) {
      try { ws.close(4001, reason === 'expired' ? 'Room closed: nobody joined' : 'Room closed: the host left'); } catch { /* already closed */ }
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
    if (move) times.push(move);
    if (this.gameOver && meta.emptySince != null) times.push(meta.emptySince + this.t(FINISHED_EMPTY_MS));

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
      try { ws.send(text); } catch { /* socket already gone */ }
    }
  }

  private stateForClients() {
    const state = this.engine!.getState();
    return {
      ...state,
      winner: this.gameOver?.winner ?? null,
      endReason: this.gameOver?.reason ?? null,
      drawOfferFrom: this.engine!.drawOfferFrom,
      seq: this.seq,
      lobbyClosesAt: state.phase === 'waiting' ? this.meta?.expiresAt ?? null : null,
      moveDeadline: this.slowDeadline() ?? this.clockDeadline(),
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
    for (const ws of this.ctx.getWebSockets()) {
      if (ws === except) continue;
      try { ws.send(text); } catch { /* socket already gone */ }
    }
  }
}

function eventKey(seq: number): string {
  return 'ev:' + String(seq).padStart(8, '0');
}

/** Only accept known settings, within the game's limits */
export function sanitizeConfig(input: Partial<GameConfig> = {}): Partial<GameConfig> {
  const config: Partial<GameConfig> = {};
  if (input.boardSize === '4x4x4' || input.boardSize === '6x6x6' || input.boardSize === '8x8x8') config.boardSize = input.boardSize;
  if ([2, 4, 6].includes(Number(input.playerCount))) config.playerCount = Number(input.playerCount);
  if (typeof input.chaosMode === 'boolean') config.chaosMode = input.chaosMode;
  if (Number.isInteger(input.chaosInterval)) config.chaosInterval = Math.min(25, Math.max(3, Number(input.chaosInterval)));
  if (typeof input.timedMode === 'boolean') config.timedMode = input.timedMode;
  if (Number.isFinite(input.timePerPlayerSeconds)) config.timePerPlayerSeconds = Math.min(1800, Math.max(60, Number(input.timePerPlayerSeconds)));
  // Board sizes the player count needs (matches the game's setup screen)
  if (config.playerCount === 4 && config.boardSize === '4x4x4') config.boardSize = '6x6x6';
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
