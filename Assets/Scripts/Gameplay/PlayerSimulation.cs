using System;
using Blitz.Contracts;

namespace Blitz.Gameplay
{
    internal static class PlayerSimulation
    {
        internal const float ControlledSpeed = 9.6f;
        internal const float ReceiverSpeed = 10.4f;
        internal const float AutoTackleReach = 1.15f;
        internal const float ManualTackleReach = 2.4f;
        internal const float CatchReach = 2f;
        private const float TickSeconds = 1f / SliceRules.TicksPerSecond;

        internal static void LineUp(SimulationState model)
        {
            int direction = model.OffenseDirection;
            for (int id = 0; id < model.Players.Length; id++)
            {
                ActorState actor = model.Players[id];
                int slot = id % SliceRules.PlayersPerTeam;
                bool offense = actor.Team == model.Possession;
                float depth;
                float lateral;
                if (offense)
                {
                    bool quickPass = model.OffensivePlay == PlayId.QuickPass;
                    float wideLane = quickPass ? 14f : 12f;
                    depth = slot < 3 ? 0f : slot == 3 ? -4f : -1.2f;
                    lateral = slot < 3 ? (slot - 1) * 4.2f
                        : slot == 4 ? -wideLane : slot == 5 ? wideLane : slot == 6 ? -6.5f : 0f;
                }
                else
                {
                    bool blitz = model.DefensivePlay == PlayId.Blitz;
                    depth = slot < 3 ? (blitz ? 3.5f : 4.5f)
                        : slot < 5 ? (blitz ? 7f : 12f) : (blitz ? 15f : 20f);
                    lateral = slot < 3 ? (slot - 1) * 4.2f
                        : slot == 3 ? -12f : slot == 4 ? 12f : slot == 5 ? -6f : 6f;
                }
                actor.Position = Constrain(new FieldVector(model.LineOfScrimmage + direction * depth, lateral), false);
                actor.SnapPosition = actor.Position;
                actor.Velocity = default(FieldVector);
                actor.ContactCooldownTicks = 0;
                actor.BlockCooldownTicks = 0;
            }
            model.CarrierId = model.OffenseOffset + 3;
            model.TargetPlayerId = -1;
            model.BallMode = BallMode.Held;
            model.BallPosition = model.Players[model.CarrierId].Position;
            model.BallHeight = 1.1f;
            model.ControlledPlayerId = model.Possession == TeamId.Home ? 3 : NearestHome(model, -1);
        }

        internal static void Update(SimulationState model, PlayerCommand command)
        {
            var directions = new FieldVector[model.Players.Length];
            var turbo = new bool[model.Players.Length];
            for (int id = 0; id < model.Players.Length; id++)
            {
                ActorState actor = model.Players[id];
                if (actor.ContactCooldownTicks > 0)
                    actor.ContactCooldownTicks--;
                if (actor.BlockCooldownTicks > 0)
                    actor.BlockCooldownTicks--;

                if (id == model.ControlledPlayerId)
                {
                    directions[id] = NormalizeInput(command.Move);
                    turbo[id] = command.Turbo;
                }
                else if (actor.Team == model.Possession)
                {
                    directions[id] = OffenseDirection(model, actor);
                    turbo[id] = id == model.CarrierId && actor.Team == TeamId.Away
                        && NearestOpponentDistanceSquared(model, actor) < 81f && actor.Turbo01 > 0.1f;
                }
                else
                {
                    directions[id] = DefenseDirection(model, actor);
                }
            }

            for (int id = 0; id < model.Players.Length; id++)
            {
                ActorState actor = model.Players[id];
                float speed = BaseSpeed(model, actor);
                bool requestingTurbo = turbo[id] && DistanceSquared(directions[id], default(FieldVector)) > 0f;
                if (requestingTurbo)
                {
                    speed += 3.9f * Math.Min(1f, actor.Turbo01 * 12f);
                    actor.Turbo01 = Math.Max(0f, actor.Turbo01 - TickSeconds / 3f);
                }
                else
                {
                    actor.Turbo01 = Math.Min(1f, actor.Turbo01 + TickSeconds / 4.5f);
                }
                if (actor.ContactCooldownTicks > 0)
                    speed *= 0.12f;

                FieldVector oldPosition = actor.Position;
                FieldVector next = Add(oldPosition, Scale(directions[id], speed * TickSeconds));
                actor.Position = Constrain(next, model.BallMode == BallMode.Held && id == model.CarrierId);
                actor.Velocity = Scale(Subtract(actor.Position, oldPosition), SliceRules.TicksPerSecond);
            }
            ApplyBlocks(model);
        }

        internal static FieldVector NormalizeInput(FieldVector input)
        {
            float x = IsFinite(input.X) ? input.X : 0f;
            float z = IsFinite(input.Z) ? input.Z : 0f;
            double magnitudeSquared = (double)x * x + (double)z * z;
            if (magnitudeSquared <= 1d)
                return new FieldVector(x, z);
            double inverse = 1d / Math.Sqrt(magnitudeSquared);
            return new FieldVector((float)(x * inverse), (float)(z * inverse));
        }

        internal static FieldVector ReceiverRoutePosition(SimulationState model, ActorState receiver, int liveTicks)
        {
            float seconds = liveTicks * TickSeconds;
            int slot = receiver.Id % SliceRules.PlayersPerTeam;
            bool quickPass = model.OffensivePlay == PlayId.QuickPass;
            float routeSpeed = quickPass ? (slot == 6 ? 10f : 8.8f) : (slot == 6 ? 9f : 8.2f);
            float forward = routeSpeed * seconds;
            float lateral = receiver.SnapPosition.Z;
            if (quickPass && slot == 4)
                lateral -= Math.Max(0f, seconds - 0.65f) * 3.5f;
            else if (quickPass && slot == 5)
                lateral -= Math.Max(0f, seconds - 0.7f) * 5f;
            else if (quickPass && slot == 6)
                lateral += Math.Min(seconds, 2f) * 0.8f;
            return Constrain(new FieldVector(receiver.SnapPosition.X + model.OffenseDirection * forward,
                GameplayRules.Clamp(lateral, -18f, 18f)), false);
        }

        internal static FieldVector ReceiverLeadVelocity(SimulationState model, ActorState receiver)
        {
            if (DistanceSquared(receiver.Velocity, default(FieldVector)) > 0.01f)
                return receiver.Velocity;
            FieldVector next = ReceiverRoutePosition(model, receiver, model.LiveTicks + 1);
            return Scale(NormalizeInput(Scale(Subtract(next, receiver.Position),
                SliceRules.TicksPerSecond / ReceiverSpeed)), ReceiverSpeed);
        }

        internal static int NearestHome(SimulationState model, int excludedId)
        {
            int nearest = -1;
            float best = float.MaxValue;
            for (int id = 0; id < SliceRules.PlayersPerTeam; id++)
            {
                if (id == excludedId)
                    continue;
                float distance = DistanceSquared(model.Players[id].Position, model.BallPosition);
                if (distance < best)
                {
                    nearest = id;
                    best = distance;
                }
            }
            return nearest;
        }

        internal static float NearestOpponentDistanceSquared(SimulationState model, ActorState actor)
        {
            float best = float.MaxValue;
            for (int id = 0; id < model.Players.Length; id++)
            {
                if (model.Players[id].Team == actor.Team)
                    continue;
                best = Math.Min(best, DistanceSquared(actor.Position, model.Players[id].Position));
            }
            return best;
        }

        internal static FieldVector Add(FieldVector a, FieldVector b)
        {
            return new FieldVector(a.X + b.X, a.Z + b.Z);
        }

        internal static FieldVector Subtract(FieldVector a, FieldVector b)
        {
            return new FieldVector(a.X - b.X, a.Z - b.Z);
        }

        internal static FieldVector Scale(FieldVector vector, float scale)
        {
            return new FieldVector(vector.X * scale, vector.Z * scale);
        }

        internal static float DistanceSquared(FieldVector a, FieldVector b)
        {
            float x = a.X - b.X;
            float z = a.Z - b.Z;
            return x * x + z * z;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static float BaseSpeed(SimulationState model, ActorState actor)
        {
            if (actor.Id == model.ControlledPlayerId)
                return ControlledSpeed;
            if (actor.Team != model.Possession)
                return 8.9f;
            if (model.BallMode == BallMode.Held && actor.Id == model.CarrierId)
                return 9.4f;
            int slot = actor.Id % SliceRules.PlayersPerTeam;
            return slot >= 4 ? ReceiverSpeed : slot == 3 ? 9.4f : 8.3f;
        }

        private static FieldVector OffenseDirection(SimulationState model, ActorState actor)
        {
            int slot = actor.Id % SliceRules.PlayersPerTeam;
            if (model.BallMode == BallMode.Held && actor.Id == model.CarrierId)
                return CarrierDirection(model, actor);
            if (slot < 3)
                return BlockingDirection(model, actor);
            if (slot >= 4)
            {
                FieldVector target = ReceiverRoutePosition(model, actor, model.LiveTicks);
                return NormalizeInput(Scale(Subtract(target, actor.Position), SliceRules.TicksPerSecond / ReceiverSpeed));
            }
            return default(FieldVector);
        }

        private static FieldVector CarrierDirection(SimulationState model, ActorState carrier)
        {
            if (carrier.Id % SliceRules.PlayersPerTeam == 3 && !model.ForwardPassUsed
                && model.OffensivePlay == PlayId.QuickPass && model.LiveTicks < 100)
                return new FieldVector(model.LiveTicks < 24 ? -model.OffenseDirection * 0.3f : 0f, 0f);

            ActorState nearest = null;
            float best = float.MaxValue;
            for (int id = 0; id < model.Players.Length; id++)
            {
                ActorState defender = model.Players[id];
                if (defender.Team == model.Possession)
                    continue;
                float ahead = model.OffenseDirection * (defender.Position.X - carrier.Position.X);
                if (ahead < -3f)
                    continue;
                float distance = DistanceSquared(carrier.Position, defender.Position);
                if (distance < best)
                {
                    best = distance;
                    nearest = defender;
                }
            }
            float targetZ = carrier.Position.Z * 0.9f;
            if (nearest != null && best < 100f)
            {
                float side = carrier.Position.Z - nearest.Position.Z;
                int sign = Math.Abs(side) < 0.3f ? model.RunLane : side > 0f ? 1 : -1;
                targetZ = GameplayRules.Clamp(carrier.Position.Z + sign * 7f, -18f, 18f);
            }
            return NormalizeInput(new FieldVector(model.OffenseDirection * 11f, targetZ - carrier.Position.Z));
        }

        private static FieldVector BlockingDirection(SimulationState model, ActorState blocker)
        {
            ActorState carrier = model.Players[model.BallMode == BallMode.Held ? model.CarrierId : model.OffenseOffset + 3];
            int slot = blocker.Id % SliceRules.PlayersPerTeam;
            if (model.LiveTicks <= 75)
            {
                int defenseOffset = (int)GameplayRules.Opponent(model.Possession) * SliceRules.PlayersPerTeam;
                ActorState assignment = model.Players[defenseOffset + slot];
                if (model.OffenseDirection * (assignment.Position.X - carrier.Position.X) >= -3f
                    && DistanceSquared(blocker.Position, assignment.Position) < 225f)
                    return NormalizeInput(Subtract(assignment.Position, blocker.Position));
            }
            FieldVector escort = new FieldVector(carrier.Position.X + model.OffenseDirection * 3.5f,
                carrier.Position.Z + (slot - 1) * 3.7f);
            ActorState threat = null;
            float best = float.MaxValue;
            for (int id = 0; id < model.Players.Length; id++)
            {
                ActorState defender = model.Players[id];
                if (defender.Team == model.Possession)
                    continue;
                if (model.OffenseDirection * (defender.Position.X - carrier.Position.X) < -2f)
                    continue;
                float distance = DistanceSquared(escort, defender.Position);
                if (distance < best)
                {
                    best = distance;
                    threat = defender;
                }
            }
            FieldVector target = threat != null && best < 144f ? threat.Position : escort;
            return NormalizeInput(Subtract(target, blocker.Position));
        }

        private static FieldVector DefenseDirection(SimulationState model, ActorState defender)
        {
            FieldVector target;
            int slot = defender.Id % SliceRules.PlayersPerTeam;
            if (model.BallMode == BallMode.InFlight)
            {
                bool rushing = slot < 3 || (model.DefensivePlay == PlayId.Blitz && slot < 5);
                if (rushing)
                {
                    target = model.Players[model.OffenseOffset + 3].Position;
                }
                else
                {
                    ActorState receiver = model.Players[model.OffenseOffset + 4 + (slot - 3) % 3];
                    target = new FieldVector(receiver.Position.X + model.OffenseDirection * 2.5f, receiver.Position.Z);
                }
            }
            else
            {
                ActorState carrier = model.Players[model.CarrierId];
                target = Add(carrier.Position, Scale(carrier.Velocity, 0.3f));
                if (model.DefensivePlay == PlayId.Cover && slot >= 3 && model.LiveTicks < 75
                    && DistanceSquared(defender.Position, carrier.Position) > 81f)
                {
                    ActorState receiver = model.Players[model.OffenseOffset + 4 + (slot - 3) % 3];
                    target = new FieldVector(receiver.Position.X + model.OffenseDirection * 2.5f, receiver.Position.Z);
                }
            }
            return NormalizeInput(Subtract(target, defender.Position));
        }

        private static void ApplyBlocks(SimulationState model)
        {
            for (int slot = 0; slot < 3; slot++)
            {
                ActorState blocker = model.Players[model.OffenseOffset + slot];
                if (blocker.BlockCooldownTicks > 0)
                    continue;
                ActorState nearest = null;
                float best = 1.8f * 1.8f;
                for (int id = 0; id < model.Players.Length; id++)
                {
                    ActorState defender = model.Players[id];
                    if (defender.Team == model.Possession || defender.ContactCooldownTicks > 0)
                        continue;
                    float distance = DistanceSquared(blocker.Position, defender.Position);
                    if (distance < best)
                    {
                        nearest = defender;
                        best = distance;
                    }
                }
                if (nearest != null)
                {
                    nearest.ContactCooldownTicks = 72;
                    blocker.BlockCooldownTicks = 90;
                }
            }
        }

        private static FieldVector Constrain(FieldVector position, bool carrier)
        {
            float margin = carrier ? 0f : 1f;
            return new FieldVector(
                GameplayRules.Clamp(position.X, -SliceRules.EndZoneDepthYards + margin,
                    SliceRules.FieldLengthYards + SliceRules.EndZoneDepthYards - margin),
                GameplayRules.Clamp(position.Z, -SliceRules.FieldHalfWidthYards + margin,
                    SliceRules.FieldHalfWidthYards - margin));
        }
    }
}
