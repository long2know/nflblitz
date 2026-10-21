#!/usr/bin/env python3
"""Bounded actual original-C / production caller, VM, pose, sound and raw-host qualification."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys
import time

import oracle
from mutations import MUTATIONS, ROOT_MUTATIONS, apply

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[1]
sys.path.insert(0,str(ROOT/"tests/parity"))
from contact_inputs import fixture_profile
from prepare_inputs import fixture_hashes
from kernel_sources import canonical_hashes, production_paths, successor_hashes

EXTRA_PRODUCTION = (
    "Assets/Scripts/Presentation/SourceCinematicPlayback.cs",
    "Assets/Scripts/Presentation/SourceNormalCameraModel.cs",
    "Assets/Scripts/Presentation/SourceImpactShaker.cs",
    "Assets/Scripts/Runtime/SourceSoundVmTransport.cs",
    "Assets/Scripts/Runtime/FixedStepBridge.cs",
)
HARNESS = (
    "tests/defensive-presnap-switch/Program.cs",
    "tests/defensive-presnap-switch/PreSnapSwitch.Checks.csproj",
    "tests/defensive-presnap-switch/PreSnapSwitch.Production.csproj",
    "tests/parity/FullKernelInputs.cs",
    "tests/parity/FullKernelSources.props",
    "tests/audio/vm/SourceSoundVmSources.props",
)


def sha(raw):
    return hashlib.sha256(raw).hexdigest()


def require_result(code, result, expected_failure=None):
    if (not isinstance(result,dict) or result.get("schema")!="defensive-presnap-switch-checks/v1" or
            not isinstance(result.get("assertions"),int) or result["assertions"]<=0):
        raise RuntimeError("A real nonempty focused result is mandatory; compiler/start failures are not controls.")
    if expected_failure:
        if (code!=1 or result.get("status")!="failed" or
                not result.get("failed_assertion","").startswith("PRE_SNAP_ASSERTION: "+expected_failure)):
            raise RuntimeError("Compiled control missed its exact named causal assertion: "+expected_failure)
    elif (code!=0 or result.get("status")!="passed" or result.get("original_C_station_cases")!=70 or
          result.get("original_C_caller_cases")!=18 or result.get("legal_ordered_randrng10_values")!=10 or
          result.get("actual_VM_offsets")!=116 or
          result.get("interrupted_VM_offsets")!=232 or result.get("secondary_VM_offsets")!=116 or
          result.get("root_boundary_offsets")!=[24,39,40,113,114,115] or
          result.get("factory")!="SourceC2Composition.CreatePreSnapSwitchSimulation"):
        raise RuntimeError("The complete bounded original-C/caller/VM/pose/sound inventory is required.")


def arguments(argv=None):
    parser=argparse.ArgumentParser(description=__doc__)
    for name in ("source-root","fixture-manifest","sound-functions","old-c2-inputs","c2-m1-inputs",
                 "presnap-switch-inputs","player-data","output"):
        parser.add_argument("--"+name,type=Path,required=True)
    parser.add_argument("--sdk-netstandard21",action="store_true")
    parser.add_argument("--controls",action="store_true",help="All ten original and five root controls, freshly compiled serially, and final baseline.")
    return parser.parse_args(argv)


def main(argv=None):
    args=arguments(argv)
    output=args.output.absolute()
    if (output.exists() or output.resolve()!=output or not output.is_relative_to(ROOT/".squad/.scratch")):
        raise ValueError("Choose a new real ignored U2 output; do not replace prior evidence.")
    output.mkdir(parents=True)
    fixture_profile(args.fixture_manifest,"contact272")
    inputs=fixture_hashes(args.fixture_manifest,args.sound_functions)
    inputs.update(successor_hashes(args.presnap_switch_inputs))
    inputs.update(canonical_hashes(args.old_c2_inputs))
    inputs.update(canonical_hashes(args.c2_m1_inputs,"contact281-c2-m1"))
    manifest=json.loads((args.player_data/"manifest.json").read_bytes())
    if manifest.get("profile")!="original-teams-contact282-c2-m1-presnap-switch" or len(manifest["files"])!=563:
        raise ValueError("The actual explicit563-file player bundle is required; no old factory fallback.")
    for pin in manifest["files"]:
        name=Path(pin["path"])
        path=args.player_data/name
        if name.is_absolute() or ".." in name.parts or path.resolve()!=path.absolute() or sha(path.read_bytes())!=pin["sha256"]:
            raise ValueError("Player data pin differs: "+str(name))
        inputs[str(path.absolute())]=pin["sha256"]
    paths=production_paths()+[ROOT/name for name in EXTRA_PRODUCTION]
    production={p.relative_to(ROOT).as_posix():p.read_bytes() for p in paths}
    harness={name:(ROOT/name).read_bytes() for name in HARNESS}
    source_pins={name:sha(raw) for name,raw in production.items()}
    result=dict(schema="defensive-presnap-switch-qualification/v1",status="failed",commands=[],runs={},
                controls_requested=args.controls,controls=[],source_sha256=source_pins,inputs_sha256=inputs,
                harness_sha256={name:sha(raw) for name,raw in harness.items()},
                actual_factory="SourceC2Composition.CreatePreSnapSwitchSimulation",
                native_Unity_DSP_or_whole_feature_qualified=False)
    work=output/"work";work.mkdir();(work/"empty-feed").mkdir()
    env=dict(os.environ,TMPDIR=str(work),TMP=str(work),TEMP=str(work),DOTNET_CLI_TELEMETRY_OPTOUT="1",
             DOTNET_NOLOGO="1",MSBUILDDISABLENODEREUSE="1",PYTHONDONTWRITEBYTECODE="1")
    def execute(command,label,timeout=240):
        start=time.monotonic()
        process=subprocess.run(command,cwd=ROOT,env=env,capture_output=True,text=True,timeout=timeout)
        text=process.stdout+process.stderr
        (output/(label+".log")).write_text(text)
        result["commands"].append(dict(command=command,exit=process.returncode,seconds=round(time.monotonic()-start,3),
            log=label+".log",diagnostics=sorted(set(line for line in text.splitlines()
                if re.search(r"\b(?:error|warning) (?:CS|NETSDK|MSB|NU)\d+",line)))))
        return process.returncode,text
    def build(project,label,common):
        command=["dotnet","build",str(project),"--no-restore","-t:Rebuild","-c","Release","-v:q","--nologo",
                 "-p:UseSharedCompilation=false","-p:TreatWarningsAsErrors=true",*common]
        code,text=execute(command,label+"-build")
        if code and "NETSDK1004" in text:
            code,_=execute(["dotnet","restore",str(project),"--source",str(work/"empty-feed"),"-v:q",
                            "-p:NuGetAudit=false",*common],label+"-restore")
            if code:raise RuntimeError("Restore failed after actual missing SDK assets: "+label)
            code,text=execute(command,label+"-build-restored")
        if code or "0 Warning(s)" not in text or "0 Error(s)" not in text:
            raise RuntimeError("Zero-diagnostic fresh compilation required: "+label)
    def focused(label,spec=None):
        copy=output/"copies"/label
        source,tests=copy/"source",copy/"harness"
        for base,files in ((source,production),(tests,harness)):
            for name,raw in files.items():
                if spec and base==source and name==spec[1]:raw=apply(raw,spec)
                target=base/name;target.parent.mkdir(parents=True,exist_ok=True);target.write_bytes(raw)
        project=tests/"tests/defensive-presnap-switch/PreSnapSwitch.Checks.csproj"
        directory=output/"build"/label
        common=["-p:BlitzSourceRoot="+str(source)+"/","-p:BaseIntermediateOutputPath="+str(directory/"obj")+"/",
                "-p:BaseOutputPath="+str(directory/"bin")+"/"]
        build(project,label,common)
        destination=output/label
        command=["dotnet",str(directory/"bin/Release/net10.0/PreSnapSwitch.Checks.dll"),
                 str(args.fixture_manifest.absolute()),str(args.sound_functions.absolute()),
                 str(args.presnap_switch_inputs.absolute()),str(args.old_c2_inputs.absolute()),
                 str(args.c2_m1_inputs.absolute()),str(output/"original/oracle.json"),str(destination),
                 str(args.player_data.absolute())]
        code,_=execute(command,label)
        receipt=json.loads((destination/"focused-result.json").read_bytes())
        result["runs"][label]=dict(exit=code,zero_diagnostics=True,receipt=receipt)
        require_result(code,receipt,spec[4] if spec else None)
        if spec:
            result["controls"].append(dict(id=spec[0],actual_source=spec[1],named_kill=receipt["failed_assertion"],
                                           original_sha256=sha(production[spec[1]]),
                                           mutated_sha256=sha(apply(production[spec[1]],spec)),
                                           fresh_compile_zero_diagnostics=True))
        shutil.rmtree(copy)
    started=time.monotonic()
    code=1
    try:
        reference=oracle.generate(args.source_root,args.presnap_switch_inputs,output/"original")
        result["original_C"]=dict(cases=len(reference["cases"]),VM_offsets=len(reference["vm"]),
                                  extracted_bodies=reference["extracted_bodies"],diagnostics=reference["compiler_diagnostics"])
        if args.sdk_netstandard21:
            build(HERE/"PreSnapSwitch.Production.csproj","production-netstandard21",[
                "-p:BaseIntermediateOutputPath="+str(output/"sdk/obj")+"/",
                "-p:BaseOutputPath="+str(output/"sdk/bin")+"/"])
        focused("baseline")
        if args.controls:
            for spec in MUTATIONS+ROOT_MUTATIONS:focused("control-"+spec[0],spec)
        focused("final-baseline")
        props=output/"host-build.props"
        props.write_text("<Project><PropertyGroup><BaseIntermediateOutputPath>"+str(output)+
            "/host/obj/$(MSBuildProjectName)/</BaseIntermediateOutputPath><BaseOutputPath>"+str(output)+
            "/host/bin/$(MSBuildProjectName)/</BaseOutputPath></PropertyGroup></Project>")
        build(ROOT/"tools/source-player/SourcePlayer.Checks.csproj","actual-host",["-p:DirectoryBuildPropsPath="+str(props)])
        code,text=execute(["dotnet",str(output/"host/bin/SourcePlayer.Checks/Release/net10.0/SourcePlayer.Checks.dll"),
            str(args.player_data.absolute()),"--defensive-presnap-switch-only","--proof-output",str(output/"raw-host")],
            "actual-raw-host",timeout=300)
        if code:raise RuntimeError("The actual public raw-host path failed: "+text[-2500:])
        result["status"]="passed-focused-not-whole-feature";code=0
    except (OSError,ValueError,RuntimeError,subprocess.SubprocessError) as error:
        result["error"]=str(error)
        print(str(error),file=sys.stderr)
    finally:
        result["seconds"]=round(time.monotonic()-started,3)
        result["source_unchanged"]={p.relative_to(ROOT).as_posix():sha(p.read_bytes()) for p in paths}==source_pins
        result["inputs_unchanged"]=all(sha(Path(name).read_bytes())==pin for name,pin in inputs.items())
        if not result["source_unchanged"] or not result["inputs_unchanged"]:
            result["status"]="inconclusive-frozen-input-changed";code=1
        copies=output/"copies"
        if copies.exists():shutil.rmtree(copies)
        (output/"result.json").write_text(json.dumps(result,indent=2)+"\n")
    print(json.dumps(dict(status=result["status"],report=str(output/"result.json"),seconds=result["seconds"])))
    return code


if __name__=="__main__":
    sys.exit(main())
