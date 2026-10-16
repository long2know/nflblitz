using System;

namespace Blitz.Gameplay
{
    public enum SourceRandomKind { None, RandRange, CStandardRand }
    public enum SourceRandomCall
    {
        None, GoForTwo, FreePat, CpuOnsideSurprise, CpuOnsideSuppress, OvertimeKickoff,
        CpuFakePunt, CpuOffensePool, CpuFourthDownFakePunt, CpuDefenseGoalLine,
        CpuDefenseGoalLineKind, CpuDefenseNearGoal, CpuDefensePool
    }

    public readonly struct SourceRandomRequest
    {
        public SourceRandomKind Kind { get; }
        public SourceRandomCall Call { get; }
        public int BoundOrModulus { get; }
        public int CallIndex { get; }
        public bool IsRequired { get { return Kind != SourceRandomKind.None; } }

        internal SourceRandomRequest(SourceRandomKind kind, SourceRandomCall call, int boundOrModulus, int callIndex)
        {
            Kind = kind;
            Call = call;
            BoundOrModulus = boundOrModulus;
            CallIndex = callIndex;
        }
    }

    public readonly struct SourceRandomDecision
    {
        public bool IsResolved { get; }
        public bool Value { get; }
        public int DrawsConsumed { get; }
        public SourceRandomRequest RandomRequest { get; }

        internal SourceRandomDecision(bool isResolved, bool value, int drawsConsumed = 0,
            SourceRandomRequest randomRequest = default(SourceRandomRequest))
        {
            IsResolved = isResolved;
            Value = value;
            DrawsConsumed = drawsConsumed;
            RandomRequest = randomRequest;
        }
    }

    public enum SourcePatChoice { Unselected, FreeKick, TwoPointPlay }
    public enum SourcePatDisposition
    {
        NotExtraPoint, SkipUnnecessary, AwaitHumanSelection, AwaitRandom,
        RunTwoPointPlay, FreeKickComplete
    }

    public readonly struct SourcePatInput
    {
        public SourceRulesState State { get; }
        public int QuarterIndex { get; }
        public int PackedTime { get; }
        public int OffenseScore { get; }
        public int DefenseScore { get; }
        public bool IsHumanOffense { get; }
        public SourcePatChoice Choice { get; }
        public int? GoForTwoDraw { get; }
        public int? FreeKickDraw { get; }

        // A non-Unselected choice is the final team_play returned by play_select.
        // Human timeouts can therefore supply TwoPointPlay after cpu_pick_play.
        public SourcePatInput(SourceRulesState state, int quarterIndex, int packedTime,
            int offenseScore, int defenseScore, bool isHumanOffense,
            SourcePatChoice choice = SourcePatChoice.Unselected, int? goForTwoDraw = null,
            int? freeKickDraw = null)
        {
            State = state;
            QuarterIndex = quarterIndex;
            PackedTime = packedTime;
            OffenseScore = offenseScore;
            DefenseScore = defenseScore;
            IsHumanOffense = isHumanOffense;
            Choice = choice;
            GoForTwoDraw = goForTwoDraw;
            FreeKickDraw = freeKickDraw;
        }
    }

    public readonly struct SourcePatDecision
    {
        public SourcePatDisposition Disposition { get; }
        public SourceRulesState State { get; }
        public SourcePatChoice Choice { get; }
        public int Side0Points { get; }
        public int Side1Points { get; }
        public bool KickGood { get; }
        public bool CheckEndOfPeriod { get; }
        public int DrawsConsumed { get; }
        public SourceRandomRequest RandomRequest { get; }

        internal SourcePatDecision(SourcePatDisposition disposition, SourceRulesState state,
            SourcePatChoice choice = SourcePatChoice.Unselected, int points = 0, bool kickGood = false,
            bool checkEndOfPeriod = false, int drawsConsumed = 0,
            SourceRandomRequest randomRequest = default(SourceRandomRequest), int scoringSide = -1)
        {
            Disposition = disposition;
            State = state;
            Choice = choice;
            int awardSide = scoringSide == -1 ? state.OffenseSide : scoringSide;
            Side0Points = awardSide == 0 ? points : 0;
            Side1Points = awardSide == 1 ? points : 0;
            KickGood = kickGood;
            CheckEndOfPeriod = checkEndOfPeriod;
            DrawsConsumed = drawsConsumed;
            RandomRequest = randomRequest;
        }
    }

    public enum SourceKickoffKind { Opening, Quick, Onside }

    public readonly struct SourcePlayStart
    {
        public SourceRulesState State { get; }
        public SourceGameFlags Flags { get; }
        public int LastPossession { get; }
        public int OffensivePlayCounterSide { get; }

        internal SourcePlayStart(SourceRulesState state, SourceGameFlags flags, int offensivePlayCounterSide)
        {
            State = state;
            Flags = flags;
            LastPossession = state.OffenseSide;
            OffensivePlayCounterSide = offensivePlayCounterSide;
        }
    }

    public readonly struct SourceKickoffInput
    {
        public SourceRulesState State { get; }
        public int QuarterIndex { get; }
        public int PackedTime { get; }
        public bool FirstKickoff { get; }
        public bool HalfKickoff { get; }
        public int FirstPossessionSide { get; }
        public int Captain { get; }
        public bool RequestedOnsideKick { get; }

        public SourceKickoffInput(SourceRulesState state, int quarterIndex, int packedTime,
            bool firstKickoff, bool halfKickoff, int firstPossessionSide, int captain,
            bool requestedOnsideKick = false)
        {
            SourceMatchRules.RequireSide(firstPossessionSide);
            SourceMatchRules.RequireSide(captain);
            State = state;
            QuarterIndex = quarterIndex;
            PackedTime = packedTime;
            FirstKickoff = firstKickoff;
            HalfKickoff = halfKickoff;
            FirstPossessionSide = firstPossessionSide;
            Captain = captain;
            RequestedOnsideKick = requestedOnsideKick;
        }
    }

    public readonly struct SourceKickoffPlan
    {
        public SourceRulesState State { get; }
        public SourceKickoffKind Kind { get; }
        public SourceGameFlags Flags { get; }
        public int LastPossession { get; }
        public int KickingSide { get; }
        public int ReceivingSide { get; }
        public int Captain { get; }
        public int AutoHikeTicks { get; }
        public bool FirstKickoffAfterPreparation { get { return false; } }
        public float LinePositionZ { get { return 0f; } }
        public SourceBallType BallType { get { return SourceBallType.Kickoff; } }
        public int BallCarrier { get { return -1; } }

        internal SourceKickoffPlan(SourceRulesState state, SourceKickoffKind kind,
            SourceGameFlags flags, int kickingSide, int captain, int autoHikeTicks)
        {
            State = state;
            Kind = kind;
            Flags = flags;
            LastPossession = state.OffenseSide;
            KickingSide = kickingSide;
            ReceivingSide = 1 - kickingSide;
            Captain = captain;
            AutoHikeTicks = autoHikeTicks;
        }
    }

    public readonly struct SourceCpuOnsideInput
    {
        public int QuarterIndex { get; }
        public int PackedTime { get; }
        public int KickingScore { get; }
        public int ReceivingScore { get; }
        public int? SurpriseRand { get; }
        public int? SuppressRand { get; }

        // These are raw nonnegative C rand() outputs, NOT randrng() draws.
        public SourceCpuOnsideInput(int quarterIndex, int packedTime, int kickingScore,
            int receivingScore, int? surpriseRand = null, int? suppressRand = null)
        {
            QuarterIndex = quarterIndex;
            PackedTime = packedTime;
            KickingScore = kickingScore;
            ReceivingScore = receivingScore;
            SurpriseRand = surpriseRand;
            SuppressRand = suppressRand;
        }
    }

    public enum SourcePeriodDisposition
    {
        NotExpired, FinishCurrentPlay, DeferredForExtraPoint, AttractMode,
        AwaitOvertimeDraw, NextQuarter, HalftimeKickoff, OvertimeKickoff,
        MatchEnded, CabinetContinuationDeclined
    }

    public readonly struct SourcePeriodInput
    {
        public SourceRulesState State { get; }
        public int QuarterIndex { get; }
        public int PackedTime { get; }
        public int Side0Score { get; }
        public int Side1Score { get; }
        public int FirstPossessionSide { get; }
        public int OvertimeKickoff { get; }
        public bool CurrentPlayFinished { get; }
        public bool IsAttractMode { get; }
        public bool ContinueAfterCabinetGate { get; }
        public int? OvertimeFirstDraw { get; }

        public SourcePeriodInput(SourceRulesState state, int quarterIndex, int packedTime,
            int side0Score, int side1Score, int firstPossessionSide, int overtimeKickoff = -1,
            bool currentPlayFinished = true, bool isAttractMode = false,
            bool continueAfterCabinetGate = true, int? overtimeFirstDraw = null)
        {
            if (quarterIndex < 0 || quarterIndex > 6)
                throw new ArgumentOutOfRangeException(nameof(quarterIndex));
            SourceMatchRules.RequireSide(firstPossessionSide);
            if (overtimeKickoff < -1 || overtimeKickoff > 1)
                throw new ArgumentOutOfRangeException(nameof(overtimeKickoff));
            State = state;
            QuarterIndex = quarterIndex;
            PackedTime = packedTime;
            Side0Score = side0Score;
            Side1Score = side1Score;
            FirstPossessionSide = firstPossessionSide;
            OvertimeKickoff = overtimeKickoff;
            CurrentPlayFinished = currentPlayFinished;
            IsAttractMode = isAttractMode;
            ContinueAfterCabinetGate = continueAfterCabinetGate;
            OvertimeFirstDraw = overtimeFirstDraw;
        }
    }

    public readonly struct SourcePeriodDecision
    {
        public SourcePeriodDisposition Disposition { get; }
        public SourceRulesState State { get; }
        public int CompletedQuarterIndex { get; }
        public int QuarterIndex { get; }
        public int PackedTime { get; }
        public int OvertimeKickoff { get; }
        public bool HalfKickoff { get; }
        public bool RestartClockProcess { get; }
        public bool MatchEnded { get; }
        public bool ClearOffTurnover { get { return RestartClockProcess; } }
        public bool CabinetGateRequired { get; }
        public int DrawsConsumed { get; }
        public SourceRandomRequest RandomRequest { get; }

        internal SourcePeriodDecision(SourcePeriodInput input, SourcePeriodDisposition disposition,
            SourceRulesState state, bool advanceQuarter = false, bool restartClock = false,
            bool halfKickoff = false, bool matchEnded = false, bool cabinetGateRequired = false,
            int overtimeKickoff = -2, int drawsConsumed = 0,
            SourceRandomRequest randomRequest = default(SourceRandomRequest))
        {
            Disposition = disposition;
            State = state;
            CompletedQuarterIndex = input.QuarterIndex;
            QuarterIndex = input.QuarterIndex + (advanceQuarter ? 1 : 0);
            PackedTime = restartClock ? SourceClock.QuarterTime : input.PackedTime;
            OvertimeKickoff = overtimeKickoff == -2 ? input.OvertimeKickoff : overtimeKickoff;
            HalfKickoff = halfKickoff;
            RestartClockProcess = restartClock;
            MatchEnded = matchEnded;
            CabinetGateRequired = cabinetGateRequired;
            DrawsConsumed = drawsConsumed;
            RandomRequest = randomRequest;
        }
    }

    public enum SourceOpponentPlay { Other, Punt, FieldGoal, Custom }
    public enum SourceCpuPlayFamily
    {
        Punt, FakePunt, FieldGoal, ShortOffense, LongOffense, PuntReturn, BlockFieldGoal,
        GoalLineDefense, BlitzDefense, ShortDefense, LongDefense, ShortDefenseCustom, LongDefenseCustom
    }

    public readonly struct SourceRandomDraws
    {
        public int? First { get; }
        public int? Second { get; }
        public int? Third { get; }

        public SourceRandomDraws(int? first, int? second = null, int? third = null)
        {
            First = first;
            Second = second;
            Third = third;
        }

        internal int? At(int index)
        {
            switch (index)
            {
                case 0: return First;
                case 1: return Second;
                case 2: return Third;
                default: throw new ArgumentOutOfRangeException(nameof(index));
            }
        }
    }

    public readonly struct SourceCpuPlayInput
    {
        public SourceRulesState State { get; }
        public int SelectingSide { get; }
        public int QuarterIndex { get; }
        public int PackedTime { get; }
        public int SelectingScore { get; }
        public int OpponentScore { get; }
        public bool SuperFieldGoal { get; }
        public SourceOpponentPlay OpponentPlay { get; }
        public SourceRandomDraws Draws { get; }

        public SourceCpuPlayInput(SourceRulesState state, int selectingSide, int quarterIndex,
            int packedTime, int selectingScore, int opponentScore, bool superFieldGoal = false,
            SourceOpponentPlay opponentPlay = SourceOpponentPlay.Other,
            SourceRandomDraws draws = default(SourceRandomDraws))
        {
            SourceMatchRules.RequireSide(selectingSide);
            State = state;
            SelectingSide = selectingSide;
            QuarterIndex = quarterIndex;
            PackedTime = packedTime;
            SelectingScore = selectingScore;
            OpponentScore = opponentScore;
            SuperFieldGoal = superFieldGoal;
            OpponentPlay = opponentPlay;
            Draws = draws;
        }
    }

    public readonly struct SourceCpuPlayDecision
    {
        public bool IsResolved { get; }
        public SourceCpuPlayFamily Family { get; }
        public int PoolIndex { get; }
        public int PreOverridePoolIndex { get; }
        public int DrawsConsumed { get; }
        public SourceRandomRequest RandomRequest { get; }

        internal SourceCpuPlayDecision(bool isResolved, SourceCpuPlayFamily family,
            int poolIndex = -1, int preOverridePoolIndex = -1, int drawsConsumed = 0,
            SourceRandomRequest randomRequest = default(SourceRandomRequest))
        {
            IsResolved = isResolved;
            Family = family;
            PoolIndex = poolIndex;
            PreOverridePoolIndex = preOverridePoolIndex;
            DrawsConsumed = drawsConsumed;
            RandomRequest = randomRequest;
        }
    }

    // Gameplay decisions in nfl.c 875-1173, 1982-2463, 2567-2716, 3111-3143
    // (15b49474...), and playsel.c 1045-1053, 1240-1257, 1344-1369,
    // 1496-1503, 2710-2737 (f5e87b92...). Cabinet I/O and motion are adapters.
    public static class SourceMatchFlow
    {
        private static readonly int[] TwoPointChances = { 0, 2, 2, 10, 0, 0, 10, 0, 0, 10, 0, 0, 8, 10, 0, 0, 9 };

        // nfl.c 833-858: run ONCE at the top of a new source game_proc play,
        // before PAT selection or ordinary play selection. In particular a
        // defensive TD must make its scoring side the new off_init before PAT.
        public static SourcePlayStart BeginPlay(SourceRulesState state, int quarterIndex,
            int packedTime, bool firstKickoff = false)
        {
            int side = state.OffenseSide;
            SourceGameFlags flags = SourceGameFlags.None;
            if (quarterIndex % 2 == 0 && state.PlayType == SourcePlayType.Kickoff
                && packedTime == SourceClock.QuarterTime)
                flags |= SourceGameFlags.OpeningKick;
            var initialized = new SourceRulesState(side, side, state.Down,
                state.LineOfScrimmage, state.FirstDown, state.PlayType, state.NoFirstDown);
            return new SourcePlayStart(initialized, flags, firstKickoff ? 1 - side : side);
        }

        public static int PlaySelectionCountdownStart(int quarterIndex)
        {
            // Argument passed to timedown_play_select, not a wall-clock duration.
            return quarterIndex == 0 ? 9 : 5;
        }

        public static SourceRandomDecision GoForTwo(int offenseScore, int defenseScore, int? draw = null)
        {
            int deficit = defenseScore - offenseScore;
            if (deficit <= -9)
                return new SourceRandomDecision(true, false);
            if (deficit >= 9)
                return new SourceRandomDecision(true, true);
            if (!draw.HasValue)
                return new SourceRandomDecision(false, false, 0,
                    RangeRequest(SourceRandomCall.GoForTwo, 10, 0));
            RequireDraw(draw.Value, 10);
            // Preserve the draw even when the probability is zero or ten.
            return new SourceRandomDecision(true, draw.Value < TwoPointChances[8 - deficit], 1);
        }

        public static SourcePatDecision SelectPat(SourcePatInput input)
        {
            SourceRulesState state = input.State;
            if (state.PlayType != SourcePlayType.ExtraPoint)
                return new SourcePatDecision(SourcePatDisposition.NotExtraPoint, state);

            if (input.PackedTime == 0 && input.QuarterIndex > 2 && input.OffenseScore > input.DefenseScore)
            {
                var skipped = WithPlayType(state, SourcePlayType.Normal);
                return new SourcePatDecision(SourcePatDisposition.SkipUnnecessary, skipped,
                    checkEndOfPeriod: true);
            }

            SourcePatChoice choice = input.Choice;
            int draws = 0;
            if (choice == SourcePatChoice.Unselected)
            {
                if (input.IsHumanOffense)
                    return new SourcePatDecision(SourcePatDisposition.AwaitHumanSelection, state);
                SourceRandomDecision two = GoForTwo(input.OffenseScore, input.DefenseScore, input.GoForTwoDraw);
                if (!two.IsResolved)
                    return new SourcePatDecision(SourcePatDisposition.AwaitRandom, state,
                        randomRequest: two.RandomRequest);
                draws += two.DrawsConsumed;
                choice = two.Value ? SourcePatChoice.TwoPointPlay : SourcePatChoice.FreeKick;
            }

            if (choice == SourcePatChoice.TwoPointPlay)
                return new SourcePatDecision(SourcePatDisposition.RunTwoPointPlay, state,
                    choice, drawsConsumed: draws);
            if (choice != SourcePatChoice.FreeKick)
                throw new ArgumentOutOfRangeException(nameof(input));

            int lead = input.OffenseScore - input.DefenseScore;
            bool canMiss = input.IsHumanOffense
                ? lead > 14 && input.QuarterIndex > 2
                : lead > 8 && input.QuarterIndex > 1;
            bool good = true;
            if (canMiss)
            {
                if (!input.FreeKickDraw.HasValue)
                    return new SourcePatDecision(SourcePatDisposition.AwaitRandom, state, choice,
                        drawsConsumed: draws, randomRequest: RangeRequest(SourceRandomCall.FreePat, 100, draws));
                RequireDraw(input.FreeKickDraw.Value, 100);
                draws++;
                good = input.FreeKickDraw.Value >= (input.IsHumanOffense ? 2 : 3);
            }

            var next = new SourceRulesState(state.InitialOffenseSide, state.InitialOffenseSide,
                state.Down, state.LineOfScrimmage, state.FirstDown, SourcePlayType.Kickoff, state.NoFirstDown);
            return new SourcePatDecision(SourcePatDisposition.FreeKickComplete, next, choice,
                good ? 1 : 0, good, input.PackedTime == 0, draws, scoringSide: state.OffenseSide);
        }

        public static SourceKickoffPlan PrepareKickoff(SourceKickoffInput input)
        {
            if (input.State.PlayType != SourcePlayType.Kickoff)
                throw new ArgumentException("PrepareKickoff requires PT_KICKOFF.", nameof(input));
            SourcePlayStart start = BeginPlay(input.State, input.QuarterIndex, input.PackedTime, input.FirstKickoff);
            int kicker = start.State.OffenseSide;
            int side = kicker;
            int captain = input.Captain;
            SourceGameFlags flags = start.Flags;
            int los;
            SourceKickoffKind kind;
            if (input.FirstKickoff)
            {
                los = 30;
                kind = SourceKickoffKind.Opening;
            }
            else
            {
                if (side != input.FirstPossessionSide)
                    captain = 1 - captain;
                if (input.RequestedOnsideKick && !input.HalfKickoff)
                {
                    los = 30;
                    kind = SourceKickoffKind.Onside;
                    flags |= SourceGameFlags.OnsideKick;
                }
                else
                {
                    side = 1 - side;
                    los = 40;
                    kind = SourceKickoffKind.Quick;
                    flags |= SourceGameFlags.QuickKick;
                }
            }

            var state = new SourceRulesState(side, side, input.State.Down, los, 100,
                SourcePlayType.Kickoff, input.State.NoFirstDown);
            return new SourceKickoffPlan(state, kind, flags, kicker, captain,
                input.FirstKickoff ? SourceClock.InitialKickoffTicks : SourceClock.LaterKickoffTicks);
        }

        public static SourceRandomDecision CpuOnside(SourceCpuOnsideInput input)
        {
            if (!input.SurpriseRand.HasValue)
                return new SourceRandomDecision(false, false, 0,
                    new SourceRandomRequest(SourceRandomKind.CStandardRand,
                        SourceRandomCall.CpuOnsideSurprise, 33, 0));
            RequireRawRand(input.SurpriseRand.Value);
            if (input.SurpriseRand.Value % 33 == 0)
                return new SourceRandomDecision(true, true, 1);
            if (!input.SuppressRand.HasValue)
                return new SourceRandomDecision(false, false, 1,
                    new SourceRandomRequest(SourceRandomKind.CStandardRand,
                        SourceRandomCall.CpuOnsideSuppress, 5, 1));
            RequireRawRand(input.SuppressRand.Value);
            if (input.SuppressRand.Value % 5 == 0 || input.QuarterIndex < 3)
                return new SourceRandomDecision(true, false, 2);
            int deficit = input.ReceivingScore - input.KickingScore;
            return new SourceRandomDecision(true,
                (input.PackedTime < 0x3c00 && deficit > 0) || deficit > 7, 2);
        }

        public static SourcePeriodDecision AdvancePeriod(SourcePeriodInput input)
        {
            SourceRulesState state = input.State;
            if (input.PackedTime != 0)
                return new SourcePeriodDecision(input, SourcePeriodDisposition.NotExpired, state);
            if (!input.CurrentPlayFinished)
                return new SourcePeriodDecision(input, SourcePeriodDisposition.FinishCurrentPlay, state);
            if (state.PlayType == SourcePlayType.ExtraPoint)
                return new SourcePeriodDecision(input, SourcePeriodDisposition.DeferredForExtraPoint, state);
            if (input.IsAttractMode)
                return new SourcePeriodDecision(input, SourcePeriodDisposition.AttractMode, state);

            if (input.QuarterIndex >= 3
                && (input.Side0Score != input.Side1Score || input.QuarterIndex == 6))
            {
                // This is the gameplay terminal boundary. game_over's cabinet RAM
                // reset, records/free-game screens and post-game RNG are not run.
                return new SourcePeriodDecision(input, SourcePeriodDisposition.MatchEnded,
                    state, matchEnded: true);
            }
            if (!input.ContinueAfterCabinetGate)
                return new SourcePeriodDecision(input, SourcePeriodDisposition.CabinetContinuationDeclined,
                    state, matchEnded: true, cabinetGateRequired: true);

            if (input.QuarterIndex == 1)
            {
                var halfState = new SourceRulesState(1 - input.FirstPossessionSide,
                    state.InitialOffenseSide, state.Down, state.LineOfScrimmage, state.FirstDown,
                    SourcePlayType.Kickoff, state.NoFirstDown);
                return new SourcePeriodDecision(input, SourcePeriodDisposition.HalftimeKickoff,
                    halfState, true, true, true, cabinetGateRequired: true);
            }
            if (input.QuarterIndex == 0 || input.QuarterIndex == 2)
                return new SourcePeriodDecision(input, SourcePeriodDisposition.NextQuarter,
                    state, true, true, cabinetGateRequired: true);

            int ot = input.OvertimeKickoff;
            int draws = 0;
            if (ot == -1)
            {
                if (!input.OvertimeFirstDraw.HasValue)
                    return new SourcePeriodDecision(input, SourcePeriodDisposition.AwaitOvertimeDraw,
                        state, cabinetGateRequired: true,
                        randomRequest: RangeRequest(SourceRandomCall.OvertimeKickoff, 2, 0));
                RequireDraw(input.OvertimeFirstDraw.Value, 2);
                ot = input.OvertimeFirstDraw.Value;
                draws = 1;
            }
            int nextSide = 1 - ot;
            var overtimeState = new SourceRulesState(nextSide, state.InitialOffenseSide,
                state.Down, state.LineOfScrimmage, state.FirstDown, SourcePlayType.Kickoff, state.NoFirstDown);
            return new SourcePeriodDecision(input, SourcePeriodDisposition.OvertimeKickoff,
                overtimeState, true, true, cabinetGateRequired: true, overtimeKickoff: nextSide,
                drawsConsumed: draws);
        }

        public static bool ToggleCaptainAfterResult(SourcePlayResolution resolution, SourcePlayType lastPlayType,
            int firstPossessionSide)
        {
            SourceMatchRules.RequireSide(firstPossessionSide);
            return resolution.State.PlayType != SourcePlayType.Kickoff
                && lastPlayType != SourcePlayType.Kickoff
                && resolution.State.OffenseSide != resolution.State.InitialOffenseSide
                && resolution.State.OffenseSide == firstPossessionSide;
        }

        public static int CpuPlayPoolSize(SourceCpuPlayFamily family)
        {
            // playsel.c 314-582; indices refer to the original weighted arrays,
            // not to deduplicated play IDs. The playbook adapter resolves the index.
            switch (family)
            {
                case SourceCpuPlayFamily.ShortOffense: return 52;
                case SourceCpuPlayFamily.LongOffense: return 53;
                case SourceCpuPlayFamily.ShortDefense: return 20;
                case SourceCpuPlayFamily.LongDefense: return 26;
                case SourceCpuPlayFamily.ShortDefenseCustom: return 27;
                case SourceCpuPlayFamily.LongDefenseCustom: return 29;
                case SourceCpuPlayFamily.BlitzDefense: return 19;
                default: return 0;
            }
        }

        // playsel.c:cpu_pick_play, 2506-2705. No source RNG implementation is
        // invented: callers supply each draw and resolve weighted pool identities.
        public static SourceCpuPlayDecision CpuPickPlay(SourceCpuPlayInput input)
        {
            SourceRulesState state = input.State;
            int ytg = state.FirstDown - state.LineOfScrimmage;
            int deficit = input.OpponentScore - input.SelectingScore;
            bool range = state.LineOfScrimmage >= 50;
            bool onePlay = input.PackedTime < 0x0800;
            bool offense = state.OffenseSide == input.SelectingSide;
            int draws = 0;
            SourceCpuPlayFamily family;
            SourceRandomRequest request;
            int draw;

            if (offense)
            {
                if (state.PlayType == SourcePlayType.ExtraPoint)
                    family = SourceCpuPlayFamily.ShortOffense;
                else if (state.Down == 4)
                {
                    int periodsLeft = Math.Max(0, 3 - input.QuarterIndex);
                    if (input.PackedTime >= 0x10000)
                        periodsLeft++;
                    if (input.QuarterIndex == 0 || input.QuarterIndex == 2)
                    {
                        if (deficit > 3 && ytg < 5)
                            family = SourceCpuPlayFamily.ShortOffense;
                        else if (deficit > periodsLeft * 7 && state.LineOfScrimmage > 40)
                            family = SourceCpuPlayFamily.LongOffense;
                        else
                            family = range || input.SuperFieldGoal ? SourceCpuPlayFamily.FieldGoal : SourceCpuPlayFamily.Punt;
                    }
                    else if (input.QuarterIndex == 1)
                        family = range || input.SuperFieldGoal ? SourceCpuPlayFamily.FieldGoal : SourceCpuPlayFamily.Punt;
                    else if (range)
                        family = deficit <= 3 ? SourceCpuPlayFamily.FieldGoal
                            : onePlay || ytg > 10 ? SourceCpuPlayFamily.LongOffense : SourceCpuPlayFamily.ShortOffense;
                    else if (deficit <= 0 && !input.SuperFieldGoal)
                        family = SourceCpuPlayFamily.Punt;
                    else
                        family = onePlay || ytg > 10 ? SourceCpuPlayFamily.LongOffense : SourceCpuPlayFamily.ShortOffense;
                }
                else if ((input.QuarterIndex >= 3 || input.QuarterIndex == 1) && onePlay)
                    family = range && deficit <= 3 ? SourceCpuPlayFamily.FieldGoal
                        : state.LineOfScrimmage < 93 ? SourceCpuPlayFamily.LongOffense : SourceCpuPlayFamily.ShortOffense;
                else
                    family = state.Down >= 1 && state.Down <= 3
                        ? SourceCpuPlayFamily.ShortOffense : SourceCpuPlayFamily.LongOffense;

                if (family == SourceCpuPlayFamily.Punt)
                {
                    if (ytg <= 25)
                    {
                        if (!TryDraw(input.Draws, draws, 10, SourceRandomCall.CpuFakePunt, out draw, out request))
                            return new SourceCpuPlayDecision(false, family, drawsConsumed: draws, randomRequest: request);
                        draws++;
                        if (draw == 0)
                            family = SourceCpuPlayFamily.FakePunt;
                    }
                    return new SourceCpuPlayDecision(true, family, drawsConsumed: draws);
                }
            }
            else
            {
                if (input.OpponentPlay == SourceOpponentPlay.Punt)
                    return new SourceCpuPlayDecision(true, SourceCpuPlayFamily.PuntReturn);
                if (input.OpponentPlay == SourceOpponentPlay.FieldGoal)
                    return new SourceCpuPlayDecision(true, SourceCpuPlayFamily.BlockFieldGoal);
                if (state.LineOfScrimmage >= 90)
                {
                    if (!TryDraw(input.Draws, draws, 100, SourceRandomCall.CpuDefenseGoalLine, out draw, out request))
                        return new SourceCpuPlayDecision(false, SourceCpuPlayFamily.GoalLineDefense,
                            drawsConsumed: draws, randomRequest: request);
                    draws++;
                    if (draw < 80)
                    {
                        if (!TryDraw(input.Draws, draws, 2, SourceRandomCall.CpuDefenseGoalLineKind, out draw, out request))
                            return new SourceCpuPlayDecision(false, SourceCpuPlayFamily.GoalLineDefense,
                                drawsConsumed: draws, randomRequest: request);
                        draws++;
                        family = draw != 0 ? SourceCpuPlayFamily.GoalLineDefense : SourceCpuPlayFamily.ShortDefense;
                    }
                    else
                        family = SourceCpuPlayFamily.BlitzDefense;
                }
                else if (state.LineOfScrimmage >= 80)
                {
                    if (!TryDraw(input.Draws, draws, 100, SourceRandomCall.CpuDefenseNearGoal, out draw, out request))
                        return new SourceCpuPlayDecision(false, SourceCpuPlayFamily.ShortDefense,
                            drawsConsumed: draws, randomRequest: request);
                    draws++;
                    family = draw < 75 ? SourceCpuPlayFamily.ShortDefense : SourceCpuPlayFamily.BlitzDefense;
                }
                else
                    family = ytg >= 10 * (5 - state.Down) ? SourceCpuPlayFamily.LongDefense : SourceCpuPlayFamily.ShortDefense;

                if (input.OpponentPlay == SourceOpponentPlay.Custom)
                {
                    if (family == SourceCpuPlayFamily.ShortDefense)
                        family = SourceCpuPlayFamily.ShortDefenseCustom;
                    else if (family == SourceCpuPlayFamily.LongDefense)
                        family = SourceCpuPlayFamily.LongDefenseCustom;
                }
            }

            int size = CpuPlayPoolSize(family);
            if (size == 0)
                return new SourceCpuPlayDecision(true, family, drawsConsumed: draws);
            SourceRandomCall poolCall = offense ? SourceRandomCall.CpuOffensePool : SourceRandomCall.CpuDefensePool;
            if (!TryDraw(input.Draws, draws, size, poolCall, out draw, out request))
                return new SourceCpuPlayDecision(false, family, drawsConsumed: draws, randomRequest: request);
            int index = draw;
            draws++;
            if (offense && family == SourceCpuPlayFamily.ShortOffense && state.Down == 4)
            {
                if (!TryDraw(input.Draws, draws, 2, SourceRandomCall.CpuFourthDownFakePunt, out draw, out request))
                    return new SourceCpuPlayDecision(false, family, index, index, draws, request);
                draws++;
                if (draw != 0)
                    return new SourceCpuPlayDecision(true, SourceCpuPlayFamily.FakePunt,
                        preOverridePoolIndex: index, drawsConsumed: draws);
            }
            return new SourceCpuPlayDecision(true, family, index, index, draws);
        }

        private static bool TryDraw(SourceRandomDraws inputs, int index, int bound, SourceRandomCall call,
            out int value, out SourceRandomRequest request)
        {
            int? supplied = inputs.At(index);
            request = default(SourceRandomRequest);
            value = 0;
            if (!supplied.HasValue)
            {
                request = RangeRequest(call, bound, index);
                return false;
            }
            RequireDraw(supplied.Value, bound);
            value = supplied.Value;
            return true;
        }

        private static SourceRulesState WithPlayType(SourceRulesState state, SourcePlayType playType)
        {
            return new SourceRulesState(state.OffenseSide, state.InitialOffenseSide, state.Down,
                state.LineOfScrimmage, state.FirstDown, playType, state.NoFirstDown);
        }

        private static SourceRandomRequest RangeRequest(SourceRandomCall call, int bound, int index)
        {
            return new SourceRandomRequest(SourceRandomKind.RandRange, call, bound, index);
        }

        private static void RequireDraw(int value, int bound)
        {
            if (value < 0 || value >= bound)
                throw new ArgumentOutOfRangeException(nameof(value), "Supply the source randrng(bound) output.");
        }

        private static void RequireRawRand(int value)
        {
            if (value < 0)
                throw new ArgumentOutOfRangeException(nameof(value), "Supply the source C rand() output.");
        }
    }
}
