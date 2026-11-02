using System;
using System.Collections.Generic;
using System.Text.Json;

internal sealed class AssertionFailure : Exception
{
    internal AssertionFailure(string label) : base(label) { }
}

internal static class Checks
{
    internal static int Count { get; private set; }

    internal static void Check(string label, bool condition)
    {
        Count++;
        if (!condition)
            throw new AssertionFailure(label);
    }

    internal static void Equal<T>(T expected, T actual, string label)
    {
        Check(label + ": expected " + expected + ", actual " + actual,
            EqualityComparer<T>.Default.Equals(expected, actual));
    }

    internal static void Throws<T>(string label, Action action) where T : Exception
    {
        bool rejected = false;
        try { action(); }
        catch (T) { rejected = true; }
        Check(label, rejected);
    }
}

internal static class Program
{
    private static int Main()
    {
        var result = new Dictionary<string, object>
        {
            { "schema", "actual-source-contract-checks/v1" },
            { "status", "error" },
            { "human_onside_contract", "source-human-onside-held-gesture/v1" },
            { "full_gameplay_parity_claimed", false }
        };
        int exit = 1;
        try
        {
            int start = Checks.Count;
            SourceContractChecks.RunPromoted();
            int promoted = Checks.Count - start;
            if (promoted != 177 || SourceContractChecks.EnumMappings != 65)
                throw new InvalidOperationException("Expected the exact promoted 177 checks / 65 enum mappings.");
            result["promoted_dto_assertions"] = promoted;
            result["enum_mappings"] = SourceContractChecks.EnumMappings;
            start = Checks.Count;
            SourceContractChecks.RunAdditionalGuards();
            result["additional_dto_assertions"] = Checks.Count - start;
            start = Checks.Count;
            SourcePresentationChecks.Run();
            int presentation = Checks.Count - start;
            result["presentation_dto_assertions"] = presentation;
            if (presentation != 189)
                throw new InvalidOperationException("Expected the exact 189 presentation/flip DTO checks.");
            start = Checks.Count;
            RulesFlowChecks.Run();
            int rules = Checks.Count - start;
            if (rules != 122)
                throw new InvalidOperationException("Expected the exact promoted 122 rules/flow checks.");
            result["rules_flow_assertions"] = rules;
            if (Checks.Count != 511)
                throw new InvalidOperationException("Expected the unchanged 511-check retained prefix.");
            result["retained_assertions"] = Checks.Count;
            result["human_onside_fields_per_case"] = 14;
            result["human_onside_api_guard_assertions"] = 0;
            start = Checks.Count;
            try { RulesFlowChecks.RunHumanOnsideInputs(); }
            finally
            {
                result["human_onside_tuple_assertions"] = Checks.Count - start;
                result["human_onside_cases"] = RulesFlowChecks.HumanOnsideCases;
            }
            if (Checks.Count - start != 1232 || RulesFlowChecks.HumanOnsideCases != 88)
                throw new InvalidOperationException("Expected exactly 88 human-onside tuples / 1232 field checks.");
            start = Checks.Count;
            try { RulesFlowChecks.RunHumanOnsideApiGuard(); }
            finally { result["human_onside_api_guard_assertions"] = Checks.Count - start; }
            if (Checks.Count - start != 1 || Checks.Count != 1744)
                throw new InvalidOperationException("Expected one human-onside API guard / 1744 total checks.");
            result["status"] = "passed";
            exit = 0;
        }
        catch (AssertionFailure failure)
        {
            result["status"] = "failed";
            result["failed_assertion"] = failure.Message;
        }
        catch (Exception error)
        {
            result["exception_type"] = error.GetType().FullName;
            result["error"] = error.Message;
        }
        if (result.ContainsKey("retained_assertions"))
            result["human_onside_assertions"] = Checks.Count - (int)result["retained_assertions"];
        result["assertions"] = Checks.Count;
        Console.WriteLine(JsonSerializer.Serialize(result));
        return exit;
    }
}
