import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import test from 'node:test';
import { pathToFileURL } from 'node:url';

const source = process.env.SOURCE_PLAYER_RENDERER ?
  pathToFileURL(process.env.SOURCE_PLAYER_RENDERER) : new URL('./wwwroot/renderer.js', import.meta.url);
const { SourceRenderer, shadowVertex, shadowFragment, sourceObservation } = await import(source);
const code = await readFile(source, 'utf8');
const projection = [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1];
test('light observation preserves actual state, omits only camera history/immutable inventories, and cannot mutate live nested objects', () => {
  const status = { sourceTick: 387, camera: { sourceTick: 387, cameraTrace: [{ sourceTick: 1 }] },
    input: { stations: [{ buttons: 7 }] }, records: { screen: 'Inactive', privateGlyphsExcluded: true } };
  const audio = { nodes: [{ voiceId: 9 }], active: true };
  const observed = sourceObservation(status, audio, true, false);
  assert.equal(observed.state.sourceTick, 387);
  assert.equal(observed.state.camera.sourceTick, 387);
  assert.equal(Object.hasOwn(observed.state.camera, 'cameraTrace'), false);
  assert.equal(Object.hasOwn(observed, 'build'), false);
  assert.equal(Object.hasOwn(observed, 'resources'), false);
  assert.equal(observed.state.records.privateGlyphsExcluded, true);
  observed.state.input.stations[0].buttons = 0;
  observed.audioReceiver.nodes[0].voiceId = 99;
  assert.equal(status.input.stations[0].buttons, 7);
  assert.equal(audio.nodes[0].voiceId, 9);
  assert.equal(status.camera.cameraTrace.length, 1, 'Observation must not drain or alter an actual producer/history collection.');
});
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

test('source-affine packet retains full non-TRS matrices through present, uniforms and torso numbers', () => {
  const { target, draws } = playerScene(0), packets = Array(14 * 15 * 16).fill(0);
  for (let actor = 0; actor < 14; actor++) for (let joint = 0; joint < 15; joint++) {
    const offset = (actor * 15 + joint) * 16;
    const values = [1.375, 0.1875, -0.3125, 0, -0.4375, 0.875, 0.0625, 0,
      0.125, -0.25, 1.625, 0, actor + 0.375, joint - 0.625, actor - joint + 0.875, 1];
    for (let i = 0; i < 16; i++) packets[offset + i] = values[i];
  }
  const { gl, calls } = recordedGl();
  target.gl = gl;
  target.meshes = new Map(['world', 'numbers',
    ...Array.from({ length: 15 }, (_, i) => `joint/${i}`), 'sleeve/2', 'sleeve/5']
    .map(id => [id, { vao: id, count: 3 }]));
  target.textures = new Map(['world-cutout', 'ARMHND', 'ARMHND2', 'HEDSHU', 'JERSF',
    'LEGHEL', 'SLVMSK', 'JNUM', 'BALL'].map(id => [id, { texture: id }]));
  const record = target.draw;
  target.draw = function (...args) {
    record.apply(this, args);
    SourceRenderer.prototype.draw.apply(this, args);
  };
  target.drawScene = identity => SourceRenderer.prototype.drawScene.call(target, identity);
  target.drawEffects = () => {};
  target.drawHud = () => {};
  target.assertHealthy = () => {};
  target.frameCount = 0;
  target.ball = {};
  const ball = Array(20).fill(0);
  ball[1] = 11; ball[3] = 255; ball[4] = ball[9] = ball[14] = ball[19] = 1;
  const status = JSON.stringify({ sourceTick: 11, ball: {
    sourceTick: 11, rawFlags: 0, visible: false,
    worldMatrix: Array.from({ length: 12 }, (_, i) => ball[4 + (i % 4) * 4 + Math.floor(i / 4)]),
  } });
  SourceRenderer.prototype.present.call(target, projection, packets, Array(14).fill(0),
    Array.from({ length: 14 }, (_, i) => i), ball, [], [], [], [], status);
  assert(target.current.joints instanceof Float32Array);
  const uniforms = calls.filter(call => call[0] === 'uniformMatrix4fv' && call[1] === 'model');
  assert.equal(uniforms.length, draws.length, 'The actual draw method must submit every selected matrix.');
  for (let actor = 0; actor < 14; actor++) for (let joint = 0; joint < 15; joint++) {
    const selected = draws.filter(draw => draw.mesh === `joint/${joint}`)[actor];
    const offset = (actor * 15 + joint) * 16;
    assert.deepEqual(Array.from(selected.matrix), Array.from(packets.slice(offset, offset + 16), Math.fround),
      `Full authored affine packet actor${actor}/joint${joint}, not quaternion/TRS reconstruction.`);
    const upload = uniforms[draws.indexOf(selected)];
    assert.equal(upload[2], false, 'Column-major matrix uploads must not request transposition.');
    assert.deepEqual(upload[3], selected.matrix, 'Actual model uniform receives the full selected affine basis.');
  }
  for (let actor = 0; actor < 14; actor++) {
    const number = draws.filter(draw => draw.mesh === 'numbers')[actor];
    const offset = (actor * 15 + 1) * 16;
    assert.deepEqual(Array.from(number.matrix), Array.from(packets.slice(offset, offset + 16), Math.fround),
      'Actual number geometry keeps the complete torso affine and pivot-centered origin.');
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

function syntheticWorldPacket() {
  const corners = [[240, 190, .01, 255, 255], [248, 191, .01, 255, 0],
    [250, 188, .01, 0, 0], [242, 187, .01, 0, 255]];
  return {
    vertices: [0, 1, 2, 2, 3, 0].flatMap(corner => corners[corner]), links: [0, 0, 2, 28],
    state: {
      schema: 'blitz-source-world/v1', enabled: true, epoch: 0, presentSequence: 1,
      sourceTick: 5, cameraSourceTick: 5, sourceFramesObserved: 1, renderInterpolation01: .25, hres: 512, vres: 384,
      sourceRevision: '89f840d58d152a2a9f230cb8d0cdffbd554c8ded',
      playerGitBlob: '5744c51f30b625b9f80b92388ab49ff0ebc5bb2c', fieldGitBlob: '2821ba68986cb7e62c42cf920747c5ba9b91ea37',
      cameraDepthRow: [.125, -.25, .75, 100],
      venue: { stableId: 'source-field2-stadpie', manifest: '1'.repeat(64),
        field: 0, stadium: 0, homePrefix: 'BEA', fieldTextureTable: 'field2_textures',
        stadiumTextureTable: 'day_stadium_textures', alternateVenuesAdmitted: false, teamEndZonesBound: false },
      fog: { origin: 'Unbound', configurationBound: false, enabled: false, isFoggy: null, fogMask: null, thickFogMask: null,
        colorRgb: 0, start: 0, end: 0, onDrawOrder: 0x8000, offDrawOrder: 0x58000 },
      shadows: {
        materialReady: false, acceptedLinks: 1, rasterStride: 5, selectorStride: 4, sourceDrawOrder: 0x20200,
        inheritedCull: 'GR_CULL_NEGATIVE', alphaSource: 'GR_ALPHASOURCE_TEXTURE_ALPHA',
        alphaFunction: 'GR_CMP_GREATER', alphaValue: 100, colorFunction: 'GR_COLORCOMBINE_TEXTURE_TIMES_ALPHA',
        textureFunction: 'GR_TEXTURECOMBINE_DECAL', clampMode: 'GR_TEXTURECLAMP_WRAP', filter: 'BILINEAR',
        depthMode: 'GR_DEPTHBUFFER_WBUFFER', depthFunction: 'GR_CMP_GREATER', depthWrite: true,
        rgbSource: 'GR_BLEND_SRC_ALPHA', rgbDestination: 'GR_BLEND_ONE_MINUS_SRC_ALPHA',
        alphaSourceBlend: 'GR_BLEND_SRC_ALPHA', alphaDestinationBlend: 'GR_BLEND_ONE_MINUS_SRC_ALPHA',
        textures: ['psshould', 'pslimb', 'pstorso', 'pshand'].map((name, slot) => ({
          slot, sourceName: `${name}.wms`, authoringPath: `img3d/${name.toUpperCase()}.TGA`, missing: true, texture: null,
          authoringGitBlob: ['b05993dc691d1c6d7632c26fa37092d09ebba4d9', 'a589bfe2eb87dea0fff00421fdd81c25aad65ea4',
            'cbe1aebd1249da69cf6136e1072c20dc532901c2', 'e6f69144c2ff170d25afcc0398c0f2d68ed2c700'][slot],
        })),
        actors: Array.from({ length: 14 }, (_, id) => ({ id, rawFlags: id ? 0 : 2, onScreen: id === 0, acceptedLinks: id ? 0 : 1 })),
        blocker: 'SOURCE_WORLD_SHADOW_TEXTURES_NOT_ADMITTED: synthetic missing-resource adapter fixture',
      },
    },
  };
}

function worldTarget() {
  const { gl: recorder, calls } = recordedGl();
  const gl = new Proxy(recorder, { get: (target, key) => {
    if (['getShaderParameter', 'getProgramParameter'].includes(key)) return () => true;
    if (['createBuffer', 'createVertexArray', 'createShader', 'createProgram'].includes(key)) return () => ({ kind: key });
    if (key === 'getUniformLocation') return (_, name) => name;
    return target[key];
  } });
  return { target: { gl, textures: new Map(), world: [{ state: { sourceDrawOrder: 0x10000 } }],
    drawCalls: 0, blend: SourceRenderer.prototype.blend }, calls };
}

test('adapter-only: actual world receiver uploads supplied source raster triangles, holds clocks and reports missing originals without drawing substitutes', () => {
  const packet = syntheticWorldPacket(), { target, calls } = worldTarget();
  SourceRenderer.prototype.worldPresentation.call(target, projection, packet);
  assert.deepEqual(calls.find(call => call[0] === 'bufferData')?.[2], Float32Array.from(packet.vertices));
  assert.equal(target.worldFx.metadata.fog.isFoggy, null, 'Unbound source state must not become a fabricated no-fog decision.');
  SourceRenderer.prototype.drawShadows.call(target, false);
  assert.equal(calls.filter(call => call[0] === 'drawArrays').length, 0, 'Missing PSSH* originals must never become generic black quads.');
  assert.deepEqual(target.lastShadowDraws, []);
  const held = structuredClone(packet); held.state.presentSequence++;
  SourceRenderer.prototype.worldPresentation.call(target, projection, held);
  assert.equal(target.worldResourceCreations, 1, 'Redraw holds one actual shader/VBO/VAO resource set.');
  assert.equal(target.worldFx.metadata.sourceFramesObserved, 1, 'The receiver must not advance any source clock.');
  assert.throws(() => SourceRenderer.prototype.worldPresentation.call(target, projection, held), /SOURCE_WORLD_CLOCK/);
  const reset = structuredClone(packet);
  Object.assign(reset.state, { epoch: 2, sourceTick: 0, cameraSourceTick: -1, presentSequence: 3 });
  SourceRenderer.prototype.worldPresentation.call(target, projection, reset);
  assert.equal(target.worldResourceCreations, 1, 'Observed reset epochs clear source frames but retain immutable GPU resources.');
});

test('adapter-only: world packet rejects wrong source linkage, material, resource substitution, size and invented fog admission', () => {
  const cases = [
    [packet => { packet.vertices.pop(); }, /SOURCE_WORLD_SHADOW_SIZE/],
    [packet => { packet.vertices[0] = NaN; }, /SOURCE_WORLD_SHADOW_SIZE/],
    [packet => { packet.links[2] = 0; }, /SOURCE_WORLD_SHADOW_LINK/],
    [packet => { packet.links[3] = 30; }, /SOURCE_WORLD_SHADOW_LINK/],
    [packet => { packet.vertices[3] = 256; }, /SOURCE_WORLD_SHADOW_GEOMETRY/],
    [packet => { packet.state.shadows.depthWrite = false; }, /SOURCE_WORLD_SHADOW_MATERIAL/],
    [packet => { packet.state.shadows.alphaValue = 0; }, /SOURCE_WORLD_SHADOW_MATERIAL/],
    [packet => { packet.state.shadows.materialReady = true; }, /SOURCE_WORLD_SHADOW_RESOURCES/],
    [packet => { packet.state.shadows.textures[0].sourceName = 'world/static-shadow'; }, /SOURCE_WORLD_SHADOW_RESOURCES/],
    [packet => { packet.state.fog.enabled = true; }, /SOURCE_WORLD_FOG/],
    [packet => { packet.state.fog.fogMask = 0; }, /SOURCE_WORLD_FOG/],
    [packet => { packet.state.venue.field = 4; }, /SOURCE_WORLD_VENUE/],
  ];
  for (const [mutate, error] of cases) {
    const packet = syntheticWorldPacket(), { target } = worldTarget(); mutate(packet);
    assert.throws(() => SourceRenderer.prototype.worldPresentation.call(target, projection, packet), error);
  }
});

test('adapter-only: supplied original-shadow resources draw exact raster linkage with source alpha, separate blends and disclosed portable depth conversion', () => {
  const packet = syntheticWorldPacket(), { target, calls } = worldTarget(), material = packet.state.shadows;
  const corners = [[310.75, 89.75, .0018046662, 255, 255], [317.5, 87, .0018393677, 255, 0],
    [322.5, 87.5, .0018328563, 0, 0], [315.75, 90.25, .0017983979, 0, 255]];
  packet.vertices = [0, 1, 2, 2, 3, 0].flatMap(corner => corners[corner]);
  for (const texture of material.textures) {
    texture.texture = `original/player-shadow/${texture.slot}`; texture.missing = false; texture.textureSha256 = '2'.repeat(64);
    target.textures.set(texture.texture, { texture: texture.texture, sha256: texture.textureSha256,
      repeat: true, linear: true, origin: texture.authoringPath });
  }
  material.materialReady = true;
  packet.state.fog = { ...packet.state.fog, origin: 'Versus', configurationBound: true, enabled: true,
    isFoggy: 2, fogMask: 1, thickFogMask: 1, colorRgb: 0x808080, start: 1, end: 500 };
  SourceRenderer.prototype.worldPresentation.call(target, projection, packet);
  SourceRenderer.prototype.drawShadows.call(target, false);
  assert.deepEqual(calls.filter(call => call[0] === 'drawArrays'), [['drawArrays', 'TRIANGLES', 0, 6]]);
  assert.deepEqual(calls.findLast(call => call[0] === 'bindTexture'), ['bindTexture', 'TEXTURE_2D', 'original/player-shadow/2']);
  assert(calls.some(call => call[0] === 'uniform1f' && call[1] === 'cutoff' && call[2] === 100 / 255));
  assert(calls.some(call => call[0] === 'depthMask' && call[1] === true));
  assert(calls.some(call => call[0] === 'depthFunc' && call[1] === 'LESS'));
  const area = (corners[1][0] - corners[0][0]) * (corners[2][1] - corners[0][1]) -
    (corners[1][1] - corners[0][1]) * (corners[2][0] - corners[0][0]);
  const windingAtDraw = calls.slice(0, calls.findIndex(call => call[0] === 'drawArrays'))
    .findLast(call => call[0] === 'frontFace')?.[1];
  assert.equal(windingAtDraw, area > 0 ? 'CCW' : 'CW',
    'The genuine source raster orientation must survive culling, not inherit the reflected world-mesh winding.');
  assert.equal(calls.findLast(call => call[0] === 'frontFace')?.[1], 'CW',
    'Restore the imported player/world mesh winding after drawing source raster shadows.');
  assert(calls.some(call => call[0] === 'uniform1f' && call[1] === 'normalizedDepthBias' &&
    call[2] === 28 / 65535), 'Source16-bit bias units must not become24-bit GPU implementation-dependent polygon-offset units.');
  assert.deepEqual(calls.find(call => call[0] === 'blendFuncSeparate'),
    ['blendFuncSeparate', 'SRC_ALPHA', 'ONE_MINUS_SRC_ALPHA', 'SRC_ALPHA', 'ONE_MINUS_SRC_ALPHA']);
  assert.deepEqual(calls.find(call => call[0] === 'uniform2f' && call[1] === 'fogRange'), ['uniform2f', 'fogRange', 1, 500]);
  assert.equal(target.worldFx.metadata.sourceTick, 5, 'Drawing supplied C# fog uniforms must not run a renderer simulation.');
  assert.match(shadowVertex, /cameraDepth=1\.0\/raster\.z/);
  assert.match(shadowVertex, /projected\.z-2\.0\*normalizedDepthBias\*cameraDepth/);
  assert.match(shadowVertex, /st\.x\/256\.0,1\.0-st\.y\/256\.0/);
  assert.match(shadowFragment, /if\(texel\.a<=cutoff\) discard/);
  assert.match(shadowFragment, /vec4\(texel\.rgb\*texel\.a,texel\.a\)/);
});

test('actual world composition retains field/table materials and source field2 pass after actors, without changing old independent profiles', () => {
  const { target, draws } = playerScene(0), packet = syntheticWorldPacket();
  target.worldFx = { metadata: packet.state, links: packet.links };
  target.sceneUniforms = { ...target.sceneUniforms, fogEnabled: 'fogEnabled', fogColor: 'fogColor',
    fogRange: 'fogRange', cameraDepthRow: 'cameraDepthRow' };
  target.world[0].state.sourceDrawOrder = 0x10000;
  target.world.push({ mesh: 'field2-goalposts', texture: 'original-goalposts', state: {
    ...target.world[0].state, sourceDrawOrder: 0x48000, depthMask: 'FXTRUE',
  } });
  SourceRenderer.prototype.drawScene.call(target, false);
  assert.equal(draws[0].mesh, 'world');
  assert.equal(draws.at(-1).mesh, 'field2-goalposts', 'Original draw_field2/goalposts are after player geometry inside fog on/off, not buried in the first world table.');
  assert.equal(draws.filter(draw => draw.mesh === 'numbers').length, 14, 'No actor/number skin or source-affine draw is removed.');
  assert.equal(target.lastLateWorldDraws[0].sourceDrawOrder, 0x48000);
});

test('actual managed world envelope is consumed unchanged when an explicit production packet receipt is supplied',
  { skip: !process.env.SOURCE_WORLD_MANAGED_PACKET }, async () => {
    const receipt = JSON.parse(await readFile(process.env.SOURCE_WORLD_MANAGED_PACKET, 'utf8'));
    const packet = receipt.effectsEnvelope.world, { target } = worldTarget();
    SourceRenderer.prototype.worldPresentation.call(target, projection, packet);
    assert.equal(target.worldFx.metadata.shadows.acceptedLinks, receipt.acceptedLinks);
    assert.deepEqual(target.worldFx.vertices, Float32Array.from(packet.vertices));
    assert(packet.links.length > 0, 'The C# receipt must originate from actual on-screen actor poses, not the synthetic adapter fixture.');
    assert.equal(target.worldFx.metadata.shadows.materialReady, false);
    assert.equal(receipt.userFogEnvelopes.length, 5);
    for (let selection = 0; selection < 5; selection++) {
      const { target: user, calls } = worldTarget(), supplied = receipt.userFogEnvelopes[selection].world;
      SourceRenderer.prototype.worldPresentation.call(user, projection, supplied);
      SourceRenderer.prototype.fogUniforms.call(user, {
        fogEnabled: 'fogEnabled', fogColor: 'fogColor', fogRange: 'fogRange', cameraDepthRow: 'cameraDepthRow',
      });
      assert.equal(user.worldFx.metadata.fog.userConfiguration.selection, selection);
      assert.equal(user.worldFx.metadata.fog.sourceEnabled, selection ? true : null);
      assert.equal(user.worldFx.metadata.fog.thickFogMask, null, 'Unknown saved source power-up masks cannot become invented zeroes.');
      assert.equal(user.worldFx.metadata.fog.fogMask, selection === 2 ? 0 : null,
        'Only the actual guaranteed-thick source branch determines final pup_fog0.');
      assert.deepEqual(calls.find(call => call[0] === 'uniform1i' && call[1] === 'fogEnabled'),
        ['uniform1i', 'fogEnabled', selection !== 0]);
      assert.deepEqual(calls.find(call => call[0] === 'uniform2f' && call[1] === 'fogRange'),
        ['uniform2f', 'fogRange', selection ? 1 : 0, [0, 1250, 500, 700, 700][selection]]);
    }
  });
