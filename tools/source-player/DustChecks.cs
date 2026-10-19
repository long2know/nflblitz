using System.Text.Json;
using Blitz.Contracts;
using Blitz.Gameplay;
using Blitz.Runtime;
using Blitz.SourcePlayer;

static class DustAdapterChecks
{
    private static int assertions;
    private static void Require(bool value, string message)
    {
        assertions++;
        if (!value) throw new InvalidOperationException("DUST ADAPTER: " + message);
    }

    public static void Run(PlayerData data)
    {
        Require(data.Configuration.Match.Stations.Count == 4 && data.Fire.DustFrames.Count == 10,
            "Use the actual four-human fixture and authenticated ten-DIRT v2 generation.");
        var sink = new Sink();
        var host = new PlayerHost(data, sink, options: PreviewOptions.FromUrl("?source-fire=portable"));
        var rawCues = new List<SourceCallbackCue>();
        host.Bridge.SourcePresentationCueRaised += cue => {
            if (cue is SourceCallbackCue callback && cue.Kind == SourcePresentationCueKind.DustCallback)
                rawCues.Add(callback);
        };
        Require(sink.Textures.Count == 27 && sink.Textures.Skip(17).Select(t => t.Hash)
            .SequenceEqual(data.Fire.DustFrames.Select(t => t.SourceSha256)),
            "The real browser sink must upload the original ten DIRT payloads after the unchanged seventeen TRAIL frames.");
        var copies = new HashSet<long>(); var deleted = new HashSet<long>();
        long firstDust = -1, lastSourceTick = -1;
        int createdChildren = 0, entries = 0, rngRecords = 0, lastContinue = -1000;
        bool paused = false, skipped = false;
        for (int tick = 0; tick < 3600 && !(copies.Count > 0 && deleted.Count > 0 &&
            firstDust >= 0 && host.Game.SourceTick >= firstDust + 120 && paused && skipped); tick++)
        {
            var before = host.Game.SourceFireFrame;
            int[] buttons = new int[4];
            if (host.Game.State.Phase == GamePhase.PlaySelect && tick % 20 < 5)
                Array.Fill(buttons, (int)SourceButtons.B);
            if (host.Game.State.Phase == GamePhase.DeadBall && tick - lastContinue > 90)
            { host.Continue(0); lastContinue = tick; }
            host.Capture(new double[8], buttons);
            host.Advance(FixedStepBridge.StepSeconds, 1024, 768, 1024, 768, 1);
            var frame = host.Game.SourceFireFrame;
            if (host.Game.SourceTick == lastSourceTick)
            {
                skipped = true;
                Require(ReferenceEquals(before, frame), "A skipped host sample must retain the immutable source dust frame.");
                continue;
            }
            lastSourceTick = host.Game.SourceTick;
            if (frame == null) continue;
            createdChildren += frame.Records.Count(r => r.Kind == "CreateProcess" && r.Detail == "dustcld");
            entries += frame.Records.Count(r => r.Kind == "DustEntryCache");
            rngRecords += frame.Records.Count(r => r.Kind == "Rng" && r.Site.Function == "dust_cloud_proc");
            Require(sink.Slots.SequenceEqual(host.Fire.Draws.Select(p => p.TextureSlot)),
                "The actual C# bridge must send every real draw with the consumer-selected texture slot.");
            foreach (var particle in frame.Particles.Where(p => p.TextureKind == SourceFireTextureKind.Dust))
            {
                copies.Add(particle.ObjectSerial);
                if (firstDust < 0 && !particle.Hidden) firstDust = frame.SourceTick;
                Require(host.Fire.Draws.Any(p => p.ObjectSerial == particle.ObjectSerial && p.TextureSlot == 17 + particle.TextureFrame) == !particle.Hidden,
                    "A naturally emitted visible DIRT object must have a matching actual renderer packet; source-hidden objects must not draw.");
            }
            foreach (var record in frame.Records.Where(r => r.Kind == "DeleteObject" && r.Site.Function == "dust_cloud_proc" ||
                r.Kind == "ExitAlertDeleteObject"))
            {
                deleted.Add(record.ObjectSerial);
                Require(!frame.Particles.Any(p => p.ObjectSerial == record.ObjectSerial),
                    "A real dust deletion/exit alert must remove its renderer object in that exact source frame.");
            }
            if (copies.Count > 0 && !paused)
            {
                host.SetActive(false); host.Advance(2, 1024, 768, 1024, 768, 1);
                Require(host.Game.SourceTick == lastSourceTick && ReferenceEquals(frame, host.Game.SourceFireFrame),
                    "Public focus pause must freeze dust, source RNG and process stages without catch-up.");
                host.SetActive(true); paused = true;
            }
        }
        Require(rawCues.Count > 0 && copies.Count > 0 && deleted.Count > 0,
            "Public cabinet controls must naturally execute original contact/landing DustCallback and real DIRT creation/deletion.");
        Require(host.DustCallbacks == rawCues.Count && host.BoundDustCallbacks == rawCues.Count &&
            host.UnqualifiedVisualCallbacks == 0,
            "Every raw source DustCallback must be retained and genuinely bound, not hidden by zeroing an unqualified counter.");
        int expectedChildren = rawCues.Sum(c => c.Argument0 == 4 || c.Argument0 == 3 ? 1 :
            c.Argument0 == 1 || c.Argument0 == 2 ? 2 : 4);
        Require(createdChildren == expectedChildren && entries == rngRecords,
            "All 1/2/4 child emissions must preserve original callback order and one shared entry draw per actual process.");
        var random = host.Game.SourceRandomService as SourceInjectedRandom ??
            throw new InvalidOperationException("DUST ADAPTER: The actual Core shared random service must expose its real ledger.");
        var dustDraws = random.Trace.Where(r => r.Site.SourceIdentity == "player.c:dust_cloud_proc:9624").ToArray();
        Require(dustDraws.Length == entries && dustDraws.All(r => r.Site.Kind == SourceRandomKind.RandRange &&
            r.Site.BoundOrModulus == 3 && r.Provenance.Evidence == SourceRandomEvidence.ExternalUnverified),
            "Real dust entry must consume shared source randrng(3), retaining ExternalUnverified PRNG provenance.");
        Require(host.Game.SourceTick >= firstDust + 120 && paused && skipped && host.Game.State.Players.Count == 14,
            "Real four-human play must continue beyond dust while pause/57Hz sample retention remain correct.");
        long continued = host.Game.SourceTick;
        host.Restart(); host.Advance(0, 1024, 768, 1024, 768, 1);
        Require(host.Game.SourceTick == 0 && host.Game.SourceFireFrame == null && host.Fire.Draws.Count == 0 &&
            sink.Slots.Length == 0 && host.DustCallbacks == 0 && host.BoundDustCallbacks == 0,
            "Public restart must clear every dust process, object, packet, callback counter and epoch.");
        Console.WriteLine(JsonSerializer.Serialize(new {
            status = "passed", assertions, rawCallbacks = rawCues.Count, createdChildren, entries,
            naturalDirtObjects = copies.Count, naturalDeletions = deleted.Count,
            sharedRange3Draws = dustDraws.Length, firstDustSourceTick = firstDust, continuedSourceTick = continued,
            paused, skipped, publicRestartCleared = true,
            boundary = "Actual compiled production Core/browser-host controls and authenticated authoring data; not a browser/GPU or native witness."
        }));
    }

    private sealed class Sink : IRenderSink
    {
        internal readonly List<(string Id, string Hash)> Textures = new();
        internal int[] Slots = Array.Empty<int>();
        public void Texture(string id, int width, int height, byte[] rgba, int mips, bool repeat, bool linear, string hash, string origin)
        { if (id.StartsWith("fire/", StringComparison.Ordinal)) Textures.Add((id, hash)); }
        public void Mesh(string id, double[] vertices, int[] indices) { }
        public void WorldDraw(string mesh, string texture, string state) { }
        public void Model(int id, string joints) { }
        public void Appearance(int id, string metadata) { }
        public void FreeBall(string mesh, string texture, string alternateTexture, string metadata) { }
        public void Effects(double[] projection, double[] vertices, int[] textures, int[] flags, double[] flash, string metadata)
        { Slots = textures; }
        public void Present(double[] camera, double[] joints, int[] models, int[] appearances, double[] ball,
            double[] hudVertices, int[] hudColors, string[] hudTextures, int[] hudCounts, string status) { }
    }
}
