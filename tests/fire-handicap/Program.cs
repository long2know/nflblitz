using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Blitz.Contracts;
using Blitz.Gameplay;
using Blitz.Runtime;

internal sealed class FireHandicapAssertion : Exception
{
    internal FireHandicapAssertion(string identity) : base(identity) { }
}

internal static class Program
{
    private static FullKernelInputs real;
    private static Process original;
    private static StreamWriter transcript;
    private static readonly Dictionary<string, int> cases = new Dictionary<string, int>();
    private static readonly List<object> observations = new List<object>();
    private static int assertions;
    private static int referenceCalls;
    private static int undefinedOriginalConsumerCases;
    private static string scope;
    private static readonly Func<SourceBallKind, SourceBallType> SourceBallTypeFor =
        (Func<SourceBallKind, SourceBallType>)typeof(GameSimulation).GetMethod("SourceBallTypeFor",
            BindingFlags.NonPublic | BindingFlags.Static).CreateDelegate(typeof(Func<SourceBallKind, SourceBallType>));

    private static void Check(bool condition, string identity)
    {
        assertions++;
        if (!condition) throw new FireHandicapAssertion(scope + ": " + identity);
    }

    private static T Bind<T>(GameSimulation game, string method)
    {
        MethodInfo info = typeof(GameSimulation).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance,
            null, typeof(T).GetMethod("Invoke").GetParameters().Select(p => p.ParameterType).ToArray(), null);
        if (info == null) throw new MissingMethodException(method);
        return (T)(object)info.CreateDelegate(typeof(T), game);
    }

    private static int Bit(bool value) { return value ? 1 : 0; }
    private static string Text(object value)
    {
        if (value is float) return ((float)value).ToString("R", CultureInfo.InvariantCulture);
        return Convert.ToString(value, CultureInfo.InvariantCulture);
    }

    private static string ReferenceOutput(IEnumerable<object> fields)
    {
        string input = string.Join(" ", fields.Select(Text));
        original.StandardInput.WriteLine(input);
        original.StandardInput.Flush();
        string output = original.StandardOutput.ReadLine();
        if (output == null) throw new InvalidOperationException("Original C stopped: " + original.StandardError.ReadToEnd());
        transcript.WriteLine(input);
        transcript.WriteLine(output);
        transcript.Flush();
        referenceCalls++;
        return output;
    }

    private static int[] Reference(IEnumerable<object> fields)
    {
        return ReferenceOutput(fields).Split(' ').Select(x => int.Parse(x, CultureInfo.InvariantCulture)).ToArray();
    }

    private static SourceMatchConfiguration Configuration(int humans = 3, int homeTeam = 0, int awayTeam = 0,
        SourceStationLayout layout = SourceStationLayout.FourStation, int difficulty = 5)
    {
        var stations = new List<SourceStationConfiguration>();
        if ((humans & 1) != 0) stations.Add(new SourceStationConfiguration(0, TeamId.Home, 3));
        if ((humans & 2) != 0) stations.Add(new SourceStationConfiguration(
            layout == SourceStationLayout.FourStation ? 2 : 1, TeamId.Away, 10));
        return new SourceMatchConfiguration(SourceSimulationProfile.OriginalRelease57Hz, layout,
            new SourceTeamSelection(homeTeam, new[] { 0, 1, 2, 3, 4, 5, 6 }),
            new SourceTeamSelection(awayTeam, new[] { 0, 1, 2, 3, 4, 5, 6 }),
            stations, TeamId.Home, SourceCaptainSlot.First, difficulty: difficulty);
    }

    private static GameSimulation Game(int humans = 3, int homeTeam = 0, int awayTeam = 0)
    {
        return real.Create(configuration: Configuration(humans, homeTeam, awayTeam),
            presentation: new SourcePresentationSettings(TeamId.Home, new[] { -1, -1, -1, -1 }, 0),
            randomFactory: seed => new SourceInjectedRandom(site => 99,
                new SourceRandomProvenance("fire-handicap/controlled-raw99",
                    "Declared original-C/component fixture, not a public replay or original platform PRNG",
                    "source-fire-handicap", SourceRandomEvidence.ExternalUnverified)));
    }

    private static void Rule(GameSimulation game, int side = 0, int initial = 0, int down = 1,
        int los = 30, int first = 100, SourcePlayType type = SourcePlayType.Normal)
    {
        SimulationState m = game.Model;
        m.SourceRules = new SourceRulesState(side, initial, down, los, first, type);
        m.Possession = (TeamId)side;
        m.Down = down;
        m.LineOfScrimmage = SourceMatchRules.AbsoluteYardsFromSourceWorld(SourceMatchRules.WorldPosition(los, side));
        m.FirstDownLine = SourceMatchRules.AbsoluteYardsFromSourceWorld(SourceMatchRules.WorldPosition(first, side));
    }

    private static ActorState FireFixture(GameSimulation game, int side = 0)
    {
        SimulationState m = game.Model;
        Rule(game, side, side);
        m.Phase = GamePhase.LiveBall;
        m.SourceLastPossession = side;
        m.SourceFlags = SourceGameFlags.None;
        m.BallKind = SourceBallKind.Pass;
        m.SourceBallFlags = SourceBallFlags.None;
        m.SourceForwardCatcher = -1;
        m.SourceLastResult = SourcePlayResult.Down;
        m.SourceC2PendingResult = null;
        m.SourceHasContactSpot = false;
        m.SourcePendingTackleAttacker = m.SourcePendingTackleVictim = -1;
        m.CarrierId = side * 7 + 4;
        m.TargetPlayerId = m.CarrierId;
        m.ThrowerId = side * 7 + 3;
        m.BallMode = BallMode.Held;
        m.ClockActive = false;
        m.CatchSourceTick = m.SourceTick - 18;
        m.SourceCatchX = SourceMatchRules.WorldPosition(40, side);
        m.SourcePlayEndX = SourceMatchRules.WorldPosition(40, side);
        m.SourcePlayEndZ = 0f;
        m.SourceFumbleX = 0f;
        for (int i = 0; i < 2; i++)
        {
            m.SourceTeamFire[i] = false;
            m.SourceLastReceiver[i] = -1;
            m.SourceConsecutiveCompletions[i] = m.SourceConsecutiveLosses[i] = 0;
            m.SourceInterceptions[i] = m.SourceLostFumbles[i] = 0;
        }
        ActorState actor = m.Players[m.CarrierId];
        actor.SourcePendingFireCheck = false;
        actor.SourceRawFlags = 0;
        actor.SourceMode = SourceActorMode.Receiver;
        SourcePlayerDriver.SetSourcePosition(actor, new SourceVector3(m.SourceCatchX, 0f, 0f));
        SourceBallDriver.SetPosition(m, SourcePlayerDriver.WorldPosition(actor));
        return actor;
    }

    private static int[] FireReference(GameSimulation game, ActorState actor, string command,
        SourcePlayEndCause cause = SourcePlayEndCause.CarrierTackledInBounds, bool? live = null, float? height = null)
    {
        SimulationState m = game.Model;
        SourceVector3 root = SourcePlayerDriver.WorldPosition(actor);
        return Reference(new object[] {
            command, (int)cause, (int)m.Possession, m.SourceRules.InitialOffenseSide, m.SourceLastPossession,
            m.SourceRules.Down, m.SourceRules.LineOfScrimmage, m.SourceRules.FirstDown,
            (int)m.SourceRules.PlayType, (int)SourceBallTypeFor(m.BallKind), (int)m.SourceFlags, m.SourcePlayEndX,
            m.SourceFumbleX, m.SourceForwardCatcher, Bit(m.SourceTeamFire[0]), Bit(m.SourceTeamFire[1]),
            m.SourceConsecutiveCompletions[0], m.SourceConsecutiveCompletions[1],
            m.SourceLastReceiver[0], m.SourceLastReceiver[1], m.SourceConsecutiveLosses[0], m.SourceConsecutiveLosses[1],
            actor.Id, root.X, height ?? root.Y, root.Z, Bit(actor.SourcePendingFireCheck),
            (int)(m.SourceTick - m.CatchSourceTick), m.SourceCatchX, Bit(live ?? m.Phase == GamePhase.LiveBall),
            (int)m.SourceLastResult, Bit(m.SourceRules.NoFirstDown), (int)m.SourceBallFlags,
            m.SourceInterceptions[0], m.SourceInterceptions[1], m.SourceLostFumbles[0], m.SourceLostFumbles[1]
        });
    }

    private static void FireEquals(int[] expected, GameSimulation game, ActorState actor, bool result = false)
    {
        SimulationState m = game.Model;
        int[] actual = {
            Bit(m.SourceTeamFire[0]), Bit(m.SourceTeamFire[1]),
            m.SourceConsecutiveCompletions[0], m.SourceConsecutiveCompletions[1],
            m.SourceLastReceiver[0], m.SourceLastReceiver[1], m.SourceConsecutiveLosses[0], m.SourceConsecutiveLosses[1],
            m.SourceForwardCatcher, m.SourceLastPossession, (int)m.SourceFlags,
            m.SourceInterceptions[0], m.SourceInterceptions[1], m.SourceLostFumbles[0], m.SourceLostFumbles[1],
            Bit(actor.SourcePendingFireCheck), (int)m.Possession, m.SourceRules.Down, m.SourceRules.LineOfScrimmage,
            m.SourceRules.FirstDown, (int)m.SourceLastResult, (int)m.SourceRules.PlayType
        };
        Check(expected.Length == actual.Length, "nonempty original-C field inventory");
        for (int i = 0; i < (result ? actual.Length : 16); i++)
            Check(expected[i] == actual[i], "original-C field[" + i + "] expected=" + expected[i] + " actual=" + actual[i]);
        if (!cases.ContainsKey(scope)) cases[scope] = 0;
        cases[scope]++;
    }

    private static void CarrierCases()
    {
        GameSimulation game = Game();
        scope = "carrier";
        foreach (int side in new[] { 0, 1 })
            foreach (int held in new[] { 17, 18, 19 })
                foreach (int count in new[] { 0, 1, 2, 3 })
                    foreach (bool sameReceiver in new[] { false, true })
                    {
                        ActorState actor = FireFixture(game, side);
                        game.Model.CatchSourceTick = game.SourceTick - held;
                        game.Model.SourceLastReceiver[side] = sameReceiver ? actor.Id : side * 7 + 5;
                        game.Model.SourceConsecutiveCompletions[side] = count;
                        actor.SourceControllerStation = count % 2 == 0 ? -1 : side == 0 ? 0 : 2;
                        int[] expected = FireReference(game, actor, "C");
                        long draws = game.Model.SourceRandomDraws;
                        Bind<Action<ActorState>>(game, "SourceCarrierControl")(actor);
                        FireEquals(expected, game, actor);
                        Check(game.Model.SourceRandomDraws == draws, "fire/control trigger consumes no RNG");
                    }
        scope = "carrier-predicates";
        foreach (int side in new[] { 0, 1 })
            foreach (SourceBallKind kind in new[] { SourceBallKind.Pass, SourceBallKind.Bobble, SourceBallKind.Lateral })
                foreach (float delta in new[] { -.002f, 0f, .002f })
                {
                    ActorState actor = FireFixture(game, side);
                    game.Model.BallKind = kind;
                    game.Model.SourceLastReceiver[side] = actor.Id;
                    game.Model.SourceConsecutiveCompletions[side] = 2;
                    game.Model.SourceCatchX = SourceMatchRules.WorldPosition(30, side) + delta;
                    int[] expected = FireReference(game, actor, "C");
                    Bind<Action<ActorState>>(game, "SourceCarrierControl")(actor);
                    FireEquals(expected, game, actor);
                }
        scope = "air-pending";
        foreach (int side in new[] { 0, 1 })
            foreach (bool live in new[] { false, true })
                foreach (bool inbounds in new[] { false, true })
                {
                    ActorState actor = FireFixture(game, side);
                    SimulationState m = game.Model;
                    m.SourceLastReceiver[side] = actor.Id;
                    m.SourceConsecutiveCompletions[side] = 2;
                    m.SourceFlags |= SourceGameFlags.AirCatch;
                    SourcePlayerDriver.SetSourcePosition(actor, new SourceVector3(m.SourceCatchX, 3f, 0f));
                    int[] expected = FireReference(game, actor, "C");
                    Bind<Action<ActorState>>(game, "SourceCarrierControl")(actor);
                    FireEquals(expected, game, actor);
                    Check(actor.SourcePendingFireCheck && !m.SourceTeamFire[side], "airborne third completion is pending");
                    m.Phase = live ? GamePhase.LiveBall : GamePhase.DeadBall;
                    m.CatchSourceTick--;
                    SourcePlayerDriver.SetSourcePosition(actor, new SourceVector3(m.SourceCatchX, 0f, inbounds ? 0f : 200f));
                    expected = FireReference(game, actor, "C");
                    Bind<Action<ActorState>>(game, "SourceCarrierControl")(actor);
                    FireEquals(expected, game, actor);
                    Check(m.SourceTeamFire[side] == inbounds, "pending fire lands inbounds even after play-over");
                }
        scope = "possession";
        foreach (int side in new[] { 0, 1 })
            foreach (SourceBallKind kind in new[] { SourceBallKind.Pass, SourceBallKind.Bobble, SourceBallKind.Lateral,
                SourceBallKind.Fumble, SourceBallKind.Kickoff })
                foreach (bool bounce in new[] { false, true })
                    foreach (bool receivingFire in new[] { false, true })
                    {
                        ActorState actor = FireFixture(game, side);
                        SimulationState m = game.Model;
                        m.SourceLastPossession = 1 - side;
                        m.BallKind = kind;
                        m.SourceBallFlags = bounce ? SourceBallFlags.Bounce : SourceBallFlags.None;
                        m.SourceTeamFire[1 - side] = true;
                        m.SourceTeamFire[side] = receivingFire;
                        m.SourceLastReceiver[side] = side * 7 + 5;
                        m.SourceLastReceiver[1 - side] = (1 - side) * 7 + 5;
                        m.SourceConsecutiveCompletions[side] = m.SourceConsecutiveCompletions[1 - side] = 2;
                        m.SourceConsecutiveLosses[0] = m.SourceConsecutiveLosses[1] = 1;
                        int[] expected = FireReference(game, actor, "C");
                        Bind<Action<ActorState>>(game, "SourceCarrierControl")(actor);
                        FireEquals(expected, game, actor);
                    }
    }

    private static void ResultCases()
    {
        GameSimulation game = Game();
        scope = "result";
        foreach (int side in new[] { 0, 1 })
            foreach (int down in new[] { 1, 4 })
                foreach (int end in new[] { 20, 29, 30, 31, 60 })
                    foreach (int losses in new[] { 0, 1, 2, 3 })
                    {
                        ActorState actor = FireFixture(game, side);
                        SimulationState m = game.Model;
                        Rule(game, side, side, down, 30, 60);
                        m.SourcePlayEndX = SourceMatchRules.WorldPosition(end, side);
                        m.SourceTeamFire[side] = true;
                        m.SourceConsecutiveCompletions[side] = 3;
                        m.SourceLastReceiver[side] = actor.Id;
                        m.SourceConsecutiveLosses[1 - side] = losses;
                        int[] expected = FireReference(game, actor, "P");
                        Bind<Action<SourcePlayEndCause, int, FieldVector, int, SourceC2PendingEnd>>(game, "ApplySourcePlayEnd")(
                            SourcePlayEndCause.CarrierTackledInBounds, actor.Id, actor.Position, -1, null);
                        FireEquals(expected, game, actor, true);
                    }
        scope = "result-kinds";
        var outcomes = new[] {
            new[] { 0, 1, 0, 0, 20, 0 }, new[] { 0, 4, 0, 0, 20, 0 },
            new[] { 0, 4, 4, 0, 20, 0 }, new[] { 0, 1, 2, 0, -5, 0 },
            new[] { 0, 1, 3, 0, 102, 0 }, new[] { 0, 1, 4, 4, 80, 4 },
            new[] { 0, 1, 4, 4, 80, 0 }, new[] { 4, -1, 1, 0, 89, 0 },
            new[] { 4, -1, 3, 0, 101, 0 }, new[] { 1, 1, 1, 3, 20, 0 },
            new[] { 2, 4, 4, 4, 20, 0 }, new[] { 3, 4, 5, 2, 30, 0 }
        };
        foreach (int side in new[] { 0, 1 })
            foreach (bool fire in new[] { false, true })
                foreach (int[] row in outcomes)
                {
                    ActorState actor = FireFixture(game, side);
                    SimulationState m = game.Model;
                    Rule(game, side, side, row[1], row[0] == 4 ? 90 : 30, 100, (SourcePlayType)row[0]);
                    m.BallKind = (SourceBallKind)Enum.Parse(typeof(SourceBallKind), ((SourceBallType)row[3]).ToString());
                    m.SourcePlayEndX = SourceMatchRules.WorldPosition(row[4], side);
                    m.SourceFlags = (SourceGameFlags)row[5];
                    m.SourceLastResult = SourcePlayResult.MissedKick;
                    for (int i = 0; i < 2; i++)
                    {
                        m.SourceTeamFire[i] = fire;
                        m.SourceConsecutiveCompletions[i] = 2;
                        m.SourceLastReceiver[i] = i * 7 + 4;
                        m.SourceConsecutiveLosses[i] = 1;
                    }
                    int[] expected = FireReference(game, actor, "P", (SourcePlayEndCause)row[2]);
                    Bind<Action<SourcePlayEndCause, int, FieldVector, int, SourceC2PendingEnd>>(game, "ApplySourcePlayEnd")(
                        (SourcePlayEndCause)row[2], actor.Id, actor.Position, -1, null);
                    FireEquals(expected, game, actor, true);
                }
        scope = "result-control-owner";
        foreach (int side in new[] { 0, 1 })
            foreach (int last in new[] { 0, 1 })
            {
                ActorState actor = FireFixture(game, side);
                SimulationState m = game.Model;
                m.SourceLastPossession = last;
                m.SourceConsecutiveLosses[1 - side] = 1;
                m.SourceTeamFire[side] = true;
                m.SourcePlayEndX = SourceMatchRules.WorldPosition(20, last);
                int[] expected = FireReference(game, actor, "P");
                Bind<Action<SourcePlayEndCause, int, FieldVector, int, SourceC2PendingEnd>>(game, "ApplySourcePlayEnd")(
                    SourcePlayEndCause.CarrierTackledInBounds, actor.Id, actor.Position, -1, null);
                FireEquals(expected, game, actor, true);
            }
        scope = "counter-boundary";
        foreach (int count in new[] { 1, 2, 3, int.MaxValue - 1 })
        {
            ActorState actor = FireFixture(game);
            game.Model.SourceConsecutiveLosses[1] = count;
            game.Model.SourcePlayEndX = SourceMatchRules.WorldPosition(20, 0);
            int[] expected = FireReference(game, actor, "P");
            Bind<Action<SourcePlayEndCause, int, FieldVector, int, SourceC2PendingEnd>>(game, "ApplySourcePlayEnd")(
                SourcePlayEndCause.CarrierTackledInBounds, actor.Id, actor.Position, -1, null);
            FireEquals(expected, game, actor, true);
        }
    }

    private static int PlayId(SourcePlayDefinition play)
    {
        if (play == null) return -1;
        return play.Identity == "play_spider" ? 0 : play.Identity == "play_zigzag" ? 1
            : play.Identity == "play_safe_cover" ? 2 : 3;
    }

    private static GameSimulation FH01Factory()
    {
        SourceMatchConfiguration configuration = Configuration();
        var presentation = new SourcePresentationSettings(TeamId.Home, new[] { -1, -1, -1, -1 }, 0);
        var movement = new SourceMovementSettings(0, 0, false, 0, 0, false);
        Func<int, ISourceRandom> random = seed => new SourceInjectedRandom(site => 99,
            new SourceRandomProvenance("FH01/controlled-defined-result", "Original-C result component case",
                "not public replay or target PRNG", SourceRandomEvidence.ExternalUnverified));
        if (!real.IsC2Profile)
            return SourceContactComposition.CreateSimulation(real.Animations, real.ReferencePivots, real.Playbook,
                configuration, real.Rosters, presentation, real.MovementRuntime, movement, real.SoundFunctions, random);
        SourceC2Bootstrap bootstrap = SourceC2Bootstrap.FromOriginalProgramBootAtSoundBanks(real.C2Data);
        return real.IsC2M1Profile
            ? SourceC2Composition.CreateM1Simulation(real.Animations, real.ReferencePivots, real.Playbook,
                configuration, real.Rosters, presentation, real.MovementRuntime, movement, real.SoundFunctions, bootstrap, random)
            : SourceC2Composition.CreateSimulation(real.Animations, real.ReferencePivots, real.Playbook,
                configuration, real.Rosters, presentation, real.MovementRuntime, movement, real.SoundFunctions, bootstrap, random);
    }

    private static void FH01IncompleteTurnover(int initial)
    {
        GameSimulation game = FH01Factory();
        int receiving = 1 - initial;
        FireFixture(game, receiving);
        SimulationState m = game.Model;
        Rule(game, receiving, initial, 4, 30, 60);
        m.SourceLastPossession = receiving;
        m.SourceFlags = SourceGameFlags.PossessionChange | SourceGameFlags.AirCatch;
        m.BallKind = SourceBallKind.Bobble; m.BallMode = BallMode.InFlight; m.CarrierId = -1;
        m.SourcePlayEndX = initial == 0 ? -78f : 78f; m.SourcePlayEndZ = 0f;
        m.SourceCatchX = m.SourcePlayEndX; m.CatchSourceTick = m.SourceTick - 19;
        m.SourceTeamFire[receiving] = true; m.SourceLastReceiver[receiving] = receiving * 7 + 4;
        m.SourceConsecutiveLosses[receiving] = 2; m.SourceInterceptions[receiving] = 1;
        ActorState selector = m.Players[receiving * 7 + 5];
        selector.SourcePendingFireCheck = false;
        SourcePlayerDriver.SetSourcePosition(selector, new SourceVector3(m.SourcePlayEndX, 0, 0));
        scope = "FH01-incomplete-fourth-down";
        int[] expected = FireReference(game, selector, "P", SourcePlayEndCause.DeadBallInBounds);
        Bind<Action<SourcePlayEndCause, int, FieldVector, int, SourceVector3?>>(game, "EndSourcePlay")(
            SourcePlayEndCause.DeadBallInBounds, selector.Id, selector.Position, -1, null);
        if (real.IsC2Profile)
        {
            Check(m.SourceC2PendingResult != null && m.Phase == GamePhase.DeadBall,
                "FH01 actual C2 result caller queues the captured input");
            Check(m.SourceTeamFire[receiving] && m.SourceLastReceiver[receiving] == receiving * 7 + 4 &&
                m.SourceConsecutiveLosses[receiving] == 2, "FH01 queued result does not reset fire early");
            m.SourceTick++;
            Bind<Action>(game, "CommitSourceC2PlayEnd")();
            Check(m.SourceC2PendingResult == null, "FH01 actual next game-proc commits queued result once");
        }
        Check(m.SourceTeamFire[receiving] == (expected[receiving] != 0),
            "FH01 receiving fire/state survives incomplete original-offense restoration");
        FireEquals(expected, game, selector, true);
    }

    private static void FH01MirroredCases()
    {
        FH01IncompleteTurnover(0);
        FH01IncompleteTurnover(1);
    }

    private static int[] HandicapReference(GameSimulation game, SourcePlayDefinition[] history = null)
    {
        SimulationState m = game.Model;
        SourceHandicapSettings settings = m.SourceHandicapSettings;
        int humans = (SourceContactDriver.IsHumanTeam(m, TeamId.Home) ? 1 : 0)
            | (SourceContactDriver.IsHumanTeam(m, TeamId.Away) ? 2 : 0);
        var fields = new List<object> {
            "H", m.HomeScore, m.AwayScore, m.Quarter - 1, (int)m.Possession,
            Bit(m.SourceConfiguration.StationLayout == SourceStationLayout.FourStation), humans,
            m.SourceConfiguration.HomeTeam.TeamIndex, m.SourceConfiguration.AwayTeam.TeamIndex,
            settings.UserBaseAdjustment, settings.HumanWinStreak, Bit(settings.RandomDrones),
            settings.OffenseTeamMask, settings.DefenseTeamMask, settings.DronesTeamMask,
            Bit(m.SourceNoAssist), Bit(m.SourceSuperDrones), Bit(m.SourceDronesLose), Bit(m.SourceDronesWinning),
            PlayId(m.SourceOffensePlay)
        };
        fields.AddRange((history ?? m.SourceHandicapPlayHistory).Select(p => (object)PlayId(p)));
        return Reference(fields);
    }

    private static void HandicapEquals(int[] expected, GameSimulation game, bool allowUndefined = true)
    {
        SimulationState m = game.Model;
        bool valid = expected[0] >= 0 && expected[0] <= 11 && expected[1] >= 0 && expected[1] <= 11;
        if (!valid) undefinedOriginalConsumerCases++;
        bool rejected = false;
        long draws = m.SourceRandomDraws;
        try { Bind<Action>(game, "UpdateSourceHandicaps")(); }
        catch (NotSupportedException error)
        {
            rejected = error.Message.Contains("outside the original twelve consumer rows");
            if (!rejected) throw;
        }
        Check(valid ? !rejected : allowUndefined && rejected, "undefined original consumer index is not clamped");
        int[] actual = new[] { m.SourceTeamHandicap[0], m.SourceTeamHandicap[1],
            Bit(m.SourceDronesLose), Bit(m.SourceDronesWinning) }.Concat(m.SourceHandicapPlayHistory.Select(PlayId)).ToArray();
        for (int i = 0; i < actual.Length; i++)
            Check(actual[i] == expected[i], "original-C handicap field[" + i + "] expected=" + expected[i] + " actual=" + actual[i]);
        Check(m.SourceRandomDraws == draws, "set_handicaps consumes no RNG");
        if (valid) Check(m.SourceAiParameters.Level(0) == expected[0] && m.SourceAiParameters.Level(1) == expected[1],
            "AI reads actual dynamic team levels");
        if (!cases.ContainsKey(scope)) cases[scope] = 0;
        cases[scope]++;
    }

    private static void HandicapCases()
    {
        GameSimulation game = Game();
        SimulationState m = game.Model;
        scope = "handicap-scores";
        foreach (int humans in new[] { 0, 1, 2, 3 })
            foreach (int quarter in new[] { 0, 1, 2, 3, 6 })
                foreach (int margin in new[] { -16, -7, -2, 0, 1, 2, 7, 16 })
                {
                    m.SourceConfiguration = Configuration(humans, 0, 2);
                    m.SourceHandicapSettings = new SourceHandicapSettings();
                    m.HomeScore = Math.Max(0, margin); m.AwayScore = Math.Max(0, -margin);
                    m.Quarter = quarter + 1; m.Possession = TeamId.Home;
                    m.SourceOffensePlay = real.Playbook.GetPlay("play_spider");
                    m.SourceNoAssist = m.SourceSuperDrones = false;
                    m.SourceDronesLose = true; m.SourceDronesWinning = true;
                    Array.Clear(m.SourceHandicapPlayHistory, 0, 5);
                    HandicapEquals(HandicapReference(game), game);
                }
        scope = "handicap-powerups";
        foreach (int humans in new[] { 0, 1, 2, 3 })
            foreach (int side in new[] { 0, 1 })
                foreach (bool noassist in new[] { false, true })
                    foreach (bool super in new[] { false, true })
                    {
                        m.SourceConfiguration = Configuration(humans, 6, 2, SourceStationLayout.TwoStation);
                        m.SourceHandicapSettings = new SourceHandicapSettings(0, 4, true, 3, 3, 3, noassist, super);
                        m.HomeScore = 0; m.AwayScore = 7; m.Quarter = 3; m.Possession = (TeamId)side;
                        m.SourceOffensePlay = real.Playbook.GetPlay("play_zigzag");
                        m.SourceNoAssist = noassist; m.SourceSuperDrones = super;
                        Array.Clear(m.SourceHandicapPlayHistory, 0, 5);
                        HandicapEquals(HandicapReference(game), game);
                    }
        scope = "handicap-repetition";
        foreach (int human in new[] { 0, 1 })
            foreach (int side in new[] { 0, 1 })
                foreach (int repeated in new[] { 0, 1, 2, 3, 4, 5 })
                    foreach (int streak in new[] { 0, 1, 2, 3, 4 })
                    {
                        m.SourceConfiguration = Configuration(1 << human, 0, 2);
                        m.SourceHandicapSettings = new SourceHandicapSettings(-1, streak);
                        m.HomeScore = 2; m.AwayScore = 0; m.Quarter = 1; m.Possession = (TeamId)side;
                        m.SourceOffensePlay = real.Playbook.GetPlay("play_spider");
                        m.SourceNoAssist = m.SourceSuperDrones = false;
                        for (int i = 0; i < 5; i++) m.SourceHandicapPlayHistory[i] =
                            real.Playbook.GetPlay(i < repeated ? "play_spider" : "play_zigzag");
                        HandicapEquals(HandicapReference(game), game);
                        Check(m.SourceHandicapRepetitionBonus == (human == side ? repeated : 0),
                            "only selected human offense is recorded, before shifting five-play history");
                    }
        scope = "handicap-team-data";
        foreach (int humans in new[] { 0, 1, 2 })
            foreach (int team in Enumerable.Range(0, 31))
            {
                m.SourceConfiguration = Configuration(humans, team, team);
                m.SourceHandicapSettings = new SourceHandicapSettings();
                m.HomeScore = m.AwayScore = 0; m.Quarter = 1; m.Possession = TeamId.Home;
                m.SourceOffensePlay = real.Playbook.GetPlay("play_spider");
                m.SourceNoAssist = m.SourceSuperDrones = false;
                Array.Clear(m.SourceHandicapPlayHistory, 0, 5);
                HandicapEquals(HandicapReference(game), game);
            }
        scope = "handicap-unclamped-boundary";
        foreach (bool super in new[] { false, true })
            foreach (int repeated in new[] { 0, 1, 4, 5 })
            {
                m.SourceConfiguration = Configuration(1, 0, 1);
                m.SourceHandicapSettings = new SourceHandicapSettings(superDrones: super);
                m.HomeScore = m.AwayScore = 0; m.Quarter = 1; m.Possession = TeamId.Home;
                m.SourceOffensePlay = real.Playbook.GetPlay("play_spider");
                m.SourceNoAssist = false; m.SourceSuperDrones = super;
                for (int i = 0; i < 5; i++) m.SourceHandicapPlayHistory[i] =
                    i < repeated ? m.SourceOffensePlay : real.Playbook.GetPlay("play_zigzag");
                HandicapEquals(HandicapReference(game), game);
            }
        m.SourceConfiguration = Configuration(1, 0, 2);
        m.SourceHandicapSettings = new SourceHandicapSettings();
        m.HomeScore = 40; m.AwayScore = 0; m.SourceSuperDrones = false;
        for (int i = 0; i < 5; i++) m.SourceHandicapPlayHistory[i] = m.SourceOffensePlay;
        HandicapEquals(HandicapReference(game), game);
        scope = "handicap-drones-lose";
        m.SourceConfiguration = Configuration(1, 0, 6);
        m.HomeScore = 0; m.AwayScore = 7;
        Array.Clear(m.SourceHandicapPlayHistory, 0, 5);
        HandicapEquals(HandicapReference(game), game);
        Check(m.SourceDronesLose && m.SourceDronesWinning && m.SourceTeamHandicap[0] == 10
            && m.SourceTeamHandicap[1] == 0, "original base3 CPU-winning branch sets its actual catch/contact flags");
        scope = "handicap-undefined";
        m.SourceConfiguration = Configuration(1, 0, 2);
        m.SourceHandicapSettings = new SourceHandicapSettings(-3);
        bool missing = false;
        try { Bind<Action>(game, "UpdateSourceHandicaps")(); }
        catch (NotSupportedException error) { missing = error.Message.Contains("diff_settings[base-3]"); }
        Check(missing, "base below3 rejects original negative difficulty index without unsafe reference execution");
    }

    private static void NextWake(GameSimulation game, PlayerCommand command = default(PlayerCommand))
    {
        long before = game.SourceTick;
        game.Step(command);
        if (game.SourceTick == before) game.Step(default(PlayerCommand));
        Check(game.SourceTick == before + 1, "exactly one source57 wake, not retry-until-success");
    }

    private static void Park(GameSimulation game, int carrier)
    {
        foreach (ActorState actor in game.Model.Players)
        {
            actor.SourceMode = SourceActorMode.Dead;
            actor.SourceAttackMode = actor.SourceAttackTime = 0;
            actor.SourceVelocity = default(FieldVector);
            actor.SourceVerticalVelocity = 0f;
            actor.SourceFriction = 0f;
            if (actor.Id != carrier)
                SourcePlayerDriver.SetSourcePosition(actor, new SourceVector3(0f, 0f, 250f + actor.Id * 70f));
        }
        if (carrier >= 0) game.Model.Players[carrier].SourceMode = SourceActorMode.Receiver;
    }

    private static void SelectControlledPlay(GameSimulation game)
    {
        NextWake(game, new PlayerCommand(source: new SourceCommand(SourceAction.SelectPlay,
            new SourcePlaySelection("play_spider", SourcePlaybookKind.Offense))));
        NextWake(game, new PlayerCommand(source: new SourceCommand(SourceAction.SelectPlay,
            new SourcePlaySelection("play_safe_cover", SourcePlaybookKind.Defense))));
        Check(game.Model.Phase == GamePhase.PreSnap && game.Model.SourceForwardCatcher == -1,
            "actual selected-play lineup resets forward_catcher exactly once before the next play");
    }

    private static void IntegrationCases()
    {
        scope = "integration";
        GameSimulation game = Game();
        SimulationState m = game.Model;
        ActorState actor = FireFixture(game);
        Park(game, actor.Id);
        m.SourceOffensePlay = real.Playbook.GetPlay("play_spider");
        m.SourceDefensePlay = real.Playbook.GetPlay("play_safe_cover");
        m.SourceConsecutiveCompletions[0] = 0;
        m.SourceLastReceiver[0] = -1;
        for (int completion = 1; completion <= 3; completion++)
        {
            m.Phase = GamePhase.LiveBall;
            m.SourceMatchPhase = SourceMatchPhase.LiveBall;
            m.SourceFlags = SourceGameFlags.None;
            Check(m.SourceForwardCatcher == -1, "real lineup, not catch, resets the per-play forward catcher");
            m.CarrierId = -1;
            m.BallMode = BallMode.InFlight;
            m.BallKind = SourceBallKind.Pass;
            m.PassTicksElapsed = 40;
            m.TargetPlayerId = actor.Id;
            m.ThrowerId = 3;
            actor.NoCatchTicks = 0;
            actor.SourcePendingFireCheck = false;
            actor.AnimationState.FullBody("run_anim", actor, game.SourceTick, n => 99 % n, AnimationSelectionEvidence.SourceVerified);
            SourcePlayerDriver.SetSourcePosition(actor, new SourceVector3(SourceMatchRules.WorldPosition(40, 0), 0f, 0f));
            SourceBallDriver.SetPosition(m, new SourceVector3(actor.SourcePosition.X, actor.SourceRoot.Y, 0f));
            Bind<Action<ActorState>>(game, "TrySourceCatch")(actor);
            Check(m.CarrierId == actor.Id && actor.SourceControllerStation == 0,
                "actual catch transfers QB controller to receiver, not completion ownership");
            Check(m.SourceForwardCatcher == -1 && m.SourceConsecutiveCompletions[0] == completion - 1,
                "catch itself does not credit fire before CONTROL_TIME");
            for (int ticks = 0; ticks < 17; ticks++) NextWake(game);
            Check(m.SourceConsecutiveCompletions[0] == completion - 1 && !m.SourceTeamFire[0],
                "actual source17 remains below completion threshold");
            NextWake(game);
            Check(m.SourceConsecutiveCompletions[0] == completion && m.SourceLastReceiver[0] == 4
                && m.SourceForwardCatcher == 4 && m.SourceTeamFire[0] == (completion == 3),
                "actual source18 third receiver completion activates fire on this wake");
            SourceMatchView match = ((ISourceMatchState)game.State).Match;
            Check(match.HomeGameplay.ConsecutiveCompletions == completion
                && match.HomeGameplay.OnFire == (completion == 3) && match.ForwardCatcherPlayerId == 4,
                "published match reflects real completion state on the same wake");
            if (completion < 3)
            {
                m.SourcePlayEndX = SourcePlayerDriver.WorldPosition(actor).X;
                Bind<Action<int, SourceAnimationSignal>>(game, "SourceAnimationSideEffect")(actor.Id, SourceAnimationSignal.PlayerDown);
                if (real.IsC2Profile) NextWake(game);
                Check(m.Phase == GamePhase.DeadBall, "real PlayerDown callback reaches play result");
                NextWake(game, new PlayerCommand(source: new SourceCommand(SourceAction.Continue)));
                Check(m.Phase == GamePhase.PlaySelect && m.SourceConsecutiveCompletions[0] == completion,
                    "result dismissal preserves actual forward completion before next lineup reset");
                SelectControlledPlay(game);
                Park(game, actor.Id);
            }
        }
        observations.Add(new { identity = "actual-three-completions", game.SourceTick,
            receiver = m.SourceLastReceiver[0], controller = actor.SourceControllerStation,
            count = m.SourceConsecutiveCompletions[0], fire = m.SourceTeamFire[0] });
        int[] alreadyLit = FireReference(game, actor, "C");
        Bind<Action<ActorState>>(game, "SourceCarrierControl")(actor);
        FireEquals(alreadyLit, game, actor);
        m.SourceOffensivePlays[0] = 4;
        m.SourceFlags &= ~SourceGameFlags.PlayEnding;
        m.SourceVictimHeight = 0f;
        Bind<Action<ActorState, ActorState, int, int, string, int, bool>>(game, "StopSourceCarrier")(
            actor, m.Players[7], 0, 10000, "evade_vs_tackle", 2490, false);
        Check(m.CarrierId == 4 && m.SourcePendingTackleVictim == 4 && m.BallMode == BallMode.Held,
            "actual contact caller consumes produced offensive fire to suppress fumble");
        SourceRandomDraw final = ((SourceInjectedRandom)game.SourceRandomService).Trace.Last();
        Check(final.Site.BoundOrModulus == 100, "fire changes actual contact probability read and draw bound before stop");
        m.SourceFlags &= ~SourceGameFlags.PlayEnding;
        m.CarrierId = 3;
        m.Players[3].PendingThrowTarget = 4;
        m.Players[3].PendingThrowTurbo = false;
        m.Players[3].SourceMode = SourceActorMode.Quarterback;
        m.Players[3].SourceVelocity = default(FieldVector);
        actor.SourceVelocity = default(FieldVector);
        SourcePlayerDriver.SetSourcePosition(m.Players[3], new SourceVector3(SourceMatchRules.WorldPosition(30, 0), 0f, 0f));
        Bind<Action<int>>(game, "ReleaseSourcePass")(3);
        float speed = (float)Math.Sqrt(m.SourceBallVelocity.X * m.SourceBallVelocity.X
            + m.SourceBallVelocity.Z * m.SourceBallVelocity.Z);
        Check((m.SourceBallFlags & SourceBallFlags.FirePass) != 0 && Math.Abs(speed - 7.5f) < .0001f,
            "actual release caller reads produced fire for fire-pass flag and original7.5 speed");
        ForcedDownCase();
        AirContactCase();
        DefensiveCarryCase();
        HandicapCallerCase();
        HandicapSettingsCallerCase();
        QuickKickCallerCases();
        DismissalAndPeriodCases();
        var legacy = new GameSimulation(37);
        legacy.Step(new PlayerCommand(action: InputAction.SelectPlay, play: Blitz.Contracts.PlayId.Run));
        legacy.Step(new PlayerCommand(action: InputAction.Snap));
        Check(legacy.Profile == SimulationProfile.LegacyBaseline60Hz && legacy.State.Tick == 2
            && legacy.State.Phase == GamePhase.LiveBall && ((ISourceMatchState)legacy.State).Match == null,
            "LegacyBaseline60 preserves immediate snap and source-free state");
    }

    private static void ForcedDownCase()
    {
        GameSimulation game = Game();
        ActorState actor = FireFixture(game, 1);
        SimulationState m = game.Model;
        Park(game, actor.Id);
        m.SourceLastPossession = 0;
        m.CatchSourceTick = m.SourceTick - 1;
        m.SourceTeamFire[0] = true;
        m.SourceLastReceiver[0] = 4;
        m.SourceConsecutiveCompletions[0] = 3;
        m.SourceConsecutiveLosses[0] = 7;
        m.SourceLastReceiver[1] = 11;
        m.SourceConsecutiveCompletions[1] = 2;
        m.SourceConsecutiveLosses[1] = 1;
        int[] expected = FireReference(game, actor, "D");
        Bind<Action<int, SourceAnimationSignal>>(game, "SourceAnimationSideEffect")(actor.Id, SourceAnimationSignal.PlayerDown);
        // C1 commits immediately; use an end spot with no loss/first down fire effect.
        FireEquals(expected, game, actor);
        Check(m.SourceTeamFire[0] && m.SourceConsecutiveLosses[0] == 7 && m.SourceInterceptions[1] == 1,
            "forced player_down control is NOT ordinary CONTROL_TIME turnover fire reset");
        if (real.IsC2Profile)
        {
            Check(m.SourceC2PendingResult != null, "actual C2 callback queues before game_proc reset bookkeeping");
            NextWake(game);
            Check(m.SourceC2PendingResult == null, "next actual game_proc wake commits queued result once");
        }
    }

    private static void AirContactCase()
    {
        GameSimulation game = Game();
        ActorState actor = FireFixture(game);
        SimulationState m = game.Model;
        Park(game, actor.Id);
        m.SourceLastReceiver[0] = 4;
        m.SourceConsecutiveCompletions[0] = 2;
        m.SourceFlags = SourceGameFlags.AirCatch;
        SourcePlayerDriver.SetSourcePosition(actor, new SourceVector3(m.SourceCatchX, 3f, 0f));
        Bind<Action<ActorState>>(game, "SourceCarrierControl")(actor);
        Check(actor.SourcePendingFireCheck && !m.SourceTeamFire[0], "contact sees pending, not already lit airborne third completion");
        Bind<Action<ActorState, ActorState, int, int, string, int, bool>>(game, "StopSourceCarrier")(
            actor, m.Players[7], 0, 0, "evade_vs_tackle", 2490, false);
        int[] landing = FireReference(game, actor, "C", live: false, height: 0f);
        Bind<Action<int, SourceAnimationSignal>>(game, "SourceAnimationSideEffect")(actor.Id, SourceAnimationSignal.PlayerDown);
        SourcePlayerDriver.SetSourcePosition(actor, new SourceVector3(m.SourceCatchX, 0f, 0f));
        NextWake(game);
        Check(m.SourceConsecutiveCompletions[0] == landing[2] && m.SourceTeamFire[0] == (landing[0] != 0)
            && m.SourceForwardCatcher == landing[8] && !actor.SourcePendingFireCheck && landing[2] == 3,
            "original pending third completion lands after result, before dismissal counter reset");
    }

    private static void DefensiveCarryCase()
    {
        GameSimulation game = Game();
        ActorState actor = FireFixture(game);
        SimulationState m = game.Model;
        Rule(game, down: 3);
        Park(game, actor.Id);
        for (int loss = 0; loss < 2; loss++)
        {
            m.Phase = GamePhase.LiveBall;
            int side = (int)m.Possession;
            m.SourcePlayEndX = SourceMatchRules.WorldPosition(m.SourceRules.LineOfScrimmage - 5, side);
            SourcePlayerDriver.SetSourcePosition(actor, new SourceVector3(m.SourcePlayEndX, 0f, 0f));
            int[] expected = FireReference(game, actor, "P");
            Bind<Action<int, SourceAnimationSignal>>(game, "SourceAnimationSideEffect")(actor.Id, SourceAnimationSignal.PlayerDown);
            if (real.IsC2Profile)
            {
                int samples = 0;
                Func<IGameState, long, int> camera = game.SourceCameraHeadingProvider;
                game.SourceCameraHeadingProvider = (snapshot, tick) => {
                    FireEquals(expected, game, actor, true);
                    samples++;
                    return camera(snapshot, tick);
                };
                NextWake(game);
                game.SourceCameraHeadingProvider = camera;
                Check(samples == 1, "queued game_proc fire/result is observed before later player/ball processes");
            }
            else FireEquals(expected, game, actor, true);
        }
        Check(m.Possession == TeamId.Away && m.SourceTeamFire[1]
            && m.SourceConsecutiveLosses[1] == 2 && m.SourceLastReceiver[1] == 11,
            "loss before fourth-down turnover preserves newly lit defense with original receiver0 identity");
        SourceAiFrame frame = Bind<Func<bool, SourceAiFrame>>(game, "BuildSourceAiFrame")(true);
        Check(frame.OffenseFire && frame.FireReceiver == 11,
            "actual AI frame consumer sees defensive fire carried to offense, not old side");
        observations.Add(new { identity = "defensive-loss-turnover", fire = m.SourceTeamFire[1],
            receiver = m.SourceLastReceiver[1], losses = m.SourceConsecutiveLosses[1], side = (int)m.Possession });
    }

    private static void HandicapCallerCase()
    {
        GameSimulation game = Game();
        SimulationState m = game.Model;
        m.Phase = GamePhase.PlaySelect;
        m.SourceBeginPlayInitialized = false;
        m.HomeScore = 20; m.AwayScore = 0; m.Quarter = 4;
        Rule(game, los: 30);
        m.SourceConfiguration = Configuration(difficulty: 10);
        m.SourceOffensePlay = real.Playbook.GetPlay("play_spider");
        m.SourceDefensePlay = real.Playbook.GetPlay("play_safe_cover");
        Array.Clear(m.SourceHandicapPlayHistory, 0, 5);
        int[] expected = HandicapReference(game);
        SelectControlledPlay(game);
        Check(m.SourceTeamHandicap[0] == expected[0] && m.SourceTeamHandicap[1] == expected[1]
            && expected[0] != 5 && expected[1] != 5, "actual lineup computes original score/quarter handicap, not scalar Difficulty");
        foreach (ActorState actor in m.Players)
            Check(actor.SourceHandicap == expected[(int)actor.Team], "actor table index comes from its team's dynamic handicap");
        Check(m.SourceAiParameters.Level(0) == expected[0] && m.SourceAiParameters.Level(1) == expected[1],
            "AI reads actual dynamic team levels");
        var snapshot = new GameSnapshot(m);
        Check(snapshot.Match.HomeGameplay.Handicap == expected[0] && snapshot.Match.AwayGameplay.Handicap == expected[1]
            && snapshot.Players.All(p => p.Source.Handicap == expected[(int)p.Team]),
            "actor and match snapshots publish consumed handicap levels");
        ActorState victim = m.Players[4];
        m.Phase = GamePhase.LiveBall; m.CarrierId = 4; m.BallKind = SourceBallKind.Pass;
        m.CatchSourceTick = game.SourceTick - 18; m.SourceNoAssist = true;
        int high;
        SourceContactDriver.FumbleOrBobble(m, victim, m.Players[7], 0, 0, n => 99 % n, out high);
        Check(high == 100 * real.Playbook.HandicapValue("bob_up_ht", expected[0]),
            "actual contact probability consumes computed actor handicap row");
        observations.Add(new { identity = "lineup-handicap-actor-AI-contact", levels = m.SourceTeamHandicap.ToArray(), high });
    }

    private static void DismissalAndPeriodCases()
    {
        GameSimulation game = Game();
        ActorState actor = FireFixture(game);
        SimulationState m = game.Model;
        m.Phase = GamePhase.DeadBall;
        Park(game, actor.Id);
        m.SourceTeamFire[0] = true; m.SourceConsecutiveCompletions[0] = 3; m.SourceLastReceiver[0] = 4;
        int[] expected = FireReference(game, actor, "X");
        NextWake(game, new PlayerCommand(source: new SourceCommand(SourceAction.Continue)));
        FireEquals(expected, game, actor);
        Check(m.SourceTeamFire[0] && m.SourceLastReceiver[0] == 4 && m.SourceConsecutiveCompletions[0] == 0,
            "no forward catcher clears only counter at actual result dismissal, not fire or receiver");
        foreach (int quarter in new[] { 1, 2, 3, 4, 7 })
        {
            GameSimulation period = Game();
            ActorState periodActor = FireFixture(period);
            SimulationState state = period.Model;
            Park(period, periodActor.Id);
            state.Phase = GamePhase.DeadBall;
            state.Quarter = quarter;
            state.SourceClock = SourceClock.Create(0);
            state.SourceForwardCatcher = 4;
            state.SourceTeamFire[0] = true;
            state.SourceLastReceiver[0] = 4;
            state.SourceConsecutiveCompletions[0] = 3;
            NextWake(period, new PlayerCommand(source: new SourceCommand(SourceAction.Continue)));
            Check(state.SourceTeamFire[0] && state.SourceLastReceiver[0] == 4
                && state.SourceConsecutiveCompletions[0] == 3,
                "quarter/half/overtime/end gate does not reset original fire for period" + quarter);
        }
        game.Restart(37);
        Check(game.Model.SourceTeamFire.All(f => !f) && game.Model.SourceConsecutiveCompletions.All(n => n == 0)
            && game.Model.SourceConsecutiveLosses.All(n => n == 0) && game.Model.SourceLastReceiver.All(n => n == -1)
            && game.Model.SourceForwardCatcher == -1, "program-boot Restart resets all original fire state");
    }

    private static void HandicapSettingsCallerCase()
    {
        var settings = new SourceHandicapSettings(0, 2, true, 1, 0, 1, true, true);
        var game = new GameSimulation(real.Animations, real.Geometry, real.Playbook, Configuration(1, 0, 2),
            seed => new SourceInjectedRandom(site => 99, new SourceRandomProvenance("fire-handicap/raw-settings",
                "Controlled program-boot globals", "raw-settings", SourceRandomEvidence.ExternalUnverified)),
            37, real.Rosters, new SourcePresentationSettings(TeamId.Home, new[] { -1, -1, -1, -1 }, 0),
            real.MovementRuntime, new SourceMovementSettings(0, 0, false, 0, 0, false),
            real.IsC2Profile ? SourceC2Bootstrap.FromOriginalProgramBootAtSoundBanks(real.C2Data) : null, settings);
        int[] expected = HandicapReference(game, new SourcePlayDefinition[5]);
        Check(game.Model.SourceNoAssist && game.Model.SourceSuperDrones
            && game.Model.SourceTeamHandicap[0] == expected[0] && game.Model.SourceTeamHandicap[1] == expected[1],
            "actual source constructor consumes explicit raw handicap globals using strict existing roster data");
    }

    private static void QuickKickCallerCases()
    {
        foreach (int kicker in new[] { 0, 1 })
        {
            GameSimulation game = Game();
            SimulationState m = game.Model;
            m.HomeScore = 20; m.AwayScore = 0; m.Quarter = 4;
            Rule(game, kicker, kicker, type: SourcePlayType.Kickoff);
            Bind<Action<bool, bool>>(game, "PrepareSourceKickoff")(false, false);
            int receiving = 1 - kicker;
            int level = m.SourceTeamHandicap[receiving];
            float[] expected = ReferenceOutput(new object[] { "K", receiving, level, 99, 99 })
                .Split(' ').Select(x => float.Parse(x, CultureInfo.InvariantCulture)).ToArray();
            SourceVector3 root = SourcePlayerDriver.WorldPosition(m.Players[m.SourceQuickReturnActor]);
            float[] actual = { root.X, root.Z, m.SourceBallPosition.X, m.SourceBallPosition.Y, m.SourceBallPosition.Z,
                m.SourceBallVelocity.X, m.SourceBallVerticalVelocity, m.SourceBallVelocity.Z,
                m.SourceBallTarget.X, m.SourceBallTarget.Z };
            for (int i = 0; i < actual.Length; i++)
                Check(BitConverter.SingleToInt32Bits(actual[i]) == BitConverter.SingleToInt32Bits(expected[i]),
                    "actual quick-kick original source bits field[" + i + "] use computed receiving handicap");
            SourceRandomDraw[] draws = ((SourceInjectedRandom)game.SourceRandomService).Trace.TakeLast(2).ToArray();
            Check(draws[0].Site.BoundOrModulus == (int)expected[10]
                && draws[1].Site.BoundOrModulus == (int)expected[11] && draws.All(d => d.Raw == 99),
                "actual quick-kick consumes original ordered two raw draws/bounds without neutral-row fallback");
            Check(level != 5, "quick-kick fixtures exercise both nonneutral handicap extremes");
        }
    }

    private static int Main(string[] args)
    {
        var result = new Dictionary<string, object> { { "schema", "source-fire-handicap-checks/v1" },
            { "status", "failed" }, { "full_game_or_native_qualified", false } };
        try
        {
            if (args.Length != 7) throw new ArgumentException("fixture SAFN original-binary output section C2-inputs-or-- M1-inputs-or--");
            Directory.CreateDirectory(args[3]);
            transcript = new StreamWriter(Path.Combine(args[3], "original-c-transcript.txt"));
            original = Process.Start(new ProcessStartInfo(args[2]) { RedirectStandardInput = true,
                RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false });
            real = FullKernelInputs.Load(args[0], soundFunctionsPath: args[1],
                c2InputsPath: args[5] == "-" ? null : args[5], c2M1InputsPath: args[6] == "-" ? null : args[6]);
            string section = args[4];
            if (section == "all" || section == "fire") { CarrierCases(); ResultCases(); }
            if (section == "all" || section == "handicap") HandicapCases();
            if (section == "all" || section == "integration") IntegrationCases();
            if (section == "all" || section == "FH01") FH01MirroredCases();
            if (section == "FH01-home") FH01IncompleteTurnover(0);
            if (section == "FH01-away") FH01IncompleteTurnover(1);
            Check(assertions > 0 && referenceCalls > 0, "actual field/reference assertions are nonempty");
            result["status"] = "passed";
        }
        catch (FireHandicapAssertion error) { result["failed_assertion"] = error.Message; }
        catch (Exception error) { result["error"] = error.ToString(); }
        finally
        {
            if (original != null)
            {
                original.StandardInput.Close();
                if (!original.WaitForExit(5000)) { original.Kill(); original.WaitForExit(); }
                result["original_exit_code"] = original.ExitCode;
                if (original.ExitCode != 0) result["status"] = "failed";
                result["original_stderr"] = original.StandardError.ReadToEnd();
                original.Dispose();
            }
            if (transcript != null) transcript.Dispose();
            result["assertions"] = assertions; result["reference_calls"] = referenceCalls;
            result["undefined_original_consumer_cases_rejected"] = undefinedOriginalConsumerCases;
            result["cases"] = cases; result["observations"] = observations;
            string text = JsonSerializer.Serialize(result);
            if (args.Length > 3) File.WriteAllText(Path.Combine(args[3], "checks.json"), text + "\n");
            Console.WriteLine(text);
        }
        return (string)result["status"] == "passed" ? 0 : 1;
    }
}
