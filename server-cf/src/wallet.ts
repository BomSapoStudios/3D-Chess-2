import { CLIENT_ID, ECONOMY, PUZZLE_ID, ITEM_ID, priceOf } from './economy.js';

// ─────────────────────────────────────────────────────────────────────────────
// Wallets — each account's pawns and the Customize items and Queen Blitz packs it owns, kept in the
// Accounts object's SQLite (so deleting an account deletes its wallet).
//
// • The app earns pawns in single-player modes the server doesn't watch, so earning is checked
//   against the rules instead: a Queen Blitz puzzle pays once, Zombies! pays by kills within a daily
//   cap, and amounts that can't happen are refused.
// • Earnings arrive in batches with ids the app made up; a batch sent twice pays once.
// • Spending pawns and recording App Store purchases happen here, atomically.
// • An App Store purchase belongs to the Apple ID, not the game account: its item stays with the
//   account that last presented it (a new account restoring the purchase takes it over).
// ─────────────────────────────────────────────────────────────────────────────

const DAY = 24 * 3600_000;
const REQUEST_MEMORY_MS = 60 * DAY; // how long a request id is remembered (offline queues are older than this only in theory)

export class WalletError extends Error {
  constructor(public status: number, message: string) { super(message); }
}

export function setupWalletTables(sql: SqlStorage) {
  sql.exec(`
    CREATE TABLE IF NOT EXISTS wallets (account_id TEXT PRIMARY KEY, balance INTEGER DEFAULT 0, lifetime INTEGER DEFAULT 0,
      migrated INTEGER DEFAULT 0, zombie_day INTEGER DEFAULT 0, zombie_today INTEGER DEFAULT 0);
    CREATE TABLE IF NOT EXISTS wallet_items (account_id TEXT, item TEXT, source TEXT, at INTEGER, PRIMARY KEY (account_id, item));
    CREATE TABLE IF NOT EXISTS wallet_cleared (account_id TEXT, puzzle TEXT, PRIMARY KEY (account_id, puzzle));
    CREATE TABLE IF NOT EXISTS wallet_runs (account_id TEXT, run_id TEXT, credited INTEGER, at INTEGER, PRIMARY KEY (account_id, run_id));
    CREATE TABLE IF NOT EXISTS wallet_requests (account_id TEXT, request_id TEXT, at INTEGER, PRIMARY KEY (account_id, request_id));
    CREATE TABLE IF NOT EXISTS apple_transactions (original_id TEXT PRIMARY KEY, transaction_id TEXT, account_id TEXT, item TEXT, environment TEXT, at INTEGER);
  `);
  // The player's local day number (days since 1970 at their midnight) the daily bonus was last claimed (-1: never)
  try { sql.exec('ALTER TABLE wallets ADD COLUMN daily_day INTEGER DEFAULT -1'); } catch { /* already there */ }
}

/** Forget an account's wallet (deleting the account). Its App Store purchases can then be restored onto a new account. */
export function purgeWallet(sql: SqlStorage, id: string) {
  for (const table of ['wallets', 'wallet_items', 'wallet_cleared', 'wallet_runs', 'wallet_requests'])
    sql.exec(`DELETE FROM ${table} WHERE account_id = ?`, id);
  // Unlock purchases go (so they can be restored onto a new account); pawn top-ups stay marked as used,
  // unlinked from the player, so the same code can't pay twice
  sql.exec("DELETE FROM apple_transactions WHERE account_id = ? AND original_id NOT LIKE 'pawns:%'", id);
  sql.exec("UPDATE apple_transactions SET account_id = '' WHERE account_id = ?", id);
}

interface WalletRow { balance: number; lifetime: number; migrated: number; zombie_day: number; zombie_today: number; daily_day: number }

function row(sql: SqlStorage, id: string): WalletRow {
  sql.exec('INSERT OR IGNORE INTO wallets (account_id) VALUES (?)', id);
  return sql.exec('SELECT balance, lifetime, migrated, zombie_day, zombie_today, daily_day FROM wallets WHERE account_id = ?', id).toArray()[0] as unknown as WalletRow;
}

const utcDay = (now: number) => Math.floor(now / DAY);

/**
 * What the app shows: pawns, every pawn ever earned, owned items, what Zombies! can still pay today,
 * and the local day the daily bonus was last claimed (the app works out from its own clock whether today's is waiting)
 */
export function walletOf(sql: SqlStorage, id: string, now = Date.now()) {
  const w = row(sql, id);
  const zombieToday = w.zombie_day === utcDay(now) ? w.zombie_today : 0;
  const items = sql.exec('SELECT item FROM wallet_items WHERE account_id = ? ORDER BY item', id).toArray().map(r => r.item as string);
  return {
    balance: w.balance, lifetime: w.lifetime, items, zombieLeftToday: Math.max(0, ECONOMY.zombieDailyCap - zombieToday), migrated: w.migrated === 1,
    lastDailyDay: w.daily_day ?? -1,
  };
}

/**
 * The daily bonus: once per day, the day starting at the player's midnight (their phone's offset from UTC,
 * kept to real time zones). Each claim's local day must be later than the last one, so changing the
 * phone's time zone can bring one claim forward at most, never add more. A second claim the same day pays
 * nothing and says when the next comes (Unix ms, the player's next midnight).
 */
export function claimDaily(sql: SqlStorage, id: string, utcOffsetMinutes: unknown, now = Date.now()) {
  const max = ECONOMY.maxUtcOffsetMinutes;
  const offset = typeof utcOffsetMinutes === 'number' && Number.isFinite(utcOffsetMinutes) ? Math.max(-max, Math.min(max, Math.round(utcOffsetMinutes))) : 0;
  const shift = offset * 60_000;
  const day = Math.floor((now + shift) / DAY);
  const nextDailyAt = (day + 1) * DAY - shift;
  const w = row(sql, id);
  if (day <= (w.daily_day ?? -1)) return { claimed: false, pawns: 0, nextDailyAt, wallet: walletOf(sql, id, now) };
  sql.exec('UPDATE wallets SET daily_day = ? WHERE account_id = ?', day, id);
  earn(sql, id, ECONOMY.dailyClaim);
  return { claimed: true, pawns: ECONOMY.dailyClaim, nextDailyAt, wallet: walletOf(sql, id, now) };
}

function earn(sql: SqlStorage, id: string, pawns: number) {
  if (pawns > 0) sql.exec('UPDATE wallets SET balance = balance + ?, lifetime = lifetime + ? WHERE account_id = ?', pawns, pawns, id);
}

/** A request id seen before (its effect already applied)? Remembers it if not. */
function seen(sql: SqlStorage, id: string, requestId: string, now: number): boolean {
  if (sql.exec('SELECT 1 FROM wallet_requests WHERE account_id = ? AND request_id = ?', id, requestId).toArray().length) return true;
  sql.exec('INSERT INTO wallet_requests (account_id, request_id, at) VALUES (?, ?, ?)', id, requestId, now);
  return false;
}

export interface Migration { balance?: unknown; lifetime?: unknown; items?: unknown; cleared?: unknown }
export interface EarnEvent { id?: unknown; kind?: unknown; puzzle?: unknown; pawns?: unknown; run?: unknown; brain?: unknown; kills?: unknown }

const count = (v: unknown, max: number) => typeof v === 'number' && Number.isFinite(v) ? Math.max(0, Math.min(max, Math.floor(v))) : 0;

/**
 * The first sync from a phone that kept pawns before the server did: its balance (capped), items and
 * cleared puzzles come along once per account. Later uploads (another phone, a reinstall) are ignored.
 */
function migrate(sql: SqlStorage, id: string, m: Migration, now: number) {
  if (row(sql, id).migrated === 1) return;
  const balance = count(m.balance, ECONOMY.migrateCap);
  const lifetime = Math.max(balance, count(m.lifetime, 10_000_000));
  sql.exec('UPDATE wallets SET balance = balance + ?, lifetime = lifetime + ?, migrated = 1 WHERE account_id = ?', balance, lifetime, id);
  const items = Array.isArray(m.items) ? m.items.slice(0, ECONOMY.migrateMaxItems) : [];
  for (const raw of items) {
    const item = String(raw).toLowerCase();
    if (ITEM_ID.test(item)) sql.exec('INSERT OR IGNORE INTO wallet_items (account_id, item, source, at) VALUES (?, ?, ?, ?)', id, item, 'phone', now);
  }
  const cleared = Array.isArray(m.cleared) ? m.cleared.slice(0, 5000) : [];
  for (const raw of cleared) {
    const puzzle = String(raw).toLowerCase();
    if (PUZZLE_ID.test(puzzle)) sql.exec('INSERT OR IGNORE INTO wallet_cleared (account_id, puzzle) VALUES (?, ?)', id, puzzle);
  }
}

/** One earning; returns the pawns it paid */
function apply(sql: SqlStorage, id: string, e: EarnEvent, now: number): number {
  if (e.kind === 'blitz') {
    // A puzzle pays the first time it's cleared, at most one pawn per pawn on the biggest board
    const puzzle = String(e.puzzle ?? '').toLowerCase();
    if (!PUZZLE_ID.test(puzzle)) throw new WalletError(400, 'Bad puzzle');
    if (sql.exec('SELECT 1 FROM wallet_cleared WHERE account_id = ? AND puzzle = ?', id, puzzle).toArray().length) return 0;
    sql.exec('INSERT INTO wallet_cleared (account_id, puzzle) VALUES (?, ?)', id, puzzle);
    const pawns = count(e.pawns, ECONOMY.blitzMaxPawns);
    earn(sql, id, pawns);
    return pawns;
  }
  if (e.kind === 'zombie') {
    // The app reports a run's kills so far; the run pays kills × rate rounded down, minus what it paid
    // already, within today's cap (kills past the cap still count as paid, as on the phone)
    const run = String(e.run ?? '');
    const rate = ECONOMY.zombieRates[String(e.brain)];
    if (!CLIENT_ID.test(run) || !rate) throw new WalletError(400, 'Bad run');
    if (typeof e.kills !== 'number' || e.kills > ECONOMY.zombieMaxKillsPerRun) throw new WalletError(400, 'Too many kills');
    const worth = Math.floor(count(e.kills, ECONOMY.zombieMaxKillsPerRun) * rate + 1e-9);
    const credited = (sql.exec('SELECT credited FROM wallet_runs WHERE account_id = ? AND run_id = ?', id, run).toArray()[0]?.credited as number | undefined) ?? 0;
    if (worth <= credited) return 0;
    const w = row(sql, id);
    const day = utcDay(now);
    const today = w.zombie_day === day ? w.zombie_today : 0;
    const pay = Math.min(worth - credited, Math.max(0, ECONOMY.zombieDailyCap - today));
    sql.exec('INSERT OR REPLACE INTO wallet_runs (account_id, run_id, credited, at) VALUES (?, ?, ?, ?)', id, run, worth, now);
    sql.exec('UPDATE wallets SET zombie_day = ?, zombie_today = ? WHERE account_id = ?', day, today + pay, id);
    earn(sql, id, pay);
    return pay;
  }
  if (e.kind === 'autoplace') {
    // Spent while offline: taken if there's still a pawn for it
    const w = row(sql, id);
    if (w.balance >= ECONOMY.autoPlace) sql.exec('UPDATE wallets SET balance = balance - ? WHERE account_id = ?', ECONOMY.autoPlace, id);
    return 0;
  }
  throw new WalletError(400, 'Unknown earning');
}

/** The app's queued earnings (and, once, the pawns it kept before the server). Each event is applied once. */
export function syncWallet(sql: SqlStorage, id: string, migration: Migration | undefined, events: unknown, now = Date.now()) {
  row(sql, id);
  if (migration && typeof migration === 'object') migrate(sql, id, migration, now);
  const list = Array.isArray(events) ? events : [];
  if (list.length > ECONOMY.maxEventsPerSync) throw new WalletError(400, 'Too many earnings at once');
  const results: { id: string; paid: number; error?: string }[] = [];
  for (const raw of list as EarnEvent[]) {
    const requestId = String(raw?.id ?? '');
    if (!CLIENT_ID.test(requestId)) { results.push({ id: requestId, paid: 0, error: 'Bad id' }); continue; }
    if (seen(sql, id, requestId, now)) { results.push({ id: requestId, paid: 0 }); continue; }
    try {
      results.push({ id: requestId, paid: apply(sql, id, raw, now) });
    } catch (e) {
      results.push({ id: requestId, paid: 0, error: e instanceof WalletError ? e.message : 'Refused' });
    }
  }
  sql.exec('DELETE FROM wallet_requests WHERE at < ?', now - REQUEST_MEMORY_MS);
  sql.exec('DELETE FROM wallet_runs WHERE at < ?', now - 7 * DAY);
  return { wallet: walletOf(sql, id, now), results };
}

/** Unlock an item for pawns: refused if it's already owned or there aren't enough. Sent twice, it charges once. */
export function spendPawns(sql: SqlStorage, id: string, requestId: string, rawItem: string, now = Date.now()) {
  const item = rawItem.toLowerCase();
  const price = priceOf(item);
  if (price === null) throw new WalletError(400, "That can't be bought with pawns");
  if (!CLIENT_ID.test(requestId)) throw new WalletError(400, 'Bad id');
  const owned = sql.exec('SELECT 1 FROM wallet_items WHERE account_id = ? AND item = ?', id, item).toArray().length > 0;
  if (sql.exec('SELECT 1 FROM wallet_requests WHERE account_id = ? AND request_id = ?', id, requestId).toArray().length) {
    return { wallet: walletOf(sql, id, now), item }; // already done
  }
  if (owned) throw new WalletError(409, 'You already own that');
  const w = row(sql, id);
  if (w.balance < price) throw new WalletError(402, `Not enough pawns yet: you need ${price - w.balance} more.`);
  seen(sql, id, requestId, now);
  sql.exec('UPDATE wallets SET balance = balance - ? WHERE account_id = ?', price, id);
  sql.exec('INSERT INTO wallet_items (account_id, item, source, at) VALUES (?, ?, ?, ?)', id, item, 'pawns', now);
  return { wallet: walletOf(sql, id, now), item };
}

export interface VerifiedPurchase { originalTransactionId: string; transactionId: string; item: string; environment: string; revoked?: boolean }
/** A verified pawn top-up (offer code): adds `pawns` once per transaction id */
export interface VerifiedCredit { transactionId: string; product: string; pawns: number; environment: string }

/**
 * App Store purchases the Worker has verified. Each original transaction unlocks its item for one
 * account: if another account had it, the item moves here (the same Apple ID restoring on a new account).
 */
export function recordPurchases(sql: SqlStorage, id: string, purchases: VerifiedPurchase[], now = Date.now(), credits: VerifiedCredit[] = []) {
  row(sql, id);
  const granted: string[] = [];
  // Pawn top-ups: each transaction pays once, whichever account sends it (a replay changes nothing)
  let credited = 0;
  for (const c of credits) {
    const key = 'pawns:' + c.transactionId;
    if (sql.exec('SELECT 1 FROM apple_transactions WHERE original_id = ?', key).toArray().length) continue;
    sql.exec('INSERT INTO apple_transactions (original_id, transaction_id, account_id, item, environment, at) VALUES (?, ?, ?, ?, ?, ?)',
      key, c.transactionId, id, c.product, c.environment, now);
    earn(sql, id, c.pawns);
    credited += c.pawns;
  }
  for (const p of purchases) {
    if (p.revoked) {
      // Refunded: the item goes from whichever account the purchase unlocked it for
      const holder = sql.exec('SELECT account_id FROM apple_transactions WHERE original_id = ?', p.originalTransactionId).toArray()[0]?.account_id as string | undefined;
      if (holder) sql.exec('DELETE FROM wallet_items WHERE account_id = ? AND item = ? AND source = ?', holder, p.item, 'apple:' + p.originalTransactionId);
      sql.exec('DELETE FROM apple_transactions WHERE original_id = ?', p.originalTransactionId);
      continue;
    }
    const prev = sql.exec('SELECT account_id FROM apple_transactions WHERE original_id = ?', p.originalTransactionId).toArray()[0];
    const prevAccount = prev?.account_id as string | undefined;
    if (prevAccount && prevAccount !== id) {
      sql.exec('DELETE FROM wallet_items WHERE account_id = ? AND item = ? AND source = ?', prevAccount, p.item, 'apple:' + p.originalTransactionId);
    }
    sql.exec('INSERT OR REPLACE INTO apple_transactions (original_id, transaction_id, account_id, item, environment, at) VALUES (?, ?, ?, ?, ?, ?)',
      p.originalTransactionId, p.transactionId, id, p.item, p.environment, now);
    const had = sql.exec('SELECT 1 FROM wallet_items WHERE account_id = ? AND item = ?', id, p.item).toArray().length > 0;
    if (!had) sql.exec('INSERT INTO wallet_items (account_id, item, source, at) VALUES (?, ?, ?, ?)', id, p.item, 'apple:' + p.originalTransactionId, now);
    granted.push(p.item);
  }
  return { wallet: walletOf(sql, id, now), granted, credited };
}
