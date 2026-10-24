// Ratings, stats and walk-out checks: results are rated, stats and leaderboards add up, walking out three times pauses Find Match.
//   npx wrangler dev --var ROOM_TIME_SCALE:0.0002 --var REQUEST_TIME_SCALE:0.000001   then   node scripts/ratings-e2e.mjs
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
const get = (path, who) => fetch(BASE + path, { headers: { Authorization: `Bearer ${who.token}` } }).then(async r => ({ status: r.status, body: await r.json().catch(() => null) }));

/** Start a 2-player game between host and guest; returns both sockets once play begins */
async function playing(host, guestP, extra = { timedMode: true, timePerPlayerSeconds: 1800 }) {
  const room = (await post('/rooms', { config: { boardSize: '4x4x4', playerCount: 2, ...extra } }, host.token)).body;
  const [a] = await join(room.code, host);
  const [b] = await join(room.code, guestP);
  await fillRest(a, 4, 'White'); await fillRest(b, 4, 'Black');
  await a.request({ type: 'game:ready' }); await b.request({ type: 'game:ready' });
  await a.wait(m => m.type === 'game:phaseChanged');
  return { room, a, b };
}

// 1. A rated game: Alice resigns, Bob gains what Alice loses
const alice = await guest('Alice'), bob = await guest('Bob');
{
  const { a, b } = await playing(alice, bob);
  await a.request({ type: 'game:resign' });
  const rated = await b.wait(m => m.type === 'game:rated', 8000);
  const w = rated.ratings.White, k = rated.ratings.Black;
  check(w && k && w.mode === 'duel', 'both players get a Duel rating change');
  check(w.after < 1000 && k.after > 1000 && Math.round(1000 - w.after) === Math.round(k.after - 1000), `the loser drops what the winner gains (${w.after} / ${k.after})`);
  check(k.after - 1000 === 20, `new players move 20 for an even game (K 40): ${k.after - 1000}`);
  a.close(); b.close();
}

// 1b. Unranked games (no clock, or Chaos Mode) don't change ratings
for (const [label, extra] of [['an untimed game', {}], ['a Chaos Mode game', { timedMode: true, timePerPlayerSeconds: 1800, chaosMode: true }]]) {
  const x = await guest('Xena'), y = await guest('Yuri');
  const { room, a, b } = await playing(x, y, extra);
  const st = (await a.request({ type: 'room:state' })).state;
  check(st.ranked === false, `${label} is marked unranked`);
  await a.request({ type: 'game:resign' });
  await b.wait(m => m.type === 'game:over', 8000);
  await sleep(300);
  check(!b.inbox.some(m => m.type === 'game:rated'), `${label} changes nobody's rating`);
  a.close(); b.close();
}

// 2. Stats and head to head
{
  const mine = (await get('/stats', bob)).body;
  check(mine.games === 1 && mine.wins === 1 && mine.duel.rating === 1020 && mine.streak === 1, `Bob's stats add up (${mine.games} games, ${mine.wins} wins, ${mine.duel.rating})`);
  check(mine.recent[0]?.opponents[0] === alice.name && mine.recent[0].change === 20, 'recent games list the opponent and the change');
  check(mine.endings.resignation === 1, 'the ending is counted as a resignation');
  check(mine.captures && mine.captures.taken === 0 && mine.captures.lost === 0, 'captures are counted (none in a resigned game)');
  const theirs = (await get(`/stats?of=${bob.userId}`, alice)).body;
  check(theirs.headToHead && theirs.headToHead.losses === 1 && theirs.headToHead.wins === 0, 'Alice sees her record against Bob');
  check(theirs.tier === 'Pawn', 'tier is Pawn until 5 rated games');
}

// 3. Leaderboards: everyone needs 5 games; friends shows friends and you
{
  const everyone = (await get('/leaderboard?mode=duel&scope=everyone', bob)).body;
  check(!everyone.players.some(p => p.userId === bob.userId), 'players with under 5 games stay off the main leaderboard');
  await post('/friends/request', { name: alice.name }, bob.token);
  await post('/friends/request', { name: bob.name }, alice.token);
  const friends = (await get('/leaderboard?mode=duel&scope=friends', bob)).body;
  check(friends.players.length === 2 && friends.players[0].userId === bob.userId, `friends leaderboard: Bob above Alice (${friends.players.map(p => p.name).join(', ')})`);
  const list = (await get('/friends', bob)).body;
  check(list.friends[0]?.tier === 'Pawn', 'friends list carries a tier badge');
}

// 4. Walking out three times in a day pauses Find Match
{
  const carol = await guest('Carol'), dan = await guest('Dan');
  check((await get('/matchmaking/cooldown', carol)).body.until === 0, 'no cooldown to start with');
  for (let i = 0; i < 3; i++) {
    const { a, b } = await playing(dan, carol);
    b.close(); // Carol walks out; the 5 minute reconnect window is a few ms here
    await a.wait(m => m.type === 'game:rated', 15000);
    a.close();
  }
  const cd = (await get('/matchmaking/cooldown', carol)).body;
  check(cd.until > Date.now(), `three walk-outs pause Find Match (until ${new Date(cd.until).toISOString()})`);
  let refusal = 'connected';
  try { (await client(`${WS_BASE}/matchmaking/connect?token=${carol.token}`)).close(); } catch (e) { refusal = e.message; }
  check(refusal === 'HTTP 403', `Find Match refuses while paused (${refusal})`);
  const s = (await get('/stats', carol)).body;
  check(s.endings.walkout === 3 && s.walkoutRate === 100, `walk-outs show in stats (${s.endings.walkout}, ${s.walkoutRate}%)`);
}

// 5. Melee: places by finishing order, robots left out
{
  const p = [await guest('P1'), await guest('P2'), await guest('P3')];
  const room = (await post('/rooms', { config: { boardSize: '6x6x6', playerCount: 4, aiColors: ['Purple'], timedMode: true, timePerPlayerSeconds: 1800 } }, p[0].token)).body;
  const c = [];
  for (const who of p) c.push((await join(room.code, who))[0]);
  for (const [i, color] of [[0, 'White'], [1, 'Black'], [2, 'Green']]) { await fillRest(c[i], 6, color); await c[i].request({ type: 'game:ready' }); }
  await fillRest(c[0], 6, 'Purple', [], 'Purple');
  await c[0].request({ type: 'game:ready', as: 'Purple' });
  await c[0].wait(m => m.type === 'game:phaseChanged');
  await c[0].request({ type: 'game:resign' });   // White out first
  await c[1].request({ type: 'game:resign' });   // then Black
  await c[0].request({ type: 'game:resign', as: 'Purple' }); // the robot too: Green wins
  const rated = await c[2].wait(m => m.type === 'game:rated', 8000);
  const r = rated.ratings;
  check(r.Green?.mode === 'melee' && !r.Purple, 'melee rating, robots not rated');
  check(r.Green.after > 1000 && r.White.after < r.Black.after && r.Black.after < r.Green.after, `places follow finishing order (Green ${r.Green.after}, Black ${r.Black.after}, White ${r.White.after})`);
  for (const x of c) x.close();
}

console.log(failures ? `\n${failures} FAILED` : '\nAll ratings checks passed');
process.exit(failures ? 1 : 0);
