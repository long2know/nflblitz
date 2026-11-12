using System;
using System.Collections.Generic;
using Blitz.Contracts;

namespace Blitz.Presentation
{
    public sealed class SourceInstructionHudRenderer
    {
        private readonly SourceInstructionPresentation presentation;
        private readonly SourceInstructionHudResources resources;

        public SourceInstructionHudRenderer(SourceInstructionPresentation presentation, SourceInstructionHudResources resources)
        {
            this.presentation = presentation ?? throw new ArgumentNullException(nameof(presentation));
            this.resources = resources ?? throw new ArgumentNullException(nameof(resources));
        }

        public void Draw(SourceHudViewport viewport)
        { DrawGraphics(viewport, presentation.Frame()); }

        public void Draw(SourceHudViewport viewport, IReadOnlyList<SourceInstructionGraphic> kickoffFrame)
        {
            if (kickoffFrame == null) throw new ArgumentNullException(nameof(kickoffFrame));
            var frame = new List<SourceInstructionGraphic>(presentation.Frame());
            frame.AddRange(kickoffFrame);
            DrawGraphics(viewport, frame);
        }

        private void DrawGraphics(SourceHudViewport viewport, IReadOnlyList<SourceInstructionGraphic> frame)
        {
            if (frame.Count == 0) return;
            SourceInstructionRaster raster = resources.ReadRaster();
            double scale = Math.Min(viewport.ContentWidth / raster.Width, viewport.ContentHeight / raster.Height);
            double x = (viewport.ContentWidth - raster.Width * scale) / 2;
            double y = (viewport.ContentHeight - raster.Height * scale) / 2;
            var primitives = new List<SourceHudPrimitive>();
            foreach (SourceInstructionGraphic graphic in frame)
            {
                var vertices = new SourceHudVertex[graphic.Vertices.Count];
                for (int i = 0; i < vertices.Length; i++)
                {
                    SourceInstructionVertex vertex = graphic.Vertices[i];
                    vertices[i] = new SourceHudVertex(x + vertex.X * scale, y + vertex.Y * scale, vertex.U, vertex.V);
                }
                primitives.Add(new SourceHudPrimitive("instructions/" + graphic.ImageSymbol, graphic.Texture,
                    graphic.Color, vertices, sourceDepth: graphic.Depth, sourceImageSymbol: graphic.ImageSymbol));
            }
            SourceHudRenderer.DrawPrimitives(resources.Asset, primitives, viewport);
        }
    }
}
