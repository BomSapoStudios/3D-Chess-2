import type { BoardPosition, PlayerColor } from '../../types/game.js';
import type { ChessBoard } from '../ChessBoard.js';
import { ChessPiece } from './ChessPiece.js';

// Knight L-shapes in 3D: 2 steps along one axis + 1 step along another.
// There are 24 distinct L-shape combinations in 3D (vs 8 in 2D).
const KNIGHT_MOVES: [number, number, number][] = [];
const axes = [0, 1, 2]; // x, y, z
for (const longAxis of axes) {
  for (const shortAxis of axes) {
    if (shortAxis === longAxis) continue;
    for (const longSign of [-2, 2]) {
      for (const shortSign of [-1, 1]) {
        const move: [number, number, number] = [0, 0, 0];
        move[longAxis] = longSign;
        move[shortAxis] = shortSign;
        KNIGHT_MOVES.push(move);
      }
    }
  }
}

export class Knight extends ChessPiece {
  readonly type = 'Knight' as const;

  getValidMoves(from: BoardPosition, color: PlayerColor, board: ChessBoard): BoardPosition[] {
    const moves: BoardPosition[] = [];
    for (const [dx, dy, dz] of KNIGHT_MOVES) {
      // Knight CAN jump over pieces — use step but skip friend check
      const pos: BoardPosition = { x: from.x + dx, y: from.y + dy, z: from.z + dz };
      if (!board.isInBounds(pos)) continue;
      if (board.isOccupiedByFriend(pos, color)) continue;
      moves.push(pos);
    }
    return moves;
  }
}
