#!/usr/bin/env python3
"""Author one source-bound public recording; historical vectors are never repaired."""
import argparse
from datetime import datetime, timezone
import json
import os
from pathlib import Path
import subprocess
import sys

import canonical
import export_portable_public as exporter
import prepare_public_replay as public

sys.path.insert(0, str(public.ROOT / "tests/parity"))
from kernel_sources import canonical_hashes, production_paths
from prepare_inputs import fixture_hashes

ROOT = public.ROOT
CASE = "portable-cabinet-wide-pass-clearance-source-bound-v5-12000"
GENERATION = "source-bound-public-input/v5"
CONTROLLER = "tests/movement-callers/PortablePublicPolicy.cs"
CONTROLLER_SHA256 = "e123e5db745acb3d4952dc0f56a2379eea36515b90bcd5c375dc769b797b330d"
RAW = [0, 1, 2, 3, 17, 31, 127, 257, 4096, 65537, 104729, 1234567,
       2147483645, 2147483646, 2147483647]
require = public.require
sha = public.sha


def manifest(pins):
    return sha(json.dumps(pins, sort_keys=True, separators=(",", ":")).encode())


def current_sources():
    production = {p.relative_to(ROOT).as_posix(): p.read_bytes() for p in production_paths(ROOT)}
    harness = {name: (ROOT / name).read_bytes() for name in canonical.HARNESS}
    require(sha(harness[CONTROLLER]) == CONTROLLER_SHA256,
            "The original public controller must not be tuned for a new witness.")
    return production, harness


def source_pins(files):
    return {name: sha(raw) for name, raw in files.items()}

def author_output(path, session_scope=None):
    output = Path(path).absolute()
    scoped = False
    if session_scope is not None:
        scope = Path(session_scope).absolute()
        scoped = (output == scope and scope.parent.parent.name == "files"
                  and "session-state" in scope.parts and scope.parent.name.startswith("io-recovery-"))
    require(output.resolve() == output and not output.exists()
            and (output.is_relative_to(ROOT / ".squad/.scratch") or scoped),
            "Use a new ignored worktree directory or the exact explicitly assigned session/files recovery scope.")
    return output


def validate_source_binding(policy, production, harness):
    require(policy["productionSourceSha256"] == source_pins(production)
            and policy["sourceManifestSha256"] == manifest(source_pins(production))
            and policy["harnessSha256"] == source_pins(harness)
            and policy["harnessManifestSha256"] == manifest(source_pins(harness))
            and policy["frozenCodeSha256"] == source_pins({**production, **harness}),
            "The authoring source generation changed; an old vector cannot be relabeled current.")

def validate_inputs(policy):
    inputs = fixture_hashes(Path(policy["fixtureManifest"]), Path(policy["soundFunctions"]))
    inputs.update(canonical_hashes(Path(policy["profileInputs"]), "contact281-c2-m1"))
    require(inputs == policy["frozenInputSha256"]
            and manifest(inputs) == policy["inputManifestSha256"],
            "The exact original input/profile closure changed during authoring.")

def validate_tool(policy):
    require(all(sha((ROOT / name).read_bytes()) == pin
                for name, pin in policy["toolSha256"].items()),
            "The sealed generation/validation tool changed during authoring.")


def validate_menu_snapshot(command, visible, host):
    require(visible["Possession"] in (0, 1)
            and command["Action"] == 0 and not command["Source"]["IsSpecified"]
            and len(command["StationInputs"]["Stations"]) == 1,
            "The original public controller must retain its single typed Home cabinet packet.")
    for station in command["StationInputs"]["Stations"]:
        require(station["StationId"] == 0,
                "The declared single Home cabinet captain is station0.")
        source = station["Command"]
        if not source["IsSpecified"]:
            continue
        if source["Action"] == 1:
            play = source["Play"]
            require(visible["Phase"] == 0 and play["IsSpecified"]
                    and play["Playbook"] == (0 if visible["Possession"] == 0 else 1),
                    "Wrong-side public menu choice at capture host " + str(host)
                    + "; never repair, discard, or reassign this command.")
        elif source["Action"] == 6:
            require(visible["Phase"] == 3,
                    "Continue must be authored from the matching public DeadBall snapshot.")
        else:
            raise ValueError("The frozen public controller emitted an undeclared menu action.")
        if source["OffenseFlipped"] is not None:
            require(visible["Possession"] == 0,
                    "Only the actual Home offense may author off_flip.")


def validate_capture(capture, observations, receipt):
    require(sha(capture) == receipt["publicInputSha256"],
            "Captured commands differ from the actual normal-Main author receipt.")
    raw_rows = capture.decode("ascii").splitlines()
    require(len(raw_rows) == public.HOSTS, "Exactly12000 captured public inputs are required.")
    projected, menus, boundary = [], 0, []
    count = 0
    for host, line in enumerate(observations):
        require(host < public.HOSTS, "Unexpected extra public pre-Step observation.")
        observed = public.decode(line)
        row = public.decode(raw_rows[host])
        require(observed["host"] == row["host"] == host
                and observed["command"] == row["command"],
                "Each immutable command needs its SAME ordered public pre-Step snapshot.")
        validate_menu_snapshot(row["command"], observed["visible"], host)
        menus += sum(s["Command"]["IsSpecified"]
                     and s["Command"]["Action"] == 1
                     for s in row["command"]["StationInputs"]["Stations"])
        if host in (4155, 4156, 4157, 4178, 4179, 4180, 4181):
            boundary.append(dict(host=host, visible=observed["visible"],
                                 command=row["command"],
                                 beforeSourceTick=observed["beforeSourceTick"],
                                 sourceTick=observed["SourceTick"]))
        projected.append(exporter.project_row(row, host))
        count += 1
    require(count == public.HOSTS and menus > 0,
            "Every authored host, including real menu selections, needs a public snapshot.")
    return public.encode(projected), dict(hosts=count, menuChoices=menus,
                                          samePublicPreStepSnapshot=True,
                                          station=0, team="Home", boundary=boundary)


def run(command, output, label):
    work = output / "work"
    work.mkdir(exist_ok=True)
    env = dict(os.environ, TMPDIR=str(work), TMP=str(work), TEMP=str(work),
               DOTNET_CLI_HOME=str(work / "cli"), DOTNET_SKIP_FIRST_TIME_EXPERIENCE="1",
               DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE="true",
               DOTNET_CLI_TELEMETRY_OPTOUT="1", DOTNET_NOLOGO="1",
               MSBUILDDISABLENODEREUSE="1", PYTHONDONTWRITEBYTECODE="1")
    result = subprocess.run(command, cwd=ROOT, env=env, capture_output=True, text=True,
                            timeout=900 if label == "one-public-window" else 240)
    text = result.stdout + result.stderr
    (output / (label + ".log")).write_text(text)
    return result.returncode, text


def freeze(args):
    output = author_output(args.output, args.session_output_scope)
    production, harness = current_sources()
    inputs = fixture_hashes(args.fixture_manifest, args.sound_functions)
    inputs.update(canonical_hashes(args.c2_m1_inputs, "contact281-c2-m1"))
    output.mkdir(parents=True)
    build = output / "build"
    project = ROOT / "tests/movement-callers/MovementCallerChecks.csproj"
    common = ["-p:BlitzSourceRoot=" + str(ROOT) + "/",
              "-p:BaseIntermediateOutputPath=" + str(build / "obj") + "/",
              "-p:BaseOutputPath=" + str(build / "bin") + "/",
              "-p:ProjectAssetsFile=" + str(args.project_assets.absolute()),
              "-p:UseSharedCompilation=false", "-p:TreatWarningsAsErrors=true"]
    command = ["dotnet", "build", str(project),
               "--no-restore", "-c", "Release", "-v:q", *common]
    status, text = run(command, output, "sealed-build")
    require(status == 0 and "0 Warning(s)" in text and "0 Error(s)" in text,
            "A fresh CSharp73 normal Main zero-diagnostic compile is required before authoring.")
    entrypoint = build / "bin/Release/net10.0/MovementCallerChecks.dll"
    status, text = run(["dotnet", str(entrypoint),
                        "portable-public-policy-checks"], output, "policy-checks")
    require(status == 0, "Frozen public policy assertions failed before any authoring window.")
    configuration = exporter.current_configuration(False)
    policy = dict(schema="source-bound-frozen-public-authoring/v1",
                  name="portable-cabinet-wide-pass-clearance", version="approved-portable-public-policy/v1",
                  case=CASE, recordingGeneration=GENERATION,
                  frozenAtUtc=datetime.now(timezone.utc).isoformat(), worktree=str(ROOT),
                  sourceRoot=str(ROOT), compileFromReadOnlySourceWithBeforeAfterSeal=True,
                  maxHosts=12000, maximumInitialWindows=1, seed=1, rawVector=RAW,
                  providerProfile="source-sound-smoke/raw-vector-v1",
                  controllerSha256=CONTROLLER_SHA256, productionSourceSha256=source_pins(production),
                  sourceManifestSha256=manifest(source_pins(production)),
                  harnessSha256=source_pins(harness), harnessManifestSha256=manifest(source_pins(harness)),
                  frozenCodeSha256=source_pins({**production, **harness}),
                  frozenInputSha256=inputs, inputManifestSha256=manifest(inputs),
                  fixtureManifest=str(args.fixture_manifest.absolute()),
                  soundFunctions=str(args.sound_functions.absolute()),
                  profileInputs=str(args.c2_m1_inputs.absolute()),
                  configuration=configuration, configurationSha256=manifest(configuration),
                  entrypoint=str(entrypoint), entrypointSha256=sha(entrypoint.read_bytes()),
                  pdbSha256=sha(entrypoint.with_suffix(".pdb").read_bytes()),
                  sameApprovedConfiguration=exporter.CONFIGURATION,
                  sourceHandicapPolicy=exporter.HANDICAP,
                  toolSha256={str(path.relative_to(ROOT)): sha(path.read_bytes())
                              for path in (Path(__file__).resolve(), Path(exporter.__file__).resolve())})
    validate_source_binding(policy, *current_sources())
    validate_inputs(policy)
    validate_tool(policy)
    public.write_new_or_verify(output / "policy.json",
                               (json.dumps(policy, indent=2) + "\n").encode())
    return output / "policy.json"


def author(policy_path):
    raw = policy_path.read_bytes()
    policy = public.decode(raw)
    output = policy_path.parent
    require(not (output / "only-public-window-started.json").exists()
            and not (output / "capture").exists(),
            "This source generation already started its one public window; never retry or extend it.")
    validate_source_binding(policy, *current_sources())
    validate_inputs(policy)
    validate_tool(policy)
    entrypoint = Path(policy["entrypoint"])
    require(sha(entrypoint.read_bytes()) == policy["entrypointSha256"]
            and sha(entrypoint.with_suffix(".pdb").read_bytes()) == policy["pdbSha256"],
            "The sealed normal Main PE/PDB changed before execution.")
    command = ["dotnet", str(entrypoint),
               policy["fixtureManifest"], str(output / "capture"), "portable-public-policy",
               policy["soundFunctions"], policy["profileInputs"], str(policy_path),
               "--source-compatibility", "portable"]
    status, text = run(command, output, "one-public-window")
    validate_source_binding(policy, *current_sources())
    validate_inputs(policy)
    validate_tool(policy)
    require(status == 0, "The single live public window failed or lacks the unchanged R_SKIP predicate; see one-public-window.log.")
    receipt = public.decode((output / "capture/public-window-result.json").read_bytes())
    require(receipt["policySha256"] == sha(raw)
            and receipt["schema"] == "portable-public-policy-first-window/v1"
            and receipt["name"] == policy["name"] and receipt["version"] == policy["version"]
            and receipt["maximumInitialWindows"] == receipt["initialExecutions"] == 1
            and receipt["attemptedHosts"] == 12000 and receipt["rawVector"] == RAW
            and receipt["rawCursor"] <= 32768,
            "The actual author receipt changed its seal, first window, provider vector, or draw budget.")
    poses = public.decode((output / "capture/R_SKIP-real-poses.json").read_bytes())
    exporter.require_candidate(receipt, poses)
    capture = (output / "capture/captured-public-commands.jsonl").read_bytes()
    with (output / "capture/public-window-observations.jsonl").open() as observations:
        projected, binding = validate_capture(capture, observations, receipt)
    public.write_new_or_verify(output / (CASE + ".jsonl"), projected)
    public.write_new_or_verify(output / "public-snapshot-binding.json",
                               (json.dumps(binding, indent=2) + "\n").encode())
    return projected, policy, receipt


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ("fixture-manifest", "sound-functions", "c2-m1-inputs", "project-assets", "output"):
        parser.add_argument("--" + name, type=Path, required=True)
    phases = parser.add_mutually_exclusive_group()
    phases.add_argument("--freeze-only", action="store_true",
                        help="Compile/seal only; do not construct or execute a gameplay window.")
    phases.add_argument("--frozen-policy", type=Path,
                        help="Execute the one window from this unchanged source/PE/input seal.")
    parser.add_argument("--session-output-scope", type=Path,
                        help="Exact new session/files/io-recovery child explicitly assigned to this author.")
    args = parser.parse_args(argv)
    try:
        policy_path = args.frozen_policy.absolute() if args.frozen_policy else freeze(args)
        if args.frozen_policy:
            policy = public.decode(policy_path.read_bytes())
            require(policy_path.parent == args.output.absolute()
                    and policy["fixtureManifest"] == str(args.fixture_manifest.absolute())
                    and policy["soundFunctions"] == str(args.sound_functions.absolute())
                    and policy["profileInputs"] == str(args.c2_m1_inputs.absolute()),
                    "The resumed source seal must retain its same authority and explicit input paths.")
        if args.freeze_only:
            print(json.dumps(dict(status="compiled-and-sealed-no-gameplay-window",
                                  policy=str(policy_path), actualAuthoringWindows=0)))
            return 0
        projected, policy, receipt = author(policy_path)
        packed = public.compress(projected)
        recipe = {
            **public.SOURCE_TIMER_EXPECTED_RECIPE,
            "case": CASE, "recording_generation": GENERATION,
            "source_manifest_sha256": policy["sourceManifestSha256"],
            "source_count": len(policy["productionSourceSha256"]),
            "harness_manifest_sha256": policy["harnessManifestSha256"],
            "harness_count": len(policy["harnessSha256"]),
            "stable_profile": "original-teams-contact281-c2-m1",
            "factory": "SourceC2Composition.CreateM1Simulation", "station_layout": 2,
            "public_snapshot_binding": "same-pre-Step-snapshot/station0-Home-captain/playbook-side",
            "public_snapshot_binding_sha256": sha(
                (args.output / "public-snapshot-binding.json").read_bytes()),
            "author_policy_sha256": sha(policy_path.read_bytes()),
            "author_entrypoint_sha256": policy["entrypointSha256"],
            "author_input_manifest_sha256": policy["inputManifestSha256"],
            "raw_vector_sha256": sha(json.dumps(RAW, separators=(",", ":")).encode()),
            "retained_negative_case": public.CORE_CURRENT_CASE,
            "retained_negative_public_sha256":
                "5a147d8a2482c6abfc5d9d143c8a62a9b1aeadaa4ad2bcc64f6f0d2dc0c194fa",
            "jsonl": dict(file=CASE + ".jsonl", sha256=sha(projected), bytes=len(projected)),
            "gzip": dict(file=CASE + ".jsonl.gz", sha256=sha(packed), bytes=len(packed)),
        }
        for suffix, data in ((".jsonl.gz", packed),
                             (".recipe.json", (json.dumps(recipe, indent=2) + "\n").encode())):
            public.write_new_or_verify(args.output / (CASE + suffix), data)
        print(json.dumps(dict(status="authored-source-bound-not-yet-full-qualified",
                              case=CASE, generation=GENERATION,
                              source=policy["sourceManifestSha256"],
                              public=sha(projected), recipe=recipe, positive=receipt["positive"])))
        return 0
    except (OSError, ValueError, RuntimeError, subprocess.SubprocessError) as error:
        print("Source-bound authoring stopped: " + str(error), file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
