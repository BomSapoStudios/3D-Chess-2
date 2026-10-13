// Rematch checks: both say yes → new room with seats swapped; a no, a walk-out, or the 15 s running out calls it off.
//   npx wrangler dev --port 8788   then   node scripts/rematch-e2e.mjs http://localhost:8788   (real timers)
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

/** Put the rest of a side's pieces on the board (Ready needs all 8), skipping the given cells */
const SET = { King: 1, Queen: 1, Rook: 2, Bishop: 2, Knight: 2 };
async function fillRest(c, size, color, avoid = [], as) {
  const skip = new Set(avoid.map(p => `${p.x},${p.y},${p.z}`));
  const state = (await c.request({ type: 'room:state' })).state;
  for (const [type, count] of Object.entries(SET)) {
    let have = state.board.flat(2).filter(p => p && p.type === type && p.color === color).length;
    for (let x = 0; x < size && have < count; x++) for (let y = 0; y < size && have < count; y++) for (let z = 0; z < size && have < count; z++) {
      if (skip.has(`${x},${y},${z}`)) continue;
      const r = await c.request({ type: 'game:placePiece', pieceType: type, position: { x, y, z }, ...(as ? { as } : {}) });
      if (r.ok) { have++; skip.add(`${x},${y},${z}`); }
    }
  }
}


// Run against `npx wrangler dev --var ROOM_TIME_SCALE:0.0002` so every room timer is 5000x shorter:
const sleep = ms => new Promise(r => setTimeout(r, ms));
const guest = async name => (await post('/auth/guest', { name })).body;
const join = async (code, who) => { const c = await client(`${WS_BASE}/rooms/${code}/connect?token=${who.token}`); const j = await c.wait(m => m.type === 'room:joined'); return [c, j]; };

/** A finished 2-player game: Alice (White) resigns */
async function finishedGame(alice, bob, config = {}) {
  const room = (await post('/rooms', { config: { boardSize: '4x4x4', playerCount: 2, ...config } }, alice.token)).body;
  const [a] = await join(room.code, alice);
  const [b] = await join(room.code, bob);
  await a.request({ type: 'game:placePiece', pieceType: 'King', position: { x: 0, y: 0, z: 0 } });
  await b.request({ type: 'game:placePiece', pieceType: 'King', position: { x: 3, y: 3, z: 3 } });
  await fillRest(a, 4, 'White'); await fillRest(b, 4, 'Black');
  await a.request({ type: 'game:ready' }); await b.request({ type: 'game:ready' });
  await a.wait(m => m.type === 'game:phaseChanged');
  await a.request({ type: 'game:resign' });
  const over = await b.wait(m => m.type === 'game:stateUpdate' && m.state.phase === 'gameover');
  return { room, a, b, over };
}

// 1. Both say yes: a new room, Bob is now White (host) and Alice Black
{
  const alice = await guest('Alice'), bob = await guest('Bob');
  const { a, b, over } = await finishedGame(alice, bob);
  check(over.state.rematch && over.state.rematch.deadline > Date.now() && !over.state.rematch.failed, 'rematch offered when the game ends');
  check((await a.request({ type: 'room:rematch', accept: true })).ok, 'Alice says yes');
  const halfway = await b.wait(m => m.type === 'room:rematch');
  check(halfway.rematch.accepted.join() === 'White' && !halfway.rematch.code, 'Bob sees Alice is in, no room yet');
  check((await b.request({ type: 'room:rematch', accept: true })).ok, 'Bob says yes');
  const ready = await a.wait(m => m.type === 'room:rematch' && m.rematch.code);
  check(ready.rematch.newColors.White === 'Black' && ready.rematch.newColors.Black === 'White', 'seats swap');
  a.close(); b.close();
  const [b2, jb] = await join(ready.rematch.code, bob);
  const [a2, ja] = await join(ready.rematch.code, alice);
  check(jb.yourColor === 'White' && jb.isHost, 'Bob hosts the rematch as White');
  check(ja.yourColor === 'Black', 'Alice plays Black');
  const carol = await guest('Carol');
  let refused = false;
  try { await join(ready.rematch.code, carol); } catch { refused = true; }
  check(refused, 'nobody else can join a rematch room');
  const placing = await b2.wait(m => m.type === 'game:stateUpdate' || m.type === 'room:playerJoined');
  check(placing.state.phase === 'placement', 'rematch goes straight to setup once both are in');
  a2.close(); b2.close();
}

// 2. One says no: called off for everyone
{
  const alice = await guest('Alice'), bob = await guest('Bob');
  const { a, b } = await finishedGame(alice, bob);
  await a.request({ type: 'room:rematch', accept: true });
  await b.request({ type: 'room:rematch', accept: false });
  const off = await a.wait(m => m.type === 'room:rematch' && m.rematch.failed);
  check(off.rematch.declined.join() === 'Black' && !off.rematch.code, 'a no calls it off, and says who');
  check(!(await a.request({ type: 'room:rematch', accept: true })).ok, 'too late to say yes after that');
  a.close(); b.close();
}

// 3. One walks out: called off
{
  const alice = await guest('Alice'), bob = await guest('Bob');
  const { a, b } = await finishedGame(alice, bob);
  await a.request({ type: 'room:rematch', accept: true });
  b.close();
  const off = await a.wait(m => m.type === 'room:rematch' && m.rematch.failed);
  check(off.rematch.failed, 'leaving the result screen calls it off');
  a.close();
}

// 4. Nobody answers in 15 seconds: called off
{
  const alice = await guest('Alice'), bob = await guest('Bob');
  const { a, b } = await finishedGame(alice, bob);
  await a.request({ type: 'room:rematch', accept: true });
  const off = await a.wait(m => m.type === 'room:rematch' && m.rematch.failed, 20000);
  check(off.rematch.failed && Date.now() >= off.rematch.deadline, 'the countdown running out calls it off');
  a.close(); b.close();
}

// 5. Four players, one robot: the robot stays put, humans pass seats along
{
  const p = [await guest('P1'), await guest('P2'), await guest('P3')];
  const room = (await post('/rooms', { config: { boardSize: '6x6x6', playerCount: 4, aiColors: ['Purple'] } }, p[0].token)).body;
  const c = [];
  for (const who of p) c.push((await join(room.code, who))[0]);
  // Every human seat resigns but one (P1 resigns, P2 resigns: P3 wins, as the robot can't play here)
  for (const [i, color] of [[0, 'White'], [1, 'Black'], [2, 'Green']]) {
    await fillRest(c[i], 6, color);
    await c[i].request({ type: 'game:ready' });
  }
  await fillRest(c[0], 6, 'Purple', [], 'Purple');
  await c[0].request({ type: 'game:ready', as: 'Purple' });
  await c[0].wait(m => m.type === 'game:phaseChanged');
  await c[0].request({ type: 'game:resign' });
  await c[1].request({ type: 'game:resign' });
  await c[2].request({ type: 'game:resign' });
  await c[2].wait(m => m.type === 'game:stateUpdate' && m.state.phase === 'gameover', 5000).catch(() => null);
  for (const x of c) await x.request({ type: 'room:rematch', accept: true });
  const ready = await c[0].wait(m => m.type === 'room:rematch' && m.rematch.code);
  const n = ready.rematch.newColors;
  check(n.Black === 'White' && n.Green === 'Black' && n.White === 'Green', `humans pass seats along (${JSON.stringify(n)})`);
  for (const x of c) x.close();
}

console.log(failures ? `\n${failures} FAILED` : '\nAll rematch checks passed');
process.exit(failures ? 1 : 0);
