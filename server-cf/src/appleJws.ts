// ─────────────────────────────────────────────────────────────────────────────
// Checks an App Store purchase (StoreKit 2). The app sends the transaction as Apple signed it: a JWS
// whose header carries a certificate chain (x5c: Apple's signing certificate, Apple's intermediate,
// Apple Root CA - G3). It's genuine if the chain ends in Apple's root, each certificate is signed by
// the next, the certificates carry Apple's markers for App Store receipts, and the JWS is signed by
// the first one. Sandbox (TestFlight, App Review) and production purchases both count.
// ─────────────────────────────────────────────────────────────────────────────

/** Apple Root CA - G3 (SHA-256 63343ABF B89A6A03 EBB57E9B 3F5FA7BE 7C4F5C75 6F3017B3 A8C488C3 653E9179) */
export const APPLE_ROOT_CA_G3 =
  'MIICQzCCAcmgAwIBAgIILcX8iNLFS5UwCgYIKoZIzj0EAwMwZzEbMBkGA1UEAwwSQXBwbGUgUm9vdCBDQSAtIEczMSYwJAYDVQQLDB1BcHBsZSBDZXJ0aWZpY2F0aW9uIEF1dGhvcml0eTETMBEGA1UECgwKQXBwbGUgSW5jLjELMAkGA1UEBhMCVVMwHhcNMTQwNDMwMTgxOTA2WhcNMzkwNDMwMTgxOTA2WjBnMRswGQYDVQQDDBJBcHBsZSBSb290IENBIC0gRzMxJjAkBgNVBAsMHUFwcGxlIENlcnRpZmljYXRpb24gQXV0aG9yaXR5MRMwEQYDVQQKDApBcHBsZSBJbmMuMQswCQYDVQQGEwJVUzB2MBAGByqGSM49AgEGBSuBBAAiA2IABJjpLz1AcqTtkyJygRMc3RCV8cWjTnHcFBbZDuWmBSp3ZHtfTjjTuxxEtX/1H7YyYl3J6YRbTzBPEVoA/VhYDKX1DyxNB0cTddqXl5dvMVztK517IDvYuVTZXpmkOlEKMaNCMEAwHQYDVR0OBBYEFLuw3qFYM4iapIqZ3r6966/ayySrMA8GA1UdEwEB/wQFMAMBAf8wDgYDVR0PAQH/BAQDAgEGMAoGCCqGSM49BAMDA2gAMGUCMQCD6cHEFl4aXTQY2e3v9GwOAEZLuN+yRhHFD/3meoyhpmvOwgPUnPWTxnS4at+qIxUCMG1mihDK1A3UT82NQz60imOlM27jbdoXt2QfyFMm+YhidDkLF1vLUagM6BgD56KyKA==';

export interface AppleTransaction {
  transactionId: string;
  originalTransactionId: string;
  productId: string;
  bundleId: string;
  environment: string;
  type?: string;
  signedDate?: number;
  revocationDate?: number;
}

// OIDs, DER-encoded (tag, length, value)
const OID = {
  ecPublicKey: [0x06, 0x07, 0x2a, 0x86, 0x48, 0xce, 0x3d, 0x02, 0x01],
  p256: [0x06, 0x08, 0x2a, 0x86, 0x48, 0xce, 0x3d, 0x03, 0x01, 0x07],
  p384: [0x06, 0x05, 0x2b, 0x81, 0x04, 0x00, 0x22],
  ecdsaSha256: [0x06, 0x08, 0x2a, 0x86, 0x48, 0xce, 0x3d, 0x04, 0x03, 0x02],
  ecdsaSha384: [0x06, 0x08, 0x2a, 0x86, 0x48, 0xce, 0x3d, 0x04, 0x03, 0x03],
  appleReceiptSigning: [0x06, 0x0a, 0x2a, 0x86, 0x48, 0x86, 0xf7, 0x63, 0x64, 0x06, 0x0b, 0x01], // 1.2.840.113635.100.6.11.1 (leaf)
  appleWwdrIntermediate: [0x06, 0x0a, 0x2a, 0x86, 0x48, 0x86, 0xf7, 0x63, 0x64, 0x06, 0x02, 0x01], // 1.2.840.113635.100.6.2.1
};

function b64(text: string): Uint8Array {
  return Uint8Array.from(atob(text), c => c.charCodeAt(0));
}

function b64url(text: string): Uint8Array {
  const s = text.replace(/-/g, '+').replace(/_/g, '/');
  return b64(s + '='.repeat((4 - s.length % 4) % 4));
}

function contains(haystack: Uint8Array, needle: number[]): boolean {
  outer: for (let i = 0; i + needle.length <= haystack.length; i++) {
    for (let j = 0; j < needle.length; j++) if (haystack[i + j] !== needle[j]) continue outer;
    return true;
  }
  return false;
}

function same(a: Uint8Array, b: Uint8Array): boolean {
  return a.length === b.length && a.every((x, i) => x === b[i]);
}

/** One DER element at `pos`: its tag and where its contents start and end */
function der(bytes: Uint8Array, pos: number): { tag: number; start: number; end: number } {
  if (pos + 2 > bytes.length) throw new Error('bad DER');
  const tag = bytes[pos];
  let len = bytes[pos + 1];
  let start = pos + 2;
  if (len & 0x80) {
    const n = len & 0x7f;
    if (n === 0 || n > 4) throw new Error('bad DER');
    len = 0;
    for (let i = 0; i < n; i++) len = len * 256 + bytes[start + i];
    start += n;
  }
  if (start + len > bytes.length) throw new Error('bad DER');
  return { tag, start, end: start + len };
}

interface Certificate {
  tbs: Uint8Array;        // the signed part
  sigAlg: Uint8Array;     // signatureAlgorithm
  signature: Uint8Array;  // DER ECDSA-Sig-Value
  spki: Uint8Array;
  notBefore: number;
  notAfter: number;
  whole: Uint8Array;
}

function time(bytes: Uint8Array, el: { tag: number; start: number; end: number }): number {
  const s = new TextDecoder().decode(bytes.slice(el.start, el.end));
  const m = el.tag === 0x17 ? /^(\d{2})(\d{2})(\d{2})(\d{2})(\d{2})(\d{2})Z$/.exec(s) : /^(\d{4})(\d{2})(\d{2})(\d{2})(\d{2})(\d{2})Z$/.exec(s);
  if (!m) throw new Error('bad time');
  let year = Number(m[1]);
  if (el.tag === 0x17) year += year < 50 ? 2000 : 1900;
  return Date.UTC(year, Number(m[2]) - 1, Number(m[3]), Number(m[4]), Number(m[5]), Number(m[6]));
}

function parseCertificate(bytes: Uint8Array): Certificate {
  const cert = der(bytes, 0);
  const tbsEl = der(bytes, cert.start);
  const sigAlgEl = der(bytes, tbsEl.end);
  const sigEl = der(bytes, sigAlgEl.end);
  if (cert.tag !== 0x30 || tbsEl.tag !== 0x30 || sigEl.tag !== 0x03) throw new Error('bad certificate');
  let pos = tbsEl.start;
  let field = der(bytes, pos);
  if (field.tag === 0xa0) { pos = field.end; field = der(bytes, pos); } // version
  pos = field.end; field = der(bytes, pos);                            // serial → signature
  pos = field.end; field = der(bytes, pos);                            // → issuer
  pos = field.end; const validity = der(bytes, pos);                   // → validity
  const notBeforeEl = der(bytes, validity.start);
  const notAfterEl = der(bytes, notBeforeEl.end);
  pos = validity.end; field = der(bytes, pos);                         // → subject
  pos = field.end; const spkiEl = der(bytes, pos);                     // → subjectPublicKeyInfo
  if (spkiEl.tag !== 0x30) throw new Error('bad certificate');
  return {
    tbs: bytes.slice(cert.start, tbsEl.end),
    sigAlg: bytes.slice(sigAlgEl.start, sigAlgEl.end),
    signature: bytes.slice(sigEl.start + 1, sigEl.end), // skip the BIT STRING's unused-bits byte
    spki: bytes.slice(pos, spkiEl.end),
    notBefore: time(bytes, notBeforeEl),
    notAfter: time(bytes, notAfterEl),
    whole: bytes,
  };
}

function curveOf(spki: Uint8Array): { name: 'P-256' | 'P-384'; size: number } {
  if (!contains(spki, OID.ecPublicKey)) throw new Error('not an EC key');
  if (contains(spki, OID.p256)) return { name: 'P-256', size: 32 };
  if (contains(spki, OID.p384)) return { name: 'P-384', size: 48 };
  throw new Error('unsupported curve');
}

/** DER ECDSA-Sig-Value (SEQUENCE of two INTEGERs) → the r‖s form WebCrypto wants */
function rawSignature(sig: Uint8Array, size: number): Uint8Array {
  const seq = der(sig, 0);
  const r = der(sig, seq.start);
  const s = der(sig, r.end);
  const out = new Uint8Array(size * 2);
  for (const [el, offset] of [[r, 0], [s, size]] as const) {
    let v = sig.slice(el.start, el.end);
    while (v.length > size && v[0] === 0) v = v.slice(1);
    if (v.length > size) throw new Error('bad signature');
    out.set(v, offset + size - v.length);
  }
  return out;
}

async function keyOf(spki: Uint8Array) {
  const curve = curveOf(spki);
  return { key: await crypto.subtle.importKey('spki', spki, { name: 'ECDSA', namedCurve: curve.name }, false, ['verify']), size: curve.size };
}

/** Was `cert` signed by `issuer`'s key? */
async function signedBy(cert: Certificate, issuer: Certificate): Promise<boolean> {
  const hash = contains(cert.sigAlg, OID.ecdsaSha384) ? 'SHA-384' : contains(cert.sigAlg, OID.ecdsaSha256) ? 'SHA-256' : null;
  if (!hash) return false;
  const { key, size } = await keyOf(issuer.spki);
  return crypto.subtle.verify({ name: 'ECDSA', hash }, key, rawSignature(cert.signature, size), cert.tbs);
}

/**
 * The transaction inside a StoreKit 2 JWS if Apple signed it for one of `bundleIds`, else null.
 * `roots` are the trusted root certificates (base64 DER): Apple Root CA - G3 in production.
 */
export async function verifyAppleTransaction(jws: string, bundleIds: string[], roots: string[] = [APPLE_ROOT_CA_G3]): Promise<AppleTransaction | null> {
  try {
    if (typeof jws !== 'string' || jws.length > 20_000) return null;
    const parts = jws.split('.');
    if (parts.length !== 3) return null;
    const header = JSON.parse(new TextDecoder().decode(b64url(parts[0]))) as { alg?: string; x5c?: string[] };
    if (header.alg !== 'ES256' || !Array.isArray(header.x5c) || header.x5c.length !== 3) return null;
    const [leaf, intermediate, root] = header.x5c.map(c => parseCertificate(b64(c)));
    if (!roots.some(r => same(b64(r), root.whole))) return null;
    if (!contains(leaf.whole, OID.appleReceiptSigning) || !contains(intermediate.whole, OID.appleWwdrIntermediate)) return null;
    if (!await signedBy(intermediate, root) || !await signedBy(leaf, intermediate)) return null;

    const { key } = await keyOf(leaf.spki);
    const signature = b64url(parts[2]);
    if (signature.length !== 64) return null;
    const signed = new TextEncoder().encode(`${parts[0]}.${parts[1]}`);
    if (!await crypto.subtle.verify({ name: 'ECDSA', hash: 'SHA-256' }, key, signature, signed)) return null;

    const t = JSON.parse(new TextDecoder().decode(b64url(parts[1]))) as Partial<AppleTransaction>;
    // The certificates had to be valid when Apple signed it (old purchases restored later still count)
    const when = typeof t.signedDate === 'number' ? t.signedDate : Date.now();
    for (const c of [leaf, intermediate, root]) if (when < c.notBefore || when > c.notAfter) return null;
    if (typeof t.bundleId !== 'string' || !bundleIds.includes(t.bundleId)) return null;
    if (typeof t.productId !== 'string' || typeof t.transactionId !== 'string' || typeof t.originalTransactionId !== 'string') return null;
    return t as AppleTransaction;
  } catch {
    return null;
  }
}
