import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { mkdir, writeFile } from 'node:fs/promises';
import { resolve } from 'node:path';
import { Cdp, pngStats } from '../../tests/browser/lib.mjs';
import { createChromeRuntime } from '../../tests/browser/runtime.mjs';

const [url, directory] = process.argv.slice(2);
assert(url && directory, 'Use input.browser.mjs <owned player URL> <evidence directory>.');
assert.equal(new URL(url).hostname, '127.0.0.1');
assert.notEqual(process.getuid(), 0, 'Use normal unprivileged Chrome sandboxing.');
const output = resolve(directory), runtime = await createChromeRuntime();
await mkdir(output, { recursive: true });
const evidence = { schema: 'source-player-enter-alias-browser/v1', url,
  normalSandbox: true, defaultAutoplay: true, samples: [], screenshots: [], errors: [] };
const args = ['--headless=new', '--enable-automation', '--remote-debugging-address=127.0.0.1',
  '--remote-debugging-port=0', `--user-data-dir=${runtime.profile}`, `--disk-cache-dir=${runtime.cache}`,
  '--no-first-run', '--no-default-browser-check', '--disable-background-networking',
  '--disable-component-update', '--disable-sync', '--window-size=1280,900', 'about:blank'];
const chrome = spawn('/opt/google/chrome/chrome', args, {
  env: { ...process.env, HOME: runtime.directory, TMPDIR: runtime.tmp, XDG_RUNTIME_DIR: runtime.tmp,
    XDG_CACHE_HOME: runtime.cache, XDG_CONFIG_HOME: runtime.config, XDG_DATA_HOME: runtime.config },
  stdio: ['ignore', 'ignore', 'pipe'],
});
let browser, page, exited = false;
const closed = new Promise(ok => chrome.once('close', () => { exited = true; ok(); }));
const sleep = ms => new Promise(ok => setTimeout(ok, ms));
async function value(expression) {
  const result = await page.send('Runtime.evaluate', { expression, returnByValue: true, awaitPromise: true });
  assert(!result.exceptionDetails, JSON.stringify(result.exceptionDetails));
  return result.result.value;
}
async function until(expression, label, timeout = 120000) {
  const deadline = Date.now() + timeout;
  while (Date.now() < deadline) {
    assert(!await value('globalThis.sourcePlayerLoader?.failed'), await value("document.querySelector('#status')?.textContent"));
    const result = await value(expression);
    if (result) return result;
    await sleep(100);
  }
  evidence.lastState = await value('globalThis.sourcePlayer?.observe()');
  throw new Error('Timed out: ' + label);
}
const codes = { Enter: ['Enter', 13], NumpadEnter: ['Enter', 13], KeyV: ['v', 86], KeyN: ['n', 78] };
async function key(code, down, repeat = false) {
  const [key, number] = codes[code];
  await page.send('Input.dispatchKeyEvent', { type: down ? 'keyDown' : 'keyUp', code, key,
    windowsVirtualKeyCode: number, nativeVirtualKeyCode: number, autoRepeat: repeat,
    ...(code === 'NumpadEnter' ? { location: 3, isKeypad: true } : {}),
    ...(down ? { text: key === 'Enter' ? '\r' : key } : {}) });
}
async function sample(label, held) {
  const input = await until(`(() => { const i=sourcePlayer.observe().state.input;
    return JSON.stringify(i.buttons) === '${JSON.stringify([held ? 1 : 0, 0, 0, 0])}' && i; })()`, label);
  assert.deepEqual(input.axes, Array(8).fill(0));
  evidence.samples.push({ label, input });
}
async function screenshot(name) {
  const { data } = await page.send('Page.captureScreenshot', { format: 'png' });
  const bytes = Buffer.from(data, 'base64');
  assert(pngStats(bytes).colors > 25, 'Require actual rendered content.');
  await writeFile(resolve(output, name + '.png'), bytes);
  evidence.screenshots.push(name + '.png');
}
try {
  const endpoint = await new Promise((ok, reject) => {
    let tail = '';
    const timer = setTimeout(() => reject(new Error('Chrome startup: ' + tail)), 30000);
    chrome.once('error', error => { clearTimeout(timer); reject(error); });
    chrome.once('exit', code => { clearTimeout(timer); reject(new Error('Chrome early exit ' + code + ': ' + tail)); });
    chrome.stderr.on('data', data => {
      tail = (tail + data).slice(-6000);
      const match = tail.match(/DevTools listening on (ws:\/\/127\.0\.0\.1:\d+\/devtools\/browser\/[^\s]+)/);
      if (match) { clearTimeout(timer); ok(match[1]); }
    });
  });
  browser = await Cdp.connect(endpoint);
  evidence.chromeArguments = (await browser.send('Browser.getBrowserCommandLine')).arguments;
  assert(!evidence.chromeArguments.some(arg => /--no-sandbox|--disable.*sandbox|--autoplay-policy/.test(arg)));
  const origin = endpoint.replace(/^ws:/, 'http:').split('/devtools/')[0];
  const tabs = await (await fetch(origin + '/json/list')).json();
  page = await Cdp.connect(tabs.find(tab => tab.type === 'page').webSocketDebuggerUrl);
  page.on('Runtime.exceptionThrown', event => evidence.errors.push(event.exceptionDetails));
  await Promise.all([page.send('Page.enable'), page.send('Runtime.enable')]);
  await page.send('Emulation.setDeviceMetricsOverride', { width: 1280, height: 900, deviceScaleFactor: 1, mobile: false });
  await page.send('Page.navigate', { url });
  await page.send('Page.bringToFront');
  await until(`(() => { const b=document.querySelector('#start-original-audio'); return b && !b.hidden && !b.disabled; })()`,
    'public original-audio start');
  const point = await value(`(() => { const r=document.querySelector('#start-original-audio').getBoundingClientRect();
    return {x:r.x+r.width/2,y:r.y+r.height/2}; })()`);
  for (const type of ['mousePressed', 'mouseReleased'])
    await page.send('Input.dispatchMouseEvent', { type, ...point, button: 'left', clickCount: 1 });
  await until('globalThis.sourcePlayer?.observe().state?.input', 'actual initialized C# input');
  evidence.initial = await value('sourcePlayer.observe().state');
  for (const alias of ['Enter', 'NumpadEnter', 'KeyV']) {
    await key(alias, true); await sample(alias + ' down', true);
    await key(alias, true, true); await sample(alias + ' repeat', true);
    await key(alias, false); await sample(alias + ' up', false);
  }
  await key('Enter', true); await key('KeyV', true);
  await key('Enter', false); await sample('V remains held after Enter release', true);
  await key('KeyV', false); await sample('both aliases released', false);
  assert.match(await value("document.querySelector('.controls').textContent"), /V\s*\/\s*Enter\s*A: confirm/);
  await key('KeyN', true); await sleep(120); await key('KeyN', false);
  await until("sourcePlayer.observe().state.records?.screen === 'Options'", 'original option selection');
  await sleep(1000);
  await key('Enter', true); await sleep(120); await key('Enter', false);
  evidence.choice = await until(`(() => { const s=sourcePlayer.observe().state;
    return s.preGameAudio?.phase === 'PlayerEntry' && s.records?.stations?.[0]?.step === 'Choice' && s; })()`,
    'original name/PIN Yes/No menu');
  await sleep(500);
  await screenshot('original-name-pin-enter-hint');
  await key('Enter', true); await sleep(120); await key('Enter', false);
  await until("sourcePlayer.observe().state.records.stations[0].step !== 'Choice'", 'Enter confirms original menu');
  evidence.confirmed = await value('sourcePlayer.observe().state');
  assert.deepEqual(evidence.confirmed.records.stations.map(station =>
    ({ stationId: station.stationId, step: station.step, selection: station.selection, enteredPositions: station.enteredPositions })),
  [{ stationId: 0, step: 'Selected', selection: 'Guest', enteredPositions: 0 }],
  'One Enter confirms the default No exactly once, without entering name characters or another station');
  assert.equal(evidence.confirmed.cabinetSession.stage, 'Records', 'The same Enter must not also continue to Teams');
  await screenshot('original-name-pin-enter-confirmed');
  evidence.final = await value('sourcePlayer.observe().state');
  assert.deepEqual(evidence.errors, []);
  evidence.status = 'passed';
} catch (error) {
  evidence.status = 'failed'; evidence.failure = error.stack;
  throw error;
} finally {
  await writeFile(resolve(output, 'receipt.json'), JSON.stringify(evidence, null, 2));
  page?.close(); browser?.close();
  if (!exited) chrome.kill('SIGTERM');
  const timeout = setTimeout(() => { if (!exited) chrome.kill('SIGKILL'); }, 5000);
  await closed; clearTimeout(timeout);
  await runtime.cleanup();
}
console.log(JSON.stringify({ status: evidence.status, samples: evidence.samples.length, screenshots: evidence.screenshots }));
