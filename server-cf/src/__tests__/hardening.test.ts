import { describe, test, expect } from 'vitest';
import { asPosition } from '../validate';
import { trustedKeyUrl } from '../gamecenter';
import { GameEngine } from '../game/GameEngine';
import { CheckDetection } from '../game/CheckDetection';
import type { PlayerColor } from '../types/game';

describe('asPosition', () => {
  test('accepts three whole numbers', () => {
    expect(asPosition({ x: 0, y: 3, z: 7 })).toEqual({ x: 0, y: 3, z: 7 });
  });
  test('drops extra fields', () => {
    expect(asPosition({ x: 1, y: 1, z: 1, junk: 'x'.repeat(1000) })).toEqual({ x: 1, y: 1, z: 1 });
  });
  test('refuses strings, fractions, arrays, negatives and missing cells', () => {
    for (const bad of [null, undefined, 'a', { x: '1', y: 1, z: 1 }, { x: 1.5, y: 1, z: 1 }, { x: [1], y: 1, z: 1 }, { x: -1, y: 0, z: 0 }, { x: 1, y: 1 }, { x: 99, y: 0, z: 0 }]) {
      expect(asPosition(bad)).toBeNull();
    }
  });
});

describe('Game Center key URL', () => {
  test('only Apple\'s Game Center key host', () => {
    expect(trustedKeyUrl('https://static.gc.apple.com/public-key/gc-prod-9.cer')).toBe(true);
    expect(trustedKeyUrl('http://static.gc.apple.com/public-key/gc-prod-9.cer')).toBe(false);
    expect(trustedKeyUrl('https://www.apple.com/public-key/gc-prod-9.cer')).toBe(false);
    expect(trustedKeyUrl('https://static.gc.apple.com/public-key/gc-prod-9.cer?x=1')).toBe(false);
    expect(trustedKeyUrl('https://static.gc.apple.com.evil.com/public-key/a.cer')).toBe(false);
  });
});

describe('Draw offers', () => {
  function game() {
    const engine = new GameEngine('DRAW01', { boardSize: '4x4x4', playerCount: 2 });
    engine.addPlayer('u1', 'A', 'White', 'human');
    engine.addPlayer('u2', 'B', 'Black', 'human');
    engine.startPlacement();
    engine.placePiece('White', 'King', { x: 0, y: 1, z: 1 });
    engine.placePiece('White', 'Rook', { x: 0, y: 2, z: 2 });
    engine.placePiece('Black', 'King', { x: 3, y: 1, z: 1 });
    engine.placePiece('Black', 'Rook', { x: 3, y: 2, z: 2 });
    for (const p of (engine as unknown as { players: { placementComplete: boolean }[] }).players) p.placementComplete = true;
    engine.startGame();
    return engine;
  }
  function anyMove(engine: GameEngine, color: PlayerColor) {
    const board = (engine as unknown as { board: { getPiecesByColor(c: PlayerColor): { position: { x: number; y: number; z: number } }[] } }).board;
    for (const p of board.getPiecesByColor(color)) {
      const moves = CheckDetection.getLegalMoves(p.position, color, board as never);
      if (moves.length) return engine.executeMove(color, p.position, moves[0]);
    }
    throw new Error('no move');
  }

  test('moving instead of answering turns the offer down', () => {
    const engine = game();
    const first = engine.getState().currentTurn!;
    const other: PlayerColor = first === 'White' ? 'Black' : 'White';
    engine.offerDraw(first);
    expect(anyMove(engine, first).success).toBe(true);   // the offerer's own move keeps it open
    expect(engine.drawOfferFrom).toBe(first);
    expect(anyMove(engine, other).success).toBe(true);   // the other player moved on
    expect(engine.drawOfferFrom).toBeNull();
    expect(engine.respondDraw(other, true)).toBeNull();
  });

  test('an old offer cannot be accepted after the game ended', () => {
    const engine = game();
    const first = engine.getState().currentTurn!;
    const other: PlayerColor = first === 'White' ? 'Black' : 'White';
    engine.offerDraw(first);
    engine.resign(other);
    expect(engine.respondDraw(other, true)).toBeNull();
  });
});
