using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Blitz.Contracts;
using Blitz.Gameplay;
using Blitz.Presentation;
using Blitz.Runtime;
using Blitz.SourcePlayer;

internal static class MatchStatisticsChecks
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private static int assertions;
    private static PlayerHost host;

    // These are declared gameplay-boundary fixtures using the real admitted library, actors,
    // camera, scheduler and C2 commit. They are not a naturally played full-match proof.
    private static object Model { get { return typeof(GameSimulation).GetProperty("Model", PrivateInstance).GetValue(host.Game); } }
    private static object Actor(int id) { return ((Array)Get(Model, "Players")).GetValue(id); }
    private static SourceMatchView Match { get { return ((ISourceMatchState)host.Game.State).Match; } }
    private static SourceCabinetFlowView Flow { get { return ((ISourceCabinetFlowState)host.Game.State).CabinetFlow; } }
    private static object Get(object value, string name) { return value.GetType().GetField(name, PrivateInstance).GetValue(value); }
    private static void Set(object value, string name, object field) { value.GetType().GetField(name, PrivateInstance).SetValue(value, field); }
    private static void SetEnum(object value, string name, string member)
    {
        FieldInfo field = value.GetType().GetField(name, PrivateInstance);
        field.SetValue(value, Enum.Parse(field.FieldType, member));
    }
    private static int[] Counts(string name) { return (int[])Get(Model, name); }

    private static object Call(string method, params object[] arguments)
    {
        return Invoke(typeof(GameSimulation).GetMethods(PrivateInstance)
            .Single(m => m.Name == method && m.GetParameters().Length == arguments.Length), host.Game, arguments);
    }

    private static object Invoke(MethodInfo method, object target, object[] arguments)
    {
        try { return method.Invoke(target, arguments); }
        catch (TargetInvocationException error) { throw error.InnerException; }
    }

    private static object Static(string type, string method, params object[] arguments)
    {
        return Invoke(typeof(GameSimulation).Assembly.GetType("Blitz.Gameplay." + type)
            .GetMethod(method, BindingFlags.Static | BindingFlags.NonPublic), null, arguments);
    }

    private static void Check(bool condition, string message)
    {
        assertions++;
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void Publish() { Call("Publish"); }
    private static void Render(int steps = 0)
    {
        host.Advance(FixedStepBridge.StepSeconds * steps, 512, 384, 512, 384, 1);
    }

    private static void Raw(int home = 0, int away = 0)
    {
        host.Capture(new double[8], new[] { home, 0, away, 0 });
    }

    private static void Wake(int held = 0)
    {
        long before = host.Game.SourceTick;
        Raw(held, held);
        for (int tick = 0; tick < 3 && host.Game.SourceTick == before; tick++) Render(1);
        Check(host.Game.SourceTick == before + 1, "Fixture must execute exactly one genuine source wake.");
    }

    private static void Tap(int button, bool both = false)
    {
        Raw(); Raw(button, both ? button : 0); Raw(); Render();
    }

    private static void Start(PlayerData data)
    {
        host = new PlayerHost(data, new StatisticsSink(), options: PreviewOptions.FromUrl(
            "?source-gameflow=portable", data.Catalog.Profile.StableId));
        Raw(); Raw(0, 1); Raw();
        Tap(4);
        Tap(1, true); Tap(1, true); Tap(1);
        Check(host.Session.Playing && host.Game.SourceTick == 0
            && host.MatchConfiguration.Stations.Any(s => s.StationId == 2),
            "Real cabinet choices must create both human sides before the first source wake.");
        Check(Match.PlayActivityStats.HomeOffensivePlays == 0 && Match.PlayActivityStats.AwayOffensivePlays == 1,
            "The genuine first-kickoff begin-play counter belongs to the receiving side.");
    }

    private static void Position(int actor, int side, float yards, float z = 0f)
    {
        Static("SourcePlayerDriver", "SetSourcePosition", Actor(actor),
            new SourceVector3(SourceMatchRules.WorldPosition((int)yards, side)
                + (yards - (int)yards) * SourceTiming.SourceUnitsPerYard * (side == 0 ? 1f : -1f), 0f, z));
    }

    private static void Prepare(int side, SourcePlayType kind = SourcePlayType.Normal, int los = 50,
        int? initial = null, bool reset = true)
    {
        if (reset) host.Bridge.Restart();
        if (host.Game.SourceTick == 0) { Wake(); Wake(); }
        Set(Model, "SourceRules", new SourceRulesState(side, initial ?? side, 2, los, 90, kind));
        Set(Model, "Possession", (TeamId)side);
        Set(Model, "SourceLastPossession", side);
        Set(Model, "SourceFlags", SourceGameFlags.None);
        Set(Model, "SourceLastResult", SourcePlayResult.Down);
        Set(Model, "SourceOffensePlay", host.Data.Playbook.GetPlay("play_zigzag"));
        Set(Model, "SourceKickApproach", false);
        Set(Model, "SourceDisallowPassTicks", 0);
        Set(Model, "SourceC2PendingResult", null);
        Set(Model, "SourcePendingTackleAttacker", -1);
        Set(Model, "SourcePendingTackleVictim", -1);
        Set(Model, "SourceHasContactSpot", false);
        Set(Model, "Phase", GamePhase.LiveBall);
        Set(Model, "SourceMatchPhase", SourceMatchPhase.LiveBall);
        Set(Model, "ClockActive", false);
        Set(Model, "LineOfScrimmage", (float)los);
        Set(Model, "FirstDownLine", 90f);
        Set(Model, "CarrierId", side * 7 + 3);
        Set(Model, "BallMode", BallMode.Held);
        Set(Model, "BallKind", SourceBallKind.Pass);
        SetEnum(Model, "SourceBallFlags", "0");
        Set(Model, "CatchSourceTick", host.Game.SourceTick - 30);
        Set(Model, "SourceNoFumbles", true);
        Set(Model, "HomeScore", 0);
        Set(Model, "AwayScore", 0);
        foreach (object actor in (Array)Get(Model, "Players"))
        {
            Set(actor, "SourceMode", SourceActorMode.Dead);
            Set(actor, "SourceAttackMode", 0);
            Set(actor, "SourceAttackTime", 0);
            Set(actor, "SourceControllerStation", -1);
            Set(actor, "SourceThrowing", false);
            Set(actor, "PendingThrowTarget", -1);
            Set(actor, "SourceVelocity", default(FieldVector));
            Set(actor, "SourceVerticalVelocity", 0f);
        }
        for (int id = 0; id < 14; id++) Position(id, 0, 15 + id * 4, id % 2 == 0 ? 80f : -80f);
        Position(side * 7 + 3, side, los - 10);
        Position(side * 7 + 4, side, los + 10);
        Set(Actor(side * 7 + 3), "Role", ActorRole.Quarterback);
        Set(Actor(side * 7 + 4), "Role", ActorRole.Receiver);
        Set(Actor(4), "SourceControllerStation", 0);
        Set(Actor(11), "SourceControllerStation", 2);
        Counts("SourceStationPlayers")[0] = 4;
        Counts("SourceStationPlayers")[2] = 11;
        Set(Model, "ControlledPlayerId", 4);
        Set(Model, "SourceBallPosition", new SourceVector3(SourceMatchRules.WorldPosition(los - 10, side), 12f, 0f));
        Call("CacheSourceGeometry");
        Publish();
    }

    private static int Attempts(int side)
    {
        return side == 0 ? Match.PlayActivityStats.HomePassAttempts : Match.PlayActivityStats.AwayPassAttempts;
    }

    private static void QueueEnd(SourcePlayEndCause cause, int side, int field)
    {
        Set(Model, "SourcePlayEndX", SourceMatchRules.WorldPosition(field, side));
        Set(Model, "SourcePlayEndZ", 0f);
        Call("EndSourcePlay", cause, (int)Get(Model, "CarrierId"), new FieldVector(field, 0f), -1, null);
        Publish();
        Check(host.Game.C2State.ResultPending, "The real C2 result must be pending before game_proc commits.");
        Call("CommitSourceC2PlayEnd");
        Check(host.Game.C2State.ResultPending, "A pending result cannot commit on its producer source tick.");
    }

    private static void VerifyReleaseAndCommit()
    {
        Prepare(0);
        SourceMatchView before = Match;
        Check(before.PlayActivityStats.HomePassAttempts == 0 && before.PlayActivityStats.AwayPassAttempts == 0
            && before.PlayActivityStats.HomeFumbles == 0 && before.PlayActivityStats.AwayTackles == 0,
            "Actual source snapshots publish measured zero, not unavailable data.");
        Call("RequestSourcePass", 4, false);
        Check((int)Get(Actor(3), "PendingThrowTarget") == 4, "Fixture must enter the genuine throw animation.");
        Publish();
        Check(Attempts(0) == 0, "Pass intent is not an attempted pass.");
        long releaseTick = -1;
        for (int tick = 0; tick < 90 && releaseTick < 0; tick++)
        {
            Wake();
            if (host.Game.SourceEvents.Any(e => e.Type == SourceEventType.PassReleased)) releaseTick = host.Game.SourceTick;
        }
        Check(releaseTick > 0 && Attempts(0) == 1 && Attempts(1) == 0,
            "The actual animation callback must count the release exactly once for the offense.");
        Check(Match.PassCompletions.HomeCompletions == 0 && before.PlayActivityStats.HomePassAttempts == 0,
            "Release is not completion, and retained pre-release snapshots are immutable.");
        Call("ReleaseSourcePass", 3);
        Publish();
        Check(Attempts(0) == 1, "A repeated callback after the carrier cleared cannot count another attempt.");
        QueueEnd(SourcePlayEndCause.DeadBallInBounds, 0, 60);
        Check(Attempts(0) == 1 && !Match.Result.IsSpecified,
            "An attempted pass is already observable while its result remains pending.");
        Wake();
        Check(!host.Game.C2State.ResultPending && Match.Result.Kind == SourceResultKind.Incomplete
            && Attempts(0) == 1 && Match.PassCompletions.HomeCompletions == 0,
            "Next-wake incomplete commit must not duplicate attempts or add a completion.");
        SourcePlayActivityStatsView committed = Match.PlayActivityStats;
        for (int tick = 0; tick < 8; tick++) Wake();
        Check(Attempts(0) == 1 && committed.HomePassAttempts == 1,
            "Pending/dead result wakes cannot recount releases.");

        Prepare(1);
        Call("ReleaseSourcePass", 10);
        Publish();
        Check(Attempts(1) == 0, "No selected throw target means no release and no attempt.");
        Set(Actor(10), "PendingThrowTarget", 11);
        Call("ReleaseSourcePass", 10);
        Set(Model, "CarrierId", 11);
        Set(Model, "BallMode", BallMode.Held);
        Set(Model, "SourceFlags", SourceGameFlags.PassThrown | SourceGameFlags.ForwardPass);
        Publish();
        QueueEnd(SourcePlayEndCause.CarrierTackledInBounds, 1, 60);
        Check(Attempts(0) == 0 && Attempts(1) == 1 && Match.PassCompletions.AwayCompletions == 0,
            "Away release precedes the existing completion accumulator.");
        Wake();
        Check(Attempts(0) == 0 && Attempts(1) == 1 && Match.PassCompletions.AwayCompletions == 1
            && Match.CompletedPassStats.AwayPassingYards == 10 && Match.CompletedPassStats.AwayTotalYards == 10,
            "New observations preserve existing side-correct completion/yardage commit.");
    }

    private static void VerifyAttemptDomain()
    {
        foreach (int side in new[] { 0, 1 })
        {
            foreach (float target in new[] { 49.75f, 50f, 60f })
            {
                Prepare(side);
                Position(side * 7 + 4, side, target);
                Set(Actor(side * 7 + 3), "PendingThrowTarget", side * 7 + 4);
                Call("ReleaseSourcePass", side * 7 + 3);
                Publish();
                Check(Attempts(side) == (target < 50f ? 0 : 1) && Attempts(1 - side) == 0,
                    "Attempt accounting uses oriented integer FIELDX >= original LOS, including equality.");
            }
            Prepare(side);
            Position(side * 7 + 4, side, 49f, 100f);
            Set(Actor(side * 7 + 4), "SourceVelocity", new FieldVector(side == 0 ? 3f : -3f, 0f));
            Set(Actor(side * 7 + 3), "PendingThrowTarget", side * 7 + 4);
            Call("ReleaseSourcePass", side * 7 + 3);
            Publish();
            var predicted = (SourceVector3)Get(Model, "SourceBallTarget");
            Check(SourceMatchRules.FieldPosition(predicted.X, side) > 50 && Attempts(side) == 1,
                "A receiver initially behind LOS may have a forward predicted target; count the latter.");

            Prepare(side);
            Position(side * 7 + 4, side, 60f, 180f);
            Set(Actor(side * 7 + 4), "SourceVelocity", new FieldVector(side == 0 ? -4f : 4f, 0f));
            Set(Actor(side * 7 + 3), "PendingThrowTarget", side * 7 + 4);
            Call("ReleaseSourcePass", side * 7 + 3);
            Publish();
            predicted = (SourceVector3)Get(Model, "SourceBallTarget");
            Check(SourceMatchRules.FieldPosition(predicted.X, side) < 50 && Attempts(side) == 0,
                "A receiver initially past LOS does not count when the actual predicted target is behind it.");

            Prepare(side, SourcePlayType.ExtraPoint, 90);
            Set(Actor(side * 7 + 3), "PendingThrowTarget", side * 7 + 4);
            Call("ReleaseSourcePass", side * 7 + 3);
            Publish();
            Check(Attempts(side) == 1, "Original qb_release_ball does not exclude a real two-point PAT pass.");

            Prepare(side, SourcePlayType.Kickoff);
            Call("RequestSourcePass", side * 7 + 4, false);
            Publish();
            Check((int)Get(Actor(side * 7 + 3), "PendingThrowTarget") == -1 && Attempts(side) == 0,
                "The real kickoff caller gate cannot create a pass intent or release statistic.");
            Prepare(side);
            Set(Actor(side * 7 + 3), "SourcePendingLateral", true);
            Set(Actor(side * 7 + 3), "PendingThrowTarget", side * 7 + 4);
            Call("ReleaseSourceBall", side * 7 + 3);
            Publish();
            Check(Attempts(side) == 0 && host.Game.State.Ball.Kind == SourceBallKind.Lateral,
                "A lateral release is not qb_release_ball and does not add a pass attempt.");
        }
    }

    private static void Stop(int side, bool lose = false)
    {
        int victim = side * 7 + 4, attacker = (1 - side) * 7 + 4;
        Set(Model, "CarrierId", victim);
        Set(Model, "SourceNoFumbles", !lose);
        Counts("SourceOffensivePlays")[side] = 4;
        Set(Actor(attacker), "SourceControllerStation", (1 - side) * 2);
        Call("StopSourceCarrier", Actor(victim), Actor(attacker), 0, lose ? 1000000 : 0, "att_push", 1293, false);
        Publish();
    }

    private static void Recover(int side, int held, SourceBallKind kind, bool bounce = false)
    {
        Set(Model, "Possession", (TeamId)side);
        Set(Model, "CarrierId", side * 7 + 4);
        Set(Model, "BallMode", BallMode.Held);
        Set(Model, "BallKind", kind);
        SetEnum(Model, "SourceBallFlags", bounce ? "Bounce" : "0");
        Set(Model, "CatchSourceTick", host.Game.SourceTick - held);
        Position(side * 7 + 4, side, 50);
        Call("SourceCarrierControl", Actor(side * 7 + 4));
        Publish();
    }

    private static void VerifyFumblesAndTackles()
    {
        foreach (int side in new[] { 0, 1 })
        {
            Prepare(side);
            Stop(side, true);
            Check(Counts("SourceFumbles")[side] == 1 && Counts("SourceLostFumbles")[side] == 0
                && Counts("SourceTackles")[1 - side] == 0,
                "Real contact fumble release is immediate, not a lost fumble or a retained-carrier tackle.");
            SourcePlayActivityStatsView dropped = Match.PlayActivityStats;
            Call("StopSourceCarrier", Actor(side * 7 + 4), Actor((1 - side) * 7 + 4), 0, 1000000, "att_push", 1293, false);
            Check(Counts("SourceFumbles")[side] == 1, "A loose ball cannot be fumbled again by the old carrier.");
            Recover(side, 18, SourceBallKind.Fumble);
            Check(Counts("SourceLostFumbles")[side] == 0, "Same-side recovery is not a lost fumble.");
            Recover(1 - side, 17, SourceBallKind.Fumble);
            Check(Counts("SourceLostFumbles")[side] == 0, "Opponent recovery does not establish control before CONTROL_TIME.");
            Recover(1 - side, 18, SourceBallKind.Fumble);
            Recover(1 - side, 19, SourceBallKind.Fumble);
            Check(Counts("SourceLostFumbles")[side] == 1 && Counts("SourceLostFumbles")[1 - side] == 0
                && dropped.HomeLostFumbles == 0 && dropped.AwayLostFumbles == 0,
                "Established opposing possession charges the losing side once and leaves earlier snapshots unchanged.");
            Check((side == 0 ? Match.PlayActivityStats.HomeFumbles : Match.PlayActivityStats.AwayFumbles) == 1
                && (side == 0 ? Match.PlayActivityStats.HomeLostFumbles : Match.PlayActivityStats.AwayLostFumbles) == 1,
                "Fumble and lost-fumble snapshot fields must expose actual independently produced values.");

            Prepare(side);
            Stop(side, true);
            QueueEnd(SourcePlayEndCause.BallOutOfBounds, side, 60);
            Wake();
            Check(Match.Result.Kind == SourceResultKind.FumbledOutOfBounds
                && Counts("SourceFumbles")[side] == 1 && Counts("SourceLostFumbles")[side] == 0,
                "A fumble out of bounds is not an invented lost possession.");

            Prepare(side);
            Set(Model, "SourceLastPossession", 1 - side);
            Set(Model, "CarrierId", side * 7 + 4);
            Set(Model, "BallKind", SourceBallKind.Fumble);
            Set(Model, "CatchSourceTick", host.Game.SourceTick);
            Call("SourcePlayerDown", side * 7 + 4);
            Publish();
            Check(host.Game.C2State.ResultPending && Counts("SourceLostFumbles")[1 - side] == 1,
                "player_down establishes possession before CONTROL_TIME, before the C2 result commit.");
            Wake();
            Check(Counts("SourceLostFumbles")[1 - side] == 1, "Result commit must not re-credit the forced controlled down.");

            foreach (bool bounce in new[] { false, true })
            {
                Prepare(side);
                Recover(1 - side, 18, SourceBallKind.Lateral, bounce);
                Check(Counts("SourceFumbles")[side] == 0
                    && Counts("SourceLostFumbles")[side] == (bounce ? 1 : 0)
                    && Counts("SourceInterceptions")[1 - side] == (bounce ? 0 : 1),
                    "Only bounced lateral losses are lost fumbles; airborne laterals are original interceptions.");
            }

            Prepare(side);
            Stop(side);
            Check(Counts("SourceTackles")[1 - side] == 1 && Counts("SourceTackles")[side] == 0
                && (side == 0 ? Match.PlayActivityStats.AwayTackles : Match.PlayActivityStats.HomeTackles) == 1,
                "Human retained-carrier progress stops must project the actual original tackle counter.");
            Stop(side);
            Call("SourcePlayerDown", side * 7 + 4);
            Publish();
            Wake();
            Check(Counts("SourceTackles")[1 - side] == 1, "Dogpile, down callback and result commit must not add tackle assists.");
            Prepare(side);
            Set(Actor((1 - side) * 7 + 4), "SourceControllerStation", -1);
            Counts("SourceStationPlayers")[(1 - side) * 2] = -1;
            Set(Model, "CarrierId", side * 7 + 4);
            Call("StopSourceCarrier", Actor(side * 7 + 4), Actor((1 - side) * 7 + 4), 0, 0, "att_push", 1293, false);
            Publish();
            Check(Match.PlayActivityStats.HomeTackles == 0 && Match.PlayActivityStats.AwayTackles == 0,
                "Original team tackle statistics deliberately exclude CPU tacklers.");
            Prepare(side, SourcePlayType.ExtraPoint, 90);
            Stop(side);
            Check(Counts("SourceTackles")[1 - side] == 1, "Original human tackle accounting has no blanket PAT exclusion.");
            Prepare(side, los: 60);
            int quarterback = side * 7 + 3, cpu = (1 - side) * 7 + 4;
            Set(Actor(cpu), "SourceControllerStation", -1);
            Counts("SourceStationPlayers")[(1 - side) * 2] = -1;
            Call("StopSourceCarrier", Actor(quarterback), Actor(cpu), 0, 0, "att_push", 1293, false);
            Call("SourcePlayerDown", quarterback);
            Publish();
            Wake();
            Check(Match.PlayActivityStats.HomeTackles == 0 && Match.PlayActivityStats.AwayTackles == 0
                && (side == 0 ? Match.PlayEndStats.AwaySacks : Match.PlayEndStats.HomeSacks) == 1
                && (side == 0 ? Match.PlayEndStats.AwaySackYards : Match.PlayEndStats.HomeSackYards) == 10,
                "A genuine CPU quarterback sack is not an original human tackle statistic; observed tackles="
                + Match.PlayActivityStats.HomeTackles + "/" + Match.PlayActivityStats.AwayTackles
                + ", sacks=" + Match.PlayEndStats.HomeSacks + "/" + Match.PlayEndStats.AwaySacks
                + ", sack yards=" + Match.PlayEndStats.HomeSackYards + "/" + Match.PlayEndStats.AwaySackYards + ".");
        }
    }

    private static void VerifyOtherExistingWriters()
    {
        Prepare(0);
        var random = (Func<int, int>)Call("SourceDrawFor", "player.c:bobble_pass");
        Set(Model, "BallKind", SourceBallKind.Kickoff);
        Static("SourceBallDriver", "Bobble", Model, Actor(4), new SourceVector3(0f, 12f, 0f), 0f, 0f, 0f, random);
        Publish();
        Check(Match.PlayActivityStats.HomeBobbles == 1 && Match.PlayActivityStats.HomeFumbles == 0
            && host.Game.State.Ball.Kind == SourceBallKind.Fumble,
            "A kickoff bobble can become LB_FUMBLE without calling the original fumbles writer.");
        Recover(1, 18, SourceBallKind.Fumble);
        Check(Match.PlayActivityStats.HomeLostFumbles == 1 && Match.PlayActivityStats.HomeFumbles == 0,
            "Lost-fumble totals are not constrained to fabricated fumbles-minus-recoveries formulas.");
        Prepare(0);
        Call("DeflectSourcePass", Actor(11));
        Publish();
        Check(Match.PlayActivityStats.AwayDeflections == 1 && Match.PlayActivityStats.HomeDeflections == 0,
            "Existing deflect_pass writer must retain its actual actor-side projection.");
    }

    private static void VerifyScoringCommit()
    {
        foreach (int initial in new[] { 0, 1 })
        {
            foreach (bool turnover in new[] { false, true })
            {
                int side = turnover ? 1 - initial : initial;
                Prepare(side, SourcePlayType.ExtraPoint, 90, initial);
                QueueEnd(SourcePlayEndCause.CarrierTouchdown, side, 100);
                Check(Match.ScoringStats.HomeTwoPointAttempts == 0 && Match.ScoringStats.AwayTwoPointAttempts == 0,
                    "A queued PAT result has not committed a two-point attempt.");
                Wake();
                Check(Match.Result.Kind == SourceResultKind.Conversion
                    && Counts("SourceTwoPointAttempts")[initial] == 1 && Counts("SourceTwoPointConversions")[initial] == 1
                    && Counts("SourceTwoPointAttempts")[1 - initial] == 0,
                    "Original conversion counters use restored initial offense, even on a defensive conversion return.");
                Check((initial == 0 ? Match.ScoringStats.HomeTwoPointAttempts : Match.ScoringStats.AwayTwoPointAttempts) == 1
                    && (initial == 0 ? Match.ScoringStats.AwayTwoPointAttempts : Match.ScoringStats.HomeTwoPointAttempts) == 0,
                    "Conversion-attempt projection must retain both actual source sides.");
                Check((initial == 0 ? Match.ScoringStats.HomeTwoPointConversions : Match.ScoringStats.AwayTwoPointConversions) == 1,
                    "Scoring snapshots must publish the actual conversion-side totals.");
                SourceScoringStatsView retained = Match.ScoringStats;
                for (int tick = 0; tick < 3; tick++) Wake();
                Check(Counts("SourceTwoPointAttempts")[initial] == 1
                    && (initial == 0 ? retained.HomeTwoPointConversions : retained.AwayTwoPointConversions) == 1,
                    "Idle result wakes do not recount the committed conversion.");

                Prepare(side, SourcePlayType.ExtraPoint, 90, initial);
                QueueEnd(SourcePlayEndCause.CarrierTackledInBounds, side, 95);
                Wake();
                Check(Match.Result.Kind == SourceResultKind.FailedConversion
                    && (initial == 0 ? Match.ScoringStats.HomeTwoPointAttempts : Match.ScoringStats.AwayTwoPointAttempts) == 1
                    && (initial == 0 ? Match.ScoringStats.HomeTwoPointConversions : Match.ScoringStats.AwayTwoPointConversions) == 0,
                    "Failed two-point conversions add an attempt, not a conversion.");
            }
            Prepare(initial);
            QueueEnd(SourcePlayEndCause.CarrierDownInOwnEndZone, initial, 0);
            Wake();
            Check(Match.Result.Kind == SourceResultKind.Safety && Counts("SourceSafeties")[initial] == 1
                && Counts("SourceSafeties")[1 - initial] == 0
                && (initial == 0 ? Match.ScoringStats.HomeSafeties : Match.ScoringStats.AwaySafeties) == 1
                && (initial == 0 ? Match.ScoringStats.AwaySafeties : Match.ScoringStats.HomeSafeties) == 0
                && (initial == 0 ? host.Game.State.AwayScore : host.Game.State.HomeScore) == 2,
                "Native safetys belongs to post-result off_side, not the side scoring safety points.");
            Prepare(1 - initial, SourcePlayType.ExtraPoint, 90, initial);
            QueueEnd(SourcePlayEndCause.CarrierDownInOwnEndZone, 1 - initial, 0);
            Wake();
            Check(Match.Result.Kind == SourceResultKind.PatSafety && Counts("SourceSafeties")[initial] == 1
                && (initial == 0 ? Match.ScoringStats.HomeSafeties : Match.ScoringStats.AwaySafeties) == 1
                && Counts("SourceSafeties")[1 - initial] == 0 && Counts("SourceTwoPointAttempts")[initial] == 0,
                "PAT safety restores initial offense before safetys and is not a two-point attempt.");
            Prepare(initial, SourcePlayType.ExtraPoint, 90);
            Set(Model, "Phase", GamePhase.PlaySelect);
            Call("ApplySourcePat", Call("DecideSourcePat", SourcePatChoice.FreeKick));
            Publish();
            Check((initial == 0 ? host.Game.State.HomeScore : host.Game.State.AwayScore) == 1
                && Match.ScoringStats.HomeTwoPointAttempts == 0 && Match.ScoringStats.AwayTwoPointAttempts == 0
                && Attempts(initial) == 0,
                "The actual free-PAT path adds its original point without inventing passes or two-point attempts.");
            Prepare(initial);
            QueueEnd(SourcePlayEndCause.CarrierTouchdown, initial, 100);
            Wake();
            Check(Counts("SourceTwoPointAttempts")[initial] == 0 && Counts("SourceSafeties")[initial] == 0
                && (initial == 0 ? Match.TouchdownStats.HomeTouchdowns : Match.TouchdownStats.AwayTouchdowns) == 1,
                "Ordinary touchdowns retain their existing accumulator and cannot invent PAT statistics.");
        }
    }

    private static void ToStage(SourceCabinetFlowStage stage)
    {
        for (int tick = 0; tick < 2500 && Flow.Stage != stage; tick++) Wake(7);
        Check(Flow.Stage == stage, "Actual cabinet flow must reach " + stage + ".");
    }

    private static void VerifyFinalAndNewMatch()
    {
        Prepare(0, SourcePlayType.ExtraPoint, 90);
        QueueEnd(SourcePlayEndCause.CarrierTouchdown, 0, 100);
        Wake();
        ToStage(SourceCabinetFlowStage.Inactive);
        Prepare(0, reset: false);
        Set(Actor(3), "PendingThrowTarget", 4);
        Call("ReleaseSourcePass", 3);
        Stop(0, true);
        Recover(1, 18, SourceBallKind.Fumble);
        Prepare(1, reset: false);
        Stop(1);
        Set(Model, "Quarter", 4);
        Set(Model, "SourcePeriodView", new SourcePeriodView(3));
        Set(Model, "SourceClock", SourceClock.Create(0, firstWakeTick: host.Game.SourceTick + 1));
        Set(Model, "HomeScore", 8); Set(Model, "AwayScore", 3);
        var scores = (int[,])Get(Model, "SourceQuarterScores");
        Array.Clear(scores, 0, scores.Length); scores[3, 0] = 8; scores[3, 1] = 3;
        Call("SourcePlayerDown", 11);
        Publish();
        Wake();
        ToStage(SourceCabinetFlowStage.FinalStats);
        SourceMatchView final = Flow.FinalMatch;
        Check(host.Game.State.Phase == GamePhase.GameOver
            && final.PlayActivityStats.HomePassAttempts == 1 && final.PlayActivityStats.HomeFumbles == 1
            && final.PlayActivityStats.HomeLostFumbles == 1 && final.PlayActivityStats.HomeTackles == 1
            && final.ScoringStats.HomeTwoPointAttempts == 1 && final.ScoringStats.HomeTwoPointConversions == 1,
            "FinalMatch must retain actual produced pass, fumble, controlled-loss and human tackle totals.");
        Check(host.Session.LastResult != null
            && ReferenceEquals(host.Session.LastResult.Statistics.Match.PlayActivityStats, final.PlayActivityStats),
            "The actual cabinet final record must retain the immutable snapshot, not rebuild counters.");
        var old = new SourceMatchView(final.Configuration, final.PlayKind, final.Phase, final.Period,
            final.Possession, final.QuarterScores, final.Stations);
        Check(old.PlayActivityStats == null && old.ScoringStats == null,
            "Unobserved old constructor calls must remain unavailable, not fabricated measured zeros.");
        for (int tick = 0; tick < 20; tick++) { Publish(); Render(); }
        Check(final.PlayActivityStats.HomePassAttempts == 1 && Match.PlayActivityStats.HomeLostFumbles == 1,
            "Final render/snapshot reads must not mutate retained statistics.");
        ToStage(SourceCabinetFlowStage.FinalConfirmation);
        for (int tick = 0; tick < 12; tick++) Wake();
        Check(final.PlayActivityStats.HomeTackles == 1 && Match.PlayActivityStats.HomeTackles == 1,
            "Terminal source idle must not clear or recount totals.");
        host.Continue(0);
        for (int tick = 0; tick < 4 && host.Session.Playing; tick++) { Raw(); Render(1); }
        Check(host.Session.Stage == SourceCabinetMenuStage.NextMatch, "Final confirmation must preserve the real next-match boundary.");
        Raw(); Render(); Tap(1);
        Check(host.Session.Playing && host.Session.MatchesStarted == 2 && host.Game.SourceTick == 0,
            "A deliberate public rematch must run the existing once-only restart.");
        Check(Match.PlayActivityStats.HomePassAttempts == 0 && Match.PlayActivityStats.HomeFumbles == 0
            && Match.PlayActivityStats.HomeLostFumbles == 0 && Match.PlayActivityStats.HomeTackles == 0
            && Match.ScoringStats.HomeTwoPointAttempts == 0 && final.PlayActivityStats.HomePassAttempts == 1
            && final.ScoringStats.HomeTwoPointConversions == 1
            && host.Session.LastResult.Statistics.Match.PlayActivityStats.HomeFumbles == 1,
            "Only deliberate newmatch resets totals; final snapshots and completed records remain immutable.");
    }

    private static int Main(string[] args)
    {
        try
        {
            if (args.Length != 1) throw new ArgumentException("Supply the existing approved563 data root, read-only.");
            string root = Path.GetFullPath(args[0]);
            var data = new PlayerData(new DataCatalog(File.ReadAllBytes(Path.Combine(root, "manifest.json")),
                path => File.ReadAllBytes(Path.Combine(root, path))));
            Start(data);
            VerifyReleaseAndCommit();
            VerifyAttemptDomain();
            VerifyFumblesAndTackles();
            VerifyOtherExistingWriters();
            VerifyScoringCommit();
            VerifyFinalAndNewMatch();
            Console.WriteLine("PASS " + assertions + " match-statistics assertions; real production boundary fixtures, not a natural full-match/browser proof.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }

    private sealed class StatisticsSink : IRenderSink
    {
        public void Texture(string id, int width, int height, byte[] rgba, int mipCount, bool repeat, bool linear, string sha256, string origin) { }
        public void Mesh(string id, double[] vertices, int[] indices) { }
        public void WorldDraw(string mesh, string texture, string state) { }
        public void Model(int id, string joints) { }
        public void Appearance(int id, string metadata) { }
        public void FreeBall(string mesh, string texture, string alternateTexture, string metadata) { }
        public void Effects(double[] projection, double[] vertices, int[] textures, int[] rawFlags, double[] flashAlpha, string metadata) { }
        public void Present(double[] camera, double[] joints, int[] models, int[] appearances, double[] ball,
            double[] hudVertices, int[] hudColors, string[] hudTextures, int[] hudCounts, string status) { }
    }
}
