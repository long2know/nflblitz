import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { spawn } from 'node:child_process';
import { mkdir, readFile, writeFile } from 'node:fs/promises';
import { createServer } from 'node:http';
import { dirname, extname, resolve, sep } from 'node:path';
import test from 'node:test';
import { SourceAudioReceiver } from './wwwroot/audio.js';
import { Cdp } from '../../tests/browser/lib.mjs';
import { sleep, value } from '../../tests/browser/chrome.mjs';
import { createChromeRuntime } from '../../tests/browser/runtime.mjs';

const RATE = 31250, TICKS = 57, POLICY = 'portable-source-qualified-pcm-one-shot/v1';
const PACKAGE = 'a'.repeat(64), BLOB = 'b'.repeat(40), BANK = 'synthetic-fixture';
const digest = bytes => createHash('sha256').update(bytes).digest('hex');
const project = tick => Math.floor(tick * RATE / TICKS);
const NativeEvent = globalThis.Event;

function fixture(count = 1440) {
  const bytes = new Uint8Array(count * 2), view = new DataView(bytes.buffer);
  for (let i = 0; i < count; i++) view.setInt16(i * 2, [-32768, 32767, -1, 0, 16384, -16384][i % 6], true);
  const hash = digest(bytes);
  return { bytes, id: 'pcm16-' + hash, count, metadata: JSON.stringify({
    schema: 'source-audio-sample/v1', format: 'pcm-s16le', sha256: hash, sampleCount: count,
    mixAlreadyBaked: true, admission: 'generated synthetic test only', nativeDspEquivalent: false,
  }) };
}
function command(kind, sequence, fields = {}) {
  return { sequence, epoch: 0, kind, sourceTick: 0, atSample: project(fields.sourceTick ?? 0),
    outputSequence: 0, subcallOrder: 0, voiceId: 0, track: -1, trackMask: 0, bank: null, playlistId: -1,
    packageSha256: null, sourceGitBlob: null, programHex: null, sampleId: null, sampleCount: 0,
    loop: false, rawVolume: 0, rawPan: 0, priorityWord: 0, volumeByte: 0, panByte: 0,
    portableGain: 0, portablePan: 0, active: false, reason: null, ...fields };
}
function batch(commands, fields = {}) {
  return JSON.stringify({ schema: 'source-audio-playback/v1', policy: POLICY, sampleRate: RATE,
    sourceTicksPerSecond: TICKS, epoch: commands[0]?.epoch ?? 0,
    sourceTick: Math.max(...commands.map(c => c.sourceTick)), commands, ...fields });
}
function play(sample, sequence = 4, fields = {}) {
  return command('play', sequence, { sourceTick: 1, outputSequence: 1, voiceId: 1, track: 0, bank: BANK,
    playlistId: 1, packageSha256: PACKAGE, sourceGitBlob: BLOB, programHex: '0101000007005a0000010000000101000500',
    sampleId: sample.id, sampleCount: sample.count, rawVolume: 160, rawPan: 127, priorityWord: 15,
    volumeByte: 160, panByte: 127, portableGain: 160 / 255, portablePan: 0, ...fields });
}
function load(sequence = 2, fields = {}) {
  return command('bank-load', sequence, { bank: BANK, packageSha256: PACKAGE, sourceGitBlob: BLOB, ...fields });
}
function stop(sequence, fields = {}) {
  return command('stop', sequence, { sourceTick: 2, track: 0, voiceId: 1, bank: BANK,
    playlistId: 1, reason: 'preempted', ...fields });
}

class Param {
  calls = [];
  setValueAtTime(value, time) { this.calls.push({ value, time }); this.onSet?.(value, time); }
  cancelScheduledValues(time) { this.calls.push({ cancelFrom: time }); }
}
class Node {
  connections = [];
  disconnected = false;
  connect(node) { this.connections.push(node); return node; }
  disconnect() { this.disconnected = true; this.connections.length = 0; }
}
class Source extends Node {
  starts = [];
  stops = [];
  start(...args) { this.starts.push(args); this.onStart?.(); }
  stop(...args) { this.stops.push(args); }
  end() { this.onended?.(); }
}
class FakeContext extends EventTarget {
  state = 'suspended';
  currentTime = 10;
  destination = new Node();
  buffers = [];
  sources = [];
  gains = [];
  pans = [];
  resumes = 0;
  suspends = 0;
  closes = 0;
  async resume() {
    this.resumes++;
    if (this.rejectResume) throw new Error('injected resume rejection');
    this.currentTime += this.resumeJump ?? 0;
    this.state = 'running';
  }
  async suspend() { this.suspends++; this.currentTime += this.suspendJump ?? 0; this.state = 'suspended'; }
  async close() { this.closes++; this.state = 'closed'; }
  createGain() {
    const node = Object.assign(new Node(), { gain: new Param() });
    if (!this.gains.length) node.gain.onSet = (value, time) => {
      if (value === 0 && time > this.currentTime && this.autoDrain !== false)
        queueMicrotask(() => this.advance(Math.max(0, time - this.currentTime)));
    };
    this.gains.push(node); return node;
  }
  createStereoPanner() {
    if (this.rejectPan) throw new Error('injected panner failure');
    const node = Object.assign(new Node(), { pan: new Param() }); this.pans.push(node); return node;
  }
  createBufferSource() {
    const node = new Source(); node.onStart = () => this.onStart?.(); this.sources.push(node); return node;
  }
  createBuffer(channels, count, rate) {
    const floats = new Float32Array(count);
    const buffer = { channels, length: count, sampleRate: rate, getChannelData: () => floats };
    this.buffers.push(buffer); return buffer;
  }
  advance(seconds) { if (this.state === 'running') this.currentTime += seconds; }
  externalState(state) { this.state = state; this.dispatchEvent(new NativeEvent('statechange')); }
}

function reservedBatch(commands, fields = {}) {
  return batch(commands.map(c => ({ loopStartSample: 0, loopEndSample: 0, mixPoints: [], ...c,
    reason: c.kind === 'active' && c.reason === null ? 'portable-browser-pause-focus-policy' : c.reason })), {
    schema: 'source-audio-playback/v4', policy: 'portable-original-pregame-continuous-source/v4', ...fields,
  });
}

async function timingReceipt(name, receipt) {
  if (!process.env.SOURCE_AUDIO_TIMING_EVIDENCE) return;
  const root = resolve(process.env.SOURCE_AUDIO_TIMING_EVIDENCE);
  await mkdir(root, { recursive: true });
  await writeFile(resolve(root, name + '.json'), JSON.stringify(receipt, null, 2) + '\n', { flag: 'wx' });
}

test('fake-node transport: actual positive-H eight normal cycles do not exhaust a global pause reserve', async () => {
  // Lead's normal Chrome trace, SHA256 170e1c82bd2ba3ab1c947be86515aed9b571a456ef8a012e7c5d2235d91c8a1c.
  const host = [.5249000000059604, 1.049500000029802, 1.5740000000298022, 2.0985000000298024,
    2.6236000000238424, 3.148200000017882, 3.6724000000059607, 4.196400000005961];
  const render = [.5973333333333333, 1.1946666666666668, 1.792, 2.389333333333333,
    2.9866666666666664, 3.5839999999999996, 4.181333333333333, 4.778666666666666];
  const jumps = [.01866666666666672, .08533333333333326, .08533333333333326,
    .016, .08533333333333326, .08533333333333326, .010666666666667268, .0853333333333337];
  await unit(async ({ receiver, context, sample, upload }) => {
    upload();
    context.baseLatency = 2048 / 48000; context.outputLatency = .128;
    let sequence = 0, tick = 0, voice = 1;
    const next = (kind, fields) => command(kind, ++sequence, { sourceTick: tick, ...fields });
    receiver.commands(reservedBatch([next('reset', { active: true, trackMask: 63 }), load(++sequence),
      next('clock-start', { reason: 'first-source-wake-or-output-after-ready-boundary' }),
      play(sample, ++sequence, { sourceTick: 0, loop: true, loopStartSample: 120, loopEndSample: sample.count })]));
    const timing = receiver.timing, observations = [];
    try {
      for (let i = 0; i < host.length; i++) {
        receiver.commands(reservedBatch([next('active', { active: false, reason: 'portable-browser-pause-focus-policy' })]));
        await receiver.settled();
        const frozen = context.currentTime;
        context.advance(.5);
        assert.equal(context.currentTime, frozen, 'The recorded500ms pause never advances logical source time.');
        context.resumeJump = jumps[i];
        receiver.commands(reservedBatch([next('active', { active: true, reason: 'portable-browser-pause-focus-policy' })]));
        await receiver.settled();
        context.advance(render[i] - (render[i - 1] ?? 0) - jumps[i]);
        tick = Math.floor(host[i] * TICKS);
        const commands = [];
        if (voice > 1) commands.push(stop(++sequence, { sourceTick: tick, voiceId: voice, track: 1, playlistId: 2 }));
        commands.push(play(sample, ++sequence, { sourceTick: tick, voiceId: ++voice,
          outputSequence: voice, track: 1, playlistId: 2 }));
        commands.push(next('program-mix', { track: 0, voiceId: 1, bank: BANK, playlistId: 1,
          reason: 'original-dcs233-global0-mixer-envelope',
          mixPoints: [{ atSample: project(tick), gain: .2 + i / 100 },
            { atSample: project(tick) + 600 * 240, gain: 1 }] }));
        receiver.commands(reservedBatch(commands));
        observations.push({ cycle: i + 1, activeH: host[i], recordedC: render[i],
          tick, anchor: receiver.anchor, headroom: receiver.anchor + project(tick) / RATE - context.currentTime });
        assert(observations.at(-1).headroom > 0);
        assert.equal(receiver.model.epoch, 0); assert.equal(receiver.model.sourceOriginTick, 0);
        assert.equal(receiver.timing, timing, 'No larger lead is selected or hidden pause credit renewed.');
      }
      assert.equal(receiver.started, 9, 'Physical continuations are not duplicate logical source plays.');
      await timingReceipt('positive-host-eight-normal-cycles', { observations, timing, status: 'pass' });
    } catch (error) {
      await timingReceipt('positive-host-eight-normal-cycles', { observations, timing, status: 'red',
        fault: receiver.snapshot().faultingCommand, error: error.message });
      throw error;
    }
  }, { initialize: false, count: RATE * 3 });
});

test('fake-node transport:96 varied pauses fence exact PCM phase, loop intro/offset, future gain and source order', async () => {
  await unit(async ({ receiver, context, sample, upload }) => {
    upload();
    context.baseLatency = 2048 / 48000; context.outputLatency = .128;
    context.getOutputTimestamp = () => ({ contextTime: 0, performanceTime: 0 });
    let sequence = 0, tick = 0, host = 0, voice = 1;
    const next = (kind, fields) => command(kind, ++sequence, { sourceTick: tick, ...fields });
    receiver.commands(reservedBatch([
      next('reset', { active: true, trackMask: 63 }), load(++sequence),
      next('clock-start', { reason: 'first-source-wake-or-output-after-ready-boundary' }),
      play(sample, ++sequence, { sourceTick: 0, loop: true, loopStartSample: 480, loopEndSample: sample.count,
        rawVolume: 255, volumeByte: 255, portableGain: 1 }),
    ]));
    const timing = receiver.timing, buffer = receiver.nodes.get(1).buffer, observations = [];
    for (let i = 0; i < 96; i++) {
      const activeH = [.31, .5, .89, 1.3][i % 4];
      context.advance(activeH + [.07, .03, -.005][i % 3]);
      host += activeH; tick = Math.floor(host * TICKS);
      const sampleAtPause = project(tick);
      const commands = [];
      if (voice > 1) commands.push(stop(++sequence, { sourceTick: tick, voiceId: voice, track: 1, playlistId: 2 }));
      commands.push(play(sample, ++sequence, { sourceTick: tick, voiceId: ++voice,
        outputSequence: voice, track: 1, playlistId: 2 }));
      const points = [{ atSample: sampleAtPause, gain: .25 },
        { atSample: sampleAtPause + 240, gain: .5 }, { atSample: sampleAtPause + 1400 * 240, gain: 1 }];
      commands.push(next('program-mix', { track: 0, voiceId: 1, bank: BANK, playlistId: 1,
        reason: 'original-dcs233-global0-mixer-envelope', mixPoints: points }));
      context.onStart = () => context.advance(2048 / 48000);
      receiver.commands(reservedBatch(commands));
      context.onStart = null;
      const old = receiver.nodes.get(1).source, staleEnded = old.onended;
      const oldAnchor = receiver.anchor, fence = oldAnchor + sampleAtPause / RATE;
      context.suspendJump = i % 2 ? .043 : 0;
      receiver.commands(reservedBatch([next('active', { active: false })]));
      assert.equal(receiver.frameReady, false);
      await receiver.settled();
      assert.deepEqual(old.stops.at(-1), [fence], 'No media frame at/after the declared source fence may escape.');
      assert.equal(old.disconnected, true);
      assert.equal(receiver.sourceElapsed(0), sampleAtPause / RATE);
      assert.equal(receiver.snapshot().clock.logicalPlaybackSample, sampleAtPause);
      const suspendedC = context.currentTime;
      context.advance([.01, .5, 5][i % 3]);
      assert.equal(context.currentTime, suspendedC);
      context.resumeJump = [0, .016, .08533333333333333, .128][i % 4];
      context.outputLatency = 0;
      receiver.commands(reservedBatch([next('active', { active: true })]));
      await receiver.settled();
      const music = receiver.nodes.get(1);
      const offset = sampleAtPause < sample.count ? sampleAtPause :
        480 + (sampleAtPause - 480) % (sample.count - 480);
      assert.equal(music.buffer, buffer, 'Immutable PCM survives every native node continuation without another upload.');
      assert.deepEqual(music.source.starts, [[receiver.anchor + sampleAtPause / RATE, offset / RATE]]);
      assert.equal(music.offsetSample, offset);
      assert.equal(music.source.buffer.getChannelData(0)[offset], buffer.getChannelData(0)[offset]);
      assert.deepEqual(music.gain.gain.calls, points.map(p => ({ value: p.gain, time: receiver.anchor + p.atSample / RATE })));
      assert.equal(receiver.snapshot().clock.logicalPlaybackSample, sampleAtPause);
      staleEnded();
      assert.equal(receiver.nodes.get(1), music, 'An old native segment callback cannot clear its continuation.');
      assert.equal(receiver.tracks[0], music);
      assert.equal(receiver.timing, timing);
      assert.equal(receiver.model.epoch, 0); assert.equal(receiver.model.sourceOriginTick, 0);
      assert.equal(receiver.model.sourceTick, tick); assert.equal(receiver.model.sequence, sequence);
      assert.equal(receiver.started, i + 2);
      assert.deepEqual(receiver.snapshot().clock.outputTimestamp, { contextTime: 0, performanceTime: 0 });
      observations.push({ cycle: i + 1, host, tick, fenceSample: sampleAtPause, offset, oldAnchor,
        anchor: receiver.anchor, segment: receiver.transport.segmentId, phase: receiver.sourceElapsed(0) });
    }
    await timingReceipt('varied-96-phase-and-future', { timing, observations, sourceDrivenByC: false });
  }, { initialize: false, count: RATE * 2 });
});

test('fake-node timing: changed play and envelope stay strict, including C advancing after play and during node construction', async () => {
  for (const mode of ['play', 'mix', 'between-commands', 'during-construction']) {
    await unit(async ({ receiver, context, sample, upload }) => {
      upload(); context.baseLatency = 2048 / 48000; context.outputLatency = .128;
      receiver.commands(reservedBatch([command('reset', 1, { active: true, trackMask: 63 }), load(2),
        command('clock-start', 3, { reason: 'first-source-wake-or-output-after-ready-boundary' }),
        play(sample, 4, { rawVolume: 255, volumeByte: 255, portableGain: 1 })]));
      const tick = 2, when = receiver.anchor + project(tick) / RATE;
      const changed = command('program-mix', 6, { sourceTick: tick, voiceId: 1, track: 0, bank: BANK, playlistId: 1,
        reason: 'original-dcs233-global0-mixer-envelope',
        mixPoints: [{ atSample: project(tick), gain: .4 }, { atSample: project(tick) + 240, gain: 1 }] });
      const newPlay = play(sample, 5, { sourceTick: tick, voiceId: 2, outputSequence: 2, track: 1, playlistId: 2 });
      context.currentTime = when + (['play', 'mix'].includes(mode) ? .00005 : -.00005);
      if (mode === 'between-commands') context.onStart = () => { context.currentTime = when + .00005; };
      if (mode === 'during-construction') {
        const create = context.createStereoPanner.bind(context);
        context.createStereoPanner = () => { context.currentTime = when + .00005; return create(); };
      }
      const commands = mode === 'mix' ? [changed] : mode === 'between-commands' ? [newPlay, changed] : [newPlay];
      assert.throws(() => receiver.commands(reservedBatch(commands)), /sample-clock underrun/);
      const fault = receiver.snapshot().faultingCommand;
      assert.equal(fault.command.kind, ['mix', 'between-commands'].includes(mode) ? 'program-mix' : 'play');
      assert.equal(fault.sourceSample, project(tick)); assert.equal(fault.epoch, 0);
      assert.equal(fault.batchIndex, mode === 'between-commands' ? 1 : 0);
      assert.equal(fault.semanticEnvelopeChange, true);
      assert(fault.deadlineContextTime > fault.scheduledTime);
      assert.equal(fault.initialLeadSeconds, receiver.timing.initialLeadSeconds);
      if (fault.command.kind === 'program-mix') assert.deepEqual(fault.previousEnvelope, [{ atSample: project(1), gain: 1 }]);
      assert.equal(receiver.nodes.size, 0, 'Late changed commands fail closed; no queue, drop, retry or replay.');
      await timingReceipt('strict-' + mode, fault);
    }, { initialize: false });
  }
});

test('fake-node timing: active-segment lead still covers45-69.405ms delivery/source phase, never unlimited active lateness', async () => {
  for (const age of [.045, .069405]) await unit(async ({ receiver, context, sample, upload }) => {
    upload(); context.baseLatency = 2048 / 48000; context.outputLatency = .128;
    receiver.commands(reservedBatch([command('reset', 1, { active: true, trackMask: 63 }), load(2),
      command('clock-start', 3, { reason: 'first-source-wake-or-output-after-ready-boundary' }),
      play(sample, 4, { loop: true, loopEndSample: sample.count, rawVolume: 255, volumeByte: 255, portableGain: 1 })]));
    const phase = Math.ceil(RATE / TICKS) / RATE, tick = 3;
    context.currentTime = receiver.clockStartContextTime + project(tick) / RATE +
      receiver.timing.renderReserveSeconds - 2048 / 48000 + age + phase;
    context.onStart = () => context.advance(2048 / 48000);
    receiver.commands(reservedBatch([
      play(sample, 5, { sourceTick: tick, voiceId: 2, outputSequence: 2, track: 1, playlistId: 2 }),
      command('program-mix', 6, { sourceTick: tick, voiceId: 1, track: 0, bank: BANK, playlistId: 1,
        reason: 'original-dcs233-global0-mixer-envelope', mixPoints: [{ atSample: project(tick), gain: .4 }] }),
    ]));
    const remaining = receiver.anchor + project(tick) / RATE - context.currentTime;
    assert(Math.abs(remaining - (.1 - age - phase)) < 1e-12 && remaining > 0,
      'The render budget includes the final batch; delivery allowance separately includes the full exact-sample source phase.');
  }, { initialize: false, count: 30000 });
  await unit(async ({ receiver, context, sample, upload }) => {
    upload(); context.baseLatency = 2048 / 48000; context.outputLatency = .128;
    receiver.commands(reservedBatch([command('reset', 1, { active: true, trackMask: 63 }), load(2),
      command('clock-start', 3, { reason: 'first-source-wake-or-output-after-ready-boundary' }),
      play(sample, 4, { loop: true, loopEndSample: sample.count })]));
    const anchor = receiver.anchor;
    context.currentTime = anchor + project(1) / RATE + .001;
    assert.throws(() => receiver.commands(reservedBatch([
      play(sample, 5, { sourceTick: 1, outputSequence: 2, voiceId: 2, track: 1, playlistId: 2 }),
    ])), /sample-clock underrun/);
    assert.equal(receiver.anchor, anchor, 'Active-event lateness cannot create a new render segment.');
    assert.equal(receiver.snapshot().faultingCommand.semanticEnvelopeChange, true);
    await timingReceipt('active-late-no-reanchor', receiver.snapshot().faultingCommand);
  }, { initialize: false, count: 30000 });
});

test('fake-node timing: changed stop/adjust controls and even an ended-voice late envelope cannot be silently clamped', async () => {
  for (const kind of ['stop', 'adjust', 'program-mix']) await unit(async ({ receiver, context, sample, upload }) => {
    upload();
    receiver.commands(reservedBatch([command('reset', 1, { active: true, trackMask: 63 }), load(2),
      command('clock-start', 3, { reason: 'first-source-wake-or-output-after-ready-boundary' }), play(sample, 4)]));
    const tick = 2;
    const fields = kind === 'stop' ? { bank: BANK, playlistId: 1, reason: 'source-stop-track' } :
      kind === 'adjust' ? { volumeByte: 0, panByte: 127, portableGain: 0, portablePan: 0 } :
        { bank: BANK, playlistId: 1, reason: 'original-dcs233-global0-mixer-envelope',
          mixPoints: [{ atSample: project(tick), gain: .4 }] };
    if (kind === 'program-mix') context.sources[0].end();
    context.currentTime = receiver.anchor + project(tick) / RATE + .00005;
    assert.throws(() => receiver.commands(reservedBatch([command(kind, 5, {
      sourceTick: tick, track: 0, voiceId: 1, ...fields,
    })])), /sample-clock underrun/);
    await timingReceipt('strict-control-' + kind, receiver.snapshot().faultingCommand);
  }, { initialize: false });
});

test('fake-node timing: reserve floors survive missing estimates, and policy/replay/ready barriers remain enforced', async () => {
  await unit(async ({ receiver, context, sample, upload }) => {
    upload(); context.baseLatency = NaN; context.outputLatency = 0;
    receiver.commands(reservedBatch([command('reset', 1, { active: true, trackMask: 63 }), load(2)]));
    context.advance(20);
    assert.equal(receiver.sourceElapsed(0), -1);
    assert.equal(receiver.snapshot().clock.logicalPlaybackSample, 0, 'Unbound resource warmup is not media playback.');
    receiver.commands(reservedBatch([command('clock-start', 3, { reason: 'first-source-wake-or-output-after-ready-boundary' })]));
    assert.equal(receiver.timing.baseLatencyEstimateSeconds, null);
    assert.equal(receiver.timing.outputLatencyEstimateSeconds, 0);
    assert(Math.abs(receiver.timing.initialLeadSeconds - (.1 + 2 * (.128 + 2048 / 48000))) < 1e-12);
    const json = reservedBatch([play(sample, 4)]);
    receiver.commands(json);
    assert.throws(() => receiver.commands(json), /sequence|duplicated or reordered/);
    context.baseLatency = .08; context.outputLatency = .2;
    receiver.commands(reservedBatch([command('reset', 5, { epoch: 1, active: true, trackMask: 63 }),
      command('clock-start', 6, { epoch: 1, reason: 'first-source-wake-or-output-after-ready-boundary' })]));
    assert(Math.abs(receiver.timing.initialLeadSeconds - .66) < 1e-12, 'Larger device estimates increase, never shrink, the qualified initial reserve.');
  }, { initialize: false });
  await unit(async ({ receiver, context, sample, upload }) => {
    upload();
    assert.throws(() => receiver.commands(reservedBatch([command('reset', 1, { active: true, trackMask: 63 }),
      load(2), play(sample, 3)])), /ready clock boundary/);
    assert.equal(context.sources.length, 0, 'v4 cannot silently start from a provisional warmup anchor.');
  }, { initialize: false });
  await unit(async ({ receiver, context, sample, upload }) => {
    upload();
    receiver.commands(reservedBatch([command('reset', 1, { active: true, trackMask: 63 }), load(2),
      command('clock-start', 3, { reason: 'first-source-wake-or-output-after-ready-boundary' })]));
    const old = command('reserved', 4, { trackMask: 35, loopStartSample: 0, loopEndSample: 0, mixPoints: [] });
    assert.throws(() => receiver.commands(batch([old], {
      schema: 'source-audio-playback/v3', policy: 'portable-original-pregame-continuous-source/v3',
    })), /clock changed or rewound/);
    assert.equal(context.sources.length, 0, 'Changing the scheduling policy inside an epoch is not a reanchor escape.');
  }, { initialize: false });
  await unit(async ({ receiver, context, sample, upload }) => {
    upload();
    receiver.commands(reservedBatch([command('reset', 1, { active: true, trackMask: 63 }), load(2),
      command('clock-start', 3, { reason: 'first-source-wake-or-output-after-ready-boundary' }),
      command('active', 4, { active: false })]));
    await receiver.settled();
    context.resumeJump = .128;
    receiver.commands(reservedBatch([command('active', 5, { active: true })]));
    receiver.commands(reservedBatch([play(sample, 6)]));
    assert.equal(context.sources.length, 0, 'Only the declared lifecycle transition owns a bounded ordered FIFO.');
    await receiver.settled();
    assert.equal(context.sources.length, 1);
    assert.equal(receiver.model.sequence, 6);
    assert.equal(context.sources[0].starts[0][0], receiver.anchor + project(1) / RATE);
  }, { initialize: false });
});

test('fake-node transport: pause/resume FIFO preserves pending play, retiring/preempted ownership, volume0 and the entire future tail', async () => {
  await unit(async ({ receiver, context, sample, upload }) => {
    upload();
    receiver.commands(reservedBatch([command('reset', 1, { active: true, trackMask: 63 }), load(2),
      command('clock-start', 3, { reason: 'first-source-wake-or-output-after-ready-boundary' }),
      play(sample, 4, { sourceTick: 0, loop: true, loopStartSample: 480, loopEndSample: sample.count })]));
    const buffer = receiver.nodes.get(1).buffer, old = context.sources[0], staleEnded = old.onended;
    context.autoDrain = false;
    receiver.commands(reservedBatch([command('active', 5, { sourceTick: 2, active: false })]));
    const fence = receiver.transport.boundaryTime;
    staleEnded();
    assert.equal(receiver.nodes.get(1).source, old, 'An already queued native callback no longer owns a fenced logical voice.');
    const tail = project(3) + 1000 * 240;
    receiver.commands(reservedBatch([
      command('active', 6, { sourceTick: 2, active: true }),
      play(sample, 7, { sourceTick: 3, voiceId: 2, outputSequence: 2, track: 1, playlistId: 2 }),
      command('adjust', 8, { sourceTick: 3, voiceId: 2, track: 1, volumeByte: 0,
        panByte: 255, portableGain: 0, portablePan: 1 }),
      command('program-mix', 9, { sourceTick: 3, voiceId: 1, track: 0, bank: BANK, playlistId: 1,
        reason: 'original-dcs233-global0-mixer-envelope',
        mixPoints: [{ atSample: project(3), gain: .2 }, { atSample: tail, gain: 1 }] }),
      stop(10, { sourceTick: 4, voiceId: 2, track: 1, playlistId: 2 }),
      play(sample, 11, { sourceTick: 4, voiceId: 3, outputSequence: 3, track: 1, playlistId: 2 }),
      command('bank-delete', 12, { sourceTick: 4, bank: BANK }),
    ]));
    assert.equal(context.sources.length, 1, 'Commands behind a real pending fence cannot leak into its old physical mapping.');
    assert.equal(receiver.deferred.length, 7);
    context.advance(fence - context.currentTime);
    context.resumeJump = .128;
    await receiver.settled();
    assert.equal(receiver.model.sequence, 12); assert.equal(receiver.model.sourceTick, 4);
    assert.equal(receiver.deferred.length, 0);
    assert.equal(receiver.started, 3);
    assert.deepEqual(receiver.snapshot().tracks, [1, 3, null, null, null, null]);
    assert.equal(context.sources.length, 4, 'One continuation plus two genuine, exactly ordered source plays.');
    const retiring = receiver.nodes.get(2), replacement = receiver.nodes.get(3), music = receiver.nodes.get(1);
    assert.equal(retiring.stopSample, project(4));
    assert.deepEqual(retiring.source.stops, [[receiver.anchor + project(4) / RATE]]);
    assert.deepEqual(retiring.gain.gain.calls.at(-1), { value: 0, time: receiver.anchor + project(3) / RATE });
    assert.equal(replacement.source.starts[0][0], receiver.anchor + project(4) / RATE);
    staleEnded();
    assert.equal(receiver.nodes.get(1), music);
    assert.equal(receiver.samples.size, 0, 'Bank eviction releases cache ownership, not the live voice PCM.');
    assert.equal(music.buffer, buffer);
    const retiringEnded = retiring.source.onended;
    context.autoDrain = true;
    receiver.commands(reservedBatch([command('active', 13, { sourceTick: 5, active: false })]));
    await receiver.settled();
    assert.equal(receiver.nodes.has(2), false, 'The old preempted voice finishes at its original logical stop, before the pause fence.');
    receiver.commands(reservedBatch([command('adjust', 14, { sourceTick: 5, voiceId: 1, track: 0,
      volumeByte: 0, panByte: 255, portableGain: 0, portablePan: 1 })]));
    receiver.commands(reservedBatch([command('active', 15, { sourceTick: 5, active: true })]));
    await receiver.settled();
    retiringEnded();
    assert.equal(receiver.nodes.get(3), replacement, 'A stale preemption completion cannot clear the resumed replacement.');
    assert.equal(music.source.buffer, buffer, 'Reconstruction uses the retained immutable buffer, not an evicted cache entry.');
    assert.deepEqual(music.gain.gain.calls, [
      { value: 0, time: receiver.anchor + project(5) / RATE }, { value: 1, time: receiver.anchor + tail / RATE },
    ], 'Adjust(volume0) changes current gain without canceling a later legitimate restore.');
    assert.deepEqual(music.pan.pan.calls, [{ value: 1, time: receiver.anchor + project(5) / RATE }]);
    const changed = [{ atSample: project(6), gain: .3 }, { atSample: tail + 240, gain: .7 }];
    receiver.commands(reservedBatch([command('program-mix', 16, { sourceTick: 6, voiceId: 1, track: 0,
      bank: BANK, playlistId: 1, reason: 'original-dcs233-global0-mixer-envelope', mixPoints: changed })]));
    assert.deepEqual(music.gain.gain.calls.slice(-3), [
      { cancelFrom: receiver.anchor + project(6) / RATE },
      ...changed.map(p => ({ value: p.gain, time: receiver.anchor + p.atSample / RATE })),
    ], 'Only a genuine changed envelope cancels its own old future, never another voice or pan.');
    await timingReceipt('ordered-transition-voices', { snapshot: receiver.snapshot(), tail,
      musicGain: music.gain.gain.calls, replacementStart: replacement.source.starts });
  }, { initialize: false, count: RATE * 2 });
});

test('fake-node transport: queued bank delete/reload and shared PCM retain admitted pending-buffer/cache ownership', async () => {
  await unit(async ({ receiver, context, sample, upload }) => {
    upload(); context.autoDrain = false;
    receiver.commands(reservedBatch([command('reset', 1, { active: true, trackMask: 63 }), load(2),
      command('clock-start', 3, { reason: 'first-source-wake-or-output-after-ready-boundary' }),
      play(sample, 4, { sourceTick: 0, loop: true, loopEndSample: sample.count })]));
    const buffer = receiver.nodes.get(1).buffer;
    receiver.commands(reservedBatch([command('active', 5, { sourceTick: 1, active: false })]));
    receiver.commands(reservedBatch([command('bank-delete', 6, { sourceTick: 1, bank: BANK })]));
    upload();
    receiver.commands(reservedBatch([load(7, { sourceTick: 1 }),
      command('active', 8, { sourceTick: 1, active: true }),
      play(sample, 9, { sourceTick: 2, voiceId: 2, track: 1, playlistId: 2, outputSequence: 2 })]));
    context.advance(receiver.transport.boundaryTime - context.currentTime);
    await receiver.settled();
    assert.equal(receiver.nodes.get(1).source.buffer, buffer);
    assert.equal(receiver.nodes.get(2).source.buffer, buffer);
    assert.equal(receiver.samples.get(sample.id).buffer, buffer,
      'An already accepted immutable reload must not be erased when an older queued bank delete reaches cache cleanup.');
    receiver.commands(reservedBatch([play(sample, 10, { sourceTick: 3, voiceId: 3, track: 2, playlistId: 2, outputSequence: 3 })]));
    assert.equal(receiver.nodes.get(3).source.buffer, buffer);
    assert.equal(context.buffers.length, 1);
  }, { initialize: false, count: RATE * 2 });
  await unit(async ({ receiver, context, sample, upload }) => {
    upload();
    const other = BANK + '-other';
    receiver.commands(reservedBatch([command('reset', 1, { active: true, trackMask: 63 }), load(2), load(3, { bank: other }),
      command('clock-start', 4, { reason: 'first-source-wake-or-output-after-ready-boundary' }),
      play(sample, 5, { sourceTick: 0 }),
      play(sample, 6, { sourceTick: 0, voiceId: 2, outputSequence: 2, bank: other, track: 1 }),
      command('bank-delete', 7, { sourceTick: 1, bank: BANK })]));
    assert.equal(receiver.samples.size, 1, 'Continuous programme keys include a primary-context suffix, not part of the bank name.');
    receiver.commands(reservedBatch([command('active', 8, { sourceTick: 1, active: false })]));
    await receiver.settled();
    receiver.commands(reservedBatch([command('active', 9, { sourceTick: 1, active: true })]));
    await receiver.settled();
    receiver.commands(reservedBatch([play(sample, 10, { sourceTick: 2, voiceId: 3, outputSequence: 3, bank: other, track: 2 })]));
    assert.equal(receiver.nodes.get(3).source.buffer, context.buffers[0]);
    assert.equal(context.buffers.length, 1);
  }, { initialize: false, count: RATE * 2 });
});

test('fake-node transport: a C# completion acknowledgment cannot truncate the resumed nonlooping PCM tail or replay it later', async () => {
  await unit(async ({ receiver, context, sample, upload }) => {
    upload();
    receiver.commands(reservedBatch([command('reset', 1, { active: true, trackMask: 63 }), load(2),
      command('clock-start', 3, { reason: 'first-source-wake-or-output-after-ready-boundary' }),
      play(sample, 4, { sourceTick: 0 })]));
    receiver.commands(reservedBatch([command('active', 5, { sourceTick: 1, active: false })]));
    await receiver.settled();
    receiver.commands(reservedBatch([command('active', 6, { sourceTick: 1, active: true })]));
    await receiver.settled();
    const continuation = receiver.nodes.get(1), source = continuation.source;
    assert.equal(continuation.offsetSample, project(1));
    const end = receiver.anchor + sample.count / RATE;
    receiver.commands(reservedBatch([stop(7, { sourceTick: 3, atSample: sample.count, reason: 'completed' })]));
    assert(end > context.currentTime, 'Producer completion precedes physical playback by the scheduling lead.');
    assert.deepEqual(source.stops, [[end]]);
    assert.equal(receiver.nodes.size, 1);
    receiver.commands(reservedBatch([command('active', 8, { sourceTick: 3, active: false })]));
    await receiver.settled();
    assert.deepEqual(source.stops, [[end]], 'A later pause cannot extend the already scheduled decoder tail.');
    assert.equal(receiver.nodes.size, 0);
    receiver.commands(reservedBatch([command('active', 9, { sourceTick: 3, active: true })]));
    await receiver.settled();
    assert.equal(context.sources.length, 2, 'A naturally completed programme must never be reconstructed.');
    assert.equal(receiver.started, 1); assert.equal(receiver.ended, 1);
    assert.equal(receiver.samples.size, 1);
  }, { initialize: false });
});

test('fake-node transport: all six tracks retain future steps, pending starts and exact loop phases without duplicate logical music', async () => {
  await unit(async ({ receiver, context, sample, upload }) => {
    upload();
    let sequence = 3;
    receiver.commands(reservedBatch([command('reset', 1, { active: true, trackMask: 63 }), load(2),
      command('clock-start', 3, { reason: 'first-source-wake-or-output-after-ready-boundary' }),
      ...Array.from({ length: 6 }, (_, track) => play(sample, ++sequence, { sourceTick: 0, track,
        voiceId: track + 1, outputSequence: track + 1, loop: true, loopStartSample: 120, loopEndSample: sample.count })),
      command('reserved', ++sequence, { sourceTick: 0, trackMask: 35 }),
      command('program-mix', ++sequence, { sourceTick: 1, track: 0, voiceId: 1, bank: BANK, playlistId: 1,
        reason: 'original-dcs233-global0-mixer-envelope', mixPoints: [
          { atSample: project(1), gain: 0 }, { atSample: project(1) + 240 * 30, gain: .2 },
          { atSample: project(1) + 240 * 1000, gain: 1 },
        ] }),
    ]));
    const originals = [...receiver.nodes.values()].map(v => ({ voice: v, source: v.source, ended: v.source.onended }));
    receiver.commands(reservedBatch([command('active', ++sequence, { sourceTick: 2, active: false })]));
    await receiver.settled();
    receiver.commands(reservedBatch([command('active', ++sequence, { sourceTick: 2, active: true })]));
    await receiver.settled();
    for (const { voice, source, ended } of originals) {
      assert.equal(source.disconnected, true);
      assert.deepEqual(voice.source.starts, [[receiver.anchor + project(2) / RATE, project(2) / RATE]]);
      ended();
      assert.equal(receiver.tracks[voice.track], voice);
    }
    const music = receiver.nodes.get(1);
    assert.deepEqual(music.gain.gain.calls, [
      { value: 0, time: receiver.anchor + project(2) / RATE },
      { value: .2, time: receiver.anchor + (project(1) + 30 * 240) / RATE },
      { value: 1, time: receiver.anchor + (project(1) + 1000 * 240) / RATE },
    ]);
    assert.equal(receiver.nodes.size, 6); assert.equal(receiver.started, 6);
    receiver.commands(reservedBatch([command('stop-all', ++sequence, { sourceTick: 3, trackMask: 63 })]));
    receiver.commands(reservedBatch([command('active', ++sequence, { sourceTick: 3, active: false })]));
    await receiver.settled();
    receiver.commands(reservedBatch([command('active', ++sequence, { sourceTick: 3, active: true })]));
    await receiver.settled();
    assert.equal(receiver.nodes.size, 0);
    assert.equal(receiver.model.reserved, 0);
    assert.equal(context.sources.length, 12, 'Stopped music/duck restores cannot leak into an empty new segment.');
  }, { initialize: false, count: RATE * 2 });
});

test('fake-node transport: queued reset, stale callbacks, fault recovery and disposal release exactly their owned segment', async () => {
  await unit(async ({ receiver, context, sample, upload }) => {
    upload(); context.autoDrain = false;
    receiver.commands(reservedBatch([command('reset', 1, { active: true, trackMask: 63 }), load(2),
      command('clock-start', 3, { reason: 'first-source-wake-or-output-after-ready-boundary' }),
      play(sample, 4, { sourceTick: 0, loop: true, loopEndSample: sample.count })]));
    const staleEnded = context.sources[0].onended;
    receiver.commands(reservedBatch([command('active', 5, { sourceTick: 1, active: false })]));
    const fence = receiver.transport.boundaryTime;
    receiver.commands(reservedBatch([command('reset', 6, { epoch: 1, active: false, trackMask: 63 }), load(7, { epoch: 1 })]));
    context.advance(fence - context.currentTime);
    await receiver.settled();
    assert.equal(receiver.nodes.size, 0); assert.equal(receiver.transport.phase, 'unbound');
    assert.equal(receiver.model.epoch, 1);
    assert.equal(receiver.commands(reservedBatch([play(sample, 4)])), false);
    receiver.commands(reservedBatch([command('active', 8, { epoch: 1, active: true })]));
    await receiver.settled();
    receiver.commands(reservedBatch([
      command('clock-start', 9, { epoch: 1, reason: 'first-source-wake-or-output-after-ready-boundary' }),
      play(sample, 10, { epoch: 1, voiceId: 2, loop: true, loopEndSample: sample.count }),
    ]));
    staleEnded();
    assert.equal(receiver.tracks[0].id, 2);
    assert.equal(context.buffers.length, 1);
    receiver.commands(reservedBatch([command('active', 11, { epoch: 1, sourceTick: 2, active: false })]));
    await receiver.close();
    assert.equal(receiver.pending, 0); assert.equal(receiver.deferred.length, 0);
    assert.equal(receiver.nodes.size, 0); assert.equal(receiver.samples.size, 0);
    assert.equal(context.state, 'closed'); assert.equal(context.closes, 1);
  }, { initialize: false });
  await unit(async ({ receiver, context, sample, upload }) => {
    upload();
    receiver.commands(reservedBatch([command('reset', 1, { active: true, trackMask: 63 }), load(2),
      command('clock-start', 3, { reason: 'first-source-wake-or-output-after-ready-boundary' }), play(sample, 4)]));
    receiver.commands(reservedBatch([command('active', 5, { sourceTick: 2, active: false })]));
    await receiver.settled();
    context.rejectPan = true;
    receiver.commands(reservedBatch([command('active', 6, { sourceTick: 2, active: true })]));
    await assert.rejects(receiver.settled(), /panner failure/);
    assert.equal(receiver.snapshot().faultingCommand.command.sequence, 6);
    assert.equal(receiver.nodes.size, 0);
    assert(context.sources.every(s => s.disconnected));
    context.rejectPan = false;
    receiver.commands(reservedBatch([command('reset', 7, { epoch: 1, active: true, trackMask: 63 }), load(8, { epoch: 1 }),
      command('clock-start', 9, { epoch: 1, reason: 'first-source-wake-or-output-after-ready-boundary' }),
      play(sample, 10, { epoch: 1, voiceId: 2 })]));
    assert.equal(receiver.nodes.size, 1); assert.equal(context.buffers.length, 1);
    assert.equal(receiver.failure, null); assert.equal(receiver.model.epoch, 1);
  }, { initialize: false });
});

test('fake-node transport: late pause/construction, forged boundaries and source progress while paused remain explicit errors', async () => {
  for (const mode of ['late-pause', 'late-continuation', 'forged', 'paused-source']) await unit(async ({ receiver, context, sample, upload }) => {
    upload();
    receiver.commands(reservedBatch([command('reset', 1, { active: true, trackMask: 63 }), load(2),
      command('clock-start', 3, { reason: 'first-source-wake-or-output-after-ready-boundary' }), play(sample, 4)]));
    const anchor = receiver.anchor;
    if (mode === 'late-pause') {
      context.currentTime = anchor + project(2) / RATE + .00005;
      assert.throws(() => receiver.commands(reservedBatch([command('active', 5, { sourceTick: 2, active: false })])), /sample-clock underrun/);
      assert.equal(receiver.anchor, anchor);
      receiver.commands(reservedBatch([command('reset', 6, { epoch: 1, active: true, trackMask: 63 }), load(7, { epoch: 1 }),
        command('clock-start', 8, { epoch: 1, reason: 'first-source-wake-or-output-after-ready-boundary' }),
        play(sample, 9, { epoch: 1, voiceId: 2 })]));
      assert.equal(receiver.failure, null, 'A failed pause guard cannot poison the still-running context ownership for an explicit new epoch.');
    } else if (mode === 'forged') {
      assert.throws(() => receiver.commands(reservedBatch([command('active', 5, {
        sourceTick: 2, active: false, reason: 'active-event-retry',
      })])), /public active transition/);
    } else {
      receiver.commands(reservedBatch([command('active', 5, { sourceTick: 2, active: false })]));
      await receiver.settled();
      if (mode === 'paused-source') {
        assert.throws(() => receiver.commands(reservedBatch([command('adjust', 6, {
          sourceTick: 3, voiceId: 1, track: 0, volumeByte: 0, panByte: 127, portableGain: 0, portablePan: 0,
        })])), /paused transport cannot advance/);
      } else {
        context.resumeJump = 1.5;
        context.onStart = () => context.advance(.5);
        receiver.commands(reservedBatch([command('active', 6, { sourceTick: 2, active: true })]));
        await assert.rejects(receiver.settled(), /sample-clock underrun/);
        assert.equal(receiver.snapshot().faultingCommand.transport.phase, 'resuming');
        assert.equal(receiver.snapshot().faultingCommand.command.sequence, 6);
        assert.equal(receiver.nodes.size, 0, 'A transition is not permission to clamp a late native start or keep partial audio.');
      }
    }
  }, { initialize: false });
});

test('production C# timing: admitted real commands retain their full semantic envelope and strict render boundary', {
  skip: !process.env.SOURCE_AUDIO_TIMING_DATA,
}, async () => {
  const root = resolve(process.env.SOURCE_AUDIO_TIMING_DATA, 'envelopes');
  const fixture = JSON.parse(await readFile(resolve(root, 'timing-fixture.json'), 'utf8'));
  assert.equal(fixture.schema, 'source-player-audio-timing-fixture/v1');
  assert.equal(fixture.directedAdmittedProgrammes, true);
  for (const mode of ['future', 'late-play', 'between-play-and-mix']) {
    await unit(async ({ receiver, context }) => {
      context.baseLatency = 2048 / 48000; context.outputLatency = .128;
      const origin = JSON.parse(fixture.batches[0]).commands[0].sourceTick;
      context.currentTime = 77.26933333333334 + project(origin) / RATE;
      for (const sample of fixture.samples) {
        assert.equal(sample.path, sample.id + '.pcm');
        receiver.sample(sample.id, sample.rate, sample.channels, new Uint8Array(await readFile(resolve(root, sample.path))), sample.metadata);
      }
      // Reconstruct the already delivered schedule; only the6827 boundary below is a timing qualification.
      for (const json of fixture.batches.slice(0, fixture.boundaryStart)) receiver.commands(json);
      const oldAnchor = receiver.anchor, timing = receiver.timing;
      context.currentTime = 196.992;
      receiver.commands(fixture.batches[fixture.boundaryStart]);
      receiver.commands(fixture.batches[fixture.boundaryStart + 1]); await receiver.settled();
      context.resumeJump = .08533333333333333;
      receiver.commands(fixture.batches[fixture.boundaryStart + 2]); await receiver.settled();
      context.advance(2048 / 48000);
      const anchor = receiver.anchor;
      assert.notEqual(anchor, oldAnchor, 'The011 refill belongs to a new physical segment, not a new source epoch.');
      assert.equal(receiver.transport.frozenSample, project(6827));
      assert.equal(receiver.transport.segmentId, 1);
      const boundaryPlay = JSON.parse(fixture.batches[fixture.boundaryStart]).commands[0];
      assert.equal(receiver.nodes.get(boundaryPlay.voiceId).offsetSample, 0,
        'The not-yet-started actual players:13 programme must resume at its first original PCM sample.');
      const json = fixture.batches[fixture.boundaryStart + 3], commands = JSON.parse(json).commands;
      assert(commands.some(c => c.kind === 'play' && c.bank === 'cursors' && c.playlistId === 80));
      assert(commands.some(c => c.kind === 'program-mix' && c.mixPoints.length > 1));
      const when = anchor + project(6828) / RATE;
      context.currentTime = mode === 'future' ? context.currentTime + .069405 + Math.ceil(RATE / TICKS) / RATE :
        when + (mode === 'late-play' ? .00005 : -.00005);
      context.onStart = () => { context.advance(mode === 'future' ? 2048 / 48000 : .0001); };
      if (mode === 'future') {
        try { receiver.commands(json); }
        catch (error) {
          await timingReceipt('actual-programmes-future-unexpected-fault', receiver.snapshot().faultingCommand);
          throw error;
        }
        assert(when > context.currentTime);
        assert.equal(receiver.anchor, anchor); assert.equal(receiver.timing, timing);
        await timingReceipt('actual-programmes-future', { oldAnchor, anchor, epoch: receiver.model.epoch, timing,
          sourceTick: 6828, currentC: context.currentTime, commands,
          remainingHeadroomMilliseconds: (when - context.currentTime) * 1000 });
      } else {
        assert.throws(() => receiver.commands(json), /sample-clock underrun/);
        const fault = receiver.snapshot().faultingCommand;
        assert.equal(fault.sourceSample, project(6828));
        assert.equal(fault.semanticEnvelopeChange, true);
        assert.equal(fault.command.kind, mode === 'late-play' ? 'play' : 'program-mix');
        await timingReceipt('actual-programmes-' + mode, fault);
      }
    }, { initialize: false });
  }
});

test('production C# timing: actual Host6827 commands stay future in pause segments without changing causal H', {
  skip: !process.env.SOURCE_AUDIO_TIMING_DATA,
}, async () => {
  const root = resolve(process.env.SOURCE_AUDIO_TIMING_DATA, 'host');
  const fixture = JSON.parse(await readFile(resolve(root, 'timing-fixture.json'), 'utf8'));
  assert.equal(fixture.schema, 'source-player-host-audio-timing-fixture/v1');
  assert.equal(fixture.privateClockOrPhaseMutation, false); assert.equal(fixture.witnessReads, 0);
  assert.equal(fixture.pauses.length, 3); assert.equal(fixture.pauses[0].pausedSource, 6827);
  await unit(async ({ receiver, context }) => {
    context.baseLatency = 2048 / 48000; context.outputLatency = .128;
    context.getOutputTimestamp = () => ({ contextTime: context.currentTime, performanceTime: 0 });
    context.currentTime = 77.26933333333334;
    for (const sample of fixture.samples)
      receiver.sample(sample.id, sample.rate, sample.channels, new Uint8Array(await readFile(resolve(root, sample.path))), sample.metadata);
    const first = fixture.pauses[0];
    for (const json of fixture.batches.slice(fixture.gameStart, first.batchStart)) receiver.commands(json);
    const initialAnchor = receiver.anchor, epoch = receiver.model.epoch, origin = receiver.model.sourceOriginTick, timing = receiver.timing;
    assert.equal(origin, 0); assert(Math.abs(initialAnchor - (77.36933333333333 + timing.renderReserveSeconds)) < 1e-12);
    let refill = 0, plays = 0, mixes = 0, minimumHeadroom = Infinity;
    let baseC = 196.992, baseH = first.pausedH;
    const segments = [];
    context.currentTime = 196.992;
    for (let index = first.batchStart; index < fixture.batches.length; index++) {
      const json = fixture.batches[index], batch = JSON.parse(json);
      const measuredH = fixture.batchHostTimes[index] - baseH;
      const audible = batch.commands.some(c => c.kind === 'play' || c.kind === 'program-mix');
      if (context.state === 'running')
        context.currentTime = Math.max(context.currentTime, baseC + measuredH + (audible ? .069405 : 0));
      let advanced = false;
      context.onStart = () => { if (!advanced) { context.advance(2048 / 48000); advanced = true; } };
      const active = batch.commands.find(c => c.kind === 'active');
      if (active) context.onStart = null;
      if (active?.active) {
        const pause = fixture.pauses.find(p => index >= p.batchStart && index < p.batchEnd);
        assert(pause, 'The actual Host active transition must have its causal pause receipt.');
        context.resumeJump = pause.renderRefillSeconds;
      }
      try {
        receiver.commands(json);
        if (active) {
          await receiver.settled();
          if (active.active) {
            refill += context.resumeJump;
            if (refill < .09) { context.advance(2048 / 48000); refill += 2048 / 48000; }
            baseC = context.currentTime; baseH = fixture.batchHostTimes[index];
            segments.push({ sample: active.atSample, anchor: receiver.anchor, baseC, baseH, segment: receiver.transport.segmentId });
            assert.equal(receiver.snapshot().clock.outputTimestamp.performanceTime, 0);
          }
        }
      } catch (error) {
        await timingReceipt('actual-host-unexpected-fault', receiver.snapshot().faultingCommand);
        throw error;
      }
      for (const c of batch.commands) if (c.kind === 'play' || c.kind === 'program-mix') {
        minimumHeadroom = Math.min(minimumHeadroom, receiver.anchor + c.atSample / RATE - context.currentTime);
        if (c.kind === 'play') plays++; else mixes++;
      }
      assert.equal(receiver.timing, timing);
      assert.equal(receiver.model.epoch, epoch); assert.equal(receiver.model.sourceOriginTick, origin);
    }
    assert(plays > 0 && minimumHeadroom > 0, 'Actual new Host programme events, not only envelope no-ops, must remain future for the entire batch.');
    assert.equal(segments.length, 3);
    await timingReceipt('actual-host-qualified', { initialAnchor, segments, epoch, origin, timing, plays, mixes,
      minimumHeadroomMilliseconds: minimumHeadroom * 1000, cumulativeRefillSeconds: refill, pauses: fixture.pauses,
      sourcePhysicsDrivenByC: false, sourceWakesAfterPauses: fixture.finalSourceTick - fixture.pauses.at(-1).resumedSource });
  }, { initialize: false });
});

// These are explicitly fake-node unit checks, never browser/autoplay acceptance.
async function unit(action, { initialize = true, count = 1440 } = {}) {
  const oldEvent = Object.getOwnPropertyDescriptor(globalThis, 'Event');
  const oldNavigator = Object.getOwnPropertyDescriptor(globalThis, 'navigator');
  class UnitGesture extends NativeEvent { get isTrusted() { return true; } }
  Object.defineProperty(globalThis, 'Event', { value: UnitGesture, configurable: true });
  Object.defineProperty(globalThis, 'navigator', { value: { userActivation: { isActive: true } }, configurable: true });
  const context = new FakeContext(), failures = [], sample = fixture(count);
  const receiver = new SourceAudioReceiver({ createContext: () => context, onFailure: error => failures.push(error.message) });
  const upload = (data = sample) => receiver.sample(data.id, RATE, 1, data.bytes, data.metadata);
  try {
    await receiver.unlock(new UnitGesture('click'));
    if (initialize) {
      upload();
      receiver.commands(batch([command('reset', 1, { trackMask: 63, active: true }), load(),
        command('reserved', 3, { trackMask: 35 })]));
    }
    await action({ receiver, context, sample, upload, failures, gesture: () => new UnitGesture('click') });
  } finally {
    await receiver.close();
    Object.defineProperty(globalThis, 'Event', oldEvent);
    if (oldNavigator) Object.defineProperty(globalThis, 'navigator', oldNavigator);
    else delete globalThis.navigator;
  }
}

test('fake-node unit: legacy construction never opens audio, and pre-unlock calls/forged gestures never queue', async () => {
  let created = 0;
  const receiver = new SourceAudioReceiver({ createContext: () => { created++; return new FakeContext(); } });
  assert.equal(created, 0);
  await assert.rejects(receiver.unlock(new NativeEvent('click')), /real current public user gesture/);
  await assert.rejects(receiver.unlock({ isTrusted: true, type: 'click' }), /real current public user gesture/);
  assert.throws(() => receiver.commands(batch([command('reset', 1, { active: true, trackMask: 63 })])), /before unlock/);
  const sample = fixture();
  assert.throws(() => receiver.sample(sample.id, RATE, 1, sample.bytes, sample.metadata), /before user-gesture unlock/);
  assert.equal(created, 0);
  assert.equal(receiver.samples.size, 0);
  assert.equal(receiver.nodes.size, 0);
  await receiver.close();
});

test('fake-node unit: signed PCM conversion, byte offsets and multi-block SHA256 agree with independent Node crypto', async () => {
  await unit(async ({ receiver, context, upload }) => {
    for (const count of [1, 2, 27, 28, 31, 32, 63, 64, 1000, 12345]) {
      const sample = fixture(count), parent = new Uint8Array(sample.bytes.length + 8);
      parent.set(sample.bytes, 4);
      upload({ ...sample, bytes: parent.subarray(4, -4) });
      const buffer = context.buffers.at(-1);
      assert.equal(buffer.channels, 1); assert.equal(buffer.sampleRate, RATE); assert.equal(buffer.length, count);
      assert.deepEqual(Array.from(buffer.getChannelData(0).subarray(0, Math.min(6, count))),
        [-1, 32767 / 32768, -1 / 32768, 0, .5, -.5].slice(0, Math.min(6, count)));
      upload(sample);
      assert.equal(context.buffers.at(-1), buffer, 'Identical hash resource must not allocate another AudioBuffer.');
    }
    assert.equal(receiver.samples.size, 10);
  }, { initialize: false });
});

test('fake-node unit: actual PCM nodes preserve source/subcall order, gain/pan, one-shot duration and supplied tracks', async () => {
  await unit(async ({ receiver, context, sample }) => {
    receiver.commands(batch([
      play(sample),
      play(sample, 5, { subcallOrder: 1, voiceId: 2, track: 5, rawVolume: 0xffffffff, rawPan: 0xffffffff,
        volumeByte: 255, panByte: 255, portableGain: 1, portablePan: 1 }),
      play(sample, 6, { kind: 'rejected', outputSequence: 2, voiceId: 0, track: -1, reason: 'all-tracks-reserved' }),
    ]));
    assert.equal(context.sources.length, 2, 'Rejected allocation is not an output node.');
    const when = 10.1 + project(1) / RATE;
    for (const source of context.sources) {
      assert.deepEqual(source.starts, [[when]]);
      assert.equal(source.buffer, context.buffers[0]);
      assert.equal(source.buffer.length, 1440);
      assert.equal(source.loop, false);
    }
    assert.deepEqual(context.gains[1].gain.calls, [{ value: 160 / 255, time: when }]);
    assert.deepEqual(context.gains[2].gain.calls, [{ value: 1, time: when }]);
    assert.deepEqual(context.pans.map(node => node.pan.calls), [[{ value: 0, time: when }], [{ value: 1, time: when }]]);
    assert.equal(context.sources[0].connections[0], context.gains[1]);
    assert.equal(context.gains[1].connections[0], context.pans[0]);
    assert.equal(context.pans[0].connections[0], receiver.output);
    assert.equal(receiver.output.connections[0], context.destination);
    assert.deepEqual(receiver.snapshot().tracks, [1, null, null, null, null, 2]);
    assert.equal(context.buffers.length, 1, 'No frame/voice uploads or rebaked gain.');
  });
});

test('fake-node unit: preemption/adjust/stop-all schedule exact times and cannot extend an earlier stop or clear replacements', async () => {
  await unit(async ({ receiver, context, sample }) => {
    receiver.commands(batch([play(sample)]));
    const old = context.sources[0], ended = old.onended;
    receiver.commands(batch([stop(5), play(sample, 6, { sourceTick: 2, outputSequence: 2, voiceId: 2 })]));
    assert.deepEqual(old.stops, [[10.1 + project(2) / RATE]]);
    ended();
    assert.deepEqual(receiver.snapshot().tracks, [2, null, null, null, null, null]);
    assert.equal(old.disconnected, true);
    receiver.commands(batch([command('adjust', 7, { sourceTick: 3, track: 0, voiceId: 2,
      volumeByte: 128, panByte: 0, portableGain: 128 / 255, portablePan: -1 })]));
    assert.deepEqual(context.gains[2].gain.calls.at(-1), { value: 128 / 255, time: 10.1 + project(3) / RATE });
    assert.deepEqual(context.pans[1].pan.calls.at(-1), { value: -1, time: 10.1 + project(3) / RATE });
    receiver.commands(batch([command('stop-all', 8, { sourceTick: 4, trackMask: 63 })]));
    assert.deepEqual(context.sources[1].stops, [[10.1 + project(4) / RATE]]);
    assert.equal(receiver.snapshot().reservedTracks, 0);
  });
  await unit(async ({ receiver, context, sample }) => {
    receiver.commands(batch([play(sample), stop(5), play(sample, 6, { sourceTick: 2, outputSequence: 2, voiceId: 2 }),
      command('stop-all', 7, { sourceTick: 3, trackMask: 63 })]));
    assert.deepEqual(context.sources[0].stops, [[10.1 + project(2) / RATE]],
      'A later stop-all must not replace an earlier preemption stop with a later Web Audio stop time.');
  });
});

test('fake-node unit: natural completion retains exact decoder tail, even when onended precedes the C# completion notification', async () => {
  await unit(async ({ receiver, context, sample }) => {
    receiver.commands(batch([play(sample)]));
    context.sources[0].end();
    assert.equal(receiver.nodes.size, 0);
    receiver.commands(batch([stop(5, { sourceTick: 4, atSample: project(1) + sample.count, reason: 'completed' })]));
    assert.equal(context.sources.length, 1);
    assert.equal(receiver.model.tracks[0], null);
    assert.equal(receiver.samples.size, 1);
  });
  await unit(async ({ receiver, context, sample }) => {
    receiver.commands(batch([play(sample), stop(5, {
      sourceTick: 4, atSample: project(1) + sample.count, reason: 'completed',
    })]));
    assert.deepEqual(context.sources[0].stops, [[10.1 + (project(1) + sample.count) / RATE]]);
  });
});

test('fake-node unit: bank delete preserves active buffers; reload cannot rebind the same immutable program', async () => {
  await unit(async ({ receiver, context, sample }) => {
    receiver.commands(batch([play(sample), command('bank-delete', 5, { sourceTick: 2, bank: BANK })]));
    assert.equal(context.sources[0].stops.length, 0);
    assert.equal(context.sources[0].disconnected, false);
    assert.equal(context.buffers.length, 1);
    receiver.commands(batch([load(6, { sourceTick: 2 })]));
    assert.throws(() => receiver.commands(batch([play(sample, 7, { sourceTick: 2,
      outputSequence: 2, voiceId: 2, track: 1, programHex: 'ffff' })])), /immutable bank\/program\/sample/);
    assert.equal(context.sources.length, 1);
    assert.equal(context.sources[0].disconnected, true, 'An invalid binding fails closed, not partly audible.');
  });
});

test('fake-node unit: original pregame activation anchors its continuous source clock without replaying joining time', async () => {
  await unit(async ({ receiver, context, sample, upload }) => {
    upload();
    const encode = commands => batch(commands.map(c => ({ ...c, loopStartSample: 0, loopEndSample: 0, mixPoints: [] })),
      { schema: 'source-audio-playback/v3', policy: 'portable-original-pregame-continuous-source/v3' });
    const origin = 5000;
    receiver.commands(encode([
      command('reset', 1, { sourceTick: origin, trackMask: 63, active: true,
        reason: 'original-mainproc-accepted-start-stop-all' }),
      load(2, { sourceTick: origin }),
    ]));
    receiver.commands(encode([play(sample, 3, { sourceTick: origin + 1 })]));
    assert.equal(receiver.model.sourceOriginTick, origin);
    assert.equal(receiver.model.sourceTick, origin + 1);
    assert(Math.abs(context.sources[0].starts[0][0] -
      (context.currentTime + .1 + (project(origin + 1) - project(origin)) / RATE)) < 1e-10,
    'Schedule relative to the actual accepted-start boundary, not thousands of idle joining ticks.');
    assert.throws(() => receiver.commands(encode([command('reserved', 4, {
      sourceTick: origin - 1, trackMask: 35,
    })])), /clock changed or rewound|clock rewound/);
  }, { initialize: false });
  for (const version of [1, 2]) await unit(async ({ receiver }) => {
    const fields = version === 1 ? {} :
      { schema: 'source-audio-playback/v2', policy: 'portable-compiled-original-playlists/v2' };
    const reset = command('reset', 1, { sourceTick: 5000, active: true, trackMask: 63,
      reason: 'original-mainproc-accepted-start-stop-all' });
    assert.throws(() => receiver.commands(batch([version === 1 ? reset :
      { ...reset, loopStartSample: 0, loopEndSample: 0 }], fields)), /invalid\/duplicate epoch reset/);
  }, { initialize: false });
});

test('fake-node unit: ready clock barrier starts once and actual global0 automation replaces only its future gain schedule', async () => {
  await unit(async ({ receiver, context, sample, upload }) => {
    upload();
    const encode = commands => batch(commands.map(c => ({
      loopStartSample: 0, loopEndSample: 0, mixPoints: [], ...c,
    })), { schema: 'source-audio-playback/v3', policy: 'portable-original-pregame-continuous-source/v3' });
    receiver.commands(encode([command('reset', 1, { trackMask: 63, active: true }), load(2)]));
    context.advance(20);
    receiver.commands(encode([
      command('clock-start', 3, { reason: 'first-source-wake-or-output-after-ready-boundary' }),
      play(sample, 4),
      command('program-mix', 5, { sourceTick: 1, track: 0, voiceId: 1, bank: BANK, playlistId: 1,
        reason: 'original-dcs233-global0-mixer-envelope',
        mixPoints: [{ atSample: project(1), gain: .4 }, { atSample: project(1) + 60 * 240, gain: 160 / 255 }] }),
    ]));
    assert(Math.abs(context.sources[0].starts[0][0] - (context.currentTime + .1 + project(1) / RATE)) < 1e-10,
      'No source wake/output existed during resource warmup; start the clock only at its ready boundary.');
    const gain = context.gains[1].gain;
    assert(gain.calls.some(c => c.cancelFrom === receiver.anchor + project(1) / RATE));
    assert(gain.calls.some(c => c.value === .4 && c.time === receiver.anchor + project(1) / RATE));
    receiver.commands(encode([command('program-mix', 6, { sourceTick: 2, track: 0, voiceId: 1,
      bank: BANK, playlistId: 1, reason: 'original-dcs233-global0-mixer-envelope',
      mixPoints: [{ atSample: project(2), gain: 160 / 255 }] })]));
    assert.deepEqual(gain.calls.slice(-2), [
      { cancelFrom: receiver.anchor + project(2) / RATE }, { value: 160 / 255, time: receiver.anchor + project(2) / RATE },
    ], 'Preemption/restore must cancel old future duck ramps without touching another voice or pan.');
    assert.throws(() => receiver.commands(encode([command('clock-start', 7, {
      sourceTick: 2, reason: 'first-source-wake-or-output-after-ready-boundary',
    })])), /once/);
  }, { initialize: false, count: 30000 });
});

test('fake-node unit: read-only fixed-origin witness observes the actual render-clock resume jump without reanchoring', async () => {
  await unit(async ({ receiver, context }) => {
    const encode = commands => batch(commands.map(c => ({
      loopStartSample: 0, loopEndSample: 0, mixPoints: [], ...c,
    })), { schema: 'source-audio-playback/v3', policy: 'portable-original-pregame-continuous-source/v3' });
    receiver.commands(encode([command('reset', 1, { trackMask: 63, active: true }),
      command('clock-start', 2, { reason: 'first-source-wake-or-output-after-ready-boundary' })]));
    const anchor = receiver.anchor;
    context.advance(.060);
    const before = receiver.sourceElapsed(0);
    receiver.commands(encode([command('active', 3, { active: false })]));
    await receiver.settled();
    context.advance(10);
    assert(Math.abs(receiver.sourceElapsed(0) - before) < 1e-12, 'Suspended wall time is not rendered audio time.');
    context.resumeJump = .085333333333;
    receiver.commands(encode([command('active', 4, { active: true })]));
    await receiver.settled();
    assert(Math.abs(receiver.sourceElapsed(0) - before - .085333333333) < 1e-10,
      'Render progress remains diagnostic; it must never become causal host/input elapsed time.');
    assert.equal(receiver.anchor, anchor);
    assert.equal(receiver.model.epoch, 0);
    assert.equal(receiver.sourceElapsed(1), -1, 'A different epoch cannot reuse the old audio time origin.');
  }, { initialize: false });
});

test('fake-node unit: explicit v2 compiled playlist loops its supplied region and bank eviction retains playing buffers', async () => {
  await unit(async ({ receiver, context, sample, upload }) => {
    upload();
    const compiled = commands => batch(commands.map(c => ({ ...c,
      loopStartSample: c.loop ? 480 : 0, loopEndSample: c.loop ? sample.count : 0,
    })), { schema: 'source-audio-playback/v2', policy: 'portable-compiled-original-playlists/v2' });
    receiver.commands(compiled([command('reset', 1, { trackMask: 63, active: true }), load(),
      command('reserved', 3, { trackMask: 35 }), play(sample, 4, { loop: true })]));
    assert.equal(context.sources[0].loop, true);
    assert.equal(context.sources[0].loopStart, 480 / RATE);
    assert.equal(context.sources[0].loopEnd, sample.count / RATE);
    receiver.commands(compiled([command('bank-delete', 5, { sourceTick: 100, bank: BANK })]));
    assert.equal(receiver.snapshot().sampleBytes, 0, 'Unloaded bank PCM cache is released, not every original bank eagerly retained.');
    assert.equal(context.sources[0].stops.length, 0, 'Bank deletion does not stop the actual already-playing buffer.');
    assert.equal(context.sources[0].disconnected, false);
    receiver.commands(compiled([stop(6, { sourceTick: 101, reason: 'source-stop-track' })]));
    assert.equal(context.sources[0].stops.length, 1, 'Only the actual source stop ends the indefinitely looping voice.');
    assert.equal(receiver.snapshot().compiledOriginalPrograms, true);
  }, { initialize: false });
  await unit(async ({ receiver, context, sample }) => {
    assert.throws(() => receiver.commands(batch([play(sample, 4, { loop: true })])), /invalid command/);
    assert.equal(context.sources.length, 0, 'v1 isolated admission cannot be converted into a loop by changing a flag.');
  });
});

test('fake-node unit: a qualified FFFFFFFF source slot has an ordered no-entry outcome, never replacement PCM', async () => {
  const compiled = commands => batch(commands.map(c => ({ ...c, loopStartSample: 0, loopEndSample: 0 })),
    { schema: 'source-audio-playback/v2', policy: 'portable-compiled-original-playlists/v2' });
  const empty = command('unassigned-playlist', 3, { sourceTick: 1, outputSequence: 1, bank: BANK,
    playlistId: 10, packageSha256: PACKAGE, sourceGitBlob: BLOB, rawVolume: 160, rawPan: 127,
    volumeByte: 160, panByte: 127, portableGain: 160 / 255, reason: 'original-sdk-unassigned-ffffffff-slot' });
  await unit(async ({ receiver, context }) => {
    receiver.commands(compiled([command('reset', 1, { trackMask: 63, active: true }), load(), empty]));
    assert.equal(context.sources.length, 0);
    assert.equal(receiver.snapshot().sampleBytes, 0, 'No click, silent sample or unrelated buffer is fabricated.');
    assert.equal(receiver.model.outputSequence, 1, 'The actual original source call ordinal is consumed once.');
  }, { initialize: false });
  await unit(async ({ receiver, context }) => {
    assert.throws(() => receiver.commands(compiled([
      command('reset', 1, { trackMask: 63, active: true }), load(),
      { ...empty, reason: 'unsupported-opcode13' },
    ])), /source-qualified FFFFFFFF/);
    assert.equal(context.sources.length, 0, 'Assigned-but-unsupported programs cannot be laundered into empty-slot outcomes.');
  }, { initialize: false });
});

test('fake-node unit: source active freezes existing nodes, paused reset cancels queues and old epochs/onended cannot replay or remove a new voice', async () => {
  await unit(async ({ receiver, context, sample }) => {
    receiver.commands(batch([play(sample)]));
    const oldEnded = context.sources[0].onended;
    receiver.commands(batch([command('active', 5, { sourceTick: 1, active: false })]));
    assert.equal(receiver.frameReady, false);
    await receiver.settled();
    const frozenTime = context.currentTime;
    context.advance(20);
    assert.equal(context.currentTime, frozenTime);
    assert.equal(context.sources[0].stops.length, 0);
    receiver.commands(batch([command('active', 6, { sourceTick: 1, active: true })]));
    await receiver.settled();
    assert.equal(context.sources[0].starts.length, 1, 'Resume must never restart a buffer.');
    receiver.commands(batch([command('active', 7, { sourceTick: 1, active: false })]));
    await receiver.settled();
    receiver.commands(batch([command('reset', 8, { epoch: 1, trackMask: 63, active: false }),
      load(9, { epoch: 1 })]));
    await receiver.settled();
    assert.equal(context.sources[0].disconnected, true);
    assert.equal(context.buffers.length, 1);
    assert.equal(receiver.commands(batch([play(sample)])), false);
    assert.equal(receiver.snapshot().sequence, 9);
    receiver.commands(batch([command('active', 10, { epoch: 1, active: true })]));
    await receiver.settled();
    receiver.commands(batch([play(sample, 11, { epoch: 1, outputSequence: 2, voiceId: 2 })]));
    oldEnded();
    assert.deepEqual(receiver.snapshot().tracks, [2, null, null, null, null, null]);
    assert.equal(context.sources.length, 2);
    assert.equal(context.buffers.length, 1);
  });
});

test('fake-node unit: a malformed trailing command is rejected before any node in that batch is created', async () => {
  await unit(async ({ receiver, context, sample }) => {
    assert.throws(() => receiver.commands(batch([play(sample),
      play(sample, 5, { outputSequence: 2, voiceId: 2, track: 1, sampleId: 'pcm16-' + '0'.repeat(64) })])),
    /unknown\/mismatched PCM/);
    assert.equal(context.sources.length, 0);
    assert.equal(receiver.output.gain.calls.at(-1).value, 0);
    assert.throws(() => receiver.commands(batch([play(sample)])), /fresh reset epoch/);
  });
});

test('fake-node unit: a new epoch accepts a new producer ordinal but never reuses command/voice identities', async () => {
  await unit(async ({ receiver, context, sample }) => {
    receiver.commands(batch([play(sample, 4, { outputSequence: 17 })]));
    receiver.commands(batch([command('reset', 5, { epoch: 1, active: true, trackMask: 63 }), load(6, { epoch: 1 })]));
    receiver.commands(batch([play(sample, 7, { epoch: 1, outputSequence: 1, voiceId: 2 })]));
    assert.equal(context.sources.length, 2);
    assert.equal(context.sources[0].disconnected, true);
    assert.deepEqual(receiver.snapshot().tracks, [2, null, null, null, null, null]);
    assert.throws(() => receiver.commands(batch([play(sample, 8, { epoch: 1, outputSequence: 1, voiceId: 3, track: 1 })])),
      /duplicated or reordered/);
    receiver.commands(batch([command('reset', 9, { epoch: 2, active: true, trackMask: 63 }), load(10, { epoch: 2 })]));
    assert.throws(() => receiver.commands(batch([play(sample, 11, { epoch: 2, outputSequence: 1, voiceId: 2 })])), /reuses a voice/);
    assert.equal(context.sources.length, 2, 'Only the genuinely new voice may allocate another source.');
  });
});

for (const [name, change, error] of [
  ['command sequence duplicate', c => ({ ...c, sequence: 3 }), /sequence/],
  ['fractional source tick', c => ({ ...c, sourceTick: 1.5 }), /scalar bounds/],
  ['unsafe command ordinal', c => ({ ...c, sequence: Number.MAX_SAFE_INTEGER + 1 }), /sequence/],
  ['sample clock rounding', c => ({ ...c, atSample: project(1) + 1 }), /exact source clock/],
  ['mixed epochs', c => ({ ...c, epoch: 1 }), /epoch/],
  ['unknown command', c => ({ ...c, kind: 'guessed-music-loop' }), /unknown command/],
  ['looped PCM', c => ({ ...c, loop: true }), /scalar bounds/],
  ['sample length substitution', c => ({ ...c, sampleCount: c.sampleCount - 1 }), /PCM sample binding/],
  ['package provenance', c => ({ ...c, packageSha256: '0'.repeat(64) }), /provenance binding/],
  ['raw gain clamp', c => ({ ...c, volumeByte: 255 }), /clamp mismatch/],
  ['rebaked playlist mix', c => ({ ...c, portableGain: .1 }), /portable projection/],
  ['out of range pan', c => ({ ...c, portablePan: 3 }), /portable projection/],
  ['negative endpoint gain', c => ({ ...c, rawVolume: 0, volumeByte: 0, portableGain: -1e-13 }), /portable projection/],
  ['pan endpoint overflow', c => ({ ...c, rawPan: 255, panByte: 255, portablePan: 1 + 1e-13 }), /portable projection/],
  ['missing output ordinal', c => ({ ...c, outputSequence: 0 }), /duplicated or reordered/],
  ['invalid track', c => ({ ...c, track: 6 }), /track/],
  ['unknown schema field', c => ({ ...c, guessed: true }), /fields\/schema/],
]) {
  test('fake-node unit rejects ' + name + ' without creating a source', async () => {
    await unit(async ({ receiver, context, sample }) => {
      assert.throws(() => receiver.commands(batch([change(play(sample))], { sourceTick: 2 })), error);
      assert.equal(context.sources.length, 0);
    });
  });
}

test('fake-node unit: cached PCM corruption/metadata, wrong formats and malformed batch schemas fail explicitly', async () => {
  for (const [mutate, error] of [
    [s => { s.bytes[0] ^= 1; }, /hash\/identity/],
    [s => { s.metadata = s.metadata.replace('generated synthetic test only', 'different admission'); }, /cached PCM metadata/],
    [s => { s.metadata = s.metadata.replace('"mixAlreadyBaked":true', '"mixAlreadyBaked":false'); }, /PCM metadata/],
    [s => { s.bytes = s.bytes.subarray(0, -1); }, /exact-sized/],
  ]) await unit(async ({ receiver, sample, upload }) => {
    mutate(sample);
    assert.throws(() => upload(sample), error);
    assert.equal(receiver.samples.size, 1, 'A poisoned upload must not replace the immutable cached AudioBuffer.');
  });
  await unit(async ({ receiver, sample }) => {
    assert.throws(() => receiver.sample(sample.id, 48000, 2, sample.bytes, sample.metadata), /31250Hz/);
  });
  for (const json of ['{', batch([command('reserved', 4)], { schema: 'source-audio-playback/v2' }),
    batch([command('reserved', 4)], { sourceTicksPerSecond: 0 })])
    await unit(async ({ receiver, context }) => {
      assert.throws(() => receiver.commands(json), /JSON|schema\/policy/);
      assert.equal(context.sources.length, 0);
    });
});

test('fake-node unit: external suspension/closed contexts, resume rejection and graph failure are not accepted as playback', async () => {
  await unit(async ({ receiver, context, failures, sample }) => {
    receiver.commands(batch([play(sample)]));
    context.externalState('suspended');
    assert.match(failures[0], /context is suspended/);
    assert.equal(context.sources[0].disconnected, true);
    assert.throws(() => receiver.frameReady, /context is suspended/);
  });
  await unit(async ({ receiver, context, sample }) => {
    context.state = 'closed';
    assert.throws(() => receiver.commands(batch([play(sample)])), /context is closed/);
    assert.equal(context.sources.length, 0);
  });
  await unit(async ({ receiver, context, failures }) => {
    receiver.commands(batch([command('active', 4, { active: false })]));
    await receiver.settled();
    context.rejectResume = true;
    receiver.commands(batch([command('active', 5, { active: true })]));
    await assert.rejects(receiver.settled(), /resume rejection/);
    assert.match(failures[0], /resume rejection/);
    assert.equal(context.sources.length, 0);
  });
  await unit(async ({ receiver, context, sample }) => {
    context.rejectPan = true;
    assert.throws(() => receiver.commands(batch([play(sample)])), /panner failure/);
    assert.equal(context.sources[0].starts.length, 0);
    assert.equal(context.sources[0].disconnected, true);
    assert.equal(context.gains[1].disconnected, true);
    assert.equal(receiver.nodes.size, 0);
  });
});

test('fake-node unit: clock underrun is terminal rather than bursting stale plays, fresh reset can recover without reupload', async () => {
  await unit(async ({ receiver, context, sample }) => {
    context.advance(1);
    assert.throws(() => receiver.commands(batch([play(sample)])), /sample-clock underrun/);
    assert.equal(context.sources.length, 0);
    receiver.commands(batch([command('reset', 7, { epoch: 1, trackMask: 63, active: true }), load(8, { epoch: 1 })]));
    receiver.commands(batch([play(sample, 9, { epoch: 1, outputSequence: 2, voiceId: 2 })]));
    assert.equal(context.sources.length, 1);
    assert.deepEqual(context.sources[0].starts, [[11.1 + project(1) / RATE]]);
    assert.equal(context.buffers.length, 1);
    await receiver.close();
    assert.equal(context.closes, 1);
    assert.equal(context.sources[0].disconnected, true);
    assert.throws(() => receiver.commands(batch([play(sample)])), /after disposal/);
    assert.equal(context.closes, 1);
  });
});

test('fake-node unit: unavailable/unresumed unlock and a never-resolving resume surface errors instead of waiting/replaying forever', async () => {
  await unit(async ({ gesture }) => {
    const unavailable = new SourceAudioReceiver({ createContext: () => { throw new Error('AudioContext unavailable'); } });
    await assert.rejects(unavailable.unlock(gesture()), /AudioContext unavailable/);
    assert.equal(unavailable.context, null);
    const context = new FakeContext();
    context.resume = async () => {};
    const unresumed = new SourceAudioReceiver({ createContext: () => context });
    await assert.rejects(unresumed.unlock(gesture()), /did not resume/);
    assert.equal(context.closes, 1);
    assert.equal(unresumed.unlocked, false);
  });
  await unit(async ({ receiver, context, sample, failures }) => {
    receiver.commands(batch([play(sample), command('active', 5, { sourceTick: 1, active: false })]));
    await receiver.settled();
    context.resume = () => new Promise(() => {});
    const nativeTimeout = globalThis.setTimeout;
    try {
      globalThis.setTimeout = (callback, _) => nativeTimeout(callback, 0);
      receiver.commands(batch([command('active', 6, { sourceTick: 1, active: true })]));
      await assert.rejects(receiver.settled(), /context did not resume/);
    } finally { globalThis.setTimeout = nativeTimeout; }
    assert.match(failures[0], /context did not resume/);
    assert.equal(context.sources.length, 1);
    assert.equal(context.sources[0].disconnected, true);
    assert.equal(receiver.nodes.size, 0);
  });
});

function proofPage() {
  return `<!doctype html><meta charset="utf-8"><title>Synthetic C6 receiver validation only</title>
<button id="unlock" disabled>Unlock synthetic PCM validation</button><p id="result">Preparing generated PCM</p>
<script type="module">
import { SourceAudioReceiver } from '/audio.js';
const RATE=${RATE}, TICKS=${TICKS}, POLICY=${JSON.stringify(POLICY)}, PACKAGE=${JSON.stringify(PACKAGE)},
  BLOB=${JSON.stringify(BLOB)}, BANK=${JSON.stringify(BANK)};
const project=${project.toString()};
${[command, batch, play, load, stop].map(fn => fn.toString()).join('\n')}
const errors=[], report=error=>errors.push(String(error.stack || error));
window.addEventListener('error', event=>report(event.error || event.message));
window.addEventListener('unhandledrejection', event=>report(event.reason));
const bytes=new Uint8Array(RATE*3*2), raw=new DataView(bytes.buffer);
for(let i=0;i<bytes.length/2;i++) raw.setInt16(i*2,Math.round(Math.sin(2*Math.PI*440*i/RATE)*8192),true);
const hash=Array.from(new Uint8Array(await crypto.subtle.digest('SHA-256',bytes)),
  b=>b.toString(16).padStart(2,'0')).join('');
const sample={bytes,id:'pcm16-'+hash,count:bytes.length/2,metadata:JSON.stringify({
  schema:'source-audio-sample/v1',format:'pcm-s16le',sha256:hash,sampleCount:bytes.length/2,
  mixAlreadyBaked:true,admission:'generated synthetic browser proof only',nativeDspEquivalent:false
})};
const locked=new SourceAudioReceiver({onFailure:report}), beforeUnlock=[];
for(const action of [
  ()=>locked.commands(batch([command('reset',1,{active:true,trackMask:63})])),
  ()=>locked.sample(sample.id,RATE,1,bytes,sample.metadata)
]){try{action();beforeUnlock.push('INCORRECTLY ACCEPTED');}catch(error){beforeUnlock.push(error.message);}}
try{await locked.unlock(new Event('click'));beforeUnlock.push('INCORRECTLY UNLOCKED');}
catch(error){beforeUnlock.push(error.message);}
const noPreUnlockContext=locked.context===null;
await locked.close();
let receiver,splitter,analysers,epoch=0,sequence=0,output=0,voice=0,lastTick=0;
const next=(kind,fields={})=>command(kind,++sequence,{epoch,sourceTick:lastTick,...fields});
const send=commands=>receiver.commands(batch(commands,{epoch,sourceTick:lastTick}));
const futureTick=()=>{lastTick=Math.max(lastTick,Math.ceil((receiver.context.currentTime-receiver.anchor+.045)*TICKS));return lastTick;};
const reset=async active=>{
  epoch++;lastTick=0;
  send([next('reset',{active,trackMask:63}),load(++sequence,{epoch}),next('reserved',{trackMask:35})]);
  await receiver.settled();
};
const observe=()=>{
  const rms=analysers.map(analyser=>{
    const data=new Float32Array(analyser.fftSize);analyser.getFloatTimeDomainData(data);
    return Math.sqrt(data.reduce((sum,n)=>sum+n*n,0)/data.length);
  });
  return {rms,currentTime:receiver.context.currentTime,snapshot:receiver.snapshot()};
};
const proof=globalThis.audioProof={ready:true,phase:'locked',beforeUnlock,noPreUnlockContext,errors};
proof.observe=observe;
proof.pause=async()=>{futureTick();send([next('active',{active:false})]);await receiver.settled();return observe();};
proof.resume=async()=>{send([next('active',{active:true})]);await receiver.settled();return observe();};
proof.adjust=()=>{futureTick();send([next('adjust',{track:0,voiceId:voice,volumeByte:128,panByte:255,
  portableGain:128/255,portablePan:1})]);return observe();};
proof.deleteBank=()=>{futureTick();send([next('bank-delete',{bank:BANK})]);return observe();};
proof.stop=()=>{futureTick();send([stop(++sequence,{epoch,sourceTick:lastTick,voiceId:voice,reason:'source-stop-track'}),
  next('stop-all',{trackMask:63})]);return observe();};
proof.resetPaused=async()=>{await reset(false);return observe();};
proof.stale=()=>{
  const accepted=receiver.commands(batch([play(sample,1,{epoch:epoch-1})],{epoch:epoch-1}));
  return {accepted,...observe()};
};
proof.preempt=()=>{
  futureTick();
  const first=++voice,second=++voice;
  send([play(sample,++sequence,{epoch,sourceTick:lastTick,outputSequence:++output,voiceId:first,
    rawVolume:255,volumeByte:255,portableGain:1,rawPan:0,panByte:0,portablePan:-1}),
    stop(++sequence,{epoch,sourceTick:lastTick,voiceId:first}),
    play(sample,++sequence,{epoch,sourceTick:lastTick,outputSequence:++output,voiceId:second,
      rawVolume:255,volumeByte:255,portableGain:1,rawPan:0,panByte:0,portablePan:-1})]);
  return {replacementVoice:second,...observe()};
};
let natural;
proof.natural=async()=>{
  const pcm=bytes.slice(0,1440*2),sha=Array.from(new Uint8Array(await crypto.subtle.digest('SHA-256',pcm)),
    b=>b.toString(16).padStart(2,'0')).join('');
  const short={bytes:pcm,id:'pcm16-'+sha,count:1440,metadata:JSON.stringify({
    ...JSON.parse(sample.metadata),sha256:sha,sampleCount:1440
  })};
  receiver.sample(short.id,RATE,1,short.bytes,short.metadata);
  futureTick();const id=++voice;
  send([play(short,++sequence,{epoch,sourceTick:lastTick,outputSequence:++output,voiceId:id,
    playlistId:2,programHex:'010200',rawVolume:255,volumeByte:255,portableGain:1})]);
  const node=receiver.nodes.get(id);
  natural={voiceId:id,endSample:project(lastTick)+1440,startTime:node.when,duration:node.source.buffer.duration,
    sampleCount:node.source.buffer.length};
  node.source.addEventListener('ended',event=>{natural.endedAt=receiver.context.currentTime;natural.trustedEnded=event.isTrusted;});
  return natural;
};
proof.naturalCompleted=()=>{
  lastTick=Math.max(lastTick,Math.ceil(natural.endSample*TICKS/RATE));
  send([stop(++sequence,{epoch,sourceTick:lastTick,atSample:natural.endSample,voiceId:natural.voiceId,
    playlistId:2,reason:'completed'})]);
  return {natural,...observe()};
};
proof.failBinding=()=>{
  futureTick();let error;
  try{send([play(sample,++sequence,{epoch,sourceTick:lastTick,outputSequence:++output,voiceId:++voice,
    sampleId:'pcm16-'+'0'.repeat(64)})]);}catch(failure){error=failure.message;}
  return {error,...observe()};
};
proof.close=async()=>{
  const context=receiver.context;await receiver.close();
  splitter.disconnect();for(const analyser of analysers)analyser.disconnect();
  let disposedError;try{receiver.commands(batch([next('stop-all',{trackMask:63})]));}catch(error){disposedError=error.message;}
  return {contextState:context.state,disposedError,snapshot:receiver.snapshot()};
};
document.querySelector('#unlock').addEventListener('click',async event=>{
  try{
    proof.gesture={type:event.type,trusted:event.isTrusted,active:navigator.userActivation.isActive};
    receiver=new SourceAudioReceiver({onFailure:report});
    await receiver.unlock(event);
    splitter=receiver.context.createChannelSplitter(2);
    analysers=[receiver.context.createAnalyser(),receiver.context.createAnalyser()];
    receiver.output.connect(splitter);
    analysers.forEach((analyser,i)=>{analyser.fftSize=2048;analyser.smoothingTimeConstant=0;splitter.connect(analyser,i);});
    receiver.sample(sample.id,RATE,1,bytes,sample.metadata);
    receiver.sample(sample.id,RATE,1,bytes,sample.metadata);
    epoch=-1;await reset(true);
    lastTick=1;voice=1;output=1;
    send([play(sample,++sequence,{epoch,sourceTick:lastTick,rawVolume:255,volumeByte:255,portableGain:1,
      rawPan:0,panByte:0,portablePan:-1})]);
    const node=[...receiver.nodes.values()][0],buffer=receiver.samples.get(sample.id).buffer;
    proof.graph={context:receiver.context.constructor.name,source:node.source.constructor.name,
      gain:node.gain.constructor.name,panner:node.pan.constructor.name,
      bufferRate:buffer.sampleRate,bufferChannels:buffer.numberOfChannels,bufferSamples:buffer.length,
      firstFloats:Array.from(buffer.getChannelData(0).subarray(0,8)),
      firstPcm:Array.from({length:8},(_,i)=>raw.getInt16(i*2,true)),
      sourceStart:node.when,anchor:receiver.anchor,atSample:project(1),hash};
    proof.phase='playing';document.querySelector('#result').textContent='Real synthetic PCM graph running';
  }catch(error){report(error);proof.phase='failed';}
},{once:true});
document.querySelector('#unlock').disabled=false;
</script>`;
}

function originalProofPage() {
  return `<!doctype html><meta charset="utf-8"><title>Actual original PCM component verification</title>
<button id="unlock" disabled>Unlock actual original playlist component</button>
<script type="module">
import {SourceAudioReceiver} from '/audio.js';
const fixture=await(await fetch('/original-proof.json')).json();
if(fixture.schema!=='actual-original-compiled-playlist-component/v1'||fixture.naturalGameProducerClaimed!==false)
  throw new Error('An actual explicitly labelled C# original component fixture is required.');
const inputs=await Promise.all(fixture.samples.map(async sample=>({...sample,
 bytes:new Uint8Array(await(await fetch('/original/'+sample.path)).arrayBuffer())})));
const proof=window.originalAudioProof={ready:true,phase:'locked',errors:[],fixture};
let receiver;
proof.snapshot=()=>receiver?.snapshot();
proof.reset=()=>{receiver.commands(fixture.batches.at(-1));return receiver.snapshot();};
proof.close=async()=>{await receiver.close();return receiver.snapshot();};
document.querySelector('#unlock').addEventListener('click',async event=>{
 try{
  proof.gesture={trusted:event.isTrusted,active:navigator.userActivation.isActive,type:event.type};
  receiver=new SourceAudioReceiver({onFailure:error=>proof.errors.push(error.message)});
  await receiver.unlock(event);
  for(const sample of inputs)receiver.sample(sample.id,sample.rate,sample.channels,sample.bytes,sample.metadata);
  receiver.commands(fixture.batches[0]);receiver.commands(fixture.batches[1]);
  proof.phase='playing';
 }catch(error){proof.errors.push(String(error.stack||error));proof.phase='failed';}
},{once:true});
document.querySelector('#unlock').disabled=false;
</script>`;
}

function transportProofPage() {
  const recorder = `class TransportCapture extends AudioWorkletProcessor {
    process(inputs) {
      const input = inputs[0];
      if (input?.length) {
        const left = input[0].slice(), right = (input[1] ?? input[0]).slice();
        this.port.postMessage({ frame: currentFrame, left, right }, [left.buffer, right.buffer]);
      }
      return true;
    }
  }
  registerProcessor('transport-capture', TransportCapture);`;
  return `<!doctype html><meta charset="utf-8"><title>Bounded pause transport / synthetic phase vector</title>
<button id="start" disabled>Start labelled synthetic transport</button>
<button id="pause" disabled>Public pause</button><button id="resume" disabled>Public resume</button>
<script type="module">
import {SourceAudioReceiver} from '/audio.js';
const RATE=${RATE},TICKS=${TICKS},POLICY=${JSON.stringify(POLICY)},PACKAGE=${JSON.stringify(PACKAGE)},
  BLOB=${JSON.stringify(BLOB)},BANK=${JSON.stringify(BANK)};
const project=${project.toString()};
${[command, batch, play, load, stop, reservedBatch].map(fn => fn.toString()).join('\n')}
const wait=ms=>new Promise(resolve=>setTimeout(resolve,ms));
const hash=async bytes=>Array.from(new Uint8Array(await crypto.subtle.digest('SHA-256',bytes)),
  b=>b.toString(16).padStart(2,'0')).join('');
async function vector(count,frequency){
  const bytes=new Uint8Array(count*2),view=new DataView(bytes.buffer);
  for(let i=0;i<count;i++)view.setInt16(i*2,Math.round(6500*Math.sin(2*Math.PI*frequency*i/RATE)+
    2500*Math.sin(2*Math.PI*(frequency+173)*i/RATE)),true);
  const sha=await hash(bytes);
  return {bytes,id:'pcm16-'+sha,count,metadata:JSON.stringify({schema:'source-audio-sample/v1',
    format:'pcm-s16le',sha256:sha,sampleCount:count,mixAlreadyBaked:true,
    admission:'labelled synthetic PCM phase vector; no native/game equivalence',nativeDspEquivalent:false})};
}
const music=await vector(RATE*2+37,311),effect=await vector(3840,743);
const proof=window.transportProof={ready:true,phase:'locked',errors:[],gestures:[],segments:[],
  transitions:[],events:[],nativeEnds:[],scope:'Receiver-only normal sandbox/default autoplay; synthetic PCM, no Game.'};
const report=error=>{proof.errors.push(String(error.stack??error));proof.phase='failed';};
window.addEventListener('error',event=>report(event.error??event.message));
window.addEventListener('unhandledrejection',event=>report(event.reason));
let receiver,recorderNode,recorderUrl,sequence=0,tick=0,voice=1,output=1,host=0,activeFrom=null,lastEffect=null;
const loopStart=1680,chunks=[],gains=[{atSample:0,gain:.75}],effects=[];
const next=(kind,fields={})=>command(kind,++sequence,{sourceTick:tick,...fields});
const send=commands=>receiver.commands(reservedBatch(commands));
const take=label=>({label,activeH:host,tick,snapshot:receiver.snapshot()});
const observeHost=()=>{
  const now=performance.now();
  if(activeFrom!==null)host+=(now-activeFrom)/1000;
  activeFrom=now;tick=Math.floor(host*TICKS);
};
const segment=()=>proof.segments.at(-1);
const rememberSegment=()=>{
  const s=receiver.snapshot(),node=receiver.nodes.get(1);
  proof.segments.push({id:s.clock.transport.segmentId,anchor:s.clock.anchor,
    startSample:s.clock.transport.startSample,startContextTime:s.clock.transport.startContextTime,
    offsetSample:node.offsetSample,loopStart:node.source.loopStart,loopEnd:node.source.loopEnd,
    source:node.source.constructor.name,gain:node.gain.constructor.name,panner:node.pan.constructor.name,
    preservedFuture:structuredClone(node.mixPoints),endSample:null,endContextTime:null});
  for(const node of receiver.nodes.values()){
    const source=node.source,id=node.id,segmentId=node.segmentId;
    source.addEventListener('ended',event=>proof.nativeEnds.push({voice:id,segment:segmentId,trusted:event.isTrusted,
      C:receiver.context.currentTime,currentOwner:receiver.nodes.get(id)?.segmentId??null}));
  }
};
const buttons=()=>{document.querySelector('#pause').disabled=proof.phase!=='running';
  document.querySelector('#resume').disabled=proof.phase!=='paused';};
const gesture=(event,kind)=>proof.gestures.push({kind,trusted:event.isTrusted,active:navigator.userActivation.isActive});
document.querySelector('#start').onclick=async event=>{
  try{
    gesture(event,'start');document.querySelector('#start').disabled=true;
    receiver=new SourceAudioReceiver({onFailure:report});await receiver.unlock(event);
    proof.unlockClock=receiver.clockObservation();
    receiver.sample(music.id,RATE,1,music.bytes,music.metadata);
    receiver.sample(effect.id,RATE,1,effect.bytes,effect.metadata);
    recorderUrl=URL.createObjectURL(new Blob([${JSON.stringify(recorder)}],{type:'text/javascript'}));
    await receiver.context.audioWorklet.addModule(recorderUrl);
    recorderNode=new AudioWorkletNode(receiver.context,'transport-capture',{
      channelCount:2,channelCountMode:'explicit',numberOfInputs:1,numberOfOutputs:1,outputChannelCount:[2]});
    recorderNode.port.onmessage=event=>chunks.push(event.data);
    receiver.output.connect(recorderNode);recorderNode.connect(receiver.context.destination);
    send([next('reset',{active:true,trackMask:63}),load(++sequence),
      next('clock-start',{reason:'first-source-wake-or-output-after-ready-boundary'}),
      play(music,++sequence,{sourceTick:0,loop:true,loopStartSample:loopStart,loopEndSample:music.count,
        rawVolume:255,volumeByte:255,portableGain:1,rawPan:0,panByte:0,portablePan:-1}),
      next('program-mix',{track:0,voiceId:1,bank:BANK,playlistId:1,
        reason:'original-dcs233-global0-mixer-envelope',mixPoints:gains})]);
    proof.graph={context:receiver.context.constructor.name,contextRate:receiver.context.sampleRate,
      music:music.id,effect:effect.id,originalRate:RATE,nativeDspEquivalent:false};
    proof.initial=take('initial');rememberSegment();
    activeFrom=performance.now();proof.phase='running';buttons();
  }catch(error){report(error);}
};
document.querySelector('#pause').onclick=async event=>{
  try{
    gesture(event,'pause');observeHost();activeFrom=null;proof.phase='pausing';buttons();
    const before=take('pause-request');
    send([next('active',{active:false})]);await receiver.settled();
    segment().endSample=project(tick);segment().endContextTime=segment().anchor+project(tick)/RATE;
    proof.transitions.push({before,after:take('pause-completed'),
      heldFuture:structuredClone(receiver.nodes.get(1).mixPoints)});
    proof.phase='paused';buttons();
  }catch(error){report(error);}
};
document.querySelector('#resume').onclick=async event=>{
  try{
    gesture(event,'resume');proof.phase='resuming';buttons();
    const before=take('resume-request');
    send([next('active',{active:true})]);await receiver.settled();
    const after=take('resume-completed');
    proof.transitions.push({before,after});rememberSegment();
    activeFrom=performance.now();proof.phase='running';buttons();
  }catch(error){report(error);}
};
proof.event=cycle=>{
  if(proof.phase!=='running')throw new Error('Public transport is not stable/running.');
  observeHost();const commands=[],sample=project(tick);
  if(lastEffect){
    const completed=sample>=lastEffect.end;
    commands.push(stop(++sequence,{sourceTick:tick,voiceId:lastEffect.voice,track:1,playlistId:2,
      atSample:completed?lastEffect.end:sample,reason:completed?'completed':'preempted'}));
    if(!completed)effects.at(-1).end=sample;
  }
  const id=++voice;
  commands.push(play(effect,++sequence,{sourceTick:tick,voiceId:id,outputSequence:++output,track:1,playlistId:2,
    programHex:'010200',rawVolume:192,volumeByte:192,portableGain:192/255,
    rawPan:255,panByte:255,portablePan:1}));
  lastEffect={voice:id,end:sample+effect.count};
  effects.push({start:sample,end:lastEffect.end});
  const points=[{atSample:sample,gain:cycle%8===0?0:.25+cycle%4/16},
    {atSample:sample+240,gain:.5},{atSample:sample+30*240,gain:.75},
    {atSample:sample+1000*240,gain:1}];
  commands.push(next('program-mix',{track:0,voiceId:1,bank:BANK,playlistId:1,
    reason:'original-dcs233-global0-mixer-envelope',mixPoints:points}));
  send(commands);
  const keep=gains.filter(point=>point.atSample<sample);gains.splice(0,gains.length,...keep,...points);
  const observation=take('genuine-event-'+cycle);proof.events.push(observation);return observation;
};
proof.finish=async()=>{
  if(proof.phase!=='paused')throw new Error('Finish only after the final real public pause.');
  await wait(100);
  const rate=receiver.context.sampleRate,musicRaw=new DataView(music.bytes.buffer),effectRaw=new DataView(effect.bytes.buffer);
  const interpolate=(raw,count,position,loop)=>{
    if(loop&&position>=count)position=loopStart+(position-loopStart)%(count-loopStart);
    if(position<0||position>=count)return 0;
    const i=Math.floor(position),f=position-i,j=i+1<count?i+1:(loop?loopStart:i);
    return (raw.getInt16(i*2,true)*(1-f)+raw.getInt16(j*2,true)*f)/32768;
  };
  const summaries=proof.segments.map(s=>({id:s.id,checked:0,leftEnergy:0,rightEnergy:0,
    errorEnergy:0,rightErrorEnergy:0,maxError:0,shiftEnergy:Array(9).fill(0),preview:[],outliers:[]}));
  let gapPeak=0,gapFrames=0,lastFrame=0;
  for(const chunk of chunks){
    lastFrame=Math.max(lastFrame,chunk.frame+chunk.left.length);
    for(let i=0;i<chunk.left.length;i+=7){
      const time=(chunk.frame+i)/rate,s=proof.segments.find(s=>time>=s.startContextTime&&time<s.endContextTime);
      if(!s){
        if(proof.segments.every(s=>Math.abs(time-s.startContextTime)>.012&&Math.abs(time-s.endContextTime)>.012)){
          gapPeak=Math.max(gapPeak,Math.abs(chunk.left[i]),Math.abs(chunk.right[i]));gapFrames++;
        }
        continue;
      }
      const sample=(time-s.anchor)*RATE;
      if(sample<s.startSample+384||sample>s.endSample-384)continue;
      const offset=sample<music.count?sample:loopStart+(sample-loopStart)%(music.count-loopStart);
      if(Math.abs(offset-loopStart)<64||music.count-offset<64)continue;
      if(gains.some(p=>Math.abs(p.atSample-sample)<64))continue;
      const fx=effects.find(e=>sample>=e.start&&sample<e.end);
      if(effects.some(e=>Math.abs(e.start-sample)<64||Math.abs(e.end-sample)<64))continue;
      let gain=gains[0].gain;
      for(const point of gains)if(point.atSample<=sample)gain=point.gain;else break;
      const expected=interpolate(musicRaw,music.count,sample,true)*gain;
      const expectedRight=fx?interpolate(effectRaw,effect.count,sample-fx.start,false)*192/255:0;
      const actual=chunk.left[i],right=chunk.right[i],r=summaries[s.id],error=actual-expected;
      r.checked++;r.errorEnergy+=error*error;r.rightErrorEnergy+=(right-expectedRight)**2;
      r.leftEnergy+=expected*expected;r.rightEnergy+=expectedRight*expectedRight;
      r.maxError=Math.max(r.maxError,Math.abs(error));
      if(Math.abs(error)>.00001&&r.outliers.length<12)r.outliers.push({
        frame:chunk.frame+i,time,sample,actual,expected,gain,offset,
        nearbyGainPoints:gains.filter(p=>Math.abs(p.atSample-sample)<1000)});
      for(let d=-4;d<=4;d++){
        const shifted=interpolate(musicRaw,music.count,sample+d/2,true)*gain;
        r.shiftEnergy[d+4]+=(actual-shifted)**2;
      }
      if(r.preview.length<8)r.preview.push({sample,actual,expected,right,expectedRight,gain});
    }
  }
  proof.pcm={method:'Real AudioWorklet input capture of unchanged receiver output; compare against original quantized phase vector at logical samples.',
    logicalEpochUnchanged:receiver.model.epoch===0,sourceOrigin:receiver.model.sourceOriginTick,
    sampleFramesCaptured:lastFrame,chunkCount:chunks.length,gapFrames,gapPeak,summaries:summaries.map(r=>({
      id:r.id,checked:r.checked,leftReferenceRms:Math.sqrt(r.leftEnergy/r.checked),
      rightReferenceRms:Math.sqrt(r.rightEnergy/r.checked),rmsError:Math.sqrt(r.errorEnergy/r.checked),
      rightRmsError:Math.sqrt(r.rightErrorEnergy/r.checked),maxError:r.maxError,
      bestSubsampleShift:(r.shiftEnergy.indexOf(Math.min(...r.shiftEnergy))-4)/2,
      shiftRms:r.shiftEnergy.map(e=>Math.sqrt(e/r.checked)),preview:r.preview,outliers:r.outliers
    }))};
  proof.final=take('final-held');
  await receiver.close();recorderNode.disconnect();recorderNode.port.close();URL.revokeObjectURL(recorderUrl);
  proof.closed=receiver.snapshot();proof.phase='complete';buttons();
  return proof;
};
document.querySelector('#start').disabled=false;
</script>`;
}

async function browserServer(appBundle, originalRoot = null, pageOverride = null) {
  const moduleBytes = await readFile(new URL('./wwwroot/audio.js', import.meta.url));
  const page = pageOverride ?? (originalRoot ? originalProofPage() : proofPage()), requests = [], root = appBundle ? resolve(appBundle) : null;
  const originals = originalRoot ? resolve(originalRoot) : null;
  const mime = { '.js': 'text/javascript', '.json': 'application/json', '.wasm': 'application/wasm',
    '.html': 'text/html', '.css': 'text/css' };
  const server = createServer(async (request, response) => {
    try {
      const path = new URL(request.url, 'http://127.0.0.1').pathname;
      requests.push(path);
      response.setHeader('Cache-Control', 'no-store');
      if (path === '/health') { response.end('owned-c6-audio'); return; }
      if (path === '/audio.js') { response.setHeader('Content-Type', mime['.js']); response.end(moduleBytes); return; }
      if (path === '/') { response.setHeader('Content-Type', mime['.html']); response.end(page); return; }
      if (originals && (path === '/original-proof.json' || /^\/original\/pcm16-[0-9a-f]{64}\.pcm$/.test(path))) {
        const name = path === '/original-proof.json' ? 'audio-proof.json' : path.slice('/original/'.length);
        const file = resolve(originals, name);
        assert(file.startsWith(originals + sep));
        response.setHeader('Content-Type', path.endsWith('.json') ? mime['.json'] : 'application/octet-stream');
        response.end(await readFile(file)); return;
      }
      if (root && path.startsWith('/app/')) {
        const relative = decodeURIComponent(path.slice(5)) || 'index.html', file = resolve(root, relative);
        assert(file.startsWith(root + sep), 'Static audio proof must not escape the explicitly owned app bundle.');
        const bytes = await readFile(file);
        response.setHeader('Content-Type', mime[extname(file)] || 'application/octet-stream');
        response.end(bytes); return;
      }
      response.writeHead(404); response.end();
    } catch (error) {
      response.writeHead(error.code === 'ENOENT' ? 404 : 500);
      response.end(String(error.message));
    }
  });
  await new Promise((ok, reject) => { server.once('error', reject); server.listen(0, '127.0.0.1', ok); });
  const port = server.address().port;
  assert(port > 8097, 'Use a unique owned loopback port, never the protected listeners.');
  const url = `http://127.0.0.1:${port}/`;
  assert.equal(await (await fetch(new URL('health', url))).text(), 'owned-c6-audio', 'Verify owned server readiness.');
  return { url, requests, close: () => new Promise((ok, reject) => server.close(error => error ? reject(error) : ok())) };
}

async function normallySandboxedChrome() {
  const runtime = await createChromeRuntime();
  const executable = process.env.SOURCE_AUDIO_CHROME_BIN || '/opt/google/chrome/chrome';
  const args = ['--headless=new', '--remote-debugging-address=127.0.0.1', '--remote-debugging-port=0',
    `--user-data-dir=${runtime.profile}`, `--disk-cache-dir=${runtime.cache}`, '--no-first-run',
    '--no-default-browser-check', '--disable-background-networking', '--disable-component-update',
    '--disable-sync', '--metrics-recording-only', '--no-pings', '--window-size=1280,900', 'about:blank'];
  assert.notEqual(process.getuid?.(), 0, 'Real audio proof must not disable the Chrome sandbox to run as root.');
  const child = spawn(executable, args, { stdio: ['ignore', 'ignore', 'pipe'], env: {
    ...process.env, HOME: runtime.directory, TMPDIR: runtime.tmp, XDG_RUNTIME_DIR: runtime.tmp,
    XDG_CACHE_HOME: runtime.cache, XDG_CONFIG_HOME: runtime.config, XDG_DATA_HOME: runtime.config,
  } });
  let stderr = '', exited = false, browser, page;
  const exitedPromise = new Promise(ok => child.once('close', () => { exited = true; ok(); }));
  const close = async () => {
    page?.close(); browser?.close();
    if (!exited) child.kill('SIGTERM');
    let timer;
    await Promise.race([exitedPromise, new Promise(ok => { timer = setTimeout(ok, 5000); })]);
    clearTimeout(timer);
    if (!exited) { child.kill('SIGKILL'); await exitedPromise; }
    await runtime.cleanup();
    return { chromeExited: exited, privateRuntimeRemoved: true, directory: runtime.directory };
  };
  try {
    const endpoint = await new Promise((ok, reject) => {
      const timer = setTimeout(() => reject(new Error('Chrome startup timed out: ' + stderr)), 30000);
      child.once('error', error => { clearTimeout(timer); reject(error); });
      child.once('exit', (code, signal) => { clearTimeout(timer); reject(new Error(`Chrome exit ${code}/${signal}: ${stderr}`)); });
      child.stderr.on('data', data => {
        stderr = (stderr + String(data)).slice(-6000);
        const match = stderr.match(/DevTools listening on (ws:\/\/127\.0\.0\.1:\d+\/devtools\/browser\/[^\s]+)/);
        if (match) { clearTimeout(timer); ok(match[1]); }
      });
    });
    const response = await fetch(`http://127.0.0.1:${new URL(endpoint).port}/json/list`, { signal: AbortSignal.timeout(5000) });
    assert.equal(response.status, 200, 'Verify actual owned Chrome/CDP readiness.');
    const targets = await response.json();
    browser = await Cdp.connect(endpoint);
    page = await Cdp.connect(targets.find(target => target.type === 'page').webSocketDebuggerUrl);
    await Promise.all([page.send('Page.enable'), page.send('Runtime.enable'), page.send('Network.enable')]);
    return { page, version: await browser.send('Browser.getVersion'), args, pid: child.pid, close };
  } catch (error) { await close(); throw error; }
}

async function untilBrowser(page, expression, description, timeout = 15000) {
  const deadline = Date.now() + timeout;
  while (Date.now() < deadline) {
    const result = await value(page, expression);
    if (result) return result;
    await sleep(30);
  }
  throw new Error('Timed out: ' + description);
}

test('real Chrome transport:16 trusted repeated public pauses retain native PCM phase and programme future without a lifetime clock budget',
  { skip: !process.env.SOURCE_AUDIO_TRANSPORT_BROWSER_EVIDENCE, timeout: 180000 }, async () => {
    const output = process.env.SOURCE_AUDIO_TRANSPORT_BROWSER_EVIDENCE;
    const server = await browserServer(null, null, transportProofPage());
    const evidence = { schema: 'source-player-pause-transport-browser/v1', status: 'running',
      syntheticPhaseVector: true, gameOrPhysicsMutation: false, exceptions: [],
      audioJsSha256: digest(await readFile(new URL('./wwwroot/audio.js', import.meta.url))),
      audioTestSha256: digest(await readFile(new URL('./audio.test.mjs', import.meta.url))) };
    let chrome;
    const click = async id => {
      const point = await value(chrome.page, `(()=>{const b=document.querySelector('#${id}');
        if(b.disabled)throw new Error('Disabled public ${id} control');
        const r=b.getBoundingClientRect();return {x:r.x+r.width/2,y:r.y+r.height/2};})()`);
      await chrome.page.send('Input.dispatchMouseEvent', { type: 'mousePressed', ...point, button: 'left', clickCount: 1 });
      await chrome.page.send('Input.dispatchMouseEvent', { type: 'mouseReleased', ...point, button: 'left', clickCount: 1 });
    };
    const phase = async expected => {
      const actual = await untilBrowser(chrome.page, `transportProof.phase==='${expected}'||transportProof.phase==='failed'?
        {phase:transportProof.phase,errors:transportProof.errors}:false`, 'public transport ' + expected);
      assert.equal(actual.phase, expected, JSON.stringify(actual.errors));
    };
    try {
      chrome = await normallySandboxedChrome();
      evidence.chrome = { version: chrome.version, args: chrome.args, pid: chrome.pid };
      chrome.page.on('Runtime.exceptionThrown', event => evidence.exceptions.push(event.exceptionDetails));
      await chrome.page.send('Page.navigate', { url: server.url });
      await chrome.page.send('Page.bringToFront');
      await untilBrowser(chrome.page, 'window.transportProof?.ready', 'transport PCM/worklet proof preparation');
      await click('start'); await phase('running'); await sleep(500);
      await value(chrome.page, 'transportProof.event(0)');
      for (let i = 0; i < 16; i++) {
        await click('pause'); await phase('paused');
        await sleep(i < 8 ? 500 : [60, 350, 650][i % 3]);
        await click('resume'); await phase('running');
        await sleep(i < 8 ? 500 : [250, 730, 410][i % 3]);
        await value(chrome.page, `transportProof.event(${i + 1})`);
      }
      await click('pause'); await phase('paused');
      evidence.proof = await value(chrome.page, 'transportProof.finish()');
      const proof = evidence.proof;
      assert.deepEqual(proof.errors, []); assert.deepEqual(evidence.exceptions, []);
      assert.equal(proof.phase, 'complete'); assert.equal(proof.graph.context, 'AudioContext');
      assert.equal(proof.graph.originalRate, RATE);
      assert.equal(proof.gestures.length, 34);
      assert(proof.gestures.every(g => g.trusted && g.active), 'Every pause and resume is actual trusted browser input, not a forged event.');
      assert.equal(proof.segments.length, 17);
      assert.equal(proof.pcm.logicalEpochUnchanged, true); assert.equal(proof.pcm.sourceOrigin, 0);
      const pauses = proof.transitions.filter(t => t.after.label === 'pause-completed');
      const resumes = proof.transitions.filter(t => t.after.label === 'resume-completed');
      for (const [i, segment] of proof.segments.entries()) {
        const offset = segment.startSample < RATE * 2 + 37 ? segment.startSample :
          1680 + (segment.startSample - 1680) % (RATE * 2 + 37 - 1680);
        assert.equal(segment.offsetSample, offset, 'No native continuation may skip, replay or reset its original loop phase.');
        assert.equal(segment.source, 'AudioBufferSourceNode');
        assert.equal(segment.gain, 'GainNode'); assert.equal(segment.panner, 'StereoPannerNode');
        assert.equal(segment.id, i);
        assert(Math.abs(segment.startContextTime - segment.anchor - segment.startSample / RATE) < 1e-12);
        if (i) {
          assert.deepEqual(segment.preservedFuture, pauses[i - 1].heldFuture);
          assert.equal(segment.startSample, pauses[i - 1].after.snapshot.clock.logicalPlaybackSample);
          assert.equal(resumes[i - 1].after.snapshot.clock.logicalPlaybackSample, segment.startSample);
          assert.equal(resumes[i - 1].before.snapshot.clock.logicalPlaybackSample, segment.startSample);
          assert.equal(resumes[i - 1].after.tick, resumes[i - 1].before.tick);
          const realH = proof.events[i].activeH - pauses[i - 1].before.activeH;
          assert(realH >= (i <= 8 ? .5 : .25), 'Real positive causal performance time, never a zero-H pause construction.');
        }
      }
      assert(proof.pcm.gapFrames > 1000 && proof.pcm.gapPeak < .00001,
        'The old segment and queued future voices must emit no doubled/replayed sound in the held physical gap.');
      assert(proof.pcm.summaries.every(s => s.checked > 500 && s.rmsError < .00001 && s.maxError < .0001 && s.rightRmsError < .00001 &&
        Math.abs(s.bestSubsampleShift) <= 1),
      'Actual captured PCM must match the independent logical phase vector within native resampling precision, across every segment.');
      assert(proof.pcm.summaries.some(s => s.rightReferenceRms > .01), 'A real nonlooping pending/active effect tail must also reach the captured graph.');
      assert(proof.nativeEnds.length >= 16 && proof.nativeEnds.every(e => e.trusted));
      assert.equal(proof.final.snapshot.startedVoices, 18, 'Native continuations do not duplicate logical music or source events.');
      assert.equal(proof.closed.nodes.length, 0); assert.equal(proof.closed.sampleCount, 0);
      assert.deepEqual(proof.closed.cleanupErrors, []); assert.equal(proof.closed.contextState, 'closed');
      evidence.status = 'bounded-normal-browser-transport-pass-not-game-release';
    } catch (error) {
      evidence.status = 'failed'; evidence.error = String(error.stack ?? error);
      if (chrome && !evidence.proof) evidence.partial = await value(chrome.page, 'window.transportProof').catch(() => null);
      throw error;
    } finally {
      evidence.cleanup = await chrome?.close();
      await server.close();
      await mkdir(dirname(output), { recursive: true });
      await writeFile(output, JSON.stringify(evidence, null, 2) + '\n', { flag: 'wx' });
    }
  });

test('real Chrome: one render-output clock pause/resume diagnostic without game-time advancement',
  { skip: !process.env.SOURCE_AUDIO_CLOCK_EVIDENCE, timeout: 90000 }, async () => {
    const server = await browserServer(null);
    let chrome;
    try {
      chrome = await normallySandboxedChrome();
      await chrome.page.send('Page.navigate', { url: server.url });
      await chrome.page.send('Page.setDocumentContent', {
        frameId: (await chrome.page.send('Page.getFrameTree')).frameTree.frame.id,
        html: `<button id="clock">Measure clock only</button><script>
        window.clockOnly={done:false,frames:[],errors:[],scope:'Actual platform clock diagnostic only; no Game/source ticks/PCM/native equivalence.'};
        document.querySelector('#clock').onclick=async e=>{
          const c=new AudioContext({latencyHint:'interactive'}), p=clockOnly;
          const take=label=>p.frames.push({label,host:performance.now(),state:c.state,currentTime:c.currentTime,
            output:c.getOutputTimestamp(),baseLatency:c.baseLatency,outputLatency:c.outputLatency??null});
          const wait=ms=>new Promise(r=>setTimeout(r,ms));
          try{
            p.gesture={trusted:e.isTrusted,active:navigator.userActivation.isActive};
            take('before-first-resume');await c.resume();take('first-resume-completed');
            for(let i=0;i<5;i++){await wait(100);take('steady-running-'+i);}
            take('suspend-requested');await c.suspend();take('suspend-completed');
            await wait(500);take('paused-wall-time');
            take('resume-requested');await c.resume();take('resume-completed');
            for(let i=0;i<12;i++){await wait(10);take('post-resume-'+i);}
          }catch(error){p.errors.push(String(error.stack||error));}
          finally{await c.close();p.done=true;}
        };</script>`,
      });
      const point = await value(chrome.page, `(()=>{const r=document.querySelector('#clock').getBoundingClientRect();return {x:r.x+r.width/2,y:r.y+r.height/2};})()`);
      await chrome.page.send('Input.dispatchMouseEvent', { type: 'mousePressed', ...point, button: 'left', clickCount: 1 });
      await chrome.page.send('Input.dispatchMouseEvent', { type: 'mouseReleased', ...point, button: 'left', clickCount: 1 });
      await untilBrowser(chrome.page, 'clockOnly.done', 'bounded actual platform render/output clock diagnostic');
      const evidence = await value(chrome.page, 'clockOnly');
      assert.deepEqual(evidence.errors, []);
      assert.deepEqual(evidence.gesture, { trusted: true, active: true });
      evidence.args = chrome.args; evidence.version = chrome.version;
      await writeFile(process.env.SOURCE_AUDIO_CLOCK_EVIDENCE, JSON.stringify(evidence, null, 2) + '\n');
    } finally { await chrome?.close(); await server.close(); }
  });

test('real Chrome: original C# playlist PCM reaches a trusted-unlocked graph and cancels its actual looping voice on reset',
  { skip: !process.env.SOURCE_AUDIO_ORIGINAL_DATA }, async () => {
    assert(process.env.SOURCE_AUDIO_ORIGINAL_EVIDENCE, 'A new owned original-audio evidence path is required.');
    const root = resolve(process.env.SOURCE_AUDIO_ORIGINAL_DATA);
    const fixture = JSON.parse(await readFile(resolve(root, 'audio-proof.json'), 'utf8'));
    assert.equal(fixture.originalDataManifestSha256, 'fef5f838759a50afdacc4d94b0749d580b1ac90406624c887fc6ea01dc6cfa42');
    assert.equal(fixture.naturalGameProducerClaimed, false);
    const server = await browserServer(null, root);
    let chrome;
    const evidence = { schema: 'actual-original-web-audio-component/v1', naturalGameProducerClaimed: false,
      method: 'Actual original PCM and ordered v2 commands exported by current C# SourceAudioPlayback; not synthetic samples or native DSP.',
      originalDataManifestSha256: fixture.originalDataManifestSha256, sampleIds: fixture.samples.map(s => s.id) };
    try {
      chrome = await normallySandboxedChrome();
      evidence.args = chrome.args; evidence.version = chrome.version;
      await chrome.page.send('Page.navigate', { url: server.url });
      await untilBrowser(chrome.page, 'window.originalAudioProof?.ready', 'actual original C# resource loading');
      const point = await value(chrome.page, `(()=>{const r=document.querySelector('#unlock').getBoundingClientRect();return {x:r.x+r.width/2,y:r.y+r.height/2};})()`);
      await chrome.page.send('Input.dispatchMouseEvent', { type: 'mousePressed', ...point, button: 'left', clickCount: 1 });
      await chrome.page.send('Input.dispatchMouseEvent', { type: 'mouseReleased', ...point, button: 'left', clickCount: 1 });
      await untilBrowser(chrome.page, 'originalAudioProof.phase==="playing"', 'actual original loop scheduling');
      evidence.gesture = await value(chrome.page, 'originalAudioProof.gesture');
      assert.deepEqual(evidence.gesture, { trusted: true, active: true, type: 'click' });
      evidence.playing = await untilBrowser(chrome.page, `(()=>{const s=originalAudioProof.snapshot();return s.signal?.rms>0.00001?s:false;})()`,
        'nonzero real original waveform through the actual AudioContext analyser');
      assert.equal(evidence.playing.compiledOriginalPrograms, true);
      assert.equal(evidence.playing.contextState, 'running');
      assert(evidence.playing.nodes.some(n => n.loop && n.bank === 'main1' && n.playlistId === 0 &&
        n.loopStartSample > 0 && n.loopEndSample > n.loopStartSample));
      evidence.reset = await value(chrome.page, 'originalAudioProof.reset()');
      assert.equal(evidence.reset.nodes.length, 0);
      evidence.closed = await value(chrome.page, 'originalAudioProof.close()');
      assert.equal(evidence.closed.contextState, 'closed');
      assert.deepEqual(await value(chrome.page, 'originalAudioProof.errors'), []);
      evidence.status = 'scoped-actual-original-pcm-browser-pass';
    } catch (error) {
      evidence.status = 'failed'; evidence.error = String(error.stack ?? error); throw error;
    } finally {
      await mkdir(dirname(process.env.SOURCE_AUDIO_ORIGINAL_EVIDENCE), { recursive: true });
      await writeFile(process.env.SOURCE_AUDIO_ORIGINAL_EVIDENCE, JSON.stringify(evidence, null, 2) + '\n');
      await chrome?.close(); await server.close();
    }
  });

test('real Chrome: trusted unlock, synthetic PCM graph samples, source pause/epoch/preemption and legacy no-AudioContext startup',
  { skip: process.env.SOURCE_AUDIO_BROWSER !== '1', timeout: 240000 }, async () => {
    const output = process.env.SOURCE_AUDIO_BROWSER_EVIDENCE;
    assert(output, 'Real browser proof requires an explicit new persistent evidence path.');
    const evidence = { schema: 'source-player-c6-web-audio-browser/v1', status: 'running', syntheticOnly: true,
      originalBanksLoaded: false, nativeDspEquivalent: false, observations: [], errors: [], exceptions: [] };
    let server, chrome;
    const observe = async name => {
      const observation = await value(chrome.page, 'audioProof.observe()');
      evidence.observations.push({ name, ...observation }); return observation;
    };
    try {
      server = await browserServer(process.env.SOURCE_AUDIO_APP_BUNDLE);
      chrome = await normallySandboxedChrome();
      evidence.url = server.url; evidence.chrome = { version: chrome.version, args: chrome.args, pid: chrome.pid };
      chrome.page.on('Runtime.exceptionThrown', event => evidence.exceptions.push(event.exceptionDetails));
      await chrome.page.send('Page.navigate', { url: server.url });
      await chrome.page.send('Page.bringToFront');
      await untilBrowser(chrome.page, 'globalThis.audioProof?.ready', 'synthetic module initialization');
      evidence.preUnlock = await value(chrome.page, '({phase:audioProof.phase,errors:audioProof.beforeUnlock,noContext:audioProof.noPreUnlockContext})');
      assert.equal(evidence.preUnlock.noContext, true);
      assert.equal(evidence.preUnlock.phase, 'locked');
      assert(evidence.preUnlock.errors.every(message => /before|gesture/.test(message)));
      const button = await value(chrome.page, `(() => {const r=document.querySelector('#unlock').getBoundingClientRect();return {x:r.x+r.width/2,y:r.y+r.height/2};})()`);
      await chrome.page.send('Input.dispatchMouseEvent', { type: 'mousePressed', button: 'left', clickCount: 1, ...button });
      await chrome.page.send('Input.dispatchMouseEvent', { type: 'mouseReleased', button: 'left', clickCount: 1, ...button });
      await untilBrowser(chrome.page, 'audioProof.phase==="playing" || audioProof.phase==="failed"', 'real trusted user-gesture unlock');
      evidence.gesture = await value(chrome.page, 'audioProof.gesture');
      assert.deepEqual(evidence.gesture, { type: 'click', trusted: true, active: true });
      assert.equal(await value(chrome.page, 'audioProof.phase'), 'playing', JSON.stringify(await value(chrome.page, 'audioProof.errors')));
      evidence.graph = await value(chrome.page, 'audioProof.graph');
      assert.equal(evidence.graph.context, 'AudioContext');
      assert.equal(evidence.graph.source, 'AudioBufferSourceNode');
      assert.equal(evidence.graph.gain, 'GainNode');
      assert.equal(evidence.graph.panner, 'StereoPannerNode');
      assert.equal(evidence.graph.bufferRate, RATE); assert.equal(evidence.graph.bufferChannels, 1);
      assert.equal(evidence.graph.bufferSamples, RATE * 3);
      assert.deepEqual(evidence.graph.firstFloats, evidence.graph.firstPcm.map(sample => sample / 32768));
      assert.equal(evidence.graph.sourceStart, evidence.graph.anchor + project(1) / RATE);
      await sleep(200);
      const left = await observe('real-left-channel-synthetic-pcm');
      assert(left.rms[0] > .12 && left.rms[0] < .22 && left.rms[1] < .001, 'Real graph must emit the generated sine only on the supplied left pan.');
      assert.equal(left.snapshot.sampleCount, 1, 'Duplicate immutable upload must not allocate another resource.');
      const paused = await value(chrome.page, 'audioProof.pause()');
      evidence.observations.push({ name: 'source-active-false', ...paused });
      await sleep(250);
      const frozen = await observe('source-clock-and-nodes-frozen');
      assert.equal(frozen.currentTime, paused.currentTime);
      assert.equal(frozen.snapshot.contextState, 'suspended');
      assert.equal(frozen.snapshot.startedVoices, 1);
      await value(chrome.page, 'audioProof.resume()');
      await sleep(100);
      const resumed = await observe('source-resume-no-replay');
      assert(resumed.currentTime > frozen.currentTime && resumed.rms[0] > .12);
      assert.equal(resumed.snapshot.startedVoices, 1);
      await value(chrome.page, 'audioProof.adjust()'); await sleep(150);
      const right = await observe('real-adjusted-gain-and-right-pan');
      assert(right.rms[1] > .065 && right.rms[1] < .11 && right.rms[0] < .001);
      await value(chrome.page, 'audioProof.deleteBank()'); await sleep(80);
      const deleted = await observe('bank-delete-keeps-active-pcm');
      assert(deleted.rms[1] > .065 && deleted.snapshot.nodes.length === 1);
      await value(chrome.page, 'audioProof.stop()'); await sleep(180);
      const stopped = await observe('source-stop-all-real-silence');
      assert(stopped.rms.every(rms => rms < .001));
      assert.equal(stopped.snapshot.nodes.length, 0);
      assert.equal(stopped.snapshot.reservedTracks, 0);
      await value(chrome.page, 'audioProof.resetPaused()');
      const stale = await value(chrome.page, 'audioProof.stale()');
      assert.equal(stale.accepted, false);
      assert.equal(stale.snapshot.sampleCount, 1);
      await value(chrome.page, 'audioProof.resume()');
      const preempt = await value(chrome.page, 'audioProof.preempt()');
      await sleep(220);
      const replacement = await observe('real-preemption-old-onended-keeps-replacement');
      assert.deepEqual(replacement.snapshot.tracks, [preempt.replacementVoice, null, null, null, null, null]);
      assert.equal(replacement.snapshot.nodes.length, 1);
      assert(replacement.rms[0] > .12);
      await value(chrome.page, 'audioProof.resetPaused()');
      await sleep(120);
      await value(chrome.page, 'audioProof.resume()'); await sleep(150);
      const reset = await observe('epoch-reset-cancels-all-old-audio-no-burst-on-resume');
      assert(reset.rms.every(rms => rms < .001));
      assert.equal(reset.snapshot.nodes.length, 0);
      assert.equal(reset.snapshot.sampleCount, 1);
      assert.equal(reset.snapshot.startedVoices, 3);
      evidence.naturalStart = await value(chrome.page, 'audioProof.natural()');
      assert.equal(evidence.naturalStart.sampleCount, 1440);
      assert.equal(evidence.naturalStart.duration, 1440 / RATE);
      await sleep(180);
      evidence.naturalCompletion = await value(chrome.page, 'audioProof.naturalCompleted()');
      assert.equal(evidence.naturalCompletion.snapshot.nodes.length, 0);
      assert(evidence.naturalCompletion.rms.every(rms => rms < .001));
      assert.equal(evidence.naturalCompletion.natural.trustedEnded, true);
      assert(evidence.naturalCompletion.natural.endedAt >=
        evidence.naturalStart.startTime + evidence.naturalStart.duration);
      evidence.bindingError = await value(chrome.page, 'audioProof.failBinding()');
      assert.match(evidence.bindingError.error, /PCM sample binding/);
      evidence.closed = await value(chrome.page, 'audioProof.close()');
      assert.equal(evidence.closed.contextState, 'closed');
      assert.match(evidence.closed.disposedError, /after disposal/);
      assert.deepEqual(evidence.closed.snapshot.cleanupErrors, []);
      evidence.errors = await value(chrome.page, 'audioProof.errors');
      assert.deepEqual(evidence.errors, []);
      assert.deepEqual(evidence.exceptions, []);

      if (process.env.SOURCE_AUDIO_APP_BUNDLE) {
        const receiptBytes = await readFile(resolve(process.env.SOURCE_AUDIO_APP_BUNDLE, 'build-info.json'));
        const receipt = JSON.parse(receiptBytes);
        const { identifier } = await chrome.page.send('Page.addScriptToEvaluateOnNewDocument', {
          source: 'Object.defineProperty(globalThis,"AudioContext",{value:undefined,configurable:true});',
        });
        await chrome.page.send('Page.navigate', { url: new URL('app/', server.url).href });
        await chrome.page.send('Page.bringToFront');
        const startup = await untilBrowser(chrome.page,
          `(() => { const snapshot=globalThis.sourcePlayer?.snapshot();
            return snapshot?.ready || snapshot?.failed || globalThis.sourcePlayerLoader?.failed ?
              {snapshot,status:document.querySelector('#status')?.textContent} : false; })()`,
          'actual emitted default C# browser app without AudioContext', 180000);
        const legacy = startup.snapshot;
        assert(legacy && !legacy.failed, startup.status);
        assert.equal(legacy.ready, true, 'Actual approved default must still reach its existing portable-ready gate.');
        assert.equal(legacy.failed, false);
        assert.equal(legacy.audioReceiver.contextState, 'not-created');
        assert.equal(legacy.audioReceiver.unlocked, false);
        assert.equal(await value(chrome.page, 'typeof AudioContext'), 'undefined');
        assert.equal(legacy.state.audio.decoded, null, 'Approved default must not silently acquire a bank admission.');
        assert.equal(legacy.state.manifestSha256, receipt.dataManifestSha256);
        evidence.defaultApp = { receiptSha256: digest(receiptBytes), profile: receipt.profile,
          manifestSha256: receipt.dataManifestSha256, snapshot: legacy,
          contextUnavailable: true, nativeUnityBuild: false };
        assert(server.requests.includes('/app/main.js') && server.requests.includes('/app/input.js') &&
          server.requests.some(path => path.startsWith('/app/_framework/SourcePlayer.Browser') && path.endsWith('.wasm')) &&
          server.requests.some(path => path.startsWith('/app/_framework/SourcePlayer.Core') && path.endsWith('.wasm')));
        await chrome.page.send('Page.removeScriptToEvaluateOnNewDocument', { identifier });
      }
      evidence.status = 'passed';
    } catch (error) {
      evidence.status = 'failed'; evidence.error = String(error.stack || error); throw error;
    } finally {
      await chrome?.close();
      if (server) { evidence.requests = server.requests; await server.close(); }
      evidence.ownedHelpersStopped = true;
      await mkdir(dirname(output), { recursive: true });
      await writeFile(output, JSON.stringify(evidence, null, 2) + '\n');
    }
  });
