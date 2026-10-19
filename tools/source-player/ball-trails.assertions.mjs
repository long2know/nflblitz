import assert from 'node:assert/strict';

export function assertBallTrailEvidence(evidence) {
  assert.equal(evidence.schema, 'original-trail-ball-browser/v1');
  assert.equal(evidence.status, 'qualified-portable-original-football-ghosts-NOT-native-parity');
  assert.equal(evidence.nativeUnityOrPixelsQualified, false);
  assert.equal(evidence.build.nativeUnityBuild, false);
  assert.equal(evidence.build.dataManifestSha256, 'ff2851555e6f8b01814b4a29dd20f8194761662debe8e6cb99db789dc5349f0d');
  assert.deepEqual(new URL(evidence.url).searchParams.getAll('source-fire'), ['portable']);
  assert.equal(evidence.final.state.epoch, 0, 'No reset may substitute for a real ghost lifecycle.');
  assert.equal(evidence.errors.length, 0); assert.equal(evidence.networkErrors.length, 0);
  assert(!evidence.input.some(i => i.name === 'r' || i.name === 'R'), 'No restart is a ball-trail witness.');
  assert.equal(evidence.nativeProgramJournal.length, 3);
  assert.equal(evidence.finalProgramCount, 3, 'The proof must not inject a replacement GPU program.');
  assert(evidence.nativeProgramJournal.every(p => p.linked && p.shaders.every(s => s.compiled)));
  const scene = evidence.nativeProgramJournal[0].shaders.find(s => s.source.includes('uniform float constantAlphaValue;'));
  assert(scene?.source.includes('if(constantAlpha) color.a=constantAlphaValue;') &&
    scene.source.includes('if(textureTimesAlpha) color.rgb*=color.a;'),
  'The actual linked native-journal shader must multiply RGB by CC_ALPHA, not only blend alpha.');
  const frame = evidence.frames.find(f => f.name === 'natural-nonfire-ghosts-public-pause');
  assert(frame?.snapshot.ready && !frame.snapshot.failed && frame.publicPauseActive);
  const state = frame.snapshot.state, gpu = frame.gpu, target = evidence.target;
  assert.equal(state.phase, 'LiveBall'); assert.equal(state.ball.mode, 'InFlight');
  assert(['BallTrail', 'PlayerCloud'].includes(state.effects.selectedBallEffect));
  assert.equal(state.actors.length, 14);
  assert.equal(state.active, false);
  assert.deepEqual(frame.screenshotSourceTickBracket, [state.sourceTick, state.sourceTick],
    'A public-paused natural PNG must carry an honest stable source-tick bracket.');
  assert(gpu.ballTrailPixels > 0 && gpu.ballTrailColoredPixels > 0 && gpu.ballTrailNonWhitePixels > 0,
    'Ghost-only real geometry pixels must be visible and colored; body/white/blank pixels cannot substitute.');
  const expectedAlpha = [215, 200, 185, 170, 155], expectedScale = [2.25, 2.15, 2.05, 1.95, 1.85];
  assert.deepEqual(gpu.ballTrailMaterialDraws.map(d => d.alpha).sort((a, b) => b - a), expectedAlpha,
    'All five actual GPU material alpha stages must be present in the natural ghost chain.');
  for (const draw of gpu.ballTrailMaterialDraws) {
    const trail = state.ballTrails.find(t => t.objectSerial === draw.objectSerial);
    assert(trail && trail.visible && !trail.white && trail.localFlags === 0x40 && trail.alternate);
    assert.equal(trail.scale, expectedScale[trail.stage]);
    assert.equal(trail.alpha, expectedAlpha[trail.stage]);
    assert.equal(draw.mesh, 'original/free-ball/limb_medball_obj_big');
    assert.equal(draw.indices, 144);
    assert.equal(draw.texture, 'teams/runtime/HEDSHU2.WMS');
    assert.equal(draw.textureSha256, 'b59af47bf7baf6a2bcffacd4fb68af206229099102c00baf608d771f4ac5836e');
    assert.deepEqual(draw.gpuModel, Array.from(Float32Array.from(trail.packet.slice(4))));
    assert.equal(draw.gpuConstantAlphaValue, Math.fround(trail.alpha / 255),
      'Actual native-readback ghost alpha must match the source stage, not a forged metadata value.');
    assert(Boolean(draw.gpuConstantAlpha) && Boolean(draw.gpuTextureTimesAlpha) && draw.gpuWhiteAdd === 0 &&
      draw.gpuOriginalTextureBound && draw.gpuDepthTest && draw.gpuDepthWrite && draw.gpuDepthFunction === 'LESS' &&
      draw.gpuCullEnabled && draw.gpuCullFace === 'BACK' && draw.gpuFrontFace === 'CCW' &&
      draw.gpuBlendEnabled && draw.gpuSourceAlphaBlend);
  }
  const life = evidence.lifecycleRecords, entry = target.entry.sourceTick;
  assert.equal(life.birth.sourceTick, entry - 1);
  assert.equal(life.birth.line, 10361); assert.equal(life.birth.Detail, 'balltrl');
  assert.equal(life.birth.value, 0x900);
  assert.deepEqual(life.stages.map(r => r.value), expectedAlpha,
    'All five actual C# source-wake records are mandatory for the same natural football object.');
  assert.deepEqual(life.stages.map(r => r.sourceTick), [0, 1, 2, 3, 4].map(i => entry + i));
  assert.deepEqual(life.stages.map(r => r.line), [10553, 10558, 10561, 10564, 10567]);
  assert.deepEqual(life.sleeps.map(r => r.value), [1, 1, 1, 1, 1]);
  assert.deepEqual(life.sleeps.map(r => r.sourceTick), [0, 1, 2, 3, 4].map(i => entry + i));
  assert.equal(life.deletion.sourceTick, entry + 5); assert.equal(life.deletion.line, 10570);
  assert.equal(life.returned.sourceTick, entry + 5); assert.equal(life.returned.value, 1);
  assert([...life.stages, ...life.sleeps, life.deletion, life.returned].every(r =>
    r.ObjectSerial === target.objectSerial && r.ProcessSerial === target.processSerial && r.function === 'trail_ball_proc'));
  let later = false, disappeared = false;
  for (const row of evidence.observations.rows) {
    const s = row.snapshot.state, t = s.ballTrails.find(t => t.objectSerial === target.objectSerial);
    if (t) {
      assert.deepEqual(t.entry, target.entry); assert.deepEqual(t.position, target.position);
      assert.equal(t.heading, target.heading); assert.equal(t.elevation, target.elevation); assert.equal(t.roll, target.roll);
      later ||= s.sourceTick > entry && s.ball.position.some((v, i) => v !== state.ball.position[i]);
    } else if (s.sourceTick >= entry + 5) disappeared = true;
  }
  assert(later && disappeared, 'Same natural ghost must remain static while the real ball moves, then disappear.');
  assert(evidence.final.state.sourceTick >= state.sourceTick + 120,
    'At least120 actual source ticks of continuing gameplay are required after the ghost witness.');
  assert(evidence.final.state.actors.some((a, i) => a.poseDigest !== state.actors[i].poseDigest));
  for (const name of ['after-original-ghost-deletion', 'continuing-real-gameplay'])
    assert(evidence.frames.some(f => f.name === name && /^[a-f0-9]{64}$/.test(f.pngSha256)));
  const after = evidence.frames.find(f => f.name === 'after-original-ghost-deletion');
  assert(!after.snapshot.state.ballTrails.some(t => t.objectSerial === target.objectSerial) &&
    !after.gpu.ballTrailDraws.some(t => t.objectSerial === target.objectSerial),
  'The deleted natural football object must leave both the actual C# inventory and actual GPU draws.');
}
