import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { mkdir, writeFile } from 'node:fs/promises';
import { Cdp, pngStats, requireRenderedField } from '../../tests/browser/lib.mjs';
import { value, sleep } from '../../tests/browser/chrome.mjs';
import { requireOriginalFrame, requirePortableReady, requireProgress, requireCausalMovement } from './assertions.mjs';

const args = process.argv.slice(2);
function option(name, fallback) {
  const index = args.indexOf(name);
  if (index < 0) return fallback;
  if (!args[index + 1] || args[index + 1].startsWith('--')) throw new Error(`Value required for ${name}`);
  return args[index + 1];
}
const url = option('--url', 'http://127.0.0.1:8082/');
const port = Number(option('--cdp-port', '9222'));
const diagnostic = args.includes('--diagnostic-input-only');
const artifacts = new URL('./artifacts/' + (diagnostic ? 'cabinet-diagnostic/' : 'portable-player/'), import.meta.url);
await mkdir(artifacts, { recursive: true });
const version = await (await fetch(`http://127.0.0.1:${port}/json/version`)).json();
const browser = await Cdp.connect(version.webSocketDebuggerUrl);
let page, targetId;
const messages = [], errors = [], networkErrors = [], observations = [], keyboard = [], network = [];
const evidence = {
  schema: 'blitz-portable-source-player-browser/v1', status: 'running', requestedMode: diagnostic ? 'strict-permitted input diagnostic' : 'portable playable slice',
  unityBuild: false, fullM1Qualification: false, nativeArcadeAcceptance: false,
  browserVersion: version.Browser, url, messages, errors, networkErrors, keyboard, observations, network,
};

async function snapshot() {
  const result = await value(page, 'globalThis.sourcePlayer?.snapshot()');
  if (result?.failed) throw new Error(await value(page, 'document.getElementById("status").textContent'));
  return result;
}

async function until(predicate, description, timeout = 30000) {
  const deadline = Date.now() + timeout;
  while (Date.now() < deadline) {
    const state = await snapshot();
    if (state?.state && predicate(state)) return state;
    if (errors.length || networkErrors.length) throw new Error(`${description}: actual browser/network error`);
    await sleep(100);
  }
  throw new Error(`Timed out: ${description}. No simulation-state/pose/ball override is allowed.`);
}

async function key(name, down) {
  const bindings = { w: ['KeyW', 87], a: ['KeyA', 65], s: ['KeyS', 83], d: ['KeyD', 68],
    v: ['KeyV', 86], b: ['KeyB', 66], n: ['KeyN', 78], r: ['KeyR', 82], Enter: ['Enter', 13], Escape: ['Escape', 27] };
  const [code, number] = bindings[name];
  keyboard.push({ name, down, wallMilliseconds: Date.now() });
  await page.send('Input.dispatchKeyEvent', {
    type: down ? 'keyDown' : 'keyUp', key: name, code, windowsVirtualKeyCode: number, nativeVirtualKeyCode: number,
  });
}
async function tap(name) {
  await key(name, true);
  try { await sleep(75); } finally { await key(name, false); }
  await sleep(75);
}

async function capture(name) {
  const combined = await value(page, '({snapshot:sourcePlayer.snapshot(),proof:sourcePlayer.gpuProof()})');
  requireOriginalFrame(combined.snapshot, combined.proof);
  const result = await page.send('Page.captureScreenshot', { format: 'png' });
  const png = Buffer.from(result.data, 'base64'), pixels = pngStats(png);
  requireRenderedField(pixels);
  await writeFile(new URL(name + '.png', artifacts), png);
  const after = await snapshot();
  const record = { name, before: combined.snapshot, gpuProof: combined.proof, pixels,
    screenshotSourceTickBracket: [combined.snapshot.state.sourceTick, after.state.sourceTick] };
  observations.push(record);
  return record;
}

try {
  ({ targetId } = await browser.send('Target.createTarget', { url: 'about:blank', newWindow: false }));
  const pages = await (await fetch(`http://127.0.0.1:${port}/json/list`)).json();
  page = await Cdp.connect(pages.find(p => p.id === targetId).webSocketDebuggerUrl);
  page.on('Runtime.consoleAPICalled', event => {
    const message = event.args.map(a => a.value ?? a.description ?? '').join(' ');
    messages.push(message);
    if (event.type === 'error') errors.push(message);
  });
  page.on('Runtime.exceptionThrown', event => errors.push(event.exceptionDetails));
  page.on('Network.loadingFailed', event => networkErrors.push(event));
  page.on('Network.responseReceived', event => network.push({
    path: new URL(event.response.url).pathname, status: event.response.status, mime: event.response.mimeType,
  }));
  await Promise.all([page.send('Page.enable'), page.send('Runtime.enable'), page.send('Network.enable')]);
  await page.send('Emulation.setDeviceMetricsOverride', { width: 1280, height: 1000, deviceScaleFactor: 1, mobile: false });
  await page.send('Page.navigate', { url });
  await page.send('Page.bringToFront');
  const loaded = await until(s => s.state.sourceTick >= 3, 'actual .NET source kernel/render startup', 180000);
  if (!diagnostic) requirePortableReady(loaded, messages);
  else if (!loaded.state.kernel.portableBound)
    assert.equal(messages.some(m => m.startsWith('SOURCE_PLAYER_READY ')), false,
      'A strict-source diagnostic must never claim portable player-ready status.');
  for (const assembly of loaded.build.assemblies) {
    const response = await fetch(new URL(assembly.path, url));
    assert.equal(response.status, 200, 'Real C# Webcil assembly must be served.');
    assert.match(response.headers.get('content-type'), /application\/wasm/, 'Actual C# assembly WASM MIME required.');
    const bytes = Buffer.from(await response.arrayBuffer());
    assert.equal(bytes.length, assembly.bytes);
    assert.equal(createHash('sha256').update(bytes).digest('hex'), assembly.sha256);
    assert(network.some(n => n.path.endsWith('/' + assembly.path) && n.status === 200),
      `The actual .NET loader did not request ${assembly.path}; a JS-only fallback cannot pass.`);
  }
  await capture('original-kickoff');
  await until(s => s.state.phase === 'LiveBall' && s.state.ball.mode === 'InFlight' && s.state.ball.kind === 'Kickoff',
    'actual original C# kickoff flight', 15000);
  const flight = await capture('original-source-ball-flight');
  assert.equal(flight.before.state.ball.mode, 'InFlight');
  assert(flight.gpuProof.ballPixels > 0, 'The real moving C# ball must produce original-geometry GPU pixels.');
  const before = await until(s => s.state.phase === 'LiveBall' && s.state.ball.carrier >= 0,
    'original kickoff catch and source human-control window', 20000);
  await key('w', true); await key('n', true);
  await sleep(650);
  const moving = await snapshot();
  await key('w', false); await key('n', false);
  requireProgress(before.state, moving.state);
  requireCausalMovement(before.state, moving.state);
  await tap('v');
  await capture('cabinet-moving-original-actors');
  evidence.cabinetMovement = { before: before.state, after: moving.state,
    method: 'Actual CDP keyboard events -> browser raw cabinet packet -> production SourceCabinetInputBuffer -> production M1 movement. No C# or JS state setters.' };
  if (!diagnostic) {
    let passed = false, snapped = false, tackle = false, possession = false, passCaptured = false, menuCaptured = false;
    let manualSnap, manualPass;
    const possessionBefore = loaded.state.possession;
    const deadline = Date.now() + 120000;
    while (Date.now() < deadline && !(passed && snapped && tackle && possession)) {
      const current = (await snapshot()).state;
      possession ||= current.possession !== possessionBefore || (current.eventCounts.PossessionChanged || 0) > 0;
      tackle ||= (current.contactCounts.ProgressStopped || 0) > 0 && current.events.some(e =>
        e.type === 'PlayResolved' && (e.cause === 'CarrierTackledInBounds' || e.cause === 'CarrierDownInOwnEndZone'));
      if (manualSnap) {
        manualPass ||= current.events.find(e => e.type === 'PassReleased' && e.team === 'Home' && e.actor === manualSnap.actor &&
          e.sourceTick > manualSnap.sourceTick);
        passed = !!manualPass;
        if (passed && !passCaptured) {
          await capture('actual-human-source-pass');
          passCaptured = true;
        }
      }
      if (current.phase === 'DeadBall') { await tap('Enter'); continue; }
      if (current.phase === 'PlaySelect') {
        if (!menuCaptured) { await capture('original-playbook-selection'); menuCaptured = true; }
        await sleep(450);
        await tap('b');
        continue;
      }
      if (current.phase === 'PreSnap' && current.playKind === 'Normal' && current.possession === 'Home') {
        const priorTick = current.sourceTick;
        await sleep(1100);
        await tap('b');
        const snapState = await until(s => s.state.events.some(e =>
          e.type === 'Snapped' && e.team === 'Home' && e.sourceTick > priorTick), 'real source manual Home snap');
        if (!passed)
          manualSnap = snapState.state.events.find(e => e.type === 'Snapped' && e.team === 'Home' && e.sourceTick > priorTick);
        snapped = true;
        await until(s => s.state.ball.mode === 'Held' && s.state.ball.carrier === manualSnap.actor ||
          s.state.phase === 'DeadBall', 'actual original hike reaches the human quarterback');
        await key('w', true); await key('b', true);
        await sleep(45);
        await key('b', false); await key('w', false);
        continue;
      }
      if (current.phase === 'LiveBall' && current.possession === 'Home' && current.playKind === 'Normal') {
        if (passed) {
          await key('w', true); await key('n', true); await sleep(500);
          await key('w', false); await key('n', false);
        } else {
          await key('w', true); await tap('b'); await key('w', false);
        }
      } else if (current.phase === 'LiveBall') {
        await key('w', true); await key('n', true); await tap('v'); await sleep(300);
        await key('w', false); await key('n', false);
      } else await sleep(150);
    }
    assert(snapped && passed && tackle && possession,
      `Ordinary source controls did not demonstrate every required slice outcome: ${JSON.stringify({ snapped, passed, tackle, possession })}`);
    evidence.slice = { snapped, passed, tackle, possession, manualSnap, manualPass,
      tackleAttribution: 'Actual ProgressStopped plus a classified carrier-tackle result was observed after real A-key input; no claim of exclusive A-only reach/parity. CPU contact may also terminate a play.' };
    await capture('actual-source-contact-possession-progress');
  }
  const active = (await snapshot()).state;
  await tap('Escape');
  const paused = (await snapshot()).state;
  await sleep(1200);
  const stillPaused = (await snapshot()).state;
  assert.equal(stillPaused.sourceTick, paused.sourceTick, 'Pause must stop source clock and simulation.');
  assert.equal(stillPaused.active, false);
  await tap('Escape');
  await key('w', false); await key('n', false);
  await key('w', true); await key('n', true);
  const beforeHide = await snapshot();
  let otherTarget, otherPage;
  try {
    ({ targetId: otherTarget } = await browser.send('Target.createTarget', { url: 'about:blank', newWindow: false }));
    const pages = await (await fetch(`http://127.0.0.1:${port}/json/list`)).json();
    otherPage = await Cdp.connect(pages.find(p => p.id === otherTarget).webSocketDebuggerUrl);
    await browser.send('Target.activateTarget', { targetId: otherTarget });
    for (const [keyName, code, number] of [['w', 'KeyW', 87], ['n', 'KeyN', 78]])
      await otherPage.send('Input.dispatchKeyEvent', {
        type: 'keyUp', key: keyName, code, windowsVirtualKeyCode: number, nativeVirtualKeyCode: number,
      });
    await sleep(1300);
    const hidden = await value(page, '({hidden:document.hidden,focused:document.hasFocus()})');
    assert(hidden.hidden && !hidden.focused, 'The actual game must really be hidden/unfocused in an owned second tab.');
    await page.send('Page.bringToFront');
    await sleep(80);
    const returned = await snapshot();
    assert(returned.state.sourceTick - beforeHide.state.sourceTick <= 12,
      'Hidden time incorrectly became source simulation time on return.');
    assert(returned.state.input.buttons.every(n => n === 0) && returned.state.input.axes.every(n => n === 0),
      'Controls released in the other actual tab remained stuck.');
    evidence.focus = { beforeSourceTick: beforeHide.state.sourceTick, returnedSourceTick: returned.state.sourceTick,
      hiddenMilliseconds: 1300, actualHiddenState: hidden, returnedInput: returned.state.input };
  } finally {
    otherPage?.close();
    if (otherTarget) await browser.send('Target.closeTarget', { targetId: otherTarget });
  }
  await page.send('Emulation.setDeviceMetricsOverride', { width: 390, height: 844, deviceScaleFactor: 1, mobile: false });
  await sleep(300);
  await capture('original-hud-portrait');
  assert.equal(errors.length, 0, 'Actual JavaScript/.NET console errors fail the player.');
  assert.equal(networkErrors.length, 0, 'Required resources may not fail silently.');
  evidence.status = diagnostic ? 'input-render-diagnostic-passed-NOT-player-ready' : 'portable-playable-slice-passed';
  evidence.portableQualified = !diagnostic;
  evidence.pause = { sourceTickBeforePause: active.sourceTick, pausedTick: paused.sourceTick, stillPausedTick: stillPaused.sourceTick };
  evidence.final = await snapshot();
} catch (error) {
  evidence.status = 'failed';
  evidence.error = String(error.stack || error);
  if (page) {
    try {
      evidence.final = await value(page, 'globalThis.sourcePlayer?.snapshot()');
      const screenshot = await page.send('Page.captureScreenshot', { format: 'png' });
      await writeFile(new URL('failure.png', artifacts), Buffer.from(screenshot.data, 'base64'));
    } catch (diagnosticError) { evidence.captureError = String(diagnosticError); }
  }
  process.exitCode = 1;
} finally {
  await writeFile(new URL('evidence.json', artifacts), JSON.stringify(evidence, null, 2) + '\n');
  page?.close();
  if (targetId) await browser.send('Target.closeTarget', { targetId });
  browser.close();
  console.log(JSON.stringify({ status: evidence.status, portableQualified: evidence.portableQualified ?? false,
    sourceTick: evidence.final?.state?.sourceTick, observations: observations.map(o => o.name), error: evidence.error }, null, 2));
}
