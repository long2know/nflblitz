import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import vm from 'node:vm';
import test from 'node:test';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const source = fs.readFileSync(path.join(root, 'Assets/Scripts/Runtime/SourceCabinetInput.jslib'), 'utf8');

function fixture({ width = 360, height = 740, dpr = 2, pads = [] } = {}) {
  const library = {};
  const canvas = { width: width * dpr, height: height * dpr,
    getBoundingClientRect: () => ({ width, height }) };
  const context = vm.createContext({
    Module: { canvas }, window: { devicePixelRatio: dpr },
    navigator: { getGamepads: () => pads }, LibraryManager: { library },
    mergeInto: (target, value) => Object.assign(target, value)
  });
  vm.runInContext(source, context);
  return { library, canvas, context };
}

test('actual DPR2 portrait measures CSS360 separately from buffer720; resize is live', () => {
  const { library, canvas } = fixture();
  assert.deepEqual([0, 1, 2, 3, 4].map(n => library.SourceCabinetViewport(n)), [360, 740, 720, 1480, 2]);
  canvas.width = 2560;
  canvas.height = 1600;
  canvas.getBoundingClientRect = () => ({ width: 1280, height: 800 });
  assert.deepEqual([0, 1, 2, 3].map(n => library.SourceCabinetViewport(n)), [1280, 800, 2560, 1600]);
});

test('four standard pads retain independent raw analog axes and simultaneous ABC', () => {
  const pads = Array.from({ length: 4 }, (_, i) => ({
    connected: true, mapping: 'standard', axes: [i / 3, -i / 3],
    buttons: Array.from({ length: 6 }, (_, b) => ({ pressed: [0, 1, 5].includes(b) }))
  }));
  const { library } = fixture({ pads });
  for (let station = 0; station < 4; station++) {
    assert.equal(library.SourceCabinetGamepad(station, 0), 1);
    assert.equal(library.SourceCabinetGamepad(station, 1), station / 3);
    assert.equal(library.SourceCabinetGamepad(station, 2), -station / 3);
    assert.equal(library.SourceCabinetGamepad(station, 3), 7);
  }
});

test('unmapped pad is explicit, disconnected/absent pads are neutral rather than stale', () => {
  const { library, context } = fixture({ pads: [{ connected: true, mapping: 'vendor', axes: [1, 1], buttons: [] }] });
  assert.equal(library.SourceCabinetGamepad(0, 0), -1);
  assert.equal(library.SourceCabinetGamepad(0, 3), 0);
  assert.equal(library.SourceCabinetGamepad(3, 1), 0);
  context.navigator.getGamepads = () => [{ connected: false }];
  assert.equal(library.SourceCabinetGamepad(0, 0), 0);
  assert.equal(library.SourceCabinetGamepad(0, 3), 0);
});

test('source plugin does not request fullscreen, change canvas sizing or create another clock', () => {
  assert.doesNotMatch(source, /requestFullscreen|setInterval|setTimeout|requestAnimationFrame|\.style\s*=|canvas\.(width|height)\s*=/);
});
