#!/usr/bin/env python3
"""Check local Unity metadata/GUID references without running an importer."""

import argparse
from contextlib import contextmanager
import hashlib
from pathlib import Path
import re
import shutil
import unittest
from unittest.mock import patch
import uuid


ROOT = Path(__file__).resolve().parents[1]
GUID = re.compile(r"[0-9a-fA-F]{32}")
GUID_LINE = re.compile(r"(?m)^guid:[ \t]*([^\r\n]*)")
SCRIPT_KEY = re.compile(r"(?m)^[ \t]*m_Script[ \t]*:")
SCRIPT_MAP = re.compile(r"(?m)^[ \t]*m_Script[ \t]*:[ \t]*\{([^}]*)\}")


def snapshot(assets):
    paths = [assets, *assets.rglob("*")]
    root_meta = assets.with_name(assets.name + ".meta")
    if root_meta.exists() or root_meta.is_symlink():
        paths.append(root_meta)
    result = {}
    for path in sorted(paths):
        info = path.lstat()
        result[path] = (info.st_mode, info.st_size, info.st_mtime_ns)
    return result


def valid_guid(value):
    return GUID.fullmatch(value) is not None and value != "0" * 32


def check_inventory(assets):
    if assets.is_symlink() or not assets.is_dir():
        raise ValueError(f"Assets must be an existing non-symlink directory: {assets}")
    before = snapshot(assets)
    errors, definitions = [], {}
    asset_paths = [path for path in before if not path.name.endswith(".meta")]
    meta_paths = [path for path in before if path.name.endswith(".meta")]
    for path in before:
        if path.is_symlink():
            errors.append(f"{path}: symlinks are not audited")
    for asset in asset_paths:
        meta = asset.with_name(asset.name + ".meta")
        if meta not in before:
            errors.append(f"{asset}: missing metadata {meta.name}")
    for meta in meta_paths:
        if meta.is_symlink():
            continue
        if meta.name == ".meta":
            errors.append(f"{meta}: invalid metadata filename")
            continue
        asset = meta.with_name(meta.name[:-5])
        if asset not in before:
            errors.append(f"{meta}: orphan metadata has no asset")
        content = meta.read_bytes()
        if content and not content.endswith(b"\n"):
            errors.append(f"{meta}: nonempty metadata must end with terminal LF")
        values = GUID_LINE.findall(content.decode("utf-8"))
        if len(values) != 1 or not valid_guid(values[0].strip()):
            errors.append(f"{meta}: expected exactly one nonzero 32 hex GUID, got {values!r}")
            continue
        guid = values[0].strip().lower()
        if guid in definitions:
            errors.append(f"{meta}: duplicate GUID {guid}; also defined by {definitions[guid]}")
        else:
            definitions[guid] = asset
    references = 0
    scenes = [path for path in asset_paths if path.suffix.lower() in (".unity", ".prefab")]
    for scene in scenes:
        if scene.is_symlink():
            continue
        text = scene.read_text(encoding="utf-8")
        maps = SCRIPT_MAP.findall(text)
        if len(maps) != len(SCRIPT_KEY.findall(text)):
            errors.append(f"{scene}: malformed m_Script reference; expected a Unity flow mapping")
        for fields in maps:
            references += 1
            guids = re.findall(r"\bguid:[ \t]*([^,\s}]+)", fields)
            ids = re.findall(r"\bfileID:[ \t]*(-?\d+)", fields)
            if len(guids) != 1 or not valid_guid(guids[0]) or len(ids) != 1 or int(ids[0]) == 0:
                errors.append(f"{scene}: invalid script reference {{{fields}}}")
                continue
            target = definitions.get(guids[0].lower())
            if target is None:
                errors.append(f"{scene}: unresolved script GUID {guids[0]}")
            elif target.is_symlink() or not target.is_file() or target.suffix.lower() != ".cs":
                errors.append(f"{scene}: script GUID {guids[0]} does not resolve to a local .cs asset: {target}")
    if before != snapshot(assets):
        errors.append("Assets changed during metadata check; rerun on an owner-complete stable snapshot")
    if errors:
        raise ValueError("\n".join(errors))
    return {"assets": len(asset_paths), "metadata": len(meta_paths), "guids": len(definitions),
            "scenes_prefabs": len(scenes), "script_references": references}


@contextmanager
def isolated_tree():
    directory = ROOT / "tests" / ".artifacts" / ("unity-metadata-" + uuid.uuid4().hex)
    directory.mkdir(parents=True, exist_ok=False)
    try:
        yield directory
    finally:
        shutil.rmtree(directory)


class UnityMetadataTests(unittest.TestCase):
    def setUp(self):
        self.directory = self.enterContext(isolated_tree())
        self.assets = self.directory / "Assets"
        self.script_meta = self.assets / "Scripts" / "Example.cs.meta"
        self.reference = "  m_Script: {fileID: 11500000, guid: " + "c" * 32 + ", type: 3}\n"
        files = {
            "Assets.meta": "guid: " + "a" * 32 + "\nfolderAsset: yes\n",
            "Assets/Scripts.meta": "guid: " + "b" * 32 + "\nfolderAsset: yes\n",
            "Assets/Scripts/Example.cs": "class Example {}\n",
            "Assets/Scripts/Example.cs.meta": "fileFormatVersion: 2\nguid: " + "c" * 32 + "\nMonoImporter:\n",
            "Assets/Boot.unity": self.reference,
            "Assets/Boot.unity.meta": "guid: " + "d" * 32 + "\n",
            "Assets/Player.prefab": self.reference,
            "Assets/Player.prefab.meta": "guid: " + "e" * 32 + "\n",
        }
        for name, text in files.items():
            path = self.directory / name
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text(text, encoding="utf-8")

    def test_complete_inventory_and_both_reference_types(self):
        self.assertEqual({"assets": 5, "metadata": 5, "guids": 5, "scenes_prefabs": 2,
                          "script_references": 2}, check_inventory(self.assets))

    def test_33_hex_regression_and_invalid_guids_fail(self):
        original = self.script_meta.read_text()
        for value in ("c" * 32 + "0", "g" * 32, "0" * 32, ""):
            with self.subTest(value=value):
                self.script_meta.write_text(original.replace("c" * 32, value))
                with self.assertRaisesRegex(ValueError, r"Example\.cs\.meta.*32 hex GUID"):
                    check_inventory(self.assets)

    def test_valid_guid_without_final_lf_fails_native_eof_regression(self):
        valid = b"fileFormatVersion: 2\nguid: " + b"c" * 32 + b"\nMonoImporter:\n  assetBundleVariant:\n"
        self.script_meta.write_bytes(valid[:-1])
        with self.assertRaisesRegex(ValueError, r"Example\.cs\.meta: nonempty metadata.*terminal LF"):
            check_inventory(self.assets)
        self.script_meta.write_bytes(valid)
        self.assertEqual(5, check_inventory(self.assets)["guids"])
        self.script_meta.write_bytes(valid.replace(b"\n", b"\r\n"))
        self.assertEqual(5, check_inventory(self.assets)["guids"])

    def test_missing_file_metadata_fails(self):
        self.script_meta.unlink()
        with self.assertRaisesRegex(ValueError, r"Example\.cs: missing metadata"):
            check_inventory(self.assets)

    def test_missing_folder_metadata_fails(self):
        (self.assets / "Scripts.meta").unlink()
        with self.assertRaisesRegex(ValueError, r"Scripts: missing metadata"):
            check_inventory(self.assets)

    def test_orphan_metadata_fails(self):
        self.script_meta.with_suffix("").unlink()
        with self.assertRaisesRegex(ValueError, "orphan metadata"):
            check_inventory(self.assets)

    def test_duplicate_guids_are_case_insensitive(self):
        (self.assets / "Boot.unity.meta").write_text("guid: " + "C" * 32 + "\n")
        with self.assertRaisesRegex(ValueError, "duplicate GUID"):
            check_inventory(self.assets)

    def test_missing_duplicate_guid_declarations_fail(self):
        for value in ("fileFormatVersion: 2\n", "guid: " + "c" * 32 + "\nguid: " + "f" * 32 + "\n"):
            with self.subTest(value=value):
                self.script_meta.write_text(value)
                with self.assertRaisesRegex(ValueError, "exactly one"):
                    check_inventory(self.assets)

    def test_bad_scene_and_prefab_script_references_fail(self):
        for name in ("Boot.unity", "Player.prefab"):
            scene = self.assets / name
            for guid, expected in (("f" * 32, "unresolved script GUID"),
                                   ("c" * 33, "invalid script reference"),
                                   ("d" * 32, r"does not resolve to a local \.cs")):
                with self.subTest(name=name, guid=guid):
                    scene.write_text(self.reference.replace("c" * 32, guid))
                    with self.assertRaisesRegex(ValueError, expected):
                        check_inventory(self.assets)
            scene.write_text(self.reference)

    def test_malformed_or_null_script_reference_fails(self):
        for reference in ("  m_Script: {fileID: 0}\n", "  m_Script:\n    fileID: 11500000\n"):
            with self.subTest(reference=reference):
                (self.assets / "Boot.unity").write_text(reference)
                with self.assertRaisesRegex(ValueError, "script reference|m_Script reference"):
                    check_inventory(self.assets)

    def test_wrapped_flow_reference_resolves(self):
        (self.assets / "Boot.unity").write_text(self.reference.replace(", guid:", ",\n    guid:"))
        self.assertEqual(2, check_inventory(self.assets)["script_references"])

    def test_completed_new_asset_during_check_cannot_be_silently_omitted(self):
        original = Path.read_bytes
        changed = False

        def read(path, *args, **kwargs):
            nonlocal changed
            text = original(path, *args, **kwargs)
            if path == self.script_meta and not changed:
                changed = True
                (self.assets / "Added.cs").write_text("class Added {}\n")
                (self.assets / "Added.cs.meta").write_text("guid: " + "f" * 32 + "\n")
            return text

        with patch.object(Path, "read_bytes", read), self.assertRaisesRegex(ValueError, "Assets changed during"):
            check_inventory(self.assets)


def actual_camera_mutation():
    source = ROOT / "Assets" / "Scripts" / "Editor" / "NormalCameraChecks.cs.meta"
    original = source.read_bytes()
    text = original.decode("utf-8")
    values = GUID_LINE.findall(text)
    if len(values) != 1 or not valid_guid(values[0].strip()):
        raise ValueError(f"Cannot establish valid actual camera metadata before mutation: {source}")
    with isolated_tree() as directory:
        assets = directory / "Assets"
        assets.mkdir()
        folder_guid = uuid.uuid4().hex
        while folder_guid == values[0].strip().lower():
            folder_guid = uuid.uuid4().hex
        (directory / "Assets.meta").write_text("guid: " + folder_guid + "\nfolderAsset: yes\n")
        (assets / "NormalCameraChecks.cs").write_text("// Isolated metadata-only fixture; never compiled.\n")
        copied_meta = assets / source.name
        copied_meta.write_bytes(original)
        check_inventory(assets)
        mutant = re.sub(r"(?m)^(guid:[ \t]*)([0-9a-fA-F]{32})([ \t]*\r?)$",
                        lambda match: match[1] + match[2] + "0" + match[3], text, count=1)
        copied_meta.write_bytes(mutant.encode("utf-8"))
        try:
            check_inventory(assets)
        except ValueError as error:
            if "NormalCameraChecks.cs.meta: expected exactly one nonzero 32 hex GUID" not in str(error):
                raise AssertionError(f"Mutant failed for the wrong reason: {error}") from error
        else:
            raise AssertionError("33-hex actual camera metadata mutant survived")
    if source.read_bytes() != original:
        raise ValueError("Actual camera metadata changed concurrently; mutation evidence is invalid")
    return hashlib.sha256(original).hexdigest()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--unit-only", action="store_true", help="explicitly do NOT run live inventory/actual-file mutation")
    args = parser.parse_args()
    result = unittest.TextTestRunner(verbosity=2).run(unittest.defaultTestLoader.loadTestsFromTestCase(UnityMetadataTests))
    if not result.wasSuccessful():
        return 1
    if args.unit_only:
        print("UNIT-ONLY: live Assets inventory and actual camera mutation NOT RUN.")
        return 0
    try:
        inventory = check_inventory(ROOT / "Assets")
        source_hash = actual_camera_mutation()
    except (ValueError, OSError, AssertionError) as error:
        print(f"UNITY METADATA FAILED: {error}")
        return 1
    print(f"ACTUAL UNITY METADATA PASSED: {inventory}")
    print(f"ACTUAL 33-HEX CAMERA MUTATION KILLED; shared source unchanged sha256={source_hash}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
