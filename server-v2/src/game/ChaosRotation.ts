import type { ChaosRotation, ChaosRotationType, ChessPieceState, BoardPosition } from '../types/game.js';
import type { ChessBoard } from './ChessBoard.js';

// ─────────────────────────────────────────────────────────────────────────────
// ChaosRotation — ported from C# ChaosRotationManager.cs
//
// Performs Rubik's-cube-style rotations on the 3D chess board.
// Three types: face (outer layer), layer (internal slice), diagonal.
// ─────────────────────────────────────────────────────────────────────────────

export class ChaosRotationEngine {
  /**
   * Generate a random chaos rotation and apply it to the board.
   * Returns the rotation descriptor (for broadcast to clients).
   */
  static applyRandom(board: ChessBoard): ChaosRotation {
    const types: ChaosRotationType[] = ['face', 'layer', 'diagonal'];
    const axes: ('x' | 'y' | 'z')[] = ['x', 'y', 'z'];
    const type = types[Math.floor(Math.random() * types.length)];
    const axis = axes[Math.floor(Math.random() * axes.length)];
    const maxIndex = board.size - 1;
    const index = Math.floor(Math.random() * board.size);
    const clockwise = Math.random() < 0.5;

    return ChaosRotationEngine.apply(board, type, axis, index, clockwise);
  }

  static apply(
    board: ChessBoard,
    type: ChaosRotationType,
    axis: 'x' | 'y' | 'z',
    index: number,
    clockwise: boolean,
  ): ChaosRotation {
    const positionMap: { from: BoardPosition; to: BoardPosition }[] = [];
    const affectedPositions: BoardPosition[] = [];

    // Collect all pieces on the board before rotation
    const allPieces = board.getAllPieces();

    // Build the position map for this rotation
    const map = ChaosRotationEngine.buildPositionMap(board.size, type, axis, index, clockwise);

    // Apply the mapping: move pieces to new positions
    // First, snapshot pieces that will move
    const movedPieces: { piece: ChessPieceState; newPos: BoardPosition }[] = [];
    const clearedPositions = new Set<string>();

    for (const [fromKey, toPos] of map.entries()) {
      const fromPos = ChaosRotationEngine.keyToPos(fromKey);
      const piece = board.getPiece(fromPos);
      if (piece) {
        movedPieces.push({ piece, newPos: toPos });
      }
      positionMap.push({ from: fromPos, to: toPos });
      affectedPositions.push(fromPos);
      clearedPositions.add(fromKey);
    }

    // Clear all affected cells
    for (const key of clearedPositions) {
      const pos = ChaosRotationEngine.keyToPos(key);
      board.setPiece(pos, null);
    }

    // Place pieces at new positions
    for (const { piece, newPos } of movedPieces) {
      board.setPiece(newPos, { ...piece, position: { ...newPos } });
    }

    return { type, axis, index, clockwise, affectedPositions, positionMap };
  }

  /**
   * Build a map from position key → new BoardPosition for a rotation.
   */
  private static buildPositionMap(
    size: number,
    type: ChaosRotationType,
    axis: 'x' | 'y' | 'z',
    index: number,
    clockwise: boolean,
  ): Map<string, BoardPosition> {
    const map = new Map<string, BoardPosition>();

    if (type === 'face' || type === 'layer') {
      // Rotate a 2D slice of the cube around the given axis
      // The slice is at `index` along `axis`
      for (let a = 0; a < size; a++) {
        for (let b = 0; b < size; b++) {
          const from = ChaosRotationEngine.slicePos(axis, index, a, b);
          const [newA, newB] = clockwise
            ? [b, size - 1 - a]
            : [size - 1 - b, a];
          const to = ChaosRotationEngine.slicePos(axis, index, newA, newB);
          map.set(ChaosRotationEngine.posToKey(from), to);
        }
      }
    } else {
      // Diagonal rotation: rotate around a body diagonal of the cube
      // Simplified: rotate around the main diagonal (0,0,0)→(n,n,n)
      // This cycles (x,y,z) → (z,x,y) or the reverse
      for (let x = 0; x < size; x++) {
        for (let y = 0; y < size; y++) {
          for (let z = 0; z < size; z++) {
            const from: BoardPosition = { x, y, z };
            const to: BoardPosition = clockwise
              ? { x: z, y: x, z: y }
              : { x: y, y: z, z: x };
            map.set(ChaosRotationEngine.posToKey(from), to);
          }
        }
      }
    }

    return map;
  }

  private static slicePos(
    axis: 'x' | 'y' | 'z',
    index: number,
    a: number,
    b: number,
  ): BoardPosition {
    if (axis === 'x') return { x: index, y: a, z: b };
    if (axis === 'y') return { x: a, y: index, z: b };
    return { x: a, y: b, z: index };
  }

  private static posToKey(pos: BoardPosition): string {
    return `${pos.x},${pos.y},${pos.z}`;
  }

  private static keyToPos(key: string): BoardPosition {
    const [x, y, z] = key.split(',').map(Number);
    return { x, y, z };
  }
}
