using System;
using Blitz.Contracts;

namespace Blitz.Gameplay
{
    // One Advance resumes after the previous sleep and runs until the next sleep
    // or return. Inputs are opaque native int samples, not station/button mappings.
    public sealed class SourceAnyButtonWait
    {
        private readonly int minTicks, waitTicks, allAbcdMask;
        private int delayRemaining, pollsRemaining, result;
        public bool Completed { get; private set; }
        public int Result
        {
            get
            {
                if (!Completed) throw new InvalidOperationException("The native wait has not returned.");
                return result;
            }
        }
        public SourceAnyButtonWait(int minTicks, int waitTicks, int allAbcdMask)
        {
            if (minTicks < 0) throw new ArgumentOutOfRangeException(nameof(minTicks));
            if (waitTicks < 0) throw new ArgumentOutOfRangeException(nameof(waitTicks));
            if (allAbcdMask == 0) throw new ArgumentOutOfRangeException(nameof(allAbcdMask));
            this.minTicks = minTicks; this.waitTicks = waitTicks; this.allAbcdMask = allAbcdMask;
            delayRemaining = minTicks; pollsRemaining = waitTicks;
        }
        public SourceAnyButtonWaitSnapshot Snapshot
        {
            get
            {
                return new SourceAnyButtonWaitSnapshot(minTicks, waitTicks, delayRemaining, pollsRemaining,
                    Completed, Completed ? (int?)result : null);
            }
        }
        public bool Advance(ISourceAnyButtonInputs inputs, Action tickSound, Action<int> sleep)
        {
            if (inputs == null) throw new ArgumentNullException(nameof(inputs));
            if (tickSound == null) throw new ArgumentNullException(nameof(tickSound));
            if (sleep == null) throw new ArgumentNullException(nameof(sleep));
            if (Completed) throw new InvalidOperationException("A returned wait cannot be resumed.");
            if (delayRemaining > 0)
            {
                delayRemaining--;
                sleep(1);
                return false;
            }
            if (pollsRemaining > 0)
            {
                pollsRemaining--;
                int sample = inputs.ReadPlayerSwitches();
                if ((sample & allAbcdMask) != 0)
                {
                    if (inputs.ReadShowPlyselIdiotBox() == 0) tickSound();
                    result = sample;
                    Completed = true;
                    return true;
                }
                result = 0;
                sleep(1);
                return false;
            }
            Completed = true;
            return true;
        }
    }
}
