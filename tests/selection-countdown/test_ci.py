#!/usr/bin/env python3
"""Mandatory additive CI, immutable source authority and nonoptional regression/control guards."""
import argparse
import ast
import copy
from contextlib import redirect_stderr
import hashlib
import importlib.util
import io
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
from threading import Barrier, Event, Lock
import unittest
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[2]
HERE = Path(__file__).resolve().parent
BASE_WORKFLOW_SHA256 = "1016fb32a3a24b2f020c1cd75bb8e348dd92c789bf70378f85afffd992681e06"
GUARD_COMMAND = ("python3 -B tests/selection-countdown/test_ci.py "
    "--authority .squad/.scratch/SourceData/possession-service-original --output .squad/.scratch/selection-countdown-ci-guards")
RUN_COMMAND = ("python3 -B tests/selection-countdown/run.py "
    "--fixture .squad/.scratch/SourceData/source-kernel-contact272/fixture.json "
    "--sound .squad/.scratch/SourceData/source-kernel-contact272/audio-functions/sound-functions.safn "
    "--m1 .squad/.scratch/SourceData/c2-m1 "
    "--impact .squad/.scratch/SourceData/contact-profile/impact-m1/impact-presentation.json "
    "--normal .squad/.scratch/raw-camera-ci/original-C/normal.json "
    "--speech .squad/.scratch/SourceData/possession-services/speech/possession-speech.bytes "
    "--hud .squad/.scratch/SourceData/possession-services/hud/interface.json "
    "--fire .squad/.scratch/SourceData/possession-services/fire "
    "--authority .squad/.scratch/SourceData/possession-service-original "
    "--output .squad/.scratch/selection-countdown-ci --controls --workers 2")
TIMER_STEP = ("      - name: Compare original selection countdown and actual cabinet timeout\n"
    "        run: |\n          " + GUARD_COMMAND + "\n          " + RUN_COMMAND + "\n")
POSSESSION_CASE = "portable-cabinet-wide-pass-clearance-possession-12000"
TIMER_CASE = "portable-cabinet-wide-pass-clearance-selection-countdown-12000"
TIMER_PREPARE_STEP = ("      - name: Prepare source-timer public replay input without simulation\n"
    "        run: python3 -B tests/movement-callers/prepare_public_replay.py prepare "
    f"--case {TIMER_CASE} --fixture tests/movement-callers/fixtures/{TIMER_CASE}.jsonl.gz "
    f"--recipe tests/movement-callers/fixtures/{TIMER_CASE}.recipe.json "
    f"--output .squad/.scratch/SourceData/{TIMER_CASE}.jsonl\n")
BASE_GENERATION_COMMAND = ("python3 -B tests/movement-callers/check_replay_generations.py "
    "--baseline-ref cf3f2b5db3cc94ac3f51e01dae94dd74d90ebc02 "
    "--fixture-manifest .squad/.scratch/SourceData/source-kernel-contact272/fixture.json "
    "--sound-functions .squad/.scratch/SourceData/source-kernel-contact272/audio-functions/sound-functions.safn "
    "--c2-m1-inputs .squad/.scratch/SourceData/c2-m1 "
    "--replay-commands .squad/.scratch/SourceData/portable-cabinet-wide-pass-clearance-12000.jsonl "
    "--output .squad/.scratch/public-replay-generation-ci")
GENERATION_COMMAND = (BASE_GENERATION_COMMAND +
    " --previous-ref e539258b6b6160f0dcdc072a05c4887e6946423e "
    f"--possession-replay-commands .squad/.scratch/SourceData/{POSSESSION_CASE}.jsonl")
EXPORT_UNIT_LINE = "          python3 -B tests/movement-callers/test_export_portable_public.py -q\n"
GENERATION_DELTA = (
    ("", TIMER_PREPARE_STEP),
    ("", EXPORT_UNIT_LINE),
    (BASE_GENERATION_COMMAND, GENERATION_COMMAND),
    (f"--replay-commands .squad/.scratch/SourceData/{POSSESSION_CASE}.jsonl",
     f"--replay-commands .squad/.scratch/SourceData/{TIMER_CASE}.jsonl"),
)
INVENTORY_COMMANDS = (
    "python3 -B tests/kick-meter/test_runner.py -q",
    "python3 -B tests/fire-kick-integration/test_runner.py -q",
    "python3 -B -m unittest discover -s tests/fire-producers -p 'test_*.py' -q",
    "python3 -B -m unittest discover -s tests/audio/runtime -p 'test_ci.py' -q",
    "python3 -B tests/movement-callers/test_public_replay.py -q",
    "python3 -B tests/movement-callers/test_replay_generations.py -q",
    "python3 -B tests/movement-callers/test_export_portable_public.py -q",
    "python3 -B tests/source-host-lifecycle/run.py --output tests/source-host-lifecycle/.artifacts/ci-managed --controls",
    "python3 -B tests/impact-dust/test_ci.py",
    "python3 -B tests/impact-dust/run.py --output .squad/.scratch/impact-dust-ci --controls",
    "python3 -B tests/impact-dust/js_controls.py --output .squad/.scratch/impact-dust-js-ci",
    "python3 -B tests/loose-helmet/test_ci.py",
    "python3 -B tests/loose-helmet/run.py --output .squad/.scratch/loose-helmet-ci --controls",
    "python3 -B tests/loose-helmet/js_controls.py --output .squad/.scratch/loose-helmet-js-ci",
)
INVENTORY_ORIGINAL = "".join("          " + command + "\n" for command in INVENTORY_COMMANDS)
INVENTORY_START = ("          python3 -B tests/ci/run_inventory_suites.py "
    "--output .squad/.scratch/inventory-suites-ci <<'STAGE7_SUITES'\n")
INVENTORY_GROUPED = INVENTORY_START + INVENTORY_ORIGINAL + "          STAGE7_SUITES\n"


def load(name, path):
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


run = load("selection_countdown_runner_guards", HERE / "run.py")


def validate_workflow(text):
    if text.count(TIMER_STEP) != 1:
        raise AssertionError("Required actual source countdown/cabinet C, production and compiled controls stage changed.")
    if text.count(INVENTORY_GROUPED) != 1:
        raise AssertionError("Exact required Stage7 bindings/runner grouping changed or became optional.")
    tree = ast.parse((ROOT / "tests/ci/run_inventory_suites.py").read_text())
    definitions = {node.targets[0].id: ast.literal_eval(node.value) for node in tree.body
                   if isinstance(node, ast.Assign) and isinstance(node.targets[0], ast.Name) and
                   node.targets[0].id in ("COMMANDS", "BINDINGS_SHA256", "PUBLIC_REPLAY")}
    if (definitions.get("COMMANDS") != INVENTORY_COMMANDS or definitions.get("PUBLIC_REPLAY") != 4 or
            definitions.get("BINDINGS_SHA256") !=
            hashlib.sha256(("\n".join(INVENTORY_COMMANDS) + "\n").encode()).hexdigest()):
        raise AssertionError("Fixed Stage7 runner must bind every original argv once; only replay may use the second chain.")
    inherited = text.replace(INVENTORY_GROUPED, INVENTORY_ORIGINAL, 1)
    for old, new in GENERATION_DELTA:
        if inherited.count(new) != 1:
            raise AssertionError("Required exact source-timer recording, both generation boundaries and exporter guards changed.")
        inherited = inherited.replace(new, old, 1)
    inherited = inherited.replace(TIMER_STEP, "", 1)
    if hashlib.sha256(inherited.encode()).hexdigest() != BASE_WORKFLOW_SHA256:
        raise AssertionError("Preserve EVERY main39 command, inventory, pin, campaign and exact40-minute budget unchanged.")
    if text.index(TIMER_STEP) < text.index("--output .squad/.scratch/possession-services-ci --controls"):
        raise AssertionError("Authenticated original source, current kernel, camera, HUD/FX/speech must precede the timer suite.")
    if not (text.index(TIMER_PREPARE_STEP) < text.index(GENERATION_COMMAND) <
            text.index(f"--replay-commands .squad/.scratch/SourceData/{TIMER_CASE}.jsonl")):
        raise AssertionError("Explicit current timer preparation and both historical/current boundaries precede full current M1.")
    return inherited


class SelectionCI(unittest.TestCase):
    authority = output = None

    def test_exact_additive_workflow_and_every_inherited_campaign(self):
        validate_workflow((ROOT / ".github/workflows/game-build.yml").read_text())
        possession = (ROOT / "tests/presentation/possession-services/test_ci.py").read_text()
        self.assertIn("selection_ci.validate_workflow(text)", possession)

    def test_only_exact_reversible_inventory_grouping_is_allowed(self):
        text = (ROOT / ".github/workflows/game-build.yml").read_text()
        for old, new in (
            (INVENTORY_GROUPED, INVENTORY_ORIGINAL),
            (INVENTORY_START, INVENTORY_START.replace("--output .squad/.scratch/inventory-suites-ci ", "")),
            (INVENTORY_START, INVENTORY_START.replace("python3 -B", "echo python3 -B")),
            (INVENTORY_START, INVENTORY_START.replace(" <<", " || true <<")),
            (INVENTORY_START, INVENTORY_START.replace("python3 -B", "BLITZ_CI_MUTATION_WORKERS=3 python3 -B")),
            ("          STAGE7_SUITES\n", ""),
            (INVENTORY_COMMANDS[4], INVENTORY_COMMANDS[4] + " --workers 2"),
            (INVENTORY_COMMANDS[4], "true"),
            (INVENTORY_COMMANDS[4], INVENTORY_COMMANDS[5]),
        ):
            with self.subTest(change=old), self.assertRaises(AssertionError):
                validate_workflow(text.replace(old, new, 1))

    def test_deletion_substitution_optional_execution_budget_or_replay_changes_are_red(self):
        text = (ROOT / ".github/workflows/game-build.yml").read_text()
        changes = [(TIMER_STEP, ""), (RUN_COMMAND, "echo " + RUN_COMMAND), (GUARD_COMMAND, "true"),
            (RUN_COMMAND, RUN_COMMAND.replace(" --controls", "")),
            (RUN_COMMAND, RUN_COMMAND.replace(" --workers 2", "")),
            (RUN_COMMAND, RUN_COMMAND.replace(" --workers 2", " --workers 1")),
            (RUN_COMMAND, RUN_COMMAND.replace(" --workers 2", " --workers 3")),
            ("timeout-minutes: 40", "timeout-minutes: 41"),
            ("BLITZ_CI_MUTATION_WORKERS: '2'", "BLITZ_CI_MUTATION_WORKERS: '1'"),
            ("      - name: Compare original selection countdown", "      - if: false\n        name: Compare original selection countdown"),
            ("    steps:\n", "    continue-on-error: true\n    steps:\n"),
            ("python3 tests/movement-callers/run.py", "echo python3 tests/movement-callers/run.py"),
            ("--baseline-ref cf3f2b5db3cc94ac3f51e01dae94dd74d90ebc02", "--baseline-ref HEAD")]
        changes += [
            (TIMER_PREPARE_STEP, ""), (GENERATION_COMMAND, BASE_GENERATION_COMMAND),
            (GENERATION_COMMAND, "true"), (EXPORT_UNIT_LINE, ""),
            (EXPORT_UNIT_LINE, EXPORT_UNIT_LINE.replace("python3 -B", "echo python3 -B")),
            (GENERATION_COMMAND, GENERATION_COMMAND + " || true"),
            (GENERATION_COMMAND, GENERATION_COMMAND + " --single-public-attempt"),
            ("--previous-ref e539258b6b6160f0dcdc072a05c4887e6946423e", "--previous-ref HEAD"),
            (f"--possession-replay-commands .squad/.scratch/SourceData/{POSSESSION_CASE}.jsonl",
             f"--possession-replay-commands .squad/.scratch/SourceData/{TIMER_CASE}.jsonl"),
            (f"--replay-commands .squad/.scratch/SourceData/{TIMER_CASE}.jsonl",
             f"--replay-commands .squad/.scratch/SourceData/{POSSESSION_CASE}.jsonl"),
        ]
        for option in ("--authority", "--fire", "--normal", "--hud", "--speech", "--fixture", "--m1"):
            tokens = RUN_COMMAND.split()
            offset = tokens.index(option)
            changed = tokens[:offset] + tokens[offset + 2:]
            changes.append((RUN_COMMAND, " ".join(changed)))
        for old, new in changes:
            with self.subTest(changed=old):
                self.assertEqual(text.count(old), 1)
                with self.assertRaises(AssertionError):
                    validate_workflow(text.replace(old, new, 1))

    def test_actual_inputs_and_all_controls_are_required_arguments(self):
        tokens = RUN_COMMAND.split()[3:]
        self.assertTrue(run.argument_parser().parse_args(tokens).controls)
        for option in ("--controls", "--authority", "--fire", "--normal", "--hud", "--speech", "--fixture", "--m1"):
            changed = list(tokens)
            start = changed.index(option)
            del changed[start:start + (1 if option == "--controls" else 2)]
            with self.subTest(option=option), redirect_stderr(io.StringIO()), self.assertRaises(SystemExit):
                run.argument_parser().parse_args(changed)

    def test_no_case_or_compiled_control_can_disappear(self):
        fixtures, controls = run.required_inventory()
        self.assertEqual((len(fixtures), sum(c["ticks"] for c in fixtures), len(controls)), (17, 10417, 21))
        for case in fixtures:
            with self.subTest(case=case["name"]), patch.object(run, "cases",
                    return_value=[c for c in fixtures if c != case]), self.assertRaisesRegex(AssertionError, "All17"):
                run.required_inventory()
        for control in controls:
            with self.subTest(control=control[0]), patch.object(run, "mutations",
                    return_value=[c for c in controls if c != control]), self.assertRaisesRegex(AssertionError, "All21"):
                run.required_inventory()

    def test_random_cases_and_compiled_controls_are_additive_and_mandatory(self):
        fixtures, controls = run.required_random_inventory()
        self.assertEqual((len(fixtures), len(controls)), (33, 21))
        for case in fixtures:
            with self.subTest(case=case["name"]), patch.object(run.random_oracle, "cases",
                    return_value=[c for c in fixtures if c != case]), self.assertRaisesRegex(AssertionError, "All33"):
                run.required_random_inventory()
        for control in controls:
            with self.subTest(control=control[0]), patch.object(run.controls_module, "random_mutations",
                    return_value=[c for c in controls if c != control]), self.assertRaisesRegex(AssertionError, "All21"):
                run.required_random_inventory()
        names = {c["name"] for c in fixtures}
        self.assertTrue(all(c[5] == "lifecycle" or set(c[5].split(",")) <= names for c in controls))

    def test_fixed_scopes_bind_every_exact_immutable_control_in_both_runners(self):
        _, timer = run.required_inventory()
        random_fixtures, random_controls = run.required_random_inventory()
        controls = timer + random_controls
        run.validate_control_inventory(controls)
        block = (HERE / "Program.cs").read_text().split("Dictionary<string, string> controlScopes", 1)[1].split("};", 1)[0]
        managed = dict(re.findall(r'\{ "([^"]+)", "([^"]+)" \}', block))
        self.assertEqual(managed, run.CONTROL_SCOPES)
        self.assertEqual(list(run.EXPECTED_CAUSES), [c[0] for c in controls])
        for index, control in enumerate(controls):
            scope = run.control_scope(control, random_fixtures)
            run.validate_binding(control[0], scope)
            self.assertIn(control[4], run.EXPECTED_CAUSES[control[0]])
            for wrong in ("", "all", "timer", "unknown", "random-offense-raw0" if scope != "random-offense-raw0" else "timer-traces"):
                with self.subTest(control=control[0], scope=wrong), self.assertRaisesRegex(AssertionError, "binding"):
                    run.validate_binding(control[0], wrong)
            for field in range(len(control)):
                changed = list(controls)
                replacement = list(control)
                replacement[field] += "-substituted"
                changed[index] = tuple(replacement)
                with self.subTest(control=control[0], field=field), self.assertRaisesRegex(AssertionError, "All42 exact immutable"):
                    run.validate_control_inventory(changed)
        for unknown in ("", "unknown", "baseline-substitute"):
            with self.subTest(control=unknown), self.assertRaisesRegex(AssertionError, "binding"):
                run.validate_binding(unknown, "all")
        with self.assertRaisesRegex(AssertionError, "All42 exact immutable"):
            run.validate_control_inventory(controls[:-1])

    def test_case_execution_receipts_require_exact_bindings_and_real_completed_prefixes(self):
        fixtures, timer = run.required_inventory()
        random_fixtures, random_controls = run.required_random_inventory()
        full_plan = run.execution_plan("all", fixtures, random_fixtures)
        self.assertEqual(full_plan, [c["name"] for c in fixtures] + ["actual-random-gesture"] +
            list(run.LIFECYCLE_CASES) + [c["name"] for c in random_fixtures] + list(run.CALLER_CASES))
        for control in timer + random_controls:
            scope = run.control_scope(control, random_fixtures)
            plan = run.execution_plan(scope, fixtures, random_fixtures)
            failed = run.EXPECTED_CAUSES[control[0]].startswith("SELECTION ASSERTION: ")
            receipt = dict(schema="selection-execution-scope/v2", admitted=True, control=control[0], scope=scope,
                requestedCases=plan, status="failed" if failed else "passed",
                startedCases=plan[:1] if failed else plan, completedCases=[] if failed else plan)
            run.validate_execution_scope(receipt, control[0], scope, fixtures, random_fixtures, int(failed))
            for field, value in (("control", "another-control"), ("scope", "all"), ("admitted", False),
                                 ("requestedCases", plan[:-1]), ("requestedCases", ["unrelated"] * len(plan)),
                                 ("startedCases", []), ("startedCases", ["unrelated"]), ("completedCases", ["unrelated"]),
                                 ("status", "unknown")):
                bad = dict(receipt, **{field: value})
                with self.subTest(control=control[0], field=field), self.assertRaises(AssertionError):
                    run.validate_execution_scope(bad, control[0], scope, fixtures, random_fixtures, int(failed))
            with self.subTest(partial=control[0]), self.assertRaisesRegex(AssertionError, "binding"):
                run.validate_execution_scope(receipt, "baseline", "all", fixtures, random_fixtures, int(failed))
        full = dict(schema="selection-execution-scope/v2", admitted=True, control="baseline", scope="all",
            requestedCases=full_plan, startedCases=full_plan, completedCases=full_plan, status="passed")
        run.validate_execution_scope(full, "baseline", "all", fixtures, random_fixtures, 0)
        for field in ("requestedCases", "startedCases", "completedCases"):
            bad = dict(full, **{field: full_plan[:-1]})
            with self.subTest(full=field), self.assertRaises(AssertionError):
                run.validate_execution_scope(bad, "baseline", "all", fixtures, random_fixtures, 0)

    def test_only_the_fixed_preexisting_causal_failure_counts(self):
        _, timer = run.required_inventory()
        _, random_controls = run.required_random_inventory()
        for control in timer + random_controls:
            label = control[0]
            row = dict(name=label, scope=run.CONTROL_SCOPES[label], compilerInvoked=True, compile=0, exit=1,
                executionExit=1 if run.EXPECTED_CAUSES[label].startswith("SELECTION ASSERTION: ") else 0,
                originalTimerComparedCases=17, output=run.EXPECTED_CAUSES[label] + "\nretained detail")
            run.validate_causal_failure(control, row)
            for field, value in (("name", "other"), ("scope", "all"), ("compilerInvoked", False), ("compile", 1),
                                 ("exit", 0), ("executionExit", 2), ("output", "SELECTION ASSERTION: unrelated\n" + row["output"]),
                                 ("output", row["output"].splitlines()[0] + " substituted\n")):
                bad = dict(row, **{field: value})
                with self.subTest(control=label, field=field), self.assertRaisesRegex(AssertionError, "causal control"):
                    run.validate_causal_failure(control, bad)
            if run.EXPECTED_CAUSES[label].startswith("original-C timer trace"):
                with self.subTest(traces=label), self.assertRaisesRegex(AssertionError, "All17"):
                    run.validate_causal_failure(control, dict(row, originalTimerComparedCases=16))

    def test_workers_are_explicit_serial_by_default_and_bounded(self):
        tokens = RUN_COMMAND.split()[3:]
        start = tokens.index("--workers")
        del tokens[start:start + 2]
        with patch.dict(os.environ, BLITZ_CI_MUTATION_WORKERS="2"):
            self.assertEqual(run.argument_parser().parse_args(tokens).workers, 1)
            active, maximum = 0, 0
            def serial(control):
                nonlocal active, maximum
                active += 1
                maximum = max(maximum, active)
                active -= 1
                return dict(name=control[0])
            self.assertEqual([r["name"] for r in run.run_controls([("one",), ("two",)], serial)], ["one", "two"])
            self.assertEqual(maximum, 1)
        self.assertEqual(run.argument_parser().parse_args(tokens + ["--workers", "2"]).workers, 2)
        for value in ("0", "3", "-1", "1.5", "", "two"):
            with self.subTest(value=value), redirect_stderr(io.StringIO()), self.assertRaises(SystemExit):
                run.argument_parser().parse_args(tokens + ["--workers", value])
        for value in (0, 3, "2", True, None):
            with self.subTest(internal=value), self.assertRaisesRegex(AssertionError, "exactly 1 or 2"):
                run.run_controls([], lambda _: None, value)

    def test_two_workers_isolate_outputs_and_return_declared_order(self):
        barrier, release, lock = Barrier(2), Event(), Lock()
        active, maximum, completed = 0, 0, []
        def execute(control):
            nonlocal active, maximum
            name = control[0]
            with lock:
                active += 1
                maximum = max(maximum, active)
            destination = self.output / ("worker-" + name)
            destination.mkdir()
            (destination / "source.cs").write_text(name)
            (destination / "runtime").mkdir()
            barrier.wait(timeout=10)
            if name == "one":
                self.assertTrue(release.wait(timeout=10))
            with lock:
                completed.append(name)
                active -= 1
            if name == "two":
                release.set()
            return dict(name=name, buildRoot=str(destination), runtimeRoot=str(destination / "runtime"))
        rows = run.run_controls([("one",), ("two",)], execute, 2)
        self.assertEqual(maximum, 2)
        self.assertEqual(completed, ["two", "one"])
        self.assertEqual([r["name"] for r in rows], ["one", "two"])
        self.assertEqual(len({r["buildRoot"] for r in rows}), 2)
        self.assertEqual(len({r["runtimeRoot"] for r in rows}), 2)
        for row in rows:
            self.assertEqual((Path(row["buildRoot"]) / "source.cs").read_text(), row["name"])

    def test_worker_errors_drain_and_receipt_or_identity_substitution_is_red(self):
        barrier, finished = Barrier(2), Event()
        def execute(control):
            barrier.wait(timeout=10)
            if control[0] == "one":
                raise RuntimeError("actual child failed")
            finished.set()
            return dict(name=control[0])
        with self.assertRaisesRegex(RuntimeError, "actual child failed"):
            run.run_controls([("one",), ("two",)], execute, 2)
        self.assertTrue(finished.is_set())
        for controls in ([("one",), ("one",)], [("../escape",)], [("",)], [("all/cases",)]):
            with self.subTest(controls=controls), self.assertRaisesRegex(AssertionError, "distinct, nonescaping"):
                run.run_controls(controls, lambda _: self.fail("Invalid control was scheduled."), 2)
        with self.assertRaisesRegex(AssertionError, "Missing/substituted"):
            run.run_controls([("one",)], lambda _: dict(name="two"))

    def test_post_checks_share_the_worker_limit_and_drain_on_failure(self):
        calls = []
        self.assertEqual(run.post_checks(lambda: calls.append("final") or 1,
            lambda: calls.append("compatibility") or 2, 1), (1, 2))
        self.assertEqual(calls, ["final", "compatibility"])
        barrier, finished = Barrier(2), Event()
        def final():
            barrier.wait(timeout=10)
            raise RuntimeError("final baseline failed")
        def compatibility():
            barrier.wait(timeout=10)
            finished.set()
            return 2
        with self.assertRaisesRegex(RuntimeError, "final baseline failed"):
            run.post_checks(final, compatibility, 2)
        self.assertTrue(finished.is_set())
        for workers in (0, 3, "2", True):
            with self.subTest(workers=workers), self.assertRaisesRegex(AssertionError, "exactly 1 or 2"):
                run.post_checks(lambda: None, lambda: None, workers)

    def test_only_fresh_full_unchanged_baseline_compilation_can_be_reused(self):
        source, destination = self.output / "fresh-baseline.dll", self.output / "final-baseline.dll"
        source.write_bytes(b"this run's freshly compiled bytes")
        baseline = dict(name="baseline", scope="all", compile=0, exit=0,
                        assemblySha256=hashlib.sha256(source.read_bytes()).hexdigest())
        run.reuse_baseline_assembly(baseline, source, destination)
        self.assertEqual(destination.read_bytes(), source.read_bytes())
        for field, value in (("name", "old-run"), ("scope", "random-offense-raw0"), ("compile", 1), ("exit", 1)):
            bad = dict(baseline, **{field: value})
            with self.subTest(field=field), self.assertRaisesRegex(AssertionError, "fully qualified"):
                run.reuse_baseline_assembly(bad, source, destination)
        source.write_bytes(source.read_bytes() + b"changed")
        with self.assertRaisesRegex(AssertionError, "assembly changed"):
            run.reuse_baseline_assembly(baseline, source, destination)

    def test_output_must_be_new_resolved_and_worktree_local(self):
        good = self.output / "new-output"
        self.assertEqual(run.new_output_path(good), good)
        for path in (self.output, HERE / "not-scratch", ROOT / ".squad/.scratch", good / ".." / "alias"):
            with self.subTest(path=str(path)), self.assertRaisesRegex(AssertionError, "NEW ignored worktree-local"):
                run.new_output_path(path)
        alias = self.output / "symlink-output"
        alias.symlink_to(self.output, target_is_directory=True)
        try:
            with self.assertRaisesRegex(AssertionError, "without symlinked parents"):
                run.new_output_path(alias / "new-child")
        finally:
            alias.unlink()

    def test_success_requires_exact_full_or_causal_scope_not_a_case_count(self):
        fixtures, timer = run.required_inventory()
        random_fixtures, controls = run.required_random_inventory()
        full = dict(status="passed", scope="all", assertions=42424,
                    cases=[c["name"] for c in fixtures] + list(run.CALLER_CASES),
                    randomCases=[c["name"] for c in random_fixtures],
                    randomLifecycleCases=list(run.LIFECYCLE_CASES))
        self.assertEqual(run.validate_summary(json.dumps(full), "all", fixtures, random_fixtures), full)
        with self.assertRaisesRegex(AssertionError, "omitted/substituted"):
            run.validate_summary(json.dumps(dict(full, assertions=42423)), "all", fixtures, random_fixtures)
        for field in ("cases", "randomCases", "randomLifecycleCases"):
            for change in ("omitted", "substituted", "reordered"):
                bad = copy.deepcopy(full)
                if change == "omitted":
                    bad[field].pop()
                elif change == "substituted":
                    bad[field][0] = "unrelated-case"
                else:
                    bad[field].reverse()
                with self.subTest(field=field, change=change), self.assertRaisesRegex(AssertionError, "omitted/substituted"):
                    run.validate_summary(json.dumps(bad), "all", fixtures, random_fixtures)
        for control in timer + controls:
            scope = run.control_scope(control, random_fixtures)
            partial = dict(status="passed", scope=scope, assertions=1,
                cases=[c["name"] for c in fixtures] if scope == "timer-traces" else [scope] if scope in run.CALLER_CASES else [],
                randomCases=[scope] if scope in {c["name"] for c in random_fixtures} else [],
                randomLifecycleCases=[scope] if scope in run.LIFECYCLE_CASES else [])
            run.validate_summary(json.dumps(partial), scope, fixtures, random_fixtures)
            with self.subTest(control=control[0]), self.assertRaisesRegex(AssertionError, "partial scope"):
                run.validate_summary(json.dumps(partial), "all", fixtures, random_fixtures)
            bad = copy.deepcopy(partial)
            bad[next(k for k in ("cases", "randomCases", "randomLifecycleCases") if partial[k])] = []
            with self.subTest(missing=control[0]), self.assertRaisesRegex(AssertionError, "omitted/substituted"):
                run.validate_summary(json.dumps(bad), scope, fixtures, random_fixtures)
        for scope in ("", "all", "timer", "random-offense-raw0,random-defense-raw0", "unknown"):
            control = ("unknown", "source", "old", "new", "assertion", scope)
            with self.subTest(scope=scope), self.assertRaises(AssertionError):
                run.control_scope(control, random_fixtures)
        for text in ("", "{", "null", "[]"):
            with self.subTest(corrupt=text), self.assertRaises((ValueError, AttributeError)):
                run.validate_summary(text, "all", fixtures, random_fixtures)

    def test_unknown_child_status_and_unproved_failure_never_count_as_kills(self):
        run.validate_child(subprocess.CompletedProcess([], 0, '{"status":"passed"}', ""))
        run.validate_child(subprocess.CompletedProcess([], 1, "", "SELECTION ASSERTION: actual cause"))
        for code, stdout, stderr in ((2, "", "SELECTION ASSERTION: cause"), (-9, "", ""),
                                     (1, "", "unrelated failure"), (1, "success-shaped fallback", "SELECTION ASSERTION: cause"),
                                     (0, "{}", "compiler warning")):
            with self.subTest(code=code, stdout=stdout, stderr=stderr), self.assertRaises(AssertionError):
                run.validate_child(subprocess.CompletedProcess([], code, stdout, stderr))

    def test_new_random_original_dependencies_are_already_in_the_exact76_acquisition(self):
        fetch = load("random_inherited_acquisition", ROOT / "tests/presentation/possession-services/fetch_inputs.py")
        _, inventory = fetch.catalog()
        raw = run.random_oracle.verified_authority(self.authority)
        self.assertEqual(len(raw), 15)
        for name, pin in run.random_oracle.EXTRA_PINS.items():
            repo, path = name.split("/", 1)
            self.assertEqual(inventory[repo, path]["gitBlobSha1"], pin)
        copied = self.output / "extra-random-authority"
        copied.mkdir()
        for name, data in raw.items():
            path = copied / name
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(data)
        for name in run.random_oracle.EXTRA_PINS:
            path = copied / name
            data = path.read_bytes()
            path.unlink()
            with self.subTest(missing=name), self.assertRaisesRegex(ValueError, "Missing/escaping random controller"):
                run.random_oracle.verified_authority(copied)
            path.write_bytes(data + b"\n")
            with self.subTest(changed=name), self.assertRaisesRegex(ValueError, "Wrong authenticated random controller"):
                run.random_oracle.verified_authority(copied)
            path.unlink()
            path.symlink_to(self.authority / name)
            with self.subTest(symlink=name), self.assertRaisesRegex(ValueError, "Missing/escaping random controller"):
                run.random_oracle.verified_authority(copied)
            path.unlink()
            path.write_bytes(data)

    def test_random_authority_normalizes_relative_cli_root_without_allowing_aliases(self):
        raw = run.random_oracle.verified_authority(self.authority)
        copied = self.output / "relative-random-authority"
        copied.mkdir()
        for name, data in raw.items():
            path = copied / name
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(data)
        previous = Path.cwd()
        try:
            os.chdir(ROOT)
            relative = copied.relative_to(ROOT)
            self.assertEqual(run.random_oracle.verified_authority(relative), raw)
            self.assertEqual(run.random_oracle.verified_authority(copied), raw)
            alias = self.output / "relative-random-authority-alias"
            alias.symlink_to(copied, target_is_directory=True)
            for root in (alias, alias.relative_to(ROOT), copied / ".." / copied.name):
                with self.subTest(root=str(root)), self.assertRaisesRegex(
                        ValueError, "Regular explicit original source authority"):
                    run.random_oracle.verified_authority(root)
        finally:
            os.chdir(previous)

    def test_default_compatibility_cannot_be_empty_filtered_regenerated_or_unpinned(self):
        summary = dict(hosts=1200, SourceTick=1140, rawCalls=491, selections=4, defaultSeed=1, rawVectorFiltered=False)
        receipt = dict(status="passed", base=run.compatibility.BASE, tree=run.compatibility.TREE,
            originalTargetRandomQualified=False, fullPublicReplayRegenerated=False, hostedOrFullM1Campaign=False,
            runs=[dict(name=n, compile=0, exit=0, diagnostics="", summary=copy.deepcopy(summary)) for n in ("main40", "current")],
            compared={n: dict(bytes=1, sha256="a" * 64, byteIdentical=True)
                      for n in ("default-host.jsonl", "default-rng.json", "default-menu.json")})
        run.validate_default_compatibility(receipt)
        for key, value in (("base", "HEAD"), ("tree", "0" * 40), ("originalTargetRandomQualified", True),
                           ("fullPublicReplayRegenerated", True), ("hostedOrFullM1Campaign", True)):
            changed = copy.deepcopy(receipt)
            changed[key] = value
            with self.subTest(field=key), self.assertRaises(AssertionError):
                run.validate_default_compatibility(changed)
        for label in (0, 1):
            for field, value in (("hosts", 1199), ("SourceTick", 1139), ("rawCalls", 0), ("rawCalls", 490),
                                 ("selections", 0), ("selections", 3), ("defaultSeed", 2),
                                 ("rawVectorFiltered", True)):
                changed = copy.deepcopy(receipt)
                changed["runs"][label]["summary"][field] = value
                with self.subTest(run=label, field=field), self.assertRaises(AssertionError):
                    run.validate_default_compatibility(changed)
        for name in receipt["compared"]:
            for key, value in (("bytes", 0), ("sha256", "0"), ("byteIdentical", False)):
                changed = copy.deepcopy(receipt)
                changed["compared"][name][key] = value
                with self.subTest(trace=name, field=key), self.assertRaises(AssertionError):
                    run.validate_default_compatibility(changed)
            changed = copy.deepcopy(receipt)
            del changed["compared"][name]
            with self.subTest(missing=name), self.assertRaises(AssertionError):
                run.validate_default_compatibility(changed)
    def test_every_timer_dependency_is_in_the_inherited_pinned_acquisition_union(self):
        fetch = load("selection_inherited_acquisition", ROOT / "tests/presentation/possession-services/fetch_inputs.py")
        recipe, inputs = fetch.catalog()
        self.assertEqual(len(inputs), 76)
        self.assertEqual(recipe["repositories"]["nfl"]["revision"], "89f840d58d152a2a9f230cb8d0cdffbd554c8ded")
        self.assertEqual(recipe["repositories"]["midway"]["revision"], "1792293aaaea00a919b59e8c05716978e85b4333")
        for name, pin in run.oracle.PINS.items():
            repo, path = name.split("/", 1)
            self.assertEqual(inputs[repo, path]["gitBlobSha1"], pin)
        self.assertEqual(len(run.oracle.verified_authority(self.authority)), 13)

    def test_each_missing_changed_or_symlinked_original_is_a_meaningful_failure(self):
        copied = self.output / "authority"
        copied.mkdir()
        for name in run.oracle.PINS:
            path = copied / name
            path.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(self.authority / name, path)
        for name in run.oracle.PINS:
            path = copied / name
            raw = path.read_bytes()
            path.unlink()
            with self.subTest(missing=name), self.assertRaisesRegex(ValueError, "Missing/escaping timer authority"):
                run.oracle.verified_authority(copied)
            path.write_bytes(bytes([raw[0] ^ 1]) + raw[1:])
            with self.subTest(changed=name), self.assertRaisesRegex(ValueError, "Wrong authenticated timer Git blob"):
                run.oracle.verified_authority(copied)
            path.unlink()
            path.symlink_to(self.authority / name)
            with self.subTest(symlink=name), self.assertRaisesRegex(ValueError, "Missing/escaping timer authority"):
                run.oracle.verified_authority(copied)
            path.unlink()
            path.write_bytes(raw)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--authority", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args, rest = parser.parse_known_args()
    output = args.output.absolute()
    if output.exists() or ROOT / ".squad/.scratch" not in output.parents or output.resolve() != output:
        parser.error("Use a NEW ignored worktree-local guard output.")
    output.mkdir(parents=True)
    SelectionCI.authority, SelectionCI.output = args.authority.absolute(), output
    return not unittest.TextTestRunner(verbosity=1).run(unittest.defaultTestLoader.loadTestsFromTestCase(SelectionCI)).wasSuccessful()


if __name__ == "__main__":
    raise SystemExit(main())
