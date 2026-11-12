import {
  createRecordStorage, readRecords, compareExchangeRecords, closeRecordStorage,
} from '/records.js';

const DATABASE = 'nflblitz-original-records', STORE = 'cabinet';
const keys = ['primary', 'backup', 'recovery'];
const images = {
  seed: { primary: 'synthetic-primary\0"\\\n\ud800🟩', backup: 'opaque previous\n"backup"',
    recovery: 'opaque quarantine\0\udfff["not","an envelope"]' },
  A: { primary: 'synthetic-winner-A', backup: 'synthetic-A-previous', recovery: 'synthetic-A-quarantine' },
  B: { primary: 'synthetic-winner-B', backup: 'synthetic-B-previous', recovery: 'synthetic-B-quarantine' },
};
let handle;
const current = () => handle ??= createRecordStorage();
const read = async () => JSON.parse(await readRecords(current()));
const equal = (left, right) => keys.every(key => left[key] === right[key]);
const require = (condition, name) => { if (!condition) throw new Error(`RECORD_BROWSER_CHECK_${name}`); };
const close = () => { if (handle !== undefined) closeRecordStorage(handle); handle = undefined; };
const errorCode = async action => {
  try { await action(); return 'INCORRECTLY_SUCCEEDED'; } catch (error) {
    require(error.name === 'SourceRecordStorageError' &&
      /^SOURCE_RECORD_STORAGE_[A-Z_]+$/.test(error.message) && !error.cause, 'SANITIZED_ERROR');
    return error.message;
  }
};

async function rawOpen(version = 1, upgrade) {
  return new Promise((resolve, reject) => {
    const request = indexedDB.open(DATABASE, version);
    request.onerror = () => reject(new Error('RECORD_BROWSER_CHECK_RAW_OPEN'));
    request.onupgradeneeded = () => upgrade?.(request.result);
    request.onsuccess = () => resolve(request.result);
  });
}

async function rawWrite(database, key, value) {
  await new Promise((resolve, reject) => {
    const tx = database.transaction(STORE, 'readwrite');
    tx.oncomplete = resolve;
    tx.onabort = tx.onerror = () => reject(new Error('RECORD_BROWSER_CHECK_RAW_WRITE'));
    tx.objectStore(STORE).put(value, key);
  });
}

async function resetOwnedDatabase() {
  close();
  await new Promise((resolve, reject) => {
    const request = indexedDB.deleteDatabase(DATABASE);
    request.onsuccess = resolve;
    request.onerror = request.onblocked = () => reject(new Error('RECORD_BROWSER_CHECK_OWNED_RESET'));
  });
}

async function measureCompare(expected, replacement) {
  const original = IDBDatabase.prototype.transaction, puts = IDBObjectStore.prototype.put,
    deletes = IDBObjectStore.prototype.delete;
  const events = [];
  let writes = 0, strict = false;
  IDBDatabase.prototype.transaction = function (...args) {
    const tx = Reflect.apply(original, this, args);
    if (args[1] === 'readwrite') {
      strict = tx.durability === 'strict';
      tx.addEventListener('complete', () => events.push('complete'));
    }
    return tx;
  };
  IDBObjectStore.prototype.put = function (...args) { writes++; return Reflect.apply(puts, this, args); };
  IDBObjectStore.prototype.delete = function (...args) { writes++; return Reflect.apply(deletes, this, args); };
  try {
    const won = await compareExchangeRecords(current(), expected, JSON.stringify(replacement));
    events.push('resolved');
    return { won, writes, strict, afterComplete: events.join(',') === 'complete,resolved' };
  } finally {
    IDBDatabase.prototype.transaction = original;
    IDBObjectStore.prototype.put = puts;
    IDBObjectStore.prototype.delete = deletes;
  }
}

globalThis.recordsProof = Object.freeze({
  ready: true,
  async initialize() {
    const databases = await indexedDB.databases();
    require(!databases.some(db => db.name === DATABASE), 'OWNED_PROFILE_MUST_START_ABSENT');
    require(equal(await read(), { primary: null, backup: null, recovery: null }), 'ABSENT_TRIPLE');
    const result = await measureCompare(null, images.seed);
    require(equal(await read(), images.seed), 'COMMITTED_TRIPLE');
    return { absent: true, exactOpaqueStrings: true, ...result };
  },
  async seedAfterReload() {
    require(equal(await read(), images.seed), 'RELOADED_EXACT_TRIPLE');
    return { exactOpaqueStrings: true, heapIndependent: true };
  },
  async race(which) {
    require(which === 'A' || which === 'B', 'RACE_NAME');
    return measureCompare(images.seed.primary, images[which]);
  },
  async winner() {
    const image = await read(), which = ['A', 'B'].find(name => equal(image, images[name]));
    require(!!which, 'UNTORN_WINNER_TRIPLE');
    return which;
  },
  async stale() {
    const before = await read(), result = await measureCompare(images.seed.primary, images.seed);
    require(!result.won && result.writes === 0 && equal(await read(), before), 'STALE_NO_MUTATION');
    return { ...result, unchanged: true };
  },
  async abort() {
    const before = await read(), original = IDBObjectStore.prototype.put,
      transaction = IDBDatabase.prototype.transaction;
    let successes = 0, aborted = false, completed = false;
    IDBDatabase.prototype.transaction = function (...args) {
      const tx = Reflect.apply(transaction, this, args);
      if (args[1] === 'readwrite') {
        tx.addEventListener('abort', () => { aborted = true; });
        tx.addEventListener('complete', () => { completed = true; });
      }
      return tx;
    };
    IDBObjectStore.prototype.put = function (...args) {
      const request = Reflect.apply(original, this, args), tx = this.transaction;
      request.addEventListener('success', () => {
        successes++;
        if (args[1] === 'backup') tx.abort();
      });
      return request;
    };
    let error;
    try { error = await errorCode(() => compareExchangeRecords(current(), before.primary, JSON.stringify(images.seed))); }
    finally {
      IDBObjectStore.prototype.put = original;
      IDBDatabase.prototype.transaction = transaction;
    }
    return { error, nativeTransactionAborted: aborted, successfulRequestsBeforeAbort: successes,
      completed, allThreeUnchanged: equal(await read(), before) };
  },
  async corruption() {
    const before = await read(), database = await rawOpen(), cases = [];
    try {
      for (const [key, value, kind] of [['primary', undefined, 'present-undefined'],
        ['backup', null, 'present-null'], ['recovery', { bad: true }, 'present-object']]) {
        await rawWrite(database, key, value);
        const readError = await errorCode(() => readRecords(current()));
        const writeError = await errorCode(() => compareExchangeRecords(current(), before.primary, JSON.stringify(images.seed)));
        require(readError === 'SOURCE_RECORD_STORAGE_DATA' && writeError === readError, 'BAD_VALUE_NOT_EMPTY');
        await new Promise((resolve, reject) => {
          const tx = database.transaction(STORE, 'readonly'), request = tx.objectStore(STORE).openCursor(key);
          request.onsuccess = () => {
            const actual = request.result?.value;
            require(!!request.result && (kind === 'present-object' ? actual?.bad === true : actual === value),
              'BAD_VALUE_NOT_CLEARED');
          };
          tx.oncomplete = resolve;
          tx.onabort = tx.onerror = () => reject(new Error('RECORD_BROWSER_CHECK_CORRUPT_RETAINED'));
        });
        await rawWrite(database, key, before[key]);
        cases.push({ key, kind, readError, writeError, preserved: true });
      }
    } finally { database.close(); }
    require(equal(await read(), before), 'CORRUPTION_FIXTURE_RESTORED');
    return cases;
  },
  async unexpectedKeys() {
    const before = await read(), database = await rawOpen(), cases = [];
    try {
      for (const key of ['Primary', 'unexpected', 7]) {
        const opaque = 'owned-synthetic-unrecognized-key-value';
        await rawWrite(database, key, opaque);
        const readError = await errorCode(() => readRecords(current()));
        const writeError = await errorCode(() => compareExchangeRecords(current(), before.primary, JSON.stringify(images.seed)));
        require(readError === 'SOURCE_RECORD_STORAGE_SCHEMA' && writeError === readError, 'UNKNOWN_KEY_NOT_EMPTY');
        await new Promise((resolve, reject) => {
          const tx = database.transaction(STORE, 'readwrite'), store = tx.objectStore(STORE);
          const request = store.get(key);
          request.onsuccess = () => {
            require(request.result === opaque, 'UNKNOWN_KEY_NOT_CHANGED');
            store.delete(key);
          };
          tx.oncomplete = resolve;
          tx.onabort = tx.onerror = () => reject(new Error('RECORD_BROWSER_CHECK_UNKNOWN_KEY_RETAINED'));
        });
        require(equal(await read(), before), 'UNKNOWN_KEY_CANONICAL_VALUES_UNCHANGED');
        cases.push({ kind: typeof key === 'number' ? 'numeric' : key === 'Primary' ? 'wrong-case' : 'extra',
          readError, writeError, preserved: true });
      }
    } finally { database.close(); }
    return cases;
  },
  async close() {
    const old = current();
    close();
    const readError = await errorCode(() => readRecords(old));
    const writeError = await errorCode(() => compareExchangeRecords(old, null, JSON.stringify(images.seed)));
    require(readError === 'SOURCE_RECORD_STORAGE_CLOSED' && writeError === readError, 'CLOSED_NOT_EMPTY');
    return { readError, writeError };
  },
  async blocked() {
    close();
    const blocker = await rawOpen(), original = IDBFactory.prototype.open;
    let nativeRequest, requestedVersion, nativeBlocked = false;
    // Schema v1 has no earlier persisted version. Return a genuinely blocked native upgrade
    // request at the opening boundary to exercise its late-event/failure handling.
    IDBFactory.prototype.open = function (name, version) {
      requestedVersion = version;
      nativeRequest = Reflect.apply(original, this, [name, 2]);
      nativeRequest.addEventListener('blocked', () => { nativeBlocked = true; });
      return nativeRequest;
    };
    let result, settled;
    try {
      const pending = readRecords(current());
      IDBFactory.prototype.open = original;
      result = await errorCode(() => pending);
      settled = new Promise(resolve => {
        nativeRequest.addEventListener('error', resolve, { once: true });
        nativeRequest.addEventListener('success', () => { nativeRequest.result.close(); resolve(); }, { once: true });
      });
    } finally {
      IDBFactory.prototype.open = original;
      blocker.close();
    }
    await settled;
    require(result === 'SOURCE_RECORD_STORAGE_BLOCKED' && nativeBlocked && requestedVersion === 1,
      'NATIVE_BLOCKED_NOT_EMPTY');
    close();
    const unchanged = await rawOpen();
    require(unchanged.version === 1, 'BLOCKED_LATE_UPGRADE_ABORTED');
    unchanged.close();
    return { error: result, nativeBlocked, requestedVersion, databaseVersionUnchanged: true,
      method: 'Native version2 request injected at open; version1 has no existing lower version.' };
  },
  async versionChange() {
    const before = await read(), old = current();
    const upgraded = await rawOpen(2);
    upgraded.close();
    const changed = await errorCode(() => readRecords(old));
    close();
    const newer = await errorCode(() => readRecords(current()));
    close();
    const database = await rawOpen(2);
    let image;
    await new Promise((resolve, reject) => {
      const tx = database.transaction(STORE, 'readonly'), values = {};
      for (const key of keys) {
        const request = tx.objectStore(STORE).get(key);
        request.onsuccess = () => { values[key] = request.result ?? null; };
      }
      tx.oncomplete = () => { image = values; resolve(); };
      tx.onabort = tx.onerror = () => reject(new Error('RECORD_BROWSER_CHECK_NEWER_RETAINED'));
    });
    database.close();
    require(changed === 'SOURCE_RECORD_STORAGE_VERSION_CHANGED' && newer === 'SOURCE_RECORD_STORAGE_VERSION' &&
      equal(image, before), 'NEWER_VERSION_NOT_RESET');
    return { changed, newer, version: 2, allThreeUnchanged: true };
  },
  async wrongSchema() {
    await resetOwnedDatabase();
    const database = await rawOpen(1, db => db.createObjectStore('unrecognized'));
    database.close();
    const error = await errorCode(() => readRecords(current()));
    close();
    const retained = await rawOpen();
    require(error === 'SOURCE_RECORD_STORAGE_SCHEMA' && retained.objectStoreNames.length === 1 &&
      retained.objectStoreNames.contains('unrecognized'), 'SCHEMA_NOT_RESET');
    retained.close();
    return { error, unknownSchemaRetained: true };
  },
  resetOwnedDatabase,
});
