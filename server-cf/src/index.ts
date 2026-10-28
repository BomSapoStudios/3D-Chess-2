import { issueToken, verifyToken } from './auth.js';
import { verifyIdToken, type Provider } from './oidc.js';
import { verifyGameCenter } from './gamecenter.js';
import { revokeApple } from './appleRevoke.js';
import { verifyAppleTransaction, APPLE_ROOT_CA_G3 } from './appleJws.js';
import { ECONOMY, itemOfProduct } from './economy.js';
import type { GameConfig, PlayerColor } from './types/game.js';

export { Room } from './room.js';
export { Matchmaker } from './matchmaker.js';
export { Accounts } from './accounts.js';

// ─────────────────────────────────────────────────────────────────────────────
// 3D Chess game server (Cloudflare Worker)
//
//   GET  /health                        → { ok: true, minClientVersion }
//
//   Every other request says which app build sent it: header X-Client-Version (sockets: ?cv=).
//   Builds older than MIN_CLIENT_VERSION get 426 { updateRequired: true } and the app asks the
//   player to update. Deleting an account always works, whatever the build.
//
//   Accounts (see accounts.ts). Calls that sign you in return { token, userId, name }.
//   POST /auth/guest                    → new account with a unique generated name
//   POST /auth/refresh                  → fresh token + current name   (Bearer)
//   POST /auth/apple   { idToken }      → sign in with Apple; links to the current
//   POST /auth/google  { idToken }        account (Bearer optional) or restores yours
//   POST /auth/gamecenter { teamPlayerId, publicKeyUrl, signature, salt, timestamp }
//                                       → the same, with Apple's Game Center identity
//   POST /auth/signout                  → every older token stops working; a fresh one back (Bearer)
//   POST /names/reroll                  → a new unique name            (Bearer)
//   GET  /me                            → { userId, name, providers }  (Bearer)
//   POST /link/code                     → { code } to type on another device (Bearer)
//   POST /link/redeem  { code }         → become that account on this device
//   POST /account/delete { appleAuthorizationCode?, appleNonce? } → delete your account and everything
//                                         about you; resigns your games (Bearer)
//   GET  /friends                       → { friends, challenges }; marks you online (Bearer)
//   GET  /friends/search?q=neon         → { results: [{ userId, name, status }] } names containing q
//   POST /friends/request { name }      POST /friends/respond { friendId, accept }
//   POST /friends/remove  { friendId }  POST /friends/block   { friendId }
//   POST /friends/challenge { friendId, config? } → { code } private room for you two
//   POST /friends/dismiss { roomCode }
//
//   Games
//   POST /rooms        { config, isPublic? }   (Authorization: Bearer <token>)
//                                       → { code, config }
//   GET  /rooms/:code                   → room summary (players, open seats)
//   GET  /rooms/:code/connect?token=…   → WebSocket into the room
//   GET  /matchmaking/connect?token=…   → WebSocket into the Find Match queue
//   GET  /matchmaking/cooldown          → { until, ranked } (until: Unix ms, 0 = not paused;
//                                         ranked: signed in to Game Center, so Find Match can rate you)
//   GET  /stats?of=<userId>             → a player's ratings and stats (yours without ?of)
//   GET  /leaderboard?mode=duel|melee&scope=friends|everyone
//
//   Pawns (wallet.ts; prices and earning rules in economy.ts). Each answers with the wallet:
//   { balance, lifetime, items: ['set.abyssglow', ...], zombieLeftToday, migrated }
//   GET  /wallet                                                        (Bearer)
//   POST /wallet/sync  { migrate?, events: [{ id, kind: 'blitz', puzzle, pawns } |
//                        { id, kind: 'zombie', run, brain, kills } | { id, kind: 'autoplace' }] }
//                                       → { wallet, results }  earnings queued by the app (Bearer)
//   POST /wallet/spend { requestId, item } → { wallet, item }   unlock for pawns (Bearer)
//   POST /wallet/purchase { transactions: [StoreKit 2 JWS, ...] }
//                                       → { wallet, granted, rejected }  App Store purchases (Bearer)
//
// The token is passed as a query parameter for WebSockets because neither
// browsers nor Unity's WebGL WebSocket can set headers on the upgrade request.
// ─────────────────────────────────────────────────────────────────────────────

export interface Env {
  ROOM: DurableObjectNamespace;
  MATCHMAKER: DurableObjectNamespace;
  ACCOUNTS: DurableObjectNamespace;
  TOKEN_SECRET: string;
  APPLE_AUDIENCES: string;
  GOOGLE_CLIENT_IDS: string;
  /** Local tests only: multiply every room timer by this */
  ROOM_TIME_SCALE?: string;
  /** Local tests only: multiply friend request expiry by this */
  REQUEST_TIME_SCALE?: string;
  /** Local tests only ('1'): POST /rooms may ask for a ranked room as if Find Match had made it */
  TEST_MATCHED?: string;
  /** Apple push: the .p8 key's contents (secret), its key id, the team id and the app's bundle id */
  APNS_KEY?: string;
  APNS_KEY_ID?: string;
  APNS_TEAM_ID?: string;
  APNS_TOPIC?: string;
  /** Sign in with Apple key (secret, the .p8 file's contents) and its key id: revokes Apple sign-in on account deletion */
  SIWA_KEY?: string;
  SIWA_KEY_ID?: string;
  /** Oldest app build (OnlineClient.ClientVersion) that may play online; "0" lets every build in */
  MIN_CLIENT_VERSION?: string;
  /** Local tests only: extra trusted root certificates (base64 DER, comma-separated) for App Store transactions */
  TEST_APPLE_ROOTS?: string;
}

/** Call the Accounts object; returns its JSON and HTTP status */
export async function accounts(env: Env, op: string, body: Record<string, unknown>): Promise<{ status: number; data: Record<string, unknown> }> {
  const stub = env.ACCOUNTS.get(env.ACCOUNTS.idFromName('global'));
  const res = await stub.fetch(`https://accounts/${op}`, { method: 'POST', body: JSON.stringify(body) });
  return { status: res.status, data: await res.json() as Record<string, unknown> };
}

const CODE_CHARS = 'ABCDEFGHJKLMNPQRSTUVWXYZ23456789'; // no 0/O or 1/I

function newCode(): string {
  const bytes = crypto.getRandomValues(new Uint8Array(6));
  return Array.from(bytes, b => CODE_CHARS[b % CODE_CHARS.length]).join('');
}

/** Create a room with a fresh code (retrying on the rare collision) */
export async function createRoom(env: Env, hostUserId: string, config: Partial<GameConfig>, isPublic: boolean, reserved?: Record<string, PlayerColor>, matched = false, ranked = matched): Promise<{ code: string; config: GameConfig }> {
  for (let attempt = 0; attempt < 5; attempt++) {
    const code = newCode();
    const stub = env.ROOM.get(env.ROOM.idFromName(code));
    const res = await stub.fetch('https://room/init', {
      method: 'POST',
      body: JSON.stringify({ code, hostUserId, isPublic, config, reserved, matched, ranked }),
    });
    if (res.ok) return await res.json() as { code: string; config: GameConfig };
  }
  throw new Error('Could not allocate a room code');
}

/**
 * The network a request came from, for per-network limits: the IPv4 address, or an IPv6
 * address's /64 (one home or phone gets a whole /64). Empty for this computer (local tests).
 */
function networkOf(ip: string | null): string {
  if (!ip || ip === '127.0.0.1' || ip === '::1') return '';
  if (!ip.includes(':')) return ip;
  const [head, tail = ''] = ip.split('::');
  const groups = head.split(':').filter(Boolean);
  if (groups.length < 4) groups.push(...Array(8 - groups.length - tail.split(':').filter(Boolean).length).fill('0'));
  return groups.slice(0, 4).join(':') + '::/64';
}

const CORS = {
  'Access-Control-Allow-Origin': '*',
  'Access-Control-Allow-Methods': 'GET, POST, OPTIONS',
  'Access-Control-Allow-Headers': 'Content-Type, Authorization, X-Client-Version',
};

function json(body: unknown, status = 200): Response {
  return Response.json(body, { status, headers: CORS });
}

export default {
  async fetch(request: Request, env: Env): Promise<Response> {
    const url = new URL(request.url);
    const path = url.pathname.replace(/\/+$/, '');

    if (request.method === 'OPTIONS') return new Response(null, { headers: CORS });

    const minClientVersion = Number(env.MIN_CLIENT_VERSION) || 0;
    if (path === '/health' || path === '') return json({ ok: true, service: '3d-chess', minClientVersion });

    const bearer = request.headers.get('Authorization')?.replace(/^Bearer\s+/i, '') ?? null;
    // Only the WebSocket routes take the token in the URL (sockets can't send headers); URLs end up in logs
    const socketRoute = path === '/matchmaking/connect' || /^\/rooms\/[A-Za-z0-9]{6}\/connect$/.test(path);

    // Builds too old to play online are told to update (builds from before this check send no version)
    const clientVersion = Number(request.headers.get('X-Client-Version') ?? (socketRoute ? url.searchParams.get('cv') : null)) || 0;
    if (clientVersion < minClientVersion && path !== '/account/delete') {
      return json({ error: 'A new version of 3D Chess Ultra is out. Please update to keep playing online.', updateRequired: true, minClientVersion }, 426);
    }

    const user = await verifyToken(env.TOKEN_SECRET, bearer ?? (socketRoute ? url.searchParams.get('token') : null));
    const body = request.method === 'POST' ? await request.json().catch(() => ({})) as Record<string, unknown> : {};
    const network = networkOf(request.headers.get('CF-Connecting-IP'));
    // Every Accounts call made for the caller carries their token's version, so a signed-out token stops working
    const caller = () => ({ userId: user?.userId, tokenV: user?.v ?? 0 });

    // A sign-in answer: the account plus a fresh token at its current version
    const signedIn = async (data: Record<string, unknown>): Promise<Response> => {
      const { v, ...rest } = data;
      const { token } = await issueToken(env.TOKEN_SECRET, data.userId as string, data.name as string, Number(v) || 0);
      return json({ ...rest, token });
    };

    // Run an Accounts call; when it identifies an account, hand back a fresh token for it
    const account = async (op: string, extra: Record<string, unknown> = {}, signsIn = false): Promise<Response> => {
      const { status, data } = await accounts(env, op, { ...extra, ...caller() });
      if (status !== 200 || !signsIn) return json(data, status);
      return signedIn(data);
    };
    const needUser = () => json({ error: 'Sign in first' }, 401);

    if (request.method === 'POST' && path === '/auth/guest') {
      // New accounts per network address per day are capped (the address is only kept as a hash)
      const { status, data } = await accounts(env, 'guest', { ip: network });
      if (status !== 200) return json(data, status);
      return signedIn(data);
    }
    if (request.method === 'POST' && path === '/auth/refresh') return user ? account('refresh', { name: user.name }, true) : needUser();
    if (request.method === 'POST' && (path === '/auth/apple' || path === '/auth/google')) {
      const provider: Provider = path.endsWith('apple') ? 'apple' : 'google';
      const audiences = (provider === 'apple' ? env.APPLE_AUDIENCES : env.GOOGLE_CLIENT_IDS ?? '').split(',').map(s => s.trim()).filter(Boolean);
      if (audiences.length === 0) return json({ error: `Sign in with ${provider} isn't set up yet` }, 501);
      const subject = typeof body.idToken === 'string' ? await verifyIdToken(provider, body.idToken, audiences, body.nonce) : null;
      if (!subject) return json({ error: 'Sign-in failed, please try again' }, 401);
      return account('login', { provider, subject }, true);
    }
    if (request.method === 'POST' && path === '/auth/gamecenter') {
      const bundleIds = env.APPLE_AUDIENCES.split(',').map(s => s.trim()).filter(Boolean);
      const subject = await verifyGameCenter(body, bundleIds);
      if (!subject) return json({ error: 'Game Center sign-in failed' }, 401);
      return account('login', { provider: 'gamecenter', subject }, true);
    }
    // Sign out everywhere: every token issued so far stops working; the answer holds a new one for
    // this device (an app signing out here too just drops it)
    if (request.method === 'POST' && path === '/auth/signout') return user ? account('signout', {}, true) : needUser();
    if (request.method === 'POST' && path === '/names/reroll') return user ? account('reroll', {}, true) : needUser();
    if (request.method === 'GET' && path === '/me') return user ? account('me') : needUser();
    if (request.method === 'POST' && path === '/link/code') return user ? account('link-code') : needUser();
    if (request.method === 'POST' && path === '/link/redeem') {
      // Wrong codes are counted per network, so codes can't be guessed by trying them all
      const { status, data } = await accounts(env, 'link-redeem', { code: body.code, ip: network });
      if (status !== 200) return json(data, status);
      return signedIn(data);
    }
    // Delete your account: Accounts forgets you, then your games are resigned and Apple sign-in revoked
    if (request.method === 'POST' && path === '/account/delete') {
      if (!user) return needUser();
      const { status, data } = await accounts(env, 'delete-account', caller());
      if (status !== 200) return json(data, status);
      for (const code of ((data.codes as string[]) ?? []).slice(0, 40)) { // well under the Worker's subrequest limit
        await env.ROOM.get(env.ROOM.idFromName(code)).fetch('https://room/remove-user', { method: 'POST', body: JSON.stringify({ userId: user.userId }) }).catch(() => undefined);
      }
      const code = typeof body.appleAuthorizationCode === 'string' ? body.appleAuthorizationCode : '';
      const appleRevoked = code ? await revokeApple(env, code, (data.appleSubjects as string[]) ?? [], typeof body.appleNonce === 'string' ? body.appleNonce : undefined).catch(() => false) : false;
      return json({ ok: true, appleRevoked });
    }

    // Push notifications: register this device, and read or change the notification switches
    if (path === '/push/register' && request.method === 'POST') return user ? account('push-register', { token: body.token, sandbox: body.sandbox === true }) : needUser();
    if (path === '/push/unregister' && request.method === 'POST') return user ? account('push-unregister', { token: body.token }) : needUser();
    if (path === '/push/settings') {
      if (!user) return needUser();
      return account('push-settings', request.method === 'POST' ? { prefs: body.prefs } : {});
    }

    // Pawns: the wallet, queued earnings, unlocking with pawns, and App Store purchases
    if (path.startsWith('/wallet')) {
      if (!user) return needUser();
      if (request.method === 'GET' && path === '/wallet') return account('wallet');
      if (request.method === 'POST' && path === '/wallet/sync') return account('wallet-sync', { migrate: body.migrate, events: body.events });
      if (request.method === 'POST' && path === '/wallet/spend') return account('wallet-spend', { requestId: body.requestId, item: body.item });
      if (request.method === 'POST' && path === '/wallet/purchase') {
        const list = Array.isArray(body.transactions) ? body.transactions.slice(0, ECONOMY.maxTransactionsPerCall) : [];
        const bundleIds = env.APPLE_AUDIENCES.split(',').map(s => s.trim()).filter(Boolean);
        const roots = [APPLE_ROOT_CA_G3, ...(env.TEST_APPLE_ROOTS ?? '').split(',').map(s => s.trim()).filter(Boolean)];
        const purchases = [];
        let rejected = 0;
        for (const jws of list) {
          const t = await verifyAppleTransaction(String(jws), bundleIds, roots);
          const item = t ? itemOfProduct(t.productId) : null;
          if (!t || !item) { rejected++; continue; }
          purchases.push({ originalTransactionId: t.originalTransactionId, transactionId: t.transactionId, item, environment: t.environment, revoked: !!t.revocationDate });
        }
        const { status, data } = await accounts(env, 'wallet-purchase', { ...caller(), purchases });
        return json(status === 200 ? { ...data, rejected } : data, status);
      }
    }

    // Ratings and stats: a player's profile, the leaderboards, and the Find Match cooldown
    if (request.method === 'GET' && (path === '/stats' || path === '/leaderboard' || path === '/matchmaking/cooldown')) {
      if (!user) return needUser();
      if (path === '/stats') return account('stats', { of: url.searchParams.get('of') ?? '' });
      if (path === '/leaderboard') return account('leaderboard', { mode: url.searchParams.get('mode'), scope: url.searchParams.get('scope') });
      return account('cooldown');
    }

    if (path.startsWith('/friends')) {
      if (!user) return needUser();
      if (request.method === 'GET' && path === '/friends') return account('friends');
      if (request.method === 'GET' && path === '/friends/search') return account('search', { query: url.searchParams.get('q') ?? '' });
      if (request.method === 'POST') {
        if (path === '/friends/request') return account('friend-request', { name: body.name });
        if (path === '/friends/respond') return account('friend-respond', { friendId: body.friendId, accept: body.accept });
        if (path === '/friends/remove') return account('friend-remove', { friendId: body.friendId });
        if (path === '/friends/block') return account('block', { friendId: body.friendId });
        if (path === '/friends/dismiss') return account('challenge-dismiss', { roomCode: body.roomCode });
        if (path === '/friends/challenge') {
          const allowed = await accounts(env, 'can-challenge', { ...caller(), friendId: body.friendId });
          if (allowed.status !== 200) return json(allowed.data, allowed.status);
          const room = await createRoom(env, user.userId, (body.config as Partial<GameConfig>) ?? {}, false);
          await accounts(env, 'challenge', { userId: user.userId, friendId: body.friendId, roomCode: room.code });
          return json(room);
        }
      }
    }

    // Your games in progress (or waiting for players), newest first, with whose turn it is
    if (path === '/games' && request.method === 'GET') {
      if (!user) return needUser();
      const { status, data } = await accounts(env, 'games', caller());
      if (status !== 200) return json(data, status);
      const games = [];
      for (const code of (data.codes as string[]) ?? []) {
        const res = await env.ROOM.get(env.ROOM.idFromName(code)).fetch(`https://room/info?userId=${encodeURIComponent(user.userId)}`);
        const info = res.ok ? await res.json() as Record<string, unknown> : null;
        if (!info || info.phase === 'gameover' || !info.yourColor) {
          await accounts(env, 'game-remove', { roomCode: code, userId: user.userId }); // gone or finished
          continue;
        }
        games.push(info);
      }
      return json({ games });
    }

    if (path === '/rooms' && request.method === 'POST') {
      if (!user) return json({ error: 'Sign in first' }, 401);
      // Rooms per account per day are capped (and this checks the token is still good)
      const allowed = await accounts(env, 'room-create', caller());
      if (allowed.status !== 200) return json(allowed.data, allowed.status);
      const matched = env.TEST_MATCHED === '1' && body.matched === true; // never in production
      const room = await createRoom(env, user.userId, (body.config as Partial<GameConfig>) ?? {}, body.isPublic === true, undefined, matched);
      return json(room);
    }

    const roomMatch = path.match(/^\/rooms\/([A-Za-z0-9]{6})(\/connect)?$/);
    if (roomMatch) {
      const code = roomMatch[1].toUpperCase();
      const stub = env.ROOM.get(env.ROOM.idFromName(code));
      if (!roomMatch[2]) {
        const res = await stub.fetch('https://room/info');
        return json(await res.json(), res.status);
      }
      if (!user) return new Response('Sign in first', { status: 401 });
      const valid = await accounts(env, 'check', caller());
      if (valid.status !== 200) return new Response('Sign in again', { status: 401 });
      return stub.fetch('https://room/connect', {
        // reveal=1: the app shows other players' setup when play starts (private setup)
        headers: { Upgrade: 'websocket', 'X-User-Id': user.userId, 'X-User-Name': user.name, 'X-Reveal': url.searchParams.get('reveal') === '1' ? '1' : '0' },
      });
    }

    if (path === '/matchmaking/connect') {
      if (!user) return new Response('Sign in first', { status: 401 });
      // Walked out of too many games lately: no Find Match until the cooldown ends
      const { status, data } = await accounts(env, 'cooldown', caller());
      if (status !== 200) return new Response('Sign in again', { status: 401 });
      if ((data.until as number) > 0) return new Response('Find Match is paused for you', { status: 403 });
      const stub = env.MATCHMAKER.get(env.MATCHMAKER.idFromName('global'));
      return stub.fetch('https://matchmaker/connect', {
        // X-Ranked: signed in to Game Center (one account per Apple ID), so this player's games can be rated
        headers: { Upgrade: 'websocket', 'X-User-Id': user.userId, 'X-User-Name': user.name, 'X-Ranked': data.ranked === true ? '1' : '0' },
      });
    }

    return json({ error: 'Not found' }, 404);
  },
};
