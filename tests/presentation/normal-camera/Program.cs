using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Blitz.AssetImport;
using Blitz.Contracts;
using Blitz.Gameplay;
using Blitz.Presentation;

internal static class Program
{
    private static int assertions;
    private static readonly List<string> cases = new List<string>();
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    private static int Main(string[] args)
    {
        try
        {
            Run("raw-target-and-integrator", RawTargets);
            Run("pass-endpoint-and-offscreen-cache", PassTargets);
            Run("gopher-and-resumed-source-wakes", Gopher);
            Run("normal-dispatch-and-impact-return", ImpactReturn);
            Run("conditional-random-and-pending-shaker", RandomOrder);
            Run("immutable-core-camera-boundary", ImmutableBoundary);
            Run("actual-core-raw-observation-seam", CoreObservation);
            Run("actual-core-restricted-camera-feedback", CoreFeedback);
            Run("source-raster-and-frame-not-render", RasterAndFrame);
            Run("all-kickoff-data-and-original-trig-state", Kickoff);
            Run("field-goal-new-play-lifecycle", FieldGoalLifecycle);
            Run("raw-camera-new-match-reset", MatchReset);
            if (args.Length > 0 && args[0] == "--active")
                Run("original-C-active-dispatch-sequences", () => CompareActive(args[1]));
            else if (args.Length > 0 && args[0] != "-") Run("original-C-normal-sequences", () => CompareNormal(args[0]));
            if (args.Length > 1 && args[0] != "--active" && args[1] != "-")
                Run("pinned-original-camera-data", () => Check(SourceNormalCameraData.Read(File.ReadAllBytes(args[1])).Kickoff.Count == 11,
                    "read the actual pinned eleven-entry kickoff catalog"));
            Console.WriteLine("{\"status\":\"passed\",\"assertions\":" + assertions + ",\"cases\":[" +
                string.Join(",", cases.Select(s => "\"" + s + "\"")) + "]}");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine("RAW CAMERA ASSERTION: " + error.Message);
            Console.Error.WriteLine(error.StackTrace);
            return 1;
        }
    }

    private static void Run(string name, Action test) { test(); cases.Add(name); }
    private static void Check(bool value, string message)
    { assertions++; if (!value) throw new InvalidOperationException(message); }
    private static void Near(float observed, float expected, float tolerance, string message)
    {
        Check(!float.IsNaN(observed) && Math.Abs(observed - expected) <= tolerance,
        message + " expected=" + expected.ToString("R", Invariant) + " observed=" + observed.ToString("R", Invariant));
    }
    private static void Reject(Action test, string message)
    {
        bool rejected = false;
        try { test(); } catch (InvalidOperationException) { rejected = true; } catch (ArgumentException) { rejected = true; }
        Check(rejected, message);
    }

    private static SourceCameraBoundary Boundary(long tick, int mode = 2, int carrier = 0, int actorMode = 7,
        float actorX = 100f, float actorZ = 0f, int flags = 0, int ballFlags = 0, int flight = 0,
        int type = 1, int quarter = 1, bool hh = false, int side = 0,
        SourceVector3? lead = null, SourceVector3? ballPosition = null, SourceVector3? targetCamera = null,
        SourceVector3? receiverCamera = null)
    {
        var actors = new SourceCameraActor[14];
        for (int id = 0; id < actors.Length; id++)
            actors[id] = new SourceCameraActor(id, id == carrier ? actorMode : 7, 0, 147,
                new SourceVector3(id == carrier ? actorX : (id / 7 == side ? -130f : 40f) + id * 3f,
                    0f, id == carrier ? actorZ : id * 2f - 15f),
                id == 1 ? receiverCamera ?? new SourceVector3(0, 0, 100) : new SourceVector3(0, 0, 100));
        return new SourceCameraBoundary(tick, 0, mode, flags, type == 3 ? 1 : 0, 0, quarter, side, side, 30,
            carrier, hh, false, 0, 0,
            new SourceCameraBall(ballPosition ?? new SourceVector3(-20, 0, 0), new SourceVector3(2, 3, 1),
                new SourceVector3(-180, 0, 0), lead ?? new SourceVector3(177, 0, 81),
                targetCamera ?? new SourceVector3(0, 0, 100), type, ballFlags, flight, side * 7 + 3, 1), actors);
    }

    private static SourceCinematicSeed Seed(float velocity = 3f)
    {
        return new SourceCinematicSeed(new SourceVector3(-130, 80, 1), new SourceVector3(0, 15.6f, 0),
            150, SourceCinematicMath.Radians(90), SourceCinematicMath.Radians(-25),
            new SourceVector3(velocity, 0, 0), 4f);
    }
    private static int NoRandom(int bound, string site)
    { throw new InvalidOperationException("unexpected camera draw " + bound + " " + site); }

    private static void RawTargets()
    {
        var model = new SourceNormalCameraModel();
        var plan = model.Prepare(Seed(), Boundary(1), 512, 384, NoRandom);
        var result = model.Integrate(plan);
        Near(plan.Target.X, 100, .0001f, "normal must track actual actor root, not held-ball or public-yard position");
        Near(result.Velocity.X, 3f * .35f + .08f * (100f - 2f), .0001f,
            "normal must retain original scalar velocity/acceleration, never EMA");
        Near(result.Focus.X, result.Velocity.X, .0001f, "one source wake integrates once");
        Near(result.Acceleration.X, .08f * 98f, .0001f, "retain original acceleration state");
        Check(plan.PlayerCount == 1 && plan.PlayerMask == 1 && plan.CarrierMode == 7,
            "raw actor identities/mode determine tracking eligibility");
        plan = model.Prepare(Seed(), Boundary(2, actorMode: 5, actorX: 230, actorZ: 71), 512, 384, NoRandom);
        Near(plan.Target.X, 230 + 6f * 7.8f, .0001f,
            "actual MODE_QB, not inferred LOS crossing, retains original lead");
        Near(plan.Target.Z, 71f * .68f * .82f, .0001f, "actual MODE_QB retains original lateral root scaling");
        Near(plan.DistanceTarget, 30f * 7.8f + 71f * .4f, .0001f, "QB raw lateral distancing");
        var hh = model.Prepare(Seed(), Boundary(3, hh: true), 512, 384, NoRandom);
        Near(hh.DistanceTarget, 27f * 7.8f, .0001f, "actual HH team uses original camera parameter dimension");
    }

    private static void PassTargets()
    {
        var model = new SourceNormalCameraModel();
        var a = Boundary(1, carrier: -1, type: 0, flight: 1, ballPosition: new SourceVector3(-50, 20, -40),
            targetCamera: new SourceVector3(100, 100, 100), receiverCamera: new SourceVector3(70, 80, 100));
        model.BeginWake(a, false);
        var plan = model.Prepare(Seed(), a, 512, 384, NoRandom);
        Check(model.TargetOffscreen && plan.CarrierMode == -2 && plan.PlayerCount == 0,
            "both cached ground/lead projections mark the original offscreen pass branch");
        float biasedX = (-50f - 177f) * .75f + 177f;
        float expectedX = biasedX - ((177f - biasedX) / 2f * (float)Math.Sin(SourceCinematicMath.Radians(-25))) + 6f * 7.8f;
        Near(plan.Target.X, expectedX, .0001f, "original offscreen span uses actual ball.tx, not receiver position");
        var next = Boundary(2, carrier: -1, type: 0, flight: 3, ballFlags: 0x100,
            lead: new SourceVector3(291, 0, -80));
        model.BeginWake(next, false);
        var movedLead = model.Prepare(Seed(), next, 512, 384, NoRandom);
        Check(Math.Abs(plan.Target.X - movedLead.Target.X) > 15f,
            "original ball.tx/tz lead endpoint, never current receiver, must causally change the raw target");
        var result = model.Integrate(plan);
        Near(result.DistanceAcceleration, .0005f * (plan.DistanceTarget - 150f - 2f), .00001f,
            "offscreen carrierless distance uses original slow .0005 acceleration, not default smoothing");
        next = Boundary(3, carrier: -1, type: 0, flight: 1,
            targetCamera: new SourceVector3(100, 100, 100), receiverCamera: new SourceVector3(0, 0, 100));
        model.BeginWake(next, false);
        model.Prepare(Seed(), next, 512, 384, NoRandom);
        Check(!model.TargetOffscreen, "one visible cached point prevents the original BOTH-offscreen latch");
        next = Boundary(4, carrier: 0, ballFlags: 0x100);
        model.BeginWake(next, false);
        model.Prepare(Seed(), next, 512, 384, NoRandom);
        Check(!model.TargetOffscreen, "actual carrier clears the original persistent BF_TGT_OFFSCRN");
    }

    private static void Gopher()
    {
        var playback = Playback();
        playback.CameraSourceTick(Boundary(1, mode: 3), 512, 384, (b, s) => 1);
        playback.ObserveCameraProcessSuspension(true);
        Reject(() => playback.CameraSourceTick(Boundary(4), 512, 384, NoRandom),
            "a suspended source camera cannot execute");
        playback.ObserveCameraProcessSuspension(false);
        playback.CameraSourceTick(Boundary(8, mode: 1), 512, 384, NoRandom);
        Near(playback.SourceState.Eye.X, -1000, 0, "was_playover2 executes original GOPHER before alignment");
        Near(playback.SourceState.Focus.Y, -75, 0, "GOPHER retains original underground focus");
        Check(playback.OriginalNormal.WasPlayOver == 1, "process wakes, not skipped menu ticks, decrement original state");
        playback.CameraSourceTick(Boundary(9, mode: 1), 512, 384, NoRandom);
        Check(!playback.OriginalNormal.LastPlan.Gopher && playback.SourceState.Eye.Y > 0f,
            "the next actual pre-snap wake snaps out of GOPHER");
        Near(playback.SourceState.Velocity.X, 0, 0, "pre-snap snap resets original velocity");
        Near(playback.SourceState.DistanceVelocity, 0, 0, "pre-snap snap resets original distance velocity");
    }

    private static void ImpactReturn()
    {
        var playback = Playback();
        playback.CameraSourceTick(Boundary(1), 512, 384, NoRandom);
        long before = playback.OriginalNormal.NormalWakes;
        Check(playback.ExecuteContactSelection(1, 1, 4, "synthetic_contact", 123, 0),
            "actual source contact selector must start the supplied eligible transient");
        for (int i = 2; i <= 4; i++)
        {
            playback.CameraSourceTick(Boundary(i, actorX: 100 + i), 512, 384, NoRandom);
            Check(playback.OriginalNormal.NormalWakes == before && playback.IsTransientActive,
                "original normal state must not advance behind a special camera");
        }
        SourceCinematicSeed beforeReturn = playback.SourceState;
        playback.CameraSourceTick(Boundary(5), 512, 384, NoRandom);
        Check(!playback.IsTransientActive && playback.Frame.ReturnedToNormal &&
            playback.OriginalNormal.NormalWakes == before + 1, "ModeRevert executes original normal on the expiry wake");
        Check(playback.Frame.ElapsedTicks == 4, "original zoom expiry increments tick_cnt after ModeRevert");
        Check(playback.SourceState.ThetaVelocity != 0f,
            "travel return retains actual transient angle velocity for original normal integrator");
        float delta = SourceCinematicMath.Radians(90) - beforeReturn.Theta;
        if (delta > SourceCinematicMath.TwoPi / 2f) delta -= SourceCinematicMath.TwoPi;
        if (delta <= -SourceCinematicMath.TwoPi / 2f) delta += SourceCinematicMath.TwoPi;
        Near(playback.SourceState.Theta, beforeReturn.Theta + beforeReturn.ThetaVelocity * .35f + .08f * delta,
            .000001f, "travel return must integrate actual transient angular state rather than snap/reseed");
        long count = playback.OriginalNormal.NormalWakes;
        playback.CameraSourceTick(Boundary(6), 512, 384, NoRandom);
        Check(playback.OriginalNormal.NormalWakes == count + 1 && !playback.IsResumingNormal,
            "after return there is one normal dispatch, not an independent perpetual return loop");
    }

    private static void RandomOrder()
    {
        var playback = Playback();
        playback.CameraSourceTick(Boundary(1, mode: 1), 512, 384, NoRandom);
        playback.ExecuteShakerOpcode(1, 1, 0, 0, "synthetic_shaker", 124);
        var draws = new List<string>();
        playback.CameraSourceTick(Boundary(2, mode: 3), 512, 384,
            (b, s) => { draws.Add(b + ":" + s); return draws.Count == 3 ? 0 : 2; });
        Check(draws.Count == 3 && draws[0].Contains("normal-sideline") &&
            draws[1].Contains("shaker-X") && draws[2].Contains("shaker-Z"),
            "conditional normal sideline draw precedes the two shared shaker draws");
        Near(playback.SourceState.Focus.X, playback.OriginalNormal.Integrate(playback.OriginalNormal.LastPlan).Focus.X + 8f,
            .0001f, "original shaker affects the persistent focus/eye state, not a render-only offset");
        Near(playback.SourceState.Focus.Z, playback.OriginalNormal.Integrate(playback.OriginalNormal.LastPlan).Focus.Z - 8f,
            .0001f, "original shaker X then Z requests must retain their distinct source results");
        draws.Clear();
        playback.CameraSourceTick(Boundary(3, mode: 3), 512, 384, (b, s) => { draws.Add(s); return 1; });
        Check(draws.Count == 2 && !draws.Any(s => s.Contains("sideline")),
            "play-over conditional draw occurs once per pre-snap reset, not every render/wake");
        Reject(() => playback.CameraSourceTick(Boundary(3), 512, 384, NoRandom), "duplicate source wake rejected");
        var clean = Playback();
        clean.CameraSourceTick(Boundary(1), 512, 384, NoRandom);
        Reject(() => clean.CameraSourceTick(Boundary(3), 512, 384, NoRandom), "omitted source wake rejected");
    }

    private static void ImmutableBoundary()
    {
        var initial = Boundary(1);
        var actors = initial.Actors.ToArray();
        var copied = new SourceCameraBoundary(1, 0, 2, 0, 0, 0, 1, 0, 0, 30, 0, false, false, 0, 0, initial.Ball, actors);
        actors[0] = new SourceCameraActor(0, 7, 0, 0, new SourceVector3(999, 0, 999), default(SourceVector3));
        Near(copied.Actors[0].Position.X, 100, 0, "camera boundary must detach every raw actor root");
        Check(copied.Ball.Target.X == 177 && copied.Ball.Position.X == -20 && copied.Ball.Handler == 3,
            "immutable boundary preserves distinct ball origin/velocity/lead/handler observations");
        Reject(() => new SourceCameraBoundary(1, 0, 2, 0, 0, 0, 1, 0, 0, 30, 0, false, false, 0, 0,
            initial.Ball, new SourceCameraActor[13]), "incomplete actor identity observation is unavailable, never guessed");
    }

    private static void CoreObservation()
    {
        var stations = new[] {
            new SourceStationConfiguration(0, TeamId.Home, 3), new SourceStationConfiguration(1, TeamId.Home, 1),
            new SourceStationConfiguration(2, TeamId.Away, 10), new SourceStationConfiguration(3, TeamId.Away, 8)
        };
        var model = new SimulationState
        {
            SourceMode = true,
            SourceTick = 77,
            Phase = GamePhase.LiveBall,
            Possession = TeamId.Home,
            Quarter = 2,
            LineOfScrimmage = 94,
            SourceLastPossession = 1,
            CarrierId = 3,
            SourceRules = new SourceRulesState(0, 0, 2, 33, 43),
            SourceConfiguration = new SourceMatchConfiguration(SourceSimulationProfile.OriginalRelease57Hz,
                SourceStationLayout.FourStation, new SourceTeamSelection(0, new[] { 0, 1, 2, 3, 4, 5, 6 }),
                new SourceTeamSelection(1, new[] { 0, 1, 2, 3, 4, 5, 6 }), stations, TeamId.Home, SourceCaptainSlot.First),
            SourcePresentation = new SourcePresentationSettings(TeamId.Home, new[] { -1, -1, -1, -1 }, 0, 1),
            SourceFlags = SourceGameFlags.PassThrown | SourceGameFlags.HikingBall,
            SourceBallPosition = new SourceVector3(-101, 29, 57),
            SourceBallOrigin = new SourceVector3(-131, 0, -17),
            SourceBallTarget = new SourceVector3(233, 0, -101),
            SourceCameraBallTarget = new SourceVector3(47, 61, 113),
            SourceBallVelocity = new FieldVector(3, 4),
            SourceBallVerticalVelocity = 5,
            BallPosition = new FieldVector(999, 888),
            BallKind = SourceBallKind.Pass,
            TargetPlayerId = 5,
            ThrowerId = 3,
            PassTicksElapsed = 1,
            SourceBallFlags = SourceBallFlags.TargetOffscreen | SourceBallFlags.Phantom
        };
        for (int id = 0; id < 14; id++)
        {
            var actor = model.Players[id];
            actor.SourceCoordinates = true; actor.SourcePosition = new FieldVector(17 + id * 3, 91 - id);
            actor.SourceHeight = 7 + id; actor.SourceMode = id == 3 ? SourceActorMode.Quarterback : SourceActorMode.Receiver;
            actor.Facing = 73 + id; actor.Position = new FieldVector(777, -888);
            model.SourceCameraGroundCenters[id] = new SourceVector3(id, id + 4, id + 100);
        }
        model.Players[4].Facing = -2055;
        var capture = typeof(GameSnapshot).GetMethod("CaptureCamera",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        var boundary = (SourceCameraBoundary)capture.Invoke(null, new object[] { model, 9 });
        Check(boundary.SourceTick == 77 && boundary.ProcessOrder == 9 && boundary.RawGameMode == 2 &&
            boundary.QuarterIndex == 1 && boundary.OffenseSide == 0 && boundary.LastPossession == 1 &&
            boundary.LineOfScrimmage == 33 && boundary.HumanTeammates && boundary.Pullback,
            "actual Core camera snapshot retains raw tick/order/mode/quarter/possession/LOS/HH/pullback, not IGameState inference");
        Check(boundary.Actors[3].Position.X == 26 && boundary.Actors[3].Position.Y == 10 &&
            boundary.Actors[3].Position.Z == 88 && boundary.Actors[3].Heading == 76 && boundary.Actors[3].RawMode == 5 &&
            boundary.Actors[3].GroundCameraPosition.Z == 103,
            "actual Core camera snapshot retains exact source actor roots, modes, headings and preceding ground cache");
        Check(boundary.Actors[4].Heading == -2055 &&
            SourceCinematicTarget.FromRawActor(boundary.Actors[4]).Facing == -2055,
            "actual Core camera preserves raw unwrapped actor facing without normalization or a new validity failure");
        Check(boundary.Ball.Position.X == -101 && boundary.Ball.Origin.X == -131 && boundary.Ball.Target.X == 233 &&
            boundary.Ball.Velocity.X == 3 && boundary.Ball.Velocity.Y == 5 && boundary.Ball.Velocity.Z == 4 &&
            boundary.Ball.Handler == 3 && boundary.Ball.Receiver == 5 && boundary.Ball.FlightTime == 1 &&
            boundary.Ball.RawFlags == 0x900 && boundary.Ball.TargetCameraPosition.X == 47,
            "actual Core camera snapshot retains raw ball endpoints/velocity/identity/flags instead of public yards or receiver guesses");
        model.SourceBallTarget = new SourceVector3(999, 0, 0);
        model.Players[3].SourcePosition = new FieldVector(-999, 0);
        Check(boundary.Ball.Target.X == 233 && boundary.Actors[3].Position.X == 26,
            "actual Core camera observation remains immutable after actor/ball process writes");
        model.Phase = GamePhase.PreSnap;
        model.SourceRules = new SourceRulesState(0, 0, 1, 30, 100, SourcePlayType.Kickoff);
        model.SourceKickApproach = false;
        Check(GameSnapshot.RawCameraGameMode(model) == 1 && GameSnapshot.RawMovementPhase(model) == 5,
            "camera raw kickoff mode is PRE_SNAP before actual approach; the inherited movement adapter remains unchanged");
        model.SourceKickApproach = true;
        Check(GameSnapshot.RawCameraGameMode(model) == 5,
            "camera raw kickoff mode reaches GM_PRE_KICKOFF only at actual Core approach");
    }

    private static void CoreFeedback()
    {
        var game = new GameSimulation();
        game.Model.SourceMode = true;
        game.Model.SourceTick = 77;
        game.Model.SourceBallFlags = SourceBallFlags.White | SourceBallFlags.Bounce;
        Reject(() => game.ApplySourceCameraFeedback(new SourceCameraFeedback(77, true)),
            "actual Core rejects camera writes outside its executing pre-player wake");
        var executing = typeof(GameSimulation).GetField("executingSourceCamera",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        executing.SetValue(game, true);
        game.ApplySourceCameraFeedback(new SourceCameraFeedback(77, true));
        Check((int)game.Model.SourceBallFlags == 0x121, "actual Core feedback changes only original target-offscreen flag");
        Reject(() => game.ApplySourceCameraFeedback(new SourceCameraFeedback(77, false)),
            "actual Core rejects duplicate camera feedback on one source wake");
        game.Model.SourceTick++;
        game.Model.SourceRules = new SourceRulesState(0, 0, 1, 30, 100, SourcePlayType.Kickoff);
        game.ApplySourceCameraFeedback(new SourceCameraFeedback(78, false, 37));
        Check((int)game.Model.SourceBallFlags == 0x21 && game.Model.PreSnapTicksRemaining == 37,
            "actual Core retains unrelated raw ball flags and original opening kickoff auto_hike feedback");
        executing.SetValue(game, false);
    }

    private static void RasterAndFrame()
    {
        var playback = Playback();
        Reject(() => playback.RequireSourceCameraAtTick(0), "initial/render-only frame is not a source wake");
        Reject(() => playback.CameraSourceTick(Boundary(1), 1024, 768, NoRandom),
            "browser content viewport must not replace the actual DIP source raster");
        playback.CameraSourceTick(Boundary(1), 512, 256, NoRandom);
        var frame = playback.RequireSourceCameraAtTick(1);
        Check(frame.Position.X == playback.SourceState.Eye.X && frame.Transform.Count == 12,
            "movement consumes the same current raw optical transform");
        Reject(() => playback.RequireSourceCameraAtTick(0), "stale movement frame rejected");
        long wakes = playback.OriginalNormal.NormalWakes;
        for (int i = 0; i < 11; i++) playback.RequireSourceCameraAtTick(1);
        Check(playback.OriginalNormal.NormalWakes == wakes, "render/movement reads never advance normal integrator");
        Near(SourceCameraProjection.OpticalAspect(512, 256), 4f / 3f, .000001f,
            "low DIP raster2/3 vertical scale remains distinct from optical4/3 and browser viewport");
        Near(SourceCameraProjection.VerticalFieldOfView(512, 256),
            SourceCameraProjection.VerticalFieldOfView(512, 384), .000001f, "both raw DIP projections preserve inherited optical transform");
    }

    private static void Kickoff()
    {
        for (int ordinal = 0; ordinal < 11; ordinal++)
        {
            var input = Boundary(0, mode: 5, carrier: -1, type: 3, quarter: 0, hh: true);
            var playback = new SourceCinematicPlayback(Library(), SyntheticData(), input);
            var draws = new List<int>();
            playback.CameraSourceTick(Boundary(1, mode: 5, carrier: -1, type: 3, quarter: 0, hh: true), 512, 384,
                (b, s) => { draws.Add(b); return ordinal; });
            Check(draws.SequenceEqual(new[] { 11 }) && playback.Frame.Table == "csi_kickoff" &&
                playback.Frame.Ordinal == ordinal && playback.CameraFeedback.OpeningAutoHikeTicks == 37 + ordinal,
                "opening kickoff must retain all weighted ordinals, exact conditional request and auto_hike feedback");
            Near(playback.SourceState.Distance, 27f * 7.8f, .001f, "kickoff distance uses the actual HH parameter dimension");
            playback.CameraSourceTick(Boundary(2, mode: 5, carrier: -1, type: 3, quarter: 0), 512, 384, NoRandom);
            Check(playback.OriginalNormal.NormalWakes == 1, "opening kickoff never runs a hidden second normal loop");
            playback.CameraSourceTick(Boundary(3, mode: 2, carrier: 1, type: 3, quarter: 0), 512, 384, NoRandom);
            Check(!playback.IsTransientActive && !playback.OriginalNormal.KickoffFlag &&
                playback.OriginalNormal.NormalWakes == 2, "actual carrier/bounce ends kickoff before dispatch on that wake");
        }
    }

    private static SourceCameraBoundary GoalBoundary(long tick, int side, int mode, bool selected = true, bool kicked = false)
    {
        var input = Boundary(tick, mode: mode, side: side, carrier: side * 7 + 3, actorMode: 5,
            flags: kicked ? 0x200 : 0, type: selected ? 4 : 0);
        return new SourceCameraBoundary(tick, input.ProcessOrder, mode, input.RawGameFlags, selected ? 2 : 0,
            input.RawPlayResult, input.QuarterIndex, side, side, input.LineOfScrimmage, input.Carrier,
            input.HumanTeammates, input.Pullback, input.AutoHikeTicks, selected ? 4 : 0, input.Ball, input.Actors);
    }

    private static void FieldGoalLifecycle()
    {
        foreach (int side in new[] { 0, 1 })
        foreach (bool menu in new[] { false, true })
        foreach (bool nextGoal in new[] { true, false })
        {
            var playback = new SourceCinematicPlayback(Library(), SyntheticData(), GoalBoundary(0, side, 1));
            playback.CameraSourceTick(GoalBoundary(1, side, 1), 512, 384, NoRandom);
            var draws = new List<string>();
            playback.CameraSourceTick(GoalBoundary(2, side, 2, kicked: true), 512, 384,
                (bound, site) => { draws.Add(bound + ":" + site); return 0; });
            Check(draws.SequenceEqual(new[] { "2:cambot.c:3001/select-csi-fg-track" }),
                "field-goal selection uses its actual shared request before the first mode tick");
            playback.CameraSourceTick(GoalBoundary(3, side, 2, kicked: true), 512, 384, NoRandom);
            playback.CameraSourceTick(GoalBoundary(4, side, 3, kicked: true), 512, 384, NoRandom);
            int retainedTicks = playback.Frame.ElapsedTicks;
            long normalWakes = playback.OriginalNormal.NormalWakes;
            Check(retainedTicks == 3 && playback.IsTransientActive && playback.SourceTargetKind == 3 &&
                playback.TargetActorId == -1, "field-goal tracking retains its real non-actor target and source clock");
            long tick = menu ? 205 : 5;
            if (menu)
            {
                playback.ObserveCameraProcessSuspension(true);
                Reject(() => playback.CameraSourceTick(GoalBoundary(tick, side, 1, nextGoal), 512, 384, NoRandom),
                    "actual menu suspension cannot execute a field-goal camera wake");
                Check(playback.LastCameraTick == 4 && playback.Frame.ElapsedTicks == retainedTicks &&
                    playback.OriginalNormal.NormalWakes == normalWakes,
                    "menu time neither resets nor advances original dispatch state");
                playback.ObserveCameraProcessSuspension(false);
            }
            try
            {
                playback.CameraSourceTick(GoalBoundary(tick, side, 1, nextGoal), 512, 384, NoRandom);
            }
            catch (InvalidOperationException error)
            {
                throw new InvalidOperationException("field-goal new-play retains source dispatch clock/target: " + error.Message, error);
            }
            Check(!playback.IsTransientActive && playback.RawMode == 0 &&
                playback.Frame.ElapsedTicks == retainedTicks && playback.SourceTargetKind == 3 &&
                playback.TargetActorId == -1,
                "field-goal new-play retains source dispatch clock/target");
            Check(playback.OriginalNormal.LastPlan.Gopher && playback.SourceState.Eye.X == -1000f &&
                playback.OriginalNormal.WasPlayOver == 1 && playback.OriginalNormal.NormalWakes == normalWakes + 1 &&
                !playback.CameraFeedback.FieldGoalLastCarrier.HasValue,
                "new play executes exactly one original GOPHER without a field-goal re-entry or extra feedback");
            playback.CameraSourceTick(GoalBoundary(tick + 1, side, 1, nextGoal), 512, 384, NoRandom);
            Check(!playback.OriginalNormal.LastPlan.Gopher && playback.OriginalNormal.WasPlayOver == 0 &&
                playback.SourceState.Eye.Y > 0 && playback.SourceState.Velocity.X == 0 &&
                playback.Frame.ElapsedTicks == retainedTicks && playback.OriginalNormal.NormalWakes == normalWakes + 2,
                "the following source wake snaps once and retains the dormant CSI clock");
        }
    }

    private static void MatchReset()
    {
        var playback = Playback();
        playback.CameraSourceTick(Boundary(1), 512, 384, NoRandom);
        playback.ExecuteContactSelection(1, 1, 4, "synthetic_contact", 123, 0);
        playback.CameraSourceTick(Boundary(2), 512, 384, NoRandom);
        playback.ExecuteShakerOpcode(2, 1, 0, 0, "synthetic_shaker", 124);
        Check(playback.IsTransientActive && playback.TargetActorId == 4 && playback.NeedsShakeDraws,
            "new-match reset control starts with actual active mode, target, clock and pending shaker");
        var initial = Boundary(0, side: 1);
        playback.Reset(initial);
        Check(!playback.IsTransientActive && !playback.IsResumingNormal && !playback.HasRenderFrame &&
            !playback.NeedsShakeDraws && playback.TargetActorId == -1 && playback.SourceTargetKind == 0 &&
            playback.LastCameraTick == -1 && playback.Frame.ElapsedTicks == 0 &&
            playback.OriginalNormal.WasPlayOver == 1 && playback.OriginalNormal.NormalWakes == 0 &&
            playback.LastCameraBoundary == null,
            "new match still fully clears transient/target/clock/shaker and original process state");
        Reject(() => playback.RequireSourceCameraAtTick(2), "new match invalidates the prior movement camera frame");
        var clean = new SourceCinematicPlayback(Library(), SyntheticData(), initial);
        var input = Boundary(1, side: 1);
        playback.CameraSourceTick(input, 512, 384, NoRandom);
        clean.CameraSourceTick(input, 512, 384, NoRandom);
        Check(playback.CurrentSourceCamera.Transform.SequenceEqual(clean.CurrentSourceCamera.Transform) &&
            playback.SourceState.Focus.X == clean.SourceState.Focus.X &&
            playback.SourceState.Eye.X == clean.SourceState.Eye.X &&
            playback.SourceState.Distance == clean.SourceState.Distance && playback.Frame.ElapsedTicks == 0,
            "a reset epoch starts at the same normal state as a fresh production playback owner");
    }

    private static SourceCinematicPlayback Playback()
    { return new SourceCinematicPlayback(Library(), SyntheticData(), Boundary(0)); }

    private static SourceNormalCameraData SyntheticData()
    {
        var entries = new SourceKickoffCameraDefinition[11];
        for (int i = 0; i < entries.Length; i++)
            entries[i] = new SourceKickoffCameraDefinition(new SourceCinematicEntry("csi_kickoff", i, 1, 19, "CAM_P2P",
                12, 12, 23, -18, 137, new[] { new SourceVector3(.1f, .2f, .3f),
                    new SourceVector3(0, 0, 1), default(SourceVector3) }), new SourceVector3(315, 66, -112), 37 + i);
        var goals = new SourceCinematicEntry[2];
        for (int i = 0; i < 2; i++)
            goals[i] = new SourceCinematicEntry("csi_fs_fgtrk", i, 1, 9, "CAM_SP_CNFR", 40, 0, 0, -12, 171,
                new[] { new SourceVector3(0, .1f, .2f), new SourceVector3(0, .3f, .7f) });
        return new SourceNormalCameraData(entries, goals);
    }

    private static SourceCinematicLibrary Library()
    {
        string text = "{\"schema\":\"original-impact-presentation/v1\",\"source_revision\":\"" +
            SourceTeamRosterLibrary.PinnedSourceRevision + "\",\"source_tree\":\"" +
            SourceTeamRosterLibrary.PinnedSourceTree + "\",\"profile\":\"normal-release-57hz-no-debug-no-NOSETCAM\"," +
            "\"tick_rate\":57,\"camera_mode_count\":22,\"tables\":[{\"symbol\":\"synthetic_impact\",\"entries\":[" +
            "{\"ordinal\":0,\"source_line\":1,\"mode\":5,\"mode_symbol\":\"CAM_ZO_NR\",\"mode_ticks\":3," +
            "\"travel_ticks\":3,\"theta_degrees\":29,\"phi_degrees\":-31,\"distance\":167,\"control_points\":[]}]}]," +
            "\"shaker_exp\":[" + string.Join(",", Enumerable.Repeat("1", 64)) + "]," +
            "\"animation_cues\":[{\"script\":\"synthetic_shaker\",\"line\":124,\"kind\":\"shaker\"," +
            "\"arguments\":[8,6],\"execution\":\"synthetic executed fixture\"}],\"inputs\":[]," +
            "\"contact_camera_sites\":[{\"function\":\"synthetic_contact\",\"line\":123,\"table\":\"synthetic_impact\"}]," +
            "\"qualifications\":[\"Authored synthetic branch fixture; not original numeric data or gameplay proof\"]}";
        byte[] bytes = Encoding.UTF8.GetBytes(text);
        return SourceCinematicLibrary.Read(bytes, AuthorizedAssetRoot.Sha256(bytes));
    }

    private static void CompareNormal(string path)
    {
        string[] expected = File.ReadAllLines(path);
        Check(expected.Length == 1440, "original-C exact normal sequence inventory");
        int row = 0;
        for (int scenario = 0; scenario < 6; scenario++)
        {
            var model = new SourceNormalCameraModel();
            SourceCinematicSeed state = Seed(0);
            for (int tick = 0; tick < 240; tick++)
            {
                int side = scenario % 2, direction = side != 0 ? -1 : 1, qb = side * 7 + 3;
                int carrier = tick < 90 ? qb : tick < 145 ? -1 : side * 7 + 1;
                var actors = new SourceCameraActor[14];
                SourceVector3 ballPosition = new SourceVector3(direction * (-180f + tick * 1.1f), 0, tick % 71 - 35f);
                for (int id = 0; id < actors.Length; id++)
                    actors[id] = new SourceCameraActor(id, id == qb ? 5 : 7, 0, 700,
                        id == carrier ? ballPosition : new SourceVector3(direction * (-156f + (id % 7 - 3) * 6f), 0, (id % 7 - 3) * 10f),
                        new SourceVector3(0, 0, 100));
                var input = new SourceCameraBoundary(tick, 0, tick < 25 ? 1 : tick < 225 ? 2 : 3,
                    tick >= 225 ? 0x400 : 0, 0, tick >= 225 ? 5 : 0, 1, side, side, 30, carrier,
                    scenario / 2 == 1, scenario / 2 == 2, 0, 0,
                    new SourceCameraBall(ballPosition, default(SourceVector3), default(SourceVector3),
                        new SourceVector3(direction * 155f, 0, 90), new SourceVector3(90, 60, 100), 0,
                        tick >= 90 && tick < 145 ? 0x100 : 0, tick >= 90 ? tick - 89 : 0, qb, side * 7 + 1), actors);
                model.BeginWake(input, false);
                state = model.Integrate(model.Prepare(state, input, 512, 384, (b, s) => 1));
                model.CompleteWake(input);
                float[] actual = { state.Eye.X, state.Eye.Y, state.Eye.Z, state.Focus.X, state.Focus.Y, state.Focus.Z, state.Distance };
                string[] values = expected[row++].Split('\t');
                for (int k = 0; k < actual.Length; k++)
                    Near(actual[k], float.Parse(values[k + 2], Invariant), .005f,
                        "original C normal scalar scenario=" + scenario + " tick=" + tick + " column=" + k);
            }
        }
    }

    private static void CompareActive(string path)
    {
        string directory = Path.GetDirectoryName(Path.GetFullPath(path));
        var index = JsonDocument.Parse(File.ReadAllBytes(path));
        var libraryBytes = File.ReadAllBytes(Path.Combine(directory, "impact.json"));
        var library = SourceCinematicLibrary.Read(libraryBytes, AuthorizedAssetRoot.Sha256(libraryBytes));
        var data = SourceNormalCameraData.Read(File.ReadAllBytes(Path.Combine(directory, "normal.json")));
        var shaker = library.AnimationCues.First(c => c.Kind == "shaker");
        string[] tables = { "csi_fs_endplay", "csi_fs_endqtr", "csi_sn_diveattack", "csi_zm_divetack",
                "csi_rs_suplex", "csi_rs_suplex_nf", "csi_zm_endplayfg" };
        var maxima = new float[45];
        int total = 0, caseCount = 0;
        var observations = new List<object>();
        foreach (var item in index.RootElement.GetProperty("cases").EnumerateArray())
        {
            string name = item.GetProperty("name").GetString();
            string[] inputLines = File.ReadAllLines(item.GetProperty("inputs").GetString());
            string[] expectedLines = File.ReadAllLines(item.GetProperty("expected").GetString());
            Check(inputLines.Length == expectedLines.Length, "actual C/input wake inventory " + name);
            int opening = item.GetProperty("opening").GetInt32(), serial = 0, lastCarrier = 0;
            SourceCinematicPlayback playback = null;
            var observed = new List<string>();
            for (int row = 0; row < inputLines.Length; row++)
            {
                string[] values = inputLines[row].Split('\t');
                Func<int, int> integer = n => int.Parse(values[n], Invariant);
                Func<int, float> number = n => float.Parse(values[n], Invariant);
                Func<int, SourceVector3> vector = n => new SourceVector3(number(n), number(n + 1), number(n + 2));
                var actors = new SourceCameraActor[14];
                for (int actor = 0; actor < actors.Length; actor++)
                {
                    int start = 36 + actor * 9;
                    actors[actor] = new SourceCameraActor(actor, integer(start), integer(start + 1),
                        integer(start + 2), vector(start + 3), vector(start + 6));
                }
                var input = new SourceCameraBoundary(integer(0), 0, integer(1), integer(2), integer(3), integer(4),
                    integer(5), integer(6), integer(7), integer(8), integer(9), integer(10) != 0, integer(11) != 0,
                    integer(12), integer(13), new SourceCameraBall(vector(16), vector(19), vector(22), vector(25),
                        vector(28), integer(31), integer(32), integer(33), integer(34), integer(35)), actors);
                if (playback == null) playback = new SourceCinematicPlayback(library, data, input);
                if (row > 0 && input.SourceTick != playback.LastCameraTick + 1)
                {
                    playback.ObserveCameraProcessSuspension(true);
                    playback.ObserveCameraProcessSuspension(false);
                }
                var draws = new List<int>();
                playback.CameraSourceTick(input, number(14), number(15), (bound, site) =>
                {
                    int result = bound == 11 && opening >= 0 ? opening : (serial * 5 + 2) % bound;
                    serial++; draws.Add(bound); draws.Add(result); return result;
                });
                string[] expected = expectedLines[row].Split('\t');
                string label = name + " sourceTick=" + input.SourceTick;
                Check(int.Parse(expected[0], Invariant) == input.SourceTick, "exact original source wake " + label);
                Check(int.Parse(expected[1], Invariant) == playback.RawMode, "original active dispatch mode " + label);
                Check(int.Parse(expected[2], Invariant) == playback.OriginalNormal.WasPlayOver, "original was_playover wake state " + label);
                Check(int.Parse(expected[3], Invariant) == (playback.OriginalNormal.KickoffFlag ? 1 : 0), "original kickoff lifetime " + label);
                Check(int.Parse(expected[4], Invariant) == playback.OriginalNormal.NormalWakes, "original normal dispatch count " + label);
                int outputFlags = (input.Ball.RawFlags & ~0x100) | (playback.CameraFeedback.TargetOffscreen ? 0x100 : 0);
                Check(int.Parse(expected[5], Invariant) == outputFlags, "original ball target flag feedback " + label);
                Check(int.Parse(expected[6], Invariant) == (playback.CameraFeedback.OpeningAutoHikeTicks ?? input.AutoHikeTicks),
                    "original kickoff auto_hike feedback " + label);
                SourceNormalCameraPlan plan = playback.OriginalNormal.LastPlan;
                Check(int.Parse(expected[7], Invariant) == plan.CarrierMode, "original exact carrier mode " + label);
                Check(int.Parse(expected[8], Invariant) == plan.PlayerMask, "original exact tracked actor mask " + label);
                Check(int.Parse(expected[9], Invariant) == plan.PlayerCount, "original exact tracked actor count " + label);
                Check(int.Parse(expected[10], Invariant) == playback.Frame.ElapsedTicks,
                    "original dispatch tick_cnt survives mode-only return " + label);
                Check(int.Parse(expected[11], Invariant) * 2 == draws.Count, "actual conditional camera RNG request count " + label);
                for (int k = 0; k < draws.Count; k++)
                    Check(int.Parse(expected[12 + k], Invariant) == draws[k], "actual conditional camera RNG bound/result order " + label);
                SourceCinematicSeed s = playback.SourceState;
                var floats = new List<float> { s.Eye.X, s.Eye.Y, s.Eye.Z, s.Focus.X, s.Focus.Y, s.Focus.Z,
                        s.Distance, s.Theta, s.Phi, s.ThetaTarget, s.PhiTarget,
                        s.Velocity.X, s.Velocity.Y, s.Velocity.Z, s.DistanceVelocity, s.ThetaVelocity, s.PhiVelocity,
                        s.Acceleration.X, s.Acceleration.Y, s.Acceleration.Z, s.DistanceAcceleration,
                        s.ThetaAcceleration, s.PhiAcceleration, s.SinTheta, s.CosTheta, s.SinPhi, s.CosPhi };
                floats.AddRange(playback.CurrentSourceCamera.Transform);
                floats.AddRange(new[] { plan.Target.X, plan.Target.Y, plan.Target.Z, plan.DistanceTarget,
                        plan.HorizontalDistance, plan.DepthDistance });
                int offset = 12 + draws.Count;
                for (int k = 0; k < floats.Count; k++)
                {
                    if (k >= 39 && playback.RawMode != 0) continue;
                    float reference = float.Parse(expected[offset + k], Invariant);
                    maxima[k] = Math.Max(maxima[k], Math.Abs(floats[k] - reference));
                    float tolerance = k >= 23 && k <= 38 || k == 7 || k == 8 || k == 9 || k == 10 ||
                        k == 15 || k == 16 || k == 21 || k == 22 ? .00004f : .01f;
                    Near(floats[k], reference, tolerance, "original active C scalar " + label + " column=" + k);
                }
                int telemetry = offset + floats.Count;
                int expectedCarrier = int.Parse(expected[telemetry], Invariant);
                Check(playback.CameraFeedback.FieldGoalLastCarrier.HasValue == (expectedCarrier != lastCarrier),
                    "original exact field-goal last_carrier feedback occurrence " + label);
                lastCarrier = playback.CameraFeedback.FieldGoalLastCarrier ?? lastCarrier;
                Check(lastCarrier == expectedCarrier, "original exact field-goal last_carrier feedback " + label);
                Check(playback.SourceTargetKind == int.Parse(expected[telemetry + 1], Invariant) &&
                    playback.TargetActorId == int.Parse(expected[telemetry + 2], Invariant),
                    "original target identity survives mode-only return " + label);
                observed.Add(input.SourceTick + "\t" + playback.RawMode + "\t" +
                    string.Join("\t", floats.Select(f => f.ToString("R", Invariant))) + "\t" +
                    playback.Frame.ElapsedTicks + "\t" + plan.CarrierMode + "\t" + plan.PlayerMask + "\t" +
                    plan.PlayerCount + "\t" + lastCarrier + "\t" + playback.SourceTargetKind + "\t" + playback.TargetActorId);
                int eventTable = integer(162), eventOrdinal = integer(163), eventActor = integer(164), eventShake = integer(165);
                if (eventTable >= 0)
                {
                    string table = tables[eventTable];
                    serial++;
                    var cue = library.AnimationCues.FirstOrDefault(c => c.Kind == "camera" && c.Arguments[0] == table);
                    if (cue != null) playback.ExecuteCameraOpcode(input.SourceTick, 1, eventActor, cue.Script, cue.Line, eventOrdinal);
                    else
                    {
                        cue = library.ContactCameraSites.FirstOrDefault(c => c.Arguments[0] == table);
                        if (cue != null)
                            playback.ExecuteContactSelection(input.SourceTick, 1, eventActor, cue.Script, cue.Line, eventOrdinal);
                        else
                        {
                            var selector = typeof(SourceCinematicPlayback).GetMethod("Select",
                                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                            selector.Invoke(playback, new object[] { table, eventOrdinal, eventActor });
                        }
                    }
                }
                if (eventShake != 0)
                    playback.ExecuteShakerOpcode(input.SourceTick, 2, eventActor, input.Carrier, shaker.Script, shaker.Line);
            }
            string output = Path.Combine(directory, name + ".observed.tsv");
            File.WriteAllLines(output, observed);
            observations.Add(new
            {
                name = name,
                wakes = inputLines.Length,
                observed = output,
                observedSha256 = AuthorizedAssetRoot.Sha256(File.ReadAllBytes(output))
            });
            total += inputLines.Length; caseCount++;
        }
        Check(total == index.RootElement.GetProperty("source_wakes").GetInt32(), "original active C total exact source wake inventory");
        File.WriteAllText(Path.Combine(directory, "managed-comparison.json"), JsonSerializer.Serialize(
            new
            {
                status = "passed",
                sourceWakes = total,
                cases = caseCount,
                maximumAbsoluteErrorByColumn = maxima,
                observations = observations
            }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
