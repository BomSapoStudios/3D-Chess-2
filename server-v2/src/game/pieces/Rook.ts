import type { BoardPosition, PlayerColor } from '../../types/game.js';
import type { ChessBoard } from '../ChessBoard.js';
import { ChessPiece } from './ChessPiece.js';

export class Rook extends ChessPiece {
  readonly type = 'Rook' as const;

  getValidMoves(from: BoardPosition, color: PlayerColor, board: ChessBoard): BoardPosition[] {
    const moves: BoardPosition[] = [];
    // Rook slides along a single axis (6 directions)
    const axes: [number, number, number][] = [
      [1,0,0], [-1,0,0],
      [0,1,0], [0,-1,0],
      [0,0,1], [0,0,-1],
    ];
    for (const [dx, dy, dz] of axes) {
      moves.push(...this.slide(from, dx, dy, dz, color, board));
    }
    return moves;
  }
}
