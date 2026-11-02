using System;
using Blitz.Contracts;
using Blitz.Presentation;

namespace Blitz.Runtime
{
    public interface ISourceInstructionAdmissionProgramBinding : ISourceInstructionProgramBinding
    {
        void BindSourceInstructionAdmission(SourceInstructionAdmissionBinding binding);
    }

    public sealed class SourceInstructionConsumer : IDisposable
    {
        private readonly ISourceInstructionProgramBinding program;
        private readonly ISourceInstructionAdmissionProgramBinding admissionProgram;
        private SourceInstructionAdmissionBinding admission;
        private long serial;
        private bool admissionReady, fallthroughObserved, disposed;
        public SourceInstructionPresentation Presentation { get; }
        public bool HasInvocation { get { return serial != 0; } }
        public bool HasSourceAdmission { get { return admissionReady; } }
        internal bool HasPreparedSourceAdmission { get { return admission != null; } }
        public int? IdiotBoxesShown
        { get { return admission == null ? Presentation.IdiotBoxesShown : admission.State.ReadIdiotBoxesShown(); } }
        public SourceInstructionSnapshot Program { get { return HasInvocation ? program.Current : null; } }

        public SourceInstructionConsumer(ISourceInstructionProgramBinding program, ISourceInstructionResources resources,
            ISourceInstructionObjectFlashBinding objectFlash, ISourceInstructionSoundPlayer sounds)
        {
            this.program = program ?? throw new ArgumentNullException(nameof(program));
            admissionProgram = program as ISourceInstructionAdmissionProgramBinding;
            Presentation = new SourceInstructionPresentation(resources, objectFlash, sounds, SynchronizeEffect);
        }

        public void BindSourceKickoffInstructionOwnedHelpers(int allAbcdMask,
            SourceInstructionProcessIds processIds, ISourceInstructionInputs inputs,
            ISourceInstructionAdmissionState state, int childProcessId, int maximumPlayers,
            int buttonAMask, int buttonBMask, ISourceKickoffInstructionChildInputs childInputs,
            ISourceKickoffInstructionPlayerButtons buttons, SourceKickoffInstructionChildOperands operands,
            ISourceKickoffInstructionChildEffects effects, SourceKickoffInstructionOwnedHelpers helpers,
            ISourceKickoffInstructionChildReturnBinding returns)
        {
            RequireAlive();
            if (admission != null || HasInvocation || program.Current != null || Presentation.Failure != null)
                throw Lifecycle("Bind owned kickoff admission once before any instruction invocation.");
            if (admissionProgram == null)
                throw Lifecycle("Owned kickoff admission requires the actual program's admission binding.");
            var owner = new AdmissionEffects(this, state);
            var invocation = new SourceInstructionInvocation(allAbcdMask, processIds, inputs, owner);
            var coordinator = SourceKickoffInstructionCoordinator.WithSourceChild(childProcessId, maximumPlayers,
                buttonAMask, buttonBMask, childInputs, buttons,
                SourceKickoffInstructionChildBinding.WithOwnedHelpers(operands, effects, helpers, returns));
            admission = SourceInstructionAdmissionBinding.WithKickoffCoordinator(invocation, owner, coordinator);
            InitializeSourceAdmission();
        }

        private void InitializeSourceAdmission()
        {
            try
            {
                admissionProgram.BindSourceInstructionAdmission(admission);
                admissionReady = true;
            }
            catch (Exception error) { Presentation.Abort(error); throw; }
        }

        public long Invoke(int allAbcdMask, SourceInstructionProcessIds processIds, ISourceInstructionInputs inputs)
        {
            RequireAlive();
            if (admission != null)
                throw Lifecycle("Owned kickoff admission uses the real catch/play-selection producers, not manual invocation.");
            if (Presentation.Failure != null || (HasInvocation && (Program == null
                || Program.Stage != SourceInstructionStage.Fallthrough)))
                throw new SourceInstructionPresentationException(SourceInstructionPresentationError.InvalidLifecycle,
                    "Concurrent/faulted instruction resources require an actual source-program reset.");
            long admitted = program.Invoke(new SourceInstructionInvocation(allAbcdMask, processIds, inputs, Presentation));
            Presentation.BeginInvocation(admitted);
            serial = admitted;
            return serial;
        }

        private void SynchronizeEffect()
        {
            RequireAlive();
            SourceInstructionSnapshot current = ObserveProgram();
            if (!HasInvocation || current == null || current.Process.Serial != serial)
                throw new SourceInstructionPresentationException(SourceInstructionPresentationError.InvalidLifecycle,
                    "Presentation effects must belong to the actual admitted instruction process.");
            Presentation.Synchronize(current.Process.LastWakeTick, program.Records, false);
        }

        public void AfterSourceStep(long sourceTick)
        {
            RequireAlive();
            RequireAdmissionReady();
            SourceInstructionSnapshot current;
            try { current = ObserveProgram(); }
            catch (Exception error) { Presentation.Abort(error); throw; }
            if (!HasInvocation) return;
            if (current == null || current.Process.Serial != serial || current.Stage == SourceInstructionStage.Faulted)
            {
                var error = Presentation.Failure ?? new SourceInstructionPresentationException(
                    SourceInstructionPresentationError.InvalidLifecycle,
                    "The instruction producer is missing or faulted; it cannot qualify presentation progress.");
                Presentation.Abort(error);
                throw error;
            }
            Presentation.Synchronize(sourceTick, program.Records, true);
            fallthroughObserved = current.Stage == SourceInstructionStage.Fallthrough;
        }

        private SourceInstructionSnapshot ObserveProgram()
        {
            RequireAdmissionReady();
            if (admissionReady && Presentation.Failure != null) throw Presentation.Failure;
            SourceInstructionSnapshot current = program.Current;
            if (admissionReady && current != null && current.Process.Serial != serial)
            {
                if ((HasInvocation && !fallthroughObserved) || current.Stage == SourceInstructionStage.Faulted)
                    throw Lifecycle("Automatic instruction presentation cannot replace an unfinished or faulted source process.");
                Presentation.BeginInvocation(current.Process.Serial);
                serial = current.Process.Serial;
                fallthroughObserved = false;
            }
            return current;
        }

        private void RequireAdmissionReady()
        {
            if (admission != null && !admissionReady)
                throw Lifecycle("Source admission did not finish initialization; no instruction dispatch is admitted.");
        }

        // The runtime calls this only from its existing real simulation-restart notification.
        public void OnSourceProgramReset()
        {
            RequireAlive();
            if (admission != null && program.Current != null)
                throw Lifecycle("Reset notification requires the actual restarted source program.");
            Presentation.Reset(); serial = 0;
            fallthroughObserved = false;
            // Game retains successful bindings, but a failed initial bind was never stored there.
            if (admission != null && !admissionReady) InitializeSourceAdmission();
        }
        public void Dispose()
        {
            if (disposed) return;
            Presentation.Dispose(); serial = 0; disposed = true;
        }
        private void RequireAlive()
        { if (disposed) throw new ObjectDisposedException(nameof(SourceInstructionConsumer)); }
        private static SourceInstructionPresentationException Lifecycle(string message)
        { return new SourceInstructionPresentationException(SourceInstructionPresentationError.InvalidLifecycle, message); }

        // One forwarding owner shares the caller's live admission flag with the
        // existing four-page presentation, which only accepts its own -1/2 writes.
        private sealed class AdmissionEffects : ISourceInstructionAdmissionState, ISourceInstructionEffects
        {
            private readonly SourceInstructionConsumer owner;
            private readonly ISourceInstructionAdmissionState state;
            private SourceInstructionPresentation Presentation { get { return owner.Presentation; } }
            internal AdmissionEffects(SourceInstructionConsumer owner, ISourceInstructionAdmissionState state)
            { this.owner = owner; this.state = state ?? throw new ArgumentNullException(nameof(state)); }
            public int ReadIdiotBoxesShown() { return state.ReadIdiotBoxesShown(); }
            public int ReadPlayerStatus() { return state.ReadPlayerStatus(); }
            public int ReadGamesPlayed(int slot) { return state.ReadGamesPlayed(slot); }
            public void WriteIdiotBoxesShown(int value)
            {
                state.WriteIdiotBoxesShown(value);
                // Initialization (including restart) has no instructions_proc.
                // The kickoff coordinator's 1 belongs to admission, not a page.
                if (value == -1 || value == 2 && owner.program.Current != null)
                    Presentation.WriteIdiotBoxesShown(value);
            }
            public ISourceInstructionPlate BeginPlate(string image, string texture, SourceInstructionPosition position)
            { return Presentation.BeginPlate(image, texture, position); }
            public void SetPlateWidthScale(ISourceInstructionPlate plate, float scale)
            { Presentation.SetPlateWidthScale(plate, scale); }
            public void SetPlateHeightScale(ISourceInstructionPlate plate, float scale)
            { Presentation.SetPlateHeightScale(plate, scale); }
            public void SetPlateId(ISourceInstructionPlate plate, string symbol)
            { Presentation.SetPlateId(plate, symbol); }
            public void GeneratePlateVertices(ISourceInstructionPlate plate)
            { Presentation.GeneratePlateVertices(plate); }
            public void SetTextFont(SourceInstructionFont font) { Presentation.SetTextFont(font); }
            public void SetStringId(int id) { Presentation.SetStringId(id); }
            public void SetTextPosition(SourceInstructionPosition position) { Presentation.SetTextPosition(position); }
            public void SetTextColor(SourceInstructionColor color) { Presentation.SetTextColor(color); }
            public ISourceInstructionText Print(SourceInstructionTextRequest request) { return Presentation.Print(request); }
            public void StoreMessage(int index, ISourceInstructionText text) { Presentation.StoreMessage(index, text); }
            public ISourceInstructionFlasher CreateTextFlasher(string name, int processId, ISourceInstructionText title,
                ISourceInstructionText second, int thirdArgument, int fourthArgument)
            { return Presentation.CreateTextFlasher(name, processId, title, second, thirdArgument, fourthArgument); }
            public void CreatePlateFlasher(string name, int processId, ISourceInstructionPlate plate,
                int flashes, int delay, int fourthArgument)
            { Presentation.CreatePlateFlasher(name, processId, plate, flashes, delay, fourthArgument); }
            public void PlaySound(SourceInstructionSound sound) { Presentation.PlaySound(sound); }
            public void DeleteStrings(int id, int mask) { Presentation.DeleteStrings(id, mask); }
            public void KillTextFlasher(ISourceInstructionFlasher flasher, int signal)
            { Presentation.KillTextFlasher(flasher, signal); }
            public void DeletePlate(ISourceInstructionPlate plate) { Presentation.DeletePlate(plate); }
        }
    }
}
