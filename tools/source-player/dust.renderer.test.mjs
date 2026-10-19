import assert from 'node:assert/strict';
import test from 'node:test';
import { pathToFileURL } from 'node:url';

const source = process.env.SOURCE_PLAYER_RENDERER ?
  pathToFileURL(process.env.SOURCE_PLAYER_RENDERER) : new URL('./wwwroot/renderer.js', import.meta.url);
const { SourceRenderer } = await import(source);

function receive(slots = [17, 19, 26]) {
  const target = { textures: new Map(Array.from({ length: 27 }, (_, i) => [`fire/${i}`, { texture: `DIRT-slot-${i}` }])) };
  SourceRenderer.prototype.effects.call(target, Array(16).fill(0), Array(slots.length * 30).fill(0), slots,
    Array(14).fill(0), Array(14).fill(0), JSON.stringify({
      frame: { enabled: true, cull: 0, dustDraws: slots.length },
      hudConstantColor: 0xc0000000, hudCombiner: 'DECAL_TEXTURE_ADD_CCALPHA', hudHighlight: [],
    }));
  return target;
}

test('actual dust receiver accepts the complete ordered DIRT0..9 slots without relabelling TRAIL', () => {
  let target;
  assert.doesNotThrow(() => { target = receive(); }, 'All ten original DIRT slots must be accepted by the actual renderer.');
  assert.deepEqual(target.fx.textures, [17, 19, 26], 'All queued source dust objects must retain their selected original frame slots.');
  for (const slot of [-1, 27, 1.5]) assert.throws(() => receive([slot]), /unbound original TRAIL\/DIRT frame/);
  const missing = receive();
  missing.textures.delete('fire/26');
  assert.throws(() => SourceRenderer.prototype.effects.call(missing, Array(16).fill(0), Array(30).fill(0), [26],
    Array(14).fill(0), Array(14).fill(0), JSON.stringify({
      frame: { enabled: true }, hudConstantColor: 0xc0000000, hudCombiner: 'DECAL_TEXTURE_ADD_CCALPHA', hudHighlight: [],
    })), /unbound original TRAIL\/DIRT frame/);
});

test('actual dust draw calls use DIRT identities, source order, ALWAYS/write and separate SRC_ALPHA blends', () => {
  const target = receive(), calls = [];
  target.gl = new Proxy({}, { get: (_, name) => /^[A-Z_0-9]+$/.test(name) ? name : (...args) => calls.push([name, ...args]) });
  Object.assign(target, { fire: 'shared-original-draw-spfx', fireBuffer: 'buffer', fireVao: 'vao',
    fireUniforms: { sourceProjection: 'projection', atlas: 'atlas', proofMask: 'mask' } });
  SourceRenderer.prototype.drawEffects.call(target, false);
  assert.deepEqual(calls.filter(c => c[0] === 'bindTexture').map(c => c[2]),
    ['DIRT-slot-17', 'DIRT-slot-19', 'DIRT-slot-26'],
    'Every actual dust draw must bind its original DIRT frame, never TRAIL0 or latest-only.');
  assert.deepEqual(calls.filter(c => c[0] === 'drawArrays').map(c => c.slice(1)),
    [['TRIANGLES', 0, 6], ['TRIANGLES', 6, 6], ['TRIANGLES', 12, 6]],
    'Every actual source dust object must reach a separate ordered draw.');
  assert(calls.some(c => c[0] === 'depthFunc' && c[1] === 'ALWAYS'), 'Original dust draw must use depth ALWAYS.');
  assert(calls.some(c => c[0] === 'depthMask' && c[1] === true), 'Original dust draw must write depth.');
  assert(calls.some(c => c[0] === 'blendFuncSeparate' &&
    c.slice(1).join() === 'SRC_ALPHA,ONE_MINUS_SRC_ALPHA,SRC_ALPHA,ONE_MINUS_SRC_ALPHA'),
  'Original dust draw must apply SRC_ALPHA to both RGB and alpha blend sources.');
});

test('actual dust deletion packet clears prior objects and cannot invent renderer-side particles', () => {
  const target = receive([]);
  const calls = [];
  target.gl = new Proxy({}, { get: (_, name) => (...args) => calls.push([name, ...args]) });
  SourceRenderer.prototype.drawEffects.call(target, false);
  assert.equal(calls.length, 0, 'An empty real source dust packet must not retain or synthesize any draw.');
});
