import { digest } from '../src/device-auth.js';
export async function identity() {
  const pair = await crypto.subtle.generateKey({ name: 'ECDSA', namedCurve: 'P-256' }, true, ['sign', 'verify']);
  return { privateKey: pair.privateKey, publicKey: Buffer.from(await crypto.subtle.exportKey('spki', pair.publicKey)).toString('base64') };
}
export async function signedHeaders(key, user, route, body, overrides = {}) {
  const timestamp = overrides.timestamp ?? String(Date.now());
  const nonce = overrides.nonce ?? Buffer.from(crypto.getRandomValues(new Uint8Array(16))).toString('hex');
  const origin = overrides.origin ?? 'https://pilot.example';
  const message = `pilot-device-v2\n${overrides.method ?? 'POST'}\n${origin}/device/${user}/${route}\n${timestamp}\n${nonce}\n${await digest(body)}`;
  const signature = Buffer.from(await crypto.subtle.sign({ name: 'ECDSA', hash: 'SHA-256' }, key.privateKey, new TextEncoder().encode(message))).toString('hex');
  return { 'X-Pilot-Timestamp': timestamp, 'X-Pilot-Nonce': nonce, 'X-Pilot-Signature': signature };
}
