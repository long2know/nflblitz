using Blitz.Runtime;

namespace Blitz.SourcePlayer;

public sealed class SourceRealtimeFrameClock
{
    private double waiting, buffered;
    public double PendingSeconds => waiting + buffered;
    public double ObservedSeconds { get; private set; }
    public long HostSteps { get; private set; }

    public int Advance(double elapsed, Func<double, int> advance)
    {
        ArgumentNullException.ThrowIfNull(advance);
        if (!double.IsFinite(elapsed) || elapsed < 0)
            throw new ArgumentOutOfRangeException(nameof(elapsed));
        waiting += elapsed;
        ObservedSeconds += elapsed;
        double delivered = Math.Min(waiting, Math.Max(0, FixedStepBridge.MaximumAccumulatedSeconds - buffered));
        int steps = advance(delivered);
        DataCatalog.Require(steps is >= 0 and <= FixedStepBridge.MaximumStepsPerFrame,
            "SOURCE_REALTIME_STEPS: one actual callback must retain the existing bounded bridge budget.");
        waiting = Math.Max(0, waiting - delivered);
        buffered = Math.Max(0, buffered + delivered - steps * FixedStepBridge.StepSeconds);
        HostSteps = checked(HostSteps + steps);
        return steps;
    }

    public void Clear()
    {
        waiting = buffered = ObservedSeconds = 0;
        HostSteps = 0;
    }

    public void PreservePendingAcrossInputClear()
    {
        waiting += buffered;
        buffered = 0;
    }
}
