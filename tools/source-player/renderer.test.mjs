import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import test from 'node:test';
import { pathToFileURL } from 'node:url';
import { runInNewContext } from 'node:vm';

const source = process.env.SOURCE_PLAYER_RENDERER ?
  pathToFileURL(process.env.SOURCE_PLAYER_RENDERER) : new URL('./wwwroot/renderer.js', import.meta.url);
const { SourceRenderer, shadowVertex, shadowFragment, sourceObservation } = await import(source);
const code = await readFile(source, 'utf8');
const projection = [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1];
test('light observation preserves actual state, omits only camera history/immutable inventories, and cannot mutate live nested objects', async () => {
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
  const loader = await readFile(new URL('./wwwroot/main.js', import.meta.url), 'utf8');
  const publicObject = loader.indexOf("Object.defineProperty(globalThis, 'sourcePlayer',");
  assert(publicObject >= 0, 'Exercise the actual public loader object, not a copied diagnostic wrapper.');
  const costs = { sourceTick: 387, samples: [{ milliseconds: 1.25 }] };
  const wire = JSON.stringify(costs, null, 2) + '\n';
  const calls = { frames: 0, clock: 0, scheduled: 0 };
  let hostClock = 41.5, active = false;
  const activeError = new Error('Actual host rejects active source-step diagnostics.');
  const advance = () => { calls.frames++; status.sourceTick++; hostClock++; };
  const scope = {
    api: undefined, ready: false, failed: true,
    performance: { now: () => { calls.clock++; return hostClock; } },
    requestAnimationFrame: () => { calls.scheduled++; },
    setTimeout: () => { calls.scheduled++; },
  };
  runInNewContext(loader.slice(publicObject), scope);
  assert.equal(typeof scope.sourcePlayer.sourceStepCosts, 'function', 'The public diagnostic export must actually exist.');
  for (const api of [undefined, {}, { SourceStepCosts: 7 }]) {
    scope.api = api;
    assert.throws(() => scope.sourcePlayer.sourceStepCosts(), /not initialized/);
  }
  scope.api = {
    Frame: advance, FrameObserved: advance, Capture: advance, SetActive: advance,
    SourceStepCosts: () => { if (active) throw activeError; return wire; },
  };
  assert.equal(scope.sourcePlayer.sourceStepCosts(), wire,
    'Forward the exact C# JSON string, including whitespace, while terminal/failed and not yet ready.');
  const copy = JSON.parse(scope.sourcePlayer.sourceStepCosts());
  copy.samples[0].milliseconds = 999;
  assert.equal(JSON.parse(scope.sourcePlayer.sourceStepCosts()).samples[0].milliseconds, 1.25);
  assert.equal(costs.samples[0].milliseconds, 1.25, 'Caller-owned parsed diagnostics must not mutate actual host cost data.');
  active = true;
  assert.throws(() => scope.sourcePlayer.sourceStepCosts(), error => error === activeError,
    'Propagate the actual active-export failure without catching, polling or replacing it.');
  assert.equal(status.sourceTick, 387);
  assert.equal(hostClock, 41.5);
  assert.deepEqual(calls, { frames: 0, clock: 0, scheduled: 0 });
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

function teamSelectionFixture(Renderer) {
  const { gl, calls } = recordedGl(), draws = [];
  const model = Array.from({ length: 15 }, (_, joint) => ({
    drawEnabled: true, parts: [{ mesh: `ready/${joint}`, slot: joint >= 2 && joint <= 7 ? 0 : 2 }],
  }));
  const target = {
    gl, calls, draws, scene: 'scene', hud: 'hud', hudVao: 'hud-vao', hudBuffer: 'hud-buffer',
    sceneUniforms: Object.fromEntries(['model', 'viewProjection', 'atlas', 'fogEnabled', 'constantAlpha',
      'constantAlphaValue', 'textureTimesAlpha', 'playerFlashAdd', 'cutoff', 'identityOutput'].map(name => [name, name])),
    hudUniforms: { contentSize: 'contentSize', atlas: 'hud-atlas', highlightAdd: 'highlightAdd' },
    canvas: { getBoundingClientRect: () => ({ width: 512, height: 384 }) },
    models: new Map([[6, model], [10, structuredClone(model)]]),
    appearances: new Map([0, 1].map(side => [side, {
      textures: [`skin${side}`, 'face', `uniform${side}`], numberMesh: `number${side}`,
      numberTexture: `uniform${side}`, numberMatrix: 1,
      headParts: [{ mesh: 'must-not-use-a-game-popped-head', slot: 1 }], headJoint: 8,
    }])),
    meshes: new Map([...model.map(joint => joint.parts[0].mesh), 'number0', 'number1']
      .map(mesh => [mesh, { vao: mesh, count: 3 }])),
    textures: new Map(['skin0', 'skin1', 'face', 'uniform0', 'uniform1', 'hud/shared']
      .map(texture => [texture, { texture }])),
    world: [{}], ball: {}, frameCount: 0,
    drawScene() {}, drawEffects() {}, assertHealthy() {},
    drawHud() { Renderer.prototype.drawHud.call(this); },
    draw(mesh, texture, matrix, identity) {
      draws.push({ mesh, texture, matrix: Array.from(matrix), identity });
      Renderer.prototype.draw.call(this, mesh, texture, matrix, identity);
    },
  };
  const packet = {
    schema: 'source-tmsel-players/v1', epoch: 5, sourceTick: 214, beforeHudPrimitive: 1,
    projection: [...projection], joints: Array(480).fill(0), models: [6, 10], appearances: [0, 1],
    actors: [0, 1].map(side => ({
      id: side ? 10 : 3, team: side, teamIndex: side ? 30 : 0, rosterIndex: 3,
      facing: side ? 512 : 490, rawFlags: side ? 0 : 8,
      script: side ? 'qb_ready_anim' : 'qb_ready_f_anim', sourceFrame: 1,
      processSerial: side + 1, processBirthTick: 214, lastWakeTick: 214,
    })),
  };
  for (let side = 0; side < 2; side++) for (let joint = 0; joint < 15; joint++)
    packet.joints.splice((side * 15 + joint) * 16, 16,
      1.375, .1875, -.3125, 0, -.4375, .875, .0625, 0, .125, -.25, 1.625, 0,
      side ? -18.25 : 18.5, joint + .625, 65.875, 1);
  const ball = Array(20).fill(0);
  ball[3] = 255; ball[4] = ball[9] = ball[14] = ball[19] = 1;
  const status = {
    epoch: 5, sourceTick: 0, cabinetSession: { stage: 'Teams' }, teamSelectionPlayers: packet,
    ball: { sourceTick: 0, rawFlags: 0, visible: false,
      worldMatrix: Array.from({ length: 12 }, (_, i) => ball[4 + (i % 4) * 4 + Math.floor(i / 4)]) },
  };
  const present = snapshot => Renderer.prototype.present.call(target, projection, Array(14 * 15 * 16).fill(0),
    Array(14).fill(6), Array(14).fill(0), ball, Array(3 * 3 * 4).fill(0),
    [0xffffffff, 0xffffffff, 0xffffffff], Array(3).fill('hud/shared'), [3, 3, 3], JSON.stringify(snapshot));
  return { target, packet, status, present, calls, draws };
}

test('actual Present/HUD caller draws both source team-ready rigs between real background and text without reconstructing poses', async () => {
  const exercise = Renderer => {
    const fixture = teamSelectionFixture(Renderer), { packet, status, present, calls, draws } = fixture;
    present(status);
    assert.equal(draws.length, 32, 'Both actual15-joint ready rigs and their number meshes must be submitted, not status-only.');
    const order = calls.filter(call => ['drawArrays', 'drawElements'].includes(call[0]));
    assert.deepEqual(order[0], ['drawArrays', 'TRIANGLES', 0, 3], 'Only the actual opaque backdrop precedes the models.');
    assert(order.slice(1, -1).every(call => call[0] === 'drawElements'));
    assert.deepEqual(order.at(-1), ['drawArrays', 'TRIANGLES', 3, 6], 'Foreground HUD resumes after both meshes, even with one shared atlas.');
    assert.deepEqual(calls.find(call => call[0] === 'uniformMatrix4fv' && call[1] === 'viewProjection')?.[3],
      Float32Array.from(packet.projection), 'Use the actual native team camera, never the field-camera matrix.');
    for (let side = 0; side < 2; side++) for (let joint = 0; joint < 15; joint++) {
      const draw = draws[side * 16 + joint];
      assert.deepEqual(draw.matrix, packet.joints.slice((side * 15 + joint) * 16, (side * 15 + joint + 1) * 16).map(Math.fround));
      assert.equal(draw.identity, null, 'The live native model must draw normal materials, not an identity proof or silhouette.');
    }
    assert(!draws.some(draw => draw.mesh.includes('popped-head')));
    assert.equal(calls.filter(call => call[0] === 'clear').length, 1);
    assert.deepEqual(calls.find(call => call[0] === 'clear'), ['clear', 'DEPTH_BUFFER_BIT'],
      'The native team camera clears stale field depth only, preserving the actual cabinet backdrop.');
    return fixture;
  };
  const fixture = exercise(SourceRenderer);
  const saved = fixture.target.current.teamSelectionPlayers.joints[0];
  fixture.packet.joints[0] = 99;
  assert.equal(fixture.target.current.teamSelectionPlayers.joints[0], saved, 'Copy the actual immutable wire packet.');
  fixture.draws.length = 0;
  fixture.present({ ...fixture.status, cabinetSession: { stage: 'Rosters' }, teamSelectionPlayers: null });
  assert.equal(fixture.target.current.teamSelectionPlayers, null);
  assert.equal(fixture.draws.length, 0, 'Leaving the actual team screen must not leave stale preview actors.');
  const mutation = code.replace('SourceRenderer.prototype.drawTeamSelectionPlayers.call(this);', '');
  assert.notEqual(mutation, code);
  const omitted = await import('data:text/javascript,' + encodeURIComponent(mutation));
  assert.throws(() => exercise(omitted.SourceRenderer), /must be submitted, not status-only/,
    'Removing the real production draw caller must fail despite intact status metadata and model packets.');
});

test('actual Present rejects mismatched team-ready source process/epoch/rig packets instead of falling back to game actors', () => {
  const mutations = [
    snapshot => { snapshot.cabinetSession.stage = 'Rosters'; },
    snapshot => { snapshot.teamSelectionPlayers.epoch++; },
    snapshot => { snapshot.teamSelectionPlayers.models[1] = 6; },
    snapshot => { snapshot.teamSelectionPlayers.beforeHudPrimitive = 4; },
    snapshot => { snapshot.teamSelectionPlayers.joints[0] = '1'; },
    snapshot => { snapshot.teamSelectionPlayers.joints.pop(); },
    snapshot => { snapshot.teamSelectionPlayers.actors[0].script = 'qb_ready_anim'; },
    snapshot => { snapshot.teamSelectionPlayers.actors[1].lastWakeTick--; },
    snapshot => { snapshot.teamSelectionPlayers.actors[1].rosterIndex = 0; },
  ];
  for (const mutate of mutations) {
    const fixture = teamSelectionFixture(SourceRenderer), changed = structuredClone(fixture.status);
    mutate(changed);
    assert.throws(() => fixture.present(changed), /SOURCE_TMSEL_(PACKET|ACTOR)/);
    assert.equal(fixture.draws.length, 0);
  }
});

test('actual public C# team-selection poses and complete Present reach the production GPU caller', async t => {
  const path = process.env.SOURCE_PLAYER_TEST_TMSEL_PACKET;
  if (!path) { t.skip('An explicit actual public C# team-selection packet receipt is required.'); return; }
  const artifact = JSON.parse(await readFile(path, 'utf8')), packet = artifact.actualPacket;
  assert.equal(artifact.actualMenuInputAndSourceClock, true);
  assert.equal(artifact.directAnimationOrSelectorInvocation, false);
  const exercise = Renderer => {
    const { target, status, present, draws, calls } = teamSelectionFixture(Renderer);
    target.models.clear(); target.appearances.clear(); target.meshes.clear();
    for (const model of artifact.models) for (const joint of model.definition)
      for (const part of joint.parts) target.meshes.set(part.mesh, { vao: part.mesh, count: 3 });
    for (const appearance of artifact.appearances) {
      const value = appearance.definition;
      target.meshes.set(value.numberMesh, { vao: value.numberMesh, count: 3 });
      for (let i = 0; i < value.textures.length; i++) {
        const previous = target.textures.get(value.textures[i]);
        if (previous) assert.equal(previous.sha256, value.textureHashes[i]);
        target.textures.set(value.textures[i], { texture: value.textures[i], sha256: value.textureHashes[i] });
      }
    }
    for (const model of artifact.models)
      Renderer.prototype.model.call(target, model.id, JSON.stringify(model.definition));
    for (const appearance of artifact.appearances)
      Renderer.prototype.appearance.call(target, appearance.id, JSON.stringify(appearance.definition));
    if (artifact.actualPresent) {
      const actual = artifact.actualPresent;
      assert.deepEqual(actual.status.teamSelectionPlayers, packet);
      for (const texture of actual.hudTextures)
        if (!target.textures.has(texture)) target.textures.set(texture, { texture });
      // This GL-call harness records, but does not rasterize, the unrelated world pass.
      target.worldFx = actual.status.world ? { metadata: structuredClone(actual.status.world) } : undefined;
      Renderer.prototype.present.call(target, actual.camera, actual.joints, actual.models, actual.appearances,
        actual.ball, actual.hudVertices, actual.hudColors, actual.hudTextures, actual.hudCounts, JSON.stringify(actual.status));
    } else {
      status.epoch = packet.epoch; status.teamSelectionPlayers = packet;
      present(status);
    }
    const expectedDraws = artifact.models.reduce((count, model) => count + 1 +
      model.definition.filter(joint => joint.drawEnabled).reduce((sum, joint) => sum + joint.parts.length, 0), 0);
    assert.equal(draws.length, expectedDraws, 'The actual native team packet must reach mesh draws, not status-only.');
    let drawIndex = 0;
    for (let side = 0; side < 2; side++) {
      const definition = target.models.get(packet.models[side]);
      const appearance = target.appearances.get(packet.appearances[side]);
      assert.equal(appearance.rosterIndex, 3);
      for (let joint = 0; joint < 15; joint++) if (definition[joint].drawEnabled)
        for (const part of definition[joint].parts) {
          const draw = draws[drawIndex++];
          assert.equal(draw.mesh, part.mesh);
          assert.equal(draw.texture, appearance.textures[part.slot]);
          assert.deepEqual(draw.matrix,
            packet.joints.slice((side * 15 + joint) * 16, (side * 15 + joint + 1) * 16).map(Math.fround),
            'The actual produced affine, not ascale, animated-ay or a reconstructed TRS, must reach each native mesh.');
        }
      const number = draws[drawIndex++];
      assert.equal(number.mesh, appearance.numberMesh);
      assert.deepEqual(number.matrix, packet.joints.slice((side * 15 + appearance.numberMatrix) * 16,
        (side * 15 + appearance.numberMatrix + 1) * 16).map(Math.fround));
    }
    assert.equal(drawIndex, draws.length);
    assert(drawIndex > 32, 'Consume the actual source498/held-ball/sleeve/head definitions, not a fifteen-triangle stand-in.');
    assert.equal(calls.filter(call => call[0] === 'drawElements').length, drawIndex);
    assert.equal(target.status.sourceTick, 0, 'Actual menu poses advance on their source clock, not the unadvanced game backdrop.');
  };
  exercise(SourceRenderer);
  const mutation = code.replace('SourceRenderer.prototype.drawTeamSelectionPlayers.call(this);', '');
  assert.notEqual(mutation, code);
  const omitted = await import('data:text/javascript,' + encodeURIComponent(mutation));
  assert.throws(() => exercise(omitted.SourceRenderer), /actual native team packet must reach mesh draws/,
    'Removing the actual draw caller must fail with the genuine public C# Present and retained source definitions.');
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

function selectedVenue(field = 2, stadium = 1, homePrefix = 'EAG') {
  const prefixes = 'CAR FAL RAV BIL PAN BEA BEN BRW COW BRO LIO PAC COL JAG CHI DOL VIK PAT SAI GIA JET RAI EAG STE CHA FOR SEA RAM BUC TIT RED'.split(' ');
  return {
    stableId: 'source-field2-stadpie', manifest: '3'.repeat(64),
    selectionSchema: 'blitz-source-environment/v2',
    selectionId: `source-field2-stadpie/field-${field}/stadium-${stadium}/${homePrefix}`,
    field, stadium, homePrefix, homeTeamIndex: prefixes.indexOf(homePrefix),
    fieldTextureTable: ['field2_textures', 'field2_textures', 'astro_turf_field_textures',
      'dirt_field_textures', 'snow_field_textures', 'asphalt_field_textures', 'mud_field_textures'][field],
    stadiumTextureTable: ['day_stadium_textures', 'stadium_textures', 'snow_stadium_textures', 'stadium_textures'][stadium],
    alternateVenuesAdmitted: true, teamEndZonesBound: true,
  };
}

function selectedWorld(venue = selectedVenue()) {
  const { target, calls } = worldTarget();
  target.world = [];
  target.meshes = new Map(['turf', 'stands', 'endzone'].map(id => [id, {}]));
  const bindings = [
    ['turf', 6, 'field2', ['grass198.wms', 'grass198.wms', 'grass1_t.wms', 'grass1_d.wms',
      'grass1_s.wms', 'grass1_a.wms', 'grass1_m.wms'][venue.field], 'grass198.wms'],
    ...(venue.stadium === 3 ? [] : [['stands', 19, 'stadpie',
      ['crowdd_d.wms', 'crowdd98.wms', 'crowdd_s.wms'][venue.stadium], 'crowdd98.wms']]),
    ['endzone', 1, 'field2', venue.homePrefix + 'ez1.wms', 'Beaez1.wms'],
  ];
  for (const [mesh, materialSlot, sourceSet, selectedName, authoredName] of bindings) {
    const sourcePath = 'img3d/' + selectedName.replace(/\.wms$/, '.tga');
    target.textures.set(mesh, { sha256: '4'.repeat(64), origin: sourcePath + ' / TGA32' });
    SourceRenderer.prototype.worldDraw.call(target, mesh, mesh, JSON.stringify({
      venue, sourceSet, materialSlot, selectedName, authoredName, sourcePath,
      textureSha256: '4'.repeat(64), sourceDrawOrder: 0x10000,
    }));
  }
  return { target, calls, venue };
}

test('selected source turf/night/Eagles uses its uploaded field, stadium and endzone bindings, not the historic Bears world', () => {
  const { target, venue } = selectedWorld(), packet = syntheticWorldPacket();
  packet.state.venue = venue;
  SourceRenderer.prototype.worldPresentation.call(target, projection, packet);
  assert.equal(target.worldFx.metadata.venue.selectionId, 'source-field2-stadpie/field-2/stadium-1/EAG');
  assert.deepEqual(target.world.map(draw => [draw.mesh, draw.texture, draw.state.selectedName]),
    [['turf', 'turf', 'grass1_t.wms'], ['stands', 'stands', 'crowdd98.wms'], ['endzone', 'endzone', 'EAGez1.wms']]);
});

test('selected material receiver covers every original field/stadium index and active team prefix without a default alias', () => {
  for (let field = 0; field < 7; field++) for (let stadium = 0; stadium < 4; stadium++) {
    const { target, venue } = selectedWorld(selectedVenue(field, stadium, 'BEA')), packet = syntheticWorldPacket();
    packet.state.venue = venue;
    SourceRenderer.prototype.worldPresentation.call(target, projection, packet);
    assert.equal(target.world.length, stadium === 3 ? 2 : 3);
    assert.equal(target.world.some(draw => draw.state.sourceSet === 'stadpie'), stadium !== 3);
    assert.equal(target.worldFx.metadata.venue.selectionId, `source-field2-stadpie/field-${field}/stadium-${stadium}/BEA`);
  }
  for (const prefix of 'CAR FAL RAV BIL PAN BEA BEN BRW COW BRO LIO PAC COL JAG CHI DOL VIK PAT SAI GIA JET RAI EAG STE CHA FOR SEA RAM BUC TIT RED'.split(' ')) {
    const { target, venue } = selectedWorld(selectedVenue(2, 3, prefix)), packet = syntheticWorldPacket();
    packet.state.venue = venue;
    SourceRenderer.prototype.worldPresentation.call(target, projection, packet);
    assert.equal(target.world.at(-1).state.selectedName, prefix + 'ez1.wms');
  }
  const { target, venue } = selectedWorld(selectedVenue(2, 3));
  target.textures.set('forbidden-stadium', { sha256: '4'.repeat(64), origin: 'img3d/crowdd98.tga / TGA32' });
  assert.throws(() => SourceRenderer.prototype.worldDraw.call(target, 'stands', 'forbidden-stadium', JSON.stringify({
    venue, materialSlot: 19, sourceSet: 'stadpie', authoredName: 'crowdd98.wms',
    selectedName: 'crowdd98.wms', sourcePath: 'img3d/crowdd98.tga', textureSha256: '4'.repeat(64),
  })), /SOURCE_WORLD_VENUE/, 'Original stadium3 must not retain an uploaded stadium draw.');
});

test('selected venue rejects a mismatched world packet, material, texture hash and absent resource instead of falling back', () => {
  for (const mutate of [
    packet => { packet.state.venue.field = 4; },
    packet => { packet.state.venue.stadium = 2; },
    packet => { packet.state.venue.homePrefix = 'BEA'; },
    packet => { packet.state.venue.homeTeamIndex = 5; },
    packet => { packet.state.venue.fieldTextureTable = 'field2_textures'; },
    packet => { packet.state.venue.selectionSchema = 'blitz-source-environment/v1'; },
  ]) {
    const { target, venue } = selectedWorld(), packet = syntheticWorldPacket();
    packet.state.venue = structuredClone(venue); mutate(packet);
    assert.throws(() => SourceRenderer.prototype.worldPresentation.call(target, projection, packet), /SOURCE_WORLD_VENUE/);
  }
  for (const mutate of [
    state => { state.selectedName = 'grass198.wms'; },
    state => { state.sourcePath = 'img3d/grass198.tga'; },
    state => { state.textureSha256 = '5'.repeat(64); },
    state => { state.materialSlot = 1; },
  ]) {
    const { target } = selectedWorld(), draw = target.world[0], state = structuredClone(draw.state);
    mutate(state);
    assert.throws(() => SourceRenderer.prototype.worldDraw.call(target, draw.mesh, draw.texture, JSON.stringify(state)),
      /SOURCE_WORLD_VENUE/);
  }
  const { target } = selectedWorld(), draw = target.world[0];
  target.textures.delete(draw.texture);
  assert.throws(() => SourceRenderer.prototype.worldDraw.call(target, draw.mesh, draw.texture, JSON.stringify(draw.state)),
    /Unbound original world draw/);
});

test('authenticated runtime venue groups switch at new-match epochs without uploading replacement resources', () => {
  const { target, venue: cardinals } = selectedWorld(selectedVenue(0, 0, 'CAR'));
  const { target: eagles, venue } = selectedWorld(selectedVenue(0, 0, 'EAG'));
  for (const [id, texture] of eagles.textures) target.textures.set('eagles/' + id, texture);
  for (const draw of eagles.world)
    SourceRenderer.prototype.worldDraw.call(target, draw.mesh, 'eagles/' + draw.texture, JSON.stringify(draw.state));
  const packet = syntheticWorldPacket();
  packet.state.venue = cardinals;
  SourceRenderer.prototype.worldPresentation.call(target, projection, packet);
  const resources = target.worldResourceCreations;
  const changed = structuredClone(packet);
  changed.state.venue = venue;
  changed.state.presentSequence++;
  assert.throws(() => SourceRenderer.prototype.worldPresentation.call(target, projection, changed), /new-match epoch/);
  assert.equal(target.worldFx.metadata.venue.homePrefix, 'CAR', 'Rejected choices cannot partially replace the active selection.');
  changed.state.epoch++;
  SourceRenderer.prototype.worldPresentation.call(target, projection, changed);
  assert.equal(target.worldFx.metadata.venue.homePrefix, 'EAG');
  assert.equal(target.world.at(-1).state.selectedName, 'EAGez1.wms');
  const rematch = structuredClone(changed);
  rematch.state.epoch++; rematch.state.presentSequence++;
  SourceRenderer.prototype.worldPresentation.call(target, projection, rematch);
  assert.equal(target.worldResourceCreations, resources, 'Rematch reuses original GPU resources.');
  const absent = structuredClone(rematch);
  absent.state.epoch++; absent.state.presentSequence++;
  absent.state.venue = selectedVenue(0, 0, 'RED');
  assert.throws(() => SourceRenderer.prototype.worldPresentation.call(target, projection, absent), /SOURCE_WORLD_VENUE/);
  assert.equal(target.worldFx.metadata.venue.homePrefix, 'EAG', 'Missing catalog choices must not reuse a loaded-prefix fallback.');
});

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

test('actual newly selected managed venue uploads and packet agree at the production renderer boundary',
  { skip: !process.env.SOURCE_VENUE_MANAGED_PACKET }, async () => {
    const receipt = JSON.parse(await readFile(process.env.SOURCE_VENUE_MANAGED_PACKET, 'utf8'));
    assert.equal(receipt.schema, 'source-venue-managed-production/v1');
    assert.equal(receipt.synthetic, false);
    const packet = receipt.effectsEnvelope.world, { target } = worldTarget();
    target.world = [];
    target.meshes = new Map(receipt.uploadedDraws.map(draw => [draw.mesh, {}]));
    target.textures = new Map(receipt.uploadedTextures.map(texture => [texture.id, texture]));
    for (const draw of receipt.uploadedDraws)
      SourceRenderer.prototype.worldDraw.call(target, draw.mesh, draw.texture, JSON.stringify(draw.state));
    SourceRenderer.prototype.worldPresentation.call(target, projection, packet);
    assert.equal(target.worldFx.metadata.venue.selectionId, receipt.selectionId);
    assert(receipt.sourceTick > 0);
    assert.equal(target.world.length, receipt.uploadedWorldDrawCalls);
    assert.equal(new Set(target.world.map(draw => draw.mesh.split('/')[1])).size, receipt.sourceDrawPieces);
    if (packet.state.venue.stadium === 3) {
      assert.equal(receipt.sourceDrawPieces, 12);
      assert.equal(receipt.uploadedWorldDrawCalls, 20, 'Original field pieces retain their actual multiple material submeshes.');
      assert(target.world.every(draw => draw.state.sourceSet === 'field2'));
      assert.equal(receipt.uploadedTextures.filter(texture => texture.id.startsWith('world/')).length,
        receipt.catalogSelections === 31 ? 134 : 14);
    }
    const wrong = structuredClone(packet);
    wrong.state.venue.manifest = 'f'.repeat(64);
    wrong.state.presentSequence++;
    assert.throws(() => SourceRenderer.prototype.worldPresentation.call(target, projection, wrong), /SOURCE_WORLD_VENUE/);
  });
