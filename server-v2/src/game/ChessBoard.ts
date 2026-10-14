import { v4 as uuidv4 } from 'uuid';
import type {
  BoardState, BoardPosition, ChessPieceState,
  PlayerColor, PieceType, BoardSize,
} from '../types/game.js';
import { BOARD_DIMENSIONS } from '../types/game.js';

// ─────────────────────────────────────────────────────────────────────────────
// ChessBoard — authoritative 3D board state
// Ported from C# ChessBoard.cs
// ─────────────────────────────────────────────────────────────────────────────

export class ChessBoard {
  readonly size: number;
  readonly boardSize: BoardSize;
  private board: BoardState;

  constructor(boardSize: BoardSize) {
    this.boardSize = boardSize;
    this.size = BOARD_DIMENSIONS[boardSize];
    this.board = this.createEmptyBoard();
  }

  private createEmptyBoard(): BoardState {
    const s = this.size;
    return Array.from({ length: s }, () =>
      Array.from({ length: s }, () =>
        Array<ChessPieceState | null>(s).fill(null)
      )
    );
  }

  // ── Validation ──────────────────────────────────────────────────────────────

  isInBounds(pos: BoardPosition): boolean {
    return (
      pos.x >= 0 && pos.x < this.size &&
      pos.y >= 0 && pos.y < this.size &&
      pos.z >= 0 && pos.z < this.size
    );
  }

  // ── Piece access ────────────────────────────────────────────────────────────

  getPiece(pos: BoardPosition): ChessPieceState | null {
    if (!this.isInBounds(pos)) return null;
    return this.board[pos.x][pos.y][pos.z];
  }

  setPiece(pos: BoardPosition, piece: ChessPieceState | null): void {
    if (!this.isInBounds(pos)) throw new Error(`Out of bounds: ${JSON.stringify(pos)}`);
    this.board[pos.x][pos.y][pos.z] = piece;
  }

  isEmpty(pos: BoardPosition): boolean {
    return this.getPiece(pos) === null;
  }

  isOccupiedByEnemy(pos: BoardPosition, myColor: PlayerColor): boolean {
    const piece = this.getPiece(pos);
    return piece !== null && piece.color !== myColor;
  }

  isOccupiedByFriend(pos: BoardPosition, myColor: PlayerColor): boolean {
    const piece = this.getPiece(pos);
    return piece !== null && piece.color === myColor;
  }

  // ── Piece manipulation ──────────────────────────────────────────────────────

  placePiece(pos: BoardPosition, type: PieceType, color: PlayerColor): ChessPieceState {
    const piece: ChessPieceState = {
      id: uuidv4(),
      type,
      color,
      position: { ...pos },
    };
    this.setPiece(pos, piece);
    return piece;
  }

  movePiece(from: BoardPosition, to: BoardPosition): ChessPieceState | null {
    const piece = this.getPiece(from);
    if (!piece) return null;
    const captured = this.getPiece(to);
    this.setPiece(from, null);
    const moved: ChessPieceState = { ...piece, position: { ...to } };
    this.setPiece(to, moved);
    return captured;
  }

  removePiece(pos: BoardPosition): ChessPieceState | null {
    const piece = this.getPiece(pos);
    this.setPiece(pos, null);
    return piece;
  }

  // ── Queries ─────────────────────────────────────────────────────────────────

  getAllPieces(): ChessPieceState[] {
    const pieces: ChessPieceState[] = [];
    for (let x = 0; x < this.size; x++)
      for (let y = 0; y < this.size; y++)
        for (let z = 0; z < this.size; z++) {
          const p = this.board[x][y][z];
          if (p) pieces.push(p);
        }
    return pieces;
  }

  getPiecesByColor(color: PlayerColor): ChessPieceState[] {
    return this.getAllPieces().filter(p => p.color === color);
  }

  findKing(color: PlayerColor): ChessPieceState | null {
    return this.getPiecesByColor(color).find(p => p.type === 'King') ?? null;
  }

  // ── Deep clone ──────────────────────────────────────────────────────────────
  // Used by the minimax / check-simulation logic to test moves without mutating
  // the authoritative board.

  clone(): ChessBoard {
    const copy = new ChessBoard(this.boardSize);
    for (let x = 0; x < this.size; x++)
      for (let y = 0; y < this.size; y++)
        for (let z = 0; z < this.size; z++) {
          const p = this.board[x][y][z];
          copy.board[x][y][z] = p ? { ...p, position: { ...p.position } } : null;
        }
    return copy;
  }

  // ── Serialization ────────────────────────────────────────────────────────────

  toState(): BoardState {
    // Return a deep copy of the board array for transmission
    return this.board.map(plane =>
      plane.map(row =>
        row.map(cell => cell ? { ...cell, position: { ...cell.position } } : null)
      )
    );
  }

  static fromState(state: BoardState, boardSize: BoardSize): ChessBoard {
    const board = new ChessBoard(boardSize);
    const s = board.size;
    for (let x = 0; x < s; x++)
      for (let y = 0; y < s; y++)
        for (let z = 0; z < s; z++) {
          const cell = state[x]?.[y]?.[z] ?? null;
          board.board[x][y][z] = cell ? { ...cell, position: { ...cell.position } } : null;
        }
    return board;
  }

  // ── Board hash (for threefold repetition detection) ──────────────────────────

  hash(): string {
    const pieces = this.getAllPieces()
      .sort((a, b) => {
        const ka = `${a.position.x},${a.position.y},${a.position.z}`;
        const kb = `${b.position.x},${b.position.y},${b.position.z}`;
        return ka.localeCompare(kb);
      })
      .map(p => `${p.type}:${p.color}:${p.position.x},${p.position.y},${p.position.z}`)
      .join('|');
    return pieces;
  }

  // ── Debug ───────────────────────────────────────────────────────────────────

  debugPrint(): string {
    const pieces = this.getAllPieces();
    return pieces.map(p =>
      `${p.color} ${p.type} @ (${p.position.x},${p.position.y},${p.position.z})`
    ).join('\n');
  }
}
