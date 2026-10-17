#!/usr/bin/env python3
"""Run actual M1 caller/VM/cache regressions, original post-VM C physics, and strict isolated mutants."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import uuid

from compare_physics import compare, PLAYER_BLOB

SUITE = Path(__file__).resolve().parent
ROOT = SUITE.parents[1]
ARTIFACTS = SUITE / ".artifacts"
sys.path.insert(0, str(ROOT / "tests/parity"))
from prepare_inputs import fixture_hashes, MOVEMENT_ANIMATION_SHA, MOVEMENT_SELECTED_SHA

MUTANTS = (
    ("shared-before-script", "SourceMovementIntegration.cs",
     "actor.SourceSpinCount = Model.SourceSpinCount[(int)actor.Team] = state.TeamSpinCount;",
     "actor.SourceSpinCount = state.TeamSpinCount;",
     "actual third-spin script predicate sees shared count increment BEFORE immediate VM callback"),
    ("captured-control-block", "SourceMovementIntegration.cs",
     "RunSourceEvasion(actor, 1, eligible)",
     "RunSourceEvasion(actor, 1, SourceMovement.ModeAllowsRun(CaptureSourceMovement(actor), MovementContext(actor)))",
     "captured eligibility survives real StandardRun->BeginTwoPart->UNINT stiff-arm->hurdle ordering"),
    ("weapon-return", "SourceMovementIntegration.cs", "if (after.ReturnFromMode)",
     "if (after.ReturnFromMode && actor.SourceControllerStation >= 0)",
     "actual weapon control boundary transfer produces ReturnFromMode early return"),
    ("camera-cache-not-world", "SourceMovementIntegration.cs",
     "new SourceEvasionLeapInput(Model.SourceCameraJoints[target, instruction.B],",
     "new SourceEvasionLeapInput(sourceGeometry.WorldJoint(GameSnapshot.SourcePlayerView(victim, Model.SourceTick), instruction.B),",
     "actual Core LEAPATT input is preceding real camera joint/current transform and exact original9 operands"),
    ("actual-leap-operands", "SourceMovementIntegration.cs",
     "new SourceVector3(instruction.X, instruction.Y, instruction.Z), instruction.C,",
     "new SourceVector3(instruction.X, instruction.Y, instruction.Z), instruction.C + 1,",
     "actual Core LEAPATT input is preceding real camera joint/current transform and exact original9 operands"),
    ("immediate-general-run", "SourceAnimationState.cs", "| SourceAnimationSignal.GeneralRun",
     "/* isolated mutation: defer GeneralRun */",
     "actual script completes through one immediate GeneralRun hurdle_anim"),
    ("caller-original-units", "SourceMovementIntegration.cs",
     "new SourceVector3(state.X, state.Y, state.Z)",
     "new SourceVector3(state.X, state.Y / SourceTiming.SourceUnitsPerYard, state.Z)",
     "actual post-VM Core physics applies original source-unit Y/gravity once, without yards conversion"),
    ("team-powerup-mask", "SourceMovementIntegration.cs",
     "(sourceMovementSettings.FastTurboTeamMask & (1 << (int)actor.Team)) != 0",
     "sourceMovementSettings.FastTurboTeamMask != 0",
     "actual pup_fastturborun selects executing TEAM bit"),
)


def source_files():
    return [path for folder in ("Contracts", "Gameplay", "AssetImport")
            for path in sorted((ROOT / "Assets/Scripts" / folder).glob("*.cs"))] + [
                ROOT / "Assets/Scripts/Presentation/SourceCinematicModel.cs"]


def hashes():
    return {path.relative_to(ROOT).as_posix(): hashlib.sha256(path.read_bytes()).hexdigest()
            for path in source_files()}


def require_summary(code, summary, mode):
    if not isinstance(summary, dict) or summary.get("schema") != "source-movement-caller-checks/v1":
        raise RuntimeError("Missing actual M1 caller summary; compilation/crash/status-only output is not evidence.")
    expected = 808 if mode == "controlled" else 36732
    status = "bounded-passed" if mode == "bounded" else "passed"
    if (code != 0 or summary.get("status") != status or summary.get("assertions") != expected
            or summary.get("actual_Hurdle_VM_scripts") != 16
            or summary.get("actual_MaybeDive_windows_per_hurdle") != 7
            or summary.get("actual_Core_post_VM_physics_cases") != 664
            or summary.get("actual_Core_LEAPATT_operand_bytes") != 36
            or summary.get("whole_M1_or_full_gameplay_qualified") is not False):
        raise RuntimeError("Actual M1 caller inventory or bounded evidence layer differs.")
    if mode == "complete" and (summary.get("known_R_SKIP_asset_missing") is not False
                               or summary.get("complete_publication_qualification") is not True
                               or summary.get("actual_animation_library_manifest") != MOVEMENT_ANIMATION_SHA):
        raise RuntimeError("Complete caller smoke requires the exact true157 replay library and no missing R_SKIP.")
    if mode == "bounded" and (summary.get("known_R_SKIP_asset_missing") is not True
                              or summary.get("complete_publication_qualification") is not False
                              or summary.get("actual_animation_library_manifest") != MOVEMENT_SELECTED_SHA):
        raise RuntimeError("The explicit156 diagnostic must remain blocked, not full publication green.")
    if mode == "controlled" and (summary.get("complete_publication_qualification") is not False
                                 or summary.get("actual_animation_library_manifest") != MOVEMENT_ANIMATION_SHA):
        raise RuntimeError("Controlled-only checks are not complete smoke qualification or a library alias.")


def require_mutant(code, summary, field):
    if (code != 1 or not isinstance(summary, dict) or summary.get("status") != "failed"
            or summary.get("schema") != "source-movement-caller-checks/v1"
            or not isinstance(summary.get("assertions"), int) or summary["assertions"] <= 0
            or field not in summary.get("failed_assertion", "")):
        raise RuntimeError("Actual M1 mutant did not fail its named offending-field assertion: " + field)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source-root", type=Path, required=True, help="Pinned original player.c root, not C# sources.")
    parser.add_argument("--fixture-manifest", type=Path, required=True)
    parser.add_argument("--bounded156-diagnostic", action="store_true",
                        help="Explicit known-missing-asset control only; never the final publication/CI qualification.")
    parser.add_argument("--no-mutations", action="store_true", help="Author follow-up only, not complete CI.")
    args = parser.parse_args()
    ARTIFACTS.mkdir(exist_ok=True)
    mode = "bounded" if args.bounded156_diagnostic else "complete"
    fixture = args.fixture_manifest.resolve()
    original = args.source_root.resolve()
    before = hashes()
    inputs_before = fixture_hashes(fixture)
    original_before = hashlib.sha256((original / "player.c").read_bytes()).hexdigest()
    snapshot = {path.relative_to(ROOT).as_posix(): path.read_bytes() for path in source_files()}
    if before != {name: hashlib.sha256(data).hexdigest() for name, data in snapshot.items()}:
        raise RuntimeError("Production inventory changed while capturing mutation snapshot.")
    workspace = ARTIFACTS / ("run-" + uuid.uuid4().hex)
    work = ARTIFACTS / "work"
    work.mkdir(exist_ok=True)
    environment = dict(os.environ, TMPDIR=str(work), TMP=str(work), TEMP=str(work),
                       DOTNET_CLI_TELEMETRY_OPTOUT="1", DOTNET_NOLOGO="1")
    result = {"schema": "source-movement-caller-run/v1", "status": "failed",
              "commands": [], "mutants": [], "mode": mode,
              "production_sha256_before": before, "actual_input_sha256_before": inputs_before,
              "original_player_sha256_before": original_before,
              "whole_M1_or_full_gameplay_qualified": False, "native_or_browser_qualified": False,
              "complete_ci_requested": not args.no_mutations and mode == "complete"}

    def execute(label, section, source_root):
        output = ARTIFACTS / label
        build = workspace / label / "build"
        command = ["dotnet", "run", "--project", str(SUITE / "MovementCallerChecks.csproj"),
                   "--configuration", "Release", "--verbosity", "quiet",
                   "-p:BlitzSourceRoot=" + str(source_root) + "/",
                   "-p:BaseIntermediateOutputPath=" + str(build / "obj") + "/",
                   "-p:BaseOutputPath=" + str(build / "bin") + "/",
                   "--", str(fixture), str(output), section]
        result["commands"].append(command)
        process = subprocess.run(command, cwd=ROOT, env=environment, capture_output=True, text=True, timeout=240)
        text = process.stdout + process.stderr
        (ARTIFACTS / (label + ".log")).write_text(text)
        rows = [json.loads(line) for line in process.stdout.splitlines() if line.startswith('{"schema":')]
        return process.returncode, rows[0] if len(rows) == 1 else None, text

    exit_code = 1
    try:
        code, baseline, text = execute("baseline-" + mode, mode, ROOT)
        result["baseline"] = baseline
        try:
            require_summary(code, baseline, mode)
        except RuntimeError:
            print(text[-5000:])
            raise
        result["original_C_post_VM"] = compare(original,
            ARTIFACTS / ("baseline-" + mode) / "actual-caller-physics.tsv", ARTIFACTS / "native")
        if not args.no_mutations:
            for name, filename, anchor, replacement, field in MUTANTS:
                source_root = workspace / name / "source"
                for relative, data in snapshot.items():
                    path = source_root / relative
                    path.parent.mkdir(parents=True, exist_ok=True)
                    path.write_bytes(data)
                relative = "Assets/Scripts/Gameplay/" + filename
                target = source_root / relative
                data = target.read_bytes()
                if data.count(anchor.encode()) != 1:
                    raise RuntimeError("M1 actual mutation anchor must remain unique: " + name)
                changed = data.replace(anchor.encode(), replacement.encode())
                target.write_bytes(changed)
                code, summary, text = execute(name, "controlled", source_root)
                item = {"id": name, "actual_source": relative, "source_sha256": before[relative],
                        "mutated_source_sha256": hashlib.sha256(changed).hexdigest(),
                        "exit_code": code, "summary": summary}
                result["mutants"].append(item)
                try:
                    require_mutant(code, summary, field)
                except RuntimeError:
                    print(text[-5000:])
                    raise
                item["offending_field_rejected"] = True
                print("PASS actual M1 caller mutant:", name, "->", summary["failed_assertion"])
                if hashes() != before or fixture_hashes(fixture) != inputs_before:
                    raise RuntimeError("Live copied sources or actual data changed during isolated mutation proof.")
        code, final, text = execute("final-baseline-" + mode, mode, ROOT)
        result["final_baseline"] = final
        require_summary(code, final, mode)
        result["status"] = "bounded-passed" if mode == "bounded" else "passed"
        exit_code = 0
    except (OSError, ValueError, RuntimeError, subprocess.TimeoutExpired) as error:
        result["error"] = str(error)
        print("M1 CALLER REGRESSION FAILED:", error, file=sys.stderr)
    finally:
        if workspace.exists():
            shutil.rmtree(workspace)
        result["own_isolated_source_and_build_removed"] = not workspace.exists()
        result["production_sha256_after"] = hashes()
        result["actual_input_sha256_after"] = fixture_hashes(fixture)
        result["original_player_sha256_after"] = hashlib.sha256((original / "player.c").read_bytes()).hexdigest()
        stable = (before == result["production_sha256_after"] and inputs_before == result["actual_input_sha256_after"]
                  and original_before == result["original_player_sha256_after"])
        result["sources_and_original_inputs_unchanged"] = stable
        if not stable:
            result["status"] = "inconclusive-sources-or-inputs-changed"
            exit_code = 1
        filename = "result-bounded156.json" if mode == "bounded" else "result.json"
        (ARTIFACTS / filename).write_text(json.dumps(result, indent=2) + "\n")
    if exit_code == 0:
        print("M1", result["status"], "/36732 actual caller assertions /5312 unchanged original-C physics assertions /",
              len(result["mutants"]), "real source mutants / complete qualification:", mode == "complete")
    return exit_code


if __name__ == "__main__":
    sys.exit(main())
