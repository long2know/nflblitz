using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Blitz.Contracts;

namespace Blitz.Presentation
{
    public sealed class SourceInstructionFontResource
    {
        public string Font { get; }
        public string Texture { get; }
        public SourceInstructionFontResource(string actualFont, string actualTexture)
        {
            if (string.IsNullOrEmpty(actualFont) || string.IsNullOrEmpty(actualTexture))
                throw new ArgumentException("Actual font and atlas identities are required.");
            Font = actualFont; Texture = actualTexture;
        }
    }

    public interface ISourceKickoffInstructionResources
    {
        SourceInstructionRaster ReadRaster();
        uint ResolveColor(SourceKickoffInstructionChildColor color);
        uint ResolveObjectId(string baseIdSymbol, int slotOffset);
        SourceInstructionImage ResolvePlate(string imageSymbol, string textureSymbol);
        IReadOnlyList<SourceInstructionGraphic> GeneratePlate(SourceInstructionImage image,
            SourceInstructionRaster raster, SourceInstructionPoint position, float zAngle, float hScale);
        SourceInstructionTextLayout FormatText(SourceKickoffInstructionChildTextRequest request,
            SourceKickoffInstructionChildFont font, uint color, SourceInstructionRaster raster,
            SourceInstructionPoint position, float textZIncrement);
    }

    // z_angle/h_scale are native sprite operands, not inferred width/height scales
    // or degrees. The binding supplies the native beginobj defaults and transform.
    public interface ISourceKickoffInstructionNativePlateLayout
    {
        IReadOnlyList<SourceInstructionVertex> Layout(SourceInstructionImage image, SourceInstructionRaster raster,
            SourceInstructionPoint position, float zAngle, float hScale);
    }

    public interface ISourceKickoffInstructionSoundPlayer
    {
        void Play(SourceKickoffInstructionChildSound sound);
    }

    public sealed class SourceKickoffInstructionFormatSlot
    {
        public SourceKickoffInstructionChildTextKind Kind { get; }
        public int Ordinal { get; }
        public int NativePrintLine { get; }
        public string OriginalFormat { get; }
        public string NativeSecondArgumentSymbols { get; }
        public int NativeSecondArgument { get; }
        public SourceKickoffInstructionFormatSlot(SourceKickoffInstructionChildTextKind kind, int ordinal,
            int nativePrintLine, string originalFormat, string nativeSecondArgumentSymbols, int nativeSecondArgument)
        {
            if (originalFormat == null || nativeSecondArgumentSymbols != "HJUST_CENTER|VJUST_CENTER")
                throw new SourceInstructionPresentationException(SourceInstructionPresentationError.UnsupportedNativeBinding,
                    "Supply the original child format and its actual native second-argument binding.");
            Kind = kind; Ordinal = ordinal; NativePrintLine = nativePrintLine; OriginalFormat = originalFormat;
            NativeSecondArgumentSymbols = nativeSecondArgumentSymbols; NativeSecondArgument = nativeSecondArgument;
        }
    }

    public sealed class SourceKickoffInstructionFormatCall
    {
        public SourceKickoffInstructionFormatSlot Slot { get; }
        public SourceKickoffInstructionChildTextRequest Request { get; }
        public SourceKickoffInstructionChildFont Font { get; }
        public uint Color { get; }
        public SourceInstructionRaster Raster { get; }
        public SourceInstructionPoint Position { get; }
        public float TextZIncrement { get; }
        public SourceKickoffInstructionFormatCall(SourceKickoffInstructionFormatSlot slot,
            SourceKickoffInstructionChildTextRequest request, SourceKickoffInstructionChildFont font, uint color,
            SourceInstructionRaster raster, SourceInstructionPoint position, float textZIncrement)
        {
            Slot = slot; Request = request; Font = font; Color = color; Raster = raster;
            Position = position; TextZIncrement = textZIncrement;
        }
    }

    public sealed class SourceKickoffInstructionTextRun
    {
        public string Text { get; }
        public SourceKickoffInstructionChildFont Font { get; }
        public uint Color { get; }
        public float OffsetX { get; }
        public float OffsetY { get; }
        public SourceInstructionJustification Justification { get; }
        public SourceKickoffInstructionTextRun(string text, SourceKickoffInstructionChildFont font, uint color,
            float offsetX, float offsetY, SourceInstructionJustification justification)
        {
            if (text == null || !SourceInstructionRaster.Finite(offsetX) || !SourceInstructionRaster.Finite(offsetY)
                || (justification != SourceInstructionJustification.Left && justification != SourceInstructionJustification.Center))
                throw new ArgumentException("Evaluated child text requires explicit style, offsets and alignment.");
            foreach (char c in text)
                if (char.IsControl(c)) throw new SourceInstructionPresentationException(
                    SourceInstructionPresentationError.UnsupportedNativeBinding,
                    "Native format/control sequences must be evaluated before glyph layout.");
            Text = text; Font = font; Color = color; OffsetX = offsetX; OffsetY = offsetY; Justification = justification;
        }
    }

    public sealed class SourceKickoffInstructionFormattedText
    {
        public uint StateColor { get; }
        public ReadOnlyCollection<SourceKickoffInstructionTextRun> Runs { get; }
        public SourceKickoffInstructionFormattedText(uint actualReturnedStringStateColor,
            IEnumerable<SourceKickoffInstructionTextRun> runs)
        {
            if (runs == null) throw new ArgumentNullException(nameof(runs));
            StateColor = actualReturnedStringStateColor;
            Runs = new List<SourceKickoffInstructionTextRun>(runs).AsReadOnly();
        }
    }

    public interface ISourceKickoffInstructionNativeTextFormatter
    {
        SourceKickoffInstructionFormattedText Evaluate(SourceKickoffInstructionFormatCall call);
    }

    public sealed class SourceKickoffInstructionHudBindings
    {
        private static readonly int[][] PrintLines = {
            new[] { 4583 }, new[] { 4591 }, new[] { 4596, 4598 }, new[] { 4601, 4603, 4605 },
            new[] { 4608, 4610 }, new[] { 4613, 4615, 4617 },
            new[] { 4627, 4629, 4631, 4633, 4635, 4637, 4639 }, new[] { 4642, 4644, 4646, 4648 },
            new[] { 4656, 4658, 4660, 4662, 4664, 4666, 4668 }, new[] { 4671, 4673, 4675 }
        };
        private readonly Dictionary<int, SourceKickoffInstructionFormatSlot> formats =
            new Dictionary<int, SourceKickoffInstructionFormatSlot>();
        private readonly Dictionary<SourceKickoffInstructionChildFont, SourceInstructionFontResource> fonts =
            new Dictionary<SourceKickoffInstructionChildFont, SourceInstructionFontResource>();
        private readonly Dictionary<SourceKickoffInstructionChildColor, uint> colors =
            new Dictionary<SourceKickoffInstructionChildColor, uint>();
        private readonly ISourceKickoffInstructionNativeTextFormatter formatter;
        private readonly uint objectIdBase;
        public uint PlateColor { get; }
        public ISourceKickoffInstructionNativePlateLayout PlateLayout { get; }

        public SourceKickoffInstructionHudBindings(IEnumerable<SourceKickoffInstructionFormatSlot> originalFormats,
            ISourceKickoffInstructionNativeTextFormatter nativeFormatter,
            IReadOnlyDictionary<SourceKickoffInstructionChildFont, SourceInstructionFontResource> actualFonts,
            IReadOnlyDictionary<SourceKickoffInstructionChildColor, uint> actualColors,
            ISourceKickoffInstructionNativePlateLayout nativePlateLayout, uint actualBeginObjectColor,
            uint actualIdiotBoxObjectId)
        {
            if (originalFormats == null || nativeFormatter == null || actualFonts == null || actualColors == null
                || nativePlateLayout == null)
                throw Missing("Actual child formats, native formatter/layout, fonts and colors are required.");
            if (actualIdiotBoxObjectId > uint.MaxValue - 3)
                throw new SourceInstructionPresentationException(SourceInstructionPresentationError.UnsupportedNativeBinding,
                    "The portable object-ID binding requires the four native slot additions without overflow.");
            formatter = nativeFormatter; PlateLayout = nativePlateLayout;
            PlateColor = actualBeginObjectColor; objectIdBase = actualIdiotBoxObjectId;
            foreach (SourceKickoffInstructionFormatSlot slot in originalFormats)
            {
                if (slot == null || ExpectedLine(slot.Kind, slot.Ordinal) < 0 || ExpectedLine(slot.Kind, slot.Ordinal) != slot.NativePrintLine
                    || formats.ContainsKey(slot.NativePrintLine))
                    throw Missing("Duplicate or incorrectly located original child print slot.");
                formats.Add(slot.NativePrintLine, slot);
            }
            foreach (int[] lines in PrintLines)
                foreach (int line in lines)
                    if (!formats.ContainsKey(line)) throw Missing("Missing original child format at print line " + line + ".");
            foreach (SourceKickoffInstructionChildFont font in Enum.GetValues(typeof(SourceKickoffInstructionChildFont)))
            {
                SourceInstructionFontResource value;
                if (!actualFonts.TryGetValue(font, out value) || value == null) throw Missing("Missing actual child font " + font + ".");
                fonts.Add(font, value);
            }
            foreach (SourceKickoffInstructionChildColor color in Enum.GetValues(typeof(SourceKickoffInstructionChildColor)))
            {
                uint value;
                if (!actualColors.TryGetValue(color, out value)) throw Missing("Missing actual child color " + color + ".");
                if (color == SourceKickoffInstructionChildColor.NativeZero && value != 0)
                    throw Missing("The source's literal zero color must not become a named-color substitution.");
                colors.Add(color, value);
            }
        }

        internal uint ResolveColor(SourceKickoffInstructionChildColor color)
        {
            uint value;
            if (!colors.TryGetValue(color, out value)) throw Missing("Unbound child color " + color + ".");
            return value;
        }
        internal SourceInstructionFontResource ResolveFont(SourceKickoffInstructionChildFont font)
        {
            SourceInstructionFontResource value;
            if (!fonts.TryGetValue(font, out value)) throw Missing("Unbound child font " + font + ".");
            return value;
        }
        internal uint ResolveObjectId(string symbol, int offset)
        {
            if (symbol != "OID_IDIOTBOX" || offset < 0 || offset > 3)
                throw Missing("The supplied native child object ID/slot is not bound.");
            return objectIdBase + (uint)offset;
        }
        internal SourceKickoffInstructionFormattedText Evaluate(SourceKickoffInstructionChildTextRequest request,
            SourceKickoffInstructionChildFont font, uint color, SourceInstructionRaster raster,
            SourceInstructionPoint position, float textZIncrement)
        {
            SourceKickoffInstructionFormatSlot slot;
            if (ExpectedLine(request.Kind, request.Ordinal) != request.NativePrintLine
                || !formats.TryGetValue(request.NativePrintLine, out slot)
                || slot.NativeSecondArgumentSymbols != request.NativeSecondArgumentSymbols
                || (request.Kind == SourceKickoffInstructionChildTextKind.Header
                    ? !request.DisplayPlayerNumber.HasValue || request.DisplayPlayerNumber < 1 || request.DisplayPlayerNumber > 4
                    : request.DisplayPlayerNumber.HasValue))
                throw Missing("The child request has no exact original native print/argument binding.");
            SourceKickoffInstructionFormattedText result = formatter.Evaluate(new SourceKickoffInstructionFormatCall(
                slot, request, font, color, raster, position, textZIncrement));
            if (result == null || result.Runs.Count == 0) throw Missing("The child formatter returned no evaluated string state/runs.");
            foreach (SourceKickoffInstructionTextRun run in result.Runs)
                if (run == null) throw Missing("The child formatter returned a null run.");
            return result;
        }
        private static int ExpectedLine(SourceKickoffInstructionChildTextKind kind, int ordinal)
        {
            int index = (int)kind;
            return index < 0 || index >= PrintLines.Length || ordinal < 1 || ordinal > PrintLines[index].Length
                ? -1 : PrintLines[index][ordinal - 1];
        }
        private static SourceInstructionPresentationException Missing(string message)
        { return new SourceInstructionPresentationException(SourceInstructionPresentationError.MissingResource, message); }
    }

    // One store and text-state machine serves every child in the runtime epoch.
    // Cleanup is global ID/mask matching, never ownership by invocation or by the
    // source's shared static beginobj pointer.
    public sealed class SourceKickoffInstructionPresentation : ISourceKickoffInstructionChildEffects, IDisposable
    {
        private abstract class Owned
        {
            internal bool Deleted;
            internal IReadOnlyList<SourceInstructionGraphic> Graphics;
        }
        private sealed class Plate : Owned, ISourceInstructionPlate
        {
            internal SourceKickoffInstructionPresentation Owner;
            internal long Generation;
            internal SourceInstructionImage Image;
            internal SourceInstructionRaster Raster;
            internal SourceInstructionPoint Position;
            internal float? ZAngle, HScale;
            internal uint? Id;
        }
        private sealed class Text : Owned
        {
            internal int Id;
        }
        private readonly ISourceKickoffInstructionResources resources;
        private readonly ISourceKickoffInstructionSoundPlayer sounds;
        private readonly List<Owned> objects = new List<Owned>();
        private SourceKickoffInstructionChildFont? font;
        private SourceKickoffInstructionChildColor? color;
        private int? stringId;
        private float? textZIncrement;
        private SourceInstructionRaster? textRaster;
        private SourceInstructionPoint? textPosition;
        private long generation;
        private bool disposed;
        public SourceInstructionPresentationException Failure { get; private set; }

        public SourceKickoffInstructionPresentation(ISourceKickoffInstructionResources resources,
            ISourceKickoffInstructionSoundPlayer sounds)
        {
            this.resources = resources ?? throw new ArgumentNullException(nameof(resources));
            this.sounds = sounds ?? throw new ArgumentNullException(nameof(sounds));
        }
        public void SetTextZIncrement(float value)
        {
            Run(() =>
            {
                if (value != 0) throw Error(SourceInstructionPresentationError.UnsupportedNativeBinding,
                    "Only the authored child's explicit zero text-depth increment is supported.");
                textZIncrement = value;
            });
        }
        public ISourceInstructionPlate BeginPlate(string imageSymbol, string textureSymbol, SourceInstructionPoint point)
        {
            return Run<ISourceInstructionPlate>(() =>
            {
                SourceInstructionImage image = resources.ResolvePlate(imageSymbol, textureSymbol);
                if (image == null) throw Error(SourceInstructionPresentationError.MissingResource, "beginobj requires an actual child plate image.");
                var plate = new Plate { Owner = this, Generation = generation, Image = image, Raster = ReadRaster(), Position = point };
                objects.Add(plate);
                return plate;
            });
        }
        public void SetPlateZAngle(ISourceInstructionPlate plate, float value)
        {
            Run(() =>
            {
                if (!SourceInstructionRaster.Finite(value)) throw Error(SourceInstructionPresentationError.InvalidGeometry, "Nonfinite native z_angle.");
                RequirePlate(plate).ZAngle = value;
            });
        }
        public void SetPlateHScale(ISourceInstructionPlate plate, float value)
        {
            Run(() =>
            {
                if (!SourceInstructionRaster.Finite(value) || value <= 0)
                    throw Error(SourceInstructionPresentationError.InvalidGeometry, "Invalid native h_scale.");
                RequirePlate(plate).HScale = value;
            });
        }
        public void SetPlateObjectId(ISourceInstructionPlate plate, string baseIdSymbol, int slotOffset)
        { Run(() => RequirePlate(plate).Id = resources.ResolveObjectId(baseIdSymbol, slotOffset)); }
        public void GeneratePlateVertices(ISourceInstructionPlate plate)
        {
            Run(() =>
            {
                Plate state = RequirePlate(plate);
                if (!state.ZAngle.HasValue || !state.HScale.HasValue || !state.Id.HasValue)
                    throw Error(SourceInstructionPresentationError.InvalidLifecycle, "Source z_angle/h_scale/object-ID writes must precede vertex generation.");
                state.Graphics = Graphics(resources.GeneratePlate(state.Image, state.Raster, state.Position,
                    state.ZAngle.Value, state.HScale.Value));
            });
        }
        public void SetTextFont(SourceKickoffInstructionChildFont value) { Run(() => { font = value; }); }
        public void SetStringId(int id) { Run(() => { stringId = id; }); }
        public void SetTextColor(SourceKickoffInstructionChildColor value)
        { Run(() => { resources.ResolveColor(value); color = value; }); }
        public void SetTextPosition(SourceInstructionPoint point)
        { Run(() => { textRaster = ReadRaster(); textPosition = point; }); }
        public void Print(SourceKickoffInstructionChildTextRequest request)
        {
            Run(() =>
            {
                if (!font.HasValue || !color.HasValue || !stringId.HasValue || !textZIncrement.HasValue
                    || !textRaster.HasValue || !textPosition.HasValue)
                    throw Error(SourceInstructionPresentationError.InvalidLifecycle, "All source child text-state writes must precede oprintf.");
                SourceInstructionTextLayout layout = resources.FormatText(request, font.Value, resources.ResolveColor(color.Value),
                    textRaster.Value, textPosition.Value, textZIncrement.Value);
                if (layout == null) throw Error(SourceInstructionPresentationError.MissingResource, "oprintf requires actual child string state and glyph geometry.");
                objects.Add(new Text { Id = stringId.Value, Graphics = Graphics(layout.Graphics) });
            });
        }
        public void PlaySound(SourceKickoffInstructionChildSound sound)
        {
            Run(() =>
            {
                if (sound == null) throw new ArgumentNullException(nameof(sound));
                sounds.Play(sound);
            });
        }
        public void DeleteStrings(int id, uint mask)
        {
            Run(() =>
            {
                foreach (Owned value in objects)
                {
                    var text = value as Text;
                    if (text != null && ((uint)text.Id & mask) == ((uint)id & mask)) text.Deleted = true;
                }
            });
        }
        public void DeletePlateClass(string baseIdSymbol, int slotOffset, uint mask)
        {
            Run(() =>
            {
                uint id = resources.ResolveObjectId(baseIdSymbol, slotOffset);
                foreach (Owned value in objects)
                {
                    var plate = value as Plate;
                    if (plate != null && plate.Id.HasValue && (plate.Id.Value & mask) == (id & mask)) plate.Deleted = true;
                }
            });
        }
        public IReadOnlyList<SourceInstructionGraphic> Frame()
        {
            RequireAlive();
            var frame = new List<SourceInstructionGraphic>();
            if (Failure == null)
                foreach (Owned value in objects)
                    if (!value.Deleted && value.Graphics != null) frame.AddRange(value.Graphics);
            return frame.AsReadOnly();
        }
        public void Reset()
        {
            RequireAlive(); Cleanup(); objects.Clear(); generation++;
            font = null; color = null; stringId = null; textZIncrement = null; textRaster = null; textPosition = null;
            Failure = null;
        }
        public void Abort(Exception error)
        {
            RequireAlive();
            Failure = error as SourceInstructionPresentationException
                ?? Error(SourceInstructionPresentationError.InvalidLifecycle, "The source kickoff presentation epoch stopped.", error);
            Cleanup();
        }
        public void Dispose()
        {
            if (disposed) return;
            Cleanup(); objects.Clear(); disposed = true;
        }
        private void Cleanup() { foreach (Owned value in objects) value.Deleted = true; }
        private Plate RequirePlate(ISourceInstructionPlate value)
        {
            var plate = value as Plate;
            if (plate == null || plate.Owner != this || plate.Generation != generation || plate.Deleted)
                throw Error(SourceInstructionPresentationError.InvalidHandle, "A live returned child plate from this presentation epoch is required.");
            return plate;
        }
        private SourceInstructionRaster ReadRaster()
        {
            SourceInstructionRaster raster = resources.ReadRaster();
            if (raster.Width <= 0 || raster.Height <= 0)
                throw Error(SourceInstructionPresentationError.MissingResource, "The actual child native raster is unavailable.");
            return raster;
        }
        private static IReadOnlyList<SourceInstructionGraphic> Graphics(IReadOnlyList<SourceInstructionGraphic> values)
        {
            if (values == null || values.Count == 0)
                throw Error(SourceInstructionPresentationError.MissingResource, "The actual child resource/layout returned no drawable graphics.");
            var copy = new List<SourceInstructionGraphic>(values);
            foreach (SourceInstructionGraphic value in copy)
                if (value == null) throw Error(SourceInstructionPresentationError.InvalidGeometry, "Null child graphic.");
            return copy.AsReadOnly();
        }
        private void RequireAlive()
        { if (disposed) throw new ObjectDisposedException(nameof(SourceKickoffInstructionPresentation)); }
        private void Run(Action action) { Run<object>(() => { action(); return null; }); }
        private T Run<T>(Func<T> action)
        {
            RequireAlive();
            if (Failure != null) throw Error(SourceInstructionPresentationError.InvalidLifecycle, "Reset the failed source kickoff epoch before reuse.");
            try { return action(); }
            catch (Exception error)
            {
                Failure = error as SourceInstructionPresentationException
                    ?? Error(SourceInstructionPresentationError.UnsupportedNativeBinding, "Child presentation binding failed; no substitute was created.", error);
                Cleanup(); throw Failure;
            }
        }
        private static SourceInstructionPresentationException Error(SourceInstructionPresentationError code, string message,
            Exception inner = null)
        { return new SourceInstructionPresentationException(code, message, inner); }
    }
}
