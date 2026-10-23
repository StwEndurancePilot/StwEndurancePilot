import test from 'node:test';
import assert from 'node:assert/strict';
import worker, { Device, digest, readLimited, verify } from '../src/index.js';
import { identity, signedHeaders } from './helpers.js';

const userA = '111111111111111111', userB = '222222222222222222';
const app = '333333333333333333';
let sequence = 0;
function fixture() {
  const instances = new Map();
  const keys = new Map();
  const env = { DISCORD_APPLICATION_ID: app, PUBLIC_ORIGIN: 'https://pilot.example', DEVICE_RATE_LIMIT: { limit: async () => ({ success: true }) },
    DEVICES: { idFromName: x => x, get(id) {
      if (!instances.has(id)) {
        let state, alarm;
        const ctx = { blockConcurrencyWhile: fn => fn(), storage: {
          get: async () => structuredClone(state), put: async (_, value) => { state = structuredClone(value); },
          setAlarm: async value => { alarm = value; }, deleteAlarm: async () => { alarm = undefined; }
        } };
        const device = new Device(ctx, env);
        instances.set(id, { device, state: () => state, alarm: () => alarm });
      }
      return instances.get(id).device;
    } }
  };
  async function interaction(user, name, options, id = String(++sequence)) {
    const response = await env.DEVICES.get(user).fetch(new Request('https://device/interaction', {
      method: 'POST', body: JSON.stringify({ interaction: { id, type: 2, application_id: app, token: 'interaction-secret', user: { id: user },
        data: { name: 'pilot', options: [{ name, options }] } }, proof: { timestamp: String(Math.floor(Date.now() / 1000) + 1), body: '{}', signature: 'test' } })
    }));
    return response.json();
  }
  async function device(user, route, token, body = {}) {
    const raw = JSON.stringify(body);
    const headers = keys.has(token) ? await signedHeaders(keys.get(token), user, route, raw) : { Authorization: `Bearer ${token}` };
    return worker.fetch(new Request(`https://pilot.example/device/${user}/${route}`, {
      method: 'POST', headers, body: raw
    }), env);
  }
  async function pair(user, token = 'ab'.repeat(32)) {
    const reply = await interaction(user, 'pair');
    const code = /Pairing code: ([a-f0-9]+)/.exec(reply.data.content)[1];
    const key = await identity();
    keys.set(token, key); // Labels in tests refer to distinct signing identities, not bearer credentials.
    assert.equal((await device(user, 'pair', '', { code, publicKey: key.publicKey })).status, 200);
    return { token, code, key };
  }
  return { env, instances, interaction, device, pair, keys };
}

test('Discord signature validates exact bytes, rejects tampering, missing headers and stale timestamps', async () => {
  const keys = await crypto.subtle.generateKey('Ed25519', true, ['sign', 'verify']);
  const publicKey = Buffer.from(await crypto.subtle.exportKey('raw', keys.publicKey)).toString('hex');
  const timestamp = String(Math.floor(Date.now() / 1000));
  const body = '{"type":1}';
  const sig = Buffer.from(await crypto.subtle.sign('Ed25519', keys.privateKey, new TextEncoder().encode(timestamp + body))).toString('hex');
  const request = new Request('https://pilot.example', { headers: { 'X-Signature-Timestamp': timestamp, 'X-Signature-Ed25519': sig } });
  assert.equal(await verify(request, body, publicKey), true);
  assert.equal(await verify(request, body + ' ', publicKey), false);
  assert.equal(await verify(request, body, publicKey, Date.now() + 400_000), false);
  assert.equal(await verify(new Request('https://pilot.example'), body, publicKey), false);
});

test('public routing rejects forged interactions and internal dispatch paths', async () => {
  const { env } = fixture();
  assert.equal((await worker.fetch(new Request('https://pilot.example/interactions', { method: 'POST', body: '{}' }), env)).status, 401);
  assert.equal((await worker.fetch(new Request(`https://pilot.example/device/${userA}/interaction`, { method: 'POST', body: '{}' }), env)).status, 404);
});

test('pairing stores only a public device key, is single-use, and cannot overwrite a paired PC', async () => {
  const f = fixture();
  const { token, code, key } = await f.pair(userA);
  const state = f.instances.get(userA).state();
  assert.equal(state.token, undefined);
  assert.equal(state.publicKey, key.publicKey);
  assert.equal(state.pairHash, undefined);
  assert.equal(JSON.stringify(state).includes(code), false);
  assert.equal((await f.device(userA, 'pair', '', { code, token: 'cd'.repeat(32) })).status, 403);
  assert.match((await f.interaction(userA, 'pair')).data.content, /already paired/);
});

test('pairing rejects a wrong user and expires without letting bad guesses lock the owner out', async () => {
  const f = fixture();
  const message = await f.interaction(userA, 'pair');
  const code = /Pairing code: ([a-f0-9]+)/.exec(message.data.content)[1];
  const key = await identity();
  assert.equal((await f.device(userB, 'pair', '', { code, publicKey: key.publicKey })).status, 403);
  for (let n = 0; n < 5; n++) assert.equal((await f.device(userA, 'pair', '', { code: 'bad', publicKey: key.publicKey })).status, 403);
  assert.equal((await f.device(userA, 'pair', '', { code, publicKey: key.publicKey })).status, 200);
  await f.interaction(userA, 'unlink');
  await f.interaction(userA, 'pair');
  f.instances.get(userA).state().pairUntil = Date.now() - 1;
  assert.equal((await f.device(userA, 'pair', '', { code, token: 'ab'.repeat(32) })).status, 403);
});

test('device signing keys cannot poll, submit results or revoke another user', async () => {
  const f = fixture();
  const a = await f.pair(userA);
  await f.pair(userB, 'cd'.repeat(32));
  for (const route of ['poll', 'result', 'revoke']) assert.equal((await f.device(userB, route, a.token)).status, 401);
});

test('commands require online PC, replies are private, queued actions deliver once', async () => {
  const f = fixture();
  const { token } = await f.pair(userA);
  assert.match((await f.interaction(userA, 'start')).data.content, /offline/);
  await f.device(userA, 'poll', token);
  const response = await f.interaction(userA, 'start', undefined, 'same-id');
  assert.deepEqual(response, { type: 5, data: { flags: 64 } });
  assert.match((await f.interaction(userA, 'start', undefined, 'same-id')).data.content, /already been handled/);
  const first = await (await f.device(userA, 'poll', token)).json();
  assert.equal(first.command.command, 'start');
  assert.equal(first.command.replyToken, undefined);
  assert.equal((await (await f.device(userA, 'poll', token)).json()).command, null);
  assert.match((await f.interaction(userA, 'stop')).data.content, /already in progress/);
});

test('expired actions cannot be executed or completed; alarm removes interaction tokens', async () => {
  const f = fixture();
  const { token } = await f.pair(userA);
  await f.device(userA, 'poll', token);
  await f.interaction(userA, 'start');
  const pending = f.instances.get(userA).state().pending;
  pending.expires = Date.now() - 1;
  assert.equal((await (await f.device(userA, 'poll', token)).json()).command, null);
  assert.equal((await f.device(userA, 'result', token, { id: pending.id, content: 'done' })).status, 409);
  const device = f.instances.get(userA).device;
  device.respond = async (_, content) => { assert.match(content, /did not respond/); };
  await device.alarm();
  assert.equal(f.instances.get(userA).state().pending, undefined);
});

test('results require delivered matching request and screenshot permission in protocol', async () => {
  const f = fixture();
  const { token } = await f.pair(userA);
  await f.device(userA, 'poll', token);
  await f.interaction(userA, 'progress');
  const id = f.instances.get(userA).state().pending.id;
  assert.equal((await f.device(userA, 'result', token, { id, content: 'done' })).status, 409);
  await f.device(userA, 'poll', token);
  assert.equal((await f.device(userA, 'result', token, { id: 'wrong', content: 'done' })).status, 409);
  assert.equal((await f.device(userA, 'result', token, { id, content: 'done', image: '/9j/' })).status, 400);
  f.instances.get(userA).device.respond = async (_, content) => { assert.equal(content, 'done'); };
  assert.equal((await f.device(userA, 'result', token, { id, content: 'done' })).status, 200);
  assert.equal((await f.device(userA, 'result', token, { id, content: 'done' })).status, 409);
});

test('unlink and local revoke invalidate credentials and clear pending commands', async () => {
  for (const remote of [true, false]) {
    const f = fixture();
    const { token } = await f.pair(userA);
    await f.device(userA, 'poll', token);
    await f.interaction(userA, 'start');
    if (remote) await f.interaction(userA, 'unlink');
    else await f.device(userA, 'revoke', token);
    assert.equal((await f.device(userA, 'poll', token)).status, 401);
    assert.equal(f.instances.get(userA).state().pending, undefined);
  }
});

test('body limits apply to actual streamed bytes and edge rate limits precede device access', async () => {
  await assert.rejects(readLimited(new Request('https://pilot.example', { method: 'POST', body: 'a'.repeat(100) }), 10));
  const f = fixture();
  f.env.DEVICE_RATE_LIMIT.limit = async () => ({ success: false });
  assert.equal((await f.device(userA, 'poll', '')).status, 429);
  assert.equal(f.instances.size, 0);
});

test('signed device requests bind method destination user route body timestamp and nonce', async () => {
  const f = fixture();
  const { key } = await f.pair(userA);
  const raw = '{}';
  const headers = await signedHeaders(key, userA, 'poll', raw);
  const send = (route = 'poll', body = raw, h = headers) => worker.fetch(new Request(`https://pilot.example/device/${userA}/${route}`, { method: 'POST', headers: h, body }), f.env);
  assert.equal((await send('result')).status, 401);
  assert.equal((await send('poll', '{"tampered":true}')).status, 401);
  assert.equal((await send('poll', raw, await signedHeaders(key, userA, 'poll', raw, { origin: 'https://other.example' }))).status, 401);
  assert.equal((await send('poll', raw, await signedHeaders(key, userB, 'poll', raw))).status, 401);
  assert.equal((await send('poll', raw, await signedHeaders(key, userA, 'poll', raw, { timestamp: String(Date.now() - 61_000) }))).status, 401);
  assert.equal((await send()).status, 200);
  assert.equal((await send()).status, 401); // Even a captured legitimate signed request is single-use.
  assert.equal((await send('poll', raw, { Authorization: 'Bearer ' + 'ab'.repeat(32) })).status, 401);
});

test('device request replay remains blocked across object recreation and key expiry', async () => {
  const f = fixture();
  const { key } = await f.pair(userA);
  const headers = await signedHeaders(key, userA, 'poll', '{}');
  const request = () => new Request('https://device/poll', { method: 'POST', headers, body: '{}' });
  const entry = f.instances.get(userA);
  assert.equal((await entry.device.fetch(request())).status, 200);
  const restarted = new Device(entry.device.ctx, f.env);
  assert.equal((await restarted.fetch(request())).status, 401);
  entry.state().keyExpires = Date.now() - 1;
  assert.equal((await f.device(userA, 'poll', 'ab'.repeat(32))).status, 401);
});

test('unlink bypasses full command quota and rejects older signed pairing requests', async () => {
  const f = fixture();
  await f.pair(userA);
  const entry = f.instances.get(userA);
  entry.state().seen = Array.from({ length: 100 }, (_, n) => ({ id: `flood-${n}`, until: Date.now() + 600_000 }));
  assert.match((await f.interaction(userA, 'unlink')).data.content, /revoked/);
  assert.equal(entry.state().publicKey, undefined);
  const response = await entry.device.fetch(new Request('https://device/interaction', { method: 'POST', body: JSON.stringify({
    interaction: { id: 'old', data: { options: [{ name: 'pair' }] } }, proof: { timestamp: String(Math.floor(Date.now() / 1000) - 1) }
  }) }));
  assert.match((await response.json()).data.content, /predates revocation/);
});

test('slow body times out without waiting for the sender to close the stream', async () => {
  let cancelled = false;
  const body = new ReadableStream({ start(controller) { controller.enqueue(new Uint8Array([123])); }, cancel() { cancelled = true; } });
  await assert.rejects(readLimited(new Request('https://pilot.example', { method: 'POST', body, duplex: 'half' }), 2048, 20), /timeout/);
  assert.equal(cancelled, true);
});

test('result delivery uses a fixed Discord destination, no redirects, suppressed mentions and consumes sensitive state', async t => {
  const f = fixture();
  const { token } = await f.pair(userA);
  await f.device(userA, 'poll', token);
  await f.interaction(userA, 'screenshot');
  const { command } = await (await f.device(userA, 'poll', token)).json();
  t.mock.method(globalThis, 'fetch', async (url, init) => {
    assert.equal(new URL(url).origin, 'https://discord.com');
    assert.equal(init.redirect, 'error');
    assert.ok(init.signal);
    const payload = JSON.parse(init.body.get('payload_json'));
    assert.deepEqual(payload.allowed_mentions, { parse: [] });
    assert.equal(payload.attachments[0].filename, 'game.jpg');
    assert.equal(init.body.get('files[0]').type, 'image/jpeg');
    return new Response('', { status: 200 });
  });
  assert.equal((await f.device(userA, 'result', token, { id: command.id, content: '@everyone', image: '/9j/2Q==' })).status, 200);
  const state = JSON.stringify(f.instances.get(userA).state());
  assert.equal(state.includes('interaction-secret'), false);
  assert.equal(state.includes('/9j/2Q=='), false);
});
