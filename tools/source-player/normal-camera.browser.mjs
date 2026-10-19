import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { mkdir, writeFile } from 'node:fs/promises';
import { resolve, relative } from 'node:path';
import { Cdp, pngStats, requireRenderedField } from '../../tests/browser/lib.mjs';
import { sleep, value } from '../../tests/browser/chrome.mjs';
import { CABINET_KEYS } from './wwwroot/input.js';
import { requireOriginalCameraStage, requireStableCameraPause, requireFourHumanCameraCausality } from './normal-camera.assertions.mjs';

const args = process.argv.slice(2);
const option = (name, fallback) => args.includes(name) ? args[args.indexOf(name) + 1] : fallback;
const url = option('--url', 'http://127.0.0.1:8096/?source-fire=portable');
const port = Number(option('--cdp-port', '9222'));
const output = option('--output', '.squad/.scratch/raw-camera-proof/browser');
assert(relative(resolve('.squad/.scratch'), resolve(output)).length > 0 &&
  !relative(resolve('.squad/.scratch'), resolve(output)).startsWith('..'), 'Use a new owned ignored proof directory.');
await mkdir(output, { recursive: false });
const hash = bytes => createHash('sha256').update(bytes).digest('hex');
const version = await (await fetch(`http://127.0.0.1:${port}/json/version`)).json();
const browser = await Cdp.connect(version.webSocketDebuggerUrl);
const evidence = { schema: 'source-raw-camera-natural-browser/v1', status: 'running', url,
  method: 'One owned CDP target in an existing protected browser. Public cabinet keys, Escape/Continue/play selection only. No Core/RNG/camera/cue/position setters, seed override, teleport or injected source state. PNGs are unedited normal gameplay.',
  browserVersion: version.Browser, input: [], frames: [], samples: [], cameraRecords: [], sourceRandom: [],
  controls: [], messages: [], errors: [], networkErrors: [], network: [], wholeGameQualified: false,
  nativeQualified: false, audioQualified: false, performanceQualified: false };
let page, targetId, last, observedTick = -1;
const cameraRecords = new Map(), randomRecords = new Map();
const held = new Set();
async function snapshot() {
  const current = await value(page, 'globalThis.sourcePlayer?.snapshot()');
  if (current?.failed) throw new Error(await value(page, 'document.getElementById("status").textContent'));
  if (current?.state) {
    last = current; observedTick = current.state.sourceTick;
    for (const record of current.state.camera.cameraTrace)
      cameraRecords.set(`${record.sourceTick}/${record.processOrder}/${record.kind}`, record);
    for (const draw of current.state.sourceRandom.tail) randomRecords.set(draw.order, draw);
  }
  return current;
}
function keyDetails(code) {
  if (code.startsWith('Key')) return [code.slice(3).toLowerCase(), code.charCodeAt(3)];
  const special = { Escape: 27, Enter: 13, End: 35, Delete: 46, PageDown: 34,
    ArrowUp: 38, ArrowDown: 40, ArrowLeft: 37, ArrowRight: 39 };
  assert(special[code], 'Known actual cabinet key required: ' + code);
  return [code, special[code]];
}
async function key(code, down) {
  if (held.has(code) === down) return;
  const [name, number] = keyDetails(code);
  evidence.input.push({ sourceTickBeforeEvent: observedTick, code, down, wallMilliseconds: Date.now() });
  await page.send('Input.dispatchKeyEvent', { type: down ? 'keyDown' : 'keyUp', code, key: name,
    windowsVirtualKeyCode: number, nativeVirtualKeyCode: number });
  if (down) held.add(code); else held.delete(code);
}
async function release() { for (const code of [...held]) await key(code, false); }
async function tap(codes, wait = 35) {
  for (const code of codes) await key(code, true);
  await sleep(wait);
  for (const code of codes) await key(code, false);
}
async function until(predicate, label, timeout = 180000) {
  const end = Date.now() + timeout;
  while (Date.now() < end) {
    const current = await snapshot();
    if (current?.state && predicate(current)) return current;
    assert.equal(evidence.errors.length, 0, label + ': no runtime errors');
    await sleep(20);
  }
  throw new Error('Timed out: ' + label);
}
async function capture(name, predicate = () => true) {
  await release(); await tap(['Escape'], 10);
  await until(s => !s.state.active, 'real public Escape pause', 5000);
  await sleep(40);
  const combined = await value(page, '({snapshot:sourcePlayer.snapshot(),gpu:sourcePlayer.gpuProof()})');
  if (!predicate(combined.snapshot.state)) {
    evidence.controls.push({ name: 'missed-branch-not-captured', requested: name,
      sourceTick: combined.snapshot.state.sourceTick });
    await tap(['Escape'], 10); return null;
  }
  requireOriginalCameraStage(combined.snapshot, combined.gpu);
  const screenshot = await page.send('Page.captureScreenshot', { format: 'png' });
  const bytes = Buffer.from(screenshot.data, 'base64'), after = await snapshot();
  const bracket = [combined.snapshot.state.sourceTick, after.state.sourceTick];
  requireStableCameraPause(combined.snapshot.state, after.state, bracket);
  const pixels = pngStats(bytes);
  requireRenderedField(pixels);
  assert.equal(pixels.width, 1024); assert.equal(pixels.height, 768);
  const filename = `${name}-${bracket[0]}.png`;
  await writeFile(`${output}/${filename}`, bytes);
  const record = { name, filename, pngSha256: hash(bytes), pngBytes: bytes.length,
    sourceTickBracket: bracket, pixels, ...combined };
  evidence.frames.push(record);
  if (!evidence.controls.some(c => c.name === 'paused-public-cabinet-cannot-wake-camera')) {
    const before = after.state;
    for (const binding of CABINET_KEYS) { await key(binding[0], true); await key(binding[6], true); }
    await sleep(180);
    const paused = await snapshot();
    requireStableCameraPause(before, paused.state, [before.sourceTick, paused.state.sourceTick]);
    assert(paused.state.input.axes.every(n => n === 0) && paused.state.input.buttons.every(n => n === 0));
    evidence.controls.push({ name: 'paused-public-cabinet-cannot-wake-camera', before, after: paused.state });
    await release();
  }
  await tap(['Escape'], 10);
  await until(s => s.state.active && s.state.sourceTick > after.state.sourceTick,
    'public resume produces a fresh actual source wake', 15000);
  return record;
}

try {
  ({ targetId } = await browser.send('Target.createTarget', { url: 'about:blank', newWindow: false, background: true }));
  evidence.ownedTarget = targetId;
  await writeFile(`${output}/ownership.json`, JSON.stringify({ targetId, cdpPort: port,
    existingBrowserOwned: false, serverUrl: url }, null, 2) + '\n');
  const targets = await (await fetch(`http://127.0.0.1:${port}/json/list`)).json();
  page = await Cdp.connect(targets.find(target => target.id === targetId).webSocketDebuggerUrl);
  page.on('Runtime.consoleAPICalled', event => {
    const text = event.args.map(argument => argument.value ?? argument.description ?? '').join(' ');
    evidence.messages.push(text); if (event.type === 'error') evidence.errors.push(text);
  });
  page.on('Runtime.exceptionThrown', event => evidence.errors.push(event.exceptionDetails));
  page.on('Network.loadingFailed', event => evidence.networkErrors.push(event));
  page.on('Network.responseReceived', event => evidence.network.push({
    path: new URL(event.response.url).pathname, status: event.response.status, mime: event.response.mimeType }));
  await Promise.all([page.send('Page.enable'), page.send('Runtime.enable'), page.send('Network.enable')]);
  await page.send('Emulation.setDeviceMetricsOverride', { width: 1024, height: 768, deviceScaleFactor: 1, mobile: false });
  await page.send('Emulation.setFocusEmulationEnabled', { enabled: true });
  await page.send('Page.navigate', { url });
  const loaded = await until(s => s.state.sourceTick >= 2 && s.state.camera.transientTable === 'csi_kickoff',
    'actual original opening kickoff source stage');
  evidence.build = loaded.build; evidence.device = loaded.device;
  evidence.focus = await value(page, '({hidden:document.hidden,focused:document.hasFocus()})');
  assert.equal(evidence.focus.hidden, false); assert.equal(evidence.focus.focused, true);
  const receipt = Buffer.from(await (await fetch(new URL('build-info.json', url))).arrayBuffer());
  evidence.servedReceiptSha256 = hash(receipt);
  evidence.httpAssemblies = [];
  for (const assembly of loaded.build.assemblies) {
    const response = await fetch(new URL(assembly.path, url)), bytes = Buffer.from(await response.arrayBuffer());
    assert.equal(response.status, 200); assert.equal(bytes.length, assembly.bytes); assert.equal(hash(bytes), assembly.sha256);
    assert.match(response.headers.get('content-type'), /application\/wasm/);
    assert(evidence.network.some(r => r.path.endsWith('/' + assembly.path) && r.status === 200));
    evidence.httpAssemblies.push({ ...assembly, observedSha256: hash(bytes), status: response.status });
  }
  evidence.httpFrontend = [];
  for (const file of ['main.js', 'renderer.js', 'input.js']) {
    const pin = loaded.build.sources.find(source => source.path === `tools/source-player/wwwroot/${file}`);
    const response = await fetch(new URL(file, url)), bytes = Buffer.from(await response.arrayBuffer());
    assert(pin); assert.equal(response.status, 200); assert.equal(hash(bytes), pin.sha256);
    evidence.httpFrontend.push({ file, status: response.status, sha256: hash(bytes) });
  }
  const manifestBytes = Buffer.from(await (await fetch(new URL('data/manifest.json', url))).arrayBuffer());
  assert.equal(hash(manifestBytes), loaded.build.dataManifestSha256);
  const manifest = JSON.parse(manifestBytes);
  assert.equal(manifest.files.length, 557);
  assert.equal(manifest.origins.normalCameraBaseManifestSha256, 'e39f91af2b3624e2eaf364c129a94bb81f75da3b88c3e3d6138371e54a202f89');
  assert(manifest.entrypoints.normalCamera);
  evidence.dataManifestSha256 = hash(manifestBytes);
  const settings = await (await fetch(new URL('data/' + manifest.entrypoints.settings, url))).json();
  assert.deepEqual(settings.stations.map(station => station.id), [0, 1, 2, 3]);
  evidence.configuredStations = settings.stations;
  await capture('opening-original-kickoff', state => state.camera.transientTable === 'csi_kickoff');
  const deadline = Date.now() + Number(option('--seconds', '900')) * 1000;
  let preSnap = false, pass = false, runner = false, impact = null, resumed = false, continuing = false;
  let lastMenu = -1000, lastB = -1000, lastSample = -1, deadPlay = -1, continueAt = -1;
  let menuObservation = null;
  while (Date.now() < deadline && !(preSnap && pass && runner && impact && resumed && continuing)) {
    const current = await snapshot(), state = current.state, camera = state.camera;
    if (state.sourceTick !== lastSample) {
      if (state.sourceTick % 19 === 0 || state.phase === 'PlaySelect' || camera.transient)
        evidence.samples.push({ sourceTick: state.sourceTick, sourceWakes: state.sourceWakes, renderSamples: state.frames,
          phase: state.phase, playNumber: state.playNumber, ball: state.ball, camera,
          controllers: state.actors.filter(actor => actor.controller >= 0).map(actor => actor.controller) });
      lastSample = state.sourceTick;
    }
    if (!preSnap && state.phase === 'PreSnap' && state.playKind === 'Normal' && !camera.transient) {
      preSnap = !!await capture('normal-original-pre-snap-QB',
        s => s.phase === 'PreSnap' && s.playKind === 'Normal' && !s.camera.transient);
      continue;
    }
    if (!pass && state.phase === 'LiveBall' && state.ball.mode === 'InFlight' && state.ball.kind === 'Pass' &&
        state.events.some(event => event.type === 'PassReleased' && event.sourceTick >= state.sourceTick - 85) &&
        !camera.transient) {
      pass = !!await capture('normal-original-raw-pass-target',
        s => s.ball.mode === 'InFlight' && s.ball.kind === 'Pass' && !s.camera.transient);
      continue;
    }
    const qb = state.possession === 'Home' ? 3 : 10;
    if (!runner && state.phase === 'LiveBall' && state.ball.mode === 'Held' && state.ball.carrier >= 0 &&
        state.ball.carrier !== qb && !camera.transient) {
      runner = !!await capture('normal-original-runner-tracking',
        s => s.phase === 'LiveBall' && s.ball.mode === 'Held' && !s.camera.transient);
      continue;
    }
    if (!impact && camera.transient && camera.transientTable !== 'csi_kickoff') {
      const cue = [...cameraRecords.values()].find(record =>
        ['CameraOpcode', 'ContactCameraSelection'].includes(record.kind) && record.table === camera.transientTable);
      if (cue) {
        const frame = await capture('eligible-original-special-impact',
          s => s.camera.transient && s.camera.transientTable !== 'csi_kickoff');
        if (frame) impact = { sourceTick: frame.snapshot.state.sourceTick, playNumber: state.playNumber, cue,
          table: frame.snapshot.state.camera.transientTable, normalWakes: frame.snapshot.state.camera.normalWakes };
      }
      continue;
    }
    if (impact && !resumed && !camera.transient && state.playNumber > impact.playNumber &&
        camera.normalWakes > impact.normalWakes + 1 && state.phase !== 'PlaySelect') {
      resumed = !!await capture('resumed-original-normal-new-play',
        s => !s.camera.transient && s.phase !== 'PlaySelect');
      continue;
    }
    if (!continuing && resumed && state.phase === 'LiveBall') {
      const before = state;
      for (const binding of CABINET_KEYS) { await key(binding[0], true); await key(binding[6], true); }
      const after = (await until(s => s.state.active && s.state.sourceTick >= before.sourceTick + 8 &&
        s.state.input.axes.filter(axis => axis !== 0).length >= 4,
      'four public cabinets reach real progressing Core wakes', 15000)).state;
      evidence.pendingCausalObservation = { before, after };
      requireFourHumanCameraCausality(before, after, [0, 1, 2, 3]);
      evidence.controls.push({ name: 'actual-four-human-public-cabinet-causality', before, after });
      await release();
      continuing = !!await capture('continuing-four-human-original-camera');
      continue;
    }
    if (state.phase === 'PlaySelect') {
      await release();
      if (!menuObservation) menuObservation = { tick: state.sourceTick, normalWakes: camera.normalWakes, cameraTick: camera.sourceTick };
      if (state.sourceTick - menuObservation.tick >= 3) {
        assert.equal(camera.normalWakes, menuObservation.normalWakes);
        assert.equal(camera.sourceTick, menuObservation.cameraTick);
        if (!evidence.controls.some(control => control.name === 'actual-menu-suspends-camera-wakes'))
          evidence.controls.push({ name: 'actual-menu-suspends-camera-wakes', before: menuObservation,
            after: { tick: state.sourceTick, normalWakes: camera.normalWakes, cameraTick: camera.sourceTick } });
      }
      if (state.sourceTick - lastMenu > 18 && state.sourceTick - menuObservation.tick >= 3) {
        lastMenu = state.sourceTick; await tap(CABINET_KEYS.map(binding => binding[5]), 70);
      }
    } else if (state.phase === 'DeadBall') {
      menuObservation = null; await release();
      if (deadPlay !== state.playNumber) {
        deadPlay = state.playNumber; continueAt = state.sourceTick + (camera.transient ? 45 : 22);
      }
      if (state.sourceTick >= continueAt) { await tap(['Enter']); continueAt = state.sourceTick + 90; }
    } else {
      menuObservation = null;
      if (state.phase === 'LiveBall')
        for (const binding of CABINET_KEYS) { await key(binding[0], true); await key(binding[6], true); }
      else await release();
      if (state.playKind === 'Normal' && (state.phase === 'PreSnap' || state.phase === 'LiveBall' &&
          state.ball.carrier === qb) && state.sourceTick - lastB > 22) {
        lastB = state.sourceTick;
        await tap(CABINET_KEYS.map(binding => binding[5]), 45);
      }
      if (state.phase === 'LiveBall' && state.sourceTick % 41 < 2)
        await tap(CABINET_KEYS.map(binding => binding[4]), 25);
    }
    await sleep(15);
  }
  evidence.witness = { preSnap, pass, runner, impact, resumed, continuing };
  assert(preSnap && pass && runner && impact && resumed && continuing,
    'Natural public gameplay must witness kickoff, normal QB/pass/runner, eligible impact and resumed continuing four-human play.');
  assert.equal(evidence.errors.length, 0); assert.equal(evidence.networkErrors.length, 0);
  evidence.final = await snapshot(); evidence.status = 'passed';
} catch (error) {
  evidence.status = 'failed'; evidence.error = error.stack;
  evidence.final = await snapshot().catch(() => last);
  process.exitCode = 1;
} finally {
  evidence.cameraRecords = [...cameraRecords.values()];
  evidence.sourceRandom = [...randomRecords.values()];
  if (page) { await release().catch(() => {}); page.close(); }
  if (targetId) await browser.send('Target.closeTarget', { targetId });
  browser.close(); evidence.ownedTargetClosed = true;
  await writeFile(`${output}/evidence.json`, JSON.stringify(evidence, null, 2) + '\n');
  console.log(JSON.stringify({ status: evidence.status, witness: evidence.witness, error: evidence.error, output }));
}
