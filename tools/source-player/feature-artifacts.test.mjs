import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { readFile } from 'node:fs/promises';
import test from 'node:test';

const root = new URL('./artifacts/', import.meta.url);
const read = async name => JSON.parse(await readFile(new URL(name, root)));
const spectacle = await read('spectacle-attempt3/evidence.json');
const fieldGoal = await read('fg-attempt3/evidence.json');
const punt = await read('punt-attempt3/evidence.json');
const options = await read('features-current/evidence.json');
const receipt = await readFile(new URL('candidate-build-info.json', root));
const receiptSha = createHash('sha256').update(receipt).digest('hex');
const clone = value => structuredClone(value);

function currentBuild(evidence) {
  assert.equal(evidence.receiptSha256, receiptSha, 'Use the actual current WASM receipt, not relabelled prior footage.');
  assert.equal(evidence.httpAssemblies.length, 2);
  for (const assembly of evidence.httpAssemblies) {
    assert.equal(assembly.observedSha256, assembly.sha256);
    assert(evidence.assemblyRequests.some(r => r.url.endsWith('/' + assembly.path) && r.status === 200));
  }
  assert.match(evidence.device.unmaskedRenderer, /NVIDIA.*RTX 3070/);
}
function fireFact(evidence) {
  currentBuild(evidence);
  const frame = evidence.frames.find(f => f.name === 'actual-source-fire');
  assert(frame, 'A real live source-emission frame is required.');
  const state = frame.snapshot.state, effects = state.effects;
  assert.equal(state.actorCount, 14); assert.equal(state.kernel.sourceFire, 'portable');
  assert(effects.framesConsumed > 0 && effects.draws > 0 && frame.gpu.firePixels > 0,
    'Real Core particles must draw original alpha texture pixels, not a render-only emitter.');
  assert.equal(effects.sourceTick, state.sourceTick);
  assert.equal(effects.cameraSourceTick, state.camera.sourceTick);
  assert.equal(effects.selectedTextures.length, effects.draws);
  assert(effects.recordCounts.CreateProcess > 0 && effects.recordCounts.CreateObject > 0 &&
    effects.recordCounts.PlayerEntryCache > 0, 'A producer/cache/object history must accompany real particles.');
  assert(effects.selectedTextures.every(t => Number.isInteger(t.TextureFrame) && t.TextureFrame >= 0 &&
    t.TextureFrame < 17 && /^[0-9a-f]{64}$/.test(t.TextureSourceSha256)), 'Selected original TRAIL identities required.');
}

test('current real partial spectacle artifact proves source producers/original GPU sprites, not full spectacle acceptance', () => {
  fireFact(spectacle);
  for (const change of [
    d => { d.receiptSha256 = '0'.repeat(64); },
    d => { d.frames.find(f => f.name === 'actual-source-fire').gpu.firePixels = 0; },
    d => { d.frames.find(f => f.name === 'actual-source-fire').snapshot.state.effects.recordCounts.CreateObject = 0; },
    d => { d.frames.find(f => f.name === 'actual-source-fire').snapshot.state.effects.selectedTextures[0].TextureFrame = 17; },
  ]) {
    const changed = clone(spectacle); change(changed); assert.throws(() => fireFact(changed));
  }
});

test('current eligible CSI/shaker/return facts have actual contact cues, source draws and changing camera frames', () => {
  const frames = fieldGoal.frames;
  const before = frames.find(f => f.name === 'source-camera-before-contact').snapshot.state.camera;
  const during = frames.find(f => f.name === 'eligible-source-camera').snapshot.state.camera;
  const shake = frames.find(f => f.name === 'eligible-source-shaker').snapshot.state.camera;
  const returned = frames.find(f => f.name === 'eligible-source-camera-return').snapshot.state.camera;
  assert.equal(before.transient, false);
  assert.equal(during.transient, true);
  assert(during.cameraTrace.some(c => c.kind === 'ContactCameraSelection' && c.table?.startsWith('csi_')));
  const stage = shake.cameraTrace.findLast(c => c.kind === 'camera-source-stage');
  assert(stage.shakeX != null && stage.shakeZ != null, 'A retained shaker cue without actual source RNG draws is insufficient.');
  assert.equal(returned.transient, false); assert.equal(returned.resuming, false);
  assert.notDeepEqual(before.position, during.position);
  assert.notDeepEqual(during.position, returned.position);
});

function fieldGoalFact(evidence) {
  currentBuild(evidence);
  const meter = evidence.frames.find(f => f.name === 'actual-fieldGoal-meter').snapshot.state;
  const release = evidence.frames.find(f => f.name === 'actual-fieldGoal-release');
  assert.equal(meter.kick.mode, 'PortableAuthored');
  assert.equal(meter.kick.metrics.FieldGoalBarWidth, 151);
  assert.equal(meter.kick.metrics.FieldGoalMarkerWidth, 27);
  assert.equal(meter.kick.metrics.PuntMeterHeight, 209);
  assert.equal(meter.kick.metrics.NativeGeneratedMetricsKnown, false);
  assert(meter.kick.meter.IsSpecified && meter.kick.meter.ShowMeterFlag && meter.kick.meter.Kind === 1);
  const state = release.snapshot.state, launch = state.kick.lastLaunch;
  assert(launch.IsSpecified && launch.Kind === 1 && launch.SourceTick > meter.kick.meter.CreatedSourceTick);
  assert(launch.Velocity.X || launch.Velocity.Y || launch.Velocity.Z);
  assert.equal(state.ball.mode, 'InFlight'); assert(release.gpu.ballPixels > 0);
  assert(state.hud.elements.every(e => typeof e === 'string'));
  assert(evidence.input.some(i => i.name === 'b' && i.down && i.observedSourceTick > meter.kick.meter.CreatedSourceTick));
}
test('current public FG meter -> cabinet lock -> original animation release -> physical ball is real', () => {
  fieldGoalFact(fieldGoal);
  const changed = clone(fieldGoal);
  changed.frames.find(f => f.name === 'actual-fieldGoal-release').gpu.ballPixels = 0;
  assert.throws(() => fieldGoalFact(changed));
});

test('real URL/reset cases retain independent defaults, reject duplicates/native data and preserve static uploads', () => {
  assert.equal(options.status, 'passed'); assert.equal(options.cases.length, 9);
  for (const row of options.cases) {
    assert.equal(row.status, 'passed'); assert.equal(row.ownedTargetClosed, true);
    if (row.loaded) {
      assert.equal(row.restarted.state.epoch, 1);
      assert.equal(row.restarted.state.effects.epoch, 1);
      assert.equal(row.restarted.state.effects.enabled, row.loaded.state.effects.enabled);
      assert.equal(row.restarted.state.kick.mode, row.loaded.state.kick.mode);
    } else assert.equal(row.terminal.className, 'failed');
  }
});

function fullAcceptance() {
  assert(spectacle.witnessed.flash, 'Live visible raw actor0x400 white flash remains UNMET.');
  assert(punt.witnessed.puntMeter && punt.witnessed.puntRelease, 'Current-build public punt evidence remains UNMET.');
}
test('actual partial artifacts cannot be relabelled complete spectacle/kick qualification', () => {
  assert.throws(fullAcceptance, /raw actor0x400 white flash remains UNMET/);
  assert.equal(spectacle.status, 'failed');
  assert.equal(punt.status, 'failed');
  assert.match(spectacle.error, /Original popped-head limb is missing/);
  assert.match(punt.error, /Original popped-head limb is missing/);
});
