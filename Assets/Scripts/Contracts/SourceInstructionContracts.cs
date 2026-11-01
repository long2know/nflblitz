using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Blitz.Contracts
{
    public interface ISourceAnyButtonInputs
    {
        int ReadPlayerSwitches();
        int ReadShowPlyselIdiotBox();
    }

    public interface ISourceInstructionInputs : ISourceAnyButtonInputs
    {
        int ReadTsec();
        int ReadIsLowResolution();
    }

    public enum SourceInstructionPage { Rules, Receivers, Audibles, Hints }
    public enum SourceInstructionFont { Bast23, Bast18, Bast10Low, Bast10 }
    public enum SourceInstructionColor { LightRed, White, LightYellow }
    public enum SourceInstructionJustification { Center, Left }
    public enum SourceInstructionStage { PendingEntry, EntryDelay, PageDelay, Waiting, FinalDelay, Fallthrough, Faulted }

    // These are source expressions, not measured pixels. The consumer must supply
    // SPRITE_HRES/SPRITE_VRES and their evaluation semantics; no raster is invented.
    public readonly struct SourceInstructionPosition
    {
        public int HorizontalOffset { get; }
        public int VerticalOffset { get; }
        public float Depth { get; }
        public bool TruncateFloatHalfAfterOffset { get; }
        public SourceInstructionPosition(int horizontalOffset, int verticalOffset, float depth,
            bool truncateFloatHalfAfterOffset)
        {
            HorizontalOffset = horizontalOffset; VerticalOffset = verticalOffset;
            Depth = depth; TruncateFloatHalfAfterOffset = truncateFloatHalfAfterOffset;
        }
    }

    public readonly struct SourceInstructionTextRequest
    {
        public SourceInstructionPage Page { get; }
        public int Line { get; }
        public SourceInstructionJustification Justification { get; }
        public string NativeSecondArgumentSymbols
        {
            get
            {
                return Justification == SourceInstructionJustification.Left
                    ? "HJUST_LEFT|VJUST_CENTER" : "HJUST_CENTER|VJUST_CENTER";
            }
        }
        public SourceInstructionTextRequest(SourceInstructionPage page, int line,
            SourceInstructionJustification justification)
        {
            if (page != SourceInstructionPage.Rules && page != SourceInstructionPage.Receivers
                && page != SourceInstructionPage.Audibles && page != SourceInstructionPage.Hints)
                throw new ArgumentOutOfRangeException(nameof(page));
            if (line < 0 || line > 6) throw new ArgumentOutOfRangeException(nameof(line));
            if (justification != SourceInstructionJustification.Center && justification != SourceInstructionJustification.Left)
                throw new ArgumentOutOfRangeException(nameof(justification));
            Page = page; Line = line; Justification = justification;
        }
    }

    public sealed class SourceInstructionSound
    {
        public bool Direct { get; }
        public string BankSymbol { get; }
        public string SoundSymbol { get; }
        public string VolumeSymbol { get; }
        public int Pan { get { return 127; } }
        public string PrioritySymbol { get; }
        public int? RawPriority { get; }
        private SourceInstructionSound(bool direct, string bank, string sound, string volume,
            string priority, int? rawPriority)
        {
            Direct = direct; BankSymbol = bank; SoundSymbol = sound; VolumeSymbol = volume;
            PrioritySymbol = priority; RawPriority = rawPriority;
        }
        public static readonly SourceInstructionSound Tick =
            new SourceInstructionSound(true, null, "tick_sndn", "VOLUME1", null, 1);
        public static readonly SourceInstructionSound PageAdvance =
            new SourceInstructionSound(false, "plyr_bnk_str", "RICOCHET_SND", "VOLUME2", "PRIORITY_LVL5", null);
    }

    public interface ISourceInstructionPlate { }
    public interface ISourceInstructionText { }
    public interface ISourceInstructionFlasher { }

    // An admitted provider performs the external call attempts, owns the opaque
    // returns, and resolves resource/text symbols. No null/default renderer exists.
    public interface ISourceInstructionEffects
    {
        void WriteIdiotBoxesShown(int value);
        ISourceInstructionPlate BeginPlate(string imageSymbol, string textureSymbol, SourceInstructionPosition position);
        void SetPlateWidthScale(ISourceInstructionPlate plate, float scale);
        void SetPlateHeightScale(ISourceInstructionPlate plate, float scale);
        void SetPlateId(ISourceInstructionPlate plate, string objectIdSymbol);
        void GeneratePlateVertices(ISourceInstructionPlate plate);
        void SetTextFont(SourceInstructionFont font);
        void SetStringId(int id);
        void SetTextPosition(SourceInstructionPosition position);
        void SetTextColor(SourceInstructionColor color);
        ISourceInstructionText Print(SourceInstructionTextRequest request);
        void StoreMessage(int index, ISourceInstructionText text);
        ISourceInstructionFlasher CreateTextFlasher(string name, int processId,
            ISourceInstructionText title, ISourceInstructionText second, int thirdArgument, int fourthArgument);
        void CreatePlateFlasher(string name, int processId, ISourceInstructionPlate plate,
            int flashes, int delay, int fourthArgument);
        void PlaySound(SourceInstructionSound sound);
        void DeleteStrings(int id, int mask);
        void KillTextFlasher(ISourceInstructionFlasher flasher, int signal);
        void DeletePlate(ISourceInstructionPlate plate);
    }

    public sealed class SourceInstructionProcessIds
    {
        public int Instructions { get; }
        public int PostPlayer { get; }
        public int Camera { get; }
        public int Message { get; }
        public SourceInstructionProcessIds(int instructions, int postPlayer, int camera, int message)
        {
            Instructions = instructions; PostPlayer = postPlayer; Camera = camera; Message = message;
        }
    }

    public sealed class SourceInstructionInvocation
    {
        public int AllAbcdMask { get; }
        public SourceInstructionProcessIds ProcessIds { get; }
        public ISourceInstructionInputs Inputs { get; }
        public ISourceInstructionEffects Effects { get; }
        public SourceInstructionInvocation(int allAbcdMask, SourceInstructionProcessIds processIds,
            ISourceInstructionInputs inputs, ISourceInstructionEffects effects)
        {
            if (allAbcdMask == 0) throw new ArgumentOutOfRangeException(nameof(allAbcdMask));
            AllAbcdMask = allAbcdMask;
            ProcessIds = processIds ?? throw new ArgumentNullException(nameof(processIds));
            Inputs = inputs ?? throw new ArgumentNullException(nameof(inputs));
            Effects = effects ?? throw new ArgumentNullException(nameof(effects));
        }
    }

    // The same object must implement this state and the invocation's effects,
    // so initialization, producer entry and instruction writes share one flag.
    public interface ISourceInstructionAdmissionState
    {
        int ReadIdiotBoxesShown();
        void WriteIdiotBoxesShown(int value);
        int ReadPlayerStatus();
        int ReadGamesPlayed(int slot);
    }

    public enum SourceKickoffInstructionStepKind { Sleep = 1, ExternalFallthrough = 2 }

    public readonly struct SourceKickoffInstructionStep
    {
        public SourceKickoffInstructionStepKind Kind { get; }
        public int SleepTicks { get; }
        private SourceKickoffInstructionStep(SourceKickoffInstructionStepKind kind, int ticks)
        { Kind = kind; SleepTicks = ticks; }
        public static SourceKickoffInstructionStep Sleep(int ticks)
        {
            if (ticks < 1 || ticks > 0x7fff) throw new ArgumentOutOfRangeException(nameof(ticks));
            return new SourceKickoffInstructionStep(SourceKickoffInstructionStepKind.Sleep, ticks);
        }
        public static SourceKickoffInstructionStep ExternalFallthrough()
        { return new SourceKickoffInstructionStep(SourceKickoffInstructionStepKind.ExternalFallthrough, 0); }
    }

    // Compatibility boundary for callers that explicitly supply the entire
    // parent suffix. Complete-coordinator bindings instead require child bodies.
    public interface ISourceKickoffInstructionContinuation
    {
        SourceKickoffInstructionStep Advance();
    }

    public interface ISourceKickoffInstructionContinuationFactory
    {
        ISourceKickoffInstructionContinuation Create();
    }

    public interface ISourceKickoffInstructionCoordinatorInputs
    {
        int ReadFourPlayerVersion();
        int ReadPlayerControl(int slot);
    }

    public interface ISourceKickoffInstructionPlayerButtons
    {
        int ReadCurrentButtons(int player);
        void WriteCurrentButtons(int player, int value);
        int ReadOldButtons(int player);
        void WriteOldButtons(int player, int value);
    }

    public sealed class SourceKickoffInstructionChildInvocation
    {
        public long Serial { get; }
        public int ProcessId { get; }
        public string Name { get { return "idiots"; } }
        public int Argument0 { get; }
        public int Argument1 { get; }
        public int Argument2 { get; }
        public int Argument3 { get; }
        internal SourceKickoffInstructionChildInvocation(long serial, int processId,
            int argument0, int argument1, int argument2, int argument3)
        {
            Serial = serial; ProcessId = processId;
            Argument0 = argument0; Argument1 = argument1;
            Argument2 = argument2; Argument3 = argument3;
        }
    }

    public enum SourceKickoffInstructionChildStepKind
    { Sleep = 1, ExternalFallthrough = 2, ExternalRetirement = 3, NativeDie = 4, SourceFallthrough = 5 }

    public readonly struct SourceKickoffInstructionChildStep
    {
        public SourceKickoffInstructionChildStepKind Kind { get; }
        public int SleepTicks { get; }
        public int DieSignal { get; }
        public SourceKickoffInstructionChildReturnReason ReturnReason { get; }
        private SourceKickoffInstructionChildStep(SourceKickoffInstructionChildStepKind kind, int ticks)
            : this(kind, ticks, 0, default(SourceKickoffInstructionChildReturnReason)) { }
        private SourceKickoffInstructionChildStep(SourceKickoffInstructionChildStepKind kind, int ticks,
            int dieSignal, SourceKickoffInstructionChildReturnReason returnReason)
        { Kind = kind; SleepTicks = ticks; DieSignal = dieSignal; ReturnReason = returnReason; }
        public static SourceKickoffInstructionChildStep Sleep(int ticks)
        {
            if (ticks < 1 || ticks > 0x7fff) throw new ArgumentOutOfRangeException(nameof(ticks));
            return new SourceKickoffInstructionChildStep(SourceKickoffInstructionChildStepKind.Sleep, ticks);
        }
        public static SourceKickoffInstructionChildStep ExternalFallthrough()
        { return new SourceKickoffInstructionChildStep(SourceKickoffInstructionChildStepKind.ExternalFallthrough, 0); }
        // Only an independently supplied child body may request removal. Returning
        // from that body alone must not silently turn into native die() semantics.
        public static SourceKickoffInstructionChildStep ExternalRetirement()
        { return new SourceKickoffInstructionChildStep(SourceKickoffInstructionChildStepKind.ExternalRetirement, 0); }
        public static SourceKickoffInstructionChildStep NativeDie(int signal)
        { return new SourceKickoffInstructionChildStep(SourceKickoffInstructionChildStepKind.NativeDie, 0, signal, default(SourceKickoffInstructionChildReturnReason)); }
        public static SourceKickoffInstructionChildStep SourceFallthrough(SourceKickoffInstructionChildReturnReason reason)
        {
            if (reason != SourceKickoffInstructionChildReturnReason.DbgKoCompileTimeReturn
                && reason != SourceKickoffInstructionChildReturnReason.NormalBodyEnd)
                throw new ArgumentOutOfRangeException(nameof(reason));
            return new SourceKickoffInstructionChildStep(SourceKickoffInstructionChildStepKind.SourceFallthrough, 0, 0, reason);
        }
    }

    public interface ISourceKickoffInstructionChildContinuation
    {
        SourceKickoffInstructionChildStep Advance();
    }

    public interface ISourceKickoffInstructionChildContinuationFactory
    {
        ISourceKickoffInstructionChildContinuation Create(SourceKickoffInstructionChildInvocation invocation);
    }

    // These explicit operands/providers are not inferred from local actor count,
    // native headers, an SDK or a resource profile. Source children are an explicit opt-in.
    public sealed class SourceKickoffInstructionCoordinator
    {
        public int ChildProcessId { get; }
        public int MaximumPlayers { get; }
        public int ButtonAMask { get; }
        public int ButtonBMask { get; }
        public ISourceKickoffInstructionCoordinatorInputs Inputs { get; }
        public ISourceKickoffInstructionPlayerButtons Buttons { get; }
        public ISourceKickoffInstructionChildContinuationFactory ChildContinuations { get; }
        public SourceKickoffInstructionChildBinding SourceChild { get; }
        public SourceKickoffInstructionCoordinator(int childProcessId, int maximumPlayers,
            int buttonAMask, int buttonBMask, ISourceKickoffInstructionCoordinatorInputs inputs,
            ISourceKickoffInstructionPlayerButtons buttons,
            ISourceKickoffInstructionChildContinuationFactory childContinuations)
            : this(childProcessId, maximumPlayers, buttonAMask, buttonBMask, inputs, buttons, childContinuations, null) { }

        public static SourceKickoffInstructionCoordinator WithSourceChild(int childProcessId, int maximumPlayers,
            int buttonAMask, int buttonBMask, ISourceKickoffInstructionChildInputs inputs,
            ISourceKickoffInstructionPlayerButtons buttons, SourceKickoffInstructionChildBinding child)
        {
            if (child == null) throw new ArgumentNullException(nameof(child));
            return new SourceKickoffInstructionCoordinator(childProcessId, maximumPlayers,
                buttonAMask, buttonBMask, inputs, buttons, null, child);
        }

        private SourceKickoffInstructionCoordinator(int childProcessId, int maximumPlayers,
            int buttonAMask, int buttonBMask, ISourceKickoffInstructionCoordinatorInputs inputs,
            ISourceKickoffInstructionPlayerButtons buttons,
            ISourceKickoffInstructionChildContinuationFactory childContinuations,
            SourceKickoffInstructionChildBinding sourceChild)
        {
            // This portable Int32 participation domain does not establish MAX_PLYRS.
            if (maximumPlayers < 1 || maximumPlayers > 31)
                throw new ArgumentOutOfRangeException(nameof(maximumPlayers));
            if (buttonAMask == 0) throw new ArgumentOutOfRangeException(nameof(buttonAMask));
            if (buttonBMask == 0) throw new ArgumentOutOfRangeException(nameof(buttonBMask));
            ChildProcessId = childProcessId; MaximumPlayers = maximumPlayers;
            ButtonAMask = buttonAMask; ButtonBMask = buttonBMask;
            Inputs = inputs ?? throw new ArgumentNullException(nameof(inputs));
            Buttons = buttons ?? throw new ArgumentNullException(nameof(buttons));
            if (sourceChild == null && childContinuations == null)
                throw new ArgumentNullException(nameof(childContinuations));
            ChildContinuations = childContinuations; SourceChild = sourceChild;
        }
    }

    public sealed class SourceInstructionAdmissionBinding
    {
        public SourceInstructionInvocation Invocation { get; }
        public ISourceInstructionAdmissionState State { get; }
        public ISourceKickoffInstructionContinuationFactory KickoffContinuationFactory { get; }
        public SourceKickoffInstructionCoordinator KickoffCoordinator { get; }
        public SourceInstructionAdmissionBinding(SourceInstructionInvocation invocation,
            ISourceInstructionAdmissionState state,
            ISourceKickoffInstructionContinuationFactory kickoffContinuationFactory)
            : this(invocation, state, kickoffContinuationFactory, null) { }

        public static SourceInstructionAdmissionBinding WithKickoffCoordinator(
            SourceInstructionInvocation invocation, ISourceInstructionAdmissionState state,
            SourceKickoffInstructionCoordinator coordinator)
        {
            if (coordinator == null) throw new ArgumentNullException(nameof(coordinator));
            return new SourceInstructionAdmissionBinding(invocation, state, null, coordinator);
        }

        private SourceInstructionAdmissionBinding(SourceInstructionInvocation invocation,
            ISourceInstructionAdmissionState state,
            ISourceKickoffInstructionContinuationFactory kickoffContinuationFactory,
            SourceKickoffInstructionCoordinator coordinator)
        {
            Invocation = invocation ?? throw new ArgumentNullException(nameof(invocation));
            State = state ?? throw new ArgumentNullException(nameof(state));
            if (!ReferenceEquals(invocation.Effects, state))
                throw new ArgumentException("Admission reads/writes and instruction effects require the SAME coherent flag owner.", nameof(state));
            if (coordinator == null && kickoffContinuationFactory == null)
                throw new ArgumentNullException(nameof(kickoffContinuationFactory));
            KickoffContinuationFactory = kickoffContinuationFactory;
            KickoffCoordinator = coordinator;
        }
    }

    public sealed class SourceAnyButtonWaitSnapshot
    {
        public int MinTicks { get; }
        public int WaitTicks { get; }
        public int DelayRemaining { get; }
        public int PollsRemaining { get; }
        public bool Completed { get; }
        public int? Result { get; }
        public SourceAnyButtonWaitSnapshot(int minTicks, int waitTicks, int delayRemaining,
            int pollsRemaining, bool completed, int? result)
        {
            MinTicks = minTicks; WaitTicks = waitTicks; DelayRemaining = delayRemaining;
            PollsRemaining = pollsRemaining; Completed = completed; Result = result;
        }
    }

    public sealed class SourceInstructionSnapshot
    {
        public SourceProcessView Process { get; }
        public SourceInstructionStage Stage { get; }
        public SourceInstructionPage? Page { get; }
        public int? IdiotBoxesShown { get; }
        public SourceAnyButtonWaitSnapshot Wait { get; }
        public ReadOnlyCollection<int> WaitResults { get; }
        public bool ConditionalProgramCompleted { get { return Stage == SourceInstructionStage.Fallthrough; } }
        public SourceInstructionSnapshot(SourceProcessView process, SourceInstructionStage stage,
            SourceInstructionPage? page, int? idiotBoxesShown, SourceAnyButtonWaitSnapshot wait,
            IEnumerable<int> waitResults)
        {
            if (waitResults == null) throw new ArgumentNullException(nameof(waitResults));
            Process = process; Stage = stage; Page = page; IdiotBoxesShown = idiotBoxesShown; Wait = wait;
            WaitResults = new List<int>(waitResults).AsReadOnly();
        }
    }
}
