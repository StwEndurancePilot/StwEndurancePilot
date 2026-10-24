import test from 'node:test';
import assert from 'node:assert/strict';
import { message, accountMenus, selection } from '../src/messages.js';
const hash = 'a'.repeat(64);
test('progress shows account, wave and time, with explicit errors only', () => {
  const content = 'Account: offline-player\nStatus: Running\nWave: 12 / 30\nSession time: 2h 15m 00s\nCompleted runs: 1';
  const normal = message('progress', content).embeds[0];
  assert.equal(normal.title, 'Run progress');
  assert.equal(normal.fields.find(f => f.name === 'Wave').value, '12 / 30');
  assert.equal(normal.fields.some(f => f.name === 'Needs attention'), false);
  const failed = message('progress', content + '\nError: Open Pilot to check the paused runner.').embeds[0];
  assert.equal(failed.title, 'Run progress · Error');
  assert.equal(failed.color, 0xDE777B);
  assert.equal(failed.fields.find(f => f.name === 'Account').value, 'offline-player');
});
test('account menus cover 100 accounts with bounded signed selections', () => {
  const accounts = Array.from({ length: 100 }, (_, i) => ({ number: i + 1, name: `Account ${i + 1}`, selected: i === 0 }));
  const menus = accountMenus(accounts, hash);
  assert.equal(menus.length, 4);
  assert.equal(menus[0].components[0].options[0].description, 'Currently selected');
  for (const [page, row] of menus.entries()) {
    assert.equal(row.components[0].options.length, 25);
    for (const option of row.components[0].options) assert.deepEqual(selection({ component_type: 3, custom_id: row.components[0].custom_id, values: [option.value] }), { account: Number(option.value), accountSet: hash });
    assert.equal(selection({ component_type: 3, custom_id: row.components[0].custom_id, values: [page === 0 ? '26' : '1'] }), null);
  }
  for (const values of [[], ['1','2'], ['01'], ['101'], ['1\n']]) assert.equal(selection({ component_type: 3, custom_id: `pilot:switch:${hash}:0`, values }), null);
  assert.equal(selection({ component_type: 2, custom_id: `pilot:switch:${hash}:0`, values: ['1'] }), null);
  assert.throws(() => accountMenus(accounts, 'invalid'));
  assert.throws(() => accountMenus([...accounts, accounts[0]], hash));
  assert.throws(() => accountMenus([{number: 2, name:'wrong',selected:false}], hash));
});
test('command embeds suppress mentions, separate metrics and embed screenshots', () => {
  for (const command of ['progress','statistics','accounts','switch','login','screenshot','menu','start','stop','launch']) {
    const payload = message(command, 'Example response');
    assert.equal(payload.embeds.length, 1);
    assert.deepEqual(payload.allowed_mentions, { parse: [] });
    assert.equal(payload.content, '');
  }
  assert.deepEqual(message('progress','Runner: running\nWave: 14').embeds[0].fields.map(f=>f.value), ['running','14']);
  assert.equal(message('screenshot','Current game',true).embeds[0].image.url, 'attachment://game.jpg');
});

test('missing Fortnite is a complete error embed, never a capture success or plain text', () => {
  const result = message('screenshot', 'No visible Fortnite window was found.');
  assert.equal(result.content, '');
  const embed = result.embeds[0];
  assert.equal(embed.title, 'Screenshot unavailable · Fortnite not found');
  assert.equal(embed.color, 0xDE777B);
  assert.match(embed.fields[0].value, /Open Fortnite/);
  assert.equal(embed.image, undefined);
  assert.match(embed.footer.text, /not completed/);
});
test('accounts embed presents selected account and count with no separate switch command', () => {
  const result = message('accounts', 'Choose an account below.', false, [{ number: 1, name: 'Saved account', selected: true }]);
  assert.equal(result.embeds[0].fields[0].value, 'Saved account');
  assert.equal(result.embeds[0].fields[1].value, '1');
  assert.equal(JSON.stringify(result).includes('/switch'), false);
});
