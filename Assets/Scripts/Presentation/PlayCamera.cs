using Blitz.AssetImport;
using Blitz.Contracts;
using UnityEngine;

namespace Blitz.Presentation
{
    internal readonly struct HudLayout
    {
        internal readonly float Scale;
        internal readonly float Width;
        internal readonly float Height;
        internal readonly bool Narrow;
        internal readonly float Header;
        internal readonly float Footer;
        internal readonly float Sidebar;
        internal readonly Rect Viewport;

        internal HudLayout(int pixelWidth, int pixelHeight)
        {
            Scale = pixelWidth < 650 ? Mathf.Max(0.5f, pixelWidth / 480f) : Mathf.Clamp(pixelWidth / 1280f, 0.82f, 1.4f);
            Width = pixelWidth / Scale;
            Height = pixelHeight / Scale;
            Narrow = Width < 650f;
            Sidebar = pixelWidth >= 1000 && pixelHeight >= 520 ? 300f : 0f;
            Header = Narrow ? 142f : 104f;
            Footer = Sidebar > 0f ? 88f : Narrow ? 252f : 184f;
            if (Height < 500f)
            {
                Header = 90f;
                Footer = 126f;
            }
            float sceneHeight = Mathf.Max(1f, Height - Header - Footer);
            Viewport = new Rect(0f, Footer / Height, (Width - Sidebar) / Width, sceneHeight / Height);
        }
    }

    internal sealed class PlayCamera
    {
        private readonly Camera camera;
        // Yard-space envelope of FieldScene's interim primitive actor/held ball, not an original mesh measurement.
        private readonly NormalCameraModel model = new NormalCameraModel(2.35f, 1.05f);
        private SourceCinematicPresenter sourceCinematics;
        private readonly bool hasOriginalWorldBounds;
        private readonly Bounds originalWorldBounds;
        private bool wasFocused;
        private int normalDirection = 1;
        private bool sourceStageDriven;
        private NormalCameraFrame sourceNormalFrame;
        private SourceCinematicSeed previousNormalSourceState;
        private long lastSourceStageTick = -1;
        private float sourceAspect = 512f / 384f;
        private bool sourceCameraSuspended, allowSourceStageGap;
        private readonly SourceCinematicProcessClock sourceProcessClock = new SourceCinematicProcessClock();
        private readonly SourceCinematicTarget[] sourceTargets = new SourceCinematicTarget[SliceRules.PlayersPerTeam * 2];
        private System.Func<int> sourceCameraOrder;
        private System.Func<int, string, int> sourceCameraRandom;
        private System.Func<IGameState, int> sourceRawGameMode;
        private System.Func<int, SourceVector3> sourceActorRoot;
        private System.Func<int, int> stageRandomDraw;
        private int stageRandomAxis;
        private readonly SourcePlayerRig sourceProfilePrefab;
        private readonly bool movement157;
        private bool sourceMovementRasterConfigured;

        internal Camera Camera { get { return camera; } }
        internal SourceCinematicPlayback SourcePlayback
        {
            get
            {
                if (sourceCinematics == null) throw new System.InvalidOperationException("Original source camera stage is not initialized.");
                return sourceCinematics.Playback;
            }
        }
        internal SourceCinematicTransform CurrentSourceCamera { get { return SourcePlayback.CurrentSourceCamera; } }
        internal SourceCinematicTransform RequireSourceCameraAtTick(long executingSourceTick)
        {
            if (!sourceStageDriven || sourceCameraSuspended || lastSourceStageTick != executingSourceTick)
                throw new System.InvalidOperationException("Sample the exact executing pre-player source camera tick before publishing its movement frame.");
            return SourcePlayback.RequireSourceCameraAtTick(executingSourceTick);
        }

        internal void ConfigureSourceMovementRaster(float horizontalResolution, float verticalResolution)
        {
            if (!movement157 || sourceStageDriven)
                throw new System.InvalidOperationException("Configure M1's explicit original DIP raster512x384 or512x256 before initializing the source camera.");
            sourceAspect = SourceCameraFrameClock.RequireOriginalRasterAspect(horizontalResolution, verticalResolution);
            sourceMovementRasterConfigured = true;
        }
        internal int SourceHeading1024
        {
            get
            {
                return sourceCinematics != null && sourceCinematics.Playback.LastCameraTick >= 0 ?
                    sourceCinematics.Playback.SourceHeading1024 : normalDirection < 0 ? 768 : 256;
            }
        }

        internal void AttachSourceCinematics(SourceCinematicPlayback playback)
        {
            if (sourceCinematics != null)
                throw new System.InvalidOperationException("The original source camera already has a presentation consumer.");
            sourceCinematics = new SourceCinematicPresenter(playback);
        }

        internal void InitializeSourceStage(IGameState initialState)
        {
            if (sourceCinematics != null)
                throw new System.InvalidOperationException("Source camera stage is already initialized.");
            camera.rect = new Rect(0f, 0f, 1f, 1f);
            if (movement157 && !sourceMovementRasterConfigured)
                throw new System.InvalidOperationException("Original movement source camera needs explicitly configured DIP resolution; browser viewport is not an input.");
            if (!movement157) sourceAspect = Mathf.Max(.01f, Screen.width / (float)Mathf.Max(1, Screen.height));
            sourceNormalFrame = model.Update(initialState, sourceAspect, 0f, true);
            previousNormalSourceState = SourceSeed(sourceNormalFrame, default(SourceVector3), 0f);
            AttachSourceCinematics(new SourceCinematicPlayback(SourcePlayerProvider.ResolveRequiredCinematicLibrary(sourceProfilePrefab),
                previousNormalSourceState));
            sourceStageDriven = true;
        }

        internal void ConfigureSourceStage(IGameState initialState, System.Func<int> actualCameraOrder,
            System.Func<int, string, int> actualCameraRandom, System.Func<IGameState, int> actualRawGameMode,
            System.Func<int, SourceVector3> actualSourceActorRoot)
        {
            if (actualCameraOrder == null || actualCameraRandom == null || actualRawGameMode == null || actualSourceActorRoot == null)
                throw new System.InvalidOperationException("Original source camera registration requires actual Core order/RNG/game-mode/source-world getters.");
            sourceCameraOrder = actualCameraOrder; sourceCameraRandom = actualCameraRandom;
            sourceRawGameMode = actualRawGameMode; sourceActorRoot = actualSourceActorRoot;
            stageRandomDraw = DrawStageRandom;
            InitializeSourceStage(initialState);
        }

        internal void ConfigureSourceStage(IGameState initialState, System.Func<int> actualCameraOrder,
            System.Func<int, string, int> actualCameraRandom, System.Func<int, SourceVector3> actualSourceActorRoot)
        {
            if (actualCameraOrder == null || actualCameraRandom == null || actualSourceActorRoot == null)
                throw new System.InvalidOperationException("Original camera needs Core's actual stage order, shared camera RNG and canonical source roots.");
            sourceCameraOrder = actualCameraOrder; sourceCameraRandom = actualCameraRandom;
            sourceActorRoot = actualSourceActorRoot; sourceRawGameMode = null;
            stageRandomDraw = DrawStageRandom;
            InitializeSourceStage(initialState);
        }

        internal int SampleAtSourceTick(IGameState preActorState, long executingSourceTick)
        {
            if (sourceCameraOrder == null || sourceCameraRandom == null || sourceActorRoot == null)
                throw new System.InvalidOperationException("ConfigureSourceStage before registering SampleAtSourceTick; a last-render getter is not a producer.");
            ISourceGameState source = preActorState as ISourceGameState;
            if (source == null || source.SourceTick != executingSourceTick)
                throw new System.InvalidOperationException("Source camera needs the exact executing pre-actor source-tick snapshot.");
            ObserveSourceCameraSuspension(false);
            stageRandomAxis = 0;
            if (sourceRawGameMode != null)
                return AdvanceSourceStage(preActorState, executingSourceTick, sourceCameraOrder(), stageRandomDraw,
                    sourceRawGameMode(preActorState));
            bool newPlay = sourceProcessClock.BeginsSourcePhase(preActorState.Phase);
            int heading = AdvanceSourceStage(preActorState, executingSourceTick, sourceCameraOrder(), stageRandomDraw, newPlay);
            sourceProcessClock.CompleteSourcePhase(preActorState.Phase);
            return heading;
        }

        internal void ConsumeSourceCue(SourcePresentationCue cue)
        {
            SourcePlayback.Execute(new SourcePresentationCueBatch(new[] { cue }));
        }

        internal void ConsumeResolvedSourceSound(Blitz.Gameplay.SourceSoundVmDispatch dispatch,
            System.Action<Blitz.Gameplay.SourceSoundVmDispatch> actualRawOutputConsumer = null)
        {
            SourcePlayback.ConsumeResolvedSourceSound(dispatch, actualRawOutputConsumer);
        }

        internal void ObserveSourceHostStep(IGameState state)
        {
            if (!sourceStageDriven) return;
            ISourceGameState source = state as ISourceGameState;
            if (source == null) throw new System.InvalidOperationException("Source camera host observation needs an actual source snapshot.");
            if (lastSourceStageTick >= 0 && source.SourceTick < lastSourceStageTick) ResetSourceStage(state);
            ISourceMatchState match = state as ISourceMatchState;
            bool suspended = state.Phase == GamePhase.PlaySelect || match != null && match.Match != null &&
                (match.Match.Phase == SourceMatchPhase.PlaySelection || match.Match.Phase == SourceMatchPhase.PatSelection);
            ObserveSourceCameraSuspension(suspended);
        }

        private int DrawStageRandom(int bound)
        {
            if (bound != 3 || stageRandomAxis >= 2)
                throw new System.InvalidOperationException("This source impact camera stage consumes only actual ordered shaker X/Z randrng(3).");
            string site = stageRandomAxis++ == 0 ? "cambot.c:1009/shaker-X" : "cambot.c:1010/shaker-Z";
            return sourceCameraRandom(bound, site);
        }

        internal int AdvanceSourceStage(IGameState beforeActors, long executingSourceTick, int actualProcessOrder,
            System.Func<int, int> actualCameraRandrng, int actualRawGameMode)
        {
            bool newPlay = sourceProcessClock.BeginsNewPlay(actualRawGameMode);
            int heading = AdvanceSourceStage(beforeActors, executingSourceTick, actualProcessOrder, actualCameraRandrng, newPlay);
            sourceProcessClock.CompleteCameraTick(actualRawGameMode);
            return heading;
        }

        internal int AdvanceSourceStage(IGameState beforeActors, long executingSourceTick, int actualProcessOrder,
            System.Func<int, int> actualCameraRandrng, bool actualNewPlayAfterPlayOver)
        {
            if (!sourceStageDriven || sourceCinematics == null || actualCameraRandrng == null)
                throw new System.InvalidOperationException("Bind the original camera stage and its real Core camera-process RNG before source actors execute.");
            if (sourceCameraSuspended)
                throw new System.InvalidOperationException("CAMBOT is suspended by actual source menu processing.");
            if (lastSourceStageTick >= 0 && executingSourceTick != lastSourceStageTick + 1 && !allowSourceStageGap)
                throw new System.InvalidOperationException("Original camera stage must run once on every executing source tick, including host catch-up steps.");
            sourceNormalFrame = model.Update(beforeActors, sourceAspect, 1f / SourceTiming.DefaultTicksPerSecond,
                actualNewPlayAfterPlayOver);
            SourceCinematicSeed normal = SourceSeed(sourceNormalFrame, default(SourceVector3), 0f);
            if (lastSourceStageTick >= 0 && !actualNewPlayAfterPlayOver)
                normal = SourceSeed(sourceNormalFrame,
                    new SourceVector3(normal.Focus.X - previousNormalSourceState.Focus.X,
                        normal.Focus.Y - previousNormalSourceState.Focus.Y, normal.Focus.Z - previousNormalSourceState.Focus.Z),
                    normal.Distance - previousNormalSourceState.Distance);
            previousNormalSourceState = normal;
            SourceCinematicSeed target = SourceSeed(model.SourceTransientReturnTarget(beforeActors, sourceAspect),
                default(SourceVector3), 0f);
            int mask = 0;
            foreach (PlayerView actor in beforeActors.Players)
            {
                if (actor.Id < 0 || actor.Id >= sourceTargets.Length || (mask & (1 << actor.Id)) != 0)
                    throw new System.InvalidOperationException("Original camera needs unique actor-ID ordered source targets.");
                SourceCinematicTarget targetActor = SourceCinematicTarget.FromActor(actor);
                sourceTargets[actor.Id] = sourceActorRoot == null ? targetActor :
                    new SourceCinematicTarget(sourceActorRoot(actor.Id), targetActor.Facing);
                mask |= 1 << actor.Id;
            }
            if (mask != (1 << sourceTargets.Length) - 1)
                throw new System.InvalidOperationException("Original camera needs all fourteen actual source actor targets.");
            int? randomX = null, randomZ = null;
            if (sourceCinematics.Playback.NeedsShakeDraws)
            {
                randomX = actualCameraRandrng(3);
                randomZ = actualCameraRandrng(3);
            }
            sourceCinematics.Playback.CameraSourceTick(executingSourceTick, actualProcessOrder, sourceTargets, target,
                actualNewPlayAfterPlayOver, randomX, randomZ, normal);
            lastSourceStageTick = executingSourceTick;
            allowSourceStageGap = false;
            return sourceCinematics.Playback.SourceHeading1024;
        }

        internal void ObserveSourceCameraSuspension(bool actualSuspended)
        {
            if (!sourceStageDriven)
                throw new System.InvalidOperationException("Initialize the original camera stage before observing process suspension.");
            if (sourceCameraSuspended && !actualSuspended) allowSourceStageGap = true;
            sourceCameraSuspended = actualSuspended;
            sourceCinematics.Playback.ObserveCameraProcessSuspension(actualSuspended);
        }

        internal void ResetSourceStage(IGameState initialState)
        {
            if (!sourceStageDriven) return;
            sourceNormalFrame = model.Update(initialState, sourceAspect, 0f, true);
            previousNormalSourceState = SourceSeed(sourceNormalFrame, default(SourceVector3), 0f);
            sourceCinematics.Playback.Reset(previousNormalSourceState); lastSourceStageTick = -1;
            sourceCameraSuspended = allowSourceStageGap = false;
            sourceProcessClock.Reset();
            stageRandomAxis = 0;
        }

        private static SourceCinematicSeed SourceSeed(NormalCameraFrame frame, SourceVector3 velocity, float distanceVelocity)
        {
            float units = SourceTiming.SourceUnitsPerYard;
            return new SourceCinematicSeed(new SourceVector3(frame.Eye.X * units, frame.EyeHeight * units, frame.Eye.Z * units),
                new SourceVector3(frame.Focus.X * units, NormalCameraModel.FocusHeightYards * units, frame.Focus.Z * units),
                frame.Distance * units, SourceCinematicMath.Radians(frame.Direction > 0 ? 90 : 270),
                SourceCinematicMath.Radians(-NormalCameraModel.PitchDegrees), velocity, distanceVelocity);
        }

        internal PlayCamera(Transform parent) : this(parent, null) { }

        internal PlayCamera(Transform parent, SourcePlayerRig sourcePrefab)
        {
            sourceProfilePrefab = sourcePrefab;
            movement157 = sourcePrefab != null && SourceTeamResourceProfile.FromStableId(sourcePrefab.StableId).IsMovement157;
            camera = new GameObject("Play camera").AddComponent<Camera>();
            camera.transform.SetParent(parent, false);
            camera.tag = "MainCamera";
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Palette.Ink;
            camera.orthographic = false;
            camera.nearClipPlane = 0.3f;
            camera.farClipPlane = 230f;
            camera.allowHDR = false;
            camera.allowMSAA = true;
            if (parent != null)
            {
                SourceEnvironmentProvider[] worlds = parent.GetComponentsInChildren<SourceEnvironmentProvider>(true);
                bool found = false;
                Bounds bounds = default(Bounds);
                foreach (SourceEnvironmentProvider world in worlds)
                foreach (MeshRenderer renderer in world.GetComponentsInChildren<MeshRenderer>(true))
                {
                    if (!found) { bounds = renderer.bounds; found = true; }
                    else bounds.Encapsulate(renderer.bounds);
                }
                hasOriginalWorldBounds = found;
                originalWorldBounds = bounds;
            }
        }

        internal void Render(IGameState state, bool immediate)
        {
            HudLayout layout = new HudLayout(Screen.width, Screen.height);
            camera.rect = sourceStageDriven ? new Rect(0f, 0f, 1f, 1f) : layout.Viewport;
            float aspect = Mathf.Max(0.01f, camera.pixelWidth / (float)Mathf.Max(1, camera.pixelHeight));
            if (!movement157) sourceAspect = aspect;
            bool focused = Application.isFocused;
            ISourceGameState source = state as ISourceGameState;
            if (sourceStageDriven && immediate && source != null && lastSourceStageTick >= 0 && source.SourceTick < lastSourceStageTick)
                ResetSourceStage(state);
            NormalCameraFrame frame = sourceStageDriven ?
                new NormalCameraFrame(sourceNormalFrame.Focus, sourceNormalFrame.Distance, aspect, sourceNormalFrame.Direction) :
                model.Update(state, aspect, focused ? Time.unscaledDeltaTime : 0f, immediate || (focused && !wasFocused));
            wasFocused = focused;
            normalDirection = frame.Direction;
            camera.aspect = frame.Aspect;
            camera.fieldOfView = frame.VerticalFieldOfView;
            camera.transform.position = new Vector3(frame.Eye.X, frame.EyeHeight, frame.Eye.Z);
            camera.transform.LookAt(new Vector3(frame.Focus.X, NormalCameraModel.FocusHeightYards, frame.Focus.Z));
            if (sourceCinematics != null) sourceCinematics.Apply(camera);
            if (hasOriginalWorldBounds)
            {
                Vector3 eye = camera.transform.position, min = originalWorldBounds.min, max = originalWorldBounds.max;
                camera.farClipPlane = SourceCinematicDepthRange.RequiredFarPlane(new SourceVector3(eye.x, eye.y, eye.z),
                    new SourceVector3(min.x, min.y, min.z), new SourceVector3(max.x, max.y, max.z));
            }
        }
    }
}
