#!/usr/bin/env python3
"""Final separately authorized original-M1 exhibition policy; one prospective window."""
import argparse
from datetime import datetime, timezone
import gzip
import json
import os
from pathlib import Path
import subprocess
import sys
import time

import original_m1_open_space as v8

canonical, exporter, reviewed, public = v8.canonical, v8.exporter, v8.reviewed, v8.public
ROOT = v8.ROOT
AUTHORITY = ROOT / ".squad/.scratch/original-m1-v9-prevent-authority"
PRECHECK = ROOT / ".squad/.scratch/original-m1-v9-prevent-preseal"
POLICY = AUTHORITY / "prospective-policy.json"
CASE = "running-taunt-exhibition-prevent-original-m1-v9-12000"
NAME = "running-taunt-exhibition-prevent-original-m1"
VERSION = "source-running-taunt-exhibition-prevent-controller/v1"
GENERATION = "source-public-running-taunt-exhibition-original-m1/v9"
ADAPTER = ("tests/movement-callers/OriginalM1PreventExhibitionChecks.csproj",
           "tests/movement-callers/OriginalM1PreventExhibition.cs")
WRAPPER = ADAPTER[1]
TOOLS = ("tests/movement-callers/original_m1_prevent_exhibition.py",
         "tests/movement-callers/test_prevent_exhibition.py", *v8.TOOLS)
POLICY_DECISION = dict(
    phase="LiveBall", possession="Away", humanTeam="Home", opposingPublishedCarrierOnly=True,
    offensiveFieldX="100-publicAwayCarrier.Position.X", minimumFieldXInclusive=75,
    maximumFieldXExclusive=95, cushionYards=8,
    belowCushion="raw joystick directly away from published carrier position",
    atOrAboveCushion="zero raw joystick axes; no fixed lane or carrier prediction",
    coincidentPublicPoints="fixed positive world X escape",
    heldButtons="original controller Held with only defensive A tackle removed",
    preserveOriginalTurbo=True, delegateOriginalEveryHostExactlyOnce=True,
    outsideBand="return original intent byte-for-byte", competitiveFootballClaim=False)
POLICY_MUTANTS = (
    ("lower-bound", "fieldX < 75f || fieldX >= 95f", "fieldX < 74f || fieldX >= 95f",
     "prevent wrapper delegates every input outside exact defensive band"),
    ("upper-bound", "fieldX < 75f || fieldX >= 95f", "fieldX < 75f || fieldX > 95f",
     "prevent wrapper delegates every input outside exact defensive band"),
    ("six-not-eight", "if (dx * dx + dz * dz >= 64f)", "if (dx * dx + dz * dz >= 36f)",
     "prevent wrapper exact8 yard threshold retreats below and holds at or above"),
    ("drops-base-turbo", "SourceButtons held = original.Held & ~SourceButtons.A;",
     "SourceButtons held = SourceButtons.None;",
     "prevent wrapper preserves original turbo and removes only defensive tackle"),
    ("same-team", "if (carrier.Team == human.Team) return original;",
     "if (view.Possession == TeamId.Home) return original;",
     "prevent wrapper delegates every input outside exact defensive band"),
    ("double-base-advance", "PortableIntent original = controller.Choose(view);",
     "PortableIntent original = controller.Choose(view); controller.Choose(view);",
     "prevent wrapper delegates every input outside exact defensive band"),
)
require, sha, manifest, write_new = v8.require, v8.sha, v8.manifest, v8.write_new


def inventories():
    production, harness, _ = v8.inventories()
    return production, harness, {name: (ROOT / name).read_bytes() for name in ADAPTER}


def retained_history():
    result = v8.history()
    for path in sorted(v8.AUTHORITY.rglob("*")):
        if path.is_file():
            require(not path.is_symlink(), "Failed-v8 authority must contain only regular evidence.")
            result[path.relative_to(ROOT).as_posix()] = sha(path.read_bytes())
    for name in (*v8.ADAPTER, "tests/movement-callers/original_m1_open_space.py"):
        result[name] = sha((ROOT / name).read_bytes())
    return result


def environment(work):
    work.mkdir(parents=True, exist_ok=True)
    (work / "empty-feed").mkdir(exist_ok=True)
    return dict(os.environ, TMPDIR=str(work), TMP=str(work), TEMP=str(work),
                DOTNET_CLI_HOME=str(work / "dotnet-home"), DOTNET_NOLOGO="1",
                DOTNET_CLI_TELEMETRY_OPTOUT="1", DOTNET_EnableDiagnostics="0",
                MSBUILDDISABLENODEREUSE="1", PYTHONDONTWRITEBYTECODE="1")


def execute(command, label, commands, folder=AUTHORITY, timeout=240):
    started = time.monotonic()
    process = subprocess.run(command, cwd=ROOT, env=environment(folder / "work"),
                             capture_output=True, text=True, timeout=timeout)
    text = process.stdout + process.stderr
    (folder / (label + ".log")).write_text(text)
    commands.append(dict(command=command, exit=process.returncode, log=label + ".log",
                         seconds=round(time.monotonic() - started, 3)))
    return process, text


def build(snapshot, destination, label, commands, folder=AUTHORITY):
    common = ["-p:BlitzSourceRoot=" + str(snapshot) + "/",
              "-p:BaseIntermediateOutputPath=" + str(destination / "obj") + "/",
              "-p:BaseOutputPath=" + str(destination / "bin") + "/",
              "-p:UseSharedCompilation=false", "-p:TreatWarningsAsErrors=true"]
    project = snapshot / ADAPTER[0]
    command = ["dotnet", "build", str(project), "--no-restore", "-t:Rebuild",
               "-c", "Release", "-v:q", "--nologo", *common]
    process, text = execute(command, label + "-build", commands, folder)
    if process.returncode and "NETSDK1004" in text:
        restored, _ = execute(["dotnet", "restore", str(project), "--source",
                               str(folder / "work/empty-feed"), "-v:q", "-p:NuGetAudit=false", *common],
                              label + "-restore-after-missing-assets", commands, folder)
        require(restored.returncode == 0, "Empty-feed SDK restore failed after actual missing assets.")
        process, text = execute(command, label + "-build-restored", commands, folder)
    require(process.returncode == 0 and "0 Warning(s)" in text and "0 Error(s)" in text,
            "Fresh zero-diagnostic CSharp73 compilation required: " + label)
    return destination / "bin/Release/net10.0/OriginalM1PreventExhibitionChecks.dll"


def snapshot_files(folder, files):
    for name, raw in files.items():
        target = folder / name
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_bytes(raw)


def precheck():
    require(not PRECHECK.exists() and not AUTHORITY.exists(),
            "Use the sole fixed pre-seal check workspace before any v9 authority exists.")
    production, harness, adapter = inventories()
    files = {**production, **harness, **adapter}
    PRECHECK.mkdir(parents=True)
    commands, runs = [], {}
    for name, old, new, expected in (("baseline", None, None, None), *POLICY_MUTANTS):
        snapshot = PRECHECK / "snapshots" / name
        snapshot_files(snapshot, files)
        if old is not None:
            path = snapshot / WRAPPER
            text = path.read_text()
            require(text.count(old) == 1, "Each wrapper control requires a unique actual code anchor.")
            path.write_text(text.replace(old, new))
        dll = build(snapshot, PRECHECK / "build" / name, name, commands, PRECHECK)
        if old is None:
            original, text = execute(["dotnet", str(dll), "original-policy-checks"],
                                     "original157-policy-checks", commands, PRECHECK)
            checks = public.decode(text)
            require(original.returncode == 0 and checks["assertions"] == 157 and
                    checks["actualGameConstructions"] == checks["actualGameSteps"] == 0,
                    "The unchanged original157 policy checks must execute with zero gameplay.")
            runs["original157"] = checks
        process, text = execute(["dotnet", str(dll), "prevent-policy-checks"],
                                name + "-policy-checks", commands, PRECHECK)
        checks = public.decode(text)
        require(checks["actualGameConstructions"] == checks["actualGameSteps"] == 0,
                "Wrapper pre-seal checks must never construct or step a game.")
        if old is None:
            require(process.returncode == 0 and checks["status"] == "passed",
                    "Actual prevent wrapper synthetic checks failed.")
        else:
            require(process.returncode == 1 and checks["status"] == "failed" and
                    expected in checks["failed_assertion"],
                    "Actual compiled wrapper control must reject its own named contract: " + name)
        runs[name] = dict(exit=process.returncode, checks=checks, zeroDiagnostics=True,
                          sourceSha256=sha((snapshot / WRAPPER).read_bytes()))
    require(all((ROOT / n).read_bytes() == raw for n, raw in files.items()),
            "Pre-seal synthetic controls must not change actual source or policy.")
    result = dict(schema="original-M1-prevent-exhibition-zero-game-controls/v1",
                  status="passed", commands=commands, runs=runs, actualGameConstructions=0,
                  actualGameSteps=0, compiledCausalControls=len(POLICY_MUTANTS))
    write_new(PRECHECK / "result.json", result)
    print(json.dumps(dict(status="passed-zero-game-original157-wrapper-and-six-controls",
        originalAssertions=157, wrapperAssertions=runs["baseline"]["checks"]["assertions"],
        controls=len(POLICY_MUTANTS), actualGameConstructions=0, actualGameSteps=0)))
    return 0


def identity(policy):
    expected = dict(
        schema="source-original-M1-prevent-exhibition-frozen-first-window/v1",
        case=CASE, name=NAME, version=VERSION, recordingGeneration=GENERATION,
        baseControllerVersion=reviewed.VERSION, controllerSource=v8.CONTROLLER,
        controllerSha256=v8.CONTROLLER_SHA256, wrapperSource=WRAPPER,
        worktree=str(ROOT), authority=str(AUTHORITY), maxHosts=12000, maximumRawValues=32768,
        maximumInitialWindows=1, maximumAuthoringInvocations=1, finalOldM1ScenarioCycle=True,
        noRetryOrExtension=True, seed=1, providerProfile="source-sound-smoke/raw-vector-v1",
        rawVector=reviewed.RAW, rawVectorSha256=reviewed.RAW_SHA256,
        sourceManifestSha256=v8.SOURCE_SHA256, replayHarnessManifestSha256=v8.REPLAY_HARNESS_SHA256,
        configuration=exporter.current_configuration(False),
        configurationSha256=v8.CONFIGURATION_SHA256, startupObject="OriginalM1PreventExhibitionEntry",
        profile="original-teams-contact281-c2-m1", factory="SourceC2Composition.CreateM1Simulation",
        callbackDataSha256=canonical.C2_M1_DATA_SHA, animationSha256=canonical.C2_ANIMATION_SHA,
        policyDecision=POLICY_DECISION)
    require(all(policy.get(name) == value for name, value in expected.items()),
            "The exact prospective final exhibition policy/profile/config/provider/paths are required.")
    require(manifest(policy["configuration"]) == v8.CONFIGURATION_SHA256 and
            sha(json.dumps(policy["rawVector"], separators=(",", ":")).encode()) == reviewed.RAW_SHA256,
            "Unchanged original-M1 configuration and raw-vector seals required.")


def check_frozen(policy):
    identity(policy)
    production, harness, adapter = inventories()
    require({n: sha(b) for n, b in {**production, **harness, **adapter}.items()} ==
            policy["frozenCodeSha256"] and
            policy["wrapperSha256"] == sha(adapter[WRAPPER]) and
            manifest({n: sha(b) for n, b in {**harness, **adapter}.items()}) ==
            policy["authoringHarnessManifestSha256"],
            "Frozen original source/base controller/separate wrapper entrypoint changed.")
    require({n: sha((ROOT / n).read_bytes()) for n in TOOLS} == policy["toolSha256"],
            "Frozen final-window tool or policy-test source changed.")
    data = v8.inputs(Path(policy["fixtureManifest"]), Path(policy["soundFunctions"]), Path(policy["profileInputs"]))
    require(data == policy["frozenInputSha256"] and manifest(data) == policy["inputManifestSha256"],
            "Exact frozen original281 input closure changed.")
    require(all(sha((ROOT / n).read_bytes()) == pin for n, pin in policy["historySha256"].items()),
            "Historical fixtures, v6/v8 failures, or v7 authorities changed.")
    dll = Path(policy["entrypoint"])
    require(dll == AUTHORITY / "build/bin/Release/net10.0/OriginalM1PreventExhibitionChecks.dll" and
            not dll.is_symlink() and sha(dll.read_bytes()) == policy["entrypointSha256"] and
            sha(dll.with_suffix(".pdb").read_bytes()) == policy["pdbSha256"],
            "The actual prospectively frozen final-window PE/PDB must remain unchanged.")
    require(sha((PRECHECK / "result.json").read_bytes()) == policy["preSealChecksSha256"],
            "Actual compiled zero-game policy control receipt changed.")
    for name, pin in reviewed.ORIGINAL_FILES.items():
        require(sha((Path(policy["originalRoot"]) / name).read_bytes()) == pin,
                "Original source citation differs: " + name)


def freeze(args):
    require(not AUTHORITY.exists(), "The distinct final old-M1 authority cannot be replaced.")
    v8.check_frozen(public.decode(v8.POLICY.read_bytes()))
    failed = public.decode((v8.AUTHORITY / "authoring-result.json").read_bytes())
    require(failed["exit"] == 1 and failed["actualAuthoringInvocations"] == 1 and
            failed["receipt"]["rSkipFrames"] == 0 and failed["exportExecuted"] is False,
            "The exhausted v8 is retained as failed, never reused or exported.")
    pre = public.decode((PRECHECK / "result.json").read_bytes())
    require(pre["status"] == "passed" and pre["compiledCausalControls"] == 6 and
            pre["actualGameConstructions"] == pre["actualGameSteps"] == 0,
            "Actual zero-game wrapper scope/control checks must pass before the prospective seal.")
    production, harness, adapter = inventories()
    data = v8.inputs(args.fixture_manifest.absolute(), args.sound_functions.absolute(), args.m1_inputs.absolute())
    history = retained_history()
    AUTHORITY.mkdir(parents=True)
    snapshot_files(AUTHORITY / "snapshot", {**production, **harness, **adapter})
    snapshot_files(AUTHORITY / "frozen-tools", {n: (ROOT / n).read_bytes() for n in TOOLS})
    commands = []
    dll = build(AUTHORITY / "snapshot", AUTHORITY / "build", "sealed", commands)
    policy_checks = {}
    for selector in ("original-policy-checks", "prevent-policy-checks"):
        process, text = execute(["dotnet", str(dll), selector], selector, commands)
        checks = public.decode(text)
        require(process.returncode == 0 and checks["status"] == "passed" and
                checks["actualGameConstructions"] == checks["actualGameSteps"] == 0,
                "The actual sealed PE must pass both policy checks with zero gameplay.")
        policy_checks[selector] = checks
    require(policy_checks["original-policy-checks"]["assertions"] == 157,
            "The base controller's original157 synthetic checks must remain intact.")
    policy = dict(
        schema="source-original-M1-prevent-exhibition-frozen-first-window/v1",
        case=CASE, name=NAME, version=VERSION, recordingGeneration=GENERATION,
        baseControllerVersion=reviewed.VERSION, controllerSource=v8.CONTROLLER,
        controllerSha256=v8.CONTROLLER_SHA256, wrapperSource=WRAPPER, wrapperSha256=sha(adapter[WRAPPER]),
        worktree=str(ROOT), authority=str(AUTHORITY), frozenAtUtc=datetime.now(timezone.utc).isoformat(),
        maxHosts=12000, maximumRawValues=32768, maximumInitialWindows=1, maximumAuthoringInvocations=1,
        finalOldM1ScenarioCycle=True, noRetryOrExtension=True, seed=1,
        providerProfile="source-sound-smoke/raw-vector-v1", rawVector=reviewed.RAW,
        rawVectorSha256=reviewed.RAW_SHA256, sourceManifestSha256=v8.SOURCE_SHA256, sourceCount=103,
        replayHarnessManifestSha256=v8.REPLAY_HARNESS_SHA256, replayHarnessCount=12,
        authoringHarnessManifestSha256=manifest({n: sha(b) for n, b in {**harness, **adapter}.items()}),
        authoringHarnessCount=14, frozenCodeSha256={n: sha(b) for n, b in {
            **production, **harness, **adapter}.items()}, toolSha256={n: sha((ROOT / n).read_bytes()) for n in TOOLS},
        fixtureManifest=str(args.fixture_manifest.absolute()), soundFunctions=str(args.sound_functions.absolute()),
        profileInputs=str(args.m1_inputs.absolute()), frozenInputSha256=data, inputManifestSha256=manifest(data),
        inputCount=1122, configuration=exporter.current_configuration(False),
        configurationSha256=v8.CONFIGURATION_SHA256, startupObject="OriginalM1PreventExhibitionEntry",
        profile="original-teams-contact281-c2-m1", factory="SourceC2Composition.CreateM1Simulation",
        callbackDataSha256=canonical.C2_M1_DATA_SHA, animationSha256=canonical.C2_ANIMATION_SHA,
        entrypoint=str(dll), entrypointSha256=sha(dll.read_bytes()), pdbSha256=sha(dll.with_suffix(".pdb").read_bytes()),
        historySha256=history, v8PolicySha256=sha(v8.POLICY.read_bytes()),
        preSealChecksSha256=sha((PRECHECK / "result.json").read_bytes()), preSealChecks=pre,
        sealedPolicyChecks=policy_checks, buildCommands=commands, policyDecision=POLICY_DECISION,
        originalRoot=str(args.original_root.absolute()), originalCommit=reviewed.ORIGINAL_COMMIT,
        originalSourceFilesSha256=reviewed.ORIGINAL_FILES,
        sourceAuthority="player.c4086..4120 selects among four unmodified competitors then overrides for any enemy under6 yards",
        publicProjection=["Phase", "Possession", "ControlledPlayerId", "Ball.CarrierId", "Ball.Position",
                          "LineOfScrimmage", "Match.PlayKind", "Players.Id/Team/Position/Velocity/Role/Turbo01"],
        wholeGameOrNativeAccepted=False)
    identity(policy)
    write_new(POLICY, policy)
    check_frozen(policy)
    print(json.dumps(dict(status="prospectively-frozen-zero-gameplay-final-oldM1-cycle",
        policySha256=sha(POLICY.read_bytes()), wrapperSha256=policy["wrapperSha256"],
        unchangedBaseControllerSha256=v8.CONTROLLER_SHA256,
        authoringHarnessManifestSha256=policy["authoringHarnessManifestSha256"],
        originalAssertions=157, wrapperAssertions=policy_checks["prevent-policy-checks"]["assertions"])))
    return 0


def validate_window(receipt, counts, poses):
    require(receipt["schema"] == "source-original-M1-prevent-exhibition-first-window/v1" and
            receipt["recordingGeneration"] == GENERATION and receipt["version"] == VERSION and
            receipt["baseControllerVersion"] == reviewed.VERSION and receipt["faults"] == [] and
            receipt["attemptedHosts"] == receipt["completedHosts"] == 12000 and
            receipt["initialExecutions"] == receipt["maximumInitialWindows"] == 1 and receipt["preventHosts"] > 0,
            "Exactly one complete actual final exhibition window is required.")
    require(counts["gameConstructors"] == 1 and counts["directAnimationAdvanceCalls"] == 0 and
            counts["hostStepCalls"] == 12000 and counts["withinPerDriverBudget"] is True and
            counts["unfilteredRawNextValues"] == receipt["rawCursor"] <= 32768,
            "Actual final-window construction/host/raw budget accounting must pass.")
    exporter.require_candidate(receipt, poses, False)


def run():
    raw = POLICY.read_bytes()
    policy = public.decode(raw)
    require(not (AUTHORITY / "only-authoring-invocation.json").exists() and
            not (AUTHORITY / "only-public-window-started.json").exists(),
            "The final old-M1 authoring invocation is consumed; no retry or further policy window.")
    check_frozen(policy)
    write_new(AUTHORITY / "only-authoring-invocation.json", dict(
        policySha256=sha(raw), maximumAuthoringInvocations=1, maxHosts=12000,
        recordingGeneration=GENERATION, invokedAtUtc=datetime.now(timezone.utc).isoformat()))
    commands = []
    result = dict(schema="source-original-M1-prevent-only-authoring-window/v1", status="STOP-authoring-fault",
        actualAuthoringInvocations=1, maximumInitialWindows=1, policySha256=sha(raw), commands=commands,
        exportExecuted=False, wholeGameOrNativeAccepted=False, finalOldM1ScenarioCycle=True)
    code = 2
    try:
        capture = AUTHORITY / "capture"
        process, _ = execute(["dotnet", policy["entrypoint"], policy["fixtureManifest"], str(capture),
                               policy["soundFunctions"], policy["profileInputs"], str(POLICY)],
                              "ONLY-final-public-window", commands, timeout=900)
        code = process.returncode
        receipt_raw = (capture / "public-window-result.json").read_bytes()
        receipt = public.decode(receipt_raw)
        counts = public.decode((capture / "execution-counts.json").read_bytes())
        result.update(receipt=receipt, receiptSha256=sha(receipt_raw), executionCounts=counts)
        public.exact_metadata(public.decode((AUTHORITY / "only-public-window-started.json").read_bytes()),
            dict(policySha=sha(raw), maximumInitialWindows=1, maxHosts=12000, recordingGeneration=GENERATION),
            "actual final exhibition pre-construction marker")
        require(receipt["policySha256"] == sha(raw), "Final exhibition receipt differs from prospective policy.")
        draws = public.decode((capture / "all-shared-raw-draws.json").read_bytes())
        require(len(draws) == receipt["rawCursor"] and
                all(d["Order"] == i and d["Raw"] == reviewed.RAW[i % len(reviewed.RAW)]
                    for i, d in enumerate(draws)), "Every raw value must remain globally unfiltered.")
        if code == 0:
            validate_window(receipt, counts, public.decode((capture / "R_SKIP-real-poses.json").read_bytes()))
            result["status"] = "recorded-new-window-full-qualification-required"
        else:
            result["status"] = "STOP-incomplete-fault-or-unmet-final-window"
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
            "Only the authentic complete positive final exhibition window may export.")
    validate_window(receipt, public.decode((capture / "execution-counts.json").read_bytes()),
                    public.decode((capture / "R_SKIP-real-poses.json").read_bytes()))
    public.exact_metadata(public.decode((AUTHORITY / "only-public-window-started.json").read_bytes()),
        dict(policySha=sha(policy_raw), maximumInitialWindows=1, maxHosts=12000, recordingGeneration=GENERATION),
        "final exhibition actual marker")
    invocation = public.decode((AUTHORITY / "only-authoring-invocation.json").read_bytes())
    require(invocation["policySha256"] == sha(policy_raw) and invocation["maximumAuthoringInvocations"] == 1 and
            invocation["maxHosts"] == 12000 and invocation["recordingGeneration"] == GENERATION,
            "Exactly one prospective final exhibition invocation is required.")
    captured = (capture / "captured-public-commands.jsonl").read_bytes()
    require(sha(captured) == receipt["publicInputSha256"], "Saved final exhibition public commands changed.")
    projected = exporter.project_capture(captured)
    packed = public.compress(projected)
    require(gzip.decompress(packed) == projected, "Final exhibition public gzip roundtrip failed.")
    recipe = {
        **public.SOURCE_TIMER_EXPECTED_RECIPE, "case": CASE, "recording_generation": GENERATION,
        "policy": VERSION, "controller_source": WRAPPER, "controller_sha256": policy["wrapperSha256"],
        "base_controller": reviewed.VERSION, "base_controller_source": v8.CONTROLLER,
        "base_controller_sha256": v8.CONTROLLER_SHA256, "policy_decision": POLICY_DECISION,
        "source_manifest_sha256": v8.SOURCE_SHA256, "source_count": 103,
        "stable_profile": policy["profile"], "factory": policy["factory"], "station_layout": 2,
        "replay_harness_manifest_sha256": v8.REPLAY_HARNESS_SHA256, "replay_harness_count": 12,
        "authoring_harness_manifest_sha256": policy["authoringHarnessManifestSha256"], "authoring_harness_count": 14,
        "authoring_policy_sha256": sha(policy_raw), "configuration_sha256": v8.CONFIGURATION_SHA256,
        "input_manifest_sha256": policy["inputManifestSha256"], "input_count": 1122,
        "entrypoint_sha256": policy["entrypointSha256"], "pdb_sha256": policy["pdbSha256"],
        "startup_object": "OriginalM1PreventExhibitionEntry", "maximum_authoring_windows": 1,
        "raw_vector_sha256": reviewed.RAW_SHA256, "competitive_football_claim": False,
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
    commands.add_parser("precheck")
    before = commands.add_parser("freeze")
    for name in ("fixture-manifest", "sound-functions", "m1-inputs", "original-root"):
        before.add_argument("--" + name, type=Path, required=True)
    commands.add_parser("run")
    commands.add_parser("export")
    args = parser.parse_args(argv)
    try:
        return precheck() if args.action == "precheck" else freeze(args) if args.action == "freeze" else \
            run() if args.action == "run" else export()
    except (OSError, ValueError, KeyError, TypeError, RuntimeError, subprocess.SubprocessError) as error:
        print(str(error), file=sys.stderr)
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
