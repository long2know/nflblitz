import hashlib
import io
import json
import tempfile
import unittest
import uuid
from contextlib import ExitStack, redirect_stderr, redirect_stdout
from pathlib import Path
from unittest.mock import patch

import canonical


class _ReplayInvocationCaptured(BaseException):
    """Stop a mocked CLI test at the actual process boundary, before any replay."""


class CompatibilityCliIngressTests(unittest.TestCase):
    @staticmethod
    def arguments(output):
        args = []
        for name in ("fixture-manifest", "sound-functions", "c2-m1-inputs", "source-root", "replay-commands"):
            args += ["--" + name, str(output.parent / ("inert-" + name))]
        return args + ["--output", str(output)]

    @staticmethod
    def option(mode, equals=False):
        return ["--source-compatibility=" + mode] if equals else ["--source-compatibility", mode]

    def assert_argument_rejection(self, options, diagnostic="duplicate argument"):
        output = canonical.ROOT / ".squad/.scratch" / ("cli-ingress-uncreated-" + uuid.uuid4().hex)
        self.assertFalse(output.exists())
        args = self.arguments(output) + options
        stderr = io.StringIO()
        with ExitStack() as stack:
            downstream = []
            for owner, name in (
                (Path, "absolute"), (Path, "exists"), (Path, "mkdir"), (Path, "read_bytes"),
                (canonical, "fixture_profile"), (canonical, "fixture_hashes"),
                (canonical, "canonical_hashes"), (canonical, "production_paths"),
                (canonical, "inject"), (canonical.subprocess, "run"), (canonical, "compare"),
                (canonical.importlib.util, "spec_from_file_location"),
            ):
                downstream.append(stack.enter_context(patch.object(
                    owner, name, side_effect=AssertionError("CLI rejection reached downstream " + name))))
            with redirect_stderr(stderr), self.assertRaises(SystemExit) as error:
                canonical.m1_main(args)
            self.assertEqual(error.exception.code, 2)
            for operation in downstream:
                operation.assert_not_called()
        message = stderr.getvalue()
        self.assertIn("--source-compatibility", message)
        self.assertIn(diagnostic, message)
        self.assertNotIn("FileNotFoundError", message)
        self.assertNotIn("M1 REGRESSION FAILED", message)
        self.assertFalse(output.exists())

    def test_conflicting_separated_modes_reject_before_any_downstream_work(self):
        for first, second in (("strict", "portable"), ("portable", "strict")):
            with self.subTest(first=first, second=second):
                self.assert_argument_rejection(self.option(first) + self.option(second))

    def test_identical_separated_modes_reject_before_any_downstream_work(self):
        for mode in ("strict", "portable"):
            with self.subTest(mode=mode):
                self.assert_argument_rejection(self.option(mode) + self.option(mode))

    def test_all_equals_form_duplicates_are_argument_errors(self):
        for first in ("strict", "portable"):
            for second in ("strict", "portable"):
                with self.subTest(first=first, second=second):
                    self.assert_argument_rejection(self.option(first, True) + self.option(second, True))

    def test_all_mixed_form_duplicates_are_argument_errors(self):
        for first in ("strict", "portable"):
            for second in ("strict", "portable"):
                for equals_first in (False, True):
                    with self.subTest(first=first, second=second, equals_first=equals_first):
                        self.assert_argument_rejection(
                            self.option(first, equals_first) + self.option(second, not equals_first))

    def test_duplicate_rejection_precedes_validation_of_either_mode_value(self):
        for first, second in (("automatic", "strict"), ("portable", "automatic"),
                              ("automatic", "automatic"), ("strict", "")):
            with self.subTest(first=first, second=second):
                self.assert_argument_rejection(self.option(first, True) + self.option(second))

    def test_recognized_argparse_abbreviations_cannot_hide_a_second_selection(self):
        for options in (
            self.option("strict") + ["--source-comp=portable"],
            ["--source-comp", "portable"] + self.option("strict", True),
        ):
            with self.subTest(options=options):
                self.assert_argument_rejection(options)

    def test_unknown_single_values_remain_early_argument_errors(self):
        for value in ("automatic", "", "StrictSource", "Portable"):
            for equals in (False, True):
                with self.subTest(value=value, equals=equals):
                    self.assert_argument_rejection(self.option(value, equals), "invalid choice")

    def test_default_and_single_modes_forward_once_with_truthful_mode_labels(self):
        cases = (([], "strict"), (self.option("strict"), "strict"),
                 (self.option("strict", True), "strict"), (self.option("portable"), "portable"),
                 (self.option("portable", True), "portable"))
        scratch = canonical.ROOT / ".squad/.scratch"
        scratch.mkdir(parents=True, exist_ok=True)
        for options, expected in cases:
            with self.subTest(options=options), tempfile.TemporaryDirectory(prefix="cli-forward-", dir=scratch) as work:
                output = Path(work) / "output"
                args = self.arguments(output) + options
                inert_replay = output.parent / "inert-replay-commands"
                actual_read_bytes = Path.read_bytes
                calls = []

                def read_bytes(path):
                    if path == inert_replay:
                        return b"CLI metadata test only; never executed as public input"
                    return actual_read_bytes(path)

                def capture(command, **kwargs):
                    calls.append(command)
                    if command[:2] == ["dotnet", "build"]:
                        return canonical.subprocess.CompletedProcess(command, 0, "0 Warning(s)\n0 Error(s)\n", "")
                    raise _ReplayInvocationCaptured()

                with ExitStack() as stack:
                    profile = stack.enter_context(patch.object(canonical, "fixture_profile", return_value="contact272"))
                    stack.enter_context(patch.object(canonical, "fixture_hashes", return_value={}))
                    stack.enter_context(patch.object(canonical, "canonical_hashes", return_value={}))
                    stack.enter_context(patch.object(canonical, "HARNESS", ()))
                    stack.enter_context(patch.object(canonical, "production_paths",
                                                    return_value=[canonical.ROOT / canonical.CACHE_SOURCE]))
                    stack.enter_context(patch.object(Path, "read_bytes", read_bytes))
                    process = stack.enter_context(patch.object(canonical.subprocess, "run", side_effect=capture))
                    physics = stack.enter_context(patch.object(canonical, "compare",
                                                              side_effect=AssertionError("no physics execution")))
                    with redirect_stdout(io.StringIO()), redirect_stderr(io.StringIO()), \
                            self.assertRaises(_ReplayInvocationCaptured):
                        canonical.m1_main(args)
                    self.assertEqual(process.call_count, 2)
                    physics.assert_not_called()
                    profile.assert_called_once()
                forwarded = calls[1]
                self.assertEqual(forwarded[0], "dotnet")
                self.assertEqual(forwarded.count("--source-compatibility"), 1)
                self.assertEqual(forwarded[forwarded.index("--source-compatibility") + 1], expected)
                result = json.loads((output / "result.json").read_text())
                self.assertEqual(result["gameplay_compatibility"], expected)
                self.assertEqual(result["portable_is_deliberate_correction_not_original_hardware"],
                                 expected == "portable")
                self.assertEqual(result["no_new_policy_seed_vector_clock_pointer_or_live_source_changes"],
                                 expected == "strict")
                self.assertFalse(result["complete_compiled_control_closure_executed"])

    def test_real_public_runner_entrypoint_reports_duplicate_argument_not_fixture_error(self):
        for options in (
            self.option("strict") + self.option("portable"),
            self.option("portable", True) + self.option("strict"),
            self.option("strict") + self.option("strict", True),
            self.option("portable", True) + self.option("portable", True),
        ):
            with self.subTest(options=options):
                output = canonical.ROOT / ".squad/.scratch" / ("cli-child-uncreated-" + uuid.uuid4().hex)
                command = [canonical.sys.executable, "-B", str(canonical.HERE / "run.py"),
                           *self.arguments(output), *options]
                result = canonical.subprocess.run(command, capture_output=True, text=True)
                self.assertEqual(result.returncode, 2)
                self.assertIn("--source-compatibility", result.stderr)
                self.assertIn("duplicate argument", result.stderr)
                self.assertNotIn("FileNotFoundError", result.stderr)
                self.assertNotIn("M1 REGRESSION FAILED", result.stderr)
                self.assertEqual(result.stdout, "")
                self.assertFalse(output.exists())


class CanonicalObservationGuards(unittest.TestCase):
    def test_historical_c1_cache_raw_inputs_do_not_replace_current_defaults(self):
        program = (canonical.HERE / "Program.cs").read_text()
        controlled = (canonical.HERE / "Controlled.cs").read_text()
        declaration = controlled[controlled.index("    private static SourceHandicapSettings HistoricalC1CacheHandicapSettings()"):
                                 controlled.index("    private static GameSimulation NormalFixture(")]
        self.assertIn("return new SourceHandicapSettings(randomDrones: true);", declaration)
        self.assertNotIn("Model.", declaration)
        factory = program[program.index("    private static GameSimulation Create("):
                          program.index("    private static PlayerCommand Raw(")]
        self.assertIn("SourceHandicapSettings handicapSettings = null", factory)
        self.assertEqual(factory.count("handicapSettings: handicapSettings"), 2)
        self.assertNotIn("new SourceHandicapSettings", factory)
        smoke = program[program.index("    private static void Smoke("):
                        program.index("    private static void RawMovementCampaign(")]
        self.assertIn("handicapSettings: cacheContractOnly ? HistoricalC1CacheHandicapSettings() : null", smoke)
        self.assertEqual(program.count("HistoricalC1CacheHandicapSettings()"), 1)
        defaults = (canonical.ROOT / "Assets/Scripts/Gameplay/GameSimulation.FireHandicap.cs").read_text()
        self.assertIn("bool randomDrones = false", defaults)

    def test_unknown_cli_compatibility_rejects_before_any_output_or_replay(self):
        with tempfile.TemporaryDirectory() as work:
            output = Path(work) / "must-not-exist"
            args = []
            for key in ("fixture-manifest", "sound-functions", "c2-m1-inputs", "source-root", "replay-commands"):
                args += ["--" + key, "not-read-invalid-option"]
            args += ["--output", str(output), "--source-compatibility", "automatic"]
            with redirect_stderr(io.StringIO()), self.assertRaises(SystemExit) as error:
                canonical.m1_main(args)
            self.assertEqual(error.exception.code, 2)
            self.assertFalse(output.exists())

    def test_portable_receipt_cannot_promote_strict_or_drop_the_pose_gate(self):
        receipt = {
            "failure": None, "completedHosts": 12000, "SourceTick": 11400,
            "profile": "original-teams-contact281-c2-m1",
            "factory": "REAL SourceC2Composition.CreateM1Simulation",
            "sourceData": "b0c02e5516f4f932625e1f8ccb3ff0fd53ae222657f899eb8c9d0b0c2e74460a",
            "originalPublicVectorRetainedWithoutStateConditionedRegeneration": True,
            "gameplayCompatibility": "Portable", "positive": None,
        }
        summary = {"status": "failed", "failed_assertion": canonical.M1_COVERAGE_ASSERTION}
        self.assertEqual("unmet-public-R_SKIP", canonical.require_m1_replay(1, summary, receipt, "portable"))
        with self.assertRaises(RuntimeError):
            canonical.require_m1_replay(1, summary, receipt, "strict")
        with self.assertRaises(RuntimeError):
            canonical.require_m1_replay(0, {"status": "passed", "public_R_SKIP_gate_qualified": True},
                                        receipt, "portable")
        with self.assertRaises(RuntimeError):
            canonical.require_m1_replay(1, summary, dict(receipt, failure={"error": "new fault"}), "portable")

    def test_full_m1_terminal_report_is_persisted_and_failures_are_not_silent(self):
        for status in ("passed-full-M1-public-caller-gate", "UNMET-public-R_SKIP",
                       "failed", "inconclusive-generation-changed",
                       "passed-scoped-entry-and-cache-only", "passed-portable-M1-public-caller-gate",
                       "failed-portable-source-fault"):
            with self.subTest(status=status), tempfile.TemporaryDirectory() as work:
                output = Path(work)
                result = {"status": status, "runs": {"unlogged-proof": "not-for-console"}}
                stdout, stderr = io.StringIO(), io.StringIO()
                with redirect_stdout(stdout), redirect_stderr(stderr):
                    canonical._write_m1_result(output, result)
                self.assertEqual(json.loads((output / "result.json").read_text()), result)
                success = status in ("passed-full-M1-public-caller-gate", "passed-portable-M1-public-caller-gate")
                visible, empty = (stdout, stderr) if success else (stderr, stdout)
                self.assertEqual(empty.getvalue(), "")
                self.assertIn(status, visible.getvalue())
                self.assertIn(str(output / "result.json"), visible.getvalue())
                self.assertNotIn("not-for-console", visible.getvalue())
                if not success:
                    self.assertIn("M1 REGRESSION FAILED:", visible.getvalue())
                if status == "UNMET-public-R_SKIP":
                    self.assertIn(canonical.M1_COVERAGE_ASSERTION, visible.getvalue())

    def test_snap_hike_root_is_explicit_and_controls_target_actual_producer_context(self):
        source = (canonical.ROOT / "Assets/Scripts/Gameplay/GameSimulation.Source.cs").read_text()
        request = source[source.index("        private void SnapSource("):
                         source.index("        private void PrepareSourceKickoff(")]
        self.assertNotIn("Model.Phase = GamePhase.LiveBall", request)
        self.assertIn("SourceGameFlags.HikingBall", request)
        self.assertIn("SourcePlayerDriver.Snap(Model, SourceDraw)", request)
        release = source[source.index("            if ((signal & SourceAnimationSignal.HikeBall) != 0)"):
                         source.index("        private void BindSourceAnimation(")]
        self.assertIn("Model.Phase = GamePhase.LiveBall", release)
        self.assertIn("Model.TargetPlayerId = target", release)
        self.assertIn("EmitSource(SourceEventType.Snapped", release)
        self.assertEqual(len(canonical.SNAP_HIKE_CONTROLS), 3)
        for identity, old, new, assertion in canonical.SNAP_HIKE_CONTROLS:
            with self.subTest(identity=identity):
                self.assertEqual(source.count(old), 1)
                self.assertNotEqual(old, new)
                self.assertGreater(len(assertion), 20)
        self.assertIn("tests/movement-callers/SnapHikeChecks.cs", canonical.HARNESS)

    def test_successor_full_gate_cannot_relabel_the_old_zero_window(self):
        receipt = {
            "failure": None, "completedHosts": 12000, "SourceTick": 11400,
            "profile": "original-teams-contact281-c2-m1",
            "factory": "REAL SourceC2Composition.CreateM1Simulation",
            "sourceData": "b0c02e5516f4f932625e1f8ccb3ff0fd53ae222657f899eb8c9d0b0c2e74460a",
            "originalPublicVectorRetainedWithoutStateConditionedRegeneration": True,
            "positive": None,
        }
        summary = {"status": "failed", "failed_assertion": canonical.M1_COVERAGE_ASSERTION}
        self.assertEqual("unmet-public-R_SKIP", canonical.require_m1_replay(1, summary, receipt))
        with self.assertRaises(RuntimeError):
            canonical.require_m1_replay(0, {"status": "passed", "public_R_SKIP_gate_qualified": True}, receipt)
        for field, value in (("factory", "direct constructor"), ("profile", "original-teams-contact281-c2"),
                             ("sourceData", "old-nine"), ("completedHosts", 11999),
                             ("originalPublicVectorRetainedWithoutStateConditionedRegeneration", False),
                             ("failure", {"source": "unrelated fault"})):
            with self.subTest(field=field), self.assertRaises(RuntimeError):
                canonical.require_m1_replay(1, summary, dict(receipt, **{field: value}))
        positive = dict(receipt, positive={"actor": 11, "sameInstance": 9, "firstOrdinal": 0, "secondOrdinal": 1})
        self.assertEqual("positive", canonical.require_m1_replay(0,
            {"status": "passed", "public_R_SKIP_gate_qualified": True}, positive))

    def test_successor_uses_actual_factory_and_retains_full_source_R_SKIP_gate(self):
        inputs = (canonical.ROOT / "tests/parity/FullKernelInputs.cs").read_text()
        self.assertIn("SourceC2Composition.CreateM1Simulation(Animations, ReferencePivots", inputs)
        self.assertIn("c2M1InputsPath", inputs)
        source = (canonical.HERE / "Canonical.cs").read_text()
        self.assertIn("PlayerCommand[] commands = ReadPublicReplay(replayPath);", source)
        self.assertIn("PlayerCommand command = commands[host];", source)
        self.assertIn(canonical.M1_COVERAGE_ASSERTION, source)
        self.assertIn("witness.Captured(actor.Id)", source)
        self.assertNotIn("SetValue(", source)
        runner = (canonical.HERE / "canonical.py").read_text()
        self.assertIn("for spec in CANONICAL_CACHE_MUTANTS:", runner)
        self.assertIn("for identity, filename, old, new, expected in controls.MUTANTS:", runner)
        self.assertIn("_write_m1_result(output, result)", runner[runner.index("def m1_main("):])
        self.assertIn('return 0 if result["status"] in ("passed-full-M1-public-caller-gate", "passed-portable-M1-public-caller-gate",', runner)
        self.assertIn('complete_compiled_control_closure_executed', runner)
        self.assertIn('if args.single_public_attempt:', runner)

    def test_exact_frozen_unique_call_site_and_byte_normalization(self):
        original = (canonical.ROOT / canonical.CACHE_SOURCE).read_bytes()
        self.assertEqual(hashlib.sha256(original).hexdigest(), canonical.CACHE_SOURCE_SHA)
        observed = canonical.inject(original)
        self.assertEqual(observed.count(b"geometry.CameraJoints("), original.count(b"geometry.CameraJoints("))
        self.assertEqual(observed.count(b"CanonicalCacheCapture.Record("), 1)
        self.assertEqual(observed.replace(canonical.OBSERVED_CALL.encode(), canonical.CALL.encode()), original)

    def test_hash_change_cannot_be_observed_as_same_generation(self):
        original = (canonical.ROOT / canonical.CACHE_SOURCE).read_bytes()
        with self.assertRaisesRegex(ValueError, "exact frozen original production hash"):
            canonical.inject(original + b"\n")

    def test_canonical_factory_and_entry_are_actual(self):
        inputs = (canonical.ROOT / "tests/parity/FullKernelInputs.cs").read_text()
        self.assertIn("SourceC2Composition.CreateSimulation(Animations, ReferencePivots", inputs)
        self.assertIn("SourceC2Bootstrap.FromOriginalProgramBootAtSoundBanks(C2Data)", inputs)
        source = (canonical.HERE / "Canonical.cs").read_text()
        self.assertNotIn(".CameraJoints(", source)
        self.assertNotIn("GetField(", source)
        self.assertIn("source-sound-smoke/raw-vector-v1", source)
        self.assertIn("host < 12000", source)
        self.assertIn("SourceActorPoseSampler.Evaluate(library, real.ReferencePivots", source)
        self.assertIn("ReferenceRows == witness.CurrentRows", source)

    def test_required_actual_production_compile_closure_is_complete(self):
        paths = {p.relative_to(canonical.ROOT).as_posix() for p in canonical.production_paths()}
        for name in (
            "Assets/Scripts/Runtime/SourceC2Composition.cs",
            "Assets/Scripts/Runtime/SourceContactComposition.cs",
            "Assets/Scripts/Presentation/SourceC2SoundFunctions.cs",
            "Assets/Scripts/Presentation/SourceAudioFunctions.cs",
            "Assets/Scripts/Gameplay/SourceCatchGeometry.cs",
            "Assets/Scripts/Gameplay/GameSimulation.FireHandicap.cs",
            "Assets/Scripts/AssetImport/SourceC2CallbackData.cs",
        ):
            self.assertIn(name, paths)
        for name in (
            "Assets/Scripts/AssetImport/SourceFireSpfxData.cs",
            "Assets/Scripts/Contracts/SourceFireSpfxContracts.cs",
            "Assets/Scripts/Gameplay/GameSimulation.FireSpfx.cs",
            "Assets/Scripts/Contracts/SourceKickMeterContracts.cs",
            "Assets/Scripts/Gameplay/SourceKickMeterProcess.cs",
        ):
            self.assertIn(name, paths)
        self.assertEqual(len(paths), 72)


if __name__ == "__main__":
    unittest.main()
