export const CABINET_KEYS = Object.freeze([
  ['KeyW', 'KeyS', 'KeyA', 'KeyD', 'KeyV', 'KeyB', 'KeyN'],
  ['ArrowUp', 'ArrowDown', 'ArrowLeft', 'ArrowRight', 'Delete', 'End', 'PageDown'],
  ['KeyI', 'KeyK', 'KeyJ', 'KeyL', 'KeyU', 'KeyO', 'KeyP'],
  ['KeyT', 'KeyG', 'KeyF', 'KeyH', 'KeyZ', 'KeyX', 'KeyC'],
]);

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
  constructor(api, canvas, stations, fatal, focusChanged) {
    this.api = api; this.canvas = canvas; this.stations = stations;
    this.keys = new Set(); this.paused = false; this.fatal = fatal; this.focusChanged = focusChanged;
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
      else if (event.code === 'KeyR') { this.keys.clear(); this.api.Restart(); this.poll(); }
      else { this.api.Continue(this.stations[0]); this.poll(); }
      return;
    }
    if (!this.configured.has(event.code)) return;
    event.preventDefault();
    if (down) this.keys.add(event.code); else this.keys.delete(event.code);
    this.poll();
  }

  focus() {
    this.keys.clear();
    this.api.Capture(Array(8).fill(0), Array(4).fill(0));
    this.api.SetActive(!this.paused && !document.hidden && document.hasFocus());
    this.focusChanged();
  }

  poll() {
    const active = !this.paused && !document.hidden && document.hasFocus();
    const packet = active ? cabinetPacket(this.keys, navigator.getGamepads?.(), this.stations) :
      { axes: Array(8).fill(0), buttons: Array(4).fill(0) };
    this.api.Capture(packet.axes, packet.buttons);
  }

  close() { for (const remove of this.listeners) remove(); this.listeners.length = 0; }
}
