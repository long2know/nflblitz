using System;
using System.Linq;
using Blitz.Contracts;
using Blitz.Gameplay;

internal static partial class Program
{
    private static void InputAndStationCallers()
    {
        GameSimulation game = Create();
        int calls = 0;
        SourceStationInput observed = default(SourceStationInput);
        game.SourceStationInputTransform = (snapshot, tick, frame) =>
        {
            calls++;
            Check(((ISourceMatchState)snapshot).SourceTick == tick, "M1 retains actual executing-tick input transform");
            observed = frame.Stations[0];
            return frame;
        };
        game.Step(Raw(SourceButtons.A | SourceButtons.B | SourceButtons.C, SourceButtons.A | SourceButtons.B | SourceButtons.C));
        Check(calls == 0, "M1 skipped host wake does not consume B1 queued raw edges");
        game.Step(Raw(SourceButtons.None, released: SourceButtons.A | SourceButtons.B | SourceButtons.C));
        Check(calls == 1 && observed.Held == SourceButtons.None
            && observed.Pressed == (SourceButtons.A | SourceButtons.B | SourceButtons.C)
            && observed.Released == observed.Pressed, "M1 retains complete queued tap/ABC combination masks without priority collapse");
        game.Step(Raw(SourceButtons.None));
        Check(calls == 2 && observed.Pressed == SourceButtons.None && observed.Released == SourceButtons.None,
            "M1 raw edges execute once, not per render or repeated source wake");
        game.Restart();
        game.Step(Raw(SourceButtons.B, SourceButtons.B));
        ((IPendingSourceInput)game).ClearPendingInput();
        game.Step(default(PlayerCommand));
        Check(observed.Pressed == SourceButtons.None && observed.Held == SourceButtons.None,
            "M1 strong focus clear still flushes pending B1 edges/held sample");

        game = Create(settings: new SourceMovementSettings(0, 1, true, 1, 2, false));
        SourceMovementContext home = Bind<Func<ActorState, SourceMovementContext>>(game, "MovementContext")(game.Model.Players[0]);
        SourceMovementContext away = Bind<Func<ActorState, SourceMovementContext>>(game, "MovementContext")(game.Model.Players[7]);
        Check(home.SpeedPowerup && away.SpeedPowerup, "actual pup_speed remains GLOBAL, not a team mask");
        Check(home.FastTurboPowerup && !away.FastTurboPowerup, "actual pup_fastturborun selects executing TEAM bit");
        Check(!home.RunOutOfBoundsPowerup && away.RunOutOfBoundsPowerup, "actual pup_runob selects executing TEAM bit");
        game.Model.Phase = GamePhase.LiveBall;
        game.Model.SourceStationTurbo[0] = 57f;
        Bind<Action<PlayerCommand>>(game, "UpdateSourceStationMovementResources")(Raw(SourceButtons.C));
        Check(B(game.Model.SourceStationTurbo[0]) == B(57f),
            "actual station infinite-turbo branch freezes resource, does not invent refill");
        game = Create();
        game.Model.Phase = GamePhase.LiveBall;
        game.Model.SourceTeamFire[0] = true;
        game.Model.SourceStationTurbo[0] = 57f;
        Bind<Action<PlayerCommand>>(game, "UpdateSourceStationMovementResources")(Raw(SourceButtons.C));
        Check(B(game.Model.SourceStationTurbo[0]) == B(57f), "actual station fire branch freezes resource");
        game.Model.SourceTeamFire[0] = false;
        Bind<Action<PlayerCommand>>(game, "UpdateSourceStationMovementResources")(Raw(SourceButtons.C));
        Check(B(game.Model.SourceStationTurbo[0]) == B(57f - .35f),
            "actual shared station resource depletes once, not once per fourteen actors");
        game.Model.Possession = TeamId.Away;
        float before = game.Model.SourceStationTurbo[0];
        float expected = Math.Min(100f, before + .35f);
        expected = Math.Min(100f, expected + .15f);
        Bind<Action<PlayerCommand>>(game, "UpdateSourceStationMovementResources")(Raw(SourceButtons.None));
        Check(B(game.Model.SourceStationTurbo[0]) == B(expected),
            "actual defending station recharge preserves separately clamped original additions");
    }
}
