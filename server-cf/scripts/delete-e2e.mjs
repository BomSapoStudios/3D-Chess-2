// End-to-end check of account deletion: everything about the player goes, their games are
// resigned, and neither their old token nor a game finishing later brings the account back.
//   node scripts/delete-e2e.mjs [baseUrl]
import WebSocket from 'ws';

const BASE = process.argv[2] ?? 'http://localhost:8787';
const WS_BASE = BASE.replace(/^http/, 'ws');
let failures = 0;
const check = (cond, label) => { console.log(`${cond ? 'PASS' : 'FAIL'}  ${label}`); if (!cond) failures++; };

async function call(method, path, body, token) {
  const res = await fetch(BASE + path, {
    method,
    headers: { 'Content-Type': 'application/json', ...(token ? { Authorization: `Bearer ${token}` } : {}) },
    body: method === 'POST' ? JSON.stringify(body ?? {}) : undefined,
  });
  return { status: res.status, body: await res.json().catch(() => ({})) };
}
const post = (p, b, t) => call('POST', p, b, t);
const get = (p, t) => call('GET', p, undefined, t);

/** Join a room and collect what it sends */
function join(code, token) {
  return new Promise((resolve, reject) => {
    const ws = new WebSocket(`${WS_BASE}/rooms/${code}/connect?token=${token}&reveal=1`);
    const inbox = [];
    ws.on('message', d => inbox.push(JSON.parse(d.toString())));
    ws.on('error', reject);
    ws.on('unexpected-response', (_q, r) => reject(new Error(`HTTP ${r.statusCode}`)));
    ws.on('open', () => resolve({ ws, inbox }));
  });
}
const until = async (cond, ms = 4000) => { const end = Date.now() + ms; while (!cond() && Date.now() < end) await new Promise(r => setTimeout(r, 50)); return cond(); };

const a = (await post('/auth/guest')).body;
const b = (await post('/auth/guest')).body;
const c = (await post('/auth/guest')).body;

// A and B are friends; C has blocked A
await post('/friends/request', { name: a.name }, b.token);
await post('/friends/respond', { friendId: b.userId, accept: true }, a.token);
await post('/friends/block', { friendId: a.userId }, c.token);
check((await get('/friends', b.token)).body.friends.some(f => f.userId === a.userId), 'before: B has A as a friend');

// A hosts a lobby and is in a started game with B
const lobby = (await post('/rooms', { config: { boardSize: '5x5x5', playerCount: 2 } }, a.token)).body;
const la = await join(lobby.code, a.token);
const game = (await post('/rooms', { config: { boardSize: '5x5x5', playerCount: 2 } }, b.token)).body;
const gb = await join(game.code, b.token);
const ga = await join(game.code, a.token);
await until(() => gb.inbox.some(m => m.type === 'game:phaseChanged' || m.state?.phase === 'placement'));
check((await get('/games', a.token)).body.games?.length === 2, 'before: A is in two games');

// Delete
check((await post('/account/delete', {})).status === 401, 'deleting needs sign-in');
const del = await post('/account/delete', {}, a.token);
check(del.status === 200 && del.body.ok === true, 'A deletes their account');
check(del.body.appleRevoked === false, 'no Apple sign-in to revoke for a guest');

const closing = ws => ws.readyState === WebSocket.CLOSING || ws.readyState === WebSocket.CLOSED;
check(await until(() => closing(la.ws) && closing(ga.ws)), "A's game connections are closed");
check((await get(`/rooms/${lobby.code}`)).status === 404, "A's lobby is closed");
check(await until(() => gb.inbox.some(m => m.type === 'game:over')), 'B sees the game end (it was still in setup)');

check((await post('/auth/refresh', {}, a.token)).status === 401, "A's old token can't sign in or bring the account back");
check((await get('/me', a.token)).status === 401, '/me no longer knows A');
check((await post('/friends/request', { name: a.name }, c.token)).status === 404, "A's name finds nobody");
check(!(await get('/friends', b.token)).body.friends.some(f => f.userId === a.userId), 'A is gone from B\'s friends');
check((await get(`/stats?of=${a.userId}`, b.token)).status === 401, "A's profile is gone");
check((await post('/account/delete', {}, a.token)).status === 401, 'deleting twice is refused');

// The deleted player starts fresh as a new guest
const fresh = (await post('/auth/guest')).body;
check(fresh.userId !== a.userId && fresh.token, 'the app can start again with a new account');

for (const s of [gb.ws]) s.close();
console.log(failures === 0 ? '\nAll checks passed' : `\n${failures} check(s) failed`);
process.exit(failures === 0 ? 0 : 1);
