export const CABINET_KEYS = Object.freeze([
  ['KeyW', 'KeyS', 'KeyA', 'KeyD', 'KeyV', 'KeyB', 'KeyN'],
  ['ArrowUp', 'ArrowDown', 'ArrowLeft', 'ArrowRight', 'Delete', 'End', 'PageDown'],
  ['KeyI', 'KeyK', 'KeyJ', 'KeyL', 'KeyU', 'KeyO', 'KeyP'],
  ['KeyT', 'KeyG', 'KeyF', 'KeyH', 'KeyZ', 'KeyX', 'KeyC'],
]);

export class ObservedHostClock {
  constructor() { this.seconds = 0; this.last = null; this.running = false; }
  sample(milliseconds, running) {
    if (!Number.isFinite(milliseconds) || milliseconds < 0 ||
      this.last !== null && milliseconds < this.last)
      throw new Error('Actual observed host time must be finite and monotonic.');
    if (this.last !== null && this.running) this.seconds += (milliseconds - this.last) / 1000;
    this.last = milliseconds; this.running = running;
    return this.seconds;
  }
}

export class SourceCallerTrace {
  constructor(now = () => performance.now()) {
    this.now = now; this.first = []; this.recent = []; this.slowest = []; this.count = 0;
    this.lastPacket = null; this.currentPacket = null; this.presentCount = 0;
  }
  begin(kind, details = {}) {
    const row = { sequence: ++this.count, kind, beginMilliseconds: this.now(),
      endMilliseconds: null, completed: false, ...details };
    if (this.first.length < 16) this.first.push(row);
    this.recent.push(row);
    if (this.recent.length > 128) this.recent.shift();
    return row;
  }
  measure(kind, details, action) {
    const row = this.begin(kind, details);
    try { const result = action(); row.completed = true; return result; }
    finally { this.finish(row); }
  }
  finish(row) {
    row.endMilliseconds = this.now();
    this.slowest.push(row);
    this.slowest.sort((a, b) => (b.endMilliseconds - b.beginMilliseconds) -
      (a.endMilliseconds - a.beginMilliseconds));
    if (this.slowest.length > 16) this.slowest.pop();
  }
  present(status, action) {
    const row = this.begin('js-webgl-present', { packet: null });
    try {
      const snapshot = JSON.parse(status);
      const packet = {
        provenance: 'fresh C# Render packet at synchronous Present; not a live getter',
        epoch: snapshot.epoch, hostTick: snapshot.hostTick, sourceTick: snapshot.sourceTick,
        phase: snapshot.phase, matchPhase: snapshot.matchPhase, playKind: snapshot.playKind,
        cameraSourceTick: snapshot.camera?.sourceTick, cameraSuspended: snapshot.camera?.suspended,
        instructions: snapshot.instructions, realtimeClock: snapshot.realtimeClock,
      };
      row.packet = packet;
      this.lastPacket = packet; this.currentPacket = packet; this.presentCount++;
      const result = action(); row.completed = true; return result;
    } finally { this.finish(row); this.currentPacket = null; }
  }
  stage(kind, action, details = {}) {
    return this.measure(kind, { packet: this.currentPacket,
      provenance: this.currentPacket ? 'current Present packet' : 'no authoritative live source/host available',
      ...details }, action);
  }
  snapshot() {
    const retained = new Set([...this.first, ...this.recent, ...this.slowest]).size;
    return { schema: 'source-player-bounded-caller-drain-diagnostics/v1',
      clockDomain: 'performance.now milliseconds; diagnostic wall time only',
      scope: 'Frame/drain total and JavaScript/synchronous WebGL spans; NOT full managed Render or GPU completion',
      firstCapacity: 16, recentCapacity: 128, slowestCapacity: 16, events: this.count,
      dropped: this.count - retained, presentCount: this.presentCount,
      first16: this.first, last128: this.recent, slowest16: this.slowest, lastRenderedPacket: this.lastPacket };
  }
}

export class SourceDebtDrain {
  constructor({ hasDebt, drain, canRun, fatal, trace, createChannel = () => new MessageChannel() }) {
    this.hasDebt = hasDebt; this.drain = drain; this.canRun = canRun; this.fatal = fatal;
    this.trace = trace; this.generation = 0; this.sequence = 0; this.pending = null; this.closed = false;
    this.channel = createChannel();
    this.channel.port1.onmessage = event => this.receive(event.data);
  }
  request() {
    if (this.closed || this.pending || !this.canRun() || !this.hasDebt()) return;
    this.pending = { generation: this.generation, sequence: ++this.sequence };
    this.channel.port2.postMessage(this.pending);
  }
  receive(token) {
    if (this.closed || !this.pending || token?.generation !== this.pending.generation ||
      token?.sequence !== this.pending.sequence) return;
    this.pending = null;
    if (!this.canRun() || !this.hasDebt()) return;
    try { this.drain(); this.request(); }
    catch (error) { this.cancel('drain-failure'); this.dispose(); this.fatal(error); }
  }
  cancel(reason) {
    this.generation++; this.pending = null;
    this.trace?.measure('cancel-stale-drain', { reason, generation: this.generation }, () => {});
  }
  dispose() {
    if (this.closed) return;
    this.cancel('dispose'); this.closed = true;
    this.channel.port1.onmessage = null;
    this.channel.port1.close(); this.channel.port2.close();
  }
}

export function cabinetPacket(keys, pads, stations) {
  const axes = Array(8).fill(0), buttons = Array(4).fill(0);
  for (const station of stations) {
    const binding = CABINET_KEYS[station], held = i => keys.has(binding[i]);
    let x = Number(held(3)) - Number(held(2)), y = Number(held(1)) - Number(held(0));
    let mask = Number(held(4)) | (Number(held(5)) << 1) | (Number(held(6)) << 2);
    const pad = pads?.[station];
    if (pad?.connected) {
      if (pad.mapping !== 'standard') throw new Error(`Gamepad ${station + 1} has no standard mapping; no guessed cabinet binding.`);
      if (!x && !y) {
        x = pad.axes[0] ?? 0; y = pad.axes[1] ?? 0;
        const down = i => pad.buttons[i]?.pressed === true;
        if (down(14) || down(15)) x = Number(down(15)) - Number(down(14));
        if (down(12) || down(13)) y = Number(down(13)) - Number(down(12));
      }
      if (pad.buttons[0]?.pressed) mask |= 1;
      if (pad.buttons[1]?.pressed) mask |= 2;
      if (pad.buttons[5]?.pressed) mask |= 4;
    }
    if (!Number.isFinite(x) || !Number.isFinite(y) || Math.abs(x) > 1 || Math.abs(y) > 1)
      throw new Error(`Gamepad ${station + 1} returned invalid raw axes.`);
    axes[station * 2] = x; axes[station * 2 + 1] = y; buttons[station] = mask;
  }
  return { axes, buttons };
}

export class SourceInput {
  constructor(api, canvas, stations, fatal, focusChanged, observedClock = null, lifecycleChanged = null) {
    this.api = api; this.canvas = canvas; this.stations = stations;
    this.keys = new Set(); this.paused = false; this.fatal = fatal; this.focusChanged = focusChanged;
    this.observedClock = observedClock;
    this.lifecycleChanged = lifecycleChanged;
    this.configured = new Set(stations.flatMap(id => CABINET_KEYS[id]));
    this.listeners = [];
    this.listen(window, 'keydown', event => this.key(event, true));
    this.listen(window, 'keyup', event => this.key(event, false));
    this.listen(window, 'blur', () => this.focus());
    this.listen(window, 'focus', () => this.focus());
    this.listen(document, 'visibilitychange', () => this.focus());
    this.listen(canvas, 'pointerdown', () => canvas.focus());
  }

  listen(target, type, action) {
    const listener = event => { try { action(event); } catch (error) { this.fatal(error); } };
    target.addEventListener(type, listener);
    this.listeners.push(() => target.removeEventListener(type, listener));
  }

  key(event, down) {
    if (event.code === 'Escape' || event.code === 'KeyR' || event.code === 'Enter') {
      event.preventDefault();
      if (!down || event.repeat) return;
      if (event.code === 'Escape') { this.paused = !this.paused; this.focus(); }
      else if (event.code === 'KeyR') {
        this.lifecycleChanged?.('restart');
        this.keys.clear(); this.api.Restart(); this.poll();
      }
      else { this.api.Continue(this.stations[0]); this.poll(); }
      return;
    }
    if (!this.configured.has(event.code)) return;
    event.preventDefault();
    if (down && event.repeat && !this.keys.has(event.code)) return;
    if (down) this.keys.add(event.code); else this.keys.delete(event.code);
    this.poll();
  }

  focus() {
    this.lifecycleChanged?.(this.paused ? 'pause' : 'focus-or-visibility');
    this.keys.clear();
    this.poll();
    this.api.SetActive(!this.paused && !document.hidden && document.hasFocus());
    this.focusChanged();
  }

  poll() {
    const active = !this.paused && !document.hidden && document.hasFocus();
    const packet = active ? cabinetPacket(this.keys, navigator.getGamepads?.(), this.stations) :
      { axes: Array(8).fill(0), buttons: Array(4).fill(0) };
    this.capture(packet.axes, packet.buttons);
  }

  capture(axes, buttons) {
    if (this.observedClock && this.api.CaptureObserved)
      this.api.CaptureObserved(axes, buttons, this.observedClock());
    else this.api.Capture(axes, buttons);
  }

  close() {
    this.lifecycleChanged?.('dispose');
    for (const remove of this.listeners) remove(); this.listeners.length = 0;
  }
}
