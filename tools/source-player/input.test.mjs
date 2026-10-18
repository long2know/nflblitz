import assert from 'node:assert/strict';
import test from 'node:test';
import { CABINET_KEYS, cabinetPacket } from './wwwroot/input.js';

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
