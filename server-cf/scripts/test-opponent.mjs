// Plays a very simple opponent in a live room, for trying the app against: places a full set
// wherever the server accepts them, readies up, then on its turn tries king/rook steps until one is legal.
//   node scripts/test-opponent.mjs <ROOMCODE> <token> [server]
import WebSocket from 'ws';
const [code, token, base = 'https://3d-chess.bomsapostudios.workers.dev'] = process.argv.slice(2);
const ws = new WebSocket(`${base.replace(/^http/, 'ws')}/rooms/${code}/connect?token=${encodeURIComponent(token)}`);
let rematchSent = false;
let me = null, nextId = 1, busy = false, placed = false, mine = [];
const pending = new Map();
const request = msg => new Promise(res => { const id = nextId++; pending.set(id, res); ws.send(JSON.stringify({ ...msg, requestId: id })); });
const log = (...a) => console.log(new Date().toISOString().slice(11, 19), ...a);

async function place(state) {
  if (placed) return; placed = true;
  const n = Number(state.config.boardSize[0]);
  const cells = [];
  for (let x = 0; x < n; x++) for (let y = 0; y < n; y++) for (let z = 0; z < n; z++) cells.push({ x, y, z });
  for (const type of ['King', 'Queen', 'Rook', 'Rook', 'Bishop', 'Bishop', 'Knight', 'Knight']) {
    for (const pos of cells) {
      const r = await request({ type: 'game:placePiece', pieceType: type, position: pos });
      if (r.ok) { log('placed', type, JSON.stringify(pos)); mine.push(pos); break; }
    }
  }
  const r = await request({ type: 'game:ready' });
  log('ready', r.ok, r.error ?? '');
}

async function move(state) {
  if (busy) return; busy = true;
  const pieces = [];
  state.board.flat(2).forEach(p => { if (p && p.color === me) pieces.push(p); });
  const steps = [];
  for (let dx = -1; dx <= 1; dx++) for (let dy = -1; dy <= 1; dy++) for (let dz = -1; dz <= 1; dz++) if (dx || dy || dz) steps.push([dx, dy, dz]);
  // Knight jumps: two along one axis and one along another
  const jumps = [];
  for (const a of [0, 1, 2]) for (const b of [0, 1, 2]) if (a !== b) for (const sa of [-2, 2]) for (const sb of [-1, 1]) { const d = [0, 0, 0]; d[a] = sa; d[b] = sb; jumps.push(d); }
  for (const p of pieces.filter(p => p.type === 'Knight')) for (const [dx, dy, dz] of jumps) {
    const to = { x: p.position.x + dx, y: p.position.y + dy, z: p.position.z + dz };
    const r = await request({ type: 'game:move', from: p.position, to });
    if (r.ok) { log('moved', p.type, JSON.stringify(p.position), '→', JSON.stringify(to)); busy = false; return; }
  }
  for (const k of [1, 2, 3]) for (const p of pieces.sort(() => Math.random() - 0.5)) {
    for (const [dx, dy, dz] of steps.sort(() => Math.random() - 0.5)) {
      const to = { x: p.position.x + dx * k, y: p.position.y + dy * k, z: p.position.z + dz * k };
      const r = await request({ type: 'game:move', from: p.position, to });
      if (r.ok) { log('moved', p.type, JSON.stringify(p.position), '→', JSON.stringify(to)); busy = false; return; }
    }
  }
  log('no legal move found; resigning'); await request({ type: 'game:resign' }); busy = false;
}

ws.on('message', async data => {
  const msg = JSON.parse(data.toString());
  if (msg.type === 'ack') { pending.get(msg.requestId)?.(msg); pending.delete(msg.requestId); return; }
  if (msg.type === 'room:joined') { me = msg.yourColor; log('joined', code, 'as', me); }
  if (msg.type !== 'game:stateUpdate' && msg.type !== 'room:joined' && msg.type !== 'room:playerJoined') { log('←', msg.type, msg.color ?? '', msg.reason ?? ''); }
  const s = msg.state;
  if (!s) return;
  if (s.phase === 'placement') place(s);
  if (s.phase === 'playing' && s.currentTurn === me) setTimeout(() => move(s), 1500);
  if (s.phase === 'gameover') {
    log('game over', s.winner, s.endReason, JSON.stringify(s.rematch));
    if (!rematchSent) { rematchSent = true; setTimeout(async () => log('rematch yes', (await request({ type: 'room:rematch', accept: true })).ok), 2000); }
  }
  if (msg.type === 'room:rematch') log('rematch', JSON.stringify(msg.rematch));
  if (msg.type === 'game:drawOffered') log('draw accepted', (await request({ type: 'game:respondDraw', accept: true })).ok);
});
ws.on('close', (c, r) => { log('closed', c, r.toString()); process.exit(0); });
ws.on('error', e => log('error', e.message));
setTimeout(() => process.exit(0), 45 * 60 * 1000);
