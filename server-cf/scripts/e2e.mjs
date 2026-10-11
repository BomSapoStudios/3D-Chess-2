// End-to-end check against a running server (default: `npm run dev` on :8787).
// Two guests create and join a room, place kings, play moves, test a bad move,
// a reconnect, resignation, and Find Match.
//   node scripts/e2e.mjs [baseUrl]
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

const health = await fetch(BASE + '/health').then(r => r.json());
check(health.ok === true, 'health check');

const alice = (await post('/auth/guest', { name: 'Alice' })).body;
const bob = (await post('/auth/guest', { name: 'Bob' })).body;
check(alice.token && bob.token && alice.userId !== bob.userId, 'guest tokens issued');

const unauth = await post('/rooms', { config: {} });
check(unauth.status === 401, 'creating a room needs a token');

const room = (await post('/rooms', { config: { boardSize: '4x4x4', playerCount: 2 } }, alice.token)).body;
check(/^[A-Z2-9]{6}$/.test(room.code), `room created (${room.code})`);

const a = await client(`${WS_BASE}/rooms/${room.code}/connect?token=${alice.token}`);
const aJoined = await a.wait(m => m.type === 'room:joined');
check(aJoined.yourColor === 'White' && aJoined.isHost, 'host seated as White');

const b = await client(`${WS_BASE}/rooms/${room.code}/connect?token=${bob.token}`);
const bJoined = await b.wait(m => m.type === 'room:joined');
check(bJoined.yourColor === 'Black', 'second player seated as Black');
check(bJoined.state.phase === 'placement', 'room moves to placement when full');
await a.wait(m => m.type === 'room:playerJoined');

const info = await fetch(`${BASE}/rooms/${room.code}`).then(r => r.json());
check(info.openSeats === 0 && info.players.length === 2, 'room summary shows both players');

// Placement: kings plus a rook for White
check((await a.request({ type: 'game:placePiece', pieceType: 'King', position: { x: 0, y: 0, z: 0 } })).ok, 'White places King');
check((await a.request({ type: 'game:placePiece', pieceType: 'Rook', position: { x: 0, y: 3, z: 3 } })).ok, 'White places Rook');
check((await b.request({ type: 'game:placePiece', pieceType: 'King', position: { x: 3, y: 3, z: 3 } })).ok, 'Black places King');
const badZone = await b.request({ type: 'game:placePiece', pieceType: 'Queen', position: { x: 0, y: 1, z: 1 } });
check(!badZone.ok, `placement outside your zone is refused (${badZone.error})`);

await a.request({ type: 'game:ready' });
await b.request({ type: 'game:ready' });
const started = await a.wait(m => m.type === 'game:phaseChanged' && m.phase === 'playing');
check(!!started, 'game starts when both are ready');
const startState = (await a.request({ type: 'room:state' })).state;
const pawnAt = (x, y, z) => startState.board[x][y][z]?.type === 'Pawn';
check(pawnAt(1, 0, 0) && pawnAt(1, 3, 3) && pawnAt(2, 3, 3), 'a pawn spawns in front of each placed piece (like the game)');

// Moves
const outOfTurn = await b.request({ type: 'game:move', from: { x: 3, y: 3, z: 3 }, to: { x: 3, y: 2, z: 3 } });
check(!outOfTurn.ok, `moving out of turn is refused (${outOfTurn.error})`);
const m1 = await a.request({ type: 'game:move', from: { x: 1, y: 0, z: 0 }, to: { x: 2, y: 0, z: 0 } });
check(m1.ok, `White moves a pawn${m1.ok ? '' : ` (${m1.error})`}`);
const seen = await b.wait(m => m.type === 'game:moveValidated' && m.color === 'White');
check(!!seen, 'Black sees White\'s move');
const illegal = await b.request({ type: 'game:move', from: { x: 3, y: 3, z: 3 }, to: { x: 0, y: 0, z: 0 } });
check(!illegal.ok, `illegal move is refused (${illegal.error})`);

// Reconnect: Bob drops and comes back to the same seat
b.close();
await a.wait(m => m.type === 'room:playerLeft' && m.color === 'Black');
const b2 = await client(`${WS_BASE}/rooms/${room.code}/connect?token=${bob.token}`);
const back = await b2.wait(m => m.type === 'room:joined');
check(back.yourColor === 'Black' && back.state.turnNumber >= 1, 'reconnect keeps seat and game state');
const m2 = await b2.request({ type: 'game:move', from: { x: 3, y: 3, z: 3 }, to: { x: 3, y: 2, z: 3 } });
check(m2.ok, `Black moves after reconnecting${m2.ok ? '' : ` (${m2.error})`}`);

// A stranger can't take a seat in a full room
const carol = (await post('/auth/guest', { name: 'Carol' })).body;
let refused = false;
try { await client(`${WS_BASE}/rooms/${room.code}/connect?token=${carol.token}`); } catch { refused = true; }
check(refused, 'full room refuses a third player');

// Resignation ends the game
await b2.request({ type: 'game:resign' });
const over = await a.wait(m => m.type === 'game:over');
check(over.winner === 'White' && over.reason === 'resignation', 'resignation ends the game for White');
a.close(); b2.close();

// Find Match pairs two waiting players
const m1c = await client(`${WS_BASE}/matchmaking/connect?token=${alice.token}`);
const m2c = await client(`${WS_BASE}/matchmaking/connect?token=${carol.token}`);
m1c.ws.send(JSON.stringify({ type: 'matchmaking:join', config: { boardSize: '4x4x4', playerCount: 2 } }));
await m1c.wait(m => m.type === 'matchmaking:queued');
m2c.ws.send(JSON.stringify({ type: 'matchmaking:join', config: { boardSize: '4x4x4', playerCount: 2 } }));
const f1 = await m1c.wait(m => m.type === 'matchmaking:found');
const f2 = await m2c.wait(m => m.type === 'matchmaking:found');
check(f1.roomCode && f1.roomCode === f2.roomCode, `Find Match pairs players into one room (${f1.roomCode})`);

// Robot seats: the host's device plays them
const botRoom = (await post('/rooms', { config: { boardSize: '6x6x6', playerCount: 4, aiColors: ['Black', 'Purple'], chaosMode: true, chaosInterval: 5 } }, alice.token)).body;
check(botRoom.config.aiColors?.length === 2 && botRoom.config.chaosInterval === 5, `room with two robot seats (${botRoom.code})`);
const h = await client(`${WS_BASE}/rooms/${botRoom.code}/connect?token=${alice.token}`);
const hJoined = await h.wait(m => m.type === 'room:joined');
check(hJoined.state.players.length === 3 && hJoined.state.phase === 'waiting', 'robots are seated, one human seat still open');
const g = await client(`${WS_BASE}/rooms/${botRoom.code}/connect?token=${carol.token}`);
const gJoined = await g.wait(m => m.type === 'room:joined');
check(gJoined.yourColor === 'Green' && gJoined.state.phase === 'placement', 'the human guest gets the open seat and placement starts');
const asRobot = await h.request({ type: 'game:placePiece', as: 'Black', pieceType: 'King', position: { x: 5, y: 4, z: 2 } });
  const outside = await h.request({ type: 'game:placePiece', as: 'Black', pieceType: 'Queen', position: { x: 5, y: 0, z: 0 } });
  check(!outside.ok, 'big-board placement zones match the game');
check(asRobot.ok, `host places a piece for a robot${asRobot.ok ? '' : ` (${asRobot.error})`}`);
const notHost = await g.request({ type: 'game:placePiece', as: 'Purple', pieceType: 'King', position: { x: 2, y: 1, z: 5 } });
check(!notHost.ok, 'only the host can act for robots');
h.close(); g.close();

console.log(failures === 0 ? '\nAll checks passed' : `\n${failures} check(s) failed`);
process.exit(failures === 0 ? 0 : 1);
