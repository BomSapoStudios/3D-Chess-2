import { PUZZLE_ID } from './economy.js';

// ─────────────────────────────────────────────────────────────────────────────
// Queen Blitz difficulty stats — how long each player really takes on each puzzle, so new packs can
// be ordered and tuned by it. Kept in the Accounts object's SQLite (deleting an account deletes them).
//
// • The app keeps one record per puzzle until its first clear: time on the clock across every
//   attempt (leaving and coming back adds to it, nothing resets it), attempts, and at the clear the
//   moves used and whether Auto-place put the queen down. Replays after the first clear aren't sent.
// • Records ride along on POST /wallet/sync (no requests of their own) and are whole totals, so
//   sending one twice changes nothing; a total only grows, and a cleared record is final.
// • A puzzle is "<pack>.<index>" plus a signature of its layout (the app's QueenBlitz.Sig and
//   Tools/QueenBlitz/stats.py make the same one), so a puzzle changed in a pack starts fresh numbers.
// • GET /blitz/stats (with the STATS_KEY secret) answers with totals per puzzle, never per player.
// ─────────────────────────────────────────────────────────────────────────────

export const BLITZ_STATS = {
  /** Records in one sync (the app sends at most 50) */
  maxPerSync: 100,
  /** Puzzles one account can have records for */
  maxPerAccount: 5000,
  /** Longer than this on one puzzle isn't believable (a phone left on the puzzle for a month) */
  maxSeconds: 30 * 24 * 3600,
  maxAttempts: 100_000,
  maxMoves: 1000,
} as const;

/** The signature of a puzzle's layout: FNV-1a, 8 hex digits */
export const PUZZLE_SIG = /^[0-9a-f]{8}$/;

export function setupBlitzStatsTables(sql: SqlStorage) {
  sql.exec(`
    CREATE TABLE IF NOT EXISTS blitz_stats (account_id TEXT, puzzle TEXT, sig TEXT, seconds REAL, attempts INTEGER, cleared INTEGER,
      moves INTEGER, auto INTEGER, par INTEGER, updated INTEGER, PRIMARY KEY (account_id, puzzle, sig));
    CREATE INDEX IF NOT EXISTS blitz_stats_puzzle ON blitz_stats (puzzle, sig);
  `);
}

export function purgeBlitzStats(sql: SqlStorage, id: string) {
  sql.exec('DELETE FROM blitz_stats WHERE account_id = ?', id);
}

export interface StatRecord { puzzle?: unknown; sig?: unknown; seconds?: unknown; attempts?: unknown; cleared?: unknown; moves?: unknown; auto?: unknown; par?: unknown }

const clamp = (v: unknown, max: number, whole = true) => {
  if (typeof v !== 'number' || !Number.isFinite(v)) return 0;
  const n = Math.max(0, Math.min(max, v));
  return whole ? Math.floor(n) : Math.round(n * 10) / 10;
};

/** The app's records; returns how many were taken. Bad ones are skipped (they never block the wallet sync). */
export function recordBlitzStats(sql: SqlStorage, id: string, records: unknown, now = Date.now()): number {
  if (!Array.isArray(records) || records.length === 0) return 0;
  let room = BLITZ_STATS.maxPerAccount - (sql.exec('SELECT COUNT(*) AS n FROM blitz_stats WHERE account_id = ?', id).toArray()[0]?.n as number ?? 0);
  let taken = 0;
  for (const r of records.slice(0, BLITZ_STATS.maxPerSync) as StatRecord[]) {
    const puzzle = String(r?.puzzle ?? '').toLowerCase();
    const sig = String(r?.sig ?? '').toLowerCase();
    if (!PUZZLE_ID.test(puzzle) || !PUZZLE_SIG.test(sig)) continue;
    const exists = sql.exec('SELECT 1 FROM blitz_stats WHERE account_id = ? AND puzzle = ? AND sig = ?', id, puzzle, sig).toArray().length > 0;
    if (!exists && room <= 0) continue;
    const cleared = r.cleared === true ? 1 : 0;
    sql.exec(`
      INSERT INTO blitz_stats (account_id, puzzle, sig, seconds, attempts, cleared, moves, auto, par, updated) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
      ON CONFLICT (account_id, puzzle, sig) DO UPDATE SET
        seconds = CASE WHEN blitz_stats.cleared = 1 THEN blitz_stats.seconds ELSE MAX(blitz_stats.seconds, excluded.seconds) END,
        attempts = CASE WHEN blitz_stats.cleared = 1 THEN blitz_stats.attempts ELSE MAX(blitz_stats.attempts, excluded.attempts) END,
        moves = CASE WHEN blitz_stats.cleared = 1 THEN blitz_stats.moves ELSE excluded.moves END,
        auto = CASE WHEN blitz_stats.cleared = 1 THEN blitz_stats.auto ELSE excluded.auto END,
        cleared = MAX(blitz_stats.cleared, excluded.cleared),
        par = excluded.par,
        updated = CASE WHEN blitz_stats.cleared = 1 THEN blitz_stats.updated ELSE excluded.updated END`,
      id, puzzle, sig, clamp(r.seconds, BLITZ_STATS.maxSeconds, false), clamp(r.attempts, BLITZ_STATS.maxAttempts),
      cleared, cleared ? clamp(r.moves, BLITZ_STATS.maxMoves) : 0, cleared && r.auto === true ? 1 : 0, clamp(r.par, 1000), now);
    if (!exists) room--;
    taken++;
  }
  return taken;
}

/** The value at fraction q of a sorted list (linear between neighbours), to the tenth */
function quantile(sorted: number[], q: number): number | null {
  if (sorted.length === 0) return null;
  const at = (sorted.length - 1) * q, lo = Math.floor(at), hi = Math.ceil(at);
  return Math.round((sorted[lo] + (sorted[hi] - sorted[lo]) * (at - lo)) * 10) / 10;
}

export interface PuzzleSummary {
  puzzle: string; sig: string; par: number;
  players: number;            // started it
  cleared: number;
  clearRate: number;          // cleared / players
  autoPlaced: number;         // clears where Auto-place put the queen down
  /** Total clock time up to the first clear, over every attempt: quartiles and 90th percentile, in seconds */
  solveSeconds: { p25: number | null; median: number | null; p75: number | null; p90: number | null };
  /** Attempts up to the first clear */
  attempts: { median: number | null; p75: number | null; mean: number | null };
  /** Players who haven't cleared it (given up or still at it): their time so far */
  stuck: { players: number; medianSeconds: number | null };
  lastUpdated: number;
}

/**
 * Totals per puzzle version, for tuning packs. pack: only that pack's puzzles ("blitz-5", or
 * "daily" for every daily). Never reveals a player: just counts and spreads.
 */
export function blitzStatsReport(sql: SqlStorage, pack = ''): PuzzleSummary[] {
  const p = pack.toLowerCase();
  const where = !p ? '' : p === 'daily' ? "WHERE puzzle LIKE 'daily-%'" : 'WHERE puzzle LIKE ?';
  const params = p && p !== 'daily' ? [p.replace(/[%_]/g, '') + '.%'] : [];
  const rows = sql.exec(`SELECT puzzle, sig, seconds, attempts, cleared, auto, par, updated FROM blitz_stats ${where} ORDER BY puzzle, sig`, ...params).toArray() as
    { puzzle: string; sig: string; seconds: number; attempts: number; cleared: number; auto: number; par: number; updated: number }[];
  const groups = new Map<string, typeof rows>();
  for (const r of rows) {
    const key = `${r.puzzle} ${r.sig}`;
    const g = groups.get(key);
    if (g) g.push(r); else groups.set(key, [r]);
  }
  const out: PuzzleSummary[] = [];
  for (const g of groups.values()) {
    const done = g.filter(r => r.cleared === 1);
    const stuck = g.filter(r => r.cleared !== 1);
    const secs = done.map(r => r.seconds).sort((a, b) => a - b);
    const tries = done.map(r => r.attempts).sort((a, b) => a - b);
    out.push({
      puzzle: g[0].puzzle, sig: g[0].sig, par: Math.max(...g.map(r => r.par)),
      players: g.length, cleared: done.length, clearRate: Math.round(done.length / g.length * 1000) / 1000,
      autoPlaced: done.filter(r => r.auto === 1).length,
      solveSeconds: { p25: quantile(secs, 0.25), median: quantile(secs, 0.5), p75: quantile(secs, 0.75), p90: quantile(secs, 0.9) },
      attempts: { median: quantile(tries, 0.5), p75: quantile(tries, 0.75), mean: tries.length ? Math.round(tries.reduce((a, b) => a + b, 0) / tries.length * 10) / 10 : null },
      stuck: { players: stuck.length, medianSeconds: quantile(stuck.map(r => r.seconds).sort((a, b) => a - b), 0.5) },
      lastUpdated: Math.max(...g.map(r => r.updated)),
    });
  }
  return out;
}
