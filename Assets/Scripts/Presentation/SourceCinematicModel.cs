using System;
using System.Collections.ObjectModel;
using Blitz.AssetImport;
using Blitz.Contracts;

namespace Blitz.Presentation
{
    public sealed class SourceCinematicTransform
    {
        public SourceVector3 Position { get; }
        public ReadOnlyCollection<float> Transform { get; }
        private SourceCinematicTransform(SourceVector3 position, float[] matrix)
        { Position = position; Transform = Array.AsReadOnly(matrix); }

        public static SourceCinematicTransform FromState(SourceCinematicSeed state)
        {
            SourceVector3 forward = SourceCinematicMath.Subtract(state.Focus, state.Eye);
            SourceVector3 above = SourceCinematicMath.Subtract(
                new SourceVector3(state.Focus.X, state.Focus.Y + 10f, state.Focus.Z), state.Eye);
            SourceVector3 right = Normalize(Cross(above, forward));
            forward = Normalize(forward);
            SourceVector3 up = Cross(forward, right);
            return new SourceCinematicTransform(state.Eye, new[] {
                right.X, right.Y, right.Z, 0f, up.X, up.Y, up.Z, 0f,
                forward.X, forward.Y, forward.Z, 0f
            });
        }

        private static SourceVector3 Cross(SourceVector3 a, SourceVector3 b)
        {
            return new SourceVector3(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
        }
        private static SourceVector3 Normalize(SourceVector3 value)
        {
            float squared = value.X * value.X;
            squared += value.Y * value.Y; squared += value.Z * value.Z;
            float length = (float)Math.Sqrt(squared);
            if (!(length > 0f) || float.IsInfinity(length))
                throw new InvalidOperationException("Original cambot transform cannot normalize a degenerate view/up vector.");
            return new SourceVector3(value.X / length, value.Y / length, value.Z / length);
        }
    }

    public readonly struct SourceCinematicTarget
    {
        public SourceVector3 Position { get; }
        public int Facing { get; }
        public SourceCinematicTarget(SourceVector3 position, int facing)
        {
            SourceCinematicMath.RequireFinite(position);
            if (facing < 0 || facing > 1023) throw new ArgumentOutOfRangeException(nameof(facing));
            Position = position; Facing = facing;
        }

        public static SourceCinematicTarget FromActor(PlayerView actor)
        {
            if (!actor.Source.IsSpecified)
                throw new InvalidOperationException("Original camera targets require an actual source actor snapshot.");
            float x = actor.FacingDirection.X, z = actor.FacingDirection.Z;
            SourceCinematicMath.RequireFinite(x); SourceCinematicMath.RequireFinite(z);
            double angle = Math.Atan2(x, z);
            int heading = (int)Math.Round(angle * 512d / Math.PI, MidpointRounding.AwayFromZero) & 1023;
            double canonical = heading * Math.PI / 512d;
            if (Math.Abs(Math.Sin(canonical) - x) > .000002d || Math.Abs(Math.Cos(canonical) - z) > .000002d)
                throw new InvalidOperationException("Original camera facing must be the exact published 1024-heading grid, not render-interpolated facing or actor velocity.");
            return new SourceCinematicTarget(new SourceVector3((actor.Position.X - 50f) * SourceTiming.SourceUnitsPerYard,
                actor.Height * SourceTiming.SourceUnitsPerYard, actor.Position.Z * SourceTiming.SourceUnitsPerYard), heading);
        }
    }

    public readonly struct SourceCinematicSeed
    {
        public SourceVector3 Eye { get; }
        public SourceVector3 Focus { get; }
        public float Distance { get; }
        public float Theta { get; }
        public float Phi { get; }
        public SourceVector3 Velocity { get; }
        public float DistanceVelocity { get; }
        public float ThetaVelocity { get; }
        public float PhiVelocity { get; }

        public SourceCinematicSeed(SourceVector3 eye, SourceVector3 focus, float distance, float theta, float phi,
            SourceVector3 velocity = default(SourceVector3), float distanceVelocity = 0f,
            float thetaVelocity = 0f, float phiVelocity = 0f)
        {
            SourceCinematicMath.RequireFinite(eye); SourceCinematicMath.RequireFinite(focus);
            SourceCinematicMath.RequireFinite(velocity);
            SourceCinematicMath.RequireFinite(distance); SourceCinematicMath.RequireFinite(theta);
            SourceCinematicMath.RequireFinite(phi); SourceCinematicMath.RequireFinite(distanceVelocity);
            SourceCinematicMath.RequireFinite(thetaVelocity); SourceCinematicMath.RequireFinite(phiVelocity);
            if (distance <= 0f) throw new ArgumentOutOfRangeException(nameof(distance));
            Eye = eye; Focus = focus; Distance = distance; Theta = theta; Phi = phi;
            Velocity = velocity; DistanceVelocity = distanceVelocity; ThetaVelocity = thetaVelocity; PhiVelocity = phiVelocity;
        }
    }

    public readonly struct SourceCinematicFrame
    {
        public SourceCinematicSeed State { get; }
        public bool Active { get; }
        public bool ReturnedToNormal { get; }
        public bool SnapOnReturn { get; }
        public int ElapsedTicks { get; }
        public string Table { get; }
        public int Ordinal { get; }
        internal SourceCinematicFrame(SourceCinematicSeed state, bool active, bool returned, bool snap,
            int ticks, SourceCinematicEntry entry)
        {
            State = state; Active = active; ReturnedToNormal = returned; SnapOnReturn = snap;
            ElapsedTicks = ticks; Table = entry == null ? null : entry.Table; Ordinal = entry == null ? -1 : entry.Ordinal;
        }
    }

    public sealed class SourceCinematicModel
    {
        private const int SnapTarget = 1, MovePosition = 2, MoveAngles = 4, MoveEye = 8, NewTheta = 16, TrackPosition = 32;
        private SourceCinematicEntry entry;
        private SourceVector3 eye, focus, velocity, acceleration, desired, previousDesired;
        private float distance, distanceVelocity, distanceAcceleration, theta, phi, thetaTarget, phiTarget;
        private float thetaVelocity, phiVelocity, thetaAcceleration, phiAcceleration, selectedTheta, targetAdjustment;
        private float splineTargetY, trackingBuffer = 2f, trackingAcceleration = .08f;
        private SourceVector3[] spline;
        private int tick;
        public bool IsActive { get; private set; }
        public SourceCinematicSeed State
        {
            get { return new SourceCinematicSeed(eye, focus, distance, theta, phi, velocity, distanceVelocity, thetaVelocity, phiVelocity); }
        }

        public SourceCinematicModel(SourceCinematicSeed seed)
        {
            Reset(seed);
        }

        public void Reset(SourceCinematicSeed seed)
        {
            eye = seed.Eye; focus = seed.Focus; distance = seed.Distance;
            theta = thetaTarget = seed.Theta; phi = phiTarget = seed.Phi;
            velocity = seed.Velocity; distanceVelocity = seed.DistanceVelocity;
            thetaVelocity = seed.ThetaVelocity; phiVelocity = seed.PhiVelocity;
            acceleration = default(SourceVector3); distanceAcceleration = thetaAcceleration = phiAcceleration = 0f;
            desired = previousDesired = default(SourceVector3); spline = null; entry = null; tick = 0; IsActive = false;
        }

        public bool Select(SourceCinematicEntry selected, float modeTargetYAdjustment = 0f)
        {
            if (selected == null) throw new ArgumentNullException(nameof(selected));
            SourceCinematicMath.RequireFinite(modeTargetYAdjustment);
            if (selected.IsNoSelection) return false;
            entry = selected;
            selectedTheta = SourceCinematicMath.Radians(selected.ThetaDegrees);
            targetAdjustment = modeTargetYAdjustment;
            tick = 0; spline = null; IsActive = true;
            return true;
        }

        // Exactly one executing source camera tick. The caller supplies every original target sample.
        public SourceCinematicFrame Step(SourceCinematicTarget target, bool newPlayAfterPlayOver = false)
        {
            if (!IsActive) return new SourceCinematicFrame(State, false, false, false, tick, entry);
            if (newPlayAfterPlayOver || tick >= entry.ModeTicks)
            {
                IsActive = false;
                return new SourceCinematicFrame(State, false, true, entry.TravelTicks < 2, tick, entry);
            }
            int flags;
            int mode = entry.RawMode;
            if (mode <= 6) flags = Zoom(target);
            else if (mode <= 9) flags = FloatSpline(target);
            else flags = Rail(target);
            theta = SourceCinematicMath.Wrap(theta); thetaTarget = SourceCinematicMath.Wrap(thetaTarget);
            phi = SourceCinematicMath.ClampPhi(phi); phiTarget = SourceCinematicMath.ClampPhi(phiTarget);
            if ((flags & SnapTarget) != 0)
            {
                focus = desired; distance = entry.Distance;
                theta = thetaTarget; phi = phiTarget;
                velocity = acceleration = default(SourceVector3);
                distanceVelocity = distanceAcceleration = thetaVelocity = phiVelocity = thetaAcceleration = phiAcceleration = 0f;
            }
            else
            {
                if ((flags & TrackPosition) != 0)
                {
                    acceleration = new SourceVector3(Buffered(desired.X - focus.X), Buffered(desired.Y - focus.Y), Buffered(desired.Z - focus.Z));
                    distanceAcceleration = 0f;
                    velocity = SourceCinematicMath.Scale(velocity, .35f); distanceVelocity *= .35f;
                }
                if ((flags & MovePosition) != 0)
                {
                    velocity = SourceCinematicMath.Add(velocity, acceleration); focus = SourceCinematicMath.Add(focus, velocity);
                    distanceVelocity += distanceAcceleration; distance += distanceVelocity;
                }
                if ((flags & MoveAngles) != 0)
                {
                    thetaVelocity += thetaAcceleration; phiVelocity += phiAcceleration;
                    theta += thetaVelocity; phi += phiVelocity;
                }
            }
            if ((flags & MoveEye) != 0) PlaceEye();
            if ((flags & NewTheta) != 0)
            {
                float x = focus.X - eye.X, z = focus.Z - eye.Z;
                float squared = x * x + z * z;
                if (!(squared > 0f))
                    throw new InvalidOperationException("Original special camera has a degenerate horizontal view direction.");
                float inverse = 1f / (float)Math.Sqrt(squared);
                x *= inverse; z *= inverse;
                z = Math.Max(-1f, Math.Min(1f, z));
                float angle = (float)Math.Acos(z);
                if (x < 0f) angle = SourceCinematicMath.TwoPi - angle;
                theta = SourceCinematicMath.Wrap(angle);
            }
            tick++;
            return new SourceCinematicFrame(State, true, false, false, tick, entry);
        }

        public void ApplyOriginalShakeOffset(SourceVector3 offset)
        {
            SourceCinematicMath.RequireFinite(offset);
            eye = SourceCinematicMath.Add(eye, offset); focus = SourceCinematicMath.Add(focus, offset);
        }

        public SourceCinematicFrame ResumeNormal(SourceCinematicSeed actualNormalTarget, bool snap)
        {
            if (IsActive) throw new InvalidOperationException("An active original transient cannot also execute normal camera.");
            desired = actualNormalTarget.Focus;
            thetaTarget = SourceCinematicMath.Wrap(actualNormalTarget.Theta);
            phiTarget = SourceCinematicMath.ClampPhi(actualNormalTarget.Phi);
            if (snap)
            {
                focus = desired; distance = actualNormalTarget.Distance; theta = thetaTarget; phi = phiTarget;
                velocity = acceleration = default(SourceVector3);
                distanceVelocity = distanceAcceleration = thetaVelocity = phiVelocity = thetaAcceleration = phiAcceleration = 0f;
            }
            else
            {
                trackingBuffer = 2f; trackingAcceleration = .08f;
                acceleration = new SourceVector3(Buffered(desired.X - focus.X), Buffered(desired.Y - focus.Y),
                    Buffered(desired.Z - focus.Z));
                distanceAcceleration = Buffered(actualNormalTarget.Distance - distance);
                velocity = SourceCinematicMath.Scale(velocity, .35f);
                distanceVelocity *= .35f;
                float delta = thetaTarget - theta;
                if (delta > SourceCinematicMath.TwoPi / 2f) delta -= SourceCinematicMath.TwoPi;
                if (delta <= -SourceCinematicMath.TwoPi / 2f) delta += SourceCinematicMath.TwoPi;
                thetaAcceleration = .08f * delta; phiAcceleration = .08f * (phiTarget - phi);
                thetaVelocity *= .35f; phiVelocity *= .35f;
                velocity = SourceCinematicMath.Add(velocity, acceleration);
                focus = SourceCinematicMath.Add(focus, velocity);
                distanceVelocity += distanceAcceleration; distance += distanceVelocity;
                thetaVelocity += thetaAcceleration; phiVelocity += phiAcceleration;
                theta += thetaVelocity; phi += phiVelocity;
            }
            PlaceEye();
            return new SourceCinematicFrame(State, false, false, snap, tick, entry);
        }

        private int Zoom(SourceCinematicTarget target)
        {
            SourceVector3 now = target.Position;
            desired = new SourceVector3(now.X, now.Y + 5f * (390f / 300f * 2f) + targetAdjustment, now.Z);
            SourceVector3 delta = SourceCinematicMath.Subtract(desired, previousDesired);
            previousDesired = desired;
            int flags = SnapTarget | MoveEye;
            if (tick == 0)
            {
                SetSelectedTheta(target, entry.Mode == SourceCinematicMode.ZoomNoFacing,
                    entry.Mode == SourceCinematicMode.Zoom);
                thetaTarget = selectedTheta; phiTarget = SourceCinematicMath.Radians(entry.PhiDegrees);
                if (entry.TravelTicks < 2) { theta = thetaTarget; phi = phiTarget; }
                else { AccelerateDecelerate(0, entry.TravelTicks); flags = MoveEye; }
            }
            else if (entry.TravelTicks < 2) focus = SourceCinematicMath.Add(focus, delta);
            else
            {
                focus = SourceCinematicMath.Add(focus, delta);
                AccelerateDecelerate(tick, entry.TravelTicks);
                flags = MovePosition | MoveAngles | MoveEye;
            }
            return flags;
        }

        private int FloatSpline(SourceCinematicTarget target)
        {
            if (entry.TravelTicks == 0)
                throw new InvalidOperationException("Original float spline requires the actual source-resolved travel count.");
            if (tick == 0)
            {
                SetSelectedTheta(target, entry.Mode != SourceCinematicMode.Spline,
                    entry.Mode == SourceCinematicMode.Spline);
                SourceVector3 end = entry.Mode == SourceCinematicMode.SplineCameraTarget ? focus : target.Position;
                splineTargetY = end.Y;
                if (entry.Mode == SourceCinematicMode.SplineCameraTarget)
                {
                    focus = target.Position; distance = entry.Distance;
                }
                float phi = SourceCinematicMath.Radians(entry.PhiDegrees);
                float horizontal = (float)Math.Cos(phi) * entry.Distance;
                end = new SourceVector3(end.X - (float)Math.Sin(selectedTheta) * horizontal,
                    end.Y - (float)Math.Sin(phi) * entry.Distance, end.Z - (float)Math.Cos(selectedTheta) * horizontal);
                SourceVector3 delta = SourceCinematicMath.Subtract(end, eye);
                float horizontalLength = (float)Math.Sqrt(delta.X * delta.X + delta.Z * delta.Z);
                float length = (float)Math.Sqrt(delta.X * delta.X + delta.Z * delta.Z + delta.Y * delta.Y);
                if (!(length > 0f) || !(horizontalLength > 0f))
                    throw new InvalidOperationException("Original float spline cannot normalize coincident/vertical endpoints.");
                float sineX = delta.Y / length, cosineX = horizontalLength / length;
                float sineY = delta.X / horizontalLength, cosineY = delta.Z / horizontalLength;
                spline = new SourceVector3[4]; spline[0] = eye; spline[3] = end;
                for (int i = 0; i < 2; i++)
                {
                    SourceVector3 p = entry.ControlPoints[i];
                    float y = p.Z * sineX + p.Y * cosineX, z = p.Z * cosineX - p.Y * sineX;
                    spline[i + 1] = new SourceVector3((z * sineY + p.X * cosineY) * length + eye.X,
                        y * length + eye.Y, (z * cosineY - p.X * sineY) * length + eye.Z);
                }
                acceleration = default(SourceVector3); distanceAcceleration = 0f;
            }
            if (entry.Mode != SourceCinematicMode.SplineCameraTarget)
            {
                trackingBuffer = 2f; trackingAcceleration = .08f;
                SourceVector3 movement = new SourceVector3(velocity.X, 0f, velocity.Z);
                spline[3] = SourceCinematicMath.Add(spline[3], movement);
                spline[2] = SourceCinematicMath.Add(spline[2], SourceCinematicMath.Scale(movement, 2f / 3f));
                spline[1] = SourceCinematicMath.Add(spline[1], SourceCinematicMath.Scale(movement, 1f / 3f));
                focus = new SourceVector3(focus.X, target.Position.Y + 4f * (390f / 300f * 2f) + targetAdjustment, focus.Z);
            }
            else
            {
                trackingBuffer = .25f; trackingAcceleration = .25f;
                focus = new SourceVector3(focus.X, Math.Max(target.Position.Y * .4f, splineTargetY + targetAdjustment), focus.Z);
            }
            desired = new SourceVector3(target.Position.X, focus.Y, target.Position.Z);
            if (tick <= entry.TravelTicks) eye = SourceCinematicMath.Bezier(spline, tick, entry.TravelTicks);
            return TrackPosition | MovePosition | NewTheta;
        }

        private int Rail(SourceCinematicTarget target)
        {
            if (entry.TravelTicks == 0)
                throw new InvalidOperationException("Original rail camera requires its actual travel count.");
            if (tick == 0)
            {
                SetSelectedTheta(target, entry.Mode != SourceCinematicMode.Rail, false);
                float sine = (float)Math.Sin(selectedTheta), cosine = (float)Math.Cos(selectedTheta);
                spline = new SourceVector3[4];
                for (int i = 0; i < spline.Length; i++)
                {
                    SourceVector3 p = entry.ControlPoints[i];
                    SourceVector3 rotated = new SourceVector3(entry.Distance * (p.Z * sine + p.X * cosine),
                        entry.Distance * p.Y, entry.Distance * (p.Z * cosine - p.X * sine));
                    spline[i] = entry.Mode == SourceCinematicMode.RailNoTracking ? rotated :
                        SourceCinematicMath.Add(rotated, target.Position);
                }
            }
            focus = new SourceVector3(target.Position.X, target.Position.Y + 5f * (390f / 300f * 2f), target.Position.Z);
            if (tick <= entry.TravelTicks) eye = SourceCinematicMath.Bezier(spline, tick, entry.TravelTicks);
            return NewTheta;
        }

        private void SetSelectedTheta(SourceCinematicTarget target, bool noFacing, bool limitSwing)
        {
            selectedTheta += noFacing ? thetaTarget : target.Facing * (SourceCinematicMath.TwoPi / 1024f);
            selectedTheta = SourceCinematicMath.Wrap(selectedTheta);
            if (!limitSwing) return;
            float delta = selectedTheta - thetaTarget, half = SourceCinematicMath.TwoPi / 2f;
            if (delta <= -half) delta += half + half;
            if (delta >= half) delta -= half + half;
            float quarter = SourceCinematicMath.TwoPi / 4f;
            if (delta > quarter) selectedTheta -= 2f * (delta - quarter);
            if (delta < -quarter) selectedTheta -= 2f * (delta + quarter);
        }

        private void AccelerateDecelerate(int currentTick, int totalTicks)
        {
            if (currentTick > totalTicks)
            {
                acceleration = default(SourceVector3); distanceAcceleration = thetaAcceleration = phiAcceleration = 0f;
                return;
            }
            if (totalTicks < 2) return;
            float scale;
            int ticks;
            if (currentTick == 0)
            {
                scale = 4f; ticks = totalTicks;
                velocity = default(SourceVector3); distanceVelocity = thetaVelocity = phiVelocity = 0f;
            }
            else
            {
                ticks = totalTicks / 2 + 1;
                if (currentTick != ticks) return;
                if ((totalTicks & 1) != 0) scale = -2f;
                else
                {
                    acceleration = SourceCinematicMath.Scale(acceleration, -1f);
                    distanceAcceleration = -distanceAcceleration; thetaAcceleration = -thetaAcceleration; phiAcceleration = -phiAcceleration;
                    return;
                }
            }
            scale /= ticks * ticks;
            acceleration = SourceCinematicMath.Scale(SourceCinematicMath.Subtract(desired, focus), scale);
            distanceAcceleration = scale * (entry.Distance - distance);
            float delta = thetaTarget - theta;
            if (delta > SourceCinematicMath.TwoPi / 2f) delta -= SourceCinematicMath.TwoPi;
            if (delta <= -SourceCinematicMath.TwoPi / 2f) delta += SourceCinematicMath.TwoPi;
            thetaAcceleration = scale * delta; phiAcceleration = scale * (phiTarget - phi);
        }

        private void PlaceEye()
        {
            float cosine = (float)Math.Cos(phi);
            eye = new SourceVector3(focus.X - distance * cosine * (float)Math.Sin(theta),
                focus.Y - distance * (float)Math.Sin(phi), focus.Z - distance * cosine * (float)Math.Cos(theta));
        }

        private float Buffered(float delta)
        {
            float buffer = trackingBuffer;
            if (delta < -buffer) buffer = -buffer;
            else if (delta <= buffer) return 0f;
            return trackingAcceleration * (delta - buffer);
        }
    }

    internal static class SourceCinematicMath
    {
        internal const float TwoPi = (float)(Math.PI * 2d);
        internal static float Radians(float degrees) { return (float)(degrees * Math.PI / 180d); }
        internal static float Wrap(float angle)
        {
            while (angle >= TwoPi) angle -= TwoPi;
            while (angle < 0f) angle += TwoPi;
            return angle;
        }
        internal static float ClampPhi(float phi) { return Math.Max(Radians(-89), Math.Min(Radians(89), phi)); }
        internal static void RequireFinite(float value)
        { if (float.IsNaN(value) || float.IsInfinity(value)) throw new ArgumentOutOfRangeException(nameof(value)); }
        internal static void RequireFinite(SourceVector3 value)
        { RequireFinite(value.X); RequireFinite(value.Y); RequireFinite(value.Z); }
        internal static SourceVector3 Add(SourceVector3 a, SourceVector3 b) { return new SourceVector3(a.X + b.X, a.Y + b.Y, a.Z + b.Z); }
        internal static SourceVector3 Subtract(SourceVector3 a, SourceVector3 b) { return new SourceVector3(a.X - b.X, a.Y - b.Y, a.Z - b.Z); }
        internal static SourceVector3 Scale(SourceVector3 a, float scale) { return new SourceVector3(a.X * scale, a.Y * scale, a.Z * scale); }
        internal static SourceVector3 Bezier(SourceVector3[] points, int currentTick, int totalTicks)
        {
            float k2 = currentTick / (float)totalTicks, k3 = 1f - k2, k4 = k2 * k2, u1 = k3;
            float k1 = k3 * k3;
            k3 *= k4 * 3f; k4 *= k2; k2 *= k1 * 3f; k1 *= u1;
            return new SourceVector3(points[0].X * k1 + points[1].X * k2 + points[2].X * k3 + points[3].X * k4,
                points[0].Y * k1 + points[1].Y * k2 + points[2].Y * k3 + points[3].Y * k4,
                points[0].Z * k1 + points[1].Z * k2 + points[2].Z * k3 + points[3].Z * k4);
        }
    }
}
