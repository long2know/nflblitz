#!/usr/bin/env python3
"""Guard tests only: metadata, content pins and confinement; no synthetic geometry/runtime substitution."""
import hashlib
import json
from pathlib import Path
import shutil
import unittest
import uuid

import fetch_inputs
import prepare_inputs


class RealInputGuards(unittest.TestCase):
    def setUp(self):
        self.work = Path(__file__).resolve().parent / ".artifacts" / ("input-guard-" + uuid.uuid4().hex)
        self.work.mkdir(parents=True)

    def tearDown(self):
        shutil.rmtree(self.work)

    def test_combined_actual_source_pin_inventory_has_no_conflicts(self):
        pins = fetch_inputs.input_pins()
        self.assertEqual(69, len(pins))
        self.assertEqual("5744c51f30b625b9f80b92388ab49ff0ebc5bb2c", pins["player.c"])
        self.assertEqual("15b49474fb55e8bac280b6515fcf7bf59cdf8664", pins["nfl.c"])
        self.assertEqual(24, sum(name.lower().endswith(".wan") for name in pins))
        for pin in pins.values():
            self.assertRegex(pin, r"^[0-9a-f]{40}$")

    def test_actual_camera_oracle_input_closure_is_acquired(self):
        self.assertEqual({
            "include/GAME.H", "include/ani3d.h", "include/camdefs.h", "CamData.c",
            "cambot.c", "plyrseq.c", "detect.c", "player.c", "lib/ani3d/ANIM.C",
            "lib/ani3d/pipe3D.c",
        }, set(fetch_inputs.CAMERA_INPUTS))
        pins = fetch_inputs.input_pins()
        for name, pin in fetch_inputs.CAMERA_INPUTS.items():
            self.assertEqual(pin, pins[name], name)

    def test_sound_inputs_reuse_the_exact_four_pin_acquirer_and_actual_state_producers(self):
        pins = fetch_inputs.input_pins()
        sound = fetch_inputs.sound_function_pins()
        self.assertEqual({"sounds.c", "include/sndcalls.h", "include/player.h", "plyrseq.c"}, set(sound))
        self.assertEqual({"Game.c", "mainproc.c"}, set(fetch_inputs.SOUND_STATE_INPUTS))
        for name, pin in {**sound, **fetch_inputs.SOUND_STATE_INPUTS}.items():
            self.assertEqual(pin, pins[name], name)

    def test_sound_payload_audit_and_original_include_have_explicit_nonalias_pins(self):
        self.assertEqual("4a557304d3b041ee04dff8ed1354714fc5665abaeb1c3d458d94171adf1a0280",
                         prepare_inputs.SOUND_SHA)
        self.assertEqual("db9e2f18fb6c62147843250ddd80c36712360a46193ab02ef23a96ab48224096",
                         prepare_inputs.SOUND_INCLUDE_SHA)
        self.assertEqual("4601c44212825a87b5b8c46c15dd91cccc58ad84e7e22e40d696a16afa5cab40",
                         prepare_inputs.SOUND_AUDIT_SHA)

    def test_git_blob_pin_is_not_plain_file_sha1(self):
        self.assertEqual("d670460b4b4aece5915caf5c68d12f560a9fe3e4",
                         fetch_inputs.blob_sha1(b"test content\n"))
        self.assertNotEqual(hashlib.sha1(b"test content\n").hexdigest(),
                            fetch_inputs.blob_sha1(b"test content\n"))

    def test_output_is_confined_to_this_worktree_scratch(self):
        allowed = fetch_inputs.ROOT / ".squad/.scratch/SourceData/example"
        self.assertEqual(allowed, fetch_inputs.ignored_output(allowed))
        for path in (fetch_inputs.ROOT / "Assets/unwanted", fetch_inputs.ROOT,
                     fetch_inputs.ROOT / ".squad/.scratch", self.work / ".." / "escape"):
            with self.subTest(path=path), self.assertRaises(ValueError):
                fetch_inputs.ignored_output(path)

    def test_actual_offending_file_bytes_are_hashed_not_status_only(self):
        path = self.work / "owned.json"
        path.write_bytes(b'{"owned_synthetic_guard":1}\n')
        expected = hashlib.sha256(path.read_bytes()).hexdigest()
        self.assertEqual(path, prepare_inputs.verified_file(self.work, "owned.json", expected))
        path.write_bytes(b'{"owned_synthetic_guard":2}\n')
        with self.assertRaisesRegex(ValueError, "content pin changed"):
            prepare_inputs.verified_file(self.work, "owned.json", expected)

    def test_fixture_file_path_traversal_is_rejected_before_read(self):
        for relative in ("../outside.json", "/outside.json", "nested/../../outside.json"):
            with self.subTest(relative=relative), self.assertRaisesRegex(ValueError, "escapes"):
                prepare_inputs.verified_file(self.work, relative, "0" * 64)

    def test_fixture_symlink_cannot_hide_a_changed_artifact(self):
        target = self.work / "owned.json"
        target.write_bytes(b"owned synthetic guard")
        linked = self.work / "linked.json"
        linked.symlink_to(target)
        with self.assertRaisesRegex(ValueError, "symlinks"):
            prepare_inputs.verified_file(self.work, "linked.json", prepare_inputs.sha(target))

    def test_canonical117_pin_is_not_any_historical_manifest_alias(self):
        self.assertEqual("f3c34d90337c1432a0eb7a7a59fd3d583e9130946283ae8d929e008e0e1fd274",
                         prepare_inputs.ANIMATION_SHA)
        self.assertNotEqual("2baec9ff3fe07a75aab48f8ddaaf45371fcb13dfb1c9765334d1320342be6f48",
                            prepare_inputs.ANIMATION_SHA)
        self.assertNotEqual("18af668aa0ff97985f91030f6c17d0552a9630fe25d40893dc5a7ac8381b7cdf",
                            prepare_inputs.ANIMATION_SHA)

    def test_movement_library_identity_never_relabels_canonical117_or_selected156(self):
        self.assertEqual("f0ae9395e57ae8327b7b55493dd6c457102adb4a13ecbb581e3551d400a3800e",
                         prepare_inputs.MOVEMENT_ANIMATION_SHA)
        self.assertEqual("5e8fc7c5f2011303fd7b1aea87e6f957b1acdf4cbb15a43fd82f978d4b944b68",
                         prepare_inputs.MOVEMENT_SELECTED_SHA)
        self.assertEqual(3, len({prepare_inputs.ANIMATION_SHA, prepare_inputs.MOVEMENT_ANIMATION_SHA,
                                prepare_inputs.MOVEMENT_SELECTED_SHA}))

    def test_runtime_recipe_has_no_test_first_header_responses(self):
        recipe = json.loads((fetch_inputs.ROOT / "tools/movement/runtime.schema.json").read_text())
        data = recipe["properties"]["data"]
        self.assertEqual({"Constants", "Tables", "Friction", "Defense"}, set(data["properties"]))
        self.assertFalse(data["additionalProperties"])
        self.assertNotIn("Responses", data["properties"])

    def test_fixture_schema_failure_precedes_original_data_loading(self):
        path = self.work / "fixture.json"
        path.write_text(json.dumps({"schema": "wrong", "source_revision": fetch_inputs.REVISION}))
        with self.assertRaisesRegex(ValueError, "schema/revision"):
            prepare_inputs.fixture_hashes(path)


if __name__ == "__main__":
    unittest.main()
