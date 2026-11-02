using System.Reflection;
using System.Text.Json;
using Blitz.AssetImport;
using Blitz.Contracts;
using Blitz.Gameplay;
using Blitz.Runtime;
using Blitz.SourcePlayer;
using Blitz.Storage;

internal static class CustomSelectionChecks
{
    private const string FallbackSite = "playsel.c:PlaySel__MakeChoice:2847";
    private static int assertions;
    private static PlayerData data;
    private static readonly List<object> cases = new();

    private static void Check(bool condition, string identity)
    {
        assertions++;
        if (!condition) throw new InvalidOperationException("CUSTOM_SELECTION: " + identity);
    }

    private static void Reject(Action operation, string field)
    {
        Exception failure = null;
        try { operation(); }
        catch (Exception error) { failure = error is TargetInvocationException ? error.InnerException : error; }
        Check(failure != null && failure.Message.Contains(field, StringComparison.Ordinal),
            "must reject offending field: " + field + "; actual " + failure);
    }

    private static object Model(GameSimulation game) =>
        typeof(GameSimulation).GetProperty("Model", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(game);
    private static object Get(object target, string field) =>
        target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);
    private static void Set(object target, string field, object value) =>
        target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
    private static object Call(object target, string method, params object[] arguments) =>
        target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, arguments);

    private sealed class Fixture
    {
        internal readonly GameSimulation Game;
        internal readonly SourceMatchConfiguration Configuration;
        internal readonly SourceCabinetPlaySelection Menu;
        internal readonly SourceInjectedRandom Random;
        internal readonly List<SourceCabinetMenuEvent> MenuEvents = new();
        internal readonly List<SourceGameEvent> Selections = new();
        internal readonly SourceButtons[] Buttons = new SourceButtons[4];
        internal readonly int[] X = new int[4], Z = new int[4];
        internal SourceCommand Command;
        private readonly SourceCabinetInputBuffer input;
        private readonly SourceCabinetQuantizer quantizer;
        private readonly SourcePlayType kind;
        private bool requestMenu = true;
        internal SourceMatchView Match => ((ISourceMatchState)Game.State).Match;

        internal Fixture(bool bind = true, int draw = 0, TeamId offense = TeamId.Home,
            int team = 30, int down = 1, int los = 35, SourcePlayType kind = SourcePlayType.Normal,
            SourceCaptainSlot captain = SourceCaptainSlot.First,
            SourceStationCustomPlayInventory[] inventory = null, bool singleHuman = false,
            SourceGameplayCompatibility compatibility = SourceGameplayCompatibility.Portable,
            Func<SourceMatchConfiguration, SourceSavedRecordSession> savedRecords = null, bool audibles = false,
            bool cabinetFlow = false, SourceCabinetContinuationPolicy continuation = SourceCabinetContinuationPolicy.KeepParticipatingStations)
        {
            this.kind = kind;
            SourceMatchConfiguration original = data.Configuration.Match;
            SourceStationConfiguration[] stations = singleHuman
                ? new[] { new SourceStationConfiguration((int)offense * 2, offense) }
                : new[] { new SourceStationConfiguration(0, TeamId.Home), new SourceStationConfiguration(1, TeamId.Home),
                    new SourceStationConfiguration(2, TeamId.Away), new SourceStationConfiguration(3, TeamId.Away) };
            Configuration = new SourceMatchConfiguration(SourceSimulationProfile.OriginalRelease57Hz,
                SourceStationLayout.FourStation, new SourceTeamSelection(team, original.HomeTeam.RosterIndices),
                new SourceTeamSelection(team == 30 ? 1 : 30, original.AwayTeam.RosterIndices), stations,
                original.FirstPossession, captain, original.TimeAdjustment, original.Difficulty);
            SourceInjectedRandom random = null;
            Func<int, ISourceRandom> randomFactory = seed => random = new SourceInjectedRandom(
                site => site.SourceIdentity == FallbackSite ? draw : 0,
                new SourceRandomProvenance(data.Playbook.Profile,
                    "explicit custom-selection test inputs; NOT target PRNG", "bounded source-wake fixture",
                    SourceRandomEvidence.ExternalUnverified));
            SourcePlaybook audibleBook = audibles
                ? SourceInterfaceData.ReadDefaultAudiblePlaybook(catalog.EntryBytes("playbook")) : null;
            Game = audibles ? SourceC2Composition.CreateDefaultAudiblesSimulation(data.Animations, data.ReferencePivots,
                audibleBook, Configuration, data.Rosters, data.Configuration.Presentation, data.Movement,
                data.MovementSettings, data.Sound, SourceC2Bootstrap.FromOriginalProgramBootAtSoundBanks(data.Callbacks),
                randomFactory: randomFactory, compatibility: compatibility,
                defaultAudibles: data.Hud.CreateDefaultAudibleData(audibleBook), impactLibrary: data.Impact)
                : SourceC2Composition.CreatePreSnapSwitchSimulation(data.Animations, data.ReferencePivots,
                data.Playbook, Configuration, data.Rosters, data.Configuration.Presentation, data.Movement,
                data.MovementSettings, data.Sound, SourceC2Bootstrap.FromOriginalProgramBootAtSoundBanks(data.Callbacks),
                randomFactory: randomFactory, compatibility: compatibility);
            Random = random;
            Game.BindSourcePossessionSoundData(data.PossessionSound);
            if (bind)
                Game.BindSourceCustomPlaySession(savedRecords != null ? new SourceCustomPlaySession(savedRecords(Configuration))
                    : inventory == null ? SourceCustomPlaySession.KnownNone(Configuration)
                    : new SourceCustomPlaySession(Configuration, inventory), data.Hud);
            if (cabinetFlow)
                Game.BindSourceCabinetFlow(new SourceCabinetFlowConfiguration(
                    Configuration.Stations.Aggregate(0, (mask, station) => mask | 1 << station.StationId),
                    new SourceCabinetButtonMasks(0xf0, 0x70, 0xf0f0f0f0u), continuation,
                    SourceCabinetWaitInitialization.ZeroCountersOnEntry));
            Menu = new SourceCabinetPlaySelection(data.Hud, customPlaySelection: bind ? Game : null);
            Menu.EventRaised += MenuEvents.Add;
            Game.SourceStationInputTransform = Menu.ApplyAtSourceTick;
            var camera = new PreviewCamera(data, null, Game);
            camera.Playback.OriginalNormal.StartedOpeningKickoff();
            input = new SourceCabinetInputBuffer(Configuration, preserveSourceDigitalChanges: audibles);
            quantizer = new SourceCabinetQuantizer(data.Hud.Input);

            // Declared post-result boundary only; timer, input, game_proc, selection,
            // lineup and RNG below are the real production implementations.
            object model = Model(Game);
            Set(model, "SourceRules", new SourceRulesState((int)offense, (int)offense, down, los,
                Math.Min(100, los + 30), kind));
            Set(model, "Possession", offense);
            Set(model, "SourceLastPossession", (int)offense);
            Set(model, "SourceBeginPlayInitialized", false);
            Set(model, "SourceFlags", SourceGameFlags.None);
            Set(model, "Phase", GamePhase.DeadBall);
            Set(model, "SourceMatchPhase", SourceMatchPhase.PlayResult);
            Set(model, "SourceClock", SourceClock.Create());
            Set(model, "ClockActive", false);
            Call(Game, "Publish");
            Check(Match.Phase == SourceMatchPhase.PlayResult && Game.State.Phase == GamePhase.DeadBall,
                "declared post-result boundary waits for the actual Continue input");
            Check(Game.SourceDefaultAudiblesEnabled == audibles && Match.Audibles == null &&
                Game.SourceDefensivePreSnapSwitchEnabled,
                "explicit chosen factory retains its separate audible admission");
        }

        internal int Captain(TeamId side) => SourceCabinetStations.Captain(Match, side);
        internal SourceRandomDraw[] Fallbacks => Random.Trace.Where(d => d.Site.SourceIdentity == FallbackSite).ToArray();

        internal void Wake()
        {
            long tick = Game.SourceTick;
            for (int host = 0; host < 3 && Game.SourceTick == tick; host++)
            {
                input.Capture(Configuration.Stations.Select(station => new SourceCabinetSample(station.StationId,
                    quantizer.SevenLevel(X[station.StationId], Z[station.StationId]), Buttons[station.StationId])).ToArray());
                SourceCommand command = requestMenu ? new SourceCommand(SourceAction.Continue) : Command;
                requestMenu = false;
                Command = default;
                Game.Step(new PlayerCommand(source: command, stationInputs: input.NextHostFrame()));
                Selections.AddRange(Game.SourceEvents.Where(e => e.Type == SourceEventType.PlaySelected));
            }
            Check(Game.SourceTick == tick + 1, "one ordered actual source wake per fixture sample");
        }

        private static SourceCustomPlay SavedPlay(int formation, string name, int run)
        {
            SourceCustomPlay play = SourceCustomPlayCodec.CreateFromFormation(formation, name, "ROUTE");
            for (int receiver = 0; receiver < 3; receiver++)
            {
                SourceCustomPoint root = play.Point(receiver, 0);
                play = SourceCustomPlayCodec.ReplaceRoute(play, receiver, new[] {
                    root, new SourceCustomPoint(root.X, root.Y + run, SourceCustomPointFlags.Last)
                });
            }
            return play;
        }

        private static SourceSavedRecordSession SavedSession(SourceMatchConfiguration configuration,
            SourceRecordAudibles audibles = null, bool missingAudible = false, bool knownNew = false, bool invalidFlags = false)
        {
            var stations = new List<SourceStationRecordSelection>();
            foreach (SourceStationConfiguration station in configuration.Stations)
            {
                if (station.StationId >= 2)
                {
                    stations.Add(new SourceStationRecordSelection(station.StationId, SourceRecordSelectionState.Guest));
                    continue;
                }
                var plays = new SourceCustomPlay[9];
                plays[station.StationId == 0 ? 0 : 2] = SavedPlay(0, "CUT", 12);
                if (station.StationId == 1 && !missingAudible && !knownNew) plays[7] = SavedPlay(1, "WIDE", 18);
                if (station.StationId == 1 && invalidFlags)
                {
                    SourceCustomPlay play = plays[2];
                    plays[2] = new SourceCustomPlay(play.Name1, play.Name2, play.Motion,
                        play.Flags | (SourceCustomPlayFlags)128, play.Points);
                }
                var record = new SourcePlayerRecord(station.StationId,
                    new SourceRecordIdentity(station.StationId == 0 ? "FIRST" : "SECOND", "0000"),
                    new SourceRecordCareer(gamesPlayed: knownNew ? 0 : 12, gamesWon: knownNew ? 0 : 10),
                    playsAllowed: knownNew ? 3 : 9,
                    customPlays: plays, audibles: station.StationId == 1 ? audibles : null);
                stations.Add(new SourceStationRecordSelection(station.StationId,
                    knownNew ? SourceRecordSelectionState.KnownNew : SourceRecordSelectionState.KnownSaved,
                    identity: knownNew ? record.Identity : null, record: record));
            }
            return new SourceSavedRecordSession(configuration, stations);
        }

        private static void ChooseSaved(Fixture fixture)
        {
            fixture.Ready();
            int captain = fixture.Captain(TeamId.Home);
            for (int page = 0; page < 4; page++) fixture.Press(captain, SourceButtons.C);
            Check(fixture.Menu.Page(TeamId.Home) == 4 && fixture.Game.IsSourceCustomPageAvailable(captain),
                "actual captain's existing saved page4 is reachable through four original C presses");
            fixture.X[captain] = 3; fixture.Wake(); fixture.X[captain] = 0; fixture.Wake();
            fixture.X[captain] = 3; fixture.Wake(); fixture.X[captain] = 0; fixture.Wake();
            Check(fixture.Menu.Cursor(TeamId.Home) == 2, "actual original joystick chooses saved slot2");
            fixture.Press(captain, SourceButtons.B);
            fixture.Press(fixture.Captain(TeamId.Away), SourceButtons.B);
            Check(fixture.Game.State.Phase == GamePhase.PreSnap, "both actual choices complete real source lineup");
        }

        internal static void SavedSelectionAndExecution()
        {
            var fixture = new Fixture(captain: SourceCaptainSlot.Second, savedRecords: cfg => SavedSession(cfg));
            ChooseSaved(fixture);
            SourcePlaySelection selected = fixture.Match.HomePlay;
            int observationDraws = fixture.Random.Trace.Count;
            Check(ReferenceEquals(fixture.Menu.CustomPlay(fixture.Game.State, TeamId.Home, 2),
                    fixture.Game.SourceCustomPlaySession.SavedRecords.Station(1).Record.CustomPlays[2]) &&
                fixture.Menu.CustomPlay(fixture.Game.State, TeamId.Home, 8) == null &&
                fixture.Menu.CustomPlay(fixture.Game.State, TeamId.Away, 2) == null &&
                fixture.Random.Trace.Count == observationDraws,
                "readonly custom-card observer exposes the captain's actual saved27point grid, missing/defense NULL and zero RNG");
            var play = (SourcePlayDefinition)Get(Model(fixture.Game), "SourceOffensePlay");
            var drones = (SourceRouteState[])Get(Model(fixture.Game), "SourceDrones");
            Check(selected.PlaySymbol == "custom_p1_s2" && selected.SavedOrigin.StationId == 1 &&
                selected.SavedOrigin.SlotIndex == 2 && selected.PageIndex == 4 && selected.SlotIndex == 2 &&
                (play.Flags & SourcePlayFlags.Custom) != 0 && !data.Playbook.Plays.ContainsKey(selected.PlaySymbol),
                "real source lineup retains typed actual captain/slot identity outside the static authenticated catalog");
            for (int receiver = 0; receiver < 3; receiver++)
                Check(ReferenceEquals(drones[receiver + 4].Program, play.Routes[receiver + 4]) &&
                    !data.Playbook.Routes.ContainsKey(play.Routes[receiver + 4].Identity),
                    "real receiver drone uses its converted saved route, not a catalog/PAT substitute");
            var model = (SimulationState)Model(fixture.Game);
            int receiverId = Enumerable.Range(4, 3).First(id => model.Players[id].SourceControllerStation < 0);
            SourceVector3 before = SourcePlayerDriver.WorldPosition(model.Players[receiverId]);
            fixture.Wakes(60); fixture.Press(1, SourceButtons.B); fixture.Wakes(18);
            Check(fixture.Game.State.Phase == GamePhase.LiveBall && model.SourceDrones[receiverId].WordIndex > 0 &&
                ReferenceEquals(model.SourceDrones[receiverId].Program, play.Routes[receiverId]) &&
                SourcePlayerDriver.WorldPosition(model.Players[receiverId]).X != before.X && fixture.Fallbacks.Length == 0,
                "actual source Snap and player wakes execute the saved route and move its drone receiver without a fallback draw");
            cases.Add(new { name = "saved-page-lineup-live-route", selected.PlaySymbol,
                selected.SavedOrigin.StationId, selected.SavedOrigin.SlotIndex,
                receiverId, route = model.SourceDrones[receiverId].Program.Identity,
                model.SourceDrones[receiverId].WordIndex });

            foreach (int highlighted in new[] { 7, 8 })
            {
                var forced = new Fixture(captain: SourceCaptainSlot.Second, savedRecords: cfg => SavedSession(cfg));
                forced.Ready(); forced.Highlight(TeamId.Home, 4, highlighted); forced.UntilForced();
                Check(forced.Match.HomePlay.SavedOrigin.StationId == 1 &&
                    forced.Match.HomePlay.SavedOrigin.SlotIndex == (highlighted == 7 ? 7 : 2) &&
                    forced.Fallbacks.Length == 0 && forced.Menu.Cursor(TeamId.Home) == highlighted,
                    "timer chooses requested existing or captain's first existing0..8, with zero randrng(9) and unchanged cursor");
            }

            var missing = new Fixture(captain: SourceCaptainSlot.Second, savedRecords: cfg => SavedSession(cfg));
            missing.Ready(); missing.Highlight(TeamId.Home, 4, 8);
            int draws = missing.Random.Trace.Count; missing.Press(1, SourceButtons.B);
            Check(!missing.Menu.Selected(TeamId.Home) && missing.Selections.Count == 0 &&
                missing.Random.Trace.Count == draws && missing.MenuEvents.All(e => e.Kind != SourceCabinetMenuEventKind.Selected),
                "saved inventory's missing manual slot remains NULL without event/sound/RNG or ready mutation");
            missing.Command = new SourceCommand(SourceAction.SelectPlay,
                new SourcePlaySelection("custom_p1_s2", SourcePlaybookKind.Offense, 2, 4));
            Reject(missing.Wake, "custom_p1_s2");
            var other = new Fixture(captain: SourceCaptainSlot.Second, savedRecords: cfg => SavedSession(cfg));
            other.Ready(); other.Command = new SourceCommand(SourceAction.SelectPlay, selected);
            Reject(other.Wake, "SAME session");
            var wrongStation = new Fixture(captain: SourceCaptainSlot.Second, savedRecords: cfg => SavedSession(cfg));
            wrongStation.Ready();
            wrongStation.Command = new SourceCommand(SourceAction.SelectPlay, new SourcePlaySelection(
                new SourceSavedPlayOrigin(wrongStation.Game.SourceCustomPlaySession.SavedRecords, 0, 0)));
            Reject(wrongStation.Wake, "captain");
            Reject(() => new Fixture(savedRecords: cfg => SavedSession(cfg, invalidFlags: true)), "invalid flags");
            var fresh = new Fixture(captain: SourceCaptainSlot.Second, savedRecords: cfg => SavedSession(cfg, knownNew: true));
            fresh.Ready();
            for (int page = 0; page < 4; page++) fresh.Press(1, SourceButtons.C);
            Check(fresh.Menu.Page(TeamId.Home) == 4 &&
                fresh.Game.SourceCustomPlaySession.SavedRecords.Station(1).State == SourceRecordSelectionState.KnownNew &&
                fresh.Game.SourceCustomPlaySession.InventoryFor(1) == SourceCustomPlayInventoryState.KnownSaved,
                "KnownNew with its actual just-created saved definition is not inferred empty");
        }

        internal static void SavedAudiblesAndRestore()
        {
            var rows = new SourceRecordAudibles(new short[] { -44, 39, -1 }, new short[3]);
            var fixture = new Fixture(captain: SourceCaptainSlot.Second, audibles: true,
                savedRecords: cfg => SavedSession(cfg, rows));
            ChooseSaved(fixture);
            var model = (SimulationState)Model(fixture.Game);
            SourceAudibleState audibles = model.SourceAudibleState;
            SourcePlayDefinition original = model.SourceOffensePlay;
            SourcePlaySelection originalSelection = fixture.Match.HomePlay;
            Check(audibles.Ids[1, 0, 0] == -44 && audibles.Ids[1, 0, 1] == 39 &&
                audibles.Ids[0, 0, 0] == 1 && audibles.Ids[0, 0, 1] == 2 && audibles.Ids[0, 0, 2] == 3 &&
                audibles.Ids[1, 1, 0] == 1 && audibles.Ids[1, 1, 1] == 8 && audibles.Ids[1, 1, 2] == 7,
                "actual saved signed offense row installs; allzero rows retain exact select.c960 offense/defense defaults");
            fixture.Press(1, SourceButtons.C); fixture.Wake(); fixture.Press(1, SourceButtons.C);
            Check(fixture.Match.Audibles.HomeTeam.Action == 1, "actual double C opens saved audible mode");
            for (int choice = 1; choice <= 4; choice++)
            {
                fixture.Z[1] = 3; fixture.Wake(); fixture.Z[1] = 0; fixture.Wake(); fixture.Wakes(5);
                Check(fixture.Match.Audibles.Offense.Choice == choice % 4,
                    "actual digital Down selects next original audible: expected=" + choice % 4 +
                    ",actual=" + fixture.Match.Audibles.Offense.Choice + ",play=" + model.SourceOffensePlay.Identity);
                if (choice == 1)
                    Check(model.SourceOffensePlay.Identity == "custom_p1_s7" && model.SourceOffenseFlipped &&
                        fixture.Match.HomePlay.SavedOrigin.SlotIndex == 7 &&
                        ReferenceEquals(model.SourceDrones[4].Program, model.SourceOffensePlay.Routes[4]),
                        "signed custom -44 rebuilds actual formation/routes/controllers and off_flip from this captain's slot7");
                if (choice == 2)
                    Check(model.SourceOffensePlay.Identity == "custom_p1_s2" && !model.SourceOffenseFlipped,
                        "positive custom39 executes slot2 and clears the signed flip");
            }
            Check(ReferenceEquals(model.SourceOffensePlay, original) &&
                ReferenceEquals(fixture.Match.HomePlay.SavedOrigin, originalSelection.SavedOrigin) &&
                !model.SourceOffenseFlipped && fixture.Fallbacks.Length == 0,
                "ORIGINAL restores the real saved definition, typed identity and original flip without static lookup or RNG");
            cases.Add(new { name = "saved-signed-audibles-original-restore", original = original.Identity,
                rows = new[] { -44, 39, -1 }, fallbackDraws = fixture.Fallbacks.Length });

            var blocked = new Fixture(captain: SourceCaptainSlot.Second, savedRecords: cfg => SavedSession(cfg));
            blocked.Ready();
            ((SimulationState)Model(blocked.Game)).SourceAudibleState = new SourceAudibleState { PlaybookMode = 2 };
            blocked.Highlight(TeamId.Home, 3, 0); blocked.Press(1, SourceButtons.C);
            Check(blocked.Menu.Page(TeamId.Home) == 0, "original pup_playbook2 still skips a genuinely populated custom page");

            var unavailable = new Fixture(captain: SourceCaptainSlot.Second, audibles: true,
                savedRecords: cfg => SavedSession(cfg,
                    new SourceRecordAudibles(new short[] { 44, 39, 1 }, new short[3]), missingAudible: true));
            ChooseSaved(unavailable);
            unavailable.Press(1, SourceButtons.C); unavailable.Wake(); unavailable.Press(1, SourceButtons.C);
            unavailable.Z[1] = 3;
            Reject(unavailable.Wake, "no forced or random fallback");
            Check(unavailable.Fallbacks.Length == 0 &&
                ((SimulationState)Model(unavailable.Game)).SourceOffensePlay.Identity == "custom_p1_s2",
                "custom audible missing is forced=false NULL, never timer randomness or a replacement play");
        }

        private sealed class CommitBarrier : ISourceRecordCommitBarrier
        {
            internal int Begins, Reads;
            internal SourceFinalRecordCommit Context;
            internal SourceRecordCommitBarrierState State;
            public void BeginFinalCommit(SourceFinalRecordCommit context)
            {
                Begins++; Context = context;
                Check(context.FinalMatch.Phase == SourceMatchPhase.MatchEnded, "record callback receives the actual terminal match");
            }
            public SourceRecordCommitBarrierState ReadFinalCommitState(SourceFinalRecordCommit context)
            {
                Check(ReferenceEquals(context, Context), "every record poll retains the SAME final commit context");
                Reads++; return State;
            }
        }

        private static void DeclaredFinalPeriod(Fixture fixture, int periods)
        {
            var model = (SimulationState)Model(fixture.Game);
            model.Quarter = periods;
            model.SourcePeriodView = new SourcePeriodView(periods - 1);
            model.SourceClock = SourceClock.Create(0);
            model.PackedGameTime = 0;
            model.HomeScore = 6; model.AwayScore = 0;
            model.SourceQuarterScores[0, 0] = 6;
            Call(fixture.Game, "Publish");
        }

        internal static void FinalRecordCommitOrdering()
        {
            foreach (SourceRecordCommitBarrierState completion in new[] {
                SourceRecordCommitBarrierState.Committed, SourceRecordCommitBarrierState.Declined })
            {
                var fixture = new Fixture(savedRecords: cfg => SavedSession(cfg), cabinetFlow: true);
                var barrier = new CommitBarrier();
                fixture.Game.BindSourceRecordCommitBarrier(barrier);
                DeclaredFinalPeriod(fixture, 4);
                for (int i = 0; i < 700 && ((ISourceCabinetFlowState)fixture.Game.State).CabinetFlow.Stage !=
                    SourceCabinetFlowStage.RecordCommitWait; i++) fixture.Wake();
                SourceCabinetFlowView flow = ((ISourceCabinetFlowState)fixture.Game.State).CabinetFlow;
                Check(flow.Stage == SourceCabinetFlowStage.RecordCommitWait && barrier.Begins == 1 &&
                    barrier.Context.CompletedPeriodCount == 4 && barrier.Context.ParticipatingStationMask == 15 &&
                    barrier.Context.HomeScore == 6 && barrier.Context.AwayScore == 0 &&
                    ReferenceEquals(flow.FinalMatch, barrier.Context.FinalMatch) &&
                    barrier.Context.FinalMatch.QuarterScores.Count == 4 && barrier.Context.ProcessSerial == 1,
                    "actual expired fourth-period continuation captures genuine period/participation/final identity before any statistics wait");
                int draws = fixture.Random.Trace.Count;
                fixture.Buttons[0] = SourceButtons.A | SourceButtons.B | SourceButtons.C;
                fixture.Command = new SourceCommand(SourceAction.Continue); fixture.Wakes(3);
                barrier.State = SourceRecordCommitBarrierState.Failed; fixture.Wakes(3);
                Check(((ISourceCabinetFlowState)fixture.Game.State).CabinetFlow.Stage == SourceCabinetFlowStage.RecordCommitWait &&
                    barrier.Begins == 1 && fixture.Random.Trace.Count == draws &&
                    ((SourceCabinetFlowProcess)Get(fixture.Game, "sourceCabinetFlow")).AnyButton == null,
                    "pending/failed durable IO sleeps actual game_proc; held ABC/Continue cannot start stats, redraw save, consume RNG or fake a wait");
                Reject(() => fixture.Game.Restart(), "commit");
                barrier.State = completion; fixture.Wake();
                flow = ((ISourceCabinetFlowState)fixture.Game.State).CabinetFlow;
                Check(flow.Stage == SourceCabinetFlowStage.FinalStats && barrier.Begins == 1 &&
                    ReferenceEquals(flow.FinalMatch, barrier.Context.FinalMatch),
                    "real committed or explicitly declined completion releases exactly the original final-stat continuation once");
                int reads = barrier.Reads; fixture.Wakes(3);
                Check(barrier.Reads == reads && barrier.Begins == 1, "final stats cannot poll or resubmit the completed record operation");
            }

            var partial = new Fixture(savedRecords: cfg => SavedSession(cfg), cabinetFlow: true,
                continuation: SourceCabinetContinuationPolicy.RequireConfirmation);
            var partialBarrier = new CommitBarrier(); partial.Game.BindSourceRecordCommitBarrier(partialBarrier);
            DeclaredFinalPeriod(partial, 2);
            int partialBudget = 4 * SourceTiming.DefaultTicksPerSecond + 23 * SourceTiming.DefaultTicksPerSecond
                + 10 * (SourceTiming.DefaultTicksPerSecond + 26) + 20;
            for (int i = 0; i < partialBudget &&
                ((ISourceCabinetFlowState)partial.Game.State).CabinetFlow.Stage != SourceCabinetFlowStage.FinalStats; i++)
                partial.Wake();
            SourceCabinetFlowView final = ((ISourceCabinetFlowState)partial.Game.State).CabinetFlow;
            Check(final.Stage == SourceCabinetFlowStage.FinalStats && final.FinalMatch.Period.CompletedQuarterIndex == 1 &&
                final.FinalMatch.QuarterScores.Count == 2 && partialBarrier.Begins == 0,
                "a real declined second-period cabinet continuation remains two periods and bypasses original save guard; never fake four: " +
                final.Stage + "/elapsed=" + final.ElapsedSourceWakes + "/timer=" + final.ContinuationSeconds +
                "/period=" + partial.Match.Period.QuarterIndex + "/begins=" + partialBarrier.Begins);
            cases.Add(new { name = "actual-final-record-io-barrier-before-stats", completedPeriods = 4,
                partialDeclinedPeriods = 2, outcomes = new[] { "Committed", "Declined" } });
        }

        internal void Wakes(int count) { for (int i = 0; i < count; i++) Wake(); }
        internal void Ready()
        {
            Wakes(22);
            Check(Match.Phase == (kind == SourcePlayType.ExtraPoint ? SourceMatchPhase.PatSelection
                : SourceMatchPhase.PlaySelection) && Match.SelectionTimer != null,
                "actual Continue input enters production play-selection timer and cabinet warmup");
        }
        internal void Press(int station, SourceButtons buttons)
        {
            Buttons[station] = SourceButtons.None; Wake();
            Buttons[station] = buttons; Wake();
            Buttons[station] = SourceButtons.None; Wake();
        }
        internal void Highlight(TeamId side, int page, int slot)
        {
            ((int[])Get(Menu, "page"))[(int)side] = page;
            ((int[])Get(Menu, "cursor"))[(int)side] = slot;
        }
        internal void UntilForced()
        {
            TeamId offense = Match.Possession.Offense;
            for (int i = 0; i < 1800 && Game.State.Phase == GamePhase.PlaySelect &&
                !Menu.Selected(offense); i++) Wake();
            Check(Menu.Selected(offense), "the real timer must force the actual captain highlight");
        }
    }

    private static void ManualAndPages()
    {
        var fixture = new Fixture(captain: SourceCaptainSlot.Second);
        fixture.Ready();
        int captain = fixture.Captain(TeamId.Home);
        Check(captain == 1, "source teammate order selects the real controlling station, not team number");
        int before = fixture.Random.Trace.Count;
        fixture.Highlight(TeamId.Home, 4, 8);
        fixture.Press(captain, SourceButtons.B);
        Check(!fixture.Menu.Selected(TeamId.Home) && !fixture.Match.SelectionTimer.HomeSelected &&
            fixture.Selections.Count == 0 && fixture.MenuEvents.All(e => e.Kind != SourceCabinetMenuEventKind.Selected) &&
            fixture.Random.Trace.Count == before && fixture.Menu.Page(TeamId.Home) == 4 && fixture.Menu.Cursor(TeamId.Home) == 8,
            "manual missing custom returns NULL: no RNG, selection, participant clear, sound event or cursor rewrite");
        fixture.Highlight(TeamId.Home, 3, 0);
        fixture.Press(0, SourceButtons.C);
        Check(fixture.Menu.Page(TeamId.Home) == 3, "noncaptain cannot cycle this side's page");
        fixture.Press(captain, SourceButtons.C);
        Check(fixture.Menu.Page(TeamId.Home) == 0 && fixture.Random.Trace.Count == before &&
            fixture.MenuEvents.Count(e => e.Kind == SourceCabinetMenuEventKind.PageChanged) == 1,
            "known-none captain skips offense/custom4 with one page sound and no random draw; page=" +
            fixture.Menu.Page(TeamId.Home) + " draws=" + fixture.Random.Trace.Count + "/" + before +
            " events=" + string.Join(",", fixture.MenuEvents.Select(e => e.StationId + ":" + e.Kind + ":" + e.PageIndex)));
        Check(data.Hud.PlaySlots["off_slots"].Skip(5).Take(3).All(slot => slot.PlaySymbol == null),
            "actual approved HUD retains its three static team placeholders");
        string actual = data.Hud.PageSlot(SourcePlaybookKind.Offense, 0, 0, fixture.Configuration.HomeTeam.TeamIndex).PlaySymbol;
        fixture.Press(captain, SourceButtons.B);
        Check(fixture.Match.HomePlay.PlaySymbol == actual && fixture.Match.HomePlay.PageIndex == 0 &&
            fixture.Match.HomePlay.SlotIndex == 0 && fixture.Match.SelectionTimer.HomeSelected &&
            fixture.Selections.Count(e => e.Team == TeamId.Home) == 1 && fixture.Fallbacks.Length == 0,
            "manual team placeholder resolves to the actual selected team's first play, not missing custom inventory");
        fixture.Press(captain, SourceButtons.B);
        Check(fixture.Selections.Count(e => e.Team == TeamId.Home) == 1 &&
            fixture.MenuEvents.Count(e => e.Kind == SourceCabinetMenuEventKind.Selected) == 1,
            "repeated B cannot replay source choice, participant clear or selection audio/event");
        fixture.Highlight(TeamId.Away, 1, 0);
        fixture.Press(fixture.Captain(TeamId.Away), SourceButtons.C);
        Check(fixture.Menu.Page(TeamId.Away) == 0 && fixture.Random.Trace.Count == before,
            "actual defensive captain retains its two-page cycle without a custom-page or RNG branch");
        cases.Add(new { name = "manual-missing-pages-team-placeholder", controllingStation = captain, actual });
    }

    private static void ForcedCurrentPage()
    {
        var checks = new[] {
            (Draw: 1, Offense: TeamId.Home, Down: 1, Los: 35, Kind: SourcePlayType.Normal, Special: -1),
            (Draw: 2, Offense: TeamId.Away, Down: 1, Los: 35, Kind: SourcePlayType.Normal, Special: -1),
            (Draw: 6, Offense: TeamId.Home, Down: 4, Los: 70, Kind: SourcePlayType.Normal, Special: 38),
            (Draw: 7, Offense: TeamId.Home, Down: 4, Los: 35, Kind: SourcePlayType.Normal, Special: 37),
            (Draw: 0, Offense: TeamId.Home, Down: 1, Los: 97, Kind: SourcePlayType.ExtraPoint, Special: 40)
        };
        foreach (var item in checks)
        {
            var fixture = new Fixture(draw: item.Draw, offense: item.Offense, down: item.Down, los: item.Los,
                kind: item.Kind, singleHuman: item.Kind == SourcePlayType.ExtraPoint);
            fixture.Ready();
            int captain = fixture.Captain(item.Offense);
            int sourceTeam = item.Offense == TeamId.Home ? fixture.Configuration.HomeTeam.TeamIndex
                : fixture.Configuration.AwayTeam.TeamIndex;
            string expected = item.Special >= 0 ? data.Hud.PlaySlots["off_slots"][item.Special].PlaySymbol
                : data.Hud.PageSlot(SourcePlaybookKind.Offense, 0, item.Draw, sourceTeam).PlaySymbol;
            fixture.Highlight(item.Offense, 4, 8);
            int before = fixture.Random.Trace.Count;
            fixture.UntilForced();
            SourceRandomDraw[] draws = fixture.Fallbacks;
            Check(draws.Length == 1 && draws[0].Site.Kind == SourceRandomKind.RandRange &&
                draws[0].Site.BoundOrModulus == 9 && draws[0].Value == item.Draw &&
                draws[0].Site.Process == "game_proc" && draws[0].Site.ActorId == -1 &&
                draws[0].Site.CallIndex == 0 && draws[0].Order == before,
                "forced known-none consumes exactly the first shared game_proc randrng(9), never CPU/public/gesture RNG");
            Check(fixture.Menu.Page(item.Offense) == 4 && fixture.Menu.Cursor(item.Offense) == 8 &&
                fixture.MenuEvents.Count(e => e.Side == item.Offense &&
                    e.Kind == SourceCabinetMenuEventKind.ForcedSelected) == 1,
                "forced fallback commits once without rewriting source UI page/cursor");
            if (item.Kind == SourcePlayType.ExtraPoint)
                Check(fixture.Game.State.HomeScore == 1 &&
                    fixture.Match.PatChoice == SourcePatOption.FreeKick &&
                    fixture.Selections.All(e => e.Selection.PlaySymbol != expected),
                    "draw0 resolves actual current extra-point slot to the real free-PAT path, not two-point catalog play");
            else
            {
                SourcePlaySelection actual = item.Offense == TeamId.Home ? fixture.Match.HomePlay : fixture.Match.AwayPlay;
                Check(actual.PlaySymbol == expected && actual.PageIndex == 0 && actual.SlotIndex == item.Draw &&
                    !actual.Pool.HasValue && fixture.Selections.Count(e => e.Team == item.Offense) == 1,
                    "actual forced source selection retains the drawn first-page ordinal/team/FG/punt identity");
                Check(fixture.Random.Trace.Skip(before + 1).All(d => d.Site.SourceIdentity != FallbackSite),
                    "remaining same-wake lineup/CPU draws cannot replay the custom fallback");
                fixture.Wakes(2);
                Check(fixture.Fallbacks.Length == 1 &&
                    fixture.MenuEvents.Count(e => e.Side == item.Offense &&
                        e.Kind == SourceCabinetMenuEventKind.ForcedSelected) == 1,
                    "following real wakes do not duplicate forced selection or menu audio");
            }
            cases.Add(new { name = "forced-current-first-page", item.Draw, item.Offense, item.Down, item.Los,
                kind = item.Kind.ToString(), expected, captain, sourceTick = draws[0].Site.SourceTick,
                processOrder = draws[0].Site.ProcessOrder, callIndex = draws[0].Site.CallIndex, order = draws[0].Order });
        }
    }

    private static void InventoryAndCallerGuards()
    {
        var fixture = new Fixture();
        Reject(() => fixture.Game.BindSourceCustomPlaySession(SourceCustomPlaySession.KnownNone(fixture.Configuration),
            data.Hud), "once");
        Reject(() => fixture.Game.ResolveSourceCustomPlayChoice(new SourceCustomPlayChoice(0, TeamId.Home,
            SourcePlaybookKind.Offense, 4, 0, 0, false)), "game_proc");
        var unbound = new Fixture(bind: false);
        Reject(() => unbound.Game.BindSourceCustomPlaySession(SourceCustomPlaySession.KnownNone(fixture.Configuration),
            data.Hud), "SAME");
        Reject(() => unbound.Game.BindSourceCustomPlaySession(SourceCustomPlaySession.KnownNone(unbound.Configuration),
            null), "authenticated original HUD");
        var strict = new Fixture(bind: false, compatibility: SourceGameplayCompatibility.StrictSource);
        Reject(() => strict.Game.BindSourceCustomPlaySession(SourceCustomPlaySession.KnownNone(strict.Configuration),
            data.Hud), "Portable");
        Reject(() => new GameSimulation().BindSourceCustomPlaySession(SourceCustomPlaySession.KnownNone(fixture.Configuration),
            data.Hud), "Portable");
        Check(strict.Game.Compatibility == SourceGameplayCompatibility.StrictSource &&
            strict.Game.SourceCustomPlaySession == null, "strict/legacy profiles remain unbound without profile promotion");
        Reject(() => new SourceCustomPlaySession(fixture.Configuration,
            new[] { new SourceStationCustomPlayInventory(0, SourceCustomPlayInventoryState.KnownNone) }), "every");
        Reject(() => new SourceStationCustomPlayInventory(0, (SourceCustomPlayInventoryState)2), "state");
        unbound.Ready();
        Reject(() => unbound.Game.BindSourceCustomPlaySession(SourceCustomPlaySession.KnownNone(unbound.Configuration),
            data.Hud), "before the first");
        unbound.Highlight(TeamId.Home, 4, 0);
        int before = unbound.Random.Trace.Count;
        Reject(() => unbound.Press(0, SourceButtons.B), "inventory/decompression");
        Check(unbound.Random.Trace.Count == before && unbound.Selections.Count == 0,
            "absent session cannot masquerade as an empty saved-play scan");

        var states = new[] {
            new SourceStationCustomPlayInventory(0, SourceCustomPlayInventoryState.KnownNone),
            new SourceStationCustomPlayInventory(1, SourceCustomPlayInventoryState.Unavailable),
            new SourceStationCustomPlayInventory(2, SourceCustomPlayInventoryState.KnownNone),
            new SourceStationCustomPlayInventory(3, SourceCustomPlayInventoryState.KnownNone) };
        var unknown = new Fixture(captain: SourceCaptainSlot.Second, inventory: states);
        unknown.Ready();
        unknown.Highlight(TeamId.Home, 3, 0);
        unknown.Press(1, SourceButtons.C);
        Check(unknown.Menu.Page(TeamId.Home) == 0 &&
            unknown.Game.SourceCustomPlaySession.InventoryFor(1) == SourceCustomPlayInventoryState.Unavailable,
            "unavailable controlling station skips custom without borrowing teammate0's known-none inventory");
        unknown.Highlight(TeamId.Home, 4, 0);
        before = unknown.Random.Trace.Count;
        Reject(() => unknown.Press(1, SourceButtons.B), "inventory is unavailable");
        Check(unknown.Random.Trace.Count == before && unknown.Selections.Count == 0 &&
            !unknown.Menu.Selected(TeamId.Home), "unknown inventory rejects before RNG, chosen bit and audio");
        var unknownForced = new Fixture(captain: SourceCaptainSlot.Second, inventory: states);
        unknownForced.Ready();
        unknownForced.Highlight(TeamId.Home, 4, 8);
        before = unknownForced.Random.Trace.Count;
        Reject(unknownForced.UntilForced, "inventory is unavailable");
        Check(unknownForced.Random.Trace.Count == before && unknownForced.Selections.Count == 0 &&
            !unknownForced.Menu.Selected(TeamId.Home) &&
            unknownForced.MenuEvents.All(e => e.Kind != SourceCabinetMenuEventKind.ForcedSelected),
            "actual timer force cannot treat unavailable captain1 inventory as teammate0's empty scan");

        Action<SourceCustomPlayChoice, string> invalid = (choice, field) => {
            var active = new Fixture();
            active.Ready();
            int start = active.Random.Trace.Count;
            active.Game.SourceStationInputTransform = (state, tick, frame) => {
                active.Game.ResolveSourceCustomPlayChoice(new SourceCustomPlayChoice(tick + choice.SourceTick,
                    choice.Team, choice.Playbook, choice.PageIndex, choice.SlotIndex, choice.StationId, choice.Forced));
                return frame;
            };
            Reject(active.Wake, field);
            Check(active.Random.Trace.Count == start && active.Selections.Count == 0,
                "invalid live custom request rejects before the shared draw: " + field);
        };
        invalid(new SourceCustomPlayChoice(0, TeamId.Away, SourcePlaybookKind.Offense, 4, 0, 2, false), "offense/custom-page");
        invalid(new SourceCustomPlayChoice(0, TeamId.Home, SourcePlaybookKind.Defense, 4, 0, 0, false), "offense/custom-page");
        invalid(new SourceCustomPlayChoice(0, TeamId.Home, SourcePlaybookKind.Offense, 0, 0, 0, false), "offense/custom-page");
        invalid(new SourceCustomPlayChoice(0, TeamId.Home, SourcePlaybookKind.Offense, 4, 0, 1, false), "captain");
        invalid(new SourceCustomPlayChoice(1, TeamId.Home, SourcePlaybookKind.Offense, 4, 0, 0, false), "game_proc");
        invalid(new SourceCustomPlayChoice(0, TeamId.Home, SourcePlaybookKind.Offense, 4, 0, 0, true), "timer continuation");
        cases.Add(new { name = "unknown-inventory-unbound-prewake-source-caller-guards", unavailableInventoryRejected = true });
    }

    private static void RandomGestureUnchanged()
    {
        Fixture Run(bool bind)
        {
            var fixture = new Fixture(bind: bind);
            fixture.Ready();
            int station = fixture.Captain(TeamId.Home);
            fixture.X[station] = -3; fixture.Wake();
            fixture.X[station] = 0; fixture.Wake();
            fixture.X[station] = -3; fixture.Buttons[station] = SourceButtons.C; fixture.Wake();
            fixture.X[station] = 0; fixture.Buttons[station] = SourceButtons.None;
            for (int i = 0; i < 60 && !fixture.Menu.Selected(TeamId.Home); i++) fixture.Wake();
            Check(fixture.Menu.Selected(TeamId.Home), "the real paced random gesture still selects");
            return fixture;
        }
        Fixture baseline = Run(false), bound = Run(true);
        string[] Trace(Fixture fixture) => fixture.Random.Trace.Select(d => d.Site + " value=" + d.Value).ToArray();
        Check(Trace(baseline).SequenceEqual(Trace(bound)) &&
            bound.Random.Trace.Count(d => d.Site.SourceIdentity.StartsWith("playsel.c:play_select_cntrl:", StringComparison.Ordinal)) == 30 &&
            bound.Fallbacks.Length == 0 && baseline.Match.HomePlay.PlaySymbol == bound.Match.HomePlay.PlaySymbol &&
            baseline.Match.HomePlay.PageIndex == bound.Match.HomePlay.PageIndex &&
            baseline.Match.HomePlay.SlotIndex == bound.Match.HomePlay.SlotIndex,
            "new inventory does not alter any of the gesture's thirty ordered shared draws or chosen page/slot");
        Check(baseline.MenuEvents.Select(e => (e.Kind, e.PageIndex, e.SlotIndex, e.SoundCall))
            .SequenceEqual(bound.MenuEvents.Select(e => (e.Kind, e.PageIndex, e.SlotIndex, e.SoundCall))),
            "random gesture emits identical ordered menu/audio operations");
        cases.Add(new { name = "existing-random-gesture", draws = 30, fullOrderedTraceUnchanged = true });
    }

    private static void AudiblePageResolution()
    {
        byte[] bytes = catalog.EntryBytes("playbook");
        SourcePlaybook book = SourceInterfaceData.ReadDefaultAudiblePlaybook(bytes);
        SourceDefaultAudibleData audible = data.Hud.CreateDefaultAudibleData(book);
        MethodInfo resolve = typeof(SourceDefaultAudibleData).GetMethod("Resolve", BindingFlags.Instance | BindingFlags.NonPublic);
        SourcePlayDefinition Resolve(short id, int down, int los, bool super = false, bool noPunt = false)
        {
            object[] arguments = { id, SourceAudibleRole.Offense, 30, new SourceRulesState(0, 0, down, los, 100),
                0, 0x02000000u, super, noPunt, false };
            return (SourcePlayDefinition)resolve.Invoke(audible, arguments);
        }
        Check(Resolve(1, 1, 35).Identity == data.Hud.PageSlot(SourcePlaybookKind.Offense, 0, 0, 30).PlaySymbol &&
            Resolve(7, 4, 70).Identity == data.Hud.PlaySlots["off_slots"][38].PlaySymbol &&
            Resolve(8, 4, 35).Identity == data.Hud.PlaySlots["off_slots"][37].PlaySymbol &&
            Resolve(7, 4, 35, super: true).Identity == data.Hud.PlaySlots["off_slots"][38].PlaySymbol &&
            Resolve(7, 4, 35, noPunt: true).Identity == data.Hud.PageSlot(SourcePlaybookKind.Offense, 0, 6, 30).PlaySymbol,
            "factored real current-page helper preserves existing audible team/field-goal/punt/powerup resolution");
        Reject(() => Resolve(37, 1, 35), "Custom/missing audible pages");
        cases.Add(new { name = "existing-audible-page-resolver", customAudible = "static helper rejects dynamic origins; no timeout RNG",
            fullAudibleInputIntegration = "not admitted by approved pre-snap-switch563 profile" });
    }

    private static DataCatalog catalog;
    public static int Main(string[] args)
    {
        try
        {
            if (args.Length != 1) throw new ArgumentException("Supply the existing approved563 data root; no data preparation or fallback.");
            string root = Path.GetFullPath(args[0]);
            catalog = new DataCatalog(File.ReadAllBytes(Path.Combine(root, "manifest.json")),
                path => File.ReadAllBytes(Path.Combine(root, path)));
            Check(catalog.Profile.IsContact282C2M1PreSnapSwitch && !catalog.Profile.IsContact282C2M1DefaultAudibles &&
                catalog.FileCount == 563, "use the actual unchanged approved pre-snap-switch563 producer inputs");
            data = new PlayerData(catalog);
            ManualAndPages();
            ForcedCurrentPage();
            InventoryAndCallerGuards();
            RandomGestureUnchanged();
            AudiblePageResolution();
            Fixture.SavedSelectionAndExecution();
            Fixture.SavedAudiblesAndRestore();
            Fixture.FinalRecordCommitOrdering();
            Console.WriteLine(JsonSerializer.Serialize(new { schema = "source-custom-selection-checks/v1",
                status = "passed", assertions, profile = catalog.Profile.StableId, catalog.ManifestSha256, cases,
                boundary = "Actual authenticated PlayerData/HUD/catalog/M1 simulation, quantizer/FIFO, real menu/game_proc/timer/selection/lineup/live saved routes and separately admitted audible factory. Explicit saved-record fixtures and declared post-result entry; not browser, native or whole-match equivalence." }));
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            Console.WriteLine(JsonSerializer.Serialize(new { schema = "source-custom-selection-checks/v1",
                status = "failed", assertions, error = error.ToString() }));
            return 1;
        }
    }
}
