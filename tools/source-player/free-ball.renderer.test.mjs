import assert from 'node:assert/strict';
import test from 'node:test';
import { pathToFileURL } from 'node:url';
const { SourceRenderer } = await import(process.env.SOURCE_PLAYER_RENDERER ?
  pathToFileURL(process.env.SOURCE_PLAYER_RENDERER) : new URL('./wwwroot/renderer.js', import.meta.url));

const mesh = 'original/free-ball/limb_medball_obj_big', primary = 'teams/runtime/HEDSHU.WMS',
  alternate = 'teams/runtime/HEDSHU2.WMS';
const metadata = {
  origin: 'original-medball2-SDL-source-authored-not-native-medball2.h',
  sourceSha256: 'd54218dd7d0fc45fe7ad7072cb39b12913dfed80687999f69d5e93a269bc9704',
  geometrySha256: '22b92aa3f0b113be4c18edef70a6503fac448c1d85ff187b84b68c23b8d2eada',
  textureSha256: 'b2a5795cc67286d6a9a1c60c21f92f393134ab81f18dedfad84ffe405edb3fa3',
  alternateTextureSha256: 'b59af47bf7baf6a2bcffacd4fb68af206229099102c00baf608d771f4ac5836e',
  sourcePivot: 'authoring-origin-unchanged', sourceBasis: 'original-source-XYZ',
  sourceTriangleOrder: 'Renderer321', rawTextureCode: 0, sourceScale: 1, nativeGeneratedHeaderEquivalent: false,
};
function fixture() {
  return { meshes: new Map([[mesh, { count: 144 }]]),
    textures: new Map([[primary, { sha256: metadata.textureSha256 }], [alternate, { sha256: metadata.alternateTextureSha256 }]]) };
}

test('synthetic receiver: require exact free-ball model, source origin and HEDSHU, rejecting held-ball substitutes', () => {
  const target = fixture();
  SourceRenderer.prototype.freeBall.call(target, mesh, primary, alternate, JSON.stringify(metadata));
  assert.equal(target.ball.mesh, mesh);
  for (const mutate of [
    m => { m.sourcePivot = 'recentered'; }, m => { m.sourceScale = 1.2; },
    m => { m.rawTextureCode = 24; }, m => { m.geometrySha256 = 'a'.repeat(64); },
    m => { m.textureSha256 = 'a'.repeat(64); }, m => { m.nativeGeneratedHeaderEquivalent = true; },
  ]) {
    const changed = structuredClone(metadata); mutate(changed);
    assert.throws(() => SourceRenderer.prototype.freeBall.call(fixture(), mesh, primary, alternate, JSON.stringify(changed)),
      /Exact source medball2 geometry/);
  }
  assert.throws(() => SourceRenderer.prototype.freeBall.call(fixture(), 'hand001b', primary, alternate, JSON.stringify(metadata)),
    /Exact source medball2 geometry/);
});

test('synthetic actual scene: submitted flight matrix, source321 winding and raw alternate/white reach the real draw', () => {
  const target = fixture(), calls = [], state = [];
  target.gl = new Proxy({}, { get: (_, key) => /^[A-Z_0-9]+$/.test(key) ? key : (...args) => state.push([key, ...args]) });
  const matrix = Float32Array.from([0, 2, 0, 0, 0, 0, -2, 0, -2, 0, 0, 0, 7, 11, 13, 1]);
  Object.assign(target, { sceneUniforms: { playerFlashAdd: 'white' }, world: [],
    models: new Map([[0, Array.from({ length: 15 }, () => ({ drawEnabled: false }))]]),
    appearances: new Map([[0, { numberMatrix: 1, numberMesh: 'number', numberTexture: primary }]]),
    current: { camera: new Float32Array(16), joints: new Float32Array(14 * 15 * 16),
      models: Array(14).fill(0), appearances: Array(14).fill(0), ball: [1, 37, 0, 255], ballMatrix: matrix },
    draw: (mesh, texture, matrix, identity) => calls.push({ mesh, texture, matrix: Array.from(matrix), identity }),
  });
  SourceRenderer.prototype.freeBall.call(target, mesh, primary, alternate, JSON.stringify(metadata));
  SourceRenderer.prototype.drawScene.call(target, false);
  let actual = calls.find(c => c.mesh === mesh);
  assert.deepEqual(actual.matrix, Array.from(matrix), 'Actual scene must submit the C# source flight/spin matrix, not identity or cosmetic rotation.');
  assert.equal(actual.texture, primary);
  assert(state.some(c => c[0] === 'frontFace' && c[1] === 'CCW'), 'Actual free-ball source321 winding must reach GPU culling.');
  assert(state.some(c => c[0] === 'blendFuncSeparate' && c.slice(1).join() === 'SRC_ALPHA,ONE_MINUS_SRC_ALPHA,ONE,ZERO'));
  calls.length = 0; target.current.ball[2] = 0x41;
  SourceRenderer.prototype.drawScene.call(target, false);
  actual = calls.find(c => c.mesh === mesh);
  assert.equal(actual.texture, alternate, 'Actual free-ball alternate bit must select the original HEDSHU2 decal.');
  assert(state.some(c => c[0] === 'uniform1f' && c[1] === 'white' && c[2] === 1));
  calls.length = 0; target.current.ball[0] = 0;
  SourceRenderer.prototype.drawScene.call(target, false);
  assert(!calls.some(c => c.mesh === mesh), 'Held-ball actor geometry must not acquire a duplicate separate free ball.');
  assert.equal(calls.length, 14, 'All fourteen retained actors must still draw when the free ball is hidden.');
});

test('synthetic actual bridge: reject stale tick or detached affine components before submitting a scene', () => {
  const world = [0, 0, 2, 7, 0, 2, 0, 11, -2, 0, 0, 13];
  const packet = [1, 37, 0, 255, 0, 0, -2, 0, 0, 2, 0, 0, 2, 0, 0, 0, 7, 11, 13, 1];
  const target = { world: [{}], ball: {}, drawScene() {}, drawEffects() {}, drawHud() {}, assertHealthy() {}, frameCount: 0 };
  const status = JSON.stringify({ sourceTick: 37, ball: { sourceTick: 37, rawFlags: 0, visible: true, worldMatrix: world } });
  const present = (ball, header = status) => SourceRenderer.prototype.present.call(target, Array(16).fill(0), Array(14 * 15 * 16).fill(0),
    Array(14).fill(0), Array(14).fill(0), ball, [], [], [], [], header);
  present(packet);
  assert.deepEqual(Array.from(target.current.ballMatrix), packet.slice(4));
  const serializedFloat = JSON.parse(status), exactMarshalledFloat = packet.slice();
  serializedFloat.ball.worldMatrix[0] = 0.12345679;
  exactMarshalledFloat[4] = Math.fround(0.12345679);
  assert.doesNotThrow(() => present(exactMarshalledFloat, JSON.stringify(serializedFloat)),
    'JSON shortest-roundtrip C# float and exact double-marshalled binary32 must compare in the actual GPU domain.');
  assert.equal(target.current.ballMatrix[0], Math.fround(serializedFloat.ball.worldMatrix[0]),
    'JSON shortest-roundtrip C# float and exact double-marshalled binary32 must compare in the actual GPU domain.');
  for (const index of [1, 2, 7, 12, 16]) {
    const changed = packet.slice(); changed[index]++;
    assert.throws(() => present(changed), /same-source-tick immutable rotation\/scale\/pivot matrix/);
  }
});
