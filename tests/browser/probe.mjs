import assert from 'node:assert/strict';
import { mkdir, writeFile } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { key, launchChrome, sleep, targetSession, value } from './chrome.mjs';
import { pngStats } from './lib.mjs';

const directory = path.join(path.dirname(fileURLToPath(import.meta.url)), 'artifacts', `transport-${Date.now()}`);
await mkdir(directory, { recursive: true });
const evidence = { scope: 'Chrome/CDP transport only, NOT game evidence', status: 'failed', timestampUtc: new Date().toISOString() };
let chrome;
let otherTarget;
try {
  chrome = await launchChrome(directory);
  await chrome.cdp.send('Emulation.setDeviceMetricsOverride', { width: 1280, height: 800, deviceScaleFactor: 1, mobile: false });
  await chrome.cdp.send('Page.navigate', { url: 'about:blank' });
  await chrome.cdp.send('Page.bringToFront');
  assert.equal(await value(chrome.cdp, '21 * 2'), 42);
  await value(chrome.cdp, `document.body.style.background = 'rgb(10,150,10)'; 'probe'`);
  const { data } = await chrome.cdp.send('Page.captureScreenshot', { format: 'png' });
  const png = Buffer.from(data, 'base64');
  const stats = pngStats(png);
  assert.ok(stats.greenFraction > 0.9);
  await writeFile(path.join(directory, 'transport.png'), png);
  await key(chrome.cdp, 'w', true);
  await key(chrome.cdp, 'Shift', true);
  otherTarget = (await chrome.cdp.send('Target.createTarget', { url: 'about:blank', background: false })).targetId;
  await chrome.cdp.send('Target.activateTarget', { targetId: otherTarget });
  await sleep(200);
  const other = await targetSession(chrome.cdp, otherTarget);
  await key(other, 'w', false);
  await key(other, 'Shift', false);
  const blurred = await value(chrome.cdp, '({hidden:document.hidden, focused:document.hasFocus()})');
  assert.equal(blurred.hidden, true);
  assert.equal(blurred.focused, false);
  await chrome.cdp.send('Page.bringToFront');
  const resumed = await value(chrome.cdp, '({hidden:document.hidden, focused:document.hasFocus()})');
  assert.equal(resumed.hidden, false);
  assert.equal(resumed.focused, true);
  await chrome.cdp.send('Target.closeTarget', { targetId: otherTarget });
  otherTarget = null;
  evidence.status = 'passed';
  evidence.version = chrome.version;
  evidence.launch = chrome.launch;
  evidence.stats = stats;
  evidence.focusTransport = { blurred, resumed, releaseTarget: 'active other tab, NOT hidden game target',
    limitation: 'DOM/tab/input transport only; NOT Unity input/focus behavior.' };
  console.log(`Chrome transport and screenshot pixel decoding verified (${chrome.version.product}). NOT a Unity/WebGL gameplay run.`);
} catch (error) {
  evidence.error = error.message;
  process.exitCode = 1;
  console.error(`Chrome transport unavailable: ${error.message}`);
} finally {
  if (chrome) {
    try {
      if (otherTarget) await chrome.cdp.send('Target.closeTarget', { targetId: otherTarget });
      await chrome.close();
    } catch (error) {
      evidence.status = 'failed';
      evidence.cleanupError = error.message;
      process.exitCode = 1;
    }
  }
  await writeFile(path.join(directory, 'transport.json'), `${JSON.stringify(evidence, null, 2)}\n`);
  console.log(`Transport evidence: ${path.relative(process.cwd(), path.join(directory, 'transport.json'))}`);
}
