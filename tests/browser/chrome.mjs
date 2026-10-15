import { spawn } from 'node:child_process';
import { chmod, mkdir, rm, stat } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { Cdp } from './lib.mjs';

export const sleep = ms => new Promise(resolve => setTimeout(resolve, ms));

export async function launchChrome(directory) {
  const profile = path.join(directory, 'profile');
  const cache = path.join(directory, 'cache');
  const config = path.join(directory, 'config');
  // Chrome's process-singleton Unix socket has a 108-byte address limit.
  const socketRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../.artifacts/chrome', String(process.pid));
  if (Buffer.byteLength(socketRoot) > 62) throw new Error('Repository path is too long for Chrome process-singleton sockets.');
  await mkdir(profile, { recursive: true });
  await mkdir(cache, { recursive: true });
  await mkdir(config, { recursive: true });
  await mkdir(socketRoot, { recursive: true });
  await chmod(socketRoot, 0o700);
  if (((await stat(socketRoot)).mode & 0o777) !== 0o700) {
    await rm(socketRoot, { recursive: true, force: true });
    await rm(profile, { recursive: true, force: true });
    await rm(cache, { recursive: true, force: true });
    await rm(config, { recursive: true, force: true });
    throw new Error('Repository filesystem does not preserve private 0700 directories (Chrome process-singleton requires them). Run from a POSIX-permission worktree. External temporary directories are not used.');
  }
  const args = [
    '--headless=new', '--remote-debugging-address=127.0.0.1', '--remote-debugging-port=0',
    `--user-data-dir=${profile}`, `--disk-cache-dir=${cache}`,
    '--no-first-run', '--no-default-browser-check', '--disable-background-networking',
    '--disable-component-update', '--disable-sync', '--metrics-recording-only', '--no-pings',
    '--autoplay-policy=no-user-gesture-required', '--window-size=1280,800', 'about:blank',
  ];
  if (process.getuid?.() === 0) args.unshift('--no-sandbox');
  const child = spawn(process.env.CHROME_BIN || 'google-chrome', args, {
    env: { ...process.env, TMPDIR: socketRoot, XDG_CACHE_HOME: cache, XDG_CONFIG_HOME: config },
    stdio: ['ignore', 'ignore', 'pipe'],
  });
  let stderr = '';
  let cdp;
  const closed = new Promise(resolve => child.once('exit', resolve));
  try {
    const endpoint = await new Promise((resolve, reject) => {
      const timeout = setTimeout(() => reject(new Error(`Chrome did not become ready. ${stderr}`)), 30000);
      child.once('error', error => { clearTimeout(timeout); reject(error); });
      child.once('exit', (code, signal) => { clearTimeout(timeout); reject(new Error(`Chrome exited early (code=${code}, signal=${signal}). ${stderr}`)); });
      child.stderr.on('data', data => {
        stderr = (stderr + String(data)).slice(-6000);
        const match = stderr.match(/DevTools listening on (ws:\/\/127\.0\.0\.1:\d+\/devtools\/browser\/[^\s]+)/);
        if (match) { clearTimeout(timeout); resolve(match[1]); }
      });
    });
    const port = new URL(endpoint).port;
    const response = await fetch(`http://127.0.0.1:${port}/json/list`);
    if (!response.ok) throw new Error(`Chrome DevTools HTTP status ${response.status}`);
    const pages = await response.json();
    const page = pages.find(item => item.type === 'page');
    if (!page?.webSocketDebuggerUrl) throw new Error('Chrome created no inspectable page.');
    cdp = await Cdp.connect(page.webSocketDebuggerUrl);
    await Promise.all([
      cdp.send('Page.enable'),
      cdp.send('Runtime.enable'),
      cdp.send('Network.enable'),
      cdp.send('Log.enable'),
    ]);
    const version = await cdp.send('Browser.getVersion');
    return { cdp, version, close: async () => {
      cdp.close();
      if (child.exitCode === null) child.kill('SIGTERM');
      await Promise.race([closed, sleep(5000)]);
      if (child.exitCode === null) { child.kill('SIGKILL'); await closed; }
      await rm(socketRoot, { recursive: true, force: true });
      await rm(config, { recursive: true, force: true });
    } };
  } catch (error) {
    cdp?.close();
    if (child.exitCode === null) child.kill('SIGTERM');
    await Promise.race([closed, sleep(5000)]);
    if (child.exitCode === null) { child.kill('SIGKILL'); await closed; }
    await rm(socketRoot, { recursive: true, force: true });
    await rm(config, { recursive: true, force: true });
    throw error;
  }
}

export async function value(cdp, expression) {
  const result = await cdp.send('Runtime.evaluate', { expression, returnByValue: true, awaitPromise: true });
  if (result.exceptionDetails) throw new Error(`Browser evaluation failed: ${JSON.stringify(result.exceptionDetails)}`);
  return result.result.value;
}

const KEYS = {
  '1': { key: '1', code: 'Digit1', windowsVirtualKeyCode: 49, text: '1' },
  '2': { key: '2', code: 'Digit2', windowsVirtualKeyCode: 50, text: '2' },
  '3': { key: '3', code: 'Digit3', windowsVirtualKeyCode: 51, text: '3' },
  ' ': { key: ' ', code: 'Space', windowsVirtualKeyCode: 32, text: ' ' },
  w: { key: 'w', code: 'KeyW', windowsVirtualKeyCode: 87, text: 'w' },
  a: { key: 'a', code: 'KeyA', windowsVirtualKeyCode: 65, text: 'a' },
  s: { key: 's', code: 'KeyS', windowsVirtualKeyCode: 83, text: 's' },
  d: { key: 'd', code: 'KeyD', windowsVirtualKeyCode: 68, text: 'd' },
  f: { key: 'f', code: 'KeyF', windowsVirtualKeyCode: 70, text: 'f' },
  q: { key: 'q', code: 'KeyQ', windowsVirtualKeyCode: 81, text: 'q' },
  r: { key: 'r', code: 'KeyR', windowsVirtualKeyCode: 82, text: 'r' },
  Shift: { key: 'Shift', code: 'ShiftLeft', windowsVirtualKeyCode: 16 },
};

export async function key(cdp, name, down) {
  if (!KEYS[name]) throw new Error(`Unknown test key ${name}`);
  const details = { ...KEYS[name] };
  if (!down) delete details.text;
  await cdp.send('Input.dispatchKeyEvent', {
    ...details, nativeVirtualKeyCode: details.windowsVirtualKeyCode, type: down ? 'keyDown' : 'keyUp',
  });
}

export async function press(cdp, name) {
  await key(cdp, name, true);
  try { await sleep(120); } finally { await key(cdp, name, false); }
  await sleep(120);
}
