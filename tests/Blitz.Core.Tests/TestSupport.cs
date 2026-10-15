using System;
using System.Collections.Generic;
using System.Globalization;
using Blitz.Contracts;
using Blitz.Gameplay;
using Xunit;

namespace Blitz.Core.Tests
{
    internal static class Check
    {
        internal static GameSimulation NewGame(int seed = 41)
        {
            var game = new GameSimulation(seed);
            ValidState(game.State);
            Assert.Empty(game.Events);
            return game;
        }

        internal static void Step(IGameSimulation game, PlayerCommand command = default(PlayerCommand))
        {
            long tick = game.State.Tick;
            game.Step(command);
            Assert.Equal(tick + 1, game.State.Tick);
            ValidState(game.State);
            foreach (var item in game.Events)
            {
                Assert.Equal(game.State.Tick, item.Tick);
                Assert.True(Enum.IsDefined(typeof(GameEventType), item.Type), "Unknown event type.");
                Assert.True(item.Team == TeamId.Home || item.Team == TeamId.Away, "Invalid event team.");
                Assert.InRange(item.PlayerId, -1, 13);
                Finite(item.Position.X, "event.Position.X");
                Finite(item.Position.Z, "event.Position.Z");
            }
        }

        internal static void SelectAndSnap(IGameSimulation game, PlayId homePlay = PlayId.Run)
        {
            Assert.Equal(GamePhase.PlaySelect, game.State.Phase);
            bool homeOffense = game.State.Possession == TeamId.Home;
            var play = homeOffense ? homePlay : PlayId.Cover;
            Step(game, new PlayerCommand(action: InputAction.SelectPlay, play: play));
            Assert.Equal(GamePhase.PreSnap, game.State.Phase);
            if (homeOffense)
            {
                Step(game, new PlayerCommand(action: InputAction.Snap));
            }
            else
            {
                for (int i = 0; i < 60; i++)
                {
                    Step(game);
                }
            }
            Assert.Equal(GamePhase.LiveBall, game.State.Phase);
        }

        internal static void FinishPlay(IGameSimulation game)
        {
            for (int i = 0; i < 3600 && game.State.Phase == GamePhase.LiveBall; i++)
            {
                Step(game, new PlayerCommand(move: new FieldVector(0, 1)));
            }
            Assert.Equal(GamePhase.DeadBall, game.State.Phase);
        }

        internal static void GiveAwayPossession(IGameSimulation game)
        {
            for (int play = 0; play < 4 && game.State.Possession == TeamId.Home; play++)
            {
                SelectAndSnap(game);
                FinishPlay(game);
                Assert.NotEqual(PlayOutcome.FirstDown, game.State.LastOutcome);
                Step(game, new PlayerCommand(action: InputAction.Continue));
                Assert.Equal(GamePhase.PlaySelect, game.State.Phase);
            }
            Assert.Equal(TeamId.Away, game.State.Possession);
            Assert.Equal(-1, game.State.OffenseDirection);
        }

        internal static PlayerView Player(IGameState state, int id)
        {
            Assert.InRange(id, 0, state.Players.Count - 1);
            Assert.Equal(id, state.Players[id].Id);
            return state.Players[id];
        }

        internal static void Finite(float value, string field)
        {
            Assert.False(float.IsNaN(value) || float.IsInfinity(value), field + " must be finite.");
        }

        internal static void Near(float expected, float actual, float tolerance = 0.00001f)
        {
            Assert.True(Math.Abs(expected - actual) <= tolerance,
                "Expected " + expected.ToString("R", CultureInfo.InvariantCulture) +
                " +/- " + tolerance.ToString("R", CultureInfo.InvariantCulture) +
                ", observed " + actual.ToString("R", CultureInfo.InvariantCulture));
        }

        internal static void ValidState(IGameState state)
        {
            Assert.NotNull(state);
            Assert.True(Enum.IsDefined(typeof(GamePhase), state.Phase), "Invalid phase.");
            Assert.True(state.Possession == TeamId.Home || state.Possession == TeamId.Away, "Invalid possession.");
            Assert.Equal(state.Possession == TeamId.Home ? 1 : -1, state.OffenseDirection);
            Assert.InRange(state.Tick, 0, long.MaxValue);
            Assert.InRange(state.PlayNumber, 0, int.MaxValue);
            Assert.InRange(state.Quarter, 1, 4);
            Assert.InRange(state.QuarterTicksRemaining, 0, 7200);
            Assert.InRange(state.PreSnapTicksRemaining, 0, 300);
            Assert.InRange(state.Down, 1, 4);
            Assert.InRange(state.LineOfScrimmage, 1f, 99f);
            Assert.InRange(state.FirstDownLine, 0f, 100f);
            Assert.InRange(state.ControlledPlayerId, 0, 6);
            Assert.InRange(state.HomeScore, 0, int.MaxValue);
            Assert.InRange(state.AwayScore, 0, int.MaxValue);
            Assert.Equal(14, state.Players.Count);
            for (int i = 0; i < 14; i++)
            {
                var player = state.Players[i];
                Assert.Equal(i, player.Id);
                Assert.Equal(i < 7 ? TeamId.Home : TeamId.Away, player.Team);
                Finite(player.Position.X, "player[" + i + "].Position.X");
                Finite(player.Position.Z, "player[" + i + "].Position.Z");
                Finite(player.Velocity.X, "player[" + i + "].Velocity.X");
                Finite(player.Velocity.Z, "player[" + i + "].Velocity.Z");
                Finite(player.Turbo01, "player[" + i + "].Turbo01");
                Assert.InRange(player.Turbo01, 0f, 1f);
            }
            var ball = state.Ball;
            Assert.True(Enum.IsDefined(typeof(BallMode), ball.Mode), "Invalid ball mode.");
            Finite(ball.Position.X, "Ball.Position.X");
            Finite(ball.Position.Z, "Ball.Position.Z");
            Finite(ball.Height, "Ball.Height");
            Assert.True(ball.Height >= 0, "Ball.Height must not be negative.");
            if (ball.Mode == BallMode.Held)
            {
                Assert.InRange(ball.CarrierId, 0, 13);
                Assert.Equal(state.Possession, state.Players[ball.CarrierId].Team);
                Assert.Equal(state.Players[ball.CarrierId].Position.X, ball.Position.X);
                Assert.Equal(state.Players[ball.CarrierId].Position.Z, ball.Position.Z);
                if (state.Possession == TeamId.Home)
                {
                    Assert.Equal(ball.CarrierId, state.ControlledPlayerId);
                }
            }
            else
            {
                Assert.Equal(-1, ball.CarrierId);
            }
            if (ball.Mode == BallMode.InFlight)
            {
                Assert.InRange(ball.TargetPlayerId, 0, 13);
                Assert.Equal(state.Possession, state.Players[ball.TargetPlayerId].Team);
                Assert.InRange(ball.TargetPlayerId % 7, 4, 6);
                if (state.Possession == TeamId.Home)
                {
                    Assert.Equal(3, state.ControlledPlayerId);
                }
            }
            else
            {
                Assert.Equal(-1, ball.TargetPlayerId);
            }
        }

        internal static List<string> StateFields(IGameState state)
        {
            var fields = new List<string>
            {
                "Tick=" + state.Tick,
                "PlayNumber=" + state.PlayNumber,
                "Phase=" + (int)state.Phase,
                "Possession=" + (int)state.Possession,
                "OffenseDirection=" + state.OffenseDirection,
                "Quarter=" + state.Quarter,
                "QuarterTicksRemaining=" + state.QuarterTicksRemaining,
                "PreSnapTicksRemaining=" + state.PreSnapTicksRemaining,
                "Down=" + state.Down,
                "LineOfScrimmage=" + Bits(state.LineOfScrimmage),
                "FirstDownLine=" + Bits(state.FirstDownLine),
                "HomeScore=" + state.HomeScore,
                "AwayScore=" + state.AwayScore,
                "ControlledPlayerId=" + state.ControlledPlayerId,
                "HomePlay=" + (int)state.HomePlay,
                "AwayPlay=" + (int)state.AwayPlay,
                "LastOutcome=" + (int)state.LastOutcome,
                "Ball.Mode=" + (int)state.Ball.Mode,
                "Ball.Position.X=" + Bits(state.Ball.Position.X),
                "Ball.Position.Z=" + Bits(state.Ball.Position.Z),
                "Ball.Height=" + Bits(state.Ball.Height),
                "Ball.CarrierId=" + state.Ball.CarrierId,
                "Ball.TargetPlayerId=" + state.Ball.TargetPlayerId,
                "Players.Count=" + state.Players.Count
            };
            foreach (var player in state.Players)
            {
                string name = "Players[" + player.Id + "].";
                fields.Add(name + "Id=" + player.Id);
                fields.Add(name + "Team=" + (int)player.Team);
                fields.Add(name + "Position.X=" + Bits(player.Position.X));
                fields.Add(name + "Position.Z=" + Bits(player.Position.Z));
                fields.Add(name + "Velocity.X=" + Bits(player.Velocity.X));
                fields.Add(name + "Velocity.Z=" + Bits(player.Velocity.Z));
                fields.Add(name + "Turbo01=" + Bits(player.Turbo01));
            }
            return fields;
        }

        internal static List<string> EventFields(IReadOnlyList<GameEvent> events)
        {
            var fields = new List<string> { "Events.Count=" + events.Count };
            for (int i = 0; i < events.Count; i++)
            {
                var item = events[i];
                string name = "Events[" + i + "].";
                fields.Add(name + "Tick=" + item.Tick);
                fields.Add(name + "Type=" + (int)item.Type);
                fields.Add(name + "Team=" + (int)item.Team);
                fields.Add(name + "PlayerId=" + item.PlayerId);
                fields.Add(name + "Value=" + item.Value);
                fields.Add(name + "Position.X=" + Bits(item.Position.X));
                fields.Add(name + "Position.Z=" + Bits(item.Position.Z));
            }
            return fields;
        }

        internal static void SameFields(IReadOnlyList<string> expected, IReadOnlyList<string> actual, string context)
        {
            Assert.True(expected.Count == actual.Count,
                context + ": expected " + expected.Count + " fields, actual " + actual.Count);
            for (int i = 0; i < expected.Count; i++)
            {
                Assert.True(expected[i] == actual[i],
                    context + " field " + i + ": expected " + expected[i] + ", actual " + actual[i]);
            }
        }

        internal static void ActorsUnmoved(IGameState expected, IGameState actual)
        {
            for (int i = 0; i < 14; i++)
            {
                Assert.Equal(expected.Players[i].Position.X, actual.Players[i].Position.X);
                Assert.Equal(expected.Players[i].Position.Z, actual.Players[i].Position.Z);
            }
            Assert.Equal(expected.Ball.Position.X, actual.Ball.Position.X);
            Assert.Equal(expected.Ball.Position.Z, actual.Ball.Position.Z);
            Assert.Equal(expected.Ball.Height, actual.Ball.Height);
        }

        internal static void EventTypes(IReadOnlyList<GameEvent> events, params GameEventType[] expected)
        {
            Assert.Equal(expected.Length, events.Count);
            for (int i = 0; i < expected.Length; i++)
            {
                Assert.Equal(expected[i], events[i].Type);
            }
        }

        private static string Bits(float value)
        {
            return BitConverter.SingleToInt32Bits(value).ToString("X8", CultureInfo.InvariantCulture);
        }
    }
}
