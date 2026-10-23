import test from 'node:test';
import assert from 'node:assert/strict';
import { Miniflare, convertV4MiniflareOptions } from 'miniflare';
import { identity, signedHeaders } from './helpers.js';

test('Cloudflare runtime: signed pairing, concurrent claims, isolation and revocation', async () => {
  const keys = await crypto.subtle.generateKey('Ed25519', true, ['sign', 'verify']);
  const publicKey = Buffer.from(await crypto.subtle.exportKey('raw', keys.publicKey)).toString('hex');
  const app = '333333333333333333';
  const user = '111111111111111111';
  const mf = new Miniflare(convertV4MiniflareOptions({
    modules: [{ type: 'ESModule', path: 'src/index.js' }, { type: 'ESModule', path: 'src/device-auth.js' }], compatibilityDate: '2026-10-23',
    durableObjects: { DEVICES: { className: 'Device', useSQLite: true } },
    ratelimits: { DEVICE_RATE_LIMIT: { namespace_id: '817294', simple: { limit: 120, period: 60 } } },
    bindings: { DISCORD_PUBLIC_KEY: publicKey, DISCORD_APPLICATION_ID: app, PUBLIC_ORIGIN: 'https://pilot.example' }
  }));
  let sequence = 0;
  async function interaction(command, application = app) {
    const timestamp = String(Math.floor(Date.now() / 1000));
    const body = JSON.stringify({ id: String(100000000000000000n + BigInt(++sequence)), type: 2, application_id: application, token: 'secret', user: { id: user }, data: { name: 'pilot', options: [{ name: command, type: 1 }] } });
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
    const code = /Pairing code: ([a-f0-9]+)/.exec(pairing.data.content)[1];
    const tokens = await Promise.all([identity(), identity()]);
    const claims = await Promise.all(tokens.map(key => device('pair', null, { code, publicKey: key.publicKey })));
    assert.deepEqual(claims.map(r => r.status).sort(), [200, 403]);
    const token = tokens[claims.findIndex(r => r.status === 200)];
    assert.equal((await device('poll', token, {}, '222222222222222222')).status, 401);
    assert.equal((await device('poll', token)).status, 200);
    assert.equal((await (await interaction('progress')).json()).type, 5);
    const polls = await Promise.all([device('poll', token), device('poll', token)]);
    const commands = await Promise.all(polls.map(r => r.json()));
    assert.equal(commands.filter(r => r.command !== null).length, 1);
    await interaction('unlink');
    assert.equal((await device('poll', token)).status, 401);
  } finally { await mf.dispose(); }
});
