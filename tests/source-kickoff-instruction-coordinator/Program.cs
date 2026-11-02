using System;
using System.Collections.Generic;
using Blitz.Contracts;
using Blitz.Gameplay;

namespace Blitz.SourceKickoffInstructionCoordinatorComponent
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

        // Explicit component inputs/owned handles, never a native resource provider.
        private sealed class State : ISourceInstructionAdmissionState,
            ISourceInstructionInputs, ISourceInstructionEffects
        {
            internal int Flag = 0, Status = 1;
            internal readonly int[] Games = { 3, 3, 3, 3 };
            internal readonly Queue<int> StatusSamples = new Queue<int>();
            internal readonly List<string> Trace = new List<string>();
            internal Action OnStatusRead = null;
            private Plate plate = null;
            private Flasher flasher = null;

            public int ReadIdiotBoxesShown() { Trace.Add("flag:read"); return Flag; }
            public void WriteIdiotBoxesShown(int value) { Trace.Add("flag:write:" + value); Flag = value; }
            public int ReadPlayerStatus()
            {
                Trace.Add("status:read");
                if (OnStatusRead != null) OnStatusRead();
                return StatusSamples.Count == 0 ? Status : StatusSamples.Dequeue();
            }
            public int ReadGamesPlayed(int slot) { Trace.Add("games:read:" + slot); return Games[slot]; }
            public int ReadTsec() { Trace.Add("tsec:read"); return 0; }
            public int ReadIsLowResolution() { return 0; }
            public int ReadPlayerSwitches() { throw new TestFailure("zero-tsec wait must not poll switches"); }
            public int ReadShowPlyselIdiotBox() { throw new TestFailure("zero-poll wait must not read sound gate"); }
            public ISourceInstructionPlate BeginPlate(string image, string texture, SourceInstructionPosition position)
            {
                Equal("playresult", image, "unchanged real instruction plate symbol");
                Equal("SCR_PLATE_TID", texture, "unchanged real instruction texture symbol");
                plate = new Plate(); Trace.Add("plate:begin"); return plate;
            }
            private void RequirePlate(ISourceInstructionPlate value)
            { Check(plate != null && ReferenceEquals(plate, value), "owned plate return retained"); }
            public void SetPlateWidthScale(ISourceInstructionPlate value, float scale) { RequirePlate(value); }
            public void SetPlateHeightScale(ISourceInstructionPlate value, float scale) { RequirePlate(value); }
            public void SetPlateId(ISourceInstructionPlate value, string id) { RequirePlate(value); }
            public void GeneratePlateVertices(ISourceInstructionPlate value) { RequirePlate(value); }
            public void SetTextFont(SourceInstructionFont font) { Trace.Add("font:" + font); }
            public void SetStringId(int id) { Equal(0x34, id, "unchanged page string group"); }
            public void SetTextPosition(SourceInstructionPosition position) { Trace.Add("text:position"); }
            public void SetTextColor(SourceInstructionColor color) { Trace.Add("color:" + color); }
            public ISourceInstructionText Print(SourceInstructionTextRequest request)
            { Trace.Add("print:" + request.Page + ":" + request.Line); return new Text(); }
            public void StoreMessage(int index, ISourceInstructionText text)
            { Check(text != null && (index == 0 || index == 1), "actual text return stored"); }
            public ISourceInstructionFlasher CreateTextFlasher(string name, int id,
                ISourceInstructionText first, ISourceInstructionText second, int third, int fourth)
            {
                Check(first != null, "actual title return used");
                flasher = new Flasher(); return flasher;
            }
            public void CreatePlateFlasher(string name, int id, ISourceInstructionPlate value,
                int flashes, int delay, int fourth) { RequirePlate(value); }
            public void PlaySound(SourceInstructionSound sound) { Trace.Add("sound:" + sound.SoundSymbol); }
            public void DeleteStrings(int id, int mask) { Trace.Add("strings:delete"); }
            public void KillTextFlasher(ISourceInstructionFlasher value, int signal)
            { Check(ReferenceEquals(flasher, value), "owned flasher return used"); }
            public void DeletePlate(ISourceInstructionPlate value)
            { RequirePlate(value); Trace.Add("plate:delete"); plate = null; }
        }

        private sealed class Boundary : ISourceKickoffInstructionCoordinatorInputs,
            ISourceKickoffInstructionPlayerButtons
        {
            private readonly State state;
            internal int FourPlayerVersion = 0, FourReads = 0;
            internal readonly int[] Controls = { -1, -1, -1, -1 };
            internal readonly int[] Current = new int[32], Old = new int[32];
            internal readonly Dictionary<int, Queue<int>> ControlSamples = new Dictionary<int, Queue<int>>();
            internal Action OnFourRead = null;
            internal bool ThrowFour = false, ThrowCurrentWrite = false;
            internal Boundary(State state) { this.state = state; }
            public int ReadFourPlayerVersion()
            {
                state.Trace.Add("four:read"); FourReads++;
                if (OnFourRead != null) OnFourRead();
                if (ThrowFour) throw new InvalidOperationException("explicit four-player provider fault");
                return FourPlayerVersion;
            }
            public int ReadPlayerControl(int slot)
            {
                Queue<int> samples;
                int value = ControlSamples.TryGetValue(slot, out samples) && samples.Count != 0
                    ? samples.Dequeue() : Controls[slot];
                state.Trace.Add("control:" + slot + ":" + value); return value;
            }
            public int ReadCurrentButtons(int player)
            { state.Trace.Add("current:read:" + player); return Current[player]; }
            public void WriteCurrentButtons(int player, int value)
            {
                state.Trace.Add("current:write:" + player + ":" + value);
                if (ThrowCurrentWrite) throw new InvalidOperationException("explicit button provider fault");
                Current[player] = value;
            }
            public int ReadOldButtons(int player)
            { state.Trace.Add("old:read:" + player); return Old[player]; }
            public void WriteOldButtons(int player, int value)
            { state.Trace.Add("old:write:" + player + ":" + value); Old[player] = value; }
        }

        private enum ChildBehavior
        { Retire, SleepThenRetire, SleepThenFallthrough, Null, ThrowCreate, ThrowAdvance, Invalid }

        // Deliberately synthetic cooperative child bodies. Their observable sleep/
        // explicit lifecycle is exercised; none purports to implement native child UI.
        private sealed class Children : ISourceKickoffInstructionChildContinuationFactory
        {
            private readonly Rig rig;
            internal ChildBehavior Behavior = ChildBehavior.Retire;
            internal int Advances = 0;
            internal readonly List<SourceKickoffInstructionChildInvocation> Invocations =
                new List<SourceKickoffInstructionChildInvocation>();
            internal Children(Rig rig) { this.rig = rig; }
            public ISourceKickoffInstructionChildContinuation Create(SourceKickoffInstructionChildInvocation invocation)
            {
                Equal(invocation.Serial, rig.Host.Caller, "child factory runs only inside actual child wake");
                rig.State.Trace.Add("child:create:" + invocation.Argument0);
                Invocations.Add(invocation);
                if (Behavior == ChildBehavior.ThrowCreate) throw new InvalidOperationException("explicit child factory fault");
                if (Behavior == ChildBehavior.Null) return null;
                return new Child(this, invocation);
            }
            private sealed class Child : ISourceKickoffInstructionChildContinuation
            {
                private readonly Children owner;
                private readonly SourceKickoffInstructionChildInvocation invocation;
                private bool first = true;
                internal Child(Children owner, SourceKickoffInstructionChildInvocation invocation)
                { this.owner = owner; this.invocation = invocation; }
                public SourceKickoffInstructionChildStep Advance()
                {
                    owner.Advances++; owner.rig.State.Trace.Add("child:advance:" + invocation.Argument0);
                    if (owner.Behavior == ChildBehavior.ThrowAdvance)
                        throw new InvalidOperationException("explicit child continuation fault");
                    if (owner.Behavior == ChildBehavior.Invalid) return default(SourceKickoffInstructionChildStep);
                    if (first && (owner.Behavior == ChildBehavior.SleepThenRetire
                        || owner.Behavior == ChildBehavior.SleepThenFallthrough))
                    { first = false; return SourceKickoffInstructionChildStep.Sleep(2); }
                    return owner.Behavior == ChildBehavior.SleepThenFallthrough
                        ? SourceKickoffInstructionChildStep.ExternalFallthrough()
                        : SourceKickoffInstructionChildStep.ExternalRetirement();
                }
            }
        }

        private sealed class LegacySuffix : ISourceKickoffInstructionContinuationFactory,
            ISourceKickoffInstructionContinuation
        {
            private readonly State state;
            internal int Created = 0, Advances = 0;
            internal LegacySuffix(State state) { this.state = state; }
            public ISourceKickoffInstructionContinuation Create()
            {
                Equal(1, state.Flag, "legacy producer flag precedes suffix factory");
                Created++; return this;
            }
            public SourceKickoffInstructionStep Advance()
            {
                return Advances++ == 0 ? SourceKickoffInstructionStep.Sleep(1)
                    : SourceKickoffInstructionStep.ExternalFallthrough();
            }
        }

        private sealed class Rig
        {
            // These supplied component operands do NOT establish native constants.
            internal const int ChildId = 0x6b42, InstructionId = 0x6b43;
            internal const int PostId = 0x0502, CameraId = 0x1400, A = 0x10, B = 0x20;
            internal readonly SourcePortableProcessHost Host = new SourcePortableProcessHost();
            internal readonly State State = new State();
            internal readonly Boundary Boundary;
            internal readonly Children Children;
            internal readonly LegacySuffix Legacy;
            internal readonly SourceInstructionInvocation Invocation;
            internal readonly SourceKickoffInstructionCoordinator Config;
            internal readonly SourceInstructionAdmissionBinding Binding;
            internal readonly SourceInstructionDispatcher Dispatcher;
            internal readonly List<long> Commits = new List<long>();
            internal long Serial = 32;
            internal Func<long> OverrideNext = null;
            internal Action<long> OnCommit = null;

            internal Rig(bool coordinator = true, int version = 0, int maximumPlayers = 4)
            {
                Host.Create(1, 0x10, "game_proc", 0);
                Host.Create(2, 0x500, "player_proc", 0);
                Host.Create(20, PostId, "last_player_proc", 0);
                Host.Create(17, CameraId, "cambot_proc", 0);
                Boundary = new Boundary(State) { FourPlayerVersion = version };
                Children = new Children(this); Legacy = new LegacySuffix(State);
                Invocation = new SourceInstructionInvocation(4,
                    new SourceInstructionProcessIds(InstructionId, PostId, CameraId, 0x6b44), State, State);
                Config = new SourceKickoffInstructionCoordinator(ChildId, maximumPlayers, A, B, Boundary, Boundary, Children);
                Binding = coordinator ? SourceInstructionAdmissionBinding.WithKickoffCoordinator(Invocation, State, Config)
                    : new SourceInstructionAdmissionBinding(Invocation, State, Legacy);
                Dispatcher = new SourceInstructionDispatcher(Host);
                if (coordinator) Dispatcher.InitializeAdmission(Binding, Next, Commit);
                else Dispatcher.InitializeAdmission(Binding);
                State.Trace.Clear();
            }

            internal long Next()
            {
                long value = OverrideNext == null ? checked(Serial + 1) : OverrideNext();
                State.Trace.Add("serial:next:" + value); return value;
            }
            internal void Commit(long serial)
            {
                Serial = serial; Commits.Add(serial); State.Trace.Add("serial:commit:" + serial);
                if (OnCommit != null) OnCommit(serial);
            }
            internal long? Within(long caller, Func<long?> action)
            {
                Host.BeginTick(Host.SourceTick + 1);
                Check(Host.Visit(caller), "actual declared caller wakes");
                try
                {
                    long? serial = action();
                    if (serial.HasValue) Commit(serial.Value);
                    return serial;
                }
                finally { Host.Complete(); }
            }
            internal long Catch()
            {
                long? serial = Within(2, () => Dispatcher.AdmitKickoffFromCatch(Binding, true, 2, Next));
                Check(serial.HasValue, "actual kickoff producer created"); return serial.Value;
            }
            internal long Select()
            {
                long? serial = Within(1, () => Dispatcher.AdmitAtPlaySelectionReturn(Binding, true, 1, Next));
                Check(serial.HasValue, "actual source-ordered instruction admitted"); return serial.Value;
            }
            internal void DispatchThisTick(long serial)
            { Check(Dispatcher.Dispatch(serial, f => State.Trace.Add("begin:" + f)), "actual dispatcher owns serial"); }
            internal void Tick(params long[] serials)
            {
                Host.BeginTick(Host.SourceTick + 1);
                foreach (long serial in serials) DispatchThisTick(serial);
                Equal(0L, Host.Caller, "actual host caller released after continuation");
            }
            internal List<long> ChildCreates()
            {
                var result = new List<long>();
                foreach (SourcePossessionProcessRecord record in Host.Records)
                    if (record.Operation == "Create" && Dispatcher.OwnsKickoffChild(record.Serial)) result.Add(record.Serial);
                return result;
            }
            internal void KillChildren(IEnumerable<long> serials)
            { foreach (long serial in serials) Host.Kill(serial); }
        }

        private static int RecordCount(Rig rig, string operation)
        {
            int count = 0;
            foreach (SourcePossessionProcessRecord record in rig.Host.Records)
                if (record.Operation == operation) count++;
            return count;
        }

        private static SourcePossessionProcessRecord SingleRecord(Rig rig, string operation)
        {
            Equal(1, RecordCount(rig, operation), "one actual host record: " + operation);
            foreach (SourcePossessionProcessRecord record in rig.Host.Records)
                if (record.Operation == operation) return record;
            throw new TestFailure("missing host record: " + operation);
        }

        private static void CreationAndDeferredEntry()
        {
            var rig = new Rig();
            long parent = rig.Catch();
            Equal(0, rig.State.Flag, "catch creation must not reserve flag 1");
            Equal(0, rig.ChildCreates().Count, "producer creation performs no inline child work");
            Equal(0, rig.Children.Invocations.Count, "producer creation performs no inline child factory");
            Equal(0, rig.Boundary.FourReads, "producer creation does not cache four-player input");
            Equal(0, rig.Host.Require(parent).Id, "parent creation keeps literal PID zero");
            rig.DispatchThisTick(parent);
            Equal(0, rig.State.Flag, "birth-tick dispatch cannot execute parent inline");
            rig.State.Trace.Clear(); rig.Tick(parent);
            Equal(1, rig.State.Flag, "parent entry publishes exactly 1");
            Check(rig.State.Trace.IndexOf("flag:write:1") < rig.State.Trace.IndexOf("serial:next:34"),
                "parent flag write precedes first child creation");
            Equal(0, rig.Children.Invocations.Count, "parent wake does not inline child body");
            Equal(0, rig.Legacy.Created, "complete coordinator does not delegate to whole suffix");
            Equal(1, SingleRecord(rig, "Sleep").Value, "existence wait requests exactly sleep 1");
        }

        private static void VersionReadOrder()
        {
            foreach (int version in new[] { 0, 1, -7 })
            {
                var rig = new Rig(version: 0, maximumPlayers: version == 0 ? 4 : 2);
                long parent = rig.Catch();
                rig.OnCommit = serial => { if (serial == 35) rig.Boundary.FourPlayerVersion = version; };
                rig.Boundary.OnFourRead = () =>
                {
                    Equal(2, rig.ChildCreates().Count, "four-player read follows first two child creation attempts");
                    Equal(parent, rig.Host.Caller, "version getter remains in parent wake");
                    Equal(0, rig.Children.Invocations.Count, "version read does not require child execution");
                };
                rig.Tick(parent);
                Equal(version == 0 ? 2 : 4, rig.ChildCreates().Count, "live nonzero four-player branch");
                Equal(1, rig.Boundary.FourReads, "one version read after initial two children");
                rig.Boundary.FourPlayerVersion = version == 0 ? 1 : 0;
                rig.Tick(parent);
                Equal(1, rig.Boundary.FourReads, "returned sleep polls existence, not version or spawn block");
                Equal(0, rig.ChildCreates().Count, "returned sleep never respawns children");
            }
        }

        private static void ChildShapeAndOrder()
        {
            var rig = new Rig(version: 1);
            long parent = rig.Catch(); rig.Tick(parent);
            List<long> children = rig.ChildCreates();
            Equal(4, children.Count, "four source child attempts");
            for (int slot = 0; slot < children.Count; slot++)
            {
                long serial = children[slot];
                var invocation = rig.Dispatcher.KickoffChildInvocation(serial);
                Equal(34L + slot, serial, "child creation serial order");
                Equal(slot, invocation.Argument0, "child slot source order");
                Equal(0, invocation.Argument1 | invocation.Argument2 | invocation.Argument3, "child trailing arguments are all zero");
                Equal("idiots", invocation.Name, "source child qcreate name");
                Equal(Rig.ChildId, invocation.ProcessId, "explicit child PID operand");
                Equal(serial, invocation.Serial, "child identity shares actual host serial");
                Equal(Rig.ChildId, rig.Host.Require(serial).Id, "actual host uses child PID");
                Equal(SourceInstructionDispatcher.KickoffChildFunction, rig.Host.Require(serial).Function,
                    "actual child message function");
            }
            rig.Tick(children.ToArray());
            Equal(4, rig.Children.Invocations.Count, "fresh per-child continuations actually dispatched");
            for (int slot = 0; slot < children.Count; slot++)
                Check(ReferenceEquals(rig.Children.Invocations[slot], rig.Dispatcher.KickoffChildInvocation(children[slot])),
                    "factory receives exact retained child invocation");
            Equal(4, rig.Children.Advances, "one real external advance per child");
        }

        private static void SuspendAndSpecificResume()
        {
            var rig = new Rig();
            rig.Host.Create(80, Rig.ChildId, "foreign_matching_child", 0);
            rig.Host.Create(81, 0x8001, "protected_high_bit", 0);
            rig.Host.Create(82, Rig.ChildId ^ 1, "foreign_near_pid", 0);
            long parent = rig.Catch(); rig.Tick(parent);
            Check(rig.Host.Require(1).Suspended && rig.Host.Require(2).Suspended,
                "parent suspends ordinary matching processes");
            Check(rig.Host.Require(82).Suspended, "specific child resume must not resume other PIDs");
            Check(!rig.Host.Require(81).Suspended, "suspend predicate preserves high-bit nonmatches");
            Check(!rig.Host.Require(80).Suspended, "specific resume includes foreign exact-PID process");
            Check(!rig.Host.Require(parent).Suspended, "suspension excludes actual parent caller");
            foreach (long child in rig.ChildCreates())
                Check(!rig.Host.Require(child).Suspended, "created child is specifically resumed");
            var suspend = SingleRecord(rig, "SuspendMask");
            Equal(0, suspend.Id, "source suspend ID operand");
            Equal(32768, suspend.Value, "source suspend mask operand");
            var resume = SingleRecord(rig, "ResumeMask");
            Equal(Rig.ChildId, resume.Id, "source resume child PID operand");
            Equal(-1, resume.Value, "source resume full mask operand");
            var operations = new List<string>();
            foreach (SourcePossessionProcessRecord record in rig.Host.Records)
                if (record.Operation == "SuspendMask" || record.Operation == "ResumeMask"
                    || record.Operation == "ExistsCall" || record.Operation == "Sleep")
                    operations.Add(record.Operation);
            Equal("SuspendMask|ResumeMask|ExistsCall|Sleep", string.Join("|", operations),
                "exact parent control/poll/sleep source order");
            Equal(0, RecordCount(rig, "ResumeAllCall"), "no early resume-all while children exist");
        }

        private static void ExistenceIncludesForeign()
        {
            var rig = new Rig();
            rig.Host.Create(80, Rig.ChildId, "foreign_matching_child", 0);
            rig.Host.Create(81, Rig.ChildId ^ 1, "foreign_near_pid", 0);
            long parent = rig.Catch(); rig.Tick(parent);
            List<long> children = rig.ChildCreates(); rig.Tick(children.ToArray());
            foreach (long child in children) Check(!rig.Host.Contains(child), "explicit external child retirement");
            rig.Tick(parent);
            Equal(1, RecordCount(rig, "Sleep"), "foreign matching live PID keeps parent waiting");
            Equal(0, RecordCount(rig, "ResumeAllCall"), "owned-child exhaustion is not absence");
            rig.Host.Kill(80); rig.Tick(parent);
            Equal(1, RecordCount(rig, "ResumeAllCall"), "full-PID existence excludes unrelated live neighbors");
            Equal(1, RecordCount(rig, "Fallthrough"), "absence reaches only proven parent fallthrough");
            Check(rig.Host.Contains(81), "unrelated process still alive at parent fallthrough");
        }

        private static void SuspendedAliveAndDead()
        {
            var host = new SourcePortableProcessHost();
            host.Create(1, Rig.ChildId, "suspended", 0, 0x8001);
            host.Create(2, Rig.ChildId ^ 1, "near", 0);
            Check(host.Exists(Rig.ChildId, -1), "actual host existence includes suspended alive");
            host.Kill(1);
            Check(!host.Exists(Rig.ChildId, -1), "actual host full-PID query excludes dead and neighboring PID");
            host.Create(3, Rig.ChildId, "zero_sleep_alive", 0, 0);
            Check(host.Exists(Rig.ChildId, -1), "actual host existence is not runnable/sleep-positive test");
            host.Kill(3);
            Check(!host.Exists(Rig.ChildId, -1), "dead zero-sleep process no longer exists");
            var rig = new Rig();
            long parent = rig.Catch(); rig.Tick(parent);
            List<long> children = rig.ChildCreates(); rig.KillChildren(children);
            rig.Host.Create(80, Rig.ChildId, "late_suspended_foreign", 0, 0x8001);
            rig.Tick(parent);
            Equal(1, RecordCount(rig, "Sleep"), "suspended foreign live PID prevents epilogue");
            Check(rig.Host.Require(80).Suspended, "polling does not silently resume foreign child");
            rig.Host.Kill(80); rig.Tick(parent);
            Equal(1, RecordCount(rig, "Fallthrough"), "dead matching PID releases existence loop");
        }

        private static void SleepReturnAndEpilogue()
        {
            var rig = new Rig();
            long parent = rig.Catch(); rig.Tick(parent);
            List<long> children = rig.ChildCreates();
            Equal(-1, SingleRecord(rig, "ExistsCall").Value, "existence uses all-ones mask");
            Equal(1, SingleRecord(rig, "Sleep").Value, "parent returns from exactly one sleep tick");
            rig.KillChildren(children);
            Check(rig.Host.Require(1).Suspended, "last child disappearance does not inline parent epilogue");
            Equal(0, RecordCount(rig, "ResumeAllCall"), "no epilogue before sleep returns");
            rig.State.OnStatusRead = () => Check(!rig.Host.Require(1).Suspended,
                "resume-all precedes first epilogue status read");
            rig.Tick(parent);
            Equal(1, RecordCount(rig, "ExistsCall"), "one repoll on returned sleep");
            Equal(0, RecordCount(rig, "Sleep"), "false existence exits loop without another sleep");
            Equal(1, RecordCount(rig, "ResumeAllCall"), "one authenticated epilogue resume-all");
            Equal(1, RecordCount(rig, "Fallthrough"), "parent falls through after epilogue");
            Equal(1, rig.State.Flag, "parent epilogue must not invent completion flag 2");
        }

        private static void EpilogueLiveReadsAndButtons()
        {
            var rig = new Rig();
            long parent = rig.Catch(); rig.Tick(parent);
            rig.KillChildren(rig.ChildCreates());
            foreach (int status in new[] { 0, 2, 4, 0 }) rig.State.StatusSamples.Enqueue(status);
            rig.Boundary.Controls[1] = -1;
            rig.Boundary.ControlSamples[2] = new Queue<int>(new[] { 5, 6, 7 });
            rig.Boundary.Current[6] = 0x81; rig.Boundary.Old[7] = 0x42;
            rig.State.Trace.Clear(); rig.Tick(parent);
            Equal(0x81 | Rig.A | Rig.B, rig.Boundary.Current[6], "epilogue OR preserves current button bits");
            Equal(0x42 | Rig.A | Rig.B, rig.Boundary.Old[7], "epilogue OR preserves old button bits");
            Equal(0, rig.Boundary.Current[5], "condition control index must not be cached for current write");
            Equal(0, rig.Boundary.Old[6], "old write must reread live control index");
            Equal("begin:ko_idiot_box_proc|status:read|status:read|control:1:-1|status:read|control:2:5|control:2:6|current:read:6|current:write:6:177|control:2:7|old:read:7|old:write:7:114|status:read",
                string.Join("|", rig.State.Trace), "epilogue source short-circuit/live-read/current-before-old order");
        }

        private static void ChildExternalLifecycle()
        {
            var rig = new Rig(); rig.Children.Behavior = ChildBehavior.SleepThenFallthrough;
            long parent = rig.Catch(); rig.Tick(parent);
            List<long> children = rig.ChildCreates(); long child = children[0];
            rig.Tick(child);
            Equal(2, rig.Host.Require(child).SleepRemaining, "child supplied sleep is preserved");
            rig.Tick(child); Equal(1, rig.Children.Advances, "child sleep has not returned early");
            rig.Tick(child); Equal(2, rig.Children.Advances, "child continuation advances only after returned sleep");
            Check(rig.Host.Contains(child), "external child fallthrough must not auto-retire");
            rig.Tick(child); Equal(2, rig.Children.Advances, "returned child body is not replayed");
            rig.Tick(parent);
            Equal(1, RecordCount(rig, "Sleep"), "returned-but-live child still satisfies parent existence");
            rig.Host.Kill(child);
            rig.Children.Behavior = ChildBehavior.Retire;
            rig.Tick(children[1]);
            Check(!rig.Host.Contains(children[1]), "independent child body may explicitly request retirement");
            rig.Tick(parent);
            Equal(1, RecordCount(rig, "Fallthrough"), "explicit child deaths release parent");
        }

        private static void RequiredConfigurationAndAllocator()
        {
            var rig = new Rig();
            RejectArgument(() => new SourceKickoffInstructionCoordinator(Rig.ChildId, 4, Rig.A, Rig.B, null, rig.Boundary, rig.Children),
                "four-player/control provider is mandatory");
            RejectArgument(() => new SourceKickoffInstructionCoordinator(Rig.ChildId, 4, Rig.A, Rig.B, rig.Boundary, null, rig.Children),
                "epilogue button provider is mandatory");
            RejectArgument(() => new SourceKickoffInstructionCoordinator(Rig.ChildId, 4, Rig.A, Rig.B, rig.Boundary, rig.Boundary, null),
                "real per-child continuation factory is mandatory");
            foreach (int maximum in new[] { 0, 32 })
                RejectArgument(() => new SourceKickoffInstructionCoordinator(Rig.ChildId, maximum, Rig.A, Rig.B, rig.Boundary, rig.Boundary, rig.Children),
                    "explicit portable maximum-player domain, not actor count");
            RejectArgument(() => new SourceKickoffInstructionCoordinator(Rig.ChildId, 4, 0, Rig.B, rig.Boundary, rig.Boundary, rig.Children),
                "explicit A mask is mandatory");
            RejectArgument(() => new SourceKickoffInstructionCoordinator(Rig.ChildId, 4, Rig.A, 0, rig.Boundary, rig.Boundary, rig.Children),
                "explicit B mask is mandatory");
            RejectArgument(() => SourceInstructionAdmissionBinding.WithKickoffCoordinator(rig.Invocation, rig.State, null),
                "no implicit complete-coordinator configuration");
            RejectArgument(() => SourceInstructionAdmissionBinding.WithKickoffCoordinator(rig.Invocation, new State(), rig.Config),
                "complete coordinator keeps coherent flag/effect identity");
            var missing = new SourceInstructionDispatcher(new SourcePortableProcessHost());
            RejectArgument(() => missing.InitializeAdmission(rig.Binding), "no private/default child serial namespace");
            Equal(0L, missing.LastKickoffSerial, "missing allocator creates no producer");
            RejectArgument(() => SourceKickoffInstructionChildStep.Sleep(0), "child cannot invent default sleep");
            RejectArgument(() => SourceKickoffInstructionChildStep.Sleep(0x8000), "child sleep preserves host portable range");
        }

        private static void ChildFaults()
        {
            foreach (ChildBehavior behavior in new[] {
                ChildBehavior.Null, ChildBehavior.ThrowCreate, ChildBehavior.ThrowAdvance, ChildBehavior.Invalid })
            {
                var rig = new Rig(); rig.Children.Behavior = behavior;
                long parent = rig.Catch(); rig.Tick(parent);
                long child = rig.ChildCreates()[0];
                RejectState(() => rig.Tick(child), "required child callback fault propagates");
                int creates = rig.Children.Invocations.Count, advances = rig.Children.Advances;
                RejectState(() => rig.Tick(child), "failed child cannot replay");
                Equal(creates, rig.Children.Invocations.Count, "fault does not recreate child continuation");
                Equal(advances, rig.Children.Advances, "fault does not advance child again");
                rig.Host.Kill(child);
                RejectState(() => rig.Tick(parent), "child fault cannot become successful parent progress even after death");
                Equal(0, RecordCount(rig, "ResumeAllCall"), "child fault is not epilogue completion");
                Equal(1, rig.State.Flag, "child fault does not fabricate flag completion");
                RejectState(() => rig.Dispatcher.Admit(90, rig.Invocation), "child fault protects later shared admission");
            }
        }

        private static void ParentProviderAndSerialFaults()
        {
            var version = new Rig(); version.Boundary.ThrowFour = true;
            long parent = version.Catch();
            RejectState(() => version.Tick(parent), "live four-player provider failure propagates");
            Equal(2, version.ChildCreates().Count, "four-player failure retains earlier source attempts without rollback");
            Equal(0, RecordCount(version, "SuspendMask"), "failed version read cannot skip ahead to suspension");
            RejectState(() => version.Tick(parent), "failed parent cannot replay child creations");
            Equal(0L, version.Host.Caller, "failed parent releases actual caller");

            var collision = new Rig(); long collidingParent = collision.Catch();
            collision.OverrideNext = () => 34;
            RejectArgument(() => collision.Tick(collidingParent), "second-child serial collision is a host error, not progress");
            Equal(1, collision.ChildCreates().Count, "successful first child remains on later creation failure");
            Equal(34L, collision.Serial, "only successful child creation commits shared serial");
            Equal(0, collision.Boundary.FourReads, "second attempt failure occurs before version read");
            RejectState(() => collision.Tick(collidingParent), "failed creation does not retry suffix");

            var buttons = new Rig(); buttons.Boundary.Controls[0] = 3; buttons.Boundary.ThrowCurrentWrite = true;
            long epilogue = buttons.Catch(); buttons.Tick(epilogue);
            buttons.KillChildren(buttons.ChildCreates());
            RejectState(() => buttons.Tick(epilogue), "epilogue effect failure propagates after resume-all");
            Equal(1, RecordCount(buttons, "ResumeAllCall"), "epilogue effects follow actual resume-all attempt");
            Equal(0, RecordCount(buttons, "Fallthrough"), "failed epilogue is not source fallthrough");
            int traceCount = buttons.State.Trace.Count;
            RejectState(() => buttons.Tick(epilogue), "failed epilogue is never replayed");
            Equal(traceCount, buttons.State.Trace.Count, "partial epilogue effects are not silently repeated");
        }

        private static void SharedSerialInterleaving()
        {
            var rig = new Rig();
            long parent = rig.Catch();
            long foreign = rig.Next(); rig.Host.Create(foreign, 0x8100, "other_global_creation", 0); rig.Commit(foreign);
            try { rig.Tick(parent); }
            catch (TestFailure) { throw; }
            catch (Exception error)
            { throw new TestFailure("shared serial coordinator child allocation succeeds: " + error.GetType().Name); }
            List<long> children = rig.ChildCreates();
            Equal("35|36", string.Join("|", children), "parent children allocate in actual shared namespace");
            long later = rig.Next(); rig.Host.Create(later, 0x8101, "later_global_creation", 0); rig.Commit(later);
            Equal(37L, later, "child commits advance shared namespace for other producers");
            rig.KillChildren(children); rig.Tick(parent);
            rig.State.Games[0] = 2;
            long instruction = rig.Select();
            Equal(38L, instruction, "selection admission shares parent/child/global serial namespace");
            var all = new HashSet<long>();
            foreach (SourceProcessView process in rig.Host.Processes)
                Check(all.Add(process.Serial), "actual host serials remain unique");
        }

        private static void CoordinatorToInstructionFinalDelay()
        {
            var rig = new Rig(); rig.State.Games[0] = 2;
            long parent = rig.Catch(); rig.Tick(parent);
            List<long> children = rig.ChildCreates(); rig.Tick(children.ToArray()); rig.Tick(parent);
            Equal(1, rig.State.Flag, "complete parent leaves admission flag 1, not 2");
            long instruction = rig.Select();
            Equal(1, rig.State.Flag, "selection does not inline instruction entry");
            rig.Tick(instruction);
            Equal(-1, rig.State.Flag, "actual instruction entry consumes parent-produced flag");
            int bounded = 0;
            while (rig.Dispatcher.Snapshot(instruction).Stage != SourceInstructionStage.FinalDelay && bounded++ < 100)
                rig.Tick(instruction);
            Equal(SourceInstructionStage.FinalDelay, rig.Dispatcher.Snapshot(instruction).Stage, "unchanged real final delay reached");
            for (int i = 0; i < 9; i++)
            { rig.Tick(instruction); Equal(-1, rig.State.Flag, "final delay has not returned"); }
            rig.Tick(instruction);
            Equal(2, rig.State.Flag, "only unchanged instruction final sleep return publishes 2");
            Equal(SourceInstructionStage.Fallthrough, rig.Dispatcher.Snapshot(instruction).Stage, "instruction source fallthrough");
        }

        private static void LegacyAndManualCompatibility()
        {
            var legacy = new Rig(coordinator: false);
            long parent = legacy.Catch(); legacy.Tick(parent);
            Equal(1, legacy.Legacy.Created, "explicit legacy whole-suffix factory remains supported");
            Equal(0, legacy.ChildCreates().Count, "legacy binding does not opt into child coordinator");
            legacy.Tick(parent); legacy.Tick(parent);
            Equal(2, legacy.Legacy.Advances, "legacy suffix return is not replayed");
            Check(legacy.Host.Contains(parent), "legacy suffix fallthrough does not imply native retirement");
            Equal(1, legacy.State.Flag, "legacy suffix keeps its existing flag behavior");
            var manual = new Rig(coordinator: false);
            manual.Dispatcher.Admit(90, manual.Invocation);
            Equal(SourceInstructionStage.PendingEntry, manual.Dispatcher.Snapshot(90).Stage, "legacy manual admission remains deferred");
            manual.Tick(90); Equal(-1, manual.State.Flag, "legacy manual real instruction entry");
            RejectArgument(() => new SourceInstructionAdmissionBinding(manual.Invocation, manual.State, null),
                "legacy null suffix remains rejected without overload ambiguity");
            var unbound = new SourceInstructionDispatcher(manual.Host);
            Check(!unbound.Dispatch(999, f => { throw new TestFailure("unbound unknown dispatch is inert"); }),
                "unbound dispatcher creates no implicit programs");
        }

        private static void ParentFallthroughAndReset()
        {
            var rig = new Rig();
            long parent = rig.Catch(); rig.Tick(parent); rig.KillChildren(rig.ChildCreates()); rig.Tick(parent);
            Check(rig.Host.Contains(parent), "parent source fallthrough does not prove native stack retirement");
            int fourReads = rig.Boundary.FourReads;
            rig.State.Trace.Clear(); rig.Tick(parent);
            Equal(0, rig.State.Trace.Count, "returned parent body cannot execute epilogue again");
            Equal(0, rig.Host.Records.Count, "returned parent is parked without host wake or synthetic die");
            Equal(fourReads, rig.Boundary.FourReads, "returned parent never rereads version");
            rig.State.Status = 0;
            var reset = new SourceInstructionDispatcher(new SourcePortableProcessHost());
            int commits = rig.Commits.Count;
            reset.InitializeAdmission(rig.Binding, rig.Next, rig.Commit);
            Equal(2, rig.State.Flag, "fresh coordinator dispatcher initializes from live participation");
            Equal(commits, rig.Commits.Count, "reset does not allocate children");
            Equal(0L, reset.LastKickoffSerial, "reset is not producer creation");
        }

        private static int Main(string[] args)
        {
            string[] names = {
                "creation-and-deferred-entry", "version-read-after-two-attempts", "child-shape-and-order",
                "suspend-and-specific-resume", "existence-includes-foreign-live-pid", "suspended-alive-and-dead",
                "sleep-return-and-epilogue", "epilogue-live-reads-and-buttons", "child-explicit-external-lifecycle",
                "required-configuration-and-allocator", "child-callback-faults", "parent-provider-and-serial-faults",
                "shared-global-serial-interleaving", "coordinator-to-instruction-final-delay",
                "legacy-external-suffix-and-manual", "parent-fallthrough-not-native-retirement-and-reset"
            };
            Action[] cases = {
                CreationAndDeferredEntry, VersionReadOrder, ChildShapeAndOrder, SuspendAndSpecificResume,
                ExistenceIncludesForeign, SuspendedAliveAndDead, SleepReturnAndEpilogue, EpilogueLiveReadsAndButtons,
                ChildExternalLifecycle, RequiredConfigurationAndAllocator, ChildFaults, ParentProviderAndSerialFaults,
                SharedSerialInterleaving, CoordinatorToInstructionFinalDelay, LegacyAndManualCompatibility,
                ParentFallthroughAndReset
            };
            int selected = -1;
            if (args.Length != 0 && (args.Length != 2 || args[0] != "--case"
                || (selected = Array.IndexOf(names, args[1])) < 0))
            { Console.WriteLine("ERROR invalid explicit case selector"); return 2; }
            int count = 0;
            for (int i = 0; i < cases.Length; i++)
            {
                if (selected >= 0 && selected != i) continue;
                try { cases[i](); Console.WriteLine("PASS " + names[i]); }
                catch (TestFailure error) { Console.WriteLine("FAIL " + names[i] + ": " + error.Message); return 1; }
                catch (Exception error)
                { Console.WriteLine("ERROR " + names[i] + ": " + error.GetType().Name + ": " + error.Message); return 2; }
                count++;
            }
            Console.WriteLine("SUMMARY component_cases=" + count + " failures=0 Game=0 Unity=0 native=0");
            return 0;
        }
    }
}
