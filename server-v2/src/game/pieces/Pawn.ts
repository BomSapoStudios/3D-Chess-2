import type { BoardPosition, PlayerColor } from '../../types/game.js';
import type { ChessBoard } from '../ChessBoard.js';
import { PLAYER_ADVANCE } from '../../types/game.js';
import { ChessPiece } from './ChessPiece.js';

export class Pawn extends ChessPiece {
  readonly type = 'Pawn' as const;

  getValidMoves(from: BoardPosition, color: PlayerColor, board: ChessBoard): BoardPosition[] {
    const moves: BoardPosition[] = [];
    const { axis, direction } = PLAYER_ADVANCE[color];

    // ── Forward move (1 step along advance axis, non-capture) ────────────────
    const fwd: [number, number, number] = [0, 0, 0];
    fwd[axis === 'x' ? 0 : axis === 'y' ? 1 : 2] = direction;
    const forwardPos: BoardPosition = {
      x: from.x + fwd[0],
      y: from.y + fwd[1],
      z: from.z + fwd[2],
    };
    if (board.isInBounds(forwardPos) && board.isEmpty(forwardPos)) {
      moves.push(forwardPos);
    }

    // ── Capture moves (8 diagonal cells perpendicular to advance axis) ───────
    // The pawn captures any of the 8 adjacent cells that are "in front" but
    // not directly ahead — i.e. one step forward and one step sideways/diagonally.
    for (let d1 = -1; d1 <= 1; d1++) {
      for (let d2 = -1; d2 <= 1; d2++) {
        if (d1 === 0 && d2 === 0) continue; // that's the direct forward, handled above
        const capturePos = this.buildCapturePos(from, axis, direction, d1, d2);
        if (
          capturePos &&
          board.isInBounds(capturePos) &&
          board.isOccupiedByEnemy(capturePos, color)
        ) {
          moves.push(capturePos);
        }
      }
    }

    return moves;
  }

  private buildCapturePos(
    from: BoardPosition,
    advanceAxis: 'x' | 'y' | 'z',
    direction: 1 | -1,
    d1: number,
    d2: number,
  ): BoardPosition | null {
    // Two "perpendicular" axes to the advance axis
    const perp = this.perpendicularAxes(advanceAxis);
    const pos = { ...from };
    // Move 1 step forward
    pos[advanceAxis] += direction;
    // Move sideways along the two perpendicular axes
    pos[perp[0]] += d1;
    pos[perp[1]] += d2;
    return pos;
  }

  private perpendicularAxes(axis: 'x' | 'y' | 'z'): ['x' | 'y' | 'z', 'x' | 'y' | 'z'] {
    if (axis === 'x') return ['y', 'z'];
    if (axis === 'y') return ['x', 'z'];
    return ['x', 'y'];
  }

  /**
   * Returns true if a pawn at `pos` has reached the promotion rank
   * (the far face opposite its starting face).
   */
  static isPromotionSquare(pos: BoardPosition, color: PlayerColor, boardSize: number): boolean {
    const { axis, direction } = PLAYER_ADVANCE[color];
    const coord = pos[axis];
    return direction === 1 ? coord === boardSize - 1 : coord === 0;
  }
}
