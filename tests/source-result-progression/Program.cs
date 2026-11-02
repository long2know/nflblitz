using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using Blitz.Contracts;
using Blitz.Gameplay;

internal static class SourceResultProgressionChecks
{
    private const string ContractSha256 = "b4a998d4fafec3fd04b0e24c3ea0b1e2d7cfd9941a0307c3d633d186b2d83f36";
    private sealed class LiteralFailure : Exception { }
    private sealed class LiteralCase
    {
        internal string Id;
        internal string[] Names;
        internal double[] Expected;
        internal JsonElement Definition;
    }

    private static readonly List<object> Fields = new List<object>();
    private static readonly List<string> Cases = new List<string>();
    private static readonly Dictionary<string, LiteralCase> Inventory = new Dictionary<string, LiteralCase>();
    private static LiteralCase current;
    private static int cursor;
    private static object failure;
    private static JsonElement plan;

    private static string[] Strings(JsonElement array)
    {
        var result = new List<string>();
        foreach (JsonElement item in array.EnumerateArray()) result.Add(item.GetString());
        return result.ToArray();
    }

    private static double[] Numbers(JsonElement array)
    {
        var result = new List<double>();
        foreach (JsonElement item in array.EnumerateArray()) result.Add(item.GetDouble());
        return result.ToArray();
    }

    private static void Load(JsonElement root)
    {
        plan = root.GetProperty("literalPlanFrozenBeforeCompile");
        string[] names = Strings(plan.GetProperty("fieldOrder"));
        foreach (JsonElement item in plan.GetProperty("tableCases").EnumerateArray())
        {
            string id = item.GetProperty("id").GetString();
            Inventory.Add(id, new LiteralCase
            {
                Id = id, Names = names, Expected = Numbers(item.GetProperty("expected")), Definition = item
            });
        }
        foreach (JsonElement item in plan.GetProperty("specialCases").EnumerateArray())
        {
            string id = item.GetProperty("id").GetString();
            string[] fields;
            double[] values;
            if (id == "pending.saved-input")
            {
                LiteralCase basis = Inventory[item.GetProperty("baseExpectedCase").GetString()];
                var fs = new List<string>(basis.Names);
                fs.AddRange(Strings(item.GetProperty("additionalFields")));
                fields = fs.ToArray();
                var vs = new List<double>(basis.Expected);
                vs.AddRange(Numbers(item.GetProperty("additionalExpected")));
                values = vs.ToArray();
            }
            else
            {
                fields = Strings(item.GetProperty("fields"));
                values = Numbers(item.GetProperty("expected"));
            }
            Inventory.Add(id, new LiteralCase { Id = id, Names = fields, Expected = values, Definition = item });
        }
    }

    private static void Check(string name, double actual)
    {
        if (cursor >= current.Names.Length || current.Names[cursor] != name)
            throw new InvalidOperationException("Observation order differs from the pinned literal inventory.");
        double expected = current.Expected[cursor++];
        Fields.Add(new { @case = current.Id, field = name, expected, actual });
        if (actual == expected) return;
        failure = new { @case = current.Id, field = name, expected, actual };
        throw new LiteralFailure();
    }

    private static int Bit(bool value) { return value ? 1 : 0; }

    private static SourcePlayEndInput Input(string id, out int carrier)
    {
        JsonElement v = Inventory[id].Definition.GetProperty("input");
        carrier = v[15].GetInt32();
        return new SourcePlayEndInput(new SourceRulesState(v[0].GetInt32(), v[1].GetInt32(),
            v[2].GetInt32(), v[3].GetInt32(), v[4].GetInt32(), (SourcePlayType)v[5].GetInt32(),
            v[6].GetInt32() != 0), (SourcePlayEndCause)v[8].GetInt32(),
            (SourceBallType)v[9].GetInt32(), (SourceGameFlags)v[10].GetInt32(),
            v[7].GetInt32(), v[11].GetSingle(), v[12].GetSingle(), v[13].GetSingle(),
            (SourcePlayResult)v[14].GetInt32());
    }

    private static void Seed(SimulationState model, SourcePlayEndInput input, int carrier)
    {
        model.SourceRules = input.State;
        model.SourceLastPossession = input.LastPossession;
        model.SourceFlags = input.Flags;
        model.SourcePlayEndX = input.PlayEndX;
        model.SourcePlayEndZ = input.PlayEndZ;
        model.SourceFumbleX = input.FumbleX;
        model.SourceLastResult = input.PreviousResult;
        model.CarrierId = carrier;
        model.Possession = (TeamId)input.State.OffenseSide;
        JsonElement seed = plan.GetProperty("tableFreshSeed");
        for (int side = 0; side < 2; side++)
        {
            model.SourceFirstDowns[side] = seed.GetProperty("firstDowns")[side].GetInt32();
            model.SourceFourthDownConversions[side] = seed.GetProperty("fourthDownConversions")[side].GetInt32();
            model.SourceLostFumbles[side] = seed.GetProperty("lostFumbles")[side].GetInt32();
        }
    }

    private static SourcePlayResolution Accumulate(SimulationState model, SourcePlayEndInput input, int carrier)
    {
        SourcePlayResolution resolution = SourceMatchRules.Resolve(input);
        if (!resolution.TransitionApplied)
            throw new InvalidOperationException("Declared component branch did not resolve.");
        model.AccumulateSourcePlayEndStats(input, resolution, carrier);
        return resolution;
    }

    private static void ObserveTable(SimulationState model, SourcePlayResolution r)
    {
        Check("firstDowns.home", model.SourceFirstDowns[0]);
        Check("firstDowns.away", model.SourceFirstDowns[1]);
        Check("fourthDownConversions.home", model.SourceFourthDownConversions[0]);
        Check("fourthDownConversions.away", model.SourceFourthDownConversions[1]);
        Check("classification.status", (int)r.Classification.Status);
        Check("classification.result", (int)r.Classification.Result);
        Check("classification.offense", r.Classification.OffenseSide);
        Check("fieldEnd.raw", r.UnclampedFieldEnd);
        Check("fieldEnd.bounded", r.FieldEnd);
        Check("next.offense", r.State.OffenseSide);
        Check("next.initialOffense", r.State.InitialOffenseSide);
        Check("next.down", r.State.Down);
        Check("next.los", r.State.LineOfScrimmage);
        Check("next.firstDown", r.State.FirstDown);
        Check("next.playType", (int)r.State.PlayType);
        Check("justGotFirstDown", Bit(r.JustGotFirstDown));
        Check("turnoverOnDowns", Bit(r.TurnoverOnDowns));
        Check("preserveDisplayedLines", Bit(r.PreserveDisplayedLines));
        Check("lostFumbles.home", model.SourceLostFumbles[0]);
        Check("lostFumbles.away", model.SourceLostFumbles[1]);
        Check("flags", (int)r.Flags);
        Check("classification.playEndX", r.Classification.PlayEndX);
    }

    private static void Table(string id)
    {
        int carrier;
        SourcePlayEndInput input = Input(id, out carrier);
        var model = new SimulationState();
        Seed(model, input, carrier);
        ObserveTable(model, Accumulate(model, input, carrier));
    }

    private static void Counts(string prefix, SimulationState model)
    {
        Check(prefix + ".first.home", model.SourceFirstDowns[0]);
        Check(prefix + ".first.away", model.SourceFirstDowns[1]);
        Check(prefix + ".conversions.home", model.SourceFourthDownConversions[0]);
        Check(prefix + ".conversions.away", model.SourceFourthDownConversions[1]);
    }

    private static void ApplyLiteral(SimulationState model, string id)
    {
        int carrier;
        SourcePlayEndInput input = Input(id, out carrier);
        Accumulate(model, input, carrier);
    }

    private static void Lifecycle()
    {
        var model = new SimulationState();
        Counts("fresh", model);
        ApplyLiteral(model, "fourth.earned.home");
        Counts("after-home", model);
        ApplyLiteral(model, "new-possession.changed-owner.away");
        Counts("after-new-possession", model);
        ApplyLiteral(model, "fourth.earned.away");
        Counts("after-away", model);
        Counts("fresh-again", new SimulationState());
    }

    private static void Pending()
    {
        int carrier;
        SourcePlayEndInput original = Input("noop.down.different-owner.away", out carrier);
        var model = new SimulationState();
        Seed(model, original, carrier);
        var pending = new SourceC2PendingEnd(model, original.Cause, carrier,
            new FieldVector(0f, 0f), -1, original.BallType);
        model.SourceRules = new SourceRulesState(0, 0, 2, 95, 100);
        model.SourceLastPossession = 0;
        model.SourcePlayEndX = 312f;
        model.SourceLastResult = SourcePlayResult.NewPossession;
        model.Possession = TeamId.Home;
        ObserveTable(model, Accumulate(model, pending.Input, carrier));
        Check("captured.down", pending.Input.State.Down);
        Check("captured.lastPossession", pending.Input.LastPossession);
        Check("captured.endX", pending.Input.PlayEndX);
        Check("epoch", model.SourceC2EndEpoch);
        Check("late.down", model.SourceRules.Down);
        Check("late.los", model.SourceRules.LineOfScrimmage);
        Check("late.lastPossession", model.SourceLastPossession);
        Check("late.endX", model.SourcePlayEndX);
    }

    private static void ViewCounts(string prefix, SourceResultProgressionStatsView view)
    {
        Check(prefix + ".specified", Bit(view != null));
        Check(prefix + ".first.home", view.HomeFirstDowns);
        Check(prefix + ".first.away", view.AwayFirstDowns);
        Check(prefix + ".conversions.home", view.HomeFourthDownConversions);
        Check(prefix + ".conversions.away", view.AwayFourthDownConversions);
    }

    private static void Snapshot()
    {
        SimulationState model = Program.ComponentState();
        model.SourceFirstDowns[0] = 3;
        model.SourceFirstDowns[1] = 7;
        model.SourceFourthDownConversions[0] = 2;
        model.SourceFourthDownConversions[1] = 5;
        GameSnapshot s0 = new GameSnapshot(model);
        ViewCounts("s0", s0.Match.ResultProgressionStats);
        ApplyLiteral(model, "fourth.earned.home");
        GameSnapshot s1 = new GameSnapshot(model);
        ViewCounts("s1", s1.Match.ResultProgressionStats);
        model.SourceFirstDowns[0] = 9;
        model.SourceFirstDowns[1] = 11;
        model.SourceFourthDownConversions[0] = 13;
        model.SourceFourthDownConversions[1] = 15;
        GameSnapshot s2 = new GameSnapshot(model);
        ViewCounts("s2", s2.Match.ResultProgressionStats);
        ViewCounts("retained-s0", s0.Match.ResultProgressionStats);
        ViewCounts("retained-s1", s1.Match.ResultProgressionStats);
        using (JsonDocument diagnostics = JsonDocument.Parse(JsonSerializer.Serialize(Program.FixtureDiagnostics())))
        {
            JsonElement d = diagnostics.RootElement;
            foreach (string name in new[] { "sourceFrames", "selectedFrame", "joints", "actorsInitialized",
                "layersInitialized", "initializationStepsPerLayer", "randomRequests" })
                Check("fixture." + name, d.GetProperty(name).GetInt32());
            Check("fixture.originalProvenanceSupplied", Bit(d.GetProperty("originalProvenanceSupplied").GetBoolean()));
        }
    }

    private sealed class ViewInputs
    {
        internal readonly SourceMatchConfiguration Configuration = Program.Configuration();
        internal readonly SourcePeriodView Period = new SourcePeriodView(0);
        internal readonly SourcePossessionView Possession = new SourcePossessionView(
            TeamId.Home, TeamId.Home, TeamId.Home, TeamId.Home, SourceCaptainSlot.First);
        internal readonly SourceQuarterScore[] Scores = { new SourceQuarterScore(0, 0, 0) };
        internal readonly SourceStationView[] Stations = new SourceStationView[0];
        internal readonly SourcePlaceKickStatsView Kick = new SourcePlaceKickStatsView(0, 0, 0, 0);
        internal readonly SourcePlayEndStatsView End = new SourcePlayEndStatsView(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
        internal readonly SourceCompletedPassStatsView Completed = new SourceCompletedPassStatsView(0, 0, 0, 0, 0, 0);
        internal readonly SourcePassCompletionsView Completions = new SourcePassCompletionsView(0, 0);
        internal readonly SourceInterceptionsView Interceptions = new SourceInterceptionsView(0, 0);
        internal readonly SourceTimeOfPossessionView Time = new SourceTimeOfPossessionView(0, 0);

        internal SourceMatchView Create(int overload, SourceResultProgressionStatsView progression)
        {
            switch (overload)
            {
                case 0: return new SourceMatchView(Configuration, SourcePlayKind.Normal, SourceMatchPhase.PlayResult,
                    Period, Possession, Scores, Stations);
                case 1: return new SourceMatchView(Time, Configuration, SourcePlayKind.Normal, SourceMatchPhase.PlayResult,
                    Period, Possession, Scores, Stations);
                case 2: return new SourceMatchView(Interceptions, Time, Configuration, SourcePlayKind.Normal,
                    SourceMatchPhase.PlayResult, Period, Possession, Scores, Stations);
                case 3: return new SourceMatchView(Completions, Interceptions, Time, Configuration, SourcePlayKind.Normal,
                    SourceMatchPhase.PlayResult, Period, Possession, Scores, Stations);
                case 4: return new SourceMatchView(Completed, Completions, Interceptions, Time, Configuration,
                    SourcePlayKind.Normal, SourceMatchPhase.PlayResult, Period, Possession, Scores, Stations);
                case 5: return new SourceMatchView(End, Completed, Completions, Interceptions, Time, Configuration,
                    SourcePlayKind.Normal, SourceMatchPhase.PlayResult, Period, Possession, Scores, Stations);
                case 6: return new SourceMatchView(Kick, End, Completed, Completions, Interceptions, Time, Configuration,
                    SourcePlayKind.Normal, SourceMatchPhase.PlayResult, Period, Possession, Scores, Stations);
                case 7: return new SourceMatchView(progression, Kick, End, Completed, Completions, Interceptions, Time,
                    Configuration, SourcePlayKind.Normal, SourceMatchPhase.PlayResult, Period, Possession, Scores, Stations);
                default: throw new ArgumentOutOfRangeException(nameof(overload));
            }
        }
    }

    private static int RejectRange(Action action, string parameter)
    {
        try { action(); return 0; }
        catch (ArgumentOutOfRangeException e) { return Bit(e.ParamName == parameter); }
    }

    private static void Dto()
    {
        var view = new SourceResultProgressionStatsView(1, 2, 3, 4);
        Check("homeFirstDowns", view.HomeFirstDowns);
        Check("awayFirstDowns", view.AwayFirstDowns);
        Check("homeFourthDownConversions", view.HomeFourthDownConversions);
        Check("awayFourthDownConversions", view.AwayFourthDownConversions);
        Check("reject.home-first-negative", RejectRange(
            () => new SourceResultProgressionStatsView(-1, 0, 0, 0), "homeFirstDowns"));
        Check("reject.away-first-negative", RejectRange(
            () => new SourceResultProgressionStatsView(0, -1, 0, 0), "awayFirstDowns"));
        Check("reject.home-conversion-negative", RejectRange(
            () => new SourceResultProgressionStatsView(0, 0, -1, 0), "homeFourthDownConversions"));
        Check("reject.away-conversion-negative", RejectRange(
            () => new SourceResultProgressionStatsView(0, 0, 0, -1), "awayFourthDownConversions"));
        var inputs = new ViewInputs();
        int rejected = 0;
        try { inputs.Create(7, null); }
        catch (ArgumentNullException e) { rejected = Bit(e.ParamName == "resultProgressionStats"); }
        Check("reject.null-progression-view", rejected);
        SourceMatchView match = inputs.Create(7, view);
        Check("eighth.progression-reference", Bit(ReferenceEquals(match.ResultProgressionStats, view)));
        Check("eighth.place-kick-reference", Bit(ReferenceEquals(match.PlaceKickStats, inputs.Kick)));
        Check("eighth.play-end-reference", Bit(ReferenceEquals(match.PlayEndStats, inputs.End)));
        Check("eighth.completed-pass-reference", Bit(ReferenceEquals(match.CompletedPassStats, inputs.Completed)));
        Check("eighth.completions-reference", Bit(ReferenceEquals(match.PassCompletions, inputs.Completions)));
        Check("eighth.interceptions-reference", Bit(ReferenceEquals(match.Interceptions, inputs.Interceptions)));
        Check("eighth.time-reference", Bit(ReferenceEquals(match.TimeOfPossession, inputs.Time)));
        for (int overload = 0; overload < 7; overload++)
            Check("legacy" + overload + ".unspecified", Bit(inputs.Create(overload, null).ResultProgressionStats == null));
    }

    private static void Run(string id)
    {
        current = Inventory[id];
        cursor = 0;
        switch (id)
        {
            case "progression.lifecycle": Lifecycle(); break;
            case "pending.saved-input": Pending(); break;
            case "snapshot.detachment": Snapshot(); break;
            case "dto.validation": Dto(); break;
            default: Table(id); break;
        }
        if (cursor != current.Names.Length)
            throw new InvalidOperationException("Incomplete literal observation case.");
        Cases.Add(id);
    }

    private static int Main(string[] args)
    {
        try
        {
            if (args.Length != 2 && args.Length != 4 || args[0] != "--contract"
                || args.Length == 4 && args[2] != "--case")
                throw new ArgumentException("Use --contract <pinned-contract> [--case <declared-id>].");
            byte[] bytes = File.ReadAllBytes(args[1]);
            using (SHA256 sha = SHA256.Create())
                if (BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant() != ContractSha256)
                    throw new InvalidOperationException("Captured contract identity mismatch.");
            using (JsonDocument document = JsonDocument.Parse(bytes))
            {
                Load(document.RootElement);
                if (args.Length == 4)
                {
                    if (!Inventory.ContainsKey(args[3])) throw new ArgumentException("Unknown selected case.");
                    Run(args[3]);
                }
                else
                {
                    foreach (JsonElement item in plan.GetProperty("tableCases").EnumerateArray())
                        Run(item.GetProperty("id").GetString());
                    foreach (JsonElement item in plan.GetProperty("specialCases").EnumerateArray())
                        Run(item.GetProperty("id").GetString());
                }
            }
            Console.WriteLine(JsonSerializer.Serialize(new { status = "passed", cases = Cases, fields = Fields }));
            return 0;
        }
        catch (LiteralFailure)
        {
            Console.WriteLine(JsonSerializer.Serialize(new
                { status = "assertion-failed", cases = Cases, fields = Fields, failure }));
            return 2;
        }
        catch (Exception e)
        {
            Console.WriteLine(JsonSerializer.Serialize(new
                { status = "technical-error", exception = e.GetType().FullName, cases = Cases, fields = Fields }));
            return 3;
        }
    }
}
