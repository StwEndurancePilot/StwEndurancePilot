// Discord-native presentation: restrained colour, clear titles and compact metrics.
const titles = { progress: 'Run progress', statistics: 'Session & lifetime', accounts: 'Your accounts', switch: 'Choose an account', login: 'Connect an Epic account', screenshot: 'Fortnite · live capture', menu: 'Pilot window', start: 'Runner control', stop: 'Runner control', launch: 'Fortnite launch', kill: 'Fortnite closed', restart: 'Fortnite restart', notice: 'Endurance Pilot' };
export function message(command, content, image = false, accounts = null) {
  const embed = { title: titles[command] ?? titles.notice, description: content, color: 0xB8C8BE };
  const confirmed = { start: ['Runner started', 'Run started'], stop: ['Runner stopped', 'Run stopped'], launch: ['Fortnite launch requested', 'Launching Fortnite'], menu: ['Pilot is visible', 'Pilot is open'], switch: ['Selected account changed', 'Account selected'] }[command];
  if (confirmed && content.startsWith(confirmed[0])) { embed.title = confirmed[1]; embed.color = 0x88B79D; }
  const failure = failureDetails(command, content, image);
  if (failure) {
    embed.title = failure.title;
    embed.color = 0xDE777B;
    embed.fields = [{ name: 'What to do next', value: failure.next }];
    embed.footer = { text: 'Endurance Pilot · Action not completed' };
  }
  if (!failure && ['progress', 'statistics'].includes(command) && content.includes('\n') && content.split('\n').every(line => line.includes(': '))) {
    embed.fields = content.split('\n').slice(0, 25).map(line => { const at = line.indexOf(': '); return { name: line.slice(0, at).slice(0, 256), value: line.slice(at + 2).slice(0, 1024) || '—', inline: true }; });
    delete embed.description;
  }
  if (command === 'progress' && content.startsWith('Account: ') && embed.fields) {
    const error = embed.fields.find(field => field.name === 'Error');
    embed.title = error ? 'Run progress · Error' : 'Run progress';
    embed.color = error ? 0xDE777B : 0x88B79D;
    embed.fields = embed.fields.filter(field => field.name !== 'Needs attention');
    embed.footer = { text: 'Live game reading · Session time includes pauses' };
  }
  if (accounts?.length) {
    embed.title = 'Your accounts'; embed.color = 0xB8C8BE;
    const selected = accounts.find(a => a.selected);
    embed.fields = [
      { name: 'Selected for launch', value: selected?.name ?? 'No account selected', inline: true },
      { name: 'Saved on this PC', value: String(accounts.length), inline: true }
    ];
    embed.footer = { text: 'Account changes apply to your next Fortnite launch' };
  }
  if (image) embed.image = { url: 'attachment://game.jpg' };
  return { content: '', embeds: [embed], allowed_mentions: { parse: [] }, attachments: [], components: [] };
}
function failureDetails(command, content, image) {
  // Progress has its own explicit Error field. Account names and wave notes
  // must not accidentally trigger the generic prose error classifier.
  if (command === 'progress' && content.startsWith('Account: ')) return null;
  if (command === 'screenshot' && !image) {
    if (/No visible Fortnite window/i.test(content)) return { title: 'Screenshot unavailable · Fortnite not found', next: 'Open Fortnite on your paired PC, then run `/screenshot` again.' };
    if (/disabled|not permitted/i.test(content)) return { title: 'Screenshot blocked · Permission required', next: 'Enable screenshot access by pairing again in Pilot, then retry.' };
    return { title: 'Screenshot could not be captured', next: 'Check that Fortnite is open and its game window is available on your PC, then retry `/screenshot`.' };
  }
  if (/^No saved accounts/i.test(content)) return { title: 'No accounts connected', next: 'Use `/login` to connect an Epic account, then open `/accounts`.' };
  if (/^Your PC is offline/i.test(content)) return { title: 'PC unavailable', next: 'Open Pilot on your paired PC and check its connection, then retry your command.' };
  if (/^Your saved accounts have changed/i.test(content)) return { title: 'Account list has changed', next: 'Open `/accounts` again and choose from the updated dropdown.' };
  if (/^(Stop the runner|Finish account sign-in)/i.test(content)) return { title: 'Action blocked · PC is busy', next: 'Finish the activity described above on your PC, then try again.' };
  if (/could not|couldn’t|failed|not permitted|disabled|unavailable|offline|did not respond|not be delivered/i.test(content)) return { title: 'Action could not be completed', next: 'Check Pilot on your PC before repeating the command.' };
  return null;
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
