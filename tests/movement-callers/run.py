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
from contact_inputs import ANIMATION_SHA as CONTACT_ANIMATION_SHA, fixture_profile
from kernel_sources import production_paths

COMPLETE_ASSERTIONS = 36948
BOUNDED_ASSERTIONS = 12792
SMOKE_TRACE = "source-sound-smoke/raw-vector-v1"
CACHE_TARGET_ASSERTIONS = 437452
CACHE_TARGET_COUNTS = {
    "FrameCount": 474, "GeometryCalls": 6636, "CurrentRows": 99540, "PreviousRows": 99330,
    "ReferenceRows": 99540, "OrdinaryRows": 96825, "LegacyRows": 420, "ContactFrames": 79,
    "DisplacedActors": 178, "SkippedHosts": 25, "CurrentMatrixFrames": 474, "AddedAssertions": 437030,
    "source474Observed": True, "lastObservedSourceTick": 474,
}
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
CACHE_MUTANTS = (
    ("refresh-after-contact", "production", "Assets/Scripts/Gameplay/GameSimulation.Source.cs",
     "SourceContacts();", "SourceContacts();\n            CacheSourcePlayerCameraJoints();",
     "actual cache geometry is sampled only post-player and BEFORE RL_COLLIS"),
    ("wrong-current-root-basis", "harness", "tests/movement-callers/Program.cs",
     "IReadOnlyList<SourceVector3> expected = sample.Joints;",
     "IReadOnlyList<SourceVector3> expected = inner.CameraJoints(sample.View, afterRoot, sample.Camera);",
     "all source-time cached joint bits equal the actual PRE-RL_COLLIS geometry result"),
    ("drop-contact-cache-checks", "harness", "tests/movement-callers/Program.cs",
     "Assert(SameBits(game.Model.SourceCameraJoints[id, joint], expected[joint]),",
     "if (rootChanged) continue;\n                    Assert(SameBits(game.Model.SourceCameraJoints[id, joint], expected[joint]),",
     "all14x15 cache comparisons include EVERY contact wake and EVERY displaced actor"),
)


def source_files():
    return production_paths()


def hashes():
    return {path.relative_to(ROOT).as_posix(): hashlib.sha256(path.read_bytes()).hexdigest()
            for path in source_files()}


def require_summary(code, summary, mode, animation_sha=MOVEMENT_ANIMATION_SHA):
    if not isinstance(summary, dict) or summary.get("schema") != "source-movement-caller-checks/v1":
        raise RuntimeError("Missing actual M1 caller summary; compilation/crash/status-only output is not evidence.")
    expected = 808 if mode == "controlled" else COMPLETE_ASSERTIONS if mode == "complete" else BOUNDED_ASSERTIONS
    status = "bounded-passed" if mode == "bounded" else "passed"
    retained = summary.get("retained_M1_assertions", summary.get("assertions"))
    if (code != 0 or summary.get("status") != status or retained != expected
            or mode == "controlled" and summary.get("assertions") != expected
            or summary.get("actual_Hurdle_VM_scripts") != 16
            or summary.get("actual_MaybeDive_windows_per_hurdle") != 7
            or summary.get("actual_Core_post_VM_physics_cases") != 664
            or summary.get("actual_Core_LEAPATT_operand_bytes") != 36
            or summary.get("whole_M1_or_full_gameplay_qualified") is not False):
        raise RuntimeError("Actual M1 caller inventory or bounded evidence layer differs.")
    if mode != "controlled":
        cache = summary.get("source_time_cache", {})
        if (summary.get("assertions") != retained + cache.get("AddedAssertions", -1)
                or cache.get("CurrentRows") != 210 * cache.get("FrameCount", -1)
                or cache.get("ReferenceRows") != cache.get("CurrentRows")
                or cache.get("PreviousRows", 0) < 210
                or cache.get("OrdinaryRows", 0) < 210
                or cache.get("CurrentMatrixFrames") != cache.get("FrameCount")
                or cache.get("ContactFrames", 0) <= 0 or cache.get("DisplacedActors", 0) <= 0
                or cache.get("source474Observed") is not True):
            raise RuntimeError("Added passive cache assertions cannot replace retained M1 contracts or omit contact/previous-cache rows.")
    if mode == "complete" and (summary.get("known_R_SKIP_asset_missing") is not False
                               or summary.get("complete_publication_qualification") is not True
                               or summary.get("actual_animation_library_manifest") != animation_sha
                               or summary.get("public_smoke_trace") != SMOKE_TRACE
                               or summary.get("public_smoke_source_tick", 0) <= 2299
                               or summary.get("public_smoke_R_SKIP_frames_after2299", 0) < 2
                               or summary.get("public_smoke_R_SKIP_instance_advanced") is not True
                               or summary.get("public_smoke_cache_assertions", 0) < 35910
                               or summary.get("public_smoke_resolved_sound_dispatches", 0) < 1
                               or summary.get("public_smoke_sound_random_draws", 0) < 1):
        raise RuntimeError("Complete caller smoke requires the exact explicitly selected production library and no missing R_SKIP.")
    if mode == "bounded" and (summary.get("known_R_SKIP_asset_missing") is not True
                              or summary.get("complete_publication_qualification") is not False
                              or summary.get("actual_animation_library_manifest") != MOVEMENT_SELECTED_SHA):
        raise RuntimeError("The explicit156 diagnostic must remain blocked, not full publication green.")
    if mode == "controlled" and (summary.get("complete_publication_qualification") is not False
                                 or summary.get("actual_animation_library_manifest") != animation_sha):
        raise RuntimeError("Controlled-only checks are not complete smoke qualification or a library alias.")


def require_mutant(code, summary, field):
    if (code != 1 or not isinstance(summary, dict) or summary.get("status") != "failed"
            or summary.get("schema") != "source-movement-caller-checks/v1"
            or not isinstance(summary.get("assertions"), int) or summary["assertions"] <= 0
            or field not in summary.get("failed_assertion", "")):
        raise RuntimeError("Actual M1 mutant did not fail its named offending-field assertion: " + field)


def require_cache_summary(code, summary):
    cache = summary.get("source_time_cache", {}) if isinstance(summary, dict) else {}
    if (code != 0 or not isinstance(summary, dict)
            or summary.get("schema") != "source-movement-caller-checks/v1"
            or summary.get("status") != "cache-contract-passed"
            or summary.get("mode") != "targeted-source474-cache-contract-only"
            or summary.get("complete_publication_qualification") is not False
            or summary.get("whole_M1_or_full_gameplay_qualified") is not False
            or summary.get("actual_animation_library_manifest") != CONTACT_ANIMATION_SHA
            or summary.get("public_smoke_trace") != SMOKE_TRACE
            or summary.get("public_smoke_source_tick") != 474
            or summary.get("retained_old_smoke_assertions") != 422
            or summary.get("assertions") != CACHE_TARGET_ASSERTIONS
            or summary.get("assertions") != 422 + cache.get("AddedAssertions", -1)
            or any(cache.get(key) != value for key, value in CACHE_TARGET_COUNTS.items())):
        raise RuntimeError("Targeted cache proof requires every actual source474/14x15/current/previous/contact contract, not full M1 green.")


def cache_contract(args):
    source_root = args.frozen_source_root.absolute()
    source_manifest_path = args.frozen_source_map.absolute()
    allowed = (ROOT / "tests/movement-callers/.artifacts", ROOT / ".squad/.scratch")
    if not any(source_root.is_relative_to(path) for path in allowed):
        raise ValueError("Targeted cache compilation requires an explicit local ignored frozen source copy.")
    manifest = json.loads(source_manifest_path.read_bytes())
    if manifest.get("schema") != "source-contact-cache-compile-snapshot/v1":
        raise ValueError("Expected an explicitly sealed cache compile snapshot, never live changing production.")
    pins = manifest["source_files_sha256"]
    snapshot = {}
    for name, pin in pins.items():
        path = source_root / name
        if Path(name).is_absolute() or ".." in Path(name).parts or path.is_symlink():
            raise ValueError("Nonportable frozen cache source: " + name)
        data = path.read_bytes()
        if hashlib.sha256(data).hexdigest() != pin:
            raise ValueError("Frozen cache compile input changed: " + name)
        snapshot[name] = data
    actual_names = {p.relative_to(source_root).as_posix() for p in production_paths(source_root)}
    if actual_names != set(pins):
        raise ValueError("Frozen compile-source inventory differs; no omitted dependency or extra stand-in.")
    test_names = ["tests/movement-callers/" + name for name in
                  ("MovementCallerChecks.csproj", "Directory.Build.props", "Program.cs", "Canonical.cs",
                   "Controlled.cs", "InputAndStation.cs", "SnapHikeChecks.cs", "CompatibilityChecks.cs")]
    test_names += ["tests/parity/FullKernelInputs.cs", "tests/parity/FullKernelSources.props",
                   "tests/audio/vm/SourceSoundVmSources.props"]
    test_snapshot = {name: (ROOT / name).read_bytes() for name in test_names}
    fixture = args.fixture_manifest.resolve()
    fixture_profile(fixture, "contact272")
    input_before = fixture_hashes(fixture, args.sound_functions)
    workspace = ARTIFACTS / ("cache-isolated-" + uuid.uuid4().hex)
    work = ARTIFACTS / "cache-work"
    work.mkdir(exist_ok=True)
    env = dict(os.environ, TMPDIR=str(work), TMP=str(work), TEMP=str(work),
               DOTNET_CLI_TELEMETRY_OPTOUT="1", DOTNET_NOLOGO="1", MSBUILDDISABLENODEREUSE="1")
    receipt = {"schema": "source-contact-M1-cache-contract-run/v1", "status": "failed",
               "commands": [], "negative_controls": [],
               "immutable_production_sources_sha256": pins,
               "test_closure_sha256": {name: hashlib.sha256(data).hexdigest() for name, data in test_snapshot.items()},
               "actual_inputs_sha256_before": input_before, "source_manifest_sha256": hashlib.sha256(source_manifest_path.read_bytes()).hexdigest(),
               "original8_M1_mutants_preserved_not_qualified_by_this_target": [row[0] for row in MUTANTS],
               "full_M1_B1_or_new_generation_qualified": False, "live_production_read_or_hashed": False}

    def execute(label, spec=None):
        copy = workspace / label
        production, harness = copy / "production", copy / "harness"
        for base, entries in ((production, snapshot), (harness, test_snapshot)):
            for name, data in entries.items():
                target = base / name
                target.parent.mkdir(parents=True, exist_ok=True)
                target.write_bytes(data)
        mutation = None
        if spec:
            name, kind, relative, old, new, field = spec
            target = (production if kind == "production" else harness) / relative
            data = target.read_bytes()
            if data.count(old.encode()) != 1:
                raise RuntimeError("Cache negative-control anchor must be unique: " + name)
            target.write_bytes(data.replace(old.encode(), new.encode()))
            mutation = {"id": name, "kind": kind, "source": relative, "original_sha256": hashlib.sha256(data).hexdigest(),
                        "mutated_sha256": hashlib.sha256(target.read_bytes()).hexdigest(), "expected_assertion": field}
        project = harness / "tests/movement-callers/MovementCallerChecks.csproj"
        build = copy / "build"
        props = ["-p:BlitzSourceRoot=" + str(production) + "/",
                 "-p:BaseIntermediateOutputPath=" + str(build / "obj") + "/",
                 "-p:BaseOutputPath=" + str(build / "bin") + "/"]

        def run(command, suffix):
            receipt["commands"].append([str(x) for x in command])
            result = subprocess.run([str(x) for x in command], cwd=ROOT, env=env,
                                    capture_output=True, text=True, timeout=240)
            text = result.stdout + result.stderr
            (ARTIFACTS / ("cache-" + label + "-" + suffix + ".log")).write_text(text)
            return result, text
        command = ["dotnet", "build", project, "--no-restore", "--configuration", "Release",
                   "--verbosity", "quiet", *props]
        process, text = run(command, "compile")
        if process.returncode and "NETSDK1004" in text:
            feed = workspace / "empty-feed"; feed.mkdir(parents=True, exist_ok=True)
            restored, text = run(["dotnet", "restore", project, "--source", feed, "-p:NuGetAudit=false",
                                  "--verbosity", "quiet", *props], "restore-after-missing-assets")
            if restored.returncode:
                raise RuntimeError("Existing declared SDK restore failed:\n" + text[-5000:])
            process, text = run(command, "compile-restored")
        if process.returncode:
            raise RuntimeError("Cache control compile errors are NOT kills:\n" + text[-5000:])
        output = ARTIFACTS / ("cache-" + label)
        command = ["dotnet", build / "bin/Release/net10.0/MovementCallerChecks.dll",
                   fixture, output, "cache-contract"]
        if args.sound_functions is not None:
            command.append(args.sound_functions.resolve())
        process, text = run(command, "execute")
        summaries = [json.loads(row) for row in process.stdout.splitlines() if row.startswith('{"schema":')]
        summary = summaries[0] if len(summaries) == 1 else None
        if mutation is not None:
            mutation.update(compiled=True, compile_exit_code=0, exit_code=process.returncode, actual_summary=summary)
            receipt["negative_controls"].append(mutation)
        return process.returncode, summary, text

    code = 1
    try:
        status, baseline, text = execute("baseline")
        receipt["baseline"] = baseline
        try:
            require_cache_summary(status, baseline)
        except RuntimeError:
            print(text[-10000:]); raise
        for spec in CACHE_MUTANTS:
            status, summary, text = execute(spec[0], spec)
            try:
                require_mutant(status, summary, spec[-1])
            except RuntimeError:
                print(text[-10000:]); raise
            receipt["negative_controls"][-1]["named_assertion_rejected"] = True
            print("PASS compiled cache control", spec[0], "->", summary["failed_assertion"])
        status, final, text = execute("final-baseline")
        receipt["final_baseline"] = final
        require_cache_summary(status, final)
        receipt["status"] = "cache-contract-passed-only"; code = 0
    except (OSError, ValueError, RuntimeError, subprocess.TimeoutExpired) as error:
        receipt["error"] = str(error)
        print("TARGETED CACHE CONTRACT FAILED:", error, file=sys.stderr)
    finally:
        receipt["frozen_compile_inputs_unchanged"] = all(hashlib.sha256((source_root / n).read_bytes()).hexdigest() == h
                                                         for n, h in pins.items())
        receipt["test_inputs_unchanged"] = all((ROOT / n).read_bytes() == data for n, data in test_snapshot.items())
        receipt["actual_inputs_sha256_after"] = fixture_hashes(fixture, args.sound_functions)
        receipt["actual_inputs_unchanged"] = receipt["actual_inputs_sha256_after"] == input_before
        if not all(receipt[name] for name in ("frozen_compile_inputs_unchanged", "test_inputs_unchanged", "actual_inputs_unchanged")):
            receipt["status"] = "inconclusive-input-changed"; code = 1
        if workspace.exists(): shutil.rmtree(workspace)
        receipt["isolated_negative_sources_and_builds_removed"] = not workspace.exists()
        (ARTIFACTS / "cache-contract-result.json").write_text(json.dumps(receipt, indent=2) + "\n")
    return code


def current_cache_snapshot(snapshot=None):
    if snapshot is None:
        before = hashes()
        snapshot = {p.relative_to(ROOT).as_posix(): p.read_bytes() for p in source_files()}
        if before != {name: hashlib.sha256(data).hexdigest() for name, data in snapshot.items()}:
            raise RuntimeError("Current cache source generation changed during explicit freeze capture.")
    destination = ARTIFACTS / ("cache-current-source-" + uuid.uuid4().hex)
    destination.mkdir()
    for name, data in snapshot.items():
        target = destination / name
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_bytes(data)
    manifest = destination / "source-manifest.json"
    manifest.write_text(json.dumps({
        "schema": "source-contact-cache-compile-snapshot/v1",
        "source_generation": "Explicit current checkout freeze capture; not the archived pre-AI snapshot",
        "source_files_sha256": {name: hashlib.sha256(data).hexdigest() for name, data in snapshot.items()},
        "source_count": len(snapshot),
    }, indent=2) + "\n")
    return destination, manifest


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source-root", type=Path, required=True, help="Pinned original player.c root, not C# sources.")
    parser.add_argument("--fixture-manifest", type=Path, required=True)
    parser.add_argument("--input-profile", choices=("legacy", "contact272"), default="legacy",
                        help="The new272 generation is explicit; selected156 remains a separately named negative control.")
    parser.add_argument("--sound-functions", type=Path,
                        help="Explicit frozen SAFN input, required by the synchronous source sound VM.")
    parser.add_argument("--bounded156-diagnostic", action="store_true",
                        help="Explicit known-missing-asset control only; never the final publication/CI qualification.")
    parser.add_argument("--no-mutations", action="store_true", help="Author follow-up only, not complete CI.")
    parser.add_argument("--cache-contract-only", action="store_true",
                        help="Targeted source474 geometry witness +3 compiled controls; never full M1 qualification.")
    parser.add_argument("--frozen-source-root", type=Path,
                        help="Explicit sealed ignored production snapshot; do not read a moving live Core generation.")
    parser.add_argument("--frozen-source-map", type=Path,
                        help="source-contact-cache-compile-snapshot/v1 manifest for the explicit source root.")
    parser.add_argument("--cache-contract-current", action="store_true",
                        help="After an explicit source freeze only: seal current sources and run the cache-only3-control campaign.")
    args = parser.parse_args()
    ARTIFACTS.mkdir(exist_ok=True)
    if args.cache_contract_only:
        if args.cache_contract_current:
            if args.frozen_source_root is not None or args.frozen_source_map is not None:
                parser.error("Choose explicit frozen-source inputs OR --cache-contract-current, never both.")
            args.frozen_source_root, args.frozen_source_map = current_cache_snapshot()
        if args.input_profile != "contact272" or args.frozen_source_root is None or args.frozen_source_map is None:
            parser.error("--cache-contract-only requires explicit contact272 and both frozen source arguments.")
        return cache_contract(args)
    mode = "bounded" if args.bounded156_diagnostic else "complete"
    fixture = args.fixture_manifest.resolve()
    profile = fixture_profile(fixture, args.input_profile)
    animation_sha = CONTACT_ANIMATION_SHA if profile == "contact272" else MOVEMENT_ANIMATION_SHA
    original = args.source_root.resolve()
    before = hashes()
    inputs_before = fixture_hashes(fixture, args.sound_functions)
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
              "input_profile": profile, "expected_animation_sha256": animation_sha,
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
        if args.sound_functions is not None:
            command.append(str(args.sound_functions.resolve()))
        result["commands"].append(command)
        process = subprocess.run(command, cwd=ROOT, env=environment, capture_output=True, text=True, timeout=240)
        text = process.stdout + process.stderr
        (ARTIFACTS / (label + ".log")).write_text(text)
        rows = [json.loads(line) for line in process.stdout.splitlines() if line.startswith('{"schema":')]
        return process.returncode, rows[0] if len(rows) == 1 else None, text

    exit_code = 1
    try:
        if profile == "contact272" and not args.no_mutations:
            cache_args = argparse.Namespace(**vars(args))
            cache_args.frozen_source_root, cache_args.frozen_source_map = current_cache_snapshot(snapshot)
            if cache_contract(cache_args) != 0:
                raise RuntimeError("Required current-generation cache baseline/3 compiled controls/final baseline failed.")
            cache_result = ARTIFACTS / "cache-contract-result.json"
            result["required_cache_contract"] = {
                "path": str(cache_result), "sha256": hashlib.sha256(cache_result.read_bytes()).hexdigest(),
                "compiled_controls": len(CACHE_MUTANTS), "original8_M1_mutants_retained": True}
        code, baseline, text = execute("baseline-" + mode, mode, ROOT)
        result["baseline"] = baseline
        try:
            require_summary(code, baseline, mode, animation_sha)
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
                if hashes() != before or fixture_hashes(fixture, args.sound_functions) != inputs_before:
                    raise RuntimeError("Live copied sources or actual data changed during isolated mutation proof.")
        code, final, text = execute("final-baseline-" + mode, mode, ROOT)
        result["final_baseline"] = final
        require_summary(code, final, mode, animation_sha)
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
        result["actual_input_sha256_after"] = fixture_hashes(fixture, args.sound_functions)
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
        print("M1", result["status"], "/" + str(result["baseline"]["assertions"]) +
              " actual caller assertions /5312 unchanged original-C physics assertions /",
              len(result["mutants"]), "real source mutants / complete qualification:", mode == "complete")
    return exit_code


if __name__ == "__main__":
    if "--snap-hike-only" in sys.argv:
        import canonical
        sys.exit(canonical.snap_hike_main(sys.argv[1:]))
    if "--c2-m1-inputs" in sys.argv:
        import canonical
        sys.exit(canonical.m1_main(sys.argv[1:]))
    sys.exit(main())
