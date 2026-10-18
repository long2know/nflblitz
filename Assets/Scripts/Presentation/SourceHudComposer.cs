using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Blitz.AssetImport;
using Blitz.Contracts;
using Blitz.Runtime;

namespace Blitz.Presentation
{
    public readonly struct SourceHudVertex
    {
        public double X { get; }
        public double Y { get; }
        public double S { get; }
        public double T { get; }
        internal SourceHudVertex(double x, double y, double s, double t) { X = x; Y = y; S = s; T = t; }
    }

    public sealed class SourceHudPrimitive
    {
        public string Element { get; }
        public string Texture { get; }
        public uint Color { get; }
        public SourceHudRect Bounds { get; }
        public ReadOnlyCollection<SourceHudVertex> Vertices { get; }
        internal SourceHudPrimitive(string element, string texture, uint color, SourceHudVertex[] vertices)
        {
            Element = element; Texture = texture; Color = color;
            Vertices = Array.AsReadOnly(vertices);
            double left = double.MaxValue, top = double.MaxValue, right = double.MinValue, bottom = double.MinValue;
            foreach (SourceHudVertex vertex in vertices)
            {
                left = Math.Min(left, vertex.X); top = Math.Min(top, vertex.Y);
                right = Math.Max(right, vertex.X); bottom = Math.Max(bottom, vertex.Y);
            }
            Bounds = new SourceHudRect(left, top, right - left, bottom - top);
        }
    }

    public sealed class SourceHudStatusAnimation
    {
        private long lastTick = -1;
        private int delay = 8;
        public bool RedClock { get; private set; }
        public void Advance(long sourceTick, bool clockWarning)
        {
            if (sourceTick == lastTick) return;
            if (sourceTick < lastTick || lastTick >= 0 && sourceTick != lastTick + 1)
                throw new SourceInterfaceException("The HUD needs every ordered source wake; render catch-up cannot skip clock/status ticks.");
            lastTick = sourceTick;
            if (clockWarning)
            {
                if (delay-- == 0) { RedClock = !RedClock; delay = 8; }
            }
            else if (RedClock) RedClock = false;
        }
        public void Clear() { lastTick = -1; delay = 8; RedClock = false; }
    }

    public sealed class SourceHudComposer
    {
        private const uint White = 0xffffffffu;
        private const uint Yellow = 0xffffff00u;
        private const uint Red = 0xffff0000u;
        private readonly SourceInterfaceData data;
        private readonly List<SourceHudPrimitive> draw = new List<SourceHudPrimitive>();
        public SourceHudComposer(SourceInterfaceData data) { this.data = data ?? throw new ArgumentNullException(nameof(data)); }

        public IReadOnlyList<SourceHudPrimitive> Compose(IGameState state, SourceHudViewport viewport,
            SourceCabinetPlaySelection selection, bool redClock, bool suspended)
        {
            draw.Clear();
            var hud = new SourceHudState(state, data);
            var layout = new SourceHudLayout(viewport, hud.Match.Stations.Count);
            SourceHudRect score = layout.Score;
            Image("score/art", data.Image("gameclock"), score, White);
            double scale = layout.Scale;
            Text("score/clock", "bast8t", hud.Clock, score.X + 24 * scale, score.Y + 18 * scale, scale, redClock ? Red : White);
            Text("score/period", "bast8t", hud.Period, score.X + 27 * scale, score.Y + 31 * scale, scale, White);
            Text("score/home/name", "bast8t", hud.HomeName, score.X + 62 * scale, score.Y + 18 * scale, scale,
                hud.HighlightedSide == TeamId.Home ? Yellow : White);
            Text("score/home/value", "bast8t", hud.HomeScore.ToString(), score.X + 84 * scale, score.Y + 18 * scale, scale,
                hud.HighlightedSide == TeamId.Home ? Yellow : White);
            Text("score/away/name", "bast8t", hud.AwayName, score.X + 62 * scale, score.Y + 31 * scale, scale,
                hud.HighlightedSide == TeamId.Away ? Yellow : White);
            Text("score/away/value", "bast8t", hud.AwayScore.ToString(), score.X + 84 * scale, score.Y + 31 * scale, scale,
                hud.HighlightedSide == TeamId.Away ? Yellow : White);
            if (hud.Match.Phase == SourceMatchPhase.PlaySelection || hud.Match.Phase == SourceMatchPhase.PatSelection)
            {
                if (selection == null) throw new SourceInterfaceException("Original play-page HUD has no source selection consumer.");
                if (layout.Portrait)
                    Menu(state, selection.LastInteractedSide, layout.Menu, selection);
                else
                {
                    double width = layout.Menu.Width / 2;
                    Menu(state, TeamId.Home, new SourceHudRect(layout.Menu.X, layout.Menu.Y, width, layout.Menu.Height), selection);
                    Menu(state, TeamId.Away, new SourceHudRect(layout.Menu.X + width, layout.Menu.Y, width, layout.Menu.Height), selection);
                }
            }
            else if (hud.ResultGraphic != null && hud.Match.Phase != SourceMatchPhase.LiveBall)
            {
                SourceInterfaceImage image = data.Image(hud.ResultGraphic);
                double fit = Math.Min(layout.Message.Width / image.Width, layout.Message.Height / image.Height);
                var rect = new SourceHudRect(layout.Message.X + (layout.Message.Width - image.Width * fit) / 2,
                    layout.Message.Y, image.Width * fit, image.Height * fit);
                Image("result/" + hud.ResultGraphic, image, rect, White);
            }
            else if (hud.StateText.Length > 0 && hud.Match.Phase != SourceMatchPhase.LiveBall && !hud.KickMeter.ShowMeterFlag)
                FitText("state", "bast18", hud.StateText, layout.Message, scale, White);
            KickMeter(hud.KickMeter, hud.KickMeterMetrics, viewport);
            if (suspended)
                FitText("adapter/pause", "bast18", "PAUSED - RELEASE CONTROLS", layout.Message, scale, Yellow);
            Controls(hud.Match, layout, hud.KickMeter.ShowMeterFlag);
            foreach (SourceHudPrimitive primitive in draw)
                if (!viewport.Content.Contains(primitive.Bounds))
                    throw new SourceInterfaceException("Original HUD element exceeds actual CSS content: " + primitive.Element + ".");
            return Array.AsReadOnly(draw.ToArray());
        }

        public IReadOnlyList<SourceHudPrimitive> Indicator(int stationId, double x, double y, SourceHudViewport viewport,
            int humanStations)
        {
            SourceCabinetQuantizer.ValidateStationId(stationId);
            draw.Clear();
            var layout = new SourceHudLayout(viewport, humanStations);
            double scale = Math.Min(layout.Scale, 1.25);
            SourceInterfaceImage arrow = data.Image(stationId == 0 ? "arrow_blue" : stationId == 1 ? "arrow_green" :
                stationId == 2 ? "arrow_red" : "arrow_yellow");
            double left = Math.Max(2, Math.Min(viewport.ContentWidth - arrow.Width * scale - 2, x - arrow.AnchorX * scale));
            double top = Math.Max(layout.Score.Bottom + 4, Math.Min(layout.Controls.Y - arrow.Height * scale - 4, y - arrow.AnchorY * scale));
            Sprite("indicator/" + stationId + "/arrow", arrow.Id, left + arrow.AnchorX * scale, top + arrow.AnchorY * scale, scale, 1, false, White);
            Sprite("indicator/" + stationId + "/number", "arrow_num" + (stationId + 1),
                left + arrow.AnchorX * scale, top + arrow.AnchorY * scale, scale, 1, false, White);
            return Array.AsReadOnly(draw.ToArray());
        }

        private void Controls(SourceMatchView match, SourceHudLayout layout, bool hideTurbo)
        {
            for (int i = 0; i < match.Configuration.Stations.Count; i++)
            {
                int stationId = match.Configuration.Stations[i].StationId;
                SourceStationView station = SourceCabinetStations.View(match, stationId);
                SourceHudRect area = layout.Station(i, match.Stations.Count);
                SourceInterfaceImage empty = data.Image("tb_empty");
                double scale = Math.Min(layout.Scale, (area.Width - 10) / empty.Width);
                double anchorX = area.X + area.Width / 2, anchorY = area.Y + 8 * scale;
                if (!hideTurbo)
                    Sprite("station/" + stationId + "/turbo-frame", "tb_empty", anchorX, anchorY, scale, 1, false, White);
                string color = stationId == 0 ? "blue" : stationId == 1 ? "green" : stationId == 2 ? "red" : "yellow";
                if (!hideTurbo)
                    Sprite("station/" + stationId + "/turbo", "tb_" + color, anchorX, anchorY, scale, station.Turbo01, false, White);
                string keys = stationId == 0 ? "A:V B:B C:N" : stationId == 1 ? "A:DEL B:END C:PGDN" :
                    stationId == 2 ? "A:U B:O C:P" : "A:Z B:X C:C";
                string move = stationId == 0 ? "WASD" : stationId == 1 ? "ARROWS" : stationId == 2 ? "IJKL" : "TFGH";
                FitText("station/" + stationId + "/keys", "bast8t", "P" + (stationId + 1) + " " + keys,
                    new SourceHudRect(area.X + 2, area.Y + 19 * scale, Math.Max(0, area.Width - 4), Math.Max(0, area.Height * 0.32)), 1.5, White);
                FitText("station/" + stationId + "/move", "bast8t", move + " - PAD " + (stationId + 1),
                    new SourceHudRect(area.X + 2, area.Y + area.Height * 0.66, Math.Max(0, area.Width - 4), area.Height * 0.3), 1.3, White);
            }
        }

        private void KickMeter(SourceKickMeterView meter, SourceKickMeterMetrics metrics, SourceHudViewport viewport)
        {
            if (!meter.ShowMeterFlag) return;
            if (metrics == null || metrics.ContentSha256 != data.ContentSha256)
                throw new SourceInterfaceException("The meter and HUD must use the SAME pinned authored-image generation.");
            float x = meter.Kind == SourceKickMeterKind.FieldGoal
                ? meter.Owner == TeamId.Home ? 148f : 364f
                : meter.Owner == TeamId.Home ? 64f : 448f;
            if (meter.Status == SourceKickMeterStatus.TimeExpired)
            {
                MeterSprite("kick/timeout/art", "playresult", x, 36f, viewport, 0.9f, 0.55f);
                double scale = Math.Min(viewport.ContentWidth / 512, viewport.ContentHeight / 384);
                SourceInterfaceTexture atlas = data.Textures["interface"];
                foreach (SourceHudGlyph glyph in SourceHudText.Center(data.Font("bast18"), "TIME EXPIRED",
                    (viewport.ContentWidth - 512 * scale) / 2 + x * scale,
                    (viewport.ContentHeight - 384 * scale) / 2 + 348 * scale, scale))
                    MeterPrimitive("kick/timeout/text", viewport, glyph.Rect.X, glyph.Rect.Y,
                        glyph.Rect.Right, glyph.Rect.Bottom, glyph.Image.X / (double)atlas.Width,
                        (glyph.Image.X + glyph.Image.Width) / (double)atlas.Width,
                        glyph.Image.Y / (double)atlas.Height, (glyph.Image.Y + glyph.Image.Height) / (double)atlas.Height,
                        0xff00ffff);
                return;
            }
            float y = meter.Kind == SourceKickMeterKind.FieldGoal ? 56f : 101f;
            if (meter.Kind == SourceKickMeterKind.FieldGoal)
            {
                MeterSprite("kick/fg/box", "k_box", x, y, viewport);
                MeterSprite("kick/fg/bar", "k_bar", x, y, viewport);
                MeterSprite("kick/fg/marker", "k_marker", x + meter.MarkerOffsetPixels, y, viewport);
                if (meter.Wind != 0f)
                {
                    float sign = meter.Wind < 0f ? 1f : -1f;
                    SourceInterfaceImage arrow = data.Image("k_windarrow");
                    float width = 30f + (arrow.Width - 30f) * Math.Abs(meter.Wind);
                    MeterSprite("kick/fg/wind-text", "k_windtext", x + 40f * sign, y, viewport);
                    MeterSprite("kick/fg/wind-arrow", "k_windarrow",
                        x - 45f * sign + (sign > 0f ? arrow.Width - width : 0f),
                        y, viewport, width: width, fromRight: true, flip: sign > 0f);
                }
                MeterSprite("kick/fg/ball", "k_ball", x + meter.SpriteOffsetPixels, y, viewport);
            }
            else
            {
                MeterSprite("kick/punt/bar", "p_meter", x, y, viewport);
                MeterSprite("kick/punt/ball", "k_ball", x, y + meter.SpriteOffsetPixels, viewport);
            }
        }

        private void MeterSprite(string element, string symbol, float x, float y, SourceHudViewport viewport,
            float widthScale = 1f, float heightScale = 1f, float? width = null, bool fromRight = false, bool flip = false)
        {
            SourceInterfaceImage image = data.Image(symbol);
            SourceInterfaceTexture atlas = data.Textures["interface"];
            double scale = Math.Min(viewport.ContentWidth / 512, viewport.ContentHeight / 384);
            double offsetX = (viewport.ContentWidth - 512 * scale) / 2;
            double offsetY = (viewport.ContentHeight - 384 * scale) / 2;
            // Reuse the HUD's authored-anchor interpretation, not unknown generated native anchors/crops.
            double left = offsetX + (x - image.AnchorX * widthScale) * scale;
            double top = offsetY + (384 - y - image.AnchorY * heightScale) * scale;
            double right = left + (width ?? image.Width) * widthScale * scale;
            double bottom = top + image.Height * heightScale * scale;
            double s0 = (image.X + (fromRight ? image.Width - (width ?? image.Width) : 0)) / (double)atlas.Width;
            double s1 = (image.X + (fromRight ? image.Width : (width ?? image.Width))) / (double)atlas.Width;
            double t0 = image.Y / (double)atlas.Height, t1 = (image.Y + image.Height) / (double)atlas.Height;
            if (flip) { double swap = s0; s0 = s1; s1 = swap; }
            MeterPrimitive(element, viewport, left, top, right, bottom, s0, s1, t0, t1, White);
        }

        private void MeterPrimitive(string element, SourceHudViewport viewport, double left, double top,
            double right, double bottom, double s0, double s1, double t0, double t1, uint color)
        {
            // Ordinary canvas clipping also clips UVs; it does NOT manufacture native Composit image metrics.
            double l = Math.Max(0, left), r = Math.Min(viewport.ContentWidth, right);
            double t = Math.Max(0, top), b = Math.Min(viewport.ContentHeight, bottom);
            if (r <= l || b <= t) return;
            double sl = s0 + (s1 - s0) * (l - left) / (right - left);
            double sr = s0 + (s1 - s0) * (r - left) / (right - left);
            double tt = t1 + (t0 - t1) * (t - top) / (bottom - top);
            double tb = t1 + (t0 - t1) * (b - top) / (bottom - top);
            draw.Add(new SourceHudPrimitive(element, "interface", color, new[] {
                new SourceHudVertex(l, t, sl, tt), new SourceHudVertex(r, t, sr, tt),
                new SourceHudVertex(r, b, sr, tb), new SourceHudVertex(l, t, sl, tt),
                new SourceHudVertex(r, b, sr, tb), new SourceHudVertex(l, b, sl, tb) }));
        }

        private void Menu(IGameState state, TeamId side, SourceHudRect area, SourceCabinetPlaySelection selection)
        {
            SourceMatchView match = ((ISourceMatchState)state).Match;
            int captain = SourceCabinetStations.Captain(match, side);
            if (captain < 0)
            {
                FitText("selection/" + side + "/cpu", "bast18", "CPU", area, 1.5, White);
                return;
            }
            const double sourceWidth = 252, sourceHeight = 350;
            double scale = Math.Min(area.Width / sourceWidth, area.Height / sourceHeight);
            if (scale <= 0) return;
            double x = area.X + (area.Width - sourceWidth * scale) / 2, y = area.Y;
            int sourceTeam = side == TeamId.Home ? match.Configuration.HomeTeam.TeamIndex : match.Configuration.AwayTeam.TeamIndex;
            FitText("selection/" + side + "/title", "bast18", data.TeamAbbreviations[sourceTeam] + " - PLAYER " + (captain + 1),
                new SourceHudRect(x, y, sourceWidth * scale, 26 * scale), scale, Yellow);
            Text("selection/" + side + "/page", "bast10", "PAGE " + (selection.Page(side) + 1),
                x + 126 * scale, y + 38 * scale, scale, Yellow);
            if (match.SelectionCountdown >= 0)
                Text("selection/" + side + "/countdown", "bast10", match.SelectionCountdown.ToString(),
                    x + 236 * scale, y + 38 * scale, scale, White);
            for (int slot = 0; slot < 9; slot++)
            {
                double left = x + slot % 3 * 83 * scale, top = y + (52 + slot / 3 * 96) * scale;
                SourceInterfacePlaySlot play = selection.Slot(state, side, selection.Page(side), slot);
                bool flip = side == match.Possession.Offense && selection.Flipped;
                string graphic = flip && play.FlippedGraphic != null ? play.FlippedGraphic : play.NormalGraphic;
                if (graphic == null) throw new SourceInterfaceException("Missing actual selected play graphic: " + play.Name);
                if (graphic.StartsWith("limb_", StringComparison.Ordinal))
                    Plane("selection/" + side + "/play/" + slot, graphic, left, top, scale, White);
                else
                {
                    Plane("selection/" + side + "/blank/" + slot, "limb_blank", left, top, scale, White);
                    Sprite("selection/" + side + "/play/" + slot, graphic, left + scale, top + scale, scale, 1, flip, White);
                }
                FitText("selection/" + side + "/name/" + slot, "bast8t", play.Name,
                    new SourceHudRect(left + scale, top + 81 * scale, 81 * scale, 14 * scale), scale, White);
            }
            if (!selection.Hidden(side) && !selection.Selected(side))
            {
                int slot = selection.Cursor(side);
                uint color = captain == 0 ? 0xff0000ffu : captain == 1 ? 0xff00ff00u : captain == 2 ? Red : Yellow;
                Plane("selection/" + side + "/cursor", side == TeamId.Home ? "limb_cursorb_obj" : "limb_cursorr_obj",
                    x + slot % 3 * 83 * scale, y + (52 + slot / 3 * 96) * scale, scale, color);
            }
        }

        private void Plane(string element, string symbol, double x, double y, double scale, uint color)
        {
            ReadOnlyCollection<SourceInterfaceTriangle> triangles;
            if (!data.Planes.TryGetValue(symbol, out triangles))
                throw new SourceInterfaceException("Missing original play-selection plane: " + symbol);
            foreach (SourceInterfaceTriangle triangle in triangles)
            {
                var vertices = new SourceHudVertex[3];
                for (int i = 0; i < vertices.Length; i++)
                {
                    SourceInterfaceVertex vertex = triangle.Vertices[i];
                    vertices[i] = new SourceHudVertex(x + vertex.X * scale, y - vertex.Y * scale, vertex.S, 1 - vertex.T);
                }
                draw.Add(new SourceHudPrimitive(element, triangle.Texture, color, vertices));
            }
        }

        private void Sprite(string element, string symbol, double anchorX, double anchorY, double scale, double fraction, bool flipped, uint color)
        {
            SourceInterfaceImage image = data.Image(symbol);
            Image(element, image, new SourceHudRect(anchorX - image.AnchorX * scale, anchorY - image.AnchorY * scale,
                image.Width * scale * fraction, image.Height * scale), color, fraction, flipped);
        }

        private void Image(string element, SourceInterfaceImage image, SourceHudRect rect, uint color,
            double fraction = 1, bool flipped = false)
        {
            SourceInterfaceTexture atlas = data.Textures["interface"];
            double s0 = image.X / (double)atlas.Width, s1 = (image.X + image.Width * fraction) / atlas.Width;
            double t0 = image.Y / (double)atlas.Height, t1 = (image.Y + image.Height) / (double)atlas.Height;
            if (flipped) { double swap = s0; s0 = s1; s1 = swap; }
            draw.Add(new SourceHudPrimitive(element, "interface", color, new[] {
                new SourceHudVertex(rect.X, rect.Y, s0, t1), new SourceHudVertex(rect.Right, rect.Y, s1, t1),
                new SourceHudVertex(rect.Right, rect.Bottom, s1, t0), new SourceHudVertex(rect.X, rect.Y, s0, t1),
                new SourceHudVertex(rect.Right, rect.Bottom, s1, t0), new SourceHudVertex(rect.X, rect.Bottom, s0, t0) }));
        }

        private void Text(string element, string font, string text, double x, double y, double scale, uint color)
        {
            foreach (SourceHudGlyph glyph in SourceHudText.Center(data.Font(font), text, x, y, scale))
                Image(element, glyph.Image, glyph.Rect, color);
        }

        private void FitText(string element, string fontId, string text, SourceHudRect bounds, double preferredScale, uint color)
        {
            SourceInterfaceFont font = data.Font(fontId);
            double width = SourceHudText.Width(font, text);
            double scale = Math.Min(preferredScale, Math.Min(bounds.Width / Math.Max(1, width + 4), bounds.Height / (font.Height + 6)));
            if (scale <= 0) return;
            Text(element, fontId, text, bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2, scale, color);
        }
    }
}
