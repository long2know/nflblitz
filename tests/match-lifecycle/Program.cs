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

    private static int Main(string[] args)
    {
        var result = new Dictionary<string, object> {
            ["schema"] = "source-match-ended-caller-checks/v1",
            ["status"] = "error", ["whole_game_or_native_qualified"] = false,
            ["declared_boundary_cases_not_public_full_match"] = true
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
            result["status"] = "passed";
        }
        catch (LifecycleAssertion error) { result["status"] = "failed"; result["failed_assertion"] = error.Message; }
        catch (Exception error) when (error is ArgumentException || error is InvalidOperationException
            || error is NotSupportedException || error is IOException)
        { result["error"] = error.ToString(); Console.Error.WriteLine(error); }
        result["assertions"] = assertions; result["cases"] = cases;
        Console.WriteLine(JsonSerializer.Serialize(result));
        return (string)result["status"] == "passed" ? 0 : 1;
    }
}
