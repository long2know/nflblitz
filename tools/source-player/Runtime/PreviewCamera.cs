using Blitz.AssetImport;
using Blitz.Contracts;
using Blitz.Gameplay;
using Blitz.Presentation;

namespace Blitz.SourcePlayer;

public sealed class PreviewCamera
{
    private readonly GameSimulation game;
    private readonly PlayerData data;
    private readonly NormalCameraModel normal;
    private readonly SourceCinematicProcessClock process = new();
    private readonly SourceCinematicTarget[] targets = new SourceCinematicTarget[14];
    private SourceCinematicSeed previousNormal;
    public SourceCinematicPlayback Playback { get; }
    public SourceCinematicTransform Current => Playback.CurrentSourceCamera;
    public long LastSourceTick => Playback.LastCameraTick;
    public bool Suspended { get; private set; }
    public long CueCount { get; private set; }
    public double[] ViewProjection { get; private set; }

    public PreviewCamera(PlayerData data, RenderAssets assets, GameSimulation game)
    {
        this.data = data;
        this.game = game;
        normal = new NormalCameraModel(assets.ActorHeightYards, assets.ActorRadiusYards);
        float aspect = SourceCameraFrameClock.RequireOriginalRasterAspect(
            data.Bootstrap.HorizontalResolution, data.Bootstrap.VerticalResolution);
        previousNormal = Seed(normal.Update(game.State, aspect, 0f, true), default, 0f);
        Playback = new SourceCinematicPlayback(data.Impact, previousNormal);
        ViewProjection = FrameMath.Camera(Current, data.Bootstrap.HorizontalResolution, data.Bootstrap.VerticalResolution,
            FarDistance(Current));
        game.SourceCameraHeadingProvider = Sample;
    }

    private int Sample(IGameState state, long tick)
    {
        DataCatalog.Require(((ISourceGameState)state).SourceTick == tick,
            "The camera needs the executing pre-player source snapshot, never the last render frame.");
        Playback.ObserveCameraProcessSuspension(false);
        Suspended = false;
        int rawMode = game.SourceCameraRawGameMode;
        bool newPlay = process.BeginsNewPlay(rawMode);
        float aspect = SourceCameraFrameClock.RequireOriginalRasterAspect(
            data.Bootstrap.HorizontalResolution, data.Bootstrap.VerticalResolution);
        NormalCameraFrame frame = normal.Update(state, aspect, 1f / SourceTiming.DefaultTicksPerSecond, newPlay);
        SourceCinematicSeed next = Seed(frame, default, 0f);
        if (Playback.LastCameraTick >= 0 && !newPlay)
            next = Seed(frame, new SourceVector3(next.Focus.X - previousNormal.Focus.X,
                next.Focus.Y - previousNormal.Focus.Y, next.Focus.Z - previousNormal.Focus.Z),
                next.Distance - previousNormal.Distance);
        previousNormal = next;
        SourceCinematicSeed target = Seed(normal.SourceTransientReturnTarget(state, aspect), default, 0f);
        int mask = 0;
        foreach (PlayerView actor in state.Players)
        {
            DataCatalog.Require(actor.Id is >= 0 and < 14 && (mask & (1 << actor.Id)) == 0,
                "The source camera requires every unique actual actor.");
            SourceCinematicTarget facing = SourceCinematicTarget.FromActor(actor);
            targets[actor.Id] = new SourceCinematicTarget(game.SourceActorWorldPosition(actor.Id), facing.Facing);
            mask |= 1 << actor.Id;
        }
        DataCatalog.Require(mask == (1 << 14) - 1, "The real source camera requires all fourteen actors.");
        int? x = null, z = null;
        if (Playback.NeedsShakeDraws)
        {
            x = game.DrawSourceCameraRandom(3, "cambot.c:1009/shaker-X");
            z = game.DrawSourceCameraRandom(3, "cambot.c:1010/shaker-Z");
        }
        Playback.CameraSourceTick(tick, game.SourceCameraStageOrder, targets, target, newPlay, x, z, next);
        process.CompleteCameraTick(rawMode);
        SourceCinematicTransform executed = Playback.RequireSourceCameraAtTick(tick);
        game.SetSourceMovementCameraFrame(new SourceMovementCameraFrame(executed.Position, executed.Transform,
            data.Bootstrap.HorizontalResolution, data.Bootstrap.VerticalResolution), tick);
        ViewProjection = FrameMath.Camera(executed, data.Bootstrap.HorizontalResolution, data.Bootstrap.VerticalResolution,
            FarDistance(executed));
        return Playback.SourceHeading1024;
    }

    public void ObserveHostStep(IGameState state)
    {
        SourceMatchPhase phase = ((ISourceMatchState)state).Match.Phase;
        Suspended = phase is SourceMatchPhase.PlaySelection or SourceMatchPhase.PatSelection;
        Playback.ObserveCameraProcessSuspension(Suspended);
    }

    public void Cue(SourcePresentationCue cue)
    {
        if (cue.Kind is SourcePresentationCueKind.CameraOpcode or SourcePresentationCueKind.ContactCameraSelection
            or SourcePresentationCueKind.ShakerOpcode or SourcePresentationCueKind.PlayerDownCallback)
        {
            Playback.Execute(new SourcePresentationCueBatch(new[] { cue }));
            CueCount++;
        }
    }

    private float FarDistance(SourceCinematicTransform camera)
    {
        float far = 0;
        foreach (var mesh in data.World.Manifest.meshes)
        {
            float[] bounds = mesh.boundsUnity;
            for (int x = 0; x < 2; x++)
            for (int y = 0; y < 2; y++)
            for (int z = 0; z < 2; z++)
            {
                float dx = bounds[x == 0 ? 0 : 3] - camera.Position.X;
                float dy = bounds[y == 0 ? 1 : 4] - camera.Position.Y;
                float dz = -bounds[z == 0 ? 2 : 5] - camera.Position.Z;
                far = Math.Max(far, (float)Math.Sqrt(dx * dx + dy * dy + dz * dz));
            }
        }
        return far + SourceTiming.SourceUnitsPerYard;
    }

    private static SourceCinematicSeed Seed(NormalCameraFrame frame, SourceVector3 velocity, float distanceVelocity)
    {
        float units = SourceTiming.SourceUnitsPerYard;
        return new SourceCinematicSeed(
            new SourceVector3(frame.Eye.X * units, frame.EyeHeight * units, frame.Eye.Z * units),
            new SourceVector3(frame.Focus.X * units, NormalCameraModel.FocusHeightYards * units, frame.Focus.Z * units),
            frame.Distance * units, SourceCinematicMath.Radians(frame.Direction > 0 ? 90 : 270),
            SourceCinematicMath.Radians(-NormalCameraModel.PitchDegrees), velocity, distanceVelocity);
    }
}
