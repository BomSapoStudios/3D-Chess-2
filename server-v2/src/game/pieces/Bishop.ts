import type { BoardPosition, PlayerColor } from '../../types/game.js';
import type { ChessBoard } from '../ChessBoard.js';
import { ChessPiece } from './ChessPiece.js';

// All 26 direction vectors that have at least 2 non-zero components (diagonal movement).
// Note: This includes "true 3D diagonals" (|dx|=|dy|=|dz|=1) — 8 of them —
// as well as planar diagonals (exactly 2 non-zero components) — 12 of them.
const DIAGONAL_DIRECTIONS: [number, number, number][] = [];
for (let dx = -1; dx <= 1; dx++)
  for (let dy = -1; dy <= 1; dy++)
    for (let dz = -1; dz <= 1; dz++) {
      const nonZero = (dx !== 0 ? 1 : 0) + (dy !== 0 ? 1 : 0) + (dz !== 0 ? 1 : 0);
      if (nonZero >= 2) DIAGONAL_DIRECTIONS.push([dx, dy, dz]);
    }

export class Bishop extends ChessPiece {
  readonly type = 'Bishop' as const;

  getValidMoves(from: BoardPosition, color: PlayerColor, board: ChessBoard): BoardPosition[] {
    const moves: BoardPosition[] = [];
    for (const [dx, dy, dz] of DIAGONAL_DIRECTIONS) {
      moves.push(...this.slide(from, dx, dy, dz, color, board));
    }
    return moves;
  }
}
