import copy
import hashlib
import io
import json
import re
import shutil
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
                faults = {}
                if status == "failed-portable-source-fault":
                    result["production_before"] = {"synthetic-input-not-for-console": "a" * 64}
                    for index, label in enumerate(("isolated-observer", "final-uninstrumented")):
                        faults[label] = {
                            "host": index + 7, "before": index + 10, "SourceTick": index + 11,
                            "error": "System.InvalidOperationException: synthetic " + label +
                                     "\n   at SyntheticSource.Step()",
                        }
                    result["runs"].update({
                        "uninstrumented": {"exit_code": 0, "summary": {"status": "passed"}},
                        **{label: {"fault_metadata": dict(fault, actual="not-for-console"),
                                   "commands": ["not-for-console"], "poses": ["not-for-console"],
                                   "allSharedRawDraws": ["not-for-console"]}
                           for label, fault in faults.items()},
                    })
                original = json.dumps(result, indent=2) + "\n"
                stdout, stderr = io.StringIO(), io.StringIO()
                with redirect_stdout(stdout), redirect_stderr(stderr):
                    canonical._write_m1_result(output, result)
                self.assertEqual((output / "result.json").read_text(), original)
                self.assertEqual(json.dumps(result, indent=2) + "\n", original)
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
                diagnostics = [json.loads(line) for line in visible.getvalue().splitlines()
                               if line.startswith("{")]
                if status != "failed-portable-source-fault":
                    self.assertEqual(diagnostics, [])
                    continue
                self.assertEqual(len(diagnostics), 2, "each actual fault label needs structured evidence")
                expected_production_hash = hashlib.sha256(json.dumps(
                    result["production_before"], sort_keys=True, separators=(",", ":")).encode()).hexdigest()
                expected_report_hash = hashlib.sha256((output / "result.json").read_bytes()).hexdigest()
                self.assertEqual({entry["run"] for entry in diagnostics}, set(faults))
                for entry in diagnostics:
                    self.assertEqual(entry["schema"], "source-M1-fault-diagnostic/v1")
                    self.assertEqual(entry["status"], "captured-fault")
                    self.assertEqual(entry["report_sha256"], expected_report_hash)
                    self.assertEqual(entry["production_manifest_sha256"], expected_production_hash)
                    for key, value in faults[entry["run"]].items():
                        self.assertEqual(entry[key], value)
                    self.assertFalse(entry["run_truncated"])
                    self.assertFalse(entry["error_truncated"])
                    self.assertEqual(entry["error_json_sha256"], hashlib.sha256(
                        json.dumps(entry["error"]).encode("ascii")).hexdigest())
                    self.assertNotIn("missing_fields", entry)
                self.assertNotIn("synthetic-input-not-for-console", stderr.getvalue())
                self.assertEqual(result["status"], "failed-portable-source-fault")

    def test_m1_fault_diagnostics_make_missing_or_malformed_evidence_explicit(self):
        hidden = "synthetic-private-value-not-for-console"
        valid = {"host": 0, "before": 0, "SourceTick": 1, "error": "synthetic fault"}
        cases = [
            ({"runs": None}, "fault_metadata"),
            ({"runs": {}}, "fault_metadata"),
            ({"runs": {"unlogged-proof": hidden}}, "fault_metadata"),
            ({"runs": {"isolated-observer": {"fault_metadata": None}}}, "fault_metadata"),
        ]
        for field, value in (("host", True), ("before", -1), ("SourceTick", "not-an-int"),
                             ("error", {"actual": hidden}), ("error", " \n"),
                             ("host", 1 << 100)):
            cases.append(({"runs": {"final-uninstrumented": {
                "fault_metadata": {**valid, field: value}, "actual": hidden}}}, field))
        cases.append(({"runs": {"isolated-observer": {"fault_metadata": valid}},
                       "production_before": {"synthetic-input": {"commands": hidden}}},
                      "production_before"))
        for changes, missing in cases:
            with self.subTest(missing=missing, changes=changes), tempfile.TemporaryDirectory() as work:
                output = Path(work)
                result = {"status": "failed-portable-source-fault",
                          "production_before": {"synthetic-input": "b" * 64}, **changes}
                original = json.dumps(result, indent=2) + "\n"
                stdout, stderr = io.StringIO(), io.StringIO()
                with redirect_stdout(stdout), redirect_stderr(stderr):
                    canonical._write_m1_result(output, result)
                diagnostics = [json.loads(line) for line in stderr.getvalue().splitlines()
                               if line.startswith("{")]
                self.assertTrue(diagnostics, "missing evidence must be explicit, not silent")
                self.assertEqual(stdout.getvalue(), "")
                self.assertNotIn(hidden, stderr.getvalue())
                self.assertIn("M1 REGRESSION FAILED: failed-portable-source-fault", stderr.getvalue())
                self.assertEqual((output / "result.json").read_text(), original)
                self.assertEqual(json.dumps(result, indent=2) + "\n", original)
                for entry in diagnostics:
                    self.assertEqual(entry["status"], "missing-evidence")
                    self.assertIn(missing, entry["missing_fields"])
                    self.assertEqual(entry["report_sha256"], hashlib.sha256(
                        (output / "result.json").read_bytes()).hexdigest())
                    if missing == "production_before":
                        self.assertIsNone(entry["production_manifest_sha256"])

    def test_m1_fault_diagnostics_are_bounded_and_bind_untruncated_evidence(self):
        hidden = "synthetic-private-value-not-for-console"
        error = ("System.InvalidOperationException: synthetic underlying cause\n" +
                 "\U0001f642\n\x00" * 10000 + "\n   at SyntheticSource.LastFrame()")
        labels = ["isolated-observer-" + str(i) + "-" + "\U0001f642" * 1000 for i in range(10)]
        result = {
            "status": "failed-portable-source-fault",
            "production_before": {"synthetic-input": "c" * 64},
            "runs": {label: {"fault_metadata": {"host": 7, "before": 10, "SourceTick": 11,
                                              "error": error, "actual": hidden},
                             "commands": hidden, "poses": hidden, "allSharedRawDraws": hidden}
                     for label in labels},
        }
        original = json.dumps(result, indent=2) + "\n"
        with tempfile.TemporaryDirectory() as work:
            output = Path(work)
            stdout, stderr = io.StringIO(), io.StringIO()
            with redirect_stdout(stdout), redirect_stderr(stderr):
                canonical._write_m1_result(output, result)
            text = stderr.getvalue()
            diagnostics = [json.loads(line) for line in text.splitlines() if line.startswith("{")]
            self.assertEqual(len(diagnostics), 9, "eight bounded faults plus explicit omission evidence")
            self.assertLess(len(text.encode()), 1024 * 1024)
            self.assertEqual(stdout.getvalue(), "")
            self.assertNotIn(hidden, text)
            self.assertNotIn("\x00", text)
            self.assertEqual((output / "result.json").read_text(), original)
            self.assertEqual(json.dumps(result, indent=2) + "\n", original)
            report_hash = hashlib.sha256((output / "result.json").read_bytes()).hexdigest()
            production_hash = hashlib.sha256(json.dumps(
                result["production_before"], sort_keys=True, separators=(",", ":")).encode()).hexdigest()
            for entry, label in zip(diagnostics[:8], labels):
                self.assertEqual(entry["status"], "captured-fault")
                self.assertTrue(entry["run_truncated"])
                self.assertTrue(entry["error_truncated"])
                self.assertLessEqual(len(entry["run"]), 128)
                self.assertLessEqual(len(entry["error"]), 8192)
                self.assertTrue(entry["run"].startswith(label[:20]))
                self.assertTrue(entry["error"].startswith(
                    "System.InvalidOperationException: synthetic underlying cause"))
                self.assertTrue(entry["error"].endswith("at SyntheticSource.LastFrame()"))
                self.assertIn("[truncated]", entry["error"])
                self.assertEqual(entry["error_json_sha256"], hashlib.sha256(
                    json.dumps(error).encode("ascii")).hexdigest())
                self.assertEqual(entry["run_json_sha256"], hashlib.sha256(
                    json.dumps(label).encode("ascii")).hexdigest())
                self.assertEqual(entry["report_sha256"], report_hash)
                self.assertEqual(entry["production_manifest_sha256"], production_hash)
            self.assertEqual(diagnostics[-1]["status"], "missing-evidence")
            self.assertEqual(diagnostics[-1]["omitted_fault_runs"], 2)
            self.assertIn("additional_fault_metadata_runs", diagnostics[-1]["missing_fields"])
            self.assertEqual(diagnostics[-1]["report_sha256"], report_hash)

    @staticmethod
    def side_attempt(work, label="isolated-observer"):
        destination = Path(work) / label
        destination.mkdir(mode=0o700)
        source = (canonical.ROOT / "Assets/Scripts/Gameplay/GameSimulation.Source.cs").read_text()
        template = json.loads(re.search(r'"\\nSOURCE_SIDE_GUARD_V1[^"]*"', source).group(0))
        error = ("System.ArgumentException: The selected original playbook does not belong to the submitting source side."
                 + template.format(0, 0, 0, 1, 1, 0) + "\n   at SyntheticSource.SelectSourcePlay()")
        def packet(kind, symbol):
            return {"StationInputs": {"Stations": [{
                "StationId": 0, "Command": {"IsSpecified": True, "Action": 1, "Play": {
                    "IsSpecified": True, "Playbook": kind, "PlaySymbol": symbol}},
                "StickX": "synthetic-private-not-for-console",
            }]}, "actual": "synthetic-private-not-for-console"}
        raw = packet(0, "play_hail_mary")
        applied = packet(1, "play_suicide_blitz")
        receipt = {
            "observed": label == "isolated-observer",
            "profile": "original-teams-contact281-c2-m1", "sourceData": canonical.C2_M1_DATA_SHA,
            "replayFileSha256": "d" * 64, "SourceTick": 11,
            "failure": {"host": 7, "before": 10, "SourceTick": 11, "error": error, "command": raw,
                        "actual": {"SourceRules": {"OffenseSide": 0},
                                   "State": {"Tick": 6, "Phase": 2, "Possession": 1},
                                   "poses": "synthetic-private-not-for-console"}},
        }
        row = {"host": 7, "beforeSourceTick": 10, "SourceTick": 11,
               "command": raw, "applied": applied, "actors": "synthetic-private-not-for-console"}
        expected = {key: receipt[key] for key in ("observed", "profile", "sourceData", "replayFileSha256")}
        return destination, receipt, row, expected

    def test_source_side_guard_operands_survive_transport_without_snapshot_inference(self):
        for label in ("uninstrumented", "isolated-observer", "final-uninstrumented"):
            with self.subTest(label=label), tempfile.TemporaryDirectory() as work:
                destination, receipt, row, expected = self.side_attempt(work, label)
                if label == "final-uninstrumented":
                    receipt["failure"]["error"] += "\n" + "   at SyntheticSource.LongStack()\n" * 1000
                receipt_bytes = json.dumps(receipt).encode()
                row_bytes = (json.dumps(row) + "\n").encode()
                (destination / "m1-public-replay.jsonl").write_bytes(row_bytes)
                evidence = canonical._m1_side_evidence(destination, label, receipt_bytes, expected)
                self.assertEqual(evidence["status"], "captured-context")
                self.assertEqual(evidence["issues"], [])
                self.assertEqual(evidence["guard"], {
                    "possession": 0, "phase": 0, "selecting_side": 0, "playbook": 1,
                    "selecting_offense": True, "requesting_team": 0})
                self.assertEqual(evidence["raw_stations"][0]["playbook"], 0)
                self.assertEqual(evidence["applied_stations"][0]["playbook"], 1)
                self.assertEqual(evidence["published"], {"tick": 6, "phase": 2, "possession": 1})
                self.assertEqual(evidence["current_rules"], {"offense_side": 0})
                self.assertEqual(evidence["receipt_sha256"], hashlib.sha256(receipt_bytes).hexdigest())
                self.assertEqual(evidence["source_row_sha256"], hashlib.sha256(row_bytes).hexdigest())
                original_receipt = copy.deepcopy(receipt)
                fault = {key: receipt["failure"][key] for key in ("host", "before", "SourceTick", "error")}
                fault["source_side"] = evidence
                result = {"status": "failed-portable-source-fault",
                          "production_before": {"synthetic-input": "a" * 64},
                          "runs": {label: {"fault_metadata": fault}}}
                original = json.dumps(result, indent=2) + "\n"
                stdout, stderr = io.StringIO(), io.StringIO()
                with redirect_stdout(stdout), redirect_stderr(stderr):
                    canonical._write_m1_result(Path(work), result)
                diagnostic = json.loads(next(line for line in stderr.getvalue().splitlines()
                                             if line.startswith("{")))
                self.assertEqual(diagnostic["source_side"], evidence)
                self.assertEqual(diagnostic["error_truncated"], label == "final-uninstrumented")
                self.assertEqual(diagnostic["report_sha256"], hashlib.sha256(
                    (Path(work) / "result.json").read_bytes()).hexdigest())
                self.assertEqual((Path(work) / "result.json").read_text(), original)
                self.assertEqual(json.dumps(result, indent=2) + "\n", original)
                self.assertEqual(receipt, original_receipt)
                self.assertEqual(stdout.getvalue(), "")
                self.assertNotIn("synthetic-private-not-for-console", stderr.getvalue())
                self.assertIn("M1 REGRESSION FAILED: failed-portable-source-fault", stderr.getvalue())
                receipt["failure"]["actual"]["SourceRules"]["OffenseSide"] = 1
                different_rules = canonical._m1_side_evidence(
                    destination, label, json.dumps(receipt).encode(), expected)
                self.assertEqual(different_rules["status"], "captured-context")
                self.assertEqual(different_rules["guard"]["possession"], 0)
                self.assertEqual(different_rules["current_rules"]["offense_side"], 1)

    def test_source_side_evidence_rejects_missing_malformed_and_conflicting_bindings(self):
        mutations = [
            ("conflicting_host", lambda r, row, e: row.update(host=8)),
            ("conflicting_tick", lambda r, row, e: row.update(beforeSourceTick=9)),
            ("conflicting_tick", lambda r, row, e: r.update(SourceTick=True)),
            ("conflicting_replay_hash", lambda r, row, e: r.update(replayFileSha256="e" * 64)),
            ("conflicting_profile", lambda r, row, e: r.update(profile="unrelated-profile")),
            ("conflicting_source_data", lambda r, row, e: r.update(sourceData="f" * 64)),
            ("conflicting_observed", lambda r, row, e: r.update(observed=False)),
            ("conflicting_raw_command", lambda r, row, e: row.update(command=None)),
            ("malformed_applied_stations", lambda r, row, e: row.update(applied={"StationInputs": 1})),
            ("missing_current_rules", lambda r, row, e: r["failure"]["actual"].pop("SourceRules")),
            ("missing_published", lambda r, row, e: r["failure"]["actual"].pop("State")),
            ("missing_guard", lambda r, row, e: r["failure"].update(error="synthetic generic fault")),
            ("malformed_guard", lambda r, row, e: r["failure"].update(
                error=r["failure"]["error"].replace("possession=0", "possession=" + "1" * 300))),
            ("conflicting_guard", lambda r, row, e: r["failure"].update(
                error=r["failure"]["error"].replace("selectingOffense=1", "selectingOffense=0"))),
            ("conflicting_guard", lambda r, row, e: r["failure"].update(
                error=r["failure"]["error"].replace("possession=0", "possession=2"))),
            ("conflicting_guard", lambda r, row, e: r["failure"].update(
                error=r["failure"]["error"].replace("phase=0", "phase=5"))),
            ("conflicting_guard_applied", lambda r, row, e: row["applied"]["StationInputs"]["Stations"][0]
             ["Command"]["Play"].update(Playbook=0)),
        ]
        for issue, mutate in mutations:
            with self.subTest(issue=issue), tempfile.TemporaryDirectory() as work:
                destination, receipt, row, expected = self.side_attempt(work)
                mutate(receipt, row, expected)
                (destination / "m1-public-replay.jsonl").write_text(json.dumps(row) + "\n")
                evidence = canonical._m1_side_evidence(
                    destination, destination.name, json.dumps(receipt).encode(), expected)
                self.assertIn(issue, evidence["issues"])
                self.assertNotEqual(evidence["status"], "captured-context")
                self.assertNotIn("synthetic-private-not-for-console", json.dumps(evidence))
        for payload, issue in ((None, "missing_source_row"), (b"[]\n", "malformed_source_row"),
                               (b"{}\n", "conflicting_host"), (b"{}", "malformed_source_row"),
                               (b"x" * (2 * 1024 * 1024 + 1) + b"\n", "oversized_source_row")):
            with self.subTest(issue=issue), tempfile.TemporaryDirectory() as work:
                destination, receipt, row, expected = self.side_attempt(work)
                if payload is not None:
                    (destination / "m1-public-replay.jsonl").write_bytes(payload)
                evidence = canonical._m1_side_evidence(
                    destination, destination.name, json.dumps(receipt).encode(), expected)
                self.assertIn(issue, evidence["issues"])
                self.assertNotEqual(evidence["status"], "captured-context")
        with tempfile.TemporaryDirectory() as work:
            destination, receipt, row, expected = self.side_attempt(work)
            with patch.object(Path, "open", side_effect=AssertionError("wrong-label artifact read")):
                evidence = canonical._m1_side_evidence(
                    destination, "final-uninstrumented", json.dumps(receipt).encode(), expected)
            self.assertIn("conflicting_label", evidence["issues"])
            with patch.object(Path, "resolve", side_effect=OSError("synthetic inaccessible attempt")):
                evidence = canonical._m1_side_evidence(
                    destination, destination.name, json.dumps(receipt).encode(), expected)
            self.assertIn("conflicting_label", evidence["issues"])
            for payload in (b"null", b"[]", b"{}", b'{"failure":null}'):
                evidence = canonical._m1_side_evidence(destination, destination.name, payload, expected)
                self.assertIn("malformed_receipt", evidence["issues"])
                self.assertEqual(evidence["status"], "missing-evidence")

    def test_source_side_diagnostic_hash_identity_and_privacy_fail_closed(self):
        with tempfile.TemporaryDirectory() as work:
            destination, receipt, row, expected = self.side_attempt(work)
            (destination / "m1-public-replay.jsonl").write_text(json.dumps(row) + "\n")
            evidence = canonical._m1_side_evidence(
                destination, destination.name, json.dumps(receipt).encode(), expected)
            fault = {key: receipt["failure"][key] for key in ("host", "before", "SourceTick", "error")}
            for key, value in (("source_row_sha256", "e" * 64), ("receipt_sha256", "e" * 64),
                               ("actual", {"poses": "synthetic-private-not-for-console"})):
                with self.subTest(key=key):
                    changed = dict(evidence, **{key: value})
                    view = canonical._m1_side_diagnostic_view(changed, destination.name, fault)
                    self.assertNotEqual(view["status"], "captured-context")
                    self.assertNotIn("synthetic-private-not-for-console", json.dumps(view))
            view = canonical._m1_side_diagnostic_view(evidence, "final-uninstrumented", fault)
            self.assertIn("conflicting_fault_identity", view["issues"])
            view = canonical._m1_side_diagnostic_view(evidence, destination.name, dict(fault, host=8))
            self.assertIn("conflicting_fault_identity", view["issues"])
            self.assertEqual(canonical._m1_side_diagnostic_view(evidence, destination.name, fault), evidence)

    def test_source_side_csharp_prefix_predicate_and_direct_operand_contract(self):
        source = (canonical.ROOT / "Assets/Scripts/Gameplay/GameSimulation.Source.cs").read_text()
        method = source[source.index("        private void SelectSourcePlay("):
                        source.index("            SourcePlayDefinition chosen =")]
        predicate = "if (original.IsSpecified && (original.Playbook == SourcePlaybookKind.Offense) != selectingOffense)"
        prefix = "The selected original playbook does not belong to the submitting source side."
        self.assertEqual(method.count(predicate), 1)
        self.assertEqual(method.count('throw new ArgumentException("' + prefix + '"'), 1)
        self.assertLess(method.index(predicate), method.index("SOURCE_SIDE_GUARD_V1"))
        self.assertLess(method.index("SOURCE_SIDE_GUARD_V1"), method.index("RequireSourcePlaybook();"))
        self.assertIn("(int)Model.Possession, (int)Model.Phase, (int)selectingSide, (int)original.Playbook", method)
        self.assertIn("selectingOffense ? 1 : 0, requestingTeam.HasValue", method)
        self.assertIn("System.Globalization.CultureInfo.InvariantCulture", method)
        with tempfile.TemporaryDirectory() as work:
            _, receipt, _, _ = self.side_attempt(work)
            import check_replay_generations as generations
            self.assertEqual(receipt["failure"]["error"].splitlines()[0], generations.SIDE_ERROR)

    def test_source_side_live_binding_retains_harness_and_one_byte_negative_guards(self):
        import check_replay_generations as generations
        files, identity = generations.u2_current_snapshot()
        self.assertEqual(identity["source_count"], 78)
        self.assertEqual(identity["harness_count"], 12)
        self.assertEqual(identity["source_manifest_sha256"], generations.U2_CURRENT_SOURCE_SHA256)
        self.assertEqual(identity["harness_manifest_sha256"],
                         "e3816f617b1dbd12251d75ead06d5d455e429d34887a5ee7a1a7b4045ad1af97")
        for name in ("Assets/Scripts/Gameplay/GameSimulation.Source.cs", "tests/movement-callers/Canonical.cs"):
            altered = dict(files)
            altered[name] += b"\n"
            with self.assertRaisesRegex(ValueError, "payload"):
                generations.validate_snapshot(altered, 78, generations.U2_CURRENT_SOURCE_SHA256,
                                              generations.U2_HARNESS_SHA256)

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
        self.assertEqual(hashlib.sha256(original).hexdigest(), canonical.U1_CACHE_SOURCE_SHA)
        with self.assertRaisesRegex(ValueError, "exact frozen original production hash"):
            canonical.inject(original)
        observed = canonical.inject_current(original)
        self.assertEqual(observed.count(b"geometry.CameraJoints("), original.count(b"geometry.CameraJoints("))
        self.assertEqual(observed.count(b"CanonicalCacheCapture.Record("), 1)
        self.assertEqual(observed.replace(canonical.OBSERVED_CALL.encode(), canonical.CALL.encode()), original)

    def test_hash_change_cannot_be_observed_as_same_generation(self):
        original = (canonical.ROOT / canonical.CACHE_SOURCE).read_bytes()
        with self.assertRaisesRegex(ValueError, "explicitly frozen historical or U1 generation"):
            canonical.inject_current(original + b"\n")

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
            "Assets/Scripts/AssetImport/SourceTeamPoppedHeadData.cs",
            "Assets/Scripts/AssetImport/SourceFreeBallData.cs",
            "Assets/Scripts/AssetImport/SourceNormalCameraData.cs",
            "Assets/Scripts/Contracts/SourceCameraContracts.cs",
        ):
            self.assertIn(name, paths)
        self.assertEqual(len(paths), 78)
        self.assertIn("Assets/Scripts/Gameplay/GameSimulation.Possession.cs", paths)
        self.assertIn("Assets/Scripts/Gameplay/SourcePlaySelectionTimer.cs", paths)


class CurrentTrajectoryCaptureTests(unittest.TestCase):
    private = "synthetic-private-pose-world-rng-never-public"

    def setUp(self):
        self.work = canonical.HERE / ".artifacts" / ("current-trajectory-" + uuid.uuid4().hex)
        self.work.mkdir(parents=True)

    def tearDown(self):
        shutil.rmtree(self.work)

    @staticmethod
    def packet(kind=0, symbol="play_hail_mary", generated=False):
        play = {"IsSpecified": False, "PlaySymbol": None, "Playbook": 0,
                "SlotIndex": -1, "PageIndex": -1, "Pool": None, "PoolIndex": -1}
        source = {"IsSpecified": False, "Action": 0, "Play": play, "TargetPlayerId": -1,
                  "PatChoice": 0, "KickoffChoice": None, "AudibleSlot": -1, "OffenseFlipped": None}
        menu = dict(copy.deepcopy(source), IsSpecified=True, Action=1,
                    Play=dict(play, IsSpecified=True, Playbook=kind, PlaySymbol=symbol))
        station = {"StationId": 0, "StickX": 2, "StickZ": -1, "Held": 1,
                   "Pressed": 1, "Released": 0, "Command": menu}
        if generated:
            station["IsSpecified"] = True
        return {"Move": {"X": 0.25, "Z": -0.5}, "Turbo": False, "Action": 0, "Play": 0,
                "TargetPlayerId": -1, "Source": source, "StationInputs": {"Stations": [station]}}

    def attempt(self, profile="original-teams-contact281-c2-m1"):
        output = self.work / uuid.uuid4().hex
        destination = output / "uninstrumented"
        destination.mkdir(parents=True)
        source_data, factory, _ = canonical._M1_TRAJECTORY_PROFILES[profile]
        inputs, sources = [], []
        for host in range(2):
            pre = ({"Tick": 9, "Phase": 2, "Possession": 1} if host == 0
                   else copy.deepcopy(sources[-1]["postAttemptPublished"]))
            pre_model = ({"Phase": 0, "Possession": 0} if host == 0
                         else copy.deepcopy(sources[-1]["postAttemptModel"]))
            pre_rules = ({"OffenseSide": 1} if host == 0
                         else copy.deepcopy(sources[-1]["postAttemptRules"]))
            source = {key: None for key in canonical._M1_TRAJECTORY_SOURCE_FIELDS}
            source.update(
                host=host, beforeSourceTick=5 + host, SourceTick=6 + host,
                prePublished=pre, preModel=pre_model, preRules=pre_rules,
                postAttemptPublished=({"Tick": 10, "Phase": 0, "Possession": 1} if host == 0 else dict(pre)),
                postAttemptModel={"Phase": 1 if host == 0 else 0, "Possession": 0},
                postAttemptRules={"OffenseSide": 0}, stepSucceededOrFaulted="succeeded" if host == 0 else "faulted",
                command=self.packet(generated=True),
                applied=self.packet(1, "play_suicide_blitz", True), Phase=0,
                actors=self.private, sounds=self.private, Ball=self.private, Match=self.private,
                SourceEvents=[{"Tick": 10, "SourceTick": 6 + host, "Type": 17,
                               "Team": 1 if host == 0 else None, "StationId": -1,
                               "Result": self.private, "Position": self.private}],
            )
            inputs.append({"host": host, "command": self.packet()})
            sources.append(source)
        error = ("System.ArgumentException: The selected original playbook does not belong to the submitting source side."
                 "\nSOURCE_SIDE_GUARD_V1 possession=0 phase=0 selectingSide=0 playbook=1 selectingOffense=1 requestingTeam=null"
                 "\n   at SyntheticSource.SelectSourcePlay()")
        receipt = {
            "observed": False, "historical": False, "profile": profile, "sourceData": source_data,
            "factory": factory, "gameplayCompatibility": "Portable", "completedHosts": 2, "SourceTick": 7,
            "failure": {"host": 1, "before": 6, "SourceTick": 7, "error": error,
                        "command": sources[-1]["command"],
                        "actual": {"State": sources[-1]["postAttemptPublished"],
                                   "SourceRules": sources[-1]["postAttemptRules"], "poses": self.private}},
        }
        case = {"output": output, "destination": destination, "input": output / "input.jsonl",
                "inputs": inputs, "sources": sources, "receipt": receipt}
        self.write_attempt(case)
        return case

    @staticmethod
    def write_attempt(case):
        case["input"].write_bytes(canonical.public_replay.encode(case["inputs"]))
        case["receipt"]["replayFileSha256"] = canonical.sha(case["input"].read_bytes())
        (case["destination"] / "m1-public-replay.jsonl").write_bytes(
            b"".join(canonical._m1_trajectory_json(row) + b"\n" for row in case["sources"]))
        (case["destination"] / "m1-replay-result.json").write_bytes(canonical._m1_trajectory_json(case["receipt"]))

    @staticmethod
    def synthetic_binding(capture, label, expected, production, harness):
        canonical._m1_trajectory_require(label == "uninstrumented" and expected["observed"] is False,
                                         "conflicting_label")
        capture["attempt"].update(
            workflowRunId="12345", jobId="synthetic-metadata-only", headCommit="1" * 40, headTree="2" * 40,
            productionInventoryCountAndSha256={"count": 78, "sha256": "3" * 64},
            harnessInventoryCountAndSha256={"count": 12, "sha256": "4" * 64},
            controllerSha256="5" * 64, recipeSha256="6" * 64, compressedInputSha256="7" * 64)

    def capture(self, case, label="uninstrumented", receipt_bytes=None):
        receipt = case["receipt"]
        expected = {key: receipt[key] for key in ("observed", "profile", "sourceData", "factory", "replayFileSha256")}
        raw = ((case["destination"] / "m1-replay-result.json").read_bytes()
               if receipt_bytes is None else receipt_bytes)
        with patch.object(canonical, "_m1_trajectory_binding", side_effect=self.synthetic_binding):
            return canonical._capture_m1_trajectory(case["destination"], label, case["input"], raw, expected, {}, {})

    def publish(self, case, capture):
        receipt = case["receipt"]
        receipt_bytes = (case["destination"] / "m1-replay-result.json").read_bytes()
        fault = {key: receipt["failure"][key] for key in ("host", "before", "SourceTick", "error")}
        expected = {key: receipt[key] for key in ("observed", "profile", "sourceData", "replayFileSha256")}
        fault["source_side"] = canonical._m1_side_evidence(case["destination"], "uninstrumented", receipt_bytes, expected)
        result = {"status": "failed-portable-source-fault", "production_before": {"synthetic": "a" * 64},
                  "runs": {"uninstrumented": {"trajectory_capture": capture, "fault_metadata": fault,
                                               "replay_receipt_sha256": canonical.sha(receipt_bytes)}}}
        original = json.dumps(result, indent=2) + "\n"
        stdout, stderr = io.StringIO(), io.StringIO()
        with redirect_stdout(stdout), redirect_stderr(stderr):
            canonical._write_m1_result(case["output"], result)
        self.assertEqual((case["output"] / "result.json").read_text(), original)
        self.assertEqual(json.dumps(result, indent=2) + "\n", original)
        safe = case["output"] / canonical._M1_TRAJECTORY_DIRECTORY
        envelope = json.loads((safe / "attempt.json").read_bytes())
        rows = [json.loads(line) for line in (safe / "trajectory.jsonl").read_bytes().splitlines()]
        self.assertFalse(self.private in stderr.getvalue(), "private fault data reached console")
        self.assertEqual(stdout.getvalue(), "")
        self.assertIn("M1 REGRESSION FAILED: failed-portable-source-fault", stderr.getvalue())
        self.assertIn("SOURCE_CURRENT_TRAJECTORY_V1 ", stderr.getvalue())
        case["log"] = stderr.getvalue()
        return safe, envelope, rows

    def test_literal_h0_full_prefix_terminal_seal_and_privacy_survive_actual_writer(self):
        for profile in canonical._M1_TRAJECTORY_PROFILES:
            with self.subTest(profile=profile):
                case = self.attempt(profile)
                capture = self.capture(case)
                self.assertEqual(capture["status"], "complete")
                self.assertEqual(capture["issues"], [])
                safe, envelope, rows = self.publish(case, capture)
                self.assertEqual(envelope["status"], "complete")
                self.assertEqual([row["host"] for row in rows], [0, 1])
                self.assertEqual(rows[0]["prePublished"], {"Tick": 9, "Phase": 2, "Possession": 1})
                self.assertEqual(rows[0]["preModel"], {"Phase": 0, "Possession": 0})
                self.assertEqual(rows[0]["preRules"], {"OffenseSide": 1})
                self.assertEqual(rows[1]["prePublished"], rows[0]["postAttemptPublished"])
                self.assertEqual(rows[1]["stepSucceededOrFaulted"], "faulted")
                self.assertEqual(rows[1]["rawStationMenuIds"][0]["playbook"], 0)
                self.assertEqual(rows[1]["appliedStationMenuIds"][0]["playbook"], 1)
                self.assertIsNone(rows[1]["postAttemptSourceEventIds"][0]["Team"])
                for host, row in enumerate(rows):
                    self.assertEqual(row["inputOneBasedLine"], host + 1)
                    self.assertEqual(row["generatedRowOneBasedLine"], host + 1)
                    self.assertEqual(row["inputRowSha256"], canonical.sha(
                        case["input"].read_bytes().splitlines(keepends=True)[host]))
                    self.assertEqual(row["generatedRowSha256"], canonical.sha(
                        (case["destination"] / "m1-public-replay.jsonl").read_bytes().splitlines(keepends=True)[host]))
                    self.assertNotEqual(row["inputRowSha256"], row["generatedRowSha256"])
                attempt = envelope["attempt"]
                self.assertEqual(attempt["actualReceiptProfile"], profile)
                self.assertEqual(attempt["actualReceiptSha256"], canonical.sha(
                    (case["destination"] / "m1-replay-result.json").read_bytes()))
                self.assertEqual(attempt["generatedReplayJsonlSha256"], canonical.sha(
                    (case["destination"] / "m1-public-replay.jsonl").read_bytes()))
                self.assertEqual(attempt["terminalReportFileSha256"], canonical.sha(
                    (case["output"] / "result.json").read_bytes()))
                self.assertEqual(envelope["trajectory"]["sha256"], canonical.sha((safe / "trajectory.jsonl").read_bytes()))
                self.assertEqual(envelope["sealSha256"], canonical.sha(canonical._m1_trajectory_json(
                    {key: value for key, value in envelope.items() if key != "sealSha256"})))
                guard = envelope["terminal"]["sourceSideGuard"]
                self.assertEqual(guard["status"], "captured-context")
                self.assertEqual(guard["guard"]["possession"], 0)
                self.assertEqual(rows[-1]["postAttemptPublished"]["Possession"], 1)
                self.assertIsNone(guard["guard"]["requesting_team"])
                self.assertEqual({path.name for path in safe.iterdir()}, {"attempt.json", "trajectory.jsonl"})
                for path in safe.iterdir():
                    text = path.read_text()
                    self.assertFalse(self.private in text, "private source fields reached safe artifact")
                    for forbidden in ('"command"', '"Move"', '"StickX"', '"actors"', '"rawCursor"', '"poses"'):
                        self.assertFalse(forbidden in text, "unsafe field reached safe artifact: " + forbidden)
                    self.assertTrue(all(len(line) + 1 <= 16384 for line in path.read_bytes().splitlines()))
                self.assertLessEqual(sum(path.stat().st_size for path in safe.iterdir()), 16777216)

    def test_null_station_frame_is_not_silently_changed_to_empty_or_unspecified(self):
        case = self.attempt()
        case["inputs"][0]["command"]["StationInputs"] = None
        case["sources"][0]["command"]["StationInputs"] = None
        case["sources"][0]["applied"]["StationInputs"] = {"Stations": []}
        self.write_attempt(case)
        capture = self.capture(case)
        self.assertEqual(capture["status"], "complete")
        _, _, rows = self.publish(case, capture)
        self.assertIsNone(rows[0]["rawStationMenuIds"])
        self.assertEqual(rows[0]["appliedStationMenuIds"], [])

    def test_capture_rejects_gaps_missing_context_conflicts_enums_and_overflow(self):
        def oversized_symbol(case):
            for command in (case["inputs"][0]["command"], case["sources"][0]["command"]):
                command["StationInputs"]["Stations"][0]["Command"]["Play"]["PlaySymbol"] = "x" * 129
        cases = [
            ("malformed_projection", lambda c: c["sources"][0].pop("prePublished"), 0),
            ("malformed_projection", lambda c: c["sources"][0].update(preModel=None), 0),
            ("malformed_projection", lambda c: c["sources"][0]["preModel"].update(Phase=True), 0),
            ("malformed_projection", lambda c: c["sources"][0].update(unexpected=self.private), 0),
            ("conflicting_host", lambda c: c["sources"][1].update(host=0), 1),
            ("conflicting_host", lambda c: c["sources"][1].update(host=2), 1),
            ("conflicting_tick", lambda c: c["sources"][1].update(beforeSourceTick=5), 1),
            ("conflicting_continuity", lambda c: c["sources"][1]["prePublished"].update(Phase=4), 1),
            ("conflicting_packet", lambda c: c["sources"][0]["command"].update(Turbo=True), 0),
            ("malformed_projection", oversized_symbol, 0),
            ("capture_bound", lambda c: c["sources"][0].update(SourceEvents=c["sources"][0]["SourceEvents"] * 129), 0),
        ]
        for issue, mutate, rows in cases:
            with self.subTest(issue=issue):
                case = self.attempt()
                mutate(case)
                self.write_attempt(case)
                capture = self.capture(case)
                self.assertEqual(capture["status"], "incomplete")
                self.assertIn(issue, capture["issues"])
                self.assertEqual(capture["rows"], rows)
                _, envelope, retained = self.publish(case, capture)
                self.assertEqual(envelope["status"], "incomplete")
                self.assertEqual(len(retained), rows)
                self.assertEqual(envelope["terminal"]["resultStatus"], "failed-portable-source-fault")
        case = self.attempt()
        with patch.object(canonical, "_M1_TRAJECTORY_MAX_BYTES", 16384):
            capture = self.capture(case)
        self.assertIn("capture_bound", capture["issues"])
        self.assertEqual(capture["rows"], 0)

    def test_capture_rejects_wrong_labels_receipts_duplicate_json_and_partial_lines(self):
        for field, value in (("observed", True), ("historical", True), ("factory", "incompatible"),
                             ("profile", "unrelated"), ("sourceData", "f" * 64), ("completedHosts", 1)):
            case = self.attempt()
            receipt = dict(case["receipt"], **{field: value})
            capture = self.capture(case, receipt_bytes=canonical._m1_trajectory_json(receipt))
            self.assertEqual(capture["status"], "incomplete", field)
        case = self.attempt()
        self.assertIn("conflicting_label", self.capture(case, "isolated-observer")["issues"])
        self.assertIn("missing_receipt", self.capture(case, receipt_bytes=b"")["issues"])
        for mutate in (
                lambda raw: raw[:-1],
                lambda raw: b'{"host":0,"host":1}\n',
                lambda raw: b"x" * (2 * 1024 * 1024 + 1) + b"\n"):
            case = self.attempt()
            path = case["destination"] / "m1-public-replay.jsonl"
            path.write_bytes(mutate(path.read_bytes()))
            capture = self.capture(case)
            self.assertEqual(capture["status"], "incomplete")
            self.assertTrue(capture["issues"])

    def test_changed_emitted_projection_cannot_upload_extra_fields_or_claim_completeness(self):
        case = self.attempt()
        capture = self.capture(case)
        self.assertEqual(capture["status"], "complete")
        candidate = case["output"] / "work/current-trajectory.jsonl"
        rows = [json.loads(line) for line in candidate.read_bytes().splitlines()]
        rows[0]["actors"] = self.private
        candidate.write_bytes(b"".join(canonical._m1_trajectory_json(row) + b"\n" for row in rows))
        safe, envelope, retained = self.publish(case, capture)
        self.assertEqual(envelope["status"], "incomplete")
        self.assertIn("malformed_projection", envelope["issues"])
        self.assertIn("conflicting_projection_hash", envelope["issues"])
        self.assertEqual(retained, [])
        for path in safe.iterdir():
            self.assertFalse(self.private in path.read_text(), "non-whitelisted actor payload reached public artifact")

    def test_changed_projection_hash_and_metadata_cannot_be_laundered_by_terminal_writer(self):
        for mutate in (
                lambda capture: capture.update(sha256="f" * 64),
                lambda capture: capture["attempt"].update(actualReceiptSha256="f" * 64),
                lambda capture: capture["attempt"].update(label="isolated-observer"),
                lambda capture: capture["attempt"].update(poses=self.private),
                lambda capture: capture["terminal"].update(host=0)):
            case = self.attempt()
            capture = self.capture(case)
            mutate(capture)
            safe, envelope, _ = self.publish(case, capture)
            self.assertEqual(envelope["status"], "incomplete")
            self.assertTrue(envelope["issues"])
            for path in safe.iterdir():
                self.assertFalse(self.private in path.read_text(), "malformed metadata reached public artifact")

    def test_first_difference_uses_literal_pre_visible_not_previous_post_as_h0_or_model(self):
        case = self.attempt()
        self.capture(case)
        rows = [json.loads(line) for line in (case["output"] / "work/current-trajectory.jsonl").read_bytes().splitlines()]
        originals = [{"host": row["host"], "beforeSourceTick": row["beforeSourceTick"],
                      "SourceTick": row["afterSourceTick"],
                      "visible": {field: row["prePublished"][field] for field in ("Phase", "Possession")},
                      "sourceEventIds": copy.deepcopy(row["postAttemptSourceEventIds"])} for row in rows]
        self.assertIsNone(canonical.first_m1_trajectory_difference(rows, originals))
        originals[1]["visible"]["Possession"] = 0
        difference = canonical.first_m1_trajectory_difference(rows, originals)
        self.assertEqual((difference["host"], difference["field"], difference["current"], difference["original"]),
                         (1, "prePublished.Possession", 1, 0))
        self.assertEqual(difference["predecessor"]["host"], 0)
        self.assertEqual(difference["predecessor"]["currentSourceEventIds"], rows[0]["postAttemptSourceEventIds"])
        self.assertEqual(difference["currentPostAttemptSourceEventIds"], rows[1]["postAttemptSourceEventIds"])
        self.assertIn("Core-remains-provisional", difference["originalAuthentication"])
        originals[0]["visible"]["Phase"] = 0
        difference = canonical.first_m1_trajectory_difference(rows, originals)
        self.assertEqual((difference["host"], difference["field"], difference["current"]), (0, "prePublished.Phase", 2))
        self.assertIsNone(difference["predecessor"])
        originals[0]["visible"]["Tick"] = 0
        with self.assertRaisesRegex(ValueError, "malformed_projection"):
            canonical.first_m1_trajectory_difference(rows, originals)
        del originals[0]["visible"]["Tick"]
        with self.assertRaisesRegex(ValueError, "conflicting_host"):
            canonical.first_m1_trajectory_difference(rows, originals[:1])

    def test_real_binding_uses_current_inventory_and_immutable_recipe_not_authoring_pin_refresh(self):
        for profile, (source, factory, case) in canonical._M1_TRAJECTORY_PROFILES.items():
            recipe = json.loads((canonical.HERE / "fixtures" / (case + ".recipe.json")).read_bytes())
            production = {"synthetic-" + str(index): "a" * 64 for index in range(78)}
            harness = {name: "b" * 64 for name in canonical.HARNESS}
            harness["tests/movement-callers/PortablePublicPolicy.cs"] = recipe["controller_sha256"]
            expected = {"observed": False, "profile": profile, "sourceData": source,
                        "factory": factory, "replayFileSha256": recipe["jsonl"]["sha256"]}
            capture = canonical._m1_new_trajectory_capture()
            head = canonical.subprocess.CompletedProcess([], 0, "1" * 40 + "\n" + "2" * 40 + "\n", "")
            with patch.dict(canonical.os.environ, {"GITHUB_RUN_ID": "42", "GITHUB_JOB": "core-and-tooling"}), \
                    patch.object(canonical.subprocess, "run", return_value=head):
                canonical._m1_trajectory_binding(capture, "uninstrumented", expected, production, harness)
            self.assertEqual(capture["issues"], [])
            attempt = capture["attempt"]
            self.assertEqual(attempt["productionInventoryCountAndSha256"], {
                "count": 78, "sha256": canonical.sha(canonical._m1_trajectory_json(production))})
            self.assertEqual(attempt["harnessInventoryCountAndSha256"], {
                "count": 12, "sha256": canonical.sha(canonical._m1_trajectory_json(harness))})
            self.assertEqual(attempt["controllerSha256"], recipe["controller_sha256"])
            self.assertEqual(attempt["compressedInputSha256"], recipe["gzip"]["sha256"])
            self.assertEqual(attempt["headCommit"], "1" * 40)
            self.assertEqual(attempt["workflowRunId"], "42")
            with patch.object(canonical.subprocess, "run", return_value=head), \
                    self.assertRaisesRegex(ValueError, "conflicting_input"):
                canonical._m1_trajectory_binding(canonical._m1_new_trajectory_capture(), "uninstrumented",
                                                dict(expected, replayFileSha256="f" * 64), production, harness)

    def test_actual_m1_cli_keeps_first_fault_and_finalizes_capture_before_returning_failure(self):
        case = self.attempt()
        output = self.work / "cli-output"
        arguments = [
            "--fixture-manifest", str(self.work / "inert-fixture"), "--sound-functions", str(self.work / "inert-sound"),
            "--source-root", str(self.work), "--c2-m1-inputs", str(self.work / "inert-profile"),
            "--replay-commands", str(case["input"]), "--output", str(output), "--source-compatibility", "portable"]
        calls = []
        def process(command, **kwargs):
            calls.append(command)
            if command[:2] == ["dotnet", "build"]:
                return canonical.subprocess.CompletedProcess(command, 0, "0 Warning(s)\n0 Error(s)\n", "")
            self.assertEqual(command[0], "dotnet")
            self.assertEqual(kwargs["env"]["DOTNET_EnableDiagnostics"], "0")
            destination = Path(command[3])
            destination.mkdir(parents=True)
            for name in ("m1-public-replay.jsonl", "m1-replay-result.json"):
                shutil.copyfile(case["destination"] / name, destination / name)
            summary = {"schema": "source-movement-caller-checks/v1", "status": "error", "assertions": 1}
            return canonical.subprocess.CompletedProcess(command, 2, json.dumps(summary) + "\n", "")
        with ExitStack() as stack:
            stack.enter_context(patch.object(canonical, "fixture_profile"))
            stack.enter_context(patch.object(canonical, "fixture_hashes", return_value={}))
            stack.enter_context(patch.object(canonical, "canonical_hashes", return_value={}))
            stack.enter_context(patch.object(canonical, "production_paths",
                                            return_value=[canonical.ROOT / canonical.CACHE_SOURCE]))
            stack.enter_context(patch.object(canonical, "HARNESS", ()))
            stack.enter_context(patch.object(canonical, "_m1_trajectory_binding", side_effect=self.synthetic_binding))
            stack.enter_context(patch.object(canonical.subprocess, "run", side_effect=process))
            physics = stack.enter_context(patch.object(canonical, "compare",
                                                      side_effect=AssertionError("no native execution")))
            with redirect_stdout(io.StringIO()), redirect_stderr(io.StringIO()):
                code = canonical.m1_main(arguments)
            physics.assert_not_called()
        self.assertEqual(code, 1)
        self.assertEqual(len(calls), 2, "one mocked build and one mocked source attempt; no rescue or next replay")
        result = json.loads((output / "result.json").read_bytes())
        self.assertEqual(result["status"], "failed-portable-source-fault")
        self.assertEqual(result["public_vector_executions"], 1)
        self.assertEqual(set(result["runs"]), {"uninstrumented"})
        self.assertFalse(result["complete_compiled_control_closure_executed"])
        safe = output / canonical._M1_TRAJECTORY_DIRECTORY
        envelope = json.loads((safe / "attempt.json").read_bytes())
        self.assertEqual(envelope["status"], "complete")
        self.assertEqual(envelope["trajectory"]["rows"], 2)
        self.assertEqual(envelope["attempt"]["terminalReportFileSha256"], canonical.sha((output / "result.json").read_bytes()))
        self.assertEqual(json.loads((safe / "trajectory.jsonl").read_bytes().splitlines()[0])["host"], 0)

    def test_csharp_reads_literal_pre_and_post_operands_without_extra_step_or_inference(self):
        source = (canonical.HERE / "Canonical.cs").read_text()
        method = source[source.index("    private static void M1CompleteReplay("):
                        source.index("    private static int AuthorPreSnapPublicPolicy(")]
        self.assertEqual(method.count("game.Step(command)"), 1)
        for capture in (
                "var prePublished = new { game.State.Tick, game.State.Phase, game.State.Possession };",
                "var preModel = new { game.Model.Phase, game.Model.Possession };",
                "var preRules = new { game.Model.SourceRules.OffenseSide };"):
            self.assertIn(capture, method)
            self.assertLess(method.index(capture), method.index("try { game.Step(command)"))
        for name in ("postAttemptPublished", "postAttemptModel", "postAttemptRules"):
            self.assertLess(method.index("catch (Exception error)"), method.index("var " + name))
            self.assertLess(method.index("var " + name), method.index("game.TakeSourceSoundVmDispatches()"))
        self.assertIn('firstFailure == null ? "succeeded" : "faulted"', method)
        self.assertIn("if (firstFailure != null) throw firstFailure;", method)

    def test_existing_workflow_jobs_gates_budgets_and_retention_transport_remain_unchanged(self):
        text = (canonical.ROOT / ".github/workflows/game-build.yml").read_text()
        self.assertEqual(canonical.sha(text.encode()),
                         "3803af8675168fa48a6e4f3e85b8d7fd86f53fe7236002324d29562679a91f69",
                         "every original job, gate, input and budget must remain byte-identical")
        self.assertIn("--output .squad/.scratch/m1-ci-full-qualification --source-compatibility portable", text)
        self.assertIn("--output .squad/.scratch/defensive-presnap-switch-ci/new-full --source-compatibility portable", text)

    @staticmethod
    def log_bindings(envelope):
        return {key: envelope["attempt"][key] for key in (
            "workflowRunId", "jobId", "headCommit", "headTree", "actualReceiptProfile",
            "productionInventoryCountAndSha256", "harnessInventoryCountAndSha256")}

    def test_failed_job_log_transport_round_trips_exact_safe_files_without_artifact_upload(self):
        case = self.attempt()
        safe, envelope, expected_rows = self.publish(case, self.capture(case))
        bindings = self.log_bindings(envelope)
        for timestamp in ("", "2026-10-23T09:24:00.1234567Z "):
            lines = [timestamp + line for line in case["log"].splitlines(keepends=True)]
            rows, receipt = canonical.read_m1_trajectory_log(lines, bindings)
            self.assertEqual(rows, expected_rows)
            self.assertEqual(receipt, envelope)
            self.assertEqual(b"".join(canonical._m1_trajectory_json(row) + b"\n" for row in rows),
                             (safe / "trajectory.jsonl").read_bytes())
            self.assertEqual(canonical._m1_trajectory_json(receipt) + b"\n", (safe / "attempt.json").read_bytes())
            records = [line for line in case["log"].splitlines(keepends=True) if line.startswith(
                (canonical._M1_TRAJECTORY_ROW_PREFIX, canonical._M1_TRAJECTORY_SEAL_PREFIX))]
            self.assertLessEqual(sum(len(line.encode("ascii")) for line in records), 16777216)
            self.assertTrue(all(len(line.encode("ascii")) <= 16384 for line in records))

    def test_log_intake_rejects_wrong_jobs_missing_or_duplicate_rows_seals_and_unknown_payloads(self):
        case = self.attempt()
        _, envelope, _ = self.publish(case, self.capture(case))
        bindings = self.log_bindings(envelope)
        lines = [line for line in case["log"].splitlines(keepends=True) if line.startswith(
            (canonical._M1_TRAJECTORY_ROW_PREFIX, canonical._M1_TRAJECTORY_SEAL_PREFIX))]
        for changed in (
                lines[1:], lines[:1] + lines, lines[:-1], lines + lines[-1:],
                lines[:1] + lines[2:],
                [line.replace('"preModel":', '"actors":') if index == 0 else line
                 for index, line in enumerate(lines)],
                [line.replace('"sealSha256":"', '"sealSha256":"f') if index == 2 else line
                 for index, line in enumerate(lines)]):
            with self.assertRaises(ValueError):
                canonical.read_m1_trajectory_log(changed, bindings)
        for field, value in (("workflowRunId", "99"), ("jobId", "unrelated-job"),
                             ("headCommit", "f" * 40), ("actualReceiptProfile", "unrelated"),
                             ("harnessInventoryCountAndSha256", {"count": 12, "sha256": "f" * 64})):
            with self.subTest(field=field), self.assertRaisesRegex(ValueError, "malformed_binding"):
                canonical.read_m1_trajectory_log(lines, dict(bindings, **{field: value}))
        with self.assertRaisesRegex(ValueError, "malformed_binding"):
            canonical.read_m1_trajectory_log(lines, {})

    def test_actual_emitted_scalar_parser_rejects_missing_null_extra_and_malformed_fields(self):
        case = self.attempt()
        self.capture(case)
        row = json.loads((case["output"] / "work/current-trajectory.jsonl").read_bytes().splitlines()[0])
        mutations = [
            lambda r: r.pop("inputRowSha256"),
            lambda r: r.update(generatedRowSha256=None),
            lambda r: r.update(host=True),
            lambda r: r.update(inputOneBasedLine=2),
            lambda r: r.update(generatedRowOneBasedLine=0),
            lambda r: r.update(host=12000),
            lambda r: r["prePublished"].update(Tick=None),
            lambda r: r["preRules"].update(OffenseSide=None),
            lambda r: r["postAttemptModel"].update(Phase=5),
            lambda r: r["prePublished"].update(actors=self.private),
            lambda r: r["rawStationMenuIds"][0].update(axes=self.private),
            lambda r: r["rawStationMenuIds"][0].update(station=True),
            lambda r: r["rawStationMenuIds"][0].update(playbook=None),
            lambda r: r.update(rawStationMenuIds=r["rawStationMenuIds"] * 5),
            lambda r: r.update(rawStationMenuIds=r["rawStationMenuIds"] * 2),
            lambda r: r["postAttemptSourceEventIds"][0].pop("Team"),
            lambda r: r["postAttemptSourceEventIds"][0].update(Result=self.private),
            lambda r: r["postAttemptSourceEventIds"][0].update(Type=True),
            lambda r: r["postAttemptSourceEventIds"][0].update(StationId=4),
            lambda r: r.update(stepSucceededOrFaulted=None),
        ]
        for mutate in mutations:
            changed = copy.deepcopy(row)
            mutate(changed)
            with self.assertRaises(ValueError):
                canonical._m1_trajectory_row(changed, 0)

    def test_full12000_scalar_capacity_and_terminal_equality_without_executing_a_game(self):
        case = self.attempt()
        command = self.packet()
        command["StationInputs"] = None
        template = case["sources"][0]
        with case["input"].open("wb") as inputs, (case["destination"] / "m1-public-replay.jsonl").open("wb") as generated:
            for host in range(12000):
                before, after = host * 57 // 60, (host + 1) * 57 // 60
                state = {"Phase": 0, "Possession": 0}
                rules = {"OffenseSide": 0}
                row = dict(template, host=host, beforeSourceTick=before, SourceTick=after,
                           prePublished=dict(state, Tick=host), postAttemptPublished=dict(state, Tick=host + 1),
                           preModel=state, postAttemptModel=state, preRules=rules, postAttemptRules=rules,
                           command=command, applied=command, SourceEvents=[], stepSucceededOrFaulted="succeeded")
                inputs.write(canonical.public_replay.encode([{"host": host, "command": command}]))
                generated.write(canonical._m1_trajectory_json(row) + b"\n")
        receipt = case["receipt"]
        receipt.update(completedHosts=12000, SourceTick=11400, failure=None,
                       replayFileSha256=canonical.sha(case["input"].read_bytes()))
        (case["destination"] / "m1-replay-result.json").write_bytes(canonical._m1_trajectory_json(receipt))
        capture = self.capture(case)
        self.assertEqual(capture["status"], "complete")
        self.assertEqual(capture["rows"], 12000)
        self.assertEqual(capture["terminal"]["outcome"], "succeeded")
        self.assertEqual(capture["terminal"]["afterSourceTick"], 11400)
        result = {"status": "failed", "runs": {"uninstrumented": {
            "trajectory_capture": capture, "replay_receipt_sha256": capture["attempt"]["actualReceiptSha256"]}}}
        stderr = io.StringIO()
        with redirect_stdout(io.StringIO()), redirect_stderr(stderr):
            canonical._write_m1_result(case["output"], result)
        safe = case["output"] / canonical._M1_TRAJECTORY_DIRECTORY
        envelope = json.loads((safe / "attempt.json").read_bytes())
        self.assertEqual(envelope["status"], "complete")
        self.assertEqual(envelope["trajectory"]["rows"], 12000)
        self.assertEqual(envelope["terminal"]["resultStatus"], "failed", "capacity does not qualify a replay")
        self.assertEqual(envelope["terminal"]["afterSourceTick"], 11400)
        rows, restored = canonical.read_m1_trajectory_log(stderr.getvalue().splitlines(), self.log_bindings(envelope))
        self.assertEqual(restored, envelope)
        self.assertEqual((rows[0]["host"], rows[-1]["host"]), (0, 11999))
        self.assertEqual(canonical.sha(b"".join(canonical._m1_trajectory_json(row) + b"\n" for row in rows)),
                         envelope["trajectory"]["sha256"])
        self.assertLessEqual(sum(len(line.encode("ascii")) + 1 for line in stderr.getvalue().splitlines()
                                 if line.startswith((canonical._M1_TRAJECTORY_ROW_PREFIX,
                                                     canonical._M1_TRAJECTORY_SEAL_PREFIX))), 16777216)


if __name__ == "__main__":
    unittest.main()
