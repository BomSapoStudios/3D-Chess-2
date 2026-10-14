import type { PlayerColor, PieceType } from '../types/game.js';
import type { ChessBoard } from './ChessBoard.js';
import { CheckDetection } from './CheckDetection.js';

// ─────────────────────────────────────────────────────────────────────────────
// GameEndDetection — ported from C# GameEndDetectionManager.cs
// ─────────────────────────────────────────────────────────────────────────────

export type GameEndResult =
  | { type: 'checkmate'; loser: PlayerColor }
  | { type: 'stalemate' }
  | { type: 'insufficient_material' }
  | { type: 'threefold_repetition' }
  | null; // game continues

// Piece values for insufficient material calculation
const PIECE_VALUES: Record<PieceType, number> = {
  King: 0, // King doesn't count for material
  Queen: 9,
  Rook: 5,
  Bishop: 3,
  Knight: 3,
  Pawn: 1,
};

export class GameEndDetection {
  /**
   * Check all active players for end conditions.
   * Returns the first end result found, or null if game continues.
   */
  static check(
    activeColors: PlayerColor[],
    currentTurn: PlayerColor,
    board: ChessBoard,
    positionHistory: string[],
  ): GameEndResult {
    // Threefold repetition (board hash repeated 3+ times)
    const currentHash = board.hash();
    const repetitions = positionHistory.filter(h => h === currentHash).length;
    if (repetitions >= 3) {
      return { type: 'threefold_repetition' };
    }

    // Check current player for checkmate or stalemate
    if (CheckDetection.isCheckmate(currentTurn, board)) {
      return { type: 'checkmate', loser: currentTurn };
    }
    if (CheckDetection.isStalemate(currentTurn, board)) {
      return { type: 'stalemate' };
    }

    // Insufficient material (only relevant in 2-player games)
    if (activeColors.length === 2) {
      if (GameEndDetection.isInsufficientMaterial(activeColors, board)) {
        return { type: 'insufficient_material' };
      }
    }

    return null;
  }

  /**
   * Insufficient material: neither side can force checkmate.
   * Standard cases: K vs K, K+B vs K, K+N vs K, K+B vs K+B (same color bishops).
   */
  static isInsufficientMaterial(activeColors: PlayerColor[], board: ChessBoard): boolean {
    if (activeColors.length !== 2) return false;

    const [colorA, colorB] = activeColors;
    const piecesA = board.getPiecesByColor(colorA).filter(p => p.type !== 'King');
    const piecesB = board.getPiecesByColor(colorB).filter(p => p.type !== 'King');

    const matA = piecesA.reduce((s, p) => s + PIECE_VALUES[p.type], 0);
    const matB = piecesB.reduce((s, p) => s + PIECE_VALUES[p.type], 0);

    // K vs K
    if (matA === 0 && matB === 0) return true;

    // K+minor vs K
    if ((matA <= 3 && matB === 0) || (matA === 0 && matB <= 3)) return true;

    // K+B vs K+B (simplified — always draw for now; a full check would verify same-color bishops)
    if (piecesA.length === 1 && piecesB.length === 1 &&
        piecesA[0].type === 'Bishop' && piecesB[0].type === 'Bishop') {
      return true;
    }

    return false;
  }
}
