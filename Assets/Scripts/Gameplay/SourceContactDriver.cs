using System;
using Blitz.Contracts;

namespace Blitz.Gameplay
{
    internal enum SourceBallLoss { None, Fumble, Bobble }

    internal static class SourceContactDriver
    {
        private const uint NoCylinder = 0x0200;
        private const uint PuppetModes = 0x00c00000;
        private const float BodyDiameter = 10f;
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

        // detect.c 2545-2970. Attack dispatch supplies the original base pair;
        // this does not substitute for C1's collision/puppet/reaction selection.
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

        // detect.c 555-662, 1012-1099, blob 68c239fa2e617988107fbebeb4dfeff5bbfe9233.
        // This is att_none's cylinder response, not the unported attack/evade/
        // block-vs-rush decision tables preceding that response.
        internal static void ResolveBodyCylinders(SimulationState model)
        {
            var distances = new float[model.Players.Length, model.Players.Length];
            var angles = new int[model.Players.Length, model.Players.Length];
            for (int i = 0; i < model.Players.Length; i++)
                for (int j = i + 1; j < model.Players.Length; j++)
                {
                    SourceVector3 a = SourcePlayerDriver.WorldPosition(model.Players[i]);
                    SourceVector3 b = SourcePlayerDriver.WorldPosition(model.Players[j]);
                    float x = b.X - a.X, z = b.Z - a.Z;
                    distances[i, j] = (float)Math.Sqrt(x * x + z * z);
                    distances[j, i] = distances[i, j];
                    angles[i, j] = model.SourcePlaybook.Directions.Heading(new SourcePlanar(a.X, a.Z), new SourcePlanar(b.X, b.Z));
                    angles[j, i] = (angles[i, j] + 512) & 1023;
                }
            model.SourceDistances = distances;
            model.SourceAngles = angles;
            for (int i = 0; i < model.Players.Length; i++)
            {
                for (int j = i + 1; j < model.Players.Length; j++)
                {
                    ActorState a = model.Players[i];
                    ActorState d = model.Players[j];
                    uint modes = a.AnimationState.Primary.Mode | d.AnimationState.Primary.Mode;
                    if ((modes & NoCylinder) != 0 || distances[i, j] >= BodyDiameter)
                        continue;
                    if ((modes & PuppetModes) != 0)
                        throw new NotSupportedException("Unported source puppet cylinder: actor=" + i + " target=" + j);
                    if (Math.Abs(SourcePlayerDriver.WorldPosition(a).Y - SourcePlayerDriver.WorldPosition(d).Y) > 30f)
                        continue;
                    // Carrier contact is dispatched by the tackle/evade owner;
                    // do not push the pair apart before that dispatch sees it.
                    if (a.Team != d.Team && (a.Id == model.CarrierId || d.Id == model.CarrierId))
                        continue;
                    if (a.Team == model.Possession)
                    {
                        ActorState swap = a;
                        a = d;
                        d = swap;
                    }
                    Respond(model, a, d, distances[i, j]);
                }
            }
        }

        private static void Respond(SimulationState model, ActorState a, ActorState d, float distance)
        {
            SourceVector3 pa = SourcePlayerDriver.WorldPosition(a), pd = SourcePlayerDriver.WorldPosition(d);
            var connecting = new FieldVector(pd.X - pa.X, pd.Z - pa.Z);
            float squared = PlayerSimulation.DistanceSquared(connecting, default(FieldVector));
            connecting = squared < 0.001f ? new FieldVector(1f, 0f)
                : PlayerSimulation.Scale(connecting, 1f / (float)Math.Sqrt(squared));
            float da = Math.Abs(a.SourceVelocity.X * connecting.X + a.SourceVelocity.Z * connecting.Z);
            float dd = Math.Abs(d.SourceVelocity.X * connecting.X + d.SourceVelocity.Z * connecting.Z);
            if (Math.Abs(da) < 0.001 && Math.Abs(dd) < 0.001)
                da = dd = 1f;
            if (d.Role == ActorRole.Receiver && d.Id != model.CarrierId)
                da += 6f;
            if (d.AnimationState.Primary.IsUninterruptible)
                da += 6f;
            if (a.AnimationState.Primary.IsUninterruptible)
                dd += 6f;
            bool ending = (model.SourceFlags & SourceGameFlags.PlayEnding) != 0;
            if ((d.AnimationState.Primary.Mode & SourceAnimationModes.Reacting) != 0
                && !ending && pd.Y > 6.5f)
            {
                da = 0f;
                dd = 1f;
            }
            if ((a.AnimationState.Primary.Mode & SourceAnimationModes.Reacting) != 0
                && !ending && pa.Y > 6.5f)
            {
                dd = 0f;
                da = 1f;
            }
            if (model.Phase == GamePhase.PreSnap && d.Role == ActorRole.Lineman)
            {
                da = 1f;
                dd = 0f;
            }
            if (model.Phase == GamePhase.PreSnap && a.Role == ActorRole.Lineman)
            {
                da = 0f;
                dd = 1f;
            }
            float response = (BodyDiameter - distance) / (da + dd);
            Move(a, PlayerSimulation.Scale(connecting, -da * response));
            Move(d, PlayerSimulation.Scale(connecting, dd * response));
        }

        private static void Move(ActorState actor, FieldVector delta)
        {
            SourcePlayerDriver.MoveSource(actor, delta);
            actor.Velocity = PlayerSimulation.Add(actor.Velocity,
                PlayerSimulation.Scale(delta, SourceTiming.DefaultTicksPerSecond / SourceTiming.SourceUnitsPerYard));
        }
    }
}
