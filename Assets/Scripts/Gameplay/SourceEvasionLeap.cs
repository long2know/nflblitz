using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Blitz.Contracts;

namespace Blitz.Gameplay
{
    public sealed class SourceEvasionLeapInput
    {
        public SourceVector3 TargetJointInCamera { get; }
        public SourceVector3 TargetVelocity { get; }
        public ReadOnlyCollection<float> CameraTransform { get; }
        public SourceVector3 CameraPosition { get; }
        public SourceVector3 AttackOffset { get; }
        public int Time { get; }
        public float MaximumYVelocity { get; }
        public float MaximumPlanarVelocity { get; }
        public float MinimumPlanarVelocity { get; }
        public bool TargetIsExecutingActor { get; }

        public SourceEvasionLeapInput(SourceVector3 targetJointInCamera, SourceVector3 targetVelocity,
            IReadOnlyList<float> cameraTransform, SourceVector3 cameraPosition, SourceVector3 attackOffset,
            int time, float maximumYVelocity, float maximumPlanarVelocity, float minimumPlanarVelocity,
            bool targetIsExecutingActor)
        {
            if (cameraTransform == null || cameraTransform.Count != 12)
                throw new ArgumentException("Supply the actual twelve-element source cambot.xform.", nameof(cameraTransform));
            if (time != -1 && time <= 0)
                throw new ArgumentOutOfRangeException(nameof(time), "ANI_LEAPATT requires a positive source tick count or AA_TOGROUND (-1).");
            Check(targetJointInCamera); Check(targetVelocity); Check(cameraPosition); Check(attackOffset);
            SourceMovementGuard.Finite(maximumYVelocity, nameof(maximumYVelocity));
            SourceMovementGuard.Finite(maximumPlanarVelocity, nameof(maximumPlanarVelocity));
            SourceMovementGuard.Finite(minimumPlanarVelocity, nameof(minimumPlanarVelocity));
            var copy = new float[12];
            for (int i = 0; i < copy.Length; i++)
            {
                SourceMovementGuard.Finite(cameraTransform[i], nameof(cameraTransform));
                copy[i] = cameraTransform[i];
            }
            TargetJointInCamera = targetJointInCamera; TargetVelocity = targetVelocity;
            CameraTransform = Array.AsReadOnly(copy); CameraPosition = cameraPosition; AttackOffset = attackOffset;
            Time = time; MaximumYVelocity = maximumYVelocity; MaximumPlanarVelocity = maximumPlanarVelocity;
            MinimumPlanarVelocity = minimumPlanarVelocity; TargetIsExecutingActor = targetIsExecutingActor;
        }

        private static void Check(SourceVector3 vector)
        {
            SourceMovementGuard.Finite(vector.X, nameof(vector));
            SourceMovementGuard.Finite(vector.Y, nameof(vector));
            SourceMovementGuard.Finite(vector.Z, nameof(vector));
        }
    }

    public readonly struct SourceEvasionLeapResult
    {
        public SourceMovementState State { get; }
        public bool Executed { get; }
        public int FlightTicks { get; }
        public int ScriptBytesConsumed { get { return Executed ? 36 : 0; } }
        internal SourceEvasionLeapResult(SourceMovementState state, bool executed, int flightTicks)
        {
            State = state; Executed = executed; FlightTicks = flightTicks;
        }
    }

    public static class SourceEvasionLeap
    {
        public static int ResolveVictim(int sourceVictimArgument, int ballCarrier, int throwTarget, int offenseSide)
        {
            SourceMovementGuard.Side(offenseSide);
            if (sourceVictimArgument < -2 || sourceVictimArgument > 13 ||
                ballCarrier < -1 || ballCarrier > 13 || throwTarget < -1 || throwTarget > 13)
                throw new ArgumentOutOfRangeException(nameof(sourceVictimArgument));
            int victim = sourceVictimArgument;
            if (victim == -1) victim = ballCarrier;
            if (victim == -2) victim = throwTarget;
            if (victim == -1) victim = 3 + 7 * offenseSide;
            return victim;
        }

        // ANI_LEAPATT uses cached CAMERA-space target joints, unlike the public world-joint adapter.
        // It intentionally predicts target Y twice, and its minimum-speed return bypasses the Y cap.
        public static SourceEvasionLeapResult LeapAttack(SourceMovementState state, SourceMovementTick tick,
            SourceDirectionMath directions, SourceEvasionLeapInput input)
        {
            state.Validate();
            if (directions == null || input == null) throw new ArgumentNullException(nameof(input));
            if (!tick.Execute) return new SourceEvasionLeapResult(state, false, 0);
            int time = input.Time;
            if (time == -1)
            {
                float a = -0.08f / 2f, b = state.VelocityY, c = state.Y + input.AttackOffset.Y;
                float discriminant = b * b - 4f * a * c;
                // Negative discriminants produce NaN roots; both comparisons then retain source t=1.
                float root1 = (-1f * b + (float)Math.Sqrt(discriminant)) / (2f * a);
                float root2 = (-1f * b - (float)Math.Sqrt(discriminant)) / (2f * a);
                float selected = 1f;
                if (root1 > selected) selected = root1;
                if (root2 > selected) selected = root2;
                if (selected >= 2147483648f)
                    throw new InvalidOperationException("Original ANI_LEAPATT flight time is outside signed int32.");
                time = (int)selected;
            }
            float t = time;
            SourceVector3 joint = input.TargetJointInCamera;
            SourceVector3 velocity = input.TargetIsExecutingActor
                ? new SourceVector3(state.VelocityX, state.VelocityY, state.VelocityZ) : input.TargetVelocity;
            ReadOnlyCollection<float> matrix = input.CameraTransform;
            float tx = joint.X * matrix[0] + joint.Y * matrix[4] + joint.Z * matrix[8] + input.CameraPosition.X;
            float ty = joint.X * matrix[1] + joint.Y * matrix[5] + joint.Z * matrix[9] + input.CameraPosition.Y;
            float tz = joint.X * matrix[2] + joint.Y * matrix[6] + joint.Z * matrix[10] + input.CameraPosition.Z;
            tx += t * velocity.X;
            ty += t * velocity.Y + 0.5f * -0.08f * t * t;
            tz += t * velocity.Z;
            float ay = 0f;
            for (int i = 0; i < 4; i++)
            {
                float ax = state.X + (input.AttackOffset.Z * directions.Sin(state.TargetHeading) +
                    input.AttackOffset.X * directions.Cos(state.TargetHeading));
                ay = state.Y + input.AttackOffset.Y;
                float az = state.Z + (input.AttackOffset.Z * directions.Cos(state.TargetHeading) -
                    input.AttackOffset.X * directions.Sin(state.TargetHeading));
                state.VelocityX = (tx - ax) / t;
                state.VelocityY = ((ty - ay) / t) - 0.5f * -0.08f * t;
                state.VelocityZ = (tz - az) / t;
                float planar = SourceMovement.Speed(state);
                if (planar > input.MaximumPlanarVelocity)
                {
                    state.VelocityX = state.VelocityX / planar * input.MaximumPlanarVelocity;
                    state.VelocityZ = state.VelocityZ / planar * input.MaximumPlanarVelocity;
                }
                if (planar < input.MinimumPlanarVelocity)
                {
                    SourceMovement.Velocity(ref state, directions, 0f, input.MinimumPlanarVelocity, state.TargetHeading);
                    return new SourceEvasionLeapResult(state, true, time);
                }
                int angle = directions.Heading(new SourcePlanar(state.X, state.Z),
                    new SourcePlanar(state.X + state.VelocityX, state.Z + state.VelocityZ));
                int next = MidAngle(angle, state.TargetHeading);
                if (i == 3) next = angle;
                state.TargetHeading = next;
            }
            ty += t * (input.TargetIsExecutingActor ? state.VelocityY : velocity.Y) + 0.5f * -0.08f * t * t;
            if (ty < 0f) ty = 0f;
            state.VelocityY = ((ty - ay) / t) - 0.5f * -0.08f * t;
            if (state.VelocityY > input.MaximumYVelocity) state.VelocityY = input.MaximumYVelocity;
            state.Validate();
            return new SourceEvasionLeapResult(state, true, time);
        }

        private static int MidAngle(int a, int b)
        {
            int difference = Math.Abs(a - b);
            if (difference <= 512) return (a + b) / 2;
            if (a < b) a += 1024;
            else b += 1024;
            return ((a + b) % 1024) / 2;
        }
    }
}
