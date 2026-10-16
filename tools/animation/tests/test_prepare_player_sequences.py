"""Original multisequence WAN/metadata fixtures; no upstream animation values."""

import contextlib
import copy
import hashlib
import importlib.util
import io
import json
import os
from pathlib import Path
import shutil
import struct
import subprocess
import sys
import unittest
from unittest import mock
import uuid


MODULE_PATH = Path(__file__).resolve().parents[1] / "prepare_player_sequences.py"
BALL_CATALOG = MODULE_PATH.with_name("ball_sequences.json")
SPEC = importlib.util.spec_from_file_location("tools.animation.prepare_player_sequences", MODULE_PATH)
batch = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(batch)
SELECTED = {"ALPHA": [1, 3], "BETA": [0, 1, 2], "GAMMA": [0]}


def blob_sha1(data):
    return hashlib.sha1(b"blob " + str(len(data)).encode("ascii") + b"\0" + data).hexdigest()


def chunk(tag, payload, number):
    data = struct.pack(">4sIII", tag, len(payload) + 8, number, 0) + payload
    return data + b"\0" * (len(data) % 2)


def animation(name, flags, number, *, bad_root=False):
    count = len(flags)
    header = struct.pack("<20sII10I", name, 0x4001, 15, *([0] * 10))
    root = b"".join(struct.pack("<3f", float("nan") if bad_root and i == count - 1 else i + .25,
                                number * 2., -i - .5) for i in range(count))
    records = [struct.pack("<ii", -2, count) + root]
    for limb in range(15, 0, -1):
        values = []
        for frame in range(count):
            rotation = ((0., -1., 0., 1., 0., 0., 0., 0., 1.) if frame == 1
                        else (1., 0., 0., 0., 1., 0., 0., 0., 1.))
            values.append(struct.pack("<12f", limb + .5, frame + .25, -2., *rotation))
        records.append(struct.pack("<ii", limb, count) + b"".join(values))
    records.append(struct.pack("<ii", -1, count) + struct.pack(f"<{count}i", *flags))
    return chunk(b"3AN ", header + b"".join(records) + struct.pack("<i", 0), number)


def fixture(*, bad_root=False):
    """Two sequences share a single file pin; a second file has deselected data."""
    def container(chunks):
        payload = chunk(b"NOTE", b"original synthetic batch input", 0) + b"".join(chunks)
        return b"WIFF" + struct.pack(">I", len(payload)) + payload

    files = {
        "anim/Multi.WAN": container([
            animation(b"alpha.bip", (0, 0x4000, 0, 0x4000), 1),
            animation(b"beta.BIP", (0x4004, 0x4000, 0x4010), 2),
        ]),
        "anim/Single.wan": container([
            animation(b"gamma.bip", (0x4000, 0), 3, bad_root=bad_root),
        ]),
    }
    entries = []
    for name, path, count in (("ALPHA", "anim/Multi.WAN", 4),
                              ("BETA", "anim/Multi.WAN", 3),
                              ("GAMMA", "anim/Single.wan", 2)):
        selected = SELECTED[name]
        entries.append({
            "runtime_name": name, "source_path": path, "source_git_blob_sha1": blob_sha1(files[path]),
            "source_frames": count, "selected_frames": len(selected),
            "source_first": selected[0], "source_last": selected[-1],
        })
    return files, {
        "schema": "original-wan-sequence-catalog/v1",
        "source": {"repository": "synthetic/animation", "commit": "a" * 40},
        "format_source": copy.deepcopy(batch.reader.wan.FORMAT_SOURCE),
        "sequence_count": 3, "source_frame_count": 9, "selected_frame_count": 6,
        "clips": entries,
    }


class CatalogTests(unittest.TestCase):
    def assert_expanded_catalog(self, catalog, digest):
        self.assertEqual(catalog["verified_inventory_sha256"],
                         "18af668aa0ff97985f91030f6c17d0552a9630fe25d40893dc5a7ac8381b7cdf")
        self.assertNotIn("checkpoint", catalog)
        self.assertEqual((catalog["sequence_count"], catalog["source_frame_count"],
                          catalog["selected_frame_count"]), (117, 13514, 10288))
        self.assertEqual(catalog["source"], {
            "repository": "historicalsource/nflblitz2000",
            "commit": "89f840d58d152a2a9f230cb8d0cdffbd554c8ded",
        })
        self.assertEqual(catalog["format_source"], batch.reader.wan.FORMAT_SOURCE)
        self.assertEqual(len({e["source_path"] for e in catalog["clips"]}), 17)
        self.assertEqual([e["runtime_name"] for e in catalog["clips"]],
                         sorted(e["runtime_name"] for e in catalog["clips"]))
        for entry in catalog["clips"]:
            self.assertEqual(set(entry), batch.ENTRY_FIELDS)
        projection = json.dumps(catalog["clips"], sort_keys=True, separators=(",", ":")).encode()
        self.assertEqual(hashlib.sha256(projection).hexdigest(),
                         "7c8771031ae5c164356c58d5cf82163e6f47683213c8e96ca1fb2dc688020169")
        self.assertEqual(digest, "421e3adac3ba3dce186293de69f631abcee5559d89f3ed38d7781a703ea1bb74")

    def test_expanded_catalog_is_exact_metadata_only_requirements_not_a_gameplay_freeze(self):
        self.assert_expanded_catalog(*batch.load_catalog(BALL_CATALOG))

    def test_expanded_catalog_preserves_all_56_default_entries_and_default_bytes(self):
        default, digest = batch.load_catalog(batch.CATALOG)
        self.assertEqual(digest, "c87b485de70bacaba9d2dd83bb2ae6bbfc59b90885e2aaa0f15e3133c7c3fa43")
        expanded, _ = batch.load_catalog(BALL_CATALOG)
        by_name = {e["runtime_name"]: e for e in expanded["clips"]}
        self.assertEqual(len(by_name.keys() - {e["runtime_name"] for e in default["clips"]}), 61)
        for entry in default["clips"]:
            self.assertEqual(by_name[entry["runtime_name"]], entry, entry["runtime_name"])
        self.assertEqual(by_name["CATCH_2F"]["source_path"], "anim/CATCH.WAN")
        self.assertEqual(by_name["C_BICYCF"]["source_path"], "anim/catches2.wan")
        self.assertEqual(tuple(by_name["DLINE_03"][k] for k in
                               ("source_frames", "selected_frames", "source_first", "source_last")),
                         (287, 144, 0, 286))
        self.assertEqual(tuple(by_name["SPIN"][k] for k in
                               ("source_frames", "selected_frames", "source_first", "source_last")),
                         (56, 28, 1, 55))

    def test_mutated_expanded_metadata_fails_the_same_public_catalog_gate(self):
        original = json.loads(BALL_CATALOG.read_bytes())
        self.assert_expanded_catalog(*batch.load_catalog(BALL_CATALOG))
        for mutate in (
                lambda c: c.update(verified_inventory_sha256="0" * 64),
                lambda c: c["source"].update(commit="0" * 40),
                lambda c: c["clips"][0].update(source_git_blob_sha1="0" * 40),
                lambda c: c["clips"][0].update(source_path="anim/Other.wan"),
                lambda c: c["clips"][0].update(runtime_name="OTHER"),
                lambda c: c["clips"][6].update(source_first=1),
                lambda c: c["clips"][6].update(rotations_xyzw=[])):
            changed = copy.deepcopy(original)
            mutate(changed)
            with self.subTest(mutation=changed != original):
                with mock.patch.object(batch, "_read_bytes", return_value=json.dumps(changed).encode()):
                    with self.assertRaises((batch.WanError, AssertionError)):
                        self.assert_expanded_catalog(*batch.load_catalog(BALL_CATALOG))

    def test_published_catalog_is_only_frozen_pins_names_counts_and_boundaries(self):
        catalog, digest = batch.load_catalog(batch.CATALOG)
        self.assertEqual(catalog["checkpoint"], "edc6193")
        self.assertEqual(catalog["verified_inventory_sha256"],
                         "bb3f1a4fe92a9bce64dc06cf51a462928eb7da7512f01747e8c9850ed6b76aa3")
        self.assertEqual(catalog["sequence_count"], 56)
        self.assertEqual(catalog["source_frame_count"], 4046)
        self.assertEqual(catalog["selected_frame_count"], 3731)
        self.assertEqual(catalog["source"], {
            "repository": "historicalsource/nflblitz2000",
            "commit": "89f840d58d152a2a9f230cb8d0cdffbd554c8ded",
        })
        self.assertEqual(catalog["format_source"], batch.reader.wan.FORMAT_SOURCE)
        self.assertEqual(len({e["source_path"] for e in catalog["clips"]}), 11)
        self.assertEqual([e["runtime_name"] for e in catalog["clips"]],
                         sorted(e["runtime_name"] for e in catalog["clips"]))
        for entry in catalog["clips"]:
            self.assertEqual(set(entry), batch.ENTRY_FIELDS)
            self.assertFalse(Path(entry["source_path"]).is_absolute())
        by_name = {e["runtime_name"]: e for e in catalog["clips"]}
        self.assertEqual(by_name["FALL_04"]["source_path"], "anim/FALL.WAN")
        self.assertEqual((by_name["FALL_04"]["source_frames"],
                          by_name["FALL_04"]["selected_frames"],
                          by_name["FALL_04"]["source_first"],
                          by_name["FALL_04"]["source_last"]), (150, 61, 28, 148))
        self.assertEqual(by_name["2P_C"]["source_path"], "anim/Line.wan")
        self.assertEqual(by_name["2P_C2"]["source_path"], "anim/line99.wan")
        frozen_projection = json.dumps(catalog["clips"], sort_keys=True, separators=(",", ":")).encode()
        self.assertEqual(hashlib.sha256(frozen_projection).hexdigest(),
                         "fa27736e27ba2684e7f55c024db4272ea95006feaebd9a156aae68c895d31c33")
        self.assertEqual(digest, hashlib.sha256(batch.CATALOG.read_bytes()).hexdigest())


class BatchTests(unittest.TestCase):
    def setUp(self):
        self.directory = batch.reader.wan.SCRATCH / ("animation-batch-test-" + uuid.uuid4().hex)
        self.directory.mkdir(parents=True)
        self.addCleanup(shutil.rmtree, self.directory)
        self.source = self.directory / "source-one"
        self.destination = self.directory / "bundle"
        self.catalog_path = self.directory / "catalog.json"
        self.files, self.catalog = fixture()
        self.write_fixture()

    def write_fixture(self):
        for relative, data in self.files.items():
            path = self.source / relative
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(data)
        self.write_catalog()

    def write_catalog(self, catalog=None):
        self.catalog_path.write_text(json.dumps(self.catalog if catalog is None else catalog),
                                     encoding="utf-8")

    def prepare(self, destination=None, **kwargs):
        return batch.prepare_bundle(self.source, destination or self.destination,
                                    self.catalog_path, **kwargs)

    def snapshot(self, destination=None):
        root = destination or self.destination
        return {p.relative_to(root).as_posix(): p.read_bytes()
                for p in root.rglob("*") if p.is_file()}

    def cli(self, *extra):
        stdout, stderr = io.StringIO(), io.StringIO()
        with contextlib.redirect_stdout(stdout), contextlib.redirect_stderr(stderr):
            status = batch.main([
                "--source-root", str(self.source), "--destination", str(self.destination),
                "--catalog", str(self.catalog_path), *extra,
            ])
        return status, stdout.getvalue(), stderr.getvalue()

    def assert_fixture_output(self, destination):
        inventory = json.loads((destination / batch.MANIFEST).read_bytes())
        self.assertEqual(inventory["catalog_sha256"],
                         hashlib.sha256(self.catalog_path.read_bytes()).hexdigest(), "catalog_sha256")
        self.assertEqual(len(inventory["clips"]), len(self.catalog["clips"]), "output clip count")
        for entry, expected in zip(inventory["clips"], self.catalog["clips"]):
            name = expected["runtime_name"]
            for field in batch.ENTRY_FIELDS:
                self.assertEqual(entry[field], expected[field], field)
            self.assertEqual(entry["path"], "clips/" + name + ".json", "path")
            self.assertEqual(entry["selected_source_frame_indices"], SELECTED[name],
                             "selected_source_frame_indices")
            encoded = (destination / entry["path"]).read_bytes()
            self.assertEqual(entry["sha256"], hashlib.sha256(encoded).hexdigest(), "sha256")
            clip = json.loads(encoded)
            self.assertEqual(clip["sequence"]["runtime_name"], name, "runtime_name")
            self.assertEqual(clip["sequence"]["selected_source_frame_indices"], SELECTED[name],
                             "selected_source_frame_indices")
            self.assertEqual([f["source_frame_index"] for f in clip["frames"]],
                             SELECTED[name], "source_frame_index")
            self.assertEqual([f["exported_frame_index"] for f in clip["frames"]],
                             list(range(len(SELECTED[name]))), "exported_frame_index")
            self.assertEqual(clip["provenance"]["local_input"], expected["source_path"], "local_input")

    def test_explicit_catalog_does_not_change_the_implicit_frozen_default(self):
        status, output, error = self.cli()
        self.assertEqual(status, 0, error)
        self.assertEqual(json.loads(output)["clips"], 3)
        self.assert_fixture_output(self.destination)
        stdout, stderr = io.StringIO(), io.StringIO()
        default_destination = self.directory / "implicit-default"
        with mock.patch.object(batch, "load_catalog", wraps=batch.load_catalog) as load:
            with contextlib.redirect_stdout(stdout), contextlib.redirect_stderr(stderr):
                status = batch.main(["--source-root", str(self.source),
                                     "--destination", str(default_destination)])
        load.assert_called_once_with(batch.CATALOG)
        self.assertEqual(status, 2)
        self.assertIn("Line.wan", stderr.getvalue())
        self.assertFalse(default_destination.exists())
        self.assertEqual(hashlib.sha256(batch.CATALOG.read_bytes()).hexdigest(),
                         "c87b485de70bacaba9d2dd83bb2ae6bbfc59b90885e2aaa0f15e3133c7c3fa43")

    def test_another_catalog_cannot_reuse_a_bundle_even_with_identical_sequences(self):
        original_result = self.prepare()
        original = self.snapshot()
        other_path = self.directory / "other-catalog.json"
        other = copy.deepcopy(self.catalog)
        other["verified_inventory_sha256"] = "b" * 64
        other_path.write_text(json.dumps(other), encoding="utf-8")
        with self.assertRaisesRegex(batch.WanError, "identity mismatch: export-inventory.json"):
            batch.prepare_bundle(self.source, self.destination, other_path, reuse=True)
        self.assertEqual(self.snapshot(), original)
        other_destination = self.directory / "other-bundle"
        other_result = batch.prepare_bundle(self.source, other_destination, other_path)
        self.assertNotEqual(other_result["manifest_sha256"], original_result["manifest_sha256"])
        other_outputs = self.snapshot(other_destination)
        for path, data in original.items():
            if path != batch.MANIFEST:
                self.assertEqual(other_outputs[path], data, path)
        other["clips"] = [other["clips"][0], other["clips"][2]]
        other.update(sequence_count=2, source_frame_count=6, selected_frame_count=3)
        other_path.write_text(json.dumps(other), encoding="utf-8")
        with self.assertRaises(batch.WanError):
            batch.prepare_bundle(self.source, self.destination, other_path, reuse=True)
        self.assertEqual(self.snapshot(), original)

    def test_catalog_symlinks_and_parent_traversal_are_rejected_before_output(self):
        alias = self.directory / "catalog-alias.json"
        alias.symlink_to(self.catalog_path)
        parent_alias = self.directory / "catalog-parent-alias"
        parent_alias.symlink_to(self.directory, target_is_directory=True)
        for path, message in (
                (alias, "symlinks"),
                (parent_alias / "catalog.json", "symlinks"),
                (self.directory / "unused" / ".." / "catalog.json", "parent traversal")):
            with self.subTest(path=path), self.assertRaisesRegex(batch.WanError, message):
                batch.prepare_bundle(self.source, self.destination, path)
            self.assertFalse(self.destination.exists())

    def test_wrong_source_path_case_fails_visibly_without_normalizing_or_writing(self):
        for entry in self.catalog["clips"][:2]:
            entry["source_path"] = "anim/multi.wan"
        self.write_catalog()
        status, _, error = self.cli()
        self.assertEqual(status, 2)
        self.assertIn("anim/multi.wan", error)
        self.assertFalse(self.destination.exists())
        self.assertEqual((self.source / "anim/Multi.WAN").read_bytes(), self.files["anim/Multi.WAN"])

    def test_runtime_alias_preserves_original_synthetic_authoring_name_case_and_sparse_map(self):
        authoring_name = b"alpha_balls_123456.b"
        payload = animation(authoring_name, (0, 0x4000, 0, 0x4000, 0x4000, 0), 7)
        data = b"WIFF" + struct.pack(">I", len(payload)) + payload
        self.files = {"anim/Alias.WaN": data}
        self.catalog["clips"] = [{
            "runtime_name": "ALPHA_BALLS_123", "source_path": "anim/Alias.WaN",
            "source_git_blob_sha1": blob_sha1(data), "source_frames": 6,
            "selected_frames": 3, "source_first": 1, "source_last": 4,
        }]
        self.catalog.update(sequence_count=1, source_frame_count=6, selected_frame_count=3)
        self.write_fixture()
        self.prepare()
        manifest = json.loads((self.destination / batch.MANIFEST).read_bytes())
        entry = manifest["clips"][0]
        clip = json.loads((self.destination / entry["path"]).read_bytes())
        self.assertEqual(clip["sequence"]["authoring_name"], authoring_name.decode("ascii"))
        self.assertEqual(clip["sequence"]["runtime_name"], "ALPHA_BALLS_123")
        self.assertEqual(clip["provenance"]["local_input"], "anim/Alias.WaN")
        self.assertEqual(entry["selected_source_frame_indices"], [1, 3, 4])
        self.assertEqual(clip["sequence"]["selected_source_frame_indices"], [1, 3, 4])
        for ordinal, source_frame in enumerate((1, 3, 4)):
            frame = clip["frames"][ordinal]
            self.assertEqual((frame["exported_frame_index"], frame["source_frame_index"]),
                             (ordinal, source_frame))
            self.assertEqual(frame["root_translation"]["authoring_xyz"],
                             [source_frame + .25, 14., -source_frame - .5])
            self.assertEqual(frame["root_translation"]["scaled_runtime_xyz"],
                             [v * .12898 for v in [source_frame + .25, 14., -source_frame - .5]])
            self.assertEqual(len(frame["rotations_xyzw"]), 15)

    def test_generated_artifact_mutations_fail_the_same_identity_selection_and_hash_gate(self):
        self.prepare()
        self.assert_fixture_output(self.destination)
        for field in ("catalog_sha256", "source_git_blob_sha1", "source_path",
                      "selected_source_frame_indices", "path", "sha256", "runtime_name",
                      "source_frame_index"):
            destination = self.directory / ("mutated-" + field)
            shutil.copytree(self.destination, destination)
            manifest_path = destination / batch.MANIFEST
            manifest = json.loads(manifest_path.read_bytes())
            entry = manifest["clips"][1]
            if field == "catalog_sha256":
                manifest[field] = "0" * 64
            elif field == "source_git_blob_sha1":
                entry[field] = "0" * 40
            elif field == "source_path":
                entry[field] = "anim/Other.wan"
            elif field == "selected_source_frame_indices":
                entry[field] = [0, 2, 2]
            elif field == "path":
                entry[field] = "clips/OTHER.json"
            elif field == "sha256":
                entry[field] = "0" * 64
            else:
                clip_path = destination / entry["path"]
                clip = json.loads(clip_path.read_bytes())
                if field == "runtime_name":
                    clip["sequence"][field] = "OTHER"
                else:
                    clip["frames"].reverse()
                clip_path.write_text(batch.reader.encode_clip(clip), encoding="utf-8")
                entry["sha256"] = hashlib.sha256(clip_path.read_bytes()).hexdigest()
            manifest_path.write_text(batch.reader.encode_clip(manifest), encoding="utf-8")
            before = self.snapshot(destination)
            with self.subTest(field=field):
                with self.assertRaisesRegex(AssertionError, field):
                    self.assert_fixture_output(destination)
                with self.assertRaises(batch.WanError):
                    self.prepare(destination, reuse=True)
                self.assertEqual(self.snapshot(destination), before)

    def test_multiple_sequences_share_one_read_and_preserve_every_selected_map(self):
        with mock.patch.object(batch, "_read_source", wraps=batch._read_source) as read_source:
            with mock.patch.object(batch.reader, "full_clip", wraps=batch.reader.full_clip) as decode:
                result = self.prepare()
        self.assertEqual(read_source.call_count, 2)
        self.assertEqual([call.args[1] for call in read_source.call_args_list],
                         ["anim/Multi.WAN", "anim/Single.wan"])
        self.assertEqual(decode.call_count, 3)
        self.assertIs(decode.call_args_list[0].args[0], decode.call_args_list[1].args[0])
        self.assertEqual((result["clips"], result["source_files"], result["source_frames"],
                          result["selected_frames"], result["reused"]), (3, 2, 9, 6, False))
        manifest_bytes = (self.destination / batch.MANIFEST).read_bytes()
        self.assertEqual(hashlib.sha256(manifest_bytes).hexdigest(), result["manifest_sha256"])
        inventory = json.loads(manifest_bytes)
        self.assertEqual(inventory["schema"], "original-wan-export-inventory/v1")
        self.assertEqual(inventory["source"], self.catalog["source"])
        self.assertEqual(inventory["catalog_sha256"], hashlib.sha256(self.catalog_path.read_bytes()).hexdigest())
        self.assertEqual([e["runtime_name"] for e in inventory["clips"]], ["ALPHA", "BETA", "GAMMA"])
        for entry, expected in zip(inventory["clips"], self.catalog["clips"]):
            for field in batch.ENTRY_FIELDS:
                self.assertEqual(entry[field], expected[field], (entry["runtime_name"], field))
            self.assertEqual(entry["path"], "clips/" + entry["runtime_name"] + ".json")
            encoded = (self.destination / entry["path"]).read_bytes()
            self.assertEqual(entry["sha256"], hashlib.sha256(encoded).hexdigest())
            decoded = json.loads(encoded)
            source_data = self.files[entry["source_path"]]
            self.assertEqual(decoded["provenance"]["local_input"], entry["source_path"])
            self.assertEqual(decoded["provenance"]["commit"], "a" * 40)
            self.assertEqual(decoded["provenance"]["format_source"], self.catalog["format_source"])
            self.assertEqual(decoded["provenance"]["git_blob_sha1"], blob_sha1(source_data))
            self.assertEqual(decoded["provenance"]["sha256"], hashlib.sha256(source_data).hexdigest())
            self.assertEqual(entry["source_sha256"], decoded["provenance"]["sha256"])
            selected = SELECTED[entry["runtime_name"]]
            self.assertEqual(entry["selected_source_frame_indices"], selected)
            self.assertEqual(decoded["sequence"]["selected_source_frame_indices"], selected)
            self.assertEqual([f["source_frame_index"] for f in decoded["frames"]], selected)
            self.assertEqual([f["exported_frame_index"] for f in decoded["frames"]], list(range(len(selected))))
            for frame, source_index in zip(decoded["frames"], selected):
                self.assertEqual(len(frame["rotations_xyzw"]), 15)
                self.assertEqual(frame["root_translation"]["authoring_xyz"][0], source_index + .25)
                self.assertEqual(frame["root_translation"]["scaled_runtime_xyz"][0],
                                 (source_index + .25) * .12898)
            self.assertNotIn(str(self.source).encode(), encoded)
            self.assertTrue(all(value is None for key, value in decoded["playback"].items()
                                if key != "status"))
        self.assertEqual(set(self.snapshot()), {
            batch.MANIFEST, "clips/ALPHA.json", "clips/BETA.json", "clips/GAMMA.json"})

    def test_actual_cli_is_standalone_and_deterministic_from_different_physical_roots(self):
        args = ["--source-root", str(self.source), "--destination", str(self.destination),
                "--catalog", str(self.catalog_path)]
        process = subprocess.run([sys.executable, "-B", str(MODULE_PATH), *args], cwd=self.directory,
                                 capture_output=True, text=True, check=False)
        self.assertEqual(process.returncode, 0, process.stderr)
        first = self.snapshot()
        second_source = self.directory / "different-physical-root"
        shutil.copytree(self.source, second_source)
        second_destination = self.directory / "different-bundle"
        args[1], args[3] = str(second_source), str(second_destination)
        process_two = subprocess.run([sys.executable, "-B", str(MODULE_PATH), *args], cwd=second_source,
                                     capture_output=True, text=True, check=False)
        self.assertEqual(process_two.returncode, 0, process_two.stderr)
        self.assertEqual(self.snapshot(second_destination), first)
        self.assertEqual(json.loads(process.stdout), json.loads(process_two.stdout))
        self.assertEqual(self.cli("--reuse")[0], 0)
        self.assertEqual(self.snapshot(), first)

    def test_wrong_blob_or_changed_source_creates_no_bundle(self):
        for entry in self.catalog["clips"][:2]:
            entry["source_git_blob_sha1"] = "0" * 40
        self.write_catalog()
        status, _, error = self.cli()
        self.assertEqual(status, 2)
        self.assertIn("Git blob SHA-1", error)
        self.assertFalse(self.destination.exists())
        self.files, self.catalog = fixture()
        self.write_fixture()
        path = self.source / "anim/Single.wan"
        path.write_bytes(path.read_bytes() + b"changed")
        with self.assertRaisesRegex(batch.WanError, "Git blob SHA-1"):
            self.prepare()
        self.assertFalse(self.destination.exists())

    def test_duplicate_sequences_conflicting_pins_and_wrong_format_are_rejected(self):
        for mutation, message in (
                (lambda c: c["clips"][1].update(runtime_name="ALPHA"), "duplicate sequence"),
                (lambda c: c["clips"][1].update(source_git_blob_sha1="0" * 40), "conflicting source pins"),
                (lambda c: c["format_source"].update(commit="0" * 40), "format source"),
                (lambda c: c["source"].update(commit="bad"), "source commit"),
                (lambda c: c.update(sequence_count=56), "sequence_count"),
                (lambda c: c["clips"][0].update(frames=[]), "only names"),
                (lambda c: c["clips"][0].update(path="../output.json"), "only names")):
            catalog = copy.deepcopy(self.catalog)
            mutation(catalog)
            self.write_catalog(catalog)
            with self.subTest(message=message), self.assertRaisesRegex(batch.WanError, message):
                self.prepare()
            self.assertFalse(self.destination.exists())

    def test_unsafe_names_paths_nonfinite_and_impossible_counts_are_rejected(self):
        cases = [
            ("runtime_name", value) for value in ("../EVIL", "/EVIL", "bad\\name", "lower", "", "X:Y")
        ] + [
            ("source_path", value) for value in (
                "../evil.wan", "/evil.wan", "anim\\Multi.WAN", "anim//Multi.WAN",
                "anim/./Multi.WAN", "anim/../Multi.WAN", "anim/drive:X.wan", "anim/file.wan.")
        ] + [
            ("source_frames", 1025), ("source_frames", True), ("source_frames", 0),
            ("selected_frames", 0), ("source_first", -1), ("source_last", 100),
            ("source_frames", float("nan")),
        ]
        for key, value in cases:
            catalog = copy.deepcopy(self.catalog)
            catalog["clips"][0][key] = value
            self.write_catalog(catalog)
            with self.subTest(key=key, value=value), self.assertRaises(batch.WanError):
                self.prepare()
            self.assertFalse(self.destination.exists())
        text = json.dumps(self.catalog).replace('"schema":', '"schema":"duplicate","schema":', 1)
        self.catalog_path.write_text(text, encoding="utf-8")
        with self.assertRaisesRegex(batch.WanError, "duplicate JSON key"):
            self.prepare()

    def test_source_counts_names_and_boundaries_must_match_actual_decoded_bytes(self):
        for mutation in (
                lambda c: c["clips"][0].update(source_first=0),
                lambda c: (c["clips"][0].update(source_frames=5), c.update(source_frame_count=10)),
                lambda c: c["clips"][0].update(runtime_name="MISSING")):
            catalog = copy.deepcopy(self.catalog)
            mutation(catalog)
            self.write_catalog(catalog)
            with self.assertRaises(batch.WanError):
                self.prepare()
            self.assertFalse(self.destination.exists())

    def test_deselected_nonfinite_input_is_rejected_before_any_output(self):
        self.files, self.catalog = fixture(bad_root=True)
        self.write_fixture()
        with self.assertRaisesRegex(batch.WanError, "non-finite root"):
            self.prepare()
        self.assertFalse(self.destination.exists())

    def test_destination_bounds_and_existing_directories_never_overwrite(self):
        for destination in (
                MODULE_PATH.parent / "must-not-be-a-batch-bundle",
                batch.reader.wan.SCRATCH, self.source / "bundle", self.directory,
                self.directory / "unused" / ".." / "bundle"):
            with self.subTest(destination=destination), self.assertRaises(batch.WanError):
                self.prepare(destination)
        self.assertFalse(self.destination.exists())
        self.destination.mkdir()
        unrelated = self.destination / "unrelated"
        unrelated.write_bytes(b"keep this")
        for reuse in (False, True):
            with self.subTest(reuse=reuse), self.assertRaises(batch.WanError):
                self.prepare(reuse=reuse)
            self.assertEqual(unrelated.read_bytes(), b"keep this")
        self.assertEqual({p.name for p in self.destination.iterdir()}, {"unrelated"})

    def test_source_and_destination_symlinks_and_file_aliases_are_rejected(self):
        real = self.directory / "real"
        real.mkdir()
        link = self.directory / "link"
        link.symlink_to(real, target_is_directory=True)
        for destination in (link, link / "bundle"):
            with self.subTest(destination=destination), self.assertRaisesRegex(batch.WanError, "symlinks"):
                self.prepare(destination, reuse=True)
        self.assertEqual(list(real.iterdir()), [])
        source_alias = self.directory / "source-alias"
        source_alias.symlink_to(self.source, target_is_directory=True)
        with self.assertRaisesRegex(batch.WanError, "symlinks"):
            batch.prepare_bundle(source_alias, self.destination, self.catalog_path)
        source_file = self.source / "anim/Multi.WAN"
        outside_source = self.directory / "moved-synthetic.WAN"
        source_file.rename(outside_source)
        source_file.symlink_to(outside_source)
        with self.assertRaisesRegex(batch.WanError, "symlinks"):
            self.prepare()
        self.assertEqual(outside_source.read_bytes(), self.files["anim/Multi.WAN"])
        alias = self.directory / "file-alias"
        os.link(outside_source, alias)
        with self.assertRaises(batch.WanError):
            self.prepare(alias, reuse=True)
        self.assertEqual(alias.read_bytes(), self.files["anim/Multi.WAN"])
        self.assertFalse(self.destination.exists())

    def test_reuse_is_explicit_and_validates_entire_bundle_without_writes(self):
        first_result = self.prepare()
        original = self.snapshot()
        stats = {p: (p.stat().st_mtime_ns, p.stat().st_ino)
                 for p in self.destination.rglob("*")}
        with self.assertRaisesRegex(batch.WanError, "already exists"):
            self.prepare()
        second_result = self.prepare(reuse=True)
        self.assertTrue(second_result["reused"])
        self.assertEqual(second_result["manifest_sha256"], first_result["manifest_sha256"])
        self.assertEqual(self.snapshot(), original)
        for path, expected in stats.items():
            self.assertEqual((path.stat().st_mtime_ns, path.stat().st_ino), expected)

    def test_reuse_rejects_corrupt_missing_extra_or_symlinked_artifacts_without_repair(self):
        def changed_manifest(root):
            path = root / batch.MANIFEST
            inventory = json.loads(path.read_bytes())
            inventory["clips"][0]["sha256"] = "0" * 64
            path.write_text(batch.reader.encode_clip(inventory), encoding="utf-8")

        def symlink_clip(root):
            path = root / "clips/ALPHA.json"
            target = self.directory / "synthetic-clip-alias.json"
            target.write_bytes(path.read_bytes())
            path.unlink()
            path.symlink_to(target)

        mutations = (
            lambda root: (root / "clips/ALPHA.json").write_bytes(b"corrupt"),
            changed_manifest,
            lambda root: (root / "clips/BETA.json").unlink(),
            lambda root: (root / "unrelated").write_bytes(b"keep"),
            lambda root: (root / "unrelated-directory").mkdir(),
            symlink_clip,
        )
        for index, mutate in enumerate(mutations):
            destination = self.directory / f"bundle-{index}"
            self.prepare(destination)
            mutate(destination)
            before = self.snapshot(destination)
            paths = {p.relative_to(destination).as_posix() for p in destination.rglob("*")}
            with self.subTest(mutation=index), self.assertRaises(batch.WanError):
                self.prepare(destination, reuse=True)
            self.assertEqual(self.snapshot(destination), before)
            self.assertEqual({p.relative_to(destination).as_posix() for p in destination.rglob("*")}, paths)


if __name__ == "__main__":
    unittest.main()
