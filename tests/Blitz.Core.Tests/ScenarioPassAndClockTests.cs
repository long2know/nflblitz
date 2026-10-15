using Blitz.Contracts;
using Xunit;

namespace Blitz.Core.Tests
{
    public sealed class ScenarioPassAndClockTests
    {
        [Theory]
        [InlineData(TeamId.Home, 2, 22f, 52f, TeamId.Home, 3, 52f)]
        [InlineData(TeamId.Away, 2, 78f, 48f, TeamId.Away, 3, 48f)]
        [InlineData(TeamId.Home, 4, 22f, 52f, TeamId.Away, 1, 0f)]
        [InlineData(TeamId.Away, 4, 78f, 48f, TeamId.Home, 1, 100f)]
        public void ActualIncompleteKeepsSnapLosAndFourthDownTransfersInBothDirections(
            TeamId offense, int down, float los, float target, TeamId next, int nextDown, float nextTarget)
        {
            var game = Scenarios.Live(offense, down, los, target);
            game.Model.AwayPlay = PlayId.QuickPass;
            game.Model.AwayPassDecisionTick = 1;
            Check.Step(game, new PlayerCommand(action: InputAction.Pass,
                targetPlayerId: offense == TeamId.Home ? 4 : 11));
            Assert.Equal(BallMode.InFlight, game.State.Ball.Mode);
            Assert.InRange(game.Model.PassTicksTotal, 22, 66);
            int receiver = game.State.Ball.TargetPlayerId;
            var fixedLead = game.Model.PassTarget;
            var miss = new FieldVector(offense == TeamId.Home ? 105f : -5f, 0);
            game.Model.Players[receiver].Position = miss;
            game.Model.Players[receiver].SnapPosition = miss;
            for (int i = 0; i < 100 && game.State.Phase == GamePhase.LiveBall; i++)
            {
                Check.Step(game);
            }
            Assert.Equal(GamePhase.DeadBall, game.State.Phase);
            Assert.Equal(los, game.State.LineOfScrimmage);
            Assert.Equal(next, game.State.Possession);
            Assert.Equal(nextDown, game.State.Down);
            Assert.Equal(nextTarget, game.State.FirstDownLine);
            Assert.NotEqual(los, game.State.Ball.Position.X);
            Check.Near(fixedLead.X, game.State.Ball.Position.X);
            Check.Near(fixedLead.Z, game.State.Ball.Position.Z);
            Assert.Equal(BallMode.Dead, game.State.Ball.Mode);
            Assert.Equal(-1, game.State.Ball.CarrierId);
            Assert.Equal(-1, game.State.Ball.TargetPlayerId);
            Assert.Equal(0f, game.State.Ball.Height);
            Assert.Equal(down == 4 ? PlayOutcome.TurnoverOnDowns : PlayOutcome.Incomplete, game.State.LastOutcome);
            if (down == 4)
            {
                Check.EventTypes(game.Events, GameEventType.Incomplete, GameEventType.TurnoverOnDowns, GameEventType.PossessionChanged);
                Assert.Equal(next, game.Events[1].Team);
                Assert.Equal(next, game.Events[2].Team);
                Assert.Equal(los, game.Events[2].Position.X);
            }
            else
            {
                Check.EventTypes(game.Events, GameEventType.Incomplete);
            }
            Assert.Equal(offense, game.Events[0].Team);
            Assert.Equal(receiver, game.Events[0].PlayerId);
            Assert.Equal(0, game.State.HomeScore);
            Assert.Equal(0, game.State.AwayScore);
        }

        [Theory]
        [InlineData(TeamId.Home)]
        [InlineData(TeamId.Away)]
        public void PassAtSnapLosIsLegalButUsedForwardPassCannotBeRepeatedByQuarterback(TeamId offense)
        {
            var game = Scenarios.Live(offense, 1, 50f, offense == TeamId.Home ? 80f : 20f);
            Scenarios.CarrierAt(game, 50f);
            game.Model.AwayPlay = PlayId.QuickPass;
            game.Model.AwayPassDecisionTick = 1;
            // Away's ready quarterback backpedals on this tick, keeping it legally behind LOS.
            Check.Step(game, new PlayerCommand(action: InputAction.Pass, targetPlayerId: 4));
            Assert.Equal(BallMode.InFlight, game.State.Ball.Mode);
            Check.EventTypes(game.Events, GameEventType.PassThrown);

            var used = Scenarios.Live(offense, 1, 50f, offense == TeamId.Home ? 80f : 20f);
            used.Model.ForwardPassUsed = true;
            used.Model.AwayPlay = PlayId.QuickPass;
            used.Model.AwayPassDecisionTick = 1;
            Check.Step(used, new PlayerCommand(action: InputAction.Pass, targetPlayerId: 4));
            Assert.NotEqual(BallMode.InFlight, used.State.Ball.Mode);
            Assert.DoesNotContain(used.Events, item => item.Type == GameEventType.PassThrown);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(4)]
        public void ExpiredClockKeepsCurrentLivePlayThenEmitsOneQuarterEndAtItsTerminalStep(int quarter)
        {
            var game = Scenarios.Live(TeamId.Home);
            Scenarios.CarrierAt(game, 25f);
            Scenarios.ParkOtherActors(game);
            game.Model.Quarter = quarter;
            game.Model.QuarterTicksRemaining = 1;
            Check.Step(game);
            Assert.Equal(GamePhase.LiveBall, game.State.Phase);
            Assert.Equal(0, game.State.QuarterTicksRemaining);
            Assert.Empty(game.Events);
            for (int i = 0; i < 10; i++)
            {
                Check.Step(game);
                Assert.Equal(GamePhase.LiveBall, game.State.Phase);
                Assert.Equal(0, game.State.QuarterTicksRemaining);
                Assert.DoesNotContain(game.Events, item => item.Type == GameEventType.QuarterEnded);
            }
            Scenarios.OutOfBoundsAt(game, 25f);
            Check.EventTypes(game.Events, GameEventType.OutOfBounds, GameEventType.QuarterEnded);
            Assert.Equal(quarter, game.Events[1].Value);
            Assert.Equal(TeamId.Home, game.Events[1].Team);
            Assert.Equal(-1, game.Events[1].PlayerId);
            var nextScrimmage = game.State;
            Check.Step(game);
            Assert.Equal(GamePhase.DeadBall, game.State.Phase);
            Assert.Empty(game.Events);
            Check.Step(game, new PlayerCommand(action: InputAction.Continue));
            if (quarter < 4)
            {
                Assert.Equal(GamePhase.PlaySelect, game.State.Phase);
                Assert.Equal(quarter + 1, game.State.Quarter);
                Assert.Equal(7200, game.State.QuarterTicksRemaining);
                Assert.Equal(nextScrimmage.Down, game.State.Down);
                Assert.Equal(nextScrimmage.LineOfScrimmage, game.State.LineOfScrimmage);
                Assert.Equal(nextScrimmage.FirstDownLine, game.State.FirstDownLine);
                Assert.Equal(nextScrimmage.Possession, game.State.Possession);
                Assert.Empty(game.Events);
            }
            else
            {
                Assert.Equal(GamePhase.GameOver, game.State.Phase);
                Check.EventTypes(game.Events, GameEventType.GameEnded);
                Assert.Equal(0, game.Events[0].Value);
            }
        }

        [Fact]
        public void PassAlreadyInFlightFinishesAfterClockZeroWithoutBeingCutOff()
        {
            var game = Scenarios.Live(TeamId.Home);
            Check.Step(game, new PlayerCommand(action: InputAction.Pass, targetPlayerId: 4));
            Assert.Equal(BallMode.InFlight, game.State.Ball.Mode);
            game.Model.QuarterTicksRemaining = 1;
            bool caught = false;
            for (int i = 0; i < 100 && game.State.Ball.Mode == BallMode.InFlight; i++)
            {
                Check.Step(game);
                Assert.Equal(0, game.State.QuarterTicksRemaining);
                Assert.DoesNotContain(game.Events, item => item.Type == GameEventType.QuarterEnded);
                foreach (var item in game.Events) caught |= item.Type == GameEventType.PassCaught;
            }
            Assert.True(caught, "The actual scheduled catch must still occur after the clock reaches zero.");
            Assert.Equal(GamePhase.LiveBall, game.State.Phase);
            Assert.Equal(4, game.State.Ball.CarrierId);
            Scenarios.OutOfBoundsAt(game, 35f);
            Check.EventTypes(game.Events, GameEventType.OutOfBounds, GameEventType.QuarterEnded);
            Assert.Equal(1, game.Events[1].Value);
        }

        [Theory]
        [InlineData(0, 0, 0)]
        [InlineData(6, 6, 0)]
        [InlineData(12, 2, 1)]
        [InlineData(2, 12, -1)]
        public void FinalResultReportsHomeWinAwayWinOrTieThenExplicitRestartResetsEverything(int home, int away, int expected)
        {
            var game = Scenarios.Live(TeamId.Home);
            game.Model.Quarter = 4;
            game.Model.QuarterTicksRemaining = 1;
            game.Model.HomeScore = home;
            game.Model.AwayScore = away;
            Scenarios.OutOfBoundsAt(game, 25f);
            Check.EventTypes(game.Events, GameEventType.OutOfBounds, GameEventType.QuarterEnded);
            Check.Step(game, new PlayerCommand(action: InputAction.Continue));
            Assert.Equal(GamePhase.GameOver, game.State.Phase);
            Assert.Equal(home, game.State.HomeScore);
            Assert.Equal(away, game.State.AwayScore);
            Check.EventTypes(game.Events, GameEventType.GameEnded);
            Assert.Equal(expected, game.Events[0].Value);
            Assert.Equal(TeamId.Home, game.Events[0].Team);
            Assert.Equal(-1, game.Events[0].PlayerId);
            var final = Check.StateFields(game.State);
            Check.Step(game, new PlayerCommand(action: (InputAction)999, targetPlayerId: int.MinValue));
            var current = Check.StateFields(game.State);
            current[0] = final[0];
            Check.SameFields(final, current, "unknown action cannot unfreeze final");
            Assert.Empty(game.Events);
            game.Restart(41);
            Check.SameFields(Check.StateFields(Check.NewGame(41).State), Check.StateFields(game.State), "scenario final restart");
            Assert.Empty(game.Events);
        }

        [Fact]
        public void ScorePossessionAndQuarterEffectsHaveStableOrderOnTheSameTerminalTick()
        {
            var game = Scenarios.Live(TeamId.Home);
            game.Model.Quarter = 4;
            game.Model.QuarterTicksRemaining = 1;
            Scenarios.CarrierAt(game, 100f);
            Scenarios.ParkOtherActors(game);
            Check.Step(game);
            Check.EventTypes(game.Events, GameEventType.Touchdown, GameEventType.PossessionChanged, GameEventType.QuarterEnded);
            Assert.Equal(6, game.State.HomeScore);
            Assert.Equal(0, game.State.AwayScore);
            Assert.Equal(TeamId.Home, game.Events[0].Team);
            Assert.Equal(TeamId.Away, game.Events[1].Team);
            Assert.Equal(4, game.Events[2].Value);
            Check.Step(game, new PlayerCommand(action: InputAction.Continue));
            Check.EventTypes(game.Events, GameEventType.GameEnded);
            Assert.Equal(1, game.Events[0].Value);
        }
    }
}
