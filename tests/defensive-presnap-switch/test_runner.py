import argparse
import importlib.util
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys
import time
import unittest

HERE=Path(__file__).resolve().parent
ROOT=HERE.parents[1]
sys.path.insert(0,str(HERE))
import run
from mutations import MUTATIONS,ROOT_MUTATIONS,apply


def root_boundary(argv):
    parser=argparse.ArgumentParser(description="Actual-C root boundary only; no host, authoring or replay window.")
    for name in ("source-root","fixture-manifest","sound-functions","old-c2-inputs","c2-m1-inputs",
                 "presnap-switch-inputs","player-data","output"):
        parser.add_argument("--"+name,type=Path,required=True)
    parser.add_argument("--sdk-netstandard21",action="store_true")
    parser.add_argument("--controls",action="store_true")
    parser.add_argument("--checks-assets",type=Path)
    parser.add_argument("--sdk-assets",type=Path)
    args=parser.parse_args(argv)
    output=args.output.absolute()
    if output.exists() or output.resolve()!=output or not output.is_relative_to(ROOT/".squad/.scratch"):
        raise ValueError("Choose a new ignored root-boundary output; preserve earlier evidence.")
    output.mkdir(parents=True)
    run.fixture_profile(args.fixture_manifest,"contact272")
    inputs=run.fixture_hashes(args.fixture_manifest,args.sound_functions)
    inputs.update(run.successor_hashes(args.presnap_switch_inputs))
    inputs.update(run.canonical_hashes(args.old_c2_inputs))
    inputs.update(run.canonical_hashes(args.c2_m1_inputs,"contact281-c2-m1"))
    manifest=json.loads((args.player_data/"manifest.json").read_bytes())
    if manifest.get("profile")!="original-teams-contact282-c2-m1-presnap-switch" or len(manifest["files"])!=563:
        raise ValueError("The exact actual563-file successor player bundle is required.")
    for pin in manifest["files"]:
        relative=Path(pin["path"]); path=(args.player_data/relative).absolute()
        if relative.is_absolute() or ".." in relative.parts or path.resolve()!=path or run.sha(path.read_bytes())!=pin["sha256"]:
            raise ValueError("Player input differs: "+str(relative))
        inputs[str(path)]=pin["sha256"]
    paths=run.production_paths()+[ROOT/name for name in run.EXTRA_PRODUCTION]
    production={p.relative_to(ROOT).as_posix():p.read_bytes() for p in paths}
    harness={name:(ROOT/name).read_bytes() for name in run.HARNESS}
    result=dict(schema="defensive-presnap-root-boundary/v1",status="failed",commands=[],runs={},
                controls=[],inputs_sha256=inputs,source_sha256={p:run.sha(raw) for p,raw in production.items()},
                harness_sha256={p:run.sha(raw) for p,raw in harness.items()},
                public_authoring_windows_started=0,whole_or_native_qualified=False)
    work=output/"work"; work.mkdir(); (work/"empty-feed").mkdir()
    env=dict(os.environ,TMPDIR=str(work),TMP=str(work),TEMP=str(work),PYTHONDONTWRITEBYTECODE="1",
             DOTNET_CLI_TELEMETRY_OPTOUT="1",DOTNET_NOLOGO="1",MSBUILDDISABLENODEREUSE="1")
    def execute(command,label):
        start=time.monotonic()
        process=subprocess.run(command,cwd=ROOT,env=env,capture_output=True,text=True,timeout=240)
        text=process.stdout+process.stderr
        (output/(label+".log")).write_text(text)
        result["commands"].append(dict(command=command,exit=process.returncode,log=label+".log",
            log_sha256=run.sha(text.encode()),seconds=round(time.monotonic()-start,3),
            diagnostics=sorted(set(line for line in text.splitlines()
                if re.search(r"\b(?:error|warning) (?:CS|NETSDK|MSB|NU)\d+",line)))))
        return process.returncode,text
    def build(project,label,common,assets):
        if assets:
            if not assets.is_file():raise ValueError("Explicit restored project assets are missing: "+str(assets))
            common=common+["-p:ProjectAssetsFile="+str(assets.absolute())]
        command=["dotnet","build",str(project),"--no-restore","-t:Rebuild","-c","Release","-v:q","--nologo",
                 "-p:UseSharedCompilation=false","-p:TreatWarningsAsErrors=true",*common]
        code,text=execute(command,label+"-build")
        if code and "NETSDK1004" in text and not assets:
            code,_=execute(["dotnet","restore",str(project),"--source",str(work/"empty-feed"),"-v:q",
                            "-p:NuGetAudit=false",*common],label+"-restore-after-actual-missing-assets")
            if code:raise RuntimeError("SDK restore failed after the actual chosen missing-assets error.")
            code,text=execute(command,label+"-build-restored")
        if code or "0 Warning(s)" not in text or "0 Error(s)" not in text:
            raise RuntimeError("A fresh zero-diagnostic build is required: "+label)
    def focused(label,spec=None):
        directory=output/label
        for part,files in (("source",production),("harness",harness)):
            for name,raw in files.items():
                if spec and part=="source" and name==spec[1]:raw=apply(raw,spec)
                path=directory/"copy"/part/name
                path.parent.mkdir(parents=True,exist_ok=True);path.write_bytes(raw)
        common=["-p:BlitzSourceRoot="+str(directory/"copy/source")+"/",
                "-p:BaseIntermediateOutputPath="+str(directory/"obj")+"/",
                "-p:BaseOutputPath="+str(directory/"bin")+"/"]
        build(directory/"copy/harness/tests/defensive-presnap-switch/PreSnapSwitch.Checks.csproj",
              label,common,args.checks_assets)
        code,_=execute(["dotnet",str(directory/"bin/Release/net10.0/PreSnapSwitch.Checks.dll"),
            str(args.fixture_manifest.absolute()),str(args.sound_functions.absolute()),
            str(args.presnap_switch_inputs.absolute()),str(args.old_c2_inputs.absolute()),
            str(args.c2_m1_inputs.absolute()),str(output/"original/oracle.json"),
            str(directory/"observed"),str(args.player_data.absolute())],label)
        receipt=json.loads((directory/"observed/focused-result.json").read_bytes())
        result["runs"][label]=dict(exit=code,fresh_compile_zero_diagnostics=True,receipt=receipt)
        run.require_result(code,receipt,spec[4] if spec else None)
        if spec:
            if not receipt["failed_assertion"].startswith("PRE_SNAP_ASSERTION: "+spec[4]):
                raise RuntimeError("The exact named root-boundary assertion did not kill the compiled mutant.")
            result["controls"].append(dict(id=spec[0],source=spec[1],old=spec[2],new=spec[3],
                original_sha256=run.sha(production[spec[1]]),
                mutated_sha256=run.sha(apply(production[spec[1]],spec)),
                named_kill=receipt["failed_assertion"],fresh_compile_zero_diagnostics=True))
        elif (receipt.get("interrupted_VM_offsets")!=232 or receipt.get("secondary_VM_offsets")!=116 or
              receipt.get("root_boundary_offsets")!=[24,39,40,113,114,115]):
            raise RuntimeError("Source-header interruptions, secondary roots and the full stance continuation are mandatory.")
        shutil.rmtree(directory/"copy")
    code=1
    try:
        reference=run.oracle.generate(args.source_root,args.presnap_switch_inputs,output/"original")
        result["original_C"]=dict(extracted_bodies=reference["extracted_bodies"],
            compiler_diagnostics=reference["compiler_diagnostics"],commands=reference["commands"],
            executions=reference["executions"],
            main_offsets=len(reference["vm"]),interrupted_offsets=sum(len(x["rows"]) for x in reference["vm_interrupted"]),
            secondary_offsets=len(reference["vm_secondary"]),
            oracle_sha256=run.sha((output/"original/oracle.json").read_bytes()))
        if args.sdk_netstandard21:
            build(HERE/"PreSnapSwitch.Production.csproj","production-netstandard21",[
                "-p:BaseIntermediateOutputPath="+str(output/"sdk/obj")+"/",
                "-p:BaseOutputPath="+str(output/"sdk/bin")+"/"],args.sdk_assets)
        focused("baseline")
        if args.controls:
            for spec in ROOT_MUTATIONS+tuple(x for x in MUTATIONS if x[0]=="immediate-entry-double-advance"):
                focused("control-"+spec[0],spec)
        focused("final-baseline")
        result["status"]="passed-root-boundary-not-whole-feature";code=0
    except (OSError,ValueError,RuntimeError,subprocess.SubprocessError) as error:
        result["error"]=str(error);print(str(error),file=sys.stderr)
    finally:
        result["source_unchanged"]={p.relative_to(ROOT).as_posix():run.sha(p.read_bytes()) for p in paths}==result["source_sha256"]
        result["inputs_unchanged"]=all(run.sha(Path(p).read_bytes())==pin for p,pin in inputs.items())
        if not result["source_unchanged"] or not result["inputs_unchanged"]:
            result["status"]="inconclusive-input-changed";code=1
        for copy in output.glob("*/copy"):shutil.rmtree(copy)
        (output/"result.json").write_text(json.dumps(result,indent=2)+"\n")
    print(json.dumps(dict(status=result["status"],report=str(output/"result.json"))))
    return code


class RunnerTests(unittest.TestCase):
    def test_ten_controls_change_real_unique_production_anchors(self):
        self.assertEqual(len(MUTATIONS),10)
        self.assertEqual(len({m[0] for m in MUTATIONS}),10)
        for spec in MUTATIONS:
            raw=(ROOT/spec[1]).read_bytes()
            changed=apply(raw,spec)
            self.assertNotEqual(raw,changed,spec[0])
            self.assertEqual(changed.count(spec[3].encode()),raw.count(spec[3].encode())+1,spec[0])

    def test_original_acquisition_is_exact_union_without_relabeling_old69_or78(self):
        sys.path.insert(0,str(ROOT/"tests/parity"))
        import fetch_inputs
        legacy=fetch_inputs.input_pins("legacy")
        contact=fetch_inputs.input_pins("contact272")
        selected=json.loads((HERE/"source-inputs.json").read_bytes())["inputs"]
        actual=fetch_inputs.input_pins("defensive-presnap-switch")
        self.assertEqual(len(legacy),69)
        self.assertEqual(len(contact),78)
        self.assertEqual(actual,{**contact,**selected})

    def test_root_boundary_controls_change_real_production_and_keep_original_ten(self):
        self.assertEqual(len(ROOT_MUTATIONS),5)
        self.assertEqual(len({m[0] for m in MUTATIONS+ROOT_MUTATIONS}),15)
        for spec in ROOT_MUTATIONS:
            raw=(ROOT/spec[1]).read_bytes()
            self.assertNotEqual(raw,apply(raw,spec),spec[0])
            self.assertTrue(spec[4].startswith("original VM "),spec[0])
        self.assertEqual(ROOT_MUTATIONS[0][4],"original VM root-Y offset24;")

    def test_complete_runner_declares_all_fifteen_controls_without_replacing_original_ten(self):
        self.assertEqual([m[0] for m in run.MUTATIONS], [m[0] for m in MUTATIONS])
        self.assertEqual([m[0] for m in run.ROOT_MUTATIONS], [m[0] for m in ROOT_MUTATIONS])
        text=(HERE/"run.py").read_text()
        self.assertIn("for spec in MUTATIONS+ROOT_MUTATIONS:",text)
        self.assertIn('"-t:Rebuild"',text)

    def test_u2_current_source_and_harness_are_separate_from_three_immutable_history_generations(self):
        sys.path.insert(0,str(ROOT/"tests/movement-callers"))
        import check_replay_generations as generations
        historical,identity=generations.current_snapshot()
        live,actual=generations.u2_current_snapshot()
        self.assertEqual(identity["commit"],"c339efcccc7caf82fb1c3533bf952dc4d3f3600e")
        self.assertEqual(identity["source_manifest_sha256"],
            "a876fa4b6c286043d9791624904852b6ad7cbfef98f0eaf4d186feb4ac3f20fd")
        self.assertEqual(identity["harness_manifest_sha256"],
            "ea910e3039fc8f39b4958c83a535eb5e81c2f3ec29dbfbf15757ece675206b91")
        self.assertEqual(actual["source_manifest_sha256"],generations.U2_CURRENT_SOURCE_SHA256)
        self.assertEqual(actual["harness_manifest_sha256"],generations.U2_HARNESS_SHA256)
        self.assertNotEqual(identity["source_manifest_sha256"],actual["source_manifest_sha256"])
        self.assertNotEqual(identity["harness_manifest_sha256"],actual["harness_manifest_sha256"])
        controller="tests/movement-callers/PortablePublicPolicy.cs"
        self.assertEqual(historical[controller],live[controller])
        for name in ("Assets/Scripts/Gameplay/SourceAnimationState.cs","tests/movement-callers/Canonical.cs"):
            changed=dict(live);changed[name]+=b"\n"
            with self.assertRaisesRegex(ValueError,"payload"):
                generations.validate_snapshot(changed,78,generations.U2_CURRENT_SOURCE_SHA256,
                    generations.U2_HARNESS_SHA256)

    def test_both_full_generations_keep_the_actual_original_C_output_guard(self):
        sys.path.insert(0,str(ROOT/"tests/movement-callers"))
        import canonical
        output=ROOT/".squad/.scratch/defensive-switch-u2-remaining/uncreated-output-contract"
        old=canonical.post_vm_output(output)
        new=canonical.post_vm_output(output,True)
        for path in (old,new):
            self.assertTrue(path.is_relative_to(ROOT/"tests/movement-callers/.artifacts"))
            self.assertNotEqual(path,output/"original-post-VM")
        self.assertNotEqual(old,new)
        self.assertEqual(old.name,"m1-post-VM-"+run.sha(str(output).encode())[:16])

    def test_control_success_requires_named_failure_after_nonzero_real_assertions(self):
        good=dict(schema="defensive-presnap-switch-checks/v1",status="failed",assertions=12,
                  failed_assertion="PRE_SNAP_ASSERTION: occupied-skip")
        run.require_result(1,good,"occupied-skip")
        for code,value in ((0,good),(1,{**good,"assertions":0}),(1,{**good,"failed_assertion":"compiler failed"}),
                           (1,None),(1,{**good,"status":"passed"})):
            with self.assertRaises(RuntimeError):run.require_result(code,value,"occupied-skip")

    def test_baseline_requires_every_original_case_and_VM_inventory(self):
        good=dict(schema="defensive-presnap-switch-checks/v1",status="passed",assertions=4000,
                  original_C_station_cases=70,original_C_caller_cases=18,legal_ordered_randrng10_values=10,
                  actual_VM_offsets=116,interrupted_VM_offsets=232,secondary_VM_offsets=116,
                  root_boundary_offsets=[24,39,40,113,114,115],
                  factory="SourceC2Composition.CreatePreSnapSwitchSimulation")
        run.require_result(0,good)
        for field in ("original_C_station_cases","original_C_caller_cases","legal_ordered_randrng10_values",
                      "actual_VM_offsets","interrupted_VM_offsets","secondary_VM_offsets","root_boundary_offsets","factory"):
            with self.assertRaises(RuntimeError):run.require_result(0,{**good,field:None})

    def test_production_projects_link_actual_sound_composition_and_cinematic_consumer(self):
        for name in ("PreSnapSwitch.Checks.csproj","PreSnapSwitch.Production.csproj"):
            text=(HERE/name).read_text()
            self.assertIn("../audio/vm/SourceSoundVmSources.props",text)
            self.assertIn("../parity/FullKernelSources.props",text)
            self.assertIn("SourceCinematicPlayback.cs",text)
            self.assertIn("SourceSoundVmTransport.cs",text)
            self.assertIn("<LangVersion>7.3</LangVersion>",text)


if __name__=="__main__":
    if len(sys.argv)>1 and sys.argv[1]=="--root-boundary":sys.exit(root_boundary(sys.argv[2:]))
    unittest.main()
