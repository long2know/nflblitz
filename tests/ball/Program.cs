using System;
using System.Collections.Generic;
using System.Text.Json;

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
            if (args.Length != 3)
                throw new ArgumentException("BallChecks actual-fixture.json original-C-case-directory all|native|caller|sentinel|geometry");
            FullKernelInputs real = FullKernelInputs.Load(args[0]);
            result["real_input_fixture_sha256"] = real.ManifestSha256;
            string section = args[2];
            if (section != "all" && section != "native" && section != "caller" && section != "sentinel" && section != "geometry")
                throw new ArgumentException("Unknown bounded ball section.");
            if (section == "all" || section == "sentinel")
                result["sentinel_assertions"] = SentinelChecks.Run(real, args[1]);
            if (section == "all" || section == "native")
                result["native_ball_assertions"] = NativeBallChecks.Run(real, args[1]);
            if (section == "all" || section == "caller")
                result["caller_assertions"] = SourceCallerChecks.Run(real, args[1]);
            if (section == "all" || section == "geometry")
                result["geometry_caller_assertions"] = GeometryCallerChecks.Run(real);
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
