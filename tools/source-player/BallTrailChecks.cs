using System.Text.Json;
using Blitz.Contracts;
using Blitz.Gameplay;
using Blitz.Presentation;
using Blitz.Runtime;
using Blitz.SourcePlayer;

static class BallTrailChecks
{
    private static int assertions;
    private static void Require(bool condition, string message)
    {
        assertions++;
        if (!condition) throw new InvalidOperationException("BALL TRAIL ADAPTER: " + message);
    }

    public static void Run(PlayerData data)
    {
        var sink = new Sink();
        var host = new PlayerHost(data, sink, options: PreviewOptions.FromUrl("?source-fire=portable"));
        var untouched = KernelBinding.Create(data, PreviewOptions.FromUrl(""));
        Require(!untouched.SourceFireEnabled && SourceFreeBallBinding.CaptureTrails(untouched).Count == 0 &&
            SourceFreeBallBinding.Capture(untouched).Alpha == 255,
            "Default fire-off factory/main-ball defaults must remain unchanged and have no trail fallback.");
        Require(data.Configuration.Match.Stations.Count == 4,
            "This causal adapter proof must use the unchanged prepared four-human data.");
        host.Advance(0, 1024, 768, 1024, 768, 1);
        Require(sink.BallUploads == 1 && sink.BallMesh == "original/free-ball/limb_medball_obj_big" &&
            sink.BallIndices == 144, "Trail consumers must reuse the once-uploaded genuine medball2 mesh.");
        var copies = new Dictionary<long, SourceBallTrail>();
        var retired = new HashSet<long>();
        bool paused = false, flight = false, detached = false, skipped = false;
        long firstTick = -1, lastTick = -1;
        int lastContinue = -1000;
        for (int hostTick = 0; hostTick < 1800 && !(copies.Count >= 8 && retired.Count >= 5 &&
            host.Game.SourceTick >= firstTick + 120 && paused && skipped); hostTick++)
        {
            IGameState before = host.Game.State;
            SourceFreeBallDraw previousMain = SourceFreeBallBinding.Capture(host.Game);
            SourceFireRenderFrame previousEffects = host.Game.SourceFireFrame;
            var buttons = new int[4];
            if (before.Phase == GamePhase.PlaySelect && hostTick % 20 < 5)
                Array.Fill(buttons, (int)SourceButtons.B);
            if (before.Phase == GamePhase.DeadBall && hostTick - lastContinue > 90)
            { host.Continue(0); lastContinue = hostTick; }
            host.Capture(new double[8], buttons);
            host.Advance(FixedStepBridge.StepSeconds, 1024, 768, 1024, 768, 1);
            SourceFreeBallDraw main = SourceFreeBallBinding.Capture(host.Game);
            var draws = SourceFreeBallBinding.CaptureTrails(host.Game);
            Require(sink.Packet.Length == 20 * (draws.Count + 1) &&
                sink.Packet.Take(20).SequenceEqual(FrameMath.FreeBall(main)),
                "The actual ball bridge must retain the main20 packet byte-for-byte, followed by every ghost matrix.");
            JsonElement state = sink.State;
            var described = state.GetProperty("ballTrails").EnumerateArray().ToArray();
            Require(described.Length == draws.Count, "The real snapshot and real GPU packet must expose the same complete trail inventory.");
            SourceFireRenderFrame frame = host.Game.SourceFireFrame;
            if (host.Game.SourceTick == lastTick)
            {
                skipped = true;
                Require(ReferenceEquals(previousEffects, frame), "A skipped60-to57 host frame must hold football stages, not age them.");
            }
            lastTick = host.Game.SourceTick;
            for (int i = 0; i < draws.Count; i++)
            {
                SourceFreeBallDraw draw = draws[i];
                SourceBallTrail trail = draw.Trail!.Value;
                Require(sink.Packet.Skip(20 * (i + 1)).Take(20).SequenceEqual(FrameMath.FreeBall(draw)) &&
                    described[i].GetProperty("objectSerial").GetInt64() == trail.ObjectSerial &&
                    described[i].GetProperty("alpha").GetInt32() == draw.Alpha &&
                    described[i].GetProperty("entry").GetProperty("sourceTick").GetInt64() == trail.EntrySample.SourceTick,
                    "Every actual appended ghost packet must retain its matching immutable snapshot serial/entry/stage.");
                if (!copies.TryGetValue(trail.ObjectSerial, out SourceBallTrail entry))
                {
                    copies.Add(trail.ObjectSerial, trail);
                    Require(trail.Stage == 0 && trail.EntrySample.SourceTick == host.Game.SourceTick &&
                        previousMain.State.SourceTick == host.Game.SourceTick - 1 &&
                        trail.EntrySample.WorldPosition.X == previousMain.State.Position.X &&
                        trail.EntrySample.WorldPosition.Y == previousMain.State.Position.Y &&
                        trail.EntrySample.WorldPosition.Z == previousMain.State.Position.Z &&
                        trail.EntrySample.Heading == previousMain.State.Heading &&
                        trail.EntrySample.Elevation == previousMain.State.Elevation &&
                        trail.EntrySample.Roll == previousMain.State.Roll,
                        "Actual effects-before-host level0 entry must copy all previous completed ball fields before its next ball wake.");
                }
                else
                    Require(trail.EntrySample.WorldPosition.Equals(entry.EntrySample.WorldPosition) &&
                        trail.EntrySample.Heading == entry.EntrySample.Heading &&
                        trail.EntrySample.Elevation == entry.EntrySample.Elevation && trail.EntrySample.Roll == entry.EntrySample.Roll,
                        "Actual Core ghosts must never track later live positions/orientations.");
                Require(draw.Alternate && draw.Alpha == 215 - 15 * trail.Stage &&
                    draw.State.Scale == new[] { 2.25f, 2.15f, 2.05f, 1.95f, 1.85f }[trail.Stage],
                    "Actual Core-to-preview ghosts must retain original ALT, all five alphas and source scales.");
                detached |= draw.State.Position.X != main.State.Position.X || draw.State.Position.Y != main.State.Position.Y ||
                    draw.State.Position.Z != main.State.Position.Z;
            }
            if (frame != null)
                foreach (SourceFireRecord record in frame.Records.Where(r => r.Kind == "DeleteObject" && r.Site.Function == "trail_ball_proc"))
                {
                    retired.Add(record.ObjectSerial);
                    Require(!draws.Any(d => d.Trail!.Value.ObjectSerial == record.ObjectSerial),
                        "A naturally deleted Core football ghost must disappear from its actual renderer packet.");
                }
            if (draws.Any(d => d.State.Visible) && host.Game.State.Phase == GamePhase.LiveBall &&
                host.Game.State.Ball.Mode == BallMode.InFlight)
            {
                flight = true;
                if (firstTick < 0) firstTick = host.Game.SourceTick;
                if (!paused)
                {
                    host.SetActive(false);
                    host.Advance(2, 1024, 768, 1024, 768, 1);
                    Require(host.Game.SourceTick == lastTick && ReferenceEquals(frame, host.Game.SourceFireFrame),
                        "Actual public pause must hold naturally produced football stages without catch-up.");
                    host.SetActive(true); paused = true;
                }
            }
        }
        Require(flight && detached && copies.Count >= 8 && retired.Count >= 5 &&
            host.Game.SourceTick >= firstTick + 120 && paused && skipped,
            "Public cabinet/host input must naturally produce non-fire flight, static ghosts, deletion and continuing gameplay.");
        Require(sink.BallUploads == 1, "Ghost lifecycle must never re-upload or replace the original static geometry.");
        SourceFireRenderFrame old = host.Game.SourceFireFrame;
        host.Restart();
        host.Advance(0, 1024, 768, 1024, 768, 1);
        Require(host.Epoch == 1 && host.Game.SourceTick == 0 && host.Game.SourceFireFrame == null &&
            SourceFreeBallBinding.CaptureTrails(host.Game).Count == 0 &&
            sink.State.GetProperty("ballTrails").GetArrayLength() == 0 && sink.Packet.Length == 20,
            "Actual public host Restart must discard all ghost processes, objects, packets and source epochs.");
        Require(old != null && old.SourceTick > 0, "Restart must not mutate the previously published immutable source frame.");
        Console.WriteLine(JsonSerializer.Serialize(new {
            status = "passed", ballTrailAdapterAssertions = assertions, naturalGhosts = copies.Count,
            deletedGhosts = retired.Count, firstFlightSourceTick = firstTick, continuedSourceTick = lastTick,
            source57SkippedFrameHeld = skipped, pausedStageHeld = paused, publicRestartCleared = true,
            boundary = "Actual compiled production Core/preview composition with public cabinet/host APIs; not a browser, Unity or native-pixel witness."
        }));
    }

    private sealed class Sink : IRenderSink
    {
        public double[] Packet = Array.Empty<double>();
        public JsonElement State;
        public int BallUploads, BallIndices;
        public string? BallMesh;
        public void Texture(string id, int width, int height, byte[] rgba, int mipCount, bool repeat, bool linear, string sha256, string origin) { }
        public void Mesh(string id, double[] vertices, int[] indices)
        { if (id == "original/free-ball/limb_medball_obj_big") { BallUploads++; BallIndices = indices.Length; } }
        public void WorldDraw(string mesh, string texture, string state) { }
        public void Model(int id, string joints) { }
        public void Appearance(int id, string metadata) { }
        public void FreeBall(string mesh, string texture, string alternateTexture, string metadata) { BallMesh = mesh; }
        public void Effects(double[] projection, double[] vertices, int[] textures, int[] rawFlags, double[] flashAlpha, string metadata) { }
        public void Present(double[] camera, double[] joints, int[] models, int[] appearances, double[] ball,
            double[] hudVertices, int[] hudColors, string[] hudTextures, int[] hudCounts, string status)
        {
            Packet = ball;
            using var document = JsonDocument.Parse(status);
            State = document.RootElement.Clone();
        }
    }
}
