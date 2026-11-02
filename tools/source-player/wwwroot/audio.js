const RATE = 31250, MAX_BYTES = 64 * 1024 * 1024, LOOK_AHEAD = .1;
const POLICY = 'portable-source-qualified-pcm-one-shot/v1';
const COMPILED_POLICY = 'portable-compiled-original-playlists/v2';
const PREGAME_POLICY = 'portable-original-pregame-continuous-source/v3';
const RESERVED_POLICY = 'portable-original-pregame-continuous-source/v4';
// Measured011/clock-diagnostic qualification floors, including startup outputLatency=0.
const RENDER_BATCH_FLOOR = 2048 / 48000, RENDER_REFILL_FLOOR = .128;
const SAMPLE_KEYS = 'schema format sha256 sampleCount mixAlreadyBaked admission nativeDspEquivalent'.split(' ');
const BATCH_KEYS = 'schema policy sampleRate sourceTicksPerSecond epoch sourceTick commands'.split(' ');
const COMMAND_KEYS = ('sequence epoch kind sourceTick atSample outputSequence subcallOrder voiceId track trackMask ' +
  'bank playlistId packageSha256 sourceGitBlob programHex sampleId sampleCount loop rawVolume rawPan priorityWord ' +
  'volumeByte panByte portableGain portablePan active reason').split(' ');
const COMPILED_COMMAND_KEYS = [...COMMAND_KEYS, 'loopStartSample', 'loopEndSample'];
const PREGAME_COMMAND_KEYS = [...COMPILED_COMMAND_KEYS, 'mixPoints'];
const HASH = /^[0-9a-f]{64}$/, BLOB = /^[0-9a-f]{40}$/;
const K = new Uint32Array([
  0x428a2f98, 0x71374491, 0xb5c0fbcf, 0xe9b5dba5, 0x3956c25b, 0x59f111f1, 0x923f82a4, 0xab1c5ed5,
  0xd807aa98, 0x12835b01, 0x243185be, 0x550c7dc3, 0x72be5d74, 0x80deb1fe, 0x9bdc06a7, 0xc19bf174,
  0xe49b69c1, 0xefbe4786, 0x0fc19dc6, 0x240ca1cc, 0x2de92c6f, 0x4a7484aa, 0x5cb0a9dc, 0x76f988da,
  0x983e5152, 0xa831c66d, 0xb00327c8, 0xbf597fc7, 0xc6e00bf3, 0xd5a79147, 0x06ca6351, 0x14292967,
  0x27b70a85, 0x2e1b2138, 0x4d2c6dfc, 0x53380d13, 0x650a7354, 0x766a0abb, 0x81c2c92e, 0x92722c85,
  0xa2bfe8a1, 0xa81a664b, 0xc24b8b70, 0xc76c51a3, 0xd192e819, 0xd6990624, 0xf40e3585, 0x106aa070,
  0x19a4c116, 0x1e376c08, 0x2748774c, 0x34b0bcb5, 0x391c0cb3, 0x4ed8aa4a, 0x5b9cca4f, 0x682e6ff3,
  0x748f82ee, 0x78a5636f, 0x84c87814, 0x8cc70208, 0x90befffa, 0xa4506ceb, 0xbef9a3f7, 0xc67178f2,
]);

function require(condition, detail) {
  if (!condition) throw new Error('SOURCE_AUDIO: ' + detail);
}
function integer(value, low = 0, high = Number.MAX_SAFE_INTEGER) {
  return Number.isSafeInteger(value) && value >= low && value <= high;
}
function exactObject(value, keys, label) {
  require(value !== null && typeof value === 'object' && !Array.isArray(value) &&
    Object.keys(value).length === keys.length && keys.every(key => Object.hasOwn(value, key)), label + ' fields/schema mismatch.');
}
function parse(json, keys, label) {
  require(typeof json === 'string', label + ' must be JSON text.');
  let value;
  try { value = JSON.parse(json); }
  catch (error) { throw new Error('SOURCE_AUDIO: invalid ' + label + ' JSON.', { cause: error }); }
  exactObject(value, keys, label);
  return value;
}
function at(tick, ticksPerSecond) {
  const sample = BigInt(tick) * BigInt(RATE) / BigInt(ticksPerSecond);
  require(sample <= BigInt(Number.MAX_SAFE_INTEGER), 'source sample clock exceeds exact browser integers.');
  return Number(sample);
}
function projection(command) {
  require(Number.isFinite(command.portableGain) && Number.isFinite(command.portablePan) &&
    command.portableGain >= 0 && command.portableGain <= 1 && command.portablePan >= -1 && command.portablePan <= 1 &&
    Math.abs(command.portableGain - command.volumeByte / 255) < 1e-12 &&
    Math.abs(command.portablePan - (command.panByte - 127) / (command.panByte <= 127 ? 127 : 128)) < 1e-12,
  'gain/pan do not match the declared portable projection.');
}
function bankName(value) {
  require(typeof value === 'string' && /^[A-Za-z0-9][A-Za-z0-9_.-]{0,63}$/.test(value), 'invalid bank identity.');
}
function contextOperation(operation, action) {
  let timer;
  const deadline = new Promise((_, reject) => {
    timer = setTimeout(() => reject(new Error('SOURCE_AUDIO: context did not ' + action + ' within five seconds.')), 5000);
  });
  return Promise.race([operation, deadline]).finally(() => clearTimeout(timer));
}

function schedulingPolicy(context, reserved) {
  const estimate = value => Number.isFinite(value) && value >= 0 ? value : null;
  const baseEstimate = estimate(context.baseLatency), outputEstimate = estimate(context.outputLatency);
  const batch = Math.max(RENDER_BATCH_FLOOR, baseEstimate ?? 0);
  const refill = Math.max(RENDER_REFILL_FLOOR, outputEstimate ?? 0);
  // Scheduling lead within one render segment, not a lifetime allowance for pause skew.
  const renderReserve = reserved ? 2 * (batch + refill) : 0;
  return Object.freeze({ policy: reserved ? 'portable-pause-segment-transport/v1' : 'legacy-100ms/v1',
    deliveryAllowanceSeconds: LOOK_AHEAD, renderReserveSeconds: renderReserve,
    initialLeadSeconds: LOOK_AHEAD + renderReserve,
    qualifiedRenderBatchSeconds: reserved ? batch : 0, qualifiedRefillSeconds: reserved ? refill : 0,
    baseLatencyEstimateSeconds: baseEstimate, outputLatencyEstimateSeconds: outputEstimate,
    estimatesAreUpperBounds: false,
    qualificationScope: reserved ? 'Strict active-segment scheduling; public pauses fence and hold the original logical media sample.' : 'Historical100ms policy.' });
}

function sameEnvelopeFuture(before, after, now) {
  if (!before?.length || !after?.length) return null;
  let a = 0, b = 0;
  const samples = new Set([now]);
  for (const point of [...before, ...after]) if (point.atSample > now) samples.add(point.atSample);
  for (const sample of [...samples].sort((a, b) => a - b)) {
    while (a + 1 < before.length && before[a + 1].atSample <= sample) a++;
    while (b + 1 < after.length && after[b + 1].atSample <= sample) b++;
    if (before[a].gain !== after[b].gain) return false;
  }
  return true;
}

// AudioSample is a synchronous WASM import; SubtleCrypto cannot finish before the next command import.
function pcmHash(bytes) {
  const padded = new Uint8Array(Math.ceil((bytes.length + 9) / 64) * 64);
  padded.set(bytes); padded[bytes.length] = 128;
  const view = new DataView(padded.buffer), bits = bytes.length * 8;
  view.setUint32(padded.length - 8, Math.floor(bits / 0x100000000));
  view.setUint32(padded.length - 4, bits >>> 0);
  const h = new Uint32Array([0x6a09e667, 0xbb67ae85, 0x3c6ef372, 0xa54ff53a,
    0x510e527f, 0x9b05688c, 0x1f83d9ab, 0x5be0cd19]), w = new Uint32Array(64);
  const rotate = (value, n) => (value >>> n) | (value << (32 - n));
  for (let block = 0; block < padded.length; block += 64) {
    for (let i = 0; i < 16; i++) w[i] = view.getUint32(block + i * 4);
    for (let i = 16; i < 64; i++) {
      const a = w[i - 15], b = w[i - 2];
      w[i] = w[i - 16] + (rotate(a, 7) ^ rotate(a, 18) ^ (a >>> 3)) +
        w[i - 7] + (rotate(b, 17) ^ rotate(b, 19) ^ (b >>> 10));
    }
    let [a, b, c, d, e, f, g, j] = h;
    for (let i = 0; i < 64; i++) {
      const t = (j + (rotate(e, 6) ^ rotate(e, 11) ^ rotate(e, 25)) +
        ((e & f) ^ (~e & g)) + K[i] + w[i]) >>> 0;
      const u = ((rotate(a, 2) ^ rotate(a, 13) ^ rotate(a, 22)) + ((a & b) ^ (a & c) ^ (b & c))) >>> 0;
      j = g; g = f; f = e; e = (d + t) >>> 0; d = c; c = b; b = a; a = (t + u) >>> 0;
    }
    for (const [i, value] of [a, b, c, d, e, f, g, j].entries()) h[i] += value;
  }
  return Array.from(h, value => value.toString(16).padStart(8, '0')).join('');
}

function initialState() {
  return { epoch: -1, sequence: 0, sourceTick: 0, batchTick: 0, ticksPerSecond: 0, active: true,
    reserved: 0, banks: new Map(), bankPins: new Map(), programs: new Map(), tracks: Array(6).fill(null),
    lastVoice: 0, outputSequence: 0, subcall: -1, compiled: false, continuous: false, reservedTiming: false,
    sourceOriginTick: 0, clockStarted: false };
}

export class SourceAudioReceiver {
  constructor({ onFailure = error => queueMicrotask(() => { throw error; }), createContext } = {}) {
    this.createContext = createContext ?? (() => {
      require(typeof globalThis.AudioContext === 'function', 'Web Audio AudioContext is unavailable.');
      return new AudioContext({ latencyHint: 'interactive' });
    });
    this.onFailure = onFailure;
    this.context = null; this.output = null; this.unlocked = false; this.closed = false;
    this.failure = null; this.samples = new Map(); this.sampleBytes = 0;
    this.model = initialState(); this.nodes = new Map(); this.tracks = Array(6).fill(null);
    this.anchor = 0; this.desiredActive = true; this.pending = 0; this.transition = Promise.resolve();
    this.started = 0; this.ended = 0; this.cleanupErrors = [];
    this.contextTransitions = [];
    this.inFlightCommand = null; this.faultingCommand = null;
    this.batchContextTime = null; this.batchIndex = -1; this.batchSourceTick = null;
    this.timing = null; this.clockStartContextTime = null; this.deadlineContextTime = null; this.deadlineSample = null;
    this.transport = null; this.deferred = [];
    this.stateChanged = () => {
      if (this.closed || !this.unlocked || this.pending || this.failure) return;
      const expected = this.desiredActive ? 'running' : 'suspended';
      if (this.context.state !== expected)
        this.report(new Error(`SOURCE_AUDIO: context is ${this.context.state}, expected ${expected}; explicit restart/unlock required.`));
    };
  }

  async unlock(event) {
    require(!this.closed, 'receiver is disposed.');
    require(event instanceof Event && event.isTrusted === true && ['click', 'pointerup', 'keydown'].includes(event.type) &&
      globalThis.navigator?.userActivation?.isActive === true, 'a real current public user gesture is required for audio unlock.');
    if (this.unlocked) {
      require(this.ready, 'the unlocked context is not ready; source active/reset commands own suspension.');
      return;
    }
    require(!this.context, 'an earlier audio unlock failed; create a new receiver, not a queued retry.');
    try {
      this.context = this.createContext();
      require(this.context.state !== 'closed', 'audio context is closed.');
      await contextOperation(this.context.resume(), 'resume');
      require(this.context.state === 'running', 'audio context did not resume; no decoded composition may start.');
      this.output = this.context.createGain();
      if (typeof this.context.createAnalyser === 'function') {
        this.analyser = this.context.createAnalyser(); this.analyser.fftSize = 2048;
        this.output.connect(this.analyser); this.analyser.connect(this.context.destination);
      } else this.output.connect(this.context.destination);
      this.unlocked = true;
      this.context.addEventListener('statechange', this.stateChanged);
    } catch (error) {
      this.fault(error);
      await this.close();
      throw error;
    }
  }

  get ready() {
    return this.unlocked && !this.closed && !this.failure && this.pending === 0 && this.context.state === 'running';
  }

  get frameReady() {
    this.healthy();
    if (this.pending) return false;
    this.checkContext();
    return true;
  }

  sourceElapsed(epoch) {
    if (!this.unlocked || this.closed || !this.model.clockStarted || this.model.epoch !== epoch) return -1;
    if (this.transport) return (this.logicalPlaybackSample() -
      at(this.model.sourceOriginTick, this.model.ticksPerSecond)) / RATE;
    return this.context.currentTime - this.anchor - at(this.model.sourceOriginTick, this.model.ticksPerSecond) / RATE +
      this.timing.initialLeadSeconds;
  }

  logicalPlaybackSample() {
    const transport = this.transport;
    if (transport.phase === 'unbound') return transport.startSample;
    if (transport.phase === 'paused' || transport.phase === 'resuming') return transport.frozenSample;
    const sample = Math.max(transport.startSample, (this.context.currentTime - this.anchor) * RATE);
    return transport.phase === 'pausing' ? Math.min(sample, transport.frozenSample) : sample;
  }

  clockObservation() {
    const output = typeof this.context?.getOutputTimestamp === 'function' ? this.context.getOutputTimestamp() : null;
    return {
      currentTime: this.context?.currentTime ?? null, hostMilliseconds: performance.now(),
      outputTimestamp: output ? { contextTime: output.contextTime, performanceTime: output.performanceTime } : null,
      baseLatency: this.context?.baseLatency ?? null, outputLatency: this.context?.outputLatency ?? null,
    };
  }

  healthy() {
    require(!this.closed, 'receiver is disposed.');
    require(this.unlocked, 'audio was submitted before user-gesture unlock; pre-unlock calls are never queued.');
    if (this.failure) throw this.failure;
  }

  checkContext() {
    require(this.context.state !== 'closed', 'audio context is closed.');
    if (!this.pending) require(this.context.state === (this.desiredActive ? 'running' : 'suspended'),
      'audio context is unavailable/unresumed or externally suspended.');
  }

  sample(id, sampleRate, channels, pcm16Le, json) {
    try {
      this.healthy(); this.checkContext();
      const meta = parse(json, SAMPLE_KEYS, 'sample metadata');
      require(meta.schema === 'source-audio-sample/v1' && meta.format === 'pcm-s16le' &&
        meta.mixAlreadyBaked === true && meta.nativeDspEquivalent === false &&
        typeof meta.admission === 'string' && meta.admission.trim().length > 0, 'unsupported PCM metadata/admission.');
      require(sampleRate === RATE && channels === 1 && integer(meta.sampleCount, 1, MAX_BYTES / 2) &&
        pcm16Le instanceof Uint8Array && pcm16Le.byteLength === meta.sampleCount * 2,
      'PCM must be exact-sized mono signed PCM16LE at 31250Hz.');
      require(typeof meta.sha256 === 'string' && HASH.test(meta.sha256) && id === 'pcm16-' + meta.sha256 &&
        pcmHash(pcm16Le) === meta.sha256, 'PCM sample hash/identity mismatch.');
      const cached = this.samples.get(id);
      if (cached) {
        require(SAMPLE_KEYS.every(key => cached.meta[key] === meta[key]), 'cached PCM metadata mismatch.');
        return;
      }
      require(this.samples.size < 4096 && this.sampleBytes + pcm16Le.byteLength <= MAX_BYTES, 'PCM resource cache exceeds the admitted bound.');
      const buffer = this.context.createBuffer(channels, meta.sampleCount, sampleRate);
      const floats = buffer.getChannelData(0), bytes = new DataView(pcm16Le.buffer, pcm16Le.byteOffset, pcm16Le.byteLength);
      for (let i = 0; i < floats.length; i++) floats[i] = bytes.getInt16(i * 2, true) / 32768;
      this.samples.set(id, { buffer, meta: Object.freeze(meta) });
      this.sampleBytes += pcm16Le.byteLength;
    } catch (error) { this.fault(error); throw error; }
  }

  validate(batch) {
    const reservedTiming = batch.schema === 'source-audio-playback/v4' && batch.policy === RESERVED_POLICY;
    const continuous = reservedTiming || batch.schema === 'source-audio-playback/v3' && batch.policy === PREGAME_POLICY;
    const compiled = continuous || batch.schema === 'source-audio-playback/v2' && batch.policy === COMPILED_POLICY;
    require((compiled || batch.schema === 'source-audio-playback/v1' && batch.policy === POLICY) && batch.sampleRate === RATE &&
      integer(batch.sourceTicksPerSecond, 1, 1000) && integer(batch.epoch) && integer(batch.sourceTick) &&
      Array.isArray(batch.commands) && batch.commands.length > 0 && batch.commands.length <= 512,
    'unsupported batch schema/policy/clock/size.');
    at(batch.sourceTick, batch.sourceTicksPerSecond);
    if (batch.epoch < this.model.epoch) return null;
    const state = { ...this.model, banks: new Map(this.model.banks), bankPins: new Map(this.model.bankPins),
      programs: new Map(this.model.programs), tracks: this.model.tracks.slice() };
    if (this.failure) require(batch.epoch > state.epoch && batch.commands[0]?.kind === 'reset',
      'receiver fault requires a fresh reset epoch; commands are never retried.');
    if (batch.epoch === state.epoch) require(compiled === state.compiled && continuous === state.continuous &&
      reservedTiming === state.reservedTiming &&
      batch.sourceTicksPerSecond === state.ticksPerSecond &&
      batch.sourceTick >= state.batchTick, 'batch source clock changed or rewound.');
    else require(batch.commands[0]?.kind === 'reset', 'a new epoch must begin with reset.');
    for (const command of batch.commands) {
      exactObject(command, continuous ? PREGAME_COMMAND_KEYS : compiled ? COMPILED_COMMAND_KEYS : COMMAND_KEYS, 'command');
      if (continuous) require(Array.isArray(command.mixPoints) && command.mixPoints.length <= 1024 &&
        (command.kind === 'program-mix' ? command.mixPoints.length > 0 : command.mixPoints.length === 0),
      'invalid original programme mixer envelope.');
      require(integer(command.sequence, 1) && command.sequence > state.sequence && command.epoch === batch.epoch &&
        integer(command.sourceTick) && command.sourceTick <= batch.sourceTick && integer(command.atSample) &&
        integer(command.outputSequence) && integer(command.subcallOrder, 0, 0x7fffffff) &&
        integer(command.voiceId) && integer(command.track, -1, 5) && integer(command.trackMask, 0, 63) &&
        integer(command.playlistId, -1, 0x7fffffff) && integer(command.sampleCount, 0, MAX_BYTES / 2) &&
        integer(command.rawVolume, 0, 0xffffffff) && integer(command.rawPan, 0, 0xffffffff) &&
        integer(command.priorityWord, 0, 0xffffffff) && integer(command.volumeByte, 0, 255) &&
        integer(command.panByte, 0, 255) && Number.isFinite(command.portableGain) &&
        Number.isFinite(command.portablePan) && typeof command.active === 'boolean' &&
        (compiled ? typeof command.loop === 'boolean' && integer(command.loopStartSample, 0, MAX_BYTES / 2) &&
          integer(command.loopEndSample, 0, MAX_BYTES / 2) && (command.loop ?
            command.loopStartSample < command.loopEndSample && command.loopEndSample === command.sampleCount :
            command.loopStartSample === 0 && command.loopEndSample === 0) : command.loop === false),
      'invalid command types, epoch, sequence, track or scalar bounds.');
      for (const key of ['bank', 'packageSha256', 'sourceGitBlob', 'programHex', 'sampleId', 'reason'])
        require(command[key] === null || typeof command[key] === 'string', 'invalid nullable command ' + key + '.');
      if (command.kind === 'reset') {
        const activation = continuous && command.reason === 'original-mainproc-accepted-start-stop-all';
        require(command === batch.commands[0] && batch.epoch > state.epoch &&
          (activation ? command.sourceTick > 0 && command.atSample === at(command.sourceTick, batch.sourceTicksPerSecond) :
            command.sourceTick === 0 && command.atSample === 0) &&
          command.trackMask === 63, 'invalid/duplicate epoch reset.');
        state.epoch = batch.epoch; state.sourceTick = command.sourceTick;
        state.sourceOriginTick = command.sourceTick; state.banks.clear(); state.tracks.fill(null);
        state.reserved = 0; state.active = command.active; state.ticksPerSecond = batch.sourceTicksPerSecond;
        state.outputSequence = 0; state.subcall = -1; state.compiled = compiled; state.continuous = continuous;
        state.reservedTiming = reservedTiming;
        state.clockStarted = false;
      } else {
        require(command.epoch === state.epoch && command.sourceTick >= state.sourceTick, 'command source clock rewound.');
        require(!reservedTiming || state.active || command.sourceTick === state.sourceTick,
          'paused transport cannot advance the logical source clock.');
        const projected = at(command.sourceTick, state.ticksPerSecond);
        require(command.atSample === projected || command.kind === 'stop' && command.reason === 'completed' &&
          command.atSample <= projected, 'command atSample differs from the exact source clock.');
        switch (command.kind) {
          case 'clock-start':
            require(compiled && state.active && !state.clockStarted && state.outputSequence === 0 &&
              command.outputSequence === 0 && command.sourceTick === state.sourceOriginTick &&
              command.reason === 'first-source-wake-or-output-after-ready-boundary',
            'the ready barrier may start an epoch clock once, before its first actual source output only.');
            state.clockStarted = true;
            break;
          case 'bank-load': {
            bankName(command.bank);
            require(HASH.test(command.packageSha256 ?? '') && BLOB.test(command.sourceGitBlob ?? '') &&
              !state.banks.has(command.bank), 'invalid/duplicate bank load.');
            const pin = command.packageSha256 + ':' + command.sourceGitBlob;
            require(!state.bankPins.has(command.bank) || state.bankPins.get(command.bank) === pin, 'immutable bank identity changed.');
            state.bankPins.set(command.bank, pin); state.banks.set(command.bank, pin);
            break;
          }
          case 'bank-delete':
            bankName(command.bank);
            require(state.banks.delete(command.bank), 'bank delete has no loaded binding.');
            break;
          case 'bank-load-rejected':
            bankName(command.bank);
            require(compiled && state.banks.has(command.bank) && command.reason === 'original-sdk-already-loaded',
              'only the exact original SDK duplicate-load result is supported.');
            break;
          case 'bank-delete-rejected':
            bankName(command.bank);
            require(compiled && !state.banks.has(command.bank) && command.reason === 'original-sdk-bank-not-found',
              'only the exact original SDK missing-bank delete result is supported.');
            break;
          case 'reserved': state.reserved = command.trackMask; break;
          case 'unassigned-playlist':
            require(!reservedTiming || state.clockStarted, 'the v4 ready clock boundary must precede source output.');
            bankName(command.bank);
            require(compiled && state.active && command.outputSequence > 0 &&
              (command.outputSequence > state.outputSequence || command.outputSequence === state.outputSequence &&
                command.subcallOrder > state.subcall) &&
              state.banks.get(command.bank) === command.packageSha256 + ':' + command.sourceGitBlob &&
              integer(command.playlistId) && command.programHex === null && command.sampleId === null &&
              command.sampleCount === 0 && command.voiceId === 0 && command.track === -1 &&
              command.reason === 'original-sdk-unassigned-ffffffff-slot',
            'only an explicitly source-qualified FFFFFFFF sparse slot can have no program/PCM; never an unsupported-program fallback.');
            projection(command);
            state.outputSequence = command.outputSequence; state.subcall = command.subcallOrder;
            break;
          case 'play':
          case 'rejected': {
            require(!reservedTiming || state.clockStarted, 'the v4 ready clock boundary must precede source output.');
            state.clockStarted = true;
            require(state.active && command.outputSequence > 0 &&
              (command.outputSequence > state.outputSequence || command.outputSequence === state.outputSequence &&
                command.subcallOrder > state.subcall), 'source output was suspended, duplicated or reordered.');
            bankName(command.bank);
            require(state.banks.get(command.bank) === command.packageSha256 + ':' + command.sourceGitBlob &&
              integer(command.playlistId) && typeof command.programHex === 'string' &&
              /^(?:[0-9a-fA-F]{2})+$/.test(command.programHex), 'play has no exact bank/program provenance binding.');
            const sample = this.samples.get(command.sampleId);
            require(sample && sample.meta.sampleCount === command.sampleCount, 'play has an unknown/mismatched PCM sample binding.');
            require(command.volumeByte === Math.min(command.rawVolume, 255) && command.panByte === Math.min(command.rawPan, 255),
              'raw source volume/pan clamp mismatch.');
            projection(command);
            const key = command.bank + ':' + command.playlistId + (continuous ? ':' + Number(command.track !== 0) : ''), binding = JSON.stringify([
              command.packageSha256, command.sourceGitBlob, command.programHex, command.sampleId, command.sampleCount,
              ...(compiled ? [command.loopStartSample, command.loopEndSample] : []),
            ]);
            require(!state.programs.has(key) || state.programs.get(key) === binding, 'immutable bank/program/sample binding changed.');
            state.programs.set(key, binding); state.outputSequence = command.outputSequence; state.subcall = command.subcallOrder;
            if (command.kind === 'rejected') {
              require(command.voiceId === 0 && typeof command.reason === 'string' && command.reason.length > 0,
                'rejected allocation must not start a voice.');
            } else {
              require(command.track >= 0 && !state.tracks[command.track] && command.voiceId > state.lastVoice &&
                integer(command.atSample + command.sampleCount), 'play reuses a voice/occupied track or overflows its sample clock.');
              state.lastVoice = command.voiceId;
              state.tracks[command.track] = { voiceId: command.voiceId, bank: command.bank, playlistId: command.playlistId,
                endSample: command.loop ? Infinity : command.atSample + command.sampleCount };
            }
            break;
          }
          case 'program-mix': {
            const voice = state.tracks[0];
            require(continuous && state.active && command.outputSequence === 0 &&
              command.reason === 'original-dcs233-global0-mixer-envelope' && command.track === 0 &&
              voice?.voiceId === command.voiceId && voice.bank === command.bank && voice.playlistId === command.playlistId &&
              command.mixPoints[0].atSample === command.atSample,
            'programme mixer must target its actual live global0 voice, not another track or producer output.');
            let previous = -1;
            for (const point of command.mixPoints) {
              exactObject(point, ['atSample', 'gain'], 'programme mixer point');
              require(integer(point.atSample) && point.atSample >= command.atSample && point.atSample > previous &&
                point.atSample <= command.atSample + 32768 * 240 &&
                Number.isFinite(point.gain) && point.gain >= 0 && point.gain <= 1,
              'programme mixer has an invalid source-derived frame clock/gain.');
              previous = point.atSample;
            }
            break;
          }
          case 'stop':
          case 'adjust': {
            const voice = state.tracks[command.track];
            require(voice && voice.voiceId === command.voiceId, 'stop/adjust does not target the exact track/voice.');
            if (command.kind === 'stop') {
              require(command.bank === voice.bank && command.playlistId === voice.playlistId &&
                typeof command.reason === 'string' && command.reason.length > 0 &&
                (command.reason !== 'completed' || command.atSample === voice.endSample), 'stop resource/natural-duration mismatch.');
              state.tracks[command.track] = null;
            } else projection(command);
            break;
          }
          case 'stop-all':
            require(command.trackMask === 63, 'stop-all must target all six tracks.');
            state.tracks.fill(null); state.reserved = 0;
            break;
          case 'active':
            require(command.active !== state.active && (!reservedTiming ||
              command.reason === 'portable-browser-pause-focus-policy'), 'invalid/duplicate public active transition.');
            state.active = command.active;
            break;
          default: throw new Error('SOURCE_AUDIO: unknown command kind ' + command.kind + '.');
        }
        state.sourceTick = command.sourceTick;
      }
      state.sequence = command.sequence;
    }
    state.batchTick = batch.sourceTick;
    return state;
  }

  commands(json) {
    try {
      require(!this.closed && this.unlocked, 'audio commands arrived before unlock or after disposal; no queue/replay.');
      this.checkContext();
      const batch = parse(json, BATCH_KEYS, 'playback batch'), next = this.validate(batch);
      if (!next) return false;
      const buffers = new Map(batch.commands.filter(command => command.kind === 'play')
        .map(command => [command, this.samples.get(command.sampleId).buffer]));
      this.batchContextTime = this.context.currentTime;
      this.batchSourceTick = batch.sourceTick;
      for (const [index, command] of batch.commands.entries()) {
        this.batchIndex = index;
        this.inFlightCommand = command;
        if (this.pending && this.transport) {
          require(this.deferred.length < 512, 'transport transition command FIFO exceeds its bound; no commands were dropped.');
          this.deferred.push({ command, next, buffer: buffers.get(command), index,
            contextTime: this.batchContextTime, sourceTick: batch.sourceTick });
        } else this.apply(command, next.compiled, next, buffers.get(command));
      }
      this.inFlightCommand = null;
      this.batchIndex = -1;
      this.model = next;
      return true;
    } catch (error) { this.fault(error); throw error; }
  }

  apply(command, compiled = false, next = this.model, buffer = null) {
    if (command.kind === 'reset') {
      this.cancelNodes(); this.failure = null;
      require(this.cleanupErrors.length === 0, 'old nodes could not be canceled; create a new receiver.');
      this.startClock(command.atSample, next.reservedTiming, false);
      this.faultingCommand = null; this.deadlineContextTime = null; this.deadlineSample = null;
      this.output.gain.cancelScheduledValues(0);
      this.output.gain.setValueAtTime(1, this.context.currentTime);
      this.setContextActive(command.active, command);
      return;
    }
    const when = this.anchor + command.atSample / RATE;
    if (command.kind === 'clock-start') {
      require(this.ready, 'epoch clock start requires the genuinely resumed context.');
      this.startClock(command.atSample, next.reservedTiming, true);
    } else if (command.kind === 'play') {
      require(this.ready, 'play requires a resumed context and completed active transition before advancing source time.');
      this.requireDeadline(when, command);
      const voice = { id: command.voiceId, epoch: command.epoch, track: command.track,
        source: null, gain: null, pan: null, when, started: false, stopAt: Infinity,
        bank: command.bank, playlistId: command.playlistId, sampleId: command.sampleId, loop: command.loop,
        buffer: buffer ?? this.samples.get(command.sampleId).buffer, startSample: command.atSample,
        endSample: command.loop ? Infinity : command.atSample + command.sampleCount, stopSample: Infinity,
        loopStartSample: command.loopStartSample ?? 0, loopEndSample: command.loopEndSample ?? 0,
        mixPoints: [{ atSample: command.atSample, gain: command.portableGain }],
        panPoints: [{ atSample: command.atSample, gain: command.portablePan }] };
      this.nodes.set(voice.id, voice); this.tracks[voice.track] = voice;
      this.materialize(voice, voice.startSample, command, false);
      this.started++;
    } else if (command.kind === 'program-mix') {
      this.requireTransportDeadline(when, command);
      const voice = this.nodes.get(command.voiceId);
      if (voice) {
        if (voice.gain) {
          voice.gain.gain.cancelScheduledValues(when);
          for (const point of command.mixPoints) {
            const pointTime = this.anchor + point.atSample / RATE;
            this.requireDeadline(pointTime, command, point.atSample);
            voice.gain.gain.setValueAtTime(point.gain, pointTime);
          }
        }
        voice.mixPoints = command.mixPoints;
      }
    } else if (command.kind === 'stop') {
      const voice = this.nodes.get(command.voiceId);
      if (command.reason !== 'completed' && (this.transport || voice && when < voice.stopAt))
        this.requireTransportDeadline(when, command);
      if (voice) this.stopVoice(voice, when, command.atSample);
      if (this.tracks[command.track]?.id === command.voiceId) this.tracks[command.track] = null;
    } else if (command.kind === 'adjust') {
      const voice = this.nodes.get(command.voiceId);
      if (this.transport || voice) this.requireTransportDeadline(when, command);
      if (voice) {
        if (voice.gain) {
          voice.gain.gain.setValueAtTime(command.portableGain, when);
          this.requireDeadline(when, command);
          voice.pan.pan.setValueAtTime(command.portablePan, when);
        }
        voice.mixPoints = [{ atSample: command.atSample, gain: command.portableGain },
          ...voice.mixPoints.filter(point => point.atSample > command.atSample)];
        voice.panPoints = [{ atSample: command.atSample, gain: command.portablePan }];
      }
    } else if (command.kind === 'stop-all') {
      if (this.transport) this.requireTransportDeadline(when, command);
      for (const voice of this.nodes.values()) {
        if (when < voice.stopAt) this.requireTransportDeadline(when, command);
        this.stopVoice(voice, when, command.atSample);
      }
      this.tracks.fill(null);
    } else if (command.kind === 'active') this.setContextActive(command.active, command);
    else if (command.kind === 'bank-delete' && compiled)
      this.evictBank(command.bank, this.deferred.length ? this.model : next);
  }

  startClock(sample, reserved, bound) {
    this.timing = schedulingPolicy(this.context, reserved);
    this.clockStartContextTime = this.context.currentTime;
    this.anchor = this.clockStartContextTime + this.timing.initialLeadSeconds - sample / RATE;
    this.transport = reserved ? { phase: bound ? 'running' : 'unbound', segmentId: 0,
      startSample: sample, startContextTime: this.anchor + sample / RATE,
      frozenSample: null, boundaryTime: null } : null;
  }

  requireTransportDeadline(when, command) {
    if (this.transport?.phase === 'paused') {
      require(command.atSample >= this.transport.frozenSample, 'paused control precedes the held transport sample.');
      return;
    }
    this.requireDeadline(when, command);
  }

  materialize(voice, sample, command, continuation) {
    sample = Math.max(sample, voice.startSample);
    const when = this.anchor + sample / RATE;
    this.requireDeadline(when, command, sample);
    const source = voice.source = this.context.createBufferSource();
    const gain = voice.gain = this.context.createGain(), pan = voice.pan = this.context.createStereoPanner();
    source.buffer = voice.buffer; source.loop = voice.loop;
    let offset = sample - voice.startSample;
    if (voice.loop) {
      source.loopStart = voice.loopStartSample / RATE; source.loopEnd = voice.loopEndSample / RATE;
      if (offset >= voice.loopEndSample)
        offset = voice.loopStartSample + (offset - voice.loopStartSample) % (voice.loopEndSample - voice.loopStartSample);
    }
    voice.when = this.anchor + voice.startSample / RATE;
    voice.nativeWhen = when; voice.offsetSample = offset; voice.segmentId = this.transport?.segmentId ?? 0;
    for (const [param, points] of [[gain.gain, voice.mixPoints], [pan.pan, voice.panPoints]]) {
      let current = points[0];
      for (const point of points) if (point.atSample <= sample) current = point;
      this.requireDeadline(when, command, sample);
      param.setValueAtTime(current.gain, when);
      for (const point of points) if (point.atSample > sample) {
        const pointTime = this.anchor + point.atSample / RATE;
        this.requireDeadline(pointTime, command, point.atSample);
        param.setValueAtTime(point.gain, pointTime);
      }
    }
    source.connect(gain); gain.connect(pan); pan.connect(this.output);
    voice.fenced = false;
    source.onended = () => { if (!voice.fenced) this.release(voice, source); };
    this.requireDeadline(when, command, sample);
    if (continuation) source.start(when, offset / RATE);
    else source.start(when);
    voice.started = true;
    voice.stopAt = this.anchor + voice.stopSample / RATE;
    if (Number.isFinite(voice.stopSample)) {
      this.requireDeadline(voice.stopAt, command, voice.stopSample);
      source.stop(voice.stopAt);
    }
  }

  requireDeadline(when, command, sample = command.atSample) {
    const current = this.context.currentTime;
    if (when >= current) return;
    this.deadlineContextTime = current; this.deadlineSample = sample;
    throw new Error('SOURCE_AUDIO: sample-clock underrun ' +
      (command.kind === 'program-mix' ? 'in original programme mixer ' : '') +
      `${command.bank}:${command.playlistId} epoch${command.epoch} source${command.sourceTick} ` +
      `lateMs=${((current - when) * 1000).toFixed(3)}; no late acceptance, clamp, queue or replay. Restart required.`);
  }

  evictBank(bank, next) {
    if (next.banks.has(bank)) return;
    const candidates = new Set(), retained = new Set();
    for (const [key, json] of next.programs) {
      const sample = JSON.parse(json)[3];
      if (key.startsWith(bank + ':')) candidates.add(sample);
      else if (next.banks.has(key.slice(0, key.indexOf(':')))) retained.add(sample);
    }
    for (const id of candidates) {
      if (retained.has(id)) continue;
      const sample = this.samples.get(id);
      if (sample) { this.samples.delete(id); this.sampleBytes -= sample.meta.sampleCount * 2; }
    }
  }

  setContextActive(active, command) {
    const expected = active ? 'running' : 'suspended';
    if (!this.pending && this.context.state === expected) { this.desiredActive = active; return; }
    const transport = this.transport;
    const submission = { index: this.batchIndex, contextTime: this.batchContextTime, sourceTick: this.batchSourceTick };
    const fence = !active && transport?.phase === 'running' ? {
      sample: command.atSample, when: this.anchor + command.atSample / RATE,
    } : null;
    if (fence) {
      this.requireDeadline(fence.when, command);
      transport.phase = 'pausing'; transport.frozenSample = fence.sample; transport.boundaryTime = fence.when;
      // Render every already-submitted sample before the fence, and none at/after it.
      // Suspension may complete later; its render-clock overshoot is silence, not media time.
      this.output.gain.setValueAtTime(0, fence.when);
      for (const voice of this.nodes.values()) {
        if (!voice.source) continue;
        voice.fenced = true;
        voice.source.onended = null;
        if (fence.when < voice.stopAt) {
          this.requireDeadline(fence.when, command);
          voice.source.stop(fence.when);
        }
      }
    } else if (active && transport?.phase === 'paused') transport.phase = 'resuming';
    this.desiredActive = active;
    const observed = { active, requestedContextTime: this.context.currentTime,
      requestedHostMilliseconds: performance.now(), sourceTick: this.model.sourceTick, epoch: this.model.epoch,
      requestedClockObservation: this.clockObservation(),
      transportSegment: transport?.segmentId ?? null, boundarySample: fence?.sample ?? transport?.frozenSample ?? null,
      completedContextTime: null, completedHostMilliseconds: null };
    this.contextTransitions.push(observed);
    if (this.contextTransitions.length > 8) this.contextTransitions.shift();
    this.pending++;
    this.transition = this.transition.then(async () => {
      if (this.closed || this.failure) return;
      try {
        if (fence) {
          const deadline = performance.now() + 5000;
          while (this.context.currentTime < fence.when) {
            if (this.closed || this.failure) return;
            require(this.context.state === 'running', 'context stopped before the logical pause fence.');
            require(performance.now() < deadline, 'context did not reach the logical pause fence within five seconds.');
            await new Promise(resolve => setTimeout(resolve, 10));
          }
        }
        if (this.closed || this.failure) return;
        await contextOperation(active ? this.context.resume() : this.context.suspend(), active ? 'resume' : 'suspend');
        if (this.closed || this.failure) return;
        observed.completedContextTime = this.context.currentTime;
        observed.completedHostMilliseconds = performance.now();
        observed.completedClockObservation = this.clockObservation();
        require(this.context.state === expected, 'context failed to ' + (active ? 'resume.' : 'suspend.'));
        if (fence) {
          for (const voice of this.nodes.values()) {
            this.disconnectVoice(voice);
            if (Math.min(voice.endSample, voice.stopSample) <= fence.sample) this.release(voice);
          }
          transport.phase = 'paused';
        } else if (active && transport?.phase === 'resuming') {
          // The old segment actually ended at this source sample. Continue its exact
          // PCM phase and complete future automation, not the context's resume refill.
          const sample = transport.frozenSample;
          this.anchor = this.context.currentTime + this.timing.initialLeadSeconds - sample / RATE;
          transport.segmentId++; transport.startSample = sample;
          transport.startContextTime = this.anchor + sample / RATE;
          this.output.gain.setValueAtTime(1, transport.startContextTime);
          for (const voice of this.nodes.values()) this.materialize(voice, sample, command, true);
          this.requireDeadline(transport.startContextTime, command, sample);
          transport.phase = 'running';
        }
      } catch (error) {
        if (!this.closed) {
          this.inFlightCommand = command; this.batchIndex = submission.index;
          this.batchContextTime = submission.contextTime; this.batchSourceTick = submission.sourceTick;
          try { this.report(error); }
          finally { this.inFlightCommand = null; this.batchIndex = -1; }
        }
      }
    }).then(() => { this.pending--; this.flushDeferred(); });
  }

  flushDeferred() {
    if (this.closed) { this.deferred.length = 0; return; }
    while (!this.pending && this.deferred.length) {
      const { command, next, buffer, index, contextTime, sourceTick } = this.deferred.shift();
      this.inFlightCommand = command; this.batchIndex = index;
      this.batchContextTime = contextTime; this.batchSourceTick = sourceTick;
      try { this.apply(command, next.compiled, next, buffer); }
      catch (error) { this.report(error); break; }
      finally { this.inFlightCommand = null; this.batchIndex = -1; }
    }
  }

  async settled() {
    do { await this.transition; } while (this.pending);
    this.healthy(); this.checkContext();
  }

  stopVoice(voice, when, sample) {
    if (when < voice.stopAt) {
      voice.source?.stop(when);
      voice.stopAt = when; voice.stopSample = sample;
      if (!voice.source && this.transport?.phase === 'paused' && sample <= this.transport.frozenSample) this.release(voice);
    }
  }

  disconnectVoice(voice) {
    if (voice.source) voice.source.onended = null;
    voice.source?.disconnect(); voice.gain?.disconnect(); voice.pan?.disconnect();
    voice.source = null; voice.gain = null; voice.pan = null; voice.started = false;
  }

  release(voice, source = voice.source) {
    if (this.nodes.get(voice.id) !== voice || voice.source !== source) return;
    this.disconnectVoice(voice);
    this.nodes.delete(voice.id); this.ended++;
    if (this.model.epoch === voice.epoch && this.tracks[voice.track]?.id === voice.id) this.tracks[voice.track] = null;
  }

  cancelNodes() {
    for (const voice of this.nodes.values()) {
      if (voice.source) voice.source.onended = null;
      for (const action of [() => { if (voice.started) voice.source.stop(); }, () => voice.source?.disconnect(),
        () => voice.gain?.disconnect(), () => voice.pan?.disconnect()]) {
        try { action(); } catch (error) { this.cleanupErrors.push(String(error)); }
      }
    }
    this.nodes.clear(); this.tracks.fill(null);
  }

  fault(error) {
    if (!this.faultingCommand && this.inFlightCommand) {
      const command = this.inFlightCommand, voice = this.nodes.get(command.voiceId), before = voice?.mixPoints ?? null;
      this.faultingCommand = { schema: 'source-audio-command-fault/v1', command: structuredClone(command),
        kind: command.kind, bank: command.bank ?? voice?.bank ?? null,
        playlistId: command.playlistId >= 0 ? command.playlistId : voice?.playlistId ?? null,
        batchIndex: this.batchIndex, batchSourceTick: this.batchSourceTick, batchContextTime: this.batchContextTime,
        sourceSample: this.deadlineSample ?? command.atSample, epoch: command.epoch, anchor: this.anchor,
        scheduledTime: this.anchor + (this.deadlineSample ?? command.atSample) / RATE,
        deadlineContextTime: this.deadlineContextTime, faultContextTime: this.context.currentTime,
        initialLeadSeconds: this.timing?.initialLeadSeconds ?? null, timing: this.timing,
        transport: this.transport ? { ...this.transport } : null,
        previousEnvelope: before ? structuredClone(before) : null,
        semanticEnvelopeChange: command.kind === 'program-mix' ?
          (before ? !sameEnvelopeFuture(before, command.mixPoints, command.atSample) : null) : command.kind === 'play' ? true : null,
        clock: this.clockObservation() };
    }
    this.failure ??= error;
    this.deferred.length = 0;
    if (this.transport) this.transport.phase = 'faulted';
    this.cancelNodes();
    if (this.output && this.context.state !== 'closed') {
      this.output.gain.cancelScheduledValues(0);
      this.output.gain.setValueAtTime(0, this.context.currentTime);
    }
  }

  report(error) { this.fault(error); this.onFailure(this.failure); }

  snapshot() {
    let signal = null;
    if (this.analyser && !this.closed) {
      const samples = new Float32Array(this.analyser.fftSize);
      this.analyser.getFloatTimeDomainData(samples);
      let sum = 0, peak = 0;
      for (const sample of samples) { sum += sample * sample; peak = Math.max(peak, Math.abs(sample)); }
      signal = { rms: Math.sqrt(sum / samples.length), peak, samples: samples.length,
        contextSampleRate: this.context.sampleRate, originalBufferSampleRate: RATE };
    }
    return { unlocked: this.unlocked, contextState: this.context?.state ?? 'not-created', disposed: this.closed,
      epoch: this.model.epoch, sequence: this.model.sequence, active: this.model.active, pendingTransitions: this.pending,
      reservedTracks: this.model.reserved, sampleCount: this.samples.size, sampleBytes: this.sampleBytes,
      startedVoices: this.started, endedVoices: this.ended, nodes: [...this.nodes.values()].map(voice => ({
        voiceId: voice.id, epoch: voice.epoch, track: voice.track, startTime: voice.when,
        startSample: voice.startSample, stopSample: Number.isFinite(voice.stopSample) ? voice.stopSample : null,
        nativeStartTime: voice.nativeWhen, offsetSample: voice.offsetSample, transportSegment: voice.segmentId,
        bank: voice.bank, playlistId: voice.playlistId, sampleId: voice.sampleId, loop: voice.loop,
        loopStartSample: voice.loopStartSample, loopEndSample: voice.loopEndSample,
      })), tracks: this.tracks.map(voice => voice?.id ?? null), failure: this.failure?.message ?? null,
      banks: [...this.model.banks.keys()], compiledOriginalPrograms: this.model.compiled,
      continuousPreGameClock: this.model.continuous, sourceOriginTick: this.model.sourceOriginTick, signal,
      clock: this.context ? {
        ...this.clockObservation(),
        currentTime: this.context.currentTime, anchor: this.anchor, sourceTick: this.model.sourceTick,
        sourceCoordinate: 'last-delivered-command-not-current-host-source',
        clockStartContextTime: this.clockStartContextTime, timing: this.timing,
        transport: this.transport ? { ...this.transport, queuedCommands: this.deferred.length } : null,
        logicalPlaybackSample: this.transport ? this.logicalPlaybackSample() : null,
        scheduledCurrentSourceTime: this.model.ticksPerSecond ?
          this.anchor + at(this.model.sourceTick, this.model.ticksPerSecond) / RATE : null,
        currentSourceLeadMilliseconds: this.model.ticksPerSecond ?
          (this.anchor + at(this.model.sourceTick, this.model.ticksPerSecond) / RATE - this.context.currentTime) * 1000 : null,
        transitions: this.contextTransitions.map(value => ({ ...value })),
      } : null,
      faultingCommand: this.faultingCommand ? structuredClone(this.faultingCommand) : null,
      cleanupErrors: this.cleanupErrors.slice(), nativeDspEquivalent: false };
  }

  async close() {
    if (this.closed) return;
    this.closed = true; this.deferred.length = 0; this.cancelNodes();
    this.samples.clear(); this.sampleBytes = 0;
    if (this.context) {
      this.context.removeEventListener('statechange', this.stateChanged);
      this.output?.disconnect();
      this.analyser?.disconnect();
      if (this.context.state !== 'closed') await contextOperation(this.context.close(), 'close');
    }
    await this.transition;
    require(this.cleanupErrors.length === 0, 'owned node cleanup failed: ' + this.cleanupErrors.join('; '));
  }
}
