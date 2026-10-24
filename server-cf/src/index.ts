import { issueToken, verifyToken } from './auth.js';
import { verifyIdToken, type Provider } from './oidc.js';
import type { GameConfig, PlayerColor } from './types/game.js';

export { Room } from './room.js';
export { Matchmaker } from './matchmaker.js';
export { Accounts } from './accounts.js';

// ─────────────────────────────────────────────────────────────────────────────
// 3D Chess game server (Cloudflare Worker)
//
//   GET  /health                        → { ok: true }
//
//   Accounts (see accounts.ts). Calls that sign you in return { token, userId, name }.
//   POST /auth/guest                    → new account with a unique generated name
//   POST /auth/refresh                  → fresh token + current name   (Bearer)
//   POST /auth/apple   { idToken }      → sign in with Apple; links to the current
//   POST /auth/google  { idToken }        account (Bearer optional) or restores yours
//   POST /names/reroll                  → a new unique name            (Bearer)
//   GET  /me                            → { userId, name, providers }  (Bearer)
//   POST /link/code                     → { code } to type on another device (Bearer)
//   POST /link/redeem  { code }         → become that account on this device
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
//   GET  /matchmaking/cooldown          → { until } (Unix ms, 0 = not paused)
//   GET  /stats?of=<userId>             → a player's ratings and stats (yours without ?of)
//   GET  /leaderboard?mode=duel|melee&scope=friends|everyone
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
export async function createRoom(env: Env, hostUserId: string, config: Partial<GameConfig>, isPublic: boolean, reserved?: Record<string, PlayerColor>, matched = false): Promise<{ code: string; config: GameConfig }> {
  for (let attempt = 0; attempt < 5; attempt++) {
    const code = newCode();
    const stub = env.ROOM.get(env.ROOM.idFromName(code));
    const res = await stub.fetch('https://room/init', {
      method: 'POST',
      body: JSON.stringify({ code, hostUserId, isPublic, config, reserved, matched }),
    });
    if (res.ok) return await res.json() as { code: string; config: GameConfig };
  }
  throw new Error('Could not allocate a room code');
}

const CORS = {
  'Access-Control-Allow-Origin': '*',
  'Access-Control-Allow-Methods': 'GET, POST, OPTIONS',
  'Access-Control-Allow-Headers': 'Content-Type, Authorization',
};

function json(body: unknown, status = 200): Response {
  return Response.json(body, { status, headers: CORS });
}

export default {
  async fetch(request: Request, env: Env): Promise<Response> {
    const url = new URL(request.url);
    const path = url.pathname.replace(/\/+$/, '');

    if (request.method === 'OPTIONS') return new Response(null, { headers: CORS });

    if (path === '/health' || path === '') return json({ ok: true, service: '3d-chess' });

    const bearer = request.headers.get('Authorization')?.replace(/^Bearer\s+/i, '') ?? null;
    const user = await verifyToken(env.TOKEN_SECRET, bearer ?? url.searchParams.get('token'));
    const body = request.method === 'POST' ? await request.json().catch(() => ({})) as Record<string, unknown> : {};

    // Run an Accounts call; when it identifies an account, hand back a fresh token for it
    const account = async (op: string, extra: Record<string, unknown> = {}, signsIn = false): Promise<Response> => {
      const { status, data } = await accounts(env, op, { ...extra, userId: user?.userId });
      if (status !== 200 || !signsIn) return json(data, status);
      const { token } = await issueToken(env.TOKEN_SECRET, data.userId as string, data.name as string);
      return json({ ...data, token });
    };
    const needUser = () => json({ error: 'Sign in first' }, 401);

    if (request.method === 'POST' && path === '/auth/guest') {
      const { status, data } = await accounts(env, 'guest', {});
      if (status !== 200) return json(data, status);
      const { token } = await issueToken(env.TOKEN_SECRET, data.userId as string, data.name as string);
      return json({ ...data, token });
    }
    if (request.method === 'POST' && path === '/auth/refresh') return user ? account('refresh', { name: user.name }, true) : needUser();
    if (request.method === 'POST' && (path === '/auth/apple' || path === '/auth/google')) {
      const provider: Provider = path.endsWith('apple') ? 'apple' : 'google';
      const audiences = (provider === 'apple' ? env.APPLE_AUDIENCES : env.GOOGLE_CLIENT_IDS ?? '').split(',').map(s => s.trim()).filter(Boolean);
      if (audiences.length === 0) return json({ error: `Sign in with ${provider} isn't set up yet` }, 501);
      const subject = typeof body.idToken === 'string' ? await verifyIdToken(provider, body.idToken, audiences) : null;
      if (!subject) return json({ error: 'Sign-in failed, please try again' }, 401);
      return account('login', { provider, subject }, true);
    }
    if (request.method === 'POST' && path === '/names/reroll') return user ? account('reroll', {}, true) : needUser();
    if (request.method === 'GET' && path === '/me') return user ? account('me') : needUser();
    if (request.method === 'POST' && path === '/link/code') return user ? account('link-code') : needUser();
    if (request.method === 'POST' && path === '/link/redeem') {
      const { status, data } = await accounts(env, 'link-redeem', { code: body.code });
      if (status !== 200) return json(data, status);
      const { token } = await issueToken(env.TOKEN_SECRET, data.userId as string, data.name as string);
      return json({ ...data, token });
    }
    // Push notifications: register this device, and read or change the notification switches
    if (path === '/push/register' && request.method === 'POST') return user ? account('push-register', { token: body.token, sandbox: body.sandbox === true }) : needUser();
    if (path === '/push/unregister' && request.method === 'POST') return account('push-unregister', { token: body.token });
    if (path === '/push/settings') {
      if (!user) return needUser();
      return account('push-settings', request.method === 'POST' ? { prefs: body.prefs } : {});
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
          const allowed = await accounts(env, 'can-challenge', { userId: user.userId, friendId: body.friendId });
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
      const { data } = await accounts(env, 'games', { userId: user.userId });
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
      return stub.fetch('https://room/connect', {
        // reveal=1: the app shows other players' setup when play starts (private setup)
        headers: { Upgrade: 'websocket', 'X-User-Id': user.userId, 'X-User-Name': user.name, 'X-Reveal': url.searchParams.get('reveal') === '1' ? '1' : '0' },
      });
    }

    if (path === '/matchmaking/connect') {
      if (!user) return new Response('Sign in first', { status: 401 });
      // Walked out of too many games lately: no Find Match until the cooldown ends
      const { data } = await accounts(env, 'cooldown', { userId: user.userId });
      if ((data.until as number) > 0) return new Response('Find Match is paused for you', { status: 403 });
      const stub = env.MATCHMAKER.get(env.MATCHMAKER.idFromName('global'));
      return stub.fetch('https://matchmaker/connect', {
        headers: { Upgrade: 'websocket', 'X-User-Id': user.userId, 'X-User-Name': user.name },
      });
    }

    return json({ error: 'Not found' }, 404);
  },
};
