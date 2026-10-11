// Room lifecycle checks: lobby expiry, Extend, reconnect windows, Take your time pace, replay, cleanup.
//   npx wrangler dev --var ROOM_TIME_SCALE:0.0002 --var REQUEST_TIME_SCALE:0.000001   then   node scripts/lifecycle-e2e.mjs
import WebSocket from 'ws';

const BASE = process.argv[2] ?? 'http://localhost:8787';
const WS_BASE = BASE.replace(/^http/, 'ws');
let failures = 0;

function check(cond, label) {
  console.log(`${cond ? 'PASS' : 'FAIL'}  ${label}`);
  if (!cond) failures++;
}

async function post(path, body, token) {
  const res = await fetch(BASE + path, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', ...(token ? { Authorization: `Bearer ${token}` } : {}) },
    body: JSON.stringify(body),
  });
  return { status: res.status, body: await res.json() };
}

/** A connected client that records messages and can wait for one */
function client(url) {
  return new Promise((resolve, reject) => {
    const ws = new WebSocket(url);
    const inbox = [];
    const waiters = [];
    let nextId = 1;
    ws.on('message', data => {
      const msg = JSON.parse(data.toString());
      inbox.push(msg);
      for (const w of [...waiters]) if (w.match(msg)) { waiters.splice(waiters.indexOf(w), 1); w.resolve(msg); }
    });
    ws.on('error', reject);
    ws.on('unexpected-response', (_req, res) => reject(new Error(`HTTP ${res.statusCode}`)));
    ws.on('open', () => resolve({
      ws,
      inbox,
      wait(match, ms = 5000) {
        const found = inbox.find(match);
        if (found) { inbox.splice(inbox.indexOf(found), 1); return Promise.resolve(found); }
        return new Promise((res, rej) => {
          const w = { match: m => { if (match(m)) { inbox.splice(inbox.indexOf(m), 1); return true; } return false; }, resolve: res };
          waiters.push(w);
          setTimeout(() => rej(new Error('timeout waiting for message')), ms);
        });
      },
      async request(msg) {
        const requestId = nextId++;
        ws.send(JSON.stringify({ ...msg, requestId }));
        return this.wait(m => m.type === 'ack' && m.requestId === requestId);
      },
      close() { ws.close(); },
    }));
  });
}


// Run against `npx wrangler dev --var ROOM_TIME_SCALE:0.0002` so every room timer is 5000x shorter:
// lobby 10 min → 0.12 s (alarms fire no sooner than 1 s), reconnect 5 min → 0.06 s, move day → 17 s.
const get = (path, token) => fetch(BASE + path, { headers: { Authorization: `Bearer ${token}` } }).then(async r => ({ status: r.status, body: await r.json() }));
const sleep = ms => new Promise(r => setTimeout(r, ms));
const info = code => fetch(`${BASE}/rooms/${code}`).then(r => r.status);

const alice = (await post('/auth/guest', { name: 'Alice' })).body;
const bob = (await post('/auth/guest', { name: 'Bob' })).body;
const join = async (code, who) => { const c = await client(`${WS_BASE}/rooms/${code}/connect?token=${who.token}`); const j = await c.wait(m => m.type === 'room:joined'); return [c, j]; };
async function startGame(config) {
  const room = (await post('/rooms', { config: { boardSize: '4x4x4', playerCount: 2, ...config } }, alice.token)).body;
  const [a] = await join(room.code, alice);
  const [b] = await join(room.code, bob);
  await a.request({ type: 'game:placePiece', pieceType: 'King', position: { x: 0, y: 0, z: 0 } });
  await b.request({ type: 'game:placePiece', pieceType: 'King', position: { x: 3, y: 3, z: 3 } });
  await a.request({ type: 'game:ready' }); await b.request({ type: 'game:ready' });
  await a.wait(m => m.type === 'game:phaseChanged');
  return { room, a, b };
}

// Settings
const slowRoom = (await post('/rooms', { config: { pace: 'slow', timedMode: true } }, alice.token)).body;
check(slowRoom.config.pace === 'slow' && slowRoom.config.timedMode === false, 'Take your time pace is accepted and replaces the chess clock');
const liveRoom = (await post('/rooms', { config: {} }, alice.token)).body;
check(liveRoom.config.pace === 'live', 'rooms are Live by default');

// Live lobby nobody joins closes on its own
{
  const [a, j] = await join(liveRoom.code, alice);
  check(typeof j.state.lobbyClosesAt === 'number', 'the lobby tells the host when it closes');
  const closed = await a.wait(m => m.type === 'room:closed', 5000).catch(() => null);
  check(closed?.reason === 'expired', 'an unjoined Live lobby closes and tells the host');
  await sleep(500);
  check(await info(liveRoom.code) === 404, 'the closed room is gone');
}

// Host can extend a lobby; the lobby closes after the host walks away
{
  const [a, j] = await join(slowRoom.code, alice);
  const ext = await a.request({ type: 'room:extend' });
  check(ext.ok && ext.expiresAt > j.state.lobbyClosesAt, 'the host can keep the lobby open longer');
  a.close();
  await sleep(3000);
  check(await info(slowRoom.code) === 404, 'a lobby closes after the host leaves it');
}

// Live: a player who drops and doesn't come back forfeits; the finished room is cleared once empty
{
  const { room, a, b } = await startGame({ pace: 'live' });
  b.close();
  const left = await a.wait(m => m.type === 'room:playerLeft');
  check(left.graceMs > 0, 'Live: the other player is told how long the seat is held');
  const over = await a.wait(m => m.type === 'game:over', 5000).catch(() => null);
  check(over?.winner === 'White', 'Live: a player who never comes back forfeits');
  a.close();
  await sleep(3000);
  check(await info(room.code) === 404, 'a finished room is cleared after everyone leaves');
}

// Take your time: leaving is fine, missed moves are replayed, and a day without moving forfeits
{
  const { room, a, b } = await startGame({ pace: 'slow' });
  const seqBefore = (await b.request({ type: 'room:state' })).state.seq;
  b.close();
  const left = await a.wait(m => m.type === 'room:playerLeft');
  check(left.graceMs === null, 'Take your time: no reconnect countdown');
  await sleep(2500);
  const moved = await a.request({ type: 'game:move', from: { x: 1, y: 0, z: 0 }, to: { x: 2, y: 0, z: 0 } });
  check(moved.ok, 'Take your time: the game goes on while the opponent is away');
  const [b2, back] = await join(room.code, bob);
  check(back.state.phase === 'playing' && back.state.moveDeadline > Date.now(), 'the returning player sees their move deadline');
  const replay = await b2.request({ type: 'room:replay', since: seqBefore });
  const missed = b2.inbox.filter(m => m.type === 'game:moveValidated' && m.color === 'White');
  check(replay.ok && missed.length === 1, 'the missed move is replayed on return');
  const over = await a.wait(m => m.type === 'game:over', 30000).catch(() => null);
  check(over?.winner === 'White' && over.reason === 'timeout', 'Take your time: a player who doesn\'t move in a day loses');
  a.close(); b2.close();
}

// Live: both players connected but nobody moves (app left open) - the player on move times out,
// then the finished room closes even though both are still connected
{
  const { room, a, b } = await startGame({ pace: 'live' });
  const over = await a.wait(m => m.type === 'game:over', 8000).catch(() => null);
  check(over?.reason === 'timeout' && over.winner === 'Black', 'Live: a game nobody plays ends (the player on move times out)');
  const closed = await b.wait(m => m.type === 'room:closed', 8000).catch(() => null);
  check(closed?.reason === 'finished', 'a finished room closes itself even with players still connected');
  await sleep(500);
  check(await info(room.code) === 404, 'the finished room is deleted');
  a.close(); b.close();
}

// Find Match: nobody waits in the queue forever
{
  const q = await client(`${WS_BASE}/matchmaking/connect?token=${alice.token}`);
  q.ws.send(JSON.stringify({ type: 'matchmaking:join', config: { boardSize: '8x8x8', playerCount: 6 } }));
  const timedOut = await q.wait(m => m.type === 'matchmaking:timeout', 8000).catch(() => null);
  check(!!timedOut, 'Find Match gives up after a while instead of waiting forever');
  q.close();
}

// Friend requests expire (REQUEST_TIME_SCALE shrinks 30 days to about 2.6 s); an expired request can be sent again
{
  await post('/friends/request', { name: alice.name }, bob.token);
  check((await get('/friends', alice.token)).body.friends.some(f => f.userId === bob.userId && f.status === 'incoming'), 'friend request arrives');
  await sleep(4000);
  check(!(await get('/friends', alice.token)).body.friends.some(f => f.userId === bob.userId), 'an unanswered request expires');
  const again = await post('/friends/request', { name: alice.name }, bob.token);
  check(again.body.status === 'outgoing', 'an expired request can be sent again');
}

console.log(failures === 0 ? '\nAll checks passed' : `\n${failures} check(s) failed`);
process.exit(failures === 0 ? 0 : 1);
