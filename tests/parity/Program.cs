using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Linq;
using Blitz.Gameplay;

internal static class Program
{
    private static int assertions;
    private static int failures;
    private static readonly List<string> FailureDetails = new List<string>();
    private static readonly Dictionary<string, int> Compared = new Dictionary<string, int>();
    private static readonly Dictionary<string, int> OriginalOnly = new Dictionary<string, int>();

    internal static void Check(string label, bool condition)
    {
        assertions++;
        if (condition)
            return;
        failures++;
        if (FailureDetails.Count < 30)
            FailureDetails.Add(label);
    }

    internal static void Equal<T>(string label, T expected, T actual)
    {
        Check(label + " expected=" + expected + " actual=" + actual,
            EqualityComparer<T>.Default.Equals(expected, actual));
    }

    internal static uint Bits(float value)
    {
        return BitConverter.ToUInt32(BitConverter.GetBytes(value), 0);
    }

    internal static void Throws<T>(string label, Action action, params string[] messages) where T : Exception
    {
        try
        {
            action();
            Check(label + " did not throw " + typeof(T).Name, false);
        }
        catch (T exception)
        {
            bool relevant = true;
            foreach (string message in messages)
                relevant &= exception.Message.Contains(message);
            Check(label + " diagnostic=" + exception.Message, relevant);
        }
    }

    private static int Int(JsonElement data, string name) { return data.GetProperty(name).GetInt32(); }
    private static float Float(JsonElement data, string name) { return data.GetProperty(name).GetSingle(); }
    private static bool Bool(JsonElement data, string name) { return data.GetProperty(name).GetBoolean(); }

    private static SourceResultInput ResultInput(JsonElement input, float endX, int fieldEnd)
    {
        return new SourceResultInput((SourcePlayType)Int(input, "play_type"),
            (SourcePlayEndCause)Int(input, "cause"), (SourceBallType)Int(input, "ball_type"),
            (SourceGameFlags)Int(input, "flags"), fieldEnd, Int(input, "offense_side"),
            endX, Float(input, "fumble_x"), (SourcePlayResult)Int(input, "previous_result"));
    }

    private static void Classification(string id, JsonElement expected, SourceResultClassification actual)
    {
        Equal(id + ".play_result", Int(expected, "play_result"), (int)actual.Result);
        Equal(id + ".off_side", Int(expected, "off_side"), actual.OffenseSide);
        Equal(id + ".play_end_pos_bits", expected.GetProperty("play_end_pos_bits").GetUInt32(), Bits(actual.PlayEndX));
        Equal(id + ".classifier_status",
            Int(expected, "play_result") == -123 ? SourceClassifierStatus.IntentionalNoOp : SourceClassifierStatus.Classified,
            actual.Status);
    }

    private static SourceClockInput ClockInput(JsonElement input, long tick)
    {
        return new SourceClockInput(tick, Bool(input, "clock_active"), Int(input, "quarter_index"),
            Int(input, "time_adjustment"), !Bool(input, "adjustment_read_error"), Bool(input, "is_attract_mode"));
    }

    private static void ClockOutput(string id, JsonElement input, JsonElement expected,
        SourceClockStep step, long expectedNextWake, int previousPossessionDelta = 0,
        int? expectedLocal = null)
    {
        Equal(id + ".packed", Int(expected, "packed"), step.State.PackedTime);
        Equal(id + ".local_packed", expectedLocal ?? Int(expected, "packed"), step.State.LocalPackedTime);
        Equal(id + ".play_time", Int(expected, "play_time"), step.State.PlayTime);
        int owner = Int(input, "offense_side");
        for (int team = 0; team < 2; team++)
            Equal(id + ".time_of_possession[" + team + "]",
                expected.GetProperty("time_of_possession")[team].GetInt32(),
                input.GetProperty("time_of_possession")[team].GetInt32()
                    + (owner == team ? step.TimeOfPossessionDelta + previousPossessionDelta : 0));
        bool readAdjustment = false, warning = false;
        int sleep = 0;
        foreach (JsonElement call in expected.GetProperty("events").EnumerateArray())
        {
            string text = call.GetString();
            if (text == "get_adjustment:TIME_ADJ") readAdjustment = true;
            if (text.StartsWith("sound:cursor:CLOCK_LOW_SND:", StringComparison.Ordinal)) warning = true;
            if (text == "sleep:1") sleep = 1;
        }
        Equal(id + ".read_adjustment", readAdjustment, step.ReadTimeAdjustment);
        Equal(id + ".low_clock_warning", warning, step.LowClockWarning);
        Equal(id + ".sleep_ticks", sleep, step.SleepTicks);
        Equal(id + ".next_wake_tick", expectedNextWake, step.State.NextWakeTick);
    }

    private static void CompareCase(JsonElement row)
    {
        string id = row.GetProperty("id").GetString();
        string kind = row.GetProperty("kind").GetString();
        JsonElement input = row.GetProperty("input");
        JsonElement expected = row.GetProperty("expected");
        if (kind == "point" || kind == "line")
        {
            OriginalOnly[kind] = OriginalOnly.ContainsKey(kind) ? OriginalOnly[kind] + 1 : 1;
            return;
        }
        Compared[kind] = Compared.ContainsKey(kind) ? Compared[kind] + 1 : 1;
        switch (kind)
        {
            case "classify":
                Classification(id, expected, SourceMatchRules.Classify(
                    ResultInput(input, Float(input, "play_end_x"), Int(input, "field_end"))));
                break;
            case "spot":
                int side = Int(input, "offense_side");
                var before = new SourceRulesState(side, side, 2, 40, 70, (SourcePlayType)Int(input, "play_type"));
                var resolution = SourceMatchRules.Resolve(new SourcePlayEndInput(before,
                    (SourcePlayEndCause)Int(input, "cause"), (SourceBallType)Int(input, "ball_type"),
                    (SourceGameFlags)Int(input, "flags"), side, Float(input, "play_end_x"),
                    fumbleX: Float(input, "fumble_x"), previousResult: (SourcePlayResult)Int(input, "previous_result")));
                Classification(id, expected, resolution.Classification);
                Equal(id + ".fpend", Int(expected, "fpend"), resolution.FieldEnd);
                break;
            case "clock":
                int initial = Bool(input, "original_initialization")
                    ? SourceClock.QuarterTime : Int(input, "packed_time");
                var step = SourceClock.Step(SourceClock.Create(initial, Int(input, "play_time")),
                    ClockInput(input, 0));
                ClockOutput(id, input, expected, step, initial == 0 ? 0L : 1L);
                break;
            case "clock_pair":
                var first = SourceClock.Step(
                    SourceClock.Create(Int(input, "packed_time"), Int(input, "play_time")), ClockInput(input, 0));
                ClockOutput(id + ".first", input, expected.GetProperty("first"), first, 1);
                var between = SourceClock.DiscardFraction(first.State);
                Equal(id + ".published_after_discard", Int(expected, "published_after_discard"), between.PackedTime);
                Equal(id + ".suspended_local_gt", Int(expected.GetProperty("first"), "packed"), between.LocalPackedTime);
                var second = SourceClock.Step(between, ClockInput(input, 1));
                ClockOutput(id + ".second", input, expected.GetProperty("second"), second, 2,
                    first.TimeOfPossessionDelta, Bool(input, "clock_active") ? (int?)null
                        : Int(expected.GetProperty("first"), "packed"));
                Equal(id + ".completed_iterations", 2, Int(expected, "completed_iterations"));
                break;
            case "field":
                Equal(id + ".yard", Int(expected, "yard"),
                    SourceMatchRules.FieldPosition(Float(input, "value"), Int(input, "offense_side")));
                break;
            case "world":
                Equal(id + ".bits", expected.GetProperty("bits").GetUInt32(),
                    Bits(SourceMatchRules.WorldPosition(Int(input, "value"), Int(input, "offense_side"))));
                break;
            case "discard":
                var discarded = SourceClock.DiscardFraction(SourceClock.Create(Int(input, "packed_time")));
                Equal(id + ".packed", Int(expected, "packed"), discarded.PackedTime);
                Equal(id + ".local_not_discarded", Int(input, "packed_time"), discarded.LocalPackedTime);
                break;
            case "onside":
                int quarter = Int(input, "quarter_index");
                int packed = Int(input, "packed_time");
                int kicking = Int(input, "kicking_score");
                int receiving = Int(input, "receiving_score");
                int draw0 = Int(input, "raw_rand_0");
                var decision = SourceMatchFlow.CpuOnside(new SourceCpuOnsideInput(
                    quarter, packed, kicking, receiving, draw0, Int(input, "raw_rand_1")));
                Equal(id + ".resolved", true, decision.IsResolved);
                Equal(id + ".onside", Bool(expected, "onside"), decision.Value);
                Equal(id + ".original_rand_draws_consumed", expected.GetProperty("draws").GetArrayLength(), decision.DrawsConsumed);
                Equal(id + ".resolved_no_rng_request", false, decision.RandomRequest.IsRequired);
                var awaiting = SourceMatchFlow.CpuOnside(new SourceCpuOnsideInput(quarter, packed, kicking, receiving));
                Equal(id + ".first_rand_request", false, awaiting.IsResolved);
                Equal(id + ".rng_not_randrng", SourceRandomKind.CStandardRand, awaiting.RandomRequest.Kind);
                Equal(id + ".surprise_modulus", 33, awaiting.RandomRequest.BoundOrModulus);
                Equal(id + ".surprise_call_order", 0, awaiting.RandomRequest.CallIndex);
                var afterFirst = SourceMatchFlow.CpuOnside(new SourceCpuOnsideInput(quarter, packed, kicking, receiving, draw0));
                bool needsSecond = expected.GetProperty("draws").GetArrayLength() == 2;
                Equal(id + ".original_short_circuit", !needsSecond, afterFirst.IsResolved);
                Equal(id + ".first_draw_consumed_once", 1, afterFirst.DrawsConsumed);
                if (needsSecond)
                {
                    Equal(id + ".suppress_modulus", 5, afterFirst.RandomRequest.BoundOrModulus);
                    Equal(id + ".suppress_call_order", 1, afterFirst.RandomRequest.CallIndex);
                    Equal(id + ".second_rng_not_randrng", SourceRandomKind.CStandardRand, afterFirst.RandomRequest.Kind);
                }
                else
                    Equal(id + ".surprise_short_circuit_value", Bool(expected, "onside"), afterFirst.Value);
                break;
            default:
                throw new InvalidOperationException("Unrecognized original-C case kind: " + kind);
        }
    }

    private static int Main(string[] args)
    {
        string c2Inputs = null;
        if (args.Length >= 2 && args[args.Length - 2] == "--c2-inputs")
        {
            c2Inputs = args[args.Length - 1];
            args = args.Take(args.Length - 2).ToArray();
        }
        if (HostAdapterChecks.Enabled ? args.Length != 3 && args.Length != 4 : args.Length != 2)
        {
            Console.Error.WriteLine("Usage: ParityChecks original-C-cases.json synthetic-contract-traces.json [actual-fixture.json in full-kernel mode]");
            return 2;
        }
        try
        {
            int totalCases = 0;
            using (JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(args[0])))
            {
                JsonElement root = document.RootElement;
                Equal("oracle.schema", "original-c-oracle-cases/v1", root.GetProperty("schema").GetString());
                JsonElement pin = root.GetProperty("manifest").GetProperty("source");
                Equal("oracle.revision", "89f840d58d152a2a9f230cb8d0cdffbd554c8ded", pin.GetProperty("revision").GetString());
                Equal("oracle.result_blob", "eaf1201d0531a9d06d8ce0b1e9e75463062e2923",
                    pin.GetProperty("blobs").GetProperty("result.c").GetString());
                Equal("oracle.nfl_blob", "15b49474fb55e8bac280b6515fcf7bf59cdf8664",
                    pin.GetProperty("blobs").GetProperty("nfl.c").GetString());
                foreach (JsonElement row in root.GetProperty("cases").EnumerateArray())
                {
                    CompareCase(row);
                    totalCases++;
                }
                foreach (JsonProperty entry in root.GetProperty("inventory").EnumerateObject())
                {
                    int actual = Compared.ContainsKey(entry.Name) ? Compared[entry.Name] : OriginalOnly[entry.Name];
                    Equal("oracle.inventory." + entry.Name, entry.Value.GetInt32(), actual);
                }
            }
            foreach (string required in new[] { "classify", "spot", "clock", "clock_pair", "field", "world", "discard", "onside" })
                Check("nonempty production comparison group " + required, Compared.ContainsKey(required) && Compared[required] > 0);
            int beforeContracts = assertions;
            FullKernelInputs fullKernelInputs = HostAdapterChecks.Enabled ? FullKernelInputs.Load(args[2],
                soundFunctionsPath: args.Length == 4 ? args[3] : null, c2InputsPath: c2Inputs) : null;
            ContractChecks.Run(args[1], fullKernelInputs);
            int contractAssertions = assertions - beforeContracts;
            Check("nonzero contract assertions", contractAssertions >= 60);
            var summary = new
            {
                status = failures == 0 ? "passed" : "failed",
                executed_original_c_cases = totalCases,
                compared_cases = Compared,
                original_c_only_no_production_geometry_seam = OriginalOnly,
                assertions,
                contract_assertions = contractAssertions,
                full_kernel_host_adapter_exercised = HostAdapterChecks.Enabled,
                real_input_fixture_sha256 = fullKernelInputs == null ? null : fullKernelInputs.ManifestSha256,
                real_animation_library_sha256 = fullKernelInputs == null ? null : fullKernelInputs.Animations.InventorySha256,
                failures,
                failure_details = FailureDetails,
                full_gameplay_parity_claimed = false,
                browser_or_unity_executed = false
            };
            Console.WriteLine(JsonSerializer.Serialize(summary));
            return failures == 0 ? 0 : 1;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 2;
        }
    }
}
