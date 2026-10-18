using System;
using System.Collections.Generic;
using Blitz.AssetImport;
using Blitz.Contracts;
using Blitz.Runtime;

namespace Blitz.Presentation
{
    public readonly struct SourceHudGlyph
    {
        public SourceInterfaceImage Image { get; }
        public SourceHudRect Rect { get; }
        internal SourceHudGlyph(SourceInterfaceImage image, SourceHudRect rect) { Image = image; Rect = rect; }
    }

    public static class SourceHudText
    {
        public static double Width(SourceInterfaceFont font, string text)
        {
            if (font == null || text == null) throw new ArgumentNullException(font == null ? nameof(font) : nameof(text));
            double width = 0;
            foreach (char c in text)
                width += c == ' ' ? font.SpaceWidth : font.Glyph(c).Width + font.CharacterSpacing;
            return width;
        }

        public static IReadOnlyList<SourceHudGlyph> Center(SourceInterfaceFont font, string text,
            double x, double y, double scale)
        {
            if (scale <= 0 || double.IsNaN(scale) || double.IsInfinity(scale)) throw new ArgumentOutOfRangeException(nameof(scale));
            double width = Width(font, text), height = 0;
            foreach (char c in text)
                if (c != ' ') height = Math.Max(height, font.Glyph(c).Height);
            // Goose truncates the HALF, not the final scaled/positioned string.
            double startX = x - Math.Truncate(width * scale / 2);
            double top = y - Math.Truncate(height * scale / 2);
            var output = new List<SourceHudGlyph>();
            double cursor = 0;
            foreach (char c in text)
            {
                if (c == ' ') { cursor += font.SpaceWidth * scale; continue; }
                SourceInterfaceImage glyph = font.Glyph(c);
                output.Add(new SourceHudGlyph(glyph, new SourceHudRect(startX + cursor - glyph.AnchorX * scale,
                    top - glyph.AnchorY * scale, glyph.Width * scale, glyph.Height * scale)));
                cursor += (glyph.Width + font.CharacterSpacing) * scale;
            }
            return output.AsReadOnly();
        }
    }

    public readonly struct SourceHudState
    {
        public SourceMatchView Match { get; }
        public string Clock { get; }
        public string Period { get; }
        public string HomeName { get; }
        public string AwayName { get; }
        public int HomeScore { get; }
        public int AwayScore { get; }
        public TeamId HighlightedSide { get; }
        public bool ClockActive { get; }
        public bool ClockWarning { get; }
        public string ResultGraphic { get; }
        public string StateText { get; }
        public int SelectionCountdown { get; }
        public SourceKickMeterMetrics KickMeterMetrics { get; }
        public SourceKickMeterView KickMeter { get; }

        public SourceHudState(IGameState state, SourceInterfaceData data)
        {
            if (state == null || data == null) throw new ArgumentNullException(state == null ? nameof(state) : nameof(data));
            var source = state as ISourceMatchState;
            if (source == null || source.Match == null || source.Match.Configuration.Profile != SourceSimulationProfile.OriginalRelease57Hz)
                throw new SourceInterfaceException("Original HUD requires an actual source-match snapshot; legacy clock/text is not a source-state substitute.");
            Match = source.Match;
            var kicking = state as ISourceKickMeterState;
            KickMeterMetrics = kicking == null ? null : kicking.KickMeterMetrics;
            KickMeter = kicking == null ? default(SourceKickMeterView) : kicking.KickMeter;
            if (KickMeter.IsSpecified && (KickMeterMetrics == null
                || kicking.KickMeterMode != SourceKickMeterMode.PortableAuthored))
                throw new SourceInterfaceException("A kick-meter HUD snapshot requires its explicit portable-authored metric provenance.");
            if (!Match.Period.IsSpecified || !Match.Possession.IsSpecified)
                throw new SourceInterfaceException("Original HUD requires actual period/possession DTO values.");
            int minute = (int)((source.PackedGameTime >> 16) & 255), second = (int)((source.PackedGameTime >> 8) & 255);
            if (second > 59) throw new SourceInterfaceException("Invalid source packed clock seconds.");
            Clock = " " + minute + ":" + second.ToString("00");
            int quarter = Match.Period.QuarterIndex;
            Period = quarter == 0 ? "1ST" : quarter == 1 ? "2ND" : quarter == 2 ? "3RD" : quarter == 3 ? "4TH" : "OT" + (quarter - 3);
            HomeName = data.TeamAbbreviations[Match.Configuration.HomeTeam.TeamIndex];
            AwayName = data.TeamAbbreviations[Match.Configuration.AwayTeam.TeamIndex];
            HomeScore = state.HomeScore; AwayScore = state.AwayScore;
            HighlightedSide = Match.PlayKind == SourcePlayKind.Kickoff
                ? (Match.Possession.Offense == TeamId.Home ? TeamId.Away : TeamId.Home) : Match.Possession.Offense;
            ClockActive = source.ClockActive;
            ClockWarning = (source.PackedGameTime & 0x00ffff00u) <= 0x00001f00u && (quarter == 1 || quarter >= 3);
            SelectionCountdown = Match.SelectionCountdown;
            ResultGraphic = ResultImage(Match.Result);
            StateText = Describe(Match);
        }

        private static string ResultImage(SourceMatchResult result)
        {
            if (!result.IsSpecified || result.Status.HasValue && result.Status.Value != SourceResultStatus.Classified)
                return null;
            switch (result.Kind)
            {
                case SourceResultKind.Touchdown: return "msg_tdn";
                case SourceResultKind.Safety:
                case SourceResultKind.PatSafety: return "msg_saf";
                case SourceResultKind.Conversion: return "msg_2pt";
                case SourceResultKind.FieldGoal:
                case SourceResultKind.ExtraPoint: return "msg_gd";
                case SourceResultKind.MissedKick:
                case SourceResultKind.FailedConversion: return "msg_ngd";
                default: return result.TurnoverOnDowns ? "msg_trn" : result.JustGotFirstDown ? "msg_1dn" : null;
            }
        }

        private static string Describe(SourceMatchView match)
        {
            switch (match.Phase)
            {
                case SourceMatchPhase.PatSelection:
                    if (match.PatStatus == SourcePatStatus.AwaitHumanSelection) return "FREE POINT OR TWO POINT PLAY";
                    return "PAT " + match.PatStatus.ToString().ToUpperInvariant();
                case SourceMatchPhase.FreePointResult:
                    if (!match.Result.IsSpecified) throw new SourceInterfaceException("Free-point HUD result is unspecified.");
                    return match.Result.Kind == SourceResultKind.ExtraPoint ? "EXTRA POINT IS GOOD" : "EXTRA POINT NO GOOD";
                case SourceMatchPhase.PlaySelection: return "SELECT PLAY";
                case SourceMatchPhase.PreSnap:
                    if (match.PlayKind == SourcePlayKind.Kickoff)
                    {
                        if (!match.Kickoff.HasValue || !match.KickingTeam.HasValue || !match.ReceivingTeam.HasValue)
                            throw new SourceInterfaceException("Kickoff HUD requires the actual kickoff and side DTOs.");
                        return match.Kickoff.Value == SourceKickoffType.Onside ? "ONSIDE KICK" : "KICKOFF RETURN";
                    }
                    return match.PlayKind == SourcePlayKind.FieldGoal ? "FIELD GOAL" : match.PlayKind == SourcePlayKind.Punt ? "PUNT" : "READY";
                case SourceMatchPhase.PlayResult:
                    if (!match.Result.IsSpecified) throw new SourceInterfaceException("Play-result HUD requires the actual source result.");
                    if (match.Result.Status == SourceResultStatus.IntentionalNoOp) return "SOURCE RESULT INTENTIONAL NO-OP";
                    if (match.Result.Status == SourceResultStatus.InvalidSourceBranch) return "SOURCE RESULT INVALID BRANCH";
                    return match.Result.Kind.ToString().ToUpperInvariant();
                case SourceMatchPhase.PeriodTransition:
                    switch (match.Period.Status)
                    {
                        case SourcePeriodStatus.HalftimeKickoff: return "HALFTIME";
                        case SourcePeriodStatus.OvertimeKickoff:
                        case SourcePeriodStatus.AwaitOvertimeDraw: return "OVERTIME";
                        case SourcePeriodStatus.DeferredForExtraPoint: return "EXTRA POINT";
                        default: return "END OF QUARTER";
                    }
                case SourceMatchPhase.MatchEnded: return "FINAL";
                case SourceMatchPhase.LiveBall: return "";
                default: throw new SourceInterfaceException("Source HUD phase is unspecified.");
            }
        }
    }

    public sealed class SourceHudEventJournal
    {
        private readonly Queue<SourceGameEvent> pending = new Queue<SourceGameEvent>();
        private long lastHostTick = -1;
        private long lastSourceTick = -1;
        public long AcceptedCount { get; private set; }
        public int PendingCount { get { return pending.Count; } }

        public void Append(SourceGameEvent value)
        {
            if (!value.IsSpecified || value.Tick < lastHostTick || value.SourceTick < lastSourceTick)
                throw new SourceInterfaceException("Out-of-order/unspecified source HUD event; render frames cannot reorder source steps.");
            if (pending.Count >= 16384)
                throw new SourceInterfaceException("Source HUD event consumer stalled; refusing to drop ordered events.");
            lastHostTick = value.Tick; lastSourceTick = value.SourceTick;
            pending.Enqueue(value); AcceptedCount++;
        }

        public void Drain(Action<SourceGameEvent> consume)
        {
            if (consume == null) throw new ArgumentNullException(nameof(consume));
            while (pending.Count > 0) consume(pending.Dequeue());
        }

        public void Clear()
        {
            pending.Clear(); lastHostTick = lastSourceTick = -1; AcceptedCount = 0;
        }
    }
}
