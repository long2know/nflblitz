import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { createHash } from 'node:crypto';
import { lstat, readFile, realpath, rmdir, writeFile } from 'node:fs/promises';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { Cdp } from '../../../tests/browser/lib.mjs';
import { createChromeRuntime } from '../../../tests/browser/runtime.mjs';

const [baseArg, outputArg] = process.argv.slice(2);
assert(baseArg && outputArg, 'Use subpath.browser.mjs <owned loopback /nflblitz/ URL> <new receipt.json>.');
const base = new URL(baseArg), output = resolve(outputArg);
const project = resolve(dirname(fileURLToPath(import.meta.url)), '../../..');
assert(base.protocol === 'http:' && base.hostname === '127.0.0.1' && Number(base.port) > 8097 &&
  base.pathname === '/nflblitz/' && !base.search && !base.hash && !base.username && !base.password);
assert(output.startsWith(resolve(project, '.squad/.scratch/') + '/'), 'Write evidence only in owned project scratch.');
assert.notEqual(process.getuid(), 0, 'Use the unprivileged user and normal Chrome sandbox.');
const root = process.env.CHROME_RUNTIME_ROOT;
assert(root && /^\/home\/long2know\/\.bsp-[a-f0-9]{8}$/.test(root),
  'Create a new uniquely owned short 0700 POSIX browser root; no system temporary paths.');
const rootIdentity = await lstat(root);
assert(rootIdentity.isDirectory() && !rootIdentity.isSymbolicLink() && rootIdentity.uid === process.getuid() &&
  (rootIdentity.mode & 0o777) === 0o700 && await realpath(root) === root);
const hash = bytes => createHash('sha256').update(bytes).digest('hex');
const pause = ms => new Promise(ok => setTimeout(ok, ms));
const expectedHeaders = {
  'cross-origin-opener-policy': 'same-origin', 'cross-origin-embedder-policy': 'require-corp',
  'cross-origin-resource-policy': 'same-origin', 'cache-control': 'no-store',
  'x-content-type-options': 'nosniff', 'referrer-policy': 'no-referrer',
};
const evidence = { schema: 'frontend-exact-image-subpath-browser/v1', status: 'running', base: base.href,
  harnessSha256: hash(await readFile(fileURLToPath(import.meta.url))),
  normalSandbox: true, defaultAutoplay: true, trustedCabinetInputsOnly: true,
  privateClockScoreSeedOrRecordMutation: false, fullRegulationRepeated: false,
  stages: [], exceptions: [], consoleErrors: [], browserErrors: [],
  network: { requests: 0, gameRequests: 0, browserFaviconRequests: 0, violations: [], failures: [],
    headerFailures: [], responses: [] } };
const network = evidence.network, requests = new Map();
let chrome, runtime, browser, page, stopped, exited = false, stage = 'startup';

function coldStartCommand(method, params) {
  const id = ++page.nextId;
  return new Promise((resolve, reject) => {
    const timeout = setTimeout(() => {
      page.pending.delete(id); reject(new Error('Actual cold-start command timed out: ' + method));
    }, 120000);
    page.pending.set(id, { resolve, reject, timeout });
    page.socket.send(JSON.stringify({ id, method, params }));
  });
}

async function value(expression) {
  const params = { expression, returnByValue: true, awaitPromise: true };
  const result = ['trusted-start', 'real-cabinet-and-store-loaded'].includes(stage) ?
    await coldStartCommand('Runtime.evaluate', params) : await page.send('Runtime.evaluate', params);
  assert(!result.exceptionDetails, 'Read-only observation failed: ' + JSON.stringify(result.exceptionDetails));
  return result.result.value;
}

async function state() {
  return value(`(() => {
    const p=globalThis.sourcePlayer?.observe(), s=p?.state, a=p?.audioReceiver;
    return { loaded:!!s, failed:p?.failed === true || globalThis.sourcePlayerLoader?.failed === true,
      status:document.querySelector('#status')?.textContent, sourceTick:s?.sourceTick,
      active:s?.active, menuStage:s?.cabinetSession?.stage, matchPhase:s?.matchPhase,
      screen:s?.records?.screen, storeStatus:s?.records?.storeStatus, busy:s?.records?.busy,
      stationStep:s?.records?.stations?.[0]?.step, generation:s?.records?.Generation,
      recordCount:s?.records?.RecordCount, preGamePhase:s?.preGameAudio?.phase,
      actorPositions:(s?.actors || []).map(actor => actor.position),
      audio:a ? { state:a.contextState, unlocked:a.unlocked, voices:a.startedVoices,
        sampleCount:a.sampleCount, sampleBytes:a.sampleBytes, signal:a.signal } : null };
  })()`);
}

async function until(predicate, label, timeout = 120000) {
  stage = label;
  const deadline = Date.now() + timeout;
  let actual;
  while (Date.now() < deadline) {
    actual = await state();
    assert(!actual.failed, 'Frozen player failed: ' + actual.status);
    if (predicate(actual)) {
      evidence.stages.push({ stage, ...actual });
      console.log(JSON.stringify({ stage, sourceTick: actual.sourceTick, screen: actual.screen,
        menuStage: actual.menuStage, generation: actual.generation, storeStatus: actual.storeStatus }));
      return actual;
    }
    await pause(100);
  }
  evidence.lastObservation = actual;
  throw new Error('Bounded subpath stage timed out: ' + label);
}

async function key(code, down) {
  await page.send('Input.dispatchKeyEvent', { type: down ? 'keyDown' : 'keyUp', code,
    key: code.startsWith('Key') ? code.slice(3).toLowerCase() : code,
    windowsVirtualKeyCode: code.startsWith('Key') ? code.charCodeAt(3) : 27,
    nativeVirtualKeyCode: code.startsWith('Key') ? code.charCodeAt(3) : 27 });
}
const keys = { up: 'KeyW', down: 'KeyS', left: 'KeyA', right: 'KeyD', a: 'KeyV', b: 'KeyB', c: 'KeyN' };
async function tap(binding) {
  const before = await state();
  await key(keys[binding], true); await pause(70); await key(keys[binding], false); await pause(165);
  if (before.menuStage === 'Joining' && binding === 'c' || before.screen === 'Options' && binding === 'a' ||
    before.screen === 'Selection' && before.stationStep === 'Choice' && binding === 'a') await pause(750);
}
async function down(count) { for (let i = 0; i < count; i++) await tap('down'); }
async function keyboard(at, target, columns) {
  while (Math.floor(at / columns) > Math.floor(target / columns)) { await tap('up'); at -= columns; }
  while (Math.floor(at / columns) < Math.floor(target / columns)) { await tap('down'); at += columns; }
  while (at > target) { await tap('left'); at--; }
  while (at < target) { await tap('right'); at++; }
  await tap('a'); return at;
}

async function clickStart() {
  stage = 'trusted-start';
  let rect;
  const deadline = Date.now() + 120000;
  while (Date.now() < deadline) {
    rect = await value(`(() => { const b=document.querySelector('#start-original-audio');
      if(!b || b.hidden || b.disabled)return null; const r=b.getBoundingClientRect();
      return {x:r.x+r.width/2,y:r.y+r.height/2}; })()`);
    if (rect) break;
    await pause(150);
  }
  assert(rect, 'The actual trusted sound-start button did not appear.');
  await coldStartCommand('Input.dispatchMouseEvent', { type: 'mousePressed', button: 'left', clickCount: 1, ...rect });
  await coldStartCommand('Input.dispatchMouseEvent', { type: 'mouseReleased', button: 'left', clickCount: 1, ...rect });
  return until(s => s.menuStage === 'Joining' && s.screen && s.audio?.unlocked, 'real-cabinet-and-store-loaded');
}

// New synthetic cabinet input in the exclusive profile; never record identity/PIN or store contents.
async function identity(create) {
  await tap('c'); await until(s => s.screen === 'Options', 'original-options');
  await tap('a'); await until(s => s.screen === 'Selection', 'original-record-selection');
  await tap('up'); await tap('a');
  let cursor = 0;
  for (const character of 'ZQPFXA') cursor = await keyboard(cursor, 'ABCDEFGHIJKLMNOPQRSTUVWXYZ-*! '.indexOf(character), 4);
  await until(s => s.stationStep === 'Pin', 'original-six-cell-name-completed');
  cursor = 0;
  for (const character of '5937') cursor = await keyboard(cursor, '123456789 0'.indexOf(character), 3);
  await until(s => s.stationStep === (create ? 'Missing' : 'Selected'), create ? 'new-record-not-found' : 'saved-record-selected');
  if (create) {
    await tap('a');
    await until(s => s.stationStep === 'Selected' && !s.busy, 'public-record-create-committed');
  }
}

async function databaseMetadata() {
  const names = await page.send('IndexedDB.requestDatabaseNames', { securityOrigin: base.origin });
  assert.deepEqual(names.databaseNames, ['nflblitz-original-records']);
  const result = await page.send('IndexedDB.requestDatabase', {
    securityOrigin: base.origin, databaseName: 'nflblitz-original-records',
  });
  return result.databaseWithObjectStores;
}

try {
  const response = await fetch(new URL('build-info.json', base));
  assert(response.ok);
  evidence.buildInfoSha256 = hash(Buffer.from(await response.arrayBuffer()));
  assert.equal(evidence.buildInfoSha256, '4bf03362c5a2ee22cb7dea5ae9198f04531a9e25dc968f57f12f7645c5274c14');
  runtime = await createChromeRuntime({ root });
  const args = ['--headless=new', '--remote-debugging-address=127.0.0.1', '--remote-debugging-port=0',
    `--user-data-dir=${runtime.profile}`, `--disk-cache-dir=${runtime.cache}`, '--no-first-run',
    '--no-default-browser-check', '--disable-background-networking', '--disable-component-update',
    '--disable-sync', '--metrics-recording-only', '--no-pings', '--window-size=1280,900', 'about:blank'];
  evidence.chromeArgs = args;
  chrome = spawn('/opt/google/chrome/chrome', args, { stdio: ['ignore', 'ignore', 'pipe'],
    env: { ...process.env, HOME: runtime.directory, TMPDIR: runtime.tmp, XDG_RUNTIME_DIR: runtime.tmp,
      XDG_CACHE_HOME: runtime.cache, XDG_CONFIG_HOME: runtime.config, XDG_DATA_HOME: runtime.config } });
  stopped = new Promise(ok => chrome.once('close', () => { exited = true; ok(); }));
  let stderr = '';
  const endpoint = await new Promise((ok, reject) => {
    const timer = setTimeout(() => reject(new Error('Normal Chrome startup timed out.')), 30000);
    chrome.once('error', error => { clearTimeout(timer); reject(error); });
    chrome.stderr.on('data', data => {
      stderr = (stderr + data.toString()).slice(-6000);
      const match = stderr.match(/DevTools listening on (ws:\/\/127\.0\.0\.1:\d+\/devtools\/browser\/[^\s]+)/);
      if (match) { clearTimeout(timer); ok(match[1]); }
    });
  });
  browser = await Cdp.connect(endpoint);
  evidence.browserVersion = await browser.send('Browser.getVersion');
  const { targetId } = await browser.send('Target.createTarget', { url: 'about:blank' });
  const targets = await (await fetch(`http://127.0.0.1:${new URL(endpoint).port}/json/list`)).json();
  page = await Cdp.connect(targets.find(target => target.id === targetId).webSocketDebuggerUrl);
  await Promise.all(['Page', 'Runtime', 'Network', 'Log', 'IndexedDB'].map(domain => page.send(domain + '.enable')));
  page.on('Network.requestWillBeSent', entry => {
    const url = new URL(entry.request.url);
    if (!['http:', 'https:'].includes(url.protocol)) return;
    const favicon = url.origin === base.origin && url.pathname === '/favicon.ico' && entry.initiator.type === 'other';
    const game = url.origin === base.origin && url.pathname.startsWith(base.pathname);
    network.requests++; network.gameRequests += Number(game); network.browserFaviconRequests += Number(favicon);
    if (!game && !favicon) network.violations.push({ url: url.href, type: entry.type, initiator: entry.initiator.type });
    requests.set(entry.requestId, { url: url.href, game, favicon, type: entry.type });
  });
  page.on('Network.responseReceived', entry => {
    const request = requests.get(entry.requestId);
    if (!request) return;
    if (entry.response.status >= 400) network.failures.push({ ...request, status: entry.response.status });
    const headers = Object.fromEntries(Object.entries(entry.response.headers).map(([key, value]) => [key.toLowerCase(), value]));
    if (request.game) for (const [name, expected] of Object.entries(expectedHeaders))
      if (headers[name] !== expected) network.headerFailures.push({ url: request.url, name, actual: headers[name] });
    network.responses.push({ ...request, status: entry.response.status, mime: entry.response.mimeType,
      headers: Object.fromEntries(Object.keys(expectedHeaders).map(key => [key, headers[key]])) });
  });
  page.on('Network.loadingFailed', entry => {
    const request = requests.get(entry.requestId);
    if (request) network.failures.push({ ...request, error: entry.errorText, canceled: !!entry.canceled });
  });
  page.on('Runtime.exceptionThrown', entry => evidence.exceptions.push(entry.exceptionDetails.text));
  page.on('Runtime.consoleAPICalled', entry => {
    if (entry.type === 'error') evidence.consoleErrors.push(entry.args.map(arg => arg.value || arg.description || arg.type).join(' '));
  });
  page.on('Log.entryAdded', ({ entry }) => { if (entry.level === 'error') evidence.browserErrors.push(entry.text); });
  await page.send('Page.bringToFront'); await page.send('Page.navigate', { url: base.href });
  const initial = await clickStart();
  assert.equal(initial.recordCount, 0, 'Only an exclusive new synthetic profile may be used.');
  evidence.secureContext = await value('({secure:isSecureContext,isolated:crossOriginIsolated})');
  assert.deepEqual(evidence.secureContext, { secure: true, isolated: true });
  evidence.indexedDbBefore = await databaseMetadata();
  await identity(true);
  const saved = await state();
  assert(saved.recordCount > 0 && saved.generation > 0);
  await page.send('Page.reload', { ignoreCache: true });
  const reloaded = await clickStart();
  assert.equal(reloaded.recordCount, saved.recordCount);
  assert.equal(reloaded.generation, saved.generation);
  evidence.indexedDbAfter = await databaseMetadata();
  assert.deepEqual(evidence.indexedDbAfter, evidence.indexedDbBefore);
  await identity(false);
  await tap('a'); await until(s => s.screen === 'Career', 'saved-record-career-loaded');
  await down(4); await tap('a'); await tap('c');
  await until(s => s.menuStage === 'Teams', 'public-team-selection');
  await until(s => !s.preGamePhase || s.preGamePhase === 'Teams', 'source-options-return', 15000);
  await tap('a'); await tap('a'); await tap('a');
  const playing = await until(s => s.menuStage === 'Playing' && s.sourceTick >= 2 && s.audio?.state === 'running',
    'genuine-source-game-start', 90000);
  const awake = await until(s => s.sourceTick >= playing.sourceTick + 20 && s.audio?.voices > 0 &&
    s.audio?.sampleCount > 0 && s.audio?.signal?.rms > 0, 'bounded-natural-source-wakes-and-original-audio', 45000);
  evidence.sourceWakes = { first: playing.sourceTick, last: awake.sourceTick,
    naturallyObserved: awake.sourceTick - playing.sourceTick, originalAudio: awake.audio };
  await key('Escape', true); await key('Escape', false);
  await until(s => s.active === false && s.audio?.state === 'suspended', 'public-pause-for-proof');
  evidence.webgl = await value(`(() => { const canvas=document.querySelector('#game'),gl=canvas.getContext('webgl2');
    if(!gl)return null; const debug=gl.getExtension('WEBGL_debug_renderer_info');
    return {version:gl.getParameter(gl.VERSION),renderer:debug ? gl.getParameter(debug.UNMASKED_RENDERER_WEBGL) :
      gl.getParameter(gl.RENDERER),contextLost:gl.isContextLost(),width:canvas.width,height:canvas.height}; })()`);
  assert(evidence.webgl?.version.includes('WebGL 2') && !evidence.webgl.contextLost);
  const screenshot = await page.send('Page.captureScreenshot', { format: 'png' });
  const image = Buffer.from(screenshot.data, 'base64'), imagePath = resolve(dirname(output), 'subpath-source-game.png');
  await writeFile(imagePath, image, { flag: 'wx', mode: 0o600 });
  evidence.screenshot = { path: imagePath, sha256: hash(image), bytes: image.length };
  assert(network.gameRequests > 963, 'The actual complete payload/runtime must load, not a proxy sentinel.');
  for (const field of ['violations', 'failures', 'headerFailures']) assert.deepEqual(network[field], [], field);
  for (const field of ['exceptions', 'consoleErrors', 'browserErrors']) assert.deepEqual(evidence[field], [], field);
  evidence.store = { createdThroughPublicCabinet: true, selectedAfterReload: true,
    generation: saved.generation, recordCount: saved.recordCount, sameOrigin: base.origin };
  evidence.status = 'passed';
} catch (error) {
  evidence.status = 'failed'; evidence.failure = { stage, name: error.name, message: error.message };
  if (page) try { evidence.lastObservation = await state(); } catch {}
  process.exitCode = 1;
} finally {
  page?.close(); browser?.close();
  if (chrome && !exited) chrome.kill('SIGTERM');
  if (stopped) {
    let timer;
    await Promise.race([stopped, new Promise(ok => { timer = setTimeout(ok, 5000); })]); clearTimeout(timer);
    if (!exited) { chrome.kill('SIGKILL'); await stopped; }
  }
  if (runtime) await runtime.cleanup();
  const actual = await lstat(root);
  assert(actual.ino === rootIdentity.ino && actual.dev === rootIdentity.dev && actual.uid === rootIdentity.uid);
  await rmdir(root);
  evidence.cleanup = { chromeExited: exited, exclusiveProfileAndRootRemoved: true };
  await writeFile(output, JSON.stringify(evidence, null, 2) + '\n', { flag: 'wx', mode: 0o600 });
  console.log(JSON.stringify({ status: evidence.status, stage, evidence: output, requests: network.requests,
    gameRequests: network.gameRequests, rootFaviconRequests: network.browserFaviconRequests,
    errors: evidence.exceptions.length + evidence.consoleErrors.length + evidence.browserErrors.length }));
}
