import { digest, exact, isHex, isUser, publicKey, verifyDevice } from './device-auth.js';
export { digest } from './device-auth.js';
const encoder = new TextEncoder();
const hex = (value) => Uint8Array.from(value.match(/../g) ?? [], x => parseInt(x, 16));
const random = (size) => Array.from(crypto.getRandomValues(new Uint8Array(size)), x => x.toString(16).padStart(2, '0')).join('');
const json = (value, status = 200) => Response.json(value, { status, headers: { 'Cache-Control': 'no-store' } });
const reply = content => json({ type: 4, data: { content, flags: 64, allowed_mentions: { parse: [] } } });
const commands = new Set(['progress', 'statistics', 'accounts', 'switch', 'login', 'screenshot', 'menu', 'start', 'stop', 'launch']);

export async function readLimited(request, limit, timeoutMs = 5000) {
  const reader = request.body?.getReader();
  if (!reader) return '';
  let length = 0;
  const chunks = [];
  let timer;
  const timeout = new Promise((_, reject) => { timer = setTimeout(() => reject(new Error('timeout')), timeoutMs); });
  try {
    for (;;) {
      const { done, value } = await Promise.race([reader.read(), timeout]);
      if (done) break;
      length += value.length;
      if (length > limit) throw new Error('size');
      chunks.push(value);
    }
  } finally { clearTimeout(timer); void reader.cancel().catch(() => {}); }
  const bytes = new Uint8Array(length);
  let offset = 0;
  for (const chunk of chunks) { bytes.set(chunk, offset); offset += chunk.length; }
  return new TextDecoder('utf-8', { fatal: true }).decode(bytes);
}

export async function verify(request, body, publicKey, now = Date.now()) {
  const signature = request.headers.get('X-Signature-Ed25519') ?? '';
  const timestamp = request.headers.get('X-Signature-Timestamp') ?? '';
  if (!exact(/^[a-f0-9]{128}$/i, signature) || !exact(/^[a-f0-9]{64}$/i, publicKey) ||
      !exact(/^[0-9]{10}$/, timestamp) || Math.abs(now - Number(timestamp) * 1000) > 300_000) return false;
  try {
    const key = await crypto.subtle.importKey('raw', hex(publicKey), 'Ed25519', false, ['verify']);
    return await crypto.subtle.verify('Ed25519', key, hex(signature), encoder.encode(timestamp + body));
  } catch { return false; }
}

export default {
  async fetch(request, env) {
    try {
      const url = new URL(request.url);
      if (url.protocol !== 'https:' || url.origin !== env.PUBLIC_ORIGIN || url.search) return json({ error: 'Not found' }, 404);
      if (url.pathname === '/interactions' && request.method === 'POST') {
        const body = await readLimited(request, 32768);
        if (!await verify(request, body, env.DISCORD_PUBLIC_KEY)) return json({ error: 'Unauthorized' }, 401);
        const interaction = JSON.parse(body);
        if (interaction.application_id !== env.DISCORD_APPLICATION_ID) return json({ error: 'Unauthorized' }, 401);
        if (interaction.type === 1) return json({ type: 1 });
        const user = interaction.member?.user?.id ?? interaction.user?.id;
        if (!isUser(user) || !isUser(interaction.id) || interaction.type !== 2 || interaction.data?.name !== 'pilot') return reply('Unsupported command.');
        return await env.DEVICES.get(env.DEVICES.idFromName(user)).fetch(new Request('https://device/interaction', {
          method: 'POST', body: JSON.stringify({ interaction, proof: { body, signature: request.headers.get('X-Signature-Ed25519'), timestamp: request.headers.get('X-Signature-Timestamp') } })
        }));
      }
      const route = /^\/device\/(\d{17,20})\/(pair|poll|result|revoke)$/.exec(url.pathname);
      if (!route || request.method !== 'POST') return json({ error: 'Not found' }, 404);
      // Cloudflare supplies this header. Do not persist or log addresses.
      if (!(await env.DEVICE_RATE_LIMIT.limit({ key: request.headers.get('CF-Connecting-IP') ?? 'unknown' })).success)
        return json({ error: 'Too many requests' }, 429);
      // Read with a deadline BEFORE entering the per-user object lock. Slow unauthenticated
      // uploads must not block the user's revocation/commands inside the Durable Object.
      const body = await readLimited(request, route[2] === 'result' ? 2_850_000 : 2048);
      const headers = new Headers();
      for (const key of ['X-Pilot-Timestamp', 'X-Pilot-Nonce', 'X-Pilot-Signature'])
        if (request.headers.has(key)) headers.set(key, request.headers.get(key));
      return await env.DEVICES.get(env.DEVICES.idFromName(route[1])).fetch(new Request(`https://device/${route[2]}`, { method: 'POST', headers, body }));
    } catch { return json({ error: 'Request rejected' }, 400); }
  }
};

export class Device {
  constructor(ctx, env) { this.ctx = ctx; this.env = env; this.storage = ctx.storage; }
  async fetch(request) {
    // Serialize the complete operation, including hash awaits and storage writes.
    return this.ctx.blockConcurrencyWhile(() => this.handle(request));
  }
  async handle(request) {
    const path = new URL(request.url).pathname;
    const now = Date.now();
    let state = await this.storage.get('state') ?? { seen: [] };
    state.seen = state.seen.filter(x => x.until > now);
    if (path === '/interaction') {
      const { interaction: i, proof } = await request.json();
      const option = i.data.options?.[0];
      const command = option?.name;
      if (Number(proof.timestamp) * 1000 <= (state.revokedBefore ?? 0)) return reply('Request predates revocation. Send a new command.');
      if (state.seen.some(x => x.id === i.id)) return reply('This request has already been handled.');
      // A signed unlink must remain available even after the command quota is exhausted.
      if (command === 'unlink') {
        await this.save({ seen: [{ id: i.id, until: now + 600_000 }], revokedBefore: now });
        return reply('Disconnected. Device access has been revoked.');
      }
      if (state.seen.length >= 100) return reply('Too many requests. Try again shortly.');
      state.seen.push({ id: i.id, until: now + 600_000 });
      await this.save(state);
      if (command === 'pair') {
        if (state.publicKey || state.token) return reply('A PC is already paired. Use /pilot unlink before pairing another.');
        if (state.pairUntil > now) return reply('A pairing code is already active. Wait five minutes or use /pilot unlink.');
        const code = random(16);
        state.pairHash = await digest(code);
        state.pairUntil = now + 300_000;
        state.user = i.member?.user?.id ?? i.user.id;
        await this.save(state);
        const user = i.member?.user?.id ?? i.user.id;
        return reply(`In Pilot, open the account menu → Discord remote control. Enter this Worker address: ${this.env.PUBLIC_ORIGIN}\nDiscord user ID: ${user}\nPairing code: ${code}\nExpires in five minutes. Only enter this on your own PC. Never share the code.`);
      }
      if (!commands.has(command)) return reply('Unsupported command.');
      if (!state.publicKey || state.keyExpires <= now || now - (state.lastPoll ?? 0) > 20_000) return reply('Your PC is offline, pairing expired, or remote control is disabled.');
      if (state.pending) return reply('A command is already in progress. Try again shortly.');
      if (now - (state.lastCommand ?? 0) < 3000) return reply('Please wait a few seconds between commands.');
      const account = option.options?.find(x => x.name === 'account')?.value;
      if (command === 'switch' && (!Number.isInteger(account) || account < 1 || account > 100)) return reply('Choose an account number from /pilot accounts.');
      state.lastCommand = now;
      state.pending = { id: i.id, command, account, expires: now + 45_000,
        application: i.application_id, replyToken: i.token, delivered: false, proof };
      await this.save(state);
      return json({ type: 5, data: { flags: 64 } });
    }
    const raw = await request.text();
    const body = JSON.parse(raw);
    if (!body || typeof body !== 'object' || Array.isArray(body)) return json({ error: 'Invalid request' }, 400);
    if (path === '/pair') {
      if (state.publicKey || state.token || !state.pairHash || state.pairUntil <= now) return json({ error: 'Pairing rejected' }, 403);
      // The code has 128 bits of entropy. Rate-limit at the edge; a guessed public user ID
      // must not let unauthenticated callers burn someone else's pairing attempts.
      if (!isHex(body.code, 32) || await digest(body.code) !== state.pairHash || body.token !== undefined) return json({ error: 'Pairing rejected' }, 403);
      try { await publicKey(body.publicKey); } catch { return json({ error: 'Pairing rejected' }, 403); }
      state.publicKey = body.publicKey;
      state.keyExpires = now + 90 * 24 * 60 * 60 * 1000;
      state.nonces = [];
      delete state.pairHash; delete state.pairUntil; delete state.attempts;
      await this.save(state);
      return json({ paired: true, keyExpires: state.keyExpires });
    }
    const nonces = await verifyDevice(request, raw, state, this.env.PUBLIC_ORIGIN, path.slice(1), now);
    if (!nonces) return json({ error: 'Unauthorized' }, 401);
    state.nonces = nonces;
    await this.storage.put('state', state);
    if (path === '/revoke') { await this.save({ seen: [], revokedBefore: now }); return json({ revoked: true }); }
    if (path === '/poll') {
      state.lastPoll = now;
      const p = state.pending;
      const command = p && !p.delivered && p.expires > now ? { id: p.id, command: p.command, account: p.account, expires: p.expires, proof: p.proof } : null;
      if (command) p.delivered = true; // Never replay a mutating command after a lost response.
      await this.save(state);
      return json({ command });
    }
    if (path === '/result') {
      const p = state.pending;
      if (!p || !p.delivered || p.resultReceived || p.id !== body.id || p.expires < now) return json({ error: 'Expired request' }, 409);
      if (typeof body.content !== 'string' || body.content.length > 1900) return json({ error: 'Invalid response' }, 400);
      let image;
      if (body.image != null) {
        if (p.command !== 'screenshot' || typeof body.image !== 'string' || body.image.length > 2_800_000) return json({ error: 'Invalid image' }, 400);
        image = Uint8Array.from(atob(body.image), c => c.charCodeAt(0));
        if (image.length > 2_097_152 || image[0] !== 255 || image[1] !== 216 || image[2] !== 255) return json({ error: 'Invalid image' }, 400);
      }
      // Keep only the reply handle until delivery succeeds or the existing deadline fires.
      // Never persist result text/images, and never redeliver the command after a reply failure.
      p.resultReceived = true;
      delete p.proof;
      await this.save(state);
      try { await this.respond(p, body.content, image); }
      catch (error) {
        return json({ error: 'Discord reply delivery failed', discordStatus: error.discordStatus ?? null }, 502);
      }
      delete state.pending;
      await this.save(state);
      return json({ accepted: true });
    }
    return json({ error: 'Not found' }, 404);
  }
  async save(state) {
    await this.storage.put('state', state);
    const deadlines = [state.pending?.expires, state.pairUntil, ...state.seen.map(x => x.until)].filter(Boolean);
    if (deadlines.length) await this.storage.setAlarm(Math.max(Date.now() + 1000, Math.min(...deadlines)));
    else await this.storage.deleteAlarm();
  }
  async respond(p, content, image) {
    const payload = { content, allowed_mentions: { parse: [] }, attachments: [] };
    let body;
    const headers = { 'User-Agent': 'DiscordBot (https://github.com/StwEndurancePilot/StwEndurancePilot, 1.0)' };
    if (image) {
      payload.attachments = [{ id: 0, filename: 'game.jpg' }];
      body = new FormData();
      body.set('payload_json', JSON.stringify(payload));
      body.set('files[0]', new Blob([image], { type: 'image/jpeg' }), 'game.jpg');
    } else { body = JSON.stringify(payload); headers['Content-Type'] = 'application/json'; }
    // Fixed Discord origin. Device results cannot choose a destination.
    const response = await fetch(`https://discord.com/api/v10/webhooks/${p.application}/${encodeURIComponent(p.replyToken)}/messages/@original`, { method: 'PATCH', body, headers, redirect: 'error', signal: AbortSignal.timeout(8000) });
    await response.body?.cancel();
    if (!response.ok) {
      const error = new Error('Delivery failed');
      error.discordStatus = response.status;
      throw error;
    }
  }
  async alarm() {
    return this.ctx.blockConcurrencyWhile(async () => {
      const state = await this.storage.get('state');
      if (!state) return;
      const now = Date.now();
      const expired = state.pending?.expires <= now ? state.pending : null;
      if (expired) delete state.pending;
      if (state.pairUntil <= now) { delete state.pairUntil; delete state.pairHash; delete state.attempts; }
      state.seen = state.seen.filter(x => x.until > now);
      await this.save(state);
      if (expired) await this.respond(expired, expired.resultReceived
        ? 'The PC handled this command, but its reply could not be delivered. Check Pilot before repeating an action.'
        : 'The PC did not respond in time. Check Pilot before repeating an action.');
    });
  }
}
