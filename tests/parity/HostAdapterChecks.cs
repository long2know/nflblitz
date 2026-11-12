using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Blitz.Contracts;
using Blitz.Gameplay;

internal static class HostAdapterChecks
{
#if PARITY_FULL_KERNEL
    internal const bool Enabled = true;
#else
    internal const bool Enabled = false;
#endif

    internal static void Run(FullKernelInputs inputs, JsonElement fixture)
    {
#if PARITY_FULL_KERNEL
        if (inputs == null)
            throw new System.ArgumentNullException(nameof(inputs), "Actual prepared full-kernel inputs are required.");
        var game = Create(inputs);
        Program.Equal("injected source profile", SimulationProfile.OriginalRelease57Hz, game.Profile);
        Program.Equal("legacy constructor keeps legacy profile", SimulationProfile.LegacyBaseline60Hz,
            new GameSimulation(1).Profile);
        var initial = (ISourceGameState)game.State;
        Program.Equal("initial source tick", 0L, initial.SourceTick);
        Program.Equal("initial source Hz", fixture.GetProperty("source_hz").GetInt32(), initial.SourceTicksPerSecond);
        Program.Equal("initial source packed clock", 0x20000u, initial.PackedGameTime);
        JsonElement ticks = fixture.GetProperty("source_ticks_after_each_host_step");
        long prior = 0;
        var noWake = new System.Collections.Generic.List<int>();
        for (int host = 1; host <= ticks.GetArrayLength(); host++)
        {
            game.Step(default(PlayerCommand));
            var state = (ISourceGameState)game.State;
            Program.Equal("57/60 actual host tick " + host, ticks[host - 1].GetInt64(), state.SourceTick);
            Program.Equal("host tick remains 60Hz " + host, (long)host, state.Tick);
            if (state.SourceTick == prior) noWake.Add(host);
            prior = state.SourceTick;
        }

        JsonElement expectedNoWake = fixture.GetProperty("host_ticks_without_source_wake");
        Program.Equal("skipped source wake count", expectedNoWake.GetArrayLength(), noWake.Count);
        for (int i = 0; i < noWake.Count && i < expectedNoWake.GetArrayLength(); i++)
            Program.Equal("skipped source wake host " + i, expectedNoWake[i].GetInt32(), noWake[i]);
        Program.Equal("retained source snapshot remains detached", 0L, initial.SourceTick);
        Program.Equal("exact source clock remainder after sixty host steps", 0, game.Model.SourceClockRemainder);

        // Isolate adapter action ordering from the original opening kickoff and snap grace period.
        game = Create(inputs);
        PrepareNormalPlaySelect(game);
        int ordinal = 0;
        foreach (JsonElement trace in fixture.GetProperty("edge_trace").EnumerateArray())
        {
            JsonElement force;
            if (trace.TryGetProperty("force_play_select_before", out force) && force.GetBoolean())
                game.Model.Phase = GamePhase.PlaySelect;
            InputAction action = (InputAction)System.Enum.Parse(typeof(InputAction), trace.GetProperty("action").GetString());
            game.Step(new PlayerCommand(action: action, play: ordinal == 0 ? PlayId.Run : PlayId.QuickPass));
            Program.Equal("latched action source tick " + ordinal,
                trace.GetProperty("expected_source_tick").GetInt64(), ((ISourceGameState)game.State).SourceTick);
            Program.Equal("latched action phase " + ordinal, trace.GetProperty("phase").GetString(), game.State.Phase.ToString());
            JsonElement expectedEvent = trace.GetProperty("event");
            Program.Equal("latched action event count " + ordinal,
                expectedEvent.ValueKind == JsonValueKind.Null ? 0 : 1, game.Events.Count);
            if (expectedEvent.ValueKind != JsonValueKind.Null && game.Events.Count > 0)
            {
                Program.Equal("latched action event " + ordinal, expectedEvent.GetString(), game.Events[0].Type.ToString());
                Program.Equal("latched event source identity " + ordinal,
                    ((ISourceGameState)game.State).SourceTick, game.Events[0].SourceTick);
            }
            if (ordinal == 1) Program.Equal("first queued edge was Run", PlayId.Run, game.State.HomePlay);
            if (ordinal == 2) Program.Equal("second queued edge was QuickPass", PlayId.QuickPass, game.State.HomePlay);
            ordinal++;
        }
        Program.Equal("host action fixture trace length", 4, ordinal);
        game = Create(inputs);
        PrepareNormalPlaySelect(game);
        game.Step(new PlayerCommand(action: InputAction.SelectPlay));
        game.Restart(1);
        PrepareNormalPlaySelect(game);
        game.Step(default(PlayerCommand));
        game.Step(default(PlayerCommand));
        Program.Equal("restart clears pending edge queue", GamePhase.PlaySelect, game.State.Phase);
        Program.Equal("restart clears pending edge events", 0, game.Events.Count);
        Program.Equal("restart resets host/source clock adapter", 1L, ((ISourceGameState)game.State).SourceTick);
#endif
    }

        internal static class RulesCallerChecks
        {
            private sealed class Scenario
            {
                internal readonly string Name, Play;
                internal readonly SourceBallKind Ball;
                internal readonly SourcePlayEndCause Cause;
                internal readonly SourceGameFlags Flags;
                internal readonly SourcePlayResult Result;
                internal readonly bool Return, Turnover;
                internal readonly int Los, First, Points;
                internal readonly SourcePlayType Next;
                internal readonly GameEventType[] Events;

                internal Scenario(string name, string play, SourceBallKind ball, SourcePlayEndCause cause,
                    SourceGameFlags flags, SourcePlayResult result, int los, int first,
                    GameEventType[] events, bool returned = false, bool turnover = false,
                    int points = 0, SourcePlayType next = SourcePlayType.Normal)
                {
                    Name = name; Play = play; Ball = ball; Cause = cause; Flags = flags;
                    Result = result; Los = los; First = first; Events = events;
                    Return = returned; Turnover = turnover; Points = points; Next = next;
                }
            }

            // Authenticated playsel.c f5e87b92... never promotes play_type for a
            // selected special. result.c eaf1201d...:136-235 classifies the ball under
            // PT_NORMAL; nfl.c 1626-1891 then commits the listed source result.
            private static readonly Scenario[] Scenarios = {
                new Scenario("field-goal-good", "play_field_goal", SourceBallKind.FieldGoal,
                    SourcePlayEndCause.DeadBallInBounds, SourceGameFlags.BallKicked | SourceGameFlags.ThroughUprights,
                    SourcePlayResult.FieldGoal, 60, 90, new GameEventType[0], points: 3, next: SourcePlayType.Kickoff),
                new Scenario("field-goal-missed", "play_field_goal", SourceBallKind.FieldGoal,
                    SourcePlayEndCause.DeadBallInBounds, SourceGameFlags.BallKicked,
                    SourcePlayResult.MissedKick, 40, 70, new[] { GameEventType.PossessionChanged }),
                new Scenario("field-goal-blocked-recovered", "play_field_goal", SourceBallKind.Fumble,
                    SourcePlayEndCause.CarrierTackledInBounds, SourceGameFlags.BallKicked | SourceGameFlags.Fumble,
                    SourcePlayResult.Down, 50, 80, new[] { GameEventType.TurnoverOnDowns, GameEventType.PossessionChanged },
                    turnover: true),
                new Scenario("field-goal-return", "play_field_goal", SourceBallKind.FieldGoal,
                    SourcePlayEndCause.CarrierTackledInBounds, SourceGameFlags.BallKicked | SourceGameFlags.PossessionChange,
                    SourcePlayResult.NewPossession, 50, 80, new GameEventType[0], returned: true),
                new Scenario("punt-downed", "play_punt", SourceBallKind.Punt,
                    SourcePlayEndCause.DeadBallInBounds, SourceGameFlags.BallPunted,
                    SourcePlayResult.DownedPunt, 50, 80, new[] { GameEventType.PossessionChanged }),
                new Scenario("punt-out-of-bounds", "play_punt", SourceBallKind.Punt,
                    SourcePlayEndCause.BallOutOfBounds, SourceGameFlags.BallPunted,
                    SourcePlayResult.PuntOutOfBounds, 50, 80,
                    new[] { GameEventType.OutOfBounds, GameEventType.PossessionChanged }),
                new Scenario("punt-downed-end-zone", "play_punt", SourceBallKind.Punt,
                    SourcePlayEndCause.DeadBallInBounds, SourceGameFlags.BallPunted,
                    SourcePlayResult.DownedPunt, 20, 50, new[] { GameEventType.PossessionChanged }),
                new Scenario("fake-field-goal-carrier", "play_fake_field_goal", SourceBallKind.Pass,
                    SourcePlayEndCause.CarrierTackledInBounds, SourceGameFlags.None,
                    SourcePlayResult.Down, 50, 80, new[] { GameEventType.TurnoverOnDowns, GameEventType.PossessionChanged },
                    turnover: true),
                new Scenario("fake-punt-carrier", "play_fake_punt", SourceBallKind.Pass,
                    SourcePlayEndCause.CarrierTackledInBounds, SourceGameFlags.None,
                    SourcePlayResult.Down, 50, 80, new[] { GameEventType.TurnoverOnDowns, GameEventType.PossessionChanged },
                    turnover: true)
            };

            internal static readonly List<object> Rows = new List<object>();
            internal static int CaseCount { get; private set; }

            private static T Bind<T>(GameSimulation game, string name)
            {
                return (T)(object)typeof(GameSimulation).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic,
                    null, typeof(T).GetMethod("Invoke").GetParameters().Select(p => p.ParameterType).ToArray(), null)
                    .CreateDelegate(typeof(T), game);
            }

            private static void Wake(GameSimulation game, SourcePlaySelection selection)
            {
                long tick = game.SourceTick;
                game.Step(new PlayerCommand(source: new SourceCommand(SourceAction.SelectPlay, selection)));
                if (game.SourceTick == tick) game.Step(default(PlayerCommand));
            }

            private static void Wake(GameSimulation game, SourceAction action)
            {
                long tick = game.SourceTick;
                game.Step(new PlayerCommand(source: new SourceCommand(action)));
                if (game.SourceTick == tick) game.Step(default(PlayerCommand));
            }

            private static void RunQuickKickCase(FullKernelInputs inputs, TeamId kicker)
            {
                string id = "StrictSource/" + kicker + "/quick-kick-clock";
                var configuration = new SourceMatchConfiguration(SourceSimulationProfile.OriginalRelease57Hz,
                    SourceStationLayout.TwoStation, new SourceTeamSelection(0, new[] { 0, 1, 2, 3, 4, 5, 6 }),
                    new SourceTeamSelection(1, new[] { 0, 1, 2, 3, 4, 5, 6 }),
                    new[] { new SourceStationConfiguration(0, TeamId.Home), new SourceStationConfiguration(1, TeamId.Away) },
                    TeamId.Home, SourceCaptainSlot.First);
                GameSimulation game = inputs.Create(configuration: configuration,
                    randomFactory: seed => new SourceInjectedRandom(site => 99,
                        new SourceRandomProvenance("quick-kick/raw99", "Declared between-play boundary, not target RNG",
                            "two-station", SourceRandomEvidence.ExternalUnverified)));
                for (int host = 0; host < SliceRules.TicksPerSecond; host++) game.Step(default(PlayerCommand));
                Program.Check(id + "/real-startup", game.SourceTick == 57 && game.Model.SourceClockRemainder == 0
                    && game.Model.SourceMovementCameraTick == game.SourceTick);
                SimulationState m = game.Model;
                m.Phase = GamePhase.DeadBall; m.SourceMatchPhase = SourceMatchPhase.PlayResult;
                m.SourceBeginPlayInitialized = false;
                Bind<Action<SourceRulesState>>(game, "SetSourceRules")(
                    new SourceRulesState((int)kicker, (int)kicker, 1, 20, 50, SourcePlayType.Kickoff));
                m.SourceClock = SourceClock.Create(0x3b00, 19, firstWakeTick: m.SourceTick + 1);
                m.ClockActive = false;
                Wake(game, SourceAction.Continue);
                long prepared = game.SourceTick;
                Program.Check(id + "/public-continue-first-receiver-countdown",
                    m.SourceKickoffKind == SourceKickoffKind.Quick && m.SourceRules.LineOfScrimmage == 40
                    && m.SourceRules.OffenseSide == 1 - (int)kicker
                    && m.Phase == GamePhase.PreSnap && m.PreSnapTicksRemaining == 2);
                Program.Check(id + "/pre-release-clock-paused", !m.ClockActive
                    && m.SourceClock.PackedTime == 0x3b00 && m.SourceClock.PlayTime == 19);
                Wake(game, SourceAction.None);
                Program.Check(id + "/second-receiver-countdown", m.Phase == GamePhase.PreSnap
                    && m.PreSnapTicksRemaining == 1 && !m.ClockActive);
                Wake(game, SourceAction.None);
                Program.Check(id + "/third-receiver-release", m.Phase == GamePhase.LiveBall
                    && m.PreSnapTicksRemaining == 0 && !m.SourceBallFrozen && m.CarrierId == -1
                    && m.Players[(1 - (int)kicker) * 7 + 3].SourceMode == SourceActorMode.Receiver);
                Program.Check(id + "/clock-start-follows-clock-process", m.ClockActive
                    && m.SourceClock.PackedTime == 0x3b00 && m.SourceClock.LocalPackedTime == 0x3b00
                    && m.SourceClock.PlayTime == 0 && game.SourceTick == prepared + 2);
                Program.Equal(id + "/quick-drop-does-not-invent-ball-kicked", SourceGameFlags.QuickKick, m.SourceFlags);
                Program.Check(id + "/published-live-clock-release", game.State.Phase == GamePhase.LiveBall
                    && ((ISourceGameState)game.State).ClockActive
                    && game.SourceEvents.Count(e => e.Type == SourceEventType.Kicked && e.SourceTick == prepared + 2) == 1);
                Wake(game, SourceAction.None);
                Program.Check(id + "/first-active-packed-clock", m.ClockActive
                    && m.SourceClock.PackedTime == 0x3a14 && m.SourceClock.PlayTime == 1
                    && ((ISourceGameState)game.State).PackedGameTime == 0x3a14u);
                Wake(game, SourceAction.None);
                Program.Check(id + "/clock-runs-before-catch-or-ground", m.ClockActive && m.CarrierId == -1
                    && m.SourceBallPosition.Y > 0 && m.SourceClock.PackedTime == 0x3a13);
                Rows.Add(new {
                    id, declared_between_play_boundary = true, source = "player.c:_mode_wpnps:2106-2127",
                    prepared_source_tick = prepared, actual_source_tick = game.SourceTick,
                    phase = m.Phase.ToString(), counter = m.PreSnapTicksRemaining, clock_active = m.ClockActive,
                    packed_time = m.SourceClock.PackedTime, play_time = m.SourceClock.PlayTime,
                    flags = (int)m.SourceFlags
                });
                CaseCount++;
            }

            private static void RunCase(FullKernelInputs inputs, SourceGameplayCompatibility policy,
                TeamId initial, Scenario scenario)
            {
                string id = policy + "/" + initial + "/" + scenario.Name;
                var configuration = new SourceMatchConfiguration(SourceSimulationProfile.OriginalRelease57Hz,
                    SourceStationLayout.TwoStation, new SourceTeamSelection(0, new[] { 0, 1, 2, 3, 4, 5, 6 }),
                    new SourceTeamSelection(1, new[] { 0, 1, 2, 3, 4, 5, 6 }),
                    new[] { new SourceStationConfiguration(0, TeamId.Home), new SourceStationConfiguration(1, TeamId.Away) },
                    TeamId.Home, SourceCaptainSlot.First);
                GameSimulation game = inputs.Create(configuration: configuration, compatibility: policy,
                    randomFactory: seed => new SourceInjectedRandom(site => 99,
                        new SourceRandomProvenance("rules-caller/raw99", "Declared result boundary, not target RNG",
                            "two-station", SourceRandomEvidence.ExternalUnverified)));
                for (int host = 0; host < SliceRules.TicksPerSecond; host++) game.Step(default(PlayerCommand));
                Program.Check(id + "/real-startup", game.SourceTick == 57 && game.Model.SourceClockRemainder == 0
                    && game.Model.SourceMovementCameraTick == game.SourceTick);

                SimulationState m = game.Model;
                m.Phase = GamePhase.PlaySelect; m.SourceMatchPhase = SourceMatchPhase.PlaySelection;
                m.SourceBeginPlayInitialized = false; m.SourceOffenseFlipped = false;
                Bind<Action<SourceRulesState>>(game, "SetSourceRules")(new SourceRulesState((int)initial, (int)initial, 4, 60, 90));
                Wake(game, new SourcePlaySelection(scenario.Play, SourcePlaybookKind.Offense));
                Wake(game, new SourcePlaySelection("play_safe_cover", SourcePlaybookKind.Defense));
                Program.Check(id + "/actual-selection-return", m.Phase == GamePhase.PreSnap
                    && m.SourceOffensePlay.Identity == scenario.Play && m.SourceDefensePlay.Identity == "play_safe_cover"
                    && m.SourceRules.OffenseSide == (int)initial && m.SourceRules.Down == 4
                    && m.SourceRules.LineOfScrimmage == 60 && m.SourceRules.FirstDown == 90);
                Program.Equal(id + "/selected-special-stays-normal", SourcePlayType.Normal, m.SourceRules.PlayType);

                // Declare only the result boundary after real public selection. These
                // cases do not claim that a human kicked, blocked or returned the ball.
                int side = scenario.Return ? 1 - (int)initial : (int)initial;
                if (scenario.Return)
                    Bind<Action<SourceRulesState>>(game, "SetSourceRules")(
                        new SourceRulesState(side, (int)initial, 4, 60, 90, m.SourceRules.PlayType));
                m.Phase = GamePhase.LiveBall; m.SourceMatchPhase = SourceMatchPhase.LiveBall;
                m.SourceLastPossession = side; m.SourceFlags = scenario.Flags; m.BallKind = scenario.Ball;
                m.SourceLastResult = SourcePlayResult.Touchdown;
                m.SourcePlayEndX = scenario.Name == "punt-downed-end-zone" ? (side == 0 ? 468f : -468f) : 0f;
                m.SourcePlayEndZ = 0f; m.SourceFumbleX = 0f;
                m.HomeScore = m.AwayScore = 0;
                Array.Clear(m.SourceQuarterScores, 0, m.SourceQuarterScores.Length);
                m.SourceClock = SourceClock.Create(0x3c07, firstWakeTick: m.SourceTick + 1);
                m.ClockActive = true; m.CarrierId = side * 7 + 3;
                SourceBallDriver.SetPosition(m, new SourceVector3(m.SourcePlayEndX, 0, 0));
                int typedBefore = game.SourceEvents.Count, legacyBefore = game.Events.Count;
                long draws = m.SourceRandomDraws, tickBefore = m.SourceTick, hostBefore = m.Tick;
                Bind<Action<SourcePlayEndCause, int, FieldVector, int, SourceVector3?>>(game, "EndSourcePlay")(
                    scenario.Cause, m.CarrierId, m.BallPosition, -1, null);
                Bind<Action>(game, "Publish")();

                int resolvedSide = scenario.Points != 0 ? (int)initial : 1 - (int)initial;
                Program.Check(id + "/classified-normal-result", m.SourceResultView.Status == SourceResultStatus.Classified
                    && m.SourceResultView.PlayKind == SourcePlayKind.Normal
                    && m.SourceResultView.Kind == (SourceResultKind)scenario.Result
                    && m.SourceResultView.Cause == (SourcePlayEndKind)scenario.Cause);
                Program.Check(id + "/source-spot-down-restart", m.SourceRules.OffenseSide == resolvedSide
                    && m.SourceRules.InitialOffenseSide == (int)initial
                    && m.SourceRules.Down == (scenario.Points != 0 ? 4 : 1)
                    && m.SourceRules.LineOfScrimmage == scenario.Los && m.SourceRules.FirstDown == scenario.First
                    && m.SourceRules.PlayType == scenario.Next);
                int home = initial == TeamId.Home ? scenario.Points : 0;
                int away = initial == TeamId.Away ? scenario.Points : 0;
                Program.Check(id + "/source-score-and-quarter-score", m.HomeScore == home && m.AwayScore == away
                    && m.SourceQuarterScores[0, 0] == home && m.SourceQuarterScores[0, 1] == away);
                Program.Check(id + "/source-flags-and-turnover", m.SourceFlags == scenario.Flags
                    && m.SourceResultView.TurnoverOnDowns == scenario.Turnover
                    && m.SourceLastResult == scenario.Result);
                Program.Check(id + "/clock-stop-and-fraction-discard", !m.ClockActive
                    && m.SourceClock.PackedTime == 0x3c00 && m.SourceClock.LocalPackedTime == 0x3c07
                    && m.Phase == GamePhase.DeadBall && m.SourceMatchPhase == SourceMatchPhase.PlayResult);
                SourceGameEvent[] typed = game.SourceEvents.Skip(typedBefore).ToArray();
                GameEvent[] legacy = game.Events.Skip(legacyBefore).ToArray();
                Program.Check(id + "/ordered-typed-result", typed.Length == 1
                    && typed[0].Type == SourceEventType.PlayResolved && typed[0].PlayKind == SourcePlayKind.Normal
                    && typed[0].Result.Kind == (SourceResultKind)scenario.Result
                    && typed[0].Result.Status == SourceResultStatus.Classified
                    && typed[0].ActorPlayerId == side * 7 + 3 && typed[0].Team == (TeamId)side
                    && typed[0].Tick == hostBefore && typed[0].SourceTick == tickBefore);
                Program.Check(id + "/ordered-legacy-result", legacy.Select(e => e.Type).SequenceEqual(scenario.Events)
                    && legacy.All(e => e.Tick == hostBefore && e.SourceTick == tickBefore));
                Program.Check(id + "/no-added-result-rng-or-source-wake",
                    m.SourceRandomDraws == draws && m.SourceTick == tickBefore && m.Tick == hostBefore);
                Rows.Add(new {
                    id, declared_result_boundary = true, selection_caller = "public Step -> FinishSourcePlaySelection",
                    result_caller = "EndSourcePlay -> ApplySourcePlayEnd", source_tick = tickBefore, host_tick = hostBefore,
                    selected_play = m.SourceOffensePlay.Identity, result = m.SourceLastResult.ToString(),
                    classifier = m.SourceResultView.Status.ToString(), ending_type = m.SourceResultView.PlayKind.ToString(),
                    next_type = m.SourceRules.PlayType.ToString(), offense = m.SourceRules.OffenseSide,
                    down = m.SourceRules.Down, los = m.SourceRules.LineOfScrimmage, first = m.SourceRules.FirstDown,
                    home = m.HomeScore, away = m.AwayScore, packed_time = m.SourceClock.PackedTime,
                    rng_delta = m.SourceRandomDraws - draws, typed_events = typed.Select(e => e.Type.ToString()).ToArray(),
                    legacy_events = legacy.Select(e => e.Type.ToString()).ToArray()
                });
                CaseCount++;
            }

            internal static void Run(FullKernelInputs inputs)
            {
                if (inputs == null || inputs.IsC2Profile)
                    throw new ArgumentException("Explicit actual Contact272 inputs, not a queued M1 qualification, are required.", nameof(inputs));
                foreach (SourceGameplayCompatibility policy in new[] { SourceGameplayCompatibility.StrictSource })
                foreach (TeamId initial in new[] { TeamId.Home, TeamId.Away })
                foreach (Scenario scenario in Scenarios)
                    RunCase(inputs, policy, initial, scenario);
                foreach (TeamId kicker in new[] { TeamId.Home, TeamId.Away }) RunQuickKickCase(inputs, kicker);
                Program.Equal("rules caller exact scenario inventory", 20, CaseCount);
            }
        }

#if PARITY_FULL_KERNEL
    private static GameSimulation Create(FullKernelInputs inputs)
    {
        if (!inputs.IsC2Profile) return inputs.Create(1);
        var configuration = new SourceMatchConfiguration(SourceSimulationProfile.OriginalRelease57Hz,
            SourceStationLayout.TwoStation, new SourceTeamSelection(0, new[] { 0, 1, 2, 3, 4, 5, 6 }),
            new SourceTeamSelection(1, new[] { 0, 1, 2, 3, 4, 5, 6 }),
            new[] { new SourceStationConfiguration(0, TeamId.Home) }, TeamId.Home, SourceCaptainSlot.First);
        return inputs.Create(1, configuration: configuration,
            presentation: new SourcePresentationSettings(TeamId.Home, new[] { -1, -1, -1, -1 }, 0));
    }

    private static void PrepareNormalPlaySelect(GameSimulation game)
    {
        game.Model.Phase = GamePhase.PlaySelect;
        game.Model.SourceRules = new SourceRulesState(0, 0, 1, 20, 50);
        game.Model.Possession = TeamId.Home;
        game.Model.SourceBallFrozen = false;
        game.Model.BallMode = BallMode.Held;
        game.Model.CarrierId = 3;
        game.Model.TargetPlayerId = -1;
        game.Model.ClockActive = false;
    }
#endif
}
