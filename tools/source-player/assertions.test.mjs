import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import test from 'node:test';
import { requireOriginalFrame, requirePortableReady, requireProgress } from './assertions.mjs';

const file = new URL('./artifacts/portable-player/evidence.json', import.meta.url);
const evidence = JSON.parse(await readFile(file));
const actual = evidence.observations[0].before, proof = evidence.observations[0].gpuProof;
const clone = value => structuredClone(value);

test('actual emitted browser artifact contains original live C#/WebGL actors and authored HUD', () => {
  requireOriginalFrame(actual, proof);
});

test('actual emitted portable browser artifact binds the real immutable policy and C# assemblies', () => {
  requirePortableReady(actual, evidence.messages);
});

test('mutating the actual emitted artifact to an unbound or strict factory turns the ready gate red', () => {
  for (const change of [
    value => { value.ready = false; },
    value => { value.state.kernel.portableBound = false; },
    value => { value.state.kernel.policy = 'StrictSource'; },
    value => { value.state.kernel.snapshotPolicy = 'StrictSource'; },
    value => { value.build.assemblies = []; },
  ]) {
    const changed = clone(actual); change(changed);
    assert.throws(() => requirePortableReady(changed, evidence.messages));
  }
});

test('mutating actual emitted actor inventory, source pose, camera or original texture binding turns the gate red', () => {
  for (const change of [
    value => { value.state.actorCount = 0; },
    value => { value.state.actors[0].clip = null; },
    value => { value.state.actors[0].animationSourceTick--; },
    value => { value.state.camera.sourceTick--; },
    value => { value.state.actors[0].tint = '#ff0000'; },
    value => { value.state.hud.vertices = 0; },
    value => { value.state.hud.committedSourceTick -= 2; },
  ]) {
    const changed = clone(actual); change(changed);
    assert.throws(() => requireOriginalFrame(changed, proof));
  }
  const borrowed = clone(proof);
  borrowed.appearances[7].textures[2] = borrowed.appearances[0].textures[2];
  borrowed.appearances[7].textureHashes[2] = borrowed.appearances[0].textureHashes[2];
  assert.throws(() => requireOriginalFrame(actual, borrowed), /borrowed|atlas|uniform/);
  const invisible = clone(proof); invisible.visibleAway = 0;
  assert.throws(() => requireOriginalFrame(actual, invisible), /BOTH teams/);
});

test('mutating a real emitted frame to frozen ticks rejects apparent render-only progress', () => {
  const state = actual.state;
  assert.throws(() => requireProgress(state, clone(state)), /stalled/);
  const posesOnly = clone(state); posesOnly.actors[0].poseDigest = '1234567890abcdef';
  assert.throws(() => requireProgress(state, posesOnly), /stalled/);
  const ticksOnly = clone(state); ticksOnly.sourceTick += 2; ticksOnly.sourceWakes += 2; ticksOnly.hostTick += 3;
  assert.throws(() => requireProgress(state, ticksOnly), /static mesh/);
});

function requireHumanSlice(receipt) {
  const { manualSnap, manualPass } = receipt.slice;
  assert.equal(manualSnap.type, 'Snapped');
  assert.equal(manualSnap.team, 'Home');
  assert.equal(manualPass.type, 'PassReleased');
  assert.equal(manualPass.team, 'Home', 'A prior CPU pass cannot prove the human pass control.');
  assert.equal(manualPass.actor, manualSnap.actor);
  assert(manualPass.sourceTick > manualSnap.sourceTick, 'The human pass must follow its actual source snap.');
  const state = receipt.final.state;
  assert(state.events.some(e => e.type === 'PassReleased' && e.team === 'Home' &&
    e.actor === manualPass.actor && e.sourceTick === manualPass.sourceTick));
  assert(state.contactCounts.ProgressStopped > 0);
  assert(state.events.some(e => e.type === 'PlayResolved' &&
    (e.cause === 'CarrierTackledInBounds' || e.cause === 'CarrierDownInOwnEndZone')),
  'A blocking puppet link is not a classified carrier tackle.');
}

test('actual browser artifact proves human Home snap/pass and classified tackle rather than CPU/block substitutes', () => {
  requireHumanSlice(evidence);
  const cpu = clone(evidence); cpu.slice.manualPass.team = 'Away';
  assert.throws(() => requireHumanSlice(cpu), /CPU pass/);
  const early = clone(evidence); early.slice.manualPass.sourceTick = early.slice.manualSnap.sourceTick - 1;
  assert.throws(() => requireHumanSlice(early), /follow/);
  const blocking = clone(evidence); blocking.final.state.contactCounts.ProgressStopped = 0;
  assert.throws(() => requireHumanSlice(blocking));
});

test('actual moving source ball produces original-geometry pixels rather than state-only flight claims', () => {
  const flight = evidence.observations.find(o => o.name === 'original-source-ball-flight');
  assert.equal(flight.before.state.ball.mode, 'InFlight');
  assert(flight.gpuProof.ballPixels > 0);
  assert.match(flight.gpuProof.ballOrigin, /original hand001b material6/);
  assert.notDeepEqual(flight.before.state.ball.position, actual.state.ball.position);
});
