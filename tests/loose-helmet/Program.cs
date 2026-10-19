using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Blitz.AssetImport;
using Blitz.Contracts;
using Blitz.Gameplay;
using Blitz.Presentation;
using Blitz.Runtime;

internal static class HelmetChecks
{
    private static int assertions;
    private static float[] quarter = Enumerable.Range(0, 257).Select(i => (float)Math.Sin(i * Math.PI / 512)).ToArray();
    private static readonly List<object> helperRows = new List<object>();
    private const string Admission = "actual source-effect consumer must admit loosehlm";
    private const string Entry = "first wake must invert cached neck8 with its paired camera and launch in world axes";
    private const string Motion = "every source wake must preserve original float stores, integer fields, bounce tape and spin";

    private static void Check(bool condition, string message)
    {
        assertions++;
        if (!condition) throw new InvalidOperationException("HELMET ASSERTION: " + message);
    }
    private static int Bits(float value) { return BitConverter.ToInt32(BitConverter.GetBytes(value), 0); }
    private static int[] Bits(SourceVector3 value) { return new[] { Bits(value.X), Bits(value.Y), Bits(value.Z) }; }
    private static float Sin(int angle)
    {
        int t = angle & 1023;
        return t <= 256 ? quarter[t] : t <= 512 ? quarter[512 - t] : t <= 768 ? -quarter[t - 512] : -quarter[1024 - t];
    }
    private static int Asin(float value)
    {
        float x = Math.Abs(value);
        int i = 0;
        while (i < 255 && quarter[i + 1] < x) i++;
        float ld = quarter[i] - x, hd = quarter[i + 1] - x;
        ld *= ld; hd *= hd;
        int result = hd < ld ? i + 1 : i;
        return value < 0 ? -result : result;
    }
    private static SourceFireCameraFrame Camera(long tick)
    {
        return new SourceFireCameraFrame(tick, new SourceVector3(10, 20, 30),
            new float[] { .6f, 0, .8f, 0, 0, 1, 0, 0, -.8f, 0, .6f, 0 }, "synthetic paired camera");
    }
    private static SourceFireSpfxBundle Synthetic(bool helmet = true)
    {
        var geometry = new SourceFireGeometry(new[] {
            new SourceVector3(-4, -4, 0), new SourceVector3(4, -4, 0), new SourceVector3(4, 4, 0) },
            new float[] { 0, 0, 1, 0, 1, 1 }, new[] { 0, 1, 2 }, new[] { 0, 1, 2 });
        var textures = Enumerable.Range(0, 17).Select(i => new SourceFireTextureFrame(32, 32, 3,
            "synthetic/TRAIL" + i, new string('a', 64), new byte[4096])).ToArray();
        return new SourceFireSpfxBundle(new string('b', 64), "declared synthetic asset-free data", geometry,
            textures, new SourceFireTextureFrame[0], helmet ? geometry : null);
    }
    private sealed class Host
    {
        internal readonly SourceFireSpfxRuntime Runtime;
        internal readonly List<SourceFireRandomRequest> Requests = new List<SourceFireRandomRequest>();
        internal readonly List<object> Random = new List<object>();
        internal readonly List<SourceFireDirectSound> Sounds = new List<SourceFireDirectSound>();
        internal readonly List<long> Wakes = new List<long>();
        internal long Tick = -1, Serial = 100;
        internal int EntryValue = 45, Mode = 2, BallFlags = 0, Origin = 120, CacheReads;
        internal float NeckY = 10, Offset;
        internal Host(bool helmet = true)
        {
            Runtime = new SourceFireSpfxRuntime(Synthetic(helmet));
            Runtime.Reset(new SourceFireBootstrap(SourceFireOrdering.PortableDeclaredSlots, SourceFireClockOrigin.PortableLogicalEpoch,
                SourceFireStalePolicy.PortableCancelDeletedAndFreezeOrphan, 0, Origin, "synthetic declared epoch"));
        }
        internal SourceFireHostCallbacks Callbacks()
        {
            return new SourceFireHostCallbacks(r =>
            {
                int value = r.Bound == 100 ? EntryValue : (Requests.Count * 17 + 11) % r.Bound;
                Requests.Add(r);
                Random.Add(new { tick = Tick, bound = r.Bound, value, line = r.Site.SourceLine });
                return value;
            }, () => Mode, actor =>
            {
                CacheReads++;
                long produced = Math.Max(0, Tick - 1);
                var joints = Enumerable.Range(0, 15).Select(i => new SourceVector3(i + 1 + Offset, i == 8 ? NeckY : i + 2, i + 3)).ToArray();
                return new SourceFireCachedPlayerFrame(actor, produced, Camera(produced), joints, "synthetic retained jpos15");
            }, () => new SourceFireBallWorldSample(Tick, new SourceVector3(1, 2, 3), "unused synthetic ball"),
                actor => 0, (actor, flags, site) => { }, site => Wakes.Add(site.NativeProcessSerial), (slot, ordinal) => { },
                sound => { Sounds.Add(sound); return new SourceFireSoundSubmission(false, "ordered synthetic cue journal, no PCM"); },
                () => ++Serial, () => new SourceFireBallRenderSample(Tick, new SourceVector3(1, 2, 3), 0, 0, 0, 1, BallFlags,
                    "current synthetic global ball flags"), null, Sin,
                actor => new SourceLooseHelmetDecal("runtime/TESTLOD.WMS", new string('c', 64), "synthetic/team.wms", 128, 256));
        }
        internal void Step()
        {
            Tick++;
            Runtime.BeginSourceTick(Runtime.PortableHeadThenHostTick(Tick, unchecked(Origin + (int)Tick),
                new SourceFireWakeSlot[0]), Callbacks());
        }
        internal SourceFireSite Site()
        { return new SourceFireSite(Tick, unchecked(Origin + (int)Tick), 1, 0, 30, 0, 5, "player.c", "synthetic_creator", 1); }
        internal long Spawn()
        {
            try { return Runtime.Apply(SourceFireCommand.Create(Site(), (SourceFireEntry)6, new SourceFireArgs(5, 0, 0))); }
            catch (SourceFireBoundaryException error) { throw new InvalidOperationException("HELMET ASSERTION: " + Admission + ": " + error.Message); }
        }
        internal void Control(SourceFireOperation op, int group = 0)
        { Runtime.Apply(SourceFireCommand.Control(Site(), op, group)); }
        internal SourceFireRenderFrame Frame()
        { return Runtime.CaptureFrame(new SourceFireRenderBasis(Camera(Tick), 384, SourceFireCull.Off)); }
        internal SourceLooseHelmet Helmet() { return Frame().LooseHelmets.Single(); }
        internal object Storage { get { return typeof(SourceFireSpfxRuntime).GetField("helmet", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(Runtime); } }
        internal void Set(string field, object value) { Storage.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(Storage, value); }
        internal T Get<T>(string field) { return (T)Storage.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(Storage); }
    }
    private sealed class Reference
    {
        internal float X, Y, Z, Vx, Vy, Vz, Tx, Ty, Tz, Scale = 2;
        internal int Fwd, Fwd2, Phi, Pitch, Flight, Flags, Bounces, BounceTime, Draws, EntryValue;
        internal Reference(int entry, float neckY, int flags = 0, int phi = 0)
        {
            EntryValue = entry; Flags = flags; Phi = phi;
            X = 9 * .6f + 10 * 0f + 11 * -.8f + 10;
            Y = 9 * 0f + neckY * 1f + 11 * 0f + 20;
            Z = 9 * .8f + 10 * 0f + 11 * .6f + 30;
            Vx = Vz = entry < 45 ? -.35f : .35f; Vy = 1.6f; Draws = 1;
        }
        private int Draw(int bound) { return (Draws++ * 17 + 11) % bound; }
        internal void Step(int pcount)
        {
            X += Vx; Y += Vy + -.08f / 2f; Z += Vz; Vy += -.08f;
            if (Y <= .9f * Scale)
            {
                Bounces++; Flags |= 0x20;
                Y = .9f * Scale; BounceTime = pcount;
                Vy = (float)(Vy * -0.5d); Vy = (float)(Vy - 0.10d);
                if (Vy <= 0) { Vx = Vy = Vz = 0; Tx = X; Ty = 0; Tz = Z; }
                else
                {
                    float speed = (float)Math.Sqrt(Vx * Vx + Vz * Vz);
                    int theta = speed > 0 ? Asin(Vz / speed) : 0;
                    if (Vx < 0) theta = 512 - theta;
                    int[] signs = { 1, 1, 1, -1, -1, -1 }, bounds = { 201, 157, 157, 201, 157, 157 };
                    for (int i = 0; i < bounds.Length; i++) theta += signs[i] * Draw(bounds[i]);
                    Vx = .5f * speed * Sin(theta + 256); Vz = .5f * speed * Sin(theta);
                    float time = -2f * Vy / -.08f;
                    Tx = X + time * Vx; Ty = 0; Tz = Z + time * Vz;
                }
            }
            Flight++;
            float horizontal = (float)Math.Sqrt(Vx * Vx + Vz * Vz);
            float magnitude = (float)Math.Sqrt(Vx * Vx + Vy * Vy + Vz * Vz);
            if (Vx == 0 && Vz == 0) Fwd2 = 0;
            else
            {
                Fwd = Asin(Vx / horizontal);
                if (Vz < 0) Fwd = (512 - Fwd) & 1023;
                if ((Flags & 2) != 0) Fwd2 = Asin(Vy / magnitude);
                else { Pitch += 40; Fwd2 = Pitch % 1024; }
                Phi += Draw(35) + 30;
            }
        }
        internal void Compare(SourceLooseHelmet actual, int requests)
        {
            Check(Bits(actual.Position).SequenceEqual(Bits(new SourceVector3(X, Y, Z))) &&
                Bits(actual.Velocity).SequenceEqual(Bits(new SourceVector3(Vx, Vy, Vz))) &&
                Bits(actual.Target).SequenceEqual(Bits(new SourceVector3(Tx, Ty, Tz))) &&
                actual.Heading == Fwd && actual.Elevation == Fwd2 && actual.Roll == Phi &&
                actual.Pitch == Pitch && actual.PitchRate == 40 && actual.Alpha == 255 && actual.Scale == 2 &&
                actual.FlightTime == Flight && actual.RawFlags == Flags && actual.Bounces == Bounces &&
                actual.BounceTime == BounceTime && requests == Draws, Motion);
        }
    }
    private static void AdmissionAndEntry()
    {
        var h = new Host(); h.Step(); long first = h.Spawn();
        Check(h.Runtime.Processes.Single().GroupId == 0x950 && h.Runtime.Processes.Single().SleepField == 1 &&
            h.Frame().LooseHelmets.Count == 0 && h.Requests.Count == 0 && h.CacheReads == 0,
            "qcreate must insert default level0/sleep1 without sampling cache or duplicating probability RNG");
        h.Offset = 3; h.Step();
        SourceLooseHelmet a = h.Helmet();
        SourceVector3 entry = new SourceVector3(12 * .6f + 10 * 0f + 11 * -.8f + 10, 30, 12 * .8f + 10 * 0f + 11 * .6f + 30);
        Check(Bits(a.EntryWorldPosition).SequenceEqual(Bits(entry)) &&
            Bits(a.Position).SequenceEqual(Bits(new SourceVector3(entry.X + .35f, entry.Y + (1.6f - .08f / 2f), entry.Z + .35f))) &&
            a.Velocity.X == .35f && a.Velocity.Z == .35f && h.CacheReads == 1 &&
            h.Requests.Select(r => r.Bound).SequenceEqual(new[] { 100, 35 }), Entry);
        h.Offset = 900; h.NeckY = -200; h.Step();
        Check(h.CacheReads == 1 && h.Helmet().EntryWorldPosition.Y == 30, "detached helmet must never resample or follow its actor after first entry");
        var pair = new Host(); pair.Step(); long older = pair.Spawn(); long newer = pair.Spawn(); pair.Step();
        Check(pair.Wakes.SequenceEqual(new[] { newer, older }), "new level-zero helmet processes must preserve existing head-insertion order");
        foreach (int entryValue in new[] { 0, 44, 45, 99 })
        {
            var boundary = new Host { EntryValue = entryValue }; boundary.Step(); boundary.Spawn(); boundary.Step();
            Check(boundary.Helmet().Velocity.X == (entryValue < 45 ? -.35f : .35f) &&
                boundary.Helmet().Velocity.Z == boundary.Helmet().Velocity.X, "one shared range100<45 must negate BOTH world-axis launch velocities");
        }
    }
    private static void Physics()
    {
        foreach (int entry in new[] { 0, 44, 45, 99 })
        foreach (float height in new[] { -19.5f, 0f, 10f })
        foreach (int flags in new[] { 0, 2 })
        {
            var h = new Host { EntryValue = entry, NeckY = height };
            h.Step(); h.Set("Flags", flags); h.Set("Phi", 1020); h.Spawn();
            var reference = new Reference(entry, height, flags, 1020);
            for (int wake = 0; wake < 420; wake++)
            {
                h.Mode = wake < 70 ? 2 : wake < 140 ? 3 : 1;
                h.Step(); reference.Step(h.Origin + (int)h.Tick); reference.Compare(h.Helmet(), h.Requests.Count);
            }
            Check(h.Runtime.Processes.Count == 1 && h.Helmet().Bounces > 200 && h.Helmet().Alpha == 255,
                "while1 must retain resting ground/counter work through deadball/presnap without TTL or fade");
            Check(h.Sounds.Count == 3 && h.Sounds.All(s => s.Call == 108 && s.Volume == 115 && s.Pan == 127 &&
                s.PriorityExpression == "PRIORITY_LVL2" && s.ResolvedPriority == 10),
                "only first three postincrement bounces must journal original BALL_HIT_GRND cue arguments");
            Check(h.Requests.All(r => r.Site.Function == "loose_helmet_proc") &&
                h.Requests.Count(r => r.Bound == 100) == 1 &&
                h.Requests.Where(r => r.Bound != 100 && r.Bound != 35).Select(r => r.Bound)
                    .ChunkSix().All(b => b.SequenceEqual(new[] { 201, 157, 157, 201, 157, 157 })),
                "rebound shared draw order must be exactly201/157/157/201/157/157 before the moving spin draw");
        }
    }
    private static IEnumerable<int[]> ChunkSix(this IEnumerable<int> values)
    {
        var all = values.ToArray();
        for (int i = 0; i < all.Length; i += 6) yield return all.Skip(i).Take(6).ToArray();
    }
    private static void Lifecycle()
    {
        var h = new Host(); h.Step(); h.Spawn(); h.Step(); SourceLooseHelmet first = h.Helmet();
        h.Control(SourceFireOperation.KillProcessGroup, 0x950);
        Check(h.Runtime.Processes.Count == 0 && h.Frame().LooseHelmets.Count == 1 &&
            !h.Runtime.CurrentRecords.Any(r => r.Kind.StartsWith("ExitAlert", StringComparison.Ordinal)),
            "killall must not invent a dust-style exit alert or delete the globally backed helmet object");
        h.Step();
        Check(h.Helmet().FlightTime == first.FlightTime && h.Helmet().Roll == first.Roll,
            "a killed process leaves its global object drawable without stack-orphan cancellation");
        h.Control(SourceFireOperation.DeleteObjectGroup, 0x450); h.Spawn(); h.Step();
        SourceLooseHelmet replacement = h.Helmet();
        Check(replacement.FlightTime == first.FlightTime + 1 && replacement.Roll > first.Roll,
            "replacement entry must carry global phi/flight_time instead of resetting ball_data");
        h.Set("Flags", 0x20); h.Set("Tx", 17f); h.Set("Tz", 19f); h.Set("BounceTime", -31);
        h.Control(SourceFireOperation.KillProcessGroup, 0x950); h.Control(SourceFireOperation.DeleteObjectGroup, 0x450);
        h.Spawn(); h.Step();
        Check(h.Helmet().RawFlags == 0x20 && h.Helmet().Target.X == 17 && h.Helmet().Target.Z == 19 &&
            h.Helmet().BounceTime == -31 && h.Helmet().Bounces == 0,
            "entry may reset only explicit source fields, retaining global flags/targets/bounce_time");
        int draws = h.Requests.Count, flight = h.Helmet().FlightTime;
        h.Control(SourceFireOperation.DeleteObjectGroup, 0x450); h.Step();
        Check(h.Runtime.Processes.Count == 1 && h.Frame().LooseHelmets.Count == 0 &&
            h.Get<int>("FlightTime") == flight + 1 && h.Requests.Count == draws + 1,
            "delete_multiple_objects alone must not kill or cancel a process that only writes global ball_data");
        var over = new Host(); over.Step(); over.Spawn(); over.Step(); over.Set("Phi", int.MaxValue);
        bool rejected = false;
        try { over.Step(); } catch (SourceFireBoundaryException error) { rejected = error.Message.Contains("signed int phi overflow"); }
        Check(rejected, "source signed-overflow domain must stop explicitly rather than invent wrapping or bounded raw phi");
        var missing = new Host(false); missing.Step(); missing.Spawn(); rejected = false;
        try { missing.Step(); } catch (SourceFireBoundaryException error) { rejected = error.Message.StartsWith("SOURCE_HELMET_DATA_REQUIRED", StringComparison.Ordinal); }
        Check(rejected, "legacy data must fail with typed missing PHELM rather than substitute phead/ball/generic art");
    }
    private static object Invoke(GameSimulation game, string name, params object[] arguments)
    {
        try { return typeof(GameSimulation).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance).Invoke(game, arguments); }
        catch (TargetInvocationException error) { throw error.InnerException; }
    }
    private static void Set(GameSimulation game, string name, object value)
    { typeof(GameSimulation).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(game, value); }
    private static void Helpers()
    {
        foreach (var test in new[] {
            new[] { 1, -1, 5, 1, 0, 0 }, new[] { 2, 3, 5, 1, 0, 0 }, new[] { 2, -1, 4, 1, 0, 0 },
            new[] { 2, -1, 5, 1, 44, 99 }, new[] { 2, -1, 5, 1, 45, 9 }, new[] { 2, -1, 5, 0, 0, 10 },
            new[] { 2, -1, 5, 0, 99, 9 }, new[] { 2, -1, 5, 1, 45, 10 },
            new[] { 3, -1, 5, 1, 0, 0 }, new[] { 2, 3, 5, 1, 44, 99, -1 },
            new[] { 2, 3, 4, 1, 0, 0, -1 }
        })
        {
            var h = new Host(); h.Step(); h.Spawn(); h.Step(); int start = h.Runtime.CurrentRecords.Count;
            var game = new GameSimulation();
            Set(game, "sourceFireConfiguration", new SourceFireGameplayConfiguration(h.Runtime, h.Origin, SourceFireCull.Off));
            Set(game, "sourcePresentation", new SourcePresentationSettings(TeamId.Home, new[] { test[1], -1, -1, -1 }, 0));
            var provenance = new SourceRandomProvenance("synthetic-helper", "declared raw tape", "no platform PRNG assertion",
                SourceRandomEvidence.ExternalUnverified);
            int cursor = 0;
            var rng = new SourceInjectedRandom(site => cursor++ == 0 ? test[4] : test[5], provenance);
            Set(game, "sourceRandom", rng);
            game.Model.SourceTick = h.Tick; game.Model.Phase = test[0] == 2 ? GamePhase.LiveBall :
                test[0] == 1 ? GamePhase.PreSnap : GamePhase.DeadBall;
            game.Model.CarrierId = test[2]; game.Model.SourceTeamFire[0] = test[3] == 1;
            ActorState actor = game.Model.Players[5]; actor.SourceControllerStation = test.Length > 6 ? test[6] : 0;
            Invoke(game, "BeginSourceProcess", "synthetic_player", 5, 0);
            Invoke(game, "SourceMaybePopHelmet", actor);
            bool guarded = test[0] != 2 || actor.SourceControllerStation >= 0 && test[1] != -1, carrier = test[2] == 5;
            bool fire = test[3] == 1 && test[4] < 45, popped = !guarded && carrier && (fire || test[5] < 10);
            SourceFireRecord[] records = h.Runtime.CurrentRecords.Skip(start).ToArray();
            Check(rng.Trace.Count == (guarded || !carrier || fire ? 1 : 2) && rng.Trace.All(d => d.Site.BoundOrModulus == 100),
                "helper must draw first100 before mode/head guards and never duplicate either probability draw");
            Check(guarded ? h.Frame().LooseHelmets.Count == 1 && h.Runtime.Processes.Count == 1 :
                h.Frame().LooseHelmets.Count == 0 && h.Runtime.Processes.Count == (popped ? 1 : 0),
                "guarded calls retain prior helmet, but kill then delete must precede the noncarrier return");
            Check(guarded || records.Select(r => r.Kind).Take(2).SequenceEqual(new[] { "KillProcess", "DeleteObjectGroup" }),
                "actual helper must preserve killall0950 then delete450 order and full masks");
            Check(actor.SourceHelmetPopped == popped && game.ContactEvents.Count == (popped ? 1 : 0) &&
                game.UnresolvedContactSoundCalls.Count == (popped ? fire ? 1 : 2 : 0),
                "existing helmet flag, event, announcer and hard-hit calls must remain exactly once before qcreate");
            if (popped)
            {
                Check(game.ContactEvents.Single().Kind == SourceContactEventKind.HelmetPopped &&
                    game.ContactEvents.Single().ActorPlayerId == 5 &&
                    game.UnresolvedContactSoundCalls[0].OriginalArguments.SequenceEqual(new[] {
                        "ancr_swaped_bnk_str", fire ? "165" : "166", "ANCR_VOL2", "127", "LVL4" }) &&
                    (fire || game.UnresolvedContactSoundCalls[1].OriginalArguments.SequenceEqual(new[] {
                        "plyr_bnk_str", "P_HARD_HIT3_SND", "ANCR_VOL2", "127", "PRIORITY_LVL3" })),
                    "retained helper flag/event and exact original announcer/hard-hit arguments must not be substituted");
            }
            helperRows.Add(new { input = test, random = rng.Trace.Select(d => new { bound = d.Site.BoundOrModulus,
                value = d.Value, site = d.Site.SourceIdentity }).ToArray(), popped,
                objectAlive = h.Frame().LooseHelmets.Count == 1, processAlive = h.Runtime.Processes.Count == 1,
                operations = records.Select(r => r.Kind).ToArray(),
                sounds = game.UnresolvedContactSoundCalls.Select(s => s.OriginalArguments.ToArray()).ToArray() });
        }
        var cleanup = new Host(); cleanup.Step(); cleanup.Spawn(); cleanup.Step();
        var next = new GameSimulation();
        Set(next, "sourceFireConfiguration", new SourceFireGameplayConfiguration(cleanup.Runtime, cleanup.Origin, SourceFireCull.Off));
        Set(next, "sourceConfiguration", typeof(GameSimulation).GetMethod("DefaultSourceConfiguration",
            BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[0]));
        next.Model.SourceTick = cleanup.Tick; next.Model.Quarter = 1; next.Model.SourceClock = SourceClock.Create();
        next.Model.SourceRules = new SourceRulesState(0, 0, 1, 30, 100, SourcePlayType.Normal);
        Invoke(next, "InitializeSourceProcesses");
        while (next.Model.SourceProcesses.SourceTick < cleanup.Tick)
        {
            next.Model.SourceProcesses.BeginTick(next.Model.SourceProcesses.SourceTick + 1);
            Check(next.Model.SourceProcesses.Visit(GameSimulation.GameProcess),
                "new-play helper requires the real admitted game_proc continuation");
            if (next.Model.SourceProcesses.SourceTick < cleanup.Tick) next.Model.SourceProcesses.Complete();
        }
        Invoke(next, "BeginSourceProcess", "game_proc", -1, 0);
        Invoke(next, "ContinueSource");
        next.Model.SourceProcesses.Complete();
        Check(cleanup.Runtime.Processes.Count == 0 && cleanup.Frame().LooseHelmets.Count == 0 &&
            cleanup.Runtime.CurrentRecords.Any(r => r.Site.SourcePath == "nfl.c" && r.Site.SourceLine == 867 && r.Kind == "KillProcess") &&
            cleanup.Runtime.CurrentRecords.Any(r => r.Site.SourcePath == "nfl.c" && r.Site.SourceLine == 868 && r.Kind == "DeleteObjectGroup"),
            "actual new-play continuation must kill then delete helmet at nfl867/868 before selection");
        Check(!next.Model.SourceProcesses.Require(GameSimulation.GameProcess).Suspended &&
            next.Model.SourceProcesses.Require(2).Suspended,
            "new-play selection suspends actors without suspending its real game_proc caller");
    }
    private static void Rendering()
    {
        int cursor = 0;
        var directions = new SourceDirectionMath(quarter,
            Enumerable.Range(0, 49).Select(i => i == 24 ? -1 : cursor++ % 32 * 32), "synthetic-trig", "synthetic-sticks");
        var h = new Host(); h.Step(); h.Spawn(); h.Step(); h.Set("Flags", 1); h.Set("Phi", 2051); h.Set("Heading", 137); h.Set("Elevation", -333);
        SourceLooseHelmet helmet = h.Helmet();
        var normal = SourceFreeBallRenderModel.EvaluateHelmet(helmet, h.Tick, 0, directions);
        var white = SourceFreeBallRenderModel.EvaluateHelmet(helmet, h.Tick, 1, directions);
        Check(!normal.Transform.White && white.Transform.White && normal.Transform.State.Visible &&
            normal.Helmet.Decal.TextureId == "runtime/TESTLOD.WMS" && normal.Helmet.Decal.IntegerAspect == 4 &&
            normal.Transform.State.PupBigBall == 0 && normal.Transform.State.Scale == 2,
            "helmet draws must use current GLOBAL ball WHITE and captured team decal2/ratio4 without ball-carrier or bigball gates");
        float sx = Sin(333), cx = Sin(333 + 256), sy = Sin(137), cy = Sin(137 + 256),
            sz = Sin(2051), cz = Sin(2051 + 256);
        float[] x = { 1, 0, 0, 0, 0, cx, -sx, 0, 0, sx, cx, 0 };
        float[] y = { cy, 0, sy, 0, 0, 1, 0, 0, -sy, 0, cy, 0 };
        float[] z = { cz, -sz, 0, 0, sz, cz, 0, 0, 0, 0, 1, 0 };
        float[] expected = SourceFreeBallRenderModel.Multiply(SourceFreeBallRenderModel.Multiply(y, x), z);
        for (int row = 0; row < 3; row++)
            for (int col = 0; col < 3; col++)
                Check(Bits(normal.Transform.WorldMatrix[row * 4 + col]) == Bits(expected[row * 4 + col] * 2f),
                    "draw transform must be original Y*X(-fwd2)*Z(raw phi) with scale on basis only");
        Check(h.Helmet().Roll == 2051 && normal.AlphaSource == "CC_ALPHA" && normal.DepthFunction == "LESS" &&
            normal.DepthWrite && normal.Wrap == "WRAP/WRAP" && normal.ColorCombine == "TEXTURE_TIMES_ALPHA" &&
            white.ColorCombine == "DECAL_TEXTURE_ADD_CCALPHA/FF", "stored raw phi and original helmet material state must not be normalized or replaced by puff state");
        float sf, tf; SourceFireSpfxFrameConsumer.AspectScales(4, out sf, out tf);
        Check(sf == 128 && tf == 256, "literal helmet ratio4 must map normalized ST to original128/256 Glide units");
        foreach (string field in new[] { "Roll", "Pitch", "PitchRate", "Alpha", "FlightTime", "RawFlags", "Bounces", "BounceTime" })
            Check(typeof(SourceLooseHelmet).GetProperty(field).PropertyType == typeof(int), "all source ball_data scalar/counter fields must retain int types");
        h.Control(SourceFireOperation.HideAllObjects);
        Check(!SourceFreeBallRenderModel.EvaluateHelmet(h.Helmet(), h.Tick, 0, directions).Transform.State.Visible,
            "actual source hide/unhide applies to the detached object without retiring its process");
        h.Control(SourceFireOperation.UnhideAllObjects);
        Check(h.Helmet().Hidden == false && h.Frame().GlobalBallFlags == 0, "current global flags must be sampled separately from helmet own flags");
    }
    private static object Row(SourceLooseHelmet h)
    {
        return new { position = Bits(h.Position), velocity = Bits(h.Velocity), target = Bits(h.Target),
            heading = h.Heading, elevation = h.Elevation, phi = h.Roll, pitch = h.Pitch, pitchRate = h.PitchRate,
            alpha = h.Alpha, scale = Bits(h.Scale), flightTime = h.FlightTime, flags = h.RawFlags,
            bounces = h.Bounces, bounceTime = h.BounceTime };
    }
    public static int Main(string[] args)
    {
        try
        {
            if (args.Length > 0 && args[0] == "--trace")
            {
                quarter = JsonSerializer.Deserialize<float[]>(File.ReadAllText(args[6]));
                var h = new Host { EntryValue = int.Parse(args[1]), NeckY = float.Parse(args[2], CultureInfo.InvariantCulture) };
                h.Step(); h.Set("Flags", int.Parse(args[3])); h.Set("Phi", int.Parse(args[4])); h.Spawn();
                var rows = new List<object>();
                for (int i = 0; i < int.Parse(args[5]); i++) { h.Step(); rows.Add(Row(h.Helmet())); }
                Console.WriteLine(JsonSerializer.Serialize(new { rows, random = h.Random, sounds = h.Sounds.Select(s => new {
                    tick = s.Site.SourceTick, s.Call, s.Volume, s.Pan, priority = s.ResolvedPriority, line = s.Site.SourceLine }).ToArray() }));
                return 0;
            }
            if (args.Length > 0 && args[0] == "--draw")
            {
                quarter = JsonSerializer.Deserialize<float[]>(File.ReadAllText(args[1]));
                int cursor = 0;
                var directions = new SourceDirectionMath(quarter,
                    Enumerable.Range(0, 49).Select(i => i == 24 ? -1 : cursor++ % 32 * 32), "original-trig-component", "synthetic-sticks");
                var decal = new SourceLooseHelmetDecal("runtime/TESTLOD.WMS", new string('c', 64), "synthetic/team.wms", 128, 256);
                var rows = new List<object>();
                foreach (int globalFlags in new[] { 0, 1 })
                foreach (int ownFlags in new[] { 0, 1 })
                {
                    var helmet = new SourceLooseHelmet(1, 101, 5, 1, 0, new SourceVector3(7, 11, 13),
                        new SourceVector3(7, 11, 13), new SourceVector3(), new SourceVector3(), 137, -333, 2051,
                        40, 40, 127, 2f, 3, ownFlags, 0, 0, false, decal);
                    var draw = SourceFreeBallRenderModel.EvaluateHelmet(helmet, 7, globalFlags, directions);
                    rows.Add(new { globalFlags, ownFlags, matrix = draw.Transform.CameraMatrix(Camera(7)).Select(Bits).ToArray(),
                        white = draw.Transform.White, alpha = draw.Transform.Alpha, effectiveAlpha = draw.Transform.EffectiveAlpha });
                }
                Console.WriteLine(JsonSerializer.Serialize(rows));
                return 0;
            }
            if (args.Length > 0 && args[0] == "--carry")
            {
                quarter = JsonSerializer.Deserialize<float[]>(File.ReadAllText(args[1]));
                var h = new Host(); h.Step(); h.Spawn();
                for (int i = 0; i < 120; i++) h.Step();
                object before = Row(h.Helmet());
                h.Control(SourceFireOperation.KillProcessGroup, 0x950);
                h.Control(SourceFireOperation.DeleteObjectGroup, 0x450); h.Spawn(); h.Step();
                Console.WriteLine(JsonSerializer.Serialize(new { rows = new[] { before, Row(h.Helmet()) }, random = h.Random }));
                return 0;
            }
            if (args.Length > 0 && args[0] == "--geometry")
            {
                SourceFireSpfxBundle bundle = SourceFireSpfxReader.ReadAuthorized(Path.GetFullPath(args[1]), "manifest.json", args[2]);
                bundle.RequireHelmet();
                SourceFireGeometry geometry = bundle.HelmetGeometry;
                Console.WriteLine(JsonSerializer.Serialize(new { positionBits = geometry.Positions.SelectMany(Bits).ToArray(),
                    uvBits = geometry.RawAuthoringUvs.Select(Bits).ToArray(), vertices = geometry.VertexIndices,
                    uvs = geometry.UvIndices, manifestSha256 = bundle.ManifestSha256 }));
                return 0;
            }
            AdmissionAndEntry(); Physics(); Lifecycle(); Helpers(); Rendering();
            Console.WriteLine(JsonSerializer.Serialize(new { status = "passed", assertions, helperRows,
                syntheticInputs = true, originalAssetsRequired = false, nativeQualified = false }));
            return 0;
        }
        catch (Exception error)
        {
            Console.WriteLine(JsonSerializer.Serialize(new { status = "failed", assertions, error = error.ToString() }));
            return 1;
        }
    }
}
