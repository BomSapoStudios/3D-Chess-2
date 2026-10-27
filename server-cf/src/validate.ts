import type { BoardPosition } from './types/game.js';

// Checks on what clients send, before it reaches the rules engine

/** A board cell from a client: three whole numbers, or null */
export function asPosition(value: unknown): BoardPosition | null {
  if (!value || typeof value !== 'object') return null;
  const { x, y, z } = value as Record<string, unknown>;
  if (![x, y, z].every(n => Number.isInteger(n) && (n as number) >= 0 && (n as number) < 16)) return null;
  return { x: x as number, y: y as number, z: z as number };
}

/**
 * Messages per socket per minute. A normal game sends a few; the most is a host auto-placing every
 * robot seat's pieces at once (about 100). This is a backstop for a client stuck in a loop (the kind
 * of bug behind the October 2026 outage): past it the socket is closed and the app reconnects.
 */
const MESSAGES_PER_MINUTE = 1200;
const messageCounts = new WeakMap<object, { start: number; count: number }>();

export function floodedBy(ws: object, now = Date.now()): boolean {
  let c = messageCounts.get(ws);
  if (!c || now - c.start >= 60_000) messageCounts.set(ws, c = { start: now, count: 0 });
  return ++c.count > MESSAGES_PER_MINUTE;
}
