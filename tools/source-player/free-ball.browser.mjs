import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { mkdir, writeFile } from 'node:fs/promises';
import { launchChrome, sleep, value } from '../../tests/browser/chrome.mjs';

const args = process.argv.slice(2);
const option = (name, fallback) => args.includes(name) ? args[args.indexOf(name) + 1] : fallback;
const url = option('--url', 'http://127.0.0.1:8085/');
const directory = option('--output', 'tools/source-player/artifacts/free-ball-attempt1');
const seconds = Number(option('--seconds', '90'));
assert(new URL(url).origin === 'http://127.0.0.1:8085', 'Use only the distinct owned candidate.');
assert(seconds > 0 && seconds <= 180, 'This is one bounded public-input attempt, not a policy sweep.');
await mkdir(directory, { recursive: true });
const evidence = { schema: 'original-free-ball-browser/v1', url, status: 'running', frames: [], input: [],
  errors: [], networkErrors: [], messages: [], assemblyRequests: [],
  method: 'Fresh owned Chrome and public cabinet events only; actual C# WASM and GPU bindings; no actor/ball/pose/clock/RNG/flags forcing.',
  screenshotQualification: 'Unmodified gameplay PNGs are bracketed by source ticks, never relabelled same-tick pixels.',
  nativeUnityOrPixelsQualified: false };
const chrome = await launchChrome(directory), page = chrome.cdp;
let observedSourceTick = -1;
const heldKeys = new Set();
const keys = { b: ['KeyB', 66], n: ['KeyN', 78], w: ['KeyW', 87], Enter: ['Enter', 13] };
const sha = bytes => createHash('sha256').update(bytes).digest('hex');
async function snapshot() {
  const result = await value(page, 'globalThis.sourcePlayer?.snapshot()');
  if (result?.failed) throw new Error(await value(page, 'document.getElementById("status").textContent'));
  if (result?.state) observedSourceTick = result.state.sourceTick;
  return result;
}
async function key(name, down) {
  const [code, number] = keys[name];
  evidence.input.push({ name, down, observedSourceTick, at: Date.now() });
  await page.send('Input.dispatchKeyEvent', { type: down ? 'keyDown' : 'keyUp', key: name, code,
    windowsVirtualKeyCode: number, nativeVirtualKeyCode: number });
  if (down) heldKeys.add(name); else heldKeys.delete(name);
}
async function tap(name, hold = 65) {
  await key(name, true);
  try { await sleep(hold); } finally { await key(name, false); }
  await sleep(70);
}
async function capture(name) {
  // One synchronous browser task pairs the immutable C# state with actual GPU uniforms.
  const paired = await value(page, '({snapshot:sourcePlayer.snapshot(),gpu:sourcePlayer.gpuProof()})');
  assert.equal(paired.snapshot.state.sourceTick, paired.gpu.sourceTick);
  if (paired.gpu.ballDraw) {
    assert.equal(paired.gpu.ballDraw.sourceTick, paired.snapshot.state.sourceTick);
    assert.deepEqual(paired.gpu.ballDraw.gpuModel, Array.from(Float32Array.from(paired.snapshot.state.ball.packet.slice(4))));
    assert(paired.gpu.ballDraw.gpuOriginalTextureBound);
  }
  const image = await page.send('Page.captureScreenshot', { format: 'png' });
  const png = Buffer.from(image.data, 'base64');
  await writeFile(`${directory}/${name}.png`, png);
  const after = await snapshot();
  evidence.frames.push({ name, ...paired, pngBytes: png.length, pngSha256: sha(png),
    screenshotSourceTickBracket: [paired.snapshot.state.sourceTick, after.state.sourceTick] });
  return paired;
}
try {
  page.on('Runtime.consoleAPICalled', event => {
    const text = event.args.map(a => a.value ?? a.description ?? '').join(' ');
    evidence.messages.push(text); if (event.type === 'error') evidence.errors.push(text);
  });
  page.on('Runtime.exceptionThrown', event => evidence.errors.push(event.exceptionDetails));
  page.on('Network.loadingFailed', event => evidence.networkErrors.push(event));
  page.on('Network.responseReceived', event => {
    if (/SourcePlayer\..*\.wasm$/.test(new URL(event.response.url).pathname))
      evidence.assemblyRequests.push({ url: event.response.url, status: event.response.status, mime: event.response.mimeType });
  });
  await page.send('Emulation.setDeviceMetricsOverride', { width: 1280, height: 1000, deviceScaleFactor: 1, mobile: false });
  await page.send('Page.navigate', { url }); await page.send('Page.bringToFront');
  let loaded;
  const loadDeadline = Date.now() + 180000;
  while (Date.now() < loadDeadline) {
    await sleep(200); loaded = await snapshot();
    if (!loaded && await value(page, 'globalThis.sourcePlayerLoader?.failed'))
      throw new Error(await value(page, 'document.getElementById("status").textContent'));
    if (loaded?.ready) break;
  }
  assert(loaded?.ready, 'The actual new C# build must earn SOURCE_PLAYER_READY.');
  evidence.device = loaded.device; evidence.build = loaded.build;
  const receipt = Buffer.from(await (await fetch(new URL('build-info.json', url))).arrayBuffer());
  evidence.receiptSha256 = sha(receipt);
  assert.notEqual(evidence.receiptSha256, 'c23092d9bcdae85142867e9a397c9cb67253dbe291611d2fce7948eb365bc9cc');
  evidence.httpAssemblies = [];
  for (const assembly of loaded.build.assemblies) {
    const response = await fetch(new URL(assembly.path, url)), bytes = Buffer.from(await response.arrayBuffer());
    assert.equal(response.status, 200); assert.equal(bytes.length, assembly.bytes); assert.equal(sha(bytes), assembly.sha256);
    assert.match(response.headers.get('content-type'), /application\/wasm/);
    assert(evidence.assemblyRequests.some(r => r.url.endsWith('/' + assembly.path) && r.status === 200));
    evidence.httpAssemblies.push({ ...assembly, observedSha256: sha(bytes), status: response.status });
  }
  await capture('ready-original-teams');
  const deadline = Date.now() + seconds * 1000;
  const witnessed = { flight: false, changingSourceOrientation: false, retainedHeldBall: false };
  evidence.witnessed = witnessed;
  evidence.strategy = 'Observe the unmodified initial source kickoff; thereafter only existing public menu B/snap B/Continue and legal pass B. No restart or simulation-state injection.';
  let firstFlight, lastMenu = -100, ownSnap = -1, passSent = false, resultPlay = -1, resultEntry = -1;
  while (Date.now() < deadline && !Object.values(witnessed).every(Boolean)) {
    const current = await snapshot(), s = current.state;
    if (s.phase === 'LiveBall' && s.ball.mode === 'InFlight' && s.ball.visible && s.ball.position[1] > 3 &&
        (!firstFlight || s.sourceTick >= firstFlight.snapshot.state.sourceTick + 5) &&
        (!witnessed.flight || !witnessed.changingSourceOrientation)) {
      const frame = await capture(!witnessed.flight ? 'actual-free-ball-flight' : 'later-source-flight-orientation');
      if (frame.gpu.ballPixels > 0 && frame.snapshot.state.ball.visible && frame.snapshot.state.ball.mode === 'InFlight') {
        if (!firstFlight) { firstFlight = frame; witnessed.flight = true; }
        else if (frame.snapshot.state.sourceTick > firstFlight.snapshot.state.sourceTick &&
            frame.gpu.ballDraw.matrix.slice(0, 12).some((v, i) => v !== firstFlight.gpu.ballDraw.matrix[i])) {
          witnessed.changingSourceOrientation = true;
        }
      }
    }
    if (!witnessed.retainedHeldBall && !s.ball.visible && s.ball.carrier >= 0 &&
        ['PreSnap', 'LiveBall'].includes(s.phase)) {
      const frame = await capture('retained-original-held-ball');
      const carrier = frame.snapshot.state.ball.carrier;
      witnessed.retainedHeldBall = carrier >= 0 && !frame.snapshot.state.ball.visible &&
        frame.gpu.ballDraw === null && frame.gpu.ballPixels === 0 &&
        (frame.snapshot.state.actors[carrier].model & 12) !== 0 && frame.gpu.actorPixels[carrier] > 0;
    }
    if (Object.values(witnessed).every(Boolean)) break;
    if (s.phase === 'DeadBall') {
      if (resultPlay !== s.playNumber) { resultPlay = s.playNumber; resultEntry = s.sourceTick; }
      if (s.sourceTick - resultEntry >= 48) await tap('Enter');
    } else if (s.phase === 'PlaySelect' && s.sourceTick - lastMenu > 30) {
      lastMenu = s.sourceTick; passSent = false;
      await sleep(450); await tap('b');
    } else if (s.phase === 'PreSnap' && s.playKind === 'Normal' && s.possession === 'Home' && ownSnap !== s.playNumber) {
      ownSnap = s.playNumber; await sleep(1000); await tap('b'); passSent = false;
    } else if (s.phase === 'LiveBall' && s.possession === 'Home' && s.playKind === 'Normal' &&
        !passSent && s.ball.carrier === 3) {
      await tap('b', 45); passSent = true;
    } else await sleep(45);
  }
  evidence.final = await snapshot();
  assert(Object.values(witnessed).every(Boolean), 'Bounded public-input flight/spin/retained-held witnesses were not all achieved.');
  assert.equal(evidence.errors.length, 0); assert.equal(evidence.networkErrors.length, 0);
  assert.equal(evidence.final.state.epoch, 0, 'No resets or retries may substitute for a genuine flight witness.');
  evidence.status = 'qualified-source-free-ball-witnesses-NOT-native-parity';
} catch (error) {
  evidence.status = 'failed-or-bounded-unmet'; evidence.error = String(error.stack || error); process.exitCode = 1;
  try { evidence.final = await value(page, 'globalThis.sourcePlayer?.snapshot()'); await capture('failure'); } catch {}
} finally {
  for (const name of [...heldKeys]) { try { await key(name, false); } catch {} }
  await writeFile(`${directory}/evidence.json`, JSON.stringify(evidence, null, 2) + '\n');
  await chrome.close();
  console.log(JSON.stringify({ status: evidence.status, witnessed: evidence.witnessed,
    sourceTick: evidence.final?.state?.sourceTick, receiptSha256: evidence.receiptSha256,
    device: evidence.device, error: evidence.error }, null, 2));
}
