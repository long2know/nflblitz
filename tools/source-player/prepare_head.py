#!/usr/bin/env python3
"""Extend a verified private player bundle with the original source-authored phead; never fetch."""
import argparse
import json
from pathlib import Path
from prepare import Bundle, read, require


def extend(base, base_hash, head, head_hash, output):
    base = base.absolute()
    manifest = json.loads(read(base, "manifest.json", base_hash))
    require(manifest["schema"] == "blitz-portable-source-player/v1" and
            manifest["profile"] == "original-teams-contact281-c2-m1",
            "The actual original M1 player input generation is required.")
    bundle = Bundle()
    for record in manifest["files"]:
        bundle.add(base, record["path"], record["sha256"])
    entries, origins = dict(manifest["entrypoints"]), dict(manifest["origins"])
    require("poppedHead" not in entries, "Base data already contains a popped-head generation.")
    head = head.absolute()
    definition = bundle.document(head, "popped-head.json", head_hash, "popped-head/popped-head.json")
    require(definition["schema"] == "original-source-authored-popped-head/v1" and
            definition["sourceRevision"] == manifest["sourceRevision"] and
            definition["sourceSha256"] == "3fb4e566b57670e397fde16c6f515d06c95379420074c2d6c7b111bca843e257" and
            definition["headerSha256"] == "f835cda5498db7b3f2783b3cfda99d07a2ef765f821f1655c1b4be3d555a57b5" and
            not definition["nativeGeneratedHeaderEquivalent"] and definition["rawTextureCode"] == 4 and
            definition["logicalTextureSlot"] == 1 and definition["sourceHeadJoint"] == 8,
            "Exact original phead SDL reconstruction and raw4/logical1 mapping required.")
    bundle.add(head, definition["sourcePayload"], definition["sourceSha256"],
               "popped-head/" + definition["sourcePayload"])
    bundle.add(head, definition["headerPath"], definition["headerSha256"],
               "popped-head/" + definition["headerPath"])
    entries["poppedHead"] = "popped-head/popped-head.json"
    origins["poppedHead"] = definition["origin"]
    prepared, pin = bundle.persist(output, entries, origins)
    return {"manifestSha256": pin, "files": len(prepared["files"]), "baseManifestSha256": base_hash,
            "poppedHeadManifestSha256": head_hash, "output": str(output.absolute())}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--base-data", type=Path, required=True)
    parser.add_argument("--base-manifest-sha256", required=True)
    parser.add_argument("--head-bundle", type=Path, required=True)
    parser.add_argument("--head-manifest-sha256", required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    try:
        print(json.dumps(extend(args.base_data, args.base_manifest_sha256, args.head_bundle,
                                args.head_manifest_sha256, args.output), indent=2))
    except (ValueError, OSError, KeyError) as error:
        parser.exit(1, "Original head inputs rejected: " + str(error) + "\n")


if __name__ == "__main__":
    main()
