using System;
using System.Collections.Generic;
using Blitz.Contracts;
using Blitz.Gameplay;

namespace Blitz.SourceKickoffInstructionChildComponent
{
    internal static class Program
    {
        private sealed class TestFailure : Exception
        { internal TestFailure(string message) : base(message) { } }

        private static int assertions;
        private static void Check(bool condition, string causal)
        {
            assertions++;
            if (!condition) throw new TestFailure(causal);
        }
        private static void Equal<T>(T expected, T actual, string causal)
        {
            assertions++;
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
                throw new TestFailure(causal + ": expected " + expected + ", got " + actual);
        }
        private static T Throws<T>(Action action, string causal) where T : Exception
        {
            assertions++;
            try { action(); }
            catch (T error) { return error; }
            catch (Exception error)
            { throw new TestFailure(causal + ": expected " + typeof(T).Name + ", got " + error.GetType().Name); }
            throw new TestFailure(causal + ": expected " + typeof(T).Name + ", no exception");
        }

        // All services below are explicitly synthetic component-bound fixtures.
        // They supply scripts and symbolic handles, not Game, Unity or native providers.
        private sealed class Tape
        {
            internal readonly List<string> Entries = new List<string>();
            internal Action<string> OnCall;
            private string failure;
            private int occurrence, matches;
            internal void FailAt(string operation, int at = 1)
            { failure = operation; occurrence = at; matches = 0; }
            internal void Add(string operation)
            {
                Entries.Add(operation);
                if (OnCall != null) OnCall(operation);
                if (operation == failure && ++matches == occurrence)
                    throw new InvalidOperationException("synthetic component fault at " + operation);
            }
            internal void Clear() { Entries.Clear(); }
            internal int Count(string prefix)
            {
                int result = 0;
                foreach (string entry in Entries)
                    if (entry.StartsWith(prefix, StringComparison.Ordinal)) result++;
                return result;
            }
            internal string Joined { get { return string.Join("|", Entries); } }
        }

        private sealed class State : ISourceInstructionAdmissionState, ISourceInstructionInputs,
            ISourceInstructionEffects
        {
            private readonly Tape tape;
            internal int Flag, Status;
            internal readonly int[] Games = { 0, 0, 0, 0 };
            internal readonly Queue<int> StatusSamples = new Queue<int>();
            internal readonly List<int> FlagWrites = new List<int>();
            internal readonly List<int> GameReads = new List<int>();
            internal State(Tape tape, int status) { this.tape = tape; Status = status; }
            public int ReadIdiotBoxesShown() { tape.Add("flag:read"); return Flag; }
            public void WriteIdiotBoxesShown(int value)
            { tape.Add("flag:write:" + value); Flag = value; FlagWrites.Add(value); }
            public int ReadPlayerStatus()
            {
                int value = StatusSamples.Count == 0 ? Status : StatusSamples.Dequeue();
                tape.Add("read:status:" + value); return value;
            }
            public int ReadGamesPlayed(int slot)
            { int value = Games[slot]; GameReads.Add(slot); tape.Add("read:games:" + slot + ":" + value); return value; }
            private static TestFailure Unadmitted()
            { return new TestFailure("child must not use the frozen page/global-any-button fixture"); }
            public int ReadTsec() { throw Unadmitted(); }
            public int ReadIsLowResolution() { throw Unadmitted(); }
            public int ReadPlayerSwitches() { throw Unadmitted(); }
            public int ReadShowPlyselIdiotBox() { throw Unadmitted(); }
            public ISourceInstructionPlate BeginPlate(string image, string texture, SourceInstructionPosition point)
            { throw Unadmitted(); }
            public void SetPlateWidthScale(ISourceInstructionPlate plate, float value) { throw Unadmitted(); }
            public void SetPlateHeightScale(ISourceInstructionPlate plate, float value) { throw Unadmitted(); }
            public void SetPlateId(ISourceInstructionPlate plate, string id) { throw Unadmitted(); }
            public void GeneratePlateVertices(ISourceInstructionPlate plate) { throw Unadmitted(); }
            public void SetTextFont(SourceInstructionFont font) { throw Unadmitted(); }
            public void SetStringId(int id) { throw Unadmitted(); }
            public void SetTextPosition(SourceInstructionPosition position) { throw Unadmitted(); }
            public void SetTextColor(SourceInstructionColor color) { throw Unadmitted(); }
            public ISourceInstructionText Print(SourceInstructionTextRequest request) { throw Unadmitted(); }
            public void StoreMessage(int index, ISourceInstructionText text) { throw Unadmitted(); }
            public ISourceInstructionFlasher CreateTextFlasher(string name, int processId,
                ISourceInstructionText title, ISourceInstructionText second, int third, int fourth)
            { throw Unadmitted(); }
            public void CreatePlateFlasher(string name, int processId, ISourceInstructionPlate plate,
                int flashes, int delay, int fourth) { throw Unadmitted(); }
            public void PlaySound(SourceInstructionSound sound) { throw Unadmitted(); }
            public void DeleteStrings(int id, int mask) { throw Unadmitted(); }
            public void KillTextFlasher(ISourceInstructionFlasher flasher, int signal) { throw Unadmitted(); }
            public void DeletePlate(ISourceInstructionPlate plate) { throw Unadmitted(); }
        }

        private sealed class Inputs : ISourceKickoffInstructionChildInputs, ISourceKickoffInstructionPlayerButtons
        {
            private readonly Tape tape;
            internal int Version, Carrier, OffensiveSide, LowResolution = 0, Tsec = 7;
            internal readonly int[] Controls = { 0, 1, 2, 3 };
            internal readonly int[] Current = new int[32], Old = new int[32];
            internal readonly Queue<int> Versions = new Queue<int>(), Lows = new Queue<int>();
            internal readonly Dictionary<int, Queue<int>> ControlSamples = new Dictionary<int, Queue<int>>();
            internal readonly Dictionary<int, int> Teams = new Dictionary<int, int>();
            internal Inputs(Tape tape, int version)
            {
                this.tape = tape; Version = version;
                for (int player = 0; player < 32; player++) Teams.Add(player, player < 2 ? 0 : 1);
            }
            public int ReadFourPlayerVersion()
            {
                int value = Versions.Count == 0 ? Version : Versions.Dequeue();
                tape.Add("read:version:" + value); return value;
            }
            public int ReadPlayerControl(int slot)
            {
                Queue<int> samples;
                int value = ControlSamples.TryGetValue(slot, out samples) && samples.Count != 0
                    ? samples.Dequeue() : Controls[slot];
                tape.Add("read:control:" + slot + ":" + value); return value;
            }
            public int ReadBallCarrier() { tape.Add("read:carrier:" + Carrier); return Carrier; }
            public int ReadPlayerTeam(int nativeControlIndex)
            {
                tape.Add("read:team:" + nativeControlIndex);
                int value;
                if (!Teams.TryGetValue(nativeControlIndex, out value))
                    throw new InvalidOperationException("synthetic unsupported team index " + nativeControlIndex);
                return value;
            }
            public int ReadOffensiveSide() { tape.Add("read:offense:" + OffensiveSide); return OffensiveSide; }
            public int ReadIsLowResolution()
            {
                int value = Lows.Count == 0 ? LowResolution : Lows.Dequeue();
                tape.Add("read:low:" + value); return value;
            }
            public int ReadTsec() { tape.Add("read:tsec:" + Tsec); return Tsec; }
            public int ReadCurrentButtons(int player)
            { tape.Add("current:read:" + player); return Current[player]; }
            public void WriteCurrentButtons(int player, int value)
            { tape.Add("current:write:" + player + ":" + value); Current[player] = value; }
            public int ReadOldButtons(int player) { tape.Add("old:read:" + player); return Old[player]; }
            public void WriteOldButtons(int player, int value)
            { tape.Add("old:write:" + player + ":" + value); Old[player] = value; }
        }

        private sealed class Plate : ISourceInstructionPlate
        {
            internal long Owner;
            internal string Image, Texture, BaseId;
            internal SourceInstructionPoint Point;
            internal float Angle, HScale;
            internal int Slot, Vertices;
            internal bool Deleted;
        }
        private sealed class PrintCall
        {
            internal long Caller;
            internal SourceKickoffInstructionChildTextRequest Request;
            internal SourceInstructionPoint Point;
            internal SourceKickoffInstructionChildFont Font;
            internal SourceKickoffInstructionChildColor Color;
            internal int StringId;
        }
        private sealed class Deletion
        {
            internal long Caller;
            internal int Id;
            internal string BaseId;
            internal uint Mask;
        }
        private sealed class Effects : ISourceKickoffInstructionChildEffects
        {
            private readonly Rig rig;
            internal readonly List<Plate> Plates = new List<Plate>();
            internal readonly List<PrintCall> Prints = new List<PrintCall>();
            internal readonly List<SourceKickoffInstructionChildFont> Fonts =
                new List<SourceKickoffInstructionChildFont>();
            internal readonly List<SourceKickoffInstructionChildColor> Colors =
                new List<SourceKickoffInstructionChildColor>();
            internal readonly List<SourceInstructionPoint> Positions = new List<SourceInstructionPoint>();
            internal readonly List<Deletion> StringDeletes = new List<Deletion>(), PlateDeletes = new List<Deletion>();
            internal readonly List<SourceKickoffInstructionChildSound> Sounds =
                new List<SourceKickoffInstructionChildSound>();
            internal readonly HashSet<int> LiveStringIds = new HashSet<int>();
            internal Plate LastPlate;
            internal float TextZ = 19.0f;
            internal int ZWrites, StringId;
            internal bool NullPlate;
            private SourceInstructionPoint position;
            private SourceKickoffInstructionChildFont font;
            private SourceKickoffInstructionChildColor color;
            internal Effects(Rig rig) { this.rig = rig; }
            private void Call(string operation) { rig.Tape.Add("effect:" + operation); }
            private Plate Require(ISourceInstructionPlate handle)
            {
                Plate plate = handle as Plate;
                if (plate == null || !Plates.Contains(plate) || plate.Owner != rig.Host.Caller || plate.Deleted)
                    throw new TestFailure("source setup must mutate the current child's actual plate return");
                return plate;
            }
            public void SetTextZIncrement(float value)
            { Call("z"); TextZ = value; ZWrites++; }
            public ISourceInstructionPlate BeginPlate(string image, string texture, SourceInstructionPoint point)
            {
                Call("plate");
                if (NullPlate) return null;
                LastPlate = new Plate { Owner = rig.Host.Caller, Image = image, Texture = texture, Point = point };
                Plates.Add(LastPlate); return LastPlate;
            }
            public void SetPlateZAngle(ISourceInstructionPlate plate, float value)
            { Call("angle"); Require(plate).Angle = value; }
            public void SetPlateHScale(ISourceInstructionPlate plate, float value)
            { Call("hscale"); Require(plate).HScale = value; }
            public void SetPlateObjectId(ISourceInstructionPlate plate, string baseId, int slot)
            { Call("object-id"); Plate value = Require(plate); value.BaseId = baseId; value.Slot = slot; }
            public void GeneratePlateVertices(ISourceInstructionPlate plate)
            { Call("verts"); Require(plate).Vertices++; }
            public void SetTextFont(SourceKickoffInstructionChildFont value)
            { Call("font:" + value); font = value; Fonts.Add(value); }
            public void SetStringId(int id) { Call("string-id:" + id); StringId = id; }
            public void SetTextColor(SourceKickoffInstructionChildColor value)
            { Call("color:" + value); color = value; Colors.Add(value); }
            public void SetTextPosition(SourceInstructionPoint point)
            { Call("position"); position = point; Positions.Add(point); }
            public void Print(SourceKickoffInstructionChildTextRequest request)
            {
                Call("print:" + request.NativePrintLine);
                Prints.Add(new PrintCall { Caller = rig.Host.Caller, Request = request,
                    Point = position, Font = font, Color = color, StringId = StringId });
                LiveStringIds.Add(StringId);
            }
            public void PlaySound(SourceKickoffInstructionChildSound sound)
            { Call("sound"); Sounds.Add(sound); }
            public void DeleteStrings(int id, uint mask)
            {
                Call("delete-strings");
                StringDeletes.Add(new Deletion { Caller = rig.Host.Caller, Id = id, Mask = mask });
                LiveStringIds.Remove(id);
            }
            public void DeletePlateClass(string baseId, int slot, uint mask)
            {
                Call("delete-plate-class");
                PlateDeletes.Add(new Deletion { Caller = rig.Host.Caller, BaseId = baseId, Id = slot, Mask = mask });
                foreach (Plate plate in Plates)
                    if (plate.BaseId == baseId && plate.Slot == slot) plate.Deleted = true;
            }
        }

        private sealed class WaitCall : ISourceKickoffActivePlayerWait
        {
            private readonly Rig rig;
            private readonly SourceKickoffActivePlayerWaitStep[] script;
            internal readonly int Minimum, Timeout, Slot;
            internal readonly long Caller;
            internal int Advances;
            internal WaitCall(Rig rig, int minimum, int timeout, int slot, SourceKickoffActivePlayerWaitStep[] script)
            {
                this.rig = rig; Minimum = minimum; Timeout = timeout; Slot = slot;
                Caller = rig.Host.Caller; this.script = (SourceKickoffActivePlayerWaitStep[])script.Clone();
            }
            public SourceKickoffActivePlayerWaitStep Advance()
            {
                Equal(Caller, rig.Host.Caller, "active-player helper resumes inside its actual child wake");
                rig.Tape.Add("wait:advance");
                if (Advances >= script.Length) throw new TestFailure("source replayed an exhausted synthetic wait");
                return script[Advances++];
            }
        }
        private sealed class Waits : ISourceKickoffActivePlayerWaitFactory
        {
            private readonly Rig rig;
            internal bool NullResult;
            internal readonly List<WaitCall> Calls = new List<WaitCall>();
            internal SourceKickoffActivePlayerWaitStep[] Script = {
                SourceKickoffActivePlayerWaitStep.Sleep(2), SourceKickoffActivePlayerWaitStep.Returned()
            };
            internal Waits(Rig rig) { this.rig = rig; }
            public ISourceKickoffActivePlayerWait Create(int minimum, int timeout, int slot)
            {
                rig.Tape.Add("wait:create");
                if (NullResult) return null;
                var call = new WaitCall(rig, minimum, timeout, slot, Script); Calls.Add(call); return call;
            }
        }

        private sealed class FlashCall : ISourceKickoffInstructionChildContinuation
        {
            private readonly Rig rig;
            private readonly SourceKickoffInstructionChildStep[] script;
            internal readonly SourceKickoffInstructionFlashInvocation Invocation;
            internal int Advances;
            internal FlashCall(Rig rig, SourceKickoffInstructionFlashInvocation invocation,
                SourceKickoffInstructionChildStep[] script)
            { this.rig = rig; Invocation = invocation; this.script = (SourceKickoffInstructionChildStep[])script.Clone(); }
            public SourceKickoffInstructionChildStep Advance()
            {
                Equal(Invocation.Serial, rig.Host.Caller, "flash continuation advances only on its actual host wake");
                rig.Tape.Add("flash:advance");
                if (Advances >= script.Length) throw new TestFailure("source replayed an exhausted synthetic flash");
                return script[Advances++];
            }
        }
        private sealed class Flashes : ISourceKickoffFlashContinuationFactory
        {
            private readonly Rig rig;
            internal bool NullResult;
            internal readonly List<FlashCall> Calls = new List<FlashCall>();
            internal SourceKickoffInstructionChildStep[] Script = {
                SourceKickoffInstructionChildStep.Sleep(3), SourceKickoffInstructionChildStep.ExternalRetirement()
            };
            internal Flashes(Rig rig) { this.rig = rig; }
            public ISourceKickoffInstructionChildContinuation Create(SourceKickoffInstructionFlashInvocation invocation)
            {
                Equal(invocation.Serial, rig.Host.Caller, "flash factory requires the later actual flash caller");
                rig.Tape.Add("flash:create");
                if (NullResult) return null;
                var call = new FlashCall(rig, invocation, Script); Calls.Add(call); return call;
            }
        }

        private sealed class ReturnCall
        {
            internal SourceKickoffInstructionChildInvocation Invocation;
            internal SourceKickoffInstructionChildReturnReason Reason;
        }
        private sealed class Returns : ISourceKickoffInstructionChildReturnBinding
        {
            private readonly Rig rig;
            internal SourceKickoffInstructionChildReturnAction Action =
                SourceKickoffInstructionChildReturnAction.KeepLive;
            internal readonly List<ReturnCall> Calls = new List<ReturnCall>();
            internal Returns(Rig rig) { this.rig = rig; }
            public SourceKickoffInstructionChildReturnAction ReturnFromSourceBody(
                SourceKickoffInstructionChildInvocation invocation, SourceKickoffInstructionChildReturnReason reason)
            {
                Equal(invocation.Serial, rig.Host.Caller, "source-return binding executes within the returning child");
                rig.Tape.Add("return:" + reason);
                Calls.Add(new ReturnCall { Invocation = invocation, Reason = reason }); return Action;
            }
        }

        private sealed class LegacyChildren : ISourceKickoffInstructionChildContinuationFactory
        {
            private readonly Rig rig;
            internal int Created, Advances;
            internal SourceKickoffInstructionChildStep[] Script = {
                SourceKickoffInstructionChildStep.Sleep(2), SourceKickoffInstructionChildStep.ExternalFallthrough()
            };
            internal LegacyChildren(Rig rig) { this.rig = rig; }
            public ISourceKickoffInstructionChildContinuation Create(SourceKickoffInstructionChildInvocation invocation)
            {
                Equal(invocation.Serial, rig.Host.Caller, "legacy child factory still requires actual deferred wake");
                rig.Tape.Add("legacy-child:create"); Created++; return new Continuation(this);
            }
            private sealed class Continuation : ISourceKickoffInstructionChildContinuation
            {
                private readonly LegacyChildren owner;
                private readonly SourceKickoffInstructionChildStep[] script;
                private int index;
                internal Continuation(LegacyChildren owner)
                { this.owner = owner; script = (SourceKickoffInstructionChildStep[])owner.Script.Clone(); }
                public SourceKickoffInstructionChildStep Advance()
                {
                    owner.rig.Tape.Add("legacy-child:advance"); owner.Advances++;
                    if (index >= script.Length) throw new TestFailure("legacy child body was replayed");
                    return script[index++];
                }
            }
        }
        private sealed class LegacySuffix : ISourceKickoffInstructionContinuationFactory,
            ISourceKickoffInstructionContinuation
        {
            private readonly Rig rig;
            internal int Created, Advances;
            internal LegacySuffix(Rig rig) { this.rig = rig; }
            public ISourceKickoffInstructionContinuation Create()
            { rig.Tape.Add("legacy-parent:create"); Created++; return this; }
            public SourceKickoffInstructionStep Advance()
            {
                rig.Tape.Add("legacy-parent:advance");
                return Advances++ == 0 ? SourceKickoffInstructionStep.Sleep(1)
                    : SourceKickoffInstructionStep.ExternalFallthrough();
            }
        }

        private sealed class Rig
        {
            // Explicit synthetic operands; these numbers do not authenticate native symbols.
            internal const int ChildId = 0x6b42, InstructionId = 0x6b43, PostId = 0x0502,
                CameraId = 0x1400, A = 0x10, B = 0x20;
            internal readonly SourcePortableProcessHost Host = new SourcePortableProcessHost();
            internal readonly Tape Tape = new Tape();
            internal readonly State State;
            internal readonly Inputs Inputs;
            internal readonly Effects Effects;
            internal readonly Waits Waits;
            internal readonly Flashes Flashes;
            internal readonly Returns Returns;
            internal readonly LegacyChildren LegacyChildren;
            internal readonly LegacySuffix LegacySuffix;
            internal readonly SourceInstructionInvocation Invocation;
            internal readonly SourceKickoffInstructionChildBinding ChildBinding;
            internal readonly SourceKickoffInstructionCoordinator Config;
            internal readonly SourceInstructionAdmissionBinding Binding;
            internal readonly SourceInstructionDispatcher Dispatcher;
            internal readonly List<long> Commits = new List<long>(), Children = new List<long>();
            internal long Serial = 32, Parent;
            internal Action<long> OnCommit;
            internal Func<long> OverrideNext;
            internal Rig(bool source = true, bool debug = false, int version = 0,
                int status = 3, bool legacySuffix = false, bool initialize = true,
                SourceKickoffInstructionChildOperands operands = null)
            {
                State = new State(Tape, status); Inputs = new Inputs(Tape, version);
                Effects = new Effects(this); Waits = new Waits(this); Flashes = new Flashes(this);
                Returns = new Returns(this); LegacyChildren = new LegacyChildren(this);
                LegacySuffix = new LegacySuffix(this);
                Host.Create(1, 0x10, "game_proc", 0); Host.Create(2, 0x500, "player_proc", 0);
                Host.Create(20, PostId, "last_player_proc", 0); Host.Create(17, CameraId, "cambot_proc", 0);
                Invocation = new SourceInstructionInvocation(4,
                    new SourceInstructionProcessIds(InstructionId, PostId, CameraId, 0x6b44), State, State);
                ChildBinding = new SourceKickoffInstructionChildBinding(
                    operands ?? new SourceKickoffInstructionChildOperands(debug, 101.0f, 203.0f, 0, 1),
                    Effects, Waits, Flashes, Returns);
                Config = source
                    ? SourceKickoffInstructionCoordinator.WithSourceChild(ChildId, 4, A, B, Inputs, Inputs, ChildBinding)
                    : new SourceKickoffInstructionCoordinator(ChildId, 4, A, B, Inputs, Inputs, LegacyChildren);
                Binding = legacySuffix ? new SourceInstructionAdmissionBinding(Invocation, State, LegacySuffix)
                    : SourceInstructionAdmissionBinding.WithKickoffCoordinator(Invocation, State, Config);
                Dispatcher = new SourceInstructionDispatcher(Host);
                if (initialize)
                {
                    if (legacySuffix) Dispatcher.InitializeAdmission(Binding);
                    else Dispatcher.InitializeAdmission(Binding, Next, Commit);
                }
                Tape.Clear();
            }
            internal long Next()
            { Tape.Add("serial:next"); return OverrideNext == null ? checked(Serial + 1) : OverrideNext(); }
            internal void Commit(long serial)
            {
                Serial = serial; Commits.Add(serial); Tape.Add("serial:commit");
                if (OnCommit != null) OnCommit(serial);
            }
            internal long Catch()
            {
                Host.BeginTick(Host.SourceTick + 1);
                Check(Host.Visit(2), "actual kickoff catch caller must wake");
                try
                {
                    long? serial = Dispatcher.AdmitKickoffFromCatch(Binding, true, 2, Next);
                    Check(serial.HasValue, "actual kickoff parent must be admitted");
                    Commit(serial.Value); Parent = serial.Value; return Parent;
                }
                finally { Host.Complete(); }
            }
            internal void CaptureChildren()
            {
                Children.Clear();
                foreach (SourcePossessionProcessRecord record in Host.Records)
                    if (record.Operation == "Create" && Dispatcher.OwnsKickoffChild(record.Serial))
                        Children.Add(record.Serial);
            }
            internal void Start()
            { Catch(); Tick(Parent); CaptureChildren(); Tape.Clear(); }
            internal void DispatchThisTick(long serial)
            { Check(Dispatcher.Dispatch(serial, f => Tape.Add("begin:" + f)), "actual dispatcher must own requested serial"); }
            internal void Tick(params long[] serials)
            {
                Host.BeginTick(Host.SourceTick + 1);
                foreach (long serial in serials) DispatchThisTick(serial);
                Equal(0L, Host.Caller, "actual host caller must be released after dispatch");
            }
            internal long Foreign(int id, string function, ushort sleep = 1)
            { long serial = Next(); Host.Create(serial, id, function, 0, sleep); Commit(serial); return serial; }
            internal List<long> FlashSerials()
            {
                var serials = new List<long>();
                foreach (SourceProcessView process in Host.Processes)
                    if (Dispatcher.OwnsKickoffFlash(process.Serial)) serials.Add(process.Serial);
                serials.Sort(); return serials;
            }
        }

        private static int Records(Rig rig, string operation, long serial = 0)
        {
            int count = 0;
            foreach (SourcePossessionProcessRecord record in rig.Host.Records)
                if (record.Operation == operation && (serial == 0 || record.Serial == serial)) count++;
            return count;
        }
        private static SourcePossessionProcessRecord Record(Rig rig, string operation, long serial = 0)
        {
            Equal(1, Records(rig, operation, serial), "one actual host record required for " + operation);
            foreach (SourcePossessionProcessRecord record in rig.Host.Records)
                if (record.Operation == operation && (serial == 0 || record.Serial == serial)) return record;
            throw new TestFailure("missing actual host record " + operation);
        }
        private static void Point(float x, float y, float depth, SourceInstructionPoint actual, string causal)
        { Equal(x, actual.X, causal + " X"); Equal(y, actual.Y, causal + " Y"); Equal(depth, actual.Depth, causal + " depth"); }
        private static void Order(Tape tape, params string[] operations)
        {
            int after = -1;
            foreach (string operation in operations)
            {
                int at = tape.Entries.IndexOf(operation, after + 1);
                Check(at > after, "source operation order must retain " + operation);
                after = at;
            }
        }
        private static void NoSuffix(Rig rig)
        {
            Equal(0, rig.Effects.Sounds.Count, "sound must not precede actual helper return");
            Equal(0, rig.Effects.StringDeletes.Count, "strings must not be cleaned before actual helper return");
            Equal(0, rig.Effects.PlateDeletes.Count, "sprites must not be cleaned before actual helper return");
            Equal(0, rig.Returns.Calls.Count, "source return must not precede actual helper return");
        }
        private static void FaultCannotReplay(Rig rig, long serial)
        {
            rig.Tape.Clear();
            Throws<InvalidOperationException>(() => rig.Tick(serial), "failed continuation must reject replay");
            Equal(0, rig.Tape.Entries.Count, "failed continuation must emit no provider work on replay");
            Equal(0, rig.Host.Records.Count, "failed continuation must not wake or retire on replay");
            Equal(0L, rig.Host.Caller, "failed continuation must release the actual host caller");
        }
        private static void Choose(Rig rig, string branch)
        {
            rig.Inputs.Carrier = branch == "carrier" ? 0 : 9;
            rig.Inputs.Controls[0] = 0;
            rig.Inputs.Teams[0] = branch == "defense" ? 1 : 0;
            rig.Inputs.OffensiveSide = 0;
        }

        private sealed class ExpectedLine
        {
            internal SourceKickoffInstructionChildTextKind Kind;
            internal int Ordinal, NativeLine, Offset, Delta;
            internal bool Low;
            internal ExpectedLine(SourceKickoffInstructionChildTextKind kind, int ordinal, int line,
                int offset, int delta = 0, bool low = false)
            { Kind = kind; Ordinal = ordinal; NativeLine = line; Offset = offset; Delta = delta; Low = low; }
        }
        private static ExpectedLine[] Layout(string branch)
        {
            if (branch == "carrier") return new[] {
                new ExpectedLine(SourceKickoffInstructionChildTextKind.CarrierStatus, 1, 4591, 75),
                new ExpectedLine(SourceKickoffInstructionChildTextKind.SpeedAdvice, 1, 4596, 55),
                new ExpectedLine(SourceKickoffInstructionChildTextKind.SpeedAdvice, 2, 4598, 43, -2, true),
                new ExpectedLine(SourceKickoffInstructionChildTextKind.SpinAdvice, 1, 4601, 23),
                new ExpectedLine(SourceKickoffInstructionChildTextKind.SpinAdvice, 2, 4603, 11, -1, true),
                new ExpectedLine(SourceKickoffInstructionChildTextKind.SpinAdvice, 3, 4605, -1, -3, true),
                new ExpectedLine(SourceKickoffInstructionChildTextKind.StiffArmAdvice, 1, 4608, -21),
                new ExpectedLine(SourceKickoffInstructionChildTextKind.StiffArmAdvice, 2, 4610, -33, -1, true),
                new ExpectedLine(SourceKickoffInstructionChildTextKind.HurdleAdvice, 1, 4613, -53),
                new ExpectedLine(SourceKickoffInstructionChildTextKind.HurdleAdvice, 2, 4615, -65, -2, true),
                new ExpectedLine(SourceKickoffInstructionChildTextKind.HurdleAdvice, 3, 4617, -77, -3, true)
            };
            if (branch == "support") return new[] {
                new ExpectedLine(SourceKickoffInstructionChildTextKind.BlockingAdvice, 1, 4627, 55, 1, true),
                new ExpectedLine(SourceKickoffInstructionChildTextKind.BlockingAdvice, 2, 4629, 43, -1, true),
                new ExpectedLine(SourceKickoffInstructionChildTextKind.BlockingAdvice, 3, 4631, 31, -2, true),
                new ExpectedLine(SourceKickoffInstructionChildTextKind.BlockingAdvice, 4, 4633, 19, -3, true),
                new ExpectedLine(SourceKickoffInstructionChildTextKind.BlockingAdvice, 5, 4635, 7, -4, true),
                new ExpectedLine(SourceKickoffInstructionChildTextKind.BlockingAdvice, 6, 4637, -5, -6, true),
                new ExpectedLine(SourceKickoffInstructionChildTextKind.BlockingAdvice, 7, 4639, -17, -7, true),
                new ExpectedLine(SourceKickoffInstructionChildTextKind.LateralPositionAdvice, 1, 4642, -37, -6, true),
                new ExpectedLine(SourceKickoffInstructionChildTextKind.LateralPositionAdvice, 2, 4644, -49, -7, true),
                new ExpectedLine(SourceKickoffInstructionChildTextKind.LateralPositionAdvice, 3, 4646, -61, -8, true),
                new ExpectedLine(SourceKickoffInstructionChildTextKind.LateralPositionAdvice, 4, 4648, -73, -10, true)
            };
            if (branch != "defense") throw new TestFailure("unknown explicitly selected expected layout");
            return new[] {
                new ExpectedLine(SourceKickoffInstructionChildTextKind.DefenderSelectionAdvice, 1, 4656, 55, 1, true),
                new ExpectedLine(SourceKickoffInstructionChildTextKind.DefenderSelectionAdvice, 2, 4658, 43, -1, true),
                new ExpectedLine(SourceKickoffInstructionChildTextKind.DefenderSelectionAdvice, 3, 4660, 31, -2, true),
                new ExpectedLine(SourceKickoffInstructionChildTextKind.DefenderSelectionAdvice, 4, 4662, 19, -3, true),
                new ExpectedLine(SourceKickoffInstructionChildTextKind.DefenderSelectionAdvice, 5, 4664, 7, -5, true),
                new ExpectedLine(SourceKickoffInstructionChildTextKind.DefenderSelectionAdvice, 6, 4666, -5, -6, true),
                new ExpectedLine(SourceKickoffInstructionChildTextKind.DefenderSelectionAdvice, 7, 4668, -17, -8, true),
                new ExpectedLine(SourceKickoffInstructionChildTextKind.DiveTackleAdvice, 1, 4671, -37, -7, true),
                new ExpectedLine(SourceKickoffInstructionChildTextKind.DiveTackleAdvice, 2, 4673, -49, -9, true),
                new ExpectedLine(SourceKickoffInstructionChildTextKind.DiveTackleAdvice, 3, 4675, -61, -10, true)
            };
        }
        private static void AssertLayout(Rig rig, string branch, int[] lowValues)
        {
            ExpectedLine[] expected = Layout(branch);
            Equal(expected.Length + 1, rig.Effects.Prints.Count, branch + " complete selected print count");
            Equal(expected.Length + 1, rig.Effects.Positions.Count, branch + " one position per selected print");
            PrintCall header = rig.Effects.Prints[0];
            int lowAt = 0;
            for (int i = 0; i < expected.Length; i++)
            {
                ExpectedLine line = expected[i]; PrintCall actual = rig.Effects.Prints[i + 1];
                int low = line.Low ? lowValues[lowAt++] : 0;
                Equal(line.Kind, actual.Request.Kind, branch + " semantic slot at " + line.NativeLine);
                Equal(line.Ordinal, actual.Request.Ordinal, branch + " semantic ordinal at " + line.NativeLine);
                Equal(line.NativeLine, actual.Request.NativePrintLine, branch + " source print citation");
                Equal((int?)null, actual.Request.DisplayPlayerNumber, branch + " nonheader has no player substitution");
                Equal("HJUST_CENTER|VJUST_CENTER", actual.Request.NativeSecondArgumentSymbols, branch + " centered request");
                Point(header.Point.X + 5, header.Point.Y - 90 + line.Offset + (low != 0 ? line.Delta : 0),
                    1.0f, actual.Point, branch + " retained-coordinate layout at " + line.NativeLine);
                Equal(header.Caller, actual.Caller, branch + " print runs in the source child");
                Equal(header.StringId, actual.StringId, branch + " prints retain the slot string ID");
                Equal(branch == "carrier" && i == 0 ? SourceKickoffInstructionChildFont.Bast10
                    : SourceKickoffInstructionChildFont.Bast8T, actual.Font, branch + " exact print font");
                Equal(branch == "carrier" && i == 0 ? SourceKickoffInstructionChildColor.LightCyan
                    : SourceKickoffInstructionChildColor.LightYellow, actual.Color, branch + " exact print color");
                int printAt = rig.Tape.Entries.IndexOf("effect:print:" + line.NativeLine);
                Check(printAt >= 2, branch + " selected print has a real ordered effect transcript");
                Equal("effect:position", rig.Tape.Entries[printAt - 1], branch + " position immediately precedes print");
                if (line.Low)
                    Equal("read:low:" + low, rig.Tape.Entries[printAt - 2], branch + " fresh low-res read at each conditional position");
                else
                    Check(!rig.Tape.Entries[printAt - 2].StartsWith("read:low:", StringComparison.Ordinal),
                        branch + " fixed position must not consume low-res input");
            }
            Equal(lowValues.Length, lowAt, branch + " expected low-res sequence is fully consumed");
            Equal(lowValues.Length, rig.Tape.Count("read:low:"), branch + " exact live low-res read count");
            Equal(0, rig.Inputs.Lows.Count, branch + " no cached low-res leaves provider values unused");
            Equal(branch == "carrier" ? 3 : 2, rig.Effects.Fonts.Count, branch + " exact font operation count");
            Equal(branch == "carrier" ? 3 : 2, rig.Effects.Colors.Count, branch + " exact color operation count");
            Equal(SourceKickoffInstructionChildFont.Bast10, rig.Effects.Fonts[0], branch + " header font first");
            Equal(SourceKickoffInstructionChildColor.LightBlue, rig.Effects.Colors[0], branch + " slot-zero header color first");
            if (branch == "carrier")
            {
                Equal(SourceKickoffInstructionChildFont.Bast10, rig.Effects.Fonts[1], "carrier status font stage");
                Equal(SourceKickoffInstructionChildColor.LightCyan, rig.Effects.Colors[1], "carrier status color stage");
            }
            Equal(SourceKickoffInstructionChildFont.Bast8T, rig.Effects.Fonts[rig.Effects.Fonts.Count - 1],
                branch + " advice font stage");
            Equal(SourceKickoffInstructionChildColor.LightYellow, rig.Effects.Colors[rig.Effects.Colors.Count - 1],
                branch + " advice color stage");
            var expectedEffects = new List<string> {
                "effect:z", "effect:plate", "effect:angle", "effect:hscale", "effect:object-id", "effect:verts",
                "effect:font:Bast10", "effect:string-id:52", "effect:color:LightBlue", "effect:position", "effect:print:4583"
            };
            if (branch == "carrier")
            { expectedEffects.Add("effect:font:Bast10"); expectedEffects.Add("effect:color:LightCyan"); }
            else
            { expectedEffects.Add("effect:font:Bast8T"); expectedEffects.Add("effect:color:LightYellow"); }
            for (int i = 0; i < expected.Length; i++)
            {
                if (branch == "carrier" && i == 1)
                { expectedEffects.Add("effect:font:Bast8T"); expectedEffects.Add("effect:color:LightYellow"); }
                expectedEffects.Add("effect:position"); expectedEffects.Add("effect:print:" + expected[i].NativeLine);
            }
            var actualEffects = new List<string>();
            foreach (string operation in rig.Tape.Entries)
                if (operation.StartsWith("effect:", StringComparison.Ordinal)) actualEffects.Add(operation);
            Equal(string.Join("|", expectedEffects), string.Join("|", actualEffects),
                branch + " exact ordered primitive effect transcript");
        }
        private static int[] Lows(Rig rig, int count, bool alternate, int phase = 0)
        {
            int[] values = new int[count];
            for (int i = 0; i < count; i++)
            { values[i] = alternate && (i + phase) % 2 == 0 ? -7 : 0; rig.Inputs.Lows.Enqueue(values[i]); }
            return values;
        }

        private static void DebugCompileChoice()
        {
            foreach (SourceKickoffInstructionChildReturnAction action in new[] {
                SourceKickoffInstructionChildReturnAction.KeepLive, SourceKickoffInstructionChildReturnAction.ExternallyRetire })
            {
                var rig = new Rig(debug: true); rig.Start(); long child = rig.Children[0];
                rig.Inputs.Version = 2; rig.State.Status = 0; rig.Returns.Action = action;
                rig.Tick(child);
                Equal("begin:plr_ko_idiot_box_proc|return:DbgKoCompileTimeReturn", rig.Tape.Joined,
                    "DBG_KO must return before any state, argument-derived setup or effects");
                Equal(0, rig.Effects.Prints.Count, "DBG_KO must not print");
                Equal(0, rig.Waits.Calls.Count, "DBG_KO must not invoke active-player wait");
                Equal(0, rig.FlashSerials().Count, "DBG_KO must not create flash");
                Equal(0, rig.LegacyChildren.Created, "source debug path must not delegate the child body");
                Equal(1, rig.Returns.Calls.Count, "DBG_KO requires one explicit return policy decision");
                Equal(SourceKickoffInstructionChildReturnReason.DbgKoCompileTimeReturn, rig.Returns.Calls[0].Reason,
                    "DBG_KO must retain a distinct source-return reason");
                Equal(1, Record(rig, "SourceChildFallthrough", child).Value, "DBG_KO is source fallthrough reason one");
                Equal(0, Records(rig, "NativeChildDie"), "DBG_KO must not be relabeled native die");
                Equal(action == SourceKickoffInstructionChildReturnAction.KeepLive, rig.Host.Contains(child),
                    "DBG_KO liveness must follow only the explicit return binding");
                rig.Tape.Clear(); rig.Tick(child);
                Equal(0, rig.Tape.Entries.Count, "terminal debug return must not replay");
                Equal(0, rig.Host.Records.Count, "terminal debug return must not synthesize another wake");
            }
        }

        private static void CoordinatesBeforeInactiveGuard()
        {
            var rig = new Rig(); rig.Start(); long child = rig.Children[1], sibling = rig.Children[0];
            rig.Inputs.Versions.Enqueue(0); rig.Inputs.Versions.Enqueue(1); rig.State.Status = 0;
            rig.State.Games[1] = 3; rig.Tick(child);
            Equal("begin:plr_ko_idiot_box_proc|read:version:0|read:version:1|read:status:0", rig.Tape.Joined,
                "inactive guard must follow independent X then Y reads and precede games");
            Equal(0, Record(rig, "NativeChildDie", child).Value, "inactive native die signal is zero");
            Equal(Rig.ChildId, Record(rig, "Die", child).Id, "inactive die removes the actual child PID");
            Check(!rig.Host.Contains(child), "inactive native die must retire this child serial");
            Check(rig.Host.Contains(sibling), "inactive native die must leave sibling alive");
            Equal(0, rig.FlashSerials().Count, "inactive guard must precede flash creation");
            Equal(0, rig.Effects.Plates.Count, "inactive guard must precede plate setup");
            NoSuffix(rig);
        }

        private static void LiveExperienceThreshold()
        {
            foreach (int games in new[] { -1, 2, 3 })
            {
                var rig = new Rig(); rig.State.Games[0] = 9;
                rig.Start(); long child = rig.Children[0];
                Equal(0, rig.State.GameReads.Count, "parent creation cannot snapshot child experience");
                rig.State.Games[0] = games; rig.State.Status = 1;
                rig.Tick(child);
                Order(rig.Tape, "read:version:0", "read:version:0", "read:status:1", "read:games:0:" + games);
                Equal(games <= 2, rig.Host.Contains(child), "child-entry games threshold must be strict greater than two");
                Equal(games <= 2 ? 1 : 0, rig.FlashSerials().Count, "only admitted live novices create flash");
                Equal(games > 2 ? 1 : 0, Records(rig, "NativeChildDie", child), "experienced guard must use NativeDie");
                Equal(games <= 2 ? 1 : 0, rig.Waits.Calls.Count, "negative and two games must not acquire an extra native guard");
                Equal(1, rig.State.Flag, "child guards must not write the shared instruction flag");
                NoSuffix(rig);
            }
        }

        private static void RealFlashCreation()
        {
            var rig = new Rig(); rig.Start(); long child = rig.Children[0];
            rig.Inputs.Controls[0] = 17; rig.Inputs.Carrier = 17;
            long interleaved = rig.Foreign(0x8005, "component_foreign_process");
            long expected = interleaved + 1; rig.Tape.Clear();
            rig.Tape.OnCall = operation =>
            {
                if (operation != "effect:z") return;
                Equal(child, rig.Host.Caller, "flash creation must happen while the actual child is caller");
                Check(rig.Host.Contains(expected), "real flash must exist before shared text setup");
                Equal(expected, rig.Commits[rig.Commits.Count - 1], "flash must commit the shared allocator before setup");
                Equal(0, rig.Flashes.Calls.Count, "flash creation must not run its factory inline");
            };
            rig.Tick(child); rig.Tape.OnCall = null;
            List<long> flashes = rig.FlashSerials();
            Equal(1, flashes.Count, "one actual flash host entry per admitted child");
            Equal(expected, flashes[0], "flash must use next globally shared serial after unrelated allocation");
            var invocation = rig.Dispatcher.KickoffFlashInvocation(expected);
            Equal(expected, invocation.Serial, "flash invocation identity is the actual host serial");
            Equal(0, invocation.ProcessId, "flash PID is literal zero, not argument twenty-two");
            Equal("flash", invocation.Name, "flash source process name");
            Equal("flash_plyr_proc2", invocation.Function, "flash source callback identity");
            Equal(0, invocation.Argument0, "flash argument zero must use slot rather than control");
            Equal(8, invocation.Argument1, "flash argument one");
            Equal(22, invocation.Argument2, "flash observed P2_ID argument");
            Equal(0, invocation.Argument3, "flash argument three");
            SourceProcessView actual = rig.Host.Require(expected);
            Equal(0, actual.Id, "real flash host uses literal PID zero");
            Equal(SourceInstructionDispatcher.KickoffFlashFunction, actual.Function, "real host binds exact flash function");
            Equal(rig.Host.SourceTick, actual.BirthTick, "real flash birth is the source child wake");
            Equal((ushort)1, actual.SleepWord, "ordinary flash creation retains deferred host sleep");
            Check(rig.Dispatcher.OwnsKickoffFlash(expected) && !rig.Dispatcher.OwnsKickoffChild(expected),
                "flash and instruction children must be separately owned");
            Record(rig, "Create", expected);
            Order(rig.Tape, "serial:next", "serial:commit", "effect:z", "effect:plate");
            rig.DispatchThisTick(expected);
            Equal(0, rig.Flashes.Calls.Count, "birth-tick dispatch must not execute flash factory");
            Equal(0, Records(rig, "Wake", expected), "flash cannot wake inline in creation tick");
            rig.Tape.Clear(); rig.Tick(expected);
            Equal(1, rig.Flashes.Calls.Count, "flash factory must run on its later actual host wake");
            Check(ReferenceEquals(invocation, rig.Flashes.Calls[0].Invocation), "flash factory receives exact retained invocation");
            Equal(1, rig.Flashes.Calls[0].Advances, "flash body advances once on first actual wake");
            Equal((ushort)3, rig.Host.Require(expected).SleepWord, "flash sleep must reach actual host word");
            Equal(3, Record(rig, "Sleep", expected).Value, "flash external sleep has exact host delay");
            Equal("begin:flash_plyr_proc2|flash:create|flash:advance", rig.Tape.Joined,
                "flash must use the actual source dispatcher classification");
            Throws<ArgumentException>(() => rig.Dispatcher.KickoffFlashInvocation(child), "child cannot masquerade as a flash");
        }

        private static void SpriteSetup()
        {
            var rig = new Rig(); rig.Start(); long child = rig.Children[0]; rig.Tick(child);
            Equal(1, rig.Effects.Plates.Count, "one source beginobj return");
            Plate plate = rig.Effects.Plates[0];
            Equal(child, plate.Owner, "plate belongs to actual executing child");
            Equal("playresult", plate.Image, "source plate image symbol");
            Equal("SCR_PLATE_TID", plate.Texture, "source plate texture symbol");
            Point(101.0f, 150.0f, 1.4f, plate.Point, "source box coordinates use explicit synthetic BARX1");
            Equal(256.04f, plate.Angle, "source native z_angle operand");
            Equal(1.45f, plate.HScale, "source native h_scale operand");
            Equal("OID_IDIOTBOX", plate.BaseId, "source sprite object-ID class");
            Equal(0, plate.Slot, "source object-ID slot offset");
            Equal(1, plate.Vertices, "vertices generated once after all plate mutations");
            Equal(0.0f, rig.Effects.TextZ, "source shared text-z increment is zero");
            Equal(1, rig.Effects.ZWrites, "source shared text-z set once with no invented restoration");
            var setup = new List<string>();
            foreach (string operation in rig.Tape.Entries)
                if (operation.StartsWith("effect:", StringComparison.Ordinal) && setup.Count < 6) setup.Add(operation);
            Equal("effect:z|effect:plate|effect:angle|effect:hscale|effect:object-id|effect:verts",
                string.Join("|", setup), "source typed sprite setup must preserve every ordered operation");
            NoSuffix(rig);
        }

        private static SourceKickoffInstructionChildColor ExpectedColor(int slot, int version)
        {
            SourceKickoffInstructionChildColor[,] colors = {
                { SourceKickoffInstructionChildColor.LightBlue, SourceKickoffInstructionChildColor.LightBlue },
                { SourceKickoffInstructionChildColor.LightRed, SourceKickoffInstructionChildColor.LightGreen },
                { SourceKickoffInstructionChildColor.NativeZero, SourceKickoffInstructionChildColor.LightRed },
                { SourceKickoffInstructionChildColor.NativeZero, SourceKickoffInstructionChildColor.LightYellow }
            };
            return colors[slot, version];
        }
        private static void Header(Rig rig, int slot, float x, float y, int colorVersion)
        {
            PrintCall header = rig.Effects.Prints[0];
            Point(x, y, 1.4f, rig.Effects.Plates[0].Point, "retained independently selected box coordinates");
            Point(x - 5, y + 90, 1.0f, header.Point, "source player-header position");
            Equal(SourceKickoffInstructionChildFont.Bast10, header.Font, "header must use BAST10");
            Equal(ExpectedColor(slot, colorVersion), header.Color, "live header color table row");
            Equal(0x34 + slot, header.StringId, "header string ID is fifty-two plus slot");
            Equal(SourceKickoffInstructionChildTextKind.Header, header.Request.Kind, "header semantic request");
            Equal(1, header.Request.Ordinal, "header semantic ordinal");
            Equal(4583, header.Request.NativePrintLine, "header source print citation");
            Equal((int?)(slot + 1), header.Request.DisplayPlayerNumber, "header player number must be one-based");
            Equal("HJUST_CENTER|VJUST_CENTER", header.Request.NativeSecondArgumentSymbols, "header is centered");
            Equal(3, rig.Tape.Count("read:version:"), "X, Y and header must perform three separate version reads");
        }
        private static void AllLayoutAndHeaderRows()
        {
            float[,] xs = { { 101, 64 }, { 203, 192 }, { 0, 320 }, { 0, 448 } };
            float[,] ys = { { 150, 150 }, { 150, 180 }, { 150, 150 }, { 150, 180 } };
            for (int slot = 0; slot < 4; slot++)
                for (int version = 0; version < 2; version++)
                {
                    var rig = new Rig(version: 1); rig.Start();
                    Equal(4, rig.Children.Count, "parent live four-player input creates all observed slots");
                    rig.Inputs.Version = version; rig.State.Status = 1 << slot; rig.State.Games[slot] = 2;
                    rig.Tick(rig.Children[slot]); Header(rig, slot, xs[slot, version], ys[slot, version], version);
                }
            var mixed = new Rig(version: 1); mixed.Start();
            mixed.Inputs.Versions.Enqueue(0); mixed.Inputs.Versions.Enqueue(1); mixed.Inputs.Versions.Enqueue(0);
            mixed.Tick(mixed.Children[1]); Header(mixed, 1, 203, 180, 0);
            Order(mixed.Tape, "read:version:0", "read:version:1", "effect:font:Bast10",
                "effect:string-id:53", "read:version:0", "effect:color:LightRed");
            var swapped = new Rig(operands: new SourceKickoffInstructionChildOperands(false, 101, 203, 1, 0));
            swapped.Start(); swapped.Tick(swapped.Children[0]); Header(swapped, 0, 150, 101, 0);
        }

        private static void CarrierPrecedence()
        {
            var rig = new Rig(); rig.Start();
            rig.Inputs.Controls[0] = -1; rig.Inputs.Carrier = -1;
            rig.Inputs.Teams[-1] = 0;
            int[] lows = Lows(rig, 6, false); rig.Tick(rig.Children[0]);
            AssertLayout(rig, "carrier", lows);
            Equal(0, rig.Tape.Count("read:team:"), "carrier equality must win before any team lookup");
            Equal(0, rig.Tape.Count("read:offense:"), "carrier equality must win before offense comparison");
            Equal(1, rig.Tape.Count("read:control:"), "carrier path must not add a nonnegative-control gate");
            Equal(1, rig.Tape.Count("read:carrier:"), "carrier branch uses one live carrier read");
        }
        private static void OffenseControlReread()
        {
            var rig = new Rig(); rig.Start();
            rig.Inputs.Carrier = 8; rig.Inputs.ControlSamples[0] = new Queue<int>(new[] { 7, 9 });
            rig.Inputs.Teams[7] = 1; rig.Inputs.Teams[9] = 0; rig.Inputs.Teams[0] = 1;
            int[] lows = Lows(rig, 11, false); rig.Tick(rig.Children[0]);
            AssertLayout(rig, "support", lows);
            Equal(2, rig.Tape.Count("read:control:0:"), "support team lookup must reread control after carrier comparison");
            Equal(1, rig.Tape.Count("read:control:0:7"), "first comparison uses first live control");
            Equal(1, rig.Tape.Count("read:control:0:9"), "team lookup uses newly read control");
            Equal(1, rig.Tape.Count("read:team:9"), "support branch must index the fresh controlled player's team");
            Equal(0, rig.Tape.Count("read:team:7"), "support cannot cache failed comparison control");
            Equal(0, rig.Tape.Count("read:team:0"), "support cannot index team by instruction slot");
            Equal(1, rig.Tape.Count("read:offense:"), "support selection must compare against live offensive side");
        }
        private static void DefenseAndNegativeIndexBoundary()
        {
            var defense = new Rig(); defense.Start(); Choose(defense, "defense");
            int[] lows = Lows(defense, 10, false); defense.Tick(defense.Children[0]);
            AssertLayout(defense, "defense", lows);
            Equal(1, defense.Tape.Count("read:team:0"), "defense still requires actual controlled-player team lookup");
            var unsupported = new Rig(); unsupported.Start();
            unsupported.Inputs.Carrier = 8;
            unsupported.Inputs.ControlSamples[0] = new Queue<int>(new[] { 7, -1 });
            InvalidOperationException failure = Throws<InvalidOperationException>(
                () => unsupported.Tick(unsupported.Children[0]), "unsupported negative team index must surface binding failure");
            Equal("synthetic unsupported team index -1", failure.Message, "negative index cannot silently become defense");
            Equal("read:team:-1", unsupported.Tape.Entries[unsupported.Tape.Entries.Count - 1],
                "unsupported index faults at provider boundary without invented fallback");
            Equal(1, unsupported.Effects.Prints.Count, "unsupported team domain must not print any branch");
            Equal(0, Records(unsupported, "NativeChildDie"), "portable domain failure must not become native guard die");
            NoSuffix(unsupported); FaultCannotReplay(unsupported, unsupported.Children[0]);
            var supported = new Rig(); supported.Start();
            supported.Inputs.Carrier = 8;
            supported.Inputs.ControlSamples[0] = new Queue<int>(new[] { 7, -1 });
            supported.Inputs.Teams[-1] = 0;
            int[] supportedLows = Lows(supported, 11, false); supported.Tick(supported.Children[0]);
            AssertLayout(supported, "support", supportedLows);
            Equal(1, supported.Tape.Count("read:team:-1"), "supported negative domain must pass through without a new gate");
        }
        private static void PerPositionLowResolution()
        {
            foreach (string branch in new[] { "carrier", "support", "defense" })
                for (int phase = 0; phase < 2; phase++)
                {
                    var rig = new Rig(); rig.Start(); Choose(rig, branch);
                    int count = branch == "carrier" ? 6 : branch == "support" ? 11 : 10;
                    int[] lows = Lows(rig, count, true, phase); rig.Tick(rig.Children[0]);
                    AssertLayout(rig, branch, lows);
                    Equal(1, rig.Waits.Calls.Count, branch + " complete selected layout must reach player-specific wait");
                    NoSuffix(rig);
                }
        }

        private static void ActivePlayerWaitBoundary()
        {
            var rig = new Rig(); rig.Start(); long child = rig.Children[0];
            rig.Waits.Script = new[] { SourceKickoffActivePlayerWaitStep.Sleep(3),
                SourceKickoffActivePlayerWaitStep.Sleep(1), SourceKickoffActivePlayerWaitStep.Returned() };
            rig.Inputs.Tsec = 2;
            rig.Tape.OnCall = operation => { if (operation == "effect:print:4617") rig.Inputs.Tsec = 9; };
            rig.Tick(child); rig.Tape.OnCall = null;
            Equal(1, rig.Waits.Calls.Count, "active-player helper must be created once");
            WaitCall wait = rig.Waits.Calls[0];
            Equal(40, wait.Minimum, "active-player helper minimum must be forty, not global wait minimum ten");
            Equal(54, wait.Timeout, "active-player helper timeout must use six times fresh post-layout tsec");
            Equal(0, wait.Slot, "active-player helper must receive original slot");
            Equal(child, wait.Caller, "active-player helper factory belongs to actual child");
            Equal((ushort)3, rig.Host.Require(child).SleepWord, "helper sleep must be the actual host sleep word");
            Equal(3, Record(rig, "Sleep", child).Value, "first helper delay must reach host unchanged");
            int prints = rig.Effects.Prints.Count; long entryWake = rig.Host.Require(child).LastWakeTick;
            NoSuffix(rig);
            for (int remaining = 2; remaining >= 1; remaining--)
            {
                rig.Tape.Clear(); rig.Tick(child);
                Equal(remaining, rig.Host.Require(child).SleepRemaining, "sleep countdown must use actual host visits");
                Equal(entryWake, rig.Host.Require(child).LastWakeTick, "sleep countdown is not a helper wake");
                Equal(1, wait.Advances, "sleep countdown must not advance the helper early");
                Equal(0, rig.Tape.Entries.Count, "sleep countdown must not rerun setup or helper");
                NoSuffix(rig);
            }
            rig.Tape.Clear(); rig.Tick(child);
            Equal("begin:plr_ko_idiot_box_proc|wait:advance", rig.Tape.Joined,
                "later child wake must resume only the existing helper");
            Equal(2, wait.Advances, "same helper must advance on later actual wake");
            Equal((ushort)1, rig.Host.Require(child).SleepWord, "second declared helper delay must reach host");
            NoSuffix(rig);
            rig.Tape.Clear(); rig.Tick(child);
            Equal(3, wait.Advances, "helper Returned must be consumed exactly once");
            Equal(1, rig.Waits.Calls.Count, "helper must not be recreated after sleep");
            Equal(prints, rig.Effects.Prints.Count, "helper resumes must not repeat presentation");
            Equal(1, rig.Tape.Count("wait:advance"), "one helper advance on return wake");
            Equal(0, Records(rig, "Sleep", child), "helper return must not invent a final child delay");
            Equal(1, rig.Effects.Sounds.Count, "helper return alone releases suffix");
            Equal(1, rig.Returns.Calls.Count, "normal source return occurs after helper Returned");
            rig.Tape.Clear(); rig.Tick(child);
            Equal(0, rig.Tape.Entries.Count, "source terminal child must not repeat helper or suffix");
            Equal(0, rig.Host.Records.Count, "source terminal child must not wake again");
        }

        private static void SoundAndIdMaskCleanup()
        {
            var rig = new Rig(); rig.Start(); long child = rig.Children[1];
            rig.Waits.Script = new[] { SourceKickoffActivePlayerWaitStep.Returned() };
            rig.Tick(child);
            Equal(1, rig.Effects.Sounds.Count, "child ricochet must be attempted once after helper return");
            SourceKickoffInstructionChildSound sound = rig.Effects.Sounds[0];
            Equal("plyr_bnk_str", sound.BankSymbol, "child sound bank operand");
            Equal("RICOCHET_SND", sound.SoundSymbol, "child sound symbol operand");
            Equal("VOLUME1", sound.VolumeSymbol, "child sound must not reuse page-advance volume");
            Equal("PRIORITY_LVL4", sound.PrioritySymbol, "child sound must not reuse page-advance priority");
            Equal(127, sound.Pan, "child ricochet pan operand");
            Equal(1, rig.Effects.StringDeletes.Count, "slot strings are cleaned once");
            Equal(0x35, rig.Effects.StringDeletes[0].Id, "string cleanup must target fifty-two plus original slot");
            Equal(uint.MaxValue, rig.Effects.StringDeletes[0].Mask, "string cleanup must preserve full thirty-two-bit mask");
            Equal(child, rig.Effects.StringDeletes[0].Caller, "string cleanup executes in original child");
            Equal(1, rig.Effects.PlateDeletes.Count, "slot sprite class is cleaned once");
            Equal("OID_IDIOTBOX", rig.Effects.PlateDeletes[0].BaseId, "sprite cleanup must target source object-ID class");
            Equal(1, rig.Effects.PlateDeletes[0].Id, "sprite cleanup must target original slot offset");
            Equal(uint.MaxValue, rig.Effects.PlateDeletes[0].Mask, "sprite cleanup must preserve full thirty-two-bit mask");
            Order(rig.Tape, "wait:create", "wait:advance", "effect:sound", "effect:delete-strings",
                "effect:delete-plate-class", "return:NormalBodyEnd");
            Equal(SourceKickoffInstructionChildReturnReason.NormalBodyEnd, rig.Returns.Calls[0].Reason,
                "normal source return follows both cleanup attempts");
            Equal(2, Record(rig, "SourceChildFallthrough", child).Value, "normal source body end is distinct reason two");
            Equal(0, Records(rig, "NativeChildDie"), "normal child end contains no native die");
            Equal(0, Records(rig, "Sleep", child), "normal suffix contains no extra source sleep");
            Equal(0, Records(rig, "ResumeAllCall"), "child suffix contains no parent resume-all");
            Equal(1, rig.FlashSerials().Count, "slot cleanup must not cancel the actual flash");
            Equal(0.0f, rig.Effects.TextZ, "suffix must not restore shared text-z");
            rig.Tape.Clear(); rig.Tick(child);
            Equal(0, rig.Tape.Entries.Count, "suffix cannot repeat on later dispatch");
            Equal(1, rig.Effects.StringDeletes.Count, "string cleanup stays once-only");
            Equal(1, rig.Effects.PlateDeletes.Count, "sprite cleanup stays once-only");
        }

        private static void ExplicitDieVersusReturn()
        {
            foreach (bool inactive in new[] { true, false })
            {
                var rig = new Rig(); rig.Start(); long child = rig.Children[0], sibling = rig.Children[1];
                rig.State.Status = inactive ? 0 : 3; rig.State.Games[0] = 3;
                rig.Tick(child);
                Record(rig, "NativeChildDie", child); Record(rig, "Die", child);
                Check(!rig.Host.Contains(child) && rig.Host.Contains(sibling), "native die must retire only its own child");
                Equal(0, rig.Returns.Calls.Count, "native guard die must bypass source-return binding");
                Equal(0, Records(rig, "SourceChildFallthrough"), "native guard die cannot be source fallthrough");
            }
            foreach (SourceKickoffInstructionChildReturnAction action in new[] {
                SourceKickoffInstructionChildReturnAction.KeepLive, SourceKickoffInstructionChildReturnAction.ExternallyRetire })
            {
                var rig = new Rig(); rig.Start(); long child = rig.Children[0], sibling = rig.Children[1];
                rig.Returns.Action = action; rig.Waits.Script = new[] { SourceKickoffActivePlayerWaitStep.Returned() };
                rig.Tick(child);
                Record(rig, "SourceChildFallthrough", child);
                Equal(0, Records(rig, "NativeChildDie"), "normal return retirement cannot be labeled native die");
                Equal(action == SourceKickoffInstructionChildReturnAction.KeepLive, rig.Host.Contains(child),
                    "normal source return requires the explicit lifecycle action");
                Equal(1, Records(rig, action == SourceKickoffInstructionChildReturnAction.KeepLive
                    ? "ExternalChildReturnKeepLive" : "ExternalChildReturnRetirement", child),
                    "normal return lifecycle evidence must be explicitly external");
                rig.State.Games[1] = 3; rig.Tick(sibling);
                Check(!rig.Host.Contains(sibling), "other child exits through its own observed guard");
                Equal(action == SourceKickoffInstructionChildReturnAction.KeepLive, rig.Host.Exists(Rig.ChildId, -1),
                    "real host existence must retain a KeepLive terminal child");
                rig.Tick(rig.Parent);
                if (action == SourceKickoffInstructionChildReturnAction.KeepLive)
                {
                    Equal(1, Record(rig, "Sleep", rig.Parent).Value, "KeepLive must keep actual parent in sleep-one wait");
                    Equal(0, Records(rig, "ResumeAllCall"), "KeepLive must not release parent");
                }
                else
                {
                    Record(rig, "ResumeAllCall", rig.Parent); Record(rig, "Fallthrough", rig.Parent);
                    Check(rig.Host.Contains(rig.Parent), "accepted parent fallthrough is not guessed native retirement");
                    Equal(1, rig.FlashSerials().Count, "PID-zero flash must not be counted as a live child-PID match");
                }
                Equal(1, rig.State.Flag, "normal return must not invent flag two");
            }
        }

        private static void ParentAnyLiveMatchAndSharedAllocation()
        {
            var rig = new Rig(); rig.Catch();
            rig.OnCommit = serial =>
            {
                if (rig.Dispatcher.OwnsKickoffChild(serial)
                    && rig.Dispatcher.KickoffChildInvocation(serial).Argument0 == 1) rig.Inputs.Version = 1;
            };
            rig.Tick(rig.Parent); rig.CaptureChildren(); rig.OnCommit = null;
            Equal(4, rig.Children.Count, "parent must read live version only after children zero and one");
            Equal(0, rig.Effects.Plates.Count, "parent must not execute source child inline");
            Equal(0, rig.LegacyChildren.Created, "source binding must not create external whole-child continuations");
            Equal(1, rig.Tape.Count("read:version:"), "parent creation has only its own version read");
            for (int slot = 0; slot < 4; slot++)
            {
                long child = rig.Children[slot];
                SourceKickoffInstructionChildInvocation invocation = rig.Dispatcher.KickoffChildInvocation(child);
                Equal(slot, invocation.Argument0, "parent exact child creation slot order");
                Equal(0, invocation.Argument1 | invocation.Argument2 | invocation.Argument3, "parent exact trailing arguments");
                Equal("idiots", invocation.Name, "parent exact child name");
                Equal(Rig.ChildId, rig.Host.Require(child).Id, "source child uses accepted parent PID");
                Check(!rig.Host.Require(child).Suspended, "parent specifically resumes created child PID");
                rig.State.Games[slot] = 3;
            }
            Equal(32768, Record(rig, "SuspendMask", rig.Parent).Value, "parent suspend mask is preserved");
            Equal(-1, Record(rig, "ResumeMask", rig.Parent).Value, "parent exact child resume mask is preserved");
            Equal(Rig.ChildId, Record(rig, "ResumeMask", rig.Parent).Id, "parent resumes exact child ID");
            long foreign = rig.Foreign(Rig.ChildId, "component_foreign_matching", 0x8001);
            long neighbor = rig.Foreign(Rig.ChildId ^ 1, "component_foreign_neighbor", 0x8001);
            for (int i = 1; i < rig.Commits.Count; i++)
                Equal(rig.Commits[i - 1] + 1, rig.Commits[i], "all source and unrelated allocations share one serial stream");
            rig.Tick(rig.Children.ToArray());
            foreach (long child in rig.Children) Check(!rig.Host.Contains(child), "owned child guard exits through actual host");
            Check(rig.Host.Require(foreign).Suspended, "unrelated matching entry remains suspended and alive");
            Check(rig.Host.Exists(Rig.ChildId, -1), "actual existence must include unrelated suspended matching PID");
            rig.Tick(rig.Parent);
            Equal(1, Record(rig, "Sleep", rig.Parent).Value, "ANY matching live PID must keep parent sleeping one");
            Equal(0, Records(rig, "ResumeAllCall"), "owned-child exhaustion cannot release parent early");
            rig.Host.Kill(foreign);
            rig.State.Status = 0; rig.State.StatusSamples.Enqueue(1);
            rig.State.StatusSamples.Enqueue(0); rig.State.StatusSamples.Enqueue(0); rig.State.StatusSamples.Enqueue(0);
            rig.Inputs.ControlSamples[0] = new Queue<int>(new[] { 5, 6, 7 });
            rig.Inputs.Current[6] = 0x80; rig.Inputs.Old[7] = 0x100;
            rig.Tape.Clear(); rig.Tick(rig.Parent);
            Record(rig, "ResumeAllCall", rig.Parent); Record(rig, "Fallthrough", rig.Parent);
            Check(rig.Host.Contains(neighbor) && !rig.Host.Require(neighbor).Suspended, "resume-all preserves unrelated live neighbor");
            Equal(0xb0, rig.Inputs.Current[6], "accepted parent ORs A and B into fresh current-control target");
            Equal(0x130, rig.Inputs.Old[7], "accepted parent rereads control before old-button target");
            Equal(0, rig.Inputs.Current[5], "parent guard control value must not replace current target");
            Equal(1, rig.Tape.Count("current:write:"), "parent mutates only live participating current target");
            Equal(1, rig.Tape.Count("old:write:"), "parent mutates only live participating old target");
            Order(rig.Tape, "read:status:1", "read:control:0:5", "read:control:0:6", "current:read:6",
                "current:write:6:176", "read:control:0:7", "old:read:7", "old:write:7:304", "read:status:0");
            Equal(1, rig.State.Flag, "accepted parent epilogue still has no guessed flag-two write");
            Equal(0, Records(rig, "Die", rig.Parent), "accepted parent fallthrough still has no native die");
        }

        private static void SharedResourceAndFlashLifetime()
        {
            var rig = new Rig(); rig.Start(); long first = rig.Children[0], second = rig.Children[1];
            rig.Waits.Script = new[] { SourceKickoffActivePlayerWaitStep.Sleep(1), SourceKickoffActivePlayerWaitStep.Returned() };
            rig.Tick(first); long flash0 = rig.FlashSerials()[0];
            rig.Tick(second); List<long> flashes = rig.FlashSerials(); long flash1 = flashes[1];
            Equal(2, rig.Effects.Plates.Count, "interleaved source children each finish their own plate setup");
            Plate plate0 = rig.Effects.Plates[0], plate1 = rig.Effects.Plates[1];
            Equal(first, plate0.Owner, "first plate setup belongs to first actual caller");
            Equal(second, plate1.Owner, "second plate setup belongs to second actual caller");
            Check(ReferenceEquals(plate1, rig.Effects.LastPlate), "synthetic shared last pointer now identifies second plate");
            Equal(1, plate0.Vertices, "first setup completed before yielding");
            Equal(1, plate1.Vertices, "second setup completed before yielding");
            rig.Tick(first);
            Check(plate0.Deleted && !plate1.Deleted, "first cleanup must target own ID class, not last shared plate handle");
            Check(!rig.Effects.LiveStringIds.Contains(52) && rig.Effects.LiveStringIds.Contains(53),
                "first cleanup must retain other child's strings");
            Equal(53, rig.Effects.StringId, "cleanup must not restore the other child's shared string-ID state");
            Check(rig.Host.Contains(flash0) && rig.Host.Contains(flash1), "first cleanup must not cancel either real flash");
            rig.Tick(second);
            Check(plate1.Deleted, "second cleanup must target second ID class");
            Equal(2, rig.Effects.PlateDeletes.Count, "each source child performs its own once-only ID cleanup");
            Equal(0, rig.Effects.LiveStringIds.Count, "both slot string groups cleaned without global-delete shortcut");
            Equal(2, rig.Effects.ZWrites, "only setup writes shared text-z");
            Equal(0.0f, rig.Effects.TextZ, "neither cleanup restores shared text-z");
            Equal(0, rig.Flashes.Calls.Count, "source child cleanup must not inline deferred flash bodies");
            Check(rig.Host.Contains(flash0) && rig.Host.Contains(flash1), "both flash lifetimes outlive child cleanup");
            rig.Tick(flash0, flash1);
            Equal(2, rig.Flashes.Calls.Count, "both deferred flashes eventually dispatch on actual wakes");
            rig.Tick(flash0, flash1); rig.Tick(flash0, flash1); rig.Tick(flash0, flash1);
            Check(!rig.Host.Contains(flash0) && !rig.Host.Contains(flash1), "only explicit external flash scripts retire flashes");
            Equal(2, Records(rig, "ExternalFlashRetirement"), "flash retirement must be explicitly labeled external");
            Equal(0, Records(rig, "NativeChildDie"), "external flash retirement is not observed child die");
            Check(rig.Host.Contains(first) && rig.Host.Contains(second), "flash retirement must not retire KeepLive source children");
        }

        private sealed class FaultPoint
        {
            internal readonly string Operation;
            internal readonly int Occurrence;
            internal FaultPoint(string operation, int occurrence = 1) { Operation = operation; Occurrence = occurrence; }
        }
        private static void FailClosedWithoutInventedFinally()
        {
            FaultPoint[] points = {
                new FaultPoint("begin:plr_ko_idiot_box_proc"),
                new FaultPoint("read:version:0"), new FaultPoint("read:version:0", 2), new FaultPoint("read:version:0", 3),
                new FaultPoint("read:status:3"), new FaultPoint("read:games:0:0"),
                new FaultPoint("serial:next"), new FaultPoint("serial:commit"),
                new FaultPoint("effect:z"), new FaultPoint("effect:plate"), new FaultPoint("effect:angle"),
                new FaultPoint("effect:hscale"), new FaultPoint("effect:object-id"), new FaultPoint("effect:verts"),
                new FaultPoint("effect:font:Bast10"), new FaultPoint("effect:string-id:52"),
                new FaultPoint("effect:color:LightBlue"), new FaultPoint("effect:position"),
                new FaultPoint("effect:print:4583"), new FaultPoint("read:carrier:9"),
                new FaultPoint("read:control:0:0"), new FaultPoint("read:control:0:0", 2),
                new FaultPoint("read:team:0"), new FaultPoint("read:offense:0"),
                new FaultPoint("effect:font:Bast8T"), new FaultPoint("effect:color:LightYellow"),
                new FaultPoint("read:low:0"), new FaultPoint("read:low:0", 11),
                new FaultPoint("effect:position", 12), new FaultPoint("effect:print:4648"),
                new FaultPoint("read:tsec:7"), new FaultPoint("wait:create"), new FaultPoint("wait:advance"),
                new FaultPoint("effect:sound"), new FaultPoint("effect:delete-strings"),
                new FaultPoint("effect:delete-plate-class"), new FaultPoint("return:NormalBodyEnd")
            };
            foreach (FaultPoint point in points)
            {
                var rig = new Rig(); rig.Start(); long child = rig.Children[0]; Choose(rig, "support");
                rig.Waits.Script = new[] { SourceKickoffActivePlayerWaitStep.Returned() };
                rig.Tape.FailAt(point.Operation, point.Occurrence);
                InvalidOperationException error = Throws<InvalidOperationException>(() => rig.Tick(child),
                    "required source boundary must surface failure at " + point.Operation);
                Equal("synthetic component fault at " + point.Operation, error.Message, "required source failure must not be swallowed");
                Equal(point.Operation, rig.Tape.Entries[rig.Tape.Entries.Count - 1],
                    "no later source call or invented finally cleanup may follow failed " + point.Operation);
                Equal(0L, rig.Host.Caller, "failed source call must still release dispatcher caller");
                Equal(0, Records(rig, "NativeChildDie"), "provider failure cannot be relabeled observed native die");
                Equal(0, Records(rig, "ExternalChildReturnKeepLive"), "failed source call cannot become successful return");
                Check(rig.Host.Contains(child), "provider fault must not invent child retirement");
                FaultCannotReplay(rig, child);
                Throws<InvalidOperationException>(() => rig.Tick(rig.Parent), "faulted child must prevent parent successful progress");
                Equal("begin:ko_idiot_box_proc", rig.Tape.Joined, "faulted parent may enter dispatch but must not run epilogue providers");
                Equal(0, Records(rig, "ResumeAllCall"), "faulted child must not release parent suspension");
            }
            foreach (string missing in new[] { "plate", "wait", "wait-step", "return-action", "serial" })
            {
                var rig = new Rig(); rig.Start(); long child = rig.Children[0];
                if (missing == "plate") rig.Effects.NullPlate = true;
                if (missing == "wait") rig.Waits.NullResult = true;
                if (missing == "wait-step") rig.Waits.Script = new[] { default(SourceKickoffActivePlayerWaitStep) };
                if (missing == "return-action")
                { rig.Waits.Script = new[] { SourceKickoffActivePlayerWaitStep.Returned() }; rig.Returns.Action = 0; }
                if (missing == "serial") rig.OverrideNext = () => child;
                if (missing == "serial")
                    Throws<ArgumentException>(() => rig.Tick(child), "duplicate flash serial must fail at actual host creation");
                else
                    Throws<InvalidOperationException>(() => rig.Tick(child), "invalid required source result must fail for " + missing);
                Equal(0, Records(rig, "ExternalChildReturnKeepLive"), "invalid source result cannot become successful return");
                if (missing != "return-action") NoSuffix(rig);
                FaultCannotReplay(rig, child);
            }
            var resumed = new Rig(); resumed.Start(); long resumedChild = resumed.Children[0];
            resumed.Waits.Script = new[] { SourceKickoffActivePlayerWaitStep.Sleep(1), SourceKickoffActivePlayerWaitStep.Returned() };
            resumed.Tick(resumedChild); resumed.Tape.Clear(); resumed.Tape.FailAt("wait:advance");
            Throws<InvalidOperationException>(() => resumed.Tick(resumedChild), "resumed active-player helper failure must surface");
            Equal("wait:advance", resumed.Tape.Entries[resumed.Tape.Entries.Count - 1],
                "resumed helper failure must not release suffix");
            NoSuffix(resumed); FaultCannotReplay(resumed, resumedChild);
            foreach (string failure in new[] { "flash:create", "flash:advance", "null", "invalid", "source-step" })
            {
                var rig = new Rig(); rig.Start(); long child = rig.Children[0]; rig.Tick(child);
                long flash = rig.FlashSerials()[0]; rig.Tape.Clear();
                if (failure == "null") rig.Flashes.NullResult = true;
                else if (failure == "invalid") rig.Flashes.Script = new[] { default(SourceKickoffInstructionChildStep) };
                else if (failure == "source-step")
                    rig.Flashes.Script = new[] { SourceKickoffInstructionChildStep.NativeDie(0) };
                else rig.Tape.FailAt(failure);
                Throws<InvalidOperationException>(() => rig.Tick(flash), "required flash boundary must fail for " + failure);
                Check(rig.Host.Contains(flash), "failed external flash must not invent retirement");
                Equal(0, Records(rig, "ExternalFlashRetirement"), "failed flash cannot become successful retirement");
                FaultCannotReplay(rig, flash);
                rig.Tick(child);
                Throws<InvalidOperationException>(() => rig.Tick(child), "flash fault must block later child suffix progress");
                NoSuffix(rig);
            }
        }

        private static void PortableIndexAndTimeDomain()
        {
            var fixture = new Rig();
            Throws<ArgumentException>(() => new SourceKickoffInstructionChildOperands(false, float.NaN, 2, 0, 1),
                "portable BARX1 binding must be finite");
            Throws<ArgumentException>(() => new SourceKickoffInstructionChildOperands(false, 1, float.PositiveInfinity, 0, 1),
                "portable BARX2 binding must be finite");
            Throws<ArgumentException>(() => new SourceKickoffInstructionChildOperands(false, 1, 2, -1, 1),
                "portable X component must be explicit and supported");
            Throws<ArgumentException>(() => new SourceKickoffInstructionChildOperands(false, 1, 2, 0, 2),
                "portable Y component must be explicit and supported");
            Throws<ArgumentNullException>(() => new SourceKickoffInstructionChildBinding(null, fixture.Effects, fixture.Waits, fixture.Flashes, fixture.Returns),
                "source child requires explicit operands");
            Throws<ArgumentNullException>(() => new SourceKickoffInstructionChildBinding(fixture.ChildBinding.Operands, null, fixture.Waits, fixture.Flashes, fixture.Returns),
                "source child requires explicit effects");
            Throws<ArgumentNullException>(() => new SourceKickoffInstructionChildBinding(fixture.ChildBinding.Operands, fixture.Effects, null, fixture.Flashes, fixture.Returns),
                "source child requires player-specific wait");
            Throws<ArgumentNullException>(() => new SourceKickoffInstructionChildBinding(fixture.ChildBinding.Operands, fixture.Effects, fixture.Waits, null, fixture.Returns),
                "source child requires actual flash binding");
            Throws<ArgumentNullException>(() => new SourceKickoffInstructionChildBinding(fixture.ChildBinding.Operands, fixture.Effects, fixture.Waits, fixture.Flashes, null),
                "source child requires explicit source-return policy");
            Throws<ArgumentNullException>(() => SourceKickoffInstructionCoordinator.WithSourceChild(Rig.ChildId, 4, Rig.A, Rig.B,
                fixture.Inputs, fixture.Inputs, null), "new source construction cannot silently become legacy");
            Throws<ArgumentNullException>(() => new SourceKickoffInstructionCoordinator(Rig.ChildId, 4, Rig.A, Rig.B,
                fixture.Inputs, fixture.Inputs, null), "legacy constructor must still require whole-child factory");
            Throws<ArgumentException>(() => SourceKickoffInstructionCoordinator.WithSourceChild(Rig.ChildId, 0, Rig.A, Rig.B,
                fixture.Inputs, fixture.Inputs, fixture.ChildBinding), "parent portable player domain is unchanged");
            Throws<ArgumentException>(() => SourceKickoffInstructionCoordinator.WithSourceChild(Rig.ChildId, 4, 0, Rig.B,
                fixture.Inputs, fixture.Inputs, fixture.ChildBinding), "source path must not guess an A mask");
            Equal(0, fixture.Tape.Entries.Count, "configuration validation must not execute providers");
            foreach (int version in new[] { -1, 2 })
                for (int read = 1; read <= 3; read++)
                {
                    var rig = new Rig(); rig.Start(); long child = rig.Children[0];
                    for (int before = 1; before < read; before++) rig.Inputs.Versions.Enqueue(0);
                    rig.Inputs.Versions.Enqueue(version);
                    Throws<ArgumentOutOfRangeException>(() => rig.Tick(child), "unsupported live table version must be a portable binding failure");
                    Equal(read, rig.Tape.Count("read:version:"), "version domain failure must occur at the actual live read");
                    Equal(0, Records(rig, "NativeChildDie"), "version failure is not an extra source participation guard");
                    Equal(0, rig.Waits.Calls.Count, "version failure must not silently substitute a table row");
                    NoSuffix(rig); FaultCannotReplay(rig, child);
                }
            foreach (int tsec in new[] { -1, int.MaxValue / 6 + 1 })
            {
                var rig = new Rig(); rig.Start(); long child = rig.Children[0]; rig.Inputs.Tsec = tsec;
                Throws<ArgumentOutOfRangeException>(() => rig.Tick(child), "unsupported six-times time domain must fail without wrapping");
                Equal(1, rig.Tape.Count("read:tsec:"), "time domain is checked at helper-call source point");
                Equal(0, rig.Waits.Calls.Count, "unsupported time domain must not install a successful wait");
                Equal(0, Records(rig, "NativeChildDie"), "time binding failure is not observed native die");
                NoSuffix(rig); FaultCannotReplay(rig, child);
            }
            foreach (int tsec in new[] { 0, int.MaxValue / 6 })
            {
                var rig = new Rig(); rig.Start(); long child = rig.Children[0]; rig.Inputs.Tsec = tsec;
                rig.Waits.Script = new[] { SourceKickoffActivePlayerWaitStep.Sleep(0x7fff) };
                rig.Tick(child);
                Equal(6 * tsec, rig.Waits.Calls[0].Timeout, "representable six-times timeout must retain exact value");
                Equal((ushort)0x7fff, rig.Host.Require(child).SleepWord, "portable helper retains full admitted positive sleep domain");
                NoSuffix(rig);
            }
            Throws<ArgumentException>(() => SourceKickoffActivePlayerWaitStep.Sleep(0), "helper sleep zero cannot silently mean return");
            Throws<ArgumentException>(() => SourceKickoffActivePlayerWaitStep.Sleep(0x8000), "helper sleep must not encode suspension");
            Throws<ArgumentException>(() => SourceKickoffInstructionChildStep.SourceFallthrough(0), "source return reason must be explicit");
        }

        private static void CompatibilityAndNoGameplayWrites()
        {
            var rig = new Rig();
            Check(ReferenceEquals(rig.ChildBinding, rig.Config.SourceChild) && rig.Config.ChildContinuations == null,
                "source construction must bind exactly the owned path");
            Equal(0, rig.Effects.Plates.Count, "source constructor must not execute presentation");
            Equal(0, rig.Waits.Calls.Count, "source constructor must not execute helper");
            Equal(0, rig.Flashes.Calls.Count, "source constructor must not execute flash");
            Equal(0L, rig.Dispatcher.LastKickoffSerial, "dispatcher constructor must not admit a default parent");
            Equal(0, rig.State.Flag, "source initialization remains flag zero with human status");
            var noHuman = new Rig(status: 0);
            Equal(2, noHuman.State.Flag, "source initialization remains flag two without human status");
            rig.Start(); long child = rig.Children[0];
            int[] games = (int[])rig.State.Games.Clone(), controls = (int[])rig.Inputs.Controls.Clone();
            int[] current = (int[])rig.Inputs.Current.Clone(), old = (int[])rig.Inputs.Old.Clone();
            int status = rig.State.Status, flagWrites = rig.State.FlagWrites.Count;
            rig.Waits.Script = new[] { SourceKickoffActivePlayerWaitStep.Returned() }; rig.Tick(child);
            Equal(status, rig.State.Status, "owned child must not write participation");
            Equal(flagWrites, rig.State.FlagWrites.Count, "owned child must not write completion flag");
            for (int i = 0; i < 4; i++)
            {
                Equal(games[i], rig.State.Games[i], "owned child must not write experience");
                Equal(controls[i], rig.Inputs.Controls[i], "owned child must not write player control");
            }
            for (int i = 0; i < current.Length; i++)
            {
                Equal(current[i], rig.Inputs.Current[i], "owned child must not write current buttons");
                Equal(old[i], rig.Inputs.Old[i], "owned child must not write old buttons");
            }
            Equal(0, rig.Tape.Count("flag:write:"), "child cannot invent completion flag write");
            Equal(0, rig.Tape.Count("current:write:"), "child cannot absorb parent's button writes");
            Equal(0, rig.Tape.Count("old:write:"), "child cannot absorb parent's old-button writes");
            Equal(0, rig.LegacyChildren.Created, "owned child cannot fall back to external whole body");
            Equal(0, rig.LegacySuffix.Created, "owned child cannot fall back to external parent suffix");
            Equal(0, Records(rig, "ResumeAllCall"), "owned child cannot resume parent-owned processes");

            var legacy = new Rig(source: false); legacy.Start(); long oldChild = legacy.Children[0];
            Check(legacy.Config.SourceChild == null && ReferenceEquals(legacy.Config.ChildContinuations, legacy.LegacyChildren),
                "existing constructor retains explicit legacy factory path");
            legacy.Tick(oldChild);
            Equal((ushort)2, legacy.Host.Require(oldChild).SleepWord, "old step one still sleeps actual child");
            legacy.Tick(oldChild); legacy.Tick(oldChild);
            Record(legacy, "ExternalChildFallthrough", oldChild);
            Check(legacy.Host.Contains(oldChild), "old step two still leaves external fallthrough live");
            Equal(0, legacy.Effects.Prints.Count, "legacy path does not accidentally run source child");
            Equal(0, legacy.Returns.Calls.Count, "legacy fallthrough does not acquire new source-return policy");
            legacy.Tape.Clear(); legacy.Tick(oldChild);
            Equal(0, legacy.Tape.Entries.Count, "legacy fallthrough still parks terminal body");
            var retired = new Rig(source: false); retired.Start(); long retiredChild = retired.Children[0];
            retired.LegacyChildren.Script = new[] { SourceKickoffInstructionChildStep.ExternalRetirement() };
            retired.Tick(retiredChild);
            Check(!retired.Host.Contains(retiredChild), "old step three still externally retires actual serial");
            Equal(0, Records(retired, "NativeChildDie"), "old external retirement cannot be relabeled native die");
            Equal(0, retired.Returns.Calls.Count, "old external retirement cannot call source return binding");

            var flashFallthrough = new Rig(); flashFallthrough.Start();
            flashFallthrough.Flashes.Script = new[] { SourceKickoffInstructionChildStep.ExternalFallthrough() };
            flashFallthrough.Tick(flashFallthrough.Children[0]); long liveFlash = flashFallthrough.FlashSerials()[0];
            flashFallthrough.Tick(liveFlash);
            Record(flashFallthrough, "ExternalFlashFallthrough", liveFlash);
            Check(flashFallthrough.Host.Contains(liveFlash), "external flash fallthrough must not silently retire");
            flashFallthrough.Tape.Clear(); flashFallthrough.Tick(liveFlash);
            Equal(0, flashFallthrough.Tape.Entries.Count, "terminal external flash must not replay");
            Equal(0, flashFallthrough.Host.Records.Count, "terminal external flash must not synthesize another wake");

            var suffix = new Rig(legacySuffix: true); suffix.Catch(); suffix.Tick(suffix.Parent); suffix.Tick(suffix.Parent);
            Equal(1, suffix.LegacySuffix.Created, "legacy parent constructor still uses explicit factory");
            Equal(2, suffix.LegacySuffix.Advances, "legacy parent sleep then external fallthrough remains supported");
            Record(suffix, "ExternalFallthrough", suffix.Parent);
            Check(suffix.Host.Contains(suffix.Parent), "legacy parent fallthrough remains live");
            Equal(0, suffix.Effects.Prints.Count, "legacy parent path does not acquire source child");

            var manual = new Rig(initialize: false);
            manual.Dispatcher.Admit(100, manual.Invocation); manual.Tick(100);
            Equal(SourceInstructionStage.EntryDelay, manual.Dispatcher.Snapshot(100).Stage, "manual invocation retains original entry stage");
            Equal((ushort)2, manual.Host.Require(100).SleepWord, "manual invocation retains original entry delay");
            Equal(-1, manual.State.Flag, "manual invocation retains original flag ownership");
            Equal(0L, manual.Dispatcher.LastKickoffSerial, "manual invocation does not auto-enable kickoff");
            Equal(0, manual.Effects.Prints.Count, "manual invocation does not run child effects");
            Equal(1, (int)SourceKickoffInstructionChildStepKind.Sleep, "old sleep enum value remains one");
            Equal(2, (int)SourceKickoffInstructionChildStepKind.ExternalFallthrough, "old external fallthrough enum remains two");
            Equal(3, (int)SourceKickoffInstructionChildStepKind.ExternalRetirement, "old external retirement enum remains three");
            Equal(4, (int)SourceKickoffInstructionChildStepKind.NativeDie, "native die is a new distinct enum value");
            Equal(5, (int)SourceKickoffInstructionChildStepKind.SourceFallthrough, "source fallthrough is a new distinct enum value");
        }

        private static int Main(string[] args)
        {
            string[] names = {
                "debug-compile-choice", "coordinates-before-inactive-guard", "live-experience-threshold",
                "real-flash-creation", "sprite-setup", "all-layout-and-header-rows", "carrier-precedence",
                "offense-control-reread", "defense-and-negative-index-boundary", "per-position-low-resolution",
                "active-player-wait-boundary", "sound-and-id-mask-cleanup", "explicit-die-versus-return",
                "parent-any-live-match-and-shared-allocation", "shared-resource-and-flash-lifetime",
                "fail-closed-without-invented-finally", "portable-index-and-time-domain",
                "compatibility-and-no-gameplay-writes"
            };
            Action[] cases = {
                DebugCompileChoice, CoordinatesBeforeInactiveGuard, LiveExperienceThreshold,
                RealFlashCreation, SpriteSetup, AllLayoutAndHeaderRows, CarrierPrecedence,
                OffenseControlReread, DefenseAndNegativeIndexBoundary, PerPositionLowResolution,
                ActivePlayerWaitBoundary, SoundAndIdMaskCleanup, ExplicitDieVersusReturn,
                ParentAnyLiveMatchAndSharedAllocation, SharedResourceAndFlashLifetime,
                FailClosedWithoutInventedFinally, PortableIndexAndTimeDomain, CompatibilityAndNoGameplayWrites
            };
            int selected = -1;
            if (args.Length != 0 && (args.Length != 2 || args[0] != "--case"
                || (selected = Array.IndexOf(names, args[1])) < 0))
            { Console.WriteLine("ERROR invalid explicit case selector"); return 2; }
            int count = 0, total = 0;
            for (int i = 0; i < cases.Length; i++)
            {
                if (selected >= 0 && selected != i) continue;
                assertions = 0;
                try
                {
                    cases[i]();
                    Check(assertions > 0, "selected causal case must execute positive assertions");
                    Console.WriteLine("PASS " + names[i] + " assertions=" + assertions);
                }
                catch (TestFailure error) { Console.WriteLine("FAIL " + names[i] + ": " + error.Message); return 1; }
                catch (Exception error)
                { Console.WriteLine("ERROR " + names[i] + ": " + error.GetType().Name + ": " + error.Message); return 2; }
                count++; total += assertions;
            }
            if (count <= 0 || total <= 0)
            { Console.WriteLine("ERROR no positive causal cases"); return 2; }
            Console.WriteLine("SUMMARY component_cases=" + count + " assertions=" + total
                + " failures=0 Game=0 Unity=0 native=0");
            return 0;
        }
    }
}
