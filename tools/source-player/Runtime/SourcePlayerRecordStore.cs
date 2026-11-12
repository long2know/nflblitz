using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Blitz.Contracts;
using Blitz.Storage;

namespace Blitz.SourcePlayer;

public sealed class SourceRecordStorageImage
{
    public string Primary { get; }
    public string Backup { get; }
    public string Recovery { get; }
    public SourceRecordStorageImage(string primary, string backup = null, string recovery = null)
    { Primary = primary; Backup = backup; Recovery = recovery; }

    public string EncodeBrowserJson()
        => JsonSerializer.Serialize(new { primary = Primary, backup = Backup, recovery = Recovery });

    public static SourceRecordStorageImage DecodeBrowserJson(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Count() != 3 ||
                !root.TryGetProperty("primary", out var primary) || !root.TryGetProperty("backup", out var backup) ||
                !root.TryGetProperty("recovery", out var recovery))
                throw new SourceRecordStorageException();
            string Value(JsonElement field) => field.ValueKind == JsonValueKind.Null ? null :
                field.ValueKind == JsonValueKind.String ? field.GetString() : throw new SourceRecordStorageException();
            return new SourceRecordStorageImage(Value(primary), Value(backup), Value(recovery));
        }
        catch (Exception e) when (e is JsonException or ArgumentException or InvalidOperationException)
        { throw new SourceRecordStorageException(); }
    }
}

// Browser implementation: one IndexedDB readwrite transaction compares the
// primary opaque string and updates primary/backup/recovery together.
public interface ISourcePlayerRecordStorage
{
    Task<SourceRecordStorageImage> ReadAsync();
    Task<bool> CompareExchangeAsync(string expectedPrimary, SourceRecordStorageImage replacement);
}

public sealed class SourceRecordDelegateStorage : ISourcePlayerRecordStorage
{
    private readonly Func<Task<SourceRecordStorageImage>> read;
    private readonly Func<string, SourceRecordStorageImage, Task<bool>> compareExchange;
    public SourceRecordDelegateStorage(Func<Task<SourceRecordStorageImage>> read,
        Func<string, SourceRecordStorageImage, Task<bool>> compareExchange)
    {
        this.read = read ?? throw new ArgumentNullException(nameof(read));
        this.compareExchange = compareExchange ?? throw new ArgumentNullException(nameof(compareExchange));
    }
    public async Task<SourceRecordStorageImage> ReadAsync()
    {
        try { return await read() ?? throw new SourceRecordStorageException(); }
        catch (Exception e) when (e is not OutOfMemoryException and not AccessViolationException)
        { throw new SourceRecordStorageException(); }
    }
    public async Task<bool> CompareExchangeAsync(string expectedPrimary, SourceRecordStorageImage replacement)
    {
        try { return await compareExchange(expectedPrimary, replacement); }
        catch (Exception e) when (e is not OutOfMemoryException and not AccessViolationException)
        { throw new SourceRecordStorageException(); }
    }
}

// Optional desktop/test adapter, not browser-WASM's nondurable virtual filesystem.
// Backup/quarantine are durable before the atomic primary rename (the commit point).
public sealed class SourceRecordFileStorage : ISourcePlayerRecordStorage
{
    public const string NonUtf8RecoveryPrefix = "nflblitz-corrupt-file-base64:";
    private readonly string directory;
    public SourceRecordFileStorage(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory)) throw new ArgumentException("An explicit local save directory is required.");
        this.directory = Path.GetFullPath(directory);
        Directory.CreateDirectory(this.directory);
    }
    public Task<SourceRecordStorageImage> ReadAsync()
    {
        using var guard = Lock();
        return Task.FromResult(ReadImage());
    }
    public Task<bool> CompareExchangeAsync(string expectedPrimary, SourceRecordStorageImage replacement)
    {
        ArgumentNullException.ThrowIfNull(replacement);
        using var guard = Lock();
        if (!string.Equals(Read("records.json"), expectedPrimary, StringComparison.Ordinal)) return Task.FromResult(false);
        Write("records.previous.json", replacement.Backup);
        Write("records.recovery.json", replacement.Recovery);
        Write("records.json", replacement.Primary);
        return Task.FromResult(true);
    }
    private FileStream Lock() => new(Path.Combine(directory, "records.lock"),
        FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    private SourceRecordStorageImage ReadImage()
        => new(Read("records.json"), Read("records.previous.json"), Read("records.recovery.json"));
    private string Read(string name)
    {
        string path = Path.Combine(directory, name);
        try
        {
            byte[] bytes = File.ReadAllBytes(path);
            try { return new UTF8Encoding(false, true).GetString(bytes); }
            // Preserve arbitrary corrupt bytes losslessly for explicit quarantine/recovery.
            catch (DecoderFallbackException) { return NonUtf8RecoveryPrefix + Convert.ToBase64String(bytes); }
        }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
    }
    private void Write(string name, string value)
    {
        string path = Path.Combine(directory, name);
        if (value == null) { if (File.Exists(path)) File.Delete(path); return; }
        string pending = path + ".pending";
        try
        {
            using (var stream = new FileStream(pending, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                byte[] bytes = Encoding.UTF8.GetBytes(value);
                stream.Write(bytes); stream.Flush(true);
            }
            File.Move(pending, path, true);
        }
        finally { if (File.Exists(pending)) File.Delete(pending); }
    }
}

public enum SourceRecordStoreStatus
{
    NotLoaded, NotFound, Ready, Saved, AlreadyCommitted, Recovered,
    Corrupt, UnsupportedVersion, InvalidData, Conflict, IoFailure
}

public enum SourceRecordSavePolicy { LegacyV2 = 0, CustomBlockHistoryV3 = 1 }

public sealed class SourceRecordStoreResult
{
    public SourceRecordStoreStatus Status { get; }
    public string Code { get; }
    public long Generation { get; }
    public int RecordCount { get; }
    public bool Succeeded => Status is SourceRecordStoreStatus.Ready or SourceRecordStoreStatus.Saved
        or SourceRecordStoreStatus.AlreadyCommitted or SourceRecordStoreStatus.Recovered;
    public SourceRecordStoreResult(SourceRecordStoreStatus status, string code, long generation, int recordCount)
    { Status = status; Code = code; Generation = generation; RecordCount = recordCount; }
}

public sealed class SourceRecordExport
{
    public SourceRecordStoreResult Result { get; }
    public string Envelope { get; }
    public SourceRecordExport(SourceRecordStoreResult result, string envelope)
    { Result = result; Envelope = envelope; }
}

public sealed class SourcePlayerRecordStore
{
    public const string Schema = "nflblitz-portable-local-records";
    public const int Version = 2;
    public const int CustomBlockHistoryVersion = 3;
    public const int OldestSupportedVersion = 1;
    public const int MaximumEnvelopeCharacters = 4 * 1024 * 1024;
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 32
    };
    private readonly ISourcePlayerRecordStorage storage;
    private readonly SemaphoreSlim operations = new(1, 1);
    private SourceRecordStorageImage image;
    private SourceRecordStoreStatus loadState = SourceRecordStoreStatus.NotLoaded;
    private bool writable;
    public SourcePlayerRecords Current { get; private set; }
    public long Generation { get; private set; }
    public bool IsWritable => writable;
    public bool RequiresFormatUpgrade { get; private set; }
    public SourceRecordStoreStatus LoadState => loadState;
    public SourceRecordSavePolicy SavePolicy { get; }
    public int WriteVersion => SavePolicy == SourceRecordSavePolicy.CustomBlockHistoryV3 ? CustomBlockHistoryVersion : Version;
    // This safe notification never contains names, PINs, serialized records or IO exception messages.
    public SourceRecordStoreResult Notification { get; private set; }

    public SourcePlayerRecordStore(ISourcePlayerRecordStorage storage,
        SourceRecordSavePolicy policy = SourceRecordSavePolicy.LegacyV2)
    {
        this.storage = storage ?? throw new ArgumentNullException(nameof(storage));
        if (policy != SourceRecordSavePolicy.LegacyV2 && policy != SourceRecordSavePolicy.CustomBlockHistoryV3)
            throw new ArgumentOutOfRangeException(nameof(policy));
        SavePolicy = policy;
        Notification = new SourceRecordStoreResult(SourceRecordStoreStatus.NotLoaded, "SOURCE_SAVE_NOT_LOADED", 0, 0);
    }

    public async Task<SourceRecordStoreResult> LoadAsync()
    {
        await operations.WaitAsync();
        try
        {
            try
            {
                image = await storage.ReadAsync() ?? throw new IOException();
                writable = false;
                if (image.Primary == null)
                {
                    bool absent = image.Backup == null && image.Recovery == null;
                    loadState = absent ? SourceRecordStoreStatus.NotFound : SourceRecordStoreStatus.Corrupt;
                    return Notify(loadState, absent ? "SOURCE_SAVE_NOT_FOUND" : "SOURCE_SAVE_PRIMARY_MISSING");
                }
                Decoded decoded = Decode(image.Primary);
                loadState = decoded.Status;
                if (decoded.Status != SourceRecordStoreStatus.Ready) return Notify(decoded.Status, decoded.Code);
                Current = decoded.Records; Generation = decoded.Generation; writable = true;
                RequiresFormatUpgrade = decoded.Version < WriteVersion;
                return Notify(SourceRecordStoreStatus.Ready, decoded.Version < Version ?
                    "SOURCE_SAVE_LOADED_LEGACY_WORLD_UNAVAILABLE" : RequiresFormatUpgrade ?
                    "SOURCE_SAVE_LOADED_LEGACY_CUSTOM_BLOCK_HISTORY" : "SOURCE_SAVE_LOADED");
            }
            catch (Exception e) when (StorageFailure(e))
            {
                writable = false; loadState = SourceRecordStoreStatus.IoFailure;
                return Notify(SourceRecordStoreStatus.IoFailure, "SOURCE_SAVE_READ_FAILED");
            }
        }
        finally { operations.Release(); }
    }

    public async Task<SourceRecordStoreResult> InitializeNewAsync()
    {
        await operations.WaitAsync();
        try
        {
            if (loadState != SourceRecordStoreStatus.NotFound || image == null ||
                image.Primary != null || image.Backup != null || image.Recovery != null)
                return Notify(SourceRecordStoreStatus.InvalidData, "SOURCE_SAVE_EXPLICIT_NOT_FOUND_REQUIRED");
            return await Persist(new SourcePlayerRecords(), 1, image.Backup, image.Recovery,
                SourceRecordStoreStatus.Saved, "SOURCE_SAVE_INITIALIZED");
        }
        finally { operations.Release(); }
    }

    public Task<SourceRecordStoreResult> CreateAsync(SourceRecordIdentity identity,
        bool allowSourceLeastUsedReplacement = false)
        => Mutate(current => current.Create(identity, allowSourceLeastUsedReplacement,
            SavePolicy == SourceRecordSavePolicy.CustomBlockHistoryV3 ?
                SourceCustomBlockState.NeverAllocated : SourceCustomBlockState.LegacyUnknown), "SOURCE_RECORD_CREATED");
    public Task<SourceRecordStoreResult> SaveCustomPlayAsync(SourceRecordIdentity identity, int slotIndex, SourceCustomPlay play)
        => Mutate(current => {
            SourcePlayerRecords next = current.SaveCustomPlay(identity, slotIndex, play);
            return SavePolicy == SourceRecordSavePolicy.CustomBlockHistoryV3 && play != null ?
                next.CommitCustomBlock(identity) : next;
        }, "SOURCE_CUSTOM_PLAY_SAVED");
    public Task<SourceRecordStoreResult> CommitCustomBlockAsync(SourceRecordIdentity identity)
        => Mutate(current => {
            if (SavePolicy != SourceRecordSavePolicy.CustomBlockHistoryV3)
                throw new InvalidOperationException("SOURCE_CUSTOM_BLOCK_HISTORY_POLICY_REQUIRED");
            return current.CommitCustomBlock(identity);
        }, "SOURCE_CUSTOM_BLOCK_COMMITTED");
    public Task<SourceRecordStoreResult> SaveAudiblesAsync(SourceRecordIdentity identity, SourceRecordAudibles audibles)
        => Mutate(current => current.SaveAudibles(identity, audibles), "SOURCE_AUDIBLES_SAVED");
    public Task<SourceRecordStoreResult> DeleteAsync(SourceRecordIdentity identity)
        => Mutate(current => current.Delete(identity), "SOURCE_RECORD_DELETED");

    public async Task<SourceRecordStoreResult> CommitMatchAsync(SourceCompletedRecordMatch match)
    {
        await operations.WaitAsync();
        try
        {
            if (!writable) return NotWritable();
            try
            {
                SourceRecordMatchUpdate update = Current.CommitMatch(match);
                if (update.AlreadyCommitted) return Notify(SourceRecordStoreStatus.AlreadyCommitted, "SOURCE_MATCH_ALREADY_COMMITTED");
                return await Persist(update.Records, checked(Generation + 1), image.Primary, image.Recovery,
                    SourceRecordStoreStatus.Saved, "SOURCE_MATCH_COMMITTED");
            }
            catch (Exception e) when (ValidationFailure(e))
            { return Notify(SourceRecordStoreStatus.InvalidData, ValidationCode(e)); }
        }
        finally { operations.Release(); }
    }

    public async Task<SourceRecordExport> ExportAsync()
    {
        await operations.WaitAsync();
        try
        {
            if (Current == null) return new SourceRecordExport(NotWritable(), null);
            string envelope = Encode(Current, Generation);
            return new SourceRecordExport(Notify(SourceRecordStoreStatus.Ready,
                writable ? "SOURCE_SAVE_EXPORTED" : "SOURCE_SAVE_RETAINED_VALID_STATE_EXPORTED"), envelope);
        }
        finally { operations.Release(); }
    }

    public async Task<SourceRecordStoreResult> ImportAsync(string portableEnvelope, bool replaceFailedStore = false)
    {
        await operations.WaitAsync();
        try
        {
            Decoded imported = Decode(portableEnvelope);
            if (imported.Status != SourceRecordStoreStatus.Ready) return Notify(imported.Status, imported.Code);
            if (!writable && loadState != SourceRecordStoreStatus.NotFound && !replaceFailedStore) return NotWritable();
            if (image == null) return Notify(SourceRecordStoreStatus.InvalidData, "SOURCE_SAVE_LOAD_BEFORE_IMPORT");
            var cursors = new Dictionary<string, long>(imported.Records.CommittedMatches, StringComparer.Ordinal);
            if (Current != null)
                foreach (var cursor in Current.CommittedMatches)
                    if (!cursors.TryGetValue(cursor.Key, out long old) || old < cursor.Value) cursors[cursor.Key] = cursor.Value;
            var worlds = imported.Version < Version && Current != null ? Current.WorldRecords : imported.Records.WorldRecords;
            var next = new SourcePlayerRecords(imported.Records.Records, cursors, worlds);
            string backup = writable ? image.Primary : ValidBackup();
            string recovery = !writable && image.Primary != null ? PreserveRecovery(image.Recovery, image.Primary) : image.Recovery;
            long previousGeneration = Generation;
            if (!writable && backup != null) previousGeneration = Math.Max(previousGeneration, Decode(backup).Generation);
            return await Persist(next, checked(previousGeneration + 1), backup, recovery,
                SourceRecordStoreStatus.Saved, "SOURCE_SAVE_IMPORTED");
        }
        catch (Exception e) when (ValidationFailure(e))
        { return Notify(SourceRecordStoreStatus.InvalidData, ValidationCode(e)); }
        finally { operations.Release(); }
    }

    public async Task<SourceRecordStoreResult> RecoverAsync(bool replaceUnsupportedVersion = false)
    {
        await operations.WaitAsync();
        try
        {
            try
            {
                image = await storage.ReadAsync() ?? throw new IOException();
                Decoded primary = image.Primary == null ? null : Decode(image.Primary);
                if (primary?.Status == SourceRecordStoreStatus.Ready)
                {
                    Current = primary.Records; Generation = primary.Generation;
                    RequiresFormatUpgrade = primary.Version < WriteVersion;
                    writable = true; loadState = SourceRecordStoreStatus.Ready;
                    return Notify(SourceRecordStoreStatus.Ready, "SOURCE_SAVE_RECOVERY_NOT_NEEDED");
                }
                writable = false;
                loadState = primary?.Status ?? SourceRecordStoreStatus.Corrupt;
                if (primary?.Status == SourceRecordStoreStatus.UnsupportedVersion && !replaceUnsupportedVersion)
                    return Notify(SourceRecordStoreStatus.UnsupportedVersion, "SOURCE_SAVE_RECOVERY_REQUIRES_VERSION_REPLACEMENT_CONSENT");
                if (image.Backup == null) return Notify(SourceRecordStoreStatus.Corrupt, "SOURCE_SAVE_NO_VALID_BACKUP");
                Decoded backup = Decode(image.Backup);
                if (backup.Status != SourceRecordStoreStatus.Ready)
                    return Notify(backup.Status, "SOURCE_SAVE_NO_VALID_BACKUP");
                return await Persist(backup.Records, checked(Math.Max(Generation, backup.Generation) + 1), image.Backup,
                    PreserveRecovery(image.Recovery, image.Primary), SourceRecordStoreStatus.Recovered, "SOURCE_SAVE_BACKUP_RECOVERED");
            }
            catch (Exception e) when (StorageFailure(e))
            { writable = false; return Notify(SourceRecordStoreStatus.IoFailure, "SOURCE_SAVE_RECOVERY_IO_FAILED"); }
            catch (Exception e) when (ValidationFailure(e))
            { return Notify(SourceRecordStoreStatus.InvalidData, ValidationCode(e)); }
        }
        finally { operations.Release(); }
    }

    private async Task<SourceRecordStoreResult> Mutate(Func<SourcePlayerRecords, SourcePlayerRecords> mutation, string code)
    {
        await operations.WaitAsync();
        try
        {
            if (!writable) return NotWritable();
            try
            {
                SourcePlayerRecords next = mutation(Current);
                return await Persist(next, checked(Generation + 1), image.Primary, image.Recovery,
                    SourceRecordStoreStatus.Saved, code);
            }
            catch (Exception e) when (ValidationFailure(e))
            { return Notify(SourceRecordStoreStatus.InvalidData, ValidationCode(e)); }
        }
        finally { operations.Release(); }
    }

    private async Task<SourceRecordStoreResult> Persist(SourcePlayerRecords next, long generation,
        string backup, string recovery, SourceRecordStoreStatus success, string code)
    {
        try
        {
            string envelope = Encode(next, generation);
            var replacement = new SourceRecordStorageImage(envelope, backup, recovery);
            if (!await storage.CompareExchangeAsync(image.Primary, replacement))
            {
                writable = false; loadState = SourceRecordStoreStatus.Conflict;
                return Notify(SourceRecordStoreStatus.Conflict, "SOURCE_SAVE_STALE_WRITER");
            }
            // A rejected/failed write must never install the speculative record set.
            Current = next; Generation = generation; image = replacement;
            RequiresFormatUpgrade = false;
            writable = true; loadState = SourceRecordStoreStatus.Ready;
            return Notify(success, code);
        }
        catch (Exception e) when (StorageFailure(e))
        {
            writable = false; loadState = SourceRecordStoreStatus.IoFailure;
            return Notify(SourceRecordStoreStatus.IoFailure, "SOURCE_SAVE_WRITE_FAILED");
        }
        catch (Exception e) when (ValidationFailure(e))
        { return Notify(SourceRecordStoreStatus.InvalidData, ValidationCode(e)); }
    }

    private string ValidBackup()
    {
        if (image.Backup == null) return null;
        return Decode(image.Backup).Status == SourceRecordStoreStatus.Ready ? image.Backup : null;
    }
    private static string PreserveRecovery(string previous, string current)
    {
        if (current == null) return previous;
        if (previous == null || previous == current) return current;
        return JsonSerializer.Serialize(new[] { previous, current });
    }
    private SourceRecordStoreResult NotWritable()
        => Notify(loadState == SourceRecordStoreStatus.Ready ? SourceRecordStoreStatus.InvalidData : loadState,
            "SOURCE_SAVE_NOT_WRITABLE_LOAD_OR_RECOVER_FIRST");
    private SourceRecordStoreResult Notify(SourceRecordStoreStatus status, string code)
        => Notification = new SourceRecordStoreResult(status, code, Generation, Current?.Records.Count ?? 0);
    private static bool StorageFailure(Exception e)
        => e is IOException or UnauthorizedAccessException or NotSupportedException or System.Security.SecurityException
            or SourceRecordStorageException;
    private static bool ValidationFailure(Exception e)
        => e is ArgumentException or InvalidOperationException or OverflowException or FormatException or JsonException;
    private static string ValidationCode(Exception e)
    {
        string message = e is InvalidOperationException ? e.Message : "";
        return message switch {
            "SOURCE_RECORD_EXISTS" => message,
            "SOURCE_RECORD_NOT_FOUND" => message,
            "SOURCE_CUSTOM_PLAY_PROTECTED" => message,
            "SOURCE_RECORD_CAPACITY_REPLACEMENT_REQUIRES_CONSENT" => message,
            "SOURCE_MATCH_COMMIT_GAP" => message,
            "SOURCE_RECORD_STATISTICS_UNAVAILABLE" => message,
            "SOURCE_RECORD_SELECTION_UNAVAILABLE" => message,
            "SOURCE_CUSTOM_BLOCK_HISTORY_POLICY_REQUIRED" => message,
            _ => "SOURCE_SAVE_VALIDATION_FAILED"
        };
    }

    private string Encode(SourcePlayerRecords records, long generation)
    {
        if (generation < 1) throw new ArgumentOutOfRangeException(nameof(generation));
        var payload = new Payload {
            Records = records.Records.Select(ToDocument).ToArray(),
            CommittedMatches = new SortedDictionary<string, long>(records.CommittedMatches, StringComparer.Ordinal),
            WorldRecords = records.WorldRecords.Select(ToWorldDocument).ToArray()
        };
        string body = Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(payload, Json));
        var envelope = new Envelope {
            Schema = Schema, Version = WriteVersion, SourceRevision = SourceSavedRecordLimits.OriginalRevision,
            Generation = generation, Payload = body,
            Sha256 = Digest(WriteVersion, SourceSavedRecordLimits.OriginalRevision, generation, body)
        };
        string text = JsonSerializer.Serialize(envelope, Json);
        if (text.Length > MaximumEnvelopeCharacters) throw new ArgumentException("SOURCE_SAVE_CAPACITY");
        return text;
    }
    private Decoded Decode(string text)
    {
        if (text == null || text.Length == 0 || text.Length > MaximumEnvelopeCharacters)
            return new Decoded(SourceRecordStoreStatus.Corrupt, "SOURCE_SAVE_LENGTH_INVALID");
        try
        {
            using var document = JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = 32 });
            CheckDuplicateProperties(document.RootElement);
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty("schema", out var schema) || schema.GetString() != Schema)
                return new Decoded(SourceRecordStoreStatus.Corrupt, "SOURCE_SAVE_FORMAT_INVALID");
            if (!document.RootElement.TryGetProperty("version", out var version) || !version.TryGetInt32(out int number))
                return new Decoded(SourceRecordStoreStatus.Corrupt, "SOURCE_SAVE_VERSION_MISSING");
            if (number < OldestSupportedVersion || number > WriteVersion)
                return new Decoded(SourceRecordStoreStatus.UnsupportedVersion, "SOURCE_SAVE_VERSION_UNSUPPORTED");
            Envelope envelope = JsonSerializer.Deserialize<Envelope>(text, Json);
            if (envelope.SourceRevision != SourceSavedRecordLimits.OriginalRevision)
                return new Decoded(SourceRecordStoreStatus.UnsupportedVersion, "SOURCE_SAVE_SOURCE_REVISION_UNSUPPORTED");
            if (envelope.Generation < 1 || envelope.Payload == null || envelope.Sha256 == null ||
                envelope.Sha256 != Digest(envelope.Version, envelope.SourceRevision, envelope.Generation, envelope.Payload))
                return new Decoded(SourceRecordStoreStatus.Corrupt, "SOURCE_SAVE_INTEGRITY_FAILED");
            byte[] body = Convert.FromBase64String(envelope.Payload);
            if (Convert.ToBase64String(body) != envelope.Payload) throw new FormatException();
            string bodyText = new UTF8Encoding(false, true).GetString(body);
            using var payloadDocument = JsonDocument.Parse(bodyText, new JsonDocumentOptions { MaxDepth = 32 });
            CheckDuplicateProperties(payloadDocument.RootElement);
            Payload payload = JsonSerializer.Deserialize<Payload>(bodyText, Json);
            if (payload == null || payload.Records == null || payload.CommittedMatches == null) throw new FormatException();
            // A cultural dictionary comparer can collapse distinct raw JSON keys.
            foreach (JsonElement row in payloadDocument.RootElement.GetProperty("records").EnumerateArray())
            {
                if (row.ValueKind != JsonValueKind.Object || !row.TryGetProperty("career", out JsonElement career) ||
                    career.ValueKind != JsonValueKind.Object || career.EnumerateObject().Count() != 17)
                    throw new FormatException();
                bool hasBlockState = row.TryGetProperty("customBlockState", out JsonElement blockState);
                if (number == CustomBlockHistoryVersion ?
                    !hasBlockState || blockState.ValueKind != JsonValueKind.Number ||
                        !blockState.TryGetInt32(out int state) || state < 0 || state > 2 :
                    hasBlockState)
                    throw new FormatException();
            }
            if (number == 1 && payloadDocument.RootElement.TryGetProperty("worldRecords", out _))
                throw new FormatException();
            if (number >= 2 && (payload.WorldRecords == null || payload.WorldRecords.Length != SourceSavedRecordLimits.WorldRecordCount))
                throw new FormatException();
            var worlds = payload.WorldRecords?.Select(FromWorldDocument).ToArray();
            var records = new SourcePlayerRecords(payload.Records.Select(record => FromDocument(record, number)).ToArray(),
                payload.CommittedMatches, worlds);
            return new Decoded(records, envelope.Generation, number);
        }
        catch (Exception e) when (ValidationFailure(e))
        { return new Decoded(SourceRecordStoreStatus.Corrupt, "SOURCE_SAVE_CONTENT_INVALID"); }
    }
    private static string Digest(int version, string revision, long generation, string payload)
    {
        string preimage = Schema + "\n" + version.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\n" +
            revision + "\n" + generation.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\n" + payload;
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(preimage))).ToLowerInvariant();
    }
    private static void CheckDuplicateProperties(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonProperty property in value.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new FormatException();
                CheckDuplicateProperties(property.Value);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
            foreach (JsonElement item in value.EnumerateArray()) CheckDuplicateProperties(item);
    }
    private RecordDocument ToDocument(SourcePlayerRecord record)
    {
        SourceRecordCareer c = record.Career;
        return new RecordDocument {
            RecordNumber = record.RecordNumber, Name = record.Identity.Name,
            Pin = record.Identity.CopyPinForLocalPersistence(), DiskBank = record.DiskBank, PlaysAllowed = record.PlaysAllowed,
            Career = new SortedDictionary<string, int>(StringComparer.Ordinal) {
                ["last_used"] = c.LastUsed, ["games_played"] = c.GamesPlayed, ["games_won"] = c.GamesWon,
                ["winstreak"] = c.WinStreak, ["points_scored"] = c.PointsScored, ["points_allowed"] = c.PointsAllowed,
                ["teams_defeated"] = c.TeamsDefeated, ["tackles"] = c.Tackles, ["sacks"] = c.Sacks,
                ["passing_yds"] = c.PassingYards, ["rushing_yds"] = c.RushingYards, ["world_records"] = c.WorldRecords,
                ["pass_attempts"] = c.PassAttempts, ["pass_completions"] = c.PassCompletions,
                ["pass_touchdowns"] = c.PassTouchdowns, ["intercepted"] = c.Intercepted, ["trivia_pts"] = c.TriviaPoints
            },
            CustomPlays = record.CustomPlays.Select(play => play == null ? null :
                Convert.ToBase64String(SourceCustomPlayCodec.EncodeCompact(play))).ToArray(),
            OffenseAudibles = record.Audibles.Offense.ToArray(), DefenseAudibles = record.Audibles.Defense.ToArray(),
            CustomBlockState = SavePolicy == SourceRecordSavePolicy.CustomBlockHistoryV3 ? (int)record.CustomBlockState : null
        };
    }
    private SourcePlayerRecord FromDocument(RecordDocument record, int version)
    {
        if (record == null || record.Career == null || record.Career.Count != 17 ||
            record.CustomPlays == null || record.CustomPlays.Length != 9)
            throw new FormatException();
        var c = record.Career;
        int Field(string key)
        {
            foreach (var item in c)
                if (StringComparer.Ordinal.Equals(item.Key, key)) return item.Value;
            throw new FormatException();
        }
        var career = new SourceRecordCareer(Field("last_used"), Field("games_played"), Field("games_won"), Field("winstreak"),
            Field("points_scored"), Field("points_allowed"), Field("teams_defeated"), Field("tackles"), Field("sacks"),
            Field("passing_yds"), Field("rushing_yds"), Field("world_records"), Field("pass_attempts"),
            Field("pass_completions"), Field("pass_touchdowns"), Field("intercepted"), Field("trivia_pts"));
        var plays = new SourceCustomPlay[9];
        for (int i = 0; i < plays.Length; i++)
        {
            if (record.CustomPlays[i] == null) continue;
            byte[] bytes = Convert.FromBase64String(record.CustomPlays[i]);
            if (Convert.ToBase64String(bytes) != record.CustomPlays[i]) throw new FormatException();
            SourceCustomPlayValidation decoded = SourceCustomPlayCodec.DecodeCompact(bytes);
            if (!decoded.Valid || decoded.Repaired) throw new FormatException();
            plays[i] = decoded.Play;
        }
        SourceCustomBlockState blockState = SourceCustomBlockState.LegacyUnknown;
        if (version == CustomBlockHistoryVersion)
            blockState = (SourceCustomBlockState)(record.CustomBlockState ?? throw new FormatException());
        else if (SavePolicy == SourceRecordSavePolicy.CustomBlockHistoryV3 && plays.Any(play => play != null && play.Exists))
            blockState = SourceCustomBlockState.Allocated;
        return new SourcePlayerRecord(record.RecordNumber, new SourceRecordIdentity(record.Name, record.Pin), career,
            record.DiskBank, record.PlaysAllowed, plays, new SourceRecordAudibles(record.OffenseAudibles, record.DefenseAudibles),
            blockState);
    }
    private static WorldRecordDocument ToWorldDocument(SourceWorldRecord world)
        => world == null ? null : new WorldRecordDocument {
            Category = (int)world.Category, Value = world.Value, Name = world.Holder.Name,
            Pin = world.Holder.CopyPinForLocalPersistence()
        };
    private static SourceWorldRecord FromWorldDocument(WorldRecordDocument world)
        => world == null ? null : new SourceWorldRecord((SourceWorldRecordCategory)world.Category, world.Value,
            new SourceRecordIdentity(world.Name, world.Pin));
    private sealed class Decoded
    {
        public SourceRecordStoreStatus Status { get; }
        public string Code { get; }
        public SourcePlayerRecords Records { get; }
        public long Generation { get; }
        public int Version { get; }
        public Decoded(SourceRecordStoreStatus status, string code) { Status = status; Code = code; }
        public Decoded(SourcePlayerRecords records, long generation, int version)
        {
            Status = SourceRecordStoreStatus.Ready; Records = records; Generation = generation;
            Version = version; Code = "SOURCE_SAVE_VALID";
        }
    }
    private sealed class Envelope
    {
        public Envelope() { }
        [JsonRequired] public string Schema { get; set; }
        [JsonRequired] public int Version { get; set; }
        [JsonRequired] public string SourceRevision { get; set; }
        [JsonRequired] public long Generation { get; set; }
        [JsonRequired] public string Payload { get; set; }
        [JsonRequired] public string Sha256 { get; set; }
    }
    private sealed class Payload
    {
        public Payload() { }
        [JsonRequired] public RecordDocument[] Records { get; set; }
        [JsonRequired] public SortedDictionary<string, long> CommittedMatches { get; set; }
        public WorldRecordDocument[] WorldRecords { get; set; }
    }
    private sealed class RecordDocument
    {
        public RecordDocument() { }
        [JsonRequired] public int RecordNumber { get; set; }
        [JsonRequired] public string Name { get; set; }
        [JsonRequired] public string Pin { get; set; }
        [JsonRequired] public int DiskBank { get; set; }
        [JsonRequired] public int PlaysAllowed { get; set; }
        [JsonRequired] public SortedDictionary<string, int> Career { get; set; }
        [JsonRequired] public string[] CustomPlays { get; set; }
        [JsonRequired] public short[] OffenseAudibles { get; set; }
        [JsonRequired] public short[] DefenseAudibles { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? CustomBlockState { get; set; }
    }
    private sealed class WorldRecordDocument
    {
        public WorldRecordDocument() { }
        [JsonRequired] public int Category { get; set; }
        [JsonRequired] public int Value { get; set; }
        [JsonRequired] public string Name { get; set; }
        [JsonRequired] public string Pin { get; set; }
    }
}

// JS/IndexedDB adapters wrap browser exception text in this data-free boundary.
public sealed class SourceRecordStorageException : Exception
{
    public SourceRecordStorageException() : base("SOURCE_LOCAL_STORAGE_FAILED") { }
}
