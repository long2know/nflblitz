#!/usr/bin/env python3
"""Asset-free guards for the actual linked pure selector and explicit input contract."""
import hashlib
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
        current = (ROOT / "Assets/Scripts/Gameplay/SourceSoundVm.cs").read_bytes().decode("utf-8")
        old, new = RUNNER.POST_HOST_SELECTION_ANCHOR, RUNNER.POST_HOST_SELECTION_DEFERRED
        self.assertEqual(current.count(old), 1)
        self.assertIn("private void ExecuteSourceSoundVm(ActorState actor, SourceCallbackCue cue)", old)
        self.assertTrue(old.endswith("RequireSourceSoundVmBinding();"))
        self.assertEqual(hashlib.sha256(current.encode()).hexdigest(),
                         RUNNER.POST_HOST_SELECTION_SOURCE_SHA256)
        self.assertEqual(RUNNER.POST_HOST_SELECTION_BINDING,
                         "defensive-presnap-switch/binding-required/v1")
        self.assertIn("deferred.Add(Tuple.Create(actor,cue)); return;", new)
        self.assertEqual(new.replace(
            " if (!deliveringDeferred) { deferred.Add(Tuple.Create(actor,cue)); return; }", ""), old)
        self.assertEqual(RUNNER.MUTATION_FIELDS["post-host-selection"],
                         "current source process call offset enters selector")
        name, filename, edits = RUNNER.mutation_specs()[-1]
        self.assertEqual((name, filename), ("post-host-selection", "SourceSoundVm.cs"))
        mutated = RUNNER.mutated_source(current, name, edits)
        expected = current.replace(
            "private bool sourceSoundVmFailed;",
            "private bool sourceSoundVmFailed;\n        private bool deliveringDeferred;\n"
            "        private readonly List<Tuple<ActorState, SourceCallbackCue>> deferred = "
            "new List<Tuple<ActorState, SourceCallbackCue>>();", 1).replace(
            "var batch = new SourceSoundVmBatch(sourceSoundVmPending);",
            "deliveringDeferred = true; foreach (var item in deferred) "
            "ExecuteSourceSoundVm(item.Item1,item.Item2); deferred.Clear(); deliveringDeferred = false;\n"
            "            var batch = new SourceSoundVmBatch(sourceSoundVmPending);", 1).replace(old, new, 1)
        self.assertEqual(mutated, expected)
        reverse = mutated
        for before, after in reversed(edits):
            self.assertEqual(reverse.count(after), 1)
            reverse = reverse.replace(after, before, 1)
        self.assertEqual(reverse, current)
        start = mutated.index("private void ExecuteSourceSoundVm(ActorState actor, SourceCallbackCue cue)")
        points = [
            "RequireSourceSoundVmBinding();",
            "if (!deliveringDeferred) { deferred.Add(Tuple.Create(actor,cue)); return; }",
            "bool preSnapSwitchSound = SourceDefensivePreSnapSwitchEnabled",
            'cue.Context.ExecutingSymbol == "m_hopclp_anim"',
            "if (preSnapSwitchSound && (cue.Kind != SourcePresentationCueKind.SoundCallback",
            "if (SourceC2Enabled)",
            "foreach (SourceC2VmBinding candidate in sourceC2Bootstrap.Data.Bindings)",
            "ExecuteSourceC2Sound(c2Context, C2SoundState(actorState), actorState, execution);",
            "SourceAudioVmBinding binding = null;",
            "SourceAudioFunctionResult result = SourceAudioFunctions.Select(",
            "AdvanceSourceSoundRandom(context, result);",
        ]
        offsets = [mutated.index(point, start) for point in points]
        self.assertEqual(offsets, sorted(set(offsets)))

    def test_post_host_selection_rejects_dropped_edits_stale_source_and_native_retarget(self):
        current = (ROOT / "Assets/Scripts/Gameplay/SourceSoundVm.cs").read_bytes().decode("utf-8")
        name, _, edits = RUNNER.mutation_specs()[-1]
        native = """        private void ExecuteSourceC2NativeSound(SourceC2SoundContext context)
        {
            RequireSourceSoundVmBinding();"""
        self.assertEqual(current.count(native), 1)
        for value, changed in (
            (current + "\n", edits),
            (current, edits[:-1]),
            (current, edits[:-1] + [(native, native + " return;")]),
        ):
            with self.subTest(edits=len(changed)), self.assertRaisesRegex(
                    RuntimeError, "current source and binding-required boundary"):
                RUNNER.mutated_source(value, name, changed)

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
                     "tests/ball/compile_profile.py", "tests/run-core.py", "tests/camera/run.py",
                     "tests/audio/runtime/run.py"]:
            text = (ROOT / path).read_text()
            if "from kernel_sources import production_paths" in text:
                self.assertIn("production_paths()", text, path)
                names = {p.relative_to(ROOT).as_posix() for p in RUNNER.source_files()}
                self.assertIn("Assets/Scripts/Presentation/SourceAudioFunctions.cs", names)
                self.assertIn("Assets/Scripts/Presentation/SourceC2SoundFunctions.cs", names)
                self.assertIn("Assets/Scripts/Runtime/SourceC2Composition.cs", names)
            else:
                for name in ("SourceAudioFunctions.cs", "SourceC2SoundFunctions.cs"):
                    self.assertIn(name, text, path)

    def test_explicit_staged_projects_link_both_selectors_from_their_selected_root(self):
        for project, prefix in (
            ("audio/runtime/SourceSoundRuntime.Checks.csproj", "$(BlitzSourceRoot)"),
            ("presentation/movement-stage/StageChecks.csproj", "$(BlitzStageRoot)"),
            ("presentation/interface-packaging/PackagingChecks.csproj", "$(BlitzPackagingRoot)"),
            ("presentation/sound-runtime/SoundPackagingChecks.csproj", "$(SoundPackagingRoot)"),
        ):
            includes = {item.attrib["Include"] for item in ET.parse(ROOT / "tests" / project).findall(".//Compile")}
            for name in ("SourceAudioFunctions.cs", "SourceC2SoundFunctions.cs"):
                self.assertIn(prefix + "Assets/Scripts/Presentation/" + name, includes, project)
        runtime = (ROOT / "tests/audio/runtime/run.py").read_text()
        for name in ("Assets/Scripts/Runtime/SourceC2Composition.cs", "tests/parity/FullKernelSources.props"):
            self.assertIn(name, runtime)

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
        self.assertEqual([name for name, _, _ in RUNNER.mutation_specs()], [
            "double-consume", "wrong-stream", "wrong-process-order", "cursor-offset-reset",
            "raw-flags-as-role", "mode-as-role", "wrong-script-tuple", "unknown-is-null",
            "partial-offset-rollback", "duplicate-delivery", "post-host-selection",
        ])
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
