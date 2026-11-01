import assert from 'node:assert/strict';
import test from 'node:test';
import {
  createRecordStorage, readRecords, compareExchangeRecords, closeRecordStorage,
} from './wwwroot/records.js';
import { runNativeRecords } from '../../tests/records/browser/native.mjs';

const empty = JSON.stringify({ primary: null, backup: null, recovery: null });
const code = name => ({ name: 'SourceRecordStorageError', message: `SOURCE_RECORD_STORAGE_${name}` });
const rejects = (promise, name) => assert.rejects(promise, error =>
  error.name === code(name).name && error.message === code(name).message && !error.cause);

async function withFactory(factory, action) {
  const previous = Object.getOwnPropertyDescriptor(globalThis, 'indexedDB');
  Object.defineProperty(globalThis, 'indexedDB', { value: factory, configurable: true });
  try { await action(); } finally {
    if (previous) Object.defineProperty(globalThis, 'indexedDB', previous);
    else delete globalThis.indexedDB;
  }
}

test('argument/wire validation occurs before opening; only explicit null or strings are accepted', async () => {
  let opened = 0;
  await withFactory({ open() { opened++; throw new Error('PRIVATE-FOREIGN-DETAIL'); } }, async () => {
    assert.throws(() => createRecordStorage('metadata-digest'), code('ARGUMENT'));
    for (const handle of [undefined, null, 0, -1, 1.5, '1', Number.MAX_SAFE_INTEGER])
      await rejects(readRecords(handle), 'ARGUMENT');
    const handle = createRecordStorage();
    try {
      for (const json of [undefined, {}, 'null', '[]', '{}', '{',
        '{"Primary":null,"backup":null,"recovery":null}',
        '{"primary":null,"backup":null}',
        '{"primary":null,"backup":null,"recovery":null,"extra":null}',
        '{"primary":null,"primary":null,"backup":null,"recovery":null}',
        '{"primary":1,"backup":null,"recovery":null}',
        '{"primary":null,"backup":false,"recovery":null}',
        '{"primary":null,"backup":null,"recovery":{}}'])
        await rejects(compareExchangeRecords(handle, null, json), 'WIRE');
      for (const expected of [undefined, false, 1, {}])
        await rejects(compareExchangeRecords(handle, expected, empty), 'ARGUMENT');
      assert.equal(opened, 0, 'Invalid input must never create a database.');
      const unusual = JSON.stringify({ recovery: '":\u0000\\\ud800\n🟩', primary: '', backup: null });
      await rejects(compareExchangeRecords(handle, null, unusual), 'OPEN_FAILED');
      assert.equal(opened, 1, 'Valid opaque strings reach the real opening boundary unchanged.');
      await rejects(readRecords(handle), 'OPEN_FAILED');
      assert.equal(opened, 1, 'A failed handle must not silently retry or reset the database.');
    } finally { closeRecordStorage(handle); }
  });
});

test('storage is lazy, uses the schema-stable namespace, and unsupported/closed are never empty', async () => {
  await withFactory(undefined, async () => {
    const handle = createRecordStorage();
    await rejects(readRecords(handle), 'UNSUPPORTED');
    closeRecordStorage(handle);
    closeRecordStorage(handle);
    await rejects(readRecords(handle), 'CLOSED');
    await rejects(compareExchangeRecords(handle, null, empty), 'CLOSED');
  });
  const calls = [];
  await withFactory({ open(...args) { calls.push(args); throw new Error('PRIVATE-OPEN-DETAIL'); } }, async () => {
    const handle = createRecordStorage();
    assert.deepEqual(calls, []);
    await rejects(readRecords(handle), 'OPEN_FAILED');
    assert.deepEqual(calls, [['nflblitz-original-records', 1]]);
    closeRecordStorage(handle);
  });
});

test('native-error mapping exposes only data-free codes (synthetic open failures, not native durability proof)', async () => {
  for (const [name, expected] of [['VersionError', 'VERSION'], ['QuotaExceededError', 'QUOTA'],
    ['SecurityError', 'UNAVAILABLE'], ['NotAllowedError', 'UNAVAILABLE'],
    ['InvalidStateError', 'CLOSED'], ['AbortError', 'ABORTED'], ['UnknownError', 'OPEN_FAILED'],
    ['SourceRecordStorageError', 'OPEN_FAILED']]) {
    await withFactory({ open() { throw new DOMException('PRIVATE-PIN-ENVELOPE', name); } }, async () => {
      const handle = createRecordStorage();
      try { await rejects(readRecords(handle), expected); } finally { closeRecordStorage(handle); }
    });
  }
});

test('blocked/closed pending opens reject and late native events cannot create or retain a database', async () => {
  for (const blocked of [true, false]) {
    let request, aborted = 0, closed = 0, created = 0;
    await withFactory({ open() {
      request = { transaction: { abort() { aborted++; } }, result: {
        createObjectStore() { created++; }, close() { closed++; },
      } };
      return request;
    } }, async () => {
      const handle = createRecordStorage(), pending = readRecords(handle);
      if (blocked) request.onblocked();
      else closeRecordStorage(handle);
      await rejects(pending, blocked ? 'BLOCKED' : 'CLOSED');
      request.onupgradeneeded({ oldVersion: 0, newVersion: 1 });
      request.onsuccess();
      assert.equal(created, 0, 'A rejected open must never perform a late upgrade.');
      assert.equal(aborted, 1);
      assert.equal(closed, 1);
      closeRecordStorage(handle);
    });
  }
});

test('real sandboxed Chrome: native IndexedDB atomicity/reload/recovery and actual C# browser imports',
  { skip: process.env.SOURCE_RECORDS_BROWSER !== '1', timeout: 240000 }, async () => {
    await runNativeRecords();
  });
