using System;
using System.Collections.Generic;
using System.Text.Json;
using Blitz.Contracts;
using Blitz.Gameplay;

internal static class CompletedPassStatsChecks
{
    private sealed class NamedAssertion : Exception
    {
        internal readonly string Field;
        internal NamedAssertion(string field) { Field = field; }
    }

    private sealed class Scenario
    {
        internal readonly string Name;
        internal readonly int[] Home, Away;
        internal readonly SourcePlayResult Result;
        internal readonly int End, Raw, NextLos, Status, HomeX, AwayX, HomeClassX, AwayClassX;
        internal readonly bool OwnerFlips, NextFlips, Changed;
        internal Scenario(string name, int[] home, int[] away, SourcePlayResult result = SourcePlayResult.Down,
            int end = 50, int raw = 50, int nextLos = 50, bool ownerFlips = false,
            bool nextFlips = false, bool changed = false, int status = 0,
            int homeX = 0, int awayX = 0, int homeClassX = 0, int awayClassX = 0)
        {
            Name = name; Home = home; Away = away; Result = result; End = end; Raw = raw;
            NextLos = nextLos; OwnerFlips = ownerFlips; NextFlips = nextFlips; Changed = changed;
            Status = status; HomeX = homeX; AwayX = awayX; HomeClassX = homeClassX; AwayClassX = awayClassX;
        }
    }

    private static readonly Scenario[] Scenarios =
    {
        new Scenario("pass.gain", new[] {27,32,3,1,-3,8,5,2}, new[] {7,12,2,1,17,28,6,2}),
        new Scenario("pass.loss", new[] {-3,2,3,1,-3,8,5,2}, new[] {7,12,2,1,-13,-2,6,2}),
        new Scenario("pass.zero", new[] {7,12,3,1,-3,8,5,2}, new[] {7,12,2,1,-3,8,6,2}),
        new Scenario("pass.fourth-down", new[] {-3,2,3,1,-3,8,5,2}, new[] {7,12,2,1,-13,-2,6,2},
            nextFlips: true),
        new Scenario("pass.touchdown", new[] {77,82,3,2,-3,8,5,2}, new[] {7,12,2,1,67,78,6,3},
            SourcePlayResult.Touchdown, 100, nextLos: 90),
        new Scenario("pass.conversion", new[] {17,22,3,1,-3,8,5,2}, new[] {7,12,2,1,7,18,6,2},
            SourcePlayResult.Conversion, 100, nextLos: 90),
        new Scenario("pass.failed-conversion", new[] {-33,-28,3,1,-3,8,5,2}, new[] {7,12,2,1,-43,-32,6,2},
            SourcePlayResult.FailedConversion, nextLos: 90),
        new Scenario("pass.rounding-to-midfield", new[] {27,32,3,1,-3,8,5,2}, new[] {7,12,2,1,17,28,6,2}),
        new Scenario("pass.lower-bound", new[] {-22,-17,3,1,-3,8,5,2}, new[] {7,12,2,1,-32,-21,6,2},
            end: 1, raw: -10, nextLos: 1, homeX: -468, awayX: 468, homeClassX: -468, awayClassX: 468),
        new Scenario("pass.upper-bound", new[] {76,81,3,1,-3,8,5,2}, new[] {7,12,2,1,66,77,6,2},
            end: 99, raw: 110, nextLos: 99, homeX: 468, awayX: -468, homeClassX: 468, awayClassX: -468),
        new Scenario("pass.safety", new[] {-22,-17,3,1,-3,8,5,2}, new[] {7,12,2,1,-32,-21,6,2},
            SourcePlayResult.Safety, 1, -10, 30, homeX: -468, awayX: 468, homeClassX: -468, awayClassX: 468),
        new Scenario("pass.fumbled-oob-keeps-fpend", new[] {27,32,3,1,-3,8,5,2}, new[] {7,12,2,1,17,28,6,2},
            SourcePlayResult.FumbledOutOfBounds, homeClassX: -78, awayClassX: 78),
        new Scenario("pass.last-established-owner", new[] {27,32,3,1,-3,8,5,2}, new[] {7,12,2,1,17,28,6,2}),
        new Scenario("pass.intentional-noop-non-normal", new[] {27,32,3,1,-3,8,5,2}, new[] {7,12,2,1,17,28,6,2},
            status: 1),
        new Scenario("pass.quick-bit-not-veto", new[] {27,32,3,1,-3,8,5,2}, new[] {7,12,2,1,17,28,6,2}),
        new Scenario("pass.kick-bits-not-veto", new[] {27,32,3,1,-3,8,5,2}, new[] {7,12,2,1,17,28,6,2}),
        new Scenario("pass.quick-carrier-mismatch", new[] {77,82,3,2,-3,8,5,2}, new[] {7,12,2,1,67,78,6,3},
            SourcePlayResult.Touchdown, 100, nextLos: 90),
        new Scenario("guard.pass-thrown-only", new[] {7,12,2,1,-3,8,5,2}, new[] {7,12,2,1,-3,8,5,2}),
        new Scenario("guard.no-forward", new[] {7,12,2,1,-3,8,5,2}, new[] {7,12,2,1,-3,8,5,2}),
        new Scenario("guard.incomplete", new[] {7,12,2,1,-3,8,5,2}, new[] {7,12,2,1,-3,8,5,2},
            SourcePlayResult.Incomplete, nextLos: 30),
        new Scenario("guard.explicit-possession", new[] {7,12,2,1,-3,8,5,2}, new[] {7,12,2,1,-3,8,5,2},
            SourcePlayResult.NewPossession, changed: true),
        new Scenario("guard.derived-possession", new[] {7,12,2,1,-3,8,5,2}, new[] {7,12,2,1,-3,8,5,2},
            SourcePlayResult.NewPossession, ownerFlips: true, nextFlips: true, changed: true),
        new Scenario("guard.dead-ball", new[] {7,12,2,1,-3,8,5,2}, new[] {7,12,2,1,-3,8,5,2}, status: 1),
        new Scenario("guard.quick-new-possession", new[] {7,12,2,1,-3,8,5,2}, new[] {7,12,2,1,-3,8,5,2},
            SourcePlayResult.NewPossession),
        new Scenario("guard.quick-touchdown", new[] {7,12,2,1,-3,8,5,2}, new[] {7,12,2,1,-3,8,5,2},
            SourcePlayResult.Touchdown, 100, nextLos: 90),
        new Scenario("guard.classifier-side-flip", new[] {7,12,2,1,-3,8,5,2}, new[] {7,12,2,1,-3,8,5,2},
            SourcePlayResult.Touchback, 99, 110, 20, true, true,
            homeX: 468, awayX: -468, homeClassX: 468, awayClassX: -468)
    };

    private static readonly List<object> Fields = new List<object>();
    private static readonly List<string> Cases = new List<string>();
    private static readonly string[] TupleNames = {"passing-yards", "total-yards", "completions", "pass-touchdowns"};
    private static string currentCase, selectedCase;

    private static void Check(string suffix, int actual, int expected)
    {
        string field = currentCase + "." + suffix;
        Fields.Add(new { @case = currentCase, field, expected, actual });
        if (actual != expected) throw new NamedAssertion(field);
    }

    private static int Bit(bool value) { return value ? 1 : 0; }
    private static void Case(string name, Action action)
    {
        if (selectedCase != null && selectedCase != name) return;
        currentCase = name;
        action();
        Cases.Add(name);
    }

    private static SourcePlayEndInput Input(string name, int side)
    {
        int los = 30, down = 1, last = side, offense = side;
        SourcePlayType play = SourcePlayType.Normal;
        SourcePlayEndCause cause = SourcePlayEndCause.CarrierTackledInBounds;
        SourceBallType ball = SourceBallType.Pass;
        SourceGameFlags flags = SourceGameFlags.ForwardPass | SourceGameFlags.PassThrown;
        float x = 0f, fumbleX = 0f;
        switch (name)
        {
            case "pass.loss": los = 60; break;
            case "pass.zero": los = 50; break;
            case "pass.fourth-down": los = 60; down = 4; break;
            case "pass.touchdown": cause = SourcePlayEndCause.CarrierTouchdown; break;
            case "pass.conversion": play = SourcePlayType.ExtraPoint; los = 90;
                cause = SourcePlayEndCause.CarrierTouchdown; break;
            case "pass.failed-conversion": play = SourcePlayType.ExtraPoint; los = 90;
                cause = SourcePlayEndCause.CarrierOutOfBounds; break;
            case "pass.rounding-to-midfield": x = side == 0 ? -7.75f : 7.75f; break;
            case "pass.lower-bound": x = side == 0 ? -468f : 468f; break;
            case "pass.upper-bound": x = side == 0 ? 468f : -468f; break;
            case "pass.safety": cause = SourcePlayEndCause.CarrierDownInOwnEndZone;
                x = side == 0 ? -468f : 468f; break;
            case "pass.fumbled-oob-keeps-fpend": cause = SourcePlayEndCause.BallOutOfBounds;
                ball = SourceBallType.Fumble; fumbleX = side == 0 ? -78f : 78f; break;
            case "pass.last-established-owner": offense = 1 - side; break;
            case "pass.intentional-noop-non-normal": play = SourcePlayType.FieldGoal; break;
            case "pass.quick-bit-not-veto": flags |= SourceGameFlags.QuickKick; break;
            case "pass.kick-bits-not-veto": flags |= SourceGameFlags.BallKicked | SourceGameFlags.BallPunted; break;
            case "pass.quick-carrier-mismatch": play = SourcePlayType.Kickoff;
                cause = SourcePlayEndCause.CarrierTouchdown; flags |= SourceGameFlags.QuickKick; break;
            case "guard.pass-thrown-only": flags = SourceGameFlags.PassThrown; break;
            case "guard.no-forward": flags = SourceGameFlags.None; break;
            case "guard.incomplete": cause = SourcePlayEndCause.BallOutOfBounds; break;
            case "guard.explicit-possession": flags |= SourceGameFlags.PossessionChange; break;
            case "guard.derived-possession": last = 1 - side; break;
            case "guard.dead-ball": play = SourcePlayType.FieldGoal;
                cause = SourcePlayEndCause.DeadBallInBounds; break;
            case "guard.quick-new-possession": play = SourcePlayType.Kickoff;
                cause = SourcePlayEndCause.CarrierOutOfBounds; flags |= SourceGameFlags.QuickKick; break;
            case "guard.quick-touchdown": play = SourcePlayType.Kickoff;
                cause = SourcePlayEndCause.CarrierTouchdown; flags |= SourceGameFlags.QuickKick; break;
            case "guard.classifier-side-flip": cause = SourcePlayEndCause.BallOutOfBounds;
                ball = SourceBallType.Fumble; x = side == 0 ? 468f : -468f; break;
        }
        return new SourcePlayEndInput(new SourceRulesState(offense, side, down, los, 90, play),
            cause, ball, flags, last, x, fumbleX: fumbleX, previousResult: SourcePlayResult.Down);
    }

    private static int Carrier(string scenario, int side)
    {
        if (scenario == "pass.quick-carrier-mismatch") return side == 0 ? 11 : 4;
        return side == 0 ? 4 : 11;
    }

    private static void StateTuple(SimulationState model, string prefix, int[] expected)
    {
        for (int side = 0; side < 2; side++)
        {
            int[] actual = {model.SourcePassingYards[side], model.SourceTotalYards[side],
                model.SourcePassCompletions[side], model.SourcePassTouchdowns[side]};
            for (int field = 0; field < 4; field++)
                Check(prefix + (side == 0 ? ".home." : ".away.") + TupleNames[field],
                    actual[field], expected[side * 4 + field]);
        }
    }

    private static void SnapshotTuple(GameSnapshot snapshot, string prefix, int[] expected)
    {
        SourceCompletedPassStatsView group = snapshot.Match.CompletedPassStats;
        SourcePassCompletionsView completions = snapshot.Match.PassCompletions;
        int[] actual = {group.HomePassingYards, group.HomeTotalYards, completions.HomeCompletions,
            group.HomePassTouchdowns, group.AwayPassingYards, group.AwayTotalYards,
            completions.AwayCompletions, group.AwayPassTouchdowns};
        for (int side = 0; side < 2; side++)
            for (int field = 0; field < 4; field++)
                Check(prefix + (side == 0 ? ".home." : ".away.") + TupleNames[field],
                    actual[side * 4 + field], expected[side * 4 + field]);
    }

    private static void Table(Scenario scenario, int side)
    {
        var model = new SimulationState();
        model.SourcePassingYards[0] = 7; model.SourcePassingYards[1] = -3;
        model.SourceTotalYards[0] = 12; model.SourceTotalYards[1] = 8;
        model.SourcePassCompletions[0] = 2; model.SourcePassCompletions[1] = 5;
        model.SourcePassTouchdowns[0] = 1; model.SourcePassTouchdowns[1] = 2;
        model.SourceOffensivePlays[0] = 8; model.SourceOffensivePlays[1] = 13;
        model.SourceConsecutiveCompletions[0] = 1; model.SourceConsecutiveCompletions[1] = 4;
        model.SourceInterceptions[0] = 7; model.SourceInterceptions[1] = 9;
        model.SourceTimeOfPossession = new SourceTimeOfPossessionView(11, 17);
        model.SourceTeamFire[0] = true; model.SourceTeamFire[1] = false;
        model.SourceForwardCatcher = 4;
        SourcePlayEndInput input = Input(scenario.Name, side);
        SourcePlayResolution resolution = SourceMatchRules.Resolve(input);
        if (!resolution.TransitionApplied) throw new InvalidOperationException("Declared component input did not resolve.");
        model.AccumulateSourcePassCompletion(input, resolution, Carrier(scenario.Name, side));
        StateTuple(model, "state", side == 0 ? scenario.Home : scenario.Away);
        Check("classification.owner", resolution.Classification.OffenseSide, scenario.OwnerFlips ? 1 - side : side);
        Check("classification.result", (int)resolution.Classification.Result, (int)scenario.Result);
        Check("classification.status", (int)resolution.Classification.Status, scenario.Status);
        Check("resolved.possession-change", Bit((resolution.Flags & SourceGameFlags.PossessionChange) != 0), Bit(scenario.Changed));
        Check("raw-end", resolution.UnclampedFieldEnd, scenario.Raw);
        Check("field-end", resolution.FieldEnd, scenario.End);
        Check("next-owner", resolution.State.OffenseSide, scenario.NextFlips ? 1 - side : side);
        Check("next-los", resolution.State.LineOfScrimmage, scenario.NextLos);
        Check("rounded-x", (int)resolution.RoundedPlayEndX, side == 0 ? scenario.HomeX : scenario.AwayX);
        Check("classified-x", (int)resolution.Classification.PlayEndX, side == 0 ? scenario.HomeClassX : scenario.AwayClassX);
        Check("preserve.offensive.home", model.SourceOffensivePlays[0], 8);
        Check("preserve.offensive.away", model.SourceOffensivePlays[1], 13);
        Check("preserve.consecutive.home", model.SourceConsecutiveCompletions[0], 1);
        Check("preserve.consecutive.away", model.SourceConsecutiveCompletions[1], 4);
        Check("preserve.interceptions.home", model.SourceInterceptions[0], 7);
        Check("preserve.interceptions.away", model.SourceInterceptions[1], 9);
        Check("preserve.time.home", model.SourceTimeOfPossession.HomeSeconds, 11);
        Check("preserve.time.away", model.SourceTimeOfPossession.AwaySeconds, 17);
        Check("preserve.fire.home", Bit(model.SourceTeamFire[0]), 1);
        Check("preserve.fire.away", Bit(model.SourceTeamFire[1]), 0);
        Check("preserve.forward-catcher", model.SourceForwardCatcher, 4);
    }

    private static void Accumulate(SimulationState model, string name, int side)
    {
        SourcePlayEndInput input = Input(name, side);
        model.AccumulateSourcePassCompletion(input, SourceMatchRules.Resolve(input), Carrier(name, side));
    }

    private static void Lifecycle()
    {
        SimulationState model = Program.ComponentState();
        StateTuple(model, "fresh.state", new[] {0,0,0,0,0,0,0,0});
        GameSnapshot s0 = new GameSnapshot(model);
        SnapshotTuple(s0, "snapshot.s0", new[] {0,0,0,0,0,0,0,0});
        Accumulate(model, "pass.gain", 0);
        StateTuple(model, "first.state", new[] {20,20,1,0,0,0,0,0});
        GameSnapshot s1 = new GameSnapshot(model);
        SnapshotTuple(s1, "snapshot.s1", new[] {20,20,1,0,0,0,0,0});
        Accumulate(model, "pass.loss", 0);
        StateTuple(model, "second.state", new[] {10,10,2,0,0,0,0,0});
        SnapshotTuple(s1, "retained.first.s1", new[] {20,20,1,0,0,0,0,0});
        Accumulate(model, "pass.zero", 0);
        StateTuple(model, "third.state", new[] {10,10,3,0,0,0,0,0});
        Accumulate(model, "pass.touchdown", 1);
        StateTuple(model, "fourth.state", new[] {10,10,3,0,70,70,1,1});
        GameSnapshot s2 = new GameSnapshot(model);
        SnapshotTuple(s2, "snapshot.s2", new[] {10,10,3,0,70,70,1,1});
        Accumulate(model, "pass.conversion", 0);
        StateTuple(model, "fifth.state", new[] {20,20,4,0,70,70,1,1});
        Accumulate(model, "pass.lower-bound", 0);
        StateTuple(model, "sixth.state", new[] {-9,-9,5,0,70,70,1,1});
        SnapshotTuple(new GameSnapshot(model), "snapshot.final", new[] {-9,-9,5,0,70,70,1,1});
        SnapshotTuple(s0, "retained.final.s0", new[] {0,0,0,0,0,0,0,0});
        SnapshotTuple(s1, "retained.final.s1", new[] {20,20,1,0,0,0,0,0});
        SnapshotTuple(s2, "retained.final.s2", new[] {10,10,3,0,70,70,1,1});
        model.Phase = GamePhase.PlaySelect;
        SnapshotTuple(new GameSnapshot(model), "phase.select", new[] {-9,-9,5,0,70,70,1,1});
        model.Phase = GamePhase.DeadBall;
        SnapshotTuple(new GameSnapshot(model), "phase.dead", new[] {-9,-9,5,0,70,70,1,1});
        Accumulate(model, "guard.explicit-possession", 0);
        StateTuple(model, "guarded.state", new[] {-9,-9,5,0,70,70,1,1});
        var fresh = new SimulationState();
        StateTuple(fresh, "reset.fresh", new[] {0,0,0,0,0,0,0,0});
        StateTuple(model, "reset.old", new[] {-9,-9,5,0,70,70,1,1});
        SnapshotTuple(s1, "reset.retained.s1", new[] {20,20,1,0,0,0,0,0});
        Check("reset.arrays.passing-distinct", Bit(!ReferenceEquals(fresh.SourcePassingYards, model.SourcePassingYards)), 1);
        Check("reset.arrays.total-distinct", Bit(!ReferenceEquals(fresh.SourceTotalYards, model.SourceTotalYards)), 1);
        Check("reset.arrays.completions-distinct", Bit(!ReferenceEquals(fresh.SourcePassCompletions, model.SourcePassCompletions)), 1);
        Check("reset.arrays.td-distinct", Bit(!ReferenceEquals(fresh.SourcePassTouchdowns, model.SourcePassTouchdowns)), 1);
    }

    private static void Api()
    {
        var group = new SourceCompletedPassStatsView(-7, 9, -12, 6, 1, 2);
        Check("dto.home.passing", group.HomePassingYards, -7);
        Check("dto.away.passing", group.AwayPassingYards, 9);
        Check("dto.home.total", group.HomeTotalYards, -12);
        Check("dto.away.total", group.AwayTotalYards, 6);
        Check("dto.home.td", group.HomePassTouchdowns, 1);
        Check("dto.away.td", group.AwayPassTouchdowns, 2);
        var zeros = new SourceCompletedPassStatsView(-1, -2, -3, -4, 0, 0);
        Check("dto.zero.home", zeros.HomePassTouchdowns, 0);
        Check("dto.zero.away", zeros.AwayPassTouchdowns, 0);
        bool negativeHome = false, negativeAway = false, nullGroup = false, inheritedNull = false;
        try { new SourceCompletedPassStatsView(0, 0, 0, 0, -1, 0); }
        catch (ArgumentOutOfRangeException ex) { negativeHome = ex.ParamName == "homePassTouchdowns"; }
        try { new SourceCompletedPassStatsView(0, 0, 0, 0, 0, -1); }
        catch (ArgumentOutOfRangeException ex) { negativeAway = ex.ParamName == "awayPassTouchdowns"; }
        Check("dto.negative-home-rejected", Bit(negativeHome), 1);
        Check("dto.negative-away-rejected", Bit(negativeAway), 1);
        SourceMatchConfiguration configuration = Program.Configuration();
        var period = new SourcePeriodView(0);
        var possession = new SourcePossessionView(TeamId.Home, TeamId.Home, TeamId.Home,
            TeamId.Away, SourceCaptainSlot.Second);
        var scores = new[] {new SourceQuarterScore(0, 3, 6)};
        var stations = new SourceStationView[0];
        var time = new SourceTimeOfPossessionView(11, 17);
        var interceptions = new SourceInterceptionsView(7, 9);
        var completions = new SourcePassCompletionsView(2, 5);
        var old = new[]
        {
            new SourceMatchView(configuration, SourcePlayKind.Normal, SourceMatchPhase.PlayResult,
                period, possession, scores, stations),
            new SourceMatchView(time, configuration, SourcePlayKind.Normal, SourceMatchPhase.PlayResult,
                period, possession, scores, stations),
            new SourceMatchView(interceptions, time, configuration, SourcePlayKind.Normal,
                SourceMatchPhase.PlayResult, period, possession, scores, stations),
            new SourceMatchView(completions, interceptions, time, configuration, SourcePlayKind.Normal,
                SourceMatchPhase.PlayResult, period, possession, scores, stations)
        };
        for (int i = 0; i < old.Length; i++)
        {
            Check("old" + i + ".group-null", Bit(old[i].CompletedPassStats == null), 1);
            Check("old" + i + ".time", Bit(ReferenceEquals(old[i].TimeOfPossession, i == 0 ? null : time)), 1);
            Check("old" + i + ".interceptions", Bit(ReferenceEquals(old[i].Interceptions, i < 2 ? null : interceptions)), 1);
            Check("old" + i + ".completions", Bit(ReferenceEquals(old[i].PassCompletions, i < 3 ? null : completions)), 1);
        }
        var result = new SourceMatchResult(SourcePlayKind.Normal, SourceResultKind.Touchdown,
            SourceResultStatus.Classified, SourcePlayEndKind.CarrierTouchdown, 6, 0, true, true);
        var homePlay = new SourcePlaySelection("SYNTHETIC_API_HOME", (SourcePlaybookKind)0, 2, 3);
        var awayPlay = new SourcePlaySelection("SYNTHETIC_API_AWAY", (SourcePlaybookKind)1, 4, 5);
        var timer = new SourceSelectionTimerView(7, 9, true, 0x123u, false, -1, true, false);
        var homeRandom = new SourceRandomSelectionView(12, true, 1, (SourceRandomSelectionAction)1);
        var awayRandom = new SourceRandomSelectionView(13, true, 2, (SourceRandomSelectionAction)2);
        var current = new SourceMatchView(group, completions, interceptions, time, configuration,
            SourcePlayKind.Normal, SourceMatchPhase.PlayResult, period, possession, scores, stations,
            result, homePlay, awayPlay, 19, SourcePatOption.TwoPointPlay, SourcePatStatus.RunTwoPointPlay,
            SourceKickoffType.Quick, TeamId.Home, TeamId.Away, true,
            new SourceTeamGameplayView(TeamId.Home, true, 4, 2, 1, 2),
            new SourceTeamGameplayView(TeamId.Away, false, 11, 3, 4, 3),
            4, true, true, 3, SourceGameplayCompatibility.Portable, 3, null, timer, homeRandom, awayRandom);
        Check("new.group-retained", Bit(ReferenceEquals(current.CompletedPassStats, group)), 1);
        Check("new.completions-retained", Bit(ReferenceEquals(current.PassCompletions, completions)), 1);
        Check("new.interceptions-retained", Bit(ReferenceEquals(current.Interceptions, interceptions)), 1);
        Check("new.time-retained", Bit(ReferenceEquals(current.TimeOfPossession, time)), 1);
        Check("forward.configuration", Bit(ReferenceEquals(current.Configuration, configuration)), 1);
        Check("forward.kind", (int)current.PlayKind, 0);
        Check("forward.phase", (int)current.Phase, 5);
        Check("forward.result", (int)current.Result.Kind, 1);
        Check("forward.points", current.Result.HomePoints, 6);
        Check("forward.first-down", Bit(current.Result.JustGotFirstDown), 1);
        Check("forward.turnover", Bit(current.Result.TurnoverOnDowns), 1);
        Check("forward.period", current.Period.QuarterIndex, 0);
        Check("forward.first-possession", (int)current.Possession.FirstPossession, 1);
        Check("forward.captain", (int)current.Possession.Captain, 1);
        Check("forward.scores.home", current.QuarterScores[0].HomeScore, 3);
        Check("forward.scores.away", current.QuarterScores[0].AwayScore, 6);
        Check("forward.stations", current.Stations.Count, 0);
        Check("forward.home-play", Bit(current.HomePlay.PlaySymbol == "SYNTHETIC_API_HOME"), 1);
        Check("forward.away-play", Bit(current.AwayPlay.PlaySymbol == "SYNTHETIC_API_AWAY"), 1);
        Check("forward.countdown", current.SelectionCountdown, 19);
        Check("forward.pat-choice", (int)current.PatChoice, 2);
        Check("forward.pat-status", (int)current.PatStatus, 4);
        Check("forward.kickoff", (int)current.Kickoff.Value, 1);
        Check("forward.kicking", (int)current.KickingTeam.Value, 0);
        Check("forward.receiving", (int)current.ReceivingTeam.Value, 1);
        Check("forward.flipped", Bit(current.OffenseFlipped.Value), 1);
        Check("forward.home-gameplay", current.HomeGameplay.Handicap, 2);
        Check("forward.away-gameplay", current.AwayGameplay.Handicap, 3);
        Check("forward.catcher", current.ForwardCatcherPlayerId, 4);
        Check("forward.drones-lose", Bit(current.DronesLose), 1);
        Check("forward.drones-winning", Bit(current.DronesWinning), 1);
        Check("forward.bonus", current.HandicapRepetitionBonus, 3);
        Check("forward.compatibility", (int)current.Compatibility, 1);
        Check("forward.repairs", (int)current.CompatibilityRepairCount, 3);
        Check("forward.repair-list", current.CompatibilityRepairs.Count, 0);
        Check("forward.timer", Bit(ReferenceEquals(current.SelectionTimer, timer)), 1);
        Check("forward.home-random", (int)current.HomeRandomSelection.SourceTick, 12);
        Check("forward.away-random", (int)current.AwayRandomSelection.SourceTick, 13);
        Check("forward.audibles", Bit(current.Audibles == null), 1);
        try
        {
            new SourceMatchView((SourceCompletedPassStatsView)null, completions, interceptions, time,
                configuration, SourcePlayKind.Normal, SourceMatchPhase.PlayResult, period, possession, scores, stations);
        }
        catch (ArgumentNullException ex) { nullGroup = ex.ParamName == "completedPassStats"; }
        try
        {
            new SourceMatchView(group, completions, interceptions, null, configuration,
                SourcePlayKind.Normal, SourceMatchPhase.PlayResult, period, possession, scores, stations);
        }
        catch (ArgumentNullException ex) { inheritedNull = ex.ParamName == "timeOfPossession"; }
        Check("new.null-group-rejected", Bit(nullGroup), 1);
        Check("new.inherited-null-rejected", Bit(inheritedNull), 1);
    }

    private static void Legacy()
    {
        var model = new SimulationState();
        Check("match-null", Bit(new GameSnapshot(model).Match == null), 1);
        StateTuple(model, "state", new[] {0,0,0,0,0,0,0,0});
    }

    private static int Main(string[] args)
    {
        try
        {
            if (args.Length == 2 && args[0] == "--case") selectedCase = args[1];
            else if (args.Length != 0) throw new ArgumentException("Only --case <declared-name> is supported.");
            foreach (Scenario scenario in Scenarios)
                for (int side = 0; side < 2; side++)
                {
                    Scenario captured = scenario;
                    int team = side;
                    Case(scenario.Name + (side == 0 ? ".home" : ".away"), () => Table(captured, team));
                }
            Case("pass.lifecycle", Lifecycle);
            Case("pass.api", Api);
            Case("pass.legacy-neutral", Legacy);
            if (selectedCase != null && Cases.Count != 1) throw new ArgumentException("Unknown declared case.");
            Console.WriteLine(JsonSerializer.Serialize(new {status = "passed", selectedCase,
                cases = Cases, assertions = Fields.Count, fields = Fields, fixture = Program.FixtureDiagnostics()}));
            return 0;
        }
        catch (NamedAssertion failure)
        {
            Console.WriteLine(JsonSerializer.Serialize(new {status = "assertion-failed", selectedCase,
                failedAssertion = failure.Field, cases = Cases, assertions = Fields.Count, fields = Fields,
                fixture = Program.FixtureDiagnostics()}));
            return 2;
        }
        catch (Exception failure)
        {
            Console.WriteLine(JsonSerializer.Serialize(new {status = "unexpected-error", selectedCase,
                error = failure.GetType().FullName, message = failure.Message, cases = Cases,
                assertions = Fields.Count, fields = Fields, fixture = Program.FixtureDiagnostics()}));
            return 1;
        }
    }
}
