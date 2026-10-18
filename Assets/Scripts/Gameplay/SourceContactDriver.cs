using System;
using Blitz.Contracts;

namespace Blitz.Gameplay
{
    internal enum SourceBallLoss { None, Fumble, Bobble }

    internal static partial class SourceContactDriver
    {
        private static readonly int[] BobbleHuman = { 50, 55, 60, 65, 70, 75, 80, 85, 90, 95, 100, 100 };
        private static readonly int[] BobbleHumanTeam = { 30, 35, 40, 45, 50, 55, 64, 73, 82, 91, 100, 100 };
        private static readonly int[] BobbleDrone = { 40, 45, 50, 55, 60, 65, 72, 79, 86, 93, 100, 100 };
        private static readonly int[] BobbleCap = { 99, 99, 99, 99, 99, 99, 50, 45, 40, 30, 35, 0 };
        private static readonly int[] BobbleUp = { 75, 70, 65, 60, 55, 50, 40, 30, 20, 15, 5, 0 };

        internal static bool IsHumanTeam(SimulationState model, TeamId team)
        {
            if (model.SourceConfiguration == null)
                return false;
            foreach (SourceStationConfiguration station in model.SourceConfiguration.Stations)
                if (station.Team == team) return true;
            return false;
        }

        // detect.c 2545-2970. The active dispatcher supplies each caller's original base pair.
        internal static SourceBallLoss FumbleOrBobble(SimulationState model, ActorState victim,
            ActorState attacker, int bobbleBase, int fumbleBase, Func<int, int> random, out int high)
        {
            high = 0;
            if (model.CarrierId != victim.Id || model.Phase != GamePhase.LiveBall)
                return SourceBallLoss.None;
            int victimSide = (int)victim.Team, attackerSide = (int)attacker.Team;
            int held = unchecked((int)(model.SourceTick - model.CatchSourceTick));
            int offenseSide = (int)model.Possession;
            int lead = offenseSide == 0 ? model.HomeScore - model.AwayScore : model.AwayScore - model.HomeScore;
            int bobble = 0;
            if (model.BallKind == SourceBallKind.Pass || model.BallKind == SourceBallKind.Bobble
                || model.BallKind == SourceBallKind.Lateral || model.BallKind == SourceBallKind.Kickoff
                || model.BallKind == SourceBallKind.Fumble)
            {
                bobble = Math.Max(bobbleBase, attacker.SourceControllerStation >= 0
                    ? BobbleHuman[attacker.SourceHandicap] : IsHumanTeam(model, attacker.Team)
                    ? BobbleHumanTeam[attacker.SourceHandicap] : BobbleDrone[attacker.SourceHandicap]);
                if ((model.SourceFlags & SourceGameFlags.OnsideKick) != 0) bobble += 30;
                bobble -= held;
                if (held >= 18) bobble = 0;
                if (model.SourceTeamFire[1 - offenseSide] && model.Possession == victim.Team)
                    bobble = Math.Min(bobble * 2, bobble + (100 - bobble) / 2);
                bobble = Math.Min(bobble, BobbleCap[victim.SourceHandicap]);
            }
            int fumble = fumbleBase;
            high = 100 * BobbleUp[victim.SourceHandicap];
            if (model.SourceVictimHeight > 0f)
            {
                fumble *= 3;
                fumble /= 2;
            }
            if ((model.SourceFlags & SourceGameFlags.AirCatch) != 0 && fumble > bobble * 100)
            {
                bobble = fumble / 100;
                fumble = 0;
            }
            if ((model.SourceFlags & SourceGameFlags.OpeningKick) != 0
                && (model.SourceFlags & SourceGameFlags.OnsideKick) == 0)
            {
                fumble /= 50;
                high /= 3;
            }
            if (lead < 0 && !model.SourceNoAssist)
            {
                fumble *= 2;
                fumble /= 3;
                high *= 2;
                high /= 3;
                if (model.Quarter - 1 > 1)
                {
                    fumble /= 4;
                    high /= 4;
                }
            }
            if (lead <= -11 && !model.SourceNoAssist)
            {
                fumble = 0;
                high /= 2;
            }
            int field = SourceMatchRules.FieldPosition(SourcePlayerDriver.WorldPosition(victim).X, offenseSide);
            if (model.Quarter - 1 > 2 && !model.SourceNoAssist
                && (lead >= 4 && field >= 50 || lead >= 8 && field < 50))
            {
                fumble *= 5;
                fumble /= 4;
                high *= 5;
                high /= 4;
            }
            if (model.SourceLostFumbles[victimSide] > 1
                || model.SourceLostFumbles[victimSide] > model.SourceLostFumbles[attackerSide])
                fumble /= 2;
            if (model.SourceLostFumbles[victimSide] + model.SourceInterceptions[1 - victimSide] >= 4)
            {
                fumble /= 100;
                high /= 100;
            }
            if (model.SourceDronesLose && model.SourceDronesWinning)
            {
                // Active code chooses this branch from human side, not victim side.
                if (!IsHumanTeam(model, TeamId.Home))
                {
                    fumble /= 20;
                    bobble = Math.Min(25, bobble);
                    high = 0;
                }
                else
                {
                    fumble = Math.Max(2000, fumble);
                    if (bobble != 0) bobble = 100;
                    high = 10000;
                }
            }
            if (model.SourceNoFumbles) fumble = 0;
            if ((model.SourceFlags & SourceGameFlags.OnsideKick) != 0)
            {
                high = 10000;
                int onsideBase = Math.Min(5000, 10000 - 200 * held);
                onsideBase *= 2;
                onsideBase /= 2 + model.SourceFumblesOnPlay;
                fumble = Math.Max(fumble, onsideBase);
            }
            if (model.SourceTeamFire[victimSide] && victim.Team == model.Possession
                && model.SourceLastReceiver[victimSide] == victim.Id)
                bobble = 0;
            if (model.SourceTeamFire[victimSide]) fumble = 0;
            if (model.SourceSpinCount[victimSide] > 2 && model.SourceAbusingSpin
                && IsHumanTeam(model, victim.Team))
                fumble += 5000;
            if (model.SourceOffensivePlays[victimSide] < 4 && victim.SourceControllerStation >= 0)
                fumble = 0;
            if (!IsHumanTeam(model, victim.Team) && model.SourceSuperDrones)
                fumble = 0;
            if ((model.BallKind == SourceBallKind.Fumble || model.BallKind == SourceBallKind.Lateral
                || model.BallKind == SourceBallKind.Punt || model.BallKind == SourceBallKind.Kickoff)
                && bobble * 100 > fumble)
            {
                fumble = (model.SourceFlags & SourceGameFlags.OnsideKick) != 0
                    ? bobble * 100 : Math.Min(bobble, 20) * 100;
                bobble = 0;
            }
            if (fumble > bobble * 100)
                return random(10000) < fumble ? SourceBallLoss.Fumble : SourceBallLoss.None;
            return random(100) < bobble ? SourceBallLoss.Bobble : SourceBallLoss.None;
        }

    }
}
