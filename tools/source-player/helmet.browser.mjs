import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { mkdir, writeFile } from 'node:fs/promises';
import { launchChrome, sleep, value } from '../../tests/browser/chrome.mjs';
import { CABINET_KEYS } from './wwwroot/input.js';

const args = process.argv.slice(2);
const option = (key, fallback) => args.includes(key) ? args[args.indexOf(key) + 1] : fallback;
const url = option('--url', 'http://127.0.0.1:8095/?source-fire=portable');
const output = option('--output', '.squad/.scratch/loose-helmet-proof/browser');
const seconds = Number(option('--seconds', '600'));
await mkdir(output, { recursive: false });
const hash = bytes => createHash('sha256').update(bytes).digest('hex');
const evidence = {
  schema: 'source-loose-helmet-natural-browser/v1', status: 'running', url,
  method: 'Fresh owned Chrome and actual WASM. Only four-cabinet public keys, public Escape pause/resume and Continue. After a natural helmet, Continue is deliberately delayed to observe the original unbounded effect; no source state, seed, RNG, cue, pose or object injection.',
  declaredWallCampaignSeconds: seconds, input: [], samples: [], frames: [], messages: [], errors: [], networkErrors: [],
  assemblyRequests: [], wholeGameQualified: false, nativeQualified: false, audioQualified: false, performanceQualified: false,
};
const chrome = await launchChrome(output), page = chrome.cdp;
evidence.browser = chrome.version; evidence.launch = chrome.launch;
let observedTick = -1, lastGoodSnapshot;
async function snapshot() {
  const current = await value(page, 'globalThis.sourcePlayer?.snapshot()');
  if (current?.state) observedTick = current.state.sourceTick;
  if (current?.failed) {
    evidence.runtimeFailure = { current, status: await value(page, 'document.getElementById("status").textContent') };
    throw new Error(evidence.runtimeFailure.status);
  }
  if (current?.ready) lastGoodSnapshot = current;
  return current;
}
async function key(code, down) {
  const [key, number] = code.startsWith('Key') ? [code.slice(3).toLowerCase(), code.charCodeAt(3)] :
    code === 'End' ? ['End', 35] : code === 'Escape' ? ['Escape', 27] : ['Enter', 13];
  evidence.input.push({ sourceTickBeforeEvent: observedTick, code, down, wallMilliseconds: Date.now() });
  await page.send('Input.dispatchKeyEvent', { type: down ? 'keyDown' : 'keyUp', code, key,
    windowsVirtualKeyCode: number, nativeVirtualKeyCode: number });
}
async function tap(codes, milliseconds = 60) {
  for (const code of codes) await key(code, true);
  await sleep(milliseconds);
  for (const code of codes) await key(code, false);
}
async function capture(name, requireHelmet = false) {
  const proof = await value(page, '({snapshot:sourcePlayer.snapshot(),gpu:sourcePlayer.gpuProof()})');
  if (requireHelmet && proof.gpu.looseHelmetPixels === 0) return null;
  const image = await page.send('Page.captureScreenshot', { format: 'png' });
  const png = Buffer.from(image.data, 'base64');
  await writeFile(`${output}/${name}.png`, png);
  const after = await snapshot();
  const frame = { name, ...proof, pngSha256: hash(png), pngBytes: png.length,
    screenshotSourceTickBracket: [proof.snapshot.state.sourceTick, after.state.sourceTick] };
  evidence.frames.push(frame);
  if (requireHelmet) {
    assert.equal(frame.screenshotSourceTickBracket[0], frame.screenshotSourceTickBracket[1],
      'Normal-render original PHELM PNG must be genuinely paused at one actual source tick.');
    assert(proof.gpu.looseHelmetColoredPixels > 0 && proof.gpu.looseHelmetDraws.length > 0);
    for (const draw of proof.gpu.looseHelmetMaterialDraws) {
      assert.equal(draw.mesh, 'original/loose-helmet/limb_phelm_obj'); assert.equal(draw.indices, 267);
      assert.equal(draw.decalSlot, 2); assert.equal(draw.integerAspect, 4);
      assert.equal(draw.gpuDepthFunction, 'LESS'); assert.equal(draw.gpuDepthWrite, true);
      assert.equal(draw.gpuConstantAlpha, true); assert.equal(draw.gpuTextureTimesAlpha, true);
      assert.equal(draw.gpuSourceAlphaBlend, true); assert.equal(draw.gpuOriginalTextureBound, true);
      assert.equal(draw.gpuWhiteAdd, draw.globalBallFlags & 1 ? 1 : 0);
      const actor = proof.gpu.appearances[draw.actor];
      assert.equal(draw.texture, actor.textures[2]); assert.equal(draw.textureSha256, actor.textureHashes[2]);
      const packet = proof.snapshot.state.effects.looseHelmets.find(h => h.objectSerial === draw.objectSerial);
      assert.deepEqual(draw.gpuModel, packet.packet.slice(4).map(Math.fround));
    }
  }
  return frame;
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
  await page.send('Emulation.setDeviceMetricsOverride', { width: 1024, height: 768, deviceScaleFactor: 1, mobile: false });
  await page.send('Page.navigate', { url }); await page.send('Page.bringToFront');
  const loadingDeadline = Date.now() + 180000;
  let loaded;
  while (Date.now() < loadingDeadline) {
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
    const response = await fetch(new URL(name, url)), bytes = Buffer.from(await response.arrayBuffer());
    assert(expected); assert.equal(response.status, 200); assert.equal(hash(bytes), expected.sha256);
    evidence.httpFrontend.push({ name, httpStatus: response.status, sha256: hash(bytes) });
  }
  const response = await fetch(new URL('data/manifest.json', url));
  const manifestBytes = Buffer.from(await response.arrayBuffer());
  assert.equal(hash(manifestBytes), loaded.build.dataManifestSha256);
  evidence.dataManifestSha256 = hash(manifestBytes);
  const manifest = JSON.parse(manifestBytes);
  assert.equal(manifest.files.length, 556);
  const settings = await (await fetch(new URL('data/' + manifest.entrypoints.settings, url))).json();
  assert.deepEqual(settings.stations.map(s => s.id), [0, 1, 2, 3]); evidence.actualConfiguredStations = settings.stations;
  const fireManifest = await (await fetch(new URL('data/' + manifest.entrypoints.fire, url))).json();
  assert.equal(fireManifest.schema, 'blitz-source-fire-spfx-portable/v3');
  assert.equal(fireManifest.helmetInput.gitBlobSha1, 'd086f6944207a0ff2fa906ef7fc9692dcfb289ec');
  evidence.helmetData = fireManifest.helmetInput;
  await capture('ready-original-four-human');
  const deadline = Date.now() + seconds * 1000;
  let flightObject = -1, flightTick = -1, bounceTick = -1, settledTick = -1, cleanupTick = -1;
  let lastSample = -1, lastMenuTap = -1000, resultPlay = -1, continueAt = -1, seenPops = 0;
  const objects = new Set(), deleted = new Set(), sourceRandom = new Map();
  while (Date.now() < deadline) {
    const current = await snapshot(), state = current.state, fx = state.effects, helmets = fx.looseHelmets || [];
    const pops = state.contactCounts?.HelmetPopped || 0;
    if (pops > seenPops) { seenPops = pops; continueAt = Math.max(continueAt, state.sourceTick + 220); }
    if (state.sourceTick !== lastSample) {
      const records = fx.records.filter(r => r.function === 'loose_helmet_proc' ||
        r.Detail?.startsWith('loosehlm:') || r.kind === 'KillProcess' && r.line === 867);
      for (const r of records)
        if (r.kind === 'Rng' && r.function === 'loose_helmet_proc')
          sourceRandom.set(`${r.sourceTick}/${r.ProcessSerial}/${r.wake}/${r.line}`, r);
      if (helmets.length || records.length || state.sourceTick % 57 < 2)
        evidence.samples.push({ sourceTick: state.sourceTick, sourceWakes: state.sourceWakes, renderSamples: state.frames,
          phase: state.phase, playNumber: state.playNumber, contactCounts: state.contactCounts,
          helmets, records, configuredControllers: state.actors.filter(a => a.controller >= 0).map(a => a.controller) });
      lastSample = state.sourceTick;
    }
    for (const helmet of helmets) objects.add(helmet.objectSerial);
    for (const record of fx.records)
      if (record.kind === 'DeleteObjectGroup' && record.Detail?.startsWith('loosehlm:')) {
        deleted.add(record.ObjectSerial);
        if (record.ObjectSerial === flightObject && record.line === 868 && bounceTick >= 0) cleanupTick = record.sourceTick;
      }
    const flying = helmets.find(h => h.visible && h.bounces === 0 && h.flightTime > 1);
    const bounce = helmets.find(h => h.visible && h.objectSerial === flightObject && h.bounces > 0);
    if (flying && flightObject < 0 || bounce && bounceTick < 0) {
      await tap(['Escape'], 10); await sleep(40);
      const paused = await snapshot();
      assert.equal(paused.state.active, false);
      const frame = await capture(flightObject < 0 ? 'natural-original-PHELM-flight' : 'natural-original-PHELM-bounce', true);
      if (frame) {
        const visible = frame.snapshot.state.effects.looseHelmets.find(h => h.visible);
        if (flightObject < 0) { flightObject = visible.objectSerial; flightTick = frame.snapshot.state.sourceTick; }
        else bounceTick = frame.snapshot.state.sourceTick;
      }
      await tap(['Escape'], 10); await sleep(20);
      continue;
    }
    const resting = helmets.find(h => h.visible && h.objectSerial === flightObject && h.bounces > 3 && h.velocity.every(v => v === 0));
    if (resting && settledTick < 0) {
      await tap(['Escape'], 10); await sleep(40);
      const frame = await capture('natural-original-PHELM-resting-no-TTL', true);
      if (frame) settledTick = frame.snapshot.state.sourceTick;
      await tap(['Escape'], 10); await sleep(20);
    }
    if (flightObject >= 0 && deleted.has(flightObject) && bounceTick < 0) {
      evidence.earlyOriginalHelperCleanup = { objectSerial: flightObject, sourceTick: state.sourceTick };
      flightObject = -1; flightTick = -1;
    }
    if (cleanupTick >= 0 && settledTick >= 0 && state.sourceTick >= cleanupTick + 45) {
      assert(helmets.every(h => h.objectSerial !== flightObject));
      assert.equal(new Set(state.actors.filter(a => a.controller >= 0).map(a => a.controller)).size, 4);
      assert(seenPops > 0 && state.actorCount === 14);
      const bounds = [...sourceRandom.values()].map(r => Number(r.Detail.match(/^randrng\((\d+)\)$/)?.[1]));
      assert(bounds.includes(100) && bounds.includes(35) && bounds.includes(201) && bounds.includes(157),
        'Original launch/spin/bounce must be real shared source RNG requests, not cosmetic renderer randomness.');
      await capture('continuing-four-human-after-original-helmet-cleanup');
      evidence.final = await snapshot();
      evidence.witness = { originalHelmetPopped: seenPops, objects: [...objects], deleted: [...deleted],
        flightObject, flightTick, bounceTick, settledTick, cleanupTick, continuedSourceTick: state.sourceTick,
        sharedSourceRandomRecords: [...sourceRandom.values()], sourceWakesDistinctFromRenderSamples: state.sourceWakes !== state.frames };
      evidence.status = 'passed'; break;
    }
    if (state.phase === 'PlaySelect' && state.sourceTick - lastMenuTap >= 18) {
      lastMenuTap = state.sourceTick; await tap(CABINET_KEYS.map(binding => binding[5]), 80);
    } else if (state.phase === 'DeadBall') {
      if (resultPlay !== state.playNumber) {
        resultPlay = state.playNumber; continueAt = Math.max(continueAt, state.sourceTick + (helmets.length ? 220 : 48));
      }
      if (state.sourceTick >= continueAt) { await tap(['Enter']); continueAt = state.sourceTick + 90; }
    }
    await sleep(12);
  }
  assert.equal(evidence.status, 'passed', 'Natural original HelmetPopped/PHELM flight/bounce/settle/cleanup and continuing four-human play must all be witnessed.');
  assert.equal(evidence.errors.length, 0);
  assert.equal(evidence.networkErrors.length, 0);
} catch (error) {
  evidence.status = 'failed'; evidence.error = error.stack;
  evidence.final = await value(page, 'globalThis.sourcePlayer?.snapshot()').catch(() => lastGoodSnapshot);
  process.exitCode = 1;
} finally {
  await chrome.close(); evidence.ownedBrowserClosed = true;
  await writeFile(`${output}/evidence.json`, JSON.stringify(evidence, null, 2) + '\n');
  console.log(JSON.stringify({ status: evidence.status, witness: evidence.witness, error: evidence.error, output }));
}
