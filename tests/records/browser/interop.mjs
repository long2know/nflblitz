import { dotnet } from './_framework/dotnet.js';
import * as records from './records.js';

const runtime = await dotnet.withDiagnosticTracing(false).create();
runtime.setModuleImports('source-player-records', records);
const exports = await runtime.getAssemblyExports(runtime.getConfig().mainAssemblyName);
const managed = exports.Blitz.SourcePlayer.Tests.RecordBrowserChecks;
await runtime.runMain();

const marker = 'synthetic corrupt primary\0"\\\n🟩';
const prior = 'synthetic older quarantine\0🙂';
let handle;
const storage = () => handle ??= records.createRecordStorage();
const image = async () => JSON.parse(await records.readRecords(storage()));
const require = (condition, name) => { if (!condition) throw new Error('RECORD_INTEROP_CHECK_' + name); };
const describe = json => JSON.parse(json);

globalThis.recordsInterop = Object.freeze({
  ready: true,
  opaque: async () => describe(await managed.OpaqueRoundTrip()),
  initialize: async () => describe(await managed.InitializeSynthetic()),
  load: async () => describe(await managed.LoadSynthetic()),
  race: async () => describe(await managed.RaceSynthetic()),
  recover: async () => describe(await managed.RecoverSynthetic()),
  closeAndLoad: async () => describe(await managed.CloseAndLoad()),
  retry: async () => describe(await managed.RetrySave()),
  async abortSave() {
    const before = await image(), put = IDBObjectStore.prototype.put,
      transaction = IDBDatabase.prototype.transaction;
    let successes = 0, aborted = false;
    IDBDatabase.prototype.transaction = function (...args) {
      const tx = Reflect.apply(transaction, this, args);
      if (args[1] === 'readwrite') tx.addEventListener('abort', () => { aborted = true; });
      return tx;
    };
    IDBObjectStore.prototype.put = function (...args) {
      const request = Reflect.apply(put, this, args), tx = this.transaction;
      request.addEventListener('success', () => {
        successes++;
        if (args[1] === 'backup') tx.abort();
      });
      return request;
    };
    let result;
    try { result = describe(await managed.AbortedSave()); } finally {
      IDBDatabase.prototype.transaction = transaction;
      IDBObjectStore.prototype.put = put;
    }
    const after = await image();
    require(aborted && successes >= 2 &&
      ['primary', 'backup', 'recovery'].every(key => before[key] === after[key]), 'MANAGED_ABORT_TRIPLE_UNCHANGED');
    return { ...result, previousCurrentReferenceAndGenerationRetained: true,
      nativeTransactionAborted: aborted, allThreeUnchanged: true };
  },
  async malformed() {
    const malformed = 'unpaired\ud800', replacement = { primary: malformed, backup: null, recovery: null };
    require(await records.compareExchangeRecords(storage(), null, JSON.stringify(replacement)), 'MALFORMED_SEED');
    const result = describe(await managed.MalformedRead()), retained = await image();
    require(retained.primary === malformed && retained.backup === null && retained.recovery === null,
      'MALFORMED_RETAINED_EXACT');
    require(await records.compareExchangeRecords(storage(), malformed, JSON.stringify({
      primary: null, backup: null, recovery: null,
    })), 'MALFORMED_EXPLICIT_FIXTURE_CLEANUP');
    return { ...result, exactCodeUnitsRetained: true };
  },
  async corrupt() {
    const before = await image();
    require(typeof before.primary === 'string' && typeof before.backup === 'string', 'ACTUAL_BACKEND_TRIPLE');
    require(await records.compareExchangeRecords(storage(), before.primary, JSON.stringify({
      primary: marker, backup: before.backup, recovery: prior,
    })), 'CORRUPTION_FIXTURE_CAS');
    return { actualPreviousBackupRetained: true, deliberateSyntheticCorruptionOnly: true };
  },
  async recoveryExact() {
    const current = await image(), recovery = JSON.parse(current.recovery);
    require(Array.isArray(recovery) && recovery.length === 2 && recovery[0] === prior && recovery[1] === marker,
      'LOSSLESS_QUARANTINE');
    require(typeof current.primary === 'string' && typeof current.backup === 'string', 'RECOVERED_TRIPLE');
    return { bothOpaqueQuarantinesExact: true, committedPrimaryAndBackup: true };
  },
  close() {
    managed.Close();
    if (handle !== undefined) records.closeRecordStorage(handle);
    handle = undefined;
  },
});
window.addEventListener('pagehide', () => globalThis.recordsInterop.close(), { once: true });
