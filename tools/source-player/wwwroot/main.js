import { createVerifiedAssetLoader, createVerifiedRuntimeResourceLoader } from './verified-assets.js';

const canvas = document.querySelector('#game'), status = document.querySelector('#status');
const details = document.querySelector('#runtime-details'), start = performance.now();
const loader = globalThis.sourcePlayerLoader, { fail, progress } = loader;
let failed = false, ready = false, api, renderer, input, frameHandle, previousTime;
let diagnostic = false, build, loadMilliseconds;
let audio, audioDisposalInstalled = false;
let observedHostClock;
let copySourceObservation;
let callerTrace, callerDrain, lastDimensions, CallerTrace, DebtDrain;
const callerProfile = 'aot-rooted-bounded-caller-drain/v1';
const av306CallerProfile = 'aot-rooted-bounded-caller-drain-av306/v1';
const av306Profile = 'original-teams-complete-av306';
const av306ManifestSha256 = '51e94caffcf5875e024206eb069dfd905ee13e134b09e7f5020ff68349461e5d';
const twoStationRecordsProfile = 'original-teams-complete-av306-two-station-records-v1';
const twoStationRecordsManifestSha256 = '745129381c326e9f237ac4e6c3e020503553aada3e1f993dfb9321c09ee8f32d';
const twoStationRecordsFeature = 'source-complete-original-two-station-records-audiovisual/v1';
const twoStationRecordsFactory = 'SourceC2Composition.CreateCompleteAv306TwoStationRecordsSimulation';
const twoStationRecordsSettings = 'settings-av306-two-station-records-v1.json';
const twoStationRecordsCallerProfile = 'aot-rooted-bounded-caller-drain-av306-two-station-records/v1';
let selectedCallerProfile = callerProfile;
const timings = [], sourceSamples = [];
const files = new Map(), requests = [];

const digest = async bytes => Array.from(new Uint8Array(await crypto.subtle.digest('SHA-256', bytes)))
  .map(byte => byte.toString(16).padStart(2, '0')).join('');
const verifiedAssets = createVerifiedAssetLoader({
  baseUrl: import.meta.url, digest, fetchBytes: path => fetchBytes('./' + path),
});

loader.onFailure(() => {
  failed = true; ready = false;
  callerDrain?.dispose();
  cancelAnimationFrame(frameHandle);
  try { api?.SetActive(false); } catch { /* Keep the first explicit kernel/asset failure. */ }
  input?.close();
  closeAudio();
});

function closeAudio() {
  if (audio) void audio.close().catch(error => {
    console.error('SOURCE_AUDIO_DISPOSE_FAILED', error);
    fail(error);
  });
}

Object.defineProperty(globalThis, 'sourcePlayerAudio', { value: Object.freeze({
  async unlock(event) {
    const { SourceAudioReceiver } = await requiredModule('./audio.js');
    if (failed) throw new Error('The source player has already failed; no audio composition may start.');
    audio ??= new SourceAudioReceiver({ onFailure: fail });
    await audio.unlock(event);
    if (!audioDisposalInstalled) {
      window.addEventListener('pagehide', () => {
        callerDrain?.dispose();
        try { api?.Shutdown?.(); } catch (error) { fail(error); }
        closeAudio();
      }, { once: true });
      audioDisposalInstalled = true;
    }
    return audio.snapshot();
  },
  snapshot: () => audio?.snapshot() ?? { unlocked: false, contextState: 'not-created', originalBankAdmission: false },
}), writable: false, configurable: false });

canvas.addEventListener('webglcontextlost', event => { event.preventDefault(); fail(new Error('Actual WebGL context lost')); });

async function requiredModule(relative) {
  try { return await import(relative); } catch (error) {
    throw new Error(`Required source player module could not be loaded: ${relative}: ${error?.message || error}`, { cause: error });
  }
}

async function fetchBytes(relative) {
  try {
    const response = await fetch(new URL(relative, import.meta.url), { cache: 'no-cache' });
    if (!response.ok) throw new Error(`HTTP ${response.status}`);
    const bytes = new Uint8Array(await response.arrayBuffer());
    requests.push({ path: relative, bytes: bytes.length, mime: response.headers.get('content-type') });
    return bytes;
  } catch (error) {
    throw new Error(`Required source player resource could not be loaded: ${relative}: ${error?.message || error}`, { cause: error });
  }
}

async function loadData() {
  build = JSON.parse(new TextDecoder().decode(await fetchBytes('./build-info.json')));
  if (failed) return;
  if (build.schema !== 'blitz-source-player-build/v1' || build.runtimeFlavor !== 'portable C# browser preview (not a Unity player build)')
    throw new Error('The explicit source-player build receipt is missing or from another runtime.');
  const manifestBytes = await fetchBytes('./data/manifest.json');
  if (failed) return;
  const manifestHash = await digest(manifestBytes);
  if (failed) return;
  if (manifestHash !== build.dataManifestSha256) throw new Error('Served data manifest does not match this actual build receipt.');
  const manifest = JSON.parse(new TextDecoder().decode(manifestBytes));
  if (build.execution?.callerProfile !== undefined) {
    const drain = build.execution.callerDrain;
    const historicalCaller = build.execution.callerProfile === callerProfile &&
      manifest.profile === 'original-teams-complete-original303-selected-world-v2' &&
      manifestHash === 'aeb2efdebdbe260f63f6d785c461048dc9cec337098490761c2815cd8ffc4edd';
    const av306Caller = build.execution.callerProfile === av306CallerProfile &&
      manifest.profile === av306Profile && manifestHash === av306ManifestSha256;
    const twoStationRecordsCaller = build.execution.callerProfile === twoStationRecordsCallerProfile &&
      manifest.profile === twoStationRecordsProfile && manifestHash === twoStationRecordsManifestSha256;
    if (!(historicalCaller || av306Caller || twoStationRecordsCaller) || build.execution.mode !== 'aot' ||
        build.execution.runAOTCompilation !== true || build.execution.jsonReflectionEnabled !== true ||
        drain?.schema !== 'bounded-source-caller-drain/v1' || drain.scheduling !== 'MessageChannel' ||
        drain.elapsedSecondsPerDrain !== 0 || drain.maximumHostStepsPerCallback !== 6 ||
        drain.maximumBridgeIntervalSeconds !== .1 || drain.freshInputPollingPerDrain !== 0 ||
        drain.audioLeadChanged !== false || drain.sourceClockRngOrEpochChanged !== false)
      throw new Error('Unknown or mismatched explicit source caller-drain execution profile.');
    selectedCallerProfile = build.execution.callerProfile;
    callerTrace = new CallerTrace();
  }
  const selectedWorld = manifest.profile === 'original-teams-complete-original303-selected-world-v2';
  const av306 = manifest.profile === av306Profile;
  const twoStationRecords = manifest.profile === twoStationRecordsProfile;
  const av306Assets = av306 || twoStationRecords;
  if (manifest.schema !== (av306Assets ? 'blitz-portable-source-player/v3' :
      selectedWorld ? 'blitz-portable-source-player/v2' : 'blitz-portable-source-player/v1') ||
      twoStationRecords && (manifestHash !== twoStationRecordsManifestSha256 ||
        manifest.featureContract !== twoStationRecordsFeature || manifest.factory !== twoStationRecordsFactory ||
        build.profile !== twoStationRecordsProfile || build.factory !== twoStationRecordsFactory ||
        build.featureContract !== twoStationRecordsFeature ||
        manifest.ancestorManifestSha256 !== av306ManifestSha256 ||
        manifest.files.length !== 1154 || manifest.files.reduce((sum, file) => sum + file.bytes, 0) !== 300324845 ||
        manifest.features?.originalStationLayout !== 2 ||
        manifest.features?.sourceTwoStationRecordWorkflow !== 'source-two-station-record-workflow/v1' ||
        manifest.entrypoints?.settings !== twoStationRecordsSettings ||
        !manifest.files.some(file => file.path === twoStationRecordsSettings && file.bytes === 556 &&
          file.sha256 === '6ad8eb0239d2b0432d55dcf47e2fd893a38397e036befa682ffb9b0ed89c4fc2')) ||
      av306 && (manifestHash !== av306ManifestSha256 ||
        manifest.featureContract !== 'source-complete-original-animation-course-audiovisual/v3' ||
        manifest.factory !== 'SourceC2Composition.CreateCompleteAv306Simulation' ||
        manifest.ancestorManifestSha256 !== 'aeb2efdebdbe260f63f6d785c461048dc9cec337098490761c2815cd8ffc4edd' ||
        manifest.files.length !== 1153 || manifest.files.reduce((sum, file) => sum + file.bytes, 0) !== 300324289 ||
        manifest.features?.originalSelectedClipInputs !== 306 ||
        manifest.features?.originalCompiledPlaylistPrograms !== 1205 ||
        manifest.entrypoints?.courseCamera !== 'camera/complete-av306-course.json' ||
        manifest.entrypoints?.originalAudio !== 'audio/complete-av306-manifest.json') ||
      av306Assets && (manifest.features?.originalSelectedClipInputs !== 306 ||
        manifest.features?.originalCompiledPlaylistPrograms !== 1205 ||
        manifest.entrypoints?.courseCamera !== 'camera/complete-av306-course.json' ||
        manifest.entrypoints?.originalAudio !== 'audio/complete-av306-manifest.json') ||
      (selectedWorld || av306Assets) && manifest.selectedWorld?.schema !== 'source-complete-selected-world-admission/v2' ||
      selectedWorld && (manifest.featureContract !== 'source-complete-original-prematch-selected-world/v2' ||
        manifest.selectedWorld?.schema !== 'source-complete-selected-world-admission/v2'))
    throw new Error('Unknown or unauthenticated original-data generation.');
  const seen = new Set();
  for (const file of manifest.files) {
    if (!/^[A-Za-z0-9_.\-/]+$/.test(file.path) || file.path.split('/').some(p => !p || p === '.' || p === '..') ||
        seen.has(file.path.toLowerCase()) || file.bytes <= 0 || !/^[0-9a-f]{64}$/.test(file.sha256))
      throw new Error(`Unsafe/duplicate/unpinned source file ${file.path}.`);
    seen.add(file.path.toLowerCase());
  }
  let next = 0, done = 0;
  await Promise.all(Array.from({ length: 6 }, async () => {
    while (!failed && next < manifest.files.length) {
      const file = manifest.files[next++];
      const bytes = await verifiedAssets.load({ ...file, path: 'data/' + file.path });
      if (failed) return;
      files.set(file.path, bytes);
      const cache = verifiedAssets.snapshot();
      progress(`Verified original source/assets: ${++done}/${manifest.files.length} ` +
        `(${cache.cached} cached, ${cache.downloaded} downloaded)`);
    }
  }));
  if (failed) return;
  const settings = JSON.parse(new TextDecoder().decode(files.get(manifest.entrypoints.settings)));
  if (twoStationRecords && (settings.profile !== 1 || settings.station_layout !== 2 ||
      settings.stations.length !== 2 || settings.stations.some((station, side) =>
        station.id !== side || station.team !== side || station.initial_player_id !== -1 || station.profile !== null)))
    throw new Error('Named TwoStation requires actual mapped two-station settings, not a FourStation data alias.');
  return { manifestBytes, profile: manifest.profile, stations: settings.stations.map(station => station.id) };
}

async function startOriginalAudio(profile) {
  const { SourceAudioReceiver } = await requiredModule('./audio.js');
  audio ??= new SourceAudioReceiver({ onFailure: fail });
  const button = document.querySelector('#start-original-audio');
  if (!button) throw new Error('The explicit complete-original user-gesture start control is missing.');
  button.hidden = false;
  document.querySelector('#audio-qualification').textContent =
    profile === twoStationRecordsProfile ?
      'Named TwoStation: original P1/Home and P2/Away records/custom plays, AV306 and source playlist PCM. Click Start to unlock audio.' :
    profile === av306Profile ?
      'Complete AV306: original stances/course camera and qualified source playlist PCM. Click Start to unlock audio; native DSP/pixels remain unqualified.' :
      'Complete-original303: original shadow/cabinet art and executed source playlist PCM. Click Start to unlock audio; native DSP/pixels remain unqualified.';
  progress('Original resources verified. Click START ORIGINAL GAME WITH SOUND to unlock the real AudioContext.');
  await new Promise((resolve, reject) => {
    let starting = false;
    const launch = event => {
      if (starting || failed) return;
      starting = true; button.disabled = true;
      audio.unlock(event).then(resolve, reject);
    };
    button.addEventListener('click', launch, { once: true });
  });
  button.hidden = true;
  window.addEventListener('pagehide', () => {
    callerDrain?.dispose();
    try { api?.Shutdown?.(); } catch (error) { fail(error); }
    closeAudio();
  }, { once: true });
  audioDisposalInstalled = true;
}

function frame(time) {
  if (failed) return;
  try {
    if (audio?.unlocked && !audio.frameReady) {
      callerDrain?.cancel('audio-not-ready');
      observedHostClock?.sample(performance.now(), false);
      previousTime = undefined;
      frameHandle = requestAnimationFrame(frame);
      return;
    }
    const dimensions = renderer.resize(), before = performance.now();
    lastDimensions = dimensions;
    input.poll();
    if (failed) return;
    const observedTime = observeHostTime();
    const elapsed = previousTime === undefined ? 0 : observedTime - previousTime;
    previousTime = observedTime;
    const advance = () => api.FrameObserved ?
      api.FrameObserved(elapsed, observedTime, dimensions.width, dimensions.height, dimensions.cssWidth, dimensions.cssHeight, dimensions.dpr) :
      api.Frame(elapsed, dimensions.width, dimensions.height, dimensions.cssWidth, dimensions.cssHeight, dimensions.dpr);
    const steps = callerTrace ? traceAdvance('raf-frame', elapsed, observedTime, advance) : advance();
    if (failed) return;
    callerDrain?.request();
    timings.push({ totalMilliseconds: performance.now() - before, webglMilliseconds: renderer.lastMilliseconds, hostSteps: steps });
    if (timings.length > 300) timings.shift();
    const state = renderer.status;
    const recordTransfers = document.querySelector('#record-transfers');
    if (recordTransfers) {
      recordTransfers.hidden = state.records == null;
      document.querySelector('#records-export').disabled = !api.CanExportRecords();
      document.querySelector('#records-import').disabled = !api.CanImportRecords();
    }
    const featureStatus = document.querySelector('#feature-status');
    const audioStatus = audio?.unlocked ? `Original decoded PCM (${audio.started} voices)` : 'Audio output unqualified';
    if (featureStatus) featureStatus.textContent = `${audioStatus} · Fire ${state.effects.enabled ? 'portable original SDL/17-TGA' : 'disabled'} · ` +
      `Kick meters ${state.kick.mode} (authored151/27/209, NOT native Composit) · ` +
      `Phead ${state.assets.poppedHead ? 'original SDL-derived, NOT native head.h' : 'input absent; required raw flag stops'} · ` +
      'Free ball original medball2/HEDSHU + Core source rotation · ' +
      'Native pixels/Unity/DSP remain unqualified.';
    sourceSamples.push({ time: performance.now(), tick: state.sourceTick });
    if (sourceSamples.length > 300) sourceSamples.shift();
    if (!ready && state.sourceTick >= 2 && state.actorCount === 14 && renderer.frameCount >= 2) {
      if (state.kernel.portableBound && state.kernel.policy !== 'StrictSource') {
        const proof = renderer.proof();
        if (failed) return;
        if (proof.visibleHome > 0 && proof.visibleAway > 0) {
          ready = true;
          console.info('SOURCE_PLAYER_READY', JSON.stringify({
            runtimeFlavor: state.runtimeFlavor, assembly: build.mainAssembly, manifestSha256: state.manifestSha256,
            sourceTick: state.sourceTick, policy: state.kernel.policy, actors: state.actorCount,
            actualWebGL: renderer.device, visibleHome: proof.visibleHome, visibleAway: proof.visibleAway,
          }));
        }
      } else if (!diagnostic) {
        diagnostic = true;
        console.info('SOURCE_PLAYER_STRICT_DIAGNOSTIC', 'Actual kernel/render is running, but frozen portable factory integration is not bound. No player-ready qualification.');
      }
    }
    status.className = ready ? 'ready' : 'diagnostic';
    const banner = ready ? 'PLAYABLE PORTABLE PREVIEW' : state.kernel.portableBound ?
      'WARMING ACTUAL PORTABLE KERNEL' : 'STRICT-SOURCE DIAGNOSTIC — NOT PLAYER-READY';
    status.textContent = `${banner} · ` +
      `${state.phase} / ${state.playKind} · source tick ${state.sourceTick} · ${state.kernel.policy} · ` +
      `${state.active ? 'running' : 'paused — release controls'}` +
      (state.hud.resultPending ? ' · actual C2 result pending (one source wake)' : '');
    if (state.frames % 15 === 0) {
      const policy = state.kernel;
      details.textContent = `${build.mainAssembly} / .NET ${build.sdkVersion} browser-WASM\n` +
        `14 real actors · ${state.hud.primitives} original HUD commands · ${state.assets.worldDraws} original world draws\n` +
        `${policy.policy} · repairs ${policy.repairCount ?? 'unavailable'} · raw/effective handicap ` +
        `${policy.teamHandicap?.map(team => `${team.team}:${team.raw}/${team.effective}`).join(' · ') || 'unavailable'}\n` +
        `${policy.limitation}\n` +
        `${policy.repairs?.slice(-4).map(repair => `Repair ${repair.sequence} @${repair.sourceTick}: ` +
          `${repair.kind} subject${repair.subject} ${repair.rawIndex}->${repair.effectiveIndex} ${repair.sourceContext}`).join('\n') || 'No compatibility repairs observed.'}\n` +
        `${renderer.device.unmaskedRenderer || renderer.device.renderer}\n` +
        `Fire ${state.effects.enabled ? 'portable original SDL/17-TGA' : 'disabled'} · ` +
        `kick meters ${state.kick.mode} (authored151/27/209, NOT native Composit)\n` +
        `Popped head ${state.assets.poppedHead ? 'original phead SDL-derived, raw4/HEDSHU1, native head.h unqualified' : 'no input; no fallback'}\n` +
        'Free ball original medball2/HEDSHU, actual source-angle matrix; native header/pixels unqualified\n' +
        `${audioStatus} · native Unity / arcade bit-exact acceptance open`;
    }
    frameHandle = requestAnimationFrame(frame);
  } catch (error) { fail(error); }
}

function traceAdvance(kind, elapsedSeconds, observedSeconds, action) {
  const beforePresent = callerTrace.presentCount;
  const row = callerTrace.begin(kind, { elapsedSeconds, observedSeconds,
    generation: callerDrain?.generation ?? 0, debtBefore: api.HasWholeSourceStepDebt(),
    lastRenderedPacketBefore: callerTrace.lastPacket,
    beforeProvenance: 'last rendered packet; NOT authoritative current live source/host',
    rendered: false, actualPacketAtEnd: null });
  try { const result = action(); row.hostSteps = result; row.completed = true; return result; }
  finally {
    callerTrace.finish(row);
    row.debtAfter = row.completed ? api.HasWholeSourceStepDebt() : null;
    row.rendered = callerTrace.presentCount !== beforePresent;
    if (row.rendered) row.actualPacketAtEnd = callerTrace.lastPacket;
  }
}

function drainSourceDebt() {
  const d = lastDimensions;
  return traceAdvance('message-channel-drain', 0, null,
    () => api.Frame(0, d.width, d.height, d.cssWidth, d.cssHeight, d.dpr));
}

function observeHostTime() {
  return observedHostClock.sample(performance.now(), !input?.paused && !document.hidden &&
    document.hasFocus() && (!audio?.unlocked || audio.frameReady));
}

async function startPlayer() {
  if (failed) return;
  const [{ dotnet }, { SourceRenderer, sourceObservation },
    { SourceInput, ObservedHostClock, SourceCallerTrace, SourceDebtDrain }] = await Promise.all([
    requiredModule('./_framework/dotnet.js'), requiredModule('./renderer.js'), requiredModule('./input.js'),
  ]);
  if (failed) return;
  copySourceObservation = sourceObservation;
  CallerTrace = SourceCallerTrace; DebtDrain = SourceDebtDrain;
  renderer = new SourceRenderer(canvas);
  const originalData = await loadData();
  if (failed) return;
  if (callerTrace) {
    for (const name of ['drawScene', 'drawEffects', 'drawHud', 'assertHealthy', 'texture', 'mesh', 'resize']) {
      const original = renderer[name];
      renderer[name] = function (...args) {
        return callerTrace.stage('js-webgl-' + name, () => original.apply(this, args),
          name === 'texture' || name === 'mesh' ? { resourceId: String(args[0]).slice(0, 160) } : {});
      };
    }
  }
  progress('Loading the genuine .NET10 WebAssembly runtime and C# production gameplay assembly');
  const runtimeBuilder = dotnet.withDiagnosticTracing(false);
  if (build.verifiedRuntimeResources) {
    runtimeBuilder.withResourceLoader(createVerifiedRuntimeResourceLoader({
      baseUrl: import.meta.url, resources: build.verifiedRuntimeResources, load: verifiedAssets.load,
    }));
  }
  const runtime = await runtimeBuilder.create();
  if (failed) return;
  runtime.setModuleImports('source-player-records', await requiredModule('./records.js'));
  if (failed) return;
  runtime.setModuleImports('source-player', {
    readFile: path => {
      const bytes = files.get(path);
      if (!bytes) throw new Error(`Required actual source file is absent: ${path}`);
      files.delete(path);
      return bytes;
    },
    progress,
    audioReady: () => audio?.ready === true,
    audioClockElapsed: epoch => audio?.sourceElapsed(epoch) ?? -1,
    audioSample: (...args) => {
      if (!audio) throw new Error('Decoded source audio was submitted before public user-gesture unlock.');
      audio.sample(...args);
    },
    audioCommands: commands => {
      if (!audio) throw new Error('Decoded source audio was submitted before public user-gesture unlock.');
      audio.commands(commands);
    },
    texture: (...args) => renderer.texture(...args),
    mesh: (...args) => renderer.mesh(...args),
    worldDraw: (...args) => renderer.worldDraw(...args),
    model: (...args) => renderer.model(...args),
    appearance: (...args) => renderer.appearance(...args),
    freeBall: (...args) => renderer.freeBall(...args),
    effects: (...args) => renderer.effects(...args),
    present: (...args) => {
      if (!callerTrace) return renderer.present(...args);
      const previousEpoch = callerTrace.lastPacket?.epoch;
      try { return callerTrace.present(args.at(-1), () => renderer.present(...args)); }
      finally {
        if (previousEpoch !== undefined && previousEpoch !== callerTrace.lastPacket?.epoch)
          callerDrain?.cancel('rendered-epoch-change');
      }
    },
  });
  const config = runtime.getConfig();
  build.mainAssembly = config.mainAssemblyName;
  const exports = await runtime.getAssemblyExports(config.mainAssemblyName);
  if (failed) return;
  api = exports.Blitz.SourcePlayer.Browser;
  if (!api?.Start || !api?.Frame || !api?.Capture || !api?.CabinetStations)
    throw new Error('Actual C# browser exports are missing; JavaScript gameplay is not a fallback.');
  await runtime.runMain();
  if (failed) return;
  if (['original-teams-complete-original303', 'original-teams-complete-original303-source-banks',
    'original-teams-complete-original303-records', 'original-teams-complete-original303-final',
    'original-teams-complete-original303-prematch',
    'original-teams-complete-original303-selected-world-v2', av306Profile,
    twoStationRecordsProfile].includes(originalData.profile)) {
    if (!api.StartCompleteOriginal) throw new Error('The actual complete-original C# data factory/audio entry is missing.');
    await startOriginalAudio(originalData.profile);
    if (failed) return;
    await new Promise(resolve => setTimeout(resolve, 0));
    if (failed) return;
    await api.StartCompleteOriginal(originalData.manifestBytes, location.href);
  } else api.Start(originalData.manifestBytes, location.href);
  if (failed) return;
  if (files.size !== 0) throw new Error(`The real source loaders left ${files.size} unconsumed input files.`);
  const transferStatus = document.querySelector('#records-transfer-status');
  document.querySelector('#records-export')?.addEventListener('click', async () => {
    try {
      if (!api.CanExportRecords()) return;
      const envelope = await api.ExportRecords();
      const blob = new Blob([envelope], { type: 'application/json' }), url = URL.createObjectURL(blob);
      const link = document.createElement('a');
      link.href = url; link.download = 'nflblitz-local-records.json'; link.click();
      setTimeout(() => URL.revokeObjectURL(url), 1000);
      transferStatus.textContent = 'Local record export requested.';
    } catch { transferStatus.textContent = 'Record export failed; stored data was not replaced.'; }
  });
  document.querySelector('#records-import')?.addEventListener('change', async event => {
    const file = event.target.files?.[0];
    event.target.value = '';
    if (!file || !api.CanImportRecords()) return;
    try {
      if (file.size > 32 * 1024 * 1024) throw new Error('Bounded local record file required.');
      api.ImportRecords(await file.text());
      transferStatus.textContent = 'Import staged; confirm through the current cabinet screen.';
    } catch { transferStatus.textContent = 'Record import failed; stored data was not replaced.'; }
  });
  observedHostClock = new ObservedHostClock();
  input = new SourceInput(api, canvas, Array.from(api.CabinetStations()), fail,
    () => { previousTime = undefined; }, observeHostTime,
    reason => callerDrain?.cancel(reason));
  if (callerTrace) {
    if (typeof api.HasWholeSourceStepDebt !== 'function')
      throw new Error('The explicit caller-drain profile requires the actual post-commit debt getter.');
    callerDrain = new DebtDrain({
      hasDebt: () => api.HasWholeSourceStepDebt(), drain: drainSourceDebt, fatal: fail, trace: callerTrace,
      canRun: () => !failed && lastDimensions != null && !input.paused && !document.hidden &&
        document.hasFocus() && (!audio?.unlocked || audio.frameReady),
    });
  }
  canvas.focus();
  if (failed) return;
  input.focus();
  if (failed) return;
  loadMilliseconds = performance.now() - start;
  frameHandle = requestAnimationFrame(frame);
}

try { await startPlayer(); } catch (error) { fail(error); }

function summary(values) {
  if (!values.length) return null;
  const ordered = values.slice().sort((a, b) => a - b);
  return { samples: values.length, mean: values.reduce((a, b) => a + b, 0) / values.length,
    p50: ordered[Math.floor(ordered.length * .5)], p95: ordered[Math.floor(ordered.length * .95)], max: ordered.at(-1) };
}

Object.defineProperty(globalThis, 'sourcePlayer', { value: Object.freeze({
  observe: () => {
    const sound = audio?.snapshot() ?? { unlocked: false, contextState: 'not-created', originalBankAdmission: false };
    return copySourceObservation ? copySourceObservation(renderer?.status, sound, ready, failed) :
      structuredClone({ ready, failed, state: renderer?.status, audioReceiver: sound });
  },
  snapshot: () => structuredClone({
    ready, failed, build, state: renderer?.status, device: renderer?.device,
    audioReceiver: audio?.snapshot() ?? { unlocked: false, contextState: 'not-created', originalBankAdmission: false },
    timings: { loadMilliseconds, frames: summary(timings.map(t => t.totalMilliseconds)),
      webgl: summary(timings.map(t => t.webglMilliseconds)), sourceSamples },
    resources: requests, verifiedAssetCache: verifiedAssets.snapshot(),
  }),
  sourceStepCosts: () => {
    if (!api || typeof api.SourceStepCosts !== 'function')
      throw new Error('Actual C# source-step diagnostics are not initialized.');
    return api.SourceStepCosts();
  },
  callerDiagnostics: () => {
    if (!callerTrace || !callerDrain)
      throw new Error('The explicit caller-drain profile is not initialized.');
    if (!failed && !input?.paused)
      throw new Error('Caller diagnostics require actual public pause or terminal failure.');
    return JSON.stringify({ ...callerTrace.snapshot(), enabled: true, profile: selectedCallerProfile,
      generation: callerDrain.generation, pending: callerDrain.pending != null, disposed: callerDrain.closed });
  },
  gpuProof: () => renderer.proof(),
}), writable: false, configurable: false });
