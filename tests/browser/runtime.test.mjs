import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { access, chmod, lstat, readFile, readdir, rename, symlink, unlink, writeFile } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import test from 'node:test';
import { createChromeRuntime } from './runtime.mjs';
import { launchChrome, targetSession } from './chrome.mjs';

test('Chrome uses a unique owned 0700 runtime and cleans only that runtime', async () => {
  const first = await createChromeRuntime();
  const second = await createChromeRuntime();
  try {
    assert.notEqual(first.directory, second.directory);
    const info = await lstat(first.directory);
    assert.equal(info.mode & 0o777, 0o700);
    assert.equal(info.uid, process.getuid());
    for (const name of ['profile', 'cache', 'config', 'tmp']) {
      assert.equal(path.dirname(first[name]), first.directory);
      assert.equal((await lstat(first[name])).mode & 0o777, 0o700);
    }
    await writeFile(path.join(second.profile, 'sentinel'), 'untouched');
    await first.cleanup();
    await assert.rejects(access(first.directory), { code: 'ENOENT' });
    assert.equal(await readFile(path.join(second.profile, 'sentinel'), 'utf8'), 'untouched');
    await first.cleanup();
  } finally {
    await first.cleanup();
    await second.cleanup();
  }
});

test('cleanup unlinks child symlinks without traversing their target', async () => {
  const first = await createChromeRuntime();
  const second = await createChromeRuntime();
  try {
    const sentinel = path.join(second.profile, 'sentinel');
    await writeFile(sentinel, 'untouched');
    await symlink(second.profile, path.join(first.profile, 'external'));
    await first.cleanup();
    assert.equal(await readFile(sentinel, 'utf8'), 'untouched');
  } finally {
    await first.cleanup();
    await second.cleanup();
  }
});

test('cleanup refuses a runtime root replaced by a symlink', async () => {
  const first = await createChromeRuntime();
  const second = await createChromeRuntime();
  const moved = `${first.directory}-moved`;
  try {
    await writeFile(path.join(second.profile, 'sentinel'), 'untouched');
    await rename(first.directory, moved);
    await symlink(second.directory, first.directory);
    await assert.rejects(first.cleanup(), /Refusing to clean replaced\/symlinked/);
    assert.equal(await readFile(path.join(second.profile, 'sentinel'), 'utf8'), 'untouched');
  } finally {
    await unlink(first.directory);
    await rename(moved, first.directory);
    await first.cleanup();
    await second.cleanup();
  }
});

test('a symlink-selected runtime base is rejected before creating a profile', async () => {
  const first = await createChromeRuntime();
  const second = await createChromeRuntime();
  try {
    const link = path.join(first.profile, 'runtime-link');
    await symlink(second.directory, link);
    await assert.rejects(createChromeRuntime({ root: link }), /not a symlink/);
    await assert.rejects(createChromeRuntime({ root: path.join(link, 'cache') }), /not a symlink/);
    assert.deepEqual(await readdir(second.cache), []);
  } finally {
    await first.cleanup();
    await second.cleanup();
  }
});

test('a filesystem that discards private permissions fails loudly and cleans its own directory', async () => {
  let created;
  await assert.rejects(createChromeRuntime({
    setMode: async directory => { created = directory; await chmod(directory, 0o777); },
  }), /requires owned private 0700 POSIX storage/);
  assert.ok(created);
  await assert.rejects(access(created), { code: 'ENOENT' });
});

test('an unavailable browser fails without hanging or leaving its private runtime', async () => {
  const runtime = await createChromeRuntime();
  const root = path.dirname(runtime.directory);
  await runtime.cleanup();
  const before = await readdir(root);
  const previous = process.env.CHROME_BIN;
  process.env.CHROME_BIN = path.join(root, 'nonexistent-chrome');
  const start = Date.now();
  try {
    await assert.rejects(launchChrome('unused-evidence-directory'), /ENOENT/);
    assert.ok(Date.now() - start < 10000, 'Failed browser startup must not hang on a never-emitted exit event.');
    assert.deepEqual(await readdir(root), before);
  } finally {
    if (previous === undefined) delete process.env.CHROME_BIN;
    else process.env.CHROME_BIN = previous;
  }
});

test('owned target session attachment is explicit and cannot silently fall back to the hidden game', async () => {
  const calls = [];
  const cdp = { send: async (...args) => {
    calls.push(args);
    return args[0] === 'Target.attachToTarget' ? { sessionId: 'owned-session' } : {};
  } };
  const session = await targetSession(cdp, 'owned-other-tab');
  await session.send('Input.dispatchKeyEvent', { type: 'keyUp', key: 'Shift' });
  assert.deepEqual(calls, [
    ['Target.attachToTarget', { targetId: 'owned-other-tab', flatten: true }],
    ['Input.dispatchKeyEvent', { type: 'keyUp', key: 'Shift' }, 'owned-session'],
  ]);
  await assert.rejects(targetSession({ send: async () => ({}) }, 'owned-other-tab'), /did not attach/);
});

test('focused browser subset cannot be mislabeled as a full-match run', () => {
  const result = spawnSync(process.execPath, [fileURLToPath(new URL('./smoke.mjs', import.meta.url)),
    '--focus-layout-only', '--full-match'], { encoding: 'utf8', timeout: 5000 });
  assert.equal(result.status, 1);
  assert.match(result.stderr, /Focus\/layout subset cannot claim a full match/);
});

test('actor visual subset requires explicit mode and cannot silently waive full/focus proof', () => {
  const script = fileURLToPath(new URL('./smoke.mjs', import.meta.url));
  for (const [args, expected] of [
    [['--visual-controls-only'], /requires explicit actors/],
    [['--visual-controls-only', '--full-match', '--url', 'http://localhost/?actors=source'], /cannot claim/],
    [['--visual-controls-only', '--focus-layout-only', '--url', 'http://localhost/?actors=source'], /cannot claim/],
    [['--visual-controls-only', '--expected-source-pose', 'standing', '--url', 'http://localhost/?actors=source'], /Expected source pose/],
    [['--final-presentation'], /Final presentation requires/],
  ]) {
    const result = spawnSync(process.execPath, [script, ...args], { encoding: 'utf8', timeout: 5000 });
    assert.equal(result.status, 1);
    assert.match(result.stderr, expected);
  }
});
