import assert from 'node:assert/strict';
import { execFile } from 'node:child_process';
import { mkdir, readFile, rm, writeFile } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { promisify, parseArgs } from 'node:util';
import { key, launchChrome, press, sleep, value } from './chrome.mjs';
import { assertNoRuntimeErrors, pngStats, readVisibleHud, requireHud, requireRenderedField, safeUrl } from './lib.mjs';

const exec = promisify(execFile);
const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const { values } = parseArgs({ options: {
  url: { type: 'string', default: 'http://127.0.0.1:8080/' },
  'artifact-root': { type: 'string', default: 'Build/WebGL' },
  'timeout-ms': { type: 'string', default: '180000' },
  'full-match': { type: 'boolean', default: false },
  'match-timeout-ms': { type: 'string', default: '1800000' },
} });
const targetUrl = new URL(values.url);
assert.ok(['http:', 'https:'].includes(targetUrl.protocol), 'Smoke URL must be an HTTP-served real Unity artifact.');
const timeout = Number(values['timeout-ms']);
const matchTimeout = Number(values['match-timeout-ms']);
assert.ok(Number.isFinite(timeout) && timeout >= 1000 && Number.isFinite(matchTimeout) && matchTimeout >= 1000);
const directory = path.join(root, 'tests/browser/artifacts', `game-${Date.now()}`);
await mkdir(directory, { recursive: true });
const evidence = {
  status: 'failed', url: safeUrl(targetUrl.href), timestampUtc: new Date().toISOString(),
  scope: 'Actual Unity WebGL and visible rendered HUD; keyboard controls only. No production state mutation.',
  checks: [], observations: [], errors: [], resources: [],
  fullMatch: { status: values['full-match'] ? 'pending' : 'not-requested', scoreObserved: false, finalRestart: 'not-run' },
};
let chrome;
let serial = 0;
const held = new Set();

async function viewport(width, height) {
  await chrome.cdp.send('Emulation.setDeviceMetricsOverride', { width, height, deviceScaleFactor: 1, mobile: false });
  await sleep(300);
  const canvas = await value(chrome.cdp, `(() => {
    const c = [...document.querySelectorAll('canvas')].find(e => e.getBoundingClientRect().width > 0);
    if (!c) return null;
    const b = c.getBoundingClientRect(), s = getComputedStyle(c);
    return { x:b.x, y:b.y, width:b.width, height:b.height, bufferWidth:c.width, bufferHeight:c.height,
      visible:s.visibility !== 'hidden' && s.display !== 'none', viewportWidth:innerWidth, viewportHeight:innerHeight };
  })()`);
  assert.ok(canvas?.visible && canvas.width >= 200 && canvas.height >= 200 && canvas.bufferWidth > 0 && canvas.bufferHeight > 0,
    `Real canvas must be visible: ${JSON.stringify(canvas)}`);
  assert.ok(canvas.x >= -1 && canvas.y >= -1 && canvas.x + canvas.width <= width + 1 && canvas.y + canvas.height <= height + 1,
    `Canvas must fit ${width}x${height}: ${JSON.stringify(canvas)}`);
  return canvas;
}

async function observe(label = 'observe') {
  assertNoRuntimeErrors(evidence.errors);
  const name = `${String(++serial).padStart(4, '0')}-${label.replace(/[^a-zA-Z0-9-]/g, '-')}`;
  const { data } = await chrome.cdp.send('Page.captureScreenshot', { format: 'png', captureBeyondViewport: false });
  const png = Buffer.from(data, 'base64');
  const screenshot = path.join(directory, `${name}.png`);
  await writeFile(screenshot, png);
  const dimensions = await value(chrome.cdp, '({width:innerWidth,height:innerHeight})');
  const { data: headerData } = await chrome.cdp.send('Page.captureScreenshot', {
    format: 'png', clip: { x: 0, y: 0, width: dimensions.width, height: Math.min(200, Math.floor(dimensions.height * 0.3)), scale: 1 },
  });
  const headerScreenshot = path.join(directory, `${name}-header.png`);
  await writeFile(headerScreenshot, Buffer.from(headerData, 'base64'));
  const ocrOptions = { maxBuffer: 1024 * 1024, env: { ...process.env, TMPDIR: directory, OMP_THREAD_LIMIT: '2' } };
  const [{ stdout }, { stdout: headerText }] = await Promise.all([
    exec('tesseract', [screenshot, 'stdout', '--psm', '11'], ocrOptions),
    exec('tesseract', [headerScreenshot, 'stdout', '--psm', '11'], ocrOptions),
  ]);
  await writeFile(path.join(directory, `${name}.txt`), stdout);
  await writeFile(path.join(directory, `${name}-header.txt`), headerText);
  const hud = readVisibleHud(stdout, headerText);
  const observation = { elapsedMs: Date.now() - started, screenshot: path.relative(root, screenshot), ...hud, pixels: pngStats(png) };
  evidence.observations.push(observation);
  if (Number(hud.homeScore) > 0 || Number(hud.awayScore) > 0) evidence.fullMatch.scoreObserved = true;
  return observation;
}

async function until(predicate, label, milliseconds = 30000) {
  const deadline = Date.now() + milliseconds;
  let latest;
  do {
    latest = await observe(label);
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

async function phase(expected, label, milliseconds) {
  return until(state => state.phase === expected, label, milliseconds);
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

const started = Date.now();
try {
  const artifactRoot = path.resolve(root, values['artifact-root']);
  assert.ok(artifactRoot.startsWith(`${root}${path.sep}`), 'Artifact must be inside the repository.');
  await exec('sh', [path.join(root, 'scripts/verify-webgl.sh'), artifactRoot],
    { cwd: root, env: { ...process.env, WEBGL_ALLOW_TEST_STUB: '0' } });
  const index = await readFile(path.join(artifactRoot, 'index.html'), 'utf8');
  assert.ok(!/WEBGL_HOSTING_TEST_STUB|hosting-test-stub/.test(index), 'Hosting test stubs are never Unity gameplay evidence.');
  await exec('tesseract', ['--version']);
  chrome = await launchChrome(directory);
  evidence.browser = chrome.version;
  const cdp = chrome.cdp;
  cdp.on('Runtime.exceptionThrown', entry => evidence.errors.push({ kind: 'exception', message: entry.exceptionDetails.text }));
  cdp.on('Runtime.consoleAPICalled', entry => {
    if (['error', 'assert'].includes(entry.type)) evidence.errors.push({ kind: 'console', message: entry.args.map(arg => arg.value ?? arg.description ?? arg.type).join(' ').slice(0, 2000) });
  });
  cdp.on('Log.entryAdded', ({ entry }) => {
    if (entry.level === 'error') evidence.errors.push({ kind: 'log', message: entry.text.slice(0, 2000), url: safeUrl(entry.url ?? '') });
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
  assert.ok(evidence.resources.some(item => /\.wasm$/.test(new URL(item.url).pathname) && item.status === 200 &&
    item.mimeType === 'application/wasm'), 'Actual WebAssembly must load with application/wasm and HTTP 200.');
  assert.ok(evidence.resources.some(item => /\.data$/.test(new URL(item.url).pathname) && item.status === 200),
    'Actual Unity data archive must load successfully.');
  const desktop = await viewport(1280, 800);
  evidence.checks.push({ name: 'real-unity-load-and-desktop-canvas', status: 'passed', canvas: desktop });
  await cdp.send('Input.dispatchMouseEvent', { type: 'mousePressed', x: desktop.x + desktop.width / 2, y: desktop.y + desktop.height / 2, button: 'left', clickCount: 1 });
  await cdp.send('Input.dispatchMouseEvent', { type: 'mouseReleased', x: desktop.x + desktop.width / 2, y: desktop.y + desktop.height / 2, button: 'left', clickCount: 1 });
  await press(cdp, '2');
  await phase('PreSnap', 'quick-pass-selected');
  await press(cdp, ' ');
  await phase('LiveBall', 'snapped');
  await press(cdp, '1');
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
  const defense = await phase('LiveBall', 'away-auto-snap', 10000);
  assert.equal(defense.possession, 'Away');
  assert.ok(Number.isInteger(defense.controlledPlayerId) && defense.controlledPlayerId < 7, 'Home defensive control marker must be readable.');
  await press(cdp, 'q');
  const switched = await observe('defender-switched');
  assert.notEqual(switched.controlledPlayerId, defense.controlledPlayerId, 'Q must visibly change the controlled Home defender.');
  assert.ok(Number.isInteger(switched.controlledPlayerId) && switched.controlledPlayerId < 7);
  await press(cdp, 'f');
  await hold(['s', 'Shift'], () => phase('DeadBall', 'defensive-play-dead', 45000));
  evidence.checks.push({ name: 'real-turnover-home-defense-switch-and-tackle-input', status: 'passed',
    note: 'Home defensive possession/control/switch observed; F sent, automatic contact may be the terminal tackle.' });
  await press(cdp, 'r');
  const restarted = await until(item => item.phase === 'PlaySelect' && item.possession === 'Home' &&
    item.homeScore === 0 && item.awayScore === 0, 'restart');
  requireRenderedField(restarted.pixels);
  const small = await viewport(360, 740);
  const smallHud = await observe('small-viewport');
  requireRenderedField(smallHud.pixels);
  requireHud(smallHud);
  evidence.checks.push({ name: 'small-canvas-hud-and-explicit-restart', status: 'passed', canvas: small });
  await viewport(1280, 800);
  if (values['full-match']) {
    const end = Date.now() + matchTimeout;
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
  console.log(`Actual WebGL smoke passed: ${evidence.checks.length} checks. Full match: ${evidence.fullMatch.status}.`);
} catch (error) {
  evidence.error = error.stack ?? String(error);
  console.error(`WEBGL SMOKE FAILED: ${error.message}`);
  process.exitCode = 1;
} finally {
  if (chrome) {
    for (const name of held) { try { await key(chrome.cdp, name, false); } catch {} }
    await chrome.close();
  }
  for (const name of ['profile', 'cache', 'config']) await rm(path.join(directory, name), { recursive: true, force: true });
  evidence.elapsedMs = Date.now() - started;
  await writeFile(path.join(directory, 'evidence.json'), `${JSON.stringify(evidence, null, 2)}\n`);
  console.log(`Evidence: ${path.relative(root, path.join(directory, 'evidence.json'))}`);
}
