#!/usr/bin/env python3
"""Extend an immutable prepared player bundle with required original camera data, without rewriting any old pin."""
import argparse
import json
from pathlib import Path

from prepare import Bundle, digest, require

CAMERA_SHA256 = "43e9ccbf3c5162ac99b86d1c2f4925d834e5b7d78fb7b1631f4d33c431c4f3c4"


def prepare(source, expected_manifest, camera, output):
    source, camera, output = source.absolute(), camera.absolute(), output.absolute()
    raw = (source / "manifest.json").read_bytes()
    require(digest(raw) == expected_manifest, "Immutable prepared player manifest changed.")
    original = json.loads(raw)
    require(original["schema"] == "blitz-portable-source-player/v1" and
            original["profile"] == "original-teams-contact281-c2-m1" and
            "normalCamera" not in original["entrypoints"], "An unextended actual M1 player bundle is required.")
    bundle = Bundle()
    for pin in original["files"]:
        bundle.add(source, pin["path"], pin["sha256"])
    bundle.add(camera.parent, camera.name, CAMERA_SHA256, "camera/normal-camera-original.json")
    entrypoints = dict(original["entrypoints"])
    entrypoints["normalCamera"] = "camera/normal-camera-original.json"
    origins = dict(original["origins"])
    origins["camera"] = "Original active CAMBOT normal/integrator and eleven kickoff/two field-goal entries; " \
        "unchanged seven-table impact catalog. Host/native arithmetic and scheduler qualifications remain."
    origins["normalCameraBaseManifestSha256"] = expected_manifest
    manifest, manifest_hash = bundle.persist(output, entrypoints, origins)
    pins = {pin["path"]: pin for pin in manifest["files"]}
    require(all(pins[pin["path"]] == pin for pin in original["files"]) and
            len(manifest["files"]) == len(original["files"]) + 1,
            "Every inherited original data pin must be retained byte-for-byte.")
    return dict(schema="source-player-original-camera-data/v1", output=str(output),
                baseManifestSha256=expected_manifest, baseFileCount=len(original["files"]),
                manifestSha256=manifest_hash, fileCount=len(manifest["files"]),
                cameraSha256=CAMERA_SHA256, originalPinsPreserved=True, formatMigration=False)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--input", type=Path, required=True)
    parser.add_argument("--input-manifest-sha256", required=True)
    parser.add_argument("--camera", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    try:
        receipt = prepare(args.input, args.input_manifest_sha256, args.camera, args.output)
        print(json.dumps(receipt, indent=2, sort_keys=True))
    except (OSError, ValueError, KeyError) as error:
        parser.exit(1, "Original camera data rejected: " + str(error) + "\n")


if __name__ == "__main__":
    main()
