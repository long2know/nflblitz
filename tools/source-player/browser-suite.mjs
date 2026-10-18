import { readFile, mkdir, writeFile } from 'node:fs/promises';
import { spawn } from 'node:child_process';
import { launchChrome } from '../../tests/browser/chrome.mjs';

const args = process.argv.slice(2);
const suite = args[0];
if (!['loader', 'baseline', 'features'].includes(suite)) throw new Error('Choose loader, baseline or features actual browser suite.');
const output = args[1] || `tools/source-player/artifacts/${suite}-current`;
await mkdir(output, { recursive: true });
const chrome = await launchChrome(output);
try {
  const port = (await readFile(`${chrome.launch.runtime}/profile/DevToolsActivePort`, 'utf8')).split('\n')[0];
  const script = suite === 'loader' ? 'tools/source-player/loader.browser.test.mjs' :
    suite === 'features' ? 'tools/source-player/feature-options.browser.test.mjs' : 'tools/source-player/browser-smoke.mjs';
  const command = [script, '--url', 'http://127.0.0.1:8083/', '--cdp-port', port];
  if (suite !== 'baseline') command.push('--evidence', `${output}/evidence.json`);
  const child = spawn(process.execPath, command, { stdio: ['ignore', 'pipe', 'pipe'] });
  let log = '';
  child.stdout.on('data', data => { log += data; process.stdout.write(data); });
  child.stderr.on('data', data => { log += data; process.stderr.write(data); });
  const code = await new Promise((resolve, reject) => { child.on('error', reject); child.on('close', resolve); });
  await writeFile(`${output}/command.log`, log);
  await writeFile(`${output}/owned-browser.json`, JSON.stringify({ command, code, browser: chrome.version,
    launch: chrome.launch, originalSharedChromeTouched: false }, null, 2) + '\n');
  if (code !== 0) process.exitCode = code || 1;
} finally { await chrome.close(); }
