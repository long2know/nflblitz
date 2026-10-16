import { crc32, deflateSync, inflateSync } from 'node:zlib';

export function safeUrl(value) {
  try {
    const url = new URL(value);
    return `${url.protocol}//${url.host}${url.pathname}`;
  } catch {
    return String(value).slice(0, 300);
  }
}

export function actorModeFromUrl(value) {
  const modes = new URL(value).searchParams.getAll('actors');
  if (modes.length > 1 || modes.some(mode => !['source', 'graybox'].includes(mode))) {
    throw new Error('Actor selection must be one explicit actors=source or actors=graybox.');
  }
  return modes[0] ?? null;
}

export function requireActorMarker(mode, messages, pose = 'bind-pose') {
  const sourceMarkers = {
    'bind-pose': 'Actor mode: local source mesh/textures, bind-pose-only.',
    'qbstance-frame0': 'Actor mode: local source mesh/textures, QBSTANCE frame0 source pose (static; no original continuous animation; not verified pre-snap).',
  };
  if (mode === 'source' && !sourceMarkers[pose]) throw new Error(`Unsupported expected source pose ${pose}`);
  const marker = mode === 'source' ? sourceMarkers[pose]
    : mode === 'graybox' ? 'Actor mode: explicitly selected procedural graybox.' : null;
  if (!marker) throw new Error('Explicit actor mode is required for actor identity evidence.');
  if (!messages.some(message => message.includes(marker))) {
    throw new Error(`Actual Unity console actor marker missing: ${marker}`);
  }
  return marker;
}

export function parseGenerationProof(receipt, text) {
  if (receipt.schemaVersion !== 2 || receipt.generationProof !== 'build-generation.txt' ||
      !/^[0-9a-f]{32}$/.test(receipt.buildId ?? '')) throw new Error('Expected a schema2 generation receipt.');
  const lines = text.trimEnd().split(/\r?\n/);
  if (lines.length !== 12) throw new Error('Generation proof must have its exact twelve fields.');
  for (const field of ['format=blitz-webgl-build-v1', `build-id=${receipt.buildId}`,
    'unity-version=6000.3.24f1', 'build-target=WebGL', 'build-result=Succeeded', 'total-errors=0']) {
    if (lines.filter(line => line === field).length !== 1) throw new Error(`Invalid generation proof field ${field}`);
  }
  const artifacts = new Map();
  for (const line of lines.filter(line => line.startsWith('sha256='))) {
    const match = line.match(/^sha256=([0-9a-f]{64}) (index\.html|Build\/[A-Za-z0-9_][A-Za-z0-9_.-]*\.(?:loader\.js|framework\.js|wasm|data))$/);
    if (!match || artifacts.has(match[2])) throw new Error('Unsafe, duplicate or malformed generation artifact.');
    artifacts.set(match[2], match[1]);
  }
  if (artifacts.size !== 5 || !artifacts.has('index.html') ||
      ['loader.js', 'framework.js', 'wasm', 'data'].some(suffix =>
        [...artifacts.keys()].filter(file => file.endsWith(`.${suffix}`)).length !== 1)) {
    throw new Error('Generation proof must identify index and all four unique real player payloads.');
  }
  return artifacts;
}

export function parseHud(text) {
  const normalized = text.replace(/[‐‑–—]/g, '-').replace(/\r/g, '').toUpperCase();
  let phase = null;
  if (/\b(GAME\s*OVER|FINAL(?:\s+SCORE)?|TIE\s+GAME|HOME\s+WINS|AWAY\s+WINS)\b/.test(normalized)) phase = 'GameOver';
  else if (/\bDEAD[\s_-]*BALL\b|\bSPACE\s+NEXT\s+PLAY\b/.test(normalized)) phase = 'DeadBall';
  else if (/\bPRE[\s_-]*SNAP\b|\bREADY\s+TO\s+SNAP\b|\bREAD\s+THE\s+OFFENSE\b/.test(normalized)) phase = 'PreSnap';
  else if (/\bLIVE[\s_-]*BALL\b|\bMAKE\s+YOUR\s+MOVE\b|\bFIND\s+THE\s+CATCH\b|\bSTOP\s+THE\s+DRIVE\b/.test(normalized)) phase = 'LiveBall';
  else if (/\bPLAY[\s_-]*SELECT(?:ION)?\b|\bSELECT\s+(?:A\s+)?PLAY\b|\bCALL\s+YOUR\s+PLAY\b|\bSET\s+YOUR\s+DEFENSE\b/.test(normalized)) phase = 'PlaySelect';
  const possession = normalized.match(/\b(?:POSSESSION|OFFENSE|BALL)\s*[:|-]?\s*(HOME|AWAY)\b/)
    ?? normalized.match(/\b(HOME|AWAY)\s+BALL\b/);
  const controlled = normalized.match(/\b(?:CONTROLLED|CONTROL|CTRL)\s*(?:PLAYER)?\s*[:#|-]?\s*(\d{1,2})\b/);
  const role = normalized.match(/\bYOU\s*[:/]\s*(QB|DEF\s*[1-7]|WR\s*[1-3])\b/);
  const home = normalized.match(/\bHOME\s*[:|-]?\s*(\d{1,3})\b/);
  const away = normalized.match(/\bAWAY\s*[:|-]?\s*(\d{1,3})\b/);
  const quarter = normalized.match(/\b(?:QUARTER|Q)\s*[:#|-]?\s*([1-4])\b/);
  const clock = normalized.match(/\b([0-9]{1,2}):([0-5][0-9])\b/);
  const down = normalized.match(/\bDOWN\s*[:#|-]?\s*([1-4])\b/)
    ?? normalized.match(/\b([1-4])(?:ST|ND|RD|TH)\s*&/);
  const playNumber = normalized.match(/\bPLAY\s*#\s*(\d+)\b/);
  const result = {
    phase,
    possession: possession ? possession[1] === 'HOME' ? 'Home' : 'Away' : null,
    controlledPlayerId: controlled ? Number(controlled[1]) : role
      ? role[1] === 'QB' ? 3 : /DEF/.test(role[1]) ? Number(role[1].match(/\d/)[0]) - 1 : Number(role[1].match(/\d/)[0]) + 3
      : null,
    homeScore: home ? Number(home[1]) : null,
    awayScore: away ? Number(away[1]) : null,
    quarter: quarter ? Number(quarter[1]) : null,
    clockSeconds: clock ? Number(clock[1]) * 60 + Number(clock[2]) : null,
    down: down ? Number(down[1]) : null,
    playNumber: playNumber ? Number(playNumber[1]) : null,
    passCaught: /\b(CAUGHT|PASS\s+COMPLETE)\b/.test(normalized),
    passThrown: /\b(PASS\s+THROWN|PASS\s+TO|PASSING|IN\s+FLIGHT)\b/.test(normalized),
    touchdown: /\bTOUCHDOWN\b/.test(normalized),
    safety: /\bSAFETY\b/.test(normalized),
  };
  return result;
}

export function requireHud(state) {
  if (!state.phase) throw new Error('No actual game phase was readable in the rendered HUD; loader screenshots are not gameplay evidence.');
  if (!['Home', 'Away'].includes(state.possession)) throw new Error('Rendered HUD must expose HOME/AWAY possession.');
  if (!Number.isInteger(state.homeScore) || !Number.isInteger(state.awayScore)) throw new Error('Rendered HUD scores are not readable.');
  return state;
}

export function requireFreshMatchHud(state, screenshot) {
  requireHud(state);
  if (state.phase !== 'PlaySelect' || state.possession !== 'Home' || state.homeScore !== 0 ||
      state.awayScore !== 0 || state.quarter !== 1 || state.clockSeconds !== 120) {
    throw new Error(`Fresh-match HUD changed or was misread in ${screenshot}: ${JSON.stringify(state)}`);
  }
}

export function requireFinalPresentation(headerText, legendText, documentTitle, narrow, screenshot) {
  const compact = text => text.toUpperCase().replace(/[^A-Z0-9]/g, '');
  if (!compact(headerText).includes('NFLBLITZ2000') || !compact(documentTitle).includes('NFLBLITZ2000') ||
      compact(headerText + documentTitle).includes('YARDRUSH')) {
    throw new Error(`Final NFL BLITZ 2000 HUD/document title missing or stale in ${screenshot}`);
  }
  if (!narrow && !compact(headerText).includes('UNITYSOURCEPORTINPROGRESS')) {
    throw new Error(`Final desktop in-progress subtitle unreadable in ${screenshot}`);
  }
  const legend = legendText.toUpperCase();
  if (!/\bW\s*=\s*\+\s*X\s+FIELD\b/.test(legend) || !/\bD\s*=\s*\+\s*Z\s+FIELD\b/.test(legend) ||
      /\bRIGHT\b|\bFAR\s+SIDE\b/.test(legend)) {
    throw new Error(`Explicit world-field W=+X / D=+Z legend missing or stale in ${screenshot}`);
  }
}

export function requireNarrowControls(text, screenshot) {
  const normalized = text.toUpperCase();
  for (const [label, pattern] of [
    ['movement', /\bWASD\b|\bARROWS\b/], ['turbo', /\bSHIFT\b/],
    ['tackle', /\bF\s+TACKLE\b/], ['defender switch', /\bQ\s+SWITCH\b/],
    ['run play', /\b1\s+RUN\b/], ['pass play', /\b2\s+QUICK\s+PASS\b/],
  ]) {
    if (!pattern.test(normalized)) throw new Error(`Narrow ${label} control is not readable in ${screenshot}`);
  }
}

export function readTeamScore(text, team) {
  if (!['Home', 'Away'].includes(team)) throw new Error(`Unknown score team ${team}`);
  const normalized = String(text ?? '').trim().toUpperCase();
  const match = normalized.match(/^(\d{1,3})$/) ??
    normalized.match(new RegExp(`^${team.toUpperCase()}[,.:|]?\\s+(\\d{1,3})$`));
  return match ? Number(match[1]) : null;
}

export function scoreRects(canvas) {
  const scale = canvas.width < 650 ? Math.max(0.5, canvas.width / 480) : Math.min(1.4, Math.max(0.82, canvas.width / 1280));
  const narrow = canvas.width / scale < 650;
  const boxes = narrow
    ? { home: { x: 16 * scale, y: 39 * scale, width: 145 * scale, height: 52 * scale },
      away: { x: canvas.width - 161 * scale, y: 39 * scale, width: 145 * scale, height: 52 * scale } }
    : { home: { x: canvas.width / 2 - 268 * scale, y: 9 * scale, width: 168 * scale, height: 58 * scale },
      away: { x: canvas.width / 2 + 100 * scale, y: 9 * scale, width: 168 * scale, height: 58 * scale } };
  return Object.fromEntries(Object.entries(boxes).map(([team, box]) => [team, {
    x: canvas.x + box.x + 12 * scale, y: canvas.y + box.y + 18 * scale,
    width: box.width - 20 * scale, height: box.height - 18 * scale,
  }]));
}

export function phaseRect(canvas) {
  if (canvas.width < 1000 || canvas.height < 520) throw new Error('Fast phase capture requires the actual desktop sidebar.');
  const scale = Math.min(1.4, Math.max(0.82, canvas.width / 1280));
  return { x: canvas.x + canvas.width - 272 * scale, y: canvas.y + 130 * scale,
    width: 244 * scale, height: 20 * scale, scale: 3 };
}

export function readVisibleHud(fullText, headerText, scoreText = {}) {
  const header = parseHud(headerText);
  return { ...parseHud(fullText), homeScore: readTeamScore(scoreText.home, 'Home'), awayScore: readTeamScore(scoreText.away, 'Away'),
    possession: header.possession, quarter: header.quarter, clockSeconds: header.clockSeconds };
}

export function parseOcrTsv(tsv) {
  const words = [];
  const lines = new Map();
  for (const row of tsv.trim().split('\n').slice(1)) {
    const fields = row.split('\t');
    if (fields[0] !== '5' || fields.length < 12 || !fields[11].trim()) continue;
    const [left, top, width, height, confidence] = fields.slice(6, 11).map(Number);
    if (![left, top, width, height, confidence].every(Number.isFinite)) throw new Error('Invalid OCR word coordinates.');
    const line = fields.slice(1, 5).join(':');
    const word = { line, left, top, width, height, confidence, text: fields.slice(11).join('\t').trim() };
    words.push(word);
    if (!lines.has(line)) lines.set(line, []);
    lines.get(line).push(word);
  }
  return { words, text: [...lines.values()].map(items => items.map(word => word.text).join(' ')).join('\n') };
}

export function fieldPlayerOffset(observation, canvas) {
  const fieldWords = observation.landmarks.filter(word => word.left < canvas.x + canvas.width - 300 &&
    word.top > canvas.y + 104 && word.top < canvas.y + canvas.height - 88);
  const player = fieldWords.find(word => /^YOU[/:]?$/i.test(word.text));
  const anchor = fieldWords.find(word => /^SCRIMMAGE$/i.test(word.text)) ??
    fieldWords.find(word => /^FIRST$/i.test(word.text));
  if (!player || !anchor) {
    throw new Error(`Cannot prove released movement without actual player/field OCR landmarks: ${observation.screenshot}`);
  }
  return { x: player.left + player.width / 2 - anchor.left,
    y: player.top + player.height / 2 - anchor.top, anchor: anchor.text };
}

export function assertNoRuntimeErrors(errors) {
  if (errors.length) throw new Error(`Browser runtime/network/console errors: ${JSON.stringify(errors)}`);
}

function paeth(a, b, c) {
  const p = a + b - c;
  const pa = Math.abs(p - a);
  const pb = Math.abs(p - b);
  const pc = Math.abs(p - c);
  return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
}

export function decodePng(png) {
  if (!png.subarray(0, 8).equals(Buffer.from([137, 80, 78, 71, 13, 10, 26, 10]))) {
    throw new Error('Screenshot is not a PNG.');
  }
  let width;
  let height;
  let channels;
  const chunks = [];
  for (let offset = 8; offset + 12 <= png.length;) {
    const length = png.readUInt32BE(offset);
    const type = png.toString('ascii', offset + 4, offset + 8);
    const data = png.subarray(offset + 8, offset + 8 + length);
    if (data.length !== length) throw new Error('Truncated screenshot PNG.');
    if (type === 'IHDR') {
      width = data.readUInt32BE(0);
      height = data.readUInt32BE(4);
      if (data[8] !== 8 || ![2, 6].includes(data[9]) || data[12] !== 0) {
        throw new Error('Expected a noninterlaced 8-bit RGB/RGBA Chrome screenshot.');
      }
      channels = data[9] === 6 ? 4 : 3;
    } else if (type === 'IDAT') {
      chunks.push(data);
    }
    offset += length + 12;
  }
  if (!width || !height || !channels || width * height > 16000000) throw new Error('Invalid screenshot dimensions.');
  const raw = inflateSync(Buffer.concat(chunks));
  const stride = width * channels;
  if (raw.length !== height * (stride + 1)) throw new Error('Screenshot scanline size mismatch.');
  let previous = Buffer.alloc(stride);
  const pixels = Buffer.alloc(width * height * channels);
  for (let y = 0; y < height; y++) {
    const filter = raw[y * (stride + 1)];
    const row = Buffer.from(raw.subarray(y * (stride + 1) + 1, (y + 1) * (stride + 1)));
    if (filter > 4) throw new Error('Unknown PNG scanline filter.');
    for (let x = 0; x < stride; x++) {
      const left = x >= channels ? row[x - channels] : 0;
      const up = previous[x];
      const upperLeft = x >= channels ? previous[x - channels] : 0;
      let predictor = 0;
      if (filter === 1) predictor = left;
      else if (filter === 2) predictor = up;
      else if (filter === 3) predictor = Math.floor((left + up) / 2);
      else if (filter === 4) predictor = paeth(left, up, upperLeft);
      row[x] = (row[x] + predictor) & 255;
    }
    row.copy(pixels, y * stride);
    previous = row;
  }
  return { width, height, channels, pixels };
}

export function cropPng(image, rectangle) {
  if (Buffer.isBuffer(image)) image = decodePng(image);
  const x = Math.floor(rectangle.x), y = Math.floor(rectangle.y);
  const width = Math.ceil(rectangle.width), height = Math.ceil(rectangle.height);
  const scale = rectangle.scale ?? 1;
  if (![x, y, width, height, scale].every(Number.isFinite) || x < 0 || y < 0 || width < 1 || height < 1 ||
      x + width > image.width || y + height > image.height || !Number.isInteger(scale) || scale < 1 || scale > 4) {
    throw new Error(`Invalid screenshot crop: ${JSON.stringify(rectangle)} in ${image.width}x${image.height}`);
  }
  const outputWidth = width * scale, outputHeight = height * scale;
  const raw = Buffer.alloc((outputWidth * image.channels + 1) * outputHeight);
  for (let outputY = 0; outputY < outputHeight; outputY++) {
    for (let outputX = 0; outputX < outputWidth; outputX++) {
      const source = ((y + Math.floor(outputY / scale)) * image.width + x + Math.floor(outputX / scale)) * image.channels;
      const destination = outputY * (outputWidth * image.channels + 1) + 1 + outputX * image.channels;
      image.pixels.copy(raw, destination, source, source + image.channels);
    }
  }
  const chunk = (type, data) => {
    const bytes = Buffer.alloc(data.length + 12);
    bytes.writeUInt32BE(data.length, 0);
    bytes.write(type, 4);
    data.copy(bytes, 8);
    bytes.writeUInt32BE(crc32(bytes.subarray(4, -4)), bytes.length - 4);
    return bytes;
  };
  const header = Buffer.alloc(13);
  header.writeUInt32BE(outputWidth, 0);
  header.writeUInt32BE(outputHeight, 4);
  header[8] = 8;
  header[9] = image.channels === 4 ? 6 : 2;
  return Buffer.concat([Buffer.from([137, 80, 78, 71, 13, 10, 26, 10]), chunk('IHDR', header),
    chunk('IDAT', deflateSync(raw)), chunk('IEND', Buffer.alloc(0))]);
}

export function tightScorePng(png) {
  const image = decodePng(png);
  let left = image.width, top = image.height, right = -1, bottom = -1;
  for (let y = 0; y < image.height; y++) {
    for (let x = 0; x < image.width; x++) {
      const offset = (y * image.width + x) * image.channels;
      if (image.pixels[offset] > 160 && image.pixels[offset + 1] > 160 && image.pixels[offset + 2] > 160) {
        left = Math.min(left, x); right = Math.max(right, x);
        top = Math.min(top, y); bottom = Math.max(bottom, y);
      }
    }
  }
  if (right < 0) return png;
  left = Math.max(0, left - 6); top = Math.max(0, top - 6);
  right = Math.min(image.width - 1, right + 6); bottom = Math.min(image.height - 1, bottom + 6);
  return cropPng(image, { x: left, y: top, width: right - left + 1, height: bottom - top + 1 });
}

export function pngStats(png) {
  const { width, height, channels, pixels } = Buffer.isBuffer(png) ? decodePng(png) : png;
  let green = 0, bright = 0;
  const colors = new Set();
  for (let x = 0; x < pixels.length; x += channels) {
    const r = pixels[x], g = pixels[x + 1], b = pixels[x + 2];
    if (g > 35 && g > r * 1.2 && g > b * 1.05) green++;
    if (r > 180 && g > 180 && b > 180) bright++;
    colors.add((r >> 4) * 256 + (g >> 4) * 16 + (b >> 4));
  }
  return { width, height, greenFraction: green / (width * height), brightFraction: bright / (width * height), colors: colors.size };
}

export function turboBarFraction(image, canvas) {
  phaseRect(canvas);
  if (Buffer.isBuffer(image)) image = decodePng(image);
  const scale = Math.min(1.4, Math.max(0.82, canvas.width / 1280));
  const x = Math.round(canvas.x + canvas.width - 272 * scale);
  const y = Math.round(canvas.y + (Math.max(430, canvas.height / scale - 256) + 25) * scale);
  const width = Math.round(244 * scale), height = Math.round(9 * scale);
  if (x < 0 || y < 0 || x + width > image.width || y + height > image.height) throw new Error('Turbo bar is outside the actual screenshot.');
  let filled = 0;
  for (let row = y; row < y + height; row++) {
    for (let column = x; column < x + width; column++) {
      const offset = (row * image.width + column) * image.channels;
      const r = image.pixels[offset], g = image.pixels[offset + 1], b = image.pixels[offset + 2];
      if ((r < 120 && g > 120 && b > 120) || (r > 180 && g > 120 && b < 180)) filled++;
    }
  }
  return filled / (width * height);
}

export function requireRenderedField(stats) {
  if (stats.width < 200 || stats.height < 200 || stats.greenFraction < 0.03 || stats.colors < 25) {
    throw new Error(`Screenshot does not contain a meaningfully rendered field: ${JSON.stringify(stats)}`);
  }
}

export class Cdp {
  constructor(socket) {
    this.socket = socket;
    this.nextId = 0;
    this.pending = new Map();
    this.listeners = new Map();
    socket.addEventListener('message', ({ data }) => {
      let message;
      try {
        message = JSON.parse(String(data));
      } catch (error) {
        this.rejectPending(error);
        return;
      }
      if (message.id) {
        const pending = this.pending.get(message.id);
        if (!pending) return;
        this.pending.delete(message.id);
        clearTimeout(pending.timeout);
        if (message.error) pending.reject(new Error(JSON.stringify(message.error)));
        else pending.resolve(message.result);
      } else {
        for (const callback of this.listeners.get(message.method) ?? []) callback(message.params);
      }
    });
    socket.addEventListener('close', () => this.rejectPending(new Error('Chrome debugging connection closed.')));
    socket.addEventListener('error', () => this.rejectPending(new Error('Chrome debugging connection failed.')));
  }

  static async connect(url) {
    const socket = new WebSocket(url);
    await new Promise((resolve, reject) => {
      const timeout = setTimeout(() => reject(new Error('Chrome debugging connection timed out.')), 15000);
      socket.addEventListener('open', () => { clearTimeout(timeout); resolve(); }, { once: true });
      socket.addEventListener('error', () => { clearTimeout(timeout); reject(new Error('Cannot connect to Chrome.')); }, { once: true });
    });
    return new Cdp(socket);
  }

  on(method, callback) {
    if (!this.listeners.has(method)) this.listeners.set(method, []);
    this.listeners.get(method).push(callback);
  }

  send(method, params = {}, sessionId) {
    const id = ++this.nextId;
    return new Promise((resolve, reject) => {
      const timeout = setTimeout(() => {
        this.pending.delete(id);
        reject(new Error(`Chrome command timed out: ${method}`));
      }, 30000);
      this.pending.set(id, { resolve, reject, timeout });
      this.socket.send(JSON.stringify({ id, method, params, sessionId }));
    });
  }

  rejectPending(error) {
    for (const pending of this.pending.values()) {
      clearTimeout(pending.timeout);
      pending.reject(error);
    }
    this.pending.clear();
  }

  close() {
    this.rejectPending(new Error('Chrome client closed.'));
    this.socket.close();
  }
}
