const DATABASE = 'nflblitz-original-records', VERSION = 1, STORE = 'cabinet';
const KEYS = ['primary', 'backup', 'recovery'];
const storages = new Map();
let nextHandle = 1;

class RecordStorageError extends Error {
  constructor(code) {
    super(`SOURCE_RECORD_STORAGE_${code}`);
    this.name = 'SourceRecordStorageError';
  }
}

const failure = code => new RecordStorageError(code);

function nativeFailure(error, fallback) {
  if (error instanceof RecordStorageError) return error;
  const code = {
    VersionError: 'VERSION', QuotaExceededError: 'QUOTA',
    SecurityError: 'UNAVAILABLE', NotAllowedError: 'UNAVAILABLE',
    InvalidStateError: 'CLOSED', AbortError: 'ABORTED',
  }[error?.name];
  return failure(code || fallback);
}

function storage(handle) {
  if (!Number.isSafeInteger(handle) || handle < 1 || handle >= nextHandle)
    throw failure('ARGUMENT');
  const state = storages.get(handle);
  if (!state) throw failure('CLOSED');
  if (state.error) throw state.error;
  return state;
}

function imageFromJson(json) {
  if (typeof json !== 'string') throw failure('WIRE');
  let image;
  try { image = JSON.parse(json); } catch { throw failure('WIRE'); }
  if (!image || Array.isArray(image) || Object.keys(image).length !== 3 ||
      KEYS.some(key => !Object.hasOwn(image, key) || image[key] !== null && typeof image[key] !== 'string'))
    throw failure('WIRE');
  // JSON.parse alone discards duplicate properties; count only colons outside string tokens.
  if (json.replace(/"(?:\\[\s\S]|[^"\\])*"/g, '""').split(':').length !== 4)
    throw failure('WIRE');
  return image;
}

function open(state) {
  if (state.closed) return Promise.reject(failure('CLOSED'));
  if (state.error) return Promise.reject(state.error);
  if (state.database) return Promise.resolve(state.database);
  if (state.opening) return state.opening;
  state.opening = new Promise((resolve, reject) => {
    let request, settled = false;
    const rejectOpen = error => {
      if (settled) return;
      settled = true;
      state.error = nativeFailure(error, 'OPEN_FAILED');
      reject(state.error);
    };
    state.cancelOpen = () => rejectOpen(failure('CLOSED'));
    try {
      const factory = globalThis.indexedDB;
      if (!factory || typeof factory.open !== 'function') throw failure('UNSUPPORTED');
      request = factory.open(DATABASE, VERSION);
      request.onerror = () => rejectOpen(request.error);
      request.onblocked = () => rejectOpen(failure('BLOCKED'));
      request.onupgradeneeded = event => {
        try {
          if (settled || state.closed) { request.transaction.abort(); return; }
          if (event.oldVersion !== 0 || event.newVersion !== VERSION) throw failure('VERSION');
          request.result.createObjectStore(STORE);
        } catch (error) {
          rejectOpen(error);
          request.transaction.abort();
        }
      };
      request.onsuccess = () => {
        const database = request.result;
        if (settled || state.closed) { database.close(); return; }
        if (database.version !== VERSION || database.objectStoreNames.length !== 1 ||
            !database.objectStoreNames.contains(STORE)) {
          database.close(); rejectOpen(failure('SCHEMA')); return;
        }
        database.onversionchange = () => {
          state.error = failure('VERSION_CHANGED');
          state.database = null;
          database.close();
        };
        database.onclose = () => {
          if (!state.closed && !state.error) state.error = failure('UNAVAILABLE');
          state.database = null;
        };
        settled = true;
        state.cancelOpen = null;
        state.database = database;
        resolve(database);
      };
    } catch (error) { rejectOpen(error); }
  });
  return state.opening;
}

async function transaction(handle, expectedPrimary, replacement) {
  const state = storage(handle), database = await open(state);
  if (state.closed) throw failure('CLOSED');
  if (state.error) throw state.error;
  return new Promise((resolve, reject) => {
    let tx, error, result;
    const abort = reason => {
      error = nativeFailure(reason, replacement ? 'WRITE_FAILED' : 'READ_FAILED');
      try { tx.abort(); } catch { reject(error); }
    };
    try {
      tx = replacement ? database.transaction(STORE, 'readwrite', { durability: 'strict' }) :
        database.transaction(STORE, 'readonly');
      state.transactions.add(tx);
      const finished = () => state.transactions.delete(tx);
      tx.oncomplete = () => { finished(); error ? reject(error) : resolve(result); };
      tx.onerror = event => {
        error ??= nativeFailure(event.target.error, replacement ? 'WRITE_FAILED' : 'READ_FAILED');
      };
      tx.onabort = () => {
        finished();
        reject(error || failure(state.closed ? 'CLOSED' : 'ABORTED'));
      };
      const store = tx.objectStore(STORE);
      if (store.keyPath !== null || store.autoIncrement) throw failure('SCHEMA');
      const image = { primary: null, backup: null, recovery: null };
      const request = store.openCursor();
      request.onsuccess = () => {
        try {
          const cursor = request.result;
          if (cursor) {
            if (!KEYS.includes(cursor.key)) throw failure('SCHEMA');
            if (typeof cursor.value !== 'string') throw failure('DATA');
            image[cursor.key] = cursor.value;
            cursor.continue();
            return;
          }
          if (!replacement) { result = JSON.stringify(image); return; }
          result = image.primary === expectedPrimary;
          if (!result) return;
          for (const name of KEYS) {
            if (replacement[name] === null) store.delete(name);
            else store.put(replacement[name], name);
          }
        } catch (reason) { abort(reason); }
      };
    } catch (reason) {
      if (tx) abort(reason);
      else reject(nativeFailure(reason, replacement ? 'WRITE_FAILED' : 'READ_FAILED'));
    }
  });
}

export function createRecordStorage() {
  if (arguments.length !== 0 || !Number.isSafeInteger(nextHandle + 1)) throw failure('ARGUMENT');
  const handle = nextHandle++;
  storages.set(handle, { closed: false, error: null, database: null, opening: null,
    cancelOpen: null, transactions: new Set() });
  return handle;
}

export function readRecords(handle) {
  return transaction(handle, null, null);
}

export async function compareExchangeRecords(handle, expectedPrimary, replacementJson) {
  storage(handle);
  if (expectedPrimary !== null && typeof expectedPrimary !== 'string') throw failure('ARGUMENT');
  const replacement = imageFromJson(replacementJson);
  return transaction(handle, expectedPrimary, replacement);
}

export function closeRecordStorage(handle) {
  if (!Number.isSafeInteger(handle) || handle < 1 || handle >= nextHandle) throw failure('ARGUMENT');
  const state = storages.get(handle);
  if (!state) return;
  storages.delete(handle);
  state.closed = true;
  state.cancelOpen?.();
  for (const tx of state.transactions) {
    try { tx.abort(); } catch { /* An already completed transaction needs no cancellation. */ }
  }
  state.database?.close();
  state.database = null;
}
