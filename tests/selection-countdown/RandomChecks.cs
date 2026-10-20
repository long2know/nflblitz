using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Blitz.Contracts;
using Blitz.Gameplay;
using Blitz.Runtime;

internal static partial class SelectionCountdownChecks
{
    private static readonly List<string> randomCases = new List<string>();
    private static readonly List<string> randomLifecycleCases = new List<string>();
    private static readonly string[] randomLifecycleNames = {
        "random-pause-render", "random-restart", "random-dependency-failure", "random-output-failure"
    };

    private static string[] ValidateSelectionScope(string group, string control, string timerFixture, string fixture)
    {
        string expected;
        if (control == "baseline" || control == "final-baseline") expected = "all";
        else if (string.IsNullOrWhiteSpace(control) || !controlScopes.TryGetValue(control, out expected))
            throw new InvalidOperationException("Unknown/omitted control/scope binding.");
        if (group != expected)
            throw new InvalidOperationException("Wrong/substituted control/scope binding.");
        using (JsonDocument timers = JsonDocument.Parse(File.ReadAllBytes(timerFixture)))
        {
            var items = timers.RootElement.EnumerateArray().ToArray();
            if (!items.Select(item => item.GetProperty("name").GetString()).SequenceEqual(timerCaseNames) ||
                items.Sum(item => item.GetProperty("ticks").GetInt32()) != 10417)
                throw new InvalidOperationException("Every execution scope requires the exact 17-case/10417-dispatch timer inventory.");
        }
        using (JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(fixture)))
        {
            string[] names = document.RootElement.EnumerateArray().Select(item => item.GetProperty("name").GetString()).ToArray();
            if (names.Length != 33 || names.Distinct().Count() != 33 || names.Any(string.IsNullOrWhiteSpace))
                throw new InvalidOperationException("Every execution scope requires the complete distinct 33-case random inventory.");
            if (group == "all")
            {
                requestedExecutionCases = timerCaseNames.Concat(new[] { "actual-random-gesture" })
                    .Concat(randomLifecycleNames).Concat(names).Concat(callerCaseNames).ToArray();
                return null;
            }
            if (group == "timer-traces")
            {
                requestedExecutionCases = timerCaseNames;
                return null;
            }
            if (!names.Contains(group) && !randomLifecycleNames.Contains(group) && !callerCaseNames.Contains(group))
                throw new InvalidOperationException("Unknown/empty/substituted timer/random execution scope.");
            requestedExecutionCases = new[] { group };
            return new[] { group };
        }
    }

    private static void RandomGestureReproduction(string output)
    {
        BeginCase("actual-random-gesture");
        var cabinet = new Cabinet();
        long entry = cabinet.OpenMenu();
        try
        {
            cabinet.Advance(entry + 21);
            cabinet.Advance(entry + 22, x: -3);
            cabinet.Advance(entry + 23);
            cabinet.Advance(entry + 24, SourceButtons.C, x: -3);
            cabinet.Advance(entry + 90);
            Check(cabinet.Selector.Selected(TeamId.Home),
                "actual public left-neutral-left+C gesture completes the source random controller");
            EndCase("actual-random-gesture");
        }
        finally
        {
            File.WriteAllText(Path.Combine(output, "actual-random-gesture.json"),
                JsonSerializer.Serialize(new { cabinet.Trace, cabinet.Events, cabinet.Selections }));
        }
    }

        private static void RandomCases(string fixture, string output, string[] selectedCases = null)
        {
            var boundaries = new Dictionary<string, object>();
            using (JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(fixture)))
                foreach (JsonElement item in document.RootElement.EnumerateArray())
                {
                    string name = item.GetProperty("name").GetString();
                    if (selectedCases != null && !selectedCases.Contains(name)) continue;
                    BeginCase(name);
                    int raw = item.GetProperty("raw").GetInt32();
                    bool four = item.GetProperty("four").GetBoolean();
                    var stations = item.GetProperty("stations").EnumerateArray().Select(s =>
                        new SourceStationConfiguration(s.GetProperty("id").GetInt32(),
                            (TeamId)s.GetProperty("side").GetInt32(), s.GetProperty("side").GetInt32() * 7 + 3)).ToArray();
                    // Teammates cannot control the same actor; ownership is configured, never inferred from station ID.
                    var used = new HashSet<int>();
                    for (int i = 0; i < stations.Length; i++)
                    {
                        int actor = (int)stations[i].Team * 7 + 3;
                        while (!used.Add(actor)) actor++;
                        stations[i] = new SourceStationConfiguration(stations[i].StationId, stations[i].Team, actor);
                    }
                    var configuration = new SourceMatchConfiguration(SourceSimulationProfile.OriginalRelease57Hz,
                        four ? SourceStationLayout.FourStation : SourceStationLayout.TwoStation,
                        new SourceTeamSelection(4, Enumerable.Range(0, 7).ToArray()),
                        new SourceTeamSelection(8, Enumerable.Range(0, 7).ToArray()), stations,
                        TeamId.Away, (SourceCaptainSlot)item.GetProperty("captain").GetInt32());
                    var random = new SourceInjectedRandom(site => raw, new SourceRandomProvenance("cabinet-random-controlled",
                        "Authored unfiltered constant raw input at EVERY source RNG site, including construction",
                        "case=" + name + ";raw=" + raw, SourceRandomEvidence.ExternalUnverified));
                    var cabinet = new Cabinet(configurationOverride: configuration, randomFactory: seed => random);
                    long entry = cabinet.OpenMenu();
                    Check(cabinet.Match.Possession.Offense == TeamId.Home,
                        "controlled original caller starts with the actual receiving Home offense: " + name);
                    boundaries.Add(name, new { offense = (int)cabinet.Match.Possession.Offense,
                        los = cabinet.Game.Model.SourceRules.LineOfScrimmage,
                        firstDown = cabinet.Game.Model.SourceRules.FirstDown,
                        homeScore = cabinet.Game.State.HomeScore, awayScore = cabinet.Game.State.AwayScore,
                        down = cabinet.Game.State.Down, quarter = cabinet.Match.Period.QuarterIndex,
                        time = ((ISourceGameState)cabinet.Game.State).PackedGameTime, playType = (int)cabinet.Match.PlayKind });
                    var events = item.GetProperty("events").EnumerateArray().ToArray();
                    var current = new Dictionary<int, SourceCabinetSample>();
                    var quantizer = new SourceCabinetQuantizer(hud.Input);
                    foreach (SourceStationConfiguration station in stations)
                        current.Add(station.StationId, new SourceCabinetSample(station.StationId,
                            quantizer.SevenLevel(0, 0), SourceButtons.None));
                    long done = -1;
                    var flipState = new List<int>();
                    for (int tick = 1; tick <= item.GetProperty("ticks").GetInt32(); tick++)
                    {
                        if (tick == item.GetProperty("suspendAt").GetInt32())
                            cabinet.Game.Model.SourceProcesses.SuspendMultiple(0x0010, -1);
                        if (tick == item.GetProperty("resumeAt").GetInt32())
                            cabinet.Game.Model.SourceProcesses.ResumeMultiple(0x0010, -1);
                        foreach (JsonElement input in events.Where(e => e.GetProperty("tick").GetInt32() == tick))
                        {
                            int station = input.GetProperty("station").GetInt32();
                            current[station] = new SourceCabinetSample(station,
                                quantizer.SevenLevel(input.GetProperty("x").GetInt32(), input.GetProperty("z").GetInt32()),
                                (SourceButtons)input.GetProperty("held").GetInt32());
                        }
                        cabinet.AdvanceSamples(entry + tick, station => current[station]);
                        flipState.Add(cabinet.Match.OffenseFlipped == true ? 1 : 0);
                        if (done < 0 && cabinet.Game.State.Phase != GamePhase.PlaySelect) done = tick;
                    }
                    int[] sides = item.GetProperty("sides").EnumerateArray().Select(s => s.GetInt32()).ToArray();
                    var draws = random.Trace.Where(d => d.Site.SourceTick > entry &&
                        (d.Site.SourceIdentity.StartsWith("playsel.c:play_select_cntrl:", StringComparison.Ordinal) ||
                        d.Site.SourceIdentity.StartsWith("SourceMatchFlow.Cpu", StringComparison.Ordinal))).ToArray();
                    foreach (SourceRandomDraw draw in draws)
                        Check(draw.Site.Process == "game_proc" && draw.Site.ActorId == -1 && draw.Raw == raw,
                            "random gesture consumes the real Core menu process's shared unfiltered raw stream");
                    for (int i = 1; i < draws.Length; i++)
                        Check(draws[i].Order > draws[i - 1].Order &&
                            (draws[i].Site.SourceTick != draws[i - 1].Site.SourceTick ||
                            draws[i].Site.CallIndex == draws[i - 1].Site.CallIndex + 1),
                            "random menu requests retain exactly-once shared draw/call order");
                    var normalized = new {
                        draws = draws.Select(d => new object[] { d.Site.SourceTick - entry,
                            d.Site.SourceIdentity.Contains("/side") ? d.Site.SourceIdentity.EndsWith("side0", StringComparison.Ordinal) ? 0 : 1 : -1,
                            d.Site.BoundOrModulus, d.Raw.Value, d.Value }).ToArray(),
                        sounds = cabinet.Events.Where(e => e.SoundCall.HasValue && sides.Contains((int)e.Side))
                            .Select(e => new object[] { e.SourceTick - entry, (int)e.Side, e.SoundCall.Value,
                                e.SoundVolume.Value, e.SoundPan.Value, e.SoundPriority.Value }).ToArray(),
                        updates = cabinet.Events.Where(e => e.Kind == SourceCabinetMenuEventKind.PageChanged ||
                            e.Kind == SourceCabinetMenuEventKind.Flipped).Select(e => new object[] {
                                e.SourceTick - entry, (int)e.Side, e.PageIndex,
                                e.Side == cabinet.Match.Possession.Offense && e.OffenseFlipped ? 1 : 0, e.SlotIndex }).ToArray(),
                        choices = cabinet.Selections.Where(e => !e.Selection.Pool.HasValue)
                            .Select(e => new object[] { e.SourceTick - entry, (int)e.Team.Value, e.Selection.PageIndex,
                                e.Selection.SlotIndex, e.Selection.PlaySymbol }).ToArray(),
                        cpus = cabinet.Selections.Where(e => e.Selection.Pool.HasValue)
                            .Select(e => new object[] { e.SourceTick - entry, (int)e.Team.Value, e.Selection.PlaySymbol }).ToArray(),
                        callerDone = done, offenseFlipped = cabinet.Match.OffenseFlipped == true ? 1 : 0, flipState
                    };
                    File.WriteAllText(Path.Combine(output, name + ".random.json"), JsonSerializer.Serialize(normalized));
                    File.WriteAllText(Path.Combine(output, name + ".raw.json"), JsonSerializer.Serialize(random.Trace));
                    File.WriteAllText(Path.Combine(output, name + ".cabinet.json"), JsonSerializer.Serialize(new {
                        cabinet.Trace, cabinet.Events, cabinet.Selections, homePage = cabinet.Selector.Page(TeamId.Home),
                        homeSlot = cabinet.Selector.Cursor(TeamId.Home), awayPage = cabinet.Selector.Page(TeamId.Away),
                        awaySlot = cabinet.Selector.Cursor(TeamId.Away), cabinet.Selector.Flipped }));
                    randomCases.Add(name);
                    EndCase(name);
                }
            File.WriteAllText(Path.Combine(output, "random-boundaries.json"), JsonSerializer.Serialize(boundaries));
        }

    private static Cabinet StartRandomCabinet(string output, string name,
        Func<SourcePlaySelection, bool, SourceCommand> command = null)
    {
        BeginCase(name);
        var cabinet = new Cabinet(selectionCommand: command);
        long entry = cabinet.OpenMenu();
        cabinet.Advance(entry + 21);
        cabinet.Advance(entry + 22, x: -3);
        cabinet.Advance(entry + 23);
        cabinet.Advance(entry + 24, SourceButtons.C, x: -3);
        cabinet.Advance(entry + 27);
        Check(cabinet.Match.HomeRandomSelection.IsSpecified && !cabinet.Selector.Selected(TeamId.Home),
            "actual random controller remains live after its first admitted draw pair");
        File.WriteAllText(Path.Combine(output, name + ".entry.json"), JsonSerializer.Serialize(new { entry }));
        return cabinet;
    }

    private static int RandomDrawCount(Cabinet cabinet)
    {
        return ((SourceInjectedRandom)cabinet.Game.SourceRandomService).Trace.Count(d =>
            d.Site.SourceIdentity.StartsWith("playsel.c:play_select_cntrl:", StringComparison.Ordinal));
    }

    private static void RandomLifecycle(string output, string[] selectedCases = null)
    {
        if (selectedCases == null || selectedCases.Contains("random-pause-render"))
        {
            var cabinet = StartRandomCabinet(output, "random-pause-render");
            long paused = cabinet.Game.SourceTick;
            int count = RandomDrawCount(cabinet), slot = cabinet.Selector.Cursor(TeamId.Home),
                page = cabinet.Selector.Page(TeamId.Home);
            var composer = new Blitz.Presentation.SourceHudComposer(hud);
            for (int render = 0; render < 25; render++)
                composer.Compose(cabinet.Game.State, new Blitz.Presentation.SourceHudViewport(1024, 768, 1024, 768, 1),
                    cabinet.Selector, false, false);
            cabinet.Bridge.SetActive(false);
            for (int scan = 0; scan < 25; scan++)
                Check(cabinet.Bridge.AdvanceSource(.1, cabinet.Input.NextHostFrame) == 0,
                    "inactive bridge cannot admit a random menu consumer wake");
            Check(cabinet.Game.SourceTick == paused && RandomDrawCount(cabinet) == count &&
                cabinet.Selector.Cursor(TeamId.Home) == slot && cabinet.Selector.Page(TeamId.Home) == page,
                "input scanning, rendering and paused host time consume no random draws or menu progress");
            cabinet.Input.Clear(); cabinet.Selector.ClearPendingInput(); cabinet.Bridge.SetActive(true);
            cabinet.Advance(paused + 42);
            Check(cabinet.Selector.Selected(TeamId.Home) && RandomDrawCount(cabinet) == 30,
                "resume completes precisely fifteen admitted random pairs without host-time catchup");
            randomLifecycleCases.Add("random-pause-render");
            EndCase("random-pause-render");
        }

        if (selectedCases == null || selectedCases.Contains("random-restart"))
        {
            var cabinet = StartRandomCabinet(output, "random-restart");
            cabinet.Bridge.Restart();
            Check(!cabinet.Match.HomeRandomSelection.IsSpecified && RandomDrawCount(cabinet) == 0,
                "restart clears the actual random controller and shared RNG epoch");
            long entry = cabinet.OpenMenu();
            cabinet.Advance(entry + 1);
            Check(!cabinet.Match.HomeRandomSelection.IsSpecified && RandomDrawCount(cabinet) == 0,
                "new menu cannot inherit a prior random continuation without its public gesture");
            cabinet.Advance(entry + 80);
            Check(RandomDrawCount(cabinet) == 0 && !cabinet.Selector.Selected(TeamId.Home),
                "an untouched new menu never draws or silently auto-selects from an old random controller");
            randomLifecycleCases.Add("random-restart");
            EndCase("random-restart");
        }

        foreach (bool outputFailure in new[] { false, true })
        {
            string name = outputFailure ? "random-output-failure" : "random-dependency-failure";
            if (selectedCases != null && !selectedCases.Contains(name)) continue;
            var cabinet = StartRandomCabinet(output, name,
                outputFailure ? null : (Func<SourcePlaySelection, bool, SourceCommand>)((play, flip) =>
                { throw new Blitz.AssetImport.SourceInterfaceException("actual random play mapping unavailable"); }));
            if (outputFailure)
                cabinet.Selector.EventRaised += value => {
                    if (value.IsRandomSelection && value.Kind == SourceCabinetMenuEventKind.Selected)
                        throw new InvalidOperationException("actual random selected output unavailable");
                };
            bool failed = false;
            try { cabinet.Advance(cabinet.Game.SourceTick + 42); }
            catch (Exception error)
            {
                failed = outputFailure ? error is InvalidOperationException &&
                    error.Message == "actual random selected output unavailable" :
                    error is Blitz.AssetImport.SourceInterfaceException && error.Message == "actual random play mapping unavailable";
            }
            Check(failed && cabinet.Selections.Count == 0 && RandomDrawCount(cabinet) == 30,
                "random dependency/output failure propagates after the original final pair without a substitute play");
            int count = RandomDrawCount(cabinet);
            failed = false;
            try { cabinet.Advance(cabinet.Game.SourceTick + 1); }
            catch (InvalidOperationException error) { failed = error.Message.Contains("Restart is required"); }
            Check(failed && RandomDrawCount(cabinet) == count,
                "failed random selection is fail-stop; no retry draws, CPU fallback or selected-side overwrite");
            randomLifecycleCases.Add(outputFailure ? "random-output-failure" : "random-dependency-failure");
            EndCase(name);
        }
        File.WriteAllText(Path.Combine(output, "random-lifecycle.json"),
            JsonSerializer.Serialize(new { cases = randomLifecycleCases, assertions }));
    }
}
