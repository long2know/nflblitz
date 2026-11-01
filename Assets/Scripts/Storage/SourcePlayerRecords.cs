using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Blitz.Contracts;

namespace Blitz.Storage
{
    public sealed class SourceCompletedRecordMatch
    {
        public SourceRecordMatchId Id { get; }
        public SourceSavedRecordSession Session { get; }
        public SourceMatchView FinalMatch { get; }
        public int HomeScore { get; }
        public int AwayScore { get; }
        public int CompletedPeriodCount { get; }
        public ReadOnlyCollection<int> QuarterParticipation { get; }
        public ReadOnlyCollection<int> TriviaPoints { get; }

        public SourceCompletedRecordMatch(SourceRecordMatchId id, SourceSavedRecordSession session,
            SourceMatchView finalMatch, int homeScore, int awayScore, int completedPeriodCount,
            IReadOnlyList<int> quarterParticipation, IReadOnlyList<int> triviaPoints = null)
        {
            if (string.IsNullOrEmpty(id.SessionId) || id.MatchNumber < 1)
                throw new ArgumentException("A deliberate session/match identity is required.", nameof(id));
            Session = session ?? throw new ArgumentNullException(nameof(session));
            FinalMatch = finalMatch ?? throw new ArgumentNullException(nameof(finalMatch));
            if (!ReferenceEquals(finalMatch.Configuration, session.Configuration))
                throw new ArgumentException("The final observation and station records require the same match configuration.");
            if (finalMatch.Phase != SourceMatchPhase.MatchEnded ||
                finalMatch.Period.Status != SourcePeriodStatus.MatchEnded)
                throw new ArgumentException("Career updates require an actual completed match, not a result screen.");
            if (completedPeriodCount < 4 || completedPeriodCount > 7 ||
                completedPeriodCount != (finalMatch.Period.CompletedQuarterIndex ?? finalMatch.Period.QuarterIndex) + 1)
                throw new ArgumentOutOfRangeException(nameof(completedPeriodCount));
            if (homeScore < 0 || awayScore < 0) throw new ArgumentOutOfRangeException(nameof(homeScore));
            if (!IsSourceSaveEligible(completedPeriodCount, homeScore, awayScore))
                throw new ArgumentException("Original game_over saves only completed winners or the seventh-period terminal tie.");
            if (finalMatch.QuarterScores.Count != completedPeriodCount)
                throw new ArgumentException("Completed-match scores require every actual period delta.");
            int measuredHome = 0, measuredAway = 0;
            foreach (SourceQuarterScore quarter in finalMatch.QuarterScores)
            {
                measuredHome = checked(measuredHome + quarter.HomeScore);
                measuredAway = checked(measuredAway + quarter.AwayScore);
            }
            if (measuredHome != homeScore || measuredAway != awayScore)
                throw new ArgumentException("Completed-match scores must agree with the actual retained period deltas.");
            if (quarterParticipation == null || quarterParticipation.Count != 4 ||
                triviaPoints != null && triviaPoints.Count != 4)
                throw new ArgumentException("Retain four physical station participation/trivia positions.");
            var quarters = new int[4];
            var trivia = new int[4];
            int fullMask = (1 << completedPeriodCount) - 1;
            for (int i = 0; i < 4; i++)
            {
                if (quarterParticipation[i] < 0 || (quarterParticipation[i] & ~fullMask) != 0)
                    throw new ArgumentOutOfRangeException(nameof(quarterParticipation));
                quarters[i] = quarterParticipation[i];
                if (triviaPoints != null) trivia[i] = triviaPoints[i];
            }
            foreach (SourceStationRecordSelection station in session.Stations)
            {
                int expectedSide = session.Configuration.StationLayout == SourceStationLayout.FourStation ?
                    station.StationId / 2 : station.StationId;
                bool matched = false;
                foreach (SourceStationConfiguration configured in session.Configuration.Stations)
                    if (configured.StationId == station.StationId && (int)configured.Team == expectedSide)
                    { matched = true; break; }
                if (!matched) throw new ArgumentException("Record updates require original physical-station/team mapping.");
                if (triviaPoints == null && station.Record != null)
                    trivia[station.StationId] = station.Record.Career.TriviaPoints;
            }
            for (int i = 0; i < 4; i++)
            {
                bool configured = false;
                foreach (SourceStationRecordSelection station in session.Stations)
                    if (station.StationId == i) { configured = true; break; }
                if (!configured && quarters[i] != 0)
                    throw new ArgumentException("Unconfigured stations cannot invent quarter participation.");
                if (trivia[i] < short.MinValue || trivia[i] > short.MaxValue)
                    throw new ArgumentOutOfRangeException(nameof(triviaPoints));
            }
            Id = id; HomeScore = homeScore; AwayScore = awayScore; CompletedPeriodCount = completedPeriodCount;
            QuarterParticipation = Array.AsReadOnly(quarters); TriviaPoints = Array.AsReadOnly(trivia);
        }

        // Only for the current cabinet policy: humans are fixed before wake1 and
        // cannot join/leave during the match. Other policies must pass real masks.
        public static SourceCompletedRecordMatch FromFixedParticipation(SourceRecordMatchId id,
            SourceSavedRecordSession session, SourceMatchView finalMatch, int homeScore, int awayScore,
            int completedPeriodCount)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));
            var masks = new int[4];
            if (completedPeriodCount < 4 || completedPeriodCount > 7)
                throw new ArgumentOutOfRangeException(nameof(completedPeriodCount));
            foreach (SourceStationRecordSelection station in session.Stations)
                masks[station.StationId] = (1 << completedPeriodCount) - 1;
            return new SourceCompletedRecordMatch(id, session, finalMatch, homeScore, awayScore,
                completedPeriodCount, masks);
        }

        public static bool IsSourceSaveEligible(int completedPeriodCount, int homeScore, int awayScore)
        {
            if (completedPeriodCount < 0 || completedPeriodCount > 7)
                throw new ArgumentOutOfRangeException(nameof(completedPeriodCount));
            if (homeScore < 0 || awayScore < 0) throw new ArgumentOutOfRangeException(nameof(homeScore));
            // nfl.c:2741-2751 guards BOTH career and world-record writers.
            return completedPeriodCount >= 4 && (homeScore != awayScore || completedPeriodCount == 7);
        }
    }

    public sealed class SourceRecordMatchUpdate
    {
        public SourcePlayerRecords Records { get; }
        public bool AlreadyCommitted { get; }
        public int UpdatedStations { get; }
        public int UpdatedWorldRecords { get; }
        public SourceRecordMatchUpdate(SourcePlayerRecords records, bool alreadyCommitted, int updatedStations)
            : this(records, alreadyCommitted, updatedStations, 0) { }
        public SourceRecordMatchUpdate(SourcePlayerRecords records, bool alreadyCommitted, int updatedStations, int updatedWorldRecords)
        {
            Records = records; AlreadyCommitted = alreadyCommitted; UpdatedStations = updatedStations;
            UpdatedWorldRecords = updatedWorldRecords;
        }
    }

    public sealed class SourcePlayerRecords
    {
        private readonly Dictionary<SourceRecordIdentity, SourcePlayerRecord> byIdentity;
        public ReadOnlyCollection<SourcePlayerRecord> Records { get; }
        public ReadOnlyDictionary<string, long> CommittedMatches { get; }
        public ReadOnlyCollection<SourceWorldRecord> WorldRecords { get; }

        public SourcePlayerRecords(IReadOnlyList<SourcePlayerRecord> records = null,
            IDictionary<string, long> committedMatches = null)
            : this(records, committedMatches, null) { }

        public SourcePlayerRecords(IReadOnlyList<SourcePlayerRecord> records,
            IDictionary<string, long> committedMatches, IReadOnlyList<SourceWorldRecord> worldRecords)
        {
            if (records != null && records.Count > 350) throw new ArgumentException("Original records are limited to 350.");
            var copy = new SourcePlayerRecord[records == null ? 0 : records.Count];
            var slots = new bool[350];
            byIdentity = new Dictionary<SourceRecordIdentity, SourcePlayerRecord>();
            for (int i = 0; i < copy.Length; i++)
            {
                SourcePlayerRecord record = records[i];
                if (record == null || slots[record.RecordNumber] || byIdentity.ContainsKey(record.Identity))
                    throw new ArgumentException("Record slots and original name/PIN identities must be unique.");
                ValidateRecord(record);
                slots[record.RecordNumber] = true; byIdentity.Add(record.Identity, record); copy[i] = record;
            }
            Array.Sort(copy, (a, b) => a.RecordNumber.CompareTo(b.RecordNumber));
            Records = Array.AsReadOnly(copy);
            var cursors = new Dictionary<string, long>(StringComparer.Ordinal);
            if (committedMatches != null)
                foreach (KeyValuePair<string, long> item in committedMatches)
                {
                    var id = new SourceRecordMatchId(item.Key, item.Value);
                    cursors.Add(id.SessionId, id.MatchNumber);
                }
            CommittedMatches = new ReadOnlyDictionary<string, long>(cursors);
            if (worldRecords != null && worldRecords.Count != SourceSavedRecordLimits.WorldRecordCount)
                throw new ArgumentException("Retain all six original global world-record categories.", nameof(worldRecords));
            var worlds = new SourceWorldRecord[SourceSavedRecordLimits.WorldRecordCount];
            for (int i = 0; i < worlds.Length; i++)
            {
                SourceWorldRecord world = worldRecords == null ? null : worldRecords[i];
                if (world != null && (int)world.Category != i)
                    throw new ArgumentException("Global world records must retain original category order.", nameof(worldRecords));
                worlds[i] = world;
            }
            WorldRecords = Array.AsReadOnly(worlds);
        }

        public SourcePlayerRecord Find(SourceRecordIdentity identity)
        {
            if (identity == null) throw new ArgumentNullException(nameof(identity));
            SourcePlayerRecord found;
            return byIdentity.TryGetValue(identity, out found) ? found : null;
        }

        public SourcePlayerRecords Create(SourceRecordIdentity identity, bool allowSourceLeastUsedReplacement = false)
        {
            if (Find(identity) != null) throw new InvalidOperationException("SOURCE_RECORD_EXISTS");
            int slot = SelectRecordNumber(allowSourceLeastUsedReplacement);
            return Put(new SourcePlayerRecord(slot, identity, new SourceRecordCareer()));
        }

        public SourcePlayerRecords Delete(SourceRecordIdentity identity)
        {
            if (Find(identity) == null) throw new InvalidOperationException("SOURCE_RECORD_NOT_FOUND");
            var list = new List<SourcePlayerRecord>();
            foreach (SourcePlayerRecord record in Records) if (!record.Identity.Equals(identity)) list.Add(record);
            return new SourcePlayerRecords(list, CommittedMatches, WorldRecords);
        }

        public static int UsableSlots(SourcePlayerRecord record)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));
            int[] wins = { 0, 0, 0, 5, 10, 15, 20, 25, 30 };
            int count = 0;
            while (count < 9 && Math.Max(0, record.Career.GamesWon) >= wins[count]) count++;
            return Math.Max(record.PlaysAllowed, count);
        }

        public SourcePlayerRecords SaveCustomPlay(SourceRecordIdentity identity, int slotIndex, SourceCustomPlay play)
        {
            SourcePlayerRecord record = Require(identity);
            if (slotIndex < 0 || slotIndex >= UsableSlots(record)) throw new ArgumentOutOfRangeException(nameof(slotIndex));
            SourceCustomPlay old = record.CustomPlays[slotIndex];
            if (old != null && (old.Flags & SourceCustomPlayFlags.Protected) != 0)
                throw new InvalidOperationException("SOURCE_CUSTOM_PLAY_PROTECTED");
            if (play != null)
            {
                SourceCustomPlayValidation validation = SourceCustomPlayCodec.ValidateAndNormalize(play);
                if (!validation.Valid || !play.Exists || ((int)play.Flags & ~127) != 0 ||
                    (play.Flags & SourceCustomPlayFlags.Unused) != 0)
                    throw new ArgumentException("SOURCE_CUSTOM_PLAY_INVALID");
                play = validation.Play;
            }
            var plays = new SourceCustomPlay[9];
            for (int i = 0; i < 9; i++) plays[i] = record.CustomPlays[i];
            plays[slotIndex] = play;
            var updated = new SourcePlayerRecord(record.RecordNumber, identity, record.Career, record.DiskBank,
                UsableSlots(record), plays, record.Audibles);
            // read_plays_hd clears the entire offensive row if one custom target
            // becomes invalid, rather than substituting a generic/missing play.
            if (!CustomAudiblesValid(updated))
                updated = new SourcePlayerRecord(updated.RecordNumber, identity, updated.Career, updated.DiskBank,
                    updated.PlaysAllowed, plays, new SourceRecordAudibles(new short[3], record.Audibles.Defense));
            return Put(updated);
        }

        public SourcePlayerRecords SaveAudibles(SourceRecordIdentity identity, SourceRecordAudibles audibles)
        {
            SourcePlayerRecord record = Require(identity);
            if (audibles == null) throw new ArgumentNullException(nameof(audibles));
            return Put(new SourcePlayerRecord(record.RecordNumber, identity, record.Career, record.DiskBank,
                record.PlaysAllowed, record.CustomPlays, audibles));
        }

        public SourceRecordMatchUpdate CommitMatch(SourceCompletedRecordMatch match)
        {
            if (match == null) throw new ArgumentNullException(nameof(match));
            long previous;
            CommittedMatches.TryGetValue(match.Id.SessionId, out previous);
            if (match.Id.MatchNumber <= previous) return new SourceRecordMatchUpdate(this, true, 0);
            if (match.Id.MatchNumber != previous + 1) throw new InvalidOperationException("SOURCE_MATCH_COMMIT_GAP");
            SourceMatchView final = match.FinalMatch;
            if (final.PlayActivityStats == null || final.PlayEndStats == null || final.CompletedPassStats == null ||
                final.PassCompletions == null || final.Interceptions == null)
                throw new InvalidOperationException("SOURCE_RECORD_STATISTICS_UNAVAILABLE");
            SourcePlayerRecords current = this;
            int updated = 0, fullMask = (1 << match.CompletedPeriodCount) - 1;
            var worlds = new SourceWorldRecord[SourceSavedRecordLimits.WorldRecordCount];
            for (int i = 0; i < worlds.Length; i++) worlds[i] = WorldRecords[i];
            int updatedWorlds = 0;
            // The original writer visits physical station order, not configuration array order.
            for (int stationId = 0; stationId < 4; stationId++)
            {
                SourceStationRecordSelection station = null;
                foreach (SourceStationRecordSelection item in match.Session.Stations)
                    if (item.StationId == stationId) { station = item; break; }
                if (station == null) continue;
                if (station.State == SourceRecordSelectionState.Unavailable)
                    throw new InvalidOperationException("SOURCE_RECORD_SELECTION_UNAVAILABLE");
                if (station.Identity != null)
                {
                    if (final.TouchdownStats == null)
                        throw new InvalidOperationException("SOURCE_RECORD_STATISTICS_UNAVAILABLE");
                    int worldSide = match.Session.Configuration.StationLayout == SourceStationLayout.FourStation ? stationId / 2 : stationId;
                    int scored = worldSide == 0 ? match.HomeScore : match.AwayScore;
                    int allowed = worldSide == 0 ? match.AwayScore : match.HomeScore;
                    int[] candidates = {
                        worldSide == 0 ? final.PlayEndStats.HomeSacks : final.PlayEndStats.AwaySacks,
                        worldSide == 0 ? final.TouchdownStats.HomeTouchdowns : final.TouchdownStats.AwayTouchdowns,
                        worldSide == 0 ? final.PlayActivityStats.HomeTackles : final.PlayActivityStats.AwayTackles,
                        worldSide == 0 ? final.Interceptions.HomeInterceptionsMade : final.Interceptions.AwayInterceptionsMade,
                        scored, scored - allowed
                    };
                    // check_world_records_4p visits actual named physical stations;
                    // unlike career credit it does NOT require a full quarter mask.
                    for (int category = 0; category < worlds.Length; category++)
                    {
                        int value = candidates[category];
                        if (category == (int)SourceWorldRecordCategory.VictoryMargin && value <= 0) continue;
                        if (worlds[category] != null && worlds[category].Value >= value) continue;
                        if (ReferenceEquals(worlds[category], WorldRecords[category])) updatedWorlds++;
                        worlds[category] = new SourceWorldRecord((SourceWorldRecordCategory)category, value, station.Identity);
                    }
                }
                if (station.State == SourceRecordSelectionState.Guest || match.QuarterParticipation[stationId] != fullMask)
                    continue;
                SourcePlayerRecord record = current.Find(station.Identity);
                bool existing = record != null;
                if (!existing) { current = current.Create(station.Identity); record = current.Require(station.Identity); }
                int side = match.Session.Configuration.StationLayout == SourceStationLayout.FourStation ? stationId / 2 : stationId;
                int ownScore = side == 0 ? match.HomeScore : match.AwayScore;
                int otherScore = side == 0 ? match.AwayScore : match.HomeScore;
                int opponent = side == 0 ? final.Configuration.AwayTeam.TeamIndex : final.Configuration.HomeTeam.TeamIndex;
                bool cpuOpponent = true;
                for (int other = 0; other < 4; other++)
                {
                    int otherSide = match.Session.Configuration.StationLayout == SourceStationLayout.FourStation ? other / 2 : other;
                    if (otherSide == 1 - side && match.QuarterParticipation[other] != 0) cpuOpponent = false;
                }
                SourceRecordCareer c = record.Career;
                bool won = ownScore > otherScore;
                int defeated = c.TeamsDefeated;
                if (won && (cpuOpponent || HumanOpponentAwardsDefeatedBit(opponent))) defeated |= 1 << opponent;
                var career = new SourceRecordCareer(existing ? 0 : 1, checked(c.GamesPlayed + 1),
                    checked(c.GamesWon + (won ? 1 : 0)), won ? checked(c.WinStreak + 1) : 0,
                    checked(c.PointsScored + ownScore), checked(c.PointsAllowed + otherScore), defeated,
                    checked(c.Tackles + (side == 0 ? final.PlayActivityStats.HomeTackles : final.PlayActivityStats.AwayTackles)),
                    checked(c.Sacks + (side == 0 ? final.PlayEndStats.HomeSacks : final.PlayEndStats.AwaySacks)),
                    checked(c.PassingYards + (side == 0 ? final.CompletedPassStats.HomePassingYards : final.CompletedPassStats.AwayPassingYards)),
                    checked(c.RushingYards + (side == 0 ? final.PlayEndStats.HomeRushingYards : final.PlayEndStats.AwayRushingYards)),
                    c.WorldRecords,
                    checked(c.PassAttempts + (side == 0 ? final.PlayActivityStats.HomePassAttempts : final.PlayActivityStats.AwayPassAttempts)),
                    checked(c.PassCompletions + (side == 0 ? final.PassCompletions.HomeCompletions : final.PassCompletions.AwayCompletions)),
                    checked(c.PassTouchdowns + (side == 0 ? final.CompletedPassStats.HomePassTouchdowns : final.CompletedPassStats.AwayPassTouchdowns)),
                    checked(c.Intercepted + (side == 0 ? final.Interceptions.AwayInterceptionsMade : final.Interceptions.HomeInterceptionsMade)),
                    match.TriviaPoints[stationId]);
                current = current.Put(new SourcePlayerRecord(record.RecordNumber, record.Identity, career,
                    record.DiskBank, record.PlaysAllowed, record.CustomPlays, record.Audibles));
                updated++;
            }
            var cursors = new Dictionary<string, long>(current.CommittedMatches, StringComparer.Ordinal);
            cursors[match.Id.SessionId] = match.Id.MatchNumber;
            return new SourceRecordMatchUpdate(new SourcePlayerRecords(current.Records, cursors, worlds), false, updated, updatedWorlds);
        }

        public static bool HumanOpponentAwardsDefeatedBit(int sourceTeamId)
        {
            if (sourceTeamId < 0 || sourceTeamId >= 31) throw new ArgumentOutOfRangeException(nameof(sourceTeamId));
            // teamdata.c:1076 team_ladder tiers7/8; GAME.H original numeric IDs.
            return sourceTeamId != 9 && sourceTeamId != 28 && sourceTeamId != 17 && sourceTeamId != 11;
        }

        private SourcePlayerRecord Require(SourceRecordIdentity identity)
        { return Find(identity) ?? throw new InvalidOperationException("SOURCE_RECORD_NOT_FOUND"); }
        private SourcePlayerRecords Put(SourcePlayerRecord record)
        {
            var list = new List<SourcePlayerRecord>();
            foreach (SourcePlayerRecord old in Records)
                if (old.RecordNumber != record.RecordNumber) list.Add(old);
            list.Add(record);
            return new SourcePlayerRecords(list, CommittedMatches, WorldRecords);
        }
        private int SelectRecordNumber(bool replace)
        {
            SourcePlayerRecord[] slots = new SourcePlayerRecord[350];
            foreach (SourcePlayerRecord record in Records) slots[record.RecordNumber] = record;
            int least = 0, count = -1;
            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i] == null || slots[i].Career.GamesPlayed <= 0 && slots[i].PlaysAllowed == 0) return i;
                if (slots[i].Career.LastUsed >= count) { count = slots[i].Career.LastUsed; least = i; }
            }
            if (!replace) throw new InvalidOperationException("SOURCE_RECORD_CAPACITY_REPLACEMENT_REQUIRES_CONSENT");
            return least;
        }
        private static void ValidateRecord(SourcePlayerRecord record)
        {
            for (int i = 0; i < 9; i++)
            {
                SourceCustomPlay play = record.CustomPlays[i];
                if (play == null) continue;
                SourceCustomPlayValidation valid = SourceCustomPlayCodec.ValidateAndNormalize(play);
                if (i >= record.PlaysAllowed || !play.Exists || ((int)play.Flags & ~127) != 0 ||
                    (play.Flags & SourceCustomPlayFlags.Unused) != 0 || !valid.Valid || valid.Repaired)
                    throw new ArgumentException("SOURCE_STORED_CUSTOM_PLAY_INVALID");
            }
            if (!CustomAudiblesValid(record)) throw new ArgumentException("SOURCE_STORED_CUSTOM_AUDIBLE_INVALID");
        }
        private static bool CustomAudiblesValid(SourcePlayerRecord record)
        {
            foreach (short signed in record.Audibles.Offense)
            {
                int id = Math.Abs((int)signed);
                if (id < 37) continue;
                SourceCustomPlay play = record.CustomPlays[id - 37];
                if (play == null || !play.Exists) return false;
                int flags = (int)play.Flags, primary = flags & 0x38;
                if ((flags & 64) != 0 || (flags & ~127) != 0 ||
                    primary != 0 && primary != 8 && primary != 16 && primary != 32) return false;
            }
            return true;
        }
    }
}
