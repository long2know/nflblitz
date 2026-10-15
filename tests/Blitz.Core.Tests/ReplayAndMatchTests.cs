using System;
using System.Collections.Generic;
using Blitz.Contracts;
using Xunit;

namespace Blitz.Core.Tests
{
    public sealed class ReplayAndMatchTests
    {
        [Theory]
        [InlineData(0)]
        [InlineData(41)]
        [InlineData(-173)]
        public void SameSeedCommandStreamMatchesEveryStateAndOrderedEventFieldEachTick(int seed)
        {
            var first = Check.NewGame(seed);
            var second = Check.NewGame(seed);
            var seenPhases = new HashSet<GamePhase>();
            var seenEvents = new HashSet<GameEventType>();
            int liveTick = 0;
            Check.SameFields(Check.StateFields(first.State), Check.StateFields(second.State), "initial replay");
            for (int tick = 0; tick < 200000 && first.State.Phase != GamePhase.GameOver; tick++)
            {
                seenPhases.Add(first.State.Phase);
                if (first.State.Phase != GamePhase.LiveBall) liveTick = 0;
                var command = MatchCommand(first.State, liveTick++);
                Check.Step(first, command);
                Check.Step(second, command);
                Check.SameFields(Check.StateFields(first.State), Check.StateFields(second.State), "state replay tick " + tick);
                Check.SameFields(Check.EventFields(first.Events), Check.EventFields(second.Events), "event replay tick " + tick);
                foreach (var item in first.Events) seenEvents.Add(item.Type);
            }
            Assert.Equal(GamePhase.GameOver, first.State.Phase);
            seenPhases.Add(first.State.Phase);
            Assert.Equal(5, seenPhases.Count);
            Assert.Contains(GameEventType.Snapped, seenEvents);
            Assert.Contains(GameEventType.PassThrown, seenEvents);
            Assert.Contains(GameEventType.PassCaught, seenEvents);
            Assert.Contains(GameEventType.PossessionChanged, seenEvents);
            Assert.Contains(GameEventType.QuarterEnded, seenEvents);
            Assert.Contains(GameEventType.GameEnded, seenEvents);
            first.Restart(seed);
            second.Restart(seed);
            Check.SameFields(Check.StateFields(first.State), Check.StateFields(second.State), "replay restart state");
            Check.SameFields(Check.EventFields(first.Events), Check.EventFields(second.Events), "replay restart events");
            for (int tick = 0; tick < 600; tick++)
            {
                var command = MatchCommand(first.State, tick);
                Check.Step(first, command);
                Check.Step(second, command);
                Check.SameFields(Check.StateFields(first.State), Check.StateFields(second.State), "post-restart replay " + tick);
                Check.SameFields(Check.EventFields(first.Events), Check.EventFields(second.Events), "post-restart events " + tick);
            }
        }

        [Fact]
        public void FourQuartersEndExactlyOnceAndFinalStateOnlyAdvancesTickUntilRestart()
        {
            var game = Check.NewGame();
            var endedQuarters = new List<int>();
            int gameEndedCount = 0;
            int liveTick = 0;
            for (int i = 0; i < 200000 && game.State.Phase != GamePhase.GameOver; i++)
            {
                var before = game.State;
                if (before.Phase != GamePhase.LiveBall) liveTick = 0;
                Check.Step(game, MatchCommand(before, liveTick++));
                int expectedClock = before.Phase == GamePhase.LiveBall
                    ? Math.Max(0, before.QuarterTicksRemaining - 1)
                    : before.QuarterTicksRemaining;
                if (before.Phase == GamePhase.DeadBall && before.QuarterTicksRemaining == 0 && before.Quarter < 4)
                {
                    Assert.Equal(before.Quarter + 1, game.State.Quarter);
                    Assert.Equal(7200, game.State.QuarterTicksRemaining);
                    Assert.Equal(before.Possession, game.State.Possession);
                    Assert.Equal(before.LineOfScrimmage, game.State.LineOfScrimmage);
                    Assert.Equal(before.Down, game.State.Down);
                    Assert.Equal(before.FirstDownLine, game.State.FirstDownLine);
                }
                else
                {
                    Assert.Equal(expectedClock, game.State.QuarterTicksRemaining);
                }
                foreach (var item in game.Events)
                {
                    if (item.Type == GameEventType.QuarterEnded)
                    {
                        Assert.Equal(GamePhase.DeadBall, game.State.Phase);
                        Assert.Equal(0, game.State.QuarterTicksRemaining);
                        Assert.Equal(game.State.Quarter, item.Value);
                        Assert.Equal(TeamId.Home, item.Team);
                        Assert.Equal(-1, item.PlayerId);
                        endedQuarters.Add(item.Value);
                    }
                    if (item.Type == GameEventType.GameEnded)
                    {
                        gameEndedCount++;
                        Assert.Equal(GamePhase.GameOver, game.State.Phase);
                        Assert.Equal(TeamId.Home, item.Team);
                        Assert.Equal(-1, item.PlayerId);
                        Assert.Equal(Math.Sign(game.State.HomeScore - game.State.AwayScore), item.Value);
                    }
                }
            }
            Assert.Equal(GamePhase.GameOver, game.State.Phase);
            Assert.Equal(new[] { 1, 2, 3, 4 }, endedQuarters);
            Assert.Equal(1, gameEndedCount);
            Assert.Equal(4, game.State.Quarter);
            Assert.Equal(0, game.State.QuarterTicksRemaining);
            var final = game.State;
            var finalFields = Check.StateFields(final);
            foreach (InputAction action in Enum.GetValues(typeof(InputAction)))
            {
                Check.Step(game, new PlayerCommand(new FieldVector(float.MaxValue, float.NaN), true, action, PlayId.Run, 4));
                var current = Check.StateFields(game.State);
                current[0] = finalFields[0];
                Check.SameFields(finalFields, current, "frozen final " + action);
                Assert.Empty(game.Events);
            }
            game.Restart(41);
            Check.SameFields(Check.StateFields(Check.NewGame(41).State), Check.StateFields(game.State), "final restart");
            Assert.Empty(game.Events);
        }

        internal static PlayerCommand MatchCommand(IGameState state, int liveTick)
        {
            switch (state.Phase)
            {
                case GamePhase.PlaySelect:
                    return new PlayerCommand(action: InputAction.SelectPlay,
                        play: state.Possession == TeamId.Home ? PlayId.QuickPass : PlayId.Blitz);
                case GamePhase.PreSnap:
                    return new PlayerCommand(action: state.Possession == TeamId.Home ? InputAction.Snap : InputAction.None);
                case GamePhase.DeadBall:
                    return new PlayerCommand(action: InputAction.Continue);
                case GamePhase.LiveBall:
                    if (state.Possession == TeamId.Home)
                    {
                        if (state.Ball.Mode == BallMode.Held && state.Ball.CarrierId == 3 && liveTick == 1)
                        {
                            return new PlayerCommand(action: InputAction.Pass, targetPlayerId: 4);
                        }
                        return new PlayerCommand(new FieldVector(1, liveTick % 240 < 120 ? 0.35f : -0.35f),
                            liveTick % 180 < 120, action: liveTick % 89 == 0 ? (InputAction)999 : InputAction.None);
                    }
                    var controlled = state.Players[state.ControlledPlayerId];
                    return new PlayerCommand(new FieldVector(
                        state.Ball.Position.X - controlled.Position.X,
                        state.Ball.Position.Z - controlled.Position.Z), liveTick % 180 < 100,
                        liveTick % 61 == 0 ? InputAction.SwitchDefender : InputAction.Tackle);
                default:
                    return default(PlayerCommand);
            }
        }
    }
}
