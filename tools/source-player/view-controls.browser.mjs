import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { mkdir, writeFile } from 'node:fs/promises';
import { join } from 'node:path';
import { Cdp, pngStats } from '../../tests/browser/lib.mjs';

const args = process.argv.slice(2);
const option = (name, fallback) => args.includes(name) ? args[args.indexOf(name) + 1] : fallback;
const url = option('--url', 'http://127.0.0.1:8082/');
const port = option('--cdp-port', '9222');
const directory = option('--evidence', 'tools/source-player/artifacts/game-view');
const sleep = ms => new Promise(resolve => setTimeout(resolve, ms));
const digest = bytes => createHash('sha256').update(bytes).digest('hex');
const version = await (await fetch(`http://127.0.0.1:${port}/json/version`)).json();
const browser = await Cdp.connect(version.webSocketDebuggerUrl);
await mkdir(directory, { recursive: true });
const evidence = { schema: 'source-player-game-view-browser/v1', url, browser: version.Browser,
  method: 'Fresh isolated browser context, actual C#/WebGL player and trusted CDP mouse/keyboard input; no gameplay setters.',
  screenshots: [], layouts: [], fullscreenEvents: [], input: [], errors: [] };
let context, target, page;

async function value(expression) {
  const result = await page.send('Runtime.evaluate', { expression, returnByValue: true, awaitPromise: true });
  if (result.exceptionDetails) throw new Error(JSON.stringify(result.exceptionDetails));
  return result.result.value;
}
async function until(expression, label, timeout = 20000) {
  const deadline = Date.now() + timeout;
  while (Date.now() < deadline) {
    const result = await value(expression);
    if (result) return result;
    await sleep(50);
  }
  evidence.atFailure = await value(`({
    status:document.querySelector('#status')?.textContent,
    observed:globalThis.sourcePlayer?.observe()
  })`);
  throw new Error(`Timed out: ${label}`);
}
async function key(code, key, number, down) {
  await page.send('Input.dispatchKeyEvent', {
    type: down ? 'keyDown' : 'keyUp', code, key, windowsVirtualKeyCode: number, nativeVirtualKeyCode: number,
    ...(down && key === 'Enter' ? { text: '\r', unmodifiedText: '\r' } : {}),
  });
}
async function tap(code, text, number) {
  await key(code, text, number, true);
  try { await sleep(120); } finally { await key(code, text, number, false); }
  await sleep(120);
}
async function click(id) {
  const point = await value(`(() => {
    const button = document.getElementById(${JSON.stringify(id)}), r = button.getBoundingClientRect();
    return { x: r.x + r.width / 2, y: r.y + r.height / 2, disabled: button.disabled };
  })()`);
  assert.equal(point.disabled, false, `${id} must be usable.`);
  for (const type of ['mousePressed', 'mouseReleased'])
    await page.send('Input.dispatchMouseEvent', { type, x: point.x, y: point.y, button: 'left', clickCount: 1 });
}
async function cabinetLeft(station, code, text, number) {
  const before = await value('sourcePlayer.observe().state.input.changes');
  await key(code, text, number, true);
  try {
    const observed = await until(`(() => {
      const input = sourcePlayer.observe().state.input;
      return input.axes[${station * 2}] === -1 && input;
    })()`, `real P${station + 1} left reaches the C# cabinet buffer`);
    assert(observed.changes > before);
    evidence.input.push({ station, code, observed, gameOnly: await value("document.documentElement.classList.contains('game-only')") });
  } finally { await key(code, text, number, false); }
  await until(`sourcePlayer.observe().state.input.axes[${station * 2}] === 0`, 'physical release reaches C#');
}
async function layout(name, gameOnly = true) {
  await value('new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)))');
  const observed = await value(`(() => {
    const canvas = document.querySelector('#game'), r = canvas.getBoundingClientRect();
    const button = document.querySelector('#game-view-toggle'), b = button.getBoundingClientRect();
    return { mode: document.documentElement.classList.contains('game-only'),
      width: innerWidth, height: innerHeight, dpr: devicePixelRatio,
      canvas: { x:r.x, y:r.y, width:r.width, height:r.height, pixels:[canvas.width,canvas.height] },
      scroll:[document.documentElement.scrollWidth,document.documentElement.scrollHeight],
      client:[document.documentElement.clientWidth,document.documentElement.clientHeight],
      exit:{ x:b.x, y:b.y, width:b.width, height:b.height, disabled:button.disabled, text:button.textContent },
      focus:document.activeElement?.id, ready:sourcePlayer.observe().ready, failed:sourcePlayer.observe().failed,
      actorCount:sourcePlayer.observe().state.actorCount, portableBound:sourcePlayer.observe().state.kernel.portableBound,
      diagnosticVisible:getComputedStyle(document.querySelector('#status')).display !== 'none' };
  })()`);
  observed.name = name;
  assert.equal(observed.mode, gameOnly, name);
  assert.equal(observed.portableBound, true, 'Test the actual bound C# player, not a loader or synthetic canvas.');
  assert.equal(observed.actorCount, 14);
  assert.equal(observed.failed, false);
  if (!observed.ready) assert.equal(observed.diagnosticVisible, true, 'Unqualified pregame diagnostics must stay visible.');
  assert(Math.abs(observed.canvas.width / observed.canvas.height - 4 / 3) < .002, `${name}: 4:3`);
  assert.deepEqual(observed.canvas.pixels,
    [Math.round(observed.canvas.width * observed.dpr), Math.round(observed.canvas.height * observed.dpr)],
    `${name}: existing renderer.resize must adopt CSS size and DPR.`);
  if (gameOnly) {
    const { canvas: c, exit: b, width, height } = observed;
    assert(c.x >= -.5 && c.y >= -.5 && c.x + c.width <= width + .5 && c.y + c.height <= height + .5, `${name}: no crop`);
    assert(Math.abs(c.x + c.width / 2 - width / 2) <= 1 && Math.abs(c.y + c.height / 2 - height / 2) <= 1, `${name}: centered`);
    assert(observed.scroll.every((size, i) => size <= observed.client[i] + 1), `${name}: no document overflow`);
    assert(b.x >= 0 && b.y >= 0 && b.x + b.width <= width + .5 && b.y + b.height <= height + .5 && !b.disabled,
      `${name}: Exit must remain reachable.`);
    assert.equal(b.text, 'Exit game view');
  }
  evidence.layouts.push(observed);
  return observed;
}
async function screenshot(name) {
  const { data } = await page.send('Page.captureScreenshot', { format: 'png' });
  const bytes = Buffer.from(data, 'base64'), path = join(directory, name + '.png');
  const stats = pngStats(bytes);
  assert(stats.colors > 25, 'Evidence must contain real rendered content.');
  await writeFile(path, bytes);
  evidence.screenshots.push({ name, path, sha256: digest(bytes), bytes: bytes.length, stats });
}

try {
  const command = await browser.send('Browser.getBrowserCommandLine');
  assert(!command.arguments.some(arg => /--no-sandbox|--disable-setuid-sandbox|--single-process|--ignore-certificate-errors/.test(arg)),
    'Use a normal sandboxed browser.');
  evidence.chromeArguments = command.arguments.filter(arg => !arg.startsWith('--user-data-dir='));
  ({ browserContextId: context } = await browser.send('Target.createBrowserContext'));
  ({ targetId: target } = await browser.send('Target.createTarget', { url: 'about:blank', browserContextId: context }));
  const pages = await (await fetch(`http://127.0.0.1:${port}/json/list`)).json();
  page = await Cdp.connect(pages.find(item => item.id === target).webSocketDebuggerUrl);
  page.on('Runtime.exceptionThrown', event => evidence.errors.push(event.exceptionDetails));
  page.on('Runtime.consoleAPICalled', event => {
    const text = event.args.map(arg => arg.value ?? arg.description ?? '').join(' ');
    if (event.type === 'error') evidence.errors.push(text);
  });
  await Promise.all([page.send('Page.enable'), page.send('Runtime.enable'), page.send('Network.enable')]);
  await page.send('Emulation.setDeviceMetricsOverride', { width: 1280, height: 900, deviceScaleFactor: 1, mobile: false });
  await page.send('Page.navigate', { url });
  await page.send('Page.bringToFront');
  await until(`(() => { const start = document.querySelector('#start-original-audio');
    if (globalThis.sourcePlayerLoader?.failed) throw new Error(document.querySelector('#status').textContent);
    return start && !start.hidden && !start.disabled; })()`, 'real public audio unlock', 180000);
  assert.equal(await value("document.querySelector('#game-view-toggle').disabled"), true);
  assert.equal(await value("getComputedStyle(document.querySelector('#status')).display !== 'none'"), true);
  await click('start-original-audio');
  await until('globalThis.sourcePlayer?.observe().state?.input', 'actual initialized C# input');
  for (const [station, code, text, number] of [
    [0, 'KeyA', 'a', 65], [1, 'ArrowLeft', 'ArrowLeft', 37], [2, 'KeyJ', 'j', 74], [3, 'KeyF', 'f', 70],
  ]) await cabinetLeft(station, code, text, number);
  await until("!document.querySelector('#game-view-toggle').disabled", 'actual initialized interactive C# view');
  evidence.qualification = 'Actual bound C#/WebGL interactive prematch; retain its diagnostic until gameplay qualifies Ready. No Ready mutation or whole-game acceptance claim.';
  evidence.prematchStage = await value('sourcePlayer.observe().state.cabinetSession.stage');
  await value(`document.addEventListener('fullscreenchange', () => {
    (globalThis.gameViewTestEvents ??= []).push({ fullscreen:document.fullscreenElement?.id ?? null,
      active:globalThis.sourcePlayer.observe().state.active });
  })`);
  evidence.runtime = await value(`(() => {
    const s = sourcePlayer.snapshot(); return { build:s.build, device:s.device,
      sourceTick:s.state.sourceTick, actorCount:s.state.actorCount, audio:s.audioReceiver };
  })()`);
  await layout('normal', false); await screenshot('normal');
  await value("document.querySelector('#game-view-toggle').focus()");
  assert.equal(await value('document.activeElement.id'), 'game-view-toggle');
  await tap('Enter', 'Enter', 13);
  await until("document.documentElement.classList.contains('game-only')", 'keyboard-accessible native button activation');
  assert.equal(await value('document.activeElement.id'), 'game');
  await layout('game-only'); await screenshot('game-only');
  await cabinetLeft(3, 'KeyF', 'f', 70);
  await tap('Escape', 'Escape', 27);
  await until('sourcePlayer.observe().state.active === false', 'existing Escape pause in game-only');
  await click('game-view-toggle');
  assert.equal(await value('sourcePlayer.observe().state.active'), false, 'Changing view must not unpause the game.');
  await click('game-view-toggle');
  assert.equal(await value('sourcePlayer.observe().state.active'), false);
  await tap('Escape', 'Escape', 27);
  await until('sourcePlayer.observe().state.active === true', 'existing Escape unpause after view restoration');
  for (const metrics of [
    { width: 480, height: 800, deviceScaleFactor: 2, mobile: false },
    { width: 900, height: 420, deviceScaleFactor: 1.5, mobile: false },
  ]) {
    await page.send('Emulation.setDeviceMetricsOverride', metrics);
    await layout(`${metrics.width}x${metrics.height}-dpr${metrics.deviceScaleFactor}`);
    await screenshot(`resized-${metrics.width}x${metrics.height}`);
  }
  await page.send('Emulation.setDeviceMetricsOverride', { width: 1280, height: 900, deviceScaleFactor: 1, mobile: false });
  await layout('restored-window');
  const supported = await value("typeof document.querySelector('#game-viewport').requestFullscreen === 'function' && document.fullscreenEnabled");
  evidence.nativeSupported = supported;
  if (supported) {
    await click('game-fullscreen');
    await until("document.fullscreenElement?.id === 'game-viewport'", 'real native fullscreen entry');
    assert.equal(await value("document.querySelector('#game-fullscreen').getAttribute('aria-pressed')"), 'true');
    await layout('native-fullscreen'); await screenshot('native-fullscreen');
    const activeBefore = await value('sourcePlayer.observe().state.active');
    await value('document.exitFullscreen()');
    await until('document.fullscreenElement === null', 'real browser fullscreen exit');
    assert.equal(await value("document.querySelector('#game-fullscreen').getAttribute('aria-pressed')"), 'false');
    assert.equal(await value('sourcePlayer.observe().state.active'), activeBefore, 'Native exit must not invent a pause.');
    assert.equal(await value('document.activeElement.id'), 'game');
    await until('!navigator.userActivation.isActive', 'transient activation expires before real denied request', 10000);
    await value("document.querySelector('#game-fullscreen').click()");
    await until("document.querySelector('#game-view-message').textContent.includes('denied')",
      'real user-activation denial is readable');
    assert.equal(await value("document.querySelector('#game-fullscreen').getAttribute('aria-pressed')"), 'false');
    await layout('native-denial-fallback');
    evidence.denial = await value("document.querySelector('#game-view-message').textContent");
  } else {
    await click('game-fullscreen');
    evidence.unsupported = await value("document.querySelector('#game-view-message').textContent");
    assert.match(evidence.unsupported, /unavailable/);
  }
  await click('game-view-toggle');
  await layout('normal-restored', false);
  assert.equal(await value("getComputedStyle(document.querySelector('header')).display !== 'none'"), true);
  assert.equal(await value('document.activeElement.id'), 'game');
  evidence.final = await value('sourcePlayer.observe()');
  evidence.fullscreenEvents = await value('globalThis.gameViewTestEvents ?? []');
  assert.deepEqual(evidence.errors, []);
  await click('game-view-toggle');
  if (supported) {
    await click('game-fullscreen');
    await until("document.fullscreenElement?.id === 'game-viewport'", 'native fullscreen before real context-loss diagnostic');
  }
  assert.equal(await value(`(() => {
    const extension = document.querySelector('#game').getContext('webgl2').getExtension('WEBGL_lose_context');
    if (!extension) return false;
    extension.loseContext(); return true;
  })()`), true);
  await until(`sourcePlayer.observe().failed && !document.fullscreenElement &&
    !document.documentElement.classList.contains('game-only')`, 'real context failure restores diagnostic page');
  evidence.expectedFailure = await value(`({
    text:document.querySelector('#status').textContent,
    visible:getComputedStyle(document.querySelector('#status')).display !== 'none',
    viewDisabled:document.querySelector('#game-view-toggle').disabled
  })`);
  assert.equal(evidence.expectedFailure.visible, true);
  assert.equal(evidence.expectedFailure.viewDisabled, true);
  assert.match(evidence.expectedFailure.text, /Actual WebGL context (?:was )?lost/);
  assert(evidence.errors.every(error => typeof error === 'string' && error.startsWith('SOURCE_PLAYER_FAILED')));
  evidence.accepted = true;
} catch (error) {
  evidence.accepted = false;
  evidence.failure = error.stack;
  throw error;
} finally {
  await writeFile(join(directory, 'evidence.json'), JSON.stringify(evidence, null, 2) + '\n');
  page?.close();
  if (target) await browser.send('Target.closeTarget', { targetId: target }).catch(() => {});
  if (context) await browser.send('Target.disposeBrowserContext', { browserContextId: context }).catch(() => {});
  browser.close();
}
console.log(`Game view browser accepted: ${evidence.layouts.length} layouts, ${evidence.screenshots.length} real screenshots.`);
