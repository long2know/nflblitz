import assert from 'node:assert/strict';
import { value } from '../browser/chrome.mjs';
import { Cdp } from '../browser/lib.mjs';

export function frameStats(intervals) {
  assert.ok(intervals.length > 0 && intervals.every(ms => Number.isFinite(ms) && ms > 0),
    'Timing evidence requires actual positive frame intervals.');
  const sorted = [...intervals].sort((a, b) => a - b);
  const durationMs = intervals.reduce((total, ms) => total + ms, 0);
  const middle = Math.floor(sorted.length / 2);
  return {
    intervals: intervals.length, durationMs, fps: intervals.length * 1000 / durationMs,
    medianFrameMs: sorted.length % 2 ? sorted[middle] : (sorted[middle - 1] + sorted[middle]) / 2,
    p95FrameMs: sorted[Math.ceil(sorted.length * 0.95) - 1], maxFrameMs: sorted.at(-1),
  };
}

export function rendererKind(renderer) {
  const name = renderer.unmaskedRenderer || '';
  if (/swiftshader|llvmpipe|softpipe|software|lavapipe/i.test(name)) return 'software';
  if (/nvidia|radeon|intel|apple|adreno|mali/i.test(name)) return 'hardware-renderer-reported';
  return 'unknown';
}

export async function webglRenderer(cdp) {
  const actual = await value(cdp, `(() => {
    const canvas = document.querySelector("#unity-canvas");
    const gl = window.__viewportGl || canvas?.getContext("webgl2") || canvas?.getContext("webgl");
    if (!gl) return null;
    const debug = gl.getExtension("WEBGL_debug_renderer_info");
    return {
      vendor: gl.getParameter(gl.VENDOR), renderer: gl.getParameter(gl.RENDERER),
      version: gl.getParameter(gl.VERSION), shadingLanguage: gl.getParameter(gl.SHADING_LANGUAGE_VERSION),
      unmaskedVendor: debug ? gl.getParameter(debug.UNMASKED_VENDOR_WEBGL) : null,
      unmaskedRenderer: debug ? gl.getParameter(debug.UNMASKED_RENDERER_WEBGL) : null,
      contextAttributes: gl.getContextAttributes(), contextLost: gl.isContextLost(),
      drawingWidth: gl.drawingBufferWidth, drawingHeight: gl.drawingBufferHeight,
    };
  })()`);
  assert.ok(actual && !actual.contextLost, 'An actual initialized game WebGL context is required.');
  return { ...actual, kind: rendererKind(actual) };
}

export async function chromeGpu(cdp) {
  const endpoint = new URL(cdp.socket.url);
  assert.equal(endpoint.hostname, '127.0.0.1', 'GPU inspection must stay on loopback.');
  const response = await fetch(`http://127.0.0.1:${endpoint.port}/json/version`,
    { signal: AbortSignal.timeout(5000) });
  assert.ok(response.ok, 'The owned browser endpoint must be responsive.');
  const browser = await Cdp.connect((await response.json()).webSocketDebuggerUrl);
  try {
    const { gpu } = await browser.send('SystemInfo.getInfo');
    return { devices: gpu.devices, featureStatus: gpu.featureStatus, auxAttributes: gpu.auxAttributes,
      driverBugWorkarounds: gpu.driverBugWorkarounds };
  } finally {
    browser.close();
  }
}

export async function sampleFrames(cdp, label, durationMs = 5000) {
  assert.ok(Number.isFinite(durationMs) && durationMs >= 1000 && durationMs <= 10000);
  const actual = await value(cdp, `new Promise((resolve, reject) => {
    const canvas = document.querySelector("#unity-canvas");
    const gl = window.__viewportGl || canvas?.getContext("webgl2") || canvas?.getContext("webgl");
    if (!gl || document.hidden || !document.hasFocus()) {
      reject(new Error("Frame sampling requires a visible, focused, actual game WebGL context."));
      return;
    }
    const saved = [], rafIntervals = [], drawnIntervals = [];
    let calls = 0, previousCalls = 0, start, previousRaf, previousDraw, drawnFrames = 0, request;
    const restore = () => {
      cancelAnimationFrame(request);
      for (const [name, descriptor] of saved) {
        if (descriptor) Object.defineProperty(gl, name, descriptor);
        else delete gl[name];
      }
    };
    for (const name of ["drawArrays", "drawElements", "drawArraysInstanced", "drawElementsInstanced", "drawRangeElements"]) {
      if (typeof gl[name] !== "function") continue;
      const original = gl[name], descriptor = Object.getOwnPropertyDescriptor(gl, name);
      saved.push([name, descriptor]);
      gl[name] = function(...args) { calls++; return original.apply(this, args); };
    }
    const timer = setTimeout(() => { restore(); reject(new Error("Actual frame sampling timed out.")); }, 20000);
    function frame(time) {
      if (document.hidden || !document.hasFocus()) {
        clearTimeout(timer); restore(); reject(new Error("Game lost visibility/focus during timing sample.")); return;
      }
      if (start === undefined) start = time;
      if (previousRaf !== undefined && time > previousRaf) rafIntervals.push(time - previousRaf);
      if (calls > previousCalls) {
        drawnFrames++;
        if (previousDraw !== undefined && time > previousDraw) drawnIntervals.push(time - previousDraw);
        previousDraw = time;
      }
      previousCalls = calls;
      previousRaf = time;
      if (time - start < ${durationMs}) { request = requestAnimationFrame(frame); return; }
      clearTimeout(timer);
      restore();
      resolve({ durationMs: time - start, rafIntervals, drawnIntervals, drawnFrames, drawCalls: calls,
        viewport: { width: innerWidth, height: innerHeight, dpr: devicePixelRatio },
        drawingBuffer: { width: gl.drawingBufferWidth, height: gl.drawingBufferHeight },
        visibility: document.visibilityState, focused: document.hasFocus() });
    }
    request = requestAnimationFrame(frame);
  })`);
  return { label, requestedDurationMs: durationMs, ...actual,
    raf: frameStats(actual.rafIntervals), rendered: frameStats(actual.drawnIntervals),
    method: 'RAF intervals plus RAF intervals containing real game GL draw submissions; not GPU-completion/display-refresh counters.' };
}
