using System;
using System.Collections.Generic;
using System.Linq;
using Blitz.Contracts;
using Blitz.Gameplay;

internal static partial class Program
{
    private sealed class PreGameDriver : ISourcePreGameMenuDriver
    {
        internal SourcePreGameMenuPhase Phase = SourcePreGameMenuPhase.Joining;
        internal int Mask = 15, Focus = 0, OptionIndex = 1;
        internal bool Busy = false, Yes;
        internal readonly SourcePreGameMenuScreen[] Screens = new SourcePreGameMenuScreen[4];
        internal Action<SourcePreGameMenuWake> OnWake;
        internal readonly List<SourcePreGameMenuWake> Wakes = new List<SourcePreGameMenuWake>();
        public SourcePreGameMenuRoute ReadRoute()
        {
            return new SourcePreGameMenuRoute(Phase, Mask,
                Phase == SourcePreGameMenuPhase.Joining || Phase == SourcePreGameMenuPhase.PortableManagement ? -1 : Focus,
                Screens, Busy, new[] { Yes, Yes, Yes, Yes }, OptionIndex);
        }
        public void OnSourceMenuWake(SourcePreGameMenuWake wake)
        {
            Wakes.Add(wake);
            if (OnWake != null) OnWake(wake);
        }
        internal void Screen(SourcePreGameMenuScreen screen)
        { for (int i = 0; i < 4; i++) Screens[i] = screen; }
    }

    private sealed class PreGameRig
    {
        internal readonly PreGameDriver Driver = new PreGameDriver();
        internal readonly SourceInjectedRandom Random;
        internal readonly SourcePreGameSoundService Service;
        internal readonly List<SourceProcessSoundCall> Calls = new List<SourceProcessSoundCall>();
        internal readonly List<SourceProcessSoundControl> Controls = new List<SourceProcessSoundControl>();
        internal readonly List<SourcePreGameSoundActivation> Activations = new List<SourcePreGameSoundActivation>();
        internal readonly SourceButtons[] Buttons = new SourceButtons[4];
        internal readonly SourceDigitalDirections[] Directions = new SourceDigitalDirections[4];
        internal PreGameRig()
        {
            Random = new SourceInjectedRandom(site => 1, new SourceRandomProvenance("pregame/process-test",
                "Explicit source raw tape, NOT target PRNG", "constant1, no seed search", SourceRandomEvidence.ExternalUnverified));
            Service = new SourcePreGameSoundService(SourcePreGameSoundEntry.FromOriginalPermanentBanksAfterAcceptedStart(),
                Random, Driver);
            Capture();
        }
        internal void Capture()
        {
            var input = new SourceStationInput[4];
            for (int i = 0; i < 4; i++)
                input[i] = new SourceStationInput(i, held: Buttons[i], digitalCurrent: Directions[i]);
            Service.CapturePhysical(new SourcePhysicalSwitchFrame(input));
        }
        internal void Drain()
        {
            SourcePreGameSoundActivation activation;
            if (Service.TryTakeEntryActivation(out activation)) Activations.Add(activation);
            Calls.AddRange(Service.TakeSourceProcessSoundCalls());
            Controls.AddRange(Service.TakeSourceSoundControls());
        }
        internal void Step()
        {
            long tick = Service.SourceTick;
            for (int host = 0; host < 3 && Service.SourceTick == tick; host++) Service.AdvanceHostTick();
            Check(Service.SourceTick == tick + 1, "pregame fixture advances the actual57Hz source clock, not a render event");
            Drain();
        }
        internal void Steps(int count) { for (int i = 0; i < count; i++) Step(); }
        internal void Until(Func<bool> done, int budget = 180)
        {
            for (int i = 0; i < budget && !done(); i++) Step();
            Check(done(), "real pregame continuation reaches its bounded source boundary");
        }
        internal void Press(int station, SourceButtons buttons)
        {
            Buttons[station] = 0; Capture(); Steps(2);
            Buttons[station] = buttons; Capture(); Step();
            Buttons[station] = 0; Capture(); Steps(2);
        }
        internal void Move(int station, SourceDigitalDirections direction)
        {
            Directions[station] = 0; Capture(); Steps(2);
            Directions[station] = direction; Capture(); Step();
            Directions[station] = 0; Capture(); Steps(2);
        }
        internal void Options()
        {
            Driver.Phase = SourcePreGameMenuPhase.Options; Driver.Screen(SourcePreGameMenuScreen.Options);
            Until(() => Calls.Any(call => call.PlaylistId == 9));
            Check(Activations.Count == 1 && Activations[0].SourceTick < Calls[0].SourceTick &&
                Calls[0].Process == "pre_game_scrns" && Service.Processes.Any(process => process.Id == 0x0c00),
                "accepted route activates sourceentry once, then actual pre_game PID wakes before intro9");
            Until(() => Driver.Wakes.Any(wake => wake.Process.HasValue
                && wake.Process.Value.Function == "plyr_select_option_proc" && wake.ButtonsEnabled));
        }
        internal void Names()
        {
            Options();
            Driver.OnWake = wake => {
                if (wake.Route.Phase == SourcePreGameMenuPhase.Options && wake.ClosedButtons(0) == SourceButtons.A)
                { Driver.Phase = SourcePreGameMenuPhase.PlayerEntry; Driver.Screen(SourcePreGameMenuScreen.YesNo); }
            };
            Press(0, SourceButtons.A);
            Until(() => Driver.Wakes.Any(wake => wake.Process.HasValue && wake.Process.Value.Function == "player_cursor"));
            Driver.OnWake = null;
        }
    }

    private static void PreGameProcesses()
    {
        var rig = new PreGameRig();
        int joined = 0;
        rig.Driver.OnWake = wake => {
            if (wake.ClosedButtons(3) != 0) joined++;
            Check(!wake.IsOriginalProcess && wake.StationId == -1,
                "joining is a once-per-source nonnative callback, never a fake positive game/source process");
        };
        rig.Buttons[3] = SourceButtons.C; rig.Capture();
        rig.Buttons[3] = 0; rig.Capture();
        for (int i = 0; i < 60; i++) rig.Service.AdvanceHostTick();
        rig.Drain();
        Check(rig.Service.SourceTick == 57 && joined == 1 && rig.Calls.Count == 0 && rig.Controls.Count == 0 &&
            rig.Activations.Count == 0 && rig.Service.Processes.Count == 0 && rig.Random.Trace.Count == 0,
            "actual60:57 FIFO preserves a physical press/release and has NO unjoined sound/banks/nativeprocess/RNG claim");
        long pausedTick = rig.Service.SourceTick;
        rig.Service.SetActive(false);
        for (int i = 0; i < 10; i++) rig.Service.AdvanceHostTick();
        Check(rig.Service.SourceTick == pausedTick, "portablepause freezes genuine pregame source clock without restarting music");
        rig.Service.SetActive(true); rig.Driver.OnWake = null;
        rig.Options();
        rig.Until(() => rig.Calls.Any(call => call.Process == "welcome_proc"));
        SourceProcessSoundCall welcome = rig.Calls.Single(call => call.Process == "welcome_proc");
        SourceProcessView welcomeBirth = rig.Driver.Wakes.First(wake => wake.Process.HasValue
            && wake.Process.Value.Function == "plyr_select_option_proc").Process.Value;
        Check(welcome.PlaylistId == 6 && welcome.Volume == 235 && welcome.Pan == 127 && welcome.PriorityWord == 0x8280 &&
            rig.Random.Trace.Count == 1 && rig.Random.Trace[0].Site.BoundOrModulus == 3 &&
            rig.Random.Trace[0].Site.Process == "welcome_proc" &&
            welcome.SourceTick - welcomeBirth.BirthTick == 51,
            "actual qcreate welcomePID0 sleeps50 after first child wake, draws exactlybound3 then intro11/6/7 ontrack5");
        SourcePreGameMenuWake retained = rig.Driver.Wakes.Last();
        int outputs = rig.Calls.Count;
        Reject(() => retained.Emit(SourcePreGameMenuSound.OptionCursor), "current actual source process");
        Check(rig.Service.TakeSourceProcessSoundCalls().Count == 0 && rig.Calls.Count == outputs,
            "retained/asynchronous menu event cannot allocate an output ordinal or select a sound");

        var names = new PreGameRig(); names.Names();
        SourceProcessSoundCall finish = names.Calls.Single(call => call.PlaylistId == 20);
        SourcePreGameMenuWake firstCursor = names.Driver.Wakes.First(wake => wake.Process.HasValue
            && wake.Process.Value.Function == "player_cursor");
        Check(finish.Process == "plyr_select_option_proc" && finish.Volume == 255 && finish.PriorityWord == 1 &&
            firstCursor.Scan.SourceTick - finish.SourceTick == 51,
            "actual option finish20/28-flash+20sleep precedes next-wake player_cursor creation+two-tick entrance");
        int[] cursorIds = names.Driver.Wakes.Where(wake => wake.Scan.SourceTick == firstCursor.Scan.SourceTick &&
            wake.Process.HasValue && wake.Process.Value.Function == "player_cursor").Select(wake => wake.Process.Value.Id).ToArray();
        Check(cursorIds.SequenceEqual(new[] { 206, 204, 202, 200 }),
            "source qcreate-head order is genuineP4/P3/P2/P1, not later sorted UI events");
        long selected = 0;
        names.Driver.Yes = true;
        names.Driver.OnWake = wake => {
            if (wake.StationId == 0 && wake.Screen == SourcePreGameMenuScreen.YesNo && wake.ClosedButtons(0) != 0)
            {
                wake.Emit(SourcePreGameMenuSound.YesNoSelect);
                selected = wake.Scan.SourceTick; names.Driver.Screens[0] = SourcePreGameMenuScreen.Name;
            }
        };
        names.Press(0, SourceButtons.A);
        Check(names.Calls.Last().PlaylistId == 28 && names.Calls.Last().Operand.EndsWith(":4042", StringComparison.Ordinal),
            "real YES confirmation uses28 atflash_yes_no4042, never generic27");
        names.Until(() => names.Driver.Wakes.Any(wake => wake.StationId == 0 &&
            wake.Screen == SourcePreGameMenuScreen.Name && wake.Scan.SourceTick >= selected + 33));
        Check(names.Driver.Wakes.Where(wake => wake.StationId == 0 && wake.Scan.SourceTick > selected)
            .First().Scan.SourceTick == selected + 33,
            "the actual normal YESNO source process is blocked for11*sleep3, but physical scans/otherstationprocesses continue");
        names.Driver.OnWake = wake => {
            if (wake.StationId != 0) return;
            if (wake.Screen == SourcePreGameMenuScreen.Name && wake.ClosedButtons(0) != 0)
            { wake.Emit(SourcePreGameMenuSound.NameSelect); selected = wake.Scan.SourceTick;
                names.Driver.Screens[0] = SourcePreGameMenuScreen.RecordLookup; }
            else if (wake.Screen == SourcePreGameMenuScreen.RecordLookup)
            { wake.Emit(SourcePreGameMenuSound.RecordFound); names.Driver.Screens[0] = SourcePreGameMenuScreen.Ready; }
        };
        names.Press(0, SourceButtons.A);
        names.Until(() => names.Calls.Any(call => call.PlaylistId == 80));
        SourceProcessSoundCall found = names.Calls.Single(call => call.PlaylistId == 80);
        Check(found.SourceTick == selected + 9 && found.Process == "player_cursor" &&
            found.Operand.EndsWith(":2932", StringComparison.Ordinal) && found.Volume == 205 && found.PriorityWord == 5,
            "lastletter completion is source8flash+1loop before actual record lookup/found80; no async observer stamp");

        names.Driver.OnWake = null; names.Driver.Phase = SourcePreGameMenuPhase.Teams;
        names.Step(); names.Driver.Phase = SourcePreGameMenuPhase.Rosters; names.Step();
        names.Driver.Phase = SourcePreGameMenuPhase.Versus; names.Step();
        names.Driver.OnWake = wake => {
            if (wake.Route.Phase == SourcePreGameMenuPhase.Versus && wake.ClosedButtons(0) != 0)
                names.Driver.Phase = SourcePreGameMenuPhase.MatchRequested;
        };
        names.Press(0, SourceButtons.A);
        names.Until(() => names.Service.ReadyForGame);
        SourcePreGameSoundHandoff handoff;
        Check(names.Service.TryTakeGameHandoff(out handoff) && handoff.CurrentCursorBank == "cursors" &&
            handoff.LoadedBanks.SequenceEqual(new[] { "players", "generic", "tkperm", "cursors", "intro" }) &&
            handoff.ReservedTracks == 35 && handoff.LastOutputSequence == names.Calls.Count + names.Controls.Count,
            "actual versus1-wake boundary hands known bank state and canonical terminal ordinal to a new declared game epoch");
        Reject(() => names.Service.CapturePhysical(SourcePhysicalSwitchFrame.Neutral()), "handed ownership");
        Func<int, ISourceRandom> factory = handoff.CreateGameRandomFactory();
        ISourceRandom first = factory(1);
        SourceRandomDraw draw = first.Draw(new SourceRandomSite(SourceRandomKind.RandRange, 5, 0, -1,
            "vs_screen", 0, 0, first.Provenance.Profile, "lib/select/vsscreen.c:vs_screen:1159"));
        Check(draw.Order == 1 && names.Random.Trace.Count == 2 &&
            names.Random.Trace.Last().Site.SourceTick > names.Service.SourceTick,
            "VSuses SAME underlyingstream afterwelcome, maps explicit new local0 domain without draw/reseed/previewGame");
        Reject(() => handoff.CreateGameRandomFactory(), "already been claimed");

        PreGameCapProcesses();
        PreGameModalTransition();
        PreGameScopeFailures();
    }

    private static void PreGameModalTransition()
    {
        var rig = new PreGameRig(); rig.Names();
        long[] children = rig.Service.Processes.Where(process => process.Function == "player_cursor")
            .Select(process => process.Serial).ToArray();
        rig.Driver.OnWake = wake => {
            if (wake.StationId == 0 && wake.ClosedButtons(0) != 0)
                rig.Driver.Phase = SourcePreGameMenuPhase.PortableManagement;
        };
        rig.Press(0, SourceButtons.A);
        Check(rig.Service.ActivePhase == SourcePreGameMenuPhase.PlayerEntry && children.All(serial =>
            rig.Service.Processes.Any(process => process.Serial == serial)),
            "a modal opened by an earlier same-tick child retains native player cursor lifetimes when the parent later wakes");
        int calls = rig.Calls.Count;
        rig.Driver.OnWake = null; rig.Steps(3);
        Check(rig.Calls.Count == calls, "portable modal neither generates native calls nor resumes native siblings");
        rig.Driver.OnWake = wake => {
            if (!wake.IsOriginalProcess && wake.ClosedButtons(0) != 0)
            {
                rig.Driver.Phase = SourcePreGameMenuPhase.CreatePlay;
                rig.Driver.Screens[0] = SourcePreGameMenuScreen.CapSlots;
            }
        };
        rig.Press(0, SourceButtons.A);
        rig.Until(() => rig.Service.Processes.Any(process => process.Function == "create_play_proc"));
        Check(rig.Service.ActivePhase == SourcePreGameMenuPhase.CreatePlay &&
            !rig.Service.Processes.Any(process => process.Function == "player_cursor"),
            "actual native parent resumes from modal into create_process CAP instead of becoming a dead portable caller");
    }

    private static void PreGameCapProcesses()
    {
        var rig = new PreGameRig();
        rig.Driver.Mask = 1; rig.Driver.Focus = 0; rig.Driver.OptionIndex = 0;
        rig.Options();
        rig.Driver.OnWake = wake => {
            if (wake.Route.Phase == SourcePreGameMenuPhase.Options && wake.ClosedButtons(0) != 0)
            {
                wake.Emit(SourcePreGameMenuSound.OptionSelect);
                rig.Driver.Phase = SourcePreGameMenuPhase.CreatePlay;
                rig.Driver.Screens[0] = SourcePreGameMenuScreen.CapName;
            }
        };
        rig.Press(0, SourceButtons.A);
        rig.Until(() => rig.Driver.Wakes.Any(wake => wake.Process.HasValue
            && wake.Process.Value.Function == "create_play_proc"));
        SourceProcessSoundCall capTune = rig.Calls.Single(call => call.Bank == "intro" && call.PlaylistId == 10);
        SourceProcessSoundCall capName = rig.Calls.Single(call => call.Bank == "intro" && call.PlaylistId == 12);
        Check(capTune.Process == "create_play_proc" && capTune.Volume == 235 && capTune.PriorityWord == 0x8000 &&
            capName.SourceTick == capTune.SourceTick + 10 && capName.Volume == 205 && capName.PriorityWord == 0x8280 &&
            rig.Service.Processes.Any(process => process.Id == 0x1000 && process.Function == "create_play_exit_proc"),
            "actual CAP wipeout/transition/create/exiter processes own original intro10/12 and track-stop/reserve lifecycle");
        long cursorSerial = rig.Service.Processes.Single(process => process.Function == "create_play_proc").Serial;
        rig.Driver.OnWake = wake => {
            if (wake.Screen == SourcePreGameMenuScreen.CapName && wake.ClosedDirections(0) != 0)
                wake.Emit(SourcePreGameMenuSound.CapLetterCursor);
            if (wake.Screen == SourcePreGameMenuScreen.CapName && wake.ClosedButtons(0) != 0)
            {
                wake.Emit(SourcePreGameMenuSound.CapLetterSelect);
                rig.Driver.Screens[0] = SourcePreGameMenuScreen.RecordLookup;
            }
            else if (wake.Screen == SourcePreGameMenuScreen.RecordLookup)
            {
                wake.Emit(SourcePreGameMenuSound.RecordFound); rig.Driver.Screens[0] = SourcePreGameMenuScreen.CapActions;
            }
        };
        rig.Move(0, SourceDigitalDirections.Right);
        Check(rig.Calls.Last().PlaylistId == 85 && rig.Calls.Last().Operand == "cap.c:get_name:1506" &&
            rig.Calls.Last().ProcessSerial == cursorSerial, "CAP name cursor uses original85/get_name, not normal87/player_cursor");
        rig.Press(0, SourceButtons.A);
        rig.Driver.Busy = true; rig.Steps(12);
        Check(!rig.Calls.Any(call => call.PlaylistId == 80), "async data/storage pending cannot emit a record-found observer event");
        rig.Driver.Busy = false;
        rig.Until(() => rig.Calls.Any(call => call.PlaylistId == 80));
        Check(rig.Driver.Wakes.Last().IsStorageResume && rig.Calls.Last().Operand == "cap.c:show_record_status_msg:1552" &&
            rig.Calls.Last().Process == "create_play_proc",
            "actual completed storage resumes the SAME native CAP process before source1552 found80");

        long selectTick = -1;
        rig.Driver.OnWake = wake => {
            if (wake.Screen == SourcePreGameMenuScreen.CapActions && wake.ClosedButtons(0) != 0)
            {
                wake.Emit(SourcePreGameMenuSound.CapSelect); selectTick = wake.Scan.SourceTick;
                rig.Driver.Screens[0] = SourcePreGameMenuScreen.CapFormation;
            }
            else if (wake.Screen == SourcePreGameMenuScreen.CapFormation && wake.ClosedDirections(0) != 0)
                wake.Emit(SourcePreGameMenuSound.FormationCursor);
        };
        rig.Press(0, SourceButtons.A);
        rig.Until(() => rig.Driver.Wakes.Any(wake => wake.Screen == SourcePreGameMenuScreen.CapFormation));
        Check(rig.Driver.Wakes.First(wake => wake.Screen == SourcePreGameMenuScreen.CapFormation).Scan.SourceTick == selectTick + 21,
            "actual flash_lcb's1+10*2 blocks CAP caller for21 source ticks, not UI renders");
        rig.Move(0, SourceDigitalDirections.Down);
        Check(rig.Calls.Last().PlaylistId == 87 && rig.Calls.Last().Operand == "cap.c:lcb_formation:2595",
            "accepted actual formation movement emits original87 at correct2595 caller");
        long[] ordinals = rig.Calls.Select(call => call.OutputSequence).Concat(rig.Controls.Select(call => call.OutputSequence)).OrderBy(value => value).ToArray();
        Check(ordinals.SequenceEqual(Enumerable.Range(1, ordinals.Length).Select(i => (long)i)),
            "actual CAP entry controls and accepted menu calls share one canonical owner without gaps/second stamps");
        rig.Driver.OnWake = null; rig.Driver.Phase = SourcePreGameMenuPhase.PortableManagement;
        rig.Step(); rig.Step();
        Check(rig.Service.Processes.Any(process => process.Function == "create_play_proc"),
            "portable management modal retains and suspends the actual CAP continuation without inventing a native exit");
        rig.Driver.Phase = SourcePreGameMenuPhase.Joining;
        rig.Step(); rig.Step();
        Check(rig.Controls.Last().FunctionSymbol == "create_play_exit_proc" &&
            rig.Controls.Last().SourceLine == 857 && !rig.Service.Processes.Any(process => process.Id == 0x1000),
            "real CAP exit process stops the CAP tune/reserves once before nonnative local management; no finished game_proc reuse");
    }

    private static void PreGameScopeFailures()
    {
        var rig = new PreGameRig(); rig.Options();
        rig.Driver.OnWake = wake => {
            if (wake.Route.Phase == SourcePreGameMenuPhase.Options && wake.ClosedButtons(0) != 0)
                wake.Emit(SourcePreGameMenuSound.OptionSelect);
        };
        Reject(() => rig.Press(0, SourceButtons.A), "only to CREATE");
        Check(rig.Service.Failure != null, "wrong ENTER NAME generic-select caller faults rather than acquiring native CAP identity");

        var portable = new PreGameRig();
        portable.Driver.Phase = SourcePreGameMenuPhase.PortableManagement;
        portable.Driver.Screen(SourcePreGameMenuScreen.Portable);
        portable.Driver.OnWake = wake => wake.Emit(SourcePreGameMenuSound.CapCursor);
        Reject(portable.Step, "current actual source process");
        Check(portable.Calls.Count == 0 && portable.Random.Trace.Count == 0,
            "portable recovery/career management cannot fabricate a native original audio scope or random selection");
    }

    private static void PreGameActualGameHandoff(string root)
    {
        var rig = new PreGameRig(); rig.Options();
        rig.Until(() => rig.Calls.Any(call => call.Process == "welcome_proc"));
        rig.Driver.OnWake = wake => {
            if (wake.Route.Phase == SourcePreGameMenuPhase.Options && wake.ClosedButtons(0) != 0)
                rig.Driver.Phase = SourcePreGameMenuPhase.Teams;
        };
        rig.Press(0, SourceButtons.A);
        rig.Until(() => rig.Service.ActivePhase == SourcePreGameMenuPhase.Teams);
        rig.Driver.OnWake = null; rig.Driver.Phase = SourcePreGameMenuPhase.Rosters; rig.Step();
        rig.Driver.Phase = SourcePreGameMenuPhase.Versus; rig.Step();
        rig.Driver.OnWake = wake => {
            if (wake.Route.Phase == SourcePreGameMenuPhase.Versus && wake.ClosedButtons(0) != 0)
                rig.Driver.Phase = SourcePreGameMenuPhase.MatchRequested;
        };
        rig.Press(0, SourceButtons.A); rig.Until(() => rig.Service.ReadyForGame);
        SourcePreGameSoundHandoff handoff;
        Check(rig.Service.TryTakeGameHandoff(out handoff), "actual source VS continuation produces the exclusive game handoff");
        Func<int, ISourceRandom> factory = handoff.CreateGameRandomFactory();
        var inputs = new OrderingInputs(root);
        GameSimulation game = inputs.Create(factory);
        handoff.ObserveGameClock(game);
        var gameRig = new OrderingRig(game);
        for (int i = 0; i < 40; i++) { gameRig.Step(); handoff.ObserveGameClock(game); }
        long finishedClock = game.SourceTick;
        int previous = rig.Random.Trace.Count;
        GameSimulation next = inputs.Create(factory);
        handoff.ObserveGameClock(next);
        Check(rig.Random.Trace[previous].Order == previous &&
            rig.Random.Trace[previous].Site.SourceTick > rig.Service.SourceTick + finishedClock &&
            next.SourceTick == 0 && game.SourceTick == finishedClock,
            "fresh actual Game constructor preserves SAME random stream and advances after the full previous source clock, not only its last draw");
        Check(handoff.BankState.CurrentCursorBank == next.SourceCursorSoundBank &&
            handoff.BankState.PreviousSourceTick == next.SourceTick,
            "VS bank token reads the verified actual Game source cursor/global, not an InitialBanks guess");
        var nextRig = new OrderingRig(next);
        nextRig.Step(); handoff.ObserveGameClock(next);
        int beforeReset = rig.Random.Trace.Count;
        handoff.PrepareGameReset(next);
        next.Restart(9876);
        handoff.ObserveGameClock(next);
        Check(rig.Random.Trace.Count > beforeReset && rig.Random.Trace[beforeReset].Order == beforeReset &&
            rig.Random.Trace[beforeReset].Site.SourceTick > rig.Random.Trace[beforeReset - 1].Site.SourceTick,
            "explicit pre-reset source clock capture preserves native-stream order even when legacy reset supplies a different seed");
        Reject(() => handoff.ObserveGameClock(new GameSimulation()), "exact handed-off random domain");
    }
}
