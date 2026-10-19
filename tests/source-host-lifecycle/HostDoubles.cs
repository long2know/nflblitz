// Managed collaborators for the real BlitzRuntime and SourceFreeBallRenderer.
// These are not Unity engine implementations or native/player test evidence.
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Blitz.Contracts;

internal static class HostProbe
{
    internal static readonly List<string> Calls = new List<string>();
    internal static string Fault;
    internal static bool Source = true, Fire = true, CabinetHeld;
    internal static Blitz.Runtime.FixedStepBridge Bridge;
    internal static Blitz.Runtime.SourceCabinetUnityInput Input;
    internal static Blitz.Presentation.PlayHud Hud;
    internal static int Captures, TrailCaptures, Draws, CommandBuffers, Errors;
    internal static void Record(string call) { Calls.Add(call); }
    internal static int Count(string call) { return Calls.FindAll(value => value == call).Count; }
    internal static void Reset(string fault, bool source)
    {
        Calls.Clear(); Fault = fault; Source = source; Fire = true; CabinetHeld = false;
        Bridge = null; Input = null; Hud = null; Captures = TrailCaptures = Draws = CommandBuffers = Errors = 0;
        UnityEngine.Input.Clear();
        UnityEngine.Application.absoluteURL = source ? "https://managed.invalid/" : "https://managed.invalid/?actors=graybox";
    }
}

namespace UnityEngine
{
    [AttributeUsage(AttributeTargets.Class)] public sealed class DisallowMultipleComponent : Attribute { }
    [AttributeUsage(AttributeTargets.Field)] public sealed class SerializeField : Attribute { }
    public class MonoBehaviour
    {
        public readonly Transform transform = new Transform();
        public readonly GameObject gameObject = new GameObject();
        public bool isActiveAndEnabled = true;
        protected static void Destroy(object value) { }
    }
    public sealed class Transform { }
    public sealed class GameObject
    {
        public T AddComponent<T>() where T : new()
        {
            HostProbe.Record("add." + typeof(T).Name);
            return new T();
        }
    }
    public sealed class Camera
    {
        public void AddCommandBuffer(Rendering.CameraEvent when, Rendering.CommandBuffer commands)
        { HostProbe.CommandBuffers++; HostProbe.Record("ball.commands.attach"); }
        public void RemoveCommandBuffer(Rendering.CameraEvent when, Rendering.CommandBuffer commands) { }
    }
    public static class Resources
    {
        public static T Load<T>(string key) where T : class
        {
            HostProbe.Record("resource." + key);
            if (typeof(T) == typeof(Blitz.Runtime.SourceInterfaceServices))
                return new Blitz.Runtime.SourceInterfaceServices() as T;
            if (typeof(T) == typeof(Blitz.Presentation.SourceFireSpfxAsset))
                return new Blitz.Presentation.SourceFireSpfxAsset() as T;
            if (typeof(T) == typeof(Blitz.Presentation.SourceFreeBallAsset))
                return HostProbe.Fault == "missing-resource" ? null : new Blitz.Presentation.SourceFreeBallAsset() as T;
            throw new InvalidOperationException("Unexpected managed resource: " + key);
        }
    }
    public enum KeyCode
    {
        R, W, A, S, D, UpArrow, DownArrow, LeftArrow, RightArrow, LeftShift, RightShift,
        Alpha1, Alpha2, Alpha3, Keypad1, Keypad2, Keypad3, Space, F, Q
    }
    public static class Input
    {
        private static readonly HashSet<KeyCode> held = new HashSet<KeyCode>();
        private static readonly HashSet<KeyCode> down = new HashSet<KeyCode>();
        public static bool GetKey(KeyCode key) { return held.Contains(key); }
        public static bool GetKeyDown(KeyCode key) { return down.Contains(key); }
        public static void Clear() { held.Clear(); down.Clear(); }
        public static void Press(KeyCode key) { held.Add(key); down.Add(key); }
    }
    public static class Application { public static string absoluteURL; public static int targetFrameRate; public static bool runInBackground; }
    public static class QualitySettings { public static int vSyncCount; }
    public static class Time { public static float unscaledDeltaTime = 1f / 60f; }
    public static class Debug
    {
        public static void Log(string value) { }
        public static void LogWarning(string value) { }
        public static void LogError(string value) { HostProbe.Errors++; HostProbe.Record("error." + value); }
    }
    public sealed class Event { }
    public struct Color { public static Color white { get { return default(Color); } } }
    public enum TextAnchor { UpperLeft }
    public sealed class GUIStyle
    {
        public TextAnchor alignment; public bool wordWrap; public int fontSize;
        public GUIStyle(GUIStyle other) { }
    }
    public sealed class GUISkin { public GUIStyle box; }
    public static class GUI
    {
        public static int depth; public static Color color; public static readonly GUISkin skin = new GUISkin();
        public static string LastBox;
        public static void Box(Rect rect, string text, GUIStyle style) { LastBox = text; }
    }
    public struct Rect { public Rect(float x, float y, float width, float height) { } }
    public static class Screen { public static int width = 512, height = 384; }
    public static class Mathf
    {
        public static float Max(float a, float b) { return Math.Max(a, b); }
        public static float Min(float a, float b) { return Math.Min(a, b); }
    }
    public struct Vector4 { public Vector4(float x, float y, float z, float w) { } }
    public struct Matrix4x4
    {
        private float[,] values;
        public static Matrix4x4 identity { get { return new Matrix4x4 { values = new float[4, 4] }; } }
        public float this[int row, int column] { get { return values[row, column]; } set { values[row, column] = value; } }
    }
    public sealed class Mesh { }
    public sealed class Material { }
    public sealed class MaterialPropertyBlock
    {
        public void SetMatrix(string name, Matrix4x4 value) { }
        public void SetVector(string name, Vector4 value) { }
        public void SetFloat(string name, float value) { }
    }
}

namespace UnityEngine.Scripting
{
    [AttributeUsage(AttributeTargets.Method)] public sealed class Preserve : Attribute { }
}

namespace UnityEngine.Rendering
{
    public enum CameraEvent { AfterForwardOpaque }
    public sealed class CommandBuffer
    {
        public string name;
        public void Clear() { HostProbe.Record("ball.present"); }
        public void DrawMesh(UnityEngine.Mesh mesh, UnityEngine.Matrix4x4 matrix, UnityEngine.Material material,
            int submesh, int pass, UnityEngine.MaterialPropertyBlock properties) { HostProbe.Draws++; }
        public void Release() { }
    }
}

namespace Blitz.Contracts
{
    public enum GamePhase { PlaySelect, PreSnap, LiveBall, DeadBall }
    public enum TeamId { Home, Away }
    public enum PlayId { Run, QuickPass, Cover, Blitz }
    public enum InputAction { SelectPlay, Snap, Continue, Pass, Tackle, SwitchDefender }
    public enum SourceButtons { Action }
    public enum SourceGameplayCompatibility { Original, Portable }
    public enum SourceKickMeterMode { Disabled, PortableAuthored }
    public enum SourceFireCull { Off }
    public enum SourcePresentationCueKind { PlayerDownCallback, Other }
    public enum SourceSoundVmStatus { Resolved }
    public struct SourceBallTrail { }
    public struct SourceRosterIdentity { }
    public struct SourceLooseHelmetDecal { }
    public struct FieldVector { public FieldVector(float x, float z) { } }
    public interface IGameState
    {
        long Tick { get; } GamePhase Phase { get; } int PlayNumber { get; }
        TeamId Possession { get; } int ControlledPlayerId { get; }
    }
    public interface ISourceMatchState : IGameState { SourceMatch Match { get; } }
    public interface ISourceGameState : IGameState { long SourceTick { get; } }
    public sealed class TestState : ISourceMatchState, ISourceGameState
    {
        public long Tick { get; set; } public long SourceTick { get; set; }
        public GamePhase Phase { get { return GamePhase.PlaySelect; } }
        public int PlayNumber { get { return 0; } }
        public TeamId Possession { get { return TeamId.Home; } }
        public int ControlledPlayerId { get { return 0; } }
        public SourceMatch Match { get; private set; }
        public TestState() { Match = new SourceMatch(); }
    }
    public sealed class SourceMatch { public readonly object Configuration = new object(); }
    public interface ISourceAnimationLibrary { }
    public sealed class TestAnimations : ISourceAnimationLibrary { }
    public sealed class GameEvent { }
    public sealed class SourceGameEvent { }
    public class SourcePresentationCue { }
    public sealed class SourceCallbackCue : SourcePresentationCue { public SourcePresentationCueKind Kind; }
    public sealed class SourcePresentationCueBatch
    { public readonly List<SourcePresentationCue> Cues = new List<SourcePresentationCue> { new SourceCallbackCue() }; }
    public sealed class SourceSoundVmDispatch { public SourceSoundVmStatus Status; }
    public sealed class SourceSoundVmBatch { public readonly List<SourceSoundVmDispatch> Dispatches = new List<SourceSoundVmDispatch>(); }
    public sealed class SourceContactSoundCue { }
    public interface ISourceSoundVmOutputConsumer { }
    public sealed class SourceInputFrame { }
    public static class SourceGameplayCompatibilityOptions
    { public static SourceGameplayCompatibility FromUrl(string url) { return SourceGameplayCompatibility.Original; } }
    public static class SourceFireGameplayOptions { public static bool PortableRequested(string url) { return HostProbe.Source && HostProbe.Fire; } }
    public static class SourceKickMeterOptions { public static SourceKickMeterMode FromUrl(string url) { return SourceKickMeterMode.Disabled; } }
    public sealed class SourceFireGameplayConfiguration
    {
        public SourceFireGameplayConfiguration(Blitz.Runtime.SourceFireSpfxRuntime runtime, int epoch, SourceFireCull cull,
            Func<SourceRosterIdentity, TeamId, SourceLooseHelmetDecal> readHelmetDecal = null) { }
    }
}

namespace Blitz.AssetImport
{
    public sealed class SourceInterfaceException : InvalidOperationException
    { public SourceInterfaceException(string message) : base(message) { } }
    public sealed class PlayerAssetException : Exception { }
    public sealed class SourceRosterException : Exception { }
    public sealed class SourceInterfaceMovementBootstrap { public int HorizontalResolution = 512, VerticalResolution = 384; }
    public static class SourceTeamPresentationModel
    {
        public static SourceLooseHelmetDecal LooseHelmetDecal(object rosters, object catalog, object match,
            Blitz.Presentation.SourcePresentationSettings settings, SourceRosterIdentity identity, TeamId side)
        { return new SourceLooseHelmetDecal(); }
    }
}

namespace Blitz.Contracts
{
    public readonly struct SourceCameraFeedback { }
}

namespace Blitz.Gameplay
{
    public sealed class SourceMovementCameraFrame
    { public SourceMovementCameraFrame(object position, object transform, int width, int height) { } }
    public sealed class GameSimulation
    {
        public IGameState State { get; private set; }
        public bool SourceFireEnabled;
        public Func<SourceInputFrame, SourceInputFrame> SourceStationInputTransform;
        public Func<IGameState, long, int> SourceCameraHeadingProvider;
        public int SourceCameraStageOrder = -1, SourceCameraRawGameMode, SourceCameraHeading;
        public long SourceCameraHeadingSampleTick;
        public object SourceFireFrame;
        public readonly List<SourceContactSoundCue> UnresolvedContactSoundCalls = new List<SourceContactSoundCue>();
        public SourcePresentationCueBatch PresentationCues { get; private set; }
        public GameSimulation(int seed) { Restart(); }
        public void Restart() { State = new TestState(); PresentationCues = new SourcePresentationCueBatch(); }
        public int DrawSourceCameraRandom(int range) { return 0; }
        public object SourceActorWorldPosition(int actor) { return null; }
        public void ApplySourceCameraFeedback(SourceCameraFeedback feedback) { }
        public void SetSourceMovementCameraFrame(SourceMovementCameraFrame frame, long tick) { }
        public SourceSoundVmBatch TakeSourceSoundVmDispatches()
        { HostProbe.Record("sound.take"); return new SourceSoundVmBatch(); }
    }
}

namespace Blitz.Presentation
{
    public sealed class SourcePlayerRig { }
    public sealed class SourceTeamResourceProfile
    {
        public string StableId { get { return "managed-profile"; } }
        public bool IsContact281C2M1 { get { return HostProbe.Source; } }
        public bool UsesC2Data { get { return HostProbe.Source; } }
        public static SourceTeamResourceProfile SelectRuntimeProfile(string url, string stableId) { return new SourceTeamResourceProfile(); }
    }
    public static class SourcePlayerProvider
    {
        internal static readonly ISourceAnimationLibrary Animations = new TestAnimations();
        public static SourcePlayerRig ResolveActorMode(SourceTeamResourceProfile profile) { return HostProbe.Source ? new SourcePlayerRig() : null; }
        public static ISourceAnimationLibrary ResolveRequiredAnimationLibrary(SourcePlayerRig prefab) { return Animations; }
        public static SourceTeamPresentationAsset ResolveRequiredTeamPresentation(SourcePlayerRig prefab)
        { return new SourceTeamPresentationAsset(); }
    }
    public sealed class SourceTeamPresentationAsset
    { public readonly object Rosters = new object(), Catalog = new object(); }
    public sealed class SourceHudAsset { public readonly HudData Data = new HudData(); }
    public sealed class HudData { public readonly object Input = new object(); }
    public sealed class SourcePresentationSettings { public int DesignatedHomeSide; }
    public sealed class SourceCinematicPlayback { }
    public sealed class SourceCinematicTransform { public object Position, Transform; }
    public sealed class FieldScene
    {
        public FieldScene(UnityEngine.Transform parent, SourcePlayerRig prefab, ISourceAnimationLibrary animations)
        { HostProbe.Record("field.create"); }
        public void BindSourceTeamDrawState(int side, Func<int, object> state) { }
        public void BindSourceFireFlash(object shader) { }
        public void Render(IGameState previous, IGameState current, float alpha, float delta) { HostProbe.Record("field.render"); }
        public void ResetRenderHistory() { HostProbe.Record("field.reset"); }
        public string AnimationEvidence(IGameState state) { return "{}"; }
        public void Dispose() { }
    }
    public sealed class PlayCamera
    {
        public readonly UnityEngine.Camera Camera = new UnityEngine.Camera();
        public readonly SourceCinematicPlayback SourcePlayback = new SourceCinematicPlayback();
        public readonly SourceCinematicTransform CurrentSourceCamera = new SourceCinematicTransform();
        public PlayCamera(UnityEngine.Transform parent, SourcePlayerRig prefab) { HostProbe.Record("camera.create"); }
        public void ConfigureSourceMovementRaster(int width, int height) { HostProbe.Record("camera.raster"); }
        public void ConfigureSourceStage(IGameState state, Func<int> order, Func<int, int> random,
            Func<IGameState, int> mode, Func<int, object> position, Action<SourceCameraFeedback> feedback)
        { HostProbe.Record("camera.stage"); }
        public int SampleAtSourceTick(IGameState state, long tick) { return 0; }
        public SourceCinematicTransform RequireSourceCameraAtTick(long tick) { return CurrentSourceCamera; }
        public void Render(IGameState state, bool reset)
        {
            if (HostProbe.Fault == "initial-camera") throw new InvalidOperationException("managed initial camera failure");
            HostProbe.Record(reset ? "camera.render.reset" : "camera.render");
        }
        public void ObserveSourceHostStep(IGameState state) { HostProbe.Record("camera.observe"); }
        public void ConsumeSourceCue(SourcePresentationCue cue) { HostProbe.Record("camera.cue"); }
        public void ConsumeResolvedSourceSound(SourceSoundVmDispatch dispatch, Action<SourceSoundVmDispatch> deliver) { deliver(dispatch); }
    }
    public sealed class PlayHud
    {
        public PlayHud() { HostProbe.Hud = this; }
        public void Initialize(Blitz.Runtime.BlitzRuntime runtime)
        {
            HostProbe.Record("hud.initialize");
            if (HostProbe.Fault == "hud") throw new InvalidOperationException("managed HUD initialization failure");
        }
        public void OnSourceHostStep(IGameState state) { HostProbe.Record("hud.source"); }
        public void ClearNotices() { HostProbe.Record("hud.clear"); }
        public void OnGameEvent(GameEvent value) { HostProbe.Record("hud.event"); }
        public void OnSourceGameEvent(SourceGameEvent value) { HostProbe.Record("hud.source-event"); }
    }
    public sealed class SourceFreeBallAsset
    {
        public const string ResourceKey = "BlitzSourceFreeBall/original-medball2";
        public readonly UnityEngine.Mesh Mesh = new UnityEngine.Mesh();
        public void Load()
        {
            HostProbe.Record("ball.load");
            if (HostProbe.Fault == "data") throw new Blitz.AssetImport.SourceInterfaceException("managed required medball2 data failure");
        }
        public UnityEngine.Material CreateMaterial(bool alternate)
        {
            HostProbe.Record(alternate ? "ball.material.alternate" : "ball.material.primary");
            if (HostProbe.Fault == (alternate ? "alternate-material" : "primary-material"))
                throw new InvalidOperationException("managed required free-ball material failure");
            return new UnityEngine.Material();
        }
    }
    public sealed class SourceFreeBallDraw
    {
        public readonly FreeBallState State = new FreeBallState();
        public readonly SourceBallTrail? Trail;
        public bool White, Alternate; public int Alpha = 255;
        public SourceFreeBallDraw(long sourceTick = 0, bool trail = false)
        {
            State.SourceTick = sourceTick;
            if (trail) Trail = new SourceBallTrail();
        }
        public float[] CameraMatrix(SourceCinematicTransform camera) { return new float[12]; }
    }
    public sealed class SourceLooseHelmetDraw { }
    public sealed class FreeBallState { public bool Visible = true; public long SourceTick; }
    public static class SourceCameraFrameClock
    {
        public static void RequireOriginalRasterAspect(float width, float height)
        { if (width != 512 || (height != 384 && height != 256)) throw new InvalidOperationException("Invalid managed raster input"); }
    }
    public sealed class SourceFireSpfxAsset
    {
        public const string ResourceKey = "managed-fire";
        public object PlayerFlashShader;
        public object Load() { return null; }
    }
    public sealed class SourceFireSpfxRenderer
    {
        public void Initialize(SourceFireSpfxAsset asset, UnityEngine.Camera camera, SourceTeamPresentationAsset teams) { }
        public void ResetSourceEpoch() { HostProbe.Record("fire.reset"); }
        public void ConsumeAfterSourceStep(object frame, IReadOnlyList<SourceLooseHelmetDraw> helmets) { }
    }
}

namespace Blitz.Runtime
{
    public sealed class SourceInterfaceServices
    {
        public readonly Blitz.Presentation.SourceHudAsset Hud = new Blitz.Presentation.SourceHudAsset();
        public readonly Blitz.Presentation.SourceTeamResourceProfile Profile = new Blitz.Presentation.SourceTeamResourceProfile();
        public readonly Blitz.AssetImport.SourceInterfaceMovementBootstrap MovementBootstrap = new Blitz.AssetImport.SourceInterfaceMovementBootstrap();
        public bool NativeGamepadsEnabled;
        public ISourceAnimationLibrary Animations { get { return Blitz.Presentation.SourcePlayerProvider.Animations; } }
        public object Configuration() { return new object(); }
        public Blitz.Presentation.SourcePresentationSettings PresentationSettings
        { get { return new Blitz.Presentation.SourcePresentationSettings(); } }
        public static string ResourceKeyFor(Blitz.Presentation.SourceTeamResourceProfile profile) { return "managed-services"; }
        public Blitz.Gameplay.GameSimulation CreateSimulation(Blitz.Presentation.SourcePlayerRig prefab,
            SourceGameplayCompatibility compatibility, SourceFireGameplayConfiguration fireEffects, SourceKickMeterMode kickMeters)
        { return new Blitz.Gameplay.GameSimulation(1) { SourceFireEnabled = fireEffects != null }; }
    }
    public static class SourceInterfacePresentation
    {
        public static Blitz.Presentation.SourcePresentationSettings Settings(IGameState state) { return new Blitz.Presentation.SourcePresentationSettings(); }
        public static object DrawState(int actor, IGameState state) { return null; }
    }
    public sealed class SourceFireSpfxRuntime { public SourceFireSpfxRuntime(object data) { } }
    public sealed class SourceCabinetPlaySelection
    {
        public SourceCabinetPlaySelection(Blitz.Presentation.HudData data) { }
        public SourceInputFrame ApplyAtSourceTick(SourceInputFrame frame) { return frame; }
        public void Clear() { HostProbe.Record("selection.clear"); }
        public void ClearPendingInput() { HostProbe.Record("selection.pending.clear"); }
    }
    public sealed class CabinetBuffer
    {
        public bool WaitingForNeutral = true;
        public SourceInputFrame NextHostFrame() { HostProbe.Record("input.frame"); return new SourceInputFrame(); }
    }
    public sealed class SourceCabinetUnityInput
    {
        public readonly CabinetBuffer Buffer = new CabinetBuffer();
        public SourceCabinetUnityInput(object configuration, object input, bool gamepads) { HostProbe.Input = this; }
        public void Poll(SourceMatch match)
        { HostProbe.Record("input.poll"); if (!HostProbe.CabinetHeld) Buffer.WaitingForNeutral = false; }
        public void Clear() { HostProbe.Record("input.clear"); Buffer.WaitingForNeutral = true; }
        public void Pulse(int station, SourceButtons button) { }
        public void KeyboardEvent(UnityEngine.Event value) { }
    }
    public sealed class SourceSoundVmTransport
    {
        public string Failure;
        public void Reset() { HostProbe.Record("sound.reset"); Failure = null; }
        public void Drain(SourceSoundVmBatch batch) { HostProbe.Record("sound.drain"); }
        public void FlushNativePrefix() { HostProbe.Record("sound.flush"); }
        public void CompletePresentationBatch() { HostProbe.Record("sound.complete"); }
        public void BindOutput(ISourceSoundVmOutputConsumer consumer) { }
        public SourceSoundVmDispatch TakeForCue(SourceCallbackCue cue) { return null; }
        public void DeliverResolved(SourceSoundVmDispatch dispatch) { }
    }
    public static class SourceFreeBallBinding
    {
        public static ReadOnlyCollection<Blitz.Presentation.SourceLooseHelmetDraw> CaptureHelmets(
            Blitz.Gameplay.GameSimulation simulation)
        { return new List<Blitz.Presentation.SourceLooseHelmetDraw>().AsReadOnly(); }
        public static Blitz.Presentation.SourceFreeBallDraw Capture(Blitz.Gameplay.GameSimulation simulation)
        {
            HostProbe.Captures++;
            return new Blitz.Presentation.SourceFreeBallDraw(((ISourceGameState)simulation.State).SourceTick);
        }
        public static ReadOnlyCollection<Blitz.Presentation.SourceFreeBallDraw> CaptureTrails(
            Blitz.Gameplay.GameSimulation simulation)
        {
            HostProbe.TrailCaptures++;
            return new List<Blitz.Presentation.SourceFreeBallDraw>
            {
                new Blitz.Presentation.SourceFreeBallDraw(((ISourceGameState)simulation.State).SourceTick, true)
            }.AsReadOnly();
        }
    }
    public sealed class FixedStepBridge
    {
        private readonly Blitz.Gameplay.GameSimulation simulation;
        public bool Active = true;
        public int Restarts, Activations, SourceAdvances, LegacyAdvances;
        public IGameState State { get { return simulation.State; } }
        public IGameState PreviousState { get { return State; } }
        public float RenderInterpolation01 { get { return 0; } }
        public event Action<GameEvent> EventRaised;
        public event Action<SourceGameEvent> SourceEventRaised;
        public event Action<SourcePresentationCue> SourcePresentationCueRaised;
        public event Action<IGameState> BeforeHostStep;
        public event Action<IGameState> AfterHostStep;
        public event Action AfterSimulationStep, AfterSourcePresentation, Restarted;
        public int HookCount
        {
            get
            {
                return Count(EventRaised) + Count(SourceEventRaised) + Count(SourcePresentationCueRaised) +
                    Count(BeforeHostStep) + Count(AfterHostStep) + Count(AfterSimulationStep) +
                    Count(AfterSourcePresentation) + Count(Restarted);
            }
        }
        private static int Count(Delegate value) { return value == null ? 0 : value.GetInvocationList().Length; }
        public FixedStepBridge(Blitz.Gameplay.GameSimulation value) { simulation = value; HostProbe.Bridge = this; }
        public void SetActive(bool active)
        { Active = active; if (active) Activations++; HostProbe.Record(active ? "bridge.active" : "bridge.inactive"); }
        public void Restart()
        {
            Restarts++; HostProbe.Record("bridge.restart"); simulation.Restart();
            if (Restarted != null) Restarted();
        }
        public void QueueAction(InputAction action, PlayId play, int targetPlayerId) { }
        public void AdvanceSource(float seconds, Func<SourceInputFrame> input)
        { if (Active) { SourceAdvances++; input(); EmitStep(); } }
        public void Advance(float seconds, FieldVector move, bool turbo)
        { if (Active) { LegacyAdvances++; EmitStep(); } }
        private void EmitStep()
        {
            if (BeforeHostStep != null) BeforeHostStep(State);
            if (AfterSimulationStep != null) AfterSimulationStep();
            if (EventRaised != null) EventRaised(new GameEvent());
            if (HostProbe.Source && SourceEventRaised != null) SourceEventRaised(new SourceGameEvent());
            if (HostProbe.Source && SourcePresentationCueRaised != null) SourcePresentationCueRaised(new SourceCallbackCue());
            if (AfterSourcePresentation != null) AfterSourcePresentation();
            if (AfterHostStep != null) AfterHostStep(State);
        }
    }
}
