using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Blitz.AssetImport;
using Blitz.Contracts;
using Blitz.Gameplay;
using Blitz.Presentation;
using Blitz.Runtime;

internal static class Program
{
    private static int assertions;
    private static SourceFireSpfxBundle data;
    private static readonly List<SourceBallTrail> stages = new List<SourceBallTrail>();

    private sealed class Host
    {
        internal readonly SourceFireSpfxRuntime Runtime;
        internal readonly List<SourceFireSite> Wakes = new List<SourceFireSite>();
        internal long Tick = -1, NextSerial = 32;
        internal int Mode = SourceFireIds.InPlay, Samples, PointSamples, Draws, SampleTickOffset;
        internal SourceVector3 Ball;
        internal int Heading, Elevation, Roll;
        internal float Scale;
        internal bool FullSample = true;

        internal Host(SourceFireStalePolicy policy = SourceFireStalePolicy.PortableCancelDeletedAndFreezeOrphan)
        {
            Runtime = new SourceFireSpfxRuntime(data);
            Reset(policy);
        }

        internal void Reset(SourceFireStalePolicy policy = SourceFireStalePolicy.PortableCancelDeletedAndFreezeOrphan)
        {
            Runtime.Reset(new SourceFireBootstrap(SourceFireOrdering.PortableDeclaredSlots,
                SourceFireClockOrigin.PortableLogicalEpoch, policy, 0, 100,
                "declared ball-trail component fixture, not a native dispatch witness"));
            Tick = -1;
        }

        internal void Step()
        {
            Tick++;
            Ball = new SourceVector3(7 + Tick * 101, 11 + Tick * 3, 13 - Tick * 17);
            Heading = 137 + (int)Tick * 71; Elevation = -333 + (int)Tick * 41; Roll = 1025 + (int)Tick * 60;
            Scale = 3.5f + Tick;
            Runtime.BeginSourceTick(Runtime.PortableHeadThenHostTick(Tick, 100 + (int)Tick, new SourceFireWakeSlot[0]),
                new SourceFireHostCallbacks(request => { Draws++; return 0; },
                    () => Mode, actor => { throw new InvalidOperationException("No actor-pose fallback for a football ghost."); },
                    () => { PointSamples++; return new SourceFireBallWorldSample(Tick, Ball, "declared fire point, not orientation"); },
                    actor => 0, (actor, flags, site) => { }, site => Wakes.Add(site),
                    (slot, ordinal) => { throw new InvalidOperationException("Unexpected component host continuation."); },
                    sound => new SourceFireSoundSubmission(false, "component metadata, no trail sound"),
                    () => ++NextSerial, FullSample ? (Func<SourceFireBallRenderSample>)(() =>
                    {
                        Samples++;
                        return new SourceFireBallRenderSample(Tick + SampleTickOffset, Ball, Heading, Elevation, Roll,
                            Scale, 0x841, "declared complete draw-field copy at this process-entry wake");
                    }) : null));
        }

        internal SourceFireSite Site()
        { return new SourceFireSite(Tick, 100 + (int)Tick, 16, 0x600, 20, 0, -1, "player.c", "ball_proc", 10311); }

        internal SourceFireBallEffect Select(SourceBallKind kind = SourceBallKind.Pass, bool fire = false,
            bool turbo = false, bool bounce = false, bool teamFire = false, int flight = 1)
        { return Runtime.BallFreeOrPhantomStep(Site(), new SourceFireBallEmitterInput(kind, fire, turbo, bounce, teamFire, flight)); }

        internal void Control(SourceFireOperation operation, int id = 0, int mask = -1)
        { Runtime.Apply(SourceFireCommand.Control(Site(), operation, id, mask)); }

        internal SourceFireRenderFrame Frame()
        {
            return Runtime.CaptureFrame(new SourceFireRenderBasis(new SourceFireCameraFrame(Tick,
                new SourceVector3(17, 11, -80), new float[] { .6f, 0, -.8f, 0, 0, 1, 0, 0, .8f, 0, .6f, 0 },
                "declared paired component camera, not a native scene"), 384, SourceFireCull.Back));
        }
    }

    private static void Require(bool value, string message)
    {
        assertions++;
        if (!value) throw new InvalidOperationException("BALL TRAIL: " + message);
    }

    private static void Reject(Action action, string message)
    {
        bool rejected = false;
        try { action(); } catch (SourceFireBoundaryException) { rejected = true; }
        Require(rejected, message);
    }

    private static object Row(Host host)
    {
        SourceFireRenderFrame frame = host.Frame();
        SourceBallTrail? trail = frame.BallTrails.Count == 0 ? (SourceBallTrail?)null : frame.BallTrails.Single();
        return new {
            tick = host.Tick, processes = host.Runtime.Processes.Count,
            sleep = host.Runtime.Processes.Count == 0 ? 0 : (int)host.Runtime.Processes.Single().SleepField,
            objects = frame.BallTrails.Count, stage = trail.HasValue ? trail.Value.Stage : -1,
            alpha = trail.HasValue ? trail.Value.Alpha : 0, scale = trail.HasValue ? trail.Value.Scale : 0,
            flags = trail.HasValue ? trail.Value.LocalFlags : 0, hidden = trail.HasValue && trail.Value.Hidden,
            position = trail.HasValue ? new[] { trail.Value.EntrySample.WorldPosition.X,
                trail.Value.EntrySample.WorldPosition.Y, trail.Value.EntrySample.WorldPosition.Z } : new float[3],
            angles = trail.HasValue ? new[] { trail.Value.EntrySample.Heading, trail.Value.EntrySample.Elevation,
                trail.Value.EntrySample.Roll } : new int[3],
            entryTick = trail.HasValue ? trail.Value.EntrySample.SourceTick : 0, samples = host.Samples
        };
    }

    private static List<object> Lifecycle(bool suspended)
    {
        var host = new Host();
        host.Step();
        Require(host.Select() == SourceFireBallEffect.BallTrail, "ordinary Pass must select BTRAIL.");
        Require(host.Runtime.Processes.Count == 1 &&
            host.Runtime.CurrentRecords.Any(r => r.Kind == "CreateProcess" && r.Detail == "balltrl"),
            "BTRAIL must enqueue the original balltrl football-mesh process, not only return a selector.");
        SourceFireProcessView process = host.Runtime.Processes.Single();
        Require(process.Entry == SourceFireEntry.BallTrail && process.GroupId == SourceFireIds.Ball &&
            process.BirthTick == 0 && process.SleepField == 1 && process.ObjectSerial == 0,
            "balltrl must queue under BALL_PID with next-wake sleep1, not execute inline.");
        Require(host.Samples == 0 && host.Frame().BallTrails.Count == 0, "Queue time must not capture a ball or create a draw.");
        var rows = new List<object> { Row(host) };
        SourceFireRenderFrame immutable = null;
        int lastTick = suspended ? 14 : 7;
        for (int tick = 1; tick < lastTick; tick++)
        {
            if (suspended && tick == 9)
            {
                host.Control(SourceFireOperation.ResumeAll);
                host.Control(SourceFireOperation.UnhideAllObjects);
            }
            host.Step();
            if (tick == 1)
            {
                SourceBallTrail first = host.Frame().BallTrails.Single();
                Require(first.EntrySample.WorldPosition.X == 108 && first.EntrySample.Heading == 208 &&
                    first.EntrySample.Elevation == -292 && first.EntrySample.Roll == 1085 &&
                    first.EntrySample.SourceTick == 1, "Entry must read XYZ and all angles AFTER queue time, without a position-only fallback.");
                Require(first.EntrySample.RawFlags == 0x841 && first.EntrySample.Scale == 4.5f &&
                    first.LocalFlags == 0x40 && first.Scale == 2.25f,
                    "Copy all draw fields at entry, then overwrite local flags/scale rather than inheriting global White/Phantom.");
                Require(host.Wakes.Single().Function == "trail_ball_proc" && host.Wakes.Single().NativeRunLevel == 0 &&
                    host.Wakes.Single().ActorId == -1, "balltrl entry belongs to its real level0 wake, not RL_BALL or an actor.");
                immutable = host.Frame();
                if (suspended)
                {
                    host.Control(SourceFireOperation.SuspendMask, 0, 0x8000);
                    host.Control(SourceFireOperation.HideAllObjects);
                }
            }
            if (suspended && tick == 8)
            {
                host.Control(SourceFireOperation.ResumeMask, SourceFireIds.Spfx);
                host.Control(SourceFireOperation.KillProcessGroup, SourceFireIds.Spfx);
                host.Control(SourceFireOperation.DeleteObjectGroup, SourceFireIds.PuffObject);
                host.Control(SourceFireOperation.ResetLineupLatches);
                Require(host.Runtime.Processes.Single().SleepField == 0x8001 &&
                    host.Frame().BallTrails.Single().Hidden, "Selection SPFX cleanup/resume and new-lineup latches must not remove/age BALL_PID.");
            }
            SourceFireRenderFrame frame = host.Frame();
            if (frame.BallTrails.Count != 0)
            {
                SourceBallTrail trail = frame.BallTrails.Single();
                int stage = suspended ? tick < 9 ? 0 : tick - 8 : tick - 1;
                Require(trail.Stage == stage && trail.Alpha == new[] { 215, 200, 185, 170, 155 }[stage] &&
                    trail.Scale == new[] { 2.25f, 2.15f, 2.05f, 1.95f, 1.85f }[stage],
                    "Exactly five original alpha/scale stages must advance only on unsuspended source wakes.");
                Require(trail.EntrySample.WorldPosition.X == 108 && trail.EntrySample.WorldPosition.Y == 14 &&
                    trail.EntrySample.WorldPosition.Z == -4 && trail.EntrySample.Heading == 208 &&
                    trail.EntrySample.Elevation == -292 && trail.EntrySample.Roll == 1085,
                    "Trail XYZ/fwd/fwd2/phi must stay static; no live tracking, velocity or spin integration.");
                Require(host.Samples == 1 && host.PointSamples == 0 && host.Draws == 0,
                    "A whole render copy is read exactly once; non-fire stages have no point/pose/RNG callback.");
                if (!suspended)
                {
                    stages.Add(trail);
                    Require(frame.Records.Single(r => r.Kind == "Sleep").Value == 1 &&
                        host.Runtime.Processes.Single().SleepField == 1,
                        "Every visible stage, including155/1.85, must perform one sleep1.");
                    Require(frame.Records.Single(r => r.Kind == "BallTrailStage").Site.SourceLine ==
                        new[] { 10553, 10558, 10561, 10564, 10567 }[stage],
                        "Each actual stage record must identify its original assignment, not an invented uniform line stride.");
                }
            }
            rows.Add(Row(host));
        }
        Require(host.Frame().BallTrails.Count == 0 && host.Runtime.Processes.Count == 0 &&
            host.Runtime.CurrentRecords.Any(r => r.Kind == "DeleteObject" && r.Site.SourceLine == 10570) &&
            host.Runtime.CurrentRecords.Any(r => r.Kind == "ReturnDie" && r.Value == 1),
            "The sixth unsuspended entry wake must delete the football object before normal shell return.");
        Require(immutable.BallTrails.Single().Stage == 0 && !immutable.BallTrails.Single().Hidden,
            "Published immutable entry frames must not change after hidden/staged/deleted live objects.");
        return rows;
    }

    private static void Controls()
    {
        var host = new Host(); host.Step(); host.Select(); host.Step();
        host.Control(SourceFireOperation.HideAllObjects);
        host.Control(SourceFireOperation.SuspendMask, 0, 0x8000);
        host.Select(turbo: true); host.Step();
        Require(host.Frame().BallTrails.Count == 2 && host.Frame().BallTrails[0].Hidden &&
            !host.Frame().BallTrails[1].Hidden && host.Frame().BallTrails[0].Stage == 0,
            "Hide/suspend affect existing nodes only; later-created equal-priority footballs retain arrival order.");
        Require(host.Frame().BallTrails.Select(t => t.ObjectSerial).SequenceEqual(
            host.Frame().BallTrails.Select(t => t.ObjectSerial).OrderBy(x => x)),
            "Equal DRAWORDER_BALL objects must retain source creation order, not alpha/distance sorting.");
        host.Control(SourceFireOperation.KillProcessGroup, SourceFireIds.Ball);
        SourceFireRenderFrame orphan = host.Frame();
        Require(orphan.BallTrails.Count == 2 && host.Runtime.Processes.Count == 0 &&
            orphan.PortableBoundaries.Any(b => b.StartsWith("portable:freeze-orphan-stack-object:", StringComparison.Ordinal)),
            "A process kill is not object deletion; portable orphan freeze must be explicit.");
        host.Step();
        Require(host.Frame().BallTrails.All(t => t.Stage == 0), "A killed owner's portable object must not silently keep aging.");
        host.Control(SourceFireOperation.DeleteObjectGroup, SourceFireIds.BallObject);
        Require(host.Frame().BallTrails.Count == 0, "OID_BALL deletion must remove owned football objects.");

        var pending = new Host(); pending.Step(); pending.Select();
        pending.Control(SourceFireOperation.KillProcessGroup, SourceFireIds.Ball); pending.Step();
        Require(pending.Samples == 0 && pending.Frame().BallTrails.Count == 0,
            "Killing a queued process before entry must not sample or render a ghost.");

        var deleted = new Host(); deleted.Step(); deleted.Select(); deleted.Step();
        deleted.Control(SourceFireOperation.DeleteObjectGroup, SourceFireIds.BallObject);
        Require(deleted.Runtime.Processes.Count == 1, "Object deletion must not fabricate an immediate process kill.");
        deleted.Step();
        Require(deleted.Runtime.Processes.Count == 0 &&
            deleted.Frame().PortableBoundaries.Any(b => b.StartsWith("portable:cancel-deleted-object-continuation:", StringComparison.Ordinal)),
            "Portable deleted-object cancellation occurs at the next actual continuation access.");

        var strict = new Host(SourceFireStalePolicy.RejectUndefinedAccess); strict.Step(); strict.Select(); strict.Step();
        strict.Control(SourceFireOperation.KillProcessGroup, SourceFireIds.Ball);
        Reject(() => strict.Frame(), "Strict mode must reject orphan stack-object access, not claim defined native cleanup.");
        var stale = new Host(SourceFireStalePolicy.RejectUndefinedAccess); stale.Step(); stale.Select(); stale.Step();
        stale.Control(SourceFireOperation.DeleteObjectGroup, SourceFireIds.BallObject);
        Reject(stale.Step, "Strict mode must reject a deleted native object continuation.");

        var missing = new Host { FullSample = false }; missing.Step(); missing.Select();
        Reject(missing.Step, "No entry-angle sample must fail; a position-only fire callback is never an orientation substitute.");
        var wrongTick = new Host { SampleTickOffset = -1 }; wrongTick.Step(); wrongTick.Select();
        Reject(wrongTick.Step, "A stale/queue-tick sample cannot stand in for the entry read.");

        var reset = new Host(); reset.Step(); reset.Select(turbo: true); reset.Step();
        SourceFireRenderFrame before = reset.Frame();
        reset.Control(SourceFireOperation.ResetBallProcessEffect);
        Require(reset.Runtime.Processes.Count == 1 && reset.Frame().BallTrails.Count == 1 &&
            reset.Runtime.BallEffect == SourceFireBallEffect.None,
            "ball_proc selector reset must not invent a trail/object cleanup.");
        reset.Reset(); reset.Step();
        Require(reset.Runtime.Processes.Count == 0 && reset.Frame().BallTrails.Count == 0 &&
            reset.Frame().PortableBoundaries.Count == 0 && reset.Runtime.BallEffect == SourceFireBallEffect.None &&
            before.BallTrails.Count == 1, "A real host restart must clear processes, objects, boundaries and selector without changing old frames.");
    }

    private static void Selectors()
    {
        var host = new Host(); host.Step();
        Action<SourceBallKind, bool, bool, bool, bool, int, int, SourceFireBallEffect, int, int> select =
            (kind, fire, turbo, bounce, team, flight, mode, effect, trails, puffs) =>
        {
            host.Mode = mode;
            int start = host.Runtime.CurrentRecords.Count;
            Require(host.Select(kind, fire, turbo, bounce, team, flight) == effect, "Original named selector and Bobble retention must remain unchanged.");
            SourceFireRecord[] creates = host.Runtime.CurrentRecords.Skip(start).Where(r => r.Kind == "CreateProcess").ToArray();
            Require(creates.Count(r => r.Detail == "balltrl") == trails && creates.Count(r => r.Detail == "smkpuf") == puffs,
                "PCLOUD/BTRAIL queue every selected wake; FIRE alone uses its original even-flight triplet.");
            foreach (SourceFireRecord record in creates.Where(r => r.Detail == "balltrl"))
            {
                SourceFireProcessView process = host.Runtime.Processes.Single(p => p.Serial == record.ProcessSerial);
                Require(record.Site.SourceLine == 10361 && process.GroupId == SourceFireIds.Ball &&
                    process.Args.A0 == 0 && process.Args.A1 == 0 && process.Args.A2 == 0 && process.Args.A3 == 0,
                    "The original balltrl call site must keep BALL_PID and all four zero arguments.");
            }
        };
        select(SourceBallKind.Pass, false, false, false, false, 1, 2, SourceFireBallEffect.BallTrail, 1, 0);
        select(SourceBallKind.Pass, false, true, false, false, 2, 2, SourceFireBallEffect.PlayerCloud, 1, 0);
        select(SourceBallKind.Bobble, false, false, false, false, 3, 2, SourceFireBallEffect.PlayerCloud, 1, 0);
        select(SourceBallKind.Kickoff, false, false, false, false, 4, 1, SourceFireBallEffect.None, 0, 0);
        select(SourceBallKind.Kickoff, false, false, false, false, 5, 2, SourceFireBallEffect.BallTrail, 1, 0);
        select(SourceBallKind.Punt, false, false, false, false, 6, 2, SourceFireBallEffect.BallTrail, 1, 0);
        select(SourceBallKind.Punt, false, false, true, false, 7, 2, SourceFireBallEffect.Blink, 0, 0);
        select(SourceBallKind.Pass, true, true, false, false, 3, 2, SourceFireBallEffect.Fire, 0, 0);
        select(SourceBallKind.Pass, true, true, false, false, 4, 2, SourceFireBallEffect.Fire, 0, 3);
        select(SourceBallKind.Bobble, false, false, false, false, 6, 2, SourceFireBallEffect.Fire, 0, 3);
        select(SourceBallKind.FieldGoal, false, false, false, false, 8, 2, SourceFireBallEffect.None, 0, 0);
        select(SourceBallKind.FieldGoal, false, false, false, true, 10, 2, SourceFireBallEffect.Fire, 0, 3);
        select(SourceBallKind.FieldGoal, false, false, true, true, 12, 2, SourceFireBallEffect.Blink, 0, 0);
        select(SourceBallKind.Lateral, true, true, false, true, 14, 2, SourceFireBallEffect.None, 0, 0);
        select(SourceBallKind.Fumble, true, true, false, true, 16, 2, SourceFireBallEffect.None, 0, 0);
        select(SourceBallKind.Pass, false, false, false, false, 17, 3, SourceFireBallEffect.None, 0, 0);
        Require(host.Samples == 0 && host.PointSamples == 0 && host.Draws == 0,
            "Selection queues children without inline reads, sound or direct RNG.");
        Reject(() => SourceFireCommand.Create(host.Site(), SourceFireEntry.BallTrail, new SourceFireArgs(1, 0, 0)),
            "A trail birth must not accept a queue-time position/actor through invented arguments.");
    }

    private static List<object> GlobalDraws(SourceDirectionMath directions)
    {
        var rows = new List<object>();
        SourceCinematicTransform camera = SourceCinematicTransform.FromState(new SourceCinematicSeed(
            new SourceVector3(17, 11, -80), new SourceVector3(-3, 5, 6), 90, 0, 0));
        foreach (SourceBallTrail original in stages)
        foreach (bool hidden in new[] { false, true })
        foreach (int carrier in new[] { -1, 3 })
        foreach (int flags in new[] { 0, 1, 0x800, 0x801, 0x40, 0x841 })
        foreach (int bigBall in new[] { 0, -2 })
        {
            var trail = new SourceBallTrail(original.ObjectSerial, original.ProcessSerial, original.EntrySample, original.Stage, hidden);
            SourceFreeBallDraw draw = SourceFreeBallRenderModel.EvaluateTrail(trail, 29, carrier, flags, bigBall, directions);
            bool visible = !hidden && (carrier == -1 || (flags & 0x800) != 0), white = (flags & 1) != 0;
            Require(draw.State.Visible == visible && draw.Alternate && draw.White == white &&
                draw.State.RawFlags == (0x40 | (flags & 1)) && draw.Alpha == original.Alpha &&
                draw.EffectiveAlpha == (white ? 255 : original.Alpha),
                "draw_ball must use current GLOBAL carrier/Phantom/White and local ALT/alpha; GLIDE3 White overwrites CC_ALPHA withFF.");
            Require(draw.State.Position.X == 108 && draw.State.Heading == 208 && draw.State.Elevation == -292 &&
                draw.State.Roll == 1085 && draw.State.Scale == original.Scale,
                "Draws must use the retained process-entry pose, not current ball angles or scale.");
            SourceFreeBallDraw expectedScale = SourceFreeBallRenderModel.Evaluate(new SourceFreeBallState(29, visible,
                original.EntrySample.WorldPosition, original.EntrySample.Heading, original.EntrySample.Elevation,
                original.EntrySample.Roll, original.Scale * (bigBall != 0 ? 1.5f : 1),
                original.LocalFlags | (flags & 1), 0), directions);
            for (int row = 0; row < 3; row++)
            for (int column = 0; column < 4; column++)
            {
                int i = row * 4 + column;
                Require(draw.WorldMatrix[i] == expectedScale.WorldMatrix[i],
                    "GLOBAL pup_bigball is evaluated at draw time and scales only the copied basis, never translation.");
            }
            rows.Add(new {
                input = new { carrier, globalFlags = flags, bigBall, hidden, localFlags = original.LocalFlags,
                    alpha = original.Alpha, scale = original.Scale,
                    position = new[] { original.EntrySample.WorldPosition.X, original.EntrySample.WorldPosition.Y,
                        original.EntrySample.WorldPosition.Z },
                    heading = original.EntrySample.Heading, elevation = original.EntrySample.Elevation, roll = original.EntrySample.Roll,
                    cameraEye = new[] { camera.Position.X, camera.Position.Y, camera.Position.Z }, cameraMatrix = camera.Transform },
                drawn = draw.State.Visible, alternate = draw.Alternate, white, localAlpha = draw.Alpha,
                effectiveAlpha = draw.EffectiveAlpha, matrix = visible ? draw.CameraMatrix(camera) : new float[12]
            });
        }
        SourceFreeBallDraw main = SourceFreeBallRenderModel.Evaluate(new SourceFreeBallState(29, true,
            new SourceVector3(7, 11, 13), 137, -333, 1025, 2, 0, 0), directions);
        Require(main.Alpha == 255 && main.EffectiveAlpha == 255 && !main.Trail.HasValue && !main.Alternate,
            "The unaffected main-ball draw/default must remain alpha255 and primary-decal with no trail identity.");
        Reject(() => SourceFreeBallRenderModel.EvaluateTrail(default(SourceBallTrail), 29, -1, 0, 0, directions),
            "An unspecified football trail must fail instead of rendering a default ghost.");
        return rows;
    }

    private static int Main(string[] args)
    {
        try
        {
            data = SourceFireSpfxReader.ReadAuthorized(args[0], "manifest.json", args[1]);
            SourcePlaybook playbook;
            using (var reader = File.OpenText(args[3])) playbook = SourcePlaybook.LoadConverted(reader);
            List<object> normalRows = Lifecycle(false), suspendedRows = Lifecycle(true);
            Selectors(); Controls();
            List<object> drawCases = GlobalDraws(playbook.Directions);
            File.WriteAllText(args[2], JsonSerializer.Serialize(new { status = "passed", assertions,
                normalRows, suspendedRows, drawCases, nativeQualified = false, UnityExecuted = false },
                new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine("PASS ball-trail production: " + assertions + " assertions; normal/suspended original process traces; " +
                drawCases.Count + " original-table global/local draw cases.");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }
}
