import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { createHash } from 'node:crypto';
import { readFile, lstat, writeFile } from 'node:fs/promises';
import { createServer } from 'node:http';
import { dirname, extname, resolve, sep } from 'node:path';
import { fileURLToPath } from 'node:url';
import { Cdp } from '../../browser/lib.mjs';
import { createChromeRuntime } from '../../browser/runtime.mjs';

const here = dirname(fileURLToPath(import.meta.url));
const project = resolve(here, '../../..');
const pause = ms => new Promise(resolve => setTimeout(resolve, ms));

async function value(page, expression) {
  const result = await page.send('Runtime.evaluate', { expression, returnByValue: true, awaitPromise: true });
  if (result.exceptionDetails) throw new Error('RECORD_BROWSER_EVALUATION_FAILED: ' +
    (result.exceptionDetails.exception?.description || result.exceptionDetails.text));
  return result.result.value;
}

async function ready(page, name) {
  const deadline = Date.now() + 45000;
  while (Date.now() < deadline) {
    if (await value(page, `globalThis.${name}?.ready === true`)) return;
    await pause(40);
  }
  throw new Error('RECORD_BROWSER_READY_TIMEOUT_' + name);
}

async function navigate(page, url, name) {
  await page.send('Page.navigate', { url });
  await ready(page, name);
}

async function serverFor(bundle) {
  const requests = [], mime = { '.js': 'text/javascript', '.mjs': 'text/javascript',
    '.wasm': 'application/wasm', '.json': 'application/json', '.html': 'text/html' };
  const server = createServer(async (request, response) => {
    try {
      const path = new URL(request.url, 'http://127.0.0.1').pathname;
      requests.push(path);
      response.setHeader('Cache-Control', 'no-store');
      if (path === '/health') { response.end('owned-browser-records'); return; }
      if (path === '/' || path === '/interop/') {
        response.setHeader('Content-Type', 'text/html');
        const script = path === '/' ? '/page.mjs' : '/interop/interop.mjs';
        response.end(`<!doctype html><meta charset="utf-8"><title>Owned synthetic record component verification</title>
<p>Real IndexedDB / managed browser boundary, not the cabinet UI.</p><script type="module" src="${script}"></script>`);
        return;
      }
      let file;
      if (path === '/records.js') file = resolve(project, 'tools/source-player/wwwroot/records.js');
      else if (path === '/page.mjs') file = resolve(here, 'page.mjs');
      else if (bundle && path.startsWith('/interop/')) {
        file = resolve(bundle, decodeURIComponent(path.slice('/interop/'.length)));
        assert(file.startsWith(bundle + sep), 'Serve only the explicitly owned browser-check bundle.');
      } else { response.writeHead(404); response.end(); return; }
      response.setHeader('Content-Type', mime[extname(file)] || 'application/octet-stream');
      response.end(await readFile(file));
    } catch (error) {
      response.writeHead(error.code === 'ENOENT' ? 404 : 500);
      response.end('RECORD_TEST_STATIC_RESOURCE_FAILED');
    }
  });
  await new Promise((ok, reject) => {
    server.once('error', reject);
    server.listen(0, '127.0.0.1', ok);
  });
  const port = server.address().port;
  assert(port > 8097, 'Keep protected game listeners untouched.');
  const url = `http://127.0.0.1:${port}/`;
  assert.equal(await (await fetch(url + 'health')).text(), 'owned-browser-records');
  return { url, requests,
    close: () => new Promise((ok, reject) => server.close(error => error ? reject(error) : ok())) };
}

async function sandboxedChrome() {
  assert.notEqual(process.getuid?.(), 0, 'Run normal Chrome as the existing unprivileged user.');
  const runtime = await createChromeRuntime();
  const executable = '/opt/google/chrome/chrome';
  const args = ['--headless=new', '--remote-debugging-address=127.0.0.1', '--remote-debugging-port=0',
    `--user-data-dir=${runtime.profile}`, `--disk-cache-dir=${runtime.cache}`, '--no-first-run',
    '--no-default-browser-check', '--disable-background-networking', '--disable-component-update',
    '--disable-sync', '--metrics-recording-only', '--no-pings', '--window-size=1280,900', 'about:blank'];
  const child = spawn(executable, args, { stdio: ['ignore', 'ignore', 'pipe'], env: {
    ...process.env, HOME: runtime.directory, TMPDIR: runtime.tmp, XDG_RUNTIME_DIR: runtime.tmp,
    XDG_CACHE_HOME: runtime.cache, XDG_CONFIG_HOME: runtime.config, XDG_DATA_HOME: runtime.config,
  } });
  let browser, stderr = '', exited = false, endpoint;
  const pages = [];
  const stopped = new Promise(ok => child.once('close', () => { exited = true; ok(); }));
  const close = async () => {
    for (const page of pages) page.close();
    browser?.close();
    if (!exited) child.kill('SIGTERM');
    let timer;
    await Promise.race([stopped, new Promise(ok => { timer = setTimeout(ok, 5000); })]);
    clearTimeout(timer);
    if (!exited) { child.kill('SIGKILL'); await stopped; }
    await runtime.cleanup();
  };
  try {
    endpoint = await new Promise((ok, reject) => {
      const timer = setTimeout(() => reject(new Error('RECORD_CHROME_START_TIMEOUT: ' + stderr)), 30000);
      child.once('error', error => { clearTimeout(timer); reject(error); });
      child.once('exit', (code, signal) => {
        clearTimeout(timer); reject(new Error(`RECORD_CHROME_EXIT_${code}_${signal}: ${stderr}`));
      });
      child.stderr.on('data', data => {
        stderr = (stderr + String(data)).slice(-6000);
        const match = stderr.match(/DevTools listening on (ws:\/\/127\.0\.0\.1:\d+\/devtools\/browser\/[^\s]+)/);
        if (match) { clearTimeout(timer); ok(match[1]); }
      });
    });
    browser = await Cdp.connect(endpoint);
    const port = new URL(endpoint).port;
    const newPage = async () => {
      const { targetId } = await browser.send('Target.createTarget', { url: 'about:blank' });
      const response = await fetch(`http://127.0.0.1:${port}/json/list`, { signal: AbortSignal.timeout(5000) });
      assert.equal(response.status, 200, 'Verify actual owned Chrome/CDP responsiveness.');
      const targets = await response.json(), target = targets.find(item => item.id === targetId);
      assert(target?.webSocketDebuggerUrl);
      const page = await Cdp.connect(target.webSocketDebuggerUrl);
      pages.push(page);
      await Promise.all([page.send('Page.enable'), page.send('Runtime.enable'), page.send('Network.enable')]);
      return page;
    };
    return { newPage, version: await browser.send('Browser.getVersion'), args, pid: child.pid, close };
  } catch (error) { await close(); throw error; }
}

function requireManaged(result, status, generation) {
  assert.equal(result.status, status);
  if (generation !== undefined) assert.equal(result.Generation, generation);
  assert.equal(result.RecordCount, 1);
  assert.equal(result.customExact, true, 'Inspect actual source compact-grid bytes, not just a successful status.');
  assert.equal(result.audiblesExact, true, 'Inspect actual signed custom audible IDs after managed reload.');
}

export async function runNativeRecords() {
  const output = process.env.SOURCE_RECORDS_BROWSER_EVIDENCE;
  assert(output, 'Specify a new owned JSON evidence file.');
  const destination = resolve(output), owned = resolve(project, '.squad/.scratch/browser-records-20261030');
  assert(destination.startsWith(owned + sep), 'Evidence stays in this owned ignored task root.');
  const parent = await lstat(dirname(destination));
  assert(parent.isDirectory() && !parent.isSymbolicLink());
  const bundle = process.env.SOURCE_RECORDS_APP_BUNDLE ? resolve(process.env.SOURCE_RECORDS_APP_BUNDLE) : null;
  if (bundle) assert(bundle.startsWith(owned + sep), 'Use the actual newly built record browser checks.');
  const evidence = { schema: 'frontend-original-record-browser/v1', status: 'running', syntheticOwnedProfileOnly: true,
    wholeCabinetUiClaimed: false, freshBrowserProcessSameProfile: false,
    durabilityScope: 'Native IndexedDB transaction completion, same-origin tab and page reload in one normally sandboxed browser process.',
    native: {}, managed: {}, errors: [], exceptions: [] };
  let server, chrome;
  try {
    server = await serverFor(bundle);
    chrome = await sandboxedChrome();
    evidence.url = server.url;
    evidence.chrome = { version: chrome.version, args: chrome.args, pid: chrome.pid };
    const first = await chrome.newPage(), second = await chrome.newPage();
    for (const page of [first, second]) {
      page.on('Runtime.exceptionThrown', event => evidence.exceptions.push({
        text: event.exceptionDetails.text,
        description: event.exceptionDetails.exception?.description,
      }));
      page.on('Runtime.consoleAPICalled', event => {
        if (event.type === 'error') evidence.errors.push('Unexpected browser console error.');
      });
    }
    await navigate(first, server.url, 'recordsProof');
    evidence.native.initial = await value(first, 'recordsProof.initialize()');
    assert.deepEqual(evidence.native.initial, { absent: true, exactOpaqueStrings: true,
      won: true, writes: 3, strict: true, afterComplete: true });
    await first.send('Page.reload', { ignoreCache: true });
    await ready(first, 'recordsProof');
    evidence.native.reload = await value(first, 'recordsProof.seedAfterReload()');
    assert.equal(evidence.native.reload.heapIndependent, true);
    await navigate(second, server.url, 'recordsProof');
    evidence.native.newTab = await value(second, 'recordsProof.seedAfterReload()');
    evidence.native.race = await Promise.all([
      value(first, 'recordsProof.race("A")'), value(second, 'recordsProof.race("B")'),
    ]);
    assert.equal(evidence.native.race.filter(result => result.won).length, 1);
    for (const result of evidence.native.race) {
      assert.equal(result.writes, result.won ? 3 : 0);
      assert.equal(result.afterComplete, true);
      assert.equal(result.strict, true);
    }
    evidence.native.winner = await value(first, 'recordsProof.winner()');
    assert.equal(await value(second, 'recordsProof.winner()'), evidence.native.winner);
    evidence.native.stale = await value(first, 'recordsProof.stale()');
    assert.equal(evidence.native.stale.unchanged, true);
    evidence.native.abort = await value(first, 'recordsProof.abort()');
    assert.equal(evidence.native.abort.error, 'SOURCE_RECORD_STORAGE_ABORTED');
    assert.equal(evidence.native.abort.nativeTransactionAborted, true);
    assert(evidence.native.abort.successfulRequestsBeforeAbort >= 2);
    assert.equal(evidence.native.abort.completed, false);
    assert.equal(evidence.native.abort.allThreeUnchanged, true);
    evidence.native.corruption = await value(first, 'recordsProof.corruption()');
    assert.equal(evidence.native.corruption.length, 3);
    assert(evidence.native.corruption.every(result => result.preserved));
    evidence.native.closed = await value(second, 'recordsProof.close()');
    evidence.native.blocked = await value(first, 'recordsProof.blocked()');
    evidence.native.newerVersion = await value(first, 'recordsProof.versionChange()');
    evidence.native.wrongSchema = await value(first, 'recordsProof.wrongSchema()');
    await value(first, 'recordsProof.resetOwnedDatabase()');

    if (bundle) {
      const url = server.url + 'interop/';
      await navigate(first, url, 'recordsInterop');
      evidence.managed.opaque = await value(first, 'recordsInterop.opaque()');
      assert.equal(evidence.managed.opaque.managedEncodeAndDecodeExact, true);
      evidence.managed.malformed = await value(first, 'recordsInterop.malformed()');
      assert.equal(evidence.managed.malformed.status, 'IoFailure');
      assert.equal(evidence.managed.malformed.exactCodeUnitsRetained, true);
      evidence.managed.initial = await value(first, 'recordsInterop.initialize()');
      requireManaged(evidence.managed.initial, 'Saved', 5);
      assert.equal(evidence.managed.initial.slot0Saved, true);
      evidence.managed.abort = await value(first, 'recordsInterop.abortSave()');
      requireManaged(evidence.managed.abort, 'IoFailure', 5);
      assert.equal(evidence.managed.abort.IsWritable, false);
      assert.equal(evidence.managed.abort.previousCurrentReferenceAndGenerationRetained, true);
      evidence.managed.loadAfterAbort = await value(first, 'recordsInterop.load()');
      requireManaged(evidence.managed.loadAfterAbort, 'Ready', 5);
      evidence.managed.retryAfterAbort = await value(first, 'recordsInterop.retry()');
      requireManaged(evidence.managed.retryAfterAbort, 'Saved', 6);
      await first.send('Page.reload', { ignoreCache: true });
      await ready(first, 'recordsInterop');
      evidence.managed.reload = await value(first, 'recordsInterop.load()');
      requireManaged(evidence.managed.reload, 'Ready', 6);
      await navigate(second, url, 'recordsInterop');
      evidence.managed.newTab = await value(second, 'recordsInterop.load()');
      requireManaged(evidence.managed.newTab, 'Ready', 6);
      evidence.managed.race = await Promise.all([
        value(first, 'recordsInterop.race()'), value(second, 'recordsInterop.race()'),
      ]);
      assert.equal(evidence.managed.race.filter(result => result.status === 'Saved').length, 1);
      assert.equal(evidence.managed.race.filter(result => result.status === 'Conflict').length, 1);
      for (const result of evidence.managed.race) {
        requireManaged(result, result.status, result.status === 'Saved' ? 7 : 6);
        assert.equal(result.slot1Saved, result.status === 'Saved');
        assert.equal(result.IsWritable, result.status === 'Saved');
      }
      await value(first, 'recordsInterop.load()');
      await value(second, 'recordsInterop.close()');
      evidence.managed.corruption = await value(first, 'recordsInterop.corrupt()');
      evidence.managed.recovery = await value(first, 'recordsInterop.recover()');
      requireManaged(evidence.managed.recovery, 'Recovered', 8);
      assert.equal(evidence.managed.recovery.slot0Saved, true);
      assert.equal(evidence.managed.recovery.slot1Saved, false);
      evidence.managed.quarantine = await value(first, 'recordsInterop.recoveryExact()');
      await first.send('Page.reload', { ignoreCache: true });
      await ready(first, 'recordsInterop');
      evidence.managed.recoveryReload = await value(first, 'recordsInterop.load()');
      requireManaged(evidence.managed.recoveryReload, 'Ready', 8);
      evidence.managed.quarantineReload = await value(first, 'recordsInterop.recoveryExact()');
      evidence.managed.closed = await value(first, 'recordsInterop.closeAndLoad()');
      requireManaged(evidence.managed.closed, 'IoFailure', 8);
      assert.equal(evidence.managed.closed.IsWritable, false);
      await value(first, 'recordsInterop.close()');
    }
    assert.deepEqual(evidence.exceptions, [], 'No unexpected browser exception is accepted as storage failure.');
    assert.deepEqual(evidence.errors, []);
    const files = ['tools/source-player/wwwroot/records.js', 'tools/source-player/BrowserRecords.cs',
      'tests/records/browser/RecordBrowserChecks.cs', 'tests/records/browser/interop.mjs'];
    evidence.sources = await Promise.all(files.map(async file => ({
      path: file, sha256: createHash('sha256').update(await readFile(resolve(project, file))).digest('hex'),
    })));
    evidence.status = bundle ? 'native-idb-and-actual-managed-browser-pass' : 'native-idb-pass-managed-not-run';
  } catch (error) {
    evidence.status = 'failed';
    evidence.error = String(error.stack || error);
    throw error;
  } finally {
    evidence.requests = server?.requests || [];
    await chrome?.close();
    await server?.close();
    evidence.cleanup = { chromeAndOwnedRuntimeStopped: true, ownedLoopbackStopped: true };
    await writeFile(destination, JSON.stringify(evidence, null, 2) + '\n', { flag: 'wx' });
  }
}
