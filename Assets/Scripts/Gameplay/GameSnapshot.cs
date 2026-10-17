using System;
using System.Collections.Generic;
using Blitz.Contracts;

namespace Blitz.Gameplay
{
    internal sealed class GameSnapshot : ISourcePresentationState
    {
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

        internal GameSnapshot(SimulationState model)
        {
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
                    model.SourceOffenseFlipped);
            }
        }

        internal static PlayerView SourcePlayerView(ActorState actor, long tick, bool relativeToOrigin = false)
        {
            return new PlayerView(actor.Id, actor.Team, relativeToOrigin ? new FieldVector(50f, 0f) : actor.Position,
                actor.Velocity, actor.Turbo01, relativeToOrigin ? 0f : actor.Height,
                SourcePoseMath.Facing(actor.Facing), actor.Role, actor.AnimationState.View(actor, tick),
                actor.ModelScale, new SourceActorView(actor.SourceControllerStation, actor.SourceRoster, actor.NoCatchTicks,
                    new SourceActorPresentationView(SourceActorFlags(actor), (int)actor.SourceMode)));
        }

        private static int SourceActorFlags(ActorState actor)
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
