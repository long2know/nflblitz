using System;
using System.Collections.Generic;
using System.Text.Json;
using Blitz.Contracts;
using Blitz.Gameplay;

namespace Blitz.SourceFourthDownAttemptsComponent
{
    internal static class Program
    {
        private sealed class SemanticFailure : Exception
        {
            internal readonly string Case, Field;
            internal readonly object Expected, Actual;
            internal SemanticFailure(string id, string field, object expected, object actual)
            { Case = id; Field = field; Expected = expected; Actual = actual; }
        }

        private sealed class Catalog
        {
            internal readonly Dictionary<string, SourcePlayDefinition> Objects =
                new Dictionary<string, SourcePlayDefinition>(StringComparer.Ordinal);
            internal readonly SourcePlaybook Book;
            internal Catalog()
            {
                var route = new SourceRouteProgram("synthetic_route", "synthetic-component", 1, new[] { 0 });
                var slots = new SourceFormationSlot[7];
                for (int i = 0; i < slots.Length; i++)
                    slots[i] = new SourceFormationSlot(new SourcePlanar(0f, 0f), route.Identity,
                        SourceActorMode.Lineup, (SourceFormationControl)0);
                var formation = new SourceFormation("synthetic_formation", "synthetic-component", 1, slots);
                var routes = new[] { route, route, route, route, route, route, route };
                Objects.Add("P", Play("play_punt", SourcePlayFlags.Punt, formation, routes));
                Objects.Add("F", Play("play_field_goal", SourcePlayFlags.FieldGoal, formation, routes));
                Objects.Add("N", Play("ordinary", SourcePlayFlags.None, formation, routes));
                Objects.Add("FP", Play("fake_punt", SourcePlayFlags.FakePunt, formation, routes));
                Objects.Add("FF", Play("fake_field_goal", SourcePlayFlags.FakeFieldGoal, formation, routes));
                Objects.Add("PF", Play("flag_punt", SourcePlayFlags.Punt, formation, routes));
                Objects.Add("GF", Play("flag_field_goal", SourcePlayFlags.FieldGoal, formation, routes));
                Objects.Add("PC", Play("play_punt", SourcePlayFlags.Punt, formation, routes));
                Objects.Add("FC", Play("play_field_goal", SourcePlayFlags.FieldGoal, formation, routes));
                Book = new SourcePlaybook("synthetic-component", "synthetic-component",
                    new[] { Objects["P"], Objects["F"], Objects["N"], Objects["FP"],
                        Objects["FF"], Objects["PF"], Objects["GF"] },
                    new[] { route }, new SourceWeightedPlayPool[0]);
            }

            private static SourcePlayDefinition Play(string identity, SourcePlayFlags flags,
                SourceFormation formation, SourceRouteProgram[] routes)
            {
                return new SourcePlayDefinition(identity, identity, "synthetic-component", 1,
                    SourcePlaySide.Offense, flags, formation, routes);
            }

            internal SourcePlaybook Missing(string identity)
            {
                var plays = new List<SourcePlayDefinition>();
                foreach (KeyValuePair<string, SourcePlayDefinition> pair in Book.Plays)
                    if (pair.Key != identity) plays.Add(pair.Value);
                return new SourcePlaybook("synthetic-component", "synthetic-component",
                    plays, Book.Routes.Values, new SourceWeightedPlayPool[0]);
            }
        }

        private static readonly object[][] StateRows =
        {
            new object[] { "home-normal-fourth", 0, 4, "N", 0, 0, 1, 0, false, SourcePlayType.Normal },
            new object[] { "away-normal-fourth", 1, 4, "N", 0, 0, 0, 1, false, SourcePlayType.Normal },
            new object[] { "first-down-not-attempt", 0, 1, "N", 0, 0, 0, 0, false, SourcePlayType.Normal },
            new object[] { "second-down-not-attempt", 1, 2, "N", 0, 0, 0, 0, false, SourcePlayType.Normal },
            new object[] { "third-down-not-attempt", 0, 3, "N", 0, 0, 0, 0, false, SourcePlayType.Normal },
            new object[] { "home-canonical-punt", 0, 4, "P", 0, 0, 0, 0, false, SourcePlayType.Normal },
            new object[] { "away-canonical-field-goal", 1, 4, "F", 0, 0, 0, 0, false, SourcePlayType.Normal },
            new object[] { "home-fake-punt", 0, 4, "FP", 0, 0, 1, 0, false, SourcePlayType.Normal },
            new object[] { "away-fake-field-goal", 1, 4, "FF", 0, 0, 0, 1, false, SourcePlayType.Normal },
            new object[] { "noncanonical-punt-flag", 0, 4, "PF", 0, 0, 1, 0, false, SourcePlayType.Normal },
            new object[] { "noncanonical-field-goal-flag", 1, 4, "GF", 0, 0, 0, 1, false, SourcePlayType.Normal },
            new object[] { "punt-name-clone-not-canonical", 0, 4, "PC", 0, 0, 1, 0, false, SourcePlayType.Normal },
            new object[] { "field-goal-name-clone-not-canonical", 1, 4, "FC", 0, 0, 0, 1, false, SourcePlayType.Normal },
            new object[] { "home-cumulative", 0, 4, "N", 5, 9, 6, 9, false, SourcePlayType.Normal },
            new object[] { "away-cumulative", 1, 4, "N", 5, 9, 5, 10, false, SourcePlayType.Normal },
            new object[] { "no-first-down-does-not-suppress", 0, 4, "N", 0, 0, 1, 0, true, SourcePlayType.Normal },
            new object[] { "metadata-playtype-does-not-substitute", 0, 4, "N", 0, 0, 1, 0, false, SourcePlayType.Punt },
            new object[] { "canonical-reference-not-playtype", 1, 4, "F", 0, 0, 0, 0, false, SourcePlayType.Normal }
        };

        private static readonly List<object> Cases = new List<object>();
        private static readonly List<object[]> Fixture = new List<object[]>();
        private static List<object[]> fields;
        private static string currentCase, selected = "all", selectedField;
        private static int assertions, fixtureCalls;
        private static bool selectedSeen, selectedFieldSeen;
        private static SimulationState fixture;
        private static Catalog catalog;

        private static bool Start(string id)
        {
            if (selected != "all" && selected != id) return false;
            selectedSeen = true;
            currentCase = id;
            fields = new List<object[]>();
            Cases.Add(new Dictionary<string, object> { { "id", id }, { "fields", fields } });
            return true;
        }

        private static void Check(string name, object expected, object actual)
        {
            fields.Add(new[] { (object)name, actual });
            if (selectedField != null && name != selectedField) return;
            selectedFieldSeen = true;
            assertions++;
            if (!Equals(expected, actual))
                throw new SemanticFailure(currentCase, name, expected, actual);
        }

        private static void FixtureCheck(string name, object expected, object actual)
        {
            Fixture.Add(new[] { (object)name, actual });
            assertions++;
            if (!Equals(expected, actual))
                throw new SemanticFailure("fixture", name, expected, actual);
        }

        private static void Configure(SimulationState state, int side, int down,
            SourcePlayType type = SourcePlayType.Normal, bool noFirst = false)
        {
            state.SourceRules = new SourceRulesState(side, side, down, 30, 90, type, noFirst);
            state.Possession = (TeamId)side;
            state.Down = down;
            state.SourceBeginPlayInitialized = true;
            state.SourcePlaybook = catalog.Book;
        }

        private static void SeedUnrelated(SimulationState state)
        {
            state.SourceOffensivePlays[0] = 17; state.SourceOffensivePlays[1] = 19;
            state.SourceFieldGoalAttempts[0] = 7; state.SourceFieldGoalAttempts[1] = 11;
            state.SourceFirstDowns[0] = 3; state.SourceFirstDowns[1] = 5;
            state.SourceFourthDownConversions[0] = 2; state.SourceFourthDownConversions[1] = 4;
        }

        private static void Unrelated(SimulationState state)
        {
            Check("offensive.home", 17, state.SourceOffensivePlays[0]);
            Check("offensive.away", 19, state.SourceOffensivePlays[1]);
            Check("fieldGoalAttempts.home", 7, state.SourceFieldGoalAttempts[0]);
            Check("fieldGoalAttempts.away", 11, state.SourceFieldGoalAttempts[1]);
            Check("firstDowns.home", 3, state.SourceFirstDowns[0]);
            Check("firstDowns.away", 5, state.SourceFirstDowns[1]);
            Check("conversions.home", 2, state.SourceFourthDownConversions[0]);
            Check("conversions.away", 4, state.SourceFourthDownConversions[1]);
            Check("beginInitialized", true, state.SourceBeginPlayInitialized);
        }

        private static void StateCases()
        {
            foreach (object[] row in StateRows)
            {
                if (!Start((string)row[0])) continue;
                var state = new SimulationState();
                Configure(state, (int)row[1], (int)row[2], (SourcePlayType)row[9], (bool)row[8]);
                SeedUnrelated(state);
                state.SourceFourthDownAttempts[0] = (int)row[4];
                state.SourceFourthDownAttempts[1] = (int)row[5];
                state.AccumulateSourceFourthDownAttemptAtSelectionReturn(catalog.Book, catalog.Objects[(string)row[3]]);
                Check("attempts.home", row[6], state.SourceFourthDownAttempts[0]);
                Check("attempts.away", row[7], state.SourceFourthDownAttempts[1]);
                Unrelated(state);
            }
        }

        private static void FreshReset()
        {
            if (!Start("fresh-reset")) return;
            var first = new SimulationState();
            Check("newState.home", 0, first.SourceFourthDownAttempts[0]);
            Check("newState.away", 0, first.SourceFourthDownAttempts[1]);
            first.SourceFourthDownAttempts[0] = 8; first.SourceFourthDownAttempts[1] = 9;
            var second = new SimulationState();
            Check("seeded.home", 8, first.SourceFourthDownAttempts[0]);
            Check("seeded.away", 9, first.SourceFourthDownAttempts[1]);
            Check("secondNew.home", 0, second.SourceFourthDownAttempts[0]);
            Check("secondNew.away", 0, second.SourceFourthDownAttempts[1]);
        }

        private static void Temporal(string id, string returned, string[] later, int home, int away)
        {
            if (!Start(id)) return;
            Configure(fixture, 0, 4);
            fixture.Phase = GamePhase.PreSnap; fixture.SourceMatchPhase = SourceMatchPhase.PreSnap;
            fixture.SourceFourthDownAttempts[0] = fixture.SourceFourthDownAttempts[1] = 0;
            fixture.SourceOffensePlay = catalog.Objects[returned];
            fixture.AccumulateSourceFourthDownAttemptAtSelectionReturn(catalog.Book, fixture.SourceOffensePlay);
            Check("afterReturn.home", home, fixture.SourceFourthDownAttempts[0]);
            Check("afterReturn.away", away, fixture.SourceFourthDownAttempts[1]);
            var first = new GameSnapshot(fixture);
            foreach (string identity in later) fixture.SourceOffensePlay = catalog.Objects[identity];
            var second = new GameSnapshot(fixture);
            Check("afterLater.home", home, fixture.SourceFourthDownAttempts[0]);
            Check("afterLater.away", away, fixture.SourceFourthDownAttempts[1]);
            Check("firstSnapshot.home", home, first.Match.FourthDownAttempts.HomeFourthDownAttempts);
            Check("firstSnapshot.away", away, first.Match.FourthDownAttempts.AwayFourthDownAttempts);
            Check("laterSnapshot.home", home, second.Match.FourthDownAttempts.HomeFourthDownAttempts);
            Check("laterSnapshot.away", away, second.Match.FourthDownAttempts.AwayFourthDownAttempts);
        }

        private static void Snapshot()
        {
            if (!Start("snapshot")) return;
            fixture.SourceFourthDownAttempts[0] = 2; fixture.SourceFourthDownAttempts[1] = 5;
            fixture.SourceFirstDowns[0] = 11; fixture.SourceFirstDowns[1] = 13;
            fixture.SourceFourthDownConversions[0] = 3; fixture.SourceFourthDownConversions[1] = 7;
            var first = new GameSnapshot(fixture);
            Check("first.home", 2, first.Match.FourthDownAttempts.HomeFourthDownAttempts);
            Check("first.away", 5, first.Match.FourthDownAttempts.AwayFourthDownAttempts);
            Check("first.firstDowns.home", 11, first.Match.ResultProgressionStats.HomeFirstDowns);
            Check("first.firstDowns.away", 13, first.Match.ResultProgressionStats.AwayFirstDowns);
            Check("first.conversions.home", 3, first.Match.ResultProgressionStats.HomeFourthDownConversions);
            Check("first.conversions.away", 7, first.Match.ResultProgressionStats.AwayFourthDownConversions);
            fixture.SourceFourthDownAttempts[0] = 8; fixture.SourceFourthDownAttempts[1] = 9;
            var second = new GameSnapshot(fixture);
            Check("afterMutation.first.home", 2, first.Match.FourthDownAttempts.HomeFourthDownAttempts);
            Check("afterMutation.first.away", 5, first.Match.FourthDownAttempts.AwayFourthDownAttempts);
            Check("second.home", 8, second.Match.FourthDownAttempts.HomeFourthDownAttempts);
            Check("second.away", 9, second.Match.FourthDownAttempts.AwayFourthDownAttempts);
            Check("model.home", 8, fixture.SourceFourthDownAttempts[0]);
            Check("model.away", 9, fixture.SourceFourthDownAttempts[1]);
        }

        private static void Rejection(Action action, string exception, string parameter)
        {
            Exception caught = null;
            try { action(); }
            catch (Exception error) { caught = error; }
            Check("rejection", true, caught != null);
            Check("exception", exception, caught == null ? null : caught.GetType().Name);
            var argument = caught as ArgumentException;
            Check("parameter", parameter, argument == null ? null : argument.ParamName);
        }

        private static SourceMatchView NewView(SourceFourthDownAttemptsView attempts, SourceMatchView basis)
        {
            return new SourceMatchView(attempts, basis.ResultProgressionStats, basis.PlaceKickStats,
                basis.PlayEndStats, basis.CompletedPassStats, basis.PassCompletions, basis.Interceptions,
                basis.TimeOfPossession, basis.Configuration, basis.PlayKind, basis.Phase, basis.Period,
                basis.Possession, basis.QuarterScores, basis.Stations);
        }

        private static void DtoCases()
        {
            if (Start("dto-zero"))
            {
                var view = new SourceFourthDownAttemptsView(0, 0);
                Check("home", 0, view.HomeFourthDownAttempts); Check("away", 0, view.AwayFourthDownAttempts);
            }
            if (Start("dto-values"))
            {
                var view = new SourceFourthDownAttemptsView(2, 5);
                Check("home", 2, view.HomeFourthDownAttempts); Check("away", 5, view.AwayFourthDownAttempts);
            }
            if (Start("dto-negative-home"))
                Rejection(() => new SourceFourthDownAttemptsView(-1, 0),
                    "ArgumentOutOfRangeException", "homeFourthDownAttempts");
            if (Start("dto-negative-away"))
                Rejection(() => new SourceFourthDownAttemptsView(0, -1),
                    "ArgumentOutOfRangeException", "awayFourthDownAttempts");
            if (Start("dto-new-null"))
            {
                SourceMatchView basis = new GameSnapshot(fixture).Match;
                Rejection(() => NewView(null, basis), "ArgumentNullException", "fourthDownAttempts");
            }
        }

        private static void LegacyOverloads()
        {
            if (!Start("legacy-overloads")) return;
            SourceMatchView b = new GameSnapshot(fixture).Match;
            var views = new[]
            {
                new SourceMatchView(b.Configuration, b.PlayKind, b.Phase, b.Period, b.Possession, b.QuarterScores, b.Stations),
                new SourceMatchView(b.TimeOfPossession, b.Configuration, b.PlayKind, b.Phase, b.Period, b.Possession, b.QuarterScores, b.Stations),
                new SourceMatchView(b.Interceptions, b.TimeOfPossession, b.Configuration, b.PlayKind, b.Phase, b.Period, b.Possession, b.QuarterScores, b.Stations),
                new SourceMatchView(b.PassCompletions, b.Interceptions, b.TimeOfPossession, b.Configuration, b.PlayKind, b.Phase, b.Period, b.Possession, b.QuarterScores, b.Stations),
                new SourceMatchView(b.CompletedPassStats, b.PassCompletions, b.Interceptions, b.TimeOfPossession, b.Configuration, b.PlayKind, b.Phase, b.Period, b.Possession, b.QuarterScores, b.Stations),
                new SourceMatchView(b.PlayEndStats, b.CompletedPassStats, b.PassCompletions, b.Interceptions, b.TimeOfPossession, b.Configuration, b.PlayKind, b.Phase, b.Period, b.Possession, b.QuarterScores, b.Stations),
                new SourceMatchView(b.PlaceKickStats, b.PlayEndStats, b.CompletedPassStats, b.PassCompletions, b.Interceptions, b.TimeOfPossession, b.Configuration, b.PlayKind, b.Phase, b.Period, b.Possession, b.QuarterScores, b.Stations),
                new SourceMatchView(b.ResultProgressionStats, b.PlaceKickStats, b.PlayEndStats, b.CompletedPassStats, b.PassCompletions, b.Interceptions, b.TimeOfPossession, b.Configuration, b.PlayKind, b.Phase, b.Period, b.Possession, b.QuarterScores, b.Stations)
            };
            Check("count", 8, views.Length);
            for (int i = 0; i < views.Length; i++) Check("old." + i + ".unspecified", true, views[i].FourthDownAttempts == null);
            var counts = new SourceFourthDownAttemptsView(2, 5);
            var added = new[] { NewView(counts, b) };
            SourceMatchView current = added[0];
            Check("new.count", 1, added.Length);
            Check("new.nonnull", true, current.FourthDownAttempts != null);
            Check("new.sameView", true, ReferenceEquals(counts, current.FourthDownAttempts));
            Check("new.sameConfiguration", true, ReferenceEquals(b.Configuration, current.Configuration));
            Check("new.sameProgression", true, ReferenceEquals(b.ResultProgressionStats, current.ResultProgressionStats));
        }

        private static void Invalid(string id, string exception, string parameter)
        {
            if (!Start(id)) return;
            var state = new SimulationState();
            Configure(state, 0, 4); SeedUnrelated(state);
            state.SourceFourthDownAttempts[0] = 5; state.SourceFourthDownAttempts[1] = 9;
            SourcePlaybook book = id == "invalid-book" ? null
                : id == "missing-canonical-punt" ? catalog.Missing("play_punt")
                : id == "missing-canonical-field-goal" ? catalog.Missing("play_field_goal") : catalog.Book;
            SourcePlayDefinition play = id == "invalid-selected" ? null : catalog.Objects["N"];
            Rejection(() => state.AccumulateSourceFourthDownAttemptAtSelectionReturn(book, play), exception, parameter);
            Check("attempts.home", 5, state.SourceFourthDownAttempts[0]);
            Check("attempts.away", 9, state.SourceFourthDownAttempts[1]);
            Unrelated(state);
        }

        private static void FixtureFields()
        {
            using (JsonDocument document = JsonDocument.Parse(JsonSerializer.Serialize(global::Program.FixtureDiagnostics())))
            {
                JsonElement d = document.RootElement;
                FixtureCheck("componentStateCalls", 1, fixtureCalls);
                FixtureCheck("frames", 1, d.GetProperty("sourceFrames").GetInt32());
                FixtureCheck("frameIndex", 0, d.GetProperty("selectedFrame").GetInt32());
                FixtureCheck("joints", 15, d.GetProperty("joints").GetInt32());
                FixtureCheck("actors", 14, d.GetProperty("actorsInitialized").GetInt32());
                FixtureCheck("layers", 28, d.GetProperty("layersInitialized").GetInt32());
                FixtureCheck("initializationStepsPerLayer", 1, d.GetProperty("initializationStepsPerLayer").GetInt32());
                FixtureCheck("rngRequests", 0, d.GetProperty("randomRequests").GetInt32());
                FixtureCheck("originalProvenance", false, d.GetProperty("originalProvenanceSupplied").GetBoolean());
                // These are fixture input declarations, independently bound to the unchanged fixture pin.
                FixtureCheck("originalGitBlobSha1", null, null);
                FixtureCheck("originalRawSha256", null, null);
            }
        }

        public static int Main(string[] args)
        {
            SemanticFailure failure = null;
            string technical = null;
            try
            {
                for (int i = 0; i < args.Length; i++)
                {
                    if (args[i] == "--case" && i + 1 < args.Length) selected = args[++i];
                    else if (args[i] == "--field" && i + 1 < args.Length) selectedField = args[++i];
                    else throw new ArgumentException("Unknown component argument.");
                }
                if (selected == "all" && selectedField != null) throw new ArgumentException("A field requires a selected case.");
                fixture = global::Program.ComponentState(); fixtureCalls++;
                catalog = new Catalog();
                StateCases(); FreshReset();
                Temporal("normal-to-punt-audible", "N", new[] { "P" }, 1, 0);
                Temporal("punt-to-normal-audible", "P", new[] { "N" }, 0, 0);
                Temporal("field-goal-to-fake-audible", "F", new[] { "FF" }, 0, 0);
                Temporal("several-later-identity-changes", "N", new[] { "F", "N", "P" }, 1, 0);
                Snapshot(); DtoCases(); LegacyOverloads();
                Invalid("invalid-book", "ArgumentNullException", "selectedPlaybook");
                Invalid("invalid-selected", "ArgumentNullException", "selectedOffensePlay");
                Invalid("missing-canonical-punt", "InvalidOperationException", null);
                Invalid("missing-canonical-field-goal", "InvalidOperationException", null);
                if (!selectedSeen || selectedField != null && !selectedFieldSeen)
                    throw new ArgumentException("Unknown selected literal assertion.");
            }
            catch (SemanticFailure error) { failure = error; }
            catch (Exception error) { technical = error.GetType().Name; }
            if (fixture != null)
            {
                try { FixtureFields(); }
                catch (SemanticFailure error) { if (failure == null) failure = error; }
                catch (Exception error) { technical = error.GetType().Name; }
            }
            var payload = new Dictionary<string, object>
            {
                { "schema", "source-fourth-down-attempts/component-v1" },
                { "selectedCase", selected }, { "selectedField", selectedField },
                { "fixture", Fixture }, { "cases", Cases }, { "assertions", assertions },
                { "technical", technical },
                { "failure", failure == null ? null : new Dictionary<string, object>
                    { { "case", failure.Case }, { "field", failure.Field },
                      { "expected", failure.Expected }, { "actual", failure.Actual } } }
            };
            string output = JsonSerializer.Serialize(payload);
            if (System.Text.Encoding.UTF8.GetByteCount(output) > 65536) return 3;
            Console.WriteLine(output);
            return technical != null ? 2 : failure != null ? 1 : 0;
        }
    }

}
