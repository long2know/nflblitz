import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { createHash } from 'node:crypto';
import { createServer } from 'node:http';
import { lstat, mkdir, readFile, realpath, rmdir, writeFile } from 'node:fs/promises';
import { dirname, extname, resolve, sep } from 'node:path';
import { fileURLToPath } from 'node:url';
import { Cdp } from '../../browser/lib.mjs';
import { createChromeRuntime } from '../../browser/runtime.mjs';

const project = resolve(dirname(fileURLToPath(import.meta.url)), '../../..');
const [bundleArg, evidenceArg, mode] = process.argv.slice(2);
assert(bundleArg && evidenceArg && [undefined, '--full-match', '--records-only', '--startup-only'].includes(mode),
  'Use browser.mjs <actual AppBundle|owned loopback base URL> <new owned evidence directory> [--full-match|--records-only|--startup-only].');
const external = /^https?:\/\//.test(bundleArg) ? new URL(bundleArg) : null;
if (external) {
  assert(external.protocol === 'http:' && ['127.0.0.1', 'localhost', '[::1]'].includes(external.hostname) &&
    Number(external.port) > 8097 && !external.username && !external.password && !external.search && !external.hash,
    'External acceptance targets must be explicit uniquely owned loopback HTTP origins above8097.');
  assert(external.pathname.endsWith('/'), 'Use the explicit game base URL ending in slash.');
  assert(/^[a-f0-9]{64}$/.test(process.env.SOURCE_RECORDS_EXPECTED_BUILD_SHA256 || ''),
    'External image acceptance requires the exact frozen build-info SHA256.');
}
const bundle = external ? null : await realpath(resolve(bundleArg)), destination = resolve(evidenceArg);
const owned = resolve(project, '.squad/.scratch/original-record-ui-20261030');
const dockerOwned = resolve(project, '.squad/.scratch/docker-delivery-20261030');
assert(destination.startsWith(owned + sep) ||
  external && dirname(destination) === dockerOwned && /^browser-\d{3}$/.test(destination.slice(dockerOwned.length + 1)),
  'Use the owned record UI root or the deployment owner’s exact numbered browser evidence path.');
assert(external || bundle.startsWith(resolve(project, '.squad/.scratch/original-completion-20261030') + sep) ||
  bundle.startsWith('/home/long2know/nflblitz-e698fa22-direct-webgl-20261027/original-completion-20261030/'),
  'Use an explicitly prepared current original-completion AppBundle, never an old game.');
await mkdir(destination, { recursive: false, mode: 0o700 });
const evidence = { schema: 'actual-original-record-cabinet-browser/v1', status: 'running',
  syntheticOwnedProfileOnly: true, controls: 'trusted CDP keyboard/mouse events only',
  gameClockScorePoseOrSeedMutation: false, privateRecordSnapshotOrPinLogging: false,
  fullMatchRequested: mode === '--full-match', startupDiagnosticOnly:mode === '--startup-only',
  stages: [], screenshots: [], exceptionCount: 0, consoleErrorCount: 0 };
const pause = ms => new Promise(ok => setTimeout(ok, ms));
const hash = data => createHash('sha256').update(data).digest('hex');
evidence.harnessSha256 = hash(await readFile(fileURLToPath(import.meta.url)));
let page, chrome, runtime, browser, server, ownedShortRoot, shortRootIdentity, currentStage = 'startup';
let exited = false, stopped;
const requestOrigins = new Set(), requestKinds = new Map(), failureStatuses = new Set();
const network = { httpRequests:0, localHttpRequests:0, nonlocalHttpRequests:0, httpFailures:0,
  loadingFailures:0, canceledLoads:0, requestOrigins:[], failureStatuses:[], safeStaticFailureBasenames:[] };
evidence.network = network;

function commandDuringColdStart(client, method, params, timeoutMs = 120000) {
  const id = ++client.nextId;
  return new Promise((ok, reject) => {
    const timeout = setTimeout(() => {
      client.pending.delete(id);
      reject(new Error('RECORD_UI_COLD_START_COMMAND_TIMEOUT_' + method));
    }, timeoutMs);
    client.pending.set(id, { resolve: ok, reject, timeout });
    client.socket.send(JSON.stringify({ id, method, params }));
  });
}

async function value(expression) {
  const parameters = { expression, returnByValue: true, awaitPromise: true };
  const result = currentStage === 'actual-cabinet-loaded' ?
    await commandDuringColdStart(page, 'Runtime.evaluate', parameters) :
    await page.send('Runtime.evaluate', parameters);
  if (result.exceptionDetails) throw new Error('RECORD_UI_READ_ONLY_BROWSER_OBSERVATION_FAILED');
  return result.result.value;
}

async function state() {
  return value(`(() => {
    const p = globalThis.sourcePlayer?.observe();
    const s = p?.state;
    if (!s) return { loaded:false, failed:p?.failed === true };
    const a = s.audibles?.core;
    return { loaded:true, failed:p.failed, sourceTick:s.sourceTick, phase:s.phase, matchPhase:s.matchPhase,
      playKind:s.playKind, possession:s.possession, homeScore:s.homeScore, awayScore:s.awayScore, active:s.active,
      menuStage:s.cabinetSession?.stage, records:s.records,
      matchesStarted:s.cabinetSession?.matchesStarted, matchesCompleted:s.cabinetSession?.matchesCompleted,
      preGame:s.preGameAudio,
      flowStage:s.cabinetFlow?.Stage, waitingForRelease:s.cabinetFlow?.WaitingForButtonRelease,
      quarter:s.cabinetFlow?.CompletedQuarterIndex, menuHomePage:s.menu?.home?.page,
      menuHomeSlots:s.menu?.home?.slots, selected:s.selectedPlays,
      audibleChoice:a?.Offense?.Choice, audibleAction:a?.HomeTeam?.Action,
      audibleTimer:a?.HomeTeam?.Timer, audibleCaller:a?.Offense?.CallerStationId,
      graphics:(s.instructions?.pageGraphics || 0)+(s.instructions?.kickoffGraphics || 0)+(s.instructions?.introductionGraphics || 0),
      intro:s.instructions?.showPlaySelectionInstruction,
      elements:s.hud?.elements || [], text:s.hud?.text || [],
      audioState:p.audioReceiver?.contextState, audioUnlocked:p.audioReceiver?.unlocked,
      audioEpoch:p.audioReceiver?.epoch, audioSourceOrigin:p.audioReceiver?.sourceOriginTick,
      audioSignal:p.audioReceiver?.signal, audioVoices:p.audioReceiver?.startedVoices,
      audioClock:p.audioReceiver?.clock,
      audioSampleCount:p.audioReceiver?.sampleCount, audioSampleBytes:p.audioReceiver?.sampleBytes,
      audioBanks:p.audioReceiver?.banks, audioNodes:p.audioReceiver?.nodes,
      realtimeClock:s.realtimeClock,
      inputSamples:s.input?.samples, inputButtons:s.input?.buttons,
      inputWaitingForNeutral:s.input?.waitingForNeutral,
      eventCounts:s.eventCounts,
      actorPositions:(s.actors || []).map(actor => actor.position),
      resolvedScripts:[...new Set((s.actors || []).flatMap(actor => [actor.script,actor.secondary]).filter(Boolean))],
      generation:s.records?.Generation, recordCount:s.records?.RecordCount,
      result:s.cabinetSession?.records?.at(-1) };
  })()`);
}

function safeSample(s) {
  return { stage: currentStage, sourceTick: s.sourceTick, phase: s.phase, matchPhase: s.matchPhase,
    playKind: s.playKind, menuStage: s.menuStage, recordsScreen: s.records?.screen,
    storeStatus: s.records?.storeStatus, generation: s.generation, recordCount: s.recordCount,
    finalCommit: s.records?.finalCommit, flowStage: s.flowStage, homeScore: s.homeScore, awayScore: s.awayScore,
    audioState: s.audioState, audioUnlocked: s.audioUnlocked,
    audibleAction:s.audibleAction, audibleChoice:s.audibleChoice };
}
function clockSample(s) {
  return { sourceTick:s.sourceTick, active:s.active, audioState:s.audioState, realtimeClock:s.realtimeClock,
    audioEpoch:s.audioEpoch, audioSourceOrigin:s.audioSourceOrigin, audioClock:s.audioClock,
    sampleCount:s.audioSampleCount, sampleBytes:s.audioSampleBytes, startedVoices:s.audioVoices };
}

async function until(predicate, label, timeout = 30000) {
  currentStage = label;
  const deadline = Date.now() + timeout;
  let actual;
  while (Date.now() < deadline) {
    actual = await state();
    if (actual.failed) {
      evidence.lastSafeState = safeSample(actual);
      throw new Error('RECORD_UI_ACTUAL_PLAYER_FAILED');
    }
    if (await predicate(actual)) {
      const sample = safeSample(actual);
      evidence.stages.push(sample);
      console.log(JSON.stringify({ progress: sample }));
      return actual;
    }
    await pause(70);
  }
  evidence.lastSafeState = safeSample(actual || {});
  throw new Error('RECORD_UI_STAGE_TIMEOUT_' + label);
}

const keys = { up: 'KeyW', down: 'KeyS', left: 'KeyA', right: 'KeyD', a: 'KeyV', b: 'KeyB', c: 'KeyN' };
const held = new Set();
async function key(code, down) {
  await page.send('Input.dispatchKeyEvent', { type: down ? 'keyDown' : 'keyUp', code,
    key: code.startsWith('Key') ? code.slice(3).toLowerCase() : code,
    windowsVirtualKeyCode: code.startsWith('Key') ? code.charCodeAt(3) : 0,
    nativeVirtualKeyCode: code.startsWith('Key') ? code.charCodeAt(3) : 0 });
}
async function release() {
  for (const code of [...held]) { await key(code, false); held.delete(code); }
}
async function chord(codes) {
  for (const code of [...held]) if (!codes.includes(code)) { await key(code, false); held.delete(code); }
  for (const code of codes) if (!held.has(code)) { await key(code, true); held.add(code); }
}
async function tap(binding) {
  const before = await state();
  await release();
  await key(keys[binding], true); held.add(keys[binding]);
  await pause(70);
  await release(); await pause(165);
  if (before.records?.sourceMenuBound && before.menuStage !== 'Playing') {
    if (before.menuStage === 'Joining' && binding === 'c' ||
        before.records.screen === 'Options' && binding === 'a' ||
        before.records.screen === 'Selection' && before.records.stations?.[0]?.step === 'Choice' && binding === 'a')
      await pause(750);
    else if (before.records.screen === 'Editor' && binding === 'a') await pause(400);
  }
}
async function down(count) { for (let i = 0; i < count; i++) await tap('down'); }
async function doubleTurbo() {
  await release();
  await pause(150);
  for (let i = 0; i < 2; i++) {
    await key(keys.c, true); held.add(keys.c);
    await pause(40);
    await release(); await pause(55);
  }
}

async function keyboard(at, target, columns) {
  while (Math.floor(at / columns) > Math.floor(target / columns)) { await tap('up'); at -= columns; }
  while (Math.floor(at / columns) < Math.floor(target / columns)) { await tap('down'); at += columns; }
  while (at > target) { await tap('left'); at--; }
  while (at < target) { await tap('right'); at++; }
  await tap('a');
  return at;
}

// These are new synthetic inputs in an exclusive new Chrome profile. Neither is
// put in screenshots' filenames, evidence JSON, status dumps or console output.
const syntheticName = 'ZQTEST', syntheticPin = '2749';
async function enterIdentity(create) {
  await tap('c');
  await until(s => s.records?.screen === 'Options', 'original-options');
  await tap('a');
  await until(s => s.records?.screen === 'Selection', 'normal-name-entry');
  await tap('up'); await tap('a');
  let cursor = 0;
  for (const character of syntheticName) cursor = await keyboard(cursor, 'ABCDEFGHIJKLMNOPQRSTUVWXYZ-*! '.indexOf(character), 4);
  cursor = 0;
  for (const character of syntheticPin) cursor = await keyboard(cursor, '123456789 0'.indexOf(character), 3);
  await until(s => s.records?.stations?.[0]?.step === (create ? 'Missing' : 'Selected'), create ? 'explicit-not-found' : 'named-record-loaded');
  if (create) {
    const before = await state();
    assert.equal(before.records.storeStatus, 'NotFound', 'Typing an identity must not initialize a missing cabinet.');
    await tap('a');
    await until(s => s.records?.stations?.[0]?.step === 'Selected' && !s.records.busy, 'explicit-create-committed');
  }
}

async function screenshot(name) {
  await release();
  const captured = await page.send('Page.captureScreenshot', { format: 'png' });
  const bytes = Buffer.from(captured.data, 'base64');
  await writeFile(resolve(destination, name), bytes, { flag: 'wx', mode: 0o600 });
  evidence.screenshots.push({ path: name, bytes: bytes.length, sha256: hash(bytes), actualRenderedCanvas: true });
}

async function editor() {
  await tap('a'); await tap('a'); await tap('a');
  await until(s => s.records?.screen === 'Editor', 'actual-original-editor');
  await down(1); await tap('a'); await tap('a');
  for (let i = 0; i < 6; i++) await tap('up');
  await tap('a'); await down(3); await tap('a');
  for (let i = 0; i < 6; i++) await tap('right');
  await tap('a'); await down(5); await tap('a');
  await down(4); await tap('a'); await down(1); await tap('a');
  await screenshot('actual-source-editor-route.png');
  await down(3); await tap('a');
  let cursor = 0;
  for (const c of 'ZIG') cursor = await keyboard(cursor, c.charCodeAt(0) - 65, 4);
  cursor = await keyboard(cursor, 31, 4);
  for (const c of 'RUN') cursor = await keyboard(cursor, c.charCodeAt(0) - 65, 4);
  await keyboard(cursor, 31, 4);
  await down(10); await tap('a');
  await until(s => s.records?.screen === 'CustomSlots' && !s.records.busy && s.records.storeStatus === 'Saved', 'actual-custom-route-saved');
  await screenshot('actual-source-saved-custom-card.png');
  await tap('c'); await tap('left'); await tap('c');
  await down(6); await tap('a');
  return until(s => s.records?.screen === 'Career' && !s.records.busy && s.records.storeStatus === 'Saved', 'actual-signed-audibles-saved');
}

async function startMatch(fromSelected = false) {
  if (fromSelected) await tap('a');
  await down(4); await tap('a'); await tap('c');
  await until(s => s.menuStage === 'Teams', 'real-team-selection');
  await until(s => !s.preGame || s.preGame.phase === 'Teams', 'real-original-options-return', 15000);
  await tap('a'); await tap('a'); await tap('a');
  return until(s => s.menuStage === 'Playing' && s.sourceTick >= 2 && s.audioState === 'running',
    'genuine-source-game-start', 90000);
}

async function customSelection() {
  currentStage = 'natural-saved-play-selection';
  const deadline = Date.now() + 180000;
  let actual, firstMoving;
  while (Date.now() < deadline) {
    actual = await state();
    if (actual.failed) {
      evidence.lastSafeState = safeSample(actual);
      throw new Error('RECORD_UI_ACTUAL_GAME_FAILED');
    }
    if (actual.matchPhase === 'LiveBall' && !evidence.actualMovingPlay) {
      if (!firstMoving) firstMoving = { sourceTick:actual.sourceTick, actorPositions:actual.actorPositions };
      else {
        if (actual.sourceTick > firstMoving.sourceTick && actual.sourceTick - firstMoving.sourceTick <= 20 &&
            JSON.stringify(actual.actorPositions) !== JSON.stringify(firstMoving.actorPositions))
          evidence.actualMovingPlay = { firstSourceTick:firstMoving.sourceTick, secondSourceTick:actual.sourceTick,
            phase:actual.matchPhase, sourceActorPositionsChanged:true, inputOnly:true };
        firstMoving = { sourceTick:actual.sourceTick, actorPositions:actual.actorPositions };
      }
    }
    if (actual.matchPhase !== 'LiveBall') firstMoving = null;
    if (actual.matchPhase === 'PlaySelection' && actual.possession === 'Home') break;
    if (actual.graphics > 0 || [1, 2].includes(actual.flowStage)) await chord([keys.a, keys.b, keys.c]);
    else await release();
    await pause(90);
  }
  await release();
  assert.equal(actual?.matchPhase, 'PlaySelection', 'Reach the real source selector, not a component selection call.');
  assert.equal(actual?.possession, 'Home');
  assert(evidence.actualMovingPlay, 'The actual kickoff must advance moving source players before saved-play selection.');
  await until(s => (s.intro ?? 0) < 10, 'source-play-introduction-return', 30000);
  const start = (await state()).sourceTick;
  await until(s => s.sourceTick >= start + 40, 'source-menu-initialization', 10000);
  for (let i = 0; i < 4; i++) await tap('c');
  await until(s => s.menuHomePage === 4 && s.elements.some(e => e.startsWith('selection/Home/custom/0/route/0/')),
    'real-saved-page-four-diagram');
  await screenshot('actual-saved-page-four.png');
  await tap('b');
  const formed = await until(s => s.matchPhase === 'PreSnap', 'real-saved-formation', 15000);
  assert(Number.isInteger(formed.audibleAction) && Number.isInteger(formed.audibleChoice),
    'After actual normal play selection, audibles.core must expose the SAME consumed home action and choice.');
  evidence.actualAudiblePreSnap = { sourceTick:formed.sourceTick, action:formed.audibleAction,
    choice:formed.audibleChoice, timer:formed.audibleTimer, callerStationId:formed.audibleCaller };
  await doubleTurbo();
  const opened = await until(s => s.audibleAction === 1, 'real-double-turbo-audibles');
  assert.equal(opened.audibleCaller, 0,
    'The actually opened source audible must belong to the named human station, not an assumed inactive caller.');
  await tap('down');
  await until(s => s.audibleChoice === 1 && s.selected?.OffenseFlipped === true, 'real-signed-custom-audible');
  await screenshot('actual-signed-custom-audible.png');
  for (let i = 0; i < 3; i++) { await tap('down'); await pause(150); }
  await until(s => s.audibleChoice === 0 && s.selected?.OffenseFlipped === false, 'real-original-play-restore');
  const sounding = await until(s => s.audioState === 'running' && s.audioVoices > 0 && s.audioSignal?.rms > 0,
    'actual-game-original-audio-graph');
  evidence.gameplayAudioObservation = { sourceTick:sounding.sourceTick, startedVoices:sounding.audioVoices,
    banks:sounding.audioBanks, nodes:sounding.audioNodes, signal:sounding.audioSignal,
    realOriginalGraph:true, hearingOrNativeDspEquivalenceClaimed:false };
  evidence.publicPauseResumeClock = { before:clockSample(sounding) };
  await key('Escape', true); await key('Escape', false);
  const paused = await until(s => s.active === false && s.audioState === 'suspended', 'public-pause-for-gpu-readback');
  assert.equal(paused.audioClock?.transport?.phase, 'paused', 'Public pause must finish the declared logical media fence.');
  assert.equal(paused.audioClock.logicalPlaybackSample, paused.audioClock.transport.frozenSample,
    'Suspended logical playback must hold its exact source/programme sample.');
  evidence.publicPauseResumeClock.paused = clockSample(paused);
  const materials = await value(`(() => { const p=globalThis.sourcePlayer.gpuProof(); return {
    shadowPixels:p.shadowPixels ?? null, shadowMaterialPixels:p.world?.shadowMaterialPixels ?? null,
    worldShadowPixels:p.world?.shadowPixels ?? null }; })()`);
  evidence.gameplayMaterialObservations = materials;
  await key('Escape', true); await key('Escape', false);
  const resumed = await until(s => s.active === true && s.audioState === 'running', 'public-resume-after-gpu-readback');
  assert.equal(resumed.audioEpoch, paused.audioEpoch, 'Resume must retain the original logical audio epoch.');
  assert.equal(resumed.audioSourceOrigin, paused.audioSourceOrigin, 'Resume must retain the original logical source origin.');
  assert.equal(resumed.audioClock?.transport?.segmentId, paused.audioClock.transport.segmentId + 1,
    'Only the genuine resume boundary may create the next physical render segment.');
  assert.equal(resumed.audioClock.transport.startSample, paused.audioClock.transport.frozenSample,
    'The new physical segment must continue at the exact held logical sample, not replay or skip it.');
  evidence.publicPauseResumeClock.resumed = clockSample(resumed);
}

async function firstNormalPlayAfterResume() {
  await release();
  const resumed = await state();
  const neutral = await until(s => s.sourceTick >= resumed.sourceTick + 65 &&
    s.inputSamples > resumed.inputSamples && s.inputWaitingForNeutral === false &&
    s.inputButtons?.length === 4 && s.inputButtons.every(button => button === 0),
    'actual-post-resume-physical-neutral', 10000);
  assert.equal(neutral.matchPhase, 'PreSnap', 'The public resume must retain the actual selected normal PreSnap.');
  assert.equal(neutral.playKind, 'Normal');
  await tap('a');
  const live = await until(s => s.matchPhase === 'LiveBall' && s.playKind === 'Normal',
    'actual-post-resume-normal-live-play', 20000);
  let previous = live, moved = null, maximumRms = live.audioSignal?.rms || 0;
  const settled = await until(s => {
    maximumRms = Math.max(maximumRms, s.audioSignal?.rms || 0);
    if (!moved && s.matchPhase === 'LiveBall' && previous.matchPhase === 'LiveBall' &&
        s.sourceTick > previous.sourceTick && s.sourceTick - previous.sourceTick <= 20 &&
        JSON.stringify(s.actorPositions) !== JSON.stringify(previous.actorPositions))
      moved = { firstSourceTick:previous.sourceTick, secondSourceTick:s.sourceTick };
    previous = s;
    return s.sourceTick >= live.sourceTick + 240 && s.audioState === 'running' &&
      s.audioVoices > neutral.audioVoices && maximumRms > 0 && moved !== null;
  }, 'actual-post-resume-original-audio-and-moving-play', 30000);
  evidence.firstNormalPlayAfterPublicResume = { physicalButton:'A', sourceTicksObserved:settled.sourceTick - live.sourceTick,
    neutralSourceTick:neutral.sourceTick, neutralPhysicalSamples:neutral.inputSamples, actualMovingPoses:moved,
    live:clockSample(live), settled:clockSample(settled), maximumObservedOriginalGraphRms:maximumRms,
    freshOriginalVoices:settled.audioVoices - neutral.audioVoices, actualEventCounts:settled.eventCounts,
    banks:settled.audioBanks, nodes:settled.audioNodes, sourceClockScorePoseOrRngMutation:false };
  await writeFile(resolve(destination, 'post-resume-first-play.json'), JSON.stringify({
    schema:'source-record-actual-post-resume-play/v1', status:'passed',
    buildReceiptSha256:evidence.buildReceiptSha256, observation:evidence.firstNormalPlayAfterPublicResume,
    fullMatchStillRequired:mode === '--full-match'
  }, null, 2) + '\n', { flag:'wx', mode:0o600 });
  console.log(JSON.stringify({ actualPostResumeFirstPlay:'passed', buildReceiptSha256:evidence.buildReceiptSha256,
    sourceTick:settled.sourceTick, originalGraphRms:maximumRms, movingSourceTicks:moved,
    fullMatchContinuing:mode === '--full-match' }));
}

async function fullMatch() {
  currentStage = 'natural-named-full-match';
  const deadline = Date.now() + 20 * 60 * 1000;
  const flow = new Set();
  const resolvedScripts = new Set(), audioBanks = new Set(), audioPlaylists = new Set();
  let maximumRms = 0, lastHeartbeat = 0;
  let pulse = false, actual;
  while (Date.now() < deadline) {
    actual = await state();
    if (actual.failed) {
      evidence.lastSafeState = safeSample(actual);
      throw new Error('RECORD_UI_NATURAL_FULL_MATCH_FAILED');
    }
    flow.add(actual.flowStage);
    for (const script of actual.resolvedScripts || []) resolvedScripts.add(script);
    for (const bank of actual.audioBanks || []) audioBanks.add(bank);
    for (const voice of actual.audioNodes || []) audioPlaylists.add(voice.bank + ':' + voice.playlistId);
    maximumRms = Math.max(maximumRms, actual.audioSignal?.rms || 0);
    if (Date.now() - lastHeartbeat > 30000) {
      lastHeartbeat = Date.now();
      console.log(JSON.stringify({ regulation: safeSample(actual) }));
    }
    if (actual.records?.finalCommit === 'Committed' && actual.menuStage === 'NextMatch') break;
    if (actual.records?.finalCommit === 'Failed') throw new Error('RECORD_UI_REAL_FINAL_SAVE_FAILED');
    if (actual.flowStage === 7) {
      await release(); await pause(120); await tap('a');
    } else if (actual.graphics > 0 || [1, 2, 6].includes(actual.flowStage)) {
      await chord([keys.a, keys.b, keys.c]);
    } else if (actual.phase === 'PlaySelect') {
      pulse = !pulse; await chord(pulse ? [keys.b] : []);
    } else if (actual.phase === 'PreSnap' && actual.playKind !== 'Kickoff') {
      pulse = !pulse; await chord(pulse ? [keys.a] : []);
    } else await release();
    await pause(120);
  }
  await release();
  assert.equal(actual?.records?.finalCommit, 'Committed', 'Only an actual awaited final commit completes this natural match.');
  assert.equal(actual?.menuStage, 'NextMatch');
  assert(flow.has(2) && flow.has(4), 'The actual match must traverse real EOQ and halftime, not a fabricated four-period result.');
  evidence.naturalFinal = { sourceTick: actual.sourceTick, homeScore: actual.homeScore, awayScore: actual.awayScore,
    storeGeneration: actual.generation, recordCount: actual.recordCount, result: actual.result, flowStages: [...flow],
    inputOnly: true, realWallClockAndRaf: true, finalSaveAcknowledged: true,
    actualEventCounts:actual.eventCounts, resolvedScripts:[...resolvedScripts], audioBanks:[...audioBanks],
    originalPlayingPrograms:[...audioPlaylists], maximumObservedOriginalGraphRms:maximumRms };
  await screenshot('actual-final-career-committed.png');
}

async function clickStart() {
  currentStage = 'public-user-gesture-start';
  let rect;
  const deadline = Date.now() + 120000;
  while (Date.now() < deadline) {
    rect = await value(`(() => { const b=document.querySelector('#start-original-audio'); if(!b || b.hidden || b.disabled)return null;
      const r=b.getBoundingClientRect();return {x:r.x+r.width/2,y:r.y+r.height/2}; })()`);
    if (rect) break;
    await pause(120);
  }
  assert(rect, 'The actual original-game trusted gesture button must become available.');
  await commandDuringColdStart(page, 'Input.dispatchMouseEvent', { type: 'mousePressed', button: 'left', clickCount: 1, ...rect });
  await commandDuringColdStart(page, 'Input.dispatchMouseEvent', { type: 'mouseReleased', button: 'left', clickCount: 1, ...rect });
  await until(s => s.menuStage === 'Joining' && s.records && s.audioUnlocked === true &&
    ['running', 'suspended'].includes(s.audioState), 'actual-cabinet-loaded', 120000);
}

async function reload() {
  await release();
  await page.send('Page.reload', { ignoreCache: true });
  await clickStart();
}

async function externalRecreationGate(prefix = '') {
  if (process.env.SOURCE_RECORDS_RECREATE_GATE !== '1') return;
  assert(external, 'Container recreation coordination applies only to the explicitly supplied external origin.');
  await release(); await key('Escape', true); await key('Escape', false);
  const actual = await until(s => s.active === false && s.audioState === 'suspended', 'public-pause-before-container-recreation');
  await writeFile(resolve(destination, prefix + 'recreate-ready.json'), JSON.stringify({
    schema:'source-record-container-recreate-ready/v1', origin:external.origin,
    buildReceiptSha256:evidence.buildReceiptSha256, generation:actual.generation,
    recordCount:actual.recordCount, pausedByPublicEscape:true, browserProfileRetained:true
  }, null, 2) + '\n', { flag:'wx', mode:0o600 });
  const resume = resolve(destination, prefix + 'recreate-resume.json'), deadline = Date.now() + 180000;
  let acknowledged = false;
  while (Date.now() < deadline) {
    try {
      const marker = JSON.parse(await readFile(resume, 'utf8'));
      assert.equal(marker.schema, 'source-record-container-recreate-resume/v1');
      assert.equal(marker.buildReceiptSha256, evidence.buildReceiptSha256);
      acknowledged = true; break;
    } catch (error) {
      if (error.code !== 'ENOENT') throw error;
    }
    await pause(250);
  }
  assert(acknowledged, 'The deployment owner must recreate only its own container at the SAME origin and acknowledge.');
  const response = await fetch(new URL('build-info.json', external), { signal:AbortSignal.timeout(10000) });
  assert(response.ok);
  assert.equal(hash(Buffer.from(await response.arrayBuffer())), evidence.buildReceiptSha256,
    'The recreated container must expose the SAME immutable game, not another candidate.');
  const observation = { sameOrigin:external.origin, sameBuildReceipt:true, generation:actual.generation,
    exclusiveBrowserAndIndexedDbRetained:true, noPrivateRecordMutation:true };
  if (prefix) evidence.finalContainerRecreation = observation;
  else evidence.containerRecreation = observation;
}

try {
  assert.notEqual(process.getuid?.(), 0, 'Use the existing unprivileged user and normal Chrome sandbox.');
  ownedShortRoot = process.env.CHROME_RUNTIME_ROOT || '/home/long2know/.bru-4e97629f';
  assert(['/home/long2know/.bru-4e97629f', '/home/long2know/.bdk-4e97629f'].includes(ownedShortRoot),
    'Use only the explicitly assigned absent named-UI or Docker acceptance Chrome root.');
  await mkdir(ownedShortRoot, { mode: 0o700 });
  shortRootIdentity = await lstat(ownedShortRoot);
  assert(shortRootIdentity.uid === process.getuid() && (shortRootIdentity.mode & 0o777) === 0o700 &&
    !shortRootIdentity.isSymbolicLink() && await realpath(ownedShortRoot) === ownedShortRoot);
  runtime = await createChromeRuntime({ root: ownedShortRoot });
  let buildBytes;
  if (external) {
    const response = await fetch(new URL('build-info.json', external), { signal:AbortSignal.timeout(10000) });
    assert(response.ok, 'The owned container must be responsive before Chrome starts.');
    buildBytes = Buffer.from(await response.arrayBuffer());
  } else buildBytes = await readFile(resolve(bundle, 'build-info.json'));
  evidence.build = JSON.parse(buildBytes);
  evidence.buildReceiptSha256 = hash(buildBytes);
  if (process.env.SOURCE_RECORDS_EXPECTED_BUILD_SHA256)
    assert.equal(evidence.buildReceiptSha256, process.env.SOURCE_RECORDS_EXPECTED_BUILD_SHA256);
  const mime = { '.html': 'text/html', '.js': 'text/javascript', '.mjs': 'text/javascript',
    '.wasm': 'application/wasm', '.json': 'application/json', '.css': 'text/css' };
  let url;
  if (!external) {
    server = createServer(async (request, response) => {
      try {
        const path = new URL(request.url, 'http://127.0.0.1').pathname;
        if (path === '/health') { response.end('owned-record-ui'); return; }
        if (path === '/favicon.ico') { response.writeHead(204); response.end(); return; }
        const file = resolve(bundle, path === '/' ? 'index.html' : decodeURIComponent(path.slice(1)));
        assert(file.startsWith(bundle + sep));
        response.setHeader('Cross-Origin-Opener-Policy', 'same-origin');
        response.setHeader('Cross-Origin-Embedder-Policy', 'require-corp');
        response.setHeader('Cache-Control', 'no-store');
        response.setHeader('Content-Type', mime[extname(file)] || 'application/octet-stream');
        response.end(await readFile(file));
      } catch { response.writeHead(404); response.end('OWNED_RECORD_UI_RESOURCE_MISSING'); }
    });
    await new Promise((ok, reject) => { server.once('error', reject); server.listen(0, '127.0.0.1', ok); });
    const port = server.address().port;
    assert(port > 8097, 'Keep existing listeners untouched.');
    evidence.port = port;
    url = `http://127.0.0.1:${port}/`;
    assert.equal(await (await fetch(url + 'health')).text(), 'owned-record-ui');
  } else {
    url = external.href;
    evidence.externalOrigin = external.origin;
    evidence.port = Number(external.port);
  }
  const args = ['--headless=new', '--remote-debugging-address=127.0.0.1', '--remote-debugging-port=0',
    `--user-data-dir=${runtime.profile}`, `--disk-cache-dir=${runtime.cache}`, '--no-first-run',
    '--no-default-browser-check', '--disable-background-networking', '--disable-component-update',
    '--disable-sync', '--metrics-recording-only', '--no-pings', '--window-size=1280,900', 'about:blank'];
  chrome = spawn('/opt/google/chrome/chrome', args, { stdio: ['ignore', 'ignore', 'pipe'], env: { ...process.env,
    HOME: runtime.directory, TMPDIR: runtime.tmp, XDG_RUNTIME_DIR: runtime.tmp, XDG_CACHE_HOME: runtime.cache,
    XDG_CONFIG_HOME: runtime.config, XDG_DATA_HOME: runtime.config } });
  stopped = new Promise(ok => chrome.once('close', () => { exited = true; ok(); }));
  let stderr = '';
  const endpoint = await new Promise((ok, reject) => {
    const timer = setTimeout(() => reject(new Error('RECORD_UI_OWNED_CHROME_START_TIMEOUT')), 30000);
    chrome.once('error', () => { clearTimeout(timer); reject(new Error('RECORD_UI_OWNED_CHROME_START_FAILED')); });
    chrome.stderr.on('data', data => {
      stderr = (stderr + String(data)).slice(-6000);
      const match = stderr.match(/DevTools listening on (ws:\/\/127\.0\.0\.1:\d+\/devtools\/browser\/[^\s]+)/);
      if (match) { clearTimeout(timer); ok(match[1]); }
    });
  });
  browser = await Cdp.connect(endpoint);
  evidence.browser = { pid: chrome.pid, version: await browser.send('Browser.getVersion'), args, normalSandbox: true, defaultAutoplay: true };
  const { targetId } = await browser.send('Target.createTarget', { url: 'about:blank' });
  const targets = await (await fetch(`http://127.0.0.1:${new URL(endpoint).port}/json/list`)).json();
  page = await Cdp.connect(targets.find(target => target.id === targetId).webSocketDebuggerUrl);
  await Promise.all([page.send('Page.enable'), page.send('Runtime.enable'), page.send('Network.enable')]);
  page.on('Network.requestWillBeSent', entry => {
    const request = new URL(entry.request.url);
    if (!['http:', 'https:'].includes(request.protocol)) return;
    requestKinds.set(entry.requestId, true);
    requestOrigins.add(request.origin);
    network.requestOrigins = [...requestOrigins];
    network.httpRequests++;
    if (request.origin === new URL(url).origin) network.localHttpRequests++;
    else network.nonlocalHttpRequests++;
  });
  page.on('Network.responseReceived', entry => {
    if (requestKinds.has(entry.requestId) && entry.response.status >= 400) {
      network.httpFailures++;
      failureStatuses.add(entry.response.status);
      network.failureStatuses = [...failureStatuses];
      const path = new URL(entry.response.url).pathname;
      const basename = path.split('/').at(-1);
      if (/^[A-Za-z0-9_.-]+$/.test(basename) && !basename.includes(syntheticName) && !basename.includes(syntheticPin))
        network.safeStaticFailureBasenames.push({ basename, status:entry.response.status,
          family:path.startsWith('/_framework/') ? 'framework' : path.includes('/data/') ? 'data' : 'other-static' });
    }
  });
  page.on('Network.loadingFailed', entry => {
    if (!requestKinds.has(entry.requestId)) return;
    if (entry.canceled) network.canceledLoads++;
    else network.loadingFailures++;
    requestKinds.delete(entry.requestId);
  });
  page.on('Network.loadingFinished', entry => requestKinds.delete(entry.requestId));
  page.on('Runtime.exceptionThrown', () => { evidence.exceptionCount++; });
  page.on('Runtime.consoleAPICalled', entry => {
    if (entry.type !== 'error') return;
    evidence.consoleErrorCount++;
    if (!evidence.firstRedactedConsoleError)
      evidence.firstRedactedConsoleError = entry.args.map(argument => argument.value ?? argument.description ?? argument.type)
        .join(' ').split(syntheticName).join('<owned-name-redacted>').split(syntheticPin).join('<owned-pin-redacted>').slice(0, 10000);
  });
  await page.send('Page.bringToFront');
  await page.send('Page.navigate', { url });
  await clickStart();
  if (mode === '--startup-only') {
    await pause(1500);
    evidence.startupObserved = safeSample(await state());
  } else {
  await enterIdentity(true);
  const saved = await editor();
  evidence.createdAndSaved = { generation: saved.generation, recordCount: saved.recordCount };
  await externalRecreationGate();
  await reload();
  const loaded = await state();
  assert.equal(loaded.generation, saved.generation, 'Reload must not reinitialize or rewrite the real named custom record.');
  await enterIdentity(false);
  await tap('a'); await tap('a');
  await until(s => s.records?.screen === 'CustomSlots' && s.elements.some(e => e.startsWith('records/slot/0/route/0/')),
    'reloaded-actual-custom-route-card');
  await tap('c');
  await until(s => s.records?.screen === 'Audibles' && s.text.some(t => String(t.text).includes('-37')),
    'reloaded-actual-signed-audibles');
  await screenshot('actual-reloaded-audibles.png');
  if (mode !== '--records-only') {
    await tap('b');
    await startMatch();
    await customSelection();
    await firstNormalPlayAfterResume();
  }
  if (mode === '--full-match') {
    await fullMatch();
    const completed = await state(), finalGeneration = completed.generation;
    await tap('b'); await tap('a');
    const rematched = await until(s => s.menuStage === 'Playing' && s.sourceTick >= 2 &&
      s.sourceTick < completed.sourceTick && s.audioState === 'running', 'actual-retained-record-rematch', 90000);
    assert.equal(rematched.generation, finalGeneration, 'A deliberate rematch must not repeat the committed final transaction.');
    assert.equal(rematched.matchesCompleted, completed.matchesCompleted, 'Rematch must retain the genuine completed-match cursor.');
    assert.equal(rematched.matchesStarted, completed.matchesStarted + 1, 'Rematch must start exactly one fresh authoritative Game.');
    evidence.actualRematch = { sourceTick:rematched.sourceTick, generation:rematched.generation,
      matchesCompleted:rematched.matchesCompleted, matchesStarted:rematched.matchesStarted,
      sameActualBrowserAndStore:true, publicButtonsOnly:true };
    await externalRecreationGate('final-');
    await reload();
    assert.equal((await state()).generation, finalGeneration, 'Reload must recover the actual committed final generation.');
    await enterIdentity(false);
    await tap('a');
    await until(s => s.records?.screen === 'Career' && s.text.some(t => /GAMES 1  WINS/.test(String(t.text))),
      'actual-committed-career-reloaded');
    await screenshot('actual-reloaded-final-career.png');
  }
  }
  assert.equal(evidence.exceptionCount, 0, 'Actual browser must have no unexpected exceptions.');
  assert.equal(evidence.consoleErrorCount, 0, 'Actual browser must have no unexpected console errors.');
  assert.equal(network.nonlocalHttpRequests, 0, 'Actual game resources must not request another HTTP(S) origin.');
  assert.equal(network.httpFailures, 0, 'Actual game resources must not have failed HTTP status codes.');
  assert.equal(network.loadingFailures, 0, 'Actual game resources must not have uncanceled network loading failures.');
  evidence.status = 'passed';
} catch (error) {
  evidence.status = 'failed';
  evidence.failure = { stage: currentStage, name: error.name, message: error.message };
  if (page) {
    try {
      evidence.failureObservation = await value(`(() => {
        const p=globalThis.sourcePlayer?.observe(),s=p?.state,a=p?.audioReceiver;
        const details=p?.failed ? globalThis.sourcePlayer.snapshot() : null;
        return { status:document.querySelector('#status')?.textContent,
          sourceTick:s?.sourceTick, sourceAudio:s?.audio?.decoded,
          realtimeClock:s?.realtimeClock,
          recentProcessSounds:s?.audio?.processDeliveries?.slice(-2), failedFrameTimings:details?.timings,
          receiver:a ? { epoch:a.epoch, sequence:a.sequence, banks:a.banks, contextState:a.contextState,
            clock:a.clock, faultingCommand:a.faultingCommand,
            failure:a.failure, sampleCount:a.sampleCount, sampleBytes:a.sampleBytes,
            startedVoices:a.startedVoices, pendingTransitions:a.pendingTransitions, signal:a.signal } : null };
      })()`);
      assert(materials.shadowPixels > 0 && materials.shadowMaterialPixels > 0,
        'The actual current gameplay geometry must render original shadows, including the material-vs-no-shadow differential.');
    } catch { }
  }
  process.exitCode = 1;
} finally {
  try { if (page) await release(); } catch { }
  page?.close(); browser?.close();
  if (chrome && !exited) chrome.kill('SIGTERM');
  if (stopped) {
    let timer;
    await Promise.race([stopped, new Promise(ok => { timer = setTimeout(ok, 5000); })]); clearTimeout(timer);
    if (!exited) { chrome.kill('SIGKILL'); await stopped; }
  }
  if (runtime) await runtime.cleanup();
  if (shortRootIdentity) {
    const actual = await lstat(ownedShortRoot);
    assert(actual.ino === shortRootIdentity.ino && actual.dev === shortRootIdentity.dev && actual.uid === shortRootIdentity.uid);
    await rmdir(ownedShortRoot);
  }
  if (server) { server.closeAllConnections(); await new Promise(ok => server.close(ok)); }
  evidence.cleanup = { chromeExited: exited, ownedChromeRootRemoved: !!shortRootIdentity, serverStopped: !!server,
    protectedPortsUntouched: [8080, 8096, 8097] };
  await writeFile(resolve(destination, 'result.json'), JSON.stringify(evidence, null, 2) + '\n', { flag: 'wx', mode: 0o600 });
  console.log(JSON.stringify({ status: evidence.status, stage: currentStage, evidence: resolve(destination, 'result.json'),
    screenshots: evidence.screenshots.length, exceptionCount: evidence.exceptionCount, consoleErrorCount: evidence.consoleErrorCount }));
}
