using Blitz.AssetImport;
using Blitz.Contracts;
using Blitz.Gameplay;
using Blitz.Presentation;

namespace Blitz.SourcePlayer;

public sealed class OriginalHudProcess
{
    private readonly SourceInterfaceData data;
    private long pendingTick = -1;
    public IGameState CommittedState { get; private set; }
    public SourceHudState CommittedHud { get; private set; }
    public bool ResultPending { get; private set; }
    public long CommittedSourceTick => ((ISourceGameState)CommittedState).SourceTick;
    public long PendingResultWakes { get; private set; }

    public OriginalHudProcess(SourceInterfaceData data, GameSimulation game)
    {
        this.data = data;
        Observe(game);
    }

    public void Observe(GameSimulation game)
    {
        var match = ((ISourceMatchState)game.State).Match;
        ResultPending = game.C2State.ResultPending;
        if (ResultPending)
        {
            // Actual C2 QueueSourceC2PlayEnd publishes DeadBall before game_proc commits
            // its result on the NEXT source wake. Hold the last committed authored HUD
            // for exactly that typed process interval; never fabricate a classified result.
            DataCatalog.Require(game.C2State.Enabled && CommittedState != null &&
                game.State.Phase == GamePhase.DeadBall && match.Phase == SourceMatchPhase.PlayResult,
                "A pending original HUD result requires the actual enabled C2 play-over process.");
            if (pendingTick < 0) { pendingTick = game.SourceTick; PendingResultWakes++; }
            DataCatalog.Require(game.SourceTick == pendingTick,
                "Original C2 result did not commit on the next source wake; refusing an indefinite HUD hold.");
            return;
        }
        pendingTick = -1;
        CommittedHud = new SourceHudState(game.State, data);
        CommittedState = game.State;
    }
}
