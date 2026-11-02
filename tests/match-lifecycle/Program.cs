using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Blitz.Contracts;
using Blitz.Gameplay;
using Blitz.Runtime;

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
