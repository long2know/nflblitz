import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import test from 'node:test';
import { createVerifiedAssetLoader, createVerifiedRuntimeResourceLoader } from './wwwroot/verified-assets.js';

const bytes = value => new TextEncoder().encode(value);
const digest = async value => createHash('sha256').update(value).digest('hex');
const pin = async (path, value) => ({ path, bytes: bytes(value).length, sha256: await digest(bytes(value)) });
const baseUrl = 'https://example.test/nflblitz/main.js?fresh=1#ignored';

function storage() {
  const entries = new Map(), deleted = [];
  return {
    entries, deleted,
    open: async () => ({
      match: async key => entries.get(key)?.clone(),
      put: async (key, response) => { entries.set(key, response.clone()); },
      delete: async key => { deleted.push(key); return entries.delete(key); },
    }),
  };
}

test('a separate loader after reload rechecks persistent bytes and performs no network request', async () => {
  const cache = storage(), file = await pin('data/model.bin', 'actual model bytes'), calls = [];
  const first = createVerifiedAssetLoader({ baseUrl, digest, getStorage: () => cache,
    fetchBytes: async path => { calls.push(path); return bytes('actual model bytes'); } });
  assert.deepEqual(await first.load(file), bytes('actual model bytes'));
  const second = createVerifiedAssetLoader({ baseUrl: 'https://example.test/nflblitz/main.js?new=2',
    digest, getStorage: () => cache, fetchBytes: () => { throw new Error('warm network must not run'); } });
  assert.deepEqual(await second.load(file), bytes('actual model bytes'));
  assert.equal(calls.length, 1);
  assert.equal(second.snapshot().cached, 1);
  assert.equal(second.snapshot().downloadedBytes, 0);
  assert.equal(second.baseUrl, 'https://example.test/nflblitz/');
});

test('changed bytes at the same filename require the new SHA and never return the old profile settings', async () => {
  const cache = storage(), old = await pin('data/settings.json', 'four station'), next = await pin('data/settings.json', 'two station');
  let downloaded = 'four station', count = 0;
  const load = createVerifiedAssetLoader({ baseUrl, digest, getStorage: () => cache,
    fetchBytes: async () => { count++; return bytes(downloaded); } });
  assert.deepEqual(await load.load(old), bytes('four station'));
  downloaded = 'two station';
  assert.deepEqual(await load.load(next), bytes('two station'));
  assert.equal(count, 2);
  assert.equal(cache.entries.size, 2);
});

test('same SHA is isolated between normalized application prefixes and origins', async () => {
  const cache = storage(), file = await pin('data/input.bin', 'same bytes');
  const calls = [];
  for (const root of ['https://example.test/nflblitz/main.js', 'https://example.test/games/nflblitz/main.js',
    'https://other.test/nflblitz/main.js']) {
    const load = createVerifiedAssetLoader({ baseUrl: root, digest, getStorage: () => cache,
      fetchBytes: async path => { calls.push([root, path]); return bytes('same bytes'); } });
    await load.load(file);
  }
  assert.equal(calls.length, 3);
  assert.equal(cache.entries.size, 3);
});

test('a physically poisoned CacheStorage response is evicted and replaced only with verified network bytes', async () => {
  const cache = storage(), file = await pin('data/input.bin', 'correct'), warnings = [];
  const key = new URL('.verified-assets/' + file.sha256, 'https://example.test/nflblitz/').href;
  cache.entries.set(key, new Response(bytes('poison!')));
  const load = createVerifiedAssetLoader({ baseUrl, digest, getStorage: () => cache, warn: message => warnings.push(message),
    fetchBytes: async () => bytes('correct') });
  assert.deepEqual(await load.load(file), bytes('correct'));
  assert.deepEqual(cache.deleted, [key]);
  assert.equal(load.snapshot().corruptEntries, 1);
  assert.match(warnings[0], /failed integrity.*discarded/);
  assert.deepEqual(new Uint8Array(await cache.entries.get(key).clone().arrayBuffer()), bytes('correct'));
});

test('network hash or byte-length mismatch never reaches callers or persistent cache', async () => {
  for (const value of ['changed', 'far longer wrong content']) {
    const cache = storage(), file = await pin('data/input.bin', 'correct');
    const load = createVerifiedAssetLoader({ baseUrl, digest, getStorage: () => cache,
      fetchBytes: async () => bytes(value) });
    await assert.rejects(load.load(file), /content pin mismatch: data\/input.bin/);
    assert.equal(cache.entries.size, 0);
  }
});

test('unsupported, denied and quota cache failures issue one explicit warning and keep verified network loading', async () => {
  for (const type of ['unsupported', 'getter-denied', 'open-denied', 'put-quota', 'match-denied']) {
    const warnings = [], cache = storage(), calls = [];
    const error = new DOMException('Owned cache fixture denied', type === 'put-quota' ? 'QuotaExceededError' : 'SecurityError');
    const getStorage = () => {
      if (type === 'unsupported') return undefined;
      if (type === 'getter-denied') throw error;
      if (type === 'open-denied') return { open: async () => { throw error; } };
      return { open: async () => {
        const store = await cache.open();
        if (type === 'put-quota') store.put = async () => { throw error; };
        if (type === 'match-denied') store.match = async () => { throw error; };
        return store;
      } };
    };
    const load = createVerifiedAssetLoader({ baseUrl, digest, getStorage, warn: message => warnings.push(message),
      fetchBytes: async path => { calls.push(path); return bytes('correct'); } });
    for (const path of ['data/a.bin', 'data/b.bin']) assert.deepEqual(await load.load(await pin(path, 'correct')), bytes('correct'));
    assert.equal(calls.length, 2, type);
    assert.equal(warnings.length, 1, type);
    assert.match(warnings[0], /cache unavailable.*integrity-checked network downloads/);
    assert.equal(load.snapshot().persistentCacheAvailable, false);
  }
});

test('a poisoned hit followed by corrupt network still fails; cache fallback is never an integrity fallback', async () => {
  const cache = storage(), file = await pin('data/input.bin', 'correct');
  const key = new URL('.verified-assets/' + file.sha256, 'https://example.test/nflblitz/').href;
  cache.entries.set(key, new Response(bytes('corrupt')));
  const load = createVerifiedAssetLoader({ baseUrl, digest, getStorage: () => cache, warn: () => {},
    fetchBytes: async () => bytes('also bad') });
  await assert.rejects(load.load(file), /content pin mismatch/);
  assert.equal(cache.entries.size, 0);
});

test('unsafe and unpinned entries fail before either cache or network access', async () => {
  const load = createVerifiedAssetLoader({ baseUrl, digest,
    getStorage: () => { throw new Error('must not inspect storage'); },
    fetchBytes: () => { throw new Error('must not fetch'); } });
  for (const entry of [{ path: '../escape', bytes: 1, sha256: 'a'.repeat(64) },
    { path: '/absolute', bytes: 1, sha256: 'a'.repeat(64) }, { path: 'data/a', bytes: 1, sha256: 'unpinned' },
    { path: 'data/a', bytes: 1.5, sha256: 'a'.repeat(64) }])
    await assert.rejects(load.load(entry), /Unsafe or unpinned/);
});

test('digest failure is fatal, not an excuse to return cached or downloaded unverified bytes', async () => {
  const cache = storage(), file = await pin('data/input.bin', 'correct');
  const load = createVerifiedAssetLoader({ baseUrl, digest: async () => { throw new Error('SHA unavailable'); },
    getStorage: () => cache, fetchBytes: async () => bytes('correct') });
  await assert.rejects(load.load(file), /SHA unavailable/);
  assert.equal(cache.entries.size, 0);
});

test('actual runtime resource callback binds boot integrity to known bytes and keeps fixed-name JS on the default fresh path', async () => {
  const file = await pin('_framework/dotnet.native.wasm', 'native fixture'), cache = storage();
  const assets = createVerifiedAssetLoader({ baseUrl, digest, getStorage: () => cache,
    fetchBytes: async () => bytes('native fixture') });
  const load = createVerifiedRuntimeResourceLoader({ baseUrl,
    resources: { schema: 'source-verified-runtime-resources/v1', files: [file] }, load: assets.load });
  const integrity = 'sha256-' + Buffer.from(file.sha256, 'hex').toString('base64');
  const response = await load('dotnetwasm', 'dotnet.native.wasm', './_framework/dotnet.native.wasm', integrity);
  assert.equal(response.headers.get('Content-Type'), 'application/wasm');
  assert.deepEqual(new Uint8Array(await response.arrayBuffer()), bytes('native fixture'));
  assert.equal(load('dotnetjs', 'dotnet.js', './_framework/dotnet.js', ''), null);
  assert.throws(() => load('dotnetwasm', 'native', './_framework/dotnet.native.wasm', 'sha256-wrong'),
    /boot integrity differs/);
  assert.throws(() => load('assembly', 'missing', './_framework/missing.wasm', ''), /no content pin/);
  assert.throws(() => load('assembly', 'foreign', 'https://foreign.test/_framework/missing.wasm', ''), /escaped/);
});
