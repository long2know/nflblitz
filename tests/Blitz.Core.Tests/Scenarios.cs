using Blitz.Contracts;
using Blitz.Gameplay;
using Xunit;

namespace Blitz.Core.Tests
{
    internal static class Scenarios
    {
        // Unity's documented internal Model seam is visible because real sources are linked.
        internal static GameSimulation Live(TeamId offense, int down = 1, float los = 20f, float target = 50f)
        {
            var game = Check.NewGame();
            game.Model.Possession = offense;
            game.Model.Down = down;
            game.Model.LineOfScrimmage = los;
            game.Model.FirstDownLine = target;
            Check.Step(game, new PlayerCommand(action: InputAction.SelectPlay,
                play: offense == TeamId.Home ? PlayId.Run : PlayId.Cover));
            Assert.Equal(GamePhase.PreSnap, game.State.Phase);
            if (offense == TeamId.Home)
            {
                Check.Step(game, new PlayerCommand(action: InputAction.Snap));
            }
            else
            {
                for (int i = 0; i < 60; i++) Check.Step(game);
            }
            Assert.Equal(GamePhase.LiveBall, game.State.Phase);
            game.Model.AwayPlay = PlayId.Run;
            return game;
        }

        internal static void CarrierAt(GameSimulation game, float x, float z = 0f)
        {
            var model = game.Model;
            Assert.Equal(BallMode.Held, model.BallMode);
            model.Players[model.CarrierId].Position = new FieldVector(x, z);
            model.Players[model.CarrierId].Velocity = default(FieldVector);
            model.BallPosition = new FieldVector(x, z);
        }

        internal static void ParkOtherActors(GameSimulation game)
        {
            var model = game.Model;
            float farX = model.Players[model.CarrierId].Position.X < 50 ? 95 : 5;
            for (int id = 0; id < 14; id++)
            {
                if (id == model.CarrierId) continue;
                var position = new FieldVector(farX, (id % 7 - 3) * 3f);
                model.Players[id].Position = position;
                model.Players[id].SnapPosition = position;
                model.Players[id].Velocity = default(FieldVector);
            }
        }

        internal static void OutOfBoundsAt(GameSimulation game, float x)
        {
            CarrierAt(game, x, SliceRules.FieldHalfWidthYards);
            ParkOtherActors(game);
            Check.Step(game);
            Assert.Equal(GamePhase.DeadBall, game.State.Phase);
        }

        internal static GameSimulation DefensiveReach(float distance)
        {
            var game = Live(TeamId.Away, 1, 80f, 50f);
            CarrierAt(game, 75f);
            ParkOtherActors(game);
            game.Model.ControlledPlayerId = 3;
            game.Model.Players[3].Position = new FieldVector(75f, distance);
            game.Model.Players[3].SnapPosition = game.Model.Players[3].Position;
            return game;
        }
    }
}
