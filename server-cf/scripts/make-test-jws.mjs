// Makes src/__tests__/fixtures/apple-jws.json: StoreKit 2-style signed transactions on a throwaway
// certificate chain shaped like Apple's (P-384 root and intermediate, P-256 leaf, Apple's markers),
// for tests only. The server trusts that test root only when TEST_APPLE_ROOTS says so (local dev).
//   node scripts/make-test-jws.mjs        (needs openssl; keys stay in a temp folder)
import { execFileSync } from 'node:child_process';
import { mkdtempSync, readFileSync, writeFileSync, mkdirSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { createPrivateKey, sign } from 'node:crypto';

const dir = mkdtempSync(join(tmpdir(), 'testjws-'));
const ssl = (...args) => execFileSync('openssl', args, { cwd: dir, stdio: ['ignore', 'pipe', 'pipe'] });
const write = (name, text) => writeFileSync(join(dir, name), text);

function chain(prefix) {
  ssl('ecparam', '-name', 'secp384r1', '-genkey', '-noout', '-out', `${prefix}root.key`);
  ssl('req', '-x509', '-new', '-key', `${prefix}root.key`, '-sha384', '-days', '3650', '-subj', `/CN=${prefix}Test Root`, '-out', `${prefix}root.pem`);
  ssl('ecparam', '-name', 'secp384r1', '-genkey', '-noout', '-out', `${prefix}int.key`);
  ssl('req', '-new', '-key', `${prefix}int.key`, '-subj', `/CN=${prefix}Test Intermediate`, '-out', `${prefix}int.csr`);
  write(`${prefix}int.ext`, 'basicConstraints=critical,CA:true\n1.2.840.113635.100.6.2.1=ASN1:NULL\n');
  ssl('x509', '-req', '-in', `${prefix}int.csr`, '-CA', `${prefix}root.pem`, '-CAkey', `${prefix}root.key`, '-CAcreateserial', '-sha384', '-days', '3000', '-extfile', `${prefix}int.ext`, '-out', `${prefix}int.pem`);
  ssl('ecparam', '-name', 'prime256v1', '-genkey', '-noout', '-out', `${prefix}leaf.key`);
  ssl('req', '-new', '-key', `${prefix}leaf.key`, '-subj', `/CN=${prefix}Test Leaf`, '-out', `${prefix}leaf.csr`);
  write(`${prefix}leaf.ext`, '1.2.840.113635.100.6.11.1=ASN1:NULL\n');
  ssl('x509', '-req', '-in', `${prefix}leaf.csr`, '-CA', `${prefix}int.pem`, '-CAkey', `${prefix}int.key`, '-CAcreateserial', '-sha384', '-days', '2000', '-extfile', `${prefix}leaf.ext`, '-out', `${prefix}leaf.pem`);
  const b64 = name => ssl('x509', '-in', name, '-outform', 'DER').toString('base64');
  return { x5c: [b64(`${prefix}leaf.pem`), b64(`${prefix}int.pem`), b64(`${prefix}root.pem`)], key: createPrivateKey(readFileSync(join(dir, `${prefix}leaf.key`))) };
}

const url = buf => Buffer.from(buf).toString('base64url');
function jws(c, payload) {
  const head = url(JSON.stringify({ alg: 'ES256', x5c: c.x5c }));
  const body = url(JSON.stringify(payload));
  const sig = sign('sha256', Buffer.from(`${head}.${body}`), { key: c.key, dsaEncoding: 'ieee-p1363' });
  return `${head}.${body}.${url(sig)}`;
}

const good = chain('');
const other = chain('other-');
const now = Date.now();
const tx = (id, productId, extra = {}) => ({
  transactionId: String(id), originalTransactionId: String(id), bundleId: 'BomSapo.Chess3D', productId,
  type: 'Non-Consumable', environment: 'Sandbox', purchaseDate: now, signedDate: now, ...extra,
});

const valid = jws(good, tx(2000001, 'BomSapo.Chess3D.set.abyssglow'));
const parts = valid.split('.');
const tampered = `${parts[0]}.${url(JSON.stringify(tx(2000001, 'BomSapo.Chess3D.set.roboarena')))}.${parts[2]}`;

const fixtures = {
  testRoot: good.x5c[2],
  valid,
  backdrop: jws(good, tx(2000002, 'BomSapo.Chess3D.backdrop.graveyard')),
  promotion: jws(good, tx(2000003, 'BomSapo.Chess3D.promotion.splat')),
  everything: jws(good, tx(2000008, 'BomSapo.Chess3D.bundle.everything')),
  e2e: Array.from({ length: 6 }, (_, i) => jws(good, tx(3000001 + i, 'BomSapo.Chess3D.set.mushroomgrove'))),
  e2eRefund: jws(good, tx(3000001, 'BomSapo.Chess3D.set.mushroomgrove', { revocationDate: now })),
  wrongBundle: jws(good, tx(2000004, 'BomSapo.Chess3D.set.abyssglow', { bundleId: 'com.someone.else' })),
  notOurs: jws(good, tx(2000005, 'com.other.coins')),
  otherRoot: jws(other, tx(2000006, 'BomSapo.Chess3D.set.abyssglow')),
  tampered,
  revoked: jws(good, tx(2000007, 'BomSapo.Chess3D.set.wildkingdom', { revocationDate: now })),
};
mkdirSync(new URL('../src/__tests__/fixtures/', import.meta.url), { recursive: true });
writeFileSync(new URL('../src/__tests__/fixtures/apple-jws.json', import.meta.url), JSON.stringify(fixtures, null, 2) + '\n');
console.log('wrote src/__tests__/fixtures/apple-jws.json');
