import importlib.util
from pathlib import Path
import unittest


SPEC = importlib.util.spec_from_file_location("source_player_prepare", Path(__file__).with_name("prepare.py"))
prepare = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(prepare)


class PreparationChecks(unittest.TestCase):
    def possession_fixture(self):
        import json
        root, source, _, _ = self.camera_fixture()
        manifest = json.loads((source / "manifest.json").read_bytes())
        manifest["entrypoints"]["normalCamera"] = "first.bytes"
        (source / "manifest.json").write_text(json.dumps(manifest))
        speech = root / "authored-speech.bytes"
        speech.write_bytes(b"authored synthetic operand input, not original numeric data")
        import prepare_possession
        return root, source, speech, prepare_possession

    def test_possession_extension_preserves_all_original_pins_and_profile(self):
        import json
        import shutil
        root, source, speech, module = self.possession_fixture()
        old_pin = module.SPEECH_SHA256
        try:
            module.SPEECH_SHA256 = prepare.digest(speech.read_bytes())
            original = (source / "manifest.json").read_bytes()
            before = {p.name: p.read_bytes() for p in source.iterdir()}
            receipt = module.prepare(source, prepare.digest(original), speech, root / "extended")
            extended = json.loads((root / "extended/manifest.json").read_bytes())
            self.assertTrue(receipt["originalPinsPreserved"])
            self.assertFalse(receipt["formatMigration"])
            self.assertEqual(receipt["fileCount"], 3)
            self.assertEqual(extended["entrypoints"]["possessionSound"], "camera/possession-speech.bytes")
            self.assertEqual(extended["profile"], json.loads(original)["profile"])
            self.assertEqual(json.loads(original)["files"], [pin for pin in extended["files"] if pin["path"] != "camera/possession-speech.bytes"])
            self.assertEqual(before, {p.name: p.read_bytes() for p in source.iterdir()})
        finally:
            module.SPEECH_SHA256 = old_pin
            shutil.rmtree(root)

    def test_possession_extension_rejects_bad_source_hash_operand_and_changed_inherited_file(self):
        import shutil
        root, source, speech, module = self.possession_fixture()
        old_pin = module.SPEECH_SHA256
        try:
            expected = prepare.digest((source / "manifest.json").read_bytes())
            with self.assertRaisesRegex(ValueError, "manifest changed"):
                module.prepare(source, "0" * 64, speech, root / "bad-manifest")
            with self.assertRaisesRegex(ValueError, "authored-speech.bytes"):
                module.prepare(source, expected, speech, root / "bad-operands")
            module.SPEECH_SHA256 = prepare.digest(speech.read_bytes())
            (source / "first.bytes").write_bytes(b"changed inherited synthetic asset")
            with self.assertRaisesRegex(ValueError, "first.bytes"):
                module.prepare(source, expected, speech, root / "bad-old-asset")
        finally:
            module.SPEECH_SHA256 = old_pin
            shutil.rmtree(root)

    def camera_fixture(self):
        import json
        import uuid
        root = prepare.PROJECT / ".squad/.scratch" / ("camera-data-fixture-" + uuid.uuid4().hex)
        source = root / "base"
        source.mkdir(parents=True)
        payloads = {"first.bytes": b"authored synthetic first input", "second.bytes": b"authored synthetic second input"}
        records = []
        for name, raw in payloads.items():
            (source / name).write_bytes(raw)
            records.append(dict(path=name, bytes=len(raw), sha256=prepare.digest(raw)))
        manifest = dict(schema="blitz-portable-source-player/v1", profile="original-teams-contact281-c2-m1",
                        entrypoints={"first": "first.bytes"}, files=records, origins={"synthetic": "not original data"})
        (source / "manifest.json").write_text(json.dumps(manifest))
        camera = root / "authored-camera.json"
        camera.write_bytes(b'{"synthetic":"not original numeric data"}')
        import prepare_camera
        return root, source, camera, prepare_camera

    def test_camera_extension_preserves_every_inherited_pin_without_format_migration(self):
        import json
        import shutil
        root, source, camera, module = self.camera_fixture()
        old_pin = module.CAMERA_SHA256
        try:
            module.CAMERA_SHA256 = prepare.digest(camera.read_bytes())
            original = (source / "manifest.json").read_bytes()
            before = {p.name: p.read_bytes() for p in source.iterdir()}
            receipt = module.prepare(source, prepare.digest(original), camera, root / "extended")
            extended = json.loads((root / "extended/manifest.json").read_bytes())
            self.assertEqual(extended["schema"], "blitz-portable-source-player/v1")
            self.assertFalse(receipt["formatMigration"])
            self.assertEqual(receipt["fileCount"], 3)
            self.assertEqual(json.loads(original)["files"], [pin for pin in extended["files"] if not pin["path"].startswith("camera/")])
            self.assertEqual(before, {p.name: p.read_bytes() for p in source.iterdir()})
        finally:
            module.CAMERA_SHA256 = old_pin
            shutil.rmtree(root)

    def test_camera_extension_rejects_wrong_manifest_camera_and_changed_old_asset(self):
        import shutil
        root, source, camera, module = self.camera_fixture()
        old_pin = module.CAMERA_SHA256
        try:
            expected = prepare.digest((source / "manifest.json").read_bytes())
            with self.assertRaisesRegex(ValueError, "manifest changed"):
                module.prepare(source, "0" * 64, camera, root / "bad-manifest")
            with self.assertRaisesRegex(ValueError, "authored-camera.json"):
                module.prepare(source, expected, camera, root / "bad-camera")
            module.CAMERA_SHA256 = prepare.digest(camera.read_bytes())
            (source / "first.bytes").write_bytes(b"changed authored fixture")
            with self.assertRaisesRegex(ValueError, "first.bytes"):
                module.prepare(source, expected, camera, root / "changed-old-asset")
        finally:
            module.CAMERA_SHA256 = old_pin
            shutil.rmtree(root)

    def test_actual_prepared_inputs_have_complete_verified_inventory(self):
        root = Path(__file__).resolve().parent / "data"
        if not root.exists():
            self.skipTest("Explicit ignored original data is not installed; no placeholder data is generated.")
        import json
        manifest = json.loads((root / "manifest.json").read_bytes())
        self.assertEqual(manifest["schema"], "blitz-portable-source-player/v1")
        self.assertEqual(manifest["profile"], "original-teams-contact281-c2-m1")
        self.assertEqual(manifest["sourceRevision"], prepare.SOURCE)
        self.assertGreater(len(manifest["files"]), 300)
        for record in manifest["files"]:
            data = prepare.read(root, record["path"], record["sha256"])
            self.assertEqual(len(data), record["bytes"], record["path"])
        for entrypoint in manifest["entrypoints"].values():
            self.assertIn(entrypoint, {record["path"] for record in manifest["files"]})

    def test_relative_explicit_input_roots_are_normalized_without_allowing_traversal(self):
        here = Path(__file__).resolve().parent
        actual = prepare.read(here / "../source-player", "match-settings.json",
                              prepare.digest((here / "match-settings.json").read_bytes()))
        self.assertEqual(actual, (here / "match-settings.json").read_bytes())
        for invalid in ("../match-settings.json", "/match-settings.json", "a//b", "a\\b", "a:b", "a/./b"):
            with self.assertRaises(ValueError, msg=invalid):
                prepare.portable(invalid)

    def test_real_source_artifact_mutation_is_rejected_by_name(self):
        here = Path(__file__).resolve().parent
        with self.assertRaisesRegex(ValueError, "match-settings.json"):
            prepare.read(here, "match-settings.json", "0" * 64)


if __name__ == "__main__":
    unittest.main()
