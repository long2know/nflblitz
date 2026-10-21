"""Fail-closed source/payload/count/semantic guards for the actual generation helper."""
import copy
import hashlib
import io
import json
from pathlib import Path
import shutil
import subprocess
import sys
import tarfile
from types import SimpleNamespace
import unittest
from unittest.mock import patch
import uuid

import check_replay_generations as generations
import prepare_public_replay as replay

ARTIFACT_ROOT = replay.ROOT / "tests/movement-callers/.artifacts"


class RecordingGenerationTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.old_data = replay.load_fixture(replay.POSITIVE_FIXTURE, replay.POSITIVE_CASE)
        cls.row = replay.decode(cls.old_data.splitlines()[5845])
        cls.possession_data = replay.load_fixture(replay.SOURCE_CURRENT_FIXTURE, replay.SOURCE_CURRENT_CASE)
        cls.possession_row = replay.decode(cls.possession_data.splitlines()[2835])
        cls.live, cls.identity = generations.current_snapshot()

    def setUp(self):
        self.work = ARTIFACT_ROOT / ("generation-guard-" + uuid.uuid4().hex)
        self.work.mkdir(parents=True)

    def tearDown(self):
        shutil.rmtree(self.work)

    def summary(self):
        return {"schema": "source-movement-caller-checks/v1", "mode": "m1-complete",
                "gameplay_compatibility": "Portable", "real_input_fixture_sha256": generations.FIXTURE_SHA256,
                "actual_animation_library_manifest": generations.C2_ANIMATION_SHA,
                "whole_M1_or_full_gameplay_qualified": False, "native_or_browser_qualified": False,
                "assertions": 2270287, "status": "passed", "public_R_SKIP_gate_qualified": True}

    def receipt(self):
        return {"schema": "source-C2-M1-real-factory-public-replay/v1", "observed": False, "historical": False,
                "gameplayCompatibility": "Portable", "profile": "original-teams-contact281-c2-m1",
                "sourceData": generations.C2_M1_DATA_SHA, "factory": "REAL SourceC2Composition.CreateM1Simulation",
                "replayFileSha256": generations.OLD_PUBLIC_SHA256,
                "originalPublicVectorRetainedWithoutStateConditionedRegeneration": True,
                "sourceAudioDSPOrTargetRngOrFullGameAccepted": False, "completedHosts": 12000,
                "SourceTick": 11400, "rawCursor": 23771, "rSkipFrames": 26, "failure": None,
                "positive": {"actor": 12, "InstanceId": 208, "firstTick": 5674, "secondTick": 5699,
                             "firstOrdinal": 0, "secondOrdinal": 25}}

    def stale(self):
        summary, receipt = self.summary(), self.receipt()
        summary.update(status="error", error=generations.SIDE_ERROR + "\n   at Program.M1CompleteReplay(",
                       assertions=1126422)
        summary.pop("public_R_SKIP_gate_qualified")
        summary.pop("mode")
        command = copy.deepcopy(self.row["command"])
        command["StationInputs"]["Stations"][0]["IsSpecified"] = True
        fault = {"host": 5845, "before": 5552, "SourceTick": 5553, "command": command,
                 "error": generations.SIDE_ERROR + "\n   at Blitz.Gameplay.GameSimulation.SelectSourcePlay("
                          "\n   at Blitz.Gameplay.GameSimulation.ApplySourceStationMenuCommands(",
                 "actual": {"host": 5845, "rawCursor": 10142, "SourceTick": 5553,
                            "SourceRandomProcess": "game_proc", "SourceRandomActor": -1,
                            "SourceRandomProcessOrder": 0, "SourceRandomCallIndex": 0, "SourceFlags": 0,
                            "State": {"Tick": 5845, "PlayNumber": 17, "Phase": 0,
                                      "Possession": 0, "ControlledPlayerId": 3}}}
        receipt.update(completedHosts=5846, SourceTick=5553, rawCursor=10142, rSkipFrames=0, positive=None, failure=fault)
        return summary, receipt

    def test_actual_current_source_and_unchanged_harness_are_pinned_not_injected(self):
        self.assertEqual((self.identity["source_count"], self.identity["harness_count"]), (78, 12))
        self.assertEqual(self.identity["source_manifest_sha256"], generations.CURRENT_SOURCE_SHA256)
        self.assertEqual(self.identity["harness_manifest_sha256"], generations.HARNESS_SHA256)
        self.assertEqual(generations.TIMER_RECORDING_SOURCE_SHA256,
                         replay.SOURCE_TIMER_EXPECTED_RECIPE["source_manifest_sha256"])
        self.assertNotEqual(generations.CURRENT_SOURCE_SHA256, generations.TIMER_RECORDING_SOURCE_SHA256)
        for name in ("Assets/Scripts/Gameplay/GameSimulation.Source.cs", "tests/movement-callers/Canonical.cs",
                     "tests/movement-callers/PortablePublicPolicy.cs"):
            changed = dict(self.live)
            changed[name] += b"\n"
            with self.subTest(changed=name), self.assertRaisesRegex(ValueError, "payload"):
                generations.validate_snapshot(changed, 78, generations.CURRENT_SOURCE_SHA256)
        changed = dict(self.live)
        del changed["Assets/Scripts/Gameplay/GameSimulation.Source.cs"]
        with self.assertRaisesRegex(ValueError, "source payload"):
            generations.validate_snapshot(changed, 78, generations.CURRENT_SOURCE_SHA256)
        with self.assertRaisesRegex(ValueError, "source payload"):
            generations.validate_snapshot(self.live, 77, generations.PREVIOUS_SOURCE_SHA256)

    def test_both_archived_sources_are_exact_git_authenticated_named_generations(self):
        for ref, previous, count, digest in (
            (generations.BASELINE_REF, False, 76, generations.BASELINE_SOURCE_SHA256),
            (generations.PREVIOUS_REF, True, 77, generations.PREVIOUS_SOURCE_SHA256),
        ):
            files, identity = generations.historical_snapshot(ref, previous)
            with self.subTest(ref=ref):
                self.assertEqual(identity["source_count"], count)
                self.assertEqual(identity["source_manifest_sha256"], digest)
                self.assertEqual(identity["commit"], ref)
                self.assertEqual(identity["harness_manifest_sha256"], generations.HARNESS_SHA256)
                self.assertEqual(files["tests/movement-callers/PortablePublicPolicy.cs"],
                                 self.live["tests/movement-callers/PortablePublicPolicy.cs"])

    def test_wrong_commit_or_tree_rejected_before_archive_or_build(self):
        for ref, tree in (("HEAD", generations.BASELINE_TREE), (generations.BASELINE_REF, "0" * 40)):
            with self.subTest(ref=ref, tree=tree), self.assertRaisesRegex(ValueError, "commit/tree"):
                generations.validate_ref(ref, tree)
        with patch.object(generations, "git") as git:
            with self.assertRaisesRegex(ValueError, "commit/tree"):
                generations.historical_snapshot("HEAD")
            git.assert_not_called()
        with patch.object(generations, "git", side_effect=[b"", b"0" * 40]) as git:
            with self.assertRaisesRegex(ValueError, "commit/tree"):
                generations.historical_snapshot(generations.BASELINE_REF)
            self.assertEqual(git.call_count, 2)
        for ref, tree in ((generations.BASELINE_REF, generations.PREVIOUS_TREE),
                          (generations.PREVIOUS_REF, generations.BASELINE_TREE),
                          ("HEAD", generations.PREVIOUS_TREE)):
            with self.subTest(previous=ref), self.assertRaisesRegex(ValueError, "commit/tree"):
                generations.validate_ref(ref, tree, previous=True)

    def test_missing_commit_fetch_is_only_the_exact_pin_never_branch_tip_or_checkout(self):
        missing = subprocess.CalledProcessError(1, ["git", "cat-file"])
        with patch.object(generations, "git", side_effect=[
                missing, b"", generations.BASELINE_TREE.encode(), b""]) as git:
            with self.assertRaisesRegex(ValueError, "archive selection"):
                generations.historical_snapshot(generations.BASELINE_REF)
            self.assertEqual(git.call_args_list[1].args,
                             ("fetch", "--no-tags", "--depth=1", "origin", generations.BASELINE_REF))
            self.assertEqual(git.call_count, 4)

    def archive(self, members):
        target = io.BytesIO()
        with tarfile.open(fileobj=target, mode="w") as archive:
            for name, data, kind in members:
                member = tarfile.TarInfo(name)
                member.type = kind
                member.size = len(data) if kind == tarfile.REGTYPE else 0
                archive.addfile(member, io.BytesIO(data) if member.size else None)
        return target.getvalue()

    def test_archive_wrong_payload_hash_missing_extra_duplicate_symlink_and_traversal_fail(self):
        name, data = "Assets/Scripts/Gameplay/test.cs", b"unchanged source bytes"
        blob = hashlib.sha1(b"blob " + str(len(data)).encode() + b"\0" + data).hexdigest()
        members = [(name, data, tarfile.REGTYPE)]
        self.assertEqual(generations.archive_payload(self.archive(members), {name: blob}), {name: data})
        for changed in (
            [(name, data + b"\n", tarfile.REGTYPE)], [], members * 2,
            members + [("unselected.cs", b"extra", tarfile.REGTYPE)],
            [(name, b"", tarfile.SYMTYPE)],
            [("../escape.cs", data, tarfile.REGTYPE)], [("/absolute.cs", data, tarfile.REGTYPE)],
        ):
            with self.subTest(members=[item[0] for item in changed]), self.assertRaises(ValueError):
                generations.archive_payload(self.archive(changed), {name: blob})

    def test_actual_old_input_requires_exact_bytes_hash_and_bounded_regular_path(self):
        path = self.work / "old.jsonl"
        path.write_bytes(self.old_data)
        self.assertEqual(generations.old_commands(path)[5845], self.row)
        path.write_bytes(self.old_data.replace(b'"X":0', b'"X":1', 1))
        with self.assertRaisesRegex(ValueError, "command identity SHA256"):
            generations.old_commands(path)
        path.write_bytes(b"")
        with self.assertRaisesRegex(ValueError, "byte count"):
            generations.old_commands(path)
        link = self.work / "link.jsonl"
        link.symlink_to(path)
        with self.assertRaisesRegex(ValueError, "symlinks"):
            generations.old_commands(link)

    def test_historical_positive_is_separately_classified_and_requires_full_nonzero_exact_counts(self):
        summary, receipt = self.summary(), self.receipt()
        self.assertEqual(generations.historical_positive(0, summary, receipt), "positive-historical-main38-only")
        for field, value in (
            ("completedHosts", 0), ("completedHosts", 11999), ("SourceTick", 0), ("SourceTick", 11399),
            ("rSkipFrames", 0), ("rSkipFrames", 25), ("rawCursor", 0),
            ("replayFileSha256", replay.RECORDINGS[replay.SOURCE_CURRENT_CASE]["public_sha256"]),
            ("sourceData", "0" * 64), ("failure", {"error": "generic"}), ("positive", None),
        ):
            changed = copy.deepcopy(receipt)
            changed[field] = value
            with self.subTest(field=field, value=value), self.assertRaises((ValueError, RuntimeError)):
                generations.historical_positive(0, summary, changed)
        for field, value in (("assertions", 0), ("status", "error"), ("public_R_SKIP_gate_qualified", False),
                             ("mode", "controlled")):
            changed = dict(summary, **{field: value})
            with self.subTest(field=field), self.assertRaises((ValueError, RuntimeError)):
                generations.historical_positive(0, changed, receipt)
        with self.assertRaises((ValueError, RuntimeError)):
            generations.historical_positive(2, summary, receipt)

    def test_pinned_main39_rejection_is_only_the_exact_stale_side_fault_never_positive(self):
        summary, receipt = self.stale()
        self.assertEqual(generations.main39_stale(2, summary, receipt, receipt["failure"], self.row),
                         "rejected-pinned-main39-old-stale-playbook-side")
        for field, value in (
            ("completedHosts", 0), ("completedHosts", 12000), ("SourceTick", 5552),
            ("rawCursor", 0), ("rSkipFrames", 26), ("positive", self.receipt()["positive"]),
            ("replayFileSha256", "0" * 64),
        ):
            changed = copy.deepcopy(receipt)
            changed[field] = value
            with self.subTest(field=field), self.assertRaises(ValueError):
                generations.main39_stale(2, summary, changed, changed["failure"], self.row)
        for field, value in (("status", "passed"), ("error", "System.InvalidOperationException: generic"),
                             ("assertions", 0), ("public_R_SKIP_gate_qualified", True),
                             ("complete_publication_qualification", True), ("mode", "m1-complete")):
            with self.subTest(summary=field), self.assertRaises(ValueError):
                generations.main39_stale(2, dict(summary, **{field: value}), receipt, receipt["failure"], self.row)
        with self.assertRaises(ValueError):
            generations.main39_stale(0, summary, receipt, receipt["failure"], self.row)

    def test_actual_first_fault_command_counts_and_source_validator_are_not_optional(self):
        summary, receipt = self.stale()
        for field, value in (("host", 0), ("before", 0), ("SourceTick", 0),
                             ("error", "System.ArgumentException: some unrelated argument"),
                             ("error", generations.SIDE_ERROR)):
            changed = copy.deepcopy(receipt)
            changed["failure"][field] = value
            with self.subTest(fault=field, value=value), self.assertRaises(ValueError):
                generations.main39_stale(2, summary, changed, changed["failure"], self.row)
        for station, possession in ((1, 0), (0, 1)):
            changed = copy.deepcopy(receipt)
            changed["failure"]["command"]["StationInputs"]["Stations"][0]["StationId"] = station
            changed["failure"]["actual"]["State"]["Possession"] = possession
            with self.subTest(station=station, possession=possession), self.assertRaises(ValueError):
                generations.main39_stale(2, summary, changed, changed["failure"], self.row)
        with self.assertRaisesRegex(ValueError, "first kernel fault"):
            generations.main39_stale(2, summary, receipt, {}, self.row)

    def candidate_stale(self):
        summary, receipt = self.stale()
        command = copy.deepcopy(self.possession_row["command"])
        command["StationInputs"]["Stations"][0]["IsSpecified"] = True
        fault = receipt["failure"]
        fault.update(host=2835, before=2693, SourceTick=2694, command=command)
        fault["actual"].update(host=2835, rawCursor=5792, SourceTick=2694, SourceRandomProcessOrder=1)
        fault["actual"]["State"].update(Tick=2835, PlayNumber=8, Possession=1)
        receipt.update(completedHosts=2836, SourceTick=2694, rawCursor=5792,
                       replayFileSha256=generations.POSSESSION_PUBLIC_SHA256)
        return summary, receipt

    def test_actual_current_stale_is_exactly2694_not_the_main39_stale5553(self):
        summary, receipt = self.candidate_stale()
        self.assertEqual(generations.current_stale(2, summary, receipt, receipt["failure"], self.possession_row),
                         "rejected-current-possession-stale-playbook-side")
        for field, value in (("completedHosts", 5846), ("SourceTick", 5553), ("rawCursor", 10142),
                             ("rSkipFrames", 1), ("replayFileSha256", generations.OLD_PUBLIC_SHA256),
                             ("positive", self.receipt()["positive"])):
            changed = copy.deepcopy(receipt)
            changed[field] = value
            with self.subTest(field=field), self.assertRaises(ValueError):
                generations.current_stale(2, summary, changed, changed["failure"], self.possession_row)
        for code in (0, 1, -9):
            with self.subTest(code=code), self.assertRaises(ValueError):
                generations.current_stale(code, summary, receipt, receipt["failure"], self.possession_row)
        for classify, row in ((generations.main39_stale, self.row),
                              (generations.current_stale, self.row)):
            with self.assertRaises(ValueError):
                classify(2, summary, receipt, receipt["failure"], row)
        old_summary, old_receipt = self.stale()
        with self.assertRaises(ValueError):
            generations.current_stale(2, old_summary, old_receipt, old_receipt["failure"], self.possession_row)

    def test_current_and_main39_stale_phase_process_side_and_actual_fault_are_required(self):
        for factory, classify, row in ((self.stale, generations.main39_stale, self.row),
                                       (self.candidate_stale, generations.current_stale, self.possession_row)):
            summary, receipt = factory()
            for field, value in (("Phase", 1), ("Tick", 0), ("PlayNumber", 0),
                                 ("Possession", 2), ("ControlledPlayerId", -1)):
                changed = copy.deepcopy(receipt)
                changed["failure"]["actual"]["State"][field] = value
                with self.subTest(classifier=classify.__name__, field=field), self.assertRaises(ValueError):
                    classify(2, summary, changed, changed["failure"], row)
            for field, value in (("SourceRandomProcess", "generic"), ("SourceRandomProcessOrder", 99),
                                 ("SourceRandomActor", 0), ("SourceRandomCallIndex", 1),
                                 ("SourceFlags", 1), ("rawCursor", 0), ("host", 0), ("SourceTick", 0)):
                changed = copy.deepcopy(receipt)
                changed["failure"]["actual"][field] = value
                with self.subTest(classifier=classify.__name__, field=field), self.assertRaises(ValueError):
                    classify(2, summary, changed, changed["failure"], row)
            with self.assertRaises(ValueError):
                classify(2, summary, receipt, {}, row)
            with self.assertRaises(ValueError):
                classify(2, dict(summary, error="System.Exception: generic"), receipt, receipt["failure"], row)
            with self.assertRaises(ValueError):
                classify(2, dict(summary, error=None), receipt, receipt["failure"], row)
            for field in ("actual", "State"):
                changed = copy.deepcopy(receipt)
                if field == "actual":
                    changed["failure"]["actual"] = None
                else:
                    changed["failure"]["actual"]["State"] = None
                with self.subTest(missing_context=field), self.assertRaises(ValueError):
                    classify(2, summary, changed, changed["failure"], row)

    def test_pinned_main39_positive_is_neither_historical_nor_current_full_qualification(self):
        summary, receipt = self.summary(), self.receipt()
        receipt.update(rawCursor=22504, rSkipFrames=164, replayFileSha256=generations.POSSESSION_PUBLIC_SHA256,
                       positive={"actor": 12, "InstanceId": 201, "firstTick": 6738, "secondTick": 6824,
                                 "firstOrdinal": 0, "secondOrdinal": 16})
        self.assertEqual(generations.main39_positive(0, summary, receipt), "positive-pinned-main39-possession-only")
        for field, value in (("completedHosts", 0), ("SourceTick", 11399), ("rawCursor", 23771),
                             ("rSkipFrames", 26), ("positive", self.receipt()["positive"]),
                             ("replayFileSha256", generations.OLD_PUBLIC_SHA256),
                             ("failure", {"error": "generic"})):
            changed = copy.deepcopy(receipt)
            changed[field] = value
            with self.subTest(field=field), self.assertRaises((ValueError, RuntimeError)):
                generations.main39_positive(0, summary, changed)
        with self.assertRaises((ValueError, RuntimeError)):
            generations.historical_positive(0, summary, receipt)
        with self.assertRaises((ValueError, RuntimeError)):
            generations.main39_positive(2, summary, receipt)

    def test_actual_cli_rejects_unknown_duplicate_or_shortcut_modes_before_output(self):
        self.assert_partition_cli_rejects_unknown_duplicate_and_unavailable_modes()
        command = [sys.executable, "-B", str(Path(generations.__file__)),
                   "--baseline-ref", generations.BASELINE_REF,
                   "--previous-ref", generations.PREVIOUS_REF,
                   "--fixture-manifest", str(self.work / "fixture.json"),
                   "--sound-functions", str(self.work / "sound.safn"),
                   "--c2-m1-inputs", str(self.work / "m1"),
                   "--replay-commands", str(self.work / "old.jsonl"),
                   "--possession-replay-commands", str(self.work / "possession.jsonl"),
                   "--output", str(self.work / "output")]
        for change, diagnostic in (
            (["--baseline-ref", generations.BASELINE_REF], "duplicate --baseline-ref"),
            (["--output", str(self.work / "another")], "duplicate --output"),
            (["--baseline-ref", "HEAD"], "invalid choice"),
            (["--previous-ref", "HEAD"], "invalid choice"),
            (["--previous-ref", generations.PREVIOUS_REF], "duplicate --previous-ref"),
            (["--possession-replay-commands", str(self.work / "another.jsonl")],
             "duplicate --possession-replay-commands"),
            *((["--" + name, str(self.work / "duplicate")], "duplicate --" + name)
              for name in ("fixture-manifest", "sound-functions", "c2-m1-inputs", "replay-commands")),
            (["--single-public-attempt"], "unrecognized arguments"),
            (["--no-mutations"], "unrecognized arguments"),
            (["--source-compatibility", "strict"], "unrecognized arguments"),
        ):
            result = subprocess.run(command + change, capture_output=True, text=True)
            with self.subTest(change=change):
                self.assertEqual(result.returncode, 2)
                self.assertIn(diagnostic, result.stderr)
                self.assertFalse((self.work / "output").exists())
        for option in ("--baseline-ref", "--previous-ref", "--possession-replay-commands"):
            changed = list(command)
            index = changed.index(option)
            del changed[index:index + 2]
            result = subprocess.run(changed, capture_output=True, text=True)
            with self.subTest(missing=option):
                self.assertEqual(result.returncode, 2)
                self.assertIn("required", result.stderr)
                self.assertFalse((self.work / "output").exists())

    def test_wrong_generation_input_selection_is_not_reinterpreted_or_regenerated(self):
        old, possession = self.work / "old.jsonl", self.work / "possession.jsonl"
        old.write_bytes(self.old_data)
        possession.write_bytes(self.possession_data)
        self.assertEqual(generations.old_commands(possession, replay.SOURCE_CURRENT_CASE)[2835], self.possession_row)
        for path, case in ((old, replay.SOURCE_CURRENT_CASE), (possession, replay.POSITIVE_CASE),
                           (possession, replay.SOURCE_TIMER_CASE)):
            with self.subTest(case=case), self.assertRaises(ValueError):
                generations.old_commands(path, case)

    def test_actual_execution_rejects_noop_build_crash_and_missing_replay_artifact(self):
        args = SimpleNamespace(output=self.work, fixture_manifest=self.work / "fixture.json",
                               sound_functions=self.work / "sound.safn", c2_m1_inputs=self.work / "m1",
                               replay_commands=self.work / "old.jsonl",
                               possession_replay_commands=self.work / "possession.jsonl")
        builds = "0 Warning(s)\n0 Error(s)\n"
        for label, returns, error in (
            ("compile-failure", [subprocess.CompletedProcess([], 1, "bad compile", "")], "zero-diagnostic"),
            ("empty-replay", [subprocess.CompletedProcess([], 0, builds, ""),
                              subprocess.CompletedProcess([], 0, "", "")], "one actual"),
            ("no-artifact", [subprocess.CompletedProcess([], 0, builds, ""),
                             subprocess.CompletedProcess([], 0, json.dumps(self.summary()) + "\n", "")], None),
        ):
            args.output = self.work / label
            args.output.mkdir()
            with patch.object(generations.subprocess, "run", side_effect=returns), self.subTest(label=label):
                with self.assertRaises((ValueError, OSError)) as failure:
                    generations.execute("current", self.live, args, [self.row] * 5846,
                                        [self.possession_row] * 2836)
                if error:
                    self.assertIn(error, str(failure.exception))

    def test_source_ref_substitution_cannot_execute_or_create_a_named_current_probe(self):
        args = SimpleNamespace(output=self.work)
        previous, _ = generations.historical_snapshot(generations.PREVIOUS_REF, previous=True)
        with patch.object(generations.subprocess, "run") as run:
            with self.assertRaisesRegex(ValueError, "source payload"):
                generations.execute("current", previous, args, [], [])
            with self.assertRaisesRegex(ValueError, "source payload"):
                generations.execute("main39", self.live, args, [], [])
            with self.assertRaisesRegex(ValueError, "exact named"):
                generations.execute("HEAD", self.live, args, [], [])
            run.assert_not_called()
        self.assertEqual(list(self.work.iterdir()), [])

    def test_zero_duplicate_or_wrong_summary_never_becomes_generation_acceptance(self):
        self.assert_closed_partitions_are_disjoint_complete_and_default_still_runs_all_five()
        self.assert_every_partition_requires_every_actual_compiled_classified_probe()
        for stdout in ("", "not a summary\n", '{"schema":"wrong"}\n{"schema":"wrong"}\n'):
            with self.subTest(stdout=stdout), self.assertRaisesRegex(ValueError, "one actual"):
                generations.summary_line(stdout)
        with self.assertRaisesRegex(ValueError, "summary"):
            generations.common_result(generations.summary_line('{"schema":"wrong"}\n'), self.receipt())

    def assert_closed_partitions_are_disjoint_complete_and_default_still_runs_all_five(self):
        self.assertEqual(generations.probe_inventory(), (
            "main38-old-positive", "main39-old-stale", "main39-possession-positive",
            "current-possession-stale", "u2-current-possession-stale"))
        archived = generations.probe_inventory("archived-four")
        current = generations.probe_inventory("u2-current-fifth")
        self.assertFalse(set(archived).intersection(current))
        self.assertEqual(archived + current, generations.probe_inventory("all-five"))
        self.assertIsNone(generations.argument_parser().get_default("partition"))
        for value in ("", "old", "current", "all", None):
            with self.subTest(partition=value), self.assertRaisesRegex(ValueError, "closed"):
                generations.probe_inventory(value)

    def assert_every_partition_requires_every_actual_compiled_classified_probe(self):
        probes = {}
        for name in generations.ALL_PROBES:
            positive = "positive" in name
            probes[name] = dict(source_generation=generations.PROBE_SOURCES[name],
                compile_zero_diagnostics=True, exit_code=0 if positive else 2,
                classification=("positive-historical-main38-only" if name == "main38-old-positive" else
                    "positive-pinned-main39-possession-only" if positive else
                    "rejected-pinned-main39-old-stale-playbook-side" if name == "main39-old-stale" else
                    "rejected-current-possession-stale-playbook-side"))
        for partition in ("all-five", "archived-four", "u2-current-fifth"):
            selected = {name: probes[name] for name in generations.probe_inventory(partition)}
            generations.validate_probe_inventory(partition, selected)
            for name in selected:
                missing = dict(selected)
                del missing[name]
                with self.subTest(partition=partition, missing=name), self.assertRaises(ValueError):
                    generations.validate_probe_inventory(partition, missing)
                for field, value in (("source_generation", "unavailable"), ("compile_zero_diagnostics", False),
                                     ("exit_code", 1), ("exit_code", True), ("classification", "skipped")):
                    changed = copy.deepcopy(selected)
                    changed[name][field] = value
                    with self.subTest(probe=name, field=field), self.assertRaises(ValueError):
                        generations.validate_probe_inventory(partition, changed)
            with self.assertRaises(ValueError):
                generations.validate_probe_inventory(partition, dict(selected, unavailable={}))

    def assert_partition_cli_rejects_unknown_duplicate_and_unavailable_modes(self):
        command = [sys.executable, "-B", generations.__file__,
                   "--baseline-ref", generations.BASELINE_REF, "--previous-ref", generations.PREVIOUS_REF]
        for name in ("fixture-manifest", "sound-functions", "c2-m1-inputs", "replay-commands",
                     "possession-replay-commands", "output"):
            command.extend(("--" + name, str(self.work / name)))
        for change, message in ((["--partition", "unavailable"], "invalid choice"),
                (["--partition", "archived-four", "--partition", "u2-current-fifth"], "duplicate --partition")):
            process = subprocess.run(command + change, capture_output=True, text=True)
            self.assertEqual(process.returncode, 2)
            self.assertIn(message, process.stderr)
            self.assertFalse((self.work / "output").exists())


if __name__ == "__main__":
    unittest.main()
