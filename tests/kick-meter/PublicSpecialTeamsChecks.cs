using System;
using System.Collections.Generic;
using System.Linq;
using Blitz.Contracts;
using Blitz.Gameplay;

internal static partial class Program
{
    private static readonly List<object> publicSpecialTrace = new List<object>();

    private sealed class PublicKickOriginal
    {
        private readonly GameSimulation game;
        private SourceMovementCameraFrame camera;
        private SourceVector3 rightAnkle, leftAnkle, ball, target;
        private int mode, flags, counter;
        private bool meterStarted, launchChecked;
        private long created;

        internal PublicKickOriginal(GameSimulation game, Cabinet cabinet)
        {
            this.game = game;
            if (reference == null) return;
            game.SourceCameraHeadingProvider = (snapshot, tick) => {
                int heading = FullKernelInputs.SetControlledMovementCamera(game, snapshot, tick);
                SimulationState m = game.Model;
                camera = m.SourceMovementCamera;
                int punter = (int)m.Possession * 7 + 3;
                rightAnkle = m.SourceCameraJoints[punter, 11]; leftAnkle = m.SourceCameraJoints[punter, 14];
                ball = m.SourceBallPosition; target = m.SourceBallTarget;
                mode = m.Phase == GamePhase.PreSnap ? 1 : m.Phase == GamePhase.LiveBall ? 2 : 3;
                flags = (int)m.SourceFlags;
                SourceKickMeterView meter = ((ISourceKickMeterState)game.State).KickMeter;
                counter = meter.IsSpecified && ((m.SourceFlags & SourceGameFlags.HikingBall) != 0
                    || m.SourceSnapTick >= meter.CreatedSourceTick) ? m.SourceHikeCounterAtSnap : m.PreSnapTicksRemaining;
                return heading;
            };
            cabinet.Observe = Observe;
        }

        private int InitialRaw(string identity)
        {
            SourceRandomDraw[] draws = ((SourceInjectedRandom)game.SourceRandomService).Trace
                .Where(d => d.Site.SourceTick == created && d.Site.SourceIdentity == identity).ToArray();
            return draws.Length == 0 ? 0 : draws.Single().Raw.Value;
        }

        private void Observe(SourceKickMeterView meter)
        {
            if (!meter.IsSpecified) return;
            if (!meterStarted)
            {
                created = meter.CreatedSourceTick;
                Check(reference.Ask("M", (int)meter.Kind, (int)meter.Owner, meter.SpecialTeamsAttribute,
                    meter.Fake ? 1 : 0, InitialRaw("plyrinfo.c:kick_meter_proc:wind"),
                    InitialRaw("plyrinfo.c:kick_meter_proc:wind_sign"),
                    InitialRaw("plyrinfo.c:punt_meter_proc:initial_strength"), 0, 0) == "READY",
                    "new raw S2 caller starts unchanged original-C meter with actual production raw draws");
                meterStarted = true;
            }
            if (game.SourceTick > created)
                CompareMeter(meter, reference.Row("T", game.SourceTick - created, mode, flags, counter),
                    created, "new-public-S2-original-C");
            SourceKickLaunchView launch = ((ISourceKickMeterState)game.State).LastKickLaunch;
            if (!launch.IsSpecified || launchChecked) return;
            launchChecked = true;
            SimulationState m = game.Model;
            bool punt = launch.Kind == SourceKickMeterKind.Punt;
            SourceRandomDraw[] draws = ((SourceInjectedRandom)game.SourceRandomService).Trace
                .Where(d => d.Site.SourceTick == launch.SourceTick && d.Site.SourceIdentity
                    == (punt ? "player.c:set_punt_ball_vels" : "player.c:set_field_goal_ball_vels")).ToArray();
            int[] tape = draws.Select(d => d.Raw.Value).Concat(Enumerable.Repeat(0, 16)).Take(16).ToArray();
            ActorState actor = m.Players[launch.ActorPlayerId];
            TeamId side = actor.Team;
            float[] original = punt
                ? PuntReference(side, m.SourceRules.LineOfScrimmage, meter.PuntStrength, actor.Flipped,
                    actor.Flipped ? leftAnkle : rightAnkle, camera, tape)
                : GoalReference(side, m.SourceRules.LineOfScrimmage, m.SourceHikeCounterAtSnap,
                    meter.AccuracyPercent, ball, target, actor.SourceHandicap, m.Quarter, m.SourceClock.PackedTime,
                    side == TeamId.Home ? m.HomeScore : m.AwayScore, side == TeamId.Home ? m.AwayScore : m.HomeScore,
                    m.SourceTeamFire[(int)side], m.SourcePerfectFieldGoal, m.SourceNoAssist, m.SourceDronesLose, true, tape);
            CompareBall(launch.Position, launch.Velocity, launch.Target, launch.TotalSourceTicks, original,
                "new-public-S2-launch-original-C");
            Check(draws.Length == (int)original[15], "original-C launch consumes exactly the actual production draw count");
            for (int i = 0; i < draws.Length; i++)
                Check(draws[i].Site.BoundOrModulus == (int)original[16 + i],
                    "original-C launch consumes actual random bounds in exact order; no padding consumed");
        }
    }

    private static void SpecialTrace(string name, GameSimulation game)
    {
        SimulationState m = game.Model;
        publicSpecialTrace.Add(new {
            name, game.SourceTick, hostTick = game.State.Tick, phase = m.Phase.ToString(),
            offense = (int)m.Possession, type = (int)m.SourceRules.PlayType, flags = (int)m.SourceFlags,
            play = m.SourceOffensePlay.Identity, carrier = m.CarrierId,
            ballKind = m.BallKind.ToString(), ball = m.SourceBallPosition,
            meter = ((ISourceKickMeterState)game.State).KickMeter,
            input = m.SourceCurrentInput.StationInputs,
            actors = m.Players.Select(p => new { p.Id, mode = p.SourceMode.ToString(),
                station = p.SourceControllerStation, script = p.AnimationState.Primary.ScriptSymbol,
                sequence = p.AnimationState.Primary.SequenceName,
                frame = p.AnimationState.Primary.FrameOrdinal, modes = p.AnimationState.Primary.Mode,
                position = SourcePlayerDriver.WorldPosition(p) }).ToArray(),
            events = game.SourceEvents.Select(e => new { e.Type, e.SourceTick, e.ActorPlayerId, e.TargetPlayerId }).ToArray(),
            rng = ((SourceInjectedRandom)game.SourceRandomService).Trace.ToArray()
        });
    }

    private static void PublicFakeContinuation(FullKernelInputs inputs, TeamId side, bool punt)
    {
        var configuration = new SourceMatchConfiguration(SourceSimulationProfile.OriginalRelease57Hz,
            SourceStationLayout.FourStation,
            new SourceTeamSelection(0, new[] { 0, 1, 2, 3, 4, 5, 6 }),
            new SourceTeamSelection(1, new[] { 0, 1, 2, 3, 4, 5, 6 }),
            new[] { new SourceStationConfiguration(0, TeamId.Home),
                new SourceStationConfiguration(2, TeamId.Away) },
            side, SourceCaptainSlot.First);
        int station = side == TeamId.Home ? 0 : 2;
        GameSimulation game = Factory(inputs, 2, configuration, metrics, publicSeed: true);
        PrepareInitialMatchBoundary(game, side, punt, true);
        var cabinet = new Cabinet(game, configuration, station);
        new PublicKickOriginal(game, cabinet);
        string name = "S2/HUMAN/" + (punt ? "FAKE-PUNT/" : "FAKE-FG/") + side;
        cabinet.Select(punt ? "play_fake_punt" : "play_fake_field_goal", side, true);
        SpecialTrace(name + "/selected", game);
        Check(game.Model.SourceRules.PlayType == SourcePlayType.Normal, name + ": original selected fake stays PT_NORMAL");
        for (int tick = 0; tick < 12; tick++) cabinet.Wake();
        cabinet.Pulse(SourceButtons.A);
        int qb = (int)side * 7 + 3;
        for (int tick = 0; tick < 100 && (game.Model.Phase != GamePhase.LiveBall || game.Model.CarrierId != qb); tick++)
            cabinet.Wake();
        SpecialTrace(name + "/hike", game);
        Check(game.Model.Phase == GamePhase.LiveBall && game.Model.CarrierId == qb,
            name + ": raw A reaches actual center VM hike and QB catch");
        Check((game.Model.SourceFlags & (SourceGameFlags.BallKicked | SourceGameFlags.BallPunted)) == 0,
            name + ": fake does not launch a real field goal or punt");
        Check(((ISourceKickMeterState)game.State).KickMeter.Status == SourceKickMeterStatus.Complete,
            name + ": source fake meter retires at real hike");
        bool released = false;
        for (int tick = 0; tick < 80 && !released; tick++)
        {
            cabinet.Wake(tick == 0 ? SourceButtons.B : SourceButtons.None, 0, tick == 0 ? 3 : 0);
            SpecialTrace(name + "/throw-" + tick, game);
            released = game.SourceEvents.Any(e => e.Type == SourceEventType.PassReleased
                || e.Type == SourceEventType.LateralReleased);
        }
        Check(released && game.Model.ThrowerId == qb,
            name + ": actual human B continues fake through throw animation release");
        Check(game.Model.BallKind == SourceBallKind.Pass || game.Model.BallKind == SourceBallKind.Lateral,
            name + ": fake ball is a source pass/lateral, not a kick");
        observations.Add(new { name, actualRawFakeContinuation = true, game.SourceTick,
            game.Model.ThrowerId, game.Model.TargetPlayerId, ballKind = game.Model.BallKind.ToString(),
            trace = publicSpecialTrace.ToArray() });
        publicSpecialTrace.Clear();
    }

    private static void PublicSpecialTeamsChecks(FullKernelInputs inputs)
    {
        foreach (TeamId side in new[] { TeamId.Home, TeamId.Away })
            foreach (bool punt in new[] { true, false })
                PublicFakeContinuation(inputs, side, punt);
    }

    private static void PublicFieldGoalBlock(FullKernelInputs inputs, TeamId side, bool continueReturn = false,
        bool finishReturn = false)
    {
        var configuration = new SourceMatchConfiguration(SourceSimulationProfile.OriginalRelease57Hz,
            SourceStationLayout.FourStation,
            new SourceTeamSelection(0, new[] { 0, 1, 2, 3, 4, 5, 6 }),
            new SourceTeamSelection(1, new[] { 0, 1, 2, 3, 4, 5, 6 }),
            new[] { new SourceStationConfiguration(0, TeamId.Home),
                new SourceStationConfiguration(2, TeamId.Away) },
            side, SourceCaptainSlot.First);
        int station = side == TeamId.Home ? 0 : 2;
        GameSimulation game = Factory(inputs, 2, configuration, metrics, publicSeed: true);
        PrepareInitialMatchBoundary(game, side, false, true);
        var cabinet = new Cabinet(game, configuration, station);
        new PublicKickOriginal(game, cabinet);
        string name = "S2/LIVE/RAW-FG-BLOCK" + (finishReturn ? "-RETURN-RESULT/" : continueReturn ? "-CONTINUATION/" : "/") + side;
        cabinet.Select("play_field_goal", side, true);
        SimulationState m = game.Model;
        ActorState kicker = m.Players[(int)side * 7 + 6];
        ActorState defender = m.Players.First(p => p.Team != side && p.SourceControllerStation >= 0);
        SourceVector3 start = SourcePlayerDriver.WorldPosition(kicker);
        SourcePlayerDriver.SetSourcePosition(defender, new SourceVector3(start.X + (side == TeamId.Home ? 6f : -6f), 0f, start.Z));
        foreach (ActorState actor in m.Players) actor.SourceRawFlags |= (int)SourceMovementFlags.NoCollision;
        for (int tick = 0; tick < 13; tick++) cabinet.Wake();
        SpecialTrace(name + "/pre-snap", game);
        cabinet.Pulse(SourceButtons.A);
        bool kicked = false, blocked = false, placedAtActualHolder = false;
        for (int tick = 0; tick < 120 && !blocked && m.Phase != GamePhase.DeadBall; tick++)
        {
            cabinet.OtherHeld = kicked ? SourceButtons.A : SourceButtons.None;
            cabinet.Wake();
            SpecialTrace(name + "/live-" + tick, game);
            if (!placedAtActualHolder && m.Phase == GamePhase.LiveBall
                && m.CarrierId == (int)side * 7 + 3 && m.BallKind == SourceBallKind.Lateral)
            {
                SourcePlayerDriver.SetSourcePosition(defender, new SourceVector3(
                    m.SourceBallPosition.X, 0f, m.SourceBallPosition.Z));
                defender.SourceVelocity = default(FieldVector);
                placedAtActualHolder = true;
                SpecialTrace(name + "/declared-live-block-position-at-real-holder", game);
            }
            kicked |= (m.SourceFlags & SourceGameFlags.BallKicked) != 0;
            blocked |= game.SourceEvents.Any(e => e.Type == SourceEventType.PassDeflected && e.ActorPlayerId == defender.Id);
        }
        Check(kicked, name + ": actual raw selected FG reaches original kicker release");
        Check(blocked && (m.SourceBallFlags & SourceBallFlags.Deflected) != 0,
            name + ": actual defender first-touch blocks live field-goal ball");
        Check(m.SourceDeflections[(int)defender.Team] == 1 && m.LastTouchPlayerId == defender.Id,
            name + ": original active deflect producer updates actual defender state");
        long blockTick = game.SourceTick;
        int recovered = -1;
        long recoveryTick = -1;
        SourceVector3 recoveryPosition = default(SourceVector3);
        if (continueReturn)
        {
            cabinet.OtherHeld = SourceButtons.None;
            for (int tick = 0; tick < 240 && !(m.SourceLastPlayEndCause.HasValue && !game.C2State.ResultPending); tick++)
            {
                if (m.CarrierId >= 0)
                {
                    if (recovered < 0)
                    {
                        recovered = m.CarrierId; recoveryTick = game.SourceTick;
                        recoveryPosition = SourcePlayerDriver.WorldPosition(m.Players[recovered]);
                    }
                    cabinet.OtherX = side == TeamId.Home ? -3 : 3;
                }
                cabinet.Wake();
                SpecialTrace(name + "/real-post-block-wake-" + tick, game);
                if (recovered >= 0 && game.SourceTick - recoveryTick >= 24) break;
            }
            Check(recovered >= 0 || m.SourceLastPlayEndCause.HasValue,
                name + ": actual blocked-ball window reaches a real recovery or source result");
            if (recovered >= 0)
                Check(m.Players[recovered].Team != side && m.Players[recovered].SourceControllerStation >= 0
                    && m.SourceLastPossession == (int)m.Possession && game.SourceTick - m.CatchSourceTick >= 18
                    && SourcePlayerDriver.WorldPosition(m.Players[recovered]).X != recoveryPosition.X,
                    name + ": real defending recovery reaches18-control credit and raw human return");
            if (finishReturn) FinishPublicKickReturn(game, configuration, cabinet, name);
        }
        observations.Add(new { name, actualRawKickAndBallBlock = true, game.SourceTick,
            blockTick, recovered, recoveryTick,
            finalResult = m.SourceLastPlayEndCause.HasValue ? m.SourceLastResult.ToString() : null,
            finalCause = m.SourceLastPlayEndCause.HasValue ? m.SourceLastPlayEndCause.ToString() : null,
            kicker = kicker.Id, defender = defender.Id, m.SourceBallPosition,
            velocity = new SourceVector3(m.SourceBallVelocity.X, m.SourceBallVerticalVelocity, m.SourceBallVelocity.Z),
            trace = publicSpecialTrace.ToArray() });
        publicSpecialTrace.Clear();
    }

    private static void PublicKickOutcome(FullKernelInputs inputs, TeamId side, string outcome, bool finishReturn = false)
    {
        var configuration = new SourceMatchConfiguration(SourceSimulationProfile.OriginalRelease57Hz,
            SourceStationLayout.FourStation,
            new SourceTeamSelection(0, new[] { 0, 1, 2, 3, 4, 5, 6 }),
            new SourceTeamSelection(1, new[] { 0, 1, 2, 3, 4, 5, 6 }),
            new[] { new SourceStationConfiguration(0, TeamId.Home),
                new SourceStationConfiguration(2, TeamId.Away) },
            side, SourceCaptainSlot.First);
        GameSimulation game = Factory(inputs, 2, configuration, metrics, publicSeed: true);
        bool punt = !outcome.StartsWith("FG-", StringComparison.Ordinal);
        bool good = outcome == "FG-post-goal60";
        bool inwardNegative = outcome == "punt-OOB" && side == TeamId.Away;
        PrepareInitialMatchBoundary(game, side, punt, true);
        SimulationState m = game.Model;
        int los = !punt ? 60 : outcome == "punt-endzone" ? 49 : 30;
        m.SourceRules = new SourceRulesState((int)side, (int)side, 4, los, 100);
        m.LineOfScrimmage = SourceMatchRules.AbsoluteYardsFromSourceWorld(SourceMatchRules.WorldPosition(los, (int)side));
        m.SourceLinePosition = outcome == "punt-OOB" ? side == TeamId.Home ? 20f : -20f : 0f;
        var cabinet = new Cabinet(game, configuration, side == TeamId.Home ? 0 : 2);
        new PublicKickOriginal(game, cabinet);
        string name = "S2/LIVE/RAW-" + (finishReturn ? "PUNT-RETURN-RESULT"
            : inwardNegative ? "punt-INWARD-IB-negative" : outcome) + "/" + side;
        cabinet.Select(punt ? "play_punt" : "play_field_goal", side, true);
        foreach (ActorState actor in m.Players) actor.SourceRawFlags |= (int)SourceMovementFlags.NoCollision;
        for (int tick = 0; tick < (outcome == "FG-miss40" ? 11 : 13); tick++) cabinet.Wake();
        if (outcome == "punt-endzone")
        {
            bool centered = false;
            for (int tick = 0; tick < 80 && !centered; tick++)
            {
                SourceKickMeterView meter = ((ISourceKickMeterState)game.State).KickMeter;
                centered = meter.Status == SourceKickMeterStatus.Tracking
                    && Math.Abs(meter.PositionFixed10000 + meter.VelocityFixed10000)
                        <= Math.Abs(meter.VelocityFixed10000) / 2;
                if (!centered) cabinet.Wake();
            }
            Check(centered, name + ": raw human waits for the actual source power indicator center");
        }
        cabinet.Pulse(SourceButtons.A);
        for (int tick = 0; tick < 160 && (m.SourceFlags & (punt ? SourceGameFlags.BallPunted : SourceGameFlags.BallKicked)) == 0; tick++)
        {
            cabinet.Wake();
            SpecialTrace(name + "/raw-launch-wake-" + tick, game);
        }
        Check((m.SourceFlags & (punt ? SourceGameFlags.BallPunted : SourceGameFlags.BallKicked)) != 0,
            name + ": original raw menu/A snap reaches actual VM kick release");
        SourceVector3 target = m.SourceBallTarget;
        int receiver = outcome == "punt-down" ? (int)side * 7 + 3
            : outcome == "punt-return" ? m.Players.First(p => p.Team != side && p.SourceControllerStation >= 0).Id : -1;
        foreach (ActorState actor in m.Players)
            if (actor.Id != receiver || outcome == "punt-down") actor.SourceMode = SourceActorMode.Dead;
        if (receiver >= 0 && outcome != "punt-down")
        {
            SourcePlayerDriver.SetSourcePosition(m.Players[receiver], new SourceVector3(target.X, 0f, target.Z));
            m.Players[receiver].SourceVelocity = default(FieldVector);
            SpecialTrace(name + "/declared-coverage-at-real-kick-target", game);
        }
        int caught = -1;
        long caughtTick = -1;
        SourceVector3 caughtAt = default(SourceVector3);
        bool lateCoveragePlaced = false;
        bool groundedLoose = false;
        for (int tick = 0; tick < 400 && !(m.SourceLastPlayEndCause.HasValue && !game.C2State.ResultPending); tick++)
        {
            if (outcome == "punt-down" && !lateCoveragePlaced && m.SourceBallVerticalVelocity < 0f
                && m.SourceBallPosition.Y < 14f)
            {
                ActorState cover = m.Players[receiver];
                cover.SourceMode = SourceActorMode.DefensiveBack;
                SourcePlayerDriver.SetSourcePosition(cover,
                    new SourceVector3(m.SourceBallPosition.X, 0f, m.SourceBallPosition.Z));
                cover.SourceVelocity = default(FieldVector);
                lateCoveragePlaced = true;
                SpecialTrace(name + "/declared-late-coverage-not-ball-or-result", game);
            }
            if (m.CarrierId >= 0)
            {
                if (caught < 0)
                {
                    caught = m.CarrierId; caughtTick = game.SourceTick;
                    caughtAt = SourcePlayerDriver.WorldPosition(m.Players[caught]);
                }
                cabinet.OtherX = side == TeamId.Home ? -3 : 3;
            }
            cabinet.Wake();
            SpecialTrace(name + "/actual-flight-contact-result-" + tick, game);
            if ((outcome == "punt-endzone" || inwardNegative) && m.SourceBallBounces > 0 && m.SourceBallVelocity.X == 0f
                && m.SourceBallVelocity.Z == 0f && m.SourceBallVerticalVelocity == 0f)
            {
                groundedLoose = true;
                break;
            }
            if (caught >= 0 && game.SourceTick - caughtTick >= 24) break;
        }
        if (outcome == "punt-endzone")
        {
            Check(groundedLoose && m.Phase == GamePhase.LiveBall && m.CarrierId == -1
                && !m.SourceLastPlayEndCause.HasValue,
                name + ": original grounded punt stays live even beyond the goal line, without automatic touchback");
            ActorState cover = m.Players[(int)side * 7 + 3];
            cover.SourceMode = SourceActorMode.DefensiveBack;
            SourcePlayerDriver.SetSourcePosition(cover, new SourceVector3(m.SourceBallPosition.X, 0f, m.SourceBallPosition.Z));
            cover.SourceVelocity = default(FieldVector);
            for (int tick = 0; tick < 40 && !(m.SourceLastPlayEndCause.HasValue && !game.C2State.ResultPending); tick++)
            {
                cabinet.Wake();
                SpecialTrace(name + "/declared-cover-downs-real-grounded-punt-" + tick, game);
            }
        }
        if (inwardNegative)
            Check(groundedLoose && m.Phase == GamePhase.LiveBall && m.CarrierId == -1 && m.Possession == side
                && !m.SourceLastPlayEndCause.HasValue && SourceBallDriver.InBounds(m.SourceBallPosition)
                && (m.SourceFlags & SourceGameFlags.PlayEnding) == 0,
                name + ": original random punt Z is not mirrored, inward grounded ball remains live");
        else if (outcome == "punt-return")
        {
            Check(caught == receiver && m.Possession != side,
                name + ": real selected punt is caught by actual opposing human actor");
            Check(game.SourceTick - m.CatchSourceTick >= 18 && m.SourceLastPossession == (int)m.Possession,
                name + ": original18-wake return-control credit is reached publicly");
            Check(m.Players[caught].SourceControllerStation >= 0
                && SourcePlayerDriver.WorldPosition(m.Players[caught]).X != caughtAt.X,
                name + ": raw return input is consumed by actual controlled carrier");
            if (finishReturn) FinishPublicKickReturn(game, configuration, cabinet, name);
        }
        else
        {
            SourcePlayResult expected = good ? SourcePlayResult.FieldGoal
                : outcome == "FG-miss40" ? SourcePlayResult.MissedKick
                : outcome == "punt-endzone" ? SourcePlayResult.DownedPunt
                : outcome == "punt-OOB" ? SourcePlayResult.PuntOutOfBounds : SourcePlayResult.DownedPunt;
            Check(m.SourceLastPlayEndCause.HasValue && !game.C2State.ResultPending && m.SourceLastResult == expected,
                name + ": actual ball/contact queue is committed by real game_proc as original " + expected);
            Check(good ? m.HomeScore == 3 && m.AwayScore == 0
                : m.Possession != side && m.HomeScore == 0 && m.AwayScore == 0,
                name + ": original kick transition preserves exact scoring and possession");
            if (!punt && !good) Check(m.SourceRules.LineOfScrimmage == 40, name + ": real missed FG at LOS60 yields opponent40");
            if (outcome == "punt-endzone") Check(m.SourceRules.LineOfScrimmage == (side == TeamId.Home ? 20 : 2),
                name + ": original actual downed end-zone20 versus field98/opponent2 placement");
            if (good)
            {
                for (int tick = 0; tick < 64 && m.SourceRules.PlayType == SourcePlayType.Kickoff
                    && m.Phase == GamePhase.DeadBall; tick++)
                {
                    cabinet.QueuedCommand = new SourceCommand(SourceAction.Continue);
                    cabinet.Wake();
                    SpecialTrace(name + "/real-post-score-continue-" + tick, game);
                }
                Check(m.SourceRules.PlayType == SourcePlayType.Kickoff && m.SourceKickoffKind == SourceKickoffKind.Quick
                    && m.SourceRules.LineOfScrimmage == 40 && m.Possession == TeamId.Away,
                    name + ": real scored field goal reaches actual next quick-kick40 restart");
                for (int tick = 0; tick < 3 && m.Phase == GamePhase.PreSnap; tick++) cabinet.Wake();
                Check(m.Phase == GamePhase.LiveBall && m.ClockActive
                    && (m.SourceBallFlags & SourceBallFlags.Freeze) == 0,
                    name + ": real receiving actor releases post-score quick ball and clock");
            }
        }
        observations.Add(new { name, actualRawKickResult = true, los,
            declaredCoverageBoundaryAfterActualKick = true, receiver, caught, caughtTick,
            game.SourceTick, possession = (int)m.Possession, finalLos = m.SourceRules.LineOfScrimmage,
            result = m.SourceLastPlayEndCause.HasValue ? m.SourceLastResult.ToString() : null,
            cause = m.SourceLastPlayEndCause.HasValue ? m.SourceLastPlayEndCause.ToString() : null,
            m.SourceBallPosition, trace = publicSpecialTrace.ToArray() });
        publicSpecialTrace.Clear();
    }

    private static void FinishPublicKickReturn(GameSimulation game, SourceMatchConfiguration configuration,
        Cabinet cabinet, string name)
    {
        SimulationState m = game.Model;
        int carrier = m.CarrierId;
        bool wall = false;
        for (int tick = 0; tick < 200 && !wall && m.Phase == GamePhase.LiveBall; tick++)
        {
            cabinet.Wake();
            wall = Math.Abs(SourcePlayerDriver.WorldPosition(m.Players[carrier]).Z) >= 182.85f - 4f;
            SpecialTrace(name + "/original-default-running-wall-" + tick, game);
        }
        Check(wall && m.Phase == GamePhase.LiveBall && !m.SourceLastPlayEndCause.HasValue
            && SourceBallDriver.InBounds(SourcePlayerDriver.WorldPosition(m.Players[carrier])),
            name + ": original player_proc FIELDW-minus4 wall blocks ordinary running, not held-ball drawing");
        cabinet.OtherHeld = SourceButtons.A;
        cabinet.Wake();
        long hurdleTick = game.SourceTick;
        SpecialTrace(name + "/actual-fresh-human-A-at-source-wall", game);
        cabinet.OtherHeld = SourceButtons.None;
        Check(((SourceInjectedRandom)game.SourceRandomService).Trace.Any(d => d.Site.SourceTick == hurdleTick
            && d.Site.ActorId == carrier && d.Site.SourceIdentity == "player.c:_mode_wpn:hurdle"),
            name + ": actual raw A invokes original hurdle producer, never an injected UNINT mode");
        int resolved = 0;
        for (int tick = 0; tick < 120 && !(m.SourceLastPlayEndCause.HasValue && !game.C2State.ResultPending); tick++)
        {
            cabinet.Wake();
            resolved += game.SourceEvents.Count(e => e.Type == SourceEventType.PlayResolved);
            SpecialTrace(name + "/continuous-raw-return-to-result-" + tick, game);
        }
        Check(m.SourceLastPlayEndCause == SourcePlayEndCause.CarrierOutOfBounds && !game.C2State.ResultPending
            && m.SourceLastResult == SourcePlayResult.NewPossession && resolved == 1,
            name + ": continuous raw return actually reaches original carrier-OOB and one committed NewPossession");
        Check(m.SourceRules.PlayType == SourcePlayType.Normal && m.HomeScore == 0 && m.AwayScore == 0,
            name + ": real return result keeps source normal dispatch and does not invent kick points");
        cabinet = new Cabinet(game, configuration, (int)m.Possession * 2);
        for (int tick = 0; tick < 64 && m.Phase == GamePhase.DeadBall; tick++)
        {
            cabinet.QueuedCommand = new SourceCommand(SourceAction.Continue);
            cabinet.Wake();
            SpecialTrace(name + "/actual-return-result-continue-" + tick, game);
        }
        Check(m.Phase == GamePhase.PlaySelect && m.SourceRules.PlayType == SourcePlayType.Normal,
            name + ": actual receiving-side Continue starts the next normal play-selection producer");
    }

    private static void PublicKickResultChecks(FullKernelInputs inputs)
    {
        var failures = new List<string>();
        foreach (TeamId side in new[] { TeamId.Home, TeamId.Away })
        {
            PublicKickResultCase(() => PublicFieldGoalBlock(inputs, side, true),
                "FG-block-continuation/" + side, failures);
            foreach (string outcome in new[] { "FG-miss40", "punt-endzone", "punt-OOB", "punt-down", "punt-return" })
                PublicKickResultCase(() => PublicKickOutcome(inputs, side, outcome), outcome + "/" + side, failures);
            if (side == TeamId.Home)
                PublicKickResultCase(() => PublicKickOutcome(inputs, side, "FG-post-goal60"),
                    "FG-post-goal60/" + side, failures);
        }
        Check(failures.Count == 0, "finite independent kick results: " + string.Join(" | ", failures));
    }

    private static void PublicKickResultCase(Action run, string name, List<string> failures)
    {
        try { run(); }
        catch (Exception error)
        {
            failures.Add(name + ": " + error.Message);
            observations.Add(new { name, failed = true, error = error.ToString(),
                trace = publicSpecialTrace.ToArray() });
            publicSpecialTrace.Clear();
        }
    }

    private static void PublicSafetyRestart(FullKernelInputs inputs, TeamId side, bool onside)
    {
        var configuration = new SourceMatchConfiguration(SourceSimulationProfile.OriginalRelease57Hz,
            SourceStationLayout.FourStation,
            new SourceTeamSelection(0, new[] { 0, 1, 2, 3, 4, 5, 6 }),
            new SourceTeamSelection(1, new[] { 0, 1, 2, 3, 4, 5, 6 }),
            new[] { new SourceStationConfiguration(0, TeamId.Home), new SourceStationConfiguration(1, TeamId.Home),
                new SourceStationConfiguration(2, TeamId.Away), new SourceStationConfiguration(3, TeamId.Away) },
            TeamId.Home, SourceCaptainSlot.First);
        GameSimulation game = Factory(inputs, 2, configuration, metrics, publicSeed: true);
        var cabinet = new Cabinet(game, configuration, (int)side * 2);
        for (int host = 0; host < 60; host++) cabinet.Host();
        string name = "S2/SAFETY-TO-" + (onside ? "RAW-ONSIDE30/" : "QUICK40/") + side;
        SimulationState m = game.Model;
        int offense = 1 - (int)side, catcher = (int)side * 7 + 4;
        float sign = side == TeamId.Home ? 1f : -1f;
        // Reuse Quality EndZone's declared loose-ball boundary, not a result setter.
        m.Phase = GamePhase.LiveBall; m.SourceMatchPhase = SourceMatchPhase.LiveBall; m.ClockActive = true;
        m.Possession = (TeamId)offense; m.SourceLastPossession = offense;
        m.SourceRules = new SourceRulesState(offense, offense, 2, 30, 80);
        m.SourceOffensePlay = inputs.Playbook.GetPlay("play_spider");
        m.SourceDefensePlay = inputs.Playbook.GetPlay("play_safe_cover");
        m.SourceFlags = SourceGameFlags.BallCrossedLine | SourceGameFlags.OverLineSpeedsSet | SourceGameFlags.Fumble;
        m.SourceBallFlags = SourceBallFlags.None;
        m.CarrierId = -1; m.BallMode = BallMode.InFlight; m.BallKind = SourceBallKind.Fumble;
        m.PassTicksElapsed = 30; m.PassTicksTotal = 60; m.TargetPlayerId = 4;
        m.ThrowerId = offense * 7 + 3; m.SourceKickApproach = false;
        foreach (ActorState actor in m.Players)
        {
            actor.SourceMode = SourceActorMode.Dead; actor.SourceRawFlags = (int)SourceMovementFlags.NoCollision;
            actor.SourceControllerStation = -1; actor.NoCatchTicks = 0;
            actor.SourceVelocity = default(FieldVector); actor.SourceVerticalVelocity = 0f;
            actor.SourceJob = SourceActorJob.None; actor.SourceTarget = -1;
            SourcePlayerDriver.SetSourcePosition(actor, new SourceVector3(actor.Id * 25f - 150f, 0f, 130f));
        }
        for (int station = 0; station < 4; station++)
        {
            int actor = station / 2 * 7 + (station % 2 == 0 ? 3 : 5);
            m.SourceStationPlayers[station] = actor; m.Players[actor].SourceControllerStation = station;
        }
        SourceBallDriver.SetPosition(m, new SourceVector3(-400f * sign, 10f, 0f));
        SourceBallDriver.SetTarget(m, -400f * sign, 0f);
        m.SourceBallVelocity = default(FieldVector); m.SourceBallVerticalVelocity = 0f;
        cabinet.Wake();
        Check(m.CarrierId == -1, name + ": real camera/ball primer runs with catcher dead");
        ActorState carrier = m.Players[catcher];
        SourcePlayerDriver.SetSourcePosition(carrier, new SourceVector3(-400f * sign, 0f, 0f));
        carrier.SourceMode = SourceActorMode.DefensiveBack; carrier.Role = ActorRole.Defender;
        cabinet.Wake();
        SpecialTrace(name + "/real-ownEZ-recovery", game);
        Check(m.CarrierId == catcher && m.Possession == side
            && (m.SourceFlags & SourceGameFlags.RecoveredInEndZone) != 0,
            name + ": actual loose-ball recovery creates own-EZ possession");
        foreach (ActorState other in m.Players.Where(p => p.Id != catcher)) other.SourceMode = SourceActorMode.Dead;
        foreach (float x in new[] { -370.5f, -362.7f })
        {
            SourcePlayerDriver.SetSourcePosition(carrier, new SourceVector3(x * sign, 0f, 0f));
            carrier.SourceVelocity = default(FieldVector);
            cabinet.Wake();
        }
        Check((m.SourceFlags & SourceGameFlags.LeftEndZone) != 0, name + ": real FIELDX3 marks leaving the end zone");
        while (game.SourceTick - m.CatchSourceTick < 18) cabinet.Wake();
        Check(m.SourceLastPossession == (int)side, name + ": actual18-control wake credits recovered side");
        SourcePlayerDriver.SetSourcePosition(carrier, new SourceVector3(-400f * sign, 0f, 183.5f));
        carrier.SourceVelocity = default(FieldVector);
        cabinet.Wake();
        SpecialTrace(name + "/actual-carrier-OOB", game);
        Check(game.C2State.ResultPending
            && m.SourceC2PendingResult.Input.Cause == SourcePlayEndCause.CarrierDownInOwnEndZone,
            name + ": actual carrier producer queues original own-EZ cause");
        for (int tick = 0; tick < 40 && game.C2State.ResultPending; tick++) cabinet.Wake();
        Check(m.SourceLastResult == SourcePlayResult.Safety && m.SourceRules.PlayType == SourcePlayType.Kickoff
            && (side == TeamId.Home ? m.AwayScore == 2 && m.HomeScore == 0 : m.HomeScore == 2 && m.AwayScore == 0),
            name + ": next real game_proc commits safety and exact opposing2 points");
        cabinet = new Cabinet(game, configuration, (int)side * 2 + (int)m.SourceCaptain);
        SourceButtons chord = SourceButtons.A | SourceButtons.B | SourceButtons.C;
        for (int tick = 0; tick < 64 && m.Phase == GamePhase.DeadBall; tick++)
        {
            cabinet.QueuedCommand = new SourceCommand(SourceAction.Continue);
            cabinet.Wake(onside ? chord : SourceButtons.None, 0, onside ? -3 : 0);
            SpecialTrace(name + "/actual-safety-result-continue-" + tick, game);
        }
        Check(m.SourceKickingSide == (int)side
            && m.SourceKickoffKind == (onside ? SourceKickoffKind.Onside : SourceKickoffKind.Quick)
            && m.SourceRules.LineOfScrimmage == (onside ? 30 : 40),
            name + ": original safety restart is raw onside30 or quick40, never invented universal20");
        for (int tick = 0; tick < 200 && m.Phase == GamePhase.PreSnap; tick++) cabinet.Wake();
        Check(m.Phase == GamePhase.LiveBall && m.BallKind == SourceBallKind.Kickoff,
            name + ": actual selected restart actor releases real ball");
        observations.Add(new { name, reusedDeclaredEndZoneGeometry = true, actualSafetyRestart = true,
            game.SourceTick, m.HomeScore, m.AwayScore, kind = m.SourceKickoffKind.ToString(),
            finalLos = m.SourceRules.LineOfScrimmage, trace = publicSpecialTrace.ToArray() });
        publicSpecialTrace.Clear();
    }

    private static void PublicPeriodRestartInput(FullKernelInputs inputs, TeamId first, int quarter)
    {
        var configuration = new SourceMatchConfiguration(SourceSimulationProfile.OriginalRelease57Hz,
            SourceStationLayout.FourStation,
            new SourceTeamSelection(0, new[] { 0, 1, 2, 3, 4, 5, 6 }),
            new SourceTeamSelection(1, new[] { 0, 1, 2, 3, 4, 5, 6 }),
            new[] { new SourceStationConfiguration(0, TeamId.Home), new SourceStationConfiguration(2, TeamId.Away) },
            first, SourceCaptainSlot.First);
        GameSimulation game = Factory(inputs, 2, configuration, metrics, publicSeed: true);
        var cabinet = new Cabinet(game, configuration, (int)first * 2);
        SourceButtons chord = SourceButtons.A | SourceButtons.B | SourceButtons.C;
        cabinet.OtherHeld = chord; cabinet.OtherY = -3;
        for (int host = 0; host < 60; host++) cabinet.Host(chord, 0, -3);
        SimulationState m = game.Model;
        string name = "S2/RAW-ABC-UP/" + (quarter == 2 ? "HALFTIME/" : "FIRST-OT/") + first;
        Check(game.SourceTick == 57 && m.SourceKickoffKind == SourceKickoffKind.Opening
            && m.SourceRules.LineOfScrimmage == 30 && (m.SourceFlags & SourceGameFlags.OnsideKick) == 0,
            name + ": actual opening kickoff rejects held raw onside chord");
        SpecialTrace(name + "/actual-opening-raw-chord-ignored", game);
        m.Phase = GamePhase.DeadBall; m.Quarter = quarter;
        m.Possession = first; m.SourceLastPossession = (int)first;
        m.SourceRules = new SourceRulesState((int)first, (int)first, 2, 30, 80);
        m.SourceFlags = SourceGameFlags.None;
        m.SourceClock = SourceClock.Create(1, firstWakeTick: game.SourceTick + 1);
        m.ClockActive = true;
        foreach (ActorState actor in m.Players) actor.SourceMode = SourceActorMode.Dead;
        cabinet.Wake(chord, 0, -3);
        cabinet.Wake(chord, 0, -3);
        Check(m.SourceClock.ProcessComplete && m.SourceClock.PackedTime == 0,
            name + ": declared last subtick actually expires and clock process retires before Continue");
        for (int tick = 0; tick < 64 && m.Quarter == quarter; tick++)
        {
            cabinet.QueuedCommand = new SourceCommand(SourceAction.Continue);
            cabinet.Wake(chord, 0, -3);
            SpecialTrace(name + "/actual-period-continue-" + tick, game);
        }
        bool half = quarter == 2;
        Check(m.Quarter == quarter + 1 && m.SourceRules.PlayType == SourcePlayType.Kickoff
            && m.SourceKickoffKind == (half ? SourceKickoffKind.Quick : SourceKickoffKind.Onside)
            && m.SourceRules.LineOfScrimmage == (half ? 40 : 30),
            name + ": actual halftime forbids onside but original OT allows held raw chord");
        if (half)
            Check(m.SourceKickingSide == 1 - (int)first && (m.SourceFlags & SourceGameFlags.OnsideKick) == 0,
                name + ": actual opposite-first-possession halftime kicker survives raw chord");
        else
            Check(((SourceInjectedRandom)game.SourceRandomService).Trace.Count(
                d => d.Site.SourceIdentity == "SourceMatchFlow.OvertimeKickoff") == 1,
                name + ": original first-OT coin consumes exactly one actual2-bound draw");
        cabinet.OtherHeld = SourceButtons.None; cabinet.OtherY = 0;
        for (int tick = 0; tick < 200 && m.Phase == GamePhase.PreSnap; tick++) cabinet.Wake();
        Check(m.Phase == GamePhase.LiveBall && m.BallKind == SourceBallKind.Kickoff,
            name + ": actual period restart actor releases selected source ball");
        observations.Add(new { name, declaredFinalSubtickAndFinishedPlay = true,
            actualRawPeriodInput = true, game.SourceTick, quarter = m.Quarter,
            kind = m.SourceKickoffKind.ToString(), kicker = m.SourceKickingSide,
            finalLos = m.SourceRules.LineOfScrimmage, trace = publicSpecialTrace.ToArray() });
        publicSpecialTrace.Clear();
    }

    private static void PublicOnsideInput(FullKernelInputs inputs, TeamId side, int vector)
    {
        var configuration = new SourceMatchConfiguration(SourceSimulationProfile.OriginalRelease57Hz,
            SourceStationLayout.FourStation,
            new SourceTeamSelection(0, new[] { 0, 1, 2, 3, 4, 5, 6 }),
            new SourceTeamSelection(1, new[] { 0, 1, 2, 3, 4, 5, 6 }),
            new[] { new SourceStationConfiguration(0, TeamId.Home),
                new SourceStationConfiguration(2, TeamId.Away) },
            side, SourceCaptainSlot.First);
        GameSimulation game = Factory(inputs, 2, configuration, metrics, publicSeed: true);
        var cabinet = new Cabinet(game, configuration, side == TeamId.Home ? 0 : 2);
        for (int host = 0; host < 60; host++) cabinet.Host();
        Check(game.SourceTick == 57, "onside input fixture has real60/57 startup and retained camera");
        SimulationState m = game.Model;
        m.Phase = GamePhase.DeadBall;
        m.Possession = side; m.SourceLastPossession = (int)side;
        m.SourceRules = new SourceRulesState((int)side, (int)side, 1, 30, 100, SourcePlayType.Kickoff);
        m.SourceHalfKickoff = false;
        string name = "S2/HUMAN/ONSIDE-CHOICE/" + side + "/" + vector;
        SourceButtons chord = SourceButtons.A | SourceButtons.B | SourceButtons.C;
        if (vector == 1) { cabinet.OtherHeld = chord; cabinet.OtherY = -3; }
        cabinet.QueuedCommand = new SourceCommand(SourceAction.Continue);
        cabinet.Wake(vector == 1 ? SourceButtons.None : vector == 3 ? SourceButtons.A | SourceButtons.B : chord,
            vector == 2 ? 3 : 0, vector == 1 ? 0 : -3);
        SpecialTrace(name + "/actual-Continue-with-raw-switches", game);
        SourceKickoffKind expected = vector == 0 ? SourceKickoffKind.Onside : SourceKickoffKind.Quick;
        Check(m.Phase == (vector == 1 ? GamePhase.LiveBall : GamePhase.PreSnap) && m.SourceKickoffKind == expected,
            name + ": actual game_proc uses original same-team ABC/exact-UP chord");
        Check(m.SourceRules.LineOfScrimmage == (vector == 0 ? 30 : 40)
            && m.Possession == (vector == 0 ? side : side == TeamId.Home ? TeamId.Away : TeamId.Home),
            name + ": original onside30 versus swapped quick40, not universal20");
        Check(((m.SourceFlags & SourceGameFlags.OnsideKick) != 0) == (vector == 0)
            && ((m.SourceFlags & SourceGameFlags.QuickKick) != 0) == (vector != 0),
            name + ": actual source flags preserve distinct restart branches");
        cabinet.OtherHeld = SourceButtons.None; cabinet.OtherY = 0;
        bool live = m.Phase == GamePhase.LiveBall;
        for (int tick = 0; tick < 200 && !live; tick++)
        {
            cabinet.Wake();
            SpecialTrace(name + "/actual-kick-wake-" + tick, game);
            live = m.Phase == GamePhase.LiveBall;
        }
        Check(live && m.BallKind == SourceBallKind.Kickoff,
            name + ": actual public source actor releases selected kickoff branch");
        if (vector == 0)
            Check((m.SourceFlags & SourceGameFlags.BallKicked) != 0,
                name + ": onside uses actual kicker release");
        else
            Check((m.SourceBallFlags & SourceBallFlags.Freeze) == 0 && m.ClockActive
                && (m.SourceFlags & SourceGameFlags.BallKicked) == 0,
                name + ": quick receiver releases native frozen ball and starts clock");
        observations.Add(new { name, declaredPostScoreKickoffBoundary = true,
            actualRawOnsideInput = true, game.SourceTick, kind = m.SourceKickoffKind.ToString(),
            flags = (int)m.SourceFlags, m.SourceBallPosition,
            trace = publicSpecialTrace.ToArray() });
        publicSpecialTrace.Clear();
    }
}
