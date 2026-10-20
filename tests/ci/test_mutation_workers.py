#!/usr/bin/env python3
"""Exercise actual overlap, exact inventories, hard bounds and drained failures."""
import os
import subprocess
import sys
import threading
import unittest
from unittest.mock import patch

from isolated_mutations import run_mutations


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


if __name__ == "__main__":
    unittest.main()
