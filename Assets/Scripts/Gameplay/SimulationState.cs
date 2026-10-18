using System;
using System.Collections.Generic;
using Blitz.Contracts;

namespace Blitz.Gameplay
{
    internal sealed class ActorState
    {
        internal readonly int Id;
        internal readonly TeamId Team;
        internal FieldVector Position;
        internal FieldVector SnapPosition;
        internal FieldVector SourcePosition;
        internal float SourceHeight;
        internal bool SourceCoordinates;
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
        internal int SourceControllerStation = -1;
        internal SourceRosterIdentity SourceRoster;
        internal SourceDirectionMath SourceDirections;
        internal SourceActorMode SourceMode;
        internal SourceActorJob SourceJob;
        internal int SourceTarget = -1;
        internal int SourceStick = 24;
        internal int SourcePreviousStick = 24;
        internal SourceDroneButtons SourceButtons;
        internal SourceDroneButtons SourceOldButtons;
        internal SourceDroneButtons SourceNewButtons;
        internal SourceDroneButtons SourceReleasedButtons;
        internal readonly SourceDroneButtons[] SourceButtonHistory = new SourceDroneButtons[5];
        internal int SourceDroneThrowTarget = -1;
        internal int SourceNonATicks;
        internal int SourceNonBTicks;
        internal bool SourceUnblockable;
        internal bool SourceWaveArm;
        internal bool SourceBackpedal;
        internal int SourceBackpedalTarget = -1;
        internal bool SourceReverseRoute;
        internal int SourceForcedMoveHeading = -1;
        internal bool SourceThrowing;
        internal int SourceHandicap = 5;
        internal int SourceRawHandicap = 5;
        internal string SourcePendingCatch;
        internal string SourceRequestedScript;
        internal int SourceCatchLaunchTime;
        internal bool SourceCatchTwoPart;
        internal bool SourcePendingLateral;
        internal bool SourceLateHittable;
        internal FieldVector SourceBallTarget;
        internal FieldVector SourceBallLocation;
        internal int SourceBallTimeRemaining;
        internal bool SourcePlayOver;
        internal bool SourceIsCarrier;
        internal bool SourcePendingFireCheck;
        internal bool SourceHelmetPopped;
        internal int SourceSpinCount;
        internal int SourceRawFlags = 0;
        internal int SourceStickTime;
        internal int SourceBackpedalExpireTime;
        internal int SourceAttackTime;
        internal int SourceAttackMode;
        internal int SourceAttackJointA;
        internal int SourceAttackJointB;
        internal SourceVector3 SourceAttackRadii;
        internal int SourceCelebrateTime;
        internal int SourceOverrideTime;
        internal float SourceOverrideSpeed;
        internal int SourceDroneFace;
        internal int SourceTurnRate;
        internal int SourceUpperTurnRate;
        internal int SourceUpperTurnHack;
        internal float SourceMotionSnapX;
        internal long SourceLastLeapCacheTick = -1;
        internal int SourceLastLeapBytesConsumed;
        internal int SourceLastLeapFlightTicks;
        internal int SourceMaybeDiveCallbacks;
        internal long SourceLastMaybeDiveTick = -1;
        internal int SourceLastSpinPredicateCount = -1;
        internal bool SourceCapturedControlBlock;
        internal bool SourceReturnedFromMovementMode;
        internal int SourceImmediateGeneralRuns;
        internal int? SourceHitPartnerId;
        internal int SourcePuppetLinkId = -1;
        internal long SourcePuppetReleaseTick;
        internal int SourceBlockCount;

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
        internal int SourceQuickReturnActor = -1;
        internal bool SourceKickApproach;
        internal bool SourceFirstKickoff = true;
        internal FieldVector SourceBallVelocity;
        internal float SourceBallVerticalVelocity;
        internal SourceBallFlags SourceBallFlags;
        internal SourceVector3 SourceBallPosition;
        internal SourceVector3 SourceBallOrigin;
        internal SourceVector3 SourceBallTarget;
        internal SourceVector3 SourceBallPrevious;
        internal int SourceBallBounces;
        internal long SourceBallBounceTick;
        internal int SourceBallPitch;
        internal int SourceBallPitchRate = 40;
        internal int SourceBallRoll;
        internal int SourceBallHeading;
        internal int SourceBallElevation;
        internal float SourceBallScale = 2f;
        internal int SourceLastPossession;
        internal int SourceLastCarrierId = -1;
        internal float SourceCatchX;
        internal FieldVector SourceContactPosition;
        internal SourceVector3 SourceContactSourcePosition;
        internal float SourcePlayEndX;
        internal float SourcePlayEndZ;
        internal bool SourceHasContactSpot;
        internal float SourceVictimHeight;
        internal string SourceVictimSequence;
        internal FieldVector SourceCollisionVelocity;
        internal bool SourceQbEvaded;
        internal bool SourcePlowSoundPending;
        internal readonly int[] SourceTackles = new int[2];
        internal readonly int[] SourceOffensivePlays = new int[2];
        internal readonly int[] SourceLostFumbles = new int[2];
        internal readonly int[] SourceInterceptions = new int[2];
        internal readonly int[] SourceDeflections = new int[2];
        internal readonly int[] SourceFumbles = new int[2];
        internal readonly int[] SourceBobbles = new int[2];
        internal readonly int[] SourceLastReceiver = { -1, -1 };
        internal readonly int[] SourceConsecutiveCompletions = new int[2];
        internal readonly int[] SourceConsecutiveLosses = new int[2];
        internal readonly bool[] SourceTeamFire = new bool[2];
        internal bool SourceFireEffectsEnabled;
        internal int SourceForwardCatcher = -1;
        internal readonly int[] SourceTeamHandicap = new int[2];
        internal readonly int[] SourceEffectiveTeamHandicap = new int[2];
        internal SourceGameplayCompatibility SourceCompatibility;
        internal long SourceCompatibilityRepairCount;
        internal readonly System.Collections.Generic.List<SourceCompatibilityRepair> SourceCompatibilityRepairs =
            new System.Collections.Generic.List<SourceCompatibilityRepair>();

        internal int ResolveCompatibilityIndex(SourceCompatibilityRepairKind kind, int subject, int raw,
            int length, string context)
        {
            if (length < 1) throw new ArgumentOutOfRangeException(nameof(length));
            if (raw >= 0 && raw < length) return raw;
            if (SourceCompatibility != SourceGameplayCompatibility.Portable
                || kind == SourceCompatibilityRepairKind.BlockCountConsumerIndex && raw < 0)
                throw new NotSupportedException(context + ": unsupported consumer index " + raw + ".");
            int effective = Math.Max(0, Math.Min(length - 1, raw));
            RecordCompatibilityRepair(kind, subject, raw, effective, context);
            return effective;
        }

        internal void RecordCompatibilityRepair(SourceCompatibilityRepairKind kind, int subject, int raw,
            int effective, string context, int? retainedStick = null)
        {
            if (SourceCompatibility != SourceGameplayCompatibility.Portable)
                throw new InvalidOperationException("Compatibility repairs require the explicit Portable policy.");
            SourceCompatibilityRepairCount++;
            // Retain a bounded chronological tail, plus the lifetime count; never notify per frame.
            if (SourceCompatibilityRepairs.Count == 64) SourceCompatibilityRepairs.RemoveAt(0);
            SourceCompatibilityRepairs.Add(new SourceCompatibilityRepair(SourceCompatibilityRepairCount,
                SourceTick, kind, subject, raw, effective, context, retainedStick));
        }
        internal readonly SourcePlayDefinition[] SourceHandicapPlayHistory = new SourcePlayDefinition[5];
        internal SourceHandicapSettings SourceHandicapSettings;
        internal int SourceHandicapRepetitionBonus;
        internal readonly int[] SourceSpinCount = new int[2];
        internal readonly int[] SourceHurdleCount = new int[2];
        internal int SourceFumblesOnPlay;
        internal bool SourceNoAssist = false;
        internal bool SourceNoFumbles = false;
        internal bool SourceNoInterceptions = false;
        internal bool SourceSuperDrones = false;
        internal bool SourceDronesLose = false;
        internal bool SourceDronesWinning = false;
        internal bool SourceAbusingSpin = false;
        internal bool SourceBeginPlayInitialized;
        internal float SourceFumbleX;
        internal int SourcePendingTackleVictim = -1;
        internal int SourcePendingTackleAttacker = -1;
        internal long SourceRandomDraws;
        internal string SourceRandomProcess = "game_proc";
        internal int SourceRandomActor = -1;
        internal int SourceRandomProcessOrder;
        internal int SourceRandomCallIndex;
        internal int SourceDispatchOrdinal;
        internal int SourcePresentationOrder;
        internal string SourceAnimationFailure;
        internal string SourceCurrentPlayerBank;
        internal bool SourceC2Enabled;
        internal bool? SourceTaunted;
        internal string SourceAnnouncerSwappedBank;
        internal string SourceTauntBank;
        internal long SourceC2EndEpoch;
        internal long SourceC2BankEpoch;
        internal SourceC2PendingEnd SourceC2PendingResult;
        internal IReadOnlyDictionary<string, SourceAnimationProgram> SourceC2Programs;
        internal readonly List<SourceC2WhistleProcess> SourceC2Whistles = new List<SourceC2WhistleProcess>();
        internal long SourceC2WhistleSerial;
        internal readonly uint[] SourceAnimationUniqueMasks = new uint[6];
        internal SourcePlaybook SourcePlaybook;
        internal SourcePlayDefinition SourceOffensePlay;
        internal SourcePlayDefinition SourceDefensePlay;
        internal SourceFormationSlot[] SourceFormationSlots;
        internal readonly SourceRouteState[] SourceDrones = new SourceRouteState[14];
        internal SourceAiParameters SourceAiParameters;
        internal PlayerCommand SourceCurrentInput;
        internal int SourceCameraHeading;
        internal long SourceCameraHeadingSampleTick;
        internal SourceMovementRuntimeData SourceMovementData;
        internal SourceMovementSettings SourceMovementSettings;
        internal SourceMovementCameraFrame SourceMovementCamera;
        internal long SourceMovementCameraTick = -1;
        internal readonly SourceVector3[,] SourceCameraJoints = new SourceVector3[14, 15];
        internal long SourceCameraJointTick = -1;
        internal uint SourceBreathControl;
        internal Func<int, int> SourceCStandardRandom;
        internal SourceQuarterbackPlan SourceQuarterbackPlan;
        internal SourceAiFrame SourceAiFrame;
        internal float[,] SourceDistances;
        internal int[,] SourceAngles;
        internal SourceBlockAssignments SourceBlocking;
        internal IReadOnlyList<SourceReceiverAssessment> SourceReceivers;
        internal long SourceSnapTick;
        internal int SourceMotionDirection = 1;
        internal SourceMatchConfiguration SourceConfiguration;
        internal SourcePresentationSettings SourcePresentation;
        internal bool SourceOffenseFlipped;
        internal ISourceTeamRoster SourceRosters;
        internal SourceCaptainSlot SourceCaptain;
        internal bool SourceRequestedOnside;
        internal int SourceHikeCounterAtSnap;
        internal int SourcePuntStrength;
        internal float SourceKickMeterPercent;
        internal float SourceKickWind;
        internal SourceKickMeterMetrics SourceKickMeterMetrics;
        internal SourceKickMeterProcess SourceKickMeter;
        internal SourceKickLaunchView SourceKickLaunch;
        internal bool SourcePerfectFieldGoal = false;
        internal readonly int[] SourceStationPlayers = { -1, -1, -1, -1 };
        internal readonly float[] SourceStationTurbo = { 100f, 100f, 100f, 100f };
        internal readonly int[,] SourceQuarterScores = new int[7, 2];
        internal SourceMatchResult SourceResultView;
        internal SourcePeriodView SourcePeriodView;
        internal SourceMatchPhase SourceMatchPhase;
        internal SourcePlaySelection SourceHomePlay;
        internal SourcePlaySelection SourceAwayPlay;
        internal readonly bool[] SourcePlaySelectionReady = new bool[2];
        internal SourcePatOption SourcePatChoice = SourcePatOption.Unselected;
        internal SourcePatStatus SourcePatStatus = SourcePatStatus.NotExtraPoint;
        internal int SourceSelectionCountdown = -1;
        internal float SourceHumanTurbo = 100f;
        internal int SourceDisallowPassTicks;
        internal bool SourceBallFrozen
        {
            get { return (SourceBallFlags & SourceBallFlags.Freeze) != 0; }
            set
            {
                if (value) SourceBallFlags |= SourceBallFlags.Freeze;
                else SourceBallFlags &= ~SourceBallFlags.Freeze;
            }
        }
        internal bool SourceBallDeflected
        {
            get { return (SourceBallFlags & SourceBallFlags.Deflected) != 0; }
            set
            {
                if (value) SourceBallFlags |= SourceBallFlags.Deflected;
                else SourceBallFlags &= ~SourceBallFlags.Deflected;
            }
        }
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
