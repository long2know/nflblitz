import assert from 'node:assert/strict';
import test from 'node:test';
import { pathToFileURL } from 'node:url';

const { SourceRenderer } = await import(process.env.SOURCE_PLAYER_RENDERER ?
  pathToFileURL(process.env.SOURCE_PLAYER_RENDERER) : new URL('./wwwroot/renderer.js', import.meta.url));
const mesh = 'original/loose-helmet/limb_phelm_obj', texture = 'teams/runtime/TESTLOD.WMS';
const hash = 'c'.repeat(64);
const asset = {
  mesh, sourceSha256: '03130e8e5dab04b4d5bd64290595b884bfb1e007440d9d228db9419f47614e5a',
  geometrySha256: 'd8c14967916b24b3135839680f1419c0af1a6aeceea34f3aa6a10d8127ff59c9',
  origin: 'original-phelm-SDL-factor1.2-authoring-origin-not-native-helm.h',
  uvOrigin: 'original-cvtmod-normalized-six-decimal-ST-ratio4-bottom-left',
  sourceScale: 1.2, sourceTriangleOrder: 'Renderer321', nativeGeneratedHeaderEquivalent: false,
};
const matrix = [0, 0, 2, 7, 0, 2, 0, 11, -2, 0, 0, 13];
const packet = [1, 37, 0, 255, 0, 0, -2, 0, 0, 2, 0, 0, 2, 0, 0, 0, 7, 11, 13, 1];
function helmet() {
  return {
    objectSerial: 71, processSerial: 51, actor: 5, sourceTick: 37, entrySourceTick: 3, cacheSourceTick: 2,
    objectId: 0x450, drawOrder: 0x50000, decalSlot: 2, integerAspect: 4, textureWidth: 128, textureHeight: 256,
    alpha: 255, scale: 2, pitchRate: 40, white: false, effectiveAlpha: 255, visible: true, hidden: false,
    roll: 2051, flightTime: 35, bounces: 2, bounceTime: 149, rawFlags: 0x20,
    texture, textureSha256: hash, worldMatrix: matrix.slice(), packet: packet.slice(),
  };
}
function styles() {
  return {
    frame: { enabled: true, sourceTick: 37, globalBallFlags: 0 },
    looseHelmets: [helmet()], helmetAsset: structuredClone(asset),
    hudConstantColor: 0xc0000000, hudCombiner: 'DECAL_TEXTURE_ADD_CCALPHA', hudHighlight: [],
  };
}
function fixture() {
  return { meshes: new Map([[mesh, { count: 267 }], ['limbs/limb_phead_obj/1', { count: 267 }]]),
    textures: new Map([[texture, { width: 128, height: 256, sha256: hash }]]) };
}
function receive(target, value = styles()) {
  SourceRenderer.prototype.effects.call(target, Array(16).fill(0), [], [], Array(14).fill(0), Array(14).fill(0),
    JSON.stringify(value));
}

test('helmet actual receiver: authoritative original PHELM and captured team decal2 must reach the effect frame', () => {
  const target = fixture(), value = styles();
  receive(target, value);
  assert.equal(target.fx.helmets.length, 1, 'Actual receiver must consume detached helmets, not leave a flag-only renderer.');
  assert.deepEqual(Array.from(target.fx.helmets[0].matrix), packet.slice(4));
  assert.equal(target.fx.helmets[0].metadata.texture, texture);
  assert.equal(target.fx.helmetAsset.mesh, mesh);
  for (const change of [
    s => { s.helmetAsset.mesh = 'limbs/limb_phead_obj/1'; },
    s => { s.helmetAsset.sourceScale = 1; },
    s => { s.helmetAsset.geometrySha256 = 'a'.repeat(64); },
    s => { s.helmetAsset.nativeGeneratedHeaderEquivalent = true; },
  ]) {
    const wrong = styles(); change(wrong);
    assert.throws(() => receive(fixture(), wrong), /Exact original PHELM authoring mesh/);
  }
});

test('helmet actual receiver: reject wrong team atlas, ratio, integer fields and detached/stale matrix components', () => {
  for (const change of [
    h => { h.textureSha256 = 'a'.repeat(64); }, h => { h.integerAspect = 3; }, h => { h.decalSlot = 1; },
    h => { h.roll = 1.5; }, h => { h.sourceTick = 36; }, h => { h.worldMatrix[3]++; },
    h => { h.packet[16]++; }, h => { h.packet[2] = 1; }, h => { h.white = true; },
    h => { h.texture = 'teams/runtime/HEDSHU.WMS'; },
  ]) {
    const wrong = styles(); change(wrong.looseHelmets[0]);
    assert.throws(() => receive(fixture(), wrong), /same-tick detached source fields/);
  }
  const duplicate = styles(); duplicate.looseHelmets.push(helmet());
  assert.throws(() => receive(fixture(), duplicate), /submitted exactly once/);
});

test('helmet actual receiver: raw roll and arbitrary old live flight counters are not normalized or given a TTL', () => {
  const target = fixture(), value = styles();
  value.looseHelmets[0].roll = 1234567;
  value.looseHelmets[0].flightTime = 1000000;
  receive(target, value);
  assert.equal(target.fx.helmets[0].metadata.roll, 1234567);
  assert.equal(target.fx.helmets[0].metadata.flightTime, 1000000);
  const hidden = styles();
  hidden.looseHelmets[0].hidden = true; hidden.looseHelmets[0].visible = false; hidden.looseHelmets[0].packet[0] = 0;
  receive(target, hidden);
  assert.equal(target.fx.helmets[0].metadata.visible, false);
  const disabled = styles(); disabled.frame.enabled = false;
  assert.throws(() => receive(fixture(), disabled), /Disabled source-fire cannot submit/);
});

function drawable() {
  const target = fixture(), calls = [], state = [];
  target.gl = new Proxy({}, { get: (_, key) => /^[A-Z_0-9]+$/.test(key) ? key : (...args) => state.push([key, ...args]) });
  receive(target);
  Object.assign(target, {
    sceneUniforms: { playerFlashAdd: 'white', constantAlphaValue: 'alpha', constantAlpha: 'ccAlpha', textureTimesAlpha: 'combine' },
    world: [], models: new Map([[0, Array.from({ length: 15 }, () => ({ drawEnabled: false }))]]),
    appearances: new Map([[0, { numberMatrix: 1, numberMesh: 'number', numberTexture: texture }]]),
    current: { camera: new Float32Array(16), joints: new Float32Array(14 * 15 * 16),
      models: Array(14).fill(0), appearances: Array(14).fill(0), ball: [0, 37, 0, 255] },
    draw: (mesh, texture, matrix, identity) => calls.push({ mesh, texture, matrix: Array.from(matrix), identity }),
  });
  return { target, calls, state };
}
test('helmet actual scene: detached original mesh draws while ball is held, with immutable flight matrix and source321', () => {
  const { target, calls, state } = drawable();
  SourceRenderer.prototype.drawScene.call(target, false);
  const actual = calls.find(c => c.mesh === mesh);
  assert(actual, 'Actual scene must draw detached PHELM even while the ball/carrier draw is hidden.');
  assert.equal(actual.texture, texture, 'Actual helmet draw must bind the captured team pdecal2, not a ball/skin material.');
  assert.deepEqual(actual.matrix, packet.slice(4), 'Actual helmet GPU matrix must retain all C# flight/spin components.');
  assert(state.some(c => c[0] === 'frontFace' && c[1] === 'CCW'), 'Actual PHELM source321 winding must reach GPU.');
  assert.equal(target.lastHelmetDraws[0].indices, 267);
  assert.equal(target.lastHelmetDraws[0].decalSlot, 2);
  assert.equal(target.lastHelmetDraws[0].integerAspect, 4);
});

test('helmet actual scene: CC alpha, depth LESS/write and separate alpha blend must not use dust state', () => {
  const { target, state } = drawable();
  SourceRenderer.prototype.drawLooseHelmets.call(target, false, false);
  for (const expected of [
    ['depthFunc', 'LESS'], ['depthMask', true], ['uniform1i', 'ccAlpha', true], ['uniform1i', 'combine', true],
    ['blendFuncSeparate', 'SRC_ALPHA', 'ONE_MINUS_SRC_ALPHA', 'ONE', 'ZERO'],
  ])
    assert(state.some(call => JSON.stringify(call) === JSON.stringify(expected)),
      'Actual helmet material must preserve source CC-alpha/LESS/write/separate alpha blend: ' + expected[0]);
  assert.equal(target.lastHelmetDraws[0].alpha, 255);
});

test('helmet actual scene: WHITE is current global ball flags, never helmet own flags or a renderer-side phase', () => {
  const { target, state } = drawable();
  target.fx.helmets[0].metadata.rawFlags = 1;
  SourceRenderer.prototype.drawLooseHelmets.call(target, false, false);
  assert.equal(target.lastHelmetDraws[0].white, false, 'Helmet own BF_WHITE must not whiten the draw.');
  assert(state.some(call => call[0] === 'uniform1f' && call[1] === 'white' && call[2] === 0));
  target.fx.metadata.globalBallFlags = 1;
  SourceRenderer.prototype.drawLooseHelmets.call(target, false, false);
  assert.equal(target.lastHelmetDraws[0].white, true, 'Current global ball BF_WHITE must reach the actual helmet material.');
  assert(state.some(call => call[0] === 'uniform1f' && call[1] === 'white' && call[2] === 1));
  target.fx.helmets[0].metadata.visible = false;
  SourceRenderer.prototype.drawLooseHelmets.call(target, false, false);
  assert.equal(target.lastHelmetDraws.length, 0, 'Source-hidden detached objects must not draw.');
});

test('helmet actual bridge: current selected actor texture and global ball state remain authenticated at scene submission', () => {
  const target = fixture();
  target.world = [{}]; target.ball = {};
  target.appearances = new Map([[0, { textures: ['unused', 'unused', texture] }]]);
  Object.assign(target, { drawScene() {}, drawEffects() {}, drawHud() {}, assertHealthy() {}, frameCount: 0 });
  receive(target);
  const ball = [0, 37, 0, 255, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1];
  const status = { sourceTick: 37, ball: { sourceTick: 37, rawFlags: 0, visible: false,
    worldMatrix: [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0] } };
  const present = () => SourceRenderer.prototype.present.call(target, Array(16).fill(0), Array(14 * 15 * 16).fill(0),
    Array(14).fill(0), Array(14).fill(0), ball, [], [], [], [], JSON.stringify(status));
  present();
  target.appearances.get(0).textures[2] = 'teams/runtime/OTHERLOD.WMS';
  assert.throws(present, /selected actor team decal2 and current global ball flags/);
  target.appearances.get(0).textures[2] = texture; target.fx.metadata.globalBallFlags = 1;
  assert.throws(present, /selected actor team decal2 and current global ball flags/);
});
