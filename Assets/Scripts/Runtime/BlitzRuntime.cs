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
        private bool focused = true;
        private bool paused;
        private bool waitingForRelease;
        private string sourceFailure;
        private uint evidenceSerial;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        [SerializeField] private string runtimeStatus;
#endif

        public IGameState State { get { return bridge == null ? null : bridge.State; } }
        public bool InputSuspended { get { return !focused || paused; } }
        public Camera GameCamera { get { return playCamera == null ? null : playCamera.Camera; } }

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
                ISourceAnimationLibrary animations = SourcePlayerProvider.ResolveRequiredAnimationLibrary(prefab);
                var simulation = animations == null ? new Blitz.Gameplay.GameSimulation(1) :
                    new Blitz.Gameplay.GameSimulation(animations, 1);
                bridge = new FixedStepBridge(simulation, animations == null ? null : FixedStepBridge.BindSourceInputReset(simulation));
                field = new FieldScene(transform, prefab, animations);
                playCamera = new PlayCamera(transform);
                hud = gameObject.AddComponent<PlayHud>();
                hud.Initialize(this);
                bridge.EventRaised += OnGameEvent;
                field.Render(bridge.State, bridge.State, 1f, 0f);
                playCamera.Render(bridge.State, true);
            }
            catch (PlayerAssetException e) { FailSourceMode(e); }
            catch (InvalidOperationException e) { FailSourceMode(e); }
            catch (NotSupportedException e) { FailSourceMode(e); }
        }

        private void Update()
        {
            if (InputSuspended || bridge == null || sourceFailure != null)
                return;
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

        private void FailSourceMode(Exception error)
        {
            sourceFailure = error.Message;
            if (bridge != null) bridge.SetActive(false);
            Debug.LogError("SOURCE ANIMATION ERROR: " + sourceFailure);
        }

        private void OnGUI()
        {
            if (sourceFailure == null) return;
            int depth = GUI.depth;
            GUI.depth = -10000;
            GUI.color = Color.white;
            var errorStyle = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft, wordWrap = true, fontSize = 16 };
            GUI.Box(new Rect(12, 12, Mathf.Max(280, Screen.width - 24), Mathf.Min(260, Screen.height - 24)),
                "SOURCE ANIMATION ERROR\n" + sourceFailure + "\nNo static/graybox substitute was created.", errorStyle);
            GUI.depth = depth;
        }

        internal void SelectPlay(int choice)
        {
            if (bridge == null || State.Phase != GamePhase.PlaySelect)
                return;
            PlayId play = State.Possession == TeamId.Home
                ? (choice == 1 ? PlayId.Run : PlayId.QuickPass)
                : (choice == 1 ? PlayId.Cover : PlayId.Blitz);
            QueueAction(InputAction.SelectPlay, play);
        }

        internal void PrimaryAction()
        {
            if (State == null)
                return;
            if (State.Phase == GamePhase.PreSnap)
                QueueAction(InputAction.Snap);
            else if (State.Phase == GamePhase.DeadBall)
                QueueAction(InputAction.Continue);
        }

        internal void QueueAction(InputAction action, PlayId play = PlayId.Run, int targetPlayerId = -1)
        {
            if (!InputSuspended && !waitingForRelease && bridge != null)
                bridge.QueueAction(action, play, targetPlayerId);
        }

        internal void RestartMatch()
        {
            if (bridge == null)
                return;
            bridge.Restart();
            if (field != null) field.ResetRenderHistory();
            waitingForRelease = true;
            hud.ClearNotices();
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
            if (bridge != null)
                bridge.SetActive(!InputSuspended);
            if (field != null)
                field.ResetRenderHistory();
        }

        private void OnDisable()
        {
            if (bridge != null)
                bridge.SetActive(false);
        }

        private void OnEnable()
        {
            if (bridge != null)
                bridge.SetActive(!InputSuspended);
        }

        private void OnDestroy()
        {
            if (bridge != null)
                bridge.EventRaised -= OnGameEvent;
            if (field != null)
                field.Dispose();
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
