import assert from 'node:assert/strict';
import { mkdir, writeFile } from 'node:fs/promises';
import { Cdp } from '../../tests/browser/lib.mjs';
import { sleep, value } from '../../tests/browser/chrome.mjs';

const args = process.argv.slice(2);
const option = (name, fallback) => args.includes(name) ? args[args.indexOf(name) + 1] : fallback;
const url = option('--url', 'http://127.0.0.1:8083/');
const port = Number(option('--cdp-port', '9222'));
const output = option('--evidence', 'tools/source-player/artifacts/feature-options/evidence.json');
const version = await (await fetch(`http://127.0.0.1:${port}/json/version`)).json();
const browser = await Cdp.connect(version.webSocketDebuggerUrl);
const evidence = { schema: 'source-player-feature-options-browser/v1', url, cases: [],
  method: 'Owned real tabs, URL opt-ins and public R key only. No gameplay/state/asset injection.', nativeQualified: false };
await mkdir(output.slice(0, output.lastIndexOf('/')), { recursive: true });
try {
  for (const [query, fire, meters] of [
    ['', false, 'Disabled'],
    ['#ignored?source-compatibility=strict&source-fire=portable', false, 'Disabled'],
    ['?source-fire=portable', true, 'Disabled'],
    ['?source-kick-meters=portable-authored', false, 'PortableAuthored'],
    ['?source-fire=portable&source-kick-meters=portable-authored', true, 'PortableAuthored'],
    ['?source-fire=portable&source-fire=disabled', null, null],
    ['?source-kick-meters=native', null, null],
    ['?source-kick-meters=portable-authored&source-kick-meters=disabled', null, null],
    ['?sourceprofile=original-teams-contact272&source-fire=portable', null, null],
  ]) {
    const row = { query, status: 'running', messages: [] }; evidence.cases.push(row);
    const { targetId } = await browser.send('Target.createTarget', { url: 'about:blank', newWindow: false });
    let page;
    try {
      const targets = await (await fetch(`http://127.0.0.1:${port}/json/list`)).json();
      page = await Cdp.connect(targets.find(t => t.id === targetId).webSocketDebuggerUrl);
      await Promise.all([page.send('Runtime.enable'), page.send('Page.enable')]);
      page.on('Runtime.consoleAPICalled', event => row.messages.push(event.args.map(a => a.value ?? a.description ?? '').join(' ')));
      await page.send('Page.navigate', { url: new URL(query || '.', url).href });
      await page.send('Page.bringToFront');
      const until = async predicate => {
        const deadline = Date.now() + 180000;
        while (Date.now() < deadline) {
          const result = await value(page, `({
            snapshot:globalThis.sourcePlayer?.snapshot(),
            loaderFailed:globalThis.sourcePlayerLoader?.failed,
            text:document.getElementById('status')?.textContent,
            className:document.getElementById('status')?.className
          })`);
          if (predicate(result)) return result;
          await sleep(100);
        }
        throw new Error('Actual feature URL did not become ready or explicitly terminal: ' + query);
      };
      if (fire === null) {
        const failed = await until(r => r.loaderFailed || r.snapshot?.failed);
        assert.equal(failed.snapshot?.ready ?? false, false);
        assert.equal(failed.className, 'failed');
        assert.match(failed.text, /^SOURCE PLAYER STOPPED:/);
        assert.equal(row.messages.some(m => m.startsWith('SOURCE_PLAYER_READY ')), false);
        row.terminal = failed;
      } else {
        const loaded = (await until(r => r.snapshot?.ready || r.snapshot?.failed)).snapshot;
        assert.equal(loaded.failed, false);
        assert.equal(loaded.state.effects.enabled, fire);
        assert.equal(loaded.state.kick.mode, meters);
        assert.equal(loaded.state.actorCount, 14);
        assert.equal(loaded.state.kernel.policy, 'Portable');
        const proof = await value(page, 'sourcePlayer.gpuProof()');
        assert(proof.visibleHome > 0 && proof.visibleAway > 0);
        row.loaded = loaded; row.gpu = proof;
        await page.send('Input.dispatchKeyEvent', { type: 'keyDown', key: 'r', code: 'KeyR', windowsVirtualKeyCode: 82 });
        await sleep(80);
        await page.send('Input.dispatchKeyEvent', { type: 'keyUp', key: 'r', code: 'KeyR', windowsVirtualKeyCode: 82 });
        row.restarted = (await until(r => r.snapshot?.state?.epoch === 1 && r.snapshot.state.sourceTick >= 2)).snapshot;
        const resetProof = await value(page, 'sourcePlayer.gpuProof()');
        assert.equal(row.restarted.state.effects.epoch, 1);
        assert.equal(resetProof.textureCount, proof.textureCount, 'Public restart must not upload the original textures twice.');
        assert.equal(row.restarted.state.effects.enabled, fire);
        assert.equal(row.restarted.state.kick.mode, meters);
      }
      row.status = 'passed';
    } catch (error) {
      row.status = 'failed'; row.error = String(error.stack || error); throw error;
    } finally {
      page?.close();
      await browser.send('Target.closeTarget', { targetId });
      row.ownedTargetClosed = true;
    }
  }
  evidence.status = 'passed';
} catch (error) {
  evidence.status = 'failed'; evidence.error = String(error.stack || error); process.exitCode = 1;
} finally {
  browser.close();
  await writeFile(output, JSON.stringify(evidence, null, 2) + '\n');
  console.log(JSON.stringify({ status: evidence.status, cases: evidence.cases.map(c => ({ query: c.query, status: c.status })), error: evidence.error }, null, 2));
}
