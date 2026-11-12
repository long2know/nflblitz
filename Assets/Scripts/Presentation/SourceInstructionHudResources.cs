using System;
using System.Collections.Generic;
using Blitz.AssetImport;
using Blitz.Contracts;

namespace Blitz.Presentation
{
    public sealed class SourceInstructionHudResources : ISourceInstructionResources
    {
        private readonly Func<SourceInstructionRaster> readRaster;
        private readonly Dictionary<SourceInstructionFont, SourceInstructionFontResource> fonts;
        private readonly Dictionary<SourceInstructionColor, uint> colors;
        private readonly SourceInstructionTextProgram text;
        private readonly ISourceInstructionNativeSpriteLayout spriteLayout;
        private readonly string plateTexture, whiteTexture;
        public SourceHudAsset Asset { get; }

        public SourceInstructionHudResources(SourceHudAsset asset, Func<SourceInstructionRaster> actualRaster,
            IEnumerable<SourceInstructionFormatSlot> originalFormats, ISourceInstructionNativeTextFormatter nativeFormatter,
            IReadOnlyDictionary<SourceInstructionFont, SourceInstructionFontResource> actualFonts,
            IReadOnlyDictionary<SourceInstructionColor, uint> actualColors,
            ISourceInstructionNativeSpriteLayout nativeSpriteLayout, string actualPlateTexture, string actualWhiteTexture)
        {
            Asset = asset ?? throw new ArgumentNullException(nameof(asset));
            readRaster = actualRaster ?? throw new ArgumentNullException(nameof(actualRaster));
            spriteLayout = nativeSpriteLayout ?? throw new ArgumentNullException(nameof(nativeSpriteLayout));
            text = new SourceInstructionTextProgram(originalFormats, nativeFormatter);
            if (actualFonts == null || actualColors == null || string.IsNullOrEmpty(actualPlateTexture)
                || string.IsNullOrEmpty(actualWhiteTexture))
                throw Missing("Actual source fonts, colors and plate/white atlas bindings are required.");
            fonts = new Dictionary<SourceInstructionFont, SourceInstructionFontResource>();
            colors = new Dictionary<SourceInstructionColor, uint>();
            foreach (SourceInstructionFont font in Enum.GetValues(typeof(SourceInstructionFont)))
            {
                SourceInstructionFontResource binding;
                if (!actualFonts.TryGetValue(font, out binding) || binding == null) throw Missing("Missing source font " + font + ".");
                Asset.Data.Font(binding.Font);
                RequireTexture(binding.Texture);
                fonts.Add(font, binding);
            }
            foreach (SourceInstructionColor color in Enum.GetValues(typeof(SourceInstructionColor)))
            {
                uint value;
                if (!actualColors.TryGetValue(color, out value)) throw Missing("Missing actual native color " + color + ".");
                colors.Add(color, value);
            }
            plateTexture = actualPlateTexture; whiteTexture = actualWhiteTexture;
            RequireTexture(plateTexture); RequireTexture(whiteTexture);
        }

        public SourceInstructionRaster ReadRaster()
        {
            SourceInstructionRaster raster = readRaster();
            if (raster.Width <= 0 || raster.Height <= 0) throw Missing("The native raster binding is unspecified.");
            return raster;
        }
        public uint ResolveColor(SourceInstructionColor color)
        {
            uint value;
            if (!colors.TryGetValue(color, out value)) throw Missing("Unbound native color " + color + ".");
            return value;
        }
        public SourceInstructionImage ResolvePlate(string imageSymbol, string textureSymbol)
        {
            if (imageSymbol != "playresult" || textureSymbol != "SCR_PLATE_TID")
                throw Missing("The supplied native plate image/texture pair is not bound.");
            SourceInterfaceImage image = Asset.Data.Image(imageSymbol);
            SourceInterfaceTexture atlas = RequireTexture(plateTexture);
            int width = checked((int)image.Width), height = checked((int)image.Height);
            if (width != image.Width || height != image.Height) throw Missing("Nonintegral native image metrics are not supported.");
            return new SourceInstructionImage(image.Id, plateTexture, whiteTexture, width, height,
                image.AnchorX, image.AnchorY, image.X / (double)atlas.Width, image.Y / (double)atlas.Height,
                (image.X + image.Width) / (double)atlas.Width, (image.Y + image.Height) / (double)atlas.Height);
        }
        public IReadOnlyList<SourceInstructionGraphic> GeneratePlate(SourceInstructionImage image,
            SourceInstructionRaster raster, SourceInstructionPoint position, float widthScale, float heightScale)
        {
            SourceInstructionRectangle rectangle = spriteLayout.Layout(raster, image, position, widthScale, heightScale);
            if (rectangle.Width <= 0 || rectangle.Height <= 0) throw Missing("The native sprite layout returned no actual rectangle.");
            return new[] { Quad(image.Symbol, image.Texture, image.WhiteTexture, ResolveColor(SourceInstructionColor.White),
                position.Depth, rectangle, image.U0, image.V0, image.U1, image.V1) };
        }
        public SourceInstructionTextLayout FormatText(SourceInstructionTextRequest request,
            SourceInstructionFont font, uint color, SourceInstructionRaster raster, SourceInstructionPoint position)
        {
            SourceInstructionFormattedText formatted = text.Evaluate(request, font, color, raster, position);
            var output = new List<SourceInstructionGraphic>();
            foreach (SourceInstructionTextRun run in formatted.Runs)
            {
                SourceInstructionFontResource binding;
                if (!fonts.TryGetValue(run.Font, out binding)) throw Missing("The evaluated native font is unbound.");
                AppendText(output, binding, run.Text, run.Color, run.OffsetX, run.OffsetY, run.Justification, raster, position);
            }
            if (output.Count == 0) throw Missing("The original instruction text resolved to no actual glyph images.");
            return new SourceInstructionTextLayout(formatted.StateColor, output);
        }
        public ISourceKickoffInstructionResources BindKickoff(SourceKickoffInstructionHudBindings bindings)
        { return new KickoffResources(this, bindings); }

        private void AppendText(List<SourceInstructionGraphic> output, SourceInstructionFontResource binding,
            string value, uint color, float offsetX, float offsetY, SourceInstructionJustification justification,
            SourceInstructionRaster raster, SourceInstructionPoint position)
        {
            SourceInterfaceFont actualFont = Asset.Data.Font(binding.Font);
            SourceInterfaceTexture atlas = RequireTexture(binding.Texture);
            double x = position.X + offsetX, y = raster.Height - (position.Y + offsetY);
            double leftShift = justification == SourceInstructionJustification.Left
                ? Math.Truncate(SourceHudText.Width(actualFont, value) / 2) : 0;
            foreach (SourceHudGlyph glyph in SourceHudText.Center(actualFont, value, x, y, 1))
            {
                SourceInterfaceImage image = glyph.Image;
                var rect = new SourceInstructionRectangle(glyph.Rect.X + leftShift, glyph.Rect.Y, glyph.Rect.Width, glyph.Rect.Height);
                output.Add(Quad(image.Id, binding.Texture, null, color, position.Depth, rect,
                    image.X / (double)atlas.Width, image.Y / (double)atlas.Height,
                    (image.X + image.Width) / (double)atlas.Width, (image.Y + image.Height) / (double)atlas.Height));
            }
        }

        private sealed class KickoffResources : ISourceKickoffInstructionResources
        {
            private readonly SourceInstructionHudResources owner;
            private readonly SourceKickoffInstructionHudBindings bindings;
            internal KickoffResources(SourceInstructionHudResources owner, SourceKickoffInstructionHudBindings bindings)
            {
                this.owner = owner; this.bindings = bindings ?? throw Missing("Actual child HUD bindings are required.");
                try
                {
                    foreach (SourceKickoffInstructionChildFont font in Enum.GetValues(typeof(SourceKickoffInstructionChildFont)))
                    {
                        SourceInstructionFontResource resource = bindings.ResolveFont(font);
                        owner.Asset.Data.Font(resource.Font); owner.RequireTexture(resource.Texture);
                    }
                    owner.ResolvePlate("playresult", "SCR_PLATE_TID");
                }
                catch (SourceInterfaceException error) { throw MissingAsset(error); }
            }
            public SourceInstructionRaster ReadRaster() { return owner.ReadRaster(); }
            public uint ResolveColor(SourceKickoffInstructionChildColor color) { return bindings.ResolveColor(color); }
            public uint ResolveObjectId(string symbol, int offset) { return bindings.ResolveObjectId(symbol, offset); }
            public SourceInstructionImage ResolvePlate(string image, string texture)
            {
                try { return owner.ResolvePlate(image, texture); }
                catch (SourceInterfaceException error) { throw MissingAsset(error); }
            }
            public IReadOnlyList<SourceInstructionGraphic> GeneratePlate(SourceInstructionImage image,
                SourceInstructionRaster raster, SourceInstructionPoint position, float zAngle, float hScale)
            {
                IReadOnlyList<SourceInstructionVertex> vertices = bindings.PlateLayout.Layout(image, raster, position, zAngle, hScale);
                if (vertices == null || vertices.Count == 0) throw Missing("The native child sprite layout returned no actual vertices.");
                if (vertices.Count % 3 != 0) throw new SourceInstructionPresentationException(
                    SourceInstructionPresentationError.InvalidGeometry, "Native child sprite triangle vertices are required.");
                return new[] { new SourceInstructionGraphic(image.Texture, image.WhiteTexture, image.Symbol,
                    bindings.PlateColor, position.Depth, vertices) };
            }
            public SourceInstructionTextLayout FormatText(SourceKickoffInstructionChildTextRequest request,
                SourceKickoffInstructionChildFont font, uint color, SourceInstructionRaster raster,
                SourceInstructionPoint position, float textZIncrement)
            {
                SourceKickoffInstructionFormattedText formatted = bindings.Evaluate(request, font, color, raster, position, textZIncrement);
                var output = new List<SourceInstructionGraphic>();
                try
                {
                    foreach (SourceKickoffInstructionTextRun run in formatted.Runs)
                        owner.AppendText(output, bindings.ResolveFont(run.Font), run.Text, run.Color,
                            run.OffsetX, run.OffsetY, run.Justification, raster, position);
                }
                catch (SourceInterfaceException error) { throw MissingAsset(error); }
                if (output.Count == 0) throw Missing("The original child text resolved to no actual glyph images.");
                return new SourceInstructionTextLayout(formatted.StateColor, output);
            }
            private static SourceInstructionPresentationException MissingAsset(SourceInterfaceException error)
            { return new SourceInstructionPresentationException(SourceInstructionPresentationError.MissingResource,
                "The actual child HUD resource is unavailable.", error); }
        }
        private SourceInterfaceTexture RequireTexture(string id)
        {
            try { return Asset.TextureAtlas(id); }
            catch (SourceInterfaceException error)
            {
                throw new SourceInstructionPresentationException(SourceInstructionPresentationError.MissingResource,
                    "The actual atlas is unavailable: " + id + ".", error);
            }
        }
        private static SourceInstructionGraphic Quad(string image, string texture, string white, uint color, float depth,
            SourceInstructionRectangle rect, double u0, double v0, double u1, double v1)
        {
            double right = rect.Left + rect.Width, bottom = rect.Top + rect.Height;
            return new SourceInstructionGraphic(texture, white, image, color, depth, new[] {
                new SourceInstructionVertex(rect.Left, rect.Top, u0, v1), new SourceInstructionVertex(right, rect.Top, u1, v1),
                new SourceInstructionVertex(right, bottom, u1, v0), new SourceInstructionVertex(rect.Left, rect.Top, u0, v1),
                new SourceInstructionVertex(right, bottom, u1, v0), new SourceInstructionVertex(rect.Left, bottom, u0, v0) });
        }
        private static SourceInstructionPresentationException Missing(string message)
        { return new SourceInstructionPresentationException(SourceInstructionPresentationError.MissingResource, message); }
    }
}
