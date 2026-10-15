using System;
using Blitz.Contracts;

namespace Blitz.Gameplay
{
    internal readonly struct PlayResolution
    {
        internal readonly TeamId Possession;
        internal readonly int Down;
        internal readonly float LineOfScrimmage;
        internal readonly float FirstDownLine;
        internal readonly PlayOutcome Outcome;
        internal readonly int HomePoints;
        internal readonly int AwayPoints;

        internal PlayResolution(TeamId possession, int down, float lineOfScrimmage,
            float firstDownLine, PlayOutcome outcome, int homePoints = 0, int awayPoints = 0)
        {
            Possession = possession;
            Down = down;
            LineOfScrimmage = lineOfScrimmage;
            FirstDownLine = firstDownLine;
            Outcome = outcome;
            HomePoints = homePoints;
            AwayPoints = awayPoints;
        }
    }

    internal static class GameplayRules
    {
        internal static int Direction(TeamId team)
        {
            return team == TeamId.Home ? 1 : -1;
        }

        internal static TeamId Opponent(TeamId team)
        {
            return team == TeamId.Home ? TeamId.Away : TeamId.Home;
        }

        internal static float FirstDownTarget(TeamId offense, float spot)
        {
            return Clamp(spot + Direction(offense) * SliceRules.FirstDownYards, 0f, SliceRules.FieldLengthYards);
        }

        internal static float Spot(float x)
        {
            float rounded = (float)(x < 50f ? Math.Ceiling(x) : Math.Floor(x));
            return Clamp(rounded, 1f, SliceRules.FieldLengthYards - 1f);
        }

        internal static bool IsOutside(FieldVector position)
        {
            return Math.Abs(position.Z) >= SliceRules.FieldHalfWidthYards
                || position.X <= -SliceRules.EndZoneDepthYards
                || position.X >= SliceRules.FieldLengthYards + SliceRules.EndZoneDepthYards;
        }

        internal static bool IsOpponentEndZone(TeamId offense, float x)
        {
            return offense == TeamId.Home ? x >= SliceRules.FieldLengthYards : x <= 0f;
        }

        internal static bool IsOwnEndZone(TeamId offense, float x)
        {
            return offense == TeamId.Home ? x <= 0f : x >= SliceRules.FieldLengthYards;
        }

        internal static PlayResolution Resolve(TeamId offense, int down, float snapLineOfScrimmage,
            float firstDownLine, float endX, PlayOutcome cause)
        {
            bool touchdown = cause == PlayOutcome.Touchdown;
            bool safety = cause == PlayOutcome.Safety
                || ((cause == PlayOutcome.Tackled || cause == PlayOutcome.OutOfBounds)
                    && IsOwnEndZone(offense, endX));
            if (touchdown || safety)
            {
                TeamId scoring = touchdown ? offense : Opponent(offense);
                TeamId receiving = Opponent(offense);
                int points = touchdown ? SliceRules.TouchdownPoints : SliceRules.SafetyPoints;
                float restart = receiving == TeamId.Home ? 20f : 80f;
                return new PlayResolution(receiving, 1, restart, FirstDownTarget(receiving, restart),
                    touchdown ? PlayOutcome.Touchdown : PlayOutcome.Safety,
                    scoring == TeamId.Home ? points : 0, scoring == TeamId.Away ? points : 0);
            }

            float spot = cause == PlayOutcome.Incomplete ? snapLineOfScrimmage : Spot(endX);
            if (Direction(offense) * (spot - firstDownLine) >= 0f)
                return new PlayResolution(offense, 1, spot, FirstDownTarget(offense, spot), PlayOutcome.FirstDown);

            if (down >= SliceRules.MaximumDown)
            {
                TeamId receiving = Opponent(offense);
                return new PlayResolution(receiving, 1, spot, FirstDownTarget(receiving, spot), PlayOutcome.TurnoverOnDowns);
            }
            return new PlayResolution(offense, down + 1, spot, firstDownLine, cause);
        }

        internal static float Clamp(float value, float minimum, float maximum)
        {
            return value < minimum ? minimum : value > maximum ? maximum : value;
        }
    }
}
