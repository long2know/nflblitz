#!/usr/bin/env python3
"""Prepare explicit production272 inputs without changing historical fixture bytes or resource identities."""
import argparse
import json
from pathlib import Path
import sys

from contact_inputs import (ANIMATION_SHA, IMPACT_SHA, LEGACY_FIXTURE_SHA, PROFILE, PRODUCTION_PROFILE,
                            SCHEMA, SUPPLEMENTAL_SHA, animation_files, fixture_hashes)
from fetch_inputs import ROOT, REVISION, ignored_output, input_pins
from prepare_inputs import fixture_hashes as legacy_hashes, sha, verified_file


def prepare(legacy_fixture, production_bundle, supplemental_bundle, impact, output, reuse=False):
    legacy_fixture = Path(legacy_fixture).absolute()
    production_bundle, supplemental_bundle = Path(production_bundle).absolute(), Path(supplemental_bundle).absolute()
    impact = Path(impact).absolute()
    output = ignored_output(output)
    if not output.is_relative_to(ROOT / ".squad/.scratch/SourceData"):
        raise ValueError("Contact272 fixtures belong in this worktree's ignored .squad/.scratch/SourceData.")
    if output.exists():
        if not reuse:
            raise ValueError("Fixture destination exists; use a new output or explicit read-only --reuse.")
        return fixture_hashes(output / "fixture.json")
    for source in (legacy_fixture.parent, production_bundle, supplemental_bundle, impact.parent):
        if output.is_relative_to(source) or source.is_relative_to(output):
            raise ValueError("Fixture output and every read-only source must be independent.")
    verified_file(legacy_fixture.parent, legacy_fixture.name, LEGACY_FIXTURE_SHA)
    legacy = json.loads(legacy_fixture.read_bytes())
    if legacy.get("schema") != "source-kernel-real-inputs/v1":
        raise ValueError("The frozen legacy443 fixture is required, not a relabeled Contact272 fixture.")
    existing = legacy_hashes(legacy_fixture)
    contact_paths, _ = animation_files(production_bundle, "export-inventory.json", ANIMATION_SHA, 272, 24892)
    extra_paths, _ = animation_files(supplemental_bundle, "export-inventory.json", SUPPLEMENTAL_SHA, 115, 11402)
    verified_file(impact.parent, impact.name, IMPACT_SHA)
    output.mkdir(parents=True)
    for name in existing:
        source = Path(name)
        relative = "legacy-fixture.json" if source == legacy_fixture else source.relative_to(legacy_fixture.parent)
        destination = output / relative
        destination.parent.mkdir(parents=True, exist_ok=True)
        destination.write_bytes(source.read_bytes())
    for directory, source_root, paths in (("contact-animation", production_bundle, contact_paths),
                                          ("contact-supplemental", supplemental_bundle, extra_paths)):
        for source in paths:
            destination = output / directory / source.relative_to(source_root)
            destination.parent.mkdir(parents=True, exist_ok=True)
            destination.write_bytes(source.read_bytes())
    (output / "contact-impact.json").write_bytes(impact.read_bytes())
    manifest = dict(legacy)
    manifest.update({
        "schema": SCHEMA, "source_revision": REVISION,
        "production_profile": PRODUCTION_PROFILE, "original_source_pins": input_pins(PROFILE),
        "legacy_fixture": {"path": "legacy-fixture.json", "sha256": LEGACY_FIXTURE_SHA},
        "contact_animation": {"path": "contact-animation/export-inventory.json", "sha256": ANIMATION_SHA,
                              "clips": 272, "selected_frames": 24892},
        "contact_supplemental_animation": {"path": "contact-supplemental/export-inventory.json",
                                           "sha256": SUPPLEMENTAL_SHA, "clips": 115, "selected_frames": 11402},
        "contact_impact": {"path": "contact-impact.json", "sha256": IMPACT_SHA},
    })
    path = output / "fixture.json"
    path.write_text(json.dumps(manifest, indent=2, sort_keys=True) + "\n", encoding="utf-8")
    inputs = fixture_hashes(path)
    (output / "preparation.json").write_text(json.dumps({
        "schema": "source-contact-fixture-preparation/v1", "status": "prepared-inputs-only",
        "fixture_sha256": sha(path), "input_profile": PROFILE, "actual_inputs_sha256": inputs,
        "legacy_fixture_sha256_unchanged": sha(legacy_fixture) == LEGACY_FIXTURE_SHA,
        "full_regressions_executed": False, "full_gameplay_parity_claimed": False,
    }, indent=2) + "\n", encoding="utf-8")
    return inputs


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--profile", choices=(PROFILE,), required=True)
    parser.add_argument("--legacy-fixture", type=Path, required=True)
    parser.add_argument("--production-bundle", type=Path, required=True,
                        help="Actual output of the permanent Contact272 production animation producer.")
    parser.add_argument("--supplemental-bundle", type=Path, required=True,
                        help="Separate pinned115 reference; never used as the simulation's animation library.")
    parser.add_argument("--impact", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--reuse", action="store_true", help="Verify existing fixture bytes without rewriting them.")
    args = parser.parse_args()
    inputs = prepare(args.legacy_fixture, args.production_bundle, args.supplemental_bundle,
                     args.impact, args.output, args.reuse)
    print("PREPARED ONLY:", len(inputs), "verified local input files; no source campaign or Unity execution.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
