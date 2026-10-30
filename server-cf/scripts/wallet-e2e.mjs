// End-to-end check of pawns on the server: earning, the Zombies! cap, unlocking with pawns,
// moving a phone's pawns over, App Store purchases (moving between accounts, refunds) and
// account deletion. Needs the local server started with the test certificate root trusted:
//   npx wrangler dev --var TEST_APPLE_ROOTS:$(node -p "require('./src/__tests__/fixtures/apple-jws.json').testRoot")
//   node scripts/wallet-e2e.mjs [baseUrl]
import { readFileSync } from 'node:fs';

const BASE = process.argv[2] ?? 'http://localhost:8787';
const F = JSON.parse(readFileSync(new URL('../src/__tests__/fixtures/apple-jws.json', import.meta.url), 'utf8'));
let failures = 0;
let requests = 0;
const check = (cond, label) => { console.log(`${cond ? 'PASS' : 'FAIL'}  ${label}`); if (!cond) failures++; };
const rid = () => crypto.randomUUID();

async function call(method, path, body, token) {
  requests++;
  const res = await fetch(BASE + path, {
    method,
    headers: { 'Content-Type': 'application/json', ...(token ? { Authorization: `Bearer ${token}` } : {}) },
    body: method === 'POST' ? JSON.stringify(body ?? {}) : undefined,
  });
  return { status: res.status, body: await res.json() };
}
const post = (p, b, t) => call('POST', p, b, t);
const get = (p, t) => call('GET', p, undefined, t);

check((await get('/wallet')).status === 401, 'the wallet needs a signed-in player');

const a = (await post('/auth/guest')).body;
const b = (await post('/auth/guest')).body;
let w = (await get('/wallet', a.token)).body;
check(w.balance === 0 && w.items.length === 0 && w.zombieLeftToday === 100 && w.migrated === false, 'a new player has an empty wallet');

// Moving a phone's pawns over: once, capped
let s = (await post('/wallet/sync', { migrate: { balance: 4000, lifetime: 9000, items: ['set.AbyssGlow'], cleared: ['starter.0'] }, events: [] }, a.token)).body;
check(s.wallet.balance === 4000 && s.wallet.items.includes('set.abyssglow') && s.wallet.migrated, 'the phone\'s pawns and items move over');
s = (await post('/wallet/sync', { migrate: { balance: 4000 }, events: [] }, a.token)).body;
check(s.wallet.balance === 4000, '...only once');

// Earning
const clear = { id: rid(), kind: 'blitz', puzzle: 'starter.4', pawns: 6 };
s = (await post('/wallet/sync', { events: [clear, { id: rid(), kind: 'blitz', puzzle: 'starter.0', pawns: 6 }] }, a.token)).body;
check(s.results[0].paid === 6 && s.results[1].paid === 0, 'a new puzzle pays; one cleared on the phone before doesn\'t');
s = (await post('/wallet/sync', { events: [clear] }, a.token)).body;
check(s.results[0].paid === 0 && s.wallet.balance === 4006, 'a sync sent twice pays once');
const run = rid();
s = (await post('/wallet/sync', { events: [{ id: rid(), kind: 'zombie', run, brain: 'hard', kills: 30 }, { id: rid(), kind: 'zombie', run, brain: 'hard', kills: 70 }] }, a.token)).body;
check(s.results[0].paid === 60 && s.results[1].paid === 40 && s.wallet.zombieLeftToday === 0, 'Zombies! pays up to the daily cap of 100');
s = (await post('/wallet/sync', { events: [{ id: rid(), kind: 'zombie', run: rid(), brain: 'easy', kills: 5 }] }, a.token)).body;
check(s.results[0].paid === 0, '...and nothing more today');
check((await post('/wallet/sync', { events: Array.from({ length: 101 }, () => ({ id: rid(), kind: 'autoplace' })) }, a.token)).status === 400, 'too many earnings in one sync is refused');

// Unlocking with pawns
let sp = await post('/wallet/spend', { requestId: rid(), item: 'backdrop.graveyard' }, a.token);
check(sp.status === 200 && sp.body.wallet.balance === 4106 - 3000 && sp.body.wallet.items.includes('backdrop.graveyard'), 'a backdrop unlocks for 3,000 pawns');
sp = await post('/wallet/spend', { requestId: rid(), item: 'backdrop.graveyard' }, a.token);
check(sp.status === 409, 'buying it again is refused');
sp = await post('/wallet/spend', { requestId: rid(), item: 'set.roboarena' }, a.token);
check(sp.status === 402 && (await get('/wallet', a.token)).body.balance === 1106, 'not enough pawns: refused, nothing charged');
check((await post('/wallet/spend', { requestId: rid(), item: 'gold.bars' }, a.token)).status === 400, 'only real items can be bought');

// App Store purchases
let pu = (await post('/wallet/purchase', { transactions: [F.e2e[0], F.wrongBundle, F.otherRoot, F.tampered, 'junk'] }, a.token)).body;
check(pu.granted?.includes('set.mushroomgrove') && pu.rejected === 4 && pu.wallet.items.includes('set.mushroomgrove'), 'a genuine App Store purchase unlocks its item; fakes are refused');
pu = (await post('/wallet/purchase', { transactions: [F.e2e[0]] }, b.token)).body;
const wa = (await get('/wallet', a.token)).body;
check(pu.wallet.items.includes('set.mushroomgrove') && !wa.items.includes('set.mushroomgrove'), 'restored on another account, the purchase moves there');
pu = (await post('/wallet/purchase', { transactions: [F.everything] }, b.token)).body;
check(pu.granted?.includes('bundle.everything') && pu.wallet.items.includes('bundle.everything'), 'Unlock Everything is recorded with the account');
check((await post('/wallet/spend', { requestId: rid(), item: 'bundle.everything' }, b.token)).status === 400, "Unlock Everything can't be bought with pawns");
pu = (await post('/wallet/purchase', { transactions: [F.e2eRefund] }, b.token)).body;
check(!pu.wallet.items.includes('set.mushroomgrove'), 'a refund takes it away');

// Pawn top-ups from offer codes
const pawnsBefore = (await get('/wallet', b.token)).body.balance;
pu = (await post('/wallet/purchase', { transactions: [F.pawns50, F.pawnsOtherRoot, F.pawnsUnknown] }, b.token)).body;
check(pu.credited === 50 && pu.rejected === 2 && pu.wallet.balance === pawnsBefore + 50 && !pu.wallet.items.some(i => i.startsWith('pawns')), 'a 50-pawn code adds 50 pawns; fakes and unknown top-ups are refused');
pu = (await post('/wallet/purchase', { transactions: [F.pawns50] }, b.token)).body;
check(pu.credited === 0 && pu.wallet.balance === pawnsBefore + 50, 'the same code sent again adds nothing');
pu = (await post('/wallet/purchase', { transactions: [F.pawns50] }, a.token)).body;
check(pu.credited === 0, '...nor from another account');
pu = (await post('/wallet/purchase', { transactions: [F.pawns100, F.pawns100b] }, b.token)).body;
check(pu.credited === 200 && pu.wallet.balance === pawnsBefore + 250, 'two 100-pawn codes add 200');

// The daily bonus
check((await post('/wallet/daily', {})).status === 401, 'the daily bonus needs a signed-in player');
w = (await get('/wallet', b.token)).body;
check(w.dailyAvailable === true && w.nextDailyAt > Date.now(), 'the wallet says the daily bonus is waiting, and when the next one comes');
let dc = (await post('/wallet/daily', {}, b.token)).body;
check(dc.claimed === true && dc.pawns === 50 && dc.wallet.balance === w.balance + 50 && dc.wallet.dailyAvailable === false, 'claiming it adds 50 pawns');
dc = (await post('/wallet/daily', {}, b.token)).body;
check(dc.claimed === false && dc.wallet.balance === w.balance + 50 && dc.wallet.nextDailyAt === w.nextDailyAt, 'a second claim the same day adds nothing and says when the next comes');

// Deleting the account deletes the wallet
check((await post('/account/delete', {}, a.token)).status === 200, 'account deleted');
check((await get('/wallet', a.token)).status === 401, 'its wallet is gone with it');

console.log(`\n${requests} requests to the Worker`);
console.log(failures ? `${failures} FAILED` : 'All passed');
process.exit(failures ? 1 : 0);
