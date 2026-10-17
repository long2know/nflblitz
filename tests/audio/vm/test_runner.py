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
    def test_frozen_selector_is_an_explicit_actual_compile_dependency(self):
        props = ET.parse(ROOT / "tests/audio/vm/SourceSoundVmSources.props")
        includes = [item.attrib["Include"] for item in props.findall(".//Compile")]
        self.assertEqual(["$(BlitzSourceRoot)Assets/Scripts/Presentation/SourceAudioFunctions.cs"], includes)

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
            self.assertIn("Assets/Scripts/Presentation/SourceAudioFunctions.cs", (ROOT / path).read_text(), path)

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
                "assertions": 800734 if native else 141, "actual_C_cases": 15912 if native else 0,
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
