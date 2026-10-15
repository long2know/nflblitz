using Blitz.Contracts;
using Xunit;

namespace Blitz.Core.Tests
{
    public sealed class ScenarioRuleTests
    {
        [Theory]
        [InlineData(TeamId.Home, 20f, 50f, 80f)]
        [InlineData(TeamId.Away, 80f, 50f, 20f)]
        public void ActualTerminalStepAtThirtyYardsPublishesCauseThenFirstDown(
            TeamId offense, float los, float target, float nextTarget)
        {
            var game = Scenarios.Live(offense, 3, los, target);
            Scenarios.OutOfBoundsAt(game, 50f);
            Assert.Equal(PlayOutcome.FirstDown, game.State.LastOutcome);
            Assert.Equal(offense, game.State.Possession);
            Assert.Equal(1, game.State.Down);
            Assert.Equal(50f, game.State.LineOfScrimmage);
            Assert.Equal(nextTarget, game.State.FirstDownLine);
            Check.EventTypes(game.Events, GameEventType.OutOfBounds, GameEventType.FirstDown);
            Assert.Equal(offense, game.Events[0].Team);
            Assert.Equal(offense, game.Events[1].Team);
            Assert.Equal(50f, game.Events[1].Position.X);
            Assert.Equal(0f, game.Events[1].Position.Z);
            Assert.Equal(game.State.Ball.Position.X, game.Events[0].Position.X);
            Assert.Equal(game.State.Ball.Position.Z, game.Events[0].Position.Z);
        }

        [Theory]
        [InlineData(TeamId.Home, 20f, 50f, 36.75f, 37f, TeamId.Away, 7f)]
        [InlineData(TeamId.Away, 80f, 50f, 63.5f, 63f, TeamId.Home, 93f)]
        public void ActualFailedFourthDownImmediatelyResolvesPossessionWithoutMirroringWorldSpot(
            TeamId offense, float los, float target, float end, float spot, TeamId receiving, float nextTarget)
        {
            var game = Scenarios.Live(offense, 4, los, target);
            Scenarios.OutOfBoundsAt(game, end);
            Assert.Equal(receiving, game.State.Possession);
            Assert.Equal(receiving == TeamId.Home ? 1 : -1, game.State.OffenseDirection);
            Assert.Equal(1, game.State.Down);
            Assert.Equal(spot, game.State.LineOfScrimmage);
            Assert.Equal(nextTarget, game.State.FirstDownLine);
            Assert.Equal(PlayOutcome.TurnoverOnDowns, game.State.LastOutcome);
            Check.EventTypes(game.Events, GameEventType.OutOfBounds, GameEventType.TurnoverOnDowns, GameEventType.PossessionChanged);
            Assert.Equal(offense, game.Events[0].Team);
            Assert.Equal(receiving, game.Events[1].Team);
            Assert.Equal(receiving, game.Events[2].Team);
            Assert.Equal(spot, game.Events[2].Position.X);
            Assert.Equal(0f, game.Events[2].Position.Z);
            Assert.NotEqual(spot, game.State.Ball.Position.X);
            Assert.Equal(SliceRules.FieldHalfWidthYards, game.State.Ball.Position.Z);
            var positions = game.State;
            Check.Step(game);
            Check.ActorsUnmoved(positions, game.State);
            Assert.Empty(game.Events);
            Check.Step(game, new PlayerCommand(action: InputAction.Continue));
            Assert.Equal(GamePhase.PlaySelect, game.State.Phase);
            Assert.Equal(receiving, game.State.Possession);
            Assert.Equal(spot, game.State.LineOfScrimmage);
            Assert.Equal(nextTarget, game.State.FirstDownLine);
        }

        [Theory]
        [InlineData(TeamId.Home, 100f, 6, 0, TeamId.Away, 80f)]
        [InlineData(TeamId.Away, 0f, 0, 6, TeamId.Home, 20f)]
        public void InBoundsGoalCrossingScoresSixOnceForTheCorrectTeam(
            TeamId offense, float goal, int home, int away, TeamId receiving, float ownTwenty)
        {
            var game = Scenarios.Live(offense);
            Scenarios.CarrierAt(game, goal);
            Scenarios.ParkOtherActors(game);
            int carrier = game.Model.CarrierId;
            Check.Step(game);
            Assert.Equal(GamePhase.DeadBall, game.State.Phase);
            Assert.Equal(PlayOutcome.Touchdown, game.State.LastOutcome);
            Assert.Equal(home, game.State.HomeScore);
            Assert.Equal(away, game.State.AwayScore);
            Assert.Equal(receiving, game.State.Possession);
            Assert.Equal(1, game.State.Down);
            Assert.Equal(ownTwenty, game.State.LineOfScrimmage);
            Assert.Equal(50f, game.State.FirstDownLine);
            Check.EventTypes(game.Events, GameEventType.Touchdown, GameEventType.PossessionChanged);
            Assert.Equal(offense, game.Events[0].Team);
            Assert.Equal(carrier, game.Events[0].PlayerId);
            Assert.Equal(6, game.Events[0].Value);
            Assert.Equal(receiving, game.Events[1].Team);
            Assert.Equal(ownTwenty, game.Events[1].Position.X);
            var terminal = game.State;
            for (int i = 0; i < 30; i++)
            {
                Check.Step(game, new PlayerCommand(new FieldVector(1, 1), true, InputAction.Pass, targetPlayerId: 4));
                Assert.Equal(home, game.State.HomeScore);
                Assert.Equal(away, game.State.AwayScore);
                Check.ActorsUnmoved(terminal, game.State);
                Assert.Empty(game.Events);
            }
            Check.Step(game, new PlayerCommand(action: InputAction.Continue));
            Assert.Equal(ownTwenty, game.State.LineOfScrimmage);
            Assert.Equal(50f, game.State.FirstDownLine);
            Assert.Equal(receiving == TeamId.Home ? 3 : 10, game.State.Ball.CarrierId);
        }

        [Theory]
        [InlineData(TeamId.Home, -1f, 0, 2, TeamId.Away, 80f)]
        [InlineData(TeamId.Away, 101f, 2, 0, TeamId.Home, 20f)]
        public void OwnEndZoneOutOfBoundsScoresTwoAndScoringTeamReceivesAtOwnTwenty(
            TeamId offense, float end, int home, int away, TeamId receiving, float ownTwenty)
        {
            var game = Scenarios.Live(offense);
            Scenarios.OutOfBoundsAt(game, end);
            Assert.Equal(PlayOutcome.Safety, game.State.LastOutcome);
            Assert.Equal(home, game.State.HomeScore);
            Assert.Equal(away, game.State.AwayScore);
            Assert.Equal(receiving, game.State.Possession);
            Assert.Equal(ownTwenty, game.State.LineOfScrimmage);
            Assert.Equal(50f, game.State.FirstDownLine);
            Check.EventTypes(game.Events, GameEventType.OutOfBounds, GameEventType.Safety, GameEventType.PossessionChanged);
            Assert.Equal(offense, game.Events[0].Team);
            Assert.Equal(receiving, game.Events[1].Team);
            Assert.Equal(2, game.Events[1].Value);
            Assert.Equal(receiving, game.Events[2].Team);
            Check.Step(game, new PlayerCommand(action: InputAction.Tackle));
            Assert.Equal(home, game.State.HomeScore);
            Assert.Equal(away, game.State.AwayScore);
            Assert.Empty(game.Events);
        }

        [Theory]
        [InlineData(TeamId.Home, 100f, 0f, PlayOutcome.Touchdown)]
        [InlineData(TeamId.Home, 100f, SliceRules.FieldHalfWidthYards, PlayOutcome.OutOfBounds)]
        [InlineData(TeamId.Home, 100f, -SliceRules.FieldHalfWidthYards, PlayOutcome.OutOfBounds)]
        [InlineData(TeamId.Home, 110f, 0f, PlayOutcome.OutOfBounds)]
        [InlineData(TeamId.Away, 0f, 0f, PlayOutcome.Touchdown)]
        [InlineData(TeamId.Away, 0f, SliceRules.FieldHalfWidthYards, PlayOutcome.OutOfBounds)]
        [InlineData(TeamId.Away, 0f, -SliceRules.FieldHalfWidthYards, PlayOutcome.OutOfBounds)]
        [InlineData(TeamId.Away, -10f, 0f, PlayOutcome.OutOfBounds)]
        public void SidelineAndBackBoundaryWinAmbiguousCornersButInBoundsGoalWinsTackle(
            TeamId offense, float x, float z, PlayOutcome expectedCause)
        {
            var game = Scenarios.Live(offense);
            Scenarios.CarrierAt(game, x, z);
            Scenarios.ParkOtherActors(game);
            game.Model.LiveTicks = 12;
            int defender = offense == TeamId.Home ? 7 : game.Model.ControlledPlayerId;
            game.Model.Players[defender].Position = new FieldVector(x, z);
            Check.Step(game, new PlayerCommand(action: InputAction.Tackle));
            Assert.Equal(GamePhase.DeadBall, game.State.Phase);
            Assert.Equal(expectedCause == PlayOutcome.Touchdown ? 6 : 0,
                game.State.HomeScore + game.State.AwayScore);
            Assert.Equal(expectedCause == PlayOutcome.Touchdown ? GameEventType.Touchdown : GameEventType.OutOfBounds,
                game.Events[0].Type);
            Assert.DoesNotContain(game.Events, item => item.Type == GameEventType.Tackled);
            Assert.Equal(expectedCause == PlayOutcome.Touchdown, game.State.LastOutcome == PlayOutcome.Touchdown);
        }

        [Theory]
        [InlineData(0.8f, true)]
        [InlineData(1.8f, true)]
        [InlineData(3.5f, false)]
        public void ControlledHomeDefenderRequiresTackleActionWithinBoundedReach(float distance, bool shouldTackle)
        {
            var neutral = Scenarios.DefensiveReach(distance);
            var tackling = Scenarios.DefensiveReach(distance);
            neutral.Model.LiveTicks = 12;
            tackling.Model.LiveTicks = 12;
            Check.Step(neutral);
            Check.Step(tackling, new PlayerCommand(action: InputAction.Tackle));
            Assert.Equal(GamePhase.LiveBall, neutral.State.Phase);
            Assert.Equal(shouldTackle ? GamePhase.DeadBall : GamePhase.LiveBall, tackling.State.Phase);
            if (shouldTackle)
            {
                Assert.Equal(PlayOutcome.Tackled, tackling.State.LastOutcome);
                Check.EventTypes(tackling.Events, GameEventType.Tackled);
                Assert.Equal(TeamId.Away, tackling.Events[0].Team);
                Assert.Equal(10, tackling.Events[0].PlayerId);
            }
            else
            {
                Assert.Empty(tackling.Events);
            }
        }

        [Fact]
        public void EqualDistanceDefenderSwitchChoosesLowerOtherHomeId()
        {
            var game = Scenarios.DefensiveReach(5f);
            game.Model.ControlledPlayerId = 3;
            game.Model.Players[0].Position = new FieldVector(74f, 0);
            game.Model.Players[1].Position = new FieldVector(76f, 0);
            Check.Step(game, new PlayerCommand(action: InputAction.SwitchDefender));
            Assert.Equal(0, game.State.ControlledPlayerId);
            Assert.Equal(GamePhase.LiveBall, game.State.Phase);
        }

        [Theory]
        [InlineData(InputAction.Pass)]
        [InlineData(InputAction.SelectPlay)]
        [InlineData(InputAction.Snap)]
        [InlineData(InputAction.Continue)]
        [InlineData((InputAction)999)]
        public void WrongRoleAndUnknownActionsOnHomeDefenseCannotAlterTheLiveStep(InputAction action)
        {
            var invalid = Scenarios.DefensiveReach(10f);
            var neutral = Scenarios.DefensiveReach(10f);
            Check.Step(invalid, new PlayerCommand(new FieldVector(0, 1), true, action, (PlayId)999, int.MaxValue));
            Check.Step(neutral, new PlayerCommand(new FieldVector(0, 1), true));
            Check.SameFields(Check.StateFields(neutral.State), Check.StateFields(invalid.State), "invalid Home defense action");
            Check.SameFields(Check.EventFields(neutral.Events), Check.EventFields(invalid.Events), "invalid Home defense events");
        }

        [Theory]
        [InlineData(TeamId.Home, -1f, 0, 2)]
        [InlineData(TeamId.Away, 101f, 2, 0)]
        public void RealContactInOwnEndZonePublishesTackleThenSafetyForOpponent(
            TeamId offense, float x, int home, int away)
        {
            var game = Scenarios.Live(offense);
            Scenarios.CarrierAt(game, x);
            Scenarios.ParkOtherActors(game);
            game.Model.LiveTicks = 12;
            int defender = offense == TeamId.Home ? 7 : game.Model.ControlledPlayerId;
            game.Model.Players[defender].Position = new FieldVector(x, 0.8f);
            Check.Step(game, new PlayerCommand(action: InputAction.Tackle));
            Assert.Equal(GamePhase.DeadBall, game.State.Phase);
            Assert.Equal(PlayOutcome.Safety, game.State.LastOutcome);
            Assert.Equal(home, game.State.HomeScore);
            Assert.Equal(away, game.State.AwayScore);
            Check.EventTypes(game.Events, GameEventType.Tackled, GameEventType.Safety, GameEventType.PossessionChanged);
            TeamId scoring = offense == TeamId.Home ? TeamId.Away : TeamId.Home;
            Assert.Equal(offense, game.Events[0].Team);
            Assert.Equal(scoring, game.Events[1].Team);
            Assert.Equal(2, game.Events[1].Value);
            Assert.Equal(scoring, game.Events[2].Team);
        }
    }
}
