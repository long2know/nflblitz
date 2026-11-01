using System;
using System.Collections.Generic;
using Blitz.Contracts;

namespace Blitz.Gameplay
{
    public sealed class SourceInstructionDispatcher
    {
        public const string Function = "instructions_proc";
        public const string KickoffFunction = "ko_idiot_box_proc";
        public const string KickoffChildFunction = "plr_ko_idiot_box_proc";
        public const string KickoffFlashFunction = "flash_plyr_proc2";
        private readonly SourcePortableProcessHost host;
        private readonly Func<SourceInstructionSound, SourceInstructionSoundContext, SourceProcessSoundCall> instructionSoundOutput;
        private readonly Func<SourceKickoffInstructionChildSound, SourceProcessSoundCall> kickoffSoundOutput;
        private SourceInstructionSound executingSound;
        private SourceKickoffInstructionChildSound executingKickoffSound;
        private SourceInstructionSoundContext executingSoundContext;
        private SourceProcessSoundCall executingSoundOutput;
        private bool soundOutputObserved;
        private readonly Dictionary<long, InstructionProcess> entries = new Dictionary<long, InstructionProcess>();
        private readonly Dictionary<long, KickoffProcess> kickoffEntries = new Dictionary<long, KickoffProcess>();
        private readonly Dictionary<long, KickoffChildProcess> kickoffChildren = new Dictionary<long, KickoffChildProcess>();
        private readonly Dictionary<long, KickoffFlashProcess> kickoffFlashes = new Dictionary<long, KickoffFlashProcess>();
        private bool admitting, automaticAdmissionFaulted;
        private SourceInstructionAdmissionBinding automaticBinding;
        private SourceKickoffInstructionChildBinding boundSourceChild;
        private Func<long> kickoffNextSerial;
        private Action<long> kickoffCommitSerial;
        public long LastSerial { get; private set; }
        public long LastKickoffSerial { get; private set; }
        public SourceInstructionDispatcher(SourcePortableProcessHost host)
            : this(host, null, null) { }

        internal SourceInstructionDispatcher(SourcePortableProcessHost host,
            Func<SourceInstructionSound, SourceInstructionSoundContext, SourceProcessSoundCall> instructionSoundOutput,
            Func<SourceKickoffInstructionChildSound, SourceProcessSoundCall> kickoffSoundOutput)
        {
            this.host = host ?? throw new ArgumentNullException(nameof(host));
            this.instructionSoundOutput = instructionSoundOutput;
            this.kickoffSoundOutput = kickoffSoundOutput;
        }

        internal void RequireExecutingSound(SourceInstructionSound sound, SourceInstructionSoundContext context)
        {
            if (sound == null || !ReferenceEquals(executingSound, sound) || host.Caller <= 0
                || host.Caller != context.InstructionSerial || host.SourceTick != context.SourceTick
                || executingSoundContext.SourceTick != context.SourceTick
                || executingSoundContext.InstructionSerial != context.InstructionSerial
                || executingSoundContext.CallOrdinal != context.CallOrdinal || !Owns(host.Caller))
                throw new InvalidOperationException("Instruction sound requires its actual live source sound callback and exact context.");
        }

        internal void RequireExecutingKickoffSound(SourceKickoffInstructionChildSound sound)
        {
            if (sound == null || !ReferenceEquals(executingKickoffSound, sound) || host.Caller <= 0
                || host.Caller != executingSoundContext.InstructionSerial
                || host.SourceTick != executingSoundContext.SourceTick || !OwnsKickoffChild(host.Caller))
                throw new InvalidOperationException("Kickoff sound requires its actual live source child sound callback.");
        }

        internal SourceProcessSoundCall TakeExecutingSound(SourceInstructionSound sound, SourceInstructionSoundContext context)
        { RequireExecutingSound(sound, context); return TakeExecutingSoundOutput(); }

        internal SourceProcessSoundCall TakeExecutingKickoffSound(SourceKickoffInstructionChildSound sound)
        { RequireExecutingKickoffSound(sound); return TakeExecutingSoundOutput(); }

        private SourceProcessSoundCall TakeExecutingSoundOutput()
        {
            if (executingSoundOutput == null || soundOutputObserved)
                throw new InvalidOperationException("An unbound or already observed instruction sound cannot supply another output.");
            soundOutputObserved = true;
            return executingSoundOutput;
        }

        private void ExecuteSound(SourceInstructionSound sound, int ordinal, Action effect)
        {
            RequireNoSoundCallback();
            executingSound = sound;
            executingSoundContext = new SourceInstructionSoundContext(host.SourceTick, host.Caller, ordinal);
            try
            {
                if (instructionSoundOutput != null)
                    executingSoundOutput = instructionSoundOutput(sound, executingSoundContext);
                effect();
            }
            finally { ClearSoundCallback(); }
        }

        private void ExecuteKickoffSound(SourceKickoffInstructionChildSound sound, Action effect)
        {
            RequireNoSoundCallback();
            executingKickoffSound = sound;
            executingSoundContext = new SourceInstructionSoundContext(host.SourceTick, host.Caller, 0);
            try
            {
                if (kickoffSoundOutput != null) executingSoundOutput = kickoffSoundOutput(sound);
                effect();
            }
            finally { ClearSoundCallback(); }
        }

        private void RequireNoSoundCallback()
        {
            if (executingSound != null || executingKickoffSound != null)
                throw new InvalidOperationException("A source sound callback cannot reenter another sound output.");
        }

        private void ClearSoundCallback()
        {
            executingSound = null; executingKickoffSound = null;
            executingSoundContext = default(SourceInstructionSoundContext);
            executingSoundOutput = null; soundOutputObserved = false;
        }

        public bool Owns(long serial) { return entries.ContainsKey(serial); }
        public bool OwnsKickoff(long serial) { return kickoffEntries.ContainsKey(serial); }
        public bool OwnsKickoffChild(long serial) { return kickoffChildren.ContainsKey(serial); }
        public bool OwnsKickoffFlash(long serial) { return kickoffFlashes.ContainsKey(serial); }
        public SourceKickoffInstructionChildInvocation KickoffChildInvocation(long serial)
        {
            KickoffChildProcess child;
            if (!kickoffChildren.TryGetValue(serial, out child))
                throw new ArgumentException("No kickoff child owns this serial.", nameof(serial));
            return child.Invocation;
        }
        public SourceKickoffInstructionFlashInvocation KickoffFlashInvocation(long serial)
        {
            KickoffFlashProcess flash;
            if (!kickoffFlashes.TryGetValue(serial, out flash))
                throw new ArgumentException("No kickoff flash owns this serial.", nameof(serial));
            return flash.Invocation;
        }
        public SourceInstructionSnapshot LastSnapshot
        { get { return LastSerial == 0 ? null : Snapshot(LastSerial); } }
        public SourceInstructionSnapshot Snapshot(long serial)
        {
            InstructionProcess process;
            if (!entries.TryGetValue(serial, out process))
                throw new ArgumentException("No instruction invocation owns this serial.", nameof(serial));
            return process.Snapshot(host.Require(serial));
        }

        public void Admit(long serial, SourceInstructionInvocation invocation)
        {
            BeginAdmission();
            try { AdmitCore(serial, invocation, 0); }
            finally { admitting = false; }
        }

        public void AdmitFromProcess(long serial, SourceInstructionInvocation invocation, long expectedCaller)
        {
            BeginAdmission();
            try
            {
                RequireActiveCaller(expectedCaller);
                AdmitCore(serial, invocation, expectedCaller);
            }
            finally { admitting = false; }
        }

        public void InitializeAdmission(SourceInstructionAdmissionBinding binding)
        { InitializeAdmission(binding, null, null); }

        public void InitializeAdmission(SourceInstructionAdmissionBinding binding,
            Func<long> nextSerial, Action<long> commitSerial)
        {
            BeginAdmission();
            try
            {
                if (binding == null) throw new ArgumentNullException(nameof(binding));
                RequireNoAdmissionFault();
                if (host.Caller != 0 || automaticBinding != null || entries.Count != 0 || kickoffEntries.Count != 0)
                    throw new InvalidOperationException("Initialize instruction admission on a fresh idle dispatcher.");
                if (binding.KickoffCoordinator != null && (nextSerial == null || commitSerial == null))
                    throw new ArgumentException("Complete kickoff coordination requires the actual shared serial allocator and commit.");
                SourceKickoffInstructionChildBinding child = binding.KickoffCoordinator == null
                    ? null : binding.KickoffCoordinator.SourceChild;
                if (child != null && child.OwnedHelpers != null)
                    child = child.BindOwnedFactories(
                        new SourceKickoffOwnedActivePlayerWaitFactory(binding.State, child.OwnedHelpers),
                        new SourceKickoffOwnedFlashFactory(host, child.OwnedHelpers));
                binding.State.WriteIdiotBoxesShown(binding.State.ReadPlayerStatus() != 0 ? 0 : 2);
                boundSourceChild = child;
                automaticBinding = binding;
                kickoffNextSerial = nextSerial;
                kickoffCommitSerial = commitSerial;
            }
            catch { automaticAdmissionFaulted = true; throw; }
            finally { admitting = false; }
        }

        public long? AdmitAtPlaySelectionReturn(SourceInstructionAdmissionBinding binding,
            bool defaultPlayTypeArm, long expectedCaller, Func<long> nextSerial)
        {
            BeginAdmission();
            try
            {
                RequireAutomaticAdmission(binding);
                RequireActiveCaller(expectedCaller);
                if (!defaultPlayTypeArm || binding.State.ReadIdiotBoxesShown() != 1) return null;
                for (int slot = 0; slot < 4; slot++)
                    if (binding.State.ReadGamesPlayed(slot) < 3
                        && (binding.State.ReadPlayerStatus() & (1 << slot)) != 0)
                    {
                        if (nextSerial == null) throw new ArgumentNullException(nameof(nextSerial));
                        long serial = nextSerial();
                        AdmitCore(serial, binding.Invocation, expectedCaller);
                        return serial;
                    }
                return null;
            }
            catch { automaticAdmissionFaulted = true; throw; }
            finally { admitting = false; }
        }

        public long? AdmitKickoffFromCatch(SourceInstructionAdmissionBinding binding,
            bool kickoff, long expectedCaller, Func<long> nextSerial)
        {
            BeginAdmission();
            try
            {
                RequireAutomaticAdmission(binding);
                RequireActiveCaller(expectedCaller);
                if (!kickoff || binding.State.ReadIdiotBoxesShown() != 0) return null;
                if (nextSerial == null) throw new ArgumentNullException(nameof(nextSerial));
                long serial = nextSerial();
                RequireActiveCaller(expectedCaller);
                // player.c:3664 supplies PID 0. Entry, not creation, writes 1.
                host.Create(serial, 0, KickoffFunction, 0);
                kickoffEntries.Add(serial, new KickoffProcess(binding));
                LastKickoffSerial = serial;
                return serial;
            }
            catch { automaticAdmissionFaulted = true; throw; }
            finally { admitting = false; }
        }

        private void BeginAdmission()
        {
            if (admitting)
                throw new InvalidOperationException("Instruction admission cannot reenter its source-ordered provider reads.");
            admitting = true;
        }

        private void RequireActiveCaller(long expectedCaller)
        {
            if (expectedCaller <= 0 || host.Caller != expectedCaller)
                throw new InvalidOperationException("Instruction creation requires the actual declared executing caller.");
        }

        private void RequireAutomaticAdmission(SourceInstructionAdmissionBinding binding)
        {
            if (binding == null) throw new ArgumentNullException(nameof(binding));
            RequireNoAdmissionFault();
            if (!ReferenceEquals(automaticBinding, binding))
                throw new InvalidOperationException("Use the same explicitly initialized instruction admission binding.");
        }

        private void RequireNoAdmissionFault()
        {
            if (automaticAdmissionFaulted)
                throw new InvalidOperationException("Failed automatic instruction admission requires a fresh dispatcher.");
            foreach (InstructionProcess process in entries.Values)
                if (process.Faulted)
                    throw new InvalidOperationException("A failed instruction continuation requires a fresh dispatcher.");
            foreach (KickoffProcess process in kickoffEntries.Values)
                if (process.Faulted)
                    throw new InvalidOperationException("A failed external kickoff continuation requires a fresh dispatcher.");
            foreach (KickoffChildProcess process in kickoffChildren.Values)
                if (process.Faulted)
                    throw new InvalidOperationException(process.SourceChild == null
                        ? "A failed external kickoff child requires a fresh dispatcher."
                        : "A failed source kickoff child requires a fresh dispatcher.");
            foreach (KickoffFlashProcess process in kickoffFlashes.Values)
                if (process.Faulted)
                    throw new InvalidOperationException("A failed external kickoff flash requires a fresh dispatcher.");
        }

        private void AdmitCore(long serial, SourceInstructionInvocation invocation, long expectedCaller)
        {
            if (invocation == null) throw new ArgumentNullException(nameof(invocation));
            if (host.Caller != expectedCaller)
                throw new InvalidOperationException("Admit an explicit source program between process dispatches.");
            RequireNoAdmissionFault();
            foreach (InstructionProcess process in entries.Values)
                if (!process.Completed)
                    throw new InvalidOperationException("Concurrent or faulted shared instruction resources need an explicit reset.");
            RequireBinding(invocation.ProcessIds.PostPlayer, "last_player_proc");
            RequireBinding(invocation.ProcessIds.Camera, "cambot_proc");
            foreach (SourceProcessView process in host.Processes)
                if (process.Id == invocation.ProcessIds.Instructions && !Owns(process.Serial))
                    throw new InvalidOperationException("The supplied instruction PID collides with another portable process.");
            host.Create(serial, invocation.ProcessIds.Instructions, Function, 0);
            entries.Add(serial, new InstructionProcess(serial, invocation));
            LastSerial = serial;
        }

        private void RequireBinding(int id, string function)
        {
            foreach (SourceProcessView process in host.Processes)
                if (process.Id == id && process.Function == function) return;
            throw new InvalidOperationException("The required source PID is not bound to " + function + ".");
        }

        public bool Dispatch(long serial, Action<string> beginProcess)
        {
            InstructionProcess process;
            if (!entries.TryGetValue(serial, out process))
                return DispatchKickoff(serial, beginProcess) || DispatchKickoffChild(serial, beginProcess)
                    || DispatchKickoffFlash(serial, beginProcess);
            if (beginProcess == null) throw new ArgumentNullException(nameof(beginProcess));
            if (process.Faulted)
                throw new InvalidOperationException("A failed external instruction call cannot be replayed as successful progress.");
            // Fallthrough is not a native die() claim. Preserve the terminal entry
            // without resuming its returned body or inventing a process-retirement rule.
            if (process.Completed) return true;
            if (!host.Visit(serial)) return true;
            bool returned = false;
            try
            {
                beginProcess(Function);
                process.Advance(host, (sound, effect) => ExecuteSound(sound, process.NextSoundOrdinal(), effect));
                returned = true;
            }
            finally
            {
                if (!returned) process.MarkFaulted();
                host.Complete();
            }
            return true;
        }

        private bool DispatchKickoff(long serial, Action<string> beginProcess)
        {
            KickoffProcess process;
            if (!kickoffEntries.TryGetValue(serial, out process)) return false;
            if (beginProcess == null) throw new ArgumentNullException(nameof(beginProcess));
            if (process.Faulted)
                throw new InvalidOperationException("A failed external kickoff continuation cannot be replayed.");
            if (process.Returned) return true;
            if (!host.Visit(serial)) return true;
            bool returned = false;
            try
            {
                beginProcess(KickoffFunction);
                if (!process.Entered)
                {
                    process.Binding.State.WriteIdiotBoxesShown(1);
                    process.Entered = true;
                    if (process.Binding.KickoffCoordinator == null)
                    {
                        process.Continuation = process.Binding.KickoffContinuationFactory.Create();
                        if (process.Continuation == null)
                            throw new InvalidOperationException("The admitted kickoff suffix requires an actual external continuation.");
                    }
                }
                if (process.Binding.KickoffCoordinator != null)
                    AdvanceKickoffCoordinator(process, serial);
                else
                {
                    SourceKickoffInstructionStep step = process.Continuation.Advance();
                    if (step.Kind == SourceKickoffInstructionStepKind.Sleep)
                        host.Sleep(step.SleepTicks);
                    else if (step.Kind == SourceKickoffInstructionStepKind.ExternalFallthrough)
                    {
                        process.Returned = true;
                        host.Record("ExternalFallthrough", serial, 0, 0);
                    }
                    else throw new InvalidOperationException("The external kickoff continuation supplied no declared progress.");
                }
                returned = true;
            }
            finally
            {
                if (!returned) process.Faulted = true;
                host.Complete();
            }
            return true;
        }

        private void CreateKickoffChild(KickoffProcess parent, long parentSerial, int slot)
        {
            SourceKickoffInstructionCoordinator config = parent.Binding.KickoffCoordinator;
            long childSerial = kickoffNextSerial();
            RequireActiveCaller(parentSerial);
            var invocation = new SourceKickoffInstructionChildInvocation(
                childSerial, config.ChildProcessId, slot, 0, 0, 0);
            host.Create(childSerial, config.ChildProcessId, KickoffChildFunction, 0);
            kickoffChildren.Add(childSerial, new KickoffChildProcess(invocation, parent.Binding, boundSourceChild));
            kickoffCommitSerial(childSerial);
        }

        private void CreateKickoffFlash(KickoffChildProcess child, long childSerial, int slot)
        {
            long serial = kickoffNextSerial();
            RequireActiveCaller(childSerial);
            var invocation = new SourceKickoffInstructionFlashInvocation(serial, slot);
            host.Create(serial, invocation.ProcessId, KickoffFlashFunction, 0);
            kickoffFlashes.Add(serial, new KickoffFlashProcess(invocation, child.SourceChild));
            kickoffCommitSerial(serial);
        }

        private void AdvanceKickoffCoordinator(KickoffProcess process, long serial)
        {
            RequireNoAdmissionFault();
            SourceKickoffInstructionCoordinator config = process.Binding.KickoffCoordinator;
            if (!process.CoordinatorStarted)
            {
                CreateKickoffChild(process, serial, 0);
                CreateKickoffChild(process, serial, 1);
                if (config.Inputs.ReadFourPlayerVersion() != 0)
                {
                    CreateKickoffChild(process, serial, 2);
                    CreateKickoffChild(process, serial, 3);
                }
                host.Record("SuspendMask", serial, 0, 32768);
                host.SuspendMultiple(0, 32768);
                host.Record("ResumeMask", serial, config.ChildProcessId, -1);
                host.ResumeMultiple(config.ChildProcessId, -1);
                process.CoordinatorStarted = true;
            }
            host.Record("ExistsCall", serial, config.ChildProcessId, -1);
            if (host.Exists(config.ChildProcessId, -1))
            {
                host.Sleep(1);
                return;
            }
            host.Record("ResumeAllCall", serial, 0, 0);
            host.ResumeAll();
            int buttonMask = config.ButtonAMask | config.ButtonBMask;
            ISourceKickoffInstructionPlayerButtons buttons = config.Buttons;
            for (int slot = 0; slot < config.MaximumPlayers; slot++)
            {
                if ((process.Binding.State.ReadPlayerStatus() & (1 << slot)) != 0
                    && config.Inputs.ReadPlayerControl(slot) >= 0)
                {
                    int player = config.Inputs.ReadPlayerControl(slot);
                    buttons.WriteCurrentButtons(player, buttons.ReadCurrentButtons(player) | buttonMask);
                    player = config.Inputs.ReadPlayerControl(slot);
                    buttons.WriteOldButtons(player, buttons.ReadOldButtons(player) | buttonMask);
                }
            }
            process.Returned = true;
            host.Record("Fallthrough", serial, 0, 0);
        }

        private bool DispatchKickoffChild(long serial, Action<string> beginProcess)
        {
            KickoffChildProcess process;
            if (!kickoffChildren.TryGetValue(serial, out process)) return false;
            if (beginProcess == null) throw new ArgumentNullException(nameof(beginProcess));
            if (process.Faulted)
                throw new InvalidOperationException(process.SourceChild == null
                    ? "A failed external kickoff child cannot be replayed."
                    : "A failed source kickoff child cannot be replayed.");
            if (process.Returned) return true;
            if (!host.Visit(serial)) return true;
            bool returned = false;
            try
            {
                RequireNoAdmissionFault();
                beginProcess(KickoffChildFunction);
                if (process.Continuation == null)
                {
                    process.Continuation = process.SourceChild == null
                        ? process.Factory.Create(process.Invocation)
                        : new SourceKickoffInstructionChildProcess(process.Invocation, process.State,
                            process.Inputs, process.SourceChild, slot => CreateKickoffFlash(process, serial, slot),
                            ExecuteKickoffSound);
                    if (process.Continuation == null)
                        throw new InvalidOperationException("A kickoff child requires an actual external continuation.");
                }
                SourceKickoffInstructionChildStep step = process.Continuation.Advance();
                if (step.Kind == SourceKickoffInstructionChildStepKind.Sleep)
                    host.Sleep(step.SleepTicks);
                else if (step.Kind == SourceKickoffInstructionChildStepKind.ExternalFallthrough)
                {
                    process.Returned = true;
                    host.Record("ExternalChildFallthrough", serial, process.Invocation.ProcessId, 0);
                }
                else if (step.Kind == SourceKickoffInstructionChildStepKind.ExternalRetirement)
                {
                    process.Returned = true;
                    host.Kill(serial);
                }
                else if (step.Kind == SourceKickoffInstructionChildStepKind.NativeDie && process.SourceChild != null)
                {
                    if (step.DieSignal != 0)
                        throw new InvalidOperationException("The observed kickoff child die operand is zero.");
                    process.Returned = true;
                    host.Record("NativeChildDie", serial, process.Invocation.ProcessId, step.DieSignal);
                    host.Kill(serial);
                }
                else if (step.Kind == SourceKickoffInstructionChildStepKind.SourceFallthrough && process.SourceChild != null)
                {
                    host.Record("SourceChildFallthrough", serial, process.Invocation.ProcessId, (int)step.ReturnReason);
                    SourceKickoffInstructionChildReturnAction action = process.SourceChild.Returns.ReturnFromSourceBody(
                        process.Invocation, step.ReturnReason);
                    if (action == SourceKickoffInstructionChildReturnAction.KeepLive)
                        host.Record("ExternalChildReturnKeepLive", serial, process.Invocation.ProcessId, 0);
                    else if (action == SourceKickoffInstructionChildReturnAction.ExternallyRetire)
                    {
                        host.Record("ExternalChildReturnRetirement", serial, process.Invocation.ProcessId, 0);
                        host.Kill(serial);
                    }
                    else throw new InvalidOperationException("The source child return binding supplied no declared lifecycle action.");
                    process.Returned = true;
                }
                else throw new InvalidOperationException("The external kickoff child supplied no declared progress.");
                returned = true;
            }
            finally
            {
                if (!returned) process.Faulted = true;
                host.Complete();
            }
            return true;
        }

        private bool DispatchKickoffFlash(long serial, Action<string> beginProcess)
        {
            KickoffFlashProcess process;
            if (!kickoffFlashes.TryGetValue(serial, out process)) return false;
            if (beginProcess == null) throw new ArgumentNullException(nameof(beginProcess));
            if (process.Faulted)
                throw new InvalidOperationException("A failed external kickoff flash cannot be replayed.");
            if (process.Returned) return true;
            if (!host.Visit(serial)) return true;
            bool returned = false;
            try
            {
                RequireNoAdmissionFault();
                beginProcess(KickoffFlashFunction);
                if (process.Continuation == null)
                {
                    process.Continuation = process.Factory.Create(process.Invocation);
                    if (process.Continuation == null)
                        throw new InvalidOperationException("A kickoff flash requires an actual external continuation.");
                }
                SourceKickoffInstructionChildStep step = process.Continuation.Advance();
                if (step.Kind == SourceKickoffInstructionChildStepKind.Sleep)
                    host.Sleep(step.SleepTicks);
                else if (step.Kind == SourceKickoffInstructionChildStepKind.ExternalFallthrough)
                {
                    process.Returned = true;
                    host.Record("ExternalFlashFallthrough", serial, process.Invocation.ProcessId, 0);
                }
                else if (step.Kind == SourceKickoffInstructionChildStepKind.ExternalRetirement)
                {
                    process.Returned = true;
                    host.Record("ExternalFlashRetirement", serial, process.Invocation.ProcessId, 0);
                    host.Kill(serial);
                }
                else if (step.Kind == SourceKickoffInstructionChildStepKind.SourceFallthrough
                    && process.OwnedHelpers != null
                    && step.ReturnReason == SourceKickoffInstructionChildReturnReason.NormalBodyEnd)
                {
                    host.Record("SourceFlashFallthrough", serial, process.Invocation.ProcessId, 0);
                    SourceKickoffInstructionFlashReturnAction action =
                        process.OwnedHelpers.FlashReturns.ReturnFromSourceBody(process.Invocation);
                    if (action == SourceKickoffInstructionFlashReturnAction.KeepLive)
                        host.Record("ExternalFlashReturnKeepLive", serial, process.Invocation.ProcessId, 0);
                    else if (action == SourceKickoffInstructionFlashReturnAction.ExternallyRetire)
                    {
                        host.Record("ExternalFlashReturnRetirement", serial, process.Invocation.ProcessId, 0);
                        host.Kill(serial);
                    }
                    else throw new InvalidOperationException("The source flash return binding supplied no declared lifecycle action.");
                    process.Returned = true;
                }
                else throw new InvalidOperationException("The external kickoff flash supplied no declared progress.");
                returned = true;
            }
            finally
            {
                if (!returned) process.Faulted = true;
                host.Complete();
            }
            return true;
        }

        private sealed class KickoffProcess
        {
            internal readonly SourceInstructionAdmissionBinding Binding;
            internal ISourceKickoffInstructionContinuation Continuation;
            internal bool Entered, CoordinatorStarted, Returned, Faulted;
            internal KickoffProcess(SourceInstructionAdmissionBinding binding) { Binding = binding; }
        }

        private sealed class KickoffChildProcess
        {
            internal readonly SourceKickoffInstructionChildInvocation Invocation;
            internal readonly ISourceKickoffInstructionChildContinuationFactory Factory;
            internal readonly SourceKickoffInstructionChildBinding SourceChild;
            internal readonly ISourceInstructionAdmissionState State;
            internal readonly ISourceKickoffInstructionChildInputs Inputs;
            internal ISourceKickoffInstructionChildContinuation Continuation;
            internal bool Returned, Faulted;
            internal KickoffChildProcess(SourceKickoffInstructionChildInvocation invocation,
                SourceInstructionAdmissionBinding parent, SourceKickoffInstructionChildBinding sourceChild)
            {
                Invocation = invocation; State = parent.State;
                SourceKickoffInstructionCoordinator config = parent.KickoffCoordinator;
                Factory = config.ChildContinuations; SourceChild = sourceChild;
                Inputs = config.Inputs as ISourceKickoffInstructionChildInputs;
            }
        }

        private sealed class KickoffFlashProcess
        {
            internal readonly SourceKickoffInstructionFlashInvocation Invocation;
            internal readonly ISourceKickoffFlashContinuationFactory Factory;
            internal readonly SourceKickoffInstructionOwnedHelpers OwnedHelpers;
            internal ISourceKickoffInstructionChildContinuation Continuation;
            internal bool Returned, Faulted;
            internal KickoffFlashProcess(SourceKickoffInstructionFlashInvocation invocation,
                SourceKickoffInstructionChildBinding child)
            { Invocation = invocation; Factory = child.Flashes; OwnedHelpers = child.OwnedHelpers; }
        }

        private sealed class InstructionProcess
        {
            private readonly long serial;
            private readonly SourceInstructionInvocation invocation;
            private readonly List<int> results = new List<int>();
            private SourceInstructionStage stage;
            private int pageIndex = -1;
            private int? flag;
            private ISourceInstructionPlate plate;
            private ISourceInstructionText title, ruleDetail;
            private ISourceInstructionFlasher flasher;
            private SourceAnyButtonWait wait;
            private int soundOrdinal;
            private ISourceInstructionEffects Effects { get { return invocation.Effects; } }
            private SourceInstructionPage Page { get { return (SourceInstructionPage)pageIndex; } }
            internal bool Completed { get { return stage == SourceInstructionStage.Fallthrough; } }
            internal bool Faulted { get { return stage == SourceInstructionStage.Faulted; } }
            internal InstructionProcess(long serial, SourceInstructionInvocation invocation)
            { this.serial = serial; this.invocation = invocation; stage = SourceInstructionStage.PendingEntry; }
            internal int NextSoundOrdinal() { return checked(soundOrdinal++); }
            internal void MarkFaulted() { stage = SourceInstructionStage.Faulted; }
            internal SourceInstructionSnapshot Snapshot(SourceProcessView process)
            {
                return new SourceInstructionSnapshot(process, stage, pageIndex < 0 ? (SourceInstructionPage?)null : Page,
                    flag, wait == null ? null : wait.Snapshot, results);
            }
            private void PublishFlag(int value) { Effects.WriteIdiotBoxesShown(value); flag = value; }

            internal void Advance(SourcePortableProcessHost host, Action<SourceInstructionSound, Action> soundOutput)
            {
                while (true)
                {
                    switch (stage)
                    {
                        case SourceInstructionStage.PendingEntry:
                            PublishFlag(-1);
                            stage = SourceInstructionStage.EntryDelay;
                            host.Sleep(2);
                            return;
                        case SourceInstructionStage.EntryDelay:
                            host.Record("SuspendMask", serial, 0, 0x8000);
                            host.SuspendMultiple(0, 0x8000);
                            Resume(host, invocation.ProcessIds.Instructions);
                            Resume(host, invocation.ProcessIds.PostPlayer);
                            Resume(host, invocation.ProcessIds.Camera);
                            plate = Effects.BeginPlate("playresult", "SCR_PLATE_TID",
                                new SourceInstructionPosition(0, 10, 2.5f, true));
                            if (plate == null)
                                throw new InvalidOperationException("Instruction entry requires a valid beginobj return.");
                            Effects.SetPlateWidthScale(plate, 308.0f / 212.0f);
                            Effects.SetPlateHeightScale(plate, 190.0f / 79.0f);
                            Effects.SetPlateId(plate, "OID_MESSAGE");
                            Effects.GeneratePlateVertices(plate);
                            pageIndex = 0;
                            SetupPage(host);
                            return;
                        case SourceInstructionStage.PageDelay:
                            if (pageIndex == 0)
                                Effects.CreatePlateFlasher("flash", invocation.ProcessIds.Message, plate, 2, 3, 0);
                            int tsec = invocation.Inputs.ReadTsec();
                            if (tsec < 0 || tsec > int.MaxValue / 9)
                                throw new ArgumentOutOfRangeException("tsec", "The admitted wait domain requires nonnegative 9*tsec in Int32.");
                            wait = new SourceAnyButtonWait(10, 9 * tsec, invocation.AllAbcdMask);
                            stage = SourceInstructionStage.Waiting;
                            continue;
                        case SourceInstructionStage.Waiting:
                            if (!wait.Advance(invocation.Inputs,
                                () => soundOutput(SourceInstructionSound.Tick,
                                    () => Effects.PlaySound(SourceInstructionSound.Tick)), host.Sleep)) return;
                            results.Add(wait.Result);
                            soundOutput(SourceInstructionSound.PageAdvance,
                                () => Effects.PlaySound(SourceInstructionSound.PageAdvance));
                            Effects.DeleteStrings(0x34, -1);
                            Effects.KillTextFlasher(flasher, 0);
                            if (pageIndex < 3)
                            {
                                pageIndex++;
                                SetupPage(host);
                                return;
                            }
                            Effects.DeletePlate(plate);
                            host.Record("ResumeAllCall", serial, 0, 0);
                            host.ResumeAll();
                            stage = SourceInstructionStage.FinalDelay;
                            host.Sleep(10);
                            return;
                        case SourceInstructionStage.FinalDelay:
                            PublishFlag(2);
                            stage = SourceInstructionStage.Fallthrough;
                            host.Record("Fallthrough", serial, invocation.ProcessIds.Instructions, 0);
                            return;
                        default:
                            throw new InvalidOperationException("Only a pending instruction continuation may execute.");
                    }
                }
            }

            private void Resume(SourcePortableProcessHost host, int id)
            {
                host.Record("ResumeMask", serial, id, -1);
                host.ResumeMultiple(id, -1);
            }
            private void Position(int horizontal, int vertical)
            { Effects.SetTextPosition(new SourceInstructionPosition(horizontal, vertical, 1.0f, false)); }
            private ISourceInstructionText Print(int line, SourceInstructionJustification justification)
            { return Effects.Print(new SourceInstructionTextRequest(Page, line, justification)); }
            private ISourceInstructionText PrintStored(int line, int slot, SourceInstructionJustification justification)
            {
                ISourceInstructionText text = Print(line, justification);
                if (text == null) throw new InvalidOperationException("The admitted pmsg write requires a valid text return.");
                Effects.StoreMessage(slot, text);
                return text;
            }
            private void SetupPage(SourcePortableProcessHost host)
            {
                Effects.SetTextFont(invocation.Inputs.ReadIsLowResolution() != 0
                    ? SourceInstructionFont.Bast23 : SourceInstructionFont.Bast18);
                Effects.SetStringId(0x34);
                Position(0, 76);
                Effects.SetTextColor(SourceInstructionColor.LightRed);
                title = PrintStored(0, 0, SourceInstructionJustification.Center);
                Effects.SetTextColor(pageIndex == 0 ? SourceInstructionColor.LightRed : SourceInstructionColor.LightYellow);
                Effects.SetTextFont(invocation.Inputs.ReadIsLowResolution() != 0
                    ? SourceInstructionFont.Bast10Low : SourceInstructionFont.Bast10);
                if (pageIndex == 0)
                {
                    Position(-128, 47);
                    ruleDetail = PrintStored(1, 1, SourceInstructionJustification.Left);
                    Effects.SetTextColor(SourceInstructionColor.White);
                    Position(40, 47);
                    Print(2, SourceInstructionJustification.Center);
                    Position(0, 25);
                    Effects.SetTextColor(SourceInstructionColor.LightYellow);
                    Print(3, SourceInstructionJustification.Center);
                    Position(0, 3);
                    Effects.SetTextColor(SourceInstructionColor.White);
                    Print(4, SourceInstructionJustification.Center);
                    Position(0, -19);
                    Effects.SetTextColor(SourceInstructionColor.LightYellow);
                    Print(5, SourceInstructionJustification.Center);
                    Position(0, -41);
                    Effects.SetTextColor(SourceInstructionColor.White);
                    Print(6, SourceInstructionJustification.Center);
                }
                else
                {
                    int[] offsets = { 49, 32, 8, -9, -33, -50 };
                    for (int line = 1; line <= 6; line++)
                    {
                        if (line == 3) Effects.SetTextColor(SourceInstructionColor.White);
                        if (line == 5) Effects.SetTextColor(SourceInstructionColor.LightYellow);
                        Position(0, offsets[line - 1]);
                        Print(line, SourceInstructionJustification.Center);
                    }
                }
                string name = pageIndex == 0 ? "ftp15" : pageIndex == 3 ? "ftp17" : "ftp16";
                flasher = Effects.CreateTextFlasher(name, 0, title, pageIndex == 0 ? ruleDetail : null, 0, 0);
                if (flasher == null) throw new InvalidOperationException("The admitted page requires a text-flasher return.");
                stage = SourceInstructionStage.PageDelay;
                host.Sleep(2);
            }
        }
    }
}
