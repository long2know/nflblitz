using System;
using System.Collections.Generic;
using Blitz.Contracts;
using Blitz.Gameplay;

namespace Blitz.SourceInstructionalBoxesComponent
{
    internal static class Program
    {
        private sealed class TestFailure : Exception
        { internal TestFailure(string message) : base(message) { } }
        private static void Check(bool condition, string name)
        { if (!condition) throw new TestFailure(name); }
        private static void Equal<T>(T expected, T actual, string name)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
                throw new TestFailure(name + ": expected " + expected + ", got " + actual);
        }
        private static void ArgumentRejected(Action call, string name)
        {
            bool rejected = false;
            try { call(); }
            catch (ArgumentException) { rejected = true; }
            Check(rejected, name);
        }
        private static void StateRejected(Action call, string name)
        {
            bool rejected = false;
            try { call(); }
            catch (InvalidOperationException) { rejected = true; }
            Check(rejected, name);
        }
        private static void Before(List<string> trace, string first, string second)
        {
            int a = trace.IndexOf(first), b = trace.IndexOf(second);
            Check(a >= 0 && b > a, "source order: " + first + " before " + second);
        }

        private sealed class Inputs : ISourceInstructionInputs
        {
            internal readonly Queue<int> Samples = new Queue<int>();
            internal readonly Queue<int> Shows = new Queue<int>();
            internal readonly Queue<int> Tsecs = new Queue<int>();
            internal readonly Queue<int> LowRes = new Queue<int>();
            internal int SwitchReads, ShowReads, TsecReads, LowReads;
            internal Action<string> Log;
            private int Take(Queue<int> queue, string kind)
            {
                Check(queue.Count > 0, "unplanned external input read: " + kind);
                int value = queue.Dequeue();
                if (Log != null) Log("read:" + kind + ":" + value);
                return value;
            }
            public int ReadPlayerSwitches() { SwitchReads++; return Take(Samples, "switch"); }
            public int ReadShowPlyselIdiotBox() { ShowReads++; return Take(Shows, "show"); }
            public int ReadTsec() { TsecReads++; return Take(Tsecs, "tsec"); }
            public int ReadIsLowResolution() { LowReads++; return Take(LowRes, "low"); }
        }

        private static void InitialDelay()
        {
            var log = new List<string>();
            var input = new Inputs { Log = log.Add };
            input.Samples.Enqueue(0x104); input.Shows.Enqueue(0);
            var wait = new SourceAnyButtonWait(2, 1, 4);
            SourceAnyButtonWaitSnapshot initial = wait.Snapshot;
            StateRejected(() => { int ignored = wait.Result; }, "no premature wait result");
            for (int i = 0; i < 2; i++)
            {
                Check(!wait.Advance(input, () => log.Add("tick"), n => log.Add("sleep:" + n)), "delay yields");
                Equal(0, input.SwitchReads, "no polling in initial delay");
                Equal(0, input.ShowReads, "no gate read in initial delay");
            }
            Check(wait.Advance(input, () => log.Add("tick"), n => log.Add("sleep:" + n)), "held first sample accepted");
            Equal(0x104, wait.Result, "entire accepted sample");
            Equal("sleep:1|sleep:1|read:switch:260|read:show:0|tick", string.Join("|", log), "delay/accept call trace");
            Equal(2, initial.DelayRemaining, "old wait snapshot immutable");
            Check(!initial.Completed && !initial.Result.HasValue, "old snapshot not changed on completion");
            StateRejected(() => wait.Advance(input, () => { }, n => { }), "returned wait cannot be replayed");
        }

        private static void TimeoutBoundaries()
        {
            foreach (int count in new[] { 0, 1, 3 })
            {
                var log = new List<string>();
                var input = new Inputs { Log = log.Add };
                for (int i = 0; i < count; i++) input.Samples.Enqueue(0x20);
                var wait = new SourceAnyButtonWait(0, count, 4);
                for (int i = 0; i < count; i++)
                {
                    Check(!wait.Advance(input, () => log.Add("tick"), n => log.Add("sleep:" + n)), "rejection always yields");
                    Check(!wait.Completed, "last rejection is not a returned sleep");
                    Equal((i + 1) * 2, log.Count, "one read then one sleep per rejected poll");
                    Equal("sleep:1", log[log.Count - 1], "including final rejected sleep");
                }
                Check(wait.Advance(input, () => log.Add("tick"), n => log.Add("sleep:" + n)), "timeout returns on resume");
                Equal(0, wait.Result, "timeout result zero");
                Equal(count, input.SwitchReads, "exact timeout polls");
                Equal(0, input.ShowReads, "timeout does not read sound gate");
                Equal(count * 2, log.Count, "no extra timeout calls");
            }
            var delayOnlyInput = new Inputs();
            var delayOnly = new SourceAnyButtonWait(1, 0, 4);
            int sleeps = 0;
            Check(!delayOnly.Advance(delayOnlyInput, () => { throw new TestFailure("unexpected tick"); }, n => sleeps += n),
                "zero-poll wait still performs initial delay");
            Check(delayOnly.Advance(delayOnlyInput, () => { throw new TestFailure("unexpected tick"); }, n => sleeps += n),
                "zero-poll wait returns after delay sleep");
            Equal(1, sleeps, "delay-only raw sleep operand");
            Equal(0, delayOnlyInput.SwitchReads, "delay-only no poll");
        }

        private static void OpaqueAcceptance()
        {
            int[] samples = { 0x104, unchecked((int)0x80000104), int.MinValue };
            int[] masks = { 4, 4, int.MinValue };
            int[] gates = { 0, 1, -2 };
            for (int i = 0; i < samples.Length; i++)
            {
                var log = new List<string>();
                var input = new Inputs { Log = log.Add };
                input.Samples.Enqueue(samples[i]); input.Shows.Enqueue(gates[i]);
                var wait = new SourceAnyButtonWait(0, 3, masks[i]);
                Check(wait.Advance(input, () => log.Add("tick"), n => log.Add("sleep:" + n)), "accepted poll returns immediately");
                Equal(samples[i], wait.Result, "opaque signed and unmasked acceptance");
                Equal(1, input.SwitchReads, "no release/repress or extra sample");
                Equal(1, input.ShowReads, "gate read only after acceptance");
                Equal(i == 0 ? 3 : 2, log.Count, "conditional sound and no post-accept sleep");
                Equal("read:switch:" + samples[i], log[0], "sample precedes gate");
                Equal("read:show:" + gates[i], log[1], "gate precedes optional sound");
                if (i == 0) Equal("tick", log[2], "zero gate calls tick");
            }
            var rejectedThenAccepted = new Inputs();
            rejectedThenAccepted.Samples.Enqueue(0x20); rejectedThenAccepted.Samples.Enqueue(0x204);
            rejectedThenAccepted.Shows.Enqueue(0);
            var multiple = new SourceAnyButtonWait(0, 2, 4);
            var calls = new List<string>();
            Check(!multiple.Advance(rejectedThenAccepted, () => calls.Add("tick"), n => calls.Add("sleep")), "first sample rejects");
            Check(multiple.Advance(rejectedThenAccepted, () => calls.Add("tick"), n => calls.Add("sleep")), "second sample accepts");
            Equal(0x204, multiple.Result, "later accepted entire sample");
            Equal("sleep|tick", string.Join("|", calls), "rejection sleep then acceptance sound");
        }

        private static void InvalidWaitInputs()
        {
            ArgumentRejected(() => new SourceAnyButtonWait(-1, 0, 4), "negative min unsupported");
            ArgumentRejected(() => new SourceAnyButtonWait(0, -1, 4), "negative wait unsupported");
            ArgumentRejected(() => new SourceAnyButtonWait(0, 0, 0), "unknown mask not defaulted");
            var wait = new SourceAnyButtonWait(0, 0, 4);
            ArgumentRejected(() => wait.Advance(null, () => { }, n => { }), "required input callback");
            ArgumentRejected(() => wait.Advance(new Inputs(), null, n => { }), "required sound callback");
            ArgumentRejected(() => wait.Advance(new Inputs(), () => { }, null), "required sleep callback");
            Check(!wait.Completed, "invalid callbacks do not return success");
        }

        private sealed class Plate : ISourceInstructionPlate { }
        private sealed class Text : ISourceInstructionText
        {
            internal readonly SourceInstructionTextRequest Request;
            internal Text(SourceInstructionTextRequest request) { Request = request; }
        }
        private sealed class Flasher : ISourceInstructionFlasher
        {
            internal readonly SourceInstructionPage Page;
            internal Flasher(SourceInstructionPage page) { Page = page; }
        }
        private sealed class Draw
        {
            internal SourceInstructionTextRequest Text;
            internal SourceInstructionPosition Position;
            internal SourceInstructionFont Font;
            internal SourceInstructionColor Color;
        }
        private sealed class Effects : ISourceInstructionEffects
        {
            private readonly SourcePortableProcessHost host;
            private long capturedTick = -1;
            private int capturedRecords;
            private Plate plate;
            private Text title, detail;
            private Flasher flasher;
            private SourceInstructionPosition position;
            private SourceInstructionFont font;
            private SourceInstructionColor color;
            internal readonly List<string> Trace = new List<string>();
            internal readonly List<Draw> Draws = new List<Draw>();
            internal readonly List<SourceInstructionSound> Sounds = new List<SourceInstructionSound>();
            internal readonly List<SourceInstructionFont> Fonts = new List<SourceInstructionFont>();
            internal readonly List<long> FlasherTicks = new List<long>();
            internal readonly List<long> PageSoundTicks = new List<long>();
            internal int? Flag;
            internal bool ReturnNullPlate;
            internal SourceInstructionPosition PlatePosition;
            internal Effects(SourcePortableProcessHost host) { this.host = host; }
            internal void CaptureHost()
            {
                if (capturedTick != host.SourceTick) { capturedTick = host.SourceTick; capturedRecords = 0; }
                var records = host.Records;
                while (capturedRecords < records.Count)
                {
                    SourcePossessionProcessRecord record = records[capturedRecords++];
                    Trace.Add("host:" + record.Operation + ":" + record.Id + ":" + record.Value);
                }
            }
            internal void Add(string value) { CaptureHost(); Trace.Add(value); }
            public void WriteIdiotBoxesShown(int value) { Add("flag:" + value); Flag = value; }
            public ISourceInstructionPlate BeginPlate(string image, string texture, SourceInstructionPosition at)
            {
                Equal("playresult", image, "plate image identity"); Equal("SCR_PLATE_TID", texture, "plate texture identity");
                Add("plate:begin"); PlatePosition = at;
                plate = ReturnNullPlate ? null : new Plate();
                return plate;
            }
            private void RequirePlate(ISourceInstructionPlate value)
            { Check(plate != null && object.ReferenceEquals(plate, value), "actual live plate handle"); }
            public void SetPlateWidthScale(ISourceInstructionPlate value, float scale)
            { RequirePlate(value); Equal(308f / 212f, scale, "plate width operands"); Add("plate:width"); }
            public void SetPlateHeightScale(ISourceInstructionPlate value, float scale)
            { RequirePlate(value); Equal(190f / 79f, scale, "plate height operands"); Add("plate:height"); }
            public void SetPlateId(ISourceInstructionPlate value, string id)
            { RequirePlate(value); Equal("OID_MESSAGE", id, "plate object id symbol"); Add("plate:id"); }
            public void GeneratePlateVertices(ISourceInstructionPlate value)
            { RequirePlate(value); Add("plate:vertices"); }
            public void SetTextFont(SourceInstructionFont value) { font = value; Fonts.Add(value); Add("font:" + value); }
            public void SetStringId(int value) { Add("group:" + value); }
            public void SetTextPosition(SourceInstructionPosition value)
            { position = value; Add("position:" + value.HorizontalOffset + ":" + value.VerticalOffset); }
            public void SetTextColor(SourceInstructionColor value) { color = value; Add("color:" + value); }
            public ISourceInstructionText Print(SourceInstructionTextRequest request)
            {
                Add("print:" + request.Page + ":" + request.Line);
                Draws.Add(new Draw { Text = request, Position = position, Font = font, Color = color });
                var text = new Text(request);
                if (request.Line == 0) title = text;
                if (request.Page == SourceInstructionPage.Rules && request.Line == 1) detail = text;
                return text;
            }
            public void StoreMessage(int index, ISourceInstructionText text)
            {
                Check(object.ReferenceEquals(index == 0 ? title : detail, text), "pmsg stores actual print return");
                Check(index == 0 || index == 1, "only native pmsg slots");
                Add("store:" + index);
            }
            public ISourceInstructionFlasher CreateTextFlasher(string name, int id,
                ISourceInstructionText first, ISourceInstructionText second, int third, int fourth)
            {
                Check(object.ReferenceEquals(first, title), "flasher receives title return");
                Check(object.ReferenceEquals(second, title.Request.Page == SourceInstructionPage.Rules ? detail : null),
                    "only rules flasher receives second text return");
                Equal(0, id, "text flasher pid zero"); Equal(0, third, "text flasher arg3"); Equal(0, fourth, "text flasher arg4");
                Equal(title.Request.Page == SourceInstructionPage.Rules ? "ftp15"
                    : title.Request.Page == SourceInstructionPage.Hints ? "ftp17" : "ftp16", name, "native flasher name");
                Add("text-flash:" + title.Request.Page);
                FlasherTicks.Add(host.SourceTick);
                flasher = new Flasher(title.Request.Page);
                return flasher;
            }
            public void CreatePlateFlasher(string name, int id, ISourceInstructionPlate value, int flashes, int delay, int fourth)
            {
                RequirePlate(value); Equal("flash", name, "plate flash name"); Equal(Rig.MessagePid, id, "supplied message pid");
                Equal(2, flashes, "plate flash count"); Equal(3, delay, "plate flash delay"); Equal(0, fourth, "plate flash arg4");
                Add("plate:flash");
            }
            public void PlaySound(SourceInstructionSound value)
            {
                Sounds.Add(value); Add(value.Direct ? "sound:tick" : "sound:page");
                Equal(127, value.Pan, "sound pan");
                if (value.Direct)
                {
                    Equal("tick_sndn", value.SoundSymbol, "direct tick symbol"); Equal("VOLUME1", value.VolumeSymbol, "tick volume");
                    Equal<int?>(1, value.RawPriority, "tick raw priority"); Check(value.BankSymbol == null, "direct tick no bank");
                }
                else
                {
                    PageSoundTicks.Add(host.SourceTick);
                    Equal("plyr_bnk_str", value.BankSymbol, "page bank"); Equal("RICOCHET_SND", value.SoundSymbol, "page sound");
                    Equal("VOLUME2", value.VolumeSymbol, "page volume"); Equal("PRIORITY_LVL5", value.PrioritySymbol, "page priority");
                }
            }
            public void DeleteStrings(int id, int mask) { Equal(0x34, id, "string group"); Equal(-1, mask, "string mask"); Add("strings:delete"); }
            public void KillTextFlasher(ISourceInstructionFlasher value, int signal)
            {
                Check(object.ReferenceEquals(flasher, value), "kill actual page flasher return"); Equal(0, signal, "flasher kill signal");
                Add("text-kill:" + flasher.Page);
            }
            public void DeletePlate(ISourceInstructionPlate value) { RequirePlate(value); Add("plate:delete"); plate = null; }
        }

        private sealed class Rig
        {
            // Explicit synthetic component identifiers, not claimed native declarations.
            internal const int InstructionPid = 0x6a31, MessagePid = 0x6a32, PostPid = 0x0502, CameraPid = 0x1400;
            internal readonly SourcePortableProcessHost Host = new SourcePortableProcessHost();
            internal readonly SourceInstructionDispatcher Dispatcher;
            internal readonly List<int> WaitArguments = new List<int>();
            internal readonly List<long> WaitStartTicks = new List<long>();
            internal Inputs Input;
            internal Effects Effect;
            internal long Serial;
            private int lastWaitPage = -1;
            internal Rig(bool admit = true, bool mixed = false)
            {
                Host.Create(1, 0x10, "game_proc", 0, 9);
                Host.Create(2, PostPid, "last_player_proc", 0x30e);
                Host.Create(3, CameraPid, "cambot_proc", 0x100);
                Host.Create(4, 0x8001, "already_suspended", 0, 0x8007);
                Dispatcher = new SourceInstructionDispatcher(Host);
                Configure(mixed);
                if (admit) Admit(33);
            }
            internal void Configure(bool mixed)
            {
                Effect = new Effects(Host); Input = new Inputs { Log = Effect.Add };
                foreach (int value in mixed ? new[] { 0, 1, 0, 2 } : new[] { 0, 0, 0, 0 }) Input.Tsecs.Enqueue(value);
                foreach (int value in mixed ? new[] { -1, 0, 0, 1, 1, 0, 0, 1 } : new[] { 0, 0, 0, 0, 0, 0, 0, 0 })
                    Input.LowRes.Enqueue(value);
                if (mixed)
                {
                    Input.Samples.Enqueue(0x104); Input.Samples.Enqueue(unchecked((int)0x80000104));
                    Input.Shows.Enqueue(0); Input.Shows.Enqueue(1);
                }
                WaitArguments.Clear(); WaitStartTicks.Clear(); lastWaitPage = -1;
                Effect.CaptureHost();
            }
            internal SourceInstructionInvocation Invocation(SourceInstructionProcessIds ids = null)
            {
                return new SourceInstructionInvocation(4,
                    ids ?? new SourceInstructionProcessIds(InstructionPid, PostPid, CameraPid, MessagePid), Input, Effect);
            }
            internal void Admit(long serial) { Dispatcher.Admit(serial, Invocation()); Serial = serial; Effect.CaptureHost(); }
            internal SourceInstructionSnapshot Snapshot { get { return Dispatcher.Snapshot(Serial); } }
            internal void Tick()
            {
                Host.BeginTick(Host.SourceTick + 1);
                foreach (SourceProcessView process in Host.Processes)
                    if (Dispatcher.Owns(process.Serial))
                        Check(Dispatcher.Dispatch(process.Serial, function => Effect.Add("wake:" + function)), "actual dispatcher owns invocation");
                Effect.CaptureHost();
                SourceInstructionSnapshot snapshot = Snapshot;
                if (snapshot.Stage == SourceInstructionStage.Waiting && snapshot.Page.HasValue)
                {
                    int page = (int)snapshot.Page.Value;
                    if (page != lastWaitPage)
                    {
                        lastWaitPage = page;
                        Equal(10, snapshot.Wait.MinTicks, "all four native wait minima");
                        WaitArguments.Add(snapshot.Wait.WaitTicks);
                        WaitStartTicks.Add(Host.SourceTick);
                    }
                }
            }
            internal void Until(SourceInstructionStage stage)
            {
                for (int i = 0; i < 512; i++) { Tick(); if (Snapshot.Stage == stage) return; }
                throw new TestFailure("bounded real scheduler did not reach " + stage);
            }
        }

        private static void EntryAndExitOrder()
        {
            var rig = new Rig();
            SourceInstructionSnapshot beforeEntry = rig.Snapshot;
            Equal(SourceInstructionStage.PendingEntry, beforeEntry.Stage, "no effects on admission");
            Check(!beforeEntry.IdiotBoxesShown.HasValue && !beforeEntry.ConditionalProgramCompleted, "unentered state not success");
            rig.Tick();
            Equal(SourceInstructionStage.EntryDelay, rig.Snapshot.Stage, "entry first yields two");
            Equal<int?>(-1, rig.Effect.Flag, "entry flag before suspension");
            Check(!rig.Host.Require(1).Suspended, "no early suspension");
            rig.Tick();
            Check(!rig.Host.Require(1).Suspended, "second raw tick still entry sleep");
            rig.Tick();
            Equal(SourceInstructionStage.PageDelay, rig.Snapshot.Stage, "first page after entry sleep returns");
            Check(rig.Host.Require(1).Suspended, "game suspended by actual host");
            Check(!rig.Host.Require(2).Suspended && !rig.Host.Require(3).Suspended, "post-player and camera resumed");
            Check(rig.Host.Require(4).Suspended, "prior suspension not spuriously cleared early");
            var trace = rig.Effect.Trace;
            Before(trace, "flag:-1", "host:Sleep:" + Rig.InstructionPid + ":2");
            Before(trace, "host:SuspendMask:0:32768", "host:ResumeMask:" + Rig.InstructionPid + ":-1");
            Before(trace, "host:ResumeMask:" + Rig.InstructionPid + ":-1", "host:ResumeMask:" + Rig.PostPid + ":-1");
            Before(trace, "host:ResumeMask:" + Rig.PostPid + ":-1", "host:ResumeMask:" + Rig.CameraPid + ":-1");
            Before(trace, "host:ResumeMask:" + Rig.CameraPid + ":-1", "plate:begin");
            Before(trace, "plate:begin", "plate:width"); Before(trace, "plate:width", "plate:height");
            Before(trace, "plate:height", "plate:id"); Before(trace, "plate:id", "plate:vertices");
            Before(trace, "plate:vertices", "print:Rules:0");
            Equal(0, rig.Effect.PlatePosition.HorizontalOffset, "plate half hres");
            Equal(10, rig.Effect.PlatePosition.VerticalOffset, "plate half vres plus ten");
            Equal(2.5f, rig.Effect.PlatePosition.Depth, "plate depth");
            Check(rig.Effect.PlatePosition.TruncateFloatHalfAfterOffset, "plate native truncation position");
            rig.Tick();
            Equal(SourceInstructionStage.PageDelay, rig.Snapshot.Stage, "page setup sleep has not returned after one tick");
            Check(!trace.Contains("plate:flash"), "no early plate flash during setup sleep");
            rig.Tick();
            Equal(SourceInstructionStage.Waiting, rig.Snapshot.Stage, "page wait starts after setup sleep returns");
            rig.Until(SourceInstructionStage.FinalDelay);
            Equal<int?>(-1, rig.Effect.Flag, "cleanup/resume is not completion");
            Check(!rig.Snapshot.ConditionalProgramCompleted, "final delay not completed");
            Check(!rig.Host.Require(1).Suspended && !rig.Host.Require(4).Suspended, "resume_all, not saved-state restoration");
            Before(trace, "text-kill:Hints", "plate:delete");
            Before(trace, "plate:delete", "host:ResumeAllCall:0:0");
            Before(trace, "host:ResumeAllCall:0:0", "host:Sleep:" + Rig.InstructionPid + ":10");
            for (int i = 0; i < 9; i++)
            {
                rig.Tick();
                Equal<int?>(-1, rig.Effect.Flag, "flag before final sleep return");
                Check(!rig.Snapshot.ConditionalProgramCompleted, "no early final completion");
            }
            rig.Tick();
            Equal<int?>(2, rig.Effect.Flag, "completion only after tenth raw tick");
            Equal(SourceInstructionStage.Fallthrough, rig.Snapshot.Stage, "conditional source fallthrough");
            Check(rig.Host.Contains(rig.Serial), "no invented self-kill at fallthrough");
            Before(trace, "host:Sleep:" + Rig.InstructionPid + ":10", "flag:2");
            Before(trace, "flag:2", "host:Fallthrough:" + Rig.InstructionPid + ":0");
            Equal(SourceInstructionStage.PendingEntry, beforeEntry.Stage, "entry snapshot still immutable");
            int count = trace.Count;
            rig.Tick(); rig.Tick();
            Equal(count, trace.Count, "returned body not replayed on later dispatcher slots");
        }

        private static void PagesAndLiveInputs()
        {
            var rig = new Rig(mixed: true);
            rig.Until(SourceInstructionStage.Fallthrough);
            Equal("0|9|0|18", string.Join("|", rig.WaitArguments), "tsec evaluated separately at each wait");
            Equal(4, rig.Input.TsecReads, "four tsec reads, not initializer assumption");
            Equal(8, rig.Input.LowReads, "two live font tests per page");
            Equal(2, rig.Input.SwitchReads, "zero-domain pages do not poll");
            Equal(2, rig.Input.ShowReads, "sound gates only on accepted pages");
            Equal("Bast23|Bast10|Bast18|Bast10Low|Bast23|Bast10|Bast18|Bast10Low",
                string.Join("|", rig.Effect.Fonts), "source-position font evaluations");
            Equal(28, rig.Effect.Draws.Count, "seven print calls on each of four pages");
            int[] ruleX = { 0, -128, 40, 0, 0, 0, 0 }, ruleY = { 76, 47, 47, 25, 3, -19, -41 };
            int[] otherY = { 76, 49, 32, 8, -9, -33, -50 };
            SourceInstructionColor[] ruleColors = { SourceInstructionColor.LightRed, SourceInstructionColor.LightRed,
                SourceInstructionColor.White, SourceInstructionColor.LightYellow, SourceInstructionColor.White,
                SourceInstructionColor.LightYellow, SourceInstructionColor.White };
            SourceInstructionColor[] otherColors = { SourceInstructionColor.LightRed, SourceInstructionColor.LightYellow,
                SourceInstructionColor.LightYellow, SourceInstructionColor.White, SourceInstructionColor.White,
                SourceInstructionColor.LightYellow, SourceInstructionColor.LightYellow };
            for (int page = 0; page < 4; page++)
            {
                for (int line = 0; line < 7; line++)
                {
                    Draw draw = rig.Effect.Draws[page * 7 + line];
                    Equal((SourceInstructionPage)page, draw.Text.Page, "ordered page"); Equal(line, draw.Text.Line, "ordered line");
                    Equal(page == 0 && line == 1 ? SourceInstructionJustification.Left : SourceInstructionJustification.Center,
                        draw.Text.Justification, "source horizontal justification, vertical always centered");
                    Equal(page == 0 && line == 1 ? "HJUST_LEFT|VJUST_CENTER" : "HJUST_CENTER|VJUST_CENTER",
                        draw.Text.NativeSecondArgumentSymbols, "oprintf symbolic second argument, not inferred formatting");
                    Equal(page == 0 ? ruleX[line] : 0, draw.Position.HorizontalOffset, "text x expression");
                    Equal(page == 0 ? ruleY[line] : otherY[line], draw.Position.VerticalOffset, "text y expression");
                    Equal(1f, draw.Position.Depth, "text depth"); Check(!draw.Position.TruncateFloatHalfAfterOffset, "text half expression");
                    Equal(page == 0 ? ruleColors[line] : otherColors[line], draw.Color, "source text color");
                }
                var trace = rig.Effect.Trace;
                string name = ((SourceInstructionPage)page).ToString();
                Before(trace, "print:" + name + ":6", "text-flash:" + name);
                Before(trace, "text-flash:" + name, "text-kill:" + name);
                Equal(2L, rig.WaitStartTicks[page] - rig.Effect.FlasherTicks[page], "each page setup yields raw two");
                Equal(10L, rig.Effect.PageSoundTicks[page] - rig.WaitStartTicks[page], "ten initial sleeps before page advance");
            }
            Equal(1, rig.Effect.Trace.FindAll(value => value == "plate:begin").Count, "one shared plate");
            Equal(1, rig.Effect.Trace.FindAll(value => value == "plate:flash").Count, "only first page flashes plate");
            Before(rig.Effect.Trace, "text-flash:Rules", "plate:flash");
            Before(rig.Effect.Trace, "plate:flash", "read:tsec:0");
            Equal(5, rig.Effect.Sounds.Count, "four page sounds plus one conditionally admitted tick");
            Check(!rig.Effect.Sounds[0].Direct && rig.Effect.Sounds[1].Direct
                && !rig.Effect.Sounds[2].Direct && !rig.Effect.Sounds[3].Direct && !rig.Effect.Sounds[4].Direct,
                "tick before accepted-page advance, suppressed on nonzero gate");
            Equal("0|260|0|-2147483388", string.Join("|", rig.Snapshot.WaitResults), "all wait returns retained whole");
        }

        private static void DispatcherTimeout()
        {
            var rig = new Rig();
            rig.Input.Tsecs.Clear();
            foreach (int value in new[] { 1, 0, 0, 0 }) rig.Input.Tsecs.Enqueue(value);
            for (int i = 0; i < 9; i++) rig.Input.Samples.Enqueue(0x20);
            rig.Until(SourceInstructionStage.Fallthrough);
            Equal(9, rig.Input.SwitchReads, "dispatcher reaches all nine rejected polls");
            Equal(0, rig.Input.ShowReads, "dispatcher timeout never reads tick gate");
            Equal(19L, rig.Effect.PageSoundTicks[0] - rig.WaitStartTicks[0], "last rejection sleeps before page cleanup");
            Equal("0|0|0|0", string.Join("|", rig.Snapshot.WaitResults), "real dispatcher timeout results");
            Equal(4, rig.Effect.Sounds.Count, "timeout still emits each page sound");
        }

        private static string ExternalTrace(Effects effects)
        {
            return string.Join("|", effects.Trace.FindAll(value => !value.StartsWith("host:", StringComparison.Ordinal)));
        }
        private static void ReentryAndSnapshots()
        {
            var rig = new Rig();
            rig.Until(SourceInstructionStage.Waiting);
            SourceInstructionSnapshot waiting = rig.Snapshot;
            rig.Until(SourceInstructionStage.Fallthrough);
            SourceInstructionSnapshot completed = rig.Snapshot;
            Equal(0, waiting.WaitResults.Count, "old snapshot owns result array");
            Check(!waiting.Wait.Completed && !waiting.Wait.Result.HasValue, "old nested wait snapshot immutable");
            string first = ExternalTrace(rig.Effect);
            bool readOnly = false;
            try { ((IList<int>)completed.WaitResults)[0] = 42; }
            catch (NotSupportedException) { readOnly = true; }
            Check(readOnly, "snapshot result collection is read-only");
            rig.Configure(false); rig.Admit(34);
            Check(!rig.Snapshot.IdiotBoxesShown.HasValue && !rig.Snapshot.ConditionalProgramCompleted, "fresh explicit invocation pending");
            rig.Until(SourceInstructionStage.Fallthrough);
            Equal(first, ExternalTrace(rig.Effect), "sequential reentry runs the actual operation again");
            Equal(4, completed.WaitResults.Count, "prior completion unchanged by reentry");
            Equal(SourceInstructionStage.Fallthrough, rig.Dispatcher.Snapshot(33).Stage, "prior terminal entry not self-killed");
        }
        private static void DeterministicReset()
        {
            var first = new Rig(mixed: true); first.Until(SourceInstructionStage.Fallthrough);
            var reset = new Rig(mixed: true); reset.Until(SourceInstructionStage.Fallthrough);
            Equal(ExternalTrace(first.Effect), ExternalTrace(reset.Effect), "fresh host/dispatcher resets program and callbacks");
            Equal(first.Host.SourceTick, reset.Host.SourceTick, "deterministic raw dispatch count");
            Equal(string.Join("|", first.Snapshot.WaitResults), string.Join("|", reset.Snapshot.WaitResults), "deterministic results");
        }
        private static void AdmissionGuards()
        {
            var rig = new Rig(admit: false);
            Check(rig.Dispatcher.LastSnapshot == null, "no success-shaped uninvoked snapshot");
            Check(!rig.Dispatcher.Dispatch(999, f => { throw new TestFailure("unowned dispatch callback"); }), "unowned dispatch not consumed");
            ArgumentRejected(() => new SourceInstructionInvocation(0,
                new SourceInstructionProcessIds(1, 2, 3, 4), rig.Input, rig.Effect), "required source mask");
            ArgumentRejected(() => new SourceInstructionInvocation(4, null, rig.Input, rig.Effect), "required pid contract");
            ArgumentRejected(() => new SourceInstructionInvocation(4,
                new SourceInstructionProcessIds(1, 2, 3, 4), null, rig.Effect), "required input provider");
            ArgumentRejected(() => new SourceInstructionInvocation(4,
                new SourceInstructionProcessIds(1, 2, 3, 4), rig.Input, null), "required effects provider");
            StateRejected(() => rig.Dispatcher.Admit(33, rig.Invocation(
                new SourceInstructionProcessIds(Rig.InstructionPid, 99, Rig.CameraPid, Rig.MessagePid))), "unbound post pid rejected");
            StateRejected(() => rig.Dispatcher.Admit(33, rig.Invocation(
                new SourceInstructionProcessIds(0x10, Rig.PostPid, Rig.CameraPid, Rig.MessagePid))), "instruction pid collision rejected");
            Check(!rig.Host.Contains(33) && rig.Effect.Draws.Count == 0 && !rig.Effect.Flag.HasValue, "rejected admission has no gameplay effects");
            rig.Admit(33);
            StateRejected(() => rig.Dispatcher.Admit(34, rig.Invocation()), "shared-resource concurrent reentry rejected");
            Check(!rig.Host.Contains(34), "no partial concurrent process created");
        }
        private static void ExplicitFailures()
        {
            var rig = new Rig();
            rig.Input.Tsecs.Clear(); rig.Input.Tsecs.Enqueue(-1);
            rig.Until(SourceInstructionStage.PageDelay);
            rig.Tick();
            ArgumentRejected(rig.Tick, "negative live tsec is not clamped or defaulted");
            Equal(SourceInstructionStage.Faulted, rig.Snapshot.Stage, "failed external/domain path faulted");
            Equal(0L, rig.Host.Caller, "fault unwinds portable caller");
            Equal<int?>(-1, rig.Effect.Flag, "fault not completion");
            Check(!rig.Snapshot.ConditionalProgramCompleted, "fault not success");
            StateRejected(rig.Tick, "fault cannot silently retry external effects");
            StateRejected(() => rig.Dispatcher.Admit(34, rig.Invocation()), "fault requires explicit reset");
            var overflow = new Rig();
            overflow.Input.Tsecs.Clear(); overflow.Input.Tsecs.Enqueue(int.MaxValue / 9 + 1);
            overflow.Until(SourceInstructionStage.PageDelay); overflow.Tick();
            ArgumentRejected(overflow.Tick, "overflowing live wait argument rejected");
            var missingPlate = new Rig();
            missingPlate.Effect.ReturnNullPlate = true;
            missingPlate.Tick(); missingPlate.Tick();
            StateRejected(missingPlate.Tick, "invalid beginobj return is not fake resource success");
            Equal(SourceInstructionStage.Faulted, missingPlate.Snapshot.Stage, "missing required plate faults");
        }

        private static int Main()
        {
            string[] names = { "initial-delay", "timeout-boundaries", "opaque-acceptance", "invalid-wait-inputs",
                "entry-and-exit-order", "four-pages-live-inputs", "dispatcher-timeout", "reentry-snapshot-immutability", "deterministic-reset",
                "admission-guards", "explicit-failures" };
            Action[] tests = { InitialDelay, TimeoutBoundaries, OpaqueAcceptance, InvalidWaitInputs, EntryAndExitOrder,
                PagesAndLiveInputs, DispatcherTimeout, ReentryAndSnapshots, DeterministicReset, AdmissionGuards, ExplicitFailures };
            try
            {
                for (int i = 0; i < tests.Length; i++) { tests[i](); Console.WriteLine("PASS " + names[i]); }
                Console.WriteLine("PASS behavioral-cases=" + tests.Length);
                return 0;
            }
            catch (TestFailure failure)
            {
                Console.Error.WriteLine("FAIL " + failure.Message);
                return 1;
            }
        }
    }
}
