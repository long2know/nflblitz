"""Actual caller removal controls in memory; not a native/Unity execution claim."""
import unittest
import run


class C2CallerChecks(unittest.TestCase):
    def test_real_current_callers_are_wired(self):
        self.assertEqual(run.caller_guards(run.sources()), 12)

    def test_canonical_bootstrap_selector_native_and_import_seams_are_required(self):
        cases = (
            ("Runtime/SourceInterfaceServices.cs", "!ReferenceEquals(callbacks, originalC2Callbacks)", "false"),
            ("Runtime/SourceInterfaceServices.cs", "!ReferenceEquals(hit, originalC2HitExtension)", "false"),
            ("Runtime/SourceInterfaceServices.cs", "SourceC2Bootstrap.FromOriginalProgramBootAtSoundBanks(C2Data)", "AnotherAfterAIHelper(C2Data)"),
            ("Runtime/SourceInterfaceServices.cs", "SourceC2Composition.CreateSimulation(", "AnotherInspector.CreateSimulation("),
            ("Runtime/BlitzRuntime.cs", "SourceInterfaceServices.ResourceKeyFor(profile)", "SourceInterfaceServices.ResourceKey"),
            ("Editor/SourceTeamInterfaceImporter.cs", "services.InitializeContact281C2(", "services.InitializeContact272("),
            ("Editor/WebGlBuild.cs", "ConfigureSourceResourceProfile(profile.StableId)", "ConfigureSourceResourceProfile(\"original-teams-contact272\")"),
        )
        original = run.sources()
        for name, before, after in cases:
            key = "Assets/Scripts/" + name
            text = original[key].decode(); self.assertEqual(text.count(before), 1, name)
            changed = dict(original); changed[key] = text.replace(before, after, 1).encode()
            with self.subTest(path=name), self.assertRaisesRegex(AssertionError, "C2 CALLER:"):
                run.caller_guards(changed)


if __name__ == "__main__":
    unittest.main()
