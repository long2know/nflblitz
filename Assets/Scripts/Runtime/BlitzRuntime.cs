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
            bridge = new FixedStepBridge(new Blitz.Gameplay.GameSimulation(1));
            field = new FieldScene(transform);
            playCamera = new PlayCamera(transform);
            hud = gameObject.AddComponent<PlayHud>();
            hud.Initialize(this);
            bridge.EventRaised += OnGameEvent;
            field.Render(bridge.State, 0f);
            playCamera.Render(bridge.State, true);
        }

        private void Update()
        {
            if (InputSuspended || bridge == null)
                return;
            if (waitingForRelease)
            {
                if (!AnyGameKeyHeld())
                    waitingForRelease = false;
                bridge.Advance(Time.unscaledDeltaTime, default(FieldVector), false);
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
            bridge.Advance(Time.unscaledDeltaTime, new FieldVector(x, z), turbo);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            IGameState state = bridge.State;
            runtimeStatus = "tick=" + state.Tick + " phase=" + state.Phase + " play=" + state.PlayNumber
                + " possession=" + state.Possession + " controlled=" + state.ControlledPlayerId;
#endif
        }

        private void LateUpdate()
        {
            if (bridge == null)
                return;
            field.Render(bridge.State, InputSuspended ? 0f : Time.unscaledDeltaTime);
            playCamera.Render(bridge.State, false);
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
