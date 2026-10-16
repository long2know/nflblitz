import assert from 'node:assert/strict';
import test from 'node:test';
import { frameStats, rendererKind } from './gpu.mjs';

test('frame timings preserve slow frames and use nearest-rank p95', () => {
  const actual = frameStats([...Array(19).fill(10), 110]);
  assert.equal(actual.intervals, 20);
  assert.equal(actual.medianFrameMs, 10);
  assert.equal(actual.p95FrameMs, 10);
  assert.equal(actual.maxFrameMs, 110);
  assert.equal(actual.durationMs, 300);
  assert.equal(actual.fps, 20000 / 300);
  assert.equal(frameStats([10, 20]).medianFrameMs, 15);
});

test('missing or invalid samples never become successful FPS evidence', () => {
  for (const intervals of [[], [0], [-1], [NaN], [Infinity]]) {
    assert.throws(() => frameStats(intervals));
  }
});

test('software and unknown renderers cannot be labeled hardware', () => {
  assert.equal(rendererKind({ unmaskedRenderer: 'ANGLE (Google, Vulkan SwiftShader Device)' }), 'software');
  assert.equal(rendererKind({ unmaskedRenderer: 'llvmpipe (LLVM 20)' }), 'software');
  assert.equal(rendererKind({ unmaskedRenderer: 'ANGLE (NVIDIA, GeForce RTX 3070, OpenGL)' }), 'hardware-renderer-reported');
  assert.equal(rendererKind({ renderer: 'WebKit WebGL' }), 'unknown');
});
