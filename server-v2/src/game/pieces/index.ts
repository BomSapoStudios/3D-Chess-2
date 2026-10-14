import type { PieceType } from '../../types/game.js';
import { ChessPiece } from './ChessPiece.js';
import { King } from './King.js';
import { Queen } from './Queen.js';
import { Rook } from './Rook.js';
import { Bishop } from './Bishop.js';
import { Knight } from './Knight.js';
import { Pawn } from './Pawn.js';

export { ChessPiece, King, Queen, Rook, Bishop, Knight, Pawn };

const PIECE_INSTANCES: Record<PieceType, ChessPiece> = {
  King: new King(),
  Queen: new Queen(),
  Rook: new Rook(),
  Bishop: new Bishop(),
  Knight: new Knight(),
  Pawn: new Pawn(),
};

export function getPieceInstance(type: PieceType): ChessPiece {
  return PIECE_INSTANCES[type];
}
