using System;
using Blitz.Contracts;

namespace Blitz.Gameplay
{
    internal static class SourceContactDriver
    {
        private const uint NoCylinder = 0x0200;
        private const uint PuppetModes = 0x00c00000;
        private const float BodyDiameter = 10f;

        // detect.c 555-662, 1012-1099, blob 68c239fa2e617988107fbebeb4dfeff5bbfe9233.
        // This is att_none's cylinder response, not the unported attack/evade/
        // block-vs-rush decision tables preceding that response.
        internal static void ResolveBodyCylinders(SimulationState model)
        {
            var distances = new float[model.Players.Length, model.Players.Length];
            for (int i = 0; i < model.Players.Length; i++)
                for (int j = i + 1; j < model.Players.Length; j++)
                    distances[i, j] = (float)Math.Sqrt(PlayerSimulation.DistanceSquared(
                        model.Players[i].Position, model.Players[j].Position)) * SourceTiming.SourceUnitsPerYard;
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
                    if (Math.Abs(a.Height - d.Height) * SourceTiming.SourceUnitsPerYard > 30f)
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
            FieldVector connecting = PlayerSimulation.Scale(PlayerSimulation.Subtract(d.Position, a.Position),
                SourceTiming.SourceUnitsPerYard);
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
                && !ending && d.Height * SourceTiming.SourceUnitsPerYard > 6.5f)
            {
                da = 0f;
                dd = 1f;
            }
            if ((a.AnimationState.Primary.Mode & SourceAnimationModes.Reacting) != 0
                && !ending && a.Height * SourceTiming.SourceUnitsPerYard > 6.5f)
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
            float response = (BodyDiameter - distance) / (da + dd) / SourceTiming.SourceUnitsPerYard;
            Move(a, PlayerSimulation.Scale(connecting, -da * response));
            Move(d, PlayerSimulation.Scale(connecting, dd * response));
        }

        private static void Move(ActorState actor, FieldVector delta)
        {
            actor.Position = PlayerSimulation.Add(actor.Position, delta);
            actor.Velocity = PlayerSimulation.Add(actor.Velocity,
                PlayerSimulation.Scale(delta, SourceTiming.DefaultTicksPerSecond));
        }
    }
}
