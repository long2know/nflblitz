using System.Text.Json;
using System.Text.Json.Nodes;
using Blitz.AssetImport;
using Blitz.Contracts;
using Blitz.Gameplay;
using Blitz.Presentation;
using Blitz.Runtime;
using Blitz.SourcePlayer;

internal static class DefaultAudiblesChecks
{
    private static int assertions;
    private static readonly List<object> negatives = new();

    private static void Check(bool condition, string identity)
    {
        assertions++;
        if (!condition) throw new InvalidOperationException("DEFAULT_AUDIBLES_ADAPTER: " + identity);
    }

    private static void Reject(string name, Action action, string field)
    {
        Exception? failure = null;
        try { action(); }
        catch (Exception error) { failure = error; }
        Check(failure != null && failure.Message.Contains(field, StringComparison.Ordinal),
            name + " must reject the offending artifact/field: " + field);
        negatives.Add(new { name, field, error = failure!.Message });
    }

    internal static void RunSourceAdmission(byte[] manifest, Func<string, byte[]> read,
        byte[] genericFourPlayerSettings, string output)
    {
        assertions = 0;
        negatives.Clear();
        Check(DataCatalog.Hash(genericFourPlayerSettings) ==
            "a78aaa6144e80700550ff1204c393bda2c279200dbf9b5a4dad147d6c6b20777",
            "generic portable fixture remains unchanged and is not source-authored cabinet truth");
        var original = new PlayerData(new DataCatalog(manifest, read));
        object configuration = VerifyTypedConfiguration(original, manifest, read, genericFourPlayerSettings);
        var proof = new {
            schema = "source-default-audibles-source-admission-components/v1", status = "passed", assertions,
            negatives, configuration,
            sourceContract = "Authenticated GAME.H397 PLYRTEAM(station)==station/2:0/1Home,2/3Away; explicit override only.",
            playerHostConstructions = 0, playerHostAdvanceCalls = 0, sourceWakes = 0,
            activeCallerOrWorldQualification = false, all16WholeNative = "OPEN"
        };
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "source-admission-components.json"), JsonSerializer.Serialize(proof));
        Console.WriteLine(JsonSerializer.Serialize(new { proof.schema, proof.status, proof.assertions,
            negatives = negatives.Count, proof.playerHostConstructions, proof.playerHostAdvanceCalls, proof.sourceWakes }));
    }

    internal static void RunHudAndConfiguration(byte[] manifest, Func<string, byte[]> read,
        byte[] fourPlayerSettings, string output)
    {
        assertions = 0;
        negatives.Clear();
        Check(DataCatalog.Hash(fourPlayerSettings) ==
            "a78aaa6144e80700550ff1204c393bda2c279200dbf9b5a4dad147d6c6b20777",
            "existing four-player settings must remain the exact frozen input");
        var remaining = JsonNode.Parse(manifest)!["files"]!.AsArray()
            .Select(pin => pin!["path"]!.GetValue<string>()).ToHashSet(StringComparer.Ordinal);
        var actualReads = new List<object>();
        byte[] ReadOnce(string path)
        {
            Check(remaining.Remove(path), "actual HUD component loader input read once: " + path);
            byte[] bytes = read(path);
            actualReads.Add(new { path, bytes = bytes.Length, sha256 = DataCatalog.Hash(bytes) });
            return bytes;
        }
        var catalog = new DataCatalog(manifest, ReadOnce);
        var data = new PlayerData(catalog);
        var sink = new Sink();
        var assets = new RenderAssets(data, sink);
        catalog.RequireAllInputsConsumed();
        Check(remaining.Count == 0 && catalog.ConsumedInputCount == 563 &&
            catalog.ActiveInputCount == 559 && catalog.VerifiedRetainedInputCount == 4,
            "real RenderAssets and PlayerData must consume the unchanged563-file bundle");
        var original = sink.HudUploads["hud/interface"];
        var chroma = sink.HudUploads["hud/interface-chroma"];
        var white = sink.HudUploads["hud/interface-white"];
        Check(original.Width == chroma.Width && original.Height == chroma.Height &&
            original.Mips == chroma.Mips && original.Repeat == chroma.Repeat &&
            original.Linear == chroma.Linear && !chroma.Linear && !chroma.Repeat &&
            chroma.SourceHash == chroma.RgbaHash &&
            chroma.RgbaHash == "25f6779bfec368e58663409d2c21a4f44d0388a67c917b9a5d7acfd18714e37c",
            "actual production colored-chroma registration retains exact authored dimensions/filter/hash");
        Check(white.RgbaHash == "dfc0720bc4211e7128c740095e1795b0b2dd8862865deefe0824812f7fa9f297",
            "existing white-flash RGBA output remains byte-identical");
        bool rgbPreserved = true, alphaExact = true, whiteExact = true;
        for (int i = 0; i < original.Rgba.Length; i += 4)
        {
            bool black = (original.Rgba[i] | original.Rgba[i + 1] | original.Rgba[i + 2]) == 0;
            rgbPreserved &= chroma.Rgba[i] == original.Rgba[i] &&
                chroma.Rgba[i + 1] == original.Rgba[i + 1] && chroma.Rgba[i + 2] == original.Rgba[i + 2];
            byte alpha = black ? (byte)0 : original.Rgba[i + 3];
            alphaExact &= chroma.Rgba[i + 3] == alpha;
            whiteExact &= white.Rgba[i] == 255 && white.Rgba[i + 1] == 255 &&
                white.Rgba[i + 2] == 255 && white.Rgba[i + 3] == alpha;
        }
        Check(rgbPreserved && alphaExact && whiteExact,
            "every actual atlas RGB/nonblack-alpha byte survives; only exact-black alpha is removed");
        int blackIndex = (928 * original.Width + 191) * 4;
        int colorIndex = (930 * original.Width + 191) * 4;
        Check(original.Rgba.Skip(blackIndex).Take(4).SequenceEqual(new byte[] { 0, 0, 0, 16 }) &&
            chroma.Rgba.Skip(blackIndex).Take(4).SequenceEqual(new byte[] { 0, 0, 0, 0 }) &&
            chroma.Rgba.Skip(colorIndex).Take(4).SequenceEqual(new byte[] { 88, 53, 196, 255 }),
            "reported T3 black/purple counterexamples reach the real registered colored-chroma bytes");
        byte[] tiny = { 0, 0, 0, 16, 1, 0, 0, 31, 88, 53, 196, 255 };
        byte[] held = (byte[])tiny.Clone(), keyed = SourceHudTextureMask.BlackChromaKey(tiny);
        tiny[4] = 99;
        Check(keyed.SequenceEqual(new byte[] { 0, 0, 0, 0, 1, 0, 0, 31, 88, 53, 196, 255 }) &&
            held[4] == 1, "pure shared chroma helper detaches bytes and does not key near-black colors");
        Reject("chroma-incomplete-RGBA", () => SourceHudTextureMask.BlackChromaKey(new byte[3]),
            "actual authored RGBA");

        var composer = new SourceHudComposer(data.Hud);
        var viewport = new SourceHudViewport(512, 384, 512, 384, 1);
        var draws = new List<object>();
        foreach (var mode in new[] { (Combine: 4, Chroma: 1, Texture: "interface-chroma"),
            (Combine: 4, Chroma: 0, Texture: "interface"), (Combine: 16, Chroma: 1, Texture: "interface-white") })
        {
            SourceMatchView match = ConstructHudUnit(data, mode.Combine, mode.Chroma);
            var draw = composer.ComposeAudibles(match, viewport);
            SourceHudPrimitive background = draw.Single(p => p.AudibleSprite != null);
            Check(ReferenceEquals(background.Audibles, match.Audibles) &&
                ReferenceEquals(background.AudibleSprite, match.Audibles.Sprites[0]) &&
                background.AudibleSprite.ObjectSerial == 38 && background.AudibleSprite.ObjectId == 813 &&
                background.AudibleSprite.ParentProcessSerial == 6 &&
                background.AudibleSprite.ColorCombinerFunction == mode.Combine &&
                background.AudibleSprite.ChromaKeyMode == mode.Chroma &&
                background.AudibleSprite.ConstantColor == 0xffffffffu &&
                background.Texture == mode.Texture && background.Color == 0xffffffffu &&
                background.SourceDepth == 1.4f && background.Bounds.X == 12 && background.Bounds.Y == 145,
                "constructed passive unit retains actual reported4/1 identity/state/geometry without substitution");
            double[] vertices = draw.SelectMany(p => p.Vertices).SelectMany(v => new[] { v.X, v.Y, v.S, v.T }).ToArray();
            sink.Present(Array.Empty<double>(), Array.Empty<double>(), Array.Empty<int>(), Array.Empty<int>(),
                Array.Empty<double>(), vertices, draw.Select(p => unchecked((int)p.Color)).ToArray(),
                draw.Select(p => "hud/" + p.Texture).ToArray(), draw.Select(p => p.Vertices.Count).ToArray(), "{}");
            int packet = draw.ToList().IndexOf(background);
            Check(sink.LastHudTextures[packet] == "hud/" + mode.Texture &&
                sink.LastHudColors[packet] == unchecked((int)background.Color) &&
                sink.LastHudVertices.SequenceEqual(vertices) &&
                sink.HudUploads.ContainsKey(sink.LastHudTextures[packet]),
                "non-noop recording sink consumes the real emitted HUD packet and production texture binding");
            draws.Add(new { mode.Combine, mode.Chroma, background.Texture, background.Color,
                background.SourceDepth, background.Bounds, sprite = background.AudibleSprite,
                sameView = ReferenceEquals(background.Audibles, match.Audibles),
                textureSha256 = sink.HudUploads[sink.LastHudTextures[packet]].RgbaHash,
                packetVerticesSha256 = DataCatalog.Hash(System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(vertices))),
                boundary = "Constructed passive view/actual asset+sink packet unit; NOT a real Core caller or PlayerHost render." });
        }
        SourceMatchView nonwhite = ConstructHudUnit(data, 4, 1, 0xff123456u);
        SourceHudPrimitive independent = composer.ComposeAudibles(nonwhite, viewport).Single(p => p.AudibleSprite != null);
        Check(independent.Texture == "interface-chroma" && independent.Color == 0xffffffffu &&
            independent.AudibleSprite.ConstantColor == 0xff123456u &&
            ReferenceEquals(independent.Audibles, nonwhite.Audibles),
            "DECAL/chroma mapping remains independent of source constant-color/tint");

        object configProof = VerifyTypedConfiguration(data, manifest, read, fourPlayerSettings);
        var proof = new {
            schema = "source-default-audibles-hud-config-components/v1", status = "passed", assertions,
            negatives, actualReads, files = 563, active = 559, retained = 4,
            profile = catalog.Profile.StableId, catalog.ManifestSha256,
            productionTextureBindings = sink.HudUploads.ToDictionary(pair => pair.Key, pair => new {
                pair.Value.Width, pair.Value.Height, pair.Value.Mips, pair.Value.Repeat, pair.Value.Linear,
                pair.Value.SourceHash, pair.Value.RgbaHash
            }),
            actualRenderAssetsWorldBindings = sink.WorldDraws, hudPacketPresentCalls = sink.HudPresentCalls,
            constructedPassiveUnits = draws, configProof,
            playerHostAdvanceCalls = 0, sourceWakes = 0, actualRawHostEvidence = false,
            nativeUnityBrowserOrEndToEndQualified = false
        };
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "hud-config-components.json"), JsonSerializer.Serialize(proof));
        Console.WriteLine(JsonSerializer.Serialize(new { proof.schema, proof.status, proof.assertions,
            negatives = negatives.Count, proof.files, proof.active, proof.retained,
            proof.hudPacketPresentCalls, proof.playerHostAdvanceCalls, proof.sourceWakes }));
    }

    private static SourceMatchView ConstructHudUnit(PlayerData data, int combiner, int chroma, uint constant = 0xffffffffu)
    {
        SourceMatchConfiguration config = data.Configuration.Match;
        var game = new SourceProcessView(1, 0x10, "game_proc", 0, 0, 1, 628);
        var sprite = new SourceAudibleSpriteView(38, TeamId.Home, 813, 6, "aud_menu",
            new SourceVector3(70, 192, 1.4f), SourceSpriteListMembership.NonAlpha, true, constant, combiner, chroma);
        var offense = new SourcePlaySelection(data.DefaultAudibles.OffensePages[0][3].Identity, SourcePlaybookKind.Offense);
        var defense = new SourcePlaySelection(data.DefaultAudibles.DefensePages[0][0].Identity, SourcePlaybookKind.Defense);
        var view = new SourceAudibleView(628,
            new SourceAudibleTeamView(TeamId.Home, -1, 1, true, 38),
            new SourceAudibleTeamView(TeamId.Away, 0, 0, false),
            new SourceAudibleRoleView(SourceAudibleRole.Offense, TeamId.Away, -1, 0, false, offense, offense),
            new SourceAudibleRoleView(SourceAudibleRole.Defense, TeamId.Home, 0, 1, true, defense, defense),
            false, false, game, SourceAudibleRestartStage.None, Array.Empty<SourceDigitalSwitchScan>(),
            new[] { sprite }, Array.Empty<SourceAudibleFlashView>(), Array.Empty<SourceDirectSoundCall>());
        return ConfigMatch(config, view);
    }

    private static SourceMatchView ConfigMatch(SourceMatchConfiguration config, SourceAudibleView? view = null)
        => new SourceMatchView(config, SourcePlayKind.Normal, SourceMatchPhase.PreSnap,
            new SourcePeriodView(0, SourcePeriodStatus.NotExpired),
            new SourcePossessionView(TeamId.Away, TeamId.Away, TeamId.Home, TeamId.Away, config.Captain),
            Array.Empty<SourceQuarterScore>(),
            config.Stations.Select(s => new SourceStationView(s.StationId, -1, 1)).ToArray(), audibles: view);

    private static object CopiedConfiguration(SourceInterfacePackageConfiguration config)
        => new { config.Match.Profile, config.Match.StationLayout,
            home = new { config.Match.HomeTeam.TeamIndex, config.Match.HomeTeam.RosterIndices },
            away = new { config.Match.AwayTeam.TeamIndex, config.Match.AwayTeam.RosterIndices },
            config.Match.FirstPossession, config.Match.TimeAdjustment, config.Match.Difficulty,
            config.Match.ContinueAfterCabinetGate, config.Match.HomePlay, config.Match.AwayPlay,
            config.Presentation, config.NativeGamepadsEnabled };

    private static object VerifyTypedConfiguration(PlayerData original, byte[] manifest,
        Func<string, byte[]> read, byte[] fourPlayerSettings)
    {
        var explicitNull = new PlayerData(new DataCatalog(manifest, read),
            defaultAudibleStations: null, defaultAudibleCaptain: null);
        Check(JsonSerializer.Serialize(explicitNull.Configuration) == JsonSerializer.Serialize(original.Configuration) &&
            original.Configuration.Match.Stations.Count == 1 &&
            original.Configuration.Match.Stations[0].StationId == 0 &&
            original.Configuration.Match.Stations[0].Team == TeamId.Home,
            "omitted/null typed inputs retain identical canonical one-station defaults and fields");
        var four = SourceInterfacePackagePlan.ReadConfiguration(fourPlayerSettings, DataCatalog.Hash(fourPlayerSettings));
        var sourceStations = new[] {
            new SourceStationConfiguration(0, TeamId.Home),
            new SourceStationConfiguration(1, TeamId.Home),
            new SourceStationConfiguration(2, TeamId.Away),
            new SourceStationConfiguration(3, TeamId.Away)
        };
        SourceStationConfiguration[] requested = (SourceStationConfiguration[])sourceStations.Clone();
        var configured = new PlayerData(new DataCatalog(manifest, read),
            defaultAudibleStations: requested, defaultAudibleCaptain: SourceCaptainSlot.First);
        Check(JsonSerializer.Serialize(CopiedConfiguration(configured.Configuration)) ==
            JsonSerializer.Serialize(CopiedConfiguration(original.Configuration)) &&
            configured.Configuration.Match.Stations.SequenceEqual(sourceStations) &&
            configured.Configuration.Match.Captain == SourceCaptainSlot.First,
            "typed entry changes ONLY explicit stations/captain; every other source/presentation field is copied");
        requested[0] = default;
        Check(configured.Configuration.Match.Stations[0].IsSpecified &&
            configured.Configuration.Match.Stations[0].StationId == 0 &&
            configured.Configuration.Match.Stations.All(s => s.InitialPlayerId == -1),
            "effective typed station list is immutable and never invents an initial controlled actor");
        SourceMatchView match = ConfigMatch(configured.Configuration.Match);
        Check(SourceCabinetStations.Captain(match, TeamId.Home) == 0 &&
            SourceCabinetStations.Captain(match, TeamId.Away) == 2 &&
            SourceCabinetStations.Team(match, 0) == TeamId.Home &&
            SourceCabinetStations.Team(match, 1) == TeamId.Home &&
            SourceCabinetStations.Team(match, 2) == TeamId.Away,
            "source-authored grouped construction and First captain contract use original physical IDs unchanged");
        var second = new PlayerData(new DataCatalog(manifest, read),
            defaultAudibleStations: sourceStations, defaultAudibleCaptain: SourceCaptainSlot.Second);
        SourceMatchView secondMatch = ConfigMatch(second.Configuration.Match);
        Check(SourceCabinetStations.Captain(secondMatch, TeamId.Home) == 1 &&
            SourceCabinetStations.Captain(secondMatch, TeamId.Away) == 3,
            "existing Second caption slot remains legal on the source-grouped contract");
        SourceMatchView genericMatch = ConfigMatch(four.Match);
        Check(SourceCabinetStations.Captain(genericMatch, TeamId.Home) == 0 &&
            SourceCabinetStations.Captain(genericMatch, TeamId.Away) == 1 &&
            four.Match.Stations[1].Team == TeamId.Away && four.Match.Stations[2].Team == TeamId.Home,
            "generic portable reader/captain fixture retains alternating metadata without remapping");
        var quantizer = new SourceCabinetQuantizer(configured.Hud.Input);
        var buffer = new SourceCabinetInputBuffer(configured.Configuration.Match, preserveSourceDigitalChanges: true);
        buffer.Capture(sourceStations.Select(s => new SourceCabinetSample(s.StationId,
            quantizer.SevenLevel(0, 0), (SourceButtons)(s.StationId + 1))).ToArray());
        SourceInputFrame captured = buffer.NextHostFrame();
        Check(captured.Stations.Select(s => s.StationId).SequenceEqual(new[] { 0, 1, 2, 3 }) &&
            captured.Stations.All(s => s.Pressed == (SourceButtons)(s.StationId + 1)),
            "actual quantizer/buffer receives all typed physical station inputs without a host advance");
        int factories = 0;
        ISourceRandom Sentinel(int seed) { factories++; throw new InvalidOperationException("config-unit-rng"); }
        void Invalid(string name, IReadOnlyList<SourceStationConfiguration>? stations, SourceCaptainSlot? captain, string field)
        {
            factories = 0;
            Reject(name, () => {
                var candidate = new PlayerData(new DataCatalog(manifest, read),
                    defaultAudibleStations: stations, defaultAudibleCaptain: captain);
                KernelBinding.Create(candidate, randomFactory: Sentinel);
            }, field);
            Check(factories == 0, name + " must reject before simulation/RNG construction");
        }
        Invalid("typed-config-empty", Array.Empty<SourceStationConfiguration>(), null, "Home and Away");
        Invalid("typed-config-missing-away", new[] { sourceStations[0] }, null, "Home and Away");
        Invalid("typed-config-missing-home", new[] { sourceStations[2] }, null, "Home and Away");
        Invalid("typed-config-captain-only-single-human", null, SourceCaptainSlot.Second, "Home and Away");
        Invalid("typed-config-unspecified-station", new[] { default(SourceStationConfiguration), sourceStations[2] },
            null, "specified original physical");
        Invalid("typed-config-forced-actor", new[] { new SourceStationConfiguration(0, TeamId.Home, 0),
            sourceStations[2] }, null, "initial_player_id=-1");
        Invalid("typed-config-duplicate-physical", new[] { sourceStations[0], sourceStations[2],
            sourceStations[0] }, null, "specified and unique");
        Invalid("typed-config-invalid-captain", sourceStations, (SourceCaptainSlot)2, "captain");
        Invalid("typed-config-too-many", sourceStations.Concat(new[] { sourceStations[0] }).ToArray(),
            null, "specified and unique");
        Invalid("typed-config-generic-alternating-not-source", four.Match.Stations, four.Match.Captain,
            "source PLYRTEAM(station)==station/2");
        Invalid("typed-config-opposite-physical-team", new[] {
            new SourceStationConfiguration(0, TeamId.Away), sourceStations[1]
        }, SourceCaptainSlot.First, "source PLYRTEAM(station)==station/2");
        Invalid("typed-config-nondefault-roster-profile", new[] {
            new SourceStationConfiguration(0, TeamId.Home, profile:
                new SourceRosterIdentity(original.Configuration.Match.HomeTeam.TeamIndex, 0)),
            sourceStations[2]
        }, SourceCaptainSlot.First, "initial_player_id=-1");
        JsonObject oldDocument = JsonNode.Parse(manifest)!.AsObject();
        oldDocument["profile"] = SourceTeamResourceProfile.PreSnapSwitchStableId;
        oldDocument.Remove("featureContract"); oldDocument.Remove("factory");
        byte[] legacyManifest = JsonSerializer.SerializeToUtf8Bytes(oldDocument);
        var legacy = new PlayerData(new DataCatalog(legacyManifest, read));
        var legacyNull = new PlayerData(new DataCatalog(legacyManifest, read),
            defaultAudibleStations: null, defaultAudibleCaptain: null);
        Check(JsonSerializer.Serialize(legacy.Configuration) == JsonSerializer.Serialize(legacyNull.Configuration),
            "legacy inactive null/null constructor path keeps its loaded canonical configuration unchanged");
        factories = 0;
        Reject("typed-config-old-profile", () => {
            var bad = new PlayerData(new DataCatalog(legacyManifest, read),
                defaultAudibleStations: four.Match.Stations, defaultAudibleCaptain: four.Match.Captain);
            KernelBinding.Create(bad, randomFactory: Sentinel);
        }, "explicit default-audibles profile");
        Check(factories == 0, "old-profile typed entry fails before any RNG factory");
        return new { canonical = original.Configuration, effective = configured.Configuration,
            captainHome = SourceCabinetStations.Captain(match, TeamId.Home),
            captainAway = SourceCabinetStations.Captain(match, TeamId.Away),
            selectedSettings = "Source-authored immutable station objects from authenticated GAME.H397; generic fixture retained/rejected only on new override.",
            settingsSha256 = DataCatalog.Hash(fourPlayerSettings),
            inputStations = captured.Stations.Select(s => s.StationId).ToArray(),
            physicalRuntimeMappingQualified = false,
            mappingBoundary = "Source grouping is confirmed and constructor-admitted. No actual caller/control rebuild/World is executed in this component selector. Generic fixture/older profiles remain generic.",
            simulatedOrHostAdvanced = false };
    }

    internal static void Run(byte[] manifest, Func<string, byte[]> read, string? output)
    {
        assertions = 0;
        negatives.Clear();
        var paths = JsonNode.Parse(manifest)!["files"]!.AsArray()
            .Select(pin => pin!["path"]!.GetValue<string>()).ToHashSet(StringComparer.Ordinal);
        var reads = new List<object>();
        byte[] ReadOnce(string path)
        {
            Check(paths.Remove(path), "actual one-shot loader input must be consumed once: " + path);
            byte[] bytes = read(path);
            reads.Add(new { path, bytes = bytes.Length, sha256 = DataCatalog.Hash(bytes) });
            return bytes;
        }
        var catalog = new DataCatalog(manifest, ReadOnce);
        Check(catalog.Profile.IsContact282C2M1DefaultAudibles &&
            !catalog.Profile.IsContact282C2M1PreSnapSwitch && catalog.Profile.UsesPreSnapSwitchPayload &&
            catalog.FeatureContract == SourceAudibleView.Contract &&
            catalog.Factory == KernelBinding.DefaultAudiblesFactory,
            "new exact profile/feature/factory must not relabel the old exact predicate");
        var data = new PlayerData(catalog);
        var sink = new Sink();
        var host = new PlayerHost(data, sink);
        catalog.RequireAllInputsConsumed();
        Check(paths.Count == 0 && catalog.FileCount == 563 && catalog.ActiveInputCount == 559 &&
            catalog.VerifiedRetainedInputCount == 4 && catalog.ConsumedInputCount == 563,
            "actual PlayerData/RenderAssets loaders must consume559 active plus4 sealed inputs");
        host.Advance(0, 512, 384, 512, 384, 1);
        Check(host.Game.SourceDefaultAudiblesEnabled && host.Game.SourceDefensivePreSnapSwitchEnabled &&
            sink.State.GetProperty("kernel").GetProperty("factory").GetString() == KernelBinding.DefaultAudiblesFactory &&
            sink.State.GetProperty("kernel").GetProperty("resourceProfile").GetString() == catalog.Profile.StableId,
            "the real catalog -> PlayerData -> PlayerHost -> kernel must describe audibles BEFORE inherited B");
        Check(host.Game.SourceTick == 0 && sink.WorldDraws > 0 && sink.Models == 32 &&
            sink.Joints.Length == 14 * 15 * 16 && sink.Joints.All(double.IsFinite) &&
            sink.State.GetProperty("actors").GetArrayLength() == 14 &&
            sink.State.GetProperty("actors").EnumerateArray().All(actor =>
                actor.GetProperty("worldRootMatrix").GetArrayLength() == 16 &&
                actor.GetProperty("modelScale").GetSingle() > 0 &&
                actor.GetProperty("tint").GetString() == "none"),
            "actual original World/roster/scale/fifteen-joint output at the constructor boundary, not a mock host");
        Check(ReferenceEquals(host.CommittedAudibles, host.HudProcess.CommittedHud.Match.Audibles) &&
            sink.State.GetProperty("audibles").GetProperty("consumerBoundBeforeAdvance").GetBoolean() &&
            host.DirectSoundDeliveries.Count == 0 && host.SourceDirectSoundCallsTaken == 0,
            "the real committed view and bound consumer are observable; empty output is NOT audible delivery proof");

        VerifyData(data, read);
        VerifyAdmission(data, read);
        VerifyInput(data, host);
        VerifyPassiveDraw(data, host);
        VerifyDirectConsumer();
        object[] manualOrderUnits = VerifySharedOrder();
        VerifyManifest(manifest, read);
        Check(PreviewOptions.FromUrl("").Profile == SourceTeamResourceProfile.C2M1StableId,
            "historical one-argument PreviewOptions default must remain M1");
        Reject("conflicting-built-profile", () => PreviewOptions.FromUrl(
            "?sourceprofile=" + SourceTeamResourceProfile.PreSnapSwitchStableId, catalog.Profile.StableId),
            "URL and built source profile differ");
        Reject("strict-successor-policy", () => PreviewOptions.FromUrl(
            "?source-compatibility=strict", catalog.Profile.StableId), "Portable");
        Reject("unknown-unpinned-input", () => catalog.Read("private/unlisted-adapter-input"), "unpinned asset");
        var proof = new {
            schema = "source-default-audibles-adapter-checks/v1", status = "passed", assertions,
            profile = catalog.Profile.StableId, catalog.ManifestSha256,
            factory = KernelBinding.FactoryFor(catalog.Profile), featureContract = catalog.FeatureContract,
            files = catalog.FileCount, active = catalog.ActiveInputCount, retained = catalog.VerifiedRetainedInputCount,
            actualReadReceipts = reads, negatives, actualHostFrame = sink.State,
            actualHostSourceTicksAdvanced = host.Game.SourceTick,
            actualHostDirectDeliveries = host.DirectSoundDeliveries.Count,
            isolatedUnits = new {
                manuallyConstructedPassiveViews = true, manuallyConstructedDirectConsumerCalls = true,
                manuallyConstructedVmOrdinalTokens = true, actualVmCallbackDeliveryProven = false,
                coreAudibleProducerOrWorldChangeProven = false
            },
            manualSharedOrderUnits = manualOrderUnits,
            pending = new[] {
                "Unity T3 actual raw caller/World/HUD/direct delivery and callback-order causal checks.",
                "Actual mixed hop/cursors consumer receipts from the newly frozen Core, not manual order tokens.",
                "Public authoring/browser/native/Quality qualification is not admitted by these units."
            },
            browserUnityNativeOrDspQualified = false
        };
        if (output != null)
        {
            Directory.CreateDirectory(output);
            File.WriteAllText(Path.Combine(output, "adapter-checks.json"), JsonSerializer.Serialize(proof));
        }
        Console.WriteLine(JsonSerializer.Serialize(new {
            proof.schema, proof.status, proof.assertions, proof.profile, proof.factory,
            proof.files, proof.active, proof.retained, negatives = negatives.Count,
            proof.actualHostSourceTicksAdvanced, proof.actualHostDirectDeliveries,
            integratedAudiblesQualified = false
        }));
    }

    private static void VerifyData(PlayerData data, Func<string, byte[]> read)
    {
        SourceDefaultAudibleData audible = data.DefaultAudibles;
        Check(ReferenceEquals(audible.Playbook, data.Playbook) &&
            audible.Digital8.SequenceEqual(data.Hud.Input.Digital8) &&
            audible.OffensePages.Count == 4 && audible.DefensePages.Count == 2 &&
            audible.TeamSpecials.Count == 31 && audible.TeamSpecials.All(row => row.Count == 3),
            "typed data must use the actual HUD49,4x9/2x9 pages and all31x3 team specials");
        for (int team = 0; team < 31; team++)
            for (int slot = 0; slot < 3; slot++)
                Check(ReferenceEquals(audible.TeamSpecials[team][slot], data.Playbook.GetPlay(
                    data.Hud.PageSlot(SourcePlaybookKind.Offense, 0, slot, team).PlaySymbol)),
                    "actual same-catalog special team=" + team + " slot=" + slot);
        foreach (SourcePlaybookKind kind in new[] { SourcePlaybookKind.Offense, SourcePlaybookKind.Defense })
            for (int page = 0; page < (kind == SourcePlaybookKind.Offense ? 4 : 2); page++)
                for (int slot = 0; slot < 9; slot++)
                {
                    SourcePlayDefinition play = audible.PagePlay(kind, page, slot, 0);
                    Check(ReferenceEquals(play, data.Playbook.GetPlay(data.Hud.PageSlot(kind, page, slot, 0).PlaySymbol)) &&
                        play.Routes.All(route => ReferenceEquals(route, data.Playbook.GetRoute(route.Identity))),
                        "actual page and route ownership " + kind + "/" + page + "/" + slot);
                }
        SourceDefaultAudibleSpecialPlays special = audible.SpecialPlays;
        SourcePlayDefinition[] contexts = { special.DefenseBefore85, special.FieldGoalOffense,
            special.FieldGoalDefense, special.FakeFieldGoalOffense, special.PuntOffense,
            special.FakePuntOffense, special.PuntReturnDefense, special.ExtraPointOffense };
        string[] tables = { "def_slots", "off_slots", "def_slots", "off_slots", "off_slots",
            "off_slots", "def_slots", "off_slots" };
        int[] indices = { 21, 38, 19, 39, 36, 37, 20, 40 };
        for (int i = 0; i < contexts.Length; i++)
            Check(ReferenceEquals(contexts[i], data.Playbook.GetPlay(data.Hud.PlaySlots[tables[i]][indices[i]].PlaySymbol)),
                "actual source context reference " + tables[i] + "/" + indices[i]);
        byte[] corrupt = read(data.Catalog.Entry("playbook"));
        corrupt[corrupt.Length / 2] ^= 1;
        Reject("changed-actual-catalog-bytes", () => SourceInterfaceData.ReadDefaultAudiblePlaybook(corrupt),
            "authenticated existing playbook bytes");
        var declared = new SourcePlaybook(data.Playbook.Revision, data.Playbook.Profile,
            data.Playbook.Plays.Values, data.Playbook.Routes.Values, data.Playbook.Pools.Values, data.Playbook.Directions);
        Reject("declared-catalog-identity-is-not-authentication", () => data.Hud.CreateDefaultAudibleData(declared),
            "SAME catalog loaded from authenticated bytes");
    }

    private sealed class Admission
    {
        internal ISourceAnimationLibrary? Animations;
        internal IReadOnlyList<SourceVector3>? Pivots;
        internal SourcePlaybook? Book;
        internal SourceMatchConfiguration? Configuration;
        internal ISourceTeamRoster? Rosters;
        internal SourcePresentationSettings? Presentation;
        internal SourceMovementRuntimeData? Movement;
        internal SourceMovementSettings? MovementSettings;
        internal SourceAudioFunctionData? Sound;
        internal SourceC2Bootstrap? Bootstrap;
        internal SourceCinematicLibrary? Impact;
        internal SourceDefaultAudibleData? Audible;
        internal SourceGameplayCompatibility Compatibility = SourceGameplayCompatibility.Portable;
        internal Admission(PlayerData data)
        {
            Animations = data.Animations; Pivots = data.ReferencePivots; Book = data.Playbook;
            Configuration = data.Configuration.Match; Rosters = data.Rosters; Presentation = data.Configuration.Presentation;
            Movement = data.Movement; MovementSettings = data.MovementSettings; Sound = data.Sound;
            Bootstrap = SourceC2Bootstrap.FromOriginalProgramBootAtSoundBanks(data.Callbacks);
            Impact = data.Impact; Audible = data.DefaultAudibles;
        }
        internal GameSimulation Create(Func<int, ISourceRandom> random)
            => SourceC2Composition.CreateDefaultAudiblesSimulation(Animations!, Pivots!, Book!, Configuration!,
                Rosters!, Presentation!, Movement!, MovementSettings!, Sound!, Bootstrap!,
                randomFactory: random, seed: 773, compatibility: Compatibility, defaultAudibles: Audible,
                impactLibrary: Impact);
    }

    private static void VerifyAdmission(PlayerData data, Func<string, byte[]> read)
    {
        int factories = 0, observedSeed = -1;
        ISourceRandom Sentinel(int seed)
        {
            factories++; observedSeed = seed;
            throw new InvalidOperationException("adapter-seed-forward:" + seed);
        }
        void Negative(string name, Action<Admission> mutate, string field)
        {
            var input = new Admission(data);
            mutate(input);
            factories = 0;
            Reject(name, () => input.Create(Sentinel), field);
            Check(factories == 0, name + " must reject BEFORE the RNG factory/constructor draws");
        }
        Negative("null-typed-data", i => i.Audible = null, "typed data produced");
        Negative("strict-policy", i => i.Compatibility = SourceGameplayCompatibility.StrictSource, "Portable");
        Negative("missing-library", i => i.Animations = null, "actual typed original animation library");
        Negative("missing-impact", i => i.Impact = null, "library");
        Negative("missing-configuration", i => i.Configuration = null, "explicit complete kernel inputs");
        Negative("missing-pivots", i => i.Pivots = null, "explicit complete kernel inputs");
        Negative("incomplete-pivots", i => i.Pivots = data.ReferencePivots.Take(14).ToArray(), "explicit complete kernel inputs");
        Negative("missing-rosters", i => i.Rosters = null, "explicit complete kernel inputs");
        Negative("missing-presentation", i => i.Presentation = null, "explicit complete kernel inputs");
        Negative("missing-movement", i => i.Movement = null, "explicit complete kernel inputs");
        Negative("missing-movement-settings", i => i.MovementSettings = null, "explicit complete kernel inputs");
        Negative("missing-bank-bootstrap", i => i.Bootstrap = null, "BEFORE constructor bank draws");
        Negative("wrong-SAFN", i => i.Sound = data.Callbacks.HitExtensionData, "unchanged legacy six-binding SAFN");
        var oldImpact = SourceCinematicLibrary.Read(read("kernel/impact-presentation.json"),
            SourceTeamResourceProfile.C2M1ImpactSha256);
        Negative("wrong-actual-impact-generation", i => i.Impact = oldImpact, "rebound482-site catalog");
        var oldCallbacks = SourceInterfacePackagePlan.ReadC2M1Callbacks(read("kernel/c2-callback-data.json"),
            read(data.Catalog.Entry("hit")));
        Negative("wrong-actual-callback-generation", i => i.Bootstrap =
            SourceC2Bootstrap.FromOriginalProgramBootAtSoundBanks(oldCallbacks), "BEFORE constructor bank draws");
        var oldLibrary = SourceAnimationLibrary.Read(read("animation/export-inventory.json"),
            path => read("animation/" + path));
        Negative("wrong-actual-library-generation", i => i.Animations = oldLibrary, "exact 282-clip/26265-frame");
        SourcePlaybook otherBook = SourceInterfaceData.ReadDefaultAudiblePlaybook(read(data.Catalog.Entry("playbook")));
        Negative("different-authenticated-catalog-instance", i => i.Book = otherBook, "SAME authenticated HUD/catalog");
        SourceDefaultAudibleData original = data.DefaultAudibles;
        var untrusted = new SourceDefaultAudibleData(original.ResourceProfileId, original.FeatureContract,
            original.HudSha256, original.PlaybookSha256, original.Playbook, original.Digital8,
            original.OffensePages, original.DefensePages, original.TeamSpecials, original.SpecialPlays);
        Negative("declared-hashes-with-untrusted-arrays", i => i.Audible = untrusted, "declared hashes alone");
        SourceMatchConfiguration config = data.Configuration.Match;
        var incomplete = new SourceMatchConfiguration(config.Profile, SourceStationLayout.TwoStation,
            config.HomeTeam, config.AwayTeam, config.Stations, config.FirstPossession, config.Captain,
            config.TimeAdjustment, config.Difficulty, config.ContinueAfterCabinetGate, config.HomePlay, config.AwayPlay);
        Negative("incomplete-physical-TwoStation-controls", i => i.Configuration = incomplete, "plyr_control[1]==-1");
        factories = 0;
        Reject("constructor-seed-forwarding", () => new Admission(data).Create(Sentinel), "adapter-seed-forward:773");
        Check(factories == 1 && observedSeed == 773, "new factory must forward the immutable seed/factory once before construction");
        factories = 0;
        Reject("kernel-seed-forwarding", () => KernelBinding.Create(data, randomFactory: Sentinel, seed: 773),
            "adapter-seed-forward:773");
        Check(factories == 1 && observedSeed == 773, "real KernelBinding must forward the immutable seed/factory, not install a setter");
    }

    private static void VerifyInput(PlayerData data, PlayerHost host)
    {
        var quantizer = new SourceCabinetQuantizer(data.Hud.Input);
        SourceCabinetSample[] Samples(int y, SourceButtons buttons = SourceButtons.None)
            => data.Configuration.Match.Stations.Select(station =>
                new SourceCabinetSample(station.StationId, quantizer.SevenLevel(0, y), buttons)).ToArray();
        var old = new SourceCabinetInputBuffer(data.Configuration.Match);
        var raw = new SourceCabinetInputBuffer(data.Configuration.Match, preserveSourceDigitalChanges: true);
        old.Capture(Samples(-3)); old.Capture(Samples(3));
        raw.Capture(Samples(-3)); raw.Capture(Samples(3));
        Check(old.PendingFrames == 0 && raw.PendingFrames == 2,
            "ONLY explicit successor buffering queues ordered digital-only changes");
        SourceInputFrame up = raw.NextHostFrame(), down = raw.NextHostFrame();
        Check(up.Stations.All(s => s.DigitalCurrent == SourceDigitalDirections.Up && s.StickZ == 3) &&
            down.Stations.All(s => s.DigitalCurrent == SourceDigitalDirections.Down && s.StickZ == -3) &&
            up.Stations.All(s => s.Pressed == SourceButtons.None && s.Released == SourceButtons.None),
            "actual quantizer digital values and axes stay paired, without manufactured ABC/close edges");
        Check(raw.NextHostFrame().Stations.All(s => s.DigitalCurrent == SourceDigitalDirections.Down),
            "held copies retain actual nullable digital current");
        old.Clear(false);
        old.Capture(Samples(-3, SourceButtons.B)); old.Capture(Samples(3, SourceButtons.B));
        Check(old.PendingFrames == 1 && old.NextHostFrame().Stations.All(s => s.Pressed == SourceButtons.B) &&
            old.NextHostFrame().Stations.All(s => s.DigitalCurrent == SourceDigitalDirections.Down && s.Pressed == SourceButtons.None),
            "old ABC/command-only timing stays unchanged");
        raw.Clear();
        raw.Capture(Samples(-3, SourceButtons.C));
        Check(raw.WaitingForNeutral && raw.PendingFrames == 0 &&
            raw.NextHostFrame().Stations.All(s => s.DigitalCurrent == SourceDigitalDirections.None),
            "hard neutral/focus boundary clears pending digital changes and publishes observed neutral");
        raw.Capture(Samples(0)); raw.Capture(Samples(-3));
        Check(!raw.WaitingForNeutral && raw.PendingFrames == 1 &&
            raw.NextHostFrame().Stations.All(s => s.DigitalCurrent == SourceDigitalDirections.Up),
            "neutral rearming does not replay a held focus-edge backlog");
        var transform = new SourceCabinetPlaySelection(data.Hud);
        SourceInputFrame passed = transform.ApplyAtSourceTick(host.Game.State, host.Game.SourceTick, up);
        Check(ReferenceEquals(passed, up) && passed.Stations.All(s => s.DigitalCurrent == SourceDigitalDirections.Up),
            "actual non-selection/pre-snap Runtime transform passes the raw frame through without a second audible machine");
        Check(!new SourceStationInput(0).DigitalCurrent.HasValue,
            "direct-contract unavailable remains nullable, distinct from acquired neutral");
    }

    private static SourceMatchView WithSprite(PlayerData data, PlayerHost host, int combiner, int chroma,
        SourceSpriteListMembership membership = SourceSpriteListMembership.NonAlpha, bool visible = true)
    {
        SourceMatchView actual = ((ISourceMatchState)host.Game.State).Match;
        SourceProcessView game = ((ISourcePossessionState)host.Game.State).Processes.Single(p => p.Function == "game_proc");
        var sprite = new SourceAudibleSpriteView(1001, TeamId.Home, 1, game.Serial, "aud_menu",
            new SourceVector3(70, 192, 1.4f), membership, visible, 0xff123456u, combiner, chroma);
        var offense = new SourcePlaySelection(data.DefaultAudibles.OffensePages[0][3].Identity, SourcePlaybookKind.Offense);
        var defense = new SourcePlaySelection(data.DefaultAudibles.DefensePages[0][0].Identity, SourcePlaybookKind.Defense);
        TeamId off = actual.Possession.Offense, def = off == TeamId.Home ? TeamId.Away : TeamId.Home;
        var view = new SourceAudibleView(host.Game.SourceTick,
            new SourceAudibleTeamView(TeamId.Home, -1, 1, true, 1001),
            new SourceAudibleTeamView(TeamId.Away, 0, 0, false),
            new SourceAudibleRoleView(SourceAudibleRole.Offense, off, 0, 2, false, offense, offense),
            new SourceAudibleRoleView(SourceAudibleRole.Defense, def, -1, 2, false, defense, defense),
            false, false, game, SourceAudibleRestartStage.None, Array.Empty<SourceDigitalSwitchScan>(),
            new[] { sprite }, Array.Empty<SourceAudibleFlashView>(), Array.Empty<SourceDirectSoundCall>());
        return new SourceMatchView(actual.Configuration, actual.PlayKind, actual.Phase, actual.Period,
            actual.Possession, actual.QuarterScores, actual.Stations, homePlay: actual.HomePlay,
            awayPlay: actual.AwayPlay, offenseFlipped: actual.OffenseFlipped, audibles: view);
    }

    private static void VerifyPassiveDraw(PlayerData data, PlayerHost host)
    {
        var composer = new SourceHudComposer(data.Hud);
        var viewport = new SourceHudViewport(512, 384, 512, 384, 1);
        SourceMatchView normal = WithSprite(data, host, 4, 0), white = WithSprite(data, host, 0x10, 1);
        var colorDraw = composer.ComposeAudibles(normal, viewport);
        var whiteDraw = composer.ComposeAudibles(white, viewport);
        var color = colorDraw.Single(p => p.Element.EndsWith("/background", StringComparison.Ordinal));
        var flash = whiteDraw.Single(p => p.Element.EndsWith("/background", StringComparison.Ordinal));
        Check(ReferenceEquals(color.AudibleSprite, normal.Audibles.Sprites[0]) &&
            ReferenceEquals(flash.AudibleSprite, white.Audibles.Sprites[0]) &&
            color.AudibleSprite.ConstantColor == 0xff123456u && flash.AudibleSprite.ConstantColor == 0xff123456u &&
            color.Color == 0xffffffffu && flash.Color == 0xffffffffu &&
            color.Texture == "interface" && flash.Texture == "interface-white",
            "combineONE/chromaON must change the ACTUAL draw texture independently of constant color/tint");
        SourceInterfaceImage image = data.Hud.Image("aud_menu");
        double scale = Math.Min(512d / data.Bootstrap.HorizontalResolution, 384d / data.Bootstrap.VerticalResolution);
        Check(color.SourceImageSymbol == "aud_menu" && color.SourceDepth == 1.4f &&
            color.Bounds.X == (512 - data.Bootstrap.HorizontalResolution * scale) / 2 + (70 - image.AnchorX) * scale &&
            color.Bounds.Y == (384 - data.Bootstrap.VerticalResolution * scale) / 2 +
                (data.Bootstrap.VerticalResolution - 192 - image.AnchorY) * scale,
            "actual aud_menu authored anchors and source70/192/z1.4 must reach the draw");
        Check(colorDraw.All(p => ReferenceEquals(p.Audibles, normal.Audibles)) &&
            colorDraw.Where(p => p.AudibleSprite == null).All(p => p.SourceDepth == 1.2f && p.Texture == "interface") &&
            colorDraw.Where(p => p.Element.EndsWith("/team", StringComparison.Ordinal))
                .All(p => p.SourceImageSymbol.StartsWith("bast10", StringComparison.Ordinal) && p.Color == 0xffffff00u) &&
            colorDraw.Where(p => p.Element.EndsWith("/choice/2", StringComparison.Ordinal)).All(p => p.Color == 0xffffff00u) &&
            colorDraw.Where(p => p.Element.EndsWith("/original", StringComparison.Ordinal)).All(p => p.Color == 0xff808080u),
            "same committed view, full-name bast10, bast8t source rows/palette and selected choice; text never flashes");
        Check(colorDraw.Where(p => p.AudibleSprite == null).Select(p => (p.Element, p.SourceImageSymbol, p.Color))
            .SequenceEqual(whiteDraw.Where(p => p.AudibleSprite == null).Select(p => (p.Element, p.SourceImageSymbol, p.Color))),
            "only the borrowed menu background changes, not text or whole HUD");
        var hidden = WithSprite(data, host, 4, 0, visible: false);
        Check(hidden.Audibles.Sprites[0].Exists && !hidden.Audibles.Sprites[0].DrawVisible &&
            composer.ComposeAudibles(hidden, viewport).All(p => p.AudibleSprite == null),
            "draw visibility is not source list liveness");
        Check(composer.ComposeAudibles(WithSprite(data, host, 4, 0, SourceSpriteListMembership.Alpha), viewport)
            .Any(p => p.AudibleSprite != null), "both actual sprite lists admit the same source object");
        Reject("unsupported-combiner-chroma-not-normal-restore-guess", () =>
            composer.ComposeAudibles(WithSprite(data, host, 0x10, 0), viewport),
            "cannot represent audible combiner/chroma 16/0");
    }

    private static SourceDirectSoundCall Call(long sequence, int playlist = 100, long tick = 1, int order = 4,
        uint volume = 235, uint pan = 0, uint priority = 15, string bank = "cursors", long? outputSequence = null)
        => new SourceDirectSoundCall(sequence, tick, 1, "player_proc", order, 0, 0, TeamId.Home,
            "player.c", "audible_check", 11278, bank, playlist, volume, pan, priority, 0, outputSequence ?? sequence);

    private static void VerifyDirectConsumer()
    {
        var consumer = new SourceDirectSoundRecordingConsumer();
        SourceDirectSoundCall first = Call(1);
        consumer.Consume(first);
        consumer.Consume(Call(2, 101, 2));
        consumer.Consume(Call(3, 102, 3));
        Check(consumer.DeliveredCount == 3 && consumer.Receipts.Count == 3 &&
            ReferenceEquals(consumer.Receipts[0].Call, first) &&
            consumer.Receipts.Select(r => r.Call.PlaylistId).SequenceEqual(new[] { 100, 101, 102 }),
            "isolated real recording-consumer unit retains the exact original call objects and delivery order");
        Reject("direct-consumer-duplicate", () => consumer.Consume(Call(3, tick: 4)), "SOURCE_DIRECT_SOUND_ORDER");
        Reject("direct-consumer-gap", () => consumer.Consume(Call(5, tick: 4)), "SOURCE_DIRECT_SOUND_ORDER");
        Reject("direct-consumer-reversed-tick", () => consumer.Consume(Call(4)), "SOURCE_DIRECT_SOUND_ORDER");
        Reject("direct-consumer-missing-common-ordinal", () => consumer.Consume(Call(4, tick: 4, outputSequence: 0)),
            "SOURCE_DIRECT_SOUND_OUTPUT_SEQUENCE");
        Reject("direct-consumer-duplicate-common-ordinal", () => consumer.Consume(Call(4, tick: 4, outputSequence: 3)),
            "SOURCE_DIRECT_SOUND_OUTPUT_SEQUENCE");
        Reject("direct-consumer-reversed-common-ordinal", () => consumer.Consume(Call(4, tick: 4, outputSequence: 2)),
            "SOURCE_DIRECT_SOUND_OUTPUT_SEQUENCE");
        Reject("direct-consumer-wrong-volume", () => consumer.Consume(Call(4, tick: 4, volume: 234)), "SOURCE_DIRECT_SOUND_FIELDS");
        Reject("direct-consumer-wrong-pan", () => consumer.Consume(Call(4, tick: 4, pan: 255)), "SOURCE_DIRECT_SOUND_FIELDS");
        Reject("direct-consumer-wrong-priority", () => consumer.Consume(Call(4, tick: 4, priority: 10)), "SOURCE_DIRECT_SOUND_FIELDS");
        Reject("direct-consumer-wrong-bank", () => consumer.Consume(Call(4, tick: 4, bank: "players")), "SOURCE_DIRECT_SOUND_FIELDS");
        Reject("direct-consumer-wrong-playlist", () => consumer.Consume(Call(4, 103, 4)), "SOURCE_DIRECT_SOUND_FIELDS");
        Check(consumer.DeliveredCount == 3 && consumer.Receipts.Count == 3,
            "failed delivery units must not manufacture successful receipts");
    }

    private static object[] VerifySharedOrder()
    {
        var cases = new List<object>();
        Check(Call(1, outputSequence: 0).OutputSequence == 0,
            "inactive legacy DTO construction keeps zero; opted-in consumption must not accept it");
        foreach (bool directFirst in new[] { true, false })
        {
            var order = new SourceSoundOutputOrder();
            var recorder = new SourceDirectSoundRecordingConsumer();
            SourceDirectSoundCall direct = Call(1, tick: 42, order: 7, outputSequence: directFirst ? 11 : 12);
            var vm = new { OutputSequence = directFirst ? 12L : 11L, SourceTick = 42L, DispatchOrder = 7,
                RandomCalls = 0, Subcalls = new[] { 0 } };
            var delivered = new List<string>();
            var observedSequences = new List<long>();
            var plan = order.Begin(new[] { direct.OutputSequence }, new[] { vm.OutputSequence });
            foreach (var item in plan)
            {
                if (item.Direct) { recorder.Consume(direct); delivered.Add("direct"); }
                else delivered.Add("manual-vm-token");
                observedSequences.Add(item.OutputSequence);
                order.Complete(item.OutputSequence);
            }
            order.RequireComplete();
            Check(direct.SourceTick == vm.SourceTick && direct.DispatchOrder == vm.DispatchOrder &&
                direct.RandomCalls == 0 && vm.RandomCalls == 0 && direct.SubcallOrder == vm.Subcalls[0] &&
                plan[0].Direct == directFirst && plan.All(item => item.Index == 0) &&
                observedSequences.SequenceEqual(new[] { 11L, 12L }) &&
                recorder.DeliveredCount == 1 && ReferenceEquals(recorder.Receipts[0].Call, direct),
                "manual same-tick/same-dispatch zero-RNG opposite queues must follow ONLY the actual ordinal values");
            cases.Add(new { directFirst, delivered, observedSequences, direct, manualVmMetadata = vm,
                actualVmDispatchOrProducerDelivery = false });
        }
        var interleaved = new SourceSoundOutputOrder();
        var middle = interleaved.Begin(new[] { 22L }, new[] { 21L, 23L });
        Check(middle.Select(item => item.OutputSequence).SequenceEqual(new[] { 21L, 22L, 23L }) &&
            middle.Select(item => item.Direct).SequenceEqual(new[] { false, true, false }) &&
            middle[2].Index == 1, "manual multi-VM stream retains original indices and whole-dispatch subcalls");
        foreach (var item in middle) interleaved.Complete(item.OutputSequence);
        interleaved.RequireComplete();
        var nextBatch = interleaved.Begin(new[] { 24L }, Array.Empty<long>());
        Check(nextBatch.Single().OutputSequence == 24, "common sequence remains monotonic across source batches");
        interleaved.Complete(24);
        interleaved.RequireComplete();
        void Invalid(string name, Action<SourceSoundOutputOrder> operation)
            => Reject(name, () => operation(new SourceSoundOutputOrder()), "SOURCE_SOUND_OUTPUT_SEQUENCE");
        Invalid("missing-direct-common-ordinal", o => o.Begin(new[] { 0L }, new[] { 1L }));
        Invalid("missing-vm-common-ordinal", o => o.Begin(new[] { 1L }, new[] { 0L }));
        Invalid("duplicate-direct-common-ordinal", o => o.Begin(new[] { 1L, 1L }, Array.Empty<long>()));
        Invalid("duplicate-vm-common-ordinal", o => o.Begin(Array.Empty<long>(), new[] { 1L, 1L }));
        Invalid("duplicate-cross-queue-common-ordinal", o => o.Begin(new[] { 1L }, new[] { 1L }));
        Invalid("reversed-direct-producer-queue", o => o.Begin(new[] { 2L, 1L }, Array.Empty<long>()));
        Invalid("reversed-vm-producer-queue", o => o.Begin(Array.Empty<long>(), new[] { 2L, 1L }));
        Invalid("reversed-consumer-completion", o => { o.Begin(new[] { 1L }, new[] { 2L }); o.Complete(2); });
        Invalid("duplicate-consumer-completion", o => { o.Begin(new[] { 1L }, Array.Empty<long>()); o.Complete(1); o.Complete(1); });
        Invalid("missing-consumer-completion", o => { o.Begin(new[] { 1L }, new[] { 2L }); o.Complete(1); o.RequireComplete(); });
        Invalid("unfinished-batch-not-overwritten", o => { o.Begin(new[] { 1L }, Array.Empty<long>()); o.Begin(new[] { 2L }, Array.Empty<long>()); });
        Invalid("stale-next-batch-ordinal", o => { o.Begin(new[] { 5L }, Array.Empty<long>()); o.Complete(5); o.Begin(Array.Empty<long>(), new[] { 5L }); });
        var poisoned = new SourceSoundOutputOrder();
        Reject("bad-order-poisons-consumer", () => poisoned.Begin(new[] { 0L }, Array.Empty<long>()),
            "SOURCE_SOUND_OUTPUT_SEQUENCE");
        Reject("bad-order-has-no-empty-green-fallback", () => poisoned.Begin(Array.Empty<long>(), Array.Empty<long>()),
            "SOURCE_SOUND_OUTPUT_SEQUENCE");
        return cases.ToArray();
    }

    private static void VerifyManifest(byte[] manifest, Func<string, byte[]> read)
    {
        void Changed(string name, Action<JsonObject> change, string field)
        {
            JsonObject document = JsonNode.Parse(manifest)!.AsObject();
            change(document);
            Reject(name, () => new DataCatalog(JsonSerializer.SerializeToUtf8Bytes(document), read), field);
        }
        Changed("missing-feature", d => d.Remove("featureContract"), "featureContract");
        Changed("missing-factory", d => d.Remove("factory"), "explicit SourceC2Composition.CreateDefaultAudiblesSimulation");
        Changed("old-factory", d => d["factory"] = KernelBinding.PreSnapSwitchFactory, "explicit SourceC2Composition.CreateDefaultAudiblesSimulation");
        Changed("substituted-payload-pin", d => d["files"]![0]!["sha256"] = new string('0', 64), "unchanged563 payload pins/entrypoints");
        Changed("retained-settings-made-active", d => d["entrypoints"]!["settings"] = "settings.json", "unchanged563 payload pins/entrypoints");
        Changed("extra-unconsumed-input", d => d["files"]!.AsArray().Add(new JsonObject {
            ["path"] = "private/unconsumed", ["bytes"] = 1, ["sha256"] = new string('0', 64)
        }), "unchanged563 payload pins/entrypoints");
    }

    private sealed class Sink : IRenderSink
    {
        internal int WorldDraws, Models;
        internal int HudPresentCalls;
        internal double[] Joints = Array.Empty<double>();
        internal JsonElement State;
        internal string[] LastHudTextures = Array.Empty<string>();
        internal int[] LastHudColors = Array.Empty<int>();
        internal double[] LastHudVertices = Array.Empty<double>();
        internal sealed record HudUpload(int Width, int Height, int Mips, bool Repeat, bool Linear,
            string SourceHash, string RgbaHash, byte[] Rgba);
        internal readonly Dictionary<string, HudUpload> HudUploads = new();
        private readonly HashSet<string> meshes = new(), textures = new();
        public void Texture(string id, int w, int h, byte[] rgba, int mips, bool repeat, bool linear, string hash, string origin)
        {
            Check(rgba.Length >= w * h * 4 && textures.Add(id), "actual original uploaded texture " + id);
            if (id.StartsWith("hud/", StringComparison.Ordinal))
            {
                string actual = DataCatalog.Hash(rgba);
                Check(actual == hash, "real HUD binding hash matches the actual uploaded RGBA bytes: " + id);
                HudUploads.Add(id, new HudUpload(w, h, mips, repeat, linear, hash, actual, (byte[])rgba.Clone()));
            }
        }
        public void Mesh(string id, double[] vertices, int[] indices)
        {
            Check(meshes.Add(id) && vertices.Length % 5 == 0 && indices.All(i => i >= 0 && i < vertices.Length / 5),
                "actual original uploaded geometry " + id);
        }
        public void WorldDraw(string mesh, string texture, string state)
        { Check(meshes.Contains(mesh) && textures.Contains(texture), "actual World binding"); WorldDraws++; }
        public void Model(int id, string joints) { Models++; }
        public void Appearance(int id, string metadata) { }
        public void FreeBall(string mesh, string texture, string alternateTexture, string metadata)
        { Check(meshes.Contains(mesh) && textures.Contains(texture), "actual original medball2 binding"); }
        public void Effects(double[] projection, double[] vertices, int[] textures, int[] rawFlags, double[] alpha, string metadata) { }
        public void Present(double[] camera, double[] joints, int[] models, int[] appearances, double[] ball,
            double[] hudVertices, int[] hudColors, string[] hudTextures, int[] hudCounts, string status)
        {
            Joints = (double[])joints.Clone();
            State = JsonDocument.Parse(status).RootElement.Clone();
            HudPresentCalls++;
            LastHudTextures = (string[])hudTextures.Clone();
            LastHudColors = (int[])hudColors.Clone();
            LastHudVertices = (double[])hudVertices.Clone();
            Check(hudCounts.Sum() * 4 == hudVertices.Length && hudTextures.All(textures.Contains),
                "actual original HUD draw packet, not status-only");
        }
    }
}
