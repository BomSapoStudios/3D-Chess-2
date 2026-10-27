import { DurableObject } from 'cloudflare:workers';
import type { Env } from './index.js';
import { isGeneratedName, randomName } from './names.js';
import { sendPush, type PushKind, type PushMessage } from './push.js';
import { cooldownUntil, leaderboard, recordGame, recordWalkout, setupTables, stats, tiersOf, type GameResult, type Mode } from './ratings.js';

// ─────────────────────────────────────────────────────────────────────────────
// Accounts — one Durable Object (SQLite) holding every player account.
//
// • An account is the player: a stable id plus a unique generated name.
// • Logins (Sign in with Apple / Google / Game Center) are keys attached to an account, so the
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
const REQUEST_LIFETIME_MS = 30 * 24 * 60 * 60_000; // an unanswered friend request expires after 30 days (and can be sent again)
const LINK_CODE_LIFETIME_MS = 10 * 60_000;
const LIMITS = { reroll: 10, friendRequest: 30, challenge: 60 } as const; // per account per day
const GUESTS_PER_ADDRESS = 50;             // new accounts per network per day (homes, schools and phone carriers share addresses)
const CODE_CHARS = 'ABCDEFGHJKLMNPQRSTUVWXYZ23456789';

type Limit = keyof typeof LIMITS;

class HttpError extends Error {
  constructor(public status: number, message: string) { super(message); }
}

interface AccountRow { id: string; name: string | null; last_seen: number }

export class Accounts extends DurableObject<Env> {
  private sql: SqlStorage;
  private readonly requestLifetimeMs: number;

  constructor(ctx: DurableObjectState, env: Env) {
    super(ctx, env);
    this.sql = ctx.storage.sql;
    // Local tests can shorten request expiry with REQUEST_TIME_SCALE
    this.requestLifetimeMs = REQUEST_LIFETIME_MS * (Number(env.REQUEST_TIME_SCALE) > 0 ? Number(env.REQUEST_TIME_SCALE) : 1);
    this.sql.exec(`
      CREATE TABLE IF NOT EXISTS accounts (id TEXT PRIMARY KEY, name TEXT UNIQUE, created_at INTEGER, last_seen INTEGER, active_at INTEGER DEFAULT 0);
      CREATE TABLE IF NOT EXISTS logins (provider TEXT, subject TEXT, account_id TEXT, created_at INTEGER, PRIMARY KEY (provider, subject));
      CREATE TABLE IF NOT EXISTS link_codes (code TEXT PRIMARY KEY, account_id TEXT, expires INTEGER);
      CREATE TABLE IF NOT EXISTS friends (account_id TEXT, friend_id TEXT, status TEXT, created_at INTEGER, PRIMARY KEY (account_id, friend_id));
      CREATE TABLE IF NOT EXISTS blocks (account_id TEXT, blocked_id TEXT, PRIMARY KEY (account_id, blocked_id));
      CREATE TABLE IF NOT EXISTS push_tokens (token TEXT PRIMARY KEY, account_id TEXT, sandbox INTEGER, updated INTEGER);
      CREATE TABLE IF NOT EXISTS push_prefs (account_id TEXT PRIMARY KEY, prefs TEXT);
      CREATE TABLE IF NOT EXISTS active_games (account_id TEXT, room_code TEXT, joined_at INTEGER, PRIMARY KEY (account_id, room_code));
      CREATE TABLE IF NOT EXISTS declines (decliner_id TEXT, sender_id TEXT, created_at INTEGER, PRIMARY KEY (decliner_id, sender_id));
      CREATE TABLE IF NOT EXISTS challenges (to_id TEXT, from_id TEXT, room_code TEXT, created_at INTEGER, PRIMARY KEY (to_id, room_code));
      CREATE TABLE IF NOT EXISTS limits (account_id TEXT, kind TEXT, window_start INTEGER, count INTEGER, PRIMARY KEY (account_id, kind));
      CREATE TABLE IF NOT EXISTS guest_addresses (address TEXT, day INTEGER, count INTEGER, PRIMARY KEY (address, day));
    `);
    setupTables(this.sql);
  }

  async fetch(request: Request): Promise<Response> {
    const op = new URL(request.url).pathname.slice(1);
    const body = await request.json().catch(() => ({})) as Record<string, unknown>;
    try {
      const result = await this.handle(op, body);
      return Response.json(result);
    } catch (e) {
      if (e instanceof HttpError) return Response.json({ error: e.message }, { status: e.status });
      throw e;
    }
  }

  private handle(op: string, b: Record<string, unknown>): unknown {
    const userId = typeof b.userId === 'string' ? b.userId : '';
    switch (op) {
      case 'guest': return this.guest(String(b.ip ?? ''));
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
      case 'game-add': return this.gameAdd(String(b.roomCode ?? ''), Array.isArray(b.userIds) ? b.userIds.map(String) : []);
      case 'game-remove': return this.gameRemove(String(b.roomCode ?? ''), typeof b.userId === 'string' ? b.userId : null);
      case 'games': return this.games(userId);
      case 'push-register': return this.pushRegister(userId, String(b.token ?? ''), b.sandbox === true);
      case 'push-unregister': return this.pushUnregister(String(b.token ?? ''), userId);
      case 'push-settings': return this.pushSettings(userId, b.prefs as Record<string, boolean> | undefined);
      case 'game-result': return { changes: recordGame(this.sql, b.result as GameResult, Date.now()) };
      case 'walkout': recordWalkout(this.sql, String(b.walkoutUserId ?? ''), Date.now()); return { ok: true };
      case 'cooldown': return { until: cooldownUntil(this.sql, userId, Date.now()) };
      case 'stats': return this.statsFor(userId, String(b.of ?? '') || userId);
      case 'leaderboard': return this.board(userId, b.mode === 'melee' ? 'melee' : 'duel', b.scope === 'everyone' ? 'everyone' : 'friends');
      case 'notify': return this.notify(Array.isArray(b.userIds) ? b.userIds.map(String) : [], b.message as PushMessage);
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

  /** A new guest account, unless this address has made too many today */
  private async guest(ip: string) {
    if (ip) {
      const day = Math.floor(Date.now() / DAY);
      const digest = new Uint8Array(await crypto.subtle.digest('SHA-256', new TextEncoder().encode(`${day}|${ip}`)));
      const address = Array.from(digest.slice(0, 12), x => x.toString(16).padStart(2, '0')).join('');
      this.sql.exec('DELETE FROM guest_addresses WHERE day < ?', day);
      const count = (this.sql.exec('SELECT count FROM guest_addresses WHERE address = ? AND day = ?', address, day).toArray()[0]?.count as number | undefined) ?? 0;
      if (count >= GUESTS_PER_ADDRESS) throw new HttpError(429, 'Too many new players from this network today, try again tomorrow');
      this.sql.exec('INSERT OR REPLACE INTO guest_addresses (address, day, count) VALUES (?, ?, ?)', address, day, count + 1);
    }
    return this.createAccount(crypto.randomUUID(), null);
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

  // ── Active games (so a player can find their games again after the app restarts) ──

  private gameAdd(roomCode: string, userIds: string[]) {
    for (const id of userIds) {
      this.sql.exec('INSERT OR IGNORE INTO active_games (account_id, room_code, joined_at) VALUES (?, ?, ?)', id, roomCode, Date.now());
    }
    return { ok: true };
  }

  /** A finished or closed room, for everyone in it; or one player's stale entry */
  private gameRemove(roomCode: string, userId: string | null) {
    if (userId) this.sql.exec('DELETE FROM active_games WHERE room_code = ? AND account_id = ?', roomCode, userId);
    else this.sql.exec('DELETE FROM active_games WHERE room_code = ?', roomCode);
    return { ok: true };
  }

  private games(userId: string) {
    // Rooms never outlive a week of silence, so older entries are leftovers
    this.sql.exec('DELETE FROM active_games WHERE joined_at < ?', Date.now() - 30 * DAY);
    const codes = this.sql.exec('SELECT room_code FROM active_games WHERE account_id = ? ORDER BY joined_at DESC LIMIT 10', userId)
      .toArray().map(r => r.room_code as string);
    return { codes };
  }

  // ── Push notifications ──────────────────────────────────────────────────────

  private static readonly PUSH_KINDS: PushKind[] = ['joined', 'started', 'turn', 'challenge', 'friend'];

  private pushRegister(userId: string, token: string, sandbox: boolean) {
    this.account(userId);
    if (!/^[0-9a-f]{64,200}$/i.test(token)) throw new HttpError(400, 'Bad device token');
    // A device belongs to whoever signed in on it last
    this.sql.exec('INSERT OR REPLACE INTO push_tokens (token, account_id, sandbox, updated) VALUES (?, ?, ?, ?)', token.toLowerCase(), userId, sandbox ? 1 : 0, Date.now());
    return { ok: true };
  }

  /** A device signing out of notifications; with an account, only that account's own device */
  private pushUnregister(token: string, userId?: string) {
    if (userId) this.sql.exec('DELETE FROM push_tokens WHERE token = ? AND account_id = ?', token.toLowerCase(), userId);
    else this.sql.exec('DELETE FROM push_tokens WHERE token = ?', token.toLowerCase());
    return { ok: true };
  }

  /** Read (and optionally change) a player's notification switches. Everything is on by default. */
  private pushSettings(userId: string, update?: Record<string, boolean>) {
    this.account(userId);
    const prefs = this.prefsOf(userId);
    if (update) {
      for (const key of ['all', ...Accounts.PUSH_KINDS]) if (typeof update[key] === 'boolean') prefs[key] = update[key];
      this.sql.exec('INSERT OR REPLACE INTO push_prefs (account_id, prefs) VALUES (?, ?)', userId, JSON.stringify(prefs));
    }
    return { prefs };
  }

  private prefsOf(userId: string): Record<string, boolean> {
    const prefs: Record<string, boolean> = { all: true };
    for (const k of Accounts.PUSH_KINDS) prefs[k] = true;
    const row = this.sql.exec('SELECT prefs FROM push_prefs WHERE account_id = ?', userId).toArray()[0];
    if (row) Object.assign(prefs, JSON.parse(row.prefs as string));
    return prefs;
  }

  /** Send a notification to these players' devices, if they want this kind */
  private notify(userIds: string[], message: PushMessage) {
    if (!message || !Accounts.PUSH_KINDS.includes(message.kind)) return { sent: 0 };
    const targets: { token: string; sandbox: boolean }[] = [];
    for (const id of new Set(userIds)) {
      const prefs = this.prefsOf(id);
      if (!prefs.all || !prefs[message.kind]) continue;
      for (const r of this.sql.exec('SELECT token, sandbox FROM push_tokens WHERE account_id = ?', id).toArray())
        targets.push({ token: r.token as string, sandbox: r.sandbox === 1 });
    }
    this.ctx.waitUntil((async () => {
      for (const t of targets) {
        if (await sendPush(this.env, t.token, t.sandbox, message) === 'gone') this.pushUnregister(t.token);
      }
    })());
    return { sent: targets.length };
  }

  // ── Ratings and stats (see ratings.ts) ───────────────────────────────────────

  private statsFor(viewerId: string, userId: string) {
    this.account(userId);
    if (viewerId && viewerId !== userId && this.isBlocked(userId, viewerId)) throw new HttpError(404, 'Player not found');
    return stats(this.sql, userId, viewerId || null, id => this.nameOf(id));
  }

  private board(userId: string, mode: Mode, scope: 'friends' | 'everyone') {
    let among: string[] | null = null;
    if (scope === 'friends') {
      among = this.sql.exec("SELECT friend_id FROM friends WHERE account_id = ? AND status = 'friends'", userId).toArray().map(r => r.friend_id as string);
      among.push(userId);
    }
    return { mode, scope, players: leaderboard(this.sql, mode, among, id => this.nameOf(id)) };
  }

  private nameOf(userId: string): string {
    const row = this.sql.exec('SELECT name FROM accounts WHERE id = ?', userId).toArray()[0];
    return (row?.name as string) ?? 'A player';
  }

  // ── Friends ──────────────────────────────────────────────────────────────────

  private friends(userId: string) {
    const now = Date.now();
    this.account(userId);
    this.sql.exec('UPDATE accounts SET active_at = ?, last_seen = ? WHERE id = ?', now, now, userId);
    this.sql.exec('DELETE FROM challenges WHERE created_at < ?', now - CHALLENGE_LIFETIME_MS);
    this.expireRequests();
    const friends = this.sql.exec(`
      SELECT f.friend_id AS userId, a.name AS name, f.status AS status, a.active_at AS activeAt
      FROM friends f JOIN accounts a ON a.id = f.friend_id
      WHERE f.account_id = ? ORDER BY a.name`, userId).toArray()
      .map(r => ({ userId: r.userId, name: r.name ?? '(away)', status: r.status, online: now - (r.activeAt as number) < ONLINE_WINDOW_MS }));
    const tiers = tiersOf(this.sql, friends.map(f => f.userId as string));
    for (const f of friends) (f as Record<string, unknown>).tier = tiers[f.userId as string];
    const challenges = this.sql.exec(`
      SELECT c.from_id AS fromUserId, a.name AS fromName, c.room_code AS roomCode, c.created_at AS at
      FROM challenges c JOIN accounts a ON a.id = c.from_id
      WHERE c.to_id = ? ORDER BY c.created_at DESC`, userId).toArray();
    return { friends, challenges };
  }

  /** Unanswered requests lapse after 30 days; unlike a decline, the sender may then ask again */
  private expireRequests() {
    this.sql.exec("DELETE FROM friends WHERE status IN ('outgoing', 'incoming') AND created_at < ?", Date.now() - this.requestLifetimeMs);
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
    // Names are matched ignoring capitals ("shinyrook359" finds ShinyRook359)
    const found = /^[a-z]+[0-9]{1,3}$/i.test(name.trim())
      ? this.sql.exec('SELECT id, name FROM accounts WHERE name = ? COLLATE NOCASE', name.trim()).toArray()[0]
      : undefined;
    const target = found && isGeneratedName(found.name as string) ? found : undefined;
    // Blocked players look exactly like names that don't exist
    if (!target || this.isBlocked(userId, target.id as string)) throw new HttpError(404, 'No player has that name');
    const friendId = target.id as string;
    if (friendId === userId) throw new HttpError(400, "That's you!");
    this.expireRequests();
    const current = this.status(userId, friendId);
    if (current === 'friends') return { status: 'friends' };
    if (current === 'incoming') {
      this.setPair(userId, friendId, 'friends', 'friends');
      this.notify([friendId], { kind: 'friend', title: 'New friend', body: `${this.nameOf(userId)} is now your friend` });
      return { status: 'friends' };
    }
    if (current === 'outgoing') return { status: 'outgoing' };
    // Declined once means no more requests to that player (they can still send one to you)
    if (this.sql.exec('SELECT 1 FROM declines WHERE decliner_id = ? AND sender_id = ?', friendId, userId).toArray().length) {
      throw new HttpError(403, 'That player declined your friend request');
    }
    this.spend(userId, 'friendRequest');
    // Asking someone you once declined clears that decline
    this.sql.exec('DELETE FROM declines WHERE decliner_id = ? AND sender_id = ?', userId, friendId);
    this.setPair(userId, friendId, 'outgoing', 'incoming');
    this.notify([friendId], { kind: 'friend', title: 'Friend request', body: `${this.nameOf(userId)} wants to be your friend` });
    return { status: 'outgoing' };
  }

  private respond(userId: string, friendId: string, accept: boolean) {
    if (this.status(userId, friendId) !== 'incoming') throw new HttpError(404, 'No request from that player');
    if (accept) {
      this.setPair(userId, friendId, 'friends', 'friends');
      this.notify([friendId], { kind: 'friend', title: 'Friend request accepted', body: `${this.nameOf(userId)} accepted your friend request` });
    } else {
      this.removeFriend(userId, friendId);
      this.sql.exec('INSERT OR REPLACE INTO declines (decliner_id, sender_id, created_at) VALUES (?, ?, ?)', userId, friendId, Date.now());
    }
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
    this.notify([friendId], { kind: 'challenge', title: 'You\'ve been challenged!', body: `${this.nameOf(userId)} wants to play 3D Chess`, roomCode });
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
