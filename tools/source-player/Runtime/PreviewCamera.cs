using Blitz.AssetImport;
using Blitz.Contracts;
using Blitz.Gameplay;
using Blitz.Presentation;

namespace Blitz.SourcePlayer;

public sealed class PreviewCamera
{
    private readonly GameSimulation game;
    private readonly PlayerData data;
    public SourceCinematicPlayback Playback { get; }
    public SourceCinematicTransform Current => Playback.CurrentSourceCamera;
    public long LastSourceTick => Playback.LastCameraTick;
    public bool Suspended { get; private set; }
    public long CueCount { get; private set; }
    public double[] ViewProjection { get; private set; }
    public double[] CameraSpaceProjection { get; private set; }
    private readonly Queue<object> trace = new();
    public object[] Trace => trace.ToArray();

    public PreviewCamera(PlayerData data, RenderAssets assets, GameSimulation game)
    {
        this.data = data;
        this.game = game;
        SourceCameraFrameClock.RequireOriginalRasterAspect(
            data.Bootstrap.HorizontalResolution, data.Bootstrap.VerticalResolution);
        Playback = new SourceCinematicPlayback(data.Impact, data.NormalCamera,
            ((ISourceCameraState)game.State).CameraBoundary);
        ViewProjection = FrameMath.Camera(Current, data.Bootstrap.HorizontalResolution, data.Bootstrap.VerticalResolution,
            FarDistance(Current));
        CameraSpaceProjection = FrameMath.CameraSpace(data.Bootstrap.HorizontalResolution,
            data.Bootstrap.VerticalResolution, FarDistance(Current));
        game.SourceCameraHeadingProvider = Sample;
    }

    private int Sample(IGameState state, long tick)
    {
        DataCatalog.Require(((ISourceGameState)state).SourceTick == tick,
            "The camera needs the executing pre-player source snapshot, never the last render frame.");
        Playback.ObserveCameraProcessSuspension(false);
        Suspended = false;
        SourceCameraBoundary input = ((ISourceCameraState)state).CameraBoundary;
        DataCatalog.Require(input.SourceTick == tick && input.ProcessOrder == game.SourceCameraStageOrder &&
            input.RawGameMode == game.SourceCameraRawGameMode,
            "The original normal producer requires the actual immutable pre-actor Core boundary.");
        Playback.CameraSourceTick(input, data.Bootstrap.HorizontalResolution, data.Bootstrap.VerticalResolution,
            game.DrawSourceCameraRandom);
        game.ApplySourceCameraFeedback(Playback.CameraFeedback);
        SourceCinematicTransform executed = Playback.RequireSourceCameraAtTick(tick);
        game.SetSourceMovementCameraFrame(new SourceMovementCameraFrame(executed.Position, executed.Transform,
            data.Bootstrap.HorizontalResolution, data.Bootstrap.VerticalResolution), tick);
        ViewProjection = FrameMath.Camera(executed, data.Bootstrap.HorizontalResolution, data.Bootstrap.VerticalResolution,
            FarDistance(executed));
        CameraSpaceProjection = FrameMath.CameraSpace(data.Bootstrap.HorizontalResolution,
            data.Bootstrap.VerticalResolution, FarDistance(executed));
        SourceCinematicSeed raw = Playback.SourceState;
        Record(new { sourceTick = tick, kind = "camera-source-stage", processOrder = game.SourceCameraStageOrder,
            table = Playback.Frame.Table, ordinal = Playback.Frame.Ordinal, elapsed = Playback.Frame.ElapsedTicks,
            transient = Playback.IsTransientActive, resuming = Playback.IsResumingNormal,
            returned = Playback.Frame.ReturnedToNormal, rawMode = input.RawGameMode, rawFlags = input.RawGameFlags,
            normalWakes = Playback.OriginalNormal.NormalWakes, gopher = Playback.OriginalNormal.LastPlan.Gopher,
            targetOffscreen = Playback.CameraFeedback.TargetOffscreen,
            autoHike = Playback.CameraFeedback.OpeningAutoHikeTicks,
            carrierMode = Playback.OriginalNormal.LastPlan.CarrierMode,
            eye = new[] { executed.Position.X, executed.Position.Y, executed.Position.Z },
            focus = new[] { raw.Focus.X, raw.Focus.Y, raw.Focus.Z }, distance = raw.Distance,
            velocity = new[] { raw.Velocity.X, raw.Velocity.Y, raw.Velocity.Z, raw.DistanceVelocity,
                raw.ThetaVelocity, raw.PhiVelocity }, theta = raw.Theta, phi = raw.Phi,
            ballTarget = new[] { input.Ball.Target.X, input.Ball.Target.Z },
            ballPosition = new[] { input.Ball.Position.X, input.Ball.Position.Y, input.Ball.Position.Z } });
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
            var camera = cue as SourceCameraCue;
            var shaker = cue as SourceShakerCue;
            Record(new { sourceTick = cue.Context.SourceTick, kind = cue.Kind.ToString(),
                processOrder = cue.Context.ProcessOrder, actor = cue.Context.ActorPlayerId,
                function = cue.Context.ExecutingSymbol, line = cue.Context.SourceLine,
                table = camera?.Selection.TableSymbol, ordinal = camera?.Selection.SelectedOrdinal,
                amplitude = shaker?.Amplitude, totalTicks = shaker?.TotalTicks, carrier = cue.CarrierPlayerId });
        }
    }

    private void Record(object value)
    {
        trace.Enqueue(value);
        while (trace.Count > 180) trace.Dequeue();
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

}
