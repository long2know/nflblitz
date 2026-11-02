using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Blitz.Contracts
{
    public enum SourcePlaybookKind { Offense, Defense }
    public enum SourcePlayPool
    {
        Punt = 0, FakePunt = 1, FieldGoal = 2, ShortOffense = 3, LongOffense = 4,
        PuntReturn = 5, BlockFieldGoal = 6, GoalLineDefense = 7, BlitzDefense = 8,
        ShortDefense = 9, LongDefense = 10, ShortDefenseCustom = 11, LongDefenseCustom = 12
    }

    [Flags]
    public enum SourceButtons { None = 0, A = 1, B = 2, C = 4 }

    [Flags]
    public enum SourceDigitalDirections { None = 0, Up = 1, Down = 2, Left = 4, Right = 8 }

    // Physical cabinet slots are independent of participation and actor control.
    // These are quantized current values, not invented numeric Goose switch masks.
    public sealed class SourcePhysicalSwitchFrame
    {
        public ReadOnlyCollection<SourceStationInput> Stations { get; }
        public SourcePhysicalSwitchFrame(IReadOnlyList<SourceStationInput> stations)
        {
            if (stations == null) throw new ArgumentNullException(nameof(stations));
            if (stations.Count != 4)
                throw new ArgumentException("Sample all four physical cabinet stations.", nameof(stations));
            var copy = new SourceStationInput[4];
            foreach (SourceStationInput input in stations)
            {
                if (!input.IsSpecified || copy[input.StationId].IsSpecified || !input.DigitalCurrent.HasValue
                    || input.Command.IsSpecified || input.Pressed != 0 || input.Released != 0)
                    throw new ArgumentException("Physical samples require four unique quantized current stations, without commands or edges.", nameof(stations));
                copy[input.StationId] = input;
            }
            Stations = Array.AsReadOnly(copy);
        }
        public SourceStationInput Station(int physicalStationId)
        {
            SourceContractGuard.Station(physicalStationId, false, nameof(physicalStationId));
            return Stations[physicalStationId];
        }
        public bool IsNeutral
        {
            get
            {
                foreach (SourceStationInput input in Stations)
                    if (input.Held != 0 || input.StickX != 0 || input.StickZ != 0) return false;
                return true;
            }
        }
        internal bool SameCurrent(SourcePhysicalSwitchFrame other)
        {
            if (other == null) return false;
            for (int station = 0; station < 4; station++)
                if (!SameCurrent(Station(station), other.Station(station))) return false;
            return true;
        }
        internal static bool SameCurrent(SourceStationInput a, SourceStationInput b)
        {
            return a.StationId == b.StationId && a.StickX == b.StickX && a.StickZ == b.StickZ
                && a.Held == b.Held && a.DigitalCurrent == b.DigitalCurrent;
        }
        internal static SourcePhysicalSwitchFrame Neutral()
        {
            var values = new SourceStationInput[4];
            for (int station = 0; station < 4; station++)
                values[station] = new SourceStationInput(station, digitalCurrent: SourceDigitalDirections.None);
            return new SourcePhysicalSwitchFrame(values);
        }
    }

    public sealed class SourceConsumedPhysicalSwitchScan
    {
        private readonly SourcePhysicalSwitchFrame prior1, prior2;
        public long SourceTick { get; }
        public long InputEpoch { get; }
        public SourcePhysicalSwitchFrame PhysicalSwitches { get; }
        internal SourceConsumedPhysicalSwitchScan(long sourceTick, long inputEpoch, SourcePhysicalSwitchFrame current,
            SourcePhysicalSwitchFrame prior1, SourcePhysicalSwitchFrame prior2)
        {
            if (sourceTick < 1) throw new ArgumentOutOfRangeException(nameof(sourceTick));
            if (inputEpoch < 1) throw new ArgumentOutOfRangeException(nameof(inputEpoch));
            SourceTick = sourceTick; InputEpoch = inputEpoch;
            PhysicalSwitches = current ?? throw new ArgumentNullException(nameof(current));
            this.prior1 = prior1 ?? throw new ArgumentNullException(nameof(prior1));
            this.prior2 = prior2 ?? throw new ArgumentNullException(nameof(prior2));
        }
        // switch.c's close/open debounce uses the previous TWO source scans.
        // It must not be reconstructed from merged host Pressed/Released edges.
        public SourceButtons ClosedButtons(int physicalStationId)
        {
            return PhysicalSwitches.Station(physicalStationId).Held & ~prior1.Station(physicalStationId).Held
                & ~prior2.Station(physicalStationId).Held;
        }
        public SourceButtons OpenedButtons(int physicalStationId)
        {
            return prior1.Station(physicalStationId).Held & prior2.Station(physicalStationId).Held
                & ~PhysicalSwitches.Station(physicalStationId).Held;
        }
    }

    public readonly struct SourceDigitalSwitchScan
    {
        private readonly int stationId;
        public bool IsSpecified { get; }
        public int StationId { get { return IsSpecified ? stationId : -1; } }
        public long SourceTick { get; }
        public SourceDigitalDirections Current { get; }
        public SourceDigitalDirections Prior1 { get; }
        public SourceDigitalDirections Prior2 { get; }
        public SourceDigitalDirections Close { get; }

        // Masks are already decoded through the authenticated 49-to-8 table.
        // Priors are the last two SOURCE scans, not host captures or menu wakes.
        public SourceDigitalSwitchScan(int stationId, long sourceTick, SourceDigitalDirections current,
            SourceDigitalDirections prior1, SourceDigitalDirections prior2)
        {
            SourceContractGuard.Station(stationId, false, nameof(stationId));
            if (sourceTick < 0) throw new ArgumentOutOfRangeException(nameof(sourceTick));
            RequireMask(current, nameof(current));
            RequireMask(prior1, nameof(prior1));
            RequireMask(prior2, nameof(prior2));
            this.stationId = stationId;
            SourceTick = sourceTick;
            Current = current;
            Prior1 = prior1;
            Prior2 = prior2;
            Close = current & ~prior1 & ~prior2;
            IsSpecified = true;
        }

        internal static void RequireMask(SourceDigitalDirections mask, string name)
        {
            if (((int)mask & ~15) != 0) throw new ArgumentOutOfRangeException(name);
        }
    }

    public enum SourceAction
    {
        None, SelectPlay, Snap, Pass, Tackle, SwitchPlayer, Continue, SelectPat,
        SelectKickoff, Kick, Punt, Lateral, Audible, Spin, Juke, Hurdle, Dive,
        Jump, Swat, Catch, StiffArm, StartRandomPlaySelection
    }

    public sealed class SourceSavedPlayOrigin
    {
        private readonly SourceSavedRecordSession session;
        public int StationId { get; }
        public int SlotIndex { get; }

        internal SourceSavedPlayOrigin(SourceSavedRecordSession session, int stationId, int slotIndex)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));
            if (session.ResolveCustom(stationId, slotIndex, false).Kind != SourceSavedCustomChoiceKind.Existing)
                throw new ArgumentException("A saved play origin requires an actual existing station slot.");
            this.session = session;
            StationId = stationId;
            SlotIndex = slotIndex;
        }

        internal bool BelongsTo(SourceSavedRecordSession actual) { return ReferenceEquals(session, actual); }
    }

    public readonly struct SourcePlaySelection
    {
        private readonly int slotIndex;
        private readonly int pageIndex;
        private readonly int poolIndex;
        public bool IsSpecified { get { return !string.IsNullOrEmpty(PlaySymbol); } }
        public string PlaySymbol { get; }
        public SourcePlaybookKind Playbook { get; }
        public int SlotIndex { get { return IsSpecified ? slotIndex : -1; } }
        public int PageIndex { get { return IsSpecified ? pageIndex : -1; } }
        public SourcePlayPool? Pool { get; }
        public int PoolIndex { get { return IsSpecified ? poolIndex : -1; } }
        public SourceSavedPlayOrigin SavedOrigin { get; }

        public SourcePlaySelection(string playSymbol, SourcePlaybookKind playbook,
            int slotIndex = -1, int pageIndex = -1, SourcePlayPool? pool = null, int poolIndex = -1)
        {
            if (string.IsNullOrWhiteSpace(playSymbol))
                throw new ArgumentException("An original play symbol is required.", nameof(playSymbol));
            SourceContractGuard.Range((int)playbook, 0, 1, nameof(playbook));
            SourceContractGuard.Range(slotIndex, -1, int.MaxValue, nameof(slotIndex));
            SourceContractGuard.Range(pageIndex, -1, int.MaxValue, nameof(pageIndex));
            SourceContractGuard.Range(poolIndex, -1, int.MaxValue, nameof(poolIndex));
            if (pool.HasValue)
                SourceContractGuard.Range((int)pool.Value, 0, 12, nameof(pool));
            if (pool.HasValue != (poolIndex >= 0))
                throw new ArgumentException("A weighted pool and its original ordinal must be supplied together.", nameof(poolIndex));
            PlaySymbol = playSymbol;
            Playbook = playbook;
            this.slotIndex = slotIndex;
            this.pageIndex = pageIndex;
            Pool = pool;
            this.poolIndex = poolIndex;
            SavedOrigin = null;
        }

        internal SourcePlaySelection(SourceSavedPlayOrigin origin)
            : this(origin == null ? null : "custom_p" + origin.StationId + "_s" + origin.SlotIndex,
                SourcePlaybookKind.Offense, origin == null ? -1 : origin.SlotIndex, 4)
        { SavedOrigin = origin; }
    }

    public readonly struct SourceCommand
    {
        private readonly int targetPlayerId;
        private readonly int audibleSlot;
        public bool IsSpecified { get; }
        public SourceAction Action { get; }
        public SourcePlaySelection Play { get; }
        public int TargetPlayerId { get { return IsSpecified ? targetPlayerId : -1; } }
        public SourcePatOption PatChoice { get; }
        public SourceKickoffType? KickoffChoice { get; }
        public int AudibleSlot { get { return IsSpecified ? audibleSlot : -1; } }
        public bool? OffenseFlipped { get; }

        public SourceCommand(SourceAction action, SourcePlaySelection play = default(SourcePlaySelection),
            int targetPlayerId = -1, SourcePatOption patChoice = SourcePatOption.Unselected,
            SourceKickoffType? kickoffChoice = null, int audibleSlot = -1, bool? offenseFlipped = null)
        {
            SourceContractGuard.Range((int)action, 0, 21, nameof(action));
            SourceContractGuard.Player(targetPlayerId, nameof(targetPlayerId));
            SourceContractGuard.Range((int)patChoice, 0, 2, nameof(patChoice));
            if (kickoffChoice.HasValue)
                SourceContractGuard.Range((int)kickoffChoice.Value, 0, 2, nameof(kickoffChoice));
            SourceContractGuard.Range(audibleSlot, -1, int.MaxValue, nameof(audibleSlot));
            Action = action;
            Play = play;
            this.targetPlayerId = targetPlayerId;
            PatChoice = patChoice;
            KickoffChoice = kickoffChoice;
            this.audibleSlot = audibleSlot;
            OffenseFlipped = offenseFlipped;
            IsSpecified = true;
        }
    }

    public readonly struct SourceStationInput
    {
        private readonly int stationId;
        public bool IsSpecified { get; }
        public int StationId { get { return IsSpecified ? stationId : -1; } }
        public int StickX { get; }
        public int StickZ { get; }
        public SourceButtons Held { get; }
        public SourceButtons Pressed { get; }
        public SourceButtons Released { get; }
        public SourceCommand Command { get; }
        public SourceDigitalDirections? DigitalCurrent { get; }

        public SourceStationInput(int stationId, int stickX = 0, int stickZ = 0,
            SourceButtons held = SourceButtons.None, SourceButtons pressed = SourceButtons.None,
            SourceButtons released = SourceButtons.None, SourceCommand command = default(SourceCommand),
            SourceDigitalDirections? digitalCurrent = null)
        {
            SourceContractGuard.Station(stationId, false, nameof(stationId));
            SourceContractGuard.Range(stickX, -3, 3, nameof(stickX));
            SourceContractGuard.Range(stickZ, -3, 3, nameof(stickZ));
            SourceContractGuard.Buttons(held, nameof(held));
            SourceContractGuard.Buttons(pressed, nameof(pressed));
            SourceContractGuard.Buttons(released, nameof(released));
            if (digitalCurrent.HasValue)
                SourceDigitalSwitchScan.RequireMask(digitalCurrent.Value, nameof(digitalCurrent));
            this.stationId = stationId;
            StickX = stickX;
            StickZ = stickZ;
            Held = held;
            Pressed = pressed;
            Released = released;
            Command = command;
            DigitalCurrent = digitalCurrent;
            IsSpecified = true;
        }
    }

    public enum SourceCabinetSelectionSound { Moved, Edge, PageChanged, Flipped, Hidden, Selected, RandomArmed }

    public interface ISourceCabinetSelectionSoundProducer
    {
        SourceProcessSoundCall SubmitSourceCabinetSelectionSound(long sourceTick, int stationId, TeamId side,
            SourceCabinetSelectionSound sound);
    }

    public interface ISourceCabinetSoundBank
    {
        string SourceCursorSoundBank { get; }
    }

    public sealed class SourceInputFrame
    {
        public ReadOnlyCollection<SourceStationInput> Stations { get; }
        public SourcePhysicalSwitchFrame PhysicalSwitches { get; }
        public ISourceCabinetSelectionSoundProducer SelectionSoundProducer { get; }
        public bool PlaySelectionContinuationBlocked { get; }

        private SourceInputFrame(SourceInputFrame frame, ISourceCabinetSelectionSoundProducer producer, bool selectionBlocked)
        {
            Stations = frame.Stations; PhysicalSwitches = frame.PhysicalSwitches;
            SelectionSoundProducer = producer; PlaySelectionContinuationBlocked = selectionBlocked;
        }

        internal SourceInputFrame WithSelectionSoundProducer(ISourceCabinetSelectionSoundProducer producer)
        { return new SourceInputFrame(this, producer, PlaySelectionContinuationBlocked); }

        internal SourceInputFrame WithPlaySelectionContinuationBlocked()
        { return new SourceInputFrame(this, SelectionSoundProducer, true); }

        public SourceInputFrame(IReadOnlyList<SourceStationInput> stations,
            SourcePhysicalSwitchFrame physicalSwitches)
        {
            if (stations == null)
                throw new ArgumentNullException(nameof(stations));
            if (stations.Count > SourceMatchLimits.MaximumHumanStations)
                throw new ArgumentException("At most four source stations can submit input.", nameof(stations));
            var copy = new SourceStationInput[stations.Count];
            var seen = new bool[SourceMatchLimits.MaximumHumanStations];
            for (int i = 0; i < copy.Length; i++)
            {
                SourceStationInput station = stations[i];
                if (!station.IsSpecified || seen[station.StationId])
                    throw new ArgumentException("Source station inputs must be specified and unique.", nameof(stations));
                seen[station.StationId] = true;
                copy[i] = station;
            }
            Stations = Array.AsReadOnly(copy);
            PhysicalSwitches = physicalSwitches;
        }

        public SourceInputFrame(IReadOnlyList<SourceStationInput> stations) : this(stations, null) { }
    }

    public readonly struct SourceStationView
    {
        private readonly int stationId;
        private readonly int controlledPlayerId;
        public bool IsSpecified { get; }
        public int StationId { get { return IsSpecified ? stationId : -1; } }
        public int ControlledPlayerId { get { return IsSpecified ? controlledPlayerId : -1; } }
        public float Turbo01 { get; }

        public SourceStationView(int stationId, int controlledPlayerId, float turbo01)
        {
            SourceContractGuard.Station(stationId, false, nameof(stationId));
            SourceContractGuard.Player(controlledPlayerId, nameof(controlledPlayerId));
            SourceContractGuard.Finite(turbo01, nameof(turbo01));
            if (turbo01 < 0f || turbo01 > 1f)
                throw new ArgumentOutOfRangeException(nameof(turbo01));
            this.stationId = stationId;
            this.controlledPlayerId = controlledPlayerId;
            Turbo01 = turbo01;
            IsSpecified = true;
        }
    }

    public readonly struct SourceActorView
    {
        private readonly int controllerStationId;
        public bool IsSpecified { get; }
        public int ControllerStationId { get { return IsSpecified ? controllerStationId : -1; } }
        public SourceRosterIdentity Roster { get; }
        public int NoCatchSourceTicks { get; }
        public SourceActorPresentationView Presentation { get; }
        public SourceActorContactView Contact { get; }
        public int? Handicap { get; }
        public int? RawHandicap { get; }

        public SourceActorView(int controllerStationId, SourceRosterIdentity roster = default(SourceRosterIdentity),
            int noCatchSourceTicks = 0, SourceActorPresentationView presentation = default(SourceActorPresentationView),
            SourceActorContactView contact = default(SourceActorContactView), int? handicap = null, int? rawHandicap = null)
        {
            SourceContractGuard.Station(controllerStationId, true, nameof(controllerStationId));
            SourceContractGuard.Range(noCatchSourceTicks, 0, int.MaxValue, nameof(noCatchSourceTicks));
            if (handicap.HasValue) SourceContractGuard.Range(handicap.Value, 0, 11, nameof(handicap));
            this.controllerStationId = controllerStationId;
            Roster = roster;
            NoCatchSourceTicks = noCatchSourceTicks;
            Presentation = presentation;
            Contact = contact;
            Handicap = handicap;
            RawHandicap = rawHandicap ?? handicap;
            IsSpecified = true;
        }
    }

    public readonly struct SourceBallControlView
    {
        private readonly int controllerStationId;
        private readonly int controllerPlayerId;
        public bool IsSpecified { get; }
        public int ControllerStationId { get { return IsSpecified ? controllerStationId : -1; } }
        public int ControllerPlayerId { get { return IsSpecified ? controllerPlayerId : -1; } }
        public TeamId? LastPossession { get; }
        public int PossessionTicks { get; }
        public bool PossessionCreditEligible { get; }

        public SourceBallControlView(int controllerStationId, int controllerPlayerId,
            TeamId? lastPossession = null, int possessionTicks = 0, bool possessionCreditEligible = false)
        {
            SourceContractGuard.Station(controllerStationId, true, nameof(controllerStationId));
            SourceContractGuard.Player(controllerPlayerId, nameof(controllerPlayerId));
            if (lastPossession.HasValue)
                SourceContractGuard.Team(lastPossession.Value, nameof(lastPossession));
            SourceContractGuard.Range(possessionTicks, 0, int.MaxValue, nameof(possessionTicks));
            this.controllerStationId = controllerStationId;
            this.controllerPlayerId = controllerPlayerId;
            LastPossession = lastPossession;
            PossessionTicks = possessionTicks;
            PossessionCreditEligible = possessionCreditEligible;
            IsSpecified = true;
        }
    }

    public interface IPendingSourceInput
    {
        void ClearPendingInput();
    }
}
