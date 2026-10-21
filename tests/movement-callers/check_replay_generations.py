#!/usr/bin/env python3
"""Immutable historical boundaries plus separately pinned U2, never a substitute for full M1."""
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
import time

import canonical
import prepare_public_replay as replay
from contact_inputs import fixture_profile
from kernel_sources import C2_ANIMATION_SHA, C2_M1_DATA_SHA, canonical_hashes, production_paths
from prepare_inputs import fixture_hashes

ROOT = replay.ROOT
BASELINE_REF = "cf3f2b5db3cc94ac3f51e01dae94dd74d90ebc02"
BASELINE_TREE = "a1c2e38a83e34933b0da4b0cd002ae14255d9a3d"
BASELINE_SOURCE_SHA256 = "4b09019b9cbd9e79dc8d1c25b2018b0ab141c3beaa693cd54559f01324cba4a6"
PREVIOUS_REF = "e539258b6b6160f0dcdc072a05c4887e6946423e"
PREVIOUS_TREE = "12705a5ef6d0b93e96324381a8d1f474c9944d7a"
PREVIOUS_SOURCE_SHA256 = replay.SOURCE_CURRENT_EXPECTED_RECIPE["source_manifest_sha256"]
TIMER_RECORDING_SOURCE_SHA256 = replay.SOURCE_TIMER_EXPECTED_RECIPE["source_manifest_sha256"]
# New production source, same immutable recording. No recording/recipe/controller/seed is regenerated.
CURRENT_SOURCE_SHA256 = "a876fa4b6c286043d9791624904852b6ad7cbfef98f0eaf4d186feb4ac3f20fd"
HARNESS_SHA256 = "ea910e3039fc8f39b4958c83a535eb5e81c2f3ec29dbfbf15757ece675206b91"
CURRENT_REF = "c339efcccc7caf82fb1c3533bf952dc4d3f3600e"
CURRENT_TREE = "c974448da52f2ad7dc74f28e777eee1715ef9dc1"
U2_CURRENT_SOURCE_SHA256 = "7b08ba4d5734d1e49a7403c327b0b6cb373052e94b81beeac9c694595d7a9a4d"
U2_HARNESS_SHA256 = "634c9ca7f514b82ab6789f0fac154380f8aa5f7417a68dcc30bf64a446afc501"
OLD_PUBLIC_SHA256 = replay.RECORDINGS[replay.POSITIVE_CASE]["public_sha256"]
POSSESSION_PUBLIC_SHA256 = replay.RECORDINGS[replay.SOURCE_CURRENT_CASE]["public_sha256"]
FIXTURE_SHA256 = "bc4e0fefc0f86ced01c733cf208abb8471b77ad35815c4d31c685e0de1538d75"
SIDE_ERROR = "System.ArgumentException: The selected original playbook does not belong to the submitting source side."
SOURCE_EXTRAS = (
    "Assets/Scripts/Presentation/SourceCinematicModel.cs",
    "Assets/Scripts/Presentation/SourceAudioFunctions.cs",
    "Assets/Scripts/Presentation/SourceC2SoundFunctions.cs",
    "Assets/Scripts/Runtime/SourceContactComposition.cs",
    "Assets/Scripts/Runtime/SourceC2Composition.cs",
)
PROBE_PARTITIONS = {
    "archived-four": ("main38-old-positive", "main39-old-stale",
                      "main39-possession-positive", "current-possession-stale"),
    "u2-current-fifth": ("u2-current-possession-stale",),
}
ALL_PROBES = PROBE_PARTITIONS["archived-four"] + PROBE_PARTITIONS["u2-current-fifth"]
PROBE_SOURCES = dict(zip(ALL_PROBES, ("historical", "main39", "main39", "current", "u2-current")))
require = replay.require


def probe_inventory(partition="all-five"):
    require(partition in ("all-five", *PROBE_PARTITIONS), "exact closed generation partition required")
    return ALL_PROBES if partition == "all-five" else PROBE_PARTITIONS[partition]


def validate_probe_inventory(partition, probes):
    expected = probe_inventory(partition)
    require(type(probes) is dict and set(probes) == set(expected),
            "every selected exact named source/recording boundary required; no omissions or duplicates")
    for name in expected:
        probe = probes[name]
        positive = name in ("main38-old-positive", "main39-possession-positive")
        classification = ("positive-historical-main38-only" if name == "main38-old-positive" else
                          "positive-pinned-main39-possession-only" if positive else
                          "rejected-pinned-main39-old-stale-playbook-side" if name == "main39-old-stale" else
                          "rejected-current-possession-stale-playbook-side")
        require(type(probe) is dict and probe.get("source_generation") == PROBE_SOURCES[name] and
                probe.get("compile_zero_diagnostics") is True and
                type(probe.get("exit_code")) is int and probe["exit_code"] == (0 if positive else 2) and
                probe.get("classification") == classification,
                "actual compiled/classified generation probe required: " + name)


def hashes(files):
    return {name: replay.sha(data) for name, data in files.items()}


def manifest(files):
    return replay.sha(json.dumps(hashes(files), sort_keys=True, separators=(",", ":")).encode())


def validate_ref(ref, tree, previous=False, current=False):
    require(not (previous and current), "exact historical commit/tree required")
    expected = ((CURRENT_REF, CURRENT_TREE) if current else
                (PREVIOUS_REF, PREVIOUS_TREE) if previous else (BASELINE_REF, BASELINE_TREE))
    require((ref, tree) == expected, "exact historical commit/tree required")


def git(*args):
    return subprocess.run(["git", *args], cwd=ROOT, check=True, capture_output=True).stdout


def validate_snapshot(files, count, digest, harness_digest=HARNESS_SHA256):
    harness = {name: files[name] for name in canonical.HARNESS if name in files}
    sources = {name: data for name, data in files.items() if name not in canonical.HARNESS}
    require(len(harness) == 12 and manifest(harness) == harness_digest,
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


def historical_snapshot(ref, previous=False, current=False):
    expected_ref = CURRENT_REF if current else PREVIOUS_REF if previous else BASELINE_REF
    require(ref == expected_ref, "exact historical commit/tree required")
    try:
        git("cat-file", "-e", ref + "^{commit}")
    except subprocess.CalledProcessError:
        git("fetch", "--no-tags", "--depth=1", "origin", expected_ref)
    tree = git("rev-parse", ref + "^{tree}").decode().strip()
    validate_ref(ref, tree, previous, current)
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
    count, digest = ((78, CURRENT_SOURCE_SHA256) if current else
                     (77, PREVIOUS_SOURCE_SHA256) if previous else (76, BASELINE_SOURCE_SHA256))
    require(len(blobs) == count + 12 and set(canonical.HARNESS).issubset(blobs),
            "exact historical source/harness archive selection required")
    raw = git("archive", "--format=tar", ref, "--", *sorted(blobs))
    files = archive_payload(raw, blobs)
    identity = validate_snapshot(files, count, digest)
    identity.update(commit=ref, tree=tree, selected_git_archive_sha256=replay.sha(raw))
    return files, identity


def current_snapshot():
    """The immutable PR41/v3 generation, not the changed U2 working tree."""
    return historical_snapshot(CURRENT_REF, current=True)


def u2_current_snapshot():
    files = {p.relative_to(ROOT).as_posix(): p.read_bytes() for p in production_paths()}
    files.update({name: (ROOT / name).read_bytes() for name in canonical.HARNESS})
    return files, validate_snapshot(files, 78, U2_CURRENT_SOURCE_SHA256, U2_HARNESS_SHA256)


def old_commands(path, case=replay.POSITIVE_CASE):
    require(case in (replay.POSITIVE_CASE, replay.SOURCE_CURRENT_CASE), "exact boundary recording generation required")
    path = replay.output_path(path)
    require(path.suffix == ".jsonl" and not path.is_symlink(), "explicit old public JSONL required")
    require(path.stat().st_size == replay.RECORDINGS[case]["public_bytes"],
            "exact old public input byte count required")
    return replay.audit_public(path.read_bytes(), case)


def common_result(summary, receipt, case=replay.POSITIVE_CASE):
    require(type(summary) is dict and type(receipt) is dict, "actual summary and replay receipt objects required")
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
            "replayFileSha256": replay.RECORDINGS[case]["public_sha256"],
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


def main39_positive(code, summary, receipt):
    common_result(summary, receipt, replay.SOURCE_CURRENT_CASE)
    require(summary.get("mode") == "m1-complete", "main39 positive must execute m1-complete")
    require(canonical.require_m1_replay(code, summary, receipt, "portable") == "positive",
            "main39 full public positive required")
    replay.exact_metadata({key: receipt.get(key) for key in (
        "completedHosts", "SourceTick", "rawCursor", "rSkipFrames", "positive", "failure")}, {
            "completedHosts": 12000, "SourceTick": 11400, "rawCursor": 22504, "rSkipFrames": 164,
            "positive": {"actor": 12, "InstanceId": 201, "firstTick": 6738, "secondTick": 6824,
                         "firstOrdinal": 0, "secondOrdinal": 16}, "failure": None,
        }, "main39-positive")
    return "positive-pinned-main39-possession-only"


def stale_result(code, summary, receipt, fault, row, *, current):
    case = replay.SOURCE_CURRENT_CASE if current else replay.POSITIVE_CASE
    host, source_tick, raw = (2835, 2694, 5792) if current else (5845, 5553, 10142)
    common_result(summary, receipt, case)
    require(code == 2 and summary.get("status") == "error" and
            summary.get("public_R_SKIP_gate_qualified") is not True and
            summary.get("complete_publication_qualification") is not True,
            "current stale recording must fail, never success-shaped acceptance")
    require(type(summary.get("error")) is str and summary["error"].splitlines()[0:1] == [SIDE_ERROR],
            "current stale recording must reject specifically playbook-side validation")
    require("mode" not in summary and "Program.M1CompleteReplay(" in summary["error"],
            "actual replay error summary required, not a successful-mode or generic catch")
    replay.exact_metadata({key: receipt.get(key) for key in (
        "completedHosts", "SourceTick", "rawCursor", "rSkipFrames", "positive")}, {
            "completedHosts": host + 1, "SourceTick": source_tick, "rawCursor": raw,
            "rSkipFrames": 0, "positive": None,
        }, "current-stale")
    failure = receipt.get("failure")
    require(type(failure) is dict and fault == failure, "actual first kernel fault must match replay failure")
    replay.exact_metadata({key: failure.get(key) for key in ("host", "before", "SourceTick")},
                          {"host": host, "before": source_tick - 1, "SourceTick": source_tick}, "stale-fault")
    error = failure.get("error", "")
    require(type(error) is str and error.splitlines()[0:1] == [SIDE_ERROR] and
            "GameSimulation.SelectSourcePlay(" in error and "GameSimulation.ApplySourceStationMenuCommands(" in error,
            "actual first fault must be the source-side validator, not a generic exception")
    command = json.loads(json.dumps(failure["command"]))
    for station in command["StationInputs"]["Stations"]:
        require(station.pop("IsSpecified", None) is True, "actual station constructor must be specified")
    replay.public_row({"host": host, "command": command}, host)
    require(row["host"] == host and command == row["command"], "same immutable old command required at stale boundary")
    stations = command["StationInputs"]["Stations"]
    require(len(stations) == 1 and stations[0]["StationId"] == 0 and
            stations[0]["Command"]["Action"] == 1 and
            stations[0]["Command"]["Play"]["PlaySymbol"] == ("play_hail_mary" if current else "play_suicide_blitz") and
            stations[0]["Command"]["Play"]["Playbook"] == (0 if current else 1),
            "exact original station0 stale playbook-side command required")
    actual = failure["actual"]
    require(type(actual) is dict and type(actual.get("State")) is dict,
            "actual stale PlaySelect/process context objects required")
    replay.exact_metadata({key: actual.get(key) for key in (
        "host", "rawCursor", "SourceTick", "SourceRandomProcess", "SourceRandomActor",
        "SourceRandomProcessOrder", "SourceRandomCallIndex", "SourceFlags")}, {
            "host": host, "rawCursor": raw, "SourceTick": source_tick,
            "SourceRandomProcess": "game_proc", "SourceRandomActor": -1,
            "SourceRandomProcessOrder": 1 if current else 0, "SourceRandomCallIndex": 0, "SourceFlags": 0,
        }, "stale-process-context")
    replay.exact_metadata({key: actual["State"].get(key) for key in (
        "Tick", "PlayNumber", "Phase", "Possession", "ControlledPlayerId")}, {
            "Tick": host, "PlayNumber": 8 if current else 17, "Phase": 0,
            "Possession": 1 if current else 0, "ControlledPlayerId": 3,
        }, "stale-playselect-context")
    return ("rejected-current-possession-stale-playbook-side" if current
            else "rejected-pinned-main39-old-stale-playbook-side")


def main39_stale(code, summary, receipt, fault, row):
    return stale_result(code, summary, receipt, fault, row, current=False)


def current_stale(code, summary, receipt, fault, row):
    return stale_result(code, summary, receipt, fault, row, current=True)


def summary_line(stdout):
    rows = [replay.decode(line) for line in stdout.splitlines() if line.startswith('{"schema":')]
    require(len(rows) == 1 and type(rows[0]) is dict, "one actual managed summary required")
    return rows[0]


def execute(label, files, args, old_rows, possession_rows):
    expected = {
        "historical": (76, BASELINE_SOURCE_SHA256, HARNESS_SHA256),
        "main39": (77, PREVIOUS_SOURCE_SHA256, HARNESS_SHA256),
        "current": (78, CURRENT_SOURCE_SHA256, HARNESS_SHA256),
        "u2-current": (78, U2_CURRENT_SOURCE_SHA256, U2_HARNESS_SHA256),
    }
    require(label in expected, "exact named source generation execution required")
    validate_snapshot(files, *expected[label])
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
    if label == "historical":
        probes = (("main38-old-positive", args.replay_commands, historical_positive, None),)
    elif label == "main39":
        probes = (
            ("main39-old-stale", args.replay_commands, main39_stale, old_rows[5845]),
            ("main39-possession-positive", args.possession_replay_commands, main39_positive, None),
        )
    else:
        require(label in ("current", "u2-current"), "exact named source generation execution required")
        probes = ((label + "-possession-stale", args.possession_replay_commands, current_stale, possession_rows[2835]),)
    results = {}
    for name, path, classify, stale_row in probes:
        started = time.monotonic()
        destination = root / name
        process = run(["dotnet", str(build / "bin/Release/net10.0/MovementCallerChecks.dll"),
                       str(args.fixture_manifest), str(destination), "m1-complete", str(args.sound_functions),
                       str(args.c2_m1_inputs), str(path), "--source-compatibility", "portable"],
                      name + ".log", 900)
        summary = summary_line(process.stdout)
        receipt = replay.decode((destination / "m1-replay-result.json").read_text())
        if stale_row is None:
            classification = classify(process.returncode, summary, receipt)
        else:
            fault = replay.decode((destination / "first-m1-kernel-fault.json").read_text())
            classification = classify(process.returncode, summary, receipt, fault, stale_row)
        draws = replay.decode((destination / "m1-all-raw-draws.json").read_text())
        require(type(draws) is list and len(draws) == receipt["rawCursor"] and
                all(draw.get("Order") == i for i, draw in enumerate(draws)), "complete ordered actual raw trace required")
        results[name] = {
            "classification": classification, "source_generation": label, "exit_code": process.returncode,
            "seconds": round(time.monotonic() - started, 3), "compile_zero_diagnostics": True,
            "receipt_sha256": replay.sha((destination / "m1-replay-result.json").read_bytes()),
            "raw_trace_sha256": replay.sha((destination / "m1-all-raw-draws.json").read_bytes()),
            "summary": {key: value for key, value in summary.items() if key != "actual_factory_replay"},
            "receipt_metadata": {key: receipt[key] for key in (
                "completedHosts", "SourceTick", "rawCursor", "rSkipFrames", "positive", "replayFileSha256")},
        }
    require({name: (source / name).read_bytes() for name in files} == files, "isolated source/harness changed during probe")
    shutil.rmtree(source)
    return {"probes": results, "commands": commands, "compile_zero_diagnostics": True}


def argument_parser():
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ("fixture-manifest", "sound-functions", "c2-m1-inputs", "replay-commands",
                 "possession-replay-commands", "output"):
        parser.add_argument("--" + name, type=Path, required=True, action="append")
    parser.add_argument("--baseline-ref", required=True, action="append", choices=(BASELINE_REF,))
    parser.add_argument("--previous-ref", required=True, action="append", choices=(PREVIOUS_REF,))
    parser.add_argument("--partition", action="append", choices=("all-five", *PROBE_PARTITIONS),
                        help="Closed CI placement only; omission retains all five actual probes.")
    return parser


def main(argv=None):
    parser = argument_parser()
    args = parser.parse_args(argv)
    for name, values in vars(args).items():
        if name == "partition" and values is None:
            values = ["all-five"]
        if len(values) != 1:
            parser.error("duplicate --" + name.replace("_", "-") + " argument")
        setattr(args, name, values[0].absolute() if isinstance(values[0], Path) else values[0])
    started = time.monotonic()
    result = {"schema": "source-public-replay-generations/v2", "status": "failed",
              "current_full_M1_qualification": False, "native_or_whole_game_qualified": False,
              "partition": args.partition, "required_probe_inventory": list(ALL_PROBES),
              "selected_probe_inventory": list(probe_inventory(args.partition))}
    try:
        args.output = replay.output_path(args.output)
        require(not args.output.exists(), "use a fresh ignored generation-check output; never overwrite")
        rows = old_commands(args.replay_commands)
        possession_rows = old_commands(args.possession_replay_commands, replay.SOURCE_CURRENT_CASE)
        fixture_profile(args.fixture_manifest, "contact272")
        inputs = fixture_hashes(args.fixture_manifest, args.sound_functions)
        inputs.update(canonical_hashes(args.c2_m1_inputs, "contact281-c2-m1"))
        require(len(inputs) == 1122, "exact834 plus288 original input closure required")
        snapshots = {}
        if args.partition != "u2-current-fifth":
            snapshots["historical"] = historical_snapshot(args.baseline_ref)
            snapshots["main39"] = historical_snapshot(args.previous_ref, previous=True)
            snapshots["current"] = current_snapshot()
        if args.partition != "archived-four":
            snapshots["u2-current"] = u2_current_snapshot()
        args.output.mkdir(parents=True)
        result.update(sources={label: identity for label, (_, identity) in snapshots.items()},
                      inputs_sha256=inputs, old_public_sha256=OLD_PUBLIC_SHA256,
                      possession_public_sha256=POSSESSION_PUBLIC_SHA256, probes={}, commands={})
        with ThreadPoolExecutor(max_workers=2) as workers:
            pending = {label: workers.submit(execute, label, files, args, rows, possession_rows)
                       for label, (files, _) in snapshots.items()}
            errors = []
            for label, future in pending.items():
                try:
                    executed = future.result()
                    require(set(executed["probes"]) == {
                        name for name in probe_inventory(args.partition) if PROBE_SOURCES[name] == label
                    } and not set(result["probes"]).intersection(executed["probes"]),
                            "exact nonduplicated probes from every selected source generation required")
                    result["probes"].update(executed["probes"])
                    result["commands"][label] = executed["commands"]
                except (ValueError, KeyError, TypeError, AttributeError, OSError,
                        RuntimeError, subprocess.SubprocessError) as error:
                    errors.append(label + ": " + str(error))
            require(not errors, "generation probe failed: " + "; ".join(errors))
        validate_probe_inventory(args.partition, result["probes"])
        result["executed_probe_inventory"] = list(probe_inventory(args.partition))
        after = fixture_hashes(args.fixture_manifest, args.sound_functions)
        after.update(canonical_hashes(args.c2_m1_inputs, "contact281-c2-m1"))
        readers = {"historical": lambda: historical_snapshot(args.baseline_ref),
                   "main39": lambda: historical_snapshot(args.previous_ref, previous=True),
                   "current": current_snapshot, "u2-current": u2_current_snapshot}
        require(inputs == after and all(readers[label]()[0] == files for label, (files, _) in snapshots.items()) and
                old_commands(args.replay_commands) == rows and
                old_commands(args.possession_replay_commands, replay.SOURCE_CURRENT_CASE) == possession_rows,
                "source/harness/input generation changed during probes")
        result.update(status="passed-recording-generation-checks", source_harness_inputs_unchanged=True)
    except (ValueError, KeyError, TypeError, AttributeError, OSError, RuntimeError,
            subprocess.SubprocessError, tarfile.TarError) as error:
        result["error"] = str(error)
        print("Public recording generation checks failed: " + str(error), file=sys.stderr)
    result["seconds"] = round(time.monotonic() - started, 3)
    if args.output.exists() and "sources" in result:
        replay.write_new_or_verify(args.output / "result.json", (json.dumps(result, indent=2) + "\n").encode())
    print(json.dumps({key: result[key] for key in (
        "schema", "status", "current_full_M1_qualification", "native_or_whole_game_qualified")}))
    return 0 if result["status"] == "passed-recording-generation-checks" else 1


if __name__ == "__main__":
    raise SystemExit(main())
