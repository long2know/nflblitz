#!/usr/bin/env python3
"""Prepare a DISTINCT ignored C1 supplemental library; never modify/alias the frozen 56/117/156/157 bundles."""
import argparse
import base64
import hashlib
import json
import os
from pathlib import Path
import subprocess
import sys

from source import REVISION, blob, read
from translate import translate

ROOT = Path(__file__).resolve().parents[2]
ARTIFACTS = ROOT / "tests/contact/.artifacts"
BASE_SHA = "f0ae9395e57ae8327b7b55493dd6c457102adb4a13ecbb581e3551d400a3800e"
EXTRA_WANS = ("hit99.wan", "hits2.wan", "misc99_2.wan", "pupet99.wan",
              "pupet992.wan", "puppet.wan", "puppet2.wan", "puppet2k.wan")


def sha(data):
    return hashlib.sha256(data).hexdigest()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source-root", type=Path, required=True)
    parser.add_argument("--operand-root", type=Path, required=True)
    parser.add_argument("--base-bundle", type=Path, required=True)
    parser.add_argument("--download", action="store_true", help="Permit only eight exact tree-verified public contact WANs.")
    args = parser.parse_args()
    original = args.source_root.resolve()
    base = args.base_bundle.resolve()
    base_bytes = (base / "export-inventory.json").read_bytes()
    if sha(base_bytes) != BASE_SHA:
        raise ValueError("Expected the immutable true157 base, not a 56/117/156 alias.")
    inventory = json.loads(base_bytes)
    if len(inventory["clips"]) != 157:
        raise ValueError("Expected the actual true157 base.")
    base_hashes = {entry["path"]: entry["sha256"] for entry in inventory["clips"]}
    for name, pin in base_hashes.items():
        if sha((base / name).read_bytes()) != pin:
            raise ValueError("Frozen true157 bytes changed: " + name)
    sys.path.insert(0, str(ROOT / "tools/rosters"))
    from export_rosters import verify_tree
    tree = verify_tree(json.loads((original / "tree.json").read_bytes()))
    sys.path.insert(0, str(ROOT / "tools/animation"))
    import read_wan_clip as clip_reader
    wan = clip_reader.wan
    programs = translate(read(original), args.operand_root)
    names = {row["text"] for rows in programs.values() for row in rows if row["op"] in ("NewSequence", "Interpolate")}
    needed = names - {entry["runtime_name"] for entry in inventory["clips"]}
    ARTIFACTS.mkdir(exist_ok=True)
    cache = ARTIFACTS / "original"
    candidates, source_files, sources = {}, {}, {}
    allowed = {name for name in tree if name.startswith("anim/") and name.lower().endswith(".wan")
               and (original.joinpath(name).is_file() or Path(name).name.lower() in EXTRA_WANS)}
    for name in sorted(allowed):
        pin = tree[name]["sha"]
        path = original / name if (original / name).is_file() else cache / name
        if not path.is_file():
            if not args.download:
                raise ValueError("Missing precise contact WAN " + name + "; supply --download for its pinned public blob.")
            response = json.loads(subprocess.run(
                ["gh", "api", "repos/historicalsource/nflblitz2000/git/blobs/" + pin],
                capture_output=True, text=True, check=True, timeout=90).stdout)
            if response.get("sha") != pin or response.get("encoding") != "base64":
                raise ValueError("Contact WAN Git blob identity/encoding differs: " + name)
            data = base64.b64decode(response["content"])
            if blob(data) != pin:
                raise ValueError("Contact WAN does not match pinned original tree: " + name)
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(data)
        data = path.read_bytes()
        if blob(data) != pin:
            raise ValueError("Contact WAN does not match pinned original tree: " + name)
        sources[name], source_files[name] = data, path
        for tag, number, reference, offset, body, end in wan._chunks(data):
            if tag != b"3AN ":
                continue
            header = wan._unpack(wan.ANIMATION, data, body, end)
            runtime = wan._stem(header[0].split(b"\0", 1)[0].decode("ascii"))
            if runtime in needed and header[1] & wan.SELECTED:
                candidates.setdefault(runtime, []).append(name)
    output = ARTIFACTS / "contact-extra"
    (output / "clips").mkdir(parents=True, exist_ok=True)
    entries, failures = [], []
    for name in sorted(needed):
        paths = candidates.get(name, [])
        if len(paths) != 1:
            failures.append({"sequence": name, "source_candidates": paths, "error": "Expected one selected original sequence."})
            continue
        source_path = paths[0]
        pin = tree[source_path]["sha"]
        try:
            clip = clip_reader.full_clip(sources[source_path], name, {
                "repository": "historicalsource/nflblitz2000", "commit": REVISION,
                "path": source_path, "git_blob_sha1": pin,
                "source_linkage": "Reconstructed complete pinned Git tree; source data is ignored test-only input.",
            })
        except wan.WanError as error:
            failures.append({"sequence": name, "source_path": source_path, "git_blob_sha1": pin, "error": str(error)})
            continue
        data = clip_reader.encode_clip(clip).encode()
        path = "clips/" + name + ".json"
        (output / path).write_bytes(data)
        seq = clip["sequence"]
        entries.append({"runtime_name": name, "path": path, "sha256": sha(data),
                        "source_path": source_path, "source_git_blob_sha1": pin,
                        "source_frames": seq["source_frame_count"], "selected_frames": seq["selected_frame_count"],
                        "source_first": seq["selected_source_frame_indices"][0],
                        "source_last": seq["selected_source_frame_indices"][-1],
                        "selected_source_frame_indices": seq["selected_source_frame_indices"]})
    extra = {"schema": "original-wan-export-inventory/v1",
             "source": inventory["source"], "format_source": inventory["format_source"],
             "contact_profile": "C1 supplemental selected source; NOT an alias or replacement of true157", "clips": entries}
    data = (json.dumps(extra, sort_keys=True, separators=(",", ":")) + "\n").encode()
    (output / "export-inventory.json").write_bytes(data)
    receipt = {"schema": "source-contact-inputs/v1", "status": "passed" if not failures else "incomplete",
               "source_revision": REVISION, "base_sha256": BASE_SHA, "base_bundle": str(base),
               "base_count": 157, "extra_count": len(entries), "required_extra_count": len(needed),
               "extra_inventory_sha256": sha(data), "extra_selected_frames": sum(e["selected_frames"] for e in entries),
               "failures": failures, "clips": entries,
               "source_wans": {name: {"path": str(source_files[name]), "git_blob_sha1": tree[name]["sha"],
                                      "sha256": sha(sources[name])} for name in sorted({e["source_path"] for e in entries})},
               "base_unchanged": sha((base / "export-inventory.json").read_bytes()) == BASE_SHA and all(
                   sha((base / name).read_bytes()) == pin for name, pin in base_hashes.items()),
               "production_resource_profile_changed": False, "asset_import_or_publication": False}
    (ARTIFACTS / "inputs.json").write_text(json.dumps(receipt, indent=2) + "\n")
    print(json.dumps({key: receipt[key] for key in ("status", "base_count", "extra_count", "required_extra_count",
                                                  "extra_selected_frames", "extra_inventory_sha256", "failures", "base_unchanged")}))
    return 0 if not failures else 1


if __name__ == "__main__":
    sys.exit(main())
