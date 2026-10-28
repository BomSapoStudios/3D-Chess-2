// ─────────────────────────────────────────────────────────────────────────────
// Core Game Types — shared across server, web client, and Unity bridge
// ─────────────────────────────────────────────────────────────────────────────

export type PlayerColor = 'White' | 'Black' | 'Green' | 'Purple' | 'Yellow' | 'Orange';
export type PieceType = 'King' | 'Queen' | 'Rook' | 'Bishop' | 'Knight' | 'Pawn';
// 4x4x4 is only made by older versions of the app; current ones play 5x5x5 as the 2-player board
export type BoardSize = '4x4x4' | '5x5x5' | '6x6x6' | '8x8x8';
export type GamePhase = 'waiting' | 'placement' | 'playing' | 'gameover';
export type AIDifficulty = 'easy' | 'medium' | 'hard';
export type PlayerType = 'human' | 'ai' | 'guest';

export interface BoardPosition {
  x: number;
  y: number;
  z: number;
}

export interface ChessPieceState {
  type: PieceType;
  color: PlayerColor;
  position: BoardPosition;
  id: string; // unique per piece instance
}

// 3D board: boardState[x][y][z] = ChessPieceState | null
export type BoardState = (ChessPieceState | null)[][][];

export interface PlayerState {
  id: string;
  userId: string | null; // null for unauthed guests still in lobby
  username: string;
  color: PlayerColor;
  type: PlayerType;
  aiDifficulty?: AIDifficulty;
  isReady: boolean;
  isConnected: boolean;
  isEliminated: boolean;
  disconnectedAt: number | null;
  placementComplete: boolean;
  remainingTimeMs: number | null; // null if timed mode off
  eloRating: number;
}

export interface GameConfig {
  boardSize: BoardSize;
  playerCount: number; // 2 | 3 | 4 | 5 | 6
  chaosMode: boolean;
  chaosInterval: number; // every N turns
  timedMode: boolean;
  timePerPlayerSeconds: number;
  aiDifficulty: AIDifficulty;
  /** Seats played by robots. The host's device runs their AI and moves for them. */
  aiColors?: PlayerColor[];
  /** live: everyone plays now (a dropped player has 5 minutes to return).
   *  slow: "Take your time", each move may take up to 24 hours. */
  pace?: GamePace;
}

export type GamePace = 'live' | 'slow';

export interface GameState {
  gameId: string;
  roomCode: string;
  phase: GamePhase;
  config: GameConfig;
  board: BoardState;
  players: PlayerState[];
  currentTurn: PlayerColor | null;
  turnNumber: number;
  moveHistory: MoveRecord[];
  checkStatus: CheckStatus[];
  chaosCountdown: number; // turns until next chaos rotation
  winner: PlayerColor | null;
  endReason: EndReason | null;
}

export interface MoveRecord {
  moveNumber: number;
  playerColor: PlayerColor;
  from: BoardPosition;
  to: BoardPosition;
  pieceType: PieceType;
  isCapture: boolean;
  capturedPieceType?: PieceType;
  capturedColor?: PlayerColor;
  isPromotion: boolean;
  promotedTo?: PieceType;
  isChaosRotation: boolean;
  chaosRotationType?: ChaosRotationType;
  notation: string;
  timestamp: number;
}

export interface CheckStatus {
  playerColor: PlayerColor;
  isInCheck: boolean;
  threateningPieces: BoardPosition[];
  kingPosition: BoardPosition;
}

export type ChaosRotationType = 'face' | 'layer' | 'diagonal';

export interface ChaosRotation {
  type: ChaosRotationType;
  axis: 'x' | 'y' | 'z';
  index: number;       // which face/layer (0 to boardDim-1)
  clockwise: boolean;
  affectedPositions: BoardPosition[]; // positions that moved
  positionMap: { from: BoardPosition; to: BoardPosition }[]; // full mapping
}

export type EndReason =
  | 'checkmate'
  | 'stalemate'
  | 'resignation'
  | 'timeout'
  | 'draw_agreement'
  | 'insufficient_material'
  | 'threefold_repetition'
  | 'fifty_moves'      // 50 moves each with no capture or pawn move
  | 'timeout_draw'     // out of time, but the other player could never have checkmated
  | 'abandoned';

export const BOARD_DIMENSIONS: Record<BoardSize, number> = {
  '4x4x4': 4,
  '5x5x5': 5,
  '6x6x6': 6,
  '8x8x8': 8,
};

export const PLAYER_COLORS_BY_COUNT: Record<number, PlayerColor[]> = {
  2: ['White', 'Black'],
  3: ['White', 'Black', 'Green'],
  4: ['White', 'Black', 'Green', 'Purple'],
  5: ['White', 'Black', 'Green', 'Purple', 'Yellow'],
  6: ['White', 'Black', 'Green', 'Purple', 'Yellow', 'Orange'],
};

// Which axis a color advances along, and in which direction
export const PLAYER_ADVANCE: Record<PlayerColor, { axis: 'x' | 'y' | 'z'; direction: 1 | -1 }> = {
  White:  { axis: 'x', direction:  1 },
  Black:  { axis: 'x', direction: -1 },
  Green:  { axis: 'z', direction:  1 },
  Purple: { axis: 'z', direction: -1 },
  Yellow: { axis: 'y', direction:  1 },
  Orange: { axis: 'y', direction: -1 },
};

export const DEFAULT_GAME_CONFIG: GameConfig = {
  boardSize: '5x5x5',
  playerCount: 2,
  chaosMode: false,
  chaosInterval: 10,
  timedMode: false,
  timePerPlayerSeconds: 600,
  aiDifficulty: 'medium',
  pace: 'live',
};
