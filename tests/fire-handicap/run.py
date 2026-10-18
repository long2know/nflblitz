#!/usr/bin/env python3
"""Original-C/real GameSimulation fire-handicap cases and compiled causal controls."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys

from oracle import build, PINS

SUITE = Path(__file__).resolve().parent
ROOT = SUITE.parents[1]
sys.path.insert(0, str(ROOT / "tests/parity"))
from prepare_inputs import fixture_hashes
from contact_inputs import fixture_profile
from kernel_sources import production_paths, canonical_hashes

SOURCE = "Assets/Scripts/Gameplay/"
FIRE = SOURCE + "GameSimulation.FireHandicap.cs"
CALLER = SOURCE + "GameSimulation.Source.cs"
EXPECTED_CASES = {
    "carrier": 48, "carrier-predicates": 18, "air-pending": 16, "possession": 40,
    "result": 80, "result-kinds": 48, "result-control-owner": 4, "counter-boundary": 4,
    "handicap-scores": 160, "handicap-powerups": 32, "handicap-repetition": 120,
    "handicap-team-data": 93, "handicap-unclamped-boundary": 9, "handicap-drones-lose": 1, "integration": 5,
}
EXPECTED_ASSERTIONS = {"C1": 10614, "old-nine": 10623, "M1": 10623}
FH01_CASES = {"FH01-incomplete-fourth-down": 2}
FH01_ASSERTIONS = {"C1": 48, "old-nine": 54, "M1": 54}
MUTATIONS = (
    ("offense-fourth-not-third", CALLER, "Model.SourceConsecutiveCompletions[side] > 2",
     "Model.SourceConsecutiveCompletions[side] > 3", "fire", "carrier: original-C field[0]"),
    ("completion-control19", CALLER, "Model.BallKind == SourceBallKind.Pass && held == 18",
     "Model.BallKind == SourceBallKind.Pass && held == 19", "fire", "carrier: original-C field[2]"),
    ("defense-third-not-second", FIRE, "Model.SourceConsecutiveLosses[defense] == 2",
     "Model.SourceConsecutiveLosses[defense] == 3", "fire", "result: original-C field[1]"),
    ("yardage-after-los-update", FIRE, "resolution.FieldEnd < input.State.LineOfScrimmage",
     "resolution.FieldEnd < resolution.State.LineOfScrimmage", "fire", "result: original-C field[0]"),
    ("final-side-not-classified-side", FIRE, "int side = resolution.Classification.OffenseSide;",
     "int side = resolution.State.OffenseSide;", "fire", "result: original-C field[0]"),
    ("forced-down-turnover-reset", CALLER, "CreditSourcePossession(actor, false);",
     "CreditSourcePossession(actor);", "integration", "integration: original-C field[0]"),
    ("pending-landing-live-only", CALLER,
     "private void SourceCarrierControl(ActorState actor)\n        {\n            if (Model.CarrierId != actor.Id)",
     "private void SourceCarrierControl(ActorState actor)\n        {\n            if (Model.CarrierId != actor.Id || Model.Phase != GamePhase.LiveBall)",
     "fire", "air-pending: original-C field[0]"),
    ("completion-cleared-at-result", CALLER, "ApplySourceFirePlayEnd(endingInput, resolution);",
     "ApplySourceFirePlayEnd(endingInput, resolution);\n"
     "            if (Model.SourceForwardCatcher == -1) Model.SourceConsecutiveCompletions[(int)Model.Possession] = 0;",
     "integration", "integration: original pending third completion lands after result, before dismissal counter reset"),
    ("history-not-recorded", FIRE, "history[0] = Model.SourceOffensePlay;",
     "/* isolated causal control: do not record selected human offense */", "handicap",
     "handicap-scores: original-C handicap field[4]"),
    ("actor-neutral-scalar", SOURCE + "SourcePlayerDriver.cs",
     "actor.SourceHandicap = model.SourceTeamHandicap[(int)actor.Team];",
     "actor.SourceHandicap = model.SourceConfiguration.Difficulty;", "integration",
     "integration: actor table index comes from its team's dynamic handicap"),
    ("AI-neutral-scalar", FIRE, "new SourceAiParameters(sourcePlaybook, levels[0], levels[1])",
     "new SourceAiParameters(sourcePlaybook, 5, 5)", "handicap",
     "handicap-scores: AI reads actual dynamic team levels"),
    ("contact-ignores-produced-fire", SOURCE + "SourceContactDriver.cs",
     "if (model.SourceTeamFire[victimSide]) fumble = 0;",
     "if (model.SourceTeamFire[victimSide] && model.SourceTeamFire.Length == 0) fumble = 0;",
     "integration", "integration: actual contact caller consumes produced offensive fire to suppress fumble"),
    ("quick-kick-neutral-row", CALLER, "int level = Model.SourceTeamHandicap[plan.ReceivingSide];",
     "int level = 5;", "integration",
     "integration: actual quick-kick original source bits field[0] use computed receiving handicap"),
    ("invented-consumer-clamp", FIRE, "for (int i = 0; i < levels.Length; i++)",
     "levels[0] = Math.Max(0, Math.Min(11, levels[0]));\n"
     "            levels[1] = Math.Max(0, Math.Min(11, levels[1]));\n"
     "            for (int i = 0; i < levels.Length; i++)",
     "handicap", "handicap-team-data: undefined original consumer index is not clamped"),
)
FH01_MUTATIONS = (
    ("incomplete-fourth-down-classified-reset-owner", FIRE,
     "int resetSide = result == SourcePlayResult.Incomplete ? input.State.InitialOffenseSide : side;",
     "int resetSide = side;", "FH01",
     "FH01 receiving fire/state survives incomplete original-offense restoration"),
)


def sha(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def current():
    return {p.relative_to(ROOT).as_posix(): sha(p) for p in production_paths()}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ("source-root", "handicap-header", "fixture-manifest", "sound-functions",
                 "old-c2-inputs", "c2-m1-inputs", "output"):
        parser.add_argument("--" + name, type=Path, required=True)
    args = parser.parse_args()
    output = args.output.absolute()
    if (output.exists() or ".." in args.output.parts or not output.is_relative_to(ROOT / ".squad/.scratch")
            or any(p.is_symlink() for p in (output, *output.parents))):
        parser.error("Choose a new regular ignored child of this worktree's .squad/.scratch.")
    fixture_profile(args.fixture_manifest, "contact272")
    output.mkdir(parents=True)
    work = output / "work"
    work.mkdir()
    (work / "empty-feed").mkdir()
    environment = dict(os.environ, TMPDIR=str(work), TMP=str(work), TEMP=str(work),
                       PYTHONDONTWRITEBYTECODE="1", DOTNET_CLI_TELEMETRY_OPTOUT="1",
                       DOTNET_NOLOGO="1", MSBUILDDISABLENODEREUSE="1")
    before = current()
    snapshot = {name: (ROOT / name).read_bytes() for name in before}
    if before != {n: hashlib.sha256(raw).hexdigest() for n, raw in snapshot.items()}:
        raise RuntimeError("Production changed during exact source capture.")
    inputs = fixture_hashes(args.fixture_manifest, args.sound_functions)
    inputs.update(canonical_hashes(args.old_c2_inputs))
    inputs.update(canonical_hashes(args.c2_m1_inputs, "contact281-c2-m1"))
    source_before = {n: sha(args.source_root / n) for n in PINS}
    source_before["include/handicap.h"] = sha(args.handicap_header)
    harness_paths = [SUITE / name for name in ("Program.cs", "driver.c", "oracle.py", "run.py", "FireHandicapChecks.csproj")]
    harness_paths += [ROOT / name for name in ("tests/parity/FullKernelInputs.cs", "tests/parity/FullKernelSources.props",
                                               "tests/audio/vm/SourceSoundVmSources.props")]
    harness_before = {str(p.relative_to(ROOT)): sha(p) for p in harness_paths}
    result = dict(schema="source-fire-handicap-run/v1", status="failed", commands=[], command_results=[], controls=[],
                  production_sha256_before=before, actual_input_sha256_before=inputs,
                  original_sha256_before=source_before, full_game_native_CI_or_DSP_qualified=False,
                  harness_sha256_before=harness_before, no_public_M1_replays=True,
                  feature_inventory_semantics={
                      "fire_reference_cases": 258, "handicap_reference_cases": 415, "integrated_reference_calls": 10,
                      "refinement": "Added16 actual loss cases (field20), retaining field29 whose original rounding can reach LOS30; +368 assertions/+16 C calls. Added actual already-lit reentry/C comparison before contact consumer, +17 assertions/+1 C call, so its compiled control also consumes the independent reference before failing. No old suite count changed."},
                  retained_feature_inventory={
                      "original_C_calls_per_profile": 683, "assertions": EXPECTED_ASSERTIONS,
                      "cases": EXPECTED_CASES, "compiled_controls": len(MUTATIONS)},
                  FH01_revision_inventory={
                      "additional_original_C_calls_per_profile": 2, "additional_assertions": FH01_ASSERTIONS,
                      "cases": FH01_CASES, "additional_compiled_controls": len(FH01_MUTATIONS),
                      "actual_factories": ["SourceContactComposition.CreateSimulation",
                          "SourceC2Composition.CreateSimulation", "SourceC2Composition.CreateM1Simulation"],
                      "result_callers": "EndSourcePlay; actual C2 next-source-tick CommitSourceC2PlayEnd",
                      "native_vectors": "Both mirrored defined fourth-down incomplete review inputs; no expected output rebase"})

    def execute_command(command, label, timeout=180):
        result["commands"].append(command)
        process = subprocess.run(command, capture_output=True, text=True, cwd=ROOT, env=environment, timeout=timeout)
        text = process.stdout + process.stderr
        (output / (label + ".log")).write_text(text)
        result["command_results"].append(dict(label=label, exit=process.returncode,
                                               log=str(output / (label + ".log"))))
        return process.returncode, text

    def compile_source(label, source):
        build_root = output / "build" / label
        common = ["-p:BlitzSourceRoot=" + str(source) + "/",
                  "-p:BaseIntermediateOutputPath=" + str(build_root / "obj") + "/",
                  "-p:BaseOutputPath=" + str(build_root / "bin") + "/"]
        command = ["dotnet", "build", str(SUITE / "FireHandicapChecks.csproj"), "--no-restore",
                   "--configuration", "Release", "--verbosity", "quiet", *common]
        code, text = execute_command(command, label + "-compile")
        if code and "NETSDK1004" in text:
            restore = ["dotnet", "restore", str(SUITE / "FireHandicapChecks.csproj"),
                       "--source", str(work / "empty-feed"), "--verbosity", "quiet", "-p:NuGetAudit=false", *common]
            restored, restore_text = execute_command(restore, label + "-offline-assets")
            if restored:
                raise RuntimeError("Missing SDK assets could not be restored offline: " + restore_text[-4000:])
            code, text = execute_command(command, label + "-compile")
        if code or "0 Warning(s)" not in text or "0 Error(s)" not in text:
            raise RuntimeError("Every positive/control must compile with zero diagnostics: " + label + "\n" + text[-4000:])
        return build_root / "bin/Release/net10.0/FireHandicapChecks.dll"

    def execute_checks(binary, native, label, profile, section="all"):
        command = ["dotnet", str(binary), str(args.fixture_manifest.absolute()), str(args.sound_functions.absolute()),
                   str(native), str(output / label), section,
                   str(args.old_c2_inputs.absolute()) if profile == "old-nine" else "-",
                   str(args.c2_m1_inputs.absolute()) if profile == "M1" else "-"]
        code, text = execute_command(command, label)
        rows = [json.loads(line) for line in text.splitlines() if line.startswith('{"schema":')]
        if len(rows) != 1:
            raise RuntimeError("Expected one actual check result: " + label + "\n" + text[-4000:])
        return code, rows[0]

    def require_pass(code, summary, profile):
        if (code != 0 or summary.get("status") != "passed" or summary.get("original_exit_code") != 0
                or summary.get("assertions") != EXPECTED_ASSERTIONS[profile] + FH01_ASSERTIONS[profile]
                or summary.get("reference_calls") != 683 + sum(FH01_CASES.values())
                or summary.get("cases") != dict(EXPECTED_CASES, **FH01_CASES)
                or summary.get("undefined_original_consumer_cases_rejected") != 5
                or summary.get("original_stderr") != "" or len(summary.get("observations", [])) != 3):
            raise RuntimeError("Exact original field/caller inventory required: " + json.dumps(summary))

    try:
        native, provenance = build(args.source_root, output / "original-C", args.handicap_header)
        result["proper_original_C"] = provenance
        baseline = compile_source("plain", ROOT)
        for profile in ("C1", "old-nine", "M1"):
            code, summary = execute_checks(baseline, native, "baseline-" + profile, profile)
            require_pass(code, summary, profile)
            result["baseline_" + profile] = summary
            print("PASS " + profile + ": " + str(summary["assertions"]) + " assertions /685 original-C calls"
                  + " (683 retained +2 FH01)", flush=True)
        for identity, relative, old, new, section, expected in MUTATIONS + FH01_MUTATIONS:
            source = output / "copies" / identity
            for name, raw in snapshot.items():
                path = source / name
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_bytes(raw)
            target = source / relative
            text = target.read_text()
            if text.count(old) != 1:
                raise RuntimeError("Mutation anchor is not unique: " + identity)
            target.write_text(text.replace(old, new))
            mutated_hash = sha(target)
            mutant = compile_source(identity, source)
            code, summary = execute_checks(mutant, native, identity, "M1", section)
            if (code != 1 or summary.get("status") != "failed"
                    or summary.get("assertions", 0) <= 0 or summary.get("reference_calls", 0) <= 0
                    or expected not in summary.get("failed_assertion", "") or "error" in summary):
                raise RuntimeError("Compiled causal control did not fail its actual named field: "
                                   + identity + "\n" + json.dumps(summary))
            result["controls"].append(dict(identity=identity, source=relative, mutated_sha256=mutated_hash,
                                           zero_warnings_errors=True, assertion=summary["failed_assertion"],
                                           assertions=summary["assertions"], original_C_calls=summary["reference_calls"]))
            print("PASS compiled control " + identity, flush=True)
        code, summary = execute_checks(baseline, native, "final-M1", "M1")
        require_pass(code, summary, "M1")
        result["final_M1"] = summary
        result["status"] = "passed"
    except Exception as error:
        result["error"] = str(error)
        print("FAIL: " + str(error)[-6000:], flush=True)
    finally:
        result["production_sha256_after"] = current()
        after_inputs = fixture_hashes(args.fixture_manifest, args.sound_functions)
        after_inputs.update(canonical_hashes(args.old_c2_inputs))
        after_inputs.update(canonical_hashes(args.c2_m1_inputs, "contact281-c2-m1"))
        result["actual_input_sha256_after"] = after_inputs
        source_after = {n: sha(args.source_root / n) for n in PINS}
        source_after["include/handicap.h"] = sha(args.handicap_header)
        result["original_sha256_after"] = source_after
        result["harness_sha256_after"] = {str(p.relative_to(ROOT)): sha(p) for p in harness_paths}
        result["sources_inputs_originals_unchanged"] = (
            before == result["production_sha256_after"] and inputs == after_inputs and source_before == source_after
            and harness_before == result["harness_sha256_after"])
        if not result["sources_inputs_originals_unchanged"]:
            result["status"] = "inconclusive-inputs-changed"
        if (output / "copies").exists():
            shutil.rmtree(output / "copies")
        if (output / "build").exists():
            shutil.rmtree(output / "build")
        shutil.rmtree(work)
        result["own_source_build_work_copies_removed"] = True
        (output / "result.json").write_text(json.dumps(result, indent=2) + "\n")
    return 0 if result["status"] == "passed" else 1


if __name__ == "__main__":
    raise SystemExit(main())
