#!/usr/bin/env python3
"""Register and prepare the explicit C1 272-clip production successor offline."""

import argparse
import hashlib
import importlib.util
import json
from pathlib import Path
import sys


def _module(name):
    spec = importlib.util.spec_from_file_location(name, Path(__file__).with_name(name + ".py"))
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


selected = _module("prepare_player_sequences")
CATALOG = Path(__file__).with_name("contact_sequences.json")
SCHEMA = "original-wan-contact272-catalog/v1"
BASE_SHA = "f0ae9395e57ae8327b7b55493dd6c457102adb4a13ecbb581e3551d400a3800e"
EXTRA_SHA = "907e2d368394e5c9db1bebdf218abae62bb3b841230293670cb3d2db48e05fcd"
DEPENDENCIES_SHA = "34e6acabf68e757d6e1914fb7daeb520c511d63370dab544ca95a1d61ae606f5"
RECEIPT_SHA = "196ca58873341a8f0bd7f3e051b8d4a5e150bdcec1eebacd7a3067a88338041d"
EXTRA_LINKAGE = "Reconstructed complete pinned Git tree; source data is ignored test-only input."
PROFILE = "original-teams-contact272"
require = selected._require


def sha(data):
    return hashlib.sha256(data).hexdigest()


def read_json(path, pin=None):
    data = selected._read_bytes(selected._absolute(path), selected.MAX_METADATA_BYTES)
    require(pin is None or sha(data) == pin, str(path) + ": frozen metadata SHA256 mismatch")
    return json.loads(data, object_pairs_hook=selected._unique_object, parse_constant=selected._nonfinite)


def register(dependencies, receipt, destination):
    metadata = read_json(dependencies, DEPENDENCIES_SHA)
    original = read_json(receipt, RECEIPT_SHA)
    require(metadata["base157_sha256"] == original["base_sha256"] == BASE_SHA
            and metadata["supplemental115_sha256"] == original["extra_inventory_sha256"] == EXTRA_SHA
            and original["status"] == "passed" and not original["failures"]
            and metadata["required_clips"] == original["extra_count"] == 115
            and metadata["selected_frames"] == original["extra_selected_frames"] == 11402,
            "Expected the frozen, disjoint C1 selected115 requirements")
    require([e["runtime_name"] for e in metadata["clips"]] ==
            [e["runtime_name"] for e in original["clips"]], "Frozen C1 clip registries disagree")
    for entry, old in zip(metadata["clips"], original["clips"]):
        require(all(entry[key] == value for key, value in old.items()),
                entry["runtime_name"] + ": frozen C1 selected metadata differs")
    catalog = {
        "schema": SCHEMA, "profile": PROFILE,
        "source": {"repository": "historicalsource/nflblitz2000",
                   "commit": metadata["exact_source_pin"]},
        "format_source": selected.reader.wan.FORMAT_SOURCE,
        "base_manifest_sha256": BASE_SHA, "supplemental_manifest_sha256": EXTRA_SHA,
        "dependency_metadata_sha256": DEPENDENCIES_SHA, "input_receipt_sha256": RECEIPT_SHA,
        "sequence_count": 272, "selected_frame_count": 24892,
        "supplemental_source_frame_count": sum(e["source_frames"] for e in metadata["clips"]),
        "supplemental_selected_frame_count": 11402,
        "supplemental_provenance_source_linkage": EXTRA_LINKAGE,
        "source_wans": {name: {k: v for k, v in item.items() if k != "path"}
                        for name, item in original["source_wans"].items()},
        "clips": metadata["clips"],
        "shipped_SEQ_numeric_ids_or_quantization_verified": False,
    }
    destination = selected._absolute(destination)
    require(destination.parent == CATALOG.parent and destination.name == CATALOG.name,
            "Registry output must be the adjacent metadata-only contact_sequences.json")
    selected._no_symlinks(destination)
    data = selected.reader.encode_clip(catalog).encode("utf-8")
    if destination.exists():
        require(selected._read_bytes(destination, len(data)) == data, "Existing C1 registry differs; never overwrite")
    else:
        with destination.open("xb") as stream:
            stream.write(data)
    return {"schema": SCHEMA, "catalog_sha256": sha(data), "clips": 115,
            "metadata_only": True, "motion_values": False}


def load_catalog(path):
    catalog = read_json(path)
    require(catalog["schema"] == SCHEMA and catalog["profile"] == PROFILE
            and catalog["base_manifest_sha256"] == BASE_SHA
            and catalog["supplemental_manifest_sha256"] == EXTRA_SHA
            and catalog["dependency_metadata_sha256"] == DEPENDENCIES_SHA
            and catalog["input_receipt_sha256"] == RECEIPT_SHA
            and catalog["source"] == {"repository": "historicalsource/nflblitz2000",
                                      "commit": "89f840d58d152a2a9f230cb8d0cdffbd554c8ded"}
            and catalog["format_source"] == selected.reader.wan.FORMAT_SOURCE
            and catalog["sequence_count"] == 272 and catalog["selected_frame_count"] == 24892
            and catalog["supplemental_source_frame_count"] == 13477
            and catalog["supplemental_selected_frame_count"] == 11402
            and catalog["supplemental_provenance_source_linkage"] == EXTRA_LINKAGE
            and catalog["shipped_SEQ_numeric_ids_or_quantization_verified"] is False,
            "Expected the exact explicit C1 selected115/production272 registry")
    entries = catalog["clips"]
    require(len(entries) == 115 and len({e["runtime_name"] for e in entries}) == 115
            and [e["runtime_name"] for e in entries] == sorted(e["runtime_name"] for e in entries)
            and sum(e["selected_frames"] for e in entries) == 11402,
            "C1 supplemental registry names/counts/order differ")
    for entry in entries:
        selected._relative_path(entry["source_path"])
        require(entry["path"] == "clips/" + entry["runtime_name"] + ".json",
                "C1 clip path must preserve its exact runtime identity")
        selected._relative_path(entry["path"])
        selected._hex(entry["sha256"], 64, "C1 decoded clip SHA256")
        selected._hex(entry["source_git_blob_sha1"], 40, "C1 source Git blob")
        indices = entry["selected_source_frame_indices"]
        require(len(indices) == entry["selected_frames"] and indices == sorted(set(indices))
                and indices[0] == entry["source_first"] and indices[-1] == entry["source_last"]
                and all(type(i) is int and 0 <= i < entry["source_frames"] for i in indices),
                entry["runtime_name"] + ": exact source/selected index mapping differs")
    return catalog, sha(selected._read_bytes(selected._absolute(path), selected.MAX_METADATA_BYTES))


def _original_bytes(roots, relative, identity):
    found = []
    for root in roots:
        path = root / relative
        if path.exists():
            data = selected._read_source(root, relative)
            require(selected.reader.wan.git_blob_sha1(data) == identity["git_blob_sha1"]
                    and sha(data) == identity["sha256"],
                    relative + ": explicit source root contains wrong original bytes")
            found.append(data)
    require(found, relative + ": missing pinned original WAN in explicitly supplied --source-root paths")
    return found[0]


def prepare(base_bundle, source_roots, destination, catalog_path=CATALOG, *, reuse=False):
    catalog, catalog_hash = load_catalog(catalog_path)
    base_bundle, destination = selected._absolute(base_bundle), selected._absolute(destination)
    roots = [selected._absolute(root) for root in source_roots]
    require(roots and all(root.is_dir() for root in roots), "Explicit local source roots are required")
    scratch = selected._absolute(selected.reader.wan.SCRATCH)
    require(destination != scratch and destination.is_relative_to(scratch),
            "Production motion must remain beneath ignored .squad/.scratch/")
    for root in [base_bundle, destination, *roots]:
        selected._no_symlinks(root)
    for root in [base_bundle, *roots]:
        require(not destination.is_relative_to(root) and not root.is_relative_to(destination),
                "Production destination and immutable inputs must not overlap")
    exists = destination.exists()
    require(not exists or reuse, "Destination exists; --reuse only verifies an identical complete bundle")
    base = read_json(base_bundle / selected.MANIFEST, BASE_SHA)
    require(base["schema"] == selected.INVENTORY_SCHEMA
            and len(base["clips"]) == 157 and sum(e["selected_frames"] for e in base["clips"]) == 13490,
            "The immutable true157 base is mandatory; no old profile alias")
    outputs = {entry["path"]: selected._read_bytes(base_bundle / entry["path"], 8 * 1024 * 1024)
               for entry in base["clips"]}
    require(all(sha(outputs[e["path"]]) == e["sha256"] for e in base["clips"]),
            "An immutable true157 clip changed")
    selected._verify_bundle(base_bundle, dict(outputs, **{selected.MANIFEST:
        selected._read_bytes(base_bundle / selected.MANIFEST, selected.MAX_METADATA_BYTES)}))
    names = {e["runtime_name"] for e in base["clips"]}
    require(not names.intersection(e["runtime_name"] for e in catalog["clips"]),
            "C1 supplemental clips must be disjoint, never replacements or aliases")
    cache, extra = {}, []
    for expected in catalog["clips"]:
        relative = expected["source_path"]
        if relative not in cache:
            cache[relative] = _original_bytes(roots, relative, catalog["source_wans"][relative])
        clip = selected.reader.full_clip(cache[relative], expected["runtime_name"], {
            **catalog["source"], "path": relative, "git_blob_sha1": expected["source_git_blob_sha1"],
            "source_linkage": EXTRA_LINKAGE,
        })
        encoded = selected.reader.encode_clip(clip).encode("utf-8")
        ordinary = {key: expected[key] for key in selected.ENTRY_FIELDS}
        actual = selected._inventory_entry(ordinary, clip, encoded)
        require(actual["sha256"] == expected["sha256"]
                and actual["selected_source_frame_indices"] == expected["selected_source_frame_indices"]
                and clip["sequence"]["authoring_name"] == expected["authoring_name"]
                and clip["sequence"]["chunk_number"] == expected["original_wan_chunk_number"]
                and clip["sequence"]["chunk_byte_offset"] == expected["original_wan_chunk_byte_offset"],
                expected["runtime_name"] + ": original source/selection/chunk/decoded-byte identity differs")
        outputs[actual["path"]] = encoded
        extra.append({key: actual[key] for key in (
            "runtime_name", "path", "sha256", "source_path", "source_git_blob_sha1", "source_frames",
            "selected_frames", "source_first", "source_last", "selected_source_frame_indices")})
    historical_extra = {"schema": selected.INVENTORY_SCHEMA, "source": base["source"],
                        "format_source": base["format_source"],
                        "contact_profile": "C1 supplemental selected source; NOT an alias or replacement of true157",
                        "clips": extra}
    historical_bytes = (json.dumps(historical_extra, sort_keys=True, separators=(",", ":")) + "\n").encode()
    require(sha(historical_bytes) == EXTRA_SHA, "Fresh selected115 export differs from the exact frozen C1 input")
    entries = sorted([*base["clips"], *extra], key=lambda item: item["runtime_name"])
    manifest = {
        "schema": selected.INVENTORY_SCHEMA, "source": base["source"], "format_source": base["format_source"],
        "profile": PROFILE, "catalog_sha256": catalog_hash,
        "selected_base_manifest_sha256": BASE_SHA, "supplemental_manifest_sha256": EXTRA_SHA,
        "dependency_metadata_sha256": DEPENDENCIES_SHA, "input_receipt_sha256": RECEIPT_SHA,
        "producer": {"policy": "immutable157-plus-exact-selected115/v1", "files": {
            name: sha(Path(__file__).with_name(name).read_bytes()) for name in (
                "prepare_contact_profile.py", "prepare_player_sequences.py", "read_wan_clip.py", "read_wan_pose.py")}},
        "converter_replay": "R_SKIP entry and bytes retained unchanged from qualified true157; no new replay",
        "shipped_SEQ_numeric_ids_or_quantization_verified": False, "clips": entries,
    }
    data = selected.reader.encode_clip(manifest).encode("utf-8")
    require(len(data) <= selected.MAX_METADATA_BYTES, "Production272 inventory exceeds loader byte limit")
    outputs[selected.MANIFEST] = data
    if exists:
        selected._verify_bundle(destination, outputs)
    else:
        destination.parent.mkdir(parents=True, exist_ok=True)
        destination.mkdir()
        (destination / "clips").mkdir()
        for relative, content in outputs.items():
            with (destination / relative).open("xb") as stream:
                stream.write(content)
        selected._verify_bundle(destination, outputs)
    return {"schema": "original-wan-contact272-result/v1", "manifest_sha256": sha(data),
            "catalog_sha256": catalog_hash, "clips": len(entries), "selected_frames": 24892,
            "base157_manifest_sha256": BASE_SHA, "supplemental115_manifest_sha256": EXTRA_SHA,
            "unchanged_base_clips": 157, "fresh_exact_supplemental_clips": 115,
            "stable_id": PROFILE, "animation_resource_key": "SourceData/local-plyr266-contact272-" + sha(data)[:8],
            "reused": exists, "shipped_SEQ_verified": False}


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest="command", required=True)
    registry = commands.add_parser("register", help="Reproduce metadata-only registry from the frozen C1 handoff")
    registry.add_argument("--dependencies", type=Path, required=True)
    registry.add_argument("--receipt", type=Path, required=True)
    registry.add_argument("--destination", type=Path, default=CATALOG)
    bundle = commands.add_parser("prepare", help="Produce new canonical272 from immutable157 and actual original WANs")
    bundle.add_argument("--base-bundle", type=Path, required=True)
    bundle.add_argument("--source-root", type=Path, action="append", required=True,
                        help="Explicit authorized local WAN root; repeat for disjoint original trees")
    bundle.add_argument("--destination", type=Path, required=True)
    bundle.add_argument("--catalog", type=Path, default=CATALOG)
    bundle.add_argument("--reuse", action="store_true")
    args = parser.parse_args(argv)
    try:
        result = register(args.dependencies, args.receipt, args.destination) if args.command == "register" else prepare(
            args.base_bundle, args.source_root, args.destination, args.catalog, reuse=args.reuse)
        print(json.dumps(result, indent=2))
        return 0
    except (OSError, ValueError, KeyError, TypeError) as error:
        print("error: " + str(error), file=sys.stderr)
        return 2


if __name__ == "__main__":
    sys.exit(main())
