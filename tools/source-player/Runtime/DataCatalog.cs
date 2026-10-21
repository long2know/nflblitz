using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Blitz.AssetImport;

namespace Blitz.SourcePlayer;

public sealed class DataCatalog
{
    private readonly Func<string, byte[]> read;
    private readonly Dictionary<string, FilePin> files = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> entrypoints = new(StringComparer.Ordinal);
    private readonly HashSet<string> consumed = new(StringComparer.Ordinal);
    public string ManifestSha256 { get; }
    public int FileCount => files.Count;
    public long ByteCount { get; }
    public IReadOnlyDictionary<string, FilePin> Files => files;
    public IReadOnlyList<FilePin> RetainedInputs { get; } = Array.Empty<FilePin>();
    public int ActiveInputCount => FileCount - RetainedInputs.Count;
    public int ConsumedInputCount => consumed.Count;
    public int VerifiedRetainedInputCount => RetainedInputs.Count(pin => consumed.Contains(pin.Path));
    public SourceTeamResourceProfile Profile { get; }

    public sealed record FilePin(string Path, int Bytes, string Sha256);

    public DataCatalog(byte[] manifest, Func<string, byte[]> read)
    {
        this.read = read ?? throw new ArgumentNullException(nameof(read));
        if (manifest is null || manifest.Length is < 2 or > 1024 * 1024)
            throw new InvalidDataException("An explicit bounded private source-player manifest is required.");
        ManifestSha256 = Hash(manifest);
        using var json = JsonDocument.Parse(manifest);
        var root = json.RootElement;
        Require(root.GetProperty("schema").GetString() == "blitz-portable-source-player/v1" &&
            root.GetProperty("runtimeFlavor").GetString() == PlayerHost.RuntimeFlavor &&
            (root.GetProperty("profile").GetString() == SourceTeamResourceProfile.C2M1StableId ||
                root.GetProperty("profile").GetString() == SourceTeamResourceProfile.PreSnapSwitchStableId) &&
            root.GetProperty("sourceRevision").GetString() == SourceEnvironmentReader.SourceCommit &&
            root.GetProperty("converterRevision").GetString() == SourceEnvironmentReader.ToolsCommit,
            "Only the explicitly identified historical M1 or pre-snap-switch portable data generation is supported.");
        Profile = SourceTeamResourceProfile.FromStableId(root.GetProperty("profile").GetString());
        var aliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in root.GetProperty("files").EnumerateArray())
        {
            string path = Portable(value.GetProperty("path").GetString());
            string hash = value.GetProperty("sha256").GetString();
            int count = value.GetProperty("bytes").GetInt32();
            Require(aliases.Add(path) && count is > 0 and <= 64 * 1024 * 1024 && IsHash(hash),
                "Invalid, duplicate or aliased private asset pin: " + path);
            files.Add(path, new FilePin(path, count, hash));
            ByteCount += count;
        }
        Require(files.Count is >= 300 and <= 1024 && ByteCount <= 512L * 1024 * 1024,
            "The complete bounded source asset inventory is required, not a world-only inspector.");
        foreach (var value in root.GetProperty("entrypoints").EnumerateObject())
        {
            string path = Portable(value.Value.GetString());
            Require(files.ContainsKey(path), "Unpinned source entrypoint: " + value.Name);
            entrypoints.Add(value.Name, path);
        }
        foreach (string name in new[] { "rosters", "model", "skeleton", "playbook", "movement", "sound",
            "animations", "callbacks", "hit", "impact", "world", "teams", "hud", "settings", "bootstrap", "freeBall" })
            Require(entrypoints.ContainsKey(name), "Required actual source service is missing: " + name);
        if (Profile.IsContact282C2M1PreSnapSwitch)
        {
            Require(files.Count == 563, "Pre-snap switch requires the exact563-file successor, not a relabeled historical bundle.");
            foreach (var pair in new[] {
                ("animations", "animation/export-inventory-presnap-switch.json", Profile.AnimationSha256),
                ("callbacks", "kernel/c2-presnap-switch-callback-data.json", SourceInterfacePackagePlan.PreSnapSwitchCallbackSha256),
                ("impact", "kernel/impact-presnap-switch-presentation.json", SourceTeamResourceProfile.PreSnapSwitchImpactSha256)
            })
                Require(Entry(pair.Item1) == pair.Item2 && EntryHash(pair.Item1) == pair.Item3,
                    "Pre-snap switch entrypoint/generation differs: " + pair.Item1);
            Require(Entry("settings") == "settings-presnap-switch.json" &&
                files.ContainsKey("animation/clips/M_HOPCLP.json"),
                "The explicit new configuration and authentic hop clip are mandatory.");
            RetainedInputs = Array.AsReadOnly(new[] {
                new FilePin("animation/export-inventory.json", 194429, SourceTeamResourceProfile.C2AnimationSha256),
                new FilePin("kernel/c2-callback-data.json", 8195, SourceInterfacePackagePlan.C2M1CallbackSha256),
                new FilePin("kernel/impact-presentation.json", 141550, SourceTeamResourceProfile.C2M1ImpactSha256),
                new FilePin("settings.json", 834, "a78aaa6144e80700550ff1204c393bda2c279200dbf9b5a4dad147d6c6b20777")
            });
            foreach (FilePin pin in RetainedInputs)
            {
                Require(files.TryGetValue(pin.Path, out FilePin actual) && actual == pin,
                    "Missing/changed sealed retained historical input: " + pin.Path);
                Require(!entrypoints.ContainsValue(pin.Path),
                    "A sealed retained historical input cannot be an active source service: " + pin.Path);
            }
        }
    }

    internal void VerifyRetainedInputs()
    {
        foreach (FilePin pin in RetainedInputs) Read(pin.Path);
    }

    public void RequireAllInputsConsumed()
    {
        string[] remaining = files.Keys.Where(path => !consumed.Contains(path)).OrderBy(path => path, StringComparer.Ordinal).ToArray();
        Require(remaining.Length == 0, "The real source loaders left " + remaining.Length +
            " unconsumed private input files: " + string.Join(", ", remaining));
    }

    public byte[] Read(string relative)
    {
        relative = Portable(relative);
        if (!files.TryGetValue(relative, out FilePin pin))
            throw new InvalidDataException("An unpinned asset was requested: " + relative);
        byte[] bytes = read(relative);
        Require(bytes != null && bytes.Length == pin.Bytes && Hash(bytes) == pin.Sha256,
            "Missing/changed actual source asset: " + relative + ". No substitute is allowed.");
        consumed.Add(relative);
        return bytes;
    }

    public string Entry(string name) => entrypoints.TryGetValue(name, out string path) ? path :
        throw new InvalidDataException("Missing actual source service: " + name);
    public bool HasEntry(string name) => entrypoints.ContainsKey(name);
    public byte[] EntryBytes(string name) => Read(Entry(name));
    public string EntryHash(string name) => files[Entry(name)].Sha256;
    public static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    public static string Portable(string path)
    {
        Require(!string.IsNullOrWhiteSpace(path) && path.Length <= 256 && !path.Contains('\\') &&
            !path.Contains(':') && path.Split('/').All(p => p.Length > 0 && p != "." && p != ".."),
            "A bounded portable relative asset path is required.");
        AuthorizedAssetRoot.ValidateRelativePath(path);
        return path;
    }

    public static string Text(byte[] bytes) => new UTF8Encoding(false, true).GetString(bytes);
    private static bool IsHash(string value) => value is { Length: 64 } &&
        value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
    public static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }
}
