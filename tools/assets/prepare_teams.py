#!/usr/bin/env python3
"""Offline original team/model/skin/head preparation; no network or recoloring.

Consumes the final tools/rosters output read-only. All source files are checked
against a completely reconstructed pinned Git tree before conversion. Existing
output is never overwritten; original and converted art stays ignored/local.
"""

import argparse
import json
import os
from pathlib import Path, PurePosixPath
import re
import sys

import prepare_player as files

ROOT = Path(__file__).absolute().parents[2]
sys.path.insert(0, str(ROOT / "tools/rosters"))
from export_rosters import (REVISION, TREE, SourceError, git_hash, read_json,
                            relative_path, require, verify_tree)

ROSTER_SHA256 = "f747741e95766185e0c615ded549d1ce2f5fec0c48ed2ecb29ba1b755764539b"
CATALOG_NAME = "team-presentation.json"


def read_pinned(source_root, tree, label):
    relative_path(label)
    entry = tree.get(label)
    require(entry is not None and entry["type"] == "blob" and entry["mode"] != "120000",
            "Missing regular original source-tree entry: " + label)
    data = files.read_regular(source_root / label, entry["size"])
    require(len(data) == entry["size"] and git_hash("blob", data) == entry["sha"],
            "Original source content pin mismatch: " + label)
    return data


def texture_source(asset, tree):
    if asset["source_path"] is not None:
        return asset["source_path"]
    stem = PurePosixPath(asset["runtime_filename"]).stem.casefold()
    candidates = [path for path in tree if path.startswith("img3d/") and
                  PurePosixPath(path).suffix.casefold() == ".tga" and
                  PurePosixPath(path).stem.casefold() == stem]
    require(len(candidates) <= 1, "Ambiguous original authoring TGA: " + asset["id"])
    return candidates[0] if candidates else None


def atlas_pieces(runtime_filename, layout, commands, tree):
    target = runtime_filename[:-4].replace("LOD", "")
    require(re.fullmatch("[A-Z]{3}[HA]", target) is not None, "Unknown original missing LOD atlas identity.")
    require(re.search(r"(?im)^copy\s+" + target + r"\.tga\s+" + target + r"LOD\.tga\s*$", commands) and
            re.search(r"(?im)^texus\s+-t\s+argb1555\s+-o\s+" + target + r"LOD\.3df\s+" +
                      target + r"LOD\.tga\s*$", commands), "Missing original active uniform conversion branch.")
    pending, found = None, []
    by_case = {path.casefold(): path for path in tree}
    for line in layout.splitlines():
        if line.startswith("PACK_TEXTURES>"):
            pending = line.partition(">")[2].split()
        elif line.startswith("PACK>"):
            if line.partition(">")[2].strip().casefold() == (target + ".tga").casefold():
                require(pending is not None and len(pending) == 4, "Missing original four-piece atlas inputs.")
                found.append([by_case.get(("img3d/" + name).casefold()) for name in pending])
            pending = None
    require(len(found) == 1 and all(found[0]), "Missing/ambiguous original atlas layout or authored pieces.")
    return found[0]


def build_bundle(source_root, roster_bytes, authorization_reference, original_packer=None):
    require(files.sha256(roster_bytes) == ROSTER_SHA256, "Final roster content SHA256 mismatch.")
    roster = read_json(roster_bytes)
    require(roster["schema"] == "original-team-rosters/v1" and
            roster["provenance"]["commit"] == REVISION and
            roster["provenance"]["tree"] == TREE, "Wrong original roster profile/pin.")
    tree = verify_tree(read_json(files.read_regular(source_root / "tree.json", 16 * 1024 * 1024)))
    bundle = {"rosters.json": roster_bytes}
    inputs = {}
    textures, missing = [], []

    def source(label):
        data = read_pinned(source_root, tree, label)
        inputs[label] = dict(files.fingerprint(data), path=label)
        return data

    for asset in roster["assets"]:
        if asset["kind"] != "texture":
            if asset["source_path"] and asset["source_path"].endswith(".h"):
                label = asset["source_path"]
                if label not in bundle:
                    bundle[label] = source(label)
            continue
        label = texture_source(asset, tree)
        input_paths = None
        if label is None and original_packer is not None and asset["runtime_filename"].endswith("LOD.WMS"):
            label = "img3d/plyr.ld"
            layout = source(label).decode("ascii")
            commands = source("img3d/uni.bat").decode("ascii")
            pieces = atlas_pieces(asset["runtime_filename"], layout, commands, tree)
            for piece in pieces:
                source(piece)
            decoded, placements = original_packer.pack([source_root / path for path in pieces],
                                                       PurePosixPath(asset["runtime_filename"]).stem)
            representation = "original-c-hack-authoring-atlas"
            output = "textures/" + asset["runtime_filename"].lower() + ".original-c-hack.tga"
            input_paths = pieces
            bundle["packing/" + asset["runtime_filename"].lower() + ".json"] = files.json_bytes({
                "runtime_id": asset["id"], "recipe": label, "pieces": pieces, "placements": placements,
                "original_tools": original_packer.receipt,
                "quantization": "Exact original c_hack output; missing Texus-generated runtime WMS is not invented.",
            })
        elif label is None:
            missing.append(asset["id"])
            continue
        elif label.lower().endswith(".wms"):
            original = source(label)
            decoded = files.decode_argb1555_level0(original)
            representation = "original-runtime-argb1555-level0"
            output = "textures/" + asset["runtime_filename"].lower() + ".tga"
        else:
            original = source(label)
            files.inspect_tga(original)
            decoded = original
            representation = "byte-identical-original-authoring-tga"
            output = "textures/" + asset["runtime_filename"].lower() + ".authoring.tga"
        info = files.inspect_tga(decoded)
        bundle[output] = decoded
        textures.append({
            "id": asset["id"], "source_path": label,
            "source_git_blob_sha1": tree[label]["sha"], "representation": representation,
            "path": output, "sha256": files.sha256(decoded), "bytes": len(decoded),
            "width": info["width"], "height": info["height"],
            "input_paths": input_paths if input_paths is not None else [label],
        })
    source("player.c")
    source("nfl.c")
    notice = source("cambot.c")
    source("lib/ani3d/pipe3D.c")
    bundle["team-presentation-notice.txt"] = b"".join(notice.splitlines(keepends=True)[5:12])
    catalog = {
        "schema": "original-team-presentation/v1", "source_revision": REVISION, "source_tree": TREE,
        "roster_sha256": ROSTER_SHA256, "authorization_reference": authorization_reference,
        "textures": sorted(textures, key=lambda row: row["id"]),
        "inputs": [inputs[path] for path in sorted(inputs)],
        "missing_texture_ids": sorted(missing),
        "qualifications": [
            "No tint, atlas alias, resizing, synthetic geometry, or matching-uniform fallback.",
            "Missing Browns/Titans runtime LOD WMS can be derived using unchanged original c_hack and active "
            "plyr.ld/uni.bat. These exact original packed authoring atlases are identified separately from missing "
            "Texus-generated runtime WMS; no Oilers alias or guessed packing.",
            "Skin/custom-head authoring TGAs retain original bytes, not a claim of reproduced missing WMS quantization.",
            "Original lower WMS mip levels are validated but only level0 is emitted.",
            "Original number geometry, BCD digits and selector0/1 are retained. Ten-column UV placement is "
            "the authored atlas layout; draw_jersey_tris implementation is absent from this pinned tree, "
            "so target renderer floating-point/depth-bias parity is not claimed.",
            "No Editor, scene, prefab, meta, Gameplay, Contracts, hosting or publication operations.",
        ],
    }
    bundle[CATALOG_NAME] = files.json_bytes(catalog)
    return bundle, catalog


def prepare(source_root, roster_path, destination, authorization_reference, reuse_identical=False,
            original_tools_root=None):
    require(authorization_reference and len(authorization_reference) <= 128 and
            all(c.isascii() and (c.isalnum() or c in "-_") for c in authorization_reference),
            "Use a non-secret local authorization identifier.")
    source_root = files.absolute_path(source_root, Path.cwd())
    destination = files.absolute_path(destination, ROOT)
    files.check_physical(source_root, directory=True)
    require(ROOT / ".squad/.scratch" in destination.parents,
            "Destination must be under this project's ignored .squad/.scratch.")
    require(source_root != destination and source_root not in destination.parents and
            destination not in source_root.parents, "Source/destination roots must not overlap.")
    roster_path = files.absolute_path(roster_path, Path.cwd())
    packer = None
    if original_tools_root is not None:
        from original_uniform_packer import OriginalUniformPacker
        packer = OriginalUniformPacker(files.absolute_path(original_tools_root, Path.cwd()),
                                       ROOT / ".squad/.scratch/source-presentation/original-packer")
    bundle, catalog = build_bundle(source_root, files.read_regular(roster_path, 4 * 1024 * 1024),
                                   authorization_reference, packer)
    if destination.exists() or destination.is_symlink():
        require(reuse_identical, "Destination exists; choose a new path or --reuse-identical.")
        files.verify_identical(destination, bundle)
    else:
        files.make_directories(destination.parent)
        destination.mkdir(mode=0o700)
        for label, data in sorted(bundle.items()):
            files.path_parts(label)
            output = destination / label
            files.make_directories(output.parent)
            descriptor = os.open(output, os.O_WRONLY | os.O_CREAT | os.O_EXCL |
                                 getattr(os, "O_NOFOLLOW", 0), 0o600)
            with os.fdopen(descriptor, "wb") as stream:
                stream.write(data)
        files.verify_identical(destination, bundle)
    return {
        "destination": destination.relative_to(ROOT).as_posix(),
        "catalog": CATALOG_NAME, "catalog_sha256": files.sha256(bundle[CATALOG_NAME]),
        "roster_sha256": ROSTER_SHA256, "textures": len(catalog["textures"]),
        "missing_texture_ids": catalog["missing_texture_ids"],
        "source_inputs": len(catalog["inputs"]), "output_files": len(bundle),
    }


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source-root", required=True)
    parser.add_argument("--rosters", required=True, help="Final immutable tools/rosters output.")
    parser.add_argument("--destination", required=True)
    parser.add_argument("--confirm-local-use", required=True, action="store_true")
    parser.add_argument("--authorization-reference", required=True)
    parser.add_argument("--reuse-identical", action="store_true")
    parser.add_argument("--original-tools-root", help="Explicit pinned midwayvideo root; use actual c_hack for missing atlases.")
    args = parser.parse_args(argv)
    try:
        result = prepare(args.source_root, args.rosters, args.destination, args.authorization_reference,
                         args.reuse_identical, args.original_tools_root)
    except (SourceError, files.PreparationError, OSError, ValueError, KeyError) as error:
        parser.exit(1, "error: " + str(error) + "\n")
    print(files.json_bytes(result).decode("ascii"), end="")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
