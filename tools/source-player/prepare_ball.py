#!/usr/bin/env python3
"""Extend a verified private player bundle with the exact original free-ball reconstruction."""
import argparse
import json
from pathlib import Path
from prepare import Bundle, read, require

MANIFEST_SHA256 = "3d7ab26acab8a968058c6644011e1ac503547bf6ae67705974e3848ee5e00549"


def extend(base, base_hash, ball, ball_hash, output):
    base, ball = base.absolute(), ball.absolute()
    manifest = json.loads(read(base, "manifest.json", base_hash))
    require(manifest["schema"] == "blitz-portable-source-player/v1" and
            manifest["profile"] == "original-teams-contact281-c2-m1", "Actual original M1 player inputs required.")
    require(ball_hash == MANIFEST_SHA256, "Exact calibrated original free-ball generation required.")
    bundle = Bundle()
    for record in manifest["files"]:
        bundle.add(base, record["path"], record["sha256"])
    entries, origins = dict(manifest["entrypoints"]), dict(manifest["origins"])
    require("freeBall" not in entries, "Base already contains a free-ball generation.")
    definition = bundle.document(ball, "free-ball.json", ball_hash, "free-ball/free-ball.json")
    require(definition["schema"] == "original-source-authored-free-ball/v1" and
            definition["sourceRevision"] == manifest["sourceRevision"] and
            not definition["nativeGeneratedHeaderEquivalent"], "Original source-authored ball provenance required.")
    for path, hash_key in ((definition["sourcePayload"], "sourceSha256"), (definition["headerPath"], "headerSha256")):
        bundle.add(ball, path, definition[hash_key], "free-ball/" + path)
    entries["freeBall"], origins["freeBall"] = "free-ball/free-ball.json", definition["origin"]
    prepared, pin = bundle.persist(output, entries, origins)
    return {"manifestSha256": pin, "files": len(prepared["files"]), "baseManifestSha256": base_hash,
            "freeBallManifestSha256": ball_hash, "output": str(output.absolute())}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--base-data", type=Path, required=True)
    parser.add_argument("--base-manifest-sha256", required=True)
    parser.add_argument("--ball-bundle", type=Path, required=True)
    parser.add_argument("--ball-manifest-sha256", required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    try:
        print(json.dumps(extend(args.base_data, args.base_manifest_sha256, args.ball_bundle,
                                args.ball_manifest_sha256, args.output), indent=2))
    except (ValueError, OSError, KeyError) as error:
        parser.exit(1, "Original free-ball inputs rejected: " + str(error) + "\n")


if __name__ == "__main__":
    main()
