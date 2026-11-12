using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Blitz.Contracts;
using Blitz.Gameplay;
using Blitz.Runtime;

#if SOURCE_RULE_TRANSITIONS_FULL_PLAYER
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using Blitz.SourcePlayer;
#endif

#if !SOURCE_RULE_TRANSITIONS_FULL_PLAYER
internal sealed class LifecycleAssertion : Exception
{
    internal LifecycleAssertion(string message) : base(message) { }
}

internal static class Program
{
    private static FullKernelInputs inputs;
    private static int assertions;
    private static readonly List<string> cases = new List<string>();
    private const int CaptainAssertionCount = 1778;
    private static int captainAssertions;
    private static readonly List<string> captainCases = new List<string>();
    private static readonly List<Dictionary<string, object>> captainRows = new List<Dictionary<string, object>>();
    private static readonly string[] captainScenarios = {
        "turnover-on-downs-to-first-pos", "established-possession-turnover-to-first-pos",
        "next-play-kickoff-no-toggle", "ending-play-kickoff-no-toggle",
        "offense-equals-initial-no-toggle", "resolved-offense-not-first-pos-no-toggle"
    };

    private static void Check(bool condition, string identity)
    {
        assertions++;
        if (!condition) throw new LifecycleAssertion(identity);
    }

    private static T Bind<T>(GameSimulation game, string name)
    {
        return (T)(object)typeof(GameSimulation).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic,
            null, typeof(T).GetMethod("Invoke").GetParameters().Select(p => p.ParameterType).ToArray(), null)
            .CreateDelegate(typeof(T), game);
    }

    private static GameSimulation Create(SourceGameplayCompatibility mode, bool continueAfterGate = true)
    {
        var configuration = new SourceMatchConfiguration(SourceSimulationProfile.OriginalRelease57Hz,
            SourceStationLayout.FourStation, new SourceTeamSelection(0, new[] { 0, 1, 2, 3, 4, 5, 6 }),
            new SourceTeamSelection(1, new[] { 0, 1, 2, 3, 4, 5, 6 }),
            new[] { new SourceStationConfiguration(0, TeamId.Home), new SourceStationConfiguration(1, TeamId.Away),
                new SourceStationConfiguration(2, TeamId.Home), new SourceStationConfiguration(3, TeamId.Away) },
            TeamId.Home, SourceCaptainSlot.Second, continueAfterCabinetGate: continueAfterGate);
        return inputs.Create(configuration: configuration, compatibility: mode,
            randomFactory: seed => new SourceInjectedRandom(site => 99,
                new SourceRandomProvenance("match-terminal/raw99", "Declared boundary fixture, not target RNG",
                    "four-station", SourceRandomEvidence.ExternalUnverified)),
            presentation: new SourcePresentationSettings(TeamId.Home, new[] { -1, -1, -1, -1 }, 0));
    }

    private static void Prepare(GameSimulation game, int quarter, int home, int away, int clock = 0,
        SourcePlayType kind = SourcePlayType.Normal)
    {
        SimulationState m = game.Model;
        m.Phase = GamePhase.DeadBall; m.SourceMatchPhase = SourceMatchPhase.PlayResult;
        m.Quarter = quarter + 1; m.HomeScore = home; m.AwayScore = away; m.Possession = TeamId.Away;
        m.SourceRules = new SourceRulesState(1, 0, 1, 30, 100, kind);
        m.SourcePeriodView = new SourcePeriodView(quarter);
        Array.Clear(m.SourceQuarterScores, 0, m.SourceQuarterScores.Length);
        m.SourceQuarterScores[quarter, 0] = home; m.SourceQuarterScores[quarter, 1] = away;
        m.SourceClock = SourceClock.Create(clock, firstWakeTick: m.SourceTick + 1);
        m.SourceClockRemainder = 0; m.ClockActive = true;
        foreach (ActorState actor in m.Players)
        {
            actor.SourceMode = SourceActorMode.Dead;
            actor.SourceButtons = actor.SourceOldButtons = actor.SourceNewButtons = SourceDroneButtons.None;
            SourcePlayerDriver.SetSourcePosition(actor, new SourceVector3(1000 + actor.Id * 100, 0, actor.Id * 50));
        }
        Bind<Action>(game, "Publish")();
    }

    private static void Continue(GameSimulation game)
    {
        game.Step(new PlayerCommand(action: InputAction.Continue));
        Check(game.SourceEvents.All(e => e.Type != SourceEventType.MatchEnded)
            && game.Events.All(e => e.Type != GameEventType.GameEnded),
            "57Hz skipped host step cannot publish a terminal event early");
        game.Step(default(PlayerCommand));
    }

    private static void TerminalMetadata(GameSimulation game, long sourceTick)
    {
        Check(game.SourceEvents.Count(e => e.Type == SourceEventType.MatchEnded) == 1,
            "actual M1 terminal source caller publishes exactly one typed MatchEnded notification");
        SourceGameEvent item = game.SourceEvents.Single(e => e.Type == SourceEventType.MatchEnded);
        Check(item.Tick == game.State.Tick && item.SourceTick == sourceTick
            && item.Type == SourceEventType.MatchEnded && item.IsSpecified,
            "typed terminal event uses the actual host/source ticks and specified terminal identity");
        Check(item.ActorPlayerId == -1 && item.TargetPlayerId == -1 && item.StationId == -1
            && item.Value == 0 && !item.Result.IsSpecified && !item.Selection.IsSpecified
            && item.Team == game.Model.Possession && item.PlayKind == (SourcePlayKind)game.Model.SourceRules.PlayType,
            "typed terminal metadata has no invented winner value, actor, target, station or result");
        Check(game.Events.Count(e => e.Type == GameEventType.GameEnded) == 1
            && game.Events.Single(e => e.Type == GameEventType.GameEnded).Value
                == game.Model.HomeScore.CompareTo(game.Model.AwayScore),
            "legacy GameEnded remains exactly once with its unchanged score-comparison payload");
        var match = ((ISourceMatchState)game.State).Match;
        Check(game.State.Phase == GamePhase.GameOver && match.Phase == SourceMatchPhase.MatchEnded
            && !game.Model.ClockActive && match.Period.QuarterIndex == game.Model.Quarter - 1,
            "typed notification accompanies the committed real terminal phase and stopped clock");
    }

    private static void Terminal(SourceGameplayCompatibility mode, int quarter, int home, int away,
        bool gate, SourcePeriodStatus status)
    {
        cases.Add(mode + "/terminal/q" + quarter + "/" + home + "-" + away + "/gate" + gate);
        GameSimulation game = Create(mode, gate);
        Prepare(game, quarter, home, away);
        Continue(game);
        TerminalMetadata(game, game.SourceTick);
        var retained = game.SourceEvents.ToArray();
        Check(((ISourceMatchState)game.State).Match.Period.Status == status
            && ((ISourceMatchState)game.State).Match.Period.CabinetGateRequired
                == (status == SourcePeriodStatus.CabinetContinuationDeclined),
            "normal, maximum-overtime tie and declined-cabinet terminal dispositions retain source priority");
        for (int i = 0; i < 6; i++)
        {
            game.Step(new PlayerCommand(action: InputAction.Continue));
            Check(game.State.Phase == GamePhase.GameOver
                && game.SourceEvents.All(e => e.Type != SourceEventType.MatchEnded)
                && game.Events.All(e => e.Type != GameEventType.GameEnded),
                "later GameOver host steps and queued Continue cannot replay either terminal event");
        }
        Check(retained.Count(e => e.Type == SourceEventType.MatchEnded) == 1,
            "a retained terminal batch remains immutable when later host steps clear live events");
    }

    private static void Nonterminal(SourceGameplayCompatibility mode, int quarter, int home, int away,
        int clock = 0, SourcePlayType kind = SourcePlayType.Normal)
    {
        cases.Add(mode + "/nonterminal/q" + quarter + "/" + home + "-" + away + "/" + clock + "/" + kind);
        GameSimulation game = Create(mode);
        Prepare(game, quarter, home, away, clock, kind);
        Continue(game);
        Check(game.SourceEvents.All(e => e.Type != SourceEventType.MatchEnded)
            && game.Events.All(e => e.Type != GameEventType.GameEnded)
            && game.State.Phase != GamePhase.GameOver,
            "unexpired, earlier-quarter, tied overtime and pending PAT boundaries publish no terminal notification");
    }

    private static SourceInputFrame Neutral()
    {
        return new SourceInputFrame(new[] {
            new SourceStationInput(0, 0, 0, SourceButtons.None), new SourceStationInput(1, 0, 0, SourceButtons.None),
            new SourceStationInput(2, 0, 0, SourceButtons.None), new SourceStationInput(3, 0, 0, SourceButtons.None) });
    }

    private static void Bridge(SourceGameplayCompatibility mode, bool gate)
    {
        cases.Add(mode + "/actual-bridge/gate" + gate);
        GameSimulation game = Create(mode, gate);
        Prepare(game, gate ? 3 : 1, 7, 0);
        var bridge = new FixedStepBridge(game);
        var typed = new List<SourceGameEvent>();
        var legacy = new List<GameEvent>();
        int restarts = 0;
        bridge.SourceEventRaised += item => { if (item.Type == SourceEventType.MatchEnded) typed.Add(item); };
        bridge.EventRaised += item => { if (item.Type == GameEventType.GameEnded) legacy.Add(item); };
        bridge.Restarted += () => restarts++;
        SourceInputFrame neutral = Neutral();
        bridge.QueueAction(InputAction.Continue);
        bridge.AdvanceSource(3 * FixedStepBridge.StepSeconds, () => neutral);
        Check(typed.Count == 1 && legacy.Count == 1 && bridge.State.Phase == GamePhase.GameOver,
            "actual FixedStepBridge delivers typed and legacy terminal notifications exactly once in a batched frame");
        Check(typed[0].Tick == legacy[0].Tick && typed[0].SourceTick > 0
            && typed[0].Team == TeamId.Away && typed[0].Value == 0 && legacy[0].Value == 1,
            "real bridge keeps terminal buses separate and does not turn possession context into winner payload");
        bridge.AdvanceSource(FixedStepBridge.MaximumAccumulatedSeconds, () => neutral);
        Check(typed.Count == 1 && legacy.Count == 1,
            "later real bridge frames cannot re-deliver a stale terminal batch");
        bridge.Restart();
        Check(restarts == 1 && typed.Count == 1 && legacy.Count == 1
            && game.SourceEvents.All(e => e.Type != SourceEventType.MatchEnded)
            && game.State.HomeScore == 0 && game.State.AwayScore == 0
            && game.Compatibility == mode && game.State.Phase == GamePhase.PreSnap,
            "real bridge restart resets match state without leaking terminal events or duplicating subscriptions");
        Prepare(game, 6, 14, 14);
        bridge.AdvanceSource(0, () => neutral);
        bridge.QueueAction(InputAction.Continue);
        bridge.AdvanceSource(3 * FixedStepBridge.StepSeconds, () => neutral);
        Check(typed.Count == 2 && legacy.Count == 2 && restarts == 1 && legacy[1].Value == 0,
            "the same restarted bridge delivers the next match terminal once including a maximum-overtime tie");
    }

    private static void Producer(SourceGameplayCompatibility mode)
    {
        cases.Add(mode + "/actual-period-producer-no-RNG");
        GameSimulation game = Create(mode);
        Prepare(game, 3, 0, 14);
        var random = (SourceInjectedRandom)game.SourceRandomService;
        int before = random.Trace.Count;
        long draws = game.Model.SourceRandomDraws;
        Check(Bind<Func<bool>>(game, "AdvanceSourcePeriod")(),
            "actual period producer still returns its real terminal decision");
        Bind<Action>(game, "Publish")();
        TerminalMetadata(game, game.SourceTick);
        Check(random.Trace.Count == before && game.Model.SourceRandomDraws == draws,
            "typed terminal notification adds no RNG or source-game-over reward draws");
    }

    private static void Pat(SourceGameplayCompatibility mode, int quarter, int home, int away, bool terminal)
    {
        cases.Add(mode + "/actual-PAT/q" + quarter + "/" + home + "-" + away);
        GameSimulation game = Create(mode);
        // Produce the real retained camera/cache and preserve the next skipped 60/57 host wake.
        for (int i = 0; i < SliceRules.TicksPerSecond; i++) game.Step(default(PlayerCommand));
        if (game.SourceTick != SourceTiming.DefaultTicksPerSecond || game.Model.SourceClockRemainder != 0
            || game.Model.SourceMovementCamera == null || game.Model.SourceMovementCameraTick != game.SourceTick)
            throw new InvalidOperationException("PAT fixture requires real current camera/cache output and an aligned 60/57 cycle.");
        Prepare(game, quarter, home, away, kind: SourcePlayType.ExtraPoint);
        game.Model.Phase = GamePhase.PlaySelect; game.Model.SourceMatchPhase = SourceMatchPhase.PatSelection;
        Bind<Action>(game, "Publish")();
        game.Step(new PlayerCommand(source: new SourceCommand(SourceAction.SelectPat, patChoice: SourcePatOption.FreeKick)));
        Check(game.SourceEvents.All(e => e.Type != SourceEventType.MatchEnded),
            "a skipped host wake cannot complete or terminate a pending actual PAT");
        game.Step(default(PlayerCommand));
        bool skipped = away > home;
        Check(game.SourceEvents.Count(e => e.Type == SourceEventType.PatSelected) == 1
            && game.Model.SourcePatStatus == (skipped ? SourcePatStatus.SkipUnnecessary : SourcePatStatus.FreeKickComplete),
            "actual source PAT caller resolves the original selected or unnecessary PAT before the period gate");
        Check(game.State.HomeScore == home && game.State.AwayScore == away + (skipped ? 0 : 1)
            && game.SourceEvents.Count(e => e.Type == SourceEventType.FreePointResult) == (skipped ? 0 : 1),
            "actual late PAT preserves original free-point scoring and typed result emission");
        if (terminal)
        {
            TerminalMetadata(game, game.SourceTick);
            SourceEventType[] types = game.SourceEvents.Select(e => e.Type).ToArray();
            Check(Array.IndexOf(types, SourceEventType.PatSelected) < Array.IndexOf(types, SourceEventType.MatchEnded)
                && (skipped || Array.IndexOf(types, SourceEventType.FreePointResult) < Array.IndexOf(types, SourceEventType.MatchEnded)),
                "typed terminal follows actual PAT selection and free-point result without reordering source events");
        }
        else Check(game.SourceEvents.All(e => e.Type != SourceEventType.MatchEnded)
            && game.Events.All(e => e.Type != GameEventType.GameEnded)
            && game.State.Phase == GamePhase.PreSnap
            && ((ISourceMatchState)game.State).Match.Period.Status == SourcePeriodStatus.OvertimeKickoff,
            "a real tying late PAT continues into overtime without publishing either terminal event");
    }

    private static void Legacy()
    {
        foreach (int sign in new[] { -1, 0, 1 })
        {
            cases.Add("legacy60/terminal/" + sign);
            var game = new GameSimulation();
            game.Model.Phase = GamePhase.DeadBall; game.Model.Quarter = SliceRules.QuarterCount;
            game.Model.QuarterTicksRemaining = 0; game.Model.HomeScore = sign > 0 ? 7 : 0;
            game.Model.AwayScore = sign < 0 ? 7 : 0;
            game.Step(new PlayerCommand(action: InputAction.Continue));
            Check(game.State.Phase == GamePhase.GameOver && game.SourceEvents.Count == 0
                && game.Events.Single(e => e.Type == GameEventType.GameEnded).Value == sign,
                "explicit Legacy60 retains its old terminal event without a synthetic source notification");
        }
    }

    private sealed class CaptainBoundary
    {
        internal readonly int OldOffense, InitialOffense, LastPossession, Down, ResolvedOffense;
        internal readonly int ResolvedDown, ResolvedLos, ResolvedFirst;
        internal readonly SourcePlayType EndingType, NextType;
        internal readonly SourcePlayEndCause Cause;
        internal readonly SourceGameFlags Flags;
        internal readonly SourcePlayResult Result;
        internal readonly bool Toggle, Turnover;
        internal readonly float EndX;

        internal CaptainBoundary(int first, string scenario)
        {
            if (!captainScenarios.Contains(scenario)) throw new ArgumentException("Unknown declared captain boundary.");
            bool downs = scenario == "turnover-on-downs-to-first-pos";
            bool safety = scenario == "next-play-kickoff-no-toggle";
            bool same = scenario == "offense-equals-initial-no-toggle";
            bool notFirst = scenario == "resolved-offense-not-first-pos-no-toggle";
            OldOffense = downs || notFirst ? 1 - first : first;
            InitialOffense = same || notFirst ? first : 1 - first;
            LastPossession = OldOffense;
            Down = downs ? 4 : 2;
            ResolvedOffense = notFirst ? 1 - first : first;
            ResolvedDown = safety ? 2 : same ? 3 : 1;
            ResolvedLos = safety ? 30 : 50;
            ResolvedFirst = safety || same ? 60 : 80;
            EndingType = scenario == "ending-play-kickoff-no-toggle" ? SourcePlayType.Kickoff : SourcePlayType.Normal;
            NextType = safety ? SourcePlayType.Kickoff : SourcePlayType.Normal;
            Cause = safety ? SourcePlayEndCause.CarrierDownInOwnEndZone : SourcePlayEndCause.CarrierTackledInBounds;
            Flags = downs || same ? SourceGameFlags.None : SourceGameFlags.PossessionChange;
            Result = safety ? SourcePlayResult.Safety : downs || same ? SourcePlayResult.Down : SourcePlayResult.NewPossession;
            Toggle = downs || scenario == "established-possession-turnover-to-first-pos";
            Turnover = downs;
            EndX = safety ? first == 0 ? -400f : 400f : 0f;
        }
    }

    private static void CaptainCheck(bool condition, string field)
    {
        captainAssertions++;
        if (!condition) throw new LifecycleAssertion(field);
    }

    private static void CaptainInvariant(Dictionary<string, object> row, string field, bool condition)
    {
        ((Dictionary<string, bool>)row["invariants"])[field] = condition;
        row["assertions"] = (int)row["assertions"] + 1;
        CaptainCheck(condition, (string)row["id"] + "/" + field);
    }

    private static SourceMatchView Match(GameSimulation game) { return ((ISourceMatchState)game.State).Match; }

    private static Dictionary<string, object> CaptainObservation(GameSimulation game)
    {
        SimulationState m = game.Model;
        SourcePossessionView possession = Match(game).Possession;
        return new Dictionary<string, object> {
            ["host_tick"] = game.State.Tick, ["source_tick"] = game.SourceTick,
            ["captain"] = possession.Captain.ToString(), ["offense"] = possession.Offense.ToString(),
            ["initial_offense"] = possession.InitialOffense.ToString(),
            ["last_possession"] = possession.LastPossession.ToString(),
            ["first_possession"] = possession.FirstPossession.ToString(),
            ["play_type"] = m.SourceRules.PlayType.ToString(), ["phase"] = game.State.Phase.ToString(),
            ["match_phase"] = Match(game).Phase.ToString(), ["result_pending"] = game.C2State.ResultPending,
            ["end_epoch"] = game.C2State.EndEpoch, ["home_score"] = m.HomeScore, ["away_score"] = m.AwayScore,
            ["down"] = m.SourceRules.Down, ["los"] = m.SourceRules.LineOfScrimmage,
            ["first_down"] = m.SourceRules.FirstDown, ["flags"] = (int)m.SourceFlags,
            ["ball_x"] = m.SourceBallPosition.X,
            ["play_end_x"] = m.SourcePlayEndX, ["line_position_z"] = m.SourceLinePosition,
            ["clock_packed"] = m.SourceClock.PackedTime, ["clock_local"] = m.SourceClock.LocalPackedTime,
            ["clock_active"] = m.ClockActive, ["clock_play_time"] = m.SourceClock.PlayTime,
            ["clock_next_wake_tick"] = m.SourceClock.NextWakeTick,
            ["dispatch_ordinal"] = m.SourceDispatchOrdinal,
            ["game_proc_last_wake_tick"] = m.SourceProcesses.Require(GameSimulation.GameProcess).LastWakeTick,
            ["process_serials"] = m.SourceProcesses.Processes.Select(p => p.Serial).ToArray(),
            ["compatibility"] = game.Compatibility.ToString(), ["repair_count"] = m.SourceCompatibilityRepairCount,
            ["rng_trace_count"] = ((SourceInjectedRandom)game.SourceRandomService).Trace.Count,
            ["rng_draws"] = m.SourceRandomDraws
        };
    }

    private static GameSimulation CreateCaptain(FullKernelInputs factory, SourceGameplayCompatibility mode,
        TeamId first, SourceCaptainSlot initial)
    {
        var configuration = new SourceMatchConfiguration(SourceSimulationProfile.OriginalRelease57Hz,
            SourceStationLayout.FourStation, new SourceTeamSelection(0, new[] { 0, 1, 2, 3, 4, 5, 6 }),
            new SourceTeamSelection(1, new[] { 0, 1, 2, 3, 4, 5, 6 }),
            new[] { new SourceStationConfiguration(0, TeamId.Home, -1), new SourceStationConfiguration(1, TeamId.Home, -1),
                new SourceStationConfiguration(2, TeamId.Away, -1), new SourceStationConfiguration(3, TeamId.Away, -1) },
            first, initial);
        return factory.Create(configuration: configuration, compatibility: mode,
            randomFactory: seed => new SourceInjectedRandom(site => 99,
                new SourceRandomProvenance("match-captain/raw99", "Declared boundary fixture, not target RNG",
                    "grouped-four-station", SourceRandomEvidence.ExternalUnverified)),
            presentation: new SourcePresentationSettings(TeamId.Home, new[] { -1, -1, -1, -1 }, 0));
    }

    private static void CaptainCase(FullKernelInputs factory, string profile, SourceGameplayCompatibility mode,
        TeamId first, SourceCaptainSlot initial, string scenario)
    {
        string id = "captain/" + profile + "/" + first + "/" + initial + "/" + scenario;
        var boundary = new CaptainBoundary((int)first, scenario);
        SourceCaptainSlot expectedCaptain = boundary.Toggle
            ? initial == SourceCaptainSlot.First ? SourceCaptainSlot.Second : SourceCaptainSlot.First : initial;
        bool queued = profile != "C1-Strict";
        string caller = queued ? "queued-game_proc" : "immediate-EndSourcePlay";
        var actual = new Dictionary<string, object>();
        var row = new Dictionary<string, object> {
            ["id"] = id, ["profile"] = profile, ["declared_boundary_fixture"] = true,
            ["caller"] = caller, ["assertions"] = 0, ["actual"] = actual,
            ["invariants"] = new Dictionary<string, bool>(),
            ["expected"] = new Dictionary<string, object> {
                ["old_offense"] = ((TeamId)boundary.OldOffense).ToString(),
                ["initial_offense"] = ((TeamId)boundary.InitialOffense).ToString(),
                ["last_possession"] = ((TeamId)boundary.LastPossession).ToString(),
                ["resolved_offense"] = ((TeamId)boundary.ResolvedOffense).ToString(),
                ["saved_ending_type"] = boundary.EndingType.ToString(), ["next_type"] = boundary.NextType.ToString(),
                ["predicate"] = boundary.Toggle, ["captain"] = expectedCaptain.ToString(),
                ["result"] = boundary.Result.ToString(), ["cause"] = boundary.Cause.ToString(),
                ["down"] = boundary.ResolvedDown, ["los"] = boundary.ResolvedLos,
                ["first_down"] = boundary.ResolvedFirst, ["flags"] = (int)boundary.Flags,
                ["raw_play_end_x"] = boundary.EndX
            }
        };
        captainCases.Add(id); captainRows.Add(row);
        GameSimulation game = CreateCaptain(factory, mode, first, initial);
        SimulationState m = game.Model;
        SourceMatchConfiguration configuration = Match(game).Configuration;
        row["configuration"] = new Dictionary<string, object> {
            ["layout"] = configuration.StationLayout.ToString(), ["first_possession"] = configuration.FirstPossession.ToString(),
            ["initial_captain"] = configuration.Captain.ToString(), ["compatibility"] = mode.ToString(),
            ["stations"] = configuration.Stations.Select(s => new Dictionary<string, object> {
                ["id"] = s.StationId, ["team"] = s.Team.ToString(), ["initial_player_id"] = s.InitialPlayerId }).ToArray()
        };
        CaptainInvariant(row, "configuration/grouped-four-station", configuration.Stations.Count == 4
            && configuration.Stations.Select(s => s.StationId).SequenceEqual(new[] { 0, 1, 2, 3 })
            && configuration.Stations.All(s => (int)s.Team == s.StationId / 2 && s.InitialPlayerId == -1)
            && configuration.FirstPossession == first && configuration.Captain == initial
            && game.SourceC2Enabled == queued && game.Compatibility == mode);
        for (int i = 0; i < SliceRules.TicksPerSecond; i++) game.Step(default(PlayerCommand));
        actual["startup"] = CaptainObservation(game);
        CaptainInvariant(row, "startup/real-camera-and-60-host-57-source-cycle",
            game.State.Tick == SliceRules.TicksPerSecond && game.SourceTick == SourceTiming.DefaultTicksPerSecond
            && m.SourceClockRemainder == 0 && m.SourceMovementCamera != null && m.SourceMovementCameraTick == game.SourceTick);

        // Explicit result-boundary inputs, not a naturally played turnover. Do not assign clocks' host remainder,
        // source ticks, C2 epochs or camera/cache output to manufacture admission.
        m.Phase = GamePhase.LiveBall; m.SourceMatchPhase = SourceMatchPhase.LiveBall;
        m.Quarter = 1; m.HomeScore = 7; m.AwayScore = 3; m.SourcePeriodView = new SourcePeriodView(0);
        Array.Clear(m.SourceQuarterScores, 0, m.SourceQuarterScores.Length);
        m.SourceQuarterScores[0, 0] = 7; m.SourceQuarterScores[0, 1] = 3;
        Bind<Action<SourceRulesState>>(game, "SetSourceRules")(new SourceRulesState(boundary.OldOffense,
            boundary.InitialOffense, boundary.Down, 30, 60, boundary.EndingType));
        m.SourceLastPossession = boundary.LastPossession; m.SourceFlags = boundary.Flags;
        m.SourceLastResult = SourcePlayResult.Down; m.SourceResultView = default(SourceMatchResult);
        m.SourcePlayEndX = boundary.EndX; m.SourcePlayEndZ = m.SourceFumbleX = m.SourceLinePosition = 0;
        m.SourceOffenseTurnover = false; m.SourceBeginPlayInitialized = true;
        m.SourceClock = SourceClock.Create(0x3c07, firstWakeTick: m.SourceTick + 1); m.ClockActive = true;
        Bind<Action>(game, "UpdateSourceClockView")();
        int player = boundary.OldOffense * SliceRules.PlayersPerTeam + 3;
        m.CarrierId = player; m.TargetPlayerId = m.ThrowerId = -1;
        m.BallMode = BallMode.Held; m.BallKind = boundary.EndingType == SourcePlayType.Kickoff
            ? SourceBallKind.Kickoff : SourceBallKind.Pass;
        SourceBallDriver.SetPosition(m, new SourceVector3(boundary.EndX, 0, 0));
        foreach (ActorState actor in m.Players)
        {
            actor.SourceMode = SourceActorMode.Dead;
            actor.SourceButtons = actor.SourceOldButtons = actor.SourceNewButtons = SourceDroneButtons.None;
            SourcePlayerDriver.SetSourcePosition(actor, new SourceVector3(1000 + actor.Id * 100, 0, actor.Id * 50));
        }
        Bind<Action>(game, "Publish")();
        actual["before"] = CaptainObservation(game);
        CaptainInvariant(row, "before/published-captain-and-possession", Match(game).Possession.Captain == initial
            && Match(game).Possession.FirstPossession == first && Match(game).Possession.Offense == (TeamId)boundary.OldOffense
            && Match(game).Possession.InitialOffense == (TeamId)boundary.InitialOffense
            && Match(game).Possession.LastPossession == (TeamId)boundary.LastPossession);
        CaptainInvariant(row, "before/declared-result-inputs", m.SourceRules.PlayType == boundary.EndingType
            && m.SourceRules.Down == boundary.Down && m.SourceRules.LineOfScrimmage == 30 && m.SourceRules.FirstDown == 60
            && m.SourceFlags == boundary.Flags && m.SourcePlayEndX == boundary.EndX
            && m.SourceClock.PackedTime == 0x3c07 && m.ClockActive && !game.C2State.ResultPending);
        long hostBefore = game.State.Tick, sourceBefore = game.SourceTick, epochBefore = game.C2State.EndEpoch;
        long drawsBefore = m.SourceRandomDraws, repairsBefore = m.SourceCompatibilityRepairCount;
        int dispatchBefore = m.SourceDispatchOrdinal;
        long[] processesBefore = m.SourceProcesses.Processes.Select(p => p.Serial).ToArray();
        int legacyBefore = game.Events.Count, sourceEventsBefore = game.SourceEvents.Count;
        var random = (SourceInjectedRandom)game.SourceRandomService;
        int traceBefore = random.Trace.Count;
        float displayLos = m.LineOfScrimmage, displayFirst = m.FirstDownLine;
        FieldVector position = m.BallPosition;
        Bind<Action<SourcePlayEndCause, int, FieldVector, int, SourceVector3?>>(game, "EndSourcePlay")(
            boundary.Cause, player, position, -1, null);
        Bind<Action>(game, "Publish")();
        actual["queue"] = null; actual["skipped_host"] = null;
        if (queued)
        {
            actual["queue"] = CaptainObservation(game);
            SourceC2Event[] queueEvents = game.C2Events.Where(e => e.Kind == SourceC2EventKind.PlayOverQueued).ToArray();
            CaptainInvariant(row, "queue/Match.Possession.Captain", game.C2State.ResultPending
                && Match(game).Possession.Captain == initial && game.State.Tick == hostBefore && game.SourceTick == sourceBefore
                && game.C2State.EndEpoch == epochBefore + 1 && queueEvents.Length == 1
                && queueEvents[0].SourceTick == sourceBefore && queueEvents[0].Epoch == epochBefore + 1);
            CaptainInvariant(row, "queue/uncommitted-result-rules-score-clock",
                game.SourceEvents.Skip(sourceEventsBefore).All(e => e.Type != SourceEventType.PlayResolved)
                && game.Events.Count == legacyBefore && !Match(game).Result.IsSpecified
                && m.SourceRules.OffenseSide == boundary.OldOffense && m.SourceRules.PlayType == boundary.EndingType
                && m.SourceRules.Down == boundary.Down && m.SourceFlags == boundary.Flags
                && m.HomeScore == 7 && m.AwayScore == 3 && m.SourceClock.PackedTime == 0x3c07 && m.ClockActive
                && m.SourceRandomDraws == drawsBefore && random.Trace.Count == traceBefore);
            game.Step(default(PlayerCommand));
            actual["skipped_host"] = CaptainObservation(game);
            CaptainInvariant(row, "queue/skipped-host-cannot-commit", game.State.Tick == hostBefore + 1
                && game.SourceTick == sourceBefore && game.C2State.ResultPending
                && Match(game).Possession.Captain == initial && !Match(game).Result.IsSpecified
                && game.SourceEvents.All(e => e.Type != SourceEventType.PlayResolved));
            game.Step(default(PlayerCommand));
            SourceC2Event[] commits = game.C2Events.Where(e => e.Kind == SourceC2EventKind.ResultCommitted).ToArray();
            actual["commit_c2_events"] = commits.Select(e => new Dictionary<string, object> {
                ["source_tick"] = e.SourceTick, ["epoch"] = e.Epoch, ["process"] = e.Process,
                ["process_order"] = e.ProcessOrder, ["source_path"] = e.SourcePath,
                ["function"] = e.FunctionSymbol, ["line"] = e.SourceLine }).ToArray();
            CaptainInvariant(row, "queued-game_proc/commit/caller", !game.C2State.ResultPending
                && game.State.Tick == hostBefore + 2 && game.SourceTick == sourceBefore + 1
                && commits.Length == 1 && commits[0].SourceTick == game.SourceTick && commits[0].Epoch == epochBefore + 1
                && commits[0].Process == "game_proc" && commits[0].ProcessOrder >= 0
                && commits[0].SourcePath == "nfl.c" && commits[0].FunctionSymbol == "game_proc" && commits[0].SourceLine == 1381);
        }
        else
        {
            actual["commit_c2_events"] = new object[0];
            CaptainInvariant(row, "immediate-EndSourcePlay/commit/caller", !game.C2State.Enabled
                && !game.C2State.ResultPending && game.State.Tick == hostBefore && game.SourceTick == sourceBefore
                && game.C2Events.Count == 0 && game.SourceEvents.Skip(sourceEventsBefore).Count(e => e.Type == SourceEventType.PlayResolved) == 1);
        }

        actual["commit"] = CaptainObservation(game);
        CaptainInvariant(row, caller + "/commit/Match.Possession.Captain/exactly-once",
            Match(game).Possession.Captain == expectedCaptain);
        SourceMatchView committed = Match(game);
        SourceMatchResult result = committed.Result;
        int homePoints = boundary.Result == SourcePlayResult.Safety && boundary.ResolvedOffense == 1 ? 2 : 0;
        int awayPoints = boundary.Result == SourcePlayResult.Safety && boundary.ResolvedOffense == 0 ? 2 : 0;
        CaptainInvariant(row, "commit/published-possession", committed.Possession.Offense == (TeamId)boundary.ResolvedOffense
            && committed.Possession.InitialOffense == (TeamId)boundary.InitialOffense
            && committed.Possession.FirstPossession == first && committed.Possession.LastPossession == (TeamId)boundary.LastPossession
            && committed.PlayKind == (SourcePlayKind)boundary.NextType);
        actual["result"] = new Dictionary<string, object> {
            ["kind"] = result.Kind.ToString(), ["ending_type"] = result.PlayKind.ToString(),
            ["cause"] = result.Cause.ToString(), ["status"] = result.Status.ToString(),
            ["home_points"] = result.HomePoints, ["away_points"] = result.AwayPoints,
            ["turnover_on_downs"] = result.TurnoverOnDowns, ["first_down"] = result.JustGotFirstDown
        };
        CaptainInvariant(row, "commit/result-classification", result.IsSpecified && result.Kind == (SourceResultKind)boundary.Result
            && result.Status == (SourceResultStatus)SourceClassifierStatus.Classified && result.Cause == (SourcePlayEndKind)boundary.Cause
            && result.PlayKind == (SourcePlayKind)boundary.EndingType && result.TurnoverOnDowns == boundary.Turnover
            && !result.JustGotFirstDown && result.HomePoints == homePoints && result.AwayPoints == awayPoints
            && m.LastOutcome == (boundary.Turnover ? PlayOutcome.TurnoverOnDowns
                : boundary.Result == SourcePlayResult.Safety ? PlayOutcome.Safety : PlayOutcome.Tackled));
        CaptainInvariant(row, "commit/score-and-quarter-score", game.State.HomeScore == 7 + homePoints
            && game.State.AwayScore == 3 + awayPoints && m.SourceQuarterScores[0, 0] == 7 + homePoints
            && m.SourceQuarterScores[0, 1] == 3 + awayPoints);
        float expectedEndX = boundary.Turnover ? SourceMatchRules.WorldPosition(50, boundary.OldOffense)
            : SourceMatchRules.RoundPlayEndPosition(boundary.EndX);
        CaptainInvariant(row, "commit/rules-spot-down", m.SourceRules.OffenseSide == boundary.ResolvedOffense
            && m.SourceRules.InitialOffenseSide == boundary.InitialOffense && m.SourceRules.Down == boundary.ResolvedDown
            && game.State.Down == boundary.ResolvedDown && m.SourceRules.LineOfScrimmage == boundary.ResolvedLos
            && m.SourceRules.FirstDown == boundary.ResolvedFirst && m.SourceRules.PlayType == boundary.NextType
            && !m.SourceRules.NoFirstDown && m.SourcePlayEndX == expectedEndX && m.SourceLinePosition == 0);
        float expectedDisplayLos = boundary.Turnover ? displayLos : SourceMatchRules.AbsoluteYardsFromSourceWorld(
            SourceMatchRules.WorldPosition(boundary.ResolvedLos, boundary.ResolvedOffense));
        float expectedDisplayFirst = boundary.Turnover ? displayFirst : SourceMatchRules.AbsoluteYardsFromSourceWorld(
            SourceMatchRules.WorldPosition(boundary.ResolvedFirst, boundary.ResolvedOffense));
        actual["displayed_lines"] = new[] { m.LineOfScrimmage, m.FirstDownLine };
        CaptainInvariant(row, "commit/displayed-lines", m.LineOfScrimmage == expectedDisplayLos && m.FirstDownLine == expectedDisplayFirst);
        SourceGameFlags postWakeFlags = boundary.Flags;
        // At the declared LOS50, original player.c:10458 tests the signed held-ball position, not the carrier origin.
        if (queued && boundary.NextType != SourcePlayType.Kickoff
            && (boundary.ResolvedOffense == 0 ? m.SourceBallPosition.X : -m.SourceBallPosition.X) >= 0f)
            postWakeFlags |= SourceGameFlags.BallCrossedLine;
        CaptainInvariant(row, "commit/flags-and-last-result", m.SourceFlags == postWakeFlags
            && m.SourceLastResult == boundary.Result && m.SourceLastPossession == boundary.LastPossession
            && m.SourceOffenseTurnover == boundary.Turnover);
        CaptainInvariant(row, "commit/clock-fraction-and-stop", !m.ClockActive && m.SourceClock.PackedTime == 0x3c00
            && m.SourceClock.LocalPackedTime == 0x3c07 && m.SourceClock.PlayTime == 0 && m.PackedGameTime == 0x3c00
            && m.SourceClock.NextWakeTick == game.SourceTick + 1 && !m.SourceClock.ProcessComplete);
        CaptainInvariant(row, "commit/phase-ball-and-actors", game.State.Phase == GamePhase.DeadBall
            && committed.Phase == SourceMatchPhase.PlayResult && !m.SourceBeginPlayInitialized && m.BallMode == BallMode.Dead
            && m.Players.All(p => p.SourcePlayOver && !p.SourceInPlay));
        SourceGameEvent[] sourceBatch = game.SourceEvents.Skip(queued ? 0 : sourceEventsBefore).ToArray();
        GameEvent[] legacyBatch = game.Events.Skip(queued ? 0 : legacyBefore).ToArray();
        GameEventType[] expectedLegacy = boundary.Turnover ? new[] { GameEventType.TurnoverOnDowns, GameEventType.PossessionChanged }
            : boundary.Result == SourcePlayResult.Safety ? new[] { GameEventType.Safety } : new GameEventType[0];
        actual["source_events"] = sourceBatch.Select(e => e.Type.ToString()).ToArray();
        actual["legacy_events"] = legacyBatch.Select(e => e.Type.ToString()).ToArray();
        actual["source_event_context"] = sourceBatch.Select(e => new Dictionary<string, object> {
            ["type"] = e.Type.ToString(), ["team"] = e.Team.ToString(), ["actor"] = e.ActorPlayerId,
            ["target"] = e.TargetPlayerId, ["station"] = e.StationId, ["value"] = e.Value,
            ["play_kind"] = e.PlayKind.ToString(), ["host_tick"] = e.Tick, ["source_tick"] = e.SourceTick }).ToArray();
        actual["legacy_event_context"] = legacyBatch.Select(e => new Dictionary<string, object> {
            ["type"] = e.Type.ToString(), ["team"] = e.Team.ToString(), ["player"] = e.PlayerId,
            ["actor"] = e.ActorPlayerId, ["target"] = e.TargetPlayerId, ["value"] = e.Value,
            ["host_tick"] = e.Tick, ["source_tick"] = e.SourceTick }).ToArray();
        TeamId legacyTeam = (TeamId)(boundary.Turnover ? boundary.ResolvedOffense : 1 - boundary.OldOffense);
        CaptainInvariant(row, "commit/exact-source-and-legacy-events", sourceBatch.Length == 1
            && sourceBatch[0].Type == SourceEventType.PlayResolved && sourceBatch[0].Tick == game.State.Tick
            && sourceBatch[0].SourceTick == game.SourceTick && sourceBatch[0].Result.Kind == result.Kind
            && sourceBatch[0].Result.PlayKind == (SourcePlayKind)boundary.EndingType
            && sourceBatch[0].Team == (TeamId)boundary.OldOffense && sourceBatch[0].ActorPlayerId == player
            && sourceBatch[0].TargetPlayerId == -1 && sourceBatch[0].StationId == -1 && sourceBatch[0].Value == 0
            && sourceBatch[0].PlayKind == (SourcePlayKind)boundary.EndingType
            && legacyBatch.Select(e => e.Type).SequenceEqual(expectedLegacy)
            && legacyBatch.All(e => e.Team == legacyTeam && e.PlayerId == player && e.ActorPlayerId == player
                && e.TargetPlayerId == -1 && e.Value == (boundary.Result == SourcePlayResult.Safety ? 2 : 0)
                && e.Tick == game.State.Tick && e.SourceTick == game.SourceTick));
        SourceRandomDraw[] boundaryDraws = random.Trace.Skip(traceBefore).Where(d => !queued
            || d.Site.SourceTick == game.SourceTick && d.Site.Process == "game_proc").ToArray();
        actual["commit_boundary_rng_calls"] = boundaryDraws.Length;
        actual["rng_scope"] = queued ? "actual commit game_proc only; other wake callers remain separately visible in counts"
            : "complete synchronous EndSourcePlay call";
        CaptainInvariant(row, "commit/no-added-boundary-RNG", boundaryDraws.Length == 0
            && (queued || m.SourceRandomDraws == drawsBefore && random.Trace.Count == traceBefore));
        CaptainInvariant(row, "commit/dispatch", m.SourceProcesses.Processes.Select(p => p.Serial).SequenceEqual(processesBefore)
            && m.SourceProcesses.Require(GameSimulation.GameProcess).LastWakeTick == game.SourceTick
            && (queued ? m.SourceProcesses.SourceTick == sourceBefore + 1 : m.SourceDispatchOrdinal == dispatchBefore));
        CaptainInvariant(row, "commit/compatibility", game.Compatibility == mode && committed.Compatibility == mode
            && m.SourceCompatibilityRepairCount == repairsBefore);

        long commitTick = game.SourceTick, commitEpoch = game.C2State.EndEpoch;
        Bind<Action>(game, "Publish")();
        actual["later_publish"] = CaptainObservation(game);
        CaptainInvariant(row, "later/Publish-cannot-toggle", Match(game).Possession.Captain == expectedCaptain
            && game.SourceTick == commitTick && Match(game).Result.Kind == result.Kind
            && game.SourceEvents.Count == (queued ? 0 : sourceEventsBefore) + sourceBatch.Length);
        var wakes = new List<Dictionary<string, object>>();
        bool noReplay = true;
        for (int i = 0; i < 4; i++)
        {
            game.Step(default(PlayerCommand)); wakes.Add(CaptainObservation(game));
            noReplay &= Match(game).Possession.Captain == expectedCaptain && !game.C2State.ResultPending
                && game.C2State.EndEpoch == commitEpoch && game.SourceEvents.All(e => e.Type != SourceEventType.PlayResolved)
                && game.C2Events.All(e => e.Kind != SourceC2EventKind.ResultCommitted);
        }
        actual["later_host_wakes"] = wakes;
        CaptainInvariant(row, "later/host-source-wakes-cannot-replay", noReplay && game.SourceTick > commitTick);
        game.Step(new PlayerCommand(action: InputAction.Continue)); game.Step(default(PlayerCommand));
        actual["continue"] = CaptainObservation(game);
        actual["continue_kickoff_policy"] = boundary.NextType == SourcePlayType.Kickoff
            ? "independent PrepareSourceKickoff: resolved side equals configured first possession, so its captain adjustment is ineligible"
            : "ordinary non-kickoff Continue into play selection";
        CaptainInvariant(row, "later/public-Continue", Match(game).Possession.Captain == expectedCaptain
            && game.State.Phase == (boundary.NextType == SourcePlayType.Kickoff ? GamePhase.PreSnap : GamePhase.PlaySelect)
            && !game.C2State.ResultPending && game.C2State.EndEpoch == commitEpoch
            && game.SourceEvents.All(e => e.Type != SourceEventType.PlayResolved));

        actual["consumer"] = null;
        if (boundary.Toggle)
        {
            var selections = new List<SourceStationInput>();
            for (int side = 0; side < 2; side++)
            {
                bool offense = side == boundary.ResolvedOffense;
                var selection = new SourcePlaySelection(offense ? "play_spider" : "play_safe_cover",
                    offense ? SourcePlaybookKind.Offense : SourcePlaybookKind.Defense);
                selections.Add(new SourceStationInput(side * 2 + (int)expectedCaptain,
                    command: new SourceCommand(SourceAction.SelectPlay, play: selection)));
            }
            game.Step(new PlayerCommand(stationInputs: new SourceInputFrame(selections))); game.Step(default(PlayerCommand));
            actual["consumer"] = new Dictionary<string, object> {
                ["path"] = "public station selection -> FinishSourcePlaySelection -> LineUpSourceActors -> ConfigureSourceStations",
                ["state"] = CaptainObservation(game),
                ["stations"] = Match(game).Stations.Select(s => new Dictionary<string, object> {
                    ["station"] = s.StationId, ["actor"] = s.ControlledPlayerId,
                    ["control"] = (int)m.SourceFormationSlots[s.ControlledPlayerId].Control }).ToArray()
            };
            CaptainInvariant(row, "consumer/actual-next-play-selection", game.State.Phase == GamePhase.PreSnap
                && Match(game).Phase == SourceMatchPhase.PreSnap && m.SourcePlaySelectionReady.All(ready => ready)
                && m.SourceOffensePlay.Identity == "play_spider" && m.SourceDefensePlay.Identity == "play_safe_cover"
                && Match(game).Possession.Captain == expectedCaptain);
            bool controls = true;
            foreach (SourceStationView station in Match(game).Stations)
            {
                int actor = station.ControlledPlayerId;
                SourceFormationControl required = station.StationId % 2 == (int)expectedCaptain
                    ? SourceFormationControl.Captain : SourceFormationControl.SecondPlayer;
                controls &= actor >= 0 && actor / SliceRules.PlayersPerTeam == station.StationId / 2
                    && (m.SourceFormationSlots[actor].Control & required) != 0
                    && m.Players[actor].SourceControllerStation == station.StationId;
            }
            CaptainInvariant(row, "consumer/actual-captain-and-second-player-control", controls && Match(game).Stations.Count == 4
                && Match(game).Stations.Select(s => s.ControlledPlayerId).Distinct().Count() == 4);
        }
        Bind<Action>(game, "Publish")();
        actual["later"] = CaptainObservation(game);
        CaptainInvariant(row, "later/final-publication-no-double-toggle", Match(game).Possession.Captain == expectedCaptain
            && game.C2State.EndEpoch == commitEpoch && !game.C2State.ResultPending
            && game.SourceEvents.All(e => e.Type != SourceEventType.PlayResolved));
        int expectedAssertions = (queued ? 25 : 22) + (boundary.Toggle ? 2 : 0);
        if ((int)row["assertions"] != expectedAssertions)
            throw new InvalidOperationException("Captain per-case assertion partition changed: " + id);
    }

    private static void Captains(string[] args)
    {
        FullKernelInputs contact = FullKernelInputs.Load(args[0], soundFunctionsPath: args[1]);
        CaptainCheck(contact.IsContactProfile && !contact.IsC2Profile && inputs.IsC2M1Profile,
            "captain/input-generations/actual-Contact272-and-M1-without-aliasing");
        foreach (string profile in new[] { "C1-Strict", "M1-Strict", "M1-Portable" })
            foreach (TeamId first in new[] { TeamId.Home, TeamId.Away })
                foreach (SourceCaptainSlot initial in new[] { SourceCaptainSlot.First, SourceCaptainSlot.Second })
                    foreach (string scenario in captainScenarios)
                        CaptainCase(profile == "C1-Strict" ? contact : inputs, profile,
                            profile == "M1-Portable" ? SourceGameplayCompatibility.Portable : SourceGameplayCompatibility.StrictSource,
                            first, initial, scenario);
        CaptainCheck(captainCases.Count == 72 && captainCases.Distinct().Count() == 72
            && cases.Concat(captainCases).Distinct().Count() == 143,
            "captain/inventory/exact-72-added-and-143-union");
        if (captainAssertions != CaptainAssertionCount)
            throw new InvalidOperationException("The frozen additional1778 assertions are required.");
    }

    private static int Main(string[] args)
    {
        var result = new Dictionary<string, object> {
            ["schema"] = "source-match-ended-caller-checks/v1",
            ["status"] = "error", ["whole_game_or_native_qualified"] = false,
            ["declared_boundary_cases_not_public_full_match"] = true,
            ["compiled_production_identity"] = new Dictionary<string, object> {
                ["assembly"] = typeof(GameSimulation).Assembly.FullName,
                ["location"] = typeof(GameSimulation).Assembly.Location,
                ["module"] = typeof(GameSimulation).Assembly.ManifestModule.Name,
                ["mvid"] = typeof(GameSimulation).Assembly.ManifestModule.ModuleVersionId.ToString("D"),
                ["same_assembly_as_harness"] = typeof(GameSimulation).Assembly == typeof(Program).Assembly
            }
        };
        try
        {
            if (args.Length != 3) throw new ArgumentException("Expected fixture.json, sound-functions.safn and actual M1 input directory.");
            inputs = FullKernelInputs.Load(args[0], soundFunctionsPath: args[1], c2M1InputsPath: args[2]);
            Check(inputs.IsC2M1Profile, "terminal checks require the actual M1 composition");
            foreach (SourceGameplayCompatibility mode in new[] { SourceGameplayCompatibility.StrictSource, SourceGameplayCompatibility.Portable })
            {
                Nonterminal(mode, 3, 7, 0, 0x3c00);
                for (int quarter = 0; quarter < 3; quarter++) Nonterminal(mode, quarter, 7, 0);
                for (int quarter = 3; quarter < 6; quarter++) Nonterminal(mode, quarter, 14, 14);
                for (int quarter = 3; quarter <= 6; quarter++) Nonterminal(mode, quarter, 7, 0, kind: SourcePlayType.ExtraPoint);
                for (int quarter = 3; quarter <= 6; quarter++)
                {
                    Terminal(mode, quarter, 7, 0, true, SourcePeriodStatus.MatchEnded);
                    Terminal(mode, quarter, 0, 14, true, SourcePeriodStatus.MatchEnded);
                }
                Terminal(mode, 6, 14, 14, true, SourcePeriodStatus.MatchEnded);
                for (int quarter = 0; quarter < 6; quarter++)
                    Terminal(mode, quarter, 14, 14, false, SourcePeriodStatus.CabinetContinuationDeclined);
                Terminal(mode, 3, 7, 0, false, SourcePeriodStatus.MatchEnded);
                Bridge(mode, true); Bridge(mode, false); Producer(mode);
                Pat(mode, 3, 8, 6, true); Pat(mode, 6, 7, 6, true);
                Pat(mode, 3, 6, 8, true); Pat(mode, 3, 7, 6, false);
            }
            Legacy();
            Check(cases.Count == 71 && cases.Distinct().Count() == 71,
                "terminal caller suite retains all71 declared source, bridge, PAT and legacy cases");
            Captains(args);
            result["status"] = "passed";
        }
        catch (LifecycleAssertion error) { result["status"] = "failed"; result["failed_assertion"] = error.Message; }
        catch (Exception error) when (error is ArgumentException || error is InvalidOperationException
            || error is NotSupportedException || error is IOException)
        { result["error"] = error.ToString(); Console.Error.WriteLine(error); }
        result["retained"] = new Dictionary<string, object> { ["assertions"] = assertions, ["cases"] = cases };
        result["captain_transition"] = new Dictionary<string, object> {
            ["schema"] = "source-post-result-captain-checks/v1", ["contract"] = "source-post-result-captain-transition/v1",
            ["assertions"] = captainAssertions, ["expected_assertions"] = CaptainAssertionCount,
            ["cases"] = captainCases, ["records"] = captainRows,
            ["qualifying_cases"] = 24, ["guard_negative_cases"] = 48,
            ["whole_game_or_native_qualified"] = false
        };
        result["assertions"] = assertions + captainAssertions; result["cases"] = cases.Concat(captainCases).ToArray();
        Console.WriteLine(JsonSerializer.Serialize(result));
        return (string)result["status"] == "passed" ? 0 : 1;
    }
}
#endif

#if SOURCE_RULE_TRANSITIONS_FULL_PLAYER
internal sealed class SourceRuleTransitionChecks
{
    private sealed class Context
    {
        internal readonly string Id;
        internal GameSimulation Game;
        internal readonly List<object> Trace = new List<object>();
        internal readonly List<object> Assertions = new List<object>();
        internal readonly List<SourceGameEvent> Events = new List<SourceGameEvent>();
        internal readonly Dictionary<long, Dictionary<string, object>> Camera = new Dictionary<long, Dictionary<string, object>>();
        internal int RandomStart;
        internal int RandomObserved;
        internal bool Capture;
        internal Context(string id) { Id = id; }
    }

    private readonly PlayerData data;
    private readonly string originalExecutable;
    private readonly List<object> rows = new List<object>();
    private int failures, assertions;
    private string progressPath = "";

    private SourceRuleTransitionChecks(PlayerData data, string originalExecutable)
    { this.data = data; this.originalExecutable = originalExecutable; }

    private static int Int(JsonElement value, string name) { return value.GetProperty(name).GetInt32(); }
    private static float Float(JsonElement value, string name) { return value.GetProperty(name).GetSingle(); }
    private static uint Bits(float value) { return BitConverter.ToUInt32(BitConverter.GetBytes(value), 0); }
    private static float FromBits(uint value) { return BitConverter.ToSingle(BitConverter.GetBytes(value), 0); }

    private void Equal<T>(Context context, string field, T expected, T actual)
    {
        assertions++;
        context.Assertions.Add(new { field, expected, actual });
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException(context.Id + "/" + field + ": expected " + expected + ", observed " + actual);
    }

    private Dictionary<string, object> Observe(Context context, string stage)
    {
        GameSimulation game = context.Game;
        SimulationState m = game.Model;
        SourceInjectedRandom random = (SourceInjectedRandom)game.SourceRandomService;
        int randomFrom = Math.Max(context.RandomStart, context.RandomObserved);
        var snapshot = new Dictionary<string, object> {
            ["stage"] = stage, ["source_tick"] = game.SourceTick, ["host_tick"] = game.State.Tick,
            ["game_proc_wake"] = m.SourceProcesses.Require(GameSimulation.GameProcess).LastWakeTick,
            ["pending_result"] = game.C2State.ResultPending, ["phase"] = m.Phase.ToString(),
            ["quarter_index"] = m.Quarter - 1, ["match_phase"] = m.SourceMatchPhase.ToString(),
            ["cabinet_stage"] = m.SourceCabinetFlow == null ? "absent" : m.SourceCabinetFlow.Stage.ToString(),
            ["carrier"] = m.CarrierId, ["last_carrier"] = m.SourceLastCarrierId,
            ["off_side"] = m.SourceRules.OffenseSide, ["off_init"] = m.SourceRules.InitialOffenseSide,
            ["last_pos"] = m.SourceLastPossession, ["down"] = m.SourceRules.Down,
            ["los"] = m.SourceRules.LineOfScrimmage, ["first_down"] = m.SourceRules.FirstDown,
            ["play_type"] = (int)m.SourceRules.PlayType, ["play_result"] = (int)m.SourceLastResult,
            ["game_flags"] = (int)m.SourceFlags, ["play_end_pos_bits"] = Bits(m.SourcePlayEndX),
            ["line_yards_bits"] = Bits(m.SourceLinePosition), ["captain"] = (int)m.SourceCaptain,
            ["off_turnover"] = m.SourceOffenseTurnover ? 1 : 0,
            ["just_got_1st"] = m.SourceResultView.JustGotFirstDown ? 1 : 0,
            ["clock_active"] = m.ClockActive ? 1 : 0, ["packed"] = m.SourceClock.PackedTime,
            ["local_clock"] = m.SourceClock.LocalPackedTime, ["play_time"] = m.SourceClock.PlayTime,
            ["clock_process_alive"] = m.SourceProcesses.Contains(GameSimulation.ClockProcess),
            ["clock_process_complete"] = m.SourceClock.ProcessComplete,
            ["home_score"] = m.HomeScore, ["away_score"] = m.AwayScore,
            ["ball"] = new[] { m.SourceBallPosition.X, m.SourceBallPosition.Y, m.SourceBallPosition.Z },
            ["events"] = game.SourceEvents.Select(e => new {
                type = e.Type.ToString(), e.Tick, e.SourceTick, actor = e.ActorPlayerId,
                result = e.Result.IsSpecified ? e.Result.Kind.ToString() : "none"
            }).ToArray(),
            ["process_records"] = m.SourceProcesses.Records.Select(r => new {
                r.Operation, r.Serial, r.Id, r.Value, r.SourceTick
            }).ToArray(),
            ["rng_from"] = randomFrom, ["rng_to"] = random.Trace.Count,
            ["rng"] = random.Trace.Skip(randomFrom)
                .Select(d => new {
                    d.Order, d.Raw, d.Value, d.Site.SourceTick, d.Site.Process, d.Site.ActorId,
                    d.Site.SourceIdentity, d.Site.BoundOrModulus, kind = d.Site.Kind.ToString()
                }).ToArray()
        };
        context.RandomObserved = random.Trace.Count;
        context.Trace.Add(snapshot);
        return snapshot;
    }

    private GameSimulation Create(Context context, int first = 0, int coin = 0, int? cpuOffense = null,
        int goDraw = 99, int freeDraw = 99, int timeAdjustment = 2)
    {
        SourceMatchConfiguration basis = data.Configuration.Match;
        SourceStationConfiguration[] stations = cpuOffense.HasValue
            ? new[] { new SourceStationConfiguration((1 - cpuOffense.Value) * 2, (TeamId)(1 - cpuOffense.Value)) }
            : new[] { new SourceStationConfiguration(0, TeamId.Home), new SourceStationConfiguration(1, TeamId.Home),
                new SourceStationConfiguration(2, TeamId.Away), new SourceStationConfiguration(3, TeamId.Away) };
        var configuration = new SourceMatchConfiguration(SourceSimulationProfile.OriginalRelease57Hz,
            SourceStationLayout.FourStation, basis.HomeTeam, basis.AwayTeam,
            stations, (TeamId)first, SourceCaptainSlot.First, timeAdjustment: timeAdjustment);
        var banks = Enumerable.Range(0, 31).Select(data.Rosters.GetSourceSoundBankName).ToArray();
        Func<int, ISourceRandom> random = seed => new SourceInjectedRandom(
            site => site.SourceIdentity == "SourceMatchFlow.OvertimeKickoff" ? coin
                : site.SourceIdentity == "SourceMatchFlow.GoForTwo" ? goDraw
                : site.SourceIdentity == "SourceMatchFlow.FreePat" ? freeDraw : 99,
            new SourceRandomProvenance("source-rule-transitions/explicit-raw-tape", "Declared caller inputs, not target PRNG",
                "coin=" + coin + "/go=" + goDraw + "/free=" + freeDraw + "/other=99",
                SourceRandomEvidence.ExternalUnverified));
        GameSimulation game = data.Catalog.Profile.IsCompleteAv306
            ? SourceC2Composition.CreateCompleteAv306Simulation(data.Animations,
                data.ReferencePivots, data.Playbook, configuration, data.Rosters, data.Configuration.Presentation,
                data.Movement, data.MovementSettings, data.Sound,
                SourceC2Bootstrap.FromCompleteAv306VersusSoundBanks(data.Callbacks, banks), randomFactory: random,
                compatibility: SourceGameplayCompatibility.Portable, defaultAudibles: data.DefaultAudibles,
                impactLibrary: data.Impact, courseCamera: data.CourseCamera)
            : SourceC2Composition.CreateCompleteOriginalPrematchSimulation(data.Animations,
            data.ReferencePivots, data.Playbook, configuration, data.Rosters, data.Configuration.Presentation,
            data.Movement, data.MovementSettings, data.Sound,
            SourceC2Bootstrap.FromCompleteOriginalVersusSoundBanks(data.Callbacks, banks),
            randomFactory: random,
            compatibility: SourceGameplayCompatibility.Portable,
            defaultAudibles: data.DefaultAudibles, impactLibrary: data.Impact);
        context.Game = game;
        int participation = stations.Aggregate(0, (mask, station) => mask | 1 << station.StationId);
        game.BindSourceCabinetFlow(new SourceCabinetFlowConfiguration(participation,
            new SourceCabinetButtonMasks(0xf0, 0x70, 0xf0f0f0f0u),
            SourceCabinetContinuationPolicy.KeepParticipatingStations,
            SourceCabinetWaitInitialization.ZeroCountersOnEntry));
        game.SourceCameraHeadingProvider = (state, tick) => {
            if (context.Capture) context.Camera[tick] = Observe(context, "CAMBOT-after-game_proc-before-actors");
            return FullKernelInputs.SetControlledMovementCamera(game, state, tick);
        };
        for (int host = 0; host < 60; host++) game.Step(ConfiguredCommand(game, Command()));
        Equal(context, "startup/actual-57-source-wakes", 57L, game.SourceTick);
        Equal(context, "startup/natural-host-remainder", 0, game.Model.SourceClockRemainder);
        context.RandomStart = ((SourceInjectedRandom)game.SourceRandomService).Trace.Count;
        context.Capture = true;
        return game;
    }

    private static PlayerCommand Command(SourceButtons held = SourceButtons.None, SourceCommand source = default(SourceCommand))
    {
        var stations = Enumerable.Range(0, 4).Select(id => new SourceStationInput(id,
            held: held, digitalCurrent: SourceDigitalDirections.None)).ToArray();
        return new PlayerCommand(source: source,
            stationInputs: new SourceInputFrame(stations, new SourcePhysicalSwitchFrame(stations)));
    }

    private static PlayerCommand ConfiguredCommand(GameSimulation game, PlayerCommand command)
    {
        int mask = game.Model.SourceConfiguration.Stations.Aggregate(0, (bits, station) => bits | 1 << station.StationId);
        SourceStationInput[] physical = command.StationInputs.Stations.ToArray();
        return new PlayerCommand(command.Move, command.Turbo, command.Action, command.Play,
            command.TargetPlayerId, command.Source, new SourceInputFrame(
                physical.Where(station => (mask & 1 << station.StationId) != 0).ToArray(),
                new SourcePhysicalSwitchFrame(physical)));
    }

    private void Wake(Context context, PlayerCommand command = default(PlayerCommand))
    {
        GameSimulation game = context.Game;
        long before = game.SourceTick;
        if (command.StationInputs == null) command = Command(source: command.Source);
        game.Step(ConfiguredCommand(game, command));
        if (game.SourceTick == before) game.Step(ConfiguredCommand(game, Command()));
        Equal(context, "source-wake/exactly-one", before + 1, game.SourceTick);
        context.Events.AddRange(game.SourceEvents);
    }

    private static SourceBallKind BallKind(int kind)
    {
        switch (kind)
        {
            case 0: return SourceBallKind.Pass;
            case 1: return SourceBallKind.Fumble;
            case 2: return SourceBallKind.Punt;
            case 3: return SourceBallKind.Kickoff;
            case 4: return SourceBallKind.FieldGoal;
            case 5: return SourceBallKind.Bobble;
            case 6: return SourceBallKind.Lateral;
            default: throw new ArgumentOutOfRangeException(nameof(kind));
        }
    }

    private void Declare(Context context, JsonElement input)
    {
        GameSimulation game = context.Game;
        SimulationState m = game.Model;
        int side = Int(input, "offense_side"), quarter = Int(input, "quarter_index");
        m.Phase = GamePhase.LiveBall; m.SourceMatchPhase = SourceMatchPhase.LiveBall;
        m.Quarter = quarter + 1; m.SourcePeriodView = new SourcePeriodView(quarter);
        m.SourceRules = new SourceRulesState(side, Int(input, "initial_side"), Int(input, "down"),
            Int(input, "los"), Int(input, "first_down"), (SourcePlayType)Int(input, "play_type"),
            Int(input, "no_first_down") != 0);
        m.Possession = (TeamId)side; m.SourceLastPossession = Int(input, "last_possession");
        m.Down = m.SourceRules.Down;
        m.LineOfScrimmage = SourceMatchRules.AbsoluteYardsFromSourceWorld(
            SourceMatchRules.WorldPosition(m.SourceRules.LineOfScrimmage, side));
        m.FirstDownLine = SourceMatchRules.AbsoluteYardsFromSourceWorld(
            SourceMatchRules.WorldPosition(m.SourceRules.FirstDown, side));
        m.SourceOffensePlay = data.Playbook.GetPlay("play_zigzag");
        m.SourceDefensePlay = data.Playbook.GetPlay("play_safe_cover");
        m.SourceFlags = (SourceGameFlags)Int(input, "flags");
        m.SourceLastResult = (SourcePlayResult)Int(input, "previous_result");
        m.SourceResultView = default(SourceMatchResult); m.SourceOffenseTurnover = false;
        m.HomeScore = Int(input, "home_score"); m.AwayScore = Int(input, "away_score");
        Array.Clear(m.SourceQuarterScores, 0, m.SourceQuarterScores.Length);
        m.SourceQuarterScores[quarter, 0] = m.HomeScore; m.SourceQuarterScores[quarter, 1] = m.AwayScore;
        m.SourceClock = SourceClock.Create(0x13b07, firstWakeTick: game.SourceTick + 1);
        m.ClockActive = m.SourceRules.PlayType != SourcePlayType.ExtraPoint;
        m.SourceBeginPlayInitialized = true; m.SourceForwardCatcher = -1;
        m.SourcePlayEndX = Float(input, "play_end_x"); m.SourcePlayEndZ = Float(input, "play_end_z");
        m.SourceFumbleX = Float(input, "fumble_x"); m.SourceLinePosition = 0f;
        m.CatchSourceTick = game.SourceTick - Int(input, "held_ticks");
        m.CarrierId = side * 7; m.ThrowerId = m.TargetPlayerId = -1;
        m.BallMode = BallMode.Held; m.BallKind = BallKind(Int(input, "ball_type"));
        m.SourceBallFlags = SourceBallFlags.None;
        foreach (ActorState actor in m.Players)
        {
            actor.SourceMode = actor.Id == m.CarrierId ? SourceActorMode.Receiver : SourceActorMode.Dead;
            actor.SourceButtons = actor.SourceOldButtons = actor.SourceNewButtons = SourceDroneButtons.None;
            actor.SourceVelocity = default(FieldVector); actor.SourceVerticalVelocity = 0f;
            actor.SourcePlayOver = false; actor.SourceInPlay = true;
            SourcePlayerDriver.SetSourcePosition(actor, new SourceVector3(actor.Id == m.CarrierId
                ? Float(input, "actor_x") : 1000f + actor.Id * 100f, 0f,
                actor.Id == m.CarrierId ? Float(input, "actor_z") : 800f));
        }
        string trigger = input.GetProperty("trigger").GetString();
        if (trigger == "pass-ground" || trigger == "ball-oob")
        {
            foreach (ActorState actor in m.Players) actor.SourceMode = SourceActorMode.Dead;
            m.CarrierId = -1; m.BallMode = BallMode.InFlight; m.PassTicksElapsed = 31; m.PassTicksTotal = 60;
            m.SourceBallVelocity = default(FieldVector); m.SourceBallVerticalVelocity = -0.5f;
            SourceBallDriver.SetPosition(m, new SourceVector3(Float(input, "play_end_x"),
                trigger == "pass-ground" ? 0f : 10f, Float(input, "play_end_z")));
        }
        Observe(context, "declared-inputs-no-tick-rng-or-scheduler-assignment");
    }

    private JsonElement Original(Context context, JsonElement input)
    {
        SimulationState m = context.Game.Model;
        object[] values = {
            (int)m.SourceRules.PlayType, (int)m.SourceLastPlayEndCause, Int(input, "ball_type"),
            (int)m.SourceFlags, m.SourceRules.OffenseSide, m.SourceRules.InitialOffenseSide,
            m.SourceLastPossession, m.SourceRules.Down, m.SourceRules.LineOfScrimmage, m.SourceRules.FirstDown,
            m.SourcePlayEndX.ToString("R", CultureInfo.InvariantCulture),
            m.SourcePlayEndZ.ToString("R", CultureInfo.InvariantCulture),
            m.SourceFumbleX.ToString("R", CultureInfo.InvariantCulture),
            m.SourceRules.NoFirstDown ? 1 : 0, (int)m.SourceLastResult, m.Quarter - 1,
            m.HomeScore, m.AwayScore, m.SourceFirstPossession, (int)m.SourceCaptain, m.SourceClock.PackedTime
        };
        string command = "resolve " + string.Join(" ", values.Select(v => Convert.ToString(v, CultureInfo.InvariantCulture)));
        return OriginalCommand(context, command);
    }

    private JsonElement OriginalCommand(Context context, string command)
    {
        var start = new ProcessStartInfo(originalExecutable) {
            UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true
        };
        using (Process process = Process.Start(start))
        {
            process.StandardInput.WriteLine(command);
            process.StandardInput.Close();
            string text = process.StandardOutput.ReadToEnd(), error = process.StandardError.ReadToEnd();
            if (!process.WaitForExit(60000) || process.ExitCode != 0)
                throw new InvalidOperationException("Actual original-C caller failed: " + error);
            using (JsonDocument result = JsonDocument.Parse(text))
            {
                context.Trace.Add(new { stage = "independent-original-C-before-commit",
                    command, expected = result.RootElement.Clone() });
                return result.RootElement.Clone();
            }
        }
    }

    private void ClockLifetime(JsonElement input, bool restart)
    {
        var context = new Context(restart ? "T1/portable-clock/restart-preserves-global-counter-and-creates-process"
            : "T1/portable-clock/return-retires-actual-process");
        string status = "passed", error = "";
        try
        {
            GameSimulation game = Create(context);
            Declare(context, input);
            SimulationState m = game.Model;
            SourcePlayerDriver.SetSourcePosition(m.Players[m.CarrierId], new SourceVector3(0f, 0f, 0f));
            m.SourceClock = SourceClock.Create(1, 17, firstWakeTick: game.SourceTick + 1);
            JsonElement last = OriginalCommand(context, "clock 1 0 17 0 1 2 2 0 0 0 1");
            Wake(context);
            Observe(context, "actual-last-clock-subtick");
            Equal(context, "last-subtick/native-packed-zero", Int(last, "packed"), m.SourceClock.PackedTime);
            Equal(context, "last-subtick/source-sleeps-before-return", false, m.SourceClock.ProcessComplete);
            Equal(context, "last-subtick/process-still-alive", true, m.SourceProcesses.Contains(GameSimulation.ClockProcess));
            Equal(context, "last-subtick/live-play-continues", GamePhase.LiveBall, m.Phase);
            JsonElement returned = OriginalCommand(context, "clock 0 0 17 0 1 2 2 0 0 0 1");
            Equal(context, "original-zero/function-returns-without-sleep", 0, returned.GetProperty("events").GetArrayLength());
            Wake(context);
            Observe(context, "actual-clock-function-return");
            Equal(context, "returned/actual-state-complete", true, m.SourceClock.ProcessComplete);
            if (!restart)
                Equal(context, "returned/process-retired", false, m.SourceProcesses.Contains(GameSimulation.ClockProcess));
            Wake(context);
            if (!restart)
                Equal(context, "returned/no-invented-later-wake", false,
                    m.SourceProcesses.Records.Any(r => r.Serial == GameSimulation.ClockProcess && r.Operation == "Wake"));
            Equal(context, "returned/live-play-still-not-killed", GamePhase.LiveBall, m.Phase);
            if (restart)
            {
                // A declared physical ending boundary, not a naturally walked sideline.
                SourcePlayerDriver.SetSourcePosition(m.Players[m.CarrierId], new SourceVector3(0f, 0f, 190f));
                Wake(context);
                Equal(context, "ending/actual-player-result-queued", true, game.C2State.ResultPending);
                Wake(context);
                Equal(context, "ending/actual-game-proc-result-committed", false, game.C2State.ResultPending);
                int wakes = 0;
                while (m.Quarter == 1 && wakes++ < 2200)
                    Wake(context, Command(SourceButtons.A | SourceButtons.B | SourceButtons.C,
                        new SourceCommand(SourceAction.Continue)));
                Observe(context, "actual-new-quarter-after-real-cabinet-waits");
                Equal(context, "restart/next-quarter-reached", 2, m.Quarter);
                JsonElement initialized = OriginalCommand(context, "clock-init 0 1 17 0 0 2 2 0 0 0 1");
                Equal(context, "restart/native-global-play-time-not-reset", Int(initialized, "play_time"), m.SourceClock.PlayTime);
                Equal(context, "restart/native-packed-initialization", Int(initialized, "packed"), m.SourceClock.PackedTime);
                Equal(context, "restart/actual-clock-process-created", true, m.SourceProcesses.Contains(GameSimulation.ClockProcess));
                Equal(context, "restart/new-process-born-on-transition", game.SourceTick,
                    m.SourceProcesses.Require(GameSimulation.ClockProcess).BirthTick);
            }
        }
        catch (Exception failure)
        {
            failures++; status = "failed"; error = failure.ToString();
            if (context.Game != null) Observe(context, "first-failure");
        }
        rows.Add(new { id = context.Id, status, error, assertions = context.Assertions, trace = context.Trace });
        Console.WriteLine(status + " " + context.Id + (error.Length == 0 ? "" : ": " + error.Split('\n')[0]));
    }

    private void Caller(JsonElement item)
    {
        string id = item.GetProperty("id").GetString();
        var context = new Context(id);
        string status = "passed", error = "";
        try
        {
            JsonElement input = item.GetProperty("input");
            GameSimulation game = Create(context);
            Declare(context, input);
            float oldLos = game.Model.LineOfScrimmage, oldFirst = game.Model.FirstDownLine;
            Wake(context);
            Observe(context, "actual-player-or-ball-proc-queued");
            Equal(context, "producer/actual-C2-result-pending", true, game.C2State.ResultPending);
            Equal(context, "producer/source-cause", Int(input, "cause"), (int)game.Model.SourceLastPlayEndCause);
            JsonElement expected = Original(context, input);
            Wake(context);
            Observe(context, "actual-game-proc-commit-and-later-actors-ball");
            Equal(context, "commit/pending-cleared", false, game.C2State.ResultPending);
            Dictionary<string, object> commit = context.Camera[game.SourceTick];
            foreach (string field in new[] {
                "play_result", "off_side", "off_init", "last_pos", "down", "los", "first_down",
                "play_type", "game_flags", "captain", "off_turnover", "just_got_1st", "clock_active", "packed" })
                Equal(context, "original-C/" + field, Int(expected, field), (int)commit[field]);
            Equal(context, "original-C/play-end-position-bits",
                expected.GetProperty("play_end_pos_bits").GetUInt32(), (uint)commit["play_end_pos_bits"]);
            Equal(context, "original-C/hash-mark-adapter-bits",
                Bits(FromBits(expected.GetProperty("line_pos_bits").GetUInt32()) / 7.8f), (uint)commit["line_yards_bits"]);
            Equal(context, "original-C/home-score", expected.GetProperty("scores")[0].GetInt32(), (int)commit["home_score"]);
            Equal(context, "original-C/away-score", expected.GetProperty("scores")[1].GetInt32(), (int)commit["away_score"]);
            Equal(context, "commit/one-result-event", 1, game.SourceEvents.Count(e => e.Type == SourceEventType.PlayResolved));
            if (Int(expected, "off_turnover") != 0)
            {
                Equal(context, "turnover/preserve-displayed-LOS", Bits(oldLos), Bits(game.Model.LineOfScrimmage));
                Equal(context, "turnover/preserve-displayed-first-down", Bits(oldFirst), Bits(game.Model.FirstDownLine));
            }
        }
        catch (Exception failure)
        {
            failures++; status = "failed"; error = failure.ToString();
            if (context.Game != null) Observe(context, "first-failure");
        }
        rows.Add(new { id, status, error, assertions = context.Assertions, trace = context.Trace });
        Console.WriteLine(status + " " + id + (error.Length == 0 ? "" : ": " + error.Split('\n')[0]));
    }

    private void FreePatTiming()
    {
        var context = new Context("S1-T1/free-PAT/actual-fifteen-tick-award-and-forty-tick-wait");
        string status = "passed", error = "";
        try
        {
            GameSimulation game = Create(context);
            SimulationState m = game.Model;
            m.Phase = GamePhase.DeadBall; m.SourceMatchPhase = SourceMatchPhase.PlayResult;
            m.SourceRules = new SourceRulesState(0, 0, -1, 90, 100, SourcePlayType.ExtraPoint);
            m.Possession = TeamId.Home; m.SourceLastPossession = 0;
            m.SourceBeginPlayInitialized = false; m.HomeScore = 6; m.AwayScore = 0;
            m.SourceQuarterScores[0, 0] = 6; m.SourceQuarterScores[0, 1] = 0;
            m.SourceClock = SourceClock.Create(0x10000, firstWakeTick: game.SourceTick + 1);
            m.ClockActive = false;
            foreach (ActorState actor in m.Players) actor.SourceMode = SourceActorMode.Dead;
            Wake(context, Command(source: new SourceCommand(SourceAction.Continue)));
            Equal(context, "choice/actual-PAT-selection", SourceMatchPhase.PatSelection, m.SourceMatchPhase);
            Wake(context, Command(source: new SourceCommand(SourceAction.SelectPat, patChoice: SourcePatOption.FreeKick)));
            long selected = game.SourceTick;
            Observe(context, "actual-free-PAT-selected-before-original-sleep15");
            Equal(context, "original-playsel-1481/no-immediate-point", 6, m.HomeScore);
            for (int tick = 1; tick < 15; tick++)
            {
                Wake(context);
                Equal(context, "pre-award/no-early-point-" + tick, 6, m.HomeScore);
                Equal(context, "pre-award/selection-stays-suspended-" + tick, GamePhase.PlaySelect, m.Phase);
            }
            Wake(context);
            Observe(context, "actual-original-sleep15-completed-and-point-awarded");
            Equal(context, "award/source-tick", selected + 15, game.SourceTick);
            Equal(context, "award/one-point", 7, m.HomeScore);
            Equal(context, "award/one-result-event", 1, game.SourceEvents.Count(e => e.Type == SourceEventType.FreePointResult));
            Equal(context, "award/not-yet-kickoff", SourcePlayType.ExtraPoint, m.SourceRules.PlayType);
            for (int tick = 1; tick < 40; tick++)
            {
                Wake(context, Command(SourceButtons.A | SourceButtons.B | SourceButtons.C));
                Equal(context, "post-award/native-minimum-wait-" + tick, GamePhase.PlaySelect, m.Phase);
                Equal(context, "post-award/no-duplicate-point-" + tick, 7, m.HomeScore);
            }
            Wake(context, Command(SourceButtons.A | SourceButtons.B | SourceButtons.C));
            Observe(context, "actual-free-PAT-wait-return-before-kickoff");
            Equal(context, "wait-return/source-tick", selected + 55, game.SourceTick);
            Equal(context, "wait-return/source-kickoff", SourcePlayType.Kickoff, m.SourceRules.PlayType);
            Equal(context, "wait-return/no-duplicate-point", 7, m.HomeScore);
        }
        catch (Exception failure)
        {
            failures++; status = "failed"; error = failure.ToString();
            if (context.Game != null) Observe(context, "first-failure");
        }
        rows.Add(new { id = context.Id, status, error, assertions = context.Assertions, trace = context.Trace });
        Console.WriteLine(status + " " + context.Id + (error.Length == 0 ? "" : ": " + error.Split('\n')[0]));
    }

    private void Scenario(string id, Action<Context> action)
    {
        var context = new Context(id);
        string status = "passed", error = "";
        WriteProgress(id, "running");
        try { action(context); }
        catch (Exception failure)
        {
            failures++; status = "failed"; error = failure.ToString();
            if (context.Game != null) Observe(context, "first-failure");
        }
        rows.Add(new { id, status, error, assertions = context.Assertions, trace = context.Trace });
        WriteProgress(id, status);
        Console.WriteLine(status + " " + id + (error.Length == 0 ? "" : ": " + error.Split('\n')[0]));
    }

    private void DeclarePeriod(Context context, int quarter, int home, int away, int packed = 1,
        SourcePlayType type = SourcePlayType.Normal, int offense = 0, int initial = 0,
        SourceGameFlags flags = SourceGameFlags.None)
    {
        SimulationState m = context.Game.Model;
        m.Quarter = quarter + 1; m.SourcePeriodView = new SourcePeriodView(quarter);
        m.HomeScore = home; m.AwayScore = away;
        Array.Clear(m.SourceQuarterScores, 0, m.SourceQuarterScores.Length);
        m.SourceQuarterScores[quarter, 0] = home; m.SourceQuarterScores[quarter, 1] = away;
        m.SourceRules = new SourceRulesState(offense, initial, type == SourcePlayType.ExtraPoint ? -1 : 2,
            type == SourcePlayType.ExtraPoint ? 90 : 50, type == SourcePlayType.ExtraPoint ? 100 : 80, type);
        m.Possession = (TeamId)offense; m.SourceLastPossession = offense;
        m.SourceOffensePlay = data.Playbook.GetPlay("play_zigzag");
        m.SourceDefensePlay = data.Playbook.GetPlay("play_safe_cover");
        m.SourceBeginPlayInitialized = true; m.SourceFlags = flags; m.SourceForwardCatcher = -1;
        m.SourceLastResult = SourcePlayResult.Down; m.SourceResultView = default(SourceMatchResult);
        m.SourceClock = SourceClock.Create(packed, firstWakeTick: m.SourceTick + 1);
        m.ClockActive = type != SourcePlayType.ExtraPoint;
        m.Phase = GamePhase.LiveBall; m.SourceMatchPhase = SourceMatchPhase.LiveBall;
        m.CarrierId = offense * 7; m.BallMode = BallMode.Held; m.BallKind = SourceBallKind.Pass;
        m.SourceBallFlags = SourceBallFlags.None; m.CatchSourceTick = m.SourceTick - 19;
        m.SourcePlayEndX = m.SourcePlayEndZ = 0f; m.QuarterEndEmitted = false;
        foreach (ActorState actor in m.Players)
        {
            actor.SourceMode = actor.Id == m.CarrierId ? SourceActorMode.Receiver : SourceActorMode.Dead;
            actor.SourceVelocity = default(FieldVector); actor.SourceVerticalVelocity = 0f;
            actor.SourceButtons = actor.SourceOldButtons = actor.SourceNewButtons = SourceDroneButtons.None;
            actor.SourcePlayOver = false; actor.SourceInPlay = true;
            SourcePlayerDriver.SetSourcePosition(actor, new SourceVector3(actor.Id == m.CarrierId ? 0f : 1000f + actor.Id * 100f,
                0f, actor.Id == m.CarrierId ? 0f : 800f));
        }
        Observe(context, "declared-period-inputs-before-actual-source-step");
    }

    private void ExpireLive(Context context)
    {
        int quarter = context.Game.Model.Quarter;
        int subticks = context.Game.Model.SourceClock.PackedTime;
        if (subticks < 1 || subticks > 2)
            throw new InvalidOperationException("The declared live-expiry fixture requires one or two original subticks.");
        for (int tick = 0; tick < subticks; tick++) Wake(context);
        Equal(context, "expiry/native-subtick-bound", 0, context.Game.Model.SourceClock.PackedTime);
        Observe(context, "actual-clock-expired-during-live-play");
        Equal(context, "expiry/live-play-not-killed", GamePhase.LiveBall, context.Game.Model.Phase);
        Equal(context, "expiry/period-not-advanced", quarter, context.Game.Model.Quarter);
        Equal(context, "expiry/no-terminal-before-play", 0,
            context.Game.SourceEvents.Count(e => e.Type == SourceEventType.MatchEnded));
    }

    private void FinishLocation(Context context, float x = 0f, float z = 190f)
    {
        SimulationState m = context.Game.Model;
        SourcePlayerDriver.SetSourcePosition(m.Players[m.CarrierId], new SourceVector3(x, 0f, z));
        Wake(context);
        Observe(context, "actual-player-proc-ending-boundary");
        Equal(context, "ending/actual-queued-result", true, context.Game.C2State.ResultPending);
        Wake(context);
        Equal(context, "ending/actual-game-proc-commit", false, context.Game.C2State.ResultPending);
    }

    private void Drain(Context context)
    {
        int wakes = 0, quarter = context.Game.Model.Quarter;
        while (wakes < 2200)
        {
            GameSimulation game = context.Game;
            SourceCabinetFlowView flow = ((ISourceCabinetFlowState)game.State).CabinetFlow;
            bool freeWait = game.Model.Phase == GamePhase.PlaySelect && flow != null
                && flow.Stage == SourceCabinetFlowStage.ResultWait;
            if (game.Model.Quarter != quarter || game.Model.Phase == GamePhase.GameOver || game.Model.Phase == GamePhase.PreSnap
                || game.Model.Phase == GamePhase.PlaySelect && !freeWait) return;
            Wake(context, Command(SourceButtons.A | SourceButtons.B | SourceButtons.C));
            wakes++;
        }
        throw new InvalidOperationException("The existing2200-source-wake cabinet bound was exceeded.");
    }

    private void ContinuePeriod(Context context)
    {
        Wake(context, Command(SourceButtons.A | SourceButtons.B | SourceButtons.C,
            new SourceCommand(SourceAction.Continue)));
        Drain(context);
        Observe(context, "actual-cabinet-result-period-continuation-completed");
    }

    private void PatCase(JsonElement item)
    {
        string id = item.GetProperty("id").GetString();
        Scenario(id, context => {
            JsonElement input = item.GetProperty("input"), expected = item.GetProperty("expected");
            int side = Int(input, "side"), quarter = Int(input, "quarter"), packed = Int(input, "packed");
            bool human = Int(input, "human") != 0;
            GameSimulation game = Create(context, cpuOffense: human ? (int?)null : side,
                goDraw: Int(input, "go_draw"), freeDraw: Int(input, "free_draw"));
            int home = side == 0 ? Int(input, "offense_score") : Int(input, "defense_score");
            int away = side == 1 ? Int(input, "offense_score") : Int(input, "defense_score");
            DeclarePeriod(context, quarter, home, away, packed, SourcePlayType.ExtraPoint, side, side);
            SimulationState m = game.Model;
            m.Phase = GamePhase.DeadBall; m.SourceMatchPhase = SourceMatchPhase.PlayResult;
            m.SourceBeginPlayInitialized = false;
            ContinuePeriod(context);
            bool skipped = expected.GetProperty("skip").GetBoolean(), two = expected.GetProperty("two").GetBoolean();
            if (!skipped)
            {
                if (human)
                    Wake(context, Command(source: new SourceCommand(SourceAction.SelectPat,
                        patChoice: Int(input, "choice") == 2 ? SourcePatOption.TwoPointPlay : SourcePatOption.FreeKick)));
                else Wake(context);
                if (two)
                {
                    if (human)
                        Wake(context, Command(source: new SourceCommand(SourceAction.SelectPlay,
                            new SourcePlaySelection("play_zigzag", SourcePlaybookKind.Offense))));
                    Wake(context, Command(source: new SourceCommand(SourceAction.SelectPlay,
                        new SourcePlaySelection("play_safe_cover", SourcePlaybookKind.Defense))));
                    Equal(context, "native-two/actual-pre-snap", GamePhase.PreSnap, m.Phase);
                    Equal(context, "native-two/conversion-still-required", SourcePlayType.ExtraPoint, m.SourceRules.PlayType);
                }
                else
                {
                    long selected = game.SourceTick;
                    Equal(context, "native-delay/points-not-awarded-at-choice", home, m.HomeScore);
                    Equal(context, "native-delay/other-points-not-awarded-at-choice", away, m.AwayScore);
                    for (int tick = 1; tick < Int(expected, "delay"); tick++) Wake(context);
                    Equal(context, "native-delay/score-before-final-sleep-wake", home, m.HomeScore);
                    Wake(context);
                    Equal(context, "native-delay/actual-award-tick", selected + Int(expected, "delay"), game.SourceTick);
                    Equal(context, "native-award/home", Int(expected, "home"), m.HomeScore);
                    Equal(context, "native-award/away", Int(expected, "away"), m.AwayScore);
                    Equal(context, "native-award/one-result", 1, game.SourceEvents.Count(e => e.Type == SourceEventType.FreePointResult));
                    for (int tick = 1; tick < Int(expected, "wait_min"); tick++)
                    {
                        Wake(context, Command(SourceButtons.A | SourceButtons.B | SourceButtons.C));
                        Equal(context, "native-post-wait/still-selection-" + tick, GamePhase.PlaySelect, m.Phase);
                    }
                    Wake(context, Command(SourceButtons.A | SourceButtons.B | SourceButtons.C));
                    Equal(context, "native-post-wait/first-allowed-return",
                        selected + Int(expected, "delay") + Int(expected, "wait_min"), game.SourceTick);
                    Drain(context);
                }
            }
            Equal(context, "native-final/home", Int(expected, "home"), m.HomeScore);
            Equal(context, "native-final/away", Int(expected, "away"), m.AwayScore);
            var draws = ((SourceInjectedRandom)game.SourceRandomService).Trace.Where(d =>
                d.Site.SourceIdentity == "SourceMatchFlow.GoForTwo" || d.Site.SourceIdentity == "SourceMatchFlow.FreePat").ToArray();
            Equal(context, "native-rng/exact-draw-count", expected.GetProperty("draws").GetArrayLength(), draws.Length);
            for (int i = 0; i < draws.Length; i++)
            {
                Equal(context, "native-rng/bound-" + i, expected.GetProperty("draws")[i].GetProperty("bound").GetInt32(),
                    draws[i].Site.BoundOrModulus);
                Equal(context, "native-rng/value-" + i, expected.GetProperty("draws")[i].GetProperty("value").GetInt32(), draws[i].Value);
                Equal(context, "native-rng/actual-game-proc-" + i, "game_proc", draws[i].Site.Process);
            }
            if (!two)
            {
                bool terminal = packed == 0 && quarter >= 3 &&
                    (Int(expected, "home") != Int(expected, "away") || quarter == 6);
                Equal(context, "native-period/terminal-only-after-decision", terminal, m.Phase == GamePhase.GameOver);
                if (packed == 0 && !terminal)
                    Equal(context, "native-period/next-quarter", quarter + 2, m.Quarter);
            }
            Observe(context, "actual-PAT-decision-score-rng-period-final");
        });
    }

    private void PeriodCases()
    {
        foreach (int quarter in new[] { 0, 2 })
            Scenario("T2/EOQ/quarter-" + quarter + "-continues-possession", context => {
                GameSimulation game = Create(context);
                DeclarePeriod(context, quarter, 3, 0);
                ExpireLive(context); FinishLocation(context); ContinuePeriod(context);
                Equal(context, "original-EOQ1-3/next-index", quarter + 1,
                    ((ISourceMatchState)game.State).Match.Period.QuarterIndex);
                Equal(context, "original-EOQ1-3/possession-retained", 0, game.Model.SourceRules.OffenseSide);
                Equal(context, "original-EOQ1-3/no-kickoff", SourcePlayType.Normal, game.Model.SourceRules.PlayType);
            });
        foreach (int first in new[] { 0, 1 })
            Scenario("T2/halftime/initial-kicker-" + first, context => {
                GameSimulation game = Create(context, first: first);
                Equal(context, "opening/actual-configured-kicker", first, game.Model.SourceKickingSide);
                DeclarePeriod(context, 1, 0, 0);
                ExpireLive(context); FinishLocation(context); ContinuePeriod(context);
                Equal(context, "halftime/quarter-index-two", 2, ((ISourceMatchState)game.State).Match.Period.QuarterIndex);
                Equal(context, "halftime/other-initial-kicker", 1 - first, game.Model.SourceKickingSide);
                Equal(context, "halftime/receiving-side", first, game.Model.SourceRules.OffenseSide);
                Equal(context, "halftime/source-quick-kick", SourceKickoffKind.Quick, game.Model.SourceKickoffKind);
                Equal(context, "halftime/LOS40", 40, game.Model.SourceRules.LineOfScrimmage);
                Equal(context, "halftime/no-OT-draw", 0, ((SourceInjectedRandom)game.SourceRandomService).Trace
                    .Count(d => d.Site.SourceIdentity == "SourceMatchFlow.OvertimeKickoff"));
            });
        foreach (int quarter in new[] { 3, 4, 5, 6 })
        foreach (bool homeWins in new[] { true, false })
            Scenario("T2/untied-end/q" + quarter + "/" + (homeWins ? "Home" : "Away"), context => {
                GameSimulation game = Create(context);
                DeclarePeriod(context, quarter, homeWins ? 7 : 0, homeWins ? 0 : 7, packed: 2);
                Wake(context);
                Equal(context, "original-not-sudden-death/positive-clock-remains-live", GamePhase.LiveBall, game.Model.Phase);
                Equal(context, "original-not-sudden-death/no-early-terminal", 0,
                    game.SourceEvents.Count(e => e.Type == SourceEventType.MatchEnded));
                ExpireLive(context); FinishLocation(context); ContinuePeriod(context);
                Equal(context, "original-untied-end/final", GamePhase.GameOver, game.Model.Phase);
                Equal(context, "original-untied-end/retains-ending-period", quarter,
                    ((ISourceMatchState)game.State).Match.Period.QuarterIndex);
                Equal(context, "original-untied-end/once", 1,
                    context.Events.Count(e => e.Type == SourceEventType.MatchEnded));
            });
        foreach (int coin in new[] { 0, 1 })
            Scenario("T2/three-sequential-overtimes/coin-" + coin, context => {
                GameSimulation game = Create(context, coin: coin);
                int[] kickers = coin == 0 ? new[] { 1, 0, 1 } : new[] { 0, 1, 0 };
                for (int quarter = 3; quarter <= 6; quarter++)
                {
                    DeclarePeriod(context, quarter, 14, 14);
                    ExpireLive(context); FinishLocation(context); ContinuePeriod(context);
                    if (quarter == 6)
                    {
                        Equal(context, "original-OT3-tie/final", GamePhase.GameOver, game.Model.Phase);
                        Equal(context, "original-OT3-tie/no-fourth-OT", 6,
                            ((ISourceMatchState)game.State).Match.Period.QuarterIndex);
                    }
                    else
                    {
                        Equal(context, "original-OT/next-index-" + quarter, quarter + 1,
                            ((ISourceMatchState)game.State).Match.Period.QuarterIndex);
                        Equal(context, "original-OT/kickoff-alternation-" + quarter,
                            kickers[quarter - 3], game.Model.SourceKickingSide);
                        Equal(context, "original-OT/remembered-kicker-" + quarter,
                            kickers[quarter - 3], game.Model.SourceOvertimeKickoff);
                    }
                    SourceRandomDraw[] draws = ((SourceInjectedRandom)game.SourceRandomService).Trace.Where(
                        d => d.Site.SourceIdentity == "SourceMatchFlow.OvertimeKickoff").ToArray();
                    Equal(context, "original-OT/one-coin-across-periods-" + quarter, 1, draws.Length);
                    Equal(context, "original-OT/native-bound-" + quarter, 2, draws[0].Site.BoundOrModulus);
                    Equal(context, "original-OT/declared-value-" + quarter, coin, draws[0].Value);
                }
            });
        foreach (int scenario in new[] { 0, 1, 2, 3 })
            Scenario("T1-T2-S1/expired-live-TD-PAT/" + scenario, context => {
                int quarter = scenario == 3 ? 1 : 3;
                int starting = scenario == 0 || scenario == 3 ? 0 : scenario == 1 ? 1 : 2;
                GameSimulation game = Create(context);
                DeclarePeriod(context, quarter, starting, 7);
                ExpireLive(context); FinishLocation(context, 405f, 0f);
                Equal(context, "TD/source-six-after-live-expiry", starting + 6, game.Model.HomeScore);
                Equal(context, "TD/required-conversion-before-terminal", SourcePlayType.ExtraPoint, game.Model.SourceRules.PlayType);
                Equal(context, "TD/no-early-terminal", 0, context.Events.Count(e => e.Type == SourceEventType.MatchEnded));
                ContinuePeriod(context);
                bool skipped = scenario == 2;
                if (!skipped)
                {
                    Equal(context, "PAT/actual-human-selection", SourceMatchPhase.PatSelection, game.Model.SourceMatchPhase);
                    Wake(context, Command(source: new SourceCommand(SourceAction.SelectPat, patChoice: SourcePatOption.FreeKick)));
                    Drain(context);
                }
                Equal(context, "PAT/source-score-or-skip", starting + (skipped ? 6 : 7), game.Model.HomeScore);
                Equal(context, "PAT/period-only-after-needed-conversion",
                    scenario == 0 ? 4 : scenario == 3 ? 2 : 3,
                    ((ISourceMatchState)game.State).Match.Period.QuarterIndex);
                Equal(context, "PAT/terminal-only-if-untied-regulation", scenario == 1 || scenario == 2,
                    game.Model.Phase == GamePhase.GameOver);
                Equal(context, "PAT/skip-has-no-free-result", skipped ? 0 : 1,
                    context.Events.Count(e => e.Type == SourceEventType.FreePointResult));
            });
    }

    private void ExpiredConversionCases()
    {
        foreach (int scenario in Enumerable.Range(0, 6))
            Scenario("T1-T2-S1/expired-conversion-exception/" + scenario, context => {
                GameSimulation game = Create(context);
                int offense = scenario == 0 ? 0 : 1;
                SourceGameFlags flags = offense == 0 ? SourceGameFlags.None : SourceGameFlags.PossessionChange;
                if (scenario >= 2) flags |= SourceGameFlags.RecoveredInEndZone;
                if (scenario == 3) flags |= SourceGameFlags.LeftEndZone;
                DeclarePeriod(context, 3, 6, 7, 0, SourcePlayType.ExtraPoint, offense, 0, flags);
                SimulationState m = game.Model;
                if (scenario >= 2 && scenario < 4)
                    SourcePlayerDriver.SetSourcePosition(m.Players[m.CarrierId], new SourceVector3(405f, 0f, 0f));
                if (scenario >= 4)
                {
                    Wake(context);
                    Equal(context, "native-PAT/actual-held-ball-retains-carrier", m.CarrierId, m.SourceLastCarrierId);
                    foreach (ActorState actor in m.Players) actor.SourceMode = SourceActorMode.Dead;
                    m.CarrierId = -1; m.BallMode = BallMode.InFlight;
                    m.BallKind = scenario == 4 ? SourceBallKind.Fumble : SourceBallKind.Pass;
                    m.PassTicksElapsed = 31; m.PassTicksTotal = 60;
                    m.SourceBallVelocity = default(FieldVector); m.SourceBallVerticalVelocity = 0f;
                    SourceBallDriver.SetPosition(m, new SourceVector3(405f, 10f, 190f));
                    Wake(context);
                    Equal(context, "native-PAT/actual-ball-proc-queue", true, game.C2State.ResultPending);
                }
                else
                {
                    Wake(context);
                    Equal(context, "native-PAT/expired-live-conversion-not-killed", GamePhase.LiveBall, m.Phase);
                    FinishLocation(context, scenario == 1 ? -405f : scenario >= 2 ? 405f : 0f,
                        scenario == 1 ? 0f : 190f);
                }
                if (scenario >= 4) Wake(context);
                SourcePlayResult result = scenario == 1 ? SourcePlayResult.Conversion
                    : scenario == 3 || scenario == 4 ? SourcePlayResult.PatSafety : SourcePlayResult.FailedConversion;
                Equal(context, "native-result/defensive-safety-or-conversion-exception", result, m.SourceLastResult);
                Equal(context, "native-result/home", scenario == 3 || scenario == 4 ? 7 : 6, m.HomeScore);
                Equal(context, "native-result/away", scenario == 1 ? 9 : 7, m.AwayScore);
                ContinuePeriod(context);
                Equal(context, "native-result/only-tie-requires-OT", scenario == 3 || scenario == 4,
                    m.Quarter == 5 && m.Phase != GamePhase.GameOver);
            });
    }

    private void ClockWarningContextCases()
    {
        foreach (int kind in Enumerable.Range(0, 6))
            Scenario("T1/clock-warning-context/" + kind, context => {
                int quarter = kind == 0 || kind == 2 ? 1 : kind == 1 || kind == 3 ? 3 : kind == 4 ? 0 : 4;
                int packed = kind == 2 || kind == 3 ? 0x1e00 : 0x1f00;
                int playTime = kind == 5 ? 0 : 17;
                GameSimulation game = Create(context);
                DeclarePeriod(context, quarter, 0, 0, packed);
                SimulationState m = game.Model;
                foreach (ActorState actor in m.Players) actor.SourceMode = SourceActorMode.Dead;
                m.SourceClock = SourceClock.Create(packed, playTime, firstWakeTick: game.SourceTick + 1);
                m.SourceTimeOfPossession = new SourceTimeOfPossessionView(13, 19);
                var calls = new List<SourceProcessSoundCall>();
                game.SourceProcessSoundRaised += call => {
                    if (call.ProcessSerial != GameSimulation.ClockProcess) return;
                    Equal(context, "warning/actual-host-caller", GameSimulation.ClockProcess, m.SourceProcesses.Caller);
                    Equal(context, "warning/actual-source-context", "game_clock_proc", m.SourceRandomProcess);
                    Equal(context, "warning/emitted-after-wake-before-sleep", "Wake",
                        string.Join(",", m.SourceProcesses.Records.Where(r => r.Serial == GameSimulation.ClockProcess)
                            .Select(r => r.Operation)));
                    calls.Add(call);
                };
                int home = 13, away = 19;
                for (int step = 0; step < 2; step++)
                {
                    JsonElement expected = OriginalCommand(context, string.Join(" ", "clock", packed, quarter,
                        playTime, 0, 1, 2, 2, 0, home, away, 1));
                    Wake(context);
                    bool warning = expected.GetProperty("events").EnumerateArray()
                        .Any(item => item.GetString().StartsWith("sound:", StringComparison.Ordinal));
                    Equal(context, "native-clock/packed-" + step, Int(expected, "packed"), m.SourceClock.PackedTime);
                    Equal(context, "native-clock/local-" + step, Int(expected, "packed"), m.SourceClock.LocalPackedTime);
                    Equal(context, "native-clock/play-time-" + step, Int(expected, "play_time"), m.SourceClock.PlayTime);
                    Equal(context, "native-clock/home-possession-" + step,
                        expected.GetProperty("time_of_possession")[0].GetInt32(), m.SourceTimeOfPossession.HomeSeconds);
                    Equal(context, "native-clock/away-possession-" + step,
                        expected.GetProperty("time_of_possession")[1].GetInt32(), m.SourceTimeOfPossession.AwaySeconds);
                    Equal(context, "native-clock/warning-count-" + step, warning ? 1 : 0, calls.Count);
                    foreach (SourceProcessSoundCall call in calls)
                    {
                        Equal(context, "warning/source-tick", game.SourceTick, call.SourceTick);
                        Equal(context, "warning/process", "game_clock_proc", call.Process);
                        Equal(context, "warning/playlist", 36, call.PlaylistId);
                        Equal(context, "warning/bank", m.SourceCurrentCursorBank, call.Bank);
                        Equal(context, "warning/volume", 235u, call.Volume);
                        Equal(context, "warning/pan", 127u, call.Pan);
                        Equal(context, "warning/priority", 0u, call.PriorityWord);
                        Equal(context, "warning/no-rng", 0, call.RandomCalls);
                        Equal(context, "warning/original-call", 2940, call.CalleeSourceLine);
                    }
                    Equal(context, "clock/wake-sleep-order-" + step, "Wake,Sleep",
                        string.Join(",", m.SourceProcesses.Records.Where(r => r.Serial == GameSimulation.ClockProcess)
                            .Select(r => r.Operation)));
                    Equal(context, "clock/sleep-one-" + step, 1,
                        m.SourceProcesses.Records.Single(r => r.Serial == GameSimulation.ClockProcess
                            && r.Operation == "Sleep").Value);
                    Equal(context, "clock/next-real-wake-" + step, game.SourceTick + 1, m.SourceClock.NextWakeTick);
                    Equal(context, "clock/no-rng-producer-" + step, 0,
                        ((SourceInjectedRandom)game.SourceRandomService).Trace.Count(d => d.Site.Process == "game_clock_proc"));
                    context.Trace.Add(new { stage = "actual-clock-warning-and-continuation", step,
                        calls = calls.ToArray(), records = m.SourceProcesses.Records.ToArray() });
                    Observe(context, "actual-public-clock-source-wake");
                    packed = Int(expected, "packed"); playTime = Int(expected, "play_time");
                    home = expected.GetProperty("time_of_possession")[0].GetInt32();
                    away = expected.GetProperty("time_of_possession")[1].GetInt32();
                    calls.Clear();
                }
            });
        Scenario("T1/clock-warning-context/strict-invalid-producer", context => {
            GameSimulation game = Create(context);
            MethodInfo warning = typeof(GameSimulation).GetMethod("SourceOriginalClockWarningSound",
                BindingFlags.NonPublic | BindingFlags.Instance);
            int calls = 0;
            game.SourceProcessSoundRaised += call => { if (call.ProcessSerial == GameSimulation.ClockProcess) calls++; };
            Action<string> rejected = expected => {
                string error = "";
                try { warning.Invoke(game, null); }
                catch (TargetInvocationException failure)
                { if (failure.InnerException is InvalidOperationException) error = failure.InnerException.Message; }
                Equal(context, "strict-negative/" + expected, true, error.Contains(expected));
            };
            rejected("actual executing source wake");
            int actualGameProcChecks = 0;
            game.SourceStationInputTransform = (state, tick, frame) => {
                if (game.Model.SourceProcesses.Caller == GameSimulation.GameProcess)
                {
                    actualGameProcChecks++;
                    rejected("actual game_clock_proc wake");
                }
                return frame;
            };
            Wake(context);
            Equal(context, "strict-negative/actual-game-proc-checked", 1, actualGameProcChecks);
            Equal(context, "strict-negative/no-clock-call-emitted", 0, calls);
            Observe(context, "strict-invalid-producers-rejected-without-guard-relaxation");
        });
    }

    private static string Hash(byte[] bytes)
    {
        using (SHA256 sha = SHA256.Create())
            return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
    }

    private void WriteProgress(string id, string status)
    {
        if (progressPath.Length != 0)
            File.WriteAllText(progressPath, JsonSerializer.Serialize(new {
                id, status, completed = rows.Count, assertions, failures
            }));
    }

    private static int Main(string[] args)
    {
        bool clockRetirement = args.Length == 5 && args[0] == "--clock-retirement";
        bool patCallers = args.Length == 5 && args[0] == "--pat-callers";
        bool periods = args.Length == 5 && args[0] == "--periods";
        bool periodPat = args.Length == 5 && args[0] == "--period-pat";
        bool clockWarning = args.Length == 5 && args[0] == "--clock-warning-context";
        if (clockRetirement || patCallers || periods || periodPat || clockWarning) args = args.Skip(1).ToArray();
        if (args.Length != 4) throw new ArgumentException("Expected actual1114 root, new caller corpus, its actual C executable and new receipt path.");
        if (File.Exists(args[3])) throw new IOException("Preserve earlier proof; select a new receipt path.");
        string root = Path.GetFullPath(args[0]);
        var catalog = new DataCatalog(File.ReadAllBytes(Path.Combine(root, "manifest.json")),
            relative => File.ReadAllBytes(Path.Combine(root, relative)));
        var suite = new SourceRuleTransitionChecks(new PlayerData(catalog), Path.GetFullPath(args[2]));
        suite.progressPath = args[3] + ".progress.json";
        using (JsonDocument corpus = JsonDocument.Parse(File.ReadAllBytes(args[1])))
        {
            JsonElement document = corpus.RootElement;
            if (clockWarning)
            {
                if (document.GetProperty("schema").GetString() != "native-original-T1-finite-20261110/v1"
                    || document.GetProperty("authority").GetProperty("compiler").GetProperty("binary_sha256").GetString()
                        != Hash(File.ReadAllBytes(args[2])))
                    throw new InvalidDataException("Require the independently generated finite T1 corpus and its exact original-C executable.");
                suite.ClockWarningContextCases();
            }
            else if (document.GetProperty("schema").GetString() != "original-c-source-callers-20261110/v1"
                || document.GetProperty("cases").GetArrayLength() != 64
                || document.GetProperty("manifest").GetProperty("compiler").GetProperty("binary_sha256").GetString()
                    != Hash(File.ReadAllBytes(args[2])))
                throw new InvalidDataException("Require the independently generated64-case corpus and its exact original-C executable.");
            if (patCallers || periodPat)
            {
                if (document.GetProperty("pat_cases").GetArrayLength() != 60)
                    throw new InvalidDataException("The exact60 native PAT boundary cases are required.");
                foreach (JsonElement item in document.GetProperty("pat_cases").EnumerateArray()) suite.PatCase(item);
            }
            if (periods || periodPat)
            {
                suite.PeriodCases();
                suite.ExpiredConversionCases();
                suite.FreePatTiming();
            }
            if (!patCallers && !periods && !periodPat && !clockWarning)
            {
                foreach (JsonElement item in document.GetProperty("cases").EnumerateArray()) suite.Caller(item);
                suite.ClockLifetime(document.GetProperty("cases")[0].GetProperty("input"), false);
                if (!clockRetirement)
                {
                    suite.ClockLifetime(document.GetProperty("cases")[0].GetProperty("input"), true);
                    suite.FreePatTiming();
                }
            }
        }
        bool passed = suite.failures == 0 && (!clockRetirement || suite.rows.Count == 65 && suite.assertions == 1694)
            && (!patCallers || suite.rows.Count == 60) && (!periods || suite.rows.Count == 25)
            && (!periodPat || suite.rows.Count == 85) && (!clockWarning || suite.rows.Count == 7);
        File.WriteAllText(args[3], JsonSerializer.Serialize(new {
            schema = "actual-full-player-source-callers-20261110/v1",
            status = passed ? "passed" : "failed", cases = suite.rows,
            selector = clockWarning ? "actual-clock-warning-context7"
                : clockRetirement ? "original-C-callers64-plus-clock-retirement1"
                : patCallers ? "source-PAT-boundaries60" : "expanded-source-transitions",
            period_selector = periods ? "source-periods-and-expired-conversions25"
                : periodPat ? "source-periods-PAT-complete-discriminators85" : "none",
            expected_assertions = clockRetirement ? (int?)1694 : null,
            suite.assertions, suite.failures, input_manifest_sha256 = catalog.ManifestSha256,
            original_C_binary_sha256 = Hash(File.ReadAllBytes(args[2])),
            production_mvid = typeof(GameSimulation).Assembly.ManifestModule.ModuleVersionId,
            actual_source_step_only = true, direct_end_or_period_dispatch = false,
            platform_boundary = "Authenticated C caller statements on host int32/binary32; actual .NET10 full-player production. Not vendor toolchain/external ABI or underlying native RNG.",
            declared_initial_boundaries = true, natural_full_match_or_browser_claimed = false
        }, new JsonSerializerOptions { WriteIndented = true }));
        return passed ? 0 : 1;
    }
}
#endif
