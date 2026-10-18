using System.Text.Json;
using Blitz.Contracts;
using Blitz.Gameplay;
using Blitz.Presentation;
using Blitz.SourcePlayer;

static class FeatureChecks
{
    private static int checks;
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("FEATURE ADAPTER: " + message);
        checks++;
    }

    public static void Run(PlayerData data)
    {
        var defaults = PreviewOptions.FromUrl("");
        Require(!defaults.FireEnabled && defaults.KickMeters == SourceKickMeterMode.Disabled &&
            defaults.Compatibility == SourceGameplayCompatibility.Portable, "Existing preview default remains portable with BOTH new features disabled.");
        var fragmentOnly = PreviewOptions.FromUrl("http://preview/#ignored?source-compatibility=strict&source-fire=portable");
        Require(!fragmentOnly.FireEnabled && fragmentOnly.Compatibility == SourceGameplayCompatibility.Portable,
            "An option-shaped URL fragment must never override existing portable/default-disabled behavior.");
        var explicitStrict = PreviewOptions.FromUrl("http://preview/?source-compatibility=strict#source-fire=portable");
        Require(!explicitStrict.FireEnabled && explicitStrict.Compatibility == SourceGameplayCompatibility.StrictSource,
            "Explicit query compatibility remains independent of ignored fragment feature text.");
        foreach (string query in new[] {
            "?source-fire=unknown", "?source-fire=portable&source-fire=disabled",
            "?source-kick-meters=native", "?source-kick-meters=disabled&source-kick-meters=portable-authored",
            "?sourceprofile=original-teams-contact272&source-fire=portable",
            "?sourceprofile=original-teams-contact281-c2-m1&sourceprofile=original-teams-contact281-c2-m1",
            "?source-compatibility=portable&source-compatibility=strict"
        })
        {
            bool rejected = false;
            try { PreviewOptions.FromUrl(query); } catch (Exception error) when (error is InvalidOperationException or ArgumentException or NotSupportedException) { rejected = true; }
            Require(rejected, "Actual Core option parsers must reject duplicate/unknown/conflicting options: " + query);
        }
        foreach (string query in new[] { "", "?source-fire=portable", "?source-kick-meters=portable-authored",
            "?source-fire=portable&source-kick-meters=portable-authored" })
        {
            var options = PreviewOptions.FromUrl(query);
            var game = KernelBinding.Create(data, options);
            var kick = (ISourceKickMeterState)game.State;
            Require(game.SourceFireEnabled == options.FireEnabled && kick.KickMeterMode == options.KickMeters,
                "Both independent named options must reach actual M1 factory unchanged.");
            Require(game.SourceFireFrame == null && game.SourceTick == 0, "Validation/construction must not manufacture an after-step frame.");
            if (kick.KickMeterMetrics != null)
                Require(kick.KickMeterMetrics.FieldGoalBarWidth == 151 && kick.KickMeterMetrics.FieldGoalMarkerWidth == 27 &&
                    kick.KickMeterMetrics.PuntMeterHeight == 209 && !kick.KickMeterMetrics.NativeGeneratedMetricsKnown,
                    "Only strict authored HUD metrics151/27/209, explicitly NOT native Composit, may bind.");
        }
        foreach (int height in new[] { 384, 256 })
        foreach (int side in new[] { 1, -1 })
        {
            var point = new SourceVector3(side, 2, 100);
            var original = SourceFireSpfxFrameConsumer.ProjectContinuous(point, height);
            double[] matrix = FrameMath.CameraSpace(512, height, 10000);
            double rasterX = (matrix[0] * point.X / point.Z + 1) * 256;
            double rasterY = (matrix[5] * point.Y / point.Z + 1) * height / 2;
            Require(Math.Abs(rasterX - original.X) < .0001 && Math.Abs(rasterY - original.Y) < .0001,
                "World and fire GPU projection must share active geom.s negative X for both original rasters.");
            Require(side > 0 ? rasterX < 256 : rasterX > 256, "Projection sign must not be compensated by falsifying camera/world coordinates.");
        }
        var sink = new Sink();
        var host = new PlayerHost(data, sink, options: PreviewOptions.FromUrl("?source-fire=portable&source-kick-meters=portable-authored"));
        int registered = sink.TextureCount;
        for (int i = 0; i < 12; i++)
            host.Advance(1d / 60, 1024, 768, 1024, 768, 1);
        Require(host.Game.SourceFireFrame != null && host.Fire.Frame.SourceTick == host.Game.SourceTick &&
            host.Fire.FramesConsumed == host.Game.SourceTick, "Every real source wake must consume the real immutable after-step frame once.");
        var held = host.Fire.Frame;
        var random = host.Game.SourceRandomService as SourceInjectedRandom ??
            throw new InvalidOperationException("The default production preview RNG must expose its actual read-only draw ledger.");
        long consumed = host.Fire.FramesConsumed, draws = random.Trace.Count;
        host.Advance(0, 1024, 768, 1024, 768, 1);
        Require(ReferenceEquals(held, host.Fire.Frame) && consumed == host.Fire.FramesConsumed &&
            draws == random.Trace.Count, "Render/held frames must neither age FX nor consume Core RNG.");
        Require(registered == sink.TextureCount && sink.LastFlags.Length == 14 && sink.LastFlash.Length == 14,
            "Original texture uploads stay static and authoritative actor flags span all14 actors.");
        Require(sink.FireTextures.Count == 17 && sink.FireTextures.Select(t => t.Hash)
            .SequenceEqual(data.Fire.Frames.Select(t => t.SourceSha256)),
            "All17 renderer TRAIL bindings must use the authenticated selected original frame identities.");
        double[] originalRaster = FrameMath.CameraSpace(data.Bootstrap.HorizontalResolution, data.Bootstrap.VerticalResolution, 10000);
        Require(host.Camera.LastSourceTick == host.Game.SourceTick &&
            host.Game.SourceCameraHeadingSampleTick == host.Game.SourceTick &&
            host.Camera.CameraSpaceProjection[0] == originalRaster[0] &&
            host.Camera.CameraSpaceProjection[5] == originalRaster[5] &&
            JsonSerializer.SerializeToElement(host.Camera.Trace.Last()).GetProperty("processOrder").GetInt32() == host.Game.SourceCameraStageOrder,
            "The real pre-player camera stage and effect projection must share the executing source tick.");
        host.Restart();
        host.Advance(0, 1024, 768, 1024, 768, 1);
        host.Advance(1d / 60, 1024, 768, 1024, 768, 1);
        Require(host.Epoch == 1 && host.Fire.Epoch == 1 && sink.TextureCount == registered,
            "Restart must create a new source frame epoch without stale effects or duplicate texture upload.");
        var actor = new SourceFirePlayerFlashState(0x1400);
        Require(actor.White && actor.ConstantColor == 0xc0000000 && !new SourceFirePlayerFlashState(0x1000).White,
            "Only raw0x400 selects source additive-CCALPHA white, never team fire/other bits.");
        // Presentation predicate cases only; they never modify a simulation snapshot.
        foreach (var kind in new[] { SourceKickMeterKind.None, SourceKickMeterKind.FieldGoal, SourceKickMeterKind.Punt })
        foreach (bool shown in new[] { true, false })
        foreach (bool highlighted in new[] { true, false })
        {
            string? expected = !shown || !highlighted || kind == SourceKickMeterKind.None ? null :
                kind == SourceKickMeterKind.FieldGoal ? "kick/fg/ball" : "kick/punt/bar";
            Require(FirePresentation.MeterHighlightElement(kind, shown, highlighted) == expected,
                "Source meter presentation predicate must target only FG ball/punt bar and restore hidden/base material.");
        }
        using var packetDocument = JsonDocument.Parse(sink.LastMetadata);
        var packet = packetDocument.RootElement;
        Require(packet.GetProperty("hudHighlight").GetArrayLength() == 0 &&
            packet.GetProperty("hudConstantColor").GetUInt32() == 0xc0000000u &&
            packet.GetProperty("hudCombiner").GetString() == "DECAL_TEXTURE_ADD_CCALPHA",
            "Actual HUD style packet must transport explicit source additive192 material and no default meter highlight.");
        Console.WriteLine(JsonSerializer.Serialize(new { featureAdapterChecks = checks, status = "passed",
            boundary = "Actual browser adapter/default/options/source-wake/epoch/projection checks; no native/fullmatch or browser gameplay witness substituted." }));
    }

    private sealed class Sink : IRenderSink
    {
        public int TextureCount;
        public int[] LastFlags = Array.Empty<int>();
        public double[] LastFlash = Array.Empty<double>();
        public string LastMetadata = "{}";
        public List<(string Id, string Hash)> FireTextures = new();
        public void Texture(string id, int width, int height, byte[] rgba, int mipCount, bool repeat, bool linear, string hash, string origin)
        {
            TextureCount++;
            if (id.StartsWith("fire/", StringComparison.Ordinal)) FireTextures.Add((id, hash));
        }
        public void Mesh(string id, double[] vertices, int[] indices) { }
        public void WorldDraw(string mesh, string texture, string state) { }
        public void Model(int id, string joints) { }
        public void Appearance(int id, string metadata) { }
        public void FreeBall(string mesh, string texture, string origin) { }
        public void Effects(double[] projection, double[] vertices, int[] textures, int[] flags, double[] flash, string metadata)
        {
            LastFlags = flags; LastFlash = flash; LastMetadata = metadata;
            Require(vertices.Length == textures.Length * 30, "Actual selected frame/triangle packing must stay paired.");
        }
        public void Present(double[] camera, double[] joints, int[] models, int[] appearances, double[] ball,
            double[] hud, int[] colors, string[] textures, int[] counts, string status) { }
    }
}
