using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using Blitz.Contracts;

internal static class Program
{
    private static int assertions;
    private static readonly HashSet<string> Cases = new HashSet<string>(StringComparer.Ordinal);

    private sealed class AssertionFailure : Exception
    {
        internal AssertionFailure(string caseId, string field, string expected, string actual)
            : base(caseId + ": " + field + " expected " + expected + ", observed " + actual) { }
    }

    private sealed class Inputs
    {
        internal readonly SourcePlaySelection HomePlay = new SourcePlaySelection(
            "component-offense-literal", SourcePlaybookKind.Offense, 4, 2, SourcePlayPool.ShortOffense, 6);
        internal readonly SourcePlaySelection AwayPlay = new SourcePlaySelection(
            "component-defense-literal", SourcePlaybookKind.Defense, 5, 3, SourcePlayPool.BlitzDefense, 7);
        internal readonly SourceTimeOfPossessionView Time = new SourceTimeOfPossessionView(17, 29);
        internal readonly SourcePeriodView Period = new SourcePeriodView(
            3, SourcePeriodStatus.FinishCurrentPlay, 2, true);
        internal readonly SourcePossessionView Possession = new SourcePossessionView(
            TeamId.Away, TeamId.Home, TeamId.Away, TeamId.Home, SourceCaptainSlot.Second);
        internal readonly SourceMatchResult Result = new SourceMatchResult(
            SourcePlayKind.Punt, SourceResultKind.DownedPunt, SourceResultStatus.IntentionalNoOp,
            SourcePlayEndKind.DeadBallInBounds, 7, 11, true, true);
        internal readonly SourceQuarterScore[] Scores = {
            new SourceQuarterScore(0, 7, 3), new SourceQuarterScore(3, 11, 13)
        };
        internal readonly SourceStationView[] Stations = {
            new SourceStationView(0, 2, 0.25f), new SourceStationView(3, 9, 0.75f)
        };
        internal readonly SourceTeamGameplayView HomeGameplay = new SourceTeamGameplayView(
            TeamId.Home, true, 2, 4, 6, 8, 9);
        internal readonly SourceTeamGameplayView AwayGameplay = new SourceTeamGameplayView(
            TeamId.Away, true, 9, 5, 7, 10, 11);
        internal readonly SourceCompatibilityRepair[] Repairs = {
            new SourceCompatibilityRepair(13, 17, SourceCompatibilityRepairKind.BlockCountConsumerIndex,
                9, 23, 5, "component-repair-literal", 8)
        };
        internal readonly SourceSelectionTimerView Timer = new SourceSelectionTimerView(
            41, 6, true, 0xff00ffffu, true, 31, false, true);
        internal readonly SourceRandomSelectionView HomeRandom = new SourceRandomSelectionView(
            19, true, 2, SourceRandomSelectionAction.Flip);
        internal readonly SourceRandomSelectionView AwayRandom = new SourceRandomSelectionView(
            23, true, 8, SourceRandomSelectionAction.Choose);
        internal readonly SourceMatchConfiguration Configuration;
        internal readonly SourceAudibleView Audibles;

        internal Inputs()
        {
            Configuration = new SourceMatchConfiguration(
                SourceSimulationProfile.OriginalRelease57Hz, SourceStationLayout.FourStation,
                new SourceTeamSelection(4, new[] { 0, 2, 4, 6, 8, 10, 12 }),
                new SourceTeamSelection(9, new[] { 1, 3, 5, 7, 9, 11, 13 }),
                new[] {
                    new SourceStationConfiguration(0, TeamId.Home, 2),
                    new SourceStationConfiguration(3, TeamId.Away, 9)
                }, TeamId.Away, SourceCaptainSlot.Second, 3, 8, false, HomePlay, AwayPlay);
            Audibles = new SourceAudibleView(37,
                new SourceAudibleTeamView(TeamId.Home, 11, 1, false),
                new SourceAudibleTeamView(TeamId.Away, 13, 1, false),
                new SourceAudibleRoleView(SourceAudibleRole.Offense, TeamId.Away, 3, 2, true, HomePlay, HomePlay),
                new SourceAudibleRoleView(SourceAudibleRole.Defense, TeamId.Home, 0, 1, true, AwayPlay, AwayPlay),
                true, true, new SourceProcessView(47, 12, "game_proc", 5, 0, 0, 37),
                SourceAudibleRestartStage.Polling, Array.Empty<SourceDigitalSwitchScan>(),
                Array.Empty<SourceAudibleSpriteView>(), Array.Empty<SourceAudibleFlashView>(),
                Array.Empty<SourceDirectSoundCall>());
        }
    }

    private static string Format(object value)
    {
        if (value == null) return "<null>";
        var formatted = value as IFormattable;
        return formatted == null ? value.ToString() : formatted.ToString(null, CultureInfo.InvariantCulture);
    }

    private static void Check(string caseId, string field, string expected, object value)
    {
        string actual = Format(value);
        bool passed = actual == expected;
        assertions++;
        Cases.Add(caseId);
        Console.WriteLine(JsonSerializer.Serialize(new {
            kind = "assertion", caseId, field, expected, observed = actual, passed
        }));
        if (!passed) throw new AssertionFailure(caseId, field, expected, actual);
    }

    private static SourceMatchView NewMatch(Inputs f, SourceInterceptionsView interceptions,
        SourceMatchPhase phase, SourceTimeOfPossessionView time)
    {
        return new SourceMatchView(interceptions, time, f.Configuration, SourcePlayKind.Punt,
            phase, f.Period, f.Possession, f.Scores, f.Stations,
            f.Result, f.HomePlay, f.AwayPlay, 12, SourcePatOption.TwoPointPlay,
            SourcePatStatus.RunTwoPointPlay, SourceKickoffType.Onside, TeamId.Away, TeamId.Home, true,
            f.HomeGameplay, f.AwayGameplay, 9, true, true, 4,
            SourceGameplayCompatibility.Portable, 2, f.Repairs, f.Timer,
            f.HomeRandom, f.AwayRandom, f.Audibles);
    }

    private static SourceMatchView OldTimeMatch(Inputs f, SourceMatchPhase phase,
        SourceTimeOfPossessionView time)
    {
        return new SourceMatchView(time, f.Configuration, SourcePlayKind.Punt,
            phase, f.Period, f.Possession, f.Scores, f.Stations,
            f.Result, f.HomePlay, f.AwayPlay, 12, SourcePatOption.TwoPointPlay,
            SourcePatStatus.RunTwoPointPlay, SourceKickoffType.Onside, TeamId.Away, TeamId.Home, true,
            f.HomeGameplay, f.AwayGameplay, 9, true, true, 4,
            SourceGameplayCompatibility.Portable, 2, f.Repairs, f.Timer,
            f.HomeRandom, f.AwayRandom, f.Audibles);
    }

    private static void VerifyForwarded(string caseId, SourceMatchView m, Inputs f)
    {
        Check(caseId, "Configuration.Reference", "True", ReferenceEquals(f.Configuration, m.Configuration));
        Check(caseId, "PlayKind", "Punt", m.PlayKind);
        Check(caseId, "Result.IsSpecified", "True", m.Result.IsSpecified);
        Check(caseId, "Result.PlayKind", "Punt", m.Result.PlayKind);
        Check(caseId, "Result.Kind", "DownedPunt", m.Result.Kind);
        Check(caseId, "Result.Status", "IntentionalNoOp", m.Result.Status);
        Check(caseId, "Result.Cause", "DeadBallInBounds", m.Result.Cause);
        Check(caseId, "Result.HomePoints", "7", m.Result.HomePoints);
        Check(caseId, "Result.AwayPoints", "11", m.Result.AwayPoints);
        Check(caseId, "Result.JustGotFirstDown", "True", m.Result.JustGotFirstDown);
        Check(caseId, "Result.TurnoverOnDowns", "True", m.Result.TurnoverOnDowns);
        Check(caseId, "Period.IsSpecified", "True", m.Period.IsSpecified);
        Check(caseId, "Period.QuarterIndex", "3", m.Period.QuarterIndex);
        Check(caseId, "Period.Status", "FinishCurrentPlay", m.Period.Status);
        Check(caseId, "Period.CompletedQuarterIndex", "2", m.Period.CompletedQuarterIndex);
        Check(caseId, "Period.CabinetGateRequired", "True", m.Period.CabinetGateRequired);
        Check(caseId, "Possession.IsSpecified", "True", m.Possession.IsSpecified);
        Check(caseId, "Possession.Offense", "Away", m.Possession.Offense);
        Check(caseId, "Possession.InitialOffense", "Home", m.Possession.InitialOffense);
        Check(caseId, "Possession.LastPossession", "Away", m.Possession.LastPossession);
        Check(caseId, "Possession.FirstPossession", "Home", m.Possession.FirstPossession);
        Check(caseId, "Possession.Captain", "Second", m.Possession.Captain);
        Check(caseId, "HomePlay.IsSpecified", "True", m.HomePlay.IsSpecified);
        Check(caseId, "HomePlay.PlaySymbol", "component-offense-literal", m.HomePlay.PlaySymbol);
        Check(caseId, "HomePlay.Playbook", "Offense", m.HomePlay.Playbook);
        Check(caseId, "HomePlay.SlotIndex", "4", m.HomePlay.SlotIndex);
        Check(caseId, "HomePlay.PageIndex", "2", m.HomePlay.PageIndex);
        Check(caseId, "HomePlay.Pool", "ShortOffense", m.HomePlay.Pool);
        Check(caseId, "HomePlay.PoolIndex", "6", m.HomePlay.PoolIndex);
        Check(caseId, "AwayPlay.IsSpecified", "True", m.AwayPlay.IsSpecified);
        Check(caseId, "AwayPlay.PlaySymbol", "component-defense-literal", m.AwayPlay.PlaySymbol);
        Check(caseId, "AwayPlay.Playbook", "Defense", m.AwayPlay.Playbook);
        Check(caseId, "AwayPlay.SlotIndex", "5", m.AwayPlay.SlotIndex);
        Check(caseId, "AwayPlay.PageIndex", "3", m.AwayPlay.PageIndex);
        Check(caseId, "AwayPlay.Pool", "BlitzDefense", m.AwayPlay.Pool);
        Check(caseId, "AwayPlay.PoolIndex", "7", m.AwayPlay.PoolIndex);
        Check(caseId, "SelectionCountdown", "12", m.SelectionCountdown);
        Check(caseId, "SelectionTimer.Reference", "True", ReferenceEquals(f.Timer, m.SelectionTimer));
        Check(caseId, "HomeRandomSelection.IsSpecified", "True", m.HomeRandomSelection.IsSpecified);
        Check(caseId, "HomeRandomSelection.SourceTick", "19", m.HomeRandomSelection.SourceTick);
        Check(caseId, "HomeRandomSelection.ControllerAwake", "True", m.HomeRandomSelection.ControllerAwake);
        Check(caseId, "HomeRandomSelection.Direction", "2", m.HomeRandomSelection.Direction);
        Check(caseId, "HomeRandomSelection.Action", "Flip", m.HomeRandomSelection.Action);
        Check(caseId, "AwayRandomSelection.IsSpecified", "True", m.AwayRandomSelection.IsSpecified);
        Check(caseId, "AwayRandomSelection.SourceTick", "23", m.AwayRandomSelection.SourceTick);
        Check(caseId, "AwayRandomSelection.ControllerAwake", "True", m.AwayRandomSelection.ControllerAwake);
        Check(caseId, "AwayRandomSelection.Direction", "8", m.AwayRandomSelection.Direction);
        Check(caseId, "AwayRandomSelection.Action", "Choose", m.AwayRandomSelection.Action);
        Check(caseId, "PatChoice", "TwoPointPlay", m.PatChoice);
        Check(caseId, "PatStatus", "RunTwoPointPlay", m.PatStatus);
        Check(caseId, "Kickoff", "Onside", m.Kickoff);
        Check(caseId, "KickingTeam", "Away", m.KickingTeam);
        Check(caseId, "ReceivingTeam", "Home", m.ReceivingTeam);
        Check(caseId, "OffenseFlipped", "True", m.OffenseFlipped);
        Check(caseId, "HomeGameplay.IsSpecified", "True", m.HomeGameplay.IsSpecified);
        Check(caseId, "HomeGameplay.Team", "Home", m.HomeGameplay.Team);
        Check(caseId, "HomeGameplay.OnFire", "True", m.HomeGameplay.OnFire);
        Check(caseId, "HomeGameplay.LastReceiverPlayerId", "2", m.HomeGameplay.LastReceiverPlayerId);
        Check(caseId, "HomeGameplay.ConsecutiveCompletions", "4", m.HomeGameplay.ConsecutiveCompletions);
        Check(caseId, "HomeGameplay.ConsecutiveLosses", "6", m.HomeGameplay.ConsecutiveLosses);
        Check(caseId, "HomeGameplay.Handicap", "8", m.HomeGameplay.Handicap);
        Check(caseId, "HomeGameplay.EffectiveHandicap", "9", m.HomeGameplay.EffectiveHandicap);
        Check(caseId, "AwayGameplay.IsSpecified", "True", m.AwayGameplay.IsSpecified);
        Check(caseId, "AwayGameplay.Team", "Away", m.AwayGameplay.Team);
        Check(caseId, "AwayGameplay.OnFire", "True", m.AwayGameplay.OnFire);
        Check(caseId, "AwayGameplay.LastReceiverPlayerId", "9", m.AwayGameplay.LastReceiverPlayerId);
        Check(caseId, "AwayGameplay.ConsecutiveCompletions", "5", m.AwayGameplay.ConsecutiveCompletions);
        Check(caseId, "AwayGameplay.ConsecutiveLosses", "7", m.AwayGameplay.ConsecutiveLosses);
        Check(caseId, "AwayGameplay.Handicap", "10", m.AwayGameplay.Handicap);
        Check(caseId, "AwayGameplay.EffectiveHandicap", "11", m.AwayGameplay.EffectiveHandicap);
        Check(caseId, "ForwardCatcherPlayerId", "9", m.ForwardCatcherPlayerId);
        Check(caseId, "DronesLose", "True", m.DronesLose);
        Check(caseId, "DronesWinning", "True", m.DronesWinning);
        Check(caseId, "HandicapRepetitionBonus", "4", m.HandicapRepetitionBonus);
        Check(caseId, "Compatibility", "Portable", m.Compatibility);
        Check(caseId, "CompatibilityRepairCount", "2", m.CompatibilityRepairCount);
        Check(caseId, "CompatibilityRepairs.Count", "1", m.CompatibilityRepairs.Count);
        Check(caseId, "CompatibilityRepairs[0].Sequence", "13", m.CompatibilityRepairs[0].Sequence);
        Check(caseId, "CompatibilityRepairs[0].SourceTick", "17", m.CompatibilityRepairs[0].SourceTick);
        Check(caseId, "CompatibilityRepairs[0].Kind", "BlockCountConsumerIndex", m.CompatibilityRepairs[0].Kind);
        Check(caseId, "CompatibilityRepairs[0].SubjectId", "9", m.CompatibilityRepairs[0].SubjectId);
        Check(caseId, "CompatibilityRepairs[0].RawIndex", "23", m.CompatibilityRepairs[0].RawIndex);
        Check(caseId, "CompatibilityRepairs[0].EffectiveIndex", "5", m.CompatibilityRepairs[0].EffectiveIndex);
        Check(caseId, "CompatibilityRepairs[0].SourceContext", "component-repair-literal", m.CompatibilityRepairs[0].SourceContext);
        Check(caseId, "CompatibilityRepairs[0].RetainedStick", "8", m.CompatibilityRepairs[0].RetainedStick);
        Check(caseId, "QuarterScores.Count", "2", m.QuarterScores.Count);
        Check(caseId, "QuarterScores[0].IsSpecified", "True", m.QuarterScores[0].IsSpecified);
        Check(caseId, "QuarterScores[0].QuarterIndex", "0", m.QuarterScores[0].QuarterIndex);
        Check(caseId, "QuarterScores[0].HomeScore", "7", m.QuarterScores[0].HomeScore);
        Check(caseId, "QuarterScores[0].AwayScore", "3", m.QuarterScores[0].AwayScore);
        Check(caseId, "QuarterScores[1].IsSpecified", "True", m.QuarterScores[1].IsSpecified);
        Check(caseId, "QuarterScores[1].QuarterIndex", "3", m.QuarterScores[1].QuarterIndex);
        Check(caseId, "QuarterScores[1].HomeScore", "11", m.QuarterScores[1].HomeScore);
        Check(caseId, "QuarterScores[1].AwayScore", "13", m.QuarterScores[1].AwayScore);
        Check(caseId, "Stations.Count", "2", m.Stations.Count);
        Check(caseId, "Stations[0].IsSpecified", "True", m.Stations[0].IsSpecified);
        Check(caseId, "Stations[0].StationId", "0", m.Stations[0].StationId);
        Check(caseId, "Stations[0].ControlledPlayerId", "2", m.Stations[0].ControlledPlayerId);
        Check(caseId, "Stations[0].Turbo01", "0.25", m.Stations[0].Turbo01);
        Check(caseId, "Stations[1].IsSpecified", "True", m.Stations[1].IsSpecified);
        Check(caseId, "Stations[1].StationId", "3", m.Stations[1].StationId);
        Check(caseId, "Stations[1].ControlledPlayerId", "9", m.Stations[1].ControlledPlayerId);
        Check(caseId, "Stations[1].Turbo01", "0.75", m.Stations[1].Turbo01);
        Check(caseId, "Audibles.Reference", "True", ReferenceEquals(f.Audibles, m.Audibles));
        Check(caseId, "TimeOfPossession.Reference", "True", ReferenceEquals(f.Time, m.TimeOfPossession));
        Check(caseId, "TimeOfPossession.HomeSeconds", "17", m.TimeOfPossession.HomeSeconds);
        Check(caseId, "TimeOfPossession.AwaySeconds", "29", m.TimeOfPossession.AwaySeconds);
    }

    private static void VerifyDefaults(string caseId, SourceMatchView m)
    {
        Check(caseId, "PlayKind", "Normal", m.PlayKind);
        Check(caseId, "Phase", "PreSnap", m.Phase);
        Check(caseId, "Period.QuarterIndex", "0", m.Period.QuarterIndex);
        Check(caseId, "Possession.Offense", "Home", m.Possession.Offense);
        Check(caseId, "Result.IsSpecified", "False", m.Result.IsSpecified);
        Check(caseId, "HomePlay.IsSpecified", "False", m.HomePlay.IsSpecified);
        Check(caseId, "AwayPlay.IsSpecified", "False", m.AwayPlay.IsSpecified);
        Check(caseId, "SelectionCountdown", "-1", m.SelectionCountdown);
        Check(caseId, "SelectionTimer", "<null>", m.SelectionTimer);
        Check(caseId, "HomeRandomSelection.IsSpecified", "False", m.HomeRandomSelection.IsSpecified);
        Check(caseId, "AwayRandomSelection.IsSpecified", "False", m.AwayRandomSelection.IsSpecified);
        Check(caseId, "PatChoice", "Unselected", m.PatChoice);
        Check(caseId, "PatStatus", "NotExtraPoint", m.PatStatus);
        Check(caseId, "Kickoff", "<null>", m.Kickoff);
        Check(caseId, "KickingTeam", "<null>", m.KickingTeam);
        Check(caseId, "ReceivingTeam", "<null>", m.ReceivingTeam);
        Check(caseId, "OffenseFlipped", "<null>", m.OffenseFlipped);
        Check(caseId, "HomeGameplay.IsSpecified", "False", m.HomeGameplay.IsSpecified);
        Check(caseId, "AwayGameplay.IsSpecified", "False", m.AwayGameplay.IsSpecified);
        Check(caseId, "ForwardCatcherPlayerId", "-1", m.ForwardCatcherPlayerId);
        Check(caseId, "DronesLose", "False", m.DronesLose);
        Check(caseId, "DronesWinning", "False", m.DronesWinning);
        Check(caseId, "HandicapRepetitionBonus", "0", m.HandicapRepetitionBonus);
        Check(caseId, "Compatibility", "StrictSource", m.Compatibility);
        Check(caseId, "CompatibilityRepairCount", "0", m.CompatibilityRepairCount);
        Check(caseId, "CompatibilityRepairs.Count", "0", m.CompatibilityRepairs.Count);
        Check(caseId, "QuarterScores.Count", "0", m.QuarterScores.Count);
        Check(caseId, "Stations.Count", "0", m.Stations.Count);
        Check(caseId, "Audibles", "<null>", m.Audibles);
    }

    private static Exception Catch(Action action)
    {
        try { action(); return null; }
        catch (Exception error) { return error; }
    }

    private static string Parameter(Exception error)
    {
        var argument = error as ArgumentException;
        return argument == null ? "<none>" : argument.ParamName;
    }

    private static void Run()
    {
        var zero = new SourceInterceptionsView(0, 0);
        Check("dto/observed-zero", "Observed", "True", zero != null);
        Check("dto/observed-zero", "HomeInterceptionsMade", "0", zero.HomeInterceptionsMade);
        Check("dto/observed-zero", "AwayInterceptionsMade", "0", zero.AwayInterceptionsMade);
        Check("dto/observed-zero", "Type.IsSealed", "True", typeof(SourceInterceptionsView).IsSealed);
        Check("dto/observed-zero", "HomeInterceptionsMade.CanWrite", "False",
            typeof(SourceInterceptionsView).GetProperty("HomeInterceptionsMade").CanWrite);
        Check("dto/observed-zero", "AwayInterceptionsMade.CanWrite", "False",
            typeof(SourceInterceptionsView).GetProperty("AwayInterceptionsMade").CanWrite);
        Check("dto/observed-zero", "Match.Interceptions.CanWrite", "False",
            typeof(SourceMatchView).GetProperty("Interceptions").CanWrite);

        var split = new SourceInterceptionsView(2, 5);
        Check("dto/team-separation", "HomeInterceptionsMade", "2", split.HomeInterceptionsMade);
        Check("dto/team-separation", "AwayInterceptionsMade", "5", split.AwayInterceptionsMade);

        var s0 = new SourceInterceptionsView(0, 0);
        var s1 = new SourceInterceptionsView(1, 0);
        var s2 = new SourceInterceptionsView(2, 0);
        var s3 = new SourceInterceptionsView(2, 1);
        Check("dto/cumulative-old-view", "S0.HomeInterceptionsMade", "0", s0.HomeInterceptionsMade);
        Check("dto/cumulative-old-view", "S0.AwayInterceptionsMade", "0", s0.AwayInterceptionsMade);
        Check("dto/cumulative-old-view", "S1.HomeInterceptionsMade", "1", s1.HomeInterceptionsMade);
        Check("dto/cumulative-old-view", "S1.AwayInterceptionsMade", "0", s1.AwayInterceptionsMade);
        Check("dto/cumulative-old-view", "S2.HomeInterceptionsMade", "2", s2.HomeInterceptionsMade);
        Check("dto/cumulative-old-view", "S2.AwayInterceptionsMade", "0", s2.AwayInterceptionsMade);
        Check("dto/cumulative-old-view", "S3.HomeInterceptionsMade", "2", s3.HomeInterceptionsMade);
        Check("dto/cumulative-old-view", "S3.AwayInterceptionsMade", "1", s3.AwayInterceptionsMade);
        Check("dto/cumulative-old-view", "RetainedFirstPositive.HomeInterceptionsMade", "1", s1.HomeInterceptionsMade);
        Check("dto/cumulative-old-view", "RetainedFirstPositive.AwayInterceptionsMade", "0", s1.AwayInterceptionsMade);
        Check("dto/cumulative-old-view", "DistinctViews", "True", !ReferenceEquals(s1, s3));

        var f = new Inputs();
        var counts = new SourceInterceptionsView(3, 1);
        var live = NewMatch(f, counts, SourceMatchPhase.LiveBall, f.Time);
        Check("match/new-overload-live", "Interceptions", "True", ReferenceEquals(counts, live.Interceptions));
        Check("match/new-overload-live", "HomeInterceptionsMade", "3", live.Interceptions.HomeInterceptionsMade);
        Check("match/new-overload-live", "AwayInterceptionsMade", "1", live.Interceptions.AwayInterceptionsMade);
        Check("match/new-overload-live", "Phase", "LiveBall", live.Phase);
        VerifyForwarded("match/new-overload-live", live, f);

        var terminal = NewMatch(f, counts, SourceMatchPhase.MatchEnded, f.Time);
        Check("match/new-overload-terminal", "Interceptions", "True", ReferenceEquals(counts, terminal.Interceptions));
        Check("match/new-overload-terminal", "HomeInterceptionsMade", "3", terminal.Interceptions.HomeInterceptionsMade);
        Check("match/new-overload-terminal", "AwayInterceptionsMade", "1", terminal.Interceptions.AwayInterceptionsMade);
        Check("match/new-overload-terminal", "Phase", "MatchEnded", terminal.Phase);
        VerifyForwarded("match/new-overload-terminal", terminal, f);

        var oldLive = OldTimeMatch(f, SourceMatchPhase.LiveBall, f.Time);
        Check("match/reference-live", "Interceptions", "<null>", oldLive.Interceptions);
        Check("match/reference-live", "Phase", "LiveBall", oldLive.Phase);
        VerifyForwarded("match/reference-live", oldLive, f);
        var oldTerminal = OldTimeMatch(f, SourceMatchPhase.MatchEnded, f.Time);
        Check("match/reference-terminal", "Interceptions", "<null>", oldTerminal.Interceptions);
        Check("match/reference-terminal", "Phase", "MatchEnded", oldTerminal.Phase);
        VerifyForwarded("match/reference-terminal", oldTerminal, f);

        Exception noCounts = Catch(() => NewMatch(f, null, SourceMatchPhase.LiveBall, f.Time));
        Check("match/null-interceptions", "Exception.Type", "ArgumentNullException", noCounts == null ? "<none>" : noCounts.GetType().Name);
        Check("match/null-interceptions", "Exception.ParamName", "interceptions", Parameter(noCounts));
        Exception noTime = Catch(() => NewMatch(f, counts, SourceMatchPhase.LiveBall, null));
        Check("match/new-null-time", "Exception.Type", "ArgumentNullException", noTime == null ? "<none>" : noTime.GetType().Name);
        Check("match/new-null-time", "Exception.ParamName", "timeOfPossession", Parameter(noTime));

        var initialPeriod = new SourcePeriodView(0);
        var initialPossession = new SourcePossessionView(
            TeamId.Home, TeamId.Home, TeamId.Home, TeamId.Home, SourceCaptainSlot.First);
        var oldBase = new SourceMatchView(f.Configuration, SourcePlayKind.Normal,
            SourceMatchPhase.PreSnap, initialPeriod, initialPossession,
            Array.Empty<SourceQuarterScore>(), Array.Empty<SourceStationView>());
        Check("match/old-base-unobserved", "Interceptions", "<null>", oldBase.Interceptions);
        Check("match/old-base-unobserved", "TimeOfPossession", "<null>", oldBase.TimeOfPossession);
        VerifyDefaults("match/old-base-unobserved", oldBase);

        var oldTime = new SourceMatchView(f.Time, f.Configuration, SourcePlayKind.Normal,
            SourceMatchPhase.PreSnap, initialPeriod, initialPossession,
            Array.Empty<SourceQuarterScore>(), Array.Empty<SourceStationView>());
        Check("match/old-time-unobserved", "Interceptions", "<null>", oldTime.Interceptions);
        Check("match/old-time-unobserved", "TimeOfPossession.Reference", "True", ReferenceEquals(f.Time, oldTime.TimeOfPossession));
        Check("match/old-time-unobserved", "TimeOfPossession.HomeSeconds", "17", oldTime.TimeOfPossession.HomeSeconds);
        Check("match/old-time-unobserved", "TimeOfPossession.AwaySeconds", "29", oldTime.TimeOfPossession.AwaySeconds);
        VerifyDefaults("match/old-time-unobserved", oldTime);
        Exception oldNoTime = Catch(() => OldTimeMatch(f, SourceMatchPhase.LiveBall, null));
        Check("match/old-time-unobserved", "NullTime.Exception.Type", "ArgumentNullException", oldNoTime == null ? "<none>" : oldNoTime.GetType().Name);
        Check("match/old-time-unobserved", "NullTime.Exception.ParamName", "timeOfPossession", Parameter(oldNoTime));

        var newDefault = new SourceMatchView(zero, f.Time, f.Configuration, SourcePlayKind.Normal,
            SourceMatchPhase.PreSnap, initialPeriod, initialPossession,
            Array.Empty<SourceQuarterScore>(), Array.Empty<SourceStationView>());
        Check("match/new-defaults", "Interceptions", "True", ReferenceEquals(zero, newDefault.Interceptions));
        Check("match/new-defaults", "HomeInterceptionsMade", "0", newDefault.Interceptions.HomeInterceptionsMade);
        Check("match/new-defaults", "AwayInterceptionsMade", "0", newDefault.Interceptions.AwayInterceptionsMade);
        Check("match/new-defaults", "TimeOfPossession.Reference", "True", ReferenceEquals(f.Time, newDefault.TimeOfPossession));
        VerifyDefaults("match/new-defaults", newDefault);

        f.Scores[0] = new SourceQuarterScore(0, 99, 98);
        f.Stations[0] = new SourceStationView(0, 3, 1f);
        f.Repairs[0] = new SourceCompatibilityRepair(99, 101,
            SourceCompatibilityRepairKind.CarrierlessDefensiveSwitch, 1, 3, 2, "later-component-literal");
        Check("match/retained-forwarded-values", "New.QuarterScores[0].HomeScore", "7", live.QuarterScores[0].HomeScore);
        Check("match/retained-forwarded-values", "Old.QuarterScores[0].HomeScore", "7", oldLive.QuarterScores[0].HomeScore);
        Check("match/retained-forwarded-values", "New.Stations[0].ControlledPlayerId", "2", live.Stations[0].ControlledPlayerId);
        Check("match/retained-forwarded-values", "Old.Stations[0].ControlledPlayerId", "2", oldLive.Stations[0].ControlledPlayerId);
        Check("match/retained-forwarded-values", "New.CompatibilityRepairs[0].Sequence", "13", live.CompatibilityRepairs[0].Sequence);
        Check("match/retained-forwarded-values", "Old.CompatibilityRepairs[0].Sequence", "13", oldLive.CompatibilityRepairs[0].Sequence);
        Check("match/retained-forwarded-values", "Live.HomeInterceptionsMade", "3", live.Interceptions.HomeInterceptionsMade);
        Check("match/retained-forwarded-values", "Terminal.AwayInterceptionsMade", "1", terminal.Interceptions.AwayInterceptionsMade);
    }

    private static int Main()
    {
        try
        {
            Run();
            Console.WriteLine(JsonSerializer.Serialize(new {
                kind = "summary", status = "pass", cases = Cases.Count, assertions,
                gameSimulationObjects = 0, gameSimulationSteps = 0
            }));
            return 0;
        }
        catch (AssertionFailure error)
        {
            Console.WriteLine(JsonSerializer.Serialize(new {
                kind = "summary", status = "assertion-failure", cases = Cases.Count, assertions,
                message = error.Message, gameSimulationObjects = 0, gameSimulationSteps = 0
            }));
            return 1;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(JsonSerializer.Serialize(new {
                kind = "unexpected-failure", type = error.GetType().Name, message = error.Message
            }));
            return 2;
        }
    }
}
