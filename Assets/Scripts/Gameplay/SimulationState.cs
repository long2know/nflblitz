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
        internal ActorRole Role;
        internal float Height;
        internal float ModelScale = 1f;
        internal int Facing = 256;
        internal int UpperFacing = 256;
        internal int TargetFacing = 256;
        internal int TargetUpperFacing = 256;
        internal FieldVector SourceVelocity;
        internal float SourceVerticalVelocity;
        internal float SourceFriction;
        internal SourceVector3 SourceRoot;
        internal SourceAnimationState AnimationState;
        internal ActorAction Action;
        internal bool Turbo;
        internal int NonTurboTicks;
        internal bool Flipped;
        internal FieldVector SourceMove;
        internal int PendingThrowTarget = -1;
        internal bool PendingThrowTurbo;
        internal bool Down;
        internal long ModeChangedTick = -1;
        internal bool SourceInPlay;
        internal bool SourceBallKicked;
        internal bool SourceHasBall;
        internal long AnimationSerial;
        internal int NoCatchTicks;

        internal ActorState(int id)
        {
            Id = id;
            Team = id < SliceRules.PlayersPerTeam ? TeamId.Home : TeamId.Away;
        }
    }

    internal sealed class SimulationState
    {
        internal long Tick;
        internal bool SourceMode;
        internal long SourceTick;
        internal int SourceClockRemainder;
        internal uint PackedGameTime = 0x00020000;
        internal bool ClockActive;
        internal SourceClockState SourceClock;
        internal SourceRulesState SourceRules;
        internal SourceGameFlags SourceFlags;
        internal SourcePlayResult SourceLastResult;
        internal float SourceLinePosition;
        internal int SourceFirstPossession;
        internal int SourceOvertimeKickoff = -1;
        internal bool SourceHalfKickoff;
        internal SourceKickoffKind SourceKickoffKind;
        internal int SourceKickingSide;
        internal bool SourceKickApproach;
        internal bool SourceFirstKickoff = true;
        internal FieldVector SourceBallVelocity;
        internal float SourceBallVerticalVelocity;
        internal bool SourceBallFrozen;
        internal float SourceFumbleX;
        internal int SourcePendingTackleVictim = -1;
        internal int SourcePendingTackleAttacker = -1;
        internal int SourcePendingControl = -1;
        internal long SourceRandomDraws;
        internal float SourceHumanTurbo = 100f;
        internal int SourceDisallowPassTicks;
        internal bool SourceBallDeflected;
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
        internal SourceBallKind BallKind;
        internal FieldVector BallVelocity;
        internal float BallVerticalVelocity;
        internal int LastTouchPlayerId = -1;
        internal long CatchSourceTick = -18;
        internal int ThrowerId = -1;

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
