import assert from 'node:assert/strict';
import test from 'node:test';
import { deflateSync } from 'node:zlib';
import { actorModeFromUrl, assertNoRuntimeErrors, Cdp, cropPng, decodePng, fieldPlayerOffset, parseGenerationProof, parseHud, parseOcrTsv, phaseRect, pngStats, readTeamScore, readVisibleHud, requireActorMarker, requireFinalPresentation, requireFreshMatchHud, requireHud, requireNarrowControls, requireRenderedField, safeUrl, scoreRects, tightScorePng, turboBarFraction } from './lib.mjs';

test('source actor identity requires explicit supported query and actual console marker, never graybox fallback', () => {
  assert.equal(actorModeFromUrl('http://localhost/?actors=source&private=not-recorded'), 'source');
  assert.equal(actorModeFromUrl('http://localhost/?actors=graybox'), 'graybox');
  assert.equal(actorModeFromUrl('http://localhost/'), null);
  assert.throws(() => actorModeFromUrl('http://localhost/?actors=other'), /Actor selection/);
  assert.throws(() => actorModeFromUrl('http://localhost/?actors=source&actors=source'), /Actor selection/);
  const source = 'Actor mode: local source mesh/textures, bind-pose-only. No original animation; stadium/HUD remain procedural.';
  const graybox = 'Actor mode: explicitly selected procedural graybox.';
  assert.ok(requireActorMarker('source', [source]));
  assert.ok(requireActorMarker('graybox', [graybox]));
  assert.throws(() => requireActorMarker('source', [graybox]), /marker missing/);
  assert.throws(() => requireActorMarker('source', []), /marker missing/);
  assert.throws(() => requireActorMarker(null, [source]), /Explicit actor mode/);
  const qbstance = 'Actor mode: local source mesh/textures, QBSTANCE frame0 source pose (static; no original continuous animation; not verified pre-snap).';
  assert.ok(requireActorMarker('source', [qbstance], 'qbstance-frame0'));
  assert.throws(() => requireActorMarker('source', [source], 'qbstance-frame0'), /marker missing/);
  assert.throws(() => requireActorMarker('source', [qbstance], 'bind-pose'), /marker missing/);
  assert.throws(() => requireActorMarker('source', [qbstance], 'standing'), /Unsupported expected source pose/);
});

test('generation proof pins the receipt and rejects mixed, incomplete or unsafe payload identities', () => {
  const receipt = { schemaVersion: 2, buildId: '1'.repeat(32), generationProof: 'build-generation.txt' };
  const text = ['format=blitz-webgl-build-v1', `build-id=${receipt.buildId}`, 'unity-version=6000.3.24f1',
    'build-target=WebGL', 'build-result=Succeeded', 'total-errors=0', 'build-report-bytes=123',
    ...['index.html', 'Build/Game.loader.js', 'Build/Game.framework.js', 'Build/Game.wasm', 'Build/Game.data']
      .map(file => `sha256=${'a'.repeat(64)} ${file}`)].join('\n') + '\n';
  assert.equal(parseGenerationProof(receipt, text).size, 5);
  assert.throws(() => parseGenerationProof({ ...receipt, buildId: '2'.repeat(32) }, text), /proof field/);
  assert.throws(() => parseGenerationProof({ ...receipt, schemaVersion: 1 }, text), /schema2/);
  assert.throws(() => parseGenerationProof(receipt, text.replace('Build/Game.wasm', '../private.wasm')), /Unsafe/);
  assert.throws(() => parseGenerationProof(receipt, text.replace('Build/Game.data', 'Build/Game.wasm')), /duplicate/);
  assert.throws(() => parseGenerationProof(receipt, text.replace(/sha256=[a-f0-9]+ Build\/Game.data\n/, '')), /twelve/);
  assert.throws(() => parseGenerationProof(receipt, text.replace('build-result=Succeeded', 'build-result=Failed')), /proof field/);
});

test('HUD parser reads real visible phase/possession/score/control fields', () => {
  const state = requireHud(parseHud('HOME 6  AWAY 2\nQ 3  1:22\nPOSSESSION: AWAY  <\nDEAD BALL\nDOWN 2\nCONTROLLED #4\nPLAY #12\nCAUGHT'));
  assert.equal(state.phase, 'DeadBall');
  assert.equal(state.possession, 'Away');
  assert.equal(state.homeScore, 6);
  assert.equal(state.awayScore, 2);
  assert.equal(state.quarter, 3);
  assert.equal(state.clockSeconds, 82);
  assert.equal(state.down, 2);
  assert.equal(state.controlledPlayerId, 4);
  assert.equal(state.playNumber, 12);
  assert.equal(state.passCaught, true);
});

test('clock and quarter must come from the scoreboard crop, not notices or field labels', () => {
  const state = readVisibleHud('QUARTER 1 COMPLETE\nHOME BALL\nLIVE BALL\n1:22', 'HOME 06\nAWAY 02\nQUARTER 2 / 4\n2:00');
  assert.equal(state.quarter, 2);
  assert.equal(state.clockSeconds, 120);
  assert.equal(readVisibleHud('QUARTER 4 COMPLETE', '').quarter, null);
});

test('OCR TSV preserves actual text, word coordinates and line grouping', () => {
  const tsv = 'level\tpage_num\tblock_num\tpar_num\tline_num\tword_num\tleft\ttop\twidth\theight\tconf\ttext\n' +
    '5\t1\t1\t1\t1\t1\t10\t20\t30\t14\t95\tYOU\n' +
    '5\t1\t1\t1\t1\t2\t45\t20\t20\t14\t91\tQB\n' +
    '5\t1\t2\t1\t1\t1\t60\t250\t80\t12\t88\tSCRIMMAGE\n';
  const result = parseOcrTsv(tsv);
  assert.equal(result.text, 'YOU QB\nSCRIMMAGE');
  assert.equal(result.words.length, 3);
  assert.equal(result.words[2].top, 250);
  assert.equal(result.words[2].confidence, 88);
  assert.throws(() => parseOcrTsv(tsv.replace('\t60\t250\t', '\tbad\t250\t')), /Invalid OCR/);
});

test('input-release comparison uses a real field player and static field landmark, not sidebar/footer text', () => {
  const canvas = { x: 0, y: 0, width: 1280, height: 800 };
  const sidebar = { text: 'YOU', left: 1030, top: 420, width: 28 };
  const footer = { text: 'FIRST', left: 500, top: 760, width: 30 };
  const player = { text: 'YOU', left: 400, top: 400, width: 28, height: 14 };
  const anchor = { text: 'SCRIMMAGE', left: 350, top: 650, width: 80 };
  const observation = { screenshot: 'release-1280x800.png', landmarks: [sidebar, footer, player, anchor] };
  assert.deepEqual(fieldPlayerOffset(observation, canvas), { x: 64, y: -243, anchor: 'SCRIMMAGE' });
  assert.throws(() => fieldPlayerOffset({ ...observation, landmarks: [sidebar, footer, anchor] }, canvas),
    /release-1280x800.png/);
  assert.throws(() => fieldPlayerOffset({ ...observation, landmarks: [sidebar, footer, player] }, canvas),
    /Cannot prove released movement/);
});

test('turbo evidence measures the actual desktop cyan/gold bar, not a key dispatch or another HUD area', () => {
  const image = { width: 1280, height: 800, channels: 3, pixels: Buffer.alloc(1280 * 800 * 3) };
  for (let y = 529; y < 538; y++) {
    for (let x = 1008; x < 1130; x++) {
      const offset = (y * 1280 + x) * 3;
      image.pixels.set([20, 200, 220], offset);
    }
  }
  assert.equal(turboBarFraction(image, { x: 0, y: 40, width: 1280, height: 720 }), 0.5);
  for (let y = 529; y < 538; y++) {
    for (let x = 1130; x < 1191; x++) image.pixels.set([240, 196, 60], (y * 1280 + x) * 3);
  }
  assert.equal(turboBarFraction(image, { x: 0, y: 40, width: 1280, height: 720 }), 0.75);
  assert.throws(() => turboBarFraction(image, { x: 0, y: 0, width: 360, height: 740 }), /desktop sidebar/);
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
  const state = requireHud(readVisibleHud(full, 'HOME 06\nAWAY 02\nQUARTER 3\nHOME BALL', { home: 'HOME\n06', away: 'AWAY\n02' }));
  assert.equal(state.homeScore, 6);
  assert.equal(state.awayScore, 2);
});

test('actual screenshot reading order cannot promote AT HOME 20 to the Home score', () => {
  const full = 'HOME\nAWAY\nYARD RUSH\n2:00\n00\nQUARTER 1/4\n00\nHOME BALL >>> YOU PLAY OFFENSE\nAT HOME 20\nPLAY SELECT';
  assert.equal(readVisibleHud(full, full).homeScore, null);
  const hud = requireHud(readVisibleHud(full, full, { home: 'HOME,\n00', away: 'AWAY\n00' }));
  assert.equal(hud.homeScore, 0);
  assert.equal(hud.awayScore, 0);
  assert.equal(hud.clockSeconds, 120);
  assert.equal(readTeamScore('AT HOME 20', 'Home'), null);
  assert.equal(readTeamScore('HOME\nunreadable', 'Home'), null);
  assert.equal(readTeamScore('00', 'Home'), 0);
  assert.equal(readTeamScore('12', 'Away'), 12);
  assert.equal(readTeamScore('00 garbage', 'Home'), null);
  assert.throws(() => readTeamScore('HOME 00', 'Unknown'), /Unknown score team/);
});

test('actual HUD score crops isolate the numeric Score rects, excluding label/bar/spot/minimap', () => {
  const desktop = scoreRects({ x: 0, y: 0, width: 1280, height: 720 });
  assert.deepEqual(desktop.home, { x: 384, y: 27, width: 148, height: 40 });
  assert.deepEqual(desktop.away, { x: 752, y: 27, width: 148, height: 40 });
  const narrow = scoreRects({ x: 0, y: 0, width: 360, height: 740 });
  assert.deepEqual(narrow.home, { x: 21, y: 42.75, width: 93.75, height: 25.5 });
  assert.deepEqual(narrow.away, { x: 248.25, y: 42.75, width: 93.75, height: 25.5 });
  for (const rect of Object.values(narrow)) {
    assert.ok(rect.x >= 0 && rect.x + rect.width <= 360 && rect.y + rect.height < 104);
  }
});

test('current possession comes only from scoreboard, not the prior touchdown/possession notice', () => {
  const full = 'HOME BALL >>> YOU PLAY OFFENSE\n1st & 30\nDEAD BALL\nTOUCHDOWN +6\nHOME takes possession\nAWAY TOUCHDOWN +6';
  assert.equal(parseHud(full).possession, 'Away', 'This fixture reproduces actual notice reading order.');
  assert.equal(readVisibleHud(full, 'HOME BALL >>> YOU PLAY OFFENSE').possession, 'Home');
  assert.equal(readVisibleHud(full, 'unreadable').possession, null);
  assert.deepEqual(phaseRect({ x: 0, y: 40, width: 1280, height: 720 }),
    { x: 1008, y: 170, width: 244, height: 20, scale: 3 });
  assert.throws(() => phaseRect({ x: 0, y: 0, width: 360, height: 740 }), /desktop sidebar/);
});

test('world FIRST DOWN followed by 1 RUN cannot reset the observed scoreboard down', () => {
  const full = 'HOME BALL\n2nd & 20\nPLAY SELECT\nFIRST DOWN\n1 RUN\n2 QUICK PASS';
  assert.equal(parseHud(full).down, 1, 'Reproduces the actual full-frame OCR order.');
  assert.equal(readVisibleHud(full, 'HOME BALL\n2nd & 20\nQ1\n1:59').down, 2);
  assert.equal(readVisibleHud(full, 'unreadable').down, null);
});

test('narrow viewport must expose real readable movement/action/play controls, not only fit the canvas', () => {
  const text = '1 RUN\n2 QUICK PASS\nWASD / arrows move SHIFT turbo F tackle Q switch';
  requireNarrowControls(text, 'actual-360x740-controls.png');
  for (const word of ['WASD / arrows', 'SHIFT', 'F tackle', 'Q switch', '1 RUN', '2 QUICK PASS']) {
    assert.throws(() => requireNarrowControls(text.replace(word, ''), 'actual-360x740-controls.png'),
      /actual-360x740-controls.png/);
  }
});

test('viewport-only resize must preserve fresh-match state and cannot accept a falsely OCR-read 90 score', () => {
  const state = { phase: 'PlaySelect', possession: 'Home', homeScore: 0, awayScore: 0, quarter: 1, clockSeconds: 120 };
  requireFreshMatchHud(state, 'portrait.png');
  for (const mutation of [{ awayScore: 90 }, { homeScore: 20 }, { quarter: null }, { clockSeconds: 119 },
    { possession: 'Away' }, { phase: 'PreSnap' }]) {
    assert.throws(() => requireFreshMatchHud({ ...state, ...mutation }, 'portrait.png'), /portrait.png/);
  }
});

test('final presentation requires real current branding and positive world axes, not stale camera-relative hints', () => {
  const header = 'NFL BLITZ 2000\nUNITY SOURCE PORT / IN PROGRESS';
  const legend = 'W = +X field  D = +Z field | Cyan ring = YOU';
  const title = 'Unity Web Player | NFL Blitz 2000 - Unity Port';
  requireFinalPresentation(header, legend, title, false, 'desktop.png');
  requireFinalPresentation('NFL BLITZ 2000', legend, title, true, 'portrait.png');
  for (const [h, l, t, narrow] of [
    ['YARD RUSH', legend, title, false],
    ['NFL BLITZ 2000', legend, title, false],
    [header, 'W=+X (right) D=+Z (far side)', title, false],
    [header, legend.replace('+X', '-X'), title, false],
    [header, legend.replace('+Z', '-Z'), title, true],
    [header, legend, 'Unity Web Player | Yard Rush', false],
  ]) assert.throws(() => requireFinalPresentation(h, l, t, narrow, 'actual.png'), /actual.png/);
});

test('numeric OCR crop trims alignment whitespace without replacing digits or inventing a loader score', () => {
  const image = { width: 100, height: 30, channels: 3, pixels: Buffer.alloc(100 * 30 * 3) };
  const blank = cropPng(image, { x: 0, y: 0, width: 100, height: 30 });
  assert.deepEqual(tightScorePng(blank), blank);
  for (let y = 10; y < 20; y++) {
    for (let x = 70; x < 80; x++) image.pixels.set([240, 240, 240], (y * 100 + x) * 3);
  }
  const tight = decodePng(tightScorePng(cropPng(image, { x: 0, y: 0, width: 100, height: 30 })));
  assert.equal(tight.width, 22);
  assert.equal(tight.height, 22);
  assert.equal(pngStats(tight).brightFraction, 100 / (22 * 22));
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

test('CDP routes an active-tab input release through its explicit flattened session', async () => {
  const socket = new EventTarget();
  const sent = [];
  socket.send = data => {
    const message = JSON.parse(data);
    sent.push(message);
    queueMicrotask(() => socket.dispatchEvent(new MessageEvent('message',
      { data: JSON.stringify({ id: message.id, sessionId: message.sessionId, result: {} }) })));
  };
  socket.close = () => socket.dispatchEvent(new Event('close'));
  const cdp = new Cdp(socket);
  try {
    await cdp.send('Input.dispatchKeyEvent', { type: 'keyUp', key: 'w' }, 'owned-active-other-tab');
    await cdp.send('Page.bringToFront');
    assert.deepEqual(sent[0], { id: 1, method: 'Input.dispatchKeyEvent',
      params: { type: 'keyUp', key: 'w' }, sessionId: 'owned-active-other-tab' });
    assert.deepEqual(sent[1], { id: 2, method: 'Page.bringToFront', params: {} });
  } finally {
    cdp.close();
  }
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
  const decoded = decodePng(png);
  assert.equal(decoded.channels, 3);
  assert.deepEqual(pngStats(decoded), pngStats(png));
  const greenPixel = cropPng(decoded, { x: 0, y: 0, width: 1, height: 1, scale: 2 });
  assert.deepEqual(pngStats(greenPixel), { width: 2, height: 2, greenFraction: 1, brightFraction: 0, colors: 1 });
  assert.deepEqual(decodePng(cropPng(png, { x: 0, y: 0, width: 2, height: 2 })).pixels, decoded.pixels);
  assert.throws(() => cropPng(decoded, { x: 1, y: 0, width: 2, height: 1 }), /Invalid screenshot crop/);
  assert.throws(() => cropPng(decoded, { x: 0, y: -1, width: 1, height: 1 }), /Invalid screenshot crop/);
  assert.throws(() => cropPng(decoded, { x: 0, y: 0, width: 1, height: 1, scale: 0.5 }), /Invalid screenshot crop/);
  assert.throws(() => pngStats(Buffer.from('not a PNG')));
});
