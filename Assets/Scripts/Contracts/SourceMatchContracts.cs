using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Blitz.Contracts
{
    public readonly struct SourceActorInputView
    {
        public bool IsSpecified { get; }
        public int StickOrdinal { get; }
        public int Held { get; }
        public int Pressed { get; }
        public int Released { get; }
        public int NonTurboTicks { get; }
        public int NonATicks { get; }
        public int NonBTicks { get; }

        public SourceActorInputView(int stickOrdinal, int held, int pressed, int released,
            int nonTurboTicks, int nonATicks, int nonBTicks)
        {
            SourceContractGuard.Range(stickOrdinal, 0, 48, nameof(stickOrdinal));
            SourceContractGuard.Range(held, 0, 15, nameof(held));
            SourceContractGuard.Range(pressed, 0, 15, nameof(pressed));
            SourceContractGuard.Range(released, 0, 15, nameof(released));
            SourceContractGuard.Range(nonTurboTicks, -1, int.MaxValue, nameof(nonTurboTicks));
            SourceContractGuard.Range(nonATicks, -1, int.MaxValue, nameof(nonATicks));
            SourceContractGuard.Range(nonBTicks, -1, int.MaxValue, nameof(nonBTicks));
            StickOrdinal = stickOrdinal; Held = held; Pressed = pressed; Released = released;
            NonTurboTicks = nonTurboTicks; NonATicks = nonATicks; NonBTicks = nonBTicks;
            IsSpecified = true;
        }
    }

    public enum SourceCustomPlayInventoryState { Unavailable = 0, KnownNone = 1, KnownSaved = 2 }

    public readonly struct SourceStationCustomPlayInventory
    {
        private readonly int stationId;
        public bool IsSpecified { get; }
        public int StationId { get { return IsSpecified ? stationId : -1; } }
        public SourceCustomPlayInventoryState State { get; }
        public SourceStationRecordSelection RecordSelection { get; }

        public SourceStationCustomPlayInventory(int stationId, SourceCustomPlayInventoryState state)
        {
            SourceContractGuard.Station(stationId, false, nameof(stationId));
            SourceContractGuard.Range((int)state, 0, 1, nameof(state));
            this.stationId = stationId;
            State = state;
            RecordSelection = null;
            IsSpecified = true;
        }

        public SourceStationCustomPlayInventory(SourceStationRecordSelection selection)
        {
            if (selection == null) throw new ArgumentNullException(nameof(selection));
            stationId = selection.StationId;
            RecordSelection = selection;
            State = selection.State == SourceRecordSelectionState.Unavailable
                ? SourceCustomPlayInventoryState.Unavailable : SourceCustomPlayInventoryState.KnownNone;
            if (selection.Record != null)
                foreach (SourceCustomPlay play in selection.Record.CustomPlays)
                    if (play != null && play.Exists) { State = SourceCustomPlayInventoryState.KnownSaved; break; }
            IsSpecified = true;
        }

        private SourceStationCustomPlayInventory(SourceStationRecordSelection selection,
            SourceCustomPlayInventoryState state)
        {
            stationId = selection.StationId;
            RecordSelection = selection;
            State = state;
            IsSpecified = true;
        }

        internal static SourceStationCustomPlayInventory FromTwoStationRecord(SourceStationRecordSelection selection)
        {
            if (selection == null) throw new ArgumentNullException(nameof(selection));
            if (selection.State == SourceRecordSelectionState.Guest)
            {
                if (selection.Identity != null || selection.Record != null)
                    throw new ArgumentException("An explicit guest cannot carry a saved record or identity.");
                return new SourceStationCustomPlayInventory(selection, SourceCustomPlayInventoryState.KnownNone);
            }
            if ((selection.State != SourceRecordSelectionState.KnownNew &&
                 selection.State != SourceRecordSelectionState.KnownSaved) ||
                selection.Record == null || selection.Identity == null ||
                !selection.Record.Identity.Equals(selection.Identity))
                throw new ArgumentException("Named TwoStation requires its actual loaded matching record, never unavailable or an automatic guest.");
            switch (selection.Record.CustomBlockState)
            {
                case SourceCustomBlockState.NeverAllocated:
                    return new SourceStationCustomPlayInventory(selection, SourceCustomPlayInventoryState.KnownNone);
                case SourceCustomBlockState.Allocated:
                    return new SourceStationCustomPlayInventory(selection, SourceCustomPlayInventoryState.KnownSaved);
                default:
                    throw new InvalidOperationException("SOURCE_CUSTOM_BLOCK_HISTORY_UNAVAILABLE: selected named inventory has no acquired logical block history.");
            }
        }
    }

    // KnownNone means all nine saved slots were explicitly established absent.
    // Unavailable is not empty. Saved inventory retains its actual immutable record session.
    public sealed class SourceCustomPlaySession
    {
        public const string Contract = "source-custom-play-session/v1";
        public SourceMatchConfiguration Configuration { get; }
        public ReadOnlyCollection<SourceStationCustomPlayInventory> Stations { get; }
        public SourceSavedRecordSession SavedRecords { get; }

        public SourceCustomPlaySession(SourceMatchConfiguration configuration,
            IReadOnlyList<SourceStationCustomPlayInventory> stations)
            : this(configuration, stations, null)
        { }

        public SourceCustomPlaySession(SourceSavedRecordSession savedRecords)
            : this(savedRecords == null ? null : savedRecords.Configuration, SavedInventories(savedRecords), savedRecords)
        { }

        private SourceCustomPlaySession(SourceMatchConfiguration configuration,
            IReadOnlyList<SourceStationCustomPlayInventory> stations, SourceSavedRecordSession savedRecords)
        {
            Configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            if (stations == null) throw new ArgumentNullException(nameof(stations));
            if (stations.Count != configuration.Stations.Count)
                throw new ArgumentException("Specify custom inventory for every actual session station.", nameof(stations));
            var copy = new SourceStationCustomPlayInventory[stations.Count];
            var seen = new bool[SourceMatchLimits.MaximumHumanStations];
            for (int i = 0; i < copy.Length; i++)
            {
                SourceStationCustomPlayInventory inventory = stations[i];
                if (!inventory.IsSpecified || seen[inventory.StationId])
                    throw new ArgumentException("Custom inventory stations must be specified and unique.", nameof(stations));
                bool configured = false;
                foreach (SourceStationConfiguration station in configuration.Stations)
                    if (station.StationId == inventory.StationId) { configured = true; break; }
                if (!configured)
                    throw new ArgumentException("Custom inventory names an unconfigured station.", nameof(stations));
                if (savedRecords == null ? inventory.RecordSelection != null
                    : !ReferenceEquals(inventory.RecordSelection, savedRecords.Station(inventory.StationId)))
                    throw new ArgumentException("Saved inventory requires its SAME actual record session.", nameof(stations));
                seen[inventory.StationId] = true;
                copy[i] = inventory;
            }
            Stations = Array.AsReadOnly(copy);
            SavedRecords = savedRecords;
        }

        private static SourceStationCustomPlayInventory[] SavedInventories(SourceSavedRecordSession savedRecords)
        {
            if (savedRecords == null) throw new ArgumentNullException(nameof(savedRecords));
            var result = new SourceStationCustomPlayInventory[savedRecords.Stations.Count];
            for (int i = 0; i < result.Length; i++)
                result[i] = new SourceStationCustomPlayInventory(savedRecords.Stations[i]);
            return result;
        }

        public SourceCustomPlayInventoryState InventoryFor(int stationId)
        {
            SourceContractGuard.Station(stationId, false, nameof(stationId));
            foreach (SourceStationCustomPlayInventory station in Stations)
                if (station.StationId == stationId) return station.State;
            throw new ArgumentException("Custom inventory requires an actual configured station.", nameof(stationId));
        }

        public static SourceCustomPlaySession KnownNone(SourceMatchConfiguration configuration)
        {
            if (configuration == null) throw new ArgumentNullException(nameof(configuration));
            var stations = new SourceStationCustomPlayInventory[configuration.Stations.Count];
            for (int i = 0; i < stations.Length; i++)
                stations[i] = new SourceStationCustomPlayInventory(configuration.Stations[i].StationId,
                    SourceCustomPlayInventoryState.KnownNone);
            return new SourceCustomPlaySession(configuration, stations);
        }

        public static SourceCustomPlaySession FromTwoStationSavedRecords(SourceSavedRecordSession records)
        {
            if (records == null) throw new ArgumentNullException(nameof(records));
            SourceMatchConfiguration configuration = records.Configuration;
            if (configuration.Profile != SourceSimulationProfile.OriginalRelease57Hz ||
                configuration.StationLayout != SourceStationLayout.TwoStation ||
                configuration.Stations.Count != 2 || records.Stations.Count != 2)
                throw new ArgumentException("Named TwoStation inventory requires its exact actual mapped two-station configuration.");
            foreach (SourceStationConfiguration station in configuration.Stations)
                if (station.StationId < 0 || station.StationId > 1 || (int)station.Team != station.StationId)
                    throw new ArgumentException("Named TwoStation maps physical0/Home and physical1/Away only.");
            var inventories = new SourceStationCustomPlayInventory[records.Stations.Count];
            for (int i = 0; i < inventories.Length; i++)
                inventories[i] = SourceStationCustomPlayInventory.FromTwoStationRecord(records.Stations[i]);
            return new SourceCustomPlaySession(configuration, inventories, records);
        }
    }

    public readonly struct SourceCustomPlayChoice
    {
        public bool IsSpecified { get; }
        public long SourceTick { get; }
        public TeamId Team { get; }
        public SourcePlaybookKind Playbook { get; }
        public int PageIndex { get; }
        public int SlotIndex { get; }
        public int StationId { get; }
        public bool Forced { get; }
        public bool OffenseFlipped { get; }

        public SourceCustomPlayChoice(long sourceTick, TeamId team, SourcePlaybookKind playbook,
            int pageIndex, int slotIndex, int stationId, bool forced, bool offenseFlipped = false)
        {
            if (sourceTick < 0) throw new ArgumentOutOfRangeException(nameof(sourceTick));
            SourceContractGuard.Team(team, nameof(team));
            SourceContractGuard.Range((int)playbook, 0, 1, nameof(playbook));
            SourceContractGuard.Range(pageIndex, 0, 4, nameof(pageIndex));
            SourceContractGuard.Range(slotIndex, 0, 8, nameof(slotIndex));
            SourceContractGuard.Station(stationId, false, nameof(stationId));
            SourceTick = sourceTick; Team = team; Playbook = playbook;
            PageIndex = pageIndex; SlotIndex = slotIndex; StationId = stationId;
            Forced = forced; OffenseFlipped = offenseFlipped; IsSpecified = true;
        }
    }

    public interface ISourceCustomPlaySelection
    {
        SourceCustomPlaySession SourceCustomPlaySession { get; }
        bool IsSourceCustomPageAvailable(int stationId);
        SourceCommand ResolveSourceCustomPlayChoice(SourceCustomPlayChoice choice);
    }

    public enum SourceRecordCommitBarrierState { Pending, Committed, Declined, Failed }

    public sealed class SourceFinalRecordCommit
    {
        public SourceMatchView FinalMatch { get; }
        public int HomeScore { get; }
        public int AwayScore { get; }
        public int CompletedPeriodCount { get; }
        public int ParticipatingStationMask { get; }
        public long SourceTick { get; }
        public long ProcessSerial { get; }

        internal SourceFinalRecordCommit(SourceMatchView finalMatch, int homeScore, int awayScore,
            int participatingStationMask, long sourceTick, long processSerial)
        {
            if (finalMatch == null || finalMatch.Phase != SourceMatchPhase.MatchEnded
                || !finalMatch.Period.CompletedQuarterIndex.HasValue)
                throw new ArgumentException("Capture the actual immutable terminal match and completed period.", nameof(finalMatch));
            int periods = finalMatch.Period.CompletedQuarterIndex.Value + 1;
            if (periods < 4 || periods > 7 || finalMatch.QuarterScores.Count != periods)
                throw new ArgumentException("Final record commit requires the actual four-to-seven completed periods.", nameof(finalMatch));
            SourceContractGuard.Range(homeScore, 0, int.MaxValue, nameof(homeScore));
            SourceContractGuard.Range(awayScore, 0, int.MaxValue, nameof(awayScore));
            SourceContractGuard.Range(participatingStationMask, 0, 15, nameof(participatingStationMask));
            if (sourceTick <= 0 || processSerial <= 0) throw new ArgumentOutOfRangeException("sourceProcess");
            FinalMatch = finalMatch; HomeScore = homeScore; AwayScore = awayScore;
            CompletedPeriodCount = periods; ParticipatingStationMask = participatingStationMask;
            SourceTick = sourceTick; ProcessSerial = processSerial;
        }
    }

    // Runtime owns durable IO/match IDs and catches recoverable failures. Core only
    // starts once and reads completion in the executing game_proc continuation.
    public interface ISourceRecordCommitBarrier
    {
        void BeginFinalCommit(SourceFinalRecordCommit context);
        SourceRecordCommitBarrierState ReadFinalCommitState(SourceFinalRecordCommit context);
    }

    public enum SourceRandomSelectionAction { None, ChangePage, Flip, Choose }

    public readonly struct SourceRandomSelectionView
    {
        public bool IsSpecified { get; }
        public long SourceTick { get; }
        public bool ControllerAwake { get; }
        public int Direction { get; }
        public SourceRandomSelectionAction Action { get; }

        public SourceRandomSelectionView(long sourceTick, bool controllerAwake, int direction,
            SourceRandomSelectionAction action)
        {
            if (sourceTick < 0) throw new ArgumentOutOfRangeException(nameof(sourceTick));
            if (direction != 0 && direction != 1 && direction != 2 && direction != 4 && direction != 8)
                throw new ArgumentOutOfRangeException(nameof(direction));
            SourceContractGuard.Range((int)action, 0, 3, nameof(action));
            if (!controllerAwake && (direction != 0 || action != SourceRandomSelectionAction.None))
                throw new ArgumentException("A sleeping random controller cannot emit menu operations.");
            SourceTick = sourceTick; ControllerAwake = controllerAwake;
            Direction = direction; Action = action; IsSpecified = true;
        }
    }

    public sealed class SourceSelectionTimerView
    {
        public long ProcessSerial { get; }
        public int Number { get; }
        public bool Visible { get; }
        public uint Color { get; }
        public bool ForceSelection { get; }
        public long ForceTick { get; }
        public bool HomeSelected { get; }
        public bool AwaySelected { get; }

        public SourceSelectionTimerView(long serial, int number, bool visible, uint color,
            bool forceSelection, long forceTick, bool homeSelected, bool awaySelected)
        {
            ProcessSerial = serial; Number = number; Visible = visible; Color = color;
            ForceSelection = forceSelection; ForceTick = forceTick;
            HomeSelected = homeSelected; AwaySelected = awaySelected;
        }

        public bool Selected(TeamId side) { return side == TeamId.Home ? HomeSelected : AwaySelected; }
    }

    public readonly struct SourceSelectionTimerSound
    {
        public SourceProcessSoundCall Output { get; }
        public long OutputSequence { get { return Output == null ? 0 : Output.OutputSequence; } }
        public long SourceTick { get; }
        public long ProcessSerial { get; }
        public int Number { get; }
        public string Bank { get { return Output == null ? "cursors" : Output.Bank; } }
        public int PlaylistId { get { return 36; } }
        public uint Volume { get { return 235; } }
        public uint Pan { get { return 127; } }
        public uint PriorityWord { get { return 5; } }
        public bool PcmOrDspPlayback { get { return false; } }

        public SourceSelectionTimerSound(long tick, long serial, int number)
            : this(tick, serial, number, null) { }

        public SourceSelectionTimerSound(long tick, long serial, int number, SourceProcessSoundCall output)
        {
            if (output != null && (output.Kind != SourceProcessSoundKind.SelectionTimer
                || output.SourceTick != tick || output.ProcessSerial != serial
                || output.Bank != "cursors" && output.Bank != "cursors2"
                || output.PlaylistId != 36 || output.Volume != 235 || output.Pan != 127 || output.PriorityWord != 5))
                throw new ArgumentException("Timer output must belong to this actual source call.", nameof(output));
            SourceTick = tick; ProcessSerial = serial; Number = number; Output = output;
        }
    }

    public enum SourceGameplayCompatibility { StrictSource = 0, Portable = 1 }
    public enum SourceCompatibilityRepairKind
    {
        HandicapConsumerIndex, BlockCountConsumerIndex, TargetlessRunningPush, CarrierlessDefensiveSwitch,
        // Portable + bound source-gameflow/v1 only: defer the undefined ended-play
        // A target read, retaining raw controls/movement without a carrier/script/RNG substitute.
        CarrierlessDeadBallLateHit
    }

    public readonly struct SourceCompatibilityRepair
    {
        public long Sequence { get; }
        public long SourceTick { get; }
        public SourceCompatibilityRepairKind Kind { get; }
        public int SubjectId { get; }
        public int RawIndex { get; }
        public int EffectiveIndex { get; }
        public string SourceContext { get; }
        public int? RetainedStick { get; }

        public SourceCompatibilityRepair(long sequence, long sourceTick, SourceCompatibilityRepairKind kind,
            int subjectId, int rawIndex, int effectiveIndex, string sourceContext, int? retainedStick = null)
        {
            Sequence = sequence; SourceTick = sourceTick; Kind = kind; SubjectId = subjectId;
            RawIndex = rawIndex; EffectiveIndex = effectiveIndex; SourceContext = sourceContext;
            RetainedStick = retainedStick;
        }
    }

    public static class SourceGameplayCompatibilityOptions
    {
        public static SourceGameplayCompatibility Parse(string value)
        {
            if (value == "strict") return SourceGameplayCompatibility.StrictSource;
            if (value == "portable") return SourceGameplayCompatibility.Portable;
            throw new ArgumentException("source-compatibility must be exactly strict or portable.", nameof(value));
        }

        public static SourceGameplayCompatibility FromUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return SourceGameplayCompatibility.StrictSource;
            int query = url.IndexOf('?');
            int fragment = url.IndexOf('#');
            if (query < 0 || fragment >= 0 && fragment < query) return SourceGameplayCompatibility.StrictSource;
            string tail = url.Substring(query + 1).Split('#')[0];
            SourceGameplayCompatibility result = SourceGameplayCompatibility.StrictSource;
            bool seen = false;
            foreach (string part in tail.Split('&'))
            {
                string[] pair = part.Split('=');
                if (Uri.UnescapeDataString(pair[0]) != "source-compatibility") continue;
                if (seen || pair.Length != 2)
                    throw new ArgumentException("Specify source-compatibility exactly once with a value.", nameof(url));
                seen = true;
                result = Parse(Uri.UnescapeDataString(pair[1]));
            }
            return result;
        }
    }

    public enum SourcePlayKind { Normal = 0, Kickoff = 1, FieldGoal = 2, Punt = 3, ExtraPoint = 4 }
    public enum SourceResultKind
    {
        Down = 0, Touchdown = 1, Safety = 2, PatSafety = 3, NewPossession = 4,
        Incomplete = 5, FumbledOutOfBounds = 6, FieldGoal = 7, ExtraPoint = 8,
        MissedKick = 9, FailedConversion = 10, Conversion = 11, PuntOutOfBounds = 12,
        DownedPunt = 13, KickoffOutOfBounds = 14, Touchback = 15
    }
    public enum SourceResultStatus { Classified = 0, IntentionalNoOp = 1, InvalidSourceBranch = 2 }
    public enum SourcePlayEndKind
    {
        CarrierOutOfBounds = 0, CarrierTackledInBounds = 1, CarrierDownInOwnEndZone = 2,
        CarrierTouchdown = 3, DeadBallInBounds = 4, BallOutOfBounds = 5
    }
    public enum SourcePatOption { Unselected = 0, FreeKick = 1, TwoPointPlay = 2 }
    public enum SourcePatStatus
    {
        NotExtraPoint = 0, SkipUnnecessary = 1, AwaitHumanSelection = 2,
        AwaitRandom = 3, RunTwoPointPlay = 4, FreeKickComplete = 5
    }
    public enum SourceKickoffType { Opening = 0, Quick = 1, Onside = 2 }
    public enum SourcePeriodStatus
    {
        NotExpired = 0, FinishCurrentPlay = 1, DeferredForExtraPoint = 2, AttractMode = 3,
        AwaitOvertimeDraw = 4, NextQuarter = 5, HalftimeKickoff = 6, OvertimeKickoff = 7,
        MatchEnded = 8, CabinetContinuationDeclined = 9
    }

    // Presentation-facing tags, not the numeric GS_* identifiers in GAME.H.
    public enum SourceMatchPhase
    {
        Unspecified, PlaySelection, PatSelection, PreSnap, LiveBall,
        PlayResult, FreePointResult, PeriodTransition, MatchEnded
    }

    public enum SourceEventType
    {
        Unspecified, PlaySelected, Snapped, PassReleased, PassCaught, PassDeflected,
        PassMissed, Intercepted, Fumbled, Bobbled, LateralReleased, Punted, Kicked,
        PlayResolved, PatSelected, FreePointResult, PeriodChanged, PossessionChanged,
        ControllerChanged, MatchEnded
    }

    public readonly struct SourceMatchResult
    {
        public bool IsSpecified { get; }
        public SourcePlayKind PlayKind { get; }
        public SourceResultKind Kind { get; }
        public SourceResultStatus? Status { get; }
        public SourcePlayEndKind? Cause { get; }
        public int HomePoints { get; }
        public int AwayPoints { get; }
        public bool JustGotFirstDown { get; }
        public bool TurnoverOnDowns { get; }

        public SourceMatchResult(SourcePlayKind playKind, SourceResultKind kind, SourceResultStatus? status = null,
            SourcePlayEndKind? cause = null, int homePoints = 0, int awayPoints = 0,
            bool justGotFirstDown = false, bool turnoverOnDowns = false)
        {
            SourceContractGuard.Range((int)playKind, 0, 4, nameof(playKind));
            SourceContractGuard.Range((int)kind, 0, 15, nameof(kind));
            if (status.HasValue)
                SourceContractGuard.Range((int)status.Value, 0, 2, nameof(status));
            if (cause.HasValue)
                SourceContractGuard.Range((int)cause.Value, 0, 5, nameof(cause));
            SourceContractGuard.Range(homePoints, 0, int.MaxValue, nameof(homePoints));
            SourceContractGuard.Range(awayPoints, 0, int.MaxValue, nameof(awayPoints));
            PlayKind = playKind;
            Kind = kind;
            Status = status;
            Cause = cause;
            HomePoints = homePoints;
            AwayPoints = awayPoints;
            JustGotFirstDown = justGotFirstDown;
            TurnoverOnDowns = turnoverOnDowns;
            IsSpecified = true;
        }
    }

    public readonly struct SourcePeriodView
    {
        private readonly int quarterIndex;
        public bool IsSpecified { get; }
        public int QuarterIndex { get { return IsSpecified ? quarterIndex : -1; } }
        public SourcePeriodStatus Status { get; }
        public int? CompletedQuarterIndex { get; }
        public bool CabinetGateRequired { get; }

        public SourcePeriodView(int quarterIndex, SourcePeriodStatus status = SourcePeriodStatus.NotExpired,
            int? completedQuarterIndex = null, bool cabinetGateRequired = false)
        {
            SourceContractGuard.Range(quarterIndex, 0, SourceMatchLimits.MaximumPeriodCount - 1, nameof(quarterIndex));
            SourceContractGuard.Range((int)status, 0, 9, nameof(status));
            if (completedQuarterIndex.HasValue)
                SourceContractGuard.Range(completedQuarterIndex.Value, 0, SourceMatchLimits.MaximumPeriodCount - 1, nameof(completedQuarterIndex));
            this.quarterIndex = quarterIndex;
            Status = status;
            CompletedQuarterIndex = completedQuarterIndex;
            CabinetGateRequired = cabinetGateRequired;
            IsSpecified = true;
        }
    }

    public readonly struct SourcePossessionView
    {
        public bool IsSpecified { get; }
        public TeamId Offense { get; }
        public TeamId InitialOffense { get; }
        public TeamId LastPossession { get; }
        public TeamId FirstPossession { get; }
        public SourceCaptainSlot Captain { get; }

        public SourcePossessionView(TeamId offense, TeamId initialOffense, TeamId lastPossession,
            TeamId firstPossession, SourceCaptainSlot captain)
        {
            SourceContractGuard.Team(offense, nameof(offense));
            SourceContractGuard.Team(initialOffense, nameof(initialOffense));
            SourceContractGuard.Team(lastPossession, nameof(lastPossession));
            SourceContractGuard.Team(firstPossession, nameof(firstPossession));
            SourceContractGuard.Range((int)captain, 0, 1, nameof(captain));
            Offense = offense;
            InitialOffense = initialOffense;
            LastPossession = lastPossession;
            FirstPossession = firstPossession;
            Captain = captain;
            IsSpecified = true;
        }
    }

    public readonly struct SourceQuarterScore
    {
        private readonly int quarterIndex;
        public bool IsSpecified { get; }
        public int QuarterIndex { get { return IsSpecified ? quarterIndex : -1; } }
        public int HomeScore { get; }
        public int AwayScore { get; }

        public SourceQuarterScore(int quarterIndex, int homeScore, int awayScore)
        {
            SourceContractGuard.Range(quarterIndex, 0, SourceMatchLimits.MaximumPeriodCount - 1, nameof(quarterIndex));
            SourceContractGuard.Range(homeScore, 0, int.MaxValue, nameof(homeScore));
            SourceContractGuard.Range(awayScore, 0, int.MaxValue, nameof(awayScore));
            this.quarterIndex = quarterIndex;
            HomeScore = homeScore;
            AwayScore = awayScore;
            IsSpecified = true;
        }
    }

    public readonly struct SourceTeamGameplayView
    {
        public bool IsSpecified { get; }
        public TeamId Team { get; }
        public bool OnFire { get; }
        public int LastReceiverPlayerId { get; }
        public int ConsecutiveCompletions { get; }
        public int ConsecutiveLosses { get; }
        public int Handicap { get; }

        public int EffectiveHandicap { get; }
        public int RawHandicap { get { return Handicap; } }

        public SourceTeamGameplayView(TeamId team, bool onFire, int lastReceiverPlayerId,
            int consecutiveCompletions, int consecutiveLosses, int handicap, int? effectiveHandicap = null)
        {
            SourceContractGuard.Team(team, nameof(team));
            SourceContractGuard.Player(lastReceiverPlayerId, nameof(lastReceiverPlayerId));
            if (lastReceiverPlayerId >= 0 && lastReceiverPlayerId / SliceRules.PlayersPerTeam != (int)team)
                throw new ArgumentException("A fire receiver belongs to its actor's team.", nameof(lastReceiverPlayerId));
            SourceContractGuard.Range(consecutiveCompletions, 0, int.MaxValue, nameof(consecutiveCompletions));
            SourceContractGuard.Range(consecutiveLosses, 0, int.MaxValue, nameof(consecutiveLosses));
            SourceContractGuard.Range(effectiveHandicap ?? handicap, 0, 11, nameof(effectiveHandicap));
            Team = team;
            OnFire = onFire;
            LastReceiverPlayerId = lastReceiverPlayerId;
            ConsecutiveCompletions = consecutiveCompletions;
            ConsecutiveLosses = consecutiveLosses;
            Handicap = handicap;
            EffectiveHandicap = effectiveHandicap ?? handicap;
            IsSpecified = true;
        }
    }

    public enum SourceAudibleRole { Offense, Defense }
    public enum SourceAudibleRestartStage { None, Polling, SleepBeforeStartEnable, SleepAfterStartEnable }
    public enum SourceAudibleFlashStage { PendingEntry, WhiteSleep, ColorSleep, Completed, MissingSprite, Killed }
    [Flags]
    public enum SourceSpriteListMembership { None = 0, NonAlpha = 1, Alpha = 2 }

    public readonly struct SourceAudibleTeamView
    {
        public bool IsSpecified { get; }
        public TeamId Team { get; }
        public int Timer { get; }
        public int Action { get; }
        public bool Shown { get; }
        public long? BackgroundSpriteObjectSerial { get; }

        public SourceAudibleTeamView(TeamId team, int timer, int action, bool shown,
            long? backgroundSpriteObjectSerial = null)
        {
            SourceContractGuard.Team(team, nameof(team));
            SourceContractGuard.Range(timer, -1, int.MaxValue, nameof(timer));
            SourceContractGuard.Range(action, 0, 1, nameof(action));
            if (backgroundSpriteObjectSerial.HasValue && backgroundSpriteObjectSerial.Value <= 0)
                throw new ArgumentOutOfRangeException(nameof(backgroundSpriteObjectSerial));
            Team = team; Timer = timer; Action = action; Shown = shown;
            BackgroundSpriteObjectSerial = backgroundSpriteObjectSerial;
            IsSpecified = true;
        }
    }

    public readonly struct SourceAudibleRoleView
    {
        private readonly int callerStationId;
        public bool IsSpecified { get; }
        public SourceAudibleRole Role { get; }
        public TeamId Team { get; }
        public int CallerStationId { get { return IsSpecified ? callerStationId : -1; } }
        public int Choice { get; }
        public bool Pending { get; }
        public SourcePlaySelection OriginalPlay { get; }
        public SourcePlaySelection CurrentPlay { get; }

        public SourceAudibleRoleView(SourceAudibleRole role, TeamId team, int callerStationId,
            int choice, bool pending, SourcePlaySelection originalPlay, SourcePlaySelection currentPlay)
        {
            SourceContractGuard.Range((int)role, 0, 1, nameof(role));
            SourceContractGuard.Team(team, nameof(team));
            SourceContractGuard.Station(callerStationId, true, nameof(callerStationId));
            SourceContractGuard.Range(choice, 0, 3, nameof(choice));
            SourcePlaybookKind kind = role == SourceAudibleRole.Offense
                ? SourcePlaybookKind.Offense : SourcePlaybookKind.Defense;
            if (!originalPlay.IsSpecified || originalPlay.Playbook != kind)
                throw new ArgumentException("Capture the actual original role play.", nameof(originalPlay));
            if (!currentPlay.IsSpecified || currentPlay.Playbook != kind)
                throw new ArgumentException("Publish the actual current role play.", nameof(currentPlay));
            Role = role; Team = team; this.callerStationId = callerStationId;
            Choice = choice; Pending = pending; OriginalPlay = originalPlay; CurrentPlay = currentPlay;
            IsSpecified = true;
        }
    }

    public sealed class SourceAudibleSpriteView
    {
        public long ObjectSerial { get; }
        public TeamId Team { get; }
        public int ObjectId { get; }
        public long ParentProcessSerial { get; }
        public string ImageSymbol { get; }
        public SourceVector3 Position { get; }
        public SourceSpriteListMembership Membership { get; }
        public bool Exists { get { return Membership != SourceSpriteListMembership.None; } }
        public bool DrawVisible { get; }
        public uint ConstantColor { get; }
        public int ColorCombinerFunction { get; }
        public int ChromaKeyMode { get; }

        // ObjectSerial identifies the Core object reference. Membership is the actual
        // pointer-membership observation, independent of OID, parent and draw visibility.
        public SourceAudibleSpriteView(long objectSerial, TeamId team, int objectId, long parentProcessSerial,
            string imageSymbol, SourceVector3 position, SourceSpriteListMembership membership, bool drawVisible,
            uint constantColor, int colorCombinerFunction, int chromaKeyMode)
        {
            if (objectSerial <= 0) throw new ArgumentOutOfRangeException(nameof(objectSerial));
            SourceContractGuard.Team(team, nameof(team));
            if (parentProcessSerial <= 0) throw new ArgumentOutOfRangeException(nameof(parentProcessSerial));
            if (string.IsNullOrWhiteSpace(imageSymbol)) throw new ArgumentException("An actual sprite image is required.", nameof(imageSymbol));
            SourceContractGuard.Finite(position.X, nameof(position));
            SourceContractGuard.Finite(position.Y, nameof(position));
            SourceContractGuard.Finite(position.Z, nameof(position));
            SourceContractGuard.Range((int)membership, 0, 3, nameof(membership));
            SourceContractGuard.Range(chromaKeyMode, 0, 1, nameof(chromaKeyMode));
            ObjectSerial = objectSerial; Team = team; ObjectId = objectId; ParentProcessSerial = parentProcessSerial;
            ImageSymbol = imageSymbol; Position = position; Membership = membership; DrawVisible = drawVisible;
            ConstantColor = constantColor; ColorCombinerFunction = colorCombinerFunction; ChromaKeyMode = chromaKeyMode;
        }
    }

    public sealed class SourceAudibleFlashView
    {
        public TeamId Team { get; }
        public SourceProcessView Process { get; }
        public long SpriteObjectSerial { get; }
        public SourceAudibleFlashStage Stage { get; }
        public long? EntrySourceTick { get; }
        public int CompletedCycles { get; }
        public int CycleCount { get { return 3; } }
        public int DelaySourceTicks { get { return 2; } }
        public uint? SavedConstantColor { get; }
        public int? SavedColorCombinerFunction { get; }
        public long? LastMembershipCheckTick { get; }
        public bool? LastMembershipCheckExists { get; }
        public bool NormalRestoreCompleted { get { return Stage == SourceAudibleFlashStage.Completed; } }

        public SourceAudibleFlashView(TeamId team, SourceProcessView process, long spriteObjectSerial,
            SourceAudibleFlashStage stage, long? entrySourceTick, int completedCycles,
            uint? savedConstantColor, int? savedColorCombinerFunction,
            long? lastMembershipCheckTick, bool? lastMembershipCheckExists)
        {
            SourceContractGuard.Team(team, nameof(team));
            if (process.Serial <= 0 || string.IsNullOrWhiteSpace(process.Function))
                throw new ArgumentException("Observe the actual child process.", nameof(process));
            if (spriteObjectSerial <= 0) throw new ArgumentOutOfRangeException(nameof(spriteObjectSerial));
            SourceContractGuard.Range((int)stage, 0, 5, nameof(stage));
            SourceContractGuard.Range(completedCycles, 0, 3, nameof(completedCycles));
            if (entrySourceTick.HasValue && entrySourceTick.Value < 0)
                throw new ArgumentOutOfRangeException(nameof(entrySourceTick));
            if (savedConstantColor.HasValue != savedColorCombinerFunction.HasValue
                || savedConstantColor.HasValue && !entrySourceTick.HasValue)
                throw new ArgumentException("Entry color and combiner are saved together after actual entry.");
            if (lastMembershipCheckTick.HasValue != lastMembershipCheckExists.HasValue
                || lastMembershipCheckTick.HasValue && (lastMembershipCheckTick.Value < 0
                    || entrySourceTick.HasValue && lastMembershipCheckTick.Value < entrySourceTick.Value))
                throw new ArgumentException("Retain the actual membership check tick/result together.");
            if (stage == SourceAudibleFlashStage.PendingEntry
                && (entrySourceTick.HasValue || savedConstantColor.HasValue || completedCycles != 0
                    || lastMembershipCheckTick.HasValue))
                throw new ArgumentException("A pending child has not entered or checked its borrowed sprite.");
            if ((stage == SourceAudibleFlashStage.WhiteSleep || stage == SourceAudibleFlashStage.ColorSleep
                    || stage == SourceAudibleFlashStage.Completed)
                && (!savedConstantColor.HasValue || lastMembershipCheckExists != true))
                throw new ArgumentException("Active/normal completion requires saved entry state and a successful membership check.");
            if (stage == SourceAudibleFlashStage.Completed && completedCycles != 3)
                throw new ArgumentException("Normal completion follows three whole white/color cycles.");
            if (stage == SourceAudibleFlashStage.MissingSprite && lastMembershipCheckExists != false)
                throw new ArgumentException("Missing-sprite exit requires the failed actual membership check.");
            Team = team; Process = process; SpriteObjectSerial = spriteObjectSerial; Stage = stage;
            EntrySourceTick = entrySourceTick; CompletedCycles = completedCycles;
            SavedConstantColor = savedConstantColor; SavedColorCombinerFunction = savedColorCombinerFunction;
            LastMembershipCheckTick = lastMembershipCheckTick; LastMembershipCheckExists = lastMembershipCheckExists;
        }
    }

    public enum SourceProcessSoundKind
    {
        SelectionTimer, PossessionSpeech, CabinetSelection, InstructionTick, InstructionPageAdvance,
        KickoffInstruction, PlaySelectionIntroduction, OriginalGameplay, OriginalContact
    }

    public readonly struct SourceProcessCameraSelection
    {
        public long SourceTick { get; }
        public int ProcessOrder { get; }
        public int ActorId { get; }
        public string Table { get { return "csi_fs_endqtr"; } }
        public int SelectedOrdinal { get; }
        public string SourcePath { get { return "plyrinfo.c"; } }
        public string Function { get { return "show_end_quarter_proc"; } }
        public int SourceLine { get { return 4121; } }

        internal SourceProcessCameraSelection(long tick, int order, int actor, int ordinal)
        {
            if (tick <= 0) throw new ArgumentOutOfRangeException(nameof(tick));
            SourceContractGuard.Range(order, 0, int.MaxValue, nameof(order));
            SourceContractGuard.Range(actor, 0, 13, nameof(actor));
            SourceContractGuard.Range(ordinal, 0, 0, nameof(ordinal));
            SourceTick = tick; ProcessOrder = order; ActorId = actor; SelectedOrdinal = ordinal;
        }
    }

    // Actorless process calls are not animation cues or invented player/team calls.
    // Only the executing producer can construct their shared output ordinal.
    public sealed class SourceProcessSoundCall
    {
        public SourceProcessSoundKind Kind { get; }
        public long OutputSequence { get; }
        public long SourceTick { get; }
        public long ProcessSerial { get; }
        public string Process { get; }
        public int DispatchOrder { get; }
        public int SubcallOrder { get; }
        public string Operand { get; }
        public string Bank { get; }
        public int PlaylistId { get; }
        public uint Volume { get; }
        public uint Pan { get; }
        public uint PriorityWord { get; }
        public int RandomCalls { get; }
        public SourceContactSoundCue OriginalContactCue { get; }
        public string CalleeSourcePath { get; }
        public string CalleeSourceGitBlob { get; }
        public string CalleeFunction { get; }
        public int CalleeSourceLine { get; }
        public bool PcmOrDspPlayback { get { return false; } }

        internal SourceProcessSoundCall(SourceProcessSoundKind kind, long outputSequence, long sourceTick,
            long processSerial, string process, int dispatchOrder, int subcallOrder, string operand,
            string bank, int playlistId, uint volume, uint pan, uint priorityWord, int randomCalls,
            SourceContactSoundCue originalContactCue = null, string calleeSourcePath = null,
            string calleeSourceGitBlob = null, string calleeFunction = null, int calleeSourceLine = 0)
        {
            SourceContractGuard.Range((int)kind, 0, 8, nameof(kind));
            if (outputSequence <= 0) throw new ArgumentOutOfRangeException(nameof(outputSequence));
            if (sourceTick <= 0) throw new ArgumentOutOfRangeException(nameof(sourceTick));
            if (processSerial <= 0) throw new ArgumentOutOfRangeException(nameof(processSerial));
            if (string.IsNullOrWhiteSpace(process)) throw new ArgumentException("An executing source process is required.", nameof(process));
            SourceContractGuard.Range(dispatchOrder, 0, int.MaxValue, nameof(dispatchOrder));
            SourceContractGuard.Range(subcallOrder, 0, int.MaxValue, nameof(subcallOrder));
            if (string.IsNullOrWhiteSpace(operand)) throw new ArgumentException("A typed source operand is required.", nameof(operand));
            if (string.IsNullOrWhiteSpace(bank)) throw new ArgumentException("An actual source bank is required.", nameof(bank));
            SourceContractGuard.Range(playlistId, 0, int.MaxValue, nameof(playlistId));
            if (volume > 255) throw new ArgumentOutOfRangeException(nameof(volume));
            if (pan > 255) throw new ArgumentOutOfRangeException(nameof(pan));
            SourceContractGuard.Range(randomCalls, 0, int.MaxValue, nameof(randomCalls));
            if (kind == SourceProcessSoundKind.OriginalContact &&
                (originalContactCue == null || originalContactCue.Context.SourceTick > sourceTick ||
                    string.IsNullOrEmpty(calleeSourcePath) || string.IsNullOrEmpty(calleeFunction) ||
                    calleeSourceGitBlob == null || calleeSourceGitBlob.Length != 40 || calleeSourceLine <= 0))
                throw new ArgumentException("Resolved contact sound retains its actual original call and callee.");
            if (kind != SourceProcessSoundKind.OriginalContact && originalContactCue != null)
                throw new ArgumentException("Only a resolved original contact call carries contact metadata.");
            Kind = kind; OutputSequence = outputSequence; SourceTick = sourceTick;
            ProcessSerial = processSerial; Process = process; DispatchOrder = dispatchOrder; SubcallOrder = subcallOrder;
            Operand = operand; Bank = bank; PlaylistId = playlistId; Volume = volume; Pan = pan;
            PriorityWord = priorityWord; RandomCalls = randomCalls;
            OriginalContactCue = originalContactCue; CalleeSourcePath = calleeSourcePath;
            CalleeSourceGitBlob = calleeSourceGitBlob; CalleeFunction = calleeFunction; CalleeSourceLine = calleeSourceLine;
        }
    }

    public enum SourceProcessSoundControlKind
    { LoadBank, DeleteBank, StopTracks, StopAll, ReservedTracks, TrackVolume, TrackPan }

    public sealed class SourceProcessSoundControl
    {
        public SourceProcessSoundControlKind Kind { get; }
        public long OutputSequence { get; }
        public long SourceTick { get; }
        public long ProcessSerial { get; }
        public string Process { get; }
        public int DispatchOrder { get; }
        public int SubcallOrder { get; }
        public string SourcePath { get; }
        public string FunctionSymbol { get; }
        public int SourceLine { get; }
        public string Bank { get; }
        public uint TrackMask { get; }
        public uint Value { get; }

        internal SourceProcessSoundControl(SourceProcessSoundControlKind kind, long outputSequence,
            long sourceTick, long processSerial, string process, int dispatchOrder, int subcallOrder,
            string sourcePath, string functionSymbol, int sourceLine, string bank, uint trackMask, uint value)
        {
            ValidateOperands(kind, bank, trackMask, value);
            if (outputSequence <= 0 || sourceTick <= 0 || processSerial <= 0)
                throw new ArgumentOutOfRangeException("identity", "A live control requires its actual source process wake.");
            if (string.IsNullOrWhiteSpace(process) || string.IsNullOrWhiteSpace(sourcePath)
                || string.IsNullOrWhiteSpace(functionSymbol) || sourceLine < 1)
                throw new ArgumentException("Supply the actual source control caller.");
            SourceContractGuard.Range(dispatchOrder, 0, int.MaxValue, nameof(dispatchOrder));
            SourceContractGuard.Range(subcallOrder, 0, int.MaxValue, nameof(subcallOrder));
            Kind = kind; OutputSequence = outputSequence; SourceTick = sourceTick; ProcessSerial = processSerial;
            Process = process; DispatchOrder = dispatchOrder; SubcallOrder = subcallOrder;
            SourcePath = sourcePath; FunctionSymbol = functionSymbol; SourceLine = sourceLine;
            Bank = bank; TrackMask = trackMask; Value = value;
        }

        internal static void ValidateOperands(SourceProcessSoundControlKind kind, string bank, uint tracks, uint value)
        {
            SourceContractGuard.Range((int)kind, 0, 6, nameof(kind));
            bool bankControl = kind == SourceProcessSoundControlKind.LoadBank || kind == SourceProcessSoundControlKind.DeleteBank;
            if (bankControl ? string.IsNullOrWhiteSpace(bank) : bank != null)
                throw new ArgumentException("Only a bank control carries its actual bank operand.", nameof(bank));
            if (tracks > 63 || (bankControl || kind == SourceProcessSoundControlKind.StopAll) && tracks != 0)
                throw new ArgumentOutOfRangeException(nameof(tracks));
            if (value > 255 || kind != SourceProcessSoundControlKind.TrackVolume
                && kind != SourceProcessSoundControlKind.TrackPan && value != 0)
                throw new ArgumentOutOfRangeException(nameof(value));
        }
    }

    public enum SourceBootSoundOutputKind { PlaySound, Control }

    // The named constructor entry is not an executing portable process or a native allocation trace.
    public sealed class SourceBootSoundOutput
    {
        public SourceC2EntryKind EntryKind { get { return SourceC2EntryKind.CompleteOriginalVersusSoundBanks; } }
        public SourceBootSoundOutputKind Kind { get; }
        public long OutputSequence { get; }
        public long SourceTick { get { return 0; } }
        public long ProcessSerial { get { return 0; } }
        public int BootOrder { get; }
        public string SourcePath { get; }
        public string FunctionSymbol { get; }
        public int SourceLine { get; }
        public string Bank { get; }
        public int? PlaylistId { get; }
        public uint Volume { get; }
        public uint Pan { get; }
        public uint PriorityWord { get; }
        public SourceProcessSoundControlKind? ControlKind { get; }
        public uint TrackMask { get; }
        public uint Value { get; }

        internal SourceBootSoundOutput(long outputSequence, int bootOrder, string sourcePath, string function,
            int sourceLine, string bank, int? playlist, uint volume, uint pan, uint priority,
            SourceProcessSoundControlKind? control, uint tracks, uint value)
        {
            if (outputSequence <= 0 || bootOrder < 0) throw new ArgumentOutOfRangeException("identity");
            if (string.IsNullOrWhiteSpace(sourcePath) || string.IsNullOrWhiteSpace(function) || sourceLine < 1)
                throw new ArgumentException("Supply the actual named boot source caller.");
            if (control.HasValue)
            {
                SourceProcessSoundControl.ValidateOperands(control.Value, bank, tracks, value);
                if (playlist.HasValue || volume != 0 || pan != 0 || priority != 0)
                    throw new ArgumentException("A boot control is not a sound call.");
            }
            else if (!playlist.HasValue || playlist.Value < 0 || string.IsNullOrWhiteSpace(bank)
                || volume > 255 || pan > 255 || tracks != 0 || value != 0)
                throw new ArgumentException("A boot call requires its actual bank/playlist and sound operands.");
            Kind = control.HasValue ? SourceBootSoundOutputKind.Control : SourceBootSoundOutputKind.PlaySound;
            OutputSequence = outputSequence; BootOrder = bootOrder; SourcePath = sourcePath;
            FunctionSymbol = function; SourceLine = sourceLine; Bank = bank; PlaylistId = playlist;
            Volume = volume; Pan = pan; PriorityWord = priority; ControlKind = control;
            TrackMask = tracks; Value = value;
        }
    }

    public sealed class SourceDirectSoundCall
    {
        public long Sequence { get; }
        public long OutputSequence { get; }
        public long SourceTick { get; }
        public long ProcessSerial { get; }
        public string Process { get; }
        public int DispatchOrder { get; }
        public int ActorPlayerId { get; }
        public int CallerStationId { get; }
        public TeamId Team { get; }
        public string SourcePath { get; }
        public string FunctionSymbol { get; }
        public int SourceLine { get; }
        public string Bank { get; }
        public int PlaylistId { get; }
        public uint Volume { get; }
        public uint Pan { get; }
        public uint PriorityWord { get; }
        public int SubcallOrder { get; }
        public int RandomCalls { get { return 0; } }
        public bool PcmOrDspPlayback { get { return false; } }

        public SourceDirectSoundCall(long sequence, long sourceTick, long processSerial, string process,
            int dispatchOrder, int actorPlayerId, int callerStationId, TeamId team,
            string sourcePath, string functionSymbol, int sourceLine, string bank, int playlistId,
            uint volume, uint pan, uint priorityWord, int subcallOrder, long outputSequence = 0)
        {
            if (sequence <= 0) throw new ArgumentOutOfRangeException(nameof(sequence));
            if (outputSequence < 0) throw new ArgumentOutOfRangeException(nameof(outputSequence));
            if (sourceTick < 0) throw new ArgumentOutOfRangeException(nameof(sourceTick));
            if (processSerial <= 0) throw new ArgumentOutOfRangeException(nameof(processSerial));
            if (string.IsNullOrWhiteSpace(process)) throw new ArgumentException("An actual process name is required.", nameof(process));
            SourceContractGuard.Range(dispatchOrder, 0, int.MaxValue, nameof(dispatchOrder));
            SourceContractGuard.Player(actorPlayerId, nameof(actorPlayerId));
            SourceContractGuard.Station(callerStationId, true, nameof(callerStationId));
            SourceContractGuard.Team(team, nameof(team));
            if (actorPlayerId >= 0 && actorPlayerId / SliceRules.PlayersPerTeam != (int)team)
                throw new ArgumentException("A direct call retains its actual actor's team.", nameof(team));
            if (string.IsNullOrWhiteSpace(sourcePath)) throw new ArgumentException("An original call-site path is required.", nameof(sourcePath));
            if (string.IsNullOrWhiteSpace(functionSymbol)) throw new ArgumentException("An original caller is required.", nameof(functionSymbol));
            SourceContractGuard.Range(sourceLine, 1, int.MaxValue, nameof(sourceLine));
            if (string.IsNullOrWhiteSpace(bank)) throw new ArgumentException("An actual source bank is required.", nameof(bank));
            SourceContractGuard.Range(playlistId, 0, int.MaxValue, nameof(playlistId));
            if (volume > 255) throw new ArgumentOutOfRangeException(nameof(volume));
            if (pan > 255) throw new ArgumentOutOfRangeException(nameof(pan));
            SourceContractGuard.Range(subcallOrder, 0, int.MaxValue, nameof(subcallOrder));
            Sequence = sequence; SourceTick = sourceTick; ProcessSerial = processSerial; Process = process;
            DispatchOrder = dispatchOrder; ActorPlayerId = actorPlayerId; CallerStationId = callerStationId; Team = team;
            SourcePath = sourcePath; FunctionSymbol = functionSymbol; SourceLine = sourceLine;
            Bank = bank; PlaylistId = playlistId; Volume = volume; Pan = pan; PriorityWord = priorityWord;
            SubcallOrder = subcallOrder;
            OutputSequence = outputSequence;
        }
    }

    public interface ISourceDirectSoundOutputConsumer
    {
        void Consume(SourceDirectSoundCall call);
    }

    public readonly struct SourceAudibleDroneView
    {
        private readonly int actorPlayerId;
        public bool IsSpecified { get; }
        public int ActorPlayerId { get { return IsSpecified ? actorPlayerId : -1; } }
        public string RouteIdentity { get; }
        public int WordIndex { get; }
        public int TargetPlayerId { get; }
        public int RawDroneFlags { get; }
        public int RawJob { get; }
        public int ThinkTicks { get; }
        public int BlitzTicks { get; }
        public int PassReactTicks { get; }
        public int FakeZoneTicks { get; }
        public int CatchTicks { get; }
        public int WaitTicks { get; }
        public int BlockTicks { get; }
        public long NameCoordinateInvalidationSourceTick { get; }
        public bool Ended { get; }

        public SourceAudibleDroneView(int actorPlayerId, string routeIdentity, int wordIndex,
            int targetPlayerId, int rawDroneFlags, int rawJob, int thinkTicks, int blitzTicks,
            int passReactTicks, int fakeZoneTicks, int catchTicks, int waitTicks, int blockTicks,
            long nameCoordinateInvalidationSourceTick, bool ended)
        {
            SourceContractGuard.Range(actorPlayerId, 0, SliceRules.PlayersPerTeam * 2 - 1, nameof(actorPlayerId));
            if (string.IsNullOrWhiteSpace(routeIdentity))
                throw new ArgumentException("Observe the actual initialized route program.", nameof(routeIdentity));
            SourceContractGuard.Range(wordIndex, 0, int.MaxValue, nameof(wordIndex));
            SourceContractGuard.Player(targetPlayerId, nameof(targetPlayerId));
            if (nameCoordinateInvalidationSourceTick < -1)
                throw new ArgumentOutOfRangeException(nameof(nameCoordinateInvalidationSourceTick));
            this.actorPlayerId = actorPlayerId; RouteIdentity = routeIdentity; WordIndex = wordIndex;
            TargetPlayerId = targetPlayerId; RawDroneFlags = rawDroneFlags; RawJob = rawJob;
            ThinkTicks = thinkTicks; BlitzTicks = blitzTicks; PassReactTicks = passReactTicks;
            FakeZoneTicks = fakeZoneTicks; CatchTicks = catchTicks; WaitTicks = waitTicks; BlockTicks = blockTicks;
            NameCoordinateInvalidationSourceTick = nameCoordinateInvalidationSourceTick; Ended = ended;
            IsSpecified = true;
        }
    }

    public sealed class SourceAudibleGlobalView
    {
        public ReadOnlyCollection<uint> UniqueAnimationControlMasks { get; }
        public uint BreathControl { get; }
        public bool QuarterbackEvaded { get; }
        public float QuarterbackDropbackUnits { get; }
        public int QuarterbackGainSought { get; }
        public int QuarterbackRollDirection { get; }
        public int MotionDirection { get; }
        public bool QuarterbackHurdlePass { get; }

        public SourceAudibleGlobalView(IReadOnlyList<uint> uniqueAnimationControlMasks, uint breathControl,
            bool quarterbackEvaded, float quarterbackDropbackUnits, int quarterbackGainSought,
            int quarterbackRollDirection, int motionDirection, bool quarterbackHurdlePass)
        {
            if (uniqueAnimationControlMasks == null)
                throw new ArgumentNullException(nameof(uniqueAnimationControlMasks));
            if (uniqueAnimationControlMasks.Count != 6)
                throw new ArgumentException("Observe all six actual animation-control masks.", nameof(uniqueAnimationControlMasks));
            SourceContractGuard.Finite(quarterbackDropbackUnits, nameof(quarterbackDropbackUnits));
            var masks = new uint[6];
            for (int i = 0; i < masks.Length; i++) masks[i] = uniqueAnimationControlMasks[i];
            UniqueAnimationControlMasks = Array.AsReadOnly(masks);
            BreathControl = breathControl; QuarterbackEvaded = quarterbackEvaded;
            QuarterbackDropbackUnits = quarterbackDropbackUnits; QuarterbackGainSought = quarterbackGainSought;
            QuarterbackRollDirection = quarterbackRollDirection; MotionDirection = motionDirection;
            QuarterbackHurdlePass = quarterbackHurdlePass;
        }
    }

    public interface ISourceAudibleObservationState
    {
        SourceAudibleRetentionView AudibleRetention { get; }
        ReadOnlyCollection<SourceAudibleReinitializationCheckpoint> AudibleReinitializations { get; }
    }

    public interface ISourceLiveAnimationObservationState
    {
        ReadOnlyCollection<SourceLiveAnimationWitness> LiveAnimations { get; }
    }

    public readonly struct SourceLiveAnimationWitness
    {
        private readonly int actorPlayerId;
        public bool IsSpecified { get; }
        public int ActorPlayerId { get { return IsSpecified ? actorPlayerId : -1; } }
        public ActorAnimationView Animation { get; }
        public SourceAudibleVmLayerCheckpoint? PrimaryVm { get; }
        public SourceAudibleVmLayerCheckpoint? SecondaryVm { get; }
        public long? PrimaryLastAdvanceSourceTick { get; }
        public long? SecondaryLastAdvanceSourceTick { get; }

        public SourceLiveAnimationWitness(int actorPlayerId, ActorAnimationView animation,
            SourceAudibleVmLayerCheckpoint? primaryVm, SourceAudibleVmLayerCheckpoint? secondaryVm,
            long? primaryLastAdvanceSourceTick, long? secondaryLastAdvanceSourceTick)
        {
            SourceContractGuard.Range(actorPlayerId, 0, SliceRules.PlayersPerTeam * 2 - 1, nameof(actorPlayerId));
            if (animation.SourceTick < 0) throw new ArgumentOutOfRangeException(nameof(animation));
            RequireLayer(animation.Primary, primaryVm, primaryLastAdvanceSourceTick, animation.SourceTick,
                nameof(primaryVm), nameof(primaryLastAdvanceSourceTick));
            RequireLayer(animation.Secondary, secondaryVm, secondaryLastAdvanceSourceTick, animation.SourceTick,
                nameof(secondaryVm), nameof(secondaryLastAdvanceSourceTick));
            this.actorPlayerId = actorPlayerId; Animation = animation;
            PrimaryVm = primaryVm; SecondaryVm = secondaryVm;
            PrimaryLastAdvanceSourceTick = primaryLastAdvanceSourceTick;
            SecondaryLastAdvanceSourceTick = secondaryLastAdvanceSourceTick;
            IsSpecified = true;
        }

        private static void RequireLayer(SourceAnimationLayer animation, SourceAudibleVmLayerCheckpoint? vm,
            long? lastAdvanceSourceTick, long snapshotTick, string vmParameter, string tickParameter)
        {
            if (vm.HasValue && string.IsNullOrWhiteSpace(vm.Value.ProgramSymbol))
                throw new ArgumentException("An unbound VM layer is unavailable, not a zero-valued checkpoint.", vmParameter);
            if (lastAdvanceSourceTick.HasValue && !vm.HasValue)
                throw new ArgumentException("A last-advance tick requires the observed bound VM layer.", tickParameter);
            if (lastAdvanceSourceTick.HasValue &&
                (lastAdvanceSourceTick.Value < 0 || lastAdvanceSourceTick.Value > snapshotTick))
                throw new ArgumentOutOfRangeException(tickParameter);
            if (animation.IsSpecified && vm.HasValue &&
                (animation.ScriptSymbol != vm.Value.ProgramSymbol || animation.SourceModeBits != vm.Value.RawMode))
                throw new ArgumentException("Raw VM state belongs to the same snapshot animation layer.", vmParameter);
        }
    }

    public sealed class SourceAudibleRetentionView
    {
        private readonly object stationTurbo;
        private readonly object stationControls;
        private readonly object kickMeter;
        public bool HasStationTurboBacking { get { return stationTurbo != null; } }
        public bool HasStationControlBacking { get { return stationControls != null; } }
        public bool HasKickMeterProcess { get { return kickMeter != null; } }

        internal SourceAudibleRetentionView(object stationTurbo, object stationControls, object kickMeter)
        {
            this.stationTurbo = stationTurbo;
            this.stationControls = stationControls;
            this.kickMeter = kickMeter;
        }

        public bool SameStationTurboBacking(SourceAudibleRetentionView other)
        {
            return HasStationTurboBacking && other != null && other.HasStationTurboBacking
                && ReferenceEquals(stationTurbo, other.stationTurbo);
        }

        public bool SameStationControlBacking(SourceAudibleRetentionView other)
        {
            return HasStationControlBacking && other != null && other.HasStationControlBacking
                && ReferenceEquals(stationControls, other.stationControls);
        }

        public bool SameKickMeterProcess(SourceAudibleRetentionView other)
        {
            return HasKickMeterProcess && other != null && other.HasKickMeterProcess
                && ReferenceEquals(kickMeter, other.kickMeter);
        }
    }

    public enum SourceAudibleReinitializationPhase
    {
        BeginSide, RosterComplete, PreLineupActorComplete, MasksAndBreathReset,
        SelectedResetComplete, StanceActorComplete, LineupComplete,
        DefensivePreSnapActorComplete, PostInitComplete, ControlsRebuilt,
        StanceAnimationInput = 10, StanceAnimationOutput = 11
    }

    public readonly struct SourceAudibleVmLayerCheckpoint
    {
        public string ProgramSymbol { get; }
        public int LogicalInstructionIndex { get; }
        public uint RawMode { get; }
        public string TransitionSourceSequenceName { get; }
        public int? TransitionSourceOrdinal { get; }
        public int? TransitionCount { get; }
        public int? TransitionTicks { get; }
        public SourceAudibleVmLayerCheckpoint(string programSymbol, int logicalInstructionIndex, uint rawMode)
            : this(programSymbol, logicalInstructionIndex, rawMode, null, null, null, null)
        {
        }
        public SourceAudibleVmLayerCheckpoint(string programSymbol, int logicalInstructionIndex, uint rawMode,
            string transitionSourceSequenceName, int? transitionSourceOrdinal, int? transitionCount, int? transitionTicks)
        {
            if (logicalInstructionIndex < 0) throw new ArgumentOutOfRangeException(nameof(logicalInstructionIndex));
            ProgramSymbol = programSymbol; LogicalInstructionIndex = logicalInstructionIndex; RawMode = rawMode;
            TransitionSourceSequenceName = transitionSourceSequenceName;
            TransitionSourceOrdinal = transitionSourceOrdinal;
            TransitionCount = transitionCount; TransitionTicks = transitionTicks;
        }
    }

    public readonly struct SourceAudibleDbpsActorInput
    {
        public int ActorPlayerId { get; }
        public SourceVector3 Position { get; }
        public FieldVector Velocity { get; }
        public int RawMode { get; }
        public int RawJob { get; }
        public int TargetPlayerId { get; }
        public int RawAiFlags { get; }
        public int Station { get; }
        public int Facing { get; }
        public int Stick { get; }
        public SourceAudibleDbpsActorInput(int actorPlayerId, SourceVector3 position, FieldVector velocity,
            int rawMode, int rawJob, int targetPlayerId, int rawAiFlags, int station, int facing, int stick)
        {
            SourceContractGuard.Range(actorPlayerId, 0, 13, nameof(actorPlayerId));
            SourceContractGuard.Player(targetPlayerId, nameof(targetPlayerId));
            ActorPlayerId = actorPlayerId; Position = position; Velocity = velocity; RawMode = rawMode;
            RawJob = rawJob; TargetPlayerId = targetPlayerId; RawAiFlags = rawAiFlags;
            Station = station; Facing = facing; Stick = stick;
        }
    }

    public readonly struct SourceAudibleDbpsState
    {
        public SourceAudibleDroneView Drone { get; }
        public int MoveTicks { get; }
        public FieldVector ZonePoint { get; }
        public FieldVector ZoneVelocity { get; }
        public float ZoneLowX { get; }
        public float ZoneHighX { get; }
        public float ZoneLowZ { get; }
        public float ZoneHighZ { get; }
        public int Stick { get; }
        public int OldStick { get; }
        public int StickTicks { get; }
        public int Buttons { get; }
        public int ActorRawFlags { get; }
        public int ActorJob { get; }
        public int ActorTarget { get; }
        public int ActorDroneFace { get; }
        public int ActorBackpedalTarget { get; }
        public SourceAudibleDbpsState(SourceAudibleDroneView drone, int moveTicks, FieldVector zonePoint,
            FieldVector zoneVelocity, float zoneLowX, float zoneHighX, float zoneLowZ, float zoneHighZ,
            int stick, int oldStick, int stickTicks, int buttons, int actorRawFlags, int actorJob, int actorTarget,
            int actorDroneFace, int actorBackpedalTarget)
        {
            if (!drone.IsSpecified) throw new ArgumentException("Capture an actual drone state.", nameof(drone));
            Drone = drone; MoveTicks = moveTicks; ZonePoint = zonePoint; ZoneVelocity = zoneVelocity;
            ZoneLowX = zoneLowX; ZoneHighX = zoneHighX; ZoneLowZ = zoneLowZ; ZoneHighZ = zoneHighZ;
            Stick = stick; OldStick = oldStick; StickTicks = stickTicks; Buttons = buttons; ActorRawFlags = actorRawFlags;
            ActorJob = actorJob; ActorTarget = actorTarget; ActorDroneFace = actorDroneFace;
            ActorBackpedalTarget = actorBackpedalTarget;
        }
    }

    public sealed class SourceAudibleDbpsInput
    {
        public long SourceTick { get; }
        public int ActorPlayerId { get; }
        public int CameraHeading { get; }
        public int OffenseSide { get; }
        public int LineOfScrimmage { get; }
        public int DefensePlayFlags { get; }
        public int CarrierPlayerId { get; }
        public int? CarrierAngle { get; }
        public int Side0Handicap { get; }
        public int Side1Handicap { get; }
        public bool UsesSuppliedSourceGeometry { get; }
        public FieldVector FormationOffset { get; }
        public SourceAudibleDbpsState State { get; }
        public ReadOnlyCollection<SourceAudibleDbpsActorInput> Actors { get; }
        public ReadOnlyCollection<SourceAudibleDroneView> Drones { get; }
        public SourceAudibleDbpsInput(long sourceTick, int actorPlayerId, int cameraHeading,
            int offenseSide, int lineOfScrimmage, int defensePlayFlags, int carrierPlayerId, int? carrierAngle,
            int side0Handicap, int side1Handicap, bool usesSuppliedSourceGeometry, FieldVector formationOffset,
            SourceAudibleDbpsState state, IReadOnlyList<SourceAudibleDbpsActorInput> actors,
            IReadOnlyList<SourceAudibleDroneView> drones)
        {
            if (sourceTick < 0) throw new ArgumentOutOfRangeException(nameof(sourceTick));
            SourceContractGuard.Range(actorPlayerId, 0, 13, nameof(actorPlayerId));
            SourceContractGuard.Range(cameraHeading, 0, 1023, nameof(cameraHeading));
            SourceContractGuard.Range(offenseSide, 0, 1, nameof(offenseSide));
            SourceContractGuard.Player(carrierPlayerId, nameof(carrierPlayerId));
            if ((carrierPlayerId >= 0) != carrierAngle.HasValue)
                throw new ArgumentException("Observe the actual cached carrier angle only when a carrier exists.");
            if (actors == null || actors.Count != 14 || drones == null || drones.Count != 14)
                throw new ArgumentException("Freeze all fourteen actual AI actors/drone inputs.");
            var actorCopy = new SourceAudibleDbpsActorInput[14];
            var droneCopy = new SourceAudibleDroneView[14];
            for (int i = 0; i < 14; i++)
            {
                if (actors[i].ActorPlayerId != i || !drones[i].IsSpecified || drones[i].ActorPlayerId != i)
                    throw new ArgumentException("AI inputs retain actual actor indexing.");
                actorCopy[i] = actors[i]; droneCopy[i] = drones[i];
            }
            SourceTick = sourceTick; ActorPlayerId = actorPlayerId; CameraHeading = cameraHeading;
            OffenseSide = offenseSide; LineOfScrimmage = lineOfScrimmage; DefensePlayFlags = defensePlayFlags;
            CarrierPlayerId = carrierPlayerId; CarrierAngle = carrierAngle; Side0Handicap = side0Handicap;
            Side1Handicap = side1Handicap; UsesSuppliedSourceGeometry = usesSuppliedSourceGeometry;
            FormationOffset = formationOffset; State = state;
            Actors = Array.AsReadOnly(actorCopy); Drones = Array.AsReadOnly(droneCopy);
        }
    }

    public sealed class SourceAudibleDbpsCall
    {
        public SourceAudibleDbpsInput Input { get; }
        public SourceAudibleDbpsState Result { get; }
        public SourceAudibleDbpsCall(SourceAudibleDbpsInput input, SourceAudibleDbpsState result)
        {
            if (input == null || result.Drone.ActorPlayerId != input.ActorPlayerId)
                throw new ArgumentException("Pair the actual before-call input and same actor's result.");
            Input = input; Result = result;
        }
    }

    public readonly struct SourceAudibleActorCheckpoint
    {
        public PlayerView Player { get; }
        public SourceVector3 WorldPosition { get; }
        public SourceVector3 Root { get; }
        public SourceVector3 Velocity { get; }
        public FieldVector Move { get; }
        public int Facing { get; }
        public int TargetFacing { get; }
        public int Stick { get; }
        public int Buttons { get; }
        public int TargetPlayerId { get; }
        public int RawJob { get; }
        public int CelebrateTicks { get; }
        public int OverrideTicks { get; }
        public int FormationControl { get; }
        public int FormationMode { get; }
        public string FormationScript { get; }
        public int DroneStick { get; }
        public int DroneStickTicks { get; }
        public SourceAudibleVmLayerCheckpoint? PrimaryVm { get; }
        public SourceAudibleVmLayerCheckpoint? SecondaryVm { get; }
        public int? DroneFace { get; }

        public SourceAudibleActorCheckpoint(PlayerView player, SourceVector3 worldPosition,
            SourceVector3 root, SourceVector3 velocity, FieldVector move, int facing, int targetFacing,
            int stick, int buttons, int targetPlayerId, int rawJob, int celebrateTicks, int overrideTicks,
            int formationControl, int formationMode, string formationScript, int droneStick, int droneStickTicks)
            : this(player, worldPosition, root, velocity, move, facing, targetFacing, stick, buttons,
                targetPlayerId, rawJob, celebrateTicks, overrideTicks, formationControl, formationMode,
                formationScript, droneStick, droneStickTicks, null, null, null)
        {
        }

        public SourceAudibleActorCheckpoint(PlayerView player, SourceVector3 worldPosition,
            SourceVector3 root, SourceVector3 velocity, FieldVector move, int facing, int targetFacing,
            int stick, int buttons, int targetPlayerId, int rawJob, int celebrateTicks, int overrideTicks,
            int formationControl, int formationMode, string formationScript, int droneStick, int droneStickTicks,
            SourceAudibleVmLayerCheckpoint? primaryVm, SourceAudibleVmLayerCheckpoint? secondaryVm, int? droneFace)
        {
            SourceContractGuard.Player(player.Id, nameof(player));
            SourceContractGuard.Player(targetPlayerId, nameof(targetPlayerId));
            Player = player; WorldPosition = worldPosition; Root = root; Velocity = velocity; Move = move;
            Facing = facing; TargetFacing = targetFacing; Stick = stick; Buttons = buttons;
            TargetPlayerId = targetPlayerId; RawJob = rawJob; CelebrateTicks = celebrateTicks;
            OverrideTicks = overrideTicks; FormationControl = formationControl;
            FormationMode = formationMode; FormationScript = formationScript;
            DroneStick = droneStick; DroneStickTicks = droneStickTicks;
            PrimaryVm = primaryVm; SecondaryVm = secondaryVm; DroneFace = droneFace;
        }
    }

    public sealed class SourceAudibleReinitializationCheckpoint : ISourceLiveAnimationObservationState
    {
        public const int MaximumPerWake = 128;
        public long SourceTick { get; }
        public long GameProcessSerial { get; }
        public int ObservationSequence { get; }
        public SourceAudibleReinitializationPhase Phase { get; }
        public int Side { get; }
        public int ActorPlayerId { get; }
        public long RandomDrawOrdinal { get; }
        public string RandomProcess { get; }
        public int RandomActorPlayerId { get; }
        public int RandomProcessOrder { get; }
        public int RandomCallIndex { get; }
        public ReadOnlyCollection<SourceAudibleActorCheckpoint> Actors { get; }
        public ReadOnlyCollection<SourceAudibleDroneView> Drones { get; }
        public SourceAudibleGlobalView Globals { get; }
        public ReadOnlyCollection<SourceStationView> Stations { get; }
        public SourceAudibleRetentionView Retention { get; }
        public SourceAudibleDbpsCall DbpsCall { get; }
        public ReadOnlyCollection<SourceLiveAnimationWitness> LiveAnimations { get; }

        public SourceAudibleReinitializationCheckpoint(long sourceTick, long gameProcessSerial,
            int observationSequence, SourceAudibleReinitializationPhase phase, int side, int actorPlayerId,
            long randomDrawOrdinal, string randomProcess, int randomActorPlayerId, int randomProcessOrder,
            int randomCallIndex, IReadOnlyList<SourceAudibleActorCheckpoint> actors,
            IReadOnlyList<SourceAudibleDroneView> drones, SourceAudibleGlobalView globals,
            IReadOnlyList<SourceStationView> stations, SourceAudibleRetentionView retention)
            : this(sourceTick, gameProcessSerial, observationSequence, phase, side, actorPlayerId,
                randomDrawOrdinal, randomProcess, randomActorPlayerId, randomProcessOrder, randomCallIndex,
                actors, drones, globals, stations, retention, null)
        {
        }

        public SourceAudibleReinitializationCheckpoint(long sourceTick, long gameProcessSerial,
            int observationSequence, SourceAudibleReinitializationPhase phase, int side, int actorPlayerId,
            long randomDrawOrdinal, string randomProcess, int randomActorPlayerId, int randomProcessOrder,
            int randomCallIndex, IReadOnlyList<SourceAudibleActorCheckpoint> actors,
            IReadOnlyList<SourceAudibleDroneView> drones, SourceAudibleGlobalView globals,
            IReadOnlyList<SourceStationView> stations, SourceAudibleRetentionView retention,
            SourceAudibleDbpsCall dbpsCall)
            : this(sourceTick, gameProcessSerial, observationSequence, phase, side, actorPlayerId,
                randomDrawOrdinal, randomProcess, randomActorPlayerId, randomProcessOrder, randomCallIndex,
                actors, drones, globals, stations, retention, dbpsCall, null)
        {
        }

        public SourceAudibleReinitializationCheckpoint(long sourceTick, long gameProcessSerial,
            int observationSequence, SourceAudibleReinitializationPhase phase, int side, int actorPlayerId,
            long randomDrawOrdinal, string randomProcess, int randomActorPlayerId, int randomProcessOrder,
            int randomCallIndex, IReadOnlyList<SourceAudibleActorCheckpoint> actors,
            IReadOnlyList<SourceAudibleDroneView> drones, SourceAudibleGlobalView globals,
            IReadOnlyList<SourceStationView> stations, SourceAudibleRetentionView retention,
            SourceAudibleDbpsCall dbpsCall, IReadOnlyList<SourceLiveAnimationWitness> liveAnimations)
        {
            if (sourceTick < 0) throw new ArgumentOutOfRangeException(nameof(sourceTick));
            if (gameProcessSerial <= 0) throw new ArgumentOutOfRangeException(nameof(gameProcessSerial));
            SourceContractGuard.Range(observationSequence, 0, MaximumPerWake - 1, nameof(observationSequence));
            SourceContractGuard.Range((int)phase, 0, 11, nameof(phase));
            SourceContractGuard.Range(side, -1, 1, nameof(side));
            SourceContractGuard.Player(actorPlayerId, nameof(actorPlayerId));
            SourceContractGuard.Player(randomActorPlayerId, nameof(randomActorPlayerId));
            if (randomDrawOrdinal < 0) throw new ArgumentOutOfRangeException(nameof(randomDrawOrdinal));
            SourceContractGuard.Range(randomProcessOrder, 0, int.MaxValue, nameof(randomProcessOrder));
            SourceContractGuard.Range(randomCallIndex, 0, int.MaxValue, nameof(randomCallIndex));
            if (string.IsNullOrWhiteSpace(randomProcess)) throw new ArgumentException("Observe the current source RNG context.", nameof(randomProcess));
            if (actors == null || actors.Count != 14 || drones == null || drones.Count != 14
                || stations == null || stations.Count > 4 || globals == null || retention == null)
                throw new ArgumentException("Capture actual complete actor/drone/global/station/retention observations.");
            var actorCopy = new SourceAudibleActorCheckpoint[14];
            var droneCopy = new SourceAudibleDroneView[14];
            for (int i = 0; i < 14; i++)
            {
                if (actors[i].Player.Id != i || !drones[i].IsSpecified || drones[i].ActorPlayerId != i)
                    throw new ArgumentException("Checkpoint actors and drones are indexed by actual actor ID.");
                actorCopy[i] = actors[i]; droneCopy[i] = drones[i];
            }
            if (liveAnimations != null)
            {
                if (liveAnimations.Count != 14)
                    throw new ArgumentException("Observe all fourteen actor-indexed animation witnesses.", nameof(liveAnimations));
                var liveCopy = new SourceLiveAnimationWitness[14];
                for (int i = 0; i < liveCopy.Length; i++)
                {
                    SourceLiveAnimationWitness witness = liveAnimations[i];
                    if (!witness.IsSpecified || witness.ActorPlayerId != i ||
                        !witness.Animation.Equals(actorCopy[i].Player.Animation) ||
                        !Nullable.Equals(witness.PrimaryVm, actorCopy[i].PrimaryVm) ||
                        !Nullable.Equals(witness.SecondaryVm, actorCopy[i].SecondaryVm))
                        throw new ArgumentException("Animation witnesses belong to these same actor snapshots.", nameof(liveAnimations));
                    liveCopy[i] = witness;
                }
                LiveAnimations = Array.AsReadOnly(liveCopy);
            }
            var stationCopy = new SourceStationView[stations.Count];
            var seen = new HashSet<int>();
            for (int i = 0; i < stationCopy.Length; i++)
            {
                if (!stations[i].IsSpecified || !seen.Add(stations[i].StationId))
                    throw new ArgumentException("Checkpoint stations must be specified and unique.");
                stationCopy[i] = stations[i];
            }
            SourceTick = sourceTick; GameProcessSerial = gameProcessSerial;
            ObservationSequence = observationSequence; Phase = phase; Side = side;
            ActorPlayerId = actorPlayerId; RandomDrawOrdinal = randomDrawOrdinal;
            RandomProcess = randomProcess; RandomActorPlayerId = randomActorPlayerId;
            RandomProcessOrder = randomProcessOrder; RandomCallIndex = randomCallIndex;
            Actors = Array.AsReadOnly(actorCopy); Drones = Array.AsReadOnly(droneCopy);
            Globals = globals; Stations = Array.AsReadOnly(stationCopy); Retention = retention;
            if (dbpsCall != null && (phase != SourceAudibleReinitializationPhase.DefensivePreSnapActorComplete ||
                dbpsCall.Input.SourceTick != sourceTick || dbpsCall.Input.ActorPlayerId != actorPlayerId))
                throw new ArgumentException("DBPS input/result belongs to this actual call checkpoint.");
            DbpsCall = dbpsCall;
        }
    }

    public sealed class SourceAudibleView
    {
        public const string Contract = "source-default-audibles/v1";
        public long SourceTick { get; }
        public SourceAudibleTeamView HomeTeam { get; }
        public SourceAudibleTeamView AwayTeam { get; }
        public SourceAudibleRoleView Offense { get; }
        public SourceAudibleRoleView Defense { get; }
        public bool OriginalOffenseFlipped { get; }
        public bool OffenseFlipped { get; }
        public SourceProcessView GameProcess { get; }
        public SourceAudibleRestartStage RestartStage { get; }
        public ReadOnlyCollection<SourceDigitalSwitchScan> DigitalScans { get; }
        public ReadOnlyCollection<SourceAudibleSpriteView> Sprites { get; }
        public ReadOnlyCollection<SourceAudibleFlashView> FlashProcesses { get; }
        public ReadOnlyCollection<SourceDirectSoundCall> DirectSounds { get; }
        public ReadOnlyCollection<SourceAudibleDroneView> Drones { get; }
        public SourceAudibleGlobalView Globals { get; }
        public ReadOnlyCollection<int> ActorButtons { get; }

        public SourceAudibleView(long sourceTick, SourceAudibleTeamView homeTeam, SourceAudibleTeamView awayTeam,
            SourceAudibleRoleView offense, SourceAudibleRoleView defense,
            bool originalOffenseFlipped, bool offenseFlipped, SourceProcessView gameProcess,
            SourceAudibleRestartStage restartStage, IReadOnlyList<SourceDigitalSwitchScan> digitalScans,
            IReadOnlyList<SourceAudibleSpriteView> sprites, IReadOnlyList<SourceAudibleFlashView> flashProcesses,
            IReadOnlyList<SourceDirectSoundCall> directSounds,
            IReadOnlyList<SourceAudibleDroneView> drones = null, SourceAudibleGlobalView globals = null,
            IReadOnlyList<int> actorButtons = null)
        {
            if (sourceTick < 0) throw new ArgumentOutOfRangeException(nameof(sourceTick));
            if (!homeTeam.IsSpecified || homeTeam.Team != TeamId.Home
                || !awayTeam.IsSpecified || awayTeam.Team != TeamId.Away)
                throw new ArgumentException("Team audible state is indexed by actual home/away team, not role.");
            if (!offense.IsSpecified || offense.Role != SourceAudibleRole.Offense
                || !defense.IsSpecified || defense.Role != SourceAudibleRole.Defense || offense.Team == defense.Team)
                throw new ArgumentException("Offense and defense audible roles must map to different actual teams.");
            if (gameProcess.Serial <= 0 || gameProcess.Function != "game_proc")
                throw new ArgumentException("Observe the actual game_proc continuation.", nameof(gameProcess));
            SourceContractGuard.Range((int)restartStage, 0, 3, nameof(restartStage));
            if (digitalScans == null || sprites == null || flashProcesses == null || directSounds == null)
                throw new ArgumentNullException("collections", "Audible observations must be explicitly supplied, including known-empty lists.");
            var scanCopy = new SourceDigitalSwitchScan[digitalScans.Count];
            var stations = new HashSet<int>();
            for (int i = 0; i < scanCopy.Length; i++)
            {
                SourceDigitalSwitchScan scan = digitalScans[i];
                if (!scan.IsSpecified || scan.SourceTick != sourceTick || !stations.Add(scan.StationId))
                    throw new ArgumentException("Publish one current source scan per observed physical station.", nameof(digitalScans));
                scanCopy[i] = scan;
            }
            var spriteCopy = new SourceAudibleSpriteView[sprites.Count];
            var objects = new Dictionary<long, SourceAudibleSpriteView>();
            for (int i = 0; i < spriteCopy.Length; i++)
            {
                SourceAudibleSpriteView sprite = sprites[i];
                if (sprite == null || objects.ContainsKey(sprite.ObjectSerial))
                    throw new ArgumentException("Observe each actual sprite object reference once.", nameof(sprites));
                objects.Add(sprite.ObjectSerial, sprite); spriteCopy[i] = sprite;
            }
            RequireBackground(homeTeam, objects); RequireBackground(awayTeam, objects);
            var flashCopy = new SourceAudibleFlashView[flashProcesses.Count];
            var children = new HashSet<long>();
            for (int i = 0; i < flashCopy.Length; i++)
            {
                SourceAudibleFlashView flash = flashProcesses[i];
                SourceAudibleSpriteView sprite;
                if (flash == null || !children.Add(flash.Process.Serial)
                    || !objects.TryGetValue(flash.SpriteObjectSerial, out sprite) || sprite.Team != flash.Team
                    || flash.EntrySourceTick > sourceTick || flash.LastMembershipCheckTick > sourceTick)
                    throw new ArgumentException("A child borrows the same observed team sprite, never a replacement blink object.", nameof(flashProcesses));
                flashCopy[i] = flash;
            }
            var soundCopy = new SourceDirectSoundCall[directSounds.Count];
            SourceDirectSoundCall previous = null;
            for (int i = 0; i < soundCopy.Length; i++)
            {
                SourceDirectSoundCall call = directSounds[i];
                if (call == null || call.SourceTick > sourceTick || previous != null &&
                    (call.Sequence <= previous.Sequence || call.SourceTick < previous.SourceTick
                        || call.SourceTick == previous.SourceTick && (call.DispatchOrder < previous.DispatchOrder
                            || call.DispatchOrder == previous.DispatchOrder && call.SubcallOrder <= previous.SubcallOrder)))
                    throw new ArgumentException("Direct calls retain unique monotonic sequence and actual dispatch/subcall order.", nameof(directSounds));
                soundCopy[i] = call; previous = call;
            }
            if (drones != null)
            {
                if (drones.Count != SliceRules.PlayersPerTeam * 2)
                    throw new ArgumentException("Observe all fourteen actual drones or leave them unobserved.", nameof(drones));
                var droneCopy = new SourceAudibleDroneView[drones.Count];
                for (int i = 0; i < droneCopy.Length; i++)
                {
                    SourceAudibleDroneView drone = drones[i];
                    if (!drone.IsSpecified || drone.ActorPlayerId != i)
                        throw new ArgumentException("Observe each specified drone once, in actual actor order.", nameof(drones));
                    droneCopy[i] = drone;
                }
                Drones = Array.AsReadOnly(droneCopy);
            }
            if (actorButtons != null)
            {
                if (actorButtons.Count != SliceRules.PlayersPerTeam * 2)
                    throw new ArgumentException("Observe all fourteen actual actor button masks or leave them unobserved.",
                        nameof(actorButtons));
                var buttonCopy = new int[actorButtons.Count];
                for (int i = 0; i < buttonCopy.Length; i++) buttonCopy[i] = actorButtons[i];
                ActorButtons = Array.AsReadOnly(buttonCopy);
            }
            SourceTick = sourceTick; HomeTeam = homeTeam; AwayTeam = awayTeam; Offense = offense; Defense = defense;
            OriginalOffenseFlipped = originalOffenseFlipped; OffenseFlipped = offenseFlipped;
            GameProcess = gameProcess; RestartStage = restartStage;
            DigitalScans = Array.AsReadOnly(scanCopy); Sprites = Array.AsReadOnly(spriteCopy);
            FlashProcesses = Array.AsReadOnly(flashCopy); DirectSounds = Array.AsReadOnly(soundCopy);
            Globals = globals;
        }

        private static void RequireBackground(SourceAudibleTeamView team,
            Dictionary<long, SourceAudibleSpriteView> objects)
        {
            if (!team.BackgroundSpriteObjectSerial.HasValue) return;
            SourceAudibleSpriteView sprite;
            if (!objects.TryGetValue(team.BackgroundSpriteObjectSerial.Value, out sprite) || sprite.Team != team.Team)
                throw new ArgumentException("The team's background must be its actual observed sprite reference.");
        }
    }

    public sealed class SourcePlayActivityStatsView
    {
        public int HomeOffensivePlays { get; }
        public int AwayOffensivePlays { get; }
        public int HomePassAttempts { get; }
        public int AwayPassAttempts { get; }
        // Original detect.c counts only human tacklers stopping a carrier who retains the ball.
        public int HomeTackles { get; }
        public int AwayTackles { get; }
        public int HomeFumbles { get; }
        public int AwayFumbles { get; }
        public int HomeLostFumbles { get; }
        public int AwayLostFumbles { get; }
        public int HomeBobbles { get; }
        public int AwayBobbles { get; }
        public int HomeDeflections { get; }
        public int AwayDeflections { get; }

        public SourcePlayActivityStatsView(int homeOffensivePlays, int awayOffensivePlays,
            int homePassAttempts, int awayPassAttempts, int homeTackles, int awayTackles,
            int homeFumbles, int awayFumbles, int homeLostFumbles, int awayLostFumbles,
            int homeBobbles, int awayBobbles, int homeDeflections, int awayDeflections)
        {
            if (homeOffensivePlays < 0) throw new ArgumentOutOfRangeException(nameof(homeOffensivePlays));
            if (awayOffensivePlays < 0) throw new ArgumentOutOfRangeException(nameof(awayOffensivePlays));
            if (homePassAttempts < 0) throw new ArgumentOutOfRangeException(nameof(homePassAttempts));
            if (awayPassAttempts < 0) throw new ArgumentOutOfRangeException(nameof(awayPassAttempts));
            if (homeTackles < 0) throw new ArgumentOutOfRangeException(nameof(homeTackles));
            if (awayTackles < 0) throw new ArgumentOutOfRangeException(nameof(awayTackles));
            if (homeFumbles < 0) throw new ArgumentOutOfRangeException(nameof(homeFumbles));
            if (awayFumbles < 0) throw new ArgumentOutOfRangeException(nameof(awayFumbles));
            if (homeLostFumbles < 0) throw new ArgumentOutOfRangeException(nameof(homeLostFumbles));
            if (awayLostFumbles < 0) throw new ArgumentOutOfRangeException(nameof(awayLostFumbles));
            if (homeBobbles < 0) throw new ArgumentOutOfRangeException(nameof(homeBobbles));
            if (awayBobbles < 0) throw new ArgumentOutOfRangeException(nameof(awayBobbles));
            if (homeDeflections < 0) throw new ArgumentOutOfRangeException(nameof(homeDeflections));
            if (awayDeflections < 0) throw new ArgumentOutOfRangeException(nameof(awayDeflections));
            HomeOffensivePlays = homeOffensivePlays; AwayOffensivePlays = awayOffensivePlays;
            HomePassAttempts = homePassAttempts; AwayPassAttempts = awayPassAttempts;
            HomeTackles = homeTackles; AwayTackles = awayTackles;
            HomeFumbles = homeFumbles; AwayFumbles = awayFumbles;
            HomeLostFumbles = homeLostFumbles; AwayLostFumbles = awayLostFumbles;
            HomeBobbles = homeBobbles; AwayBobbles = awayBobbles;
            HomeDeflections = homeDeflections; AwayDeflections = awayDeflections;
        }
    }

    public sealed class SourceScoringStatsView
    {
        // Mirrors tdata.safetys on the post-result off_side, not the side awarded points.
        public int HomeSafeties { get; }
        public int AwaySafeties { get; }
        public int HomeTwoPointAttempts { get; }
        public int AwayTwoPointAttempts { get; }
        public int HomeTwoPointConversions { get; }
        public int AwayTwoPointConversions { get; }

        public SourceScoringStatsView(int homeSafeties, int awaySafeties,
            int homeTwoPointAttempts, int awayTwoPointAttempts,
            int homeTwoPointConversions, int awayTwoPointConversions)
        {
            if (homeSafeties < 0) throw new ArgumentOutOfRangeException(nameof(homeSafeties));
            if (awaySafeties < 0) throw new ArgumentOutOfRangeException(nameof(awaySafeties));
            if (homeTwoPointAttempts < 0) throw new ArgumentOutOfRangeException(nameof(homeTwoPointAttempts));
            if (awayTwoPointAttempts < 0) throw new ArgumentOutOfRangeException(nameof(awayTwoPointAttempts));
            if (homeTwoPointConversions < 0) throw new ArgumentOutOfRangeException(nameof(homeTwoPointConversions));
            if (awayTwoPointConversions < 0) throw new ArgumentOutOfRangeException(nameof(awayTwoPointConversions));
            HomeSafeties = homeSafeties; AwaySafeties = awaySafeties;
            HomeTwoPointAttempts = homeTwoPointAttempts; AwayTwoPointAttempts = awayTwoPointAttempts;
            HomeTwoPointConversions = homeTwoPointConversions; AwayTwoPointConversions = awayTwoPointConversions;
        }
    }

    public sealed class SourceTouchdownStatsView
    {
        public int HomeTouchdowns { get; }
        public int AwayTouchdowns { get; }

        public SourceTouchdownStatsView(int homeTouchdowns, int awayTouchdowns)
        {
            if (homeTouchdowns < 0)
                throw new ArgumentOutOfRangeException(nameof(homeTouchdowns));
            if (awayTouchdowns < 0)
                throw new ArgumentOutOfRangeException(nameof(awayTouchdowns));
            HomeTouchdowns = homeTouchdowns;
            AwayTouchdowns = awayTouchdowns;
        }
    }

    public sealed class SourceFourthDownAttemptsView
    {
        public int HomeFourthDownAttempts { get; }
        public int AwayFourthDownAttempts { get; }

        public SourceFourthDownAttemptsView(int homeFourthDownAttempts, int awayFourthDownAttempts)
        {
            if (homeFourthDownAttempts < 0)
                throw new ArgumentOutOfRangeException(nameof(homeFourthDownAttempts));
            if (awayFourthDownAttempts < 0)
                throw new ArgumentOutOfRangeException(nameof(awayFourthDownAttempts));
            HomeFourthDownAttempts = homeFourthDownAttempts;
            AwayFourthDownAttempts = awayFourthDownAttempts;
        }
    }

    public sealed class SourceResultProgressionStatsView
    {
        public int HomeFirstDowns { get; }
        public int AwayFirstDowns { get; }
        public int HomeFourthDownConversions { get; }
        public int AwayFourthDownConversions { get; }

        public SourceResultProgressionStatsView(int homeFirstDowns, int awayFirstDowns,
            int homeFourthDownConversions, int awayFourthDownConversions)
        {
            if (homeFirstDowns < 0)
                throw new ArgumentOutOfRangeException(nameof(homeFirstDowns));
            if (awayFirstDowns < 0)
                throw new ArgumentOutOfRangeException(nameof(awayFirstDowns));
            if (homeFourthDownConversions < 0)
                throw new ArgumentOutOfRangeException(nameof(homeFourthDownConversions));
            if (awayFourthDownConversions < 0)
                throw new ArgumentOutOfRangeException(nameof(awayFourthDownConversions));
            HomeFirstDowns = homeFirstDowns;
            AwayFirstDowns = awayFirstDowns;
            HomeFourthDownConversions = homeFourthDownConversions;
            AwayFourthDownConversions = awayFourthDownConversions;
        }
    }

    public sealed class SourcePlaceKickStatsView
    {
        public int HomeFieldGoalAttempts { get; }
        public int AwayFieldGoalAttempts { get; }
        public int HomeFieldGoals { get; }
        public int AwayFieldGoals { get; }

        public SourcePlaceKickStatsView(int homeFieldGoalAttempts, int awayFieldGoalAttempts,
            int homeFieldGoals, int awayFieldGoals)
        {
            if (homeFieldGoalAttempts < 0)
                throw new ArgumentOutOfRangeException(nameof(homeFieldGoalAttempts));
            if (awayFieldGoalAttempts < 0)
                throw new ArgumentOutOfRangeException(nameof(awayFieldGoalAttempts));
            if (homeFieldGoals < 0)
                throw new ArgumentOutOfRangeException(nameof(homeFieldGoals));
            if (awayFieldGoals < 0)
                throw new ArgumentOutOfRangeException(nameof(awayFieldGoals));
            HomeFieldGoalAttempts = homeFieldGoalAttempts;
            AwayFieldGoalAttempts = awayFieldGoalAttempts;
            HomeFieldGoals = homeFieldGoals;
            AwayFieldGoals = awayFieldGoals;
        }
    }

    public sealed class SourcePlayEndStatsView
    {
        public int HomeReturnYards { get; }
        public int AwayReturnYards { get; }
        public int HomeKickReturns { get; }
        public int AwayKickReturns { get; }
        public int HomeRushingYards { get; }
        public int AwayRushingYards { get; }
        public int HomeRushes { get; }
        public int AwayRushes { get; }
        public int HomeSackYards { get; }
        public int AwaySackYards { get; }
        public int HomeSacks { get; }
        public int AwaySacks { get; }

        public SourcePlayEndStatsView(int homeReturnYards, int awayReturnYards,
            int homeKickReturns, int awayKickReturns, int homeRushingYards, int awayRushingYards,
            int homeRushes, int awayRushes, int homeSackYards, int awaySackYards,
            int homeSacks, int awaySacks)
        {
            if (homeKickReturns < 0)
                throw new ArgumentOutOfRangeException(nameof(homeKickReturns));
            if (awayKickReturns < 0)
                throw new ArgumentOutOfRangeException(nameof(awayKickReturns));
            if (homeRushes < 0)
                throw new ArgumentOutOfRangeException(nameof(homeRushes));
            if (awayRushes < 0)
                throw new ArgumentOutOfRangeException(nameof(awayRushes));
            if (homeSacks < 0)
                throw new ArgumentOutOfRangeException(nameof(homeSacks));
            if (awaySacks < 0)
                throw new ArgumentOutOfRangeException(nameof(awaySacks));
            HomeReturnYards = homeReturnYards;
            AwayReturnYards = awayReturnYards;
            HomeKickReturns = homeKickReturns;
            AwayKickReturns = awayKickReturns;
            HomeRushingYards = homeRushingYards;
            AwayRushingYards = awayRushingYards;
            HomeRushes = homeRushes;
            AwayRushes = awayRushes;
            HomeSackYards = homeSackYards;
            AwaySackYards = awaySackYards;
            HomeSacks = homeSacks;
            AwaySacks = awaySacks;
        }
    }

    public sealed class SourceCompletedPassStatsView
    {
        public int HomePassingYards { get; }
        public int AwayPassingYards { get; }
        public int HomeTotalYards { get; }
        public int AwayTotalYards { get; }
        public int HomePassTouchdowns { get; }
        public int AwayPassTouchdowns { get; }

        public SourceCompletedPassStatsView(int homePassingYards, int awayPassingYards,
            int homeTotalYards, int awayTotalYards, int homePassTouchdowns, int awayPassTouchdowns)
        {
            if (homePassTouchdowns < 0)
                throw new ArgumentOutOfRangeException(nameof(homePassTouchdowns));
            if (awayPassTouchdowns < 0)
                throw new ArgumentOutOfRangeException(nameof(awayPassTouchdowns));
            HomePassingYards = homePassingYards;
            AwayPassingYards = awayPassingYards;
            HomeTotalYards = homeTotalYards;
            AwayTotalYards = awayTotalYards;
            HomePassTouchdowns = homePassTouchdowns;
            AwayPassTouchdowns = awayPassTouchdowns;
        }
    }

    public sealed class SourcePassCompletionsView
    {
        public int HomeCompletions { get; }
        public int AwayCompletions { get; }

        public SourcePassCompletionsView(int homeCompletions, int awayCompletions)
        {
            if (homeCompletions < 0)
                throw new ArgumentOutOfRangeException(nameof(homeCompletions));
            if (awayCompletions < 0)
                throw new ArgumentOutOfRangeException(nameof(awayCompletions));
            HomeCompletions = homeCompletions;
            AwayCompletions = awayCompletions;
        }
    }

    public sealed class SourceInterceptionsView
    {
        public int HomeInterceptionsMade { get; }
        public int AwayInterceptionsMade { get; }

        public SourceInterceptionsView(int homeInterceptionsMade, int awayInterceptionsMade)
        {
            HomeInterceptionsMade = homeInterceptionsMade;
            AwayInterceptionsMade = awayInterceptionsMade;
        }
    }

    public sealed class SourceTimeOfPossessionView
    {
        public int HomeSeconds { get; }
        public int AwaySeconds { get; }

        public SourceTimeOfPossessionView(int homeSeconds, int awaySeconds)
        {
            if (homeSeconds < 0)
                throw new ArgumentOutOfRangeException(nameof(homeSeconds));
            if (awaySeconds < 0)
                throw new ArgumentOutOfRangeException(nameof(awaySeconds));
            HomeSeconds = homeSeconds;
            AwaySeconds = awaySeconds;
        }
    }

    public sealed class SourceMatchView
    {
        public SourceMatchConfiguration Configuration { get; }
        public SourcePlayKind PlayKind { get; }
        public SourceMatchPhase Phase { get; }
        public SourceMatchResult Result { get; }
        public SourcePeriodView Period { get; }
        public SourcePossessionView Possession { get; }
        public SourcePlaySelection HomePlay { get; }
        public SourcePlaySelection AwayPlay { get; }
        public int SelectionCountdown { get; }
        public SourceSelectionTimerView SelectionTimer { get; }
        public SourceRandomSelectionView HomeRandomSelection { get; }
        public SourceRandomSelectionView AwayRandomSelection { get; }
        public SourcePatOption PatChoice { get; }
        public SourcePatStatus PatStatus { get; }
        public SourceKickoffType? Kickoff { get; }
        public TeamId? KickingTeam { get; }
        public TeamId? ReceivingTeam { get; }
        public bool? OffenseFlipped { get; }
        public SourceTeamGameplayView HomeGameplay { get; }
        public SourceTeamGameplayView AwayGameplay { get; }
        public int ForwardCatcherPlayerId { get; }
        public bool DronesLose { get; }
        public bool DronesWinning { get; }
        public int HandicapRepetitionBonus { get; }
        public SourceGameplayCompatibility Compatibility { get; }
        public long CompatibilityRepairCount { get; }
        public ReadOnlyCollection<SourceCompatibilityRepair> CompatibilityRepairs { get; }
        public ReadOnlyCollection<SourceQuarterScore> QuarterScores { get; }
        public ReadOnlyCollection<SourceStationView> Stations { get; }
        public SourceAudibleView Audibles { get; }
        public SourceTimeOfPossessionView TimeOfPossession { get; }
        public SourceInterceptionsView Interceptions { get; }
        public SourcePassCompletionsView PassCompletions { get; }
        public SourceCompletedPassStatsView CompletedPassStats { get; }
        public SourcePlayEndStatsView PlayEndStats { get; }
        public SourcePlaceKickStatsView PlaceKickStats { get; }
        public SourceResultProgressionStatsView ResultProgressionStats { get; }
        public SourceFourthDownAttemptsView FourthDownAttempts { get; }
        public SourceTouchdownStatsView TouchdownStats { get; }
        public SourcePlayActivityStatsView PlayActivityStats { get; }
        public SourceScoringStatsView ScoringStats { get; }

        public SourceMatchView(SourceMatchConfiguration configuration, SourcePlayKind playKind,
            SourceMatchPhase phase, SourcePeriodView period, SourcePossessionView possession,
            IReadOnlyList<SourceQuarterScore> quarterScores, IReadOnlyList<SourceStationView> stations,
            SourceMatchResult result = default(SourceMatchResult),
            SourcePlaySelection homePlay = default(SourcePlaySelection),
            SourcePlaySelection awayPlay = default(SourcePlaySelection),
            int selectionCountdown = -1, SourcePatOption patChoice = SourcePatOption.Unselected,
            SourcePatStatus patStatus = SourcePatStatus.NotExtraPoint, SourceKickoffType? kickoff = null,
            TeamId? kickingTeam = null, TeamId? receivingTeam = null, bool? offenseFlipped = null,
            SourceTeamGameplayView homeGameplay = default(SourceTeamGameplayView),
            SourceTeamGameplayView awayGameplay = default(SourceTeamGameplayView),
            int forwardCatcherPlayerId = -1, bool dronesLose = false, bool dronesWinning = false,
            int handicapRepetitionBonus = 0,
            SourceGameplayCompatibility compatibility = SourceGameplayCompatibility.StrictSource,
            long compatibilityRepairCount = 0, IReadOnlyList<SourceCompatibilityRepair> compatibilityRepairs = null,
            SourceSelectionTimerView selectionTimer = null,
            SourceRandomSelectionView homeRandomSelection = default(SourceRandomSelectionView),
            SourceRandomSelectionView awayRandomSelection = default(SourceRandomSelectionView),
            SourceAudibleView audibles = null)
        {
            if (configuration == null)
                throw new ArgumentNullException(nameof(configuration));
            SourceContractGuard.Range((int)playKind, 0, 4, nameof(playKind));
            SourceContractGuard.Range((int)phase, 0, 8, nameof(phase));
            if (!period.IsSpecified)
                throw new ArgumentException("The source period must be specified.", nameof(period));
            if (!possession.IsSpecified)
                throw new ArgumentException("Source possession must be specified.", nameof(possession));
            if (audibles != null && audibles.Offense.Team != possession.Offense)
                throw new ArgumentException("Audible role ownership must match actual possession.", nameof(audibles));
            if (quarterScores == null)
                throw new ArgumentNullException(nameof(quarterScores));
            if (stations == null)
                throw new ArgumentNullException(nameof(stations));
            if (quarterScores.Count > SourceMatchLimits.MaximumPeriodCount)
                throw new ArgumentException("There are at most seven source periods.", nameof(quarterScores));
            if (stations.Count > configuration.Stations.Count)
                throw new ArgumentException("Station views must belong to configured stations.", nameof(stations));
            SourceContractGuard.Range(selectionCountdown, -1, int.MaxValue, nameof(selectionCountdown));
            SourceContractGuard.Range((int)patChoice, 0, 2, nameof(patChoice));
            SourceContractGuard.Range((int)patStatus, 0, 5, nameof(patStatus));
            if (kickoff.HasValue)
                SourceContractGuard.Range((int)kickoff.Value, 0, 2, nameof(kickoff));
            if (kickingTeam.HasValue)
                SourceContractGuard.Team(kickingTeam.Value, nameof(kickingTeam));
            if (receivingTeam.HasValue)
                SourceContractGuard.Team(receivingTeam.Value, nameof(receivingTeam));
            if (kickingTeam.HasValue && receivingTeam.HasValue && kickingTeam.Value == receivingTeam.Value)
                throw new ArgumentException("Kicking and receiving teams must differ.", nameof(receivingTeam));
            if (homeGameplay.IsSpecified && homeGameplay.Team != TeamId.Home
                || awayGameplay.IsSpecified && awayGameplay.Team != TeamId.Away)
                throw new ArgumentException("Gameplay views must retain home/away team ownership.");
            SourceContractGuard.Player(forwardCatcherPlayerId, nameof(forwardCatcherPlayerId));
            SourceContractGuard.Range(handicapRepetitionBonus, 0, 5, nameof(handicapRepetitionBonus));
            SourceContractGuard.Range((int)compatibility, 0, 1, nameof(compatibility));
            if (compatibilityRepairCount < 0) throw new ArgumentOutOfRangeException(nameof(compatibilityRepairCount));
            var repairCopy = new SourceCompatibilityRepair[compatibilityRepairs == null ? 0 : compatibilityRepairs.Count];
            for (int i = 0; i < repairCopy.Length; i++) repairCopy[i] = compatibilityRepairs[i];
            var scoresCopy = new SourceQuarterScore[quarterScores.Count];
            int previousQuarter = -1;
            for (int i = 0; i < scoresCopy.Length; i++)
            {
                SourceQuarterScore score = quarterScores[i];
                if (!score.IsSpecified || score.QuarterIndex <= previousQuarter)
                    throw new ArgumentException("Period scores must be specified, unique and in period order.", nameof(quarterScores));
                previousQuarter = score.QuarterIndex;
                scoresCopy[i] = score;
            }
            var stationsCopy = new SourceStationView[stations.Count];
            var seen = new bool[SourceMatchLimits.MaximumHumanStations];
            for (int i = 0; i < stationsCopy.Length; i++)
            {
                SourceStationView station = stations[i];
                if (!station.IsSpecified || seen[station.StationId])
                    throw new ArgumentException("Station views must be specified and unique.", nameof(stations));
                bool configured = false;
                foreach (SourceStationConfiguration choice in configuration.Stations)
                {
                    if (choice.StationId != station.StationId)
                        continue;
                    configured = true;
                    if (station.ControlledPlayerId >= 0 && station.ControlledPlayerId / SliceRules.PlayersPerTeam != (int)choice.Team)
                        throw new ArgumentException("A controlled actor must belong to its station's team.", nameof(stations));
                    break;
                }
                if (!configured)
                    throw new ArgumentException("Station views must belong to configured stations.", nameof(stations));
                seen[station.StationId] = true;
                stationsCopy[i] = station;
            }
            Configuration = configuration;
            PlayKind = playKind;
            Phase = phase;
            Result = result;
            Period = period;
            Possession = possession;
            HomePlay = homePlay;
            AwayPlay = awayPlay;
            SelectionCountdown = selectionCountdown;
            SelectionTimer = selectionTimer;
            HomeRandomSelection = homeRandomSelection;
            AwayRandomSelection = awayRandomSelection;
            PatChoice = patChoice;
            PatStatus = patStatus;
            Kickoff = kickoff;
            KickingTeam = kickingTeam;
            ReceivingTeam = receivingTeam;
            OffenseFlipped = offenseFlipped;
            HomeGameplay = homeGameplay;
            AwayGameplay = awayGameplay;
            ForwardCatcherPlayerId = forwardCatcherPlayerId;
            DronesLose = dronesLose;
            DronesWinning = dronesWinning;
            HandicapRepetitionBonus = handicapRepetitionBonus;
            Compatibility = compatibility;
            CompatibilityRepairCount = compatibilityRepairCount;
            CompatibilityRepairs = Array.AsReadOnly(repairCopy);
            QuarterScores = Array.AsReadOnly(scoresCopy);
            Stations = Array.AsReadOnly(stationsCopy);
            Audibles = audibles;
        }

        public SourceMatchView(SourceTimeOfPossessionView timeOfPossession,
            SourceMatchConfiguration configuration, SourcePlayKind playKind,
            SourceMatchPhase phase, SourcePeriodView period, SourcePossessionView possession,
            IReadOnlyList<SourceQuarterScore> quarterScores, IReadOnlyList<SourceStationView> stations,
            SourceMatchResult result = default(SourceMatchResult),
            SourcePlaySelection homePlay = default(SourcePlaySelection),
            SourcePlaySelection awayPlay = default(SourcePlaySelection),
            int selectionCountdown = -1, SourcePatOption patChoice = SourcePatOption.Unselected,
            SourcePatStatus patStatus = SourcePatStatus.NotExtraPoint, SourceKickoffType? kickoff = null,
            TeamId? kickingTeam = null, TeamId? receivingTeam = null, bool? offenseFlipped = null,
            SourceTeamGameplayView homeGameplay = default(SourceTeamGameplayView),
            SourceTeamGameplayView awayGameplay = default(SourceTeamGameplayView),
            int forwardCatcherPlayerId = -1, bool dronesLose = false, bool dronesWinning = false,
            int handicapRepetitionBonus = 0,
            SourceGameplayCompatibility compatibility = SourceGameplayCompatibility.StrictSource,
            long compatibilityRepairCount = 0, IReadOnlyList<SourceCompatibilityRepair> compatibilityRepairs = null,
            SourceSelectionTimerView selectionTimer = null,
            SourceRandomSelectionView homeRandomSelection = default(SourceRandomSelectionView),
            SourceRandomSelectionView awayRandomSelection = default(SourceRandomSelectionView),
            SourceAudibleView audibles = null)
            : this(configuration, playKind, phase, period, possession, quarterScores, stations,
                result, homePlay, awayPlay, selectionCountdown, patChoice, patStatus, kickoff,
                kickingTeam, receivingTeam, offenseFlipped, homeGameplay, awayGameplay,
                forwardCatcherPlayerId, dronesLose, dronesWinning, handicapRepetitionBonus,
                compatibility, compatibilityRepairCount, compatibilityRepairs, selectionTimer,
                homeRandomSelection, awayRandomSelection, audibles)
        {
            if (timeOfPossession == null)
                throw new ArgumentNullException(nameof(timeOfPossession));
            TimeOfPossession = timeOfPossession;
        }

        public SourceMatchView(SourceInterceptionsView interceptions, SourceTimeOfPossessionView timeOfPossession,
            SourceMatchConfiguration configuration, SourcePlayKind playKind,
            SourceMatchPhase phase, SourcePeriodView period, SourcePossessionView possession,
            IReadOnlyList<SourceQuarterScore> quarterScores, IReadOnlyList<SourceStationView> stations,
            SourceMatchResult result = default(SourceMatchResult),
            SourcePlaySelection homePlay = default(SourcePlaySelection),
            SourcePlaySelection awayPlay = default(SourcePlaySelection),
            int selectionCountdown = -1, SourcePatOption patChoice = SourcePatOption.Unselected,
            SourcePatStatus patStatus = SourcePatStatus.NotExtraPoint, SourceKickoffType? kickoff = null,
            TeamId? kickingTeam = null, TeamId? receivingTeam = null, bool? offenseFlipped = null,
            SourceTeamGameplayView homeGameplay = default(SourceTeamGameplayView),
            SourceTeamGameplayView awayGameplay = default(SourceTeamGameplayView),
            int forwardCatcherPlayerId = -1, bool dronesLose = false, bool dronesWinning = false,
            int handicapRepetitionBonus = 0,
            SourceGameplayCompatibility compatibility = SourceGameplayCompatibility.StrictSource,
            long compatibilityRepairCount = 0, IReadOnlyList<SourceCompatibilityRepair> compatibilityRepairs = null,
            SourceSelectionTimerView selectionTimer = null,
            SourceRandomSelectionView homeRandomSelection = default(SourceRandomSelectionView),
            SourceRandomSelectionView awayRandomSelection = default(SourceRandomSelectionView),
            SourceAudibleView audibles = null)
            : this(timeOfPossession, configuration, playKind, phase, period, possession, quarterScores, stations,
                result, homePlay, awayPlay, selectionCountdown, patChoice, patStatus, kickoff,
                kickingTeam, receivingTeam, offenseFlipped, homeGameplay, awayGameplay,
                forwardCatcherPlayerId, dronesLose, dronesWinning, handicapRepetitionBonus,
                compatibility, compatibilityRepairCount, compatibilityRepairs, selectionTimer,
                homeRandomSelection, awayRandomSelection, audibles)
        {
            if (interceptions == null)
                throw new ArgumentNullException(nameof(interceptions));
            Interceptions = interceptions;
        }

        public SourceMatchView(SourcePassCompletionsView passCompletions, SourceInterceptionsView interceptions,
            SourceTimeOfPossessionView timeOfPossession,
            SourceMatchConfiguration configuration, SourcePlayKind playKind,
            SourceMatchPhase phase, SourcePeriodView period, SourcePossessionView possession,
            IReadOnlyList<SourceQuarterScore> quarterScores, IReadOnlyList<SourceStationView> stations,
            SourceMatchResult result = default(SourceMatchResult),
            SourcePlaySelection homePlay = default(SourcePlaySelection),
            SourcePlaySelection awayPlay = default(SourcePlaySelection),
            int selectionCountdown = -1, SourcePatOption patChoice = SourcePatOption.Unselected,
            SourcePatStatus patStatus = SourcePatStatus.NotExtraPoint, SourceKickoffType? kickoff = null,
            TeamId? kickingTeam = null, TeamId? receivingTeam = null, bool? offenseFlipped = null,
            SourceTeamGameplayView homeGameplay = default(SourceTeamGameplayView),
            SourceTeamGameplayView awayGameplay = default(SourceTeamGameplayView),
            int forwardCatcherPlayerId = -1, bool dronesLose = false, bool dronesWinning = false,
            int handicapRepetitionBonus = 0,
            SourceGameplayCompatibility compatibility = SourceGameplayCompatibility.StrictSource,
            long compatibilityRepairCount = 0, IReadOnlyList<SourceCompatibilityRepair> compatibilityRepairs = null,
            SourceSelectionTimerView selectionTimer = null,
            SourceRandomSelectionView homeRandomSelection = default(SourceRandomSelectionView),
            SourceRandomSelectionView awayRandomSelection = default(SourceRandomSelectionView),
            SourceAudibleView audibles = null)
            : this(interceptions, timeOfPossession, configuration, playKind, phase, period, possession,
                quarterScores, stations, result, homePlay, awayPlay, selectionCountdown, patChoice, patStatus, kickoff,
                kickingTeam, receivingTeam, offenseFlipped, homeGameplay, awayGameplay,
                forwardCatcherPlayerId, dronesLose, dronesWinning, handicapRepetitionBonus,
                compatibility, compatibilityRepairCount, compatibilityRepairs, selectionTimer,
                homeRandomSelection, awayRandomSelection, audibles)
        {
            if (passCompletions == null)
                throw new ArgumentNullException(nameof(passCompletions));
            PassCompletions = passCompletions;
        }

        public SourceMatchView(SourceCompletedPassStatsView completedPassStats,
            SourcePassCompletionsView passCompletions, SourceInterceptionsView interceptions,
            SourceTimeOfPossessionView timeOfPossession,
            SourceMatchConfiguration configuration, SourcePlayKind playKind,
            SourceMatchPhase phase, SourcePeriodView period, SourcePossessionView possession,
            IReadOnlyList<SourceQuarterScore> quarterScores, IReadOnlyList<SourceStationView> stations,
            SourceMatchResult result = default(SourceMatchResult),
            SourcePlaySelection homePlay = default(SourcePlaySelection),
            SourcePlaySelection awayPlay = default(SourcePlaySelection),
            int selectionCountdown = -1, SourcePatOption patChoice = SourcePatOption.Unselected,
            SourcePatStatus patStatus = SourcePatStatus.NotExtraPoint, SourceKickoffType? kickoff = null,
            TeamId? kickingTeam = null, TeamId? receivingTeam = null, bool? offenseFlipped = null,
            SourceTeamGameplayView homeGameplay = default(SourceTeamGameplayView),
            SourceTeamGameplayView awayGameplay = default(SourceTeamGameplayView),
            int forwardCatcherPlayerId = -1, bool dronesLose = false, bool dronesWinning = false,
            int handicapRepetitionBonus = 0,
            SourceGameplayCompatibility compatibility = SourceGameplayCompatibility.StrictSource,
            long compatibilityRepairCount = 0, IReadOnlyList<SourceCompatibilityRepair> compatibilityRepairs = null,
            SourceSelectionTimerView selectionTimer = null,
            SourceRandomSelectionView homeRandomSelection = default(SourceRandomSelectionView),
            SourceRandomSelectionView awayRandomSelection = default(SourceRandomSelectionView),
            SourceAudibleView audibles = null)
            : this(passCompletions, interceptions, timeOfPossession, configuration, playKind, phase, period, possession,
                quarterScores, stations, result, homePlay, awayPlay, selectionCountdown, patChoice, patStatus, kickoff,
                kickingTeam, receivingTeam, offenseFlipped, homeGameplay, awayGameplay,
                forwardCatcherPlayerId, dronesLose, dronesWinning, handicapRepetitionBonus,
                compatibility, compatibilityRepairCount, compatibilityRepairs, selectionTimer,
                homeRandomSelection, awayRandomSelection, audibles)
        {
            if (completedPassStats == null)
                throw new ArgumentNullException(nameof(completedPassStats));
            CompletedPassStats = completedPassStats;
        }

        public SourceMatchView(SourcePlayEndStatsView playEndStats, SourceCompletedPassStatsView completedPassStats,
            SourcePassCompletionsView passCompletions, SourceInterceptionsView interceptions,
            SourceTimeOfPossessionView timeOfPossession,
            SourceMatchConfiguration configuration, SourcePlayKind playKind,
            SourceMatchPhase phase, SourcePeriodView period, SourcePossessionView possession,
            IReadOnlyList<SourceQuarterScore> quarterScores, IReadOnlyList<SourceStationView> stations,
            SourceMatchResult result = default(SourceMatchResult),
            SourcePlaySelection homePlay = default(SourcePlaySelection),
            SourcePlaySelection awayPlay = default(SourcePlaySelection),
            int selectionCountdown = -1, SourcePatOption patChoice = SourcePatOption.Unselected,
            SourcePatStatus patStatus = SourcePatStatus.NotExtraPoint, SourceKickoffType? kickoff = null,
            TeamId? kickingTeam = null, TeamId? receivingTeam = null, bool? offenseFlipped = null,
            SourceTeamGameplayView homeGameplay = default(SourceTeamGameplayView),
            SourceTeamGameplayView awayGameplay = default(SourceTeamGameplayView),
            int forwardCatcherPlayerId = -1, bool dronesLose = false, bool dronesWinning = false,
            int handicapRepetitionBonus = 0,
            SourceGameplayCompatibility compatibility = SourceGameplayCompatibility.StrictSource,
            long compatibilityRepairCount = 0, IReadOnlyList<SourceCompatibilityRepair> compatibilityRepairs = null,
            SourceSelectionTimerView selectionTimer = null,
            SourceRandomSelectionView homeRandomSelection = default(SourceRandomSelectionView),
            SourceRandomSelectionView awayRandomSelection = default(SourceRandomSelectionView),
            SourceAudibleView audibles = null)
            : this(completedPassStats, passCompletions, interceptions, timeOfPossession,
                configuration, playKind, phase, period, possession, quarterScores, stations, result,
                homePlay, awayPlay, selectionCountdown, patChoice, patStatus, kickoff,
                kickingTeam, receivingTeam, offenseFlipped, homeGameplay, awayGameplay,
                forwardCatcherPlayerId, dronesLose, dronesWinning, handicapRepetitionBonus,
                compatibility, compatibilityRepairCount, compatibilityRepairs, selectionTimer,
                homeRandomSelection, awayRandomSelection, audibles)
        {
            if (playEndStats == null)
                throw new ArgumentNullException(nameof(playEndStats));
            PlayEndStats = playEndStats;
        }

        public SourceMatchView(SourcePlaceKickStatsView placeKickStats, SourcePlayEndStatsView playEndStats,
            SourceCompletedPassStatsView completedPassStats, SourcePassCompletionsView passCompletions,
            SourceInterceptionsView interceptions, SourceTimeOfPossessionView timeOfPossession,
            SourceMatchConfiguration configuration, SourcePlayKind playKind,
            SourceMatchPhase phase, SourcePeriodView period, SourcePossessionView possession,
            IReadOnlyList<SourceQuarterScore> quarterScores, IReadOnlyList<SourceStationView> stations,
            SourceMatchResult result = default(SourceMatchResult),
            SourcePlaySelection homePlay = default(SourcePlaySelection),
            SourcePlaySelection awayPlay = default(SourcePlaySelection),
            int selectionCountdown = -1, SourcePatOption patChoice = SourcePatOption.Unselected,
            SourcePatStatus patStatus = SourcePatStatus.NotExtraPoint, SourceKickoffType? kickoff = null,
            TeamId? kickingTeam = null, TeamId? receivingTeam = null, bool? offenseFlipped = null,
            SourceTeamGameplayView homeGameplay = default(SourceTeamGameplayView),
            SourceTeamGameplayView awayGameplay = default(SourceTeamGameplayView),
            int forwardCatcherPlayerId = -1, bool dronesLose = false, bool dronesWinning = false,
            int handicapRepetitionBonus = 0,
            SourceGameplayCompatibility compatibility = SourceGameplayCompatibility.StrictSource,
            long compatibilityRepairCount = 0, IReadOnlyList<SourceCompatibilityRepair> compatibilityRepairs = null,
            SourceSelectionTimerView selectionTimer = null,
            SourceRandomSelectionView homeRandomSelection = default(SourceRandomSelectionView),
            SourceRandomSelectionView awayRandomSelection = default(SourceRandomSelectionView),
            SourceAudibleView audibles = null)
            : this(playEndStats, completedPassStats, passCompletions, interceptions, timeOfPossession,
                configuration, playKind, phase, period, possession, quarterScores, stations, result,
                homePlay, awayPlay, selectionCountdown, patChoice, patStatus, kickoff,
                kickingTeam, receivingTeam, offenseFlipped, homeGameplay, awayGameplay,
                forwardCatcherPlayerId, dronesLose, dronesWinning, handicapRepetitionBonus,
                compatibility, compatibilityRepairCount, compatibilityRepairs, selectionTimer,
                homeRandomSelection, awayRandomSelection, audibles)
        {
            if (placeKickStats == null)
                throw new ArgumentNullException(nameof(placeKickStats));
            PlaceKickStats = placeKickStats;
        }

        public SourceMatchView(SourceResultProgressionStatsView resultProgressionStats,
            SourcePlaceKickStatsView placeKickStats, SourcePlayEndStatsView playEndStats,
            SourceCompletedPassStatsView completedPassStats, SourcePassCompletionsView passCompletions,
            SourceInterceptionsView interceptions, SourceTimeOfPossessionView timeOfPossession,
            SourceMatchConfiguration configuration, SourcePlayKind playKind,
            SourceMatchPhase phase, SourcePeriodView period, SourcePossessionView possession,
            IReadOnlyList<SourceQuarterScore> quarterScores, IReadOnlyList<SourceStationView> stations,
            SourceMatchResult result = default(SourceMatchResult),
            SourcePlaySelection homePlay = default(SourcePlaySelection),
            SourcePlaySelection awayPlay = default(SourcePlaySelection),
            int selectionCountdown = -1, SourcePatOption patChoice = SourcePatOption.Unselected,
            SourcePatStatus patStatus = SourcePatStatus.NotExtraPoint, SourceKickoffType? kickoff = null,
            TeamId? kickingTeam = null, TeamId? receivingTeam = null, bool? offenseFlipped = null,
            SourceTeamGameplayView homeGameplay = default(SourceTeamGameplayView),
            SourceTeamGameplayView awayGameplay = default(SourceTeamGameplayView),
            int forwardCatcherPlayerId = -1, bool dronesLose = false, bool dronesWinning = false,
            int handicapRepetitionBonus = 0,
            SourceGameplayCompatibility compatibility = SourceGameplayCompatibility.StrictSource,
            long compatibilityRepairCount = 0, IReadOnlyList<SourceCompatibilityRepair> compatibilityRepairs = null,
            SourceSelectionTimerView selectionTimer = null,
            SourceRandomSelectionView homeRandomSelection = default(SourceRandomSelectionView),
            SourceRandomSelectionView awayRandomSelection = default(SourceRandomSelectionView),
            SourceAudibleView audibles = null)
            : this(placeKickStats, playEndStats, completedPassStats, passCompletions, interceptions,
                timeOfPossession, configuration, playKind, phase, period, possession, quarterScores,
                stations, result, homePlay, awayPlay, selectionCountdown, patChoice, patStatus,
                kickoff, kickingTeam, receivingTeam, offenseFlipped, homeGameplay, awayGameplay,
                forwardCatcherPlayerId, dronesLose, dronesWinning, handicapRepetitionBonus,
                compatibility, compatibilityRepairCount, compatibilityRepairs, selectionTimer,
                homeRandomSelection, awayRandomSelection, audibles)
        {
            if (resultProgressionStats == null)
                throw new ArgumentNullException(nameof(resultProgressionStats));
            ResultProgressionStats = resultProgressionStats;
        }

        public SourceMatchView(SourceFourthDownAttemptsView fourthDownAttempts,
            SourceResultProgressionStatsView resultProgressionStats,
            SourcePlaceKickStatsView placeKickStats, SourcePlayEndStatsView playEndStats,
            SourceCompletedPassStatsView completedPassStats, SourcePassCompletionsView passCompletions,
            SourceInterceptionsView interceptions, SourceTimeOfPossessionView timeOfPossession,
            SourceMatchConfiguration configuration, SourcePlayKind playKind,
            SourceMatchPhase phase, SourcePeriodView period, SourcePossessionView possession,
            IReadOnlyList<SourceQuarterScore> quarterScores, IReadOnlyList<SourceStationView> stations,
            SourceMatchResult result = default(SourceMatchResult),
            SourcePlaySelection homePlay = default(SourcePlaySelection),
            SourcePlaySelection awayPlay = default(SourcePlaySelection),
            int selectionCountdown = -1, SourcePatOption patChoice = SourcePatOption.Unselected,
            SourcePatStatus patStatus = SourcePatStatus.NotExtraPoint, SourceKickoffType? kickoff = null,
            TeamId? kickingTeam = null, TeamId? receivingTeam = null, bool? offenseFlipped = null,
            SourceTeamGameplayView homeGameplay = default(SourceTeamGameplayView),
            SourceTeamGameplayView awayGameplay = default(SourceTeamGameplayView),
            int forwardCatcherPlayerId = -1, bool dronesLose = false, bool dronesWinning = false,
            int handicapRepetitionBonus = 0,
            SourceGameplayCompatibility compatibility = SourceGameplayCompatibility.StrictSource,
            long compatibilityRepairCount = 0, IReadOnlyList<SourceCompatibilityRepair> compatibilityRepairs = null,
            SourceSelectionTimerView selectionTimer = null,
            SourceRandomSelectionView homeRandomSelection = default(SourceRandomSelectionView),
            SourceRandomSelectionView awayRandomSelection = default(SourceRandomSelectionView),
            SourceAudibleView audibles = null)
            : this(resultProgressionStats, placeKickStats, playEndStats, completedPassStats,
                passCompletions, interceptions, timeOfPossession, configuration, playKind, phase,
                period, possession, quarterScores, stations, result, homePlay, awayPlay,
                selectionCountdown, patChoice, patStatus, kickoff, kickingTeam, receivingTeam,
                offenseFlipped, homeGameplay, awayGameplay, forwardCatcherPlayerId, dronesLose,
                dronesWinning, handicapRepetitionBonus, compatibility, compatibilityRepairCount,
                compatibilityRepairs, selectionTimer, homeRandomSelection, awayRandomSelection, audibles)
        {
            if (fourthDownAttempts == null)
                throw new ArgumentNullException(nameof(fourthDownAttempts));
            FourthDownAttempts = fourthDownAttempts;
        }

        public SourceMatchView(SourceTouchdownStatsView touchdownStats,
            SourceFourthDownAttemptsView fourthDownAttempts,
            SourceResultProgressionStatsView resultProgressionStats,
            SourcePlaceKickStatsView placeKickStats, SourcePlayEndStatsView playEndStats,
            SourceCompletedPassStatsView completedPassStats, SourcePassCompletionsView passCompletions,
            SourceInterceptionsView interceptions, SourceTimeOfPossessionView timeOfPossession,
            SourceMatchConfiguration configuration, SourcePlayKind playKind,
            SourceMatchPhase phase, SourcePeriodView period, SourcePossessionView possession,
            IReadOnlyList<SourceQuarterScore> quarterScores, IReadOnlyList<SourceStationView> stations,
            SourceMatchResult result = default(SourceMatchResult),
            SourcePlaySelection homePlay = default(SourcePlaySelection),
            SourcePlaySelection awayPlay = default(SourcePlaySelection),
            int selectionCountdown = -1, SourcePatOption patChoice = SourcePatOption.Unselected,
            SourcePatStatus patStatus = SourcePatStatus.NotExtraPoint, SourceKickoffType? kickoff = null,
            TeamId? kickingTeam = null, TeamId? receivingTeam = null, bool? offenseFlipped = null,
            SourceTeamGameplayView homeGameplay = default(SourceTeamGameplayView),
            SourceTeamGameplayView awayGameplay = default(SourceTeamGameplayView),
            int forwardCatcherPlayerId = -1, bool dronesLose = false, bool dronesWinning = false,
            int handicapRepetitionBonus = 0,
            SourceGameplayCompatibility compatibility = SourceGameplayCompatibility.StrictSource,
            long compatibilityRepairCount = 0, IReadOnlyList<SourceCompatibilityRepair> compatibilityRepairs = null,
            SourceSelectionTimerView selectionTimer = null,
            SourceRandomSelectionView homeRandomSelection = default(SourceRandomSelectionView),
            SourceRandomSelectionView awayRandomSelection = default(SourceRandomSelectionView),
            SourceAudibleView audibles = null)
            : this(fourthDownAttempts, resultProgressionStats, placeKickStats, playEndStats,
                completedPassStats, passCompletions, interceptions, timeOfPossession, configuration,
                playKind, phase, period, possession, quarterScores, stations, result, homePlay,
                awayPlay, selectionCountdown, patChoice, patStatus, kickoff, kickingTeam,
                receivingTeam, offenseFlipped, homeGameplay, awayGameplay, forwardCatcherPlayerId,
                dronesLose, dronesWinning, handicapRepetitionBonus, compatibility,
                compatibilityRepairCount, compatibilityRepairs, selectionTimer, homeRandomSelection,
                awayRandomSelection, audibles)
        {
            if (touchdownStats == null)
                throw new ArgumentNullException(nameof(touchdownStats));
            TouchdownStats = touchdownStats;
        }

        public SourceMatchView(SourcePlayActivityStatsView playActivityStats, SourceScoringStatsView scoringStats,
            SourceTouchdownStatsView touchdownStats, SourceFourthDownAttemptsView fourthDownAttempts,
            SourceResultProgressionStatsView resultProgressionStats,
            SourcePlaceKickStatsView placeKickStats, SourcePlayEndStatsView playEndStats,
            SourceCompletedPassStatsView completedPassStats, SourcePassCompletionsView passCompletions,
            SourceInterceptionsView interceptions, SourceTimeOfPossessionView timeOfPossession,
            SourceMatchConfiguration configuration, SourcePlayKind playKind,
            SourceMatchPhase phase, SourcePeriodView period, SourcePossessionView possession,
            IReadOnlyList<SourceQuarterScore> quarterScores, IReadOnlyList<SourceStationView> stations,
            SourceMatchResult result = default(SourceMatchResult),
            SourcePlaySelection homePlay = default(SourcePlaySelection),
            SourcePlaySelection awayPlay = default(SourcePlaySelection),
            int selectionCountdown = -1, SourcePatOption patChoice = SourcePatOption.Unselected,
            SourcePatStatus patStatus = SourcePatStatus.NotExtraPoint, SourceKickoffType? kickoff = null,
            TeamId? kickingTeam = null, TeamId? receivingTeam = null, bool? offenseFlipped = null,
            SourceTeamGameplayView homeGameplay = default(SourceTeamGameplayView),
            SourceTeamGameplayView awayGameplay = default(SourceTeamGameplayView),
            int forwardCatcherPlayerId = -1, bool dronesLose = false, bool dronesWinning = false,
            int handicapRepetitionBonus = 0,
            SourceGameplayCompatibility compatibility = SourceGameplayCompatibility.StrictSource,
            long compatibilityRepairCount = 0, IReadOnlyList<SourceCompatibilityRepair> compatibilityRepairs = null,
            SourceSelectionTimerView selectionTimer = null,
            SourceRandomSelectionView homeRandomSelection = default(SourceRandomSelectionView),
            SourceRandomSelectionView awayRandomSelection = default(SourceRandomSelectionView),
            SourceAudibleView audibles = null)
            : this(touchdownStats, fourthDownAttempts, resultProgressionStats, placeKickStats, playEndStats,
                completedPassStats, passCompletions, interceptions, timeOfPossession, configuration,
                playKind, phase, period, possession, quarterScores, stations, result, homePlay,
                awayPlay, selectionCountdown, patChoice, patStatus, kickoff, kickingTeam,
                receivingTeam, offenseFlipped, homeGameplay, awayGameplay, forwardCatcherPlayerId,
                dronesLose, dronesWinning, handicapRepetitionBonus, compatibility,
                compatibilityRepairCount, compatibilityRepairs, selectionTimer, homeRandomSelection,
                awayRandomSelection, audibles)
        {
            PlayActivityStats = playActivityStats ?? throw new ArgumentNullException(nameof(playActivityStats));
            ScoringStats = scoringStats ?? throw new ArgumentNullException(nameof(scoringStats));
        }

        public SourceRandomSelectionView RandomSelection(TeamId side)
        {
            SourceContractGuard.Team(side, nameof(side));
            return side == TeamId.Home ? HomeRandomSelection : AwayRandomSelection;
        }
    }

    public readonly struct SourceGameEvent
    {
        private readonly int actorPlayerId;
        private readonly int targetPlayerId;
        private readonly int stationId;
        public bool IsSpecified { get { return Type != SourceEventType.Unspecified; } }
        public long Tick { get; }
        public long SourceTick { get; }
        public SourceEventType Type { get; }
        public TeamId? Team { get; }
        public int ActorPlayerId { get { return IsSpecified ? actorPlayerId : -1; } }
        public int TargetPlayerId { get { return IsSpecified ? targetPlayerId : -1; } }
        public int StationId { get { return IsSpecified ? stationId : -1; } }
        public FieldVector Position { get; }
        public int Value { get; }
        public SourceMatchResult Result { get; }
        public SourcePlaySelection Selection { get; }
        public SourcePlayKind? PlayKind { get; }
        public SourcePatOption? PatChoice { get; }
        public SourceKickoffType? Kickoff { get; }

        public SourceGameEvent(long tick, long sourceTick, SourceEventType type, TeamId? team = null,
            int actorPlayerId = -1, int targetPlayerId = -1, int stationId = -1,
            FieldVector position = default(FieldVector), int value = 0,
            SourceMatchResult result = default(SourceMatchResult),
            SourcePlaySelection selection = default(SourcePlaySelection), SourcePlayKind? playKind = null,
            SourcePatOption? patChoice = null, SourceKickoffType? kickoff = null)
        {
            if (tick < 0)
                throw new ArgumentOutOfRangeException(nameof(tick));
            if (sourceTick < 0)
                throw new ArgumentOutOfRangeException(nameof(sourceTick));
            SourceContractGuard.Range((int)type, 1, 19, nameof(type));
            if (team.HasValue)
                SourceContractGuard.Team(team.Value, nameof(team));
            SourceContractGuard.Player(actorPlayerId, nameof(actorPlayerId));
            SourceContractGuard.Player(targetPlayerId, nameof(targetPlayerId));
            SourceContractGuard.Station(stationId, true, nameof(stationId));
            SourceContractGuard.Finite(position.X, nameof(position));
            SourceContractGuard.Finite(position.Z, nameof(position));
            if (playKind.HasValue)
                SourceContractGuard.Range((int)playKind.Value, 0, 4, nameof(playKind));
            if (patChoice.HasValue)
                SourceContractGuard.Range((int)patChoice.Value, 0, 2, nameof(patChoice));
            if (kickoff.HasValue)
                SourceContractGuard.Range((int)kickoff.Value, 0, 2, nameof(kickoff));
            Tick = tick;
            SourceTick = sourceTick;
            Type = type;
            Team = team;
            this.actorPlayerId = actorPlayerId;
            this.targetPlayerId = targetPlayerId;
            this.stationId = stationId;
            Position = position;
            Value = value;
            Result = result;
            Selection = selection;
            PlayKind = playKind;
            PatChoice = patChoice;
            Kickoff = kickoff;
        }
    }

    public enum SourceCabinetFlowStage
    {
        Inactive, ResultWait, EndOfQuarter, CoachingTip, HalftimeStats,
        ContinuationOffer, FinalStats, FinalConfirmation, NewMatchRequested, RecordCommitWait
    }

    public enum SourceCabinetContinuationPolicy
    {
        KeepParticipatingStations, RequireConfirmation
    }

    // Game.c's times[4] is uninitialized. This is a PORTABLE policy, not a
    // reconstruction of native stack contents. It applies to each new wait only.
    public enum SourceCabinetWaitInitialization { ZeroCountersOnEntry }

    public readonly struct SourceCabinetButtonMasks
    {
        public bool IsSpecified { get; }
        public int PlayerAbcdMask { get; }
        public int PlayerAbcMask { get; }
        public uint AllAbcdMask { get; }

        public SourceCabinetButtonMasks(int playerAbcdMask, int playerAbcMask, uint allAbcdMask)
        {
            if (playerAbcMask != 0x70 || (playerAbcdMask & 0x70) != 0x70
                || (playerAbcdMask & ~0xf0) != 0)
                throw new ArgumentException("Supply the source P_ABCD_MASK and P_A|P_B|P_C in station-byte register positions.");
            uint repeated = (uint)playerAbcdMask * 0x01010101u;
            if (allAbcdMask != repeated)
                throw new ArgumentException("ALL_ABCD_MASK must retain all four source station bytes.", nameof(allAbcdMask));
            PlayerAbcdMask = playerAbcdMask; PlayerAbcMask = playerAbcMask;
            AllAbcdMask = allAbcdMask; IsSpecified = true;
        }
    }

    /// <summary>
    /// Explicit free-play source-gameflow/v1 admission. No coins, credit/audit RAM,
    /// records, trivia, asset transitions or native undefined-data equivalence.
    /// Participation must equal the match's configured human stations; no join/drop
    /// changes are silently made to football control. RequireConfirmation ends the
    /// match if nobody confirms the portable continuation offer before timedown.
    /// Skipped-final-PAT actor cleanup retains its actual completed PAT AI context;
    /// published PT_NORMAL/down=-1 football rules and strict/live guards are unchanged.
    /// </summary>
    public sealed class SourceCabinetFlowConfiguration
    {
        public const string ContractId = "source-gameflow/v1";
        public string Contract { get { return ContractId; } }
        public int ParticipatingStationMask { get; }
        public SourceCabinetButtonMasks ButtonMasks { get; }
        public SourceCabinetContinuationPolicy ContinuationPolicy { get; }
        public SourceCabinetWaitInitialization WaitInitialization { get; }
        public bool NativeScratchContentsKnown { get { return false; } }

        public SourceCabinetFlowConfiguration(int participatingStationMask, SourceCabinetButtonMasks buttonMasks,
            SourceCabinetContinuationPolicy continuationPolicy, SourceCabinetWaitInitialization waitInitialization)
        {
            SourceContractGuard.Range(participatingStationMask, 0, 15, nameof(participatingStationMask));
            if (!buttonMasks.IsSpecified) throw new ArgumentException("Explicit source button masks are required.", nameof(buttonMasks));
            SourceContractGuard.Range((int)continuationPolicy, 0, 1, nameof(continuationPolicy));
            if (waitInitialization != SourceCabinetWaitInitialization.ZeroCountersOnEntry)
                throw new ArgumentOutOfRangeException(nameof(waitInitialization));
            ParticipatingStationMask = participatingStationMask; ButtonMasks = buttonMasks;
            ContinuationPolicy = continuationPolicy; WaitInitialization = waitInitialization;
        }
    }

    public sealed class SourceCabinetFlowView
    {
        public SourceCabinetFlowConfiguration Configuration { get; }
        public SourceCabinetFlowStage Stage { get; }
        public long StageStartedSourceTick { get; }
        // Resumptions of the stage's game_proc consumer, not host/redraw ticks.
        public int ElapsedSourceWakes { get; }
        // Helper window only. EOQ can first wait for an existing message child;
        // any-button MaximumTicks includes its minimum plus polling budget.
        public int MinimumTicks { get; }
        public int MaximumTicks { get; }
        public ReadOnlyCollection<int> ConsecutiveButtonTicks { get; }
        public int AcknowledgingStationMask { get; }
        public int ConfirmedStationMask { get; }
        public int? CompletedQuarterIndex { get; }
        public int? ContinuationSeconds { get; }
        public bool ContinuationNumberVisible { get; }
        public bool WaitingForButtonRelease { get; }
        public int? CoachingBackgroundOrdinal { get; }
        public int? CoachingTipOrdinal { get; }
        public bool NewMatchRequested { get { return Stage == SourceCabinetFlowStage.NewMatchRequested; } }
        public SourceMatchView FinalMatch { get; }
        public int FinalHomeScore { get; }
        public int FinalAwayScore { get; }

        public SourceCabinetFlowView(SourceCabinetFlowConfiguration configuration, SourceCabinetFlowStage stage,
            long stageStartedSourceTick, int elapsedSourceWakes, int minimumTicks, int maximumTicks,
            IReadOnlyList<int> consecutiveButtonTicks, int acknowledgingStationMask, int confirmedStationMask,
            int? completedQuarterIndex, int? continuationSeconds, bool continuationNumberVisible,
            bool waitingForButtonRelease, SourceMatchView finalMatch, int finalHomeScore, int finalAwayScore,
            int? coachingBackgroundOrdinal = null, int? coachingTipOrdinal = null)
        {
            if (configuration == null) throw new ArgumentNullException(nameof(configuration));
            SourceContractGuard.Range((int)stage, 0, 9, nameof(stage));
            if (stageStartedSourceTick < 0) throw new ArgumentOutOfRangeException(nameof(stageStartedSourceTick));
            SourceContractGuard.Range(elapsedSourceWakes, 0, int.MaxValue, nameof(elapsedSourceWakes));
            SourceContractGuard.Range(minimumTicks, 0, int.MaxValue, nameof(minimumTicks));
            SourceContractGuard.Range(maximumTicks, 0, int.MaxValue, nameof(maximumTicks));
            if (consecutiveButtonTicks == null || consecutiveButtonTicks.Count != 4)
                throw new ArgumentException("Retain four independently counted source stations.", nameof(consecutiveButtonTicks));
            var copy = new int[4];
            for (int i = 0; i < copy.Length; i++)
            {
                SourceContractGuard.Range(consecutiveButtonTicks[i], 0, int.MaxValue, nameof(consecutiveButtonTicks));
                copy[i] = consecutiveButtonTicks[i];
            }
            SourceContractGuard.Range(acknowledgingStationMask, 0, 15, nameof(acknowledgingStationMask));
            SourceContractGuard.Range(confirmedStationMask, 0, 15, nameof(confirmedStationMask));
            if (completedQuarterIndex.HasValue)
                SourceContractGuard.Range(completedQuarterIndex.Value, 0, 6, nameof(completedQuarterIndex));
            if (continuationSeconds.HasValue)
                SourceContractGuard.Range(continuationSeconds.Value, 0, 9, nameof(continuationSeconds));
            SourceContractGuard.Range(finalHomeScore, 0, int.MaxValue, nameof(finalHomeScore));
            SourceContractGuard.Range(finalAwayScore, 0, int.MaxValue, nameof(finalAwayScore));
            if (stage == SourceCabinetFlowStage.RecordCommitWait
                && (finalMatch == null || finalMatch.Phase != SourceMatchPhase.MatchEnded))
                throw new ArgumentException("Record commit wait requires the actual retained final match.", nameof(finalMatch));
            if (coachingBackgroundOrdinal.HasValue)
                SourceContractGuard.Range(coachingBackgroundOrdinal.Value, 0, 6, nameof(coachingBackgroundOrdinal));
            if (coachingTipOrdinal.HasValue)
                SourceContractGuard.Range(coachingTipOrdinal.Value, 0, 29, nameof(coachingTipOrdinal));
            Configuration = configuration; Stage = stage; StageStartedSourceTick = stageStartedSourceTick;
            ElapsedSourceWakes = elapsedSourceWakes; MinimumTicks = minimumTicks; MaximumTicks = maximumTicks;
            ConsecutiveButtonTicks = Array.AsReadOnly(copy); AcknowledgingStationMask = acknowledgingStationMask;
            ConfirmedStationMask = confirmedStationMask; CompletedQuarterIndex = completedQuarterIndex;
            ContinuationSeconds = continuationSeconds; ContinuationNumberVisible = continuationNumberVisible;
            WaitingForButtonRelease = waitingForButtonRelease; FinalMatch = finalMatch;
            FinalHomeScore = finalHomeScore; FinalAwayScore = finalAwayScore;
            CoachingBackgroundOrdinal = coachingBackgroundOrdinal; CoachingTipOrdinal = coachingTipOrdinal;
        }
    }

    public interface ISourceCabinetFlowState : ISourceMatchState
    {
        // Null means UNBOUND, not an implicitly admitted portable policy.
        SourceCabinetFlowView CabinetFlow { get; }
    }

    public interface ISourceCabinetFlowSimulation : ISourceMatchSimulation
    {
        void BindSourceCabinetFlow(SourceCabinetFlowConfiguration configuration);
    }

    public interface ISourceMatchState : ISourceGameState
    {
        SourceMatchView Match { get; }
    }

    public interface ISourceMatchSimulation : IGameSimulation, IPendingSourceInput
    {
        IReadOnlyList<SourceGameEvent> SourceEvents { get; }
    }
}
