import { inflateSync } from 'node:zlib';

export function safeUrl(value) {
  try {
    const url = new URL(value);
    return `${url.protocol}//${url.host}${url.pathname}`;
  } catch {
    return String(value).slice(0, 300);
  }
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

export function readVisibleHud(fullText, headerText) {
  const header = parseHud(headerText);
  return { ...parseHud(fullText), homeScore: header.homeScore, awayScore: header.awayScore };
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

export function pngStats(png) {
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
  let green = 0;
  let bright = 0;
  const colors = new Set();
  for (let y = 0; y < height; y++) {
    const filter = raw[y * (stride + 1)];
    const row = Buffer.from(raw.subarray(y * (stride + 1) + 1, (y + 1) * (stride + 1)));
    if (filter > 4) throw new Error('Unknown PNG scanline filter.');
    for (let x = 0; x < stride; x++) {
      const left = x >= channels ? row[x - channels] : 0;
      const up = previous[x];
      const upperLeft = x >= channels ? previous[x - channels] : 0;
      const predictor = [0, left, up, Math.floor((left + up) / 2), paeth(left, up, upperLeft)][filter];
      row[x] = (row[x] + predictor) & 255;
    }
    for (let x = 0; x < stride; x += channels) {
      const [r, g, b] = row.subarray(x, x + 3);
      if (g > 35 && g > r * 1.2 && g > b * 1.05) green++;
      if (r > 180 && g > 180 && b > 180) bright++;
      colors.add((r >> 4) * 256 + (g >> 4) * 16 + (b >> 4));
    }
    previous = row;
  }
  return { width, height, greenFraction: green / (width * height), brightFraction: bright / (width * height), colors: colors.size };
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

  send(method, params = {}) {
    const id = ++this.nextId;
    return new Promise((resolve, reject) => {
      const timeout = setTimeout(() => {
        this.pending.delete(id);
        reject(new Error(`Chrome command timed out: ${method}`));
      }, 30000);
      this.pending.set(id, { resolve, reject, timeout });
      this.socket.send(JSON.stringify({ id, method, params }));
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
