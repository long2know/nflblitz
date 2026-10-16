import { chmod, lstat, mkdir, mkdtemp, realpath, rm } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const defaultRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), 'artifacts');

function sameDirectory(actual, expected) {
  return actual.isDirectory() && !actual.isSymbolicLink() &&
    actual.dev === expected.dev && actual.ino === expected.ino && actual.uid === expected.uid;
}

export async function createChromeRuntime({
  root = process.env.CHROME_RUNTIME_ROOT || defaultRoot,
  setMode = chmod,
} = {}) {
  root = path.resolve(root);
  const parent = await lstat(root);
  if (!parent.isDirectory() || parent.isSymbolicLink() || await realpath(root) !== root) {
    throw new Error(`Chrome runtime root must be a real directory, not a symlink: ${root}`);
  }
  const directory = await mkdtemp(path.join(root, 'c-'));
  const identity = await lstat(directory);
  let cleaned = false;
  const cleanup = async () => {
    if (cleaned) return;
    let current;
    try {
      current = await lstat(directory);
    } catch (error) {
      if (error.code !== 'ENOENT') throw error;
      cleaned = true;
      return;
    }
    if (!sameDirectory(current, identity) || !sameDirectory(await lstat(root), parent) ||
        await realpath(directory) !== directory) {
      throw new Error(`Refusing to clean replaced/symlinked Chrome runtime: ${directory}`);
    }
    await rm(directory, { recursive: true, force: false });
    cleaned = true;
  };
  try {
    await setMode(directory, 0o700);
    const secure = await lstat(directory);
    if (!sameDirectory(secure, identity) || secure.uid !== process.getuid?.() ||
        (secure.mode & 0o777) !== 0o700) {
      throw new Error(`Chrome runtime requires owned private 0700 POSIX storage: ${directory}. ` +
        'Use tests/browser/with-posix-runtime.sh on filesystems that discard permissions.');
    }
    const directories = {};
    for (const name of ['profile', 'cache', 'config', 'tmp']) {
      directories[name] = path.join(directory, name);
      await mkdir(directories[name], { mode: 0o700 });
    }
    if (Buffer.byteLength(directories.tmp) > 62) {
      throw new Error(`Chrome runtime socket path is too long: ${directories.tmp}. Select a shorter CHROME_RUNTIME_ROOT.`);
    }
    return { directory, ...directories, cleanup };
  } catch (error) {
    await cleanup();
    throw error;
  }
}
