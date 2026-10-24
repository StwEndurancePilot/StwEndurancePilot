import test from 'node:test';
import assert from 'node:assert/strict';
import { Miniflare, convertV4MiniflareOptions } from 'miniflare';
import { identity, signedHeaders } from './helpers.js';

for (const discordStatus of [200, 302]) test(`Cloudflare runtime: pairing, isolation, reply HTTP ${discordStatus} and revocation`, async () => {
  const keys = await crypto.subtle.generateKey('Ed25519', true, ['sign', 'verify']);
  const publicKey = Buffer.from(await crypto.subtle.exportKey('raw', keys.publicKey)).toString('hex');
  const app = '333333333333333333';
  const user = '111111111111111111';
  let outboundCalls = 0;
  const mf = new Miniflare(convertV4MiniflareOptions({
    modules: [{ type: 'ESModule', path: 'src/index.js' }, { type: 'ESModule', path: 'src/device-auth.js' }, { type: 'ESModule', path: 'src/messages.js' }], compatibilityDate: '2026-10-23',
    durableObjects: { DEVICES: { className: 'Device', useSQLite: true } },
    ratelimits: { DEVICE_RATE_LIMIT: { namespace_id: '817294', simple: { limit: 120, period: 60 } } },
    bindings: { DISCORD_PUBLIC_KEY: publicKey, DISCORD_APPLICATION_ID: app, PUBLIC_ORIGIN: 'https://pilot.example' },
    outboundService: async request => {
      outboundCalls++;
      assert.equal(new URL(request.url).origin, 'https://discord.com');
      assert.equal(request.method, 'PATCH');
      assert.equal((await request.json()).embeds[0].description, 'Runtime reply');
      return Response.json({ id: 'reply' }, { status: discordStatus,
        headers: discordStatus === 302 ? { Location: 'https://untrusted.example/' } : {} });
    }
  }));
  let sequence = 0;
  async function interaction(command, application = app) {
    const timestamp = String(Math.floor(Date.now() / 1000));
    const body = JSON.stringify({ id: String(100000000000000000n + BigInt(++sequence)), type: 2, application_id: application, token: 'secret', user: { id: user }, data: command === 'pair' ? { name: 'pair' } : command === 'unlink' ? { name: 'unpair' } : { name: command } });
    const signature = Buffer.from(await crypto.subtle.sign('Ed25519', keys.privateKey, new TextEncoder().encode(timestamp + body))).toString('hex');
    return mf.dispatchFetch('https://pilot.example/interactions', { method: 'POST', body,
      headers: { 'X-Signature-Timestamp': timestamp, 'X-Signature-Ed25519': signature } });
  }
  const device = async (route, key, body = {}, id = user) => {
    const raw = JSON.stringify(body);
    return mf.dispatchFetch(`https://pilot.example/device/${id}/${route}`, {
      method: 'POST', headers: key ? await signedHeaders(key, id, route, raw) : {}, body: raw
    });
  };
  try {
    assert.equal((await interaction('pair', '444444444444444444')).status, 401);
    const pairing = await (await interaction('pair')).json();
    assert.equal(pairing.data.flags, 64);
    const code = pairing.data.embeds[0].fields.find(x => x.name === 'One-time pairing code').value.match(/[a-f0-9]{32}/)[0];
    const tokens = await Promise.all([identity(), identity()]);
    const claims = await Promise.all(tokens.map(key => device('pair', null, { code, publicKey: key.publicKey })));
    assert.deepEqual(claims.map(r => r.status).sort(), [200, 403]);
    const token = tokens[claims.findIndex(r => r.status === 200)];
    assert.equal((await device('poll', token, {}, '222222222222222222')).status, 401);
    const connectHeaders = { ...await signedHeaders(token, user, 'connect', '', { method: 'GET' }), Upgrade: 'websocket' };
    const connect = headers => mf.dispatchFetch(`https://pilot.example/device/${user}/connect`, { headers });
    assert.equal((await connect({ Upgrade: 'websocket' })).status, 401);
    assert.equal((await connect({ ...await signedHeaders(token, user, 'connect', ''), Upgrade: 'websocket' })).status, 401, 'POST proof cannot authorize GET upgrade');
    assert.equal((await mf.dispatchFetch('https://pilot.example/device/222222222222222222/connect', { headers: connectHeaders })).status, 401);
    const upgraded = await connect(connectHeaders);
    assert.equal(upgraded.status, 101);
    assert.equal((await connect(connectHeaders)).status, 401, 'Handshake nonce cannot be replayed');
    let socket = upgraded.webSocket;
    const inbox = [];
    let waiter;
    const disconnected = new WeakMap();
    function listen(ws) {
      disconnected.set(ws, { promise: new Promise(resolve => { ws.onDisconnect = resolve; }) });
      ws.addEventListener('message', event => { const value = JSON.parse(event.data); if (value.disconnected) { ws.onDisconnect(); ws.close(1000); return; } if (waiter) { const resolve = waiter; waiter = null; resolve(value); } else inbox.push(value); });
      ws.accept();
    }
    async function next() {
      if (inbox.length) return inbox.shift();
      let timer;
      try { return await Promise.race([new Promise(resolve => { waiter = resolve; }), new Promise((_, reject) => { timer = setTimeout(() => reject(new Error('WebSocket delivery timed out')), 3000); })]); }
      finally { clearTimeout(timer); waiter = null; }
    }
    listen(socket);
    assert.equal((await next()).ready, true);
    assert.equal((await (await interaction('progress')).json()).type, 5);
    const command = (await next()).command;
    assert.equal(command.command, 'progress');
    // Reconnect while an action is outstanding: it must not execute twice.
    const replacement = await connect({ ...await signedHeaders(token, user, 'connect', '', { method: 'GET' }), Upgrade: 'websocket' });
    assert.equal(replacement.status, 101);
    socket = replacement.webSocket;
    listen(socket);
    assert.equal((await next()).ready, true);
    assert.equal((await (await device('poll', token)).json()).command, null, 'Pushed command cannot be redelivered by legacy poll');
    const result = await device('result', token, { id: command.id, content: 'Runtime reply' });
    const resultBody = await result.json();
    assert.equal(result.status, discordStatus === 200 ? 200 : 502, JSON.stringify(resultBody));
    if (discordStatus === 302) assert.equal(resultBody.discordStatus, 302);
    else assert.equal(resultBody.accepted, true);
    assert.equal(outboundCalls, 1, 'Redirect must not forward tokens or results');
    const closed = disconnected.get(socket).promise;
    const unlinked = await (await interaction('unlink')).json();
    assert.match(JSON.stringify(unlinked), /Disconnected/);
    await Promise.race([closed, new Promise((_, reject) => { const timer = setTimeout(() => reject(new Error('Revocation did not invalidate socket')), 3000); timer.unref(); })]);
    assert.equal((await connect({ ...await signedHeaders(token, user, 'connect', '', { method: 'GET' }), Upgrade: 'websocket' })).status, 401);
    assert.equal((await device('poll', token)).status, 401);
  } finally { await mf.dispose(); }
});
