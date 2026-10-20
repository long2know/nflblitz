#!/usr/bin/env python3
"""Required historical-positive/current-stale checks, never current full M1 qualification."""
import argparse
from concurrent.futures import ThreadPoolExecutor
import hashlib
import io
import json
import os
from pathlib import Path, PurePosixPath
import shutil
import subprocess
import sys
import tarfile

import canonical
import prepare_public_replay as replay
from contact_inputs import fixture_profile
from kernel_sources import C2_ANIMATION_SHA, C2_M1_DATA_SHA, canonical_hashes, production_paths
from prepare_inputs import fixture_hashes

ROOT = replay.ROOT
BASELINE_REF = "cf3f2b5db3cc94ac3f51e01dae94dd74d90ebc02"
BASELINE_TREE = "a1c2e38a83e34933b0da4b0cd002ae14255d9a3d"
BASELINE_SOURCE_SHA256 = "4b09019b9cbd9e79dc8d1c25b2018b0ab141c3beaa693cd54559f01324cba4a6"
CURRENT_SOURCE_SHA256 = replay.SOURCE_CURRENT_EXPECTED_RECIPE["source_manifest_sha256"]
HARNESS_SHA256 = "ea910e3039fc8f39b4958c83a535eb5e81c2f3ec29dbfbf15757ece675206b91"
OLD_PUBLIC_SHA256 = replay.RECORDINGS[replay.POSITIVE_CASE]["public_sha256"]
FIXTURE_SHA256 = "bc4e0fefc0f86ced01c733cf208abb8471b77ad35815c4d31c685e0de1538d75"
SIDE_ERROR = "System.ArgumentException: The selected original playbook does not belong to the submitting source side."
SOURCE_EXTRAS = (
    "Assets/Scripts/Presentation/SourceCinematicModel.cs",
    "Assets/Scripts/Presentation/SourceAudioFunctions.cs",
    "Assets/Scripts/Presentation/SourceC2SoundFunctions.cs",
    "Assets/Scripts/Runtime/SourceContactComposition.cs",
    "Assets/Scripts/Runtime/SourceC2Composition.cs",
)
require = replay.require


def hashes(files):
    return {name: replay.sha(data) for name, data in files.items()}


def manifest(files):
    return replay.sha(json.dumps(hashes(files), sort_keys=True, separators=(",", ":")).encode())


def validate_ref(ref, tree):
    require(ref == BASELINE_REF and tree == BASELINE_TREE, "exact historical commit/tree required")


def git(*args):
    return subprocess.run(["git", *args], cwd=ROOT, check=True, capture_output=True).stdout


def validate_snapshot(files, count, digest):
    harness = {name: files[name] for name in canonical.HARNESS if name in files}
    sources = {name: data for name, data in files.items() if name not in canonical.HARNESS}
    require(len(harness) == 12 and manifest(harness) == HARNESS_SHA256,
            "exact unchanged twelve-file harness/project payload required")
    require(len(sources) == count and manifest(sources) == digest,
            "exact pinned pure source payload/hash required")
    require(replay.sha(harness["tests/movement-callers/PortablePublicPolicy.cs"]) ==
            replay.SOURCE_CURRENT_EXPECTED_RECIPE["controller_sha256"], "unchanged controller required")
    return {"source_count": len(sources), "source_manifest_sha256": manifest(sources),
            "harness_count": len(harness), "harness_manifest_sha256": manifest(harness),
            "files_sha256": hashes(files)}


def archive_payload(raw, blobs):
    files = {}
    directories = {parent.as_posix() for name in blobs for parent in PurePosixPath(name).parents
                   if parent != PurePosixPath(".")}
    with tarfile.open(fileobj=io.BytesIO(raw), mode="r:") as archive:
        for member in archive:
            path = PurePosixPath(member.name)
            require(not path.is_absolute() and ".." not in path.parts, "unsafe historical archive path")
            if member.isdir():
                require(member.name.rstrip("/") in directories, "unexpected historical archive directory")
                continue
            require(member.isfile() and member.name in blobs and member.name not in files,
                    "historical archive requires unique selected regular files")
            data = archive.extractfile(member).read()
            blob = hashlib.sha1(b"blob " + str(len(data)).encode() + b"\0" + data).hexdigest()
            require(blob == blobs[member.name], "historical archive Git blob payload/hash differs")
            files[member.name] = data
    require(set(files) == set(blobs), "historical archive selected payload inventory differs")
    return files


def historical_snapshot(ref):
    require(ref == BASELINE_REF, "exact historical commit/tree required")
    try:
        git("cat-file", "-e", ref + "^{commit}")
    except subprocess.CalledProcessError:
        git("fetch", "--no-tags", "--depth=1", "origin", BASELINE_REF)
    validate_ref(ref, git("rev-parse", ref + "^{tree}").decode().strip())
    selected = ("Assets/Scripts/Contracts", "Assets/Scripts/Gameplay", "Assets/Scripts/AssetImport",
                *SOURCE_EXTRAS, *canonical.HARNESS)
    blobs = {}
    for row in git("ls-tree", "-r", "-z", ref, "--", *selected).split(b"\0"):
        if not row:
            continue
        meta, name = row.decode().split("\t")
        mode, kind, blob = meta.split()
        source = name.endswith(".cs") and any(name.startswith("Assets/Scripts/" + area + "/")
                                              for area in ("Contracts", "Gameplay", "AssetImport"))
        if source or name in SOURCE_EXTRAS or name in canonical.HARNESS:
            require(kind == "blob" and mode in ("100644", "100755"), "historical source must be regular Git blobs")
            blobs[name] = blob
    require(len(blobs) == 88 and set(canonical.HARNESS).issubset(blobs),
            "exact historical source/harness archive selection required")
    raw = git("archive", "--format=tar", ref, "--", *sorted(blobs))
    files = archive_payload(raw, blobs)
    identity = validate_snapshot(files, 76, BASELINE_SOURCE_SHA256)
    identity.update(commit=ref, tree=BASELINE_TREE, selected_git_archive_sha256=replay.sha(raw))
    return files, identity


def current_snapshot():
    files = {p.relative_to(ROOT).as_posix(): p.read_bytes() for p in production_paths()}
    files.update({name: (ROOT / name).read_bytes() for name in canonical.HARNESS})
    return files, validate_snapshot(files, 77, CURRENT_SOURCE_SHA256)


def old_commands(path):
    path = replay.output_path(path)
    require(path.suffix == ".jsonl" and not path.is_symlink(), "explicit old public JSONL required")
    require(path.stat().st_size == replay.RECORDINGS[replay.POSITIVE_CASE]["public_bytes"],
            "exact old public input byte count required")
    return replay.audit_public(path.read_bytes(), replay.POSITIVE_CASE)


def common_result(summary, receipt):
    replay.exact_metadata({key: summary.get(key) for key in (
        "schema", "gameplay_compatibility", "real_input_fixture_sha256",
        "actual_animation_library_manifest", "whole_M1_or_full_gameplay_qualified", "native_or_browser_qualified")}, {
            "schema": "source-movement-caller-checks/v1",
            "gameplay_compatibility": "Portable", "real_input_fixture_sha256": FIXTURE_SHA256,
            "actual_animation_library_manifest": C2_ANIMATION_SHA,
            "whole_M1_or_full_gameplay_qualified": False, "native_or_browser_qualified": False,
        }, "summary")
    require(type(summary.get("assertions")) is int and summary["assertions"] > 0, "nonzero actual assertion count required")
    replay.exact_metadata({key: receipt.get(key) for key in (
        "schema", "observed", "historical", "gameplayCompatibility", "profile", "sourceData",
        "factory", "replayFileSha256", "originalPublicVectorRetainedWithoutStateConditionedRegeneration",
        "sourceAudioDSPOrTargetRngOrFullGameAccepted")}, {
            "schema": "source-C2-M1-real-factory-public-replay/v1", "observed": False, "historical": False,
            "gameplayCompatibility": "Portable", "profile": "original-teams-contact281-c2-m1",
            "sourceData": C2_M1_DATA_SHA, "factory": "REAL SourceC2Composition.CreateM1Simulation",
            "replayFileSha256": OLD_PUBLIC_SHA256,
            "originalPublicVectorRetainedWithoutStateConditionedRegeneration": True,
            "sourceAudioDSPOrTargetRngOrFullGameAccepted": False,
        }, "receipt")


def historical_positive(code, summary, receipt):
    common_result(summary, receipt)
    require(summary.get("mode") == "m1-complete", "historical positive must execute m1-complete")
    require(canonical.require_m1_replay(code, summary, receipt, "portable") == "positive",
            "historical full public positive required")
    replay.exact_metadata({key: receipt.get(key) for key in (
        "completedHosts", "SourceTick", "rawCursor", "rSkipFrames", "positive", "failure")}, {
            "completedHosts": 12000, "SourceTick": 11400, "rawCursor": 23771, "rSkipFrames": 26,
            "positive": {"actor": 12, "InstanceId": 208, "firstTick": 5674, "secondTick": 5699,
                         "firstOrdinal": 0, "secondOrdinal": 25}, "failure": None,
        }, "historical")
    return "positive-historical-main38-only"


def current_stale(code, summary, receipt, fault, row):
    common_result(summary, receipt)
    require(code == 2 and summary.get("status") == "error" and
            summary.get("public_R_SKIP_gate_qualified") is not True and
            summary.get("complete_publication_qualification") is not True,
            "current stale recording must fail, never success-shaped acceptance")
    require(summary.get("error", "").splitlines()[0:1] == [SIDE_ERROR],
            "current stale recording must reject specifically playbook-side validation")
    require("mode" not in summary and "Program.M1CompleteReplay(" in summary["error"],
            "actual replay error summary required, not a successful-mode or generic catch")
    replay.exact_metadata({key: receipt.get(key) for key in (
        "completedHosts", "SourceTick", "rawCursor", "rSkipFrames", "positive")}, {
            "completedHosts": 5846, "SourceTick": 5553, "rawCursor": 10142, "rSkipFrames": 0, "positive": None,
        }, "current-stale")
    failure = receipt.get("failure")
    require(type(failure) is dict and fault == failure, "actual first kernel fault must match replay failure")
    replay.exact_metadata({key: failure.get(key) for key in ("host", "before", "SourceTick")},
                          {"host": 5845, "before": 5552, "SourceTick": 5553}, "stale-fault")
    error = failure.get("error", "")
    require(error.splitlines()[0:1] == [SIDE_ERROR] and
            "GameSimulation.SelectSourcePlay(" in error and "GameSimulation.ApplySourceStationMenuCommands(" in error,
            "actual first fault must be the source-side validator, not a generic exception")
    command = json.loads(json.dumps(failure["command"]))
    for station in command["StationInputs"]["Stations"]:
        require(station.pop("IsSpecified", None) is True, "actual station constructor must be specified")
    replay.public_row({"host": 5845, "command": command}, 5845)
    require(row["host"] == 5845 and command == row["command"], "same immutable old command required at stale boundary")
    stations = command["StationInputs"]["Stations"]
    require(len(stations) == 1 and stations[0]["StationId"] == 0 and
            stations[0]["Command"]["Action"] == 1 and
            stations[0]["Command"]["Play"]["PlaySymbol"] == "play_suicide_blitz" and
            stations[0]["Command"]["Play"]["Playbook"] == 1 and
            failure["actual"]["State"]["Possession"] == 0,
            "stale DEFENSE command must belong to station0 while Home is offense")
    return "rejected-current-stale-playbook-side"


def summary_line(stdout):
    rows = [replay.decode(line) for line in stdout.splitlines() if line.startswith('{"schema":')]
    require(len(rows) == 1 and type(rows[0]) is dict, "one actual managed summary required")
    return rows[0]


def execute(label, files, args, stale_row):
    root = args.output / label
    root.mkdir()
    source, build, work = root / "source", root / "build", root / "work"
    for name, data in files.items():
        replay.write_new_or_verify(source / name, data)
    (work / "empty-feed").mkdir(parents=True)
    env = dict(os.environ, TMPDIR=str(work), TMP=str(work), TEMP=str(work),
               DOTNET_CLI_HOME=str(work / "dotnet-home"), NUGET_PACKAGES=str(work / "nuget"),
               DOTNET_CLI_TELEMETRY_OPTOUT="1", DOTNET_NOLOGO="1", MSBUILDDISABLENODEREUSE="1")
    commands = []

    def run(command, log, timeout):
        commands.append(command)
        process = subprocess.run(command, cwd=ROOT, env=env, capture_output=True, text=True, timeout=timeout)
        (root / log).write_text(process.stdout + process.stderr)
        return process

    project = source / "tests/movement-callers/MovementCallerChecks.csproj"
    props = ["-p:BlitzSourceRoot=" + str(source) + "/",
             "-p:BaseIntermediateOutputPath=" + str(build / "obj") + "/",
             "-p:BaseOutputPath=" + str(build / "bin") + "/"]
    command = ["dotnet", "build", str(project), "--no-restore", "-c", "Release", "-v", "quiet", *props]
    compiled = run(command, "build-first.log", 240)
    if compiled.returncode and "NETSDK1004" in compiled.stdout + compiled.stderr:
        restored = run(["dotnet", "restore", str(project), "--source", str(work / "empty-feed"),
                        "-p:NuGetAudit=false", "-v", "quiet", *props], "restore-missing-assets.log", 240)
        require(restored.returncode == 0, "offline restore after actual missing SDK assets failed")
        compiled = run(command, "build-after-restore.log", 240)
    text = compiled.stdout + compiled.stderr
    require(compiled.returncode == 0 and "0 Warning(s)" in text and "0 Error(s)" in text,
            "generation probe requires zero-diagnostic compilation: " + label)
    destination = root / "replay"
    process = run(["dotnet", str(build / "bin/Release/net10.0/MovementCallerChecks.dll"),
                   str(args.fixture_manifest), str(destination), "m1-complete", str(args.sound_functions),
                   str(args.c2_m1_inputs), str(args.replay_commands), "--source-compatibility", "portable"],
                  "replay.log", 900)
    summary = summary_line(process.stdout)
    receipt = replay.decode((destination / "m1-replay-result.json").read_text())
    if label == "historical":
        classification = historical_positive(process.returncode, summary, receipt)
    else:
        fault = replay.decode((destination / "first-m1-kernel-fault.json").read_text())
        classification = current_stale(process.returncode, summary, receipt, fault, stale_row)
    require({name: (source / name).read_bytes() for name in files} == files, "isolated source/harness changed during probe")
    shutil.rmtree(source)
    return {"classification": classification, "exit_code": process.returncode, "commands": commands,
            "compile_zero_diagnostics": True, "receipt_sha256": replay.sha((destination / "m1-replay-result.json").read_bytes()),
            "summary": {key: value for key, value in summary.items() if key != "actual_factory_replay"},
            "receipt_metadata": {key: receipt[key] for key in (
                "completedHosts", "SourceTick", "rawCursor", "rSkipFrames", "positive", "replayFileSha256")}}


def argument_parser():
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ("fixture-manifest", "sound-functions", "c2-m1-inputs", "replay-commands", "output"):
        parser.add_argument("--" + name, type=Path, required=True, action="append")
    parser.add_argument("--baseline-ref", required=True, action="append", choices=(BASELINE_REF,))
    return parser


def main(argv=None):
    parser = argument_parser()
    args = parser.parse_args(argv)
    for name, values in vars(args).items():
        if len(values) != 1:
            parser.error("duplicate --" + name.replace("_", "-") + " argument")
        setattr(args, name, values[0].absolute() if isinstance(values[0], Path) else values[0])
    result = {"schema": "source-public-replay-generations/v1", "status": "failed",
              "current_full_M1_qualification": False, "native_or_whole_game_qualified": False}
    try:
        args.output = replay.output_path(args.output)
        require(not args.output.exists(), "use a fresh ignored generation-check output; never overwrite")
        rows = old_commands(args.replay_commands)
        fixture_profile(args.fixture_manifest, "contact272")
        inputs = fixture_hashes(args.fixture_manifest, args.sound_functions)
        inputs.update(canonical_hashes(args.c2_m1_inputs, "contact281-c2-m1"))
        require(len(inputs) == 1122, "exact834 plus288 original input closure required")
        current, current_identity = current_snapshot()
        historical, historical_identity = historical_snapshot(args.baseline_ref)
        args.output.mkdir(parents=True)
        result.update(sources={"historical": historical_identity, "current": current_identity},
                      inputs_sha256=inputs, old_public_sha256=OLD_PUBLIC_SHA256, probes={})
        with ThreadPoolExecutor(max_workers=2) as workers:
            pending = {label: workers.submit(execute, label, files, args, rows[5845])
                       for label, files in (("historical", historical), ("current", current))}
            for label, future in pending.items():
                result["probes"][label] = future.result()
        after = fixture_hashes(args.fixture_manifest, args.sound_functions)
        after.update(canonical_hashes(args.c2_m1_inputs, "contact281-c2-m1"))
        require(inputs == after and current_snapshot()[0] == current and
                old_commands(args.replay_commands) == rows, "source/harness/input generation changed during probes")
        result.update(status="passed-recording-generation-checks", source_harness_inputs_unchanged=True)
    except (ValueError, KeyError, OSError, RuntimeError, subprocess.SubprocessError, tarfile.TarError) as error:
        result["error"] = str(error)
        print("Public recording generation checks failed: " + str(error), file=sys.stderr)
    if args.output.exists() and "sources" in result:
        replay.write_new_or_verify(args.output / "result.json", (json.dumps(result, indent=2) + "\n").encode())
    print(json.dumps({key: result[key] for key in (
        "schema", "status", "current_full_M1_qualification", "native_or_whole_game_qualified")}))
    return 0 if result["status"] == "passed-recording-generation-checks" else 1


if __name__ == "__main__":
    raise SystemExit(main())
