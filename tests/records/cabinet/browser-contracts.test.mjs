import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import test from 'node:test';
import { runInNewContext } from 'node:vm';
import { browserPaths, requireCallerDrainDiagnostics, requireCatalogVenueBinding, requireFullRegulationFinalPeriod, requireOriginalShadowMaterials, requireSourceStepCosts } from './browser-contracts.mjs';

const assigned = {
  project: '/data/source/nflblitz',
  destination: '/data/session/evidence/browser-current-001',
  external: true,
  evidenceRoot: '/data/session/evidence',
  runtimeRoot: '/home/runner/.blitz-current-001',
};

test('a fresh candidate admits explicitly assigned evidence and runtime paths without historical host names', () => {
  assert.deepEqual(browserPaths(assigned), {
    evidenceParent: assigned.evidenceRoot,
    runtimeRoot: assigned.runtimeRoot,
  });
});

test('assigned evidence does not authorize prefix siblings, nested paths or arbitrary child names', () => {
  for (const destination of [
    '/data/session/evidence-other/browser-current-001',
    '/data/session/evidence/nested/browser-current-001',
    '/data/session/evidence/current-001',
  ]) assert.throws(() => browserPaths({ ...assigned, destination }), /explicitly assigned evidence root/);
});

test('assigned roots reject relative, traversing and system-temporary paths before filesystem operations', () => {
  for (const runtimeRoot of ['relative', '/home/runner/../other', '/', '/tmp/browser', '/var/tmp/browser'])
    assert.throws(() => browserPaths({ ...assigned, runtimeRoot }), /directory/);
  assert.throws(() => browserPaths({ ...assigned, evidenceRoot: '/data/session/../evidence' }), /directory/);
});

test('the existing owned named-record and numbered deployment destinations remain admitted', () => {
  for (const destination of [
    '/data/source/nflblitz/.squad/.scratch/original-record-ui-20261030/browser-new-run',
    '/data/source/nflblitz/.squad/.scratch/docker-delivery-20261030/browser-012',
  ]) assert.equal(browserPaths({ ...assigned, destination, evidenceRoot: undefined }).evidenceParent, null);
});

test('original shadow proof requires both observed positive integer pixel counts', () => {
  requireOriginalShadowMaterials({ shadowPixels: 40, shadowMaterialPixels: 12 });
  for (const material of [
    undefined, {}, { shadowPixels: 40, shadowMaterialPixels: 0 },
    { shadowPixels: 0, shadowMaterialPixels: 12 },
    { shadowPixels: '40', shadowMaterialPixels: 12 },
    { shadowPixels: Infinity, shadowMaterialPixels: 12 },
  ]) assert.throws(() => requireOriginalShadowMaterials(material), /material-vs-no-shadow differential/);
});

test('a committed partial final cannot stand in for four source periods; real overtime remains eligible', () => {
  assert.equal(requireFullRegulationFinalPeriod({ completedQuarterIndex: 3 }), 4);
  assert.equal(requireFullRegulationFinalPeriod({ completedQuarterIndex: 5 }), 6);
  for (const period of [undefined, {}, { completedQuarterIndex: 2 }, { completedQuarterIndex: '3' }])
    assert.throws(() => requireFullRegulationFinalPeriod(period), /four completed source periods/);
});

test('actual step-cost contract rejects disabled, truncated and invalid metrics instead of substituting frame timing', () => {
  const row = tick => ({ SourceTick: tick, HostTick: Math.ceil(tick * 60 / 57),
    CoreMs: 1, SoundDrainMs: 0.1, PresentationMs: 0.2, ObserveMs: 0.05,
    CoreBytes: 32, SoundDrainBytes: 0, PresentationBytes: 16, ObserveBytes: 8 });
  const valid = { schema: 'source-player-source-step-cpu/v1', enabled: true, sourceAdvancingSteps: 80,
    first16: Array.from({ length: 16 }, (_, i) => row(i + 1)),
    last64: Array.from({ length: 64 }, (_, i) => row(i + 17)),
    render: { count: 10, totalMs: 40, maxMs: 8, bytes: 128 } };
  requireSourceStepCosts(valid, true);
  assert.throws(() => requireSourceStepCosts({ ...valid, enabled: false }), /enabled actual source-step/);
  assert.throws(() => requireSourceStepCosts({ ...valid, first16: valid.first16.slice(1) }), /exact bounded/);
  const invalid = structuredClone(valid); invalid.last64[0].CoreMs = NaN;
  assert.throws(() => requireSourceStepCosts(invalid), /elapsed field/);
  assert.throws(() => requireSourceStepCosts({ ...valid, render: undefined }), /Render measurements/);
});

test('catalog venue proof rejects a valid but stale or wrong-side home-team binding', () => {
  const valid = { homeTeamIndex:1, awayTeamIndex:23, worldEpoch:2, venue:{
    manifest:'a'.repeat(64), alternateVenuesAdmitted:true, teamEndZonesBound:true,
    homeTeamIndex:1, homePrefix:'FAL', selectionId:'source-field2-stadpie/field-0/stadium-0/FAL' } };
  requireCatalogVenueBinding(valid, 'Home');
  assert.throws(() => requireCatalogVenueBinding(valid, 'Away'), /designated home team/);
  assert.throws(() => requireCatalogVenueBinding({ ...valid,
    venue:{ ...valid.venue, homeTeamIndex:0 } }, 'Home'), /designated home team/);
  assert.throws(() => requireCatalogVenueBinding({ ...valid, venue:undefined }, 'Home'), /selected catalog venue/);
  assert.throws(() => requireCatalogVenueBinding(valid, undefined), /side explicitly/);
});

function callerTraceFixture() {
  const packet = { provenance:'fresh C# Render packet at synchronous Present; not a live getter',
    epoch:2, sourceTick:701, hostTick:738, phase:'Playing', matchPhase:'LiveBall',
    cameraSuspended:false };
  const frame = { sequence:1, kind:'raf-frame', beginMilliseconds:10, endMilliseconds:14, completed:true,
    elapsedSeconds:0.1, observedSeconds:1, generation:0, debtBefore:true, debtAfter:true, hostSteps:6,
    rendered:true, actualPacketAtEnd:packet, lastRenderedPacketBefore:null,
    beforeProvenance:'last rendered packet; NOT authoritative current live source/host' };
  const present = { sequence:2, kind:'js-webgl-present', beginMilliseconds:11, endMilliseconds:13,
    completed:true, packet };
  const drain = { ...frame, sequence:3, kind:'message-channel-drain', beginMilliseconds:15, endMilliseconds:18,
    elapsedSeconds:0, observedSeconds:null, debtAfter:false, rendered:false, actualPacketAtEnd:null };
  const cancel = { sequence:4, kind:'cancel-stale-drain', beginMilliseconds:19, endMilliseconds:19,
    completed:true, generation:1, reason:'pause' };
  const rows = [frame, present, drain, cancel];
  return { schema:'source-player-bounded-caller-drain-diagnostics/v1', enabled:true,
    profile:'aot-rooted-bounded-caller-drain/v1', firstCapacity:16, recentCapacity:128, slowestCapacity:16,
    events:4, dropped:0, presentCount:1, first16:rows, last128:rows,
    slowest16:[frame, drain, present, cancel], lastRenderedPacket:packet,
    generation:1, pending:false, disposed:false };
}

test('caller diagnostic contract rejects elapsed injection, unbounded drains and invented live tags', () => {
  requireCallerDrainDiagnostics(callerTraceFixture(), true);
  const retained = callerTraceFixture();
  const row = sequence => ({ sequence, kind:'js-webgl-mesh', beginMilliseconds:sequence,
    endMilliseconds:sequence + 1, completed:true, packet:null });
  Object.assign(retained, { events:200, dropped:40,
    first16:Array.from({ length:16 }, (_, index) => row(index + 1)),
    last128:Array.from({ length:128 }, (_, index) => row(index + 73)),
    slowest16:Array.from({ length:16 }, (_, index) => row(index + 17)) });
  requireCallerDrainDiagnostics(retained);
  for (const [mutate, message] of [
    [trace => { trace.enabled = false; }, /explicitly enabled/],
    [trace => { trace.last128[2].elapsedSeconds = 0.6; }, /zero new elapsed/],
    [trace => { trace.last128[2].observedSeconds = 1; }, /zero new elapsed/],
    [trace => { trace.last128[2].hostSteps = 7; }, /maximum of six/],
    [trace => { trace.last128[2].actualPacketAtEnd = trace.lastRenderedPacket; }, /must not invent/],
    [trace => { trace.lastRenderedPacket.provenance = 'current live source'; }, /real source\/host\/phase/],
    [trace => { trace.last128[2].endMilliseconds = 14; }, /timestamped/],
    [trace => { trace.dropped++; }, /Dropped events/],
  ]) {
    const trace = callerTraceFixture(); mutate(trace);
    assert.throws(() => requireCallerDrainDiagnostics(trace, true), message);
  }
});

test('caller early gate requires an observed executed drain and real pause cancellation, not profile metadata', () => {
  const metadataOnly = callerTraceFixture();
  Object.assign(metadataOnly.last128[2], { kind:'raf-frame', observedSeconds:1 });
  assert.throws(() => requireCallerDrainDiagnostics(metadataOnly, true), /actual zero-elapsed drain/);
  const uncanceled = callerTraceFixture(); uncanceled.pending = true;
  assert.throws(() => requireCallerDrainDiagnostics(uncanceled, true), /cancel pending/);
  const wrongBoundary = callerTraceFixture(); wrongBoundary.last128[3].reason = 'restart';
  assert.throws(() => requireCallerDrainDiagnostics(wrongBoundary, true), /actual public pause/);
});

test('browser-driver unit: the actual caller getter refuses active reads and detects clock or input mutation', async () => {
  const source = await readFile(new URL('./browser.mjs', import.meta.url), 'utf8');
  const begin = source.indexOf('async function callerDrainDiagnostics(');
  const end = source.indexOf('function stepWindowSummary(', begin);
  assert(begin >= 0 && end > begin);
  const driver = source.slice(begin, end);
  const run = async ({ active = false, terminal = false, mutate, nonstring = false } = {}) => {
    let reads = 0;
    const evidence = {};
    const observation = { failed:terminal, state:{ active, sourceTick:701, input:{ samples:4 },
      realtimeClock:{ executedHostSteps:738 } }, audioReceiver:{
      contextState:active ? 'running' : 'suspended', epoch:2, sourceOriginTick:0,
      clock:{ logicalPlaybackSample:800, transport:{ phase:active ? 'running' : 'paused',
        frozenSample:800, segmentId:1, startSample:800 } } } };
    const player = {
      observe:() => structuredClone(observation),
      callerDiagnostics:() => {
        reads++;
        if (mutate) mutate(observation);
        const trace = callerTraceFixture();
        return nonstring ? trace : JSON.stringify(trace);
      },
    };
    const promise = runInNewContext(driver + '\ncallerDrainDiagnostics("unit-only", false)', {
      assert, Date, evidence, requireCallerDrainDiagnostics,
      value:async expression => runInNewContext(expression, { sourcePlayer:player }),
    });
    return { promise, evidence, reads:() => reads };
  };
  const paused = await run(); await paused.promise;
  assert.equal(paused.reads(), 1);
  assert.equal(paused.evidence.callerDiagnosticsReads.length, 1);
  const active = await run({ active:true });
  await assert.rejects(active.promise, /paused\/frozen-sample or terminal barrier/);
  assert.equal(active.reads(), 0);
  const terminal = await run({ active:true, terminal:true }); await terminal.promise;
  for (const mutate of [
    observation => { observation.state.sourceTick++; },
    observation => { observation.state.input.samples++; },
    observation => { observation.audioReceiver.clock.logicalPlaybackSample++; },
  ]) {
    const changed = await run({ mutate });
    await assert.rejects(changed.promise, /must not advance, reactivate, capture input/);
    assert.equal(typeof changed.evidence.callerDiagnosticsReads[0].serialized, 'string',
      'Preserve the exact raw diagnostic receipt even when the read-only contract fails.');
  }
  const wrongType = await run({ nonstring:true });
  await assert.rejects(wrongType.promise, /exact public JSON string/);
});

test('browser-driver unit: the actual GPU observation rejects missing shadow pixels before accepting resume', async () => {
  const source = await readFile(new URL('./browser.mjs', import.meta.url), 'utf8');
  const begin = source.indexOf('async function customSelection()');
  const end = source.indexOf('async function firstNormalPlayAfterResume()', begin);
  assert(begin >= 0 && end > begin);
  const driver = source.slice(begin, end);
  const run = materials => runInNewContext(driver + '\ncustomSelection()', {
    assert, requireOriginalShadowMaterials, Date, stepCostsRequired:false, callerDrainRequired:false,
    currentStage: '', evidence: { actualMovingPlay: { fixture: 'driver unit, not a browser run' } },
    keys: { a: 'KeyV', b: 'KeyB', c: 'KeyN' },
    state: async () => ({ sourceTick: 100, matchPhase: 'PlaySelection', possession: 'Home' }),
    release: async () => {}, pause: async () => {}, tap: async () => {}, chord: async () => {},
    screenshot: async () => {}, doubleTurbo: async () => {}, key: async () => {},
    clockSample: sample => sample,
    value: async () => materials,
    until: async (predicate, label) => {
      const sample = {
        sourceTick: 150, intro: 0, matchPhase: 'PreSnap', menuHomePage: 4,
        elements: ['selection/Home/custom/0/route/0/unit'],
        audibleAction: label === 'real-double-turbo-audibles' ? 1 : 0,
        audibleChoice: label === 'real-signed-custom-audible' ? 1 : 0, audibleCaller: 0,
        selected: { OffenseFlipped: label === 'real-signed-custom-audible' },
        active: label !== 'public-pause-for-gpu-readback',
        audioState: label === 'public-pause-for-gpu-readback' ? 'suspended' : 'running',
        audioVoices: 1, audioSignal: { rms: 0.1 }, audioEpoch: 5, audioSourceOrigin: 17,
        audioClock: { logicalPlaybackSample: 600, transport: {
          phase: label === 'public-pause-for-gpu-readback' ? 'paused' : 'running',
          frozenSample: 600, startSample: 600,
          segmentId: label === 'public-resume-after-gpu-readback' ? 5 : 4,
        } },
      };
      assert(predicate(sample), 'The driver-unit observation must satisfy its declared stage: ' + label);
      return sample;
    },
  });
  await assert.rejects(run({ shadowPixels: 0, shadowMaterialPixels: 0 }), /material-vs-no-shadow differential/);
  await run({ shadowPixels: 40, shadowMaterialPixels: 12 });
});

test('browser-driver unit: hidden render caches are not live inputs, but resumed replay and a moving media fence reject', async () => {
  const source = await readFile(new URL('./browser.mjs', import.meta.url), 'utf8');
  const begin = source.indexOf('async function physicalFocusRelease()');
  const end = source.indexOf('async function fullMatch()', begin);
  assert(begin >= 0 && end > begin);
  const driver = source.slice(begin, end);
  const run = async ({ replayed = false, transientReplay = false, movingFence = false } = {}) => {
    const events = [];
    await runInNewContext(driver + '\nphysicalFocusRelease()', {
      assert, evidence: {}, gameTargetId: 'game',
      browser: { send: async method => method === 'Target.createTarget' ? { targetId: 'other' } : {} },
      page: { send: async () => {} }, screenshot: async () => {},
      state: async () => ({ inputSamples: 1, participatingStationMask: 1 }),
      release: async () => { events.push('release'); }, chord: async () => { events.push('held'); },
      until: async (predicate, label) => {
        const returning = label === 'actual-focus-return-without-held-input-replay';
        const blurred = label === 'actual-blur-releases-four-physical-cabinets';
        const sample = {
          sourceTick: returning ? 52 : 51, inputSamples: returning ? 3 : 2,
          inputButtons: Array(4).fill(returning && !replayed ? 0 : 4),
          inputAxes: Array.from({ length: 8 }, (_, index) => returning && !replayed ? 0 : index % 2 ? -1 : 0),
          active: true, documentHidden: blurred, documentFocused: !blurred,
          audioState: blurred ? 'suspended' : 'running', inputWaitingForNeutral: false, audioEpoch: 2,
          audioClock: { logicalPlaybackSample: movingFence ? 799 : 800,
            transport: { frozenSample: 800, startSample: 800 } },
        };
        const candidates = returning && transientReplay ?
          [{ ...sample, inputButtons: Array(4).fill(4) }, sample] : [sample];
        const accepted = candidates.find(candidate => predicate(candidate));
        assert(accepted, 'Rejected stage: ' + label);
        events.push(label);
        return accepted;
      },
    });
    return events;
  };
  const events = await run();
  const held = events.indexOf('held');
  const firstLaterRelease = events.findIndex((event, index) => index > held && event === 'release');
  assert(firstLaterRelease > events.indexOf('actual-focus-return-without-held-input-replay'));
  await assert.rejects(run({ replayed: true }), /actual-focus-return-without-held-input-replay/);
  await assert.rejects(run({ transientReplay: true }), /actual-focus-return-without-held-input-replay/);
  await assert.rejects(run({ movingFence: true }), /actual-blur-releases-four-physical-cabinets/);
});
