using System;
using System.Collections.Generic;
using System.Text.Json;
using Blitz.Contracts;
using Blitz.Gameplay;

internal static class SourceTimeOfPossessionChecks
{
    private static readonly string[] ClockFields =
    {
        "operation", "packed", "local", "playTime", "nextWake", "complete",
        "delta", "sleep", "warning", "readAdjustment", "badAdjustment", "periodExpired"
    };

    private static string activeField;
    private static int clockCases;
    private static int sequenceCheckpoints;
    private static int clockAssertions;
    private static int sequenceAssertions;
    private static int guardAssertions;

    private sealed class ClockCase
    {
        internal readonly string Id;
        internal readonly SourceClockState State;
        internal readonly SourceClockInput Input;
        internal readonly object[] Expected;
        internal readonly int[] HomeOwnerTotals;
        internal readonly int[] AwayOwnerTotals;

        internal ClockCase(string id, int packed, int local, int playTime, long nextWake, bool complete,
            SourceClockInput input, SourceClockOperation operation, int expectedPacked, int expectedLocal,
            int expectedPlayTime, long expectedNextWake, bool expectedComplete, int delta, int sleep,
            bool warning, bool readAdjustment, bool badAdjustment, bool periodExpired,
            int[] homeOwnerTotals, int[] awayOwnerTotals)
        {
            Id = id;
            State = new SourceClockState(packed, local, playTime, nextWake, complete);
            Input = input;
            Expected = new object[] { operation, expectedPacked, expectedLocal, expectedPlayTime,
                expectedNextWake, expectedComplete, delta, sleep, warning, readAdjustment,
                badAdjustment, periodExpired };
            HomeOwnerTotals = homeOwnerTotals;
            AwayOwnerTotals = awayOwnerTotals;
        }
    }

    private sealed class SequenceState
    {
        internal readonly SourceClockState Clock;
        internal readonly SourceTimeOfPossessionView Ledger;

        internal SequenceState(SourceClockState clock, SourceTimeOfPossessionView ledger)
        {
            Clock = clock;
            Ledger = ledger;
        }
    }

    private static void Equal<T>(T expected, T actual, string field)
    {
        activeField = field;
        Checks.Equal(expected, actual, field);
    }

    private static SourceClockInput Input(long tick = 0, bool active = true, int quarterIndex = 0,
        int adjustment = 2, bool readSucceeded = true, bool attract = false)
    {
        return new SourceClockInput(tick, active, quarterIndex, adjustment, readSucceeded, attract);
    }

    private static void RunClockCases()
    {
        // These are literal Lead-contract expectations, not values computed by either production helper.
        var rows = new[]
        {
            new ClockCase("minute", 0x020000, 0x020000, 0, 0, false, Input(),
                SourceClockOperation.Minute, 0x013b18, 0x013b18, 1, 1, false, 1, 1,
                false, false, false, false, new[] { 12, 23 }, new[] { 11, 24 }),
            new ClockCase("second-normal", 0x002000, 0x002000, 0, 0, false, Input(),
                SourceClockOperation.Second, 0x001f14, 0x001f14, 1, 1, false, 1, 1,
                false, true, false, false, new[] { 12, 23 }, new[] { 11, 24 }),
            new ClockCase("low-q1-odd", 0x001f00, 0x001f00, 0, 0, false, Input(quarterIndex: 1),
                SourceClockOperation.Second, 0x001e22, 0x001e22, 1, 1, false, 1, 1,
                true, false, false, false, new[] { 12, 23 }, new[] { 11, 24 }),
            new ClockCase("low-q3-even", 0x001e00, 0x001e00, 0, 0, false, Input(quarterIndex: 3),
                SourceClockOperation.Second, 0x001d22, 0x001d22, 1, 1, false, 1, 1,
                false, false, false, false, new[] { 12, 23 }, new[] { 11, 24 }),
            new ClockCase("long-play-18", 0x002800, 0x002800, 17, 0, false, Input(),
                SourceClockOperation.Second, 0x002730, 0x002730, 18, 1, false, 1, 1,
                false, false, false, false, new[] { 12, 23 }, new[] { 11, 24 }),
            new ClockCase("last-second", 0x000100, 0x000100, 0, 0, false, Input(),
                SourceClockOperation.Second, 0x000014, 0x000014, 1, 1, false, 1, 1,
                false, true, false, false, new[] { 12, 23 }, new[] { 11, 24 }),
            new ClockCase("subtick", 0x013b18, 0x013b18, 3, 0, false, Input(),
                SourceClockOperation.Subtick, 0x013b17, 0x013b17, 3, 1, false, 0, 1,
                false, false, false, false, new[] { 11, 23 }, new[] { 11, 23 }),
            new ClockCase("final-fraction", 0x000001, 0x000001, 0, 0, false, Input(),
                SourceClockOperation.Subtick, 0x000000, 0x000000, 0, 1, false, 0, 1,
                false, false, false, true, new[] { 11, 23 }, new[] { 11, 23 }),
            new ClockCase("paused", 0x002000, 0x002000, 0, 0, false, Input(active: false),
                SourceClockOperation.Paused, 0x002000, 0x002000, 0, 1, false, 0, 1,
                false, false, false, false, new[] { 11, 23 }, new[] { 11, 23 }),
            new ClockCase("attract", 0x002000, 0x002000, 0, 0, false, Input(attract: true),
                SourceClockOperation.Paused, 0x002000, 0x002000, 0, 1, false, 0, 1,
                false, false, false, false, new[] { 11, 23 }, new[] { 11, 23 }),
            new ClockCase("not-due", 0x002000, 0x002000, 0, 5, false, Input(tick: 4),
                SourceClockOperation.NotScheduled, 0x002000, 0x002000, 0, 5, false, 0, 0,
                false, false, false, false, new[] { 11, 23 }, new[] { 11, 23 }),
            new ClockCase("exit", 0x000000, 0x000000, 0, 0, false, Input(),
                SourceClockOperation.ProcessExit, 0x000000, 0x000000, 0, 0, true, 0, 0,
                false, false, false, false, new[] { 11, 23 }, new[] { 11, 23 }),
            new ClockCase("already-complete", 0x000000, 0x000000, 9, 7, true, Input(tick: 7),
                SourceClockOperation.NotScheduled, 0x000000, 0x000000, 9, 7, true, 0, 0,
                false, false, false, false, new[] { 11, 23 }, new[] { 11, 23 }),
            new ClockCase("bad-adjustment", 0x001f00, 0x001f00, 0, 0, false, Input(adjustment: -1),
                SourceClockOperation.Second, 0x001e12, 0x001e12, 1, 1, false, 1, 1,
                false, true, true, false, new[] { 12, 23 }, new[] { 11, 24 }),
            new ClockCase("read-failure", 0x001f00, 0x001f00, 0, 0, false, Input(readSucceeded: false),
                SourceClockOperation.Second, 0x001e12, 0x001e12, 1, 1, false, 1, 1,
                false, true, true, false, new[] { 12, 23 }, new[] { 11, 24 }),
            new ClockCase("discard-republish", 0x013b00, 0x013b18, 4, 1, false, Input(tick: 1),
                SourceClockOperation.Subtick, 0x013b17, 0x013b17, 4, 2, false, 0, 1,
                false, false, false, false, new[] { 11, 23 }, new[] { 11, 23 })
        };

        foreach (ClockCase row in rows)
        {
            foreach (TeamId offense in new[] { TeamId.Home, TeamId.Away })
            {
                var previous = new SourceTimeOfPossessionView(11, 23);
                SourceClockStep clock = SourceClock.Step(row.State, row.Input);
                object[] observed =
                {
                    clock.Operation, clock.State.PackedTime, clock.State.LocalPackedTime,
                    clock.State.PlayTime, clock.State.NextWakeTick, clock.State.ProcessComplete,
                    clock.TimeOfPossessionDelta, clock.SleepTicks, clock.LowClockWarning,
                    clock.ReadTimeAdjustment, clock.UsedBadAdjustmentFallback, clock.PeriodExpired
                };
                string prefix = row.Id + "/" + offense + "/";
                for (int field = 0; field < ClockFields.Length; field++)
                    Equal(row.Expected[field], observed[field], prefix + ClockFields[field]);
                SourceTimeOfPossessionView credited =
                    SourceTimeOfPossession.Accumulate(previous, offense, clock.TimeOfPossessionDelta);
                int[] totals = offense == TeamId.Home ? row.HomeOwnerTotals : row.AwayOwnerTotals;
                Equal(totals[0], credited.HomeSeconds, prefix + "HomeSeconds");
                Equal(totals[1], credited.AwaySeconds, prefix + "AwaySeconds");
                clockCases++;
            }
        }
    }

    private static SequenceState Fresh()
    {
        return new SequenceState(SourceClock.Create(), new SourceTimeOfPossessionView(0, 0));
    }

    private static SequenceState Advance(SequenceState previous, TeamId offense, long tick, bool active = true)
    {
        SourceClockStep clock = SourceClock.Step(previous.Clock, Input(tick, active));
        return new SequenceState(clock.State,
            SourceTimeOfPossession.Accumulate(previous.Ledger, offense, clock.TimeOfPossessionDelta));
    }

    private static void Checkpoint(string id, SequenceState state, int packed, int local,
        int playTime, long nextWake, int home, int away)
    {
        Equal(packed, state.Clock.PackedTime, id + "/packed");
        Equal(local, state.Clock.LocalPackedTime, id + "/local");
        Equal(playTime, state.Clock.PlayTime, id + "/playTime");
        Equal(nextWake, state.Clock.NextWakeTick, id + "/nextWake");
        Equal(home, state.Ledger.HomeSeconds, id + "/HomeSeconds");
        Equal(away, state.Ledger.AwaySeconds, id + "/AwaySeconds");
        sequenceCheckpoints++;
    }

    private static SourceTimeOfPossessionView RunSequence()
    {
        // Immutable synthetic API forks; no GameSimulation construction, scheduling or Restart is run.
        SequenceState s0 = Fresh();
        Checkpoint("S0", s0, 0x020000, 0x020000, 0, 0, 0, 0);
        SequenceState s1 = Advance(s0, TeamId.Home, 0);
        Checkpoint("S1", s1, 0x013b18, 0x013b18, 1, 1, 1, 0);
        SequenceState s2 = Advance(s1, TeamId.Home, 0);
        Checkpoint("S2", s2, 0x013b18, 0x013b18, 1, 1, 1, 0);
        SequenceState s3 = s1;
        for (long tick = 1; tick <= 24; tick++) s3 = Advance(s3, TeamId.Home, tick);
        Checkpoint("S3", s3, 0x013b00, 0x013b00, 1, 25, 1, 0);
        SequenceState s4 = Advance(s3, TeamId.Away, 25);
        Checkpoint("S4", s4, 0x013a14, 0x013a14, 2, 26, 1, 1);
        SequenceState s5 = Advance(s3, TeamId.Home, 25);
        TeamId nextOffense = TeamId.Away;
        Checkpoint("S5", s5, 0x013a14, 0x013a14, 2, 26, 2, 0);
        SequenceState s6 = s5;
        for (long tick = 26; tick <= 45; tick++) s6 = Advance(s6, nextOffense, tick);
        Checkpoint("S6", s6, 0x013a00, 0x013a00, 2, 46, 2, 0);
        SequenceState s7 = Advance(s6, nextOffense, 46);
        Checkpoint("S7", s7, 0x013914, 0x013914, 3, 47, 2, 1);
        var s8 = new SequenceState(SourceClock.DiscardFraction(SourceClock.ResetPlayTime(s1.Clock)), s1.Ledger);
        Checkpoint("S8", s8, 0x013b00, 0x013b18, 0, 1, 1, 0);
        SequenceState s9 = Advance(s8, TeamId.Away, 1, false);
        Checkpoint("S9", s9, 0x013b00, 0x013b18, 0, 2, 1, 0);
        SequenceState s10 = Advance(s9, TeamId.Away, 2);
        Checkpoint("S10", s10, 0x013b17, 0x013b17, 0, 3, 1, 0);
        SequenceState s11 = Advance(
            new SequenceState(SourceClock.Create(firstWakeTick: 100), s7.Ledger), TeamId.Away, 100);
        Checkpoint("S11", s11, 0x013b18, 0x013b18, 1, 101, 2, 2);
        SequenceState s12 = Advance(s11, TeamId.Away, 101, false);
        Checkpoint("S12", s12, 0x013b18, 0x013b18, 1, 102, 2, 2);
        SequenceState s13 = Fresh();
        Checkpoint("S13", s13, 0x020000, 0x020000, 0, 0, 0, 0);
        return s12.Ledger;
    }

    private static string ExceptionType(Action action)
    {
        try { action(); }
        catch (Exception error) { return error.GetType().Name; }
        return "none";
    }

    private static SourceMatchConfiguration Configuration()
    {
        return new SourceMatchConfiguration(SourceSimulationProfile.OriginalRelease57Hz,
            SourceStationLayout.FourStation,
            new SourceTeamSelection(0, new[] { 0, 1, 2, 3, 4, 5, 6 }),
            new SourceTeamSelection(1, new[] { 0, 1, 2, 3, 4, 5, 6 }),
            new SourceStationConfiguration[0], TeamId.Home, SourceCaptainSlot.First);
    }

    private static SourcePossessionView Possession()
    {
        return new SourcePossessionView(TeamId.Home, TeamId.Home, TeamId.Away, TeamId.Home,
            SourceCaptainSlot.First);
    }

    private static SourceMatchView Match(SourceMatchConfiguration configuration, SourceTimeOfPossessionView ledger)
    {
        return new SourceMatchView(ledger, configuration, SourcePlayKind.Normal,
            SourceMatchPhase.PlaySelection, new SourcePeriodView(0), Possession(),
            new SourceQuarterScore[0], new SourceStationView[0]);
    }

    private static void RunGuards(SourceTimeOfPossessionView retainedS12)
    {
        var zero = new SourceTimeOfPossessionView(0, 0);
        var maxPair = new SourceTimeOfPossessionView(int.MaxValue, int.MaxValue);
        var homeOverflow = new SourceTimeOfPossessionView(int.MaxValue, 0);
        var awayOverflow = new SourceTimeOfPossessionView(0, int.MaxValue);
        SourceMatchConfiguration configuration = Configuration();
        Equal(0, zero.HomeSeconds, "G01");
        Equal(0, zero.AwaySeconds, "G02");
        Equal("ArgumentOutOfRangeException", ExceptionType(() => new SourceTimeOfPossessionView(-1, 0)), "G03");
        Equal("ArgumentOutOfRangeException", ExceptionType(() => new SourceTimeOfPossessionView(0, -1)), "G04");
        Equal("ArgumentNullException",
            ExceptionType(() => SourceTimeOfPossession.Accumulate(null, TeamId.Home, 1)), "G05");
        Equal("ArgumentOutOfRangeException",
            ExceptionType(() => SourceTimeOfPossession.Accumulate(zero, (TeamId)(-1), 0)), "G06");
        Equal("ArgumentOutOfRangeException",
            ExceptionType(() => SourceTimeOfPossession.Accumulate(zero, (TeamId)2, 0)), "G07");
        Equal("ArgumentOutOfRangeException",
            ExceptionType(() => SourceTimeOfPossession.Accumulate(zero, TeamId.Home, -1)), "G08");
        Equal("ArgumentOutOfRangeException",
            ExceptionType(() => SourceTimeOfPossession.Accumulate(zero, TeamId.Away, 2)), "G09");
        Equal("OverflowException",
            ExceptionType(() => SourceTimeOfPossession.Accumulate(homeOverflow, TeamId.Home, 1)), "G10");
        Equal("OverflowException",
            ExceptionType(() => SourceTimeOfPossession.Accumulate(awayOverflow, TeamId.Away, 1)), "G11");
        Equal(true, ReferenceEquals(maxPair, SourceTimeOfPossession.Accumulate(maxPair, TeamId.Home, 0)), "G12");
        var legacy = new SourceMatchView(configuration, SourcePlayKind.Normal, SourceMatchPhase.PlaySelection,
            new SourcePeriodView(0), Possession(), new SourceQuarterScore[0], new SourceStationView[0]);
        Equal<SourceTimeOfPossessionView>(null, legacy.TimeOfPossession, "G13");
        Equal("ArgumentNullException", ExceptionType(() => Match(configuration, null)), "G14");
        var specified = new SourceTimeOfPossessionView(7, 11);
        SourceMatchView match = Match(configuration, specified);
        Equal(true, ReferenceEquals(specified, match.TimeOfPossession), "G15");
        Equal(7, match.TimeOfPossession.HomeSeconds, "G16");
        Equal(11, match.TimeOfPossession.AwaySeconds, "G17");
        SourceTimeOfPossessionView credited = SourceTimeOfPossession.Accumulate(specified, TeamId.Away, 1);
        Equal(7, match.TimeOfPossession.HomeSeconds, "G18");
        Equal(11, match.TimeOfPossession.AwaySeconds, "G19");
        Equal(7, credited.HomeSeconds, "G20");
        Equal(12, credited.AwaySeconds, "G21");
        Equal(2, retainedS12.HomeSeconds, "G22");
        Equal(2, retainedS12.AwaySeconds, "G23");
        Equal(int.MaxValue, homeOverflow.HomeSeconds, "G24");
        Equal(0, homeOverflow.AwaySeconds, "G25");
        Equal(0, awayOverflow.HomeSeconds, "G26");
        Equal(int.MaxValue, awayOverflow.AwaySeconds, "G27");
        Equal("ArgumentNullException",
            ExceptionType(() => SourceTimeOfPossession.Accumulate(null, TeamId.Home, 0)), "G28");
        SourceTimeOfPossessionView unselectedMaximum =
            SourceTimeOfPossession.Accumulate(awayOverflow, TeamId.Home, 1);
        Equal(1, unselectedMaximum.HomeSeconds, "G29");
        Equal(int.MaxValue, unselectedMaximum.AwaySeconds, "G30");
        Equal("ArgumentOutOfRangeException",
            ExceptionType(() => SourceClock.Step(SourceClock.Create(), Input(-1))), "G31");
    }

    private static int Main()
    {
        var result = new Dictionary<string, object>
        {
            { "schema", "source-possession-time-checks/v1" },
            { "component", "source-statistics-clock-consumer-time-of-possession/v1" },
            { "status", "error" },
            { "actual_data_assertions", 0 },
            { "game_simulation_executed", false },
            { "full_statistics_claimed", false },
            { "whole_game_fidelity_claimed", false }
        };
        int exit = 1;
        try
        {
            int start = Checks.Count;
            try { RunClockCases(); }
            finally { clockAssertions = Checks.Count - start; }
            start = Checks.Count;
            SourceTimeOfPossessionView retained;
            try { retained = RunSequence(); }
            finally { sequenceAssertions = Checks.Count - start; }
            start = Checks.Count;
            try { RunGuards(retained); }
            finally { guardAssertions = Checks.Count - start; }
            if (clockCases != 32 || clockAssertions != 448 || sequenceCheckpoints != 14
                || sequenceAssertions != 84 || guardAssertions != 31 || Checks.Count != 563)
                throw new InvalidOperationException("Expected exactly 448 + 84 + 31 = 563 possession-time assertions.");
            result["status"] = "passed";
            exit = 0;
        }
        catch (AssertionFailure failure)
        {
            result["status"] = "failed";
            result["failed_field"] = activeField;
            result["failed_assertion"] = failure.Message;
        }
        catch (Exception error)
        {
            result["exception_type"] = error.GetType().FullName;
            result["error"] = error.Message;
        }
        result["clock_cases"] = clockCases;
        result["clock_assertions"] = clockAssertions;
        result["sequence_checkpoints"] = sequenceCheckpoints;
        result["sequence_assertions"] = sequenceAssertions;
        result["guard_assertions"] = guardAssertions;
        result["assertions"] = Checks.Count;
        Console.WriteLine(JsonSerializer.Serialize(result));
        return exit;
    }
}
