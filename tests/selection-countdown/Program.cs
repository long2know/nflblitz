using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Blitz.AssetImport;
using Blitz.Contracts;
using Blitz.Gameplay;
using Blitz.Presentation;
using Blitz.Runtime;

internal static partial class SelectionCountdownChecks
{
    private static int assertions;
    private static readonly List<string> cases = new List<string>();
    private static FullKernelInputs inputs;
    private static SourceInterfaceData hud;
    private static SourceCinematicLibrary impact;
    private static SourceNormalCameraData normal;
    private static SourcePossessionSoundData speech;
    private static SourceFireSpfxBundle fire;
    private static bool scopeAdmitted, scopePassed;
    private static string[] requestedExecutionCases = new string[0];
    private static readonly List<string> startedExecutionCases = new List<string>();
    private static readonly List<string> completedExecutionCases = new List<string>();
    private static readonly string[] timerCaseNames = {
        "quarter0-full", "later-quarter-full", "instruction-threshold-8", "instruction-threshold-9",
        "instruction-threshold-10", "instruction-only-before-number-sleep", "instruction-at-zero",
        "real-suspended-timer-only", "real-suspended-timer-and-child", "caller-teardown-at-1",
        "caller-teardown-at-2", "caller-teardown-at-20", "caller-teardown-at-58", "caller-teardown-at-515",
        "caller-teardown-at-595", "caller-teardown-at-596", "caller-teardown-at-597"
    };
    private static readonly string[] callerCaseNames = {
        "actual-timeout-zero-grace-resumption", "actual-timeout-zero-grace-resumption-FX",
        "actual-early-side-and-highlight", "actual-early-exit-reset-new-menu",
        "actual-instruction-input-not-tutorial-ui", "actual-timeout-then-CPU-order",
        "actual-no-human-instant-caller-order", "actual-active-menu-restart",
        "actual-timeout-dependency-failure", "actual-missing-timeout-consumer", "actual-timer-output-fail-closed"
    };
    private static readonly Dictionary<string, string> controlScopes = new Dictionary<string, string> {
        { "host60-clock", "timer-traces" },
        { "zero-grace24", "timer-traces" },
        { "first-quarter8", "timer-traces" },
        { "later-quarter6", "timer-traces" },
        { "caller-quarter-off-by-one", "actual-timeout-zero-grace-resumption" },
        { "instruction-strictly-greater9", "timer-traces" },
        { "ignored-instruction-hold", "timer-traces" },
        { "initial-two-tick-delay", "timer-traces" },
        { "flasher-sleep6", "timer-traces" },
        { "suspended-timer-still-runs", "timer-traces" },
        { "premature-current-wake-choice", "actual-timeout-zero-grace-resumption" },
        { "force-page0-not-highlight", "actual-early-side-and-highlight" },
        { "force-overwrites-selected", "actual-early-side-and-highlight" },
        { "Core-overwrites-selected", "actual-early-side-and-highlight" },
        { "duplicate-forced-choice", "actual-timeout-zero-grace-resumption" },
        { "caller-does-not-finish", "actual-timeout-zero-grace-resumption" },
        { "exit-leaves-timer-alive", "actual-timeout-zero-grace-resumption" },
        { "muted-timer-beep", "actual-timeout-zero-grace-resumption" },
        { "wrong-sound-volume", "timer-traces" },
        { "sound-failure-swallowed", "actual-timer-output-fail-closed" },
        { "HUD-static-white-bast10", "actual-timeout-zero-grace-resumption" },
        { "random-start-omitted", "random-offense-raw0" },
        { "random-start-slot4", "random-offense-raw0" },
        { "random-wrong-captain", "random-both-four-captain1" },
        { "random-direction-bound3", "random-offense-raw0" },
        { "random-extra-start-draw", "random-offense-raw0" },
        { "random-final-button-draw-omitted", "random-offense-raw0" },
        { "random-extra-sleep1", "random-offense-raw0" },
        { "random-flip-cap3", "random-offense-raw1" },
        { "random-page-cap5", "random-offense-raw0" },
        { "random-one-iteration-short", "random-offense-raw0" },
        { "random-defense-zero-pages", "random-defense-raw1" },
        { "random-shared-side-order", "random-both-two-station" },
        { "random-physical-direction", "random-offense-raw0" },
        { "random-visual-only-flip", "random-from-page3-preflipped" },
        { "random-muted-selection", "random-offense-raw0" },
        { "random-premature-parent", "random-timeout-start577" },
        { "random-force-parent-omitted", "random-timeout-start577" },
        { "random-input-scan-as-consumer", "random-suspended-real-consumer" },
        { "random-draw-from-skipped-consumer", "random-suspended-real-consumer" },
        { "random-restart-retains-continuation", "random-restart" },
        { "random-output-failure-swallowed", "random-output-failure" }
    };

    private static void BeginCase(string name)
    {
        if (startedExecutionCases.Count >= requestedExecutionCases.Length ||
            requestedExecutionCases[startedExecutionCases.Count] != name)
            throw new InvalidOperationException("Wrong/substituted executed case for the admitted control scope.");
        startedExecutionCases.Add(name);
    }

    private static void EndCase(string name)
    {
        if (startedExecutionCases.Count != completedExecutionCases.Count + 1 ||
            startedExecutionCases[startedExecutionCases.Count - 1] != name)
            throw new InvalidOperationException("Missing/substituted executed case completion.");
        completedExecutionCases.Add(name);
    }
    private static void Check(bool value, string name)
    { assertions++; if (!value) throw new InvalidOperationException(name); }

    private sealed class Cabinet
    {
        internal readonly GameSimulation Game;
        internal readonly SourceCabinetPlaySelection Selector;
        internal readonly SourceCabinetInputBuffer Input;
        internal readonly FixedStepBridge Bridge;
        internal readonly SourceCinematicPlayback Camera;
        internal readonly List<SourceCabinetMenuEvent> Events = new List<SourceCabinetMenuEvent>();
        internal readonly List<object> Trace = new List<object>();
        internal readonly List<SourceSelectionTimerSound> Sounds = new List<SourceSelectionTimerSound>();
        internal readonly List<SourceGameEvent> Selections = new List<SourceGameEvent>();
        private readonly SourceCabinetQuantizer quantizer;
        private readonly SourceMatchConfiguration configuration;
        private readonly SourceHudComposer composer;
        internal IReadOnlyList<SourceHudPrimitive> Draws;
        private long observed;

        internal Cabinet(bool cpuAway = false, Func<SourcePlaySelection, bool, SourceCommand> selectionCommand = null,
            bool fireEnabled = false, bool cpuBoth = false, SourceMatchConfiguration configurationOverride = null,
            Func<int, ISourceRandom> randomFactory = null)
        {
            configuration = configurationOverride ?? new SourceMatchConfiguration(SourceSimulationProfile.OriginalRelease57Hz,
                SourceStationLayout.TwoStation,
                new SourceTeamSelection(4, Enumerable.Range(0, 7).ToArray()),
                new SourceTeamSelection(8, Enumerable.Range(0, 7).ToArray()),
                cpuBoth ? new SourceStationConfiguration[0] :
                    cpuAway ? new[] { new SourceStationConfiguration(0, TeamId.Home, 3) } :
                    new[] { new SourceStationConfiguration(0, TeamId.Home, 3),
                        new SourceStationConfiguration(1, TeamId.Away, 10) },
                cpuBoth ? TeamId.Home : TeamId.Away, SourceCaptainSlot.First);
            Game = inputs.Create(configuration: configuration,
                presentation: new SourcePresentationSettings(TeamId.Home, new[] { -1, -1, -1, -1 }, 0),
                compatibility: SourceGameplayCompatibility.Portable, camera: false, randomFactory: randomFactory,
                fireEffects: fireEnabled ? new SourceFireGameplayConfiguration(new SourceFireSpfxRuntime(fire), 0, SourceFireCull.Off) : null);
            Game.BindSourcePossessionSoundData(speech);
            Game.SourceSelectionTimerSoundRaised += Sounds.Add;
            Input = new SourceCabinetInputBuffer(configuration);
            quantizer = new SourceCabinetQuantizer(hud.Input);
            Selector = new SourceCabinetPlaySelection(hud, selectionCommand);
            Selector.EventRaised += Events.Add;
            Game.SourceStationInputTransform = (state, tick, frame) =>
            {
                Check(tick == ++observed, "cabinet observes every consecutive SOURCE dispatch");
                return Selector.ApplyAtSourceTick(state, tick, frame);
            };
            Camera = new SourceCinematicPlayback(impact, normal, ((ISourceCameraState)Game.State).CameraBoundary);
            Game.SourceCameraHeadingProvider = (state, tick) =>
            {
                Camera.ObserveCameraProcessSuspension(false);
                Camera.CameraSourceTick(((ISourceCameraState)state).CameraBoundary, 512, 384,
                    Game.DrawSourceCameraRandom, Game.SourceCameraProcesses);
                Game.ApplySourceCameraFeedback(Camera.CameraFeedback);
                if (Camera.CameraFeedback.CameraCompleted)
                {
                    SourceCinematicTransform output = Camera.RequireSourceCameraAtTick(tick);
                    Game.SetSourceMovementCameraFrame(new SourceMovementCameraFrame(output.Position,
                        output.Transform, 512, 384), tick);
                }
                else Camera.ObserveCameraProcessSuspension(true);
                return Camera.SourceHeading1024;
            };
            composer = new SourceHudComposer(hud);
            Bridge = new FixedStepBridge(Game);
            Bridge.SourceEventRaised += value => { if (value.Type == SourceEventType.PlaySelected) Selections.Add(value); };
            Bridge.AfterHostStep += state =>
            {
                Camera.ObserveCameraProcessSuspension(((ISourcePossessionState)state).Processes
                    .Single(p => p.Function == "cambot_proc").Suspended);
                if (!Game.C2State.ResultPending)
                    Draws = composer.Compose(state, new SourceHudViewport(1024, 768, 1024, 768, 1), Selector, false, false);
                SourceMatchView match = ((ISourceMatchState)state).Match;
                Trace.Add(new { tick = Game.SourceTick, host = state.Tick, phase = match.Phase.ToString(),
                    countdown = match.SelectionCountdown, camera = Camera.LastCameraTick,
                    timer = match.SelectionTimer,
                    processes = ((ISourcePossessionState)state).Processes.Where(p =>
                        p.Function == "timedown_play_select" || p.Function == "flash_text_proc").ToArray(),
                    home = match.HomePlay.PlaySymbol, away = match.AwayPlay.PlaySymbol });
            };
            Bridge.Restarted += () =>
            {
                observed = 0; Input.Clear(); Selector.Clear(); Events.Clear(); Sounds.Clear(); Selections.Clear();
                Camera.Reset(((ISourceCameraState)Game.State).CameraBoundary);
            };
        }

        internal SourceMatchView Match { get { return ((ISourceMatchState)Game.State).Match; } }
        internal SourceSelectionTimerView Timer { get { return Match.SelectionTimer; } }

        internal void Advance(long target, SourceButtons home = SourceButtons.None,
            SourceButtons away = SourceButtons.None, SourceCommand command = default(SourceCommand),
            int x = 0, int z = 0, SourceCommand awayCommand = default(SourceCommand))
        {
            bool first = true;
            if (configuration.Stations.Count == 0 && command.Action == SourceAction.Continue)
                Bridge.QueueAction(InputAction.Continue);
            while (Game.SourceTick < target)
            {
                Input.Capture(configuration.Stations.Select(s => new SourceCabinetSample(s.StationId,
                    quantizer.SevenLevel(s.StationId == 0 ? x : 0, s.StationId == 0 ? z : 0),
                    s.StationId == 0 ? home : away, first
                        ? s.StationId == 0 ? command : awayCommand : default(SourceCommand))).ToArray());
                first = false;
                Bridge.AdvanceSource(FixedStepBridge.StepSeconds, Input.NextHostFrame);
            }
        }

        internal void AdvanceSamples(long target, Func<int, SourceCabinetSample> sample)
        {
            while (Game.SourceTick < target)
            {
                Input.Capture(configuration.Stations.Select(s => sample(s.StationId)).ToArray());
                Bridge.AdvanceSource(FixedStepBridge.StepSeconds, Input.NextHostFrame);
            }
        }

        internal long OpenMenu()
        {
            for (int i = 0; i < 10000 && (Game.State.Phase != GamePhase.DeadBall || Game.C2State.ResultPending); i++)
                Advance(Game.SourceTick + 1);
            Check(Game.State.Phase == GamePhase.DeadBall, "normal public kickoff reaches a real play result");
            if (configuration.Stations.Count == 0 || configuration.Stations.Any(s => s.StationId == 0))
                Advance(Game.SourceTick + 1, command: new SourceCommand(SourceAction.Continue));
            else
            {
                long target = Game.SourceTick + 1;
                Input.Capture(configuration.Stations.Select((s, i) => new SourceCabinetSample(s.StationId,
                    quantizer.SevenLevel(0, 0), SourceButtons.None,
                    i == 0 ? new SourceCommand(SourceAction.Continue) : default(SourceCommand))).ToArray());
                while (Game.SourceTick < target) Bridge.AdvanceSource(FixedStepBridge.StepSeconds, Input.NextHostFrame);
            }
            Check(Game.State.Phase == (configuration.Stations.Count == 0 ? GamePhase.PreSnap : GamePhase.PlaySelect),
                "public Continue enters actual source selection, including the instant no-human bypass");
            return Game.SourceTick;
        }
    }

    private static void Save(Cabinet cabinet, string output, string name)
    {
        File.WriteAllText(Path.Combine(output, name + ".json"), JsonSerializer.Serialize(new {
            cabinet.Trace, cabinet.Sounds, cabinet.Events,
            selections = cabinet.Selections.Select(e => new { e.SourceTick, e.Team, e.Selection }).ToArray() }));
        cases.Add(name);
        EndCase(name);
    }

    private static bool TimerChildren(Cabinet cabinet)
    {
        return ((ISourcePossessionState)cabinet.Game.State).Processes.Any(p =>
            p.Function == "timedown_play_select" || p.Function == "flash_text_proc");
    }

    private static void Timeout(Cabinet cabinet, string output, string suffix = "")
    {
        BeginCase("actual-timeout-zero-grace-resumption" + suffix);
        long entry = cabinet.OpenMenu(), start = entry + 1, force = start + 10 * 57 + 25;
        Check(cabinet.Match.SelectionCountdown == 9 && !cabinet.Timer.Visible && cabinet.Timer.ForceTick == -1,
            "first-quarter actual menu starts at source argument9 with an unexecuted born process");
        cabinet.Advance(start);
        Check(cabinet.Timer.Visible && cabinet.Timer.Number == 9 && cabinet.Timer.Color == 0xffff0000u &&
            cabinet.Sounds.Count == 1 && cabinet.Sounds[0].SourceTick == start,
            "timer immediately displays red9 and submits one source sound on its first admitted wake");
        var layout = new SourceHudLayout(new SourceHudViewport(1024, 768, 1024, 768, 1), 2);
        double menuScale = Math.Min(layout.Menu.Width / 2 / 252, layout.Menu.Height / 350);
        var timerGlyphs = cabinet.Draws.Where(p => p.Element.EndsWith("/countdown", StringComparison.Ordinal)).ToArray();
        Check(timerGlyphs.Length == 1 && timerGlyphs.All(p => p.Color == 0xffff0000u &&
            Math.Abs(p.Bounds.Height - hud.Font("bast18").Glyph('9').Height * menuScale) < 0.00001),
            "real composed countdown uses one global source BAST18 glyph and LT_RED, never per-side static white bast10");
        cabinet.Advance(start + 7);
        Check(cabinet.Timer.Color == 0xffff0000u, "new timer flasher starts sleep7, never immediate white");
        cabinet.Advance(start + 8);
        Check(cabinet.Timer.Color == 0xffffffffu, "actual flash_text_proc child turns white after its first sleep7");
        cabinet.Advance(start + 15);
        Check(cabinet.Timer.Color == 0xffff0000u, "actual flash_text_proc restores original red after its second sleep7");
        cabinet.Advance(entry + 60);
        Check(cabinet.Match.SelectionCountdown == 8,
            "actual Core/cabinet countdown advances on admitted source wakes, not a static initial9");
        cabinet.Advance(start + 9 * 57);
        Check(cabinet.Timer.Number == 0 && cabinet.Timer.Visible && !cabinet.Timer.ForceSelection &&
            cabinet.Events.All(e => e.Kind != SourceCabinetMenuEventKind.ForcedSelected),
            "zero is a displayed number, not a premature forced choice");
        cabinet.Advance(force - 1);
        Check(cabinet.Timer.Number == 0 && !cabinet.Timer.ForceSelection && cabinet.Game.State.Phase == GamePhase.PlaySelect,
            "zero survives the entire source tsec+25 grace, including its last pre-expiry wake");
        cabinet.Advance(force);
        Check(cabinet.Timer != null && cabinet.Timer.ForceSelection && cabinet.Timer.ForceTick == force && !cabinet.Timer.Visible &&
            !TimerChildren(cabinet) && !cabinet.Events.Any(e => e.Kind == SourceCabinetMenuEventKind.ForcedSelected),
            "expiry deletes number and flasher before force1; no already-passed controller is replayed");
        cabinet.Advance(force + 1, SourceButtons.C, SourceButtons.A, x: 3);
        Check(cabinet.Game.State.Phase == GamePhase.PreSnap &&
            cabinet.Events.Count(e => e.Kind == SourceCabinetMenuEventKind.ForcedSelected) == 2 &&
            cabinet.Selections.Count == 2 &&
            cabinet.Selector.Page(TeamId.Home) == 0 && cabinet.Selector.Cursor(TeamId.Home) == 0 &&
            !cabinet.Selector.Flipped,
            "next admitted actual consumer forces CURRENT highlights once and finishes the caller's pending sleep1");
        cabinet.Advance(force + 2);
        Check(cabinet.Game.State.Phase == GamePhase.PreSnap && cabinet.Camera.LastCameraTick == force + 2 &&
            cabinet.Timer == null && cabinet.Match.SelectionCountdown == -1 && !TimerChildren(cabinet) &&
            cabinet.Events.Count(e => e.Kind == SourceCabinetMenuEventKind.ForcedSelected) == 2,
            "caller finish continuation tears down timer before resuming the real camera and lineup");
        Check(cabinet.Sounds.Count == 10 && cabinet.Sounds.Select(s => s.Number).SequenceEqual(Enumerable.Range(0, 10).Reverse()) &&
            cabinet.Sounds.All(s => s.Bank == "cursors" && s.PlaylistId == 36 && s.Volume == 235 &&
                s.Pan == 127 && s.PriorityWord == 5 && !s.PcmOrDspPlayback),
            "every actual number including zero submits exact CLOCK_LOW_SND/VOLUME4/pan127/priority3 operands once");
        Check(!cabinet.Draws.Any(p => p.Element.EndsWith("/countdown", StringComparison.Ordinal)),
            "real HUD has no stale timer glyph after selection teardown");
        Save(cabinet, output, "actual-timeout-zero-grace-resumption" + suffix);
    }

    private static void EarlyAndHighlight(string output)
    {
        BeginCase("actual-early-side-and-highlight");
        var cabinet = new Cabinet();
        long entry = cabinet.OpenMenu();
        cabinet.Advance(entry + 21);
        cabinet.Advance(entry + 22, away: SourceButtons.B);
        Check(cabinet.Selector.Selected(TeamId.Away), "early manual side selects through the actual captain B edge");
        string preserved = cabinet.Match.AwayPlay.PlaySymbol;
        cabinet.Advance(entry + 23);
        cabinet.Advance(entry + 24, SourceButtons.C);
        cabinet.Advance(entry + 25);
        cabinet.Advance(entry + 26, SourceButtons.A);
        cabinet.Advance(entry + 27, x: 3);
        cabinet.Advance(entry + 28);
        cabinet.Advance(entry + 29, z: 3);
        Check(cabinet.Selector.Cursor(TeamId.Home) == 4 && cabinet.Selector.Page(TeamId.Home) == 1,
            "public cabinet navigation retains the actual current page1 slot4");
        string expected = cabinet.Selector.Slot(cabinet.Game.State, TeamId.Home, 1, 4).PlaySymbol;
        bool flipped = cabinet.Selector.Flipped;
        Check(cabinet.Match.Possession.Offense == TeamId.Home && flipped,
            "actual offensive captain A flips the current source offense before timeout");
        cabinet.Advance(entry + 596);
        cabinet.Advance(entry + 597, awayCommand: new SourceCommand(SourceAction.SelectPlay,
            new SourcePlaySelection("play_suicide_blitz", SourcePlaybookKind.Defense)));
        cabinet.Advance(entry + 598);
        Check(cabinet.Game.State.Phase == GamePhase.PreSnap && cabinet.Match.HomePlay.PlaySymbol == expected &&
            cabinet.Match.HomePlay.PageIndex == 1 && cabinet.Match.HomePlay.SlotIndex == 4 &&
            cabinet.Match.AwayPlay.PlaySymbol == preserved &&
            cabinet.Events.Count(e => e.Kind == SourceCabinetMenuEventKind.ForcedSelected) == 1 &&
            cabinet.Events.Single(e => e.Kind == SourceCabinetMenuEventKind.ForcedSelected).Side == TeamId.Home &&
            cabinet.Match.OffenseFlipped == flipped,
            "timeout selects the actual highlighted play/page/slot/flip and never overwrites the already chosen side");
        Save(cabinet, output, "actual-early-side-and-highlight");
    }

    private static void EarlyReset(string output)
    {
        BeginCase("actual-early-exit-reset-new-menu");
        var cabinet = new Cabinet();
        long entry = cabinet.OpenMenu();
        cabinet.Advance(entry + 21);
        cabinet.Advance(entry + 22, SourceButtons.B, SourceButtons.B);
        Check(cabinet.Game.State.Phase == GamePhase.PreSnap && cabinet.Timer == null && !TimerChildren(cabinet) &&
            cabinet.Sounds.Count == 1, "both early public selections stop the real sleeping timer and its flasher");
        cabinet.Advance(entry + 80);
        Check(cabinet.Sounds.Count == 1 && !TimerChildren(cabinet),
            "early exit cannot leak timer sounds, flash wakes or a later timeout");
        cabinet.Bridge.Restart();
        Check(cabinet.Game.SourceTick == 0 && cabinet.Timer == null && !TimerChildren(cabinet) &&
            cabinet.Sounds.Count == 0 && !cabinet.Selector.Selected(TeamId.Home),
            "actual match restart clears countdown process, selector and event epoch");
        entry = cabinet.OpenMenu();
        cabinet.Advance(entry + 1);
        Check(cabinet.Timer.Number == 9 && !cabinet.Timer.ForceSelection && cabinet.Sounds.Count == 1 &&
            cabinet.Events.All(e => e.Kind != SourceCabinetMenuEventKind.ForcedSelected),
            "new real menu starts a fresh timer, never a stale expiry from the previous match");
        Save(cabinet, output, "actual-early-exit-reset-new-menu");
    }

    private static void Hold(string output)
    {
        BeginCase("actual-instruction-input-not-tutorial-ui");
        var cabinet = new Cabinet();
        int instruction = 9;
        cabinet.Game.SourcePlaySelectionInstructionValue = () => instruction;
        long entry = cabinet.OpenMenu();
        cabinet.Advance(entry + 121);
        Check(cabinet.Timer.Number == 9 && cabinet.Sounds.Count == 1 && !cabinet.Timer.ForceSelection,
            "actual source instruction predicate >=9 holds the displayed number without repeated beeps");
        instruction = 8;
        cabinet.Advance(entry + 122);
        cabinet.Advance(entry + 178);
        Check(cabinet.Timer.Number == 9, "hold release starts a full tsec wait, not a shortened or render-clock wait");
        instruction = 10;
        cabinet.Advance(entry + 179);
        Check(cabinet.Timer.Number == 8 && cabinet.Sounds.Count == 2,
            "instruction input is sampled before each number sleep, not retroactively during it");
        cabinet.Advance(entry + 270);
        Check(cabinet.Timer.Number == 8 && cabinet.Sounds.Count == 2,
            "later held instruction value also holds the next displayed number");
        instruction = 0;
        cabinet.Advance(entry + 271 + 57);
        Check(cabinet.Timer.Number == 7, "source instruction value0 releases the subsequent per-number wait");
        Save(cabinet, output, "actual-instruction-input-not-tutorial-ui");
    }

    private static void CpuAndActiveReset(string output)
    {
        BeginCase("actual-timeout-then-CPU-order");
        var cabinet = new Cabinet(cpuAway: true);
        long entry = cabinet.OpenMenu();
        cabinet.Advance(entry + 596);
        Check(cabinet.Selections.Count == 0, "CPU selection cannot run before the actual timed human controller finishes");
        cabinet.Advance(entry + 597);
        Check(cabinet.Selections.Count == 2 && cabinet.Selections[0].Team == TeamId.Home &&
            cabinet.Selections[1].Team == TeamId.Away &&
            cabinet.Selections.All(e => e.SourceTick == entry + 597) && cabinet.Game.State.Phase == GamePhase.PreSnap,
            "forced human choice precedes CPU completion when the caller's already-pending sleep1 expires");
        cabinet.Advance(entry + 598);
        Check(cabinet.Selections.Count == 2 && cabinet.Selections[1].Team == TeamId.Away &&
            cabinet.Game.State.Phase == GamePhase.PreSnap && cabinet.Timer == null,
            "source CPU choice runs once after the human force, then tears down and resumes play");
        Save(cabinet, output, "actual-timeout-then-CPU-order");
        BeginCase("actual-no-human-instant-caller-order");
        cabinet = new Cabinet(cpuBoth: true);
        entry = cabinet.OpenMenu();
        Check(cabinet.Game.State.Phase == GamePhase.PreSnap && cabinet.Selections.Count == 2 &&
            cabinet.Selections[0].Team == TeamId.Away && cabinet.Selections[1].Team == TeamId.Home &&
            cabinet.Timer == null && !TimerChildren(cabinet) && cabinet.Sounds.Count == 0,
            "no-human source bypass chooses CPU offense then defense immediately, without creating a timer");
        Save(cabinet, output, "actual-no-human-instant-caller-order");
        BeginCase("actual-active-menu-restart");
        cabinet = new Cabinet();
        entry = cabinet.OpenMenu();
        cabinet.Advance(entry + 30);
        Check(TimerChildren(cabinet), "restart control has actual live timer and flasher children");
        cabinet.Bridge.Restart();
        Check(cabinet.Timer == null && !TimerChildren(cabinet) && cabinet.Sounds.Count == 0 &&
            cabinet.Game.SourceTick == 0, "restart during an active menu cannot retain a countdown or orphan flasher");
        entry = cabinet.OpenMenu();
        cabinet.Advance(entry + 58);
        Check(cabinet.Timer.Number == 8 && cabinet.Sounds.Count == 2 &&
            cabinet.Events.All(e => e.Kind != SourceCabinetMenuEventKind.ForcedSelected),
            "new match independently executes first9 then8 without stale timer sound/force state");
        Save(cabinet, output, "actual-active-menu-restart");
    }

    private static void Failures(string output, bool onlyOutput = false)
    {
        Cabinet cabinet;
        long entry;
        bool failed;
        if (!onlyOutput)
        {
            BeginCase("actual-timeout-dependency-failure");
            cabinet = new Cabinet(selectionCommand: (play, flip) => {
                throw new SourceInterfaceException("actual selection dependency unavailable");
            });
            entry = cabinet.OpenMenu();
            failed = false;
            try { cabinet.Advance(entry + 597); }
            catch (SourceInterfaceException error) { failed = error.Message.Contains("actual selection dependency unavailable"); }
            Check(failed && !cabinet.Selector.Selected(TeamId.Home),
                "forced unavailable selection dependency fails explicitly instead of inventing a play/RNG policy");
            Save(cabinet, output, "actual-timeout-dependency-failure");
            BeginCase("actual-missing-timeout-consumer");
            cabinet = new Cabinet();
            entry = cabinet.OpenMenu();
            cabinet.Game.SourceStationInputTransform = null;
            failed = false;
            try { cabinet.Advance(entry + 596); }
            catch (InvalidOperationException error) { failed = error.Message.Contains("actual cabinet selector"); }
            Check(failed, "missing actual timeout selector propagates a standard failure, never configured or CPU fallback");
            Save(cabinet, output, "actual-missing-timeout-consumer");
        }
        BeginCase("actual-timer-output-fail-closed");
        cabinet = new Cabinet();
        entry = cabinet.OpenMenu();
        cabinet.Game.SourceSelectionTimerSoundRaised += call => { throw new InvalidOperationException("timer output failed"); };
        failed = false;
        try { cabinet.Advance(entry + 1); }
        catch (InvalidOperationException error) { failed = error.Message.Contains("timer output failed"); }
        Check(failed, "actual timer sound consumer failure propagates from its admitted process");
        failed = false;
        try { cabinet.Advance(entry + 2); }
        catch (InvalidOperationException error) { failed = error.Message.Contains("restart") || error.Message.Contains("Restart"); }
        Check(failed, "failed timer output cannot be retried or rewound on the next source wake");
        Save(cabinet, output, "actual-timer-output-fail-closed");
    }

    private static void TimerSequences(string fixture, string output)
    {
        using (JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(fixture)))
            foreach (JsonElement item in document.RootElement.EnumerateArray())
            {
                string name = item.GetProperty("name").GetString();
                BeginCase(name);
                var host = new SourcePortableProcessHost();
                long serial = 0;
                int initial = SourceMatchFlow.PlaySelectionCountdownStart(item.GetProperty("initial").GetInt32() == 9 ? 0 : 1);
                var timer = new SourcePlaySelectionTimer(host, initial, () => ++serial, function => { }, call =>
                    Check(call.Bank == "cursors" && call.PlaylistId == 36 && call.Volume == 235 &&
                        call.Pan == 127 && call.PriorityWord == 5, "production timer submits exact authenticated sound operands"));
                var rows = new List<object>();
                for (int tick = 1; tick <= item.GetProperty("ticks").GetInt32(); tick++)
                {
                    host.BeginTick(tick);
                    if (tick == item.GetProperty("suspendAt").GetInt32())
                    {
                        bool both = item.GetProperty("suspendBoth").GetInt32() == 1;
                        host.SuspendMultiple(both ? 0 : SourcePlaySelectionTimer.TimerId, both ? 0 : -1);
                    }
                    if (tick == item.GetProperty("resumeAt").GetInt32()) host.ResumeAll();
                    if (tick == item.GetProperty("stopAt").GetInt32()) timer.Stop();
                    int value = tick >= item.GetProperty("holdStart").GetInt32() &&
                        tick < item.GetProperty("holdEnd").GetInt32() ? item.GetProperty("holdValue").GetInt32() : 0;
                    timer.Dispatch(() => value);
                    SourceSelectionTimerView view = timer.Capture(false, false);
                    SourceProcessView? parent = null, child = null;
                    foreach (SourceProcessView process in host.Processes)
                        if (process.Id == SourcePlaySelectionTimer.TimerId) parent = process;
                        else if (process.Id == SourcePlaySelectionTimer.MessageId) child = process;
                    var operations = host.Records.Where(r => r.Operation == "Create" || r.Operation == "Die" ||
                        r.Operation == "Sleep" || r.Operation.StartsWith("Timer", StringComparison.Ordinal))
                        .Select(r => new object[] { r.Operation, r.Id, r.Value }).ToArray();
                    rows.Add(new { tick, number = view.Number, visible = view.Visible, color = view.Color,
                        force = view.ForceSelection, timerWord = parent.HasValue ? (int)parent.Value.SleepWord : -1,
                        flashWord = child.HasValue ? (int)child.Value.SleepWord : -1, operations });
                }
                File.WriteAllText(Path.Combine(output, name + ".managed.json"), JsonSerializer.Serialize(rows));
                cases.Add(name);
                EndCase(name);
            }
    }

    private static int Main(string[] args)
    {
        Cabinet cabinet = null;
        try
        {
            if (args.Length != 13)
                throw new InvalidOperationException("An explicit complete timer/random execution scope and control binding are required.");
            string group = args[11];
            string[] selectedCases = ValidateSelectionScope(group, args[12], args[8], args[10]);
            scopeAdmitted = true;
            if (group == "all" || group == "timer-traces") TimerSequences(args[8], args[7]);
            if (group != "timer-traces")
            {
                inputs = FullKernelInputs.Load(args[0], soundFunctionsPath: args[1], c2M1InputsPath: args[2]);
                impact = SourceCinematicLibrary.Read(File.ReadAllBytes(args[3]), SourceTeamResourceProfile.C2M1ImpactSha256);
                normal = SourceNormalCameraData.Read(File.ReadAllBytes(args[4]));
                speech = SourcePossessionSoundData.Read(File.ReadAllBytes(args[5]));
                hud = SourceInterfaceData.Read(File.ReadAllBytes(args[6]), SourceInterfacePackagePlan.HudSha256);
                fire = SourceFireSpfxReader.Read(File.ReadAllBytes(Path.Combine(args[9], "manifest.json")),
                    "90a134af37fe6d43f13977f7903315ee0d9a7f37f700669b365f45dd7c941d56",
                    path => File.ReadAllBytes(Path.Combine(args[9], path)));
                if (group == "all") RandomGestureReproduction(args[7]);
                if (group == "all" || randomLifecycleNames.Contains(group)) RandomLifecycle(args[7], selectedCases);
                if (group == "all" || (group.StartsWith("random-", StringComparison.Ordinal) && !randomLifecycleNames.Contains(group)))
                    RandomCases(args[10], args[7], selectedCases);
                if (group == "all" || group == "actual-timeout-zero-grace-resumption")
                {
                    cabinet = new Cabinet();
                    Timeout(cabinet, args[7]);
                }
                if (group == "all")
                    Timeout(new Cabinet(fireEnabled: true), args[7], "-FX");
                if (group == "all" || group == "actual-early-side-and-highlight")
                    EarlyAndHighlight(args[7]);
                if (group == "all")
                {
                    EarlyReset(args[7]);
                    Hold(args[7]);
                    CpuAndActiveReset(args[7]);
                }
                if (group == "all" || group == "actual-timer-output-fail-closed")
                    Failures(args[7], group != "all");
            }
            scopePassed = true;
            Console.WriteLine(JsonSerializer.Serialize(new { status = "passed", scope = group, assertions, cases, randomCases, randomLifecycleCases }));
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine("SELECTION ASSERTION: " + error.Message + "\n" + error.StackTrace);
            return 1;
        }
        finally
        {
            if (cabinet != null) File.WriteAllText(Path.Combine(args[7], "actual-cabinet.json"),
                JsonSerializer.Serialize(cabinet.Trace));
            if (args.Length > 7 && Directory.Exists(args[7]))
                File.WriteAllText(Path.Combine(args[7], "execution-scope.json"), JsonSerializer.Serialize(new {
                    schema = "selection-execution-scope/v2", status = scopePassed ? "passed" : "failed",
                    admitted = scopeAdmitted, control = args.Length > 12 ? args[12] : null,
                    scope = args.Length > 11 ? args[11] : null, requestedCases = requestedExecutionCases,
                    startedCases = startedExecutionCases, completedCases = completedExecutionCases, assertions
                }));
        }
    }
}
