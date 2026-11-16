import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { mkdir, writeFile } from 'node:fs/promises';
import { createHash } from 'node:crypto';
import { Cdp } from '../../tests/browser/lib.mjs';
import { createChromeRuntime } from '../../tests/browser/runtime.mjs';
import { sleep, value } from '../../tests/browser/chrome.mjs';

const option = name => {
  const at = process.argv.indexOf(name);
  assert(at >= 0 && process.argv[at + 1], `Explicit ${name} required.`);
  return process.argv[at + 1];
};
const before = option('--before'), after = option('--after'), output = option('--output');
for (const url of [before, after]) assert.equal(new URL(url).hostname, '127.0.0.1');
assert.notEqual(process.getuid(), 0, 'Use the normal Chrome sandbox as a non-root user.');
await mkdir(output, { recursive: true });
const runtime = await createChromeRuntime();
const args = ['--headless=new', '--enable-automation', '--remote-debugging-address=127.0.0.1',
  '--remote-debugging-port=0', `--user-data-dir=${runtime.profile}`, `--disk-cache-dir=${runtime.cache}`,
  '--no-first-run', '--no-default-browser-check', '--window-size=1280,960', 'about:blank'];
const report = { schema: 'original-record-choice-browser/v1', status: 'running', args,
  method: 'Fresh C#/WebGL builds; trusted public mouse/keyboard input; passive Present packet observation. Normal sandbox and default autoplay.',
  frames: [], errors: [], runs: [], input: [] };
const child = spawn(process.env.CHROME_BIN || 'google-chrome', args, {
  env: { ...process.env, HOME: runtime.directory, TMPDIR: runtime.tmp, XDG_RUNTIME_DIR: runtime.tmp,
    XDG_CACHE_HOME: runtime.cache, XDG_CONFIG_HOME: runtime.config },
  stdio: ['ignore', 'ignore', 'pipe'],
});
let cdp, stderr = '';
const closed = new Promise(resolve => child.once('close', resolve));
try {
  const endpoint = await new Promise((resolve, reject) => {
    const timer = setTimeout(() => reject(new Error(stderr)), 30000);
    child.once('error', error => { clearTimeout(timer); reject(error); });
    child.once('exit', code => { clearTimeout(timer); reject(new Error(`Chrome exit ${code}: ${stderr}`)); });
    child.stderr.on('data', data => {
      stderr += String(data);
      const match = stderr.match(/DevTools listening on (ws:\/\/127\.0\.0\.1:\d+\/devtools\/browser\/[^\s]+)/);
      if (match) { clearTimeout(timer); resolve(match[1]); }
    });
  });
  const pages = await (await fetch(`http://127.0.0.1:${new URL(endpoint).port}/json/list`)).json();
  cdp = await Cdp.connect(pages.find(page => page.type === 'page').webSocketDebuggerUrl);
  await Promise.all(['Page.enable', 'Runtime.enable'].map(method => cdp.send(method)));
  cdp.on('Runtime.exceptionThrown', event => report.errors.push(event.exceptionDetails));
  report.browser = await cdp.send('Browser.getVersion');
  report.actualCommandLine = await cdp.send('Browser.getBrowserCommandLine');
  assert(!report.actualCommandLine.arguments.some(arg => /no-sandbox|disable.*sandbox|autoplay-policy/.test(arg)));
  const until = async (expression, label, timeout = 180000) => {
    const deadline = Date.now() + timeout;
    while (Date.now() < deadline) {
      const found = await value(cdp, expression);
      if (found) return found;
      assert.equal(report.errors.length, 0, JSON.stringify(report.errors));
      await sleep(30);
    }
    throw new Error(`Timed out: ${label}`);
  };
  const key = async (name, down) => {
    const [code, number] = { n: ['KeyN', 78], v: ['KeyV', 86], w: ['KeyW', 87], s: ['KeyS', 83],
      Enter: ['Enter', 13], NumpadEnter: ['NumpadEnter', 13] }[name];
    report.input.push({ name, code, down });
    await cdp.send('Input.dispatchKeyEvent', {
      type: down ? 'keyDown' : 'keyUp', key: name === 'NumpadEnter' ? 'Enter' : name, code,
      windowsVirtualKeyCode: number, nativeVirtualKeyCode: number,
      location: name === 'NumpadEnter' ? 3 : 0, isKeypad: name === 'NumpadEnter',
    });
  };
  const tap = async name => { await key(name, true); await sleep(70); await key(name, false); await sleep(165); };
  const capture = async name => {
    const observed = await value(cdp, 'sourcePlayer.observe()');
    assert(!observed.failed, 'The actual player must not have failed before capture.');
    const rect = await value(cdp, `(() => {
      const r = document.querySelector('#game').getBoundingClientRect();
      return { x:r.x, y:r.y, width:r.width, height:r.height, scale:1 };
    })()`);
    const image = await cdp.send('Page.captureScreenshot', { format: 'png', clip: rect });
    const png = Buffer.from(image.data, 'base64');
    await writeFile(`${output}/${name}.png`, png);
    report.frames.push({ name, observed, packet: await value(cdp, '__recordFrames.at(-1)'),
      pngSha256: createHash('sha256').update(png).digest('hex') });
    console.log(JSON.stringify({ captured: name }));
  };
  const variants = [['before', before, false]];
  if (!process.argv.includes('--before-only')) variants.push(['after', after, false], ['after-yes', after, true]);
  for (const [label, url, answer] of variants) {
    await cdp.send('Page.navigate', { url });
    await until("document.querySelector('#start-original-audio')?.hidden === false", 'original resource verification');
    await value(cdp, `(async () => {
      globalThis.__recordFrames = [];
      const { SourceRenderer } = await import('./renderer.js');
      const present = SourceRenderer.prototype.present;
      SourceRenderer.prototype.present = function(...args) {
        const status = JSON.parse(args[9]);
        if (status.records?.active) {
          __recordFrames.push({ tick:status.preGameAudio.SourceTick, menuTick:status.records.menuSourceTick,
            textures:Array.from(args[7]).filter(t =>
            /original\\/(?:yes_[lr]|no_[lr]|[yn]glw[lr]\\d+)/.test(t)), text:status.hud.text,
            stations:status.records.stations });
          if (__recordFrames.length > 1000) __recordFrames.shift();
        }
        return present.apply(this, args);
      };
    })()`);
    const point = await value(cdp, `(() => {
      const r = document.querySelector('#start-original-audio').getBoundingClientRect();
      return { x:r.x+r.width/2, y:r.y+r.height/2 };
    })()`);
    for (const type of ['mousePressed', 'mouseReleased'])
      await cdp.send('Input.dispatchMouseEvent', { type, ...point, button: 'left', clickCount: 1 });
    await until("globalThis.sourcePlayer?.observe().state?.cabinetSession", 'actual C# cabinet');
    await tap('n');
    await sleep(750); // Same entry/return settling cadence as records/cabinet/browser.mjs.
    await until(`(() => {
      const s = sourcePlayer.observe().state;
      return s.records?.screen === 'Options' && s.records.menuSourceTick > 0 && !s.cabinetSession.waitingForNeutral &&
        s.preGameAudio.OriginalEntryActivated && s.preGameAudio.phase === 'Options';
    })()`, 'original options consumer ready', 15000);
    await tap('v');
    await sleep(750);
    const selection = await until("sourcePlayer.observe().state.records?.screen === 'Selection' && sourcePlayer.observe().state.records",
      'actual name/PIN selection', 15000);
    await until(`(() => {
      const s = sourcePlayer.observe().state;
      return s.preGameAudio.phase === 'PlayerEntry' && s.records.menuSourceTick > ${selection.menuSourceTick};
    })()`, 'source player_cursor resumes after the original options return', 15000);
    await capture(`${label}-no`);
    await tap('w'); await capture(`${label}-yes`);
    await tap('s');
    if (answer) await tap('w');
    if (label === 'before') {
      const frame = await value(cdp, '__recordFrames.at(-1)');
      assert(frame.text.some(row => row.text === 'YES') && frame.text.some(row => row.text === 'NO'));
    } else {
      const frame = await value(cdp, '__recordFrames.at(-1)');
      assert(frame.text.every(row => row.text !== 'YES' && row.text !== 'NO'));
      assert(frame.textures.includes(`hud/original/${answer ? 'yes_l' : 'no_l'}`));
      await tap(answer ? 'NumpadEnter' : 'Enter');
      await until(`__recordFrames.some(frame => frame.textures.includes('hud/original/${answer ? 'yglwl' : 'nglwl'}01'))`,
        'authored confirmation', 5000);
      await capture(`${label}-confirm`);
      await until(`sourcePlayer.observe().state.records.stations[0].step === '${answer ? 'Name' : 'Selected'}'`,
        'unchanged name/guest result', 5000);
      await sleep(700);
      const frames = await value(cdp, '__recordFrames');
      const flash = frames.flatMap(frame => frame.textures).filter(texture => /[yn]glw/.test(texture));
      for (const frame of frames)
        for (const texture of frame.textures.filter(texture => /[yn]glw/.test(texture)))
          assert.equal(Number(texture.slice(-2)), Math.floor((frame.tick - frame.menuTick) / 3) + 1,
            'Rendered confirmation frames must follow source ticks, not requestAnimationFrame or wall-clock guesses.');
      for (let i = 1; i <= 11; i++)
        assert(flash.includes(`hud/original/${answer ? 'yglwl' : 'nglwl'}${String(i).padStart(2, '0')}`),
          'Actual WebGL Present must observe every authored confirmation image.');
    }
    report.runs.push({ label, url, frames: await value(cdp, '__recordFrames'), state: await value(cdp, 'sourcePlayer.observe()') });
  }
  assert.equal(report.errors.length, 0);
  report.status = process.argv.includes('--before-only') ? 'baseline-only' : 'passed';
} catch (error) {
  report.status = 'failed'; report.failure = error.stack;
  if (cdp) report.atFailure = await value(cdp, `({
    status:document.querySelector('#status')?.textContent,
    observed:globalThis.sourcePlayer?.observe(), frames:globalThis.__recordFrames?.slice(-10)
  })`).catch(readError => ({ readError: String(readError) }));
  throw error;
} finally {
  await writeFile(`${output}/report.json`, JSON.stringify(report, null, 2));
  cdp?.close(); child.kill('SIGTERM');
  await closed; await runtime.cleanup();
}
