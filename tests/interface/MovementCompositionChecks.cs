using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using Blitz.AssetImport;
using Blitz.Gameplay;
using Blitz.Runtime;

internal static class MovementCompositionChecks
{
    private static int assertions;
    private static void Check(bool value, string name)
    {
        assertions++;
        if (!value) throw new Exception(name);
    }
    private static string Hash(byte[] bytes)
    {
        using (SHA256 hash = SHA256.Create())
            return BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
    }
    private static bool Same(float first, float second)
    { return BitConverter.SingleToInt32Bits(first) == BitConverter.SingleToInt32Bits(second); }

    public static int Main(string[] args)
    {
        try
        {
            SourceInterfaceMovementData data = SourceInterfaceMovementData.Read(
                File.ReadAllBytes(args[0]), "89c79007e04e9a17894c126c34dc66c199eb5a5961a4c749b1eb6b3e0e3951d6");
            var movement = new SourceMovementData(data.Constants, data.Tables);
            SourceMovementRuntimeData actual = SourceInterfaceMovementComposition.RuntimeData(movement, data);
            Check(ReferenceEquals(actual.Movement, movement), "actual composition retains the shared immutable movement instance");
            Check(actual.Defense.Count == 12 && actual.Friction.Count == 7, "actual composition retains every original movement row");
            for (int i = 0; i < 7; i++)
                Check(Same(actual.Friction[i], data.Friction[i]), "actual composition preserves original friction bits");
            for (int row = 0; row < 12; row++)
            {
                for (int i = 0; i < 6; i++)
                {
                    Check(Same(actual.Defense[row].LineSpeeds[i], data.Defense[row].Line[i]),
                        "actual composition preserves original line-speed bits");
                    Check(Same(actual.Defense[row].BackSpeeds[i], data.Defense[row].Back[i]),
                        "actual composition preserves original back-speed bits");
                }
                Check(actual.Defense[row].AccelerationBase == data.Defense[row].Base, "actual composition preserves acceleration base");
                Check(actual.Defense[row].AccelerationRange == data.Defense[row].Range, "actual composition preserves acceleration range");
                Check(actual.Defense[row].PlowProbability == data.Defense[row].Plow, "actual composition preserves plow probability");
            }
            for (int field = 0; field < 7; field++)
                for (int infinite = 0; infinite < 4; infinite++)
                    for (int fast = 0; fast < 4; fast++)
                        for (int bounds = 0; bounds < 4; bounds++)
                            foreach (bool speed in new[] { false, true })
                                foreach (bool violence in new[] { false, true })
                                {
                                    byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, object> {
                                        { "schema", "original-source-movement-bootstrap/v1" },
                                        { "field_index", field }, { "infinite_turbo_team_mask", infinite },
                                        { "speed_powerup", speed }, { "fast_turbo_team_mask", fast },
                                        { "run_out_of_bounds_team_mask", bounds }, { "violence_powerup", violence },
                                        { "source_hres", 512 }, { "source_vres", 256 }
                                    });
                                    SourceMovementSettings settings = SourceInterfaceMovementComposition.Settings(
                                        SourceInterfaceMovementBootstrap.Read(bytes, Hash(bytes)));
                                    Check(settings.FieldIndex == field, "actual settings preserve selected field");
                                    Check(settings.InfiniteTurboTeamMask == infinite, "actual settings preserve infinite-turbo team mask");
                                    Check(settings.FastTurboTeamMask == fast, "actual settings preserve fast-turbo team mask");
                                    Check(settings.RunOutOfBoundsTeamMask == bounds, "actual settings preserve out-of-bounds team mask");
                                    Check(settings.SpeedPowerup == speed, "actual settings preserve global speed");
                                    Check(settings.ViolencePowerup == violence, "actual settings preserve violence");
                                }
            Console.WriteLine(JsonSerializer.Serialize(new { schema = "source-interface-movement-composition-checks/v1",
                status = "passed", assertions = assertions, actual_movement_data = true, native_Unity_executed = false }));
            return 0;
        }
        catch (Exception error)
        {
            Console.WriteLine(JsonSerializer.Serialize(new { schema = "source-interface-movement-composition-checks/v1",
                status = "failed", assertions = assertions, failed_assertion = error.Message }));
            return 1;
        }
    }
}
