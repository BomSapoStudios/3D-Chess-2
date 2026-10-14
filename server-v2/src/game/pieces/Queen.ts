import type { BoardPosition, PlayerColor } from '../../types/game.js';
import type { ChessBoard } from '../ChessBoard.js';
import { ChessPiece } from './ChessPiece.js';

export class Queen extends ChessPiece {
  readonly type = 'Queen' as const;

  getValidMoves(from: BoardPosition, color: PlayerColor, board: ChessBoard): BoardPosition[] {
    const moves: BoardPosition[] = [];
    // Queen = Rook + Bishop: all 26 non-zero direction vectors
    for (let dx = -1; dx <= 1; dx++)
      for (let dy = -1; dy <= 1; dy++)
        for (let dz = -1; dz <= 1; dz++) {
          if (dx === 0 && dy === 0 && dz === 0) continue;
          moves.push(...this.slide(from, dx, dy, dz, color, board));
        }
    return moves;
  }
}
