using Blitz.Contracts;
using Blitz.Gameplay;
using Xunit;

namespace Blitz.Core.Tests
{
    public sealed class RuleTests
    {
        [Theory]
        [InlineData(TeamId.Home, 20f, 50f)]
        [InlineData(TeamId.Away, 80f, 50f)]
        [InlineData(TeamId.Home, 70f, 100f)]
        [InlineData(TeamId.Home, 88f, 100f)]
        [InlineData(TeamId.Away, 30f, 0f)]
        [InlineData(TeamId.Away, 12f, 0f)]
        public void NewSeriesTargetsThirtyYardsOrOpponentGoalNotTen(TeamId offense, float spot, float expected)
        {
            Assert.Equal(expected, GameplayRules.FirstDownTarget(offense, spot));
            Assert.Equal(offense == TeamId.Home ? 1 : -1, GameplayRules.Direction(offense));
            Assert.Equal(offense == TeamId.Home ? TeamId.Away : TeamId.Home, GameplayRules.Opponent(offense));
        }

        [Theory]
        [InlineData(TeamId.Home, 20f, 50f, 80f)]
        [InlineData(TeamId.Away, 80f, 50f, 20f)]
        public void FirstDownAtExactlyThirtyYardsResetsDownAndTarget(TeamId offense, float los, float target, float nextTarget)
        {
            var result = GameplayRules.Resolve(offense, 3, los, target, target, PlayOutcome.Tackled);
            Assert.Equal(offense, result.Possession);
            Assert.Equal(1, result.Down);
            Assert.Equal(target, result.LineOfScrimmage);
            Assert.Equal(nextTarget, result.FirstDownLine);
            Assert.Equal(PlayOutcome.FirstDown, result.Outcome);
            Assert.Equal(0, result.HomePoints);
            Assert.Equal(0, result.AwayPoints);
        }

        [Theory]
        [InlineData(TeamId.Home, 20f, 50f, 49f)]
        [InlineData(TeamId.Away, 80f, 50f, 51f)]
        public void BelowTargetAdvancesDownWithoutChangingTheTarget(TeamId offense, float los, float target, float end)
        {
            var result = GameplayRules.Resolve(offense, 2, los, target, end, PlayOutcome.OutOfBounds);
            Assert.Equal(offense, result.Possession);
            Assert.Equal(3, result.Down);
            Assert.Equal(end, result.LineOfScrimmage);
            Assert.Equal(target, result.FirstDownLine);
            Assert.Equal(PlayOutcome.OutOfBounds, result.Outcome);
            Assert.Equal(0, result.HomePoints);
            Assert.Equal(0, result.AwayPoints);
        }

        [Theory]
        [InlineData(TeamId.Home, 50f, 80f, 76f, 100f)]
        [InlineData(TeamId.Away, 50f, 20f, 24f, 0f)]
        public void GainedSeriesNearGoalCapsNextTargetAtGoal(TeamId offense, float los, float target, float end, float nextTarget)
        {
            float achieved = offense == TeamId.Home ? 81f : 19f;
            var result = GameplayRules.Resolve(offense, 4, los, target, achieved, PlayOutcome.Tackled);
            Assert.Equal(PlayOutcome.FirstDown, result.Outcome);
            Assert.Equal(offense, result.Possession);
            Assert.Equal(1, result.Down);
            Assert.Equal(achieved, result.LineOfScrimmage);
            Assert.Equal(nextTarget, result.FirstDownLine);
            var goalToGo = GameplayRules.Resolve(offense, 1, achieved, nextTarget, end, PlayOutcome.Tackled);
            Assert.Equal(2, goalToGo.Down);
            Assert.Equal(nextTarget, goalToGo.FirstDownLine);
        }

        [Theory]
        [InlineData(TeamId.Home, 20f, 50f, 37f, TeamId.Away, 7f)]
        [InlineData(TeamId.Away, 80f, 50f, 63f, TeamId.Home, 93f)]
        public void FailedFourthDownTransfersSameAbsoluteSpotAndReversesTarget(
            TeamId offense, float los, float target, float end, TeamId receiving, float nextTarget)
        {
            var result = GameplayRules.Resolve(offense, 4, los, target, end, PlayOutcome.Tackled);
            Assert.Equal(receiving, result.Possession);
            Assert.Equal(1, result.Down);
            Assert.Equal(end, result.LineOfScrimmage);
            Assert.Equal(nextTarget, result.FirstDownLine);
            Assert.Equal(PlayOutcome.TurnoverOnDowns, result.Outcome);
            Assert.Equal(0, result.HomePoints);
            Assert.Equal(0, result.AwayPoints);
        }

        [Theory]
        [InlineData(TeamId.Home, 22f, 52f, 99f, TeamId.Away, 0f)]
        [InlineData(TeamId.Away, 78f, 48f, 1f, TeamId.Home, 100f)]
        public void FourthDownIncompleteTransfersPossessionButNeverSpotsAtPassEnd(
            TeamId offense, float los, float target, float ballEnd, TeamId receiving, float nextTarget)
        {
            var result = GameplayRules.Resolve(offense, 4, los, target, ballEnd, PlayOutcome.Incomplete);
            Assert.Equal(receiving, result.Possession);
            Assert.Equal(1, result.Down);
            Assert.Equal(los, result.LineOfScrimmage);
            Assert.Equal(nextTarget, result.FirstDownLine);
            Assert.Equal(PlayOutcome.TurnoverOnDowns, result.Outcome);
        }

        [Theory]
        [InlineData(TeamId.Home, 22f, 52f, -8f)]
        [InlineData(TeamId.Away, 78f, 48f, 108f)]
        public void OrdinaryIncompleteConsumesOneDownAtUnchangedSnapLosEvenInOwnEndZone(
            TeamId offense, float los, float target, float ballEnd)
        {
            var result = GameplayRules.Resolve(offense, 2, los, target, ballEnd, PlayOutcome.Incomplete);
            Assert.Equal(offense, result.Possession);
            Assert.Equal(3, result.Down);
            Assert.Equal(los, result.LineOfScrimmage);
            Assert.Equal(target, result.FirstDownLine);
            Assert.Equal(PlayOutcome.Incomplete, result.Outcome);
            Assert.Equal(0, result.HomePoints);
            Assert.Equal(0, result.AwayPoints);
        }

        [Theory]
        [InlineData(TeamId.Home, PlayOutcome.Touchdown, 100f, 6, 0, TeamId.Away, 80f, 50f)]
        [InlineData(TeamId.Away, PlayOutcome.Touchdown, 0f, 0, 6, TeamId.Home, 20f, 50f)]
        [InlineData(TeamId.Home, PlayOutcome.Safety, -1f, 0, 2, TeamId.Away, 80f, 50f)]
        [InlineData(TeamId.Away, PlayOutcome.Safety, 101f, 2, 0, TeamId.Home, 20f, 50f)]
        public void ScoringCreditsCorrectTeamAndReceivingSeriesAtItsOwnTwenty(
            TeamId offense, PlayOutcome outcome, float end, int homePoints, int awayPoints,
            TeamId receiving, float ownTwenty, float nextTarget)
        {
            var result = GameplayRules.Resolve(offense, 4, 40f, 70f, end, outcome);
            Assert.Equal(homePoints, result.HomePoints);
            Assert.Equal(awayPoints, result.AwayPoints);
            Assert.Equal(receiving, result.Possession);
            Assert.Equal(1, result.Down);
            Assert.Equal(ownTwenty, result.LineOfScrimmage);
            Assert.Equal(nextTarget, result.FirstDownLine);
            Assert.Equal(outcome, result.Outcome);
        }

        [Theory]
        [InlineData(TeamId.Home, PlayOutcome.Tackled, 0f, 0, 2)]
        [InlineData(TeamId.Home, PlayOutcome.OutOfBounds, -9f, 0, 2)]
        [InlineData(TeamId.Away, PlayOutcome.Tackled, 100f, 2, 0)]
        [InlineData(TeamId.Away, PlayOutcome.OutOfBounds, 109f, 2, 0)]
        public void OwnEndZoneTackleOrOutOfBoundsIsSafetyNotTouchdown(
            TeamId offense, PlayOutcome cause, float end, int homePoints, int awayPoints)
        {
            var result = GameplayRules.Resolve(offense, 1, 20f, 50f, end, cause);
            Assert.Equal(PlayOutcome.Safety, result.Outcome);
            Assert.Equal(homePoints, result.HomePoints);
            Assert.Equal(awayPoints, result.AwayPoints);
        }

        [Theory]
        [InlineData(-8f, 1f)]
        [InlineData(0.01f, 1f)]
        [InlineData(1.1f, 2f)]
        [InlineData(23.1f, 24f)]
        [InlineData(49.01f, 50f)]
        [InlineData(50f, 50f)]
        [InlineData(50.99f, 50f)]
        [InlineData(76.9f, 76f)]
        [InlineData(98.99f, 98f)]
        [InlineData(99.99f, 99f)]
        [InlineData(109f, 99f)]
        public void NonScoringSpotRoundsTowardMidfieldAndClampsToOneThroughNinetyNine(float end, float expected)
        {
            Assert.Equal(expected, GameplayRules.Spot(end));
        }
    }
}
