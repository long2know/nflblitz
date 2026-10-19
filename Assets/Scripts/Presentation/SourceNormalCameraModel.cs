using System;
using Blitz.Contracts;

namespace Blitz.Presentation
{
    public readonly struct SourceNormalCameraPlan
    {
        public SourceCinematicSeed State { get; }
        public SourceVector3 Target { get; }
        public float DistanceTarget { get; }
        public int CarrierMode { get; }
        public int PlayerMask { get; }
        public int PlayerCount { get; }
        public float HorizontalDistance { get; }
        public float DepthDistance { get; }
        public bool Gopher { get { return Flags == 0; } }
        internal int Flags { get; }
        internal float DistanceAcceleration { get; }
        internal SourceNormalCameraPlan WithTarget(SourceVector3 target, float distance)
        { return new SourceNormalCameraPlan(State, target, distance, CarrierMode, PlayerMask, PlayerCount,
            HorizontalDistance, DepthDistance, Flags, DistanceAcceleration); }
        internal SourceNormalCameraPlan(SourceCinematicSeed state, SourceVector3 target, float distance,
            int carrierMode, int mask, int count, float horizontal, float depth, int flags, float distanceAcceleration)
        {
            State = state; Target = target; DistanceTarget = distance; CarrierMode = carrierMode;
            PlayerMask = mask; PlayerCount = count; HorizontalDistance = horizontal; DepthDistance = depth;
            Flags = flags; DistanceAcceleration = distanceAcceleration;
        }
    }

    // The active release CAM_NORMAL producer, in original centered source-world units.
    // Presentation time, actor bounds, browser aspect and HUD layout are deliberately not inputs.
    public sealed class SourceNormalCameraModel
    {
        private const int Unrotate = 1, Snap = 2, AcceleratePosition = 4, AccelerateAngles = 8;
        private const int MovePosition = 16, MoveAngles = 32, MoveEye = 64;
        private const float UnitsPerYard = 390f / 50f, UnitsPerFoot = UnitsPerYard / 3f;
        private const float FocalFactor = 390f / 300f, QbSideFactor = .68f * .82f;
        private static readonly float NormalPhi = SourceCinematicMath.Radians(-25f);
        private static readonly float Alpha = SourceCinematicMath.Radians(20.556f) / FocalFactor;
        private static readonly float SinAlpha = (float)Math.Sin(Alpha);
        private readonly float[] rotatedX = new float[14], rotatedZ = new float[14];
        private float carrierGoalDistance;
        private int showSideline;
        private int observedQuarter = -1;
        public int WasPlayOver { get; private set; } = 1;
        public bool NewQuarter { get; private set; } = true;
        public bool KickoffFlag { get; private set; }
        public long NormalWakes { get; private set; }
        public bool TargetOffscreen { get; private set; }
        public SourceNormalCameraPlan LastPlan { get; private set; }
        internal void CommitPlan(SourceNormalCameraPlan plan) { LastPlan = plan; }

        public static SourceCinematicSeed Initial(SourceCameraBoundary input)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            return new SourceCinematicSeed(new SourceVector3(input.LastPossession != 0 ? 390f : -390f, 25f,
                    input.LastPossession != 0 ? -35f : 35f),
                new SourceVector3(WorldX(30, input.OffenseSide), 6f * UnitsPerFoot, 0f), 100f,
                SourceCinematicMath.Radians(90), NormalPhi);
        }

        public bool BeginWake(SourceCameraBoundary input, bool transient)
        {
            if (input == null || input.RawGameMode == 6)
                throw new InvalidOperationException("CAMBOT does not wake while GM_INITIALIZING.");
            if (observedQuarter >= 0 && observedQuarter != input.QuarterIndex) NewQuarter = true;
            observedQuarter = input.QuarterIndex;
            TargetOffscreen = (input.Ball.RawFlags & 0x100) != 0;
            if (input.RawPlayType != 1) KickoffFlag = false;
            bool caughtKick = KickoffFlag && (input.Carrier >= 0 || (input.Ball.RawFlags & 0x20) != 0);
            if (caughtKick) KickoffFlag = false;
            return transient && (caughtKick || BeginsNewPlay(input));
        }

        public bool BeginsNewPlay(SourceCameraBoundary input)
        { return WasPlayOver != 0 && (input.RawGameMode == 0 || input.RawGameMode == 1); }

        public bool WantsOpeningKickoff(SourceCameraBoundary input)
        { return NewQuarter && WasPlayOver <= 1 && input.QuarterIndex == 0; }

        public void StartedOpeningKickoff()
        { NewQuarter = false; KickoffFlag = true; }

        public void CompleteWake(SourceCameraBoundary input)
        {
            if (input.RawGameMode == 3 || input.RawGameMode == 4) WasPlayOver = 2;
            else if (WasPlayOver != 0) WasPlayOver--;
        }

        public float MinimumDistance(SourceCameraBoundary input, bool quarterback)
        { return (quarterback ? input.HumanTeammates || input.Pullback ? 38f : 30f :
                input.HumanTeammates || input.Pullback ? 27f : 19f) * UnitsPerYard; }

        public SourceNormalCameraPlan Prepare(SourceCinematicSeed current, SourceCameraBoundary input,
            float horizontalResolution, float verticalResolution, Func<int, string, int> random,
            bool snapReturnAngles = false, bool snapTarget = false, bool unrotateTargets = true)
        {
            SourceCameraFrameClock.RequireOriginalRasterAspect(horizontalResolution, verticalResolution);
            if (random == null) throw new ArgumentNullException(nameof(random));
            NormalWakes++;
            float thetaTarget = SourceCinematicMath.Radians(input.LastPossession != 0 ? 270f : 90f);
            if (snapReturnAngles)
                current = Copy(current, current.Eye, current.Focus, current.Distance, thetaTarget, NormalPhi,
                    current.Velocity, current.DistanceVelocity, current.ThetaVelocity, current.PhiVelocity,
                    thetaTarget, NormalPhi);
            float sinTheta = current.SinTheta, cosTheta = current.CosTheta;
            float sinPhi = current.SinPhi, cosPhi = current.CosPhi;
            Array.Clear(rotatedX, 0, 14); Array.Clear(rotatedZ, 0, 14);
            float minIntercept = 2000f, maxIntercept = -2000f, minDepth = 2000f, maxDepth = -2000f;
            int carrierMode = -1, mask = 0, count = 0, limit = 14, carrier = input.Carrier;
            SourceCameraBall ball = input.Ball;
            if (carrier >= 0 || (ball.Receiver >= 0 && input.RawPlayResult == 5 && (input.RawGameFlags & 0x400) != 0 &&
                (carrier = ball.Receiver) >= 0))
            {
                SourceCameraActor actor = input.Actors[carrier];
                carrierMode = actor.RawMode;
                carrierGoalDistance = 390f + actor.Position.X * (actor.PlayerId / 7 * 2 - 1);
                TargetOffscreen = false;
            }
            else
            {
                carrier = -1;
                if (!((input.RawGameFlags & 0x1000) != 0 && KickoffFlag) && (input.RawGameFlags & 0x8000) == 0)
                {
                    limit++;
                    if (ball.RawType == 0 && (ball.RawFlags & 0x20) == 0)
                    {
                        limit++;
                        if (ball.Receiver < 0)
                            throw new InvalidOperationException("Original unbounced pass requires its actual int_receiver.");
                        SourceVector3 receiver = input.Actors[ball.Receiver].GroundCameraPosition;
                        SourceVector3 target = ball.TargetCameraPosition;
                        float xLimit = horizontalResolution / horizontalResolution / 2f / FocalFactor;
                        float yLimit = verticalResolution / horizontalResolution / 2f / FocalFactor;
                        if (!TargetOffscreen && ball.FlightTime < 2 && (receiver.Z < 10f || target.Z < 10f ||
                            Math.Abs(receiver.Y / receiver.Z) > yLimit && Math.Abs(target.Y / target.Z) > yLimit ||
                            Math.Abs(receiver.X / receiver.Z) > xLimit && Math.Abs(target.X / target.Z) > xLimit))
                            TargetOffscreen = true;
                        if (TargetOffscreen) limit += 2;
                    }
                }
            }
            while (--limit >= 0)
            {
                float x, z;
                if (limit == 17)
                {
                    x = ball.Position.X - ball.Target.X; z = ball.Position.Z - ball.Target.Z;
                    x *= .75f; z *= .75f; x += ball.Target.X; z += ball.Target.Z;
                    if (FieldX(ball.Target.X, input.OffenseSide) < input.LineOfScrimmage) z *= QbSideFactor;
                    carrierMode = -2;
                }
                else if (limit == 16)
                {
                    x = ball.Target.X; z = ball.Target.Z; limit = 14;
                }
                else if (limit == 15)
                {
                    x = ball.Position.X; z = ball.Position.Z;
                    if (FieldX(ball.Target.X, input.OffenseSide) < input.LineOfScrimmage) z *= QbSideFactor;
                    limit = 14;
                }
                else if (limit == 14)
                { x = ball.Position.X; z = ball.Position.Z; }
                else
                {
                    SourceCameraActor actor = input.Actors[limit];
                    x = actor.Position.X; z = actor.Position.Z;
                    mask <<= 1;
                    if (input.RawPlayType == 1)
                    {
                        if (limit != carrier)
                        {
                            if (carrier >= 0) continue;
                            if ((input.RawGameFlags & 0x1000) != 0)
                            { if (limit != 3 && limit != 10) continue; }
                            else if (limit != 0 && limit != 7) continue;
                            if (limit / 7 != input.LastPossession) continue;
                        }
                    }
                    else if (input.RawGameMode == 3)
                    {
                        if (limit != carrier)
                        {
                            if (carrier >= 0 || limit != 3 && limit != 10 || limit / 7 != input.LastPossession ||
                                (input.RawGameFlags & 0x200) == 0) continue;
                        }
                    }
                    else if (input.RawGameMode == 0 || input.RawGameMode == 1)
                    { if (limit / 7 != input.LastPossession) continue; }
                    else if (carrierMode == 5)
                    { if (limit != carrier) continue; z *= QbSideFactor; }
                    else if (carrierMode > -1)
                    { if (limit != carrier) continue; }
                    else if ((input.RawGameFlags & 0x8000) != 0)
                    { if (limit != 3 && limit != 10 || limit / 7 != input.LastPossession) continue; }
                    else continue;
                    mask++; count++;
                    rotatedX[limit] = x * cosTheta - z * sinTheta;
                    rotatedZ[limit] = x * sinTheta + z * cosTheta;
                }
                float rx = x * cosTheta - z * sinTheta, rz = x * sinTheta + z * cosTheta;
                float half = rz / 2f * cosPhi;
                float intercept = rx + half;
                if (intercept < minIntercept) minIntercept = intercept;
                intercept = rx - half;
                if (intercept > maxIntercept) maxIntercept = intercept;
                if (rz < minDepth) minDepth = rz;
                if (rz > maxDepth) maxDepth = rz;
            }
            float a, c, span, factor;
            if (input.RawPlayType != 1 && (input.RawGameMode == 0 || input.RawGameMode == 1))
            {
                a = rotatedX[input.LastPossession * 7 + 3];
                c = WorldX(input.LineOfScrimmage, input.OffenseSide) * sinTheta;
                span = maxIntercept - a;
                float other = a - minIntercept;
                if (other > span) span = other;
                factor = (0.1f + 2f) * FocalFactor;
            }
            else
            {
                a = (minIntercept + maxIntercept) / 2f;
                c = minDepth - ((maxDepth - minDepth) / 2f * sinPhi);
                span = maxIntercept - a; factor = (.5f + 2f) * FocalFactor;
            }
            float horizontal = Math.Abs(c * cosPhi / 2f + span) * factor;
            float sinDelta = (float)Math.Sin((SourceCinematicMath.Radians(180) - Alpha) + current.Phi);
            float depth = ((c - minDepth) * sinDelta / SinAlpha) * ((.3f + 1f) * FocalFactor);
            float b = 6f * UnitsPerFoot, distance = Math.Max(Math.Max(horizontal, depth), MinimumDistance(input, false));
            float side = ((input.HumanTeammates || input.Pullback ? 12f : 8f) * UnitsPerFoot) + 182.85f;
            float referenceDepth = c, distanceRate = .08f;
            int flags = Unrotate | AcceleratePosition | AccelerateAngles | MovePosition | MoveAngles | MoveEye;
            if (input.RawGameMode == 0 || input.RawGameMode == 1)
            {
                if (input.RawGameFlags != 0x1000)
                {
                    distance = MinimumDistance(input, true);
                    if (input.RawPlayType == 1) c += 8f * UnitsPerYard;
                }
                if (WasPlayOver != 0)
                {
                    if (input.RawPlayType == 1 && (input.RawGameFlags & 0x1000) != 0) KickoffFlag = true;
                    if (WasPlayOver > 1)
                    {
                        current = Copy(current, new SourceVector3(-1000f, -10f, 0f), new SourceVector3(-900f, -75f, 0f),
                            current.Distance, thetaTarget, NormalPhi, current.Velocity, current.DistanceVelocity,
                            current.ThetaVelocity, current.PhiVelocity, thetaTarget, NormalPhi);
                        flags = 0;
                    }
                    else flags = Unrotate | Snap | MoveEye;
                }
                showSideline = -1;
            }
            else
            {
                if ((input.RawGameMode == 2 || input.RawGameMode == 5) &&
                    (carrierMode == 5 || input.Carrier < 0 && ball.FlightTime != 0 &&
                    (input.RawGameFlags & 0x1000) == 0 && ball.RawType != 1))
                {
                    c += 6f * UnitsPerYard; distance = MinimumDistance(input, true);
                    if (carrierMode == 5)
                    {
                        referenceDepth = rotatedZ[input.Carrier];
                        distance += Math.Abs(input.Actors[input.Carrier].Position.Z) * .40f;
                    }
                    else if (carrierMode == -2)
                    { distance += Math.Abs(ball.Position.Z) * .40f; distanceRate = .0005f; }
                    side = 0f;
                }
                float ground = distance, offset = 0f;
                if (sinPhi != 0f)
                { offset = b / sinPhi; ground -= offset; offset *= cosPhi; }
                ground = ground * SinAlpha / sinDelta + offset;
                if (c - ground < -(6f * UnitsPerFoot + 468f)) c = ground - (6f * UnitsPerFoot + 468f);
                if (input.RawGameMode == 3 || input.RawGameMode == 4)
                {
                    if (showSideline < 0) showSideline = random(3, "cambot.c:1835/normal-sideline");
                    if (input.Carrier >= 0) referenceDepth = rotatedX[input.Carrier];
                    referenceDepth = Math.Abs(referenceDepth * 1.1f);
                    if (side < referenceDepth) side = referenceDepth;
                }
                if (side == 0f)
                    side = ((carrierMode == 5 || carrierMode == -2 ? 14f :
                        input.HumanTeammates || input.Pullback ? 12f : 8f) * UnitsPerFoot + 182.85f)
                        + cosPhi * (c - referenceDepth) * .5f;
                float halfDistance = distance / 2f;
                if (halfDistance > side) a = 0f;
                else if (a - halfDistance < -side) a = halfDistance - side;
                else if (a + halfDistance > side) a = side - halfDistance;
            }
            if (snapTarget) flags |= Snap | MoveEye;
            current = WithTargets(current, thetaTarget, NormalPhi);
            if ((flags & Unrotate) != 0 && unrotateTargets)
            {
                float ra = a;
                a = c * current.SinTheta + a * current.CosTheta;
                c = c * current.CosTheta - ra * current.SinTheta;
            }
            LastPlan = new SourceNormalCameraPlan(current, new SourceVector3(a, b, c), distance, carrierMode,
                mask, count, horizontal, depth, flags, distanceRate);
            return LastPlan;
        }

        public SourceCinematicSeed Integrate(SourceNormalCameraPlan plan)
        {
            SourceCinematicSeed s = plan.State;
            if (plan.Flags == 0) return s;
            SourceVector3 focus = s.Focus, velocity = s.Velocity;
            SourceVector3 acceleration = s.Acceleration;
            float d = s.Distance, theta = SourceCinematicMath.Wrap(s.Theta), phi = SourceCinematicMath.ClampPhi(s.Phi);
            float dv = s.DistanceVelocity, tv = s.ThetaVelocity, pv = s.PhiVelocity;
            float tt = SourceCinematicMath.Wrap(s.ThetaTarget), pt = SourceCinematicMath.ClampPhi(s.PhiTarget);
            float st = s.SinTheta, ct = s.CosTheta, sp = s.SinPhi, cp = s.CosPhi;
            float da = s.DistanceAcceleration, ta = s.ThetaAcceleration, pa = s.PhiAcceleration;
            if ((plan.Flags & Snap) != 0)
            {
                focus = plan.Target; d = plan.DistanceTarget; theta = tt; phi = pt;
                st = (float)Math.Sin(theta); ct = (float)Math.Cos(theta);
                sp = (float)Math.Sin(phi); cp = (float)Math.Cos(phi);
                velocity = default(SourceVector3); dv = tv = pv = 0f;
                acceleration = default(SourceVector3); da = ta = pa = 0f;
            }
            else
            {
                acceleration = new SourceVector3(Buffered(plan.Target.X - focus.X, .08f),
                    Buffered(plan.Target.Y - focus.Y, .08f), Buffered(plan.Target.Z - focus.Z, .08f));
                da = Buffered(plan.DistanceTarget - d, plan.DistanceAcceleration);
                velocity = SourceCinematicMath.Scale(velocity, .35f); dv *= .35f;
                float delta = tt - theta;
                if (delta > SourceCinematicMath.TwoPi / 2f) delta -= SourceCinematicMath.TwoPi;
                if (delta <= -SourceCinematicMath.TwoPi / 2f) delta += SourceCinematicMath.TwoPi;
                ta = .08f * delta; pa = .08f * (pt - phi);
                tv *= .35f; pv *= .35f;
                velocity = SourceCinematicMath.Add(velocity, acceleration);
                focus = SourceCinematicMath.Add(focus, velocity); dv += da; d += dv;
                tv += ta; pv += pa; theta += tv; phi += pv;
                if (tv != 0f) { st = (float)Math.Sin(theta); ct = (float)Math.Cos(theta); }
                if (pv != 0f) { sp = (float)Math.Sin(phi); cp = (float)Math.Cos(phi); }
            }
            SourceVector3 eye = new SourceVector3(focus.X - d * cp * st, focus.Y - d * sp, focus.Z - d * cp * ct);
            return new SourceCinematicSeed(eye, focus, d, theta, phi, velocity, dv, tv, pv, tt, pt,
                new SourceVector3(st, ct, sp), cp, acceleration, da, ta, pa);
        }

        internal static SourceCinematicSeed WithTargets(SourceCinematicSeed s, float theta, float phi)
        {
            return new SourceCinematicSeed(s.Eye, s.Focus, s.Distance, s.Theta, s.Phi, s.Velocity,
                s.DistanceVelocity, s.ThetaVelocity, s.PhiVelocity, theta, phi,
                new SourceVector3(s.SinTheta, s.CosTheta, s.SinPhi), s.CosPhi,
                s.Acceleration, s.DistanceAcceleration, s.ThetaAcceleration, s.PhiAcceleration);
        }

        internal static SourceCinematicSeed Copy(SourceCinematicSeed s, SourceVector3 eye, SourceVector3 focus,
            float distance, float theta, float phi, SourceVector3 velocity, float dv, float tv, float pv,
            float thetaTarget, float phiTarget)
        {
            bool sameAngles = theta == s.Theta && phi == s.Phi;
            return new SourceCinematicSeed(eye, focus, distance, theta, phi, velocity, dv, tv, pv, thetaTarget, phiTarget,
                sameAngles ? (SourceVector3?)new SourceVector3(s.SinTheta, s.CosTheta, s.SinPhi) : null,
                sameAngles ? (float?)s.CosPhi : null, s.Acceleration, s.DistanceAcceleration,
                s.ThetaAcceleration, s.PhiAcceleration);
        }

        private static float Buffered(float delta, float rate)
        {
            float buffer = 2f;
            if (delta < -buffer) buffer = -buffer;
            else if (delta <= buffer) return 0f;
            return rate * (delta - buffer);
        }
        private static float WorldX(int yards, int offense)
        { return ((FocalFactor * 6f) * (float)(offense != 0 ? 100 - yards : yards)) - 390f; }
        private static int FieldX(float world, int offense)
        { return (int)((390f + (offense != 0 ? -1f * world : world)) / (FocalFactor * 6f)); }
    }
}
