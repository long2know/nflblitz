import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { readFile } from 'node:fs/promises';
import { dirname, join } from 'node:path';
import test from 'node:test';
import { assertBallTrailEvidence } from './ball-trails.assertions.mjs';

const path = process.env.SOURCE_BALL_TRAIL_EVIDENCE;
assert(path, 'Supply the actual owned browser evidence file explicitly; synthetic data is not a gameplay witness.');
const evidence = JSON.parse(await readFile(path));

test('actual emitted browser artifact: source lifecycle, native GPU bindings/colors and continued play', async () => {
  assertBallTrailEvidence(evidence);
  const sha = bytes => createHash('sha256').update(bytes).digest('hex');
  assert.equal(sha(await readFile(join(dirname(path), 'http-build-info.json'))), evidence.receiptSha256);
  assert.equal(sha(await readFile(join(dirname(path), 'producer.browser.mjs'))), evidence.producer.browserSha256);
  assert.equal(sha(await readFile(join(dirname(path), 'producer.assertions.mjs'))), evidence.producer.assertionsSha256);
  assert.equal(sha(await readFile(new URL('./ball-trails.assertions.mjs', import.meta.url))), evidence.producer.assertionsSha256);
  for (const frame of evidence.frames) {
    const png = await readFile(join(dirname(path), frame.name + '.png'));
    assert.equal(png.length, frame.pngBytes);
    assert.equal(sha(png), frame.pngSha256);
  }
});

test('artifact-only causal negatives reject missing stages, forged alpha, wrong mesh, blank pixels and stalled continuation', () => {
  for (const mutate of [
    e => { e.lifecycleRecords.stages.splice(2, 1); },
    e => { e.frames.find(f => f.name === 'natural-nonfire-ghosts-public-pause').gpu.ballTrailMaterialDraws[0].gpuConstantAlphaValue = 1; },
    e => { e.frames.find(f => f.name === 'natural-nonfire-ghosts-public-pause').gpu.ballTrailMaterialDraws[0].mesh = 'sphere'; },
    e => { e.frames.find(f => f.name === 'natural-nonfire-ghosts-public-pause').gpu.ballTrailNonWhitePixels = 0; },
    e => { e.final.state.sourceTick = e.target.entry.sourceTick + 2; },
  ]) {
    const changed = structuredClone(evidence); mutate(changed);
    assert.throws(() => assertBallTrailEvidence(changed), assert.AssertionError,
      'A changed emitted artifact must fail the same gate used by the real browser producer.');
  }
});
