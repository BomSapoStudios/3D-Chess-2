import type { BoardPosition, PlayerColor, ChessPieceState } from '../types/game.js';
import type { ChessBoard } from './ChessBoard.js';
import { getPieceInstance } from './pieces/index.js';

// ─────────────────────────────────────────────────────────────────────────────
// CheckDetection — ported from C# CheckDetectionManager.cs
//
// Determines:
//   • Whether a player's King is in check
//   • Which pieces are delivering the check
//   • Whether a specific move would leave/put own King in check
//   • Whether a player has any legal moves (for checkmate/stalemate detection)
// ─────────────────────────────────────────────────────────────────────────────

export interface CheckInfo {
  isInCheck: boolean;
  threateningPieces: ChessPieceState[];
  kingPosition: BoardPosition | null;
}

export class CheckDetection {
  /**
   * Returns all enemy pieces that can currently attack `targetPos` on this board.
   * Used both for check detection and for "is this square safe?" queries.
   */
  static getThreatsTo(
    targetPos: BoardPosition,
    targetColor: PlayerColor,
    board: ChessBoard,
  ): ChessPieceState[] {
    const threats: ChessPieceState[] = [];
    const allPieces = board.getAllPieces();

    for (const piece of allPieces) {
      if (piece.color === targetColor) continue; // own pieces can't threaten own king
      const pieceInstance = getPieceInstance(piece.type);
      const moves = pieceInstance.getValidMoves(piece.position, piece.color, board);
      if (moves.some(m => m.x === targetPos.x && m.y === targetPos.y && m.z === targetPos.z)) {
        threats.push(piece);
      }
    }
    return threats;
  }

  /**
   * Determines whether `color`'s King is currently in check.
   */
  static getCheckInfo(color: PlayerColor, board: ChessBoard): CheckInfo {
    const king = board.findKing(color);
    if (!king) {
      return { isInCheck: false, threateningPieces: [], kingPosition: null };
    }
    const threats = CheckDetection.getThreatsTo(king.position, color, board);
    return {
      isInCheck: threats.length > 0,
      threateningPieces: threats,
      kingPosition: king.position,
    };
  }

  /**
   * Returns true if executing the given move would leave `color`'s King in check.
   * Uses a cloned board so the authoritative state is never mutated.
   */
  static wouldLeaveKingInCheck(
    from: BoardPosition,
    to: BoardPosition,
    color: PlayerColor,
    board: ChessBoard,
  ): boolean {
    const simBoard = board.clone();
    simBoard.movePiece(from, to);
    return CheckDetection.getCheckInfo(color, simBoard).isInCheck;
  }

  /**
   * Returns all legal moves for a piece at `from` — moves that are geometrically
   * valid AND do not leave the king in check.
   */
  static getLegalMoves(from: BoardPosition, color: PlayerColor, board: ChessBoard): BoardPosition[] {
    const piece = board.getPiece(from);
    if (!piece || piece.color !== color) return [];

    const candidateMoves = getPieceInstance(piece.type).getValidMoves(from, color, board);
    return candidateMoves.filter(to =>
      !CheckDetection.wouldLeaveKingInCheck(from, to, color, board)
    );
  }

  /**
   * Returns true if `color` has at least one legal move available.
   */
  static hasAnyLegalMove(color: PlayerColor, board: ChessBoard): boolean {
    const pieces = board.getPiecesByColor(color);
    for (const piece of pieces) {
      if (CheckDetection.getLegalMoves(piece.position, color, board).length > 0) {
        return true;
      }
    }
    return false;
  }

  /**
   * Returns true if `color` is in checkmate:
   * their King is in check AND they have no legal moves.
   */
  static isCheckmate(color: PlayerColor, board: ChessBoard): boolean {
    const { isInCheck } = CheckDetection.getCheckInfo(color, board);
    if (!isInCheck) return false;
    return !CheckDetection.hasAnyLegalMove(color, board);
  }

  /**
   * Returns true if `color` is in stalemate:
   * their King is NOT in check, but they have no legal moves.
   */
  static isStalemate(color: PlayerColor, board: ChessBoard): boolean {
    const { isInCheck } = CheckDetection.getCheckInfo(color, board);
    if (isInCheck) return false;
    return !CheckDetection.hasAnyLegalMove(color, board);
  }
}
