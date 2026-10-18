import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { mkdir, writeFile } from 'node:fs/promises';
import { launchChrome, sleep, value } from '../../tests/browser/chrome.mjs';

const args = process.argv.slice(2);
const option = (name, fallback) => args.includes(name) ? args[args.indexOf(name) + 1] : fallback;
const url = option('--url', 'http://127.0.0.1:8083/?source-fire=portable&source-kick-meters=portable-authored');
const directory = option('--output', 'tools/source-player/artifacts/features-attempt1');
const limit = Number(option('--seconds', '240'));
const goal = option('--goal', 'spectacle');
await mkdir(directory, { recursive: true });
const evidence = { schema: 'source-player-fire-kick-browser/v1', url, status: 'running',
  method: 'Fresh owned Chrome/profile; actual public cabinet events only; no Match/actor/ball/RNG/site/pose/state injection.',
  frames: [], input: [], messages: [], errors: [], networkErrors: [], assemblyRequests: [], fullNativeOrMatchQualified: false };
const chrome = await launchChrome(directory), page = chrome.cdp;
evidence.browser = chrome.version; evidence.launch = chrome.launch;
let observedSourceTick = -1;
const keyCodes = { w: ['KeyW', 87], a: ['KeyA', 65], s: ['KeyS', 83], d: ['KeyD', 68],
  v: ['KeyV', 86], b: ['KeyB', 66], n: ['KeyN', 78], Enter: ['Enter', 13], Escape: ['Escape', 27], r: ['KeyR', 82] };
async function snapshot() {
  const observed = await value(page, 'globalThis.sourcePlayer?.snapshot()');
  if (observed?.failed) throw new Error(await value(page, 'document.getElementById("status").textContent'));
  if (observed?.state) observedSourceTick = observed.state.sourceTick;
  return observed;
}
async function key(name, down) {
  const [code, number] = keyCodes[name];
  evidence.input.push({ name, down, at: Date.now(), observedSourceTick });
  await page.send('Input.dispatchKeyEvent', { type: down ? 'keyDown' : 'keyUp', key: name, code,
    windowsVirtualKeyCode: number, nativeVirtualKeyCode: number });
}
async function tap(name, hold = 65) {
  await key(name, true); await sleep(hold); await key(name, false); await sleep(70);
}
async function capture(name) {
  const proof = await value(page, '({snapshot:sourcePlayer.snapshot(),gpu:sourcePlayer.gpuProof()})');
  const image = await page.send('Page.captureScreenshot', { format: 'png' });
  const png = Buffer.from(image.data, 'base64');
  await writeFile(`${directory}/${name}.png`, png);
  const after = await snapshot();
  evidence.frames.push({ name, ...proof, pngBytes: png.length, pngSha256: createHash('sha256').update(png).digest('hex'),
    screenshotSourceTickBracket: [proof.snapshot.state.sourceTick, after.state.sourceTick] });
  return proof;
}
try {
  page.on('Runtime.consoleAPICalled', event => {
    const text = event.args.map(a => a.value ?? a.description ?? '').join(' ');
    evidence.messages.push(text); if (event.type === 'error') evidence.errors.push(text);
  });
  page.on('Runtime.exceptionThrown', event => evidence.errors.push(event.exceptionDetails));
  page.on('Network.loadingFailed', event => evidence.networkErrors.push(event));
  page.on('Network.responseReceived', event => {
    if (/SourcePlayer\..*\.wasm$/.test(new URL(event.response.url).pathname))
      evidence.assemblyRequests.push({ url: event.response.url, status: event.response.status, mime: event.response.mimeType });
  });
  await page.send('Emulation.setDeviceMetricsOverride', { width: 1280, height: 1000, deviceScaleFactor: 1, mobile: false });
  await page.send('Page.navigate', { url }); await page.send('Page.bringToFront');
  let loaded;
  const loadDeadline = Date.now() + 180000;
  while (Date.now() < loadDeadline) {
    await sleep(200);
    loaded = await snapshot();
    if (!loaded && await value(page, 'globalThis.sourcePlayerLoader?.failed'))
      throw new Error(await value(page, 'document.getElementById("status")?.textContent'));
    if (loaded?.ready) break;
  }
  assert(loaded?.ready, 'Actual new C# build did not earn SOURCE_PLAYER_READY.');
  evidence.device = loaded.device;
  evidence.build = loaded.build;
  const receipt = Buffer.from(await (await fetch(new URL('build-info.json', url))).arrayBuffer());
  evidence.receiptSha256 = createHash('sha256').update(receipt).digest('hex');
  evidence.httpAssemblies = [];
  for (const assembly of loaded.build.assemblies) {
    const response = await fetch(new URL(assembly.path, url)), bytes = Buffer.from(await response.arrayBuffer());
    const sha256 = createHash('sha256').update(bytes).digest('hex');
    assert.equal(response.status, 200); assert.equal(bytes.length, assembly.bytes); assert.equal(sha256, assembly.sha256);
    assert.match(response.headers.get('content-type'), /application\/wasm/);
    assert(evidence.assemblyRequests.some(r => r.url.endsWith('/' + assembly.path) && r.status === 200),
      'The actual .NET loader must request the emitted C# assembly, not a relabelled JS fallback.');
    evidence.httpAssemblies.push({ ...assembly, observedSha256: sha256, httpStatus: response.status });
  }
  await capture('ready-original-teams');
  if (args.includes('--startup-only')) {
    evidence.status = 'startup-only-NOT-gameplay-witness'; evidence.final = await snapshot();
  } else {
    const deadline = Date.now() + limit * 1000;
    let lastMenu = -1, ownSnap = -1, passSent = false, lastCapture = 0;
    const witnessed = { fire: false, flash: false, fieldGoalMeter: false, puntMeter: false,
      fieldGoalRelease: false, puntRelease: false, camera: false, shaker: false, return: false };
    evidence.witnessed = witnessed;
    evidence.strategy = args.includes('--defensive-fire') ?
      'Legal public QB hold: allow real negative-yard plays to exercise original defensive second-loss fire.' :
      args.includes('--rush-field-position') ? 'Legal public QB scramble to midfield, then the original fourth-down kick menu.' :
      'Legal public snap/pass/run or fourth-down kick choices.';
    let capturedBeforeContact = false;
    let contactStops = 0;
    while (Date.now() < deadline) {
      const current = await snapshot(), s = current.state;
      if (!capturedBeforeContact && s.phase === 'LiveBall' && s.ball.mode === 'Held' && !s.camera.transient) {
        await capture('source-camera-before-contact'); capturedBeforeContact = true;
      }
      if ((s.contactCounts.ProgressStopped || 0) > contactStops) {
        contactStops = s.contactCounts.ProgressStopped;
        await capture(`actual-carrier-contact-${contactStops}`);
      }
      if (s.effects.draws > 0 && !witnessed.fire) {
        const frame = await capture('actual-source-fire');
        witnessed.fire = frame.gpu.firePixels > 0;
      }
      if (s.actors.some(a => a.flashWhite) && !witnessed.flash) {
        const frame = await capture('actual-raw-white-flash');
        witnessed.flash = frame.gpu.actorFlash.some((alpha, actor) => alpha === 192 / 255 && frame.gpu.actorPixels[actor] > 0);
      }
      if (s.camera.transient && !witnessed.camera) { await capture('eligible-source-camera'); witnessed.camera = true; }
      const stage = s.camera.cameraTrace.findLast(row => row.kind === 'camera-source-stage');
      if (stage?.shakeX != null && stage?.shakeZ != null && !witnessed.shaker) {
        await capture('eligible-source-shaker'); witnessed.shaker = true;
      }
      if (witnessed.camera && !s.camera.transient && !s.camera.resuming && !witnessed.return) {
        await capture('eligible-source-camera-return'); witnessed.return = true;
      }
      const meter = s.kick.meter;
      if (meter?.IsSpecified && meter.ShowMeterFlag) {
        const type = meter.Kind === 1 ? 'fieldGoal' : 'punt';
        if (!witnessed[type + 'Meter']) { await capture(`actual-${type}-meter`); witnessed[type + 'Meter'] = true; }
        if (meter.Status === 2 && (meter.Highlighted || s.sourceTick - meter.CreatedSourceTick > 60)) await tap('b');
      }
      const launch = s.kick.lastLaunch;
      if (launch?.IsSpecified) {
        const type = launch.Kind === 1 ? 'fieldGoal' : 'punt';
        if (!witnessed[type + 'Release']) { await capture(`actual-${type}-release`); witnessed[type + 'Release'] = true; }
      }
      if (s.phase === 'DeadBall') { await tap('Enter'); continue; }
      if (s.phase === 'PlaySelect' && s.sourceTick - lastMenu > 30) {
        lastMenu = s.sourceTick; passSent = false;
        await sleep(450);
        if (s.possession === 'Home' && goal !== 'spectacle') {
          let menu = (await snapshot()).state.menu.home;
          for (let pages = 0; pages < 5 && !menu.slots.some(slot => slot.name.toUpperCase().includes(goal === 'punt' ? 'PUNT' : 'FIELD GOAL')); pages++) {
            await tap('n'); menu = (await snapshot()).state.menu.home;
          }
          const slot = menu.slots.find(slot => slot.name.toUpperCase().includes(goal === 'punt' ? 'PUNT' : 'FIELD GOAL') &&
            !slot.name.toUpperCase().includes('FAKE'));
          if (slot) {
            let current = menu.cursor;
            while (Math.floor(current / 3) < Math.floor(slot.index / 3)) { await tap('s'); current += 3; }
            while (Math.floor(current / 3) > Math.floor(slot.index / 3)) { await tap('w'); current -= 3; }
            while (current % 3 < slot.index % 3) { await tap('d'); current++; }
            while (current % 3 > slot.index % 3) { await tap('a'); current--; }
            await capture(`selected-original-${goal}`);
          } else {
            evidence.input.push({ decision: 'Requested special-team slot not currently offered; retain original down/field-position gate.',
              sourceTick: s.sourceTick, down: s.down, lineOfScrimmage: s.lineOfScrimmage, goal });
            while (menu.page !== 0) { await tap('n'); menu = (await snapshot()).state.menu.home; }
          }
        }
        await tap('b'); continue;
      }
      if (s.phase === 'PreSnap' && s.playKind === 'Normal' && s.possession === 'Home' &&
          !(meter?.IsSpecified && meter.ShowMeterFlag)) {
        await sleep(1000); await tap('b'); ownSnap = s.sourceTick; passSent = false; continue;
      }
      if (s.phase === 'LiveBall' && s.possession === 'Home' && s.playKind === 'Normal') {
        const fieldPosition = s.offenseDirection > 0 ? s.lineOfScrimmage : 100 - s.lineOfScrimmage;
        const waitForFourth = goal === 'punt' || goal === 'fieldGoal' && fieldPosition >= 50 ||
          goal === 'spectacle' && args.includes('--defensive-fire');
        if (waitForFourth) {
          // A real legal choice to hold the QB and accept the defense; never
          // alter down/LOS or bypass original special-team menu eligibility.
          await sleep(150);
        } else if (goal === 'fieldGoal' && args.includes('--rush-field-position')) {
          await key('w', true); await key('n', true); await sleep(150); await key('w', false); await key('n', false);
        } else if (!passSent && s.ball.carrier === 3) {
          await key('w', true); await tap('b', 45); await key('w', false); passSent = true;
        } else { await key('w', true); await key('n', true); await sleep(200); await key('w', false); await key('n', false); }
      } else if (s.phase === 'LiveBall') {
        await key('w', true); await key('n', true); await tap('v'); await sleep(200);
        await key('w', false); await key('n', false);
      } else await sleep(100);
      if (Date.now() - lastCapture > 15000) { await capture(`flow-${evidence.frames.length}`); lastCapture = Date.now(); }
      if (goal === 'punt' && witnessed.puntMeter && witnessed.puntRelease ||
          goal === 'fieldGoal' && witnessed.fieldGoalMeter && witnessed.fieldGoalRelease ||
          goal === 'spectacle' && witnessed.fire && witnessed.flash && witnessed.camera && witnessed.shaker && witnessed.return) break;
    }
    evidence.witnessed = witnessed; evidence.final = await snapshot();
    const accepted = goal === 'punt' ? witnessed.puntMeter && witnessed.puntRelease :
      goal === 'fieldGoal' ? witnessed.fieldGoalMeter && witnessed.fieldGoalRelease :
        witnessed.fire && witnessed.flash && witnessed.camera && witnessed.shaker && witnessed.return;
    evidence.requestedGoal = goal;
    evidence.status = accepted ? 'requested-goal-live-witnesses-passed' : 'bounded-live-attempt-partial-UNMET-explicit';
    if (!accepted) process.exitCode = 2;
  }
  assert.equal(evidence.errors.length, 0);
  assert.equal(evidence.networkErrors.length, 0);
} catch (error) {
  evidence.status = 'failed'; evidence.error = String(error.stack || error);
  try { evidence.final = await value(page, 'globalThis.sourcePlayer?.snapshot()'); await capture('failure'); } catch {}
  process.exitCode = 1;
} finally {
  await writeFile(`${directory}/evidence.json`, JSON.stringify(evidence, null, 2) + '\n');
  await chrome.close();
  console.log(JSON.stringify({ status: evidence.status, witnessed: evidence.witnessed, device: evidence.device,
    sourceTick: evidence.final?.state?.sourceTick, error: evidence.error }, null, 2));
}
