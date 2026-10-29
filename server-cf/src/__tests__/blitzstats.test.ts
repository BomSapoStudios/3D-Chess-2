import { describe, test, expect, beforeEach } from 'vitest';

// Node modules are loaded by name so the Worker typecheck (no Node types) stays clean
const { DatabaseSync } = await import('node:sqlite' as string);
import { blitzStatsReport, purgeBlitzStats, recordBlitzStats, setupBlitzStatsTables, BLITZ_STATS } from '../blitzstats';

/** The Durable Object's SqlStorage, on Node's built-in SQLite */
function sqlStorage(): SqlStorage {
  const db = new DatabaseSync(':memory:');
  return {
    exec(query: string, ...params: unknown[]) {
      if (params.length === 0 && query.trim().split(';').filter(s => s.trim()).length > 1) { db.exec(query); return { toArray: () => [] }; }
      const stmt = db.prepare(query);
      const rows = /^\s*SELECT/i.test(query) ? stmt.all(...(params as never[])) : (stmt.run(...(params as never[])), []);
      return { toArray: () => rows };
    },
  } as unknown as SqlStorage;
}

let sql: SqlStorage;
beforeEach(() => { sql = sqlStorage(); setupBlitzStatsTables(sql); });

const rec = (over: Record<string, unknown> = {}) => ({ puzzle: 'blitz-5.3', sig: 'a1b2c3d4', seconds: 30, attempts: 1, cleared: false, moves: 0, auto: false, par: 4, ...over });
const one = (pack = 'blitz-5') => blitzStatsReport(sql, pack)[0];

describe('Queen Blitz stats', () => {
  test('time and attempts add up while unsolved, then the first clear is final', () => {
    recordBlitzStats(sql, 'a', [rec({ seconds: 30, attempts: 1 })]);
    recordBlitzStats(sql, 'a', [rec({ seconds: 95, attempts: 3 })]);
    let s = one();
    expect(s.players).toBe(1);
    expect(s.cleared).toBe(0);
    expect(s.stuck).toEqual({ players: 1, medianSeconds: 95 });
    recordBlitzStats(sql, 'a', [rec({ seconds: 140, attempts: 4, cleared: true, moves: 4 })]);
    recordBlitzStats(sql, 'a', [rec({ seconds: 500, attempts: 9, cleared: true, moves: 3 })]); // a stale or replayed record: ignored
    s = one();
    expect(s.cleared).toBe(1);
    expect(s.solveSeconds.median).toBe(140);
    expect(s.attempts.median).toBe(4);
  });

  test('a record sent twice, or an older one arriving late, never lowers the totals', () => {
    recordBlitzStats(sql, 'a', [rec({ seconds: 95, attempts: 3 })]);
    recordBlitzStats(sql, 'a', [rec({ seconds: 30, attempts: 1 })]);
    expect(one().stuck.medianSeconds).toBe(95);
  });

  test('spreads across players; each puzzle version is counted on its own', () => {
    [60, 120, 180, 240, 300].forEach((seconds, i) => recordBlitzStats(sql, `p${i}`, [rec({ seconds, attempts: i + 1, cleared: true, moves: 4, auto: i === 0 })]));
    recordBlitzStats(sql, 'p9', [rec({ seconds: 900, attempts: 7 })]);
    recordBlitzStats(sql, 'p0', [rec({ sig: 'ffffffff', seconds: 10, cleared: true, moves: 2 })]);
    const all = blitzStatsReport(sql, 'blitz-5');
    expect(all.length).toBe(2);
    const s = all.find(r => r.sig === 'a1b2c3d4')!;
    expect(s).toMatchObject({ players: 6, cleared: 5, clearRate: 0.833, autoPlaced: 1, par: 4 });
    expect(s.solveSeconds).toEqual({ p25: 120, median: 180, p75: 240, p90: 276 });
    expect(s.attempts).toEqual({ median: 3, p75: 4, mean: 3 });
    expect(s.stuck).toEqual({ players: 1, medianSeconds: 900 });
  });

  test('a pack filter only shows that pack; "daily" shows the dailies', () => {
    recordBlitzStats(sql, 'a', [rec(), rec({ puzzle: 'blitz-50.1' }), rec({ puzzle: 'daily-2026-11-01.0' })]);
    expect(blitzStatsReport(sql, 'blitz-5').map(r => r.puzzle)).toEqual(['blitz-5.3']);
    expect(blitzStatsReport(sql, 'daily').map(r => r.puzzle)).toEqual(['daily-2026-11-01.0']);
    expect(blitzStatsReport(sql).length).toBe(3);
  });

  test('bad records are skipped and impossible numbers cut down', () => {
    const taken = recordBlitzStats(sql, 'a', [rec({ puzzle: "x'; DROP TABLE blitz_stats;--" }), rec({ sig: 'nothex!!' }), rec({ seconds: 1e12, attempts: -5 }), 'junk']);
    expect(taken).toBe(1);
    const s = one();
    expect(s.stuck.medianSeconds).toBe(BLITZ_STATS.maxSeconds);
    expect(recordBlitzStats(sql, 'a', 'not a list')).toBe(0);
  });

  test('an account has a cap on puzzles; deleting it removes its records', () => {
    const many = Array.from({ length: 100 }, (_, i) => rec({ puzzle: `pack.${i}` }));
    for (let k = 0; k < BLITZ_STATS.maxPerAccount / 100; k++) recordBlitzStats(sql, 'a', many.map(r => ({ ...r, puzzle: `p${k}.${r.puzzle.split('.')[1]}` })));
    expect(recordBlitzStats(sql, 'a', [rec()])).toBe(0);
    expect(recordBlitzStats(sql, 'a', [rec({ puzzle: 'p0.1', seconds: 99 })])).toBe(1); // existing ones still update
    purgeBlitzStats(sql, 'a');
    expect(blitzStatsReport(sql).length).toBe(0);
  });
});
