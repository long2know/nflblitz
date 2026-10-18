import copy
import json
from pathlib import Path
import re
import shutil
import unittest
from unittest import mock
import uuid

import export_portable_public as export


def captured_row():
    play = {"IsSpecified": False, "PlaySymbol": None, "Playbook": 0, "SlotIndex": -1,
            "PageIndex": -1, "Pool": None, "PoolIndex": -1}
    source = {"IsSpecified": False, "Action": 0, "Play": play, "TargetPlayerId": -1,
              "PatChoice": 0, "KickoffChoice": None, "AudibleSlot": -1, "OffenseFlipped": None}
    station = {"IsSpecified": True, "StationId": 0, "StickX": 3, "StickZ": 1,
               "Held": 4, "Pressed": 4, "Released": 0, "Command": copy.deepcopy(source)}
    return {"host": 0, "command": {"Move": {"X": 0, "Z": 0}, "Turbo": False, "Action": 0,
            "Play": 0, "TargetPlayerId": -1, "Source": source,
            "StationInputs": {"Stations": [station]}}}


class PortableExportGuards(unittest.TestCase):
    def test_only_constructor_station_metadata_is_removed_and_input_is_unchanged(self):
        row = captured_row()
        original = copy.deepcopy(row)
        projected = export.project_row(row, 0)
        expected = copy.deepcopy(row)
        del expected["command"]["StationInputs"]["Stations"][0]["IsSpecified"]
        self.assertEqual(projected, expected)
        self.assertEqual(row, original)
        self.assertFalse(projected["command"]["Source"]["IsSpecified"])
        self.assertFalse(projected["command"]["Source"]["Play"]["IsSpecified"])
        export.public.public_row(projected, 0)

    def test_nontrue_or_unknown_station_metadata_is_not_silently_normalized(self):
        for value in (False, 1, "true", None):
            row = captured_row()
            row["command"]["StationInputs"]["Stations"][0]["IsSpecified"] = value
            with self.subTest(value=value), self.assertRaises(ValueError):
                export.project_row(row, 0)
        row = captured_row()
        row["command"]["StationInputs"]["Stations"][0]["model"] = {}
        with self.assertRaisesRegex(ValueError, "field set differs"):
            export.project_row(row, 0)

    def test_observation_fields_count_order_and_missing_positive_are_rejected(self):
        row = captured_row()
        row["rng"] = []
        with self.assertRaises(ValueError):
            export.project_row(row, 0)
        with self.assertRaisesRegex(ValueError, "contiguous host"):
            export.project_row(captured_row(), 1)
        with self.assertRaisesRegex(ValueError, "all12000"):
            export.project_capture(export.public.encode([captured_row()]))
        with self.assertRaises(ValueError):
            export.require_candidate({"completedHosts": 11999}, [])

    def test_different_source_factory_or_missing_actual_pose_cannot_export(self):
        receipt = {"failure": None, "completedHosts": 12000, "SourceTick": 11400,
                   "gameplayCompatibility": "Portable",
                   "factory": "REAL SourceC2Composition.CreateM1Simulation",
                   "profile": "original-teams-contact281-c2-m1",
                   "sourceData": "b0c02e5516f4f932625e1f8ccb3ff0fd53ae222657f899eb8c9d0b0c2e74460a",
                   "positive": {"actor": 12, "InstanceId": 208, "firstTick": 5674,
                                "secondTick": 5675, "firstOrdinal": 0, "secondOrdinal": 1}}
        for key, value in (("factory", "proxy"), ("gameplayCompatibility", "StrictSource"),
                           ("SourceTick", 11399), ("failure", {"fault": "actual"})):
            with self.subTest(key=key), self.assertRaises(ValueError):
                export.require_candidate(dict(receipt, **{key: value}), [])
        with self.assertRaisesRegex(ValueError, "exact retained actual pose"):
            export.require_candidate(receipt, [])


class ExporterRevisionGuards(unittest.TestCase):
    def setUp(self):
        self.work = export.ROOT / ".squad/.scratch" / ("export-unit-" + uuid.uuid4().hex)
        self.work.mkdir(parents=True)
        self.paths = {name: self.work / (name + ".json") for name in ("capture", "receipt", "poses", "policy")}
        for path in self.paths.values():
            path.write_text("{}")
        self.output = self.work / "new-output"

    def tearDown(self):
        shutil.rmtree(self.work)

    def argv(self, output=None):
        args = []
        for name, path in self.paths.items():
            args += ["--" + name, str(path)]
        return args + ["--output", str(self.output if output is None else output)]

    def assert_no_writes(self, diagnostic, output=None, bypass_policy=False, bypass_candidate=False):
        with mock.patch.object(Path, "mkdir") as mkdir, \
                mock.patch.object(Path, "write_bytes") as write_bytes, \
                mock.patch.object(Path, "write_text") as write_text:
            with mock.patch.object(export, "validate_policy", wraps=export.validate_policy) as policy, \
                    mock.patch.object(export, "require_candidate", wraps=export.require_candidate) as candidate:
                if bypass_policy:
                    policy.side_effect = None
                    policy.return_value = ({}, export.AUTHOR_EXPORTER_SHA256)
                if bypass_candidate:
                    candidate.side_effect = None
                    candidate.return_value = None
                with self.assertRaisesRegex(ValueError, diagnostic):
                    export.main(self.argv(output))
            mkdir.assert_not_called()
            write_bytes.assert_not_called()
            write_text.assert_not_called()
        self.assertFalse(self.output.exists())

    def metadata(self):
        source = (export.ROOT / export.CONTROLLER).read_bytes()
        vector = re.findall(r'int\[\] rawVector = \{([^}]+)\}', source.decode("ascii"), re.S)
        raw_vector = [int(part.strip()) for part in vector[0].split(",")]
        harness = {name: export.sha((export.ROOT / name).read_bytes()) for name in (
            export.CONTROLLER, "tests/movement-callers/Canonical.cs", "tests/movement-callers/Program.cs",
            "tests/parity/FullKernelInputs.cs")}
        policy = {
            "name": "portable-cabinet-wide-pass-clearance", "version": "approved-portable-public-policy/v1",
            "sourceHandicapPolicy": export.HANDICAP, "sameApprovedConfiguration": export.CONFIGURATION,
            "actualFactory": "SourceC2Composition.CreateM1Simulation",
            "compatibility": "Portable explicitly; StrictSource defaults unchanged",
            "harnessSha256": harness,
            "frozenCodeSha256": dict(harness),
            "random": {"profile": "source-sound-smoke/raw-vector-v1", "rawVector": raw_vector, "seed": 1,
                       "globalUnfilteredRepeatedVectorAtEverySourceDraw": True,
                       "targetPRNGClaim": False, "evidence": "ExternalUnverified"},
        }
        receipt = {"name": policy["name"], "version": policy["version"], "rawVector": raw_vector,
                   "factory": "REAL " + policy["actualFactory"], "gameplayCompatibility": "Portable"}
        return policy, receipt

    def write_metadata(self, policy, receipt, bind=True):
        raw = (json.dumps(policy, indent=2) + "\n").encode()
        self.paths["policy"].write_bytes(raw)
        if bind:
            receipt["policySha256"] = export.sha(raw)
        self.paths["receipt"].write_text(json.dumps(receipt))

    def test_real_main_binds_policy_bytes_before_any_projection_or_write(self):
        policy, receipt = self.metadata()
        receipt["policySha256"] = "0" * 64
        self.write_metadata(policy, receipt, bind=False)
        self.assert_no_writes("policy bytes differ from retained receipt")

    def test_rebound_false_controller_and_provider_claims_fail_actual_main(self):
        for kind in ("controller", "provider", "factory"):
            policy, receipt = self.metadata()
            if kind == "controller":
                policy["harnessSha256"][export.CONTROLLER] = "0" * 64
                diagnostic = "controller source differs"
            elif kind == "provider":
                policy["random"]["profile"] = "substituted-provider"
                diagnostic = "provider profile differs"
            else:
                policy["actualFactory"] = "SourceC2Composition.FakeFactory"
                diagnostic = "factory/case differs"
            with self.subTest(kind=kind):
                self.write_metadata(policy, receipt)
                self.assert_no_writes(diagnostic)

    def test_structured_private_metadata_cannot_reach_recipe_via_actual_main(self):
        for field in ("sameApprovedConfiguration", "sourceHandicapPolicy", "name", "version", "actualFactory"):
            policy, receipt = self.metadata()
            policy[field] = {"model": [], "rng": [], "poses": []}
            self.write_metadata(policy, receipt)
            with self.subTest(field=field):
                self.assert_no_writes("scalar public text")
        policy, receipt = self.metadata()
        policy["random"]["profile"] = {"model": []}
        self.write_metadata(policy, receipt)
        self.assert_no_writes("scalar public text")

    def test_factory_source_evidence_is_bound_even_with_a_rebound_receipt(self):
        policy, receipt = self.metadata()
        policy["harnessSha256"]["tests/movement-callers/Canonical.cs"] = "0" * 64
        self.write_metadata(policy, receipt)
        self.assert_no_writes("retained factory harness source differs")

    def test_scalar_configuration_claims_and_seed_types_are_not_arbitrary(self):
        for field, value in (("sameApprovedConfiguration", "invented configuration"),
                             ("sourceHandicapPolicy", "invented handicap")):
            policy, receipt = self.metadata()
            policy[field] = value
            self.write_metadata(policy, receipt)
            with self.subTest(field=field):
                self.assert_no_writes("configuration/compatibility differs")
        policy, receipt = self.metadata()
        policy["random"]["seed"] = True
        self.write_metadata(policy, receipt)
        self.assert_no_writes("provider/vector/seed evidence differs")

    def test_constructor_invariants_share_shapes_and_cover_all_reconstructed_source_fields(self):
        changes = (
            ("StickX", 4), ("StickX", -4), ("StickZ", 4), ("StickZ", -4),
            ("Held", 8), ("Pressed", 8), ("Released", -1),
        )
        for name, value in changes:
            row = captured_row()
            row["command"]["StationInputs"]["Stations"][0][name] = value
            with self.subTest(field=name, value=value), self.assertRaises(ValueError):
                export.project_row(row, 0)
        for field, value in (("Action", 21), ("TargetPlayerId", 14), ("PatChoice", 3),
                             ("KickoffChoice", 3), ("AudibleSlot", -2)):
            for station in (True, False):
                row = captured_row()
                source = (row["command"]["StationInputs"]["Stations"][0]["Command"] if station
                          else row["command"]["Source"])
                source.update(IsSpecified=True)
                source[field] = value
                with self.subTest(field=field, station=station), self.assertRaises(ValueError):
                    export.project_row(row, 0)

    def test_specified_play_constructor_bounds_and_pool_pairing_are_preserved(self):
        for field, value in (("PlaySymbol", " "), ("Playbook", 2), ("SlotIndex", -2),
                             ("PageIndex", -2), ("PoolIndex", -2), ("Pool", 13), ("PoolIndex", 0)):
            row = captured_row()
            source = row["command"]["StationInputs"]["Stations"][0]["Command"]
            source["IsSpecified"] = True
            source["Play"].update(IsSpecified=True, PlaySymbol="synthetic_play")
            source["Play"][field] = value
            with self.subTest(field=field), self.assertRaises(ValueError):
                export.project_row(row, 0)
        row = captured_row()
        source = row["command"]["StationInputs"]["Stations"][0]["Command"]
        source.update(IsSpecified=True, Action=20, TargetPlayerId=13, PatChoice=2,
                      KickoffChoice=2, AudibleSlot=0)
        source["Play"].update(IsSpecified=True, PlaySymbol="synthetic_play", Playbook=1,
                              SlotIndex=0, PageIndex=0, Pool=12, PoolIndex=0)
        export.project_row(row, 0)

    def test_whole_capture_real_main_rejects_bad_controls_before_any_artifact_write(self):
        rows = []
        for host in range(12000):
            row = captured_row()
            row["host"] = host
            rows.append(row)
        rows[611]["command"]["StationInputs"]["Stations"][0]["Command"].update(IsSpecified=True, Action=21)
        raw = export.public.encode(rows)
        self.paths["capture"].write_bytes(raw)
        self.paths["receipt"].write_text(json.dumps({"publicInputSha256": export.sha(raw)}))
        self.paths["poses"].write_text("[]")
        self.assert_no_writes("Action must be0..20", bypass_policy=True, bypass_candidate=True)

    def test_real_main_rejects_lexical_and_parent_symlink_escapes_before_input_read(self):
        escaped = export.ROOT / ".squad/.scratch/../../tests/movement-callers/fixtures/export-unit-never-written"
        with mock.patch.object(Path, "read_bytes", side_effect=AssertionError("no input read")):
            self.assert_no_writes("own ignored output", output=escaped)
        target = self.work / "symlink-target"
        target.mkdir()
        link = self.work / "symlink-parent"
        link.symlink_to(target, target_is_directory=True)
        with mock.patch.object(Path, "read_bytes", side_effect=AssertionError("no input read")):
            self.assert_no_writes("symlinks", output=link / "never-written")
        self.assertFalse((target / "never-written").exists())
        self.assert_no_writes("NEW ignored", output=self.work)
        self.assert_no_writes("NEW ignored", output=export.ROOT / "tests/movement-callers/.artifacts/unowned-export")

    def test_original_producer_stamp_is_distinct_from_current_validator_source(self):
        self.assertEqual(export.AUTHOR_EXPORTER_SHA256,
                         "c876727da203e794859761ec7d2d92b235cf478648315fa84bf8c6a5e5e79476")
        self.assertIn('\"producerSourceSha256\": producer_sha', Path(export.__file__).read_text())
        self.assertIn('\"validator_source_sha256\": sha(Path(__file__).read_bytes())',
                      Path(export.__file__).read_text())


if __name__ == "__main__":
    unittest.main()
