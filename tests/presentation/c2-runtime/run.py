#!/usr/bin/env python3
"""Fresh production281/C2 package, factory, transport and actual-data preflight."""
import argparse
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[3]
SUITE = Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location("c2_source_methods", ROOT / "tests/interface/stage_followup.py")
methods = importlib.util.module_from_spec(spec)
spec.loader.exec_module(methods)


def require(ok, message):
    if not ok:
        raise AssertionError("C2 CALLER: " + message)


def sha(data):
    return hashlib.sha256(data).hexdigest()


def sources():
    result = {p.relative_to(ROOT).as_posix(): p.read_bytes() for p in sorted((ROOT / "Assets/Scripts").rglob("*.cs"))}
    result["tests/parity/FullKernelInputs.cs"] = (ROOT / "tests/parity/FullKernelInputs.cs").read_bytes()
    return result


def caller_guards(values):
    def text(name):
        return values["Assets/Scripts/" + name].decode()
    def body(name, method):
        return methods.compact(methods.method(text(name), method))
    services = text("Runtime/SourceInterfaceServices.cs")
    require("!ReferenceEquals(callbacks, originalC2Callbacks)" in services and
            "!ReferenceEquals(hit, originalC2HitExtension)" in services, "same canonical C2 data assets required")
    create = body("Runtime/SourceInterfaceServices.cs", "CreateSimulation")
    require("SourceC2Bootstrap.FromOriginalProgramBootAtSoundBanks(C2Data)" in create and
            "SourceC2Composition.CreateSimulation(animations,pivots,playbook,Configuration()," in create,
            "actual services must invoke the real C2 factory with pre-constructor bootstrap")
    validate = body("Runtime/SourceInterfaceServices.cs", "Validate")
    require("!ReferenceEquals(originalAnimations,canonical)" in validate and "profile.RequireLibrary(actual)" in validate,
            "actual canonical library instance and full trusted281 identity")
    awake = body("Runtime/BlitzRuntime.cs", "Awake")
    require("SelectRuntimeProfile(Application.absoluteURL,sourceResourceProfile)" in awake and
            "SourceInterfaceServices.ResourceKeyFor(profile)" in awake and
            "sourceServices.Profile.StableId!=profile.StableId" in awake,
            "real runtime selector/service registry agrees with explicit profile")
    require("!ReferenceEquals(animations,SourcePlayerProvider.ResolveRequiredAnimationLibrary(prefab))" in awake and
            "newFieldScene(transform,prefab,animations)" in awake, "renderer uses SAME selected library object")
    drain = body("Runtime/BlitzRuntime.cs", "OnAfterSimulationStep")
    require("observed(dispatch)" in drain and "soundTransport.FlushNativePrefix()" in drain and
            drain.index("observed(dispatch)") < drain.index("FlushNativePrefix()"),
            "publish actual typed raw output/status BEFORE an unbound DSP error")
    cue = body("Runtime/BlitzRuntime.cs", "ConsumeSourcePresentationCue")
    require("playCamera.ConsumeResolvedSourceSound(dispatch,soundTransport.DeliverResolved)" in cue and
            "soundTransport.FlushNativePrefix()" in cue, "single FIFO pairs animation once and flushes native prefix")
    require("bridge.Restarted += ResetSourceSoundTransport" in text("Runtime/BlitzRuntime.cs"),
            "restart clears one transport, not a second C2 queue")
    importer = text("Editor/SourceTeamInterfaceImporter.cs")
    require("PlanFromContact281C2Arguments" in importer and "services.InitializeContact281C2(" in importer and
            "plan.CopyC2HitExtension()" in importer and "plan.CopyC2Callbacks()" in importer,
            "actual saved import packages both new payloads and typed281 services")
    require("SourceTeamResourceProfile.Contact281C2" in text("Editor/SourceTeamImporter.cs") and
            "VerifyContact281C2BindingsFromCommandLine" in text("Editor/SourceTeamInterfaceChecks.cs"),
            "team/template/import and fresh-domain checks select same new281 profile")
    build = text("Editor/WebGlBuild.cs")
    require("BuildContact281C2" in build and "PrepareContact281C2Project" in build and
            "if (profile.UsesC2Data) runtime.ConfigureSourceResourceProfile(profile.StableId)" in build,
            "explicit C2 build recipe persists its real runtime selector; default recipe remains separate")
    require("profile.RequireImpact(library)" in methods.compact(text("Presentation/SourcePlayerProvider.cs")),
            "actual camera provider enforces bound477 generation")
    return 12


def m1_caller_guards(values):
    def text(name):
        return values["Assets/Scripts/" + name].decode()
    services = text("Runtime/SourceInterfaceServices.cs")
    require("if (profile.IsContact281C2M1) return C2M1ResourceKey;" in services and
            "SourceInterfacePackagePlan.C2M1ServicesResourceKey" in services, "M1 services strict new resource key")
    require("Profile.IsContact281C2M1 ? SourceInterfacePackagePlan.C2M1CallbackResourceKey" in services and
            "Profile.IsContact281C2M1 ? SourceInterfacePackagePlan.ReadC2M1Callbacks" in services,
            "M1 callback key and reader remain generation-specific")
    require("SourceC2Composition.CreateM1Simulation(animations, pivots, playbook, Configuration()" in services,
            "M1 actual runtime factory cannot use old-nine creator")
    importer = text("Editor/SourceTeamInterfaceImporter.cs")
    require("PlanFromContact281C2M1Arguments" in importer and "WithContact281C2M1" in importer and
            "services.InitializeContact281C2M1(" in importer and "C2M1CallbackSha256" in importer,
            "M1 import plan initializer and payload pin must agree")
    require("RequireAnimationProfileAsset(profile, animations)" in importer and
            "Contact281C2M1.AnimationAssetPath" in importer and
            "RequireAnimationProfileAsset(profile, sharedAnimations)" in text("Editor/SourceTeamImporter.cs"),
            "only canonical byte-identical animation may be shared, never callback/catalog/service")
    require("VerifyContact281C2M1BindingsFromCommandLine" in text("Editor/SourceTeamInterfaceChecks.cs") and
            "C2M1CallbackSha256" in text("Editor/SourceTeamInterfaceChecks.cs"),
            "M1 persisted checks must not use old-nine JSON pin")
    build = text("Editor/WebGlBuild.cs")
    require("BuildContact281C2M1" in build and "PrepareContact281C2M1Project" in build and
            "SourceTeamResourceProfile.SelectRuntimeProfile(null, profile.StableId)" in build,
            "M1 build recipe respects the shared deferred URL admission guard")
    return 7


def arguments(parser):
    for name in ("inputs", "old272", "fixture", "impact", "old-impact", "hud"):
        parser.add_argument("--" + name, type=Path)
    parser.add_argument("--output", type=Path)
    parser.add_argument("--m1-inputs", type=Path)
    parser.add_argument("--m1-impact", type=Path)


def input_args(args, output):
    given = (args.inputs, args.old272, args.fixture, args.impact, args.old_impact, args.hud)
    require(not any(given) or all(given), "all six actual input roots are required together")
    result = []
    if all(given):
        for path in given:
            path = path.resolve(strict=True)
            require(path.is_relative_to(ROOT), "all original/package inputs must be explicitly NEWroot-local")
            result.append(str(path))
        result.append(str(output / "package-inputs"))
    require(bool(args.m1_inputs) == bool(args.m1_impact), "both explicit M1 input and482 catalog paths required")
    if args.m1_inputs:
        require(bool(result), "M1 package preflight retains all old-generation actual controls")
        for path in (args.m1_inputs, args.m1_impact):
            path = path.resolve(strict=True)
            require(path.is_relative_to(ROOT), "M1 assets must remain NEWroot-local")
            result.append(str(path))
    return result


def env_for(output):
    for name in ("process-local", "dotnet-home", "nuget"):
        (output / name).mkdir(parents=True, exist_ok=True)
    return dict(os.environ, TMPDIR=str(output / "process-local"), TMP=str(output / "process-local"),
                TEMP=str(output / "process-local"), DOTNET_CLI_HOME=str(output / "dotnet-home"),
                NUGET_PACKAGES=str(output / "nuget"), DOTNET_CLI_TELEMETRY_OPTOUT="1", DOTNET_NOLOGO="1")


def snapshot(values, work):
    work.mkdir()
    for name, raw in values.items():
        path = work / name
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(raw)


def compile_project(project, work, output, env):
    destination = output / project.stem
    command = ["dotnet", "build", str(project), "--configuration", "Release", "--verbosity", "quiet", "--nologo",
               "--no-restore", "-p:BlitzSourceRoot=" + str(work) + "/",
               "-p:BaseIntermediateOutputPath=" + str(destination / "obj") + "/",
               "-p:BaseOutputPath=" + str(destination / "bin") + "/"]
    result = subprocess.run(command, cwd=ROOT, env=env, capture_output=True, text=True, timeout=180)
    if result.returncode and "NETSDK1004" in result.stdout + result.stderr:
        result = subprocess.run([x for x in command if x != "--no-restore"], cwd=ROOT, env=env,
                                capture_output=True, text=True, timeout=180)
    log = result.stdout + result.stderr
    (output / (project.stem + ".log")).write_text(log)
    require(result.returncode == 0 and "0 Warning(s)" in log and "0 Error(s)" in log,
            "production compilation must have zero diagnostics:\n" + log)
    return destination / "bin/Release/net10.0" / (project.stem + ".dll")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    arguments(parser)
    args = parser.parse_args()
    output = (args.output or SUITE / ".artifacts/assetfree").resolve()
    require(output.is_relative_to(SUITE / ".artifacts"), "only own ignored test outputs may be written")
    output.mkdir(parents=True, exist_ok=True)
    values = sources(); work = output / "source"
    report = {"schema": "contact281-c2-presentation-preflight/v1", "status": "failed",
              "source_sha256": {n: sha(b) for n, b in values.items()}, "native_Editor_DSP_or_fullgame": False}
    try:
        report["actual_caller_guards"] = caller_guards(values)
        report["actual_M1_caller_guards"] = m1_caller_guards(values)
        actual = input_args(args, output)
        report["explicit_inputs"] = actual[:-1]
        snapshot(values, work); env = env_for(output)
        compile_project(SUITE / "C2Presentation.Production.csproj", work, output, env)
        assembly = compile_project(SUITE / "C2Presentation.Checks.csproj", work, output, env)
        result = subprocess.run(["dotnet", str(assembly), *actual], cwd=ROOT, env=env,
                                capture_output=True, text=True, timeout=300)
        log = result.stdout + result.stderr
        (output / "checks.log").write_text(log); print(log, end="")
        require(result.returncode == 0 and "PASS C2 production binding:" in log, "actual compiled C2 checks failed")
        require(sources() == values, "production/test dependency changed during current-generation check")
        report.update(status="passed", actual_data=bool(actual), source_inventory_unchanged=True, output=log)
        return 0
    finally:
        (output / "result.json").write_text(json.dumps(report, indent=2) + "\n")
        if work.exists():
            shutil.rmtree(work)


if __name__ == "__main__":
    sys.exit(main())
