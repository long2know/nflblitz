using System;
using System.Collections.Generic;
using System.Reflection;
using Blitz.Contracts;

namespace Blitz.Runtime
{
    public sealed class FixedStepBridge
    {
        public const double StepSeconds = 1d / SliceRules.TicksPerSecond;
        public const double MaximumAccumulatedSeconds = 0.1d;
        public const int MaximumStepsPerFrame = 6;

        private readonly IGameSimulation simulation;
        private readonly Action clearSimulationInput;
        private readonly Queue<PlayerCommand> actions = new Queue<PlayerCommand>();
        private double accumulator;
        private bool active = true;
        private bool discardNextFrame;
        private IGameState previousState;

        public FixedStepBridge(IGameSimulation simulation, Action clearSimulationInput = null)
        {
            if (simulation == null)
                throw new ArgumentNullException(nameof(simulation));
            this.simulation = simulation;
            this.clearSimulationInput = clearSimulationInput;
            previousState = simulation.State;
        }

        public IGameState State { get { return simulation.State; } }
        public IGameState PreviousState { get { return previousState; } }
        public float RenderInterpolation01
        {
            get
            {
                ISourceGameState source = State as ISourceGameState;
                if (source == null)
                    return (float)Math.Min(1d, accumulator / StepSeconds);
                if (source.SourceTicksPerSecond <= 0)
                    throw new InvalidOperationException("Source snapshots require a positive source tick rate.");
                double phase = (State.Tick % SliceRules.TicksPerSecond) * source.SourceTicksPerSecond %
                    SliceRules.TicksPerSecond / (double)SliceRules.TicksPerSecond;
                return (float)Math.Min(1d, phase + accumulator * source.SourceTicksPerSecond);
            }
        }
        public event Action<GameEvent> EventRaised;

        public static Action BindSourceInputReset(IGameSimulation simulation)
        {
            if (simulation == null) throw new ArgumentNullException(nameof(simulation));
            MethodInfo reset = simulation.GetType().GetMethod("ClearPendingInput", BindingFlags.Instance | BindingFlags.Public,
                null, Type.EmptyTypes, null);
            if (reset != null && reset.ReturnType == typeof(void))
                return (Action)Delegate.CreateDelegate(typeof(Action), simulation, reset);
            // The current kernel has no lifecycle interface. Bind only its exact transient command buffer,
            // never actor/clock/physics state; a renamed or incompatible buffer must fail visibly.
            FieldInfo field = simulation.GetType().GetField("pendingSourceActions", BindingFlags.Instance | BindingFlags.NonPublic);
            var pending = field == null ? null : field.GetValue(simulation) as Queue<PlayerCommand>;
            if (pending == null)
                throw new InvalidOperationException("Source input lifecycle requires the kernel's transient command-buffer reset; " +
                    simulation.GetType().FullName + " exposes neither ClearPendingInput() nor the expected command buffer. Stale input replay is disabled.");
            return pending.Clear;
        }

        public void QueueAction(InputAction action, PlayId play = PlayId.Run, int targetPlayerId = -1)
        {
            if (!active || actions.Count >= 8 || !AcceptsAction(State, action))
                return;
            actions.Enqueue(new PlayerCommand(action: action, play: play, targetPlayerId: targetPlayerId));
        }

        public int Advance(double elapsedSeconds, FieldVector move, bool turbo)
        {
            if (!active)
                return 0;
            if (discardNextFrame)
            {
                discardNextFrame = false;
                return 0;
            }
            if (double.IsNaN(elapsedSeconds) || double.IsInfinity(elapsedSeconds) || elapsedSeconds < 0d)
                elapsedSeconds = 0d;

            accumulator = Math.Min(MaximumAccumulatedSeconds, accumulator + elapsedSeconds);
            int steps = 0;
            while (steps < MaximumStepsPerFrame && accumulator + 1e-9d >= StepSeconds)
            {
                PlayerCommand edge = actions.Count > 0 ? actions.Dequeue() : default(PlayerCommand);
                IGameState entry = State;
                GamePhase entryPhase = State.Phase;
                simulation.Step(new PlayerCommand(move, turbo, edge.Action, edge.Play, edge.TargetPlayerId));
                ISourceGameState sourceBefore = entry as ISourceGameState;
                ISourceGameState sourceAfter = State as ISourceGameState;
                if (sourceAfter == null || sourceBefore == null || sourceAfter.SourceTick > sourceBefore.SourceTick)
                    previousState = entry;
                else if (sourceAfter.SourceTick < sourceBefore.SourceTick)
                    previousState = State;
                accumulator = Math.Max(0d, accumulator - StepSeconds);
                steps++;

                // Events belong to this tick only; the next Step replaces them.
                IReadOnlyList<GameEvent> events = simulation.Events;
                for (int i = 0; i < events.Count; i++)
                {
                    Action<GameEvent> handler = EventRaised;
                    if (handler != null)
                        handler(events[i]);
                }
                if (State.Phase != entryPhase)
                    actions.Clear();
            }
            return steps;
        }

        public void SetActive(bool value)
        {
            active = value;
            ClearTransientInput();
        }

        public void ClearTransientInput()
        {
            actions.Clear();
            accumulator = 0d;
            discardNextFrame = true;
            previousState = State;
            if (clearSimulationInput != null) clearSimulationInput();
        }

        public void Restart()
        {
            ClearTransientInput();
            simulation.Restart(1);
            previousState = State;
        }

        private static bool AcceptsAction(IGameState state, InputAction action)
        {
            switch (state.Phase)
            {
                case GamePhase.PlaySelect:
                    return action == InputAction.SelectPlay;
                case GamePhase.PreSnap:
                    return state.Possession == TeamId.Home && action == InputAction.Snap;
                case GamePhase.LiveBall:
                    return state.Possession == TeamId.Home
                        ? action == InputAction.Pass
                        : action == InputAction.Tackle || action == InputAction.SwitchDefender;
                case GamePhase.DeadBall:
                    return action == InputAction.Continue;
                default:
                    return false;
            }
        }
    }
}
