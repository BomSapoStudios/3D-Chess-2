import { describe, test, expect, beforeEach } from 'vitest';

// Node modules are loaded by name so the Worker typecheck (no Node types) stays clean
const { DatabaseSync } = await import('node:sqlite' as string);
const fixtures = (await import('./fixtures/apple-jws.json' as string)).default;
import { purgeWallet, recordPurchases, setupWalletTables, spendPawns, syncWallet, walletOf, WalletError } from '../wallet';
import { ECONOMY, EVERYTHING, itemOfProduct, priceOf } from '../economy';
import { verifyAppleTransaction } from '../appleJws';

/** The Durable Object's SqlStorage, on Node's built-in SQLite */
function sqlStorage(): SqlStorage {
  const db = new DatabaseSync(':memory:');
  return {
    exec(query: string, ...params: unknown[]) {
      if (params.length === 0 && query.trim().split(';').filter(s => s.trim()).length > 1) { db.exec(query); return { toArray: () => [] }; }
      const stmt = db.prepare(query);
      const rows = /^\s*SELECT/i.test(query) ? stmt.all(...(params as never[])) : (stmt.run(...(params as never[])), []);
      return { toArray: () => rows };
    },
  } as unknown as SqlStorage;
}

const DAY = 24 * 3600_000;
let sql: SqlStorage;
let n = 0;
const rid = () => `req-${String(++n).padStart(6, '0')}`;
beforeEach(() => { sql = sqlStorage(); setupWalletTables(sql); });

describe('wallet', () => {
  test('starts empty with the full Zombies! allowance', () => {
    expect(walletOf(sql, 'a')).toEqual({ balance: 0, lifetime: 0, items: [], zombieLeftToday: ECONOMY.zombieDailyCap, migrated: false });
  });

  test('a Queen Blitz puzzle pays on its first clear only', () => {
    const r1 = syncWallet(sql, 'a', undefined, [{ id: rid(), kind: 'blitz', puzzle: 'starter.3', pawns: 7 }]);
    expect(r1.results[0].paid).toBe(7);
    const r2 = syncWallet(sql, 'a', undefined, [{ id: rid(), kind: 'blitz', puzzle: 'starter.3', pawns: 7 }]);
    expect(r2.results[0].paid).toBe(0);
    expect(r2.wallet.balance).toBe(7);
    expect(syncWallet(sql, 'b', undefined, [{ id: rid(), kind: 'blitz', puzzle: 'starter.3', pawns: 7 }]).results[0].paid).toBe(7); // per account
  });

  test('impossible Queen Blitz amounts are cut to the most a puzzle can pay; bad ids are refused', () => {
    expect(syncWallet(sql, 'a', undefined, [{ id: rid(), kind: 'blitz', puzzle: 'feast.1', pawns: 9999 }]).results[0].paid).toBe(ECONOMY.blitzMaxPawns);
    const bad = syncWallet(sql, 'a', undefined, [{ id: rid(), kind: 'blitz', puzzle: "x'; DROP TABLE wallets;--", pawns: 5 }]);
    expect(bad.results[0].error).toBeTruthy();
    expect(syncWallet(sql, 'a', undefined, [{ id: rid(), kind: 'blitz', puzzle: 'feast.2', pawns: -50 }]).results[0].paid).toBe(0);
  });

  test('the same earning sent twice pays once', () => {
    const id = rid();
    syncWallet(sql, 'a', undefined, [{ id, kind: 'blitz', puzzle: 'starter.1', pawns: 5 }]);
    const again = syncWallet(sql, 'a', undefined, [{ id, kind: 'blitz', puzzle: 'starter.2', pawns: 5 }]);
    expect(again.results[0].paid).toBe(0);
    expect(again.wallet.balance).toBe(5);
  });

  test('Zombies! pays a run by its kills, rounded down, as the run goes on', () => {
    const run = 'run-0000001';
    // Walnut brain (1.5 each): 3 kills = 4 pawns, then 5 kills = 7 (3 more)
    expect(syncWallet(sql, 'a', undefined, [{ id: rid(), kind: 'zombie', run, brain: 'medium', kills: 3 }]).results[0].paid).toBe(4);
    expect(syncWallet(sql, 'a', undefined, [{ id: rid(), kind: 'zombie', run, brain: 'medium', kills: 5 }]).results[0].paid).toBe(3);
    expect(syncWallet(sql, 'a', undefined, [{ id: rid(), kind: 'zombie', run, brain: 'medium', kills: 5 }]).results[0].paid).toBe(0);
    expect(walletOf(sql, 'a').zombieLeftToday).toBe(ECONOMY.zombieDailyCap - 7);
  });

  test('Zombies! stops at the daily cap and starts again the next day', () => {
    const t = Date.UTC(2026, 9, 28, 12);
    const first = syncWallet(sql, 'a', undefined, [{ id: rid(), kind: 'zombie', run: 'run-aaaaaaaa', brain: 'hard', kills: 80 }], t);
    expect(first.results[0].paid).toBe(100);
    expect(first.wallet.zombieLeftToday).toBe(0);
    expect(syncWallet(sql, 'a', undefined, [{ id: rid(), kind: 'zombie', run: 'run-bbbbbbbb', brain: 'easy', kills: 10 }], t + 1000).results[0].paid).toBe(0);
    const tomorrow = syncWallet(sql, 'a', undefined, [{ id: rid(), kind: 'zombie', run: 'run-cccccccc', brain: 'easy', kills: 10 }], t + DAY);
    expect(tomorrow.results[0].paid).toBe(10);
    expect(tomorrow.wallet.balance).toBe(110);
  });

  test('Zombies! refuses unbelievable runs and unknown brains', () => {
    expect(syncWallet(sql, 'a', undefined, [{ id: rid(), kind: 'zombie', run: 'run-dddddddd', brain: 'hard', kills: ECONOMY.zombieMaxKillsPerRun + 1 }]).results[0].error).toBeTruthy();
    expect(syncWallet(sql, 'a', undefined, [{ id: rid(), kind: 'zombie', run: 'run-dddddddd', brain: 'genius', kills: 3 }]).results[0].error).toBeTruthy();
    expect(walletOf(sql, 'a').balance).toBe(0);
  });

  test('too many earnings in one sync is refused', () => {
    const events = Array.from({ length: ECONOMY.maxEventsPerSync + 1 }, (_, i) => ({ id: rid(), kind: 'blitz', puzzle: `starter.${i}`, pawns: 1 }));
    expect(() => syncWallet(sql, 'a', undefined, events)).toThrow(WalletError);
  });

  test('auto-place spends a pawn if there is one', () => {
    syncWallet(sql, 'a', undefined, [{ id: rid(), kind: 'blitz', puzzle: 'starter.1', pawns: 2 }]);
    const r = syncWallet(sql, 'a', undefined, [{ id: rid(), kind: 'autoplace' }, { id: rid(), kind: 'autoplace' }, { id: rid(), kind: 'autoplace' }]);
    expect(r.wallet.balance).toBe(0);
    expect(r.wallet.lifetime).toBe(2);
  });
});

describe('spending pawns', () => {
  const fund = (pawns: number) => syncWallet(sql, 'a', { balance: pawns }, []);

  test('unlocks the item and charges its price', () => {
    fund(5000);
    const r = spendPawns(sql, 'a', rid(), 'backdrop.Graveyard');
    expect(r.item).toBe('backdrop.graveyard');
    expect(r.wallet.balance).toBe(5000 - ECONOMY.prices.backdrop);
    expect(r.wallet.items).toEqual(['backdrop.graveyard']);
  });

  test('refuses when short, without charging', () => {
    fund(500);
    expect(() => spendPawns(sql, 'a', rid(), 'set.abyssglow')).toThrow(/Not enough pawns/);
    expect(walletOf(sql, 'a').balance).toBe(500);
  });

  test('refuses what is already owned, and things that are not for sale', () => {
    fund(9000);
    spendPawns(sql, 'a', rid(), 'promotion.splat');
    expect(() => spendPawns(sql, 'a', rid(), 'promotion.splat')).toThrow(/already own/);
    expect(() => spendPawns(sql, 'a', rid(), 'gold.bars')).toThrow(WalletError);
    expect(walletOf(sql, 'a').balance).toBe(9000 - ECONOMY.prices.promotion);
  });

  test('the same unlock sent twice charges once', () => {
    fund(9000);
    const id = rid();
    spendPawns(sql, 'a', id, 'blitz.feast');
    const again = spendPawns(sql, 'a', id, 'blitz.feast');
    expect(again.wallet.balance).toBe(9000 - ECONOMY.prices.blitz);
  });
});

describe('moving a phone\'s pawns to the server', () => {
  test('happens once, with the balance capped', () => {
    const m = { balance: 50_000, lifetime: 60_000, items: ['set.AbyssGlow', 'nonsense!', 'blitz.feast'], cleared: ['starter.0', 'starter.1'] };
    const r = syncWallet(sql, 'a', m, []);
    expect(r.wallet.balance).toBe(ECONOMY.migrateCap);
    expect(r.wallet.items).toEqual(['blitz.feast', 'set.abyssglow']);
    expect(r.wallet.migrated).toBe(true);
    // Cleared puzzles came along: they don't pay again
    expect(syncWallet(sql, 'a', undefined, [{ id: rid(), kind: 'blitz', puzzle: 'starter.1', pawns: 5 }]).results[0].paid).toBe(0);
    // A second phone (or a reinstall) can't add its pawns again
    expect(syncWallet(sql, 'a', { balance: 9000 }, []).wallet.balance).toBe(ECONOMY.migrateCap);
  });
});

describe('App Store purchases', () => {
  const p = (orig: string, item: string, revoked = false) => ({ originalTransactionId: orig, transactionId: orig, item, environment: 'Sandbox', revoked });

  test('unlock the item; the same transaction again changes nothing', () => {
    expect(recordPurchases(sql, 'a', [p('1001', 'set.abyssglow')]).wallet.items).toEqual(['set.abyssglow']);
    expect(recordPurchases(sql, 'a', [p('1001', 'set.abyssglow')]).wallet.items).toEqual(['set.abyssglow']);
  });

  test('a purchase restored on another account moves there', () => {
    recordPurchases(sql, 'a', [p('1002', 'backdrop.graveyard')]);
    recordPurchases(sql, 'b', [p('1002', 'backdrop.graveyard')]);
    expect(walletOf(sql, 'a').items).toEqual([]);
    expect(walletOf(sql, 'b').items).toEqual(['backdrop.graveyard']);
  });

  test('moving a purchase leaves an item the old account bought with pawns', () => {
    syncWallet(sql, 'a', { balance: 5000 }, []);
    spendPawns(sql, 'a', rid(), 'backdrop.graveyard');
    recordPurchases(sql, 'a', [p('1003', 'backdrop.graveyard')]);
    recordPurchases(sql, 'b', [p('1003', 'backdrop.graveyard')]);
    expect(walletOf(sql, 'a').items).toEqual(['backdrop.graveyard']);
  });

  test('Unlock Everything is kept like any purchase, and can\'t be had for pawns', () => {
    syncWallet(sql, 'a', { balance: 10_000, items: ['bundle.everything'] }, []);
    expect(walletOf(sql, 'a').items).toEqual([EVERYTHING]); // moved over from a phone that bought it before
    expect(() => spendPawns(sql, 'b', rid(), EVERYTHING)).toThrow(/can't be bought with pawns/);
    expect(recordPurchases(sql, 'b', [p('1010', EVERYTHING)]).wallet.items).toEqual([EVERYTHING]);
  });

  test('a refund takes the item away', () => {
    recordPurchases(sql, 'a', [p('1004', 'promotion.splat')]);
    recordPurchases(sql, 'a', [p('1004', 'promotion.splat', true)]);
    expect(walletOf(sql, 'a').items).toEqual([]);
  });

  test('deleting the account forgets the wallet, so the purchase can be restored elsewhere', () => {
    recordPurchases(sql, 'a', [p('1005', 'set.roboarena')]);
    syncWallet(sql, 'a', { balance: 300 }, []);
    purgeWallet(sql, 'a');
    expect(walletOf(sql, 'a')).toMatchObject({ balance: 0, items: [], migrated: false });
    expect(recordPurchases(sql, 'c', [p('1005', 'set.roboarena')]).wallet.items).toEqual(['set.roboarena']);
  });
});

describe('economy', () => {
  test('prices by kind, product ids map to items', () => {
    expect(priceOf('set.abyssglow')).toBe(4500);
    expect(priceOf('backdrop.deepsea')).toBe(3000);
    expect(priceOf('promotion.splat')).toBe(1000);
    expect(priceOf('SET.ABYSSGLOW')).toBeNull(); // ids are lowercase
    expect(itemOfProduct('BomSapo.Chess3D.set.abyssglow')).toBe('set.abyssglow');
    expect(itemOfProduct('BomSapo.Chess3D.backdrop.graveyard')).toBe('backdrop.graveyard');
    expect(itemOfProduct('com.other.coins')).toBeNull();
    expect(itemOfProduct('BomSapo.Chess3D.coins.1000')).toBeNull();
  });

  test('Queen Blitz packs: an App Store product, and pawns can buy them too', () => {
    expect(itemOfProduct('BomSapo.Chess3D.blitz.cuberun')).toBe('blitz.cuberun');
    expect(itemOfProduct('BomSapo.Chess3D.blitz.deepfreeze')).toBe('blitz.deepfreeze');
    expect(priceOf('blitz.feast')).toBe(ECONOMY.prices.blitz);
    expect(recordPurchases(sql, 'a', [{ originalTransactionId: '1020', transactionId: '1020', item: 'blitz.blockade', environment: 'Sandbox' }]).wallet.items).toEqual(['blitz.blockade']);
  });

  test('Unlock Everything is an App Store product pawns can\'t buy', () => {
    expect(itemOfProduct('BomSapo.Chess3D.bundle.everything')).toBe(EVERYTHING);
    expect(priceOf(EVERYTHING)).toBeNull();
  });
});

describe('StoreKit 2 transaction check', () => {
  const f = fixtures;
  const bundles = ['BomSapo.Chess3D'];
  const roots = [f.testRoot];

  test('accepts a transaction signed on the trusted chain', async () => {
    const t = await verifyAppleTransaction(f.valid, bundles, roots);
    expect(t?.productId).toBe('BomSapo.Chess3D.set.abyssglow');
    expect(t?.originalTransactionId).toBe('2000001');
  });

  test('refuses the test chain unless it is trusted (production trusts only Apple Root CA - G3)', async () => {
    expect(await verifyAppleTransaction(f.valid, bundles)).toBeNull();
  });

  test('refuses another chain, a changed payload, another app and junk', async () => {
    expect(await verifyAppleTransaction(f.otherRoot, bundles, roots)).toBeNull();
    expect(await verifyAppleTransaction(f.tampered, bundles, roots)).toBeNull();
    expect(await verifyAppleTransaction(f.wrongBundle, bundles, roots)).toBeNull();
    for (const junk of ['', 'a.b.c', 'x'.repeat(30_000), f.valid.split('.').slice(0, 2).join('.') + '.AAAA']) {
      expect(await verifyAppleTransaction(junk, bundles, roots)).toBeNull();
    }
  });

  test('accepts a purchase redeemed with an offer code (free, offerType 3)', async () => {
    const t = await verifyAppleTransaction(f.offerCode, bundles, roots);
    expect(t?.productId).toBe('BomSapo.Chess3D.backdrop.deepsea');
    const item = itemOfProduct(t!.productId)!;
    expect(recordPurchases(sql, 'a', [{ originalTransactionId: t!.originalTransactionId, transactionId: t!.transactionId, item, environment: t!.environment }]).wallet.items).toEqual(['backdrop.deepsea']);
  });

  test('reports refunds; ignores products that aren\'t ours', async () => {
    expect((await verifyAppleTransaction(f.revoked, bundles, roots))?.revocationDate).toBeGreaterThan(0);
    const other = await verifyAppleTransaction(f.notOurs, bundles, roots);
    expect(other && itemOfProduct(other.productId)).toBeNull();
  });
});
