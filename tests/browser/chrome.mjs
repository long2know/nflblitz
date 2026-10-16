import { spawn } from 'node:child_process';
import { Cdp } from './lib.mjs';
import { createChromeRuntime } from './runtime.mjs';

export const sleep = ms => new Promise(resolve => setTimeout(resolve, ms));

export async function launchChrome(directory) {
  const runtime = await createChromeRuntime();
  const { profile, cache, config } = runtime;
  const args = [
    '--headless=new', '--remote-debugging-address=127.0.0.1', '--remote-debugging-port=0',
    `--user-data-dir=${profile}`, `--disk-cache-dir=${cache}`,
    '--no-first-run', '--no-default-browser-check', '--disable-background-networking',
    '--disable-component-update', '--disable-sync', '--metrics-recording-only', '--no-pings',
    '--autoplay-policy=no-user-gesture-required', '--window-size=1280,800', 'about:blank',
  ];
  if (process.getuid?.() === 0) args.unshift('--no-sandbox');
  const executable = process.env.CHROME_BIN || 'google-chrome';
  const child = spawn(executable, args, {
    env: { ...process.env, HOME: runtime.directory, TMPDIR: runtime.tmp, XDG_RUNTIME_DIR: runtime.tmp,
      XDG_CACHE_HOME: cache, XDG_CONFIG_HOME: config, XDG_DATA_HOME: config },
    stdio: ['ignore', 'ignore', 'pipe'],
  });
  let stderr = '';
  let cdp;
  let exited = false;
  const closed = new Promise(resolve => child.once('close', () => { exited = true; resolve(); }));
  const stop = async () => {
    cdp?.close();
    if (!exited) child.kill('SIGTERM');
    let stopTimer;
    await Promise.race([closed, new Promise(resolve => { stopTimer = setTimeout(resolve, 5000); })]);
    clearTimeout(stopTimer);
    if (!exited) { child.kill('SIGKILL'); await closed; }
    await runtime.cleanup();
  };
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
    const response = await fetch(`http://127.0.0.1:${port}/json/list`, { signal: AbortSignal.timeout(5000) });
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
    return { cdp, version, launch: { executable, args, runtime: runtime.directory, evidenceDirectory: directory }, close: stop };
  } catch (error) {
    await stop();
    throw error;
  }
}

export async function value(cdp, expression) {
  const result = await cdp.send('Runtime.evaluate', { expression, returnByValue: true, awaitPromise: true });
  if (result.exceptionDetails) throw new Error(`Browser evaluation failed: ${JSON.stringify(result.exceptionDetails)}`);
  return result.result.value;
}

export async function targetSession(cdp, targetId) {
  const { sessionId } = await cdp.send('Target.attachToTarget', { targetId, flatten: true });
  if (!sessionId) throw new Error(`Chrome did not attach to its owned target ${targetId}`);
  return { send: (method, params) => cdp.send(method, params, sessionId) };
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
