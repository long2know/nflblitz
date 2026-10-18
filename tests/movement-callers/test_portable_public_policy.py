import re
import unittest
from pathlib import Path

import canonical


HERE = Path(__file__).resolve().parent


class PortablePublicPolicyGuards(unittest.TestCase):
    def test_policy_uses_only_restricted_immutable_public_projection(self):
        source = (HERE / "PortablePublicPolicy.cs").read_text()
        controller = source[source.index("    private sealed class PortablePublicController"):
                            source.index("    private static int PortablePolicyChecks")]
        self.assertIn("Choose(PortableVisible view)", controller)
        for forbidden in ("GameSimulation", "IGameState", "SourceTick", "Animation", "Random",
                          ".Model", "SourceJob", "SourceDrones", ".Step(", "FrameOrdinal", "CompatibilityRepair"):
            with self.subTest(forbidden=forbidden):
                self.assertNotIn(forbidden, controller)
        projection = source[source.index("internal static PortableVisible Project"):
                            source.index("    private readonly struct PortableIntent")]
        self.assertEqual(set(re.findall(r"state\.([A-Za-z]+)", projection)),
                         {"Phase", "Possession", "ControlledPlayerId", "Ball", "Players"})
        self.assertNotIn("p.Source", projection)
        self.assertNotIn("p.Animation", projection)
        self.assertIn("Array.AsReadOnly(players.ToArray())", source)

    def test_explicit_portable_same_provider_and_one_first_window(self):
        source = (HERE / "PortablePublicPolicy.cs").read_text()
        original = (HERE / "Canonical.cs").read_text()
        start = "        int[] rawVector ="
        end = "        Check(shared.Trace"
        actual = source[source.index(start):source.index(end)]
        expected = original[original.index(start):original.index(end)]
        actual = actual.replace(
            "SourceRandomEvidence.ExternalUnverified)),\n            compatibility: SourceGameplayCompatibility.Portable);",
            "SourceRandomEvidence.ExternalUnverified)));")
        self.assertEqual(actual, expected)
        self.assertIn("compatibility: SourceGameplayCompatibility.Portable", source)
        self.assertIn("FileMode.CreateNew", source)
        self.assertLess(source.index("only-public-window-started.json"), source.index(start))
        self.assertEqual(source.count("game.Step(command)"), 1)
        self.assertIn("for (int host = 0; host < 12000; host++)", source)
        self.assertIn("if (failure != null) break;", source)
        self.assertIn("!compatibilitySpecified || gameplayCompatibility != SourceGameplayCompatibility.Portable",
                      (HERE / "Program.cs").read_text())

    def test_inputs_stay_closed_and_strict_gate_inventory_remains(self):
        source = (HERE / "PortablePublicPolicy.cs").read_text()
        self.assertIn("commands.WriteLine(JsonSerializer.Serialize(new { host, command }, options));", source)
        self.assertIn("new SourceStationInput(0, intent.X, intent.Z, intent.Held, pressed, released, intent.Menu)", source)
        self.assertIn("ReadPublicReplay(inputPath).Length == 12000", source)
        self.assertIn("game.SourceTick == 11400", source)
        self.assertIn("game.SourceTick <= 2299", source)
        self.assertIn("layer.FrameOrdinal != earlier.Item2 && pose != earlier.Item3", source)
        self.assertIn("Enumerable.Range(0, 15)", source)
        self.assertIn(canonical.M1_COVERAGE_ASSERTION, (HERE / "Canonical.cs").read_text())
        self.assertEqual(len(canonical.CANONICAL_CACHE_MUTANTS), 3)
        self.assertIn("tests/movement-callers/PortablePublicPolicy.cs", canonical.HARNESS)
        self.assertIn('<Compile Include="PortablePublicPolicy.cs" />',
                      (HERE / "MovementCallerChecks.csproj").read_text())


if __name__ == "__main__":
    unittest.main()
