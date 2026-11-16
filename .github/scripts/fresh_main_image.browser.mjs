import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { mkdir, writeFile } from 'node:fs/promises';
import { Cdp } from '../../tests/browser/lib.mjs';
import { createChromeRuntime } from '../../tests/browser/runtime.mjs';

const [url, output, root] = process.argv.slice(2);
assert(new URL(url).hostname === '127.0.0.1' && new URL(url).pathname === '/nflblitz/');
assert.notEqual(process.getuid(), 0);
await mkdir(root, { mode: 0o700 });
const runtime = await createChromeRuntime({ root });
const args = ['--headless=new', '--remote-debugging-address=127.0.0.1', '--remote-debugging-port=0',
  '--user-data-dir=' + runtime.profile, '--disk-cache-dir=' + runtime.cache,
  '--no-first-run', '--no-default-browser-check', '--disable-background-networking',
  '--disable-component-update', '--disable-sync', '--metrics-recording-only', '--no-pings', 'about:blank'];
const child = spawn('/opt/google/chrome/chrome', args, { stdio: ['ignore', 'ignore', 'pipe'],
  env: { ...process.env, HOME: runtime.directory, TMPDIR: runtime.tmp, XDG_RUNTIME_DIR: runtime.tmp,
    XDG_CACHE_HOME: runtime.cache, XDG_CONFIG_HOME: runtime.config, XDG_DATA_HOME: runtime.config } });
const closed = new Promise(resolve => child.once('close', resolve));
let browser, page;
try {
  const endpoint = await new Promise((resolve, reject) => {
    let tail = '';
    const timer = setTimeout(() => reject(new Error('Normal sandbox Chrome startup timeout.')), 30000);
    child.once('error', error => { clearTimeout(timer); reject(error); });
    child.stderr.on('data', data => {
      tail = (tail + data).slice(-6000);
      const match = tail.match(/DevTools listening on (ws:\/\/127\.0\.0\.1:\d+\/devtools\/browser\/[^\s]+)/);
      if (match) { clearTimeout(timer); resolve(match[1]); }
    });
  });
  browser = await Cdp.connect(endpoint);
  const { targetId } = await browser.send('Target.createTarget', { url: 'about:blank' });
  const targets = await (await fetch(`http://127.0.0.1:${new URL(endpoint).port}/json/list`)).json();
  page = await Cdp.connect(targets.find(target => target.id === targetId).webSocketDebuggerUrl);
  await Promise.all(['Page', 'Runtime'].map(name => page.send(name + '.enable')));
  const errors = [];
  page.on('Runtime.exceptionThrown', event => errors.push(event.exceptionDetails.text));
  const value = async expression => {
    const result = await page.send('Runtime.evaluate', { expression, returnByValue: true });
    assert(!result.exceptionDetails, JSON.stringify(result.exceptionDetails));
    return result.result.value;
  };
  const until = async expression => {
    const deadline = Date.now() + 180000;
    while (Date.now() < deadline) {
      const result = await value(expression);
      if (result) return result;
      await new Promise(resolve => setTimeout(resolve, 100));
    }
    throw new Error('Actual C#/WebGL/original-audio startup timed out.');
  };
  await page.send('Page.bringToFront');
  await page.send('Page.navigate', { url });
  const rect = await until(`(() => {const b=document.querySelector('#start-original-audio');
    if(!b||b.hidden||b.disabled)return null;const r=b.getBoundingClientRect();return {x:r.x+r.width/2,y:r.y+r.height/2};})()`);
  for (const type of ['mousePressed', 'mouseReleased'])
    await page.send('Input.dispatchMouseEvent', { type, button: 'left', clickCount: 1, ...rect });
  await until(`globalThis.sourcePlayer?.observe()?.state?.cabinetSession?.stage==='Joining'`);
  await page.send('Input.dispatchKeyEvent', { type: 'keyDown', key: 'n', code: 'KeyN',
    windowsVirtualKeyCode: 78, nativeVirtualKeyCode: 78 });
  await new Promise(resolve => setTimeout(resolve, 100));
  await page.send('Input.dispatchKeyEvent', { type: 'keyUp', key: 'n', code: 'KeyN',
    windowsVirtualKeyCode: 78, nativeVirtualKeyCode: 78 });
  const audio = await until(`(() => {const a=sourcePlayer.observe().audioReceiver;
    return a?.contextState==='running'&&a.unlocked&&a.sampleCount>0&&a.signal?.rms>0&&a;})()`);
  const webgl = await value(`(() => {const g=document.querySelector('#game').getContext('webgl2');
    return g&&{version:g.getParameter(g.VERSION),contextLost:g.isContextLost()};})()`);
  assert(webgl.version.includes('WebGL 2') && !webgl.contextLost);
  assert(await value('isSecureContext&&crossOriginIsolated'));
  assert.deepEqual(errors, []);
  await writeFile(output, JSON.stringify({ accepted: true, normalSandbox: true, defaultAutoplay: true,
    trustedStartAndCabinet: true, webgl, audio, errors, chromeArgs: args }, null, 2) + '\n');
} finally {
  page?.close();
  if (browser) {
    try { await browser.send('Browser.close'); } catch {}
    browser.close();
  }
  if (child.exitCode === null) child.kill('SIGTERM');
  let timer;
  await Promise.race([closed, new Promise(resolve => { timer = setTimeout(resolve, 5000); })]);
  clearTimeout(timer);
  if (child.exitCode === null) child.kill('SIGKILL');
  await closed;
  await runtime.cleanup();
}
