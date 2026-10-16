using System;
using System.Collections.Generic;
using Blitz.Contracts;

namespace Blitz.Gameplay
{
    public enum SimulationProfile { LegacyBaseline60Hz, OriginalRelease57Hz }

    public sealed partial class GameSimulation : IGameSimulation
    {
        private readonly List<GameEvent> stepEvents = new List<GameEvent>();
        private readonly ISourceAnimationLibrary sourceAnimations;
        private readonly Queue<PlayerCommand> pendingSourceActions = new Queue<PlayerCommand>();
        private uint randomState;

        public SimulationProfile Profile { get; }
        public string ReferenceProfile
        {
            get
            {
                return Profile == SimulationProfile.OriginalRelease57Hz
                    ? "Original normal 57 Hz source animation/rules; provisional RNG, AI and contact coverage"
                    : "Explicit approximate 60 Hz legacy baseline";
            }
        }
        internal SimulationState Model { get; private set; }
        public IGameState State { get; private set; }
        public IReadOnlyList<GameEvent> Events { get; private set; }

        public GameSimulation(int seed = 1)
        {
            Profile = SimulationProfile.LegacyBaseline60Hz;
            Restart(seed);
        }

        public GameSimulation(ISourceAnimationLibrary animations, int seed = 1)
        {
            sourceAnimations = animations ?? throw new ArgumentNullException(nameof(animations));
            Profile = SimulationProfile.OriginalRelease57Hz;
            Restart(seed);
        }

        public void Restart(int seed = 1)
        {
            randomState = unchecked((uint)seed) ^ 0x9E3779B9u;
            Model = new SimulationState();
            stepEvents.Clear();
            pendingSourceActions.Clear();
            if (Profile == SimulationProfile.OriginalRelease57Hz)
                InitializeSource();
            else
            {
                PlayerSimulation.LineUp(Model);
                Model.BallHeight = 0f;
            }
            Publish();
        }

        public void Step(PlayerCommand command)
        {
            stepEvents.Clear();
            Model.Tick++;
            if (Profile == SimulationProfile.OriginalRelease57Hz)
            {
                if (command.Action != InputAction.None)
                    pendingSourceActions.Enqueue(command);
                Model.SourceClockRemainder += SourceTiming.DefaultTicksPerSecond;
                if (Model.SourceClockRemainder >= SliceRules.TicksPerSecond)
                {
                    Model.SourceClockRemainder -= SliceRules.TicksPerSecond;
                    Model.SourceTick++;
                    PlayerCommand edge = pendingSourceActions.Count == 0 ? default(PlayerCommand)
                        : pendingSourceActions.Dequeue();
                    SourceStep(new PlayerCommand(PlayerSimulation.NormalizeInput(command.Move), command.Turbo,
                        edge.Action, edge.Play, edge.TargetPlayerId));
                }
                Publish();
                return;
            }
            switch (Model.Phase)
            {
                case GamePhase.PlaySelect:
                    if (command.Action == InputAction.SelectPlay)
                        SelectPlay(command.Play);
                    break;
                case GamePhase.PreSnap:
                    if (Model.Possession == TeamId.Home && command.Action == InputAction.Snap)
                        Snap();
                    else if (--Model.PreSnapTicksRemaining <= 0)
                        Snap();
                    break;
                case GamePhase.LiveBall:
                    LiveStep(command);
                    break;
                case GamePhase.DeadBall:
                    if (command.Action == InputAction.Continue)
                        Continue();
                    break;
            }
            Publish();
        }

        private void SelectPlay(PlayId play)
        {
            bool homeOffense = Model.Possession == TeamId.Home;
            if (homeOffense ? play != PlayId.Run && play != PlayId.QuickPass
                : play != PlayId.Cover && play != PlayId.Blitz)
                return;

            Model.HomePlay = play;
            Model.AwayPlay = homeOffense
                ? (NextInt(2) == 0 ? PlayId.Cover : PlayId.Blitz)
                : (NextInt(2) == 0 ? PlayId.Run : PlayId.QuickPass);
            Model.AwayPassDecisionTick = 36 + NextInt(30);
            Model.RunLane = NextInt(2) == 0 ? -1 : 1;
            PlayerSimulation.LineUp(Model);
            Model.PreSnapTicksRemaining = homeOffense ? SliceRules.PreSnapTicks : SliceRules.TicksPerSecond;
            Model.Phase = GamePhase.PreSnap;
            Emit(GameEventType.PlaySelected, TeamId.Home, -1, 0, Model.BallPosition);
        }

        private void Snap()
        {
            Model.Phase = GamePhase.LiveBall;
            Model.PreSnapTicksRemaining = 0;
            Model.PlayNumber++;
            Model.LiveTicks = 0;
            Model.ForwardPassUsed = false;
            Model.CatchProtectionTicks = 0;
            Model.PassTicksElapsed = 0;
            Model.PassTicksTotal = 0;
            Model.SnapLineOfScrimmage = Model.LineOfScrimmage;
            Model.LastOutcome = PlayOutcome.None;
            Model.CarrierId = Model.OffenseOffset + 3;
            Model.TargetPlayerId = -1;
            Model.BallMode = BallMode.Held;
            for (int id = 0; id < Model.Players.Length; id++)
                Model.Players[id].SnapPosition = Model.Players[id].Position;
            SynchronizeHeldBall();
            Emit(GameEventType.Snapped, Model.Possession, Model.CarrierId, 0, Model.BallPosition);
        }

        private void LiveStep(PlayerCommand command)
        {
            Model.LiveTicks++;
            if (Model.QuarterTicksRemaining > 0)
                Model.QuarterTicksRemaining--;
            if (Model.CatchProtectionTicks > 0)
                Model.CatchProtectionTicks--;

            if (Model.BallMode == BallMode.Held)
            {
                SynchronizeHeldBall();
                if (ResolveLocation())
                    return;
            }
            if (Model.Possession == TeamId.Away && command.Action == InputAction.SwitchDefender)
                Model.ControlledPlayerId = PlayerSimulation.NearestHome(Model, Model.ControlledPlayerId);

            bool flightAtEntry = Model.BallMode == BallMode.InFlight;
            PlayerSimulation.Update(Model, command);
            if (Model.BallMode == BallMode.Held)
            {
                SynchronizeHeldBall();
                if (ResolveLocation())
                    return;
            }

            if (Model.Possession == TeamId.Home && command.Action == InputAction.Pass)
                TryPass(command.TargetPlayerId);
            else if (Model.Possession == TeamId.Away)
                AwayPassingDecision();

            if (flightAtEntry)
                AdvancePass();
            if (Model.Phase != GamePhase.LiveBall || Model.BallMode != BallMode.Held)
                return;

            SynchronizeHeldBall();
            if (ResolveLocation())
                return;
            if (Model.Possession == TeamId.Away && command.Action == InputAction.Tackle)
            {
                ActorState defender = Model.Players[Model.ControlledPlayerId];
                if (PlayerSimulation.DistanceSquared(defender.Position, Model.BallPosition)
                    <= PlayerSimulation.ManualTackleReach * PlayerSimulation.ManualTackleReach)
                {
                    EndPlay(PlayOutcome.Tackled, Model.CarrierId, Model.BallPosition, defender.Id);
                    return;
                }
            }
            AutomaticTackles();
        }

        private bool ResolveLocation()
        {
            if (GameplayRules.IsOutside(Model.BallPosition))
            {
                EndPlay(PlayOutcome.OutOfBounds, Model.CarrierId, Model.BallPosition);
                return true;
            }
            if (GameplayRules.IsOpponentEndZone(Model.Possession, Model.BallPosition.X))
            {
                EndPlay(PlayOutcome.Touchdown, Model.CarrierId, Model.BallPosition);
                return true;
            }
            return false;
        }

        private void TryPass(int targetId)
        {
            int quarterbackId = Model.OffenseOffset + 3;
            if (Model.BallMode != BallMode.Held || Model.CarrierId != quarterbackId || Model.ForwardPassUsed
                || targetId < Model.OffenseOffset + 4 || targetId > Model.OffenseOffset + 6)
                return;
            ActorState quarterback = Model.Players[quarterbackId];
            if (Model.OffenseDirection * (quarterback.Position.X - Model.SnapLineOfScrimmage) > 0f)
                return;

            ActorState receiver = Model.Players[targetId];
            float distance = (float)Math.Sqrt(PlayerSimulation.DistanceSquared(quarterback.Position, receiver.Position));
            int flightTicks = (int)Math.Ceiling(distance / 32f * SliceRules.TicksPerSecond);
            flightTicks = Math.Max(22, Math.Min(66, flightTicks));
            FieldVector lead = PlayerSimulation.Add(receiver.Position,
                PlayerSimulation.Scale(PlayerSimulation.ReceiverLeadVelocity(Model, receiver),
                    (float)flightTicks / SliceRules.TicksPerSecond));
            lead = new FieldVector(GameplayRules.Clamp(lead.X, -7f, 107f),
                GameplayRules.Clamp(lead.Z, -SliceRules.FieldHalfWidthYards + 1f, SliceRules.FieldHalfWidthYards - 1f));
            if (Model.OffenseDirection * (lead.X - quarterback.Position.X) <= 0f)
                return;

            Model.ForwardPassUsed = true;
            Model.ThrowerId = quarterbackId;
            Model.BallMode = BallMode.InFlight;
            Model.CarrierId = -1;
            Model.TargetPlayerId = targetId;
            Model.PassStart = quarterback.Position;
            Model.PassTarget = lead;
            Model.PassTicksTotal = flightTicks;
            Model.PassTicksElapsed = 0;
            Model.BallPosition = quarterback.Position;
            Model.BallHeight = 1.1f;
            if (Model.Possession == TeamId.Home)
                Model.ControlledPlayerId = quarterbackId;
            Emit(GameEventType.PassThrown, Model.Possession, targetId, 0, quarterback.Position,
                quarterbackId, targetId);
        }

        private void AdvancePass()
        {
            Model.PassTicksElapsed++;
            float progress = Math.Min(1f, (float)Model.PassTicksElapsed / Model.PassTicksTotal);
            Model.BallPosition = PlayerSimulation.Add(Model.PassStart,
                PlayerSimulation.Scale(PlayerSimulation.Subtract(Model.PassTarget, Model.PassStart), progress));
            Model.BallHeight = 1.1f + 5f * (float)Math.Sin(Math.PI * progress);
            if (Model.PassTicksElapsed < Model.PassTicksTotal)
                return;

            int targetId = Model.TargetPlayerId;
            ActorState receiver = Model.Players[targetId];
            if (!GameplayRules.IsOutside(receiver.Position)
                && PlayerSimulation.DistanceSquared(receiver.Position, Model.PassTarget)
                    <= PlayerSimulation.CatchReach * PlayerSimulation.CatchReach)
            {
                Model.BallMode = BallMode.Held;
                Model.CarrierId = targetId;
                Model.TargetPlayerId = -1;
                Model.CatchProtectionTicks = 12;
                if (Model.Possession == TeamId.Home)
                    Model.ControlledPlayerId = targetId;
                SynchronizeHeldBall();
                Emit(GameEventType.PassCaught, Model.Possession, targetId, 0, Model.BallPosition,
                    targetId, Model.ThrowerId);
            }
            else
            {
                Model.BallHeight = 0f;
                EndPlay(PlayOutcome.Incomplete, targetId, Model.BallPosition);
            }
        }

        private void AwayPassingDecision()
        {
            if (Model.OffensivePlay != PlayId.QuickPass || Model.ForwardPassUsed
                || Model.BallMode != BallMode.Held || Model.CarrierId != Model.OffenseOffset + 3)
                return;
            bool pressure = Model.LiveTicks >= 20 && PlayerSimulation.NearestOpponentDistanceSquared(
                Model, Model.Players[Model.CarrierId]) < 36f;
            if (Model.LiveTicks < Model.AwayPassDecisionTick && !pressure)
                return;

            int bestId = Model.OffenseOffset + 4;
            float best = float.MinValue;
            for (int slot = 4; slot < SliceRules.PlayersPerTeam; slot++)
            {
                ActorState receiver = Model.Players[Model.OffenseOffset + slot];
                float separation = (float)Math.Sqrt(PlayerSimulation.NearestOpponentDistanceSquared(Model, receiver));
                float gain = Model.OffenseDirection * (receiver.Position.X - Model.SnapLineOfScrimmage);
                float score = separation + gain * 0.12f;
                if (score > best)
                {
                    best = score;
                    bestId = receiver.Id;
                }
            }
            TryPass(bestId);
        }

        private void AutomaticTackles()
        {
            if (Model.LiveTicks < 12 || Model.CatchProtectionTicks > 0)
                return;
            for (int id = 0; id < Model.Players.Length; id++)
            {
                ActorState defender = Model.Players[id];
                if (defender.Team == Model.Possession || id == Model.ControlledPlayerId
                    || defender.ContactCooldownTicks > 0)
                    continue;
                if (PlayerSimulation.DistanceSquared(defender.Position, Model.BallPosition)
                    <= PlayerSimulation.AutoTackleReach * PlayerSimulation.AutoTackleReach)
                {
                    EndPlay(PlayOutcome.Tackled, Model.CarrierId, Model.BallPosition, defender.Id);
                    return;
                }
            }
        }

        private void EndPlay(PlayOutcome cause, int affectedPlayerId, FieldVector position, int attackerId = -1)
        {
            if (Model.Phase != GamePhase.LiveBall)
                return;
            TeamId offense = Model.Possession;
            PlayResolution resolution = GameplayRules.Resolve(offense, Model.Down, Model.SnapLineOfScrimmage,
                Model.FirstDownLine, position.X, cause);
            if (cause == PlayOutcome.Tackled)
                Emit(GameEventType.Tackled, offense, affectedPlayerId, 0, position, attackerId, affectedPlayerId);
            else if (cause == PlayOutcome.OutOfBounds)
                Emit(GameEventType.OutOfBounds, offense, affectedPlayerId, 0, position);
            else if (cause == PlayOutcome.Incomplete)
                Emit(GameEventType.Incomplete, offense, affectedPlayerId, 0, position);

            Model.Phase = GamePhase.DeadBall;
            Model.LastOutcome = resolution.Outcome;
            Model.Possession = resolution.Possession;
            Model.Down = resolution.Down;
            Model.LineOfScrimmage = resolution.LineOfScrimmage;
            Model.FirstDownLine = resolution.FirstDownLine;
            Model.HomeScore += resolution.HomePoints;
            Model.AwayScore += resolution.AwayPoints;
            Model.BallMode = BallMode.Dead;
            Model.BallPosition = position;
            Model.BallHeight = 0f;
            Model.CarrierId = -1;
            Model.TargetPlayerId = -1;
            for (int id = 0; id < Model.Players.Length; id++)
                Model.Players[id].Velocity = default(FieldVector);

            FieldVector nextSpot = new FieldVector(Model.LineOfScrimmage, 0f);
            if (resolution.Outcome == PlayOutcome.FirstDown)
                Emit(GameEventType.FirstDown, offense, affectedPlayerId, 0, nextSpot);
            else if (resolution.Outcome == PlayOutcome.TurnoverOnDowns)
                Emit(GameEventType.TurnoverOnDowns, Model.Possession, -1, 0, nextSpot);
            else if (resolution.Outcome == PlayOutcome.Touchdown)
                Emit(GameEventType.Touchdown, offense, affectedPlayerId, SliceRules.TouchdownPoints, position);
            else if (resolution.Outcome == PlayOutcome.Safety)
                Emit(GameEventType.Safety, GameplayRules.Opponent(offense), affectedPlayerId, SliceRules.SafetyPoints, position);
            if (offense != Model.Possession)
                Emit(GameEventType.PossessionChanged, Model.Possession, -1, 0, nextSpot);
            Model.ControlledPlayerId = Model.Possession == TeamId.Home ? 3 : PlayerSimulation.NearestHome(Model, -1);
            if (Model.QuarterTicksRemaining == 0 && !Model.QuarterEndEmitted)
            {
                Model.QuarterEndEmitted = true;
                Emit(GameEventType.QuarterEnded, TeamId.Home, -1, Model.Quarter, default(FieldVector));
            }
        }

        private void Continue()
        {
            if (Model.QuarterTicksRemaining == 0)
            {
                if (Model.Quarter == SliceRules.QuarterCount)
                {
                    Model.Phase = GamePhase.GameOver;
                    int result = Model.HomeScore.CompareTo(Model.AwayScore);
                    Emit(GameEventType.GameEnded, TeamId.Home, -1, result, default(FieldVector));
                    return;
                }
                Model.Quarter++;
                Model.QuarterTicksRemaining = SliceRules.QuarterTicks;
                Model.QuarterEndEmitted = false;
            }
            Model.PreSnapTicksRemaining = 0;
            Model.Phase = GamePhase.PlaySelect;
            PlayerSimulation.LineUp(Model);
        }

        private void SynchronizeHeldBall()
        {
            Model.BallPosition = Model.Players[Model.CarrierId].Position;
            Model.BallHeight = 1.1f;
            Model.TargetPlayerId = -1;
        }

        private int NextInt(int maximum)
        {
            randomState = unchecked(randomState * 1664525u + 1013904223u);
            return (int)((randomState >> 8) % (uint)maximum);
        }

        private void Emit(GameEventType type, TeamId team, int playerId, int value, FieldVector position,
            int actorPlayerId = -1, int targetPlayerId = -1)
        {
            stepEvents.Add(new GameEvent(Model.Tick, type, team, playerId, value, position,
                actorPlayerId, targetPlayerId, Model.SourceMode ? Model.SourceTick : -1));
        }

        private void Publish()
        {
            State = new GameSnapshot(Model);
            Events = Array.AsReadOnly(stepEvents.ToArray());
        }
    }
}
