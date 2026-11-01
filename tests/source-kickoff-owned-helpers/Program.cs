using System;
using System.Collections.Generic;
using Blitz.Contracts;
using Blitz.Gameplay;

namespace Blitz.SourceKickoffOwnedHelpersComponent
{
    internal static class Program
    {
        private sealed class TestFailure : Exception
        { internal TestFailure(string message) : base(message) { } }
        private sealed class PrimitiveFailure : Exception
        { internal PrimitiveFailure() : base("injected primitive failure") { } }
        private static int assertions;
        private static void Check(bool condition, string why)
        {
            assertions++;
            if (!condition) throw new TestFailure(why);
        }
        private static void Equal<T>(T expected, T actual, string why)
        {
            assertions++;
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
                throw new TestFailure(why + ": expected " + expected + ", got " + actual);
        }
        private static void Throws<T>(Action action, string why) where T : Exception
        {
            assertions++;
            try { action(); }
            catch (T) { return; }
            catch (Exception error) { throw new TestFailure(why + ": got " + error.GetType().Name); }
            throw new TestFailure(why + ": no exception");
        }

        // Synthetic primitive/effect fixtures, never Game/Unity/native providers.
        // These masks deliberately do not alias the coordinator's button masks.
        private const uint A = 0x80, B = 0x20, C = 0x02, White = 0x400;
        private sealed class Plate : ISourceInstructionPlate { }
        private sealed class PlayerObject : ISourceKickoffPlayerObject
        {
            private readonly Fixture owner;
            internal uint Flags;
            internal int Reads, Writes, FailWriteAt;
            internal readonly List<uint> Written = new List<uint>();
            internal PlayerObject(Fixture owner, uint flags) { this.owner = owner; Flags = flags; }
            public uint ReadFlags()
            {
                Equal(owner.Flash, owner.Host.Caller, "flag read belongs to the actual flash wake");
                owner.Tape.Add("flags-read"); Reads++; return Flags;
            }
            public void WriteFlags(uint value)
            {
                Equal(owner.Flash, owner.Host.Caller, "flag write belongs to the actual flash wake");
                owner.Tape.Add("flags-write"); Writes++;
                if (Writes == FailWriteAt) throw new PrimitiveFailure();
                Flags = value; Written.Add(value);
            }
        }

        private sealed class Fixture : ISourceInstructionAdmissionState, ISourceInstructionInputs,
            ISourceInstructionEffects, ISourceKickoffInstructionChildInputs,
            ISourceKickoffInstructionPlayerButtons, ISourceKickoffInstructionChildEffects,
            ISourceKickoffCurrentSwitches, ISourceKickoffPlayerObjects,
            ISourceKickoffInstructionChildReturnBinding, ISourceKickoffInstructionFlashReturnBinding,
            ISourceKickoffActivePlayerWaitFactory, ISourceKickoffFlashContinuationFactory
        {
            internal const int ChildId = 0x6b42, ParentA = 0x1000, ParentB = 0x2000;
            internal readonly SourcePortableProcessHost Host = new SourcePortableProcessHost();
            internal readonly SourceInstructionDispatcher Dispatcher;
            internal readonly SourceInstructionInvocation Invocation;
            internal readonly SourceInstructionAdmissionBinding Binding;
            internal readonly SourceKickoffInstructionChildOperands Operands =
                new SourceKickoffInstructionChildOperands(false, 101, 203, 0, 1);
            internal readonly SourceKickoffInstructionOwnedHelpers Helpers;
            internal readonly SourceKickoffInstructionChildBinding ChildBinding;
            internal readonly List<string> Tape = new List<string>();
            internal readonly List<int> Captures = new List<int>();
            internal readonly List<SourcePossessionProcessRecord> History = new List<SourcePossessionProcessRecord>();
            internal readonly PlayerObject[] Blocks;
            internal readonly int[] Games = new int[4], Controls = { 3, 2, 1, 0 };
            internal readonly int[] Current = new int[4], Old = new int[4];
            internal int Status = 1, Shown = 0, Tsec = 7;
            internal uint Raw = 0;
            internal int StatusReads, SwitchReads, ControlReads, FlagWrites, ButtonWrites;
            internal int ChildReturns, FlashReturns, LegacyWaitCreates, LegacyWaitAdvances;
            internal int LegacyFlashCreates, LegacyFlashAdvances;
            internal bool ThrowSwitch = false, ThrowFlashReturn = false;
            internal SourceKickoffInstructionChildReturnAction ChildAction =
                SourceKickoffInstructionChildReturnAction.KeepLive;
            internal SourceKickoffInstructionFlashReturnAction FlashAction =
                SourceKickoffInstructionFlashReturnAction.KeepLive;
            internal long Serial = 100, Parent, Child, Flash;

            internal Fixture(bool legacy = false)
            {
                Blocks = new[] { new PlayerObject(this, 1), new PlayerObject(this, 2),
                    new PlayerObject(this, 4), new PlayerObject(this, 8) };
                Helpers = new SourceKickoffInstructionOwnedHelpers(A, B, C, White, this, this, this);
                ChildBinding = legacy
                    ? new SourceKickoffInstructionChildBinding(Operands, this, this, this, this)
                    : SourceKickoffInstructionChildBinding.WithOwnedHelpers(Operands, this, Helpers, this);
                var coordinator = SourceKickoffInstructionCoordinator.WithSourceChild(
                    ChildId, 4, ParentA, ParentB, this, this, ChildBinding);
                Invocation = new SourceInstructionInvocation(4,
                    new SourceInstructionProcessIds(0x6b43, 0x502, 0x1400, 0x6b44), this, this);
                Binding = SourceInstructionAdmissionBinding.WithKickoffCoordinator(Invocation, this, coordinator);
                Dispatcher = new SourceInstructionDispatcher(Host);
                Host.Create(2, 0x500, "player_proc", 0);
            }
            internal void Initialize()
            { Dispatcher.InitializeAdmission(Binding, () => checked(Serial + 1), value => Serial = value); }
            internal void Dispatch(long serial)
            { Check(Dispatcher.Dispatch(serial, function => Tape.Add("begin:" + function)), "dispatcher owns continuation"); }
            internal void Tick(params long[] serials)
            {
                Host.BeginTick(Host.SourceTick + 1);
                try { foreach (long serial in serials) Dispatch(serial); }
                finally
                {
                    History.AddRange(Host.Records);
                    Equal(0L, Host.Caller, "dispatcher releases the actual caller, including on failure");
                }
            }
            internal void StartChild()
            {
                Initialize();
                Host.BeginTick(Host.SourceTick + 1);
                Check(Host.Visit(2), "real catch caller wakes");
                try
                {
                    long? admitted = Dispatcher.AdmitKickoffFromCatch(Binding, true, 2, () => checked(Serial + 1));
                    Check(admitted.HasValue, "real catch admits the parent");
                    Parent = admitted.Value; Serial = Parent;
                }
                finally { Host.Complete(); History.AddRange(Host.Records); }
                Tick(Parent);
                long other = 0;
                foreach (SourceProcessView process in Host.Processes)
                    if (Dispatcher.OwnsKickoffChild(process.Serial))
                    {
                        int slot = Dispatcher.KickoffChildInvocation(process.Serial).Argument0;
                        if (slot == 0) Child = process.Serial;
                        else if (slot == 1) other = process.Serial;
                    }
                Check(Child != 0 && other != 0, "actual coordinator creates both original-slot children");
                Tick(Child, other);
                foreach (SourceProcessView process in Host.Processes)
                    if (Dispatcher.OwnsKickoffFlash(process.Serial))
                    { Equal(0L, Flash, "one active child creates one flash"); Flash = process.Serial; }
                Check(Flash != 0, "source child creates an actual hosted flash");
            }
            internal void EightFlashTicks()
            { for (int tick = 0; tick < 8; tick++) Tick(Flash); }
            internal long Foreign(int id, ushort sleep)
            {
                long serial = checked(Serial + 1);
                Host.Create(serial, id, "foreign_proc", 0, sleep); Serial = serial; return serial;
            }
            internal int Records(string operation, long serial)
            {
                int count = 0;
                foreach (SourcePossessionProcessRecord record in History)
                    if (record.Operation == operation && record.Serial == serial) count++;
                return count;
            }
            internal bool HasRecord(string operation, long serial, int id, int value)
            {
                foreach (SourcePossessionProcessRecord record in History)
                    if (record.Operation == operation && record.Serial == serial
                        && record.Id == id && record.Value == value) return true;
                return false;
            }
            internal int Calls(string operation)
            {
                int count = 0;
                foreach (string entry in Tape) if (entry == operation) count++;
                return count;
            }
            public int ReadIdiotBoxesShown() { Tape.Add("shown-read"); return Shown; }
            public void WriteIdiotBoxesShown(int value) { Tape.Add("shown-write"); FlagWrites++; Shown = value; }
            public int ReadPlayerStatus() { Tape.Add("status"); StatusReads++; return Status; }
            public int ReadGamesPlayed(int slot) { Tape.Add("games"); return Games[slot]; }
            public uint ReadCurrentSwitchWord()
            {
                Tape.Add("switch"); SwitchReads++;
                if (ThrowSwitch) throw new PrimitiveFailure();
                return Raw;
            }
            public ISourceKickoffPlayerObject ReadPlayerBlock(int originalPlayerNumber)
            {
                Equal(Flash, Host.Caller, "player object capture is deferred to the actual flash wake");
                Tape.Add("capture"); Captures.Add(originalPlayerNumber); return Blocks[originalPlayerNumber];
            }
            public int ReadFourPlayerVersion() { Tape.Add("version"); return 0; }
            public int ReadPlayerControl(int slot) { Tape.Add("control"); ControlReads++; return Controls[slot]; }
            public int ReadBallCarrier() { Tape.Add("carrier"); return Controls[0]; }
            public int ReadPlayerTeam(int player) { Tape.Add("team"); return 0; }
            public int ReadOffensiveSide() { Tape.Add("offense"); return 0; }
            public int ReadIsLowResolution() { Tape.Add("low"); return 0; }
            public int ReadTsec() { Tape.Add("tsec"); return Tsec; }
            public int ReadCurrentButtons(int player) { Tape.Add("current-read"); return Current[player]; }
            public int ReadOldButtons(int player) { Tape.Add("old-read"); return Old[player]; }
            public void WriteCurrentButtons(int player, int value)
            { Tape.Add("current-write"); ButtonWrites++; Current[player] = value; }
            public void WriteOldButtons(int player, int value)
            { Tape.Add("old-write"); ButtonWrites++; Old[player] = value; }
            public void SetTextZIncrement(float value) { Tape.Add("text-z"); }
            public ISourceInstructionPlate BeginPlate(string image, string texture, SourceInstructionPoint point)
            { Tape.Add("plate"); return new Plate(); }
            public void SetPlateZAngle(ISourceInstructionPlate plate, float value) { Tape.Add("plate-angle"); }
            public void SetPlateHScale(ISourceInstructionPlate plate, float value) { Tape.Add("plate-scale"); }
            public void SetPlateObjectId(ISourceInstructionPlate plate, string symbol, int slot) { Tape.Add("plate-id"); }
            public void GeneratePlateVertices(ISourceInstructionPlate plate) { Tape.Add("plate-vertices"); }
            public void SetTextFont(SourceKickoffInstructionChildFont font) { Tape.Add("font"); }
            public void SetStringId(int id) { Tape.Add("string-id"); }
            public void SetTextColor(SourceKickoffInstructionChildColor color) { Tape.Add("color"); }
            public void SetTextPosition(SourceInstructionPoint point) { Tape.Add("position"); }
            public void Print(SourceKickoffInstructionChildTextRequest request) { Tape.Add("print"); }
            public void PlaySound(SourceKickoffInstructionChildSound sound)
            {
                Equal(Child, Host.Caller, "suffix sound stays on the actual child");
                Check(ReferenceEquals(SourceKickoffInstructionChildSound.Ricochet, sound), "source suffix sound");
                Tape.Add("sound");
            }
            public void DeleteStrings(int id, uint mask)
            {
                Equal(0x34, id, "original child string ID"); Equal(uint.MaxValue, mask, "string full mask");
                Tape.Add("delete-strings");
            }
            public void DeletePlateClass(string symbol, int slot, uint mask)
            {
                Equal("OID_IDIOTBOX", symbol, "plate class symbol"); Equal(0, slot, "original plate slot");
                Equal(uint.MaxValue, mask, "plate full mask"); Tape.Add("delete-plate");
            }
            public SourceKickoffInstructionChildReturnAction ReturnFromSourceBody(
                SourceKickoffInstructionChildInvocation invocation, SourceKickoffInstructionChildReturnReason reason)
            {
                Equal(Child, Host.Caller, "child return policy uses actual host caller");
                Equal(Child, invocation.Serial, "child return identity");
                Equal(SourceKickoffInstructionChildReturnReason.NormalBodyEnd, reason, "normal child source end");
                Tape.Add("child-return"); ChildReturns++; return ChildAction;
            }
            public SourceKickoffInstructionFlashReturnAction ReturnFromSourceBody(
                SourceKickoffInstructionFlashInvocation invocation)
            {
                Equal(Flash, Host.Caller, "flash return policy uses actual host caller");
                Equal(Flash, invocation.Serial, "flash return identity");
                Tape.Add("flash-return"); FlashReturns++;
                if (ThrowFlashReturn) throw new PrimitiveFailure();
                return FlashAction;
            }
            public ISourceKickoffActivePlayerWait Create(int minimum, int maximum, int slot)
            {
                LegacyWaitCreates++; Equal(40, minimum, "legacy minimum"); Equal(6 * Tsec, maximum, "legacy timeout");
                Equal(0, slot, "legacy original slot"); return new LegacyWait(this);
            }
            public ISourceKickoffInstructionChildContinuation Create(SourceKickoffInstructionFlashInvocation invocation)
            {
                Equal(Flash, Host.Caller, "legacy flash factory remains deferred");
                LegacyFlashCreates++; return new LegacyFlash(this);
            }
            private static TestFailure FrozenPage()
            { return new TestFailure("owned helpers must not substitute the frozen page/edge-input path"); }
            public int ReadPlayerSwitches() { throw FrozenPage(); }
            public int ReadShowPlyselIdiotBox() { throw FrozenPage(); }
            public ISourceInstructionPlate BeginPlate(string image, string texture, SourceInstructionPosition point)
            { throw FrozenPage(); }
            public void SetPlateWidthScale(ISourceInstructionPlate plate, float value) { throw FrozenPage(); }
            public void SetPlateHeightScale(ISourceInstructionPlate plate, float value) { throw FrozenPage(); }
            public void SetPlateId(ISourceInstructionPlate plate, string id) { throw FrozenPage(); }
            public void SetTextFont(SourceInstructionFont font) { throw FrozenPage(); }
            public void SetTextPosition(SourceInstructionPosition point) { throw FrozenPage(); }
            public void SetTextColor(SourceInstructionColor color) { throw FrozenPage(); }
            public ISourceInstructionText Print(SourceInstructionTextRequest request) { throw FrozenPage(); }
            public void StoreMessage(int index, ISourceInstructionText text) { throw FrozenPage(); }
            public ISourceInstructionFlasher CreateTextFlasher(string name, int id,
                ISourceInstructionText first, ISourceInstructionText second, int third, int fourth)
            { throw FrozenPage(); }
            public void CreatePlateFlasher(string name, int id, ISourceInstructionPlate plate, int flashes, int delay, int fourth)
            { throw FrozenPage(); }
            public void PlaySound(SourceInstructionSound sound) { throw FrozenPage(); }
            public void DeleteStrings(int id, int mask) { throw FrozenPage(); }
            public void KillTextFlasher(ISourceInstructionFlasher flasher, int signal) { throw FrozenPage(); }
            public void DeletePlate(ISourceInstructionPlate plate) { throw FrozenPage(); }
        }

        private sealed class LegacyWait : ISourceKickoffActivePlayerWait
        {
            private readonly Fixture fixture;
            private int advances;
            internal LegacyWait(Fixture fixture) { this.fixture = fixture; }
            public SourceKickoffActivePlayerWaitStep Advance()
            {
                Equal(fixture.Child, fixture.Host.Caller, "legacy wait uses actual child wake");
                fixture.LegacyWaitAdvances++;
                if (advances++ == 0) return SourceKickoffActivePlayerWaitStep.Sleep(2);
                Equal(2, advances, "legacy wait not replayed"); return SourceKickoffActivePlayerWaitStep.Returned();
            }
        }
        private sealed class LegacyFlash : ISourceKickoffInstructionChildContinuation
        {
            private readonly Fixture fixture;
            internal LegacyFlash(Fixture fixture) { this.fixture = fixture; }
            public SourceKickoffInstructionChildStep Advance()
            {
                fixture.LegacyFlashAdvances++; Equal(1, fixture.LegacyFlashAdvances, "legacy flash not replayed");
                return SourceKickoffInstructionChildStep.ExternalRetirement();
            }
        }
        private sealed class WaitDriver
        {
            private readonly Fixture fixture;
            internal readonly ISourceKickoffActivePlayerWait Wait;
            internal WaitDriver(Fixture fixture, int minimum, int maximum, int slot)
            {
                this.fixture = fixture;
                Wait = new SourceKickoffOwnedActivePlayerWaitFactory(fixture, fixture.Helpers).Create(minimum, maximum, slot);
            }
            internal SourceKickoffActivePlayerWaitStep Tick()
            {
                fixture.Host.BeginTick(fixture.Host.SourceTick + 1);
                Check(fixture.Host.Visit(2), "real caller resumes the wait after its actual host sleep");
                try
                {
                    SourceKickoffActivePlayerWaitStep step = Wait.Advance();
                    if (step.Kind == SourceKickoffActivePlayerWaitStepKind.Sleep)
                    {
                        Equal(1, step.SleepTicks, "owned active wait always sleeps one");
                        fixture.Host.Sleep(step.SleepTicks);
                    }
                    else Equal(SourceKickoffActivePlayerWaitStepKind.Returned, step.Kind, "ordinary helper return");
                    return step;
                }
                finally { fixture.Host.Complete(); }
            }
        }

        private static void WaitFirstSleepZeroMaximum()
        {
            var f = new Fixture(); var wait = new WaitDriver(f, 0, 0, 0);
            Equal(0, f.Tape.Count, "construction reads no providers");
            Equal(SourceKickoffActivePlayerWaitStepKind.Sleep, wait.Tick().Kind, "zero maximum still sleeps first");
            Equal(0, f.StatusReads, "no entry status sample"); Equal(0, f.SwitchReads, "no entry switch sample");
            Equal(SourceKickoffActivePlayerWaitStepKind.Returned, wait.Tick().Kind, "zero maximum ends after first polling");
            Equal(1, f.StatusReads, "zero maximum samples final status"); Equal(1, f.SwitchReads, "zero maximum samples current word");
            Equal("status", f.Tape[0], "status precedes switches"); Equal("switch", f.Tape[1], "current sample follows status");
            Check(f.Host.Contains(2), "helper return is not process death");
            Equal(0, f.FlagWrites + f.ButtonWrites + f.ChildReturns + f.FlashReturns, "helper has no external state/return effects");
            Throws<InvalidOperationException>(() => wait.Wait.Advance(), "returned wait cannot replay");
            Equal(1, f.SwitchReads, "returned wait never repolls");
        }
        private static void WaitHeldCurrentMinimumLast()
        {
            foreach (uint mask in new[] { A, B, C })
            {
                var f = new Fixture { Raw = mask }; var wait = new WaitDriver(f, 3, 10, 0);
                Equal(SourceKickoffActivePlayerWaitStepKind.Sleep, wait.Tick().Kind, "held input does not bypass first sleep");
                Equal(SourceKickoffActivePlayerWaitStepKind.Sleep, wait.Tick().Kind, "held input before minimum tick one");
                Equal(1, f.SwitchReads, "polling is not suppressed by the minimum");
                Equal(SourceKickoffActivePlayerWaitStepKind.Sleep, wait.Tick().Kind, "held input before minimum tick two");
                Equal(SourceKickoffActivePlayerWaitStepKind.Returned, wait.Tick().Kind, "unchanged held current input ends at minimum");
                Equal(3, f.SwitchReads, "every pre-minimum and minimum tick polls");
                Equal(0, f.Calls("current-read") + f.Calls("old-read"), "no edge/current button-array substitution");
            }
        }
        private static void WaitMinimumAfterTimeoutFinalSample()
        {
            var f = new Fixture(); var wait = new WaitDriver(f, 5, 2, 0);
            wait.Tick();
            Equal(SourceKickoffActivePlayerWaitStepKind.Sleep, wait.Tick().Kind, "first timeout iteration continues");
            f.Raw = C;
            Equal(SourceKickoffActivePlayerWaitStepKind.Returned, wait.Tick().Kind, "timeout may precede minimum");
            Equal(2, f.StatusReads, "final timeout tick still samples participation");
            Equal(2, f.SwitchReads, "final timeout tick still samples current input");
            Equal("switch", f.Tape[f.Tape.Count - 1], "timeout test follows final polling");
            Equal(0, f.Calls("sound"), "wait itself has no sound or suffix");
        }
        private static void WaitLiveStatusOriginalPlayerByte()
        {
            var f = new Fixture { Status = 4, Raw = A }; var wait = new WaitDriver(f, 0, 4, 2);
            wait.Tick();
            Equal(SourceKickoffActivePlayerWaitStepKind.Sleep, wait.Tick().Kind, "other player's byte does not finish");
            f.Status = 2; f.Raw = A << 16;
            Equal(SourceKickoffActivePlayerWaitStepKind.Sleep, wait.Tick().Kind, "newly inactive original player cannot finish");
            Equal(1, f.SwitchReads, "inactive participation suppresses the raw switch call");
            f.Status = 4;
            Equal(SourceKickoffActivePlayerWaitStepKind.Returned, wait.Tick().Kind, "live original status and byte finish");
            Equal(3, f.StatusReads, "participation is reread after every sleep");
            Equal(2, f.SwitchReads, "only active iterations poll"); Equal(0, f.ControlReads, "wait never maps through player control");
            var high = new Fixture { Status = 8, Raw = A << 24 }; var fourth = new WaitDriver(high, 1, 4, 3);
            fourth.Tick();
            Equal(SourceKickoffActivePlayerWaitStepKind.Returned, fourth.Tick().Kind, "fourth original byte retains its high bit");
            Equal(1, high.SwitchReads, "fourth byte uses one genuine raw sample");
        }
        private static void FlashDeferredFirstFullCycleDelay8()
        {
            var f = new Fixture(); f.StartChild();
            var invocation = f.Dispatcher.KickoffFlashInvocation(f.Flash);
            Equal(0, invocation.ProcessId, "flash creation PID remains zero");
            Equal(0, invocation.Argument0, "flash captures original slot"); Equal(8, invocation.Argument1, "source child delay");
            Equal(22, invocation.Argument2, "existence operand is not flash PID or guessed child PID");
            f.Dispatch(f.Flash);
            Equal(0, f.Captures.Count, "birth-tick dispatch cannot capture or execute the new flash");
            f.Tick(f.Flash);
            Equal(1, f.Blocks[0].Writes, "first actual wake enters white unconditionally");
            Equal(8, f.Host.Require(f.Flash).SleepRemaining, "first white yields eight host visits");
            for (int tick = 1; tick < 8; tick++)
            {
                f.Tick(f.Flash); Equal(8 - tick, f.Host.Require(f.Flash).SleepRemaining, "white delay is not shortened");
                Equal(1, f.Blocks[0].Writes, "no premature clear");
            }
            f.Tick(f.Flash);
            Equal(2, f.Blocks[0].Writes, "clear occurs only after first full delay");
            Equal(8, f.Host.Require(f.Flash).SleepRemaining, "clear yields the same retained delay");
            Equal(0, f.Records("ExistsCall", f.Flash), "no existence test before the full first cycle");
            for (int tick = 1; tick < 8; tick++) f.Tick(f.Flash);
            Equal(0, f.FlashReturns, "no return before second full sleep");
            f.Tick(f.Flash);
            Equal(1, f.Records("ExistsCall", f.Flash), "one existence query after both sleeps");
            Check(f.HasRecord("ExistsCall", f.Flash, 22, -1), "actual host query uses ID22/full32 mask");
            Equal(1, f.FlashReturns, "missing ID22 ends the source body only after a full cycle");
        }
        private static void FlashCapturedObjectLiveFlags()
        {
            var f = new Fixture(); PlayerObject original = f.Blocks[0]; original.Flags = White | 1;
            f.StartChild(); f.Tick(f.Flash);
            Equal(White | 1, original.Written[0], "first compound OR preserves original other bits");
            var replacement = new PlayerObject(f, White | 4); f.Blocks[0] = replacement;
            original.Flags = White | 8;
            f.EightFlashTicks();
            Equal(8U, original.Flags, "clear rereads live flags on the captured object");
            Equal(White | 4, replacement.Flags, "replacing player_blocks cannot redirect the captured object");
            original.Flags = White | 16;
            f.EightFlashTicks();
            Equal(White | 16, original.Flags, "source end performs no extra clear or old-word restoration");
            Equal(2, original.Reads, "one live read for each compound update"); Equal(2, original.Writes, "no cleanup write");
            Equal(0, replacement.Reads + replacement.Writes, "replacement object is never touched");
            Equal(8U, f.Blocks[3].Flags, "control-index object's flags stay untouched");
            Equal(1, f.Captures.Count, "capture occurs once across both sleeps"); Equal(0, f.Captures[0], "capture uses argument zero, not control three");
        }
        private static void FlashAnyId22FullMaskSuspended()
        {
            var f = new Fixture(); f.StartChild(); f.Tick(f.Flash); f.EightFlashTicks();
            long first = f.Foreign(22, 0x8005), second = f.Foreign(22, 1), near = f.Foreign(0x10016, 1);
            Check(f.Host.Require(first).Suspended, "first matching foreign process really is suspended");
            f.EightFlashTicks();
            Equal(3, f.Blocks[0].Writes, "any suspended/live foreign ID22 repeats white");
            Equal(0, f.FlashReturns, "foreign matching PID prevents source end");
            Check(f.HasRecord("ExistsCall", f.Flash, 22, -1), "matching uses actual host full-mask query");
            f.Host.Kill(first); f.EightFlashTicks(); f.EightFlashTicks();
            Equal(5, f.Blocks[0].Writes, "remaining matching PID still repeats after one removal");
            Equal(0, f.FlashReturns, "existence is ANY match, not a recorded first handle");
            f.Host.Kill(second); f.EightFlashTicks(); f.EightFlashTicks();
            Equal(1, f.FlashReturns, "only absence of every exact ID22 ends the loop");
            Check(f.Host.Contains(near), "different upper PID bits must not match the full mask");
            Check(f.Host.Contains(f.Flash) && f.Host.Contains(f.Child), "PID0 and configured child PID do not replace existence ID22");
            Equal(1, f.Captures.Count, "repeat cycles retain original object identity");
        }
        private static void FlashExplicitReturnPolicy()
        {
            foreach (SourceKickoffInstructionFlashReturnAction action in new[] {
                SourceKickoffInstructionFlashReturnAction.KeepLive, SourceKickoffInstructionFlashReturnAction.ExternallyRetire })
            {
                var f = new Fixture { FlashAction = action };
                f.StartChild(); f.Tick(f.Flash); f.EightFlashTicks(); f.EightFlashTicks();
                Equal(1, f.FlashReturns, "explicit policy called exactly once on source body end");
                Equal(action == SourceKickoffInstructionFlashReturnAction.KeepLive, f.Host.Contains(f.Flash), "only policy decides retirement");
                Equal(1, f.Records("SourceFlashFallthrough", f.Flash), "owned source end is recorded distinctly");
                Equal(0, f.Records("ExternalFlashRetirement", f.Flash), "normal source end is not a legacy retirement step");
                Equal(action == SourceKickoffInstructionFlashReturnAction.ExternallyRetire ? 1 : 0,
                    f.Records("Die", f.Flash), "no implicit native die");
                Equal(0, f.ChildReturns, "flash return policy does not return the child wait");
                f.Tick(f.Flash); Equal(1, f.FlashReturns, "returned source body cannot replay even when kept live");
            }
        }
        private static void ChildOwnedWaitSuffixAndFlash()
        {
            var f = new Fixture { Raw = A, ChildAction = SourceKickoffInstructionChildReturnAction.ExternallyRetire };
            f.StartChild();
            Equal(0, f.SwitchReads, "child reaches owned wait's first sleep without polling");
            Equal(0, f.Captures.Count, "child queues rather than inlines owned flash");
            for (int tick = 1; tick < 40; tick++) f.Tick(f.Child, f.Flash);
            Equal(39, f.SwitchReads, "held current input sampled before minimum on actual child wakes");
            Equal(0, f.Calls("sound") + f.ChildReturns, "no child suffix or retirement before helper return");
            f.Tick(f.Child, f.Flash);
            Equal(40, f.SwitchReads, "source minimum40 counts only completed Sleep1 returns");
            Equal(40, f.Records("Sleep", f.Child), "exactly forty real child sleep operands");
            Equal(1, f.Calls("sound"), "helper returns into actual child sound suffix");
            Equal(1, f.Calls("delete-strings"), "actual child string cleanup");
            Equal(1, f.Calls("delete-plate"), "actual child plate cleanup");
            Check(f.Tape.IndexOf("sound") < f.Tape.IndexOf("delete-strings")
                && f.Tape.IndexOf("delete-strings") < f.Tape.IndexOf("delete-plate")
                && f.Tape.IndexOf("delete-plate") < f.Tape.IndexOf("child-return"), "source suffix order before return policy");
            Equal(1, f.ChildReturns, "child process policy is distinct from ordinary helper return");
            Check(!f.Host.Contains(f.Child), "child explicitly retires only after suffix");
            Equal(1, f.FlashReturns, "same-host owned flash also reaches its explicit return policy");
            Equal(2, f.Blocks[0].Writes, "owned flash executed its real white/clear cycle");
            Equal(0, f.LegacyWaitCreates + f.LegacyFlashCreates, "new path does not substitute external whole-helper bodies");
            Equal(0, f.ButtonWrites, "neither helper writes parent button state");
            Equal(2, f.FlagWrites, "only admission initialization and parent entry write the shared shown flag");
            Equal(1, f.Shown, "one coherent admission flag remains unchanged by helpers");
            Equal(0, f.Games[0], "helpers do not mutate experience");
            f.Tick(f.Parent);
            Equal(Fixture.ParentA | Fixture.ParentB, f.Current[3], "existing parent suffix resumes with original control binding");
            Equal(Fixture.ParentA | Fixture.ParentB, f.Old[3], "existing parent old-button suffix remains usable");
            Equal(2, f.ButtonWrites, "only actual parent suffix writes buttons");
        }
        private static void LegacyBoundPathCompatibility()
        {
            var f = new Fixture(true); f.StartChild();
            Check(f.ChildBinding.OwnedHelpers == null && f.ChildBinding.Waits != null && f.ChildBinding.Flashes != null,
                "old source-child binding still requires and retains its whole-body factories");
            Equal(1, f.LegacyWaitCreates, "old bound wait factory remains active");
            Equal(2, f.Host.Require(f.Child).SleepRemaining, "legacy sleep operand is not replaced by owned Sleep1");
            f.Tick(f.Flash);
            Equal(1, f.LegacyFlashCreates, "old flash factory remains active");
            Check(!f.Host.Contains(f.Flash), "old external retirement step remains usable");
            f.Tick(f.Child); f.Tick(f.Child);
            Equal(2, f.LegacyWaitAdvances, "old wait continuation resumes");
            Equal(1, f.Calls("sound"), "old helper still returns to the same child suffix");
            Equal(1, f.ChildReturns, "old bound child return policy remains active");
            Equal(0, f.SwitchReads + f.Captures.Count, "legacy path never activates owned primitive providers");
            Equal(0, f.Blocks[0].Writes, "legacy external flash has no new flag writes");
        }
        private static void MandatoryBindingsAndPortableDomains()
        {
            var f = new Fixture();
            Throws<ArgumentOutOfRangeException>(() => new SourceKickoffInstructionOwnedHelpers(0, B, C, White, f, f, f), "mandatory P1_A");
            Throws<ArgumentOutOfRangeException>(() => new SourceKickoffInstructionOwnedHelpers(A, 0, C, White, f, f, f), "mandatory P1_B");
            Throws<ArgumentOutOfRangeException>(() => new SourceKickoffInstructionOwnedHelpers(A, B, 0, White, f, f, f), "mandatory P1_C");
            Throws<ArgumentOutOfRangeException>(() => new SourceKickoffInstructionOwnedHelpers(A, B, C, 0, f, f, f), "mandatory flash mask");
            Throws<ArgumentNullException>(() => new SourceKickoffInstructionOwnedHelpers(A, B, C, White, null, f, f), "mandatory raw current switches");
            Throws<ArgumentNullException>(() => new SourceKickoffInstructionOwnedHelpers(A, B, C, White, f, null, f), "mandatory genuine player-object binding");
            Throws<ArgumentNullException>(() => new SourceKickoffInstructionOwnedHelpers(A, B, C, White, f, f, null), "mandatory explicit flash return policy");
            Throws<ArgumentNullException>(() => SourceKickoffInstructionChildBinding.WithOwnedHelpers(f.Operands, f, null, f), "no whole-helper fallback");
            Throws<ArgumentNullException>(() => SourceKickoffInstructionChildBinding.WithOwnedHelpers(f.Operands, f, f.Helpers, null), "mandatory child return policy");
            var factory = new SourceKickoffOwnedActivePlayerWaitFactory(f, f.Helpers);
            Throws<ArgumentOutOfRangeException>(() => factory.Create(-1, 0, 0), "negative portable minimum is not clamped");
            Throws<ArgumentOutOfRangeException>(() => factory.Create(0, -1, 0), "negative portable timeout is not clamped");
            Throws<ArgumentOutOfRangeException>(() => factory.Create(0, 0, -1), "negative portable player is not normalized");
            Throws<ArgumentOutOfRangeException>(() => factory.Create(0, 0, 4), "portable word cannot wrap a fifth player's shift");
            Check(f.ChildBinding.Waits == null && f.ChildBinding.Flashes == null, "new opt-in needs no external whole-helper callbacks");
            Check(!f.Dispatcher.Dispatch(2, function => f.Tape.Add("unexpected")), "unbound dispatcher does not auto-activate source children");
            Equal(0, f.Tape.Count, "configuration, guards and factories perform no provider reads");
            f.Initialize();
            Equal(1, f.StatusReads, "only existing admission initialization reads participation");
            Equal(1, f.FlagWrites, "only existing admission initialization writes shown flag");
            Equal(0, f.SwitchReads + f.Captures.Count, "binding owned factories does not execute helpers");
        }
        private static void FailedContinuationsPoisonAndDoNotReplay()
        {
            var direct = new Fixture { ThrowSwitch = true }; var wait = new WaitDriver(direct, 0, 0, 0);
            wait.Tick();
            Throws<PrimitiveFailure>(() => wait.Tick(), "even zero-timeout final poll surfaces its primitive failure");
            Throws<InvalidOperationException>(() => wait.Wait.Advance(), "failed owned wait itself rejects replay");
            Equal(1, direct.SwitchReads, "failed wait never repeats a primitive");

            var child = new Fixture { Tsec = 0, ThrowSwitch = true }; child.StartChild();
            Throws<PrimitiveFailure>(() => child.Tick(child.Child), "child surfaces owned final-tick wait failure");
            int childCalls = child.Tape.Count;
            Throws<InvalidOperationException>(() => child.Dispatch(child.Child), "failed child cannot replay");
            Equal(childCalls, child.Tape.Count, "failed child performs no repeated provider calls");
            Throws<InvalidOperationException>(() => child.Dispatcher.Admit(child.Serial + 1, child.Invocation), "wait failure poisons dispatcher admission");
            Equal(0, child.Calls("sound") + child.Calls("delete-strings") + child.ChildReturns, "no invented suffix on wait failure");
            Check(child.Host.Contains(child.Child), "wait failure does not silently kill child");

            var flash = new Fixture(); flash.StartChild(); flash.Tick(flash.Flash);
            PlayerObject original = flash.Blocks[0]; original.Flags |= 8; original.FailWriteAt = 2;
            for (int tick = 1; tick < 8; tick++) flash.Tick(flash.Flash);
            Throws<PrimitiveFailure>(() => flash.Tick(flash.Flash), "clear write failure is surfaced");
            Equal(White | 9, original.Flags, "failure performs no cleanup or whole-word restore");
            Equal(1, original.Written.Count, "only first compound update succeeded");
            Throws<InvalidOperationException>(() => flash.Dispatch(flash.Flash), "failed flash cannot replay");
            Equal(2, original.Writes, "failed clear is never retried");
            Equal(0, flash.Records("ExistsCall", flash.Flash) + flash.FlashReturns, "failed first cycle cannot query/return early");
            Throws<InvalidOperationException>(() => flash.Dispatcher.Admit(flash.Serial + 1, flash.Invocation), "flash failure poisons actual dispatcher");
            Check(flash.Host.Contains(flash.Flash), "flash fault has no implicit retirement");

            var invalid = new Fixture { FlashAction = (SourceKickoffInstructionFlashReturnAction)0 };
            invalid.StartChild(); invalid.Tick(invalid.Flash); invalid.EightFlashTicks();
            for (int tick = 1; tick < 8; tick++) invalid.Tick(invalid.Flash);
            Throws<InvalidOperationException>(() => invalid.Tick(invalid.Flash), "unsupported return action is not guessed");
            Throws<InvalidOperationException>(() => invalid.Dispatch(invalid.Flash), "failed return policy cannot replay source end");
            Equal(1, invalid.FlashReturns, "invalid return policy called once");
            Check(invalid.Host.Contains(invalid.Flash), "unsupported policy does not infer die");

            var policy = new Fixture { ThrowFlashReturn = true };
            policy.StartChild(); policy.Tick(policy.Flash); policy.EightFlashTicks();
            for (int tick = 1; tick < 8; tick++) policy.Tick(policy.Flash);
            Throws<PrimitiveFailure>(() => policy.Tick(policy.Flash), "return policy error is not swallowed");
            Throws<InvalidOperationException>(() => policy.Dispatch(policy.Flash), "throwing return policy is not replayed");
            Equal(1, policy.FlashReturns, "throwing policy called once");

            var missing = new Fixture(); missing.Blocks[0] = null; missing.StartChild();
            Throws<InvalidOperationException>(() => missing.Tick(missing.Flash), "missing genuine object has no substitute");
            Throws<InvalidOperationException>(() => missing.Dispatch(missing.Flash), "missing-object flash cannot retry");
            Equal(1, missing.Captures.Count, "failed capture attempted once");
        }

        private static int Main()
        {
            string[] names = {
                "wait-first-sleep-zero-maximum", "wait-held-current-minimum-last",
                "wait-minimum-after-timeout-final-sample", "wait-live-status-original-player-byte",
                "flash-deferred-first-full-cycle-delay8", "flash-captured-object-live-flags",
                "flash-any-id22-full-mask-suspended", "flash-explicit-return-policy",
                "child-owned-wait-suffix-and-flash", "legacy-bound-path-compatibility",
                "mandatory-bindings-and-portable-domains", "failed-continuations-poison-and-do-not-replay"
            };
            Action[] cases = {
                WaitFirstSleepZeroMaximum, WaitHeldCurrentMinimumLast, WaitMinimumAfterTimeoutFinalSample,
                WaitLiveStatusOriginalPlayerByte, FlashDeferredFirstFullCycleDelay8, FlashCapturedObjectLiveFlags,
                FlashAnyId22FullMaskSuspended, FlashExplicitReturnPolicy, ChildOwnedWaitSuffixAndFlash,
                LegacyBoundPathCompatibility, MandatoryBindingsAndPortableDomains, FailedContinuationsPoisonAndDoNotReplay
            };
            for (int index = 0; index < names.Length; index++)
            {
                int before = assertions;
                try
                {
                    cases[index]();
                    if (assertions == before) throw new TestFailure("case made no assertions");
                }
                catch (Exception error)
                { Console.WriteLine("FAIL " + names[index] + ": " + error.Message); return 1; }
                Console.WriteLine("PASS " + names[index] + " assertions=" + (assertions - before));
            }
            Console.WriteLine("SUMMARY component_cases=12 assertions=" + assertions + " failures=0 Game=0 Unity=0 native=0");
            return 0;
        }
    }
}
