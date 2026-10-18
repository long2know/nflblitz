import importlib.util
from pathlib import Path
import unittest


SPEC = importlib.util.spec_from_file_location("source_player_prepare", Path(__file__).with_name("prepare.py"))
prepare = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(prepare)


class PreparationChecks(unittest.TestCase):
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
