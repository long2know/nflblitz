#!/usr/bin/env python3
"""Freeze and execute one distinct source-driven public-controller scenario, never search."""
import argparse
from datetime import datetime, timezone
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import time

import canonical
from export_portable_public import current_configuration, require_candidate


ROOT = canonical.ROOT
AUTHORITY = ROOT / ".squad/.scratch/unity-rskip-scenario-20261101/authority"
POLICY = AUTHORITY / "prospective-policy.json"
CASE = "trips-rollout-clearance-presnap-switch-v7-12000"
NAME = "trips-rollout-clearance"
VERSION = "source-trips-rollout-public-controller/v1"
GENERATION = "source-public-open-space/v7"
CONTROLLER = "tests/movement-callers/InputAndStation.cs"
RAW = [0, 1, 2, 3, 17, 31, 127, 257, 4096, 65537, 104729, 1234567,
       2147483645, 2147483646, 2147483647]
RAW_SHA256 = "bbf469b9c4de0ce8227ea126fa33510d927f815d879281dbcefd26dabc897827"
SOURCE_SHA256 = "a061fa40aa67ce792672a13ade4aef7ad1eda0fc141d1694e1c0e9b4ec52c507"
CONFIGURATION_SHA256 = "75b13a866bcbbe9a7ad23bdff425153b985c41141c3dd42868bbeb533e25837d"
ORIGINAL_COMMIT = "89f840d58d152a2a9f230cb8d0cdffbd554c8ded"
SOURCE_CITATIONS = [
    dict(path="player.c", lines="363-367", behavior="Four unmodified random running-taunt competitors; R_SKIP is index1."),
    dict(path="player.c", lines="4022-4120",
         behavior="C/plow and stiff-arm precede FIELDX>80/no-TwoPart selection; any enemy under6 yards overrides the result."),
    dict(path="plyrseq.c", lines="6397-6420", behavior="Actual r_skip transition, selected sequence and advancing loop."),
    dict(path="playbook.c", lines="74-83,4900-4907",
         behavior="Da Bomb uses original trips formation and three original routes; public play choice, not a formation edit."),
    dict(path="playbook.c", lines="4990-4996",
         behavior="Original 3 Man Cover defensive choice; the human pursues and tackles rather than vacating coverage."),
    dict(path="player.c", lines="2498-2518",
         behavior="A rapid release/repress of C causes a spin; the controller avoids accidental double-C bursts."),
]
ORIGINAL_FILES = {
    "player.c": "61fb1be76029663294630e8633574ed065496da1ef6329071817e80622870b52",
    "playbook.c": "fa7cd55d2b69ef271075b2c071a3fcc5728811e3b0ff19f55703b6c8e3c2c690",
    "plyrseq.c": "8b7040c7a943f537a8a686a3b79b9c281454672203243599819099177bae880e",
}
TOOLS = (
    "tests/movement-callers/open_space_scenario.py",
    "tests/movement-callers/canonical.py",
    "tests/movement-callers/export_portable_public.py",
    "tests/movement-callers/prepare_public_replay.py",
    "tests/parity/kernel_sources.py",
    "tests/parity/contact_inputs.py",
    "tests/parity/prepare_inputs.py",
    "tests/ci/isolated_mutations.py",
)


def sha(raw):
    return hashlib.sha256(raw).hexdigest()


def manifest(value):
    return sha(json.dumps(value, sort_keys=True, separators=(",", ":")).encode())


def require(value, message):
    if not value:
        raise ValueError(message)


def write_new(path, value):
    with path.open("x", encoding="utf-8") as stream:
        json.dump(value, stream, indent=2)
        stream.write("\n")
        stream.flush()
        os.fsync(stream.fileno())


def inventories():
    production = {p.relative_to(ROOT).as_posix(): p.read_bytes() for p in canonical.production_paths()}
    harness = {name: (ROOT / name).read_bytes() for name in canonical.HARNESS}
    require(len(production) == 103 and len(harness) == 12, "Exact unchanged103-source/twelve-harness closure required.")
    require(manifest({n: sha(b) for n, b in production.items()}) == SOURCE_SHA256,
            "The reviewed production source generation must not change.")
    return production, harness


def inputs(fixture, sounds, profile):
    canonical.fixture_profile(fixture, "contact272")
    result = canonical.fixture_hashes(fixture, sounds)
    result.update(canonical.successor_hashes(profile))
    require(len(result) == 1123, "The exact unchanged enabled282 data closure is required.")
    return result


def identity(policy):
    expected = dict(schema="source-open-space-frozen-first-window/v1", name=NAME, version=VERSION,
                    case=CASE, recordingGeneration=GENERATION, maxHosts=12000, maximumInitialWindows=1,
                    seed=1, providerProfile="source-sound-smoke/raw-vector-v1",
                    rawVector=RAW, sourceManifestSha256=SOURCE_SHA256,
                    configuration=current_configuration(True), configurationSha256=CONFIGURATION_SHA256,
                    worktree=str(ROOT), authority=str(AUTHORITY), startupObject="Program",
                    noRetryOrExtension=True, controllerSource=CONTROLLER)
    require(all(policy.get(k) == v for k, v in expected.items()),
            "Distinct frozen scenario identity, original configuration and one-window authority required.")
    require(manifest(policy["configuration"]) == CONFIGURATION_SHA256 and
            sha(json.dumps(policy["rawVector"], separators=(",", ":")).encode()) == RAW_SHA256,
            "Original configuration and unfiltered provider seals must agree.")


def check_frozen(policy):
    identity(policy)
    production, harness = inventories()
    code = {n: sha(b) for n, b in {**production, **harness}.items()}
    require(code == policy["frozenCodeSha256"] and
            {n: sha(b) for n, b in production.items()} == policy["productionSourceSha256"] and
            manifest({n: sha(b) for n, b in harness.items()}) == policy["harnessManifestSha256"] and
            code[CONTROLLER] == policy["controllerSha256"], "Frozen source/harness/controller changed.")
    require({n: sha((ROOT / n).read_bytes()) for n in TOOLS} == policy["toolSha256"],
            "Frozen authoring tool changed.")
    data = inputs(Path(policy["fixtureManifest"]), Path(policy["soundFunctions"]), Path(policy["profileInputs"]))
    require(data == policy["frozenInputSha256"] and manifest(data) == policy["inputManifestSha256"],
            "Frozen private input closure changed.")
    entrypoint = Path(policy["entrypoint"])
    require(entrypoint == AUTHORITY / "build/bin/Release/net10.0/MovementCallerChecks.dll" and
            not entrypoint.is_symlink() and sha(entrypoint.read_bytes()) == policy["entrypointSha256"] and
            sha(entrypoint.with_suffix(".pdb").read_bytes()) == policy["pdbSha256"],
            "The sealed normal Main PE/PDB must be executed unchanged.")
    for name, pin in policy["failedV6ArtifactsSha256"].items():
        require(sha((ROOT / name).read_bytes()) == pin, "The exhausted failed-v6 boundary must remain intact.")


def environment():
    work = AUTHORITY / "work"
    work.mkdir(parents=True, exist_ok=True)
    (work / "empty-feed").mkdir(exist_ok=True)
    return dict(os.environ, TMPDIR=str(work), TMP=str(work), TEMP=str(work),
                DOTNET_CLI_HOME=str(work / "dotnet-home"), NUGET_PACKAGES=str(work / "nuget"),
                DOTNET_CLI_TELEMETRY_OPTOUT="1", DOTNET_NOLOGO="1",
                MSBUILDDISABLENODEREUSE="1", PYTHONDONTWRITEBYTECODE="1")


def execute(command, label, commands, timeout=240):
    start = time.monotonic()
    process = subprocess.run(command, cwd=ROOT, env=environment(), capture_output=True, text=True, timeout=timeout)
    text = process.stdout + process.stderr
    (AUTHORITY / (label + ".log")).write_text(text)
    commands.append(dict(command=command, exit=process.returncode, log=label + ".log",
                         seconds=round(time.monotonic() - start, 3)))
    return process, text


def freeze(args):
    require(not AUTHORITY.exists(), "Use the sole fixed scenario authority; no replacement/fresh attempt is permitted.")
    production, harness = inventories()
    binding = canonical.observer_binding(production, harness)
    data = inputs(args.fixture_manifest.absolute(), args.sound_functions.absolute(),
                  args.presnap_switch_inputs.absolute())
    previous = ROOT / ".squad/.scratch/quality-current-input-compatibility-20261101/v6-authority"
    receipt = json.loads((previous / "capture/public-window-result.json").read_bytes())
    require(receipt["recordingGeneration"] == "source-defensive-presnap-switch/v6" and
            receipt["initialExecutions"] == 1 and receipt["completedHosts"] == 12000 and
            receipt["rSkipFrames"] == 0 and receipt["positive"] is None,
            "The failed v6 is evidence, never authority for a retry.")
    failed = {p.relative_to(ROOT).as_posix(): sha(p.read_bytes()) for p in (
        previous / "policy.json", previous / "only-public-window-started.json",
        previous / "authoring-result.json", previous / "capture/public-window-result.json")}
    AUTHORITY.mkdir(parents=True)
    snapshot = AUTHORITY / "snapshot"
    for name, raw in {**production, **harness}.items():
        target = snapshot / name
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_bytes(raw)
    commands = []
    build = AUTHORITY / "build"
    common = ["-p:BlitzSourceRoot=" + str(snapshot) + "/",
              "-p:BaseIntermediateOutputPath=" + str(build / "obj") + "/",
              "-p:BaseOutputPath=" + str(build / "bin") + "/",
              "-p:UseSharedCompilation=false", "-p:TreatWarningsAsErrors=true"]
    project = snapshot / "tests/movement-callers/MovementCallerChecks.csproj"
    command = ["dotnet", "build", str(project), "--no-restore", "-t:Rebuild",
               "-c", "Release", "-v:q", "--nologo", *common]
    process, text = execute(command, "sealed-build", commands)
    if process.returncode and "NETSDK1004" in text:
        restored, _ = execute(["dotnet", "restore", str(project), "--source",
                               str(AUTHORITY / "work/empty-feed"), "-v:q", "-p:NuGetAudit=false", *common],
                              "restore-after-actual-missing-assets", commands)
        require(restored.returncode == 0, "Missing-assets restore failed; no gameplay was invoked.")
        process, text = execute(command, "sealed-build-restored", commands)
    require(process.returncode == 0 and "0 Warning(s)" in text and "0 Error(s)" in text,
            "A fresh CSharp73 normal Main zero-diagnostic compile is mandatory before the policy seal.")
    entrypoint = build / "bin/Release/net10.0/MovementCallerChecks.dll"
    checked, text = execute(["dotnet", str(entrypoint), "open-space-policy-checks"], "synthetic-policy-checks", commands)
    require(checked.returncode == 0, "Synthetic policy assertions failed; no gameplay was invoked.")
    checks = json.loads(text)
    require(checks["status"] == "passed" and checks["actualGameConstructions"] == checks["actualGameSteps"] == 0,
            "Pre-seal policy tests must not author or probe any game window.")
    facts = AUTHORITY.parent / "v6-captured-facts.json"
    policy = dict(schema="source-open-space-frozen-first-window/v1", name=NAME, version=VERSION, case=CASE,
                  recordingGeneration=GENERATION, frozenAtUtc=datetime.now(timezone.utc).isoformat(),
                  worktree=str(ROOT), authority=str(AUTHORITY), maxHosts=12000, maximumInitialWindows=1,
                  seed=1, providerProfile="source-sound-smoke/raw-vector-v1", rawVector=RAW,
                  noRetryOrExtension=True, controllerSource=CONTROLLER,
                  controllerSha256=sha(harness[CONTROLLER]),
                  productionSourceSha256={n: sha(b) for n, b in production.items()},
                  frozenCodeSha256={n: sha(b) for n, b in {**production, **harness}.items()},
                  sourceManifestSha256=SOURCE_SHA256, harnessManifestSha256=binding["harness_manifest_sha256"],
                  frozenInputSha256=data, inputManifestSha256=manifest(data),
                  fixtureManifest=str(args.fixture_manifest.absolute()), soundFunctions=str(args.sound_functions.absolute()),
                  profileInputs=str(args.presnap_switch_inputs.absolute()),
                  configuration=current_configuration(True), configurationSha256=CONFIGURATION_SHA256,
                  entrypoint=str(entrypoint), entrypointSha256=sha(entrypoint.read_bytes()),
                  pdbSha256=sha(entrypoint.with_suffix(".pdb").read_bytes()), startupObject="Program",
                  toolSha256={n: sha((ROOT / n).read_bytes()) for n in TOOLS},
                  originalCommit=ORIGINAL_COMMIT, originalSourceFilesSha256=ORIGINAL_FILES,
                  originalSourceAuthentication="Pinned public bytes independently matched the read-only local reference; its checkout HEAD differs.",
                  sourceCitations=SOURCE_CITATIONS, failedV6ArtifactsSha256=failed,
                  failedV6OfflineFactsSha256=sha(facts.read_bytes()),
                  publicProjection=["Phase", "Possession", "ControlledPlayerId", "Ball.CarrierId", "Ball.Position",
                                    "LineOfScrimmage", "Match.PlayKind",
                                    "Players.Id/Team/Position/Velocity/Role/Turbo01"],
                  policyDecisions=[
                      "Current Home offensive/defensive menu book from public possession; Da Bomb trips / 3 Man Cover.",
                      "Wait24 menu hosts; one real raw B at75 pre-snap hosts; public Continue at30 dead-ball hosts.",
                      "QB rolls out; no deadline-forced pass. After12 carrier hosts, raw left/right B only for an outlet beyond LOS+5,4..40 yards ahead, inside20 lateral, current/0.5s predicted enemy gap>=8;30-host repeat guard.",
                      "Every carrier takes the best forward12-yard geometric corridor among seven fixed lanes, considering ALL enemies at3/6/9/12 yards and0.125/0.25/0.375/0.5s public-velocity prediction; not game-state simulation or RNG search.",
                      "Approach>=75 with nearest>=8 and predicted corridor>=6 releases C and continues forward; otherwise burst with normal turbo resource hysteresis .05/.25 and12-host minimum C release to avoid accidental spin.",
                      "Defense actively pursues the actual carrier with0.3s public-velocity lead and raw A inside3 yards; chase loose public ball, escort an uncontrolled teammate.",
                      "Never read clock, score, animation, seed, raw cursor, private model, process or observed random outcomes in the controller.",
                  ],
                  zeroGameplayPolicyChecks=checks, buildCommands=commands,
                  maximumAuthoringInvocations=1, acceptanceRequiresUnchangedFullM1CacheCallerRngPhysicsAndB=True,
                  nativeOrWholeGameQualified=False)
    identity(policy)
    write_new(POLICY, policy)
    check_frozen(policy)
    print(json.dumps(dict(status="prospectively-frozen-no-gameplay", policy=str(POLICY),
                          policySha256=sha(POLICY.read_bytes()), sourceFiles=103, harnessFiles=12,
                          dataFiles=len(data), policyAssertions=checks["assertions"])))
    return 0


def run():
    policy_bytes = POLICY.read_bytes()
    policy = json.loads(policy_bytes)
    require(not (AUTHORITY / "only-public-window-started.json").exists(),
            "The sole public window was already entered; no retry is authorized.")
    check_frozen(policy)
    write_new(AUTHORITY / "only-authoring-invocation.json",
              dict(policySha256=sha(policy_bytes), maximumAuthoringInvocations=1, maxHosts=12000,
                   recordingGeneration=GENERATION, invokedAtUtc=datetime.now(timezone.utc).isoformat()))
    commands = []
    capture = AUTHORITY / "capture"
    command = ["dotnet", policy["entrypoint"], policy["fixtureManifest"], str(capture),
               "open-space-public-policy", policy["soundFunctions"], policy["profileInputs"],
               str(POLICY), "--source-compatibility", "portable"]
    result = dict(schema="source-open-space-only-authoring-window/v1", status="STOP-authoring-fault",
                  policySha256=sha(policy_bytes), actualAuthoringInvocations=1, maximumInitialWindows=1,
                  commands=commands, nativeOrWholeGameQualified=False, exportExecuted=False)
    code = 2
    try:
        process, _ = execute(command, "ONLY-public-window", commands, timeout=900)
        code = process.returncode
        receipt_bytes = (capture / "public-window-result.json").read_bytes()
        receipt = json.loads(receipt_bytes)
        result.update(receipt=receipt, receiptSha256=sha(receipt_bytes))
        require((AUTHORITY / "only-public-window-started.json").is_file(), "Actual pre-construction marker missing.")
        require(receipt["policySha256"] == sha(policy_bytes), "Actual receipt differs from the prospective policy.")
        counts = json.loads((capture / "execution-counts.json").read_bytes())
        result["executionCounts"] = counts
        require(counts["gameConstructors"] == 1 and counts["directAnimationAdvanceCalls"] == 0 and
                counts["hostStepCalls"] == receipt["completedHosts"] and
                counts["unfilteredRawNextValues"] == receipt["rawCursor"] and
                counts["withinPerDriverBudget"] is True, "Actual constructor/host/raw accounting failed.")
        trace = json.loads((capture / "all-shared-raw-draws.json").read_bytes())
        require(len(trace) == receipt["rawCursor"] and
                all(draw["Order"] == i and draw["Raw"] == RAW[i % len(RAW)] for i, draw in enumerate(trace)),
                "The global raw vector must be retained without filtering, rebasing or extra draws.")
        if code == 0:
            require(receipt["schema"] == "source-open-space-public-policy-first-window/v1" and
                    receipt["recordingGeneration"] == GENERATION and receipt["attemptedHosts"] == 12000 and
                    receipt["initialExecutions"] == receipt["maximumInitialWindows"] == 1 and
                    receipt["preSnapSwitchWitnesses"] > 0 and receipt["faults"] == [],
                    "One complete new window with an actual fresh-B witness is mandatory.")
            require_candidate(receipt, json.loads((capture / "R_SKIP-real-poses.json").read_bytes()), True)
            result["status"] = "recorded-new-window-full-qualification-required"
        else:
            require(code in (1, 2), "The bounded authoring process failed outside its recorded gates.")
            result["status"] = "STOP-incomplete-fault-or-unmet-one-window"
    except (OSError, ValueError, KeyError, TypeError, RuntimeError, subprocess.SubprocessError) as error:
        result["error"] = str(error)
        code = 2
    finally:
        try:
            check_frozen(policy)
            result["sourceHarnessToolsInputsAndFailedV6Unchanged"] = True
        except (OSError, ValueError, KeyError) as error:
            result.update(sourceHarnessToolsInputsAndFailedV6Unchanged=False, preservationError=str(error))
            code = 2
        result["exit"] = code
        result["noRetryOrExtension"] = True
        write_new(AUTHORITY / "authoring-result.json", result)
    print(json.dumps(dict(status=result["status"], exit=code, report=str(AUTHORITY / "authoring-result.json"))))
    return code


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest="action", required=True)
    before = commands.add_parser("freeze")
    for name in ("fixture-manifest", "sound-functions", "presnap-switch-inputs"):
        before.add_argument("--" + name, type=Path, required=True)
    commands.add_parser("run")
    args = parser.parse_args(argv)
    try:
        return freeze(args) if args.action == "freeze" else run()
    except (OSError, ValueError, KeyError, TypeError, RuntimeError, subprocess.SubprocessError) as error:
        print(str(error), file=sys.stderr)
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
