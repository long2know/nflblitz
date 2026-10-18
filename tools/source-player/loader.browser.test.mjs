import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { mkdir, writeFile } from 'node:fs/promises';
import { dirname, join } from 'node:path';
import test from 'node:test';
import { Cdp } from '../../tests/browser/lib.mjs';
import { sleep, value } from '../../tests/browser/chrome.mjs';

const args = process.argv.slice(2);
function option(name, fallback) {
  const index = args.indexOf(name);
  if (index < 0) return fallback;
  assert(args[index + 1] && !args[index + 1].startsWith('--'), `Value required for ${name}`);
  return args[index + 1];
}
const url = option('--url', 'http://127.0.0.1:8082/');
const port = Number(option('--cdp-port', '9222'));
const output = option('--evidence', 'tools/source-player/artifacts/loader-failures/evidence.json');
const receiptResponse = await fetch(new URL('build-info.json', url));
assert.equal(receiptResponse.status, 200);
const receiptBytes = Buffer.from(await receiptResponse.arrayBuffer());
const receipt = JSON.parse(receiptBytes);
const manifestBytes = Buffer.from(await (await fetch(new URL('data/manifest.json', url))).arrayBuffer());
const sha = bytes => createHash('sha256').update(bytes).digest('hex');
assert.equal(sha(manifestBytes), receipt.dataManifestSha256, 'Test the real pinned emitted data manifest.');
const manifest = JSON.parse(manifestBytes);
const model = 'kernel/models/plyr266.h';
assert(manifest.files.some(file => file.path === model), 'Block a real manifest-listed original model, not a dummy fixture.');
const version = await (await fetch(`http://127.0.0.1:${port}/json/version`)).json();
const browser = await Cdp.connect(version.webSocketDebuggerUrl);
const evidence = {
  schema: 'source-player-loader-browser-regression/v1', url, browserVersion: version.Browser,
  receiptSha256: sha(receiptBytes), dataManifestSha256: sha(manifestBytes), listedFiles: manifest.files.length,
  method: 'New owned tabs only; page-local Fetch interception of actual emitted requests. No source, asset or gameplay-state mutation.',
  cases: [],
};
await mkdir(dirname(output), { recursive: true });

async function until(predicate, description, timeout = 15000) {
  const deadline = Date.now() + timeout;
  while (Date.now() < deadline) {
    if (await predicate()) return;
    await sleep(50);
  }
  throw new Error(`Timed out: ${description}`);
}

async function status(page) {
  return value(page, `(() => {
    const snapshot = globalThis.sourcePlayer?.snapshot();
    const status = document.querySelector('#status');
    return { text: status?.textContent, className: status?.className,
      failed: snapshot?.failed ?? globalThis.sourcePlayerLoader?.failed ?? false,
      ready: snapshot?.ready ?? false, sourcePlayerPresent: !!globalThis.sourcePlayer,
      documentComplete: document.readyState === 'complete' };
  })()`);
}

function terminal(observation, resource, messages) {
  assert.equal(observation.failed, true, 'The actual loader must stay failed.');
  assert.equal(observation.ready, false, 'A missing required resource must never earn Ready.');
  assert.equal(observation.className, 'failed', 'The visible status must retain failed styling.');
  assert.match(observation.text, /^SOURCE PLAYER STOPPED:/, 'The visible status must be a terminal error, not Loading/Verifying.');
  assert(observation.text.includes(resource), `The terminal error must identify the failed resource: ${resource}`);
  assert.equal(messages.some(message => message.startsWith('SOURCE_PLAYER_READY ')), false);
}

async function ownedCase(name, action) {
  const result = { name, status: 'running', messages: [], networkFailures: [], interceptionErrors: [] };
  evidence.cases.push(result);
  let targetId, page;
  try {
    ({ targetId } = await browser.send('Target.createTarget', { url: 'about:blank', newWindow: false }));
    const targets = await (await fetch(`http://127.0.0.1:${port}/json/list`)).json();
    page = await Cdp.connect(targets.find(target => target.id === targetId).webSocketDebuggerUrl);
    result.ownedTargetId = targetId;
    page.on('Runtime.consoleAPICalled', event => result.messages.push(event.args.map(arg => arg.value ?? arg.description ?? '').join(' ')));
    page.on('Network.loadingFailed', event => result.networkFailures.push(event));
    await Promise.all([page.send('Page.enable'), page.send('Runtime.enable'), page.send('Network.enable')]);
    await page.send('Network.setCacheDisabled', { cacheDisabled: true });
    await page.send('Page.bringToFront');
    await action(page, result);
    assert.deepEqual(result.interceptionErrors, [], 'Page-local request interception must complete without harness errors.');
    result.status = 'passed';
  } catch (error) {
    result.status = 'failed';
    result.error = String(error.stack || error);
    throw error;
  } finally {
    if (page) {
      try {
        result.final = await status(page);
        const screenshot = await page.send('Page.captureScreenshot', { format: 'png' });
        await writeFile(join(dirname(output), name + '.png'), Buffer.from(screenshot.data, 'base64'));
        await page.send('Fetch.disable');
      } finally {
        page.close();
        if (targetId) await browser.send('Target.closeTarget', { targetId });
        result.ownedTargetClosed = true;
      }
    } else if (targetId) {
      await browser.send('Target.closeTarget', { targetId });
      result.ownedTargetClosed = true;
    }
  }
}

await test('a failed listed original asset remains terminal after other real download workers settle', async () => {
  await ownedCase('missing-original-model', async (page, result) => {
    const modelUrl = new URL('data/' + model, url).href;
    const dataUrl = new URL('data/', url).href;
    const active = new Set(), held = [];
    let blocked, released = false, lastNetwork = Date.now();
    page.on('Network.requestWillBeSent', event => {
      if (event.request.url.startsWith(dataUrl)) { active.add(event.requestId); lastNetwork = Date.now(); }
    });
    for (const eventName of ['Network.loadingFinished', 'Network.loadingFailed'])
      page.on(eventName, event => { if (active.delete(event.requestId)) lastNetwork = Date.now(); });
    page.on('Fetch.requestPaused', event => {
      const handle = async () => {
        if (event.request.url === modelUrl && !event.responseStatusCode) { blocked = event; return; }
        if (blocked && !released && event.request.url !== new URL('data/manifest.json', url).href) {
          held.push(event);
          return;
        }
        await page.send('Fetch.continueRequest', { requestId: event.requestId });
      };
      handle().catch(error => result.interceptionErrors.push(String(error)));
    });
    await page.send('Fetch.enable', { patterns: [
      { urlPattern: modelUrl, requestStage: 'Request' },
      { urlPattern: dataUrl + '*', requestStage: 'Response' },
    ] });
    await page.send('Page.navigate', { url });
    try {
      await until(() => blocked && held.length === 5, 'one real listed model and five other pending workers', 30000);
      result.blockedResource = model;
      result.otherPendingActualResponses = held.map(event => ({
        url: event.request.url, status: event.responseStatusCode, networkId: event.networkId,
      }));
      assert(result.otherPendingActualResponses.every(response => response.status === 200),
        'The other workers must have real successful HTTP responses pending, not dummy data.');
      await page.send('Fetch.failRequest', { requestId: blocked.requestId, errorReason: 'Failed' });
      await until(async () => (await status(page)).failed, 'actual first asset failure');
      result.firstFailure = await status(page);
    } finally {
      released = true;
      await Promise.all(held.map(event => page.send('Fetch.continueRequest', { requestId: event.requestId })));
    }
    await until(() => active.size === 0 && Date.now() - lastNetwork >= 500, 'all real outstanding asset workers settling', 60000);
    result.afterWorkersSettled = await status(page);
    assert.equal(result.afterWorkersSettled.text, result.firstFailure.text,
      'Late successful download workers overwrote the first terminal error.');
    terminal(result.afterWorkersSettled, model, result.messages);
  });
});

for (const module of ['main.js', '_framework/dotnet.js', 'renderer.js', 'input.js']) {
  await test(`missing emitted ${module} fails visibly before the main module graph can execute`, async () => {
    await ownedCase('missing-' + module.replaceAll('/', '-'), async (page, result) => {
      const moduleUrl = new URL(module, url).href;
      let blocked = false;
      page.on('Fetch.requestPaused', event => {
        result.blockedResource = event.request.url;
        page.send('Fetch.failRequest', { requestId: event.requestId, errorReason: 'Failed' })
          .then(() => { blocked = true; })
          .catch(error => result.interceptionErrors.push(String(error)));
      });
      await page.send('Fetch.enable', { patterns: [{ urlPattern: moduleUrl, requestStage: 'Request' }] });
      await page.send('Page.navigate', { url });
      await until(async () => blocked && (await status(page)).documentComplete, 'actual required module failure and document completion');
      await sleep(500);
      result.firstFailure = await status(page);
      terminal(result.firstFailure, module, result.messages);
      await sleep(500);
      result.afterSettled = await status(page);
      assert.equal(result.afterSettled.text, result.firstFailure.text, 'The first bootstrap error must remain terminal.');
      terminal(result.afterSettled, module, result.messages);
    });
  });
}

browser.close();
evidence.status = evidence.cases.every(item => item.status === 'passed') ? 'passed' : 'failed';
await writeFile(output, JSON.stringify(evidence, null, 2) + '\n');
