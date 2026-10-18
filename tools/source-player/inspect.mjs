import { mkdir, writeFile } from 'node:fs/promises';
import { Cdp } from '../../tests/browser/lib.mjs';
import { value, sleep } from '../../tests/browser/chrome.mjs';

const url = process.argv[2] || 'http://127.0.0.1:8082/';
const port = Number(process.argv[3] || 9222);
const output = new URL('./artifacts/', import.meta.url);
await mkdir(output, { recursive: true });
const version = await (await fetch(`http://127.0.0.1:${port}/json/version`)).json();
const browser = await Cdp.connect(version.webSocketDebuggerUrl);
let page, targetId;
const consoleMessages = [], exceptions = [], networkErrors = [];
try {
  ({ targetId } = await browser.send('Target.createTarget', { url: 'about:blank', newWindow: false }));
  const pages = await (await fetch(`http://127.0.0.1:${port}/json/list`)).json();
  page = await Cdp.connect(pages.find(p => p.id === targetId).webSocketDebuggerUrl);
  page.on('Runtime.consoleAPICalled', event => consoleMessages.push({
    type: event.type, message: event.args.map(a => a.value ?? a.description ?? '').join(' '),
  }));
  page.on('Runtime.exceptionThrown', event => exceptions.push(event.exceptionDetails));
  page.on('Network.loadingFailed', event => networkErrors.push(event));
  await Promise.all([page.send('Page.enable'), page.send('Runtime.enable'), page.send('Network.enable')]);
  await page.send('Emulation.setDeviceMetricsOverride', { width: 1280, height: 1000, deviceScaleFactor: 1, mobile: false });
  await page.send('Page.navigate', { url });
  await page.send('Page.bringToFront');
  let snapshot, status;
  const deadline = Date.now() + 240000;
  while (Date.now() < deadline) {
    await sleep(1500);
    try {
      status = await value(page, 'document.getElementById("status")?.textContent');
      snapshot = await value(page, 'globalThis.sourcePlayer?.snapshot()');
    } catch (error) {
      if (!error.message.includes('timed out')) throw error;
      continue;
    }
    if (snapshot?.state?.sourceTick >= 3 || snapshot?.failed || exceptions.length) break;
  }
  let inputObservation;
  if (process.argv.includes('--input') && snapshot?.state && !snapshot.failed) {
    const before = snapshot.state;
    const send = (code, key, down, keyCode) => page.send('Input.dispatchKeyEvent', {
      type: down ? 'keyDown' : 'keyUp', code, key,
      windowsVirtualKeyCode: keyCode, nativeVirtualKeyCode: keyCode,
    });
    await send('KeyW', 'w', true, 87);
    await send('KeyN', 'n', true, 78);
    await sleep(1800);
    await send('KeyW', 'w', false, 87);
    await send('KeyN', 'n', false, 78);
    await send('KeyV', 'v', true, 86);
    await sleep(100);
    await send('KeyV', 'v', false, 86);
    await sleep(1000);
    snapshot = await value(page, 'globalThis.sourcePlayer.snapshot()');
    status = await value(page, 'document.getElementById("status")?.textContent');
    inputObservation = { before, after: snapshot.state,
      actualBrowserKeys: ['W down', 'N down', 'W up', 'N up', 'V down', 'V up'],
      boundary: 'Actual browser keyboard/cabinet input and changing live production frames only; no strict-source portable, snap/pass/tackle or complete M1 qualification.' };
  }
  const screenshot = await page.send('Page.captureScreenshot', { format: 'png' });
  const name = snapshot?.state?.kernel?.portableBound ? 'portable-startup-diagnostic' : 'strict-diagnostic';
  await writeFile(new URL(name + '.png', output), Buffer.from(screenshot.data, 'base64'));
  let gpuProof;
  if (snapshot?.state && !snapshot.failed) gpuProof = await value(page, 'globalThis.sourcePlayer.gpuProof()');
  const evidence = { status, snapshot, gpuProof, inputObservation, version: version.Browser, consoleMessages, exceptions, networkErrors,
    purpose: 'Actual WASM startup/render diagnostic; not complete browser-input/playable-slice qualification.' };
  await writeFile(new URL(name + '.json', output), JSON.stringify(evidence, null, 2) + '\n');
  console.log(JSON.stringify({ status, failed: snapshot?.failed, sourceTick: snapshot?.state?.sourceTick,
    actors: snapshot?.state?.actorCount, device: snapshot?.device, gpuProof: gpuProof && {
      visibleHome: gpuProof.visibleHome, visibleAway: gpuProof.visibleAway, actorPixels: gpuProof.actorPixels,
    }, consoleMessages, exceptions, networkErrors }, null, 2));
  if (!snapshot?.state || snapshot.failed || exceptions.length || networkErrors.length) process.exitCode = 1;
} finally {
  page?.close();
  if (targetId) await browser.send('Target.closeTarget', { targetId });
  browser.close();
}
