import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import test from 'node:test';
import { pathToFileURL } from 'node:url';

const source = process.env.SOURCE_PLAYER_RENDERER ?
  pathToFileURL(process.env.SOURCE_PLAYER_RENDERER) : new URL('./wwwroot/renderer.js', import.meta.url);
const { SourceRenderer } = await import(source);
const code = await readFile(source, 'utf8');
const mesh = 'original/free-ball/limb_medball_obj_big', primary = 'teams/runtime/HEDSHU.WMS',
  alternate = 'teams/runtime/HEDSHU2.WMS';
const scales = [2.25, 2.15, 2.05, 1.95, 1.85];
const packet = (world, flags, alpha, visible = true) => {
  const value = [Number(visible), 37, flags, alpha, ...Array(16).fill(0)];
  for (let i = 0; i < 12; i++) value[4 + i % 4 * 4 + Math.floor(i / 4)] = world[i];
  value[19] = 1;
  return value;
};
function frame(white = false, carrier = -1) {
  const world = [1, 0, 0, 7, 0, 1, 0, 11, 0, 0, 1, 13], visible = carrier === -1;
  const ball = { sourceTick: 37, rawFlags: white ? 1 : 0, carrier, visible, pupBigBall: 0, worldMatrix: world };
  const main = packet(world, ball.rawFlags, 255, visible);
  const ballTrails = scales.map((scale, stage) => {
    const matrix = [scale, 0, 0, 2, 0, scale, 0, 3, 0, 0, scale, 4];
    return { objectSerial: 71 + stage, processSerial: 101 + stage, sourceTick: 37,
      stage, hidden: false, objectId: 0x400, drawOrder: 0x50000, localFlags: 0x40, visible,
      position: [2, 3, 4], heading: 208, elevation: -292, roll: 1085, scale, pupBigBall: 0,
      rawFlags: 0x40 | ball.rawFlags, alpha: 215 - 15 * stage,
      effectiveAlpha: white ? 255 : 215 - 15 * stage, white, alternate: true,
      entry: { sourceTick: 35, position: [2, 3, 4], heading: 208, elevation: -292, roll: 1085 },
      worldMatrix: matrix, packet: packet(matrix, 0x40 | ball.rawFlags, 215 - 15 * stage, visible) };
  });
  return { status: { sourceTick: 37, ball, ballTrails }, main, combined: main.concat(...ballTrails.map(t => t.packet)) };
}
function receive(fixture = frame()) {
  const target = { world: [{}], ball: {}, drawScene() {}, drawEffects() {}, drawHud() {}, assertHealthy() {}, frameCount: 0 };
  SourceRenderer.prototype.present.call(target, Array(16).fill(0), Array(14 * 15 * 16).fill(0),
    Array(14).fill(0), Array(14).fill(0), fixture.combined, [], [], [], [], JSON.stringify(fixture.status));
  return target;
}

test('synthetic actual receiver: complete ghost packets preserve main20 and immutable source-entry matrices', () => {
  const fixture = frame(), target = receive(fixture);
  assert.deepEqual(target.current.ball, fixture.main, 'Main-ball20 must remain unchanged when football ghosts are appended.');
  assert.equal(target.current.ballTrails.length, 5, 'All actual C# trail packets must reach the renderer, not only a selector/count.');
  for (let i = 0; i < 5; i++)
    assert.deepEqual(Array.from(target.current.ballTrails[i].matrix),
      Array.from(Float32Array.from(fixture.status.ballTrails[i].packet.slice(4))));
  for (const mutate of [
    f => { f.status.ballTrails[0].heading++; },
    f => { f.status.ballTrails[0].localFlags = 0x840; },
    f => { f.status.ballTrails[0].scale = 1; },
    f => { f.status.ballTrails[0].alpha = 255; },
    f => { f.status.ballTrails[0].effectiveAlpha = 255; },
    f => { f.status.ballTrails[0].entry.sourceTick = 38; },
    f => { f.status.ballTrails[0].sourceTick = 36; },
    f => { f.status.ballTrails[0].position[0]++; },
    f => { f.status.ballTrails[0].hidden = true; },
    f => { f.combined[24] += .1; },
  ]) {
    const changed = frame(); mutate(changed);
    assert.throws(() => receive(changed), /Football-trail GPU packet must retain/);
  }
  const held = receive(frame(false, 3));
  assert(held.current.ballTrails.every(t => t.ball[0] === 0), 'Current global carrier must suppress every local ghost draw.');
  const white = receive(frame(true));
  assert(white.current.ballTrails.every(t => t.ball[3] < 255 && t.metadata.effectiveAlpha === 255),
    'White must retain local stage metadata but override the actual GLIDE3 CC_ALPHA withFF.');
});

function drawFixture(white = false, carrier = -1) {
  const target = receive(frame(white, carrier)), calls = [], uniforms = new Map(), state = [];
  target.gl = new Proxy({}, { get: (_, name) => /^[A-Z_0-9]+$/.test(name) ? name : (...args) => {
    state.push([name, ...args]);
    if (name === 'uniform1f' || name === 'uniform1i') uniforms.set(args[0], args[1]);
  } });
  Object.assign(target, { scene: 'actual-scene-program',
    sceneUniforms: { playerFlashAdd: 'white', constantAlphaValue: 'alpha', constantAlpha: 'constant', textureTimesAlpha: 'times' },
    world: [], meshes: new Map([[mesh, { count: 144 }]]),
    ball: { mesh, texture: primary, alternateTexture: alternate },
    textures: new Map([[primary, { sha256: 'a'.repeat(64) }], [alternate, { sha256: 'b'.repeat(64) }]]),
    models: new Map([[0, Array.from({ length: 15 }, () => ({ drawEnabled: false }))]]),
    appearances: new Map([[0, { numberMatrix: 1, numberMesh: 'number', numberTexture: primary }]]),
    draw: (mesh, texture, matrix, identity) => calls.push({
      mesh, texture, matrix: Array.from(matrix), identity, uniforms: Object.fromEntries(uniforms) }),
  });
  SourceRenderer.prototype.drawScene.call(target, false);
  return { target, calls: calls.filter(c => c.mesh === mesh), state };
}

test('synthetic actual scene: original mesh/decal, arrival order, five constant alphas and source blend/depth reach draws', () => {
  const { target, calls, state } = drawFixture();
  assert.equal(calls.length, 6, 'The actual scene must draw the main football and every source football ghost.');
  assert.equal(calls[0].texture, primary, 'Main-ball primary HEDSHU must remain unchanged.');
  assert.equal(calls[0].uniforms.alpha, 1, 'Main-ball alpha255 must remain unchanged.');
  for (let i = 0; i < 5; i++) {
    const draw = calls[i + 1], trail = target.current.ballTrails[i];
    assert.equal(draw.texture, alternate, 'Every actual ghost must bind original HEDSHU2, not primary/fire/generic material.');
    assert.deepEqual(draw.matrix, Array.from(trail.matrix), 'Every ghost must submit its retained source matrix, not identity/live main tracking.');
    assert.equal(draw.uniforms.alpha, (215 - 15 * i) / 255, 'Actual trail material must use its constant source alpha stage.');
    assert.equal(draw.uniforms.constant, true);
    assert.equal(draw.uniforms.times, true, 'Actual trail RGB must be texture-times-alpha, in addition to source-alpha blending.');
    assert.equal(draw.uniforms.white, 0, 'Ordinary ghosts must not be falsely solid-white.');
  }
  assert.deepEqual(target.lastBallTrailDraws.map(t => t.objectSerial), [71, 72, 73, 74, 75],
    'Equal DRAWORDER_BALL must retain arrival order, not scale/distance sorting.');
  assert(state.some(c => c[0] === 'frontFace' && c[1] === 'CCW'));
  assert(state.some(c => c[0] === 'depthMask' && c[1] === true));
  assert(state.some(c => c[0] === 'depthFunc' && c[1] === 'LESS'));
  assert(state.some(c => c[0] === 'blendFuncSeparate' &&
    c.slice(1).join() === 'SRC_ALPHA,ONE_MINUS_SRC_ALPHA,ONE,ZERO'));
  assert.equal(drawFixture(false, 3).calls.length, 0, 'Held global gate must suppress main and all ghosts without deleting actors.');
});

test('synthetic actual scene: current global White overrides local fade with opaque white for every ball object', () => {
  const { calls } = drawFixture(true);
  assert.equal(calls.length, 6);
  assert(calls.every(c => c.uniforms.alpha === 1 && c.uniforms.white === 1),
    'GLIDE3 global White must overwrite CC_ALPHA withFF, never retain an invented ghost fade.');
});

test('actual shader-body contract: CC_ALPHA affects both RGB multiplication and blend alpha; Unity uses the same white override', async () => {
  assert.match(code, /if\(constantAlpha\) color\.a=constantAlphaValue;/,
    'Actual linked scene shader must use the submitted constant alpha, not texture alpha or opaque1.');
  const rgb = /if\(textureTimesAlpha\) color\.rgb\*=([^;]+);/.exec(code);
  assert(rgb, 'Actual linked scene shader must multiply RGB by the selected alpha before blending.');
  for (const alphaByte of [215, 200, 185, 170, 155]) {
    const alpha = alphaByte / 255;
    const factor = rgb[1] === 'color.a' ? alpha : Number(rgb[1]);
    for (const texel of [.07, .31, .8])
      assert.equal(texel * factor * alpha, texel * alpha * alpha,
        'Native API contract is texture-times-CC-alpha THEN SRC_ALPHA blend, not only translucent texture.');
  }
  const unity = await readFile(new URL('../../Assets/Scripts/Presentation/SourceFreeBall.shader', import.meta.url), 'utf8');
  assert.match(unity, /float alpha = _BallWhite > 0 \? 1 : _BallAlpha;/);
  assert.match(unity, /color \* alpha, alpha/);
  assert.match(unity, /Blend SrcAlpha OneMinusSrcAlpha, One Zero/);
  assert.match(unity, /ZWrite On[\s\S]*ZTest Less/);
});
