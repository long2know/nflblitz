#!/usr/bin/env python3
"""Asset-free guards for the actual linked pure selector and explicit input contract."""
import importlib.util
from pathlib import Path
import unittest
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[3]
SPEC = importlib.util.spec_from_file_location("sound_vm_runner", ROOT / "tests/audio/vm/run.py")
RUNNER = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(RUNNER)


class SoundVmClosure(unittest.TestCase):
    def test_post_host_selection_anchor_is_the_unique_current_binding_required_boundary(self):
        current = (ROOT / "Assets/Scripts/Gameplay/SourceSoundVm.cs").read_text()
        old, new = RUNNER.POST_HOST_SELECTION_ANCHOR, RUNNER.POST_HOST_SELECTION_DEFERRED
        self.assertEqual(current.count(old), 1)
        self.assertIn("private void ExecuteSourceSoundVm(ActorState actor, SourceCallbackCue cue)", old)
        self.assertIn("RequireSourceSoundVmBinding();\n            if (SourceC2Enabled)", old)
        self.assertIn("deferred.Add(Tuple.Create(actor,cue)); return;", new)
        self.assertEqual(new.replace(
            " if (!deliveringDeferred) { deferred.Add(Tuple.Create(actor,cue)); return; }", ""), old)
        self.assertEqual(RUNNER.MUTATION_FIELDS["post-host-selection"],
                         "current source process call offset enters selector")

    def test_frozen_selector_is_an_explicit_actual_compile_dependency(self):
        props = ET.parse(ROOT / "tests/audio/vm/SourceSoundVmSources.props")
        includes = [item.attrib["Include"] for item in props.findall(".//Compile")]
        self.assertEqual(["$(BlitzSourceRoot)Assets/Scripts/Presentation/SourceAudioFunctions.cs",
                          "$(BlitzSourceRoot)Assets/Scripts/Presentation/SourceC2SoundFunctions.cs"], includes)

    def test_every_coupled_kernel_project_imports_the_actual_selector(self):
        for project in ["Blitz.Core.Tests/Blitz.Core.Tests.csproj", "ball/BallChecks.csproj",
                        "camera/CameraChecks.csproj", "parity/ParityChecks.csproj",
                        "movement-callers/MovementCallerChecks.csproj", "audio/vm/SourceSoundVm.Checks.csproj"]:
            imports = ET.parse(ROOT / "tests" / project).findall(".//Import")
            self.assertTrue(any(item.attrib.get("Project", "").endswith("SourceSoundVmSources.props")
                                for item in imports), project)

    def test_coupled_snapshot_inventories_include_the_actual_selector(self):
        for path in ["tests/ball/run.py", "tests/parity/run.py", "tests/movement-callers/run.py",
                     "tests/ball/compile_profile.py", "tests/run-core.py"]:
            text = (ROOT / path).read_text()
            if "from kernel_sources import production_paths" in text:
                self.assertIn("production_paths()", text, path)
                names = {p.relative_to(ROOT).as_posix() for p in RUNNER.source_files()}
                self.assertIn("Assets/Scripts/Presentation/SourceAudioFunctions.cs", names)
                self.assertIn("Assets/Scripts/Presentation/SourceC2SoundFunctions.cs", names)
                self.assertIn("Assets/Scripts/Runtime/SourceC2Composition.cs", names)
            else:
                self.assertIn("Assets/Scripts/Presentation/SourceAudioFunctions.cs", text, path)

    def test_no_primary_machine_path_is_a_new_permanent_default(self):
        for path in (ROOT / "tests/audio/vm").iterdir():
            if path.suffix in (".py", ".cs", ".csproj", ".props"):
                self.assertNotIn("/data/source/" + "nflblitz", path.read_text(), str(path))

    def test_runner_requires_explicit_input_paths(self):
        path = ROOT / "tests/audio/vm/run.py"
        spec = importlib.util.spec_from_file_location("sound_vm_runner", path)
        module = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(module)
        self.assertEqual(ROOT, module.ROOT)
        text = path.read_text()
        self.assertIn('"--fixture-manifest", type=Path, required=True', text)
        self.assertIn('"--functions", type=Path, required=True', text)
        self.assertIn('"--source-root", type=Path, required=True', text)
        self.assertIn("Compile failures are NOT mutation proof", text)

    def summary(self, native=True):
        return {"schema": "source-sound-vm-checks/v1", "status": "passed",
                "assertions": 800735 if native else 142, "actual_C_cases": 15912 if native else 0,
                "actual_implemented_VM_tuples": 6, "actual_M1_library": RUNNER.MOVEMENT_ANIMATION_SHA,
                "actual_SAFN": RUNNER.DATA_SHA, "DSP_playback_SPFX_or_target_PRNG_qualified": False}

    def test_exact_full_and_short_inventory_not_status_only(self):
        for native in (False, True):
            RUNNER.require_summary(0, self.summary(native), native)
            for field in ("assertions", "actual_C_cases", "actual_implemented_VM_tuples"):
                with self.subTest(native=native, field=field), self.assertRaises(RuntimeError):
                    row = self.summary(native)
                    row[field] -= 1
                    RUNNER.require_summary(0, row, native)

    def test_wrong_actual_library_payload_claim_or_crash_cannot_pass(self):
        for code, row in ((2, self.summary()), (0, None), (0, {})):
            with self.subTest(code=code), self.assertRaises(RuntimeError):
                RUNNER.require_summary(code, row, True)
        for field, value in (("actual_M1_library", "unqualified156"),
                             ("actual_SAFN", "untrusted"), ("DSP_playback_SPFX_or_target_PRNG_qualified", True)):
            with self.subTest(field=field), self.assertRaises(RuntimeError):
                row = self.summary()
                row[field] = value
                RUNNER.require_summary(0, row, True)

    def test_new272_generation_is_explicit_with_the_same_full_sound_case_contract(self):
        for native in (False, True):
            row = self.summary(native)
            with self.subTest(native=native), self.assertRaises(RuntimeError):
                RUNNER.require_summary(0, row, native, RUNNER.CONTACT_ANIMATION_SHA)
            row["actual_M1_library"] = RUNNER.CONTACT_ANIMATION_SHA
            RUNNER.require_summary(0, row, native, RUNNER.CONTACT_ANIMATION_SHA)

    def test_all_eleven_mutants_require_the_named_field_not_just_any_failure(self):
        self.assertEqual(11, len(RUNNER.MUTATION_FIELDS))
        for name, field in RUNNER.MUTATION_FIELDS.items():
            row = {"schema": "source-sound-vm-checks/v1", "status": "failed",
                   "assertions": 1, "failed_assertion": field}
            RUNNER.require_mutant(1, row, field)
            for code, value in ((0, row), (2, row), (134, row), (1, None),
                                (1, dict(row, assertions=0)), (1, dict(row, status="error")),
                                (1, dict(row, failed_assertion="unrelated assertion"))):
                with self.subTest(name=name, code=code), self.assertRaises(RuntimeError):
                    RUNNER.require_mutant(code, value, field)


if __name__ == "__main__":
    unittest.main()
