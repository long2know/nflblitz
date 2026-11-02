using Blitz.AssetImport;
using Blitz.Contracts;
using Blitz.Gameplay;
using System.Text.Json;
using OriginalPlayer = Blitz.AssetImport.SourcePlayer;

namespace Blitz.SourcePlayer;

public sealed class PlayerData
{
    public DataCatalog Catalog { get; }
    public SourceAnimationLibrary Animations { get; }
    public SourceTeamRosterLibrary Rosters { get; }
    public SourcePlaybook Playbook { get; }
    public SourceVector3[] ReferencePivots { get; }
    public OriginalPlayer ReferencePlayer { get; }
    public SourceMovementRuntimeData Movement { get; }
    public SourceMovementSettings MovementSettings { get; }
    public SourceInterfaceMovementBootstrap Bootstrap { get; }
    public SourceAudioFunctionData Sound { get; }
    public SourceC2CallbackData Callbacks { get; }
    public SourceInterfacePackageConfiguration Configuration { get; }
    public SourceTeamPresentationData Teams { get; }
    public SourceInterfaceData Hud { get; }
    public SourceDefaultAudibleData DefaultAudibles { get; }
    public SourceCinematicLibrary Impact { get; }
    public SourceNormalCameraData NormalCamera { get; }
    public SourcePossessionSoundData PossessionSound { get; }
    public SourceEnvironmentBundle World { get; }
    public SourceFireSpfxBundle Fire { get; }
    public SourceTeamPoppedHeadData PoppedHead { get; }
    public SourceFreeBallData FreeBall { get; }
    public SourceAudioPlaybackAdmission AudioAdmission { get; }
    public SourceOriginalPresentationData OriginalPresentation { get; }
    private readonly Dictionary<string, string> headers = new(StringComparer.Ordinal);

    public PlayerData(DataCatalog data, Action<string> progress = null,
        IReadOnlyList<SourceStationConfiguration> defaultAudibleStations = null,
        SourceCaptainSlot? defaultAudibleCaptain = null, SourceAudioPlaybackAdmission decodedAudio = null)
    {
        Catalog = data ?? throw new ArgumentNullException(nameof(data));
        AudioAdmission = decodedAudio;
        if (data.Profile.IsCompleteOriginal303)
        {
            DataCatalog.Require(decodedAudio == null,
                "CompleteOriginal303 derives audio only from its exact pinned original data, not a caller-declared replacement admission.");
            AudioAdmission = ReadCompleteOriginalAudio(data);
        }
        data.VerifyRetainedInputs();
        if (defaultAudibleStations != null || defaultAudibleCaptain.HasValue)
        {
            DataCatalog.Require(data.Profile.UsesDefaultAudibles,
                "Typed audible station inputs require the explicit default-audibles profile.");
            SourceInterfacePackageConfiguration loaded = SourceInterfacePackagePlan.ReadConfiguration(
                data.EntryBytes("settings"), data.EntryHash("settings"));
            Configuration = ConfigureDefaultAudibleStations(loaded, defaultAudibleStations, defaultAudibleCaptain);
        }
        progress?.Invoke("Verifying " + data.Profile.StableId + " animation/pose library");
        Animations = SourceAnimationLibrary.Read(data.EntryBytes("animations"), path => data.Read("animation/" + path));
        data.Profile.RequireLibrary(Animations);
        progress?.Invoke("Loading original roster, rules, input, sound-selection and movement services");
        Rosters = SourceTeamRosterLibrary.Read(data.EntryBytes("rosters"), SourceTeamPresentationData.PinnedRosterSha256);
        byte[] playbookBytes = data.EntryBytes("playbook");
        if (data.Profile.UsesDefaultAudibles)
            Playbook = SourceInterfaceData.ReadDefaultAudiblePlaybook(playbookBytes);
        else
            using (var reader = new StringReader(DataCatalog.Text(playbookBytes)))
                Playbook = SourcePlaybook.LoadConverted(reader);
        DataCatalog.Require(data.EntryHash("playbook") == SourceInterfacePackagePlan.PlaybookSha256,
            "The exact actual original production playbook is required.");
        if (Configuration == null)
            Configuration = SourceInterfacePackagePlan.ReadConfiguration(data.EntryBytes("settings"), data.EntryHash("settings"));
        Bootstrap = SourceInterfaceMovementBootstrap.Read(data.EntryBytes("bootstrap"), data.EntryHash("bootstrap"));
        MovementSettings = new SourceMovementSettings(Bootstrap.FieldIndex, Bootstrap.InfiniteTurboTeamMask,
            Bootstrap.SpeedPowerup, Bootstrap.FastTurboTeamMask, Bootstrap.RunOutOfBoundsTeamMask, Bootstrap.ViolencePowerup);
        var movement = SourceInterfaceMovementData.Read(data.EntryBytes("movement"), SourceInterfaceMovementData.ContentSha256);
        Movement = new SourceMovementRuntimeData(new SourceMovementData(movement.Constants, movement.Tables),
            movement.Friction, movement.Defense.Select(row => new SourceMovementDefenseRow(row.Line, row.Back,
                row.Base, row.Range, row.Plow)).ToArray());
        Sound = SourceInterfacePackagePlan.ReadSoundVmFunctions(data.EntryBytes("sound"),
            SourceInterfacePackagePlan.SoundVmFunctionsSha256);
        Callbacks = data.Profile.IsCompleteOriginal303
            ? SourceInterfacePackagePlan.ReadCompleteOriginalCallbacks(data.EntryBytes("callbacks"), data.EntryBytes("hit"))
            : data.Profile.UsesPreSnapSwitchPayload
            ? SourceInterfacePackagePlan.ReadPreSnapSwitchCallbacks(data.EntryBytes("callbacks"), data.EntryBytes("hit"))
            : SourceInterfacePackagePlan.ReadC2M1Callbacks(data.EntryBytes("callbacks"), data.EntryBytes("hit"));
        ReferencePlayer = GeneratedPlayerReader.Read(DataCatalog.Text(data.EntryBytes("model")), "models/plyr266.h",
            DataCatalog.Text(data.EntryBytes("skeleton")), "models/skeleton.h",
            new PlayerSymbolSelection("plyr266_limbs", "fbplyr_skel", "model266_limbs", "plyr266_textures"));
        var conversion = new PlayerCoordinateConversion(SignedSourceAxis.PositiveX, SignedSourceAxis.PositiveY,
            SignedSourceAxis.NegativeZ, SourceTriangleOrder.Renderer321);
        DataCatalog.Require(ReferencePlayer.Nodes.Count == SourceTiming.JointCount,
            "The actual original fifteen-joint rig is required.");
        ReferencePivots = new SourceVector3[SourceTiming.JointCount];
        for (int i = 0; i < ReferencePivots.Length; i++)
        {
            SourceRigNode node = ReferencePlayer.Nodes[i];
            DataCatalog.Require(node.Index == i && node.ParentIndex == SourceAnimationSkeleton.ParentIndices[i],
                "Original player joint/parent order changed.");
            AssetVector3 pivot = conversion.Convert(node.BindPivot);
            ReferencePivots[i] = new SourceVector3(pivot.X, pivot.Y, pivot.Z);
        }
        progress?.Invoke("Verifying original field/stadium, uniform/number atlas and HUD identities");
        Teams = SourceTeamPresentationData.Read(data.EntryBytes("teams"), data.EntryHash("teams"), Rosters);
        foreach (var input in Teams.Inputs.Values.Where(input => input.Path.StartsWith("models/", StringComparison.Ordinal)))
        {
            byte[] bytes = data.Read("teams/" + input.Path);
            Teams.VerifyInput(input.Path, bytes);
            headers.Add(input.Path, DataCatalog.Text(bytes));
        }
        Hud = SourceInterfaceData.Read(data.EntryBytes("hud"), SourceInterfacePackagePlan.HudSha256);
        if (data.Profile.UsesDefaultAudibles)
        {
            Hud.BindDefaultAudiblePresentation(Rosters, Bootstrap);
            DefaultAudibles = Hud.CreateDefaultAudibleData(Playbook);
        }
        Impact = SourceCinematicLibrary.Read(data.EntryBytes("impact"), data.Profile.IsCompleteOriginalFinal303
            ? SourceTeamResourceProfile.CompleteOriginalFinalImpactSha256 : data.Profile.IsCompleteOriginal303
            ? SourceTeamResourceProfile.CompleteOriginalImpactSha256 : data.Profile.UsesPreSnapSwitchPayload
                ? SourceTeamResourceProfile.PreSnapSwitchImpactSha256 : SourceTeamResourceProfile.C2M1ImpactSha256);
        data.Profile.RequireImpact(Impact);
        DataCatalog.Require(data.HasEntry("normalCamera"),
            "The explicit source profile requires pinned original normal/kickoff camera data; legacy EMA is not a fallback.");
        NormalCamera = SourceNormalCameraData.Read(data.EntryBytes("normalCamera"));
        DataCatalog.Require(data.HasEntry("possessionSound"),
            "Active CAMBOT messages require their pinned original interception-speech operands; no invented playlist.");
        PossessionSound = SourcePossessionSoundData.Read(data.EntryBytes("possessionSound"));
        World = SourceEnvironmentReader.Read(data.EntryBytes("world"), data.EntryHash("world"),
            path => data.Read("world/" + path));
        if (data.Profile.IsCompleteOriginal303)
        {
            progress?.Invoke("Verifying all four actual original shadow materials and source-selected cabinet rasters");
            OriginalPresentation = SourceOriginalPresentationData.Read(data.EntryBytes("originalPresentation"),
                path => data.Read("original/" + path));
        }
        if (data.HasEntry("fire"))
        {
            progress?.Invoke("Verifying original SDL/seventeen TRAIL authoring frames; portable not native WMS");
            Fire = SourceFireSpfxReader.Read(data.EntryBytes("fire"), data.EntryHash("fire"),
                path => data.Read("fire/" + path));
        }
        if (data.HasEntry("poppedHead"))
        {
            progress?.Invoke("Verifying original phead SDL source-authored geometry, raw texture4/HEDSHU slot1");
            PoppedHead = SourceTeamPoppedHeadData.Read(data.EntryBytes("poppedHead"), data.EntryHash("poppedHead"),
                path => data.Read("popped-head/" + path));
        }
        progress?.Invoke("Verifying original medball2 free-ball authoring origin and HEDSHU/HEDSHU2 decals");
        FreeBall = SourceFreeBallData.Read(data.EntryBytes("freeBall"), data.EntryHash("freeBall"),
            path => data.Read("free-ball/" + path));
        SourceFreeBallData.RequireTextures(Teams);
    }

    public string Header(string path) => headers.TryGetValue(path, out string text) ? text :
        throw new PlayerAssetException("Required verified original generated header is missing: " + path);

    private static SourceAudioPlaybackAdmission ReadCompleteOriginalAudio(DataCatalog catalog)
    {
        DataCatalog.Require(catalog.Profile.IsCompleteOriginal303 && catalog.Entry("originalAudio") == "audio/manifest.json" &&
            catalog.EntryHash("originalAudio") == (catalog.Profile.IsCompleteOriginalPrematch303 ?
                SourceAudioPlaybackAdmission.CompletePrematchManifestSha256 : catalog.Profile.UsesCompleteSourceBanks ?
                SourceAudioPlaybackAdmission.CompleteSourceBanksManifestSha256 : SourceAudioPlaybackAdmission.CompleteOriginalManifestSha256),
            "Only the explicit complete-original profile admits its exact executed original playlist package.");
        using var document = JsonDocument.Parse(catalog.EntryBytes("originalAudio"));
        JsonElement root = document.RootElement;
        bool prematch = catalog.Profile.IsCompleteOriginalPrematch303;
        DataCatalog.Require(root.GetProperty("schema").GetString() == (prematch ? "original-compiled-playlists/v3" : "original-compiled-playlists/v2") &&
            root.GetProperty("profile").GetString() == (prematch ? SourceTeamResourceProfile.CompleteOriginalPrematchStableId : catalog.Profile.UsesCompleteSourceBanks ?
                SourceTeamResourceProfile.CompleteOriginalSourceBanksStableId : catalog.Profile.StableId) &&
            root.GetProperty("policy").GetString() == (prematch ? "portable-dcs233-global-mix-programs/v3" : SourceAudioPlaybackAdmission.CompiledPolicy) &&
            root.GetProperty("sourceRevision").GetString() == SourceAnimationLibrary.SourceRevision &&
            root.GetProperty("decoderRevision").GetString() == "1b7d0126cabb79c4c1365a90542806a60c9436f8",
            "The frozen complete-original source/decoder/program generation changed.");
        if (prematch)
            DataCatalog.Require(root.GetProperty("nativeMixerReferenceSha256").GetString() ==
                SourceAudioMixerProgram.NativeReferenceSha256 &&
                DataCatalog.Hash(catalog.Read("audio/native-reference.json")) == SourceAudioMixerProgram.NativeReferenceSha256 &&
                root.GetProperty("implicitGlobalMixerPrograms").GetInt32() == 8,
                "The independently executed2.33 implicit-global mixer and finite00 reference differs.");
        var banks = new List<SourceAudioBankInput>();
        foreach (JsonElement entry in root.GetProperty("banks").EnumerateArray())
            banks.Add(new SourceAudioBankInput(catalog.Read("audio/" + DataCatalog.Portable(entry.GetProperty("path").GetString())),
                entry.GetProperty("sha256").GetString(), entry.GetProperty("bank").GetString(),
                entry.GetProperty("sourceGitBlob").GetString(), entry.GetProperty("sourceSha256").GetString()));
        var programs = root.GetProperty("programs").EnumerateArray().Select(entry =>
            new SourceAudioProgramRequirement(entry.GetProperty("bank").GetString(), entry.GetProperty("id").GetInt32())).ToArray();
        return new SourceAudioPlaybackAdmission(catalog.Profile.StableId,
            "original-completion-authorization-20261030.json; explicit local-only original integration",
            banks, programs, 35, compiledOriginalPrograms: true,
            initialBanks: new[] { "players", "generic", "tkperm", "cursors", "intro" });
    }

    public void ValidateCabinetConfiguration(SourceMatchConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        DataCatalog.Require(configuration.Profile == SourceSimulationProfile.OriginalRelease57Hz &&
            configuration.StationLayout == SourceStationLayout.FourStation && configuration.Stations.Count > 0,
            "Free-play sessions require actual OriginalRelease57Hz FourStation humans, not the incomplete TwoStation path.");
        foreach (SourceTeamSelection team in new[] { configuration.HomeTeam, configuration.AwayTeam })
        {
            SourceTeamRoster roster = Rosters.GetTeam(team.TeamIndex);
            DataCatalog.Require(roster.Players.Count == 16 && team.RosterIndices.Count == 7 &&
                team.RosterIndices.All(index => index >= 0 && index < roster.Players.Count),
                "Each cabinet side requires seven validated selections from its actual sixteen-record original roster.");
        }
        foreach (SourceStationConfiguration station in configuration.Stations)
            DataCatalog.Require(station.Team == (TeamId)(station.StationId / 2) &&
                station.InitialPlayerId == -1 && !station.Profile.IsSpecified,
                "Free-play physical0/1 are Home,2/3 are Away; source controller assignment is not a custom record substitute.");
    }

    private static SourceInterfacePackageConfiguration ConfigureDefaultAudibleStations(
        SourceInterfacePackageConfiguration loaded, IReadOnlyList<SourceStationConfiguration> stations,
        SourceCaptainSlot? captain)
    {
        SourceMatchConfiguration original = loaded.Match;
        DataCatalog.Require(original.Profile == SourceSimulationProfile.OriginalRelease57Hz &&
            original.StationLayout == SourceStationLayout.FourStation,
            "Typed audible stations retain the actual original57 FourStation settings.");
        IReadOnlyList<SourceStationConfiguration> selected = stations ?? original.Stations;
        bool home = false, away = false;
        var seen = new bool[4];
        foreach (SourceStationConfiguration station in selected)
        {
            DataCatalog.Require(station.IsSpecified && station.InitialPlayerId == -1 && !station.Profile.IsSpecified,
                "Typed audible stations require specified original physical entries and initial_player_id=-1/default source profile.");
            DataCatalog.Require(station.StationId >= 0 && station.StationId < 4 && !seen[station.StationId],
                "Typed audible stations must be specified and unique actual source IDs0..3.");
            DataCatalog.Require(station.Team == (TeamId)(station.StationId / 2),
                "Typed audible stations require source PLYRTEAM(station)==station/2:0/1Home,2/3Away.");
            seen[station.StationId] = true;
            home |= station.Team == TeamId.Home;
            away |= station.Team == TeamId.Away;
        }
        DataCatalog.Require(home && away, "Typed audible station entry requires an actual Home and Away human.");
        var effective = new SourceMatchConfiguration(original.Profile, original.StationLayout,
            original.HomeTeam, original.AwayTeam, selected, original.FirstPossession, captain ?? original.Captain,
            original.TimeAdjustment, original.Difficulty, original.ContinueAfterCabinetGate,
            original.HomePlay, original.AwayPlay);
        return new SourceInterfacePackageConfiguration(effective, loaded.Presentation, loaded.NativeGamepadsEnabled);
    }
}
