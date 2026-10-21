using System.Text.Json;
using System.Text.Json.Nodes;
using Blitz.AssetImport;
using Blitz.Contracts;
using Blitz.Runtime;
using Blitz.SourcePlayer;

internal static class PreSnapSwitchChecks
{
    private static int assertions;
    private static void Check(bool condition, string identity)
    {
        assertions++;
        if (!condition) throw new InvalidOperationException("PRE_SNAP_RAW_HOST: " + identity);
    }

    private static PlayerData DefendingConfiguration(PlayerData data, out string configurationHash)
    {
        string settingsPath = data.Catalog.Entry("settings");
        JsonObject settings = JsonNode.Parse(data.Catalog.Read(settingsPath))!.AsObject();
        Check(settings["station_layout"]!.GetValue<int>() == 4 &&
            settings["stations"]!.AsArray().Count == 1 &&
            settings["stations"]![0]!["id"]!.GetValue<int>() == 0 &&
            settings["stations"]![0]!["team"]!.GetValue<int>() == 0,
            "the actual new default is FourStation with only physical0/Home");
        settings["first_possession"] = 1;
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(settings);
        configurationHash = DataCatalog.Hash(bytes);
        string hash = configurationHash;
        string[] names = { "rosters", "model", "skeleton", "playbook", "movement", "sound", "animations",
            "callbacks", "hit", "impact", "world", "teams", "hud", "settings", "bootstrap", "freeBall",
            "normalCamera", "possessionSound", "fire", "poppedHead" };
        var manifest = new {
            schema = "blitz-portable-source-player/v1", runtimeFlavor = PlayerHost.RuntimeFlavor,
            profile = data.Catalog.Profile.StableId, sourceRevision = SourceEnvironmentReader.SourceCommit,
            converterRevision = SourceEnvironmentReader.ToolsCommit,
            entrypoints = names.Where(data.Catalog.HasEntry).ToDictionary(name => name, data.Catalog.Entry),
            files = data.Catalog.Files.Values.Select(pin => new {
                path = pin.Path, bytes = pin.Path == settingsPath ? bytes.Length : pin.Bytes,
                sha256 = pin.Path == settingsPath ? hash : pin.Sha256
            }).ToArray()
        };
        var catalog = new DataCatalog(JsonSerializer.SerializeToUtf8Bytes(manifest),
            path => path == settingsPath ? bytes : data.Catalog.Read(path));
        return new PlayerData(catalog);
    }

    internal static void Run(PlayerData data, string? output = null)
    {
        Check(data.Catalog.Profile.IsContact282C2M1PreSnapSwitch, "an explicit new563 bundle is required");
        Check(PreviewOptions.FromUrl("").Profile == SourceTeamResourceProfile.C2M1StableId,
            "the historical FromUrl overload remains historical");
        bool mismatch = false;
        try { PreviewOptions.FromUrl("?sourceprofile=" + SourceTeamResourceProfile.C2M1StableId, data.Catalog.Profile.StableId); }
        catch (SourceInterfaceException) { mismatch = true; }
        Check(mismatch, "a historical URL cannot relabel a new built bundle");
        PlayerData defending = DefendingConfiguration(data, out string configurationHash);
        var sink = new Sink();
        var host = new PlayerHost(defending, sink);
        Check(host.Game.SourceDefensivePreSnapSwitchEnabled &&
            host.Options.Profile == data.Catalog.Profile.StableId,
            "actual catalog -> PlayerData -> KernelBinding -> new factory enables the immutable flight");
        var changes = new List<SourceGameEvent>();
        host.Bridge.SourceEventRaised += e => { if (e.Type == SourceEventType.ControllerChanged) changes.Add(e); };
        var trace = new List<object>();
        GamePhase previous = (GamePhase)(-1);
        int phaseHosts = 0, pulses = 0, holds = 0, released = 0, firstActor = -1;
        int secondHolds = 0, secondReleased = 0, secondPrevious = -1;
        long firstHop = -1, secondHop = -1;
        bool stance = false, secondStance = false, repressed = false, live = false, changedPose = false;
        string? initialPose = null;
        for (int step = 0; step < 2400; step++)
        {
            GamePhase phase = host.Game.State.Phase;
            if (phase != previous) { phaseHosts = 0; previous = phase; }
            phaseHosts++;
            int b = 0;
            if (phase == GamePhase.DeadBall && phaseHosts == 32) host.Continue(0);
            if (phase == GamePhase.PlaySelect && phaseHosts == 28) b = 2;
            if (phase == GamePhase.PreSnap && firstHop < 0 && phaseHosts == 40)
            {
                firstActor = host.Game.State.ControlledPlayerId;
                b = 2; pulses++;
            }
            else if (firstHop >= 0 && holds < 3) { b = 2; holds++; }
            else if (!repressed && firstHop >= 0 && stance && released >= 45 && phase == GamePhase.PreSnap)
            {
                secondPrevious = host.Game.State.ControlledPlayerId;
                b = 2; pulses++; repressed = true;
            }
            else if (repressed && secondHolds < 3) { b = 2; secondHolds++; }
            else if (repressed) secondReleased++;
            else if (firstHop >= 0) released++;
            long before = host.Game.SourceTick;
            host.Capture(new double[8], new[] { b, 0, 0, 0 });
            host.Advance(FixedStepBridge.StepSeconds, 512, 384, 512, 384, 1);
            JsonElement frame = sink.State;
            if (host.Game.SourceTick == before) continue;
            JsonElement[] actors = frame.GetProperty("actors").EnumerateArray().ToArray();
            JsonElement[] hopping = actors.Where(a => a.GetProperty("script").GetString() == "m_hopclp_anim").ToArray();
            if (hopping.Length != 0 && firstHop < 0)
            {
                firstHop = host.Game.SourceTick;
                initialPose = hopping[0].GetProperty("poseDigest").GetString();
                Check(pulses == 1 && changes.Count(e => e.SourceTick == firstHop && e.StationId == 0) == 1,
                    "fresh raw B must produce one real controller transfer in the executing source wake");
                SourceGameEvent transfer = changes.Single(e => e.SourceTick == firstHop && e.StationId == 0);
                Check(transfer.TargetPlayerId == firstActor && transfer.ActorPlayerId == host.Game.State.ControlledPlayerId &&
                    transfer.ActorPlayerId != firstActor, "actual ownership, previous actor and station are retained");
                Check(hopping.Single().GetProperty("id").GetInt32() == transfer.ActorPlayerId &&
                    hopping[0].GetProperty("clip").GetString() == "M_HOPCLP",
                    "the real transferred actor immediately samples the authentic hop");
                Check(host.PreSnapSwitchSoundSelections == 1, "kind5 reaches the actual transport/cinematic recording consumer");
            }
            if (repressed && secondHop < 0 && changes.Any(e => e.SourceTick == host.Game.SourceTick && e.StationId == 0))
            {
                secondHop = host.Game.SourceTick;
                SourceGameEvent transfer = changes.Single(e => e.SourceTick == secondHop && e.StationId == 0);
                Check(transfer.TargetPlayerId == secondPrevious &&
                    transfer.ActorPlayerId == host.Game.State.ControlledPlayerId &&
                    transfer.ActorPlayerId != secondPrevious,
                    "released-then-repressed raw B performs its own real ownership transfer");
                Check(actors.Single(a => a.GetProperty("id").GetInt32() == transfer.ActorPlayerId)
                    .GetProperty("script").GetString() == "m_hopclp_anim" &&
                    host.PreSnapSwitchSoundSelections == 2 && pulses == 2,
                    "the second genuine raw edge enters its own authentic hop and exactly one callback");
            }
            if (firstHop >= 0)
            {
                JsonElement actor = actors.Single(a => a.GetProperty("id").GetInt32() == host.Game.State.ControlledPlayerId);
                changedPose |= actor.GetProperty("poseDigest").GetString() != initialPose;
                stance |= actor.GetProperty("script").GetString() == "stance_anim_int" ||
                    actor.GetProperty("clip").GetString() == "RUSH_C";
                if (secondHop >= 0)
                    secondStance |= actor.GetProperty("script").GetString() == "stance_anim_int" ||
                        actor.GetProperty("clip").GetString() == "RUSH_C";
                live |= host.Game.State.Phase == GamePhase.LiveBall;
                Check(sink.Joints.Length == 14 * 15 * 16 && sink.Joints.All(double.IsFinite),
                    "actual fifteen-limb matrices, not a script-name-only observation");
                trace.Add(new { step, before, host.Game.SourceTick, b, host.Game.State.ControlledPlayerId,
                    frame, jointMatricesSha256 = DataCatalog.Hash(System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(sink.Joints))) });
                if (released >= 3)
                    Check(host.PreSnapSwitchSoundSelections == (secondHop >= 0 ? 2 : 1),
                        "held/released raw B does not cascade or repeat either hop callback");
                if (stance && secondStance && live && changedPose && secondReleased >= 40) break;
            }
        }
        Check(firstHop >= 0 && secondHop > firstHop && stance && secondStance && live && changedPose &&
            changes.Count(e => e.StationId == 0 && (e.SourceTick == firstHop || e.SourceTick == secondHop)) == 2 &&
            pulses == 2,
            "two distinct genuine raw edges each reach hop/stance before subsequent ordinary play");
        JsonElement[] audio = sink.State.GetProperty("audio").GetProperty("resolvedHopSelections").EnumerateArray().ToArray();
        Check(audio.Length == 2 && audio.All(call =>
            call.GetProperty("sameCue").GetBoolean() && call.GetProperty("sameContext").GetBoolean() &&
            call.GetProperty("PlaylistId").GetInt32() == 127 && call.GetProperty("Volume").GetInt32() == 160 &&
            call.GetProperty("Pan").GetInt32() == 127 && call.GetProperty("PriorityWord").GetUInt32() == 10 &&
            call.GetProperty("randomCalls").GetInt32() == 0 &&
            call.GetProperty("callSite").GetProperty("line").GetInt32() == 1638),
            "both actual original fixed attack calls retain current bank/human station, same context and zero callback RNG");
        if (output != null)
        {
            Directory.CreateDirectory(output);
            File.WriteAllText(Path.Combine(output, "raw-host-proof.json"), JsonSerializer.Serialize(new {
                schema = "defensive-presnap-switch-raw-host/v1", status = "passed", assertions,
                sourceConfigurationSha256 = configurationHash,
                configurationDelta = "Only explicit first_possession=Away for the public defending-station proof; no private state writes.",
                builtProfile = data.Catalog.Profile.StableId, factory = KernelBinding.FactoryFor(data.Catalog.Profile),
                data.Catalog.ManifestSha256, firstHop, secondHop, pulses, changes, trace,
                browserUnityNativeOrPcmDspQualified = false
            }));
        }
        Console.WriteLine(JsonSerializer.Serialize(new { schema = "defensive-presnap-switch-raw-host/v1",
            status = "passed", assertions, firstHop, secondHop, pulses, factory = KernelBinding.FactoryFor(data.Catalog.Profile),
            profile = data.Catalog.Profile.StableId, enabled = host.Game.SourceDefensivePreSnapSwitchEnabled,
            soundSelections = host.PreSnapSwitchSoundSelections, configurationHash }));
    }

    private sealed class Sink : IRenderSink
    {
        internal JsonElement State;
        internal double[] Joints = Array.Empty<double>();
        public void Texture(string id, int width, int height, byte[] rgba, int mipCount, bool repeat,
            bool linear, string sha256, string origin) { }
        public void Mesh(string id, double[] vertices, int[] indices) { }
        public void WorldDraw(string mesh, string texture, string state) { }
        public void Model(int id, string joints) { }
        public void Appearance(int id, string metadata) { }
        public void FreeBall(string mesh, string texture, string alternateTexture, string metadata) { }
        public void Effects(double[] projection, double[] vertices, int[] textures, int[] rawFlags,
            double[] flashAlpha, string metadata) { }
        public void Present(double[] camera, double[] joints, int[] models, int[] appearances, double[] ball,
            double[] hudVertices, int[] hudColors, string[] hudTextures, int[] hudCounts, string status)
        {
            Joints = joints.ToArray();
            using JsonDocument json = JsonDocument.Parse(status);
            State = json.RootElement.Clone();
        }
    }
}
