using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Blitz.Contracts
{
    public enum SourceInstructionPresentationError
    {
        MissingResource, UnsupportedNativeBinding, InvalidHandle, InvalidLifecycle, InvalidGeometry
    }

    public sealed class SourceInstructionPresentationException : InvalidOperationException
    {
        public SourceInstructionPresentationError Error { get; }
        public SourceInstructionPresentationException(SourceInstructionPresentationError error, string message,
            Exception inner = null) : base(message, inner) { Error = error; }
    }

    public readonly struct SourceInstructionRaster
    {
        public int Width { get; }
        public int Height { get; }
        public float HalfWidth { get; }
        public float HalfHeight { get; }
        public SourceInstructionRaster(int width, int height, float nativeHalfWidth, float nativeHalfHeight)
        {
            if (width <= 0 || height <= 0 || !Finite(nativeHalfWidth) || !Finite(nativeHalfHeight))
                throw new ArgumentOutOfRangeException(nameof(width), "Supply actual raster and native half-expression values.");
            Width = width; Height = height; HalfWidth = nativeHalfWidth; HalfHeight = nativeHalfHeight;
        }
        internal static bool Finite(float value) { return !float.IsNaN(value) && !float.IsInfinity(value); }
    }

    public readonly struct SourceInstructionPoint
    {
        public float X { get; }
        public float Y { get; }
        public float Depth { get; }
        public SourceInstructionPoint(float x, float y, float depth)
        {
            if (!SourceInstructionRaster.Finite(x) || !SourceInstructionRaster.Finite(y)
                || !SourceInstructionRaster.Finite(depth)) throw new ArgumentOutOfRangeException(nameof(x));
            X = x; Y = y; Depth = depth;
        }
    }

    public readonly struct SourceInstructionRectangle
    {
        public double Left { get; }
        public double Top { get; }
        public double Width { get; }
        public double Height { get; }
        public SourceInstructionRectangle(double left, double top, double width, double height)
        {
            if (!Finite(left) || !Finite(top) || !Finite(width) || !Finite(height) || width <= 0 || height <= 0)
                throw new ArgumentOutOfRangeException(nameof(width));
            Left = left; Top = top; Width = width; Height = height;
        }
        internal static bool Finite(double value) { return !double.IsNaN(value) && !double.IsInfinity(value); }
    }

    public sealed class SourceInstructionImage
    {
        public string Symbol { get; }
        public string Texture { get; }
        public string WhiteTexture { get; }
        public int Width { get; }
        public int Height { get; }
        public double AnchorX { get; }
        public double AnchorY { get; }
        public double U0 { get; }
        public double V0 { get; }
        public double U1 { get; }
        public double V1 { get; }
        public SourceInstructionImage(string symbol, string texture, string whiteTexture, int width, int height,
            double anchorX, double anchorY, double u0, double v0, double u1, double v1)
        {
            if (string.IsNullOrEmpty(symbol) || string.IsNullOrEmpty(texture) || string.IsNullOrEmpty(whiteTexture)
                || width <= 0 || height <= 0 || !SourceInstructionRectangle.Finite(anchorX)
                || !SourceInstructionRectangle.Finite(anchorY) || !SourceInstructionRectangle.Finite(u0)
                || !SourceInstructionRectangle.Finite(v0) || !SourceInstructionRectangle.Finite(u1)
                || !SourceInstructionRectangle.Finite(v1))
                throw new ArgumentException("Actual image, atlas, white/chroma texture, anchors and UVs are required.");
            Symbol = symbol; Texture = texture; WhiteTexture = whiteTexture; Width = width; Height = height;
            AnchorX = anchorX; AnchorY = anchorY; U0 = u0; V0 = v0; U1 = u1; V1 = v1;
        }
    }

    public readonly struct SourceInstructionVertex
    {
        public double X { get; }
        public double Y { get; }
        public double U { get; }
        public double V { get; }
        public SourceInstructionVertex(double x, double y, double u, double v)
        {
            if (!SourceInstructionRectangle.Finite(x) || !SourceInstructionRectangle.Finite(y)
                || !SourceInstructionRectangle.Finite(u) || !SourceInstructionRectangle.Finite(v))
                throw new ArgumentOutOfRangeException(nameof(x));
            X = x; Y = y; U = u; V = v;
        }
    }

    // Coordinates are native raster pixels, top-left origin. No viewport or font defaults live here.
    public sealed class SourceInstructionGraphic
    {
        public string Texture { get; }
        public string WhiteTexture { get; }
        public string ImageSymbol { get; }
        public uint Color { get; }
        public float Depth { get; }
        public ReadOnlyCollection<SourceInstructionVertex> Vertices { get; }
        public SourceInstructionGraphic(string texture, string whiteTexture, string imageSymbol, uint color,
            float depth, IEnumerable<SourceInstructionVertex> vertices)
        {
            if (string.IsNullOrEmpty(texture) || string.IsNullOrEmpty(imageSymbol) || vertices == null
                || !SourceInstructionRaster.Finite(depth)) throw new ArgumentException("Actual graphic data is required.");
            var copy = new List<SourceInstructionVertex>(vertices);
            if (copy.Count == 0 || copy.Count % 3 != 0) throw new ArgumentException("Triangle vertices are required.");
            Texture = texture; WhiteTexture = whiteTexture; ImageSymbol = imageSymbol; Color = color;
            Depth = depth; Vertices = copy.AsReadOnly();
        }
        public SourceInstructionGraphic With(string texture, uint color)
        { return new SourceInstructionGraphic(texture, WhiteTexture, ImageSymbol, color, Depth, Vertices); }
    }

    public sealed class SourceInstructionFormatSlot
    {
        public SourceInstructionPage Page { get; }
        public int Line { get; }
        public int NativePrintLine { get; }
        public string OriginalFormat { get; }
        public string NativeSecondArgumentSymbols { get; }
        public int NativeSecondArgument { get; }
        public SourceInstructionFormatSlot(SourceInstructionPage page, int line, int nativePrintLine,
            string originalFormat, string nativeSecondArgumentSymbols, int nativeSecondArgument)
        {
            var request = new SourceInstructionTextRequest(page, line,
                page == SourceInstructionPage.Rules && line == 1
                    ? SourceInstructionJustification.Left : SourceInstructionJustification.Center);
            if (originalFormat == null || nativeSecondArgumentSymbols != request.NativeSecondArgumentSymbols)
                throw new SourceInstructionPresentationException(SourceInstructionPresentationError.UnsupportedNativeBinding,
                    "Supply the original format and the actual value of its native second-argument symbols.");
            Page = page; Line = line; NativePrintLine = nativePrintLine; OriginalFormat = originalFormat;
            NativeSecondArgumentSymbols = nativeSecondArgumentSymbols; NativeSecondArgument = nativeSecondArgument;
        }
    }

    public sealed class SourceInstructionFormatCall
    {
        public SourceInstructionFormatSlot Slot { get; }
        public SourceInstructionTextRequest Request { get; }
        public SourceInstructionFont Font { get; }
        public uint Color { get; }
        public SourceInstructionPoint Position { get; }
        public SourceInstructionRaster Raster { get; }
        public SourceInstructionFormatCall(SourceInstructionFormatSlot slot, SourceInstructionTextRequest request,
            SourceInstructionFont font, uint color, SourceInstructionPoint position, SourceInstructionRaster raster)
        { Slot = slot; Request = request; Font = font; Color = color; Position = position; Raster = raster; }
    }

    public sealed class SourceInstructionTextRun
    {
        public string Text { get; }
        public SourceInstructionFont Font { get; }
        public uint Color { get; }
        public float OffsetX { get; }
        public float OffsetY { get; }
        public SourceInstructionJustification Justification { get; }
        public SourceInstructionTextRun(string text, SourceInstructionFont font, uint color, float offsetX,
            float offsetY, SourceInstructionJustification justification)
        {
            if (text == null || !SourceInstructionRaster.Finite(offsetX) || !SourceInstructionRaster.Finite(offsetY)
                || (justification != SourceInstructionJustification.Left && justification != SourceInstructionJustification.Center))
                throw new ArgumentException("Evaluated text runs require explicit style, offsets and alignment.");
            foreach (char c in text)
                if (char.IsControl(c)) throw new SourceInstructionPresentationException(
                    SourceInstructionPresentationError.UnsupportedNativeBinding,
                    "Native format/control sequences must be evaluated before glyph layout.");
            Text = text; Font = font; Color = color; OffsetX = offsetX; OffsetY = offsetY; Justification = justification;
        }
    }

    public interface ISourceInstructionNativeTextFormatter
    {
        SourceInstructionFormattedText Evaluate(SourceInstructionFormatCall call);
    }

    public sealed class SourceInstructionFormattedText
    {
        public uint StateColor { get; }
        public ReadOnlyCollection<SourceInstructionTextRun> Runs { get; }
        public SourceInstructionFormattedText(uint actualReturnedStringStateColor, IEnumerable<SourceInstructionTextRun> runs)
        {
            if (runs == null) throw new ArgumentNullException(nameof(runs));
            StateColor = actualReturnedStringStateColor;
            Runs = new List<SourceInstructionTextRun>(runs).AsReadOnly();
        }
    }

    public sealed class SourceInstructionTextLayout
    {
        public uint StateColor { get; }
        public ReadOnlyCollection<SourceInstructionGraphic> Graphics { get; }
        public SourceInstructionTextLayout(uint actualReturnedStringStateColor, IEnumerable<SourceInstructionGraphic> graphics)
        {
            if (graphics == null) throw new ArgumentNullException(nameof(graphics));
            StateColor = actualReturnedStringStateColor;
            Graphics = new List<SourceInstructionGraphic>(graphics).AsReadOnly();
        }
    }

    public interface ISourceInstructionNativeSpriteLayout
    {
        SourceInstructionRectangle Layout(SourceInstructionRaster raster, SourceInstructionImage actualImage,
            SourceInstructionPoint position, float widthScale, float heightScale);
    }

    public interface ISourceInstructionResources
    {
        SourceInstructionRaster ReadRaster();
        uint ResolveColor(SourceInstructionColor color);
        SourceInstructionImage ResolvePlate(string imageSymbol, string textureSymbol);
        IReadOnlyList<SourceInstructionGraphic> GeneratePlate(SourceInstructionImage image,
            SourceInstructionRaster raster, SourceInstructionPoint position, float widthScale, float heightScale);
        SourceInstructionTextLayout FormatText(SourceInstructionTextRequest request, SourceInstructionFont font,
            uint color, SourceInstructionRaster raster, SourceInstructionPoint position);
    }

    public readonly struct SourceInstructionPlateFlashStep
    {
        public bool White { get; }
        public bool ChromaKey { get; }
        public bool Completed { get; }
        public int SleepTicks { get; }
        public SourceInstructionPlateFlashStep(bool white, bool chromaKey, bool completed, int sleepTicks)
        {
            if ((!completed && sleepTicks <= 0) || sleepTicks < 0 || sleepTicks > 0x7fff)
                throw new ArgumentOutOfRangeException(nameof(sleepTicks));
            White = white; ChromaKey = chromaKey; Completed = completed; SleepTicks = sleepTicks;
        }
    }

    // flash_obj_proc forwards to SDK flash_obj_white. That unavailable helper has no guessed implementation.
    public interface ISourceInstructionObjectFlashProgram
    {
        SourceInstructionPlateFlashStep Advance();
    }

    public interface ISourceInstructionObjectFlashBinding
    {
        ISourceInstructionObjectFlashProgram Create(ISourceInstructionPlate plate, int flashes, int delay, int fourthArgument);
    }

    public readonly struct SourceInstructionSoundContext
    {
        public long SourceTick { get; }
        public long InstructionSerial { get; }
        public int CallOrdinal { get; }
        public SourceInstructionSoundContext(long tick, long serial, int ordinal)
        { SourceTick = tick; InstructionSerial = serial; CallOrdinal = ordinal; }
    }

    public interface ISourceInstructionSoundPlayer
    {
        void Play(SourceInstructionSound sound, SourceInstructionSoundContext context);
    }

    public interface ISourceInstructionProgramBinding
    {
        SourceInstructionSnapshot Current { get; }
        IReadOnlyList<SourcePossessionProcessRecord> Records { get; }
        long Invoke(SourceInstructionInvocation invocation);
    }
}
