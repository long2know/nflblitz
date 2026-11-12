using System;
using Blitz.Contracts;

namespace Blitz.Gameplay
{
    public static class SourceTimeOfPossession
    {
        public static SourceTimeOfPossessionView Accumulate(SourceTimeOfPossessionView previous,
            TeamId offense, int timeOfPossessionDelta)
        {
            if (previous == null)
                throw new ArgumentNullException(nameof(previous));
            if (offense != TeamId.Home && offense != TeamId.Away)
                throw new ArgumentOutOfRangeException(nameof(offense));
            if (timeOfPossessionDelta < 0 || timeOfPossessionDelta > 1)
                throw new ArgumentOutOfRangeException(nameof(timeOfPossessionDelta));
            if (timeOfPossessionDelta == 0)
                return previous;
            return offense == TeamId.Home
                ? new SourceTimeOfPossessionView(checked(previous.HomeSeconds + 1), previous.AwaySeconds)
                : new SourceTimeOfPossessionView(previous.HomeSeconds, checked(previous.AwaySeconds + 1));
        }
    }
}
