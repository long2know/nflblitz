using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Blitz.Contracts
{
    public sealed class SourcePresentationSettings
    {
        public TeamId DesignatedHomeSide { get; }
        public ReadOnlyCollection<int> StationHeads { get; }
        public int PupBigBall { get; }

        public SourcePresentationSettings(TeamId designatedHomeSide, IReadOnlyList<int> stationHeads, int pupBigBall)
        {
            SourceContractGuard.Team(designatedHomeSide, nameof(designatedHomeSide));
            if (stationHeads == null)
                throw new ArgumentNullException(nameof(stationHeads));
            if (stationHeads.Count != SourceMatchLimits.MaximumHumanStations)
                throw new ArgumentException("Publish all four original team_head station entries.", nameof(stationHeads));
            var copy = new int[stationHeads.Count];
            for (int i = 0; i < copy.Length; i++)
            {
                // teamdata.c has twenty heads; GAME.H uses -1 for an explicitly absent head.
                SourceContractGuard.Range(stationHeads[i], -1, 19, nameof(stationHeads));
                copy[i] = stationHeads[i];
            }
            DesignatedHomeSide = designatedHomeSide;
            StationHeads = Array.AsReadOnly(copy);
            // Preserve the entire source int, not just its truth value at the draw call.
            PupBigBall = pupBigBall;
        }
    }

    public readonly struct SourceActorPresentationView
    {
        private readonly int rawPlayerMode;
        public bool IsSpecified { get; }
        public int RawFlags { get; }
        public int RawPlayerMode { get { return IsSpecified ? rawPlayerMode : -1; } }

        public SourceActorPresentationView(int rawFlags, int rawPlayerMode)
        {
            SourceContractGuard.Range(rawPlayerMode, 0, 10, nameof(rawPlayerMode));
            RawFlags = rawFlags;
            this.rawPlayerMode = rawPlayerMode;
            IsSpecified = true;
        }
    }

    public readonly struct SourceBallPresentationView
    {
        public bool IsSpecified { get; }
        public int RawFlags { get; }

        public SourceBallPresentationView(int rawFlags)
        {
            RawFlags = rawFlags;
            IsSpecified = true;
        }
    }

    // Boundary tags, not numeric ANI_* opcodes or camera mode identifiers.
    public enum SourcePresentationCueKind
    {
        Unspecified, CameraOpcode, ContactCameraSelection, ShakerOpcode,
        DustCallback, SoundCallback, PlayerDownCallback
    }

    public readonly struct SourcePresentationContext
    {
        private readonly long sourceTick;
        private readonly int processOrder;
        private readonly int actorPlayerId;
        private readonly int sourceLine;
        public bool IsSpecified { get; }
        public long SourceTick { get { return IsSpecified ? sourceTick : -1; } }
        // Total within-tick instruction/callback order, including multiple instructions in one process.
        public int ProcessOrder { get { return IsSpecified ? processOrder : -1; } }
        public int ActorPlayerId { get { return IsSpecified ? actorPlayerId : -1; } }
        public string SourcePath { get; }
        public string ExecutingSymbol { get; }
        public int SourceLine { get { return IsSpecified ? sourceLine : -1; } }
        public long? ScriptInstanceId { get; }
        public string OpcodeSymbol { get; }

        public SourcePresentationContext(long sourceTick, int processOrder, int actorPlayerId,
            string sourcePath, string executingSymbol, int sourceLine,
            long? scriptInstanceId = null, string opcodeSymbol = null)
        {
            if (sourceTick < 0)
                throw new ArgumentOutOfRangeException(nameof(sourceTick));
            SourceContractGuard.Range(processOrder, 0, int.MaxValue, nameof(processOrder));
            SourceContractGuard.Range(actorPlayerId, 0, SliceRules.PlayersPerTeam * 2 - 1, nameof(actorPlayerId));
            SourceContractGuard.Range(sourceLine, 1, int.MaxValue, nameof(sourceLine));
            SourcePresentationGuard.Identity(sourcePath, nameof(sourcePath));
            SourcePresentationGuard.Identity(executingSymbol, nameof(executingSymbol));
            if (scriptInstanceId.HasValue && scriptInstanceId.Value < 0)
                throw new ArgumentOutOfRangeException(nameof(scriptInstanceId));
            if (opcodeSymbol != null)
                SourcePresentationGuard.Identity(opcodeSymbol, nameof(opcodeSymbol));
            this.sourceTick = sourceTick;
            this.processOrder = processOrder;
            this.actorPlayerId = actorPlayerId;
            SourcePath = sourcePath;
            ExecutingSymbol = executingSymbol;
            this.sourceLine = sourceLine;
            ScriptInstanceId = scriptInstanceId;
            OpcodeSymbol = opcodeSymbol;
            IsSpecified = true;
        }
    }

    public readonly struct SourceCameraSelection
    {
        private readonly int selectedOrdinal;
        public bool IsSpecified { get; }
        public string TableSymbol { get; }
        public int SelectedOrdinal { get { return IsSpecified ? selectedOrdinal : -1; } }
        public int EntryCount { get; }
        public int? RawRandomValue { get; }
        public string RandomSiteContext { get; }

        public SourceCameraSelection(string tableSymbol, int selectedOrdinal, int entryCount,
            int? rawRandomValue = null, string randomSiteContext = null)
        {
            SourcePresentationGuard.Identity(tableSymbol, nameof(tableSymbol));
            SourceContractGuard.Range(entryCount, 1, int.MaxValue, nameof(entryCount));
            SourceContractGuard.Range(selectedOrdinal, 0, entryCount - 1, nameof(selectedOrdinal));
            if (rawRandomValue.HasValue != (randomSiteContext != null))
                throw new ArgumentException("Publish the actual raw random draw and its site together.", nameof(randomSiteContext));
            if (rawRandomValue.HasValue)
            {
                SourceContractGuard.Range(rawRandomValue.Value, 0, int.MaxValue, nameof(rawRandomValue));
                SourcePresentationGuard.Identity(randomSiteContext, nameof(randomSiteContext));
                if (rawRandomValue.Value % entryCount != selectedOrdinal)
                    throw new ArgumentException("The actual randrng draw must select the published table ordinal.", nameof(rawRandomValue));
            }
            TableSymbol = tableSymbol;
            this.selectedOrdinal = selectedOrdinal;
            EntryCount = entryCount;
            RawRandomValue = rawRandomValue;
            RandomSiteContext = randomSiteContext;
            IsSpecified = true;
        }
    }

    public abstract class SourcePresentationCue
    {
        public SourcePresentationContext Context { get; }
        public SourcePresentationCueKind Kind { get; }
        // Null is unavailable; -1 is an actually observed absence of a carrier.
        public int? CarrierPlayerId { get; }

        private protected SourcePresentationCue(SourcePresentationContext context,
            SourcePresentationCueKind kind, int? carrierPlayerId)
        {
            if (!context.IsSpecified)
                throw new ArgumentException("An actual executing source instruction location is required.", nameof(context));
            if (carrierPlayerId.HasValue)
                SourceContractGuard.Player(carrierPlayerId.Value, nameof(carrierPlayerId));
            Context = context;
            Kind = kind;
            CarrierPlayerId = carrierPlayerId;
        }
    }

    public sealed class SourceCameraCue : SourcePresentationCue
    {
        public SourceCameraSelection Selection { get; }

        public SourceCameraCue(SourcePresentationContext context, SourcePresentationCueKind kind,
            SourceCameraSelection selection, int? carrierPlayerId = null) : base(context, kind, carrierPlayerId)
        {
            if (kind != SourcePresentationCueKind.CameraOpcode && kind != SourcePresentationCueKind.ContactCameraSelection)
                throw new ArgumentOutOfRangeException(nameof(kind));
            if (!selection.IsSpecified)
                throw new ArgumentException("The actual original table and selected ordinal are required.", nameof(selection));
            Selection = selection;
        }
    }

    public sealed class SourceShakerCue : SourcePresentationCue
    {
        public int Amplitude { get; }
        public int TotalTicks { get; }

        public SourceShakerCue(SourcePresentationContext context, int amplitude, int totalTicks, int carrierPlayerId)
            : base(context, SourcePresentationCueKind.ShakerOpcode, carrierPlayerId)
        {
            SourceContractGuard.Range(amplitude, 0, byte.MaxValue, nameof(amplitude));
            SourceContractGuard.Range(totalTicks, 0, int.MaxValue, nameof(totalTicks));
            // ANIM.C assigns amplitude/total before checking whether this actor is the carrier.
            Amplitude = amplitude;
            TotalTicks = totalTicks;
        }
    }

    public sealed class SourceCallbackCue : SourcePresentationCue
    {
        public string FunctionSymbol { get; }
        public int Argument0 { get; }
        public int Argument1 { get; }
        public int Argument2 { get; }

        public SourceCallbackCue(SourcePresentationContext context, SourcePresentationCueKind kind,
            string functionSymbol, int argument0, int argument1, int argument2, int? carrierPlayerId = null)
            : base(context, kind, carrierPlayerId)
        {
            if (kind != SourcePresentationCueKind.DustCallback && kind != SourcePresentationCueKind.SoundCallback &&
                kind != SourcePresentationCueKind.PlayerDownCallback)
                throw new ArgumentOutOfRangeException(nameof(kind));
            SourcePresentationGuard.Identity(functionSymbol, nameof(functionSymbol));
            FunctionSymbol = functionSymbol;
            Argument0 = argument0;
            Argument1 = argument1;
            Argument2 = argument2;
        }
    }

    public sealed class SourcePresentationCueBatch
    {
        public ReadOnlyCollection<SourcePresentationCue> Cues { get; }

        public SourcePresentationCueBatch(IReadOnlyList<SourcePresentationCue> cues)
        {
            if (cues == null)
                throw new ArgumentNullException(nameof(cues));
            var copy = new SourcePresentationCue[cues.Count];
            for (int i = 0; i < copy.Length; i++)
            {
                SourcePresentationCue cue = cues[i];
                if (cue == null)
                    throw new ArgumentException("Publish only specified source presentation cues.", nameof(cues));
                if (i > 0)
                {
                    SourcePresentationContext previous = copy[i - 1].Context;
                    SourcePresentationContext current = cue.Context;
                    if (current.SourceTick < previous.SourceTick ||
                        current.SourceTick == previous.SourceTick && current.ProcessOrder <= previous.ProcessOrder)
                        throw new ArgumentException("Source ticks and within-tick instruction order must increase.", nameof(cues));
                }
                copy[i] = cue;
            }
            Cues = Array.AsReadOnly(copy);
        }
    }

    public interface ISourcePresentationState : ISourceMatchState
    {
        SourcePresentationSettings Presentation { get; }
    }

    public interface ISourcePresentationSimulation : ISourceMatchSimulation
    {
        SourcePresentationCueBatch PresentationCues { get; }
    }

    internal static class SourcePresentationGuard
    {
        internal static void Identity(string value, string name)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("An actual source identity is required.", name);
        }
    }
}
