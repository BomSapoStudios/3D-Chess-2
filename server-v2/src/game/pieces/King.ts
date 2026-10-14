import type { BoardPosition, PlayerColor } from '../../types/game.js';
import type { ChessBoard } from '../ChessBoard.js';
import { ChessPiece } from './ChessPiece.js';

export class King extends ChessPiece {
  readonly type = 'King' as const;

  getValidMoves(from: BoardPosition, color: PlayerColor, board: ChessBoard): BoardPosition[] {
    const moves: BoardPosition[] = [];
    // King can move 1 step in any of 26 directions (all combinations of dx,dy,dz ∈ {-1,0,1})
    for (let dx = -1; dx <= 1; dx++)
      for (let dy = -1; dy <= 1; dy++)
        for (let dz = -1; dz <= 1; dz++) {
          if (dx === 0 && dy === 0 && dz === 0) continue;
          moves.push(...this.step(from, dx, dy, dz, color, board));
        }
    return moves;
  }
}
