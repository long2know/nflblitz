#!/usr/bin/env python3
"""Asset-free or explicit actual-data Contact272 preflight; no Unity process."""

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
SPEC = importlib.util.spec_from_file_location("source_methods", ROOT / "tests/interface/stage_followup.py")
methods = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(methods)


def require(value, message):
    if not value:
        raise AssertionError("CONTACT CALLER: " + message)


def sha(data):
    return hashlib.sha256(data).hexdigest()


def source_inventory(root=ROOT):
    result = {path.relative_to(root).as_posix(): path.read_bytes()
              for path in sorted((root / "Assets/Scripts").rglob("*.cs"))}
    result["tests/parity/FullKernelInputs.cs"] = (root / "tests/parity/FullKernelInputs.cs").read_bytes()
    return result


def validate_callers(sources):
    def source(name):
        return sources["Assets/Scripts/" + name].decode("utf-8")

    def body(name, method):
        return methods.compact(methods.method(source(name), method))

    services = "Runtime/SourceInterfaceServices.cs"
    validate = body(services, "Validate")
    require("profile.AnimationResourceKey" in validate and "!ReferenceEquals(originalAnimations,canonical)" in validate
            and "profile.RequireLibrary(actual);" in validate,
            "actual services must enforce same canonical profile asset and exact typed272 inventory")
    require(validate.index("SoundVmData.PayloadSha256") < validate.index("animations=actual;"),
            "mandatory exact SAFN is validated before releasing service animation instance")
    sound = source(services)
    require("!ReferenceEquals(originalSoundVmFunctions, canonical)" in sound and
            "originalSoundVmFunctions.bytes.Length != 1983" in sound,
            "actual services must reject copied/missing/wrong SAFN canonical reference before playback")
    create = body(services, "CreateSimulation")
    require("if(!Profile.IsContact272&&!Profile.UsesC2Data)" in create and "!ReferenceEquals(prefab,teams.Template)" in create,
            "historical services/copied template fail before selecting any old contact simulation")
    require("SourceContactComposition.CreateSimulation(animations,pivots,playbook,Configuration(),"
            "teams.Rosters,PresentationSettings,MovementData,MovementSettings,SoundVmData)" in create,
            "actual Unity services must use tested full C1 production factory, not an inspector helper")
    awake = body("Runtime/BlitzRuntime.cs", "Awake")
    require("SourceTeamResourceProfile.SelectRuntimeProfile(Application.absoluteURL,sourceResourceProfile)" in awake
            and "SourcePlayerProvider.ResolveActorMode(profile)" in awake
            and "SourceInterfaceServices.ResourceKeyFor(profile)" in awake
            and "sourceServices.Profile.StableId!=profile.StableId" in awake,
            "real bootstrap retains C1 default and selects only matching explicit C1/C2 resources")
    require("!ReferenceEquals(animations,SourcePlayerProvider.ResolveRequiredAnimationLibrary(prefab))" in awake
            and "sourceServices.CreateSimulation(prefab,compatibility)" in awake
            and awake.index("ReferenceEquals(animations,") < awake.index("sourceServices.CreateSimulation(prefab,compatibility)"),
            "renderer/provider/Core use same canonical272 object before factory")
    require("newFieldScene(transform,prefab,animations)" in awake,
            "actual field animation consumer receives the same library object")
    provider = source("Presentation/SourcePlayerProvider.cs")
    require("profile.RequireImpact(library);" in provider and "SourceTeamResourceProfile.ContactAnimationKey" in provider,
            "provider impact and animation keys must agree with exact new generation")
    importer = source("Editor/SourceTeamInterfaceImporter.cs")
    require("SourceInterfacePackagePlan.WithContact272(PlanFromSoundVmArguments(args))" in importer
            and "services.InitializeContact272(" in importer
            and "(!profile.IsContact272 && !profile.UsesC2Data) || !plan.HasSoundVm" in importer,
            "actual Editor plan/import must select contact272 with mandatory SAFN, never old services")
    team = source("Editor/SourceTeamImporter.cs")
    require("ImportContact272FromCommandLine" in team and "SourceTeamResourceProfile.Contact272" in team
            and "profile.RequireImpact(SourceCinematicLibrary.Read(impactBytes, expectedImpactSha256))" in team,
            "actual team importer must enforce new272 and its bound impact catalog before output")
    checks = source("Editor/SourceTeamInterfaceChecks.cs")
    require("VerifyContact272BindingsFromCommandLine" in checks and "services.Profile.StableId == profile.StableId" in checks,
            "fresh-domain checks verify saved explicit profile tag and bindings")
    field = body("Presentation/FieldScene.cs", "Render")
    require("SourceActorPoseSampler.CanInterpolate(old,view)" in field
            and "actor.SourceAnimation.Apply(old,view,renderInterpolation01)" in field,
            "real actor-root and pose paths consume the tested C1 contact seam")
    runtime = source("Runtime/BlitzRuntime.cs")
    require("SourceSimulation.UnresolvedContactSoundCalls" in runtime and "UnresolvedContactSoundCalls" not in awake,
            "unresolved actual contact calls stay raw metadata, not invented resolved dispatches")
    return 12


def arguments(parser):
    parser.add_argument("--bundle", type=Path)
    parser.add_argument("--base-bundle", type=Path)
    parser.add_argument("--fixture", type=Path)
    parser.add_argument("--impact-catalog", type=Path)
    parser.add_argument("--old-impact-catalog", type=Path)
    parser.add_argument("--hud-root", type=Path)
    parser.add_argument("--registry", type=Path, default=ROOT / "tools/animation/contact_sequences.json")
    parser.add_argument("--output", type=Path)


def actual_arguments(args, output):
    inputs = (args.bundle, args.base_bundle, args.fixture, args.impact_catalog, args.old_impact_catalog, args.hud_root)
    require(not any(inputs) or all(inputs), "actual mode requires ALL explicit272/base157/fixture/impact/old-impact/HUD inputs")
    if not all(inputs):
        return []
    return [str(path.resolve(strict=True)) for path in
            (args.bundle, args.base_bundle, args.fixture, args.impact_catalog, args.old_impact_catalog,
             args.registry, args.hud_root)] + [str(output / "package-inputs")]


def environment(output):
    for name in ("process-local", "dotnet-home", "nuget"):
        (output / name).mkdir(parents=True, exist_ok=True)
    return dict(os.environ, TMPDIR=str(output / "process-local"), TMP=str(output / "process-local"),
                TEMP=str(output / "process-local"), DOTNET_CLI_HOME=str(output / "dotnet-home"),
                NUGET_PACKAGES=str(output / "nuget"), DOTNET_CLI_TELEMETRY_OPTOUT="1", DOTNET_NOLOGO="1")


def compile_project(project, source, output, env):
    build = output / project.stem
    command = ["dotnet", "build", str(project), "--no-restore", "--configuration", "Release",
               "--verbosity", "quiet", "--nologo", "-p:BlitzSourceRoot=" + str(source) + "/",
               "-p:BaseIntermediateOutputPath=" + str(build / "obj") + "/",
               "-p:BaseOutputPath=" + str(build / "bin") + "/"]
    result = subprocess.run(command, cwd=ROOT, env=env, capture_output=True, text=True, timeout=180)
    if result.returncode != 0 and "NETSDK1004" in result.stdout + result.stderr:
        restore = [arg for arg in command if arg != "--no-restore"]
        result = subprocess.run(restore, cwd=ROOT, env=env, capture_output=True, text=True, timeout=180)
    log = result.stdout + result.stderr
    (output / (project.stem + ".log")).write_text(log)
    require(result.returncode == 0 and "0 Warning(s)" in log and "0 Error(s)" in log,
            "actual production/SDK compilation must have zero warnings/errors:\n" + log)
    return build / "bin/Release/net10.0/ContactPresentation.Checks.dll"


def snapshot(sources, destination):
    destination.mkdir()
    for name, content in sources.items():
        path = destination / name
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(content)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    arguments(parser)
    args = parser.parse_args()
    output = (args.output or SUITE / ".artifacts/preflight").resolve()
    require(output.is_relative_to(SUITE / ".artifacts") or output.is_relative_to(ROOT / ".squad/.scratch/contact-profile"),
            "checks may write only their own ignored artifacts")
    output.mkdir(parents=True, exist_ok=True)
    work = output / "source"
    sources = source_inventory()
    report = {"schema": "contact272-presentation-preflight/v1", "status": "failed",
              "source_sha256": {name: sha(raw) for name, raw in sources.items()},
              "Editor_executed": False, "native_game_or_DSP_proof": False}
    try:
        report["actual_caller_guards"] = validate_callers(sources)
        actual = actual_arguments(args, output)
        report["explicit_actual_inputs"] = actual[:-1]
        snapshot(sources, work)
        env = environment(output)
        compile_project(SUITE / "ContactPresentation.Production.csproj", work, output, env)
        assembly = compile_project(SUITE / "ContactPresentation.Checks.csproj", work, output, env)
        result = subprocess.run(["dotnet", str(assembly), *actual], cwd=ROOT, env=env,
                                capture_output=True, text=True, timeout=300)
        log = result.stdout + result.stderr
        (output / "checks.log").write_text(log)
        print(log, end="")
        require(result.returncode == 0 and "PASS Contact272 presentation:" in log, "actual compiled contract checks failed")
        require(source_inventory() == sources, "Core/presentation source generation changed during preflight; wait for freeze")
        report.update(status="passed", sources_unchanged=True, actual_data=bool(actual), output=log)
        return 0
    finally:
        (output / "result.json").write_text(json.dumps(report, indent=2) + "\n")
        if work.exists():
            shutil.rmtree(work)


if __name__ == "__main__":
    sys.exit(main())
