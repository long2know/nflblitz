using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Blitz.Contracts
{
    public static class SourceTiming
    {
        public const int DefaultTicksPerSecond = 57;
        public const int JointCount = 15;
        public const ushort AllJoints = 0x7fff;
        public const ushort LowerBodyJoints = 0x7e01;
        public const ushort UpperBodyJoints = 0x01fe;
        public const float SourceUnitsPerYard = 7.8f;
    }

    public enum AnimationSelectionEvidence { Unspecified, Approximate, SourceVerified }
    public enum ActorRole { Unspecified, Lineman, Quarterback, Receiver, Defender, Kicker }
    public enum ActorAction
    {
        Unspecified, PreSnap, Idle, Run, Backpedal, TurboRun, Spin, Juke,
        Hurdle, Dive, Throw, Catch, Intercept, Block, Contact, Fall, GetUp,
        Celebrate, Kick, Punt
    }

    public enum SourceBallKind { Unspecified, Pass, Fumble, Punt, Kickoff, FieldGoal, Bobble, Lateral }

    public enum SourceAnimationCodeReturnEvidence { LegacyUnqualified, KnownInt, VoidOpaque }
    public enum SourceC2SoundFunctionKind { TauntSpeech = 1, HitFunctionExtension, WhistleProc, WhistleImmediate }
    public enum SourceC2EntryKind { OriginalProgramBootSoundBanks, VerifiedOriginalContinuation }
    public enum SourceC2EventKind
    {
        BootBanksSelected, QuarterBanksSelected, PlayOverQueued, ResultCommitted,
        TauntSelected, QuarterbackMadSelected, ReceiverMadSelected,
        WhistleSpawned, WhistleFirstWake, WhistleEmission, CallbackEntered
    }

    public readonly struct SourceC2SoundState
    {
        public string CurrentTauntBank { get; }
        public string CurrentPlayerBank { get; }
        public int? ActorFlags { get; }
        public int? ActorRawPlayerMode { get; }
        public int? ActorPlayerNumber { get; }
        public int? ActorStation { get; }
        public bool? HasHitPartner { get; }
        public int? HitPartnerRawPlayerMode { get; }
        public int? HitPartnerStation { get; }
        public int? CurrentBallCarrier { get; }
        public int? CurrentIntendedReceiver { get; }
        public SourceC2SoundState(string currentTauntBank, string currentPlayerBank,
            int? actorFlags = null, int? actorRawPlayerMode = null, int? actorPlayerNumber = null,
            int? actorStation = null, bool? hasHitPartner = null, int? hitPartnerRawPlayerMode = null,
            int? hitPartnerStation = null, int? currentBallCarrier = null, int? currentIntendedReceiver = null)
        {
            CurrentTauntBank = currentTauntBank; CurrentPlayerBank = currentPlayerBank;
            ActorFlags = actorFlags; ActorRawPlayerMode = actorRawPlayerMode; ActorPlayerNumber = actorPlayerNumber;
            ActorStation = actorStation; HasHitPartner = hasHitPartner;
            HitPartnerRawPlayerMode = hitPartnerRawPlayerMode; HitPartnerStation = hitPartnerStation;
            CurrentBallCarrier = currentBallCarrier; CurrentIntendedReceiver = currentIntendedReceiver;
        }
    }

    public sealed class SourceC2SoundContext
    {
        private readonly SourceCallbackCue animationCue;
        public SourceC2SoundFunctionKind Kind { get; }
        public long SourceTick { get; }
        public string Process { get; }
        public int ProcessOrder { get; }
        public int FirstCallIndex { get; }
        public int ActorId { get; }
        public string SourcePath { get; }
        public string FunctionSymbol { get; }
        public int SourceLine { get; }
        public int Argument0 { get; }
        public int Argument1 { get; }
        public int Argument2 { get; }
        public long InvocationId { get; }
        public bool HasOriginalAnimationCue { get { return animationCue != null; } }
        public SourceCallbackCue OriginalAnimationCue
        {
            get
            {
                if (animationCue == null) throw new InvalidOperationException("Native process sound has no animation cue or actor.");
                return animationCue;
            }
        }

        public SourceC2SoundContext(SourceC2SoundFunctionKind kind, long sourceTick, string process,
            int processOrder, int actorId, string sourcePath, string function, int sourceLine,
            int argument0 = 0, int argument1 = 0, int argument2 = 0,
            SourceCallbackCue originalAnimationCue = null, long invocationId = 0, int firstCallIndex = 0)
        {
            if (kind < SourceC2SoundFunctionKind.TauntSpeech || kind > SourceC2SoundFunctionKind.WhistleImmediate)
                throw new ArgumentOutOfRangeException(nameof(kind));
            if (sourceTick < 0 || processOrder < 0 || sourceLine <= 0 || invocationId < 0 || firstCallIndex < 0)
                throw new ArgumentOutOfRangeException(nameof(sourceTick));
            if (string.IsNullOrEmpty(process) || string.IsNullOrEmpty(sourcePath) || string.IsNullOrEmpty(function))
                throw new ArgumentException("Actual original process and source site are required.");
            if (originalAnimationCue == null)
            {
                if (kind != SourceC2SoundFunctionKind.WhistleProc && kind != SourceC2SoundFunctionKind.WhistleImmediate)
                    throw new ArgumentException("Animation sound requires its original cue.");
                if (kind == SourceC2SoundFunctionKind.WhistleProc && actorId != -1)
                    throw new ArgumentException("Original whistle_proc executes without an actor identity.");
                if (kind == SourceC2SoundFunctionKind.WhistleImmediate && actorId < 0)
                    throw new ArgumentException("Original immediate whistle executes in an actual actor process.");
            }
            else if (actorId < 0 || actorId > 13 || originalAnimationCue.Context.ActorPlayerId != actorId ||
                originalAnimationCue.Context.SourceTick != sourceTick ||
                originalAnimationCue.Kind != SourcePresentationCueKind.SoundCallback ||
                originalAnimationCue.Context.SourcePath != sourcePath || originalAnimationCue.Context.SourceLine != sourceLine ||
                originalAnimationCue.FunctionSymbol != function || originalAnimationCue.Argument0 != argument0 ||
                originalAnimationCue.Argument1 != argument1 || originalAnimationCue.Argument2 != argument2)
                throw new ArgumentException("Original sound cue must identify the same actor, tick, source site and argument tuple.");
            if (actorId < -1 || actorId > 13) throw new ArgumentOutOfRangeException(nameof(actorId));
            Kind = kind; SourceTick = sourceTick; Process = process; ProcessOrder = processOrder; ActorId = actorId;
            SourcePath = sourcePath; FunctionSymbol = function; SourceLine = sourceLine;
            Argument0 = argument0; Argument1 = argument1; Argument2 = argument2;
            animationCue = originalAnimationCue; InvocationId = invocationId;
            FirstCallIndex = firstCallIndex;
        }
    }

    public sealed class SourceC2InitialState
    {
        public bool? Taunted { get; }
        public string AnnouncerBank { get; }
        public string TauntBank { get; }
        public string ProvenanceSha256 { get; }
        public bool? ResultPending { get; }
        public int? DeferredProcessCount { get; }
        public SourceC2InitialState(bool? taunted, string announcerBank, string tauntBank, string provenanceSha256,
            bool? resultPending, int? deferredProcessCount)
        {
            if (provenanceSha256 == null || provenanceSha256.Length != 64)
                throw new ArgumentException("A verified original continuation-state provenance pin is required.");
            foreach (char c in provenanceSha256)
                if (!(c >= '0' && c <= '9' || c >= 'a' && c <= 'f'))
                    throw new ArgumentException("Expected lowercase original continuation-state SHA256.");
            if (announcerBank != null && announcerBank != "tkbank3" && announcerBank != "tkbank4" ||
                tauntBank != null && tauntBank != "taunts3" && tauntBank != "taunts4")
                throw new ArgumentException("Unknown original swapped bank identity.");
            Taunted = taunted; AnnouncerBank = announcerBank; TauntBank = tauntBank;
            ProvenanceSha256 = provenanceSha256;
            if (deferredProcessCount.HasValue && deferredProcessCount.Value < 0)
                throw new ArgumentOutOfRangeException(nameof(deferredProcessCount));
            ResultPending = resultPending; DeferredProcessCount = deferredProcessCount;
        }
    }

    public readonly struct SourceC2Event
    {
        public SourceC2EventKind Kind { get; }
        public long SourceTick { get; }
        public string Process { get; }
        public int ProcessOrder { get; }
        public int ActorId { get; }
        public long Epoch { get; }
        public string Detail { get; }
        public bool? TauntedBefore { get; }
        public bool? TauntedAfter { get; }
        public string SourcePath { get; }
        public string FunctionSymbol { get; }
        public int? SourceLine { get; }
        public int? SelectedOrdinal { get; }
        public SourceC2Event(SourceC2EventKind kind, long tick, string process, int order, int actor,
            long epoch, string detail, bool? before, bool? after, string sourcePath = null,
            string function = null, int? sourceLine = null, int? selectedOrdinal = null)
        {
            Kind = kind; SourceTick = tick; Process = process; ProcessOrder = order; ActorId = actor;
            Epoch = epoch; Detail = detail; TauntedBefore = before; TauntedAfter = after;
            SourcePath = sourcePath; FunctionSymbol = function; SourceLine = sourceLine; SelectedOrdinal = selectedOrdinal;
        }
    }

    public readonly struct SourceC2WhistleView
    {
        public long Id { get; }
        public long CreatedSourceTick { get; }
        public bool FirstWakePending { get; }
        public int SleepRemaining { get; }
        public SourceC2WhistleView(long id, long tick, bool firstWakePending, int sleepRemaining)
        { Id = id; CreatedSourceTick = tick; FirstWakePending = firstWakePending; SleepRemaining = sleepRemaining; }
    }

    public readonly struct SourceC2StateView
    {
        public bool Enabled { get; }
        public string EntryProfile { get; }
        public string DataSha256 { get; }
        public bool? Taunted { get; }
        public string AnnouncerBank { get; }
        public string TauntBank { get; }
        public long EndEpoch { get; }
        public bool ResultPending { get; }
        public int DeferredWhistles { get; }
        public SourceC2EntryKind EntryKind { get; }
        public string EntryProvenance { get; }
        public long BankEpoch { get; }
        public IReadOnlyList<SourceC2WhistleView> WhistleProcesses { get; }
        public SourceC2StateView(string entry, string data, bool? taunted, string announcer,
            string bank, long epoch, bool pending, int whistles, SourceC2EntryKind entryKind,
            string entryProvenance, long bankEpoch, IReadOnlyList<SourceC2WhistleView> processes)
        {
            Enabled = true; EntryProfile = entry; DataSha256 = data; Taunted = taunted;
            AnnouncerBank = announcer; TauntBank = bank; EndEpoch = epoch;
            ResultPending = pending; DeferredWhistles = whistles;
            EntryKind = entryKind; EntryProvenance = entryProvenance; BankEpoch = bankEpoch;
            WhistleProcesses = processes;
        }
    }

    public readonly struct SourceVector3
    {
        public float X { get; }
        public float Y { get; }
        public float Z { get; }

        public SourceVector3(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }
    }

    public readonly struct SourceRotation
    {
        public float X { get; }
        public float Y { get; }
        public float Z { get; }
        public float W { get; }

        public SourceRotation(float x, float y, float z, float w)
        {
            X = x;
            Y = y;
            Z = z;
            W = w;
        }
    }

    // Captures the actual transition origin, including interrupted blends/turn hacks.
    public sealed class SourceJointPose
    {
        public ReadOnlyCollection<SourceRotation> Rotations { get; }

        public SourceJointPose(IReadOnlyList<SourceRotation> rotations)
        {
            if (rotations == null)
                throw new ArgumentNullException(nameof(rotations));
            if (rotations.Count != SourceTiming.JointCount)
                throw new ArgumentException("A source player pose requires fifteen joint rotations.", nameof(rotations));
            var copy = new SourceRotation[rotations.Count];
            for (int i = 0; i < copy.Length; i++)
                copy[i] = rotations[i];
            Rotations = Array.AsReadOnly(copy);
        }
    }

    public readonly struct SourceAnimationFrame
    {
        public int SourceFrame { get; }
        public SourceVector3 AuthoringRoot { get; }
        public SourceVector3 ScaledRoot { get; }
        public SourceJointPose Pose { get; }

        public SourceAnimationFrame(int sourceFrame, SourceVector3 authoringRoot,
            SourceVector3 scaledRoot, SourceJointPose pose)
        {
            SourceFrame = sourceFrame;
            AuthoringRoot = authoringRoot;
            ScaledRoot = scaledRoot;
            Pose = pose;
        }
    }

    public interface ISourceAnimationClip
    {
        string SequenceName { get; }
        string SourceRevision { get; }
        string SourcePath { get; }
        string SourceGitBlobSha1 { get; }
        string SourceSha256 { get; }
        string DecodedSha256 { get; }
        uint SourceFlags { get; }
        int SourceFrameCount { get; }
        // Script frame ordinals index this map; its values are WAN authoring indices.
        IReadOnlyList<int> SelectedSourceFrames { get; }
        SourceAnimationFrame GetFrame(int sourceFrame);
    }

    public interface ISourceAnimationLibrary
    {
        ISourceAnimationClip GetClip(string sequenceName);
    }

    public readonly struct SourceAnimationSample
    {
        public string SequenceName { get; }
        // Published frame indices are authoring indices, not selected-array ordinals.
        public int SourceFrame { get; }
        public int NextSourceFrame { get; }
        public float Interpolation01 { get; }
        public bool IsSpecified { get { return !string.IsNullOrEmpty(SequenceName); } }

        public SourceAnimationSample(string sequenceName, int sourceFrame, int nextSourceFrame,
            float interpolation01 = 0f)
        {
            SequenceName = sequenceName;
            SourceFrame = sourceFrame;
            NextSourceFrame = nextSourceFrame;
            Interpolation01 = interpolation01;
        }

        public static SourceAnimationSample FromSelectedOrdinals(ISourceAnimationClip clip,
            int frameOrdinal, int nextFrameOrdinal, float interpolation01 = 0f)
        {
            if (clip == null)
                throw new ArgumentNullException(nameof(clip));
            if (string.IsNullOrWhiteSpace(clip.SequenceName) || clip.SelectedSourceFrames == null
                || clip.SelectedSourceFrames.Count == 0 || clip.SourceFrameCount <= 0)
                throw new ArgumentException("A named clip with its selected-to-authoring frame map is required.", nameof(clip));
            SourceContractGuard.Range(frameOrdinal, 0, clip.SelectedSourceFrames.Count - 1, nameof(frameOrdinal));
            SourceContractGuard.Range(nextFrameOrdinal, 0, clip.SelectedSourceFrames.Count - 1, nameof(nextFrameOrdinal));
            SourceContractGuard.Finite(interpolation01, nameof(interpolation01));
            if (interpolation01 < 0f || interpolation01 > 1f)
                throw new ArgumentOutOfRangeException(nameof(interpolation01));
            int frame = clip.SelectedSourceFrames[frameOrdinal];
            int nextFrame = clip.SelectedSourceFrames[nextFrameOrdinal];
            SourceContractGuard.Range(frame, 0, clip.SourceFrameCount - 1, nameof(clip));
            SourceContractGuard.Range(nextFrame, 0, clip.SourceFrameCount - 1, nameof(clip));
            return new SourceAnimationSample(clip.SequenceName, frame, nextFrame, interpolation01);
        }
    }

    public readonly struct SourceAnimationLayer
    {
        public string ScriptSymbol { get; }
        public long InstanceId { get; }
        public SourceAnimationSample Sample { get; }
        public uint SourceModeBits { get; }
        public ushort JointMask { get; }
        public FieldVector FacingDirection { get; }
        public SourceJointPose TransitionFromPose { get; }
        public float Transition01 { get; }
        public SourceAnimationCodeReturnEvidence CodeReturnEvidence { get; }
        public bool IsSpecified { get { return Sample.IsSpecified && JointMask != 0; } }

        public SourceAnimationLayer(string scriptSymbol, long instanceId, SourceAnimationSample sample,
            uint sourceModeBits = 0, ushort jointMask = SourceTiming.AllJoints,
            SourceJointPose transitionFromPose = null, float transition01 = 1f,
            FieldVector facingDirection = default(FieldVector),
            SourceAnimationCodeReturnEvidence codeReturnEvidence = SourceAnimationCodeReturnEvidence.LegacyUnqualified)
        {
            ScriptSymbol = scriptSymbol;
            InstanceId = instanceId;
            Sample = sample;
            SourceModeBits = sourceModeBits;
            JointMask = jointMask;
            FacingDirection = facingDirection;
            TransitionFromPose = transitionFromPose;
            Transition01 = transition01;
            CodeReturnEvidence = codeReturnEvidence;
        }
    }

    public readonly struct ActorAnimationView
    {
        public long SourceTick { get; }
        public int SourceTicksPerSecond { get; }
        public ActorAction Action { get; }
        public AnimationSelectionEvidence SelectionEvidence { get; }
        public SourceAnimationLayer Primary { get; }
        public SourceAnimationLayer Secondary { get; }
        public SourceVector3 RootOffsetSourceUnits { get; }
        public bool IsSpecified { get { return SourceTicksPerSecond > 0 && Primary.IsSpecified; } }

        public ActorAnimationView(long sourceTick, ActorAction action,
            AnimationSelectionEvidence selectionEvidence, SourceAnimationLayer primary,
            SourceVector3 rootOffsetSourceUnits, SourceAnimationLayer secondary = default(SourceAnimationLayer),
            int sourceTicksPerSecond = SourceTiming.DefaultTicksPerSecond)
        {
            SourceTick = sourceTick;
            SourceTicksPerSecond = sourceTicksPerSecond;
            Action = action;
            SelectionEvidence = selectionEvidence;
            Primary = primary;
            Secondary = secondary;
            RootOffsetSourceUnits = rootOffsetSourceUnits;
        }
    }

    // Pure snapshot geometry: original centered source-world units, joints 0..14.
    public interface ISourceActorGeometry
    {
        SourceVector3 WorldJoint(PlayerView actor, int sourceJointIndex);
    }

    // Optional detached snapshot extension; existing IGameState/Step callers remain valid.
    public interface ISourceGameState : IGameState
    {
        long SourceTick { get; }
        int SourceTicksPerSecond { get; }
        uint PackedGameTime { get; }
        bool ClockActive { get; }
    }
}
