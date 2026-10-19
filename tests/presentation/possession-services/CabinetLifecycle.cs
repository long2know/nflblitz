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

internal static class CabinetLifecycleChecks
{
    private static void Check(bool condition, string name) { PossessionChecks.Check(condition, name); }

    private static void Reject<T>(Action operation, string message) where T : Exception
    {
        bool rejected = false;
        try { operation(); }
        catch (T error) { rejected = error.Message.Contains(message); }
        Check(rejected, "strict cabinet contract rejects " + message);
    }

    internal static void Run(SourceInterfaceData data, string output)
    {
        foreach (bool fire in new[] { false, true }) Integration(data, output, fire);
        OrderingAndMenuAdmission(data);
    }

    private static SourceMatchConfiguration Configuration()
    {
        return new SourceMatchConfiguration(SourceSimulationProfile.OriginalRelease57Hz,
            SourceStationLayout.FourStation,
            new SourceTeamSelection(4, Enumerable.Range(0, 7).ToArray()),
            new SourceTeamSelection(8, Enumerable.Range(0, 7).ToArray()),
            new[] { new SourceStationConfiguration(3, TeamId.Home, 3),
                new SourceStationConfiguration(0, TeamId.Away, 10),
                new SourceStationConfiguration(2, TeamId.Home, 4),
                new SourceStationConfiguration(1, TeamId.Away, 11) },
            TeamId.Home, SourceCaptainSlot.Second);
    }

    private sealed class Cabinet
    {
        internal readonly GameSimulation Game;
        internal readonly SourceCabinetPlaySelection Selector;
        internal readonly SourceCabinetInputBuffer Input;
        internal readonly FixedStepBridge Bridge;
        internal readonly SourceCinematicPlayback Camera;
        internal readonly List<SourceInputFrame> Scans = new List<SourceInputFrame>();
        internal readonly List<SourceCabinetMenuEvent> MenuEvents = new List<SourceCabinetMenuEvent>();
        internal readonly List<object> Trace = new List<object>();
        internal SourceHudState Hud;
        internal IReadOnlyList<SourceHudPrimitive> Draws;
        private readonly SourceMatchConfiguration configuration;
        private readonly SourceCabinetQuantizer quantizer;
        private int epoch, lastScan;

        internal Cabinet(SourceInterfaceData data, bool fire)
        {
            configuration = Configuration();
            Game = PossessionChecks.Game(fireEffects: fire, configurationOverride: configuration);
            Game.Model.Quarter = 2;
            Input = new SourceCabinetInputBuffer(configuration);
            quantizer = new SourceCabinetQuantizer(data.Input);
            Selector = new SourceCabinetPlaySelection(data);
            Selector.EventRaised += MenuEvents.Add;
            Game.SourceStationInputTransform = (state, tick, frame) =>
            {
                Check(tick == ++lastScan && ((ISourceGameState)state).SourceTick == tick,
                    "actual cabinet selector observes every ordered source dispatch, never host/render holds");
                Check(frame != null && frame.Stations.Select(s => s.StationId).SequenceEqual(
                    configuration.Stations.Select(s => s.StationId)),
                    "actual source drain retains every configured station and its independent identity");
                int random = ((SourceInjectedRandom)Game.SourceRandomService).Trace.Count;
                SourceInputFrame result = Selector.ApplyAtSourceTick(state, tick, frame);
                Check(((SourceInjectedRandom)Game.SourceRandomService).Trace.Count == random,
                    "cabinet input observation consumes no hidden gameplay or RNG work");
                Scans.Add(frame);
                Trace.Add(new { epoch, tick, kind = "input-scan",
                    stations = frame.Stations.Select(s => new { id = s.StationId,
                        held = (int)s.Held, pressed = (int)s.Pressed, released = (int)s.Released,
                        command = s.Command.IsSpecified ? s.Command.Action.ToString() : null }).ToArray(),
                    gameWake = ((ISourcePossessionState)state).Processes.Single(p => p.Function == "game_proc").LastWakeTick });
                return result;
            };
            Camera = new SourceCinematicPlayback(PossessionChecks.Cinematics, PossessionChecks.Normal,
                ((ISourceCameraState)Game.State).CameraBoundary);
            Game.SourceCameraHeadingProvider = (state, tick) =>
            {
                Camera.ObserveCameraProcessSuspension(false);
                Camera.CameraSourceTick(((ISourceCameraState)state).CameraBoundary, 512, 384,
                    Game.DrawSourceCameraRandom, Game.SourceCameraProcesses);
                Game.ApplySourceCameraFeedback(Camera.CameraFeedback);
                if (Camera.CameraFeedback.CameraCompleted)
                {
                    SourceCinematicTransform camera = Camera.RequireSourceCameraAtTick(tick);
                    Game.SetSourceMovementCameraFrame(new SourceMovementCameraFrame(camera.Position,
                        camera.Transform, 512, 384), tick);
                }
                else Camera.ObserveCameraProcessSuspension(true);
                return Camera.SourceHeading1024;
            };
            var composer = new SourceHudComposer(data);
            Bridge = new FixedStepBridge(Game);
            Bridge.AfterHostStep += state =>
            {
                Camera.ObserveCameraProcessSuspension(((ISourcePossessionState)state).Processes
                    .Single(p => p.Function == "cambot_proc").Suspended);
                Hud = new SourceHudState(state, data);
                Draws = composer.Compose(state, new SourceHudViewport(1024, 768, 1024, 768, 1),
                    Selector, false, false);
            };
            Bridge.Restarted += () =>
            {
                epoch++; lastScan = 0; Scans.Clear(); MenuEvents.Clear();
                Input.Clear(); Selector.Clear();
                Camera.Reset(((ISourceCameraState)Game.State).CameraBoundary);
            };
        }

        internal SourceCabinetSample[] Samples(Func<int, SourceButtons> buttons,
            Func<int, SourceCommand> commands = null)
        {
            return configuration.Stations.Reverse().Select(s => new SourceCabinetSample(s.StationId,
                quantizer.SevenLevel(0, 0), buttons(s.StationId),
                commands == null ? default(SourceCommand) : commands(s.StationId))).ToArray();
        }

        internal void Advance(long tick, Func<int, SourceButtons> buttons,
            Func<int, SourceCommand> commands = null)
        {
            bool first = true;
            while (Game.SourceTick < tick)
            {
                Input.Capture(Samples(buttons, first ? commands : null));
                first = false;
                long before = Game.SourceTick;
                int scans = Scans.Count;
                Bridge.AdvanceSource(FixedStepBridge.StepSeconds, Input.NextHostFrame);
                if (Game.SourceTick == before)
                    Check(Scans.Count == scans, "a real 60Hz host hold cannot call the 57Hz selector twice");
            }
        }

        internal SourceStationInput Scan(int tick, int station = 3)
        { return Scans[tick - 1].Stations.Single(s => s.StationId == station); }
    }

    private static void Integration(SourceInterfaceData data, string output, bool fire)
    {
        var cabinet = new Cabinet(data, fire);
        GameSimulation game = cabinet.Game;
        Func<int, SourceButtons> neutral = station => SourceButtons.None;
        Func<int, SourceButtons> turbo = station => station == 3 ? SourceButtons.C : SourceButtons.None;
        cabinet.Advance(1, neutral);
        game.ProbePossessionChange();
        cabinet.Advance(2, neutral);
        uint clock = ((ISourceGameState)game.State).PackedGameTime;
        int live = game.Model.LiveTicks;
        var roots = game.Model.Players.Select(p => p.Position).ToArray();
        var history = game.Model.Players.Select(p => p.SourceButtonHistory.ToArray()).ToArray();
        var random = (SourceInjectedRandom)game.SourceRandomService;
        cabinet.Advance(3, turbo);
        cabinet.Advance(4, turbo);
        cabinet.Advance(5, neutral);
        cabinet.Advance(6, station => station == 3 ? SourceButtons.A : SourceButtons.None);
        cabinet.Advance(7, neutral);
        cabinet.Advance(8, station => station == 3 ? SourceButtons.B : SourceButtons.None);
        cabinet.Advance(9, neutral);
        cabinet.Advance(10, turbo);
        cabinet.Advance(102, turbo);
        Check(cabinet.Scans.Count == 102 && cabinet.Scan(3).Pressed == SourceButtons.C &&
            cabinet.Scan(4).Pressed == SourceButtons.None && cabinet.Scan(5).Released == SourceButtons.C &&
            cabinet.Scan(6).Pressed == SourceButtons.A && cabinet.Scan(7).Released == SourceButtons.A &&
            cabinet.Scan(8).Pressed == SourceButtons.B && cabinet.Scan(9).Released == SourceButtons.B &&
            cabinet.Scan(10).Pressed == SourceButtons.C && cabinet.Scan(102).Pressed == SourceButtons.None,
            "actual cabinet press, held and release scans continue across the genuine frozen process gap");
        Check(game.Model.Players.Select((p, i) => p.Position.X == roots[i].X &&
            p.Position.Z == roots[i].Z && p.SourceButtonHistory.SequenceEqual(history[i])).All(x => x) &&
            game.Model.LiveTicks == live && ((ISourceGameState)game.State).PackedGameTime == clock,
            "ordered cabinet scans do not run frozen actor/button-history/game/clock continuations");
        Check(cabinet.MenuEvents.Count == 0 && !cabinet.Selector.Flipped &&
            cabinet.Selector.Page(TeamId.Home) == 0 && cabinet.Selector.Page(TeamId.Away) == 0 &&
            !random.Trace.Any(draw => draw.Site.SourceTick > 2 && draw.Site.Process == "game_proc"),
            "frozen live cabinet presses do not select menus or burn game_proc RNG");
        Check(cabinet.Hud.StatusProcessSuspended &&
            cabinet.Draws.Count(p => p.Element.StartsWith("possession/", StringComparison.Ordinal)) == 1,
            "real authored HUD displays the logical message while its status process is frozen");
        cabinet.Advance(103, turbo);
        var processes = ((ISourcePossessionState)game.State).Processes;
        int controlled = ((ISourceMatchState)game.State).Match.Stations.Single(s => s.StationId == 3).ControlledPlayerId;
        Check(((ISourcePossessionState)game.State).PossessionMessages.Count == 0 &&
            !processes.Any(p => p.Id == 0x0f15 || p.Id == 0x0f10) &&
            !cabinet.Draws.Any(p => p.Element.StartsWith("possession/", StringComparison.Ordinal)),
            "real cabinet/Core/HUD message cleanup removes both logical sprites and their process children");
        Check(processes.Single(p => p.Serial == 2 + controlled).LastWakeTick == 103 &&
            processes.Single(p => p.Serial == 1).LastWakeTick == 2 &&
            processes.Single(p => p.Serial == 18).LastWakeTick == 2 &&
            processes.Single(p => p.Serial == 23).LastWakeTick == 2 && cabinet.Hud.StatusProcessSuspended,
            "actual cabinet resume admits later actors before the next earlier game/clock/status slot");
        Check(cabinet.Scan(103).Held == SourceButtons.C && cabinet.Scan(103).Pressed == SourceButtons.None &&
            game.Model.Players[controlled].SourceButtons == SourceDroneButtons.C &&
            game.Model.Players[controlled].SourceNewButtons == SourceDroneButtons.C,
            "first resumed actor derives current held C from its last actor sample, not a replay of frozen A/B edges");
        if (fire) Check(!game.SourceFireRecords.Any(r => r.Kind == "Wake"),
            "real cabinet resumption does not invent an already-passed FX wake");
        cabinet.Advance(104, turbo);
        processes = ((ISourcePossessionState)game.State).Processes;
        Check(processes.Single(p => p.Serial == 1).LastWakeTick == 104 &&
            processes.Single(p => p.Serial == 18).LastWakeTick == 104 &&
            processes.Single(p => p.Serial == 23).LastWakeTick == 104 &&
            game.Model.LiveTicks == live + 1 && !cabinet.Hud.StatusProcessSuspended &&
            game.Model.Players[controlled].SourceNewButtons == SourceDroneButtons.None,
            "actual cabinet selector permits the next real game/clock/status visit without repeating held presses");
        if (fire) Check(game.SourceFireRecords.Any(r => r.Kind == "Wake"),
            "actual cabinet FX visits resume on the next real level-zero dispatch");
        cabinet.Advance(105, neutral);
        Check((game.Model.Players[controlled].SourceReleasedButtons & SourceDroneButtons.C) != 0 &&
            cabinet.Scan(105).Released == SourceButtons.C,
            "post-resume public release reaches the resumed actor exactly at its current source wake");
        game.ProbeTurnoverOnDowns(false);
        cabinet.Advance(106, neutral, station => station == 3 ? new SourceCommand(SourceAction.Continue) : default(SourceCommand));
        Check(game.State.Phase == GamePhase.PlaySelect, "post-gap public station Continue enters actual play selection");
        cabinet.Advance(107, neutral);
        cabinet.Advance(108, station => SourceButtons.B);
        cabinet.Advance(109, neutral);
        Check(!cabinet.Selector.Selected(TeamId.Home) && !cabinet.Selector.Selected(TeamId.Away),
            "new menu sleep20 cannot inherit frozen or early cabinet B presses");
        cabinet.Advance(126, neutral);
        SourceMatchView match = ((ISourceMatchState)game.State).Match;
        int offense = SourceCabinetStations.Captain(match, match.Possession.Offense);
        int defense = SourceCabinetStations.Captain(match, match.Possession.Offense == TeamId.Home ? TeamId.Away : TeamId.Home);
        cabinet.Advance(127, station => station == offense ? SourceButtons.C : SourceButtons.None);
        Check(cabinet.Selector.Page(match.Possession.Offense) == 1 &&
            cabinet.MenuEvents.Count(e => e.Kind == SourceCabinetMenuEventKind.PageChanged) == 1,
            "first actual awake menu read uses a fresh public captain C edge after original sleep20");
        cabinet.Advance(128, station => station == offense ? SourceButtons.C : SourceButtons.None);
        Check(cabinet.MenuEvents.Count(e => e.Kind == SourceCabinetMenuEventKind.PageChanged) == 1,
            "held captain C never becomes a repeated menu edge after the possession gap");
        cabinet.Advance(129, neutral);
        cabinet.Advance(130, station => station == offense ? SourceButtons.A : SourceButtons.None);
        Check(cabinet.Selector.Flipped && ((ISourceMatchState)game.State).Match.OffenseFlipped == true,
            "post-gap original offensive A reaches actual Core off_flip");
        cabinet.Advance(131, neutral);
        cabinet.Advance(132, station => station == offense || station == defense ? SourceButtons.B : SourceButtons.None);
        Check(game.State.Phase == GamePhase.PreSnap && cabinet.Camera.LastCameraTick == 132 &&
            cabinet.Selector.Selected(TeamId.Home) && cabinet.Selector.Selected(TeamId.Away),
            "both real cabinet captains select new original plays and resume the actual camera owner");
        long paused = game.SourceTick;
        cabinet.Input.Capture(cabinet.Samples(station => SourceButtons.B));
        cabinet.Input.Clear(); cabinet.Selector.ClearPendingInput(); cabinet.Bridge.SetActive(false);
        cabinet.Bridge.AdvanceSource(0.1, cabinet.Input.NextHostFrame);
        Check(game.SourceTick == paused && cabinet.Input.PendingFrames == 0,
            "public focus pause clears pending cabinet edges without hidden source catch-up");
        cabinet.Bridge.SetActive(true);
        cabinet.Input.Capture(cabinet.Samples(station => SourceButtons.B));
        Check(cabinet.Input.WaitingForNeutral && cabinet.Input.NextHostFrame().Stations.All(s => s.Held == SourceButtons.None),
            "public focus resume refuses still-held cabinet actions until actual neutral");
        cabinet.Input.Capture(cabinet.Samples(neutral));
        cabinet.Advance(133, neutral);
        Check(cabinet.Scan(133).Pressed == SourceButtons.None,
            "focus reset preserves consecutive source ordering without stale action replay");
        cabinet.Bridge.Restart();
        Check(game.SourceTick == 0 && !cabinet.Selector.Flipped && cabinet.Selector.Page(TeamId.Home) == 0 &&
            cabinet.Input.WaitingForNeutral && !((ISourcePossessionState)game.State).Processes.Any(p => p.Suspended) &&
            ((ISourcePossessionState)game.State).PossessionMessages.Count == 0,
            "real host restart resets selector, pending cabinet state, process suspension and message epoch together");
        cabinet.Advance(3, neutral);
        Check(cabinet.Scans.Count == 3 && cabinet.Camera.LastCameraTick == 3 &&
            cabinet.Scans.All(f => f.Stations.All(s => s.Pressed == SourceButtons.None && s.Released == SourceButtons.None)),
            "new match consumes source ticks1..3 once with no held/release or message leakage");
        string name = fire ? "actual-cabinet-Core-FX-selector-lifecycle" : "actual-cabinet-Core-selector-lifecycle";
        File.WriteAllText(Path.Combine(output, name + ".json"), JsonSerializer.Serialize(cabinet.Trace));
        PossessionChecks.Case(name);
    }

    private static void OrderingAndMenuAdmission(SourceInterfaceData data)
    {
        var cabinet = new Cabinet(data, false);
        Func<int, SourceButtons> neutral = station => SourceButtons.None;
        cabinet.Advance(1, neutral);
        var selector = new SourceCabinetPlaySelection(data);
        SourceInputFrame sample = cabinet.Game.Model.SourceCurrentInput.StationInputs;
        selector.ApplyAtSourceTick(cabinet.Game.State, 1, sample);
        Reject<SourceInterfaceException>(() => selector.ApplyAtSourceTick(cabinet.Game.State, 1, sample), "every ordered SOURCE tick");
        Reject<SourceInterfaceException>(() => selector.ApplyAtSourceTick(cabinet.Game.State, 0, sample), "every ordered SOURCE tick");
        cabinet.Advance(3, neutral);
        Reject<SourceInterfaceException>(() => selector.ApplyAtSourceTick(cabinet.Game.State, 3, sample), "every ordered SOURCE tick");
        Reject<SourceInterfaceException>(() => selector.ApplyAtSourceTick(cabinet.Game.State, 2, sample), "current dispatched SOURCE snapshot");
        var samples = cabinet.Samples(neutral);
        Reject<ArgumentException>(() => cabinet.Input.Capture(samples.Take(3).ToArray()), "all configured");
        Reject<ArgumentException>(() => cabinet.Input.Capture(new[] { samples[0], samples[0], samples[2], samples[3] }), "duplicate source station");
        cabinet.Game.ProbeTurnoverOnDowns(false);
        cabinet.Advance(4, neutral, station => station == 3 ? new SourceCommand(SourceAction.Continue) : default(SourceCommand));
        cabinet.Advance(5, neutral);
        cabinet.Game.Model.SourceProcesses.SuspendMultiple(0x0010, -1);
        cabinet.Advance(28, station => SourceButtons.B);
        cabinet.Advance(29, neutral);
        cabinet.Advance(30, station => SourceButtons.B);
        Check(cabinet.MenuEvents.Count == 0 && !cabinet.Selector.Selected(TeamId.Home) &&
            !cabinet.Selector.Selected(TeamId.Away),
            "input scans cannot execute a suspended actual menu consumer or consume its sleep20");
        cabinet.Game.Model.SourceProcesses.ResumeAll();
        cabinet.Advance(31, station => SourceButtons.B);
        cabinet.Advance(32, neutral);
        cabinet.Advance(49, station => SourceButtons.B);
        Check(cabinet.MenuEvents.Count == 0 && cabinet.Game.State.Phase == GamePhase.PlaySelect,
            "original menu sleep20 counts only admitted process visits, not frozen source scans");
        cabinet.Advance(50, station => SourceButtons.C);
        Check(cabinet.MenuEvents.Count(e => e.Kind == SourceCabinetMenuEventKind.PageChanged) == 2,
            "actual resumed menu sleep20 expires at the twentieth later admitted visit; events=" +
            JsonSerializer.Serialize(cabinet.MenuEvents.Select(e => new { e.SourceTick, e.Kind, e.StationId })) +
            ";sample=" + (int)cabinet.Scan(50).Pressed + ";held=" + (int)cabinet.Scan(50).Held);
        cabinet.Game.Model.SourceProcesses.Kill(1);
        cabinet.Game.ProbePublish();
        selector = new SourceCabinetPlaySelection(data);
        Reject<SourceInterfaceException>(() => selector.ApplyAtSourceTick(cabinet.Game.State, 50, sample), "one actual game_proc");
        PossessionChecks.Case("strict-cabinet-order-and-actual-menu-admission");
    }
}
