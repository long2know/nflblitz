import assert from 'node:assert/strict';
import test from 'node:test';
import { CABINET_KEYS, cabinetPacket, SourceInput } from './wwwroot/input.js';

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
