using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Blitz.Contracts
{
    public static class SourceSavedRecordLimits
    {
        public const string OriginalRevision = "89f840d58d152a2a9f230cb8d0cdffbd554c8ded";
        public const int RecordCount = 350;
        public const int NameLength = 6;
        public const int PinLength = 4;
        public const int CustomSlots = 9;
        public const int WorldRecordCount = 6;
        public const int Receivers = 3;
        public const int PointsPerRoute = 9;
        public const int CustomNameLength = 8;
        public const int CompactPlayBytes = 101;
        public const int MinimumHighScoreGames = 4;
    }

    // The arcade PIN distinguishes local records; it is not an authentication secret.
    // It deliberately has no public property (including on serializer/debug snapshots).
    public sealed class SourceRecordIdentity : IEquatable<SourceRecordIdentity>
    {
        private readonly string pin;
        public string Name { get; }
        public string DisplayName { get { return Name.TrimEnd(' '); } }

        public SourceRecordIdentity(string name, string pin)
        {
            if (name == null || name.Length < 1 || name.Length > SourceSavedRecordLimits.NameLength)
                throw new ArgumentException("Use one to six original name characters.", nameof(name));
            foreach (char c in name)
                if (!(c >= 'A' && c <= 'Z') && c != '-' && c != '*' && c != '!' && c != ' ')
                    throw new ArgumentException("The original name alphabet is A-Z, dash, star, exclamation and space.", nameof(name));
            if (name[0] == '-')
                throw new ArgumentException("The original guest prefix is not a saved identity.", nameof(name));
            if (pin == null || pin.Length != SourceSavedRecordLimits.PinLength)
                throw new ArgumentException("Use four original PIN characters.", nameof(pin));
            foreach (char c in pin)
                if (!(c >= '0' && c <= '9') && c != ' ')
                    throw new ArgumentException("The original PIN alphabet is digits and space.", nameof(pin));
            Name = name.PadRight(SourceSavedRecordLimits.NameLength, ' ');
            this.pin = pin;
        }

        public string CopyPinForLocalPersistence() { return pin; }
        public bool Equals(SourceRecordIdentity other)
        {
            return other != null && Name == other.Name && pin == other.pin;
        }
        public override bool Equals(object obj) { return Equals(obj as SourceRecordIdentity); }
        public override int GetHashCode() { return Name.GetHashCode() ^ pin.GetHashCode(); }
        public override string ToString() { return "SourceRecordIdentity"; }
    }

    [Flags]
    public enum SourceCustomPointFlags { None = 0, Juke = 1, Spin = 2, Turbo = 4, Last = 8 }

    [Flags]
    public enum SourceCustomPlayFlags
    {
        None = 0, Exists = 1, QuarterbackFar = 2, Protected = 4,
        PrimaryReceiver1 = 8, PrimaryReceiver2 = 16, PrimaryReceiver3 = 32, Unused = 64
    }

    public readonly struct SourceCustomPoint
    {
        public int X { get; }
        public int Y { get; }
        public SourceCustomPointFlags Flags { get; }
        public SourceCustomPoint(int x, int y, SourceCustomPointFlags flags)
        {
            SourceContractGuard.Range(x, 0, 255, nameof(x));
            SourceContractGuard.Range(y, 0, 255, nameof(y));
            SourceContractGuard.Range((int)flags, 0, 255, nameof(flags));
            X = x; Y = y; Flags = flags;
        }
    }

    // Three original nine-point grid routes, including each receiver's base node.
    // Motion is a source unsigned byte: 0..2 select a receiver, 3 is invalid;
    // other values mean no matching motion receiver in convert_play.
    public sealed class SourceCustomPlay
    {
        public string Name1 { get; }
        public string Name2 { get; }
        public int Motion { get; }
        public SourceCustomPlayFlags Flags { get; }
        public ReadOnlyCollection<SourceCustomPoint> Points { get; }
        public bool Exists { get { return (Flags & SourceCustomPlayFlags.Exists) != 0; } }

        public SourceCustomPlay(string name1, string name2, int motion, SourceCustomPlayFlags flags,
            IReadOnlyList<SourceCustomPoint> points)
        {
            if (name1 == null || name2 == null || name1.Length > 8 || name2.Length > 8)
                throw new ArgumentException("Custom names contain at most eight characters.");
            foreach (char c in name1 + name2)
                if (!(c >= 'A' && c <= 'Z') && !(c >= 'a' && c <= 'z') &&
                    !(c >= '0' && c <= '9') && c != ' ' && c != '!' && c != '*' && c != '-')
                    throw new ArgumentException("Invalid original custom-play name character.");
            SourceContractGuard.Range(motion, 0, 255, nameof(motion));
            SourceContractGuard.Range((int)flags, 0, 255, nameof(flags));
            if (points == null || points.Count != 27)
                throw new ArgumentException("Supply all three nine-point source routes.", nameof(points));
            var copy = new SourceCustomPoint[points.Count];
            for (int i = 0; i < copy.Length; i++) copy[i] = points[i];
            Name1 = name1; Name2 = name2; Motion = motion; Flags = flags;
            Points = Array.AsReadOnly(copy);
        }

        public SourceCustomPoint Point(int receiver, int point)
        {
            SourceContractGuard.Range(receiver, 0, 2, nameof(receiver));
            SourceContractGuard.Range(point, 0, 8, nameof(point));
            return Points[receiver * 9 + point];
        }
    }

    public enum SourceCustomPlayValidationError
    {
        None = 0, Motion = 1, BasePosition = 2, OverlappingBases = 3,
        ControlPosition = 4, OverlappingRoute = 5, SpecialLimit = 6
    }

    public sealed class SourceCustomPlayValidation
    {
        public SourceCustomPlay Play { get; }
        public SourceCustomPlayValidationError Error { get; }
        public bool Repaired { get; }
        public bool Valid { get { return Error == SourceCustomPlayValidationError.None; } }
        public SourceCustomPlayValidation(SourceCustomPlay play, SourceCustomPlayValidationError error, bool repaired)
        { Play = play; Error = error; Repaired = repaired; }
    }

    public sealed class SourceRecordCareer
    {
        public int LastUsed { get; }
        public int GamesPlayed { get; }
        public int GamesWon { get; }
        public int LossesIncludingTies { get { return GamesPlayed - GamesWon; } }
        public int WinStreak { get; }
        public int PointsScored { get; }
        public int PointsAllowed { get; }
        public int TeamsDefeated { get; }
        public int Tackles { get; }
        public int Sacks { get; }
        public int PassingYards { get; }
        public int RushingYards { get; }
        public int WorldRecords { get; }
        public int PassAttempts { get; }
        public int PassCompletions { get; }
        public int PassTouchdowns { get; }
        public int Intercepted { get; }
        public int TriviaPoints { get; }

        public SourceRecordCareer(int lastUsed = 0, int gamesPlayed = 0, int gamesWon = 0,
            int winStreak = 0, int pointsScored = 0, int pointsAllowed = 0, int teamsDefeated = 0,
            int tackles = 0, int sacks = 0, int passingYards = 0, int rushingYards = 0,
            int worldRecords = 0, int passAttempts = 0, int passCompletions = 0,
            int passTouchdowns = 0, int intercepted = 0, int triviaPoints = 0)
        {
            SourceContractGuard.Range(lastUsed, 0, short.MaxValue, nameof(lastUsed));
            SourceContractGuard.Range(gamesPlayed, 0, short.MaxValue, nameof(gamesPlayed));
            SourceContractGuard.Range(gamesWon, 0, gamesPlayed, nameof(gamesWon));
            SourceContractGuard.Range(winStreak, sbyte.MinValue, sbyte.MaxValue, nameof(winStreak));
            SourceContractGuard.Range(pointsScored, 0, short.MaxValue, nameof(pointsScored));
            SourceContractGuard.Range(pointsAllowed, 0, short.MaxValue, nameof(pointsAllowed));
            SourceContractGuard.Range(teamsDefeated, 0, int.MaxValue, nameof(teamsDefeated));
            SourceContractGuard.Range(tackles, 0, short.MaxValue, nameof(tackles));
            SourceContractGuard.Range(sacks, 0, short.MaxValue, nameof(sacks));
            SourceContractGuard.Range(worldRecords, 0, short.MaxValue, nameof(worldRecords));
            SourceContractGuard.Range(passAttempts, 0, int.MaxValue, nameof(passAttempts));
            SourceContractGuard.Range(passCompletions, 0, int.MaxValue, nameof(passCompletions));
            SourceContractGuard.Range(passTouchdowns, 0, int.MaxValue, nameof(passTouchdowns));
            SourceContractGuard.Range(intercepted, 0, int.MaxValue, nameof(intercepted));
            SourceContractGuard.Range(triviaPoints, short.MinValue, short.MaxValue, nameof(triviaPoints));
            LastUsed = lastUsed; GamesPlayed = gamesPlayed; GamesWon = gamesWon; WinStreak = winStreak;
            PointsScored = pointsScored; PointsAllowed = pointsAllowed; TeamsDefeated = teamsDefeated;
            Tackles = tackles; Sacks = sacks; PassingYards = passingYards; RushingYards = rushingYards;
            WorldRecords = worldRecords; PassAttempts = passAttempts; PassCompletions = passCompletions;
            PassTouchdowns = passTouchdowns; Intercepted = intercepted; TriviaPoints = triviaPoints;
        }

    }

    public enum SourceWorldRecordCategory
    {
        Sacks = 0, Touchdowns = 1, Tackles = 2,
        InterceptionsMade = 3, PointsScored = 4, VictoryMargin = 5
    }

    // Global single-match bests are distinct from the opaque career.world_records
    // bit field. A null table entry is unobserved, never a default score/holder.
    public sealed class SourceWorldRecord
    {
        public SourceWorldRecordCategory Category { get; }
        public int Value { get; }
        public SourceRecordIdentity Holder { get; }
        public SourceWorldRecord(SourceWorldRecordCategory category, int value, SourceRecordIdentity holder)
        {
            SourceContractGuard.Range((int)category, 0, SourceSavedRecordLimits.WorldRecordCount - 1, nameof(category));
            SourceContractGuard.Range(value, 0, short.MaxValue, nameof(value));
            Holder = holder ?? throw new ArgumentNullException(nameof(holder));
            Category = category; Value = value;
        }
    }

    public sealed class SourceRecordAudibles
    {
        public ReadOnlyCollection<short> Offense { get; }
        public ReadOnlyCollection<short> Defense { get; }
        public SourceRecordAudibles(IReadOnlyList<short> offense, IReadOnlyList<short> defense)
        {
            Offense = Copy(offense, 45, nameof(offense));
            Defense = Copy(defense, 18, nameof(defense));
        }
        private static ReadOnlyCollection<short> Copy(IReadOnlyList<short> ids, int limit, string name)
        {
            if (ids == null || ids.Count != 3) throw new ArgumentException("Supply three original audible IDs.", name);
            var copy = new short[3];
            bool zero = false, nonzero = false;
            for (int i = 0; i < 3; i++)
            {
                if (ids[i] < -limit || ids[i] > limit) throw new ArgumentOutOfRangeException(name);
                copy[i] = ids[i]; zero |= ids[i] == 0; nonzero |= ids[i] != 0;
            }
            if (zero && nonzero) throw new ArgumentException("Original audible rows are all zero or all nonzero.", name);
            return Array.AsReadOnly(copy);
        }
        public static SourceRecordAudibles Defaults
        { get { return new SourceRecordAudibles(new short[3], new short[3]); } }
    }

    // Logical portable acquisition history, not a native disk-bank/CRC witness.
    public enum SourceCustomBlockState { LegacyUnknown = 0, NeverAllocated = 1, Allocated = 2 }

    public sealed class SourcePlayerRecord
    {
        public int RecordNumber { get; }
        public SourceRecordIdentity Identity { get; }
        public SourceRecordCareer Career { get; }
        public int DiskBank { get; }
        public int PlaysAllowed { get; }
        public ReadOnlyCollection<SourceCustomPlay> CustomPlays { get; }
        public SourceRecordAudibles Audibles { get; }
        public SourceCustomBlockState CustomBlockState { get; }

        public SourcePlayerRecord(int recordNumber, SourceRecordIdentity identity, SourceRecordCareer career,
            int diskBank = -1, int playsAllowed = 3, IReadOnlyList<SourceCustomPlay> customPlays = null,
            SourceRecordAudibles audibles = null,
            SourceCustomBlockState customBlockState = SourceCustomBlockState.LegacyUnknown)
        {
            SourceContractGuard.Range(recordNumber, 0, 349, nameof(recordNumber));
            SourceContractGuard.Range(diskBank, -1, short.MaxValue, nameof(diskBank));
            SourceContractGuard.Range(playsAllowed, 0, 9, nameof(playsAllowed));
            SourceContractGuard.Range((int)customBlockState, 0, 2, nameof(customBlockState));
            Identity = identity ?? throw new ArgumentNullException(nameof(identity));
            Career = career ?? throw new ArgumentNullException(nameof(career));
            if (customPlays != null && customPlays.Count != 9)
                throw new ArgumentException("Retain all nine original slot identities.", nameof(customPlays));
            var copy = new SourceCustomPlay[9];
            if (customPlays != null) for (int i = 0; i < 9; i++)
            {
                copy[i] = customPlays[i];
                if (customBlockState == SourceCustomBlockState.NeverAllocated && copy[i] != null && copy[i].Exists)
                    throw new ArgumentException("A never-allocated custom block cannot contain a saved play.", nameof(customBlockState));
            }
            RecordNumber = recordNumber; DiskBank = diskBank; PlaysAllowed = playsAllowed;
            CustomPlays = Array.AsReadOnly(copy); Audibles = audibles ?? SourceRecordAudibles.Defaults;
            CustomBlockState = customBlockState;
        }
    }

    public enum SourceRecordSelectionState { Guest, KnownNew, KnownSaved, Unavailable }

    public sealed class SourceStationRecordSelection
    {
        public int StationId { get; }
        public SourceRecordSelectionState State { get; }
        public SourceRecordIdentity Identity { get; }
        public SourcePlayerRecord Record { get; }
        public SourceStationRecordSelection(int stationId, SourceRecordSelectionState state,
            SourceRecordIdentity identity = null, SourcePlayerRecord record = null)
        {
            SourceContractGuard.Station(stationId, false, nameof(stationId));
            SourceContractGuard.Range((int)state, 0, 3, nameof(state));
            if ((state == SourceRecordSelectionState.Guest || state == SourceRecordSelectionState.Unavailable) &&
                (identity != null || record != null))
                throw new ArgumentException("Guests/unavailable stations cannot carry saved identity data.");
            if (state == SourceRecordSelectionState.KnownNew && identity == null)
                throw new ArgumentException("A deliberately new identity is required.");
            if (state == SourceRecordSelectionState.KnownSaved && record == null)
                throw new ArgumentException("An actually loaded saved record is required.");
            if (record != null && identity != null && !record.Identity.Equals(identity))
                throw new ArgumentException("The selected saved record belongs to a different identity.");
            StationId = stationId; State = state; Identity = identity ?? (record == null ? null : record.Identity);
            Record = record;
        }
    }

    public enum SourceSavedCustomChoiceKind { Unavailable, Missing, Existing, SharedFirstPageDrawRequired }

    public readonly struct SourceSavedCustomChoice
    {
        public SourceSavedCustomChoiceKind Kind { get; }
        public int SlotIndex { get; }
        public SourceCustomPlay Play { get; }
        public SourceSavedCustomChoice(SourceSavedCustomChoiceKind kind, int slotIndex, SourceCustomPlay play)
        { Kind = kind; SlotIndex = slotIndex; Play = play; }
    }

    public sealed class SourceSavedRecordSession
    {
        public SourceMatchConfiguration Configuration { get; }
        public ReadOnlyCollection<SourceStationRecordSelection> Stations { get; }
        public SourceSavedRecordSession(SourceMatchConfiguration configuration,
            IReadOnlyList<SourceStationRecordSelection> stations)
        {
            Configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            if (stations == null || stations.Count != configuration.Stations.Count)
                throw new ArgumentException("Select record state for each configured physical station.", nameof(stations));
            var copy = new SourceStationRecordSelection[stations.Count];
            var seen = new bool[4];
            for (int i = 0; i < copy.Length; i++)
            {
                SourceStationRecordSelection item = stations[i];
                if (item == null || seen[item.StationId]) throw new ArgumentException("Duplicate/missing station selection.");
                bool found = false;
                foreach (SourceStationConfiguration station in configuration.Stations)
                    if (station.StationId == item.StationId) { found = true; break; }
                if (!found) throw new ArgumentException("A saved record cannot belong to an unconfigured station.");
                seen[item.StationId] = true; copy[i] = item;
            }
            Stations = Array.AsReadOnly(copy);
        }

        public SourceStationRecordSelection Station(int stationId)
        {
            SourceContractGuard.Station(stationId, false, nameof(stationId));
            foreach (SourceStationRecordSelection item in Stations)
                if (item.StationId == stationId) return item;
            throw new ArgumentException("The station is not configured.", nameof(stationId));
        }

        // Missing manual choices return NULL; only the actual source caller draws RNG
        // when a forced scan proves that none of this station's nine slots exists.
        public SourceSavedCustomChoice ResolveCustom(int stationId, int slotIndex, bool forced)
        {
            SourceContractGuard.Range(slotIndex, 0, 8, nameof(slotIndex));
            SourceStationRecordSelection selected = Station(stationId);
            if (selected.State == SourceRecordSelectionState.Unavailable)
                return new SourceSavedCustomChoice(SourceSavedCustomChoiceKind.Unavailable, -1, null);
            SourcePlayerRecord record = selected.Record;
            if (record != null && record.CustomPlays[slotIndex] != null && record.CustomPlays[slotIndex].Exists)
                return new SourceSavedCustomChoice(SourceSavedCustomChoiceKind.Existing, slotIndex, record.CustomPlays[slotIndex]);
            if (!forced) return new SourceSavedCustomChoice(SourceSavedCustomChoiceKind.Missing, -1, null);
            if (record != null) for (int i = 0; i < 9; i++)
                if (record.CustomPlays[i] != null && record.CustomPlays[i].Exists)
                    return new SourceSavedCustomChoice(SourceSavedCustomChoiceKind.Existing, i, record.CustomPlays[i]);
            return new SourceSavedCustomChoice(SourceSavedCustomChoiceKind.SharedFirstPageDrawRequired, -1, null);
        }
    }

    public readonly struct SourceRecordMatchId
    {
        public string SessionId { get; }
        public long MatchNumber { get; }
        public SourceRecordMatchId(string sessionId, long matchNumber)
        {
            Guid value;
            // TryParseExact trims whitespace, but receipt keys must not acquire aliases.
            if (sessionId == null || sessionId.Length != 32 || !Guid.TryParseExact(sessionId, "N", out value) || value == Guid.Empty)
                throw new ArgumentException("A deliberate session's nonempty GUID in N format is required.", nameof(sessionId));
            if (matchNumber < 1) throw new ArgumentOutOfRangeException(nameof(matchNumber));
            SessionId = sessionId.ToLowerInvariant(); MatchNumber = matchNumber;
        }
    }
}
