"""C2 registry/producer trust gates without original payloads or numeric tables."""
import importlib.util
import json
from pathlib import Path
import shutil
import unittest
import uuid

SPEC = importlib.util.spec_from_file_location("c2_production_preparer", Path(__file__).resolve().parents[1] / "prepare_c2_profile.py")
producer = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(producer)


class C2ProducerChecks(unittest.TestCase):
    def test_successor_has_only_five_explicit_source_bindings_not_new_programs(self):
        self.assertEqual(producer.M1_DATA_SHA, "b0c02e5516f4f932625e1f8ccb3ff0fd53ae222657f899eb8c9d0b0c2e74460a")
        self.assertNotEqual(producer.M1_ENTRY_PROFILE, producer.ENTRY_PROFILE)
        self.assertEqual(producer.RUNNING_BINDINGS, (
            ("r_histep_anim", 6373, -4), ("r_skip_anim", 6401, -4),
            ("r_hihand_anim", 6428, -4), ("r_bktau2_anim", 6480, -4),
            ("r_ezsprint_anim", 6508, -5)))
        self.assertFalse(set(producer.SCRIPTS) & {entry[0] for entry in producer.RUNNING_BINDINGS})
        self.assertEqual(len(producer.SCRIPTS), 9)

    def test_cyclic_running_bindings_need_actual_source_word_not_an_invented_end(self):
        lines = [""] * 6550
        for symbol, line, argument in producer.RUNNING_BINDINGS:
            lines[line - 5] = ".globl " + symbol
            lines[line - 4] = symbol + ":"
            lines[line - 2] = ".byte\tANI_CODE"
            lines[line - 1] = ".word\ttaunt_speech," + str(argument) + ",0,0"
        lines[6540] = ".globl synthetic_boundary"
        bindings = producer.running_bindings(lines)
        self.assertEqual([(b["script"], b["line"], b["arguments"][0]) for b in bindings],
                         list(producer.RUNNING_BINDINGS))
        self.assertFalse(any("ANI_END" in line for line in lines))
        lines[6400] = ".word\ttaunt_speech,-5,0,0"
        with self.assertRaisesRegex(ValueError, "callback tuple changed"):
            producer.running_bindings(lines)

    def test_tracked_extra9_catalog_preserves_exact_metadata_identity(self):
        path = producer.ROOT / "tools/animation/c2_sequences.json"
        catalog, digest = producer.selected.load_catalog(path)
        self.assertEqual(digest, "29073877f1e6586fb961b18b65d7d1173e7d77e2a8567e8f64bc2f3fbbc42210")
        self.assertEqual(set(catalog), {"schema", "source", "format_source", "sequence_count",
                                       "source_frame_count", "selected_frame_count", "clips"})
        self.assertEqual((catalog["sequence_count"], catalog["source_frame_count"],
                          catalog["selected_frame_count"]), (9, 2141, 1338))
        self.assertEqual({entry["runtime_name"] for entry in catalog["clips"]},
                         {script[:-5].upper() for script in producer.SCRIPTS})
        self.assertEqual({(entry["source_path"], entry["source_git_blob_sha1"])
                          for entry in catalog["clips"]}, {
            ("anim/misc.wan", "da6818b994f1e4ba93e48e151382a8dc96f27f58"),
            ("anim/misc3.wan", "ba79820423945efc7c45c2b66fe719511090a1ba"),
            ("anim/misc4.wan", "911c4f39800c5cd406fd5148cc3d51482b19d1f1"),
        })
        for entry in catalog["clips"]:
            self.assertEqual(set(entry), producer.selected.ENTRY_FIELDS)

    def test_extra9_catalog_rejects_payload_fields(self):
        catalog, _ = producer.selected.load_catalog(producer.ROOT / "tools/animation/c2_sequences.json")
        root = producer.ROOT / ".squad/.scratch" / ("c2-metadata-guard-" + uuid.uuid4().hex)
        try:
            root.mkdir()
            path = root / "catalog.json"
            for field in ("wan_bytes", "frame_values", "poses", "audio_samples"):
                with self.subTest(field=field):
                    entry = dict(catalog["clips"][0], **{field: []})
                    path.write_text(json.dumps(dict(catalog, clips=[entry] + catalog["clips"][1:])))
                    with self.assertRaisesRegex(ValueError, "only names, pins, counts and boundaries"):
                        producer.selected.load_catalog(path)
        finally:
            if root.exists():
                shutil.rmtree(root)

    def test_explicit_union_data_and_old_identities(self):
        self.assertEqual(producer.ANIMATION_SHA, "e87401c8cf3e348cc2d9c7255a1f18f43e41b1694ff7dff45c4bf025a5ecce4f")
        self.assertEqual(producer.DATA_SHA, "9d2cb7ac5d92c3c2178fc1970a68ccad778fb7d319513b1787fe3ba3fe905f99")
        self.assertEqual(producer.HIT_SHA, "bb978454ab57b09357e422f048e4035bea01a4d5932f453dd89be39838c6e904")
        self.assertEqual(producer.BASE_SHA, "9cae962bbb861bffe4d379bc30919cf34c0238ed35be82e605253d3059cfede9")
        self.assertEqual(producer.EXTRA_SHA, "3e69a29f11c15c5d318f486824b1a3bba1f9489905e20d4c9ce76742af7a8c5f")
        self.assertEqual(len(set(producer.SCRIPTS)), 9)
        self.assertEqual(len(producer.SOURCE_PINS), 6)

    def test_wrong_originals_fail_before_any_output(self):
        root = producer.ROOT / ".squad/.scratch" / ("c2-producer-guard-" + uuid.uuid4().hex)
        try:
            source, base, extra, output = (root / n for n in ("source", "base", "extra", "output"))
            for path in (source, base, extra):
                path.mkdir(parents=True)
            for name in producer.SOURCE_PINS:
                path = source / name; path.parent.mkdir(parents=True, exist_ok=True)
                path.write_bytes(b"explicit synthetic wrong original, not a table or motion payload")
            with self.assertRaisesRegex(ValueError, "source blob differs"):
                producer.prepare(source, base, extra, output)
            self.assertFalse(output.exists())
        finally:
            if root.exists():
                shutil.rmtree(root)


if __name__ == "__main__":
    unittest.main()
