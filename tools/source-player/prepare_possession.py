#!/usr/bin/env python3
"""Add required CAMBOT message speech data without rewriting any inherited asset pin."""
import argparse
import json
from pathlib import Path
from prepare import Bundle, digest, require

SPEECH_SHA256 = "ac9bdf7c37c8f3d9d1e7184ca96e2d9c28aad2e157940d05194513b8f06da5df"


def prepare(source, expected_manifest, speech, output):
    source, speech = source.absolute(), speech.absolute()
    raw = (source / "manifest.json").read_bytes()
    require(digest(raw) == expected_manifest, "Immutable prepared player manifest changed.")
    original = json.loads(raw)
    require(original["schema"] == "blitz-portable-source-player/v1" and
            original["profile"] == "original-teams-contact281-c2-m1" and
            "normalCamera" in original["entrypoints"] and "possessionSound" not in original["entrypoints"],
            "An actual original-camera M1 bundle without message operands is required.")
    bundle = Bundle()
    for pin in original["files"]:
        bundle.add(source, pin["path"], pin["sha256"])
    bundle.add(speech.parent, speech.name, SPEECH_SHA256, "camera/possession-speech.bytes")
    entries, origins = dict(original["entrypoints"]), dict(original["origins"])
    entries["possessionSound"] = "camera/possession-speech.bytes"
    origins["possessionServicesBaseManifestSha256"] = expected_manifest
    origins["possessionServices"] = "Active original CAMBOT possession/message process operands; portable declared host, not native scheduling or DSP playback."
    manifest, manifest_hash = bundle.persist(output, entries, origins)
    pins = {pin["path"]: pin for pin in manifest["files"]}
    require(all(pins[pin["path"]] == pin for pin in original["files"]) and
            len(pins) == len(original["files"]) + 1, "Every inherited original pin must be retained.")
    return dict(schema="source-player-possession-data/v1", manifestSha256=manifest_hash,
                baseManifestSha256=expected_manifest, fileCount=len(pins), originalPinsPreserved=True,
                speechSha256=SPEECH_SHA256, formatMigration=False)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--input", type=Path, required=True)
    parser.add_argument("--input-manifest-sha256", required=True)
    parser.add_argument("--speech", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    try:
        print(json.dumps(prepare(args.input, args.input_manifest_sha256, args.speech, args.output), indent=2))
    except (OSError, ValueError, KeyError) as error:
        parser.exit(1, "Original possession data rejected: " + str(error) + "\n")


if __name__ == "__main__":
    main()
