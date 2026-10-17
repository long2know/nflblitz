#!/usr/bin/env python3
"""Exact original-C contact reference, actual full-kernel scenarios, and isolated compiled mutants."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import uuid

from oracle import build
from source import read
from translate import translate, csharp

SUITE = Path(__file__).resolve().parent
ROOT = SUITE.parents[1]
ARTIFACTS = SUITE / ".artifacts"
SOUND_SHA = "4a557304d3b041ee04dff8ed1354714fc5665abaeb1c3d458d94171adf1a0280"
EXPECTED_ASSERTIONS = {"inputs": 24, "emit": 2, "verify": 253239, "kernel": 34548, "sound-short": 34508,
                       "heading-boundary": 598039, "movement-bearing": 22107, "route-target": 28052}
sys.path.insert(0, str(ROOT / "tests/parity"))
from prepare_inputs import fixture_hashes
from contact_inputs import ANIMATION_SHA as CONTACT_SHA, PROFILE, fixture_profile
from kernel_sources import production_paths, canonical_hashes, C2_ANIMATION_SHA, C2_DATA_SHA, C2_HIT_SHA
from c2_controls import specifications as c2_specifications
from prepare_c2 import prepare_original_oracles
from fetch_inputs import verified_original_hashes


def require_m1_sound_summary(code, summary):
    if (code != 0 or summary is None or summary.get("status") != "passed"
            or summary.get("assertions") != 251
            or summary.get("production_profile") != "original-teams-contact281-c2-m1"
            or summary.get("c2_callback_data_sha256") !=
                "b0c02e5516f4f932625e1f8ccb3ff0fd53ae222657f899eb8c9d0b0c2e74460a"
            or summary.get("c2_bounded_evidence", {}).get("m1_running_taunt_actual_factory_ANI_CODE_cases") != 30
            or summary.get("actual_bound_SAFN") != SOUND_SHA):
        raise RuntimeError("Exact M1 successor real-factory/native-running-binding inventory required; not full M1.")


def run_m1_sound(argv):
    from c2_controls import m1_specifications
    parser = argparse.ArgumentParser(description="Explicit M1 running-sound original-C/actual-factory focused closure.")
    parser.add_argument("--m1-running-sound-only", action="store_true", required=True)
    for name in ("source-root", "process-root", "fixture-manifest", "sound-functions",
                 "c2-m1-inputs", "old-c2-inputs", "contact-artifacts", "output"):
        parser.add_argument("--" + name, type=Path, required=True)
    args = parser.parse_args(argv)
    output = args.output.absolute()
    if (output.exists() or not any(output.is_relative_to(p) for p in
            (SUITE / ".artifacts", ROOT / ".squad/.scratch"))):
        parser.error("Use a new ignored output child, never overwrite frozen receipts.")
    output.mkdir(parents=True)
    work = output / "work"
    work.mkdir()
    (work / "empty-feed").mkdir()
    env = dict(os.environ, TMPDIR=str(work), TMP=str(work), TEMP=str(work),
               PYTHONDONTWRITEBYTECODE="1", DOTNET_NOLOGO="1", DOTNET_CLI_TELEMETRY_OPTOUT="1",
               MSBUILDDISABLENODEREUSE="1", DOTNET_CLI_HOME=str(work / "dotnet-home"),
               NUGET_PACKAGES=str(work / "nuget"))
    before = {str(p.relative_to(ROOT)): hashlib.sha256(p.read_bytes()).hexdigest() for p in production_paths()}
    inputs = fixture_hashes(args.fixture_manifest, args.sound_functions)
    inputs.update(canonical_hashes(args.old_c2_inputs))
    inputs.update(canonical_hashes(args.c2_m1_inputs, "contact281-c2-m1"))
    originals = verified_original_hashes(args.source_root, "contact272")
    commands, native = prepare_original_oracles(args.source_root, args.process_root, output / "original-C")
    snapshot = {str(p.relative_to(ROOT)): p.read_bytes() for p in production_paths()}
    result = {"schema": "source-C2-M1-running-sound-focused/v1", "status": "failed",
              "full_M1_game_native_or_DSP_qualified": False, "commands": commands, "controls": [],
              "production_sha256_before": before, "input_sha256_before": inputs,
              "original_source_sha256": originals, "proper_original_C": native}

    def execute(label, mutation=None):
        source = output / "copies" / label
        for name, raw in snapshot.items():
            path = source / name
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(raw)
        if mutation:
            identity, relative, old, new, expected = mutation
            path = source / relative
            text = path.read_text()
            if text.count(old) != 1:
                raise RuntimeError("M1 source mutation anchor must be unique: " + identity)
            path.write_text(text.replace(old, new))
        build = output / "build" / label
        common = ["-p:BlitzSourceRoot=" + str(source) + "/",
                  "-p:BaseIntermediateOutputPath=" + str(build / "obj") + "/",
                  "-p:BaseOutputPath=" + str(build / "bin") + "/"]
        command = ["dotnet", "build", str(SUITE / "ContactChecks.csproj"), "--no-restore",
                   "--configuration", "Release", "--verbosity", "quiet", *common]
        result["commands"].append(command)
        compiled = subprocess.run(command, cwd=ROOT, env=env, capture_output=True, text=True, timeout=240)
        text = compiled.stdout + compiled.stderr
        if compiled.returncode and "NETSDK1004" in text:
            restore = ["dotnet", "restore", str(SUITE / "ContactChecks.csproj"), "--source", str(work / "empty-feed"),
                       "--verbosity", "quiet", "-p:NuGetAudit=false", *common]
            result["commands"].append(restore)
            restored = subprocess.run(restore, cwd=ROOT, env=env, capture_output=True, text=True, timeout=240)
            if restored.returncode:
                raise RuntimeError("M1 focused restore failed after missing assets: " + label)
            compiled = subprocess.run(command, cwd=ROOT, env=env, capture_output=True, text=True, timeout=240)
            text = compiled.stdout + compiled.stderr
        (output / (label + "-compile.log")).write_text(text)
        if compiled.returncode or "0 Warning(s)" not in text or "0 Error(s)" not in text:
            raise RuntimeError("M1 focused controls require zero-diagnostic compilation, never compile-error kills: " + label)
        destination = output / label
        command = ["dotnet", str(build / "bin/Release/net10.0/ContactChecks.dll"),
                   str(args.fixture_manifest.absolute()), str(args.contact_artifacts.absolute()), "c2-m1-sound",
                   str(destination), str(args.sound_functions.absolute()), str(args.source_root.absolute()),
                   str(args.c2_m1_inputs.absolute()), str(output / "original-C"), str(args.old_c2_inputs.absolute())]
        result["commands"].append(command)
        process = subprocess.run(command, cwd=ROOT, env=env, capture_output=True, text=True, timeout=180)
        (output / (label + ".log")).write_text(process.stdout + process.stderr)
        rows = [json.loads(line) for line in process.stdout.splitlines() if line.startswith('{"schema":')]
        summary = rows[-1] if len(rows) == 1 else None
        if mutation:
            if (process.returncode != 1 or summary is None or summary.get("status") != "failed"
                    or summary.get("assertions", 0) <= 0 or expected not in summary.get("failed_assertion", "")):
                raise RuntimeError("M1 compiled control did not reject its exact named causal field: " + identity)
            result["controls"].append({"id": identity, "source": relative, "compiled": True,
                                       "zero_warnings_errors": True, "named_failure": summary["failed_assertion"]})
            print("PASS M1 compiled control " + identity, flush=True)
        else:
            require_m1_sound_summary(process.returncode, summary)
            result[label] = summary
        shutil.rmtree(source)

    try:
        execute("baseline")
        for mutation in m1_specifications():
            execute(mutation[0], mutation)
        execute("final-baseline")
        if len(result["controls"]) != len(m1_specifications()):
            raise RuntimeError("Every M1 source-binding causal control is mandatory.")
        result["status"] = "passed-focused-not-full-M1"
    except (RuntimeError, ValueError, OSError, subprocess.TimeoutExpired) as error:
        result["error"] = str(error)
        print(str(error), file=sys.stderr)
    finally:
        after = {str(p.relative_to(ROOT)): hashlib.sha256(p.read_bytes()).hexdigest() for p in production_paths()}
        after_inputs = fixture_hashes(args.fixture_manifest, args.sound_functions)
        after_inputs.update(canonical_hashes(args.old_c2_inputs))
        after_inputs.update(canonical_hashes(args.c2_m1_inputs, "contact281-c2-m1"))
        result["production_sha256_after"] = after
        result["input_sha256_after"] = after_inputs
        result["all_production_and_inputs_unchanged_during_controls"] = after == before and after_inputs == inputs
        if not result["all_production_and_inputs_unchanged_during_controls"]:
            result["status"] = "inconclusive-generation-changed"
        copies = output / "copies"
        if copies.exists():
            shutil.rmtree(copies)
        result["own_source_copies_removed"] = not copies.exists()
        (output / "result.json").write_text(json.dumps(result, indent=2) + "\n")
    return 0 if result["status"] == "passed-focused-not-full-M1" else 1

MUTANTS = (
    ("actual-wake-disconnected", "GameSimulation.Source.cs", "SourceContacts();",
     "/* isolated mutation: omitted actual detector wake */", "kernel", "actual kernel strict body10 boundary"),
    ("body-boundary-inclusive", "SourceContactDriver.Dispatch.cs", "distances[i, j] < 10f",
     "distances[i, j] <= 10f", "kernel", "actual kernel strict body10 boundary"),
    ("no-collision-flag-ignored", "SourceContactDriver.Dispatch.cs", "if ((Flags(a) & 0x20) != 0) continue;",
     "if ((Flags(a) & 0x20) == 0 && (Flags(a) & 0x20) != 0) continue;",
     "kernel", "actual kernel no body collision at active source PF_NOCOLL"),
    ("planar-not-original3D", "SourceContactDriver.Dispatch.cs", "Math.Sqrt(x * x + y * y + z * z)",
     "Math.Sqrt(x * x + z * z)", "kernel", "actual kernel no body collision at active source airborne3D"),
    ("busy-attack-filter", "SourceContactDriver.Dispatch.cs",
     "if (d.SourceAttackMode != 0 && AttackIntersects(model, d, a, low[i]))",
     "if ((Modes(d) & 2u) == 0 && d.SourceAttackMode != 0 && AttackIntersects(model, d, a, low[i]))",
     "kernel", "actual UNINT attack ANI_ATTACK_ON capsule executes during DEAD BALL"),
    ("wrong-hit-pointer", "SourceContactDriver.Dispatch.cs", "defender.SourceHitPartnerId = attacker.Id;",
     "defender.SourceHitPartnerId = defender.Id;", "kernel", "do_collision sets both real actor pointers"),
    ("wrong-puppet-facing", "GameSimulation.Contact.cs",
     "victim.Facing = attacker.Facing; victim.TargetFacing = attacker.TargetFacing;",
     "victim.Facing = (attacker.Facing + 256) & 1023; victim.TargetFacing = attacker.TargetFacing;",
     "kernel", "actual linked actors execute selected WAN frames"),
    ("capsule-pelvis-replaced", "SourceContactDriver.Dispatch.cs", "SourceVector3 pelvis = model.SourceCameraJoints[i, 0];",
     "SourceVector3 pelvis = model.SourceCameraJoints[i, 8];", "verify", "unchanged C ordered attack/body and real selection"),
    ("spin-probability-wrong", "SourceContactDriver.Dispatch.cs", "{ 60, 40, 20, 5 }",
     "{ 0, 0, 0, 0 }", "verify", "unchanged C ordered attack/body and real selection"),
    ("block-count-not-produced", "SourceContactDriver.Dispatch.cs", "rusher.SourceBlockCount++;",
     "rusher.SourceBlockCount += 0;", "verify", "unchanged C rusher block count"),
    ("clock-not-render-seconds", "SourceContactDriver.Dispatch.cs", "int seconds = m.SourceClock.PlayTime;",
     "int seconds = m.LiveTicks / SourceTiming.DefaultTicksPerSecond;", "verify",
     "unchanged C ordered attack/body and real selection"),
)
BOUNDARY_MUTANTS = (
    ("heading-blind-canonical-bearing", "SourceDroneAI.cs", "Actors[from].RawFacing - Angle(from, to)",
     "Actors[from].Facing - Angle(from, to)", "heading-boundary", "EXACT signed bearing"),
    ("heading-producer-drops-raw", "GameSimulation.Source.cs", "height: root.Y, rawFacing: actor.Facing);",
     "height: root.Y, rawFacing: actor.Facing & 1023);", "heading-boundary",
     "actual source443 signed TURNHACK actor remains RAW -66 across real source444 AI projection"),
    ("heading-receiver-omits-source-abs", "SourceDroneAI.cs", "Math.Abs(f.Bearing(e.Actor.Id, nearest)) <= 128",
     "f.Bearing(e.Actor.Id, nearest) <= 128", "heading-boundary",
     "actual receiver running-push buttons match original abs cone without RNG"),
    ("bearing-old-nonnegative-guard", "SourceMovementState.cs", "bearing < -1022 || bearing > 512",
     "bearing < 0 || bearing > 512", "movement-bearing", "actual source461 original signed neighbor rejected"),
    ("bearing-raw-producer-abs", "SourceMovementIntegration.cs",
     "neighbors[id] = new SourceMovementNeighbor(id, Model.SourceDistances[actor.Id, id], bearing);",
     "neighbors[id] = new SourceMovementNeighbor(id, Model.SourceDistances[actor.Id, id], Math.Abs(bearing));",
     "movement-bearing", "actual source461 raw -234/cache1010/distance98.68426 produces original SIGNED -220"),
    ("bearing-raw-producer-mask", "SourceMovementIntegration.cs",
     "int bearing = (1024 + actor.Facing - Model.SourceAngles[actor.Id, id]) % 1024;",
     "int bearing = (1024 + (actor.Facing & 1023) - Model.SourceAngles[actor.Id, id]) % 1024;",
     "movement-bearing", "actual source461 raw -234/cache1010/distance98.68426 produces original SIGNED -220"),
    ("bearing-raw-producer-clamp", "SourceMovementIntegration.cs",
     "neighbors[id] = new SourceMovementNeighbor(id, Model.SourceDistances[actor.Id, id], bearing);",
     "neighbors[id] = new SourceMovementNeighbor(id, Model.SourceDistances[actor.Id, id], Math.Max(0, bearing));",
     "movement-bearing", "actual source461 raw -234/cache1010/distance98.68426 produces original SIGNED -220"),
    ("bearing-headplow-abs", "SourceMovement.cs", "neighbor.Bearing < 64 && ++count >= 2",
     "Math.Abs(neighbor.Bearing) < 64 && ++count >= 2", "movement-bearing",
     "actual original standard_run signed action/draw sequence"),
    ("bearing-unqualified-lower-endpoint", "SourceMovementState.cs", "bearing < -1022 || bearing > 512",
     "bearing < -1023 || bearing > 512", "movement-bearing", "M1 exact active producer bearing guard -1023"),
)
ROUTE_MUTANTS = (
    ("route-old-canonical-guard-caller", "GameSimulation.Source.cs",
     "targetFacing: actor.TargetFacing & 1023, rawTargetFacing: actor.TargetFacing,",
     "targetFacing: actor.TargetFacing, rawTargetFacing: actor.TargetFacing,",
     "route-target", "actual source858 raw target rejected before REAL AI dispatch"),
    ("route-producer-drops-raw", "GameSimulation.Source.cs",
     "targetFacing: actor.TargetFacing & 1023, rawTargetFacing: actor.TargetFacing,",
     "targetFacing: actor.TargetFacing & 1023, rawTargetFacing: null,",
     "route-target", "constructor receives RAW -437/canonical587, not dropped/masked raw"),
    ("route-canonical-instead-raw-consumer", "SourceDroneAI.cs",
     "Math.Abs(toBall - e.Input.RawTargetFacing) < 64",
     "Math.Abs(toBall - e.Input.TargetFacing) < 64",
     "route-target", "ACTUAL original loose-ball linear-raw commands/buttons/stick"),
    ("route-raw-minus-one-as-missing", "SourceDroneAI.cs",
     "RawTargetFacing = rawTargetFacing ?? TargetFacing;",
     "RawTargetFacing = rawTargetFacing == -1 ? frame.Actors[actorId].Facing : rawTargetFacing ?? TargetFacing;",
     "route-target", "explicit RAW -1 is distinct from omitted/default -1 sentinel"),
    ("route-circular-fold-consumer", "SourceDroneAI.cs",
     "Math.Abs(toBall - e.Input.RawTargetFacing) < 64",
     "Math.Min(Math.Abs(toBall - e.Input.RawTargetFacing) % 1024, 1024 - Math.Abs(toBall - e.Input.RawTargetFacing) % 1024) < 64",
     "route-target", "ACTUAL original loose-ball linear-raw commands/buttons/stick"),
    ("route-raw-coherence-removed", "SourceDroneAI.cs",
     "(rawTargetFacing.Value & 1023) != TargetFacing", "false",
     "route-target", "route target canonical/raw coherence is mandatory"),
    ("route-unqualified-raw-minus1024", "SourceDroneAI.cs",
     "rawTargetFacing.Value < -1023", "rawTargetFacing.Value < -1024",
     "route-target", "route target strict original raw range -1024"),
)
ROUTE_SOURCE_PINS = {
    "playbook.c": "bfcdf44ce1a11a69ba1881ff2310d6b1aed5612b",
    "PlayStuf.c": "12197b46ea73cb2563fcb80b8c61c4f1e819b314",
    "playsel.c": "f5e87b921a4038d03f55174bbd5425241939e798",
    "include/PlayStuf.h": "3df5e8e8a3f478c3b02860a401c950d1e83c5ad3",
    "include/player.h": "a86b1a5b52ba842b206cd7dd0bbb26521db6dcb8",
    "player.c": "5744c51f30b625b9f80b92388ab49ff0ebc5bb2c",
    "handicap.c": "871424596fb9786af3602a49cc9d20c466df8ca1",
    "lib/ani3d/TRIGS.H": "a07d76798931f1240bdf7f3b8e753ebd7832763a",
    "drone.c": "d4b636dcd8cc34109be5834b2be2b18b7c04439a",
    "include/GAME.H": "09d65c92971c32c44b1dc49fb4e4e98b371b1cd3",
    "nfl.c": "15b49474fb55e8bac280b6515fcf7bf59cdf8664",
    "include/ani3d.h": "826c6322467fcfe503b8402b540e4731671f9b6b",
    "lib/ani3d/pipe3D.c": "0966ecaf0d7eef7d9c635a97899a421ce1eeea41",
    "lib/ani3d/ANIM.C": "97d7856d67a432937e249be274ef67a9ad354dc1",
}


def route_recipe_pins(recipe):
    profile = recipe.get("profile", {})
    if (recipe.get("repository") != "historicalsource/nflblitz2000"
            or recipe.get("revision") != "89f840d58d152a2a9f230cb8d0cdffbd554c8ded"
            or recipe.get("tree") != "a7e0352125d46075b7d98e87253ab89b9160a4c7"
            or not isinstance(profile, dict)
            or profile.get("identity") != "OriginalRelease57Hz-USE_OLD_QB_FALSE"
            or type(profile.get("ticks_per_second")) is not int or profile["ticks_per_second"] != 57
            or profile.get("use_old_qb") is not False
            or recipe.get("blobs") != ROUTE_SOURCE_PINS):
        raise RuntimeError("Qualified original14 P1 route provenance/profile/exact pins differ; no movement13 alias.")
    return dict(ROUTE_SOURCE_PINS)


def route_original_hashes(source_root):
    source_root = Path(source_root).absolute()
    if ".." in source_root.parts or not (
            source_root.is_relative_to(ROOT / ".squad/.scratch") or source_root.is_relative_to(ARTIFACTS)):
        raise RuntimeError("Route original14 requires an explicit local ignored input root, never another-worktree fallback.")
    for part in (source_root, *source_root.parents):
        if part.is_symlink():
            raise RuntimeError("Route original14 symlink inputs are forbidden.")
        if part == ROOT:
            break
    pins = route_recipe_pins(json.loads((ROOT / "tests/playbooks/pins.json").read_bytes()))
    actual = {p.relative_to(source_root).as_posix()
              for p in source_root.rglob("*") if p.is_file() or p.is_symlink()}
    if actual != set(pins):
        raise RuntimeError("Route original14 inventory differs: missing=" + str(sorted(set(pins) - actual))
                           + "; unexpected=" + str(sorted(actual - set(pins))))
    result = {}
    for name, expected in pins.items():
        path = source_root / name
        if path.is_symlink() or not path.resolve().is_relative_to(source_root.resolve()):
            raise RuntimeError("Route original14 escaping/symlink file: " + name)
        data = path.read_bytes()
        if hashlib.sha1(b"blob " + str(len(data)).encode() + b"\0" + data).hexdigest() != expected:
            raise RuntimeError("Original14 route-target Git blob pin differs: " + name)
        result[str(path)] = hashlib.sha256(data).hexdigest()
    return result


def movement_original_hashes(source_root):
    source_root = Path(source_root).absolute()
    pins = json.loads((ROOT / "tests/movement/pins.json").read_bytes())
    if (pins["revision"] != "89f840d58d152a2a9f230cb8d0cdffbd554c8ded"
            or len(pins["blobs"]) != 13
            or pins["blobs"].get("include/handicap.h") != "45eef32b32721196e4575e59a0fd2ce01e0fbbdb"
            or pins["blobs"].get("plyrinfo.c") != "e955e1c98a0fd0123ce0b865ddafdfd7c32c59d4"):
        raise RuntimeError("Complete original13 M1 source recipe differs; no partial-root fallback.")
    result = {}
    for name, expected in pins["blobs"].items():
        path = source_root / name
        if Path(name).is_absolute() or ".." in Path(name).parts or path.is_symlink():
            raise RuntimeError("Nonportable original M1 source input: " + name)
        data = path.read_bytes()
        if hashlib.sha1(b"blob " + str(len(data)).encode() + b"\0" + data).hexdigest() != expected:
            raise RuntimeError("Original13 movement-bearing blob pin differs: " + name)
        result[str(path)] = hashlib.sha256(data).hexdigest()
    return result


def sources():
    return production_paths()


def hashes(paths):
    return {path.relative_to(ROOT).as_posix(): hashlib.sha256(path.read_bytes()).hexdigest() for path in paths}


def require_pass(code, summary, mode, canonical=False):
    expected = 34934 if mode == "c2" else EXPECTED_ASSERTIONS[mode]
    current281 = canonical or mode == "c2"
    if canonical and mode == "kernel":
        expected += 22
    if (code != 0 or not isinstance(summary, dict) or summary.get("schema") != "source-contact-checks/v1"
            or summary.get("status") != "passed" or summary.get("assertions") != expected
            or summary.get("source_programs") != 131 or summary.get("source_instructions") != 2147
            or summary.get("base157_sha256") != "f0ae9395e57ae8327b7b55493dd6c457102adb4a13ecbb581e3551d400a3800e"
            or summary.get("extra115_sha256") != "907e2d368394e5c9db1bebdf218abae62bb3b841230293670cb3d2db48e05fcd"
            or summary.get("production272_sha256") != CONTACT_SHA
            or summary.get("production_profile") != ("original-teams-contact281-c2" if current281 else "original-teams-contact272")
            or summary.get("actual_animation_clips") != (281 if current281 else 272)
            or summary.get("actual_animation_frames") != (26230 if current281 else 24892)
            or current281 and summary.get("c2_animation_sha256") != C2_ANIMATION_SHA
            or summary.get("actual_bound_SAFN") != SOUND_SHA
            or summary.get("full_contact_or_game_parity") is not False):
        raise RuntimeError("Missing actual nonempty source contact " + mode + " evidence; errors/crashes are not passes.")
    if canonical and mode == "kernel" and (
            summary.get("added_canonical_result_timing_assertions") != 22
            or summary.get("retained_C1_assertions") != EXPECTED_ASSERTIONS["kernel"]):
        raise RuntimeError("All retained C1 kernel assertions plus22 exact C2 queued/next-wake assertions are required.")
    if mode == "c2" and (summary.get("c2_callback_data_sha256") != C2_DATA_SHA
                         or summary.get("c2_hit_extension_sha256") != C2_HIT_SHA
                         or summary.get("c2_bounded_evidence", {}).get("native_callback_cases") != 2146
                         or summary.get("c2_bounded_evidence", {}).get("native_speech_cases") != 8000):
        raise RuntimeError("Complete current C2 original-C data/callback/speech inventory differs.")
    if mode in ("emit", "verify") and (
            summary.get("emitted_detector_cases" if mode == "emit" else "verified_detector_cases") != 1152
            or summary.get("emitted_segment_cases" if mode == "emit" else "verified_segment_cases") != 1024):
        raise RuntimeError("Actual complete1152 detector/1024 segment inventory differs.")
    if mode == "kernel" and (summary.get("kernel_scenarios") != 22
                             or summary.get("actual_kernel_puppet_callbacks", 0) < 296):
        raise RuntimeError("Actual complete kernel scenario inventory differs.")


def main():
    global ARTIFACTS
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source-root", type=Path, required=True)
    parser.add_argument("--operand-root", type=Path, required=True)
    parser.add_argument("--camera-source-root", type=Path, required=True)
    parser.add_argument("--movement-source-root", type=Path, required=True,
                        help="Explicit complete13-file M1 original root, including pinned include/handicap.h and plyrinfo.c.")
    parser.add_argument("--route-original-root", type=Path, required=True,
                        help="Explicit qualified14-file P1 original root, NOT movement13 plus drone; every named blob verified.")
    parser.add_argument("--fixture-manifest", type=Path, required=True)
    parser.add_argument("--input-profile", choices=(PROFILE,), required=True,
                        help="Explicit new production generation; old157 compositions are not accepted.")
    parser.add_argument("--sound-functions", type=Path, required=True)
    parser.add_argument("--c2-inputs", type=Path, help="Explicit frozen canonical288 outputs; no fixture/profile alias.")
    parser.add_argument("--c2-process-root", type=Path,
                        help="Explicit pinned original process.c/mthread.c input root for the tracked native C2 generator.")
    parser.add_argument("--output", type=Path, help="New ignored output directory; preserve historical receipts.")
    parser.add_argument("--no-mutations", action="store_true")
    args = parser.parse_args()
    if (args.c2_inputs is None) != (args.c2_process_root is None):
        parser.error("--c2-inputs and --c2-process-root must be supplied together.")
    if args.output is not None:
        selected = args.output.absolute()
        if not selected.is_relative_to(SUITE / ".artifacts") or selected.exists():
            parser.error("--output requires a new local tests/contact/.artifacts child.")
        ARTIFACTS = selected
    ARTIFACTS.mkdir(parents=True, exist_ok=True)
    fixture_profile(args.fixture_manifest, args.input_profile)
    c2_native = ARTIFACTS / "native-c2"
    if args.c2_inputs is not None:
        canonical_hashes(args.c2_inputs)
        commands, native_c2_preparation = prepare_original_oracles(args.source_root, args.c2_process_root, c2_native)
    else:
        commands = []
    def actual_inputs():
        result = fixture_hashes(args.fixture_manifest.resolve(), args.sound_functions.resolve())
        if args.c2_inputs is not None:
            result.update(canonical_hashes(args.c2_inputs))
            for root in (args.c2_process_root, c2_native):
                for path in sorted(root.rglob("*")):
                    if path.is_file():
                        result[str(path.absolute())] = hashlib.sha256(path.read_bytes()).hexdigest()
        result.update(verified_original_hashes(args.source_root, args.input_profile))
        result.update(movement_original_hashes(args.movement_source_root))
        result.update(route_original_hashes(args.route_original_root))
        route_recipe = ROOT / "tests/playbooks/pins.json"
        result[str(route_recipe)] = hashlib.sha256(route_recipe.read_bytes()).hexdigest()
        for path in (args.operand_root / "include/ani3d.equ", args.operand_root / "include/player.equ",
                     args.operand_root / "include/sndcalls.equ", args.camera_source_root / "lib/ani3d/ANIM.C"):
            result[str(path.resolve())] = hashlib.sha256(path.read_bytes()).hexdigest()
        program_input = ARTIFACTS / "source-programs.json"
        if program_input.exists():
            result[str(program_input)] = hashlib.sha256(program_input.read_bytes()).hexdigest()
        return result
    translated = translate(read(args.source_root), args.operand_root)
    expected = csharp(translated)
    if (ROOT / "Assets/Scripts/Gameplay/SourceAnimationPrograms.Contact.cs").read_text() != expected:
        raise RuntimeError("Production C1 ANI program differs from precise pinned original operands/order.")
    (ARTIFACTS / "source-programs.json").write_text(json.dumps(translated, indent=2) + "\n")
    input_before = actual_inputs()
    workspace = ARTIFACTS / ("isolated-" + uuid.uuid4().hex)
    work = ARTIFACTS / "work"
    work.mkdir(exist_ok=True)
    environment = dict(os.environ, TMPDIR=str(work), TMP=str(work), TEMP=str(work),
                       DOTNET_CLI_TELEMETRY_OPTOUT="1", DOTNET_NOLOGO="1",
                       DOTNET_CLI_HOME=str(ARTIFACTS / "dotnet-home"), NUGET_PACKAGES=str(ARTIFACTS / "nuget"),
                       PYTHONDONTWRITEBYTECODE="1")
    for key in ("DOTNET_CLI_HOME", "NUGET_PACKAGES"):
        Path(environment[key]).mkdir(exist_ok=True)
    snapshot = {path.relative_to(ROOT).as_posix(): path.read_bytes() for path in sources()}
    before = hashes(sources())
    if before != {name: hashlib.sha256(data).hexdigest() for name, data in snapshot.items()}:
        raise RuntimeError("Actual source tree changed while capturing a coherent C1 source snapshot.")
    evidence = {"schema": "source-contact-regression/v1", "status": "failed", "commands": commands, "mutants": [],
                "source_sha256_before": before, "full_contact_or_game_parity": False,
                "native_or_browser_acceptance": False, "resource_profiles_changed": False,
                "input_profile": args.input_profile,
                "actual_input_sha256_before": input_before}
    evidence["canonical281_requested"] = args.c2_inputs is not None
    if args.c2_inputs is not None:
        evidence["tracked_C2_oracle_preparation"] = native_c2_preparation
    evidence["historical272_boundary_modes_retained"] = ["inputs", "heading-boundary", "movement-bearing", "route-target"]

    def canonical_mode(mode):
        return args.c2_inputs is not None and mode in ("emit", "verify", "kernel", "sound-short")

    def execute(label, mode, source_root=ROOT):
        target = ARTIFACTS / "checks" if mode in ("emit", "verify") else ARTIFACTS / label
        build_root = workspace / label / "build"
        command = ["dotnet", "build", str(SUITE / "ContactChecks.csproj"), "--no-restore",
                   "--configuration", "Release", "--verbosity", "quiet",
                   "-p:BlitzSourceRoot=" + str(source_root) + "/",
                   "-p:BaseIntermediateOutputPath=" + str(build_root / "obj") + "/",
                   "-p:BaseOutputPath=" + str(build_root / "bin") + "/"]
        evidence["commands"].append(command)
        process = subprocess.run(command, cwd=ROOT, env=environment, capture_output=True, text=True, timeout=240)
        compile_text = process.stdout + process.stderr
        (ARTIFACTS / (label + "-compile.log")).write_text(compile_text)
        if process.returncode and "NETSDK1004" in compile_text:
            feed = workspace / "empty-feed"; feed.mkdir(parents=True, exist_ok=True)
            restore = ["dotnet", "restore", str(SUITE / "ContactChecks.csproj"), "--source", str(feed),
                       "-p:NuGetAudit=false", "-p:BaseIntermediateOutputPath=" + str(build_root / "obj") + "/",
                       "--verbosity", "quiet"]
            evidence["commands"].append(restore)
            restored = subprocess.run(restore, cwd=ROOT, env=environment, capture_output=True, text=True, timeout=240)
            (ARTIFACTS / (label + "-restore.log")).write_text(restored.stdout + restored.stderr)
            if restored.returncode:
                raise RuntimeError("Existing SDK dependency restore failed: " + label)
            process = subprocess.run(command, cwd=ROOT, env=environment, capture_output=True, text=True, timeout=240)
            (ARTIFACTS / (label + "-compile-restored.log")).write_text(process.stdout + process.stderr)
        if process.returncode:
            raise RuntimeError("Successful isolated compile required; compile errors are NOT mutation kills: " + label)
        command = ["dotnet", str(build_root / "bin/Release/net10.0/ContactChecks.dll"),
                   str(args.fixture_manifest.resolve()), str(ARTIFACTS), mode, str(target),
                   str(args.sound_functions.resolve())]
        if mode in ("heading-boundary", "movement-bearing", "route-target"):
            originals = {"heading-boundary": args.source_root, "movement-bearing": args.movement_source_root,
                         "route-target": args.route_original_root}
            command.append(str(originals[mode].resolve()))
        if mode == "c2":
            command += [str(args.source_root.absolute()), str(args.c2_inputs.absolute()), str(c2_native)]
        elif canonical_mode(mode):
            command += ["--c2-inputs", str(args.c2_inputs.absolute())]
        evidence["commands"].append(command)
        process = subprocess.run(command, cwd=ROOT, env=environment, capture_output=True, text=True, timeout=240)
        (ARTIFACTS / (label + ".log")).write_text(process.stdout + process.stderr)
        rows = [json.loads(line) for line in process.stdout.splitlines() if line.startswith('{"schema":')]
        return process.returncode, rows[0] if len(rows) == 1 else None, process.stdout + process.stderr

    exit_code = 1
    try:
        native, receipt = build(args.source_root, args.operand_root, args.camera_source_root, ARTIFACTS / "native")
        evidence["original_c"] = receipt
        code, summary, text = execute("baseline-inputs", "inputs")
        evidence["baseline_inputs"] = summary
        require_pass(code, summary, "inputs")
        for mode in ("heading-boundary", "movement-bearing", "route-target"):
            code, summary, text = execute("baseline-" + mode, mode)
            evidence["baseline_" + mode] = summary
            try:
                require_pass(code, summary, mode, canonical_mode(mode))
            except RuntimeError:
                print(text[-10000:]); raise
        code, summary, text = execute("emit", "emit")
        evidence["emit"] = summary
        try:
            require_pass(code, summary, "emit", canonical_mode("emit"))
        except RuntimeError:
            print(text[-8000:]); raise
        inputs = ARTIFACTS / "checks/original-inputs.tsv"
        results = ARTIFACTS / "checks/original-results.tsv"
        with inputs.open("rb") as incoming, results.open("wb") as outgoing:
            process = subprocess.run([str(native)], stdin=incoming, stdout=outgoing, stderr=subprocess.PIPE,
                                     cwd=ROOT, env=environment, timeout=120)
        (ARTIFACTS / "native-execution.log").write_bytes(process.stderr)
        if process.returncode:
            raise RuntimeError("Actual original C contact execution failed; not an accepted boundary result.")
        evidence["oracle_inputs_sha256"] = hashlib.sha256(inputs.read_bytes()).hexdigest()
        evidence["oracle_results_sha256"] = hashlib.sha256(results.read_bytes()).hexdigest()
        evidence["original_c_execution_exit_code"] = process.returncode
        for mode in ("verify", "kernel"):
            code, summary, text = execute("baseline-" + mode, mode)
            evidence["baseline_" + mode] = summary
            try:
                require_pass(code, summary, mode, canonical_mode(mode))
            except RuntimeError:
                print(text[-8000:]); raise
        def compare_actual_puppet(label):
            root = ARTIFACTS / label
            inputs, actual = root / "actual-kernel-puppet-inputs.tsv", root / "actual-kernel-puppet-outputs.tsv"
            expected = root / "unchanged-C-puppet-outputs.tsv"
            with inputs.open("rb") as incoming, expected.open("wb") as outgoing:
                subprocess.run([str(native)], stdin=incoming, stdout=outgoing, cwd=ROOT, env=environment,
                               check=True, timeout=30)
            original_rows, actual_rows = expected.read_text().splitlines(), actual.read_text().splitlines()
            if len(actual_rows) < 296 or original_rows != actual_rows:
                mismatch = next((i for i, pair in enumerate(zip(original_rows, actual_rows)) if pair[0] != pair[1]), -1)
                raise RuntimeError("Actual kernel puppet root/position/velocity/facing differs from unchanged ANIM.C at row " + str(mismatch))
            return {"actual_kernel_callbacks": len(actual_rows), "exact_original_C_binary32_and_heading_assertions": 11 * len(actual_rows),
                    "actual_inputs_sha256": hashlib.sha256(inputs.read_bytes()).hexdigest(),
                    "actual_outputs_sha256": hashlib.sha256(actual.read_bytes()).hexdigest(),
                    "unchanged_C_outputs_sha256": hashlib.sha256(expected.read_bytes()).hexdigest()}
        evidence["actual_kernel_original_C_puppet"] = compare_actual_puppet("baseline-kernel")
        code, summary, text = execute("baseline-sound-integration", "sound-short")
        evidence["baseline_sound_integration"] = summary
        require_pass(code, summary, "sound-short", canonical_mode("sound-short"))
        if summary.get("sound_short_scenarios") != 3 or summary.get("actual_synchronous_tuple_lines") != [2448, 2461, 5544, 5555, 15955, 15998]:
            raise RuntimeError("All actual C1+Sound six tuple/partial FIFO scenarios are required in addition to the full22.")
        if args.c2_inputs is not None:
            code, summary, text = execute("baseline-c2", "c2")
            evidence["baseline_c2"] = summary
            require_pass(code, summary, "c2")
        if not args.no_mutations:
            for name, filename, anchor, replacement, mode, assertion in MUTANTS + BOUNDARY_MUTANTS + ROUTE_MUTANTS:
                root = workspace / name / "source"
                for relative, data in snapshot.items():
                    destination = root / relative
                    destination.parent.mkdir(parents=True, exist_ok=True)
                    destination.write_bytes(data)
                target = root / "Assets/Scripts/Gameplay" / filename
                data = target.read_bytes()
                if data.count(anchor.encode()) != 1:
                    raise RuntimeError("Actual C1 mutation anchor must be unique: " + name)
                changed = data.replace(anchor.encode(), replacement.encode())
                target.write_bytes(changed)
                code, summary, text = execute(name, mode, root)
                item = {"id": name, "source": str(target.relative_to(root)), "mode": mode,
                        "mutated_sha256": hashlib.sha256(changed).hexdigest(),
                        "compiled": True,
                        "exit_code": code, "summary": summary, "expected_assertion": assertion}
                evidence["mutants"].append(item)
                if (code != 1 or not isinstance(summary, dict) or summary.get("status") != "failed"
                        or summary.get("schema") != "source-contact-checks/v1" or summary.get("assertions", 0) <= 0
                        or assertion not in summary.get("failed_assertion", "")):
                    print(text[-8000:])
                    raise RuntimeError("Actual compiled C1 mutant did not fail the specified causal assertion: " + name)
                item["named_assertion_rejected"] = True
                print("PASS actual C1 mutant", name, "->", summary["failed_assertion"])
                if hashes(sources()) != before:
                    raise RuntimeError("Production changed while C1 mutants ran in isolated copies.")
            if args.c2_inputs is not None:
                for name, relative, anchor, replacement, assertion in c2_specifications(ROOT):
                    root = workspace / ("c2-" + name) / "source"
                    for path, data in snapshot.items():
                        destination = root / path
                        destination.parent.mkdir(parents=True, exist_ok=True)
                        destination.write_bytes(data)
                    target = root / relative
                    data = target.read_bytes()
                    if data.count(anchor.encode()) != 1:
                        raise RuntimeError("Actual C2 mutation anchor must be unique: " + name)
                    changed = data.replace(anchor.encode(), replacement.encode())
                    target.write_bytes(changed)
                    code, summary, text = execute("c2-" + name, "c2", root)
                    item = {"id": name, "generation": "current-C2", "source": relative, "mode": "c2",
                            "source_sha256": before[relative], "mutated_sha256": hashlib.sha256(changed).hexdigest(),
                            "compiled": True, "exit_code": code, "summary": summary, "expected_assertion": assertion}
                    evidence["mutants"].append(item)
                    if (code != 1 or not isinstance(summary, dict) or summary.get("status") != "failed"
                            or summary.get("schema") != "source-contact-checks/v1" or summary.get("assertions", 0) <= 0
                            or assertion not in summary.get("failed_assertion", "")):
                        print(text[-8000:])
                        raise RuntimeError("Actual compiled C2 control did not fail its precise causal assertion: " + name)
                    item["named_assertion_rejected"] = True
                    print("PASS actual C2 mutant", name, "->", summary["failed_assertion"])
                    if hashes(sources()) != before or actual_inputs() != input_before:
                        raise RuntimeError("Production or actual original/canonical input closure changed during C2 controls.")
        for mode in ("inputs", "verify", "sound-short", "heading-boundary", "movement-bearing", "route-target"):
            code, summary, text = execute("final-" + mode, mode)
            evidence["final_" + mode] = summary
            require_pass(code, summary, mode, canonical_mode(mode))
        if args.c2_inputs is not None:
            code, summary, text = execute("final-c2", "c2")
            evidence["final_c2"] = summary
            require_pass(code, summary, "c2")
        code, summary, text = execute("final-kernel", "kernel")
        evidence["final_kernel"] = summary
        require_pass(code, summary, "kernel", canonical_mode("kernel"))
        evidence["final_actual_kernel_original_C_puppet"] = compare_actual_puppet("final-kernel")
        evidence["status"] = "passed"; exit_code = 0
    except (OSError, RuntimeError, ValueError, subprocess.TimeoutExpired) as error:
        evidence["error"] = str(error)
        print("C1 REGRESSION FAILED:", error, file=sys.stderr)
    finally:
        if workspace.exists():
            shutil.rmtree(workspace)
        evidence["isolated_source_builds_removed"] = not workspace.exists()
        evidence["source_sha256_after"] = hashes(sources())
        evidence["production_unchanged"] = before == evidence["source_sha256_after"]
        evidence["actual_input_sha256_after"] = actual_inputs()
        evidence["actual_inputs_unchanged"] = input_before == evidence["actual_input_sha256_after"]
        if not evidence["production_unchanged"] or not evidence["actual_inputs_unchanged"]:
            evidence["status"] = "inconclusive-source-or-input-changed"; exit_code = 1
        (ARTIFACTS / "result.json").write_text(json.dumps(evidence, indent=2) + "\n")
    return exit_code


if __name__ == "__main__":
    sys.exit(run_m1_sound(sys.argv[1:]) if "--m1-running-sound-only" in sys.argv else main())
