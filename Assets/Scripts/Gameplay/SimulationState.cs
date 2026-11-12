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
        internal long SourceFakeBTick = -1;
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

    internal sealed class SourceAudibleSprite
    {
        internal long Serial, Parent;
        internal int Id;
        internal TeamId Team;
        internal uint Color = 0xffffffff;
        internal int Combiner = 4, Chroma = 1;
    }

    internal sealed class SourceAudibleChild
    {
        internal long Serial;
        internal SourceAudibleSprite Sprite;
        internal SourceAudibleFlashStage Stage;
        internal long? EntryTick, CheckTick;
        internal bool? CheckExists;
        internal uint? Color;
        internal int? Combiner;
        internal int Cycles;
        internal long BirthTick;
        internal SourceProcessView LastProcess;
    }

    internal sealed class SourceAudibleState
    {
        internal readonly int[] Timers = new int[2], Actions = new int[2], Choices = new int[2];
        internal readonly bool[] Shown = new bool[2], Pending = new bool[2];
        internal readonly int[] Callers = { -1, -1 };
        internal readonly short[,,] Ids = new short[4, 2, 3];
        internal readonly SourcePlayDefinition[] Originals = new SourcePlayDefinition[2];
        internal readonly SourcePlaySelection[] OriginalSelections = new SourcePlaySelection[2];
        internal readonly SourceAudibleSprite[] Backgrounds = new SourceAudibleSprite[2];
        internal readonly List<SourceAudibleSprite> Objects = new List<SourceAudibleSprite>();
        internal readonly List<SourceAudibleSprite> NonAlpha = new List<SourceAudibleSprite>();
        internal readonly List<SourceAudibleSprite> Alpha = new List<SourceAudibleSprite>();
        internal readonly List<SourceAudibleChild> Children = new List<SourceAudibleChild>();
        internal readonly List<SourceDirectSoundCall> Calls = new List<SourceDirectSoundCall>();
        internal bool OriginalFlip;
        internal int OffenseSide;
        internal SourceAudibleRestartStage RestartStage;
        // These original powerups are neutral in the admitted ordinary profile.
        internal bool NoPlaySelection = false, SuperFieldGoal = false, NoPunt = false;
        internal int PlaybookMode = 0;
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
        internal SourceTimeOfPossessionView SourceTimeOfPossession = new SourceTimeOfPossessionView(0, 0);
        internal SourceRulesState SourceRules;
        internal SourceGameFlags SourceFlags;
        internal SourcePlayResult SourceLastResult;
        internal SourcePlayEndCause? SourceLastPlayEndCause;
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
        internal readonly int[] SourcePassAttempts = new int[2];
        internal readonly int[] SourcePassCompletions = new int[2];
        internal readonly int[] SourcePassingYards = new int[2];
        internal readonly int[] SourceTotalYards = new int[2];
        internal readonly int[] SourcePassTouchdowns = new int[2];
        internal readonly int[] SourceReturnYards = new int[2];
        internal readonly int[] SourceKickReturns = new int[2];
        internal readonly int[] SourceRushingYards = new int[2];
        internal readonly int[] SourceRushes = new int[2];
        internal readonly int[] SourceSackYards = new int[2];
        internal readonly int[] SourceSacks = new int[2];
        internal readonly int[] SourceFieldGoalAttempts = new int[2];
        internal readonly int[] SourceFieldGoals = new int[2];
        internal readonly int[] SourceFirstDowns = new int[2];
        internal readonly int[] SourceFourthDownConversions = new int[2];
        internal readonly int[] SourceFourthDownAttempts = new int[2];
        internal readonly int[] SourceTouchdowns = new int[2];
        internal readonly int[] SourceSafeties = new int[2];
        internal readonly int[] SourceTwoPointAttempts = new int[2];
        internal readonly int[] SourceTwoPointConversions = new int[2];
        internal int SourceKickReturnCatch;
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
        internal readonly SourceRandomSelectionView[] SourceRandomSelections = new SourceRandomSelectionView[2];
        internal string SourceRandomProcess = "game_proc";
        internal int SourceRandomActor = -1;
        internal int SourceRandomProcessOrder;
        internal int SourceRandomCallIndex;
        internal int SourceDispatchOrdinal;
        internal int SourcePresentationOrder;
        internal string SourceAnimationFailure;
        internal string SourceCurrentPlayerBank;
        internal string SourceCurrentCursorBank;
        internal string SourceGameTuneBank;
        internal string SourceEoqTuneBank;
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
        internal bool SourceDefaultAudiblesEnabled;
        internal SourceAudibleView SourceAudibles = null;
        internal SourceAudibleState SourceAudibleState = null;
        internal readonly List<SourceAudibleReinitializationCheckpoint> SourceAudibleReinitializations =
            new List<SourceAudibleReinitializationCheckpoint>();
        internal readonly SourceDigitalDirections[] SourceDigitalPrior1 = new SourceDigitalDirections[4];
        internal readonly SourceDigitalDirections[] SourceDigitalPrior2 = new SourceDigitalDirections[4];
        internal readonly SourceDigitalSwitchScan[] SourceDigitalScans = new SourceDigitalSwitchScan[4];
        internal readonly long[] SourceNameCoordinateInvalidation = { -1, -1, -1, -1, -1, -1, -1,
            -1, -1, -1, -1, -1, -1, -1 };
        internal bool SourceStartsEnabled;
        internal int SourceDirectSoundSubcall;
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
        internal readonly SourceVector3[] SourceCameraGroundCenters = new SourceVector3[14];
        internal SourceVector3 SourceCameraBallTarget;
        internal long SourceCameraFeedbackTick = -1;
        internal SourcePortableProcessHost SourceProcesses;
        internal SourcePossessionMessageView SourcePossessionMessage;
        internal System.Collections.ObjectModel.ReadOnlyCollection<SourcePossessionMessageView> SourcePossessionMessages;
        internal bool SourceOffenseTurnover;
        internal bool SourceCameraPossessionContinuation;
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
        internal readonly int?[] SourceCaptainPlayers = new int?[2];
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
        internal SourceCabinetFlowView SourceCabinetFlow;
        internal SourcePlaySelection SourceHomePlay;
        internal SourcePlaySelection SourceAwayPlay;
        internal readonly bool[] SourcePlaySelectionReady = new bool[2];
        internal SourcePatOption SourcePatChoice = SourcePatOption.Unselected;
        internal SourcePatStatus SourcePatStatus = SourcePatStatus.NotExtraPoint;
        internal int SourceSelectionCountdown = -1;
        internal SourceSelectionTimerView SourceSelectionTimer;
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

        internal void CaptureSourceKickReturnCatch(bool offense)
        {
            if (BallKind == SourceBallKind.Kickoff || BallKind == SourceBallKind.Punt && !offense)
                SourceKickReturnCatch = SourceMatchRules.FieldPosition(SourceCatchX, (int)Possession);
        }

        internal void AccumulateSourceFourthDownAttemptAtSelectionReturn(
            SourcePlaybook selectedPlaybook, SourcePlayDefinition selectedOffensePlay)
        {
            if (SourceRules.Down != 4) return;
            if (selectedPlaybook == null)
                throw new ArgumentNullException(nameof(selectedPlaybook));
            if (selectedOffensePlay == null)
                throw new ArgumentNullException(nameof(selectedOffensePlay));
            SourcePlayDefinition canonicalPunt;
            SourcePlayDefinition canonicalFieldGoal;
            if (!selectedPlaybook.Plays.TryGetValue("play_punt", out canonicalPunt)
                || canonicalPunt == null
                || !selectedPlaybook.Plays.TryGetValue("play_field_goal", out canonicalFieldGoal)
                || canonicalFieldGoal == null)
                throw new InvalidOperationException(
                    "Fourth-down selection requires canonical punt and field-goal objects.");
            if (ReferenceEquals(selectedOffensePlay, canonicalPunt)
                || ReferenceEquals(selectedOffensePlay, canonicalFieldGoal))
                return;
            int side = SourceRules.OffenseSide;
            SourceFourthDownAttempts[side] = checked(SourceFourthDownAttempts[side] + 1);
        }

        internal void AccumulateSourcePlayEndStats(SourcePlayEndInput input, SourcePlayResolution resolution,
            int carrierId)
        {
            int side = resolution.Classification.OffenseSide;
            SourcePlayResult result = resolution.Classification.Result;
            SourceGameFlags flags = resolution.Flags;
            bool ordinaryReturn = (flags & (SourceGameFlags.BallPunted | SourceGameFlags.BallKicked)) != 0
                && side != input.State.InitialOffenseSide
                && result == SourcePlayResult.NewPossession
                && carrierId / SliceRules.PlayersPerTeam == side;
            bool quickReturn = (flags & SourceGameFlags.QuickKick) != 0
                && side == input.State.InitialOffenseSide
                && (result == SourcePlayResult.NewPossession || result == SourcePlayResult.Touchdown)
                && carrierId / SliceRules.PlayersPerTeam == side;
            if (ordinaryReturn || quickReturn)
            {
                int returnGain = checked(resolution.FieldEnd - SourceKickReturnCatch);
                SourceReturnYards[side] = checked(SourceReturnYards[side] + returnGain);
                SourceTotalYards[side] = checked(SourceTotalYards[side] + returnGain);
                SourceKickReturns[side] = checked(SourceKickReturns[side] + 1);
            }
            else if ((flags & SourceGameFlags.ForwardPass) != 0)
                AccumulateSourcePassCompletion(input, resolution, carrierId);
            else if ((flags & SourceGameFlags.ForwardPass) == 0
                && side == input.State.InitialOffenseSide
                && (flags & SourceGameFlags.BallPunted) == 0
                && carrierId % SliceRules.PlayersPerTeam == 3
                && (flags & SourceGameFlags.BallCrossedLine) == 0)
            {
                int sackGain = checked(resolution.FieldEnd - input.State.LineOfScrimmage);
                SourceSackYards[1 - side] = checked(SourceSackYards[1 - side] - sackGain);
                SourceSacks[1 - side] = checked(SourceSacks[1 - side] + 1);
            }
            else if ((flags & SourceGameFlags.ForwardPass) == 0
                && side == input.State.InitialOffenseSide
                && (flags & SourceGameFlags.BallPunted) == 0
                && (flags & SourceGameFlags.BallKicked) == 0
                && result != SourcePlayResult.Incomplete)
            {
                int rushGain = checked(resolution.FieldEnd - input.State.LineOfScrimmage);
                SourceRushingYards[side] = checked(SourceRushingYards[side] + rushGain);
                SourceTotalYards[side] = checked(SourceTotalYards[side] + rushGain);
                SourceRushes[side] = checked(SourceRushes[side] + 1);
            }

            int initialSide = input.State.InitialOffenseSide;
            SourcePlayDefinition canonicalFieldGoal;
            if (input.SelectedPlaybook != null
                && input.SelectedPlaybook.Plays.TryGetValue("play_field_goal", out canonicalFieldGoal)
                && ReferenceEquals(input.InitialOffensePlay, canonicalFieldGoal))
                SourceFieldGoalAttempts[initialSide] = checked(SourceFieldGoalAttempts[initialSide] + 1);
            if (result == SourcePlayResult.FieldGoal)
                SourceFieldGoals[initialSide] = checked(SourceFieldGoals[initialSide] + 1);

            if (input.State.Down == 4 && (resolution.JustGotFirstDown
                || result == SourcePlayResult.Touchdown && side == initialSide))
                SourceFourthDownConversions[side] = checked(SourceFourthDownConversions[side] + 1);
            if (resolution.JustGotFirstDown)
                SourceFirstDowns[side] = checked(SourceFirstDowns[side] + 1);
            if (result == SourcePlayResult.Touchdown)
                SourceTouchdowns[side] = checked(SourceTouchdowns[side] + 1);
            // nfl.c updates off_side before these result-switch statistics.
            int nextSide = resolution.State.OffenseSide;
            if (result == SourcePlayResult.Safety || result == SourcePlayResult.PatSafety)
                SourceSafeties[nextSide] = checked(SourceSafeties[nextSide] + 1);
            if (result == SourcePlayResult.FailedConversion || result == SourcePlayResult.Conversion)
                SourceTwoPointAttempts[nextSide] = checked(SourceTwoPointAttempts[nextSide] + 1);
            if (result == SourcePlayResult.Conversion)
                SourceTwoPointConversions[nextSide] = checked(SourceTwoPointConversions[nextSide] + 1);
        }

        internal void AccumulateSourcePassCompletion(SourcePlayEndInput input, SourcePlayResolution resolution,
            int carrierId)
        {
            int side = resolution.Classification.OffenseSide;
            SourcePlayResult result = resolution.Classification.Result;
            SourceGameFlags flags = resolution.Flags;
            bool quickReturn = (flags & SourceGameFlags.QuickKick) != 0
                && side == input.State.InitialOffenseSide
                && (result == SourcePlayResult.NewPossession || result == SourcePlayResult.Touchdown)
                && carrierId / SliceRules.PlayersPerTeam == side;
            if (quickReturn || (flags & SourceGameFlags.ForwardPass) == 0 ||
                result == SourcePlayResult.Incomplete ||
                (flags & SourceGameFlags.PossessionChange) != 0 ||
                input.Cause == SourcePlayEndCause.DeadBallInBounds ||
                side != input.State.InitialOffenseSide)
                return;
            int gain = checked(resolution.FieldEnd - input.State.LineOfScrimmage);
            SourcePassingYards[side] = checked(SourcePassingYards[side] + gain);
            SourceTotalYards[side] = checked(SourceTotalYards[side] + gain);
            SourcePassCompletions[side] = checked(SourcePassCompletions[side] + 1);
            if (result == SourcePlayResult.Touchdown)
                SourcePassTouchdowns[side] = checked(SourcePassTouchdowns[side] + 1);
        }

        internal SimulationState()
        {
            Players = new ActorState[SliceRules.PlayersPerTeam * 2];
            for (int id = 0; id < Players.Length; id++)
                Players[id] = new ActorState(id);
        }
    }
}
