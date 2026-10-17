using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Blitz.AssetImport;
using Blitz.Contracts;
using Blitz.Gameplay;

internal static class GeometryCallerChecks
{
    private static int assertions;
    private static void Check(bool condition, string identity)
    {
        assertions++;
        if (!condition) throw new BallAssertionFailure("GEOMETRY CALLER ASSERTION: " + identity);
    }

    internal static int Run(FullKernelInputs real)
    {
        var actor = new ActorState(3) { Role = ActorRole.Quarterback, Action = ActorAction.Idle,
            ModelScale = real.Rosters.GetTeam(0).Players[3].ModelScale, Facing = 0 };
        actor.AnimationState = new SourceAnimationState(real.Animations, actor.Id);
        actor.AnimationState.FullBody("qb_stance_anim", actor, 0, n => 0, AnimationSelectionEvidence.SourceVerified);
        SourcePlayerDriver.SetSourcePosition(actor, default(SourceVector3));
        var conversion = new PlayerCoordinateConversion(SignedSourceAxis.PositiveX, SignedSourceAxis.PositiveY,
            SignedSourceAxis.NegativeZ, SourceTriangleOrder.Renderer321);
        if (real.StandingPose == null)
            throw new InvalidOperationException("Original matrix pose must be prepared from the pinned WAN, never substituted.");
        var reference = new SourcePoseEvaluator(real.Player, real.StandingPose, conversion);
        for (int joint = 0; joint < 15; joint++)
        {
            AssetVector3 p = reference.SourceVertexWorld(joint, real.Player.Nodes[joint].BindPivot);
            var expected = new SourceVector3(p.X * actor.ModelScale, p.Y * actor.ModelScale, -p.Z * actor.ModelScale);
            SourceVector3 actual = real.Geometry.WorldJoint(GameSnapshot.SourcePlayerView(actor, 0), joint);
            Check(Distance(actual, expected) < 0.00002f, "actual QBSTANCE matrix/pivot/source-basis/ascale joint " + joint);
        }
        bool wristOnly = false;
        foreach (string name in new[] { "C_JMPINT", "C_JMPIN3", "C_JMP1", "C_SPLITS", "C_SROL2" })
        {
            ISourceAnimationClip clip = real.Animations.GetClip(name);
            foreach (int frame in clip.SelectedSourceFrames)
            {
                var sample = new SourceAnimationSample(name, frame, frame);
                var animation = new ActorAnimationView(0, ActorAction.Catch, AnimationSelectionEvidence.SourceVerified,
                    new SourceAnimationLayer("actual_wrist_pose_probe", 1, sample),
                    new SourceVector3(0f, clip.GetFrame(frame).ScaledRoot.Y * actor.ModelScale, 0f));
                var view = new PlayerView(4, TeamId.Home, new FieldVector(50f, 0f), default(FieldVector), 1f,
                    facingDirection: new FieldVector(0f, 1f), role: ActorRole.Receiver,
                    animation: animation, modelScale: actor.ModelScale);
                SourceVector3 pelvis = real.Geometry.WorldJoint(view, 0), left = real.Geometry.WorldJoint(view, 7),
                    right = real.Geometry.WorldJoint(view, 4);
                float dx = left.X - right.X, dy = left.Y - right.Y, dz = left.Z - right.Z;
                float length = (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
                if (length < 0.01f) continue;
                SourceVector3 inside = Offset(left, dx / length, dy / length, dz / length, 4.999f);
                SourceVector3 outside = Offset(left, dx / length, dy / length, dz / length, 5.001f);
                if (Distance(inside, pelvis) <= 14f || Distance(inside, right) <= 5f) continue;
                Check(SourceCatchRules.InCatchDistance(inside, pelvis, left, right), "actual posed wrist catches beyond pelvis14");
                Check(!SourceCatchRules.InCatchDistance(outside, pelvis, left, right), "actual posed wrist rejects beyond strict5");
                wristOnly = true;
                break;
            }
            if (wristOnly) break;
        }
        Check(wristOnly, "original sampled pose provides a genuine wrist-only catch case");
        actor.Action = ActorAction.Throw;
        actor.AnimationState.FullBody("qb_throw_sr_anim", actor, 1, n => 0, AnimationSelectionEvidence.SourceVerified);
        var releaseTicks = new List<int>();
        for (int tick = 1; tick <= 20; tick++)
            actor.AnimationState.Advance(actor, tick, n => 0, signal =>
            {
                if ((signal & SourceAnimationSignal.ReleaseBall) != 0) releaseTicks.Add(tick);
            });
        Check(releaseTicks.SequenceEqual(new[] { 11 }), "real planted throw invokes release once at source offset10");
        actor.Action = ActorAction.Fall;
        actor.SourceIsCarrier = true; actor.SourcePlayOver = false;
        actor.AnimationState.FullBody("h_front1_anim", actor, 100, n => 0, AnimationSelectionEvidence.SourceVerified);
        for (int tick = 100; tick <= 700; tick++)
            actor.AnimationState.Advance(actor, tick, n => 0, signal =>
            {
                if ((signal & SourceAnimationSignal.PlayerDown) != 0) actor.SourcePlayOver = true;
            });
        Check(actor.AnimationState.Primary.ScriptSymbol == "m_wob_fu_anim" && actor.SourceLateHittable,
            "actual H_FRONT1 player_down callback precedes original90percent carrier wobble branch; no forced getup");
        var focus = real.Create(7);
        focus.Model.SourceRules = new SourceRulesState(0, 0, 1, 40, 70);
        focus.Model.LineOfScrimmage = 40f; focus.Model.FirstDownLine = 70f;
        focus.Model.Phase = GamePhase.PlaySelect; focus.Model.SourceBeginPlayInitialized = false;
        focus.Step(new PlayerCommand(source: new SourceCommand(SourceAction.SelectPlay,
            new SourcePlaySelection("play_zigzag", SourcePlaybookKind.Offense))));
        Check(focus.State.Tick == 1 && ((ISourceGameState)focus.State).SourceTick == 0
            && focus.State.Phase == GamePhase.PlaySelect, "skipped first host wake retains original play edge");
        IGameState beforeClear = focus.State;
        ((IPendingSourceInput)focus).ClearPendingInput();
        Check(ReferenceEquals(beforeClear, focus.State), "focus flush does not advance, reset or republish match");
        focus.Step(default(PlayerCommand));
        Check(focus.State.Phase == GamePhase.PlaySelect, "focus flush actually removes first-host queued source edge");
        focus.Restart(7);
        Check(focus.State.Tick == 0 && ((ISourceGameState)focus.State).SourceTick == 0 && focus.Events.Count == 0,
            "restart resets source/host schedule and transient events");
        actor.SourceControllerStation = 0;
        var raw = new PlayerCommand(stationInputs: new SourceInputFrame(new[] { new SourceStationInput(0, 3, 2) }));
        Check(SourcePlayerDriver.ActorStick(actor, raw) == 7 && real.Playbook.Directions.StickHeading(7) == 160,
            "raw49 station levels bypass lossy normalize/requantize");
        CatchControllerBoundary(real);
        return assertions;
    }

    // Explicit internal catch/carry setup; public no-state-mutation raw-B flow is covered separately.
    private static void CatchControllerBoundary(FullKernelInputs real)
    {
        var configuration = new SourceMatchConfiguration(SourceSimulationProfile.OriginalRelease57Hz,
            SourceStationLayout.TwoStation, new SourceTeamSelection(0, new[] { 0, 1, 2, 3, 4, 5, 6 }),
            new SourceTeamSelection(1, new[] { 0, 1, 2, 3, 4, 5, 6 }),
            new[] { new SourceStationConfiguration(0, TeamId.Home, 3),
                    new SourceStationConfiguration(1, TeamId.Away, 10) }, TeamId.Home, SourceCaptainSlot.First);
        var game = real.Create(configuration: configuration);
        game.Model.Phase = GamePhase.LiveBall;
        game.Model.SourceRules = new SourceRulesState(0, 0, 1, 20, 50);
        game.Model.Possession = TeamId.Home;
        game.Model.SourceTick = 1000;
        game.Model.CarrierId = -1;
        game.Model.BallKind = SourceBallKind.Pass;
        game.Model.BallMode = BallMode.InFlight;
        game.Model.SourceFlags = SourceGameFlags.None;
        game.Model.SourceBallFlags = SourceBallFlags.None;
        game.Model.SourceLastPossession = 0;
        game.Model.ThrowerId = 3;
        game.Model.TargetPlayerId = 11;
        game.Model.PassTicksElapsed = 45;
        game.Model.PassTicksTotal = 45;
        ActorState catcher = game.Model.Players[11];
        catcher.Role = ActorRole.Defender;
        catcher.NoCatchTicks = 0;
        SourcePlayerDriver.SetSourcePosition(catcher, default(SourceVector3));
        catcher.AnimationState.FullBody("qb_stance_anim", catcher, 1000, n => 0, AnimationSelectionEvidence.SourceVerified);
        SourceVector3 wrist = real.Geometry.WorldJoint(GameSnapshot.SourcePlayerView(catcher, 1000), 7);
        SourceBallDriver.SetPosition(game.Model, wrist);
        Invoke(game, "TrySourceCatch", catcher);
        Invoke(game, "Publish");
        Check(game.Model.CarrierId == 11, "actual original-pose catch caller establishes new carrier");
        Check(game.State.Ball.Control.ControllerStationId == 1 && game.State.Ball.Control.ControllerPlayerId == 11,
            "actual catch caller transfers human controller immediately at possession tick0");
        Check(game.State.Ball.Control.PossessionTicks == 0 && !game.State.Ball.Control.PossessionCreditEligible
            && game.State.Ball.Control.LastPossession == TeamId.Home,
            "actual catch caller keeps immediate ownership independent of uncredited last_pos");
        Check(game.SourceEvents.Any(e => e.Type == SourceEventType.ControllerChanged && e.ActorPlayerId == 11 && e.StationId == 1),
            "actual immediate controller reassignment emits independent station/actor identity");
        SourceBallControlView retained = game.State.Ball.Control;
        game.Model.SourceTick = 1017;
        Invoke(game, "SourceCarrierControl", catcher);
        Invoke(game, "Publish");
        Check(game.State.Ball.Control.ControllerPlayerId == 11 && game.State.Ball.Control.PossessionTicks == 17
            && !game.State.Ball.Control.PossessionCreditEligible && game.State.Ball.Control.LastPossession == TeamId.Home,
            "actual CONTROL_TIME17 preserves ownership without possession credit");
        game.Model.SourceTick = 1018;
        Invoke(game, "SourceCarrierControl", catcher);
        Invoke(game, "Publish");
        Check(game.State.Ball.Control.ControllerPlayerId == 11 && game.State.Ball.Control.PossessionTicks == 18
            && game.State.Ball.Control.PossessionCreditEligible && game.State.Ball.Control.LastPossession == TeamId.Away,
            "actual CONTROL_TIME18 credits possession without delaying ownership");
        Check(retained.PossessionTicks == 0 && retained.LastPossession == TeamId.Home,
            "retained actual catch snapshot is detached from later possession credit");
    }

    private static void Invoke(GameSimulation game, string name, params object[] arguments)
    {
        MethodInfo method = typeof(GameSimulation).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic);
        method.Invoke(game, arguments);
    }
    private static float Distance(SourceVector3 a, SourceVector3 b)
    {
        float x = a.X - b.X, y = a.Y - b.Y, z = a.Z - b.Z;
        return (float)Math.Sqrt(x * x + y * y + z * z);
    }
    private static SourceVector3 Offset(SourceVector3 from, float x, float y, float z, float distance)
    {
        return new SourceVector3(from.X + x * distance, from.Y + y * distance, from.Z + z * distance);
    }
}
