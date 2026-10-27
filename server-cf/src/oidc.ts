// ─────────────────────────────────────────────────────────────────────────────
// Verifies sign-in tokens (OpenID Connect ID tokens) from Apple and Google.
// The app gets the token from the provider; we check its signature against the
// provider's published keys and that it was issued for our app.
// ─────────────────────────────────────────────────────────────────────────────

export type Provider = 'apple' | 'google';

const ISSUERS: Record<Provider, string[]> = {
  apple: ['https://appleid.apple.com'],
  google: ['https://accounts.google.com', 'accounts.google.com'],
};

const JWKS_URLS: Record<Provider, string> = {
  apple: 'https://appleid.apple.com/auth/keys',
  google: 'https://www.googleapis.com/oauth2/v3/certs',
};

interface Jwk { kid: string; kty: string; n: string; e: string; alg?: string }
const keyCache = new Map<Provider, { keys: Jwk[]; fetchedAt: number }>();

function b64urlDecode(text: string): Uint8Array {
  const s = atob(text.replace(/-/g, '+').replace(/_/g, '/') + '==='.slice((text.length + 3) % 4));
  return Uint8Array.from(s, c => c.charCodeAt(0));
}

async function keysFor(provider: Provider, forceRefresh: boolean): Promise<Jwk[]> {
  const cached = keyCache.get(provider);
  if (cached && !forceRefresh && Date.now() - cached.fetchedAt < 6 * 3600_000) return cached.keys;
  const res = await fetch(JWKS_URLS[provider]);
  if (!res.ok) throw new Error(`could not load ${provider} keys`);
  const keys = ((await res.json()) as { keys: Jwk[] }).keys;
  keyCache.set(provider, { keys, fetchedAt: Date.now() });
  return keys;
}

/**
 * Returns the provider's stable user id ("sub") if the token is genuine, unexpired and
 * issued for one of `audiences`; otherwise null.
 */
export async function verifyIdToken(provider: Provider, token: string, audiences: string[], nonce?: unknown): Promise<string | null> {
  const parts = token.split('.');
  if (parts.length !== 3 || audiences.length === 0) return null;
  let header: { kid?: string; alg?: string };
  let claims: { iss?: string; aud?: string | string[]; exp?: number; sub?: string; nonce?: string };
  try {
    header = JSON.parse(new TextDecoder().decode(b64urlDecode(parts[0])));
    claims = JSON.parse(new TextDecoder().decode(b64urlDecode(parts[1])));
  } catch {
    return null;
  }
  if (header.alg !== 'RS256' || !header.kid) return null;

  let jwk = (await keysFor(provider, false)).find(k => k.kid === header.kid);
  if (!jwk) jwk = (await keysFor(provider, true)).find(k => k.kid === header.kid); // keys rotate
  if (!jwk) return null;

  const key = await crypto.subtle.importKey(
    'jwk', { kty: jwk.kty, n: jwk.n, e: jwk.e, alg: 'RS256', ext: true },
    { name: 'RSASSA-PKCS1-v1_5', hash: 'SHA-256' }, false, ['verify'],
  );
  const signed = new TextEncoder().encode(`${parts[0]}.${parts[1]}`);
  if (!(await crypto.subtle.verify('RSASSA-PKCS1-v1_5', key, b64urlDecode(parts[2]), signed))) return null;

  const aud = Array.isArray(claims.aud) ? claims.aud : [claims.aud];
  if (!claims.iss || !ISSUERS[provider].includes(claims.iss)) return null;
  if (!aud.some(a => a && audiences.includes(a))) return null;
  if (!claims.exp || claims.exp * 1000 < Date.now()) return null;
  // A token asked for with a nonce (the app sends SHA-256 of a random value, then the value itself here)
  // only works for the device that asked for it, so a copied token can't be replayed
  if (claims.nonce !== undefined && (typeof nonce !== 'string' || await sha256Hex(nonce) !== claims.nonce)) return null;
  return claims.sub ?? null;
}

async function sha256Hex(text: string): Promise<string> {
  const digest = new Uint8Array(await crypto.subtle.digest('SHA-256', new TextEncoder().encode(text)));
  return Array.from(digest, b => b.toString(16).padStart(2, '0')).join('');
}
