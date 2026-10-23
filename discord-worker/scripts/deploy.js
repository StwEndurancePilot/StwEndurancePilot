import { mkdtempSync, writeFileSync, rmSync, rmdirSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { spawnSync } from 'node:child_process';

const secrets = Object.fromEntries(['DISCORD_PUBLIC_KEY', 'DISCORD_APPLICATION_ID', 'PUBLIC_ORIGIN'].map(key => {
  if (!process.env[key]) throw new Error(`Missing ${key}`);
  return [key, process.env[key]];
}));
if (!/^[a-f0-9]{64}$/i.test(secrets.DISCORD_PUBLIC_KEY) || !/^\d{17,20}$/.test(secrets.DISCORD_APPLICATION_ID)) throw new Error('Invalid Discord configuration');
const origin = new URL(secrets.PUBLIC_ORIGIN);
if (origin.protocol !== 'https:' || origin.origin !== secrets.PUBLIC_ORIGIN) throw new Error('PUBLIC_ORIGIN must be an HTTPS origin without a trailing slash');
const directory = mkdtempSync(join(tmpdir(), 'pilot-deploy-'));
const file = join(directory, 'secrets.json');
try {
  writeFileSync(file, JSON.stringify(secrets), { mode: 0o600 });
  const result = spawnSync(process.execPath, ['node_modules/wrangler/bin/wrangler.js', 'deploy', '--secrets-file', file], { stdio: 'inherit' });
  if (result.status !== 0) throw new Error('Deployment failed');
} finally { rmSync(file, { force: true }); rmdirSync(directory); }
