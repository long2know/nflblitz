using System.Text.Json;
using System.Reflection;
using Blitz.AssetImport;
using Blitz.Contracts;
using Blitz.Gameplay;
using Blitz.Presentation;
using Blitz.Runtime;
using Blitz.SourcePlayer;

static partial class Checks
{
    private static PlayerHost CabinetHost(PlayerData data, out RecordingSink sink)
    {
        sink = new RecordingSink();
        return new PlayerHost(data, sink, options: PreviewOptions.FromUrl("?source-gameflow=portable", data.Catalog.Profile.StableId));
    }

    private static void CabinetRender(PlayerHost host, int steps = 0) =>
        host.Advance(FixedStepBridge.StepSeconds * steps, 512, 384, 512, 384, 1);

    private static void CabinetRaw(PlayerHost host, int[]? buttons = null, double[]? axes = null) =>
        host.Capture(axes ?? new double[8], buttons ?? new int[4]);

    private static void CabinetTap(PlayerHost host, int button, params int[] stations)
    {
        CabinetRaw(host);
        var buttons = new int[4];
        foreach (int station in stations) buttons[station] = button;
        CabinetRaw(host, buttons);
        CabinetRaw(host);
        CabinetRender(host);
    }

    private static void CabinetMove(PlayerHost host, int station, int x, int y)
    {
        CabinetRaw(host);
        var axes = new double[8]; axes[station * 2] = x; axes[station * 2 + 1] = y;
        CabinetRaw(host, axes: axes);
        CabinetRaw(host);
        CabinetRender(host);
    }

    private static void CabinetStart(PlayerHost host, params int[] extraStations)
    {
        if (extraStations.Length != 0) CabinetTap(host, 1, extraStations);
        CabinetTap(host, 4, 0);
        int[] captains = extraStations.Any(id => id >= 2) ? new[] { 0, 2 } : new[] { 0 };
        CabinetTap(host, 1, captains);
        CabinetTap(host, 1, captains);
        CabinetTap(host, 1, 0);
        Check(host.Session.Playing && host.Session.MatchesStarted > 0 && host.Game.SourceTick == 0,
            "Public cabinet choices must construct the actual chosen match before its first source wake.");
        CabinetRaw(host);
    }

    private static string CabinetText(RecordingSink sink, string element) =>
        sink.State.GetProperty("hud").GetProperty("text").EnumerateArray()
            .Where(item => item.GetProperty("Element").GetString() == element)
            .Select(item => item.GetProperty("text").GetString()!).Single();

    private static void CabinetToStage(PlayerHost host, SourceCabinetFlowStage stage, int held = 7, int maximum = 2200)
    {
        for (int wake = 0; wake < maximum && CabinetFlow(host).Stage != stage; wake++)
        {
            CabinetRaw(host, new[] { held, 0, 0, 0 });
            CabinetRender(host, 1);
        }
        Check(CabinetFlow(host).Stage == stage,
            "Actual cabinet host did not reach " + stage + "; got " + CabinetFlow(host).Stage);
        CabinetRender(host);
    }

    private static void CabinetNextOptions(PlayerHost host)
    {
        CabinetToStage(host, SourceCabinetFlowStage.FinalConfirmation);
        CabinetRaw(host);
        CabinetRender(host, 3);
        host.Continue(0);
        CabinetRaw(host);
        for (int tick = 0; tick < 4 && host.Session.Playing; tick++) CabinetRender(host, 1);
        Check(host.Session.Stage == SourceCabinetMenuStage.NextMatch && host.Game.State.Phase == GamePhase.GameOver,
            "Public terminal Continue must reach next-match choices without erasing the retained final football state.");
    }

    private static void VerifyCabinetSession(PlayerData data)
    {
        int before = assertions;
        string profile = data.Catalog.Profile.StableId;
        foreach (SourceRosterTeam team in data.Rosters.TeamData)
        {
            Check(SourceHudText.Width(data.Hud.Font("bast18"), team.Name.ToUpperInvariant()) > 0 &&
                SourceHudText.Width(data.Hud.Font("bast13"), team.Home.ToUpperInvariant()) > 0,
                "Every actual selectable team needs original-font glyphs, not an unvisited menu-time fallback.");
            foreach (SourceRosterPlayer player in team.Players)
                Check(SourceHudText.Width(data.Hud.Font("bast8t"),
                    player.Profile.FirstName.ToUpperInvariant() + " " + player.Profile.LastName.ToUpperInvariant()) > 0,
                    "Every selectable original roster record needs its genuine original-font name glyphs.");
        }
        for (int tip = 0; tip < 30; tip++)
        {
            var lines = SourceCabinetCoachingText.Lines(tip);
            Check(SourceHudText.Width(data.Hud.Font("bast18"), lines[0]) > 0 &&
                lines.Skip(1).All(line => SourceHudText.Width(data.Hud.Font("bast10"), line) > 0),
                "All source-selected coaching ordinals must fit the admitted original glyph domain.");
        }
        Check(!PreviewOptions.FromUrl("", profile).GameflowEnabled &&
            !PreviewOptions.FromUrl("#?source-gameflow=portable", profile).GameflowEnabled,
            "Old and fragment-only routes must not inherit the portable cabinet flight.");
        foreach (string url in new[] { "?source-gameflow=", "?source-gameflow=native", "?source-gameflow=portable&source-gameflow=portable",
            "?source-gameflow=portable&source-compatibility=strict", "?source-gameflow=portable=extra" })
        {
            bool rejected = false;
            try { PreviewOptions.FromUrl(url, profile); } catch (InvalidDataException) { rejected = true; }
            Check(rejected, "Malformed/strict cabinet options must reject instead of enabling a different composition: " + url);
        }
        var host = CabinetHost(data, out var sink);
        SourceMatchConfiguration approved = data.Configuration.Match;
        GameSimulation backdrop = host.Game;
        CabinetRender(host);
        Check(host.CabinetInputStations.SequenceEqual(new[] { 0, 1, 2, 3 }) &&
            host.Session.Stage == SourceCabinetMenuStage.Joining && host.Game.SourceTick == 0 &&
            host.Game.SourceCustomPlaySession == null &&
            CabinetFlow(host).Configuration.ParticipatingStationMask == 1 &&
            CabinetText(sink, "cabinet/title") == "FREE PLAY - JOIN CABINET",
            "The opted-in actual World must present its original-atlas cabinet menu and expose four physical join inputs before wake1.");
        CabinetRender(host, 60);
        Check(host.Game.SourceTick == 0 && host.SourceWakes == 0,
            "Menu wall time cannot silently play the default match behind the choices.");
        CabinetRaw(host, new[] { 0, 1, 1, 1 });
        CabinetRaw(host);
        Check(host.Session.ParticipatingStationMask == 15, "Simultaneous public P2/P3/P4 joins must all survive the same sample.");
        CabinetTap(host, 2, 1);
        Check(host.Session.ParticipatingStationMask == 13, "B leaves exactly its physical cabinet station.");
        CabinetTap(host, 1, 1);
        CabinetRaw(host, new[] { 4, 0, 0, 0 });
        CabinetRaw(host, new[] { 1, 0, 1, 0 });
        Check(host.Session.Stage == SourceCabinetMenuStage.Teams && host.Session.WaitingForNeutral,
            "Held-through transition buttons must not also confirm the next menu.");
        CabinetRaw(host);
        CabinetMove(host, 0, -1, 0);
        CabinetMove(host, 2, 1, 0);
        Check(((JsonElement)JsonSerializer.SerializeToElement(host.Session.Describe())).GetProperty("homeTeam").GetInt32() == 30,
            "Team navigation must wrap across all31 actual teams, not the two original defaults.");
        CabinetTap(host, 1, 0, 2);
        Check(host.Session.Stage == SourceCabinetMenuStage.Rosters, "Both source-side captains confirm the team choices once.");
        CabinetMove(host, 0, -1, 0);
        CabinetMove(host, 2, 1, 0);
        CabinetTap(host, 1, 0, 2);
        Check(host.Session.Stage == SourceCabinetMenuStage.Versus, "Seven-slot roster choices must lead to the explicit versus configuration.");
        CabinetMove(host, 0, 1, 0);
        CabinetMove(host, 0, 0, 1);
        CabinetMove(host, 0, 1, 0);
        CabinetTap(host, 1, 0);
        Check(!ReferenceEquals(backdrop, host.Game) && host.Session.MatchesStarted == 1 && host.Epoch == 1 &&
            host.MatchConfiguration.HomeTeam.TeamIndex == 30 && host.MatchConfiguration.AwayTeam.TeamIndex == 2 &&
            host.MatchConfiguration.HomeTeam.RosterIndices[0] == 15 && host.MatchConfiguration.AwayTeam.RosterIndices[0] == 1 &&
            host.MatchConfiguration.FirstPossession == TeamId.Away && host.MatchConfiguration.Captain == SourceCaptainSlot.Second &&
            host.MatchConfiguration.Stations.Select(s => s.StationId).SequenceEqual(new[] { 0, 1, 2, 3 }) &&
            host.MatchConfiguration.Stations.All(s => s.Team == (TeamId)(s.StationId / 2)) &&
            ReferenceEquals(approved, data.Configuration.Match) && approved.HomeTeam.TeamIndex == 0 &&
            approved.Stations.Count == 1 && approved.HomeTeam.RosterIndices[0] == 0,
            "Only a new immutable factory configuration may change teams/records/humans; authenticated settings and physical sides remain unchanged.");
        var customSession = host.Game.SourceCustomPlaySession ??
            throw new InvalidOperationException("The selected new-player factory did not bind its explicit known-none inventory.");
        Check(ReferenceEquals(customSession.Configuration, host.MatchConfiguration) &&
            host.MatchConfiguration.Stations.All(s => customSession.InventoryFor(s.StationId) == SourceCustomPlayInventoryState.KnownNone),
            "Deliberate new free-play players bind explicit known-none inventories before wake1; absent saved data is not guessed empty on the backdrop/base route.");
        CabinetRender(host);
        var actors = sink.State.GetProperty("actors").EnumerateArray().ToArray();
        Check(actors[0].GetProperty("rosterTeam").GetInt32() == 30 && actors[0].GetProperty("rosterRecord").GetInt32() == 15 &&
            actors[7].GetProperty("rosterTeam").GetInt32() == 2 && actors[7].GetProperty("rosterRecord").GetInt32() == 1 &&
            actors.All(actor => actor.GetProperty("tint").GetString() == "none") &&
            actors[0].GetProperty("uniformHash").GetString() != actors[7].GetProperty("uniformHash").GetString(),
            "Actual selected original roster/uniform bindings must reach the fourteen-actor render packet without tint substitutes.");
        CabinetRaw(host, new[] { 1, 2, 4, 3 });
        CabinetRender(host, 3);
        Check(sink.State.GetProperty("input").GetProperty("waitingForNeutral").GetBoolean(),
            "Held menu buttons cannot leak through the fresh-match neutral latch.");
        CabinetRaw(host);
        var observed = new List<SourceInputFrame>();
        long observedTick = host.Game.SourceTick;
        host.Bridge.AfterSimulationStep += () => {
            if (host.Game.SourceTick == observedTick) return;
            observedTick = host.Game.SourceTick;
            var actual = (PlayerCommand)FlowGet(FlowModel(host.Game), "SourceCurrentInput");
            if (actual.StationInputs != null) observed.Add(actual.StationInputs);
        };
        CabinetRaw(host, new[] { 1, 2, 4, 3 }, new[] { 1d, 0d, -1d, 0d, 0d, 1d, 0d, -1d });
        CabinetRaw(host);
        CabinetRender(host, 4);
        Check(observed.Any(frame => frame.Stations.Count == 4 && frame.Stations.Select(s => (int)s.Pressed).SequenceEqual(new[] { 1, 2, 4, 3 })) &&
            observed.Any(frame => frame.Stations.Count == 4 && frame.Stations.Select(s => (int)s.Released).SequenceEqual(new[] { 1, 2, 4, 3 })),
            "The real source consumer must see simultaneous four-station press AND release edges captured between rendered frames.");
        host.SetActive(false);
        CabinetRaw(host, new[] { 7, 7, 7, 7 });
        long paused = host.Game.SourceTick;
        CabinetRender(host, 8);
        host.SetActive(true);
        CabinetRaw(host, new[] { 7, 7, 7, 7 });
        CabinetRender(host, 3);
        Check(host.Game.SourceTick <= paused + 2 && sink.State.GetProperty("input").GetProperty("waitingForNeutral").GetBoolean(),
            "Focus freeze/discard and a truly released raw sample are required before resumed held controls can participate.");

        VerifyCabinetScreensAndRematch(data);
        VerifyCarrierlessCabinetCaller(data);
        Console.WriteLine(JsonSerializer.Serialize(new { status = "passed", assertions = assertions - before,
            contract = "source-gameflow/v1 + portable-free-play-new-player-session/v1",
            boundary = "Actual PlayerHost/public raw input and rendered original-atlas packets; declared terminal/clock/statistics fixtures are edge tests, not natural full-match proof." }));
    }

    private static void VerifyCabinetScreensAndRematch(PlayerData data)
    {
        var host = CabinetHost(data, out var sink);
        CabinetStart(host);
        FlowWakes(host, 2);
        object model = FlowModel(host.Game);
        void Counters(string field, int home, int away)
        {
            var values = (int[])FlowGet(model, field); values[0] = home; values[1] = away;
        }
        Counters("SourceInterceptions", 2, 5);
        Counters("SourcePassCompletions", 3, 7);
        Counters("SourcePassingYards", 42, 84);
        Counters("SourceSacks", 1, 2);
        Counters("SourcePassAttempts", 13, 29);
        Counters("SourceFumbles", 7, 11);
        Counters("SourceLostFumbles", 2, 3);
        Counters("SourceTackles", 0, 5);
        FlowSet(model, "SourceTimeOfPossession", new SourceTimeOfPossessionView(67, 123));
        FlowEnd(host, 3, 4, 17, clock: 0);
        CabinetRender(host);
        Check(CabinetText(sink, "cabinet/title") == "PLAY RESULT" &&
            sink.State.GetProperty("hud").GetProperty("elements").EnumerateArray().Any(e => e.GetString() == "cabinet/result/plate"),
            "A real committed C2 result must compose the original result plate, not a generic phase label.");
        long tick = host.Game.SourceTick;
        int elapsed = CabinetFlow(host).ElapsedSourceWakes, random = ((SourceInjectedRandom)host.Game.SourceRandomService).Trace.Count;
        for (int redraw = 0; redraw < 12; redraw++) CabinetRender(host);
        Check(host.Game.SourceTick == tick && CabinetFlow(host).ElapsedSourceWakes == elapsed &&
            ((SourceInjectedRandom)host.Game.SourceRandomService).Trace.Count == random,
            "Repeated actual render packets must not advance waits, inputs, RNG or statistics.");
        CabinetToStage(host, SourceCabinetFlowStage.EndOfQuarter);
        Check(CabinetText(sink, "cabinet/winner/team") == data.Hud.TeamAbbreviations[1],
            "The actual EOQ winner is Away from scores4:17, not the zero MatchEnded event value.");
        CabinetToStage(host, SourceCabinetFlowStage.FinalStats);
        Check(CabinetText(sink, "cabinet/title") == "FINAL STATISTICS" &&
            CabinetText(sink, "cabinet/stats/home/score") == "4" && CabinetText(sink, "cabinet/stats/away/score") == "17" &&
            CabinetText(sink, "cabinet/stats/outcome") == data.Hud.TeamAbbreviations[1] + " WINS!" &&
            CabinetText(sink, "cabinet/stats/interceptions/home") == "2" && CabinetText(sink, "cabinet/stats/interceptions/away") == "5" &&
            CabinetText(sink, "cabinet/stats/passing/home") == "42/14" && CabinetText(sink, "cabinet/stats/passing/away") == "84/12" &&
            CabinetText(sink, "cabinet/stats/field-goals/home") == "0/0" &&
            CabinetText(sink, "cabinet/stats/quarter/0/home") == "0" && CabinetText(sink, "cabinet/stats/quarter/3/away") == "17" &&
            CabinetText(sink, "cabinet/stats/quarter/4/away") == "--",
            "Actual final HUD glyph packets must be side-correct, preserve producer values/averages, completed quarter zeroes, and unavailable versus zero.");
        Check(CabinetText(sink, "cabinet/stats/passes/home") == "13/3" &&
            CabinetText(sink, "cabinet/stats/passes/away") == "29/7" &&
            CabinetText(sink, "cabinet/stats/fumbles/home") == "7/2" &&
            CabinetText(sink, "cabinet/stats/fumbles/away") == "11/3" &&
            CabinetText(sink, "cabinet/stats/turnovers/home") == "5/2" &&
            CabinetText(sink, "cabinet/stats/turnovers/away") == "2/3" &&
            CabinetText(sink, "cabinet/stats/tackles-sacks/home") == "0/1" &&
            CabinetText(sink, "cabinet/stats/tackles-sacks/away") == "5/3",
            "Available activity producers must render asymmetric attempts/completions, fumbles/lost, opposite interceptions/own lost fumbles, and human-only tackles independently of source sacks.");
        var composer = new SourceHudComposer(data.Hud);
        var glyphs = composer.Compose(host.HudProcess.CommittedState, new SourceHudViewport(512, 384, 512, 384, 1), null!, false, false);
        Check(glyphs.Where(p => p.Element == "cabinet/stats/interceptions/home").Single().SourceImageSymbol ==
            data.Hud.Font("bast8t").Glyph('2').Id &&
            glyphs.Where(p => p.Element == "cabinet/stats/interceptions/away").Single().SourceImageSymbol ==
            data.Hud.Font("bast8t").Glyph('5').Id &&
            sink.HudTextures.All(t => t.StartsWith("hud/") && sink.Textures.ContainsKey(t)),
            "The side-correct count must select the actual original atlas glyph, not only be reported in metadata.");
        host.Advance(0, 360, 270, 360, 270, 1);
        SourceCabinetMatchRecord final = host.Session.LastResult;
        Check(final.Statistics.HomeScore == 4 && final.Statistics.AwayScore == 17 && host.Session.MatchesCompleted == 1,
            "Session records retain the real immutable final statistics exactly once.");
        SourceMatchView finalMatch = final.Statistics.Match;
        var legacyMatch = new SourceMatchView(finalMatch.TouchdownStats, finalMatch.FourthDownAttempts,
            finalMatch.ResultProgressionStats, finalMatch.PlaceKickStats, finalMatch.PlayEndStats,
            finalMatch.CompletedPassStats, finalMatch.PassCompletions, finalMatch.Interceptions,
            finalMatch.TimeOfPossession, finalMatch.Configuration, finalMatch.PlayKind, finalMatch.Phase,
            finalMatch.Period, finalMatch.Possession, finalMatch.QuarterScores, finalMatch.Stations, finalMatch.Result);
        var legacyProjection = new SourceCabinetStatistics(legacyMatch, 4, 17, 3);
        Check(legacyProjection.Rows.Single(row => row.Key == "passes").Home == "--/3" &&
            legacyProjection.Rows.Single(row => row.Key == "passes").Away == "--/7" &&
            legacyProjection.Rows.Single(row => row.Key == "fumbles").Home == "--/--" &&
            legacyProjection.Rows.Single(row => row.Key == "fumbles").Away == "--/--" &&
            legacyProjection.Rows.Single(row => row.Key == "turnovers").Home == "5/--" &&
            legacyProjection.Rows.Single(row => row.Key == "turnovers").Away == "2/--" &&
            legacyProjection.Rows.Single(row => row.Key == "tackles-sacks").Home == "--/1" &&
            legacyProjection.Rows.Single(row => row.Key == "tackles-sacks").Away == "--/3",
            "Old-constructor absent activity groups must stay unavailable while independently present completions, opposite interceptions and sacks still display.");
        CabinetNextOptions(host);
        long epoch = host.Epoch;
        GameSimulation game = host.Game;
        CabinetRaw(host, new[] { 1, 0, 0, 0 });
        CabinetRender(host, 8);
        Check(host.Epoch == epoch && host.Session.Stage == SourceCabinetMenuStage.NextMatch,
            "Held-through terminal controls cannot accidentally select a rematch.");
        CabinetTap(host, 1, 0);
        CabinetRender(host);
        CabinetRender(host);
        Check(ReferenceEquals(game, host.Game) && host.Epoch == epoch + 1 && host.Game.SourceTick == 0 &&
            host.Game.State.HomeScore == 0 && host.Game.State.AwayScore == 0 && host.SourceWakes == 0 &&
            host.Camera.CueCount == 0 && host.HudProcess.PendingResultWakes == 0 &&
            sink.State.GetProperty("events").GetArrayLength() == 0 &&
            sink.State.GetProperty("input").GetProperty("pending").GetInt32() == 0 &&
            sink.State.GetProperty("input").GetProperty("waitingForNeutral").GetBoolean() &&
            ReferenceEquals(final, host.Session.LastResult) && final.Statistics.AwayScore == 17 && host.Session.MatchesCompleted == 1,
            "A deliberate public rematch must execute the existing once-only reset and retain only deliberate choices/final records.");
        var freshProjection = new SourceCabinetStatistics(((ISourceMatchState)host.Game.State).Match, 0, 0, 0);
        Check(new[] { "passes", "fumbles", "turnovers", "tackles-sacks" }.All(key =>
                freshProjection.Rows.Single(row => row.Key == key).Home == "0/0" &&
                freshProjection.Rows.Single(row => row.Key == key).Away == "0/0") &&
            final.Statistics.Match.PlayActivityStats.HomePassAttempts == 13 &&
            final.Statistics.Match.PlayActivityStats.AwayFumbles == 11 &&
            final.Statistics.Match.PlayActivityStats.HomeTackles == 0 &&
            final.Statistics.Rows.Single(row => row.Key == "passes").Home == "13/3" &&
            final.Statistics.Rows.Single(row => row.Key == "turnovers").Home == "5/2",
            "Fresh actual groups display observed zeroes after rematch, while the retained immutable final projection keeps its old activity values.");
        CabinetRaw(host);
        FlowEnd(host, 3, 21, 9, clock: 0);
        CabinetNextOptions(host);
        CabinetTap(host, 2, 0);
        Check(host.Session.Stage == SourceCabinetMenuStage.Joining, "B deliberately returns to public participation/team choices, not a reload.");
        CabinetTap(host, 1, 2);
        CabinetTap(host, 4, 0);
        CabinetMove(host, 0, 1, 0);
        CabinetTap(host, 1, 0, 2);
        CabinetTap(host, 1, 0, 2);
        CabinetTap(host, 1, 0);
        Check(!ReferenceEquals(game, host.Game) && host.MatchConfiguration.HomeTeam.TeamIndex == 1 &&
            host.MatchConfiguration.Stations.Select(s => s.StationId).SequenceEqual(new[] { 0, 2 }) &&
            host.Epoch == epoch + 2 && host.Game.SourceTick == 0 &&
            host.Session.MatchesStarted == 3 && host.Session.MatchesCompleted == 2 &&
            final.Statistics.HomeScore == 4 && final.Statistics.AwayScore == 17,
            "Changed teams/humans require a fresh actual simulation and epoch, not mutable authenticated data or reuse of old source queues.");

        var half = CabinetHost(data, out var halfSink);
        CabinetStart(half);
        FlowEnd(half, 1, 3, 7, clock: 0);
        CabinetToStage(half, SourceCabinetFlowStage.EndOfQuarter);
        Check(halfSink.State.GetProperty("hud").GetProperty("elements").EnumerateArray().Any(e => e.GetString() == "cabinet/quarter/end"),
            "The second-quarter announcement must use admitted original msg_end/quarter imagery.");
        CabinetToStage(half, SourceCabinetFlowStage.HalftimeStats);
        Check(CabinetText(halfSink, "cabinet/title") == "HALFTIME STATISTICS" &&
            CabinetText(halfSink, "cabinet/stats/quarter/1/away") == "7" &&
            CabinetText(halfSink, "cabinet/stats/quarter/2/away") == "--" && half.Session.MatchesCompleted == 0,
            "Halftime must show actual completed quarter stats without recording a final match or inventing future quarter zeroes.");
        CabinetToStage(half, SourceCabinetFlowStage.ContinuationOffer, held: 0);
        Check(CabinetText(halfSink, "cabinet/title") == "CONTINUE GAME" &&
            CabinetText(halfSink, "cabinet/continuation/policy") == "FREE PLAY - SAME HUMAN STATIONS",
            "The actual source continuation offer needs its own rendered free-play screen, not an unvisited DTO.");
        CabinetToStage(half, SourceCabinetFlowStage.Inactive);
        Check(((ISourceMatchState)half.Game.State).Match.Period.QuarterIndex == 2 &&
            half.Game.State.Phase == GamePhase.PreSnap,
            "The rendered halftime offer must resume the actual third-quarter kickoff once.");
        var coach = CabinetHost(data, out var coachSink);
        CabinetStart(coach);
        FlowEnd(coach, 0, 0, 0, clock: 0);
        CabinetToStage(coach, SourceCabinetFlowStage.CoachingTip);
        SourceCabinetFlowView flow = CabinetFlow(coach);
        Check(CabinetText(coachSink, "cabinet/coaching/title") == SourceCabinetCoachingText.Lines(flow.CoachingTipOrdinal!.Value)[0],
            "The coaching screen must render the already-drawn source ordinal's actual hint text.");
        random = ((SourceInjectedRandom)coach.Game.SourceRandomService).Trace.Count;
        for (int redraw = 0; redraw < 8; redraw++) CabinetRender(coach);
        Check(((SourceInjectedRandom)coach.Game.SourceRandomService).Trace.Count == random &&
            CabinetFlow(coach).CoachingTipOrdinal == flow.CoachingTipOrdinal,
            "Coaching redraw cannot reselect either original random ordinal.");
        var tie = CabinetHost(data, out var tieSink);
        CabinetStart(tie);
        FlowEnd(tie, 6, 0, 0, clock: 0);
        CabinetToStage(tie, SourceCabinetFlowStage.FinalStats);
        Check(CabinetText(tieSink, "cabinet/stats/outcome") == "TIE GAME!" &&
            CabinetText(tieSink, "cabinet/stats/quarter/6/home") == "0" &&
            CabinetText(tieSink, "cabinet/stats/quarter/6/away") == "0",
            "The declared tied third-OT edge must render a tie and all seven actual period columns, not award Home from an event value.");
        SourceMatchView actual = tie.HudProcess.CommittedHud.Statistics.Match;
        var absent = new SourceMatchView(actual.Configuration, actual.PlayKind, actual.Phase, actual.Period,
            actual.Possession, actual.QuarterScores, actual.Stations, actual.Result);
        var unavailable = new SourceCabinetStatistics(absent, 0, 0, 6);
        Check(unavailable.Rows.Single(row => row.Key == "possession").Home == "--" &&
            unavailable.Rows.Single(row => row.Key == "passes").Away == "--/--" &&
            unavailable.Rows.Single(row => row.Key == "field-goals").Home == "--/--",
            "An actually omitted optional statistical producer must remain unavailable, never be projected as a fabricated zero.");
    }

    private static void VerifyNaturalCabinetMatch(PlayerData data)
    {
        var host = CabinetHost(data, out var sink);
        CabinetStart(host);
        var stages = new HashSet<string>();
        var repairs = new HashSet<string>();
        bool observedUnfilteredWaitChord = false;
        host.Bridge.AfterHostStep += state => {
            stages.Add(((ISourceCabinetFlowState)state).CabinetFlow.Stage.ToString());
            var match = ((ISourceMatchState)state).Match;
            foreach (SourceCompatibilityRepair repair in match.CompatibilityRepairs)
            {
                repairs.Add(repair.Kind.ToString());
                if (repair.Kind != SourceCompatibilityRepairKind.CarrierlessDeadBallLateHit ||
                    repair.SourceTick != host.Game.SourceTick) continue;
                var input = (PlayerCommand)FlowGet(FlowModel(host.Game), "SourceCurrentInput");
                observedUnfilteredWaitChord |= input.StationInputs != null &&
                    input.StationInputs.Stations.Any(s => s.StationId == 0 &&
                        s.Held == (SourceButtons.A | SourceButtons.B | SourceButtons.C));
            }
        };
        int publicCaptures = 0;
        long lastPlay = -1;
        int pulse = 0;
        void DrivePublicPolicy()
        {
            SourceMatchView match = ((ISourceMatchState)host.Game.State).Match;
            var buttons = new int[4];
            if (host.Game.State.Phase == GamePhase.PlaySelect)
                buttons[0] = pulse++ % 2 == 0 ? 2 : 0;
            else if (host.Game.State.Phase == GamePhase.PreSnap && match.PlayKind != SourcePlayKind.Kickoff)
                buttons[0] = pulse++ % 2 == 0 ? 1 : 0;
            else if (CabinetFlow(host).Stage == SourceCabinetFlowStage.ResultWait ||
                CabinetFlow(host).Stage == SourceCabinetFlowStage.EndOfQuarter)
                buttons[0] = 7;
            CabinetRaw(host, buttons); publicCaptures++;
            CabinetRender(host, 8);
        }
        try
        {
            for (int frame = 0; frame < 45000 && host.Session.MatchesCompleted == 0; frame++)
            {
                DrivePublicPolicy();
                if (lastPlay != host.Game.State.PlayNumber)
                {
                    lastPlay = host.Game.State.PlayNumber;
                    Console.WriteLine("cabinet natural: " + JsonSerializer.Serialize(new {
                        hostTick = host.Game.State.Tick, sourceTick = host.Game.SourceTick, play = lastPlay,
                        quarter = ((ISourceMatchState)host.Game.State).Match.Period.QuarterIndex,
                        home = host.Game.State.HomeScore, away = host.Game.State.AwayScore
                    }));
                }
            }
            Check(host.Session.MatchesCompleted == 1 && host.Session.LastResult != null &&
                stages.Contains("EndOfQuarter") && stages.Contains("HalftimeStats"),
                "Natural public-host inputs must complete regulation/intermissions and retain real final team statistics.");
            Check(repairs.Contains(nameof(SourceCompatibilityRepairKind.CarrierlessDeadBallLateHit)) &&
                observedUnfilteredWaitChord,
                "The unchanged formerly-failing natural policy must keep its actual raw ABC chord and report the bound portable undefined-target repair.");
            SourceCabinetMatchRecord result = host.Session.LastResult ??
                throw new InvalidOperationException("The natural host did not retain its actual final statistics.");
            Check(CabinetText(sink, "cabinet/title") == "FINAL STATISTICS" &&
                CabinetText(sink, "cabinet/stats/home/score") == result.Statistics.HomeScore.ToString() &&
                CabinetText(sink, "cabinet/stats/away/score") == result.Statistics.AwayScore.ToString() &&
                result.Statistics.Match.QuarterScores.Sum(score => score.HomeScore) == result.Statistics.HomeScore &&
                result.Statistics.Match.QuarterScores.Sum(score => score.AwayScore) == result.Statistics.AwayScore,
                "The natural actual final packet must render real immutable scores and matching quarter totals, not merely emit MatchEnded.");
            CabinetNextOptions(host);
            CabinetTap(host, 1, 0);
            CabinetRaw(host);
            long secondNormalSnap = -1;
            for (int frame = 0; frame < 2000 && (secondNormalSnap < 0 || host.Game.SourceTick < secondNormalSnap + 30); frame++)
            {
                DrivePublicPolicy();
                if (secondNormalSnap < 0 && host.Game.State.PlayNumber >= 2 && host.Game.State.Phase == GamePhase.LiveBall &&
                    ((ISourceMatchState)host.Game.State).Match.PlayKind == SourcePlayKind.Normal)
                    secondNormalSnap = host.Game.SourceTick;
            }
            Check(host.Session.MatchesStarted == 2 && secondNormalSnap >= 0 &&
                host.Game.SourceTick >= secondNormalSnap + 30 && host.Game.State.Phase == GamePhase.LiveBall &&
                ReferenceEquals(result, host.Session.LastResult),
                "The same natural public policy must finish the second kickoff, select/snap a normal play and continue live gameplay without reload/private setters.");
            Console.WriteLine(JsonSerializer.Serialize(new { status = "passed", natural = true, publicCaptures, stages,
                repairs, observedUnfilteredWaitChord,
                finalHome = result.Statistics.HomeScore, finalAway = result.Statistics.AwayScore,
                completedQuarter = result.Statistics.CompletedQuarterIndex, finalSourceTick = result.FinalSourceTick,
                secondSourceTick = host.Game.SourceTick, secondNormalSnap,
                secondPlay = host.Game.State.PlayNumber, secondPhase = host.Game.State.Phase.ToString(),
                matchesStarted = host.Session.MatchesStarted,
                boundary = "Actual PlayerHost/raw cabinet controls only; no clock/score/seed/gameplay-state mutation." }));
        }
        catch
        {
            Console.Error.WriteLine("cabinet natural fault: " + JsonSerializer.Serialize(new {
                hostTick = host.Game.State.Tick, sourceTick = host.Game.SourceTick,
                phase = host.Game.State.Phase.ToString(), play = host.Game.State.PlayNumber,
                quarter = ((ISourceMatchState)host.Game.State).Match.Period.QuarterIndex,
                publicCaptures, stages
            }));
            throw;
        }
    }

    // Declared caller-boundary fixtures. The separate natural policy above exercises
    // the same caller through public Capture -> source wakes without state mutation.
    private static void VerifyCarrierlessCabinetCaller(PlayerData data)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        object Member(object instance, string name) =>
            instance.GetType().GetProperty(name, flags)?.GetValue(instance) ??
            instance.GetType().GetField(name, flags)!.GetValue(instance)!;
        void Mode(object actor, string layer, uint value)
        {
            object state = Member(Member(actor, "AnimationState"), layer);
            state.GetType().GetMethod("SetMovementMode", flags)!.Invoke(state, new object[] { value });
        }
        object Prepare(PlayerHost host, int buttons, GamePhase phase = GamePhase.DeadBall,
            int carrier = -1, bool runningPush = false, bool controlDisabled = false)
        {
            object model = FlowModel(host.Game);
            object actor = ((Array)FlowGet(model, "Players")).GetValue(0)!;
            FlowSet(model, "Phase", phase);
            FlowSet(model, "SourceMatchPhase", phase == GamePhase.LiveBall ? SourceMatchPhase.LiveBall : SourceMatchPhase.PlayResult);
            FlowSet(model, "SourceResultView", new SourceMatchResult(SourcePlayKind.Normal, SourceResultKind.Down, SourceResultStatus.Classified));
            FlowSet(model, "SourceRules", new SourceRulesState(1, 1, 2, 50, 80, SourcePlayType.Normal));
            FlowSet(model, "Possession", TeamId.Away);
            FlowSet(model, "SourceOffensePlay", data.Playbook.GetPlay("play_zigzag"));
            FlowSet(model, "SourceDefensePlay", data.Playbook.GetPlay(data.Hud.PlaySlots["def_slots"][0].PlaySymbol));
            FlowSet(model, "SourceFlags", SourceGameFlags.None);
            FlowSet(model, "CarrierId", carrier);
            FlowSet(model, "BallKind", phase == GamePhase.LiveBall ? SourceBallKind.Fumble : SourceBallKind.Pass);
            FlowSet(model, "SourceBallTarget", new SourceVector3(SourceMatchRules.WorldPosition(75, 1), 90, 90));
            FlowSet(actor, "SourceMode", SourceActorMode.DefensiveBack);
            FlowSet(actor, "SourceControllerStation", 0);
            FlowSet(actor, "SourceRawFlags", 0);
            FlowSet(actor, "SourceStick", 24);
            FlowSet(actor, "SourcePreviousStick", 24);
            FlowSet(actor, "SourceButtons", (SourceDroneButtons)buttons);
            FlowSet(actor, "SourceOldButtons", SourceDroneButtons.None);
            FlowSet(actor, "SourceNewButtons", (SourceDroneButtons)buttons);
            FlowSet(actor, "NoCatchTicks", 100);
            Mode(actor, "Primary", controlDisabled ? (uint)SourceMovementModes.Uninterruptible : 0u);
            Mode(actor, "Secondary", runningPush ? (uint)SourceMovementModes.RunningPush : 0u);
            return actor;
        }
        void Invoke(PlayerHost host, object actor)
        {
            typeof(GameSimulation).GetMethod("ExecuteSourceMovementMode", flags)!
                .Invoke(host.Game, new[] { actor, (object)default(PlayerCommand) });
            FlowMethod<Action>(host.Game, "Publish")();
        }
        PlayerHost Bound()
        {
            PlayerHost host = CabinetHost(data, out _);
            CabinetStart(host);
            CabinetRaw(host);
            CabinetRender(host, 3);
            return host;
        }
        SourceCompatibilityRepair[] NewRepairs(PlayerHost host) =>
            ((ISourceMatchState)host.Game.State).Match.CompatibilityRepairs.Where(r =>
                r.Kind == SourceCompatibilityRepairKind.CarrierlessDeadBallLateHit).ToArray();
        var abc = Bound();
        object actorAbc = Prepare(abc, 7);
        int beforeAbc = ((SourceInjectedRandom)abc.Game.SourceRandomService).Trace.Count;
        Invoke(abc, actorAbc);
        var deferred = NewRepairs(abc);
        Check(deferred.Length == 1 && deferred[0].SubjectId == 0 && deferred[0].RawIndex == -1 &&
            deferred[0].EffectiveIndex == -1 && deferred[0].RetainedStick == (int)FlowGet(actorAbc, "SourceStick") &&
            ((ISourceMatchState)abc.Game.State).Match.CompatibilityRepairs.Any(r =>
                r.Kind == SourceCompatibilityRepairKind.CarrierlessDefensiveSwitch),
            "The actual bound caller must defer only undefined A, report retained steering, and STILL execute the independent B/C carrierless switch stage.");
        Check((int)(SourceDroneButtons)FlowGet(actorAbc, "SourceButtons") == 7 &&
            (int)(SourceDroneButtons)FlowGet(actorAbc, "SourceNewButtons") == 7,
            "The portable late-hit boundary cannot filter the actual held/new ABC history.");
        var bc = Bound();
        object actorBc = Prepare(bc, 6);
        int beforeBc = ((SourceInjectedRandom)bc.Game.SourceRandomService).Trace.Count;
        Invoke(bc, actorBc);
        Check(NewRepairs(bc).Length == 0 &&
            ((SourceInjectedRandom)abc.Game.SourceRandomService).Trace.Count - beforeAbc ==
                ((SourceInjectedRandom)bc.Game.SourceRandomService).Trace.Count - beforeBc &&
            abc.Game.State.Players[0].Animation.Primary.ScriptSymbol == bc.Game.State.Players[0].Animation.Primary.ScriptSymbol &&
            ((FieldVector)FlowGet(actorAbc, "SourceVelocity")).Equals((FieldVector)FlowGet(actorBc, "SourceVelocity")),
            "Deferring A adds no animation/steering/target/RNG choice compared with the same actual standard-movement and independent B/C caller.");
        foreach (bool strict in new[] { false, true })
        {
            var host = new PlayerHost(data, new RecordingSink(), options: PreviewOptions.FromUrl(
                strict ? "?source-compatibility=strict" : "", data.Catalog.Profile.StableId));
            CabinetRender(host, 3);
            object actor = Prepare(host, 7);
            bool rejected = false;
            try { Invoke(host, actor); }
            catch (TargetInvocationException error)
            { rejected = error.InnerException is InvalidOperationException && error.InnerException.Message.Contains("player_blocks[-1]"); }
            Check(rejected && ((ISourceCabinetFlowState)host.Game.State).CabinetFlow == null &&
                NewRepairs(host).Length == 0,
                "Strict and unbound Portable retain the original named undefined-target guard; the cabinet repair is not a default policy.");
        }
        foreach (var gate in new[] {
            (Phase: GamePhase.LiveBall, Carrier: -1, Push: false, Disabled: false),
            (Phase: GamePhase.DeadBall, Carrier: 7, Push: false, Disabled: false),
            (Phase: GamePhase.DeadBall, Carrier: -1, Push: true, Disabled: false),
            (Phase: GamePhase.DeadBall, Carrier: -1, Push: false, Disabled: true) })
        {
            object actor = Prepare(bc, 1, gate.Phase, gate.Carrier, gate.Push, gate.Disabled);
            Invoke(bc, actor);
            Check(NewRepairs(bc).Length == 0,
                "Live loose-ball, carrier-present, RunningPush and control-disabled A calls must not inherit the new late-hit repair.");
        }
    }
}
