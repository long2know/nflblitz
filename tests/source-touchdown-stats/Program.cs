using System;
using System.Globalization;
using System.Reflection;
using Blitz.Contracts;
using Blitz.Gameplay;

namespace Blitz.SourceTouchdownStats.Tests
{
    internal static class Program
    {
        private static int cases;
        private static int assertions;
        private static int failures;
        private static int unexpected;

        private static int Main()
        {
            Run("T01", () => Totals("T01", ApplyNew(SourcePlayResult.Touchdown, 0, 0), 1, 0));
            Run("T02", () => Totals("T02", ApplyNew(SourcePlayResult.Touchdown, 1, 1), 0, 1));
            Run("T03", () => Totals("T03", ApplyNew(SourcePlayResult.Touchdown, 1, 0), 0, 1));
            Run("T04", () => Totals("T04", ApplyNew(SourcePlayResult.Touchdown, 0, 1), 1, 0));
            Run("T05", () => Totals("T05", ApplyNew(SourcePlayResult.Touchdown, 0, 0, 1, true), 1, 0));
            Run("T06", () => Totals("T06", ApplyNew(SourcePlayResult.Touchdown, 1, 1, 4), 0, 1));
            Run("T07", () => Totals("T07", ApplyNew(SourcePlayResult.Down, 0, 0), 0, 0));
            Run("T08", () => Totals("T08", ApplyNew(SourcePlayResult.Incomplete, 1, 1), 0, 0));
            Run("T09", () => Totals("T09", ApplyNew(SourcePlayResult.FieldGoal, 0, 0), 0, 0));
            Run("T10", () => Totals("T10", ApplyNew(SourcePlayResult.Conversion, 1, 1), 0, 0));
            Run("T11", () => Totals("T11", ApplyNew(SourcePlayResult.Safety, 0, 0), 0, 0));
            Run("T12", () => Totals("T12", ApplyNew(SourcePlayResult.NewPossession, 1, 0), 0, 0));
            Run("T13", () =>
            {
                var owner = new SimulationState();
                Apply(owner, SourcePlayResult.Touchdown, 0, 0);
                Totals("T13.After1", owner, 1, 0);
                Apply(owner, SourcePlayResult.Touchdown, 1, 1);
                Totals("T13.After2", owner, 1, 1);
                Apply(owner, SourcePlayResult.Incomplete, 1, 1);
                Totals("T13.After3", owner, 1, 1);
                Apply(owner, SourcePlayResult.Touchdown, 0, 0);
                Totals("T13.After4", owner, 2, 1);
            });
            Run("T14", () =>
            {
                SimulationState populated = ApplyNew(SourcePlayResult.Touchdown, 0, 0);
                var independent = new SimulationState();
                Totals("T14", independent, 0, 0);
                Check("T14.SourceTouchdowns.Ownership", "distinct",
                    ReferenceEquals(populated.SourceTouchdowns, independent.SourceTouchdowns) ? "shared" : "distinct");
                Number("T14.OtherState.SourceTouchdowns.Home", 1, populated.SourceTouchdowns[0]);
            });
            Run("T15", () =>
            {
                var owner = new SimulationState();
                owner.SourceTouchdowns[0] = 7;
                owner.SourceTouchdowns[1] = 9;
                Apply(owner, SourcePlayResult.Touchdown, 1, 0);
                Totals("T15", owner, 7, 10);
            });

            Run("C01", () =>
            {
                var group = new SourceTouchdownStatsView(2, 3);
                Number("C01.SourceTouchdownStatsView.HomeTouchdowns", 2, group.HomeTouchdowns);
                Number("C01.SourceTouchdownStatsView.AwayTouchdowns", 3, group.AwayTouchdowns);
                Check("C01.SourceTouchdownStatsView.HomeTouchdowns.CanWrite", "false",
                    typeof(SourceTouchdownStatsView).GetProperty("HomeTouchdowns").CanWrite ? "true" : "false");
                Check("C01.SourceTouchdownStatsView.AwayTouchdowns.CanWrite", "false",
                    typeof(SourceTouchdownStatsView).GetProperty("AwayTouchdowns").CanWrite ? "true" : "false");
                Check("C01.SourceTouchdownStatsView.IsSealed", "true",
                    typeof(SourceTouchdownStatsView).IsSealed ? "true" : "false");
            });
            Run("C02", () =>
            {
                var group = new SourceTouchdownStatsView(0, 0);
                Number("C02.SourceTouchdownStatsView.HomeTouchdowns", 0, group.HomeTouchdowns);
                Number("C02.SourceTouchdownStatsView.AwayTouchdowns", 0, group.AwayTouchdowns);
            });
            Run("C03", () => ExceptionOutcome("C03", () => new SourceTouchdownStatsView(-1, 0),
                "ArgumentOutOfRangeException", "homeTouchdowns"));
            Run("C04", () => ExceptionOutcome("C04", () => new SourceTouchdownStatsView(0, -1),
                "ArgumentOutOfRangeException", "awayTouchdowns"));
            Run("C05", () => ExceptionOutcome("C05", () => View(null),
                "ArgumentNullException", "touchdownStats"));
            Run("C06", () =>
            {
                ConstructorInfo[] constructors = typeof(SourceMatchView).GetConstructors();
                Number("C06.SourceMatchView.PublicConstructorCount", 10, constructors.Length);
                for (int ordinal = 1; ordinal <= 9; ordinal++)
                {
                    SourceMatchView legacy = LegacyView(constructors, ordinal);
                    Check("C06.Overload" + ordinal.ToString(CultureInfo.InvariantCulture) + ".TouchdownStats",
                        "null", legacy.TouchdownStats == null ? "null" : "specified");
                }
            });
            Run("C07", () =>
            {
                var group = new SourceTouchdownStatsView(2, 3);
                SourceMatchView view = View(group);
                Check("C07.SourceMatchView.TouchdownStats.Reference", "same",
                    ReferenceEquals(group, view.TouchdownStats) ? "same" : "different");
                Number("C07.SourceMatchView.TouchdownStats.HomeTouchdowns", 2, view.TouchdownStats.HomeTouchdowns);
                Number("C07.SourceMatchView.TouchdownStats.AwayTouchdowns", 3, view.TouchdownStats.AwayTouchdowns);
            });
            Run("C08", () =>
            {
                SimulationState owner = ApplyNew(SourcePlayResult.Touchdown, 0, 0);
                var captured = new SourceTouchdownStatsView(owner.SourceTouchdowns[0], owner.SourceTouchdowns[1]);
                Apply(owner, SourcePlayResult.Touchdown, 0, 0);
                Totals("C08.LaterOwner", owner, 2, 0);
                Number("C08.Captured.HomeTouchdowns", 1, captured.HomeTouchdowns);
                Number("C08.Captured.AwayTouchdowns", 0, captured.AwayTouchdowns);
            });

            Run("P01", () =>
            {
                SimulationState owner = ApplyNew(SourcePlayResult.Touchdown, 0, 0);
                Number("P01.SourcePassTouchdowns.Home", 0, owner.SourcePassTouchdowns[0]);
                Number("P01.SourcePassTouchdowns.Away", 0, owner.SourcePassTouchdowns[1]);
            });
            Run("P02", () =>
            {
                SimulationState owner = ApplyNew(SourcePlayResult.Touchdown, 0, 0, 1, true);
                Number("P02.SourcePassTouchdowns.Home", 1, owner.SourcePassTouchdowns[0]);
                Number("P02.SourcePassTouchdowns.Away", 0, owner.SourcePassTouchdowns[1]);
            });
            Run("P03", () =>
            {
                SimulationState owner = ApplyNew(SourcePlayResult.Touchdown, 0, 0, 4);
                Number("P03.SourceFourthDownConversions.Home", 1, owner.SourceFourthDownConversions[0]);
                Number("P03.SourceFourthDownConversions.Away", 0, owner.SourceFourthDownConversions[1]);
            });
            Run("P04", () =>
            {
                SimulationState owner = ApplyNew(SourcePlayResult.Touchdown, 1, 0, 4);
                Number("P04.SourceFourthDownConversions.Home", 0, owner.SourceFourthDownConversions[0]);
                Number("P04.SourceFourthDownConversions.Away", 0, owner.SourceFourthDownConversions[1]);
            });
            Run("P05", () =>
            {
                var owner = new SimulationState();
                owner.SourceFourthDownAttempts[0] = 3;
                owner.SourceFourthDownAttempts[1] = 5;
                Apply(owner, SourcePlayResult.Touchdown, 0, 0);
                Number("P05.SourceFourthDownAttempts.Home", 3, owner.SourceFourthDownAttempts[0]);
                Number("P05.SourceFourthDownAttempts.Away", 5, owner.SourceFourthDownAttempts[1]);
            });
            Console.WriteLine("SUMMARY|" + cases + "|" + assertions + "|" + failures + "|" + unexpected);
            return failures == 0 && unexpected == 0 && cases == 28 && assertions == 78 ? 0 : 1;
        }

        private static SimulationState ApplyNew(SourcePlayResult result, int side, int initialSide,
            int down = 1, bool forwardPass = false)
        {
            var owner = new SimulationState();
            Apply(owner, result, side, initialSide, down, forwardPass);
            return owner;
        }

        private static void Apply(SimulationState owner, SourcePlayResult result, int side, int initialSide,
            int down = 1, bool forwardPass = false)
        {
            var state = new SourceRulesState(side, initialSide, down, 20, 30);
            SourceGameFlags flags = forwardPass ? SourceGameFlags.ForwardPass : SourceGameFlags.None;
            var input = new SourcePlayEndInput(state, SourcePlayEndCause.CarrierTackledInBounds,
                SourceBallType.Pass, flags, initialSide, 0f);
            var classification = new SourceResultClassification(SourceClassifierStatus.Classified, result, side, 0f);
            var resolution = new SourcePlayResolution(state, classification, flags,
                30, 30, 0f, 0f, 0f, 0, 0, false, false, true);
            owner.AccumulateSourcePlayEndStats(input, resolution, side * SliceRules.PlayersPerTeam);
        }

        private static SourceMatchConfiguration Configuration()
        {
            return new SourceMatchConfiguration(SourceSimulationProfile.OriginalRelease57Hz,
                SourceStationLayout.TwoStation,
                new SourceTeamSelection(0, new[] { 0, 1, 2, 3, 4, 5, 6 }),
                new SourceTeamSelection(1, new[] { 0, 1, 2, 3, 4, 5, 6 }),
                new SourceStationConfiguration[0], TeamId.Home, SourceCaptainSlot.First);
        }

        private static SourceMatchView View(SourceTouchdownStatsView group)
        {
            return new SourceMatchView(group, new SourceFourthDownAttemptsView(0, 0),
                new SourceResultProgressionStatsView(0, 0, 0, 0),
                new SourcePlaceKickStatsView(0, 0, 0, 0),
                new SourcePlayEndStatsView(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0),
                new SourceCompletedPassStatsView(0, 0, 0, 0, 0, 0),
                new SourcePassCompletionsView(0, 0), new SourceInterceptionsView(0, 0),
                new SourceTimeOfPossessionView(0, 0), Configuration(), (SourcePlayKind)0,
                (SourceMatchPhase)0, new SourcePeriodView(0),
                new SourcePossessionView(TeamId.Home, TeamId.Home, TeamId.Home, TeamId.Home, SourceCaptainSlot.First),
                new[] { new SourceQuarterScore(0, 0, 0) }, new SourceStationView[0]);
        }

        private static SourceMatchView LegacyView(ConstructorInfo[] constructors, int ordinal)
        {
            ConstructorInfo selected = null;
            foreach (ConstructorInfo constructor in constructors)
            {
                if (constructor.GetParameters().Length != 29 + ordinal)
                    continue;
                if (selected != null)
                    throw new InvalidOperationException("Ambiguous legacy constructor ordinal.");
                selected = constructor;
            }
            if (selected == null)
                throw new InvalidOperationException("Missing legacy constructor ordinal.");
            ParameterInfo[] parameters = selected.GetParameters();
            var values = new object[parameters.Length];
            for (int i = 0; i < parameters.Length; i++)
                values[i] = FixtureArgument(parameters[i]);
            return (SourceMatchView)selected.Invoke(values);
        }

        private static object FixtureArgument(ParameterInfo parameter)
        {
            switch (parameter.Name)
            {
                case "configuration": return Configuration();
                case "playKind": return (SourcePlayKind)0;
                case "phase": return (SourceMatchPhase)0;
                case "period": return new SourcePeriodView(0);
                case "possession": return new SourcePossessionView(TeamId.Home, TeamId.Home,
                    TeamId.Home, TeamId.Home, SourceCaptainSlot.First);
                case "quarterScores": return new[] { new SourceQuarterScore(0, 0, 0) };
                case "stations": return new SourceStationView[0];
                case "timeOfPossession": return new SourceTimeOfPossessionView(0, 0);
                case "interceptions": return new SourceInterceptionsView(0, 0);
                case "passCompletions": return new SourcePassCompletionsView(0, 0);
                case "completedPassStats": return new SourceCompletedPassStatsView(0, 0, 0, 0, 0, 0);
                case "playEndStats": return new SourcePlayEndStatsView(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
                case "placeKickStats": return new SourcePlaceKickStatsView(0, 0, 0, 0);
                case "resultProgressionStats": return new SourceResultProgressionStatsView(0, 0, 0, 0);
                case "fourthDownAttempts": return new SourceFourthDownAttemptsView(0, 0);
            }
            if (!parameter.IsOptional)
                throw new InvalidOperationException("Unrecognized required fixture operand: " + parameter.Name);
            object value = parameter.DefaultValue;
            if (value != null && value != DBNull.Value && value != Type.Missing)
                return value;
            return parameter.ParameterType.IsValueType ? Activator.CreateInstance(parameter.ParameterType) : null;
        }

        private static void Totals(string name, SimulationState owner, int home, int away)
        {
            Number(name + ".SourceTouchdowns.Home", home, owner.SourceTouchdowns[0]);
            Number(name + ".SourceTouchdowns.Away", away, owner.SourceTouchdowns[1]);
        }

        private static void ExceptionOutcome(string name, Func<object> action, string type, string parameter)
        {
            Exception observed = null;
            try { action(); }
            catch (Exception error) { observed = error; }
            Check(name + ".ExceptionType", type, observed == null ? "<none>" : observed.GetType().Name);
            var argument = observed as ArgumentException;
            Check(name + ".ParamName", parameter, argument == null ? "<none>" : argument.ParamName);
        }

        private static void Number(string field, int expected, int actual)
        {
            Check(field, expected.ToString(CultureInfo.InvariantCulture), actual.ToString(CultureInfo.InvariantCulture));
        }

        private static void Check(string field, string expected, string actual)
        {
            assertions++;
            bool pass = expected == actual;
            if (!pass) failures++;
            Console.WriteLine("ASSERT|" + field + "|" + expected + "|" + actual + "|" + (pass ? "PASS" : "FAIL"));
        }

        private static void Run(string id, Action action)
        {
            cases++;
            Console.WriteLine("CASE|" + id);
            try { action(); }
            catch (Exception error)
            {
                unexpected++;
                Console.WriteLine("UNEXPECTED|" + id + "|" + error.GetType().Name);
            }
        }
    }
}
