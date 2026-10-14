import { v4 as uuidv4 } from 'uuid';
import type {
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
  chaosRotation?: GameState['moveHistory'][number]; // rotation record if triggered
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
  private chaosCountdown: number;
  private turnsSinceChaos: number;
  private drawOffer: { from: PlayerColor } | null = null;

  constructor(roomCode: string, config: Partial<GameConfig> = {}) {
    this.gameId = uuidv4();
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
      id: uuidv4(),
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
    return this.getActivePlayers().map(p => p.color);
  }

  // ── Phase transitions ────────────────────────────────────────────────────────

  startPlacement(): boolean {
    if (this.phase !== 'waiting') return false;
    this.phase = 'placement';
    return true;
  }

  completePlacement(color: PlayerColor): boolean {
    const p = this.players.find(p => p.color === color);
    if (!p) return false;
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
    this.phase = 'playing';
    this.currentTurn = this.getActiveColors()[0]; // White starts
    this.positionHistory.push(this.board.hash());
    return true;
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
    this.board.placePiece(pos, pieceType, color);
    return { success: true };
  }

  private isValidPlacementZone(color: PlayerColor, pos: BoardPosition): boolean {
    const { axis, direction } = PLAYER_ADVANCE[color];
    const coord = pos[axis];
    return direction === 1 ? coord === 0 : coord === this.board.size - 1;
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

    // Run check detection for all active players
    const checkStatuses = this.buildCheckStatuses();

    // Check for end conditions on the NEXT player(s)
    let eliminatedPlayer: PlayerColor | undefined;
    let conqueredBy: PlayerColor | undefined;
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
      // Advance turn to next active player
      this.advanceTurn(movedColor);

      // Chaos mode check
      if (this.config.chaosMode) {
        this.turnsSinceChaos++;
        if (this.turnsSinceChaos >= this.config.chaosInterval) {
          this.turnsSinceChaos = 0;
          const rotation = ChaosRotationEngine.applyRandom(this.board);
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

  // ── Resignation & draw ───────────────────────────────────────────────────────

  resign(color: PlayerColor): MoveResult {
    const active = this.getActiveColors();
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
    // Multi-player: just eliminate
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
