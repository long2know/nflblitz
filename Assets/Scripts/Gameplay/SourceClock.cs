using System;

namespace Blitz.Gameplay
{
    public readonly struct SourceClockState
    {
        public int PackedTime { get; }
        public int LocalPackedTime { get; }
        public int PlayTime { get; }
        public long NextWakeTick { get; }
        public bool ProcessComplete { get; }

        public SourceClockState(int packedTime, int localPackedTime, int playTime,
            long nextWakeTick, bool processComplete = false)
        {
            PackedTime = packedTime;
            LocalPackedTime = localPackedTime;
            PlayTime = playTime;
            NextWakeTick = nextWakeTick;
            ProcessComplete = processComplete;
        }
    }

    public readonly struct SourceClockInput
    {
        public long SourceTick { get; }
        public bool ClockActive { get; }
        public int QuarterIndex { get; }
        public int TimeAdjustment { get; }
        public bool AdjustmentReadSucceeded { get; }
        public bool IsAttractMode { get; }

        public SourceClockInput(long sourceTick, bool clockActive, int quarterIndex = 0,
            int timeAdjustment = 2, bool adjustmentReadSucceeded = true, bool isAttractMode = false)
        {
            SourceTick = sourceTick;
            ClockActive = clockActive;
            QuarterIndex = quarterIndex;
            TimeAdjustment = timeAdjustment;
            AdjustmentReadSucceeded = adjustmentReadSucceeded;
            IsAttractMode = isAttractMode;
        }
    }

    public enum SourceClockOperation
    {
        NotScheduled,
        Paused,
        Subtick,
        Second,
        Minute,
        ProcessExit
    }

    public readonly struct SourceClockStep
    {
        public SourceClockState State { get; }
        public SourceClockOperation Operation { get; }
        public int SleepTicks { get; }
        public int TimeOfPossessionDelta { get; }
        public bool LowClockWarning { get; }
        public bool ReadTimeAdjustment { get; }
        public bool UsedBadAdjustmentFallback { get; }
        public bool PeriodExpired { get; }

        internal SourceClockStep(SourceClockState state, SourceClockOperation operation,
            int sleepTicks = 0, int timeOfPossessionDelta = 0, bool lowClockWarning = false,
            bool readTimeAdjustment = false, bool usedBadAdjustmentFallback = false,
            bool periodExpired = false)
        {
            State = state;
            Operation = operation;
            SleepTicks = sleepTicks;
            TimeOfPossessionDelta = timeOfPossessionDelta;
            LowClockWarning = lowClockWarning;
            ReadTimeAdjustment = readTimeAdjustment;
            UsedBadAdjustmentFallback = usedBadAdjustmentFallback;
            PeriodExpired = periodExpired;
        }
    }

    // nfl.c 2868-2993, blob 15b49474fb55e8bac280b6515fcf7bf59cdf8664.
    // A call is a source process wake, not a host/render delta. The caller orders
    // clock wakes relative to game_proc/player_proc and attributes possession time.
    public static class SourceClock
    {
        public const int SourceTicksPerSecond = 57;
        public const int QuarterTime = 0x00020000;
        public const int DefaultTimeAdjustment = 2;
        public const int DefaultAutoHikeTicks = 5 * SourceTicksPerSecond;
        public const int InitialKickoffTicks = 90;
        public const int LaterKickoffTicks = 3;

        private static readonly int[] Reloads = { 30, 25, 20, 18, 16 };

        public static SourceClockState Create(int packedTime = QuarterTime, int playTime = 0,
            long firstWakeTick = 0)
        {
            if (packedTime < 0 || packedTime > 0x00ffffff)
                throw new ArgumentOutOfRangeException(nameof(packedTime));
            if (firstWakeTick < 0)
                throw new ArgumentOutOfRangeException(nameof(firstWakeTick));
            return new SourceClockState(packedTime, packedTime, playTime, firstWakeTick);
        }

        public static SourceClockState DiscardFraction(SourceClockState state)
        {
            // game_proc masks the shared field, not game_clock_proc's local gt.
            return new SourceClockState(state.PackedTime & 0x00ffff00, state.LocalPackedTime,
                state.PlayTime, state.NextWakeTick, state.ProcessComplete);
        }

        public static SourceClockState ResetPlayTime(SourceClockState state)
        {
            return new SourceClockState(state.PackedTime, state.LocalPackedTime, 0,
                state.NextWakeTick, state.ProcessComplete);
        }

        public static int WholeSeconds(int packedTime)
        {
            return ((packedTime >> 16) & 255) * 60 + ((packedTime >> 8) & 255);
        }

        public static SourceClockStep Step(SourceClockState state, SourceClockInput input)
        {
            if (input.SourceTick < 0)
                throw new ArgumentOutOfRangeException(nameof(input));
            if (state.ProcessComplete || input.SourceTick < state.NextWakeTick)
                return new SourceClockStep(state, SourceClockOperation.NotScheduled);

            int local = state.LocalPackedTime;
            if (local == 0)
            {
                var exited = new SourceClockState(state.PackedTime, local, state.PlayTime,
                    input.SourceTick, true);
                return new SourceClockStep(exited, SourceClockOperation.ProcessExit);
            }

            long nextWake = checked(input.SourceTick + 1);
            if (!input.ClockActive || input.IsAttractMode)
            {
                var paused = new SourceClockState(state.PackedTime, local, state.PlayTime, nextWake);
                return new SourceClockStep(paused, SourceClockOperation.Paused, 1);
            }

            int playTime = state.PlayTime;
            int possessionDelta = 0;
            bool warning = false;
            bool readAdjustment = false;
            bool badAdjustment = false;
            SourceClockOperation operation;

            if ((local & 255) != 0)
            {
                local--;
                operation = SourceClockOperation.Subtick;
            }
            else if ((local & 0x0000ff00) != 0)
            {
                local -= 0x100;
                playTime++;
                possessionDelta = 1;
                operation = SourceClockOperation.Second;

                // This test reads the published time BEFORE this iteration publishes gt.
                if ((state.PackedTime & 0x00ffff00) <= 0x00001f00
                    && (input.QuarterIndex == 1 || input.QuarterIndex == 3))
                {
                    local |= 0x22;
                    warning = (state.PackedTime & 0x100) != 0;
                }
                else if (playTime >= 18)
                {
                    local |= 0x30;
                }
                else
                {
                    readAdjustment = true;
                    int adjustment = input.AdjustmentReadSucceeded ? input.TimeAdjustment : -1;
                    if (adjustment < 0 || adjustment >= Reloads.Length)
                    {
                        adjustment = 3;
                        badAdjustment = true;
                    }
                    local |= Reloads[adjustment];
                }
            }
            else if ((local & 0x00ff0000) != 0)
            {
                playTime++;
                possessionDelta = 1;
                local = (local - 0x10000) | 0x3b18;
                operation = SourceClockOperation.Minute;
            }
            else
            {
                // The original gun/sleep/die branch cannot be reached for a valid
                // 24-bit clock: while(gt != 0) already excludes zero at the top.
                throw new InvalidOperationException("Clock state is outside the source packed-time domain.");
            }

            var updated = new SourceClockState(local, local, playTime, nextWake);
            return new SourceClockStep(updated, operation, 1, possessionDelta, warning,
                readAdjustment, badAdjustment, local == 0);
        }
    }
}
