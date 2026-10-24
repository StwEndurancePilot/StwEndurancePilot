// Discord-native presentation: restrained colour, clear titles and compact metrics.
const titles = { progress: 'Run progress', statistics: 'Session & lifetime', accounts: 'Your accounts', switch: 'Choose an account', login: 'Connect an Epic account', screenshot: 'Fortnite · live capture', menu: 'Pilot window', start: 'Runner control', stop: 'Runner control', launch: 'Fortnite launch', notice: 'Endurance Pilot' };
export function message(command, content, image = false) {
  const embed = { title: titles[command] ?? titles.notice, description: content, color: 0xB8C8BE };
  const confirmed = { start: ['Runner started', 'Run started'], stop: ['Runner stopped', 'Run stopped'], launch: ['Fortnite launch requested', 'Launching Fortnite'], menu: ['Pilot is visible', 'Pilot is open'], switch: ['Selected account changed', 'Account selected'] }[command];
  if (confirmed && content.startsWith(confirmed[0])) { embed.title = confirmed[1]; embed.color = 0x88B79D; }
  if (/could not|couldn’t|failed|not permitted|disabled|unavailable|offline|did not respond|not be delivered/i.test(content)) { embed.color = 0xD6AB78; embed.footer = { text: 'Check Pilot on your PC' }; }
  if (['progress', 'statistics'].includes(command) && content.includes('\n') && content.split('\n').every(line => line.includes(': '))) {
    embed.fields = content.split('\n').slice(0, 25).map(line => { const at = line.indexOf(': '); return { name: line.slice(0, at).slice(0, 256), value: line.slice(at + 2).slice(0, 1024) || '—', inline: true }; });
    delete embed.description;
  }
  if (command === 'accounts') embed.footer = { text: 'Use /pilot switch to choose an account' };
  if (image) embed.image = { url: 'attachment://game.jpg' };
  return { content: '', embeds: [embed], allowed_mentions: { parse: [] }, attachments: [], components: [] };
}
export function accountMenus(accounts, snapshot) {
  if (!/^[a-f0-9]{64}$/.test(snapshot ?? '') || !Array.isArray(accounts) || accounts.length < 1 || accounts.length > 100) throw new Error('Invalid account menu');
  const options = accounts.map((a, i) => {
    if (a.number !== i + 1 || typeof a.name !== 'string' || a.name.length < 1 || a.name.length > 80 || typeof a.selected !== 'boolean') throw new Error('Invalid account option');
    return { label: a.name, value: String(a.number), description: a.selected ? 'Currently selected' : 'Use for your next launch' };
  });
  return Array.from({ length: Math.ceil(options.length / 25) }, (_, page) => ({ type: 1, components: [{ type: 3,
    custom_id: `pilot:switch:${snapshot}:${page}`, placeholder: options.length > 25 ? `Choose account · ${page * 25 + 1}–${Math.min(options.length, (page + 1) * 25)}` : 'Choose a saved account',
    min_values: 1, max_values: 1, options: options.slice(page * 25, (page + 1) * 25) }] }));
}
export function selection(data) {
  const match = /^pilot:switch:([a-f0-9]{64}):([0-3])$/.exec(data?.custom_id ?? '');
  if (!match || data.component_type !== 3 || !Array.isArray(data.values) || data.values.length !== 1 || !/^(?:[1-9][0-9]?|100)$/.test(data.values[0])) return null;
  const account = Number(data.values[0]), page = Number(match[2]);
  return account > page * 25 && account <= (page + 1) * 25 ? { account, accountSet: match[1] } : null;
}
