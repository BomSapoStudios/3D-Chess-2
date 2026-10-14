// ─────────────────────────────────────────────────────────────────────────────
// Core Game Types — shared across server, web client, and Unity bridge
// ─────────────────────────────────────────────────────────────────────────────

export type PlayerColor = 'White' | 'Black' | 'Green' | 'Purple' | 'Yellow' | 'Orange';
export type PieceType = 'King' | 'Queen' | 'Rook' | 'Bishop' | 'Knight' | 'Pawn';
export type BoardSize = '4x4x4' | '6x6x6' | '8x8x8';
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
}

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
  | 'abandoned';

export const BOARD_DIMENSIONS: Record<BoardSize, number> = {
  '4x4x4': 4,
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
  boardSize: '4x4x4',
  playerCount: 2,
  chaosMode: false,
  chaosInterval: 10,
  timedMode: false,
  timePerPlayerSeconds: 600,
  aiDifficulty: 'medium',
};
