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
    public string FeatureContract { get; }
    public string Factory { get; }
    public const string CompleteAv306Schema = "blitz-portable-source-player/v3";
    public const string CompleteAv306InventorySha256 = "c8fe584f8aec0c82eb34b80dd64bc494df4e5238848dfc747f6de8e1be5eb2e0";
    public const string CompleteAv306TwoStationRecordsSettingsPath = "settings-av306-two-station-records-v1.json";
    public const string CompleteAv306AncestorInventorySha256 = "ac113907c28a628cc6d8d702add474eddbb0567c07e9e4fa3d3788316f769797";
    public const string CompleteAv306AddedInventorySha256 = "4ebd1e83c5fcb6818d1d2a68d1531175c72c7893628bf99b2931b17a30bb39e5";
    public const string SelectedWorldSchema = "blitz-portable-source-player/v2";
    public const string SelectedWorldManifestSha256 = "aeb2efdebdbe260f63f6d785c461048dc9cec337098490761c2815cd8ffc4edd";
    public const string SelectedWorldCatalogSha256 = "b66d491f6f1d7956de2fd9f103f1c6d227a22a48d313e29057b04defaaf6bace";
    public const string SelectedTurfNightManifestSha256 = "ad66460368f7c71e61cd44146fe6fa362da44b8b1ea43958de7434b6d6df2c8f";
    public const string SelectedTurfNightCatalogSha256 = "b541301645c7afe8d0b0c1928e86b43fb6ae522ce117f6124238d00630ae5e13";

    public sealed record FilePin(string Path, int Bytes, string Sha256);

    private static readonly FilePin[] CompleteAv306AddedInputs = {
        new("animation/clips/QB_RDY.json", 121195, "7eaab9a19cdfed047726ca2d26d0ad291c19ef3c8efecebb052912a0efd2e0d8"),
        new("animation/clips/QB_RDYF.json", 121074, "4abac02bf1426ff9e92d3564bfec2479e0b5bf9c2fc42bb1e47c9f3b4faa069d"),
        new("animation/clips/RUSH_I.json", 185727, "ba5a1fcb52c0180ebf096d206e9062e18d87c21a03fc71cb766ea90be247038d"),
        new("animation/export-inventory-complete-av306.json", 222508, SourceTeamResourceProfile.CompleteAv306AnimationSha256),
        new("camera/complete-av306-course.json", 199776, SourceTeamResourceProfile.CompleteAv306CourseSha256),
        new("kernel/c2-complete-av306-callback-data.json", 9372, SourceTeamResourceProfile.CompleteAv306CallbackSha256),
        new("kernel/impact-complete-av306-presentation.json", 151033, SourceTeamResourceProfile.CompleteAv306ImpactSha256)
    };

    public DataCatalog(byte[] manifest, Func<string, byte[]> read)
    {
        this.read = read ?? throw new ArgumentNullException(nameof(read));
        if (manifest is null || manifest.Length is < 2 or > 1024 * 1024)
            throw new InvalidDataException("An explicit bounded private source-player manifest is required.");
        ManifestSha256 = Hash(manifest);
        using var json = JsonDocument.Parse(manifest);
        var root = json.RootElement;
        bool av306 = root.GetProperty("profile").GetString() == SourceTeamResourceProfile.CompleteAv306StableId;
        bool twoStationRecords = root.GetProperty("profile").GetString() ==
            SourceTeamResourceProfile.CompleteAv306TwoStationRecordsStableId;
        bool av306Assets = av306 || twoStationRecords;
        bool selectedWorld = root.GetProperty("profile").GetString() ==
            SourceTeamResourceProfile.CompleteOriginalSelectedWorldStableId;
        bool turfNight = selectedWorld && ManifestSha256 == SelectedTurfNightManifestSha256;
        Require(root.GetProperty("schema").GetString() == (av306Assets ? CompleteAv306Schema :
                selectedWorld ? SelectedWorldSchema : "blitz-portable-source-player/v1") &&
            root.GetProperty("runtimeFlavor").GetString() == PlayerHost.RuntimeFlavor &&
            (root.GetProperty("profile").GetString() == SourceTeamResourceProfile.C2M1StableId ||
                root.GetProperty("profile").GetString() == SourceTeamResourceProfile.PreSnapSwitchStableId ||
                root.GetProperty("profile").GetString() == SourceTeamResourceProfile.DefaultAudiblesStableId ||
                root.GetProperty("profile").GetString() == SourceTeamResourceProfile.CompleteOriginalStableId ||
                root.GetProperty("profile").GetString() == SourceTeamResourceProfile.CompleteOriginalSourceBanksStableId ||
                root.GetProperty("profile").GetString() == SourceTeamResourceProfile.CompleteOriginalRecordsStableId ||
                root.GetProperty("profile").GetString() == SourceTeamResourceProfile.CompleteOriginalFinalStableId ||
                root.GetProperty("profile").GetString() == SourceTeamResourceProfile.CompleteOriginalPrematchStableId ||
                selectedWorld || av306Assets) &&
            root.GetProperty("sourceRevision").GetString() == SourceEnvironmentReader.SourceCommit &&
            root.GetProperty("converterRevision").GetString() == SourceEnvironmentReader.ToolsCommit,
            "Only the explicitly identified historical M1 or pre-snap-switch portable data generation is supported.");
        Profile = SourceTeamResourceProfile.FromStableId(root.GetProperty("profile").GetString());
        if (twoStationRecords)
        {
            Require(ManifestSha256 == SourceTeamResourceProfile.CompleteAv306TwoStationRecordsManifestSha256 &&
                root.GetProperty("featureContract").GetString() == SourceTeamResourceProfile.CompleteAv306TwoStationRecordsFeatureContract &&
                root.GetProperty("factory").GetString() == SourceTeamResourceProfile.CompleteAv306TwoStationRecordsFactory &&
                root.GetProperty("ancestorManifestSha256").GetString() == SourceTeamResourceProfile.CompleteAv306ManifestSha256 &&
                root.GetProperty("features").GetProperty("originalSelectedClipInputs").GetInt32() == 306 &&
                root.GetProperty("features").GetProperty("originalCompiledPlaylistPrograms").GetInt32() == 1205 &&
                root.GetProperty("features").GetProperty("originalStationLayout").GetInt32() == 2 &&
                root.GetProperty("features").GetProperty("sourceTwoStationRecordWorkflow").GetString() ==
                    "source-two-station-record-workflow/v1",
                "Named TwoStation requires its distinct authenticated profile, factory and actual two-station settings generation.");
            FeatureContract = root.GetProperty("featureContract").GetString();
            Factory = root.GetProperty("factory").GetString();
        }
        else if (av306)
        {
            Require(ManifestSha256 == SourceTeamResourceProfile.CompleteAv306ManifestSha256 &&
                root.GetProperty("featureContract").GetString() == SourceTeamResourceProfile.CompleteAv306FeatureContract &&
                root.GetProperty("factory").GetString() == SourceTeamResourceProfile.CompleteAv306Factory &&
                root.GetProperty("ancestorManifestSha256").GetString() == SelectedWorldManifestSha256 &&
                root.GetProperty("features").GetProperty("originalSelectedClipInputs").GetInt32() == 306 &&
                root.GetProperty("features").GetProperty("originalCompiledPlaylistPrograms").GetInt32() == 1205,
                "CompleteAv306 requires its exact distinct v3 contract,306 clips,1205 programmes and unchanged1114 ancestor.");
            FeatureContract = root.GetProperty("featureContract").GetString();
            Factory = root.GetProperty("factory").GetString();
        }
        else if (selectedWorld)
        {
            Require((ManifestSha256 == SelectedWorldManifestSha256 || turfNight) &&
                root.GetProperty("featureContract").GetString() == SourceTeamResourceProfile.CompleteOriginalSelectedWorldFeatureContract &&
                root.GetProperty("factory").GetString() == SourceTeamResourceProfile.CompleteOriginalSelectedWorldFactory &&
                root.GetProperty("ancestorManifestSha256").GetString() == SourceTeamResourceProfile.CompleteOriginalPrematchManifestSha256,
                "The exact separately authenticated complete selected-world v2 successor is required.");
            FeatureContract = root.GetProperty("featureContract").GetString();
            Factory = root.GetProperty("factory").GetString();
        }
        else if (Profile.IsContact282C2M1DefaultAudibles)
        {
            Require(root.TryGetProperty("featureContract", out var feature) &&
                feature.ValueKind == JsonValueKind.String &&
                feature.GetString() == SourceInterfacePackagePlan.DefaultAudiblesFeatureContract,
                "Default audibles require explicit featureContract=source-default-audibles/v1; a relabeled v4 manifest is not admission.");
            Require(root.TryGetProperty("factory", out var factory) &&
                factory.ValueKind == JsonValueKind.String &&
                factory.GetString() == SourceInterfacePackagePlan.DefaultAudiblesFactory,
                "Default audibles require the explicit SourceC2Composition.CreateDefaultAudiblesSimulation factory.");
            FeatureContract = feature.GetString();
            Factory = factory.GetString();
            Require(ManifestSha256 == SourceInterfacePackagePlan.DefaultAudiblesManifestSha256,
                "Default audibles require the exact metadata-only manifest and unchanged563 payload pins/entrypoints.");
        }
        else if (Profile.IsCompleteOriginal303)
        {
            Require(ManifestSha256 == (Profile.IsCompleteOriginalPrematch303 ? SourceTeamResourceProfile.CompleteOriginalPrematchManifestSha256 :
                    Profile.IsCompleteOriginalFinal303 ? SourceTeamResourceProfile.CompleteOriginalFinalManifestSha256 :
                    Profile.IsCompleteOriginalRecords303 ? SourceTeamResourceProfile.CompleteOriginalRecordsManifestSha256 :
                    Profile.IsCompleteOriginalSourceBanks303 ?
                    SourceTeamResourceProfile.CompleteOriginalSourceBanksManifestSha256 : SourceTeamResourceProfile.CompleteOriginalManifestSha256) &&
                root.GetProperty("featureContract").GetString() == (Profile.IsCompleteOriginalPrematch303 ?
                    SourceTeamResourceProfile.CompleteOriginalPrematchFeatureContract : Profile.IsCompleteOriginalRecords303 ?
                    SourceTeamResourceProfile.CompleteOriginalRecordsFeatureContract : SourceTeamResourceProfile.CompleteOriginalFeatureContract) &&
                root.GetProperty("factory").GetString() == (Profile.IsCompleteOriginalPrematch303 ?
                    SourceTeamResourceProfile.CompleteOriginalPrematchFactory : Profile.IsCompleteOriginalFinal303 ?
                    SourceTeamResourceProfile.CompleteOriginalFinalFactory : Profile.IsCompleteOriginalRecords303 ?
                    SourceTeamResourceProfile.CompleteOriginalRecordsFactory : Profile.IsCompleteOriginalSourceBanks303 ?
                    SourceTeamResourceProfile.CompleteOriginalSourceBanksFactory : SourceTeamResourceProfile.CompleteOriginalFactory),
                "CompleteOriginal303 requires its explicit feature contract and actual source factory.");
            FeatureContract = root.GetProperty("featureContract").GetString();
            Factory = root.GetProperty("factory").GetString();
        }
        else
            Require(!root.TryGetProperty("featureContract", out _) && !root.TryGetProperty("factory", out _),
                "Historical manifests cannot carry the successor default-audibles feature/factory metadata.");
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
        Require((twoStationRecords ? files.Count == 1154 && ByteCount == 300324845 :
                av306 ? files.Count == 1153 && ByteCount == 300324289 :
                selectedWorld ? turfNight ? files.Count == 1110 && ByteCount == 262079703 :
                files.Count == 1114 && ByteCount == 262649366 :
                files.Count is >= 300 and <= 1024) && ByteCount <= 512L * 1024 * 1024,
            "The complete bounded source asset inventory is required, not a world-only inspector.");
        foreach (var value in root.GetProperty("entrypoints").EnumerateObject())
        {
            string path = Portable(value.Value.GetString());
            Require(files.ContainsKey(path), "Unpinned source entrypoint: " + value.Name);
            entrypoints.Add(value.Name, path);
        }
        if (twoStationRecords)
        {
            Require(Entry("settings") == CompleteAv306TwoStationRecordsSettingsPath &&
                files[Entry("settings")] == new FilePin(CompleteAv306TwoStationRecordsSettingsPath, 556,
                    SourceTeamResourceProfile.CompleteAv306TwoStationRecordsSettingsSha256),
                "The named TwoStation generation requires its exact new two-station settings input.");
            FilePin[] inherited = files.Values.Where(pin => pin.Path != CompleteAv306TwoStationRecordsSettingsPath).ToArray();
            Require(inherited.Length == 1153 && inherited.Sum(pin => (long)pin.Bytes) == 300324289 &&
                InventoryHash(inherited) == CompleteAv306InventorySha256,
                "Named TwoStation must retain all1153 actual AV306 inputs byte-identically; no FourStation data relabeling.");
        }
        if (av306Assets) RequireCompleteAv306Ancestor(twoStationRecords ? CompleteAv306TwoStationRecordsSettingsPath : null);
        if (selectedWorld || av306Assets)
        {
            JsonElement admission = root.GetProperty("selectedWorld");
            string catalogSha = turfNight ? SelectedTurfNightCatalogSha256 : SelectedWorldCatalogSha256;
            Require(admission.GetProperty("schema").GetString() == "source-complete-selected-world-admission/v2" &&
                admission.GetProperty("catalogSha256").GetString() == catalogSha &&
                admission.GetProperty("field").GetInt32() == (turfNight ? 2 : 0) &&
                admission.GetProperty("stadium").GetInt32() == (turfNight ? 1 : 0) &&
                admission.GetProperty("defaultHomeTeamIndex").GetInt32() == (turfNight ? 22 : 0) &&
                admission.GetProperty("defaultHomePrefix").GetString() == (turfNight ? "EAG" : "CAR") &&
                admission.GetProperty("defaultSelectionId").GetString() == (turfNight ?
                    "source-field2-stadpie/field-2/stadium-1/EAG" : "source-field2-stadpie/field-0/stadium-0/CAR") &&
                Entry("worldCatalog") == "world/catalog.json" && EntryHash("worldCatalog") == catalogSha &&
                Entry("world") == "world/environment.json" &&
                EntryHash("world") == (turfNight ? "711d9a1e56b5c4adb893391975302ce2920567f7e47b47354ebe254203735680" :
                    "fbd1dbdba23d09cbade59906ecb6814e0b226325c5fd146676eeed02dfaaf1fe"),
                "The successor must bind its exact source31-team catalog/default field/stadium/designated home.");
        }
        else
            Require(!HasEntry("worldCatalog"), "Historical v1 packages cannot admit a successor world catalog.");
        if (av306Assets)
            Require(Entry("courseCamera") == "camera/complete-av306-course.json" &&
                EntryHash("courseCamera") == SourceTeamResourceProfile.CompleteAv306CourseSha256 &&
                Entry("originalAudio") == "audio/complete-av306-manifest.json" &&
                EntryHash("originalAudio") == SourceAudioPlaybackAdmission.CompleteAv306ManifestSha256,
                "CompleteAv306 requires its exact course and independently qualified team-speech audio inputs.");
        else
            Require(!HasEntry("courseCamera"), "Historical packages cannot acquire the separate306 course capability.");
        foreach (string name in new[] { "rosters", "model", "skeleton", "playbook", "movement", "sound",
            "animations", "callbacks", "hit", "impact", "world", "teams", "hud", "settings", "bootstrap", "freeBall" })
            Require(entrypoints.ContainsKey(name), "Required actual source service is missing: " + name);
        if (Profile.UsesPreSnapSwitchPayload)
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
        if (Profile.UsesCompleteSourcePrograms)
        {
            foreach (var pair in new[] {
                ("animations", av306Assets ? "animation/export-inventory-complete-av306.json" :
                    "animation/export-inventory-complete-original.json", Profile.AnimationSha256),
                ("callbacks", av306Assets ? "kernel/c2-complete-av306-callback-data.json" :
                    "kernel/c2-complete-original-callback-data.json", av306Assets ?
                    SourceTeamResourceProfile.CompleteAv306CallbackSha256 : SourceTeamResourceProfile.CompleteOriginalCallbackSha256),
                ("impact", av306Assets ? "kernel/impact-complete-av306-presentation.json" :
                    "kernel/impact-complete-original-presentation.json", av306Assets ?
                    SourceTeamResourceProfile.CompleteAv306ImpactSha256 : Profile.IsCompleteOriginalFinal303 ?
                    SourceTeamResourceProfile.CompleteOriginalFinalImpactSha256 : SourceTeamResourceProfile.CompleteOriginalImpactSha256),
                ("originalPresentation", "original/manifest.json", Profile.UsesPreGamePresentation ?
                    SourceOriginalPresentationData.PrematchManifestSha256 : Profile.UsesRecordPresentation ?
                    SourceOriginalPresentationData.RecordsManifestSha256 : SourceOriginalPresentationData.ManifestSha256)
            })
                Require(Entry(pair.Item1) == pair.Item2 && EntryHash(pair.Item1) == pair.Item3,
                    "CompleteOriginal303 exact input generation differs: " + pair.Item1);
            Require(Entry("settings") == (twoStationRecords ? CompleteAv306TwoStationRecordsSettingsPath :
                "settings-presnap-switch.json") && HasEntry("originalAudio"),
                "The complete source profile requires its own authenticated original57 cabinet settings and decoded audio service.");
            string[] oldPaths = { "animation/export-inventory.json", "kernel/c2-callback-data.json",
                "kernel/impact-presentation.json", "settings.json", "animation/export-inventory-presnap-switch.json",
                "kernel/c2-presnap-switch-callback-data.json", "kernel/impact-presnap-switch-presentation.json" };
            string[] oldHashes = { SourceTeamResourceProfile.C2AnimationSha256, SourceInterfacePackagePlan.C2M1CallbackSha256,
                SourceTeamResourceProfile.C2M1ImpactSha256, "a78aaa6144e80700550ff1204c393bda2c279200dbf9b5a4dad147d6c6b20777",
                SourceTeamResourceProfile.PreSnapSwitchAnimationSha256, SourceInterfacePackagePlan.PreSnapSwitchCallbackSha256,
                SourceTeamResourceProfile.PreSnapSwitchImpactSha256 };
            RetainedInputs = Array.AsReadOnly(oldPaths.Select((path, i) => {
                Require(files.TryGetValue(path, out FilePin pin) && pin.Sha256 == oldHashes[i] &&
                    !entrypoints.ContainsValue(path), "A sealed inherited original input changed or became active: " + path);
                return pin;
            }).ToArray());
            if (av306Assets)
            {
                var previous = new[] {
                    new FilePin("animation/export-inventory-complete-original.json", 220332, SourceTeamResourceProfile.CompleteOriginalAnimationSha256),
                    new FilePin("kernel/c2-complete-original-callback-data.json", 9263, SourceTeamResourceProfile.CompleteOriginalCallbackSha256),
                    new FilePin("kernel/impact-complete-original-presentation.json", 150929, SourceTeamResourceProfile.CompleteOriginalFinalImpactSha256)
                };
                foreach (FilePin pin in previous)
                    Require(files.TryGetValue(pin.Path, out FilePin actual) && actual == pin &&
                        !entrypoints.ContainsValue(pin.Path),
                        "The sealed previous303 metadata must remain byte-identical and inactive: " + pin.Path);
                RetainedInputs = Array.AsReadOnly(RetainedInputs.Concat(previous).ToArray());
                var previousAudio = new List<FilePin> {
                    new("audio/manifest.json", 230218, SourceAudioPlaybackAdmission.CompletePrematchManifestSha256)
                };
                foreach (FilePin added in files.Values.Where(pin => pin.Path.StartsWith("audio/team-speech/", StringComparison.Ordinal)))
                {
                    string oldPath = "audio/" + added.Path.Substring("audio/team-speech/".Length);
                    Require(files.TryGetValue(oldPath, out FilePin oldBank),
                        "A new qualified team bank cannot replace or remove its sealed ancestor: " + oldPath);
                    previousAudio.Add(oldBank);
                }
                Require(previousAudio.Count == 32, "Retain the original prematch manifest and all31 inherited team banks.");
                foreach (FilePin pin in previousAudio)
                    Require(files.TryGetValue(pin.Path, out FilePin actual) && actual == pin &&
                        !entrypoints.ContainsValue(pin.Path),
                        "Inherited audio must remain byte-identical and inactive: " + pin.Path);
                RetainedInputs = Array.AsReadOnly(RetainedInputs.Concat(previousAudio).ToArray());
            }
            if (twoStationRecords)
            {
                var previousSettings = new FilePin("settings-presnap-switch.json", 616,
                    "dd067923f88c23c0d6a7c3a7c0b71d04bd6d90f9b758db147c286ae5e281f37e");
                Require(files.TryGetValue(previousSettings.Path, out FilePin actual) && actual == previousSettings &&
                    !entrypoints.ContainsValue(previousSettings.Path),
                    "The inherited FourStation settings must remain byte-identical and inactive in the new TwoStation profile.");
                RetainedInputs = Array.AsReadOnly(RetainedInputs.Concat(new[] { previousSettings }).ToArray());
            }
        }
    }

    private void RequireCompleteAv306Ancestor(string additionalSettings = null)
    {
        foreach (FilePin pin in CompleteAv306AddedInputs)
            Require(files.TryGetValue(pin.Path, out FilePin actual) && actual == pin,
                "CompleteAv306 requires exactly its authenticated added input: " + pin.Path);
        var added = new HashSet<string>(CompleteAv306AddedInputs.Select(pin => pin.Path), StringComparer.Ordinal);
        added.Add("audio/complete-av306-manifest.json");
        foreach (string path in files.Keys.Where(path => path.StartsWith("audio/team-speech/", StringComparison.Ordinal) &&
            path.EndsWith(".saud", StringComparison.Ordinal))) added.Add(path);
        Require(added.Count == 39 && InventoryHash(files.Values.Where(pin => added.Contains(pin.Path))) ==
            CompleteAv306AddedInventorySha256,
            "The306 successor requires exactly its39 authenticated added animation/course/audio input pins.");
        FilePin[] inherited = files.Values.Where(pin => !added.Contains(pin.Path) && pin.Path != additionalSettings)
            .OrderBy(pin => pin.Path, StringComparer.Ordinal).ToArray();
        Require(inherited.Length == 1114 && inherited.Sum(pin => (long)pin.Bytes) == 262649366 &&
            InventoryHash(inherited) == CompleteAv306AncestorInventorySha256,
            "The306 successor must retain all1114 authenticated ancestor file pins unchanged.");
    }

    private static string InventoryHash(IEnumerable<FilePin> pins)
    {
        var inventory = new StringBuilder();
        foreach (FilePin pin in pins.OrderBy(pin => pin.Path, StringComparer.Ordinal))
            inventory.Append(pin.Path).Append('\t').Append(pin.Bytes.ToString(System.Globalization.CultureInfo.InvariantCulture))
                .Append('\t').Append(pin.Sha256).Append('\n');
        return Hash(Encoding.UTF8.GetBytes(inventory.ToString()));
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
