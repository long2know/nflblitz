import assert from 'node:assert/strict';
import test from 'node:test';
import { readFile } from 'node:fs/promises';
import { requireOriginalCameraStage, requireStableCameraPause, requireFourHumanCameraCausality } from './normal-camera.assertions.mjs';

function fixture() {
  const projection = [-2, 0, 0, 0, 0, 3, 0, 0, 0, 0, 1.01, 1, 0, 0, -4.7, 0];
  const state = {
    active: false, sourceTick: 123, sourceWakes: 119, frames: 211,
    actors: Array.from({ length: 14 }, (_, id) => ({ id, controller: id < 4 ? id : -1, position: [id * 3, 0, id * 2] })),
    input: { axes: Array(8).fill(0), buttons: Array(4).fill(0) },
    sourceRandom: { evidence: 'ExternalUnverified' },
    camera: { producer: 'original-active-CAMBOT-normal-kickoff-impact', sourceTick: 123, headingSampleTick: 123,
      sourceHres: 512, sourceVres: 384, normalWakes: 17, position: [-271, 91, -23],
      transform: [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0], viewProjection: projection,
      cameraSpaceProjection: projection },
  };
  return { snapshot: { state }, gpu: { sourceTick: 123, cameraUniform: projection.map(Math.fround),
    cameraPacket: projection.map(Math.fround), proofViewport: [0, 0, 512, 384], contentViewport: [0, 0, 1024, 768] } };
}

test('camera observer requires actual GPU source matrix, raster and shared wake', () => {
  const { snapshot, gpu } = fixture();
  requireOriginalCameraStage(snapshot, gpu);
  for (const mutate of [
    copy => { copy.snapshot.state.camera.producer = 'legacy EMA'; },
    copy => { copy.snapshot.state.camera.headingSampleTick--; },
    copy => { copy.snapshot.state.camera.sourceHres = 1024; },
    copy => { copy.gpu.cameraUniform[12] += 1; },
    copy => { copy.snapshot.state.sourceRandom.evidence = 'VerifiedAlgorithm'; },
  ]) {
    const copy = structuredClone({ snapshot, gpu }); mutate(copy);
    assert.throws(() => requireOriginalCameraStage(copy.snapshot, copy.gpu));
  }
});

test('real pause and four-cabinet causal witnesses are substantive', () => {
  const before = fixture().snapshot.state, after = structuredClone(before);
  requireStableCameraPause(before, after, [123, 123]);
  assert.throws(() => requireStableCameraPause(before, after, [123, 124]));
  after.camera.normalWakes++;
  assert.throws(() => requireStableCameraPause(before, after, [123, 123]));
  after.sourceTick = 124; after.sourceWakes++; after.camera.sourceTick++;
  for (let station = 0; station < 4; station++) after.input.axes[station * 2] = 1;
  assert.throws(() => requireFourHumanCameraCausality(before, after, [0, 1, 2, 3]));
  after.actors[0].position[0]++;
  requireFourHumanCameraCausality(before, after, [0, 1, 2, 3]);
  after.input.axes[6] = 0;
  assert.throws(() => requireFourHumanCameraCausality(before, after, [0, 1, 2, 3]));
});

test('parsed and executed real camera observer mutations cannot accept bad evidence', async () => {
  const source = await readFile(new URL('./normal-camera.assertions.mjs', import.meta.url), 'utf8');
  const controls = [
    ["assert.deepEqual(gpu.cameraUniform, camera.viewProjection.map(Math.fround),", "assert.deepEqual(gpu.cameraUniform, gpu.cameraUniform,", copy => { copy.gpu.cameraUniform[12] += 1; }],
    ["assert.equal(camera.sourceTick, camera.headingSampleTick,", "assert.equal(camera.sourceTick, camera.sourceTick,", copy => { copy.snapshot.state.camera.headingSampleTick--; }],
    ["assert.equal(camera.sourceHres, 512);", "", copy => { copy.snapshot.state.camera.sourceHres = 1024; }],
  ];
  for (const [before, after, corrupt] of controls) {
    assert.equal(source.split(before).length, 2);
    const module = await import('data:text/javascript;base64,' + Buffer.from(source.replace(before, after)).toString('base64'));
    const evidence = fixture(); corrupt(evidence);
    assert.throws(() => requireOriginalCameraStage(evidence.snapshot, evidence.gpu));
    module.requireOriginalCameraStage(evidence.snapshot, evidence.gpu);
  }
});
