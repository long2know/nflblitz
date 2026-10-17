using System;
using System.Collections.Generic;
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
            IPendingSourceInput pending = simulation as IPendingSourceInput;
            this.clearSimulationInput = clearSimulationInput ?? (pending == null ? (Action)null : pending.ClearPendingInput);
            ISourceMatchState source = simulation.State as ISourceMatchState;
            if (source != null && source.Match != null && pending == null)
                throw new InvalidOperationException("The source profile requires typed IPendingSourceInput.ClearPendingInput().");
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
        public event Action<SourceGameEvent> SourceEventRaised;
        public event Action<SourcePresentationCue> SourcePresentationCueRaised;
        public event Action<IGameState> BeforeHostStep;
        public event Action<IGameState> AfterHostStep;
        public event Action AfterSimulationStep;
        public event Action AfterSourcePresentation;
        public event Action Restarted;

        public static Action BindSourceInputReset(IGameSimulation simulation)
        {
            if (simulation == null) throw new ArgumentNullException(nameof(simulation));
            IPendingSourceInput pending = simulation as IPendingSourceInput;
            if (pending == null)
                throw new InvalidOperationException("Source input lifecycle requires typed IPendingSourceInput.ClearPendingInput(); " +
                    simulation.GetType().FullName + " does not implement it. Private-buffer reflection is not supported.");
            return pending.ClearPendingInput;
        }

        public void QueueAction(InputAction action, PlayId play = PlayId.Run, int targetPlayerId = -1)
        {
            if (!active || actions.Count >= 8 || !AcceptsAction(State, action))
                return;
            actions.Enqueue(new PlayerCommand(action: action, play: play, targetPlayerId: targetPlayerId));
        }

        public int Advance(double elapsedSeconds, FieldVector move, bool turbo)
        {
            return AdvanceInternal(elapsedSeconds, move, turbo, null);
        }

        public int AdvanceSource(double elapsedSeconds, Func<SourceInputFrame> nextHostInput)
        {
            if (nextHostInput == null) throw new ArgumentNullException(nameof(nextHostInput));
            ISourceMatchState source = State as ISourceMatchState;
            if (source == null || source.Match == null)
                throw new InvalidOperationException("Raw source station input requires the explicit source-match profile.");
            return AdvanceInternal(elapsedSeconds, default(FieldVector), false, nextHostInput);
        }

        private int AdvanceInternal(double elapsedSeconds, FieldVector move, bool turbo, Func<SourceInputFrame> nextHostInput)
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
                Action<IGameState> before = BeforeHostStep;
                if (before != null) before(entry);
                SourceInputFrame input = nextHostInput == null ? null : nextHostInput();
                if (nextHostInput != null && input == null)
                    throw new InvalidOperationException("The source cabinet producer returned no input frame.");
                try
                {
                    simulation.Step(new PlayerCommand(move, turbo, edge.Action, edge.Play, edge.TargetPlayerId,
                        stationInputs: input));
                }
                finally
                {
                    Action drained = AfterSimulationStep;
                    if (drained != null) drained();
                }
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
                ISourceMatchSimulation sourceSimulation = simulation as ISourceMatchSimulation;
                if (sourceSimulation != null)
                {
                    IReadOnlyList<SourceGameEvent> sourceEvents = sourceSimulation.SourceEvents;
                    for (int i = 0; i < sourceEvents.Count; i++)
                    {
                        Action<SourceGameEvent> handler = SourceEventRaised;
                        if (handler != null) handler(sourceEvents[i]);
                    }
                }
                ISourcePresentationSimulation presentation = simulation as ISourcePresentationSimulation;
                if (presentation != null && presentation.PresentationCues != null)
                {
                    foreach (SourcePresentationCue cue in presentation.PresentationCues.Cues)
                    {
                        Action<SourcePresentationCue> handler = SourcePresentationCueRaised;
                        if (handler != null) handler(cue);
                    }
                }
                Action complete = AfterSourcePresentation;
                if (complete != null) complete();
                Action<IGameState> after = AfterHostStep;
                if (after != null) after(State);
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
            Action restarted = Restarted;
            if (restarted != null) restarted();
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
