#!/usr/bin/env python3
"""Current-checkout pure M1 presentation checks; original source/assets only when explicitly supplied."""
import argparse
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[3]
SUITE = Path(__file__).resolve().parent
ARTIFACTS = SUITE / ".artifacts"


def digest(data):
    return hashlib.sha256(data).hexdigest()


def require(value, label):
    if not value:
        raise AssertionError("M1 presentation caller guard: " + label)


def load_module(name, path):
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def caller_guards(source_root=ROOT):
    helpers = load_module("real_stage_method_helpers", ROOT / "tests/interface/stage_followup.py")
    method, compact = helpers.method, helpers.compact
    camera = (source_root / "Assets/Scripts/Presentation/PlayCamera.cs").read_text()
    playback = (source_root / "Assets/Scripts/Presentation/SourceCinematicPlayback.cs").read_text()
    runtime = (source_root / "Assets/Scripts/Runtime/BlitzRuntime.cs").read_text()
    getter = compact(method(camera, "RequireSourceCameraAtTick"))
    require("lastSourceStageTick!=executingSourceTick" in getter and "sourceCameraSuspended" in getter and
            "SourcePlayback.RequireSourceCameraAtTick(executingSourceTick)" in getter,
            "actual PlayCamera getter checks exact cached source stage and pure stamped playback")
    require(not any(token in getter for token in ("model.Update", "worldToCameraMatrix", "LookAt", "SourceUnitsPerYard", "Screen.")),
            "no render/lookAt/yard reconstruction in current source frame read")
    render = compact(method(camera, "Render"))
    require("if(!movement157)sourceAspect=aspect;" in render,
            "render aspect must not change M1's original DIP camera inputs")
    require(not any(token in render for token in ("CameraSourceTick(", "SampleAtSourceTick(", "executingSourceCamera.Publish(")),
            "render never advances source camera/clock")
    require("sourceStageDriven?newNormalCameraFrame" in render, "source render retains cached normal frame, legacy model remains separate")
    initialize = compact(method(camera, "InitializeSourceStage"))
    require("movement157&&!sourceMovementRasterConfigured" in initialize and "if(!movement157)sourceAspect=" in initialize,
            "M1 cannot initialize with browser/default original raster")
    raster = compact(method(camera, "ConfigureSourceMovementRaster"))
    require("sourceStageDriven" in raster and "SourceCameraFrameClock.RequireOriginalRasterAspect" in raster,
            "DIP resolution bound before first source wake")
    tick = compact(method(playback, "CameraSourceTick"))
    matrix = "CurrentSourceCamera=SourceCinematicTransform.FromState(camera.State);"
    publish = "executingSourceCamera.Publish(sourceTick,CurrentSourceCamera);"
    require(matrix in tick and publish in tick and tick.index(matrix) < tick.index(publish),
            "stamp only the successfully computed current pure source matrix")
    reset_at = playback.index("public void Reset(SourceCinematicSeed normalCamera)")
    require("executingSourceCamera.Reset();" in compact(method(playback[reset_at:], "Reset")),
            "source restart invalidates old frame generation")
    awake = compact(method(runtime, "Awake"))
    require(awake.index("ConfigureSourceMovementRaster(") < awake.index("ConfigureSourceStage(") <
            awake.index("RegisterSourceCameraTickProducer(playCamera.SampleAtSourceTick)") <
            awake.index("simulation.SourceCameraHeadingProvider=SampleSourceCameraHeading"),
            "actual M1 raster/raw provider registration occurs before source wake")
    require("state=>simulation.SourceCameraRawGameMode" in awake, "use Core's actual GAME.H raw mode, not GamePhase numeric cast")
    sample = compact(method(runtime, "SampleSourceCameraHeading"))
    producer = "sourceCameraProducer(preActorState,executingSourceTick)"
    frame = "playCamera.RequireSourceCameraAtTick(executingSourceTick)"
    setframe = "simulation.SetSourceMovementCameraFrame("
    require(sample.count(producer) == 1 and sample.count(frame) == 1 and sample.count(setframe) == 1 and
            sample.index(producer) < sample.index(frame) < sample.index(setframe) < sample.index("returnheading;"),
            "actual provider samples once, reads current pure frame, sets Core frame INSIDE same pre-player callback")
    require("frame.Position,frame.Transform,bootstrap.HorizontalResolution,bootstrap.VerticalResolution" in sample,
            "actual source pose12 plus explicit bootstrap DIP, never WorldJoint/Unity camera")
    require(awake.count("SourcePresentationCueRaised+=playCamera.ConsumeSourceCue;") == 1 and
            awake.count("AfterHostStep+=playCamera.ObserveSourceHostStep;") == 1,
            "one cue and host lifecycle owner")
    provider = (source_root / "Assets/Scripts/Presentation/SourcePlayerProvider.cs").read_text()
    resolve = compact(method(provider, "ResolveRequiredAnimationLibrary"))
    require("profile.AnimationResourceKey" in resolve and "profile.RequireInventory(" in resolve,
            "actual prefab profile selects exact new157 key and all manifest/clip/frame identities")
    require("ResolveRequiredTeamPresentation(sourcePlayerPrefab)" in
            (source_root / "Assets/Scripts/Presentation/FieldScene.cs").read_text(),
            "field selects actual per-prefab team resources")
    importer = (source_root / "Assets/Scripts/Editor/SourceTeamImporter.cs").read_text()
    require("profile.AnimationAssetPath" in importer and "profile.AnimationSha256" in importer and
            "CreateHierarchy(profile.StableId" in importer,
            "actual native team importer writes the same157 library/prefab profile as provider")
    interface = (source_root / "Assets/Scripts/Editor/SourceTeamInterfaceImporter.cs").read_text()
    require("PlanFromMovement157Arguments" in interface and "plan.BootstrapSha256" in interface and
            "SourceInterfaceMovementData.ResourceKey" in interface and "profile.AnimationAssetPath" in interface,
            "actual importer packages bootstrap/runtime data and exact157 path, not a viewer")
    normal = source_root / "Assets/Scripts/Presentation/NormalCameraModel.cs"
    require(digest(normal.read_bytes()) == "4df6d0ce1bfa11322aa0e1e640f1e12bc043468e3d1ccd905deed376d411fb88",
            "original proven B1/graybox normal-camera implementation remains unchanged")
    return 18


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--original-source-root", type=Path, help="Explicit pinned camera source; run unchanged original C matrix oracle.")
    parser.add_argument("--bundle157-root", type=Path)
    parser.add_argument("--hud-root", type=Path)
    parser.add_argument("--playbook-root", type=Path)
    parser.add_argument("--movement-data", type=Path)
    args = parser.parse_args()
    if any((args.hud_root, args.playbook_root, args.movement_data)) and not all(
            (args.hud_root, args.playbook_root, args.movement_data)):
        parser.error("Actual package checks require HUD, P1, and pinned runtime movement together.")
    ARTIFACTS.mkdir(parents=True, exist_ok=True)
    env = dict(os.environ, TMPDIR=str(ARTIFACTS), TMP=str(ARTIFACTS), TEMP=str(ARTIFACTS),
               PYTHONDONTWRITEBYTECODE="1", DOTNET_CLI_TELEMETRY_OPTOUT="1", DOTNET_NOLOGO="1")
    before = {str(path.relative_to(ROOT)): digest(path.read_bytes()) for path in
              sorted((ROOT / "Assets/Scripts").rglob("*.cs"))}
    report = {"schema": "movement-presentation-checks/v1", "status": "failed", "Editor_executed": False,
              "source_mode": "current-checkout-inventory", "implicit_frozen_authoring_roots": False,
              "production_sha256": before, "native_game_browser_parity_claimed": False}
    try:
        report["actual_caller_guards"] = caller_guards()
        oracle = "-"
        if args.original_source_root:
            original = load_module("actual_existing_camera_oracle", ROOT / "tests/presentation/source_oracle.py")
            oracle = str(original.generate(args.original_source_root.resolve(strict=True), ARTIFACTS / "original-C"))
        command = ["dotnet", "run", "--project", str(SUITE / "StageChecks.csproj"), "--configuration", "Release",
                   "--verbosity", "quiet", "--", oracle,
                   str(args.bundle157_root.resolve(strict=True)) if args.bundle157_root else "-"]
        if args.hud_root:
            output = ARTIFACTS / "package-inputs"
            output.mkdir(exist_ok=True)
            command += [str(args.hud_root.resolve(strict=True)), str(args.playbook_root.resolve(strict=True)),
                        str(args.movement_data.resolve(strict=True)), str(output)]
        result = subprocess.run(command, cwd=ROOT, env=env, capture_output=True, text=True, timeout=240)
        text = result.stdout + result.stderr
        print(text, end="")
        (ARTIFACTS / "pure.log").write_text(text)
        report.update(command=command, output=text, original_C_requested=bool(args.original_source_root),
                      actual157_requested=bool(args.bundle157_root), actual_package_requested=bool(args.hud_root))
        after = {str(path.relative_to(ROOT)): digest(path.read_bytes()) for path in
                 sorted((ROOT / "Assets/Scripts").rglob("*.cs"))}
        require(after == before, "source inventory changed while validation ran; wait for scoped owner freeze")
        require(result.returncode == 0, "actual pure camera/resource contract failed")
        report["status"] = "passed"
        print("PASS " + str(report["actual_caller_guards"]) + " actual producer/consumer/resource/cached-render guards")
        return 0
    except (AssertionError, OSError, subprocess.SubprocessError) as error:
        report["error"] = str(error)
        print(str(error), file=sys.stderr)
        return 1
    finally:
        (ARTIFACTS / "result.json").write_text(json.dumps(report, indent=2) + "\n")


if __name__ == "__main__":
    raise SystemExit(main())
