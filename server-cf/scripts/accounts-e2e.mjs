// End-to-end check of accounts, unique names, device linking and friends.
//   node scripts/accounts-e2e.mjs [baseUrl]
const BASE = process.argv[2] ?? 'http://localhost:8787';
let failures = 0;
const check = (cond, label) => { console.log(`${cond ? 'PASS' : 'FAIL'}  ${label}`); if (!cond) failures++; };
const NAME = /^[A-Z][a-z]+[A-Z][a-z]+[1-9][0-9]{0,2}$/;

async function call(method, path, body, token) {
  const res = await fetch(BASE + path, {
    method,
    headers: { 'Content-Type': 'application/json', ...(token ? { Authorization: `Bearer ${token}` } : {}) },
    body: method === 'POST' ? JSON.stringify(body ?? {}) : undefined,
  });
  return { status: res.status, body: await res.json() };
}
const post = (p, b, t) => call('POST', p, b, t);
const get = (p, t) => call('GET', p, undefined, t);

// Accounts and names
const a = (await post('/auth/guest', { name: 'SomeRudeName' })).body;
const b = (await post('/auth/guest')).body;
const c = (await post('/auth/guest')).body;
check(NAME.test(a.name) && NAME.test(b.name) && a.name !== b.name, `guests get distinct generated names (${a.name}, ${b.name})`);
const ar = (await post('/auth/refresh', {}, a.token)).body;
check(ar.userId === a.userId && ar.name === a.name && ar.token, 'refresh keeps the account and name');
check((await post('/auth/refresh', {}, 'garbage')).status === 401, 'refresh needs a valid token');

const re = (await post('/names/reroll', {}, c.token)).body;
check(NAME.test(re.name) && re.name !== c.name && re.userId === c.userId && re.token, `reroll gives a new name (${c.name} → ${re.name})`);
c.token = re.token; c.name = re.name;
const me = (await get('/me', c.token)).body;
check(me.name === c.name && Array.isArray(me.providers), '/me shows the current name');

// Device link codes
const code = (await post('/link/code', {}, a.token)).body.code;
check(/^[A-Z2-9]{6}$/.test(code), `link code issued (${code})`);
const linked = (await post('/link/redeem', { code })).body;
check(linked.userId === a.userId && linked.name === a.name && linked.token, 'redeeming the code signs in as the same player');
check((await post('/link/redeem', { code })).status === 404, 'a link code works only once');

// Sign in with Apple rejects fake tokens
check((await post('/auth/apple', { idToken: 'x.y.z' })).status === 401, 'fake Apple token is refused');

// Friends
check((await post('/friends/request', { name: 'NoSuchName1' }, b.token)).status === 404, 'unknown name not found');
check((await post('/friends/request', { name: b.name }, b.token)).status === 400, "can't friend yourself");
const req = (await post('/friends/request', { name: a.name }, b.token)).body;
check(req.status === 'outgoing', 'friend request sent by name');
let fa = (await get('/friends', a.token)).body;
const fromB = fa.friends.find(f => f.userId === b.userId);
check(fromB?.status === 'incoming' && fromB.name === b.name, 'recipient sees the incoming request');
check((await post('/friends/respond', { friendId: b.userId, accept: true }, a.token)).body.ok, 'request accepted');
const fb = (await get('/friends', b.token)).body;
const aForB = fb.friends.find(f => f.userId === a.userId);
check(aForB?.status === 'friends' && aForB.online === true, 'both are friends and A shows online');

const ch = await post('/friends/challenge', { friendId: b.userId, config: { boardSize: '4x4x4', playerCount: 2 } }, a.token);
check(ch.status === 200 && /^[A-Z2-9]{6}$/.test(ch.body.code), `challenge creates a private room (${ch.body.code})`);
const fb2 = (await get('/friends', b.token)).body;
check(fb2.challenges.some(x => x.roomCode === ch.body.code && x.fromName === a.name), 'friend sees the challenge');
await post('/friends/dismiss', { roomCode: ch.body.code }, b.token);
check(!(await get('/friends', b.token)).body.challenges.some(x => x.roomCode === ch.body.code), 'challenge can be dismissed');
check((await post('/friends/challenge', { friendId: c.userId }, a.token)).status === 403, "can't challenge a non-friend");

// Blocking
await post('/friends/request', { name: a.name }, c.token);
await post('/friends/block', { friendId: c.userId }, a.token);
check((await post('/friends/request', { name: a.name }, c.token)).status === 404, 'blocked player can no longer find you');
check(!(await get('/friends', a.token)).body.friends.some(f => f.userId === c.userId), 'blocked player is gone from your list');

// Search by name (part of a name works; blocked players are hidden)
const part = b.name.slice(0, 6);
const found = (await get(`/friends/search?q=${part}`, a.token)).body.results;
check(found.some(r => r.userId === b.userId), `search "${part}" finds ${b.name}`);
check((await get('/friends/search?q=ab', a.token)).body.results.length === 0, 'very short searches return nothing');
const hidden = (await get(`/friends/search?q=${c.name.slice(0, 8)}`, a.token)).body.results;
check(!hidden.some(r => r.userId === c.userId), 'blocked players are hidden from search');

// Friends survive a name change
await post('/friends/request', { name: a.name }, b.token);
await post('/friends/respond', { friendId: b.userId, accept: true }, a.token);
const renamed = (await post('/names/reroll', {}, a.token)).body;
a.token = renamed.token;
const afterRename = (await get('/friends', b.token)).body.friends.find(f => f.userId === a.userId);
check(afterRename?.status === 'friends' && afterRename.name === renamed.name, `friend kept after renaming, shown under the new name (${renamed.name})`);

// Remove
await post('/friends/remove', { friendId: b.userId }, a.token);
check(!(await get('/friends', b.token)).body.friends.some(f => f.userId === a.userId), 'removing a friend removes it for both');

// Reroll limit
let limited = false;
for (let i = 0; i < 12 && !limited; i++) {
  const r = await post('/names/reroll', {}, b.token);
  if (r.status === 429) limited = true; else b.token = r.body.token;
}
check(limited, 'name changes are limited per day');

console.log(failures === 0 ? '\nAll checks passed' : `\n${failures} check(s) failed`);
process.exit(failures === 0 ? 0 : 1);
