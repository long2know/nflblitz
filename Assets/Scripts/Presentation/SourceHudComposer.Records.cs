using System;
using System.Collections.Generic;
using System.Linq;
using Blitz.AssetImport;
using Blitz.Contracts;
using Blitz.Runtime;

namespace Blitz.Presentation
{
    public sealed partial class SourceHudComposer
    {
        private static readonly uint[] RecordRouteColors = { 0xff00ff00u, 0xff0000ffu, 0xffff0000u };
        private static readonly string[] RecordLetterSymbols = Enumerable.Range(0, 26).Select(i => ((char)('a' + i)).ToString())
            .Concat(new[] { "dsh", "ast", "exc", "spc", "del", "end" }).ToArray();

        public IReadOnlyList<SourceHudPrimitive> Records(SourceRecordScreenView view, SourceHudViewport viewport)
        {
            if (view == null) throw new ArgumentNullException(nameof(view));
            draw.Clear();
            bool editor = view.CapIdentityEntry || view.Kind == SourceRecordScreenKind.Editor || view.Kind == SourceRecordScreenKind.Formation ||
                view.Kind == SourceRecordScreenKind.Route || view.Kind == SourceRecordScreenKind.RouteAction ||
                view.Kind == SourceRecordScreenKind.PlayName || view.Kind == SourceRecordScreenKind.CustomSlots ||
                view.Kind == SourceRecordScreenKind.Audibles;
            RecordOriginal("records/backdrop", editor ? "bckgrnd" : "mbg", viewport, 0, 0, 512, 384);
            if (view.Kind == SourceRecordScreenKind.Career && HasRecordRaster("statpl_r"))
                RecordOriginal("records/title/native", "statpl_r", viewport, 190, 6, 132, 32, true);
            else RecordText("records/title", "bast18", view.Title, viewport, 8, 8, 496, 29, Yellow);
            if (view.Kind == SourceRecordScreenKind.Selection)
            {
                if (view.CapIdentityEntry) RecordCapIdentity(view, viewport);
                else RecordStations(view, viewport);
            }
            else if (view.Kind == SourceRecordScreenKind.CustomSlots)
                RecordSlots(view, viewport);
            else if (view.Kind == SourceRecordScreenKind.Confirmation)
                RecordConfirmation(view, viewport);
            else if (view.Kind == SourceRecordScreenKind.PlayName)
            {
                RecordNative("records/name/keyboard", "letters", viewport, 364, 182);
                RecordNative("records/name/cursor", "mp_" + RecordLetterSymbols[view.InputCursor], viewport, 364, 182);
                RecordRows(view.Details, "records/play-name", viewport, 234, 40, 260, 21, "bast10");
            }
            else if (editor)
            {
                if (view.Draft != null)
                {
                    RecordNativeDiagram(view.Draft, viewport, view.Receiver);
                    RecordText("records/play/name", "bast10", (view.Draft.Name1 + " " + view.Draft.Name2).ToUpperInvariant(),
                        viewport, 234, 42, 260, 24, 0xff00ffffu);
                }
                RecordRows(view.Rows, "records/action", viewport, 10, 48, 204, view.Rows.Count > 10 ? 21 : 29, "bast10", true);
                if (view.Kind == SourceRecordScreenKind.Route || view.Kind == SourceRecordScreenKind.RouteAction)
                {
                    double x = 224 + 7 * view.CursorX, y = 384 - (8 + 7 * view.CursorY);
                    RecordNative("records/route/cursor", "cursor", viewport, x, 384 - y);
                }
                RecordRows(view.Details, "records/detail", viewport, 12, 309, 200, 17, "bast8t");
            }
            else
            {
                if (view.Kind == SourceRecordScreenKind.Career && HasRecordRaster("statfade"))
                {
                    RecordOriginal("records/career/fade", "statfade", viewport, 16, 64, 480, 268);
                    for (int row = 0; row < view.Details.Count; row++)
                        RecordOriginal("records/career/plate/" + row, "sstatplr", viewport, 22, 72 + row * 20, 468, 18);
                }
                if (view.Kind == SourceRecordScreenKind.Options)
                    RecordOptions(view, viewport);
                else
                {
                    if (view.Name != null)
                        RecordText("records/identity", "bast18", DisplayRecordName(view.Name),
                            viewport, 16, 40, 480, 25, Yellow, true);
                    RecordRows(view.Details, "records/detail", viewport, 16, 72, 480, 20, "bast10");
                    int rowTop = view.Kind == SourceRecordScreenKind.Leaderboard ? 120 : view.Details.Count == 0 ? 82 : 174;
                    RecordRows(view.Rows, "records/action", viewport, 24, rowTop, 464,
                        view.Rows.Count > 7 ? 17 : 22, "bast10");
                }
            }
            if (!string.IsNullOrEmpty(view.Message) && !view.CapIdentityEntry && view.Kind != SourceRecordScreenKind.Confirmation)
                RecordText("records/message", "bast8t", view.Message, viewport, 6, 343, 500, 18, Yellow);
            RecordText("records/prompt", "bast8t", view.Busy ? "SAVING - WAIT FOR THE ATOMIC LOCAL TRANSACTION" : view.Prompt,
                viewport, 6, 365, 500, 16, view.Busy ? Red : White);
            return Finish(viewport);
        }

        private void RecordConfirmation(SourceRecordScreenView view, SourceHudViewport viewport)
        {
            RecordOriginal("records/confirmation/plate", "ynbox", viewport, 100, 91, 312, 218, true);
            RecordText("records/confirmation/question", "bast10", view.Message, viewport, 125, 108, 262, 82, Yellow);
            RecordOriginal("records/confirmation/no", "no", viewport, 161, 225, 77, 42, true);
            RecordOriginal("records/confirmation/yes", "yes", viewport, 274, 225, 77, 42, true);
            RecordOutline("records/confirmation/cursor", viewport, view.Rows[0].Selected ? 157 : 270, 220, 85, 52, Yellow);
        }

        private void RecordCapIdentity(SourceRecordScreenView view, SourceHudViewport viewport)
        {
            SourceRecordStationScreen station = view.Stations.Single(item => item.StationId == view.StationId);
            RecordNative("records/cap/identity/backplate", "lback", viewport, 364, 162);
            RecordText("records/cap/identity/player", "bast13", "PLAYER " + (station.StationId + 1),
                viewport, 12, 72, 196, 27, Yellow);
            RecordText("records/cap/identity/help", "bast10", station.Step == "Pin" ? "ENTER FOUR PIN POSITIONS" : "ENTER SIX NAME POSITIONS",
                viewport, 10, 112, 200, 46, Yellow);
            if (station.Step == "Name")
            {
                RecordNative("records/cap/identity/keyboard", "letters", viewport, 364, 182);
                RecordNative("records/cap/identity/cursor", "mp_" + RecordLetterSymbols[station.Cursor], viewport, 364, 182);
            }
            else if (station.Step == "Pin")
            {
                RecordNative("records/cap/identity/keyboard", "mpin", viewport, 364, 142);
                SourceOriginalRaster image = RecordRaster("mpin");
                RecordOutline("records/cap/identity/pin-cursor", viewport,
                    364 - image.AnchorX - 1 + station.Cursor % 3 * 27.5,
                    384 - 142 - image.AnchorY - 2 + station.Cursor / 3 * 38.5, 25, 29, Yellow);
            }
            else
            {
                RecordText("records/cap/identity/status", "bast10", station.Message ?? station.Step.ToUpperInvariant(),
                    viewport, 236, 119, 264, 52, Yellow);
                RecordText("records/cap/identity/action", "bast10", station.Step == "Missing" ?
                    "A CREATE  B REENTER  C GUEST" : "A RETRY  B GUEST  C RECOVERY",
                    viewport, 236, 209, 264, 51);
            }
            RecordText("records/cap/identity/entry", "bast18", station.Step == "Pin" ? station.PinMask : station.Name,
                viewport, 242, 331, 244, 30, Yellow, station.Step != "Pin");
        }

        private void RecordOptions(SourceRecordScreenView view, SourceHudViewport viewport)
        {
            string[] symbols = { "mcreate", "menter", "mselect" };
            for (int i = 0; i < 3; i++)
            {
                double x = 9 + 169 * i;
                RecordOriginal("records/option/plate/" + i, view.Rows[i].Selected ? "cbox" : "cboxt", viewport, x, 99, 158, 168);
                RecordOriginal("records/option/text/" + i, symbols[i] + (view.Rows[i].Selected ? "" : "t"), viewport, x + 7, 151, 144, 59, true);
            }
            RecordText("records/option/storage", "bast13", view.Rows[3].Text, viewport, 52, 291, 408, 27,
                view.Rows[3].Selected ? Yellow : White);
        }

        private void RecordStations(SourceRecordScreenView view, SourceHudViewport viewport)
        {
            foreach (SourceRecordStationScreen station in view.Stations)
            {
                double x = station.StationId * 128;
                string plaque = station.Step == "Name" ? "initplq" : station.Step == "Pin" ? "pinplq" : "ynplq";
                RecordOriginal("records/station/" + station.StationId + "/plaque", plaque + (station.StationId + 1),
                    viewport, x, 46, 128, 276);
                RecordText("records/station/" + station.StationId + "/number", "bast10", "PLAYER " + (station.StationId + 1),
                    viewport, x + 3, 37, 122, 18, Yellow);
                if (station.Step == "Name" || station.Step == "Pin")
                {
                    string key = station.Step == "Name" ? RecordLetterSymbols[station.Cursor] :
                        station.Cursor == 9 ? "pspc" : station.Cursor == 10 ? "0" :
                        station.Cursor == 11 ? "pdel" : (station.Cursor + 1).ToString();
                    SourceOriginalRaster plaqueImage = RecordRaster(plaque + (station.StationId + 1));
                    SourceOriginalRaster highlight = RecordRaster((station.StationId < 2 ? "l_" : "r_") + key);
                    double anchorX = x + plaqueImage.AnchorX;
                    double sourceY = 384 - 46 - plaqueImage.AnchorY;
                    double left = anchorX - highlight.AnchorX;
                    double top = 384 - sourceY - highlight.AnchorY;
                    RecordOutline("records/station/" + station.StationId + "/key-cursor", viewport,
                        left, top, highlight.Width, highlight.Height, Yellow);
                    RecordText("records/station/" + station.StationId + "/entry", "bast18",
                        station.Step == "Pin" ? station.PinMask : station.Name,
                        viewport, x + 5, 302, 118, 26, Yellow, station.Step == "Name");
                }
                else if (station.Step == "Choice")
                {
                    RecordText("records/station/" + station.StationId + "/yes", "bast18", "YES",
                        viewport, x + 7, 140, 114, 28, station.Yes ? Yellow : White);
                    RecordText("records/station/" + station.StationId + "/no", "bast18", "NO",
                        viewport, x + 7, 191, 114, 28, station.Yes ? White : Yellow);
                    RecordText("records/station/" + station.StationId + "/help", "bast8t", "UP YES / DOWN NO / A",
                        viewport, x + 4, 278, 120, 21, Yellow);
                }
                else
                {
                    RecordText("records/station/" + station.StationId + "/name", "bast13",
                        station.Step == "Selected" ? DisplayRecordName(station.Name) : station.Step.ToUpperInvariant(),
                        viewport, x + 4, 116, 120, 25, Yellow, station.Step == "Selected");
                    string status = station.Message ?? station.Step.ToUpperInvariant();
                    string messageImage = station.Step == "Missing" ? "mrecnf" :
                        station.Step == "Selected" && status.StartsWith("RECORD FOUND", StringComparison.Ordinal) ? "mrecf" : null;
                    if (status.Contains("CREATED PLAYS LOADED") && HasRecordRaster("gridbox"))
                    {
                        RecordOriginal("records/station/" + station.StationId + "/created", "gridbox", viewport, x + 10, 142, 108, 129);
                        RecordText("records/station/" + station.StationId + "/created/title", "bast13", "CREATED PLAYS LOADED",
                            viewport, x + 14, 167, 100, 72, Yellow);
                    }
                    else if (messageImage != null && HasRecordRaster(messageImage))
                        RecordOriginal("records/station/" + station.StationId + "/message", messageImage, viewport, x, 146, 128, 96, true);
                    else
                    {
                        string[] words = status.Split(' ');
                        for (int at = 0; at < words.Length; at += 3)
                            RecordText("records/station/" + station.StationId + "/status/" + at, "bast8t",
                                string.Join(" ", words.Skip(at).Take(3)), viewport, x + 3, 156 + at / 3 * 17, 122, 16);
                    }
                    string action = station.Step == "Missing" ? "A CREATE / B REENTER" :
                        station.Step == "Unavailable" ? "A RETRY / B GUEST" :
                        station.Step == "Duplicate" ? "A REENTER / B GUEST" : "A RECORD / B CHANGE";
                    RecordText("records/station/" + station.StationId + "/action", "bast8t", action,
                        viewport, x + 3, 298, 122, 22, Yellow);
                }
            }
        }

        private void RecordSlots(SourceRecordScreenView view, SourceHudViewport viewport)
        {
            RecordText("records/identity", "bast13", DisplayRecordName(view.Name), viewport, 10, 58, 200, 25, Yellow, true);
            RecordText("records/slots/earned", "bast10", "EARNED SLOTS " + view.UsableSlots + " / 9",
                viewport, 10, 103, 200, 22, Yellow);
            RecordText("records/slots/source", "bast8t", "SOURCE WINS: 0 0 0 5 10 15 20 25 30",
                viewport, 8, 135, 205, 40);
            for (int slot = 0; slot < 9; slot++)
            {
                double x = 226 + slot % 3 * 91, y = 57 + slot / 3 * 92;
                bool available = slot < view.UsableSlots;
                RecordOriginal("records/slot/" + slot + "/plate", available ? "mp_plate" : "mp_plateg",
                    viewport, x, y, 83, 89);
                SourceCustomPlay play = view.Plays.Count > slot ? view.Plays[slot] : null;
                if (play != null && play.Exists)
                    RecordDiagram("records/slot/" + slot, play, ScreenRect(viewport, x + 5, y + 5, 73, 62));
                string label = !available ? new[] { 0, 0, 0, 5, 10, 15, 20, 25, 30 }[slot] + " WINS" :
                    play == null ? "NEW PLAY " + (slot + 1) : (play.Name1 + " " + play.Name2).ToUpperInvariant();
                RecordText("records/slot/" + slot + "/name", "bast8t", label, viewport, x + 2, y + 69, 79, 17);
                if (slot == view.SelectedSlot) RecordOutline("records/slot/cursor", viewport, x, y, 83, 89, Yellow);
            }
        }

        private void RecordRows(IEnumerable<SourceRecordScreenRow> rows, string element, SourceHudViewport viewport,
            double x, double y, double width, double height, string font, bool plates = false)
        {
            int index = 0;
            foreach (SourceRecordScreenRow row in rows)
            {
                if (plates)
                    RecordOriginal(element + "/" + index + "/plate", row.Selected ? "button_on" : "button_off",
                        viewport, x, y, width, height - 1);
                RecordText(element + "/" + index, font, row.Text, viewport, x + 3, y + 1, width - 6, height - 2,
                    row.Selected ? Yellow : White, row.PrivateText);
                y += height; index++;
            }
        }

        private void RecordNativeDiagram(SourceCustomPlay play, SourceHudViewport viewport, int activeReceiver)
        {
            for (int x = 1; x <= 39; x += 2)
                RecordLine("records/grid/x", viewport, 224 + 7 * x, 75, 224 + 7 * x, 369, 0.4, 0x30505050u);
            for (int y = 1; y <= 43; y += 2)
                RecordLine("records/grid/y", viewport, 231, 384 - (8 + 7 * y), 497, 384 - (8 + 7 * y), 0.4, 0x30505050u);
            RecordLine("records/grid/los", viewport, 231, 320, 497, 320, 1, White);
            for (int i = 0; i < 3; i++) RecordNative("records/lineman/" + i, "mpo", viewport, 364 + (i - 1) * 17, 71);
            RecordNative("records/qb", "sym_qb", viewport, 364,
                78 - ((play.Flags & SourceCustomPlayFlags.QuarterbackFar) != 0 ? 38 : 24));
            for (int receiver = 0; receiver < 3; receiver++)
            {
                uint color = activeReceiver < 0 || receiver == activeReceiver ? RecordRouteColors[receiver] : 0xff969696u;
                SourceCustomPoint previous = play.Point(receiver, 0);
                RecordNative("records/receiver/" + receiver, "sym_r" + (receiver + 1), viewport,
                    224 + 7 * previous.X, 8 + 7 * previous.Y);
                if (play.Motion == receiver)
                {
                    RecordNative("records/motion/left", "arrow_motl", viewport, 224 + 7 * previous.X, 8 + 7 * previous.Y);
                    RecordNative("records/motion/right", "arrow_motr", viewport, 224 + 7 * previous.X, 8 + 7 * previous.Y);
                }
                for (int point = 1; point < 9 && (previous.Flags & SourceCustomPointFlags.Last) == 0; point++)
                {
                    SourceCustomPoint current = play.Point(receiver, point);
                    RecordLine("records/route/" + receiver + "/" + point, viewport,
                        224 + 7 * previous.X, 384 - (8 + 7 * previous.Y),
                        224 + 7 * current.X, 384 - (8 + 7 * current.Y), 2, color);
                    RecordNative("records/node/" + receiver + "/" + point, RecordNodeSymbol(current.Flags), viewport,
                        224 + 7 * current.X, 8 + 7 * current.Y);
                    previous = current;
                }
            }
        }

        private void RecordDiagram(string element, SourceCustomPlay play, SourceHudRect area, bool flipped = false)
        {
            Func<SourceCustomPoint, double> x = point => area.X + (flipped ? 39 - point.X : point.X - 1) / 38.0 * area.Width;
            Func<SourceCustomPoint, double> y = point => area.Y + (43 - point.Y) / 42.0 * area.Height;
            for (int receiver = 0; receiver < 3; receiver++)
            {
                SourceCustomPoint previous = play.Point(receiver, 0);
                RecordIcon(element + "/receiver/" + receiver, "sym_r" + (receiver + 1), x(previous), y(previous),
                    Math.Min(area.Width / 280, area.Height / 300));
                for (int point = 1; point < 9 && (previous.Flags & SourceCustomPointFlags.Last) == 0; point++)
                {
                    SourceCustomPoint current = play.Point(receiver, point);
                    RecordSolidLine(element + "/route/" + receiver + "/" + point, x(previous), y(previous),
                        x(current), y(current), Math.Max(0.65, area.Width / 110), RecordRouteColors[receiver]);
                    RecordIcon(element + "/node/" + receiver + "/" + point, RecordNodeSymbol(current.Flags),
                        x(current), y(current), Math.Min(area.Width / 280, area.Height / 300));
                    previous = current;
                }
            }
        }

        private static string RecordNodeSymbol(SourceCustomPointFlags flags)
        {
            if ((flags & SourceCustomPointFlags.Juke) != 0) return "sym_j";
            if ((flags & SourceCustomPointFlags.Spin) != 0) return "sym_s";
            if ((flags & SourceCustomPointFlags.Turbo) != 0) return "sym_t";
            return "cpoint";
        }

        private SourceOriginalRaster RecordRaster(string symbol)
        {
            if (original == null) throw new SourceInterfaceException("SOURCE_RECORD_ART_REQUIRED: explicit original record resources are missing.");
            return original.Image(symbol);
        }

        private bool HasRecordRaster(string symbol)
        { return original != null && original.Rasters.Any(image => image.Id == symbol); }

        private void RecordOriginal(string element, string symbol, SourceHudViewport viewport, double x, double y,
            double width, double height, bool fit = false)
        { RecordRaster(symbol); OriginalImage(element, symbol, viewport, x, y, width, height, fit); }

        private void RecordNative(string element, string symbol, SourceHudViewport viewport, double x, double sourceY)
        {
            SourceOriginalRaster image = RecordRaster(symbol);
            RecordOriginal(element, symbol, viewport, x - image.AnchorX, 384 - sourceY - image.AnchorY, image.Width, image.Height);
        }

        private void RecordIcon(string element, string symbol, double x, double y, double scale)
        {
            SourceOriginalRaster image = RecordRaster(symbol);
            double left = x - image.AnchorX * scale, top = y - image.AnchorY * scale;
            double right = left + image.Width * scale, bottom = top + image.Height * scale;
            draw.Add(new SourceHudPrimitive(element, "original/" + symbol, White, new[] {
                new SourceHudVertex(left, top, 0, 1), new SourceHudVertex(right, top, 1, 1),
                new SourceHudVertex(right, bottom, 1, 0), new SourceHudVertex(left, top, 0, 1),
                new SourceHudVertex(right, bottom, 1, 0), new SourceHudVertex(left, bottom, 0, 0)
            }, sourceImageSymbol: symbol));
        }

        private void RecordText(string element, string fontId, string text, SourceHudViewport viewport,
            double x, double y, double width, double height, uint color = White, bool privateContent = false)
        {
            if (string.IsNullOrEmpty(text)) return;
            text = text.ToUpperInvariant();
            SourceInterfaceFont font = data.Font(fontId);
            SourceHudRect bounds = ScreenRect(viewport, x, y, width, height);
            double scale = Math.Min(ScreenScale(viewport), Math.Min(bounds.Width / Math.Max(1, SourceHudText.Width(font, text) + 4),
                bounds.Height / (font.Height + 6)));
            foreach (SourceHudGlyph glyph in SourceHudText.Center(font, text,
                bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2, scale))
                Image(element, glyph.Image, glyph.Rect, color, textContent: privateContent ? null : text, privateContent: privateContent);
        }

        private void RecordOutline(string element, SourceHudViewport viewport, double x, double y, double width, double height, uint color)
        {
            RecordLine(element, viewport, x, y, x + width, y, 1, color);
            RecordLine(element, viewport, x + width, y, x + width, y + height, 1, color);
            RecordLine(element, viewport, x + width, y + height, x, y + height, 1, color);
            RecordLine(element, viewport, x, y + height, x, y, 1, color);
        }

        private void RecordLine(string element, SourceHudViewport viewport, double x0, double y0, double x1, double y1, double thickness, uint color)
        {
            SourceHudRect a = ScreenRect(viewport, x0, y0, 0, 0), b = ScreenRect(viewport, x1, y1, 0, 0);
            RecordSolidLine(element, a.X, a.Y, b.X, b.Y, thickness * ScreenScale(viewport), color);
        }

        private void RecordSolidLine(string element, double x0, double y0, double x1, double y1, double thickness, uint color)
        {
            double length = Math.Sqrt((x1 - x0) * (x1 - x0) + (y1 - y0) * (y1 - y0));
            if (length <= 0) return;
            double dx = -(y1 - y0) * thickness / (2 * length), dy = (x1 - x0) * thickness / (2 * length);
            draw.Add(new SourceHudPrimitive(element, "record-solid", color, new[] {
                new SourceHudVertex(x0 + dx, y0 + dy, 0, 0), new SourceHudVertex(x1 + dx, y1 + dy, 0, 0),
                new SourceHudVertex(x1 - dx, y1 - dy, 0, 0), new SourceHudVertex(x0 + dx, y0 + dy, 0, 0),
                new SourceHudVertex(x1 - dx, y1 - dy, 0, 0), new SourceHudVertex(x0 - dx, y0 - dy, 0, 0)
            }));
        }

        private static string DisplayRecordName(string name)
        { return name == null ? "GUEST" : name.TrimEnd(' ').Length == 0 ? "SIX SPACE NAME" : name; }
    }
}
