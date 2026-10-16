#!/usr/bin/env python3
"""Prepare a pinned selected-frame library offline from an explicit local WAN tree."""

import argparse
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import re
import stat
import sys

try:
    from . import read_wan_clip as reader
except ImportError:
    _spec = importlib.util.spec_from_file_location(
        "read_wan_clip", Path(__file__).with_name("read_wan_clip.py"))
    reader = importlib.util.module_from_spec(_spec)
    _spec.loader.exec_module(reader)


CATALOG = Path(__file__).with_name("player_sequences.json")
CATALOG_SCHEMA = "original-wan-sequence-catalog/v1"
INVENTORY_SCHEMA = "original-wan-export-inventory/v1"
MANIFEST = "export-inventory.json"
MAX_METADATA_BYTES = 1024 * 1024
ENTRY_FIELDS = {
    "runtime_name", "source_path", "source_git_blob_sha1",
    "source_frames", "selected_frames", "source_first", "source_last",
}
WanError = reader.wan.WanError


def _require(condition, message):
    if not condition:
        raise WanError(message)


def _hex(value, length, label):
    _require(isinstance(value, str) and re.fullmatch(r"[0-9a-f]{%d}" % length, value),
             f"{label} must be a lowercase {length}-character hash")


def _relative_path(value):
    _require(isinstance(value, str) and bool(value)
             and all(re.fullmatch(r"[A-Za-z0-9_.-]+", part) and part not in (".", "..")
                     and not part.endswith(".") for part in value.split("/")),
             "source path must be a portable relative repository path")
    return value


def _absolute(path):
    path = Path(path)
    _require(".." not in path.parts, "parent traversal is not allowed")
    return Path(os.path.abspath(path))


def _no_symlinks(path):
    for item in (path, *path.parents):
        _require(not item.is_symlink(), f"symlinks are not allowed: {item}")


def _read_bytes(path, limit):
    _no_symlinks(path)
    info = path.stat()
    _require(stat.S_ISREG(info.st_mode), f"expected a regular file: {path}")
    _require(info.st_size <= limit, f"input exceeds {limit}-byte limit: {path}")
    with path.open("rb") as stream:
        data = stream.read(limit + 1)
    _require(len(data) <= limit, f"input exceeds {limit}-byte limit: {path}")
    return data


def _unique_object(pairs):
    result = {}
    for key, value in pairs:
        _require(key not in result, f"duplicate JSON key: {key}")
        result[key] = value
    return result


def _nonfinite(value):
    raise WanError(f"non-finite catalog value: {value}")


def load_catalog(path):
    """Only pinned metadata is accepted; no WANs or decoded clips are consulted."""
    data = _read_bytes(_absolute(path), MAX_METADATA_BYTES)
    catalog = json.loads(data, object_pairs_hook=_unique_object, parse_constant=_nonfinite)
    required = {
        "schema", "source", "format_source", "sequence_count",
        "source_frame_count", "selected_frame_count", "clips",
    }
    _require(isinstance(catalog, dict) and required <= catalog.keys()
             and catalog.keys() <= required | {"checkpoint", "verified_inventory_sha256"},
             "catalog must contain only the defined metadata fields")
    _require(catalog["schema"] == CATALOG_SCHEMA, f"expected {CATALOG_SCHEMA}")
    source = catalog["source"]
    _require(isinstance(source, dict) and source.keys() == {"repository", "commit"},
             "catalog source requires repository and commit")
    _require(isinstance(source["repository"], str)
             and re.fullmatch(r"[\w.-]+/[\w.-]+", source["repository"], re.ASCII),
             "source repository must be owner/name")
    _hex(source["commit"], 40, "source commit")
    _require(catalog["format_source"] == reader.wan.FORMAT_SOURCE,
             "catalog format source does not match the existing pinned reader")
    if "checkpoint" in catalog:
        _require(isinstance(catalog["checkpoint"], str)
                 and re.fullmatch(r"[0-9a-f]{7,40}", catalog["checkpoint"]),
                 "checkpoint must be a Git revision label")
    if "verified_inventory_sha256" in catalog:
        _hex(catalog["verified_inventory_sha256"], 64, "verified inventory SHA256")

    entries = catalog["clips"]
    _require(isinstance(entries, list) and 1 <= len(entries) <= 512,
             "catalog requires between 1 and 512 sequences")
    names, pins, source_paths = set(), {}, {}
    for entry in entries:
        _require(isinstance(entry, dict) and entry.keys() == ENTRY_FIELDS,
                 "sequence entries must contain only names, pins, counts and boundaries")
        name = entry["runtime_name"]
        _require(isinstance(name, str) and re.fullmatch(r"[A-Z0-9_]{1,15}", name),
                 "runtime name must be a portable original uppercase sequence identifier")
        _require(name not in names, f"duplicate sequence: {name}")
        names.add(name)
        source_path = _relative_path(entry["source_path"])
        _require(source_path.lower().endswith(".wan"), "source path must name a WAN")
        folded = source_path.casefold()
        _require(source_paths.get(folded, source_path) == source_path,
                 "source paths must not differ only by case")
        source_paths[folded] = source_path
        pin = entry["source_git_blob_sha1"]
        _hex(pin, 40, "source WAN Git blob")
        _require(pins.get(source_path, pin) == pin, f"conflicting source pins: {source_path}")
        pins[source_path] = pin
        for key in ("source_frames", "selected_frames", "source_first", "source_last"):
            _require(type(entry[key]) is int, f"{name}: {key} must be an integer")
        _require(1 <= entry["selected_frames"] <= entry["source_frames"] <= reader.wan.MAX_FRAMES,
                 f"{name}: invalid source/selected frame counts")
        _require(0 <= entry["source_first"] <= entry["source_last"] < entry["source_frames"]
                 and entry["selected_frames"] <= entry["source_last"] - entry["source_first"] + 1,
                 f"{name}: invalid selected boundaries")
    for key, value in (
            ("sequence_count", len(entries)),
            ("source_frame_count", sum(e["source_frames"] for e in entries)),
            ("selected_frame_count", sum(e["selected_frames"] for e in entries))):
        _require(type(catalog[key]) is int and catalog[key] == value,
                 f"catalog {key} does not match its entries")
    return catalog, hashlib.sha256(data).hexdigest()


def _read_source(root, source_path):
    path = root.joinpath(*source_path.split("/"))
    _require(path.is_relative_to(root), "source path escapes the explicit root")
    return _read_bytes(path, reader.wan.MAX_BYTES)


def _inventory_entry(expected, clip, data):
    sequence = clip["sequence"]
    selected = sequence["selected_source_frame_indices"]
    actual = {
        "runtime_name": sequence["runtime_name"],
        "source_path": clip["provenance"]["path"],
        "source_git_blob_sha1": clip["provenance"]["git_blob_sha1"],
        "source_frames": sequence["source_frame_count"],
        "selected_frames": sequence["selected_frame_count"],
        "source_first": selected[0], "source_last": selected[-1],
    }
    _require(actual == expected,
             f"{expected['runtime_name']}: decoded source identity/counts/boundaries differ from catalog")
    return {
        **actual, "path": "clips/" + actual["runtime_name"] + ".json",
        "sha256": hashlib.sha256(data).hexdigest(),
        "source_sha256": clip["provenance"]["sha256"],
        "selected_source_frame_indices": selected,
    }


def _verify_bundle(destination, outputs):
    expected_directories = {Path(name).parent.as_posix() for name in outputs} - {"."}
    files = set()
    for path in destination.rglob("*"):
        _no_symlinks(path)
        relative = path.relative_to(destination).as_posix()
        if path.is_dir():
            _require(relative in expected_directories, f"unexpected bundle directory: {relative}")
        else:
            _require(relative in outputs, f"unexpected bundle file: {relative}")
            expected = outputs[relative]
            _require(_read_bytes(path, len(expected)) == expected,
                     f"existing bundle identity mismatch: {relative}")
            files.add(relative)
    _require(files == outputs.keys(), "existing bundle is incomplete")


def prepare_bundle(source_root, destination, catalog_path=CATALOG, *, reuse=False):
    """Decode before writing; reuse is read-only and compares every byte to a fresh export."""
    catalog, catalog_hash = load_catalog(catalog_path)
    source_root, destination = _absolute(source_root), _absolute(destination)
    scratch = _absolute(reader.wan.SCRATCH)
    _require(destination != scratch and destination.is_relative_to(scratch),
             "destination must stay beneath ignored .squad/.scratch/")
    _no_symlinks(source_root)
    _no_symlinks(destination)
    _require(source_root.is_dir(), "source root must be an existing explicit local directory")
    _require(not destination.is_relative_to(source_root) and not source_root.is_relative_to(destination),
             "source root and destination must not contain one another")
    exists = destination.exists()
    _require(not exists or reuse, "destination already exists; use --reuse only for an identical complete bundle")
    _require(not exists or destination.is_dir(), "destination must be a directory")

    cache, outputs, entries = {}, {}, []
    for expected in catalog["clips"]:
        source_path = expected["source_path"]
        if source_path not in cache:
            data = _read_source(source_root, source_path)
            _require(reader.wan.git_blob_sha1(data) == expected["source_git_blob_sha1"],
                     f"{source_path}: input does not match expected Git blob SHA-1")
            cache[source_path] = data
        clip = reader.full_clip(cache[source_path], expected["runtime_name"], {
            **catalog["source"], "path": source_path,
            "git_blob_sha1": expected["source_git_blob_sha1"],
            "local_input": source_path,
            "source_linkage": "caller-supplied commit/path; content checked against expected blob offline",
        })
        encoded = reader.encode_clip(clip).encode("utf-8")
        entry = _inventory_entry(expected, clip, encoded)
        outputs[entry["path"]] = encoded
        entries.append(entry)
    inventory = {
        "schema": INVENTORY_SCHEMA, "source": catalog["source"],
        "format_source": catalog["format_source"], "catalog_sha256": catalog_hash,
        "clips": entries,
    }
    manifest = reader.encode_clip(inventory).encode("utf-8")
    _require(len(manifest) <= MAX_METADATA_BYTES, "export inventory exceeds the library byte limit")
    outputs[MANIFEST] = manifest

    if exists:
        _verify_bundle(destination, outputs)
    else:
        destination.parent.mkdir(parents=True, exist_ok=True)
        _no_symlinks(destination)
        destination.mkdir()
        (destination / "clips").mkdir()
        for relative, data in outputs.items():
            path = destination / relative
            _no_symlinks(path)
            with path.open("xb") as stream:
                stream.write(data)
        _verify_bundle(destination, outputs)
    return {
        "schema": "original-wan-batch-result/v1", "manifest": MANIFEST,
        "manifest_sha256": hashlib.sha256(manifest).hexdigest(),
        "catalog_sha256": catalog_hash, "clips": len(entries),
        "source_files": len(cache), "source_frames": catalog["source_frame_count"],
        "selected_frames": catalog["selected_frame_count"], "reused": exists,
    }


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source-root", type=Path, required=True,
                        help="explicit local source tree; no retrieval or historical code execution")
    parser.add_argument("--destination", type=Path, required=True,
                        help="new bundle directory beneath ignored .squad/.scratch/")
    parser.add_argument("--catalog", type=Path, default=CATALOG,
                        help="metadata-only catalog (default: adjacent frozen player_sequences.json)")
    parser.add_argument("--reuse", action="store_true",
                        help="read-only reuse only after every bundle byte is freshly verified")
    args = parser.parse_args(argv)
    try:
        result = prepare_bundle(args.source_root, args.destination, args.catalog, reuse=args.reuse)
        print(reader.encode_clip(result), end="")
        return 0
    except (OSError, ValueError, UnicodeError) as exc:
        print(f"error: {exc}", file=sys.stderr)
        return 2


if __name__ == "__main__":
    sys.exit(main())
