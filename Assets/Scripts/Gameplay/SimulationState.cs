using Blitz.Contracts;

namespace Blitz.Gameplay
{
    internal sealed class ActorState
    {
        internal readonly int Id;
        internal readonly TeamId Team;
        internal FieldVector Position;
        internal FieldVector SnapPosition;
        internal FieldVector Velocity;
        internal float Turbo01 = 1f;
        internal int ContactCooldownTicks;
        internal int BlockCooldownTicks;

        internal ActorState(int id)
        {
            Id = id;
            Team = id < SliceRules.PlayersPerTeam ? TeamId.Home : TeamId.Away;
        }
    }

    internal sealed class SimulationState
    {
        internal long Tick;
        internal int PlayNumber;
        internal GamePhase Phase = GamePhase.PlaySelect;
        internal TeamId Possession = TeamId.Home;
        internal int Quarter = 1;
        internal int QuarterTicksRemaining = SliceRules.QuarterTicks;
        internal int PreSnapTicksRemaining;
        internal int Down = 1;
        internal float LineOfScrimmage = 20f;
        internal float FirstDownLine = 50f;
        internal int HomeScore;
        internal int AwayScore;
        internal int ControlledPlayerId = 3;
        internal PlayId HomePlay = PlayId.Run;
        internal PlayId AwayPlay = PlayId.Cover;
        internal PlayOutcome LastOutcome;
        internal BallMode BallMode = BallMode.Held;
        internal FieldVector BallPosition;
        internal float BallHeight;
        internal int CarrierId = 3;
        internal int TargetPlayerId = -1;
        internal readonly ActorState[] Players;

        internal float SnapLineOfScrimmage = 20f;
        internal int LiveTicks;
        internal bool ForwardPassUsed;
        internal bool QuarterEndEmitted;
        internal int CatchProtectionTicks;
        internal int PassTicksTotal;
        internal int PassTicksElapsed;
        internal FieldVector PassStart;
        internal FieldVector PassTarget;
        internal int AwayPassDecisionTick;
        internal int RunLane = 1;

        internal int OffenseDirection { get { return GameplayRules.Direction(Possession); } }
        internal int OffenseOffset { get { return (int)Possession * SliceRules.PlayersPerTeam; } }
        internal PlayId OffensivePlay { get { return Possession == TeamId.Home ? HomePlay : AwayPlay; } }
        internal PlayId DefensivePlay { get { return Possession == TeamId.Home ? AwayPlay : HomePlay; } }

        internal SimulationState()
        {
            Players = new ActorState[SliceRules.PlayersPerTeam * 2];
            for (int id = 0; id < Players.Length; id++)
                Players[id] = new ActorState(id);
        }
    }
}
