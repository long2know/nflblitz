using System.Text.Json;
#if PARITY_FULL_KERNEL
using Blitz.Contracts;
using Blitz.Gameplay;
#endif

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
        var game = inputs.Create(1);
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
        game = inputs.Create(1);
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
        game = inputs.Create(1);
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

#if PARITY_FULL_KERNEL
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
