#!/usr/bin/env python3
"""Sound package/current resolved-cue checks; default needs no ignored original inputs."""
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
        raise AssertionError("Sound packaging/transport guard: " + label)


def module_at(name, path):
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def guards(source_root=ROOT):
    helper = module_at("existing_public_source_method_parser", ROOT / "tests/interface/stage_followup.py")
    method, compact = helper.method, helper.compact
    playback = (source_root / "Assets/Scripts/Presentation/SourceCinematicPlayback.cs").read_text()
    consumer = compact(method(playback, "ConsumeResolvedSourceSound"))
    require("RequireOriginalCallback(cue);" in consumer and "Ordered(context.SourceTick,context.ProcessOrder,context.ActorPlayerId);" in consumer,
            "actual original callback/source operands and single global order validated")
    require(consumer.index("RequireOriginalCallback(cue);") < consumer.index("Ordered(") <
            consumer.index("dispatch.Status!=SourceSoundVmStatus.Resolved"), "ordered original cue consumed before resolved result/output")
    require("cue.Kind!=SourcePresentationCueKind.SoundCallback" in consumer and "dispatch.Result==null" in consumer,
            "PlayerDown/SPFX/nonResolved/missing result cannot become silence")
    require("if(dispatch.Result.Calls.Count==0)return;" in consumer and "if(actualRawOutputConsumer==null)" in consumer,
            "legitimate resolved0 is distinct from unbound nonempty output")
    require(consumer.count("actualRawOutputConsumer(dispatch);") == 1 and
            consumer.index("Calls.Count==0") < consumer.index("actualRawOutputConsumer==null") <
            consumer.index("actualRawOutputConsumer(dispatch);"),
            "same immutable nonempty raw dispatch delivered exactly once or visible DSP fault")
    require("ReferenceEquals(call.OriginalCue,cue)" in consumer and "call.SubcallOrder!=i" in consumer,
            "exact OriginalCue identity/raw subcall order retained")
    require(not any(token in consumer for token in ("SourceAudioFunctions.Select", ".Draw(", "listener(", "SourceCallback?.Invoke")),
            "already-resolved delivery never selects, draws RNG, or replays old callback")
    execute = compact(method(playback, "Execute"))
    require(execute.index("SourcePresentationCueKind.PlayerDownCallback") < execute.index("RequireOriginalCallback(callback)"),
            "already-executed PlayerDown remains prior ordering-only branch")
    camera = (source_root / "Assets/Scripts/Presentation/PlayCamera.cs").read_text()
    wrapper = compact(method(camera, "ConsumeResolvedSourceSound"))
    require("SourcePlayback.ConsumeResolvedSourceSound(dispatch,actualRawOutputConsumer);" in wrapper and
            "ConsumeSourceCue(" not in wrapper, "actual PlayCamera forwards resolved seam only once")
    package = (source_root / "Assets/Scripts/AssetImport/SourceTeamInterfacePackage.cs").read_text()
    read = compact(method(package, "ReadSoundVmFunctions"))
    require("expectedSha256==SoundVmFunctionsSha256" in read and "bytes.Length==SoundVmFunctionsBytes" in read,
            "exact frozen SAFN identity/1983bytes, not runtime inferred hash or missing input")
    importer = (source_root / "Assets/Scripts/Editor/SourceTeamInterfaceImporter.cs").read_text()
    args = method(importer, "PlanFromSoundVmArguments")
    require(all(flag in args for flag in ("-interfaceSoundVmRoot", "-interfaceSoundVmData", "-interfaceSoundVmSha256")),
            "actual Editor caller requires explicit root/path/hash, no asset guessing")
    require("soundVmText,SourceInterfacePackagePlan.SoundVmFunctionsSha256" in compact(method(importer, "Import")),
            "actual import calls explicit new10arg initializer, not historical8")
    services = (source_root / "Assets/Scripts/Runtime/SourceInterfaceServices.cs").read_text()
    create = compact(method(services, "CreateSimulation"))
    require("SourceContactComposition.CreateSimulation(animations,pivots,playbook,Configuration(),"
            "teams.Rosters,PresentationSettings,MovementData,MovementSettings,SoundVmData,"
            "kickMeterMetrics:kickMetrics)" in create
            and "SourceC2Composition.CreateSimulation(animations,pivots,playbook,Configuration(),"
            "teams.Rosters,PresentationSettings,MovementData,MovementSettings,SoundVmData,bootstrap,"
            "kickMeterMetrics:kickMetrics)" in create
            and "SourceC2Composition.CreateM1Simulation(animations,pivots,playbook,Configuration(),"
            "teams.Rosters,PresentationSettings,MovementData,MovementSettings,SoundVmData,bootstrap,"
            "compatibility:compatibility,fireEffects:fireEffects,kickMeterMetrics:kickMetrics)" in create,
            "actual services pass immutable sound to all real C1/C2 factories")
    helper.validate_sound_factories(source_root)
    require("HistoricalSoundVmResourceKey = SourceInterfacePackagePlan.SoundVmServicesResourceKey" in services and
            "SourceData/sound-functions-safn1-4a557304" in services, "actual service/data resource identity agrees with import")
    transport_path = source_root / "Assets/Scripts/Runtime/SourceSoundVmTransport.cs"
    if transport_path.exists():
        transport = transport_path.read_text()
        require("ReferenceEquals" in transport and not any(token in transport for token in ("SourceAudioFunctions.Select(", ".Draw(")),
                "actual transport pairs OriginalCue identity without post-host selection/RNG")
    return 23


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--sound-vm-data", type=Path)
    parser.add_argument("--impact-catalog", type=Path)
    parser.add_argument("--hud-root", type=Path)
    parser.add_argument("--playbook-root", type=Path)
    parser.add_argument("--movement-data", type=Path)
    args = parser.parse_args()
    if any((args.hud_root, args.playbook_root, args.movement_data)) and not all(
            (args.sound_vm_data, args.hud_root, args.playbook_root, args.movement_data)):
        parser.error("Actual package checks need explicit SAFN/HUD/P1/movement inputs together.")
    ARTIFACTS.mkdir(parents=True, exist_ok=True)
    env = dict(os.environ, TMPDIR=str(ARTIFACTS), TMP=str(ARTIFACTS), TEMP=str(ARTIFACTS),
               PYTHONDONTWRITEBYTECODE="1", DOTNET_CLI_TELEMETRY_OPTOUT="1", DOTNET_NOLOGO="1")
    before = {path.relative_to(ROOT).as_posix(): digest(path.read_bytes()) for path in
              sorted((ROOT / "Assets/Scripts").rglob("*.cs"))}
    report = {"schema": "source-sound-packaging-checks/v1", "status": "failed", "source_mode": "current-checkout",
              "source_inventory_sha256": before, "implicit_authoring_roots": False,
              "Editor_executed": False, "DSP_or_native_game_proof": False}
    try:
        report["actual_producer_consumer_guards"] = guards()
        command = ["dotnet", "run", "--project", str(SUITE / "SoundPackagingChecks.csproj"),
                   "--configuration", "Release", "--verbosity", "quiet", "--",
                   str(args.sound_vm_data.resolve(strict=True)) if args.sound_vm_data else "-",
                   str(args.impact_catalog.resolve(strict=True)) if args.impact_catalog else "-"]
        if args.hud_root:
            output = ARTIFACTS / "package-inputs"
            output.mkdir(exist_ok=True)
            command += [str(args.hud_root.resolve(strict=True)), str(args.playbook_root.resolve(strict=True)),
                        str(args.movement_data.resolve(strict=True)), str(output)]
        result = subprocess.run(command, cwd=ROOT, env=env, capture_output=True, text=True, timeout=180)
        output = result.stdout + result.stderr
        print(output, end="")
        (ARTIFACTS / "pure.log").write_text(output)
        require(result.returncode == 0, "actual pure preflight/resolved consumer contract")
        after = {path.relative_to(ROOT).as_posix(): digest(path.read_bytes()) for path in
                 sorted((ROOT / "Assets/Scripts").rglob("*.cs"))}
        require(after == before, "source inventory changed while validation ran; retry after scoped source freeze")
        report.update(status="passed", command=command, output=output, original_SAFN_supplied=bool(args.sound_vm_data),
                      actual_callback_catalog_supplied=bool(args.impact_catalog), actual_package_supplied=bool(args.hud_root))
        print("PASS " + str(report["actual_producer_consumer_guards"]) + " actual sound resource/bind/resolved-consumer guards")
        return 0
    except (AssertionError, OSError, subprocess.SubprocessError) as error:
        report["error"] = str(error)
        print(str(error), file=sys.stderr)
        return 1
    finally:
        (ARTIFACTS / "result.json").write_text(json.dumps(report, indent=2) + "\n")


if __name__ == "__main__":
    raise SystemExit(main())
