import test from 'node:test';
import assert from 'node:assert/strict';
import { message, accountMenus, selection } from '../src/messages.js';
const hash = 'a'.repeat(64);
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
