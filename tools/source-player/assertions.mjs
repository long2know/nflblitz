import assert from 'node:assert/strict';

export function requireOriginalFrame(snapshot, proof) {
  assert.equal(snapshot.failed, false, 'The real kernel/render has explicitly failed.');
  const state = snapshot.state;
  assert.equal(state?.runtimeFlavor, 'portable C# browser preview (not a Unity player build)',
    'Do not relabel an inspector/native Unity player as this preview.');
  assert.equal(state.kernel.factory, 'SourceC2Composition.CreateM1Simulation', 'The actual M1 factory must be used.');
  assert.equal(state.actorCount, 14, 'A world inspector or incomplete actor set is not a playable preview.');
  assert.equal(state.jointsPerActor, 15, 'Use the actual source fifteen-joint pose sampler.');
  assert.equal(state.sourceTicksPerSecond, 57, 'Production source clock must drive simulation.');
  assert.equal(state.profile, 'e87401c8cf3e348cc2d9c7255a1f18f43e41b1694ff7dff45c4bf025a5ecce4f',
    'Actual canonical281 animation content must feed the kernel and pose sampler.');
  assert.equal(state.actors.length, 14);
  assert.deepEqual(state.actors.map(a => a.id).sort((a, b) => a - b), Array.from({ length: 14 }, (_, i) => i));
  assert.equal(state.actors.filter(a => a.team === 'Home').length, 7);
  assert.equal(state.actors.filter(a => a.team === 'Away').length, 7);
  assert(state.actors.every(a => a.clip && a.script && /^[0-9a-f]{16}$/.test(a.poseDigest) &&
    a.animationSourceTick === state.sourceTick), 'Every original actor needs a current actual source pose, not a bind-pose fallback.');
  assert(state.actors.every(a => a.tint === 'none'), 'An RGB tint is not an original team texture.');
  assert.equal(new Set(state.actors.map(a => a.uniformHash)).size, 2, 'Both teams must retain distinct original uniform atlas content.');
  assert(state.assets.worldDraws > 0 && state.assets.meshes > 14 && state.assets.textures > 5,
    'Required original world/player/atlas assets were not rendered.');
  assert(state.hud.vertices > 0 && state.hud.primitives > 0 &&
    state.hud.origin === 'original authored atlas/font/draw composer; portable raster origin',
  'The authored original HUD raster/font/draw commands are required, not system text pretending to be the HUD.');
  assert.equal(state.hud.committedSourceTick, state.sourceTick - (state.hud.resultPending ? 1 : 0),
    'Only the real typed one-source-wake C2 pending result permits a held original HUD.');
  if (!state.camera.suspended) {
    assert.equal(state.camera.sourceTick, state.sourceTick, 'A stale render/orbit camera cannot feed source M1.');
    assert.equal(state.camera.headingSampleTick, state.sourceTick, 'Actual executing source camera MatInput is required.');
  }
  assert.equal(state.audio.output, 'Audio output unqualified', 'No synthetic/muted output may be called original PCM/DSP playback.');
  assert(proof.worldDraws > 0 && proof.meshUploads >= state.assets.meshes && proof.frameCount > 0,
    'Actual WebGL draws and original geometry uploads are required.');
  assert(proof.visibleHome > 0 && proof.visibleAway > 0, 'GPU pixels must contain original players from BOTH teams.');
  assert.equal(proof.appearances.length, 14);
  for (let id = 0; id < 14; id++) {
    const actor = state.actors[id], style = proof.appearances[id];
    assert.equal(style.sourcePlayerId, actor.sourcePlayerId);
    assert.equal(style.tint, 'none');
    assert.equal(style.uniformIdentity, actor.uniform);
    assert.equal(style.textureHashes[2], actor.uniformHash, `Actor ${id} silently borrowed another team's atlas.`);
    assert.equal(style.textures[2], 'teams/' + actor.textureSlots[2]);
    assert.equal(style.originalNumber, actor.number, 'Retain actual source packed BCD jersey numbers.');
    assert(style.numberMesh.startsWith('numbers/') && style.numberTexture === style.textures[5]);
  }
  assert(proof.device.unmaskedRenderer || proof.device.renderer, 'Record the real WebGL renderer, not launch-flag assumptions.');
}

export function requirePortableReady(snapshot, messages) {
  assert.equal(snapshot.ready, true, 'The portable player-ready marker was not earned by live C#/WebGL frames.');
  assert.equal(snapshot.state.kernel.portableBound, true, 'The new portable production factory option is unbound.');
  assert.equal(snapshot.state.kernel.policy, 'Portable', 'Strict-source/unknown policy is not positive playable qualification.');
  assert.equal(snapshot.state.kernel.snapshotPolicy, 'Portable', 'The actual immutable match snapshot must agree with the simulation policy.');
  assert(messages.some(message => message.startsWith('SOURCE_PLAYER_READY ')), 'The live kernel/render never emitted its actual ready marker.');
  assert(snapshot.build.assemblies.some(a => /\/SourcePlayer\.Core[.\w-]*\.wasm$/.test(a.path)) &&
    snapshot.build.assemblies.some(a => /\/SourcePlayer\.Browser[.\w-]*\.wasm$/.test(a.path)),
  'Actual production C# gameplay and browser Webcil assemblies are required, not JavaScript fallback physics.');
}

export function requireProgress(before, after) {
  assert(after.sourceTick > before.sourceTick + 1, 'Source ticks stalled; changing inspection images/poses cannot satisfy gameplay.');
  assert(after.hostTick > before.hostTick, 'Host simulation stopped.');
  assert.equal(after.sourceWakes - before.sourceWakes, after.sourceTick - before.sourceTick,
    'Every ordered source wake must be consumed by camera/HUD, including catch-up steps.');
  assert(before.actors.some((a, i) => a.poseDigest !== after.actors[i].poseDigest),
    'Original player poses did not change; a static mesh inspector is not gameplay.');
}

export function requireCausalMovement(before, after) {
  assert(after.input.changes > before.input.changes, 'No actual browser cabinet-input change reached C#.');
  assert(before.actors.some((a, i) => a.controller >= 0 && a.position.some((v, axis) =>
    Math.abs(v - after.actors[i].position[axis]) > .01)), 'Actual controlled source actor did not move after cabinet input.');
}
