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
        private readonly Queue<PlayerCommand> actions = new Queue<PlayerCommand>();
        private double accumulator;
        private bool active = true;
        private bool discardNextFrame;

        public FixedStepBridge(IGameSimulation simulation)
        {
            if (simulation == null)
                throw new ArgumentNullException(nameof(simulation));
            this.simulation = simulation;
        }

        public IGameState State { get { return simulation.State; } }
        public event Action<GameEvent> EventRaised;

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
                GamePhase entryPhase = State.Phase;
                simulation.Step(new PlayerCommand(move, turbo, edge.Action, edge.Play, edge.TargetPlayerId));
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
        }

        public void Restart()
        {
            ClearTransientInput();
            simulation.Restart(1);
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
