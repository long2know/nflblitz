#!/usr/bin/env python3
"""Run pinned original-C ball/caller checks and isolated real-production mutations."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import uuid

from native_oracle import build, PINS

SUITE = Path(__file__).resolve().parent
ROOT = SUITE.parents[1]
ARTIFACTS = SUITE / ".artifacts"
sys.path.insert(0, str(ROOT / "tests/parity"))
from prepare_inputs import fixture_hashes

EXPECTED = {"sentinel_assertions": 36, "native_ball_assertions": 470666,
            "caller_assertions": 18002, "geometry_caller_assertions": 32}
MUTATIONS = (
    ("pelvis-strict14", "SourceCatchRules.cs", "Distance(ball, pelvis) < 14f", "Distance(ball, pelvis) <= 14f", "native", "original standard_catch case", 1),
    ("wrist-strict5", "SourceCatchRules.cs", "Distance(ball, leftWrist) < 5f", "Distance(ball, leftWrist) <= 5f", "native", "original standard_catch case", 1),
    ("early-defender30", "SourceCatchRules.cs", "model.PassTicksElapsed <= 30 && !offense", "model.PassTicksElapsed < 30 && !offense", "native", "original standard_catch case", 1),
    ("original-deflect-quirk", "SourceBallDriver.cs", "loss * (vx + vy)", "loss * (vx + vz)", "native", "original deflect_pass case", 1),
    ("original-ball-floor", "SourceBallDriver.cs", "0.8f * model.SourceBallScale", "0.0f * model.SourceBallScale", "native", "original ball_proc physics case", 2),
    ("control-time18-probability", "SourceContactDriver.cs", "if (held >= 18) bobble = 0;", "if (held > 18) bobble = 0;", "native", "original fumble_or_bobble and launch case", 1),
    ("actual-wrist-basis", "SourceCatchGeometry.cs", "actor.Position.Z * SourceTiming.SourceUnitsPerYard - joint.Z",
     "actor.Position.Z * SourceTiming.SourceUnitsPerYard + joint.Z", "geometry", "actual QBSTANCE matrix/pivot/source-basis/ascale joint", 1),
    ("focus-edge-flush", "GameSimulation.cs", "pendingSourceActions.Clear();", "/* isolated mutation: queue not cleared */", "geometry", "focus flush actually removes", 1),
    ("raw-released-latch", "GameSimulation.cs", "released[input.StationId] |= input.Released;",
     "/* isolated mutation: queued release discarded */", "caller", "complete between-wake press/release", 1),
    ("raw-new-pressed", "SourcePlayerDriver.cs", "actor.SourceNewButtons |= (SourceDroneButtons)(int)input.Pressed;",
     "/* isolated mutation: ignore latched press */", "caller", "complete B press/release actually snaps", 1),
    ("source-transform-wake", "GameSimulation.Source.cs", "if (SourceStationInputTransform != null)",
     "if (SourceStationInputTransform != null && Model.SourceTick % 2 == 0)", "caller", "ordered raw drain transforms exactly once", 1),
    ("off-flip-real-lineup", "GameSimulation.Source.cs",
     "Model.SourceLinePosition * SourceTiming.SourceUnitsPerYard, Model.SourceOffenseFlipped)",
     "Model.SourceLinePosition * SourceTiming.SourceUnitsPerYard, false)", "caller", "off_flip passes exact source hash/offset", 1),
    ("original-predicate90", "SourceAnimationState.cs", "actor.SourceIsCarrier && random(100) < 90",
     "actor.SourceIsCarrier && random(100) <= 90", "caller", "actual original C plyr_have_ball gate", 1),
    ("singleton-camera-draw", "GameSimulation.Source.cs", "if (instruction.Op == SourceAnimationOp.CameraCue)",
     "if (instruction.Op == SourceAnimationOp.CameraCue && instruction.A > 1)", "caller", "actual typed camera DTO count", 1),
    ("noncarrier-shaker-filter", "GameSimulation.Source.cs",
     "sourcePresentationCues.Add(new SourceShakerCue(context, instruction.A, instruction.B, Model.CarrierId));",
     "{ if (actor.Id == Model.CarrierId) sourcePresentationCues.Add(new SourceShakerCue(context, instruction.A, instruction.B, Model.CarrierId)); }",
     "caller", "every actually executed presentation instruction", 1),
    ("human-receiver-columns", "SourcePassDriver.cs", "column < 2 ? 0 : column < 5 ? 1 : 2",
     "column < 3 ? 0 : column < 5 ? 1 : 2", "caller", "actual C pick_receiver/target_cmp result", 1),
    ("pat-sentinel-rejected", "SourceDroneAI.cs", "bool originalPatOrKickoffDown = rules.Down == -1",
     "bool originalPatOrKickoffDown = rules.Down == -2", "sentinel", "SourceAiFrame original PAT down=-1", 1),
    ("normal-down-upper-bound", "SourceDroneAI.cs", "rules.Down > 4 || rules.LineOfScrimmage",
     "rules.Down > 5 || rules.LineOfScrimmage", "sentinel", "SourceAiFrame rejects Normal down=5", 1),
    ("delayed-catch-controller", "GameSimulation.Source.cs",
     "if (actor.SourceControllerStation >= 0 || !SourceContactDriver.IsHumanTeam(Model, actor.Team))",
     "if (actor.SourceControllerStation >= 0 || !SourceContactDriver.IsHumanTeam(Model, actor.Team) || Model.SourceTick - Model.CatchSourceTick < 18)",
     "geometry", "actual catch caller transfers human controller immediately", 1),
    ("delayed-possession-credit", "GameSimulation.Source.cs",
     "if (side != Model.SourceLastPossession && held >= 18)",
     "if (side != Model.SourceLastPossession && held > 18)",
     "geometry", "actual CONTROL_TIME18 credits possession", 1),
)


def source_files():
    return [path for directory in ("Contracts", "Gameplay", "AssetImport")
            for path in sorted((ROOT / "Assets/Scripts" / directory).rglob("*.cs"))]


def hashes(paths):
    return {path.relative_to(ROOT).as_posix(): hashlib.sha256(path.read_bytes()).hexdigest() for path in paths}


def require_baseline(code, summary, section):
    field = {"geometry": "geometry_caller_assertions", "native": "native_ball_assertions"}.get(
        section, section + "_assertions")
    required = EXPECTED if section == "all" else {field: EXPECTED[field]}
    if (code != 0 or not isinstance(summary, dict) or summary.get("status") != "passed"
            or summary.get("schema") != "actual-source-ball-checks/v1"
            or summary.get("full_gameplay_parity_claimed") is not False
            or any(summary.get(key) != value for key, value in required.items())):
        raise RuntimeError("Expected exact nonempty actual-source ball assertion inventory.")


def require_mutant(code, summary, expected_field):
    if (code != 1 or not isinstance(summary, dict) or summary.get("status") != "failed"
            or summary.get("schema") != "actual-source-ball-checks/v1"
            or summary.get("full_gameplay_parity_claimed") is not False
            or expected_field not in summary.get("failed_assertion", "")):
        raise RuntimeError("Actual mutant did not fail its required offending-field assertion: " + expected_field)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source-root", type=Path, required=True, help="Exact pinned original C/header inputs, not production C#.")
    parser.add_argument("--fixture-manifest", type=Path, required=True)
    parser.add_argument("--section", choices=("all", "sentinel", "native", "caller", "geometry"), default="all")
    parser.add_argument("--no-mutations", action="store_true", help="Focused author check only; not the complete CI proof.")
    args = parser.parse_args()
    if args.section != "all" and not args.no_mutations:
        parser.error("Focused sections require explicit --no-mutations; CI uses the complete default.")
    ARTIFACTS.mkdir(exist_ok=True)
    fixture = args.fixture_manifest.resolve()
    original = args.source_root.resolve()
    native_root = ARTIFACTS / "native"
    before = hashes(source_files())
    fixture_before = fixture_hashes(fixture)
    original_before = {name: hashlib.sha256((original / name).read_bytes()).hexdigest() for name in PINS}
    snapshot = {path.relative_to(ROOT).as_posix(): path.read_bytes() for path in source_files()}
    if before != {name: hashlib.sha256(data).hexdigest() for name, data in snapshot.items()}:
        raise RuntimeError("Actual source inventory changed while capturing the isolated snapshot.")
    workspace = ARTIFACTS / ("run-" + uuid.uuid4().hex)
    cache = ARTIFACTS / "work"
    cache.mkdir(exist_ok=True)
    environment = dict(os.environ, TMPDIR=str(cache), TMP=str(cache), TEMP=str(cache),
                       DOTNET_CLI_TELEMETRY_OPTOUT="1", DOTNET_NOLOGO="1")
    evidence = {"schema": "source-ball-regression-run/v1", "status": "failed",
                "commands": [], "mutations": [], "production_sha256_before": before,
                "real_input_sha256_before": fixture_before, "original_sha256_before": original_before,
                "full_gameplay_parity_claimed": False, "unity_editor_or_browser_executed": False,
                "complete_ci_proof_requested": args.section == "all" and not args.no_mutations}
    exit_code = 1

    def execute(label, section, source_root):
        build_root = workspace / label / "build"
        command = ["dotnet", "run", "--project", str(SUITE / "BallChecks.csproj"),
                   "--configuration", "Release", "--verbosity", "quiet",
                   "-p:BlitzSourceRoot=" + str(source_root) + "/",
                   "-p:BaseIntermediateOutputPath=" + str(build_root / "obj") + "/",
                   "-p:BaseOutputPath=" + str(build_root / "bin") + "/",
                   "--", str(fixture), str(native_root), section]
        evidence["commands"].append(command)
        process = subprocess.run(command, cwd=ROOT, env=environment, text=True, capture_output=True, timeout=240)
        text = process.stdout + process.stderr
        (ARTIFACTS / (label + ".log")).write_text(text, encoding="utf-8")
        rows = [json.loads(line) for line in process.stdout.splitlines() if line.startswith('{"schema":')]
        return process.returncode, rows[0] if len(rows) == 1 else None, text

    try:
        evidence["original_c"] = build(original, fixture, native_root)
        code, baseline, text = execute("baseline", args.section, ROOT)
        evidence["baseline"], evidence["baseline_exit_code"] = baseline, code
        try:
            require_baseline(code, baseline, args.section)
        except RuntimeError:
            print(text[-5000:])
            raise
        if not args.no_mutations:
            for name, filename, anchor, replacement, section, expected_field, occurrences in MUTATIONS:
                source_root = workspace / name / "source"
                for relative, data in snapshot.items():
                    copied = source_root / relative
                    copied.parent.mkdir(parents=True, exist_ok=True)
                    copied.write_bytes(data)
                relative = "Assets/Scripts/Gameplay/" + filename
                target = source_root / relative
                data = target.read_bytes()
                if data.count(anchor.encode()) != occurrences:
                    raise RuntimeError("Actual mutation anchor count changed: " + name)
                mutated = data.replace(anchor.encode(), replacement.encode())
                target.write_bytes(mutated)
                code, summary, text = execute(name, section, source_root)
                row = {"id": name, "source": relative, "before": anchor, "after": replacement,
                       "expected_occurrences": occurrences, "source_sha256": before[relative],
                       "mutated_source_sha256": hashlib.sha256(mutated).hexdigest(),
                       "exit_code": code, "summary": summary}
                evidence["mutations"].append(row)
                try:
                    require_mutant(code, summary, expected_field)
                except RuntimeError:
                    print(text[-5000:])
                    raise
                row["offending_field_assertion_rejected"] = True
                print("PASS actual-source mutant: " + name + " -> " + summary["failed_assertion"])
                if hashes(source_files()) != before or fixture_hashes(fixture) != fixture_before:
                    raise RuntimeError("Actual shared sources/real inputs changed during isolated mutations.")
        code, final, text = execute("final-baseline", args.section, ROOT)
        evidence["final_baseline"], evidence["final_baseline_exit_code"] = final, code
        require_baseline(code, final, args.section)
        evidence["status"] = "passed"
        exit_code = 0
    except (OSError, ValueError, RuntimeError, subprocess.TimeoutExpired) as error:
        evidence["error"] = str(error)
        print("BALL REGRESSION FAILED: " + str(error), file=sys.stderr)
    finally:
        if workspace.exists():
            shutil.rmtree(workspace)
        evidence["isolated_source_and_build_trees_removed"] = not workspace.exists()
        evidence["production_sha256_after"] = hashes(source_files())
        evidence["real_input_sha256_after"] = fixture_hashes(fixture)
        evidence["original_sha256_after"] = {name: hashlib.sha256((original / name).read_bytes()).hexdigest() for name in PINS}
        stable = (before == evidence["production_sha256_after"] and fixture_before == evidence["real_input_sha256_after"]
                  and original_before == evidence["original_sha256_after"])
        evidence["sources_and_actual_inputs_unchanged"] = stable
        if not stable:
            evidence["status"] = "inconclusive-inputs-or-sources-changed"
            exit_code = 1
        (ARTIFACTS / "result.json").write_text(json.dumps(evidence, indent=2) + "\n", encoding="utf-8")
    if exit_code == 0:
        print("PASS: exact original-C ball/caller regressions; " + str(len(evidence["mutations"])) +
              " actual-source mutants rejected; unchanged source/input hashes.")
    return exit_code


if __name__ == "__main__":
    sys.exit(main())
