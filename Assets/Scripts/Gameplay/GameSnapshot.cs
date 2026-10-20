using System;
using System.Collections.Generic;
using Blitz.Contracts;

namespace Blitz.Gameplay
{
    internal sealed class GameSnapshot : ISourcePresentationState, ISourceKickMeterState, ISourceCameraState, ISourcePossessionState
    {
        private readonly SourceKickMeterMetrics kickMeterMetrics;
        private readonly SourceKickMeterView kickMeter;
        private readonly SourceKickLaunchView kickLaunch;
        SourceKickMeterMode ISourceKickMeterState.KickMeterMode
        { get { return kickMeterMetrics == null ? SourceKickMeterMode.Disabled : SourceKickMeterMode.PortableAuthored; } }
        SourceKickMeterMetrics ISourceKickMeterState.KickMeterMetrics { get { return kickMeterMetrics; } }
        SourceKickMeterView ISourceKickMeterState.KickMeter { get { return kickMeter; } }
        SourceKickLaunchView ISourceKickMeterState.LastKickLaunch { get { return kickLaunch; } }
        public long Tick { get; }
        public long SourceTick { get; }
        public int SourceTicksPerSecond { get; }
        public uint PackedGameTime { get; }
        public bool ClockActive { get; }
        public int PlayNumber { get; }
        public GamePhase Phase { get; }
        public TeamId Possession { get; }
        public int OffenseDirection { get; }
        public int Quarter { get; }
        public int QuarterTicksRemaining { get; }
        public int PreSnapTicksRemaining { get; }
        public int Down { get; }
        public float LineOfScrimmage { get; }
        public float FirstDownLine { get; }
        public int HomeScore { get; }
        public int AwayScore { get; }
        public int ControlledPlayerId { get; }
        public PlayId HomePlay { get; }
        public PlayId AwayPlay { get; }
        public PlayOutcome LastOutcome { get; }
        public BallView Ball { get; }
        public IReadOnlyList<PlayerView> Players { get; }
        public SourceMatchView Match { get; }
        public SourcePresentationSettings Presentation { get; }
        public SourceCameraBoundary CameraBoundary { get; }
        public System.Collections.ObjectModel.ReadOnlyCollection<SourceProcessView> Processes { get; }
        public SourcePossessionMessageView PossessionMessage { get; }
        public System.Collections.ObjectModel.ReadOnlyCollection<SourcePossessionMessageView> PossessionMessages { get; }
        public long CameraOutputSourceTick { get; }
        public bool CameraProcessSleeping { get; }

        internal GameSnapshot(SimulationState model, int cameraProcessOrder = -1)
        {
            kickMeterMetrics = model.SourceKickMeterMetrics;
            kickMeter = model.SourceKickMeter == null ? default(SourceKickMeterView) : model.SourceKickMeter.View;
            kickLaunch = model.SourceKickLaunch;
            Tick = model.Tick;
            SourceTick = model.SourceMode ? model.SourceTick : model.Tick;
            SourceTicksPerSecond = model.SourceMode ? SourceTiming.DefaultTicksPerSecond : SliceRules.TicksPerSecond;
            PackedGameTime = model.SourceMode ? model.PackedGameTime : 0u;
            ClockActive = model.SourceMode ? model.ClockActive : model.Phase == GamePhase.LiveBall;
            PlayNumber = model.PlayNumber;
            Phase = model.Phase;
            Possession = model.Possession;
            OffenseDirection = model.OffenseDirection;
            Quarter = model.Quarter;
            QuarterTicksRemaining = model.QuarterTicksRemaining;
            PreSnapTicksRemaining = model.PreSnapTicksRemaining;
            Down = model.Down;
            LineOfScrimmage = model.LineOfScrimmage;
            FirstDownLine = model.FirstDownLine;
            HomeScore = model.HomeScore;
            AwayScore = model.AwayScore;
            ControlledPlayerId = model.ControlledPlayerId;
            HomePlay = model.HomePlay;
            AwayPlay = model.AwayPlay;
            LastOutcome = model.LastOutcome;
            Presentation = model.SourceMode ? model.SourcePresentation : null;
            BallMode ballMode = (model.SourceBallFlags & SourceBallFlags.Phantom) != 0 ? BallMode.InFlight : model.BallMode;
            Ball = new BallView(ballMode, model.BallPosition, model.BallHeight,
                ballMode == BallMode.Held ? model.CarrierId : -1,
                ballMode == BallMode.InFlight ? model.TargetPlayerId : -1,
                model.BallKind, model.BallVelocity, model.BallVerticalVelocity, model.LastTouchPlayerId,
                model.SourceMode ? BallControl(model) : default(SourceBallControlView),
                model.SourceMode ? new SourceBallPresentationView(unchecked((int)model.SourceBallFlags))
                    : default(SourceBallPresentationView));

            var players = new PlayerView[model.Players.Length];
            for (int id = 0; id < players.Length; id++)
            {
                ActorState actor = model.Players[id];
                players[id] = model.SourceMode ? SourcePlayerView(actor, model.SourceTick)
                    : new PlayerView(actor.Id, actor.Team, actor.Position, actor.Velocity, actor.Turbo01,
                        actor.Height, role: actor.Role, modelScale: actor.ModelScale);
            }
            Players = Array.AsReadOnly(players);
            if (model.SourceMode)
            {
                Processes = model.SourceProcesses.Processes;
                PossessionMessage = model.SourcePossessionMessage;
                PossessionMessages = model.SourcePossessionMessages;
                CameraOutputSourceTick = model.SourceMovementCameraTick;
                CameraProcessSleeping = model.SourceCameraPossessionContinuation;
                CameraBoundary = CaptureCamera(model, cameraProcessOrder);
                var scores = new SourceQuarterScore[model.Quarter];
                for (int q = 0; q < scores.Length; q++)
                    scores[q] = new SourceQuarterScore(q, model.SourceQuarterScores[q, 0], model.SourceQuarterScores[q, 1]);
                var stations = new SourceStationView[model.SourceConfiguration.Stations.Count];
                for (int i = 0; i < stations.Length; i++)
                {
                    int station = model.SourceConfiguration.Stations[i].StationId;
                    stations[i] = new SourceStationView(station, model.SourceStationPlayers[station],
                        model.SourceStationTurbo[station] / 100f);
                }
                Match = new SourceMatchView(model.SourceConfiguration, (SourcePlayKind)model.SourceRules.PlayType,
                    model.SourceMatchPhase, model.SourcePeriodView,
                    new SourcePossessionView(model.Possession, (TeamId)model.SourceRules.InitialOffenseSide,
                        (TeamId)model.SourceLastPossession, (TeamId)model.SourceFirstPossession,
                        model.SourceCaptain), scores, stations, model.SourceResultView,
                    model.SourceHomePlay, model.SourceAwayPlay, model.SourceSelectionCountdown,
                    model.SourcePatChoice, model.SourcePatStatus,
                    model.SourceRules.PlayType == SourcePlayType.Kickoff ? (SourceKickoffType?)model.SourceKickoffKind : null,
                    model.SourceRules.PlayType == SourcePlayType.Kickoff ? (TeamId?)model.SourceKickingSide : null,
                    model.SourceRules.PlayType == SourcePlayType.Kickoff ? (TeamId?)(1 - model.SourceKickingSide) : null,
                    model.SourceOffenseFlipped,
                    SourceTeamGameplay(model, TeamId.Home), SourceTeamGameplay(model, TeamId.Away),
                    model.SourceForwardCatcher, model.SourceDronesLose, model.SourceDronesWinning,
                    model.SourceHandicapRepetitionBonus, model.SourceCompatibility,
                    model.SourceCompatibilityRepairCount, model.SourceCompatibilityRepairs);
            }
        }

        internal static int RawCameraGameMode(SimulationState model)
        {
            int mode = RawMovementPhase(model);
            return mode == 5 && !model.SourceKickApproach ? 1 : mode;
        }

        internal static int RawMovementPhase(SimulationState model)
        {
            return model.Phase == GamePhase.LiveBall ? 2
                : model.Phase == GamePhase.DeadBall ? 3 : model.Phase == GamePhase.GameOver ? 4
                : model.Phase == GamePhase.PreSnap ? model.SourceRules.PlayType == SourcePlayType.Kickoff ? 5 : 1 : 0;
        }

        private static SourceCameraBoundary CaptureCamera(SimulationState model, int processOrder)
        {
            var actors = new SourceCameraActor[14];
            for (int id = 0; id < actors.Length; id++)
            {
                ActorState actor = model.Players[id];
                actors[id] = new SourceCameraActor(id, (int)actor.SourceMode, SourceActorFlags(actor), actor.Facing,
                    SourcePlayerDriver.WorldPosition(actor), model.SourceCameraGroundCenters[id]);
            }
            int stations = 0;
            foreach (SourceStationConfiguration station in model.SourceConfiguration.Stations)
                stations |= 1 << station.StationId;
            bool hh = model.SourceConfiguration.StationLayout == SourceStationLayout.FourStation &&
                ((stations & 3) == 3 || (stations & 12) == 12);
            return new SourceCameraBoundary(model.SourceTick, processOrder, RawCameraGameMode(model),
                (int)model.SourceFlags, (int)model.SourceRules.PlayType, (int)model.SourceLastResult, model.Quarter - 1,
                model.SourceRules.OffenseSide, model.SourceLastPossession, model.SourceRules.LineOfScrimmage,
                model.CarrierId, hh, model.SourcePresentation != null && model.SourcePresentation.PupPullback != 0,
                model.PreSnapTicksRemaining, model.SourceOffensePlay == null ? 0 : (int)model.SourceOffensePlay.Flags,
                new SourceCameraBall(model.SourceBallPosition,
                    new SourceVector3(model.SourceBallVelocity.X, model.SourceBallVerticalVelocity, model.SourceBallVelocity.Z),
                    model.SourceBallOrigin, model.SourceBallTarget, model.SourceCameraBallTarget,
                    (int)GameSimulation.SourceBallTypeFor(model.BallKind), unchecked((int)model.SourceBallFlags),
                    model.PassTicksElapsed, model.ThrowerId, model.TargetPlayerId), actors,
                model.SourceOffenseTurnover, model.SourceMatchPhase == SourceMatchPhase.MatchEnded ? 4
                    : model.SourceMatchPhase == SourceMatchPhase.PeriodTransition ? 3 : 2);
        }

        internal static PlayerView SourcePlayerView(ActorState actor, long tick, bool relativeToOrigin = false)
        {
            return new PlayerView(actor.Id, actor.Team, relativeToOrigin ? new FieldVector(50f, 0f) : actor.Position,
                actor.Velocity, actor.Turbo01, relativeToOrigin ? 0f : actor.Height,
                SourcePoseMath.Facing(actor.Facing), actor.Role, actor.AnimationState.View(actor, tick),
                actor.ModelScale, new SourceActorView(actor.SourceControllerStation, actor.SourceRoster, actor.NoCatchTicks,
                    new SourceActorPresentationView(SourceActorFlags(actor), (int)actor.SourceMode),
                    new SourceActorContactView(actor.SourceHitPartnerId, actor.SourcePuppetLinkId,
                        actor.SourcePuppetReleaseTick, actor.SourceAttackMode, actor.SourceAttackTime, actor.SourceBlockCount),
                    actor.SourceHandicap, actor.SourceRawHandicap));
        }

        private static SourceTeamGameplayView SourceTeamGameplay(SimulationState model, TeamId team)
        {
            int side = (int)team;
            return new SourceTeamGameplayView(team, model.SourceTeamFire[side], model.SourceLastReceiver[side],
                model.SourceConsecutiveCompletions[side], model.SourceConsecutiveLosses[side],
                model.SourceTeamHandicap[side], model.SourceEffectiveTeamHandicap[side]);
        }

        internal static int SourceActorFlags(ActorState actor)
        {
            const int tracked = 0x01 | 0x08 | 0x200 | 0x800 | 0x1000 | 0x2000
                | 0x20000 | 0x80000 | 0x200000 | 0x800000;
            int flags = actor.SourceRawFlags & ~tracked;
            if (actor.Flipped) flags |= 0x01;
            if (actor.Turbo) flags |= 0x08;
            if (actor.SourceReverseRoute) flags |= 0x200;
            if (actor.SourceBackpedal) flags |= 0x800;
            if (actor.SourceLateHittable) flags |= 0x1000;
            if (actor.SourceWaveArm) flags |= 0x2000;
            if (actor.SourceUnblockable) flags |= 0x20000;
            if (actor.SourceThrowing) flags |= 0x80000;
            if (actor.SourcePendingFireCheck) flags |= 0x200000;
            if (actor.SourceHelmetPopped) flags |= 0x800000;
            return flags;
        }

        private static SourceBallControlView BallControl(SimulationState model)
        {
            int carrier = model.CarrierId;
            long duration = carrier < 0 ? 0 : Math.Max(0L, model.SourceTick - model.CatchSourceTick);
            int ticks = (int)Math.Min(int.MaxValue, duration);
            return new SourceBallControlView(carrier < 0 ? -1 : model.Players[carrier].SourceControllerStation,
                carrier, (TeamId)model.SourceLastPossession, ticks,
                model.Phase == GamePhase.LiveBall && carrier >= 0 && ticks >= 18);
        }
    }
}
