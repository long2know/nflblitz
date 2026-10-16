import assert from 'node:assert/strict';
import { execFile } from 'node:child_process';
import { createHash } from 'node:crypto';
import { mkdir, readFile, writeFile } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { promisify, parseArgs } from 'node:util';
import { key, launchChrome, press, sleep, targetSession, value } from './chrome.mjs';
import { actorModeFromUrl, assertNoRuntimeErrors, cropPng, decodePng, fieldPlayerOffset, parseGenerationProof, parseHud, parseOcrTsv, phaseRect, pngStats, readVisibleHud, requireActorMarker, requireFinalPresentation, requireFreshMatchHud, requireHud, requireNarrowControls, requireRenderedField, safeUrl, scoreRects, tightScorePng, turboBarFraction } from './lib.mjs';

const exec = promisify(execFile);
const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const { values } = parseArgs({ options: {
  url: { type: 'string', default: 'http://127.0.0.1:8080/' },
  'artifact-root': { type: 'string', default: 'Build/WebGL' },
  'timeout-ms': { type: 'string', default: '180000' },
  'full-match': { type: 'boolean', default: false },
  'focus-layout-only': { type: 'boolean', default: false },
  'visual-controls-only': { type: 'boolean', default: false },
  'expected-source-pose': { type: 'string', default: 'bind-pose' },
  'final-presentation': { type: 'boolean', default: false },
  'match-timeout-ms': { type: 'string', default: '900000' },
} });
const targetUrl = new URL(values.url);
const actorMode = actorModeFromUrl(targetUrl.href);
assert.ok(['bind-pose', 'qbstance-frame0'].includes(values['expected-source-pose']), 'Expected source pose must be bind-pose or qbstance-frame0.');
assert.ok(['http:', 'https:'].includes(targetUrl.protocol), 'Smoke URL must be an HTTP-served real Unity artifact.');
assert.ok(!(values['full-match'] && values['focus-layout-only']), 'Focus/layout subset cannot claim a full match.');
assert.ok(!(values['visual-controls-only'] && (values['full-match'] || values['focus-layout-only'])),
  'Visual controls subset cannot claim full-match or focus-position qualification.');
assert.ok(!values['visual-controls-only'] || actorMode, 'Visual controls subset requires explicit actors=source or actors=graybox.');
assert.ok(!values['final-presentation'] || values['visual-controls-only'], 'Final presentation requires the explicit visual-controls subset.');
const timeout = Number(values['timeout-ms']);
const matchTimeout = Number(values['match-timeout-ms']);
assert.ok(Number.isFinite(timeout) && timeout >= 1000 && Number.isFinite(matchTimeout) && matchTimeout >= 1000);
const directory = path.join(root, 'tests/browser/artifacts', `game-${Date.now()}`);
await mkdir(directory, { recursive: true });
const evidence = {
  status: 'failed', url: safeUrl(targetUrl.href), timestampUtc: new Date().toISOString(),
  scenario: values['visual-controls-only'] ? 'actor-visual-controls-subset'
    : values['focus-layout-only'] ? 'focus-layout-only-subset' : 'full-functional-smoke',
  requestedActorMode: actorMode, actorMarkers: [], warnings: [],
  expectedSourcePose: actorMode === 'source' ? values['expected-source-pose'] : null,
  finalPresentationRequested: values['final-presentation'],
  notRun: values['visual-controls-only'] ? [
    'Legacy orthographic field-offset focus proof is NOT qualified for the new perspective camera; no held-position/focus success is inferred.',
    'Defense/scoring/full-match/final restart are outside this explicitly bounded actor visual/controls subset.',
    'Original standing, animations, team variants, stadium/HUD fidelity are NOT accepted or verified.',
  ] : [],
  scope: 'Actual Unity WebGL technical gameplay/input/render baseline; keyboard controls only. No production state mutation. NOT accepted NFL Blitz 2000 visual or full behavioral fidelity.',
  checks: [], observations: [], errors: [], resources: [],
  fullMatch: { status: values['full-match'] ? 'pending' : 'not-requested', scoreObserved: false, finalRestart: 'not-run' },
};
let chrome;
let serial = 0;
const held = new Set();

async function viewport(width, height, { diagnosticOnly = false } = {}) {
  await chrome.cdp.send('Emulation.setDeviceMetricsOverride', { width, height, deviceScaleFactor: 1, mobile: false });
  await sleep(300);
  const canvas = await value(chrome.cdp, `(() => {
    const c = [...document.querySelectorAll('canvas')].find(e => e.getBoundingClientRect().width > 0);
    if (!c) return null;
    const b = c.getBoundingClientRect(), s = getComputedStyle(c);
    return { x:b.x, y:b.y, width:b.width, height:b.height, bufferWidth:c.width, bufferHeight:c.height,
      visible:s.visibility !== 'hidden' && s.display !== 'none', viewportWidth:innerWidth, viewportHeight:innerHeight };
  })()`);
  const diagnostic = await observe(`viewport-${width}x${height}`, { diagnosticOnly });
  assert.ok(canvas?.visible && canvas.width >= 200 && canvas.height >= 200 && canvas.bufferWidth > 0 && canvas.bufferHeight > 0,
    `Real canvas must be visible: ${JSON.stringify(canvas)}; screenshot=${diagnostic.screenshot}`);
  assert.ok(canvas.x >= -1 && canvas.y >= -1 && canvas.x + canvas.width <= width + 1 && canvas.y + canvas.height <= height + 1,
    `Canvas must fit ${width}x${height}: ${JSON.stringify(canvas)}; screenshot=${diagnostic.screenshot}`);
  return canvas;
}

async function observe(label = 'observe', { diagnosticOnly = false, afterCapture } = {}) {
  const dimensions = await value(chrome.cdp, `(() => {
    const canvas = document.querySelector('canvas')?.getBoundingClientRect();
    return {width:innerWidth,height:innerHeight,canvas:canvas ? {x:canvas.x,y:canvas.y,width:canvas.width,height:canvas.height} : null};
  })()`);
  const name = `${String(++serial).padStart(4, '0')}-${label.replace(/[^a-zA-Z0-9-]/g, '-')}-${dimensions.width}x${dimensions.height}`;
  const { data } = await chrome.cdp.send('Page.captureScreenshot', { format: 'png', captureBeyondViewport: false });
  const capturedElapsedMs = Date.now() - started;
  const png = Buffer.from(data, 'base64');
  const screenshot = path.join(directory, `${name}.png`);
  await writeFile(screenshot, png);
  if (afterCapture) await afterCapture({ png, dimensions, screenshot });
  const image = decodePng(png);
  const canvas = dimensions.canvas ?? { x: 0, y: 0, width: dimensions.width, height: dimensions.height };
  const headerClip = { x: Math.max(0, canvas.x), y: Math.max(0, canvas.y),
    width: Math.min(canvas.width, dimensions.width - Math.max(0, canvas.x)),
    height: Math.min(200, canvas.height, dimensions.height - Math.max(0, canvas.y)),
    scale: canvas.width < 650 ? 2 : 1 };
  const headerScreenshot = path.join(directory, `${name}-header.png`);
  await writeFile(headerScreenshot, cropPng(image, headerClip));
  const ocrOptions = { maxBuffer: 1024 * 1024, env: { ...process.env, TMPDIR: directory, OMP_THREAD_LIMIT: '2' } };
  const scoreCrops = {};
  if (dimensions.canvas) {
    for (const [team, box] of Object.entries(scoreRects(dimensions.canvas))) {
      const clip = { ...box, scale: 2 };
      if (clip.x < 0 || clip.y < 0 || clip.x + clip.width > dimensions.width || clip.y + clip.height > dimensions.height) continue;
      const file = path.join(directory, `${name}-${team}-score.png`);
      await writeFile(file, tightScorePng(cropPng(image, clip)));
      scoreCrops[team] = { screenshot: path.relative(root, file), file, clip,
        processing: 'Trim white score-glyph bounds with six-pixel padding; pixels/digit values are not replaced.' };
    }
  }
  const scoreText = {};
  const scoreOcr = Promise.all(Object.entries(scoreCrops).map(async ([team, crop]) => {
    const { stdout } = await exec('tesseract', [crop.file, 'stdout', '--psm', '8', '-c', 'tessedit_char_whitelist=0123456789'], ocrOptions);
    scoreText[team] = stdout;
    delete crop.file;
    await writeFile(path.join(directory, `${name}-${team}-score.txt`), stdout);
  }));
  const [{ stdout: tsv }, { stdout: headerText }] = await Promise.all([
    exec('tesseract', [screenshot, 'stdout', '--psm', '11', 'tsv'], ocrOptions),
    exec('tesseract', [headerScreenshot, 'stdout', '--psm', '11'], ocrOptions),
  ]);
  await scoreOcr;
  const { text: stdout, words } = parseOcrTsv(tsv);
  await writeFile(path.join(directory, `${name}.txt`), stdout);
  await writeFile(path.join(directory, `${name}.tsv`), tsv);
  await writeFile(path.join(directory, `${name}-header.txt`), headerText);
  const hud = readVisibleHud(stdout, headerText, scoreText);
  const observation = { capturedElapsedMs, elapsedMs: Date.now() - started, screenshot: path.relative(root, screenshot),
    viewport: dimensions, headerClip, scoreCrops, ...hud, pixels: pngStats(image),
    turboBarFraction: dimensions.canvas?.width >= 1000 && dimensions.canvas.height >= 520
      ? turboBarFraction(image, dimensions.canvas) : null,
    landmarks: words.filter(word => /^(YOU[:/]?|SCRIMMAGE|FIRST|DOWN)$/i.test(word.text)) };
  evidence.observations.push(observation);
  if (Number(hud.homeScore) > 0 || Number(hud.awayScore) > 0) evidence.fullMatch.scoreObserved = true;
  if (!diagnosticOnly) assertNoRuntimeErrors(evidence.errors);
  return observation;
}

async function until(predicate, label, milliseconds = 30000, options) {
  const deadline = Date.now() + milliseconds;
  let latest;
  do {
    latest = await observe(label, options);
    if (predicate(latest)) return latest;
    await sleep(250);
  } while (Date.now() < deadline);
  throw new Error(`Timed out waiting for ${label}. Last rendered HUD: ${JSON.stringify(latest)}`);
}

async function hold(names, action) {
  try {
    for (const name of names) { await key(chrome.cdp, name, true); held.add(name); }
    return await action();
  } finally {
    for (const name of names) { await key(chrome.cdp, name, false); held.delete(name); }
  }
}

async function phase(expected, label, milliseconds, options) {
  return until(state => state.phase === expected, label, milliseconds, options);
}

async function fastPhase({ png, dimensions, screenshot }) {
  const image = screenshot.replace(/\.png$/, '-phase-reactor.png');
  await writeFile(image, cropPng(png, phaseRect(dimensions.canvas)));
  const { stdout } = await exec('tesseract', [image, 'stdout', '--psm', '7'], {
    env: { ...process.env, TMPDIR: directory, OMP_THREAD_LIMIT: '2' },
  });
  await writeFile(image.replace(/\.png$/, '.txt'), stdout);
  const result = parseHud(stdout).phase;
  assert.ok(result, `Cannot read real phase reactor crop ${image}: ${JSON.stringify(stdout)}`);
  return result;
}

async function playLateral(state) {
  if (state.phase === 'DeadBall') { await press(chrome.cdp, ' '); state = await phase('PlaySelect', 'continue'); }
  assert.equal(state.phase, 'PlaySelect');
  await press(chrome.cdp, '1');
  await phase('PreSnap', 'run-selected');
  if (state.possession === 'Home') await press(chrome.cdp, ' ');
  await phase('LiveBall', 'run-live');
  return hold(['d', 'Shift'], () => phase('DeadBall', 'run-dead', 45000));
}

async function focusSmoke(canvas) {
  await press(chrome.cdp, '2');
  await phase('PreSnap', 'focus-pass-selected');
  const focusKeys = ['w', 'Shift'];
  let otherTarget;
  let hiddenStarted;
  try {
    await press(chrome.cdp, ' ');
    const snapped = await observe('focus-snapped', { afterCapture: async () => {
      await press(chrome.cdp, '1');
      for (const name of focusKeys) { await key(chrome.cdp, name, true); held.add(name); }
    } });
    assert.equal(snapped.phase, 'LiveBall', `Focus fixture must visibly snap: ${snapped.screenshot}`);
    const setup = await until(item => item.passCaught || item.phase === 'DeadBall', 'focus-caught-receiver', 15000);
    assert.ok(setup.passCaught && setup.phase === 'LiveBall' && setup.controlledPlayerId === 4,
      `Focus fixture needs a real live caught receiver, not a vulnerable idle QB: ${setup.screenshot}`);
    await sleep(800);
    const before = await observe('focus-live', { afterCapture: async () => {
      otherTarget = (await chrome.cdp.send('Target.createTarget', { url: 'about:blank', background: false })).targetId;
      await chrome.cdp.send('Target.activateTarget', { targetId: otherTarget });
      await sleep(200);
      const other = await targetSession(chrome.cdp, otherTarget);
      for (const name of focusKeys) { await key(other, name, false); held.delete(name); }
      hiddenStarted = Date.now();
    } });
    assert.equal(before.phase, 'LiveBall', `Focus fixture must hide a genuinely live play: ${before.screenshot}`);
    assert.ok(Number.isInteger(before.clockSeconds), `Unreadable match clock before focus test: ${before.screenshot}`);
    const hidden = await value(chrome.cdp, '({hidden:document.hidden, focused:document.hasFocus()})');
    assert.ok(hidden.hidden && !hidden.focused, `Focus test must really hide/blur the game: ${JSON.stringify(hidden)}`);
    await sleep(3500);
    const hiddenAfter = await value(chrome.cdp, '({hidden:document.hidden, focused:document.hasFocus()})');
    assert.ok(hiddenAfter.hidden && !hiddenAfter.focused, `Game must remain genuinely hidden until resume: ${JSON.stringify(hiddenAfter)}`);
    const observedHiddenMs = Date.now() - hiddenStarted;
    await chrome.cdp.send('Page.bringToFront');
    await chrome.cdp.send('Target.closeTarget', { targetId: otherTarget });
    otherTarget = null;
    await chrome.cdp.send('Input.dispatchMouseEvent', { type: 'mousePressed', x: canvas.x + canvas.width / 2,
      y: canvas.y + canvas.height / 2, button: 'left', clickCount: 1 });
    await chrome.cdp.send('Input.dispatchMouseEvent', { type: 'mouseReleased', x: canvas.x + canvas.width / 2,
      y: canvas.y + canvas.height / 2, button: 'left', clickCount: 1 });
    await sleep(150);
    let settled;
    const resumed = await observe('focus-return-no-held-keys', { afterCapture: async () => {
      settled = await observe('focus-return-control-released');
    } });
    assert.equal(resumed.phase, 'LiveBall', `Focus return must not fast-forward to dead ball: ${resumed.screenshot}`);
    assert.equal(resumed.quarter, before.quarter, `Focus return must not advance quarters: ${resumed.screenshot}`);
    assert.ok(Number.isInteger(resumed.clockSeconds) && before.clockSeconds - resumed.clockSeconds >= 0 &&
      before.clockSeconds - resumed.clockSeconds <= 1,
      `Focus return must discard hidden-tab catch-up time: ${resumed.screenshot}`);
    evidence.checks.push({ name: 'actual-hidden-tab-no-wall-time-catchup', status: 'passed',
      requestedHiddenMs: 3500, observedHiddenMs, hidden, hiddenAfter,
      before: before.screenshot, resumed: resumed.screenshot,
      beforeClockSeconds: before.clockSeconds, resumedClockSeconds: resumed.clockSeconds,
      precision: 'Visible clock has one-second resolution; at most one foreground/boundary second is allowed.' });
    assert.equal(settled.phase, 'LiveBall', `Input-release comparison needs a live controlled carrier: ${settled.screenshot}`);
    assert.ok(before.turboBarFraction < setup.turboBarFraction - 0.02,
      `Held Shift must visibly drain the real turbo bar: ${before.screenshot}`);
    assert.ok(settled.turboBarFraction > resumed.turboBarFraction + 0.01,
      `Released movement/Shift must let the real turbo bar recharge: ${settled.screenshot}`);
    evidence.checks.push({ name: 'actual-keyboard-turbo-drain-and-release-recharge', status: 'passed',
      setup: setup.turboBarFraction, held: before.turboBarFraction,
      resumed: resumed.turboBarFraction, released: settled.turboBarFraction,
      screenshot: settled.screenshot });
    const first = fieldPlayerOffset(resumed, canvas);
    const second = fieldPlayerOffset(settled, canvas);
    assert.equal(first.anchor, second.anchor, 'Both release observations must use the same static field landmark.');
    assert.ok(Math.abs(first.x - second.x) <= 10 && Math.abs(first.y - second.y) <= 10,
      `Released W must not keep moving the carrier relative to the fixed field: ${JSON.stringify({ first, second, resumed: resumed.screenshot, settled: settled.screenshot })}`);
    evidence.checks.push({ name: 'actual-tab-blur-held-key-release-resume-without-catchup-or-stuck-movement', status: 'passed',
      observedHiddenMs, held: focusKeys, releasedIn: 'active other tab', blurred: before.screenshot,
      resumed: resumed.screenshot, settled: settled.screenshot, playerOffset: { first, second },
      crossReference: 'Actual-source FixedStepBridge checks separately prove queue clearing and six-step catch-up cap.' });
  } finally {
    for (const name of focusKeys) {
      if (held.has(name)) { await key(chrome.cdp, name, false); held.delete(name); }
    }
    if (otherTarget) await chrome.cdp.send('Target.closeTarget', { targetId: otherTarget });
    await chrome.cdp.send('Page.bringToFront');
  }
}

async function scoreSmoke(state) {
  const deadline = Date.now() + 120000;
  while (!evidence.fullMatch.scoreObserved && Date.now() < deadline) {
    if (state.phase === 'GameOver') break;
    if (state.phase === 'DeadBall') {
      await press(chrome.cdp, ' ');
      state = await phase('PlaySelect', 'scoring-next-play');
    }
    assert.equal(state.phase, 'PlaySelect', `Score drive needs the actual next-play selection: ${state.screenshot}`);
    await press(chrome.cdp, state.possession === 'Home' ? '2' : '1');
    await phase('PreSnap', 'scoring-selected');
    if (state.possession === 'Home') await press(chrome.cdp, ' ');
    const live = await phase('LiveBall', 'scoring-live', 10000);
    if (live.possession === 'Home') await press(chrome.cdp, '1');
    state = await hold([live.possession === 'Home' ? 'w' : 's', 'Shift'],
      () => phase('DeadBall', 'scoring-play-result', Math.min(45000, Math.max(1000, deadline - Date.now()))));
  }
  assert.ok(evidence.fullMatch.scoreObserved,
    `No real non-zero scoreboard was observed during the bounded two-minute drive; latest image=${state.screenshot}`);
  const scoring = evidence.observations.find(item => item.homeScore > 0 || item.awayScore > 0);
  evidence.checks.push({ name: 'actual-nonzero-scoreboard-after-normal-gameplay', status: 'passed',
    screenshot: scoring.screenshot, homeScore: scoring.homeScore, awayScore: scoring.awayScore });
}

async function verifyServedGeneration(artifactRoot) {
  const receiptBytes = await readFile(path.join(artifactRoot, 'build-receipt.json'));
  const proofBytes = await readFile(path.join(artifactRoot, 'build-generation.txt'));
  const receipt = JSON.parse(receiptBytes);
  const artifacts = parseGenerationProof(receipt, proofBytes.toString('utf8'));
  const digest = bytes => createHash('sha256').update(bytes).digest('hex');
  const files = [];
  for (const [file, expectedHash] of artifacts) {
    const local = await readFile(path.join(artifactRoot, file));
    assert.equal(digest(local), expectedHash, `Local generation hash mismatch: ${file}`);
    if (file === 'index.html') continue; // Hosting intentionally injects responsive HTML/favicon only.
    const response = await fetch(new URL(file, targetUrl), { signal: AbortSignal.timeout(15000) });
    assert.equal(response.status, 200, `Served generation ${file} must return HTTP200.`);
    const bytes = Buffer.from(await response.arrayBuffer());
    const mime = response.headers.get('content-type')?.split(';')[0].toLowerCase();
    const expectedMime = file.endsWith('.wasm') ? ['application/wasm']
      : file.endsWith('.data') ? ['application/octet-stream'] : ['application/javascript', 'text/javascript'];
    assert.ok(expectedMime.includes(mime), `Wrong MIME for ${file}: ${mime}`);
    assert.ok(!response.headers.get('content-encoding') || response.headers.get('content-encoding') === 'identity',
      `Unexpected compression for ${file}`);
    assert.equal(bytes.length, local.length, `Served size differs from actual generation: ${file}`);
    assert.equal(digest(bytes), expectedHash, `Served payload is NOT this actual generation: ${file}`);
    files.push({ file, bytes: bytes.length, sha256: expectedHash, mime });
  }
  for (const [file, local] of [['build-receipt.json', receiptBytes], ['build-generation.txt', proofBytes]]) {
    const response = await fetch(new URL(file, targetUrl), { signal: AbortSignal.timeout(5000) });
    assert.equal(response.status, 200, `Served ${file} missing.`);
    const served = Buffer.from(await response.arrayBuffer());
    assert.equal(digest(served), digest(local), `Served ${file} is not the local validated generation.`);
  }
  evidence.generation = { receipt, proofSha256: digest(proofBytes), files,
    htmlQualification: 'Local index pinned by proof; served HTML has intentional hosting-only responsive/favicon injection.' };
  evidence.checks.push({ name: 'receipt-proof-and-four-served-payload-hashes-match', status: 'passed', buildId: receipt.buildId });
}

async function narrowLayoutSmoke() {
  const small = await viewport(360, 740);
  const hud = await observe('small-viewport');
  requireRenderedField(hud.pixels);
  requireFreshMatchHud(hud, hud.screenshot);
  const footer = Math.min(small.height, 252 * Math.max(0.5, small.width / 480));
  const screenshot = path.join(directory, 'narrow-controls-360x740.png');
  await writeFile(screenshot, cropPng(await readFile(path.join(root, hud.screenshot)),
    { x: small.x, y: small.y + small.height - footer, width: small.width, height: footer, scale: 2 }));
  const { stdout } = await exec('tesseract', [screenshot, 'stdout', '--psm', '11'], {
    env: { ...process.env, TMPDIR: directory, OMP_THREAD_LIMIT: '2' },
  });
  await writeFile(screenshot.replace(/\.png$/, '.txt'), stdout);
  requireNarrowControls(stdout, screenshot);
  evidence.checks.push({ name: 'portrait-canvas-hud-and-readable-play-move-action-controls', status: 'passed',
    canvas: small, screenshot: hud.screenshot });
  return hud;
}

async function verifyFinalPresentation(state) {
  const canvas = state.viewport.canvas;
  const scale = canvas.width < 650 ? Math.max(0.5, canvas.width / 480) : Math.min(1.4, Math.max(0.82, canvas.width / 1280));
  const narrow = canvas.width / scale < 650;
  let footer = canvas.width >= 1000 && canvas.height >= 520 ? 88 : narrow ? 252 : 184;
  if (canvas.height / scale < 500) footer = 126;
  footer = Math.min(canvas.height, footer * scale);
  const image = decodePng(await readFile(path.join(root, state.screenshot)));
  const name = narrow ? 'portrait' : 'desktop';
  const header = path.join(directory, `final-${name}-title-ocr.png`);
  const legend = path.join(directory, `final-${name}-axes-ocr.png`);
  await writeFile(header, cropPng(image, { x: canvas.x, y: canvas.y,
    width: canvas.width, height: Math.min(200, canvas.height), scale: 2 }));
  await writeFile(legend, cropPng(image, { x: canvas.x, y: canvas.y + canvas.height - footer,
    width: canvas.width, height: footer, scale: 2 }));
  const options = { env: { ...process.env, TMPDIR: directory, OMP_THREAD_LIMIT: '2' } };
  const [{ stdout: headerText }, { stdout: legendText }] = await Promise.all([
    exec('tesseract', [header, 'stdout', '--psm', '11'], options),
    exec('tesseract', [legend, 'stdout', '--psm', '11'], options),
  ]);
  await writeFile(header.replace(/\.png$/, '.txt'), headerText);
  await writeFile(legend.replace(/\.png$/, '.txt'), legendText);
  const title = await value(chrome.cdp, 'document.title');
  requireFinalPresentation(headerText, legendText, title, narrow, state.screenshot);
  evidence.presentationProofs ??= [];
  evidence.presentationProofs.push({ viewport: state.viewport, screenshot: state.screenshot,
    documentTitle: title, headerText, legendText, compactSubtitleOmission: narrow });
}

async function visualControlsSmoke() {
  evidence.actorLimitations = actorMode === 'graybox' ? 'Explicit procedural graybox; not source art or historical fidelity.'
    : values['expected-source-pose'] === 'qbstance-frame0'
      ? 'Static original QBSTANCE frame0 source pose; one Bears variant for all14. No original continuous animation, verified pre-snap/idle pose, jersey overlay or full stadium/HUD fidelity acceptance.'
      : 'Authored identity bind pose with raised/upward limbs/feet; one Bears variant for all14. No original standing/animation, jersey overlay or full stadium/HUD fidelity acceptance.';
  const label = actorMode === 'source' ? `source-${values['expected-source-pose']}` : 'graybox';
  const save = async (name, state) => {
    const file = path.join(directory, `${label}-${name}.png`);
    await writeFile(file, await readFile(path.join(root, state.screenshot)));
    evidence.cleanScreenshots ??= {};
    evidence.cleanScreenshots[name] = path.relative(root, file);
  };
  const initial = await observe('visual-desktop-play-select');
  if (values['final-presentation']) await verifyFinalPresentation(initial);
  await save('desktop-play-select', initial);
  const portrait = await narrowLayoutSmoke();
  if (values['final-presentation']) await verifyFinalPresentation(portrait);
  await save('portrait-play-select', portrait);
  const desktop = await viewport(1280, 800);
  await chrome.cdp.send('Input.dispatchMouseEvent', { type: 'mousePressed', x: desktop.x + desktop.width / 2,
    y: desktop.y + desktop.height / 2, button: 'left', clickCount: 1 });
  await chrome.cdp.send('Input.dispatchMouseEvent', { type: 'mouseReleased', x: desktop.x + desktop.width / 2,
    y: desktop.y + desktop.height / 2, button: 'left', clickCount: 1 });
  await press(chrome.cdp, '2');
  await save('pre-snap', await phase('PreSnap', 'visual-quick-pass-selected'));
  await press(chrome.cdp, ' ');
  let caught, dead;
  try {
    const snapped = await observe('visual-snapped', { afterCapture: async () => {
      await press(chrome.cdp, '1');
      for (const name of ['a', 'Shift']) { await key(chrome.cdp, name, true); held.add(name); }
    } });
    assert.equal(snapped.phase, 'LiveBall', `Keyboard snap must visibly enter LiveBall: ${snapped.screenshot}`);
    await save('live-snapped', snapped);
    caught = await until(state => state.passCaught || state.phase === 'DeadBall', 'visual-pass-catch', 20000);
    assert.ok(caught.passCaught, `Target1 pass must actually be caught, not inferred from key dispatch: ${caught.screenshot}`);
    await save('pass-caught', caught);
    dead = caught.phase === 'DeadBall' ? caught : await phase('DeadBall', 'visual-run-dead-ball', 45000);
    await save('dead-ball', dead);
  } finally {
    for (const name of ['a', 'Shift']) {
      if (held.has(name)) { await key(chrome.cdp, name, false); held.delete(name); }
    }
  }
  evidence.checks.push({ name: 'actor-mode-keyboard-select-snap-target-pass-caught-run-deadball', status: 'passed',
    caught: caught.screenshot, caughtPhase: caught.phase, deadBall: dead.screenshot });
  await press(chrome.cdp, ' ');
  const next = await phase('PlaySelect', 'visual-next-play');
  assert.equal(next.down, dead.down, `Continue must keep the actual next down: ${next.screenshot}`);
  await press(chrome.cdp, 'r');
  const restarted = await until(state => state.phase === 'PlaySelect' && state.possession === 'Home' &&
    state.homeScore === 0 && state.awayScore === 0 && state.quarter === 1 && state.clockSeconds === 120 && state.down === 1,
  'visual-restarted');
  requireRenderedField(restarted.pixels);
  await save('restarted-desktop', restarted);
  evidence.checks.push({ name: 'actor-mode-continue-next-play-and-keyboard-restart', status: 'passed',
    nextPlay: next.screenshot, restarted: restarted.screenshot });
}

const started = Date.now();
try {
  const artifactRoot = path.resolve(root, values['artifact-root']);
  assert.ok(artifactRoot.startsWith(`${root}${path.sep}`), 'Artifact must be inside the repository.');
  await exec('sh', [path.join(root, 'scripts/verify-webgl.sh'), artifactRoot],
    { cwd: root, env: { ...process.env, WEBGL_ALLOW_TEST_STUB: '0' } });
  const index = await readFile(path.join(artifactRoot, 'index.html'), 'utf8');
  assert.ok(!/WEBGL_HOSTING_TEST_STUB|hosting-test-stub/.test(index), 'Hosting test stubs are never Unity gameplay evidence.');
  if (values['visual-controls-only']) await verifyServedGeneration(artifactRoot);
  await exec('tesseract', ['--version']);
  let health;
  try { health = await fetch(targetUrl, { signal: AbortSignal.timeout(5000) }); }
  catch (error) {
    throw new Error(`Frontend health unavailable at ${safeUrl(targetUrl.href)}: ${error.cause?.message ?? error.message}`);
  }
  assert.ok(health.ok, `Frontend health returned HTTP ${health.status} for ${safeUrl(targetUrl.href)}`);
  chrome = await launchChrome(directory);
  evidence.browser = chrome.version;
  evidence.launch = chrome.launch;
  const cdp = chrome.cdp;
  cdp.on('Runtime.exceptionThrown', entry => evidence.errors.push({ kind: 'exception', message: entry.exceptionDetails.text }));
  cdp.on('Runtime.consoleAPICalled', entry => {
    const message = entry.args.map(arg => arg.value ?? arg.description ?? arg.type).join(' ').slice(0, 4000);
    if (['error', 'assert'].includes(entry.type)) evidence.errors.push({ kind: 'console', message });
    if (message.includes('Actor mode:')) evidence.actorMarkers.push(message.trim());
    if (['warning', 'warn'].includes(entry.type) || /deprecated/i.test(message)) {
      evidence.warnings.push({ kind: entry.type, message });
    }
  });
  cdp.on('Log.entryAdded', ({ entry }) => {
    if (entry.level === 'error') evidence.errors.push({ kind: 'log', message: entry.text.slice(0, 2000), url: safeUrl(entry.url ?? '') });
    if (entry.level === 'warning') evidence.warnings.push({ kind: 'browser-warning', message: entry.text.slice(0, 2000) });
  });
  cdp.on('Network.loadingFailed', entry => evidence.errors.push({ kind: 'network-failed', message: entry.errorText, type: entry.type }));
  cdp.on('Network.responseReceived', ({ response }) => {
    const item = { url: safeUrl(response.url), status: response.status, mimeType: response.mimeType };
    evidence.resources.push(item);
    if (response.status >= 400) evidence.errors.push({ kind: 'http', ...item });
  });
  cdp.on('Inspector.targetCrashed', () => evidence.errors.push({ kind: 'crash', message: 'Unity browser target crashed.' }));
  await cdp.send('Emulation.setDeviceMetricsOverride', { width: 1280, height: 800, deviceScaleFactor: 1, mobile: false });
  await cdp.send('Page.navigate', { url: targetUrl.href });
  const loaded = await until(state => state.phase === 'PlaySelect' && state.possession === 'Home' &&
    state.homeScore === 0 && state.awayScore === 0, 'unity-loaded', timeout);
  requireHud(loaded);
  requireRenderedField(loaded.pixels);
  evidence.documentTitle = await value(cdp, 'document.title');
  if (actorMode) {
    evidence.validatedActorMarker = requireActorMarker(actorMode, evidence.actorMarkers, values['expected-source-pose']);
    evidence.checks.push({ name: 'explicit-actor-mode-validated-by-actual-unity-console', status: 'passed', actorMode,
      marker: evidence.validatedActorMarker });
  }
  assert.ok(evidence.resources.some(item => /\.wasm$/.test(new URL(item.url).pathname) && item.status === 200 &&
    item.mimeType === 'application/wasm'), 'Actual WebAssembly must load with application/wasm and HTTP 200.');
  assert.ok(evidence.resources.some(item => /\.data$/.test(new URL(item.url).pathname) && item.status === 200),
    'Actual Unity data archive must load successfully.');
  const desktop = await viewport(1280, 800);
  evidence.checks.push({ name: 'real-unity-load-and-desktop-canvas', status: 'passed', canvas: desktop });
  await cdp.send('Performance.enable');
  evidence.performance = await value(cdp, `new Promise(resolve => {
    const intervals = [];
    let previous, frame, finished = false;
    const done = reason => {
      if (finished) return;
      finished = true;
      clearTimeout(timer);
      cancelAnimationFrame(frame);
      const ordered = [...intervals].sort((a,b) => a-b);
      const mean = intervals.length ? intervals.reduce((a,b) => a+b,0)/intervals.length : null;
      resolve({method:'Page requestAnimationFrame callback intervals in loaded PlaySelect; NOT instrumented Unity FPS',
        reason, samples:intervals.length, meanMs:mean, observedCallbacksPerSecond:mean ? 1000/mean : null,
        p95Ms:ordered.length ? ordered[Math.ceil(ordered.length*0.95)-1] : null,
        maxMs:ordered.length ? ordered.at(-1) : null,
        browserJsHeap:performance.memory ? {usedBytes:performance.memory.usedJSHeapSize,
          totalBytes:performance.memory.totalJSHeapSize, limitBytes:performance.memory.jsHeapSizeLimit,
          limitation:'Chrome-reported JS heap only, not total process/GPU/WASM memory'} : null});
    };
    const timer = setTimeout(() => done('bounded-five-second-window'), 5000);
    const sample = now => {
      if (finished) return;
      if (previous !== undefined) intervals.push(now-previous);
      previous = now;
      if (intervals.length >= 120) done('120-interval-sample');
      else frame = requestAnimationFrame(sample);
    };
    frame = requestAnimationFrame(sample);
  })`);
  evidence.performance.cdpMetrics = (await cdp.send('Performance.getMetrics')).metrics;
  evidence.performance.webgl = await value(cdp, `(() => {
    const gl = document.querySelector('canvas').getContext('webgl2');
    if (!gl) return {status:'WebGL2 context details unavailable'};
    const extension = gl.getExtension('WEBGL_debug_renderer_info');
    return {vendor:gl.getParameter(gl.VENDOR),renderer:gl.getParameter(gl.RENDERER),
      unmaskedVendor:extension ? gl.getParameter(extension.UNMASKED_VENDOR_WEBGL) : null,
      unmaskedRenderer:extension ? gl.getParameter(extension.UNMASKED_RENDERER_WEBGL) : null};
  })()`);
  await cdp.send('Input.dispatchMouseEvent', { type: 'mousePressed', x: desktop.x + desktop.width / 2, y: desktop.y + desktop.height / 2, button: 'left', clickCount: 1 });
  await cdp.send('Input.dispatchMouseEvent', { type: 'mouseReleased', x: desktop.x + desktop.width / 2, y: desktop.y + desktop.height / 2, button: 'left', clickCount: 1 });
  if (!values['focus-layout-only'] && !values['visual-controls-only']) {
    await press(cdp, '2');
    await phase('PreSnap', 'quick-pass-selected');
    await press(cdp, ' ');
    const snapped = await observe('snapped', { afterCapture: () => press(cdp, '1') });
    assert.equal(snapped.phase, 'LiveBall', `Actual snap must be visible before the pass: ${snapped.screenshot}`);
    const caught = await until(state => state.passCaught || state.phase === 'DeadBall', 'pass-catch', 15000);
    assert.ok(caught.passCaught, 'A real pass/catch must be observed in the HUD, not inferred from a successful key event.');
    let dead = await hold(['d', 'Shift'], () => phase('DeadBall', 'first-dead-ball', 45000));
    evidence.checks.push({ name: 'select-snap-pass-catch-run-deadball', status: 'passed' });
    await press(cdp, ' ');
    let state = await phase('PlaySelect', 'next-play');
    evidence.checks.push({ name: 'continue-to-next-play', status: 'passed' });
    for (let play = 0; play < 12 && state.possession !== 'Away'; play++) {
      dead = await playLateral(state);
      await press(cdp, ' ');
      state = await phase('PlaySelect', 'possession-observation');
    }
    assert.equal(state.possession, 'Away', 'Real downs must transfer possession; no test-only possession switch is used.');
    await press(cdp, '2');
    await phase('PreSnap', 'home-blitz-selected');
    let switched;
    const defense = await phase('LiveBall', 'away-auto-snap', 10000, { afterCapture: async capture => {
      if (!switched && await fastPhase(capture) === 'LiveBall') {
        await press(cdp, 'q');
        switched = await observe('defender-switched', { afterCapture: () => press(cdp, 'f') });
      }
    } });
    assert.equal(defense.possession, 'Away');
    assert.ok(Number.isInteger(defense.controlledPlayerId) && defense.controlledPlayerId < 7, 'Home defensive control marker must be readable.');
    assert.ok(switched, 'Q switch must be dispatched from an actually visible live phase.');
    assert.equal(switched.phase, 'LiveBall', `A dead-ball reset is not proof of Q switching: ${switched.screenshot}`);
    assert.equal(switched.possession, 'Away', `Q must be observed during actual Home defense: ${switched.screenshot}`);
    assert.notEqual(switched.controlledPlayerId, defense.controlledPlayerId, 'Q must visibly change the controlled Home defender.');
    assert.ok(Number.isInteger(switched.controlledPlayerId) && switched.controlledPlayerId < 7);
    const defensiveDead = await hold(['s', 'Shift'], () => phase('DeadBall', 'defensive-play-dead', 45000));
    evidence.checks.push({ name: 'real-turnover-home-defense-switch-and-tackle-input', status: 'passed',
      note: 'Home defensive possession/control/switch observed; F sent, automatic contact may be the terminal tackle.' });
    await scoreSmoke(defensiveDead);
    await press(cdp, 'r');
    const restarted = await until(item => item.phase === 'PlaySelect' && item.possession === 'Home' &&
      item.homeScore === 0 && item.awayScore === 0, 'restart');
    requireRenderedField(restarted.pixels);
  }
  if (values['visual-controls-only']) {
    await visualControlsSmoke();
  } else {
    await focusSmoke(desktop);
    await press(cdp, 'r');
    await phase('PlaySelect', 'post-focus-restart');
    await narrowLayoutSmoke();
    await viewport(1280, 800);
  }
  if (values['full-match']) {
    evidence.fullMatch.status = 'running';
    const end = Date.now() + matchTimeout;
    let state;
    let final;
    while (Date.now() < end) {
      state = await observe('match');
      if (state.phase === 'GameOver') { final = state; break; }
      if (state.phase === 'PlaySelect') await press(cdp, '2');
      else if (state.phase === 'PreSnap' || state.phase === 'DeadBall') await press(cdp, ' ');
      else if (state.phase === 'LiveBall') {
        if (state.possession === 'Home') {
          await press(cdp, '1');
          await hold(['w', 'Shift'], () => sleep(1300));
        } else {
          await press(cdp, 'q');
          await press(cdp, 'f');
          await hold(['s', 'Shift'], () => sleep(1300));
        }
      }
      await sleep(200);
    }
    assert.ok(final, 'Four-quarter final was not observed within the explicit wall-clock budget.');
    assert.equal(final.quarter, 4, 'The visible final must be in quarter four.');
    await press(cdp, 'r');
    await until(item => item.phase === 'PlaySelect' && item.homeScore === 0 && item.awayScore === 0 &&
      item.possession === 'Home', 'final-restart');
    evidence.fullMatch.status = 'passed';
    evidence.fullMatch.finalRestart = 'passed';
    evidence.fullMatch.final = final;
  }
  assertNoRuntimeErrors(evidence.errors);
  evidence.status = 'passed';
  console.log(`Actual WebGL ${evidence.scenario} passed: ${evidence.checks.length} checks. Full match: ${evidence.fullMatch.status}.`);
} catch (error) {
  evidence.status = 'failed';
  if (evidence.fullMatch.status === 'pending') evidence.fullMatch.status = 'blocked-before-full-match';
  else if (evidence.fullMatch.status === 'running') evidence.fullMatch.status = 'failed';
  evidence.error = error.stack ?? String(error);
  console.error(`WEBGL SMOKE FAILED: ${error.message}`);
  process.exitCode = 1;
  if (chrome && !evidence.observations.at(-1)?.phase) {
    const deadline = Date.now() + 15000;
    try {
      do {
        const diagnostic = await observe('failed-run-render-diagnostic', { diagnosticOnly: true });
        if (diagnostic.phase && diagnostic.pixels.greenFraction >= 0.03) break;
        await sleep(500);
      } while (Date.now() < deadline);
    } catch (captureError) {
      evidence.diagnosticCaptureError = captureError.stack ?? String(captureError);
    }
  }
  if (chrome && evidence.observations.at(-1)?.phase && evidence.observations.at(-1).viewport.width !== 360) {
    try { await viewport(360, 740, { diagnosticOnly: true }); }
    catch (captureError) {
      evidence.narrowFailureDiagnostic = captureError.stack ?? String(captureError);
    }
  }
} finally {
  if (chrome) {
    for (const name of held) {
      try { await key(chrome.cdp, name, false); }
      catch (error) {
        evidence.errors.push({ kind: 'cleanup-key-release', key: name, message: error.message });
        evidence.status = 'failed';
        process.exitCode = 1;
      }
    }
    try { await chrome.close(); }
    catch (error) {
      evidence.errors.push({ kind: 'cleanup-browser', message: error.message });
      evidence.status = 'failed';
      process.exitCode = 1;
    }
  }
  evidence.elapsedMs = Date.now() - started;
  await writeFile(path.join(directory, 'evidence.json'), `${JSON.stringify(evidence, null, 2)}\n`);
  console.log(`Evidence: ${path.relative(root, path.join(directory, 'evidence.json'))}`);
}
