using System.Text.Json;
using Blitz.AssetImport;
using Blitz.Contracts;
using Blitz.Gameplay;
using Blitz.Presentation;

namespace Blitz.SourcePlayer;

public sealed class FirePresentation
{
    private readonly SourceFireSpfxFrameConsumer consumer;
    private readonly IRenderSink sink;
    private readonly Dictionary<string, long> recordCounts = new(StringComparer.Ordinal);
    private readonly Queue<object> records = new();
    public bool Enabled { get; }
    public long Epoch { get; }
    public long FramesConsumed { get; private set; }
    public SourceFireRenderFrame Frame => consumer?.LastFrame;
    public IReadOnlyList<SourceFireDrawPacket> Draws => consumer?.Draws ?? (IReadOnlyList<SourceFireDrawPacket>)Array.Empty<SourceFireDrawPacket>();
    private readonly SourceFireSpfxBundle bundle;

    public FirePresentation(SourceFireSpfxBundle bundle, bool enabled, IRenderSink sink, long epoch = 0, bool uploadTextures = true)
    {
        Enabled = enabled; this.bundle = bundle; this.sink = sink; Epoch = epoch;
        if (!enabled) return;
        DataCatalog.Require(bundle != null, "Enabled fire presentation requires the authenticated original SDL/17-TGA generation.");
        consumer = new SourceFireSpfxFrameConsumer(bundle);
        for (int i = 0; uploadTextures && i < bundle.Frames.Count; i++)
        {
            var texture = bundle.Frames[i];
            sink.Texture("fire/" + i, texture.Width, texture.Height, texture.CopyRgba(), 1, true, true,
                texture.SourceSha256, bundle.InputOrigin + "; original TRAIL frame" + (i + 1));
        }
    }

    public void Observe(GameSimulation game)
    {
        DataCatalog.Require(game.SourceFireEnabled == Enabled, "The actual Core fire opt-in and renderer must agree.");
        if (!Enabled || game.SourceFireFrame == null) return;
        var frame = game.SourceFireFrame;
        if (ReferenceEquals(Frame, frame)) return;
        DataCatalog.Require(frame.SourceTick == game.SourceTick, "Consume only the actual after-source immutable fire frame.");
        consumer.Consume(frame);
        FramesConsumed++;
        foreach (var record in frame.Records)
        {
            recordCounts[record.Kind] = recordCounts.GetValueOrDefault(record.Kind) + 1;
            records.Enqueue(new { sourceTick = record.Site.SourceTick, pcount = record.Site.OriginalPcount,
                kind = record.Kind, record.ProcessSerial, record.ObjectSerial, value = record.Value,
                record.Detail, function = record.Site.Function, line = record.Site.SourceLine,
                actor = record.Site.ActorId, wake = record.Site.WakeOrdinal });
            while (records.Count > 128) records.Dequeue();
        }
    }

    public static string MeterHighlightElement(SourceKickMeterKind kind, bool shown, bool highlighted)
    {
        if (!shown || !highlighted) return null;
        return kind switch {
            SourceKickMeterKind.FieldGoal => "kick/fg/ball",
            SourceKickMeterKind.Punt => "kick/punt/bar",
            _ => null
        };
    }

    public static int[] HighlightedHudPrimitives(SourceKickMeterView kicking, IReadOnlyList<SourceHudPrimitive> hud)
    {
        string element = MeterHighlightElement(kicking.Kind, kicking.ShowMeterFlag, kicking.Highlighted);
        return element != null ?
            Enumerable.Range(0, hud.Count).Where(i => hud[i].Element == element).ToArray() : Array.Empty<int>();
    }

    public void Send(IGameState state, double[] projection, IReadOnlyList<SourceHudPrimitive> hud, SourceKickMeterView kicking)
    {
        var flags = state.Players.OrderBy(p => p.Id).Select(p => p.Source.Presentation.RawFlags).ToArray();
        var flash = flags.Select(raw => {
            var material = new SourceFirePlayerFlashState(raw);
            return material.White ? ((material.ConstantColor >> 24) & 255) / 255d : 0d;
        }).ToArray();
        var vertices = new double[Draws.Count * 6 * 5];
        int offset = 0;
        foreach (var draw in Draws)
            for (int i = 0; i < draw.CameraVertices.Count; i++)
            {
                var p = draw.CameraVertices[i];
                vertices[offset++] = p.X; vertices[offset++] = p.Y; vertices[offset++] = p.Z;
                vertices[offset++] = draw.UnityUvs[2 * i]; vertices[offset++] = draw.UnityUvs[2 * i + 1];
            }
        int[] hudHighlight = HighlightedHudPrimitives(kicking, hud);
        sink.Effects(projection, vertices, Draws.Select(d => d.TextureFrame).ToArray(), flags, flash,
            JsonSerializer.Serialize(new { frame = Describe(), hudHighlight, hudConstantColor = 0xc0000000u,
                hudCombiner = "DECAL_TEXTURE_ADD_CCALPHA", hudRestore = "TEXTURE_TIMES_ALPHA/0xff000000",
                timeoutFlasher = "TIME EXPIRED source lifetime/glyphs; native flash_text_proc phase unqualified" }));
    }

    public object Describe() => new {
        enabled = Enabled, epoch = Epoch, sourceTick = Frame?.SourceTick ?? -1,
        framesConsumed = FramesConsumed, particles = Frame?.Particles.Count ?? 0,
        draws = Draws.Count, hidden = Frame?.Particles.Count(p => p.Hidden) ?? 0,
        pcount = Frame?.OriginalPcount, assetManifestSha256 = bundle?.ManifestSha256,
        inputOrigin = bundle?.InputOrigin, projection = Frame?.Basis.ProjectionOrigin,
        depth = "portable reciprocal projection depth; native WBUFFER unqualified",
        cull = (int)(Frame?.Basis.InheritedCull ?? SourceFireCull.Off),
        cameraSourceTick = Frame?.Basis.Camera.SourceTick ?? -1,
        sourceHres = Frame?.Basis.HorizontalResolution ?? 512,
        sourceVres = Frame?.Basis.VerticalResolution ?? 384,
        recordCounts, records = records.ToArray(),
        objects = Frame?.Particles.Take(12).Select(p => new {
            p.ObjectSerial, p.ProcessSerial, p.TextureFrame, p.Hidden,
            world = new[] { p.WorldPosition.X, p.WorldPosition.Y, p.WorldPosition.Z }
        }).ToArray(),
        selectedTextures = Draws.Select(p => new { p.ObjectSerial, p.TextureFrame, p.TextureSourceSha256 }).ToArray(),
        boundaries = Frame?.PortableBoundaries,
        sound = "Original ordered metadata only; Audio output unqualified",
        nativePixelsQualified = false
    };
}
