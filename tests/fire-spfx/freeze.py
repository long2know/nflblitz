#!/usr/bin/env python3
"""Freeze the implemented-source handoff only after current production proofs pass."""
import argparse
from datetime import datetime, timezone
import hashlib
import json
from pathlib import Path
import subprocess

ROOT = Path(__file__).resolve().parents[2]
BASE = "5f570c132288871de2d1461860e19f85975caf5f"
FROZEN = {
    "source-fire-spfx-contract.json": "a1aa94043f367ca68892cb56d580e95073e091b4a5fcfb6e0032ec3080487f13",
    "source-fire-spfx-dependencies.json": "79d4e2273da80f964212f14b193a65e891aecc49334022751a9ea4e0ff421baa",
    "source-fire-spfx-acquired-inputs.json": "276d8db7a5a4227227d01e0f68341f017ad31328b355b11dbef3019e85afa88d",
}
HOOKS = [
    {"ownerFile": "Assets/Scripts/Gameplay/GameSimulation.Source.cs", "point": "SourceStep native-level0 slice before camera, including menus",
     "api": "BeginSourceTick(SourceFireTickInput, SourceFireHostCallbacks)", "requirement": "Merge actual game_proc and individual C2-whistle slots with known FX nodes; retain declared portable epoch/order or externally verified witness. Do not move all FX after game_proc or ball."},
    {"ownerFile": "Assets/Scripts/Gameplay/SourceC2Callbacks.cs", "point": "Individual existing whistle continuation",
     "api": "SourceFireWakeSlot(HostContinuation,...)", "requirement": "Run existing continuation at its actual declared native0 slot; no second always-before-FX phase controller."},
    {"ownerFile": "Assets/Scripts/Gameplay/SourceMovementIntegration.cs", "point": "Reached _mode_wpnps and _mode_dbps tails",
     "api": "WeaponPreSnapTail / DefensivePreSnapTail", "requirement": "Pass actual raw predicates; helper owns shared almost/fire/db latches. No extra human/phase guards."},
    {"ownerFile": "Assets/Scripts/Gameplay/SourceMovementIntegration.cs", "point": "Reached _mode_qb tail and carrier _mode_wpn fire point after evasion before later control block",
     "api": "MaybePlayerFire(SourceFireSite, SourceFirePlayerEmitterInput)", "requirement": "Native vx/vz, station, dragging/flip, lower reacting, actual offense/QB mode. Eligible randrng4 continues current Core SourceDraw context before modulo."},
    {"ownerFile": "Assets/Scripts/Gameplay/GameSimulation.Source.cs", "point": "Successful offensive standard_catch after mode/control updates",
     "api": "SuccessfulOffensiveCatch", "requirement": "Preserve potential-fire burst before controlled completion; duplicate players192 calls. Not OnFire edge detection."},
    {"ownerFile": "Assets/Scripts/Gameplay/GameSimulation.Source.cs", "point": "Original second-loss SPFX site only, after actual Core state changes",
     "api": "DefensiveSecondLossFireSite", "requirement": "Actual selected announcer bank and selected player3-or10's known hit partner. Unknown nullable partner is not known NULL(-1). Unconditional185 precedes conditional branch."},
    {"ownerFile": "Assets/Scripts/Gameplay/GameSimulation.Source.cs", "point": "ball_proc free/phantom branch after physics and flight_time increment",
     "api": "BallFreeOrPhantomStep(SourceFireSite, SourceFireBallEmitterInput)", "requirement": "Map ball kind by name, keep Bobble private selector, emit exact PLAYER triplet on even flight. Host retains original nonfire trail/BF_WHITE branches; explicit ResetBallProcessEffect only on real ball_proc entry."},
    {"ownerFile": "Assets/Scripts/Gameplay/GameSimulation.Source.cs", "point": "Actual reset/audible/selection/reinit/lineup sites",
     "api": "Apply(SourceFireCommand.Control(...))", "requirement": "Delete/kill order differs at clobber_playsel; interactive selection kills PFLASH, suspends0/0x8000, resumesSPFX, hides existing; exit resumes/unhides. Clear raw0x400 and lineup latches at their real sites."},
    {"ownerFile": "Assets/Scripts/Gameplay/SourceMovementIntegration.cs", "point": "CacheSourcePlayerCameraJoints producer",
     "api": "ReadCachedPlayer -> SourceFireCachedPlayerFrame(actor, cacheTick, pairedCamera, jpos15, provenance, retentionEvidence)",
     "requirement": "Retain immutable historical pair before current camera overwrite; tick equality and explicit older-cache retention evidence mandatory. No SourceWorldJoint re-pose/identity fallback."},
    {"ownerFile": "Assets/Scripts/Runtime/BlitzRuntime.cs", "point": "After successful source phases, also during menu elapsed wakes",
     "api": "CaptureFrame -> SourceFireSpfxRenderer.ConsumeAfterSourceStep", "requirement": "Bind actual authenticated asset/camera, hold prior frame on skipped host frames, never age FX in Update. Source57 opt-in must reject missing runtime/data. Legacy/default profiles unchanged."},
    {"ownerFile": "Existing player material consumer; separate lease required", "point": "Original draw_player PF_FLASHWHITE branch",
     "api": "SourceFirePlayerFlashState(rawFlags)", "requirement": "Only raw0x400; DECAL_TEXTURE_ADD_CCALPHA constant0xc0000000; source restore TEXTURE_TIMES_ALPHA/0xff000000. No tint substitute."},
]


def fingerprint(path):
    raw = path.read_bytes()
    return {"path": str(path), "bytes": len(raw), "sha256": hashlib.sha256(raw).hexdigest()}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--bundle", type=Path, required=True)
    parser.add_argument("--frozen-inputs-root", type=Path, required=True)
    parser.add_argument("--production-proof", type=Path, required=True)
    parser.add_argument("--api-proof", type=Path, required=True)
    parser.add_argument("--inputs-proof", type=Path, required=True)
    parser.add_argument("--mutations-proof", type=Path, required=True)
    parser.add_argument("--output", type=Path, default=ROOT / ".squad/.scratch/source-fire-renderer-handoff.json")
    args = parser.parse_args()
    output = args.output.resolve()
    assert output == ROOT / ".squad/.scratch/source-fire-renderer-handoff.json" and not output.exists()
    assert subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=ROOT, text=True).strip() == BASE
    assert not subprocess.check_output(["git", "diff", "--name-only"], cwd=ROOT, text=True).strip(), "No existing production file may change in this lease."
    receipts, evidence = {}, {}
    for key, path in (("production", args.production_proof), ("api", args.api_proof), ("inputs", args.inputs_proof), ("mutations", args.mutations_proof)):
        path = path.resolve()
        assert path.is_relative_to(ROOT / "tests/fire-spfx/.artifacts")
        receipts[key] = fingerprint(path); evidence[key] = json.loads(path.read_text())
        assert evidence[key]["status"] == "passed", key + " must pass before freezing"
    actual_sources = {p.relative_to(ROOT).as_posix(): hashlib.sha256(p.read_bytes()).hexdigest()
                      for p in (ROOT / "Assets/Scripts").rglob("*.cs")}
    assert actual_sources == evidence["production"]["sourceHashes"] == evidence["api"]["source_inventory_sha256"]
    assert len(actual_sources) == 120 and len(evidence["api"]["profiles"]) == 5
    assert evidence["mutations"]["compiledKilled"] == 13 and evidence["mutations"]["productionUnchanged"]
    assert evidence["inputs"]["originals_unchanged"] and evidence["inputs"]["inventory"]["baseline_metadata_preserved"] == 124
    frozen_inputs = []
    for name, expected in FROZEN.items():
        row = fingerprint(args.frozen_inputs_root.resolve() / name)
        assert row["sha256"] == expected, "Frozen input changed: " + name
        frozen_inputs.append(row)
    bundle = args.bundle.resolve()
    manifest_path = bundle / "manifest.json"; manifest = json.loads(manifest_path.read_text())
    assert fingerprint(manifest_path)["sha256"] == evidence["production"]["manifestSha256"]
    assert manifest["inputOrigin"] == "original-authoring-TGA32-and-SDL-portable-not-WMS"
    assert not manifest["nativeConvertedTextureEquivalent"] and not manifest["generatedSpfxHeaderEquivalent"]
    assert len(manifest["inputs"]) == 18 and len(manifest["frames"]) == 17 and manifest["meshBytes"] == 148
    dataset = []
    for item in manifest["inputs"]:
        row = fingerprint(bundle / item["payloadPath"])
        assert row["sha256"] == item["sha256"] and row["bytes"] == item["bytes"]
        assert evidence["inputs"]["assetPins"][item["path"]] == row["sha256"]
        dataset.append(dict(row, sourcePath=item["path"], gitBlobSha1=item["gitBlobSha1"]))
    new_files = subprocess.check_output(["git", "ls-files", "--others", "--exclude-standard"], cwd=ROOT, text=True).splitlines()
    allowed = ("Assets/Scripts/Contracts/SourceFireSpfx", "Assets/Scripts/Runtime/SourceFireSpfx",
               "Assets/Scripts/AssetImport/SourceFireSpfx", "Assets/Scripts/Presentation/SourceFireSpfx",
               "Assets/Scripts/Editor/SourceFireSpfx", "tests/fire-spfx/", "tools/fire-spfx/")
    assert new_files and all(path.startswith(allowed) for path in new_files)
    files = [dict(fingerprint(ROOT / path), relativePath=path) for path in sorted(new_files)]
    proof = evidence["production"]
    result = {
        "schema": "source-fire-renderer-implementation-handoff/v1",
        "owner": "Unity Systems", "requester": "Stephen Long", "createdUtc": datetime.now(timezone.utc).isoformat(),
        "status": "implemented-runtime-and-renderer-producer-integration-pending",
        "wholeUserTaskComplete": False, "worktree": str(ROOT), "base": BASE,
        "branch": subprocess.check_output(["git", "branch", "--show-current"], cwd=ROOT, text=True).strip(),
        "oldTrackedFilesChanged": [], "oldDefaultAndStrictProfilesModified": False,
        "publication": {"staged": False, "committed": False, "pushed": False, "PRCreated": False},
        "files": files, "frozenReadOnlyContractInputs": frozen_inputs,
        "dataset": {"manifest": fingerprint(manifest_path), "inputs": dataset, "geometry": fingerprint(bundle / "geometry.bytes"),
                    "sourceCommit": manifest["sourceCommit"], "sourceTree": manifest["sourceTree"], "toolsCommit": manifest["toolsCommit"],
                    "inputOrigin": manifest["inputOrigin"], "aspectOrigin": manifest["frames"][0]["aspectOrigin"],
                    "sourceSdlVersion": manifest["sourceSdlVersion"], "geometryParser": manifest["geometryParser"],
                    "geometryFactor": 4, "uvOrigin": manifest["uvOrigin"], "nativeConvertedTextureEquivalent": False,
                    "generatedSpfxHeaderEquivalent": False, "originalArtOrDerivedArraysTracked": False,
                    "authorizationReference": manifest["authorizationReference"]},
        "typedInterface": {"namespace": "Blitz.Contracts", "name": "ISourceFireSpfxRuntime",
                           "sourceFile": "Assets/Scripts/Contracts/SourceFireSpfxContracts.cs",
                           "pureCSharp73Netstandard21": True, "separateUnityAssetAndRendererBoundary": True,
                           "gameplayAndRendererRNG": "Every randrng through Core host SourceDraw/RandRange; zero renderer RNG; no private stream",
                           "allocation": "Host allocates globally distinct logical process serials, not native pointers",
                           "cache": "Actual jpos15 + same-tick historical cambot; explicit older-cache retention evidence",
                           "sound": "Original ordered direct metadata with explicit resolved/unresolved result; no DSP playback claim"},
        "executionModes": {"ordering": ["VerifiedDispatchWitness", "PortableDeclaredSlots"],
                           "clock": ["VerifiedOriginalPcount", "PortableLogicalEpoch"],
                           "strictWitnessVerificationOwnedByHost": True, "hashSyntaxIsNotNativeVerification": True,
                           "sortRequiresKnownEffectListPermutation": True, "witnessedRestartVisitsSupported": True,
                           "partialKillMasks": "Rejected: active source kill sites all use -1; target NODESTRUCT metadata not guessed",
                           "stalePolicies": ["RejectUndefinedAccess", "PortableCancelDeletedAndFreezeOrphan"],
                           "portableDrawStage": "Unity CameraEvent.AfterForwardAlpha; owned effect projection only",
                           "depth": "Known WBUFFER/ALWAYS/write source state; explicitly portable Unity projection depth, not native W-buffer"},
        "proofReceipts": receipts,
        "actualTests": {"productionAssertions": proof["assertions"], "originalCProcessCases": proof["comparedSourceCases"],
                        "originalMaybePlayerFireCases": proof["comparedOriginalEmitterCases"],
                        "originalBallSelectorSteps": proof["comparedOriginalBallSelectorSteps"],
                        "originalGooseControlStages": proof["comparedOriginalControlStages"],
                        "compiledCausalMutantsKilled": evidence["mutations"]["compiledKilled"],
                        "mutants": evidence["mutations"]["mutants"],
                        "negativeInputControls": evidence["inputs"]["rejected"],
                        "metadata": evidence["inputs"]["inventory"],
                        "allCurrentRuntimeScripts": evidence["api"]["runtime_sources"],
                        "allCurrentEditorScripts": evidence["api"]["ALL_Editor_sources"],
                        "apiProfiles": [{"name": p["profile"], "sources": len(p["sources_sha256"]), "assemblySha256": p["assembly_sha256"]}
                                        for p in evidence["api"]["profiles"]],
                        "language": "C#7.3", "framework": "actual archive Unity netstandard2.1 references",
                        "UnityEditorExecuted": False, "originalCBindingsAndOrderAreDeclaredComponentFixtures": True},
        "sourceReferences": {"NFLPlayerC": {"sha256": proof["originalSourceSha256"],
                                          "functions": ["player_burst_into_flames:4523", "plyr_fire_trail_proc:9518",
                                                        "ball_fire_trail_proc:9422", "flash_plyr_proc:9838", "draw_spfx:9789",
                                                        "maybe_plyr_fire:4468", "ball_proc selector/triplet:10313-10370"]},
                             "GooseObjectC": {"sha256": proof["originalObjectSourceSha256"]},
                             "GooseProcessC": {"sha256": proof["originalProcessSourceSha256"]},
                             "shells": "Both pinned PSHELL.S/cpshell.S:16-33 return->die(1), no automatic3D object deletion",
                             "geometry": "Original SDL bee1894074a84b1cfdb1a9099e51ccf9afa5fca8 and models.mak:96-98 factor4",
                             "draw": "Pinned active NFL geom.s fecf16a0511ab5d14a7f5e3a96742407e1d43aa6;664 projection, lowres2/3, integer aspect scales",
                             "playerFlash": "player.c:6853-6857 and6934-6938 raw0x400 combine/constant/restoration"},
        "CoreProducerHookRequest": HOOKS,
        "unqualified": ["Actual Core producer and opt-in M1 factory wiring", "Actual after-step and player-flash consumer proof",
                        "Global native level0/qsort tie/epoch/menu timing", "Global3D object draw order against non-FX consumers",
                        "Target PRNG algorithm/internal state sharing", "Generated spfx.h/native ABI/packing/pointer reuse",
                        "Actual TEXUS/WMS ARGB8332 quantization/alpha association/mips", "Native MIPS pixel/FPU clamp/W-buffer precision",
                        "Unity Editor execution/import/build/native pixels", "DSP/waveforms/mixer", "Full M1/full match/all16 native rows"],
        "readme": "tests/fire-spfx/README.md",
        "commands": {
            "production": proof["invocation"],
            "inputs": evidence["inputs"]["invocation"],
            "mutations": evidence["mutations"]["invocation"],
            "api": ["python3", "tests/fire-spfx/api_compile.py", "--unity-editor-data",
                    str(Path(next(p for p in evidence["api"]["profiles"][0]["references_sha256"]
                                  if p.endswith("/NetStandard/ref/2.1.0/netstandard.dll"))).parents[3]),
                    "--output", str(args.api_proof.resolve().parent)],
            "cwd": str(ROOT), "rerunRequiresNewIgnoredOutputDirectories": True
        }
    }
    output.write_text(json.dumps(result, indent=2) + "\n")
    print(json.dumps(fingerprint(output), indent=2))


if __name__ == "__main__":
    main()
