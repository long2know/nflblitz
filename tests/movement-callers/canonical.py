#!/usr/bin/env python3
"""Compare actual canonical281 composition with a separately labelled isolated cache witness."""
import argparse
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[1]
sys.path.insert(0, str(ROOT / "tests/parity"))
from contact_inputs import fixture_profile
from prepare_inputs import fixture_hashes
from kernel_sources import canonical_hashes, production_paths
from compare_physics import compare

CACHE_SOURCE = "Assets/Scripts/Gameplay/SourceMovementIntegration.cs"
CACHE_SOURCE_SHA = "882808e0ddb4ca02fe2404e9fb1683a5d78615a82933798e6ad6d93f22f77801"
CALL = """                IReadOnlyList<SourceVector3> joints = geometry.CameraJoints(
                    GameSnapshot.SourcePlayerView(actor, Model.SourceTick, true), root, frame);
                if (joints == null || joints.Count != 15)
                    throw new InvalidOperationException("The original post-player camera cache requires all fifteen actual joints.");
"""
OBSERVED_CALL = """                PlayerView qaActualCacheView = GameSnapshot.SourcePlayerView(actor, Model.SourceTick, true);
                IReadOnlyList<SourceVector3> joints = geometry.CameraJoints(
                    qaActualCacheView, root, frame);
                if (joints == null || joints.Count != 15)
                    throw new InvalidOperationException("The original post-player camera cache requires all fifteen actual joints.");
                global::CanonicalCacheCapture.Record(this, qaActualCacheView, root, frame, joints);
"""
HARNESS = (
    "tests/movement-callers/MovementCallerChecks.csproj", "tests/movement-callers/Directory.Build.props",
    "tests/movement-callers/Program.cs", "tests/movement-callers/Canonical.cs",
    "tests/movement-callers/Controlled.cs", "tests/movement-callers/InputAndStation.cs",
    "tests/parity/FullKernelInputs.cs", "tests/parity/FullKernelSources.props",
    "tests/audio/vm/SourceSoundVmSources.props",
)
CANONICAL_CACHE_MUTANTS = (
    ("refresh-after-contact", "production", "Assets/Scripts/Gameplay/GameSimulation.Source.cs",
     "SourceContacts();", "SourceContacts();\n            CacheSourcePlayerCameraJoints();",
     "canonical isolated observer records only the actual post-player/pre-contact call"),
    ("wrong-current-root-basis", "harness", "tests/movement-callers/Canonical.cs",
     "SourceVector3 referenceRoot = sample.Root;", "SourceVector3 referenceRoot = afterRoot;",
     "canonical ReferenceRows use exact PRE-contact call arguments"),
    ("drop-contact-cache-checks", "harness", "tests/movement-callers/Canonical.cs",
     "Check(SameBits(sample.Joints[joint], reference[joint]),",
     "if (displaced) continue;\n                        Check(SameBits(sample.Joints[joint], reference[joint]),",
     "canonical all14x15 comparisons include EVERY displaced actor"),
)


def sha(data):
    return hashlib.sha256(data).hexdigest()


def inject(original):
    if sha(original) != CACHE_SOURCE_SHA:
        raise ValueError("Canonical cache observation requires the exact frozen original production hash.")
    old, new = CALL.encode(), OBSERVED_CALL.encode()
    if original.count(old) != 1 or b"CanonicalCacheCapture" in original:
        raise ValueError("Canonical observer needs one unique real geometry call/count-guard site.")
    observed = original.replace(old, new)
    if observed.count(new) != 1 or observed.replace(new, old) != original:
        raise ValueError("Removing only the observer/local argument capture must restore the original bytes.")
    return observed


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--fixture-manifest", type=Path, required=True)
    parser.add_argument("--sound-functions", type=Path, required=True)
    parser.add_argument("--c2-inputs", type=Path, required=True)
    parser.add_argument("--source-root", type=Path, required=True, help="Pinned original player.c for all664 post-VM cases.")
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    output = args.output.absolute()
    if (not output.is_relative_to(ROOT) or output.exists()
            or not any(output.is_relative_to(p) for p in (HERE / ".artifacts", ROOT / ".squad/.scratch"))):
        parser.error("Use a new explicitly ignored local output, never an existing owner receipt.")
    output.mkdir(parents=True)
    fixture_profile(args.fixture_manifest, "contact272")
    inputs = fixture_hashes(args.fixture_manifest, args.sound_functions)
    inputs.update(canonical_hashes(args.c2_inputs))
    production = {p.relative_to(ROOT).as_posix(): p.read_bytes() for p in production_paths()}
    pins = {name: sha(data) for name, data in production.items()}
    harness = {name: (ROOT / name).read_bytes() for name in HARNESS}
    observed_source = inject(production[CACHE_SOURCE])
    workspace = output / "isolated"
    work = output / "work"
    work.mkdir()
    environment = dict(os.environ, TMPDIR=str(work), TMP=str(work), TEMP=str(work),
                       DOTNET_CLI_TELEMETRY_OPTOUT="1", MSBUILDDISABLENODEREUSE="1",
                       DOTNET_NOLOGO="1", PYTHONDONTWRITEBYTECODE="1",
                       DOTNET_CLI_HOME=str(work / "dotnet-home"), NUGET_PACKAGES=str(work / "nuget"))
    result = {"schema": "source-canonical281-cache-observation-run/v1", "status": "failed",
              "production_sha256_before": pins,
              "harness_sha256": {name: sha(data) for name, data in harness.items()},
              "inputs_sha256_before": inputs, "commands": [], "runs": {},
              "instrumentation": {
                  "original_source": CACHE_SOURCE, "original_sha256": CACHE_SOURCE_SHA,
                  "observed_sha256": sha(observed_source), "unique_call_site": True,
                  "removing_only_insertion_restores_original_bytes": True,
                  "additional_geometry_calls": 0, "replacement_geometry": False,
                  "model_RNG_order_return_or_live_assembly_mutation": False},
              "full_M1_or_game_or_native_acceptance": False}
    controls_spec = importlib.util.spec_from_file_location("canonical_M1_existing_controls", HERE / "run.py")
    controls = importlib.util.module_from_spec(controls_spec)
    controls_spec.loader.exec_module(controls)

    def execute(label, instrumented, mutation=None, section=None):
        copy = workspace / label
        source, tests = copy / "source", copy / "harness"
        for base, entries in ((source, production), (tests, harness)):
            for name, content in entries.items():
                path = base / name
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_bytes(content)
        if instrumented:
            (source / CACHE_SOURCE).write_bytes(observed_source)
        if mutation is not None:
            identity, kind, relative, old, new, expected = mutation
            target = (source if kind == "production" else tests) / relative
            original = target.read_bytes()
            if original.count(old.encode()) != 1:
                raise RuntimeError("Canonical cache control requires one actual unique anchor: " + identity)
            target.write_bytes(original.replace(old.encode(), new.encode()))
        project = tests / "tests/movement-callers/MovementCallerChecks.csproj"
        build = copy / "build"
        command = ["dotnet", "build", str(project), "--no-restore", "--configuration", "Release",
                   "--verbosity", "quiet", "-p:BlitzSourceRoot=" + str(source) + "/",
                   "-p:BaseIntermediateOutputPath=" + str(build / "obj") + "/",
                   "-p:BaseOutputPath=" + str(build / "bin") + "/"]
        result["commands"].append(command)
        compiled = subprocess.run(command, cwd=ROOT, env=environment, capture_output=True, text=True, timeout=240)
        (output / (label + "-compile.log")).write_text(compiled.stdout + compiled.stderr)
        if compiled.returncode and "NETSDK1004" in compiled.stdout + compiled.stderr:
            feed = work / "empty-feed"
            feed.mkdir(exist_ok=True)
            restore = ["dotnet", "restore", str(project), "--source", str(feed), "--verbosity", "quiet",
                       "-p:NuGetAudit=false", "-p:BaseIntermediateOutputPath=" + str(build / "obj") + "/"]
            result["commands"].append(restore)
            restored = subprocess.run(restore, cwd=ROOT, env=environment, capture_output=True, text=True, timeout=240)
            (output / (label + "-restore-after-missing-assets.log")).write_text(restored.stdout + restored.stderr)
            if restored.returncode:
                raise RuntimeError("Existing SDK local restore failed after missing assets: " + label)
            compiled = subprocess.run(command, cwd=ROOT, env=environment, capture_output=True, text=True, timeout=240)
            (output / (label + "-compile-after-restore.log")).write_text(compiled.stdout + compiled.stderr)
        if compiled.returncode:
            raise RuntimeError("Canonical harness must compile; a compilation failure is NOT a production failure: " + label)
        mode = section or ("canonical281-observed" if instrumented else "canonical281")
        command = ["dotnet", str(build / "bin/Release/net10.0/MovementCallerChecks.dll"),
                   str(args.fixture_manifest.absolute()), str(output / label), mode,
                   str(args.sound_functions.absolute())]
        command += [str(args.c2_inputs.absolute())] if section is None else ["--c2-inputs", str(args.c2_inputs.absolute())]
        result["commands"].append(command)
        process = subprocess.run(command, cwd=ROOT, env=environment, capture_output=True, text=True, timeout=600)
        (output / (label + ".log")).write_text(process.stdout + process.stderr)
        rows = [json.loads(line) for line in process.stdout.splitlines() if line.startswith('{"schema":')]
        actual = rows[-1] if len(rows) == 1 else None
        result["runs"][label] = {"exit_code": process.returncode, "summary": actual,
                                 "instrumented": instrumented}
        if mutation is not None:
            if (process.returncode != 1 or actual is None or actual.get("status") != "failed"
                    or actual.get("assertions", 0) <= 0 or expected not in actual.get("failed_assertion", "")):
                raise RuntimeError("Compiled canonical cache control did not reject its named causal field: " + identity)
            result["runs"][label].update(compiled=True, named_causal_rejection=True,
                                         expected_assertion=expected, mutation_source=relative,
                                         mutated_sha256=sha(target.read_bytes()))
            return actual
        failure = output / label / "first-canonical-software-failure.json"
        if section is not None:
            failure = output / label / "first-current-source-failure.json"
        if failure.exists():
            result["first_software_failure"] = {"path": str(failure), "sha256": sha(failure.read_bytes())}
            raise RuntimeError("Canonical real-composition stream stopped at a genuine software boundary: " + str(failure))
        if section is not None:
            if section == "controlled":
                from kernel_sources import C2_ANIMATION_SHA
                controls.require_summary(process.returncode, actual, "controlled", C2_ANIMATION_SHA)
            elif (process.returncode != 0 or actual is None or actual.get("status") != "passed"
                  or actual.get("actual_Core_post_VM_physics_cases") != 664
                  or actual.get("actual_Hurdle_VM_scripts") != 16
                  or actual.get("actual_MaybeDive_windows_per_hurdle") != 7
                  or actual.get("complete_publication_qualification") is not False):
                raise RuntimeError("Every existing canonical M1 caller/gesture/physics case group must pass: " + section)
            return actual
        if (process.returncode or actual is None or actual.get("status") != "canonical-entry-window-only"
                or actual.get("full_M1_qualified") is not False):
            raise RuntimeError("Canonical entry compilation/checks did not produce qualified scoped evidence: " + label)
        receipt = json.loads((output / label / "canonical-entry.json").read_bytes())
        if (receipt["completedHosts"] != 12000 or receipt["observed"] != instrumented
                or receipt["factory"] != "REAL SourceC2Composition" or receipt["fullM1OrGameOrNativeAccepted"] is not False):
            raise RuntimeError("Canonical entry/factory/window inventory differs: " + label)
        result["runs"][label]["receipt_sha256"] = sha((output / label / "canonical-entry.json").read_bytes())
        return receipt

    exit_code = 1
    try:
        untouched = execute("uninstrumented", False)
        observed = execute("isolated-observer", True)
        first = (output / "uninstrumented/canonical-observable-stream.jsonl").read_bytes()
        second = (output / "isolated-observer/canonical-observable-stream.jsonl").read_bytes()
        if first != second or untouched["allSharedRawDraws"] != observed["allSharedRawDraws"]:
            raise RuntimeError("Isolated witness changed actual state/events/cache/RNG; it cannot qualify untouched production.")
        result["byte_identical_full_observable_stream_and_all_raw_draws"] = True
        result["compared_host_records"] = len(first.splitlines())
        result["stream_sha256"] = sha(first)
        for spec in CANONICAL_CACHE_MUTANTS:
            execute("control-" + spec[0], True, spec)
        final = execute("final-uninstrumented", False)
        final_stream = (output / "final-uninstrumented/canonical-observable-stream.jsonl").read_bytes()
        if final_stream != first or final["allSharedRawDraws"] != untouched["allSharedRawDraws"]:
            raise RuntimeError("Final untouched real-composition baseline differs after isolated cache controls.")
        result["canonical_compiled_cache_controls"] = len(CANONICAL_CACHE_MUTANTS)
        result["historical_source474_three_controls_preserved_separately"] = True
        result["final_uninstrumented_baseline_identical"] = True
        execute("controlled-baseline", False, section="controlled")
        campaign = execute("existing-M1-campaigns", False, section="campaigns")
        result["original_C_post_VM"] = compare(args.source_root,
            output / "existing-M1-campaigns/actual-caller-physics.tsv", output / "original-C-post-VM")
        for identity, filename, old, new, expected in controls.MUTANTS:
            spec = (identity, "production", "Assets/Scripts/Gameplay/" + filename, old, new, expected)
            execute("M1-control-" + identity, False, spec, section="controlled")
        execute("controlled-final", False, section="controlled")
        final_campaign = execute("final-existing-M1-campaigns", False, section="campaigns")
        if final_campaign.get("assertions") != campaign.get("assertions"):
            raise RuntimeError("Canonical caller campaign assertions changed after isolated original8 controls.")
        result["original8_M1_controls_compiled_and_causal"] = 8
        result["all_existing_M1_case_groups_executed"] = True
        result["old36948_full_M1_gate_not_relabeled_as_canonical"] = True
        result["canonical_R_SKIP_coverage"] = untouched["rSkipFrames"]
        if untouched["rSkipFrames"] == 0:
            result["stronger_full_M1_coverage_blocked"] = "Unchanged unfiltered canonical12000 window does not execute the prior R_SKIP advancement assertions; no vector/filter/assertion change authorized."
        result["status"] = "passed-scoped-entry-and-cache-only"
        exit_code = 0
    except (ValueError, OSError, RuntimeError, subprocess.TimeoutExpired) as error:
        result["error"] = str(error)
        print(str(error), file=sys.stderr)
    finally:
        if workspace.exists():
            shutil.rmtree(workspace)
        result["own_copies_removed"] = not workspace.exists()
        result["production_sha256_after"] = {p.relative_to(ROOT).as_posix(): sha(p.read_bytes()) for p in production_paths()}
        after_inputs = fixture_hashes(args.fixture_manifest, args.sound_functions)
        after_inputs.update(canonical_hashes(args.c2_inputs))
        result["inputs_sha256_after"] = after_inputs
        result["production_and_inputs_unchanged"] = pins == result["production_sha256_after"] and inputs == after_inputs
        if not result["production_and_inputs_unchanged"]:
            result["status"] = "inconclusive-generation-changed"
            exit_code = 1
        (output / "result.json").write_text(json.dumps(result, indent=2) + "\n")
    print(result["status"])
    return exit_code


if __name__ == "__main__":
    raise SystemExit(main())
