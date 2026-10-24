const { DISCORD_APPLICATION_ID: id, DISCORD_BOT_TOKEN: token } = process.env;
if (!/^\d{17,20}$/.test(id ?? '') || !token) throw new Error('Discord registration secrets are missing.');
const descriptions = {

  progress: 'Current runner state and wave', statistics: 'Completed runs and lifetime rewards',
  accounts: 'View and switch your saved accounts',
  login: 'Approve a new Epic account on Epic’s website', screenshot: 'Capture only the Fortnite window',
  menu: 'Show the Pilot window on your PC', start: 'Start endurance using saved settings',
  stop: 'Stop the runner', launch: 'Launch Fortnite with the selected account'
};
const command = { name: 'pilot', description: 'Control your paired Endurance Pilot PC', type: 1,
  integration_types: [0, 1], contexts: [0, 1, 2], options: Object.entries(descriptions).map(([name, description]) => ({
    type: 1, name, description
  })) };
const response = await fetch(`https://discord.com/api/v10/applications/${id}/commands`, {
  method: 'PUT', headers: { Authorization: `Bot ${token}`, 'Content-Type': 'application/json',
    'User-Agent': 'DiscordBot (https://github.com/StwEndurancePilot/StwEndurancePilot, 1.0)' }, body: JSON.stringify([command, ...[{ name: 'pair', description: 'Connect your PC to Discord' }, { name: 'unpair', description: 'Disconnect your paired PC' }].map(c => ({ ...c, type: 1, integration_types: [0, 1], contexts: [0, 1, 2] }))]),
  redirect: 'error', signal: AbortSignal.timeout(15_000)
});
if (!response.ok) throw new Error(`Command registration failed (${response.status}).`);
console.log('Registered /pilot, /pair and /unpair.');
