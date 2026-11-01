import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { createReadStream } from 'node:fs';
import { mkdir, readFile, writeFile } from 'node:fs/promises';
import { createServer } from 'node:http';
import { extname, relative, resolve, sep } from 'node:path';
import { Cdp } from '../../tests/browser/lib.mjs';
import { createChromeRuntime } from '../../tests/browser/runtime.mjs';
import { value, sleep } from '../../tests/browser/chrome.mjs';

const app = resolve(process.argv[2]), output = resolve(process.argv[3]);
await mkdir(relative(process.cwd(), output), { recursive: true });
const save = (name, bytes) => writeFile(relative(process.cwd(), resolve(output, name)), bytes);
const receipt = JSON.parse(await readFile(resolve(app, 'build-info.json'), 'utf8'));
const generations = {
  'original-teams-complete-original303': 'fef5f838759a50afdacc4d94b0749d580b1ac90406624c887fc6ea01dc6cfa42',
  'original-teams-complete-original303-source-banks': '9df8e3a0e0cfddc0784e01c094547fdbd2fda1cb72d97d8cefafb515ca19097b',
  'original-teams-complete-original303-final': '6ad3183827cc27e5c74fd9bc140f4295266285ab9f955a190da407d825d004ce',
  'original-teams-complete-original303-prematch': '309ca9cf4f7f0d3b9f8b43427ab581fda4257d072ea00bfdb4ccc0003c2cba4f',
};
assert.equal(receipt.dataManifestSha256, generations[receipt.profile]);
const mime = { '.js': 'text/javascript', '.json': 'application/json', '.wasm': 'application/wasm', '.html': 'text/html' };
const server = createServer((request, response) => {
  const name = decodeURIComponent(new URL(request.url, 'http://127.0.0.1').pathname);
  const path = resolve(app, '.' + (name === '/' ? '/index.html' : name));
  if (!path.startsWith(app + sep)) { response.writeHead(403).end(); return; }
  response.setHeader('Content-Type', mime[extname(path)] ?? 'application/octet-stream');
  response.setHeader('Cache-Control', 'no-store');
  response.setHeader('Cross-Origin-Opener-Policy', 'same-origin');
  response.setHeader('Cross-Origin-Embedder-Policy', 'require-corp');
  const stream = createReadStream(path);
  stream.on('error', () => { if (!response.headersSent) response.writeHead(404); response.end(); });
  stream.pipe(response);
});
await new Promise(ok => server.listen(0, '127.0.0.1', ok));
assert(server.address().port > 8097, 'Use only a unique owned loopback port above protected old routes.');
const url = `http://127.0.0.1:${server.address().port}/` +
  (receipt.profile === 'original-teams-complete-original303-final' ? '?source-records=disabled' : '');
assert.equal((await fetch(url)).status, 200);
assert.notEqual(process.getuid?.(), 0, 'This proof never disables the Chrome sandbox to run as root.');
const runtime = await createChromeRuntime();
const args = ['--headless=new', '--remote-debugging-address=127.0.0.1', '--remote-debugging-port=0',
  `--user-data-dir=${runtime.profile}`, `--disk-cache-dir=${runtime.cache}`, '--no-first-run',
  '--no-default-browser-check', '--disable-background-networking', '--disable-component-update',
  '--disable-sync', '--metrics-recording-only', '--no-pings', '--window-size=1280,900', 'about:blank'];
const child = spawn('/opt/google/chrome/chrome', args, { stdio: ['ignore', 'ignore', 'pipe'], env: {
  ...process.env, HOME: runtime.directory, TMPDIR: runtime.tmp, XDG_RUNTIME_DIR: runtime.tmp,
  XDG_CACHE_HOME: runtime.cache, XDG_CONFIG_HOME: runtime.config, XDG_DATA_HOME: runtime.config,
} });
let stderr = '', page, exited = false;
const closed = new Promise(ok => child.once('close', () => { exited = true; ok(); }));
const evidence = { schema: 'original-completion-real-browser/v1', status: 'running', url, args, pid: child.pid,
  dataManifestSha256: receipt.dataManifestSha256, sourceRootDigest: receipt.sourceRootDigest,
  inputPolicy: 'Only trusted public click and keyboard events; no game-state/clock/RNG/score setter.',
  nativeDspEquivalent: false, messages: [], snapshots: [] };
async function until(expression, label, timeout = 120000) {
  expression = expression.replaceAll('sourcePlayer.snapshot()', '(sourcePlayer.observe?.() ?? sourcePlayer.snapshot())');
  const end = Date.now() + timeout;
  while (Date.now() < end) {
    const state = await value(page, `({ok:(${expression}), failed:sourcePlayerLoader.failed, text:document.querySelector('#status').textContent})`);
    if (state.failed) throw new Error(state.text);
    if (state.ok) return state.ok;
    await sleep(80);
  }
  throw new Error('Timed out: ' + label);
}
async function press(letter) {
  const code = letter.toUpperCase().charCodeAt(0);
  await page.send('Input.dispatchKeyEvent', { type: 'keyDown', key: letter, code: 'Key' + letter.toUpperCase(),
    windowsVirtualKeyCode: code, nativeVirtualKeyCode: code, text: letter });
  await sleep(120);
  await page.send('Input.dispatchKeyEvent', { type: 'keyUp', key: letter, code: 'Key' + letter.toUpperCase(),
    windowsVirtualKeyCode: code, nativeVirtualKeyCode: code });
  await sleep(150);
}
try {
  const endpoint = await new Promise((ok, reject) => {
    const timer = setTimeout(() => reject(new Error('Chrome startup timeout: ' + stderr)), 30000);
    child.once('error', error => { clearTimeout(timer); reject(error); });
    child.once('exit', (code, signal) => { clearTimeout(timer); reject(new Error(`Chrome exit${code}/${signal}: ${stderr}`)); });
    child.stderr.on('data', data => {
      stderr = (stderr + String(data)).slice(-6000);
      const match = stderr.match(/DevTools listening on (ws:\/\/127\.0\.0\.1:\d+\/devtools\/browser\/[^\s]+)/);
      if (match) { clearTimeout(timer); ok(match[1]); }
    });
  });
  const response = await fetch(`http://127.0.0.1:${new URL(endpoint).port}/json/list`);
  assert.equal(response.status, 200);
  const targets = await response.json();
  page = await Cdp.connect(targets.find(target => target.type === 'page').webSocketDebuggerUrl);
  await Promise.all([page.send('Page.enable'), page.send('Runtime.enable'), page.send('Network.enable')]);
  page.on('Runtime.consoleAPICalled', event => evidence.messages.push({
    type: event.type, text: event.args.map(arg => arg.value ?? arg.description ?? '').join(' ').slice(0, 2400),
  }));
  page.on('Runtime.exceptionThrown', event => evidence.messages.push({ type: 'exception', value: event.exceptionDetails }));
  evidence.version = await page.send('Browser.getVersion');
  await page.send('Page.navigate', { url });
  await until("!document.querySelector('#start-original-audio').hidden", 'explicit original audio start');
  evidence.beforeGesture = await value(page, 'sourcePlayerAudio.snapshot()');
  assert.equal(evidence.beforeGesture.unlocked, false);
  const point = await value(page, `(()=>{const r=document.querySelector('#start-original-audio').getBoundingClientRect();return {x:r.x+r.width/2,y:r.y+r.height/2};})()`);
  await page.send('Input.dispatchMouseEvent', { type: 'mousePressed', ...point, button: 'left', clickCount: 1 });
  await page.send('Input.dispatchMouseEvent', { type: 'mouseReleased', ...point, button: 'left', clickCount: 1 });
  // The actual303 pose/header loader is synchronous; do not time out a CDP evaluation during its measured38s cold start.
  await sleep(50000);
  await until('globalThis.sourcePlayer?.snapshot().state?.frames > 1', 'actual initialized complete original C# world');
  evidence.snapshots.push(await value(page, '(sourcePlayer.observe?.() ?? sourcePlayer.snapshot())'));
  evidence.menuGpu = await value(page, 'sourcePlayer.gpuProof()');
  await save('menu.png', Buffer.from((await page.send('Page.captureScreenshot', { format: 'png' })).data, 'base64'));
  if (receipt.profile === 'original-teams-complete-original303-prematch') {
    await press('n');
    await until("sourcePlayer.snapshot().state.cabinetSession.stage === 'Records'", 'actual original options');
    const entry = await value(page, '(sourcePlayer.observe?.() ?? sourcePlayer.snapshot()).state.preGameAudio.SourceTick');
    await until(`sourcePlayer.snapshot().state.preGameAudio.SourceTick >= ${entry + 35}`, 'actual original30-wake options gate');
    await press('d');
    await press('v');
    await until("sourcePlayer.snapshot().state.preGameAudio.phase === 'Teams'", 'genuine original20 outro and48-wake transition');
  } else {
    await press('n');
    await until("sourcePlayer.snapshot().state.cabinetSession.stage === 'Teams'", 'Teams');
  }
  for (const [letter, stage] of [['v', 'Rosters'], ['v', 'Versus'], ['v', 'Playing']]) {
    await press(letter);
    await until(`sourcePlayer.snapshot().state.cabinetSession.stage === '${stage}'`, stage);
  }
  await until('sourcePlayer.snapshot().state.sourceTick >= 800', 'genuine moving play beyond the preserved source387 scheduling failure');
  for (let n = 0; n < 15; n++) {
    evidence.snapshots.push(await value(page, '(sourcePlayer.observe?.() ?? sourcePlayer.snapshot())'));
    await sleep(80);
  }
  const normalDeadline = Date.now() + 180000;
  let preSnap;
  while (Date.now() < normalDeadline) {
    const observed = await value(page, '(sourcePlayer.observe?.() ?? sourcePlayer.snapshot())');
    if (observed.failed) throw new Error(await value(page, 'document.querySelector("#status").textContent'));
    const s = observed.state;
    if (s.phase === 'PreSnap' && s.playKind === 'Normal' &&
      s.audibles?.core?.Offense?.CallerStationId === 0) { preSnap = observed; break; }
    const graphics = (s.instructions?.pageGraphics || 0) +
      (s.instructions?.kickoffGraphics || 0) + (s.instructions?.introductionGraphics || 0);
    if (graphics) await press('v');
    else if (s.phase === 'PlaySelect') await press('b');
    else await sleep(80);
  }
  assert(preSnap, 'A genuine human normal PreSnap is required for the post-resume first-snap regression.');
  const eligible = preSnap.state.sourceTick + 60;
  await until(`sourcePlayer.snapshot().state.sourceTick >= ${eligible}`, 'actual normal pre-snap warmup');
  evidence.beforePublicPause = await value(page, '(sourcePlayer.observe?.() ?? sourcePlayer.snapshot())');
  await page.send('Input.dispatchKeyEvent', { type: 'keyDown', key: 'Escape', code: 'Escape',
    windowsVirtualKeyCode: 27, nativeVirtualKeyCode: 27 });
  await page.send('Input.dispatchKeyEvent', { type: 'keyUp', key: 'Escape', code: 'Escape',
    windowsVirtualKeyCode: 27, nativeVirtualKeyCode: 27 });
  await until("!sourcePlayer.snapshot().state.active && sourcePlayerAudio.snapshot().contextState === 'suspended'",
    'public pause before expensive diagnostic readback');
  evidence.publiclyPaused = await value(page, '(sourcePlayer.observe?.() ?? sourcePlayer.snapshot())');
  evidence.gpu = await value(page, 'sourcePlayer.gpuProof()');
  evidence.gpuReadbackPolicy = 'Actual current post-moving-play pose, publicly paused with Escape before diagnostic GPU readback; no game/source/audio clock setter.';
  await save('current-play-publicly-paused.png', Buffer.from((await page.send('Page.captureScreenshot', { format: 'png' })).data, 'base64'));
  assert(evidence.gpu.shadowPixels > 0 && evidence.gpu.world?.shadowMaterialPixels > 0 &&
    evidence.gpu.world?.metadata.shadows.materialReady === true,
    'Actual original shadow pixels, not just vertices/draw metadata, are required.');
  assert(evidence.snapshots.some(snapshot => snapshot.audioReceiver.signal?.rms > 0 &&
    snapshot.audioReceiver.startedVoices > 0), 'Actual original PCM must reach the running Chrome AudioContext graph.');
  await page.send('Input.dispatchKeyEvent', { type: 'keyDown', key: 'Escape', code: 'Escape',
    windowsVirtualKeyCode: 27, nativeVirtualKeyCode: 27 });
  await page.send('Input.dispatchKeyEvent', { type: 'keyUp', key: 'Escape', code: 'Escape',
    windowsVirtualKeyCode: 27, nativeVirtualKeyCode: 27 });
  await until("sourcePlayer.snapshot().state.active && sourcePlayerAudio.snapshot().contextState === 'running'",
    'actual public context resume');
  evidence.resumed = await value(page, '(sourcePlayer.observe?.() ?? sourcePlayer.snapshot())');
  const neutral = evidence.resumed.state.sourceTick + 3;
  await until(`sourcePlayer.snapshot().state.sourceTick >= ${neutral} &&
    sourcePlayer.snapshot().state.input.buttons.every(value => value === 0)`,
  'actual repeated neutral observations after resume');
  await press('v');
  await until("sourcePlayer.snapshot().state.matchPhase === 'LiveBall' && sourcePlayer.snapshot().state.playKind === 'Normal'",
    'first actual normal physical snap after public resume');
  const snap = await value(page, '(sourcePlayer.observe?.() ?? sourcePlayer.snapshot())');
  evidence.firstPostResumeSnap = snap;
  await until(`sourcePlayer.snapshot().state.sourceTick >= ${snap.state.sourceTick + 240}`,
    '240 genuine source wakes after the post-resume first snap');
  evidence.postResume = await value(page, '(sourcePlayer.observe?.() ?? sourcePlayer.snapshot())');
  assert.equal(evidence.postResume.audioReceiver.failure, null);
  assert.equal(evidence.postResume.audioReceiver.epoch, evidence.beforePublicPause.audioReceiver.epoch);
  assert.equal(evidence.postResume.audioReceiver.clock.anchor, evidence.beforePublicPause.audioReceiver.clock.anchor,
    'A public resume must retain its actual audio epoch/sample origin, never reanchor stale plays.');
  assert(evidence.postResume.audioReceiver.startedVoices > evidence.beforePublicPause.audioReceiver.startedVoices &&
    evidence.postResume.audioReceiver.signal.rms > 0, 'Fresh original sound must actually play after the real resumed snap.');
  evidence.status = 'scoped-original-public-resume-first-snap-audio-gpu-pass';
} catch (error) {
  evidence.status = 'failed'; evidence.error = String(error.stack ?? error);
  if (page) {
    try { evidence.failureState = await value(page, '({status:document.querySelector("#status").textContent, audio:sourcePlayerAudio.snapshot(), source:globalThis.sourcePlayer?.snapshot()})'); }
    catch (failure) { evidence.inspectionError = String(failure); }
    try { await save('failure.png', Buffer.from((await page.send('Page.captureScreenshot', { format: 'png' })).data, 'base64')); }
    catch (failure) { evidence.screenshotError = String(failure); }
  }
  process.exitCode = 1;
} finally {
  evidence.stderr = stderr;
  await save('browser.json', JSON.stringify(evidence, null, 2) + '\n');
  page?.close();
  if (!exited) child.kill('SIGTERM');
  let timer;
  await Promise.race([closed, new Promise(ok => { timer = setTimeout(ok, 5000); })]);
  clearTimeout(timer);
  if (!exited) { child.kill('SIGKILL'); await closed; }
  await runtime.cleanup();
  server.closeAllConnections();
  await new Promise(ok => server.close(ok));
}
console.log(JSON.stringify({ status: evidence.status, error: evidence.error ?? null, evidence: output }));
