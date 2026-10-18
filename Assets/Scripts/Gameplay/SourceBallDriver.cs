using System;
using Blitz.Contracts;

namespace Blitz.Gameplay
{
    [Flags]
    internal enum SourceBallFlags : uint
    {
        None = 0, White = 0x1, VelocityPitch = 0x2, TurboPass = 0x4,
        FirePass = 0x8, Bobble = 0x10, Bounce = 0x20, Alternate = 0x40,
        Freeze = 0x80, TargetOffscreen = 0x100, Deflected = 0x200,
        Spinning = 0x400, Phantom = 0x800, FieldGoalBounce = 0x1000,
        HitPost = 0x2000
    }

    internal readonly struct SourceBallStep
    {
        internal readonly bool EndPlay;
        internal readonly SourcePlayEndCause Cause;

        internal SourceBallStep(SourcePlayEndCause cause)
        {
            EndPlay = true;
            Cause = cause;
        }
    }

    // player.c ball_proc, bobble_pass, fumble_ball and pmisc.c deflect_pass.
    // Positions remain source-world binary32 until the detached view is made.
    internal static class SourceBallDriver
    {
        internal const float Gravity = -0.08f;
        internal const float HalfLength = 468f;
        internal const float HalfWidth = 182.85f;
        private const SourceBallFlags ResetOnThrow = SourceBallFlags.Bounce | SourceBallFlags.Bobble
            | SourceBallFlags.FirePass | SourceBallFlags.TurboPass | SourceBallFlags.FieldGoalBounce
            | SourceBallFlags.HitPost;

        internal static SourceVector3 SourcePosition(FieldVector position, float height)
        {
            return new SourceVector3(SourceMatchRules.SourceWorldFromAbsoluteYards(position.X),
                height * SourceTiming.SourceUnitsPerYard, position.Z * SourceTiming.SourceUnitsPerYard);
        }

        internal static void SetPosition(SimulationState model, SourceVector3 position)
        {
            model.SourceBallPosition = position;
            Publish(model);
        }

        internal static void SetTarget(SimulationState model, float x, float z)
        {
            model.SourceBallTarget = new SourceVector3(x, 0f, z);
            model.PassTarget = new FieldVector(SourceMatchRules.AbsoluteYardsFromSourceWorld(x),
                z / SourceTiming.SourceUnitsPerYard);
        }

        internal static void Publish(SimulationState model)
        {
            model.BallPosition = new FieldVector(
                SourceMatchRules.AbsoluteYardsFromSourceWorld(model.SourceBallPosition.X),
                model.SourceBallPosition.Z / SourceTiming.SourceUnitsPerYard);
            model.BallHeight = model.SourceBallPosition.Y / SourceTiming.SourceUnitsPerYard;
            model.BallVelocity = PlayerSimulation.Scale(model.SourceBallVelocity,
                SourceTiming.DefaultTicksPerSecond / SourceTiming.SourceUnitsPerYard);
            model.BallVerticalVelocity = model.SourceBallVerticalVelocity
                * SourceTiming.DefaultTicksPerSecond / SourceTiming.SourceUnitsPerYard;
        }

        internal static bool InBounds(SourceVector3 point)
        {
            return Math.Abs(point.X) <= HalfLength && Math.Abs(point.Z) <= HalfWidth;
        }

        internal static void Bobble(SimulationState model, ActorState actor, SourceVector3 leftWrist,
            float bx, float by, float bz, Func<int, int> random)
        {
            model.SourceBobbles[(int)actor.Team]++;
            SetPosition(model, leftWrist);
            float vx = bx + (random(12) - 6f) * 0.10f;
            float vz = bz + (random(12) - 6f) * 0.10f;
            float vy = by + (random(10) + 10f) * 0.10f;
            model.SourceBallVelocity = new FieldVector(vx, vz);
            model.SourceBallVerticalVelocity = vy;
            model.PassTicksElapsed = 0;
            model.TargetPlayerId = -1;
            model.BallKind = model.BallKind == SourceBallKind.Pass || model.BallKind == SourceBallKind.Bobble
                ? SourceBallKind.Bobble : SourceBallKind.Fumble;
            model.ThrowerId = actor.Id;
            model.LastTouchPlayerId = actor.Id;
            model.CarrierId = -1;
            model.BallMode = BallMode.InFlight;
            model.SourceFlags |= SourceGameFlags.Bobble;
            model.SourceFlags &= ~SourceGameFlags.PlayEnding;
            model.SourceBallFlags |= SourceBallFlags.Bobble;
            float time = SolveQuad(Gravity / 2f, vy, leftWrist.Y);
            SetTarget(model, leftWrist.X + time * vx, leftWrist.Z + time * vz);
            Publish(model);
        }

        internal static void Fumble(SimulationState model, ActorState actor, SourceVector3 leftWrist,
            float bx, float bz, Func<int, int> random)
        {
            if (actor.SourceMode == SourceActorMode.Quarterback)
                actor.SourceMode = SourceActorMode.Receiver;
            model.SourceFumbles[(int)actor.Team]++;
            SetPosition(model, leftWrist);
            float vx = bx + (random(8) - 4f) * 0.30f;
            float vz = bz + (random(8) - 4f) * 0.30f;
            float vy = random(4);
            model.SourceBallVelocity = new FieldVector(vx, vz);
            model.SourceBallVerticalVelocity = vy;
            model.PassTicksElapsed = 0;
            model.TargetPlayerId = -1;
            model.BallKind = SourceBallKind.Fumble;
            model.ThrowerId = actor.Id;
            model.LastTouchPlayerId = actor.Id;
            model.SourceBallFlags &= ~ResetOnThrow;
            model.CarrierId = -1;
            model.BallMode = BallMode.InFlight;
            model.SourceFumbleX = SourcePlayerDriver.WorldPosition(actor).X;
            model.SourceFlags |= SourceGameFlags.Fumble;
            model.SourceFlags &= ~SourceGameFlags.PlayEnding;
            float time = PositiveRoot(Gravity / 2f, vy, leftWrist.Y);
            SetTarget(model, leftWrist.X + time * vx, leftWrist.Z + time * vz);
            Publish(model);
        }

        internal static void Punt(SimulationState model, ActorState actor, SourceVector3 ankle, Func<int, int> random)
        {
            model.SourceBallOrigin = ankle;
            SetPosition(model, ankle);
            int maxX = (int)((50 - 20) * (float)(100 - model.SourceRules.LineOfScrimmage) / 100f);
            int maxY = (int)((60 - 40) * (float)(100 - model.SourceRules.LineOfScrimmage) / 100f);
            float vx = (20 + maxX * model.SourcePuntStrength / 100) / 10f;
            float vy = (40 + maxY * model.SourcePuntStrength / 100) / 10f;
            float vz = -1f + random(201) / 100f;
            if (model.Possession == TeamId.Away) vx *= -1f;
            model.SourceBallVelocity = new FieldVector(vx, vz);
            model.SourceBallVerticalVelocity = vy;
            model.SourceBallPitchRate = -45;
            model.SourceBallFlags &= ~(SourceBallFlags.VelocityPitch | SourceBallFlags.FieldGoalBounce | SourceBallFlags.HitPost);
            int time = (int)(-2f * vy / Gravity);
            SetTarget(model, ankle.X + time * vx, ankle.Z + time * vz);
            model.PassTicksElapsed = 0;
            model.PassTicksTotal = time;
            Publish(model);
        }

        internal static void FieldGoal(SimulationState model, ActorState actor, Func<int, int> random)
        {
            model.SourceBallOrigin = model.SourceBallPosition;
            bool human = SourceContactDriver.IsHumanTeam(model, actor.Team);
            int los = model.SourceRules.LineOfScrimmage;
            int success;
            if (human && model.SourceHikeCounterAtSnap > 0)
            {
                int yards = Math.Min(50, Math.Max(5, 100 - los));
                int minimum = 20 + (yards - 5) * 4 / 3;
                int maximum = 80 + (yards - 5) / 3;
                float accuracy = model.SourceKickMeterPercent >= 0f
                    ? 100f - model.SourceKickMeterPercent : 100f + model.SourceKickMeterPercent;
                success = (int)((accuracy - minimum) * 100f / (maximum - minimum));
            }
            else success = 3 * los - (human ? 175 : 125);
            if (model.Possession == TeamId.Home) model.SourceKickMeterPercent = -model.SourceKickMeterPercent;
            if (random(5) == 0) model.SourceKickMeterPercent = -model.SourceKickMeterPercent;
            int lead = model.Possession == TeamId.Home ? model.HomeScore - model.AwayScore : model.AwayScore - model.HomeScore;
            if (lead == 0 && model.Quarter - 1 > 2 && !model.SourceNoAssist && model.SourceClock.PackedTime < 0x1e00)
                success -= 30;
            if (lead == -3 && model.Quarter - 1 > 2 && !model.SourceNoAssist) success += 35;
            if (!human && model.SourceDronesLose && lead < 0 && lead > -3
                && model.Quarter - 1 > 2 && model.SourceClock.PackedTime < 0x2800)
                success -= 20;
            success = Math.Min(success, 95);
            success += (int)model.SourcePlaybook.HandicapValue("fg_bonus_ht", actor.SourceHandicap);
            float targetX = model.Possession == TeamId.Away ? -460f : 460f;
            // GOAL_TOP-GOAL_CROSSBAR-3 is 73.5f, implicitly cast to randrng's int.
            float targetY = 30f + random(73);
            float targetZ = model.SourceKickMeterPercent > 0f ? -22f : 22f;
            if (model.SourceTeamFire[(int)model.Possession] || model.SourcePerfectFieldGoal) targetZ = 0f;
            int actual = Math.Max(-50, success - random(100));
            if (targetZ != 0f)
            {
                if (actual > 0)
                    targetZ = Math.Max(40 - actual, 0) / 40f * 22f * (random(2) != 0 ? -1f : 1f);
                else if (actual > -25)
                {
                    if (random(100) < 40)
                    {
                        targetY = 22f;
                        targetZ = targetZ * random(86) * 0.01f;
                    }
                }
                else targetZ = targetZ * -65f / actual;
            }
            float distance = Math.Abs(targetX - model.SourceBallPosition.X);
            float time = 5f * (float)Math.Sqrt(distance - targetY);
            float delta = distance / time;
            model.SourceBallVelocity = new FieldVector(model.Possession == TeamId.Away ? -delta : delta,
                (targetZ - model.SourceBallPosition.Z) / time);
            model.SourceBallVerticalVelocity = delta;
            model.PassTicksElapsed = 0;
            model.PassTicksTotal = (int)time;
            // The active routine leaves tx/tz from the snap unchanged.
            Publish(model);
        }

        internal static void Drop(SimulationState model, ActorState actor, SourceVector3 wrist, Func<int, int> random)
        {
            if ((model.SourceBallFlags & SourceBallFlags.Phantom) != 0) return;
            SetPosition(model, wrist);
            float vx = random(3) * 0.15f;
            float vz = random(3) * 0.15f;
            model.SourceBallVelocity = new FieldVector(vx, vz);
            model.SourceBallVerticalVelocity = 0.43f;
            model.PassTicksElapsed = 0;
            model.TargetPlayerId = -1;
            model.SourceBallFlags &= ~ResetOnThrow;
            model.SourceBallFlags |= SourceBallFlags.Phantom;
            model.BallKind = SourceBallKind.Fumble;
            actor.SourceMode = SourceActorMode.Receiver;
            // who_threw and the actual carrier are deliberately retained.
            Publish(model);
        }

        internal static void Spin(SimulationState model, SourceVector3 rightWrist)
        {
            ClearCelebrationFlags(model);
            model.SourceBallFlags |= SourceBallFlags.Spinning;
            model.SourceBallElevation = 0;
            model.SourceBallRoll = 45;
            SetPosition(model, new SourceVector3(rightWrist.X, 3f, rightWrist.Z));
            model.SourceBallVelocity = default(FieldVector);
            model.SourceBallVerticalVelocity = 0f;
            model.CarrierId = -1;
            Publish(model);
        }

        internal static void Spike(SimulationState model, ActorState actor, SourceVector3 rightWrist,
            bool fling, Func<int, int> random)
        {
            model.CarrierId = -1;
            if (!fling) model.SourceBallFlags |= SourceBallFlags.VelocityPitch;
            model.ThrowerId = actor.Id;
            ClearCelebrationFlags(model);
            model.SourceBallOrigin = rightWrist;
            SetPosition(model, rightWrist);
            if (!fling)
            {
                model.SourceBallVelocity = new FieldVector(1.5f + random(10000) / 9000f, 0f);
                model.SourceBallVerticalVelocity = -5.5f;
            }
            else
            {
                model.SourceBallVelocity = SourceAnimationLayerState.Rotate(0f, 1.5f, actor.Facing - 576,
                    model.SourcePlaybook.Directions);
                model.SourceBallVerticalVelocity = 1.8f;
            }
            // set_spike_ball_vels declares a,b,c,t as int, including a == 0.
            int a = (int)(Gravity / 2f), b = (int)model.SourceBallVerticalVelocity, c = (int)rightWrist.Y;
            int time = (int)SolveQuad(a, b, c);
            SetTarget(model, rightWrist.X + time * model.SourceBallVelocity.X,
                rightWrist.Z + time * model.SourceBallVelocity.Z);
            model.PassTicksElapsed = 0;
            model.PassTicksTotal = time;
            Publish(model);
        }

        private static void ClearCelebrationFlags(SimulationState model)
        {
            // The source mixes GF_PASS_THROWN into ball flags: its 0x80 clears BF_FREEZE.
            model.SourceBallFlags &= ~(ResetOnThrow | SourceBallFlags.Freeze);
        }

        internal static void Deflect(SimulationState model, ActorState actor, Func<int, int> random)
        {
            model.SourceDeflections[(int)actor.Team]++;
            float vx = model.SourceBallVelocity.X;
            float vy = model.SourceBallVerticalVelocity;
            float vz = model.SourceBallVelocity.Z;
            float magnitude = (float)Math.Sqrt(vx * vx + vy * vy + vz * vz);
            if (magnitude < 0.001f)
                return;
            vx /= magnitude;
            vy /= magnitude;
            vz /= magnitude;
            magnitude *= (65 + random(31)) / 100f;
            int transfer = random(4) != 0 ? 10 + random(41) : 50 + random(41);
            float loss = transfer / 100f;
            // Active source uses v[0]+v[1], not horizontal magnitude.
            vy += loss * (vx + vy);
            vx *= 1f - loss;
            vz *= 1f - loss;
            int rotation = random(257) - random(257);
            model.SourceBallVelocity = SourceAnimationLayerState.Rotate(vx * magnitude, vz * magnitude, rotation,
                model.SourcePlaybook.Directions);
            model.SourceBallVerticalVelocity = Math.Min(vy * magnitude, 2.7f);
            model.SourceBallFlags |= SourceBallFlags.Deflected;
            model.SourceBallFlags &= ~SourceBallFlags.VelocityPitch;
            model.LastTouchPlayerId = actor.Id;
            float time = SolveQuad(Gravity / 2f, model.SourceBallVerticalVelocity,
                model.SourceBallPosition.Y);
            SetTarget(model, model.SourceBallPosition.X + time * model.SourceBallVelocity.X,
                model.SourceBallPosition.Z + time * model.SourceBallVelocity.Z);
            Publish(model);
        }

        internal static SourceBallStep Advance(SimulationState model, Func<int, int> random)
        {
            if (model.CarrierId >= 0
                && (model.SourceBallFlags & SourceBallFlags.Phantom) == 0)
                return default(SourceBallStep);
            bool live = model.Phase == GamePhase.LiveBall;
            SourceVector3 p = model.SourceBallPosition;
            if ((model.SourceFlags & SourceGameFlags.ForwardPass) == 0
                && model.BallKind == SourceBallKind.Pass
                && (model.SourceBallFlags & SourceBallFlags.Phantom) == 0
                && SourceMatchRules.FieldPosition(p.X, (int)model.Possession) >= model.SourceRules.LineOfScrimmage)
                model.SourceFlags |= SourceGameFlags.ForwardPass;
            model.SourceBallPrevious = p;
            if ((model.SourceBallFlags & (SourceBallFlags.Freeze | SourceBallFlags.Spinning)) == 0)
            {
                p = new SourceVector3(p.X + model.SourceBallVelocity.X,
                    p.Y + (model.SourceBallVerticalVelocity + Gravity / 2f),
                    p.Z + model.SourceBallVelocity.Z);
                model.SourceBallVerticalVelocity += Gravity;
            }
            p = GoalPost(model, p, random);
            SourceBallStep result = default(SourceBallStep);
            float direction = model.Possession == TeamId.Home ? 1f : -1f;
            SourceVector3 previous = model.SourceBallPrevious;
            if (model.BallKind == SourceBallKind.FieldGoal && direction * previous.X < 460f
                && direction * p.X >= 460f)
            {
                // Preserve the active asymmetric source denominator.
                float t = (460f - direction * previous.X) / (p.X - direction * previous.X);
                float y = previous.Y + t * (p.Y - previous.Y);
                float z = previous.Z + t * (p.Z - previous.Z);
                if (y >= 27f && Math.Abs(z) <= 22f)
                {
                    model.SourceFlags |= SourceGameFlags.ThroughUprights | SourceGameFlags.PlayEnding;
                    result = new SourceBallStep(SourcePlayEndCause.DeadBallInBounds);
                    live = false;
                }
            }
            if (p.Y <= 0.8f * model.SourceBallScale)
            {
                model.SourceFlags &= ~SourceGameFlags.HikingBall;
                if (live && model.CarrierId == -1)
                {
                    model.SourceBallBounces++;
                    if (InBounds(p))
                    {
                        if (model.BallKind == SourceBallKind.Pass || model.BallKind == SourceBallKind.Bobble
                            || model.BallKind == SourceBallKind.FieldGoal)
                        {
                            model.SourceFlags |= SourceGameFlags.PlayEnding;
                            result = new SourceBallStep(SourcePlayEndCause.DeadBallInBounds);
                            live = false;
                        }
                        else if (model.BallKind == SourceBallKind.Kickoff)
                        {
                            model.ClockActive = true;
                            model.SourceClock = SourceClock.ResetPlayTime(model.SourceClock);
                        }
                    }
                }
                model.SourceBallFlags |= SourceBallFlags.FieldGoalBounce;
                if (live) model.SourceBallFlags |= SourceBallFlags.Bounce;
                if ((model.SourceBallFlags & SourceBallFlags.Spinning) == 0)
                {
                    p = new SourceVector3(p.X, 0.8f * model.SourceBallScale, p.Z);
                    model.SourceBallBounceTick = model.SourceTick;
                }
                bool onside = (model.SourceFlags & SourceGameFlags.OnsideKick) != 0
                    && model.BallKind == SourceBallKind.Kickoff;
                if (onside)
                {
                    model.SourceBallVerticalVelocity = (float)(model.SourceBallVerticalVelocity * -0.9d);
                    model.SourceBallVerticalVelocity = (float)(model.SourceBallVerticalVelocity - 0.02d);
                }
                else if ((model.SourceBallFlags & SourceBallFlags.Spinning) == 0)
                {
                    model.SourceBallVerticalVelocity = (float)(model.SourceBallVerticalVelocity * -0.5d);
                    model.SourceBallVerticalVelocity = (float)(model.SourceBallVerticalVelocity - 0.10d);
                }
                if (model.SourceBallVerticalVelocity <= 0f)
                {
                    model.SourceBallVelocity = default(FieldVector);
                    model.SourceBallVerticalVelocity = 0f;
                    SetTarget(model, p.X, p.Z);
                }
                else
                {
                    float vx = model.SourceBallVelocity.X, vz = model.SourceBallVelocity.Z;
                    float magnitude = (float)Math.Sqrt(vx * vx + vz * vz);
                    int theta = magnitude > 0f ? Asin(model.SourcePlaybook.Directions, vz / magnitude) : 0;
                    if (vx < 0f) theta = 512 - theta;
                    if (onside)
                        theta += random(513) - random(513);
                    else
                    {
                        theta += random(201);
                        theta += random(157);
                        theta += random(157);
                        theta -= random(201);
                        theta -= random(157);
                        theta -= random(157);
                    }
                    var heading = new FieldVector(model.SourcePlaybook.Directions.Sin(theta), model.SourcePlaybook.Directions.Cos(theta));
                    model.SourceBallVelocity = new FieldVector(0.5f * magnitude * heading.Z,
                        0.5f * magnitude * heading.X);
                    float time = -2f * model.SourceBallVerticalVelocity / Gravity;
                    SetTarget(model, p.X + time * model.SourceBallVelocity.X, p.Z + time * model.SourceBallVelocity.Z);
                }
            }
            if (!InBounds(p) && live)
            {
                result = new SourceBallStep(SourcePlayEndCause.BallOutOfBounds);
                model.SourceFlags |= SourceGameFlags.PlayEnding;
                if (model.SourceBallBounces > 2)
                {
                    model.SourceBallVelocity = default(FieldVector);
                    model.SourceBallVerticalVelocity = 0f;
                }
            }
            model.SourceBallPosition = p;
            model.PassTicksElapsed++;
            model.SourceBallFlags &= ~SourceBallFlags.White;
            Orient(model);
            if (model.SourceRules.PlayType != SourcePlayType.Kickoff
                && SourceMatchRules.FieldPosition(p.X, (int)model.Possession) >= model.SourceRules.LineOfScrimmage)
                model.SourceFlags |= SourceGameFlags.BallCrossedLine;
            Publish(model);
            return result;
        }

        private static SourceVector3 GoalPost(SimulationState model, SourceVector3 p, Func<int, int> random)
        {
            SourceVector3 previous = model.SourceBallPrevious;
            if ((model.SourceBallFlags & SourceBallFlags.HitPost) != 0
                || model.SourceTick - model.SourceBallBounceTick < 15
                || Math.Abs(p.X) < 460f || Math.Abs(previous.X) > 460f)
                return p;
            float z = Math.Abs(p.Z);
            if (Math.Abs(z - 22f) < 1.9f && p.Y >= 26f && p.Y <= 105.5f)
            {
                model.SourceBallVelocity = SourceAnimationLayerState.Rotate(model.SourceBallVelocity.X,
                    model.SourceBallVelocity.Z, random(1024), model.SourcePlaybook.Directions);
                p = new SourceVector3(previous.X + model.SourceBallVelocity.X, p.Y,
                    previous.Z + model.SourceBallVelocity.Z);
            }
            else if (z <= 24f && p.Y >= 25f && p.Y <= 29f)
            {
                float vx = model.SourceBallVelocity.X, vy = model.SourceBallVerticalVelocity,
                    vz = model.SourceBallVelocity.Z;
                float velocity = (float)Math.Sqrt(vx * vx + vy * vy + vz * vz);
                int percent = random(31) + 60;
                int zp = random(21);
                int yp = random(101 - zp);
                int xp = 100 - yp - zp;
                velocity *= percent * 0.01f;
                vx = (vx >= 0f ? velocity : -velocity) * (random(4) != 0 ? xp : -xp) * 0.01f;
                vy = (vy >= 0f ? velocity : -velocity) * (random(3) != 0 ? -yp : yp) * 0.01f;
                vz = (vz >= 0f ? velocity : -velocity) * (random(2) != 0 ? zp : -zp) * 0.01f;
                model.SourceBallVelocity = new FieldVector(vx, vz);
                model.SourceBallVerticalVelocity = vy;
                p = new SourceVector3(previous.X + vx, previous.Y + (vy - Gravity) + Gravity / 2f,
                    previous.Z + vz);
            }
            else return p;
            model.SourceBallBounceTick = model.SourceTick;
            model.SourceBallFlags |= SourceBallFlags.HitPost;
            return p;
        }

        internal static float PositiveRoot(float a, float b, float c)
        {
            float discriminant = b * b - 4f * a * c;
            if (discriminant < 0f)
                return 0f;
            float root = (float)Math.Sqrt(discriminant);
            float first = (-1f * b + root) / (2f * a);
            float second = (-1f * b - root) / (2f * a);
            return first < second ? second : first;
        }

        internal static float SolveQuad(float a, float b, float c)
        {
            if (Math.Abs(c) < 0.001f) return 0f;
            if (Math.Abs(b) < 0.001f && Math.Abs(a) < 0.001f) return 200000f;
            if (Math.Abs(a) < 0.001f) return -1f * c / b;
            if (Math.Abs(b) < 0.001f)
                return -c / a < 0f ? 300000f : (float)Math.Sqrt(-c / a);
            float discriminant = b * b - 4f * a * c;
            if (discriminant < 0f) return 100000f;
            float root = (float)Math.Sqrt(discriminant);
            float first = (-1f * b + root) / (2f * a);
            float second = (-1f * b - root) / (2f * a);
            if (first >= 0f && second >= 0f) return Math.Min(first, second);
            return Math.Max(first, second);
        }

        private static int Asin(SourceDirectionMath directions, float value)
        {
            if (!SourcePoseMath.IsFinite(value) || value < -1f || value > 1f)
                throw new InvalidOperationException("Source iasin argument outside its original table domain: " + value);
            bool negative = value < 0f;
            float x = negative ? -value : value;
            int low = 0, high = 255;
            while (low < high)
            {
                int mid = (low + high) / 2;
                if (directions.Sin(mid + 1) < x) low = mid + 1;
                else high = mid;
            }
            float ld = directions.Sin(low) - x, hd = directions.Sin(low + 1) - x;
            int result = hd * hd < ld * ld ? low + 1 : low;
            return negative ? -result : result;
        }

        private static void Orient(SimulationState model)
        {
            float vx = model.SourceBallVelocity.X, vy = model.SourceBallVerticalVelocity,
                vz = model.SourceBallVelocity.Z;
            if (vx == 0f && vz == 0f)
            {
                if (model.Phase == GamePhase.PreSnap && model.SourceRules.PlayType == SourcePlayType.Kickoff)
                {
                    model.SourceBallElevation = 384;
                    model.SourceBallHeading = model.Possession == TeamId.Home ? 256 : 768;
                }
                else if ((model.SourceBallFlags & SourceBallFlags.Spinning) != 0)
                {
                    model.SourceBallRoll += 80;
                    model.SourceBallElevation = model.SourceBallElevation < 256
                        ? model.SourceBallElevation + 5 : 256;
                }
                else model.SourceBallElevation = 0;
                return;
            }
            float horizontal = (float)Math.Sqrt(vx * vx + vz * vz);
            int angle = Asin(model.SourcePlaybook.Directions, vx / horizontal);
            if (vz < 0f) angle = (512 - angle) & 1023;
            model.SourceBallHeading = angle;
            if ((model.SourceBallFlags & SourceBallFlags.VelocityPitch) != 0)
                model.SourceBallElevation = Asin(model.SourcePlaybook.Directions,
                    vy / (float)Math.Sqrt(vx * vx + vy * vy + vz * vz));
            else
            {
                model.SourceBallPitch += model.SourceBallPitchRate;
                model.SourceBallElevation = model.SourceBallPitch % 1024;
            }
            if (model.BallKind == SourceBallKind.Pass && (model.SourceBallFlags & SourceBallFlags.Bobble) == 0)
                model.SourceBallRoll += 60;
        }
    }
}
