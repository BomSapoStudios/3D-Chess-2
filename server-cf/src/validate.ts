import type { BoardPosition } from './types/game.js';

// Checks on what clients send, before it reaches the rules engine

/** A board cell from a client: three whole numbers, or null */
export function asPosition(value: unknown): BoardPosition | null {
  if (!value || typeof value !== 'object') return null;
  const { x, y, z } = value as Record<string, unknown>;
  if (![x, y, z].every(n => Number.isInteger(n) && (n as number) >= 0 && (n as number) < 16)) return null;
  return { x: x as number, y: y as number, z: z as number };
}
