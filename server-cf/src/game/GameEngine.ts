import type {
  ChaosRotation,
  GameState, GameConfig, PlayerState, PlayerColor, PieceType,
  BoardPosition, MoveRecord, CheckStatus, EndReason, BoardSize,
} from '../types/game.js';
import {
  PLAYER_COLORS_BY_COUNT, BOARD_DIMENSIONS, DEFAULT_GAME_CONFIG, PLAYER_ADVANCE,
} from '../types/game.js';
import { ChessBoard } from './ChessBoard.js';
import { CheckDetection } from './CheckDetection.js';
import { GameEndDetection } from './GameEndDetection.js';
import { ChaosRotationEngine } from './ChaosRotation.js';
import { Pawn } from './pieces/index.js';

/** Each side's pieces, placed during setup (matches the game's trays) */
export const STARTING_SET: Partial<Record<PieceType, number>> = { King: 1, Queen: 1, Rook: 2, Bishop: 2, Knight: 2 };

// ─────────────────────────────────────────────────────────────────────────────
// GameEngine — authoritative game coordinator
//
// One instance per active game room. Holds the ChessBoard, enforces rules,
// advances turns, and produces structured results for broadcasting.
// ─────────────────────────────────────────────────────────────────────────────

export interface MoveResult {
  success: boolean;
  error?: string;
  newState?: GameState;
  eliminatedPlayer?: PlayerColor;
  conqueredBy?: PlayerColor;
  gameOver?: { winner: PlayerColor | null; reason: EndReason };
  requiresPromotion?: { position: BoardPosition; color: PlayerColor };
  chaosRotation?: ChaosRotation; // the spin applied after this move, if any
}

export interface EngineSnapshot {
  gameId: string;
  roomCode: string;
  config: GameConfig;
  board: GameState['board'];
  players: PlayerState[];
  phase: GameState['phase'];
  currentTurn: PlayerColor | null;
  turnNumber: number;
  moveHistory: MoveRecord[];
  positionHistory: string[];
  pendingPromotion: { position: BoardPosition; color: PlayerColor } | null;
  chaosCountdown: number;
  turnsSinceChaos: number;
  drawOffer: { from: PlayerColor } | null;
  /** Players who started in check and haven't moved yet (no king may be taken until they have) */
  startedInCheck?: PlayerColor[];
  /** For each player in check, who checked them first (they win the pieces if that player forfeits) */
  firstChecker?: Partial<Record<PlayerColor, PlayerColor>>;
}

export interface PlacePieceResult {
  success: boolean;
  error?: string;
}

export class GameEngine {
  readonly gameId: string;
  readonly roomCode: string;
  readonly config: GameConfig;

  private board: ChessBoard;
  private players: PlayerState[];
  private phase: GameState['phase'];
  private currentTurn: PlayerColor | null;
  private turnNumber: number;
  private moveHistory: MoveRecord[];
  private positionHistory: string[]; // for threefold repetition
  private pendingPromotion: { position: BoardPosition; color: PlayerColor } | null = null;
  private startedInCheck: PlayerColor[] = [];
  private firstChecker: Partial<Record<PlayerColor, PlayerColor>> = {};
  private chaosCountdown: number;
  private turnsSinceChaos: number;
  private drawOffer: { from: PlayerColor } | null = null;

  constructor(roomCode: string, config: Partial<GameConfig> = {}) {
    this.gameId = crypto.randomUUID();
    this.roomCode = roomCode;
    this.config = { ...DEFAULT_GAME_CONFIG, ...config };
    this.board = new ChessBoard(this.config.boardSize);
    this.players = [];
    this.phase = 'waiting';
    this.currentTurn = null;
    this.turnNumber = 0;
    this.moveHistory = [];
    this.positionHistory = [];
    this.chaosCountdown = this.config.chaosInterval;
    this.turnsSinceChaos = 0;
  }

  // ── Player management ────────────────────────────────────────────────────────

  addPlayer(
    userId: string,
    username: string,
    color: PlayerColor,
    type: PlayerState['type'] = 'human',
    aiDifficulty?: PlayerState['aiDifficulty'],
  ): boolean {
    if (this.players.length >= this.config.playerCount) return false;
    if (this.players.find(p => p.color === color)) return false;

    const timeMs = this.config.timedMode
      ? this.config.timePerPlayerSeconds * 1000
      : null;

    this.players.push({
      id: crypto.randomUUID(),
      userId,
      username,
      color,
      type,
      aiDifficulty,
      isReady: false,
      isConnected: true,
      isEliminated: false,
      disconnectedAt: null,
      placementComplete: false,
      remainingTimeMs: timeMs,
      eloRating: 1200,
    });
    return true;
  }

  setPlayerReady(color: PlayerColor, ready: boolean): void {
    const p = this.players.find(p => p.color === color);
    if (p) p.isReady = ready;
  }

  setPlayerConnected(color: PlayerColor, connected: boolean): void {
    const p = this.players.find(p => p.color === color);
    if (p) {
      p.isConnected = connected;
      p.disconnectedAt = connected ? null : Date.now();
    }
  }

  getActivePlayers(): PlayerState[] {
    return this.players.filter(p => !p.isEliminated);
  }

  getActiveColors(): PlayerColor[] {
    // Turn order is always White, Black, Green, Purple, Yellow, Orange, whatever order players joined in
    const order = PLAYER_COLORS_BY_COUNT[6];
    return this.getActivePlayers().map(p => p.color).sort((a, b) => order.indexOf(a) - order.indexOf(b));
  }

  // ── Phase transitions ────────────────────────────────────────────────────────

  startPlacement(): boolean {
    if (this.phase !== 'waiting') return false;
    this.phase = 'placement';
    return true;
  }

  /** Pieces this player still has to put on the board */
  piecesLeftToPlace(color: PlayerColor): number {
    const total = Object.values(STARTING_SET).reduce((a, b) => a + b, 0);
    return total - this.board.getPiecesByColor(color).length;
  }

  /** A player is done setting up. Every piece must be on the board first. */
  completePlacement(color: PlayerColor): boolean {
    const p = this.players.find(p => p.color === color);
    if (!p) return false;
    if (this.piecesLeftToPlace(color) > 0) return false;
    p.placementComplete = true;
    return true;
  }

  allPlayersReady(): boolean {
    return this.players.every(p => p.isReady && p.isConnected);
  }

  allPlacementsComplete(): boolean {
    return this.players.every(p => p.placementComplete);
  }

  startGame(): boolean {
    if (this.phase !== 'placement') return false;
    if (!this.allPlacementsComplete()) return false;
    this.spawnPawns();
    this.phase = 'playing';
    // White starts, unless someone was placed into check: then the first player in turn order
    // who is in check moves first, so they get to resolve it (matches the Unity client)
    const active = this.getActiveColors();
    this.startedInCheck = active.filter(c => CheckDetection.getCheckInfo(c, this.board).isInCheck);
    this.currentTurn = this.startedInCheck[0] ?? active[0];
    this.updateFirstCheckers(null);
    this.positionHistory.push(this.board.hash());
    return true;
  }

  /**
   * Matches the Unity client: when play starts, each placed non-pawn piece gets a
   * pawn on the cell directly in front of it (one step along its advance axis),
   * if that cell is on the board and empty.
   */
  private spawnPawns(): void {
    const n = this.board.size;
    const placed: { color: PlayerColor; position: BoardPosition }[] = [];
    for (let x = 0; x < n; x++) {
      for (let y = 0; y < n; y++) {
        for (let z = 0; z < n; z++) {
          const piece = this.board.getPiece({ x, y, z });
          if (piece && piece.type !== 'Pawn') placed.push({ color: piece.color, position: { x, y, z } });
        }
      }
    }
    for (const { color, position } of placed) {
      const { axis, direction } = PLAYER_ADVANCE[color];
      const front = { ...position, [axis]: position[axis] + direction } as BoardPosition;
      if (this.board.isInBounds(front) && this.board.isEmpty(front)) {
        this.board.placePiece(front, 'Pawn', color);
      }
    }
  }

  // ── Piece placement phase ────────────────────────────────────────────────────

  placePiece(color: PlayerColor, pieceType: PieceType, pos: BoardPosition): PlacePieceResult {
    if (this.phase !== 'placement') {
      return { success: false, error: 'Not in placement phase' };
    }
    if (!this.board.isInBounds(pos)) {
      return { success: false, error: 'Position out of bounds' };
    }
    if (!this.board.isEmpty(pos)) {
      return { success: false, error: 'Cell already occupied' };
    }
    if (!this.isValidPlacementZone(color, pos)) {
      return { success: false, error: 'Position outside your placement zone' };
    }
    if (this.players.find(p => p.color === color)?.placementComplete) {
      return { success: false, error: 'You already said you are ready' };
    }
    const placed = this.board.getPiecesByColor(color).filter(p => p.type === pieceType).length;
    if (placed >= (STARTING_SET[pieceType] ?? 0)) {
      return { success: false, error: `No ${pieceType} left to place` };
    }
    this.board.placePiece(pos, pieceType, color);
    return { success: true };
  }

  /**
   * Same zones as the game (PlacementManager.GetPlacementZoneForColor): your own face of the cube,
   * and on bigger boards only part of it, so players can't start in each other's reach.
   *  6³: White/Black use Y 3-5, Z 1-4; Green/Purple use Y 0-2, X 1-4.  8³: the middle 4x4 of each face.
   */
  private isValidPlacementZone(color: PlayerColor, pos: BoardPosition): boolean {
    const { axis, direction } = PLAYER_ADVANCE[color];
    const n = this.board.size;
    if (pos[axis] !== (direction === 1 ? 0 : n - 1)) return false;
    const within = (v: number, lo: number, hi: number) => v >= lo && v <= hi;
    if (n === 6) {
      if (axis === 'x') return within(pos.y, 3, 5) && within(pos.z, 1, 4);
      if (axis === 'z') return within(pos.y, 0, 2) && within(pos.x, 1, 4);
    } else if (n === 8) {
      if (axis === 'x') return within(pos.y, 2, 5) && within(pos.z, 2, 5);
      if (axis === 'z') return within(pos.x, 2, 5) && within(pos.y, 2, 5);
      if (axis === 'y') return within(pos.x, 2, 5) && within(pos.z, 2, 5);
    }
    return true;
  }

  // ── Move execution ────────────────────────────────────────────────────────────

  executeMove(color: PlayerColor, from: BoardPosition, to: BoardPosition): MoveResult {
    if (this.phase !== 'playing') {
      return { success: false, error: 'Game is not in playing phase' };
    }
    if (this.currentTurn !== color) {
      return { success: false, error: `It is not ${color}'s turn` };
    }
    if (this.pendingPromotion) {
      return { success: false, error: 'Pawn promotion must be resolved first' };
    }

    const piece = this.board.getPiece(from);
    if (!piece) {
      return { success: false, error: 'No piece at source position' };
    }
    if (piece.color !== color) {
      return { success: false, error: 'That piece does not belong to you' };
    }

    // Validate move against legal moves (includes check avoidance)
    const legalMoves = CheckDetection.getLegalMoves(from, color, this.board);
    if (!legalMoves.some(m => m.x === to.x && m.y === to.y && m.z === to.z)) {
      return { success: false, error: 'Illegal move' };
    }

    // Nobody can take a king before every player who started in check has had a turn to get out of it
    const target = this.board.getPiece(to);
    if (target?.type === 'King' && this.startedInCheck.length > 0) {
      return { success: false, error: 'Kings cannot be taken until everyone who started in check has moved' };
    }
    this.startedInCheck = this.startedInCheck.filter(c => c !== color);

    // Execute the move
    const captured = this.board.movePiece(from, to);
    const isCapture = captured !== null;

    const record: MoveRecord = {
      moveNumber: this.turnNumber + 1,
      playerColor: color,
      from,
      to,
      pieceType: piece.type,
      isCapture,
      capturedPieceType: captured?.type,
      isPromotion: false,
      isChaosRotation: false,
      notation: this.buildNotation(piece.type, color, from, to, isCapture),
      timestamp: Date.now(),
    };
    this.moveHistory.push(record);
    this.positionHistory.push(this.board.hash());

    // Check pawn promotion
    if (piece.type === 'Pawn' && Pawn.isPromotionSquare(to, color, this.board.size)) {
      this.pendingPromotion = { position: to, color };
      return {
        success: true,
        newState: this.getState(),
        requiresPromotion: { position: to, color },
      };
    }

    return this.finalizeTurn(color, record);
  }

  promotePawn(color: PlayerColor, position: BoardPosition, pieceType: PieceType): MoveResult {
    if (!this.pendingPromotion) {
      return { success: false, error: 'No promotion pending' };
    }
    if (this.pendingPromotion.color !== color) {
      return { success: false, error: 'Not your promotion' };
    }

    const validTypes: PieceType[] = ['Queen', 'Rook', 'Bishop', 'Knight'];
    if (!validTypes.includes(pieceType)) {
      return { success: false, error: 'Invalid promotion piece' };
    }

    // Replace pawn with chosen piece
    this.board.removePiece(position);
    this.board.placePiece(position, pieceType, color);

    const lastMove = this.moveHistory[this.moveHistory.length - 1];
    if (lastMove) {
      lastMove.isPromotion = true;
      lastMove.promotedTo = pieceType;
    }

    this.pendingPromotion = null;
    return this.finalizeTurn(color, lastMove);
  }

  private finalizeTurn(movedColor: PlayerColor, record: MoveRecord): MoveResult {
    this.turnNumber++;
    this.updateFirstCheckers(movedColor);

    // Run check detection for all active players
    const checkStatuses = this.buildCheckStatuses();

    // Check for end conditions on the NEXT player(s)
    let eliminatedPlayer: PlayerColor | undefined;
    let conqueredBy: PlayerColor | undefined;
    let chaosRotation: ChaosRotation | undefined;
    let gameOver: MoveResult['gameOver'];

    const nextColors = this.getNextTurnColors(movedColor);

    for (const nextColor of nextColors) {
      const endResult = GameEndDetection.check(
        this.getActiveColors(),
        nextColor,
        this.board,
        this.positionHistory,
      );

      if (!endResult) continue;

      if (endResult.type === 'checkmate') {
        const activeColors = this.getActiveColors();
        eliminatedPlayer = endResult.loser;

        if (activeColors.length > 2) {
          // Conquest system: transfer pieces to the player who delivered checkmate
          conqueredBy = movedColor;
          this.conquest(endResult.loser, movedColor);
          this.eliminatePlayer(endResult.loser);

          // Check if only 1 player left
          if (this.getActiveColors().length === 1) {
            const winner = this.getActiveColors()[0];
            this.phase = 'gameover';
            gameOver = { winner, reason: 'checkmate' };
          }
        } else {
          // 2-player: checkmate ends the game
          this.phase = 'gameover';
          gameOver = { winner: movedColor, reason: 'checkmate' };
        }
        break;
      }

      if (endResult.type === 'stalemate') {
        // With 3+ players a stuck player just sits out their turns (like the game); only a
        // two-player stalemate is a draw
        if (this.getActiveColors().length > 2) continue;
        this.phase = 'gameover';
        gameOver = { winner: null, reason: 'stalemate' };
        break;
      }

      if (endResult.type === 'insufficient_material') {
        this.phase = 'gameover';
        gameOver = { winner: null, reason: 'insufficient_material' };
        break;
      }

      if (endResult.type === 'threefold_repetition') {
        this.phase = 'gameover';
        gameOver = { winner: null, reason: 'threefold_repetition' };
        break;
      }
    }

    if (!gameOver) {
      // Advance turn to the next active player who can move. With 3+ players, anyone with
      // no legal moves (and not in check) is skipped; if nobody at all can move, it's a draw.
      this.advanceTurn(movedColor);
      if (this.getActiveColors().length > 2) {
        let tries = this.getActiveColors().length;
        while (this.currentTurn && !CheckDetection.hasAnyLegalMove(this.currentTurn, this.board) && tries-- > 0) {
          this.advanceTurn(this.currentTurn);
        }
        if (tries < 0) {
          this.phase = 'gameover';
          gameOver = { winner: null, reason: 'stalemate' };
        }
      }
    }

    if (!gameOver) {

      // Chaos mode check
      if (this.config.chaosMode) {
        this.turnsSinceChaos++;
        if (this.turnsSinceChaos >= this.config.chaosInterval) {
          this.turnsSinceChaos = 0;
          // One slice turned a quarter (like the game's own Chaos Mode); every device plays this exact spin
          const rotation = ChaosRotationEngine.applyRandom(this.board, 'layer');
          chaosRotation = rotation;
          // Record chaos rotation in move history
          const chaosRecord: MoveRecord = {
            moveNumber: this.turnNumber,
            playerColor: this.currentTurn!,
            from: { x: -1, y: -1, z: -1 },
            to: { x: -1, y: -1, z: -1 },
            pieceType: 'King', // placeholder
            isCapture: false,
            isPromotion: false,
            isChaosRotation: true,
            chaosRotationType: rotation.type,
            notation: `CHAOS:${rotation.type}`,
            timestamp: Date.now(),
          };
          this.moveHistory.push(chaosRecord);
        }
      }
    }

    return {
      success: true,
      newState: this.getState(),
      eliminatedPlayer,
      conqueredBy,
      gameOver,
      chaosRotation,
    };
  }

  private getNextTurnColors(justMoved: PlayerColor): PlayerColor[] {
    const active = this.getActiveColors();
    const idx = active.indexOf(justMoved);
    // Return all colors that come after justMoved in turn order
    if (idx === -1) return active;
    const rotated = [...active.slice(idx + 1), ...active.slice(0, idx + 1)];
    return rotated.slice(0, active.length - 1); // exclude justMoved
  }

  private advanceTurn(currentColor: PlayerColor): void {
    const active = this.getActiveColors();
    if (active.length === 0) return;
    const idx = active.indexOf(currentColor);
    this.currentTurn = active[(idx + 1) % active.length];
  }

  // ── Multi-player elimination & conquest ──────────────────────────────────────

  private eliminatePlayer(color: PlayerColor): void {
    const p = this.players.find(p => p.color === color);
    if (p) p.isEliminated = true;
  }

  private conquest(loser: PlayerColor, winner: PlayerColor): void {
    // Transfer all of loser's pieces to winner
    const loserPieces = this.board.getPiecesByColor(loser);
    for (const piece of loserPieces) {
      if (piece.type === 'King') {
        this.board.removePiece(piece.position);
      } else {
        this.board.setPiece(piece.position, { ...piece, color: winner });
      }
    }
  }

  /** The players attacking `color`'s King, in turn order */
  private checkersOf(color: PlayerColor): PlayerColor[] {
    const threats = CheckDetection.getCheckInfo(color, this.board).threateningPieces;
    return this.getActiveColors().filter(c => c !== color && threats.some(t => t.color === c));
  }

  /**
   * Remember who first put each player in check. The first checker keeps the claim while
   * they're still attacking; a check that starts on a move belongs to the player who moved.
   */
  private updateFirstCheckers(mover: PlayerColor | null): void {
    for (const c of this.getActiveColors()) {
      const checkers = this.checkersOf(c);
      const current = this.firstChecker[c];
      if (checkers.length === 0) delete this.firstChecker[c];
      else if (!current || !checkers.includes(current)) {
        this.firstChecker[c] = mover && checkers.includes(mover) ? mover : checkers[0];
      }
    }
  }

  // ── Resignation & draw ───────────────────────────────────────────────────────

  /**
   * Forfeit. With two players left, the other one wins. With more, the game goes on:
   * a player who forfeits while in check counts as checkmated by whoever checked them
   * first, who takes their pieces; otherwise their pieces leave the board.
   */
  resign(color: PlayerColor): MoveResult {
    const active = this.getActiveColors();
    if (active.length > 2 && active.includes(color)) {
      const checkers = this.checkersOf(color);
      const checker = checkers.length > 0
        ? (this.firstChecker[color] && checkers.includes(this.firstChecker[color]!) ? this.firstChecker[color]! : checkers[0])
        : undefined;
      if (checker) this.conquest(color, checker);
      else for (const p of this.board.getPiecesByColor(color)) this.board.removePiece(p.position);

      // Their turn passes to the next player still in the game
      const wasTheirTurn = this.currentTurn === color;
      const order = active;
      this.eliminatePlayer(color);
      delete this.firstChecker[color];
      this.startedInCheck = this.startedInCheck.filter(c => c !== color);
      if (wasTheirTurn) {
        const i = order.indexOf(color);
        this.currentTurn = [...order.slice(i + 1), ...order.slice(0, i)].find(c => c !== color) ?? null;
      }
      this.updateFirstCheckers(null);
      const left = this.getActiveColors();
      if (left.length === 1) {
        this.phase = 'gameover';
        return { success: true, newState: this.getState(), eliminatedPlayer: color, conqueredBy: checker, gameOver: { winner: left[0], reason: 'resignation' } };
      }
      return { success: true, newState: this.getState(), eliminatedPlayer: color, conqueredBy: checker };
    }
    if (active.length <= 2) {
      const winner = active.find(c => c !== color) ?? null;
      this.phase = 'gameover';
      return {
        success: true,
        newState: this.getState(),
        eliminatedPlayer: color,
        gameOver: { winner, reason: 'resignation' },
      };
    }
    this.eliminatePlayer(color);
    return { success: true, newState: this.getState(), eliminatedPlayer: color };
  }

  offerDraw(color: PlayerColor): boolean {
    this.drawOffer = { from: color };
    return true;
  }

  respondDraw(color: PlayerColor, accept: boolean): MoveResult | null {
    if (!this.drawOffer || this.drawOffer.from === color) return null;
    if (accept) {
      this.phase = 'gameover';
      this.drawOffer = null;
      return {
        success: true,
        newState: this.getState(),
        gameOver: { winner: null, reason: 'draw_agreement' },
      };
    }
    this.drawOffer = null;
    return { success: true, newState: this.getState() };
  }

  /** End the game with no winner (everyone left, or a lobby that never filled) */
  abandon(): void {
    this.phase = 'gameover';
  }

  forfeitByTimeout(color: PlayerColor): MoveResult {
    this.phase = 'gameover';
    const active = this.getActiveColors().filter(c => c !== color);
    const winner = active.length === 1 ? active[0] : null;
    return {
      success: true,
      newState: this.getState(),
      eliminatedPlayer: color,
      gameOver: { winner, reason: 'timeout' },
    };
  }

  // ── Timer ────────────────────────────────────────────────────────────────────

  tickTimer(deltaMs: number): { timedOut: PlayerColor | null } {
    if (!this.config.timedMode || !this.currentTurn || this.phase !== 'playing') {
      return { timedOut: null };
    }
    const player = this.players.find(p => p.color === this.currentTurn);
    if (!player || player.remainingTimeMs === null) return { timedOut: null };

    player.remainingTimeMs -= deltaMs;
    if (player.remainingTimeMs <= 0) {
      player.remainingTimeMs = 0;
      return { timedOut: this.currentTurn };
    }
    return { timedOut: null };
  }

  // ── Persistence (Durable Object storage) ─────────────────────────────────────

  /** Everything needed to rebuild this engine after the room is evicted from memory */
  toSnapshot(): EngineSnapshot {
    return {
      gameId: this.gameId,
      roomCode: this.roomCode,
      config: this.config,
      board: this.board.toState(),
      players: this.players.map(p => ({ ...p })),
      phase: this.phase,
      currentTurn: this.currentTurn,
      turnNumber: this.turnNumber,
      moveHistory: [...this.moveHistory],
      positionHistory: [...this.positionHistory],
      pendingPromotion: this.pendingPromotion,
      chaosCountdown: this.chaosCountdown,
      turnsSinceChaos: this.turnsSinceChaos,
      drawOffer: this.drawOffer,
      startedInCheck: [...this.startedInCheck],
      firstChecker: { ...this.firstChecker },
    };
  }

  static fromSnapshot(s: EngineSnapshot): GameEngine {
    const engine = new GameEngine(s.roomCode, s.config);
    (engine as unknown as { gameId: string }).gameId = s.gameId;
    engine.board = ChessBoard.fromState(s.board, s.config.boardSize);
    engine.players = s.players.map(p => ({ ...p }));
    engine.phase = s.phase;
    engine.currentTurn = s.currentTurn;
    engine.turnNumber = s.turnNumber;
    engine.moveHistory = [...s.moveHistory];
    engine.positionHistory = [...s.positionHistory];
    engine.pendingPromotion = s.pendingPromotion;
    engine.chaosCountdown = s.chaosCountdown;
    engine.turnsSinceChaos = s.turnsSinceChaos;
    engine.drawOffer = s.drawOffer;
    engine.startedInCheck = [...(s.startedInCheck ?? [])];
    engine.firstChecker = { ...(s.firstChecker ?? {}) };
    return engine;
  }

  get drawOfferFrom(): PlayerColor | null {
    return this.drawOffer?.from ?? null;
  }

  // ── State serialization ──────────────────────────────────────────────────────

  getState(): GameState {
    return {
      gameId: this.gameId,
      roomCode: this.roomCode,
      phase: this.phase,
      config: this.config,
      board: this.board.toState(),
      players: this.players.map(p => ({ ...p })),
      currentTurn: this.currentTurn,
      turnNumber: this.turnNumber,
      moveHistory: [...this.moveHistory],
      checkStatus: this.buildCheckStatuses(),
      chaosCountdown: this.config.chaosInterval - this.turnsSinceChaos,
      winner: null,
      endReason: null,
    };
  }

  private buildCheckStatuses(): CheckStatus[] {
    return this.getActivePlayers().map(player => {
      const info = CheckDetection.getCheckInfo(player.color, this.board);
      return {
        playerColor: player.color,
        isInCheck: info.isInCheck,
        threateningPieces: info.threateningPieces.map(p => p.position),
        kingPosition: info.kingPosition ?? { x: -1, y: -1, z: -1 },
      };
    });
  }

  // ── Notation helper ─────────────────────────────────────────────────────────

  private buildNotation(
    type: PieceType,
    color: PlayerColor,
    from: BoardPosition,
    to: BoardPosition,
    isCapture: boolean,
  ): string {
    const abbr: Record<PieceType, string> = {
      King: 'K', Queen: 'Q', Rook: 'R', Bishop: 'B', Knight: 'N', Pawn: '',
    };
    const fromStr = `(${from.x},${from.y},${from.z})`;
    const toStr = `(${to.x},${to.y},${to.z})`;
    const cap = isCapture ? 'x' : '→';
    return `${color[0]}${abbr[type]}${fromStr}${cap}${toStr}`;
  }
}
