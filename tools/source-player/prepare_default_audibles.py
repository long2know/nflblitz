#!/usr/bin/env python3
"""Make one explicit metadata-only default-audibles root from the authenticated563 bundle."""

import argparse
import json
from pathlib import Path
import subprocess

from prepare import PROJECT, SOURCE, digest, portable, read, require


BASE_PROFILE = "original-teams-contact282-c2-m1-presnap-switch"
BASE_MANIFEST_SHA256 = "150409eb5a4b76272747d332e6387d68452161bf395eafb8f409c1c3656ac2e6"
PROFILE = "original-teams-contact282-c2-m1-default-audibles"
FEATURE_CONTRACT = "source-default-audibles/v1"
FACTORY = "SourceC2Composition.CreateDefaultAudiblesSimulation"
MANIFEST_SHA256 = "885c9e04cee21a2bb15d40b243744d3b1bfa6f4846ddf2ea9837c7fd3bec5548"


def require_manifest(manifest, raw):
    require(manifest.get("profile") == PROFILE, "Default-audibles manifest.profile must be explicit.")
    require(manifest.get("featureContract") == FEATURE_CONTRACT,
            "Default audibles require explicit featureContract=source-default-audibles/v1.")
    require(manifest.get("factory") == FACTORY,
            "Default audibles require explicit factory=SourceC2Composition.CreateDefaultAudiblesSimulation.")
    require(digest(raw) == MANIFEST_SHA256,
            "Default audibles require the exact metadata-only manifest and unchanged563 payload pins/entrypoints.")


def prepare(source, expected_manifest, output):
    source, output = source.absolute(), output.absolute()
    require(source.resolve() == source and output.resolve() == output,
            "Metadata-only input and output must be physical nonsymlink paths.")
    require(source != output and source not in output.parents and output not in source.parents,
            "Default-audibles output must not overlap the immutable input.")
    require(PROJECT in output.parents,
            "Private default-audibles output must remain inside this checkout.")
    require(not output.exists(), "Choose a NEW private default-audibles output; existing roots are immutable.")
    require(subprocess.run(["git", "check-ignore", "-q", str(output / "manifest.json")],
                           cwd=PROJECT).returncode == 0,
            "Original data output must be Git-ignored.")
    require(expected_manifest == BASE_MANIFEST_SHA256,
            "The actual approved563 source manifest pin is required.")
    raw = read(source, "manifest.json", expected_manifest, 1024 * 1024)
    original = json.loads(raw)
    require(original.get("schema") == "blitz-portable-source-player/v1" and
            original.get("profile") == BASE_PROFILE and original.get("sourceRevision") == SOURCE and
            len(original["files"]) == 563 and "featureContract" not in original and "factory" not in original,
            "The authentic563 pre-snap-switch source bundle is required, not relabeled data.")
    expected, aliases = {"manifest.json"}, {"manifest.json"}
    payloads, reads = {}, []
    for pin in original["files"]:
        path = portable(pin["path"])
        require(path.lower() not in aliases, "Duplicate/aliased original payload: " + path)
        aliases.add(path.lower())
        expected.add(path)
        data = read(source, path, pin["sha256"])
        require(len(data) == pin["bytes"], "Inherited byte count changed: " + path)
        payloads[path] = data
        reads.append(dict(path=path, bytes=len(data), sha256=digest(data)))
    actual = set()
    for path in source.rglob("*"):
        require(not path.is_symlink(), "Symlinked source input: " + path.relative_to(source).as_posix())
        if path.is_file():
            actual.add(path.relative_to(source).as_posix())
    require(actual == expected, "Unlisted/missing original563 inputs: " +
            repr(sorted(actual.symmetric_difference(expected))))
    successor = dict(original, profile=PROFILE, featureContract=FEATURE_CONTRACT, factory=FACTORY)
    encoded = (json.dumps(successor, indent=2, sort_keys=True) + "\n").encode()
    require_manifest(successor, encoded)
    require({k: v for k, v in successor.items() if k not in ("profile", "featureContract", "factory")} ==
            {k: v for k, v in original.items() if k != "profile"},
            "Only profile/feature/factory manifest metadata may change.")
    output.mkdir(parents=True)
    for path, data in payloads.items():
        target = output / path
        target.parent.mkdir(parents=True, exist_ok=True)
        require(target.parent.resolve() == target.parent and not target.is_symlink(),
                "Symlinked private output: " + path)
        with target.open("xb") as stream:
            stream.write(data)
    with (output / "manifest.json").open("xb") as stream:
        stream.write(encoded)
    verified = []
    for pin in original["files"]:
        path = pin["path"]
        copied = read(output, path, pin["sha256"])
        require(copied == payloads[path] and len(copied) == pin["bytes"],
                "Private metadata-only payload copy differs: " + path)
        require(read(source, path, pin["sha256"]) == copied,
                "Original immutable source changed during metadata preparation: " + path)
        verified.append(dict(path=path, bytes=len(copied), sha256=digest(copied)))
    require(read(source, "manifest.json", expected_manifest) == raw and
            (output / "manifest.json").read_bytes() == encoded,
            "Original/successor manifest changed during metadata preparation.")
    return dict(schema="source-player-default-audibles-data/v1", profile=PROFILE,
                featureContract=FEATURE_CONTRACT, factory=FACTORY, manifestSha256=digest(encoded),
                sourceManifestSha256=expected_manifest, sourceRoot=str(source), outputRoot=str(output),
                fileCount=563, payloadBytes=sum(pin["bytes"] for pin in reads),
                originalPinsPreserved=True, originalEntrypointsPreserved=True,
                manifestMetadataOnly=True, formatMigration=False, sourceReads=reads, outputReads=verified)


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--input", type=Path, required=True)
    parser.add_argument("--input-manifest-sha256", required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args(argv)
    try:
        print(json.dumps(prepare(args.input, args.input_manifest_sha256, args.output),
                         indent=2, sort_keys=True))
    except (OSError, ValueError, KeyError, TypeError) as error:
        parser.exit(1, "Default-audibles player data rejected: " + str(error) + "\n")


if __name__ == "__main__":
    main()
