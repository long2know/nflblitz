import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { createHash } from 'node:crypto';
import { mkdir, readFile, rm, writeFile } from 'node:fs/promises';
import { resolve } from 'node:path';
import { Cdp } from '../../tests/browser/lib.mjs';

const [baseArg, outputArg, profileArg] = process.argv.slice(2);
const imageSmokeOnly = process.argv[5] === '--image-smoke';
const base = new URL(baseArg), output = resolve(outputArg), profile = resolve(profileArg);
assert(base.protocol === 'http:' && base.hostname === '127.0.0.1' && Number(base.port) > 8097);
assert.notEqual(process.getuid(), 0, 'Use ordinary UID and the normal Chrome sandbox.');
const sha = value => createHash('sha256').update(value).digest('hex');
const pause = ms => new Promise(ok => setTimeout(ok, ms));
const evidence = { schema: 'persistent-verified-cache-actual-browser/v1', status: 'running', base: base.href,
  normalSandbox: true, httpCacheDisabled: true, noGameplayOrRecordMutation: true, stages: [], warnings: [], failures: [] };
let chrome, browser, page, target, closed, epoch = null;
const requests = new Map();
await mkdir(profile, { recursive: false, mode: 0o700 });
for (const name of ['data', 'cache', 'tmp', 'config']) await mkdir(profile + '/' + name, { mode: 0o700 });

async function value(expression) {
  const result = await page.send('Runtime.evaluate', { expression, returnByValue: true, awaitPromise: true });
  assert(!result.exceptionDetails, JSON.stringify(result.exceptionDetails));
  return result.result.value;
}

async function until(predicate, label, timeout = 180000) {
  const deadline = Date.now() + timeout;
  while (Date.now() < deadline) {
    const observed = await value(`({
      failed:globalThis.sourcePlayerLoader?.failed, text:document.querySelector('#status')?.textContent,
      visible:!!document.querySelector('#start-original-audio')&&!document.querySelector('#start-original-audio').hidden,
      cache:globalThis.sourcePlayer?.snapshot()?.verifiedAssetCache,
      state:globalThis.sourcePlayer?.observe()?.state
    })`);
    evidence.lastObservation = observed;
    assert(!observed.failed, observed.text);
    if (predicate(observed)) return observed;
    await pause(100);
  }
  throw new Error('Actual cache stage timed out: ' + label);
}

function reset(name) {
  epoch = { name, started: performance.now(), assetRequests: 0, assetBodyBytes: 0, assetWireBytes: 0,
    binaryRequests: 0, binaryBodyBytes: 0, buildInfoRequests: 0, manifestRequests: 0,
    fixtureRequests: 0, allRequests: 0, allWireBytes: 0, warnings: [], failures: [] };
}

async function loaded(name, navigate = false) {
  reset(name);
  if (navigate) await page.send('Page.navigate', { url: base.href });
  else await page.send('Page.reload', { ignoreCache: true });
  await until(s => s.visible, name);
  const location = await value(`(() => {const r=document.querySelector('#start-original-audio').getBoundingClientRect();
    return {x:r.x+r.width/2,y:r.y+r.height/2};})()`);
  await page.send('Input.dispatchMouseEvent', { type: 'mousePressed', button: 'left', clickCount: 1, ...location });
  await page.send('Input.dispatchMouseEvent', { type: 'mouseReleased', button: 'left', clickCount: 1, ...location });
  await until(s => s.state && s.state.records?.screen === 'Inactive', name + '-actual-Core');
  await pause(250);
  const snapshot = await value('sourcePlayer.snapshot()');
  const result = { ...epoch, milliseconds: performance.now() - epoch.started,
    cache: snapshot.verifiedAssetCache, buildSourceDigest: snapshot.build.sourceRootDigest,
    nativeWasmSha256: snapshot.build.execution.nativeRuntimeSha256, actorCount: snapshot.state.actorCount };
  assert.equal(result.actorCount, 14);
  assert.equal(result.buildInfoRequests, 1, 'Fetch the small current build receipt afresh.');
  assert.equal(result.manifestRequests, 1, 'Fetch the small exact data manifest afresh.');
  assert.deepEqual(result.failures, []);
  evidence.stages.push(result);
  epoch = null;
  return result;
}

try {
  const args = ['--headless=new', '--remote-debugging-address=127.0.0.1', '--remote-debugging-port=0',
    '--user-data-dir=' + profile + '/data', '--disk-cache-dir=' + profile + '/cache', '--no-first-run',
    '--no-default-browser-check', '--disable-background-networking', '--disable-component-update',
    '--disable-sync', '--metrics-recording-only', '--no-pings', '--window-size=1280,900', 'about:blank'];
  evidence.chromeArgs = args;
  chrome = spawn('/opt/google/chrome/chrome', args, { stdio: ['ignore', 'ignore', 'pipe'],
    env: { ...process.env, HOME: profile, TMPDIR: profile + '/tmp', XDG_RUNTIME_DIR: profile + '/tmp',
      XDG_CACHE_HOME: profile + '/cache', XDG_CONFIG_HOME: profile + '/config', XDG_DATA_HOME: profile + '/config' } });
  closed = new Promise(ok => chrome.once('close', ok));
  const endpoint = await new Promise((ok, reject) => {
    let tail = '';
    const timeout = setTimeout(() => reject(new Error('Normal sandbox Chrome startup timeout: ' + tail)), 30000);
    chrome.once('error', error => { clearTimeout(timeout); reject(error); });
    chrome.stderr.on('data', data => {
      tail = (tail + data).slice(-6000);
      const match = tail.match(/DevTools listening on (ws:\/\/127\.0\.0\.1:\d+\/devtools\/browser\/[^\s]+)/);
      if (match) { clearTimeout(timeout); ok(match[1]); }
    });
  });
  browser = await Cdp.connect(endpoint);
  ({ targetId: target } = await browser.send('Target.createTarget', { url: 'about:blank' }));
  const origin = endpoint.replace(/^ws:/, 'http:').split('/devtools/')[0];
  const tabs = await (await fetch(origin + '/json/list')).json();
  page = await Cdp.connect(tabs.find(tab => tab.id === target).webSocketDebuggerUrl);
  page.on('Runtime.consoleAPICalled', event => {
    const message = event.args.map(arg => arg.value ?? arg.description ?? '').join(' ');
    if (event.type === 'warning') { evidence.warnings.push(message); epoch?.warnings.push(message); }
  });
  page.on('Network.requestWillBeSent', event => {
    if (!epoch || !event.request.url.startsWith(base.origin)) return;
    const u = new URL(event.request.url), asset = u.pathname.startsWith(base.pathname + 'data/') &&
      u.pathname !== base.pathname + 'data/manifest.json';
    const binary = u.pathname.startsWith(base.pathname + '_framework/') && /\.(wasm|dat)$/.test(u.pathname);
    const row = { epoch, asset, binary, url: event.request.url };
    requests.set(event.requestId, row); epoch.allRequests++;
    if (asset) epoch.assetRequests++;
    if (binary) epoch.binaryRequests++;
    if (u.pathname.endsWith('/same.bin')) epoch.fixtureRequests++;
    if (u.pathname === base.pathname + 'build-info.json') epoch.buildInfoRequests++;
    if (u.pathname === base.pathname + 'data/manifest.json') epoch.manifestRequests++;
  });
  page.on('Network.responseReceived', event => {
    const row = requests.get(event.requestId);
    if (!row) return;
    const length = Number(Object.entries(event.response.headers).find(([key]) => key.toLowerCase() === 'content-length')?.[1] || 0);
    if (row.asset) row.epoch.assetBodyBytes += length;
    if (row.binary) row.epoch.binaryBodyBytes += length;
  });
  page.on('Network.loadingFinished', event => {
    const row = requests.get(event.requestId);
    if (!row) return;
    row.epoch.allWireBytes += event.encodedDataLength;
    if (row.asset) row.epoch.assetWireBytes += event.encodedDataLength;
    requests.delete(event.requestId);
  });
  page.on('Network.loadingFailed', event => {
    const row = requests.get(event.requestId);
    if (row) row.epoch.failures.push({ url: row.url, error: event.errorText });
  });
  await Promise.all([page.send('Runtime.enable'), page.send('Page.enable'), page.send('Network.enable')]);
  await page.send('Network.setCacheDisabled', { cacheDisabled: true });
  const cold = await loaded('cold-empty-cache', true);
  assert(cold.assetRequests > 1000 && cold.assetRequests <= 1153);
  assert(cold.assetBodyBytes > 250000000 && cold.assetBodyBytes <= 300324289);
  assert(cold.binaryBodyBytes > 25000000);
  const warm = await loaded('warm-reload');
  assert.equal(warm.assetRequests, 0);
  assert.equal(warm.assetBodyBytes, 0);
  assert.equal(warm.binaryRequests, 0);
  assert.equal(warm.cache.downloadedBytes, 0);
  assert(warm.cache.cachedBytes >= 300324289);
  if (!imageSmokeOnly) {
  const manifest = JSON.parse(await readFile(process.env.NFLBLITZ_CACHE_MANIFEST, 'utf8'));
  const victim = manifest.files.find(file => file.path === 'kernel/models/plyr266.h');
  assert(victim);
  await value(`caches.open('nflblitz-verified-content-v1').then(c=>c.put(
    new URL('.verified-assets/${victim.sha256}',${JSON.stringify(base.href)}).href,new Response('corrupt cache bytes')))`);
  const corrupt = await loaded('physical-corrupt-cache-reload');
  assert.equal(corrupt.assetRequests, 1);
  assert.equal(corrupt.assetBodyBytes, victim.bytes);
  assert.equal(corrupt.cache.corruptEntries, 1);
  assert(corrupt.warnings.some(warning => warning.includes('failed integrity')));
  const injection = await page.send('Page.addScriptToEvaluateOnNewDocument', { source:
    `Object.defineProperty(globalThis,'caches',{configurable:true,get(){throw new DOMException('Owned cache-unavailable fixture','SecurityError')}});` });
  const unavailable = await loaded('cache-denied-verified-network-fallback');
  assert.equal(unavailable.assetRequests, 1153);
  assert.equal(unavailable.assetBodyBytes, 300324289);
  assert.equal(unavailable.warnings.filter(warning => warning.includes('cache unavailable')).length, 1);
  assert.equal(unavailable.cache.persistentCacheAvailable, false);
  await page.send('Page.removeScriptToEvaluateOnNewDocument', { identifier: injection.identifier });
  reset('same-path-new-hash-and-base-isolation-real-http');
  const fixtureRoot = process.env.NFLBLITZ_CACHE_FIXTURE_ROOT;
  assert(fixtureRoot);
  const alpha = Buffer.from('original fixture bytes'), beta = Buffer.from('changed same-path fixture bytes');
  const fixtureReady = async () => {
    for (let i = 0; i < 200; i++) {
      if (await value('typeof cacheFixture !== "undefined"')) return;
      await pause(50);
    }
    throw new Error('Production cache module did not load on the owned HTTP contract fixture.');
  };
  await writeFile(fixtureRoot + '/same.bin', alpha);
  await page.send('Page.navigate', { url: base.origin + '/cache-contract-a/index.html' });
  await fixtureReady();
  const loadFixture = async body => value(`cacheFixture.load(${JSON.stringify({
    path: 'same.bin', bytes: body.length, sha256: sha(body) })})`);
  assert.equal(await loadFixture(alpha), alpha.toString());
  await writeFile(fixtureRoot + '/same.bin', beta);
  assert.equal(await loadFixture(beta), beta.toString());
  await page.send('Page.navigate', { url: base.origin + '/cache-contract-b/index.html' });
  await fixtureReady();
  assert.equal(await loadFixture(beta), beta.toString());
  assert.equal(epoch.fixtureRequests, 3, 'Changed SHA and separate base must each actually request the correct bytes.');
  evidence.stages.push({ ...epoch, milliseconds: performance.now() - epoch.started,
    changedSamePathBytesActuallyFetched: true, basePrefixIsolated: true });
  epoch = null;
  }
  evidence.status = 'passed';
} catch (error) {
  evidence.status = 'failed'; evidence.error = String(error.stack || error); process.exitCode = 1;
} finally {
  try { if (page) { page.close(); if (target) await browser.send('Target.closeTarget', { targetId: target }); } } catch {}
  try { if (browser) { await browser.send('Browser.close'); browser.close(); } } catch {}
  if (chrome && chrome.exitCode === null) {
    await Promise.race([closed, pause(5000)]);
    if (chrome.exitCode === null) { chrome.kill('SIGTERM'); await closed; }
  }
  await rm(profile, { recursive: true, force: true });
  evidence.cleanup = { chromeExited: !chrome || chrome.exitCode !== null, exclusiveProfileRemoved: true };
  await writeFile(output, JSON.stringify(evidence, null, 2) + '\n');
  console.log(JSON.stringify({ status: evidence.status, stages: evidence.stages.map(stage => ({
    name: stage.name, milliseconds: stage.milliseconds, assetRequests: stage.assetRequests,
    assetBodyBytes: stage.assetBodyBytes, binaryRequests: stage.binaryRequests, allWireBytes: stage.allWireBytes,
    cache: stage.cache })), error: evidence.error }, null, 2));
}
