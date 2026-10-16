using System;
using System.Collections.Generic;
using Blitz.Contracts;

namespace Blitz.Presentation
{
    public sealed class SourceImpactShaker
    {
        private readonly float[] exponent;
        public int Amplitude { get; private set; }
        public int TotalTicks { get; private set; }
        public int ElapsedTicks { get; private set; }
        public bool NeedsSourceRandomDraws { get { return ElapsedTicks < TotalTicks; } }

        public SourceImpactShaker(IReadOnlyList<float> originalExponent)
        {
            if (originalExponent == null || originalExponent.Count != 64)
                throw new ArgumentException("The converted original 64-entry shaker exponent table is required.", nameof(originalExponent));
            exponent = new float[64];
            for (int i = 0; i < exponent.Length; i++)
            {
                float value = originalExponent[i];
                if (float.IsNaN(value) || float.IsInfinity(value) || value <= 0f || value > 1f)
                    throw new ArgumentException("Invalid original shaker exponent.", nameof(originalExponent));
                exponent[i] = value;
            }
        }

        public void ExecuteOpcode(int amplitude, int totalTicks, int executingActorId, int carrierId)
        {
            if (amplitude < 0 || amplitude > 255 || totalTicks < 0 || totalTicks > 100000)
                throw new ArgumentOutOfRangeException(nameof(amplitude));
            if (executingActorId < 0 || executingActorId >= 14 || carrierId < -1 || carrierId >= 14)
                throw new ArgumentOutOfRangeException(nameof(executingActorId));
            // ANIM.C assigns these two globals before its carrier guard; non-carriers do not reset the count.
            Amplitude = amplitude; TotalTicks = totalTicks;
            if (executingActorId == carrierId) ElapsedTicks = 0;
        }

        public SourceVector3 Step(int actualRandrng3X, int actualRandrng3Z)
        {
            if (!NeedsSourceRandomDraws)
                throw new InvalidOperationException("An inactive source shaker must not consume source RNG.");
            if (actualRandrng3X < 0 || actualRandrng3X > 2 || actualRandrng3Z < 0 || actualRandrng3Z > 2)
                throw new ArgumentOutOfRangeException(nameof(actualRandrng3X));
            float value = exponent[64 * ElapsedTicks / TotalTicks];
            ElapsedTicks++;
            return new SourceVector3(value * (Amplitude * (actualRandrng3X - 1)), 0f,
                value * (Amplitude * (actualRandrng3Z - 1)));
        }

        public void Reset() { Amplitude = TotalTicks = ElapsedTicks = 0; }
    }

    public sealed class SourceImpactDust
    {
        private int sourceTexture;
        private int wait;
        private readonly int sleepTime;
        private readonly SourceVector3 velocity;
        public int TextureIndex { get; private set; }
        public SourceVector3 Position { get; private set; }
        public bool IsAlive { get; private set; }

        public SourceImpactDust(SourceVector3 actualWorldJoint, int sourceFacing, int actualRandrng3, int sleepTime = 1)
        {
            SourceCinematicMath.RequireFinite(actualWorldJoint);
            if (sourceFacing < 0 || sourceFacing > 1023 || actualRandrng3 < 0 || actualRandrng3 > 2 ||
                sleepTime < 0 || sleepTime > 1000)
                throw new ArgumentOutOfRangeException(nameof(sourceFacing));
            Position = new SourceVector3(actualWorldJoint.X, 0f, actualWorldJoint.Z);
            float theta = sourceFacing * (SourceCinematicMath.TwoPi / 1024f);
            velocity = new SourceVector3(.35f * (float)Math.Sin(theta), .10f, .35f * (float)Math.Cos(theta));
            sourceTexture = actualRandrng3; this.sleepTime = sleepTime;
            TextureIndex = 0; IsAlive = true;
        }

        public bool Step(bool sourcePreSnap)
        {
            if (!IsAlive) return false;
            if (sourceTexture >= 10) { IsAlive = false; return false; }
            if (wait > sleepTime) { wait = 0; TextureIndex = sourceTexture++; }
            Position = SourceCinematicMath.Add(Position, velocity);
            if (sourcePreSnap) { IsAlive = false; return false; }
            wait++;
            return true;
        }

        public static int[] SourceJoints(int argument)
        {
            if (argument == 4) return new[] { 10 };
            if (argument == 3) return new[] { 13 };
            if (argument == 1) return new[] { 14, 11 };
            if (argument == 2) return new[] { 13, 10 };
            return new[] { 8, 0, 13, 10 };
        }
    }
}
