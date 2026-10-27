// ─────────────────────────────────────────────────────────────────────────────
// Verifies a Game Center identity. On the phone, GKLocalPlayer's
// fetchItemsForIdentityVerificationSignature gives a URL to Apple's public key
// certificate, a signature, a salt and a timestamp. Apple signed
//   teamPlayerID + bundleID + timestamp (8 bytes, big-endian) + salt
// with that certificate's key, so checking the signature proves the player id is
// real and was issued for our app.
// ─────────────────────────────────────────────────────────────────────────────

export interface GameCenterProof {
  teamPlayerId: string;
  publicKeyUrl: string;
  signature: string; // base64
  salt: string;      // base64
  timestamp: number; // milliseconds since 1970, from Apple
}

/** How old a proof may be. The app asks for a fresh one each time it signs in. */
const MAX_AGE_MS = 15 * 60_000;

const keyCache = new Map<string, { key: CryptoKey; fetchedAt: number }>();

function b64Decode(text: string): Uint8Array {
  return Uint8Array.from(atob(text), c => c.charCodeAt(0));
}

/** Read one DER element at `pos`: its tag, where its contents start and end */
function derElement(der: Uint8Array, pos: number): { tag: number; start: number; end: number } {
  const tag = der[pos];
  let len = der[pos + 1];
  let start = pos + 2;
  if (len & 0x80) {
    const bytes = len & 0x7f;
    if (bytes === 0 || bytes > 4) throw new Error('bad certificate');
    len = 0;
    for (let i = 0; i < bytes; i++) len = len * 256 + der[start + i];
    start += bytes;
  }
  if (start + len > der.length) throw new Error('bad certificate');
  return { tag, start, end: start + len };
}

/** The SubjectPublicKeyInfo inside an X.509 certificate, ready for importKey('spki') */
export function spkiFromCertificate(der: Uint8Array): Uint8Array {
  const cert = derElement(der, 0);
  const tbs = derElement(der, cert.start);
  let pos = tbs.start;
  let field = derElement(der, pos);
  if (field.tag === 0xa0) { pos = field.end; field = derElement(der, pos); } // optional version
  // serialNumber, signature algorithm, issuer, validity, subject, then subjectPublicKeyInfo
  for (let i = 0; i < 5; i++) { pos = field.end; field = derElement(der, pos); }
  if (field.tag !== 0x30) throw new Error('bad certificate');
  return der.slice(pos, field.end);
}

/** Only Apple's Game Center certificate host (https://static.gc.apple.com/public-key/gc-prod-N.cer) */
export function trustedKeyUrl(raw: string): boolean {
  try {
    const url = new URL(raw);
    return url.protocol === 'https:' && url.hostname === 'static.gc.apple.com' && url.pathname.startsWith('/public-key/')
      && url.pathname.endsWith('.cer') && !url.search && !url.username && !url.port;
  } catch {
    return false;
  }
}

async function keyFor(url: string): Promise<CryptoKey> {
  const cached = keyCache.get(url);
  if (cached && Date.now() - cached.fetchedAt < 24 * 3600_000) return cached.key;
  const res = await fetch(url);
  if (!res.ok) throw new Error('could not load the Game Center key');
  const der = new Uint8Array(await res.arrayBuffer());
  const key = await crypto.subtle.importKey('spki', spkiFromCertificate(der), { name: 'RSASSA-PKCS1-v1_5', hash: 'SHA-256' }, false, ['verify']);
  keyCache.set(url, { key, fetchedAt: Date.now() });
  return key;
}

/** The bytes Apple signed */
export function signedPayload(teamPlayerId: string, bundleId: string, timestamp: number, salt: Uint8Array): Uint8Array {
  const enc = new TextEncoder();
  const id = enc.encode(teamPlayerId);
  const bundle = enc.encode(bundleId);
  const out = new Uint8Array(id.length + bundle.length + 8 + salt.length);
  out.set(id, 0);
  out.set(bundle, id.length);
  new DataView(out.buffer).setBigUint64(id.length + bundle.length, BigInt(timestamp), false);
  out.set(salt, id.length + bundle.length + 8);
  return out;
}

/** Returns the team player id if the proof is genuine, fresh and for one of `bundleIds`; otherwise null */
export async function verifyGameCenter(proof: Partial<GameCenterProof>, bundleIds: string[]): Promise<string | null> {
  const { teamPlayerId, publicKeyUrl, signature, salt, timestamp } = proof;
  if (typeof teamPlayerId !== 'string' || !teamPlayerId || teamPlayerId.length > 100) return null;
  if (typeof publicKeyUrl !== 'string' || !trustedKeyUrl(publicKeyUrl)) return null;
  if (typeof signature !== 'string' || typeof salt !== 'string' || typeof timestamp !== 'number' || !Number.isSafeInteger(timestamp)) return null;
  if (Math.abs(Date.now() - timestamp) > MAX_AGE_MS) return null;
  try {
    const key = await keyFor(publicKeyUrl);
    const sig = b64Decode(signature);
    const saltBytes = b64Decode(salt);
    for (const bundleId of bundleIds) {
      if (await crypto.subtle.verify('RSASSA-PKCS1-v1_5', key, sig, signedPayload(teamPlayerId, bundleId, timestamp, saltBytes))) return teamPlayerId;
    }
  } catch {
    return null;
  }
  return null;
}
