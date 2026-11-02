using System;
using System.Collections.Generic;
using Blitz.Contracts;
using Blitz.Gameplay;
using Blitz.Presentation;

namespace Blitz.Runtime
{
    public interface ISourceInstructionSoundBatchBinding
    {
        SourceSoundVmBatch Resolve(SourceInstructionSound sound, SourceInstructionSoundContext context);
    }

    public interface ISourceKickoffInstructionSoundBatchBinding
    {
        SourceSoundVmBatch Resolve(SourceKickoffInstructionChildSound sound);
    }

    public sealed class SourceInstructionSoundVmOutput : ISourceInstructionSoundPlayer
    {
        private readonly SourceSoundVmTransport transport;
        private readonly ISourceInstructionSoundBatchBinding binding;
        public SourceInstructionSoundVmOutput(SourceSoundVmTransport transport, ISourceInstructionSoundBatchBinding binding)
        {
            this.transport = transport ?? throw new ArgumentNullException(nameof(transport));
            this.binding = binding ?? throw new ArgumentNullException(nameof(binding));
        }
        public void Play(SourceInstructionSound sound, SourceInstructionSoundContext context)
        {
            DrainResolved(transport, binding.Resolve(sound, context));
        }
        internal static void DrainResolved(SourceSoundVmTransport transport, SourceSoundVmBatch batch)
        {
            if (batch == null || batch.Dispatches.Count != 1 || batch.Dispatches[0].HasOriginalAnimationCue
                || batch.Dispatches[0].Status != SourceSoundVmStatus.Resolved)
                throw new SourceInstructionPresentationException(SourceInstructionPresentationError.UnsupportedNativeBinding,
                    "Instruction sound requires one actually resolved native-process output, not an animation cue or placeholder.");
            transport.Drain(batch);
            transport.FlushNativePrefix();
        }
    }

    public sealed class SourceKickoffInstructionSoundVmOutput : ISourceKickoffInstructionSoundPlayer
    {
        private readonly SourceSoundVmTransport transport;
        private readonly ISourceKickoffInstructionSoundBatchBinding binding;
        public SourceKickoffInstructionSoundVmOutput(SourceSoundVmTransport transport, ISourceKickoffInstructionSoundBatchBinding binding)
        {
            this.transport = transport ?? throw new ArgumentNullException(nameof(transport));
            this.binding = binding ?? throw new ArgumentNullException(nameof(binding));
        }
        public void Play(SourceKickoffInstructionChildSound sound)
        { SourceInstructionSoundVmOutput.DrainResolved(transport, binding.Resolve(sound)); }
    }

    public sealed class SourceInstructionGameBinding : ISourceInstructionAdmissionProgramBinding
    {
        private readonly GameSimulation simulation;
        public SourceInstructionGameBinding(GameSimulation simulation)
        { this.simulation = simulation ?? throw new ArgumentNullException(nameof(simulation)); }
        public SourceInstructionSnapshot Current { get { return simulation.SourceInstructionProgram; } }
        public IReadOnlyList<SourcePossessionProcessRecord> Records { get { return simulation.SourcePossessionProcessRecords; } }
        public long Invoke(SourceInstructionInvocation invocation) { return simulation.InvokeSourceInstructions(invocation); }
        public void BindSourceInstructionAdmission(SourceInstructionAdmissionBinding binding)
        { simulation.BindSourceInstructionAdmission(binding); }
    }

    public sealed class SourceInstructionRuntime : IDisposable
    {
        private readonly GameSimulation simulation;
        private readonly SourceInstructionHudRenderer renderer;
        private readonly SourceInstructionConsumer consumer;
        private readonly SourceInstructionHudResources resources;
        private readonly SourceSoundVmTransport soundTransport;
        private SourceKickoffInstructionPresentation kickoffPresentation;
        public SourceInstructionConsumer Consumer { get { return consumer; } }
        public SourceKickoffInstructionPresentation KickoffPresentation { get { return kickoffPresentation; } }
        public int? IdiotBoxesShown { get { return consumer.IdiotBoxesShown; } }

        public SourceInstructionRuntime(GameSimulation simulation, SourceHudAsset actualHud,
            SourceInstructionHudResources resources, ISourceInstructionSoundBatchBinding sounds,
            SourceSoundVmTransport soundTransport, ISourceInstructionObjectFlashBinding objectFlash)
        {
            if (resources == null || !ReferenceEquals(resources.Asset, actualHud))
                throw new SourceInstructionPresentationException(SourceInstructionPresentationError.MissingResource,
                    "Instruction resources must use this runtime's actual original HUD asset.");
            this.simulation = simulation ?? throw new ArgumentNullException(nameof(simulation));
            this.resources = resources; this.soundTransport = soundTransport;
            consumer = new SourceInstructionConsumer(new SourceInstructionGameBinding(simulation), resources, objectFlash,
                new SourceInstructionSoundVmOutput(soundTransport, sounds));
            renderer = new SourceInstructionHudRenderer(consumer.Presentation, resources);
        }
        public void BindSourceKickoffInstructionOwnedHelpers(int allAbcdMask,
            SourceInstructionProcessIds processIds, ISourceInstructionInputs inputs,
            ISourceInstructionAdmissionState state, int childProcessId, int maximumPlayers,
            int buttonAMask, int buttonBMask, ISourceKickoffInstructionChildInputs childInputs,
            ISourceKickoffInstructionPlayerButtons buttons, SourceKickoffInstructionChildOperands operands,
            ISourceKickoffInstructionChildEffects effects, SourceKickoffInstructionOwnedHelpers helpers,
            ISourceKickoffInstructionChildReturnBinding returns)
        {
            if (simulation.SourceTick != 0)
                throw new SourceInstructionPresentationException(SourceInstructionPresentationError.InvalidLifecycle,
                    "Owned kickoff admission must bind before the initialized simulation's first source tick.");
            consumer.BindSourceKickoffInstructionOwnedHelpers(allAbcdMask,
                processIds, inputs, state, childProcessId, maximumPlayers, buttonAMask, buttonBMask,
                childInputs, buttons, operands, effects, helpers, returns);
        }
        public void BindSourceKickoffInstructionHud(int allAbcdMask,
            SourceInstructionProcessIds processIds, ISourceInstructionInputs inputs,
            ISourceInstructionAdmissionState state, int childProcessId, int maximumPlayers,
            int buttonAMask, int buttonBMask, ISourceKickoffInstructionChildInputs childInputs,
            ISourceKickoffInstructionPlayerButtons buttons, SourceKickoffInstructionChildOperands operands,
            SourceKickoffInstructionHudBindings bindings, ISourceKickoffInstructionSoundBatchBinding sounds,
            SourceKickoffInstructionOwnedHelpers helpers, ISourceKickoffInstructionChildReturnBinding returns)
        {
            if (simulation.SourceTick != 0 || consumer.HasPreparedSourceAdmission || consumer.HasInvocation
                || simulation.SourceInstructionProgram != null || consumer.Presentation.Failure != null || kickoffPresentation != null)
                throw new SourceInstructionPresentationException(SourceInstructionPresentationError.InvalidLifecycle,
                    "Bind one explicit kickoff HUD on the fresh source admission path, not over manual/external/faulted instructions.");
            var candidate = new SourceKickoffInstructionPresentation(resources.BindKickoff(bindings),
                new SourceKickoffInstructionSoundVmOutput(soundTransport, sounds));
            try
            {
                consumer.BindSourceKickoffInstructionOwnedHelpers(allAbcdMask, processIds, inputs, state,
                    childProcessId, maximumPlayers, buttonAMask, buttonBMask, childInputs, buttons,
                    operands, candidate, helpers, returns);
                kickoffPresentation = candidate;
            }
            catch
            {
                // A failed initialization is pending in Consumer and the actual
                // Game binding. Its identical effects owner must survive retry.
                if (consumer.HasPreparedSourceAdmission) kickoffPresentation = candidate;
                else candidate.Dispose();
                throw;
            }
        }
        public long Invoke(int allAbcdMask, SourceInstructionProcessIds processIds, ISourceInstructionInputs inputs)
        { return consumer.Invoke(allAbcdMask, processIds, inputs); }
        public void AfterSourceStep(IGameState state)
        {
            if (!consumer.HasInvocation && !consumer.HasPreparedSourceAdmission) return;
            var source = state as ISourceGameState;
            if (source == null) throw new SourceInstructionPresentationException(
                SourceInstructionPresentationError.InvalidLifecycle, "Instruction presentation requires actual source ticks.");
            consumer.AfterSourceStep(source.SourceTick);
        }
        public void Draw(SourceHudViewport viewport)
        {
            if (kickoffPresentation != null)
            {
                if (kickoffPresentation.Failure != null) throw kickoffPresentation.Failure;
                renderer.Draw(viewport, kickoffPresentation.Frame());
            }
            else if (consumer.HasInvocation) renderer.Draw(viewport);
        }
        public void OnSourceProgramReset()
        {
            try
            {
                consumer.OnSourceProgramReset();
                if (kickoffPresentation != null) kickoffPresentation.Reset();
            }
            catch (Exception error)
            {
                if (kickoffPresentation != null) kickoffPresentation.Abort(error);
                throw;
            }
        }
        public void Abort(Exception error)
        {
            consumer.Presentation.Abort(error);
            if (kickoffPresentation != null) kickoffPresentation.Abort(error);
        }
        public void Dispose()
        {
            try { consumer.Dispose(); }
            finally { if (kickoffPresentation != null) kickoffPresentation.Dispose(); }
        }
    }
}
