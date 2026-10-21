"""Closed metadata gates; optional actual private S1 export/producer failure controls."""
import argparse
from contextlib import contextmanager
import copy
import importlib.util
import json
from pathlib import Path
import shutil
import sys
import unittest
import uuid

SPEC = importlib.util.spec_from_file_location(
    "presnap_producer", Path(__file__).resolve().parents[1] / "prepare_presnap_switch_profile.py")
producer = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(producer)
SYSTEMS = None


class MetadataChecks(unittest.TestCase):
    def test_exact_one_clip_catalog_and_existing_converter(self):
        catalog, digest = producer.selected.load_catalog(
            producer.ROOT / "tools/animation/presnap_switch_sequences.json")
        self.assertEqual(digest, "99eae60de4f4e51391e68a1ce2219f3fddb880cef640ad7ec717530ec3f37894")
        self.assertEqual((catalog["sequence_count"], catalog["source_frame_count"],
                          catalog["selected_frame_count"]), (1, 35, 35))
        self.assertEqual(catalog["clips"], [{
            "runtime_name": "M_HOPCLP", "source_path": "anim/misc2.wan",
            "source_git_blob_sha1": "5b3083a71b1a7729d508b804712f5a754afaca5b",
            "source_frames": 35, "selected_frames": 35, "source_first": 0, "source_last": 34}])
        self.assertEqual(catalog["format_source"], producer.selected.reader.wan.FORMAT_SOURCE)

    def test_new_generation_does_not_relabel_old_pins_or_calls(self):
        self.assertEqual(producer.c2.ANIMATION_SHA,
                         "e87401c8cf3e348cc2d9c7255a1f18f43e41b1694ff7dff45c4bf025a5ecce4f")
        self.assertEqual(producer.c2.M1_DATA_SHA,
                         "b0c02e5516f4f932625e1f8ccb3ff0fd53ae222657f899eb8c9d0b0c2e74460a")
        self.assertNotEqual(producer.ENTRY_PROFILE, producer.c2.M1_ENTRY_PROFILE)
        self.assertNotEqual(producer.ANIMATION_SHA, producer.c2.ANIMATION_SHA)
        self.assertEqual(producer.ATTACK_SOUND, {
            "path": "sounds.c", "function": "attack_snd", "line": 1638,
            "bank_source": "plyr_bnk_str", "playlist_source": "snd1",
            "volume": 160, "pan": 127, "human_priority": 10, "drone_priority": 15})
        self.assertEqual(producer.HOP_BINDING, {
            "path": "plyrseq.c", "script": "m_hopclp_anim", "function": "attack_snd",
            "kind": "AttackSoundFixed", "line": 3409, "arguments": [127, 0, 0]})
        self.assertEqual(len(producer.c2.SCRIPTS), 9)

    def test_metadata_only_default_never_needs_originals(self):
        self.assertEqual(producer.PROFILE, "contact282-c2-m1-presnap-switch")
        self.assertEqual(producer.STABLE_ID, "original-teams-" + producer.PROFILE)
        self.assertEqual(producer.MOVEMENT_SHA,
                         "2f2d888ff89a6a89d9b72d19590115a71cce8ff5c7c7d34b8c313205cf0358ef")
        self.assertEqual(producer.C2_PROGRAMS_SHA, producer.c2.sha(
            (producer.ROOT / "Assets/Scripts/Gameplay/SourceAnimationPrograms.C2.cs").read_bytes()))


class ActualProducerChecks(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        if SYSTEMS is None:
            raise unittest.SkipTest("Use --systems-output for explicit authentic private generation checks.")
        cls.release = SYSTEMS.absolute()
        producer.selected._no_symlinks(cls.release)
        baseline = json.loads((cls.release / "baseline.json").read_bytes())
        cls.source = Path(baseline["producer_source_root"])
        cls.base = Path(baseline["base_m1_root"])
        cls.base_catalog = next(Path(p) for p in baseline["legacy_base_pins"]
                                if p.endswith("/kernel/impact-presentation.json"))
        cls.movement = producer.ROOT / "Assets/Scripts/Gameplay/SourceAnimationPrograms.Movement.cs"
        cls.work = cls.release / "validation" / ("producer-" + uuid.uuid4().hex)
        cls.work.mkdir(parents=True)
        cls.hop = cls.work / "hop"
        shutil.copytree(cls.release / "hop", cls.hop)
        cls.inventory = producer.document((cls.release / "profile/animation282/export-inventory.json").read_bytes())
        cls.callbacks = producer.document((cls.release / "profile/c2-callback-data.json").read_bytes())
        cls.clip = producer.document((cls.hop / "clips/M_HOPCLP.json").read_bytes())

    @classmethod
    def tearDownClass(cls):
        if hasattr(cls, "work") and cls.work.exists():
            shutil.rmtree(cls.work)

    def profile(self, name, **overrides):
        values = dict(source_root=self.source, base_m1_inputs=self.base, hop_bundle=self.hop,
                      movement_programs=self.movement, destination=self.work / name)
        values.update(overrides)
        return producer.prepare_profile(**values)

    @contextmanager
    def changed(self, path, content):
        original = path.read_bytes()
        if content is None:
            path.unlink()
        else:
            path.write_bytes(content)
        try:
            yield
        finally:
            path.write_bytes(original)

    def test_authentic_selected_map_quality_and_all281_inherited_bytes(self):
        hop_inventory = producer.document((self.hop / "export-inventory.json").read_bytes())
        producer.validate_hop(hop_inventory, self.clip)
        producer.validate_union(self.inventory)
        producer.validate_callbacks(self.callbacks, producer.ANIMATION_SHA)
        old = producer.document((self.base / "animation281/export-inventory.json").read_bytes())
        self.assertEqual(len(old["clips"]), 281)
        for entry in old["clips"]:
            with self.subTest(clip=entry["runtime_name"]):
                before = producer.read(self.base / "animation281" / entry["path"], entry["sha256"],
                                       8 * 1024 * 1024)
                after = producer.read(self.release / "profile/animation282" / entry["path"], entry["sha256"],
                                      8 * 1024 * 1024)
                self.assertEqual(before, after)
        old_callbacks = producer.document((self.base / "c2-callback-data.json").read_bytes())
        self.assertEqual(self.callbacks["vm_bindings"][:-1], old_callbacks["vm_bindings"])
        for field in ("banks", "speech_tables", "sound_calls", "end_producer_sites"):
            self.assertEqual(self.callbacks[field], old_callbacks[field], field)
        for path in (self.release / "profile").rglob("*"):
            producer.selected._no_symlinks(path)
        self.assertEqual(sum(p.is_file() for p in (self.release / "profile").rglob("*")), 289)

    def test_create_new_deterministic_full_profile_and_unchanged482_impact_sections(self):
        result = self.profile("reproduced")
        self.assertEqual((result["clips"], result["selected_frames"], result["bindings"]), (282, 26265, 15))
        self.assertEqual(len(result["files"]), 289)
        producer.selected._verify_bundle(self.release / "profile", {
            p.relative_to(self.work / "reproduced").as_posix(): p.read_bytes()
            for p in (self.work / "reproduced").rglob("*") if p.is_file()})
        output = self.work / "impact-reproduced"
        producer.prepare_impact(self.base_catalog, self.work / "reproduced/c2-callback-data.json",
                                self.work / "reproduced/animation282/export-inventory.json",
                                self.movement, output)
        actual = (output / "impact-presentation.json").read_bytes()
        self.assertEqual(producer.sha(actual), producer.IMPACT_SHA)
        self.assertEqual(actual, (self.release / "impact/impact-presentation.json").read_bytes())
        old, new = producer.document(self.base_catalog.read_bytes()), producer.document(actual)
        for field in old.keys() - {"schema", "contact_binding", "qualifications"}:
            self.assertEqual(producer.audio.canonical(old[field]), producer.audio.canonical(new[field]), field)
        for field in ("camera_sites", "new_callback_lines", "programs_sha256"):
            self.assertEqual(old["contact_binding"][field], new["contact_binding"][field], field)
        self.assertEqual(len(new["animation_cues"]), 482)
        self.assertEqual(sum(c["line"] == 3409 for c in new["animation_cues"]), 1)

    def test_wrong_original_source_blob_fails_before_output(self):
        source = self.work / "wrong-original"
        shutil.copytree(self.source, source)
        path = source / "sounds.c"
        path.write_bytes(path.read_bytes() + b"\n")
        with self.assertRaisesRegex(ValueError, "original source blob"):
            self.profile("bad-source-output", source_root=source)
        self.assertFalse((self.work / "bad-source-output").exists())

    def test_stale_movement_program_fails_before_output(self):
        path = self.work / "stale-Movement.cs"
        path.write_bytes(self.movement.read_bytes() + b"\n")
        with self.assertRaisesRegex(ValueError, "trusted content pin.*stale-Movement"):
            self.profile("stale-program-output", movement_programs=path)
        self.assertFalse((self.work / "stale-program-output").exists())

    def test_actual_missing_hop_clip_fails_without_fallback_or_partial_output(self):
        with self.changed(self.hop / "clips/M_HOPCLP.json", None):
            with self.assertRaises(FileNotFoundError) as caught:
                self.profile("missing-clip-output")
        self.assertIn("M_HOPCLP.json", str(caught.exception))
        self.assertFalse((self.work / "missing-clip-output").exists())

    def test_actual_wrong_selected_map_and_changed_clip_pin_fail(self):
        bad = copy.deepcopy(self.clip)
        bad["sequence"]["selected_source_frame_indices"][4] = 5
        hop_inventory = producer.document((self.hop / "export-inventory.json").read_bytes())
        with self.assertRaisesRegex(ValueError, "selected map"):
            producer.validate_hop(hop_inventory, bad)
        with self.changed(self.hop / "clips/M_HOPCLP.json", producer.audio.canonical(bad)):
            with self.assertRaisesRegex(ValueError, "trusted content pin.*M_HOPCLP"):
                self.profile("changed-clip-output")
        self.assertFalse((self.work / "changed-clip-output").exists())

    def test_actual_source_hash_count_and_quality_mismatches_fail(self):
        inventory = producer.document((self.hop / "export-inventory.json").read_bytes())
        for field, value, message in (
                ("provenance", {"sha256": "0" * 64}, "WAN source content"),
                ("sequence", {"source_frame_count": 34}, "source identity"),
                ("validation", {"max_matrix_orthogonality_error": 0.1}, "quality")):
            bad = copy.deepcopy(self.clip)
            bad[field].update(value)
            with self.subTest(field=field), self.assertRaisesRegex(ValueError, message):
                producer.validate_hop(inventory, bad)

    def test_profile_library_program_count_and_exact_tuple_controls_fail(self):
        controls = (
            lambda d: d.update(profile=producer.c2.M1_ENTRY_PROFILE),
            lambda d: d.update(animation_manifest_sha256=producer.c2.ANIMATION_SHA),
            lambda d: d.update(presnap_switch_movement_programs_sha256="0" * 64),
            lambda d: d["format_source"].update(clip_count=281),
            lambda d: d["vm_bindings"].pop(),
            lambda d: d["vm_bindings"][-1].update(arguments=[126, 0, 0]),
            lambda d: d["attack_sound"].update(human_priority=15),
            lambda d: d["vm_bindings"][0].update(arguments=[-1, 0, 0]),
        )
        for index, mutate in enumerate(controls):
            bad = copy.deepcopy(self.callbacks)
            mutate(bad)
            with self.subTest(control=index), self.assertRaises(ValueError):
                producer.validate_callbacks(bad, producer.ANIMATION_SHA)
        for mutate in (lambda d: d.update(profile="original-teams-contact281-c2"),
                       lambda d: d["clips"].pop()):
            bad = copy.deepcopy(self.inventory)
            mutate(bad)
            with self.assertRaises(ValueError):
                producer.validate_union(bad)

    def test_old14_payload_or_old281_inventory_cannot_rebind_new_impact(self):
        for callback, inventory in (
            (self.base / "c2-callback-data.json", self.release / "profile/animation282/export-inventory.json"),
            (self.release / "profile/c2-callback-data.json", self.base / "animation281/export-inventory.json"),
        ):
            output = self.work / ("stale-impact-" + uuid.uuid4().hex)
            with self.assertRaisesRegex(ValueError, "trusted content pin"):
                producer.prepare_impact(self.base_catalog, callback, inventory, self.movement, output)
            self.assertFalse(output.exists())

    def test_unlisted_file_and_symlink_are_not_trusted_exports(self):
        path = self.hop / "unlisted.json"
        path.write_bytes(b"{}")
        try:
            with self.assertRaisesRegex(ValueError, "unexpected bundle file"):
                self.profile("unlisted-output")
        finally:
            path.unlink()
        clip = self.hop / "clips/M_HOPCLP.json"
        saved = clip.read_bytes()
        target = self.work / "own-clip.json"
        target.write_bytes(saved)
        clip.unlink()
        clip.symlink_to(target)
        try:
            with self.assertRaisesRegex(ValueError, "symlinks are not allowed"):
                self.profile("symlink-output")
        finally:
            clip.unlink()
            clip.write_bytes(saved)
        self.assertFalse((self.work / "symlink-output").exists())

    def test_create_new_and_no_input_output_overlap(self):
        with self.assertRaisesRegex(ValueError, "create-new"):
            self.profile("unused", destination=self.release / "profile")
        with self.assertRaisesRegex(ValueError, "overlap"):
            self.profile("unused", destination=self.hop / "nested-output")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(add_help=False)
    parser.add_argument("--systems-output", type=Path)
    args, remaining = parser.parse_known_args()
    SYSTEMS = args.systems_output
    unittest.main(argv=[sys.argv[0], *remaining])
