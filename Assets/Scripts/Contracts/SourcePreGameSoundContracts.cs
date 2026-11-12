using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Blitz.Contracts
{
    public enum SourcePreGameMenuSound
    {
        NameCursor, NameSelect, YesNoCursor, RecordFound,
        CapCursor, CapSelect, FormationCursor, FormationSelect, RoutePoint, RouteMenu,
        CapLetterCursor, CapLetterSelect, OptionCursor, OptionSelect, PlayCursor, PlaySelect,
        RouteMenuCursor, CapYesNoCursor, CapYesNoSelect, YesNoSelect
    }

    public enum SourcePreGameMenuPhase
    {
        Joining, Options, PlayerEntry, CreatePlay, Teams, Rosters, Versus, MatchRequested, PortableManagement
    }

    public enum SourcePreGameMenuScreen
    {
        None, Options, OptionsConfirm, YesNo, Name, Pin, RecordLookup, RecordStatus,
        CapName, CapPin, CapSlots, CapActions, CapFormation, CapRoute, CapRouteMenu,
        CapPlayName, CapConfirm, CapAudibles, Ready, Portable
    }

    public sealed class SourcePreGameMenuRoute
    {
        public SourcePreGameMenuPhase Phase { get; }
        public int ParticipatingStationMask { get; }
        public int FocusStationId { get; }
        public bool StorageBusy { get; }
        public int OptionIndex { get; }
        public ReadOnlyCollection<SourcePreGameMenuScreen> StationScreens { get; }
        public ReadOnlyCollection<bool> YesAnswers { get; }

        public SourcePreGameMenuRoute(SourcePreGameMenuPhase phase, int participatingStationMask,
            int focusStationId, IReadOnlyList<SourcePreGameMenuScreen> stationScreens, bool storageBusy = false,
            IReadOnlyList<bool> yesAnswers = null, int optionIndex = 1)
        {
            SourceContractGuard.Range((int)phase, 0, 8, nameof(phase));
            SourceContractGuard.Range(participatingStationMask, 0, 15, nameof(participatingStationMask));
            SourceContractGuard.Station(focusStationId, true, nameof(focusStationId));
            if (stationScreens == null || stationScreens.Count != 4)
                throw new ArgumentException("Supply the four actual menu station states, without private record data.", nameof(stationScreens));
            var copy = new SourcePreGameMenuScreen[4];
            for (int i = 0; i < 4; i++)
            {
                SourceContractGuard.Range((int)stationScreens[i], 0, 19, nameof(stationScreens));
                copy[i] = stationScreens[i];
            }
            if (phase != SourcePreGameMenuPhase.Joining && phase != SourcePreGameMenuPhase.PortableManagement
                && (focusStationId < 0 || (participatingStationMask & (1 << focusStationId)) == 0))
                throw new ArgumentException("An original menu requires its actual participating controlling station.", nameof(focusStationId));
            Phase = phase; ParticipatingStationMask = participatingStationMask;
            FocusStationId = focusStationId; StationScreens = Array.AsReadOnly(copy); StorageBusy = storageBusy;
            SourceContractGuard.Range(optionIndex, 0, 3, nameof(optionIndex));
            OptionIndex = optionIndex;
            if (yesAnswers != null && yesAnswers.Count != 4)
                throw new ArgumentException("Supply four actual YES/NO answers.", nameof(yesAnswers));
            var answers = new bool[4];
            if (yesAnswers != null) for (int i = 0; i < 4; i++) answers[i] = yesAnswers[i];
            YesAnswers = Array.AsReadOnly(answers);
        }

        public SourcePreGameMenuScreen ScreenFor(int stationId)
        {
            SourceContractGuard.Station(stationId, false, nameof(stationId));
            return StationScreens[stationId];
        }
    }

    public interface ISourcePreGameMenuDriver
    {
        SourcePreGameMenuRoute ReadRoute();
        void OnSourceMenuWake(SourcePreGameMenuWake wake);
    }

    // Only the owning source process creates a scope. A retained scope cannot
    // emit after its real callback returns, even if an asynchronous IO completes.
    public sealed class SourcePreGameMenuWake
    {
        private readonly Action<SourcePreGameMenuWake, SourcePreGameMenuSound> emit;
        public SourcePreGameMenuRoute Route { get; }
        public SourceConsumedPhysicalSwitchScan Scan { get; }
        public SourceProcessView? Process { get; }
        public int StationId { get; }
        public SourcePreGameMenuScreen Screen { get; }
        public bool IsStorageResume { get; }
        public bool ForceSelection { get; internal set; }
        public bool ButtonsEnabled { get; internal set; } = true;
        public long CaptureSequence { get; }
        public bool IsOriginalProcess { get { return Process.HasValue; } }

        internal SourcePreGameMenuWake(SourcePreGameMenuRoute route, SourceConsumedPhysicalSwitchScan scan,
            SourceProcessView? process, int station, SourcePreGameMenuScreen screen, bool storageResume,
            long captureSequence, Action<SourcePreGameMenuWake, SourcePreGameMenuSound> emit)
        {
            Route = route; Scan = scan; Process = process; StationId = station;
            Screen = screen; IsStorageResume = storageResume; CaptureSequence = captureSequence;
            this.emit = emit;
        }

        public SourceButtons ClosedButtons(int stationId)
        { return ButtonsEnabled ? Scan.ClosedButtons(stationId) : SourceButtons.None; }
        public SourceDigitalDirections ClosedDirections(int stationId)
        {
            SourceContractGuard.Station(stationId, false, nameof(stationId));
            return Directions[stationId].Close;
        }
        public ReadOnlyCollection<SourceDigitalSwitchScan> Directions { get; internal set; }
        public void Emit(SourcePreGameMenuSound sound) { emit(this, sound); }
    }

    public sealed class SourcePreGameSoundEntry
    {
        public const string Contract = "source-pregame-after-accepted-start/v1";
        public const string OriginalRevision = "89f840d58d152a2a9f230cb8d0cdffbd554c8ded";
        public ReadOnlyCollection<string> LoadedBanks { get; }
        public uint ReservedTracks { get { return 35; } }
        public string CurrentCursorBank { get { return "cursors"; } }
        public string StartFunction { get { return "plyr_strt_butn"; } }
        public int StopAllSourceLine { get { return 1253; } }
        public int ReservedSourceLine { get { return 1254; } }
        public string Qualification
        {
            get { return "Accepted free-play cabinet start after original permanent-bank initialization; "
                + "earlier coin/DSP/native allocation and graphics-load timing are not replayed."; }
        }

        private SourcePreGameSoundEntry()
        {
            LoadedBanks = Array.AsReadOnly(new[] { "players", "generic", "tkperm", "cursors", "intro" });
        }

        public static SourcePreGameSoundEntry FromOriginalPermanentBanksAfterAcceptedStart()
        { return new SourcePreGameSoundEntry(); }
    }

    public sealed class SourceVersusSoundBankState
    {
        public string CurrentCursorBank { get; }
        public long PreviousSourceTick { get; }
        internal SourceVersusSoundBankState(string currentCursorBank, long previousSourceTick)
        {
            if (currentCursorBank != "cursors" && currentCursorBank != "cursors2")
                throw new ArgumentException("Retain the actual source cursor bank, never a resource alias.", nameof(currentCursorBank));
            if (previousSourceTick < 0) throw new ArgumentOutOfRangeException(nameof(previousSourceTick));
            CurrentCursorBank = currentCursorBank; PreviousSourceTick = previousSourceTick;
        }
    }
}
