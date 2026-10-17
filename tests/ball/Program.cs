using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Linq;

internal sealed class BallAssertionFailure : Exception
{
    internal BallAssertionFailure(string identity) : base(identity) { }
}

internal static class Program
{
    private static int Main(string[] args)
    {
        var result = new Dictionary<string, object>
        {
            { "schema", "actual-source-ball-checks/v1" }, { "status", "error" },
            { "full_gameplay_parity_claimed", false }, { "unity_editor_or_browser_executed", false }
        };
        try
        {
            string c2Inputs = null;
            if (args.Length >= 2 && args[args.Length - 2] == "--c2-inputs")
            {
                c2Inputs = args[args.Length - 1];
                args = args.Take(args.Length - 2).ToArray();
            }
            if (args.Length != 3 && args.Length != 4)
                throw new ArgumentException("BallChecks actual-fixture.json original-C-case-directory all|native|caller|sentinel|geometry");
            FullKernelInputs real = FullKernelInputs.Load(args[0], soundFunctionsPath: args.Length == 4 ? args[3] : null,
                c2InputsPath: c2Inputs);
            result["real_input_fixture_sha256"] = real.ManifestSha256;
            result["canonical_B1_animation_manifest"] = real.Animations.InventorySha256;
            result["production_contact272_selected"] = real.IsContactProfile;
            string section = args[2];
            if (section != "all" && section != "native" && section != "caller" && section != "sentinel" && section != "geometry" && section != "flip-fifo")
                throw new ArgumentException("Unknown bounded ball section.");
            if (section == "flip-fifo")
                result["caller_assertions"] = SourceCallerChecks.RunFlipOnly(real, args[1]);
            if (section == "all" || section == "sentinel")
                result["sentinel_assertions"] = SentinelChecks.Run(real, args[1]);
            if (section == "all" || section == "native")
                result["native_ball_assertions"] = NativeBallChecks.Run(real, args[1]);
            if (section == "all" || section == "caller")
            {
                // Historical callers use157, numeric controls117; the explicit new fixture selects production272.
                FullKernelInputs movementReal = FullKernelInputs.Load(args[0], movementLibrary: true,
                    soundFunctionsPath: args.Length == 4 ? args[3] : null, c2InputsPath: c2Inputs);
                result["caller_runtime_animation_manifest"] = movementReal.Animations.InventorySha256;
                result["caller_assertions"] = SourceCallerChecks.Run(movementReal, args[1]);
            }
            if (section == "all" || section == "geometry")
                result["geometry_caller_assertions"] = GeometryCallerChecks.Run(real, args[1]);
            result["caller_added_FIFO_assertions"] = SourceCallerChecks.AddedFifoAssertions;
            result["geometry_added_profile_assertions"] = GeometryCallerChecks.AddedProfileAssertions;
            result["status"] = "passed";
            Console.WriteLine(JsonSerializer.Serialize(result));
            return 0;
        }
        catch (BallAssertionFailure failure)
        {
            result["status"] = "failed";
            result["failed_assertion"] = failure.Message;
            Console.WriteLine(JsonSerializer.Serialize(result));
            return 1;
        }
        catch (Exception error)
        {
            result["exception_type"] = error.GetType().FullName;
            result["error"] = error.ToString();
            Console.WriteLine(JsonSerializer.Serialize(result));
            return 2;
        }
    }
}
