import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { mkdir, readFile, writeFile } from 'node:fs/promises';
import { launchChrome, sleep, value } from '../../tests/browser/chrome.mjs';
import { assertBallTrailEvidence } from './ball-trails.assertions.mjs';

const args = process.argv.slice(2);
const option = (name, fallback) => args.includes(name) ? args[args.indexOf(name) + 1] : fallback;
const url = option('--url', 'http://127.0.0.1:8093/?source-fire=portable');
const directory = option('--output', 'tools/source-player/artifacts/ball-trails-attempt1');
const expectedReceipt = option('--receipt-sha256', '');
const seconds = Number(option('--seconds', '60'));
const sha = bytes => createHash('sha256').update(bytes).digest('hex');
const parsed = new URL(url);
assert.equal(parsed.origin, 'http://127.0.0.1:8093', 'Use only the explicitly owned ball-trail preview.');
assert.deepEqual(parsed.searchParams.getAll('source-fire'), ['portable']);
assert(/^tools\/source-player\/artifacts\/ball-trails-[A-Za-z0-9_-]+$/.test(directory));
assert(/^[a-f0-9]{64}$/.test(expectedReceipt), 'Supply the previously verified own HTTP build receipt.');
assert(seconds > 0 && seconds <= 120, 'One bounded public-input attempt, not a policy/reset sweep.');
await mkdir('tools/source-player/artifacts', { recursive: true });
await mkdir(directory);
const evidence = {
  schema: 'original-trail-ball-browser/v1', status: 'running', url, frames: [], input: [],
  errors: [], networkErrors: [], messages: [], assemblyRequests: [],
  method: 'Fresh owned Chrome, actual production C# WASM, public Escape/Enter/cabinet input only. No model/RNG/clock/pose/flag/heap writes, private C# calls, forced scenes, acceleration or reset.',
  screenshotQualification: 'Unmodified gameplay PNGs carry their own before/after source-tick brackets. Public Escape pauses are explicitly recorded.',
  gpuQualification: 'Normal-material and identity passes use the SAME real current original mesh/texture/matrix/depth. Diagnostic framebuffer colors are not substituted gameplay PNGs or native pixel acceptance.',
  nativeUnityOrPixelsQualified: false,
};
const producer = await readFile(new URL(import.meta.url)), assertionsSource = await readFile(new URL('./ball-trails.assertions.mjs', import.meta.url));
await writeFile(`${directory}/producer.browser.mjs`, producer);
await writeFile(`${directory}/producer.assertions.mjs`, assertionsSource);
evidence.producer = { browserSha256: sha(producer), assertionsSha256: sha(assertionsSource),
  argv: process.argv.slice(2), startedUtc: new Date().toISOString() };
let chrome, page, observedTick = -1, paused = false;
const held = new Set();
const keys = { Escape: ['Escape', 27], Enter: ['Enter', 13], b: ['KeyB', 66] };

async function snapshot() {
  const result = await value(page, 'globalThis.sourcePlayer?.snapshot()');
  if (result?.failed) throw new Error(await value(page, 'document.getElementById("status").textContent'));
  if (result?.state) observedTick = result.state.sourceTick;
  return result;
}
async function key(name, down) {
  const [code, number] = keys[name];
  evidence.input.push({ name, down, observedSourceTick: observedTick, wallTime: Date.now() });
  await page.send('Input.dispatchKeyEvent', { type: down ? 'keyDown' : 'keyUp', key: name, code,
    windowsVirtualKeyCode: number, nativeVirtualKeyCode: number });
  if (down) held.add(name); else held.delete(name);
}
async function tap(name) {
  await key(name, true);
  try { await sleep(12); } finally { await key(name, false); }
  await sleep(35);
  if (name === 'Escape') paused = !paused;
}
function validate(pair) {
  const state = pair.snapshot.state, gpu = pair.gpu;
  assert.equal(state.sourceTick, gpu.sourceTick);
  assert(state.effects.enabled && pair.snapshot.build.dataManifestSha256 ===
    'ff2851555e6f8b01814b4a29dd20f8194761662debe8e6cb99db789dc5349f0d');
  const visible = state.ballTrails.filter(t => t.visible);
  assert.equal(gpu.ballTrailDraws.length, visible.length);
  assert.equal(gpu.ballTrailMaterialDraws.length, visible.length);
  for (const draw of gpu.ballTrailMaterialDraws) {
    const trail = visible.find(t => t.objectSerial === draw.objectSerial);
    assert(trail);
    assert.equal(draw.mesh, 'original/free-ball/limb_medball_obj_big');
    assert.equal(draw.indices, 144);
    assert.equal(draw.texture, 'teams/runtime/HEDSHU2.WMS');
    assert.equal(draw.textureSha256, 'b59af47bf7baf6a2bcffacd4fb68af206229099102c00baf608d771f4ac5836e');
    assert.deepEqual(draw.gpuModel, Array.from(Float32Array.from(trail.packet.slice(4))));
    assert.equal(draw.gpuConstantAlphaValue, Math.fround(trail.effectiveAlpha / 255));
    assert(Boolean(draw.gpuConstantAlpha) && Boolean(draw.gpuTextureTimesAlpha) && draw.gpuOriginalTextureBound);
    assert.equal(draw.gpuWhiteAdd, trail.white ? 1 : 0);
    assert(draw.gpuDepthTest && draw.gpuDepthWrite && draw.gpuDepthFunction === 'LESS');
    assert(draw.gpuCullEnabled && draw.gpuCullFace === 'BACK' && draw.gpuFrontFace === 'CCW');
    assert(draw.gpuBlendEnabled && draw.gpuSourceAlphaBlend, 'Actual normal draw must use SRC_ALPHA/ONE_MINUS and ONE/ZERO.');
  }
}
async function capture(name) {
  const pair = await value(page, '({snapshot:sourcePlayer.snapshot(),gpu:sourcePlayer.gpuProof()})');
  validate(pair);
  const image = await page.send('Page.captureScreenshot', { format: 'png' });
  const png = Buffer.from(image.data, 'base64');
  await writeFile(`${directory}/${name}.png`, png);
  const after = await snapshot();
  const frame = { name, ...pair, publicPauseActive: paused, pngSha256: sha(png), pngBytes: png.length,
    screenshotSourceTickBracket: [pair.snapshot.state.sourceTick, after.state.sourceTick] };
  evidence.frames.push(frame);
  return frame;
}

try {
  // Verify served identity BEFORE launching a new browser; publication replaces
  // AppBundle's inode, so an older container mount is not assumed valid.
  const response = await fetch(new URL('build-info.json', url));
  assert.equal(response.status, 200);
  const receipt = Buffer.from(await response.arrayBuffer());
  assert.equal(sha(receipt), expectedReceipt);
  evidence.receiptSha256 = sha(receipt);
  evidence.build = JSON.parse(receipt);
  await writeFile(`${directory}/http-build-info.json`, receipt);
  chrome = await launchChrome(directory); page = chrome.cdp;
  evidence.chrome = { version: chrome.version, launch: chrome.launch };
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
  await page.send('Page.addScriptToEvaluateOnNewDocument', { source: `(() => {
    const native = WebGL2RenderingContext.prototype.createProgram, programs = [];
    Object.defineProperty(WebGL2RenderingContext.prototype, 'createProgram', {
      configurable: true, writable: true, value: function(...args) {
        const result = Reflect.apply(native, this, args);
        programs.push({ gl: this, program: result }); return result;
      }
    });
    Object.defineProperty(globalThis, 'sourceBallTrailNativePrograms', { value: Object.freeze({
      read: () => programs.map(({gl,program},ordinal) => ({
        ordinal, linked: gl.getProgramParameter(program,gl.LINK_STATUS),
        shaders: gl.getAttachedShaders(program).map(shader => ({
          type: gl.getShaderParameter(shader,gl.SHADER_TYPE),
          compiled: gl.getShaderParameter(shader,gl.COMPILE_STATUS),
          source: gl.getShaderSource(shader)
        }))
      }))
    }), configurable: false, writable: false });
  })();` });
  await page.send('Emulation.setDeviceMetricsOverride', { width: 1280, height: 1000, deviceScaleFactor: 1, mobile: false });
  await page.send('Page.navigate', { url });
  await page.send('Page.bringToFront');
  let loaded;
  const loadingDeadline = Date.now() + 180000;
  while (Date.now() < loadingDeadline) {
    await sleep(150); loaded = await snapshot();
    if (!loaded && await value(page, 'globalThis.sourcePlayerLoader?.failed'))
      throw new Error(await value(page, 'document.getElementById("status").textContent'));
    if (loaded?.ready) break;
  }
  assert(loaded?.ready, 'The actual owned C# candidate must earn SOURCE_PLAYER_READY.');
  evidence.device = loaded.device;
  evidence.httpAssemblies = [];
  for (const assembly of evidence.build.assemblies) {
    const response = await fetch(new URL(assembly.path, url)), bytes = Buffer.from(await response.arrayBuffer());
    assert.equal(response.status, 200); assert.equal(bytes.length, assembly.bytes); assert.equal(sha(bytes), assembly.sha256);
    assert.match(response.headers.get('content-type'), /application\/wasm/);
    assert(evidence.assemblyRequests.some(r => r.status === 200 && r.url.endsWith('/' + assembly.path)));
    evidence.httpAssemblies.push({ ...assembly, observedSha256: sha(bytes) });
  }
  const programs = await value(page, 'sourceBallTrailNativePrograms.read()');
  assert.equal(programs.length, 3, 'Only the existing scene/HUD/fire programs may be created.');
  assert(programs.every(p => p.linked && p.shaders.every(s => s.compiled)));
  const actualScene = programs[0].shaders.find(s => s.source.includes('uniform float constantAlphaValue;'));
  assert(actualScene?.source.includes('if(constantAlpha) color.a=constantAlphaValue;') &&
    actualScene.source.includes('if(textureTimesAlpha) color.rgb*=color.a;'));
  evidence.nativeProgramJournal = programs;
  await capture('ready-original-four-human');
  const deadline = Date.now() + seconds * 1000;
  let natural;
  while (Date.now() < deadline) {
    const current = await snapshot(), s = current.state;
    if (s.phase === 'LiveBall' && s.ball.mode === 'InFlight' && s.ball.position[1] > 10 &&
        ['BallTrail', 'PlayerCloud'].includes(s.effects.selectedBallEffect) &&
        s.ballTrails.filter(t => t.visible).length >= 5) { natural = current; break; }
    if (s.phase === 'PlaySelect' && s.sourceTick % 20 < 5) await tap('b');
    else await sleep(12);
  }
  assert(natural, 'A real naturally produced non-fire flight with original ghosts must be reachable without state injection.');
  evidence.pauseRequestedAt = natural.state.sourceTick;
  await tap('Escape');
  let pausedFrame;
  for (let i = 0; i < 50; i++) {
    pausedFrame = await snapshot();
    if (!pausedFrame.state.active) break;
    await sleep(20);
  }
  assert(!pausedFrame.state.active);
  const first = await capture('natural-nonfire-ghosts-public-pause');
  assert.equal(first.snapshot.state.phase, 'LiveBall');
  assert.equal(first.snapshot.state.ball.mode, 'InFlight');
  assert(first.gpu.ballTrailPixels > 0 && first.gpu.ballTrailColoredPixels > 0 && first.gpu.ballTrailNonWhitePixels > 0,
    'Actual normal-material ghost pixels must be visible, colored and not a false-white/blank image.');
  assert(first.snapshot.state.ballTrails.every(t => !t.white));
  const target = first.snapshot.state.ballTrails.find(t => t.stage === 0 && t.visible);
  assert(target, 'Track an actual newly entered source football ghost, not a synthetic snapshot.');
  evidence.target = target;
  const heldTick = first.snapshot.state.sourceTick;
  await sleep(120);
  assert.equal((await snapshot()).state.sourceTick, heldTick, 'Public Pause must hold the natural frame; no source-clock writes.');
  await page.send('Runtime.evaluate', { expression: `(() => {
    const start = sourcePlayer.snapshot().state.sourceTick, began = performance.now(), rows = [];
    let last = -1, done = false;
    Object.defineProperty(globalThis, 'sourceBallTrailObserver', { configurable:false,writable:false,
      value:Object.freeze({read:()=>structuredClone({done,rows})}) });
    function observe() {
      const snapshot = sourcePlayer.snapshot(), tick = snapshot.state.sourceTick;
      if (tick !== last) {
        rows.push({snapshot}); last=tick;
      }
      done = tick >= start+12 || performance.now()-began > 15000 || snapshot.failed;
      if (!done) requestAnimationFrame(observe);
    }
    observe();
  })();`, returnByValue: true });
  await tap('Escape');
  let observations;
  while (Date.now() < deadline) {
    observations = await value(page, 'sourceBallTrailObserver.read()');
    if (observations.done) break;
    await sleep(35);
  }
  assert(observations?.done, 'The bounded read-only source observer must finish after real continuing source wakes.');
  evidence.observations = observations;
  evidence.observerMethod = 'Read-only completed-frame snapshots only, without extra per-frame GPU passes. Exact source-wake stages/sleeps/deletion come from the existing actual C# record stream, not an assumption that every57Hz wake is a rendered frame.';
  const targetStages = new Map();
  const records = new Map();
  let changedMain = false, deleted = false;
  for (const pair of [first, ...observations.rows]) {
    const s = pair.snapshot.state, t = s.ballTrails.find(t => t.objectSerial === target.objectSerial);
    for (const record of s.effects.records) {
      if (record.ProcessSerial !== target.processSerial) continue;
      records.set(`${record.kind}/${record.sourceTick}/${record.ObjectSerial}`, record);
    }
    if (t) {
      assert.deepEqual(t.entry, target.entry);
      assert.deepEqual(t.position, target.position);
      assert.equal(t.heading, target.heading); assert.equal(t.elevation, target.elevation); assert.equal(t.roll, target.roll);
      targetStages.set(t.stage, t.alpha);
      changedMain ||= s.ball.position.some((v, i) => v !== first.snapshot.state.ball.position[i]);
    } else if (s.sourceTick >= target.entry.sourceTick + 5) {
      deleted = true;
      assert(!s.ballTrails.some(d => d.objectSerial === target.objectSerial));
    }
  }
  const all = [...records.values()];
  evidence.observedRenderStages = [...targetStages.entries()].sort((a, b) => a[0] - b[0]);
  evidence.lifecycleRecords = {
    birth: all.find(r => r.kind === 'CreateProcess'),
    stages: all.filter(r => r.kind === 'BallTrailStage').sort((a, b) => a.sourceTick - b.sourceTick),
    sleeps: all.filter(r => r.kind === 'Sleep').sort((a, b) => a.sourceTick - b.sourceTick),
    deletion: all.find(r => r.kind === 'DeleteObject'),
    returned: all.find(r => r.kind === 'ReturnDie'),
  };
  assert.deepEqual(evidence.lifecycleRecords.stages.map(r => r.value), [215, 200, 185, 170, 155],
    'Every original source-wake stage must exist for the same natural object, even when a render frame skips a wake.');
  assert(changedMain && deleted, 'The original copied ghost must stay static while the real ball moves, then disappear.');
  await capture('after-original-ghost-deletion');
  let continuing;
  while (Date.now() < deadline) {
    continuing = await snapshot();
    if (continuing.state.sourceTick >= heldTick + 120) break;
    await sleep(25);
  }
  assert(continuing.state.sourceTick >= heldTick + 120);
  assert(continuing.state.actors.some((a, i) => a.poseDigest !== first.snapshot.state.actors[i].poseDigest),
    'Actual gameplay poses must continue after the ghost witness.');
  await capture('continuing-real-gameplay');
  evidence.final = await snapshot();
  evidence.finalProgramCount = (await value(page, 'sourceBallTrailNativePrograms.read()')).length;
  assert.equal(evidence.finalProgramCount, 3);
  assert.equal(evidence.final.state.epoch, 0, 'No intermediate/public resets may conceal a failure.');
  assert.equal(evidence.errors.length, 0); assert.equal(evidence.networkErrors.length, 0);
  evidence.witnessed = { naturalNonFireFlight: true, originalGhostMeshAndDecal: true, fiveStageSameObject: true,
    staticEntryPoseWhileMainMoves: changedMain, deletion: deleted, publicPauseHeld: true,
    continuedSourceTicks: evidence.final.state.sourceTick - heldTick, noReset: true,
    fiveStageBasis: 'Exact source-wake records for one natural object; all five GPU material stages coexist in its real paused natural chain. Completed-render stages are listed separately.' };
  evidence.status = 'qualified-portable-original-football-ghosts-NOT-native-parity';
  assertBallTrailEvidence(evidence);
} catch (error) {
  evidence.status = 'failed-or-bounded-unmet'; evidence.error = String(error.stack || error); process.exitCode = 1;
  if (page) {
    try { evidence.final = await value(page, 'globalThis.sourcePlayer?.snapshot()'); } catch {}
    try { await capture('failure'); } catch (captureError) { evidence.failureCaptureError = String(captureError); }
  }
} finally {
  for (const name of [...held]) { try { await key(name, false); } catch {} }
  await writeFile(`${directory}/evidence.json`, JSON.stringify(evidence, null, 2) + '\n');
  if (chrome) await chrome.close();
  console.log(JSON.stringify({ status: evidence.status, witnessed: evidence.witnessed,
    receiptSha256: evidence.receiptSha256, target: evidence.target?.objectSerial,
    sourceTick: evidence.final?.state?.sourceTick, error: evidence.error }, null, 2));
}
