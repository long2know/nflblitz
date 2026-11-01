using System;
using Blitz.Contracts;

namespace Blitz.Gameplay
{
    public sealed partial class GameSimulation
    {
        private SourceInstructionDispatcher sourceInstructionDispatcher;
        private SourceInstructionAdmissionBinding sourceInstructionAdmission;
        private SourcePhysicalSwitchFrame sourcePhysicalPrior1, sourcePhysicalPrior2;
        public long SourceInputEpoch { get; private set; }
        public SourceConsumedPhysicalSwitchScan ConsumedSourceSwitches { get; private set; }
        public SourceInstructionSnapshot SourceInstructionProgram
        { get { return sourceInstructionDispatcher == null ? null : sourceInstructionDispatcher.LastSnapshot; } }

        private void ResetSourceConsumedSwitches()
        {
            SourceInputEpoch = checked(SourceInputEpoch + 1);
            ConsumedSourceSwitches = null;
            sourcePhysicalPrior1 = sourcePhysicalPrior2 = SourcePhysicalSwitchFrame.Neutral();
        }

        private void PublishSourceConsumedSwitches(SourceInputFrame frame)
        {
            SourcePhysicalSwitchFrame current = frame == null ? null : frame.PhysicalSwitches;
            if (current == null)
            {
                ConsumedSourceSwitches = null;
                sourcePhysicalPrior1 = sourcePhysicalPrior2 = SourcePhysicalSwitchFrame.Neutral();
                return;
            }
            ConsumedSourceSwitches = new SourceConsumedPhysicalSwitchScan(Model.SourceTick, SourceInputEpoch,
                current, sourcePhysicalPrior1, sourcePhysicalPrior2);
            sourcePhysicalPrior2 = sourcePhysicalPrior1;
            sourcePhysicalPrior1 = current;
        }

        // These return the already stamped source call only inside its executing
        // typed effect callback. They neither allocate ordinals nor replay sound.
        public SourceProcessSoundCall TakeExecutingSourceInstructionSound(SourceInstructionSound sound,
            SourceInstructionSoundContext context)
        {
            RequireExecutingSourceSoundProcess();
            if (sourceInstructionDispatcher == null)
                throw new InvalidOperationException("No source instruction dispatcher is bound.");
            return sourceInstructionDispatcher.TakeExecutingSound(sound, context);
        }

        public SourceProcessSoundCall TakeExecutingSourceKickoffInstructionSound(SourceKickoffInstructionChildSound sound)
        {
            RequireExecutingSourceSoundProcess();
            if (sourceInstructionDispatcher == null)
                throw new InvalidOperationException("No source instruction dispatcher is bound.");
            return sourceInstructionDispatcher.TakeExecutingKickoffSound(sound);
        }

        private SourceProcessSoundCall EmitSourceInstructionSound(SourceInstructionSound sound,
            SourceInstructionSoundContext context)
        {
            sourceInstructionDispatcher.RequireExecutingSound(sound, context);
            if (ReferenceEquals(sound, SourceInstructionSound.Tick))
                return EmitSourceProcessSound(SourceProcessSoundKind.InstructionTick, "tick_sndn",
                    Model.SourceCurrentCursorBank, 17, 115, 127, 1);
            if (!ReferenceEquals(sound, SourceInstructionSound.PageAdvance))
                throw new NotSupportedException("Unknown source instruction sound operand.");
            return EmitSourceProcessSound(SourceProcessSoundKind.InstructionPageAdvance, "RICOCHET_SND",
                Model.SourceCurrentPlayerBank, 148, 160, 127, 0);
        }

        private SourceProcessSoundCall EmitSourceKickoffInstructionSound(SourceKickoffInstructionChildSound sound)
        {
            sourceInstructionDispatcher.RequireExecutingKickoffSound(sound);
            if (!ReferenceEquals(sound, SourceKickoffInstructionChildSound.Ricochet))
                throw new NotSupportedException("Unknown source kickoff instruction sound operand.");
            return EmitSourceProcessSound(SourceProcessSoundKind.KickoffInstruction, "RICOCHET_SND",
                Model.SourceCurrentPlayerBank, 148, 115, 127, 1);
        }

        // Constructors remain unbound. Bind before the first source dispatch,
        // using one coherent flag owner and an explicit kickoff implementation.
        public void BindSourceInstructionAdmission(SourceInstructionAdmissionBinding binding)
        {
            if (binding == null) throw new ArgumentNullException(nameof(binding));
            if (!Model.SourceMode || Model.SourceTick != 0 || sourceInstructionDispatcher == null
                || sourceInstructionAdmission != null || Model.SourceAnimationFailure != null)
                throw new InvalidOperationException("Bind instruction admission once on a fresh initialized source simulation.");
            try
            {
                InitializeSourceInstructionAdmission(binding);
                sourceInstructionAdmission = binding;
            }
            catch (Exception error) { FailSourceInstructionAdmission(error); throw; }
        }

        public void BindSourceKickoffInstructionCoordinator(SourceInstructionInvocation invocation,
            ISourceInstructionAdmissionState state, SourceKickoffInstructionCoordinator coordinator)
        {
            BindSourceInstructionAdmission(
                SourceInstructionAdmissionBinding.WithKickoffCoordinator(invocation, state, coordinator));
        }

        public void BindSourceKickoffInstructionOwnedHelpers(SourceInstructionInvocation invocation,
            ISourceInstructionAdmissionState state, int childProcessId, int maximumPlayers,
            int buttonAMask, int buttonBMask, ISourceKickoffInstructionChildInputs inputs,
            ISourceKickoffInstructionPlayerButtons buttons, SourceKickoffInstructionChildOperands operands,
            ISourceKickoffInstructionChildEffects effects, SourceKickoffInstructionOwnedHelpers helpers,
            ISourceKickoffInstructionChildReturnBinding returns)
        {
            BindSourceKickoffInstructionCoordinator(invocation, state,
                SourceKickoffInstructionCoordinator.WithSourceChild(childProcessId, maximumPlayers,
                    buttonAMask, buttonBMask, inputs, buttons,
                    SourceKickoffInstructionChildBinding.WithOwnedHelpers(operands, effects, helpers, returns)));
        }

        public long InvokeSourceInstructions(SourceInstructionInvocation invocation)
        {
            if (!Model.SourceMode || sourceInstructionDispatcher == null)
                throw new InvalidOperationException("Instruction admission requires an initialized source process host.");
            if (sourceInstructionAdmission != null && invocation != null
                && !ReferenceEquals(invocation.Effects, sourceInstructionAdmission.State))
                throw new ArgumentException("Bound instruction programs require the same coherent flag owner.", nameof(invocation));
            long serial = checked(sourceFireNextSerial + 1);
            sourceInstructionDispatcher.Admit(serial, invocation);
            sourceFireNextSerial = serial;
            return serial;
        }

        private void InitializeSourceInstructionAdmission()
        {
            ResetSourcePlaySelectionIntroduction();
            if (sourceInstructionAdmission == null) return;
            try { InitializeSourceInstructionAdmission(sourceInstructionAdmission); }
            catch (Exception error) { FailSourceInstructionAdmission(error); throw; }
        }

        private void InitializeSourceInstructionAdmission(SourceInstructionAdmissionBinding binding)
        {
            sourceInstructionDispatcher.InitializeAdmission(binding,
                () => checked(sourceFireNextSerial + 1), serial => sourceFireNextSerial = serial);
        }

        private void AdmitSourceInstructionsAtSelectionReturn(SourcePlayType enteringPlayType)
        {
            if (sourceInstructionAdmission == null) return;
            try
            {
                long? serial = sourceInstructionDispatcher.AdmitAtPlaySelectionReturn(sourceInstructionAdmission,
                    enteringPlayType != SourcePlayType.ExtraPoint && enteringPlayType != SourcePlayType.Kickoff,
                    GameProcess, () => checked(sourceFireNextSerial + 1));
                if (serial.HasValue) sourceFireNextSerial = serial.Value;
            }
            catch (Exception error) { FailSourceInstructionAdmission(error); throw; }
        }

        private void AdmitSourceKickoffInstructionProducer(ActorState actor)
        {
            if (sourceInstructionAdmission == null) return;
            try
            {
                long? serial = sourceInstructionDispatcher.AdmitKickoffFromCatch(sourceInstructionAdmission,
                    Model.BallKind == SourceBallKind.Kickoff, 2 + actor.Id,
                    () => checked(sourceFireNextSerial + 1));
                if (serial.HasValue) sourceFireNextSerial = serial.Value;
            }
            catch (Exception error) { FailSourceInstructionAdmission(error); throw; }
        }

        private void FailSourceInstructionAdmission(Exception error)
        { Model.SourceAnimationFailure = "Source instructional admission: " + error.Message; }

        private static bool IsSourceMessageFunction(string function)
        {
            return function == "show_message_proc" || function == "flash_obj_proc"
                || function == "sacked_sp_proc" || function == "tackled_sp_proc" || function == "tackled2_sp_proc"
                || function == SourceInstructionDispatcher.Function
                || function == SourceInstructionDispatcher.KickoffFunction
                || function == SourceInstructionDispatcher.KickoffChildFunction
                || function == SourceInstructionDispatcher.KickoffFlashFunction;
        }
    }
}
