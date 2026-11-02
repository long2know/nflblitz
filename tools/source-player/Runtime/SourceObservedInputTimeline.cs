namespace Blitz.SourcePlayer;

public sealed class SourceObservedInputTimeline
{
    private sealed record Capture(bool PreGame, long HostTick, double[] Axes, int[] Buttons);
    private readonly Queue<Capture> pending = new();
    public int PendingCount => pending.Count;
    public long Captures { get; private set; }
    public long Delivered { get; private set; }
    public long LifecycleCleared { get; private set; }

    public void Add(bool preGame, long hostTick, double[] axes, int[] buttons)
    {
        DataCatalog.Require(pending.Count < 1024, "SOURCE_OBSERVED_INPUT_OVERFLOW: no measured physical sample was dropped.");
        pending.Enqueue(new Capture(preGame, hostTick, (double[])axes.Clone(), (int[])buttons.Clone()));
        Captures++;
    }

    public void Deliver(bool preGame, long hostTick, Action<double[], int[]> capture)
    {
        while (pending.TryPeek(out var value) && value.PreGame == preGame && value.HostTick <= hostTick)
        {
            pending.Dequeue(); capture(value.Axes, value.Buttons); Delivered++;
        }
    }

    public void Clear()
    {
        LifecycleCleared += pending.Count;
        pending.Clear();
    }

    public static long TargetTick(double captureTime, double originTime, long originTick, long currentTick)
    {
        if (!double.IsFinite(captureTime) || !double.IsFinite(originTime) || captureTime < originTime ||
            originTick < 0 || currentTick < originTick)
            throw new ArgumentOutOfRangeException(nameof(captureTime));
        long measured = checked(originTick + (long)Math.Floor((captureTime - originTime) * 60 + 1e-9) + 1);
        return Math.Max(checked(currentTick + 1), measured);
    }
}
