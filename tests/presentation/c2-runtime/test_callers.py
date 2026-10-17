"""Actual caller removal controls in memory; not a native/Unity execution claim."""
import unittest
import run


class C2CallerChecks(unittest.TestCase):
    def test_real_current_callers_are_wired(self):
        self.assertEqual(run.caller_guards(run.sources()), 12)
        self.assertEqual(run.m1_caller_guards(run.sources()), 7)

    def test_m1_generation_reader_factory_and_deferred_build_gate(self):
        cases = (
            ("Runtime/SourceInterfaceServices.cs", "if (profile.IsContact281C2M1) return C2M1ResourceKey;", ""),
            ("Runtime/SourceInterfaceServices.cs", "Profile.IsContact281C2M1 ? SourceInterfacePackagePlan.ReadC2M1Callbacks",
             "Profile.IsContact281C2M1 ? SourceInterfacePackagePlan.ReadC2Callbacks"),
            ("Runtime/SourceInterfaceServices.cs", "SourceC2Composition.CreateM1Simulation(", "SourceC2Composition.CreateSimulation("),
            ("Editor/SourceTeamInterfaceImporter.cs", "services.InitializeContact281C2M1(", "services.InitializeContact281C2("),
            ("Editor/SourceTeamInterfaceImporter.cs", "SourceTeamResourceProfile.Contact281C2M1.AnimationAssetPath", "\"another-asset.asset\""),
            ("Editor/SourceTeamInterfaceChecks.cs", "SourceInterfacePackagePlan.C2M1CallbackSha256", "SourceInterfacePackagePlan.C2CallbackSha256"),
            ("Editor/WebGlBuild.cs", "SourceTeamResourceProfile.SelectRuntimeProfile(null, profile.StableId)", "SourceTeamResourceProfile.FromStableId(profile.StableId)"),
        )
        original = run.sources()
        for name, before, after in cases:
            key = "Assets/Scripts/" + name
            text = original[key].decode()
            self.assertGreaterEqual(text.count(before), 1, name)
            changed = dict(original); changed[key] = text.replace(before, after, 1).encode()
            with self.subTest(path=name), self.assertRaisesRegex(AssertionError, "C2 CALLER:"):
                run.m1_caller_guards(changed)

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
