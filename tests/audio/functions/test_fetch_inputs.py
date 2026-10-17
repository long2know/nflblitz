import importlib.util
import json
import os
from pathlib import Path
import shutil
import unittest
from unittest import mock
import uuid

ROOT = Path(__file__).absolute().parents[3]
spec = importlib.util.spec_from_file_location("fetch_function_inputs", ROOT / "tools/audio/fetch_function_inputs.py")
fetch = importlib.util.module_from_spec(spec)
spec.loader.exec_module(fetch)


class FetchInputGuards(unittest.TestCase):
    def setUp(self):
        self.base = fetch.ARTIFACTS / "fetch-input-unit" / uuid.uuid4().hex
        self.base.mkdir(parents=True)
        self.destination = self.base / "destination"
        self.config = json.loads(fetch.PINS.read_text())

    def tearDown(self):
        shutil.rmtree(self.base)
        if self.base.parent.exists() and not any(self.base.parent.iterdir()):
            self.base.parent.rmdir()

    def test_four_frozen_pins_and_canonical_identity(self):
        pins = fetch.input_pins()
        self.assertEqual(set(pins), fetch.NAMES)
        self.assertEqual(len(pins), 4)
        self.assertEqual(fetch.REVISION, self.config["commit"])

    def test_wrong_repository_never_downloads(self):
        for repository in ["HistoricalSource/nflblitz2000", "historicalsource/other", "https://example.invalid"]:
            with self.subTest(repository=repository), mock.patch.object(fetch, "download_bytes") as network:
                with self.assertRaisesRegex(fetch.InputError, "canonical original repository"):
                    fetch.acquire(self.destination, download=True, repository=repository)
                network.assert_not_called()
                self.assertFalse(self.destination.exists())

    def test_branch_uppercase_or_traversing_ref_never_downloads(self):
        for revision in ["main", fetch.REVISION.upper(), fetch.REVISION + "/../main", "", "0" * 40]:
            with self.subTest(revision=revision), mock.patch.object(fetch, "download_bytes") as network:
                with self.assertRaisesRegex(fetch.InputError, "canonical full commit/tree"):
                    fetch.acquire(self.destination, download=True, revision=revision)
                network.assert_not_called()

    def test_metadata_case_and_traversal_rejected(self):
        for name in ["Sounds.c", "../sounds.c", "/sounds.c", "include/../sounds.c", "include\\sndcalls.h", "sounds.c?ref=main"]:
            config = dict(self.config, blobs=dict(self.config["blobs"]))
            config["blobs"][name] = config["blobs"].pop("sounds.c")
            with self.subTest(name=name), self.assertRaises(fetch.InputError):
                fetch.validate_reference(config, fetch.REPOSITORY, fetch.REVISION)

    def test_retargeted_or_malformed_metadata_rejected(self):
        for field, value in [("repository", "other/repository"), ("commit", "main"), ("tree", "0" * 40)]:
            with self.subTest(field=field), self.assertRaises(fetch.InputError):
                fetch.validate_reference(dict(self.config, **{field: value}), fetch.REPOSITORY, fetch.REVISION)
        config = dict(self.config, blobs=dict(self.config["blobs"]))
        config["blobs"]["sounds.c"] = "NOT-A-BLOB"
        with self.assertRaises(fetch.InputError):
            fetch.validate_reference(config, fetch.REPOSITORY, fetch.REVISION)
        changed = self.base / "changed-pins.json"
        changed.write_text(json.dumps(self.config) + "\n")
        with mock.patch.object(fetch, "PINS", changed), self.assertRaisesRegex(fetch.InputError, "identity changed"):
            fetch.input_pins()

    def test_network_requires_explicit_single_mode(self):
        for options in [{}, {"download": True, "source_root": self.base}]:
            with self.subTest(options=options), mock.patch.object(fetch, "download_bytes") as network:
                with self.assertRaisesRegex(fetch.InputError, "exactly one"):
                    fetch.acquire(self.destination, **options)
                network.assert_not_called()

    def test_output_traversal_and_nonignored_root_rejected(self):
        for path in [self.base / ".." / "escape", ROOT / "Assets/fetched-source", fetch.ARTIFACTS]:
            with self.subTest(path=path), mock.patch.object(fetch, "download_bytes") as network:
                with self.assertRaises(fetch.InputError):
                    fetch.acquire(path, download=True)
                network.assert_not_called()

    def test_output_symlink_parent_or_leaf_rejected(self):
        real = self.base / "real"; real.mkdir()
        link = self.base / "link"; link.symlink_to(real, target_is_directory=True)
        for path in [link, link / "child"]:
            with self.subTest(path=path), mock.patch.object(fetch, "download_bytes") as network:
                with self.assertRaisesRegex(fetch.InputError, "Symlink"):
                    fetch.acquire(path, download=True)
                network.assert_not_called()

    def test_partial_destination_never_repairs_or_downloads(self):
        self.destination.mkdir()
        existing = self.destination / "sounds.c"; existing.write_bytes(b"wrong partial cache")
        with mock.patch.object(fetch, "download_bytes") as network:
            with self.assertRaises(fetch.InputError):
                fetch.acquire(self.destination, download=True)
            network.assert_not_called()
        self.assertEqual(existing.read_bytes(), b"wrong partial cache")
        self.assertFalse((self.destination / "include").exists())

    def test_invalid_download_leaves_no_partial_destination(self):
        with mock.patch.object(fetch, "download_bytes", return_value=b"wrong original bytes") as network:
            with self.assertRaisesRegex(fetch.InputError, "byte/blob verification"):
                fetch.acquire(self.destination, download=True)
            self.assertEqual(network.call_count, 1)
        self.assertFalse(self.destination.exists())
        self.assertFalse(list(self.base.glob("*.incoming-*")))

    def test_explicit_invalid_source_never_falls_back(self):
        source = self.base / "missing"
        with mock.patch.object(fetch, "download_bytes") as network:
            with self.assertRaisesRegex(fetch.InputError, "not a directory"):
                fetch.acquire(self.destination, source_root=source)
            network.assert_not_called()
        self.assertFalse(self.destination.exists())

    def test_source_symlink_and_wrong_case_rejected(self):
        source = self.base / "source"; source.mkdir()
        outside = self.base / "data"; outside.write_bytes(b"synthetic bytes")
        (source / "include").mkdir()
        (source / "include/player.h").symlink_to(outside)
        pins = {"include/player.h": fetch.blob_sha1(b"synthetic bytes")}
        with self.assertRaisesRegex(fetch.InputError, "Symlink"):
            fetch.read_cache(source, pins)
        (source / "include/player.h").unlink()
        (source / "include/Player.h").write_bytes(b"synthetic bytes")
        with self.assertRaisesRegex(fetch.InputError, "wrong-case"):
            fetch.read_cache(source, pins)
        root_link = self.base / "source-link"; root_link.symlink_to(source, target_is_directory=True)
        with self.assertRaisesRegex(fetch.InputError, "Symlink"):
            fetch.read_cache(root_link, pins)

    def test_byte_identity_and_bounded_reads(self):
        raw = b"synthetic input\r\n"
        expected = fetch.blob_sha1(raw)
        self.assertEqual(fetch.verify_bytes(raw, expected, "synthetic"), raw)
        for data in [raw.replace(b"\r\n", b"\n"), b"", b"x" * (fetch.MAX_BYTES + 1)]:
            with self.subTest(length=len(data)), self.assertRaisesRegex(fetch.InputError, "byte/blob verification"):
                fetch.verify_bytes(data, expected, "synthetic")

    def test_nonregular_source_fails_before_opening(self):
        source = self.base / "nonregular"; source.mkdir()
        os.mkfifo(source / "pipe.c")
        with self.assertRaisesRegex(fetch.InputError, "regular files"):
            fetch.read_cache(source, {"pipe.c": fetch.blob_sha1(b"synthetic")})

    def test_local_copy_reads_exact_bytes_not_text(self):
        source = self.base / "local"; source.mkdir()
        raw = b"synthetic input\r\n"
        (source / "data.c").write_bytes(raw)
        self.assertEqual(fetch.read_cache(source, {"data.c": fetch.blob_sha1(raw)}), {"data.c": raw})

    def test_existing_destination_is_never_overwritten(self):
        self.destination.mkdir()
        sentinel = self.destination / "sentinel"; sentinel.write_bytes(b"unchanged")
        with self.assertRaisesRegex(fetch.InputError, "never overwritten"):
            fetch.publish(self.destination, {"sounds.c": b"not installed"})
        self.assertEqual(sentinel.read_bytes(), b"unchanged")
        self.assertFalse((self.destination / "sounds.c").exists())


if __name__ == "__main__":
    unittest.main()
