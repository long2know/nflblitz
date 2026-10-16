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
        result["assertions"] = Checks.Count;
        Console.WriteLine(JsonSerializer.Serialize(result));
        return exit;
    }
}
