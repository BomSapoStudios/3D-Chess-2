import { DurableObject } from 'cloudflare:workers';
import type { Env } from './index.js';
import { isGeneratedName, randomName } from './names.js';

// ─────────────────────────────────────────────────────────────────────────────
// Accounts — one Durable Object (SQLite) holding every player account.
//
// • An account is the player: a stable id plus a unique generated name.
// • Logins (Sign in with Apple / Google) are keys attached to an account, so the
//   same player can come back after a reinstall or from a browser.
// • Device link codes let a player move their account to another device without
//   signing in to anything.
// • Friends, blocks and pending challenges live here too.
//
// The Worker verifies tokens and calls this object with the caller's userId:
//   POST https://accounts/<op>  { userId?, ...fields }  →  JSON
// ─────────────────────────────────────────────────────────────────────────────

const DAY = 24 * 3600_000;
const NAME_EXPIRY_MS = 90 * DAY;           // a name unused this long goes back into the pool
const ONLINE_WINDOW_MS = 45_000;           // "online" = checked in within this window
const CHALLENGE_LIFETIME_MS = 10 * 60_000; // an unanswered challenge disappears after 10 minutes
const LINK_CODE_LIFETIME_MS = 10 * 60_000;
const LIMITS = { reroll: 10, friendRequest: 30, challenge: 60 } as const; // per account per day
const CODE_CHARS = 'ABCDEFGHJKLMNPQRSTUVWXYZ23456789';

type Limit = keyof typeof LIMITS;

class HttpError extends Error {
  constructor(public status: number, message: string) { super(message); }
}

interface AccountRow { id: string; name: string | null; last_seen: number }

export class Accounts extends DurableObject<Env> {
  private sql: SqlStorage;

  constructor(ctx: DurableObjectState, env: Env) {
    super(ctx, env);
    this.sql = ctx.storage.sql;
    this.sql.exec(`
      CREATE TABLE IF NOT EXISTS accounts (id TEXT PRIMARY KEY, name TEXT UNIQUE, created_at INTEGER, last_seen INTEGER, active_at INTEGER DEFAULT 0);
      CREATE TABLE IF NOT EXISTS logins (provider TEXT, subject TEXT, account_id TEXT, created_at INTEGER, PRIMARY KEY (provider, subject));
      CREATE TABLE IF NOT EXISTS link_codes (code TEXT PRIMARY KEY, account_id TEXT, expires INTEGER);
      CREATE TABLE IF NOT EXISTS friends (account_id TEXT, friend_id TEXT, status TEXT, created_at INTEGER, PRIMARY KEY (account_id, friend_id));
      CREATE TABLE IF NOT EXISTS blocks (account_id TEXT, blocked_id TEXT, PRIMARY KEY (account_id, blocked_id));
      CREATE TABLE IF NOT EXISTS challenges (to_id TEXT, from_id TEXT, room_code TEXT, created_at INTEGER, PRIMARY KEY (to_id, room_code));
      CREATE TABLE IF NOT EXISTS limits (account_id TEXT, kind TEXT, window_start INTEGER, count INTEGER, PRIMARY KEY (account_id, kind));
    `);
  }

  async fetch(request: Request): Promise<Response> {
    const op = new URL(request.url).pathname.slice(1);
    const body = await request.json().catch(() => ({})) as Record<string, unknown>;
    try {
      const result = this.handle(op, body);
      return Response.json(result);
    } catch (e) {
      if (e instanceof HttpError) return Response.json({ error: e.message }, { status: e.status });
      throw e;
    }
  }

  private handle(op: string, b: Record<string, unknown>): unknown {
    const userId = typeof b.userId === 'string' ? b.userId : '';
    switch (op) {
      case 'guest': return this.createAccount(crypto.randomUUID(), null);
      case 'refresh': return this.refresh(userId, String(b.name ?? ''));
      case 'reroll': return this.reroll(userId);
      case 'login': return this.login(String(b.provider), String(b.subject), userId || null);
      case 'me': return this.me(userId);
      case 'link-code': return this.linkCode(userId);
      case 'link-redeem': return this.redeem(String(b.code ?? ''));
      case 'friends': return this.friends(userId);
      case 'friend-request': return this.friendRequest(userId, String(b.name ?? ''));
      case 'search': return this.search(userId, String(b.query ?? ''));
      case 'friend-respond': return this.respond(userId, String(b.friendId ?? ''), b.accept === true);
      case 'friend-remove': return this.removeFriend(userId, String(b.friendId ?? ''));
      case 'block': return this.block(userId, String(b.friendId ?? ''));
      case 'can-challenge': return this.canChallenge(userId, String(b.friendId ?? ''));
      case 'challenge': return this.challenge(userId, String(b.friendId ?? ''), String(b.roomCode ?? ''));
      case 'challenge-dismiss': return this.dismiss(userId, String(b.roomCode ?? ''));
      default: throw new HttpError(404, 'Unknown operation');
    }
  }

  // ── Accounts and names ───────────────────────────────────────────────────────

  private account(id: string): AccountRow {
    const row = this.sql.exec('SELECT id, name, last_seen FROM accounts WHERE id = ?', id).toArray()[0] as unknown as AccountRow | undefined;
    if (!row) throw new HttpError(401, 'Account not found');
    return row;
  }

  /** A generated name nobody holds. Names idle past the expiry are reclaimed. */
  private freeName(preferred: string | null): string {
    const stale = Date.now() - NAME_EXPIRY_MS;
    const tryName = (name: string): boolean => {
      const holder = this.sql.exec('SELECT id, last_seen FROM accounts WHERE name = ?', name).toArray()[0] as unknown as AccountRow | undefined;
      if (!holder) return true;
      if (holder.last_seen < stale) {
        this.sql.exec('UPDATE accounts SET name = NULL WHERE id = ?', holder.id); // they'll get a new one if they return
        return true;
      }
      return false;
    };
    if (preferred && isGeneratedName(preferred) && tryName(preferred)) return preferred;
    for (let i = 0; i < 200; i++) {
      const name = randomName();
      if (tryName(name)) return name;
    }
    throw new HttpError(503, 'No free names right now, try again');
  }

  private createAccount(id: string, preferredName: string | null) {
    const now = Date.now();
    const name = this.freeName(preferredName);
    this.sql.exec('INSERT INTO accounts (id, name, created_at, last_seen) VALUES (?, ?, ?, ?)', id, name, now, now);
    return { userId: id, name };
  }

  /** Called each time the app signs in. Also adopts players from before accounts existed. */
  private refresh(userId: string, tokenName: string) {
    if (!userId) throw new HttpError(401, 'Sign in first');
    const row = this.sql.exec('SELECT id, name FROM accounts WHERE id = ?', userId).toArray()[0] as unknown as AccountRow | undefined;
    if (!row) return this.createAccount(userId, tokenName);
    let name = row.name;
    if (!name) {
      name = this.freeName(null);
      this.sql.exec('UPDATE accounts SET name = ? WHERE id = ?', name, userId);
    }
    this.sql.exec('UPDATE accounts SET last_seen = ? WHERE id = ?', Date.now(), userId);
    return { userId, name };
  }

  private reroll(userId: string) {
    this.account(userId);
    this.spend(userId, 'reroll');
    const name = this.freeName(null);
    this.sql.exec('UPDATE accounts SET name = ?, last_seen = ? WHERE id = ?', name, Date.now(), userId);
    return { userId, name };
  }

  private me(userId: string) {
    const row = this.account(userId);
    const providers = this.sql.exec('SELECT provider FROM logins WHERE account_id = ?', userId).toArray().map(r => r.provider as string);
    return { userId, name: row.name, providers };
  }

  // ── Logins (Apple / Google) and device link codes ────────────────────────────

  /**
   * A verified sign-in. If that Apple/Google account is already linked, you become that
   * player (e.g. after reinstalling). Otherwise it's attached to the current account,
   * or a new account is made.
   */
  private login(provider: string, subject: string, currentUserId: string | null) {
    const existing = this.sql.exec('SELECT account_id FROM logins WHERE provider = ? AND subject = ?', provider, subject).toArray()[0];
    if (existing) {
      const res = this.refresh(existing.account_id as string, '');
      return { ...res, linked: false, restored: existing.account_id !== currentUserId };
    }
    let accountId = currentUserId;
    const current = accountId ? this.sql.exec('SELECT id FROM accounts WHERE id = ?', accountId).toArray()[0] : undefined;
    const alreadyHasProvider = current
      ? this.sql.exec('SELECT 1 FROM logins WHERE account_id = ? AND provider = ?', accountId, provider).toArray().length > 0
      : false;
    if (!current || alreadyHasProvider) accountId = this.createAccount(crypto.randomUUID(), null).userId;
    this.sql.exec('INSERT INTO logins (provider, subject, account_id, created_at) VALUES (?, ?, ?, ?)', provider, subject, accountId, Date.now());
    return { ...this.refresh(accountId!, ''), linked: true, restored: false };
  }

  private linkCode(userId: string) {
    this.account(userId);
    this.sql.exec('DELETE FROM link_codes WHERE account_id = ? OR expires < ?', userId, Date.now());
    const bytes = crypto.getRandomValues(new Uint8Array(6));
    const code = Array.from(bytes, x => CODE_CHARS[x % CODE_CHARS.length]).join('');
    this.sql.exec('INSERT INTO link_codes (code, account_id, expires) VALUES (?, ?, ?)', code, userId, Date.now() + LINK_CODE_LIFETIME_MS);
    return { code, expiresInSeconds: LINK_CODE_LIFETIME_MS / 1000 };
  }

  private redeem(code: string) {
    const row = this.sql.exec('SELECT account_id FROM link_codes WHERE code = ? AND expires >= ?', code.toUpperCase(), Date.now()).toArray()[0];
    if (!row) throw new HttpError(404, 'That code is wrong or has expired');
    this.sql.exec('DELETE FROM link_codes WHERE code = ?', code.toUpperCase());
    return this.refresh(row.account_id as string, '');
  }

  // ── Friends ──────────────────────────────────────────────────────────────────

  private friends(userId: string) {
    const now = Date.now();
    this.account(userId);
    this.sql.exec('UPDATE accounts SET active_at = ?, last_seen = ? WHERE id = ?', now, now, userId);
    this.sql.exec('DELETE FROM challenges WHERE created_at < ?', now - CHALLENGE_LIFETIME_MS);
    const friends = this.sql.exec(`
      SELECT f.friend_id AS userId, a.name AS name, f.status AS status, a.active_at AS activeAt
      FROM friends f JOIN accounts a ON a.id = f.friend_id
      WHERE f.account_id = ? ORDER BY a.name`, userId).toArray()
      .map(r => ({ userId: r.userId, name: r.name ?? '(away)', status: r.status, online: now - (r.activeAt as number) < ONLINE_WINDOW_MS }));
    const challenges = this.sql.exec(`
      SELECT c.from_id AS fromUserId, a.name AS fromName, c.room_code AS roomCode, c.created_at AS at
      FROM challenges c JOIN accounts a ON a.id = c.from_id
      WHERE c.to_id = ? ORDER BY c.created_at DESC`, userId).toArray();
    return { friends, challenges };
  }

  private isBlocked(a: string, b: string): boolean {
    return this.sql.exec('SELECT 1 FROM blocks WHERE (account_id = ? AND blocked_id = ?) OR (account_id = ? AND blocked_id = ?)', a, b, b, a).toArray().length > 0;
  }

  private status(a: string, b: string): string | null {
    const row = this.sql.exec('SELECT status FROM friends WHERE account_id = ? AND friend_id = ?', a, b).toArray()[0];
    return row ? row.status as string : null;
  }

  private setPair(a: string, b: string, statusA: string, statusB: string) {
    const now = Date.now();
    this.sql.exec('INSERT OR REPLACE INTO friends (account_id, friend_id, status, created_at) VALUES (?, ?, ?, ?)', a, b, statusA, now);
    this.sql.exec('INSERT OR REPLACE INTO friends (account_id, friend_id, status, created_at) VALUES (?, ?, ?, ?)', b, a, statusB, now);
  }

  /** Players whose name contains the query (names starting with it first), for adding friends */
  private search(userId: string, query: string) {
    this.account(userId);
    const q = query.replace(/[^A-Za-z0-9]/g, '');
    if (q.length < 3) return { results: [] };
    const rows = this.sql.exec(`
      SELECT a.id AS userId, a.name AS name, f.status AS status
      FROM accounts a LEFT JOIN friends f ON f.account_id = ? AND f.friend_id = a.id
      WHERE a.name LIKE ? AND a.id != ?
        AND NOT EXISTS (SELECT 1 FROM blocks b WHERE (b.account_id = ? AND b.blocked_id = a.id) OR (b.account_id = a.id AND b.blocked_id = ?))
      ORDER BY (a.name LIKE ?) DESC, a.name LIMIT 20`,
      userId, `%${q}%`, userId, userId, userId, `${q}%`).toArray();
    return { results: rows.map(r => ({ userId: r.userId, name: r.name, status: r.status ?? 'none' })) };
  }

  private friendRequest(userId: string, name: string) {
    this.account(userId);
    const target = isGeneratedName(name.trim())
      ? this.sql.exec('SELECT id FROM accounts WHERE name = ?', name.trim()).toArray()[0]
      : undefined;
    // Blocked players look exactly like names that don't exist
    if (!target || this.isBlocked(userId, target.id as string)) throw new HttpError(404, 'No player has that name');
    const friendId = target.id as string;
    if (friendId === userId) throw new HttpError(400, "That's you!");
    const current = this.status(userId, friendId);
    if (current === 'friends') return { status: 'friends' };
    if (current === 'incoming') { this.setPair(userId, friendId, 'friends', 'friends'); return { status: 'friends' }; }
    if (current === 'outgoing') return { status: 'outgoing' };
    this.spend(userId, 'friendRequest');
    this.setPair(userId, friendId, 'outgoing', 'incoming');
    return { status: 'outgoing' };
  }

  private respond(userId: string, friendId: string, accept: boolean) {
    if (this.status(userId, friendId) !== 'incoming') throw new HttpError(404, 'No request from that player');
    if (accept) this.setPair(userId, friendId, 'friends', 'friends');
    else this.removeFriend(userId, friendId);
    return { ok: true };
  }

  private removeFriend(userId: string, friendId: string) {
    this.sql.exec('DELETE FROM friends WHERE (account_id = ? AND friend_id = ?) OR (account_id = ? AND friend_id = ?)', userId, friendId, friendId, userId);
    this.sql.exec('DELETE FROM challenges WHERE (to_id = ? AND from_id = ?) OR (to_id = ? AND from_id = ?)', userId, friendId, friendId, userId);
    return { ok: true };
  }

  private block(userId: string, friendId: string) {
    this.account(userId);
    this.removeFriend(userId, friendId);
    this.sql.exec('INSERT OR IGNORE INTO blocks (account_id, blocked_id) VALUES (?, ?)', userId, friendId);
    return { ok: true };
  }

  private canChallenge(userId: string, friendId: string) {
    if (this.status(userId, friendId) !== 'friends') throw new HttpError(403, 'You can only challenge friends');
    this.spend(userId, 'challenge');
    return { ok: true };
  }

  private challenge(userId: string, friendId: string, roomCode: string) {
    if (this.status(userId, friendId) !== 'friends') throw new HttpError(403, 'You can only challenge friends');
    this.sql.exec('INSERT OR REPLACE INTO challenges (to_id, from_id, room_code, created_at) VALUES (?, ?, ?, ?)', friendId, userId, roomCode, Date.now());
    return { ok: true };
  }

  private dismiss(userId: string, roomCode: string) {
    this.sql.exec('DELETE FROM challenges WHERE to_id = ? AND room_code = ?', userId, roomCode);
    return { ok: true };
  }

  // ── Daily limits ─────────────────────────────────────────────────────────────

  private spend(userId: string, kind: Limit) {
    const now = Date.now();
    const row = this.sql.exec('SELECT window_start, count FROM limits WHERE account_id = ? AND kind = ?', userId, kind).toArray()[0];
    let start = row ? row.window_start as number : now;
    let count = row ? row.count as number : 0;
    if (now - start > DAY) { start = now; count = 0; }
    if (count >= LIMITS[kind]) throw new HttpError(429, 'Slow down! Try again tomorrow');
    this.sql.exec('INSERT OR REPLACE INTO limits (account_id, kind, window_start, count) VALUES (?, ?, ?, ?)', userId, kind, start, count + 1);
  }
}
