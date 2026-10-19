import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { mkdir, writeFile } from 'node:fs/promises';
import { launchChrome, sleep, value } from '../../tests/browser/chrome.mjs';
import { CABINET_KEYS } from './wwwroot/input.js';

const args = process.argv.slice(2);
const option = (key, fallback) => args.includes(key) ? args[args.indexOf(key) + 1] : fallback;
const url = option('--url', 'http://127.0.0.1:8094/?source-fire=portable');
const output = option('--output', '.squad/.scratch/impact-dust-proof/browser');
const seconds = Number(option('--seconds', '150'));
await mkdir(output, { recursive: false });
const evidence = { schema: 'source-impact-dust-natural-browser/v1', status: 'running', url,
  method: 'Fresh owned browser, public four-cabinet key events only. Escape pauses naturally emitted DIRT for stable normal-render PNGs, then public resume/Continue. No state, RNG, cue, pose or particle injection.',
  input: [], samples: [], frames: [], errors: [], messages: [], networkErrors: [], assemblyRequests: [],
  wholeGameQualified: false, nativeQualified: false, audioQualified: false, performanceQualified: false };
const chrome = await launchChrome(output), page = chrome.cdp;
evidence.browser = chrome.version; evidence.launch = chrome.launch;
let observedTick = -1;
const hash = bytes => createHash('sha256').update(bytes).digest('hex');
async function snapshot() {
  const result = await value(page, 'globalThis.sourcePlayer?.snapshot()');
  if (result?.failed) throw new Error(await value(page, 'document.getElementById("status").textContent'));
  if (result?.state) observedTick = result.state.sourceTick;
  return result;
}
async function key(code, down) {
  const details = code.startsWith('Key') ? [code.slice(3).toLowerCase(), code.charCodeAt(3)] :
    code === 'End' ? ['End', 35] : code === 'Escape' ? ['Escape', 27] : ['Enter', 13];
  evidence.input.push({ sourceTickBeforeEvent: observedTick, code, down, wallMilliseconds: Date.now() });
  await page.send('Input.dispatchKeyEvent', { type: down ? 'keyDown' : 'keyUp', code, key: details[0],
    windowsVirtualKeyCode: details[1], nativeVirtualKeyCode: details[1] });
}
async function tap(codes, milliseconds = 60) {
  for (const code of codes) await key(code, true);
  await sleep(milliseconds);
  for (const code of codes) await key(code, false);
}
async function capture(name, dustRequired = false) {
  const proof = await value(page, '({snapshot:sourcePlayer.snapshot(),gpu:sourcePlayer.gpuProof()})');
  const image = await page.send('Page.captureScreenshot', { format: 'png' });
  const png = Buffer.from(image.data, 'base64');
  await writeFile(`${output}/${name}.png`, png);
  const after = await snapshot();
  const frame = { name, ...proof, pngSha256: hash(png), pngBytes: png.length,
    screenshotSourceTickBracket: [proof.snapshot.state.sourceTick, after.state.sourceTick] };
  evidence.frames.push(frame);
  if (dustRequired) {
    assert(proof.gpu.dustPixels > 0, 'The same real DIRT objects/textures/alpha must produce dust-only GPU pixels.');
    assert.equal(frame.screenshotSourceTickBracket[0], frame.screenshotSourceTickBracket[1],
      'A public paused normal-render dust screenshot must have a stable genuine source tick.');
    assert(proof.snapshot.state.effects.dustDraws > 0);
  }
  return frame;
}
try {
  page.on('Runtime.consoleAPICalled', event => {
    const text = event.args.map(arg => arg.value ?? arg.description ?? '').join(' ');
    evidence.messages.push(text); if (event.type === 'error') evidence.errors.push(text);
  });
  page.on('Runtime.exceptionThrown', event => evidence.errors.push(event.exceptionDetails));
  page.on('Network.loadingFailed', event => evidence.networkErrors.push(event));
  page.on('Network.responseReceived', event => {
    if (/SourcePlayer\..*\.wasm$/.test(new URL(event.response.url).pathname))
      evidence.assemblyRequests.push({ url: event.response.url, status: event.response.status, mime: event.response.mimeType });
  });
  await page.send('Emulation.setDeviceMetricsOverride', { width: 1024, height: 768, deviceScaleFactor: 1, mobile: false });
  await page.send('Page.navigate', { url }); await page.send('Page.bringToFront');
  let loaded;
  const until = Date.now() + 180000;
  while (Date.now() < until) {
    await sleep(100); loaded = await snapshot();
    if (loaded?.ready) break;
    if (await value(page, 'globalThis.sourcePlayerLoader?.failed'))
      throw new Error(await value(page, 'document.getElementById("status").textContent'));
  }
  assert(loaded?.ready, 'Actual new C# WASM must reach SOURCE_PLAYER_READY.');
  evidence.build = loaded.build; evidence.device = loaded.device;
  const receipt = Buffer.from(await (await fetch(new URL('build-info.json', url))).arrayBuffer());
  evidence.receiptSha256 = hash(receipt);
  evidence.httpAssemblies = [];
  for (const assembly of loaded.build.assemblies) {
    const response = await fetch(new URL(assembly.path, url)), bytes = Buffer.from(await response.arrayBuffer());
    assert.equal(response.status, 200); assert.equal(bytes.length, assembly.bytes); assert.equal(hash(bytes), assembly.sha256);
    assert.match(response.headers.get('content-type'), /application\/wasm/);
    assert(evidence.assemblyRequests.some(r => r.url.endsWith('/' + assembly.path) && r.status === 200));
    evidence.httpAssemblies.push({ ...assembly, observedSha256: hash(bytes), httpStatus: response.status });
  }
  evidence.httpFrontend = [];
  for (const name of ['main.js', 'renderer.js', 'input.js']) {
    const expected = loaded.build.sources.find(s => s.path === `tools/source-player/wwwroot/${name}`);
    assert(expected, 'The actual build receipt must bind every delivered effect renderer/input module.');
    const response = await fetch(new URL(name, url)), bytes = Buffer.from(await response.arrayBuffer());
    assert.equal(response.status, 200); assert.equal(hash(bytes), expected.sha256);
    evidence.httpFrontend.push({ name, httpStatus: response.status, sha256: hash(bytes) });
  }
  const manifest = await (await fetch(new URL('data/manifest.json', url))).arrayBuffer();
  assert.equal(hash(Buffer.from(manifest)), loaded.build.dataManifestSha256);
  evidence.dataManifestSha256 = hash(Buffer.from(manifest));
  const settingsManifest = JSON.parse(Buffer.from(manifest));
  const settings = await (await fetch(new URL('data/' + settingsManifest.entrypoints.settings, url))).json();
  assert.deepEqual(settings.stations.map(s => s.id), [0, 1, 2, 3]);
  evidence.actualConfiguredStations = settings.stations;
  await capture('ready-original-four-human');
  const deadline = Date.now() + seconds * 1000;
  let firstDustTick = -1, pausedStages = 0, lastPhaseTap = -1000, lastSampleTick = -1;
  let continueAt = -1, resultPlay = -1;
  const dirtSerials = new Set(), deleted = new Set(), textureFrames = new Set();
  while (Date.now() < deadline) {
    const current = await snapshot(), state = current.state, fx = state.effects;
    if (state.sourceTick !== lastSampleTick) {
      evidence.samples.push({ sourceTick: state.sourceTick, sourceWakes: state.sourceWakes, renderSamples: state.frames,
        phase: state.phase, playNumber: state.playNumber, visualCallbacks: state.visualCallbacks,
        dustObjects: fx.dustObjects, dustDraws: fx.dustDraws, recordCounts: fx.recordCounts,
        dustProcessRecords: fx.records.filter(r => ['dust_cloud_proc', 'dust_cloud_die_alert', 'dust_cloud'].includes(r.function)) });
      lastSampleTick = state.sourceTick;
    }
    for (const object of fx.dustObjects ?? []) {
      dirtSerials.add(object.ObjectSerial); textureFrames.add(object.TextureFrame);
    }
    for (const record of fx.records) {
      if (record.kind === 'DeleteObject' && record.function === 'dust_cloud_proc' ||
          record.kind === 'ExitAlertDeleteObject' && record.function === 'dust_cloud_die_alert')
        deleted.add(record.ObjectSerial);
    }
    if (fx.dustDraws > 0 && (pausedStages === 0 || pausedStages === 1 && state.sourceTick >= firstDustTick + 4)) {
      await tap(['Escape'], 10); await sleep(50);
      const paused = await snapshot();
      assert.equal(paused.state.active, false, 'Public Escape must pause the actual source host.');
      if (paused.state.effects.dustDraws > 0) {
        const frame = await capture(`natural-original-dust-stage-${pausedStages}`, true);
        if (firstDustTick < 0) firstDustTick = frame.snapshot.state.sourceTick;
        pausedStages++;
      }
      await tap(['Escape'], 10); await sleep(20);
      continue;
    }
    if (pausedStages >= 2 && deleted.size > 0 && state.sourceTick >= firstDustTick + 180) {
      assert.equal(state.actorCount, 14);
      assert.equal(state.visualCallbacks.dust, state.visualCallbacks.boundDust);
      assert(state.visualCallbacks.boundDust > 0 && state.visualCallbacks.unqualified === 0);
      assert(state.visualCallbacks.cues.some(c => c.FunctionSymbol === 'dust_cloud' &&
        (c.path === 'plyrseq.c' || c.path === 'player.c')), 'Natural original callback identity must be retained.');
      assert.equal(new Set(state.actors.filter(a => a.controller >= 0).map(a => a.controller)).size, 4);
      await capture('continuing-four-human-after-natural-dust-cleanup');
      evidence.final = await snapshot();
      evidence.witness = { naturalDirtObjects: [...dirtSerials], deletedObjects: [...deleted],
        sampledTextureFrames: [...textureFrames], firstDustTick, pausedStages, continuedSourceTick: state.sourceTick,
        sourceWakesDistinctFromRenderSamples: state.sourceWakes !== state.frames };
      evidence.status = 'passed';
      break;
    }
    if (state.phase === 'PlaySelect' && state.sourceTick - lastPhaseTap > 8) {
      lastPhaseTap = state.sourceTick;
      await tap(CABINET_KEYS.map(binding => binding[5]), 80);
    } else if (state.phase === 'DeadBall') {
      if (resultPlay !== state.playNumber) { resultPlay = state.playNumber; continueAt = state.sourceTick + 48; }
      if (state.sourceTick >= continueAt) { await tap(['Enter']); continueAt = state.sourceTick + 90; }
    }
    await sleep(12);
  }
  assert.equal(evidence.status, 'passed', 'Natural four-human dust stages, real deletion and continuing play must all be witnessed within the bounded campaign.');
  assert.equal(evidence.errors.length, 0, 'No browser/WASM failure may be hidden by the dust witness.');
} catch (error) {
  evidence.status = 'failed'; evidence.error = error.stack; evidence.final = await snapshot().catch(() => null);
  process.exitCode = 1;
} finally {
  await chrome.close();
  evidence.ownedBrowserClosed = true;
  await writeFile(`${output}/evidence.json`, JSON.stringify(evidence, null, 2) + '\n');
  console.log(JSON.stringify({ status: evidence.status, witness: evidence.witness, error: evidence.error, output }));
}
