using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Blitz.Contracts
{
    public enum SourceGameplayCompatibility { StrictSource = 0, Portable = 1 }
    public enum SourceCompatibilityRepairKind
    {
        HandicapConsumerIndex, BlockCountConsumerIndex, TargetlessRunningPush, CarrierlessDefensiveSwitch
    }

    public readonly struct SourceCompatibilityRepair
    {
        public long Sequence { get; }
        public long SourceTick { get; }
        public SourceCompatibilityRepairKind Kind { get; }
        public int SubjectId { get; }
        public int RawIndex { get; }
        public int EffectiveIndex { get; }
        public string SourceContext { get; }
        public int? RetainedStick { get; }

        public SourceCompatibilityRepair(long sequence, long sourceTick, SourceCompatibilityRepairKind kind,
            int subjectId, int rawIndex, int effectiveIndex, string sourceContext, int? retainedStick = null)
        {
            Sequence = sequence; SourceTick = sourceTick; Kind = kind; SubjectId = subjectId;
            RawIndex = rawIndex; EffectiveIndex = effectiveIndex; SourceContext = sourceContext;
            RetainedStick = retainedStick;
        }
    }

    public static class SourceGameplayCompatibilityOptions
    {
        public static SourceGameplayCompatibility Parse(string value)
        {
            if (value == "strict") return SourceGameplayCompatibility.StrictSource;
            if (value == "portable") return SourceGameplayCompatibility.Portable;
            throw new ArgumentException("source-compatibility must be exactly strict or portable.", nameof(value));
        }

        public static SourceGameplayCompatibility FromUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return SourceGameplayCompatibility.StrictSource;
            int query = url.IndexOf('?');
            int fragment = url.IndexOf('#');
            if (query < 0 || fragment >= 0 && fragment < query) return SourceGameplayCompatibility.StrictSource;
            string tail = url.Substring(query + 1).Split('#')[0];
            SourceGameplayCompatibility result = SourceGameplayCompatibility.StrictSource;
            bool seen = false;
            foreach (string part in tail.Split('&'))
            {
                string[] pair = part.Split('=');
                if (Uri.UnescapeDataString(pair[0]) != "source-compatibility") continue;
                if (seen || pair.Length != 2)
                    throw new ArgumentException("Specify source-compatibility exactly once with a value.", nameof(url));
                seen = true;
                result = Parse(Uri.UnescapeDataString(pair[1]));
            }
            return result;
        }
    }

    public enum SourcePlayKind { Normal = 0, Kickoff = 1, FieldGoal = 2, Punt = 3, ExtraPoint = 4 }
    public enum SourceResultKind
    {
        Down = 0, Touchdown = 1, Safety = 2, PatSafety = 3, NewPossession = 4,
        Incomplete = 5, FumbledOutOfBounds = 6, FieldGoal = 7, ExtraPoint = 8,
        MissedKick = 9, FailedConversion = 10, Conversion = 11, PuntOutOfBounds = 12,
        DownedPunt = 13, KickoffOutOfBounds = 14, Touchback = 15
    }
    public enum SourceResultStatus { Classified = 0, IntentionalNoOp = 1, InvalidSourceBranch = 2 }
    public enum SourcePlayEndKind
    {
        CarrierOutOfBounds = 0, CarrierTackledInBounds = 1, CarrierDownInOwnEndZone = 2,
        CarrierTouchdown = 3, DeadBallInBounds = 4, BallOutOfBounds = 5
    }
    public enum SourcePatOption { Unselected = 0, FreeKick = 1, TwoPointPlay = 2 }
    public enum SourcePatStatus
    {
        NotExtraPoint = 0, SkipUnnecessary = 1, AwaitHumanSelection = 2,
        AwaitRandom = 3, RunTwoPointPlay = 4, FreeKickComplete = 5
    }
    public enum SourceKickoffType { Opening = 0, Quick = 1, Onside = 2 }
    public enum SourcePeriodStatus
    {
        NotExpired = 0, FinishCurrentPlay = 1, DeferredForExtraPoint = 2, AttractMode = 3,
        AwaitOvertimeDraw = 4, NextQuarter = 5, HalftimeKickoff = 6, OvertimeKickoff = 7,
        MatchEnded = 8, CabinetContinuationDeclined = 9
    }

    // Presentation-facing tags, not the numeric GS_* identifiers in GAME.H.
    public enum SourceMatchPhase
    {
        Unspecified, PlaySelection, PatSelection, PreSnap, LiveBall,
        PlayResult, FreePointResult, PeriodTransition, MatchEnded
    }

    public enum SourceEventType
    {
        Unspecified, PlaySelected, Snapped, PassReleased, PassCaught, PassDeflected,
        PassMissed, Intercepted, Fumbled, Bobbled, LateralReleased, Punted, Kicked,
        PlayResolved, PatSelected, FreePointResult, PeriodChanged, PossessionChanged,
        ControllerChanged, MatchEnded
    }

    public readonly struct SourceMatchResult
    {
        public bool IsSpecified { get; }
        public SourcePlayKind PlayKind { get; }
        public SourceResultKind Kind { get; }
        public SourceResultStatus? Status { get; }
        public SourcePlayEndKind? Cause { get; }
        public int HomePoints { get; }
        public int AwayPoints { get; }
        public bool JustGotFirstDown { get; }
        public bool TurnoverOnDowns { get; }

        public SourceMatchResult(SourcePlayKind playKind, SourceResultKind kind, SourceResultStatus? status = null,
            SourcePlayEndKind? cause = null, int homePoints = 0, int awayPoints = 0,
            bool justGotFirstDown = false, bool turnoverOnDowns = false)
        {
            SourceContractGuard.Range((int)playKind, 0, 4, nameof(playKind));
            SourceContractGuard.Range((int)kind, 0, 15, nameof(kind));
            if (status.HasValue)
                SourceContractGuard.Range((int)status.Value, 0, 2, nameof(status));
            if (cause.HasValue)
                SourceContractGuard.Range((int)cause.Value, 0, 5, nameof(cause));
            SourceContractGuard.Range(homePoints, 0, int.MaxValue, nameof(homePoints));
            SourceContractGuard.Range(awayPoints, 0, int.MaxValue, nameof(awayPoints));
            PlayKind = playKind;
            Kind = kind;
            Status = status;
            Cause = cause;
            HomePoints = homePoints;
            AwayPoints = awayPoints;
            JustGotFirstDown = justGotFirstDown;
            TurnoverOnDowns = turnoverOnDowns;
            IsSpecified = true;
        }
    }

    public readonly struct SourcePeriodView
    {
        private readonly int quarterIndex;
        public bool IsSpecified { get; }
        public int QuarterIndex { get { return IsSpecified ? quarterIndex : -1; } }
        public SourcePeriodStatus Status { get; }
        public int? CompletedQuarterIndex { get; }
        public bool CabinetGateRequired { get; }

        public SourcePeriodView(int quarterIndex, SourcePeriodStatus status = SourcePeriodStatus.NotExpired,
            int? completedQuarterIndex = null, bool cabinetGateRequired = false)
        {
            SourceContractGuard.Range(quarterIndex, 0, SourceMatchLimits.MaximumPeriodCount - 1, nameof(quarterIndex));
            SourceContractGuard.Range((int)status, 0, 9, nameof(status));
            if (completedQuarterIndex.HasValue)
                SourceContractGuard.Range(completedQuarterIndex.Value, 0, SourceMatchLimits.MaximumPeriodCount - 1, nameof(completedQuarterIndex));
            this.quarterIndex = quarterIndex;
            Status = status;
            CompletedQuarterIndex = completedQuarterIndex;
            CabinetGateRequired = cabinetGateRequired;
            IsSpecified = true;
        }
    }

    public readonly struct SourcePossessionView
    {
        public bool IsSpecified { get; }
        public TeamId Offense { get; }
        public TeamId InitialOffense { get; }
        public TeamId LastPossession { get; }
        public TeamId FirstPossession { get; }
        public SourceCaptainSlot Captain { get; }

        public SourcePossessionView(TeamId offense, TeamId initialOffense, TeamId lastPossession,
            TeamId firstPossession, SourceCaptainSlot captain)
        {
            SourceContractGuard.Team(offense, nameof(offense));
            SourceContractGuard.Team(initialOffense, nameof(initialOffense));
            SourceContractGuard.Team(lastPossession, nameof(lastPossession));
            SourceContractGuard.Team(firstPossession, nameof(firstPossession));
            SourceContractGuard.Range((int)captain, 0, 1, nameof(captain));
            Offense = offense;
            InitialOffense = initialOffense;
            LastPossession = lastPossession;
            FirstPossession = firstPossession;
            Captain = captain;
            IsSpecified = true;
        }
    }

    public readonly struct SourceQuarterScore
    {
        private readonly int quarterIndex;
        public bool IsSpecified { get; }
        public int QuarterIndex { get { return IsSpecified ? quarterIndex : -1; } }
        public int HomeScore { get; }
        public int AwayScore { get; }

        public SourceQuarterScore(int quarterIndex, int homeScore, int awayScore)
        {
            SourceContractGuard.Range(quarterIndex, 0, SourceMatchLimits.MaximumPeriodCount - 1, nameof(quarterIndex));
            SourceContractGuard.Range(homeScore, 0, int.MaxValue, nameof(homeScore));
            SourceContractGuard.Range(awayScore, 0, int.MaxValue, nameof(awayScore));
            this.quarterIndex = quarterIndex;
            HomeScore = homeScore;
            AwayScore = awayScore;
            IsSpecified = true;
        }
    }

    public readonly struct SourceTeamGameplayView
    {
        public bool IsSpecified { get; }
        public TeamId Team { get; }
        public bool OnFire { get; }
        public int LastReceiverPlayerId { get; }
        public int ConsecutiveCompletions { get; }
        public int ConsecutiveLosses { get; }
        public int Handicap { get; }

        public int EffectiveHandicap { get; }
        public int RawHandicap { get { return Handicap; } }

        public SourceTeamGameplayView(TeamId team, bool onFire, int lastReceiverPlayerId,
            int consecutiveCompletions, int consecutiveLosses, int handicap, int? effectiveHandicap = null)
        {
            SourceContractGuard.Team(team, nameof(team));
            SourceContractGuard.Player(lastReceiverPlayerId, nameof(lastReceiverPlayerId));
            if (lastReceiverPlayerId >= 0 && lastReceiverPlayerId / SliceRules.PlayersPerTeam != (int)team)
                throw new ArgumentException("A fire receiver belongs to its actor's team.", nameof(lastReceiverPlayerId));
            SourceContractGuard.Range(consecutiveCompletions, 0, int.MaxValue, nameof(consecutiveCompletions));
            SourceContractGuard.Range(consecutiveLosses, 0, int.MaxValue, nameof(consecutiveLosses));
            SourceContractGuard.Range(effectiveHandicap ?? handicap, 0, 11, nameof(effectiveHandicap));
            Team = team;
            OnFire = onFire;
            LastReceiverPlayerId = lastReceiverPlayerId;
            ConsecutiveCompletions = consecutiveCompletions;
            ConsecutiveLosses = consecutiveLosses;
            Handicap = handicap;
            EffectiveHandicap = effectiveHandicap ?? handicap;
            IsSpecified = true;
        }
    }

    public sealed class SourceMatchView
    {
        public SourceMatchConfiguration Configuration { get; }
        public SourcePlayKind PlayKind { get; }
        public SourceMatchPhase Phase { get; }
        public SourceMatchResult Result { get; }
        public SourcePeriodView Period { get; }
        public SourcePossessionView Possession { get; }
        public SourcePlaySelection HomePlay { get; }
        public SourcePlaySelection AwayPlay { get; }
        public int SelectionCountdown { get; }
        public SourcePatOption PatChoice { get; }
        public SourcePatStatus PatStatus { get; }
        public SourceKickoffType? Kickoff { get; }
        public TeamId? KickingTeam { get; }
        public TeamId? ReceivingTeam { get; }
        public bool? OffenseFlipped { get; }
        public SourceTeamGameplayView HomeGameplay { get; }
        public SourceTeamGameplayView AwayGameplay { get; }
        public int ForwardCatcherPlayerId { get; }
        public bool DronesLose { get; }
        public bool DronesWinning { get; }
        public int HandicapRepetitionBonus { get; }
        public SourceGameplayCompatibility Compatibility { get; }
        public long CompatibilityRepairCount { get; }
        public ReadOnlyCollection<SourceCompatibilityRepair> CompatibilityRepairs { get; }
        public ReadOnlyCollection<SourceQuarterScore> QuarterScores { get; }
        public ReadOnlyCollection<SourceStationView> Stations { get; }

        public SourceMatchView(SourceMatchConfiguration configuration, SourcePlayKind playKind,
            SourceMatchPhase phase, SourcePeriodView period, SourcePossessionView possession,
            IReadOnlyList<SourceQuarterScore> quarterScores, IReadOnlyList<SourceStationView> stations,
            SourceMatchResult result = default(SourceMatchResult),
            SourcePlaySelection homePlay = default(SourcePlaySelection),
            SourcePlaySelection awayPlay = default(SourcePlaySelection),
            int selectionCountdown = -1, SourcePatOption patChoice = SourcePatOption.Unselected,
            SourcePatStatus patStatus = SourcePatStatus.NotExtraPoint, SourceKickoffType? kickoff = null,
            TeamId? kickingTeam = null, TeamId? receivingTeam = null, bool? offenseFlipped = null,
            SourceTeamGameplayView homeGameplay = default(SourceTeamGameplayView),
            SourceTeamGameplayView awayGameplay = default(SourceTeamGameplayView),
            int forwardCatcherPlayerId = -1, bool dronesLose = false, bool dronesWinning = false,
            int handicapRepetitionBonus = 0,
            SourceGameplayCompatibility compatibility = SourceGameplayCompatibility.StrictSource,
            long compatibilityRepairCount = 0, IReadOnlyList<SourceCompatibilityRepair> compatibilityRepairs = null)
        {
            if (configuration == null)
                throw new ArgumentNullException(nameof(configuration));
            SourceContractGuard.Range((int)playKind, 0, 4, nameof(playKind));
            SourceContractGuard.Range((int)phase, 0, 8, nameof(phase));
            if (!period.IsSpecified)
                throw new ArgumentException("The source period must be specified.", nameof(period));
            if (!possession.IsSpecified)
                throw new ArgumentException("Source possession must be specified.", nameof(possession));
            if (quarterScores == null)
                throw new ArgumentNullException(nameof(quarterScores));
            if (stations == null)
                throw new ArgumentNullException(nameof(stations));
            if (quarterScores.Count > SourceMatchLimits.MaximumPeriodCount)
                throw new ArgumentException("There are at most seven source periods.", nameof(quarterScores));
            if (stations.Count > configuration.Stations.Count)
                throw new ArgumentException("Station views must belong to configured stations.", nameof(stations));
            SourceContractGuard.Range(selectionCountdown, -1, int.MaxValue, nameof(selectionCountdown));
            SourceContractGuard.Range((int)patChoice, 0, 2, nameof(patChoice));
            SourceContractGuard.Range((int)patStatus, 0, 5, nameof(patStatus));
            if (kickoff.HasValue)
                SourceContractGuard.Range((int)kickoff.Value, 0, 2, nameof(kickoff));
            if (kickingTeam.HasValue)
                SourceContractGuard.Team(kickingTeam.Value, nameof(kickingTeam));
            if (receivingTeam.HasValue)
                SourceContractGuard.Team(receivingTeam.Value, nameof(receivingTeam));
            if (kickingTeam.HasValue && receivingTeam.HasValue && kickingTeam.Value == receivingTeam.Value)
                throw new ArgumentException("Kicking and receiving teams must differ.", nameof(receivingTeam));
            if (homeGameplay.IsSpecified && homeGameplay.Team != TeamId.Home
                || awayGameplay.IsSpecified && awayGameplay.Team != TeamId.Away)
                throw new ArgumentException("Gameplay views must retain home/away team ownership.");
            SourceContractGuard.Player(forwardCatcherPlayerId, nameof(forwardCatcherPlayerId));
            SourceContractGuard.Range(handicapRepetitionBonus, 0, 5, nameof(handicapRepetitionBonus));
            SourceContractGuard.Range((int)compatibility, 0, 1, nameof(compatibility));
            if (compatibilityRepairCount < 0) throw new ArgumentOutOfRangeException(nameof(compatibilityRepairCount));
            var repairCopy = new SourceCompatibilityRepair[compatibilityRepairs == null ? 0 : compatibilityRepairs.Count];
            for (int i = 0; i < repairCopy.Length; i++) repairCopy[i] = compatibilityRepairs[i];
            var scoresCopy = new SourceQuarterScore[quarterScores.Count];
            int previousQuarter = -1;
            for (int i = 0; i < scoresCopy.Length; i++)
            {
                SourceQuarterScore score = quarterScores[i];
                if (!score.IsSpecified || score.QuarterIndex <= previousQuarter)
                    throw new ArgumentException("Period scores must be specified, unique and in period order.", nameof(quarterScores));
                previousQuarter = score.QuarterIndex;
                scoresCopy[i] = score;
            }
            var stationsCopy = new SourceStationView[stations.Count];
            var seen = new bool[SourceMatchLimits.MaximumHumanStations];
            for (int i = 0; i < stationsCopy.Length; i++)
            {
                SourceStationView station = stations[i];
                if (!station.IsSpecified || seen[station.StationId])
                    throw new ArgumentException("Station views must be specified and unique.", nameof(stations));
                bool configured = false;
                foreach (SourceStationConfiguration choice in configuration.Stations)
                {
                    if (choice.StationId != station.StationId)
                        continue;
                    configured = true;
                    if (station.ControlledPlayerId >= 0 && station.ControlledPlayerId / SliceRules.PlayersPerTeam != (int)choice.Team)
                        throw new ArgumentException("A controlled actor must belong to its station's team.", nameof(stations));
                    break;
                }
                if (!configured)
                    throw new ArgumentException("Station views must belong to configured stations.", nameof(stations));
                seen[station.StationId] = true;
                stationsCopy[i] = station;
            }
            Configuration = configuration;
            PlayKind = playKind;
            Phase = phase;
            Result = result;
            Period = period;
            Possession = possession;
            HomePlay = homePlay;
            AwayPlay = awayPlay;
            SelectionCountdown = selectionCountdown;
            PatChoice = patChoice;
            PatStatus = patStatus;
            Kickoff = kickoff;
            KickingTeam = kickingTeam;
            ReceivingTeam = receivingTeam;
            OffenseFlipped = offenseFlipped;
            HomeGameplay = homeGameplay;
            AwayGameplay = awayGameplay;
            ForwardCatcherPlayerId = forwardCatcherPlayerId;
            DronesLose = dronesLose;
            DronesWinning = dronesWinning;
            HandicapRepetitionBonus = handicapRepetitionBonus;
            Compatibility = compatibility;
            CompatibilityRepairCount = compatibilityRepairCount;
            CompatibilityRepairs = Array.AsReadOnly(repairCopy);
            QuarterScores = Array.AsReadOnly(scoresCopy);
            Stations = Array.AsReadOnly(stationsCopy);
        }
    }

    public readonly struct SourceGameEvent
    {
        private readonly int actorPlayerId;
        private readonly int targetPlayerId;
        private readonly int stationId;
        public bool IsSpecified { get { return Type != SourceEventType.Unspecified; } }
        public long Tick { get; }
        public long SourceTick { get; }
        public SourceEventType Type { get; }
        public TeamId? Team { get; }
        public int ActorPlayerId { get { return IsSpecified ? actorPlayerId : -1; } }
        public int TargetPlayerId { get { return IsSpecified ? targetPlayerId : -1; } }
        public int StationId { get { return IsSpecified ? stationId : -1; } }
        public FieldVector Position { get; }
        public int Value { get; }
        public SourceMatchResult Result { get; }
        public SourcePlaySelection Selection { get; }
        public SourcePlayKind? PlayKind { get; }
        public SourcePatOption? PatChoice { get; }
        public SourceKickoffType? Kickoff { get; }

        public SourceGameEvent(long tick, long sourceTick, SourceEventType type, TeamId? team = null,
            int actorPlayerId = -1, int targetPlayerId = -1, int stationId = -1,
            FieldVector position = default(FieldVector), int value = 0,
            SourceMatchResult result = default(SourceMatchResult),
            SourcePlaySelection selection = default(SourcePlaySelection), SourcePlayKind? playKind = null,
            SourcePatOption? patChoice = null, SourceKickoffType? kickoff = null)
        {
            if (tick < 0)
                throw new ArgumentOutOfRangeException(nameof(tick));
            if (sourceTick < 0)
                throw new ArgumentOutOfRangeException(nameof(sourceTick));
            SourceContractGuard.Range((int)type, 1, 19, nameof(type));
            if (team.HasValue)
                SourceContractGuard.Team(team.Value, nameof(team));
            SourceContractGuard.Player(actorPlayerId, nameof(actorPlayerId));
            SourceContractGuard.Player(targetPlayerId, nameof(targetPlayerId));
            SourceContractGuard.Station(stationId, true, nameof(stationId));
            SourceContractGuard.Finite(position.X, nameof(position));
            SourceContractGuard.Finite(position.Z, nameof(position));
            if (playKind.HasValue)
                SourceContractGuard.Range((int)playKind.Value, 0, 4, nameof(playKind));
            if (patChoice.HasValue)
                SourceContractGuard.Range((int)patChoice.Value, 0, 2, nameof(patChoice));
            if (kickoff.HasValue)
                SourceContractGuard.Range((int)kickoff.Value, 0, 2, nameof(kickoff));
            Tick = tick;
            SourceTick = sourceTick;
            Type = type;
            Team = team;
            this.actorPlayerId = actorPlayerId;
            this.targetPlayerId = targetPlayerId;
            this.stationId = stationId;
            Position = position;
            Value = value;
            Result = result;
            Selection = selection;
            PlayKind = playKind;
            PatChoice = patChoice;
            Kickoff = kickoff;
        }
    }

    public interface ISourceMatchState : ISourceGameState
    {
        SourceMatchView Match { get; }
    }

    public interface ISourceMatchSimulation : IGameSimulation, IPendingSourceInput
    {
        IReadOnlyList<SourceGameEvent> SourceEvents { get; }
    }
}
