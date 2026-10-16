"""Coupled CLI/profile/runtime-export publication gates; no Core/Unity/browser execution."""
import contextlib
import copy
import io
import json
from pathlib import Path
import shutil
import unittest
from unittest import mock
import uuid
import xml.etree.ElementTree as ET

import run


class ProfileTests(unittest.TestCase):
    def test_sdk_profile_is_explicit_and_does_not_accept_a_unity_reference(self):
        args = run.parse_arguments(["--sdk-netstandard21"])
        self.assertTrue(args.sdk_netstandard21)
        self.assertIsNone(args.unity_reference)
        for arguments in ([], ["--sdk-netstandard21", "--unity-reference", "netstandard.dll"]):
            with self.subTest(arguments=arguments), contextlib.redirect_stderr(io.StringIO()):
                with self.assertRaises(SystemExit) as error:
                    run.parse_arguments(arguments)
                self.assertEqual(error.exception.code, 2)

    def test_unity_profile_remains_explicit(self):
        args = run.parse_arguments(["--unity-reference", "actual-Unity/2.1.0/netstandard.dll"])
        self.assertFalse(args.sdk_netstandard21)
        self.assertEqual(args.unity_reference, Path("actual-Unity/2.1.0/netstandard.dll"))

    def test_sdk_project_uses_actual_netstandard21_target_and_shared_source_inventory(self):
        project = ET.parse(run.SUITE / "MovementSdkProfile.csproj").getroot()
        properties = {element.tag: element.text for group in project.findall("PropertyGroup") for element in group}
        self.assertEqual(properties["TargetFramework"], "netstandard2.1")
        self.assertEqual(properties["LangVersion"], "7.3")
        self.assertEqual(properties["TreatWarningsAsErrors"], "true")
        for name in ("MovementSdkProfile.csproj", "MovementChecks.csproj"):
            parsed = ET.parse(run.SUITE / name).getroot()
            self.assertIn("MovementSources.props", [node.attrib["Project"] for node in parsed.findall("Import")])
        includes = [node.attrib["Include"] for node in ET.parse(run.SUITE / "MovementSources.props").iter("Compile")]
        self.assertIn("$(MovementSourceRoot)Assets/Scripts/Contracts/**/*.cs", includes)
        self.assertEqual(
            {path for path in run.paths() if path.is_relative_to(run.ROOT / "Assets/Scripts/Contracts")},
            set((run.ROOT / "Assets/Scripts/Contracts").rglob("*.cs")),
        )

    def test_contract_snapshot_captures_future_nested_contracts(self):
        root = run.ARTIFACTS / ("contract-inventory-" + uuid.uuid4().hex)
        try:
            path = root / "Assets/Scripts/Contracts/NewPresentation/Extra.cs"
            path.parent.mkdir(parents=True)
            path.write_text("// Inventory fixture only; never compiled as a substitute contract.\n")
            with mock.patch.object(run, "ROOT", root):
                self.assertIn(path, run.paths())
        finally:
            shutil.rmtree(root)

    def test_sdk_failure_is_visible_without_unity_fallback(self):
        root = run.ARTIFACTS / ("sdk-failure-" + uuid.uuid4().hex)
        try:
            with mock.patch.object(run, "ARTIFACTS", root), \
                    mock.patch.object(run.subprocess, "run", return_value=mock.Mock(returncode=1, stdout="", stderr="missing SDK reference pack")), \
                    mock.patch.object(run, "unity_compile") as unity:
                with self.assertRaisesRegex(RuntimeError, "no Unity fallback"):
                    run.sdk_compile({}, {}, root / "source")
                unity.assert_not_called()
        finally:
            if root.exists():
                shutil.rmtree(root)

    def test_real_sdk_receipt_is_never_labeled_unity_validated(self):
        path = run.ARTIFACTS / "result-sdk-netstandard21.json"
        self.assertTrue(path.is_file(), "Run the explicit SDK CI command before this focused receipt gate.")
        receipt = json.loads(path.read_text())
        self.assertEqual(receipt["status"], "passed")
        self.assertEqual(receipt["validation_profile"], "sdk-netstandard21")
        self.assertIs(receipt["unity_reference_validated"], False)
        self.assertNotIn("unity_api_compile", receipt)
        api = receipt["sdk_api_compile"]
        self.assertEqual(api["target_framework"], "netstandard2.1")
        self.assertIs(api["unity_reference_validated"], False)
        self.assertIn("NOT Unity validated", api["reference_profile"])
        self.assertEqual(api["exit_code"], 0)
        self.assertEqual(len(receipt["mutations"]), 16)
        self.assertGreaterEqual(receipt["baseline"]["cases"], 14000)

    def test_unity_mode_rejects_the_real_sdk_pack_reference(self):
        receipt = json.loads((run.ARTIFACTS / "result-sdk-netstandard21.json").read_text())
        with self.assertRaisesRegex(RuntimeError, "actual installed Unity"):
            run.unity_compile({}, Path(receipt["sdk_api_compile"]["reference"]), {}, run.ARTIFACTS)


class RuntimeDataTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.producer = run.runtime_producer()
        cls.workspace = run.ARTIFACTS / ("runtime-export-checks-" + uuid.uuid4().hex)
        cls.output = cls.workspace / "runtime.json"
        cls.payload, cls.receipt = cls.producer.export(run.ARTIFACTS / "original", cls.output)
        cls.original = json.loads((run.ARTIFACTS / "source-movement-data.json").read_text())["data"]

    @classmethod
    def tearDownClass(cls):
        shutil.rmtree(cls.workspace)

    def test_exact_original_c_runtime_data_and_reproducible_cli(self):
        run.require_runtime_data(self.original, self.payload)
        second, receipt = self.producer.export(run.ARTIFACTS / "original", self.workspace / "second.json")
        self.assertEqual(second, self.payload)
        self.assertEqual(receipt["output_sha256"], self.receipt["output_sha256"])
        self.assertEqual((self.workspace / "second.json").read_bytes(), self.output.read_bytes())

    def test_runtime_fields_exclude_all_animation_fixture_data(self):
        self.assertEqual(set(self.payload["data"]), {"Constants", "Tables", "Friction", "Defense"})
        self.assertNotIn("Responses", self.output.read_text())
        self.assertIs(self.receipt["animation_fixture_included"], False)
        unit = self.output.parent / (self.output.stem + "-export/numeric-data.c")
        text = unit.read_text()
        for forbidden in ("_animate_block", "change_anim", "plyrseq.c", "responses", "script_response"):
            self.assertNotIn(forbidden, text)

    def test_both_twelve_by_six_tables_and_integer_delays_are_preserved(self):
        self.assertEqual(len(self.payload["data"]["Defense"]), 12)
        for row in self.payload["data"]["Defense"]:
            self.assertEqual(len(row["Line"]), 6)
            self.assertEqual(len(row["Back"]), 6)
            for key in ("Base", "Range", "Plow"):
                self.assertIs(type(row[key]), int)
        self.assertEqual(self.payload["data"]["Defense"], self.original["Defense"])
        self.assertEqual(self.payload["data"]["Tables"], self.original["Tables"])

    def test_fixture_or_unknown_runtime_fields_are_rejected(self):
        for key in ("Responses", "mode_expression", "HeaderStub"):
            payload = copy.deepcopy(self.payload)
            payload["data"][key] = {}
            with self.subTest(key=key), self.assertRaises(ValueError):
                self.producer.validate(payload)

    def test_real_export_mutations_are_rejected_at_the_offending_data_field(self):
        mutations = [
            ("Constants", lambda data: data["Constants"].__setitem__("QB_SPEED", data["Constants"]["QB_SPEED"] ^ 1)),
            ("Tables", lambda data: data["Tables"]["qb_runs"].reverse()),
            ("Defense", lambda data: data["Defense"].reverse()),
            ("Defense", lambda data: data["Defense"][0].__setitem__("Back", data["Defense"][0]["Line"])),
        ]
        for field, mutate in mutations:
            payload = json.loads(self.output.read_text())
            mutate(payload["data"])
            self.producer.validate(payload)
            with self.subTest(field=field), self.assertRaisesRegex(RuntimeError, "data." + field):
                run.require_runtime_data(self.original, payload)

    def test_missing_column_deduplicated_table_and_nonfinite_bits_are_rejected(self):
        payload = copy.deepcopy(self.payload)
        payload["data"]["Defense"][0]["Back"].pop()
        with self.assertRaises(ValueError):
            self.producer.validate(payload)
        payload = copy.deepcopy(self.payload)
        payload["data"]["Tables"]["push_anim_tbl"] = list(dict.fromkeys(payload["data"]["Tables"]["push_anim_tbl"]))
        with self.assertRaises(ValueError):
            self.producer.validate(payload)
        payload = copy.deepcopy(self.payload)
        payload["data"]["Constants"]["QB_SPEED"] = 0x7fc00000
        with self.assertRaises(ValueError):
            self.producer.validate(payload)

    def test_unverified_input_or_nonignored_destination_fails_before_compile(self):
        with self.assertRaises(ValueError):
            self.producer.export(run.ARTIFACTS / "original", run.ROOT / "Assets/NotIgnored.json")
        source = self.workspace / "bad-source"
        source.mkdir()
        (source / "player.c").write_bytes(b"not the original pinned source")
        with self.assertRaisesRegex(RuntimeError, "source mismatch: player.c"):
            self.producer.export(source, self.workspace / "bad.json")

    def test_schema_runtime_keys_match_the_producer(self):
        schema = json.loads((run.ROOT / "tools/movement/runtime.schema.json").read_text())
        self.assertEqual(schema["properties"]["schema"]["const"], self.producer.SCHEMA)
        fields = schema["properties"]["data"]
        self.assertIs(fields["additionalProperties"], False)
        self.assertEqual(set(fields["required"]), self.producer.RUNTIME_FIELDS)
        self.assertEqual(set(fields["properties"]), self.producer.RUNTIME_FIELDS)


if __name__ == "__main__":
    unittest.main()
