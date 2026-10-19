import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import test from 'node:test';
import { pathToFileURL } from 'node:url';

const source = process.env.SOURCE_PLAYER_RENDERER ?
  pathToFileURL(process.env.SOURCE_PLAYER_RENDERER) : new URL('./wwwroot/renderer.js', import.meta.url);
const { SourceRenderer } = await import(source);
const code = await readFile(source, 'utf8');
const projection = [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1];
const metadata = (enabled, highlight = []) => JSON.stringify({
  frame: { enabled, cull: 0 }, hudHighlight: highlight,
  hudConstantColor: 0xc0000000, hudCombiner: 'DECAL_TEXTURE_ADD_CCALPHA',
});
function effects(enabled, frames = [], flags = Array(14).fill(0), highlight = []) {
  const target = { textures: new Map(Array.from({ length: 17 }, (_, i) => [`fire/${i}`, { texture: i }])) };
  SourceRenderer.prototype.effects.call(target, projection, Array(frames.length * 30).fill(0),
    frames, flags, flags.map(raw => raw & 0x400 ? 192 / 255 : 0), metadata(enabled, highlight));
  return target;
}
function recordedGl() {
  const calls = [];
  const gl = new Proxy({}, { get: (_, key) => /^[A-Z_0-9]+$/.test(key) ? key :
    (...args) => { calls.push([key, ...args]); } });
  return { gl, calls };
}

test('adapter-only: actual effects receiver preserves selected originals and raw0x400, rejecting substitutes', () => {
  const flags = Array(14).fill(0); flags[6] = 0x1400;
  const target = effects(true, [7, 16], flags, [2]);
  assert.deepEqual(target.fx.textures, [7, 16]);
  assert.equal(target.fx.flashAlpha[6], 192 / 255);
  assert.equal(target.fx.flashAlpha[5], 0);
  assert.deepEqual([...target.fx.hudHighlight], [2]);
  assert.throws(() => SourceRenderer.prototype.effects.call(target, projection, Array(30).fill(0),
    [17], flags, target.fx.flashAlpha, metadata(true)),     /unbound original TRAIL\/DIRT frame/);
  assert.throws(() => SourceRenderer.prototype.effects.call(target, projection, [], [],
    Array(14).fill(0), Array(14).fill(192 / 255), metadata(false)), /Only actual raw0x400/);
  assert.throws(() => SourceRenderer.prototype.effects.call(target, projection, [], [],
    Array(14).fill(0), Array(14).fill(0), JSON.stringify({ frame: { enabled: false } })), /meter primitive\/material packet/);
  assert.throws(() => SourceRenderer.prototype.effects.call(target, projection, Array(30).fill(0), [0],
    Array(14).fill(0), Array(14).fill(0), metadata(false)), /Disabled Core fire/);
});

test('adapter-only: disabled effects never issue a particle draw', () => {
  const target = effects(false);
  const { gl, calls } = recordedGl(); target.gl = gl;
  SourceRenderer.prototype.drawEffects.call(target, false);
  assert.equal(calls.length, 0);
});

test('adapter-only: actual fire draw binds selected frames, camera vertices and separate source-alpha blends', () => {
  const target = effects(true, [7, 16]);
  const { gl, calls } = recordedGl(); target.gl = gl;
  target.fire = 'actual-fire-program'; target.fireVao = 'fire-vao'; target.fireBuffer = 'fire-vbo';
  target.fireUniforms = { sourceProjection: 'projection', proofMask: 'mask', atlas: 'atlas' };
  SourceRenderer.prototype.drawEffects.call(target, false);
  assert.deepEqual(calls.filter(c => c[0] === 'bindTexture').map(c => c[2]), [7, 16]);
  assert.deepEqual(calls.find(c => c[0] === 'blendFuncSeparate'),
    ['blendFuncSeparate', 'SRC_ALPHA', 'ONE_MINUS_SRC_ALPHA', 'SRC_ALPHA', 'ONE_MINUS_SRC_ALPHA']);
  assert.deepEqual(calls.filter(c => c[0] === 'drawArrays'),
    [['drawArrays', 'TRIANGLES', 0, 6], ['drawArrays', 'TRIANGLES', 6, 6]]);
  assert(calls.some(c => c[0] === 'depthFunc' && c[1] === 'ALWAYS'));
  assert(calls.some(c => c[0] === 'disable' && c[1] === 'CULL_FACE'));
  assert.equal(calls.find(c => c[0] === 'uniformMatrix4fv')[3], target.fx.projection);
});

test('adapter-only: original-atlas HUD batches split for additive192 highlight and explicitly restore base', () => {
  const target = effects(false, [], Array(14).fill(0), [1]);
  const { gl, calls } = recordedGl(); target.gl = gl;
  target.canvas = { getBoundingClientRect: () => ({ width: 512, height: 384 }) };
  target.hudUniforms = { contentSize: 'size', atlas: 'atlas', highlightAdd: 'highlight' };
  target.textures.set('interface', { texture: 'original-atlas' });
  target.current = { hudCounts: [3, 3, 3], hudColors: [0xffffffff, 0xffffffff, 0xffffffff],
    hudTextures: ['interface', 'interface', 'interface'], hudVertices: Array(36).fill(0) };
  SourceRenderer.prototype.drawHud.call(target);
  assert.deepEqual(calls.filter(c => c[0] === 'uniform1f'), [
    ['uniform1f', 'highlight', 0], ['uniform1f', 'highlight', 192 / 255], ['uniform1f', 'highlight', 0],
  ], 'Actual HUD batching must preserve source additive192 highlight and base restoration.');
  assert.deepEqual(calls.filter(c => c[0] === 'drawArrays').map(c => c.slice(2)), [[0, 3], [3, 3], [6, 3]]);
});

test('actual linked shader bodies retain source flash/additive HUD and camera-space FX consumers', () => {
  assert.match(code, /color\.rgb=clamp\(color\.rgb\+playerFlashAdd,0\.0,1\.0\)/,
    'Actual player shader must add source CCALPHA192, not multiply/tint/ignore its original texture.');
  assert.match(code, /pixel=vec4\(clamp\(color\.rgb\+highlightAdd,0\.0,1\.0\),color\.a\)/,
    'Actual HUD shader must add source CCALPHA192 and preserve original alpha.');
  assert.match(code, /gl_Position=sourceProjection\*vec4\(position,1\.0\)/);
  assert.match(code, /vec4\(texel\.rgb\*texel\.a,texel\.a\)/);
  assert.match(code, /this\.scene = program\(gl, sceneVertex, sceneFragment\)/);
  assert.match(code, /this\.hud = program\(gl, hudVertex, hudFragment\)/);
  assert.match(code, /this\.fire = program\(gl, fireVertex, fireFragment\)/);
});
