using System.Text.Json;
using Blitz.AssetImport;
using Blitz.Contracts;
using Blitz.Gameplay;
using Blitz.Presentation;
using Blitz.Runtime;
using Blitz.SourcePlayer;

static partial class Checks
{
    private static void VerifySourceWorld(PlayerData data, string? proofOutput)
    {
        if (data.World.Manifest.schema == SourceEnvironmentSelection.Schema)
        {
            VerifySelectedVenue(data, proofOutput);
            return;
        }
        int before = assertions;
        VerifyWorldMath();
        SourceWorldRenderPacket[] userFogPackets = VerifyPublicUserFog(data);
        string profile = data.Catalog.Profile.StableId;
        Check(!PreviewOptions.FromUrl("", profile).WorldEnabled &&
            !PreviewOptions.FromUrl("?source-world=disabled", profile).WorldEnabled &&
            !PreviewOptions.FromUrl("#?source-world=portable", profile).WorldEnabled &&
            PreviewOptions.FromUrl("?source%2dworld=port%61ble", profile).WorldEnabled,
            "The single exact world flight defaults OFF, ignores fragments and decodes once.");
        foreach (string url in new[] { "?source-world", "?source-world=", "?source-world=native",
            "?source-world=Portable", "?source-world=portable=extra",
            "?source-world=portable&source-world=disabled", "?source-world=portable&source-compatibility=strict" })
        {
            bool rejected = false;
            try { PreviewOptions.FromUrl(url, profile); }
            catch (InvalidDataException error) { rejected = error.Message.Contains("source-world"); }
            Check(rejected, "Invalid source-world option must fail explicitly: " + url);
        }
        var sink = new RecordingSink();
        var host = new PlayerHost(data, sink, options: PreviewOptions.FromUrl(
            "?source-gameflow=portable&source-world=portable", profile));
        var unchanged = new PlayerHost(data, new RecordingSink(), options: PreviewOptions.FromUrl("?source-gameflow=portable", profile));
        CabinetRender(host); CabinetRender(unchanged);
        Check(host.World != null && host.World.Packet != null && unchanged.World == null &&
            !JsonDocument.Parse(unchanged.LastStatus).RootElement.TryGetProperty("world", out _),
            "Only the opted actual Host emits the world packet; old status/defaults remain unchanged.");
        CabinetStart(host); CabinetStart(unchanged);
        SourceWorldPresentation initial = host.World!;
        int meshUploads = sink.Meshes.Count, textureUploads = sink.Textures.Count;
        Check(host.Epoch == 1 && initial.Epoch == 1 && initial.SourceFramesObserved == 1,
            "A deliberate fresh factory resets world observation once before source wake1.");
        bool skipped = false;
        for (int step = 0; step < 100; step++)
        {
            long tick = host.Game.SourceTick, observed = initial.SourceFramesObserved;
            CabinetRaw(host); CabinetRaw(unchanged);
            CabinetRender(host, 1); CabinetRender(unchanged, 1);
            if (host.Game.SourceTick == tick)
            {
                skipped = true;
                Check(initial.SourceFramesObserved == observed,
                    "A real57/60 host skip cannot age world/fog configuration or manufacture source wakes.");
            }
        }
        Check(skipped && host.Game.SourceTick == unchanged.Game.SourceTick &&
            host.Game.State.Players.Select(p => (p.Id, p.Position, p.Height, p.Animation.SourceTick))
                .SequenceEqual(unchanged.Game.State.Players.Select(p => (p.Id, p.Position, p.Height, p.Animation.SourceTick))) &&
            JsonSerializer.Serialize(((SourceInjectedRandom)host.Game.SourceRandomService).Trace) ==
                JsonSerializer.Serialize(((SourceInjectedRandom)unchanged.Game.SourceRandomService).Trace),
            "Source world rendering must leave actual gameplay, ordered RNG and source clocks identical to the old route.");
        SourceWorldRenderPacket packet = initial.Packet ?? throw new InvalidOperationException("Missing actual source world packet.");
        Check(packet.Links.Count > 0 && packet.Vertices.Count == packet.Links.Count / 4 * 30 &&
            initial.Actors.Any(actor => actor.OnScreen && actor.Links.Any(link => link.Accepted)),
            "Production packets must reach real on-screen actor-linked articulated geometry, not an unused synthetic model.");
        var m = host.Camera.Current.Transform; var eye = host.Camera.Current.Position;
        foreach (SourcePlayerShadow actor in initial.Actors.Where(actor => actor.OnScreen))
        {
            foreach (int joint in new[] { 0, 4, 8, 11, 14 })
            {
                int offset = (actor.ActorId * 15 + joint) * 16;
                double x = sink.Joints[offset + 12], y = sink.Joints[offset + 13], z = sink.Joints[offset + 14];
                SourceVector3 ground = actor.GroundJoints[joint];
                double gx = eye.X + m[0] * ground.X + m[4] * ground.Y + m[8] * ground.Z;
                double gy = eye.Y + m[1] * ground.X + m[5] * ground.Y + m[9] * ground.Z;
                double gz = eye.Z + m[2] * ground.X + m[6] * ground.Y + m[10] * ground.Z;
                Check(Math.Abs(gy) < .001 && Math.Abs(gx - (x + y * 1.54 / 3.07)) < .001 &&
                    Math.Abs(gz - (z - y * 1.44 / 3.07)) < .001,
                    "Actual outgoing affine joint pivots must project by source SUN onto worldY0, including source actor height/orientation.");
            }
            foreach (SourcePlayerShadowLink link in actor.Links.Where(link => link.Accepted).Take(2))
            {
                SourceVector3 a = link.CameraVertices[0], d = link.CameraVertices[3];
                double width = Math.Sqrt((a.X - d.X) * (a.X - d.X) + (a.Y - d.Y) * (a.Y - d.Y) + (a.Z - d.Z) * (a.Z - d.Z));
                Check(Math.Abs(width - SourcePlayerShadowModel.Linkage[link.LinkIndex].FirstWidth * 2) < .001,
                    "Source link half-widths remain fixed; scaled affine joints are not scaled twice or replaced by a blob radius.");
            }
        }
        JsonElement world = sink.State.GetProperty("world");
        Check(world.GetProperty("shadows").GetProperty("materialReady").GetBoolean() == false &&
            world.GetProperty("shadows").GetProperty("textures").EnumerateArray().All(texture =>
                texture.GetProperty("missing").GetBoolean() && texture.GetProperty("texture").ValueKind == JsonValueKind.Null) &&
            world.GetProperty("fog").GetProperty("configurationBound").GetBoolean() &&
            world.GetProperty("fog").GetProperty("userConfiguration").GetProperty("selection").GetInt32() == 0 &&
            world.GetProperty("fog").GetProperty("sourceEnabled").ValueKind == JsonValueKind.Null &&
            world.GetProperty("fog").GetProperty("fogMask").ValueKind == JsonValueKind.Null &&
            !world.GetProperty("fog").GetProperty("enabled").GetBoolean(),
            "Missing shadow art and None's unknown saved versus operands must remain explicit after the real user configuration, not aliased textures, invented zero masks or RNG.");
        using (var effects = JsonDocument.Parse(sink.WorldEffectsMetadata))
        {
            JsonElement envelope = effects.RootElement;
            Check(envelope.GetProperty("frame").GetProperty("enabled").GetBoolean() == host.Options.FireEnabled &&
                envelope.GetProperty("hudCombiner").GetString() == "DECAL_TEXTURE_ADD_CCALPHA" &&
                envelope.GetProperty("world").GetProperty("state").GetRawText() == packet.Metadata &&
                envelope.GetProperty("world").GetProperty("vertices").GetArrayLength() == packet.Vertices.Count,
                "The production Browser envelope must retain C6/C7/fire/meter fields and carry this actual world payload through Effects.");
        }
        long observedBefore = initial.SourceFramesObserved, sourceBefore = host.Game.SourceTick;
        int randomBefore = ((SourceInjectedRandom)host.Game.SourceRandomService).Trace.Count;
        host.SetActive(false);
        CabinetRender(host);
        double[] verticesBefore = initial.Packet!.Vertices.ToArray();
        for (int redraw = 0; redraw < 3; redraw++) CabinetRender(host, 6);
        Check(host.Game.SourceTick == sourceBefore && initial.SourceFramesObserved == observedBefore &&
            initial.Packet!.Vertices.SequenceEqual(verticesBefore) &&
            ((SourceInjectedRandom)host.Game.SourceRandomService).Trace.Count == randomBefore,
            "Pause/redraw holds real geometry, camera and source observations without advancing weather, gameplay or RNG.");
        host.SetActive(true); CabinetRaw(host); CabinetRender(host);
        double[] paused = initial.Packet!.Vertices.ToArray();
        for (int step = 0; step < 10; step++) { CabinetRaw(host); CabinetRender(host, 1); }
        Check(!paused.SequenceEqual(initial.Packet!.Vertices), "Real resumed source animation/camera must dynamically change the projected joint-linked packet.");
        // Labelled terminal fixture; only the following public cabinet rematch/new-factory inputs are exercised here.
        FlowEnd(host, 3, 0, 7, clock: 0);
        CabinetNextOptions(host);
        long epoch = host.Epoch;
        var game = host.Game;
        CabinetTap(host, 1, 0); CabinetRender(host);
        Check(ReferenceEquals(game, host.Game) && host.Epoch == epoch + 1 &&
            !ReferenceEquals(initial, host.World) && host.World!.Epoch == host.Epoch &&
            host.World.SourceFramesObserved == 1 && host.Game.SourceTick == 0 &&
            sink.Meshes.Count == meshUploads && sink.Textures.Count == textureUploads,
            "Public rematch clears the old world epoch once and retains immutable original GPU resources without re-upload.");
        FlowEnd(host, 3, 7, 0, clock: 0); CabinetNextOptions(host);
        CabinetTap(host, 2, 0);
        var rematchWorld = host.World;
        epoch = host.Epoch;
        CabinetStart(host);
        Check(!ReferenceEquals(game, host.Game) && !ReferenceEquals(rematchWorld, host.World) &&
            host.Epoch == epoch + 1 && host.World!.SourceFramesObserved == 1 && sink.Textures.Count == textureUploads,
            "Actual changed-session factory drops all old world source observations exactly once, without changing admitted data.");
        if (proofOutput != null)
        {
            string path = Path.GetFullPath(proofOutput);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(new {
                schema = "source-world-managed-production/v1", syntheticMathSeparatelyLabelled = true,
                naturalWorldSourceTick = sourceBefore, acceptedLinks = packet.Links.Count / 4,
                packetVertices = packet.Vertices.Count, nativePixelsQualified = false,
                metadata = JsonDocument.Parse(packet.Metadata).RootElement,
                effectsEnvelope = JsonDocument.Parse(packet.AttachEffects("{\"frame\":{\"enabled\":false}}")).RootElement,
                userFogEnvelopes = userFogPackets.Select(user => JsonDocument.Parse(
                    user.AttachEffects("{\"frame\":{\"enabled\":false}}")).RootElement).ToArray()
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
        Console.WriteLine(JsonSerializer.Serialize(new { status = "passed", assertions = assertions - before,
            worldLinks = packet.Links.Count / 4, sourceTick = sourceBefore,
            shadowMaterial = "blocked: four exact original textures not admitted", fog = "actual five-choice user configuration; unknown saved masks retained",
            boundary = "Actual managed Host/pose/packet/clock/reset; synthetic source-math and labelled terminal fixtures, not GPU pixels or natural full-match acceptance." }));
    }

    private sealed class SelectedVenueSink : RecordingSink, IRenderSink, ISourceAudioSink
    {
        internal readonly List<object> WorldTextures = new();
        internal readonly List<object> Draws = new();
        internal readonly List<string> WorldMeshUploads = new();
        private readonly CompleteAudioSink audio = new();
        public void AudioSample(string id, int rate, int channels, byte[] pcm, string metadata) =>
            audio.AudioSample(id, rate, channels, pcm, metadata);
        public void AudioCommands(string text) => audio.AudioCommands(text);
        void IRenderSink.Mesh(string id, double[] vertices, int[] indices)
        {
            Mesh(id, vertices, indices);
            if (id.StartsWith("world/", StringComparison.Ordinal)) WorldMeshUploads.Add(id);
        }
        void IRenderSink.Texture(string id, int width, int height, byte[] rgba, int mipCount,
            bool repeat, bool linear, string sha256, string origin)
        {
            Texture(id, width, height, rgba, mipCount, repeat, linear, sha256, origin);
            if (id.StartsWith("world/", StringComparison.Ordinal) || id.StartsWith("original/", StringComparison.Ordinal))
                WorldTextures.Add(new { id, sha256, origin, width, height, mipCount, repeat, linear });
        }
        void IRenderSink.WorldDraw(string mesh, string texture, string metadata)
        {
            WorldDraw(mesh, texture, metadata);
            Draws.Add(new { mesh, texture, state = JsonDocument.Parse(metadata).RootElement.Clone() });
        }
    }

    private static void VerifySelectedVenue(PlayerData data, string? proofOutput)
    {
        if (data.WorldCatalog != null) { VerifyRuntimeVenueChoices(data, proofOutput); return; }
        int before = assertions;
        var sink = new SelectedVenueSink();
        var host = new PlayerHost(data, sink, options: PreviewOptions.FromUrl(
            "?source-world=portable", data.Catalog.Profile.StableId));
        var unchanged = new PlayerHost(data, new RecordingSink(), options: PreviewOptions.FromUrl(
            "", data.Catalog.Profile.StableId));
        for (int step = 0; step < 12; step++)
        {
            host.Advance(FixedStepBridge.StepSeconds, 512, 384, 512, 384, 1);
            unchanged.Advance(FixedStepBridge.StepSeconds, 512, 384, 512, 384, 1);
        }
        JsonElement venue = sink.State.GetProperty("world").GetProperty("venue");
        SourceEnvironmentSelection selected = SourceEnvironmentReader.ValidateSelection(data.World.Manifest);
        Check(venue.GetProperty("selectionId").GetString() == selected.Identity &&
            venue.GetProperty("field").GetInt32() == data.Bootstrap.FieldIndex &&
            venue.GetProperty("stadium").GetInt32() == selected.Stadium &&
            venue.GetProperty("homePrefix").GetString() == selected.HomePrefix &&
            venue.GetProperty("alternateVenuesAdmitted").GetBoolean() &&
            venue.GetProperty("teamEndZonesBound").GetBoolean(),
            "The actual v2 PlayerData/Host/SourceWorldPresentation path must emit the verified source selection.");
        Check(host.World != null && host.World.Packet != null && host.Game.SourceTick > 0 &&
            sink.WorldDraws == data.World.Manifest.draws.Sum(draw => data.World.Meshes[draw.meshIndex].Indices.Length) &&
            host.Game.SourceTick == unchanged.Game.SourceTick &&
            host.Game.State.Players.Select(actor => (actor.Id, actor.Position, actor.Animation.SourceTick))
                .SequenceEqual(unchanged.Game.State.Players.Select(actor => (actor.Id, actor.Position, actor.Animation.SourceTick))) &&
            JsonSerializer.Serialize(((SourceInjectedRandom)host.Game.SourceRandomService).Trace) ==
                JsonSerializer.Serialize(((SourceInjectedRandom)unchanged.Game.SourceRandomService).Trace),
            "Selected world uses the actual draw inventory without changing original57 gameplay, animation or ordered RNG.");
        data.Catalog.RequireAllInputsConsumed();
        if (proofOutput != null)
        {
            string path = Path.GetFullPath(proofOutput);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(new {
                schema = "source-venue-managed-production/v1", synthetic = false,
                originalDataManifestSha256 = data.Catalog.ManifestSha256, selectionId = selected.Identity,
                sourceDrawPieces = data.World.Manifest.draws.Length, uploadedWorldDrawCalls = sink.WorldDraws,
                uploadedDraws = sink.Draws, uploadedTextures = sink.WorldTextures,
                effectsEnvelope = JsonDocument.Parse(sink.WorldEffectsMetadata).RootElement.Clone(),
                sourceTick = host.Game.SourceTick, assertions = assertions - before,
                nativeOrBrowserPixelsClaimed = false
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
        Console.WriteLine(JsonSerializer.Serialize(new { status = "passed-selected-venue", selectionId = selected.Identity,
            sourceTick = host.Game.SourceTick, assertions = assertions - before, uploadedDraws = sink.WorldDraws,
            boundary = "Actual newly prepared data and production managed host; not GPU pixels, complete-original successor or native runtime acceptance." }));
    }

    private static void VerifyRuntimeVenueChoices(PlayerData data, string? proofOutput)
    {
        int before = assertions;
        var sink = new SelectedVenueSink();
        var options = PreviewOptions.FromUrl("?source-gameflow=portable&source-world=portable", data.Catalog.Profile.StableId);
        Check(options.RecordsEnabled && options.PreGameSoundEnabled,
            "The runtime catalog successor retains its actual original303 pregame/records composition, not the old548 component factory.");
        var store = new SourcePlayerRecordStore(new SourceRecordDelegateStorage(
            () => Task.FromResult(new SourceRecordStorageImage(null)), (_, _) => Task.FromResult(false)));
        store.LoadAsync().GetAwaiter().GetResult();
        var host = new PlayerHost(data, sink, options: options, recordStore: store);
        void Raw(int button = 0, double x = 0) =>
            host.Capture(new[] { x, 0d, 0d, 0d, 0d, 0d, 0d, 0d }, new[] { button, 0, 0, 0 });
        void Tap(int button = 0, double x = 0)
        {
            Raw(); Raw(button, x); Raw(); CabinetRender(host);
            for (int step = 0; step < 70 && host.Session.UsingSourcePreGameInput; step++)
            { Raw(); CabinetRender(host, 1); }
        }
        CabinetRender(host);
        var initialMeshes = new HashSet<string>(sink.Meshes, StringComparer.Ordinal);
        int meshes = sink.WorldMeshUploads.Count, textures = sink.WorldTextures.Count, registered = sink.WorldDraws;
        var worldMeshIds = new HashSet<string>(sink.WorldMeshUploads, StringComparer.Ordinal);
        var worldPixels = sink.Textures.Where(texture => texture.Key.StartsWith("world/", StringComparison.Ordinal))
            .ToDictionary(texture => texture.Key, texture => texture.Value, StringComparer.Ordinal);
        bool WorldResourcesUnchanged() => sink.WorldMeshUploads.Count == meshes &&
            worldMeshIds.SetEquals(sink.WorldMeshUploads) && sink.WorldTextures.Count == textures &&
            worldPixels.All(texture => sink.Textures.ContainsKey(texture.Key) && sink.Textures[texture.Key] == texture.Value) &&
            sink.WorldDraws == registered;
        string initial = host.World.Venue.Identity;
        Tap(4);
        if (options.RecordsEnabled) { Tap(x: 1); Tap(1); }
        Check(host.Session.Stage == SourceCabinetMenuStage.Teams, "Actual public guest/join route reaches team selection.");
        bool away = data.Configuration.Presentation.DesignatedHomeSide == TeamId.Away;
        if (away) Tap(4);
        Tap(x: 1);
        if (away) Tap(4);
        Tap(1); Tap(1); Tap(1);
        Check(host.Session.Playing, "Actual public team/roster/versus choices construct a fresh match.");
        for (int step = 0; step < 3; step++) { Raw(); CabinetRender(host, 1); }
        SourceMatchConfiguration match = host.MatchConfiguration;
        int selectedTeam = away ? match.AwayTeam.TeamIndex : match.HomeTeam.TeamIndex;
        string prefix = data.Rosters.GetTeamData(selectedTeam).UniformPrefix;
        var expected = new SourceEnvironmentSelection(data.Bootstrap.FieldIndex, data.WorldCatalog.Stadium, prefix);
        Check(selectedTeam == ((away ? data.Configuration.Match.AwayTeam.TeamIndex : data.Configuration.Match.HomeTeam.TeamIndex) + 1) % 31 &&
            host.World.Venue.Identity == expected.Identity && initial != expected.Identity &&
            ReferenceEquals(host.World.SelectedWorld, data.RequireWorld(expected)),
            "The live designated home from the actually chosen match resolves its authenticated original world, not the initial loaded prefix.");
        string[] actorMeshesAdded = sink.Meshes.Except(initialMeshes).ToArray();
        Check(actorMeshesAdded.All(id => id.StartsWith("numbers/", StringComparison.Ordinal)),
            "The existing selected-player appearance path may add only its lazy source jersey-number meshes: " +
            string.Join(",", actorMeshesAdded));
        Check(WorldResourcesUnchanged(),
            "Legitimate team selection changes only catalog world bindings; original world mesh IDs/pixel hashes and upload counts stay fixed.");
        var world = host.World.SelectedWorld;
        var packet = host.World.Packet;
        long changedEpoch = host.Epoch;
        string changedEnvelope = sink.WorldEffectsMetadata;
        host.Restart(); Raw(); CabinetRender(host);
        Check(host.Epoch > changedEpoch && host.World.Venue.Identity == expected.Identity &&
            WorldResourcesUnchanged(),
            "A genuine same-configuration restart retains the selected home and immutable GPU resources in its new epoch.");
        bool rejected = false;
        try { data.RequireWorld(new SourceEnvironmentSelection((data.Bootstrap.FieldIndex + 1) % 7, data.WorldCatalog.Stadium, prefix)); }
        catch (Exception error) when (error is InvalidDataException || error is SourceEnvironmentException)
        { rejected = error.Message.Contains("actual source movement bootstrap field") ||
            error.Message.Contains("SOURCE_WORLD_VENUE_NOT_ADMITTED"); }
        Check(rejected, "Unavailable actual bootstrap field cannot silently select a loaded catalog variant.");
        data.Catalog.RequireAllInputsConsumed();
        int activeDraws = world.Manifest.draws.Sum(draw => world.Meshes[draw.meshIndex].Indices.Length);
        if (proofOutput != null)
        {
            string path = Path.GetFullPath(proofOutput);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(new {
                schema = "source-venue-managed-production/v1", synthetic = false,
                originalDataManifestSha256 = data.Catalog.ManifestSha256, selectionId = expected.Identity,
                sourceDrawPieces = world.Manifest.draws.Length, uploadedWorldDrawCalls = activeDraws,
                registeredWorldDrawCalls = registered, catalogSelections = data.WorldCatalog.Worlds.Count,
                uploadedDraws = sink.Draws, uploadedTextures = sink.WorldTextures,
                effectsEnvelope = JsonDocument.Parse(changedEnvelope).RootElement.Clone(),
                sourceTick = JsonDocument.Parse(packet.Metadata).RootElement.GetProperty("sourceTick").GetInt64(),
                publicTeamChoice = new { initialSelectionId = initial, selectedTeam, designatedHomeSide = away ? 1 : 0,
                    actualBootstrapField = data.Bootstrap.FieldIndex, changedEpoch, restartEpoch = host.Epoch,
                    unchangedWorldMeshUploads = meshes, unchangedWorldTextureUploads = textures,
                    lazyOriginalPlayerNumberMeshesAdded = actorMeshesAdded },
                assertions = assertions - before, naturalTerminalRematchClaimed = false,
                nativeOrBrowserPixelsClaimed = false
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
        Console.WriteLine(JsonSerializer.Serialize(new { status = "passed-runtime-venue-catalog",
            selectionId = expected.Identity, assertions = assertions - before, sourceTick = host.Game.SourceTick,
            catalogSelections = data.WorldCatalog.Worlds.Count, registeredDraws = registered, activeDraws,
            boundary = "Actual public team/new-match choice and same-configuration restart; renderer rematch epochs separately tested. No natural terminal/rematch, GPU pixels or full-match claim." }));
    }

    private static void WorldVersus(PlayerHost host)
    {
        CabinetTap(host, 4, 0); CabinetTap(host, 1, 0); CabinetTap(host, 1, 0);
        Check(host.Session.Stage == SourceCabinetMenuStage.Versus && !host.Session.Playing,
            "World settings must be chosen by actual public C3 Versus inputs before the new match starts.");
    }

    private static SourceWorldRenderPacket[] VerifyPublicUserFog(PlayerData data)
    {
        var old = new PlayerHost(data, new RecordingSink(), options: PreviewOptions.FromUrl(
            "?source-gameflow=portable", data.Catalog.Profile.StableId));
        WorldVersus(old);
        Check(old.Session.View().Settings.Count == 5 && !old.Session.WorldSettingsEnabled &&
            old.Session.WorldUserFog == null && old.Session.View().Settings.All(line => !line.Contains("FOG")),
            "The old flag-OFF C3 public settings remain the same five choices, without an implicit user fog configuration.");
        CabinetTap(old, 1, 0);
        Check(old.World == null && old.Session.WorldUserFog == null,
            "Old/default factory inputs must never acquire source-world or user fog bindings.");
        uint[] colors = { 0, 0xa0a0a0, 0xa0a0a0, 0, 0x673c00 };
        int[] ends = { 0, 1250, 500, 700, 700 };
        string? baselineGameplay = null;
        PlayerHost? normal = null;
        RecordingSink? normalSink = null;
        var packets = new List<SourceWorldRenderPacket>();
        for (int selected = 0; selected < 5; selected++)
        {
            var sink = new RecordingSink();
            var host = new PlayerHost(data, sink, options: PreviewOptions.FromUrl(
                "?source-gameflow=portable&source-world=portable", data.Catalog.Profile.StableId));
            CabinetRender(host);
            Check(!host.World.Fog.ConfigurationBound && host.World.UserFogInitializations == 0 && host.Session.WorldUserFog == null,
                "The menu backdrop cannot apply a future user's configuration before its deliberate match start.");
            WorldVersus(host);
            Check(host.Session.View().Settings.Count == 6 && host.Session.View().Settings[5] == "SOURCE USER FOG: NONE",
                "The opted actual C3 Versus has precisely the source's five-value fog setting, initially None.");
            CabinetMove(host, 0, 0, -1);
            Check(host.Session.View().SelectedSetting == 5, "Public Up wraps to the added fog setting, not another gameplay field.");
            for (int change = 0; change < selected; change++) CabinetMove(host, 0, 1, 0);
            string label = ((SourceWorldUserFogSelection)selected).ToString().ToUpperInvariant();
            Check(host.Session.View().Settings[5] == "SOURCE USER FOG: " + label &&
                sink.State.GetProperty("hud").GetProperty("text").EnumerateArray().Any(text =>
                    text.GetProperty("Element").GetString() == "cabinet/session/setting/5" &&
                    text.GetProperty("text").GetString() == "SOURCE USER FOG: " + label),
                "Each real public selection must render the original-font label in the actual HUD packet.");
            CabinetTap(host, 1, 0);
            SourceWorldPresentation world = host.World;
            SourceWorldUserFogConfiguration choice = host.Session.WorldUserFog ??
                throw new InvalidOperationException("The public source user fog choice was not committed.");
            SourceWorldFog fog = world.Fog;
            Check(host.Game.SourceTick == 0 && host.Epoch == 1 && choice != null &&
                (int)choice.Selection == selected && ReferenceEquals(choice, world.UserConfiguration) &&
                world.UserFogInitializations == 1 && fog.ConfigurationBound && fog.Origin == SourceWorldFogOrigin.UserConfiguration &&
                fog.SourceEnabled == (selected == 0 ? (bool?)null : true) && fog.Enabled == (selected != 0) &&
                fog.IsFoggy == (selected == 0 ? (int?)null : 1) &&
                fog.FogMask == (selected == 2 ? (int?)0 : null) && fog.ThickFogMask == null &&
                choice.SavedFogMask == null && choice.SavedThickFogMask == null &&
                choice.VersusFogMask == null && choice.VersusThickFogMask == null &&
                fog.ColorRgb == colors[selected] && fog.End == ends[selected],
                "Actual typed Versus GC_FOG commit and after-field-procs initialization must precede wake1; OR precedence keeps unknown numeric masks, with source-determined enabled/color/range/is_foggy only.");
            JsonElement packet = sink.State.GetProperty("world").GetProperty("fog");
            Check(packet.GetProperty("userConfiguration").GetProperty("selection").GetInt32() == selected &&
                packet.GetProperty("userConfiguration").GetProperty("initializations").GetInt32() == 1 &&
                packet.GetProperty("userConfiguration").GetProperty("fieldSetupIsFoggy").ValueKind == JsonValueKind.Null &&
                packet.GetProperty("thickFogMask").ValueKind == JsonValueKind.Null &&
                packet.GetProperty("colorRgb").GetUInt32() == colors[selected] &&
                packet.GetProperty("end").GetSingle() == ends[selected],
                "The production GPU metadata must contain the same actual initialized source choice/fields, not a renderer weather option or guessed RNG outcome.");
            CabinetRender(host); CabinetRender(host);
            Check(ReferenceEquals(fog, world.Fog) && world.UserFogInitializations == 1 &&
                ReferenceEquals(choice, host.Session.WorldUserFog),
                "Redraws cannot reapply GC_FOG or mutate the active immutable choice.");
            for (int step = 0; step < 8; step++) { CabinetRaw(host); CabinetRender(host, 1); }
            string gameplay = JsonSerializer.Serialize(new { host.Game.SourceTick,
                actors = sink.State.GetProperty("actors").EnumerateArray().Select(actor => new {
                    position = actor.GetProperty("position"), pose = actor.GetProperty("poseDigest") }).ToArray(),
                random = ((SourceInjectedRandom)host.Game.SourceRandomService).Trace });
            baselineGameplay ??= gameplay;
            Check(gameplay == baselineGameplay, "All five source fog choices must leave actual gameplay/poses/RNG identical under the same public policy.");
            packets.Add(world.Packet);
            if (selected == 1) { normal = host; normalSink = sink; }
        }
        PlayerHost retained = normal!;
        SourceWorldUserFogConfiguration originalChoice = retained.Session.WorldUserFog ??
            throw new InvalidOperationException("The actual Normal user fog choice was not retained.");
        SourceWorldPresentation originalWorld = retained.World;
        retained.SetActive(false); CabinetRender(retained, 9); retained.SetActive(true); CabinetRaw(retained);
        retained.Restart(); CabinetRender(retained);
        Check(ReferenceEquals(originalChoice, retained.Session.WorldUserFog) &&
            !ReferenceEquals(originalWorld, retained.World) && retained.World.UserFogInitializations == 1 &&
            retained.World.Fog.Enabled && retained.World.Fog.End == 1250,
            "Actual pause/restart retains the immutable user's choice and initializes the fresh world epoch once.");
        // Labelled terminal fixture; the rematch and changed-choice inputs below remain actual public cabinet inputs.
        FlowEnd(retained, 3, 0, 7, clock: 0); CabinetNextOptions(retained);
        CabinetTap(retained, 1, 0); CabinetRender(retained);
        Check(ReferenceEquals(originalChoice, retained.Session.WorldUserFog) &&
            retained.World.UserFogInitializations == 1 && retained.World.Fog.Enabled,
            "A real public rematch must retain its same immutable user configuration, not silently revert fog to unbound.");
        FlowEnd(retained, 3, 7, 0, clock: 0); CabinetNextOptions(retained);
        CabinetTap(retained, 2, 0); WorldVersus(retained);
        Check(retained.Session.View().SelectedSetting == 5, "Returning to choices retains the actual menu cursor, not a private test setter.");
        CabinetMove(retained, 0, -1, 0);
        CabinetTap(retained, 1, 0);
        SourceWorldUserFogConfiguration newChoice = retained.Session.WorldUserFog ??
            throw new InvalidOperationException("The new public None choice was not committed.");
        Check(!ReferenceEquals(originalChoice, newChoice) && newChoice.Selection == SourceWorldUserFogSelection.None &&
            !retained.World.Fog.Enabled && retained.World.Fog.SourceEnabled == null &&
            retained.World.Fog.FogMask == null && retained.World.Fog.ThickFogMask == null &&
            normalSink!.State.GetProperty("world").GetProperty("fog").GetProperty("isFoggy").ValueKind == JsonValueKind.Null,
            "A public changed-session None choice creates a new immutable config, clears the prior epoch's fog and preserves unknown source operands.");
        return packets.ToArray();
    }

    private static void VerifyWorldMath()
    {
        // Synthetic joints, analytic ground projection and source linkage tests, not whole-world proof.
        var camera = SourceCinematicTransform.FromState(new SourceCinematicSeed(
            new SourceVector3(0, 80, -200), new SourceVector3(0, 0, 0), 200, 0, 0));
        var joints = Enumerable.Range(0, 15).Select(i => new SourceVector3(i * 2, 5 + i, i * 3)).ToArray();
        SourcePlayerShadow shadow = SourcePlayerShadowModel.Project(0, 2, joints, camera, 512, 384);
        Check(shadow.Links.All(link => link.Accepted) && shadow.Links[0].TextureSlot == 2 &&
            shadow.Links[3].TextureSlot == 3 && shadow.Links[9].DepthBias == 10,
            "Synthetic source linkage must select the original torso/hand decals and descending per-link bias.");
        SourceVector3 torso = shadow.GroundJoints[8];
        SourceVector3 expected = SourcePlayerShadowModel.ToCamera(new SourceVector3(
            joints[8].X + joints[8].Y * 1.54f / 3.07f, 0, joints[8].Z - joints[8].Y * 1.44f / 3.07f), camera);
        Check(Math.Abs(torso.X - expected.X) < .0001 && Math.Abs(torso.Y - expected.Y) < .0001 &&
            Math.Abs(torso.Z - expected.Z) < .0001 &&
            shadow.Links.SelectMany(link => link.RasterVertices).All(v => v.X * 4 == MathF.Round(v.X * 4) &&
                v.Y * 4 == MathF.Round(v.Y * 4)),
            "Synthetic source height/SUN projection and original Single2097152 quarter-pixel snap must both be retained.");
        Check(SourcePlayerShadowModel.Project(0, 0, joints, camera, 512, 384).Links.Count == 0,
            "The actual source PF_ONSCREEN bit is the shadow draw gate; do not reclassify visibility in JS.");
        var degenerate = Enumerable.Repeat(new SourceVector3(0, 10, 0), 15).ToArray();
        Check(SourcePlayerShadowModel.Project(0, 2, degenerate, camera, 512, 384).Links.All(link => !link.Accepted),
            "Degenerate source reciprocal normalization cannot become finite replacement geometry.");
        SourceWorldFog thin = SourceWorldFog.Versus(0, 0, 1, 0, 0);
        SourceWorldFog thick = SourceWorldFog.Versus(0, 1, 0, 1, 1);
        SourceWorldFog snow = SourceWorldFog.Versus(4, 1, 0, 1, 1);
        Check(thin.Enabled && thin.ColorRgb == 0xa0a0a0 && Math.Abs(thin.PortableFactor(625.5f) - .5f) < .00001 &&
            thick.IsFoggy == 2 && thick.End == 500 && thick.ColorRgb == 0x808080 &&
            snow.IsFoggy == 1 && snow.End == 3700 && snow.ColorRgb == 0x549ec9,
            "Explicit field_setup/thick/night/snow operands must drive the distinct original versus branches; snow overrides thick.");
        Check(!SourceWorldFog.Versus(0, 0, 0, 0, 0).Enabled &&
            !SourceWorldFog.UserSelection(0).Enabled && SourceWorldFog.UserSelection(0).IsFoggy == null &&
            SourceWorldFog.UserSelection(3).ColorRgb == 0 &&
            SourceWorldFog.UserSelection(3).End == 700 && SourceWorldFog.UserSelection(4).ColorRgb == 0x673c00 &&
            SourceWorldFog.User(1, 1, 0xa0a0a0, 500).FogMask == 0 &&
            SourceWorldFog.UserSelection(2).PortableFactor(-20) == 0 &&
            SourceWorldFog.UserSelection(2).PortableFactor(600) == 1,
            "GC_FOG source selections retain none/night/smog/user-thick override and portable W-based clamping without an invented table.");
        SourceWorldUserFogConfiguration normal = SourceWorldUserFogConfiguration.ApplyVersus(SourceWorldUserFogSelection.Normal, 2, 4);
        SourceWorldFog merged = normal.InitializeAfterFieldProcs();
        SourceWorldFog knownOff = SourceWorldUserFogConfiguration.ApplyVersus(SourceWorldUserFogSelection.None, 0, 0)
            .InitializeAfterFieldProcs(1);
        Check(normal.VersusFogMask == 3 && normal.VersusThickFogMask == 4 && merged.FogMask == 0 &&
            merged.ThickFogMask == 4 && merged.IsFoggy == 1 && merged.ColorRgb == 0xa0a0a0 && merged.End == 1250 &&
            knownOff.SourceEnabled == false && knownOff.FogMask == 0 && knownOff.ThickFogMask == 0 && knownOff.IsFoggy == 1 &&
            SourceWorldFog.UserSelection(0).SourceEnabled == null && SourceWorldFog.UserSelection(0).FogMask == null &&
            SourceWorldFog.UserSelection(2).FogMask == 0 && SourceWorldFog.UserSelection(2).ThickFogMask == null,
            "Actual vsscreen apply-then-OR and do_user_fog thick clearing must preserve source precedence and distinguish known-zero, guaranteed nonzero and unknown operands.");
        bool rejected = false;
        try { SourceWorldVenue.RequireAdmitted(4, 0, "BEA", 0, 0, "BEA"); }
        catch (InvalidOperationException error) { rejected = error.Message.StartsWith("SOURCE_WORLD_VENUE_NOT_ADMITTED:"); }
        Check(rejected && SourceWorldVenue.FieldTextureTable(4) == "snow_field_textures" &&
            SourceWorldVenue.StadiumTextureTable(1) == "stadium_textures",
            "Source alternate selectors remain typed, but unavailable venue art rejects rather than aliasing field0.");
    }
}
