import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { mkdir, writeFile } from 'node:fs/promises';
import path from 'node:path';
import { launchChrome, sleep, value } from '../../tests/browser/chrome.mjs';

const args = process.argv.slice(2);
function option(name) {
  const at = args.indexOf(name);
  assert(at >= 0 && args[at + 1] && !args[at + 1].startsWith('--'), `Explicit ${name} required.`);
  return args[at + 1];
}
const url = option('--url'), beforeUrl = option('--before-renderer'), afterUrl = option('--after-renderer');
assert([url, beforeUrl, afterUrl].every(item => new URL(item).origin === new URL(url).origin),
  'Only the same explicitly owned local repro origin may supply render modules.');
assert(['127.0.0.1', 'localhost'].includes(new URL(url).hostname));
assert(Number(new URL(url).port) > 8096, 'Protected preview ports are not a rendering-test target.');
const output = path.resolve(option('--output'));
await mkdir(output, { recursive: true });
const sha = bytes => createHash('sha256').update(bytes).digest('hex');
const beforeBytes = Buffer.from(await (await fetch(beforeUrl)).arrayBuffer());
const afterBytes = Buffer.from(await (await fetch(afterUrl)).arrayBuffer());
assert.notEqual(sha(beforeBytes), sha(afterBytes), 'Before/after must be different, pinned actual renderer assets.');
const report = {
  schema: 'source-player-arm-rendering-pixels/v1', status: 'running', url,
  beforeRenderer: { url: beforeUrl, sha256: sha(beforeBytes) },
  afterRenderer: { url: afterUrl, sha256: sha(afterBytes) },
  method: 'Ordinary production World; real Escape pause; renderer-only same-packet redraw with actual before/after drawScene implementations. No gameplay/pose/camera/mesh/texture setters.',
  frames: [], errors: [], console: [], controls: [], nativeUnityBuild: false,
};
let chrome;

async function inspect(expression) {
  // Extend only this owned probe's pending evaluation, not the shared CDP helper
  // or application. Managed startup can legitimately occupy the page for >30s.
  const cdp = chrome.cdp;
  const request = cdp.send('Runtime.evaluate', { expression, returnByValue: true, awaitPromise: true });
  const id = cdp.nextId;
  const pending = cdp.pending.get(id);
  clearTimeout(pending.timeout);
  pending.timeout = setTimeout(() => {
    cdp.pending.delete(id);
    pending.reject(new Error('Owned rendering inspection exceeded180s.'));
  }, 180000);
  const result = await request;
  if (result.exceptionDetails) throw new Error(JSON.stringify(result.exceptionDetails));
  return result.result.value;
}

async function until(predicate, description, milliseconds = 20000) {
  const deadline = Date.now() + milliseconds;
  while (Date.now() < deadline) {
    const snapshot = await inspect('globalThis.sourcePlayer?.snapshot()');
    assert(!snapshot?.failed, `Actual source-player failure during ${description}.`);
    if (snapshot?.state && predicate(snapshot.state)) return snapshot;
    await sleep(60);
  }
  throw new Error(`Timed out waiting for ${description}; no state override is permitted.`);
}

async function tap(key, code, number) {
  report.controls.push({ key, code, method: 'actual CDP cabinet/page key', at: Date.now() });
  await chrome.cdp.send('Input.dispatchKeyEvent',
    { type: 'keyDown', key, code, windowsVirtualKeyCode: number, nativeVirtualKeyCode: number });
  await sleep(40);
  await chrome.cdp.send('Input.dispatchKeyEvent',
    { type: 'keyUp', key, code, windowsVirtualKeyCode: number, nativeVirtualKeyCode: number });
}

async function capture(name, variant) {
  const result = await value(chrome.cdp, `(() => {
    const r = globalThis.armRenderer, gl = r.gl, draws = [], originalDraw = r.draw;
    const packet = Object.fromEntries(Object.entries(r.current).map(([key, item]) =>
      [key, ArrayBuffer.isView(item) ? Array.from(item) : item]));
    const jointIndex = matrix => matrix.buffer === r.current.joints.buffer ?
      (matrix.byteOffset - r.current.joints.byteOffset) / 64 : -1;
    const identity = (mesh, texture, matrix, color, skinOnly) => {
      const index = jointIndex(matrix), actor = Math.floor(index / 15), joint = index % 15;
      if (index < 0 || color === null || color === 0 || actor >= 14) return 0;
      if (skinOnly && (!(joint >= 2 && joint <= 7) ||
          texture !== r.appearances.get(r.current.appearances[actor]).textures[0])) return 0;
      return index + 1;
    };
    const framebuffer = gl.createFramebuffer(), maskTexture = gl.createTexture(), depth = gl.createRenderbuffer();
    let color, allCounts, skinCounts;
    try {
      r.draw = function(mesh, texture, matrix, id) {
        const index = jointIndex(matrix);
        if (index >= 0 && index < 210) {
          const actor = Math.floor(index / 15), joint = index % 15;
          draws.push({ actor, joint, mesh, texture, indices: r.meshes.get(mesh).count,
            cutoff: gl.getUniform(r.scene, r.sceneUniforms.cutoff),
            constantAlpha: gl.getUniform(r.scene, r.sceneUniforms.constantAlpha),
            blend: gl.isEnabled(gl.BLEND), depth: gl.getParameter(gl.DEPTH_FUNC),
            depthWrite: gl.getParameter(gl.DEPTH_WRITEMASK), cull: gl.isEnabled(gl.CULL_FACE),
            cullFace: gl.getParameter(gl.CULL_FACE_MODE), frontFace: gl.getParameter(gl.FRONT_FACE) });
        }
        return originalDraw.call(this, mesh, texture, matrix, id);
      };
      r.drawScene(false); r.drawEffects(false); r.drawHud();
      color = r.canvas.toDataURL('image/png');
      gl.bindTexture(gl.TEXTURE_2D, maskTexture);
      gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGBA8, r.canvas.width, r.canvas.height, 0, gl.RGBA, gl.UNSIGNED_BYTE, null);
      gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, gl.NEAREST);
      gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MAG_FILTER, gl.NEAREST);
      gl.bindRenderbuffer(gl.RENDERBUFFER, depth);
      gl.renderbufferStorage(gl.RENDERBUFFER, gl.DEPTH_COMPONENT24, r.canvas.width, r.canvas.height);
      gl.bindFramebuffer(gl.FRAMEBUFFER, framebuffer);
      gl.framebufferTexture2D(gl.FRAMEBUFFER, gl.COLOR_ATTACHMENT0, gl.TEXTURE_2D, maskTexture, 0);
      gl.framebufferRenderbuffer(gl.FRAMEBUFFER, gl.DEPTH_ATTACHMENT, gl.RENDERBUFFER, depth);
      if (gl.checkFramebufferStatus(gl.FRAMEBUFFER) !== gl.FRAMEBUFFER_COMPLETE)
        throw new Error('Actual non-MSAA limb identity framebuffer is incomplete.');
      for (const skinOnly of [false, true]) {
        r.draw = function(mesh, texture, matrix, id) {
          return originalDraw.call(this, mesh, texture, matrix, identity(mesh, texture, matrix, id, skinOnly));
        };
        r.drawScene(true);
        const pixels = new Uint8Array(r.canvas.width * r.canvas.height * 4), counts = Array(210).fill(0);
        gl.readPixels(0, 0, r.canvas.width, r.canvas.height, gl.RGBA, gl.UNSIGNED_BYTE, pixels);
        for (let i = 0; i < pixels.length; i += 4)
          if (pixels[i] >= 1 && pixels[i] <= 210 && pixels[i + 1] === 0 && pixels[i + 2] === 0)
            counts[pixels[i] - 1]++;
        if (skinOnly) skinCounts = counts;
        else allCounts = counts;
      }
    } finally {
      gl.bindFramebuffer(gl.FRAMEBUFFER, null);
      gl.deleteFramebuffer(framebuffer); gl.deleteTexture(maskTexture); gl.deleteRenderbuffer(depth);
      r.draw = originalDraw;
      r.drawScene(false); r.drawEffects(false); r.drawHud();
    }
    r.assertHealthy('same-pose arm pixel comparison');
    return { packet, state: r.status, draws, allCounts, skinCounts, color,
      identityFramebuffer: 'actual RGBA8/DEPTH_COMPONENT24, non-MSAA, full canvas dimensions; unchanged source scene/depth/culling',
      device: r.device, width: r.canvas.width, height: r.canvas.height,
      constants: { LESS: gl.LESS, BACK: gl.BACK, CW: gl.CW } };
  })()`);
  assert.equal(result.state.active, false, 'The genuine source frame must be paused, not advanced during capture.');
  for (const key of ['color']) {
    const bytes = Buffer.from(result[key].split(',')[1], 'base64');
    const filename = `${name}-${variant}-${key}.png`;
    await writeFile(path.join(output, filename), bytes);
    result[key] = { file: filename, bytes: bytes.length, sha256: sha(bytes) };
  }
  const screenshot = await chrome.cdp.send('Page.captureScreenshot', { format: 'png' });
  const pageBytes = Buffer.from(screenshot.data, 'base64');
  await writeFile(path.join(output, `${name}-${variant}-page.png`), pageBytes);
  const after = await until(state => !state.active, 'paused source bracket');
  assert.equal(after.state.sourceTick, result.state.sourceTick);
  result.sourceTickBracket = [result.state.sourceTick, after.state.sourceTick];
  const packetBytes = Buffer.from(JSON.stringify(result.packet));
  result.packetSha256 = sha(packetBytes);
  await writeFile(path.join(output, `${name}-${variant}-frame-input.json`), packetBytes);
  delete result.packet;
  return result;
}

async function pair(name) {
  await tap('Escape', 'Escape', 27);
  await until(state => !state.active, 'real Escape pause');
  await sleep(150);
  await value(chrome.cdp, 'armRenderer.drawScene = armBeforeDraw; true');
  const before = await capture(name, 'before');
  await value(chrome.cdp, `(async () => {
    armRenderer.drawScene = (await import(${JSON.stringify(afterUrl)})).SourceRenderer.prototype.drawScene;
    return true;
  })()`);
  const after = await capture(name, 'after');
  assert.equal(after.packetSha256, before.packetSha256, 'The entire actual GPU packet must be identical across the frame pair.');
  assert.notEqual(after.color.sha256, before.color.sha256, 'A real browser pixel difference is mandatory.');
  const changed = [];
  for (const actor of before.state.actors) {
    for (const joint of [2, 3, 4, 5, 6, 7]) {
      const at = actor.id * 15 + joint;
      if (actor.textureSlots[0] === 'runtime/ARMHND2.WMS') {
        assert.equal(before.skinCounts[at], 0, 'The old path must actually reproduce alpha-discarded skin pixels.');
        if (after.skinCounts[at] > 0)
          changed.push({ actor: actor.id, team: actor.team, joint,
            beforeSkinPixels: before.skinCounts[at], afterSkinPixels: after.skinCounts[at],
            beforeWholeLimbPixels: before.allCounts[at], afterWholeLimbPixels: after.allCounts[at] });
      }
    }
  }
  assert(changed.length > 0, 'The fix must restore actual source-authored ARMHND2 arm pixels.');
  for (const draw of after.draws) {
    const appearance = after.state.actors[draw.actor];
    const skin = draw.texture === 'teams/' + appearance.textureSlots[0];
    assert((skin ? draw.cutoff < 0 : Math.abs(draw.cutoff - 1 / 255) < 1e-8) &&
      !draw.blend && !draw.constantAlpha && draw.depthWrite &&
      draw.depth === after.constants.LESS && draw.cull && draw.cullFace === after.constants.BACK &&
      draw.frontFace === after.constants.CW,
    'Actual GPU player state must retain skin RGB, other alpha cutouts, depth testing and back-face culling.');
  }
  const record = { name, before, after, changed };
  report.frames.push(record);
  await writeFile(path.join(output, 'result.json'), JSON.stringify(report, null, 2) + '\n');
  await value(chrome.cdp, 'armRenderer.drawScene = armBeforeDraw; true');
  await tap('Escape', 'Escape', 27);
  await until(state => state.active, 'ordinary resume');
}

try {
  chrome = await launchChrome(output);
  report.browser = chrome.version; report.launch = chrome.launch;
  chrome.cdp.on('Runtime.consoleAPICalled', event => {
    const message = event.args.map(a => a.value ?? a.description ?? '').join(' ');
    report.console.push(message);
    if (event.type === 'error') report.errors.push(message);
  });
  chrome.cdp.on('Runtime.exceptionThrown', event => {
    report.errors.push(event.exceptionDetails);
  });
  await chrome.cdp.send('Emulation.setDeviceMetricsOverride', { width: 1280, height: 1000, deviceScaleFactor: 1, mobile: false });
  await chrome.cdp.send('Page.navigate', { url });
  await chrome.cdp.send('Page.bringToFront');
  await writeFile(path.join(output, 'checkpoint.json'), JSON.stringify({ stage: 'owned navigation complete', launch: report.launch }) + '\n');
  const loaded = await until(state => state.sourceTick >= 2 && state.actorCount === 14, 'actual ordinary render packets', 180000);
  report.publicReadyMarker = loaded.ready;
  report.build = loaded.build;
  for (const assembly of loaded.build.assemblies) {
    const bytes = Buffer.from(await (await fetch(new URL(assembly.path, url))).arrayBuffer());
    assert.equal(sha(bytes), assembly.sha256, 'The real qualified C# assembly must be unchanged.');
  }
  await value(chrome.cdp, `(async () => {
    const { SourceRenderer } = await import(${JSON.stringify(beforeUrl)});
    globalThis.armBeforeDraw = SourceRenderer.prototype.drawScene;
    SourceRenderer.prototype.drawScene = function(...args) {
      globalThis.armRenderer = this;
      return armBeforeDraw.apply(this, args);
    };
    return true;
  })()`);
  await sleep(100);
  assert(await value(chrome.cdp, 'Boolean(globalThis.armRenderer)'));
  await pair('first-visible');
  const later = await until(state => ['DeadBall', 'PlaySelect'].includes(state.phase) &&
    state.sourceTick > loaded.state.sourceTick + 20, 'bounded ordinary kickoff/possession', 25000);
  if (later.state.phase === 'DeadBall') await tap('Enter', 'Enter', 13);
  await until(state => state.phase === 'PlaySelect' || state.phase === 'PreSnap', 'ordinary next-possession selection');
  await pair('new-possession');
  const changed = report.frames.flatMap(frame => frame.changed);
  for (const team of ['Home', 'Away']) assert(changed.some(row => row.team === team), `Actual restored arm pixels required for ${team}.`);
  for (const joint of [2, 3, 4, 5, 6, 7]) assert(changed.some(row => row.joint === joint), `Actual restored source skin pixels required for joint${joint}.`);
  assert.equal(report.errors.length, 0);
  report.status = 'passed';
} catch (error) {
  report.status = 'failed'; report.failure = error.stack;
  process.exitCode = 1;
} finally {
  await writeFile(path.join(output, 'result.json'), JSON.stringify(report, null, 2) + '\n');
  if (chrome) await chrome.close();
  report.ownedBrowserClosed = Boolean(chrome);
  await writeFile(path.join(output, 'result.json'), JSON.stringify(report, null, 2) + '\n');
}
console.log(JSON.stringify({ status: report.status, frames: report.frames.length,
  restoredSegments: report.frames.reduce((sum, frame) => sum + frame.changed.length, 0),
  failure: report.failure, output, ownedBrowserClosed: report.ownedBrowserClosed }));
