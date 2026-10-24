#!/usr/bin/env python3
"""Exercise actual overlap, exact inventories, hard bounds and drained failures."""
import contextlib
import copy
import io
import os
import json
from pathlib import Path
import shutil
import subprocess
import sys
import threading
import unittest
from unittest.mock import patch
import uuid

from isolated_mutations import run_mutations
import run_inventory_suites as inventory


class MutationWorkerTests(unittest.TestCase):
    def test_default_is_serial_and_preserves_every_case_in_order(self):
        visited = []
        caller = threading.get_ident()
        def execute(case):
            self.assertEqual(threading.get_ident(), caller)
            visited.append(case[0])
            return case[1]
        with patch.dict(os.environ):
            os.environ.pop("BLITZ_CI_MUTATION_WORKERS", None)
            self.assertEqual(run_mutations((("a", 3), ("b", 7)), execute), [3, 7])
        self.assertEqual(visited, ["a", "b"])

    def test_two_workers_really_overlap_and_return_complete_ordered_inventory(self):
        barrier = threading.Barrier(2, timeout=10)
        lock = threading.Lock()
        active, peak, seen = 0, 0, []
        def execute(case):
            nonlocal active, peak
            with lock:
                active += 1
                peak = max(peak, active)
                seen.append(case[0])
            barrier.wait()
            with lock:
                active -= 1
            return case[0]
        cases = tuple((str(n),) for n in range(6))
        with patch.dict(os.environ, BLITZ_CI_MUTATION_WORKERS="2"):
            self.assertEqual(run_mutations(cases, execute), [case[0] for case in cases])
        self.assertEqual(peak, 2)
        self.assertCountEqual(seen, [case[0] for case in cases])

    def test_duplicate_identities_and_bad_worker_counts_fail_before_execution(self):
        for value in ("", "0", "3", "-1", "02", "true"):
            with self.subTest(workers=value), patch.dict(os.environ, BLITZ_CI_MUTATION_WORKERS=value):
                with self.assertRaisesRegex(ValueError, "exactly 1 or 2"):
                    run_mutations((("case",),), lambda case: self.fail("bad configuration executed work"))
        with self.assertRaisesRegex(ValueError, "unique"):
            run_mutations((("same",), ("same",)), lambda case: self.fail("colliding trees executed work"))

    def test_real_process_failure_is_propagated_after_other_worker_finishes(self):
        barrier = threading.Barrier(2, timeout=10)
        failed = threading.Event()
        release = threading.Event()
        finished = threading.Event()
        returned = threading.Event()
        failures = []
        def execute(case):
            barrier.wait()
            if case[0] == "failing":
                try:
                    subprocess.run([sys.executable, "-c", "raise SystemExit(23)"], check=True, timeout=10)
                except subprocess.CalledProcessError:
                    failed.set()
                    raise
            else:
                self.assertTrue(release.wait(10), "test must release its independent worker")
                subprocess.run([sys.executable, "-c", "print('independent worker completed')"],
                               check=True, capture_output=True, timeout=10)
                finished.set()
        def invoke():
            try:
                run_mutations((("failing",), ("independent",)), execute)
            except BaseException as error:
                failures.append(error)
            finally:
                returned.set()
        with patch.dict(os.environ, BLITZ_CI_MUTATION_WORKERS="2"):
            thread = threading.Thread(target=invoke)
            thread.start()
            try:
                self.assertTrue(failed.wait(10), "exercise the real failing child")
                self.assertFalse(returned.wait(0.05), "failure returned while another tree was still active")
            finally:
                release.set()
                thread.join(15)
        self.assertFalse(thread.is_alive())
        self.assertEqual(len(failures), 1)
        self.assertIsInstance(failures[0], subprocess.CalledProcessError)
        self.assertEqual(failures[0].returncode, 23)
        self.assertTrue(finished.is_set(), "cleanup must not race another worker after failure")

    def test_timeout_and_assertion_failures_are_not_success_shaped_results(self):
        for failure in (subprocess.TimeoutExpired(["compiler"], 1), AssertionError("named causal guard")):
            with self.subTest(error=type(failure).__name__), patch.dict(os.environ, BLITZ_CI_MUTATION_WORKERS="2"):
                def execute(case):
                    raise failure
                with self.assertRaises(type(failure)):
                    run_mutations((("mutant",),), execute)


class InventorySuiteWorkerTests(unittest.TestCase):
    def setUp(self):
        self.output = inventory.ROOT / ".squad/.scratch/ci-inventory-scheduling" / ("unit-" + uuid.uuid4().hex)
        self.output.mkdir(parents=True)
        self.addCleanup(shutil.rmtree, self.output)

    def test_fixed_bindings_and_all100_original_named_cases_are_required(self):
        text = inventory.bindings()
        inventory.validate_bindings(text)
        self.assertEqual(len(inventory.COMMANDS), 14)
        cases = {i: inventory.source_inventory(i)[0] for i in range(14)}
        original = {i: [name for name in names if name not in inventory.CURRENT_CASE_ADDITIONS.get(i, ())]
                    for i, names in cases.items()}
        self.assertEqual(sum(map(len, original.values())), 100)
        self.assertEqual({i: len(names) for i, names in inventory.CURRENT_CASE_ADDITIONS.items()}, {4: 4, 5: 3, 6: 2})
        self.assertEqual(sum(map(len, cases.values())), 109)
        lines = text.splitlines()
        changes = ["", "\n".join(lines[:-1]) + "\n", text + lines[0] + "\n",
                   text.replace(lines[4], lines[4] + " --workers 2"), text.replace(lines[4], "true"),
                   "\n".join(lines[1:2] + lines[0:1] + lines[2:]) + "\n"]
        for changed in changes:
            with self.subTest(binding=changed[:80]), self.assertRaisesRegex(ValueError, "exactly once"):
                inventory.validate_bindings(changed)
        with patch.object(inventory, "COMMANDS", ()), self.assertRaisesRegex(ValueError, "exactly once"):
            inventory.validate_bindings("\n")

    def test_real_source_rejects_removed_renamed_extra_and_duplicate_original_or_added_cases(self):
        for index, additions in inventory.CURRENT_CASE_ADDITIONS.items():
            identities, sources = inventory.source_inventory(index)
            name, = sources
            source = (inventory.ROOT / name).read_text()
            original = next(identity for identity in identities if identity not in additions)
            probes = []
            for identity, diagnostic in ((original, "exact original named"),
                                         (additions[0], "exact current-generation added named")):
                cls, method = identity.split("::")[1].split(".")
                anchor = "    def " + method + "("
                self.assertEqual(source.count(anchor), 1)
                probes.extend((
                    (source.replace(anchor, "    def removed_" + method + "(", 1), diagnostic),
                    (source.replace(anchor, "    def test_unreviewed_replacement(", 1), diagnostic),
                    (source + "\nclass " + cls + ":\n    def " + method + "(self): pass\n", diagnostic),
                ))
            probes.append((source + "\nclass UnreviewedTests:\n    def test_extra(self): pass\n",
                           "exact original named"))
            path = self.output / "workspace" / name
            path.parent.mkdir(parents=True, exist_ok=True)
            with patch.object(inventory, "ROOT", self.output / "workspace"):
                path.write_text(source)
                self.assertEqual(inventory.source_inventory(index)[0], identities)
                for probe, (changed, diagnostic) in enumerate(probes):
                    path.write_text(changed)
                    with self.subTest(index=index, diagnostic=diagnostic), self.assertRaisesRegex(ValueError, diagnostic):
                        inventory.source_inventory(index)
                    with patch.object(inventory.subprocess, "Popen", side_effect=AssertionError("Changed inventory launched a child")):
                        with self.subTest(index=index, caller="execute_child"), self.assertRaisesRegex(ValueError, diagnostic):
                            inventory.execute_child(index, self.output / ("rejected-" + str(index) + "-" + str(probe)))

    def test_child_summaries_require_all_original_and_exact_added_cases_without_skips(self):
        for index, additions in inventory.CURRENT_CASE_ADDITIONS.items():
            identities, _ = inventory.source_inventory(index)
            text = "Ran " + str(len(identities)) + " tests in 0.01s\n\nOK\n"
            self.assertIsNone(inventory.validate_child_output(index, text, identities))
            for count in (len(identities) - len(additions), len(identities) - 1, len(identities) + 1):
                changed = text.replace("Ran " + str(len(identities)), "Ran " + str(count))
                with self.subTest(index=index, count=count), self.assertRaisesRegex(ValueError, "incomplete unittest"):
                    inventory.validate_child_output(index, changed, identities)
            for changed in (text.replace("\nOK\n", "\nOK (skipped=1)\n"), text + text, ""):
                with self.subTest(index=index, output=changed), self.assertRaises(ValueError):
                    inventory.validate_child_output(index, changed, identities)

    def test_default_and_one_execute_original_order_on_the_calling_thread(self):
        for value in (None, "1"):
            visited = []
            caller = threading.get_ident()
            def execute(index):
                self.assertEqual(threading.get_ident(), caller)
                visited.append(index)
                return dict(index=index)
            with patch.dict(os.environ):
                os.environ.pop("BLITZ_CI_MUTATION_WORKERS", None)
                if value is not None:
                    os.environ["BLITZ_CI_MUTATION_WORKERS"] = value
                rows = inventory.execute_plan(execute)
            self.assertEqual(visited, list(range(14)))
            self.assertEqual([row["index"] for row in rows], visited)

    def test_two_real_child_slots_overlap_but_remaining_chain_is_serial_and_children_are_one(self):
        barrier = threading.Barrier(2, timeout=10)
        lock = threading.Lock()
        active, peak, remaining, seen = 0, 0, [], []
        def execute(index):
            nonlocal active, peak
            directory = self.output / str(index)
            directory.mkdir()
            env = inventory.child_environment(directory)
            with lock:
                active += 1
                peak = max(peak, active)
                seen.append(index)
                if index != inventory.PUBLIC_REPLAY:
                    remaining.append(index)
            if index in (0, inventory.PUBLIC_REPLAY):
                barrier.wait()
            child = subprocess.run([sys.executable, "-B", "-c",
                "import json,os,time; time.sleep(.02); print(json.dumps([os.environ['BLITZ_CI_MUTATION_WORKERS'],os.environ['TMPDIR']]))"],
                env=env, check=True, capture_output=True, text=True, timeout=10)
            worker, temporary = json.loads(child.stdout)
            self.assertEqual(worker, "1")
            self.assertEqual(temporary, str(directory / "tmp"))
            with lock:
                active -= 1
            return dict(index=index)
        with patch.dict(os.environ, BLITZ_CI_MUTATION_WORKERS="2"):
            rows = inventory.execute_plan(execute)
        self.assertEqual(peak, 2)
        self.assertCountEqual(seen, range(14))
        self.assertEqual(remaining, [i for i in range(14) if i != inventory.PUBLIC_REPLAY])
        self.assertEqual([row["index"] for row in rows], list(range(14)))

    def test_invalid_workers_and_nested_cli_are_rejected_before_any_suite_output(self):
        for value in ("", "0", "3", "-1", "02", "unknown"):
            with patch.dict(os.environ, BLITZ_CI_MUTATION_WORKERS=value), self.assertRaisesRegex(ValueError, "exactly 1 or 2"):
                inventory.execute_plan(lambda _: self.fail("invalid workers executed a suite"))
        for label, extra in (("bad-worker", {"BLITZ_CI_MUTATION_WORKERS": "3"}),
                             ("nested", {"BLITZ_CI_MUTATION_WORKERS": "2", "BLITZ_CI_INVENTORY_CHILD": "1"})):
            output = self.output / label
            env = dict(os.environ, **extra, PYTHONDONTWRITEBYTECODE="1")
            child = subprocess.run([sys.executable, "-B", str(Path(inventory.__file__)), "--output", str(output)],
                input=inventory.bindings(), env=env, capture_output=True, text=True, timeout=10)
            self.assertEqual(child.returncode, 1, child.stdout + child.stderr)
            self.assertIn("INVENTORY SUITES FAILED:", child.stderr)
            self.assertFalse(output.exists())

    def test_real_failure_drains_the_other_chain_before_returning(self):
        barrier = threading.Barrier(2, timeout=10)
        failed, release, finished, returned = (threading.Event() for _ in range(4))
        failures = []
        def execute(index):
            if index in (0, inventory.PUBLIC_REPLAY):
                barrier.wait()
            if index == inventory.PUBLIC_REPLAY:
                try:
                    subprocess.run([sys.executable, "-B", "-c", "raise SystemExit(23)"], check=True, timeout=10)
                except subprocess.CalledProcessError:
                    failed.set()
                    raise
            if index == 0:
                self.assertTrue(release.wait(10))
                finished.set()
            return dict(index=index)
        def invoke():
            try:
                inventory.execute_plan(execute)
            except BaseException as error:
                failures.append(error)
            finally:
                returned.set()
        with patch.dict(os.environ, BLITZ_CI_MUTATION_WORKERS="2"):
            thread = threading.Thread(target=invoke)
            thread.start()
            try:
                self.assertTrue(failed.wait(10))
                self.assertFalse(returned.wait(.05))
            finally:
                release.set()
                thread.join(15)
        self.assertFalse(thread.is_alive())
        self.assertTrue(finished.is_set())
        self.assertEqual(len(failures), 1)
        self.assertIsInstance(failures[0], subprocess.CalledProcessError)
        self.assertEqual(failures[0].returncode, 23)

    def test_real_synthetic_crash_empty_and_corrupt_output_never_qualify_a_suite(self):
        original_popen = subprocess.Popen
        probes = (
            ("nonzero", "print('not a passing suite'); raise SystemExit(23)", ValueError),
            ("crash", "import os,signal; os.kill(os.getpid(),signal.SIGTERM)", ValueError),
            ("empty", "pass", ValueError),
            ("corrupt", "print('Ran 4 tests in 0.01s\\n\\nOK (skipped=1)')", ValueError),
            ("bad-encoding", "import sys; sys.stdout.buffer.write(bytes([255]))", UnicodeError),
        )
        for label, code, error in probes:
            def spawn(argv, **kwargs):
                return original_popen([sys.executable, "-B", "-c", code], **kwargs)
            directory = self.output / label
            with self.subTest(probe=label), patch.object(inventory.subprocess, "Popen", side_effect=spawn), self.assertRaises(error):
                inventory.execute_child(0, directory)
            receipt = json.loads((directory / "result.json").read_text())
            self.assertEqual(receipt["status"], "failed")

    def test_missing_corrupt_substituted_receipt_or_log_is_never_success(self):
        for index, command in enumerate(inventory.COMMANDS):
            directory = self.output / str(index)
            directory.mkdir()
            identities, sources = inventory.source_inventory(index)
            text = "synthetic harness result, never used as actual suite qualification"
            (directory / "run.log").write_text(text)
            row = dict(index=index, argv=inventory.shlex.split(command), status="passed", exit=0,
                childWorkers="1", temporary=str(directory / "tmp"), caseIds=identities,
                sourceHashes=sources, logSha256=inventory.sha(text.encode()), campaign=None)
            (directory / "result.json").write_text(json.dumps(row))
        path = self.output / "0/result.json"
        original = path.read_bytes()
        with patch.object(inventory, "validate_child_output", return_value=None):
            self.assertEqual(len(inventory.read_results(self.output)), 14)
            path.unlink()
            with self.assertRaises(OSError):
                inventory.read_results(self.output)
            path.write_text("{")
            with self.assertRaises(ValueError):
                inventory.read_results(self.output)
            for field, value in (("exit", None), ("exit", -9), ("exit", "0"), ("argv", ["true"]),
                                 ("childWorkers", "2"), ("caseIds", ["substituted"]),
                                 ("temporary", str(self.output / "1/tmp"))):
                row = json.loads(original)
                row[field] = value
                path.write_text(json.dumps(row))
                with self.subTest(field=field, value=value), self.assertRaises(ValueError):
                    inventory.read_results(self.output)
            path.write_bytes(original)
            for index, additions in inventory.CURRENT_CASE_ADDITIONS.items():
                added_path = self.output / str(index) / "result.json"
                raw = added_path.read_bytes()
                row = json.loads(raw)
                row["caseIds"] = [name for name in row["caseIds"] if name not in additions]
                added_path.write_text(json.dumps(row))
                with self.subTest(omitted_additions=index), self.assertRaisesRegex(ValueError, "case receipt"):
                    inventory.read_results(self.output)
                added_path.write_bytes(raw)
            (self.output / "0/run.log").write_text("corrupt")
            with self.assertRaisesRegex(ValueError, "child log"):
                inventory.read_results(self.output)

    def test_new_output_rejects_existing_outside_unresolved_and_symlink_parents(self):
        self.assertEqual(inventory.new_output(self.output / "new"), self.output / "new")
        for value in (self.output, inventory.ROOT / "tests/new-unowned", self.output / "missing/../alias"):
            with self.subTest(output=str(value)), self.assertRaisesRegex(ValueError, "NEW ignored"):
                inventory.new_output(value)
        alias = self.output / "alias"
        alias.symlink_to(self.output, target_is_directory=True)
        try:
            with self.assertRaisesRegex(ValueError, "symlinked parents"):
                inventory.new_output(alias / "new")
        finally:
            alias.unlink()


class CampaignResultContractTests(unittest.TestCase):
    def setUp(self):
        original_root = inventory.ROOT
        self.output = original_root / ".squad/.scratch/ci-result-contract-revision" / ("contract-" + uuid.uuid4().hex)
        self.root = self.output / "workspace"
        self.root.mkdir(parents=True)
        self.addCleanup(shutil.rmtree, self.output)
        names = {name for i in range(len(inventory.COMMANDS)) for name in inventory.source_inventory(i)[1]}
        for name in names:
            path = self.root / name
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes((original_root / name).read_bytes())
        root_patch = patch.object(inventory, "ROOT", self.root)
        root_patch.start()
        self.addCleanup(root_patch.stop)
        self.popen = subprocess.Popen

    def campaign_path(self, index):
        argv = inventory.shlex.split(inventory.COMMANDS[index])
        return self.root / argv[argv.index("--output") + 1] / "result.json"

    def valid_campaign(self, index):
        """Synthetic contract evidence only; never actual suite qualification."""
        path = self.campaign_path(index)
        path.parent.mkdir(parents=True, exist_ok=True)
        controls = inventory.campaign_controls(index)
        report = dict(schema=inventory.REPORT_SCHEMAS[index], status="passed", runs=[])
        if index == 7:
            report["compiled_causal_controls_killed"] = len(controls)
        if index == 10:
            report["semanticControlsKilled"] = len(controls)
        for name, behavior in [("baseline", None), *controls, ("final-baseline", None)]:
            control = behavior is not None
            row = dict(name=name)
            if index in (7, 9, 12):
                directory = path.parent / name
                directory.mkdir(exist_ok=True)
                assembly = directory / {7: "HostLifecycle.Checks.dll", 9: "Dust.Checks.dll", 12: "Helmet.Checks.dll"}[index]
                assembly.write_bytes(b"synthetic contract fixture, not a compiled assembly")
                (directory / "build.log").write_bytes(b"")
                row["compile_exit" if index == 7 else "compile"] = 0
                row["compile_command" if index == 7 else "compileCommand"] = [
                    "dotnet", "synthetic/csc.dll", "-noconfig", "@" + str(directory / "compile.rsp")]
                row["assembly_sha256" if index == 7 else "assemblySha256"] = inventory.sha(assembly.read_bytes())
                if index == 7:
                    text = ("FAIL " + behavior + "\n" if control else "PASS synthetic baseline\n")
                    text += "RESULT cases=11 assertions=12 failures=" + str(int(control)) + "\n"
                    row.update(compile_diagnostics="", run_exit=int(control), cases=11, assertions=12,
                               failures=int(control), run_command=["dotnet", str(assembly)])
                    if control:
                        row.update(required_named_behavior_failure=behavior, killed_by_named_behavior=True)
                elif index == 9:
                    text = ("DUST ASSERTION: " + behavior + "\n" if control else "")
                    text += "DUST RESULT assertions=1 failures=" + str(int(control)) + "\n"
                    row["exit"] = int(control)
                else:
                    summary = dict(status="failed" if control else "passed", assertions=1)
                    if control:
                        summary["error"] = "HELMET ASSERTION: " + behavior
                    else:
                        summary.update(helperRows=[], syntheticInputs=True, originalAssetsRequired=False, nativeQualified=False)
                    text = json.dumps(summary) + "\n"
                    row.update(exit=int(control), summary=summary)
                if control and index != 7:
                    row["requiredNamedFailure"] = behavior
                row["output"] = text
                (directory / "run.log").write_text(text)
            else:
                source = path.parent / (name + ".mjs")
                source.write_text("// synthetic contract fixture, not production JS\n")
                total = 3 if index == 10 else 7
                text = ("AssertionError: " + behavior + "\n" if control else "")
                text += "".join("# " + key + " " + str(value) + "\n" for key, value in
                                (("tests", total), ("pass", total - int(control)), ("fail", int(control)),
                                 ("cancelled", 0), ("skipped", 0), ("todo", 0)))
                row["parsedExit" if index == 10 else "parseExit"] = 0
                row["exit" if index == 10 else "executionExit"] = int(control)
                row["sourceSha256" if index == 10 else "sha256"] = inventory.sha(source.read_bytes())
                if index == 10:
                    row["output"] = text
                else:
                    row["expectedFailedTest"] = behavior
                (path.parent / (name + ".log")).write_text(text)
            report["runs"].append(row)
        self.write_campaign(index, report)
        return report

    def write_campaign(self, index, report):
        path = self.campaign_path(index)
        path.write_text(json.dumps(report, indent=2) + "\n")
        return dict(path=str(path), sha256=inventory.sha(path.read_bytes()), schema=inventory.REPORT_SCHEMAS[index])

    def corruptions(self, index, original):
        probes = [("nonobject-report", None, {}), ("empty-report", {}, {})]
        def change(label, update):
            value = copy.deepcopy(original)
            update(value)
            probes.append((label, value, {}))
        for label, runs in (("missing-runs", None), ("empty-runs", []), ("null-entry", [None]),
                            ("empty-entry", [{}]), ("nonobject-entry", ["passed"])):
            change(label, lambda report, runs=runs: report.update(runs=runs))
        for status in ("failed", "unknown", None):
            change("top-status-" + str(status), lambda report, status=status: report.update(status=status))
        change("partial-campaign", lambda report: report.update(runs=report["runs"][:1]))
        change("missing-control", lambda report: report["runs"].pop(1))
        change("missing-baseline", lambda report: report["runs"].pop(0))
        change("missing-final-baseline", lambda report: report["runs"].pop())
        change("duplicate-control", lambda report: report["runs"][2].update(name=report["runs"][1]["name"]))
        change("substituted-control", lambda report: report["runs"][1].update(name="substituted-original-control"))
        change("substituted-baseline", lambda report: report["runs"][0].update(name="substituted-baseline"))
        change("reordered-controls", lambda report: report["runs"].reverse())
        exit_field = "run_exit" if index == 7 else "executionExit" if index == 13 else "exit"
        for value in (None, "1", True, 2, 0):
            change("control-exit-" + str(value), lambda report, value=value: report["runs"][1].update({exit_field: value}))
        change("failed-baseline", lambda report: report["runs"][0].update({exit_field: 1}))
        change("failed-final-baseline", lambda report: report["runs"][-1].update({exit_field: 1}))
        name = original["runs"][1]["name"]
        directory = self.campaign_path(index).parent / name
        if index in (7, 9, 12):
            field = "compile_exit" if index == 7 else "compile"
            change("missing-compile", lambda report: report["runs"][1].pop(field))
            for value in (1, "0", False):
                change("compile-" + str(value), lambda report, value=value: report["runs"][1].update({field: value}))
            for label, raw in (("compile-diagnostic", b"warning: not zero diagnostics\n"), ("missing-build-log", None)):
                probes.append((label, copy.deepcopy(original), {directory / "build.log": raw}))
            if index == 7:
                change("missing-diagnostics", lambda report: report["runs"][1].pop("compile_diagnostics"))
                change("reported-diagnostics", lambda report: report["runs"][1].update(compile_diagnostics="warning"))
                change("unknown-failures", lambda report: report["runs"][1].update(failures=None))
                change("missing-cases", lambda report: report["runs"][0].pop("cases"))
                change("not-killed", lambda report: report["runs"][1].update(killed_by_named_behavior=False))
        else:
            field = "parsedExit" if index == 10 else "parseExit"
            for value in (None, 1, "0", False):
                change("parse-" + str(value), lambda report, value=value: report["runs"][1].update({field: value}))
        field = {7: "required_named_behavior_failure", 9: "requiredNamedFailure",
                 12: "requiredNamedFailure", 13: "expectedFailedTest"}.get(index)
        if field:
            change("substituted-required-behavior", lambda report: report["runs"][1].update({field: "substituted"}))
        if index in (7, 10):
            field = "compiled_causal_controls_killed" if index == 7 else "semanticControlsKilled"
            change("missing-control-count", lambda report: report.pop(field))
            change("wrong-control-count", lambda report: report.update({field: 0}))
        log = self.campaign_path(index).parent / (name + ".log") if index in (10, 13) else directory / "run.log"
        probes.append(("missing-execution-log", copy.deepcopy(original), {log: None}))
        value = copy.deepcopy(original)
        if index == 12:
            value["runs"][1]["summary"]["error"] = "HELMET ASSERTION: substituted"
            text = json.dumps(value["runs"][1]["summary"]) + "\n"
            for status in ("passed", "unknown", None):
                changed = copy.deepcopy(original)
                changed["runs"][1]["summary"]["status"] = status
                output = json.dumps(changed["runs"][1]["summary"]) + "\n"
                changed["runs"][1]["output"] = output
                probes.append(("managed-status-" + str(status), changed, {log: output.encode()}))
        else:
            text = log.read_text().replace(inventory.campaign_controls(index)[0][1], "substituted")
        if index != 13:
            value["runs"][1]["output"] = text
        probes.append(("missing-actual-named-failure", value, {log: text.encode()}))
        if index in (10, 13):
            value = copy.deepcopy(original)
            text = log.read_text().replace("# skipped 0", "# skipped 1")
            if index == 10:
                value["runs"][1]["output"] = text
            probes.append(("skipped-JS-case", value, {log: text.encode()}))
        return probes

    def apply_corruption(self, index, report, files):
        self.valid_campaign(index)
        receipt = self.write_campaign(index, report)
        for path, raw in files.items():
            if raw is None:
                path.unlink()
            else:
                path.write_bytes(raw)
        return receipt

    def child_text(self, index):
        return ("synthetic zero-exit campaign child, not real qualification" if index in inventory.REPORT_SCHEMAS else
                "Ran " + str(len(inventory.source_inventory(index)[0])) + " tests in 0.01s\n\nOK\n")

    def spawn(self, argv, **kwargs):
        index = next(i for i, command in enumerate(inventory.COMMANDS) if inventory.shlex.split(command) == argv)
        return self.popen([sys.executable, "-B", "-c", "print(" + repr(self.child_text(index)) + ")"], **kwargs)

    def receipts(self):
        for index in inventory.REPORT_SCHEMAS:
            self.valid_campaign(index)
        output = self.root / ".squad/.scratch/receipts"
        for index, command in enumerate(inventory.COMMANDS):
            directory = output / str(index)
            directory.mkdir(parents=True)
            identities, sources = inventory.source_inventory(index)
            text = self.child_text(index)
            (directory / "run.log").write_text(text)
            row = dict(index=index, argv=inventory.shlex.split(command), status="passed", exit=0, childWorkers="1",
                       temporary=str(directory / "tmp"), caseIds=identities, sourceHashes=sources,
                       logSha256=inventory.sha(text.encode()), campaign=inventory.validate_child_output(index, text, identities))
            (directory / "result.json").write_text(json.dumps(row))
        return output

    def test_positive_complete_original_campaigns_validate_without_changing_evidence(self):
        output = self.receipts()
        before = {str(p): inventory.sha(p.read_bytes()) for p in self.root.rglob("*") if p.is_file()}
        self.assertEqual(sum(len(inventory.campaign_controls(i)) for i in inventory.REPORT_SCHEMAS), 55)
        self.assertEqual(len(inventory.read_results(output)), 14)
        self.assertEqual(before, {str(p): inventory.sha(p.read_bytes()) for p in self.root.rglob("*") if p.is_file()})
        path = self.root / "tests/impact-dust/run.py"
        path.write_text(path.read_text().replace('("child-order",', '("substituted-control",', 1))
        with self.assertRaisesRegex(ValueError, "original campaign control/behavior inventory changed"):
            inventory.campaign_controls(9)

    def test_zero_exit_child_rejects_all_corrupt_campaign_contracts(self):
        for index in inventory.REPORT_SCHEMAS:
            original = self.valid_campaign(index)
            for label, report, files in self.corruptions(index, original):
                self.apply_corruption(index, report, files)
                directory = self.output / ("child-" + str(index) + "-" + label)
                with self.subTest(index=index, fault=label), patch.object(inventory.subprocess, "Popen", side_effect=self.spawn):
                    with self.assertRaises((ValueError, OSError)):
                        inventory.execute_child(index, directory)
                    receipt = json.loads((directory / "result.json").read_text())
                    self.assertEqual((receipt["status"], receipt["exit"]), ("failed", 0))
                    self.assertTrue((directory / "run.log").read_text().strip())

    def test_final_receipts_reject_all_corrupt_campaign_contracts_even_with_updated_hashes(self):
        output = self.receipts()
        for index in inventory.REPORT_SCHEMAS:
            original = self.valid_campaign(index)
            path = output / str(index) / "result.json"
            row = json.loads(path.read_text())
            for label, report, files in self.corruptions(index, original):
                row["campaign"] = self.apply_corruption(index, report, files)
                path.write_text(json.dumps(row))
                with self.subTest(index=index, fault=label), self.assertRaises((ValueError, OSError)):
                    inventory.read_results(output)
            row["campaign"] = self.write_campaign(index, self.valid_campaign(index))
            path.write_text(json.dumps(row))
        self.assertEqual(len(inventory.read_results(output)), 14)

    def test_final_scheduler_revalidates_campaigns_after_all_zero_exit_children(self):
        execute_plan = inventory.execute_plan
        for index in inventory.REPORT_SCHEMAS:
            for campaign in inventory.REPORT_SCHEMAS:
                self.valid_campaign(campaign)
            output = self.root / ".squad/.scratch" / ("final-" + str(index))
            def execute_then_corrupt(execute):
                rows = execute_plan(execute)
                report = json.loads(self.campaign_path(index).read_text())
                report["runs"] = [None]
                campaign = self.write_campaign(index, report)
                path = output / str(index) / "result.json"
                row = json.loads(path.read_text())
                row["campaign"] = campaign
                path.write_text(json.dumps(row))
                return rows
            with self.subTest(index=index), patch.dict(os.environ, BLITZ_CI_MUTATION_WORKERS="2"), \
                    patch.object(inventory.subprocess, "Popen", side_effect=self.spawn), \
                    patch.object(inventory, "execute_plan", side_effect=execute_then_corrupt), \
                    patch.object(sys, "stdin", io.StringIO(inventory.bindings())), \
                    patch.object(sys, "argv", [inventory.__file__, "--output", str(output)]), \
                    contextlib.redirect_stdout(io.StringIO()), contextlib.redirect_stderr(io.StringIO()):
                self.assertEqual(inventory.main(), 1)
            report = json.loads((output / "result.json").read_text())
            self.assertEqual(report["status"], "failed")
            self.assertEqual(report["missing"], [])
            self.assertEqual(len(report["runs"]), 14)
            self.assertTrue(all(row["status"] == "passed" and row["exit"] == 0 for row in report["runs"]))
            self.assertIn("baseline/control/final-baseline inventory", report["error"])

    def test_corrupt_campaign_failure_drains_every_started_child(self):
        for index in inventory.REPORT_SCHEMAS:
            self.valid_campaign(index)
        self.write_campaign(7, dict(schema=inventory.REPORT_SCHEMAS[7], status="passed", runs=[{}]))
        failed, returned = threading.Event(), threading.Event()
        release = self.output / "release-replay"
        output = self.root / ".squad/.scratch/draining"
        output.mkdir(parents=True)
        failures, started = [], []
        def spawn(argv, **kwargs):
            index = next(i for i, command in enumerate(inventory.COMMANDS) if inventory.shlex.split(command) == argv)
            if index != inventory.PUBLIC_REPLAY:
                return self.spawn(argv, **kwargs)
            code = ("import time\nfrom pathlib import Path\n"
                    "deadline = time.monotonic() + 10\n"
                    "while not Path(" + repr(str(release)) + ").exists():\n"
                    "    assert time.monotonic() < deadline, 'replay was never released'\n"
                    "    time.sleep(.01)\nprint(" + repr(self.child_text(index)) + ")\n")
            return self.popen([sys.executable, "-B", "-c", code], **kwargs)
        def execute(index):
            started.append(index)
            try:
                return inventory.execute_child(index, output / str(index))
            except ValueError:
                if index == 7:
                    failed.set()
                raise
        def invoke():
            try:
                inventory.execute_plan(execute)
            except BaseException as error:
                failures.append(error)
            finally:
                returned.set()
        with patch.dict(os.environ, BLITZ_CI_MUTATION_WORKERS="2"), \
                patch.object(inventory.subprocess, "Popen", side_effect=spawn):
            thread = threading.Thread(target=invoke)
            thread.start()
            try:
                self.assertTrue(failed.wait(10), "exercise actual corrupt zero-exit campaign child")
                self.assertFalse(returned.wait(.05), "campaign rejection returned before replay drained")
            finally:
                release.write_text("release started replay child\n")
                thread.join(15)
        self.assertFalse(thread.is_alive())
        self.assertEqual(len(failures), 1)
        self.assertIsInstance(failures[0], ValueError)
        self.assertCountEqual(started, [0, 1, 2, 3, 4, 5, 6, 7])
        self.assertEqual(len(started), len(set(started)))
        receipts = [json.loads((output / str(i) / "result.json").read_text()) for i in started]
        self.assertTrue(all(row["exit"] == 0 and row["childWorkers"] == "1" for row in receipts))
        self.assertEqual(next(row for row in receipts if row["index"] == 7)["status"], "failed")
        self.assertEqual(next(row for row in receipts if row["index"] == inventory.PUBLIC_REPLAY)["status"], "passed")


if __name__ == "__main__":
    unittest.main()
