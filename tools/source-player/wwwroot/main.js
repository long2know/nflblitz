const canvas = document.querySelector('#game'), status = document.querySelector('#status');
const details = document.querySelector('#runtime-details'), start = performance.now();
const loader = globalThis.sourcePlayerLoader, { fail, progress } = loader;
let failed = false, ready = false, api, renderer, input, frameHandle, previousTime;
let diagnostic = false, build, loadMilliseconds;
let audio, audioDisposalInstalled = false;
let observedHostClock;
let copySourceObservation;
const timings = [], sourceSamples = [];
const files = new Map(), requests = [];

const digest = async bytes => Array.from(new Uint8Array(await crypto.subtle.digest('SHA-256', bytes)))
  .map(byte => byte.toString(16).padStart(2, '0')).join('');

loader.onFailure(() => {
  failed = true; ready = false;
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
  if (manifestHash !== build.dataManifestSha256) throw new Error('Served private data manifest does not match this actual build receipt.');
  const manifest = JSON.parse(new TextDecoder().decode(manifestBytes));
  if (manifest.schema !== 'blitz-portable-source-player/v1') throw new Error('Unknown original-data bundle.');
  const seen = new Set();
  for (const file of manifest.files) {
    if (!/^[A-Za-z0-9_.\-/]+$/.test(file.path) || file.path.split('/').some(p => !p || p === '.' || p === '..') ||
        seen.has(file.path.toLowerCase()) || file.bytes <= 0 || !/^[0-9a-f]{64}$/.test(file.sha256))
      throw new Error(`Unsafe/duplicate/unpinned private source file ${file.path}.`);
    seen.add(file.path.toLowerCase());
  }
  let next = 0, done = 0;
  await Promise.all(Array.from({ length: 6 }, async () => {
    while (!failed && next < manifest.files.length) {
      const file = manifest.files[next++];
      const bytes = await fetchBytes('./data/' + file.path);
      if (failed) return;
      const hash = await digest(bytes);
      if (failed) return;
      if (bytes.length !== file.bytes || hash !== file.sha256)
        throw new Error(`Actual original asset content pin mismatch: ${file.path}`);
      files.set(file.path, bytes);
      progress(`Verifying private original source/asset inputs: ${++done}/${manifest.files.length}`);
    }
  }));
  if (failed) return;
  const settings = JSON.parse(new TextDecoder().decode(files.get(manifest.entrypoints.settings)));
  return { manifestBytes, profile: manifest.profile, stations: settings.stations.map(station => station.id) };
}

async function startOriginalAudio() {
  const { SourceAudioReceiver } = await requiredModule('./audio.js');
  audio ??= new SourceAudioReceiver({ onFailure: fail });
  const button = document.querySelector('#start-original-audio');
  if (!button) throw new Error('The explicit complete-original user-gesture start control is missing.');
  button.hidden = false;
  document.querySelector('#audio-qualification').textContent =
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
    try { api?.Shutdown?.(); } catch (error) { fail(error); }
    closeAudio();
  }, { once: true });
  audioDisposalInstalled = true;
}

function frame(time) {
  if (failed) return;
  try {
    if (audio?.unlocked && !audio.frameReady) {
      observedHostClock?.sample(performance.now(), false);
      previousTime = undefined;
      frameHandle = requestAnimationFrame(frame);
      return;
    }
    const dimensions = renderer.resize(), before = performance.now();
    input.poll();
    if (failed) return;
    const observedTime = observeHostTime();
    const elapsed = previousTime === undefined ? 0 : observedTime - previousTime;
    previousTime = observedTime;
    const steps = api.FrameObserved ?
      api.FrameObserved(elapsed, observedTime, dimensions.width, dimensions.height, dimensions.cssWidth, dimensions.cssHeight, dimensions.dpr) :
      api.Frame(elapsed, dimensions.width, dimensions.height, dimensions.cssWidth, dimensions.cssHeight, dimensions.dpr);
    if (failed) return;
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

function observeHostTime() {
  return observedHostClock.sample(performance.now(), !input?.paused && !document.hidden &&
    document.hasFocus() && (!audio?.unlocked || audio.frameReady));
}

async function startPlayer() {
  if (failed) return;
  const [{ dotnet }, { SourceRenderer, sourceObservation }, { SourceInput, ObservedHostClock }] = await Promise.all([
    requiredModule('./_framework/dotnet.js'), requiredModule('./renderer.js'), requiredModule('./input.js'),
  ]);
  if (failed) return;
  copySourceObservation = sourceObservation;
  renderer = new SourceRenderer(canvas);
  const originalData = await loadData();
  if (failed) return;
  progress('Loading the genuine .NET10 WebAssembly runtime and C# production gameplay assembly');
  const runtime = await dotnet.withDiagnosticTracing(false).create();
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
    present: (...args) => renderer.present(...args),
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
    'original-teams-complete-original303-prematch'].includes(originalData.profile)) {
    if (!api.StartCompleteOriginal) throw new Error('The actual complete-original C# data factory/audio entry is missing.');
    await startOriginalAudio();
    if (failed) return;
    await new Promise(resolve => setTimeout(resolve, 0));
    if (failed) return;
    await api.StartCompleteOriginal(originalData.manifestBytes, location.href);
  } else api.Start(originalData.manifestBytes, location.href);
  if (failed) return;
  if (files.size !== 0) throw new Error(`The real source loaders left ${files.size} unconsumed private input files.`);
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
    () => { previousTime = undefined; }, observeHostTime);
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
    resources: requests,
  }),
  gpuProof: () => renderer.proof(),
}), writable: false, configurable: false });
