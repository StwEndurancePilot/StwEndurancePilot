const encoder = new TextEncoder();
export const exact = (pattern, value) => typeof value === 'string' && pattern.exec(value)?.[0] === value;
export const isUser = value => exact(/^[0-9]{17,20}$/, value);
export const isHex = (value, length) => typeof value === 'string' && value.length === length && exact(/^[a-f0-9]+$/, value);
export const fromHex = value => Uint8Array.from(value.match(/../g) ?? [], x => parseInt(x, 16));
export const digest = async value => Array.from(new Uint8Array(await crypto.subtle.digest('SHA-256', encoder.encode(value))), x => x.toString(16).padStart(2, '0')).join('');

export async function publicKey(value) {
  if (typeof value !== 'string' || value.length !== 124) throw new Error('Invalid key');
  const bytes = Uint8Array.from(atob(value), c => c.charCodeAt(0));
  if (btoa(String.fromCharCode(...bytes)) !== value) throw new Error('Invalid key');
  return crypto.subtle.importKey('spki', bytes, { name: 'ECDSA', namedCurve: 'P-256' }, false, ['verify']);
}

export async function verifyDevice(request, body, state, origin, route, now) {
  if (request.method !== (route === 'connect' ? 'GET' : 'POST')) return null;
  // Legacy keyExpires values are ignored; unlink/revoke removes the public key.
  if (!state.publicKey || !isUser(state.user)) return null;
  const timestamp = request.headers.get('X-Pilot-Timestamp');
  const nonce = request.headers.get('X-Pilot-Nonce');
  const signature = request.headers.get('X-Pilot-Signature');
  if (!exact(/^[0-9]{13}$/, timestamp) || Math.abs(now - Number(timestamp)) > 60_000 ||
      !isHex(nonce, 32) || !isHex(signature, 128)) return null;
  const nonces = (state.nonces ?? []).filter(x => x.until > now);
  if (nonces.length >= 128 || nonces.some(x => x.value === nonce)) return null;
  const message = `pilot-device-v2\n${route === 'connect' ? 'GET' : 'POST'}\n${origin}/device/${state.user}/${route}\n${timestamp}\n${nonce}\n${await digest(body)}`;
  try {
    if (!await crypto.subtle.verify({ name: 'ECDSA', hash: 'SHA-256' }, await publicKey(state.publicKey), fromHex(signature), encoder.encode(message))) return null;
  } catch { return null; }
  // Keep until every accepted timestamp (including future skew) is no longer valid.
  return [...nonces, { value: nonce, until: Number(timestamp) + 60_001 }];
}
