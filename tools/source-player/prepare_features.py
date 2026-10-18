#!/usr/bin/env python3
"""Extend an explicit pinned private preview bundle with existing approved fire inputs; never fetch."""
import argparse
import json
from pathlib import Path
from prepare import Bundle, PINS, digest, read, require

FIRE_SHA256 = "8bca6ef9e1f089209aca7201e30d281d54444b6cc5b621c42f339394ef04fe88"


def extend(base, base_hash, fire, fire_hash, output):
    base = base.resolve()
    manifest = json.loads(read(base, "manifest.json", base_hash))
    require(manifest["schema"] == "blitz-portable-source-player/v1" and
            manifest["profile"] == "original-teams-contact281-c2-m1",
            "Only the actual selected M1 private input generation may be extended.")
    bundle = Bundle()
    for record in manifest["files"]:
        bundle.add(base, record["path"], record["sha256"])
    entries, origins = dict(manifest["entrypoints"]), dict(manifest["origins"])
    require("fire" not in entries, "Base data already contains a fire generation.")
    fire = fire.resolve()
    require(fire_hash == FIRE_SHA256, "The exact approved SDL/17-TGA portable generation is required.")
    definition = bundle.document(fire, "manifest.json", fire_hash, "fire/manifest.json")
    require(definition["sourceCommit"] == manifest["sourceRevision"] and len(definition["inputs"]) == 18 and
            len(definition["frames"]) == 17 and not definition["nativeConvertedTextureEquivalent"] and
            not definition["generatedSpfxHeaderEquivalent"], "Original portable fire origin/identities required.")
    for record in definition["inputs"]:
        bundle.add(fire, record["payloadPath"], record["sha256"], "fire/" + record["payloadPath"])
    bundle.add(fire, definition["meshPath"], definition["meshSha256"], "fire/" + definition["meshPath"])
    entries["fire"] = "fire/manifest.json"
    origins["fire"] = definition["inputOrigin"] + "; source SDL factor4,17original frames; no native TEXUS/WMS claim"
    new, pin = bundle.persist(output, entries, origins)
    return {"manifestSha256": pin, "files": len(new["files"]), "baseManifestSha256": base_hash,
            "fireManifestSha256": fire_hash, "output": str(output.absolute())}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--base-data", type=Path, required=True)
    parser.add_argument("--base-manifest-sha256", required=True)
    parser.add_argument("--fire-bundle", type=Path, required=True)
    parser.add_argument("--fire-manifest-sha256", required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    try:
        print(json.dumps(extend(args.base_data, args.base_manifest_sha256, args.fire_bundle,
                                args.fire_manifest_sha256, args.output), indent=2))
    except (ValueError, OSError, KeyError) as error:
        parser.exit(1, "Feature data rejected: " + str(error) + "\n")


if __name__ == "__main__":
    main()
