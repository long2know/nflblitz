using System;
using Blitz.AssetImport;
using Blitz.Contracts;
using Blitz.Presentation;
using UnityEngine;

namespace Blitz.Runtime
{
    [DisallowMultipleComponent]
    public sealed class BlitzRuntime : MonoBehaviour
    {
        private FixedStepBridge bridge;
        private FieldScene field;
        private PlayCamera playCamera;
        private PlayHud hud;
        private SourceInterfaceServices sourceServices;
        private SourceCabinetUnityInput sourceInput;
        private Blitz.Gameplay.GameSimulation simulation;
        private Func<IGameState, long, int> sourceCameraProducer;
        private long lastSourceCameraTick = -1;
        private int lastSourceCameraHeading;
        private bool focused = true;
        private bool paused;
        private bool waitingForRelease;
        private string sourceFailure;
        private uint evidenceSerial;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        [SerializeField] private string runtimeStatus;
#endif

        public IGameState State { get { return bridge == null ? null : bridge.State; } }
        public bool InputSuspended { get { return !focused || paused || !isActiveAndEnabled; } }
        public Camera GameCamera { get { return playCamera == null ? null : playCamera.Camera; } }
        public bool IsSourceProfile { get { return sourceServices != null; } }
        public string SourceFailure { get { return sourceFailure; } }
        public SourceHudAsset SourceHudAsset { get { return sourceServices == null ? null : sourceServices.Hud; } }
        public SourceCabinetPlaySelection SourceSelection { get; private set; }
        public Blitz.Gameplay.GameSimulation SourceSimulation { get { return IsSourceProfile ? simulation : null; } }
        public SourceCinematicPlayback SourcePlayback { get { return IsSourceProfile && playCamera != null ? playCamera.SourcePlayback : null; } }
        public event Action<SourceGameEvent> SourceEventRaised;
        public event Action<SourcePresentationCue> SourcePresentationCueRaised;
        public event Action<IGameState> BeforeHostStep;
        public event Action<IGameState> AfterHostStep;

        private void Awake()
        {
            Application.targetFrameRate = 60;
            Application.runInBackground = false;
            QualitySettings.vSyncCount = 0;
#if UNITY_WEBGL && !UNITY_EDITOR
            WebGLInput.captureAllKeyboardInput = false;
#endif
            try
            {
                SourcePlayerRig prefab = SourcePlayerProvider.ResolveActorMode();
                ISourceAnimationLibrary animations = null;
                if (prefab == null)
                {
                    if (!ExplicitGraybox(Application.absoluteURL))
                        throw new SourceInterfaceException("Original source player assets are missing. Only explicit ?actors=graybox selects LegacyBaseline60Hz; no implicit generic source-mode substitute was created.");
                    simulation = new Blitz.Gameplay.GameSimulation(1);
                }
                else
                {
                    sourceServices = Resources.Load<SourceInterfaceServices>(SourceInterfaceServices.ResourceKey);
                    if (sourceServices == null)
                        throw new SourceInterfaceException("Missing actual source match/HUD services at Resources/" +
                            SourceInterfaceServices.ResourceKey + ". Import the new pinned117/playbook/HUD resources; no legacy/font fallback is permitted.");
                    animations = sourceServices.Animations;
                    simulation = sourceServices.CreateSimulation(prefab);
                    SourceSelection = new SourceCabinetPlaySelection(sourceServices.Hud.Data);
                    simulation.SourceStationInputTransform = SourceSelection.ApplyAtSourceTick;
                    sourceInput = new SourceCabinetUnityInput(((ISourceMatchState)simulation.State).Match.Configuration,
                        sourceServices.Hud.Data.Input, sourceServices.NativeGamepadsEnabled);
                }
                bridge = new FixedStepBridge(simulation);
                field = new FieldScene(transform, prefab, animations);
                if (IsSourceProfile)
                {
                    SourcePresentationSettings settings = SourceInterfacePresentation.Settings(State);
                    field.BindSourceTeamDrawState((int)settings.DesignatedHomeSide,
                        actor => SourceInterfacePresentation.DrawState(actor, State));
                }
                playCamera = new PlayCamera(transform);
                if (IsSourceProfile)
                {
                    playCamera.ConfigureSourceStage(State, () => simulation.SourceCameraStageOrder,
                        simulation.DrawSourceCameraRandom, simulation.SourceActorWorldPosition);
                    RegisterSourceCameraTickProducer(playCamera.SampleAtSourceTick);
                    simulation.SourceCameraHeadingProvider = SampleSourceCameraHeading;
                    SourcePresentationCueRaised += playCamera.ConsumeSourceCue;
                    AfterHostStep += playCamera.ObserveSourceHostStep;
                }
                hud = gameObject.AddComponent<PlayHud>();
                hud.Initialize(this);
                bridge.EventRaised += OnGameEvent;
                bridge.SourceEventRaised += OnSourceGameEvent;
                bridge.SourcePresentationCueRaised += OnSourcePresentationCue;
                bridge.BeforeHostStep += OnBeforeHostStep;
                bridge.AfterHostStep += OnAfterHostStep;
                if (IsSourceProfile)
                {
                    hud.OnSourceHostStep(bridge.State);
                    ForwardInitialSourcePresentationCues();
                }
                field.Render(bridge.State, bridge.State, 1f, 0f);
                playCamera.Render(bridge.State, true);
            }
            catch (PlayerAssetException e) { FailSourceMode(e); }
            catch (InvalidOperationException e) { FailSourceMode(e); }
            catch (NotSupportedException e) { FailSourceMode(e); }
            catch (SourceRosterException e) { FailSourceMode(e); }
            catch (ArgumentException e) { FailSourceMode(e); }
        }

        private void Update()
        {
            if (InputSuspended || bridge == null || sourceFailure != null)
                return;
            if (IsSourceProfile)
            {
                try
                {
                    if (waitingForRelease && Input.GetKey(KeyCode.R)) sourceInput.Clear();
                    sourceInput.Poll(((ISourceMatchState)State).Match);
                    if (waitingForRelease && !sourceInput.Buffer.WaitingForNeutral && !Input.GetKey(KeyCode.R))
                        waitingForRelease = false;
                    if (!waitingForRelease && Input.GetKeyDown(KeyCode.R)) { RestartMatch(); return; }
                    bridge.AdvanceSource(Time.unscaledDeltaTime, sourceInput.Buffer.NextHostFrame);
                }
                catch (PlayerAssetException e) { FailSourceMode(e); }
                catch (InvalidOperationException e) { FailSourceMode(e); }
                catch (NotSupportedException e) { FailSourceMode(e); }
                catch (SourceRosterException e) { FailSourceMode(e); }
                catch (ArgumentException e) { FailSourceMode(e); }
                return;
            }
            if (waitingForRelease)
            {
                if (!AnyGameKeyHeld())
                    waitingForRelease = false;
                Advance(default(FieldVector), false);
                return;
            }
            if (Input.GetKeyDown(KeyCode.R))
            {
                RestartMatch();
                return;
            }

            ReadActionEdges();
            float x = Axis(KeyCode.W, KeyCode.UpArrow, KeyCode.S, KeyCode.DownArrow);
            float z = Axis(KeyCode.D, KeyCode.RightArrow, KeyCode.A, KeyCode.LeftArrow);
            bool turbo = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            Advance(new FieldVector(x, z), turbo);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            IGameState state = bridge.State;
            runtimeStatus = "tick=" + state.Tick + " phase=" + state.Phase + " play=" + state.PlayNumber
                + " possession=" + state.Possession + " controlled=" + state.ControlledPlayerId;
#endif
        }

        private void LateUpdate()
        {
            if (bridge == null || field == null || sourceFailure != null)
                return;
            try
            {
                field.Render(bridge.PreviousState, bridge.State, bridge.RenderInterpolation01, InputSuspended ? 0f : Time.unscaledDeltaTime);
                playCamera.Render(bridge.State, false);
            }
            catch (PlayerAssetException e) { FailSourceMode(e); }
            catch (InvalidOperationException e) { FailSourceMode(e); }
            catch (NotSupportedException e) { FailSourceMode(e); }
        }

        private void Advance(FieldVector move, bool turbo)
        {
            try { bridge.Advance(Time.unscaledDeltaTime, move, turbo); }
            catch (PlayerAssetException e) { FailSourceMode(e); }
            catch (InvalidOperationException e) { FailSourceMode(e); }
            catch (NotSupportedException e) { FailSourceMode(e); }
        }

        [UnityEngine.Scripting.Preserve]
        public void LogAnimationEvidence()
        {
            if (field != null && sourceFailure == null)
            {
                string json = field.AnimationEvidence(bridge.State);
                const int chunkLength = 3000;
                int count = (json.Length + chunkLength - 1) / chunkLength;
                string sample = bridge.State.Tick + "-" + (++evidenceSerial);
                for (int i = 0; i < count; i++)
                    Debug.Log("SOURCE_ANIMATION_EVIDENCE_PART " + sample + " " + i + "/" + count + " " +
                        json.Substring(i * chunkLength, Math.Min(chunkLength, json.Length - i * chunkLength)));
            }
        }

        internal void FailSourceMode(Exception error)
        {
            sourceFailure = error.Message;
            if (bridge != null) bridge.SetActive(false);
            Debug.LogError("SOURCE MODE ERROR: " + sourceFailure);
        }

        private void OnGUI()
        {
            if (sourceFailure == null) return;
            int depth = GUI.depth;
            GUI.depth = -10000;
            GUI.color = Color.white;
            var errorStyle = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft, wordWrap = true, fontSize = 16 };
            GUI.Box(new Rect(12, 12, Mathf.Max(280, Screen.width - 24), Mathf.Min(260, Screen.height - 24)),
                "SOURCE MODE ERROR\n" + sourceFailure + "\nNo generic HUD/font/texture/static/graybox substitute was created.", errorStyle);
            GUI.depth = depth;
        }

        internal void SelectPlay(int choice)
        {
            if (IsSourceProfile || bridge == null || State.Phase != GamePhase.PlaySelect)
                return;
            PlayId play = State.Possession == TeamId.Home
                ? (choice == 1 ? PlayId.Run : PlayId.QuickPass)
                : (choice == 1 ? PlayId.Cover : PlayId.Blitz);
            QueueAction(InputAction.SelectPlay, play);
        }

        internal void PrimaryAction()
        {
            if (IsSourceProfile || State == null)
                return;
            if (State.Phase == GamePhase.PreSnap)
                QueueAction(InputAction.Snap);
            else if (State.Phase == GamePhase.DeadBall)
                QueueAction(InputAction.Continue);
        }

        internal void QueueAction(InputAction action, PlayId play = PlayId.Run, int targetPlayerId = -1)
        {
            if (!IsSourceProfile && !InputSuspended && !waitingForRelease && bridge != null)
                bridge.QueueAction(action, play, targetPlayerId);
        }

        internal void RestartMatch()
        {
            if (bridge == null)
                return;
            bridge.Restart();
            lastSourceCameraTick = -1;
            if (sourceInput != null) sourceInput.Clear();
            if (SourceSelection != null) SourceSelection.Clear();
            if (field != null) field.ResetRenderHistory();
            waitingForRelease = true;
            hud.ClearNotices();
            if (IsSourceProfile)
            {
                hud.OnSourceHostStep(State);
                playCamera.ObserveSourceHostStep(State);
                ForwardInitialSourcePresentationCues();
            }
            playCamera.Render(State, true);
        }

        private void ReadActionEdges()
        {
            GamePhase phase = State.Phase;
            bool one = Pressed(KeyCode.Alpha1, KeyCode.Keypad1);
            bool two = Pressed(KeyCode.Alpha2, KeyCode.Keypad2);
            if (phase == GamePhase.PlaySelect)
            {
                if (one || two)
                    SelectPlay(one ? 1 : 2);
            }
            else if (phase == GamePhase.LiveBall && State.Possession == TeamId.Home)
            {
                if (one || two || Pressed(KeyCode.Alpha3, KeyCode.Keypad3))
                    QueueAction(InputAction.Pass, targetPlayerId: one ? 4 : two ? 5 : 6);
            }

            if (Input.GetKeyDown(KeyCode.Space))
                PrimaryAction();
            if (Input.GetKeyDown(KeyCode.F))
                QueueAction(InputAction.Tackle);
            if (Input.GetKeyDown(KeyCode.Q))
                QueueAction(InputAction.SwitchDefender);
        }

        private void OnGameEvent(GameEvent gameEvent)
        {
            hud.OnGameEvent(gameEvent);
        }

        private void OnSourceGameEvent(SourceGameEvent value)
        {
            hud.OnSourceGameEvent(value);
            Action<SourceGameEvent> handler = SourceEventRaised;
            if (handler != null) handler(value);
        }

        private void OnSourcePresentationCue(SourcePresentationCue cue)
        {
            Action<SourcePresentationCue> handler = SourcePresentationCueRaised;
            if (handler != null) handler(cue);
        }

        private void ForwardInitialSourcePresentationCues()
        {
            SourcePresentationCueBatch batch = simulation.PresentationCues;
            if (batch == null)
                throw new SourceInterfaceException("Initial source presentation cue availability is unknown.");
            foreach (SourcePresentationCue cue in batch.Cues)
                OnSourcePresentationCue(cue);
        }

        private void OnBeforeHostStep(IGameState state)
        {
            Action<IGameState> handler = BeforeHostStep;
            if (handler != null) handler(state);
        }

        private void OnAfterHostStep(IGameState state)
        {
            if (IsSourceProfile)
            {
                var source = (ISourceGameState)state;
                bool cameraAwake = simulation.SourceCameraStageOrder >= 0;
                if (cameraAwake && source.SourceTick > 0 && (lastSourceCameraTick != source.SourceTick ||
                    simulation.SourceCameraHeadingSampleTick != source.SourceTick ||
                    simulation.SourceCameraHeading != lastSourceCameraHeading))
                    throw new SourceInterfaceException("Missing/stale exact source-camera-stage heading at source tick " +
                        source.SourceTick + ". Register the verified source-stage producer; no interpolated transform/default-zero AI heading is accepted.");
                hud.OnSourceHostStep(state);
            }
            Action<IGameState> handler = AfterHostStep;
            if (handler != null) handler(state);
        }

        public void RegisterSourceCameraTickProducer(Func<IGameState, long, int> producer)
        {
            if (!IsSourceProfile || producer == null || sourceCameraProducer != null)
                throw new SourceInterfaceException("Register one actual source-camera-stage producer for the original profile.");
            sourceCameraProducer = producer;
        }

        public int SampleSourceCameraHeading(IGameState preActorState, long executingSourceTick)
        {
            if (sourceCameraProducer == null)
                throw new SourceInterfaceException("No actual source camera producer is registered for the executing source stage.");
            int heading = sourceCameraProducer(preActorState, executingSourceTick);
            if (heading < 0 || heading > 1023 || executingSourceTick <= lastSourceCameraTick)
                throw new SourceInterfaceException("Original camera stage requires one ordered finite 1024-heading sample per source wake.");
            lastSourceCameraTick = executingSourceTick; lastSourceCameraHeading = heading;
            return heading;
        }

        public void PulseSourceButton(int stationId, SourceButtons button)
        {
            if (sourceInput != null && !InputSuspended && sourceFailure == null)
                sourceInput.Pulse(stationId, button);
        }

        internal void ReadSourceKeyboardEvent(Event value)
        {
            if (sourceInput != null && !InputSuspended && sourceFailure == null) sourceInput.KeyboardEvent(value);
        }

        private void OnApplicationFocus(bool value)
        {
            focused = value;
            ResetFocusInput();
        }

        private void OnApplicationPause(bool value)
        {
            paused = value;
            ResetFocusInput();
        }

        private void ResetFocusInput()
        {
            waitingForRelease = true;
            if (sourceInput != null) sourceInput.Clear();
            if (SourceSelection != null) SourceSelection.ClearPendingInput();
            if (bridge != null)
                bridge.SetActive(!InputSuspended);
            if (field != null)
                field.ResetRenderHistory();
        }

        private void OnDisable()
        {
            ResetFocusInput();
        }

        private void OnEnable()
        {
            if (bridge != null)
                bridge.SetActive(!InputSuspended);
        }

        private void OnDestroy()
        {
            if (IsSourceProfile && playCamera != null)
            {
                SourcePresentationCueRaised -= playCamera.ConsumeSourceCue;
                AfterHostStep -= playCamera.ObserveSourceHostStep;
            }
            if (bridge != null)
            {
                bridge.EventRaised -= OnGameEvent;
                bridge.SourceEventRaised -= OnSourceGameEvent;
                bridge.SourcePresentationCueRaised -= OnSourcePresentationCue;
                bridge.BeforeHostStep -= OnBeforeHostStep;
                bridge.AfterHostStep -= OnAfterHostStep;
            }
            if (field != null)
                field.Dispose();
        }

        private static bool ExplicitGraybox(string url)
        {
            if (string.IsNullOrEmpty(url)) return false;
            int start = url.IndexOf('?');
            if (start < 0) return false;
            foreach (string pair in url.Substring(start + 1).Split('#')[0].Split('&'))
                if (pair == "actors=graybox") return true;
            return false;
        }

        private static float Axis(KeyCode positive, KeyCode positiveArrow, KeyCode negative, KeyCode negativeArrow)
        {
            return (Input.GetKey(positive) || Input.GetKey(positiveArrow) ? 1f : 0f)
                - (Input.GetKey(negative) || Input.GetKey(negativeArrow) ? 1f : 0f);
        }

        private static bool Pressed(KeyCode key, KeyCode keypad)
        {
            return Input.GetKeyDown(key) || Input.GetKeyDown(keypad);
        }

        private static bool AnyGameKeyHeld()
        {
            return Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.S)
                || Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.UpArrow) || Input.GetKey(KeyCode.DownArrow)
                || Input.GetKey(KeyCode.LeftArrow) || Input.GetKey(KeyCode.RightArrow)
                || Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)
                || Input.GetKey(KeyCode.Alpha1) || Input.GetKey(KeyCode.Alpha2) || Input.GetKey(KeyCode.Alpha3)
                || Input.GetKey(KeyCode.Keypad1) || Input.GetKey(KeyCode.Keypad2) || Input.GetKey(KeyCode.Keypad3)
                || Input.GetKey(KeyCode.Space) || Input.GetKey(KeyCode.F) || Input.GetKey(KeyCode.Q)
                || Input.GetKey(KeyCode.R);
        }
    }
}
