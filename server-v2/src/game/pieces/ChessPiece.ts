import type { BoardPosition, PlayerColor, PieceType } from '../../types/game.js';
import type { ChessBoard } from '../ChessBoard.js';

// ─────────────────────────────────────────────────────────────────────────────
// Abstract base for all chess pieces.
// Each subclass implements getValidMoves() for its movement rules.
// ─────────────────────────────────────────────────────────────────────────────

export abstract class ChessPiece {
  abstract readonly type: PieceType;

  /**
   * Returns all board positions this piece can legally move to from `from`,
   * ignoring whether the move leaves the king in check (that check is done
   * by CheckDetection after calling this).
   */
  abstract getValidMoves(
    from: BoardPosition,
    color: PlayerColor,
    board: ChessBoard,
  ): BoardPosition[];

  // ── Helpers shared across piece types ────────────────────────────────────────

  /**
   * Walk in a direction (dx,dy,dz) until hitting the edge, a friendly piece,
   * or an enemy piece (which can be captured). Used by Queen, Rook, Bishop.
   */
  protected slide(
    from: BoardPosition,
    dx: number,
    dy: number,
    dz: number,
    color: PlayerColor,
    board: ChessBoard,
  ): BoardPosition[] {
    const moves: BoardPosition[] = [];
    let cur: BoardPosition = { x: from.x + dx, y: from.y + dy, z: from.z + dz };
    while (board.isInBounds(cur)) {
      if (board.isOccupiedByFriend(cur, color)) break;
      moves.push({ ...cur });
      if (board.isOccupiedByEnemy(cur, color)) break; // can capture, but no further
      cur = { x: cur.x + dx, y: cur.y + dy, z: cur.z + dz };
    }
    return moves;
  }

  /**
   * Attempt a single-step move. Returns the position if reachable, else [].
   */
  protected step(
    from: BoardPosition,
    dx: number,
    dy: number,
    dz: number,
    color: PlayerColor,
    board: ChessBoard,
    captureOnly = false,
    moveOnly = false,
  ): BoardPosition[] {
    const pos: BoardPosition = { x: from.x + dx, y: from.y + dy, z: from.z + dz };
    if (!board.isInBounds(pos)) return [];
    if (captureOnly && !board.isOccupiedByEnemy(pos, color)) return [];
    if (moveOnly && !board.isEmpty(pos)) return [];
    if (board.isOccupiedByFriend(pos, color)) return [];
    return [pos];
  }
}
