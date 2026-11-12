#!/usr/bin/python3.12
"""Bounded direct compilation/execution of the real public GameSimulation boundary."""

import argparse
import datetime
import hashlib
import json
import os
from pathlib import Path
import subprocess
import sys
import time


ROOT = Path(__file__).absolute().parents[2]
BASE = ROOT / ".squad/.scratch/source-following-active-p1"
PREP = BASE / "human-possession-time-source-preparation"
ARTIFACTS = ROOT / "tests/source-runtime-integration/.artifacts"
WORK = BASE / "human-source-runtime-integration"
METADATA = BASE / "metadata/source-runtime-integration"
NEW_FILES = (
    "tests/source-runtime-integration/Program.cs",
    "tests/source-runtime-integration/run.py",
    "tests/source-runtime-integration/README.md",
)
PYTHON = "/usr/bin/python3.12"
DOTNET = "/home/long2know/.dotnet/dotnet"
CSC = "/home/long2know/.dotnet/sdk/10.0.100/Roslyn/bincore/csc.dll"
LIMITS = {
    "fixture_or_harness_corrections": 3,
    "direct_csc_compiles": 12,
    "managed_test_launches": 10,
    "compiler_timeout_seconds": 180,
    "managed_timeout_seconds": 120,
    "simulation_steps_per_scenario": 20000,
    "simulation_instances_per_launch": 20,
}
PREPARATION_PINS = {
    "source-closure.json": (64656, "dd4ca58fe9b413003a70271309eaf875c045f24b06dfb5327de3a9676ed45e89"),
    "execution-inputs.json": (45268, "800295438dbd0d787a185a3fadb9ec5cd9f109442d34a53b1031854067e1edbf"),
    "tool-inputs.json": (524, "4ca33ccd83a123844d8851947907c8631d075ca0188c15836743beb8b42e2f24"),
    "reference-inputs.json": (77089, "73826de6ae906013619081b38d6fc4419f55b388e502d6fcec35fbe932d39a8e"),
}
RECEIPT_PINS = {
    "human-possession-time-source": (5039, "3550682b16ebb09090546bd8adab811fb3870b53f929f0423ca8d5c474fa559c"),
    "human-digital-correction-1": (4086, "9e7b03c4b2632c26eb542dcf14361dc40125c5fcb8b67fbcb511215f37422fa1"),
    "human-onside-compiled": (2822, "c945dbcc8c075813eab58d323f5f86344a9e0b99822a85db5b4c28ce8dfe553e"),
}
EXPECTED = {
    "profile_attempted": "OriginalRelease57Hz",
    "compatibility_attempted": "StrictSource",
    "base-two-station.admission.exception_type": "System.InvalidOperationException",
    "base-four-station.admission.exception_type": "System.InvalidOperationException",
    "strict_roster_message": (
        "Source57 handicapping requires the existing strict SourceTeamRosterLibrary "
        "with original teaminfo.drone_base and diff_settings; no scalar Difficulty fallback is used."
    ),
    "honest-synthetic-roster-provenance.message":
        "Roster provenance must identify the known, content-verified original source pin.",
    "other_named_rejections": {
        "runner.no_arguments": 0,
        "animation-only-overload.exception_type": "System.InvalidOperationException",
        "animation-only-overload.message": (
            "Original source M1 requires actual runtime-safe SourceMovementRuntimeData "
            "and explicit field/powerup SourceMovementSettings. No tuned legacy movement fallback is used."
        ),
        "portable-without-m1-bootstrap.exception_type": "System.NotSupportedException",
        "portable-without-m1-bootstrap.message":
            "Portable gameplay requires the explicit actual M1 source bootstrap.",
        "legacy-configuration-not-source-fallback.exception_type": "System.ArgumentException",
        "legacy-configuration-not-source-fallback.message":
            "The source constructor requires the explicit 57 Hz profile. (Parameter 'configuration')",
        "honest-synthetic-roster-provenance.exception_type": "Blitz.AssetImport.SourceRosterException",
    },
    "expected_observation_inventory": {
        "constructor_attempts": 5, "constructed_source_instances": 0,
        "simulation_steps": 0, "named_cases": 6, "named_asserted_fields": 13,
    },
    "if_admitted_initial": {
        "Tick": 0, "SourceTick": 0, "SourceTicksPerSecond": 57,
        "TimeOfPossession.HomeSeconds": 0, "TimeOfPossession.AwaySeconds": 0,
        "ClockActive": False, "PackedGameTime": 131072, "Players.Count": 14,
    },
    "if_admitted_first_host": {
        "Tick": 1, "SourceTick": 0, "TimeOfPossession.HomeSeconds": 0,
        "TimeOfPossession.AwaySeconds": 0, "oldSnapshot.Tick": 0,
    },
    "if_admitted_restart": {
        "Tick": 0, "SourceTick": 0, "TimeOfPossession.HomeSeconds": 0,
        "TimeOfPossession.AwaySeconds": 0, "oldSnapshot.Tick": 1,
    },
    "rationale": [
        "SimulationState.cs: new state has Tick/SourceTick zero and explicit immutable ledger (0,0).",
        "GameSimulation.cs: source constructors converge on Restart -> InitializeSource.",
        "GameSimulation.Source.cs: PrepareSourceKickoff -> LineUpSourceActors calls UpdateSourceHandicaps unconditionally.",
        "GameSimulation.FireHandicap.cs: UpdateSourceHandicaps first requires the sealed SourceTeamRosterLibrary, BEFORE human/CPU branching.",
        "SourceTeamRosterLibrary.cs: only public creation is Read; ReadProvenance requires verified fixed original source pins and a fixed 22-input catalog.",
        "GameSimulation.cs: first host adds 57, below the 60 threshold; Publish replaces the snapshot without a source wake.",
        "GameSnapshot.cs: source-mode projection passes the model ledger; Restart creates a fresh SimulationState.",
        "SourceSoundVm.cs: every SourceStep requires the fixed original sound-function payload. This downstream limit is STATIC ONLY here.",
    ],
    "active_clock_expectation": {
        "status": "UNRUN",
        "reason": "No legitimate synthetic source construction exists through the currently inspected public roster admission. No observed output or helper supplies an oracle.",
    },
}
FIXTURE = {
    "origin": "explicitly-synthetic-public-DTO-fixture/v1",
    "seed": 37,
    "random": "SourceInjectedRandom with constant raw zero and ExternalUnverified provenance; NOT original RNG.",
    "roster": "31 x 16 actual public SourceRosterProfile/SourceTeamRoster DTOs behind an explicitly synthetic ISourceTeamRoster.",
    "configuration": "Existing OriginalRelease57Hz, StrictSource; both teams human; TwoStation and FourStation, FirstPossession Home/Away.",
    "geometry": "Actual SourceCatchGeometry with fifteen finite synthetic zero pivots, NOT authentic geometry.",
    "playbook": "Actual public SourcePlaybook/formation/route DTOs; three opening kickoff keys, synthetic origin.",
    "directions": "Synthetic linear 257-entry quarter table; 49-way numeric input mapping from current port source.",
    "movement": "Dimension-valid public runtime DTOs; empty constant/selector maps are NOT claimed live-M1 complete. Roster guard executes first.",
    "animations": "Explicit synthetic ISourceAnimationLibrary rejects asset requests. No original bytes, original claims, or fabricated clip metadata.",
    "negative_loader_document": "Loader protocol schema with repository/commit/tree/manifest labelled synthetic and content_pin_verified=false; expected REJECTION only.",
    "not_a_valid_original_export": True,
    "not_an_authentic_asset_fixture": True,
    "no_source_helper_oracles": True,
}


def now():
    return datetime.datetime.now(datetime.timezone.utc).isoformat()


def identity(path):
    path = Path(path)
    data = path.read_bytes()
    return {"path": str(path), "bytes": len(data), "sha256": hashlib.sha256(data).hexdigest()}


def verify(path, pin):
    actual = identity(path)
    expected = (pin["bytes"], pin["sha256"]) if isinstance(pin, dict) else tuple(pin)
    if (actual["bytes"], actual["sha256"]) != expected:
        raise RuntimeError("Frozen input changed: " + str(path))
    return actual


def write_json(path, value):
    data = (json.dumps(value, indent=2, sort_keys=True) + "\n").encode()
    with Path(path).open("xb") as output:
        output.write(data)
    return identity(path)


def capture(source, target):
    target.parent.mkdir(parents=True, exist_ok=True)
    with target.open("xb") as output:
        output.write(source.read_bytes())
    if identity(source)["sha256"] != identity(target)["sha256"]:
        raise RuntimeError("Source capture changed while being read: " + str(source))


def counters():
    counts = {"direct_csc_compiles": 0, "managed_test_launches": 0}
    for path in (ARTIFACTS / "attempts").glob("*/*.invocation.json"):
        kind = json.loads(path.read_text())["kind"]
        counts["direct_csc_compiles" if kind == "csc" else "managed_test_launches"] += 1
    return counts


def environment(root):
    locations = {name: root / name for name in ("home", "tmp", "cli-home")}
    for path in locations.values():
        path.mkdir(parents=True, exist_ok=False)
    return {
        "HOME": str(locations["home"]), "TMPDIR": str(locations["tmp"]),
        "TMP": str(locations["tmp"]), "TEMP": str(locations["tmp"]),
        "DOTNET_CLI_HOME": str(locations["cli-home"]),
        "PATH": "/usr/bin:/bin", "LANG": "C.UTF-8", "LC_ALL": "C.UTF-8", "TZ": "UTC",
        "DOTNET_ROOT": "/home/long2know/.dotnet",
        "DOTNET_MULTILEVEL_LOOKUP": "0", "DOTNET_ROLL_FORWARD": "Disable",
        "DOTNET_NOLOGO": "1", "DOTNET_SKIP_FIRST_TIME_EXPERIENCE": "1",
        "DOTNET_CLI_TELEMETRY_OPTOUT": "1", "DOTNET_EnableDiagnostics": "0",
        "COMPlus_EnableDiagnostics": "0", "DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE": "1",
    }


def load_inputs():
    preparations = {
        name: verify(PREP / name, pin) for name, pin in PREPARATION_PINS.items()
    }
    tools = json.loads((PREP / "tool-inputs.json").read_text())
    if {key: value["path"] for key, value in tools.items()} != {
        "python": PYTHON, "dotnet": DOTNET, "compiler": CSC
    }:
        raise RuntimeError("Unexpected tool paths.")
    for pin in tools.values():
        verify(pin["path"], pin)
    references = json.loads((PREP / "reference-inputs.json").read_text())["profiles"]
    directories = {
        "netstandard2.1": (121, "/dev/shm/ap.bweV08/t18/pkg/netstandard.library.ref/2.1.0/ref/netstandard2.1"),
        "net10.0": (167, "/home/long2know/.dotnet/packs/Microsoft.NETCore.App.Ref/10.0.0/ref/net10.0"),
    }
    for profile, (count, directory) in directories.items():
        pins = references[profile]
        if pins["count"] != count or len(pins["files"]) != count or pins["directory"] != directory:
            raise RuntimeError("Unexpected reference profile: " + profile)
        if len({p["path"] for p in pins["files"]}) != count:
            raise RuntimeError("Duplicate reference.")
        for pin in pins["files"]:
            if str(Path(pin["path"]).parent) != directory:
                raise RuntimeError("Reference outside exact admitted directory.")
            verify(pin["path"], pin)
    receipt_files = {}
    receipts = {}
    for component, pin in RECEIPT_PINS.items():
        path = BASE / "metadata" / component / "parent-scoped-acceptance.json"
        receipt_files[component] = verify(path, pin)
        receipts[component] = json.loads(path.read_text())
    frozen_metadata = identity(PREP / "frozen-inputs.json")
    frozen = json.loads((PREP / "frozen-inputs.json").read_text())["i1AndOnside12"]
    frozen = frozen + receipts["human-possession-time-source"]["currentEightFilePins"]
    if len(frozen) != 20 or len({p["path"] for p in frozen}) != 20:
        raise RuntimeError("The exact twenty accepted component pins were not resolved.")
    accepted_i1 = receipts["human-digital-correction-1"]["currentFiveFilePins"]
    by_path = {p["path"]: p for p in frozen}
    for pin in accepted_i1:
        if by_path[pin["path"]] != pin:
            raise RuntimeError("Accepted digital pin disagrees with frozen preparation.")
    for pin in frozen:
        verify(ROOT / pin["path"], pin)
    closure = json.loads((PREP / "source-closure.json").read_text())["whole70"]
    if len(closure) != 70 or len({p["path"] for p in closure}) != 70:
        raise RuntimeError("Expected actual whole seventy-source closure.")
    sources = [{"path": p["path"], "bytes": p["bytes"], "sha256": p["sha256"]} for p in closure]
    for relative in (
        "Assets/Scripts/Gameplay/SourceCatchGeometry.cs",
        "Assets/Scripts/AssetImport/SourceActorPoseSampler.cs",
    ):
        extra_id = identity(ROOT / relative)
        sources.append({"path": relative, "bytes": extra_id["bytes"], "sha256": extra_id["sha256"]})
    for pin in sources:
        if not pin["path"].startswith("Assets/Scripts/") or not pin["path"].endswith(".cs"):
            raise RuntimeError("Non-production source in model closure.")
        verify(ROOT / pin["path"], pin)
    return {
        "preparations": preparations, "tools": tools, "references": references,
        "receipts": receipt_files, "frozen_metadata": frozen_metadata, "frozen20": frozen,
        "sources": sources,
        "additional_source_purpose": "Actual public SourceCatchGeometry plus its required SourceActorPoseSampler dependency; no fake geometry/Unity class.",
        "admission": identity(METADATA / "implementation-admission.json"),
    }


def invoke(attempt, name, kind, argv, env, bindings, inputs, shell_id):
    consumed = counters()
    key = "direct_csc_compiles" if kind == "csc" else "managed_test_launches"
    if consumed[key] >= LIMITS[key]:
        raise RuntimeError("Execution budget exhausted: " + key)
    for pin in inputs["tools"].values():
        verify(pin["path"], pin)
    for profile in inputs["references"].values():
        for pin in profile["files"]:
            verify(pin["path"], pin)
    for binding in bindings:
        verify(binding["path"], binding)
    reserved = dict(consumed)
    reserved[key] += 1
    timeout = LIMITS["compiler_timeout_seconds" if kind == "csc" else "managed_timeout_seconds"]
    record = {
        "kind": kind, "name": name, "recorded_before_launch": now(), "argv": argv,
        "cwd": str(attempt), "environment": env, "timeout_seconds": timeout,
        "outer_shell_id_supplied": shell_id, "consumed_before": consumed,
        "reserved_after": reserved, "limits": LIMITS, "inputs": bindings,
        "reference_manifest": inputs["preparations"]["reference-inputs.json"],
        "tool_manifest": inputs["preparations"]["tool-inputs.json"],
        "expectations": identity(attempt / "expectations.json"),
        "fixture_manifest": identity(attempt / "fixture-manifest.json"),
        "source_manifest": identity(attempt / "source-manifest.json"),
        "no_refunds_for_failed_or_unstarted_reserved_invocations": True,
    }
    invocation = write_json(attempt / (name + ".invocation.json"), record)
    stdout = attempt / (name + ".stdout.log")
    stderr = attempt / (name + ".stderr.log")
    start = time.monotonic()
    timed_out = False
    launch_error = None
    code = None
    with stdout.open("xb") as out, stderr.open("xb") as err:
        try:
            completed = subprocess.run(argv, cwd=attempt, env=env, stdin=subprocess.DEVNULL,
                                       stdout=out, stderr=err, timeout=timeout, check=False)
            code = completed.returncode
        except subprocess.TimeoutExpired:
            timed_out = True
        except OSError as error:
            launch_error = str(error)
    completion = {
        "name": name, "kind": kind, "exit_code": code, "timeout": timed_out,
        "launch_error": launch_error, "elapsed_seconds": round(time.monotonic() - start, 6),
        "completed_at": now(), "invocation": invocation,
        "stdout": identity(stdout), "stderr": identity(stderr), "consumed_after": counters(),
    }
    write_json(attempt / (name + ".completion.json"), completion)
    return completion


def response(path, lines):
    with path.open("x") as output:
        output.write("\n".join(lines) + "\n")
    return identity(path)


def quoted(path):
    return '"' + str(path) + '"'


def preserved(inputs):
    current = []
    for pin in inputs["frozen20"]:
        current.append(verify(ROOT / pin["path"], pin))
    for pin in inputs["sources"]:
        verify(ROOT / pin["path"], pin)
    for pin in inputs["preparations"].values():
        verify(pin["path"], pin)
    for pin in inputs["receipts"].values():
        verify(pin["path"], pin)
    verify(inputs["frozen_metadata"]["path"], inputs["frozen_metadata"])
    verify(inputs["admission"]["path"], inputs["admission"])
    return {
        "all_twenty_current_pins_unchanged": True, "twenty": current,
        "all_model_production_source_inputs_unchanged": True,
        "model_source_input_count": len(inputs["sources"]),
        "parent_admission_preparations_and_three_receipts_unchanged": True,
        "source_reverse_call_sha_preserved_by_whole_file_identity":
            "726f513c0549d8bf028734c7d7c449dfbff430f8f7c244053b4cf8fdc03b8091",
        "reverse_call_sha_independently_recomputed": False,
        "no_existing_production_test_project_files_written": True,
        "no_git_queries_index_or_publication_commands": True,
    }


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--shell-id", required=True)
    args = parser.parse_args()
    if str(Path(sys.executable)) != PYTHON or not (sys.flags.isolated and sys.flags.no_site and sys.dont_write_bytecode):
        raise RuntimeError("Run only /usr/bin/python3.12 -I -S -B.")
    if Path.cwd() != ROOT:
        raise RuntimeError("Run from the supplied worktree, not another checkout.")
    ARTIFACTS.mkdir(parents=True, exist_ok=True)
    (ARTIFACTS / "attempts").mkdir(exist_ok=True)
    WORK.mkdir(parents=True, exist_ok=True)
    paths = sorted((ARTIFACTS / "attempts").iterdir())
    number = len(paths) + 1
    if number > 1 + LIMITS["fixture_or_harness_corrections"]:
        raise RuntimeError("Fixture/harness correction budget exhausted.")
    attempt = ARTIFACTS / "attempts" / ("%03d" % number)
    attempt.mkdir(exist_ok=False)
    write_json(attempt / "start.json", {
        "at": now(), "attempt": number, "corrections_consumed": number - 1,
        "shell_id_supplied": args.shell_id, "controller_argv": sys.argv,
        "controller_environment": dict(os.environ), "limits": LIMITS,
        "counters_before": counters(),
    })
    inputs = None
    commands = []
    result = None
    error = None
    status = "ERROR"
    exit_code = 2
    try:
        inputs = load_inputs()
        write_json(attempt / "input-manifest.json", inputs)
        for relative in NEW_FILES:
            capture(ROOT / relative, attempt / "test" / Path(relative).name)
        captured = []
        for pin in inputs["sources"]:
            target = attempt / "sources" / pin["path"]
            capture(ROOT / pin["path"], target)
            captured.append(identity(target))
        write_json(attempt / "source-manifest.json", {
            "origin": "Actual whole production sources, freshly copied from admitted worktree",
            "sources": captured, "additional_source_purpose": inputs["additional_source_purpose"],
            "private_internal_access_from_test": False,
        })
        write_json(attempt / "expectations.json", EXPECTED)
        write_json(attempt / "fixture-manifest.json", {
            **FIXTURE, "generator": identity(attempt / "test/Program.cs"),
            "original_asset_file_reads": 0,
        })
        environment_root = attempt / "environment"
        environment_root.mkdir()
        env = environment(environment_root)
        model = attempt / "SourceRuntimeModel.dll"
        runner = attempt / "SourceRuntimeChecks.dll"
        model_rsp = response(attempt / "model.rsp", [
            "/nologo", "/langversion:7.3", "/warn:4", "/warnaserror+", "/deterministic+",
            "/optimize+", "/nostdlib+", "/target:library", "/out:" + quoted(model),
            *["/reference:" + quoted(p["path"]) for p in inputs["references"]["netstandard2.1"]["files"]],
            *[quoted(p["path"]) for p in captured],
        ])
        model_compile = invoke(attempt, "model-compile", "csc",
            [DOTNET, "exec", CSC, "/noconfig", "@" + str(attempt / "model.rsp")], env,
            captured + [model_rsp, identity(attempt / "test/run.py")], inputs, args.shell_id)
        commands.append(model_compile)
        if model_compile["exit_code"] != 0 or model_compile["timeout"]:
            raise RuntimeError("Actual separate model compilation failed; see immutable diagnostics.")
        runner_source = identity(attempt / "test/Program.cs")
        runner_rsp = response(attempt / "runner.rsp", [
            "/nologo", "/langversion:7.3", "/warn:4", "/warnaserror+", "/deterministic+",
            "/optimize+", "/nostdlib+", "/target:exe", "/out:" + quoted(runner),
            *["/reference:" + quoted(p["path"]) for p in inputs["references"]["net10.0"]["files"]],
            "/reference:" + quoted(model), quoted(runner_source["path"]),
        ])
        runner_compile = invoke(attempt, "runner-compile", "csc",
            [DOTNET, "exec", CSC, "/noconfig", "@" + str(attempt / "runner.rsp")], env,
            [identity(model), runner_source, runner_rsp], inputs, args.shell_id)
        commands.append(runner_compile)
        if runner_compile["exit_code"] != 0 or runner_compile["timeout"]:
            raise RuntimeError("External public-API runner compilation failed; see immutable diagnostics.")
        runtime = write_json(attempt / "SourceRuntimeChecks.runtimeconfig.json", {
            "runtimeOptions": {
                "tfm": "net10.0", "framework": {"name": "Microsoft.NETCore.App", "version": "10.0.0"},
                "rollForward": "Disable",
            }
        })
        launch = invoke(attempt, "public-constructor-checks", "managed",
            [DOTNET, "exec", "--runtimeconfig", str(attempt / "SourceRuntimeChecks.runtimeconfig.json"), str(runner)],
            env, [identity(model), identity(runner), runtime, runner_source], inputs, args.shell_id)
        commands.append(launch)
        if launch["timeout"] or launch["launch_error"]:
            raise RuntimeError("Managed launch did not finish normally.")
        result = json.loads((attempt / "public-constructor-checks.stdout.log").read_text())
        write_json(attempt / "observations.json", result)
        if result["status"] != "BLOCKED_PUBLIC_ADMISSION" or launch["exit_code"] != 3:
            raise RuntimeError("Unexpected assertion/runtime result; do not count as a source-runtime pass.")
        if result["constructed_source_instances"] != 0 or result["simulation_steps"] != 0:
            raise RuntimeError("Evidence classification no longer matches the declared constructor-boundary scope.")
        if result["constructor_attempts"] != 5 or len(result["cases"]) != 6:
            raise RuntimeError("Expected exact two boundary attempts and four named public rejection cases.")
        if len(result["fields"]) != 13:
            raise RuntimeError("Named asserted-field inventory changed.")
        if any(field["status"] != "PASS" for field in result["fields"]):
            raise RuntimeError("A named public admission assertion failed.")
        status = result["status"]
        exit_code = 3
    except Exception as failure:
        error = type(failure).__name__ + ": " + str(failure)
    finally:
        preservation = None
        if inputs is not None:
            try:
                preservation = preserved(inputs)
            except Exception as failure:
                error = (error + "; " if error else "") + "PRESERVATION: " + str(failure)
                status = "ERROR"
                exit_code = 2
        summary = {
            "schema": "source-runtime-integration-attempt/v1", "attempt": number,
            "status": status, "controller_expected_exit_code": exit_code,
            "outer_shell_id_supplied": args.shell_id, "error": error, "commands": commands,
            "counters_consumed": counters(), "fixture_or_harness_corrections_consumed": number - 1,
            "limits": LIMITS, "preservation": preservation,
            "observations": identity(attempt / "observations.json") if result is not None else None,
            "source_instances_constructed": None if result is None else result["constructed_source_instances"],
            "source_steps_executed": None if result is None else result["simulation_steps"],
        }
        write_json(attempt / "summary.json", summary)
        write_json(WORK / ("attempt-%03d.json" % number), summary)
        if inputs is not None:
            write_handoff(inputs, attempt, number, summary, result)
        print(json.dumps({
            "status": status, "attempt": number, "error": error,
            "compiles": counters()["direct_csc_compiles"],
            "managed_launches": counters()["managed_test_launches"],
            "summary": str(attempt / "summary.json"),
            "handoff": str(METADATA / "handoff.json") if inputs is not None else None,
        }, sort_keys=True))
    return exit_code


def write_handoff(inputs, attempt, number, summary, result):
    path = METADATA / "handoff.json"
    prior_handoff = None
    if path.exists():
        capture(path, attempt / "prior-handoff.json")
        prior_handoff = identity(attempt / "prior-handoff.json")
    attempts = []
    for directory in sorted((ARTIFACTS / "attempts").iterdir()):
        item = directory / "summary.json"
        entry = {"attempt_directory": str(directory), "summary": identity(item) if item.exists() else None}
        if item.exists():
            old = json.loads(item.read_text())
            entry.update({key: old[key] for key in (
                "attempt", "status", "outer_shell_id_supplied", "controller_expected_exit_code",
                "error", "counters_consumed", "fixture_or_harness_corrections_consumed", "commands",
            )})
        attempts.append(entry)
    document = {
        "schema": "source-public-runtime-integration-handoff/v1",
        "author": "Quality", "future_independent_material_reviewer": "Lead",
        "status": summary["status"], "execution_timestamp_utc": now(),
        "scope": "New real production-library/public-constructor boundary execution. NO admitted source simulation, Step, Restart, clock, snapshot or mutation qualification.",
        "changed_files": [identity(ROOT / path) for path in NEW_FILES],
        "worktree": str(ROOT), "state_backend": "local", "team_root": "/data/source/nflblitz/.squad",
        "branch_and_head_as_supplied_not_queried": {
            "branch": "stlong/source-next-default-audibles-45f",
            "head": "45f045612ab52611edea31a782696478cde1aca6",
        },
        "admission": inputs["admission"], "admitted_source_profile": None,
        "prior_handoff_preserved_before_refresh": prior_handoff,
        "attempted_existing_profile": "OriginalRelease57Hz / StrictSource",
        "public_construction": {
            "api": "new GameSimulation(ISourceAnimationLibrary, ISourceActorGeometry, SourcePlaybook, SourceMatchConfiguration, Func<int,ISourceRandom>, ...)",
            "origin": FIXTURE,
            "result": (
                "Both dimension-valid synthetic DTO base-profile configurations rejected at the unconditional concrete strict-roster guard."
                if result is not None else "UNRUN: runner did not return normal observations; see attempt diagnostics."
            ),
            "first_guard_path": [
                "GameSimulation.cs source constructor -> Restart(seed)",
                "GameSimulation.Source.cs InitializeSource -> PrepareSourceKickoff(true,false)",
                "GameSimulation.Source.cs LineUpSourceActors -> UpdateSourceHandicaps",
                "GameSimulation.FireHandicap.cs sourceRosters as SourceTeamRosterLibrary, null -> InvalidOperationException",
            ],
            "legitimate_alternatives_executed": [] if result is None else [
                "OriginalRelease57Hz / StrictSource / TwoStation / both human / first Home",
                "OriginalRelease57Hz / StrictSource / FourStation / both human / first Away",
                "Public animation-only source overload -> mandatory movement DTO rejection",
                "Existing Portable compatibility without original M1 bootstrap -> explicit admission rejection",
                "Legacy source configuration -> explicit profile rejection, never fallback",
                "SourceTeamRosterLibrary.Read with honestly synthetic provenance and matching SYNTHETIC content hash -> original provenance rejection",
            ],
            "fixture_completeness_limit": FIXTURE["movement"],
            "why_not_fix_the_fixture_to_match": "The sealed concrete roster has no public constructor/factory except Read. Its loader requires fixed verified original provenance and fixed original 22-input catalog. Synthesizing matching authenticity claims is prohibited. Empty live-M1 maps are not the observed rejection and are not claimed sufficient for later execution.",
            "additional_static_not_executed_limit": "Even after legitimate roster admission, SourceStep unconditionally requires the exact original SourceAudioFunctionData payload via BindSourceSoundVmData. No mute/base-profile bypass exists.",
        },
        "source_closure": {
            "count": len(inputs["sources"]), "original_closure_count": 70,
            "manifest": identity(attempt / "source-manifest.json") if (attempt / "source-manifest.json").exists() else None,
            "extra": inputs["sources"][70:], "separate_library": True,
            "production_sources": inputs["sources"],
            "source_inputs": inputs["preparations"]["execution-inputs.json"],
        },
        "fixture_manifest": identity(attempt / "fixture-manifest.json") if (attempt / "fixture-manifest.json").exists() else None,
        "literal_expectations_before_launch": identity(attempt / "expectations.json") if (attempt / "expectations.json").exists() else None,
        "observations": result, "attempts": attempts, "latest_attempt": summary,
        "inputs_and_reference_tool_pins": identity(attempt / "input-manifest.json"),
        "binary_identities": [identity(p) for p in (attempt / "SourceRuntimeModel.dll", attempt / "SourceRuntimeChecks.dll") if p.exists()],
        "meaningful_negative_controls": {
            "model-ledger-initializer-nonzero": "UNRUN: no admitted clean initialization/publication scenario passed",
            "snapshot-ledger-argument-dropped": "UNRUN: no admitted clean snapshot scenario passed",
            "clock-consumer-credit-omitted": "UNRUN: no active clock-consumer scenario executed",
            "mutations_compiled_or_claimed_killed": 0,
        },
        "preservation": summary["preservation"],
        "precise_dependency_not_asserted_production_defect": {
            "path": "Assets/Scripts/Gameplay/GameSimulation.FireHandicap.cs",
            "member": "UpdateSourceHandicaps",
            "boundary": "Unconditional sealed SourceTeamRosterLibrary type requirement before human-only branch.",
            "loader": "Assets/Scripts/AssetImport/SourceTeamRosterLibrary.cs: Read and ReadProvenance",
            "follow_up_owner": "Lead/Unity, only under a separately released legitimate data/admission scope",
            "frozen_production_edited": False,
        },
        "limits": [
            "Compilation is C#7.3/Werror, NS2.1 model and net10 runner compatibility; not a Unity/SDK/native build.",
            "Four passing rejection cases plus two exact constructor-boundary observations are NOT successful source runtime cases.",
            "No source object returned, snapshot published, source input consumed, Step or Restart test completed; all requested ledger/clock observations remain UNRUN.",
            "No original/private assets, headers, licenses, original provenance claims, or private helper/oracle bodies used.",
            "No material approval/rejection, fidelity/whole100 claim, publication, git/index operation, other process control or nested agents.",
        ],
        "material_review_performed": False, "full_native_and_whole100": "OPEN",
    }
    raw = (json.dumps(document, indent=2, sort_keys=True) + "\n").encode()
    if len(raw) > 65536:
        raise RuntimeError("Handoff exceeds admitted 65536-byte bound.")
    with (attempt / "handoff.json").open("xb") as output:
        output.write(raw)
    with path.open("wb") as output:
        output.write(raw)


if __name__ == "__main__":
    sys.exit(main())
