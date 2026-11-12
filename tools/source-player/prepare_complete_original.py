#!/usr/bin/env python3
"""Create a separate immutable complete-original package; never mutate563/885c."""

import argparse
import json
import sys
from pathlib import Path
import subprocess

from prepare import PROJECT, digest, portable, read, require
from prepare_default_audibles import BASE_MANIFEST_SHA256

PROFILE = "original-teams-complete-original303"
FEATURE_CONTRACT = "source-complete-original-audiovisual/v1"
FACTORY = "SourceC2Composition.CreateCompleteOriginalSimulation"
ANIMATION = "073092789ea86377db7e485a26b3a02d6606a1fca5a195e99f6a44abc5c70adf"
CALLBACK = "dd5ae83a03b4eee5b12cbae0dbcd604e15db89556157e63c7eabc75733e36e19"
IMPACT = "dc4428dd86056f70a93ee027c952d39437c4c0c9b9f9a8f3679c3b4908d78c4b"
PRESENTATION = "7f85b1848a61ba68f1e3e3e74f57d4ad6a7c517078f9b7e59d55a112acaa3f8d"
AUDIO = "bb3cc14f6c1797dff79a92e192aab3f7cf1d4be58f39a99facbbcd56a598794a"
MANIFEST_SHA256 = "fef5f838759a50afdacc4d94b0749d580b1ac90406624c887fc6ea01dc6cfa42"
SOURCE_BANKS_PROFILE = "original-teams-complete-original303-source-banks"
SOURCE_BANKS_FACTORY = "SourceC2Composition.CreateCompleteOriginalSourceBanksSimulation"
SOURCE_BANKS_AUDIO = "a36dd1938a305114bb8699bec70395466ea2707fcd8f8d2aa8ccb356b0e9e823"
SOURCE_BANKS_MANIFEST_SHA256 = "9df8e3a0e0cfddc0784e01c094547fdbd2fda1cb72d97d8cefafb515ca19097b"
RECORDS_PROFILE = "original-teams-complete-original303-records"
RECORDS_FACTORY = "SourceC2Composition.CreateCompleteOriginalRecordsSimulation"
RECORDS_FEATURE = "source-complete-original-records-audiovisual/v1"
RECORDS_PRESENTATION = "a0bcad9c66d87fd596dd56c50df64e0124b27d4bd854adb69d3ae5a275200b22"
RECORDS_MANIFEST_SHA256 = "c03ea3519708063f6b89fac08a018384ba302e4381db9bde1d8942dfd4c5dd8c"
FINAL_PROFILE = "original-teams-complete-original303-final"
FINAL_FACTORY = "SourceC2Composition.CreateCompleteOriginalFinalSimulation"
FINAL_IMPACT = "121134430e77a16122dee6274525bafffe013bd6b1424587f465816c654459e2"
FINAL_MANIFEST_SHA256 = "6ad3183827cc27e5c74fd9bc140f4295266285ab9f955a190da407d825d004ce"
PREMATCH_PROFILE = "original-teams-complete-original303-prematch"
PREMATCH_FACTORY = "SourceC2Composition.CreateCompleteOriginalPrematchSimulation"
PREMATCH_FEATURE = "source-complete-original-prematch-records-audiovisual/v1"
PREMATCH_PRESENTATION = "a2ddcc4c6e346364e1b33ef9f50c28373507a071a381b7dd9188fb38a3d86433"
PREMATCH_AUDIO = "37cb399381bc1592ea1156504d3be3eddd8494d28783b3bf285f5d413065e487"
PREMATCH_MANIFEST_SHA256 = "309ca9cf4f7f0d3b9f8b43427ab581fda4257d072ea00bfdb4ccc0003c2cba4f"
SELECTED_WORLD_PROFILE = "original-teams-complete-original303-selected-world-v2"
SELECTED_WORLD_SCHEMA = "blitz-portable-source-player/v2"
SELECTED_WORLD_FEATURE = "source-complete-original-prematch-selected-world/v2"
SELECTED_WORLD_FACTORY = PREMATCH_FACTORY
SELECTED_WORLD_MANIFESTS = {
    "aeb2efdebdbe260f63f6d785c461048dc9cec337098490761c2815cd8ffc4edd": {
        "files": 1114, "catalog": "b66d491f6f1d7956de2fd9f103f1c6d227a22a48d313e29057b04defaaf6bace"
    },
    "ad66460368f7c71e61cd44146fe6fa362da44b8b1ea43958de7434b6d6df2c8f": {
        "files": 1110, "catalog": "b541301645c7afe8d0b0c1928e86b43fb6ae522ce117f6124238d00630ae5e13"
    },
}


def require_manifest(manifest, raw):
    if manifest.get("profile") == SELECTED_WORLD_PROFILE:
        expected = SELECTED_WORLD_MANIFESTS.get(digest(raw))
        require(expected is not None and manifest.get("schema") == SELECTED_WORLD_SCHEMA and
                manifest.get("featureContract") == SELECTED_WORLD_FEATURE and
                manifest.get("factory") == SELECTED_WORLD_FACTORY and
                len(manifest.get("files", [])) == expected["files"] and
                manifest.get("selectedWorld", {}).get("catalogSha256") == expected["catalog"],
                "The exact separately authenticated selected-world v2 successor is required.")
        return
    if manifest.get("profile") == PREMATCH_PROFILE:
        require(digest(raw) == PREMATCH_MANIFEST_SHA256 and
                manifest.get("featureContract") == PREMATCH_FEATURE and manifest.get("factory") == PREMATCH_FACTORY
                and len(manifest.get("files", [])) == 963,
                "The exact963-input pregame-source/DCS233-mixer/record-art successor is required.")
        return
    if manifest.get("profile") == FINAL_PROFILE:
        require(digest(raw) == FINAL_MANIFEST_SHA256 and
                manifest.get("featureContract") == RECORDS_FEATURE and manifest.get("factory") == FINAL_FACTORY
                and len(manifest.get("files", [])) == 953,
                "The exact final953-input TD-qualified record/CAP successor is required.")
        return
    if manifest.get("profile") == RECORDS_PROFILE:
        require(digest(raw) == RECORDS_MANIFEST_SHA256 and
                manifest.get("featureContract") == RECORDS_FEATURE and manifest.get("factory") == RECORDS_FACTORY
                and len(manifest.get("files", [])) == 953,
                "The exact953-input original record/CAP successor is required, not an earlier data-family alias.")
        return
    if manifest.get("profile") == SOURCE_BANKS_PROFILE:
        require(digest(raw) == SOURCE_BANKS_MANIFEST_SHA256 and
                manifest.get("featureContract") == FEATURE_CONTRACT and manifest.get("factory") == SOURCE_BANKS_FACTORY
                and len(manifest.get("files", [])) == 692,
                "The exact692-input source-bank successor is required, not the661 or old563/885c package.")
        return
    require(digest(raw) == MANIFEST_SHA256 and manifest.get("profile") == PROFILE
            and manifest.get("featureContract") == FEATURE_CONTRACT and manifest.get("factory") == FACTORY
            and len(manifest.get("files", [])) == 661,
            "The exact661-input complete-original generation/feature/factory is required; no old-profile alias.")


def canonical(value):
    return (json.dumps(value, indent=2, sort_keys=True) + "\n").encode("ascii")


def prepare(base, animation, presentation, audio, output, source_banks=False, records=False, final_impact=None, prematch=False):
    records = records or final_impact is not None
    require(not prematch or final_impact is not None, "The original pregame successor requires the exact final TD impact family.")
    base, animation, presentation, audio, output = map(lambda p: p.absolute(),
                                                      (base, animation, presentation, audio, output))
    for path in (base, animation, presentation, audio, output):
        require(path.resolve() == path, "Physical nonsymlink original data roots are required.")
    require(output.is_relative_to(PROJECT / ".squad/.scratch") and not output.exists(),
            "Choose a create-new ignored complete-original data root.")
    for path in (base, animation, presentation, audio):
        require(not output.is_relative_to(path) and not path.is_relative_to(output), "Original data input/output overlap.")
    require(subprocess.run(["git", "check-ignore", "-q", str(output / "manifest.json")],
                           cwd=PROJECT).returncode == 0, "Original data output must stay Git-ignored.")
    original_raw = read(base, "manifest.json", BASE_MANIFEST_SHA256)
    original = json.loads(original_raw)
    require(original["profile"] == "original-teams-contact282-c2-m1-presnap-switch" and len(original["files"]) == 563,
            "The exact approved563 payload is the only ancestor, not a relabeled/partial package.")
    payloads = {entry["path"]: read(base, portable(entry["path"]), entry["sha256"]) for entry in original["files"]}
    inventory = json.loads(read(animation, "export-inventory.json", ANIMATION))
    require(len(inventory["clips"]) == 303 and len({e["runtime_name"] for e in inventory["clips"]}) == 303,
            "A real unique303 inventory is required.")
    for entry in inventory["clips"]:
        name = "animation/" + portable(entry["path"])
        raw = read(animation, entry["path"], entry["sha256"])
        require(name not in payloads or payloads[name] == raw, "An inherited clip changed:" + name)
        payloads[name] = raw
    for name, source, pin in (
        ("animation/export-inventory-complete-original.json", "export-inventory.json", ANIMATION),
        ("kernel/c2-complete-original-callback-data.json", "c2-complete-original-callback-data.json", CALLBACK),
        ("kernel/impact-complete-original-presentation.json", "impact-complete-original-presentation.json", IMPACT),
    ):
        payloads[name] = (read(final_impact.parent, final_impact.name, FINAL_IMPACT)
                          if final_impact is not None and name == "kernel/impact-complete-original-presentation.json"
                          else read(animation, source, pin))
    presentation_pin = PREMATCH_PRESENTATION if prematch else RECORDS_PRESENTATION if records else PRESENTATION
    art = json.loads(read(presentation, "manifest.json", presentation_pin))
    payloads["original/manifest.json"] = read(presentation, "manifest.json", presentation_pin)
    for entry in art["sources"]:
        payloads["original/" + entry["path"]] = read(presentation, entry["path"], entry["sha256"])
    for entry in art["rasters"]:
        payloads["original/" + entry["path"]] = read(presentation, entry["path"], entry["sha256"])
    audio_pin = PREMATCH_AUDIO if prematch else SOURCE_BANKS_AUDIO if source_banks or records else AUDIO
    sounds = json.loads(read(audio, "manifest.json", audio_pin))
    payloads["audio/manifest.json"] = read(audio, "manifest.json", audio_pin)
    for entry in sounds["banks"]:
        payloads["audio/" + entry["path"]] = read(audio, entry["path"], entry["sha256"])
    if prematch:
        payloads["audio/native-reference.json"] = read(audio, "native-reference.json",
            "b231397143c03d0a707d67116aaccacde655b6245816bae8c4fd72a8f4937ed1")
    entries = dict(original["entrypoints"], animations="animation/export-inventory-complete-original.json",
                   callbacks="kernel/c2-complete-original-callback-data.json",
                   impact="kernel/impact-complete-original-presentation.json",
                   originalPresentation="original/manifest.json", originalAudio="audio/manifest.json")
    profile, factory = (PREMATCH_PROFILE, PREMATCH_FACTORY) if prematch else (FINAL_PROFILE, FINAL_FACTORY) if final_impact is not None else (RECORDS_PROFILE, RECORDS_FACTORY) if records else (
        (SOURCE_BANKS_PROFILE, SOURCE_BANKS_FACTORY) if source_banks else (PROFILE, FACTORY))
    feature = PREMATCH_FEATURE if prematch else RECORDS_FEATURE if records else FEATURE_CONTRACT
    manifest = dict(original, profile=profile, featureContract=feature, factory=factory,
                    ancestorManifestSha256=BASE_MANIFEST_SHA256, entrypoints=entries,
                    features=dict(originalSelectedClipInputs=303, originalShadowMaterials=4,
                                  originalCabinetImages=sum(r["kind"] == "image" for r in art["rasters"]),
                                  originalCompiledPlaylistPrograms=len(sounds["programs"]),
                                  audibleSelectionContract="source-default-audibles/v1",
                                  defaultFlights=["portable-gameflow", "portable-instructions", "portable-world", "original-audio"] +
                                      (["portable-records"] if records else []),
                                  nativeDspEquivalent=False, nativePixelsEquivalent=False),
                    files=[dict(path=name, bytes=len(raw), sha256=digest(raw)) for name, raw in sorted(payloads.items())])
    if prematch:
        manifest["features"]["originalPreGameSoundProcessService"] = "source-pregame-after-accepted-start/v1"
        manifest["features"]["originalImplicitGlobalMixerPrograms"] = sounds["implicitGlobalMixerPrograms"]
        manifest["features"]["defaultFlights"].append("original-pregame-source-sound")
    encoded = canonical(manifest)
    require_manifest(manifest, encoded)
    output.mkdir(parents=True)
    for name, raw in {**payloads, "manifest.json": encoded}.items():
        target = output / portable(name)
        target.parent.mkdir(parents=True, exist_ok=True)
        with target.open("xb") as stream:
            stream.write(raw)
    for entry in manifest["files"]:
        require(read(output, entry["path"], entry["sha256"]) == payloads[entry["path"]],
                "Prepared complete-original bytes differ:" + entry["path"])
    require(read(base, "manifest.json", BASE_MANIFEST_SHA256) == original_raw, "Approved563 manifest changed.")
    return dict(schema="source-player-complete-original-data/v1", profile=profile, featureContract=feature,
                factory=factory, outputRoot=str(output), manifestSha256=digest(encoded), fileCount=len(payloads),
                bytes=sum(len(raw) for raw in payloads.values()), features=manifest["features"],
                original563PinsPreserved=True, old885cManifestUnchanged=True)


def prepare_selected_world(base, world, world_sha256, output, seal_only=False):
    import copy
    base, world, output = base.resolve(), world.resolve(), output.absolute()
    require(output.resolve() == output and output.is_relative_to(PROJECT / ".squad/.scratch") and
            not output.exists(), "Choose a create-new owned ignored selected-world output.")
    require(not output.is_relative_to(base) and not output.is_relative_to(world), "Data roots overlap.")
    original_raw = read(base, "manifest.json", PREMATCH_MANIFEST_SHA256)
    original = json.loads(original_raw)
    require(original["profile"] == PREMATCH_PROFILE and len(original["files"]) == 963,
            "The exact historical963 prematch package is required.")
    inherited = {entry["path"]: read(base, entry["path"], entry["sha256"]) for entry in original["files"]}
    catalog_raw = read(world, "catalog.json", world_sha256)
    catalog = json.loads(catalog_raw)
    require(catalog["schema"] == "blitz-source-environment-catalog/v1" and
            catalog["sourceCommit"] == original["sourceRevision"] and len(catalog["entries"]) == 31,
            "A real31-team source world catalog is required.")
    default = next((entry for entry in catalog["entries"]
                    if entry["manifestSha256"] == catalog["defaultManifestSha256"]), None)
    require(default is not None, "Catalog default world is missing.")
    default_world = json.loads(read(world, default["manifestPath"], default["manifestSha256"]))
    require(default_world["schema"] == "blitz-source-environment/v2" and
            default_world["selectionId"] == catalog["defaultSelectionId"] and
            default_world["fieldSelector"] == catalog["fieldSelector"] and
            default_world["stadiumSelector"] == catalog["stadiumSelector"],
            "The actual default source selection differs from the catalog.")
    payloads = {name: raw for name, raw in inherited.items() if not name.startswith("world/")}
    payloads["world/catalog.json"] = catalog_raw
    aliases = set()
    for entry in catalog["files"]:
        relative = portable(entry["path"])
        require(relative.lower() not in aliases, "Duplicate world catalog payload: " + relative)
        aliases.add(relative.lower())
        raw = read(world, relative, entry["sha256"])
        require(len(raw) == entry["bytes"], "World catalog payload length differs: " + relative)
        payloads["world/" + relative] = raw
    bootstrap_path, settings_path = original["entrypoints"]["bootstrap"], original["entrypoints"]["settings"]
    bootstrap, settings = json.loads(inherited[bootstrap_path]), json.loads(inherited[settings_path])
    if bootstrap["field_index"] != catalog["fieldSelector"]:
        bootstrap["field_index"] = catalog["fieldSelector"]
        payloads[bootstrap_path] = canonical(bootstrap)
    side = settings["presentation"]["designated_home_side"]
    require(side in (0, 1), "The actual designated home side must be explicit.")
    team = "home_team" if side == 0 else "away_team"
    if settings[team]["team"] != default["homeTeamIndex"]:
        settings[team]["team"] = default["homeTeamIndex"]
        payloads[settings_path] = canonical(settings)
    manifest = copy.deepcopy(original)
    manifest.update(schema=SELECTED_WORLD_SCHEMA, profile=SELECTED_WORLD_PROFILE,
                    featureContract=SELECTED_WORLD_FEATURE, factory=SELECTED_WORLD_FACTORY,
                    ancestorManifestSha256=PREMATCH_MANIFEST_SHA256,
                    entrypoints=dict(original["entrypoints"], world="world/" + default["manifestPath"],
                                     worldCatalog="world/catalog.json"),
                    selectedWorld=dict(schema="source-complete-selected-world-admission/v2",
                                       catalogSha256=world_sha256, field=catalog["fieldSelector"],
                                       stadium=catalog["stadiumSelector"],
                                       defaultHomeTeamIndex=default["homeTeamIndex"],
                                       defaultHomePrefix=default["homePrefix"],
                                       defaultSelectionId=catalog["defaultSelectionId"]),
                    files=[dict(path=name, bytes=len(raw), sha256=digest(raw))
                           for name, raw in sorted(payloads.items())])
    encoded = canonical(manifest)
    if not seal_only:
        require_manifest(manifest, encoded)
    output.mkdir(parents=True)
    for name, raw in {**payloads, "manifest.json": encoded}.items():
        target = output / portable(name)
        target.parent.mkdir(parents=True, exist_ok=True)
        with target.open("xb") as stream:
            stream.write(raw)
    for entry in manifest["files"]:
        read(output, entry["path"], entry["sha256"])
    require(read(base, "manifest.json", PREMATCH_MANIFEST_SHA256) == original_raw,
            "Historical963 manifest changed during preparation.")
    return dict(schema="complete-selected-world-package/v2", profile=SELECTED_WORLD_PROFILE,
                manifestSha256=digest(encoded), fileCount=len(payloads),
                bytes=sum(len(raw) for raw in payloads.values()), catalogSha256=world_sha256,
                selection=manifest["selectedWorld"], admitted=not seal_only, historical963Unmodified=True)


def main():
    if any(value == "--selected-world" or value.startswith("--selected-world=") for value in sys.argv[1:]):
        parser = argparse.ArgumentParser(description="Prepare an explicit selected-world v2 complete successor.")
        for name in ("base", "selected-world", "output"):
            parser.add_argument("--" + name, type=Path, required=True)
        parser.add_argument("--world-sha256", required=True)
        parser.add_argument("--seal-only", action="store_true")
        args = parser.parse_args()
        print(json.dumps(prepare_selected_world(args.base, args.selected_world, args.world_sha256,
                                               args.output, args.seal_only), indent=2))
        return
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ("base", "animation", "presentation", "audio", "output"):
        parser.add_argument("--" + name, type=Path, required=True)
    parser.add_argument("--source-banks", action="store_true")
    parser.add_argument("--records", action="store_true")
    parser.add_argument("--final-impact", type=Path)
    parser.add_argument("--prematch", action="store_true")
    args = parser.parse_args()
    print(json.dumps(prepare(args.base, args.animation, args.presentation, args.audio, args.output, args.source_banks, args.records, args.final_impact, args.prematch), indent=2))


if __name__ == "__main__":
    main()
