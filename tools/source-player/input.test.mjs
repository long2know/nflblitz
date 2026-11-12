import assert from 'node:assert/strict';
import test from 'node:test';
import { readFile } from 'node:fs/promises';
import vm from 'node:vm';
import { createHash } from 'node:crypto';
import { CABINET_KEYS, cabinetPacket, SourceInput, ObservedHostClock,
  SourceCallerTrace, SourceDebtDrain } from './wwwroot/input.js';

test('observed host clock retains real active intervals, excludes explicit suspension, and rejects rewinds', () => {
  const clock = new ObservedHostClock();
  const near = (actual, expected) => assert(Math.abs(actual - expected) < 1e-12);
  assert.equal(clock.sample(1000, true), 0);
  assert.equal(clock.sample(1141, true), .141);
  near(clock.sample(1160, false), .16);
  near(clock.sample(9000, false), .16);
  near(clock.sample(9100, true), .16);
  near(clock.sample(9125, true), .185);
  assert.throws(() => clock.sample(9124, true), /monotonic/);
});

test('all four browser cabinets retain the exact production A/B/C keyboard bindings', () => {
  for (let station = 0; station < 4; station++) {
    const keys = new Set([CABINET_KEYS[station][0], ...CABINET_KEYS[station].slice(4)]);
    const packet = cabinetPacket(keys, [], [station]);
    assert.equal(packet.axes[station * 2], 0);
    assert.equal(packet.axes[station * 2 + 1], -1);
    assert.equal(packet.buttons[station], 7);
    assert.equal(packet.buttons.filter(value => value !== 0).length, 1);
  }
});

test('standard controller sends actual axes and source buttons without JavaScript gameplay', () => {
  const buttons = Array.from({ length: 16 }, () => ({ pressed: false }));
  buttons[0].pressed = true; buttons[5].pressed = true;
  const pad = { connected: true, mapping: 'standard', axes: [.47, -.51], buttons };
  assert.deepEqual(cabinetPacket(new Set(), [pad], [0]),
    { axes: [.47, -.51, 0, 0, 0, 0, 0, 0], buttons: [5, 0, 0, 0] });
  const keyboard = cabinetPacket(new Set(['KeyD']), [pad], [0]);
  assert.equal(keyboard.axes[0], 1); assert.equal(keyboard.axes[1], 0);
  buttons[12].pressed = true; buttons[14].pressed = true;
  assert.deepEqual(cabinetPacket(new Set(), [pad], [0]).axes.slice(0, 2), [-1, -1]);
});

test('ambiguous gamepads fail explicitly, and releasing keys produces a neutral source packet', () => {
  assert.throws(() => cabinetPacket(new Set(), [{ connected: true, mapping: '', axes: [], buttons: [] }], [0]),
    /no standard mapping/);
  assert.throws(() => cabinetPacket(new Set(), [{ connected: true, mapping: 'standard', axes: [NaN, 0], buttons: [] }], [0]),
    /invalid raw axes/);
  assert.deepEqual(cabinetPacket(new Set(), [], [0]), { axes: Array(8).fill(0), buttons: [0, 0, 0, 0] });
});

test('all four configured cabinets clear held controls on blur and resume without replaying them', () => {
  const originals = new Map(['window', 'document', 'navigator'].map(name =>
    [name, Object.getOwnPropertyDescriptor(globalThis, name)]));
  let input;
  try {
    const window = new EventTarget(), document = new EventTarget(), canvas = new EventTarget();
    let focused = true;
    document.hidden = false; document.hasFocus = () => focused; canvas.focus = () => {};
    for (const [name, value] of Object.entries({ window, document, navigator: { getGamepads: () => [] } }))
      Object.defineProperty(globalThis, name, { value, configurable: true });
    const captured = [], active = [], faults = [];
    input = new SourceInput({
      Capture: (axes, buttons) => captured.push({ axes: [...axes], buttons: [...buttons] }),
      SetActive: value => active.push(value),
    }, canvas, [0, 1, 2, 3], error => faults.push(error), () => {});
    const press = code => {
      const event = new Event('keydown', { cancelable: true });
      Object.defineProperties(event, { code: { value: code }, repeat: { value: false } });
      window.dispatchEvent(event);
      assert.equal(event.defaultPrevented, true);
    };
    for (const binding of CABINET_KEYS) { press(binding[0]); press(binding[5]); }
    assert.deepEqual(captured.at(-1), { axes: [0, -1, 0, -1, 0, -1, 0, -1], buttons: [2, 2, 2, 2] });
    focused = false; window.dispatchEvent(new Event('blur'));
    assert.equal(input.keys.size, 0);
    assert.deepEqual(captured.at(-1), { axes: Array(8).fill(0), buttons: [0, 0, 0, 0] });
    assert.equal(active.at(-1), false);
    focused = true; window.dispatchEvent(new Event('focus')); input.poll();
    assert.deepEqual(captured.at(-1), { axes: Array(8).fill(0), buttons: [0, 0, 0, 0] });
    assert.equal(active.at(-1), true);
    for (const binding of CABINET_KEYS) press(binding[5]);
    assert.deepEqual(captured.at(-1).buttons, [2, 2, 2, 2]);
    assert.deepEqual(faults, []);
    input.close();
    const count = captured.length;
    window.dispatchEvent(new Event('blur'));
    assert.equal(captured.length, count);
  } finally {
    input?.close();
    for (const [name, descriptor] of originals) {
      if (descriptor) Object.defineProperty(globalThis, name, descriptor);
      else delete globalThis[name];
    }
  }
});

test('observed capture preserves every complete raw sample and uses measured host time, with legacy fallback unchanged', () => {
  const calls = [], input = { api: {
    CaptureObserved: (axes, buttons, time) => calls.push({ axes, buttons, time }),
    Capture: () => assert.fail('Observed capture must not also invoke a second legacy FIFO writer.'),
  }, observedClock: () => 1.25 };
  const axes = [0, -1, 1, 0, -.5, .5, 0, 0], buttons = [7, 5, 3, 1];
  SourceInput.prototype.capture.call(input, axes, buttons);
  assert.deepEqual(calls, [{ axes, buttons, time: 1.25 }]);
  input.observedClock = null;
  input.api.Capture = (a, b) => calls.push({ axes: a, buttons: b });
  SourceInput.prototype.capture.call(input, axes, buttons);
  assert.deepEqual(calls.at(-1), { axes, buttons });
});

test('focus reactivation samples held gamepads instead of manufacturing a neutral release', () => {
  const originals = new Map(['window', 'document', 'navigator'].map(name =>
    [name, Object.getOwnPropertyDescriptor(globalThis, name)]));
  let input;
  try {
    const window = new EventTarget(), document = new EventTarget(), canvas = new EventTarget();
    let focused = true;
    document.hidden = false; document.hasFocus = () => focused; canvas.focus = () => {};
    const pads = CABINET_KEYS.map(() => ({ connected: true, mapping: 'standard', axes: [.5, -.5],
      buttons: Array.from({ length: 16 }, (_, id) => ({ pressed: id === 5 })) }));
    for (const [name, value] of Object.entries({ window, document, navigator: { getGamepads: () => pads } }))
      Object.defineProperty(globalThis, name, { value, configurable: true });
    const captured = [], active = [], faults = [];
    input = new SourceInput({
      Capture: (axes, buttons) => captured.push({ axes: [...axes], buttons: [...buttons] }),
      SetActive: value => active.push(value),
    }, canvas, [0, 1, 2, 3], error => faults.push(error), () => {});
    input.poll();
    focused = false; window.dispatchEvent(new Event('blur'));
    assert.deepEqual(captured.at(-1).buttons, [0, 0, 0, 0]);
    focused = true; window.dispatchEvent(new Event('focus'));
    assert.deepEqual(captured.at(-1), {
      axes: [.5, -.5, .5, -.5, .5, -.5, .5, -.5], buttons: [4, 4, 4, 4],
    }, 'The real C# neutral latch must see controls still held across suspension, not a fabricated all-zero sample.');
    assert.equal(active.at(-1), true);
    for (const pad of pads) { pad.axes = [0, 0]; pad.buttons[5].pressed = false; }
    input.poll();
    assert.deepEqual(captured.at(-1), { axes: Array(8).fill(0), buttons: [0, 0, 0, 0] });
    assert.deepEqual(faults, []);
  } finally {
    input?.close();
    for (const [name, descriptor] of originals) {
      if (descriptor) Object.defineProperty(globalThis, name, descriptor);
      else delete globalThis[name];
    }
  }
});

test('keyboard autorepeat cannot manufacture a new physical press after pause, focus or restart clear', async () => {
  function exercise(InputType) {
    const originals = new Map(['window', 'document', 'navigator'].map(name =>
      [name, Object.getOwnPropertyDescriptor(globalThis, name)]));
    let input;
    try {
      for (const boundary of ['pause', 'focus', 'restart']) {
        const window = new EventTarget(), document = new EventTarget(), canvas = new EventTarget();
        let focused = true, time = 0, restarts = 0;
        document.hidden = false; document.hasFocus = () => focused; canvas.focus = () => {};
        for (const [name, value] of Object.entries({ window, document, navigator: { getGamepads: () => [] } }))
          Object.defineProperty(globalThis, name, { value, configurable: true });
        const packets = [], faults = [];
        input = new InputType({
          CaptureObserved: (axes, buttons) => packets.push({ axes: [...axes], buttons: [...buttons] }),
          SetActive() {}, Restart() { restarts++; },
        }, canvas, [0, 1, 2, 3], error => faults.push(error), () => {}, () => time++ / 1000);
        const key = (code, down = true, repeat = false) => {
          const event = new Event(down ? 'keydown' : 'keyup', { cancelable: true });
          Object.defineProperties(event, { code: { value: code }, repeat: { value: repeat } });
          window.dispatchEvent(event);
          assert.equal(event.defaultPrevented, true);
        };
        const held = CABINET_KEYS.flatMap(binding => [binding[0], ...binding.slice(4)]);
        for (const code of held) key(code);
        assert.deepEqual(packets.at(-1), { axes: [0, -1, 0, -1, 0, -1, 0, -1], buttons: [7, 7, 7, 7] });
        for (const code of held) key(code, true, true);
        assert.deepEqual(packets.at(-1).buttons, [7, 7, 7, 7], 'Repeating an already tracked held key stays held.');
        if (boundary === 'pause') { key('Escape'); key('Escape'); }
        else if (boundary === 'restart') { key('KeyR'); assert.equal(restarts, 1); }
        else {
          focused = false; window.dispatchEvent(new Event('blur'));
          focused = true; window.dispatchEvent(new Event('focus'));
        }
        for (const code of held) key(code, true, true);
        input.poll();
        assert.deepEqual(packets.at(-1), { axes: Array(8).fill(0), buttons: [0, 0, 0, 0] },
          boundary + ': untracked autorepeat is not a fresh cabinet press and cannot defeat the neutral-release boundary.');
        for (const code of held) { key(code, false); key(code); }
        assert.deepEqual(packets.at(-1), { axes: [0, -1, 0, -1, 0, -1, 0, -1], buttons: [7, 7, 7, 7] },
          'Actual new presses after release must still work for every station.');
        assert.deepEqual(faults, []);
        input.close(); input = null;
      }
    } finally {
      input?.close();
      for (const [name, descriptor] of originals) {
        if (descriptor) Object.defineProperty(globalThis, name, descriptor);
        else delete globalThis[name];
      }
    }
  }
  exercise(SourceInput);
  const source = await readFile(new URL('./wwwroot/input.js', import.meta.url), 'utf8');
  const guard = 'if (down && event.repeat && !this.keys.has(event.code)) return;';
  assert.equal(source.split(guard).length, 2);
  const mutant = await import('data:text/javascript,' + encodeURIComponent(source.replace(guard, '')));
  assert.throws(() => exercise(mutant.SourceInput), /untracked autorepeat is not a fresh cabinet press/,
    'Removing the real production admission guard must replay stale controls and fail the contract.');
});

test('actual production loader keeps authenticated AV306 and historical caller generations distinct', async t => {
  const avRoot = process.env.SOURCE_PLAYER_TEST_AV306_DATA;
  const oldRoot = process.env.SOURCE_PLAYER_TEST_1114_DATA;
  if (!avRoot || !oldRoot) { t.skip('Explicit authenticated AV306 and unchanged1114 roots required.'); return; }
  const source = await readFile(new URL('./wwwroot/main.js', import.meta.url), 'utf8');
  const constants = source.slice(source.indexOf('const callerProfile ='), source.indexOf('const timings ='));
  const loader = source.slice(source.indexOf('async function loadData('), source.indexOf('async function startOriginalAudio('));
  const av = new Uint8Array(await readFile(avRoot + '/manifest.json'));
  const old = new Uint8Array(await readFile(oldRoot + '/manifest.json'));
  async function probe(bytes, profile, body = loader, receiptChanges = {}) {
    let payloads = 0;
    const sentinel = new Error('ACTUAL_MANIFEST_ACCEPTED_BEFORE_PAYLOAD_FETCH');
    const hash = value => createHash('sha256').update(value).digest('hex');
    const manifest = JSON.parse(new TextDecoder().decode(bytes));
    const receipt = {
      schema: 'blitz-source-player-build/v1', runtimeFlavor: 'portable C# browser preview (not a Unity player build)',
      dataManifestSha256: hash(bytes), profile: manifest.profile, factory: manifest.factory,
      featureContract: manifest.featureContract,
      execution: { callerProfile: profile, mode: 'aot', runAOTCompilation: true, jsonReflectionEnabled: true,
        callerDrain: { schema: 'bounded-source-caller-drain/v1', scheduling: 'MessageChannel',
          elapsedSecondsPerDrain: 0, maximumHostStepsPerCallback: 6, maximumBridgeIntervalSeconds: .1,
          freshInputPollingPerDrain: 0, audioLeadChanged: false, sourceClockRngOrEpochChanged: false } },
      ...receiptChanges,
    };
    const context = {
      failed: false, build: null, callerTrace: null, CallerTrace: SourceCallerTrace,
      TextDecoder, digest: async value => hash(value), files: new Map(), progress() {},
      fetchBytes: async path => {
        if (path === './build-info.json') return new TextEncoder().encode(JSON.stringify(receipt));
        if (path === './data/manifest.json') return bytes;
        payloads++; throw sentinel;
      },
    };
    vm.runInNewContext(constants + '\n' + body, context);
    try { await context.loadData(); assert.fail('This boundary fixture stops only at the first actual payload fetch.'); }
    catch (error) { return { accepted: error === sentinel, payloads, error }; }
  }
  const avProfile = 'aot-rooted-bounded-caller-drain-av306/v1';
  const historicalProfile = 'aot-rooted-bounded-caller-drain/v1';
  for (const [bytes, profile] of [[av, avProfile], [old, historicalProfile]]) {
    const result = await probe(bytes, profile);
    assert.equal(result.accepted, true, result.error.message);
    assert(result.payloads > 0);
  }
  for (const [bytes, profile] of [[av, historicalProfile], [old, avProfile]]) {
    const result = await probe(bytes, profile);
    assert.equal(result.accepted, false);
    assert.equal(result.payloads, 0, 'Wrong-generation callers must fail before reading any asset.');
    assert.match(result.error.message, /mismatched explicit source caller-drain/);
  }
  const mutant = loader.replace('historicalCaller || av306Caller', 'historicalCaller');
  assert.notEqual(mutant, loader);
  const omitted = await probe(av, avProfile, mutant);
  assert.equal(omitted.accepted, false, 'Removing the actual new-profile caller admission must reject the real v3 manifest.');
  assert.equal(omitted.payloads, 0);
  const twoRoot = process.env.SOURCE_PLAYER_TEST_TWO_STATION_RECORDS_DATA;
  if (twoRoot) {
    const two = new Uint8Array(await readFile(twoRoot + '/manifest.json'));
    const twoCaller = 'aot-rooted-bounded-caller-drain-av306-two-station-records/v1';
    const accepted = await probe(two, twoCaller);
    assert.equal(accepted.accepted, true, accepted.error.message);
    assert(accepted.payloads > 0);
    for (const [bytes, caller] of [[two, avProfile], [two, historicalProfile], [av, twoCaller], [old, twoCaller]]) {
      const wrong = await probe(bytes, caller);
      assert.equal(wrong.accepted, false);
      assert.equal(wrong.payloads, 0, 'Named TwoStation cannot reuse an old caller/data tuple.');
      assert.match(wrong.error.message, /mismatched explicit source caller-drain/);
    }
    for (const receipt of [{ profile: 'original-teams-complete-av306' },
      { factory: 'SourceC2Composition.CreateCompleteAv306Simulation' }]) {
      const wrong = await probe(two, twoCaller, loader, receipt);
      assert.equal(wrong.accepted, false);
      assert.equal(wrong.payloads, 0, 'A genuine TwoStation manifest cannot run under a legacy factory receipt.');
      assert.match(wrong.error.message, /unauthenticated original-data/);
    }
    const removeNew = loader.replace('historicalCaller || av306Caller || twoStationRecordsCaller',
      'historicalCaller || av306Caller');
    assert.notEqual(removeNew, loader);
    const control = await probe(two, twoCaller, removeNew);
    assert.equal(control.accepted, false, 'Omitting the actual named TwoStation caller must fail before any payload fetch.');
    assert.equal(control.payloads, 0);
  }
});

function messageQueue() {
  const queue = [], port1 = { onmessage: null, close() { this.closed = true; } };
  const port2 = { postMessage: value => queue.push(structuredClone(value)), close() { this.closed = true; } };
  return { channel: { port1, port2 }, queue,
    deliver() { const value = queue.shift(); port1.onmessage?.({ data: value }); } };
}

test('explicit MessageChannel caller drains retained600ms under100ms frames with zero new elapsed and no input or RNG replay', async () => {
  const source = await readFile(new URL('./wwwroot/main.js', import.meta.url), 'utf8');
  const block = source.slice(source.indexOf('function traceAdvance('), source.indexOf('function observeHostTime('));
  const state = { pending: .6, observed: .6, lastFrame: .6, capturedTime: .6, hosts: 0,
    captures: 0, polls: 0, rng: [], edges: [{ target: 1, id: 'press' }, { target: 12, id: 'release' }], consumed: [] };
  const widths = { width: 800, height: 600, cssWidth: 800, cssHeight: 600, dpr: 1 };
  const frameCalls = [], observedCalls = [];
  function advance(elapsed) {
    state.pending += elapsed; state.observed += elapsed;
    const steps = Math.min(6, Math.floor((state.pending + 1e-12) * 60));
    for (let i = 0; i < steps; i++) {
      state.hosts++; state.rng.push(state.hosts * 104729);
      while (state.edges[0]?.target <= state.hosts) state.consumed.push(state.edges.shift().id);
    }
    state.pending = Math.max(0, state.pending - steps / 60);
    return steps;
  }
  const api = {
    Frame: (...args) => { frameCalls.push(args); return advance(args[0]); },
    FrameObserved: (nominal, seconds) => {
      assert(seconds >= state.capturedTime, 'An observed call must not rewind a real physical capture.');
      const elapsed = seconds - state.lastFrame; state.lastFrame = seconds;
      observedCalls.push({ nominal, seconds, elapsed }); return advance(elapsed);
    },
    CaptureObserved: seconds => {
      state.captures++; state.capturedTime = seconds;
      state.edges.push({ target: 43, id: 'new-event' });
    },
    HasWholeSourceStepDebt: () => state.pending >= 1 / 60 - 1e-12,
  };
  let timestamp = 0;
  const trace = new SourceCallerTrace(() => timestamp++);
  const context = { api, callerTrace: trace, callerDrain: null, lastDimensions: widths,
    performance: { now: () => timestamp++ } };
  vm.runInNewContext(block, context);
  const messages = messageQueue();
  const pump = new SourceDebtDrain({ hasDebt: api.HasWholeSourceStepDebt,
    drain: () => context.drainSourceDebt(), canRun: () => true,
    fatal: error => { throw error; }, trace, createChannel: () => messages.channel });
  context.callerDrain = pump;
  state.polls++; api.FrameObserved(.1, .7);
  assert(Math.abs(state.pending - .6) < 1e-12, 'RAF alone retains600ms when each100ms frame consumes its six-step budget.');
  api.CaptureObserved(.72);
  const beforeObserved = state.observed;
  pump.request(); pump.request();
  assert.equal(messages.queue.length, 1, 'Only one drain task may be outstanding.');
  let callbacks = 0;
  while (messages.queue.length) {
    const previous = state.hosts;
    messages.deliver(); callbacks++;
    assert(state.hosts - previous <= 6);
    assert.equal(state.observed, beforeObserved, 'Drain cannot add any elapsed, including an interleaved observed input timestamp.');
    assert(callbacks <= 6);
  }
  assert.equal(callbacks, 6);
  assert.equal(state.hosts, 42);
  assert(Math.abs(state.pending) < 1e-12, 'Only the actual fractional arithmetic residue remains; no whole host step is discarded.');
  assert.equal(state.captures, 1);
  assert.equal(state.polls, 1);
  assert.deepEqual(state.consumed, ['press', 'release']);
  assert.deepEqual(state.rng, Array.from({ length: 42 }, (_, i) => (i + 1) * 104729));
  assert(frameCalls.every(call => call[0] === 0 && call.slice(1).join() === '800,600,800,600,1'));
  state.polls++; api.FrameObserved(.1, .8);
  assert.equal(observedCalls.length, 2, 'Drains must never call FrameObserved.');
  assert(Math.abs(observedCalls.at(-1).elapsed - .1) < 1e-12);
  assert.equal(state.hosts, 48);
  assert.deepEqual(state.consumed, ['press', 'release', 'new-event'],
    'A future real input edge must not be delivered retroactively while draining older observed time.');
  pump.dispose();
  const moduleSource = await readFile(new URL('./wwwroot/input.js', import.meta.url), 'utf8');
  const needle = 'this.drain(); this.request();';
  assert.equal(moduleSource.split(needle).length, 2);
  const mutant = await import('data:text/javascript,' + encodeURIComponent(moduleSource.replace(needle, 'this.drain();')));
  const omitted = messageQueue(); let remaining = 36;
  const stoppedPump = new mutant.SourceDebtDrain({ hasDebt: () => remaining > 0,
    drain: () => { remaining -= 6; }, canRun: () => true, fatal: assert.fail,
    createChannel: () => omitted.channel });
  stoppedPump.request(); omitted.deliver();
  assert.equal(remaining, 30, 'Removing the real repost leaves retained source debt undrained.');
  assert.equal(omitted.queue.length, 0);
  stoppedPump.dispose();
});

test('pause focus restart and dispose invalidate stale drain tasks without clearing a newer generation', () => {
  const originals = new Map(['window', 'document', 'navigator'].map(name =>
    [name, Object.getOwnPropertyDescriptor(globalThis, name)]));
  let input, pump;
  try {
    const window = new EventTarget(), document = new EventTarget(), canvas = new EventTarget();
    let focused = true, steps = 0, restarts = 0;
    document.hidden = false; document.hasFocus = () => focused; canvas.focus = () => {};
    for (const [name, value] of Object.entries({ window, document, navigator: { getGamepads: () => [] } }))
      Object.defineProperty(globalThis, name, { value, configurable: true });
    const messages = messageQueue();
    pump = new SourceDebtDrain({ hasDebt: () => true, drain: () => { steps++; },
      canRun: () => !input.paused && focused, fatal: assert.fail, createChannel: () => messages.channel });
    input = new SourceInput({ Capture() {}, SetActive() {}, Restart() { restarts++; } },
      canvas, [0], assert.fail, () => {}, null, reason => pump.cancel(reason));
    const key = code => {
      const event = new Event('keydown', { cancelable: true });
      Object.defineProperties(event, { code: { value: code }, repeat: { value: false } });
      window.dispatchEvent(event);
    };
    pump.request(); key('Escape'); messages.deliver(); assert.equal(steps, 0);
    key('Escape'); pump.request(); focused = false; window.dispatchEvent(new Event('blur'));
    messages.deliver(); assert.equal(steps, 0);
    focused = true; window.dispatchEvent(new Event('focus'));
    pump.request(); key('KeyR'); pump.request();
    assert.equal(restarts, 1);
    const current = pump.pending;
    messages.deliver();
    assert.equal(pump.pending, current, 'Old restart task must not clear the new generation.');
    assert.equal(steps, 0);
    input.close(); pump.dispose(); messages.deliver();
    assert.equal(steps, 0); assert.equal(messages.channel.port1.closed, true);
    assert.equal(messages.channel.port2.closed, true);
  } finally {
    input?.close(); pump?.dispose();
    for (const [name, descriptor] of originals) {
      if (descriptor) Object.defineProperty(globalThis, name, descriptor);
      else delete globalThis[name];
    }
  }
});

test('caller trace timestamps fresh render packet spans and never upgrades a stale or absent packet to live state', () => {
  let time = 10;
  const trace = new SourceCallerTrace(() => time);
  trace.present(JSON.stringify({ epoch: 2, sourceTick: 649, hostTick: 684, phase: 'PreSnap',
    camera: { sourceTick: 0, suspended: true }, instructions: { phase: 'intro' } }), () => {
    time += 2; trace.stage('js-webgl-drawScene', () => { time += 20; });
  });
  const row = trace.snapshot().first16[0];
  assert.equal(row.beginMilliseconds, 10); assert.equal(row.endMilliseconds, 32);
  assert.equal(row.packet.sourceTick, 649); assert.equal(row.packet.cameraSourceTick, 0);
  assert.match(row.packet.provenance, /not a live getter/);
  assert.equal(trace.snapshot().first16[1].packet, row.packet);
  trace.stage('js-webgl-mesh', () => { time++; });
  assert.equal(trace.snapshot().last128.at(-1).packet, null);
  for (let i = 0; i < 160; i++) trace.measure('bounded', {}, () => { time++; });
  const snapshot = trace.snapshot();
  assert.equal(snapshot.first16.length, 16); assert.equal(snapshot.last128.length, 128);
  assert.equal(snapshot.dropped, snapshot.events - 144);
  assert.equal(snapshot.slowest16.length, 16);
  assert.equal(snapshot.slowest16[0].kind, 'js-webgl-present');
  const detached = JSON.parse(JSON.stringify(snapshot));
  detached.first16[0].packet.sourceTick = -1;
  assert.equal(trace.snapshot().first16[0].packet.sourceTick, 649);
});

test('drain failure preserves the original error and prevents rescheduling rather than hiding audio failures', () => {
  const messages = messageQueue(), failure = new Error('actual audio deadline failure'), observed = [];
  const original = globalThis.MessageChannel;
  let channels = 0, pump;
  try {
    globalThis.MessageChannel = class { constructor() { channels++; return messages.channel; } };
    pump = new SourceDebtDrain({ hasDebt: () => true, drain: () => { throw failure; },
      canRun: () => true, fatal: error => observed.push(error) });
  } finally { globalThis.MessageChannel = original; }
  assert.equal(channels, 1, 'Production defaults must use a real MessageChannel task source, not a Promise microtask loop.');
  pump.request(); messages.deliver();
  assert.deepEqual(observed, [failure]); assert.equal(messages.queue.length, 0);
  pump.request(); assert.equal(messages.queue.length, 0, 'Terminal failure cannot schedule a retry.');
  pump.dispose();
});
