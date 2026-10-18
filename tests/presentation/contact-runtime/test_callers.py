"""Mutate actual callers in memory; these are source gates, not Unity execution."""

import unittest
import run


class ActualCallerChecks(unittest.TestCase):
    def test_actual_current_callers_use_explicit_contact272(self):
        self.assertEqual(run.validate_callers(run.source_inventory()), 12)

    def test_missing_canonical_or_bootstrap_seams_are_rejected(self):
        mutations = (
            ("Runtime/SourceInterfaceServices.cs", "!ReferenceEquals(originalAnimations, canonical)", "false"),
            ("Runtime/SourceInterfaceServices.cs", "!ReferenceEquals(originalSoundVmFunctions, canonical)", "false"),
            ("Runtime/SourceInterfaceServices.cs", "!ReferenceEquals(prefab, teams.Template)", "false"),
            ("Runtime/SourceInterfaceServices.cs", "SourceContactComposition.CreateSimulation(", "AnotherInspector.CreateSimulation("),
            ("Runtime/BlitzRuntime.cs", "ResolveActorMode(profile)", "ResolveActorMode(true)"),
            ("Runtime/BlitzRuntime.cs", "sourceServices.CreateSimulation(prefab, compatibility)",
             "sourceServices.CreateSimulation(prefab)"),
            ("Editor/SourceTeamInterfaceImporter.cs", "services.InitializeContact272(", "services.Initialize("),
            ("Presentation/SourcePlayerProvider.cs", "profile.RequireImpact(library);", ""),
        )
        original = run.source_inventory()
        for name, before, after in mutations:
            path = "Assets/Scripts/" + name
            text = original[path].decode()
            self.assertEqual(text.count(before), 1, name)
            mutated = dict(original)
            mutated[path] = text.replace(before, after, 1).encode()
            with self.subTest(path=name), self.assertRaisesRegex(AssertionError, "CONTACT CALLER:"):
                run.validate_callers(mutated)


if __name__ == "__main__":
    unittest.main()
