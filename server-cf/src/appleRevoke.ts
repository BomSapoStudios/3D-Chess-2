import type { Env } from './index.js';
import { verifyIdToken } from './oidc.js';

// ─────────────────────────────────────────────────────────────────────────────
// Sign in with Apple: cutting the link when a player deletes their account, as Apple
// requires. The app asks Apple for a fresh authorization code just before deleting; we
// trade it for a refresh token and revoke that, which removes the game from the
// player's "Sign in with Apple" list in their Apple ID settings.
//
// Needs a Sign in with Apple key (secret SIWA_KEY, the .p8 file's contents) and its
// key id (SIWA_KEY_ID). Without them this does nothing and the deletion still goes ahead.
// ─────────────────────────────────────────────────────────────────────────────

const APPLE = 'https://appleid.apple.com';

function base64url(bytes: ArrayBuffer | Uint8Array | string): string {
  const b = typeof bytes === 'string' ? new TextEncoder().encode(bytes) : new Uint8Array(bytes);
  let s = '';
  for (const x of b) s += String.fromCharCode(x);
  return btoa(s).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
}

export function revokeConfigured(env: Env): boolean {
  return !!(env.SIWA_KEY && env.SIWA_KEY_ID && env.APNS_TEAM_ID && env.APPLE_AUDIENCES);
}

/** The client secret Apple's token endpoints want: a five-minute ES256 token signed with our key */
async function clientSecret(env: Env, clientId: string): Promise<string> {
  const now = Math.floor(Date.now() / 1000);
  const header = base64url(JSON.stringify({ alg: 'ES256', kid: env.SIWA_KEY_ID }));
  const claims = base64url(JSON.stringify({ iss: env.APNS_TEAM_ID, iat: now, exp: now + 300, aud: APPLE, sub: clientId }));
  const body = env.SIWA_KEY!.replace(/-----[^-]+-----/g, '').replace(/\s+/g, '');
  const der = Uint8Array.from(atob(body), c => c.charCodeAt(0));
  const key = await crypto.subtle.importKey('pkcs8', der, { name: 'ECDSA', namedCurve: 'P-256' }, false, ['sign']);
  const input = `${header}.${claims}`;
  const sig = await crypto.subtle.sign({ name: 'ECDSA', hash: 'SHA-256' }, key, new TextEncoder().encode(input));
  return `${input}.${base64url(sig)}`;
}

/**
 * Revoke the Apple sign-in behind this authorization code, if it belongs to one of
 * `subjects` (the Apple ids linked to the account being deleted). Returns whether it was revoked.
 */
export async function revokeApple(env: Env, authorizationCode: string, subjects: string[]): Promise<boolean> {
  if (!revokeConfigured(env) || !authorizationCode || subjects.length === 0) return false;
  const clientId = env.APPLE_AUDIENCES.split(',').map(s => s.trim()).filter(Boolean)[0];
  const secret = await clientSecret(env, clientId);
  const form = (fields: Record<string, string>) => ({
    method: 'POST',
    headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
    body: new URLSearchParams(fields).toString(),
  });

  const res = await fetch(`${APPLE}/auth/token`, form({ client_id: clientId, client_secret: secret, code: authorizationCode, grant_type: 'authorization_code' }));
  if (!res.ok) { console.log(`[apple revoke] code exchange failed: ${res.status}`); return false; }
  const tokens = await res.json() as { refresh_token?: string; id_token?: string };
  // Only cut a link that belongs to this account
  const subject = tokens.id_token ? await verifyIdToken('apple', tokens.id_token, [clientId]) : null;
  if (!subject || !subjects.includes(subject) || !tokens.refresh_token) return false;

  const revoked = await fetch(`${APPLE}/auth/revoke`, form({ client_id: clientId, client_secret: secret, token: tokens.refresh_token, token_type_hint: 'refresh_token' }));
  if (!revoked.ok) console.log(`[apple revoke] revoke failed: ${revoked.status}`);
  return revoked.ok;
}
