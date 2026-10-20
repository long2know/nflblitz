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
                 "actual": {"State": {"Possession": 0}}}
        receipt.update(completedHosts=5846, SourceTick=5553, rawCursor=10142, rSkipFrames=0, positive=None, failure=fault)
        return summary, receipt

    def test_actual_current_source_and_unchanged_harness_are_pinned_not_injected(self):
        self.assertEqual((self.identity["source_count"], self.identity["harness_count"]), (77, 12))
        self.assertEqual(self.identity["source_manifest_sha256"], generations.CURRENT_SOURCE_SHA256)
        self.assertEqual(self.identity["harness_manifest_sha256"], generations.HARNESS_SHA256)
        for name in ("Assets/Scripts/Gameplay/GameSimulation.Source.cs", "tests/movement-callers/Canonical.cs",
                     "tests/movement-callers/PortablePublicPolicy.cs"):
            changed = dict(self.live)
            changed[name] += b"\n"
            with self.subTest(changed=name), self.assertRaisesRegex(ValueError, "payload"):
                generations.validate_snapshot(changed, 77, generations.CURRENT_SOURCE_SHA256)
        changed = dict(self.live)
        del changed["Assets/Scripts/Gameplay/GameSimulation.Source.cs"]
        with self.assertRaisesRegex(ValueError, "source payload"):
            generations.validate_snapshot(changed, 77, generations.CURRENT_SOURCE_SHA256)

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

    def test_current_rejection_is_only_the_exact_stale_side_fault_never_positive(self):
        summary, receipt = self.stale()
        self.assertEqual(generations.current_stale(2, summary, receipt, receipt["failure"], self.row),
                         "rejected-current-stale-playbook-side")
        for field, value in (
            ("completedHosts", 0), ("completedHosts", 12000), ("SourceTick", 5552),
            ("rawCursor", 0), ("rSkipFrames", 26), ("positive", self.receipt()["positive"]),
            ("replayFileSha256", "0" * 64),
        ):
            changed = copy.deepcopy(receipt)
            changed[field] = value
            with self.subTest(field=field), self.assertRaises(ValueError):
                generations.current_stale(2, summary, changed, changed["failure"], self.row)
        for field, value in (("status", "passed"), ("error", "System.InvalidOperationException: generic"),
                             ("assertions", 0), ("public_R_SKIP_gate_qualified", True),
                             ("complete_publication_qualification", True), ("mode", "m1-complete")):
            with self.subTest(summary=field), self.assertRaises(ValueError):
                generations.current_stale(2, dict(summary, **{field: value}), receipt, receipt["failure"], self.row)
        with self.assertRaises(ValueError):
            generations.current_stale(0, summary, receipt, receipt["failure"], self.row)

    def test_actual_first_fault_command_counts_and_source_validator_are_not_optional(self):
        summary, receipt = self.stale()
        for field, value in (("host", 0), ("before", 0), ("SourceTick", 0),
                             ("error", "System.ArgumentException: some unrelated argument"),
                             ("error", generations.SIDE_ERROR)):
            changed = copy.deepcopy(receipt)
            changed["failure"][field] = value
            with self.subTest(fault=field, value=value), self.assertRaises(ValueError):
                generations.current_stale(2, summary, changed, changed["failure"], self.row)
        for station, possession in ((1, 0), (0, 1)):
            changed = copy.deepcopy(receipt)
            changed["failure"]["command"]["StationInputs"]["Stations"][0]["StationId"] = station
            changed["failure"]["actual"]["State"]["Possession"] = possession
            with self.subTest(station=station, possession=possession), self.assertRaises(ValueError):
                generations.current_stale(2, summary, changed, changed["failure"], self.row)
        with self.assertRaisesRegex(ValueError, "first kernel fault"):
            generations.current_stale(2, summary, receipt, {}, self.row)

    def test_actual_cli_rejects_unknown_duplicate_or_shortcut_modes_before_output(self):
        command = [sys.executable, "-B", str(Path(generations.__file__)),
                   "--baseline-ref", generations.BASELINE_REF,
                   "--fixture-manifest", str(self.work / "fixture.json"),
                   "--sound-functions", str(self.work / "sound.safn"),
                   "--c2-m1-inputs", str(self.work / "m1"),
                   "--replay-commands", str(self.work / "old.jsonl"), "--output", str(self.work / "output")]
        for change, diagnostic in (
            (["--baseline-ref", generations.BASELINE_REF], "duplicate --baseline-ref"),
            (["--output", str(self.work / "another")], "duplicate --output"),
            (["--baseline-ref", "HEAD"], "invalid choice"),
            (["--single-public-attempt"], "unrecognized arguments"),
            (["--no-mutations"], "unrecognized arguments"),
            (["--source-compatibility", "strict"], "unrecognized arguments"),
        ):
            result = subprocess.run(command + change, capture_output=True, text=True)
            with self.subTest(change=change):
                self.assertEqual(result.returncode, 2)
                self.assertIn(diagnostic, result.stderr)
                self.assertFalse((self.work / "output").exists())

    def test_zero_duplicate_or_wrong_summary_never_becomes_generation_acceptance(self):
        for stdout in ("", "not a summary\n", '{"schema":"wrong"}\n{"schema":"wrong"}\n'):
            with self.subTest(stdout=stdout), self.assertRaisesRegex(ValueError, "one actual"):
                generations.summary_line(stdout)
        with self.assertRaisesRegex(ValueError, "summary"):
            generations.common_result(generations.summary_line('{"schema":"wrong"}\n'), self.receipt())


if __name__ == "__main__":
    unittest.main()
