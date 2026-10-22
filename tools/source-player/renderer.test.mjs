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

function playerScene(modelId, hiddenJoint = -1) {
  const { gl, calls } = recordedGl(), draws = [];
  const target = {
    gl, scene: 'scene', sceneUniforms: Object.fromEntries(
      ['model', 'viewProjection', 'cutoff', 'constantAlpha', 'textureTimesAlpha', 'playerFlashAdd']
        .map(name => [name, name])),
    fx: { flashAlpha: Array.from({ length: 14 }, (_, actor) => actor % 3 ? 0 : 192 / 255), helmets: [] },
    current: { camera: projection, joints: new Float32Array(14 * 15 * 16),
      models: Array(14).fill(modelId), appearances: Array.from({ length: 14 }, (_, i) => i), ball: [0] },
    models: new Map([[modelId, Array.from({ length: 15 }, (_, joint) => ({
      drawEnabled: joint !== hiddenJoint, parts: [
        { mesh: `joint/${joint}`, slot: joint >= 2 && joint <= 7 ? 0 : joint === 8 ? 1 : 2 },
        ...([2, 5].includes(joint) ? [{ mesh: `sleeve/${joint}`, slot: 4 }] : []),
      ],
    }))]]),
    appearances: new Map(Array.from({ length: 14 }, (_, actor) => [actor, {
      textures: [actor % 2 ? 'ARMHND' : 'ARMHND2', 'HEDSHU', 'JERSF', 'LEGHEL', 'SLVMSK', 'JNUM', 'BALL'],
      headJoint: 8, numberMatrix: 1,
      numberMesh: 'numbers', numberTexture: 'JNUM',
    }])),
    world: [{ mesh: 'world', texture: 'world-cutout', state: {
      depthFunction: 'GR_CMP_LESS', alphaFunction: 'GR_CMP_GREATER', alphaValue: 100,
      alphaSource: 'GR_ALPHASOURCE_TEXTURE_ALPHA', colorFunction: 'GR_COLORCOMBINE_DECAL_TEXTURE',
      rgbSource: 'GR_BLEND_ONE', rgbDestination: 'GR_BLEND_ZERO',
      alphaSourceBlend: 'GR_BLEND_ONE', alphaDestinationBlend: 'GR_BLEND_ZERO',
    } }],
    blend: SourceRenderer.prototype.blend,
    draw(mesh, texture, matrix, idColor) {
      draws.push({ mesh, texture, matrix, idColor,
        cutoff: calls.findLast(c => c[0] === 'uniform1f' && c[1] === 'cutoff')?.[2],
        flash: calls.findLast(c => c[0] === 'uniform1f' && c[1] === 'playerFlashAdd')?.[2],
        constantAlpha: calls.findLast(c => c[0] === 'uniform1i' && c[1] === 'constantAlpha')?.[2],
        blend: calls.findLast(c => ['enable', 'disable'].includes(c[0]) && c[1] === gl.BLEND)?.[0],
        cull: calls.findLast(c => ['enable', 'disable'].includes(c[0]) && c[1] === gl.CULL_FACE)?.[0],
        depth: calls.findLast(c => c[0] === 'depthFunc')?.[1],
        winding: calls.findLast(c => c[0] === 'frontFace')?.[1] });
    },
  };
  return { target, draws };
}

test('original skin draws retain zero-alpha ARMHND2 RGB for every model, team and arm segment', () => {
  // Skin is original slot0. ARMHND2's entire authored alpha0 is not an exposure mask.
  // Other original runtime uniform slots contain real cutouts and stay alpha-tested.
  for (let model = 0; model < 32; model++) for (const identityPass of [false, true]) {
    const { target, draws } = playerScene(model);
    SourceRenderer.prototype.drawScene.call(target, identityPass);
    const arms = draws.filter(draw => /^joint\/[2-7]$/.test(draw.mesh));
    assert.equal(arms.length, 14 * 6);
    for (const draw of arms) {
      const alpha = draw.texture === 'ARMHND2' ? 0 : 1;
      assert(alpha > draw.cutoff,
        `${draw.texture} ${draw.mesh} model${model}: opaque original RGB must survive the actual player cutoff`);
      assert.equal(draw.blend, 'disable', 'Texture alpha must not blend away opaque original skin.');
      assert.equal(draw.constantAlpha, false, 'Do not rewrite authored texture alpha to repair a draw-state error.');
      assert.equal(draw.cull, 'enable', 'Keep actual back-face culling; no double-sided arm workaround.');
      assert.equal(draw.depth, 'LESS');
      assert.equal(draw.winding, 'CW');
      const actor = draw.matrix.byteOffset / (15 * 16 * Float32Array.BYTES_PER_ELEMENT) | 0;
      assert.equal(draw.flash, target.fx.flashAlpha[actor], 'Retain source white-flash addition on the restored skin.');
    }
    assert.equal(draws.find(draw => draw.mesh === 'world').cutoff, 100 / 255,
      'The player correction must not disable a genuine original world alpha test.');
    for (const draw of draws.filter(draw => ['SLVMSK', 'JNUM', 'HEDSHU', 'JERSF'].includes(draw.texture))) {
      assert.equal(draw.cutoff, 1 / 255,
        `Retain the actual ${draw.texture} cutout, including the reset before jersey numbers.`);
      assert(!(0 > draw.cutoff), 'Alpha0 genuine uniform/facemask/number pixels must remain excluded.');
    }
  }
});

test('opaque player correction preserves explicit source nodraw exclusions and selected joint matrices', () => {
  const { target, draws } = playerScene(4, 6);
  for (let i = 0; i < target.current.joints.length; i++) target.current.joints[i] = i / 16;
  SourceRenderer.prototype.drawScene.call(target, false);
  assert(!draws.some(draw => draw.mesh === 'joint/6'), 'SN_NODRAW must remain an actual draw exclusion.');
  for (let actor = 0; actor < 14; actor++) for (const joint of [2, 3, 4, 5, 7]) {
    const draw = draws.filter(value => value.mesh === `joint/${joint}`)[actor];
    assert.deepEqual(draw.matrix, target.current.joints.subarray((actor * 15 + joint) * 16,
      (actor * 15 + joint + 1) * 16), 'The selected actual pose, not a replacement arm pose, must reach the GPU.');
  }
});

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
