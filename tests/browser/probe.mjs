import assert from 'node:assert/strict';
import { mkdir, rm, writeFile } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { launchChrome, value } from './chrome.mjs';
import { pngStats } from './lib.mjs';

const directory = path.join(path.dirname(fileURLToPath(import.meta.url)), 'artifacts', `transport-${Date.now()}`);
await mkdir(directory, { recursive: true });
const evidence = { scope: 'Chrome/CDP transport only, NOT game evidence', status: 'failed', timestampUtc: new Date().toISOString() };
let chrome;
try {
  chrome = await launchChrome(directory);
  await chrome.cdp.send('Page.navigate', { url: 'about:blank' });
  assert.equal(await value(chrome.cdp, '21 * 2'), 42);
  await value(chrome.cdp, `document.body.style.background = 'rgb(10,150,10)'; 'probe'`);
  const { data } = await chrome.cdp.send('Page.captureScreenshot', { format: 'png' });
  const png = Buffer.from(data, 'base64');
  const stats = pngStats(png);
  assert.ok(stats.greenFraction > 0.9);
  await writeFile(path.join(directory, 'transport.png'), png);
  evidence.status = 'passed';
  evidence.version = chrome.version;
  evidence.stats = stats;
  console.log(`Chrome transport and screenshot pixel decoding verified (${chrome.version.product}). NOT a Unity/WebGL gameplay run.`);
} catch (error) {
  evidence.error = error.message;
  process.exitCode = 1;
  console.error(`Chrome transport unavailable: ${error.message}`);
} finally {
  await chrome?.close();
  for (const name of ['profile', 'cache', 'config']) await rm(path.join(directory, name), { recursive: true, force: true });
  await writeFile(path.join(directory, 'transport.json'), `${JSON.stringify(evidence, null, 2)}\n`);
  console.log(`Transport evidence: ${path.relative(process.cwd(), path.join(directory, 'transport.json'))}`);
}
