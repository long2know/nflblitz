using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Blitz.AssetImport;

internal static class MovementBootstrapChecks
{
    private static string Hash(byte[] bytes)
    {
        using (SHA256 hash = SHA256.Create())
            return BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
    }

    private static Dictionary<string, object> Configuration(int field, int infinite, bool speed,
        int fast, int outOfBounds, bool violence, int height)
    {
        return new Dictionary<string, object> {
            { "schema", "original-source-movement-bootstrap/v1" },
            { "field_index", field }, { "infinite_turbo_team_mask", infinite },
            { "speed_powerup", speed }, { "fast_turbo_team_mask", fast },
            { "run_out_of_bounds_team_mask", outOfBounds }, { "violence_powerup", violence },
            { "source_hres", 512 }, { "source_vres", height }
        };
    }

    private static void Reject(Action operation, string name, Action<bool, string> check)
    {
        bool rejected = false;
        try { operation(); }
        catch (SourceInterfaceException) { rejected = true; }
        catch (SourceRosterException) { rejected = true; }
        check(rejected, name);
    }

    private static void RejectBytes(byte[] bytes, string name, Action<bool, string> check)
    {
        Reject(() => SourceInterfaceMovementBootstrap.Read(bytes, Hash(bytes)), name, check);
    }

    public static void Run(Action<bool, string> check)
    {
        check(SourceInterfaceMovementBootstrap.Schema == "original-source-movement-bootstrap/v1",
            "bootstrap schema is the explicit versioned production contract");
        for (int field = 0; field < 7; field++)
            for (int infinite = 0; infinite < 4; infinite++)
                for (int fast = 0; fast < 4; fast++)
                    for (int outOfBounds = 0; outOfBounds < 4; outOfBounds++)
                        foreach (bool speed in new[] { false, true })
                            foreach (bool violence in new[] { false, true })
                                foreach (int height in new[] { 256, 384 })
                                {
                                    byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(Configuration(
                                        field, infinite, speed, fast, outOfBounds, violence, height));
                                    string pin = Hash(bytes);
                                    SourceInterfaceMovementBootstrap actual =
                                        SourceInterfaceMovementBootstrap.Read(bytes, pin);
                                    check(actual.FieldIndex == field, "bootstrap retains selected field index");
                                    check(actual.InfiniteTurboTeamMask == infinite, "bootstrap retains both infinite-turbo team bits");
                                    check(actual.FastTurboTeamMask == fast, "bootstrap retains both fast-turbo team bits");
                                    check(actual.RunOutOfBoundsTeamMask == outOfBounds, "bootstrap retains both out-of-bounds team bits");
                                    check(actual.SpeedPowerup == speed, "bootstrap speed powerup remains global");
                                    check(actual.ViolencePowerup == violence, "bootstrap violence powerup remains explicit");
                                    check(actual.HorizontalResolution == 512 && actual.VerticalResolution == height,
                                        "bootstrap retains exact explicit source DIP raster");
                                    check(actual.ContentSha256 == pin, "bootstrap retains independently supplied content pin");
                                    bytes[0] = 0;
                                    check(actual.ContentSha256 == pin && actual.FieldIndex == field &&
                                        actual.VerticalResolution == height, "bootstrap values are detached from mutable input bytes");
                                }

        Dictionary<string, object> original = Configuration(6, 2, true, 1, 3, false, 256);
        byte[] valid = JsonSerializer.SerializeToUtf8Bytes(original);
        foreach (string key in new List<string>(original.Keys))
        {
            var missing = new Dictionary<string, object>(original);
            missing.Remove(key);
            RejectBytes(JsonSerializer.SerializeToUtf8Bytes(missing), "bootstrap rejects missing explicit field " + key, check);
            var absent = new Dictionary<string, object>(original) { [key] = null };
            RejectBytes(JsonSerializer.SerializeToUtf8Bytes(absent), "bootstrap rejects null explicit field " + key, check);
            string duplicate = Encoding.UTF8.GetString(valid);
            duplicate = duplicate.Substring(0, duplicate.Length - 1) + "," +
                JsonSerializer.Serialize(key) + ":" + JsonSerializer.Serialize(original[key]) + "}";
            RejectBytes(Encoding.UTF8.GetBytes(duplicate), "bootstrap rejects duplicate field " + key, check);
        }
        foreach (string key in new[] { "infinite_turbo_team_mask", "fast_turbo_team_mask", "run_out_of_bounds_team_mask" })
            foreach (object value in new object[] { -1, 4, 1.5, true, "1" })
            {
                var changed = new Dictionary<string, object>(original) { [key] = value };
                RejectBytes(JsonSerializer.SerializeToUtf8Bytes(changed), "bootstrap rejects invalid team mask " + key, check);
            }
        foreach (int value in new[] { -1, 7 })
        {
            var changed = new Dictionary<string, object>(original) { ["field_index"] = value };
            RejectBytes(JsonSerializer.SerializeToUtf8Bytes(changed), "bootstrap rejects invalid field index", check);
        }
        foreach (string key in new[] { "speed_powerup", "violence_powerup" })
            foreach (object value in new object[] { 0, 1, "false" })
            {
                var changed = new Dictionary<string, object>(original) { [key] = value };
                RejectBytes(JsonSerializer.SerializeToUtf8Bytes(changed), "bootstrap rejects coerced global boolean", check);
            }
        foreach (int[] raster in new[] { new[] { 360, 740 }, new[] { 640, 384 }, new[] { 512, 300 },
            new[] { 0, 384 }, new[] { 512, 0 } })
        {
            var changed = new Dictionary<string, object>(original) { ["source_hres"] = raster[0], ["source_vres"] = raster[1] };
            RejectBytes(JsonSerializer.SerializeToUtf8Bytes(changed), "bootstrap rejects non-original DIP", check);
        }
        foreach (string extra in new[] { "Responses", "home_side", "browser_width", "defaults" })
        {
            var changed = new Dictionary<string, object>(original) { [extra] = 1 };
            RejectBytes(JsonSerializer.SerializeToUtf8Bytes(changed), "bootstrap rejects unknown or fixture-only fields", check);
        }
        var wrongSchema = new Dictionary<string, object>(original) { ["schema"] = "original-source-movement-bootstrap/v0" };
        RejectBytes(JsonSerializer.SerializeToUtf8Bytes(wrongSchema), "bootstrap rejects changed schema", check);
        RejectBytes(Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(valid) + "{}"), "bootstrap rejects trailing JSON", check);
        RejectBytes(Encoding.UTF8.GetBytes("[]"), "bootstrap rejects array root", check);
        RejectBytes(new byte[16385], "bootstrap rejects oversized settings", check);
        Reject(() => SourceInterfaceMovementBootstrap.Read(valid, new string('0', 64)),
            "bootstrap content pin is mandatory", check);
        Reject(() => SourceInterfaceMovementBootstrap.Read(null, Hash(valid)), "bootstrap rejects missing bytes", check);
        Reject(() => SourceInterfaceMovementBootstrap.Read(valid, null), "bootstrap rejects missing trusted pin", check);
        Reject(() => SourceInterfaceMovementBootstrap.Read(valid, "not-a-sha256"), "bootstrap rejects malformed trusted pin", check);
    }
}
