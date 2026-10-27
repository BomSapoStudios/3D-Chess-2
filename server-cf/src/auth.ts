// ─────────────────────────────────────────────────────────────────────────────
// Tokens: base64url(JSON payload) + "." + base64url(HMAC-SHA256 signature), signed with
// the TOKEN_SECRET secret. A token names an account (see accounts.ts) and its current name.
// The app refreshes its token each time it signs in.
// ─────────────────────────────────────────────────────────────────────────────

export interface TokenPayload {
  userId: string;
  name: string;
  iat: number; // issued at, ms
}

const TOKEN_LIFETIME_MS = 1000 * 60 * 60 * 24 * 90; // 90 days

function b64url(bytes: Uint8Array): string {
  let s = '';
  for (const b of bytes) s += String.fromCharCode(b);
  return btoa(s).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
}

function fromB64url(text: string): Uint8Array {
  const s = atob(text.replace(/-/g, '+').replace(/_/g, '/'));
  return Uint8Array.from(s, c => c.charCodeAt(0));
}

async function key(secret: string): Promise<CryptoKey> {
  // A missing or weak secret would make tokens forgeable: refuse to sign or accept any
  if (typeof secret !== 'string' || !secret) throw new Error('TOKEN_SECRET is not set');
  return crypto.subtle.importKey(
    'raw', new TextEncoder().encode(secret), { name: 'HMAC', hash: 'SHA-256' }, false, ['sign', 'verify'],
  );
}

export async function issueToken(secret: string, userId: string, name: string): Promise<{ token: string; payload: TokenPayload }> {
  const payload: TokenPayload = { userId, name, iat: Date.now() };
  const body = b64url(new TextEncoder().encode(JSON.stringify(payload)));
  const sig = new Uint8Array(await crypto.subtle.sign('HMAC', await key(secret), new TextEncoder().encode(body)));
  return { token: `${body}.${b64url(sig)}`, payload };
}

export async function verifyToken(secret: string, token: string | null): Promise<TokenPayload | null> {
  if (!token) return null;
  const [body, sig] = token.split('.');
  if (!body || !sig) return null;
  try {
    const ok = await crypto.subtle.verify('HMAC', await key(secret), fromB64url(sig), new TextEncoder().encode(body));
    if (!ok) return null;
    const payload = JSON.parse(new TextDecoder().decode(fromB64url(body))) as TokenPayload;
    if (Date.now() - payload.iat > TOKEN_LIFETIME_MS) return null;
    return payload;
  } catch {
    return null;
  }
}
