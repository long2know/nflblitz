using System;
using System.Collections.Generic;
using Blitz.Contracts;
using Blitz.Gameplay;

namespace Blitz.SourceInstructionalAdmissionComponent
{
    internal static class Program
    {
        private sealed class TestFailure : Exception
        { internal TestFailure(string message) : base(message) { } }

        private static void Check(bool condition, string message)
        { if (!condition) throw new TestFailure(message); }

        private static void Equal<T>(T expected, T actual, string message)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
                throw new TestFailure(message + ": expected " + expected + ", got " + actual);
        }

        private static void RejectState(Action action, string message)
        {
            try { action(); }
            catch (InvalidOperationException) { return; }
            throw new TestFailure(message);
        }

        private static void RejectArgument(Action action, string message)
        {
            try { action(); }
            catch (ArgumentException) { return; }
            throw new TestFailure(message);
        }

        private sealed class Plate : ISourceInstructionPlate { }
        private sealed class Text : ISourceInstructionText { }
        private sealed class Flasher : ISourceInstructionFlasher { }

        // Explicit synthetic boundary services, not native input/resource proof.
        private sealed class State : ISourceInstructionAdmissionState,
            ISourceInstructionInputs, ISourceInstructionEffects
        {
            internal int Flag, Status = 1;
            internal readonly int[] Games = { 3, 3, 3, 3 };
            internal readonly List<string> Trace = new List<string>();
            internal readonly Queue<int> StatusSamples = new Queue<int>();
            internal int ThrowGamesAt = -1;
            internal bool ThrowPlate;
            internal Action OnFlagRead;
            private Plate plate;
            private Flasher flasher;

            public int ReadIdiotBoxesShown()
            {
                Trace.Add("read:flag");
                if (OnFlagRead != null) OnFlagRead();
                return Flag;
            }
            public void WriteIdiotBoxesShown(int value)
            { Trace.Add("write:" + value); Flag = value; }
            public int ReadPlayerStatus()
            {
                Trace.Add("read:status");
                return StatusSamples.Count == 0 ? Status : StatusSamples.Dequeue();
            }
            public int ReadGamesPlayed(int slot)
            {
                Trace.Add("read:games:" + slot);
                if (slot == ThrowGamesAt) throw new InvalidOperationException("explicit experience provider failure");
                return Games[slot];
            }
            public int ReadTsec() { Trace.Add("read:tsec"); return 0; }
            public int ReadIsLowResolution() { Trace.Add("read:low"); return 0; }
            public int ReadPlayerSwitches() { throw new TestFailure("zero-tsec domain must not poll switches"); }
            public int ReadShowPlyselIdiotBox() { throw new TestFailure("zero-poll wait must not read sound gate"); }
            public ISourceInstructionPlate BeginPlate(string image, string texture, SourceInstructionPosition position)
            {
                Equal("playresult", image, "real instruction plate request");
                Equal("SCR_PLATE_TID", texture, "real instruction texture request");
                Trace.Add("plate:begin");
                if (ThrowPlate) throw new InvalidOperationException("explicit plate provider failure");
                plate = new Plate();
                return plate;
            }
            private void RequirePlate(ISourceInstructionPlate value)
            { Check(plate != null && ReferenceEquals(plate, value), "retained plate identity"); }
            public void SetPlateWidthScale(ISourceInstructionPlate value, float scale) { RequirePlate(value); }
            public void SetPlateHeightScale(ISourceInstructionPlate value, float scale) { RequirePlate(value); }
            public void SetPlateId(ISourceInstructionPlate value, string id) { RequirePlate(value); }
            public void GeneratePlateVertices(ISourceInstructionPlate value) { RequirePlate(value); }
            public void SetTextFont(SourceInstructionFont font) { }
            public void SetStringId(int id) { Equal(0x34, id, "real page group"); }
            public void SetTextPosition(SourceInstructionPosition position) { }
            public void SetTextColor(SourceInstructionColor color) { }
            public ISourceInstructionText Print(SourceInstructionTextRequest request)
            { Trace.Add("print:" + request.Page + ":" + request.Line); return new Text(); }
            public void StoreMessage(int index, ISourceInstructionText text)
            { Check(text != null && (index == 0 || index == 1), "actual text return stored"); }
            public ISourceInstructionFlasher CreateTextFlasher(string name, int id,
                ISourceInstructionText first, ISourceInstructionText second, int third, int fourth)
            {
                Check(first != null, "actual title return for flasher");
                flasher = new Flasher();
                return flasher;
            }
            public void CreatePlateFlasher(string name, int id, ISourceInstructionPlate value,
                int flashes, int delay, int fourth) { RequirePlate(value); }
            public void PlaySound(SourceInstructionSound sound) { Trace.Add("sound:" + sound.SoundSymbol); }
            public void DeleteStrings(int id, int mask) { Trace.Add("strings:delete"); }
            public void KillTextFlasher(ISourceInstructionFlasher value, int signal)
            { Check(ReferenceEquals(flasher, value), "actual flasher return killed"); }
            public void DeletePlate(ISourceInstructionPlate value)
            { RequirePlate(value); Trace.Add("plate:delete"); plate = null; }
        }

        // The suffix is intentionally an opaque, explicitly supplied test service.
        // Its two synthetic steps do NOT model the native callbacks/remainder.
        private sealed class SuffixFactory : ISourceKickoffInstructionContinuationFactory
        {
            private readonly State state;
            internal int Created, Advances;
            internal bool ThrowOnAdvance;
            internal SuffixFactory(State state) { this.state = state; }
            public ISourceKickoffInstructionContinuation Create()
            {
                Equal(1, state.Flag, "producer publishes 1 before opaque suffix factory");
                state.Trace.Add("suffix:create");
                Created++;
                return new Suffix(this);
            }
            private sealed class Suffix : ISourceKickoffInstructionContinuation
            {
                private readonly SuffixFactory owner;
                private int step;
                internal Suffix(SuffixFactory owner) { this.owner = owner; }
                public SourceKickoffInstructionStep Advance()
                {
                    owner.state.Trace.Add("suffix:advance");
                    owner.Advances++;
                    if (owner.ThrowOnAdvance) throw new InvalidOperationException("explicit opaque suffix failure");
                    return step++ == 0 ? SourceKickoffInstructionStep.Sleep(1)
                        : SourceKickoffInstructionStep.ExternalFallthrough();
                }
            }
        }

        private sealed class Rig
        {
            // Component-only identifiers. No native PID/ALL_ABCD_MASK is inferred.
            internal const int InstructionId = 0x6a31, PostId = 0x0502, CameraId = 0x1400, MessageId = 0x6a32;
            internal readonly SourcePortableProcessHost Host = new SourcePortableProcessHost();
            internal readonly State State;
            internal readonly SourceInstructionDispatcher Dispatcher;
            internal readonly SourceInstructionInvocation Invocation;
            internal readonly SuffixFactory Factory;
            internal readonly SourceInstructionAdmissionBinding Binding;
            internal long NextSerial = 33;
            internal int Allocations;

            internal Rig(int status = 1)
            {
                State = new State { Status = status };
                Host.Create(1, 0x10, "game_proc", 0);
                Host.Create(2, 0x500, "player_proc", 0x300);
                Host.Create(20, PostId, "last_player_proc", 0x30e);
                Host.Create(17, CameraId, "cambot_proc", 0x100);
                Dispatcher = new SourceInstructionDispatcher(Host);
                Invocation = new SourceInstructionInvocation(4,
                    new SourceInstructionProcessIds(InstructionId, PostId, CameraId, MessageId), State, State);
                Factory = new SuffixFactory(State);
                Binding = new SourceInstructionAdmissionBinding(Invocation, State, Factory);
                Dispatcher.InitializeAdmission(Binding);
                State.Trace.Clear();
            }

            internal long Allocate()
            { State.Trace.Add("allocate"); Allocations++; return NextSerial; }

            internal long? Within(long caller, Func<long?> operation)
            {
                Host.BeginTick(Host.SourceTick + 1);
                Check(Host.Visit(caller), "valid component caller wakes");
                try
                {
                    long? serial = operation();
                    if (serial.HasValue) NextSerial = serial.Value + 1;
                    return serial;
                }
                finally { Host.Complete(); }
            }

            internal long? Select(bool defaultArm = true)
            { return Within(1, () => Dispatcher.AdmitAtPlaySelectionReturn(Binding, defaultArm, 1, Allocate)); }

            internal long? Catch(bool kickoff = true)
            { return Within(2, () => Dispatcher.AdmitKickoffFromCatch(Binding, kickoff, 2, Allocate)); }

            internal void Tick(long serial)
            {
                Host.BeginTick(Host.SourceTick + 1);
                Check(Dispatcher.Dispatch(serial, function => State.Trace.Add("wake:" + function)),
                    "real dispatcher owns process");
            }

            internal long MustSelect()
            {
                try
                {
                    long? serial = Select();
                    Check(serial.HasValue, "eligible actual caller creates instructions");
                    return serial.Value;
                }
                catch (TestFailure) { throw; }
                catch (Exception error)
                { throw new TestFailure("eligible actual caller admission must succeed: " + error.GetType().Name); }
            }
        }

        private static void CallerContextAndManual()
        {
            var manual = new Rig();
            manual.Dispatcher.Admit(80, manual.Invocation);
            Equal(SourceInstructionStage.PendingEntry, manual.Dispatcher.Snapshot(80).Stage, "idle manual admission retained");
            manual.Within(1, () =>
            {
                RejectState(() => manual.Dispatcher.Admit(81, manual.Invocation), "manual API still requires Caller0");
                Equal(1L, manual.Host.Caller, "manual rejection does not fake idle Caller");
                return null;
            });
            var active = new Rig();
            active.Within(1, () =>
            {
                try { active.Dispatcher.AdmitFromProcess(90, active.Invocation, 1); }
                catch (Exception error)
                { throw new TestFailure("caller-aware creation must preserve valid executing caller: " + error.GetType().Name); }
                Equal(1L, active.Host.Caller, "caller-aware API retains actual Caller");
                Equal(SourceInstructionStage.PendingEntry, active.Dispatcher.Snapshot(90).Stage, "child not executed inline");
                Equal(0, active.State.Flag, "creation does not publish instruction entry");
                Check(active.Host.Contains(90), "real host child created before caller completes");
                return null;
            });
            active.Tick(90);
            Equal(-1, active.State.Flag, "deferred actual child entry");
        }

        private static void FlagAndDefaultArm()
        {
            foreach (int flag in new[] { 0, -1, 2 })
            {
                var rig = new Rig();
                rig.State.Flag = flag; rig.State.Games[0] = 2;
                Check(!rig.Select().HasValue, "flag gate suppresses source admission");
                Equal("read:flag", string.Join("|", rig.State.Trace), "flag short circuit has no experience/status reads");
                Equal(0, rig.Allocations, "no serial allocation for false flag");
            }
            var separateArm = new Rig();
            separateArm.State.Flag = 1; separateArm.State.Games[0] = 2;
            Check(!separateArm.Select(false).HasValue, "separate native arm does not admit");
            Equal(0, separateArm.State.Trace.Count, "separate arm never reads admission providers");
        }

        private static void ExperienceAndParticipation()
        {
            var three = new Rig(15); three.State.Flag = 1;
            Check(!three.Select().HasValue, "strict experience boundary excludes three");
            Equal("read:flag|read:games:0|read:games:1|read:games:2|read:games:3",
                string.Join("|", three.State.Trace), "experienced slots skip status reads");
            var inactive = new Rig(0); inactive.State.Flag = 1;
            for (int slot = 0; slot < 4; slot++) inactive.State.Games[slot] = 2;
            Check(!inactive.Select().HasValue, "inexperience without participation is not eligible");
            Equal(0, inactive.Allocations, "inactive slots do not allocate");
        }

        private static void SlotOrderAndLiveStatus()
        {
            for (int slot = 0; slot < 4; slot++)
            {
                var rig = new Rig(1 << slot); rig.State.Flag = 1; rig.State.Games[slot] = 2;
                var expected = new List<string> { "read:flag" };
                for (int i = 0; i <= slot; i++) expected.Add("read:games:" + i);
                expected.Add("read:status"); expected.Add("allocate");
                rig.MustSelect();
                Equal(string.Join("|", expected), string.Join("|", rig.State.Trace),
                    "each exact slot bit, games-before-status and OR short circuit");
            }
            var live = new Rig(); live.State.Flag = 1;
            for (int slot = 0; slot < 4; slot++) live.State.Games[slot] = 2;
            foreach (int sample in new[] { 0, 0, 0, 8 }) live.State.StatusSamples.Enqueue(sample);
            live.MustSelect();
            Equal("read:flag|read:games:0|read:status|read:games:1|read:status|read:games:2|read:status|read:games:3|read:status|allocate",
                string.Join("|", live.State.Trace), "p_status is live at each evaluated source term");
        }

        private static void ProducerDeferredAndOpaque()
        {
            var notKickoff = new Rig();
            Check(!notKickoff.Catch(false).HasValue, "non-kickoff catch is inert");
            Equal(0, notKickoff.State.Trace.Count, "non-kickoff skips flag read");
            var rig = new Rig();
            long? pending = rig.Catch();
            Check(pending.HasValue, "zero-flag kickoff catch creates real producer");
            Equal(0, rig.State.Flag, "catch creation must not reserve flag 1");
            Equal(0, rig.Factory.Created, "opaque suffix not created inline");
            Equal(0, rig.Host.Require(pending.Value).Id, "source-proven producer PID operand zero");
            Equal(SourceInstructionDispatcher.KickoffFunction, rig.Host.Require(pending.Value).Function, "actual producer function");
            rig.Tick(pending.Value);
            Equal(1, rig.State.Flag, "producer entry publishes 1");
            Equal(1, rig.Factory.Created, "one explicitly required suffix");
            Check(rig.State.Trace.IndexOf("write:1") < rig.State.Trace.IndexOf("suffix:create"),
                "entry write precedes every opaque suffix operation");
            rig.Tick(pending.Value);
            Equal(2, rig.Factory.Advances, "explicit external continuation resumes");
            Equal(1, rig.State.Flag, "external fallthrough does not invent flag 2");
            Check(rig.Host.Contains(pending.Value), "external return does not invent native self-kill");
            rig.Tick(pending.Value);
            Equal(2, rig.Factory.Advances, "externally returned suffix is not replayed");
            Check(!rig.Catch().HasValue, "producer flag prevents later zero-gated catch attempt");
        }

        private static void ProducerToFinalDelay()
        {
            var rig = new Rig(); rig.State.Games[0] = 2;
            long? producer = rig.Catch();
            Check(producer.HasValue, "production catch admission");
            rig.Tick(producer.Value); rig.Tick(producer.Value);
            long instruction = rig.MustSelect();
            Equal(1, rig.State.Flag, "selection creation does not publish -1 inline");
            rig.Tick(instruction);
            Equal(-1, rig.State.Flag, "real instruction entry consumes produced flag");
            int bounded = 0;
            while (rig.Dispatcher.Snapshot(instruction).Stage != SourceInstructionStage.FinalDelay && bounded++ < 100)
                rig.Tick(instruction);
            Equal(SourceInstructionStage.FinalDelay, rig.Dispatcher.Snapshot(instruction).Stage, "real final delay reached");
            Equal(-1, rig.State.Flag, "cleanup/resume is not completion");
            for (int i = 0; i < 9; i++) { rig.Tick(instruction); Equal(-1, rig.State.Flag, "final delay retained"); }
            rig.Tick(instruction);
            Equal(2, rig.State.Flag, "unchanged final sleep return publishes 2");
            Equal(SourceInstructionStage.Fallthrough, rig.Dispatcher.Snapshot(instruction).Stage, "real source process fallthrough");
            Check(!rig.Select().HasValue, "completed produced flag suppresses future selection");
        }

        private static void InitializationAndFreshReset()
        {
            foreach (int status in new[] { 1, 0, -4 })
            {
                var rig = new Rig(status);
                Equal(status == 0 ? 2 : 0, rig.State.Flag, "source initialization writes 0 or 2, never 1");
                rig.State.Flag = -1; rig.State.Status = status == 0 ? 1 : 0;
                var freshHost = new SourcePortableProcessHost();
                var fresh = new SourceInstructionDispatcher(freshHost);
                fresh.InitializeAdmission(rig.Binding);
                Equal(rig.State.Status == 0 ? 2 : 0, rig.State.Flag, "fresh dispatcher resets the same coherent backing");
                Equal(0L, fresh.LastSerial, "reset is not implicit instruction admission");
                Equal(0, rig.Factory.Created, "reset does not create opaque producer suffix");
            }
        }

        private static void SharedPidAndSerialRestrictions()
        {
            var bindings = new Rig();
            bindings.Within(1, () =>
            {
                foreach (SourceInstructionProcessIds ids in new[] {
                    new SourceInstructionProcessIds(Rig.InstructionId, Rig.PostId + 1, Rig.CameraId, Rig.MessageId),
                    new SourceInstructionProcessIds(Rig.InstructionId, Rig.PostId, Rig.CameraId + 1, Rig.MessageId) })
                {
                    var invocation = new SourceInstructionInvocation(4, ids, bindings.State, bindings.State);
                    RejectState(() => bindings.Dispatcher.AdmitFromProcess(90, invocation, 1),
                        "caller-aware API retains exact post-player and camera function binding");
                    Check(!bindings.Host.Contains(90), "binding rejection creates no child");
                }
                return null;
            });
            var shared = new Rig();
            shared.Within(1, () =>
            {
                shared.Dispatcher.AdmitFromProcess(90, shared.Invocation, 1);
                RejectState(() => shared.Dispatcher.AdmitFromProcess(91, shared.Invocation, 1),
                    "caller-aware API retains shared pending-resource restriction");
                return null;
            });
            var pid = new Rig();
            pid.Host.Create(99, Rig.InstructionId, "unrelated", 0);
            pid.Within(1, () =>
            {
                RejectState(() => pid.Dispatcher.AdmitFromProcess(90, pid.Invocation, 1), "instruction PID collision retained");
                Check(!pid.Host.Contains(90), "PID rejection creates no child");
                return null;
            });
            var serial = new Rig();
            serial.Host.Create(90, 0x777, "unrelated", 0);
            serial.Within(1, () =>
            {
                RejectArgument(() => serial.Dispatcher.AdmitFromProcess(90, serial.Invocation, 1), "real host serial validation retained");
                Equal(0L, serial.Dispatcher.LastSerial, "serial rejection is not successful admission");
                return null;
            });
        }

        private static void ProviderFaults()
        {
            var rig = new Rig(); rig.State.Flag = 1; rig.State.ThrowGamesAt = 1;
            RejectState(() => rig.Select(), "experience provider fault propagates");
            Equal(0, rig.Allocations, "fault before eligibility allocates nothing");
            Equal(1, rig.State.Flag, "admission failure does not publish successful state");
            rig.State.ThrowGamesAt = -1; rig.State.Trace.Clear();
            RejectState(() => rig.Select(), "automatic provider failure requires fresh dispatcher");
            Equal(0, rig.State.Trace.Count, "faulted automatic input is not replayed");
            var process = new Rig(); process.State.Flag = 1; process.State.Games[0] = 2; process.State.ThrowPlate = true;
            long serial = process.MustSelect();
            process.Tick(serial); process.Tick(serial);
            RejectState(() => process.Tick(serial), "existing real instruction effect fault propagates");
            Equal(-1, process.State.Flag, "fault is not completion 2");
            RejectState(() => process.Dispatcher.Admit(90, process.Invocation), "faulted instruction resources remain protected");
        }

        private static void ProducerFault()
        {
            var rig = new Rig(); rig.Factory.ThrowOnAdvance = true;
            long? serial = rig.Catch(); Check(serial.HasValue, "producer scheduled before explicit suffix fault");
            RejectState(() => rig.Tick(serial.Value), "opaque suffix fault propagates");
            Equal(1, rig.State.Flag, "producer fault does not invent completion/reset");
            int advances = rig.Factory.Advances;
            RejectState(() => rig.Tick(serial.Value), "faulted producer not replayed");
            Equal(advances, rig.Factory.Advances, "no duplicate external suffix execution");
            rig.State.Games[0] = 2;
            RejectState(() => rig.Select(), "producer fault protects later shared admission");
        }

        private static void AdmissionReentry()
        {
            var rig = new Rig(); rig.State.Flag = 1; rig.State.Games[0] = 2;
            rig.State.OnFlagRead = () => rig.Dispatcher.AdmitFromProcess(90, rig.Invocation, 1);
            RejectState(() => rig.Select(), "provider cannot reenter admission");
            Check(!rig.Host.Contains(90) && rig.Dispatcher.LastSerial == 0, "reentry creates no hidden child");
            rig.State.OnFlagRead = null;
            RejectState(() => rig.Select(), "reentrant failed admission is not retried as progress");
        }

        private static void CoherentBackingAndRequiredSuffix()
        {
            var rig = new Rig();
            RejectArgument(() => new SourceInstructionAdmissionBinding(rig.Invocation, new State(), rig.Factory),
                "distinct flag owners cannot bind");
            RejectArgument(() => new SourceInstructionAdmissionBinding(rig.Invocation, rig.State, null),
                "no implicit/no-op unknown suffix provider");
            RejectArgument(() => SourceKickoffInstructionStep.Sleep(0), "no invented default sleep");
        }

        private static int Main(string[] args)
        {
            string[] names = {
                "caller-context-and-legacy-manual", "flag-and-default-arm-short-circuit",
                "strict-experience-and-active-participation", "four-slot-source-order-and-live-status",
                "producer-deferred-entry-and-opaque-suffix", "producer-to-selection-to-final-delay",
                "initialization-and-fresh-dispatcher-reset", "shared-resource-pid-and-serial-restrictions",
                "provider-faults-are-not-progress", "producer-fault-is-not-completion",
                "admission-reentry-is-rejected", "coherent-backing-and-required-suffix"
            };
            Action[] cases = {
                CallerContextAndManual, FlagAndDefaultArm, ExperienceAndParticipation, SlotOrderAndLiveStatus,
                ProducerDeferredAndOpaque, ProducerToFinalDelay, InitializationAndFreshReset,
                SharedPidAndSerialRestrictions, ProviderFaults, ProducerFault, AdmissionReentry, CoherentBackingAndRequiredSuffix
            };
            int selected = -1;
            if (args.Length != 0)
            {
                if (args.Length != 2 || args[0] != "--case" || (selected = Array.IndexOf(names, args[1])) < 0)
                { Console.WriteLine("ERROR invalid explicit component case selector"); return 2; }
            }
            int count = 0;
            for (int i = 0; i < cases.Length; i++)
            {
                if (selected >= 0 && selected != i) continue;
                try { cases[i](); Console.WriteLine("PASS " + names[i]); }
                catch (TestFailure error) { Console.WriteLine("FAIL " + names[i] + ": " + error.Message); return 1; }
                catch (Exception error) { Console.WriteLine("ERROR " + names[i] + ": " + error.GetType().Name + ": " + error.Message); return 2; }
                count++;
            }
            Console.WriteLine("SUMMARY component_cases=" + count + " failures=0 Game=0 Unity=0");
            return 0;
        }
    }
}
