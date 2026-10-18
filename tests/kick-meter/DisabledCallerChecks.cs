using System;
using System.Collections.Generic;
using Blitz.Contracts;
using Blitz.Gameplay;
using Blitz.Runtime;

internal static class DisabledCallerChecks
{
    private static int assertions;
    private static void Check(bool value, string identity)
    {
        assertions++;
        if (!value) throw new InvalidOperationException("DISABLED817: " + identity);
    }
    private static SourceInputFrame Input(SourceButtons held = SourceButtons.None, SourceButtons pressed = SourceButtons.None)
    { return new SourceInputFrame(new[] { new SourceStationInput(3, held: held, pressed: pressed) }); }
    private static void Wake(GameSimulation game, PlayerCommand command)
    {
        long before = game.SourceTick;
        game.Step(command);
        if (game.SourceTick == before) game.Step(new PlayerCommand(stationInputs: Input()));
        Check(game.SourceTick == before + 1, "observe exactly one executing57Hz source wake");
    }
    internal static int Run(FullKernelInputs[] inputs)
    {
        for (int profile = 0; profile < 3; profile++)
        foreach (TeamId side in new[] { TeamId.Home, TeamId.Away })
        foreach (bool punt in new[] { false, true })
        {
            var configuration = Program.Configuration(side);
            GameSimulation game = Program.Factory(inputs[profile], profile, configuration, null);
            Program.PrepareInitialMatchBoundary(game, side, punt);
            Wake(game, new PlayerCommand(source: new SourceCommand(SourceAction.SelectPlay,
                new SourcePlaySelection(punt ? "play_punt" : "play_field_goal", SourcePlaybookKind.Offense)),
                stationInputs: Input()));
            Check(game.State.Phase == GamePhase.PreSnap, "actual original selection reaches special-teams lineup");
            Check(game.Model.SourceRules.PlayType == (punt ? SourcePlayType.Punt : SourcePlayType.FieldGoal),
                "actual play flags establish the expected production play kind");
            for (int tick = 0; tick < 60; tick++) Wake(game, new PlayerCommand(stationInputs: Input()));
            int quarterback = (int)side * 7 + 3;
            Check(game.Model.SourceStationPlayers[3] == quarterback, "actual station owns special-teams quarterback");
            Check(game.Model.PreSnapTicksRemaining > 0 && game.Model.PreSnapTicksRemaining < 275,
                "real countdown has passed original ten-tick gate before auto-hike");
            string guard = null;
            long attempted = game.SourceTick + 1;
            try { Wake(game, new PlayerCommand(stationInputs: Input(SourceButtons.B, SourceButtons.B))); }
            catch (NotSupportedException error) { guard = error.Message; }
            Check(guard != null && guard.StartsWith("Manual source kick/punt meter needs plyrinfo.c k_bar/k_marker widths, "),
                "default gate rejects the actual raw-button caller, not constructor/input/compiler");
            Check(game.SourceTick == attempted
                && (game.Model.Players[quarterback].SourceNewButtons & SourceDroneButtons.B) != 0,
                "fresh B reaches actual actor source wake");
            Check(game.Model.Phase == GamePhase.PreSnap && (game.Model.SourceFlags & SourceGameFlags.HikingBall) == 0
                && game.Model.CarrierId == (int)side * 7 + 1 && game.SourceEvents.Count == 0,
                "missing strict native metrics cannot silently produce a fake snap or flight");
        }
        var legacy = new GameSimulation(27);
        legacy.Step(new PlayerCommand(action: InputAction.SelectPlay, play: PlayId.Run));
        legacy.Step(new PlayerCommand(action: InputAction.Snap));
        Check(legacy.State.Phase == GamePhase.LiveBall && legacy.State.Tick == 2, "unchanged Legacy60 select/snap");
        if (assertions != 817) throw new InvalidOperationException("The retained disabled817 inventory changed: " + assertions);
        return assertions;
    }
}
