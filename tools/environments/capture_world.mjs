import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { createHash } from 'node:crypto';
import { mkdir, readFile, writeFile } from 'node:fs/promises';
import path from 'node:path';
import { Cdp, pngStats } from '../../tests/browser/lib.mjs';
import { createChromeRuntime } from '../../tests/browser/runtime.mjs';
import { requireSourceFieldRegion } from './world_image_checks.mjs';

const option = name => {
  const index = process.argv.indexOf(name);
  assert.ok(index >= 0 && process.argv[index + 1], `Explicit argument required: ${name}`);
  return process.argv[index + 1];
};
const endpoint = new URL(option('--url'));
assert.equal(endpoint.origin, 'http://127.0.0.1:8186', 'World inspection must not touch live hosting.');
const expectedManifest = option('--manifest-sha256');
assert.match(expectedManifest, /^[0-9a-f]{64}$/);
const output = path.resolve(option('--output'));
const playerRoot = path.resolve(option('--player-root'));
assert.ok(output.startsWith(path.resolve('.squad/.scratch') + path.sep));
assert.ok(playerRoot.endsWith('/Build/world-preview/native-project/Build/WebGL'));
await mkdir(output, { recursive: false });
const runtimeRoot = path.resolve(process.env.CHROME_RUNTIME_ROOT || 'Build/world-chrome');
assert.ok(runtimeRoot.startsWith(path.resolve('Build') + path.sep),
  'Private Chrome runtime storage must remain under this project Build directory.');
await mkdir(runtimeRoot, { recursive: true, mode: 0o700 });
let runtime;
try {
  runtime = await createChromeRuntime({ root: runtimeRoot });
} catch (error) {
  await writeFile(path.join(output, 'report.json'), JSON.stringify({
    schema: 'original-source-world-browser/v1', status: 'failed-before-browser-start',
    expectedManifest, error: error.stack || error.message, ownedChromeStarted: false,
  }, null, 2) + '\n', { flag: 'wx' });
  throw error;
}
const args = [
  '--headless=new', '--remote-debugging-address=127.0.0.1', '--remote-debugging-port=0',
  `--user-data-dir=${runtime.profile}`, `--disk-cache-dir=${runtime.cache}`,
  '--no-first-run', '--no-default-browser-check', '--disable-background-networking',
  '--disable-component-update', '--disable-sync', '--metrics-recording-only', '--no-pings',
  '--autoplay-policy=no-user-gesture-required', '--window-size=1280,720',
  '--use-gl=angle', '--use-angle=swiftshader', '--enable-unsafe-swiftshader', 'about:blank',
];
if (process.getuid?.() === 0) args.unshift('--no-sandbox');
const executable = process.env.CHROME_BIN || 'google-chrome';
const child = spawn(executable, args, {
  env: { ...process.env, HOME: runtime.directory, TMPDIR: runtime.tmp, XDG_RUNTIME_DIR: runtime.tmp,
    XDG_CACHE_HOME: runtime.cache, XDG_CONFIG_HOME: runtime.config, XDG_DATA_HOME: runtime.config },
  stdio: ['ignore', 'ignore', 'pipe'],
});
const report = {
  schema: 'original-source-world-browser/v1', status: 'failed', expectedManifest,
  endpoint: endpoint.href, scope: 'Isolated original field2/stadpie world inspection; not main gameplay/camera/HUD/actor parity.',
  chrome: { executable, args }, captures: [], console: [], javascriptErrors: [], artifacts: [],
};
let cdp, closed = false;
const exited = new Promise(resolve => child.once('close', () => { closed = true; resolve(); }));
const sleep = ms => new Promise(resolve => setTimeout(resolve, ms));
const value = async expression => {
  const result = await cdp.send('Runtime.evaluate', { expression, returnByValue: true, awaitPromise: true });
  assert.ok(!result.exceptionDetails, JSON.stringify(result.exceptionDetails));
  return result.result.value;
};
try {
  let stderr = '';
  const debug = await new Promise((resolve, reject) => {
    const timer = setTimeout(() => reject(new Error(`Owned Chrome did not become ready: ${stderr}`)), 30000);
    child.once('error', error => { clearTimeout(timer); reject(error); });
    child.once('exit', code => { clearTimeout(timer); reject(new Error(`Chrome exited early ${code}: ${stderr}`)); });
    child.stderr.on('data', data => {
      stderr = (stderr + String(data)).slice(-6000);
      const match = stderr.match(/DevTools listening on (ws:\/\/127\.0\.0\.1:\d+\/devtools\/browser\/[^\s]+)/);
      if (match) { clearTimeout(timer); resolve(match[1]); }
    });
  });
  const pages = await (await fetch(`http://127.0.0.1:${new URL(debug).port}/json/list`)).json();
  cdp = await Cdp.connect(pages.find(p => p.type === 'page').webSocketDebuggerUrl);
  await Promise.all(['Page.enable', 'Runtime.enable', 'Network.enable', 'Log.enable'].map(method => cdp.send(method)));
  report.chrome.version = await cdp.send('Browser.getVersion');
  cdp.on('Runtime.consoleAPICalled', event => report.console.push({
    type: event.type, text: event.args.map(arg => arg.value ?? arg.description ?? '').join(' '),
  }));
  cdp.on('Runtime.exceptionThrown', event => report.javascriptErrors.push(event.exceptionDetails));
  const resources = new Set();
  cdp.on('Network.responseReceived', event => {
    const response = event.response;
    if (response.url.startsWith(endpoint.origin) && /\.(?:wasm|data|(?:framework|loader)\.js)(?:\?|$)/.test(response.url))
      resources.add(response.url);
    if (response.url.startsWith(endpoint.origin) && response.status >= 400)
      report.javascriptErrors.push({ network: response.url, status: response.status });
  });
  await cdp.send('Emulation.setDeviceMetricsOverride', { width: 1280, height: 720, deviceScaleFactor: 1, mobile: false });
  await cdp.send('Page.addScriptToEvaluateOnNewDocument', { source: `(() => {
    const get = HTMLCanvasElement.prototype.getContext;
    HTMLCanvasElement.prototype.getContext = function(kind, ...args) {
      const gl = get.call(this, kind, ...args);
      if (gl && (kind === 'webgl2' || kind === 'webgl')) {
        window.__worldGl = gl; window.__worldDraws = 0;
        for (const name of ['drawArrays','drawElements','drawArraysInstanced','drawElementsInstanced','drawRangeElements']) {
          if (typeof gl[name] !== 'function') continue;
          const original = gl[name];
          gl[name] = function(...values) { window.__worldDraws++; return original.apply(this, values); };
        }
      }
      return gl;
    };
  })();` });
  for (const view of ['overview', 'sideline', 'endzone']) {
    const start = report.console.length;
    await cdp.send('Page.navigate', { url: `${endpoint.origin}/?environment=source&view=${view}` });
    await cdp.send('Page.bringToFront');
    let ready = false;
    for (let attempt = 0; attempt < 180; attempt++) {
      ready = report.console.slice(start).some(e => e.text.includes(`WORLD_PREVIEW_READY manifest=${expectedManifest} draws=78`));
      if (ready) break;
      if (report.javascriptErrors.length || report.console.slice(start).some(e => e.type === 'error')) break;
      await sleep(1000);
    }
    assert.ok(ready, `Actual Unity source-world provider did not become ready (${view}).`);
    await sleep(2500);
    const renderer = await value(`(() => {
      const gl = window.__worldGl; if (!gl) return null;
      const debug = gl.getExtension('WEBGL_debug_renderer_info');
      return { renderer: gl.getParameter(gl.RENDERER), version: gl.getParameter(gl.VERSION),
        unmaskedRenderer: debug ? gl.getParameter(debug.UNMASKED_RENDERER_WEBGL) : null,
        contextLost: gl.isContextLost(), width: gl.drawingBufferWidth, height: gl.drawingBufferHeight,
        actualDrawSubmissions: window.__worldDraws, pageFocused: document.hasFocus() };
    })()`);
    assert.ok(renderer && !renderer.contextLost && renderer.actualDrawSubmissions > 0 && renderer.pageFocused,
      'An actual initialized/drawing/focused Unity WebGL context is required.');
    const { data } = await cdp.send('Page.captureScreenshot', { format: 'png' });
    const png = Buffer.from(data, 'base64');
    const stats = pngStats(png);
    assert.ok(stats.colors > 512 && stats.greenFraction > 0.02,
      'Actual textured field/stadium must be visible, not an off-camera/blank world.');
    const fieldRegion = view === 'sideline' ? requireSourceFieldRegion(png) : null;
    const filename = `original-world-${view}.png`;
    await writeFile(path.join(output, filename), png, { flag: 'wx' });
    report.captures.push({ view, file: filename, bytes: png.length,
      sha256: createHash('sha256').update(png).digest('hex'), renderer, pngStats: stats, fieldRegion,
      inspectionCameraOnly: true, gameplayCameraUnchanged: true });
  }
  assert.equal(resources.size, 4, 'Exactly the actual Unity loader/framework/WASM/data payloads must be observed.');
  for (const url of resources) {
    const response = await fetch(url);
    assert.equal(response.status, 200);
    const bytes = Buffer.from(await response.arrayBuffer());
    const relative = decodeURIComponent(new URL(url).pathname.slice(1));
    assert.ok(relative.startsWith('Build/') && !relative.includes('..'));
    const local = await readFile(path.join(playerRoot, relative));
    assert.deepEqual(bytes, local, `Served world player differs: ${relative}`);
    report.artifacts.push({ path: relative, bytes: bytes.length, contentType: response.headers.get('content-type'),
      sha256: createHash('sha256').update(bytes).digest('hex') });
  }
  assert.equal(report.javascriptErrors.length, 0, 'No JS/runtime/network errors are ignored.');
  assert.equal(report.console.filter(e => e.type === 'error').length, 0, 'No original material/source-world errors are ignored.');
  report.status = 'passed';
} catch (error) {
  report.error = error.stack || error.message;
  process.exitCode = 1;
} finally {
  cdp?.close();
  if (!closed) child.kill('SIGTERM');
  await Promise.race([exited, sleep(5000)]);
  if (!closed) { child.kill('SIGKILL'); await exited; }
  await runtime.cleanup();
  report.ownedChromeStopped = true;
  await writeFile(path.join(output, 'report.json'), JSON.stringify(report, null, 2) + '\n', { flag: 'wx' });
  console.log(JSON.stringify({ status: report.status, views: report.captures.length, error: report.error,
    report: path.relative(process.cwd(), path.join(output, 'report.json')) }));
}
