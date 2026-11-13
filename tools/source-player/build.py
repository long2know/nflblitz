#!/usr/bin/env python3
"""Publish the actual .NET browser app and copy an explicit ignored original-data bundle."""

import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import xml.etree.ElementTree as ET
from prepare import portable, read, require
from prepare_default_audibles import (PROFILE as AUDIBLES_PROFILE, FEATURE_CONTRACT as AUDIBLES_FEATURE,
                                      FACTORY as AUDIBLES_FACTORY, require_manifest as require_audibles_manifest)
from prepare_complete_original import (PROFILE as COMPLETE_PROFILE, FEATURE_CONTRACT as COMPLETE_FEATURE,
                                      FACTORY as COMPLETE_FACTORY, SOURCE_BANKS_PROFILE, SOURCE_BANKS_FACTORY,
                                      RECORDS_PROFILE, RECORDS_FACTORY, RECORDS_FEATURE,
                                      FINAL_PROFILE, FINAL_FACTORY,
                                      PREMATCH_PROFILE, PREMATCH_FACTORY, PREMATCH_FEATURE,
                                      SELECTED_WORLD_PROFILE, SELECTED_WORLD_SCHEMA,
                                      SELECTED_WORLD_FACTORY, SELECTED_WORLD_FEATURE,
                                      require_manifest as require_complete_manifest)


HERE = Path(__file__).resolve().parent
PROJECT = HERE.parents[1]
OLD_PROFILE = "original-teams-contact281-c2-m1"
PROFILE = "original-teams-contact282-c2-m1-presnap-switch"
CALLER_DRAIN_PROFILE = "aot-rooted-bounded-caller-drain/v1"
AV306_PROFILE = "original-teams-complete-av306"
AV306_SCHEMA = "blitz-portable-source-player/v3"
AV306_FEATURE = "source-complete-original-animation-course-audiovisual/v3"
AV306_FACTORY = "SourceC2Composition.CreateCompleteAv306Simulation"
AV306_MANIFEST_SHA256 = "51e94caffcf5875e024206eb069dfd905ee13e134b09e7f5020ff68349461e5d"
AV306_CALLER_DRAIN_PROFILE = "aot-rooted-bounded-caller-drain-av306/v1"
TWO_STATION_RECORDS_PROFILE = "original-teams-complete-av306-two-station-records-v1"
TWO_STATION_RECORDS_FEATURE = "source-complete-original-two-station-records-audiovisual/v1"
TWO_STATION_RECORDS_FACTORY = "SourceC2Composition.CreateCompleteAv306TwoStationRecordsSimulation"
TWO_STATION_RECORDS_MANIFEST_SHA256 = "745129381c326e9f237ac4e6c3e020503553aada3e1f993dfb9321c09ee8f32d"
TWO_STATION_RECORDS_SETTINGS = "settings-av306-two-station-records-v1.json"
TWO_STATION_RECORDS_SETTINGS_SHA256 = "6ad8eb0239d2b0432d55dcf47e2fd893a38397e036befa682ffb9b0ed89c4fc2"
TWO_STATION_RECORDS_CALLER_DRAIN_PROFILE = "aot-rooted-bounded-caller-drain-av306-two-station-records/v1"
PRIMARY1114_MANIFEST_SHA256 = "aeb2efdebdbe260f63f6d785c461048dc9cec337098490761c2815cd8ffc4edd"


def caller_drain_profile(profile, manifest_sha256):
    if profile == SELECTED_WORLD_PROFILE and manifest_sha256 == PRIMARY1114_MANIFEST_SHA256:
        return CALLER_DRAIN_PROFILE
    if profile == AV306_PROFILE and manifest_sha256 == AV306_MANIFEST_SHA256:
        return AV306_CALLER_DRAIN_PROFILE
    if profile == TWO_STATION_RECORDS_PROFILE and manifest_sha256 == TWO_STATION_RECORDS_MANIFEST_SHA256:
        return TWO_STATION_RECORDS_CALLER_DRAIN_PROFILE
    raise ValueError("The bounded caller-drain profile requires its exact primary1114, AV306 or named TwoStation data generation.")


def require_av306_manifest(manifest, raw):
    require(hashlib.sha256(raw).hexdigest() == AV306_MANIFEST_SHA256 and
            manifest.get("schema") == AV306_SCHEMA and manifest.get("profile") == AV306_PROFILE and
            manifest.get("featureContract") == AV306_FEATURE and manifest.get("factory") == AV306_FACTORY and
            manifest.get("ancestorManifestSha256") == PRIMARY1114_MANIFEST_SHA256,
            "The exact distinct AV306 v3 manifest, feature/factory and immutable1114 ancestor are required.")
    pins = manifest["files"]
    require(len(pins) == 1153 and sum(pin["bytes"] for pin in pins) == 300324289,
            "AV306 requires exactly1153 pinned input files/300324289 bytes.")
    introduced = {
        "animation/clips/QB_RDY.json", "animation/clips/QB_RDYF.json", "animation/clips/RUSH_I.json",
        "animation/export-inventory-complete-av306.json", "camera/complete-av306-course.json",
        "kernel/c2-complete-av306-callback-data.json", "kernel/impact-complete-av306-presentation.json",
        "audio/complete-av306-manifest.json"
    }
    introduced.update(pin["path"] for pin in pins if pin["path"].startswith("audio/team-speech/") and
                      pin["path"].endswith(".saud"))
    def inventory_hash(rows):
        return hashlib.sha256("".join(
            f'{pin["path"]}\t{pin["bytes"]}\t{pin["sha256"]}\n'
            for pin in sorted(rows, key=lambda pin: pin["path"])).encode()).hexdigest()
    require(len(introduced) == 39 and inventory_hash(pin for pin in pins if pin["path"] in introduced) ==
            "4ebd1e83c5fcb6818d1d2a68d1531175c72c7893628bf99b2931b17a30bb39e5" and
            inventory_hash(pin for pin in pins if pin["path"] not in introduced) ==
            "ac113907c28a628cc6d8d702add474eddbb0567c07e9e4fa3d3788316f769797",
            "AV306 must retain all1114 original file pins and exactly39 qualified additions unchanged.")
    require(manifest["features"]["originalSelectedClipInputs"] == 306 and
            manifest["features"]["originalCompiledPlaylistPrograms"] == 1205 and
            manifest["entrypoints"]["courseCamera"] == "camera/complete-av306-course.json" and
            manifest["entrypoints"]["originalAudio"] == "audio/complete-av306-manifest.json",
            "The actual306/course/qualified team-audio capability must be explicit, not relabeled303 metadata.")


def require_two_station_records_manifest(manifest, raw):
    require(hashlib.sha256(raw).hexdigest() == TWO_STATION_RECORDS_MANIFEST_SHA256 and
            manifest.get("schema") == AV306_SCHEMA and manifest.get("profile") == TWO_STATION_RECORDS_PROFILE and
            manifest.get("featureContract") == TWO_STATION_RECORDS_FEATURE and
            manifest.get("factory") == TWO_STATION_RECORDS_FACTORY and
            manifest.get("ancestorManifestSha256") == AV306_MANIFEST_SHA256,
            "Named TwoStation requires its exact distinct profile/factory and unchanged1153 ancestor.")
    pins = manifest["files"]
    inherited = [pin for pin in pins if pin["path"] != TWO_STATION_RECORDS_SETTINGS]
    inventory = "".join(f'{pin["path"]}\t{pin["bytes"]}\t{pin["sha256"]}\n'
                        for pin in sorted(inherited, key=lambda pin: pin["path"]))
    require(len(pins) == 1154 and sum(pin["bytes"] for pin in pins) == 300324845 and
            len(inherited) == 1153 and sum(pin["bytes"] for pin in inherited) == 300324289 and
            hashlib.sha256(inventory.encode()).hexdigest() ==
            "c8fe584f8aec0c82eb34b80dd64bc494df4e5238848dfc747f6de8e1be5eb2e0" and
            [pin for pin in pins if pin["path"] == TWO_STATION_RECORDS_SETTINGS] ==
            [dict(path=TWO_STATION_RECORDS_SETTINGS, bytes=556, sha256=TWO_STATION_RECORDS_SETTINGS_SHA256)] and
            manifest["entrypoints"]["settings"] == TWO_STATION_RECORDS_SETTINGS,
            "Named TwoStation must retain all1153 old inputs and activate only its exact new two-station settings.")
    require(manifest["features"]["originalSelectedClipInputs"] == 306 and
            manifest["features"]["originalCompiledPlaylistPrograms"] == 1205 and
            manifest["features"]["originalStationLayout"] == 2 and
            manifest["features"]["sourceTwoStationRecordWorkflow"] == "source-two-station-record-workflow/v1" and
            manifest["entrypoints"]["courseCamera"] == "camera/complete-av306-course.json" and
            manifest["entrypoints"]["originalAudio"] == "audio/complete-av306-manifest.json",
            "Named TwoStation must declare its actual two-station record workflow and authenticated shared AV assets.")


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def source_inventory(root):
    sources = set()
    for group in ("Contracts", "Gameplay", "AssetImport", "Storage"):
        sources.update((root / "Assets/Scripts" / group).glob("*.cs"))
    props = ET.parse(HERE / "ProductionSources.props").getroot()
    for item in props.iter("Compile"):
        name = item.attrib["Include"].replace("$(BlitzSourceRoot)", "")
        if "*" not in name:
            sources.add(root / name)
    records = [{"path": str(path.relative_to(root)), "sha256": sha(path), "origin": "production"}
               for path in sorted(sources)]
    for path in sorted(list((HERE / "Runtime").glob("*.cs")) + list((HERE / "wwwroot").glob("*")) +
                       list(HERE.glob("*.props")) + list(HERE.glob("*.csproj")) + list(HERE.glob("Browser*.cs"))):
        records.append({"path": str(path.relative_to(PROJECT)), "sha256": sha(path), "origin": "preview"})
    return records


def verify_private_inputs(data, manifest):
    if manifest.get("profile") == TWO_STATION_RECORDS_PROFILE:
        require_two_station_records_manifest(manifest, (data / "manifest.json").read_bytes())
    if manifest.get("profile") == AV306_PROFILE:
        require_av306_manifest(manifest, (data / "manifest.json").read_bytes())
    if manifest.get("profile") == AUDIBLES_PROFILE:
        require_audibles_manifest(manifest, (data / "manifest.json").read_bytes())
    if manifest.get("profile") in (COMPLETE_PROFILE, SOURCE_BANKS_PROFILE, RECORDS_PROFILE, FINAL_PROFILE, PREMATCH_PROFILE, SELECTED_WORLD_PROFILE):
        require_complete_manifest(manifest, (data / "manifest.json").read_bytes())
    expected = {"manifest.json"}
    aliases = {"manifest.json"}
    for entry in manifest["files"]:
        relative = portable(entry["path"])
        require(relative.lower() not in aliases, "Duplicate/aliased private input: " + relative)
        aliases.add(relative.lower())
        expected.add(relative)
        raw = read(data, relative, entry["sha256"])
        require(len(raw) == entry["bytes"], "Explicit private input byte count changed: " + relative)
    actual = set()
    for path in data.rglob("*"):
        require(not path.is_symlink(), "Symlinked private input: " + path.relative_to(data).as_posix())
        if path.is_file():
            actual.add(path.relative_to(data).as_posix())
    require(actual == expected, "Private input inventory differs; unexpected=" +
            repr(sorted(actual - expected)) + ", missing=" + repr(sorted(expected - actual)))
    if manifest["profile"] in (PROFILE, AUDIBLES_PROFILE):
        require(len(manifest["files"]) == 563, "Pre-snap switch requires the exact563-file successor.")


def stage_private_inputs(data, manifest, output):
    destination = output / "data"
    require(output.resolve() == output and not destination.is_symlink(),
            "Refusing symlinked private publish output.")
    require(not data.resolve().is_relative_to(destination) and
            not destination.is_relative_to(data.resolve()),
            "Private publish output cannot overlap its authenticated input root.")
    if destination.exists():
        require(destination.is_dir(), "Private publish data output must be a directory.")
        shutil.rmtree(destination)
    for relative in ["manifest.json"] + [entry["path"] for entry in manifest["files"]]:
        target = destination / portable(relative)
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(data / relative, target)
        require(sha(target) == sha(data / relative),
                "Private source input changed while being copied: " + relative)


def aot_evidence(output):
    intermediate = HERE / "obj/SourcePlayer.Browser/Release/net10.0/browser-wasm/wasm/for-publish"
    driver = intermediate / "driver-gen.c"
    require(driver.is_file(), "AOT module registration source is missing; a Release label is not AOT evidence.")
    registration = driver.read_text()
    objects = []
    for assembly in ("SourcePlayer.Core", "SourcePlayer.Browser"):
        symbol = "mono_aot_module_" + assembly.replace(".", "_") + "_info"
        require(symbol in registration, "Application AOT module was not registered: " + assembly)
        candidates = [p for p in intermediate.rglob("*.o")
                      if p.name in (assembly + ".o", assembly + ".dll.o")]
        require(len(candidates) == 1 and candidates[0].stat().st_size > 8,
                "Exactly one actual compiled application AOT object is required: " + assembly)
        path = candidates[0]
        require(path.read_bytes()[:4] in (b"\0asm", b"BC\xc0\xde"),
                "Application AOT output is not a WebAssembly object or LLVM bitcode: " + assembly)
        objects.append(dict(assembly=assembly, bytes=path.stat().st_size, sha256=sha(path), symbol=symbol))
    executable = os.environ.get("SOURCE_PLAYER_DOTNET") or shutil.which("dotnet")
    require(executable is not None, "The actual SDK executable is required for AOT evidence.")
    prebuilt = Path(executable).resolve().parent / (
        "packs/Microsoft.NETCore.App.Runtime.Mono.browser-wasm/10.0.0/"
        "runtimes/browser-wasm/native/dotnet.native.wasm")
    native = output / "_framework/dotnet.native.wasm"
    require(prebuilt.is_file() and native.is_file() and sha(native) != sha(prebuilt),
            "AOT must emit a new linked native runtime, not relabel the SDK-prebuilt interpreter.")
    boot = (output / "_framework/dotnet.boot.js").read_text()
    config = json.JSONDecoder().raw_decode(boot.split("/*json-start*/", 1)[1])[0]
    runtime = json.loads((output / "SourcePlayer.Browser.runtimeconfig.json").read_bytes())
    require(runtime["runtimeOptions"]["configProperties"].get(
        "System.Text.Json.JsonSerializer.IsReflectionEnabledByDefault") is True,
        "The explicit AOT profile must preserve the application's reflection-based JSON contract.")
    return dict(schema="mono-wasm-aot-rooted-execution/v1", mode="aot", runAOTCompilation=True,
                wasmBuildNative=True, publishTrimmed=True, trimmingExplicitlyAuthorized=True,
                rootedApplicationAssemblies=["SourcePlayer.Core", "SourcePlayer.Browser"],
                jsonReflectionEnabled=True,
                sourceStepCostDiagnostics="bounded-readonly/v1",
                debugLevel=config["debugLevel"], applicationObjects=objects,
                moduleRegistrationSha256=sha(driver), nativeRuntimeSha256=sha(native),
                prebuiltRuntimeSha256=sha(prebuilt))


def reuse_aot_bundle(source, expected_sha, output, sources):
    source = source.resolve()
    raw = (source / "build-info.json").read_bytes()
    require(expected_sha is not None and hashlib.sha256(raw).hexdigest() == expected_sha,
            "Reusing AOT requires the exact explicitly pinned prior build receipt.")
    previous = json.loads(raw)
    require(previous["execution"]["mode"] == "aot" and previous["execution"]["runAOTCompilation"] is True and
            previous["execution"]["jsonReflectionEnabled"] is True and
            previous["profile"] == SELECTED_WORLD_PROFILE,
            "Only the actual rooted AOT selected-world ancestor can supply compiled modules.")
    old = {entry["path"]: entry["sha256"] for entry in previous["sources"]}
    new = {entry["path"]: entry["sha256"] for entry in sources}
    require(set(old) == set(new), "The exact compiled source inventory must be unchanged for JS-only AOT reuse.")
    changed = {name for name in old if old[name] != new[name]}
    allowed = {"tools/source-player/wwwroot/main.js", "tools/source-player/wwwroot/input.js"}
    require(changed and changed.issubset(allowed),
            "JS-only caller-profile reuse cannot change C#, projects, roots, renderer, or other compiled inputs.")
    for entry in previous["assemblies"]:
        require(sha(source / portable(entry["path"])) == entry["sha256"],
                "The actual ancestor Webcil module changed: " + entry["path"])
    require(sha(source / "_framework/dotnet.native.wasm") == previous["execution"]["nativeRuntimeSha256"],
            "The actual linked ancestor native runtime changed.")
    require(not output.exists() and not output.is_relative_to(source) and not source.is_relative_to(output),
            "Choose a create-new disjoint generated output for the explicit JS-only AOT profile.")
    shutil.copytree(source, output, ignore=lambda directory, names:
                    ["data"] if Path(directory) == source and "data" in names else [])
    for name in changed:
        shutil.copyfile(PROJECT / name, output / Path(name).name)
    return dict(schema="unchanged-compiled-aot-js-only-profile/v1", buildInfoSha256=expected_sha,
                compiledSourceRootDigest=previous["sourceRootDigest"], changedJavaScript=sorted(changed),
                nativeRuntimeSha256=previous["execution"]["nativeRuntimeSha256"],
                applicationObjects=previous["execution"]["applicationObjects"],
                assemblies=previous["assemblies"])


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--data", type=Path, required=True, help="Explicit pinned private data output from prepare.py.")
    parser.add_argument("--profile", choices=(OLD_PROFILE, PROFILE, AUDIBLES_PROFILE, COMPLETE_PROFILE, SOURCE_BANKS_PROFILE, RECORDS_PROFILE, FINAL_PROFILE, PREMATCH_PROFILE, SELECTED_WORLD_PROFILE, AV306_PROFILE, TWO_STATION_RECORDS_PROFILE), default=PROFILE,
                        help="Keep the pre-snap-switch default; default audibles require their explicit new stable ID.")
    parser.add_argument("--source-root", type=Path, default=PROJECT,
                        help="Immutable accepted production source root (BlitzSourceRoot override).")
    parser.add_argument("--source-handoff", type=Path,
                        help="Required frozen Core handoff when using an external production source root.")
    parser.add_argument("--source-handoff-sha256")
    parser.add_argument("--no-restore", action="store_true",
                        help="Use already restored SDK assets without contacting package feeds; input and source guards still run.")
    parser.add_argument("--aot", action="store_true",
                        help="Explicit rooted Mono-WASM AOT v1: toolchain-required trimming with full app roots/JSON reflection; default remains unchanged.")
    parser.add_argument("--caller-drain", action="store_true",
                        help="Explicit bounded MessageChannel zero-elapsed caller; requires AOT/exact primary1114 or distinct AV306.")
    parser.add_argument("--reuse-aot-bundle", type=Path,
                        help="Explicit verified prior AOT AppBundle for JS-only caller-profile composition; never changes compiled C#.")
    parser.add_argument("--reuse-aot-build-sha256")
    args = parser.parse_args(argv)
    if args.caller_drain and (not args.aot or args.profile not in
                             (SELECTED_WORLD_PROFILE, AV306_PROFILE, TWO_STATION_RECORDS_PROFILE)):
        parser.error("The explicit caller-drain profile requires --aot and its explicit selected-world or AV306 data profile.")
    if (args.reuse_aot_bundle is not None or args.reuse_aot_build_sha256 is not None) and not args.caller_drain:
        parser.error("JS-only AOT reuse is restricted to the explicit caller-drain profile.")
    if args.profile in (AV306_PROFILE, TWO_STATION_RECORDS_PROFILE) and (
            args.reuse_aot_bundle is not None or args.reuse_aot_build_sha256 is not None):
        parser.error("AV306 requires freshly compiled application modules; cross-generation AOT reuse is forbidden.")
    data = args.data.resolve()
    if not data.is_dir() or not (data / "manifest.json").is_file():
        parser.error("A real complete private data bundle is required; no assets are fetched by build.")
    manifest = json.loads((data / "manifest.json").read_bytes())
    if manifest.get("schema") != (AV306_SCHEMA if args.profile in (AV306_PROFILE, TWO_STATION_RECORDS_PROFILE) else
                                SELECTED_WORLD_SCHEMA if args.profile == SELECTED_WORLD_PROFILE else "blitz-portable-source-player/v1"):
        parser.error("A complete prepared source-player manifest is required.")
    if manifest.get("profile") != args.profile:
        parser.error("Prepared manifest.profile differs from --profile; missing new data never falls back to historical M1.")
    try:
        verify_private_inputs(data, manifest)
    except (OSError, ValueError, KeyError, TypeError) as error:
        parser.error(str(error))
    selected_caller = None
    if args.caller_drain:
        try:
            selected_caller = caller_drain_profile(args.profile, sha(data / "manifest.json"))
        except ValueError as error:
            parser.error(str(error))
    root = args.source_root.resolve()
    if "enum SourceGameplayCompatibility" not in (root / "Assets/Scripts/Contracts/SourceMatchContracts.cs").read_text():
        parser.error("The accepted portable production Core API is required. Integrate that dependency or supply --source-root pointing to its immutable accepted snapshot; strict-source fallback is forbidden.")
    before = source_inventory(root)
    handoff_hash = None
    if root != PROJECT:
        if args.source_handoff is None or args.source_handoff_sha256 is None:
            parser.error("An external immutable source root requires its explicit --source-handoff and --source-handoff-sha256.")
        handoff_hash = sha(args.source_handoff)
        if handoff_hash != args.source_handoff_sha256:
            parser.error("The accepted frozen source handoff SHA256 changed.")
        frozen = json.loads(args.source_handoff.read_bytes())
        if frozen.get("schema") != "source-portable-m1-handoff/v1" or frozen.get("code_writes_frozen") is not True:
            parser.error("External Core source is not an explicitly frozen portable handoff.")
        production = [record for record in before if record["origin"] == "production"]
        if any(frozen["current_all113_source_sha256"].get(record["path"]) != record["sha256"] for record in production):
            parser.error("The actual compiled production closure differs from the accepted frozen Core source hashes.")
    command = ["bash", str(HERE / "dotnet.sh"), "publish", str(HERE / "SourcePlayer.Browser.csproj"),
               "-c", "Release", "--nologo", "-v", "minimal", "-p:BlitzSourceRoot=" + str(root) + "/"]
    if args.no_restore:
        command.append("--no-restore")
    if args.aot:
        command.extend(["-p:RunAOTCompilation=true", "-p:WasmBuildNative=true", "-p:PublishTrimmed=true",
                        "-p:JsonSerializerIsReflectionEnabledByDefault=true"])
    output = HERE / "bin/SourcePlayer.Browser/Release/net10.0/browser-wasm/AppBundle"
    reused = None
    if args.reuse_aot_bundle is not None:
        reused = reuse_aot_bundle(args.reuse_aot_bundle, args.reuse_aot_build_sha256, output, before)
    else:
        subprocess.run(command, cwd=PROJECT, check=True)
    after = source_inventory(root)
    if before != after:
        parser.error("Production or preview sources changed during publish; no build receipt was emitted.")
    stage_private_inputs(data, manifest, output)
    shutil.copyfile(HERE / "wwwroot/verified-assets.js", output / "verified-assets.js")
    framework = output / "_framework"
    assemblies = [p for p in framework.iterdir() if p.name.startswith("SourcePlayer.") and p.suffix == ".wasm"]
    if len(assemblies) != 2 or not any(p.name.startswith("SourcePlayer.Core") for p in assemblies):
        parser.error("Actual C# Core and Browser Webcil assemblies were not emitted.")
    sdk = subprocess.check_output(["bash", str(HERE / "dotnet.sh"), "--version"], cwd=PROJECT, text=True).strip()
    receipt = {
        "schema": "blitz-source-player-build/v1",
        "runtimeFlavor": "portable C# browser preview (not a Unity player build)",
        "sdkVersion": sdk,
        "mainAssembly": "SourcePlayer.Browser",
        "sourceRootDigest": hashlib.sha256(json.dumps(before, sort_keys=True).encode()).hexdigest(),
        "sources": before,
        "dataManifestSha256": sha(data / "manifest.json"),
        "profile": args.profile,
        "factory": TWO_STATION_RECORDS_FACTORY if args.profile == TWO_STATION_RECORDS_PROFILE else
                   AV306_FACTORY if args.profile == AV306_PROFILE else
                   SELECTED_WORLD_FACTORY if args.profile == SELECTED_WORLD_PROFILE else
                   PREMATCH_FACTORY if args.profile == PREMATCH_PROFILE else
                   FINAL_FACTORY if args.profile == FINAL_PROFILE else
                   RECORDS_FACTORY if args.profile == RECORDS_PROFILE else
                   SOURCE_BANKS_FACTORY if args.profile == SOURCE_BANKS_PROFILE else
                   COMPLETE_FACTORY if args.profile == COMPLETE_PROFILE else
                   AUDIBLES_FACTORY if args.profile == AUDIBLES_PROFILE else
                   "SourceC2Composition.CreatePreSnapSwitchSimulation" if args.profile == PROFILE else
                   "SourceC2Composition.CreateM1Simulation",
        "acceptedSourceHandoffSha256": handoff_hash,
        "verifiedRuntimeResources": {
            "schema": "source-verified-runtime-resources/v1",
            "files": [{"path": "_framework/" + path.name, "bytes": path.stat().st_size, "sha256": sha(path)}
                      for path in sorted(framework.iterdir()) if path.suffix in (".wasm", ".dat")],
        },
        "assemblies": [{"path": "_framework/" + p.name, "bytes": p.stat().st_size, "sha256": sha(p)}
                       for p in sorted(assemblies)],
        "runtimeIdentifier": "browser-wasm",
        "unsafePermission": "JSImport/JSExport generated marshalling only; no original C or unsafe memory execution",
        "nativeUnityBuild": False,
    }
    if args.profile == AUDIBLES_PROFILE:
        receipt["featureContract"] = AUDIBLES_FEATURE
    elif args.profile in (COMPLETE_PROFILE, SOURCE_BANKS_PROFILE):
        receipt["featureContract"] = COMPLETE_FEATURE
    elif args.profile in (RECORDS_PROFILE, FINAL_PROFILE):
        receipt["featureContract"] = RECORDS_FEATURE
    elif args.profile == PREMATCH_PROFILE:
        receipt["featureContract"] = PREMATCH_FEATURE
    elif args.profile == SELECTED_WORLD_PROFILE:
        receipt["featureContract"] = SELECTED_WORLD_FEATURE
    elif args.profile == AV306_PROFILE:
        receipt["featureContract"] = AV306_FEATURE
    elif args.profile == TWO_STATION_RECORDS_PROFILE:
        receipt["featureContract"] = TWO_STATION_RECORDS_FEATURE
    if args.aot:
        receipt["execution"] = aot_evidence(output)
    if args.caller_drain:
        receipt["execution"]["callerProfile"] = selected_caller
        receipt["execution"]["callerDrain"] = dict(schema="bounded-source-caller-drain/v1",
            scheduling="MessageChannel", elapsedSecondsPerDrain=0, maximumHostStepsPerCallback=6,
            maximumBridgeIntervalSeconds=.1, freshInputPollingPerDrain=0,
            audioLeadChanged=False, sourceClockRngOrEpochChanged=False)
    if reused is not None:
        require(receipt["execution"]["nativeRuntimeSha256"] == reused["nativeRuntimeSha256"] and
                receipt["execution"]["applicationObjects"] == reused["applicationObjects"] and
                receipt["assemblies"] == reused["assemblies"],
                "JS-only profile composition must preserve the exact real native/application AOT modules.")
        receipt["execution"]["compiledAotReuse"] = reused
    (output / "build-info.json").write_text(json.dumps(receipt, indent=2, sort_keys=True) + "\n")
    print(json.dumps({"output": str(output), "receiptSha256": sha(output / "build-info.json"),
                      "dataManifestSha256": receipt["dataManifestSha256"], "sourceFiles": len(before)}, indent=2))


if __name__ == "__main__":
    main()
