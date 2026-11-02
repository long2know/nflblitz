using System;
using System.Collections.Generic;
using Blitz.Contracts;
using Blitz.Gameplay;
using Blitz.Presentation;
using Blitz.Runtime;

namespace Blitz.SourceKickoffPresentationTests
{
    internal static class Program
    {
        private static readonly SourceInstructionProcessIds Ids = new SourceInstructionProcessIds(0x71, 0x72, 0x73, 0x74);
        private const int ChildId = 0x6b42;
        private const uint A = 0x80, B = 0x20, C = 0x02, White = 0x400, PlateColor = 0xff123456;
        private static int assertions, cases;
        private static void Check(bool value, string why)
        { assertions++; if (!value) throw new InvalidOperationException(why); }
        private static void Equal<T>(T expected, T actual, string why)
        { Check(EqualityComparer<T>.Default.Equals(expected, actual), why + ": expected " + expected + ", got " + actual); }
        private static T Throws<T>(Action action, string why) where T : Exception
        {
            assertions++;
            try { action(); }
            catch (T error) { return error; }
            throw new InvalidOperationException(why + ": expected " + typeof(T).Name);
        }
        private static void Error(SourceInstructionPresentationError expected, Action action, string why)
        { Equal(expected, Throws<SourceInstructionPresentationException>(action, why).Error, why + " error category"); }

        private static readonly int[][] ChildLines = {
            new[] { 4583 }, new[] { 4591 }, new[] { 4596, 4598 }, new[] { 4601, 4603, 4605 },
            new[] { 4608, 4610 }, new[] { 4613, 4615, 4617 },
            new[] { 4627, 4629, 4631, 4633, 4635, 4637, 4639 }, new[] { 4642, 4644, 4646, 4648 },
            new[] { 4656, 4658, 4660, 4662, 4664, 4666, 4668 }, new[] { 4671, 4673, 4675 }
        };
        private static List<SourceKickoffInstructionFormatSlot> Formats()
        {
            var slots = new List<SourceKickoffInstructionFormatSlot>();
            for (int kind = 0; kind < ChildLines.Length; kind++)
                for (int ordinal = 1; ordinal <= ChildLines[kind].Length; ordinal++)
                    slots.Add(new SourceKickoffInstructionFormatSlot((SourceKickoffInstructionChildTextKind)kind,
                        ordinal, ChildLines[kind][ordinal - 1], "SYNTHETIC\u001bFORMAT",
                        "HJUST_CENTER|VJUST_CENTER", -0x1357));
            return slots;
        }
        private static Dictionary<SourceKickoffInstructionChildFont, SourceInstructionFontResource> Fonts()
        {
            return new Dictionary<SourceKickoffInstructionChildFont, SourceInstructionFontResource> {
                { SourceKickoffInstructionChildFont.Bast10, new SourceInstructionFontResource("fixture/bast10", "fixture/font10") },
                { SourceKickoffInstructionChildFont.Bast8T, new SourceInstructionFontResource("fixture/bast8t", "fixture/font8") } };
        }
        private static Dictionary<SourceKickoffInstructionChildColor, uint> Colors()
        {
            var colors = new Dictionary<SourceKickoffInstructionChildColor, uint>();
            foreach (SourceKickoffInstructionChildColor color in Enum.GetValues(typeof(SourceKickoffInstructionChildColor)))
                colors.Add(color, color == SourceKickoffInstructionChildColor.NativeZero ? 0 : 0xff000000u | (uint)color);
            return colors;
        }
        private static SourceInstructionGraphic Graphic(string name, SourceInstructionPoint point, uint color)
        {
            return new SourceInstructionGraphic("fixture/atlas", "fixture/white", name, color, point.Depth, new[] {
                new SourceInstructionVertex(point.X, point.Y, 0, 0), new SourceInstructionVertex(point.X + 3, point.Y, 1, 0),
                new SourceInstructionVertex(point.X, point.Y + 7, 0, 1) });
        }

        // Synthetic resource/primitive DATA only. No Game, Unity asset, native
        // formatter or native geometry implementation is constructed or executed.
        private sealed class Resources : ISourceKickoffInstructionResources,
            ISourceKickoffInstructionNativeTextFormatter, ISourceKickoffInstructionNativePlateLayout
        {
            internal readonly SourceKickoffInstructionHudBindings Bindings;
            internal readonly List<SourceKickoffInstructionFormatCall> Calls = new List<SourceKickoffInstructionFormatCall>();
            internal readonly List<SourceInstructionPoint> PlatePoints = new List<SourceInstructionPoint>();
            internal bool MissingPlate, MissingText, EmptyGeometry;
            internal int Reads;
            internal Resources()
            { Bindings = new SourceKickoffInstructionHudBindings(Formats(), this, Fonts(), Colors(), this, PlateColor, 0x3300); }
            public SourceInstructionRaster ReadRaster()
            { Reads++; return new SourceInstructionRaster(513, 259, 256.75f, 128.75f); }
            public uint ResolveColor(SourceKickoffInstructionChildColor color) { return Bindings.ResolveColor(color); }
            public uint ResolveObjectId(string symbol, int offset) { return Bindings.ResolveObjectId(symbol, offset); }
            public SourceInstructionImage ResolvePlate(string image, string texture)
            {
                Equal("playresult", image, "authored child image"); Equal("SCR_PLATE_TID", texture, "authored texture symbol");
                return MissingPlate ? null : new SourceInstructionImage(image, "fixture/atlas", "fixture/white", 212, 79, 106, 39, 0, 0, 1, 1);
            }
            public IReadOnlyList<SourceInstructionVertex> Layout(SourceInstructionImage image, SourceInstructionRaster raster,
                SourceInstructionPoint point, float zAngle, float hScale)
            {
                Equal(256.04f, zAngle, "z_angle stays a separate native operand");
                Equal(1.45f, hScale, "h_scale is not inferred width/height scaling");
                Equal(513, raster.Width, "actual resource raster retained"); Equal(212, image.Width, "actual image metrics retained");
                PlatePoints.Add(point);
                return Graphic("plate", point, PlateColor).Vertices;
            }
            public IReadOnlyList<SourceInstructionGraphic> GeneratePlate(SourceInstructionImage image, SourceInstructionRaster raster,
                SourceInstructionPoint point, float zAngle, float hScale)
            {
                IReadOnlyList<SourceInstructionVertex> vertices = Layout(image, raster, point, zAngle, hScale);
                return EmptyGeometry ? new SourceInstructionGraphic[0] : new[] {
                    new SourceInstructionGraphic(image.Texture, image.WhiteTexture, "plate", PlateColor, point.Depth, vertices) };
            }
            public SourceKickoffInstructionFormattedText Evaluate(SourceKickoffInstructionFormatCall call)
            {
                Calls.Add(call);
                return new SourceKickoffInstructionFormattedText(0xffaa9988, new[] {
                    new SourceKickoffInstructionTextRun("SYNTHETIC", call.Font, call.Color ^ 0x00010000, 0, 0,
                        SourceInstructionJustification.Center) });
            }
            public SourceInstructionTextLayout FormatText(SourceKickoffInstructionChildTextRequest request,
                SourceKickoffInstructionChildFont font, uint color, SourceInstructionRaster raster,
                SourceInstructionPoint point, float zIncrement)
            {
                if (MissingText) return null;
                SourceKickoffInstructionFormattedText text = Bindings.Evaluate(request, font, color, raster, point, zIncrement);
                return new SourceInstructionTextLayout(text.StateColor, new[] {
                    Graphic("text/" + request.NativePrintLine, point, text.Runs[0].Color) });
            }
        }

        private sealed class Services : ISourceInstructionAdmissionState, ISourceInstructionInputs,
            ISourceKickoffInstructionChildInputs, ISourceKickoffInstructionPlayerButtons,
            ISourceKickoffCurrentSwitches, ISourceKickoffPlayerObjects, ISourceKickoffPlayerObject,
            ISourceKickoffInstructionChildReturnBinding, ISourceKickoffInstructionFlashReturnBinding,
            ISourceKickoffInstructionSoundPlayer, ISourceInstructionResources,
            ISourceInstructionObjectFlashBinding, ISourceInstructionSoundPlayer
        {
            internal int Status = 1, Shown, Returns, FlashReturns, Sounds, PageReads, PagePrints, SwitchReads, FlagWrites;
            internal uint Flags = 8;
            internal readonly List<string> Suffix = new List<string>();
            internal SourceKickoffInstructionPresentation Presentation;
            internal readonly int[] Current = new int[4], Old = new int[4];
            internal SourceKickoffInstructionOwnedHelpers Helpers()
            { return new SourceKickoffInstructionOwnedHelpers(A, B, C, White, this, this, this); }
            public int ReadIdiotBoxesShown() { return Shown; }
            public void WriteIdiotBoxesShown(int value) { Shown = value; }
            public int ReadPlayerStatus() { return Status; }
            public int ReadGamesPlayed(int slot) { return 0; }
            public int ReadPlayerSwitches() { return 4; }
            public int ReadShowPlyselIdiotBox() { return 0; }
            public int ReadTsec() { return 7; }
            public int ReadIsLowResolution() { return 1; }
            public int ReadFourPlayerVersion() { return 0; }
            public int ReadPlayerControl(int slot) { return 3 - slot; }
            public int ReadBallCarrier() { return 3; }
            public int ReadPlayerTeam(int control) { return 1; }
            public int ReadOffensiveSide() { return 0; }
            public int ReadCurrentButtons(int player) { return Current[player]; }
            public void WriteCurrentButtons(int player, int value) { Current[player] = value; }
            public int ReadOldButtons(int player) { return Old[player]; }
            public void WriteOldButtons(int player, int value) { Old[player] = value; }
            public uint ReadCurrentSwitchWord() { SwitchReads++; return A | (A << 8); }
            public ISourceKickoffPlayerObject ReadPlayerBlock(int number) { Equal(0, number, "flash captures original slot"); return this; }
            public uint ReadFlags() { return Flags; }
            public void WriteFlags(uint value) { Flags = value; FlagWrites++; }
            public SourceKickoffInstructionChildReturnAction ReturnFromSourceBody(
                SourceKickoffInstructionChildInvocation invocation, SourceKickoffInstructionChildReturnReason reason)
            {
                Equal(SourceKickoffInstructionChildReturnReason.NormalBodyEnd, reason, "source child body return policy");
                if (Presentation != null) Equal(0, Presentation.Frame().Count, "ID cleanup precedes native-return policy");
                Suffix.Add("return"); Returns++; return SourceKickoffInstructionChildReturnAction.ExternallyRetire;
            }
            public SourceKickoffInstructionFlashReturnAction ReturnFromSourceBody(SourceKickoffInstructionFlashInvocation invocation)
            { FlashReturns++; return SourceKickoffInstructionFlashReturnAction.ExternallyRetire; }
            public void Play(SourceKickoffInstructionChildSound sound)
            {
                Check(ReferenceEquals(SourceKickoffInstructionChildSound.Ricochet, sound), "child sound not replaced by four-page sound");
                Equal("VOLUME1", sound.VolumeSymbol, "child volume symbol"); Equal("PRIORITY_LVL4", sound.PrioritySymbol, "child priority symbol");
                if (Presentation != null) Check(Presentation.Frame().Count > 0, "sound attempt precedes string/plate deletion");
                Suffix.Add("sound"); Sounds++;
            }
            public SourceInstructionRaster ReadRaster()
            { PageReads++; return new SourceInstructionRaster(513, 259, 256.75f, 128.75f); }
            public uint ResolveColor(SourceInstructionColor color) { return (uint)color + 1; }
            public SourceInstructionImage ResolvePlate(string image, string texture)
            { return new SourceInstructionImage(image, "fixture/atlas", "fixture/white", 212, 79, 106, 39, 0, 0, 1, 1); }
            public IReadOnlyList<SourceInstructionGraphic> GeneratePlate(SourceInstructionImage image, SourceInstructionRaster raster,
                SourceInstructionPoint point, float width, float height) { return new[] { Graphic("page/plate", point, 1) }; }
            public SourceInstructionTextLayout FormatText(SourceInstructionTextRequest request, SourceInstructionFont font,
                uint color, SourceInstructionRaster raster, SourceInstructionPoint point)
            { PagePrints++; return new SourceInstructionTextLayout(color, new[] { Graphic("page/text", point, color) }); }
            public void Play(SourceInstructionSound sound, SourceInstructionSoundContext context) { }
            public ISourceInstructionObjectFlashProgram Create(ISourceInstructionPlate plate, int flashes, int delay, int fourth)
            { return new ExternalPlateFlash(); }
            private sealed class ExternalPlateFlash : ISourceInstructionObjectFlashProgram
            {
                public SourceInstructionPlateFlashStep Advance() { return new SourceInstructionPlateFlashStep(false, false, true, 0); }
            }
        }
        private sealed class ProgramHost : ISourceInstructionAdmissionProgramBinding
        {
            internal readonly SourcePortableProcessHost Host = new SourcePortableProcessHost();
            internal readonly SourceInstructionDispatcher Dispatcher;
            internal SourceInstructionAdmissionBinding Binding;
            private long next = 100;
            internal ProgramHost()
            {
                Dispatcher = new SourceInstructionDispatcher(Host);
                Host.Create(1, Ids.PostPlayer, "last_player_proc", 0); Host.Create(2, Ids.Camera, "cambot_proc", 0);
                Host.Create(3, 0x75, "game_proc", 0); Host.Create(4, 0x76, "player_proc", 0);
            }
            public SourceInstructionSnapshot Current { get { return Dispatcher.LastSnapshot; } }
            public IReadOnlyList<SourcePossessionProcessRecord> Records { get { return Host.Records; } }
            public void BindSourceInstructionAdmission(SourceInstructionAdmissionBinding binding)
            { Dispatcher.InitializeAdmission(binding, () => checked(next + 1), value => next = value); Binding = binding; }
            public long Invoke(SourceInstructionInvocation invocation)
            { long serial = checked(++next); Dispatcher.Admit(serial, invocation); return serial; }
            internal void AdmitKickoff(SourceInstructionConsumer consumer)
            {
                Host.BeginTick(Host.SourceTick + 1); Check(Host.Visit(4), "real portable producer wakes");
                try
                {
                    long? serial = Dispatcher.AdmitKickoffFromCatch(Binding, true, 4, () => checked(next + 1));
                    Check(serial.HasValue, "real portable producer creates kickoff"); next = serial.Value;
                }
                finally { Host.Complete(); }
                consumer.AfterSourceStep(Host.SourceTick);
            }
            internal void Step(SourceInstructionConsumer consumer)
            {
                Host.BeginTick(Host.SourceTick + 1);
                foreach (SourceProcessView process in Host.Processes)
                {
                    if (Dispatcher.Dispatch(process.Serial, name => { })) continue;
                    if (!Host.Visit(process.Serial)) continue;
                    try { Host.Sleep(1); } finally { Host.Complete(); }
                }
                consumer.AfterSourceStep(Host.SourceTick);
            }
        }
        private static readonly SourceKickoffInstructionChildOperands Operands =
            new SourceKickoffInstructionChildOperands(false, 101, 203, 0, 1);
        private static void Bind(SourceInstructionConsumer consumer, Services services, ISourceKickoffInstructionChildEffects effects)
        { consumer.BindSourceKickoffInstructionOwnedHelpers(4, Ids, services, services, ChildId, 4,
            0x1000, 0x2000, services, services, Operands, effects, services.Helpers(), services); }
        private static void Until(ProgramHost host, SourceInstructionConsumer consumer, Func<bool> done, int limit)
        {
            for (int i = 0; i < limit && !done(); i++) host.Step(consumer);
            Check(done(), "bounded source lifecycle reached its expected state");
        }

        private static void AuthoredChildProducesGraphicsAndCleansUp()
        {
            var data = new Resources(); var services = new Services(); var host = new ProgramHost();
            using (var model = new SourceKickoffInstructionPresentation(data, services))
            using (var consumer = new SourceInstructionConsumer(host, services, services, services))
            {
                services.Presentation = model; Bind(consumer, services, model);
                Equal(0, data.Reads, "binding does not dispatch the child"); host.AdmitKickoff(consumer);
                host.Step(consumer); Equal(0, model.Frame().Count, "child remains deferred");
                host.Step(consumer);
                Check(!consumer.HasInvocation, "kickoff graphics must not require a four-page invocation");
                Equal(13, model.Frame().Count, "authored carrier plate and twelve real presentation text objects");
                Equal(0, consumer.Presentation.Frame().Count, "no replacement four-page tutorial");
                Equal(0, services.PageReads, "child consumes only explicit child resource data");
                Equal(101f, data.PlatePoints[0].X, "bound BARX1"); Equal(150f, data.PlatePoints[0].Y, "authored y");
                Equal(1.4f, model.Frame()[0].Depth, "plate depth"); Equal(PlateColor, model.Frame()[0].Color, "explicit beginobj color");
                int[] expected = { 4583, 4591, 4596, 4598, 4601, 4603, 4605, 4608, 4610, 4613, 4615, 4617 };
                Equal(expected.Length, data.Calls.Count, "carrier request count");
                for (int i = 0; i < expected.Length; i++)
                {
                    SourceKickoffInstructionFormatCall call = data.Calls[i];
                    Equal(expected[i], call.Request.NativePrintLine, "authored ordered print line");
                    Equal(expected[i], call.Slot.NativePrintLine, "exact format slot"); Equal(-0x1357, call.Slot.NativeSecondArgument, "raw native format operand");
                    Equal("SYNTHETIC\u001bFORMAT", call.Slot.OriginalFormat, "original format passed to native primitive, not drawn raw");
                    Equal(0f, call.TextZIncrement, "shared authored zero text-depth increment"); Equal(1f, call.Position.Depth, "text depth");
                }
                Equal<int?>(1, data.Calls[0].Request.DisplayPlayerNumber, "header variadic player number");
                Equal(96f, data.Calls[0].Position.X, "source header x offset"); Equal(240f, data.Calls[0].Position.Y, "source header y offset");
                Equal(191f, data.Calls[3].Position.Y, "source live low-resolution speed offset");
                Equal(SourceKickoffInstructionChildFont.Bast10, data.Calls[1].Font, "status font");
                Equal(SourceKickoffInstructionChildFont.Bast8T, data.Calls[2].Font, "advice uses distinct authentic font slot");
                Equal(data.Calls[0].Color ^ 0x00010000, model.Frame()[1].Color, "per-character color is not returned-state or selected color");
                host.Step(consumer); Check((services.Flags & White) != 0, "owned flash changes player flags");
                Equal(PlateColor, model.Frame()[0].Color, "player flag flash must not become a plate/text flash");
                services.Flags |= 0x10000;
                Until(host, consumer, () => services.Returns == 1, 60);
                Equal(0, model.Frame().Count, "source full-ID cleanup removes actual child graphics");
                Equal("sound,return", string.Join(",", services.Suffix), "sound before deletion before explicit return");
                Equal(40, services.SwitchReads, "actual owned held-current/minimum wait");
                Equal(2, services.FlagWrites, "actual separate owned flash cycle"); Equal(0x10008u, services.Flags, "unrelated live flags survive");
                Equal(1, services.FlashReturns, "explicit flash return policy"); Check(model.Failure == null, "normal cleanup is not a failure");
            }
        }

        private sealed class Wait : ISourceKickoffActivePlayerWait
        {
            internal bool Done;
            public SourceKickoffActivePlayerWaitStep Advance()
            { return Done ? SourceKickoffActivePlayerWaitStep.Returned() : SourceKickoffActivePlayerWaitStep.Sleep(1); }
        }
        private sealed class Waits : ISourceKickoffActivePlayerWaitFactory
        {
            internal readonly Queue<Wait> Pending = new Queue<Wait>();
            public ISourceKickoffActivePlayerWait Create(int min, int timeout, int slot)
            { Equal(40, min, "source wait minimum"); Equal(42, timeout, "source wait timeout"); return Pending.Dequeue(); }
        }
        private sealed class Flashes : ISourceKickoffFlashContinuationFactory
        {
            public ISourceKickoffInstructionChildContinuation Create(SourceKickoffInstructionFlashInvocation invocation)
            { throw new InvalidOperationException("Direct child tests only record the creation primitive."); }
        }
        private static SourceKickoffInstructionChildProcess Child(int slot, Services services,
            SourceKickoffInstructionPresentation model, Waits waits)
        {
            return new SourceKickoffInstructionChildProcess(new SourceKickoffInstructionChildInvocation(100 + slot, ChildId, slot, 0, 0, 0),
                services, services, new SourceKickoffInstructionChildBinding(Operands, model, waits, new Flashes(), services),
                number => Equal(slot, number, "source flash creation uses original slot"));
        }
        private static void SharedIdsAreNotPerChildHandles()
        {
            var data = new Resources(); var services = new Services { Status = 3 }; var waits = new Waits();
            var first = new Wait(); var duplicate = new Wait(); var other = new Wait();
            waits.Pending.Enqueue(first); waits.Pending.Enqueue(duplicate); waits.Pending.Enqueue(other);
            using (var model = new SourceKickoffInstructionPresentation(data, services))
            {
                SourceKickoffInstructionChildProcess a = Child(0, services, model, waits);
                SourceKickoffInstructionChildProcess b = Child(0, services, model, waits);
                SourceKickoffInstructionChildProcess c = Child(1, services, model, waits);
                a.Advance(); b.Advance(); c.Advance();
                Equal(38, model.Frame().Count, "two same-ID carrier objects and distinct-ID defense objects coexist");
                model.DeleteStrings(0x34, uint.MaxValue);
                Equal(14, model.Frame().Count, "global string-ID deletion preserves every plate and the other slot's strings");
                Equal("plate", model.Frame()[0].ImageSymbol, "string deletion is not plate deletion");
                Equal("plate", model.Frame()[1].ImageSymbol, "duplicate-slot plate is not a string");
                first.Done = true; a.Advance();
                Equal(12, model.Frame().Count, "first child's ID cleanup also deletes the second child's matching objects");
                Equal(203d, model.Frame()[0].Vertices[0].X, "different slot's plate survives shared-pointer replacement");
                Equal(0, services.Returns, "presentation cannot execute the native-return policy itself");
                duplicate.Done = true; b.Advance(); Equal(12, model.Frame().Count, "repeat empty matching-class deletion is valid");
                other.Done = true; c.Advance(); Equal(0, model.Frame().Count, "remaining slot cleanup");
                ISourceInstructionPlate p = model.BeginPlate("playresult", "SCR_PLATE_TID", new SourceInstructionPoint(4, 5, 1.4f));
                model.SetPlateZAngle(p, 256.04f); model.SetPlateHScale(p, 1.45f);
                model.SetPlateObjectId(p, "OID_IDIOTBOX", 3); model.GeneratePlateVertices(p);
                model.DeletePlateClass("OID_IDIOTBOX", 0, 0xfffffffcu);
                Equal(0, model.Frame().Count, "plate class mask uses explicitly bound numeric IDs, not slot isolation");
                model.Reset();
                Error(SourceInstructionPresentationError.InvalidHandle, () => model.SetPlateHScale(p, 1.45f), "stale epoch handle");
                Equal(0, model.Frame().Count, "invalid handle fails closed");
            }
        }

        private sealed class ExternalEffects : ISourceKickoffInstructionChildEffects
        {
            private sealed class Plate : ISourceInstructionPlate { }
            internal int Plates, Texts, Deletes;
            public void SetTextZIncrement(float value) { }
            public ISourceInstructionPlate BeginPlate(string image, string texture, SourceInstructionPoint point) { Plates++; return new Plate(); }
            public void SetPlateZAngle(ISourceInstructionPlate plate, float value) { }
            public void SetPlateHScale(ISourceInstructionPlate plate, float value) { }
            public void SetPlateObjectId(ISourceInstructionPlate plate, string symbol, int slot) { }
            public void GeneratePlateVertices(ISourceInstructionPlate plate) { }
            public void SetTextFont(SourceKickoffInstructionChildFont font) { }
            public void SetStringId(int id) { }
            public void SetTextColor(SourceKickoffInstructionChildColor color) { }
            public void SetTextPosition(SourceInstructionPoint point) { }
            public void Print(SourceKickoffInstructionChildTextRequest request) { Texts++; }
            public void PlaySound(SourceKickoffInstructionChildSound sound) { }
            public void DeleteStrings(int id, uint mask) { Deletes++; }
            public void DeletePlateClass(string symbol, int slot, uint mask) { Deletes++; }
        }
        private static void UnboundManualAndExternalPathsStaySeparate()
        {
            var services = new Services(); var host = new ProgramHost();
            using (var consumer = new SourceInstructionConsumer(host, services, services, services))
            {
                for (int i = 0; i < 4; i++) host.Step(consumer);
                Check(!consumer.HasSourceAdmission && !consumer.HasInvocation && host.Binding == null, "default cannot auto-enable admission");
                Equal(0, services.PageReads, "unbound path consumes no resources");
                consumer.Invoke(4, Ids, services); host.Step(consumer); host.Step(consumer); host.Step(consumer);
                Equal(8, consumer.Presentation.Frame().Count, "manual existing page pipeline still draws its original eight objects");
                Until(host, consumer, () => consumer.Program.Stage == SourceInstructionStage.Fallthrough, 130);
                Equal(28, services.PagePrints, "manual four-page requests unchanged"); Equal(0, services.Sounds, "manual cannot select child sound");
                Equal(0, services.SwitchReads, "manual cannot select owned child wait");
            }
            services = new Services(); host = new ProgramHost(); var effects = new ExternalEffects();
            using (var consumer = new SourceInstructionConsumer(host, services, services, services))
            {
                Bind(consumer, services, effects); host.AdmitKickoff(consumer); host.Step(consumer); host.Step(consumer);
                Equal(1, effects.Plates, "explicit legacy external-effects binding remains the child owner");
                Equal(12, effects.Texts, "legacy external-effects receives authored text requests");
                Equal(0, consumer.Presentation.Frame().Count, "external child not moved into the manual pipeline");
                Until(host, consumer, () => services.Returns == 1, 60);
                Equal(2, effects.Deletes, "legacy source cleanup still reaches external primitives");
            }
        }

        private static void InvalidResourcesResetAndDisposeFailClosed()
        {
            var data = new Resources(); var formats = Formats(); formats.RemoveAt(0);
            Error(SourceInstructionPresentationError.MissingResource, () => new SourceKickoffInstructionHudBindings(
                formats, data, Fonts(), Colors(), data, PlateColor, 0x3300), "all 33 original formats are mandatory");
            formats = Formats(); formats.Add(formats[0]);
            Error(SourceInstructionPresentationError.MissingResource, () => new SourceKickoffInstructionHudBindings(
                formats, data, Fonts(), Colors(), data, PlateColor, 0x3300), "duplicate formats");
            var fonts = Fonts(); fonts.Remove(SourceKickoffInstructionChildFont.Bast8T);
            Error(SourceInstructionPresentationError.MissingResource, () => new SourceKickoffInstructionHudBindings(
                Formats(), data, fonts, Colors(), data, PlateColor, 0x3300), "no Bast8T font substitution");
            var colors = Colors(); colors[SourceKickoffInstructionChildColor.NativeZero] = 1;
            Error(SourceInstructionPresentationError.MissingResource, () => new SourceKickoffInstructionHudBindings(
                Formats(), data, Fonts(), colors, data, PlateColor, 0x3300), "source literal-zero color");
            Error(SourceInstructionPresentationError.MissingResource, () => new SourceKickoffInstructionHudBindings(
                Formats(), data, Fonts(), Colors(), null, PlateColor, 0x3300), "native child layout is not guessed");
            Error(SourceInstructionPresentationError.MissingResource, () => new SourceKickoffInstructionHudBindings(
                Formats(), null, Fonts(), Colors(), data, PlateColor, 0x3300), "native child formatting is not guessed");
            var services = new Services(); var waits = new Waits(); waits.Pending.Enqueue(new Wait());
            var model = new SourceKickoffInstructionPresentation(data, services);
            data.MissingPlate = true;
            Error(SourceInstructionPresentationError.MissingResource, () => Child(0, services, model, waits).Advance(), "missing actual plate");
            Equal(0, model.Frame().Count, "no placeholder/tutorial on missing plate");
            Error(SourceInstructionPresentationError.InvalidLifecycle, () => model.SetStringId(0x34), "faults cannot continue");
            model.Reset(); data.MissingPlate = false; data.MissingText = true;
            Error(SourceInstructionPresentationError.MissingResource, () => Child(0, services, model, waits).Advance(), "missing original text glyphs");
            Equal(0, model.Frame().Count, "text failure also cleans the already-created plate");
            model.Reset(); data.MissingText = false; data.EmptyGeometry = true;
            Error(SourceInstructionPresentationError.MissingResource, () => Child(0, services, model, waits).Advance(), "empty native layout");
            Equal(0, model.Frame().Count, "empty geometry is not success");
            model.Reset(); data.EmptyGeometry = false;
            Child(0, services, model, waits).Advance(); Equal(13, model.Frame().Count, "fresh reset restores only explicit resource-backed use");
            model.Abort(new InvalidOperationException("synthetic actual source failure"));
            Equal(0, model.Frame().Count, "runtime abort removes every child object");
            model.Reset();
            Error(SourceInstructionPresentationError.InvalidLifecycle, () => model.Print(
                new SourceKickoffInstructionChildTextRequest(SourceKickoffInstructionChildTextKind.Header, 1, 4583, 1)),
                "reset removes global text-state writes, not just the frame");
            model.Dispose(); model.Dispose();
            Throws<ObjectDisposedException>(() => model.Frame(), "disposed frame cannot revive resources");
        }

        private static void Case(string name, Action action)
        { int before = assertions; action(); cases++; Console.WriteLine("PASS " + name + " assertions=" + (assertions - before)); }
        private static int Main()
        {
            try
            {
                Case("authored-child-graphics-cleanup", AuthoredChildProducesGraphicsAndCleansUp);
                Case("shared-global-child-ids", SharedIdsAreNotPerChildHandles);
                Case("unbound-manual-external-separation", UnboundManualAndExternalPathsStaySeparate);
                Case("missing-bindings-reset-dispose", InvalidResourcesResetAndDisposeFailClosed);
                Console.WriteLine("SUMMARY component_cases=" + cases + " assertions=" + assertions + " failures=0 Game=0 Unity=0 native=0");
                return 0;
            }
            catch (Exception error) { Console.WriteLine("FAIL " + error); return 1; }
        }
    }
}
