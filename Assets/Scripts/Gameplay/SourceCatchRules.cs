using System;
using Blitz.Contracts;

namespace Blitz.Gameplay
{
    internal enum SourceCatchResult { None, Catch, Miss, Bobble, BatDown, Deflect, DownPunt }

    internal static class SourceCatchRules
    {
        private static readonly int[] NoCatch = { 0, 0, 0, 0, 0, 0, 10, 15, 20, 25, 30, 100 };
        private static readonly int[] OffensiveBobble = { 6, 5, 5, 4, 3, 2, 1, 0, 0, 0, 0, 0 };
        private static readonly int[] DefensiveBatDown = { 100, 80, 60, 40, 30, 20, 10, 5, 0, 0, 0, 0 };
        private static readonly int[] DefensiveBobble = { 100, 80, 60, 40, 30, 20, 10, 5, 0, 0, 0, 0 };
        private const uint PuppetModes = 0x00c00000;

        internal static bool ModeEligible(SimulationState model, ActorState actor, Func<int, int> random)
        {
            if (model.Phase != GamePhase.LiveBall || model.CarrierId != -1)
                return false;
            uint mode = actor.AnimationState.Primary.Mode;
            if ((mode & PuppetModes) != 0)
                return false;
            if ((mode & SourceAnimationModes.NoCatch) != 0 && random(100) >= NoCatch[actor.SourceHandicap])
                return false;
            if ((model.SourceFlags & SourceGameFlags.HikingBall) != 0
                && (model.SourceOffensePlay == null || (model.SourceOffensePlay.Flags & SourcePlayFlags.FakeFieldGoal) == 0))
                return false;
            return SourceBallDriver.InBounds(SourcePlayerDriver.WorldPosition(actor));
        }

        internal static bool InCatchDistance(SourceVector3 ball, SourceVector3 pelvis,
            SourceVector3 leftWrist, SourceVector3 rightWrist)
        {
            return Distance(ball, pelvis) < 14f || Distance(ball, leftWrist) < 5f
                || Distance(ball, rightWrist) < 5f;
        }

        internal static SourceCatchResult Evaluate(SimulationState model, ActorState actor, Func<int, int> random)
        {
            bool offense = actor.Team == model.Possession;
            int side = (int)model.Possession;
            // Active player.c compares flight_time to LB_FIELD_GOAL (4), not type.
            if (model.SourceTeamFire[side] && model.PassTicksElapsed == 4)
                return SourceCatchResult.None;
            if (model.ThrowerId == actor.Id && model.PassTicksElapsed <= 10)
                return SourceCatchResult.None;
            if (offense && model.PassTicksElapsed <= 15 && actor.Id != model.TargetPlayerId)
                return SourceCatchResult.None;
            if (actor.NoCatchTicks > 0)
                return SourceCatchResult.None;
            if ((model.BallKind == SourceBallKind.Bobble || model.BallKind == SourceBallKind.Fumble)
                && model.PassTicksElapsed <= 30)
                return SourceCatchResult.None;
            int field = SourceMatchRules.FieldPosition(SourcePlayerDriver.WorldPosition(actor).X, side);
            float height = SourcePlayerDriver.WorldPosition(actor).Y;
            switch (model.BallKind)
            {
                case SourceBallKind.Pass:
                case SourceBallKind.Bobble:
                    int lead = side == 0 ? model.HomeScore - model.AwayScore : model.AwayScore - model.HomeScore;
                    SourceCatchResult result = SourceCatchResult.Catch;
                    if (!SourceContactDriver.IsHumanTeam(model, actor.Team)
                        || lead > 3 && field > model.SourceRules.LineOfScrimmage)
                    {
                        result = random(100) < OffensiveBobble[actor.SourceHandicap]
                            ? SourceCatchResult.Bobble : SourceCatchResult.Catch;
                        if (model.SourceDronesLose && !offense && model.SourceDronesWinning)
                            result = SourceCatchResult.Miss;
                    }
                    if ((actor.AnimationState.Primary.Mode & SourceAnimationModes.Reacting) != 0)
                    {
                        if (!offense)
                            result = random(10) != 0 ? SourceCatchResult.Miss : SourceCatchResult.Deflect;
                        else if (random(100) < 40) result = SourceCatchResult.Catch;
                        else if (random(100) < 20) result = SourceCatchResult.Bobble;
                        else result = random(2) != 0 ? SourceCatchResult.Deflect : SourceCatchResult.Miss;
                    }
                    if (model.PassTicksElapsed <= 30 && !offense
                        && (result == SourceCatchResult.Bobble || result == SourceCatchResult.Catch))
                        result = random(10) != 0 ? SourceCatchResult.Deflect : SourceCatchResult.Miss;
                    if (!offense && model.SourceRules.Down == 4 && field >= model.SourceRules.LineOfScrimmage + 5
                        && result == SourceCatchResult.Catch)
                        result = SourceCatchResult.BatDown;
                    // >= 0 is intentional: the original 'air' test includes standing defenders.
                    if (!offense && result == SourceCatchResult.Catch && height >= 0f
                        && random(100) < DefensiveBatDown[actor.SourceHandicap])
                        result = SourceCatchResult.BatDown;
                    if (!offense && result == SourceCatchResult.Catch && height == 0f
                        && (model.SourceBallFlags & SourceBallFlags.Bobble) == 0
                        && random(100) < DefensiveBobble[actor.SourceHandicap])
                        result = SourceCatchResult.Bobble;
                    if (!offense && model.BallKind == SourceBallKind.Pass && model.SourceTeamFire[1 - side]
                        && (result == SourceCatchResult.Bobble || result == SourceCatchResult.BatDown)
                        && random(2) != 0)
                        result = SourceCatchResult.Catch;
                    if (!offense && model.SourceNoInterceptions && result == SourceCatchResult.Catch)
                        result = SourceCatchResult.BatDown;
                    if (offense && model.SourceTeamFire[(int)actor.Team]
                        && model.SourceLastReceiver[(int)actor.Team] == actor.Id)
                        result = SourceCatchResult.Catch;
                    if (model.SourceTeamFire[side] && result == SourceCatchResult.Deflect)
                        result = SourceCatchResult.Miss;
                    return result;
                case SourceBallKind.Lateral:
                    // Preserve the active raw world-X / integer LOS comparison.
                    return !offense && !model.SourceNoInterceptions
                        && model.SourceBallPosition.X > model.SourceRules.LineOfScrimmage
                        && (model.SourceBallFlags & SourceBallFlags.Bounce) == 0
                        && model.PassTicksTotal - model.PassTicksElapsed > 0
                        ? SourceCatchResult.Miss : SourceCatchResult.Catch;
                case SourceBallKind.Fumble:
                    return SourceCatchResult.Catch;
                case SourceBallKind.Punt:
                    return offense ? SourceCatchResult.DownPunt : SourceCatchResult.Catch;
                case SourceBallKind.Kickoff:
                    return (model.SourceFlags & SourceGameFlags.OnsideKick) != 0
                        && random(45) >= model.PassTicksElapsed ? SourceCatchResult.Deflect : SourceCatchResult.Catch;
                case SourceBallKind.FieldGoal:
                    if (offense) return SourceCatchResult.Miss;
                    return (model.SourceBallFlags & SourceBallFlags.Deflected) != 0 && random(100) < 34
                        ? SourceCatchResult.Catch : SourceCatchResult.Deflect;
                default:
                    return SourceCatchResult.None;
            }
        }

        private static float Distance(SourceVector3 a, SourceVector3 b)
        {
            float x = b.X - a.X, y = b.Y - a.Y, z = b.Z - a.Z;
            return (float)Math.Sqrt(x * x + y * y + z * z);
        }
    }
}
