using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Blitz.Contracts
{
    public static class SourceMatchLimits
    {
        public const int TeamCount = 31;
        public const int RosterRecordsPerTeam = 16;
        public const int MaximumHumanStations = 4;
        public const int MaximumPeriodCount = 7;
    }

    public enum SourceSimulationProfile { LegacyBaseline60Hz, OriginalRelease57Hz }
    public enum SourceStationLayout { TwoStation = 2, FourStation = 4 }
    public enum SourceCaptainSlot { First = 0, Second = 1 }

    public readonly struct SourceRosterIdentity
    {
        private readonly int teamIndex;
        private readonly int rosterIndex;
        public bool IsSpecified { get; }
        public int TeamIndex { get { return IsSpecified ? teamIndex : -1; } }
        public int RosterIndex { get { return IsSpecified ? rosterIndex : -1; } }

        public SourceRosterIdentity(int teamIndex, int rosterIndex)
        {
            SourceContractGuard.Range(teamIndex, 0, SourceMatchLimits.TeamCount - 1, nameof(teamIndex));
            SourceContractGuard.Range(rosterIndex, 0, SourceMatchLimits.RosterRecordsPerTeam - 1, nameof(rosterIndex));
            this.teamIndex = teamIndex;
            this.rosterIndex = rosterIndex;
            IsSpecified = true;
        }
    }

    public sealed class SourceRosterProfile
    {
        public SourceRosterIdentity Identity { get; }
        public string SourcePlayerId { get; }
        public int PackedJerseyNumber { get; }
        public float ModelScale { get; }
        public string FirstName { get; }
        public string LastName { get; }

        public SourceRosterProfile(SourceRosterIdentity identity, string sourcePlayerId,
            int packedJerseyNumber, float modelScale, string firstName = "", string lastName = "")
        {
            if (!identity.IsSpecified)
                throw new ArgumentException("A source team and static roster index are required.", nameof(identity));
            if (string.IsNullOrWhiteSpace(sourcePlayerId))
                throw new ArgumentException("The original roster-symbol/static-index key is required.", nameof(sourcePlayerId));
            SourceContractGuard.Range(packedJerseyNumber, 0, 255, nameof(packedJerseyNumber));
            SourceContractGuard.Finite(modelScale, nameof(modelScale));
            if (modelScale <= 0f)
                throw new ArgumentOutOfRangeException(nameof(modelScale));
            if (firstName == null)
                throw new ArgumentNullException(nameof(firstName));
            if (lastName == null)
                throw new ArgumentNullException(nameof(lastName));
            Identity = identity;
            SourcePlayerId = sourcePlayerId;
            PackedJerseyNumber = packedJerseyNumber;
            ModelScale = modelScale;
            FirstName = firstName;
            LastName = lastName;
        }
    }

    public sealed class SourceTeamRoster
    {
        public int TeamIndex { get; }
        public string SourceRosterSymbol { get; }
        public ReadOnlyCollection<SourceRosterProfile> Players { get; }

        public SourceTeamRoster(int teamIndex, string sourceRosterSymbol, IReadOnlyList<SourceRosterProfile> players)
        {
            SourceContractGuard.Range(teamIndex, 0, SourceMatchLimits.TeamCount - 1, nameof(teamIndex));
            if (string.IsNullOrWhiteSpace(sourceRosterSymbol))
                throw new ArgumentException("The original static roster symbol is required.", nameof(sourceRosterSymbol));
            if (players == null)
                throw new ArgumentNullException(nameof(players));
            if (players.Count != SourceMatchLimits.RosterRecordsPerTeam)
                throw new ArgumentException("A source team has sixteen static roster records.", nameof(players));
            var copy = new SourceRosterProfile[players.Count];
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < copy.Length; i++)
            {
                SourceRosterProfile player = players[i];
                if (player == null || player.Identity.TeamIndex != teamIndex || player.Identity.RosterIndex != i
                    || !seen.Add(player.SourcePlayerId))
                    throw new ArgumentException("Roster records must retain unique source identities in original static order.", nameof(players));
                copy[i] = player;
            }
            TeamIndex = teamIndex;
            SourceRosterSymbol = sourceRosterSymbol;
            Players = Array.AsReadOnly(copy);
        }
    }

    public interface ISourceTeamRoster
    {
        string SourceRevision { get; }
        IReadOnlyList<SourceTeamRoster> Teams { get; }
        SourceTeamRoster GetTeam(int sourceTeamId);
        SourceRosterProfile GetPlayer(string sourcePlayerId);
    }

    public sealed class SourceTeamSelection
    {
        public int TeamIndex { get; }
        public ReadOnlyCollection<int> RosterIndices { get; }

        public SourceTeamSelection(int teamIndex, IReadOnlyList<int> rosterIndices)
        {
            SourceContractGuard.Range(teamIndex, 0, SourceMatchLimits.TeamCount - 1, nameof(teamIndex));
            if (rosterIndices == null)
                throw new ArgumentNullException(nameof(rosterIndices));
            if (rosterIndices.Count != SliceRules.PlayersPerTeam)
                throw new ArgumentException("Select one source roster record for each of the seven actor slots.", nameof(rosterIndices));
            var copy = new int[rosterIndices.Count];
            for (int i = 0; i < copy.Length; i++)
            {
                SourceContractGuard.Range(rosterIndices[i], 0, SourceMatchLimits.RosterRecordsPerTeam - 1, nameof(rosterIndices));
                copy[i] = rosterIndices[i];
            }
            TeamIndex = teamIndex;
            RosterIndices = Array.AsReadOnly(copy);
        }
    }

    public readonly struct SourceStationConfiguration
    {
        private readonly int stationId;
        private readonly int initialPlayerId;
        public bool IsSpecified { get; }
        public int StationId { get { return IsSpecified ? stationId : -1; } }
        public TeamId Team { get; }
        public int InitialPlayerId { get { return IsSpecified ? initialPlayerId : -1; } }
        public SourceRosterIdentity Profile { get; }

        public SourceStationConfiguration(int stationId, TeamId team, int initialPlayerId = -1,
            SourceRosterIdentity profile = default(SourceRosterIdentity))
        {
            SourceContractGuard.Station(stationId, false, nameof(stationId));
            SourceContractGuard.Team(team, nameof(team));
            SourceContractGuard.Player(initialPlayerId, nameof(initialPlayerId));
            if (initialPlayerId >= 0 && initialPlayerId / SliceRules.PlayersPerTeam != (int)team)
                throw new ArgumentException("The initial actor must belong to the station's team.", nameof(initialPlayerId));
            this.stationId = stationId;
            this.initialPlayerId = initialPlayerId;
            Team = team;
            Profile = profile;
            IsSpecified = true;
        }
    }

    public sealed class SourceMatchConfiguration
    {
        public SourceSimulationProfile Profile { get; }
        public SourceStationLayout StationLayout { get; }
        public SourceTeamSelection HomeTeam { get; }
        public SourceTeamSelection AwayTeam { get; }
        public ReadOnlyCollection<SourceStationConfiguration> Stations { get; }
        public TeamId FirstPossession { get; }
        public SourceCaptainSlot Captain { get; }
        public int TimeAdjustment { get; }
        public int Difficulty { get; }
        public bool ContinueAfterCabinetGate { get; }
        public SourcePlaySelection HomePlay { get; }
        public SourcePlaySelection AwayPlay { get; }

        public SourceMatchConfiguration(SourceSimulationProfile profile, SourceStationLayout stationLayout,
            SourceTeamSelection homeTeam, SourceTeamSelection awayTeam,
            IReadOnlyList<SourceStationConfiguration> stations, TeamId firstPossession, SourceCaptainSlot captain,
            int timeAdjustment = 2, int difficulty = 5, bool continueAfterCabinetGate = true,
            SourcePlaySelection homePlay = default(SourcePlaySelection),
            SourcePlaySelection awayPlay = default(SourcePlaySelection))
        {
            SourceContractGuard.Range((int)profile, 0, 1, nameof(profile));
            if (stationLayout != SourceStationLayout.TwoStation && stationLayout != SourceStationLayout.FourStation)
                throw new ArgumentOutOfRangeException(nameof(stationLayout));
            if (homeTeam == null)
                throw new ArgumentNullException(nameof(homeTeam));
            if (awayTeam == null)
                throw new ArgumentNullException(nameof(awayTeam));
            if (stations == null)
                throw new ArgumentNullException(nameof(stations));
            if (stations.Count > (int)stationLayout)
                throw new ArgumentException("The station count exceeds the configured cabinet layout.", nameof(stations));
            SourceContractGuard.Team(firstPossession, nameof(firstPossession));
            SourceContractGuard.Range((int)captain, 0, 1, nameof(captain));
            SourceContractGuard.Range(timeAdjustment, 0, 4, nameof(timeAdjustment));
            SourceContractGuard.Range(difficulty, 0, 10, nameof(difficulty));
            var copy = new SourceStationConfiguration[stations.Count];
            var seen = new bool[SourceMatchLimits.MaximumHumanStations];
            for (int i = 0; i < copy.Length; i++)
            {
                SourceStationConfiguration station = stations[i];
                if (!station.IsSpecified || seen[station.StationId])
                    throw new ArgumentException("Stations must be specified and unique.", nameof(stations));
                int selectedTeam = station.Team == TeamId.Home ? homeTeam.TeamIndex : awayTeam.TeamIndex;
                if (station.Profile.IsSpecified && station.Profile.TeamIndex != selectedTeam)
                    throw new ArgumentException("A station profile must belong to its selected source team.", nameof(stations));
                seen[station.StationId] = true;
                copy[i] = station;
            }
            Profile = profile;
            StationLayout = stationLayout;
            HomeTeam = homeTeam;
            AwayTeam = awayTeam;
            Stations = Array.AsReadOnly(copy);
            FirstPossession = firstPossession;
            Captain = captain;
            TimeAdjustment = timeAdjustment;
            Difficulty = difficulty;
            ContinueAfterCabinetGate = continueAfterCabinetGate;
            HomePlay = homePlay;
            AwayPlay = awayPlay;
        }
    }

    internal static class SourceContractGuard
    {
        internal static void Range(int value, int minimum, int maximum, string name)
        {
            if (value < minimum || value > maximum)
                throw new ArgumentOutOfRangeException(name);
        }

        internal static void Team(TeamId value, string name)
        {
            Range((int)value, 0, 1, name);
        }

        internal static void Player(int value, string name)
        {
            Range(value, -1, SliceRules.PlayersPerTeam * 2 - 1, name);
        }

        internal static void Station(int value, bool allowUnassigned, string name)
        {
            Range(value, allowUnassigned ? -1 : 0, SourceMatchLimits.MaximumHumanStations - 1, name);
        }

        internal static void Finite(float value, string name)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
                throw new ArgumentOutOfRangeException(name);
        }

        internal static void Buttons(SourceButtons value, string name)
        {
            if (((int)value & ~7) != 0)
                throw new ArgumentOutOfRangeException(name);
        }
    }
}
