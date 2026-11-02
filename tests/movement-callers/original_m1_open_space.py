#!/usr/bin/env python3
"""One original-M1/281 window using the unchanged reviewed v7 public controller."""
import argparse
from datetime import datetime, timezone
import gzip
import json
import os
from pathlib import Path
import subprocess
import sys
import time

import canonical
import export_portable_public as exporter
import open_space_scenario as reviewed
import prepare_public_replay as public

ROOT = canonical.ROOT
AUTHORITY = ROOT / ".squad/.scratch/original-m1-v8-authority"
POLICY = AUTHORITY / "prospective-policy.json"
CASE = "trips-rollout-clearance-original-m1-v8-12000"
GENERATION = "source-public-open-space-original-m1/v8"
CONTROLLER = reviewed.CONTROLLER
CONTROLLER_SHA256 = public.OPEN_SPACE_CONTROLLER_SHA256
SOURCE_SHA256 = public.OPEN_SPACE_SOURCE_SHA256
REPLAY_HARNESS_SHA256 = public.OPEN_SPACE_HARNESS_SHA256
CONFIGURATION_SHA256 = "1d377a5738f41be8393fbd7bb77789f6fb665643b22e4dbc0f6a32908ba10239"
ADAPTER = ("tests/movement-callers/OriginalM1OpenSpaceChecks.csproj",
           "tests/movement-callers/OriginalM1OpenSpace.cs")
TOOLS = ("tests/movement-callers/original_m1_open_space.py", *reviewed.TOOLS)
FAILED_V6 = ".squad/.scratch/quality-current-input-compatibility-20261101/v6-authority/"
V7 = ".squad/.scratch/unity-rskip-scenario-20261101/authority/"
HISTORY = tuple(FAILED_V6 + name for name in (
    "policy.json", "only-public-window-started.json", "authoring-result.json",
    "capture/public-window-result.json")) + tuple(V7 + name for name in (
    "prospective-policy.json", "only-public-window-started.json", "only-authoring-invocation.json",
    "authoring-result.json", "capture/public-window-result.json"))
require = public.require
sha = public.sha
manifest = reviewed.manifest
write_new = reviewed.write_new


def inventories():
    production, harness = reviewed.inventories()
    canonical.observer_binding(production, harness)
    require(harness[CONTROLLER] and sha(harness[CONTROLLER]) == CONTROLLER_SHA256,
            "The complete reviewed v7 controller file must remain byte-identical.")
    return production, harness, {name: (ROOT / name).read_bytes() for name in ADAPTER}


def inputs(fixture, sounds, profile):
    canonical.fixture_profile(fixture, "contact272")
    data = canonical.fixture_hashes(fixture, sounds)
    data.update(canonical.canonical_hashes(profile, "contact281-c2-m1"))
    require(len(data) == 1122, "Exact original-M1/281 data closure required; no enabled282 alias.")
    return data


def history():
    names = [p.relative_to(ROOT).as_posix() for folder in ("fixtures", "provenance")
             for p in sorted((canonical.HERE / folder).glob("*")) if p.is_file()]
    return {name: sha((ROOT / name).read_bytes()) for name in (*names, *HISTORY)}


def identity(policy):
    expected = dict(
        schema="source-original-M1-open-space-frozen-first-window/v1", case=CASE,
        recordingGeneration=GENERATION, name=reviewed.NAME, version=reviewed.VERSION,
        worktree=str(ROOT), authority=str(AUTHORITY), controllerSource=CONTROLLER,
        controllerSha256=CONTROLLER_SHA256, maxHosts=12000, maximumInitialWindows=1,
        maximumAuthoringInvocations=1, noRetryOrExtension=True, seed=1,
        providerProfile="source-sound-smoke/raw-vector-v1", rawVector=reviewed.RAW,
        rawVectorSha256=reviewed.RAW_SHA256, sourceManifestSha256=SOURCE_SHA256,
        replayHarnessManifestSha256=REPLAY_HARNESS_SHA256,
        configuration=exporter.current_configuration(False),
        configurationSha256=CONFIGURATION_SHA256, startupObject="OriginalM1OpenSpaceEntry",
        profile="original-teams-contact281-c2-m1",
        factory="SourceC2Composition.CreateM1Simulation",
        callbackDataSha256=canonical.C2_M1_DATA_SHA, animationSha256=canonical.C2_ANIMATION_SHA)
    require(all(policy.get(name) == value for name, value in expected.items()),
            "Exact distinct original-M1 profile, paths, policy and single-window authority required.")
    require(manifest(policy["configuration"]) == CONFIGURATION_SHA256 and
            sha(json.dumps(policy["rawVector"], separators=(",", ":")).encode()) == reviewed.RAW_SHA256,
            "Original-M1 configuration and unchanged unfiltered provider seals must agree.")


def check_frozen(policy):
    identity(policy)
    production, harness, adapter = inventories()
    pins = {name: sha(raw) for name, raw in {**production, **harness, **adapter}.items()}
    require(pins == policy["frozenCodeSha256"] and
            manifest({name: sha(raw) for name, raw in {**harness, **adapter}.items()}) ==
            policy["authoringHarnessManifestSha256"], "Frozen source/base harness/separate entrypoint changed.")
    require({name: sha((ROOT / name).read_bytes()) for name in TOOLS} == policy["toolSha256"],
            "Prospectively frozen authoring tools changed.")
    data = inputs(Path(policy["fixtureManifest"]), Path(policy["soundFunctions"]), Path(policy["profileInputs"]))
    require(data == policy["frozenInputSha256"] and manifest(data) == policy["inputManifestSha256"],
            "Frozen original-M1 private input closure changed.")
    require(all(sha((ROOT / name).read_bytes()) == pin for name, pin in policy["historySha256"].items()),
            "Historical fixtures, v6 failure or v7 authorities changed.")
    for name, pin in reviewed.ORIGINAL_FILES.items():
        require(sha((Path(policy["originalRoot"]) / name).read_bytes()) == pin,
                "Pinned original source citation differs: " + name)
    dll = Path(policy["entrypoint"])
    require(dll == AUTHORITY / "build/bin/Release/net10.0/OriginalM1OpenSpaceChecks.dll" and
            not dll.is_symlink() and sha(dll.read_bytes()) == policy["entrypointSha256"] and
            sha(dll.with_suffix(".pdb").read_bytes()) == policy["pdbSha256"],
            "The actual separate original-M1 authoring PE/PDB must remain unchanged.")


def environment():
    work = AUTHORITY / "work"
    work.mkdir(parents=True, exist_ok=True)
    (work / "empty-feed").mkdir(exist_ok=True)
    return dict(os.environ, TMPDIR=str(work), TMP=str(work), TEMP=str(work),
                DOTNET_CLI_HOME=str(work / "dotnet-home"),
                DOTNET_CLI_TELEMETRY_OPTOUT="1", DOTNET_NOLOGO="1", DOTNET_EnableDiagnostics="0",
                MSBUILDDISABLENODEREUSE="1", PYTHONDONTWRITEBYTECODE="1")


def execute(command, label, commands, timeout=240):
    started = time.monotonic()
    process = subprocess.run(command, cwd=ROOT, env=environment(),
                             capture_output=True, text=True, timeout=timeout)
    text = process.stdout + process.stderr
    (AUTHORITY / (label + ".log")).write_text(text)
    commands.append(dict(command=command, exit=process.returncode, log=label + ".log",
                         seconds=round(time.monotonic() - started, 3)))
    return process, text


def freeze(args):
    require(not AUTHORITY.exists(), "The distinct fixed original-M1 authority cannot be replaced.")
    production, harness, adapter = inventories()
    data = inputs(args.fixture_manifest.absolute(), args.sound_functions.absolute(), args.m1_inputs.absolute())
    retained = history()
    old_v6 = public.decode((ROOT / (FAILED_V6 + "capture/public-window-result.json")).read_bytes())
    require(old_v6["completedHosts"] == 12000 and old_v6["initialExecutions"] == 1 and
            old_v6["rSkipFrames"] == 0 and old_v6["positive"] is None,
            "The exhausted v6 remains a failed boundary, never retry authority.")
    AUTHORITY.mkdir(parents=True)
    for name, raw in {**production, **harness, **adapter}.items():
        target = AUTHORITY / "snapshot" / name
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_bytes(raw)
    for name in TOOLS:
        target = AUTHORITY / "frozen-tools" / name
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_bytes((ROOT / name).read_bytes())
    build = AUTHORITY / "build"
    common = ["-p:BlitzSourceRoot=" + str(AUTHORITY / "snapshot") + "/",
              "-p:BaseIntermediateOutputPath=" + str(build / "obj") + "/",
              "-p:BaseOutputPath=" + str(build / "bin") + "/",
              "-p:UseSharedCompilation=false", "-p:TreatWarningsAsErrors=true"]
    project = AUTHORITY / "snapshot" / ADAPTER[0]
    command = ["dotnet", "build", str(project), "--no-restore", "-t:Rebuild",
               "-c", "Release", "-v:q", "--nologo", *common]
    commands = []
    process, text = execute(command, "sealed-build", commands)
    if process.returncode and "NETSDK1004" in text:
        restored, _ = execute(["dotnet", "restore", str(project), "--source",
                               str(AUTHORITY / "work/empty-feed"), "-v:q", "-p:NuGetAudit=false", *common],
                              "restore-after-actual-missing-assets", commands)
        require(restored.returncode == 0, "Empty-feed SDK restore failed after actual missing assets.")
        process, text = execute(command, "sealed-build-restored", commands)
    require(process.returncode == 0 and "0 Warning(s)" in text and "0 Error(s)" in text,
            "Fresh zero-diagnostic CSharp73 original-M1 entrypoint compilation required before sealing.")
    dll = build / "bin/Release/net10.0/OriginalM1OpenSpaceChecks.dll"
    checked, text = execute(["dotnet", str(dll), "policy-checks"], "zero-game-policy-checks", commands)
    checks = public.decode(text)
    require(checked.returncode == 0 and checks["status"] == "passed" and
            checks["actualGameConstructions"] == checks["actualGameSteps"] == 0,
            "Unchanged public-controller synthetic checks must pass without constructing/stepping a game.")
    policy = dict(
        schema="source-original-M1-open-space-frozen-first-window/v1", case=CASE,
        recordingGeneration=GENERATION, name=reviewed.NAME, version=reviewed.VERSION,
        worktree=str(ROOT), authority=str(AUTHORITY), frozenAtUtc=datetime.now(timezone.utc).isoformat(),
        controllerSource=CONTROLLER, controllerSha256=CONTROLLER_SHA256,
        maxHosts=12000, maximumInitialWindows=1, maximumAuthoringInvocations=1, noRetryOrExtension=True,
        seed=1, providerProfile="source-sound-smoke/raw-vector-v1", rawVector=reviewed.RAW,
        rawVectorSha256=reviewed.RAW_SHA256, configuration=exporter.current_configuration(False),
        configurationSha256=CONFIGURATION_SHA256, startupObject="OriginalM1OpenSpaceEntry",
        sourceManifestSha256=SOURCE_SHA256, sourceCount=103,
        replayHarnessManifestSha256=REPLAY_HARNESS_SHA256, replayHarnessCount=12,
        authoringHarnessManifestSha256=manifest({n: sha(b) for n, b in {**harness, **adapter}.items()}),
        authoringHarnessCount=14, frozenCodeSha256={n: sha(b) for n, b in {
            **production, **harness, **adapter}.items()},
        toolSha256={n: sha((ROOT / n).read_bytes()) for n in TOOLS},
        fixtureManifest=str(args.fixture_manifest.absolute()), soundFunctions=str(args.sound_functions.absolute()),
        profileInputs=str(args.m1_inputs.absolute()), frozenInputSha256=data, inputManifestSha256=manifest(data),
        inputCount=1122, profile="original-teams-contact281-c2-m1",
        factory="SourceC2Composition.CreateM1Simulation",
        callbackDataSha256=canonical.C2_M1_DATA_SHA, animationSha256=canonical.C2_ANIMATION_SHA,
        entrypoint=str(dll), entrypointSha256=sha(dll.read_bytes()),
        pdbSha256=sha(dll.with_suffix(".pdb").read_bytes()), historySha256=retained,
        originalCommit=reviewed.ORIGINAL_COMMIT, originalRoot=str(args.original_root.absolute()),
        originalSourceFilesSha256=reviewed.ORIGINAL_FILES, sourceCitations=reviewed.SOURCE_CITATIONS,
        publicProjection=["Phase", "Possession", "ControlledPlayerId", "Ball.CarrierId", "Ball.Position",
                          "LineOfScrimmage", "Match.PlayKind", "Players.Id/Team/Position/Velocity/Role/Turbo01"],
        footballPolicyChanged=False, oldReplayHarnessChanged=False, zeroGameplayPolicyChecks=checks,
        buildCommands=commands, wholeGameOrNativeAccepted=False)
    identity(policy)
    write_new(POLICY, policy)
    check_frozen(policy)
    print(json.dumps(dict(status="prospectively-frozen-zero-gameplay", policySha256=sha(POLICY.read_bytes()),
                          sourceFiles=103, unchangedReplayHarnessFiles=12, authoringHarnessFiles=14,
                          inputFiles=1122, syntheticAssertions=checks["assertions"])))
    return 0


def validate_window(receipt, counts, poses):
    require(receipt["schema"] == "source-original-M1-open-space-first-window/v1" and
            receipt["recordingGeneration"] == GENERATION and receipt["faults"] == [] and
            receipt["attemptedHosts"] == receipt["completedHosts"] == 12000 and
            receipt["initialExecutions"] == receipt["maximumInitialWindows"] == 1,
            "Exactly one complete fault-free original-M1 first window is required.")
    require(counts["gameConstructors"] == 1 and counts["directAnimationAdvanceCalls"] == 0 and
            counts["hostStepCalls"] == 12000 and counts["withinPerDriverBudget"] is True and
            counts["unfilteredRawNextValues"] == receipt["rawCursor"] <= 32768,
            "Actual original-M1 construction/host/raw budget accounting must pass.")
    exporter.require_candidate(receipt, poses, False)


def run():
    raw = POLICY.read_bytes()
    policy = public.decode(raw)
    require(not (AUTHORITY / "only-authoring-invocation.json").exists() and
            not (AUTHORITY / "only-public-window-started.json").exists(),
            "The original-M1 sole invocation has already been consumed; no retry or extension.")
    check_frozen(policy)
    write_new(AUTHORITY / "only-authoring-invocation.json", dict(
        policySha256=sha(raw), maximumAuthoringInvocations=1, maxHosts=12000,
        recordingGeneration=GENERATION, invokedAtUtc=datetime.now(timezone.utc).isoformat()))
    commands = []
    result = dict(schema="source-original-M1-only-authoring-window/v1", status="STOP-authoring-fault",
                  actualAuthoringInvocations=1, maximumInitialWindows=1, policySha256=sha(raw),
                  commands=commands, exportExecuted=False, wholeGameOrNativeAccepted=False)
    code = 2
    try:
        capture = AUTHORITY / "capture"
        process, _ = execute(["dotnet", policy["entrypoint"], policy["fixtureManifest"], str(capture),
                               policy["soundFunctions"], policy["profileInputs"], str(POLICY)],
                              "ONLY-public-window", commands, timeout=900)
        code = process.returncode
        receipt_raw = (capture / "public-window-result.json").read_bytes()
        receipt = public.decode(receipt_raw)
        counts = public.decode((capture / "execution-counts.json").read_bytes())
        result.update(receipt=receipt, receiptSha256=sha(receipt_raw), executionCounts=counts)
        public.exact_metadata(public.decode((AUTHORITY / "only-public-window-started.json").read_bytes()),
            dict(policySha=sha(raw), maximumInitialWindows=1, maxHosts=12000, recordingGeneration=GENERATION),
            "actual original-M1 pre-construction marker")
        require(receipt["policySha256"] == sha(raw), "Actual window is not bound to the prospective policy.")
        draws = public.decode((capture / "all-shared-raw-draws.json").read_bytes())
        require(len(draws) == receipt["rawCursor"] and
                all(d["Order"] == i and d["Raw"] == reviewed.RAW[i % len(reviewed.RAW)]
                    for i, d in enumerate(draws)), "Every raw value must be retained globally without filtering.")
        if code == 0:
            validate_window(receipt, counts, public.decode((capture / "R_SKIP-real-poses.json").read_bytes()))
            result["status"] = "recorded-new-window-full-qualification-required"
        else:
            result["status"] = "STOP-incomplete-fault-or-unmet-one-window"
    except (OSError, ValueError, KeyError, TypeError, RuntimeError, subprocess.SubprocessError) as error:
        result["error"] = str(error)
        code = 2
    finally:
        try:
            check_frozen(policy)
            result["frozenSourceHarnessToolsInputsAndHistoryUnchanged"] = True
        except (OSError, ValueError, KeyError) as error:
            result.update(frozenSourceHarnessToolsInputsAndHistoryUnchanged=False, preservationError=str(error))
            code = 2
        result.update(exit=code, noRetryOrExtension=True)
        write_new(AUTHORITY / "authoring-result.json", result)
    print(json.dumps(dict(status=result["status"], exit=code, report=str(AUTHORITY / "authoring-result.json"))))
    return code


def export():
    policy_raw = POLICY.read_bytes()
    policy = public.decode(policy_raw)
    check_frozen(policy)
    result = public.decode((AUTHORITY / "authoring-result.json").read_bytes())
    capture = AUTHORITY / "capture"
    receipt_raw = (capture / "public-window-result.json").read_bytes()
    receipt = public.decode(receipt_raw)
    require(result["exit"] == 0 and result["status"] == "recorded-new-window-full-qualification-required" and
            result["actualAuthoringInvocations"] == result["maximumInitialWindows"] == 1 and
            result["receipt"] == receipt and result["receiptSha256"] == sha(receipt_raw) and
            receipt["policySha256"] == sha(policy_raw) and
            result["frozenSourceHarnessToolsInputsAndHistoryUnchanged"] is True,
            "Only the authentic complete positive sole original-M1 window may export.")
    validate_window(receipt, public.decode((capture / "execution-counts.json").read_bytes()),
                    public.decode((capture / "R_SKIP-real-poses.json").read_bytes()))
    public.exact_metadata(public.decode((AUTHORITY / "only-public-window-started.json").read_bytes()),
        dict(policySha=sha(policy_raw), maximumInitialWindows=1, maxHosts=12000, recordingGeneration=GENERATION),
        "original-M1 actual pre-construction marker")
    invocation = public.decode((AUTHORITY / "only-authoring-invocation.json").read_bytes())
    require(invocation["policySha256"] == sha(policy_raw) and invocation["maximumAuthoringInvocations"] == 1 and
            invocation["maxHosts"] == 12000 and invocation["recordingGeneration"] == GENERATION,
            "Exactly one prospectively bound original-M1 invocation is required.")
    captured = (capture / "captured-public-commands.jsonl").read_bytes()
    require(sha(captured) == receipt["publicInputSha256"], "Actual saved public commands changed.")
    projected = exporter.project_capture(captured)
    packed = public.compress(projected)
    require(gzip.decompress(packed) == projected, "Original-M1 public input gzip roundtrip failed.")
    recipe = {
        **public.SOURCE_TIMER_EXPECTED_RECIPE, "case": CASE, "recording_generation": GENERATION,
        "policy": reviewed.VERSION, "controller_source": CONTROLLER, "controller_sha256": CONTROLLER_SHA256,
        "source_manifest_sha256": SOURCE_SHA256, "source_count": 103,
        "stable_profile": policy["profile"], "factory": policy["factory"], "station_layout": 2,
        "replay_harness_manifest_sha256": REPLAY_HARNESS_SHA256, "replay_harness_count": 12,
        "authoring_harness_manifest_sha256": policy["authoringHarnessManifestSha256"],
        "authoring_harness_count": 14, "authoring_policy_sha256": sha(policy_raw),
        "configuration_sha256": CONFIGURATION_SHA256,
        "input_manifest_sha256": policy["inputManifestSha256"], "input_count": 1122,
        "entrypoint_sha256": policy["entrypointSha256"], "pdb_sha256": policy["pdbSha256"],
        "startup_object": "OriginalM1OpenSpaceEntry", "maximum_authoring_windows": 1,
        "raw_vector_sha256": reviewed.RAW_SHA256,
        "jsonl": dict(file=CASE + ".jsonl", sha256=sha(projected), bytes=len(projected)),
        "gzip": dict(file=CASE + ".jsonl.gz", sha256=sha(packed), bytes=len(packed))}
    raw = (json.dumps(recipe, indent=2) + "\n").encode("ascii")
    for path, data in (
            (public.FIXTURE.with_name(CASE + ".jsonl.gz"), packed),
            (public.FIXTURE.with_name(CASE + ".recipe.json"), raw),
            (AUTHORITY / (CASE + ".jsonl"), projected)):
        public.write_new_or_verify(path, data)
    print(json.dumps(dict(status="exported-inputs-only-not-full-qualified", case=CASE,
        publicSha256=sha(projected), gzipSha256=sha(packed), recipeSha256=sha(raw),
        authoringWindows=1, gameplayExecutions=0, originalNumericalExpectedArraysExported=False)))
    return 0


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest="action", required=True)
    before = commands.add_parser("freeze")
    for name in ("fixture-manifest", "sound-functions", "m1-inputs", "original-root"):
        before.add_argument("--" + name, type=Path, required=True)
    commands.add_parser("run")
    commands.add_parser("export")
    args = parser.parse_args(argv)
    try:
        return freeze(args) if args.action == "freeze" else run() if args.action == "run" else export()
    except (OSError, ValueError, KeyError, TypeError, RuntimeError, subprocess.SubprocessError) as error:
        print(str(error), file=sys.stderr)
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
