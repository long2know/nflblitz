"""Metadata and trust-boundary checks; actual original WAN qualification is explicit."""

import copy
import importlib.util
import json
from pathlib import Path
import shutil
import unittest
from unittest import mock
import uuid

SPEC = importlib.util.spec_from_file_location("contact_profile", Path(__file__).resolve().parents[1] / "prepare_contact_profile.py")
profile = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(profile)


class ContactRegistryChecks(unittest.TestCase):
    def test_exact_metadata_only_original_selected115_registry(self):
        catalog, digest = profile.load_catalog(profile.CATALOG)
        self.assertEqual(digest, "efb5e9c3b8351d6a5e96fb8617f7e8445da0aca622f3b0cf1ea7fdbd94cb31cf")
        self.assertEqual((catalog["sequence_count"], catalog["selected_frame_count"]), (272, 24892))
        self.assertEqual(sum(entry["selected_frames"] for entry in catalog["clips"]), 11402)
        self.assertEqual(len(catalog["source_wans"]), 16)
        by_name = {entry["runtime_name"]: entry for entry in catalog["clips"]}
        self.assertIn("TIE1C_A", by_name)
        self.assertNotIn("R_SKIP", by_name)
        self.assertEqual(by_name["TIE1C_A"]["source_path"], "anim/Line.wan")
        allowed = profile.selected.ENTRY_FIELDS | {
            "path", "sha256", "selected_source_frame_indices", "authoring_name",
            "original_wan_chunk_number", "original_wan_chunk_byte_offset", "runtime_selected_ordinals"}
        for entry in catalog["clips"]:
            self.assertEqual(set(entry), allowed)
            self.assertFalse(Path(entry["source_path"]).is_absolute())
            self.assertEqual(entry["runtime_selected_ordinals"], {"first": 0, "last": entry["selected_frames"] - 1})
        self.assertFalse(catalog["shipped_SEQ_numeric_ids_or_quantization_verified"])

    def test_historical_catalog_bytes_not_changed_by_contact_registration(self):
        pins = {
            "player_sequences.json": "c87b485de70bacaba9d2dd83bb2ae6bbfc59b90885e2aaa0f15e3133c7c3fa43",
            "ball_sequences.json": "421e3adac3ba3dce186293de69f631abcee5559d89f3ed38d7781a703ea1bb74",
            "movement_sequences.json": "e7a3e9c2962274eb68aa0b820d6226a0e25fa43f012d6a5107ef86188d902316",
            "movement_complete_sequences.json": "be3cc9f877ba0b92ffe24491fcd4483d91ccf186d1875d4a41d0afda1ae8d220",
        }
        for name, pin in pins.items():
            self.assertEqual(profile.sha(profile.CATALOG.with_name(name).read_bytes()), pin, name)

    def test_real_registry_mutations_fail_the_public_gate(self):
        original = json.loads(profile.CATALOG.read_bytes())
        for mutate in (
            lambda data: data.update(base_manifest_sha256="0" * 64),
            lambda data: data.update(supplemental_manifest_sha256="0" * 64),
            lambda data: data.update(sequence_count=157),
            lambda data: data.update(selected_frame_count=13490),
            lambda data: data.update(shipped_SEQ_numeric_ids_or_quantization_verified=True),
            lambda data: data["clips"][0]["selected_source_frame_indices"].__setitem__(1, 0),
            lambda data: data["clips"][0].update(path="../escape.json"),
        ):
            changed = copy.deepcopy(original)
            mutate(changed)
            with self.subTest(changed=changed != original), mock.patch.object(profile, "read_json", return_value=changed):
                with self.assertRaises(ValueError):
                    profile.load_catalog(profile.CATALOG)

    def test_explicit_source_roots_never_hide_a_conflicting_original(self):
        root = profile.selected.reader.wan.SCRATCH / ("contact-registry-check-" + uuid.uuid4().hex)
        try:
            first, second = root / "first", root / "second"
            for path in (first, second):
                (path / "anim").mkdir(parents=True)
            original = b"original synthetic WAN identity; not motion"
            (first / "anim/example.wan").write_bytes(original)
            identity = {"git_blob_sha1": profile.selected.reader.wan.git_blob_sha1(original),
                        "sha256": profile.sha(original)}
            self.assertEqual(profile._original_bytes([first], "anim/example.wan", identity), original)
            (second / "anim/example.wan").write_bytes(original + b"wrong")
            with self.assertRaisesRegex(ValueError, "wrong original bytes"):
                profile._original_bytes([first, second], "anim/example.wan", identity)
            with self.assertRaisesRegex(ValueError, "missing pinned original"):
                profile._original_bytes([first], "anim/missing.wan", identity)
        finally:
            if root.exists():
                shutil.rmtree(root)


if __name__ == "__main__":
    unittest.main()
