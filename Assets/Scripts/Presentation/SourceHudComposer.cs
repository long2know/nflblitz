using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
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
        public SourceAudibleView Audibles { get; }
        public SourceAudibleSpriteView AudibleSprite { get; }
        public float? SourceDepth { get; }
        public string SourceImageSymbol { get; }
        public string TextContent { get; }
        public bool PrivateContent { get; }
        internal SourceHudPrimitive(string element, string texture, uint color, SourceHudVertex[] vertices,
            SourceAudibleView audibles = null, SourceAudibleSpriteView audibleSprite = null,
            float? sourceDepth = null, string sourceImageSymbol = null, string textContent = null,
            bool privateContent = false)
        {
            Element = element; Texture = texture; Color = color;
            Audibles = audibles; AudibleSprite = audibleSprite;
            SourceDepth = sourceDepth; SourceImageSymbol = sourceImageSymbol;
            TextContent = textContent;
            PrivateContent = privateContent;
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
        public void Advance(long sourceTick, bool clockWarning, bool processSuspended = false)
        {
            if (sourceTick == lastTick) return;
            if (sourceTick < lastTick || lastTick >= 0 && sourceTick != lastTick + 1)
                throw new SourceInterfaceException("The HUD needs every ordered source wake; render catch-up cannot skip clock/status ticks.");
            lastTick = sourceTick;
            if (processSuspended) return;
            if (clockWarning)
            {
                if (delay-- == 0) { RedClock = !RedClock; delay = 8; }
            }
            else if (RedClock) RedClock = false;
        }
        public void Clear() { lastTick = -1; delay = 8; RedClock = false; }
    }

    public sealed partial class SourceHudComposer
    {
        private const uint White = 0xffffffffu;
        private const uint Yellow = 0xffffff00u;
        private const uint Red = 0xffff0000u;
        private const uint AudibleGray = 0xff808080u;
        private readonly SourceInterfaceData data;
        private readonly SourceOriginalPresentationData original;
        private readonly List<SourceHudPrimitive> draw = new List<SourceHudPrimitive>();
        public SourceHudComposer(SourceInterfaceData data, SourceOriginalPresentationData original = null)
        { this.data = data ?? throw new ArgumentNullException(nameof(data)); this.original = original; }

        public IReadOnlyList<SourceHudPrimitive> Instructions(IEnumerable<SourceInstructionGraphic> graphics,
            SourceInstructionRaster raster, SourceHudViewport viewport)
        {
            if (graphics == null || raster.Width <= 0 || raster.Height <= 0)
                throw new SourceInterfaceException("Instruction projection requires its actual source raster and graphics.");
            double scale = Math.Min(viewport.ContentWidth / raster.Width, viewport.ContentHeight / raster.Height);
            double left = (viewport.ContentWidth - raster.Width * scale) / 2;
            double top = (viewport.ContentHeight - raster.Height * scale) / 2;
            var output = new List<SourceHudPrimitive>();
            foreach (SourceInstructionGraphic graphic in graphics.OrderByDescending(value => value.Depth))
            {
                if (graphic.Texture != "interface" && graphic.Texture != "interface-white")
                    throw new SourceInterfaceException("Unbound instruction atlas: " + graphic.Texture + ".");
                var vertices = graphic.Vertices.Select(value => new SourceHudVertex(
                    left + value.X * scale, top + value.Y * scale, value.U, value.V)).ToArray();
                output.Add(new SourceHudPrimitive("instructions/" + graphic.ImageSymbol, graphic.Texture,
                    graphic.Color, vertices, sourceDepth: graphic.Depth, sourceImageSymbol: graphic.ImageSymbol));
            }
            return output.AsReadOnly();
        }

        public IReadOnlyList<SourceHudPrimitive> Compose(IGameState state, SourceHudViewport viewport,
            SourceCabinetPlaySelection selection, bool redClock, bool suspended, SourceCabinetMenuView cabinetMenu = null)
        {
            draw.Clear();
            var hud = new SourceHudState(state, data);
            var layout = new SourceHudLayout(viewport, hud.Match.Stations.Count);
            if (cabinetMenu != null && cabinetMenu.Stage != SourceCabinetMenuStage.Playing)
            {
                CabinetMenu(cabinetMenu, viewport);
                if (suspended) ScreenText("adapter/pause", "bast18", "PAUSED - RELEASE CONTROLS", viewport, 16, 177, 480, 28, Yellow);
                return Finish(viewport);
            }
            if (hud.CabinetFlow != null && hud.CabinetFlow.Stage != SourceCabinetFlowStage.Inactive)
            {
                CabinetScreen(state, hud, viewport);
                if (suspended) ScreenText("adapter/pause", "bast18", "PAUSED - RELEASE CONTROLS", viewport, 16, 177, 480, 28, Yellow);
                return Finish(viewport);
            }
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
                if (hud.SelectionTimer != null && hud.SelectionTimer.Visible)
                {
                    double timerScale = Math.Min(layout.Menu.Width / (layout.Portrait ? 252 : 504), layout.Menu.Height / 350);
                    double timerX = layout.Portrait ? layout.Menu.Right - 16 * timerScale : layout.Menu.X + layout.Menu.Width / 2;
                    Text("selection/countdown", "bast18", hud.SelectionTimer.Number.ToString(),
                        timerX, layout.Menu.Y + 38 * timerScale, timerScale, hud.SelectionTimer.Color);
                }
            }
            else if (hud.PossessionMessage == null && hud.ResultGraphic != null && hud.Match.Phase != SourceMatchPhase.LiveBall)
            {
                SourceInterfaceImage image = data.Image(hud.ResultGraphic);
                double fit = Math.Min(layout.Message.Width / image.Width, layout.Message.Height / image.Height);
                var rect = new SourceHudRect(layout.Message.X + (layout.Message.Width - image.Width * fit) / 2,
                    layout.Message.Y, image.Width * fit, image.Height * fit);
                Image("result/" + hud.ResultGraphic, image, rect, White);
            }
            else if (hud.PossessionMessage == null && hud.StateText.Length > 0 && hud.Match.Phase != SourceMatchPhase.LiveBall && !hud.KickMeter.ShowMeterFlag)
                FitText("state", "bast18", hud.StateText, layout.Message, scale, White);
            KickMeter(hud.KickMeter, hud.KickMeterMetrics, viewport);
            foreach (SourcePossessionMessageView message in hud.PossessionMessages) PossessionMessage(message, viewport);
            AppendAudibles(hud.Match, viewport);
            if (suspended)
                FitText("adapter/pause", "bast18", "PAUSED - RELEASE CONTROLS", layout.Message, scale, Yellow);
            Controls(hud.Match, layout, hud.KickMeter.ShowMeterFlag);
            return Finish(viewport);
        }

        private IReadOnlyList<SourceHudPrimitive> Finish(SourceHudViewport viewport)
        {
            foreach (SourceHudPrimitive primitive in draw)
                if (!viewport.Content.Contains(primitive.Bounds))
                    throw new SourceInterfaceException("Original HUD element exceeds actual CSS content: " + primitive.Element + ".");
            return Array.AsReadOnly(draw.ToArray());
        }

        private static double ScreenScale(SourceHudViewport viewport)
        { return Math.Min(viewport.ContentWidth / 512, viewport.ContentHeight / 384); }

        private static SourceHudRect ScreenRect(SourceHudViewport viewport, double x, double y, double width, double height)
        {
            double scale = ScreenScale(viewport);
            return new SourceHudRect((viewport.ContentWidth - 512 * scale) / 2 + x * scale,
                (viewport.ContentHeight - 384 * scale) / 2 + y * scale, width * scale, height * scale);
        }

        private void ScreenText(string element, string font, string text, SourceHudViewport viewport,
            double x, double y, double width, double height, uint color = White)
        { FitText(element, font, text, ScreenRect(viewport, x, y, width, height), ScreenScale(viewport), color); }

        private void ScreenImage(string element, string symbol, SourceHudViewport viewport,
            double x, double y, double width, double height, bool fit = true)
        {
            SourceInterfaceImage image = data.Image(symbol);
            SourceHudRect rect = ScreenRect(viewport, x, y, width, height);
            if (fit)
            {
                double scale = Math.Min(rect.Width / image.Width, rect.Height / image.Height);
                rect = new SourceHudRect(rect.X + (rect.Width - image.Width * scale) / 2,
                    rect.Y + (rect.Height - image.Height * scale) / 2, image.Width * scale, image.Height * scale);
            }
            Image(element, image, rect, White);
        }

        private void OriginalImage(string element, string symbol, SourceHudViewport viewport,
            double x, double y, double width, double height, bool fit = false)
        {
            SourceOriginalRaster image = original.Image(symbol);
            SourceHudRect rect = ScreenRect(viewport, x, y, width, height);
            if (fit)
            {
                double scale = Math.Min(rect.Width / image.Width, rect.Height / image.Height);
                rect = new SourceHudRect(rect.X + (rect.Width - image.Width * scale) / 2,
                    rect.Y + (rect.Height - image.Height * scale) / 2, image.Width * scale, image.Height * scale);
            }
            draw.Add(new SourceHudPrimitive(element, "original/" + symbol, White, new[] {
                new SourceHudVertex(rect.X, rect.Y, 0, 1), new SourceHudVertex(rect.Right, rect.Y, 1, 1),
                new SourceHudVertex(rect.Right, rect.Bottom, 1, 0), new SourceHudVertex(rect.X, rect.Y, 0, 1),
                new SourceHudVertex(rect.Right, rect.Bottom, 1, 0), new SourceHudVertex(rect.X, rect.Bottom, 0, 0)
            }, sourceImageSymbol: symbol));
        }

        private void CabinetBackdrop(SourceHudViewport viewport, string title, string originalImage = null)
        {
            if (original != null && originalImage != null)
                OriginalImage("cabinet/backdrop/" + originalImage, originalImage, viewport, 0, 0, 512, 384);
            else ScreenImage("cabinet/backdrop", "sp_trns", viewport, 8, 8, 496, 368, false);
            ScreenImage("cabinet/title/plate", "playresult", viewport, 28, 8, 456, 37, false);
            ScreenText("cabinet/title", "bast18", title, viewport, 34, 12, 444, 28, Yellow);
        }

        private void ScorePlate(SourceHudState hud, SourceHudViewport viewport)
        {
            ScreenImage("cabinet/score/logo-back", "sp_logoback", viewport, 68, 248, 62, 80, false);
            ScreenImage("cabinet/score/home/bar", "sp_bar", viewport, 130, 248, 232, 40, false);
            ScreenImage("cabinet/score/away/bar", "sp_bar", viewport, 130, 288, 232, 40, false);
            ScreenImage("cabinet/score/end", "sp_end", viewport, 362, 248, 82, 80, false);
            ScreenImage("cabinet/score/logo", "sp_logo", viewport, 73, 252, 50, 72);
            ScreenText("cabinet/score/home/name", "bast18", hud.HomeName, viewport, 139, 253, 161, 30, Yellow);
            ScreenText("cabinet/score/home/value", "bast25", hud.HomeScore.ToString(), viewport, 307, 250, 81, 36, Yellow);
            ScreenText("cabinet/score/away/name", "bast18", hud.AwayName, viewport, 139, 293, 161, 30, Yellow);
            ScreenText("cabinet/score/away/value", "bast25", hud.AwayScore.ToString(), viewport, 307, 290, 81, 36, Yellow);
            ScreenImage("cabinet/score/clock-plate", "sp_clock", viewport, 187, 329, 138, 30, false);
            ScreenText("cabinet/score/clock", "bast10", hud.Clock, viewport, 193, 333, 126, 20);
            string period = hud.Match.Period.QuarterIndex == 0 ? "sp_1st" : hud.Match.Period.QuarterIndex == 1 ? "sp_2nd" :
                hud.Match.Period.QuarterIndex == 2 ? "sp_3rd" : hud.Match.Period.QuarterIndex == 3 ? "sp_4th" : "sp_ot";
            ScreenImage("cabinet/score/quarter", period, viewport, 334, 335, 100, 19);
        }

        private void CabinetScreen(IGameState state, SourceHudState hud, SourceHudViewport viewport)
        {
            SourceCabinetFlowView flow = hud.CabinetFlow;
            switch (flow.Stage)
            {
                case SourceCabinetFlowStage.ResultWait:
                    CabinetBackdrop(viewport, "PLAY RESULT");
                    if (hud.ResultGraphic != null)
                        ScreenImage("cabinet/result/" + hud.ResultGraphic, hud.ResultGraphic, viewport, 56, 61, 400, 73);
                    ScreenImage("cabinet/result/plate", "playresult", viewport, 90, 145, 332, 87, false);
                    ScreenText("cabinet/result/kind", "bast18", ResultText(hud.Match.Result), viewport, 99, 153, 314, 28, Yellow);
                    string detail = hud.Match.Result.Kind == SourceResultKind.Touchback ? "BALL GOES TO THE 20 YARD LINE" :
                        state.Down > 0 && state.Down <= 4 ? "NEXT DOWN: " + state.Down : hud.StateText;
                    ScreenText("cabinet/result/detail", "bast10", detail, viewport, 99, 191, 314, 22);
                    ScorePlate(hud, viewport);
                    ScreenText("cabinet/prompt", "bast8t", "HOLD A+B+C TO ADVANCE AFTER THE SOURCE MINIMUM WAIT", viewport, 16, 364, 480, 14);
                    return;
                case SourceCabinetFlowStage.EndOfQuarter:
                    CabinetBackdrop(viewport, "END OF PERIOD");
                    int quarter = flow.CompletedQuarterIndex ?? hud.Match.Period.QuarterIndex;
                    if (quarter < 3)
                    {
                        ScreenImage("cabinet/quarter/end", "msg_end", viewport, 97, 63, 318, 64);
                        ScreenImage("cabinet/quarter/ordinal", quarter == 0 ? "sp_1st" : quarter == 1 ? "sp_2nd" : "sp_3rd",
                            viewport, 51, 142, 91, 42);
                        ScreenImage("cabinet/quarter/word", "msg_qtr", viewport, 151, 132, 310, 64);
                    }
                    else if (hud.HomeScore != hud.AwayScore)
                    {
                        if (original != null)
                        {
                            int team = hud.HomeScore > hud.AwayScore ? hud.Match.Configuration.HomeTeam.TeamIndex :
                                hud.Match.Configuration.AwayTeam.TeamIndex;
                            OriginalImage("cabinet/winner/team", original.TeamWins[team], viewport, 52, 69, 408, 48, true);
                            OriginalImage("cabinet/winner/art", "win_win", viewport, 49, 129, 414, 64, true);
                        }
                        else
                        {
                            ScreenText("cabinet/winner/team", "bast25", hud.HomeScore > hud.AwayScore ? hud.HomeName : hud.AwayName,
                                viewport, 52, 69, 408, 48, Yellow);
                            ScreenImage("cabinet/winner/art", "msg_win", viewport, 49, 129, 414, 64);
                        }
                    }
                    else if (quarter == 6)
                        ScreenText("cabinet/quarter/tie", "bast25", "TIE GAME!", viewport, 48, 106, 416, 62, Yellow);
                    else ScreenImage("cabinet/quarter/overtime", "msg_ovr", viewport, 49, 107, 414, 70);
                    ScorePlate(hud, viewport);
                    ScreenText("cabinet/prompt", "bast8t", "HOLD A+B+C TO ADVANCE AFTER THE SOURCE MINIMUM WAIT", viewport, 16, 364, 480, 14);
                    return;
                case SourceCabinetFlowStage.CoachingTip:
                    if (!flow.CoachingTipOrdinal.HasValue || !flow.CoachingBackgroundOrdinal.HasValue)
                        throw new SourceInterfaceException("Coaching presentation requires the two already-committed source random ordinals.");
                    CabinetBackdrop(viewport, "COACHING TIP", "cheerpg" + (flow.CoachingBackgroundOrdinal.Value + 1));
                    ReadOnlyCollection<string> lines = SourceCabinetCoachingText.Lines(flow.CoachingTipOrdinal.Value);
                    if (original == null) ScreenImage("cabinet/coaching/plate", "playresult", viewport, 20, 64, 472, 252, false);
                    ScreenText("cabinet/coaching/title", "bast18", lines[0], viewport, 28, 78, 456, 34, Red);
                    for (int line = 1; line < lines.Count; line++)
                        ScreenText("cabinet/coaching/line/" + line, "bast10", lines[line], viewport, 27, 119 + line * 23, 458, 20);
                    if (original == null)
                        ScreenText("cabinet/coaching/art-limit", "bast8t", "ORIGINAL TIP TEXT - CHEER BACKGROUND ART NOT ADMITTED", viewport, 16, 327, 480, 14);
                    ScreenText("cabinet/prompt", "bast10", "PRESS A TO CONTINUE", viewport, 16, 350, 480, 20, Yellow);
                    return;
                case SourceCabinetFlowStage.ContinuationOffer:
                    CabinetBackdrop(viewport, "CONTINUE GAME");
                    ScreenText("cabinet/continuation/policy", "bast13",
                        flow.Configuration.ContinuationPolicy == SourceCabinetContinuationPolicy.RequireConfirmation ?
                            "CONFIRM FREE-PLAY CONTINUATION" : "FREE PLAY - SAME HUMAN STATIONS",
                        viewport, 24, 70, 464, 34, Yellow);
                    ScreenImage("cabinet/continuation/plate", "playresult", viewport, 155, 124, 202, 100, false);
                    if (flow.ContinuationNumberVisible && flow.ContinuationSeconds.HasValue)
                        ScreenText("cabinet/continuation/number", "bast25", flow.ContinuationSeconds.Value.ToString(),
                            viewport, 166, 135, 180, 59, Yellow);
                    ScreenText("cabinet/continuation/confirmation", "bast8t",
                        flow.WaitingForButtonRelease ? "RELEASE ALL BUTTONS BEFORE CONFIRMING" : "PRESS A TO CONFIRM",
                        viewport, 24, 215, 464, 20);
                    ScorePlate(hud, viewport);
                    return;
                case SourceCabinetFlowStage.HalftimeStats:
                case SourceCabinetFlowStage.FinalStats:
                case SourceCabinetFlowStage.FinalConfirmation:
                case SourceCabinetFlowStage.NewMatchRequested:
                    if (hud.Statistics == null) throw new SourceInterfaceException("A committed statistics screen requires its actual source match.");
                    string prompt = flow.Stage == SourceCabinetFlowStage.FinalConfirmation ?
                        flow.WaitingForButtonRelease ? "RELEASE ALL BUTTONS" : "PRESS A FOR NEXT MATCH OPTIONS" :
                        flow.Stage == SourceCabinetFlowStage.NewMatchRequested ? "NEXT MATCH OPTIONS" : "PRESS A AFTER THE SOURCE MINIMUM WAIT";
                    StatisticsScreen(hud.Statistics, viewport,
                        flow.Stage == SourceCabinetFlowStage.HalftimeStats ? "HALFTIME STATISTICS" : "FINAL STATISTICS", prompt);
                    return;
                case SourceCabinetFlowStage.RecordCommitWait:
                    CabinetBackdrop(viewport, "FINAL RECORDS", "finalstat");
                    ScreenText("cabinet/records/continuation", "bast13", "WAITING FOR SOURCE SAVE CONTINUATION",
                        viewport, 28, 158, 456, 45, Yellow);
                    return;
                default: throw new SourceInterfaceException("Unspecified active cabinet screen.");
            }
        }

        private static string ResultText(SourceMatchResult result)
        {
            switch (result.Kind)
            {
                case SourceResultKind.Down: return result.TurnoverOnDowns ? "TURNOVER ON DOWNS" : result.JustGotFirstDown ? "FIRST DOWN" : "PLAY COMPLETE";
                case SourceResultKind.Touchdown: return "TOUCHDOWN!";
                case SourceResultKind.Safety: return "SAFETY";
                case SourceResultKind.PatSafety: return "PAT SAFETY";
                case SourceResultKind.NewPossession: return "NEW POSSESSION";
                case SourceResultKind.Incomplete: return "INCOMPLETE";
                case SourceResultKind.FumbledOutOfBounds: return "FUMBLE OUT OF BOUNDS";
                case SourceResultKind.FieldGoal: return "FIELD GOAL IS GOOD";
                case SourceResultKind.ExtraPoint: return "EXTRA POINT IS GOOD";
                case SourceResultKind.MissedKick: return "NO GOOD";
                case SourceResultKind.FailedConversion: return "CONVERSION NO GOOD";
                case SourceResultKind.Conversion: return "TWO POINT CONVERSION";
                case SourceResultKind.PuntOutOfBounds: return "PUNT OUT OF BOUNDS";
                case SourceResultKind.DownedPunt: return "PUNT DOWNED";
                case SourceResultKind.KickoffOutOfBounds: return "KICKOFF OUT OF BOUNDS";
                case SourceResultKind.Touchback: return "TOUCHBACK";
                default: throw new SourceInterfaceException("No classified source result to present.");
            }
        }

        private void StatisticsScreen(SourceCabinetStatistics stats, SourceHudViewport viewport, string title, string prompt)
        {
            CabinetBackdrop(viewport, title, title == "HALFTIME STATISTICS" ? "halfstat" : "finalstat");
            string home = data.TeamAbbreviations[stats.Match.Configuration.HomeTeam.TeamIndex];
            string away = data.TeamAbbreviations[stats.Match.Configuration.AwayTeam.TeamIndex];
            ScreenText("cabinet/stats/home/name", "bast13", home, viewport, 22, 46, 120, 22, Yellow);
            ScreenText("cabinet/stats/away/name", "bast13", away, viewport, 370, 46, 120, 22, Yellow);
            ScreenText("cabinet/stats/home/score", "bast25", stats.HomeScore.ToString(), viewport, 22, 65, 120, 34, Yellow);
            ScreenText("cabinet/stats/away/score", "bast25", stats.AwayScore.ToString(), viewport, 370, 65, 120, 34, Yellow);
            bool final = stats.Match.Phase == SourceMatchPhase.MatchEnded;
            ScreenText("cabinet/stats/outcome", "bast13", final ?
                stats.Winner.HasValue ? (stats.Winner.Value == TeamId.Home ? home : away) + " WINS!" : "TIE GAME!" :
                "HALFTIME", viewport, 151, 57, 210, 30, Yellow);
            for (int quarter = 0; quarter < 7; quarter++)
            {
                double x = 81 + quarter * 46;
                ScreenText("cabinet/stats/quarter/" + quarter + "/label", "bast8t",
                    quarter < 4 ? "Q" + (quarter + 1) : "OT" + (quarter - 3), viewport, x, 101, 43, 12);
                ScreenText("cabinet/stats/quarter/" + quarter + "/home", "bast8t", stats.QuarterScore(TeamId.Home, quarter), viewport, x, 113, 43, 12);
                ScreenText("cabinet/stats/quarter/" + quarter + "/away", "bast8t", stats.QuarterScore(TeamId.Away, quarter), viewport, x, 125, 43, 12);
            }
            ScreenText("cabinet/stats/quarter/home", "bast8t", home, viewport, 15, 113, 61, 12, Yellow);
            ScreenText("cabinet/stats/quarter/away", "bast8t", away, viewport, 15, 125, 61, 12, Yellow);
            ScreenText("cabinet/stats/quarter/total", "bast8t", "TOTAL", viewport, 409, 101, 88, 12, Yellow);
            ScreenText("cabinet/stats/quarter/home-total", "bast8t", stats.HomeScore.ToString(), viewport, 409, 113, 88, 12, Yellow);
            ScreenText("cabinet/stats/quarter/away-total", "bast8t", stats.AwayScore.ToString(), viewport, 409, 125, 88, 12, Yellow);
            for (int row = 0; row < stats.Rows.Count; row++)
            {
                SourceCabinetStatRow value = stats.Rows[row];
                double y = 144 + row * 11.25;
                ScreenText("cabinet/stats/" + value.Key + "/home", "bast8t", value.Home, viewport, 19, y, 105, 11);
                ScreenText("cabinet/stats/" + value.Key + "/label", "bast8t", value.Label, viewport, 130, y, 252, 11, Yellow);
                ScreenText("cabinet/stats/" + value.Key + "/away", "bast8t", value.Away, viewport, 388, y, 105, 11);
            }
            ScreenText("cabinet/stats/availability", "bast8t", "-- MEANS UNAVAILABLE / UNPLAYED, NOT ZERO", viewport, 16, 331, 480, 14);
            ScreenText("cabinet/prompt", "bast10", prompt, viewport, 16, 352, 480, 20, Yellow);
        }

        private void CabinetMenu(SourceCabinetMenuView menu, SourceHudViewport viewport)
        {
            if (menu.Stage == SourceCabinetMenuStage.NextMatch && menu.PreviousMatch != null)
            {
                StatisticsScreen(menu.PreviousMatch, viewport, "FINAL STATISTICS", "");
                ScreenImage("cabinet/session/next/plate", "playresult", viewport, 18, 328, 476, 48, false);
                ScreenText("cabinet/session/next/choices", "bast13",
                    menu.WaitingForNeutral ? "RELEASE ALL CONTROLS" : "A REMATCH   B NEW TEAMS / PLAYERS",
                    viewport, 24, 337, 464, 27, Yellow);
                return;
            }
            string title = menu.Stage == SourceCabinetMenuStage.Joining ? "FREE PLAY - JOIN CABINET" :
                menu.Stage == SourceCabinetMenuStage.Teams ? "CHOOSE TEAMS" :
                menu.Stage == SourceCabinetMenuStage.Rosters ? "CHOOSE SEVEN ROSTER SLOTS" : "VERSUS - MATCH CONFIGURATION";
            CabinetBackdrop(viewport, title);
            SourceCabinetMenuSide[] sides = { menu.Home, menu.Away };
            for (int side = 0; side < 2; side++)
            {
                double x = 18 + side * 256;
                SourceCabinetMenuSide value = sides[side];
                ScreenText("cabinet/session/" + side + "/side", "bast10", side == 0 ? "HOME" : "AWAY", viewport, x, 51, 220, 18, Yellow);
                ScreenText("cabinet/session/" + side + "/city", "bast13", value.City, viewport, x, 72, 220, 23);
                ScreenText("cabinet/session/" + side + "/team", "bast18", value.Name, viewport, x, 97, 220, 29, value.Editing ? Yellow : White);
                ScreenText("cabinet/session/" + side + "/stations", "bast8t", value.Stations, viewport, x, 130, 220, 14);
                if (menu.Stage == SourceCabinetMenuStage.Teams || menu.Stage == SourceCabinetMenuStage.Rosters)
                    ScreenText("cabinet/session/" + side + "/ready", "bast8t", value.Ready ? "READY" : "A TO CONFIRM",
                        viewport, x, 146, 220, 15, value.Ready ? 0xff00ff00u : Yellow);
                if (menu.Stage == SourceCabinetMenuStage.Rosters)
                    for (int slot = 0; slot < value.Roster.Count; slot++)
                        ScreenText("cabinet/session/" + side + "/roster/" + slot, "bast8t", value.Roster[slot],
                            viewport, x, 170 + slot * 17, 220, 16,
                            value.Editing && slot == value.SelectedSlot ? Yellow : White);
            }
            if (menu.Stage == SourceCabinetMenuStage.Joining)
                for (int station = 0; station < 4; station++)
                {
                    string keys = station == 0 ? "WASD  V/B/N" : station == 1 ? "ARROWS  DEL/END/PGDN" :
                        station == 2 ? "IJKL  U/O/P" : "TFGH  Z/X/C";
                    string joined = (menu.ParticipatingStationMask & 1 << station) != 0 ? "JOINED" : "A TO JOIN";
                    ScreenText("cabinet/session/join/" + station, "bast10",
                        "P" + (station + 1) + " " + (station < 2 ? "HOME" : "AWAY") + " - " + joined + " - " + keys,
                        viewport, 24, 177 + station * 25, 464, 22, (menu.ParticipatingStationMask & 1 << station) != 0 ? Yellow : White);
                }
            else if (menu.Stage == SourceCabinetMenuStage.Teams)
            {
                ScreenText("cabinet/session/team-navigation", "bast13", "LEFT / RIGHT: 31 ORIGINAL TEAMS", viewport, 24, 190, 464, 26);
                ScreenText("cabinet/session/cpu-navigation", "bast10", "C: EDIT CPU SIDE WHEN IT HAS NO HUMAN", viewport, 24, 225, 464, 22);
                ScreenText("cabinet/session/team-defaults", "bast8t", "DEFAULTS ARE EXPLICIT SESSION CHOICES, NOT SAVED RECORDS", viewport, 24, 262, 464, 18);
            }
            else if (menu.Stage == SourceCabinetMenuStage.Versus)
            {
                for (int setting = 0; setting < menu.Settings.Count; setting++)
                    ScreenText("cabinet/session/setting/" + setting, "bast10", menu.Settings[setting],
                        viewport, 38, 163 + setting * (menu.Settings.Count > 5 ? 20 : 22), 436, 20,
                        setting == menu.SelectedSetting ? Yellow : White);
                ScreenText("cabinet/session/play-controls", "bast8t", "PLAY SELECT: B CHOOSE   A FLIP   C PAGE", viewport, 24, 283, 464, 15);
            }
            ScreenText("cabinet/session/audibles", "bast8t", menu.DefaultAudiblesEnabled ?
                "DEFAULT AUDIBLES: ADMITTED SOURCE COMPOSITION" : "DEFAULT AUDIBLES: UNAVAILABLE IN THIS COMPOSITION",
                viewport, 14, 304, 484, 13);
            ScreenText("cabinet/session/records", "bast8t", "NEW PLAYERS - NO SAVED CUSTOM PLAYS OR EXPERIENCE", viewport, 14, 320, 484, 13);
            ScreenText("cabinet/session/venue", "bast8t", "FIXED VENUE: FIELD0 / STADIUM0 / BEA", viewport, 14, 336, 484, 13);
            string instructions = menu.Stage == SourceCabinetMenuStage.Joining ? "A JOIN   B LEAVE   C CHOOSE TEAMS" :
                menu.Stage == SourceCabinetMenuStage.Teams ? "A READY   B JOIN / LEAVE   C CPU SIDE" :
                menu.Stage == SourceCabinetMenuStage.Rosters ? "UP/DOWN SLOT  LEFT/RIGHT RECORD  A READY  B BACK  C CPU" :
                "UP/DOWN SETTING  LEFT/RIGHT CHANGE  A START  B BACK";
            ScreenText("cabinet/session/prompt", "bast10", menu.WaitingForNeutral ? "RELEASE ALL CONTROLS" : instructions,
                viewport, 14, 355, 484, 22, Yellow);
        }

        public IReadOnlyList<SourceHudPrimitive> ComposeAudibles(SourceMatchView match, SourceHudViewport viewport)
        {
            if (match == null) throw new ArgumentNullException(nameof(match));
            draw.Clear();
            AppendAudibles(match, viewport);
            return Array.AsReadOnly(draw.ToArray());
        }

        private void AppendAudibles(SourceMatchView match, SourceHudViewport viewport)
        {
            SourceAudibleView view = match.Audibles;
            if (view == null) return;
            AudibleTeam(match, view, view.HomeTeam, viewport);
            AudibleTeam(match, view, view.AwayTeam, viewport);
        }

        private void AudibleTeam(SourceMatchView match, SourceAudibleView view, SourceAudibleTeamView team,
            SourceHudViewport viewport)
        {
            if (team.Action != 1 || !team.BackgroundSpriteObjectSerial.HasValue) return;
            SourceAudibleSpriteView sprite = null;
            foreach (SourceAudibleSpriteView candidate in view.Sprites)
                if (candidate.ObjectSerial == team.BackgroundSpriteObjectSerial.Value) { sprite = candidate; break; }
            if (sprite == null || sprite.Team != team.Team || sprite.ImageSymbol != "aud_menu" ||
                sprite.Position.X != (team.Team == TeamId.Home ? 70f : 442f) ||
                sprite.Position.Y != 192f || sprite.Position.Z != 1.4f)
                throw new SourceInterfaceException("The audible HUD requires the SAME actual team aud_menu object and original source position.");
            int hres = data.DefaultAudibleHorizontalResolution, vres = data.DefaultAudibleVerticalResolution;
            double scale = Math.Min(viewport.ContentWidth / hres, viewport.ContentHeight / vres);
            double offsetX = (viewport.ContentWidth - hres * scale) / 2;
            double offsetY = (viewport.ContentHeight - vres * scale) / 2;
            double x = offsetX + sprite.Position.X * scale;
            double y = offsetY + (vres - sprite.Position.Y) * scale;
            string element = "audible/" + team.Team;
            if (sprite.Exists && sprite.DrawVisible)
            {
                string texture;
                if (sprite.ColorCombinerFunction == 0x10 && sprite.ChromaKeyMode == 1)
                    texture = "interface-white";
                else if (sprite.ColorCombinerFunction == 0x04 && sprite.ChromaKeyMode == 1)
                    texture = "interface-chroma";
                else if (sprite.ColorCombinerFunction == 0x04 && sprite.ChromaKeyMode == 0)
                    texture = "interface";
                else
                    throw new SourceInterfaceException("SourceHudComposer cannot represent audible combiner/chroma " +
                        sprite.ColorCombinerFunction + "/" + sprite.ChromaKeyMode + "; no tint/normal-restore guess is permitted.");
                SourceInterfaceImage image = data.Image(sprite.ImageSymbol);
                Image(element + "/background", image,
                    new SourceHudRect(x - image.AnchorX * scale, y - image.AnchorY * scale,
                        image.Width * scale, image.Height * scale), White, texture: texture,
                    audibles: view, audibleSprite: sprite, sourceDepth: sprite.Position.Z);
            }
            SourceAudibleRoleView role = view.Offense.Team == team.Team ? view.Offense : view.Defense;
            int sourceTeam = team.Team == TeamId.Home ? match.Configuration.HomeTeam.TeamIndex :
                match.Configuration.AwayTeam.TeamIndex;
            Text(element + "/team", "bast10", data.DefaultAudibleTeamName(sourceTeam),
                x, y - 36 * scale, scale, Yellow, view, 1.2f);
            Text(element + "/original", "bast8t", "ORIGINAL PLAY",
                x, y - 11 * scale, scale, role.Choice == 0 ? Yellow : AudibleGray, view, 1.2f);
            for (int choice = 1; choice <= 3; choice++)
                Text(element + "/choice/" + choice, "bast8t", "AUDIBLE " + choice,
                    x, y + (4 + (choice - 1) * 15) * scale, scale,
                    role.Choice == choice ? Yellow : AudibleGray, view, 1.2f);
        }

        private void PossessionMessage(SourcePossessionMessageView message, SourceHudViewport viewport)
        {
            if (message == null) return;
            if (message.HorizontalResolution != 512 || message.VerticalResolution != 256 && message.VerticalResolution != 384)
                throw new SourceInterfaceException("Possession imagery requires the actual original sprite raster.");
            double scale = Math.Min(viewport.ContentWidth / message.HorizontalResolution,
                viewport.ContentHeight / message.VerticalResolution);
            double offsetX = (viewport.ContentWidth - message.HorizontalResolution * scale) / 2;
            double offsetY = (viewport.ContentHeight - message.VerticalResolution * scale) / 2;
            if (message.MessageNumber == 15)
            {
                if (message.Sprites.Count != 0) throw new SourceInterfaceException("Original punt return has no message sprites.");
                return;
            }
            if (message.Sprites.Count != 2)
                throw new SourceInterfaceException("The original possession message requires both actual object/flash continuations.");
            SourcePossessionSprite sprite = message.Sprites[0], component = message.Sprites[1];
            string symbol = message.MessageNumber == 8 ? "msg_int" : message.MessageNumber == 13 ? "msg_trn" : null;
            if (symbol == null || sprite.Image != symbol || component.Image != symbol + "_c1" ||
                sprite.ObjectId != component.ObjectId || sprite.WhiteFlash != component.WhiteFlash ||
                sprite.ChromaKey != component.ChromaKey)
                throw new SourceInterfaceException("Independent native Composit component geometry is unavailable; unequal component state cannot be replaced by a joined authored draw.");
            // The pinned atlas retains the WHOLE original WIF image, not generated
            // flash.h components. Draw that union once; never duplicate the image
            // as an invented _c1 or guess native Composit cut/packing coordinates.
            SourceInterfaceImage image = data.Image(symbol);
            var rect = new SourceHudRect(offsetX + (message.HorizontalResolution / 2 - image.AnchorX) * scale,
                offsetY + (message.VerticalResolution / 2 - 55 - image.AnchorY) * scale,
                image.Width * scale, image.Height * scale);
            Image("possession/" + sprite.Image + "+" + component.Image, image, rect, White,
                texture: sprite.WhiteFlash && sprite.ChromaKey ? "interface-white" : "interface");
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
            for (int slot = 0; slot < 9; slot++)
            {
                double left = x + slot % 3 * 83 * scale, top = y + (52 + slot / 3 * 96) * scale;
                if (selection.Page(side) == 4 && side == match.Possession.Offense)
                {
                    SourceCustomPlay custom = selection.CustomPlay(state, side, slot);
                    Plane("selection/" + side + "/blank/" + slot, "limb_blank", left, top, scale, White);
                    if (custom != null)
                        RecordDiagram("selection/" + side + "/custom/" + slot, custom,
                            new SourceHudRect(left + 6 * scale, top + 5 * scale, 69 * scale, 69 * scale), selection.Flipped);
                    FitText("selection/" + side + "/name/" + slot, "bast8t",
                        custom == null ? "EMPTY" : (custom.Name1 + " " + custom.Name2).ToUpperInvariant(),
                        new SourceHudRect(left + scale, top + 81 * scale, 81 * scale, 14 * scale), scale, White);
                    continue;
                }
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
            double fraction = 1, bool flipped = false, string texture = "interface",
            SourceAudibleView audibles = null, SourceAudibleSpriteView audibleSprite = null, float? sourceDepth = null,
            string textContent = null, bool privateContent = false)
        {
            SourceInterfaceTexture atlas = data.Textures["interface"];
            double s0 = image.X / (double)atlas.Width, s1 = (image.X + image.Width * fraction) / atlas.Width;
            double t0 = image.Y / (double)atlas.Height, t1 = (image.Y + image.Height) / (double)atlas.Height;
            if (flipped) { double swap = s0; s0 = s1; s1 = swap; }
            draw.Add(new SourceHudPrimitive(element, texture, color, new[] {
                new SourceHudVertex(rect.X, rect.Y, s0, t1), new SourceHudVertex(rect.Right, rect.Y, s1, t1),
                new SourceHudVertex(rect.Right, rect.Bottom, s1, t0), new SourceHudVertex(rect.X, rect.Y, s0, t1),
                new SourceHudVertex(rect.Right, rect.Bottom, s1, t0), new SourceHudVertex(rect.X, rect.Bottom, s0, t0) },
                audibles, audibleSprite, sourceDepth, image.Id, privateContent ? null : textContent, privateContent));
        }

        private void Text(string element, string font, string text, double x, double y, double scale, uint color,
            SourceAudibleView audibles = null, float? sourceDepth = null)
        {
            foreach (SourceHudGlyph glyph in SourceHudText.Center(data.Font(font), text, x, y, scale))
                Image(element, glyph.Image, glyph.Rect, color, audibles: audibles, sourceDepth: sourceDepth, textContent: text);
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
