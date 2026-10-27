// "Please update" checks: builds older than MIN_CLIENT_VERSION are turned away with 426, newer ones play.
//   npx wrangler dev --port 8789 --var MIN_CLIENT_VERSION:2   then   node scripts/update-e2e.mjs http://localhost:8789
import WebSocket from 'ws';

const BASE = process.argv[2] ?? 'http://localhost:8789';
const WS_BASE = BASE.replace(/^http/, 'ws');
let failures = 0;
const check = (cond, label) => { console.log(`${cond ? 'PASS' : 'FAIL'}  ${label}`); if (!cond) failures++; };

async function post(path, body, token, version) {
  const res = await fetch(BASE + path, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', ...(token ? { Authorization: `Bearer ${token}` } : {}), ...(version != null ? { 'X-Client-Version': String(version) } : {}) },
    body: JSON.stringify(body ?? {}),
  });
  return { status: res.status, body: await res.json().catch(() => ({})) };
}
const socketStatus = url => new Promise(resolve => {
  const ws = new WebSocket(url);
  ws.on('open', () => { ws.close(); resolve(101); });
  ws.on('unexpected-response', (_q, r) => resolve(r.statusCode));
  ws.on('error', () => resolve(0));
});

const health = await (await fetch(BASE + '/health')).json();
check(health.minClientVersion === 2, `/health says the oldest build allowed (${health.minClientVersion})`);

const old = await post('/auth/guest', {}, null, null);
check(old.status === 426 && old.body.updateRequired === true, 'a build from before the check (no version) is asked to update');
check((await post('/auth/guest', {}, null, 1)).status === 426, 'an older build is asked to update');

const fresh = await post('/auth/guest', {}, null, 2);
check(fresh.status === 200 && fresh.body.token, 'the current build signs in');
check((await post('/auth/refresh', {}, fresh.body.token, 3)).status === 200, 'a newer build is fine too');

const room = (await post('/rooms', { config: { boardSize: '5x5x5', playerCount: 2 } }, fresh.body.token, 2)).body;
check(await socketStatus(`${WS_BASE}/rooms/${room.code}/connect?token=${fresh.body.token}`) === 426, 'an old build can\'t open a game socket');
check(await socketStatus(`${WS_BASE}/rooms/${room.code}/connect?token=${fresh.body.token}&cv=2`) === 101, 'the current build can');
check(await socketStatus(`${WS_BASE}/matchmaking/connect?token=${fresh.body.token}`) === 426, 'an old build can\'t join Find Match');

check((await post('/account/delete', {}, fresh.body.token, null)).status === 200, 'an old build can still delete its account');

console.log(failures === 0 ? '\nAll checks passed' : `\n${failures} check(s) failed`);
process.exit(failures === 0 ? 0 : 1);
