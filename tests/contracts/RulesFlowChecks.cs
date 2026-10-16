using System;
using Blitz.Gameplay;

internal static class RulesFlowChecks
{
    internal static void Run()
    {
        ClockCases();
        RuleCases();
        FlowCases();
    }

    private static void Eq<T>(T expected, T actual, string name)
    {
        Checks.Equal(expected, actual, name);
    }

    private static SourceRulesState State(int side = 0, int initial = 0, int down = 1,
        int los = 20, int first = 50, SourcePlayType type = SourcePlayType.Normal, bool noFirst = false)
    {
        return new SourceRulesState(side, initial, down, los, first, type, noFirst);
    }

    private static SourcePlayResolution End(SourceRulesState state, SourcePlayEndCause cause,
        float x = 0, SourceBallType ball = SourceBallType.Pass,
        SourceGameFlags flags = SourceGameFlags.None, int last = -1, float fumble = 0)
    {
        return SourceMatchRules.Resolve(new SourcePlayEndInput(state, cause, ball, flags,
            last < 0 ? state.OffenseSide : last, x, fumbleX: fumble));
    }

    private static void ClockCases()
    {
        int[] starts = { 0x20000, 0x1f00, 0x1f00, 0x2800, 0x1f00 };
        int[] quarters = { 0, 0, 1, 0, 0 };
        int[] play = { 0, 0, 0, 17, 0 };
        int[] adjust = { 2, 2, 2, 2, -1 };
        int[] expected = { 0x13b18, 0x1e14, 0x1e22, 0x2730, 0x1e12 };
        for (int i = 0; i < starts.Length; i++)
        {
            var step = SourceClock.Step(SourceClock.Create(starts[i], play[i]),
                new SourceClockInput(0, true, quarters[i], adjust[i]));
            Eq(expected[i], step.State.PackedTime, "pinned clock " + i);
            Eq(1, step.TimeOfPossessionDelta, "possession " + i);
            Eq(1, step.SleepTicks, "sleep " + i);
        }
        Eq(285, SourceClock.DefaultAutoHikeTicks, "source auto hike");
        var pending = SourceClock.Step(SourceClock.Create(), new SourceClockInput(0, true));
        Eq(SourceClockOperation.NotScheduled,
            SourceClock.Step(pending.State, new SourceClockInput(0, true)).Operation, "no duplicate source wake");
        var discarded = SourceClock.DiscardFraction(pending.State);
        Eq(0x13b00, discarded.PackedTime, "fraction discard published");
        Eq(0x13b18, discarded.LocalPackedTime, "fraction discard preserves process local");
        Eq(0x13b17, SourceClock.Step(discarded, new SourceClockInput(1, true)).State.PackedTime,
            "resumed process republishes local");
        Eq(discarded.PackedTime,
            SourceClock.Step(discarded, new SourceClockInput(1, false)).State.PackedTime, "paused doesn't republish");
        Eq(0, SourceClock.ResetPlayTime(discarded).PlayTime, "play reset");
        Eq(0x1e12, SourceClock.Step(SourceClock.Create(0x1f00),
            new SourceClockInput(0, true, timeAdjustment: 2, adjustmentReadSucceeded: false)).State.PackedTime,
            "adjustment read failure");
        var expired = SourceClock.Step(SourceClock.Create(1), new SourceClockInput(0, true));
        Eq(true, expired.PeriodExpired, "expiry from final subtick");
        Eq(false, expired.State.ProcessComplete, "sleep before loop exits");
        Eq(SourceClockOperation.ProcessExit,
            SourceClock.Step(expired.State, new SourceClockInput(1, true)).Operation, "clock loop exits");
    }

    private static void RuleCases()
    {
        var td = End(State(), SourcePlayEndCause.CarrierTouchdown, 391);
        Eq(6, td.Side0Points, "TD exactly six");
        Eq(0, td.Side1Points, "TD other team");
        Eq(-1, td.State.Down, "TD down sentinel");
        Eq(90, td.State.LineOfScrimmage, "two point line");
        Eq(SourcePlayType.ExtraPoint, td.State.PlayType, "TD requires PAT decision");
        var safety = End(State(), SourcePlayEndCause.CarrierDownInOwnEndZone, -391);
        Eq(2, safety.Side1Points, "safety award");
        Eq(0, safety.State.OffenseSide, "safety retains kicking side");
        Eq(SourcePlayType.Kickoff, safety.State.PlayType, "safety next kick");
        var patSafety = End(State(type: SourcePlayType.ExtraPoint),
            SourcePlayEndCause.BallOutOfBounds, -391, SourceBallType.Fumble);
        Eq(1, patSafety.Side1Points, "PAT safety award");
        var returnConversion = End(State(1, 0, -1, 90, 100, SourcePlayType.ExtraPoint),
            SourcePlayEndCause.CarrierTouchdown, -391, flags: SourceGameFlags.PossessionChange);
        Eq(2, returnConversion.Side1Points, "returned conversion scores returning side");
        Eq(0, returnConversion.State.OffenseSide, "returned conversion resets kickoff to initial side");
        var fourthIncomplete = End(State(down: 4, los: 24, first: 54),
            SourcePlayEndCause.DeadBallInBounds);
        Eq(1, fourthIncomplete.State.OffenseSide, "fourth incomplete flips");
        Eq(76, fourthIncomplete.State.LineOfScrimmage, "fourth incomplete source mirror");
        Eq(100, fourthIncomplete.State.FirstDown, "goal to go");
        Eq(true, fourthIncomplete.TurnoverOnDowns, "fourth incomplete turnover flag");
        var touchback = End(State(1, 0), SourcePlayEndCause.CarrierDownInOwnEndZone, 391,
            flags: SourceGameFlags.RecoveredInEndZone);
        Eq(SourcePlayResult.Touchback, touchback.Classification.Result, "interception endzone touchback");
        Eq(20, touchback.State.LineOfScrimmage, "touchback LOS");
        Eq(SourceGameFlags.RecoveredInEndZone | SourceGameFlags.PossessionChange, touchback.Flags,
            "repair possession bit before classifier");
        var bobble = End(State(1, 0), SourcePlayEndCause.CarrierTackledInBounds, last: 0);
        Eq(0, bobble.State.OffenseSide, "unestablished control restored");
        Eq(SourceGameFlags.None, bobble.Flags, "no spurious possession flag");
        var kickoffOob = End(State(type: SourcePlayType.Kickoff), SourcePlayEndCause.BallOutOfBounds,
            -350, SourceBallType.Kickoff);
        Eq(1, kickoffOob.State.OffenseSide, "untouched kick OOB receiving side");
        Eq(40, kickoffOob.State.LineOfScrimmage, "untouched kick OOB receiving LOS40");
        var fg = End(State(), SourcePlayEndCause.DeadBallInBounds, ball: SourceBallType.FieldGoal,
            flags: SourceGameFlags.ThroughUprights);
        Eq(3, fg.Side0Points, "normal classifier field goal");
        var empty = SourceMatchRules.Resolve(new SourcePlayEndInput(State(type: SourcePlayType.FieldGoal),
            SourcePlayEndCause.DeadBallInBounds, SourceBallType.FieldGoal, SourceGameFlags.ThroughUprights,
            0, 0, previousResult: SourcePlayResult.ExtraPoint));
        Eq(SourceClassifierStatus.IntentionalNoOp, empty.Classification.Status, "empty FG row is explicit");
        Eq(SourcePlayResult.ExtraPoint, empty.Classification.Result, "empty row preserves prior result");
        Eq(false, empty.ResultSwitchHasCase, "PR_EXTRA_POINT has no result switch case");
        Eq(0, empty.Side0Points, "empty FG row is NOT invented success");
        var bad = SourceMatchRules.Classify(new SourceResultInput(SourcePlayType.Normal,
            SourcePlayEndCause.BallOutOfBounds, SourceBallType.Kickoff, 0, 50, 0));
        Eq(SourceClassifierStatus.InvalidSourceBranch, bad.Status, "invalid source hang branch explicit");
        Eq(0f, SourceMatchRules.RoundPlayEndPosition(-0.999f), "negative near zero C cast");
        Eq(0f, SourceMatchRules.RoundPlayEndPosition(-7.9f), "negative C integer division");
        Eq(-7.8f, SourceMatchRules.RoundPlayEndPosition(-8f), "negative exact integer boundary");
    }

    private static void FlowCases()
    {
        var patState = State(down: -1, los: 90, first: 100, type: SourcePlayType.ExtraPoint);
        Eq(SourcePatDisposition.AwaitHumanSelection,
            SourceMatchFlow.SelectPat(new SourcePatInput(patState, 0, 0x10000, 6, 0, true)).Disposition,
            "human PAT choice");
        var good = SourceMatchFlow.SelectPat(new SourcePatInput(patState, 0, 0x10000, 6, 0, true,
            SourcePatChoice.FreeKick));
        Eq(1, good.Side0Points, "good free PAT");
        Eq(0, good.DrawsConsumed, "no random outside miss condition");
        var missRequest = SourceMatchFlow.SelectPat(new SourcePatInput(patState, 3, 0x100, 21, 0, true,
            SourcePatChoice.FreeKick));
        Eq(SourceRandomCall.FreePat, missRequest.RandomRequest.Call, "human late blowout miss request");
        Eq(100, missRequest.RandomRequest.BoundOrModulus, "free PAT source random bound");
        var miss = SourceMatchFlow.SelectPat(new SourcePatInput(patState, 3, 0x100, 21, 0, true,
            SourcePatChoice.FreeKick, freeKickDraw: 1));
        Eq(0, miss.Side0Points, "human misses draw1");
        var skip = SourceMatchFlow.SelectPat(new SourcePatInput(patState, 3, 0, 7, 6, true));
        Eq(SourcePatDisposition.SkipUnnecessary, skip.Disposition, "unnecessary final PAT skip");
        Eq(SourcePlayType.Normal, skip.State.PlayType, "skip clears PAT for EOQ");
        Eq(true, skip.CheckEndOfPeriod, "skip checks EOQ");
        Eq(true, SourceMatchFlow.GoForTwo(0, 9).Value, "CPU down9 two point no draw");
        Eq(false, SourceMatchFlow.GoForTwo(9, 0).Value, "CPU up9 free no draw");
        Eq(SourceRandomCall.GoForTwo, SourceMatchFlow.GoForTwo(0, 0).RandomRequest.Call,
            "zero probability still requests source draw");
        Eq(false, SourceMatchFlow.GoForTwo(0, 0, 0).Value, "tied CPU takes one");
        Eq(true, SourceMatchFlow.GoForTwo(0, 2, 9).Value, "CPU down2 goes two");
        var kickState = State(type: SourcePlayType.Kickoff);
        var opening = SourceMatchFlow.PrepareKickoff(new SourceKickoffInput(kickState, 0,
            SourceClock.QuarterTime, true, false, 0, 0, true));
        Eq(SourceKickoffKind.Opening, opening.Kind, "opening suppresses onside");
        Eq(30, opening.State.LineOfScrimmage, "opening source LOS");
        Eq(90, opening.AutoHikeTicks, "opening snap clock");
        Eq(SourceGameFlags.OpeningKick, opening.Flags, "opening flags");
        var quick = SourceMatchFlow.PrepareKickoff(new SourceKickoffInput(kickState, 0,
            0x10000, false, false, 0, 0));
        Eq(1, quick.State.OffenseSide, "quick kick starts as receiver offense");
        Eq(1, quick.State.InitialOffenseSide, "quick kick changes off_init");
        Eq(1, quick.LastPossession, "quick kick changes last_pos");
        Eq(0, quick.KickingSide, "quick kicking team retained separately");
        Eq(40, quick.State.LineOfScrimmage, "quick receiving LOS");
        Eq(3, quick.AutoHikeTicks, "later kick auto hike");
        var onside = SourceMatchFlow.PrepareKickoff(new SourceKickoffInput(kickState, 3,
            0x2000, false, false, 0, 0, true));
        Eq(SourceKickoffKind.Onside, onside.Kind, "onside kind");
        Eq(0, onside.State.OffenseSide, "onside retains kicker off_side");
        Eq(30, onside.State.LineOfScrimmage, "onside LOS");
        var halftimeKick = SourceMatchFlow.PrepareKickoff(new SourceKickoffInput(kickState, 2,
            SourceClock.QuarterTime, false, true, 0, 0, true));
        Eq(SourceKickoffKind.Quick, halftimeKick.Kind, "halftime forbids onside");
        Eq(SourceRandomKind.CStandardRand, SourceMatchFlow.CpuOnside(new SourceCpuOnsideInput(0,
            0x20000, 0, 0)).RandomRequest.Kind, "onside raw rand not randrng");
        Eq(true, SourceMatchFlow.CpuOnside(new SourceCpuOnsideInput(0,
            0x20000, 0, 0, 33)).Value, "surprise onside even early");
        Eq(false, SourceMatchFlow.CpuOnside(new SourceCpuOnsideInput(3,
            0x100, 0, 7, 1, 5)).Value, "20 percent onside suppression");
        Eq(true, SourceMatchFlow.CpuOnside(new SourceCpuOnsideInput(3,
            0x100, 0, 7, 1, 1)).Value, "late losing onside");
        var live = SourceMatchFlow.AdvancePeriod(new SourcePeriodInput(State(), 3, 0, 7, 6, 0,
            currentPlayFinished: false));
        Eq(SourcePeriodDisposition.FinishCurrentPlay, live.Disposition, "expiry never kills live play");
        var defer = SourceMatchFlow.AdvancePeriod(new SourcePeriodInput(patState, 3, 0, 6, 7, 0));
        Eq(SourcePeriodDisposition.DeferredForExtraPoint, defer.Disposition, "expired game allows meaningful PAT");
        var q1 = SourceMatchFlow.AdvancePeriod(new SourcePeriodInput(State(side: 1), 0, 0, 0, 0, 0));
        Eq(1, q1.QuarterIndex, "next quarter zero based");
        Eq(1, q1.State.OffenseSide, "q1 preserves possession");
        Eq(SourceClock.QuarterTime, q1.PackedTime, "restart source quarter clock");
        var half = SourceMatchFlow.AdvancePeriod(new SourcePeriodInput(State(), 1, 0, 0, 0, 0, 0));
        Eq(1, half.State.OffenseSide, "halftime sets inverse first_pos before quick kick");
        Eq(true, half.HalfKickoff, "halftime latch");
        var otAsk = SourceMatchFlow.AdvancePeriod(new SourcePeriodInput(State(), 3, 0, 7, 7, 0));
        Eq(SourceRandomCall.OvertimeKickoff, otAsk.RandomRequest.Call, "first overtime source RNG");
        var ot1 = SourceMatchFlow.AdvancePeriod(new SourcePeriodInput(State(), 3, 0, 7, 7, 0,
            overtimeFirstDraw: 0));
        Eq(4, ot1.QuarterIndex, "first overtime index");
        Eq(1, ot1.State.OffenseSide, "first OT kick pre-play side");
        Eq(1, ot1.OvertimeKickoff, "OT alternation memory");
        var ot2 = SourceMatchFlow.AdvancePeriod(new SourcePeriodInput(State(), 4, 0, 7, 7, 0, 1));
        Eq(0, ot2.State.OffenseSide, "second OT alternates");
        Eq(0, ot2.DrawsConsumed, "second OT no coin draw");
        Eq(false, SourceMatchFlow.AdvancePeriod(new SourcePeriodInput(State(), 4,
            0x100, 9, 7, 0, 1)).MatchEnded, "not sudden death");
        Eq(true, SourceMatchFlow.AdvancePeriod(new SourcePeriodInput(State(), 6,
            0, 7, 7, 0, 0)).MatchEnded, "tie ends after third overtime");
        Eq(9, SourceMatchFlow.PlaySelectionCountdownStart(0), "first play select caller argument");
        Eq(5, SourceMatchFlow.PlaySelectionCountdownStart(1), "later play select caller argument");
        var punt = SourceMatchFlow.CpuPickPlay(new SourceCpuPlayInput(State(down: 4, los: 20, first: 40),
            0, 0, 0x10000, 0, 0));
        Eq(SourceRandomCall.CpuFakePunt, punt.RandomRequest.Call, "CPU fourth punt fake check");
        Eq(10, punt.RandomRequest.BoundOrModulus, "CPU punt fake chance");
        var shortFake = SourceMatchFlow.CpuPickPlay(new SourceCpuPlayInput(
            State(down: 4, los: 45, first: 49), 0, 0, 0x10000, 0, 4,
            draws: new SourceRandomDraws(51, 1)));
        Eq(SourceCpuPlayFamily.FakePunt, shortFake.Family, "CPU fourth down short selection override");
        Eq(51, shortFake.PreOverridePoolIndex, "CPU short selection still consumed before fake override");
        Eq(2, shortFake.DrawsConsumed, "CPU short override consumes two ordered draws");
        var defender = SourceMatchFlow.CpuPickPlay(new SourceCpuPlayInput(
            State(los: 90, first: 100), 1, 0, 0x10000, 0, 0,
            opponentPlay: SourceOpponentPlay.Custom, draws: new SourceRandomDraws(79, 0, 26)));
        Eq(SourceCpuPlayFamily.ShortDefenseCustom, defender.Family, "custom defense pool");
        Eq(26, defender.PoolIndex, "custom defense full weighted pool index");
        Eq(3, defender.DrawsConsumed, "goal line short defense ordered three draws");
        var defensiveTd = End(State(1, 0), SourcePlayEndCause.CarrierTouchdown, -391,
            flags: SourceGameFlags.PossessionChange);
        var nextPlay = SourceMatchFlow.BeginPlay(defensiveTd.State, 0, 0x10000);
        Eq(1, nextPlay.State.InitialOffenseSide, "new play establishes defensive TD scorer as off_init");
        Eq(1, nextPlay.LastPossession, "new play establishes last_pos");
        Eq(SourceGameFlags.None, nextPlay.Flags, "new play clears previous play flags");
        Eq(1, nextPlay.OffensivePlayCounterSide, "new play offensive play counter side");
        var returnTdPat = SourceMatchFlow.SelectPat(new SourcePatInput(nextPlay.State, 0, 0x10000,
            6, 0, true, SourcePatChoice.FreeKick));
        Eq(1, returnTdPat.Side1Points, "defensive TD free PAT awards scorer");
        Eq(1, returnTdPat.State.OffenseSide, "defensive TD free PAT keeps scorer as kicker");
        var openingStart = SourceMatchFlow.BeginPlay(kickState, 0, SourceClock.QuarterTime, true);
        Eq(1, openingStart.OffensivePlayCounterSide, "first kickoff counts receiving team's offensive play");
        Eq(SourceGameFlags.OpeningKick, openingStart.Flags, "new play opening kickoff flag");
    }
}
