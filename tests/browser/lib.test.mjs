import assert from 'node:assert/strict';
import test from 'node:test';
import { deflateSync } from 'node:zlib';
import { assertNoRuntimeErrors, parseHud, pngStats, readVisibleHud, requireHud, requireRenderedField, safeUrl } from './lib.mjs';

test('HUD parser reads real visible phase/possession/score/control fields', () => {
  const state = requireHud(parseHud('HOME 6  AWAY 2\nQ 3  1:22\nPOSSESSION: AWAY  <\nDEAD BALL\nDOWN 2\nCONTROLLED #4\nPLAY #12\nCAUGHT'));
  assert.equal(state.phase, 'DeadBall');
  assert.equal(state.possession, 'Away');
  assert.equal(state.homeScore, 6);
  assert.equal(state.awayScore, 2);
  assert.equal(state.quarter, 3);
  assert.equal(state.down, 2);
  assert.equal(state.controlledPlayerId, 4);
  assert.equal(state.playNumber, 12);
  assert.equal(state.passCaught, true);
});

test('phase parsing is ordered and accepts contract spellings', () => {
  for (const [text, expected] of [
    ['PLAY SELECT', 'PlaySelect'], ['PlaySelect', 'PlaySelect'],
    ['PreSnap', 'PreSnap'], ['PRE-SNAP', 'PreSnap'],
    ['LiveBall', 'LiveBall'], ['DEAD BALL', 'DeadBall'],
    ['FINAL SCORE - HOME WIN', 'GameOver'], ['GAME OVER', 'GameOver'],
    ['CALL YOUR PLAY', 'PlaySelect'], ['SET YOUR DEFENSE', 'PlaySelect'],
    ['READY TO SNAP', 'PreSnap'], ['READ THE OFFENSE', 'PreSnap'],
    ['MAKE YOUR MOVE', 'LiveBall'], ['FIND THE CATCH', 'LiveBall'],
    ['STOP THE DRIVE', 'LiveBall'], ['SPACE NEXT PLAY', 'DeadBall'],
    ['TIE GAME', 'GameOver'],
  ]) assert.equal(parseHud(text).phase, expected);
});

test('actual compact HUD role and possession text is observable without a debug bridge', () => {
  const state = requireHud(parseHud('HOME\n06\nAWAY\n02\n<<< AWAY BALL / YOU PLAY DEFENSE\nSTOP THE DRIVE\nYOU: DEF 3 / TURBO\n2nd & 28'));
  assert.equal(state.phase, 'LiveBall');
  assert.equal(state.possession, 'Away');
  assert.equal(state.controlledPlayerId, 2);
  assert.equal(state.down, 2);
  assert.equal(state.homeScore, 6);
  assert.equal(parseHud('FIND THE CATCH\nPASS IN FLIGHT').passCaught, false);
  assert.equal(parseHud('YOU: WR1 / TURBO').controlledPlayerId, 4);
});
test('loader, blank canvas, missing score and missing possession never count as game evidence', () => {
  for (const text of ['Loading 100%', '', 'PLAY SELECT HOME 0 AWAY 0', 'PLAY SELECT POSSESSION HOME']) {
    assert.throws(() => requireHud(parseHud(text)));
  }
  assert.throws(() => requireRenderedField({ width: 1280, height: 800, greenFraction: 0, colors: 1 }));
  assert.throws(() => requireRenderedField({ width: 1280, height: 800, greenFraction: 0.5, colors: 2 }));
});

test('static HOME 0 field/mini-map labels cannot masquerade as the current score', () => {
  const full = 'PLAY SELECT\nHOME BALL\nHOME 0 > 30-YARD SERIES < 100 AWAY';
  assert.throws(() => requireHud(readVisibleHud(full, 'YARD RUSH')));
  const state = requireHud(readVisibleHud(full, 'HOME 06\nAWAY 02\nQUARTER 3'));
  assert.equal(state.homeScore, 6);
  assert.equal(state.awayScore, 2);
});

test('console/network/wasm failures cannot be ignored', () => {
  assertNoRuntimeErrors([]);
  for (const error of [
    { kind: 'console', message: 'Uncaught RuntimeError: unreachable' },
    { kind: 'network', status: 404, url: 'http://127.0.0.1:8080/Build/game.wasm' },
    { kind: 'page', message: 'WebAssembly compile failed' },
  ]) assert.throws(() => assertNoRuntimeErrors([error]));
});

test('artifact URLs do not persist credentials or query strings', () => {
  assert.equal(safeUrl('http://name:password@localhost:8080/Build/game.wasm?private=value#fragment'),
    'http://localhost:8080/Build/game.wasm');
});

test('PNG statistics inspect decoded pixels, not just a PNG filename', () => {
  const signature = Buffer.from([137, 80, 78, 71, 13, 10, 26, 10]);
  const chunk = (name, data) => {
    const length = Buffer.alloc(4);
    length.writeUInt32BE(data.length);
    return Buffer.concat([length, Buffer.from(name), data, Buffer.alloc(4)]);
  };
  const header = Buffer.alloc(13);
  header.writeUInt32BE(2, 0);
  header.writeUInt32BE(2, 4);
  header[8] = 8;
  header[9] = 2;
  const raw = Buffer.from([0, 10, 150, 10, 255, 255, 255, 0, 20, 180, 10, 0, 0, 0]);
  const png = Buffer.concat([signature, chunk('IHDR', header), chunk('IDAT', deflateSync(raw)), chunk('IEND', Buffer.alloc(0))]);
  assert.deepEqual(pngStats(png), { width: 2, height: 2, greenFraction: 0.5, brightFraction: 0.25, colors: 4 });
  assert.throws(() => pngStats(Buffer.from('not a PNG')));
});
