using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text.Json;
using Blitz.Contracts;
using Blitz.SourcePlayer;
using Blitz.Storage;

namespace Blitz.SourcePlayer.Tests;

[SupportedOSPlatform("browser")]
public static partial class RecordBrowserChecks
{
    private static readonly SourceRecordIdentity Identity = new("SYNAAA", "1234");
    private static BrowserRecordStorage? storage;
    private static SourcePlayerRecordStore? store;

    public static void Main() { }

    private static SourcePlayerRecordStore Store => store ??=
        new SourcePlayerRecordStore(storage ??= new BrowserRecordStorage());

    private static SourceCustomPlay Play() => SourceCustomPlayCodec.ReplaceRoute(
        SourceCustomPlayCodec.CreateFromFormation(0, "SYN", "ROUTE"), 0,
        new[] { new SourceCustomPoint(9, 8, 0), new SourceCustomPoint(9, 18, SourceCustomPointFlags.Turbo),
            new SourceCustomPoint(20, 29, SourceCustomPointFlags.Spin),
            new SourceCustomPoint(30, 40, SourceCustomPointFlags.Last) });

    private static void Require(bool condition, string code)
    {
        if (!condition) throw new InvalidOperationException("RECORD_MANAGED_CHECK_" + code);
    }

    private static void Saved(SourceRecordStoreResult result) =>
        Require(result.Status == SourceRecordStoreStatus.Saved, "SAVE_RESULT");

    private static string Describe(SourceRecordStoreResult result)
    {
        SourcePlayerRecord? record = Store.Current?.Find(Identity);
        bool customExact = record?.CustomPlays[2] != null &&
            SourceCustomPlayCodec.EncodeCompact(record.CustomPlays[2]).SequenceEqual(SourceCustomPlayCodec.EncodeCompact(Play()));
        bool audiblesExact = record != null && record.Audibles.Offense.SequenceEqual(new short[] { -39, 39, -39 }) &&
            record.Audibles.Defense.SequenceEqual(new short[] { 1, 2, 3 });
        return JsonSerializer.Serialize(new {
            status = result.Status.ToString(), result.Code, result.Generation, result.RecordCount,
            Store.IsWritable, customExact, audiblesExact,
            slot0Saved = record?.CustomPlays[0] != null, slot1Saved = record?.CustomPlays[1] != null
        });
    }

    [JSExport]
    [return: JSMarshalAs<JSType.Promise<JSType.String>>]
    public static async Task<string> OpaqueRoundTrip()
    {
        using var adapter = new BrowserRecordStorage();
        SourceRecordStorageImage absent = await adapter.ReadAsync();
        Require(absent.Primary == null && absent.Backup == null && absent.Recovery == null, "OPAQUE_ABSENT");
        var image = new SourceRecordStorageImage("opaque primary\0\u0001\"\\\r\n\U0001F7E9",
            "opaque backup\b\f\t\U0001F642", "opaque recovery[\"not\",\"an envelope\"]\0");
        Require(await adapter.CompareExchangeAsync(null, image), "OPAQUE_WRITE");
        SourceRecordStorageImage loaded = await adapter.ReadAsync();
        Require(loaded.Primary == image.Primary && loaded.Backup == image.Backup && loaded.Recovery == image.Recovery,
            "OPAQUE_MANAGED_WIRE_EXACT");
        Require(await adapter.CompareExchangeAsync(image.Primary, absent), "OPAQUE_DELETE");
        foreach (SourceRecordStorageImage malformed in new[] {
            new SourceRecordStorageImage("\ud800"),
            new SourceRecordStorageImage(null, "\udfff"),
            new SourceRecordStorageImage(null, null, "\ud800") })
        {
            try {
                await adapter.CompareExchangeAsync(null, malformed);
                throw new InvalidOperationException("RECORD_MANAGED_CHECK_LOSSY_UTF16_WRITE");
            }
            catch (SourceRecordStorageException) { }
        }
        loaded = await adapter.ReadAsync();
        Require(loaded.Primary == null && loaded.Backup == null && loaded.Recovery == null, "MALFORMED_WRITES_UNCHANGED");
        return JsonSerializer.Serialize(new { managedEncodeAndDecodeExact = true, supportedUnicodeExact = true,
            nullDeletes = true, malformedUtf16WritesRejectedWithoutMutation = 3 });
    }

    [JSExport]
    [return: JSMarshalAs<JSType.Promise<JSType.String>>]
    public static async Task<string> MalformedRead()
    {
        using var adapter = new BrowserRecordStorage();
        var records = new SourcePlayerRecordStore(adapter);
        SourceRecordStoreResult result = await records.LoadAsync();
        Require(result.Status == SourceRecordStoreStatus.IoFailure, "MALFORMED_READ_NOT_EMPTY_OR_REWRITTEN");
        return JsonSerializer.Serialize(new { status = result.Status.ToString(), result.Code,
            notFound = false, writable = records.IsWritable });
    }

    [JSExport]
    [return: JSMarshalAs<JSType.Promise<JSType.String>>]
    public static async Task<string> InitializeSynthetic()
    {
        Require((await Store.LoadAsync()).Status == SourceRecordStoreStatus.NotFound, "EXPLICIT_NOT_FOUND");
        Saved(await Store.InitializeNewAsync());
        Saved(await Store.CreateAsync(Identity));
        Saved(await Store.SaveCustomPlayAsync(Identity, 2, Play()));
        Saved(await Store.SaveAudiblesAsync(Identity,
            new SourceRecordAudibles(new short[] { -39, 39, -39 }, new short[] { 1, 2, 3 })));
        SourceRecordStoreResult result = await Store.SaveCustomPlayAsync(Identity, 0,
            SourceCustomPlayCodec.CreateFromFormation(1, "SYN", "EXTRA"));
        Saved(result);
        Require(Store.Current.Records.Count == 1, "ACTUAL_RECORD");
        return Describe(result);
    }

    [JSExport]
    [return: JSMarshalAs<JSType.Promise<JSType.String>>]
    public static async Task<string> LoadSynthetic()
    {
        SourceRecordStoreResult result = await Store.LoadAsync();
        Require(result.Status == SourceRecordStoreStatus.Ready, "LOAD_RESULT");
        return Describe(result);
    }

    [JSExport]
    [return: JSMarshalAs<JSType.Promise<JSType.String>>]
    public static async Task<string> RaceSynthetic()
    {
        SourceRecordStoreResult result = await Store.SaveCustomPlayAsync(Identity, 1,
            SourceCustomPlayCodec.CreateFromFormation(2, "SYN", "RACE"));
        Require(result.Status is SourceRecordStoreStatus.Saved or SourceRecordStoreStatus.Conflict, "RACE_RESULT");
        return Describe(result);
    }

    [JSExport]
    [return: JSMarshalAs<JSType.Promise<JSType.String>>]
    public static async Task<string> AbortedSave()
    {
        var previous = Store.Current;
        long generation = Store.Generation;
        int count = previous.Records.Count;
        SourceRecordStoreResult result = await Store.SaveCustomPlayAsync(Identity, 0,
            SourceCustomPlayCodec.CreateFromFormation(3, "SYN", "RETRY"));
        Require(result.Status == SourceRecordStoreStatus.IoFailure && !Store.IsWritable &&
            ReferenceEquals(Store.Current, previous) && Store.Generation == generation &&
            Store.Current.Records.Count == count, "ABORT_RETAINS_PREVIOUS_CURRENT");
        return Describe(result);
    }

    [JSExport]
    [return: JSMarshalAs<JSType.Promise<JSType.String>>]
    public static async Task<string> RetrySave()
    {
        SourceRecordStoreResult result = await Store.SaveCustomPlayAsync(Identity, 0,
            SourceCustomPlayCodec.CreateFromFormation(3, "SYN", "RETRY"));
        Saved(result);
        Require(Store.Current.Find(Identity).CustomPlays[0].Name2 == "RETRY", "RETRY_ACTUAL_CUSTOM");
        return Describe(result);
    }

    [JSExport]
    [return: JSMarshalAs<JSType.Promise<JSType.String>>]
    public static async Task<string> RecoverSynthetic()
    {
        Require((await Store.LoadAsync()).Status == SourceRecordStoreStatus.Corrupt, "CORRUPT_DISTINCT_FROM_EMPTY");
        Require((await Store.InitializeNewAsync()).Status == SourceRecordStoreStatus.InvalidData, "CORRUPT_NEVER_INITIALIZED");
        SourceRecordStoreResult result = await Store.RecoverAsync();
        Require(result.Status == SourceRecordStoreStatus.Recovered, "EXPLICIT_RECOVERY");
        return Describe(result);
    }

    [JSExport]
    [return: JSMarshalAs<JSType.Promise<JSType.String>>]
    public static async Task<string> CloseAndLoad()
    {
        Require(storage != null, "EXISTING_ADAPTER");
        storage!.Dispose();
        SourceRecordStoreResult result = await Store.LoadAsync();
        Require(result.Status == SourceRecordStoreStatus.IoFailure, "CLOSED_NOT_NOT_FOUND");
        try { await storage.ReadAsync(); throw new InvalidOperationException("RECORD_MANAGED_CHECK_CLOSED_ACCEPTED"); }
        catch (SourceRecordStorageException error) {
            Require(error.Message == "SOURCE_LOCAL_STORAGE_FAILED" && error.InnerException == null, "SANITIZED_IMPORT");
        }
        return Describe(result);
    }

    [JSExport]
    public static void Close() => storage?.Dispose();
}
