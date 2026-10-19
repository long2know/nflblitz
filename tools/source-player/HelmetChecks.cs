using System.Text.Json;
using Blitz.AssetImport;
using Blitz.Contracts;
using Blitz.Gameplay;
using Blitz.Presentation;
using Blitz.Runtime;
using Blitz.SourcePlayer;

static class HelmetAdapterChecks
{
    private static int assertions;
    private static void Require(bool value, string message)
    {
        assertions++;
        if (!value) throw new InvalidOperationException("HELMET ADAPTER: " + message);
    }

    public static void Run(PlayerData data)
    {
        Require(data.Configuration.Match.Stations.Count == 4 && data.Fire.HelmetGeometry != null,
            "Use the actual four-human fixture and authenticated original PHELM v3 generation.");
        var sink = new Sink();
        var host = new PlayerHost(data, sink, options: PreviewOptions.FromUrl("?source-fire=portable"));
        Require(sink.HelmetVertices is { Length: 267 * 5 } && sink.HelmetIndices is { Length: 267 },
            "The actual browser sink must upload all89 original PHELM triangles, not phead/ball/generic geometry.");
        SourceFireGeometry geometry = data.Fire.HelmetGeometry ??
            throw new InvalidOperationException("HELMET ADAPTER: authenticated PHELM geometry required.");
        for (int i = 0; i < 267; i++)
        {
            int corner = i / 3 * 3 + (i % 3 == 1 ? 2 : i % 3 == 2 ? 1 : 0);
            SourceVector3 p = geometry.Positions[geometry.VertexIndices[corner]];
            int uv = geometry.UvIndices[corner] * 2;
            Require(sink.HelmetIndices[i] == i && sink.HelmetVertices[i * 5] == p.X &&
                sink.HelmetVertices[i * 5 + 1] == p.Y && sink.HelmetVertices[i * 5 + 2] == p.Z &&
                sink.HelmetVertices[i * 5 + 3] == geometry.RawAuthoringUvs[uv] &&
                sink.HelmetVertices[i * 5 + 4] == geometry.RawAuthoringUvs[uv + 1],
                "The real sink must preserve source authoring pivot/XYZ, original UVs and Renderer321 winding.");
        }
        int continued = -1000, lastRandom = 0, originalPops = 0;
        long lastSource = -1, firstEntry = -1, continueAfter = -1;
        bool skipped = false, paused = false, flew = false, bounced = false, settled = false, cleaned = false;
        var objects = new HashSet<long>();
        var deleted = new HashSet<long>();
        var effectDraws = new List<SourceRandomDraw>();
        var snapshots = new List<object>();
        for (int tick = 0; tick < 36000; tick++)
        {
            int[] buttons = new int[4];
            if (host.Game.State.Phase == GamePhase.PlaySelect && tick % 20 < 5)
                Array.Fill(buttons, (int)SourceButtons.B);
            if (host.Game.State.Phase == GamePhase.DeadBall && tick - continued > 90 &&
                (continueAfter < 0 || host.Game.SourceTick >= continueAfter))
            { host.Continue(0); continued = tick; }
            host.Capture(new double[8], buttons);
            SourceFireRenderFrame before = host.Game.SourceFireFrame;
            host.Advance(FixedStepBridge.StepSeconds, 1024, 768, 1024, 768, 1);
            if (host.Game.SourceTick == lastSource)
            {
                skipped = true;
                Require(ReferenceEquals(before, host.Game.SourceFireFrame), "A skipped57Hz sample must retain the immutable helmet frame.");
                continue;
            }
            lastSource = host.Game.SourceTick;
            var random = host.Game.SourceRandomService as SourceInjectedRandom ??
                throw new InvalidOperationException("HELMET ADAPTER: actual Core shared RNG ledger required.");
            foreach (var draw in random.Trace.Skip(lastRandom))
                if (draw.Site.SourceIdentity.StartsWith("player.c:loose_helmet_proc:", StringComparison.Ordinal)) effectDraws.Add(draw);
            lastRandom = random.Trace.Count;
            SourceFireRenderFrame frame = host.Game.SourceFireFrame;
            if (frame == null) continue;
            originalPops += host.Game.ContactEvents.Count(e => e.Kind == SourceContactEventKind.HelmetPopped);
            if (host.Game.ContactEvents.Any(e => e.Kind == SourceContactEventKind.HelmetPopped))
                continueAfter = host.Game.SourceTick + 220;
            Require(host.Fire.Helmets.Count == frame.LooseHelmets.Count &&
                sink.Helmets.Length == frame.LooseHelmets.Count,
                "Every actual immutable helmet object must reach the production C# renderer packet, not a count-only flag.");
            foreach (SourceLooseHelmet helmet in frame.LooseHelmets)
            {
                bool created = objects.Add(helmet.ObjectSerial);
                if (created)
                {
                    if (firstEntry < 0) firstEntry = frame.SourceTick;
                    Require(helmet.EntryWorldPosition.Y > 0 && helmet.CacheSourceTick == frame.SourceTick - 1 &&
                        frame.Records.Any(r => r.Kind == "LooseHelmetEntryCache" && r.Value == 8),
                        "Natural entry must use actual prior canonical neck8/camera and retain its non-ground Y.");
                    snapshots.Add(new { stage = "entry", frame.SourceTick, helmet.ObjectSerial, helmet.ProcessSerial,
                        helmet.EntrySourceTick, helmet.CacheSourceTick, helmet.EntryWorldPosition, helmet.Decal.TextureId,
                        helmet.Decal.SourceSha256, helmet.Position, helmet.Velocity, helmet.Roll });
                }
                SourceLooseHelmetDraw draw = host.Fire.Helmets.Single(h => h.Helmet.ObjectSerial == helmet.ObjectSerial);
                JsonElement packet = sink.Helmets.Single(h => h.GetProperty("objectSerial").GetInt64() == helmet.ObjectSerial);
                JsonElement appearance = sink.Appearances[sink.ActorAppearances[helmet.ActorId]];
                Require(packet.GetProperty("texture").GetString() == appearance.GetProperty("textures")[2].GetString() &&
                    packet.GetProperty("textureSha256").GetString() == appearance.GetProperty("textureHashes")[2].GetString() &&
                    packet.GetProperty("decalSlot").GetInt32() == 2 && packet.GetProperty("integerAspect").GetInt32() == 4 &&
                    draw.Helmet.Decal.TextureId == helmet.Decal.TextureId,
                    "Natural detached PHELM must bind the exact selected actor pdecal2/current team atlas, not HEDSHU or another team.");
                Require(helmet.Scale == 2 && helmet.Alpha == 255 && helmet.PitchRate == 40 &&
                    packet.GetProperty("packet").GetArrayLength() == 20 &&
                    packet.GetProperty("white").GetBoolean() == ((frame.GlobalBallFlags & 1) != 0),
                    "Actual packet must retain source scale/int fields and current global ball WHITE.");
                Require(packet.GetProperty("worldMatrix").EnumerateArray().Select(p => p.GetSingle())
                    .SequenceEqual(draw.Transform.WorldMatrix), "All actual C# flight/spin matrix components must reach the sink.");
                if (helmet.FlightTime > 3 && !helmet.Position.Equals(helmet.EntryWorldPosition)) flew = true;
                if (helmet.Bounces > 0 && !bounced)
                {
                    bounced = true;
                    snapshots.Add(new { stage = "bounce", frame.SourceTick, helmet.ObjectSerial, helmet.Position,
                        helmet.Velocity, helmet.Bounces, helmet.BounceTime, helmet.RawFlags });
                }
                if (helmet.Bounces > 3 && helmet.Velocity.X == 0 && helmet.Velocity.Y == 0 && helmet.Velocity.Z == 0)
                    settled = true;
                if (!paused)
                {
                    host.SetActive(false); host.Advance(2, 1024, 768, 1024, 768, 1);
                    Require(host.Game.SourceTick == frame.SourceTick && ReferenceEquals(frame, host.Game.SourceFireFrame) &&
                        random.Trace.Count == lastRandom, "Public focus pause must hold helmet fields, process and shared RNG without catch-up.");
                    host.SetActive(true); paused = true;
                }
            }
            foreach (SourceFireRecord record in frame.Records.Where(r => r.Kind == "DeleteObjectGroup" &&
                r.Detail.StartsWith("loosehlm:", StringComparison.Ordinal)))
            {
                deleted.Add(record.ObjectSerial);
                Require(frame.LooseHelmets.All(h => h.ObjectSerial != record.ObjectSerial),
                    "Source delete450 must remove the actual helmet packet in the same immutable dispatch.");
                if (record.Site.SourcePath == "nfl.c" && record.Site.SourceLine == 868) cleaned = true;
            }
            if (objects.Count > 0 && flew && bounced && settled && cleaned && frame.SourceTick >= continueAfter + 40)
            {
                Require(originalPops > 0 && effectDraws.Any(d => d.Site.BoundOrModulus == 100) &&
                    effectDraws.Any(d => d.Site.BoundOrModulus == 35) && effectDraws.Any(d => d.Site.BoundOrModulus == 201) &&
                    effectDraws.Any(d => d.Site.BoundOrModulus == 157) &&
                    effectDraws.All(d => d.Provenance.Evidence == SourceRandomEvidence.ExternalUnverified),
                    "Natural source entry/spin/bounces must consume the actual shared RNG footprint, retaining ExternalUnverified provenance.");
                Require(paused && skipped && host.Game.State.Players.Count == 14 &&
                    host.Game.State.Players.Where(p => p.Source.ControllerStationId >= 0)
                        .Select(p => p.Source.ControllerStationId).Distinct().Count() == 4,
                    "Actual four-human play must continue beyond original flight/bounce/settle/new-play cleanup.");
                long final = frame.SourceTick;
                host.Restart(); host.Advance(0, 1024, 768, 1024, 768, 1);
                Require(host.Game.SourceTick == 0 && host.Game.SourceFireFrame == null && host.Fire.Helmets.Count == 0 &&
                    sink.Helmets.Length == 0, "Public restart must clear the explicit host helmet epoch and every render packet.");
                Console.WriteLine(JsonSerializer.Serialize(new { status = "passed", assertions, originalPops,
                    objects = objects.ToArray(), deleted = deleted.ToArray(), firstEntry, final,
                    flew, bounced, settled, cleaned, paused, skipped, snapshots,
                    sharedDraws = effectDraws.GroupBy(d => d.Site.BoundOrModulus).ToDictionary(g => g.Key, g => g.Count()),
                    boundary = "Actual production C#/host, authenticated source assets and public cabinet controls; not GPU/native/full-game proof." }));
                return;
            }
        }
        throw new InvalidOperationException("HELMET ADAPTER: bounded public controls did not witness complete natural helmet flight/bounce/settle/cleanup.");
    }

    private sealed class Sink : IRenderSink
    {
        internal double[] HelmetVertices = Array.Empty<double>();
        internal int[] HelmetIndices = Array.Empty<int>();
        internal int[] ActorAppearances = Array.Empty<int>();
        internal JsonElement[] Helmets = Array.Empty<JsonElement>();
        internal readonly Dictionary<int, JsonElement> Appearances = new();
        public void Texture(string id, int width, int height, byte[] rgba, int mips, bool repeat, bool linear, string hash, string origin) { }
        public void Mesh(string id, double[] vertices, int[] indices)
        { if (id == SourceFireSpfxReader.HelmetMeshId) { HelmetVertices = vertices; HelmetIndices = indices; } }
        public void WorldDraw(string mesh, string texture, string state) { }
        public void Model(int id, string joints) { }
        public void Appearance(int id, string metadata) { Appearances[id] = JsonDocument.Parse(metadata).RootElement.Clone(); }
        public void FreeBall(string mesh, string texture, string alternateTexture, string metadata) { }
        public void Effects(double[] projection, double[] vertices, int[] textures, int[] flags, double[] flash, string metadata)
        { Helmets = JsonDocument.Parse(metadata).RootElement.GetProperty("looseHelmets").EnumerateArray().Select(h => h.Clone()).ToArray(); }
        public void Present(double[] camera, double[] joints, int[] models, int[] appearances, double[] ball,
            double[] hudVertices, int[] hudColors, string[] hudTextures, int[] hudCounts, string status)
        { ActorAppearances = appearances; }
    }
}
