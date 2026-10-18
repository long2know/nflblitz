import assert from 'node:assert/strict';
import test from 'node:test';
import { pathToFileURL } from 'node:url';

const { SourceRenderer } = await import(process.env.SOURCE_PLAYER_RENDERER ?
  pathToFileURL(process.env.SOURCE_PLAYER_RENDERER) : new URL('./wwwroot/renderer.js', import.meta.url));
const sourceSha = '3fb4e566b57670e397fde16c6f515d06c95379420074c2d6c7b111bca843e257';
const geometrySha = 'f835cda5498db7b3f2783b3cfda99d07a2ef765f821f1655c1b4be3d555a57b5';

function fixture() {
  const textures = Array.from({ length: 7 }, (_, slot) => `original-slot-${slot}`);
  const hash = 'a'.repeat(64);
  const target = {
    textures: new Map(textures.map(id => [id, { sha256: hash }])),
    meshes: new Map([['numbers', {}], ['limbs/limb_phead_obj/1', {}]]),
    appearances: new Map(),
  };
  const appearance = { tint: 'none', textures, textureHashes: textures.map(() => hash),
    numberMesh: 'numbers', numberTexture: textures[5], numberMatrix: 1,
    headJoint: 8, headVariant: 'original-phead-source-authored', headSourceSha256: sourceSha,
    headGeometrySha256: geometrySha, headParts: [{ mesh: 'limbs/limb_phead_obj/1', slot: 1 }] };
  return { target, appearance };
}

test('synthetic renderer contract: actual appearance receiver rejects wrong original head geometry/slot/joint/provenance', () => {
  const { target, appearance } = fixture();
  SourceRenderer.prototype.appearance.call(target, 0, JSON.stringify(appearance));
  assert.equal(target.appearances.get(0).headParts[0].slot, 1);
  for (const mutate of [
    a => { a.headJoint = 7; },
    a => { a.headParts[0].slot = 4; },
    a => { a.headParts[0].mesh = 'generic-head'; },
    a => { a.headSourceSha256 = '0'.repeat(64); },
    a => { a.headGeometrySha256 = '0'.repeat(64); },
  ]) {
    const changed = structuredClone(appearance); mutate(changed);
    assert.throws(() => SourceRenderer.prototype.appearance.call(target, 1, JSON.stringify(changed)),
      /Original popped-head binding/);
  }
});

test('synthetic renderer contract: real scene draw uses the original head submesh/skin and head-only identity, not body-head fallback', () => {
  const { target, appearance } = fixture();
  const calls = [], gl = new Proxy({}, { get: (_, key) => /^[A-Z_0-9]+$/.test(key) ? key : () => {} });
  Object.assign(target, { gl, sceneUniforms: {}, world: [], fx: { flashAlpha: Array(14).fill(0) },
    models: new Map([[0, Array.from({ length: 15 }, (_, joint) => ({
      drawEnabled: true, parts: [{ mesh: `body-${joint}`, slot: 0 }],
    }))]]),
    current: { models: Array(14).fill(0), appearances: Array(14).fill(0),
      joints: new Float32Array(14 * 15 * 16), camera: new Float32Array(16), ball: [0] },
    draw: (mesh, texture, matrix, identity) => calls.push({ mesh, texture, matrix, identity }),
  });
  target.appearances.set(0, appearance);
  SourceRenderer.prototype.drawScene.call(target, true, true);
  const heads = calls.filter(c => c.mesh === 'limbs/limb_phead_obj/1');
  assert.equal(heads.length, 14, 'Actual scene draw must replace only the neck with phead, never fall back to body-head geometry.');
  assert(heads.every((call, actor) => call.texture === appearance.textures[1] && call.identity === actor + 1));
  assert.equal(calls.some(c => c.mesh === 'body-8'), false);
  assert(calls.filter(c => c.mesh !== 'limbs/limb_phead_obj/1').every(c => c.identity === 0),
    'The GPU head-only identity pass must not count a torso/number as visible original phead geometry.');
});
