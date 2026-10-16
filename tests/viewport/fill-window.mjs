import assert from 'node:assert/strict';
import { execFile } from 'node:child_process';
import { createHash } from 'node:crypto';
import { mkdir, readFile, writeFile } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { parseArgs, promisify } from 'node:util';
import { launchChrome, press, sleep, value } from '../browser/chrome.mjs';
import { cropPng, decodePng, parseGenerationProof, pngStats, readVisibleHud,
  requireRenderedField, safeUrl, scoreRects, tightScorePng } from '../browser/lib.mjs';
import { chromeGpu, sampleFrames, webglRenderer } from './gpu.mjs';

const exec = promisify(execFile);
const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const { values } = parseArgs({ options: {
  url: { type: 'string', default: 'http://127.0.0.1:8080/' },
  'artifact-root': { type: 'string', default: 'Build/WebGL' },
  output: { type: 'string', default: `.squad/.scratch/fill-viewport-${Date.now()}` },
  'timeout-ms': { type: 'string', default: '180000' },
} });
const target = new URL(values.url);
target.searchParams.set('actors', 'source');
assert.ok(['http:', 'https:'].includes(target.protocol), 'An actual HTTP-served Unity player is required.');
const directory = path.resolve(root, values.output);
const artifacts = path.resolve(root, values['artifact-root']);
for (const candidate of [directory, artifacts]) {
  assert.ok(candidate.startsWith(root + path.sep), 'Evidence and artifacts must remain in the checkout.');
}
const timeout = Number(values['timeout-ms']);
assert.ok(Number.isFinite(timeout) && timeout >= 1000);
await mkdir(directory, { recursive: true });
const sha256 = bytes => createHash('sha256').update(bytes).digest('hex');
const evidence = {
  schema: 'unity-full-browser-viewport/v1', status: 'failed', url: safeUrl(target.href),
  timestampUtc: new Date().toISOString(), artifacts: [], layouts: [], screenshots: [],
  controls: [], errors: [], resources: [], consoleMarkers: [],
  performance: [],
  scope: 'Actual source-actor Unity game, browser-content viewport and backing WebGL buffer; not forced browser/F11 fullscreen.',
  hudScope: 'DPR 1 desktop/portrait HUD and controls; DPR 2 cases qualify buffer/viewport resizing, not engine HUD scaling.',
  notRun: ['Full-match completion and whole-game behavioral/visual parity.', 'Engine HUD layout qualification at DPR 2.'],
};
let chrome;
let failure;
const messages = [];

async function fetched(relative) {
  const response = await fetch(new URL(relative, target), { signal: AbortSignal.timeout(30000), cache: 'no-store' });
  assert.equal(response.status, 200, relative);
  return { response, bytes: Buffer.from(await response.arrayBuffer()) };
}

async function until(check, label, milliseconds = timeout) {
  const deadline = Date.now() + milliseconds;
  do {
    assert.deepEqual(evidence.errors, [], 'No actual browser/runtime/network errors are allowed.');
    const result = await check();
    if (result) return result;
    await sleep(100);
  } while (Date.now() < deadline);
  throw new Error(`Timed out waiting for ${label}.`);
}

async function state() {
  const start = messages.length;
  await value(chrome.cdp, 'window.__viewportUnity.SendMessage("NFL Blitz 2000 / runtime","LogAnimationEvidence")');
  return until(async () => {
    const parts = new Map();
    for (const line of messages.slice(start)) {
      if (line.startsWith('SOURCE_ANIMATION_EVIDENCE ')) {
        return JSON.parse(line.slice('SOURCE_ANIMATION_EVIDENCE '.length));
      }
      const match = line.trimEnd().match(/^SOURCE_ANIMATION_EVIDENCE_PART ([0-9-]+) ([0-9]+)\/([0-9]+) (.*)$/s);
      if (!match) continue;
      if (!parts.has(match[1])) parts.set(match[1], { count: Number(match[3]), values: new Map() });
      const group = parts.get(match[1]);
      group.values.set(Number(match[2]), match[4]);
      if (group.values.size === group.count) {
        return JSON.parse(Array.from({ length: group.count }, (_, i) => group.values.get(i)).join(''));
      }
    }
    return null;
  }, 'read-only actual source runtime evidence', 5000);
}

const phase = expected => until(async () => {
  const actual = await state();
  return actual.phase === expected ? actual : null;
}, `actual ${expected} phase`, 45000);

function summary(actual) {
  return { phase: actual.phase, ballMode: actual.ballMode, carrierId: actual.carrierId,
    controlledPlayerId: actual.controlledPlayerId, hostTick: actual.hostTick, sourceTick: actual.sourceTick };
}

async function layout(width, height, dpr = 1, label = `${width}x${height}`) {
  await chrome.cdp.send('Emulation.setDeviceMetricsOverride', {
    width, height, deviceScaleFactor: dpr, mobile: false, screenWidth: width, screenHeight: height,
  });
  const metrics = await until(async () => {
    const current = await value(chrome.cdp, `(() => {
      const canvas = document.querySelector("#unity-canvas"), gl = window.__viewportGl;
      if (!canvas || !gl) return null;
      const rect = canvas.getBoundingClientRect(), style = getComputedStyle(canvas);
      const documentStyle = getComputedStyle(document.documentElement), bodyStyle = getComputedStyle(document.body);
      return {
        innerWidth, innerHeight, dpr: devicePixelRatio, x: rect.x, y: rect.y, width: rect.width, height: rect.height,
        bufferWidth: canvas.width, bufferHeight: canvas.height, drawingWidth: gl.drawingBufferWidth,
        drawingHeight: gl.drawingBufferHeight, glViewport: [...gl.getParameter(gl.VIEWPORT)],
        contextLost: gl.isContextLost(), focused: document.activeElement === canvas,
        fullscreen: document.fullscreenElement === canvas, scrollX, scrollY,
        documentWidth: document.documentElement.scrollWidth, documentHeight: document.documentElement.scrollHeight,
        bodyWidth: document.body.scrollWidth, bodyHeight: document.body.scrollHeight,
        borderWidths: [style.borderTopWidth, style.borderRightWidth, style.borderBottomWidth, style.borderLeftWidth],
        outlineWidth: style.outlineWidth, outlineStyle: style.outlineStyle, transform: style.transform,
        display: style.display, visibility: style.visibility, boxShadow: style.boxShadow,
        inlineStyle: canvas.getAttribute("style"),
        overflow: [documentStyle.overflowX, documentStyle.overflowY, bodyStyle.overflowX, bodyStyle.overflowY],
        documentSpacing: [documentStyle.margin, documentStyle.padding, bodyStyle.margin, bodyStyle.padding],
      };
    })()`);
    return current && Math.abs(current.bufferWidth - width * dpr) <= 1 &&
      Math.abs(current.bufferHeight - height * dpr) <= 1 ? current : null;
  }, `Unity buffer resize to ${width}x${height} at DPR ${dpr}`, 15000);
  evidence.layouts.push({ label, ...metrics });
  for (const [key, expected] of Object.entries({ innerWidth: width, innerHeight: height, dpr,
    x: 0, y: 0, width, height, documentWidth: width, documentHeight: height, bodyWidth: width, bodyHeight: height,
    scrollX: 0, scrollY: 0, bufferWidth: width * dpr, bufferHeight: height * dpr,
    drawingWidth: width * dpr, drawingHeight: height * dpr })) {
    assert.ok(Math.abs(metrics[key] - expected) <= 1, `${label} ${key}: ${JSON.stringify(metrics)}`);
  }
  assert.deepEqual(metrics.glViewport, [0, 0, metrics.drawingWidth, metrics.drawingHeight],
    'Actual GL viewport must use the full resized drawing buffer, not a cropped old render.');
  assert.deepEqual(metrics.borderWidths, ['0px', '0px', '0px', '0px']);
  assert.equal(metrics.outlineWidth, '0px');
  assert.equal(metrics.outlineStyle, 'none');
  assert.equal(metrics.transform, 'none');
  assert.equal(metrics.boxShadow, 'none');
  assert.equal(metrics.display, 'block');
  assert.equal(metrics.visibility, 'visible');
  assert.equal(metrics.contextLost, false);
  assert.deepEqual(metrics.overflow, ['hidden', 'hidden', 'hidden', 'hidden']);
  assert.deepEqual(metrics.documentSpacing, ['0px', '0px', '0px', '0px']);
  return metrics;
}

async function screenshot(label, metrics) {
  await sleep(150);
  const { data } = await chrome.cdp.send('Page.captureScreenshot', { format: 'png', captureBeyondViewport: false });
  const png = Buffer.from(data, 'base64');
  const image = decodePng(png);
  assert.equal(image.width, metrics.innerWidth * metrics.dpr);
  assert.equal(image.height, metrics.innerHeight * metrics.dpr);
  const stats = pngStats(image);
  requireRenderedField(stats);
  const file = path.join(directory, `${label}.png`);
  await writeFile(file, png);
  const actual = await state();
  const record = { path: path.relative(root, file), bytes: png.length, sha256: sha256(png),
    pixels: stats, state: summary(actual) };
  evidence.screenshots.push(record);
  return { record, image, file, metrics };
}

async function ocr(shot) {
  assert.equal(shot.metrics.dpr, 1, 'HUD OCR coordinates use CSS pixels.');
  const name = shot.file.replace(/\.png$/, '');
  const options = { maxBuffer: 1024 * 1024, env: { ...process.env, TMPDIR: directory,
    MAGICK_TEMPORARY_PATH: directory, OMP_THREAD_LIMIT: '2' } };
  const header = `${name}-header.png`;
  await writeFile(header, cropPng(shot.image, { x: 0, y: 0, width: shot.image.width,
    height: Math.min(200, shot.image.height), scale: shot.image.width < 650 ? 2 : 1 }));
  const [{ stdout: text }, { stdout: headerText }] = await Promise.all([
    exec('tesseract', [shot.file, 'stdout', '--psm', '11'], options),
    exec('tesseract', [header, 'stdout', '--psm', '11'], options),
  ]);
  const scores = {};
  let situationText = '';
  if (shot.image.width < 650) {
    const scale = Math.max(0.5, shot.image.width / 480);
    const rect = { x: 0, y: Math.floor(98 * scale), width: shot.image.width, height: Math.ceil(46 * scale) };
    const raw = `${name}-situation.png`, readable = `${name}-situation-ocr.png`;
    await writeFile(raw, cropPng(shot.image, rect));
    await exec('convert', [raw, '-filter', 'Lanczos', '-resize', '400%', '-colorspace', 'Gray', readable], options);
    ({ stdout: situationText } = await exec('tesseract', [readable, 'stdout', '--psm', '6'], options));
    await writeFile(`${name}-situation.txt`, situationText);
    shot.record.situationCrop = { path: path.relative(root, raw), rect, text: situationText };
  }
  await Promise.all(Object.entries(scoreRects({ x: 0, y: 0, width: shot.image.width, height: shot.image.height }))
    .map(async ([team, rect]) => {
      const file = `${name}-${team}-score.png`;
      await writeFile(file, tightScorePng(cropPng(shot.image, { ...rect, scale: 2 })));
      const { stdout } = await exec('tesseract', [file, 'stdout', '--psm', '8',
        '-c', 'tessedit_char_whitelist=0123456789'], options);
      scores[team] = stdout;
    }));
  await writeFile(`${name}.txt`, text);
  await writeFile(`${name}-header.txt`, headerText);
  const hud = readVisibleHud(text, headerText + '\n' + situationText, scores);
  shot.record.hud = hud;
  assert.match(headerText.replace(/[^a-z0-9]/gi, '').toUpperCase(), /NFLBLITZ2000/,
    `Actual game title must be visible, not cropped: ${shot.record.path}`);
  assert.equal(hud.homeScore, 0, `Fresh-match HOME score must remain visible: ${shot.record.path}`);
  assert.equal(hud.awayScore, 0, `Fresh-match AWAY score must remain visible: ${shot.record.path}`);
  assert.ok(hud.quarter === 1 && Number.isInteger(hud.clockSeconds),
    `Fresh-match clock and quarter must remain visible: ${shot.record.path}`);
  assert.ok(hud.phase, `Actual phase must remain visible: ${shot.record.path}`);
  if (shot.image.width < 650) {
    const controls = `${name}-controls.png`, magnified = `${name}-controls-ocr.png`;
    const rect = { x: 0, y: shot.image.height - 40, width: shot.image.width, height: 40 };
    await writeFile(controls, cropPng(shot.image, rect));
    await exec('convert', [controls, '-filter', 'Lanczos', '-resize', '400%', magnified], options);
    const { stdout } = await exec('tesseract', [magnified, 'stdout', '--psm', '6'], options);
    await writeFile(`${name}-controls.txt`, stdout);
    shot.record.controls = { crop: path.relative(root, controls), rect, text: stdout,
      processing: 'Pixel-only Lanczos magnification of the actual footer; no substituted text.' };
    for (const [label, pattern] of [
      ['movement', /\bWASD\b/], ['turbo', /\bSHIFT\s+TURBO\b/],
      ['tackle', /\bF\s*TACKLE\b/], ['defender switch', /\bQ\s*SWITCH\b/],
      ['restart', /\bR\s*RESTART\b/],
    ]) assert.match(stdout.toUpperCase(), pattern, `Actual portrait ${label} control: ${shot.record.path}`);
    // The stable opening result has next-play controls, not the old PlaySelect-only run/pass labels.
    assert.match(text.toUpperCase(), /\bSPACE\s+NEXT\s+PLAY\b/);
  } else {
    assert.match(text.toUpperCase(), /\bWASD\b/);
    assert.match(text.toUpperCase(), /\bSHIFT\b/);
  }
  return hud;
}

try {
  await exec('sh', ['scripts/verify-webgl.sh', artifacts], { cwd: root, env: { ...process.env, WEBGL_ALLOW_TEST_STUB: '0' } });
  const receiptBytes = await readFile(path.join(artifacts, 'build-receipt.json'));
  const proofBytes = await readFile(path.join(artifacts, 'build-generation.txt'));
  const receipt = JSON.parse(receiptBytes);
  const proof = parseGenerationProof(receipt, proofBytes.toString());
  assert.deepEqual((await fetched('build-receipt.json')).bytes, receiptBytes);
  assert.deepEqual((await fetched('build-generation.txt')).bytes, proofBytes);
  evidence.buildId = receipt.buildId;
  evidence.generationProofSha256 = sha256(proofBytes);
  for (const [relative, expected] of proof) {
    const local = await readFile(path.join(artifacts, relative));
    assert.equal(sha256(local), expected, `Local original artifact ${relative}`);
    if (relative === 'index.html') continue; // nginx intentionally injects only the outer-page style/icon.
    const { response, bytes } = await fetched(relative);
    assert.equal(sha256(bytes), expected, `Raw served player payload ${relative}`);
    const mime = relative.endsWith('.wasm') ? 'application/wasm'
      : relative.endsWith('.data') ? 'application/octet-stream' : 'application/javascript';
    assert.equal(response.headers.get('content-type').split(';')[0], mime);
    evidence.artifacts.push({ path: relative, bytes: bytes.length, sha256: expected, mime });
  }
  chrome = await launchChrome(directory);
  evidence.chrome = { version: chrome.version, launch: chrome.launch };
  chrome.cdp.on('Runtime.consoleAPICalled', event => {
    const text = event.args.map(arg => arg.value ?? arg.description ?? '').join(' ');
    messages.push(text);
    if (event.type === 'error') evidence.errors.push({ type: 'console', text });
  });
  chrome.cdp.on('Runtime.exceptionThrown', event => evidence.errors.push({ type: 'exception', detail: event.exceptionDetails }));
  chrome.cdp.on('Log.entryAdded', event => {
    if (event.entry.level === 'error') evidence.errors.push({ type: 'log', text: event.entry.text });
  });
  chrome.cdp.on('Network.loadingFailed', event => evidence.errors.push({ type: 'network', text: event.errorText }));
  chrome.cdp.on('Network.responseReceived', event => {
    const resource = { url: safeUrl(event.response.url), status: event.response.status, mime: event.response.mimeType };
    evidence.resources.push(resource);
    if (resource.status >= 400) evidence.errors.push(resource);
  });
  chrome.cdp.on('Page.javascriptDialogOpening', event => evidence.errors.push({ type: 'dialog', text: event.message }));
  await chrome.cdp.send('Network.setCacheDisabled', { cacheDisabled: true });
  await chrome.cdp.send('Emulation.setDeviceMetricsOverride', {
    width: 1920, height: 1080, deviceScaleFactor: 1, mobile: false, screenWidth: 1920, screenHeight: 1080,
  });
  await chrome.cdp.send('Page.addScriptToEvaluateOnNewDocument', { source: `
    const originalContext = HTMLCanvasElement.prototype.getContext;
    HTMLCanvasElement.prototype.getContext = function(type, ...args) {
      const context = originalContext.call(this, type, ...args);
      if (this.id === "unity-canvas" && /^webgl2?$/.test(type) && context) window.__viewportGl = context;
      return context;
    };
    // Observe the real instance once; diagnostics below only read its existing evidence method.
    const originalThen = Promise.prototype.then;
    Promise.prototype.then = function(ok, bad) {
      return originalThen.call(this, instance => {
        if (instance && typeof instance.SendMessage === "function") {
          window.__viewportUnity = instance;
          Promise.prototype.then = originalThen;
        }
        return typeof ok === "function" ? ok(instance) : instance;
      }, bad);
    };
  ` });
  await chrome.cdp.send('Page.navigate', { url: target.href });
  await until(() => value(chrome.cdp, 'Boolean(window.__viewportUnity && window.__viewportGl)'), 'actual Unity initialization');
  await until(async () => (await state()).hostTick > 0, 'actual game Update after the splash screen');
  const marker = 'Actor mode: local source mesh/textures, continuous original animation; explicit gameplay scripts/frames, no static fallback.';
  assert.ok(messages.some(message => message.includes(marker)), 'The actual animated source actor marker is required.');
  evidence.consoleMarkers.push(marker);
  evidence.renderer = await webglRenderer(chrome.cdp);
  evidence.chromeGpu = await chromeGpu(chrome.cdp);
  evidence.performanceQualification = 'Headless test renderer and RAF/GL draw cadence only; not a visible-preview GPU qualification or a controlled software/hardware speedup comparison.';
  await chrome.cdp.send('Page.bringToFront');
  await value(chrome.cdp, 'document.querySelector("#unity-canvas").focus()');

  // Opening kickoff runs automatically. Wait without keys for a stable fresh-match comparison.
  const fresh = await phase('DeadBall');
  evidence.freshMatch = { noGameplayInputBeforeLayout: true, state: summary(fresh) };
  const shots = new Map();
  for (const [width, height, dpr, label] of [
    [1920, 1080, 1, 'desktop-1920x1080'], [2560, 1080, 1, 'widescreen-2560x1080'],
    [1280, 800, 1, 'desktop-1280x800'], [360, 740, 1, 'portrait-360x740'],
    [1920, 1080, 1, 'desktop-return-1920x1080'], [1280, 800, 2, 'hidpi-1280x800-dpr2'],
    [360, 740, 2, 'portrait-360x740-dpr2'], [1920, 1080, 1, 'desktop-return-after-hidpi'],
  ]) {
    const metrics = await layout(width, height, dpr, label);
    assert.equal(metrics.focused, true, 'Focused canvas must remain borderless after every resize.');
    assert.equal(metrics.fullscreen, false, 'The host must not force the user into fullscreen.');
    const shot = await screenshot(label, metrics);
    assert.equal(shot.record.state.phase, fresh.phase, 'Resizing alone must preserve the same fresh-match phase.');
    shots.set(label, shot);
  }
  const desktopHud = await ocr(shots.get('desktop-1920x1080'));
  const portraitHud = await ocr(shots.get('portrait-360x740'));
  for (const property of ['phase', 'possession', 'homeScore', 'awayScore', 'quarter', 'clockSeconds']) {
    assert.equal(portraitHud[property], desktopHud[property], `Resize-only desktop/portrait HUD ${property}`);
  }
  evidence.performance.push(await sampleFrames(chrome.cdp, 'fresh-match-idle-1920x1080'));
  const beforeFullscreen = await layout(1920, 1080, 1, 'before-fullscreen');
  const entered = await chrome.cdp.send('Runtime.evaluate', {
    expression: 'document.querySelector("#unity-canvas").requestFullscreen().then(() => Boolean(document.fullscreenElement))',
    awaitPromise: true, returnByValue: true, userGesture: true,
  });
  assert.equal(entered.result.value, true, 'An ordinary user-requested canvas fullscreen must work.');
  const fullscreen = await layout(1920, 1080, 1, 'user-requested-fullscreen');
  assert.equal(fullscreen.fullscreen, true);
  await screenshot('user-requested-fullscreen', fullscreen);
  await value(chrome.cdp, 'document.exitFullscreen()');
  const exited = await layout(beforeFullscreen.innerWidth, beforeFullscreen.innerHeight, 1, 'fullscreen-exited');
  assert.equal(exited.fullscreen, false);

  await press(chrome.cdp, ' ');
  const selection = await phase('PlaySelect');
  evidence.controls.push({ input: 'Space', from: fresh.phase, state: summary(selection) });
  await press(chrome.cdp, '2');
  const selected = await phase('PreSnap');
  evidence.controls.push({ input: '2', from: selection.phase, state: summary(selected) });
  await screenshot('controls-selected-presnap', exited);
  await press(chrome.cdp, ' ');
  const snapped = await phase('LiveBall');
  evidence.controls.push({ input: 'Space / ordinary source snap', from: selected.phase, state: summary(snapped) });
  await press(chrome.cdp, 'w');
  evidence.controls.push({ input: 'W keydown and keyup', state: summary(await state()) });
  const action = await sampleFrames(chrome.cdp, 'ordinary-live-play-after-select-snap-and-W');
  action.stateBefore = summary(snapped);
  action.stateAfter = summary(await state());
  evidence.performance.push(action);
  for (const artifact of evidence.artifacts) {
    assert.ok(evidence.resources.some(resource => new URL(resource.url).pathname.endsWith('/' + artifact.path) &&
      resource.status === 200), `Chrome itself must load ${artifact.path}, not only the Node verifier.`);
  }
  assert.deepEqual(evidence.errors, []);
  assert.deepEqual(await readFile(path.join(artifacts, 'build-generation.txt')), proofBytes,
    'No game rebuild or payload change may happen during viewport qualification.');
  evidence.status = 'passed-full-viewport-actual-unity';
} catch (error) {
  failure = error;
  evidence.failure = error.stack ?? String(error);
  if (chrome) {
    try {
      const { data } = await chrome.cdp.send('Page.captureScreenshot', { format: 'png', captureBeyondViewport: false });
      await writeFile(path.join(directory, 'failure.png'), Buffer.from(data, 'base64'));
    } catch {
      // A disconnected browser still leaves the failed JSON contract, never a skipped/pass result.
    }
  }
} finally {
  if (chrome) await chrome.close();
  await writeFile(path.join(directory, 'report.json'), JSON.stringify(evidence, null, 2) + '\n');
}
console.log(JSON.stringify({ status: evidence.status, buildId: evidence.buildId,
  layouts: evidence.layouts.length, screenshots: evidence.screenshots.length,
  errors: evidence.errors.length, report: path.relative(root, path.join(directory, 'report.json')) }));
if (failure) throw failure;
