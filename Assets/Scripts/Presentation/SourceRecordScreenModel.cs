using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Blitz.Contracts;

namespace Blitz.Presentation
{
    public enum SourceRecordHighScoreCategory
    {
        GrandChampions, WinPercentage, GamesWon, PointsScored, PointsAllowed, RushingYards,
        PassingYards, WinStreak, GamesPlayed, QuarterbackRating, WorldRecords, Trivia, PopularTeams
    }

    public static class SourceRecordHighScores
    {
        public static int TeamsDefeated(SourceRecordCareer career)
        {
            int count = 0;
            for (int bit = 0; bit < 31; bit++) if ((career.TeamsDefeated & 1 << bit) != 0) count++;
            return count;
        }

        public static float PassRating(SourceRecordCareer career)
        {
            if (career.PassAttempts <= 0) return 0f;
            int attempts = career.PassAttempts;
            float a = (((float)Math.Max(0, career.PassCompletions) / attempts * 100f) - 30f) / 20f;
            float b = (float)Math.Max(0, career.PassTouchdowns) / attempts * 20f;
            float c = (9.5f - (float)Math.Max(0, career.Intercepted) / attempts * 100f) / 4f;
            float d = ((float)career.PassingYards / attempts - 3f) / 4f;
            return (a + b + c + d) / 0.06f;
        }

        // Original record.c comparators, including the surprising ascending
        // defensive tie breakers and independently truncated QB ratings.
        public static int Compare(SourcePlayerRecord left, SourcePlayerRecord right, SourceRecordHighScoreCategory category)
        {
            if (left == null || right == null) throw new ArgumentNullException("records");
            SourceRecordCareer a = left.Career, b = right.Career;
            if (a.GamesPlayed < 4 || b.GamesPlayed < 4)
                return a.GamesPlayed < 4 && b.GamesPlayed < 4 ? 0 : a.GamesPlayed < 4 ? 1 : -1;
            int comparison;
            switch (category)
            {
                case SourceRecordHighScoreCategory.GrandChampions:
                    comparison = TeamsDefeated(b).CompareTo(TeamsDefeated(a)); break;
                case SourceRecordHighScoreCategory.WinPercentage:
                    comparison = (b.GamesWon * 1000 / b.GamesPlayed).CompareTo(a.GamesWon * 1000 / a.GamesPlayed); break;
                case SourceRecordHighScoreCategory.GamesWon:
                    comparison = b.GamesWon.CompareTo(a.GamesWon); break;
                case SourceRecordHighScoreCategory.PointsScored:
                    comparison = (b.PointsScored * 100 / b.GamesPlayed).CompareTo(a.PointsScored * 100 / a.GamesPlayed); break;
                case SourceRecordHighScoreCategory.PointsAllowed:
                    comparison = (a.PointsAllowed * 100 / a.GamesPlayed).CompareTo(b.PointsAllowed * 100 / b.GamesPlayed); break;
                case SourceRecordHighScoreCategory.RushingYards:
                    comparison = (int)(1000f * ((float)b.RushingYards / b.GamesPlayed - (float)a.RushingYards / a.GamesPlayed)); break;
                case SourceRecordHighScoreCategory.PassingYards:
                    comparison = (int)(1000f * ((float)b.PassingYards / b.GamesPlayed - (float)a.PassingYards / a.GamesPlayed)); break;
                case SourceRecordHighScoreCategory.WinStreak:
                    comparison = b.WinStreak.CompareTo(a.WinStreak); break;
                case SourceRecordHighScoreCategory.GamesPlayed:
                    comparison = b.GamesPlayed.CompareTo(a.GamesPlayed); break;
                case SourceRecordHighScoreCategory.QuarterbackRating:
                    comparison = ((int)PassRating(b)).CompareTo((int)PassRating(a)); break;
                case SourceRecordHighScoreCategory.Trivia:
                    comparison = b.TriviaPoints.CompareTo(a.TriviaPoints); break;
                default: throw new ArgumentException("World bests and popular-team audits are not career sorts.", nameof(category));
            }
            if (comparison != 0) return comparison;
            if (category == SourceRecordHighScoreCategory.GrandChampions || category == SourceRecordHighScoreCategory.PointsScored ||
                category == SourceRecordHighScoreCategory.RushingYards || category == SourceRecordHighScoreCategory.PassingYards ||
                category == SourceRecordHighScoreCategory.GamesPlayed)
            {
                comparison = b.GamesWon.CompareTo(a.GamesWon);
                return comparison != 0 ? comparison : b.WinStreak.CompareTo(a.WinStreak);
            }
            if (category == SourceRecordHighScoreCategory.PointsAllowed)
            {
                comparison = a.GamesWon.CompareTo(b.GamesWon);
                return comparison != 0 ? comparison : a.WinStreak.CompareTo(b.WinStreak);
            }
            return b.GamesPlayed.CompareTo(a.GamesPlayed);
        }
    }

    public enum SourceRecordScreenKind
    {
        Options, Selection, Career, CustomSlots, Editor, Formation, Route, RouteAction,
        PlayName, Audibles, LocalStorage, Confirmation, Leaderboard, WorldRecords, Final
    }

    public sealed class SourceRecordScreenRow
    {
        public string Text { get; }
        public bool Selected { get; }
        public bool PrivateText { get; }
        public SourceRecordScreenRow(string text, bool selected = false, bool privateText = false)
        { Text = text; Selected = selected; PrivateText = privateText; }
    }

    public sealed class SourceRecordStationScreen
    {
        public int StationId { get; }
        public string Step { get; }
        public string Name { get; }
        public string PinMask { get; }
        public int Cursor { get; }
        public int Entered { get; }
        public bool Yes { get; }
        public string Message { get; }

        public SourceRecordStationScreen(int stationId, string step, string name, string pinMask,
            int cursor, int entered, bool yes, string message)
        {
            StationId = stationId; Step = step; Name = name; PinMask = pinMask;
            Cursor = cursor; Entered = entered; Yes = yes; Message = message;
        }
    }

    // This is a private-to-screen projection. Host diagnostics use the runtime's
    // data-free Describe(), never serialize this view or the selected records.
    public sealed class SourceRecordScreenView
    {
        public SourceRecordScreenKind Kind { get; }
        public string Title { get; }
        public string Message { get; }
        public string Prompt { get; }
        public string StoreStatus { get; }
        public string StoreCode { get; }
        public bool Busy { get; }
        public int StationId { get; }
        public int SelectedSlot { get; }
        public int UsableSlots { get; }
        public int Receiver { get; }
        public int CursorX { get; }
        public int CursorY { get; }
        public int InputCursor { get; }
        public string Name { get; }
        public bool CapIdentityEntry { get; }
        public SourceCustomPlay Draft { get; }
        public ReadOnlyCollection<SourceCustomPlay> Plays { get; }
        public ReadOnlyCollection<SourceRecordScreenRow> Rows { get; }
        public ReadOnlyCollection<SourceRecordScreenRow> Details { get; }
        public ReadOnlyCollection<SourceRecordStationScreen> Stations { get; }

        public SourceRecordScreenView(SourceRecordScreenKind kind, string title, string message,
            string prompt, string storeStatus, string storeCode, bool busy, int stationId,
            IReadOnlyList<SourceRecordScreenRow> rows = null,
            IReadOnlyList<SourceRecordScreenRow> details = null,
            IReadOnlyList<SourceRecordStationScreen> stations = null,
            IReadOnlyList<SourceCustomPlay> plays = null, int selectedSlot = 0, int usableSlots = 0,
            SourceCustomPlay draft = null, int receiver = -1, int cursorX = 0, int cursorY = 0,
            int inputCursor = 0, string name = null, bool capIdentityEntry = false)
        {
            Kind = kind; Title = title; Message = message; Prompt = prompt;
            StoreStatus = storeStatus; StoreCode = storeCode; Busy = busy; StationId = stationId;
            Rows = Copy(rows); Details = Copy(details); Stations = Copy(stations); Plays = Copy(plays);
            SelectedSlot = selectedSlot; UsableSlots = usableSlots; Draft = draft;
            Receiver = receiver; CursorX = cursorX; CursorY = cursorY; InputCursor = inputCursor; Name = name;
            CapIdentityEntry = capIdentityEntry;
        }

        private static ReadOnlyCollection<T> Copy<T>(IReadOnlyList<T> values)
        {
            var copy = new T[values == null ? 0 : values.Count];
            for (int i = 0; i < copy.Length; i++) copy[i] = values[i];
            return Array.AsReadOnly(copy);
        }
    }
}
