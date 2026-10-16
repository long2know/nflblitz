#!/usr/bin/env python3
"""Asset-free profile/copy guards; no Unity, original art, or reference arrays."""

from contextlib import redirect_stderr
import io
import json
from pathlib import Path
import shutil
import subprocess
import sys
from types import SimpleNamespace
import unittest
from unittest import mock
import uuid

import create_preview_project as preview
import prepare_environment as env
import run_reader_checks as reader


PROJECT = Path(__file__).absolute().parents[2]


class ReaderProfileTests(unittest.TestCase):
    def parse_rejected(self, arguments):
        with redirect_stderr(io.StringIO()), self.assertRaises(SystemExit) as error:
            reader.parse_arguments(arguments)
        self.assertEqual(error.exception.code, 2)

    def test_profile_is_explicit_and_mutually_exclusive(self):
        self.parse_rejected([])
        self.parse_rejected(["--synthetic-only", "--bundle-root", "bundle"])
        self.parse_rejected(["--bundle-root"])

    def test_each_profile_retains_explicit_reader_mutation_option(self):
        synthetic = reader.parse_arguments(["--synthetic-only", "--reader-source", "mutant.cs"])
        self.assertTrue(synthetic.synthetic_only)
        self.assertIsNone(synthetic.bundle_root)
        self.assertEqual(synthetic.reader_source, Path("mutant.cs"))
        actual = reader.parse_arguments(["--bundle-root", "bundle", "--reader-source", "mutant.cs"])
        self.assertFalse(actual.synthetic_only)
        self.assertEqual(actual.bundle_root, Path("bundle"))
        self.assertEqual(actual.reader_source, Path("mutant.cs"))

    def test_synthetic_dispatch_never_reads_original_bundle(self):
        result = SimpleNamespace(returncode=0, stdout="", stderr="")
        with mock.patch.object(reader.env, "write_bundle") as write, \
                mock.patch.object(reader.env, "read_file", side_effect=AssertionError("Synthetic must not read original files.")), \
                mock.patch.object(reader.subprocess, "run", return_value=result) as run:
            self.assertEqual(reader.main(["--synthetic-only"]), 0)
        self.assertEqual(run.call_count, 2)
        self.assertEqual(run.call_args.args[0][-1], "--synthetic-only")
        project_text = write.call_args.args[2]["ReaderChecks.csproj"].decode()
        self.assertIn("Assets/Scripts/AssetImport/SourceEnvironmentData.cs", project_text)
        self.assertIn("tools/environments/ReaderChecks.cs", project_text)

    def test_missing_actual_bundle_fails_before_compilation_without_fallback(self):
        with redirect_stderr(io.StringIO()) as stderr, \
                mock.patch.object(reader.env, "read_file", side_effect=env.EnvironmentError("Missing required original input: environment.json")), \
                mock.patch.object(reader.subprocess, "run") as run, \
                mock.patch.object(reader.env, "write_bundle") as write:
            self.assertEqual(reader.main(["--bundle-root", "absent-original-bundle"]), 1)
        self.assertIn("Actual source environment reader profile rejected", stderr.getvalue())
        self.assertIn("environment.json", stderr.getvalue())
        run.assert_not_called()
        write.assert_not_called()

    def test_actual_dispatch_keeps_root_sha_and_propagates_reader_failure(self):
        # Routing only: process execution is mocked, not called an original-data pass.
        manifest = b'{"original-synthetic-routing-only":true}'
        build = SimpleNamespace(returncode=0, stdout="", stderr="")
        failure = SimpleNamespace(returncode=19, stdout="", stderr="")
        with mock.patch.object(reader.env, "read_file", return_value=manifest), \
                mock.patch.object(reader.env, "write_bundle"), \
                mock.patch.object(reader.subprocess, "run", side_effect=[build, failure]) as run:
            self.assertEqual(reader.main(["--bundle-root", "explicit-bundle"]), 19)
        command = run.call_args.args[0]
        self.assertEqual(command[-2:], [str(Path("explicit-bundle").absolute()), env.digest(manifest)])
        self.assertNotIn("--synthetic-only", command)


class PreviewGuardTests(unittest.TestCase):
    def setUp(self):
        self.work = PROJECT / ".squad/.scratch" / ("world-publication-guards-" + uuid.uuid4().hex)
        self.project = self.work / "clean-checkout"
        self.project.mkdir(parents=True, exist_ok=False)
        # Only the six actual frozen project sources/metas are copied; no source art.
        self.metadata = json.loads((PROJECT / preview.DEFAULT_SNAPSHOT).read_text())
        self.inputs = {}
        for record in self.metadata["files"]:
            relative = record["path"]
            data = env.read_file(PROJECT, relative)
            self.assertEqual(env.digest(data), record["sha256"], relative)
            destination = self.project / relative
            destination.parent.mkdir(parents=True, exist_ok=True)
            destination.write_bytes(data)
            self.inputs[relative] = data
        self.snapshot_path = self.project / preview.DEFAULT_SNAPSHOT
        self.snapshot_path.parent.mkdir(parents=True, exist_ok=True)
        self.save_metadata()
        self.destination = self.project / "Build/world-preview/native-project"

    def tearDown(self):
        shutil.rmtree(self.work)

    def save_metadata(self):
        self.snapshot_path.write_bytes(env.json_bytes(self.metadata))

    def test_default_public_snapshot_is_exactly_six_sources_and_existing_metas(self):
        expected = {relative for source in preview.SOURCE_FILES for relative in (source, source + ".meta")}
        self.assertEqual(set(self.inputs), expected)
        self.assertEqual(len(self.inputs), 12)
        self.assertTrue(all(set(record) == {"path", "sha256"} for record in self.metadata["files"]))
        self.assertFalse(any("FieldScene" in path or "/Gameplay/" in path for path in self.inputs))
        with mock.patch("subprocess.run", side_effect=AssertionError("No Editor/process may start.")):
            output = preview.create_preview_project(self.project)
        self.assertEqual(output, self.destination)
        for relative, data in self.inputs.items():
            self.assertEqual((output / relative).read_bytes(), data)
            self.assertEqual((self.project / relative).read_bytes(), data)
        receipt = json.loads((output / "preview-source-receipt.json").read_text())
        self.assertEqual(len(receipt["sourceFiles"]), 6)
        self.assertEqual(len(receipt["copiedInputs"]), 12)
        self.assertEqual(receipt["sourceSnapshotProfile"], "public-frozen-default")
        self.assertFalse(receipt["editorStarted"])
        self.assertFalse(receipt["copiedHotGameplayOrAnimationFiles"])
        self.assertFalse((output / "Assets/Imported").exists())
        self.assertFalse((output / "Assets/Scripts/Gameplay").exists())

    def test_missing_default_snapshot_does_not_search_ignored_session_state(self):
        self.snapshot_path.unlink()
        with self.assertRaisesRegex(env.EnvironmentError, "preview_sources.json"):
            preview.create_preview_project(self.project)
        self.assertFalse(self.destination.exists())

    def test_documented_default_cli_works_from_a_clean_checkout_without_session_state(self):
        for name in ("create_preview_project.py", "prepare_environment.py"):
            (self.snapshot_path.parent / name).write_bytes((PROJECT / "tools/environments" / name).read_bytes())
        self.assertFalse((self.project / ".squad").exists())
        process = subprocess.run([sys.executable, "-B", "tools/environments/create_preview_project.py"],
                                 cwd=self.project, capture_output=True, text=True)
        self.assertEqual(process.returncode, 0, process.stdout + process.stderr)
        self.assertIn("no Editor started", process.stdout)
        self.assertFalse((self.project / ".squad").exists())
        receipt = json.loads((self.destination / "preview-source-receipt.json").read_text())
        self.assertEqual(receipt["sourceSnapshotProfile"], "public-frozen-default")
        self.assertEqual(len(receipt["copiedInputs"]), 12)
        for relative, data in self.inputs.items():
            self.assertEqual((self.destination / relative).read_bytes(), data)

    def test_changed_source_fails_before_copy(self):
        for relative in preview.SOURCE_FILES:
            with self.subTest(source=relative):
                (self.project / relative).write_bytes(self.inputs[relative] + b"\n// isolated mutation\n")
                try:
                    with self.assertRaisesRegex(env.EnvironmentError, "World source changed before isolated copy: " + relative):
                        preview.create_preview_project(self.project)
                    self.assertFalse(self.destination.exists())
                finally:
                    (self.project / relative).write_bytes(self.inputs[relative])

    def test_changed_meta_fails_before_copy(self):
        for source in preview.SOURCE_FILES:
            relative = source + ".meta"
            with self.subTest(meta=relative):
                (self.project / relative).write_bytes(self.inputs[relative] + b"\n")
                try:
                    with self.assertRaisesRegex(env.EnvironmentError, "World source changed before isolated copy: " + relative):
                        preview.create_preview_project(self.project)
                    self.assertFalse(self.destination.exists())
                finally:
                    (self.project / relative).write_bytes(self.inputs[relative])

    def test_missing_actual_metadata_is_rejected_even_when_pin_is_present(self):
        relative = preview.SOURCE_FILES[0] + ".meta"
        (self.project / relative).unlink()
        with self.assertRaisesRegex(env.EnvironmentError, relative):
            preview.create_preview_project(self.project)
        self.assertFalse(self.destination.exists())

    def test_missing_pin_and_missing_metadata_are_not_fallbacks(self):
        self.metadata["files"].pop()
        self.save_metadata()
        with self.assertRaisesRegex(env.EnvironmentError, "all six copied C# sources and their metas"):
            preview.create_preview_project(self.project)
        self.assertFalse(self.destination.exists())

    def test_duplicate_and_case_alias_records_rejected(self):
        duplicate = dict(self.metadata["files"][0])
        duplicate["path"] = duplicate["path"].lower()
        self.metadata["files"].append(duplicate)
        self.save_metadata()
        with self.assertRaisesRegex(env.EnvironmentError, "Duplicate/aliased preview source identity"):
            preview.create_preview_project(self.project)
        self.assertFalse(self.destination.exists())

    def test_bad_hash_and_traversal_rejected_before_file_access(self):
        self.metadata["files"][0]["sha256"] = "not-a-sha256"
        self.save_metadata()
        with self.assertRaisesRegex(env.EnvironmentError, "Invalid preview source SHA256"):
            preview.create_preview_project(self.project)
        self.metadata["files"][0]["sha256"] = env.digest(self.inputs[preview.SOURCE_FILES[0]])
        self.metadata["files"][0]["path"] = "../escape.cs"
        self.save_metadata()
        with self.assertRaisesRegex(env.EnvironmentError, "Unsafe relative asset path"):
            preview.create_preview_project(self.project)
        self.assertFalse(self.destination.exists())

    def test_default_snapshot_cannot_expand_to_gameplay_or_other_sources(self):
        self.metadata["files"].append({"path": "Assets/Scripts/Gameplay/not-copied.cs", "sha256": "0" * 64})
        self.save_metadata()
        with self.assertRaisesRegex(env.EnvironmentError, "exactly the six copied sources and their metas"):
            preview.create_preview_project(self.project)
        self.assertFalse(self.destination.exists())

    def test_explicit_legacy_custom_snapshot_and_extra_guards_still_work(self):
        relative = "tools/environments/custom-guard.txt"
        data = b"Original synthetic extra guard, not a copied Unity input.\n"
        (self.project / relative).write_bytes(data)
        custom = dict(self.metadata)
        custom.pop("schema")
        custom["files"] = list(self.metadata["files"]) + [{"path": relative, "sha256": env.digest(data)}]
        custom_path = self.work / "explicit-custom.json"
        custom_path.write_bytes(env.json_bytes(custom))
        output = preview.create_preview_project(self.project, custom_path)
        self.assertFalse((output / relative).exists())
        receipt = json.loads((output / "preview-source-receipt.json").read_text())
        self.assertEqual(receipt["sourceSnapshotProfile"], "explicit-custom")
        self.assertEqual(receipt["sourceSnapshotSha256"], env.digest(custom_path.read_bytes()))
        self.assertEqual(len(receipt["copiedInputs"]), 12)

    def test_partial_custom_snapshot_is_rejected(self):
        custom_path = self.work / "incomplete-custom.json"
        custom = dict(self.metadata, files=self.metadata["files"][:-1])
        custom_path.write_bytes(env.json_bytes(custom))
        with self.assertRaisesRegex(env.EnvironmentError, "all six copied C# sources and their metas"):
            preview.create_preview_project(self.project, custom_path)
        self.assertFalse(self.destination.exists())

    def test_actual_copied_meta_corruption_is_rejected_after_write(self):
        write = preview.env.write_bundle
        relative = preview.SOURCE_FILES[0] + ".meta"

        def corrupt(project, destination, files):
            write(project, destination, files)
            (destination / relative).write_bytes(b"isolated corrupted copied metadata\n")

        with mock.patch.object(preview.env, "write_bundle", side_effect=corrupt), \
                self.assertRaisesRegex(env.EnvironmentError, "Isolated copy did not persist exact bytes: " + relative):
            preview.create_preview_project(self.project)

    def changed_during_copy(self, relative):
        write = preview.env.write_bundle

        def change_source(project, destination, files):
            write(project, destination, files)
            (project / relative).write_bytes(self.inputs[relative] + b"\n")

        with mock.patch.object(preview.env, "write_bundle", side_effect=change_source), \
                self.assertRaisesRegex(env.EnvironmentError, "World source changed during isolated copy: " + relative):
            preview.create_preview_project(self.project)

    def test_source_changed_during_copy_is_rejected(self):
        self.changed_during_copy(preview.SOURCE_FILES[0])

    def test_meta_changed_during_copy_is_rejected(self):
        self.changed_during_copy(preview.SOURCE_FILES[0] + ".meta")

    def test_existing_destination_is_never_overwritten(self):
        preview.create_preview_project(self.project)
        receipt = (self.destination / "preview-source-receipt.json").read_bytes()
        with self.assertRaisesRegex(env.EnvironmentError, "already exists"):
            preview.create_preview_project(self.project)
        self.assertEqual((self.destination / "preview-source-receipt.json").read_bytes(), receipt)


if __name__ == "__main__":
    unittest.main()
