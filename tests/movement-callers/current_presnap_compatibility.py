#!/usr/bin/env python3
"""Keep the authentic v5 positive and its separate integrated-current stale boundary."""
import argparse
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import time

import canonical
import check_replay_generations as generations
import prepare_public_replay as replay

ROOT = canonical.ROOT
PROFILE = "original-teams-contact282-c2-m1-presnap-switch"
FACTORY = "REAL SourceC2Composition.CreatePreSnapSwitchSimulation"
STALE_GUARD = ("SOURCE_SIDE_GUARD_V1 possession=1 phase=0 selectingSide=0 "
               "playbook=0 selectingOffense=0 requestingTeam=0")


def classify_v5_stale(code, summary, receipt):
    failure = receipt.get("failure", {})
    if (code != 2 or not isinstance(summary, dict) or summary.get("status") != "error"
            or type(summary.get("assertions")) is not int or summary["assertions"] <= 0
            or receipt.get("profile") != PROFILE or receipt.get("factory") != FACTORY
            or receipt.get("sourceData") != canonical.PRESNAP_DATA_SHA
            or receipt.get("replayFileSha256") != replay.recording(replay.PRESNAP_CURRENT_CASE)["public_sha256"]
            or receipt.get("observed") is not False or receipt.get("historical") is not False
            or receipt.get("gameplayCompatibility") != "Portable"
            or receipt.get("originalPublicVectorRetainedWithoutStateConditionedRegeneration") is not True
            or receipt.get("completedHosts") != 4913 or receipt.get("SourceTick") != 4667
            or receipt.get("rawCursor") != 8643 or receipt.get("rSkipFrames") != 0
            or receipt.get("positive") is not None
            or not isinstance(failure, dict) or failure.get("host") != 4912
            or failure.get("before") != 4666 or failure.get("SourceTick") != 4667
            or not isinstance(failure.get("error"), str)
            or failure["error"].splitlines()[:2] != [generations.SIDE_ERROR, STALE_GUARD]):
        raise ValueError("The exact current-v5 first playbook-side stale boundary is required.")
    return "rejected-integrated-current-v5-stale"


def validate_report(result):
    replay.require(result.get("schema") == "source-integrated-presnap-v5-compatibility/v1"
                   and result.get("status") == "passed-separate-v5-compatibility-boundaries"
                   and result.get("production_harness_unchanged") is True
                   and result.get("inputs_unchanged") is True
                   and set(result.get("runs", {})) == {"historical-v5-positive", "integrated-current-v5-stale"},
                   "Both actual v5 source-generation boundaries and unchanged inputs are mandatory.")
    old, stale = (result["runs"][name] for name in
                  ("historical-v5-positive", "integrated-current-v5-stale"))
    replay.require(old.get("fresh_compile_zero_diagnostics") is True
                   and stale.get("fresh_compile_zero_diagnostics") is True
                   and old.get("source_identity", {}).get("commit") == generations.NEAR_FRAME_ARCHIVE_REF
                   and old["source_identity"].get("source_count") == 78
                   and old["source_identity"].get("source_manifest_sha256") == generations.U2_CURRENT_SOURCE_SHA256
                   and old["source_identity"].get("harness_manifest_sha256") == generations.NEAR_FRAME_HARNESS_SHA256
                   and stale.get("source_identity") == canonical.CURRENT_OBSERVER_GENERATION,
                   "Historical78 and actual integrated103 source/harness identities must stay separate.")
    replay.require(canonical.require_m1_replay(old["exit"], old["summary"], old["receipt"],
                                              "portable", True) == "positive",
                   "The unchanged v5 recording must retain its actual complete historical positive.")
    classify_v5_stale(stale["exit"], stale["summary"], stale["receipt"])
    context = canonical._m1_side_diagnostic_view(stale.get("source_side"),
        "integrated-current-v5-stale", {key: stale["receipt"]["failure"][key]
                                       for key in ("host", "before", "SourceTick", "error")})
    station = dict(station=0, specified=True, action=1, play_specified=True,
                   playbook=0, symbol="play_hail_mary")
    replay.require(context.get("status") == "captured-context" and context.get("issues") == []
                   and context.get("host") == 4912 and context.get("SourceTick") == 4667
                   and context.get("receipt_sha256") == stale.get("receipt_sha256")
                   and context.get("raw_stations") == context.get("applied_stations") == [station]
                   and context.get("current_rules") == dict(offense_side=1),
                   "Actual stale receipt and raw/applied side-guard context must agree.")


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ("fixture-manifest", "sound-functions", "presnap-switch-inputs", "replay-commands", "output"):
        parser.add_argument("--" + name, type=Path, required=True)
    args = parser.parse_args(argv)
    output = args.output.absolute()
    if output.exists() or output.resolve() != output or not output.is_relative_to(ROOT / ".squad/.scratch"):
        parser.error("Use a new real ignored compatibility output; never overwrite a prior receipt.")
    canonical.fixture_profile(args.fixture_manifest, "contact272")
    inputs = canonical.fixture_hashes(args.fixture_manifest, args.sound_functions)
    inputs.update(canonical.successor_hashes(args.presnap_switch_inputs))
    data = args.replay_commands.read_bytes()
    replay.audit_public(data, replay.PRESNAP_CURRENT_CASE)
    inputs[str(args.replay_commands.absolute())] = canonical.sha(data)
    production = {p.relative_to(ROOT).as_posix(): p.read_bytes() for p in canonical.production_paths()}
    harness = {name: (ROOT / name).read_bytes() for name in canonical.HARNESS}
    binding = canonical.observer_binding(production, harness)
    archived, identity = generations.near_frame_snapshot()
    output.mkdir(parents=True)
    work = output / "work"; work.mkdir(); (work / "empty-feed").mkdir()
    env = dict(os.environ, TMPDIR=str(work), TMP=str(work), TEMP=str(work),
               DOTNET_CLI_TELEMETRY_OPTOUT="1", DOTNET_NOLOGO="1",
               MSBUILDDISABLENODEREUSE="1", PYTHONDONTWRITEBYTECODE="1")
    result = dict(schema="source-integrated-presnap-v5-compatibility/v1", status="failed",
                  commands=[], runs={}, source_sha256={n: canonical.sha(b) for n, b in production.items()},
                  harness_sha256={n: canonical.sha(b) for n, b in harness.items()}, inputs_sha256=inputs,
                  whole_current_game_or_native_accepted=False)

    def command(values, label, timeout=900):
        started = time.monotonic()
        process = subprocess.run(values, cwd=ROOT, env=env, capture_output=True, text=True, timeout=timeout)
        text = process.stdout + process.stderr
        (output / (label + ".log")).write_text(text)
        result["commands"].append(dict(command=values, exit=process.returncode, log=label + ".log",
                                       seconds=round(time.monotonic() - started, 3)))
        return process, text

    def execute(label, files, source_identity):
        copy = output / "copies" / label
        for name, raw in files.items():
            path = copy / name; path.parent.mkdir(parents=True, exist_ok=True); path.write_bytes(raw)
        project = copy / "tests/movement-callers/MovementCallerChecks.csproj"
        build = output / "build" / label
        common = ["-p:BlitzSourceRoot=" + str(copy) + "/",
                  "-p:BaseIntermediateOutputPath=" + str(build / "obj") + "/",
                  "-p:BaseOutputPath=" + str(build / "bin") + "/",
                  "-p:UseSharedCompilation=false", "-p:TreatWarningsAsErrors=true"]
        compile_command = ["dotnet", "build", str(project), "--no-restore", "-t:Rebuild",
                           "-c", "Release", "-v:q", "--nologo", *common]
        process, text = command(compile_command, label + "-build")
        if process.returncode and "NETSDK1004" in text:
            restored, _ = command(["dotnet", "restore", str(project), "--source", str(work / "empty-feed"),
                                   "-v:q", "-p:NuGetAudit=false", *common], label + "-restore")
            if restored.returncode:
                raise RuntimeError("Compatibility SDK restore failed after actual missing assets.")
            process, text = command(compile_command, label + "-build-restored")
        if process.returncode or "0 Warning(s)" not in text or "0 Error(s)" not in text:
            raise RuntimeError("Each actual compatibility source view must freshly compile with zero diagnostics.")
        destination = output / label
        process, _ = command(["dotnet", str(build / "bin/Release/net10.0/MovementCallerChecks.dll"),
                              str(args.fixture_manifest.absolute()), str(destination), "presnap-complete",
                              str(args.sound_functions.absolute()), str(args.presnap_switch_inputs.absolute()),
                              str(args.replay_commands.absolute()), "--source-compatibility", "portable"], label)
        rows = [json.loads(line) for line in process.stdout.splitlines() if line.startswith('{"schema":')]
        receipt_bytes = (destination / "m1-replay-result.json").read_bytes()
        row = dict(exit=process.returncode, summary=rows[-1] if len(rows) == 1 else None,
                   receipt=json.loads(receipt_bytes), receipt_sha256=canonical.sha(receipt_bytes),
                   source_identity=source_identity, fresh_compile_zero_diagnostics=True)
        result["runs"][label] = row
        if label == "historical-v5-positive":
            replay.require(canonical.require_m1_replay(process.returncode, row["summary"], row["receipt"],
                                                      "portable", True) == "positive",
                           "Actual historical-v5 positive failed; never replace it with current code.")
        else:
            classify_v5_stale(process.returncode, row["summary"], row["receipt"])
            row["source_side"] = canonical._m1_side_evidence(destination, label, receipt_bytes, dict(
                observed=False, profile=PROFILE, sourceData=canonical.PRESNAP_DATA_SHA, factory=FACTORY,
                replayFileSha256=inputs[str(args.replay_commands.absolute())]))
        shutil.rmtree(copy)

    code = 1
    try:
        execute("historical-v5-positive", archived, identity)
        execute("integrated-current-v5-stale", {**production, **harness}, binding)
        result["status"] = "passed-separate-v5-compatibility-boundaries"
        code = 0
    except (OSError, ValueError, RuntimeError, subprocess.SubprocessError) as error:
        result["error"] = str(error)
        print(str(error), file=sys.stderr)
    finally:
        result["production_harness_unchanged"] = all((ROOT / n).read_bytes() == raw
                                                      for n, raw in {**production, **harness}.items())
        result["inputs_unchanged"] = all(canonical.sha(Path(n).read_bytes()) == pin for n, pin in inputs.items())
        if not result["production_harness_unchanged"] or not result["inputs_unchanged"]:
            result["status"] = "inconclusive-input-changed"; code = 1
        copies = output / "copies"
        if copies.exists(): shutil.rmtree(copies)
        if code == 0:
            try: validate_report(result)
            except (ValueError, KeyError, TypeError) as error:
                result.update(status="failed", error=str(error)); code = 1
        (output / "result.json").write_text(json.dumps(result, indent=2) + "\n")
    print(json.dumps(dict(status=result["status"],report=str(output / "result.json"))))
    return code


if __name__ == "__main__":
    raise SystemExit(main())
