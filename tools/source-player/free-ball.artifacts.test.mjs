import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { readFile } from 'node:fs/promises';
import test from 'node:test';
import path from 'node:path';

const directory = process.env.SOURCE_FREE_BALL_EVIDENCE || 'tools/source-player/artifacts/free-ball-attempt1';
const evidence = JSON.parse(await readFile(path.join(directory, 'evidence.json'), 'utf8'));
const digest = bytes => createHash('sha256').update(bytes).digest('hex');
const sourceSha = 'd54218dd7d0fc45fe7ad7072cb39b12913dfed80687999f69d5e93a269bc9704';
const geometrySha = '22b92aa3f0b113be4c18edef70a6503fac448c1d85ff187b84b68c23b8d2eada';
const primarySha = 'b2a5795cc67286d6a9a1c60c21f92f393134ab81f18dedfad84ffe405edb3fa3';
const alternateSha = 'b59af47bf7baf6a2bcffacd4fb68af206229099102c00baf608d771f4ac5836e';
function bindings(frame) {
  const state = frame.snapshot.state, gpu = frame.gpu, ball = state.ball, draw = gpu.ballDraw;
  assert.equal(gpu.sourceTick, state.sourceTick, 'GPU and immutable snapshot must be one atomic source tick.');
  assert.equal(ball.sourceTick, state.sourceTick, 'Ball state must be the current actual Core source tick.');
  if (!ball.visible) {
    assert.equal(draw, null, 'A held ball must not acquire a disconnected free-ball draw.');
    assert.equal(gpu.ballPixels, 0);
    return;
  }
  assert(draw && gpu.ballPixels > 0, 'Free-ball witness needs actual visible GPU ball geometry.');
  assert.equal(draw.sourceTick, state.sourceTick, 'GPU draw must bind the actual source spin tick.');
  assert.equal(draw.mesh, 'original/free-ball/limb_medball_obj_big', 'GPU must bind original medball2, not held/generic geometry.');
  assert.equal(draw.metadata.sourceSha256, sourceSha);
  assert.equal(draw.metadata.geometrySha256, geometrySha);
  assert.equal(draw.metadata.sourcePivot, 'authoring-origin-unchanged', 'GPU ball must retain original authoring pivot.');
  assert.equal(draw.metadata.nativeGeneratedHeaderEquivalent, false);
  assert.equal(draw.texture, ball.rawFlags & 0x40 ? 'teams/runtime/HEDSHU2.WMS' : 'teams/runtime/HEDSHU.WMS',
    'GPU must bind original HEDSHU/HEDSHU2, not held-ball material6.');
  assert.equal(draw.textureSha256, ball.rawFlags & 0x40 ? alternateSha : primarySha);
  assert(draw.gpuOriginalTextureBound, 'Actual GL original texture binding must be observed.');
  assert.equal(draw.indices, 144);
  assert.equal(draw.frontFace, 'CCW-source321');
  const expected = Array.from(Float32Array.from(ball.packet.slice(4)));
  assert.deepEqual(draw.matrix, expected, 'Submitted ball matrix must remain connected to actual source state.');
  assert.deepEqual(draw.gpuModel, expected, 'Actual GPU uniform must bind the source flight/spin/scale matrix.');
  assert.equal(draw.gpuViewProjection.length, 16);
  assert(draw.gpuViewProjection.every(Number.isFinite));
}

test('actual new-build artifacts: original free-ball GPU flight, changing source orientation and retained held geometry', async () => {
  assert.equal(evidence.status, 'qualified-source-free-ball-witnesses-NOT-native-parity');
  assert.equal(evidence.nativeUnityOrPixelsQualified, false);
  assert.equal(evidence.url, 'http://127.0.0.1:8085/');
  assert.equal(evidence.errors.length, 0); assert.equal(evidence.networkErrors.length, 0);
  const bundle = 'tools/source-player/bin/SourcePlayer.Browser/Release/net10.0/browser-wasm/AppBundle';
  const receiptBytes = await readFile(path.join(bundle, 'build-info.json')), receipt = JSON.parse(receiptBytes);
  assert.equal(evidence.receiptSha256, digest(receiptBytes));
  assert.notEqual(evidence.receiptSha256, 'c23092d9bcdae85142867e9a397c9cb67253dbe291611d2fce7948eb365bc9cc');
  assert.equal(receipt.sources.length, 110, 'Actual current compile/delivery source inventory must remain exact.');
  for (const name of ['Assets/Scripts/AssetImport/SourceFreeBallData.cs',
    'Assets/Scripts/Runtime/SourceFreeBallBinding.cs', 'Assets/Scripts/Presentation/SourceFreeBallRenderModel.cs']) {
    const item = receipt.sources.find(s => s.path === name);
    assert(item, 'New actual production ball source must be compiled: ' + name);
    assert.equal(item.sha256, digest(await readFile(name)));
  }
  for (const assembly of receipt.assemblies) {
    const served = evidence.httpAssemblies.find(item => item.path === assembly.path);
    assert.equal(served.status, 200);
    assert.equal(served.observedSha256, assembly.sha256);
    assert.equal(served.bytes, assembly.bytes);
    assert.equal(digest(await readFile(path.join(bundle, assembly.path))), assembly.sha256);
    assert(evidence.assemblyRequests.some(r => r.url.endsWith('/' + assembly.path) && r.status === 200));
  }
  const flight = evidence.frames.filter(f => f.snapshot.state.phase === 'LiveBall' &&
    f.snapshot.state.ball.mode === 'InFlight' && f.snapshot.state.ball.visible && f.gpu.ballPixels > 0);
  assert(flight.length >= 2, 'Two real free-ball flight frames are required, not a status Boolean.');
  flight.forEach(bindings);
  assert(flight.at(-1).snapshot.state.sourceTick > flight[0].snapshot.state.sourceTick);
  assert(flight.at(-1).gpu.ballDraw.matrix.slice(0, 12).some((v, i) => v !== flight[0].gpu.ballDraw.matrix[i]),
    'Different source ticks must change actual orientation basis, not just ball translation.');
  assert.notDeepEqual(flight.at(-1).snapshot.state.ball.position, flight[0].snapshot.state.ball.position);
  const held = evidence.frames.find(f => f.name === 'retained-original-held-ball');
  assert(held, 'Retain an actual held-ball gameplay frame.');
  bindings(held);
  const carrier = held.snapshot.state.ball.carrier;
  assert(carrier >= 0 && (held.snapshot.state.actors[carrier].model & 12) !== 0 && held.gpu.actorPixels[carrier] > 0);
  assert.equal(new Set(evidence.frames.map(f => f.name)).size, evidence.frames.length);
  for (const frame of evidence.frames) {
    assert.equal(frame.snapshot.state.actorCount, 14);
    assert.equal(frame.snapshot.state.epoch, 0);
    assert.equal(frame.snapshot.state.manifestSha256, receipt.dataManifestSha256);
    const png = await readFile(path.join(directory, frame.name + '.png'));
    assert.equal(png.subarray(0, 8).toString('hex'), '89504e470d0a1a0a');
    assert.equal(png.length, frame.pngBytes); assert.equal(digest(png), frame.pngSha256);
    assert.equal(frame.screenshotSourceTickBracket[0], frame.snapshot.state.sourceTick);
    assert(frame.screenshotSourceTickBracket[1] >= frame.screenshotSourceTickBracket[0]);
  }
  assert(evidence.input.every(input => ['b', 'Enter'].includes(input.name)), 'Only approved legal menu/snap/pass/Continue inputs.');
  assert.equal(evidence.final.state.epoch, 0);
});

test('emitted-artifact counterfactuals: wrong model/material/pivot/tick/matrix fail named binding assertions', () => {
  const original = evidence.frames.find(f => f.gpu.ballDraw && f.gpu.ballPixels > 0);
  assert(original);
  for (const [mutate, expected] of [
    [f => { f.gpu.ballDraw.mesh = 'hand001b-material6'; }, /GPU must bind original medball2/],
    [f => { f.gpu.ballDraw.texture = 'BALL.WMS'; }, /GPU must bind original HEDSHU/],
    [f => { f.gpu.ballDraw.metadata.sourcePivot = 'recentered'; }, /original authoring pivot/],
    [f => { f.gpu.ballDraw.sourceTick++; }, /actual source spin tick/],
    [f => { f.gpu.ballDraw.gpuModel[0]++; }, /Actual GPU uniform/],
    [f => { f.gpu.ballPixels = 0; }, /actual visible GPU ball geometry/],
  ]) {
    const changed = structuredClone(original); mutate(changed);
    assert.throws(() => bindings(changed), expected);
  }
});
