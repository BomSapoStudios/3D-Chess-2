// End-to-end check of Queen Blitz solve times: they ride along on /wallet/sync and only come out as
// totals per puzzle through GET /blitz/stats with the STATS_KEY. Needs the local server started with a key:
//   npx wrangler dev --var STATS_KEY:local-test-stats-key-123
//   node scripts/blitzstats-e2e.mjs [baseUrl] [statsKey]
const BASE = process.argv[2] ?? 'http://localhost:8787';
const KEY = process.argv[3] ?? 'local-test-stats-key-123';
let failures = 0;
const check = (cond, label) => { console.log(`${cond ? 'PASS' : 'FAIL'}  ${label}`); if (!cond) failures++; };

async function call(method, path, body, headers = {}) {
  const res = await fetch(BASE + path, {
    method,
    headers: { 'Content-Type': 'application/json', ...headers },
    body: method === 'POST' ? JSON.stringify(body ?? {}) : undefined,
  });
  return { status: res.status, body: await res.json() };
}
const auth = t => ({ Authorization: `Bearer ${t}` });
const report = (pack, key = KEY) => call('GET', `/blitz/stats?pack=${pack}`, undefined, { 'X-Stats-Key': key });

const pack = 'e2e' + Math.floor(Math.random() * 1e6);
const rec = (o) => ({ puzzle: `${pack}.0`, sig: '0badc0de', seconds: 40, attempts: 1, cleared: false, moves: 0, auto: false, par: 3, ...o });
const a = (await call('POST', '/auth/guest')).body;
const b = (await call('POST', '/auth/guest')).body;

let s = await call('POST', '/wallet/sync', { events: [], stats: [rec({ seconds: 40 })] }, auth(a.token));
check(s.status === 200 && s.body.statsTaken === 1 && s.body.wallet, 'solve times ride along on a wallet sync');
await call('POST', '/wallet/sync', { events: [{ id: crypto.randomUUID(), kind: 'blitz', puzzle: `${pack}.0`, pawns: 5 }], stats: [rec({ seconds: 130, attempts: 3, cleared: true, moves: 3 })] }, auth(a.token));
await call('POST', '/wallet/sync', { events: [], stats: [rec({ seconds: 300, attempts: 2 })] }, auth(b.token));
s = await call('POST', '/wallet/sync', { events: [] }, auth(b.token));
check(s.status === 200 && s.body.statsTaken === 0, 'a sync without solve times is unchanged');

check((await call('POST', '/wallet/sync', { stats: [rec()] })).status === 401, 'sending needs a signed-in player');
check((await report(pack, '')).status === 404, 'totals need the stats key');
check((await report(pack, 'wrong-key-wrong-key')).status === 404, '...the right one');
const r = await report(pack);
const p = r.body.puzzles?.[0];
check(r.status === 200 && r.body.puzzles.length === 1, 'totals come back per puzzle');
check(p && p.players === 2 && p.cleared === 1 && p.solveSeconds.median === 130 && p.attempts.median === 3 && p.stuck.players === 1 && p.stuck.medianSeconds === 300,
  'they count the clear, its total time and tries, and the player still stuck');
check(!JSON.stringify(r.body).includes(a.userId) && !JSON.stringify(r.body).includes(b.userId), 'no player ids in the totals');

await call('POST', '/account/delete', {}, auth(b.token));
const after = (await report(pack)).body.puzzles[0];
check(after.players === 1 && after.stuck.players === 0, 'deleting an account removes its solve times');

console.log(failures ? `\n${failures} FAILED` : '\nAll passed');
process.exit(failures ? 1 : 0);
