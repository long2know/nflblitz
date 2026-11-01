using System;
using System.Collections.Generic;
using System.Linq;
using Blitz.Contracts;
using Blitz.Gameplay;
using Blitz.Presentation;
using Blitz.Runtime;

internal static class InstructionPresentationRegressions
{
    private static readonly int[,] NativeLines = {
        { 4943, 4951, 4954, 4957, 4960, 4963, 4966 },
        { 4997, 5005, 5007, 5010, 5012, 5015, 5017 },
        { 5044, 5052, 5054, 5057, 5059, 5062, 5064 },
        { 5091, 5099, 5101, 5104, 5106, 5109, 5111 }
    };
    private static readonly SourceInstructionProcessIds Ids = new SourceInstructionProcessIds(0x8020, 0x8021, 0x8022, 0x8023);
    private const uint Red = 0xff993344, White = 0xffeeeeee, Yellow = 0xffdfc040;

    private sealed class Inputs : ISourceInstructionInputs
    {
        internal int Sample = unchecked((int)0x80000004), SwitchReads, LowReads, TsecReads, GateReads;
        internal bool ThrowTsec;
        internal int[] Low = { 0, 1, 1, 0, 0, 0, 1, 1 };
        internal int[] Tsec = { 1, 2, 3, 4 };
        internal int[] Gates = { 0, 1, 0, 1 };
        public int ReadPlayerSwitches() { SwitchReads++; return Sample; }
        public int ReadShowPlyselIdiotBox() { return Gates[(GateReads++) % Gates.Length]; }
        public int ReadTsec()
        {
            if (ThrowTsec) throw new InvalidOperationException("explicit unavailable live tsec");
            return Tsec[(TsecReads++) % Tsec.Length];
        }
        public int ReadIsLowResolution() { return Low[(LowReads++) % Low.Length]; }
    }

    private sealed class Formatter : ISourceInstructionNativeTextFormatter
    {
        internal readonly List<SourceInstructionFormatCall> Calls = new List<SourceInstructionFormatCall>();
        internal bool ForceLeft;
        internal uint? ReturnedStateColor;
        public SourceInstructionFormattedText Evaluate(SourceInstructionFormatCall call)
        {
            Calls.Add(call);
            // Explicit synthetic interpreter result, never original copy or a native formatter qualification.
            return new SourceInstructionFormattedText(ReturnedStateColor ?? call.Color,
                new[] { new SourceInstructionTextRun("EVALUATED", call.Font, call.Color, 0, 0,
                    ForceLeft ? SourceInstructionJustification.Left : call.Request.Justification) });
        }
    }

    private sealed class Resources : ISourceInstructionResources
    {
        internal readonly Formatter Formatter = new Formatter();
        internal readonly SourceInstructionTextProgram Text;
        internal int RasterReads, Generated, MissingSlot = -1;
        internal bool MissingRaster;
        internal Resources() { Text = new SourceInstructionTextProgram(Formats(), Formatter); }
        public SourceInstructionRaster ReadRaster()
        { RasterReads++; return MissingRaster ? default(SourceInstructionRaster) : new SourceInstructionRaster(513, 259, 256.75f, 128.75f); }
        public uint ResolveColor(SourceInstructionColor color)
        {
            if (color == SourceInstructionColor.LightRed) return Red;
            if (color == SourceInstructionColor.White) return White;
            if (color == SourceInstructionColor.LightYellow) return Yellow;
            throw new SourceInstructionPresentationException(SourceInstructionPresentationError.MissingResource, "unbound fixture color");
        }
        public SourceInstructionImage ResolvePlate(string imageSymbol, string textureSymbol)
        {
            Check(imageSymbol == "playresult" && textureSymbol == "SCR_PLATE_TID", "native plate symbols changed");
            return new SourceInstructionImage(imageSymbol, "fixture/normal", "fixture/white", 212, 79, 106, 39, 0, 0, 1, 1);
        }
        public IReadOnlyList<SourceInstructionGraphic> GeneratePlate(SourceInstructionImage image, SourceInstructionRaster raster,
            SourceInstructionPoint position, float widthScale, float heightScale)
        {
            Generated++;
            Check(image.Width == 212 && image.Height == 79 && raster.Width == 513, "actual supplied image/raster lost");
            Check(widthScale == 308f / 212f && heightScale == 190f / 79f, "source plate scales changed");
            return new[] { Graphic("plate", position.X, position.Y, White, position.Depth, "fixture/white") };
        }
        public SourceInstructionTextLayout FormatText(SourceInstructionTextRequest request, SourceInstructionFont font,
            uint color, SourceInstructionRaster raster, SourceInstructionPoint position)
        {
            if ((int)request.Page * 7 + request.Line == MissingSlot)
                throw new SourceInstructionPresentationException(SourceInstructionPresentationError.MissingResource, "explicit missing original slot");
            SourceInstructionFormattedText formatted = Text.Evaluate(request, font, color, raster, position);
            Check(formatted.Runs.Count == 1 && formatted.Runs[0].Text == "EVALUATED", "original format was printed without evaluation");
            return new SourceInstructionTextLayout(formatted.StateColor, new[] {
                Graphic(request.Page + "/" + request.Line, position.X, position.Y, formatted.Runs[0].Color, position.Depth, null) });
        }
    }

    private sealed class ObjectFlash : ISourceInstructionObjectFlashBinding
    {
        internal ISourceInstructionPlate Plate;
        internal int Calls, Flashes, Delay, Fourth;
        internal bool Missing, InvalidChroma;
        public ISourceInstructionObjectFlashProgram Create(ISourceInstructionPlate plate, int flashes, int delay, int fourthArgument)
        {
            Plate = plate; Calls++; Flashes = flashes; Delay = delay; Fourth = fourthArgument;
            return Missing ? null : new ExplicitExternalFlash(!InvalidChroma);
        }
        private sealed class ExplicitExternalFlash : ISourceInstructionObjectFlashProgram
        {
            private int visits;
            private readonly bool chroma;
            internal ExplicitExternalFlash(bool chroma) { this.chroma = chroma; }
            public SourceInstructionPlateFlashStep Advance()
            {
                // Deliberately synthetic external SDK binding. This schedule is not native evidence.
                return visits++ == 0 ? new SourceInstructionPlateFlashStep(true, chroma, false, 3)
                    : new SourceInstructionPlateFlashStep(false, false, true, 0);
            }
        }
    }

    private sealed class Sound : ISourceInstructionSoundPlayer
    {
        internal readonly List<SourceInstructionSound> Calls = new List<SourceInstructionSound>();
        internal readonly List<SourceInstructionSoundContext> Contexts = new List<SourceInstructionSoundContext>();
        public void Play(SourceInstructionSound sound, SourceInstructionSoundContext context) { Calls.Add(sound); Contexts.Add(context); }
    }

    private sealed class Port : ISourceInstructionProgramBinding
    {
        internal SourcePortableProcessHost Host;
        internal SourceInstructionDispatcher Dispatcher;
        internal readonly List<SourcePossessionProcessRecord> Trace = new List<SourcePossessionProcessRecord>();
        internal Action<SourcePortableProcessHost> Control;
        private long next = 100;
        internal Port() { Reset(); }
        internal void Reset()
        {
            Host = new SourcePortableProcessHost(); Dispatcher = new SourceInstructionDispatcher(Host);
            Host.Create(1, Ids.PostPlayer, "last_player_proc", 0);
            Host.Create(2, Ids.Camera, "cambot_proc", 0);
            Host.Create(3, 99, "game_proc", 0);
            Trace.Clear(); Control = null;
        }
        public SourceInstructionSnapshot Current { get { return Dispatcher.LastSnapshot; } }
        public IReadOnlyList<SourcePossessionProcessRecord> Records { get { return Host.Records; } }
        public long Invoke(SourceInstructionInvocation invocation)
        { long serial = ++next; Dispatcher.Admit(serial, invocation); return serial; }
        internal void Step()
        {
            Host.BeginTick(Host.SourceTick + 1);
            Action<SourcePortableProcessHost> control = Control; Control = null;
            if (control != null) control(Host);
            try
            {
                foreach (SourceProcessView process in Host.Processes)
                {
                    if (process.Function == SourceInstructionDispatcher.Function) Dispatcher.Dispatch(process.Serial, name => { });
                    else if (Host.Visit(process.Serial)) { Host.Sleep(1); Host.Complete(); }
                }
            }
            finally { Trace.AddRange(Host.Records); }
        }
        internal void Mask(string operation, int id, int mask)
        {
            Control = host =>
            {
                host.Record(operation, 0, id, mask);
                if (operation == "SuspendMask") host.SuspendMultiple(id, mask);
                else if (operation == "ResumeMask") host.ResumeMultiple(id, mask);
                else host.ResumeAll();
            };
        }
    }

    private sealed class Fixture : IDisposable
    {
        internal readonly Inputs Inputs = new Inputs();
        internal readonly Resources Resources = new Resources();
        internal readonly ObjectFlash Flash = new ObjectFlash();
        internal readonly Sound Sound = new Sound();
        internal readonly Port Port = new Port();
        internal readonly SourceInstructionConsumer Consumer;
        internal readonly Dictionary<SourceInstructionPage, int> Waits = new Dictionary<SourceInstructionPage, int>();
        internal Fixture() { Consumer = new SourceInstructionConsumer(Port, Resources, Flash, Sound); }
        internal SourceInstructionPresentation Model { get { return Consumer.Presentation; } }
        internal long Invoke() { return Consumer.Invoke(4, Ids, Inputs); }
        internal void Step()
        {
            try { Port.Step(); }
            finally { Consumer.AfterSourceStep(Port.Host.SourceTick); }
            if (Port.Current != null && Port.Current.Page.HasValue && Port.Current.Wait != null)
                Waits[Port.Current.Page.Value] = Port.Current.Wait.WaitTicks;
        }
        internal void Until(Func<bool> predicate, int limit = 800)
        {
            for (int i = 0; i < limit; i++) { if (predicate()) return; Step(); }
            Check(predicate(), "bounded source continuation did not reach expected state");
        }
        internal SourceInstructionPlateState Plate { get { return Model.Objects.OfType<SourceInstructionPlateState>().Single(); } }
        internal SourceInstructionTextState Text(SourceInstructionPage page, int line)
        { return Model.Objects.OfType<SourceInstructionTextState>().Single(t => t.Request.Page == page && t.Request.Line == line); }
        public void Dispose() { Consumer.Dispose(); }
    }

    private static List<SourceInstructionFormatSlot> Formats()
    {
        var slots = new List<SourceInstructionFormatSlot>();
        for (int page = 0; page < 4; page++)
            for (int line = 0; line < 7; line++)
            {
                bool left = page == 0 && line == 1;
                slots.Add(new SourceInstructionFormatSlot((SourceInstructionPage)page, line, NativeLines[page, line],
                    "SYNTHETIC %" + page + ":" + line + "\u001bNATIVE_CONTROL", left
                        ? "HJUST_LEFT|VJUST_CENTER" : "HJUST_CENTER|VJUST_CENTER", left ? 0x2468 : -0x1357));
            }
        return slots;
    }

    private static SourceInstructionGraphic Graphic(string image, double x, double y, uint color, float depth, string white)
    {
        return new SourceInstructionGraphic("fixture/normal", white, image, color, depth, new[] {
            new SourceInstructionVertex(x, y, 0, 0), new SourceInstructionVertex(x + 1, y, 1, 0),
            new SourceInstructionVertex(x, y + 1, 0, 1) });
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static T Throws<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T error) { return error; }
        throw new Exception("Expected " + typeof(T).Name);
    }
    private static void AtRules(Fixture fixture)
    {
        fixture.Invoke();
        fixture.Until(() => fixture.Port.Current.Stage == SourceInstructionStage.PageDelay);
    }

    private static void LegacyNoInvocationIsInert()
    {
        using (var f = new Fixture())
        {
            for (int i = 0; i < 20; i++) f.Step();
            Check(!f.Consumer.HasInvocation && f.Consumer.Program == null && f.Model.IdiotBoxesShown == null, "legacy admitted a program");
            Check(f.Model.Objects.Count == 0 && f.Model.Frame().Count == 0 && f.Model.Processes.Count == 0, "legacy created retained objects");
            Check(f.Resources.RasterReads == 0 && f.Resources.Formatter.Calls.Count == 0 && f.Sound.Calls.Count == 0, "legacy consumed native resources");
        }
    }
    private static void RegisteredProducerOwnsReturnedObjects()
    {
        using (var f = new Fixture())
        {
            AtRules(f);
            Check(f.Consumer.HasInvocation && f.Port.Current.Process.Serial > 0 && f.Model.IdiotBoxesShown == -1, "real dispatcher did not reach consumer");
            Check(f.Model.Objects.Count == 8 && f.Resources.Generated == 1, "source plate/print creation count changed");
            Check(ReferenceEquals(f.Model.Messages[0], f.Text(SourceInstructionPage.Rules, 0)), "pmsg[0] lost returned identity");
            Check(ReferenceEquals(f.Model.Messages[1], f.Text(SourceInstructionPage.Rules, 1)), "pmsg[1] lost returned identity");
            Check(ReferenceEquals(f.Model.Flashers[0].Title, f.Model.Messages[0]) && ReferenceEquals(f.Model.Flashers[0].Second, f.Model.Messages[1]), "flasher copied shared strings");
            Check(f.Plate.Position.X == 256 && f.Plate.Position.Y == 138 && f.Plate.Position.Depth == 2.5f, "native plate truncation changed");
            Check(f.Plate.ObjectIdSymbol == "OID_MESSAGE" && f.Plate.WidthScale == 308f / 212f && f.Plate.HeightScale == 190f / 79f, "plate property writes lost");
            Check(f.Model.Messages[0].Position.X == 256.75f && f.Model.Messages[0].Position.Y == 204.75f, "text was plate-truncated");
            Check(f.Model.Frame().Count == 8 && f.Model.Frame()[0].ImageSymbol == "plate", "generated plate never reached draw frame");
            Check(f.Model.Objects.Select(o => o.CreationOrder).SequenceEqual(Enumerable.Range(1, 8).Select(i => (long)i)), "creation order changed");
        }
    }
    private static void All28SlotsRetainSourceStyleAndArguments()
    {
        using (var f = new Fixture())
        {
            f.Invoke(); f.Until(() => f.Port.Current.Stage == SourceInstructionStage.Fallthrough);
            Check(f.Resources.Formatter.Calls.Count == 28 && f.Inputs.LowReads == 8 && f.Inputs.TsecReads == 4, "live source call counts changed");
            int[] bodyOffsets = { 49, 32, 8, -9, -33, -50 };
            SourceInstructionFont[] titleFonts = { SourceInstructionFont.Bast18, SourceInstructionFont.Bast23, SourceInstructionFont.Bast18, SourceInstructionFont.Bast23 };
            SourceInstructionFont[] bodyFonts = { SourceInstructionFont.Bast10Low, SourceInstructionFont.Bast10, SourceInstructionFont.Bast10, SourceInstructionFont.Bast10Low };
            for (int page = 0; page < 4; page++)
                for (int line = 0; line < 7; line++)
                {
                    SourceInstructionTextState text = f.Text((SourceInstructionPage)page, line);
                    SourceInstructionFormatCall call = f.Resources.Formatter.Calls[page * 7 + line];
                    SourceInstructionColor expected = line == 0 || page == 0 && line == 1 ? SourceInstructionColor.LightRed
                        : page == 0 ? (line == 2 || line == 4 || line == 6 ? SourceInstructionColor.White : SourceInstructionColor.LightYellow)
                        : line == 3 || line == 4 ? SourceInstructionColor.White : SourceInstructionColor.LightYellow;
                    int dy = line == 0 ? 76 : page == 0 ? (line == 1 || line == 2 ? 47 : 25 - (line - 3) * 22) : bodyOffsets[line - 1];
                    int dx = page == 0 && line == 1 ? -128 : page == 0 && line == 2 ? 40 : 0;
                    Check(text.StringId == 0x34 && text.SourceColor == expected && text.Font == (line == 0 ? titleFonts[page] : bodyFonts[page]), "wrong retained source style at " + page + "/" + line);
                    Check(text.Position.X == 256.75f + dx && text.Position.Y == 128.75f + dy && text.Position.Depth == 1, "wrong retained position at " + page + "/" + line);
                    Check(call.Font == text.Font && call.Color == f.Resources.ResolveColor(expected), "formatter lost selected font/color");
                    Check(call.Slot.NativePrintLine == NativeLines[page, line] && call.Slot.OriginalFormat == Formats()[page * 7 + line].OriginalFormat, "format slot or original escapes changed");
                    Check(call.Slot.NativeSecondArgument == (page == 0 && line == 1 ? 0x2468 : -0x1357)
                        && call.Slot.NativeSecondArgumentSymbols == text.Request.NativeSecondArgumentSymbols, "native variadic argument was reinterpreted");
                }
            Check(f.Waits.OrderBy(p => p.Key).Select(p => p.Value).SequenceEqual(new[] { 9, 18, 27, 36 }), "live per-page tsec was replaced");
            Check(f.Port.Current.WaitResults.Count == 4 && f.Port.Current.WaitResults.All(v => v == unchecked((int)0x80000004)), "opaque full samples were masked");
        }
    }
    private static void TextFlasherMutatesSharedReturnedStrings()
    {
        using (var f = new Fixture())
        {
            f.Inputs.Sample = 0; f.Inputs.Tsec = new[] { 50 };
            AtRules(f);
            long created = f.Port.Host.SourceTick;
            SourceInstructionTextState title = f.Model.Messages[0], second = f.Model.Messages[1];
            IReadOnlyList<SourceInstructionGraphic> before = f.Model.Frame();
            while (f.Port.Host.SourceTick < created + 7) f.Step();
            Check(title.StateColor == Red && second.StateColor == Red, "flasher wrote before returned sleep(7)");
            f.Step();
            Check(title.StateColor == White && second.StateColor == White && ReferenceEquals(title, f.Model.Messages[0]), "shared state colors did not flash on exact source tick");
            Check(f.Model.Frame().Single(g => g.ImageSymbol == "Rules/0").Color == White
                && before.Single(g => g.ImageSymbol == "Rules/0").Color == Red, "character-color output or immutable prior frame was lost");
            Check(f.Text(SourceInstructionPage.Rules, 3).StateColor == Yellow, "flasher changed an unowned string");
            for (int i = 0; i < 7; i++) f.Step();
            Check(title.StateColor == Red && second.StateColor == Red && f.Model.Frame().Single(g => g.ImageSymbol == "Rules/0").Color == Red, "original captured colors were not restored");
        }
        using (var f = new Fixture())
        {
            f.Inputs.Sample = 0; f.Inputs.Tsec = new[] { 50 };
            AtRules(f);
            SourceInstructionTextState title = f.Model.Messages[0], second = f.Model.Messages[1];
            title.StateColor = Yellow;
            f.Step();
            for (int i = 0; i < 7; i++) f.Step();
            Check(title.StateColor == White, "first returned source sleep did not flash the live string");
            for (int i = 0; i < 7; i++) f.Step();
            Check(title.StateColor == Yellow && second.StateColor == Red
                && f.Model.Frame().Single(g => g.ImageSymbol == "Rules/0").Color == Yellow,
                "flasher cached creation-time style instead of reading each returned string's live entry color");
        }
        using (var f = new Fixture())
        {
            f.Inputs.Sample = 0; f.Inputs.Tsec = new[] { 50 };
            f.Resources.Formatter.ReturnedStateColor = Yellow;
            AtRules(f);
            SourceInstructionTextState title = f.Model.Messages[0];
            Check(title.SourceColor == SourceInstructionColor.LightRed && title.StateColor == Yellow
                && f.Model.Frame().Single(g => g.ImageSymbol == "Rules/0").Color == Red,
                "returned native string state was confused with selected or per-character colors");
            f.Step();
            for (int i = 0; i < 14; i++) f.Step();
            Check(title.StateColor == Yellow && f.Model.Frame().Single(g => g.ImageSymbol == "Rules/0").Color == Yellow,
                "flasher restored guessed pre-format color instead of returned ostring state color");
        }
    }
    private static void MaskForwardingUsesNativePredicateAndOrder()
    {
        using (var f = new Fixture())
        {
            f.Inputs.Sample = 0; f.Inputs.Tsec = new[] { 50 };
            AtRules(f);
            f.Until(() => f.Port.Current.Stage == SourceInstructionStage.Waiting);
            SourceInstructionFlasherState text = f.Model.Flashers.Single(p => p.Plate == null);
            f.Port.Mask("SuspendMask", 0, 0x8000); f.Step();
            SourceProcessView suspended = f.Model.Processes.Single(p => p.Serial == text.Serial);
            Check(suspended.Suspended, "native mask did not suspend PID-zero text flasher");
            for (int i = 0; i < 5; i++) f.Step();
            Check(f.Model.Processes.Single(p => p.Serial == text.Serial).SleepRemaining == suspended.SleepRemaining, "suspended sleep word advanced");
            f.Port.Mask("ResumeMask", 0, -1); f.Step();
            Check(!f.Model.Processes.Single(p => p.Serial == text.Serial).Suspended, "exact native resume mask was not forwarded");
            f.Port.Mask("SuspendMask", 0, 0); f.Step();
            Check(f.Model.Processes.All(p => p.Suspended), "zero mask did not match all owned processes");
            f.Port.Mask("ResumeAllCall", 0, 0); f.Step();
            Check(f.Model.Processes.All(p => !p.Suspended), "resume_all did not reach retained flasher host");
            SourcePossessionProcessRecord[] entry = f.Port.Trace.Where(r => r.Operation == "SuspendMask" || r.Operation == "ResumeMask").Take(4).ToArray();
            Check(entry.Length == 4 && entry[0].Operation == "SuspendMask" && entry[0].Id == 0 && entry[0].Value == 0x8000
                && entry.Skip(1).Select(r => r.Id).SequenceEqual(new[] { Ids.Instructions, Ids.PostPlayer, Ids.Camera }), "entry mask/resume source order changed");
        }
    }
    private static void ObjectFlashConsumesOwnedHandleAndExactArguments()
    {
        using (var f = new Fixture())
        {
            f.Inputs.Sample = 0; f.Inputs.Tsec = new[] { 50 };
            AtRules(f); f.Until(() => f.Flash.Calls == 1);
            Check(ReferenceEquals(f.Flash.Plate, f.Plate) && f.Flash.Flashes == 2 && f.Flash.Delay == 3 && f.Flash.Fourth == 0, "SDK helper lost original object identity/arguments");
            SourceInstructionFlasherState flasher = f.Model.Flashers.Single(p => p.Plate != null);
            Check(flasher.Name == "flash" && flasher.ProcessId == Ids.Message && f.Plate.WhiteFlash && f.Plate.ChromaKey, "owned object flasher state did not execute");
            Check(f.Model.Frame()[0].Texture == "fixture/white", "white/chroma mutation did not reach actual draw frame");
            f.Until(() => flasher.IsKilled);
            Check(!f.Plate.IsDeleted && !f.Plate.WhiteFlash && f.Model.Frame()[0].Texture == "fixture/normal", "external helper completion deleted plate or left white output");
        }
    }
    private static void PageCleanupRetainsSharedSlotsWithoutAliasing()
    {
        using (var f = new Fixture())
        {
            AtRules(f);
            SourceInstructionTextState title = f.Model.Messages[0], detail = f.Model.Messages[1];
            SourceInstructionFlasherState old = f.Model.Flashers[0];
            f.Until(() => f.Port.Current.Page == SourceInstructionPage.Receivers);
            Check(title.IsDeleted && detail.IsDeleted && old.IsKilled, "page strings/flasher were not deleted");
            Check(ReferenceEquals(f.Model.Messages[1], detail) && ReferenceEquals(f.Model.Messages[0], f.Text(SourceInstructionPage.Receivers, 0)), "shared pmsg writes were cleared/replaced incorrectly");
            Check(f.Model.Flashers.Last().Second == null && f.Model.Frame().Count == 8 && !f.Plate.IsDeleted, "receiver page reused stale highlight or lost shared plate");
        }
    }
    private static void FinalCleanupPrecedesReturnedDelayAndReentry()
    {
        using (var f = new Fixture())
        {
            long first = f.Invoke();
            f.Until(() => f.Port.Current.Stage == SourceInstructionStage.FinalDelay);
            SourceInstructionPlateState plate = f.Plate;
            Check(f.Model.IdiotBoxesShown == -1 && f.Model.Frame().Count == 0 && f.Model.Objects.All(o => o.IsDeleted)
                && f.Model.Flashers.All(p => p.IsKilled), "final cleanup did not precede completion flag");
            Check(f.Port.Host.Processes.All(p => !p.Suspended) && f.Port.Trace.Any(r => r.Operation == "ResumeAllCall"), "final source resume_all did not occur");
            for (int i = 0; i < 9; i++) { f.Step(); Check(f.Model.IdiotBoxesShown == -1, "completion flag preceded returned sleep(10)"); }
            f.Step();
            Check(f.Port.Current.Stage == SourceInstructionStage.Fallthrough && f.Model.IdiotBoxesShown == 2 && f.Port.Host.Contains(first), "fallthrough/self-retirement semantics changed");
            long second = f.Invoke();
            Check(second > first && plate.IsDeleted && f.Model.Objects.Count == 0 && f.Model.Messages.All(t => t == null), "sequential reentry reused retained resources");
            f.Until(() => f.Model.Objects.Count == 8);
            Check(!ReferenceEquals(plate, f.Plate) && f.Model.IdiotBoxesShown == -1, "reentry did not create fresh actual handles");
        }
    }
    private static void ResetInvalidatesHandlesAndDoesNotRestartProgram()
    {
        using (var f = new Fixture())
        {
            AtRules(f);
            SourceInstructionTextState old = f.Model.Messages[0];
            SourceInstructionFlasherState flasher = f.Model.Flashers[0];
            f.Port.Reset(); f.Consumer.OnSourceProgramReset();
            Check(old.IsDeleted && flasher.IsKilled && f.Model.Frame().Count == 0 && f.Model.IdiotBoxesShown == null
                && f.Model.Messages.All(t => t == null) && !f.Consumer.HasInvocation, "actual restart did not retire owned state");
            for (int i = 0; i < 5; i++) f.Step();
            Check(f.Model.Objects.Count == 0, "reset invented automatic instruction admission");
            f.Invoke(); f.Until(() => f.Model.Objects.Count == 8);
            Check(!ReferenceEquals(old, f.Model.Messages[0]), "reset preserved a stale returned text object");
        }
    }
    private static void ErrorsFailClosedAndOwnedResourcesAreCleaned()
    {
        using (var f = new Fixture())
        {
            f.Resources.MissingSlot = 3; f.Invoke();
            Throws<SourceInstructionPresentationException>(() => f.Until(() => f.Model.Objects.Count == 8));
            Check(f.Model.Failure != null && f.Model.Objects.All(o => o.IsDeleted) && f.Model.Flashers.All(p => p.IsKilled)
                && f.Model.Frame().Count == 0 && f.Model.IdiotBoxesShown != 2 && f.Port.Current.Stage == SourceInstructionStage.Faulted, "resource failure qualified progress or leaked objects");
            Throws<SourceInstructionPresentationException>(() => f.Invoke());
            f.Port.Reset(); f.Consumer.OnSourceProgramReset(); f.Resources.MissingSlot = -1;
            f.Invoke(); f.Until(() => f.Model.Objects.Count == 8);
        }
        using (var f = new Fixture())
        {
            f.Flash.Missing = true; f.Invoke();
            var error = Throws<SourceInstructionPresentationException>(() => f.Until(() => f.Flash.Calls != 0));
            Check(error.Error == SourceInstructionPresentationError.UnsupportedNativeBinding && f.Model.Frame().Count == 0, "missing SDK helper became successful flashing");
        }
        using (var f = new Fixture())
        {
            f.Inputs.ThrowTsec = true; f.Invoke();
            Throws<SourceInstructionPresentationException>(() => f.Until(() => f.Port.Current.Stage == SourceInstructionStage.Waiting));
            Check(f.Model.Objects.All(o => o.IsDeleted) && f.Model.IdiotBoxesShown != 2, "live input failure leaked retained state");
        }
        using (var f = new Fixture())
        {
            f.Flash.InvalidChroma = true; f.Invoke();
            f.Until(() => f.Flash.Calls != 0);
            var error = Throws<SourceInstructionPresentationException>(() => f.Model.Frame());
            Check(error.Error == SourceInstructionPresentationError.UnsupportedNativeBinding
                && f.Model.Failure != null && f.Model.Objects.All(o => o.IsDeleted),
                "unsupported white/chroma state silently drew normal pixels or leaked resources");
        }
    }
    private static void FormatCatalogRejectsIncompleteOrIncorrectNativeBindings()
    {
        List<SourceInstructionFormatSlot> formats = Formats();
        Throws<SourceInstructionPresentationException>(() => new SourceInstructionTextProgram(formats.Take(27), new Formatter()));
        Throws<SourceInstructionPresentationException>(() => new SourceInstructionTextProgram(formats.Concat(new[] { formats[0] }), new Formatter()));
        formats[0] = new SourceInstructionFormatSlot(SourceInstructionPage.Rules, 0, 4997, "original", "HJUST_CENTER|VJUST_CENTER", 10);
        Throws<SourceInstructionPresentationException>(() => new SourceInstructionTextProgram(formats, new Formatter()));
        Throws<SourceInstructionPresentationException>(() => new SourceInstructionFormatSlot(SourceInstructionPage.Rules, 1, 4951, "original", "HJUST_CENTER|VJUST_CENTER", 10));
        Throws<SourceInstructionPresentationException>(() => new SourceInstructionTextRun("\u001bunevaluated", SourceInstructionFont.Bast10, White, 0, 0, SourceInstructionJustification.Center));
        using (var f = new Fixture())
        {
            f.Resources.MissingRaster = true; f.Invoke();
            Throws<SourceInstructionPresentationException>(() => f.Until(() => f.Model.Objects.Count > 0));
            Check(f.Model.Frame().Count == 0 && f.Model.IdiotBoxesShown != 2, "absent raster received fake dimensions");
        }
    }
    private static void AdmissionAndLifecycleErrorsAreExplicit()
    {
        using (var f = new Fixture())
        {
            Throws<ArgumentOutOfRangeException>(() => f.Consumer.Invoke(0, Ids, f.Inputs));
            Throws<ArgumentNullException>(() => f.Consumer.Invoke(4, Ids, null));
            Throws<InvalidOperationException>(() => f.Consumer.Invoke(4, new SourceInstructionProcessIds(17, 88, 99, 66), f.Inputs));
            Check(!f.Consumer.HasInvocation && f.Model.Objects.Count == 0, "failed admission produced presentation");
            AtRules(f);
            Throws<SourceInstructionPresentationException>(() => f.Invoke());
            using (var other = new Fixture())
            {
                AtRules(other);
                SourceInstructionPresentationException error = Throws<SourceInstructionPresentationException>(
                    () => other.Model.StoreMessage(0, f.Model.Messages[0]));
                Check(error.Error == SourceInstructionPresentationError.InvalidHandle && other.Model.Objects.All(o => o.IsDeleted), "foreign returned handle was accepted");
            }
        }
        var disposed = new Fixture(); disposed.Dispose();
        Throws<ObjectDisposedException>(() => disposed.Consumer.AfterSourceStep(1));
        Throws<ObjectDisposedException>(() => disposed.Invoke());
    }
    private static void PresentationTickEpochMapsActualSourceTicks()
    {
        using (var f = new Fixture())
        {
            for (int i = 0; i < 40; i++) f.Step();
            AtRules(f);
            SourceProcessView process = f.Model.Processes.Single();
            Check(process.BirthTick == f.Port.Host.SourceTick && process.BirthTick > 40, "owned process exposed a fabricated/global-clock birth tick");
            f.Step();
            process = f.Model.Processes.Single();
            Check(process.LastWakeTick == f.Port.Host.SourceTick && f.Model.ProcessRecords.All(r => r.SourceTick == f.Port.Host.SourceTick), "owned source-epoch mapping lost actual tick identity");
            Throws<SourceInstructionPresentationException>(() => f.Consumer.AfterSourceStep(1));
            Check(f.Model.Objects.All(o => o.IsDeleted), "source rewind continued instead of requiring restart");
        }
    }
    private static void NativeFormattingDoesNotInferAlignment()
    {
        var formatter = new Formatter { ForceLeft = true, ReturnedStateColor = Red };
        var text = new SourceInstructionTextProgram(Formats(), formatter);
        SourceInstructionTextRequest request = new SourceInstructionTextRequest(SourceInstructionPage.Hints, 2, SourceInstructionJustification.Center);
        SourceInstructionFormattedText formatted = text.Evaluate(request, SourceInstructionFont.Bast23, Yellow,
            new SourceInstructionRaster(513, 259, 256.75f, 128.75f), new SourceInstructionPoint(11, 22, 1));
        Check(formatted.Runs[0].Justification == SourceInstructionJustification.Left && formatted.StateColor == Red
            && formatted.Runs[0].Color == Yellow && formatter.Calls[0].Slot.NativeSecondArgument == -0x1357
            && formatter.Calls[0].Slot.OriginalFormat.IndexOf('\u001b') >= 0, "native varargs/escape interpretation was replaced by inferred request alignment");
    }
    private static void MaskDeletionMatchesSourceStringIds()
    {
        var resources = new Resources();
        using (var model = new SourceInstructionPresentation(resources, new ObjectFlash(), new Sound(), () => { }))
        {
            model.BeginInvocation(77);
            model.Synchronize(42, new SourcePossessionProcessRecord[0], false);
            model.SetTextFont(SourceInstructionFont.Bast18); model.SetTextColor(SourceInstructionColor.LightRed);
            model.SetTextPosition(new SourceInstructionPosition(0, 0, 1, false));
            model.SetStringId(0x134);
            var first = (SourceInstructionTextState)model.Print(new SourceInstructionTextRequest(SourceInstructionPage.Rules, 0, SourceInstructionJustification.Center));
            model.SetStringId(0x34);
            var second = (SourceInstructionTextState)model.Print(new SourceInstructionTextRequest(SourceInstructionPage.Rules, 2, SourceInstructionJustification.Center));
            model.StoreMessage(0, second);
            model.SetStringId(0x35);
            var third = (SourceInstructionTextState)model.Print(new SourceInstructionTextRequest(SourceInstructionPage.Rules, 3, SourceInstructionJustification.Center));
            model.DeleteStrings(0x34, 0xff);
            Check(first.IsDeleted && second.IsDeleted && !third.IsDeleted && ReferenceEquals(model.Messages[0], second)
                && model.Frame().Count == 1, "native id/mask deletion or retained pmsg semantics changed");
        }
    }
    private static void SoundOrderingKeepsNativeIdentitiesAndLiveGate()
    {
        using (var f = new Fixture())
        {
            long serial = f.Invoke(); f.Until(() => f.Port.Current.Stage == SourceInstructionStage.Fallthrough);
            Check(f.Sound.Calls.SequenceEqual(new[] { SourceInstructionSound.Tick, SourceInstructionSound.PageAdvance,
                SourceInstructionSound.PageAdvance, SourceInstructionSound.Tick, SourceInstructionSound.PageAdvance, SourceInstructionSound.PageAdvance }), "live acceptance gate or ordered native sounds changed");
            Check(f.Inputs.GateReads == 4 && f.Inputs.SwitchReads == 4, "sound gate was read at setup rather than actual acceptance");
            Check(f.Sound.Contexts.Select(c => c.CallOrdinal).SequenceEqual(Enumerable.Range(0, 6))
                && f.Sound.Contexts.All(c => c.InstructionSerial == serial && c.SourceTick > 0)
                && f.Sound.Contexts.Select(c => c.SourceTick).SequenceEqual(f.Sound.Contexts.Select(c => c.SourceTick).OrderBy(t => t)), "sound call order/owning native process context lost");
            Check(SourceInstructionSound.Tick.Direct && SourceInstructionSound.Tick.BankSymbol == null && SourceInstructionSound.Tick.SoundSymbol == "tick_sndn"
                && SourceInstructionSound.Tick.VolumeSymbol == "VOLUME1" && SourceInstructionSound.Tick.RawPriority == 1 && SourceInstructionSound.Tick.Pan == 127, "direct tick arguments changed");
            Check(!SourceInstructionSound.PageAdvance.Direct && SourceInstructionSound.PageAdvance.BankSymbol == "plyr_bnk_str"
                && SourceInstructionSound.PageAdvance.SoundSymbol == "RICOCHET_SND" && SourceInstructionSound.PageAdvance.VolumeSymbol == "VOLUME2"
                && SourceInstructionSound.PageAdvance.PrioritySymbol == "PRIORITY_LVL5" && SourceInstructionSound.PageAdvance.Pan == 127, "page sound arguments changed");
        }
    }

    private sealed class Case
    {
        internal readonly string Name;
        internal readonly Action Test;
        internal Case(string name, Action test) { Name = name; Test = test; }
    }
    public static int Main()
    {
        Case[] cases = {
            new Case(nameof(LegacyNoInvocationIsInert), LegacyNoInvocationIsInert),
            new Case(nameof(RegisteredProducerOwnsReturnedObjects), RegisteredProducerOwnsReturnedObjects),
            new Case(nameof(All28SlotsRetainSourceStyleAndArguments), All28SlotsRetainSourceStyleAndArguments),
            new Case(nameof(TextFlasherMutatesSharedReturnedStrings), TextFlasherMutatesSharedReturnedStrings),
            new Case(nameof(MaskForwardingUsesNativePredicateAndOrder), MaskForwardingUsesNativePredicateAndOrder),
            new Case(nameof(ObjectFlashConsumesOwnedHandleAndExactArguments), ObjectFlashConsumesOwnedHandleAndExactArguments),
            new Case(nameof(PageCleanupRetainsSharedSlotsWithoutAliasing), PageCleanupRetainsSharedSlotsWithoutAliasing),
            new Case(nameof(FinalCleanupPrecedesReturnedDelayAndReentry), FinalCleanupPrecedesReturnedDelayAndReentry),
            new Case(nameof(ResetInvalidatesHandlesAndDoesNotRestartProgram), ResetInvalidatesHandlesAndDoesNotRestartProgram),
            new Case(nameof(ErrorsFailClosedAndOwnedResourcesAreCleaned), ErrorsFailClosedAndOwnedResourcesAreCleaned),
            new Case(nameof(FormatCatalogRejectsIncompleteOrIncorrectNativeBindings), FormatCatalogRejectsIncompleteOrIncorrectNativeBindings),
            new Case(nameof(AdmissionAndLifecycleErrorsAreExplicit), AdmissionAndLifecycleErrorsAreExplicit),
            new Case(nameof(PresentationTickEpochMapsActualSourceTicks), PresentationTickEpochMapsActualSourceTicks),
            new Case(nameof(NativeFormattingDoesNotInferAlignment), NativeFormattingDoesNotInferAlignment),
            new Case(nameof(MaskDeletionMatchesSourceStringIds), MaskDeletionMatchesSourceStringIds),
            new Case(nameof(SoundOrderingKeepsNativeIdentitiesAndLiveGate), SoundOrderingKeepsNativeIdentitiesAndLiveGate)
        };
        int failures = 0;
        foreach (Case test in cases)
        {
            try { test.Test(); Console.WriteLine("PASS " + test.Name); }
            catch (Exception error) { failures++; Console.WriteLine("FAIL " + test.Name + ": " + error.GetType().Name + ": " + error.Message); }
        }
        return failures == 0 ? 0 : 1;
    }
}
