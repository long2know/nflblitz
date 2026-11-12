import assert from 'node:assert/strict';
import { basename, dirname, isAbsolute, normalize, resolve, sep } from 'node:path';

function absoluteRoot(value, label) {
  assert(value && isAbsolute(value) && normalize(value) === value && value !== sep,
    `${label} must be an explicit normalized absolute directory.`);
  assert(!['/tmp', '/var/tmp'].some(root => value === root || value.startsWith(root + sep)),
    `${label} must not use a system temporary directory.`);
  return value;
}

export function browserPaths({ project, destination, external, evidenceRoot, runtimeRoot }) {
  const owned = resolve(project, '.squad/.scratch/original-record-ui-20261030');
  const dockerOwned = resolve(project, '.squad/.scratch/docker-delivery-20261030');
  const assignedParent = evidenceRoot ? absoluteRoot(evidenceRoot, 'Evidence root') : null;
  const assigned = assignedParent && dirname(destination) === assignedParent &&
    /^browser-[A-Za-z0-9][A-Za-z0-9.-]*$/.test(basename(destination));
  assert(destination.startsWith(owned + sep) ||
    external && dirname(destination) === dockerOwned && /^browser-\d{3}$/.test(basename(destination)) ||
    assigned, 'Use the owned record UI root or a new browser-* child of the explicitly assigned evidence root.');
  return {
    evidenceParent: assigned ? assignedParent : null,
    runtimeRoot: absoluteRoot(runtimeRoot || '/home/long2know/.bru-4e97629f', 'Chrome runtime root'),
  };
}

export function requireOriginalShadowMaterials(materials) {
  assert(materials && ['shadowPixels', 'shadowMaterialPixels'].every(
    field => Number.isInteger(materials[field]) && materials[field] > 0),
  'The actual current gameplay geometry must render original shadows, including the material-vs-no-shadow differential.');
}

export function requireFullRegulationFinalPeriod(period) {
  assert(Number.isInteger(period?.completedQuarterIndex) && period.completedQuarterIndex >= 3,
    'The genuine committed final must contain at least four completed source periods.');
  return period.completedQuarterIndex + 1;
}

export function requireSourceStepCosts(costs, completeWindows = false) {
  assert(costs?.schema === 'source-player-source-step-cpu/v1' && costs.enabled === true,
    'The sealed candidate must expose enabled actual source-step diagnostics.');
  const count = costs.sourceAdvancingSteps;
  assert(Number.isSafeInteger(count) && count >= 0 &&
    Array.isArray(costs.first16) && costs.first16.length === Math.min(count, 16) &&
    Array.isArray(costs.last64) && costs.last64.length === Math.min(count, 64),
  'Source-step diagnostics must retain their exact bounded startup/recent windows.');
  if (completeWindows) assert(count >= 64, 'Actual gameplay must fill both source-step diagnostic windows.');
  for (const row of [...costs.first16, ...costs.last64]) {
    assert(Number.isSafeInteger(row.SourceTick) && row.SourceTick > 0 &&
      Number.isSafeInteger(row.HostTick) && row.HostTick >= row.SourceTick,
    'Source-step diagnostic rows must identify actual advancing source/host ticks.');
    for (const field of ['CoreMs', 'SoundDrainMs', 'PresentationMs', 'ObserveMs'])
      assert(Number.isFinite(row[field]) && row[field] >= 0, 'Invalid source-step elapsed field: ' + field);
    for (const field of ['CoreBytes', 'SoundDrainBytes', 'PresentationBytes', 'ObserveBytes'])
      assert(Number.isSafeInteger(row[field]) && row[field] >= 0, 'Invalid source-step allocation field: ' + field);
  }
  assert(Number.isSafeInteger(costs.render?.count) && costs.render.count >= 0 &&
    Number.isFinite(costs.render.totalMs) && costs.render.totalMs >= 0 &&
    Number.isFinite(costs.render.maxMs) && costs.render.maxMs >= 0 &&
    Number.isSafeInteger(costs.render.bytes) && costs.render.bytes >= 0,
  'Render measurements must remain separate from source-step measurements.');
  return costs;
}

export function requireCallerDrainDiagnostics(trace, requireExecutedDrain = false) {
  assert(trace?.schema === 'source-player-bounded-caller-drain-diagnostics/v1' &&
    trace.enabled === true && trace.profile === 'aot-rooted-bounded-caller-drain/v1',
  'The sealed candidate must expose its explicitly enabled bounded caller-drain profile.');
  assert(trace.firstCapacity === 16 && trace.recentCapacity === 128 && trace.slowestCapacity === 16 &&
    Number.isSafeInteger(trace.events) && trace.events >= 0 &&
    Number.isSafeInteger(trace.presentCount) && trace.presentCount >= 0 &&
    Number.isSafeInteger(trace.generation) && trace.generation >= 0 &&
    typeof trace.pending === 'boolean' && typeof trace.disposed === 'boolean' &&
    Array.isArray(trace.first16) && trace.first16.length === Math.min(trace.events, 16) &&
    Array.isArray(trace.last128) && trace.last128.length === Math.min(trace.events, 128) &&
    Array.isArray(trace.slowest16) && trace.slowest16.length <= 16,
  'Caller diagnostics must retain their exact bounded windows and lifecycle counters.');
  const retained = new Set([...trace.first16, ...trace.last128, ...trace.slowest16].map(row => row.sequence));
  assert.equal(trace.dropped, trace.events - retained.size,
    'Dropped events must account for the unique first/recent/slowest windows, not imply a complete trace.');
  for (const [rows, first] of [[trace.first16, 1], [trace.last128, trace.events - trace.last128.length + 1]])
    rows.forEach((row, index) => assert.equal(row.sequence, first + index,
      'Caller window sequence numbers must identify the retained event interval.'));
  const packet = value => {
    assert(value && value.provenance === 'fresh C# Render packet at synchronous Present; not a live getter' &&
      Number.isSafeInteger(value.epoch) && value.epoch >= 0 &&
      Number.isSafeInteger(value.sourceTick) && value.sourceTick >= 0 &&
      Number.isSafeInteger(value.hostTick) && value.hostTick >= value.sourceTick &&
      typeof value.phase === 'string' && typeof value.matchPhase === 'string' &&
      typeof value.cameraSuspended === 'boolean',
    'Rendered packet tags must identify their real source/host/phase and suspension provenance.');
  };
  const rows = [...trace.first16, ...trace.last128, ...trace.slowest16];
  for (const row of rows) {
    assert(Number.isSafeInteger(row.sequence) && row.sequence > 0 && row.sequence <= trace.events &&
      typeof row.kind === 'string' && typeof row.completed === 'boolean' &&
      Number.isFinite(row.beginMilliseconds) && row.beginMilliseconds >= 0 &&
      Number.isFinite(row.endMilliseconds) && row.endMilliseconds >= row.beginMilliseconds,
    'Caller events must preserve bounded, timestamped completed or faulted intervals.');
    if (row.packet) packet(row.packet);
    if (row.kind === 'raf-frame' || row.kind === 'message-channel-drain') {
      assert(Number.isFinite(row.elapsedSeconds) && row.elapsedSeconds >= 0 &&
        Number.isSafeInteger(row.generation) && row.generation >= 0 &&
        typeof row.debtBefore === 'boolean' && typeof row.rendered === 'boolean' &&
        row.beforeProvenance === 'last rendered packet; NOT authoritative current live source/host' &&
        (row.completed ? typeof row.debtAfter === 'boolean' : row.debtAfter === null),
      'Frame/drain diagnostics must distinguish actual debt from stale rendered packet tags.');
      if (row.completed) assert(Number.isSafeInteger(row.hostSteps) && row.hostSteps >= 0 && row.hostSteps <= 6,
        'Each actual caller invocation must preserve the existing maximum of six host steps.');
      if (row.kind === 'message-channel-drain')
        assert(row.elapsedSeconds === 0 && row.observedSeconds === null && row.debtBefore === true,
          'A retained-debt drain must add zero new elapsed time and must not re-observe or reanchor the clock.');
      else assert(Number.isFinite(row.observedSeconds) && row.observedSeconds >= 0,
        'Observed RAF timestamps must remain explicit and separate from zero-elapsed drains.');
      if (row.rendered) packet(row.actualPacketAtEnd);
      else assert.equal(row.actualPacketAtEnd, null,
        'A callback without Present must not invent an authoritative live source/host packet.');
    }
    if (row.kind === 'cancel-stale-drain')
      assert(typeof row.reason === 'string' && row.reason.length > 0 &&
        Number.isSafeInteger(row.generation) && row.generation > 0,
      'Canceled queued drains must identify their lifecycle reason and generation.');
  }
  if (trace.lastRenderedPacket) packet(trace.lastRenderedPacket);
  if (requireExecutedDrain) {
    assert.equal(trace.pending, false, 'Public pause must cancel pending caller drains.');
    assert(rows.some(row => row.kind === 'message-channel-drain' && row.completed && row.hostSteps > 0),
      'The early causal gate needs a retained actual zero-elapsed drain, not just an enabled profile flag.');
    assert(rows.some(row => row.kind === 'cancel-stale-drain' && row.reason === 'pause'),
      'The actual public pause must leave a captured caller-drain cancellation.');
  }
  return trace;
}

export function requireCatalogVenueBinding(sample, designatedSide) {
  assert(designatedSide === 'Home' || designatedSide === 'Away',
    'The sealed catalog probe must name its designated home side explicitly.');
  const team = designatedSide === 'Home' ? sample.homeTeamIndex : sample.awayTeamIndex;
  const venue = sample.venue;
  assert(Number.isInteger(team) && team >= 0 && team < 31 && venue &&
    venue.alternateVenuesAdmitted === true && venue.teamEndZonesBound === true &&
    /^[a-f0-9]{64}$/.test(venue.manifest) && typeof venue.homePrefix === 'string' &&
    typeof venue.selectionId === 'string' && venue.selectionId.endsWith('/' + venue.homePrefix),
  'The actual game must publish its authenticated selected catalog venue.');
  assert.equal(venue.homeTeamIndex, team, 'Actual venue/end-zone ownership must match the designated home team.');
  return { designatedSide, team, worldEpoch:sample.worldEpoch, venue };
}
