using System;

namespace Blitz.Presentation
{
    public readonly struct SourceHudRect
    {
        public double X { get; }
        public double Y { get; }
        public double Width { get; }
        public double Height { get; }
        public double Right { get { return X + Width; } }
        public double Bottom { get { return Y + Height; } }
        public SourceHudRect(double x, double y, double width, double height)
        {
            if (!Finite(x) || !Finite(y) || !Finite(width) || !Finite(height) || width < 0 || height < 0)
                throw new ArgumentOutOfRangeException(nameof(width));
            X = x; Y = y; Width = width; Height = height;
        }
        public bool Contains(SourceHudRect other)
        { return other.X >= X - 0.001 && other.Y >= Y - 0.001 && other.Right <= Right + 0.001 && other.Bottom <= Bottom + 0.001; }
        public bool Overlaps(SourceHudRect other)
        { return X < other.Right && Right > other.X && Y < other.Bottom && Bottom > other.Y; }
        public bool Contains(double x, double y)
        { return x >= X && x < Right && y >= Y && y < Bottom; }
        private static bool Finite(double value) { return !double.IsNaN(value) && !double.IsInfinity(value); }
    }

    public readonly struct SourceHudViewport
    {
        public int BufferWidth { get; }
        public int BufferHeight { get; }
        public double ContentWidth { get; }
        public double ContentHeight { get; }
        public double DevicePixelRatio { get; }
        public double GuiPixelsPerPoint { get; }
        public double CssToGuiX { get { return BufferWidth / ContentWidth / GuiPixelsPerPoint; } }
        public double CssToGuiY { get { return BufferHeight / ContentHeight / GuiPixelsPerPoint; } }
        public SourceHudRect Content { get { return new SourceHudRect(0, 0, ContentWidth, ContentHeight); } }

        public SourceHudViewport(int bufferWidth, int bufferHeight, double contentWidth, double contentHeight,
            double devicePixelRatio, double guiPixelsPerPoint = 1)
        {
            if (bufferWidth < 1 || bufferHeight < 1 || !Positive(contentWidth) || !Positive(contentHeight) ||
                !Positive(devicePixelRatio) || !Positive(guiPixelsPerPoint))
                throw new ArgumentOutOfRangeException(nameof(contentWidth), "HUD needs actual canvas/CSS/DPR metrics.");
            if (Math.Abs(bufferWidth - contentWidth * devicePixelRatio) > 2 ||
                Math.Abs(bufferHeight - contentHeight * devicePixelRatio) > 2)
                throw new InvalidOperationException("Canvas drawing buffer does not follow the actual CSS content/DPR; fixed-buffer stretching is not source HUD resize support.");
            BufferWidth = bufferWidth; BufferHeight = bufferHeight; ContentWidth = contentWidth;
            ContentHeight = contentHeight; DevicePixelRatio = devicePixelRatio; GuiPixelsPerPoint = guiPixelsPerPoint;
        }

        public SourceHudRect ToGui(SourceHudRect css)
        { return new SourceHudRect(css.X * CssToGuiX, css.Y * CssToGuiY, css.Width * CssToGuiX, css.Height * CssToGuiY); }
        private static bool Positive(double value) { return !double.IsNaN(value) && !double.IsInfinity(value) && value > 0; }
    }

    public sealed class SourceHudLayout
    {
        public SourceHudViewport Viewport { get; }
        public double Scale { get; }
        public bool Portrait { get; }
        public SourceHudRect Score { get; }
        public SourceHudRect Menu { get; }
        public SourceHudRect Message { get; }
        public SourceHudRect Controls { get; }

        public SourceHudLayout(SourceHudViewport viewport, int humanStations)
        {
            if (humanStations < 0 || humanStations > 4) throw new ArgumentOutOfRangeException(nameof(humanStations));
            Viewport = viewport;
            Portrait = viewport.ContentWidth < 650;
            Scale = Math.Min(Math.Max(Portrait ? 1.5 : viewport.ContentWidth / 1024, 1.25), 2);
            double margin = Math.Min(12, viewport.ContentWidth / 30);
            if (104 * Scale + margin * 2 > viewport.ContentWidth)
                Scale = (viewport.ContentWidth - margin * 2) / 104;
            Score = new SourceHudRect(margin, margin, 104 * Scale, 48 * Scale);
            double footer = Portrait && humanStations > 2 ? 124 : Portrait ? 94 : 76;
            footer = Math.Min(footer, viewport.ContentHeight * 0.24);
            Controls = new SourceHudRect(margin, viewport.ContentHeight - footer,
                Math.Max(0, viewport.ContentWidth - margin * 2), Math.Max(0, footer - margin));
            double top = Score.Bottom + margin;
            Menu = new SourceHudRect(margin, top, Math.Max(0, viewport.ContentWidth - margin * 2),
                Math.Max(0, Controls.Y - margin - top));
            Message = new SourceHudRect(margin, Math.Min(Math.Max(top, viewport.ContentHeight * 0.27), Controls.Y),
                Math.Max(0, viewport.ContentWidth - margin * 2), Math.Max(0, Math.Min(100 * Scale, Controls.Y - top)));
        }

        public SourceHudRect Station(int displayOrdinal, int humanStations)
        {
            if (humanStations <= 0 || displayOrdinal < 0 || displayOrdinal >= humanStations)
                throw new ArgumentOutOfRangeException(nameof(displayOrdinal));
            int columns = Portrait && humanStations > 2 ? 2 : humanStations;
            int rows = (humanStations + columns - 1) / columns;
            double width = Controls.Width / columns, height = Controls.Height / rows;
            return new SourceHudRect(Controls.X + (displayOrdinal % columns) * width,
                Controls.Y + (displayOrdinal / columns) * height, width, height);
        }
    }
}
