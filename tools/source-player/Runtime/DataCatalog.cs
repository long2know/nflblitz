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
    public string ManifestSha256 { get; }
    public int FileCount => files.Count;
    public long ByteCount { get; }
    public IReadOnlyDictionary<string, FilePin> Files => files;

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
            root.GetProperty("profile").GetString() == SourceTeamResourceProfile.C2M1StableId &&
            root.GetProperty("sourceRevision").GetString() == SourceEnvironmentReader.SourceCommit &&
            root.GetProperty("converterRevision").GetString() == SourceEnvironmentReader.ToolsCommit,
            "Only the explicitly identified original M1 portable preview data generation is supported.");
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
            "animations", "callbacks", "hit", "impact", "world", "teams", "hud", "settings", "bootstrap" })
            Require(entrypoints.ContainsKey(name), "Required actual source service is missing: " + name);
    }

    public byte[] Read(string relative)
    {
        relative = Portable(relative);
        if (!files.TryGetValue(relative, out FilePin pin))
            throw new InvalidDataException("An unpinned asset was requested: " + relative);
        byte[] bytes = read(relative);
        Require(bytes != null && bytes.Length == pin.Bytes && Hash(bytes) == pin.Sha256,
            "Missing/changed actual source asset: " + relative + ". No substitute is allowed.");
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
