// ─────────────────────────────────────────────────────────────────────────────
// Ratings, stats and walk-outs, kept in the Accounts object's SQLite.
//
// • Every finished online game with at least two people in it is recorded (win/loss records,
//   head to head). Only ranked ones move ratings: Find Match games on a clock without Chaos Mode.
// • Two Elo ratings per player, starting at 1000: Duel (2 players) and Melee (4 and 6).
//   Melee scores every pair of players by finishing order, averaged over the opponents.
// • K is 40 for a player's first 10 games in a mode, 20 after. The same two players only
//   move each other's ratings in their first 5 games together each day.
// • Walking out costs a loss; the third walk-out within a day locks the player out of
//   Find Match for 15 minutes, doubling each time (up to 4 hours) until a week passes
//   without one.
// ─────────────────────────────────────────────────────────────────────────────

export type Mode = 'duel' | 'melee';

export interface ResultPlayer {
  userId: string;
  color: string;
  /** 1 = winner; players who tie share a place */
  place: number;
  walkout: boolean;
  /** Pieces this player took and lost, and what they took (piece type → count) */
  taken?: number;
  lost?: number;
  takenTypes?: Record<string, number>;
}

export interface GameResult {
  roomCode: string;
  rated: boolean;
  players: ResultPlayer[];
  boardSize: string;
  playerCount: number;
  pace: string;
  endReason: string;
  moves: number;
  durationMs: number;
}

export interface RatingChange {
  mode: Mode;
  before: number;
  after: number;
  games: number;
  tier: string;
  tierBefore: string;
}

const START = 1000;
const PROVISIONAL_GAMES = 10;
const PAIR_GAMES_PER_DAY = 5;
const MIN_GAMES_FOR_TIER = 5;
const MIN_GAMES_FOR_BOARD = 5;
const DAY = 24 * 3600_000;
const WALKOUTS_FOR_COOLDOWN = 3;
const COOLDOWN_BASE_MS = 15 * 60_000;
const COOLDOWN_MAX_MS = 4 * 3600_000;
const COOLDOWN_RESET_MS = 7 * DAY;

const DRAWS = new Set(['stalemate', 'draw_agreement', 'threefold_repetition', 'insufficient_material']);

export const TIERS: [string, number][] = [['King', 1600], ['Queen', 1400], ['Rook', 1250], ['Bishop', 1100], ['Knight', 950], ['Pawn', -Infinity]];

export function tierOf(rating: number, games: number): string {
  if (games < MIN_GAMES_FOR_TIER) return 'Pawn';
  return TIERS.find(([, min]) => rating >= min)![0];
}

export function setupTables(sql: SqlStorage): void {
  sql.exec(`
    CREATE TABLE IF NOT EXISTS ratings (account_id TEXT, mode TEXT, rating REAL, games INTEGER, best REAL, PRIMARY KEY (account_id, mode));
    CREATE TABLE IF NOT EXISTS games_played (id INTEGER PRIMARY KEY AUTOINCREMENT, room_code TEXT, ended_at INTEGER, mode TEXT, board TEXT, player_count INTEGER, pace TEXT, end_reason TEXT, moves INTEGER, duration_ms INTEGER, rated INTEGER);
    CREATE TABLE IF NOT EXISTS player_games (account_id TEXT, game_id INTEGER, color TEXT, place INTEGER, result TEXT, walkout INTEGER, rating_before REAL, rating_after REAL, PRIMARY KEY (account_id, game_id));
    CREATE INDEX IF NOT EXISTS player_games_by_game ON player_games (game_id);
    CREATE TABLE IF NOT EXISTS pair_days (pair TEXT, day INTEGER, games INTEGER, PRIMARY KEY (pair, day));
    CREATE TABLE IF NOT EXISTS walkouts (account_id TEXT, at INTEGER);
    CREATE INDEX IF NOT EXISTS walkouts_by_account ON walkouts (account_id, at);
    CREATE TABLE IF NOT EXISTS cooldowns (account_id TEXT PRIMARY KEY, until INTEGER, level INTEGER, last_walkout INTEGER);
  `);
  // Captures came later: add their columns to existing tables
  for (const column of ['taken INTEGER DEFAULT 0', 'lost INTEGER DEFAULT 0', "taken_types TEXT DEFAULT '{}'"]) {
    try { sql.exec(`ALTER TABLE player_games ADD COLUMN ${column}`); } catch { /* already there */ }
  }
}

function ratingOf(sql: SqlStorage, userId: string, mode: Mode): { rating: number; games: number; best: number } {
  const row = sql.exec('SELECT rating, games, best FROM ratings WHERE account_id = ? AND mode = ?', userId, mode).toArray()[0];
  return row ? { rating: row.rating as number, games: row.games as number, best: row.best as number } : { rating: START, games: 0, best: START };
}

function bestTier(sql: SqlStorage, userId: string): string {
  const d = ratingOf(sql, userId, 'duel'), m = ratingOf(sql, userId, 'melee');
  const tiers = [tierOf(d.rating, d.games), tierOf(m.rating, m.games)];
  return TIERS.find(([name]) => tiers.includes(name))![0];
}

/** Record a finished game; rate it if it counts. Returns each player's rating change. */
export function recordGame(sql: SqlStorage, r: GameResult, now: number): Record<string, RatingChange> {
  const players = r.players.filter(p => p.userId && !p.userId.startsWith('ai:'));
  for (const p of players) if (p.walkout) recordWalkout(sql, p.userId, now);
  if (players.length < 2 || r.endReason === 'abandoned') return {}; // nobody played it out: not kept
  const rated = r.rated;

  const mode: Mode = r.playerCount === 2 ? 'duel' : 'melee';
  const draw = DRAWS.has(r.endReason);
  sql.exec(
    'INSERT INTO games_played (room_code, ended_at, mode, board, player_count, pace, end_reason, moves, duration_ms, rated) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)',
    r.roomCode, now, mode, r.boardSize, r.playerCount, r.pace, r.endReason, r.moves, r.durationMs, rated ? 1 : 0,
  );
  const gameId = sql.exec('SELECT last_insert_rowid() AS id').one().id as number;

  if (!rated) {
    // Unranked (games with friends, untimed or Chaos games): kept for win/loss records and
    // head to head, but nobody's rating moves
    for (const p of players) {
      const current = ratingOf(sql, p.userId, mode).rating;
      const result = draw && p.place === 1 ? 'draw' : p.place === 1 ? 'win' : 'loss';
      sql.exec('INSERT OR REPLACE INTO player_games (account_id, game_id, color, place, result, walkout, rating_before, rating_after, taken, lost, taken_types) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)',
        p.userId, gameId, p.color, p.place, result, p.walkout ? 1 : 0, current, current, p.taken ?? 0, p.lost ?? 0, JSON.stringify(p.takenTypes ?? {}));
    }
    return {};
  }

  // Elo, pair by pair
  const before = new Map(players.map(p => [p.userId, ratingOf(sql, p.userId, mode)]));
  const delta = new Map(players.map(p => [p.userId, 0]));
  const day = Math.floor(now / DAY);
  for (let i = 0; i < players.length; i++) {
    for (let j = i + 1; j < players.length; j++) {
      const a = players[i], b = players[j];
      const pair = [a.userId, b.userId].sort().join('|');
      const count = (sql.exec('SELECT games FROM pair_days WHERE pair = ? AND day = ?', pair, day).toArray()[0]?.games as number | undefined) ?? 0;
      sql.exec('INSERT OR REPLACE INTO pair_days (pair, day, games) VALUES (?, ?, ?)', pair, day, count + 1);
      if (count >= PAIR_GAMES_PER_DAY) continue; // the same two players over and over: no more rating today
      const ra = before.get(a.userId)!, rb = before.get(b.userId)!;
      const expectedA = 1 / (1 + Math.pow(10, (rb.rating - ra.rating) / 400));
      const scoreA = a.place < b.place ? 1 : a.place === b.place ? 0.5 : 0;
      const ka = ra.games < PROVISIONAL_GAMES ? 40 : 20, kb = rb.games < PROVISIONAL_GAMES ? 40 : 20;
      const share = players.length - 1;
      delta.set(a.userId, delta.get(a.userId)! + ka * (scoreA - expectedA) / share);
      delta.set(b.userId, delta.get(b.userId)! + kb * ((1 - scoreA) - (1 - expectedA)) / share);
    }
  }
  sql.exec('DELETE FROM pair_days WHERE day < ?', day - 1);

  const changes: Record<string, RatingChange> = {};
  for (const p of players) {
    const old = before.get(p.userId)!;
    const tierBefore = bestTier(sql, p.userId);
    const after = Math.round((old.rating + delta.get(p.userId)!) * 10) / 10;
    const games = old.games + 1;
    sql.exec('INSERT OR REPLACE INTO ratings (account_id, mode, rating, games, best) VALUES (?, ?, ?, ?, ?)',
      p.userId, mode, after, games, Math.max(old.best, after));
    const result = draw && p.place === 1 ? 'draw' : p.place === 1 ? 'win' : 'loss';
    sql.exec('INSERT OR REPLACE INTO player_games (account_id, game_id, color, place, result, walkout, rating_before, rating_after, taken, lost, taken_types) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)',
      p.userId, gameId, p.color, p.place, result, p.walkout ? 1 : 0, old.rating, after, p.taken ?? 0, p.lost ?? 0, JSON.stringify(p.takenTypes ?? {}));
    changes[p.userId] = { mode, before: Math.round(old.rating), after: Math.round(after), games, tier: bestTier(sql, p.userId), tierBefore };
  }
  return changes;
}

/** A player left a game for good. The third time in a day starts a Find Match cooldown. */
export function recordWalkout(sql: SqlStorage, userId: string, now: number): void {
  sql.exec('INSERT INTO walkouts (account_id, at) VALUES (?, ?)', userId, now);
  sql.exec('DELETE FROM walkouts WHERE at < ?', now - 30 * DAY);
  const recent = sql.exec('SELECT COUNT(*) AS n FROM walkouts WHERE account_id = ? AND at >= ?', userId, now - DAY).one().n as number;
  const row = sql.exec('SELECT until, level, last_walkout FROM cooldowns WHERE account_id = ?', userId).toArray()[0];
  let level = (row?.level as number | undefined) ?? -1;
  const last = (row?.last_walkout as number | undefined) ?? 0;
  if (now - last > COOLDOWN_RESET_MS) level = -1;
  let until = (row?.until as number | undefined) ?? 0;
  if (recent >= WALKOUTS_FOR_COOLDOWN) {
    level++;
    until = now + Math.min(COOLDOWN_BASE_MS * Math.pow(2, level), COOLDOWN_MAX_MS);
  }
  sql.exec('INSERT OR REPLACE INTO cooldowns (account_id, until, level, last_walkout) VALUES (?, ?, ?, ?)', userId, until, Math.max(level, -1), now);
}

/** When this player can use Find Match again (0 = now) */
export function cooldownUntil(sql: SqlStorage, userId: string, now: number): number {
  const until = sql.exec('SELECT until FROM cooldowns WHERE account_id = ?', userId).toArray()[0]?.until as number | undefined;
  return until && until > now ? until : 0;
}

/** Everything the profile screen shows. With a viewer, also their record against this player. */
export function stats(sql: SqlStorage, userId: string, viewerId: string | null, nameOf: (id: string) => string) {
  const duel = ratingOf(sql, userId, 'duel'), melee = ratingOf(sql, userId, 'melee');
  const rows = sql.exec(`
    SELECT pg.game_id, pg.result, pg.walkout, pg.rating_before, pg.rating_after, pg.taken, pg.lost, pg.taken_types, g.ended_at, g.mode, g.board, g.player_count, g.end_reason
    FROM player_games pg JOIN games_played g ON g.id = pg.game_id
    WHERE pg.account_id = ? ORDER BY g.ended_at ASC`, userId).toArray();

  let wins = 0, losses = 0, draws = 0, walkouts = 0, streak = 0, bestStreak = 0;
  const endings: Record<string, number> = { checkmate: 0, resignation: 0, timeout: 0, walkout: 0, draw: 0 };
  const setups = new Map<string, number>();
  let taken = 0, lost = 0;
  const takenTypes: Record<string, number> = {};
  for (const r of rows) {
    taken += (r.taken as number) ?? 0;
    lost += (r.lost as number) ?? 0;
    try { for (const [type, n] of Object.entries(JSON.parse((r.taken_types as string) || '{}') as Record<string, number>)) takenTypes[type] = (takenTypes[type] ?? 0) + n; } catch { /* old row */ }
    const result = r.result as string;
    if (result === 'win') { wins++; streak++; bestStreak = Math.max(bestStreak, streak); }
    else { streak = 0; if (result === 'draw') draws++; else losses++; }
    if (r.walkout) { walkouts++; endings.walkout++; }
    else if (result === 'draw') endings.draw++;
    else if (r.end_reason === 'checkmate') endings.checkmate++;
    else if (r.end_reason === 'timeout') endings.timeout++;
    else endings.resignation++;
    const setup = `${r.board} · ${r.player_count} players`;
    setups.set(setup, (setups.get(setup) ?? 0) + 1);
  }
  const favorite = [...setups.entries()].sort((a, b) => b[1] - a[1])[0]?.[0] ?? null;

  const recent = rows.slice(-10).reverse().map(r => {
    const opponents = sql.exec('SELECT account_id FROM player_games WHERE game_id = ? AND account_id != ?', r.game_id, userId).toArray()
      .map(o => nameOf(o.account_id as string));
    return {
      endedAt: r.ended_at as number, mode: r.mode as string, result: r.result as string, endReason: r.walkout ? 'walkout' : r.end_reason as string,
      opponents, change: Math.round((r.rating_after as number) - (r.rating_before as number)),
    };
  });

  let headToHead = null;
  if (viewerId && viewerId !== userId) {
    // From the viewer's side: how they've done against this player
    const shared = sql.exec(`
      SELECT a.place AS mine, b.place AS theirs FROM player_games a JOIN player_games b ON a.game_id = b.game_id
      WHERE a.account_id = ? AND b.account_id = ?`, viewerId, userId).toArray();
    headToHead = {
      wins: shared.filter(s => (s.mine as number) < (s.theirs as number)).length,
      losses: shared.filter(s => (s.mine as number) > (s.theirs as number)).length,
      draws: shared.filter(s => s.mine === s.theirs).length,
    };
  }

  const total = rows.length;
  return {
    userId,
    name: nameOf(userId),
    tier: bestTier(sql, userId),
    duel: { rating: Math.round(duel.rating), best: Math.round(duel.best), games: duel.games, tier: tierOf(duel.rating, duel.games) },
    melee: { rating: Math.round(melee.rating), best: Math.round(melee.best), games: melee.games, tier: tierOf(melee.rating, melee.games) },
    games: total, wins, losses, draws,
    winRate: total ? Math.round(wins * 100 / total) : 0,
    streak, bestStreak,
    walkoutRate: total ? Math.round(walkouts * 100 / total) : 0,
    endings, favorite, recent, headToHead,
    captures: { taken, lost, mostTaken: Object.entries(takenTypes).sort((a, b) => b[1] - a[1])[0]?.[0] ?? null },
  };
}

/** Top players in a mode, everyone or just these ids */
export function leaderboard(sql: SqlStorage, mode: Mode, among: string[] | null, nameOf: (id: string) => string, limit = 50) {
  const rows = among
    ? among.length === 0 ? [] : sql.exec(`SELECT account_id, rating, games FROM ratings WHERE mode = ? AND account_id IN (${among.map(() => '?').join(',')}) ORDER BY rating DESC LIMIT ?`, mode, ...among, limit).toArray()
    : sql.exec('SELECT account_id, rating, games FROM ratings WHERE mode = ? AND games >= ? ORDER BY rating DESC LIMIT ?', mode, MIN_GAMES_FOR_BOARD, limit).toArray();
  return rows.map((r, i) => ({
    rank: i + 1, userId: r.account_id as string, name: nameOf(r.account_id as string),
    rating: Math.round(r.rating as number), games: r.games as number, tier: tierOf(r.rating as number, r.games as number),
  }));
}

/** Tier badges for a list of players (friends list, lobby) */
export function tiersOf(sql: SqlStorage, ids: string[]): Record<string, string> {
  const out: Record<string, string> = {};
  for (const id of ids) out[id] = bestTier(sql, id);
  return out;
}
