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

const DISCONNECT_GRACE_MS = 2 * 60 * 1000;      // seat is held this long for a dropped player
const ABANDONED_ROOM_MS = 24 * 60 * 60 * 1000;   // empty rooms are cleared after a day

export class Room extends DurableObject<Env> {
  private meta: RoomMeta | null = null;
  private engine: GameEngine | null = null;
  private seats: Record<string, PlayerColor> = {}; // userId → color
  private gameOver: GameOverInfo | null = null;
  private turnStartedAt: number | null = null;     // for timed games

  constructor(ctx: DurableObjectState, env: Env) {
    super(ctx, env);
    ctx.blockConcurrencyWhile(async () => {
      const stored = await ctx.storage.get(['meta', 'engine', 'seats', 'gameOver', 'turnStartedAt']);
      this.meta = (stored.get('meta') as RoomMeta) ?? null;
      const snap = stored.get('engine') as EngineSnapshot | undefined;
      this.engine = snap ? GameEngine.fromSnapshot(snap) : null;
      this.seats = (stored.get('seats') as Record<string, PlayerColor>) ?? {};
      this.gameOver = (stored.get('gameOver') as GameOverInfo) ?? null;
      this.turnStartedAt = (stored.get('turnStartedAt') as number) ?? null;
    });
  }

  // ── HTTP entry (from the Worker) ──────────────────────────────────────────────

  async fetch(request: Request): Promise<Response> {
    const url = new URL(request.url);

    if (url.pathname === '/init' && request.method === 'POST') {
      if (this.meta) return Response.json({ error: 'exists' }, { status: 409 });
      const body = await request.json() as { code: string; hostUserId: string; isPublic: boolean; config: Partial<GameConfig> };
      this.meta = { code: body.code, hostUserId: body.hostUserId, isPublic: body.isPublic, createdAt: Date.now() };
      this.engine = new GameEngine(body.code, sanitizeConfig(body.config));
      // Robot seats are filled now; the host's device plays them
      for (const color of this.engine.config.aiColors ?? []) {
        const robotId = `ai:${color}`;
        this.engine.addPlayer(robotId, 'Robot', color, 'ai', this.engine.config.aiDifficulty);
        this.engine.setPlayerConnected(color, true);
        this.seats[robotId] = color;
      }
      await this.persist();
      await this.ctx.storage.setAlarm(Date.now() + ABANDONED_ROOM_MS);
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

    // Everyone seated: move on to piece placement
    if (engine.getState().phase === 'waiting' && engine.getState().players.length === engine.config.playerCount) {
      engine.startPlacement();
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

      case 'game:placePiece': {
        const result = engine.placePiece(me.color, msg.pieceType as PieceType, msg.position as BoardPosition);
        if (!result.success) return reply(false, result.error);
        await this.persist();
        reply(true);
        this.broadcast({ type: 'game:piecePlaced', color: me.color, pieceType: msg.pieceType, position: msg.position });
        return this.broadcast({ type: 'game:stateUpdate', reason: 'placement', state: this.stateForClients() });
      }

      case 'game:ready': {
        if (engine.getState().phase !== 'placement') return reply(false, 'Not in placement phase');
        engine.completePlacement(me.color);
        this.broadcast({ type: 'game:playerReady', color: me.color });
        if (engine.allPlacementsComplete()) {
          engine.startGame();
          this.startTurnClock();
          this.broadcast({ type: 'game:phaseChanged', phase: 'playing' });
        }
        await this.persist();
        reply(true);
        return this.broadcast({ type: 'game:stateUpdate', reason: 'ready', state: this.stateForClients() });
      }

      case 'game:move': {
        if (this.chargeClock()) return reply(false, 'Out of time');
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
    this.broadcast(event);
    if (result.chaosRotation) this.broadcast({ type: 'game:chaosRotation', rotation: result.chaosRotation });
    if (result.requiresPromotion) this.broadcast({ type: 'game:pawnPromotion', ...result.requiresPromotion });
    if (result.eliminatedPlayer && !result.gameOver) {
      this.broadcast({ type: 'game:playerEliminated', color: result.eliminatedPlayer, conqueredBy: result.conqueredBy ?? null });
    }
    if (result.gameOver) this.broadcast({ type: 'game:over', ...result.gameOver });
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
    await this.persist();
    this.broadcast({ type: 'room:playerLeft', color: me.color, name: me.name, graceMs: DISCONNECT_GRACE_MS, state: this.stateForClients() }, ws);
    await this.scheduleAlarm();
  }

  async alarm(): Promise<void> {
    const engine = this.engine;
    if (!engine || !this.meta) return;
    const now = Date.now();

    // Timed games: the player on move ran out of time
    if (this.chargeClock()) {
      const loser = engine.getState().currentTurn!;
      const result = engine.forfeitByTimeout(loser);
      await this.afterResult(result, { type: 'game:timeout', color: loser });
    }

    // Seats held too long for players who never came back
    const state = engine.getState();
    if (state.phase === 'playing') {
      for (const p of state.players) {
        if (!p.isConnected && !p.isEliminated && p.disconnectedAt && now - p.disconnectedAt >= DISCONNECT_GRACE_MS) {
          const result = engine.resign(p.color);
          await this.afterResult(result, { type: 'game:abandoned', color: p.color });
        }
      }
    }

    // Only robots left in the game: nobody is playing, so end it
    const live = engine.getState();
    if (live.phase === 'playing' && !live.players.some(p => p.type !== 'ai' && !p.isEliminated)) {
      this.gameOver = { winner: null, reason: 'abandoned' as EndReason };
      await this.persist();
    }

    // Nobody here for a day: clear the room so its code can be reused
    if (this.ctx.getWebSockets().length === 0 && (now - this.meta.createdAt >= ABANDONED_ROOM_MS || this.gameOver)) {
      await this.ctx.storage.deleteAll();
      this.meta = null;
      this.engine = null;
      return;
    }
    await this.scheduleAlarm();
  }

  private async scheduleAlarm(): Promise<void> {
    if (!this.engine || !this.meta) return;
    const times = [Date.now() + ABANDONED_ROOM_MS]; // only future times: past ones would fire every second
    const state = this.engine.getState();
    for (const p of state.players) {
      // Only players who can still come back; a deadline already handled must not be rescheduled
      // (that made finished seats wake the room every second, all day)
      const deadline = p.disconnectedAt ? p.disconnectedAt + DISCONNECT_GRACE_MS : 0;
      if (!p.isConnected && !p.isEliminated && p.type !== 'ai' && deadline > Date.now() && state.phase === 'playing') times.push(deadline);
    }
    const deadline = this.clockDeadline();
    if (deadline && deadline > Date.now()) times.push(deadline);
    await this.ctx.storage.setAlarm(Math.max(Date.now() + 1000, Math.min(...times)));
  }

  // ── Chess clock (timed games) ─────────────────────────────────────────────────

  private startTurnClock(): void {
    const engine = this.engine;
    this.turnStartedAt = engine && engine.config.timedMode && engine.getState().phase === 'playing' ? Date.now() : null;
  }

  /** Charge elapsed time to the player on move; true if they have run out */
  private chargeClock(): boolean {
    const engine = this.engine;
    if (!engine || this.turnStartedAt === null) return false;
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

  // ── Helpers ──────────────────────────────────────────────────────────────────

  private stateForClients() {
    const state = this.engine!.getState();
    return {
      ...state,
      winner: this.gameOver?.winner ?? null,
      endReason: this.gameOver?.reason ?? null,
      drawOfferFrom: this.engine!.drawOfferFrom,
    };
  }

  private async persist(): Promise<void> {
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
  // Robot seats: any seats but White (the host), and at least one seat left for a human opponent
  if (Array.isArray(input.aiColors)) {
    const seats = PLAYER_COLORS_BY_COUNT[config.playerCount ?? 2];
    const robots = seats.filter(c => c !== 'White' && input.aiColors!.includes(c));
    if (robots.length < seats.length - 1) config.aiColors = robots;
  }
  return config;
}
