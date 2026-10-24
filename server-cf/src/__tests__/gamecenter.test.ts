import { describe, it, expect, vi, afterEach } from 'vitest';
import { spkiFromCertificate, signedPayload, verifyGameCenter } from '../gamecenter.js';

// A throwaway self-signed certificate and its key, standing in for Apple's
const CERT_B64 = 'MIIDBTCCAe2gAwIBAgIUQRuacZFncguPSqqbgTTlZUn6ZiYwDQYJKoZIhvcNAQELBQAwEjEQMA4GA1UEAwwHVGVzdCBHQzAeFw0yNjEwMjQyMzMzNDhaFw0zNjEwMjEyMzMzNDhaMBIxEDAOBgNVBAMMB1Rlc3QgR0MwggEiMA0GCSqGSIb3DQEBAQUAA4IBDwAwggEKAoIBAQDQqZjz4ad+vhGSKdg9zKpjW2SYhYIiLkjzxZCoJw2AU6XSvQlJcApCBmPjnb83ETEhgnhXC11NizdAHGRcU/14P7v/2Omys1Zig5BDoKtweGrs1rbnimADaqjb+BoVTBXaXISCN2WijbdKlq6Wb1+KE3Qb/387GV6RBo3RKhA3N05n5I6frjm78hPGrbsmIh47Hlfs1E1utvTCM6z2WVxSzpPGy1a9ye2tv6m06ADl2+4flLCSQZxbaQJB5NNi73SpEZZomi0OWvURkMnz8iZ2T/Js1DR+vInfgwczM/1xxrOfg4M2naVmyBiiqWKYzcbK4+Ci34CRNekPIp66vcKPAgMBAAGjUzBRMB0GA1UdDgQWBBQqCAJcZoJ8WdQvzjzo8N/MJrCpgjAfBgNVHSMEGDAWgBQqCAJcZoJ8WdQvzjzo8N/MJrCpgjAPBgNVHRMBAf8EBTADAQH/MA0GCSqGSIb3DQEBCwUAA4IBAQDCgmzGZy4aLQCM+expzdaPhzm/Fyb/QSus3uVWaRY6kaKKg+30byUg0yNSCV7waIlPH0HNZn0uxCQU99cAp6D5HqCGwrRxpeRulORlGMV3/oDT2b62P2SkeJdvQUEEtQCTkaHnuLBC8ABm1R1ZbQjPMgeTGEtE+F5EKrZ1gqFv6TJbs72pcv2j1ZZ+yF2SDiUqtONVLJ3ddi02Gd3duyss/zqcutDcXD81QCVgfh8P9Uo8Cpdukblvf2vJOamAaHa/Wiz/F8bwmoTgBdYnKhRI/s3yUAmQi0r3/3iBamGk2mqEGhBm/eKRNbX8AJN205u2qpKGyU+7xVtClq5szNOi';
const KEY_PKCS8_B64 = 'MIIEvAIBADANBgkqhkiG9w0BAQEFAASCBKYwggSiAgEAAoIBAQDQqZjz4ad+vhGSKdg9zKpjW2SYhYIiLkjzxZCoJw2AU6XSvQlJcApCBmPjnb83ETEhgnhXC11NizdAHGRcU/14P7v/2Omys1Zig5BDoKtweGrs1rbnimADaqjb+BoVTBXaXISCN2WijbdKlq6Wb1+KE3Qb/387GV6RBo3RKhA3N05n5I6frjm78hPGrbsmIh47Hlfs1E1utvTCM6z2WVxSzpPGy1a9ye2tv6m06ADl2+4flLCSQZxbaQJB5NNi73SpEZZomi0OWvURkMnz8iZ2T/Js1DR+vInfgwczM/1xxrOfg4M2naVmyBiiqWKYzcbK4+Ci34CRNekPIp66vcKPAgMBAAECggEAA/ycOltUjBnik7nZSAOp5RXBHB7qPRqdnza7oagusbB8ycQoh27Xa1Uxlw7pBorStv1bV5e/08mgp8jDFsvYsHGl9xNJvSCAZTJ2plbNX8E/6Greehm35ZN7Ssn4lEaXYPbakJ2xWJk/ksfClIeSj5H5P8hdFx/QIZI6pxqYI7iRzCeWRaNUFs8k/HzqI1TNP70BcYIGeizuERoXVsoBeFzItX+yquU+klOH+tAdgP60VLS1m33CxgL5B9aPDXc6g/pufg7XhIf2gYXZeAgqZuhmbQ5rgU3MHlvviPExNEL3LJmPc0VsKbysWdNJs+Jd1RIfuH7eHKqqwMdJ7BkRsQKBgQDq/7x9Q6AIhUn2NB4ajtWTKlFYh3muFezC5ErUiAO4ylELNJxt4fnICRtuhOitE7CHwY0+f3ZEDswogI2O4f8iYqj3PwumhXf+ZGhX6BKtA2LJXlwddtDUTXZvco32tdZiUQpqmBE5z2n6mMRNEay4pIn3F7PYcpALEzOiEXtccQKBgQDjT1cJgrWNfhjF1pfzIwQNBEcMcY8ZkHO7n2KSIE1W/sdBbyyGQz8Zf4w25FVEH9D34f2RcukjzaK8oFa17pD8Q2vDNwzKD9MciSvJVbftfaqLikIjSTaez5w8zJijp9WVGdk1ne1XSNNqxjHqdPQRKUGo8s5xDHafXeN2f/aO/wKBgBeiNpOmfG6+8OxI24oyOTp+tAWiQlOGsvP6v6fWMgGgnzvwQ5ctddBvUgnwZfIaG9PbXFgfu4YmstOT8iifg+gyYenTtlk97Q5xjKjilxY9U+8KhVsVOvVPAQ0+SRa5vODQY6rKtljJClCAkqVplKRRL+rgwF6RTvk+KiAvf+eRAoGAM0bym3KhAgowDHEcVfzQLR+J54S2g03SzcBmvBANYlKXIGuz35XL5jSfrMJVecizmzefu+/wJ+N7kpGY/fGTQ1TyOldwyyRGu2k2N3TeyPRQRKorwUoCyFCNLin5RPHXfIsYNMArikUjbDLJj9pSNIzaAwLDDz2AhPLpHRJAzIECgYAjP93be+S6eZLwmt9eQ7a+Da2WzOgPT+B/ZF10xn6lb4rBaryJvMJUXA4CJoqo/WyMwAnFYKzwTQ1eJ5AAFgcD/75fqtgWJCwxZX2+6rPh2sbjUCnPxiSp7G9gxDkFZh4fkoWSdor7GdYxk2X+gyJ6sEz49Wbu30EO9yBGGVJUQg==';

const b64 = (s: string) => Uint8Array.from(atob(s), c => c.charCodeAt(0));
const toB64 = (u: Uint8Array) => btoa(String.fromCharCode(...u));
const URL_OK = 'https://static.gc.apple.com/public-key/gc-prod-9.cer';

async function proof(over: Record<string, unknown> = {}) {
  const key = await crypto.subtle.importKey('pkcs8', b64(KEY_PKCS8_B64), { name: 'RSASSA-PKCS1-v1_5', hash: 'SHA-256' }, false, ['sign']);
  const salt = crypto.getRandomValues(new Uint8Array(4));
  const timestamp = Date.now();
  const sig = new Uint8Array(await crypto.subtle.sign('RSASSA-PKCS1-v1_5', key, signedPayload('T:_abc123', 'BomSapo.Chess3D', timestamp, salt)));
  return { teamPlayerId: 'T:_abc123', publicKeyUrl: URL_OK, signature: toB64(sig), salt: toB64(salt), timestamp, ...over };
}

describe('Game Center identity', () => {
  afterEach(() => vi.unstubAllGlobals());
  const stubFetch = () => vi.stubGlobal('fetch', vi.fn(async () => new Response(b64(CERT_B64))));

  it('reads the public key out of a certificate', async () => {
    const spki = spkiFromCertificate(b64(CERT_B64));
    await expect(crypto.subtle.importKey('spki', spki, { name: 'RSASSA-PKCS1-v1_5', hash: 'SHA-256' }, false, ['verify'])).resolves.toBeTruthy();
  });

  it('accepts a genuine proof for our app', async () => {
    stubFetch();
    expect(await verifyGameCenter(await proof(), ['BomSapo.Chess3D'])).toBe('T:_abc123');
  });

  it('rejects another app, another player, a stale proof or a non-Apple key', async () => {
    stubFetch();
    expect(await verifyGameCenter(await proof(), ['Some.Other.App'])).toBeNull();
    expect(await verifyGameCenter(await proof({ teamPlayerId: 'T:_someoneelse' }), ['BomSapo.Chess3D'])).toBeNull();
    expect(await verifyGameCenter(await proof({ timestamp: Date.now() - 3600_000 }), ['BomSapo.Chess3D'])).toBeNull();
    expect(await verifyGameCenter(await proof({ publicKeyUrl: 'https://evil.example/apple.com.cer' }), ['BomSapo.Chess3D'])).toBeNull();
    expect(await verifyGameCenter(await proof({ publicKeyUrl: 'http://static.gc.apple.com/k.cer' }), ['BomSapo.Chess3D'])).toBeNull();
  });
});
