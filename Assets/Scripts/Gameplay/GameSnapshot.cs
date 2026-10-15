using System;
using System.Collections.Generic;
using Blitz.Contracts;

namespace Blitz.Gameplay
{
    internal sealed class GameSnapshot : IGameState
    {
        public long Tick { get; }
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

        internal GameSnapshot(SimulationState model)
        {
            Tick = model.Tick;
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
            Ball = new BallView(model.BallMode, model.BallPosition, model.BallHeight,
                model.BallMode == BallMode.Held ? model.CarrierId : -1,
                model.BallMode == BallMode.InFlight ? model.TargetPlayerId : -1);

            var players = new PlayerView[model.Players.Length];
            for (int id = 0; id < players.Length; id++)
            {
                ActorState actor = model.Players[id];
                players[id] = new PlayerView(actor.Id, actor.Team, actor.Position, actor.Velocity, actor.Turbo01);
            }
            Players = Array.AsReadOnly(players);
        }
    }
}
