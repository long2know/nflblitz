import assert from 'node:assert/strict';

export function requireOriginalCameraStage(snapshot, gpu) {
  const state = snapshot.state, camera = state.camera;
  assert.equal(camera.producer, 'original-active-CAMBOT-normal-kickoff-impact');
  assert.equal(camera.sourceTick, camera.headingSampleTick, 'camera and movement must share one executed source wake');
  assert(camera.sourceTick <= state.sourceTick && camera.sourceTick >= 0);
  assert.equal(camera.sourceHres, 512);
  assert([256, 384].includes(camera.sourceVres), 'DIP raster is not the browser viewport');
  assert.equal(camera.transform.length, 12);
  assert.equal(camera.viewProjection.length, 16);
  assert.equal(camera.cameraSpaceProjection.length, 16);
  assert(camera.position.every(Number.isFinite) && camera.transform.every(Number.isFinite));
  assert(camera.normalWakes <= state.sourceWakes, 'normal dispatches cannot advance behind every transient/render sample');
  assert.equal(gpu.sourceTick, state.sourceTick);
  assert.deepEqual(gpu.cameraUniform, camera.viewProjection.map(Math.fround),
    'actual GPU uniform must be the current source optical transform, not a last-render/legacy/refitted matrix');
  assert.deepEqual(gpu.cameraPacket, camera.viewProjection.map(Math.fround));
  assert.deepEqual(gpu.proofViewport, [0, 0, 512, 384], 'canonical GPU identity proof is not the source DIP or canvas');
  assert(gpu.contentViewport[0] === 0 && gpu.contentViewport[1] === 0 && gpu.contentViewport[2] > 0 && gpu.contentViewport[3] > 0,
    'original world continues across the full content viewport, without SafeFrame/HUD camera fitting');
  assert(camera.cameraSpaceProjection[0] < 0 && camera.cameraSpaceProjection[5] > 0 &&
    camera.cameraSpaceProjection[10] > 1 && camera.cameraSpaceProjection[14] < 0,
  'raw source raster sign and actual finite near/far frustum must be retained');
  assert.equal(state.sourceRandom.evidence, 'ExternalUnverified', 'camera request evidence must not relabel the platform PRNG');
}

export function requireStableCameraPause(before, after, pngTickBracket) {
  assert.equal(before.active, false);
  assert.equal(after.active, false);
  assert.equal(before.sourceTick, after.sourceTick, 'public pause must hold the real source clock');
  assert.deepEqual(pngTickBracket, [before.sourceTick, before.sourceTick],
    'paused gameplay PNG claims require one stable actual source tick');
  assert.equal(before.camera.normalWakes, after.camera.normalWakes, 'render samples must not wake the normal integrator');
  assert.deepEqual(before.camera.position, after.camera.position);
  assert.deepEqual(before.camera.transform, after.camera.transform);
  assert.deepEqual(before.camera.viewProjection, after.camera.viewProjection);
}

export function requireFourHumanCameraCausality(before, after, heldStations) {
  assert(after.sourceTick > before.sourceTick && after.sourceWakes > before.sourceWakes,
    'real cabinet input must continue executing source wakes');
  const controlled = state => state.actors.filter(actor => actor.controller >= 0);
  assert.equal(new Set(controlled(before).map(actor => actor.controller)).size, 4);
  assert.equal(new Set(controlled(after).map(actor => actor.controller)).size, 4);
  for (const station of heldStations)
    assert(Math.abs(after.input.axes[station * 2]) + Math.abs(after.input.axes[station * 2 + 1]) +
      after.input.buttons[station] > 0, `actual cabinet station${station} packet did not reach Core`);
  assert(controlled(after).some(actor => {
    const old = before.actors.find(candidate => candidate.id === actor.id);
    return old && actor.position.some((coordinate, axis) => Math.abs(coordinate - old.position[axis]) > .01);
  }), 'actual controlled source roots must causally move, not just animate');
  assert(after.camera.sourceTick > before.camera.sourceTick, 'cabinet play must use fresh pre-actor movement frames');
}
