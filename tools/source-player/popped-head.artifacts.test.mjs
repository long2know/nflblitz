import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { readFile } from 'node:fs/promises';
import test from 'node:test';

const root = new URL('./artifacts/', import.meta.url);
const directory = new URL('original-head-live-attempt1/', root);
const evidence = JSON.parse(await readFile(new URL('evidence.json', directory)));
const punt = JSON.parse(await readFile(new URL('original-head-punt-attempt1/evidence.json', root)));
const flash = JSON.parse(await readFile(new URL('original-head-flash-attempt1/evidence.json', root)));
const receipt = await readFile(new URL('head-combined-build-info.json', root));
const sha = bytes => createHash('sha256').update(bytes).digest('hex');
const sourceSha = '3fb4e566b57670e397fde16c6f515d06c95379420074c2d6c7b111bca843e257';
const geometrySha = 'f835cda5498db7b3f2783b3cfda99d07a2ef765f821f1655c1b4be3d555a57b5';

function requireOriginalHead(run) {
  assert.equal(run.receiptSha256, sha(receipt), 'Original-head evidence must belong to the actual combined WASM build.');
  assert.equal(run.status, 'requested-goal-live-witnesses-passed');
  assert.equal(run.fullNativeOrMatchQualified, false);
  assert.equal(run.httpAssemblies.length, 2);
  assert(run.httpAssemblies.every(a => a.sha256 === a.observedSha256 &&
    run.assemblyRequests.some(r => r.status === 200 && r.url.endsWith('/' + a.path))));
  const head = run.frames.find(f => f.name === 'actual-original-popped-head');
  const continuing = run.frames.find(f => f.name === 'continued-after-original-popped-head');
  assert(head && continuing, 'Actual source flag/head/continuing frames are required, not an adapter fixture.');
  const state = head.snapshot.state, gpu = head.gpu;
  assert.equal(state.actorCount, 14);
  assert.equal(state.kernel.policy, 'Portable');
  assert.equal(state.assets.poppedHead.nativeHeaderEquivalent, false);
  const selected = gpu.poppedHeadBindings.find(binding => {
    const actor = state.actors[binding.actor];
    return (actor.rawFlags & 0x00800000) !== 0 && actor.headVariant === 'original-phead-source-authored' &&
      gpu.poppedHeadPixels[binding.actor] > 0;
  });
  assert(selected, 'Actual PF_POP_HELMET must have visible original phead-only pixels; body pixels cannot substitute.');
  assert.equal(selected.headSourceSha256, sourceSha);
  assert.equal(selected.headGeometrySha256, geometrySha);
  assert.equal(selected.headJoint, 8);
  assert.deepEqual(selected.headParts, [{ mesh: 'limbs/limb_phead_obj/1', slot: 1 }]);
  assert.equal(selected.tint, 'none');
  assert.equal(new Set(state.actors.map(a => a.uniformHash)).size, 2);
  assert(continuing.snapshot.state.sourceTick - state.sourceTick >= 120, 'The original head must not stop source gameplay.');
  assert(continuing.snapshot.ready && !continuing.snapshot.failed);
  assert.notDeepEqual(continuing.snapshot.state.actors.map(a => a.poseDigest), state.actors.map(a => a.poseDigest));
  assert.equal(run.errors.length, 0); assert.equal(run.networkErrors.length, 0);
}

test('actual current combined C# browser shows original source phead on a real flag and continues gameplay', () => {
  requireOriginalHead(evidence);
});

test('mutating actual emitted flag, original binding, head-only pixels or continuing ticks rejects the witness', () => {
  for (const mutate of [
    d => { d.receiptSha256 = '0'.repeat(64); },
    d => { d.frames.find(f => f.name === 'actual-original-popped-head').gpu.poppedHeadPixels.fill(0); },
    d => { for (const a of d.frames.find(f => f.name === 'actual-original-popped-head').snapshot.state.actors) a.rawFlags &= ~0x00800000; },
    d => { d.frames.find(f => f.name === 'actual-original-popped-head').gpu.poppedHeadBindings[0].headParts[0].mesh = 'body-8'; },
    d => { d.frames.find(f => f.name === 'actual-original-popped-head').gpu.poppedHeadBindings[0].headParts[0].slot = 4; },
    d => { d.frames.find(f => f.name === 'continued-after-original-popped-head').snapshot.state.sourceTick =
      d.frames.find(f => f.name === 'actual-original-popped-head').snapshot.state.sourceTick; },
  ]) {
    const changed = structuredClone(evidence); mutate(changed); assert.throws(() => requireOriginalHead(changed));
  }
});

test('original-head PNGs remain byte-identical to their own CDP capture receipts', async () => {
  for (const name of ['actual-original-popped-head', 'continued-after-original-popped-head']) {
    const frame = evidence.frames.find(f => f.name === name);
    const bytes = await readFile(new URL(name + '.png', directory));
    assert.equal(bytes.length, frame.pngBytes); assert.equal(sha(bytes), frame.pngSha256);
    assert(frame.screenshotSourceTickBracket[1] >= frame.screenshotSourceTickBracket[0]);
  }
  assert.match(evidence.device.unmaskedRenderer, /NVIDIA.*RTX 3070/);
});

function requirePunt(run) {
  assert.equal(run.receiptSha256, sha(receipt));
  assert.equal(run.status, 'requested-goal-live-witnesses-passed');
  const meter = run.frames.find(f => f.name === 'actual-punt-meter').snapshot.state;
  const release = run.frames.find(f => f.name === 'actual-punt-release');
  assert.equal(meter.kick.mode, 'PortableAuthored');
  assert.equal(meter.kick.metrics.PuntMeterHeight, 209);
  assert.equal(meter.kick.metrics.NativeGeneratedMetricsKnown, false);
  assert(meter.kick.meter.IsSpecified && meter.kick.meter.ShowMeterFlag && meter.kick.meter.Kind === 2);
  const launch = release.snapshot.state.kick.lastLaunch;
  assert(launch.IsSpecified && launch.Kind === 2 && launch.SourceTick > meter.kick.meter.CreatedSourceTick);
  assert(launch.Velocity.X || launch.Velocity.Y || launch.Velocity.Z);
  assert.equal(release.snapshot.state.ball.mode, 'InFlight');
  assert(release.gpu.ballPixels > 0, 'An actual punt must produce original physical-ball geometry pixels.');
  assert(run.input.some(i => i.name === 'b' && i.down && i.observedSourceTick > meter.kick.meter.CreatedSourceTick));
}

test('same combined build preserves the public original punt meter -> lock -> physical-ball pipeline', () => {
  requirePunt(punt);
  for (const mutate of [
    d => { d.frames.find(f => f.name === 'actual-punt-release').gpu.ballPixels = 0; },
    d => { d.frames.find(f => f.name === 'actual-punt-release').snapshot.state.kick.lastLaunch.Kind = 1; },
    d => { d.frames.find(f => f.name === 'actual-punt-meter').snapshot.state.kick.metrics.NativeGeneratedMetricsKnown = true; },
  ]) {
    const changed = structuredClone(punt); mutate(changed); assert.throws(() => requirePunt(changed));
  }
});

function requireWhiteFlash(run) {
  assert.equal(run.receiptSha256, sha(receipt));
  assert.equal(run.status, 'requested-goal-live-witnesses-passed');
  const frame = run.frames.find(f => f.name === 'actual-raw-white-flash');
  assert(frame, 'The real source white-flash frame is required.');
  assert(frame.snapshot.state.effects.recordCounts.ActorFlags > 0);
  assert(frame.snapshot.state.actors.some(actor => (actor.rawFlags & 0x400) !== 0 && actor.flashWhite &&
    frame.gpu.actorRawFlags[actor.id] === actor.rawFlags && frame.gpu.actorFlash[actor.id] === 192 / 255 &&
    frame.gpu.actorPixels[actor.id] > 0 && actor.tint === 'none'),
  'Only visible authoritative raw0x400 and actual additive192 may prove actor flash, never a tint or hidden actor.');
  assert(run.witnessed.fire && run.witnessed.camera && run.witnessed.shaker && run.witnessed.return);
}

test('same combined source game produces visible real raw0x400 white flash and source spectacle', async () => {
  requireWhiteFlash(flash);
  for (const mutate of [
    d => { d.frames.find(f => f.name === 'actual-raw-white-flash').gpu.actorFlash.fill(0); },
    d => { d.frames.find(f => f.name === 'actual-raw-white-flash').gpu.actorPixels.fill(0); },
    d => { for (const a of d.frames.find(f => f.name === 'actual-raw-white-flash').snapshot.state.actors) a.rawFlags &= ~0x400; },
  ]) {
    const changed = structuredClone(flash); mutate(changed); assert.throws(() => requireWhiteFlash(changed));
  }
  const frame = flash.frames.find(f => f.name === 'actual-raw-white-flash');
  const bytes = await readFile(new URL('original-head-flash-attempt1/actual-raw-white-flash.png', root));
  assert.equal(sha(bytes), frame.pngSha256);
});
