using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Blitz.Contracts;
using Blitz.Gameplay;

internal static class SourcePlaceKickStatsChecks
{
    private const string Synthetic = "SYNTHETIC_NON_ORIGINAL_PLACE_KICK_COMPONENT_VALUES";

    private sealed class NamedAssertion : Exception
    {
        internal readonly string Field;
        internal NamedAssertion(string field) : base(field) { Field = field; }
    }

    private sealed class LiteralCase
    {
        internal string Id;
        internal int[] Input;
        internal string Catalog;
        internal string Selected;
        internal string[] Names;
        internal string[] Units;
        internal int[] Expected;
    }

    private sealed class OldGroups
    {
        internal readonly SourcePlayEndStatsView PlayEnd =
            new SourcePlayEndStatsView(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
        internal readonly SourceCompletedPassStatsView Completed =
            new SourceCompletedPassStatsView(0, 0, 0, 0, 0, 0);
        internal readonly SourcePassCompletionsView Completions = new SourcePassCompletionsView(0, 0);
        internal readonly SourceInterceptionsView Interceptions = new SourceInterceptionsView(0, 0);
        internal readonly SourceTimeOfPossessionView Time = new SourceTimeOfPossessionView(0, 0);
        internal readonly SourceMatchConfiguration Configuration;
        internal readonly SourcePeriodView Period = new SourcePeriodView(0);
        internal readonly SourcePossessionView Possession = new SourcePossessionView(
            TeamId.Home, TeamId.Home, TeamId.Home, TeamId.Home, SourceCaptainSlot.First);
        internal readonly SourceQuarterScore[] Scores = { new SourceQuarterScore(0, 0, 0) };
        internal readonly SourceStationView[] Stations = new SourceStationView[0];

        internal OldGroups(SourceMatchConfiguration configuration) { Configuration = configuration; }
    }

    private static readonly List<object> Fields = new List<object>();
    private static readonly List<string> Completed = new List<string>();
    private static readonly List<LiteralCase> Cases = new List<LiteralCase>();
    private static LiteralCase current;
    private static int currentField;
    private static SourcePlaybook catalogA;
    private static SourcePlaybook catalogB;

    private static int[] Integers(JsonElement values)
    {
        var result = new List<int>();
        foreach (JsonElement value in values.EnumerateArray()) result.Add(value.GetInt32());
        return result.ToArray();
    }

    private static string[] Strings(JsonElement values)
    {
        var result = new List<string>();
        foreach (JsonElement value in values.EnumerateArray()) result.Add(value.GetString());
        return result.ToArray();
    }

    private static void LoadInventory(string path)
    {
        using (JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(path)))
        {
            JsonElement root = document.RootElement;
            if (root.GetProperty("schema").GetString() != "source-place-kick-statistics-literals/v1")
                throw new InvalidOperationException("Unexpected literal inventory schema.");
            string[] producerNames = Strings(root.GetProperty("producerNames"));
            string[] producerUnits = Strings(root.GetProperty("producerUnits"));
            foreach (JsonElement row in root.GetProperty("producerCases").EnumerateArray())
            {
                Cases.Add(new LiteralCase
                {
                    Id = row.GetProperty("id").GetString(),
                    Input = Integers(row.GetProperty("input")),
                    Catalog = row.GetProperty("catalog").GetString(),
                    Selected = row.GetProperty("selected").GetString(),
                    Names = producerNames,
                    Units = producerUnits,
                    Expected = Integers(row.GetProperty("expected"))
                });
            }
            foreach (JsonElement row in root.GetProperty("apiCases").EnumerateArray())
            {
                Cases.Add(new LiteralCase
                {
                    Id = row.GetProperty("id").GetString(),
                    Input = null,
                    Catalog = null,
                    Selected = null,
                    Names = Strings(row.GetProperty("fields")),
                    Units = Strings(row.GetProperty("units")),
                    Expected = Integers(row.GetProperty("expected"))
                });
            }
        }
        if (Cases.Count != 32)
            throw new InvalidOperationException("The full ordered thirty-two-case inventory is required.");
    }

    private static void Check(string name, int actual)
    {
        if (currentField >= current.Names.Length || current.Names[currentField] != name)
            throw new InvalidOperationException("Literal field order changed: " + current.Id + "." + name);
        int expected = current.Expected[currentField];
        string field = current.Id + "." + name;
        Fields.Add(new { @case = current.Id, field, unit = current.Units[currentField], expected, actual });
        currentField++;
        if (actual != expected) throw new NamedAssertion(field);
    }

    private static int Bit(bool value) { return value ? 1 : 0; }

    private static SourcePlaybook NewCatalog()
    {
        var route = new SourceRouteProgram("synthetic_place_kick_end", Synthetic, 1, new[] { 0 });
        var slots = new SourceFormationSlot[7];
        var routes = new SourceRouteProgram[7];
        for (int index = 0; index < 7; index++)
        {
            slots[index] = new SourceFormationSlot(new SourcePlanar(0f, 0f),
                "synthetic_place_kick_end", SourceActorMode.Lineup, SourceFormationControl.None);
            routes[index] = route;
        }
        var formation = new SourceFormation("synthetic_place_kick_formation", Synthetic, 1, slots);
        var definitions = new[]
        {
            new SourcePlayDefinition("play_field_goal", "play_field_goal", Synthetic, 1,
                SourcePlaySide.Offense, SourcePlayFlags.FieldGoal, formation, routes),
            new SourcePlayDefinition("play_fake_field_goal", "play_fake_field_goal", Synthetic, 1,
                SourcePlaySide.Offense, SourcePlayFlags.FakeFieldGoal, formation, routes),
            new SourcePlayDefinition("play_extra_point", "play_extra_point", Synthetic, 1,
                SourcePlaySide.Offense, SourcePlayFlags.None, formation, routes),
            new SourcePlayDefinition("synthetic_ordinary", "synthetic_ordinary", Synthetic, 1,
                SourcePlaySide.Offense, SourcePlayFlags.None, formation, routes),
            new SourcePlayDefinition("synthetic_flagged_fg", "synthetic_flagged_fg", Synthetic, 1,
                SourcePlaySide.Offense, SourcePlayFlags.FieldGoal, formation, routes)
        };
        return new SourcePlaybook(Synthetic, Synthetic, definitions,
            new[] { route }, new SourceWeightedPlayPool[0]);
    }

    private static SourcePlaybook Catalog(string name)
    {
        if (name == null) return null;
        if (name == "A") return catalogA;
        if (name == "B") return catalogB;
        throw new InvalidOperationException("Undeclared synthetic catalog.");
    }

    private static SourcePlayDefinition Selected(string name)
    {
        if (name == null) return null;
        string[] parts = name.Split('.');
        if (parts.Length != 2) throw new InvalidOperationException("Undeclared selected reference.");
        return Catalog(parts[0]).GetPlay(parts[1]);
    }

    private static SourceRulesState Rules(int[] value)
    {
        return new SourceRulesState(value[0], value[1], value[2], value[3], value[4],
            (SourcePlayType)value[5]);
    }

    private static SourcePlayEndInput Input(LiteralCase row)
    {
        int[] v = row.Input;
        if (row.Id == "15-legacy-input-identity-unspecified")
            return new SourcePlayEndInput(Rules(v), (SourcePlayEndCause)v[6],
                (SourceBallType)v[7], (SourceGameFlags)v[8], v[9], v[10], v[11], v[12],
                (SourcePlayResult)v[13]);
        return new SourcePlayEndInput(Catalog(row.Catalog), Selected(row.Selected), Rules(v),
            (SourcePlayEndCause)v[6], (SourceBallType)v[7], (SourceGameFlags)v[8],
            v[9], v[10], v[11], v[12], (SourcePlayResult)v[13]);
    }

    private static void Seed(SimulationState model, LiteralCase row)
    {
        int[] v = row.Input;
        model.SourceFieldGoalAttempts[0] = 10;
        model.SourceFieldGoalAttempts[1] = 20;
        model.SourceFieldGoals[0] = 30;
        model.SourceFieldGoals[1] = 40;
        model.SourceRandomDraws = 0;
        model.SourceKickReturnCatch = 30;
        model.SourceRules = Rules(v);
        model.Possession = (TeamId)v[0];
        model.SourceFlags = (SourceGameFlags)v[8];
        model.SourceLastPossession = v[9];
        model.SourcePlayEndX = v[10];
        model.SourcePlayEndZ = v[11];
        model.SourceFumbleX = v[12];
        model.SourceLastResult = (SourcePlayResult)v[13];
        model.CarrierId = v[14];
        model.SourcePlaybook = Catalog(row.Catalog);
        model.SourceOffensePlay = Selected(row.Selected);
    }

    private static void CountView(string prefix, SourcePlaceKickStatsView view)
    {
        Check(prefix + ".attempts.home", view.HomeFieldGoalAttempts);
        Check(prefix + ".attempts.away", view.AwayFieldGoalAttempts);
        Check(prefix + ".goals.home", view.HomeFieldGoals);
        Check(prefix + ".goals.away", view.AwayFieldGoals);
    }

    private static void Observe(SimulationState model, SourcePlayEndInput input,
        SourcePlayResolution resolution)
    {
        Check("classification.status", (int)resolution.Classification.Status);
        Check("classification.result", (int)resolution.Classification.Result);
        Check("classification.offense", resolution.Classification.OffenseSide);
        Check("next.offense", resolution.State.OffenseSide);
        Check("next.play-type", (int)resolution.State.PlayType);
        Check("points.home", resolution.Side0Points);
        Check("points.away", resolution.Side1Points);
        Check("input.selected.same-as-frozen", Bit(ReferenceEquals(input.InitialOffensePlay,
            Selected(current.Selected))));
        Check("input.catalog.same-as-frozen", Bit(ReferenceEquals(input.SelectedPlaybook,
            Catalog(current.Catalog))));
        Check("state.attempts.home", model.SourceFieldGoalAttempts[0]);
        Check("state.attempts.away", model.SourceFieldGoalAttempts[1]);
        Check("state.goals.home", model.SourceFieldGoals[0]);
        Check("state.goals.away", model.SourceFieldGoals[1]);
        SourcePlaceKickStatsView saved = new GameSnapshot(model).Match.PlaceKickStats;
        CountView("snapshot", saved);
        model.SourceFieldGoalAttempts[0] += 1000;
        model.SourceFieldGoalAttempts[1] += 1000;
        model.SourceFieldGoals[0] += 1000;
        model.SourceFieldGoals[1] += 1000;
        CountView("snapshot.detached", saved);
        Check("rng.draws", checked((int)model.SourceRandomDraws));
    }

    private static void Producer(SimulationState model, int index)
    {
        Seed(model, current);
        SourcePlayEndInput input;
        SourcePlayResolution resolution;
        if (index == 23 || index == 24)
        {
            model.SourceTick = 123;
            model.SourceC2EndEpoch = 7;
            int[] v = current.Input;
            var pending = new SourceC2PendingEnd(model, (SourcePlayEndCause)v[6], v[14],
                new FieldVector(0f, 0f), -1, (SourceBallType)v[7]);
            model.SourceOffensePlay = catalogB.GetPlay(index == 23
                ? "play_fake_field_goal" : "play_field_goal");
            model.SourcePlaybook = catalogB;
            model.SourceRules = new SourceRulesState(1, 1, 1, 30, 90);
            model.Possession = TeamId.Away;
            model.SourceLastPossession = 1;
            input = pending.Input;
        }
        else input = Input(current);
        resolution = SourceMatchRules.Resolve(input);
        if (index == 25)
        {
            foreach (int earlier in new[] { 0, 2, 3 })
            {
                input = Input(Cases[earlier]);
                resolution = SourceMatchRules.Resolve(input);
                model.AccumulateSourcePlayEndStats(input, resolution, Cases[earlier].Input[14]);
            }
        }
        else model.AccumulateSourcePlayEndStats(input, resolution, current.Input[14]);
        Observe(model, input, resolution);
    }

    private static int RejectNegative(int a, int b, int c, int d, string parameter)
    {
        try { new SourcePlaceKickStatsView(a, b, c, d); }
        catch (ArgumentOutOfRangeException error) { return Bit(error.ParamName == parameter); }
        return 0;
    }

    private static SourceMatchView NewMatch(SourcePlaceKickStatsView placeKick, OldGroups old)
    {
        return new SourceMatchView(placeKick, old.PlayEnd, old.Completed, old.Completions,
            old.Interceptions, old.Time, old.Configuration, SourcePlayKind.Normal,
            SourceMatchPhase.PlayResult, old.Period, old.Possession, old.Scores, old.Stations);
    }

    private static void ApiCase(SimulationState model, int index)
    {
        if (index == 26)
        {
            var fresh = new SimulationState();
            Check("attempts.home", fresh.SourceFieldGoalAttempts[0]);
            Check("attempts.away", fresh.SourceFieldGoalAttempts[1]);
            Check("goals.home", fresh.SourceFieldGoals[0]);
            Check("goals.away", fresh.SourceFieldGoals[1]);
        }
        else if (index == 27)
        {
            Check("negative.home-attempts.rejected",
                RejectNegative(-1, 0, 0, 0, "homeFieldGoalAttempts"));
            Check("negative.away-attempts.rejected",
                RejectNegative(0, -1, 0, 0, "awayFieldGoalAttempts"));
            Check("negative.home-goals.rejected",
                RejectNegative(0, 0, -1, 0, "homeFieldGoals"));
            Check("negative.away-goals.rejected",
                RejectNegative(0, 0, 0, -1, "awayFieldGoals"));
        }
        else if (index == 28)
        {
            var old = new OldGroups(model.SourceConfiguration);
            var placeKick = new SourcePlaceKickStatsView(1, 2, 3, 4);
            SourceMatchView match = NewMatch(placeKick, old);
            Check("place-kick.same-reference", Bit(ReferenceEquals(match.PlaceKickStats, placeKick)));
            Check("play-end.same-reference", Bit(ReferenceEquals(match.PlayEndStats, old.PlayEnd)));
            Check("completed-pass.same-reference", Bit(ReferenceEquals(match.CompletedPassStats, old.Completed)));
            Check("pass-completions.same-reference", Bit(ReferenceEquals(match.PassCompletions, old.Completions)));
            Check("interceptions.same-reference", Bit(ReferenceEquals(match.Interceptions, old.Interceptions)));
            Check("time-of-possession.same-reference", Bit(ReferenceEquals(match.TimeOfPossession, old.Time)));
            Check("configuration.same-reference", Bit(ReferenceEquals(match.Configuration, old.Configuration)));
        }
        else if (index == 29)
        {
            var old = new OldGroups(model.SourceConfiguration);
            var views = new[]
            {
                new SourceMatchView(old.Configuration, SourcePlayKind.Normal, SourceMatchPhase.PlayResult,
                    old.Period, old.Possession, old.Scores, old.Stations),
                new SourceMatchView(old.Time, old.Configuration, SourcePlayKind.Normal, SourceMatchPhase.PlayResult,
                    old.Period, old.Possession, old.Scores, old.Stations),
                new SourceMatchView(old.Interceptions, old.Time, old.Configuration, SourcePlayKind.Normal,
                    SourceMatchPhase.PlayResult, old.Period, old.Possession, old.Scores, old.Stations),
                new SourceMatchView(old.Completions, old.Interceptions, old.Time, old.Configuration,
                    SourcePlayKind.Normal, SourceMatchPhase.PlayResult, old.Period, old.Possession,
                    old.Scores, old.Stations),
                new SourceMatchView(old.Completed, old.Completions, old.Interceptions, old.Time,
                    old.Configuration, SourcePlayKind.Normal, SourceMatchPhase.PlayResult, old.Period,
                    old.Possession, old.Scores, old.Stations),
                new SourceMatchView(old.PlayEnd, old.Completed, old.Completions, old.Interceptions,
                    old.Time, old.Configuration, SourcePlayKind.Normal, SourceMatchPhase.PlayResult,
                    old.Period, old.Possession, old.Scores, old.Stations)
            };
            Check("configuration-overload.null", Bit(views[0].PlaceKickStats == null));
            Check("time-overload.null", Bit(views[1].PlaceKickStats == null));
            Check("interception-overload.null", Bit(views[2].PlaceKickStats == null));
            Check("completion-overload.null", Bit(views[3].PlaceKickStats == null));
            Check("completed-pass-overload.null", Bit(views[4].PlaceKickStats == null));
            Check("play-end-overload.null", Bit(views[5].PlaceKickStats == null));
        }
        else if (index == 30)
        {
            int rejected = 0;
            try { NewMatch(null, new OldGroups(model.SourceConfiguration)); }
            catch (ArgumentNullException error) { rejected = Bit(error.ParamName == "placeKickStats"); }
            Check("place-kick.correct-null-guard", rejected);
        }
        else if (index == 31)
        {
            model.SourceFieldGoalAttempts[0] = 41;
            model.SourceFieldGoalAttempts[1] = 42;
            model.SourceFieldGoals[0] = 43;
            model.SourceFieldGoals[1] = 44;
            SourcePlaceKickStatsView saved = new GameSnapshot(model).Match.PlaceKickStats;
            CountView("snapshot", saved);
            model.SourceFieldGoalAttempts[0] = model.SourceFieldGoalAttempts[1] = 0;
            model.SourceFieldGoals[0] = model.SourceFieldGoals[1] = 0;
            CountView("detached", saved);
            Check("nonsource.match-null", Bit(new GameSnapshot(new SimulationState()).Match == null));
        }
        else throw new InvalidOperationException("Undeclared API case.");
    }

    private static void Output(string status, string failedField, string error)
    {
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            schema = "source-place-kick-statistics-component/v1",
            status,
            entryPoint = "SourcePlaceKickStatsChecks.Main",
            completedCases = Completed.Count,
            completedCaseIds = Completed,
            fieldCount = Fields.Count,
            fields = Fields,
            failedField,
            error,
            fixture = Program.FixtureDiagnostics(),
            genuineCommonOrSchedulerExecuted = false,
            originalAssetsAdmitted = false,
            wholeGameParityClaimed = false
        }));
    }

    public static int Main(string[] args)
    {
        try
        {
            if (args.Length != 2 || args[0] != "--inventory")
                throw new ArgumentException("Only the complete pinned --inventory input is supported.");
            LoadInventory(args[1]);
            catalogA = NewCatalog();
            catalogB = NewCatalog();
            SimulationState model = Program.ComponentState();
            for (int index = 0; index < Cases.Count; index++)
            {
                current = Cases[index];
                currentField = 0;
                if (index < 26) Producer(model, index);
                else ApiCase(model, index);
                if (currentField != current.Expected.Length)
                    throw new InvalidOperationException("A literal case omitted fields.");
                Completed.Add(current.Id);
            }
            if (Fields.Count != 603) throw new InvalidOperationException("The full603 fields are required.");
            Output("passed", null, null);
            return 0;
        }
        catch (NamedAssertion error)
        {
            Output("assertion-failed", error.Field, null);
            return 2;
        }
        catch (Exception error)
        {
            Output("error", null, error.ToString());
            return 1;
        }
    }
}
