using Blitz.AssetImport;
using Blitz.Contracts;
using Blitz.Gameplay;
using Blitz.Presentation;
using Blitz.Runtime;
using Blitz.Storage;
using System.Globalization;
using System.Collections.Concurrent;

namespace Blitz.SourcePlayer;

public enum SourceCabinetRecordSound
{
    NameCursor, NameSelect, YesNoCursor, RecordFound,
    CapCursor, CapSelect, FormationCursor, FormationSelect, RoutePoint, RouteMenu, CapLetterCursor, CapLetterSelect,
    OptionCursor, OptionSelect, PlayCursor, PlaySelect, RouteMenuCursor, CapYesNoCursor, CapYesNoSelect, YesNoSelect
}

public sealed class SourceCabinetRecordsRuntime : ISourceRecordCommitBarrier
{
    private enum EntryStep { Choice, Name, Pin, Missing, Duplicate, Unavailable, Selected, Lookup }
    private enum Confirmation { None, ReplaceRecord, DeleteRecord, Recover, Import, Formation, Route, Discard, DeclineFinal, StartCap }
    private enum FinalState { None, Pending, Committed, Declined, Failed }
    private sealed class Station
    {
        internal EntryStep Step;
        internal bool Yes;
        internal int Cursor, Entered;
        internal readonly char[] Name = new char[6], Pin = new char[4];
        internal SourceRecordIdentity Identity;
        internal SourceRecordSelectionState State = SourceRecordSelectionState.Unavailable;
        internal string Message;
        internal void ClearEntry()
        {
            Array.Fill(Name, '-'); Array.Fill(Pin, '\0');
            Cursor = Entered = 0; Yes = false; Message = null;
        }
    }

    private sealed class UiCompletion
    {
        internal int StationId;
        internal Action Apply;
        internal readonly TaskCompletionSource Signal = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ-*! ";
    private const string PinAlphabet = "123456789 0";
    private static readonly int[] WinsNeeded = { 0, 0, 0, 5, 10, 15, 20, 25, 30 };
    private static readonly string[] Formations = { "2 PLUS 1", "1 PLUS 1 PLUS 1", "TRIPS", "1 WR / 2 BACKS", "4 LINEMEN" };
    private readonly SourcePlayerRecordStore store;
    private readonly PlayerData data;
    private readonly Station[] stations = { new(), new(), new(), new() };
    private readonly string sessionId = Guid.NewGuid().ToString("N");
    private readonly short[][] audibleDraft = { new short[3], new short[3] };
    private readonly List<SourceCustomPoint> route = new();
    private readonly char[][] playName = { new char[8], new char[8] };
    private SourceRecordScreenKind screen = SourceRecordScreenKind.Options, returnScreen, storageReturnScreen;
    private SourceSavedRecordSession boundSession;
    private SourceMatchView observedFinal;
    private SourceCompletedRecordMatch pendingFinal;
    private SourceFinalRecordCommit finalContext;
    private FinalState finalState;
    private Confirmation confirmation;
    private string stagedImport, message;
    private int participants, focus, cursor = 1, selectedSlot, formation, receiver, routeNode, routeX, routeY;
    private int nameField, namePosition, nameCursor, careerPage, leaderboard;
    private int returnCursor, storageReturnCursor;
    private bool active, busy, editAfterEntry, importArmed, exportArmed, customBlockOpen;
    private SourceCustomPlay draft;
    private readonly ConcurrentQueue<UiCompletion> uiCompletions = new();
    private bool sourceMenuBound, sourceMenuRunning;
    private volatile bool uiReady;
    private SourcePreGameMenuWake menuWake;
    private long menuSourceTick;

    public event Action<SourceCabinetRecordSound> SoundRequested;
    public bool Active => active;
    public bool BlocksGameplay => finalState is FinalState.Pending or FinalState.Failed;
    public bool CanAdministrativeRestart => !busy && !customBlockOpen && finalContext == null && observedFinal == null;
    public bool SelectionComplete { get; private set; }
    public bool BackRequested { get; private set; }
    public bool CanImport => active && screen == SourceRecordScreenKind.LocalStorage && importArmed && !busy;
    public bool CanExport => active && screen == SourceRecordScreenKind.LocalStorage && exportArmed && !busy && store.Current != null;
    private bool SelectedHistoryUnavailable => store.SavePolicy == SourceRecordSavePolicy.CustomBlockHistoryV3 &&
        Joined().Any(id => stations[id].Identity != null &&
            SelectedRecord(id)?.CustomBlockState == SourceCustomBlockState.LegacyUnknown);
    private string RecordDiagnosticCode => SelectedHistoryUnavailable && store.IsWritable ?
        "SOURCE_CUSTOM_BLOCK_HISTORY_UNAVAILABLE" : store.Notification.Code;
    public bool CanStartMatch => participants != 0 && !busy && !customBlockOpen && !SelectedHistoryUnavailable && !BlocksGameplay && Joined().All(id =>
        stations[id].Step == EntryStep.Selected &&
        (stations[id].State == SourceRecordSelectionState.Guest ||
            store.IsWritable && stations[id].Identity != null && store.Current.Find(stations[id].Identity) != null));
    public Task PendingOperation { get; private set; } = Task.CompletedTask;
    public SourceRecordStoreResult Notification => store.Notification;
    public bool SourceStorageBusy => busy && !uiReady;

    public SourceCabinetRecordsRuntime(SourcePlayerRecordStore store, PlayerData data)
    {
        this.store = store ?? throw new ArgumentNullException(nameof(store));
        this.data = data ?? throw new ArgumentNullException(nameof(data));
        DataCatalog.Require(store.LoadState != SourceRecordStoreStatus.NotLoaded,
            "SOURCE_RECORD_LOAD_REQUIRED: await the actual owning store before creating the cabinet.");
        foreach (Station station in stations) station.ClearEntry();
    }

    internal void BindSourceMenu()
    {
        DataCatalog.Require(!busy && uiCompletions.IsEmpty, "SOURCE_MENU_REBIND_REQUIRES_COMPLETED_STORAGE");
        sourceMenuBound = sourceMenuRunning = true;
        menuSourceTick = 0;
    }

    internal SourcePreGameMenuRoute ReadSourceRoute()
    {
        var screens = new SourcePreGameMenuScreen[4];
        var yes = new bool[4];
        SourcePreGameMenuPhase phase;
        if (screen == SourceRecordScreenKind.Options ||
            screen == SourceRecordScreenKind.Confirmation && confirmation == Confirmation.StartCap)
        {
            phase = SourcePreGameMenuPhase.Options;
            screens[focus] = screen == SourceRecordScreenKind.Options ? SourcePreGameMenuScreen.Options : SourcePreGameMenuScreen.OptionsConfirm;
            yes[focus] = cursor == 1;
        }
        else if (screen == SourceRecordScreenKind.Selection)
        {
            phase = editAfterEntry ? SourcePreGameMenuPhase.CreatePlay : SourcePreGameMenuPhase.PlayerEntry;
            foreach (int id in Joined())
            {
                if (editAfterEntry && id != focus) continue;
                screens[id] = stations[id].Step switch
                {
                    EntryStep.Choice => SourcePreGameMenuScreen.YesNo,
                    EntryStep.Name => editAfterEntry ? SourcePreGameMenuScreen.CapName : SourcePreGameMenuScreen.Name,
                    EntryStep.Pin => editAfterEntry ? SourcePreGameMenuScreen.CapPin : SourcePreGameMenuScreen.Pin,
                    EntryStep.Lookup => SourcePreGameMenuScreen.RecordLookup,
                    EntryStep.Selected or EntryStep.Missing => SourcePreGameMenuScreen.RecordStatus,
                    _ => SourcePreGameMenuScreen.Portable
                };
                yes[id] = stations[id].Yes;
            }
        }
        else
        {
            screens[focus] = screen switch
            {
                SourceRecordScreenKind.CustomSlots => SourcePreGameMenuScreen.CapSlots,
                SourceRecordScreenKind.Editor => SourcePreGameMenuScreen.CapActions,
                SourceRecordScreenKind.Formation => SourcePreGameMenuScreen.CapFormation,
                SourceRecordScreenKind.Route => SourcePreGameMenuScreen.CapRoute,
                SourceRecordScreenKind.RouteAction => SourcePreGameMenuScreen.CapRouteMenu,
                SourceRecordScreenKind.PlayName => SourcePreGameMenuScreen.CapPlayName,
                SourceRecordScreenKind.Audibles => SourcePreGameMenuScreen.CapAudibles,
                SourceRecordScreenKind.Confirmation when confirmation is Confirmation.Formation or Confirmation.Route or Confirmation.Discard =>
                    SourcePreGameMenuScreen.CapConfirm,
                _ => SourcePreGameMenuScreen.Portable
            };
            phase = screens[focus] == SourcePreGameMenuScreen.Portable ? SourcePreGameMenuPhase.PortableManagement : SourcePreGameMenuPhase.CreatePlay;
            yes[focus] = cursor == 1;
        }
        return new SourcePreGameMenuRoute(phase, participants, focus, screens, SourceStorageBusy, yes,
            screen == SourceRecordScreenKind.Options ? cursor : confirmation == Confirmation.StartCap ? 0 : 1);
    }

    internal void WithSourceMenuWake(SourcePreGameMenuWake wake, Action applyInput)
    {
        DataCatalog.Require(sourceMenuBound && sourceMenuRunning && menuWake == null,
            "SOURCE_MENU_CALLBACK_OWNER_REQUIRED");
        menuWake = wake; menuSourceTick = wake.Scan.SourceTick;
        try
        {
            bool applied = false;
            while (uiCompletions.TryPeek(out UiCompletion completion) &&
                (wake.StationId < 0 || completion.StationId == wake.StationId))
            {
                uiCompletions.TryDequeue(out completion);
                try { completion.Apply(); completion.Signal.SetResult(); }
                catch (Exception error) { completion.Signal.SetException(error); }
                applied = true;
            }
            if (applied || wake.IsStorageResume) return;
            if (wake.StationId >= 0 && stations[wake.StationId].Step == EntryStep.Lookup &&
                wake.Screen is SourcePreGameMenuScreen.RecordLookup or SourcePreGameMenuScreen.RecordStatus)
            {
                ResolveLookup(wake.StationId);
                return;
            }
            applyInput();
        }
        finally { menuWake = null; }
    }

    private Task ApplyUi(Action apply)
    {
        if (!sourceMenuBound || !sourceMenuRunning) { apply(); return Task.CompletedTask; }
        var completion = new UiCompletion { StationId = focus, Apply = apply };
        uiCompletions.Enqueue(completion);
        uiReady = true;
        return completion.Signal.Task;
    }

    public void BeginSelection(int participatingStationMask)
    {
        DataCatalog.Require(participatingStationMask is > 0 and < 16 && !BlocksGameplay && !busy,
            "SOURCE_RECORD_SELECTION_BOUNDARY: finish the preceding record transaction first.");
        participants = participatingStationMask;
        focus = Joined().First();
        SelectionComplete = BackRequested = editAfterEntry = false;
        foreach (int id in editAfterEntry ? new[] { focus } : Joined())
        {
            Station station = stations[id];
            station.ClearEntry(); station.Identity = null;
            station.State = SourceRecordSelectionState.Unavailable; station.Step = EntryStep.Choice;
        }
        Open(SourceRecordScreenKind.Options, 1);
        message = store.IsWritable ? "LOCAL CABINET RECORDS READY" : LoadMessage();
    }

    public void Capture(int[] pressed, int[] moved)
    {
        if (!active || busy) return;
        if (screen == SourceRecordScreenKind.Selection)
        {
            foreach (int id in Joined())
            {
                if (screen != SourceRecordScreenKind.Selection || busy) break;
                CaptureStation(id, pressed[id], moved[id]);
            }
            return;
        }
        int button = pressed[focus], direction = moved[focus];
        if (screen == SourceRecordScreenKind.Options || screen == SourceRecordScreenKind.Confirmation && confirmation == Confirmation.StartCap)
        {
            button = Joined().Select(id => pressed[id]).FirstOrDefault(value => value != 0);
            direction = Joined().Select(id => moved[id]).FirstOrDefault(value => value != 0);
        }
        int dx = Horizontal(direction), dy = Vertical(direction);
        if (button == 0 && direction == 0) return;
        switch (screen)
        {
            case SourceRecordScreenKind.Options:
                if (dx != 0)
                { cursor = Wrap(cursor + dx, 3); Cue(SourceCabinetRecordSound.OptionCursor); }
                else if (dy > 0 || (button & 4) != 0) { OpenStorage(); break; }
                if ((button & 2) != 0) { BackRequested = true; active = false; }
                else if ((button & 1) != 0)
                {
                    if (cursor == 0)
                    {
                        Confirm(Confirmation.StartCap, "ENTER CREATE A PLAY?");
                        Cue(SourceCabinetRecordSound.OptionSelect);
                    }
                    else if (cursor == 2)
                    {
                        foreach (int id in Joined()) SelectGuest(id, "SELECT TEAM - GUEST");
                        CompleteSelection();
                    }
                    else
                    {
                        editAfterEntry = false;
                        Open(SourceRecordScreenKind.Selection);
                    }
                }
                break;
            case SourceRecordScreenKind.Career:
                if (dx != 0) careerPage = Wrap(careerPage + dx, 4);
                MoveMenu(dy, 7);
                if ((button & 2) != 0) ReturnToSelection();
                else if ((button & 1) != 0) CareerAction();
                break;
            case SourceRecordScreenKind.CustomSlots:
                if (dx != 0 || dy != 0)
                { selectedSlot = Wrap(selectedSlot + dx + dy * 3, 9); Cue(SourceCabinetRecordSound.PlayCursor); }
                if ((button & 2) != 0) ExitCustomBlock();
                else if ((button & 4) != 0) OpenAudibles();
                else if ((button & 1) != 0) EditSlot();
                break;
            case SourceRecordScreenKind.Editor:
                MoveMenu(dy, 13, SourceCabinetRecordSound.CapCursor);
                if (dx != 0) ChangeEditorValue(dx);
                if ((button & 2) != 0) Confirm(Confirmation.Discard, "DISCARD UNSAVED PLAY CHANGES?");
                else if ((button & 1) != 0) EditorAction();
                break;
            case SourceRecordScreenKind.Formation:
                if (dy != 0)
                {
                    int next = dy < 0 ? Math.Max(0, formation - 1) : Wrap(formation + 1, 5);
                    if (next != formation) { formation = next; Cue(SourceCabinetRecordSound.FormationCursor); }
                }
                if ((button & 2) != 0) Open(SourceRecordScreenKind.Editor);
                else if ((button & 1) != 0)
                {
                    ApplyFormation(); Open(SourceRecordScreenKind.Editor);
                    Cue(SourceCabinetRecordSound.FormationSelect);
                }
                break;
            case SourceRecordScreenKind.Route:
                routeX = Math.Clamp(routeX + dx, 1, 39);
                routeY = Math.Clamp(routeY - dy, 1, 43);
                if ((button & 4) != 0)
                {
                    if (routeNode == 0) Open(SourceRecordScreenKind.Editor);
                    else { routeNode--; routeX = route[routeNode].X; routeY = route[routeNode].Y; }
                }
                else if ((button & 3) != 0)
                {
                    if (routeNode == 0) PlantRoute(SourceCustomPointFlags.None, false);
                    else { Open(SourceRecordScreenKind.RouteAction); Cue(SourceCabinetRecordSound.RoutePoint); }
                }
                break;
            case SourceRecordScreenKind.RouteAction:
                MoveMenu(dy, 6, SourceCabinetRecordSound.RouteMenuCursor);
                if ((button & 4) != 0) Open(SourceRecordScreenKind.Route);
                else if ((button & 3) != 0)
                {
                    if (cursor == 4) DeleteRouteNode();
                    else PlantRoute(cursor == 1 ? SourceCustomPointFlags.Juke :
                        cursor == 2 ? SourceCustomPointFlags.Spin :
                        cursor == 3 ? SourceCustomPointFlags.Turbo : SourceCustomPointFlags.None, cursor == 5);
                }
                break;
            case SourceRecordScreenKind.PlayName:
                CapturePlayName(button, direction);
                break;
            case SourceRecordScreenKind.Audibles:
                MoveMenu(dy, 8, SourceCabinetRecordSound.PlayCursor);
                if (cursor < 6 && dx != 0) ChangeAudible(dx);
                if (cursor < 6 && (button & 4) != 0)
                {
                    EnsureAudibleRow(cursor / 3);
                    audibleDraft[cursor / 3][cursor % 3] *= -1;
                    Cue(SourceCabinetRecordSound.PlaySelect);
                }
                if ((button & 2) != 0) Open(customBlockOpen ? SourceRecordScreenKind.CustomSlots : SourceRecordScreenKind.Career);
                else if ((button & 1) != 0)
                {
                    if (cursor < 6) cursor = (cursor + 1) % 6;
                    else if (cursor == 7) { Array.Clear(audibleDraft[0]); Array.Clear(audibleDraft[1]); message = "SOURCE DEFAULT ROWS RESTORED - SAVE TO KEEP"; }
                    else SaveAudibles();
                }
                break;
            case SourceRecordScreenKind.LocalStorage:
                if (dy != 0) { importArmed = exportArmed = false; MoveMenu(dy, 5); }
                if ((button & 2) != 0) CloseStorage();
                else if ((button & 1) != 0) StorageAction();
                break;
            case SourceRecordScreenKind.Confirmation:
                if (dy != 0 || dx != 0)
                {
                    cursor = cursor == 0 ? 1 : 0;
                    Cue(confirmation == Confirmation.StartCap ? SourceCabinetRecordSound.YesNoCursor : SourceCabinetRecordSound.CapYesNoCursor);
                }
                if ((button & 1) != 0 && confirmation != Confirmation.StartCap) Cue(SourceCabinetRecordSound.CapYesNoSelect);
                if ((button & 2) != 0 || (button & 1) != 0 && cursor == 0) CancelConfirmation();
                else if ((button & 1) != 0 && cursor == 1) ConfirmAction();
                break;
            case SourceRecordScreenKind.Leaderboard:
                if (dx != 0) leaderboard = Wrap(leaderboard + dx, 13);
                if ((button & 3) != 0) Open(SourceRecordScreenKind.Career);
                break;
            case SourceRecordScreenKind.WorldRecords:
                if ((button & 3) != 0) Open(SourceRecordScreenKind.Career);
                break;
            case SourceRecordScreenKind.Final:
                if (finalState == FinalState.Failed)
                {
                    if ((button & 1) != 0) RetryFinal();
                    else if ((button & 2) != 0) Confirm(Confirmation.DeclineFinal, "CONTINUE WITHOUT SAVING THIS MATCH?");
                    else if ((button & 4) != 0) OpenStorage();
                }
                else if ((button & 1) != 0 && SelectedRecord(focus) != null) Open(SourceRecordScreenKind.Career);
                else if ((button & 4) != 0) OpenStorage();
                else if ((button & 2) != 0) active = false;
                break;
        }
    }

    public void Continue(int stationId)
    {
        if (!active || busy || (participants & 1 << stationId) == 0) return;
        var pressed = new int[4]; pressed[stationId] = 1;
        Capture(pressed, new int[4]);
    }

    private void CaptureStation(int id, int button, int direction)
    {
        Station station = stations[id];
        if (station.Step == EntryStep.Name || station.Step == EntryStep.Pin)
        {
            bool pin = station.Step == EntryStep.Pin;
            int old = station.Cursor;
            station.Cursor = MoveKeyboard(station.Cursor, direction, pin ? 3 : 4, pin ? 12 : 32);
            if (old != station.Cursor) Cue(editAfterEntry ? SourceCabinetRecordSound.CapLetterCursor : SourceCabinetRecordSound.NameCursor);
            if (button != 0) EnterCharacter(id, pin);
            return;
        }
        if (station.Step == EntryStep.Choice)
        {
            if (Vertical(direction) != 0) { station.Yes = Vertical(direction) < 0; Cue(SourceCabinetRecordSound.YesNoCursor); }
            if ((button & 1) != 0)
            {
                bool answer = station.Yes;
                if (answer) { BeginName(id); station.Yes = answer; }
                else SelectGuest(id, "NO NAME ENTRY - GUEST");
                Cue(SourceCabinetRecordSound.YesNoSelect);
            }
        }
        else if (station.Step == EntryStep.Missing)
        {
            if ((button & 1) != 0) CreateRecord(id, false);
            else if ((button & 2) != 0) BeginName(id);
            else if ((button & 4) != 0) SelectGuest(id, "DELIBERATE GUEST - NOT SAVED");
        }
        else if (station.Step is EntryStep.Unavailable or EntryStep.Duplicate)
        {
            if ((button & 1) != 0)
            {
                if (station.Step == EntryStep.Duplicate) BeginName(id);
                else StartOperation(async () =>
                {
                    await store.LoadAsync();
                    await ApplyUi(() => { message = LoadMessage(); station.Step = EntryStep.Choice; });
                });
            }
            else if ((button & 2) != 0) SelectGuest(id, "DELIBERATE GUEST - NOT SAVED");
            else if ((button & 4) != 0) { focus = id; OpenStorage(); }
        }
        else if (station.Step == EntryStep.Selected)
        {
            if ((button & 2) != 0) { station.Step = EntryStep.Choice; station.State = SourceRecordSelectionState.Unavailable; station.Identity = null; }
            else if ((button & 1) != 0 && station.Identity != null)
            { focus = id; Open(SourceRecordScreenKind.Career); }
            else if ((button & 4) != 0 && (CanStartMatch || SelectedHistoryUnavailable)) CompleteSelection();
        }
    }

    private void BeginName(int id)
    {
        Station station = stations[id];
        station.ClearEntry(); station.Identity = null; station.State = SourceRecordSelectionState.Unavailable;
        station.Step = EntryStep.Name;
    }

    private void EnterCharacter(int id, bool pin)
    {
        Station station = stations[id];
        int position = station.Cursor, end = pin ? 12 : 32;
        if (position == end - (pin ? 1 : 2))
        {
            if (station.Entered > 0) station.Entered--;
        }
        else if (!pin && position == 31)
        {
            if (station.Entered == 0) return;
            while (station.Entered < 6) station.Name[station.Entered++] = ' ';
        }
        else
        {
            if (pin) station.Pin[station.Entered++] = PinAlphabet[position];
            else station.Name[station.Entered++] = Alphabet[position];
        }
        Cue(editAfterEntry ? SourceCabinetRecordSound.CapLetterSelect : SourceCabinetRecordSound.NameSelect);
        if (!pin && station.Entered == 6)
        { station.Step = EntryStep.Pin; station.Cursor = station.Entered = 0; }
        else if (pin && station.Entered == 4)
        {
            if (station.Name[0] == '-') { SelectGuest(id, "SOURCE GUEST PREFIX - NO RECORD"); return; }
            station.Identity = new SourceRecordIdentity(new string(station.Name), new string(station.Pin));
            Array.Clear(station.Pin);
            station.Step = EntryStep.Lookup;
            if (!sourceMenuBound || !sourceMenuRunning) ResolveLookup(id);
        }
    }

    private void ResolveLookup(int id)
    {
        Station station = stations[id];
        if (Joined().Any(other => other != id && stations[other].Identity != null &&
            stations[other].Step is EntryStep.Selected or EntryStep.Missing &&
            station.Identity.Equals(stations[other].Identity)))
        {
            station.Step = EntryStep.Duplicate; station.Message = "RECORD ALREADY SELECTED AT ANOTHER STATION";
            return;
        }
        if (!store.IsWritable)
        {
            if (store.LoadState == SourceRecordStoreStatus.NotFound)
            { station.Step = EntryStep.Missing; station.Message = "NEW CABINET / RECORD - CREATE?"; }
            else { station.Step = EntryStep.Unavailable; station.Message = LoadMessage(); }
            return;
        }
        SourcePlayerRecord found = store.Current.Find(station.Identity);
        if (found == null) { station.Step = EntryStep.Missing; station.Message = "RECORD NOT FOUND - CREATE?"; }
        else SelectRecord(id, false);
    }

    private void CreateRecord(int id, bool replace)
    {
        focus = id;
        StartOperation(async () =>
        {
            if (store.LoadState == SourceRecordStoreStatus.NotFound)
            {
                SourceRecordStoreResult initialized = await store.InitializeNewAsync();
                if (!initialized.Succeeded) { await ApplyUi(() => SetEntryFailure(id)); return; }
            }
            SourceRecordStoreResult result = await store.CreateAsync(stations[id].Identity, replace);
            await ApplyUi(() =>
            {
                if (result.Code == "SOURCE_RECORD_CAPACITY_REPLACEMENT_REQUIRES_CONSENT")
                { Confirm(Confirmation.ReplaceRecord, "CABINET FULL - REPLACE SOURCE LEAST-USED RECORD?"); return; }
                if (result.Succeeded) { RefreshSelectedRecords(); SelectRecord(id, true); }
                else SetEntryFailure(id);
            });
        });
    }

    private void SelectRecord(int id, bool created)
    {
        Station station = stations[id];
        station.State = created ? SourceRecordSelectionState.KnownNew : SourceRecordSelectionState.KnownSaved;
        station.Step = EntryStep.Selected; station.Entered = station.Cursor = 0;
        station.Message = created ? "NEW LOCAL RECORD SAVED" :
            store.Current.Find(station.Identity).CustomPlays.Any(play => play?.Exists == true) ?
                "RECORD FOUND / CREATED PLAYS LOADED" : "RECORD FOUND - NO CREATED PLAYS";
        if (!created) Cue(SourceCabinetRecordSound.RecordFound);
        if (editAfterEntry && id == focus) { editAfterEntry = false; Open(SourceRecordScreenKind.CustomSlots); }
    }

    private void SelectGuest(int id, string reason)
    {
        Station station = stations[id];
        station.ClearEntry(); station.Identity = null; station.State = SourceRecordSelectionState.Guest;
        station.Step = EntryStep.Selected; station.Message = reason;
    }

    private void SetEntryFailure(int id)
    { stations[id].Step = EntryStep.Unavailable; stations[id].Message = LoadMessage(); message = LoadMessage(); }

    private void CompleteSelection()
    {
        if (SelectedHistoryUnavailable)
        {
            message = "CUSTOM PLAY HISTORY UNAVAILABLE - ENTER CAP AND EXIT TO SAVE OR CHOOSE GUEST";
            return;
        }
        if (!CanStartMatch) { message = "EACH JOINED STATION MUST CHOOSE A RECORD OR GUEST"; return; }
        active = false; SelectionComplete = true;
    }

    private void ReturnToSelection()
    {
        Open(SourceRecordScreenKind.Selection);
        if (CanStartMatch) message = "C CONTINUES TO TEAM SELECTION";
    }

    public SourceSavedRecordSession BindMatch(SourceMatchConfiguration configuration)
    {
        DataCatalog.Require(!SelectedHistoryUnavailable, "SOURCE_CUSTOM_BLOCK_HISTORY_UNAVAILABLE: a successful CAP exit is required.");
        DataCatalog.Require(CanStartMatch, "SOURCE_RECORD_SELECTION_UNAVAILABLE: no automatic guest substitution.");
        var selections = configuration.Stations.Select(configured =>
        {
            Station station = stations[configured.StationId];
            if (station.State == SourceRecordSelectionState.Guest)
                return new SourceStationRecordSelection(configured.StationId, SourceRecordSelectionState.Guest);
            SourcePlayerRecord record = store.Current.Find(station.Identity);
            DataCatalog.Require(record != null, "SOURCE_SELECTED_RECORD_CHANGED_REENTER");
            return new SourceStationRecordSelection(configured.StationId, station.State, record.Identity, record);
        }).ToArray();
        boundSession = new SourceSavedRecordSession(configuration, selections);
        sourceMenuRunning = false;
        observedFinal = null; pendingFinal = null; finalContext = null; finalState = FinalState.None;
        active = false; SelectionComplete = false;
        return boundSession;
    }

    public int ReadGamesPlayed(int stationId)
    {
        SourceCabinetQuantizer.ValidateStationId(stationId);
        if ((participants & 1 << stationId) == 0) return 0;
        Station station = stations[stationId];
        if (station.State == SourceRecordSelectionState.Guest) return 0;
        SourcePlayerRecord record = SelectedRecord(stationId);
        if (record != null) return record.Career.GamesPlayed;
        if (station.State == SourceRecordSelectionState.KnownNew && station.Identity != null) return 0;
        throw new InvalidOperationException("SOURCE_RECORD_EXPERIENCE_UNAVAILABLE");
    }

    public void ObserveFinal(SourceCabinetFlowView flow)
    {
        if (flow?.FinalMatch == null || ReferenceEquals(observedFinal, flow.FinalMatch)) return;
        DataCatalog.Require(observedFinal == null && boundSession != null,
            "SOURCE_RECORD_FINAL_BOUNDARY: one immutable final belongs to one admitted match.");
        observedFinal = flow.FinalMatch;
        int periods = (flow.FinalMatch.Period.CompletedQuarterIndex ?? flow.FinalMatch.Period.QuarterIndex) + 1;
        if (!SourceCompletedRecordMatch.IsSourceSaveEligible(periods, flow.FinalHomeScore, flow.FinalAwayScore))
        {
            finalState = FinalState.Declined; message = "SOURCE PARTIAL / TIED MATCH - CAREER NOT SAVED";
            return;
        }
        throw new InvalidOperationException("SOURCE_FINAL_COMMIT_BARRIER_NOT_BOUND");
    }

    public void BeginFinalCommit(SourceFinalRecordCommit context)
    {
        ArgumentNullException.ThrowIfNull(context);
        DataCatalog.Require(finalContext == null && observedFinal == null && boundSession != null &&
            ReferenceEquals(context.FinalMatch.Configuration, boundSession.Configuration) &&
            context.ParticipatingStationMask == participants,
            "SOURCE_RECORD_FINAL_BOUNDARY: one real game_proc final must match the admitted record session.");
        finalContext = context; observedFinal = context.FinalMatch;
        if (boundSession.Stations.All(station => station.State == SourceRecordSelectionState.Guest))
        { finalState = FinalState.Declined; message = "GUEST MATCH - NO NAMED CAREER TO SAVE"; return; }
        long previous = 0;
        store.Current?.CommittedMatches.TryGetValue(sessionId, out previous);
        pendingFinal = SourceCompletedRecordMatch.FromFixedParticipation(
            new SourceRecordMatchId(sessionId, checked(previous + 1)), boundSession, context.FinalMatch,
            context.HomeScore, context.AwayScore, context.CompletedPeriodCount);
        SaveFinal();
    }

    public SourceRecordCommitBarrierState ReadFinalCommitState(SourceFinalRecordCommit context)
    {
        DataCatalog.Require(ReferenceEquals(context, finalContext),
            "SOURCE_RECORD_FINAL_CONTEXT: poll the SAME real game_proc final context.");
        return finalState switch
        {
            FinalState.Pending => SourceRecordCommitBarrierState.Pending,
            FinalState.Committed => SourceRecordCommitBarrierState.Committed,
            FinalState.Declined => SourceRecordCommitBarrierState.Declined,
            _ => SourceRecordCommitBarrierState.Failed
        };
    }

    private void SaveFinal()
    {
        finalState = FinalState.Pending;
        Open(SourceRecordScreenKind.Final);
        message = "SAVING FINAL MATCH - KEEP THIS PAGE OPEN";
        StartOperation(async () =>
        {
            SourceRecordStoreResult result = await store.CommitMatchAsync(pendingFinal);
            if (result.Succeeded)
            { finalState = FinalState.Committed; RefreshSelectedRecords(); message = "FINAL MATCH SAVED"; active = false; }
            else { finalState = FinalState.Failed; message = "MATCH NOT SAVED - " + LoadMessage(); }
        });
    }

    private void RetryFinal()
    {
        if (pendingFinal == null) return;
        finalState = FinalState.Pending;
        message = "RELOADING AUTHORITATIVE CABINET BEFORE RETRY";
        StartOperation(async () =>
        {
            SourceRecordStoreResult loaded = await store.LoadAsync();
            if (!loaded.Succeeded) { finalState = FinalState.Failed; message = LoadMessage(); return; }
            SourceRecordStoreResult result = await store.CommitMatchAsync(pendingFinal);
            finalState = result.Succeeded ? FinalState.Committed : FinalState.Failed;
            if (result.Succeeded) RefreshSelectedRecords();
            message = result.Succeeded ? "FINAL MATCH SAVED" : "MATCH NOT SAVED - " + LoadMessage();
            active = !result.Succeeded;
        });
    }

    public void ShowPostMatch()
    {
        if (busy || BlocksGameplay) return;
        focus = Joined().FirstOrDefault(id => SelectedRecord(id) != null, Joined().First());
        Open(SourceRecordScreenKind.Final);
    }

    private void CareerAction()
    {
        switch (cursor)
        {
            case 0: Open(SourceRecordScreenKind.CustomSlots); break;
            case 1: OpenAudibles(); break;
            case 2: Open(SourceRecordScreenKind.Leaderboard); break;
            case 3: Open(SourceRecordScreenKind.WorldRecords); break;
            case 4: ReturnToSelection(); break;
            case 5: OpenStorage(); break;
            case 6: Confirm(Confirmation.DeleteRecord, "DELETE THIS RECORD AND ALL OF ITS CUSTOM PLAYS?"); break;
        }
    }

    private void ExitCustomBlock()
    {
        if (!customBlockOpen) { Open(SourceRecordScreenKind.Career); return; }
        SourceRecordIdentity identity = stations[focus].Identity;
        StartOperation(async () =>
        {
            SourceRecordStoreResult result = await store.CommitCustomBlockAsync(identity);
            await ApplyUi(() =>
            {
                message = result.Succeeded ? "CUSTOM BLOCK SAVED" : "CUSTOM BLOCK NOT SAVED - " + LoadMessage();
                if (result.Succeeded)
                {
                    customBlockOpen = false;
                    RefreshSelectedRecords();
                    Open(SourceRecordScreenKind.Career);
                }
            });
        });
    }

    private void EditSlot()
    {
        SourcePlayerRecord record = SelectedRecord(focus);
        if (record == null || !store.IsWritable) { message = LoadMessage(); return; }
        if (selectedSlot >= SourcePlayerRecords.UsableSlots(record))
        { message = "SOURCE SLOT REQUIRES " + WinsNeeded[selectedSlot] + " WINS"; return; }
        SourceCustomPlay play = record.CustomPlays[selectedSlot];
        if (play != null && (play.Flags & SourceCustomPlayFlags.Protected) != 0)
        { message = "SOURCE PROTECTED PLAY - CANNOT EDIT OR DELETE"; return; }
        draft = play ?? SourceCustomPlayCodec.CreateFromFormation(0, "new", "play " + (selectedSlot + 1));
        formation = 0; Open(SourceRecordScreenKind.Editor);
        Cue(SourceCabinetRecordSound.PlaySelect);
    }

    private void EditorAction()
    {
        Cue(SourceCabinetRecordSound.CapSelect);
        switch (cursor)
        {
            case 0:
                if (Enumerable.Range(0, 3).Any(id => draft.Point(id, 1).X != 0))
                    Confirm(Confirmation.Formation, "CHANGING FORMATION CLEARS ALL PASS ROUTES - CONTINUE?");
                else Open(SourceRecordScreenKind.Formation);
                break;
            case 1: case 2: case 3:
                receiver = cursor - 1;
                if (draft.Point(receiver, 1).X != 0) Confirm(Confirmation.Route, "DELETE THIS RECEIVER'S EXISTING ROUTE AND EDIT?");
                else BeginRoute();
                break;
            case 4: case 5: case 6: case 7: ChangeEditorValue(1); break;
            case 8: BeginPlayName(); break;
            case 9: message = "SAVE THIS PLAY FIRST; AUDIBLES ARE AVAILABLE FROM THE RECORD PAGE"; break;
            case 10: SavePlay(); break;
            case 11: OpenStorage(); break;
            case 12: Confirm(Confirmation.Discard, "DISCARD UNSAVED PLAY CHANGES?"); break;
        }
    }

    private void ChangeEditorValue(int dx, bool buttonSelected = false)
    {
        SourceCustomPlayFlags flags = draft.Flags;
        int motion = draft.Motion;
        if (cursor == 4)
        {
            int primary = (flags & SourceCustomPlayFlags.PrimaryReceiver1) != 0 ? 1 :
                (flags & SourceCustomPlayFlags.PrimaryReceiver2) != 0 ? 2 :
                (flags & SourceCustomPlayFlags.PrimaryReceiver3) != 0 ? 3 : 0;
            primary = Wrap(primary + dx, 4);
            flags &= ~(SourceCustomPlayFlags.PrimaryReceiver1 | SourceCustomPlayFlags.PrimaryReceiver2 | SourceCustomPlayFlags.PrimaryReceiver3);
            if (primary != 0) flags |= (SourceCustomPlayFlags)(4 << primary);
        }
        else if (cursor == 5) flags ^= SourceCustomPlayFlags.QuarterbackFar;
        else if (cursor == 6)
        {
            int selected = motion <= 2 ? motion + 1 : 0;
            selected = Wrap(selected + dx, 4); motion = selected == 0 ? 255 : selected - 1;
        }
        else if (cursor == 7) flags ^= SourceCustomPlayFlags.Protected;
        else return;
        var changed = new SourceCustomPlay(draft.Name1, draft.Name2, motion, flags, draft.Points);
        SourceCustomPlayValidation validation = SourceCustomPlayCodec.ValidateAndNormalize(changed);
        if (validation.Valid) { draft = validation.Play; if (buttonSelected) Cue(SourceCabinetRecordSound.CapSelect); }
        else message = "SOURCE VALIDATION: " + validation.Error.ToString().ToUpperInvariant();
    }

    private void ApplyFormation()
    {
        SourceCustomPlay fresh = SourceCustomPlayCodec.CreateFromFormation(formation, draft.Name1, draft.Name2);
        draft = new SourceCustomPlay(fresh.Name1, fresh.Name2, fresh.Motion,
            fresh.Flags | (draft.Flags & SourceCustomPlayFlags.Protected), fresh.Points);
    }

    private void BeginRoute()
    {
        route.Clear(); route.Add(draft.Point(receiver, 0)); routeNode = 0;
        routeX = route[0].X; routeY = route[0].Y;
        Open(SourceRecordScreenKind.Route); message = "SET RECEIVER START POSITION";
    }

    private void PlantRoute(SourceCustomPointFlags flags, bool done)
    {
        bool actionMenu = screen == SourceRecordScreenKind.RouteAction;
        var changed = route.Take(routeNode).ToList();
        changed.Add(new SourceCustomPoint(routeX, routeY, flags));
        try
        {
            draft = SourceCustomPlayCodec.ReplaceRoute(draft, receiver, changed);
            route.Clear(); route.AddRange(changed);
            if (done || routeNode == 8) { Open(SourceRecordScreenKind.Editor); message = "SOURCE ROUTE UPDATED - SAVE PLAY TO KEEP"; }
            else
            {
                routeNode++; routeY = Math.Min(43, routeY + 2);
                Open(SourceRecordScreenKind.Route); message = "SET NEXT ACTION POINT";
            }
            if (actionMenu) Cue(SourceCabinetRecordSound.RouteMenu);
        }
        catch (ArgumentException) { message = "SOURCE ROUTE REJECTED - CHECK BOUNDS / OVERLAP / SPECIAL LIMITS"; Open(SourceRecordScreenKind.Route); }
    }

    private void DeleteRouteNode()
    {
        if (routeNode == 0) { Open(SourceRecordScreenKind.Route); return; }
        routeNode--;
        routeX = route[routeNode].X; routeY = route[routeNode].Y;
        var retained = route.Take(Math.Max(1, routeNode)).ToArray();
        draft = SourceCustomPlayCodec.ReplaceRoute(draft, receiver, retained);
        Open(SourceRecordScreenKind.Route);
    }

    private void SavePlay()
    {
        SourceCustomPlayValidation validation = SourceCustomPlayCodec.ValidateAndNormalize(draft);
        if (!validation.Valid) { message = "SOURCE VALIDATION: " + validation.Error.ToString().ToUpperInvariant(); return; }
        SourceCustomPlay savedDraft = validation.Play;
        StartOperation(async () =>
        {
            SourceRecordStoreResult result = await store.SaveCustomPlayAsync(stations[focus].Identity, selectedSlot, savedDraft);
            await ApplyUi(() =>
            {
                message = result.Succeeded ? "CUSTOM PLAY SAVED - AVAILABLE ON THE LAST OFFENSE PAGE" : "CUSTOM PLAY NOT SAVED - " + LoadMessage();
                if (result.Succeeded) { draft = null; Open(SourceRecordScreenKind.CustomSlots); }
            });
        });
    }

    private void BeginPlayName()
    {
        Array.Fill(playName[0], ' '); Array.Fill(playName[1], ' ');
        nameField = namePosition = nameCursor = 0;
        Open(SourceRecordScreenKind.PlayName);
    }

    private void CapturePlayName(int button, int direction)
    {
        int previous = nameCursor;
        nameCursor = MoveKeyboard(nameCursor, direction, 4, 32);
        if (previous != nameCursor) Cue(SourceCabinetRecordSound.CapLetterCursor);
        if (button == 0) return;
        if (nameCursor == 30)
        { if (namePosition > 0) playName[nameField][--namePosition] = ' '; }
        else if (nameCursor == 31) FinishNameField();
        else
        {
            playName[nameField][namePosition++] = Alphabet[nameCursor];
            if (namePosition == 8) FinishNameField();
        }
        Cue(SourceCabinetRecordSound.CapLetterSelect);
    }

    private void FinishNameField()
    {
        if (nameField == 0) { nameField = 1; namePosition = 0; return; }
        draft = new SourceCustomPlay(new string(playName[0]).TrimEnd(), new string(playName[1]).TrimEnd(),
            draft.Motion, draft.Flags, draft.Points);
        Open(SourceRecordScreenKind.Editor);
    }

    private void OpenAudibles()
    {
        SourcePlayerRecord record = SelectedRecord(focus);
        if (record == null) return;
        record.Audibles.Offense.CopyTo(audibleDraft[0], 0);
        record.Audibles.Defense.CopyTo(audibleDraft[1], 0);
        Open(SourceRecordScreenKind.Audibles);
    }

    private void EnsureAudibleRow(int side)
    {
        if (audibleDraft[side][0] != 0) return;
        short[] defaults = side == 0 ? new short[] { 1, 2, 3 } : new short[] { 1, 8, 7 };
        Array.Copy(defaults, audibleDraft[side], 3);
    }

    private void ChangeAudible(int dx)
    {
        int side = cursor / 3, index = cursor % 3;
        EnsureAudibleRow(side);
        int sign = audibleDraft[side][index] < 0 ? -1 : 1, id = Math.Abs(audibleDraft[side][index]);
        SourcePlayerRecord record = SelectedRecord(focus);
        for (int attempt = 0; attempt < 45; attempt++)
        {
            id = Wrap(id - 1 + dx, side == 0 ? 45 : 18) + 1;
            if (side != 0 || id <= 36 || record.CustomPlays[id - 37]?.Exists == true) break;
        }
        audibleDraft[side][index] = (short)(id * sign);
        Cue(SourceCabinetRecordSound.PlayCursor);
    }

    private void SaveAudibles()
    {
        var audibles = new SourceRecordAudibles(audibleDraft[0], audibleDraft[1]);
        StartOperation(async () =>
        {
            SourceRecordStoreResult result = await store.SaveAudiblesAsync(stations[focus].Identity, audibles);
            await ApplyUi(() =>
            {
                message = result.Succeeded ? "SIGNED SOURCE AUDIBLES SAVED" : "AUDIBLES NOT SAVED - " + LoadMessage();
                if (result.Succeeded) Open(customBlockOpen ? SourceRecordScreenKind.CustomSlots : SourceRecordScreenKind.Career);
            });
        });
    }

    private void OpenStorage()
    {
        storageReturnScreen = screen; storageReturnCursor = cursor; importArmed = exportArmed = false;
        Open(SourceRecordScreenKind.LocalStorage);
    }

    private void CloseStorage()
    { importArmed = exportArmed = false; Open(BlocksGameplay ? SourceRecordScreenKind.Final : storageReturnScreen, storageReturnCursor); }

    private void StorageAction()
    {
        switch (cursor)
        {
            case 0:
                StartOperation(async () =>
                {
                    await store.LoadAsync();
                    await ApplyUi(() => { RefreshSelectedRecords(); message = LoadMessage(); });
                }); break;
            case 1: Confirm(Confirmation.Recover, store.LoadState == SourceRecordStoreStatus.UnsupportedVersion ?
                "REPLACE UNKNOWN NEWER STORE WITH AUTHENTICATED BACKUP? OLD DATA IS QUARANTINED." :
                "RESTORE AUTHENTICATED BACKUP? OLD CORRUPT DATA IS QUARANTINED."); break;
            case 2: importArmed = true; exportArmed = false; message = "CHOOSE LOCAL PORTABLE FILE WITH THE BROWSER FILE BUTTON"; break;
            case 3: exportArmed = true; importArmed = false; message = "USE THE DOWNLOAD BUTTON - FILE INCLUDES PRIVATE LOCAL PINS"; break;
            case 4: CloseStorage(); break;
        }
    }

    public void StagePortableImport(string envelope)
    {
        if (!CanImport || envelope == null || envelope.Length > SourcePlayerRecordStore.MaximumEnvelopeCharacters)
            throw new InvalidOperationException("SOURCE_RECORD_IMPORT_NOT_ARMED_OR_TOO_LARGE");
        stagedImport = envelope; importArmed = false;
        Confirm(Confirmation.Import, "REPLACE ALL LOCAL RECORDS / CUSTOM PLAYS WITH THIS PORTABLE FILE?");
    }

    public async Task<string> ExportPortableAsync()
    {
        if (!CanExport) throw new InvalidOperationException("SOURCE_RECORD_EXPORT_NOT_ARMED");
        exportArmed = false;
        SourceRecordExport exported = await store.ExportAsync();
        await ApplyUi(() => message = exported.Result.Succeeded ? "PORTABLE FILE READY - KEEP PRIVATE LOCAL PINS SAFE" : LoadMessage());
        return exported.Result.Succeeded ? exported.Envelope : null;
    }

    private void Confirm(Confirmation action, string prompt)
    {
        confirmation = action; returnScreen = screen; returnCursor = cursor; message = prompt;
        Open(SourceRecordScreenKind.Confirmation);
    }

    private void CancelConfirmation()
    {
        if (confirmation == Confirmation.Import) stagedImport = null;
        confirmation = Confirmation.None; Open(returnScreen, returnCursor);
    }

    private void ConfirmAction()
    {
        Confirmation action = confirmation; confirmation = Confirmation.None;
        Open(returnScreen, returnCursor);
        switch (action)
        {
            case Confirmation.ReplaceRecord: CreateRecord(focus, true); break;
            case Confirmation.DeleteRecord:
                StartOperation(async () =>
                {
                    SourceRecordStoreResult result = await store.DeleteAsync(stations[focus].Identity);
                    await ApplyUi(() =>
                    {
                        message = result.Succeeded ? "RECORD DELETED" : LoadMessage();
                        if (result.Succeeded) { BeginName(focus); Open(SourceRecordScreenKind.Selection); }
                    });
                }); break;
            case Confirmation.Recover:
                StartOperation(async () =>
                {
                    await store.RecoverAsync(true);
                    await ApplyUi(() => { RefreshSelectedRecords(); message = LoadMessage(); });
                }); break;
            case Confirmation.Import:
                string envelope = stagedImport; stagedImport = null;
                StartOperation(async () =>
                {
                    SourceRecordStoreResult result = await store.ImportAsync(envelope, replaceFailedStore: true);
                    await ApplyUi(() =>
                    {
                        message = result.Succeeded ? "PORTABLE RECORDS IMPORTED - RESELECT CHANGED IDENTITIES" : LoadMessage();
                        RefreshSelectedRecords();
                    });
                }); break;
            case Confirmation.Formation: Open(SourceRecordScreenKind.Formation); break;
            case Confirmation.Route: BeginRoute(); break;
            case Confirmation.Discard: draft = null; Open(SourceRecordScreenKind.CustomSlots); break;
            case Confirmation.DeclineFinal:
                finalState = FinalState.Declined; message = "MATCH NOT SAVED - DELIBERATELY DECLINED"; active = false; break;
            case Confirmation.StartCap:
                editAfterEntry = true; BeginName(focus); Open(SourceRecordScreenKind.Selection); break;
        }
    }

    private void RefreshSelectedRecords()
    {
        if (!store.IsWritable) return;
        foreach (int id in Joined())
            if (stations[id].Identity != null && store.Current.Find(stations[id].Identity) == null)
            {
                stations[id].State = SourceRecordSelectionState.Unavailable;
                stations[id].Step = EntryStep.Unavailable; stations[id].Message = "RECORD CHANGED - REENTER OR CHOOSE GUEST";
            }
            else if (stations[id].Identity != null)
            { stations[id].State = SourceRecordSelectionState.KnownSaved; stations[id].Step = EntryStep.Selected; }
    }

    private void StartOperation(Func<Task> operation)
    {
        DataCatalog.Require(!busy, "SOURCE_RECORD_OPERATION_ALREADY_PENDING");
        busy = true; uiReady = false;
        PendingOperation = Execute();
        async Task Execute()
        {
            try { await operation(); }
            catch (Exception e) when (e is not OutOfMemoryException and not AccessViolationException)
            {
                await ApplyUi(() =>
                {
                    message = "RECORD OPERATION FAILED - RELOAD OR RECOVER";
                    if (finalState == FinalState.Pending) finalState = FinalState.Failed;
                });
            }
            finally { await ApplyUi(() => { busy = false; uiReady = false; }); }
        }
    }

    private string LoadMessage() => store.Notification.Status switch
    {
        SourceRecordStoreStatus.NotFound => "NO LOCAL CABINET - CREATE ONLY AFTER EXPLICIT CONFIRMATION",
        SourceRecordStoreStatus.Corrupt => "CORRUPT STORE - RELOAD / RECOVER / IMPORT OR DELIBERATE GUEST",
        SourceRecordStoreStatus.UnsupportedVersion => "UNKNOWN NEWER STORE - PRESERVED; EXPLICIT RECOVERY / IMPORT REQUIRED",
        SourceRecordStoreStatus.Conflict => "ANOTHER TAB SAVED - RELOAD BEFORE RETRY",
        SourceRecordStoreStatus.IoFailure => "STORAGE UNAVAILABLE - PREVIOUS RECORDS RETAINED; RELOAD / RECOVER",
        SourceRecordStoreStatus.InvalidData => "RECORD OPERATION REJECTED - " + store.Notification.Code,
        _ => store.IsWritable ? "LOCAL CABINET READY" : "RECORDS UNAVAILABLE - NO GUEST SUBSTITUTION"
    };

    private SourcePlayerRecord SelectedRecord(int id) => stations[id].Identity == null || store.Current == null ? null :
        store.Current.Find(stations[id].Identity);
    private IEnumerable<int> Joined() => Enumerable.Range(0, 4).Where(id => (participants & 1 << id) != 0);
    private void Open(SourceRecordScreenKind kind, int selected = 0)
    {
        if (kind == SourceRecordScreenKind.CustomSlots && store.SavePolicy == SourceRecordSavePolicy.CustomBlockHistoryV3)
            customBlockOpen = true;
        screen = kind; cursor = selected; active = true;
    }
    private void MoveMenu(int dy, int count, SourceCabinetRecordSound? sound = null)
    { if (dy != 0) { cursor = Wrap(cursor + dy, count); if (sound.HasValue) Cue(sound.Value); } }
    private void Cue(SourceCabinetRecordSound sound)
    {
        if (sourceMenuBound)
        {
            if (menuWake?.IsOriginalProcess == true) menuWake.Emit((SourcePreGameMenuSound)(int)sound);
            return;
        }
        SoundRequested?.Invoke(sound);
    }
    private static int Wrap(int value, int count) => (value % count + count) % count;
    private static int Horizontal(int direction) => ((direction & 8) != 0 ? 1 : 0) - ((direction & 4) != 0 ? 1 : 0);
    private static int Vertical(int direction) => ((direction & 2) != 0 ? 1 : 0) - ((direction & 1) != 0 ? 1 : 0);
    private static int MoveKeyboard(int at, int direction, int columns, int count)
    {
        int dx = Horizontal(direction), dy = Vertical(direction);
        if (dx != 0) return Math.Clamp(at + dx, 0, count - 1);
        if (dy < 0 && at >= columns) return at - columns;
        if (dy > 0 && at < count - columns) return at + columns;
        return at;
    }

    public SourceRecordScreenView View()
    {
        SourcePlayerRecord record = SelectedRecord(focus);
        var rows = new List<SourceRecordScreenRow>();
        var details = new List<SourceRecordScreenRow>();
        string title = screen.ToString().ToUpperInvariant(), prompt = "UP/DOWN CHOOSE   A SELECT   B BACK";
        void Rows(params string[] values)
        { for (int i = 0; i < values.Length; i++) rows.Add(new SourceRecordScreenRow(values[i], cursor == i)); }
        switch (screen)
        {
            case SourceRecordScreenKind.Options:
                title = "CHOOSE OPTION";
                Rows("CREATE PLAY", "ENTER NAME", "SELECT TEAM - GUEST", "C / DOWN - LOCAL RECORDS / RECOVERY");
                prompt = "LEFT/RIGHT CHOOSE   A SELECT   B BACK   C LOCAL RECORDS";
                break;
            case SourceRecordScreenKind.Selection:
                title = "ENTER NAME AND PIN NUMBER";
                prompt = CanStartMatch ? "A RECORD PAGES   B RESELECT   C CONTINUE TO TEAMS" : "CHOOSE AT EACH JOINED STATION - NO AUTOMATIC GUEST";
                break;
            case SourceRecordScreenKind.Career:
                title = "PLAYER RECORD / CAREER";
                Rows("CREATE / EDIT PLAYS", "SET AUDIBLES", "LOCAL HIGH SCORES", "WORLD RECORDS", "CONTINUE TO TEAMS", "LOCAL STORAGE", "DELETE RECORD");
                if (record != null) details.AddRange(CareerDetails(record));
                prompt = "LEFT/RIGHT RECORD PAGE   A SELECT   B STATION CHOICES";
                break;
            case SourceRecordScreenKind.CustomSlots:
                title = "CREATE / EDIT PLAY";
                prompt = "STICK SLOT   A EDIT   B RECORD PAGE   C AUDIBLES";
                break;
            case SourceRecordScreenKind.Editor:
                title = "CREATE A PLAY";
                Rows("CHOOSE FORMATION", "EDIT RECEIVER 1", "EDIT RECEIVER 2", "EDIT RECEIVER 3",
                    "PRIMARY: " + Primary(draft), "QB: " + ((draft.Flags & SourceCustomPlayFlags.QuarterbackFar) != 0 ? "SHOTGUN" : "NEAR"),
                    "MOTION: " + (draft.Motion <= 2 ? "R" + (draft.Motion + 1) : "NONE"),
                    "PROTECT ON SAVE: " + ((draft.Flags & SourceCustomPlayFlags.Protected) != 0 ? "YES" : "NO"),
                    "NAME PLAY", "SET AUDIBLES AFTER SAVE", "SAVE PLAY", "STORAGE / RECOVERY", "DISCARD / EXIT");
                prompt = "UP/DOWN ACTION   LEFT/RIGHT VALUE   A SELECT   B EXIT";
                break;
            case SourceRecordScreenKind.Formation:
                title = "CHOOSE FORMATION";
                for (int i = 0; i < Formations.Length; i++) rows.Add(new SourceRecordScreenRow(Formations[i], formation == i));
                break;
            case SourceRecordScreenKind.Route:
                title = "EDIT RECEIVER " + (receiver + 1);
                details.Add(new SourceRecordScreenRow("POINT " + routeNode + " / 8   GRID " + routeX + "," + routeY));
                prompt = "STICK MOVES   A/B PLANT   C PREVIOUS POINT / EXIT";
                break;
            case SourceRecordScreenKind.RouteAction:
                title = "SET NEXT ACTION POINT";
                Rows("NO SPECIAL", "JUKE", "SPIN", "TURBO", "DELETE NODE", "DONE EDITING ROUTE");
                prompt = "UP/DOWN ACTION   A/B SELECT   C BACK";
                break;
            case SourceRecordScreenKind.PlayName:
                title = "NAME PLAY - LINE " + (nameField + 1);
                details.Add(new SourceRecordScreenRow(new string(playName[0])));
                details.Add(new SourceRecordScreenRow(new string(playName[1])));
                prompt = "STICK CHARACTER   BUTTON SELECT   DEL / END ON KEYBOARD";
                break;
            case SourceRecordScreenKind.Audibles:
                title = "SET SOURCE AUDIBLES";
                for (int i = 0; i < 6; i++)
                {
                    short id = audibleDraft[i / 3][i % 3];
                    rows.Add(new SourceRecordScreenRow((i < 3 ? "OFF " : "DEF ") + (i % 3 + 1) + "  " + id + "  " + AudibleName(i / 3, id), cursor == i));
                }
                rows.Add(new SourceRecordScreenRow("SAVE AUDIBLES", cursor == 6));
                rows.Add(new SourceRecordScreenRow("RESTORE ORIGINAL DEFAULT ROWS", cursor == 7));
                prompt = "LEFT/RIGHT PLAY   C FLIP   A NEXT / SAVE   B BACK";
                break;
            case SourceRecordScreenKind.LocalStorage:
                title = "LOCAL PORTABLE RECORDS";
                Rows("RELOAD / RETRY", "RECOVER AUTHENTICATED BACKUP", "IMPORT LOCAL FILE", "EXPORT LOCAL FILE", "BACK");
                details.Add(new SourceRecordScreenRow("PORTABLE LOGICAL RECORDS - NOT NATIVE CMOS BINARY"));
                details.Add(new SourceRecordScreenRow("FILES INCLUDE LOCAL PINS - KEEP THEM PRIVATE"));
                break;
            case SourceRecordScreenKind.Confirmation:
                title = "EXPLICIT CONFIRMATION REQUIRED";
                Rows("NO - KEEP CURRENT DATA", "YES - PERFORM THIS ACTION");
                break;
            case SourceRecordScreenKind.Leaderboard:
                title = new[] { "LOCAL GRAND CHAMPIONS", "LOCAL WIN PERCENTAGE", "LOCAL MOST WINS", "LOCAL OFFENSE",
                    "LOCAL DEFENSE", "LOCAL RUSHING YARDS", "LOCAL PASSING YARDS", "LOCAL CONSECUTIVE WINS",
                    "LOCAL GAMES PLAYED", "LOCAL QB RATING", "LOCAL WORLD RECORDS", "LOCAL TRIVIA", "POPULAR TEAMS" }[leaderboard];
                if (leaderboard == 10) rows.AddRange(WorldRows());
                else if (leaderboard == 12) rows.Add(new SourceRecordScreenRow("SOURCE TEAM-POPULARITY AUDITS UNAVAILABLE - NOT CAREER TOTALS"));
                else rows.AddRange(LeaderboardRows());
                details.Add(new SourceRecordScreenRow(leaderboard is 10 or 12 ?
                    "ACTUAL LOCAL VALUES; NO NATIVE FACTORY CMOS / AUDIT IMAGE" :
                    "ORIGINAL MINIMUM: 4 COMPLETED GAMES; LOCAL CABINET ONLY"));
                prompt = "LEFT/RIGHT CATEGORY   A/B RECORD PAGE";
                break;
            case SourceRecordScreenKind.WorldRecords:
                title = "LOCAL WORLD RECORDS";
                rows.AddRange(WorldRows());
                details.Add(new SourceRecordScreenRow("ACTUAL LOCAL MATCH BESTS - NO NATIVE FACTORY CMOS IMAGE"));
                break;
            case SourceRecordScreenKind.Final:
                title = finalState == FinalState.Pending ? "SAVING FINAL RECORDS" : finalState == FinalState.Failed ? "FINAL RECORD SAVE FAILED" : "FINAL MATCH RECORD";
                if (record != null) details.AddRange(CareerDetails(record));
                prompt = finalState == FinalState.Failed ? "A RELOAD / RETRY   B DECLINE WITH CONSENT   C RECOVERY" :
                    "A CAREER RECORD   B NEXT MATCH   C LOCAL STORAGE";
                break;
        }
        var stationViews = Joined().Select(id =>
        {
            Station value = stations[id];
            string display = value.State == SourceRecordSelectionState.Guest ? null : value.Identity?.Name ?? new string(value.Name);
            return new SourceRecordStationScreen(id, value.Step.ToString(), display,
                new string('*', value.Step == EntryStep.Pin ? value.Entered : 0).PadRight(4, '-'),
                value.Cursor, value.Entered, value.Yes, value.Message);
        }).ToArray();
        SourceCustomPlay preview = draft;
        if (screen == SourceRecordScreenKind.Formation && draft != null)
            preview = SourceCustomPlayCodec.CreateFromFormation(formation, draft.Name1, draft.Name2);
        if (screen == SourceRecordScreenKind.Audibles && cursor < 3 && Math.Abs(audibleDraft[0][cursor]) >= 37 && record != null)
            preview = record.CustomPlays[Math.Abs(audibleDraft[0][cursor]) - 37];
        return new SourceRecordScreenView(screen, title, message, prompt, store.Notification.Status.ToString(),
            RecordDiagnosticCode, busy, focus, rows, details, stationViews, record?.CustomPlays,
            selectedSlot, record == null ? 0 : SourcePlayerRecords.UsableSlots(record), preview,
            screen is SourceRecordScreenKind.Route or SourceRecordScreenKind.RouteAction ? receiver : -1,
            routeX, routeY, nameCursor, record?.Identity.Name, editAfterEntry);
    }

    private IEnumerable<SourceRecordScreenRow> CareerDetails(SourcePlayerRecord record)
    {
        SourceRecordCareer c = record.Career;
        string[] lines = careerPage switch
        {
            0 => new[] { "GAMES " + c.GamesPlayed + "  WINS " + c.GamesWon + "  LOSSES / TIES " + c.LossesIncludingTies,
                "WIN STREAK " + c.WinStreak + "  WIN PERCENT " + (c.GamesPlayed == 0 ? 0 : c.GamesWon * 1000 / c.GamesPlayed) / 10.0m,
                "POINTS SCORED " + c.PointsScored + "  ALLOWED " + c.PointsAllowed,
                "LOCAL RANK " + Rank(record, SourceRecordHighScoreCategory.GamesWon) + "  OFF " +
                    Rank(record, SourceRecordHighScoreCategory.PointsScored) + "  DEF " + Rank(record, SourceRecordHighScoreCategory.PointsAllowed),
                "DEFEATED " + SourceRecordHighScores.TeamsDefeated(c) + " / 31  CREATED PLAY SLOTS " + SourcePlayerRecords.UsableSlots(record) + " / 9" },
            1 => new[] { "PASS ATT / COMPLETE  " + c.PassAttempts + " / " + c.PassCompletions,
                "PASS YARDS " + c.PassingYards + "  PASS TD " + c.PassTouchdowns,
                "PASSES INTERCEPTED " + c.Intercepted + "  QB RATING " + SourceRecordHighScores.PassRating(c).ToString("F1", CultureInfo.InvariantCulture),
                "RUSH YARDS " + c.RushingYards },
            2 => new[] { "TACKLES " + c.Tackles + "  SACKS " + c.Sacks,
                "TRIVIA POINTS " + c.TriviaPoints, "PRESERVED WORLD-RECORD MASK " + c.WorldRecords,
                "SOURCE DISK BANK " + record.DiskBank + "  LOCAL PLAYS ARE IN THIS PORTABLE STORE" },
            _ => Enumerable.Range(0, 4).Select(row => string.Join("  ", Enumerable.Range(row * 8, Math.Min(8, 31 - row * 8))
                .Where(team => (c.TeamsDefeated & 1 << team) != 0).Select(team => data.Hud.TeamAbbreviations[team]))).ToArray()
        };
        if (careerPage == 3 && c.TeamsDefeated == 0) lines[0] = "NO TEAMS DEFEATED";
        return lines.Select(text => new SourceRecordScreenRow(text));
    }

    private IEnumerable<SourceRecordScreenRow> LeaderboardRows()
    {
        if (store.Current == null || !store.IsWritable) return new[] { new SourceRecordScreenRow(LoadMessage()) };
        var category = (SourceRecordHighScoreCategory)leaderboard;
        var eligible = store.Current.Records.Where(record => record.Career.GamesPlayed >= 4 &&
            (leaderboard != 0 || SourceRecordHighScores.TeamsDefeated(record.Career) == 31)).ToList();
        eligible.Sort((left, right) => SourceRecordHighScores.Compare(left, right, category));
        return eligible.Take(leaderboard == 0 ? 3 : 10).Select((record, index) => new SourceRecordScreenRow(
            (index + 1) + "  " + record.Identity.Name + "  " + LeaderboardValue(record.Career, category),
            privateText: true)).DefaultIfEmpty(new SourceRecordScreenRow(leaderboard == 0 ?
                "NO LOCAL RECORD HAS DEFEATED ALL 31 TEAMS" : "NO LOCAL RECORD HAS FOUR COMPLETED GAMES"));
    }

    private IEnumerable<SourceRecordScreenRow> WorldRows()
    {
        if (store.Current == null || !store.IsWritable) return new[] { new SourceRecordScreenRow(LoadMessage()) };
        string[] categories = { "MOST SACKS", "MOST TOUCHDOWNS", "MOST TACKLES", "MOST INTERCEPTIONS", "MOST POINTS", "LARGEST VICTORY MARGIN" };
        return Enumerable.Range(0, 6).Select(i =>
        {
            SourceWorldRecord world = store.Current.WorldRecords[i];
            return new SourceRecordScreenRow(categories[i] + "  " + (world == null ? "-- UNSET" :
                world.Value + "  " + world.Holder.Name), privateText: world != null);
        });
    }

    private string Rank(SourcePlayerRecord record, SourceRecordHighScoreCategory category)
    {
        if (record.Career.GamesPlayed < 4 || store.Current == null) return "--";
        int better = store.Current.Records.Count(other => SourceRecordHighScores.Compare(other, record, category) < 0);
        return (better + 1).ToString(CultureInfo.InvariantCulture);
    }

    private static string LeaderboardValue(SourceRecordCareer c, SourceRecordHighScoreCategory category) => category switch
    {
        SourceRecordHighScoreCategory.GrandChampions => "31 TEAMS DEFEATED",
        SourceRecordHighScoreCategory.WinPercentage => c.GamesWon == c.GamesPlayed ? "1.000" :
            "." + (c.GamesWon * 1000 / c.GamesPlayed).ToString("000", CultureInfo.InvariantCulture),
        SourceRecordHighScoreCategory.GamesWon => c.GamesWon + " WINS",
        SourceRecordHighScoreCategory.PointsScored => ((float)c.PointsScored / c.GamesPlayed).ToString("F2", CultureInfo.InvariantCulture) + " PTS / GAME",
        SourceRecordHighScoreCategory.PointsAllowed => ((float)c.PointsAllowed / c.GamesPlayed).ToString("F2", CultureInfo.InvariantCulture) + " PTS / GAME",
        SourceRecordHighScoreCategory.RushingYards => ((float)c.RushingYards / c.GamesPlayed).ToString("F2", CultureInfo.InvariantCulture) + " YDS / GAME",
        SourceRecordHighScoreCategory.PassingYards => ((float)c.PassingYards / c.GamesPlayed).ToString("F2", CultureInfo.InvariantCulture) + " YDS / GAME",
        SourceRecordHighScoreCategory.WinStreak => c.WinStreak + " CONSECUTIVE WINS",
        SourceRecordHighScoreCategory.GamesPlayed => c.GamesPlayed + " GAMES",
        SourceRecordHighScoreCategory.QuarterbackRating => SourceRecordHighScores.PassRating(c).ToString("F1", CultureInfo.InvariantCulture),
        _ => c.TriviaPoints + " TRIVIA POINTS"
    };

    private string AudibleName(int side, short id)
    {
        if (id == 0) return "ORIGINAL DEFAULT ROW";
        int value = Math.Abs(id) - 1;
        if (side == 0 && value >= 36)
        {
            SourceCustomPlay play = SelectedRecord(focus)?.CustomPlays[value - 36];
            return play == null ? "UNAVAILABLE CUSTOM" : (play.Name1 + " " + play.Name2).ToUpperInvariant();
        }
        return data.Hud.PageSlot(side == 0 ? SourcePlaybookKind.Offense : SourcePlaybookKind.Defense,
            value / 9, value % 9, 0).Name;
    }

    private static string Primary(SourceCustomPlay play) =>
        (play.Flags & SourceCustomPlayFlags.PrimaryReceiver1) != 0 ? "R1" :
        (play.Flags & SourceCustomPlayFlags.PrimaryReceiver2) != 0 ? "R2" :
        (play.Flags & SourceCustomPlayFlags.PrimaryReceiver3) != 0 ? "R3" : "NONE";

    public object Describe() => new
    {
        policy = "source-local-cabinet-records/v1", active, screen = active ? screen.ToString() : "Inactive",
        busy, storeStatus = store.Notification.Status.ToString(), Code = RecordDiagnosticCode,
        store.Notification.Generation, store.Notification.RecordCount, writable = store.IsWritable,
        canImport = CanImport, canExport = CanExport, canStartMatch = CanStartMatch,
        finalCommit = finalState.ToString(), selectedSlot, receiver, selectionComplete = SelectionComplete,
        sourceMenuBound, menuSourceTick, sourceStorageBusy = SourceStorageBusy,
        stations = Joined().Select(id => new { stationId = id, step = stations[id].Step.ToString(),
            selection = stations[id].State.ToString(), enteredPositions = stations[id].Entered }).ToArray()
    };
}
