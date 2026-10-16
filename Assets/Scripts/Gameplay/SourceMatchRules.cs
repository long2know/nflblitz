using System;

namespace Blitz.Gameplay
{
    public enum SourcePlayType { Normal = 0, Kickoff = 1, FieldGoal = 2, Punt = 3, ExtraPoint = 4 }
    public enum SourceBallType { Pass = 0, Fumble = 1, Punt = 2, Kickoff = 3, FieldGoal = 4, Bobble = 5, Lateral = 6 }
    public enum SourcePlayEndCause
    {
        CarrierOutOfBounds = 0, CarrierTackledInBounds = 1, CarrierDownInOwnEndZone = 2,
        CarrierTouchdown = 3, DeadBallInBounds = 4, BallOutOfBounds = 5
    }
    public enum SourcePlayResult
    {
        Down = 0, Touchdown = 1, Safety = 2, PatSafety = 3, NewPossession = 4,
        Incomplete = 5, FumbledOutOfBounds = 6, FieldGoal = 7, ExtraPoint = 8,
        MissedKick = 9, FailedConversion = 10, Conversion = 11, PuntOutOfBounds = 12,
        DownedPunt = 13, KickoffOutOfBounds = 14, Touchback = 15
    }
    [Flags]
    public enum SourceGameFlags
    {
        None = 0, RecoveredInEndZone = 0x1, LeftEndZone = 0x2, ThroughUprights = 0x4,
        PossessionChange = 0x8, Bobble = 0x10, FirstDownLineCrossed = 0x20,
        ForwardPass = 0x40, PassThrown = 0x80, BallPunted = 0x100, BallKicked = 0x200,
        PlayEnding = 0x400, Fumble = 0x800, QuickKick = 0x1000, AirCatch = 0x2000,
        BallCrossedLine = 0x4000, HikingBall = 0x8000, OnsideKick = 0x10000,
        OverLineSpeedsSet = 0x20000, OpeningKick = 0x40000
    }

    public enum SourceClassifierStatus
    {
        Classified,
        IntentionalNoOp,
        InvalidSourceBranch
    }

    public readonly struct SourceRulesState
    {
        public int OffenseSide { get; }
        public int InitialOffenseSide { get; }
        public int Down { get; }
        public int LineOfScrimmage { get; }
        public int FirstDown { get; }
        public SourcePlayType PlayType { get; }
        public bool NoFirstDown { get; }

        public SourceRulesState(int offenseSide, int initialOffenseSide, int down,
            int lineOfScrimmage, int firstDown, SourcePlayType playType = SourcePlayType.Normal,
            bool noFirstDown = false)
        {
            SourceMatchRules.RequireSide(offenseSide);
            SourceMatchRules.RequireSide(initialOffenseSide);
            OffenseSide = offenseSide;
            InitialOffenseSide = initialOffenseSide;
            Down = down;
            LineOfScrimmage = lineOfScrimmage;
            FirstDown = firstDown;
            PlayType = playType;
            NoFirstDown = noFirstDown;
        }
    }

    public readonly struct SourceResultInput
    {
        public SourcePlayType PlayType { get; }
        public SourcePlayEndCause Cause { get; }
        public SourceBallType BallType { get; }
        public SourceGameFlags Flags { get; }
        public int FieldEnd { get; }
        public int OffenseSide { get; }
        public float PlayEndX { get; }
        public float FumbleX { get; }
        public SourcePlayResult PreviousResult { get; }

        public SourceResultInput(SourcePlayType playType, SourcePlayEndCause cause,
            SourceBallType ballType, SourceGameFlags flags, int fieldEnd, int offenseSide,
            float playEndX = 0, float fumbleX = 0, SourcePlayResult previousResult = SourcePlayResult.Down)
        {
            SourceMatchRules.RequireSide(offenseSide);
            PlayType = playType;
            Cause = cause;
            BallType = ballType;
            Flags = flags;
            FieldEnd = fieldEnd;
            OffenseSide = offenseSide;
            PlayEndX = playEndX;
            FumbleX = fumbleX;
            PreviousResult = previousResult;
        }
    }

    public readonly struct SourceResultClassification
    {
        public SourceClassifierStatus Status { get; }
        public SourcePlayResult Result { get; }
        public int OffenseSide { get; }
        public float PlayEndX { get; }

        internal SourceResultClassification(SourceClassifierStatus status, SourcePlayResult result,
            int offenseSide, float playEndX)
        {
            Status = status;
            Result = result;
            OffenseSide = offenseSide;
            PlayEndX = playEndX;
        }
    }

    public readonly struct SourcePlayEndInput
    {
        public SourceRulesState State { get; }
        public SourcePlayEndCause Cause { get; }
        public SourceBallType BallType { get; }
        public SourceGameFlags Flags { get; }
        public int LastPossession { get; }
        public float PlayEndX { get; }
        public float PlayEndZ { get; }
        public float FumbleX { get; }
        public SourcePlayResult PreviousResult { get; }

        public SourcePlayEndInput(SourceRulesState state, SourcePlayEndCause cause,
            SourceBallType ballType, SourceGameFlags flags, int lastPossession,
            float playEndX, float playEndZ = 0, float fumbleX = 0,
            SourcePlayResult previousResult = SourcePlayResult.Down)
        {
            SourceMatchRules.RequireSide(lastPossession);
            State = state;
            Cause = cause;
            BallType = ballType;
            Flags = flags;
            LastPossession = lastPossession;
            PlayEndX = playEndX;
            PlayEndZ = playEndZ;
            FumbleX = fumbleX;
            PreviousResult = previousResult;
        }
    }

    public readonly struct SourcePlayResolution
    {
        public SourceRulesState State { get; }
        public SourceResultClassification Classification { get; }
        public SourceGameFlags Flags { get; }
        public int UnclampedFieldEnd { get; }
        public int FieldEnd { get; }
        public float RoundedPlayEndX { get; }
        public float PlayEndX { get; }
        public float LinePositionZ { get; }
        public int Side0Points { get; }
        public int Side1Points { get; }
        public bool JustGotFirstDown { get; }
        public bool TurnoverOnDowns { get; }
        public bool PreserveDisplayedLines { get; }
        public int DisplayLineOfScrimmage { get; }
        public int DisplayFirstDown { get; }
        public bool ResultSwitchHasCase { get; }
        public bool TransitionApplied { get { return Classification.Status != SourceClassifierStatus.InvalidSourceBranch; } }

        internal SourcePlayResolution(SourceRulesState state, SourceResultClassification classification,
            SourceGameFlags flags, int unclampedFieldEnd, int fieldEnd, float roundedPlayEndX,
            float playEndX, float linePositionZ, int side0Points, int side1Points,
            bool justGotFirstDown, bool turnoverOnDowns, bool resultSwitchHasCase)
        {
            State = state;
            Classification = classification;
            Flags = flags;
            UnclampedFieldEnd = unclampedFieldEnd;
            FieldEnd = fieldEnd;
            RoundedPlayEndX = roundedPlayEndX;
            PlayEndX = playEndX;
            LinePositionZ = linePositionZ;
            Side0Points = side0Points;
            Side1Points = side1Points;
            JustGotFirstDown = justGotFirstDown;
            TurnoverOnDowns = turnoverOnDowns;
            PreserveDisplayedLines = turnoverOnDowns;
            bool sameSide = state.OffenseSide == state.InitialOffenseSide;
            DisplayLineOfScrimmage = sameSide ? state.LineOfScrimmage : 100 - state.LineOfScrimmage;
            DisplayFirstDown = sameSide ? state.FirstDown : 100 - state.FirstDown;
            ResultSwitchHasCase = resultSwitchHasCase;
        }
    }

    // result.c eaf1201d0531a9d06d8ce0b1e9e75463062e2923; nfl.c
    // 1387-1443, 1626-1891, 1896-1910, 1960-1963 (15b49474...).
    public static class SourceMatchRules
    {
        public const float SourceFieldHalfLength = 390f;
        public const float SourceUnitsPerYard = 7.8f;
        public const float SourceHashWidth = 24.05f;
        public const int FirstDownYards = 30;

        // FIELDX uses (FIELDHZ / 300.0f) * 6.0f, NOT the rounded literal 7.8f.
        // The binary32 difference matters at integer-yard boundaries.
        private const float FieldMacroScale = (SourceFieldHalfLength / 300.0f) * 6.0f;

        public static float RoundPlayEndPosition(float sourceWorldX)
        {
            RequireFinite(sourceWorldX);
            // Both divisions here are integer operations, truncating toward zero.
            return (unchecked((int)sourceWorldX * 10) / 78) * 7.8f;
        }

        public static int FieldPosition(float sourceWorldX, int offenseSide)
        {
            RequireSide(offenseSide);
            RequireFinite(sourceWorldX);
            float directed = offenseSide == 0 ? sourceWorldX : -1.0f * sourceWorldX;
            float numerator = SourceFieldHalfLength + directed;
            return (int)(numerator / FieldMacroScale);
        }

        public static float WorldPosition(int fieldYards, int offenseSide)
        {
            RequireSide(offenseSide);
            return FieldMacroScale * (float)(offenseSide == 0 ? fieldYards : 100 - fieldYards)
                - SourceFieldHalfLength;
        }

        public static float SourceWorldFromAbsoluteYards(float absoluteYards)
        {
            RequireFinite(absoluteYards);
            return (absoluteYards - 50f) * SourceUnitsPerYard;
        }

        public static float AbsoluteYardsFromSourceWorld(float sourceWorldX)
        {
            RequireFinite(sourceWorldX);
            return sourceWorldX / SourceUnitsPerYard + 50f;
        }

        public static SourceResultClassification Classify(SourceResultInput input)
        {
            SourcePlayResult result = input.PreviousResult;
            int side = input.OffenseSide;
            float endX = input.PlayEndX;
            if ((int)input.Cause < 0 || (int)input.Cause > 5
                || (int)input.PlayType < 0 || (int)input.PlayType > 4)
                return new SourceResultClassification(SourceClassifierStatus.InvalidSourceBranch, result, side, endX);

            if (input.PlayType == SourcePlayType.FieldGoal || input.PlayType == SourcePlayType.Punt)
                return new SourceResultClassification(SourceClassifierStatus.IntentionalNoOp, result, side, endX);

            bool changed = Has(input.Flags, SourceGameFlags.PossessionChange);
            bool recoveredAndStayed = Has(input.Flags, SourceGameFlags.RecoveredInEndZone)
                && !Has(input.Flags, SourceGameFlags.LeftEndZone);
            bool kickoff = input.PlayType == SourcePlayType.Kickoff;
            bool pat = input.PlayType == SourcePlayType.ExtraPoint;
            switch (input.Cause)
            {
                case SourcePlayEndCause.CarrierOutOfBounds:
                case SourcePlayEndCause.CarrierTackledInBounds:
                    result = pat ? SourcePlayResult.FailedConversion
                        : kickoff || changed ? SourcePlayResult.NewPossession : SourcePlayResult.Down;
                    break;
                case SourcePlayEndCause.CarrierDownInOwnEndZone:
                    bool touchback = recoveredAndStayed && (kickoff || changed);
                    result = pat
                        ? touchback ? SourcePlayResult.FailedConversion : SourcePlayResult.PatSafety
                        : touchback ? SourcePlayResult.Touchback : SourcePlayResult.Safety;
                    break;
                case SourcePlayEndCause.CarrierTouchdown:
                    result = pat ? SourcePlayResult.Conversion : SourcePlayResult.Touchdown;
                    break;
                case SourcePlayEndCause.DeadBallInBounds:
                    if (pat)
                        result = SourcePlayResult.FailedConversion;
                    else if (kickoff)
                        result = SourcePlayResult.NewPossession;
                    else if (input.BallType == SourceBallType.Punt)
                        result = input.FieldEnd <= 0 ? SourcePlayResult.Safety : SourcePlayResult.DownedPunt;
                    else if (input.BallType == SourceBallType.FieldGoal)
                        result = Has(input.Flags, SourceGameFlags.ThroughUprights)
                            ? SourcePlayResult.FieldGoal : SourcePlayResult.MissedKick;
                    else
                        result = SourcePlayResult.Incomplete;
                    break;
                case SourcePlayEndCause.BallOutOfBounds:
                    if (pat)
                    {
                        result = input.FieldEnd <= 0 && IsLoose(input.BallType)
                            ? SourcePlayResult.PatSafety : SourcePlayResult.FailedConversion;
                    }
                    else if (kickoff && input.BallType == SourceBallType.Kickoff)
                        result = SourcePlayResult.KickoffOutOfBounds;
                    else if (!kickoff && (input.BallType == SourceBallType.Pass || input.BallType == SourceBallType.Bobble))
                        result = SourcePlayResult.Incomplete;
                    else if (IsLoose(input.BallType))
                    {
                        if (input.FieldEnd <= 0)
                            result = changed && recoveredAndStayed ? SourcePlayResult.Touchback : SourcePlayResult.Safety;
                        else if (input.FieldEnd >= 100)
                        {
                            side = 1 - side;
                            result = SourcePlayResult.Touchback;
                        }
                        else
                        {
                            result = kickoff || changed ? SourcePlayResult.NewPossession : SourcePlayResult.FumbledOutOfBounds;
                            // The original writes play_end_pos but does NOT recalculate fpend.
                            if (FieldPosition(input.FumbleX, side) < input.FieldEnd)
                                endX = input.FumbleX;
                        }
                    }
                    else if (!kickoff && input.BallType == SourceBallType.Punt)
                    {
                        if (input.FieldEnd >= 100)
                        {
                            side = 1 - side;
                            result = SourcePlayResult.Touchback;
                        }
                        else
                            result = SourcePlayResult.PuntOutOfBounds;
                    }
                    else if (!kickoff && input.BallType == SourceBallType.FieldGoal)
                        result = SourcePlayResult.MissedKick;
                    else
                        return new SourceResultClassification(SourceClassifierStatus.InvalidSourceBranch, result, side, endX);
                    break;
            }
            return new SourceResultClassification(SourceClassifierStatus.Classified, result, side, endX);
        }

        public static SourcePlayResolution Resolve(SourcePlayEndInput input)
        {
            SourceRulesState before = input.State;
            int side = input.LastPossession;
            SourceGameFlags flags = input.Flags;
            if (side != before.InitialOffenseSide)
                flags |= SourceGameFlags.PossessionChange;

            float rounded = RoundPlayEndPosition(input.PlayEndX);
            int rawEnd = FieldPosition(rounded, side);
            var classification = Classify(new SourceResultInput(before.PlayType, input.Cause,
                input.BallType, flags, rawEnd, side, rounded, input.FumbleX, input.PreviousResult));
            if (classification.Status == SourceClassifierStatus.InvalidSourceBranch)
                return new SourcePlayResolution(before, classification, flags, rawEnd, rawEnd,
                    rounded, rounded, input.PlayEndZ, 0, 0, false, false, false);

            side = classification.OffenseSide;
            SourcePlayResult result = classification.Result;
            int end = result == SourcePlayResult.Touchdown || result == SourcePlayResult.Conversion
                ? 100 : Math.Max(1, Math.Min(99, rawEnd));
            int down = before.Down;
            int los = before.LineOfScrimmage;
            int first = before.FirstDown;
            int scoreSide = side;
            int points = 0;
            bool firstDown = false;
            bool turnover = false;
            bool hasCase = true;
            float playEndX = classification.PlayEndX;
            SourcePlayType next = SourcePlayType.Normal;

            switch (result)
            {
                case SourcePlayResult.Down:
                case SourcePlayResult.FumbledOutOfBounds:
                    if (end >= first)
                    {
                        down = 1;
                        los = end;
                        first = FirstDownTarget(los, before.NoFirstDown);
                        firstDown = true;
                    }
                    else if (down == 4)
                    {
                        playEndX = WorldPosition(end, side);
                        side = 1 - side;
                        down = 1;
                        turnover = true;
                        // Reapply FIELDX after changing sides; 100-end is NOT
                        // numerically identical at every binary32 boundary.
                        los = FieldPosition(playEndX, side);
                        first = FirstDownTarget(los, before.NoFirstDown);
                    }
                    else
                    {
                        los = end;
                        down++;
                    }
                    break;
                case SourcePlayResult.Incomplete:
                    side = before.InitialOffenseSide;
                    down++;
                    if (down == 5)
                    {
                        side = 1 - side;
                        down = 1;
                        turnover = true;
                        los = 100 - los;
                        first = FirstDownTarget(los, before.NoFirstDown);
                    }
                    break;
                case SourcePlayResult.Touchback:
                    down = 1;
                    los = 20;
                    first = FirstDownTarget(los, before.NoFirstDown);
                    break;
                case SourcePlayResult.PatSafety:
                    scoreSide = 1 - side;
                    points = 1;
                    side = before.InitialOffenseSide;
                    next = SourcePlayType.Kickoff;
                    break;
                case SourcePlayResult.Safety:
                    scoreSide = 1 - side;
                    points = 2;
                    next = SourcePlayType.Kickoff;
                    break;
                case SourcePlayResult.NewPossession:
                    down = 1;
                    los = end;
                    first = FirstDownTarget(los, before.NoFirstDown);
                    break;
                case SourcePlayResult.Touchdown:
                    down = -1;
                    points = 6;
                    next = SourcePlayType.ExtraPoint;
                    los = 90;
                    first = 100;
                    break;
                case SourcePlayResult.FailedConversion:
                    side = before.InitialOffenseSide;
                    next = SourcePlayType.Kickoff;
                    break;
                case SourcePlayResult.Conversion:
                    points = 2;
                    side = before.InitialOffenseSide;
                    next = SourcePlayType.Kickoff;
                    break;
                case SourcePlayResult.FieldGoal:
                    points = 3;
                    side = before.InitialOffenseSide;
                    next = SourcePlayType.Kickoff;
                    break;
                case SourcePlayResult.MissedKick:
                    side = 1 - side;
                    los = 100 - los;
                    down = 1;
                    first = FirstDownTarget(los, before.NoFirstDown);
                    break;
                case SourcePlayResult.PuntOutOfBounds:
                case SourcePlayResult.DownedPunt:
                    side = 1 - before.InitialOffenseSide;
                    down = 1;
                    los = result == SourcePlayResult.DownedPunt && end >= 99 ? 20 : 100 - end;
                    first = FirstDownTarget(los, before.NoFirstDown);
                    break;
                case SourcePlayResult.KickoffOutOfBounds:
                    side = 1 - before.InitialOffenseSide;
                    down = 1;
                    los = 40;
                    first = FirstDownTarget(los, before.NoFirstDown);
                    break;
                default:
                    // PR_EXTRA_POINT has no game_proc result-switch case.
                    hasCase = false;
                    break;
            }

            float z = input.PlayEndZ;
            RequireFinite(z);
            // HASHWIDTH is an unsuffixed (double) constant in GAME.H.
            if (Math.Abs(z) > 24.05)
                z = (float)((z / Math.Abs(z)) * 24.05);
            var after = new SourceRulesState(side, before.InitialOffenseSide, down, los, first,
                next, before.NoFirstDown);
            return new SourcePlayResolution(after, classification, flags, rawEnd, end, rounded,
                playEndX, z, scoreSide == 0 ? points : 0, scoreSide == 1 ? points : 0,
                firstDown, turnover, hasCase);
        }

        public static int FirstDownTarget(int lineOfScrimmage, bool noFirstDown = false)
        {
            return noFirstDown ? 100 : Math.Min(100, lineOfScrimmage + FirstDownYards);
        }

        internal static void RequireSide(int side)
        {
            if (side != 0 && side != 1)
                throw new ArgumentOutOfRangeException(nameof(side), "Source side must be 0 or 1.");
        }

        private static bool Has(SourceGameFlags flags, SourceGameFlags bit) { return (flags & bit) != 0; }
        private static bool IsLoose(SourceBallType type) { return type == SourceBallType.Fumble || type == SourceBallType.Lateral; }
        private static void RequireFinite(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
                throw new ArgumentOutOfRangeException(nameof(value));
        }
    }
}
