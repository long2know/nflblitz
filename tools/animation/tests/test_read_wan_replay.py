"""Original synthetic sparse WAN records; no reference animation values."""

import copy
import hashlib
import importlib.util
import json
import math
from pathlib import Path
import shutil
import struct
import types
import unittest
import uuid


TOOLS = Path(__file__).resolve().parents[1]
SPEC = importlib.util.spec_from_file_location("tools.animation.read_wan_replay", TOOLS / "read_wan_replay.py")
replay = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(replay)
SPEC = importlib.util.spec_from_file_location("tools.animation.prepare_movement_complete",
                                             TOOLS / "prepare_movement_complete.py")
complete = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(complete)
CATALOG = json.loads(complete.CATALOG.read_bytes())


def matrix(angle):
    c, s = math.cos(math.radians(angle)), math.sin(math.radians(angle))
    return (c, -s, 0., s, c, 0., 0., 0., 1.)


def angle(limb, frame, donor):
    return (30 + frame * 40) if donor else (limb * 3 + frame * 20)


def chunk(name, number, offset, *, donor, missing=None, flags=0x4000,
          frame_flags=(0x4000, 0, 0x4000), bad_kind=None, bad_frame=0):
    header = struct.pack("<20sII10I", name.encode(), flags, 15, *([0] * 10))
    count = len(frame_flags)
    payload, records = bytearray(header), {}
    for kind in (-1, -2, *range(1, 16)):
        if kind == missing:
            continue
        position = offset + 16 + len(payload)
        payload += struct.pack("<ii", kind, count)
        records[kind] = {"record": position, "data": position + 8, "count": count}
        for frame in range(count):
            if kind == -1:
                payload += struct.pack("<i", frame_flags[frame])
            elif kind == -2:
                payload += struct.pack("<3f", frame + .5, frame + 2., -frame * 3.)
            else:
                m = matrix(angle(kind, frame, donor))
                if kind == bad_kind and frame == bad_frame:
                    m = (math.nan, *m[1:])
                payload += struct.pack("<12f", .5, .25, -1., *m)
    payload += struct.pack("<i", 0)
    return struct.pack(">4sIII", b"3AN ", len(payload) + 8, number, 0) + payload, records


def fixture(*, target_flags=0x4000, missing=5, donor_missing=None, intervening=False,
            bad_kind=None, bad_frame=0, bad_donor=False):
    prefix = struct.pack(">4sIII", b"NOTE", 8, 0, 0)
    start = 8 + len(prefix)
    donor, records = chunk("Huffrun2", 1, start, donor=True, missing=donor_missing,
                           frame_flags=(0x4000,) * 5,
                           bad_kind=5 if bad_donor else None, bad_frame=4)
    middle = chunk("Other", 2, start + len(donor), donor=True)[0] if intervening else b""
    target, target_records = chunk("r_skip", 3, start + len(donor) + len(middle),
                                  donor=False, missing=missing, flags=target_flags,
                                  bad_kind=bad_kind, bad_frame=bad_frame)
    body = prefix + donor + middle + target
    data = b"WIFF" + struct.pack(">I", len(body)) + body
    recipe = copy.deepcopy(CATALOG["converter_replay"])
    recipe.update(retained_chunk_number=1, retained_chunk_byte_offset=start,
                  retained_chunk_end_byte_offset=start + len(donor), retained_source_frame_count=5,
                  retained_record_offset=records.get(5, {"record": 1})["record"],
                  retained_data_offset=records.get(5, {"data": 9})["data"])
    provenance = {
        "repository": "synthetic/replay", "commit": "a" * 40, "path": "own.wan",
        "git_blob_sha1": replay.wan.git_blob_sha1(data),
    }
    return data, provenance, recipe, target_records


class ReplayTests(unittest.TestCase):
    def assert_contract(self, module=replay):
        data, provenance, recipe, records = fixture()
        clip = module.full_replay_clip(data, "R_SKIP", provenance, recipe)
        self.assertEqual(clip["schema"], replay.SCHEMA, "explicit replay schema")
        self.assertEqual(clip["sequence"]["animation_flags"], 0x4000, "raw selected/uncompressed flag")
        self.assertEqual(clip["sequence"]["authoring_name"], "r_skip", "original authoring name")
        self.assertEqual(clip["sequence"]["selected_source_frame_indices"], [0, 2], "sparse authoring map")
        self.assertEqual(clip["converter_replay"], recipe, "complete explicit last-writer provenance")
        self.assertEqual({r["id"] for r in clip["record_layout"]}, set(records), "only actual source records")
        self.assertEqual(len(clip["record_layout"]), 16, "no invented target hand record")
        self.assertEqual(clip["validation"]["retained_writer_validated_limb_frames"], 75)
        self.assertEqual(len(clip["frames"]), 2, "all selected frames preserved")
        for ordinal, frame in enumerate(clip["frames"]):
            source = ordinal * 2
            self.assertEqual(frame["exported_frame_index"], ordinal)
            self.assertEqual(frame["source_frame_index"], source)
            self.assertEqual(frame["source_frame_flags"], 0x4000)
            root = [source + .5, source + 2., -source * 3.]
            self.assertEqual(frame["root_translation"]["authoring_xyz"], root, "own target root")
            self.assertEqual(frame["root_translation"]["scaled_runtime_xyz"],
                             [v * .12898 for v in root], "unchanged source scale")
            self.assertEqual(len(frame["rotations_xyzw"]), 15)
            for joint, q in enumerate(frame["rotations_xyzw"]):
                expected = math.radians(angle(joint + 1, source, joint == 4)) / 2
                self.assertAlmostEqual(q[0], 0.)
                self.assertAlmostEqual(q[1], 0.)
                self.assertAlmostEqual(q[2], math.sin(expected), places=7,
                                       msg="same authoring frame of original last writer, not target or frame zero")
                self.assertAlmostEqual(q[3], math.cos(expected), places=7)
        self.assertTrue(all(value is None for key, value in clip["playback"].items() if key != "status"))
        self.assertEqual(data, fixture()[0], "source bytes never changed")
        return clip

    def test_actual_explicit_reader_preserves_source_and_last_writer_identity(self):
        self.assert_contract()

    def test_default_readers_still_reject_missing_records_without_any_fallback(self):
        data, provenance, _, _ = fixture()
        for read in (replay.reader.full_clip, replay.wan.first_pose):
            with self.subTest(reader=read.__name__), self.assertRaisesRegex(
                    replay.WanError, "missing flags, root translation, or limb records"):
                read(data, "R_SKIP", provenance)

    def test_unselected_unknown_flags_and_wrong_omissions_cannot_be_qualified(self):
        for kwargs, message in (
                ({"target_flags": 1}, "not selected"),
                ({"target_flags": 0x4002}, "unsupported animation flags"),
                ({"target_flags": 0x4001}, "uncompressed selected target"),
                ({"missing": 6}, "single explicitly qualified omission"),
                ({"missing": None}, "single explicitly qualified omission"),
                ({"donor_missing": 5}, "missing flags, root translation, or limb records"),
                ({"intervening": True}, "last preceding selected animation")):
            data, provenance, recipe, _ = fixture(**kwargs)
            with self.subTest(kwargs=kwargs), self.assertRaisesRegex(replay.WanError, message):
                replay.full_replay_clip(data, "R_SKIP", provenance, recipe)

    def test_explicit_source_reference_and_recipe_guards(self):
        data, provenance, recipe, _ = fixture()
        cases = [None, {}, {**recipe, "policy": "implicit-default"},
                 {**recipe, "source_reference_symbol": "unrelated_anim"},
                 {**recipe, "source_reference_git_blob_sha1": "0" * 40},
                 {**recipe, "shipped_binary_verified": True},
                 {**recipe, "retained_authoring_name": "unrelated"},
                 {**recipe, "retained_record_offset": recipe["retained_record_offset"] + 48},
                 {**recipe, "retained_data_offset": recipe["retained_data_offset"] + 48},
                 {**recipe, "retained_source_frame_count": 1},
                 {**recipe, "retained_stride": 12}]
        cases.append({**recipe, "retained_animation_flags": 0x4001})
        for changed in cases:
            with self.subTest(recipe=changed), self.assertRaises(replay.WanError):
                replay.full_replay_clip(data, "R_SKIP", provenance, changed)
        with self.assertRaisesRegex(replay.WanError, "only for source-referenced R_SKIP"):
            replay.full_replay_clip(data, "OTHER", provenance, recipe)
        with self.assertRaisesRegex(replay.WanError, "Git blob"):
            replay.full_replay_clip(data[:-1], "R_SKIP", provenance, recipe)

    def test_deselected_target_and_later_retained_writer_matrices_are_validated(self):
        for kwargs in ({"bad_kind": 15, "bad_frame": 1}, {"bad_donor": True}):
            data, provenance, recipe, _ = fixture(**kwargs)
            with self.subTest(kwargs=kwargs), self.assertRaisesRegex(replay.WanError, "non-finite rotation"):
                replay.full_replay_clip(data, "R_SKIP", provenance, recipe)

    def test_real_record_payload_corruptions_fail_their_intended_parser_assertion(self):
        original, provenance, recipe, records = fixture()
        cases = [
            ("<f", records[-2]["data"] + 12, math.nan, "non-finite root"),
            ("<i", records[15]["record"], 4, "duplicate animation record"),
            ("<i", records[-2]["data"] - 8, 99, "unsupported animation record"),
            ("<i", records[-2]["data"] - 4, 0, "invalid frame count"),
            ("<i", records[-2]["data"] - 4, 1025, "invalid frame count"),
            ("<i", records[-2]["data"] - 4, 2, "inconsistent frame counts"),
        ]
        for layout, position, value, message in cases:
            data = bytearray(original)
            struct.pack_into(layout, data, position, value)
            changed = {**provenance, "git_blob_sha1": replay.wan.git_blob_sha1(data)}
            with self.subTest(message=message), self.assertRaisesRegex(replay.WanError, message):
                replay.full_replay_clip(data, "R_SKIP", changed, recipe)

    def test_isolated_real_replay_mutants_fail_artifact_assertions_not_builds(self):
        path = TOOLS / "read_wan_replay.py"
        before = path.read_bytes()
        source = before.decode()
        mutations = {
            "wrong-retained-frame": ('q = donor_frames[frame]["rotations_xyzw"][joint]',
                                     'q = donor_frames[0]["rotations_xyzw"][joint]'),
            "lost-frame": ('"frames": frames,', '"frames": frames[:-1],'),
            "wrong-root-scale": ("value * wan.TRANSLATION_SCALE for value in root",
                                 "value for value in root"),
            "false-compressed-flag": ('"animation_flags": flags,',
                                      '"animation_flags": flags | 1,'),
        }
        self.assert_contract()
        for name, (old, new) in mutations.items():
            self.assertEqual(source.count(old), 1, name)
            mutant = types.ModuleType("tools.animation.mutant_replay")
            mutant.__file__ = str(path)
            mutant.__package__ = "tools.animation"
            exec(compile(source.replace(old, new), str(path), "exec"), mutant.__dict__)
            with self.subTest(mutation=name), self.assertRaises(AssertionError):
                self.assert_contract(mutant)
        self.assertEqual(before, path.read_bytes())


class CompleteCatalogTests(unittest.TestCase):
    def test_new_composite_catalog_resolves_exact_frozen_156_plus_one_not_a_replacement(self):
        catalog, _, base_path = complete.load_catalog(complete.CATALOG)
        base, digest = complete.selected.load_catalog(base_path)
        self.assertEqual(digest, "e7a3e9c2962274eb68aa0b820d6226a0e25fa43f012d6a5107ef86188d902316")
        self.assertEqual(catalog["selected_base"]["manifest_sha256"],
                         "5e8fc7c5f2011303fd7b1aea87e6f957b1acdf4cbb15a43fd82f978d4b944b68")
        self.assertEqual((catalog["sequence_count"], catalog["source_frame_count"],
                          catalog["selected_frame_count"]), (157, 18174, 13490))
        self.assertEqual((base["sequence_count"], base["source_frame_count"],
                          base["selected_frame_count"]), (156, 18104, 13420))
        self.assertNotIn("R_SKIP", {entry["runtime_name"] for entry in base["clips"]})
        self.assertEqual(catalog["extension"]["source_git_blob_sha1"],
                         "256acb7ddb838627312193ae19db66abd18ac146")
        self.assertEqual(catalog["converter_replay"]["missing_source_limb_id"], 5)
        self.assertFalse(catalog["converter_replay"]["shipped_binary_verified"])
        for name, expected in (
            ("player_sequences.json", "c87b485de70bacaba9d2dd83bb2ae6bbfc59b90885e2aaa0f15e3133c7c3fa43"),
            ("ball_sequences.json", "421e3adac3ba3dce186293de69f631abcee5559d89f3ed38d7781a703ea1bb74"),
        ):
            self.assertEqual(hashlib.sha256((TOOLS / name).read_bytes()).hexdigest(), expected)

    def test_actual_catalog_mutations_fail_the_production_loader(self):
        directory = replay.wan.SCRATCH / "source-rskip-export" / ("catalog-test-" + uuid.uuid4().hex)
        directory.mkdir(parents=True)
        self.addCleanup(shutil.rmtree, directory)
        shutil.copyfile(TOOLS / "movement_sequences.json", directory / "movement_sequences.json")
        path = directory / "complete.json"
        for mutate, message in (
            (lambda d: d["selected_base"].update(catalog_sha256="0" * 64), "catalog SHA256"),
            (lambda d: d["extension"].update(source_git_blob_sha1="0" * 40), "audited pinned R_SKIP"),
            (lambda d: d["extension"].update(source_first=1), "seventy original selected"),
            (lambda d: d["extension"].update(source_first=False), "integers, not booleans"),
            (lambda d: d.update(sequence_count=156), "exactly the selected base plus R_SKIP"),
            (lambda d: d["converter_replay"].update(source_reference_symbol="wrong"), "active R_SKIP"),
            (lambda d: d["code_references"][0].update(git_blob_sha1="0" * 40), "source-reference pins"),
            (lambda d: d["source_playback_evidence"].update(steady_frames_per_second_at_57Hz=60),
             "timing evidence"),
        ):
            changed = copy.deepcopy(CATALOG)
            mutate(changed)
            path.write_text(json.dumps(changed), encoding="utf-8")
            with self.subTest(message=message), self.assertRaisesRegex(replay.WanError, message):
                complete.load_catalog(path)


if __name__ == "__main__":
    unittest.main()
