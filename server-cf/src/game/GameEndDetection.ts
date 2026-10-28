import type { PlayerColor } from '../types/game.js';
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
  | { type: 'fifty_moves' }
  | null; // game continues

/** Moves by every player in a row with no capture and no pawn move, before the game is a draw */
export const QUIET_MOVES_PER_PLAYER = 50;

/** What a player has besides the King, for deciding whether anyone can still checkmate */
function forces(color: PlayerColor, board: ChessBoard): { minors: number; bishops: number; heavy: boolean } {
  const pieces = board.getPiecesByColor(color).filter(p => p.type !== 'King');
  return {
    minors: pieces.filter(p => p.type === 'Bishop' || p.type === 'Knight').length,
    bishops: pieces.filter(p => p.type === 'Bishop').length,
    // A Rook or Queen can mate, and a Pawn can become one
    heavy: pieces.some(p => p.type === 'Queen' || p.type === 'Rook' || p.type === 'Pawn'),
  };
}

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
    quietMoves = 0,
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

    // Nobody can checkmate anybody any more
    if (GameEndDetection.isInsufficientMaterial(activeColors, board)) {
      return { type: 'insufficient_material' };
    }

    // Nobody has made progress (a capture or a pawn move) in 50 moves each
    if (quietMoves >= QUIET_MOVES_PER_PLAYER * activeColors.length) {
      return { type: 'fifty_moves' };
    }

    return null;
  }

  /**
   * Nobody left can checkmate anybody (a dead position), so the game is a draw:
   *   2 players: K v K, K + one Bishop or Knight v K, K + Bishop v K + Bishop.
   *   3 or more: no Rook, Queen or Pawn anywhere and at most one Bishop or Knight on the
   *   whole board (two players' minor pieces could otherwise team up on a third King).
   */
  static isInsufficientMaterial(activeColors: PlayerColor[], board: ChessBoard): boolean {
    const all = activeColors.map(c => forces(c, board));
    if (all.some(f => f.heavy)) return false;
    if (activeColors.length > 2) return all.reduce((n, f) => n + f.minors, 0) <= 1;
    const [a, b] = all;
    if (a.minors + b.minors <= 1) return true;
    return a.minors === 1 && b.minors === 1 && a.bishops === 1 && b.bishops === 1;
  }

  /** This player alone has too little to ever checkmate: no Rook, Queen or Pawn, and at most one minor piece */
  static cannotMate(color: PlayerColor, board: ChessBoard): boolean {
    const f = forces(color, board);
    return !f.heavy && f.minors <= 1;
  }
}
