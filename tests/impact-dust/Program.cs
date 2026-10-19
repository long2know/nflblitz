using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Blitz.AssetImport;
using Blitz.Contracts;
using Blitz.Presentation;
using Blitz.Runtime;

internal static class DustChecks
{
    private static int assertions;
    private static SourceFireSpfxBundle data;
    private static float[] originalQuarterSines;
    private static readonly List<object> comparisons = new List<object>();
    private static readonly List<object> emissions = new List<object>();
    private static object renderComparison;
    private static readonly List<object> lifecycleComparisons = new List<object>();

    private static void Check(bool value, string message)
    {
        assertions++;
        if (!value) throw new InvalidOperationException("DUST ASSERTION: " + message);
    }

    private static void Near(float actual, float expected, string name)
    { Check(Math.Abs(actual - expected) < .00003f, name); }

    private sealed class Host
    {
        internal readonly SourceFireSpfxRuntime Runtime;
        internal readonly List<SourceFireRandomRequest> Requests = new List<SourceFireRandomRequest>();
        internal readonly List<int> DrawValues = new List<int>();
        internal readonly List<long> Wakes = new List<long>();
        internal long Tick = -1, NextSerial = 100;
        internal int Mode = SourceFireIds.InPlay, RandomValue, Facing = 137;
        internal float CacheOffset;
        internal bool MissingCache;
        internal Host(SourceFireSpfxBundle bundle = null)
        {
            Runtime = new SourceFireSpfxRuntime(bundle ?? data);
            Runtime.Reset(Bootstrap());
        }
        internal static SourceFireBootstrap Bootstrap()
        {
            return new SourceFireBootstrap(SourceFireOrdering.PortableDeclaredSlots,
                SourceFireClockOrigin.PortableLogicalEpoch,
                SourceFireStalePolicy.PortableCancelDeletedAndFreezeOrphan, 0, 0, "declared synthetic component epoch");
        }
        internal SourceFireHostCallbacks Callbacks()
        {
            return new SourceFireHostCallbacks(r => {
                Requests.Add(r); DrawValues.Add(RandomValue);
                Check(r.Site.SourceTick == Tick, "entry RNG must execute in the actual effect wake");
                return RandomValue;
            }, () => Mode, actor => {
                if (MissingCache) return null;
                var joints = new SourceVector3[15];
                for (int i = 0; i < joints.Length; i++)
                    joints[i] = new SourceVector3(i + 1 + CacheOffset, i + 2, i + 3);
                return new SourceFireCachedPlayerFrame(actor, Math.Max(0, Tick - 1),
                    Camera(Math.Max(0, Tick - 1)), joints, "declared synthetic cached camera joints");
            }, () => new SourceFireBallWorldSample(Tick, new SourceVector3(1, 2, 100), "unused synthetic ball"),
                a => 0, (a, f, s) => { }, s => Wakes.Add(s.NativeProcessSerial), (s, o) => { },
                s => new SourceFireSoundSubmission(false, "unused synthetic sound"), () => ++NextSerial,
                () => new SourceFireBallRenderSample(Tick, new SourceVector3(1, 2, 100), 0, 0, 0, 1, 0,
                    "synthetic actual entry sample"), actor => Velocity(Facing));
        }
        internal void Step()
        {
            Tick++;
            Runtime.BeginSourceTick(Runtime.PortableHeadThenHostTick(Tick, (int)Tick,
                new SourceFireWakeSlot[0]), Callbacks());
        }
        internal SourceFireSite Site()
        { return new SourceFireSite(Tick, (int)Tick, 1, 0x305, 30, 0, 5, "plyrseq.c", "declared_callback", 1); }
        internal long Spawn(int joint = 10, int sleep = 1)
        {
            try { return Runtime.Apply(SourceFireCommand.Create(Site(), (SourceFireEntry)5, new SourceFireArgs(5, joint, sleep))); }
            catch (SourceFireBoundaryException error)
            {
                throw new InvalidOperationException("DUST ASSERTION: actual source-effect consumer must admit dustcld, not leave DustCallback unqualified: " + error.Message);
            }
        }
        internal SourceFireRenderFrame Frame()
        { return Runtime.CaptureFrame(new SourceFireRenderBasis(Camera(Tick), 384, SourceFireCull.Off)); }
        internal void Control(SourceFireOperation operation, int id = 0, int mask = -1)
        { Runtime.Apply(SourceFireCommand.Control(Site(), operation, id, mask)); }
    }

    private static SourceFireCameraFrame Camera(long tick)
    {
        return new SourceFireCameraFrame(tick, new SourceVector3(10, 20, 30),
            new float[] { .6f, 0, .8f, 0, 0, 1, 0, 0, -.8f, 0, .6f, 0 }, "declared synthetic paired camera");
    }
    private static SourceVector3 Velocity(int heading)
    {
        if (originalQuarterSines == null) return heading == 137 ?
            new SourceVector3(.2f, .10f, -.3f) : new SourceVector3(-.3f, .10f, .2f);
        return new SourceVector3(.35f * Sin(heading) + 0f * Sin(heading + 256), .10f,
            .35f * Sin(heading + 256) - 0f * Sin(heading));
    }
    private static float Sin(int heading)
    {
        int t = heading & 1023;
        return t <= 256 ? originalQuarterSines[t] : t <= 512 ? originalQuarterSines[512 - t] :
            t <= 768 ? -originalQuarterSines[t - 512] : -originalQuarterSines[1024 - t];
    }
    private static SourceFireSpfxBundle Synthetic(bool dust = true)
    {
        var fire = new SourceFireTextureFrame[17]; var dirt = new SourceFireTextureFrame[dust ? 10 : 0];
        for (int i = 0; i < fire.Length; i++)
            fire[i] = new SourceFireTextureFrame(32, 32, 3, "synthetic/TRAIL" + i, new string('a', 64), new byte[4096]);
        for (int i = 0; i < dirt.Length; i++)
            dirt[i] = new SourceFireTextureFrame(32, 32, 3, "synthetic/DIRT" + i, i.ToString().PadLeft(64, '0'), new byte[4096]);
        var geometry = new SourceFireGeometry(new[] {
            new SourceVector3(-4, -4, 0), new SourceVector3(4, -4, 0),
            new SourceVector3(4, 4, 0), new SourceVector3(-4, 4, 0) },
            new float[] { 0, 0, 1, 0, 1, 1, 0, 1 }, new[] { 0, 1, 2, 0, 2, 3 }, new[] { 0, 1, 2, 0, 2, 3 });
        return new SourceFireSpfxBundle(new string('b', 64), "synthetic asset-free contract input", geometry, fire, dirt);
    }

    private static void Emissions()
    {
        foreach (int arg in new[] { 4, 3, 1, 2, 0, -1, 19 })
        {
            var h = new Host(); h.Step();
            int[] expected = arg == 4 ? new[] { 10 } : arg == 3 ? new[] { 13 } :
                arg == 1 ? new[] { 14, 11 } : arg == 2 ? new[] { 13, 10 } : new[] { 8, 0, 13, 10 };
            var commands = SourceFireCommand.DustCloud(h.Site(), arg);
            emissions.Add(new { argument = arg, requests = commands.Select(c => new {
                args = new[] { c.Args.A0, c.Args.A1, c.Args.A2, c.Args.A3 }, group = c.GroupId, line = c.Site.SourceLine }).ToArray() });
            Check(commands.Select(c => c.Args.A1).SequenceEqual(expected), "helper must preserve original 1/2/4 qcreate joint order");
            foreach (var c in commands)
            {
                Check(c.ProcessName == "dustcld" && c.GroupId == 0xf98 && c.Args.A0 == 5 &&
                    c.Args.A2 == 1 && c.Args.A3 == 0, "helper arguments and SPFX_PID must match original");
                h.Runtime.Apply(c);
            }
            Check(h.Requests.Count == 0 && h.Frame().Particles.Count == 0, "qcreate must not sample pose or consume RNG inline");
            Check(h.Runtime.Processes.All(p => p.BirthTick == 0 && p.SleepField == 1), "birth must queue default level-zero sleep1");
            long[] serials = h.Runtime.Processes.Select(p => p.Serial).ToArray();
            h.Step();
            Check(h.Wakes.SequenceEqual(serials) && h.Requests.Count == expected.Length,
                "head insertion must visit reversed child births and one shared range3 draw per entry");
            Check(h.Frame().Particles.Count == expected.Length &&
                h.Frame().Particles.All(p => p.TextureKind == SourceFireTextureKind.Dust && p.TextureFrame == 0),
                "all queued dust children must create original DIRT0 objects");
        }
    }

    private static void Timing()
    {
        foreach (int initial in new[] { 0, 1, 2 })
        foreach (int sleep in new[] { 0, 1, 2 })
        foreach (int preSnap in new[] { -1, 0, 4 })
        {
            var h = new Host { RandomValue = initial };
            h.Step(); h.Spawn(10, sleep);
            var rows = new List<object>();
            int i = initial, j = 0, frame = 0;
            bool alive = true;
            SourceVector3 velocity = Velocity(h.Facing);
            float x = 11 * .6f + 13 * -.8f + 10, y = 0, z = 11 * .8f + 13 * .6f + 30;
            for (int wake = 0; wake < 34; wake++)
            {
                h.Mode = preSnap >= 0 && wake >= preSnap ? SourceFireIds.PreSnap : SourceFireIds.InPlay;
                h.Step();
                if (alive)
                {
                    if (i >= 10) alive = false;
                    else
                    {
                        if (j > sleep) { j = 0; frame = i++; }
                        x += velocity.X; y += velocity.Y; z += velocity.Z;
                        if (h.Mode == SourceFireIds.PreSnap) alive = false;
                        j++;
                    }
                }
                var particles = h.Frame().Particles;
                Check((particles.Count == 1) == alive, "original j>sleep_time/next-wake deletion and pre-snap visibility");
                Check(h.Requests.Count == 1 && h.Requests[0].Bound == 3 &&
                    h.Requests[0].Site.Function == "dust_cloud_proc" && h.Requests[0].Site.SourceLine == 9624,
                    "exactly one shared randrng(3) must occur at original entry line9624");
                if (alive)
                {
                    SourceFireParticle p = particles[0];
                    Check(p.TextureFrame == frame, "initial DIRT0 then original j>sleep_time texture timing");
                    Near(p.WorldPosition.X, x, "entry cached camera transpose and frozen X velocity");
                    Near(p.WorldPosition.Y, y, "dust starts y0 and moves before pre-snap");
                    Near(p.WorldPosition.Z, z, "entry cached camera transpose and frozen Z velocity");
                    Check(h.Runtime.Processes.Single().SleepField == 1, "every live dust iteration sleeps exactly one source wake");
                }
                if (wake == 0 && preSnap == 0)
                {
                    var kinds = h.Runtime.CurrentRecords.Select(r => r.Kind).ToArray();
                    Check(Array.IndexOf(kinds, "CreateObject") < Array.IndexOf(kinds, "Rng") &&
                        Array.IndexOf(kinds, "Rng") < Array.IndexOf(kinds, "DustStage") &&
                        Array.IndexOf(kinds, "DustStage") < Array.IndexOf(kinds, "DeleteObject") && !kinds.Contains("Sleep"),
                        "early pre-snap must still enter/create/draw/move/delete without an invented visible frame");
                }
                rows.Add(new { tick = wake, alive, frame = alive ? particles[0].TextureFrame : -1,
                    xyz = alive ? new[] { particles[0].WorldPosition.X, particles[0].WorldPosition.Y, particles[0].WorldPosition.Z } : new float[0],
                    sleep = h.Runtime.Processes.Count == 0 ? 0 : h.Runtime.Processes[0].SleepField });
            }
            comparisons.Add(new { initial, sleep, preSnap, heading = h.Facing, rows });
        }
    }

    private static void EntryAndCleanup()
    {
        var h = new Host { RandomValue = 2 }; h.Step(); h.Spawn();
        h.CacheOffset = 40; h.Step();
        SourceFireParticle entry = h.Frame().Particles.Single();
        Near(entry.WorldPosition.X, 51 * .6f - 13 * .8f + 10 + Velocity(137).X,
            "entry must read the first-wake cache, not the creator or current render pose");
        Check(entry.TextureFrame == 0, "random entry i must not select the initial texture");
        h.CacheOffset = 400; h.Facing = 769; h.Step();
        SourceFireParticle next = h.Frame().Particles.Single();
        Near(next.WorldPosition.X, entry.WorldPosition.X + Velocity(137).X,
            "entry position and facing velocity must not follow current actor/camera");
        h.Control(SourceFireOperation.SuspendMask, 0, 0x8000); h.Step();
        Check(h.Frame().Particles[0].WorldPosition.Equals(next.WorldPosition) &&
            h.Runtime.Processes[0].SleepField == 0x8001 && h.Requests.Count == 1,
            "suspend preserves exact dust continuation without movement or RNG");
        h.Control(SourceFireOperation.ResumeMask, SourceFireIds.Spfx); h.Step();
        Check(h.Frame().Particles[0].TextureFrame == 2, "resume must retain j and advance the original texture stage");
        h.Control(SourceFireOperation.KillProcessGroup, SourceFireIds.Spfx);
        Check(h.Runtime.Processes.Count == 0 && h.Frame().Particles.Count == 0 &&
            h.Runtime.CurrentRecords.Count(r => r.Kind == "ExitAlertDeleteObject") == 1,
            "kill must invoke dust_cloud_die_alert and delete its still-existing object exactly once");
        Check(h.Runtime.CurrentRecords.Single(r => r.Kind == "KillProcess").Value == 2,
            "dust kill cause must use actual Goose PD_WIPEOUT2, not an assumed symbolic zero");
        lifecycleComparisons.Add(new { name = "kill-live", alive = h.Frame().Particles.Count > 0,
            deletes = h.Runtime.CurrentRecords.Count(r => r.Kind == "ExitAlertDeleteObject"), entryRandom = h.Requests.Count });
        var deleted = new Host(); deleted.Step(); deleted.Spawn(); deleted.Step();
        deleted.Control(SourceFireOperation.DeleteObjectGroup, SourceFireIds.PuffObject);
        deleted.Control(SourceFireOperation.KillProcessGroup, SourceFireIds.Spfx);
        Check(!deleted.Runtime.CurrentRecords.Any(r => r.Kind == "ExitAlertDeleteObject"),
            "exit alert must not double-delete an already deleted object");
        lifecycleComparisons.Add(new { name = "delete-then-kill", alive = deleted.Frame().Particles.Count > 0,
            deletes = deleted.Runtime.CurrentRecords.Count(r => r.Kind == "DeleteObjectGroup" || r.Kind == "ExitAlertDeleteObject"),
            entryRandom = deleted.Requests.Count });
        var birth = new Host(); birth.Step(); birth.Spawn();
        birth.Control(SourceFireOperation.KillProcessGroup, SourceFireIds.Spfx);
        Check(birth.Requests.Count == 0 && birth.Frame().Particles.Count == 0,
            "kill-before-entry must not create an object or consume entry RNG");
        lifecycleComparisons.Add(new { name = "kill-before-entry", alive = birth.Frame().Particles.Count > 0,
            deletes = birth.Runtime.CurrentRecords.Count(r => r.Kind == "DeleteObjectGroup" || r.Kind == "ExitAlertDeleteObject"),
            entryRandom = birth.Requests.Count });
        var reset = new Host(); reset.Step(); reset.Spawn(); reset.Step(); var retained = reset.Frame();
        reset.Runtime.Reset(Host.Bootstrap());
        Check(reset.Runtime.Processes.Count == 0 && retained.Particles.Count == 1,
            "reset clears every owned process/object without mutating published frames");
        var missing = new Host(Synthetic(false)); missing.Step(); missing.Spawn();
        bool rejected = false;
        try { missing.Step(); }
        catch (SourceFireBoundaryException e) { rejected = e.Message.Contains("SOURCE_DUST_DATA_REQUIRED"); }
        Check(rejected, "v1/missing dust must produce named SOURCE_DUST_DATA_REQUIRED, not succeed with smoke");
        var cache = new Host { MissingCache = true }; cache.Step(); cache.Spawn();
        rejected = false;
        try { cache.Step(); }
        catch (SourceFireBoundaryException e) { rejected = e.Message.Contains("matching cambot"); }
        Check(rejected, "missing actual entry pose must fail, never synthesize joints");
    }

    private static void Render()
    {
        var h = new Host(); h.Step(); h.Spawn(); h.Step();
        var consumer = new SourceFireSpfxFrameConsumer(data); var frame = h.Frame();
        SourceFireDrawPacket packet = consumer.Consume(frame).Single();
        renderComparison = new { cameraVertices = packet.CameraVertices.Select(p => new[] { p.X, p.Y, p.Z }).ToArray(),
            packet.TextureFrame, packet.TextureSlot, packet.TextureSourceSha256 };
        Check(packet.TextureKind == SourceFireTextureKind.Dust && packet.TextureSlot == 17 &&
            packet.TextureSourceSha256 == data.DustFrames[0].SourceSha256,
            "real frame consumer must bind original DIRT, not TRAIL or a counter");
        SourceVector3 translation = SourceFireSpfxFrameConsumer.CameraTranslation(frame.Particles[0].WorldPosition, frame.Basis.Camera);
        for (int i = 0; i < 6; i++)
        {
            SourceVector3 p = data.Geometry.Positions[data.Geometry.VertexIndices[i]];
            Near(packet.CameraVertices[i].X, p.Y + translation.X, "draw_spfx fixed billboard X basis");
            Near(packet.CameraVertices[i].Y, p.X + translation.Y, "draw_spfx fixed billboard Y basis");
            Near(packet.CameraVertices[i].Z, -p.Z + translation.Z, "draw_spfx fixed billboard Z basis");
        }
        Check(packet.State.DepthFunction == "ALWAYS" && packet.State.DepthWrite &&
            packet.State.Wrap == "WRAP/WRAP" && packet.State.BlendSourceRgb == "SRC_ALPHA" &&
            packet.State.BlendSourceAlpha == "SRC_ALPHA", "draw_spfx depth/write/wrap and separate source-alpha blends");
        var texel = new SourceFireRgba(.8f, .4f, .2f, .5f); var combined = SourceFireMaterialState.Combine(texel);
        Check(combined.R == .4f && combined.A == .5f, "draw_spfx RGB times texture alpha before SRC_ALPHA blend");
        Check(ReferenceEquals(consumer.Consume(frame), consumer.Draws), "render-only resampling must not age or replace dust");
        h.Control(SourceFireOperation.HideAllObjects); h.Step();
        Check(consumer.Consume(h.Frame()).Count == 0, "hidden dust object must not draw");
    }

    public static int Main(string[] args)
    {
        try
        {
            data = Synthetic();
            if (args.Length == 4)
            {
                data = SourceFireSpfxReader.ReadAuthorized(Path.GetFullPath(args[0]), "manifest.json", args[1]);
                originalQuarterSines = JsonSerializer.Deserialize<float[]>(File.ReadAllText(args[2]));
                Check(originalQuarterSines.Length == 257, "all original direction table entries required for component comparison");
            }
            Emissions(); Timing(); EntryAndCleanup(); Render();
            if (args.Length == 4)
                File.WriteAllText(args[3], JsonSerializer.Serialize(new { assertions, comparisons, emissions, renderComparison, lifecycleComparisons,
                    textures = data.DustFrames.Select(f => new { f.SourcePath, f.SourceSha256,
                        rgbaSha256 = AuthorizedAssetRoot.Sha256(f.CopyRgba()) }).ToArray() }));
            Console.WriteLine("DUST RESULT assertions=" + assertions + " failures=0");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error.Message);
            Console.WriteLine("DUST RESULT assertions=" + assertions + " failures=1");
            return 1;
        }
    }
}
