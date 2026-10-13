import type { Env } from './index.js';

// ─────────────────────────────────────────────────────────────────────────────
// Apple Push Notifications, sent straight from the Worker over HTTPS.
//
// Auth is a short-lived ES256 token signed with the team's APNs key (secret APNS_KEY,
// the .p8 file's contents) and reused for up to 50 minutes, as Apple asks.
// Development builds register with the sandbox; App Store / TestFlight builds with
// production. The app tells us which when it registers its token.
// ─────────────────────────────────────────────────────────────────────────────

export type PushKind = 'joined' | 'started' | 'turn' | 'challenge' | 'friend';

export interface PushMessage {
  title: string;
  body: string;
  /** Opened when the notification is tapped: the room to open, if any */
  roomCode?: string;
  kind: PushKind;
}

export type PushResult = 'sent' | 'gone' | 'failed';

let cachedJwt: { token: string; issued: number } | null = null;
let cachedKey: CryptoKey | null = null;

function base64url(bytes: ArrayBuffer | Uint8Array | string): string {
  const b = typeof bytes === 'string' ? new TextEncoder().encode(bytes) : new Uint8Array(bytes);
  let s = '';
  for (const x of b) s += String.fromCharCode(x);
  return btoa(s).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
}

async function signingKey(pem: string): Promise<CryptoKey> {
  if (cachedKey) return cachedKey;
  const body = pem.replace(/-----[^-]+-----/g, '').replace(/\s+/g, '');
  const der = Uint8Array.from(atob(body), c => c.charCodeAt(0));
  cachedKey = await crypto.subtle.importKey('pkcs8', der, { name: 'ECDSA', namedCurve: 'P-256' }, false, ['sign']);
  return cachedKey;
}

async function providerToken(env: Env): Promise<string> {
  const now = Math.floor(Date.now() / 1000);
  if (cachedJwt && now - cachedJwt.issued < 50 * 60) return cachedJwt.token;
  const header = base64url(JSON.stringify({ alg: 'ES256', kid: env.APNS_KEY_ID }));
  const claims = base64url(JSON.stringify({ iss: env.APNS_TEAM_ID, iat: now }));
  const input = `${header}.${claims}`;
  const sig = await crypto.subtle.sign({ name: 'ECDSA', hash: 'SHA-256' }, await signingKey(env.APNS_KEY!), new TextEncoder().encode(input));
  cachedJwt = { token: `${input}.${base64url(sig)}`, issued: now };
  return cachedJwt.token;
}

export function pushConfigured(env: Env): boolean {
  return !!(env.APNS_KEY && env.APNS_KEY_ID && env.APNS_TEAM_ID && env.APNS_TOPIC);
}

/** Send one notification to one device */
export async function sendPush(env: Env, deviceToken: string, sandbox: boolean, msg: PushMessage): Promise<PushResult> {
  if (!pushConfigured(env)) return 'failed';
  const host = sandbox ? 'https://api.sandbox.push.apple.com' : 'https://api.push.apple.com';
  const payload = {
    aps: { alert: { title: msg.title, body: msg.body }, sound: 'default', 'thread-id': msg.roomCode ?? msg.kind },
    roomCode: msg.roomCode,
    kind: msg.kind,
  };
  try {
    const res = await fetch(`${host}/3/device/${deviceToken}`, {
      method: 'POST',
      headers: {
        authorization: `bearer ${await providerToken(env)}`,
        'apns-topic': env.APNS_TOPIC!,
        'apns-push-type': 'alert',
        'apns-priority': '10',
        // One notification per room and kind: a newer "your move" replaces an older one
        ...(msg.roomCode ? { 'apns-collapse-id': `${msg.roomCode}-${msg.kind}` } : {}),
        'content-type': 'application/json',
      },
      body: JSON.stringify(payload),
    });
    if (res.ok) return 'sent';
    const reason = ((await res.json().catch(() => ({}))) as { reason?: string }).reason ?? '';
    console.log(`[push] ${res.status} ${reason}`);
    // The device uninstalled the app, or the token belongs to the other environment
    if (res.status === 410 || reason === 'BadDeviceToken' || reason === 'DeviceTokenNotForTopic' || reason === 'Unregistered') return 'gone';
    if (res.status === 403) cachedJwt = null; // expired or rejected provider token: make a new one next time
    return 'failed';
  } catch (e) {
    console.log(`[push] error ${String(e)}`);
    return 'failed';
  }
}
