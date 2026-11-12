"""Opt-in finite production-caller checks; never run the default P1 matrix."""
import argparse
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import time
import xml.etree.ElementTree as ET

import oracle
from support import ROOT, SUITE, GateError, blob_sha1, require_recipe, sha256, verify_inputs


FIXTURE_SHA256 = "bc4e0fefc0f86ced01c733cf208abb8471b77ad35815c4d31c685e0de1538d75"
SOUND_SHA256 = "4a557304d3b041ee04dff8ed1354714fc5665abaeb1c3d458d94171adf1a0280"
CONTROL = (
    "Assets/Scripts/Gameplay/GameSimulation.Source.cs",
    "foreach (SourceDefenderEffect effect in step.DefenderEffects)",
    "foreach (SourceDefenderEffect effect in new SourceDefenderEffect[0])",
)
CONTROL_FAILURE = "native actor12 state-int1 actual=0 native=64"
CASE_NAMES = (
    "P1/receiver/hail-mary/far", "P1/receiver/hail-mary/near",
    "P1/receiver/upper-cut/far", "P1/receiver/upper-cut/near",
    "P1/receiver/ordered-defender-consumer", "P1/receiver/delay-block-to-live-blocker",
    "P1/receiver/terminal-block", "P1/defense/man-zone-blitz",
    "P1/defense/zone-widths", "P1/defense/all-blitz-rushers",
    "P1/defense/fake-zone-to-man-native-timer", "P1/catalog/active-word-reachability",
    "P1/receiver/first-juke-far-skip", "P1/receiver/real-cover-fake-not-sold",
    "P1/receiver/source256-fake-to-real-cover-ghost-consumer",
)
EXTRA = r"""
static void supplied_cache(void) {
    for(int i=0;i<14;i++)for(int j=0;j<14;j++){distxz[i][j]=nf();p2p_angles[i][j]=ni();}
    supplied_geometry=1;
}
static char *pending_script(char *name) {
    if(!strcmp(name,"-"))return NULL;
    if(!strcmp(name,"c_jmpint_anim"))return &c_jmpint_anim;
    if(!strcmp(name,"juke_c_anim"))return &juke_c_anim;
    if(!strcmp(name,"spin_anim"))return &spin_anim;
    if(!strcmp(name,"r_histep_anim"))return &r_histep_anim;
    if(!strcmp(name,"m_wavqb_anim"))return &m_wavqb_anim;
    if(!strcmp(name,"m_wavqbf_anim"))return &m_wavqbf_anim;
    if(!strcmp(name,"c_no_vel1_anim"))return &c_no_vel1_anim;
    if(!strcmp(name,"c_no_vel2_anim"))return &c_no_vel2_anim;
    fprintf(stderr,"unknown actual pending script\n");abort();
}
static void supplied_state(void) {
    int i=ni();if(i<0||i>=14)abort();drone_t *d=&drone_blocks[i];
    int *start=get_route(ns());int word=ni();d->pscript=start+word;
    d->flags=ni();d->stick=ni();d->old_stick=ni();d->os_time=ni();
    d->but=ni();d->old_but=ni();d->ob_time=ni();(void)ni();d->target=ni();
    d->pass_react_time=ni();d->wait_time=ni();d->block_time=ni();d->fake_time=ni();
    d->catch_time=ni();d->evade_time=ni();d->think_time=ni();d->blitz_time=ni();
    d->move_time=ni();d->order_time=ni();d->order_dir=ni();player_blocks[i].fake_zone_time=ni();
    d->mx=nf();d->mz=nf();d->gx=nf();d->gz=nf();d->vgx=nf();d->vgz=nf();
    d->zxlow=nf();d->zxhigh=nf();d->zzlow=nf();d->zzhigh=nf();
    d->zpoint[0]=nf();d->zpoint[1]=nf();d->zpv[0]=nf();d->zpv[1]=nf();
    d->pending_catch=pending_script(ns());
}
static void clear_actor_tape(void) {raw_length=raw_cursor=0;}
static void initial_dbps(void) {
    int actor=ni();pdrone=&drone_blocks[actor];
    pdrone->pscript=get_route(ns());player_blocks[actor].job=JOB_NONE;
    pdrone->zxlow=pdrone->zxhigh=pdrone->zzlow=pdrone->zzhigh=0;
    int base=rng_count;_drone_dbps(&player_blocks[actor]);emit_state(actor,base);
}
"""


def replace_once(text, old, new):
    if text.count(old) != 1:
        raise GateError("Live-caller boundary/producer anchor must occur exactly once: " + old)
    return text.replace(old, new)


def projected_driver(driver):
    start = driver.index("static void step(void)")
    end = driver.index("static void numeric(void)", start)
    body = driver[start:end]
    split = body.index(" drone_t *d=&drone_blocks[actor];")
    emit = "static void emit_state(int actor,int base) {\n" + body[split:]
    driver = driver[:start] + emit + body[:split] + "\n emit_state(actor,base);\n}\n" + driver[end:]
    driver = replace_once(driver, "static char input[32768],*token;",
                          "static char input[32768],*token;\nstatic int supplied_geometry;")
    if driver.count("geometry();") != 3:
        raise GateError("Expected all three retained native geometry boundaries.")
    driver = driver.replace("geometry();", "if(!supplied_geometry)geometry();")
    driver = replace_once(driver, "rng_count=0;raw_length=0;raw_cursor=0;",
                          "rng_count=0;raw_length=0;raw_cursor=0;supplied_geometry=0;")
    start = driver.index("static void actor(void)")
    end = driver.index("static void route(void)", start)
    pose = driver[start:end].replace("static void actor(void)", "static void pose(void)")
    # Later managed observations must not replace independently replayed native targets/jobs.
    pose = replace_once(pose, "p->job=ni();drone_blocks[i].target=ni();",
                         "int job=ni();if(job!=-999)p->job=job;(void)ni();")
    driver = replace_once(driver, "int main(void){", pose + EXTRA + "\nint main(void){")
    return replace_once(driver, 'else if(!strcmp(token,"quit"))break;',
        'else if(!strcmp(token,"cache"))supplied_cache();\n'
        '  else if(!strcmp(token,"state"))supplied_state();\n'
        '  else if(!strcmp(token,"pose"))pose();\n'
        '  else if(!strcmp(token,"clear-tape"))clear_actor_tape();\n'
        '  else if(!strcmp(token,"update-receivers")){update_receiver_data();puts("UPDATED");fflush(stdout);}\n'
        '  else if(!strcmp(token,"dump")){int actor=ni();emit_state(actor,rng_count);}\n'
        '  else if(!strcmp(token,"initial-dbps"))initial_dbps();\n'
        '  else if(!strcmp(token,"abort-route")){int actor=ni();pdrone=&drone_blocks[actor];abort_drone_route(actor);}\n'
        '  else if(!strcmp(token,"quit"))break;')


def build_native(original_root, output, recipe, environment):
    protocol = output / "protocol"
    protocol.mkdir(parents=True)
    base, functions = oracle.SUITE, oracle.FUNCTIONS
    boundary = (base / "boundaries.c").read_bytes()
    driver = projected_driver((base / "driver.c").read_text(encoding="utf-8"))
    (protocol / "boundaries.c").write_bytes(boundary)
    (protocol / "driver.c").write_text(driver, encoding="utf-8")
    try:
        oracle.SUITE = protocol
        oracle.FUNCTIONS = [*functions, ("drone.c", "abort_drone_route")]
        receipt = oracle.build(original_root, output / "oracle", recipe, environment)
    finally:
        oracle.SUITE, oracle.FUNCTIONS = base, functions
    if (receipt["selected_function_count"] != 54
            or any(item["expression_rewrites"] != 0 for item in receipt["selected_functions"])):
        raise GateError("Require retained53 functions plus unchanged abort_drone_route, without expression rewrites.")
    receipt["public_caller_projection"] = {
        "boundary_sha256": sha256(boundary),
        "retained_driver_sha256": sha256((base / "driver.c").read_bytes()),
        "extended_driver_sha256": sha256(driver.encode()),
        "actual_cached_geometry_supplied": True,
        "actual_pre_step_drone_state_supplied": True,
        "native_targets_not_overwritten_from_managed_outputs": True,
        "original_function_expressions_changed": False,
        "target_ABI_scheduler_or_libc_RNG_claim": False,
    }
    write_json(output / "public-caller-provenance.json", receipt)
    return output / "oracle/source-oracle", receipt


def write_json(path, value):
    path.write_text(json.dumps(value, indent=2) + "\n", encoding="utf-8")


def hashes(paths):
    return {str(path): sha256(path.read_bytes()) for path in sorted(paths)}


def build_inputs():
    project = SUITE / "LiveCallerChecks.csproj"
    paths = {project}
    pending = [project]
    while pending:
        current = pending.pop()
        tree = ET.parse(current)
        for item in tree.findall(".//Compile"):
            pattern = item.attrib["Include"]
            if pattern.startswith("$(BlitzSourceRoot)"):
                paths.update(ROOT.glob(pattern.replace("$(BlitzSourceRoot)", "", 1)))
            else:
                paths.add((current.parent / pattern).resolve())
        for item in tree.findall("./Import"):
            dependency = (current.parent / item.attrib["Project"]).resolve()
            if dependency not in paths:
                paths.add(dependency)
                pending.append(dependency)
    if any(not p.is_file() or p.is_symlink() or not p.is_relative_to(ROOT) for p in paths):
        raise GateError("Actual managed project dependencies must be readable regular repository files.")
    return paths


def input_files(root, count):
    files = []
    for path in root.rglob("*"):
        if path.is_symlink() or path.name == ".env" or path.name.startswith(".env."):
            raise GateError("Input closure must contain only regular authenticated game inputs.")
        if path.is_file():
            files.append(path)
    if len(files) != count:
        raise GateError("Require exact authenticated input closure: " + str(root) + " (" + str(count) + " files).")
    return files


def create_output(path, inputs):
    path = path.absolute()
    if (path.exists() or path.is_symlink() or path.resolve() != path
            or any(path == root or path.is_relative_to(root) for root in inputs)):
        raise GateError("--output must be a fresh physical directory, outside authenticated inputs.")
    path.mkdir()
    return path


def require_finite(code, report):
    if (code != 0 or not isinstance(report, dict) or report.get("status") != "passed"
            or report.get("qualified") is not False or report.get("original_C_compared") is not True
            or report.get("scope") != "finite public scheduled-route caller evidence"
            or any(type(report.get(key)) is not int or report[key] != value for key, value in
                   (("assertions", 277768), ("nativeWakes", 511), ("assignmentFramesWithUnverifiedTies", 0)))
            or [case.get("name") for case in report.get("cases", [])] != list(CASE_NAMES)):
        raise GateError("Require the exact15 named finite cases/511 native wakes/277768 assertions/no qsort ties.")


def require_disconnect(code, report):
    if (code != 1 or not isinstance(report, dict) or report.get("status") != "failed"
            or CONTROL_FAILURE not in report.get("error", "")
            or type(report.get("nativeWakes")) is not int or report["nativeWakes"] <= 0):
        raise GateError("Require the actual ordered defender producer disconnect, not a crash/build error/survivor.")


def arguments(argv):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source-root", type=Path, required=True, help="Authenticated14-file original source root.")
    parser.add_argument("--fixture-manifest", type=Path, required=True, help="Authenticated Contact272 fixture/full835 tree.")
    parser.add_argument("--sound-functions", type=Path, required=True, help="Unchanged authenticated SAFN.")
    parser.add_argument("--c2-m1-inputs", type=Path, required=True, help="Genuine authenticated M1288 root.")
    parser.add_argument("--output", type=Path, required=True, help="Fresh output with an existing physical parent.")
    parser.add_argument("--dotnet", default=os.environ.get("SOURCE_PLAYER_DOTNET", "dotnet"), help="Existing .NET10 SDK executable.")
    return parser.parse_args(argv)


def main(argv=None):
    args = arguments(argv)
    original = args.source_root.resolve()
    fixture, sound, m1 = (p.resolve() for p in (args.fixture_manifest, args.sound_functions, args.c2_m1_inputs))
    try:
        output = create_output(args.output, (original, fixture.parent, m1, sound.parent))
    except (OSError, GateError) as error:
        print(str(error), file=sys.stderr)
        return 2
    report = {
        "schema": "source-playbook-live-caller-delivery/v1", "status": "failed", "commands": [],
        "input_roots": {"original": str(original), "fixture": str(fixture), "sound": str(sound), "m1": str(m1)},
        "historical_qualification_reminted": False, "full_gameplay_parity_claimed": False,
        "boundary": "Home/FourStation/two humans; declared live/layout/handoff; host C, not target ABI/RNG.",
    }
    before = input_before = original_before = None
    started = time.monotonic()
    work = output / "work"
    work.mkdir()
    environment = dict(os.environ, TMPDIR=str(work), TMP=str(work), TEMP=str(work),
        PYTHONDONTWRITEBYTECODE="1", DOTNET_CLI_HOME=str(work / "cli"), NUGET_PACKAGES=str(work / "nuget"),
        DOTNET_NOLOGO="1", DOTNET_CLI_TELEMETRY_OPTOUT="1", DOTNET_EnableDiagnostics="0",
        DOTNET_CLI_USE_MSBUILD_SERVER="0", MSBUILDDISABLENODEREUSE="1")

    def run(label, command):
        start = time.monotonic()
        result = subprocess.run([str(x) for x in command], cwd=ROOT, env=environment,
                                capture_output=True, text=True, timeout=240)
        text = result.stdout + result.stderr
        (output / (label + ".log")).write_text(text, encoding="utf-8")
        report["commands"].append({"label": label, "command": [str(x) for x in command],
            "exit_code": result.returncode, "seconds": round(time.monotonic() - start, 3)})
        return result.returncode, text

    def compile_checks(label, source, build):
        common = ["-p:BaseIntermediateOutputPath=" + str(build / "obj") + "/",
                  "-p:MSBuildProjectExtensionsPath=" + str(build / "obj") + "/",
                  "-p:BaseOutputPath=" + str(build / "bin") + "/"]
        project = source / "tests/playbooks/LiveCallerChecks.csproj"
        command = [args.dotnet, "build", project, "--no-restore", "-c", "Release", "-v", "quiet", *common]
        code, text = run(label + "-build", command)
        if code and "NETSDK1004" in text:
            empty = work / "empty-feed"
            empty.mkdir(exist_ok=True)
            code, _ = run(label + "-restore", [args.dotnet, "restore", project, "--source", empty,
                                                "-p:NuGetAudit=false", "-v", "quiet", *common])
            if code:
                raise GateError("Missing-reference restore failed, not a behavioral rejection: " + label)
            code, text = run(label + "-rebuild", command)
        if code or "0 Warning(s)" not in text or "0 Error(s)" not in text:
            raise GateError("C#7.3 compilation must have zero warnings/errors: " + label)
        dll = build / "bin/Release/net10.0/LiveCallerChecks.dll"
        shutil.copyfile(dll, output / (label + ".dll"))
        return dll

    def check(label, dll, native):
        target = output / label
        target.mkdir()
        receipt = target / "cases.json"
        code, _ = run(label, [args.dotnet, dll, fixture, sound, m1, receipt, "finite", native])
        return code, json.loads(receipt.read_bytes())

    def copy_sources(destination, contents):
        for name, data in contents.items():
            target = destination / name
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_bytes(data)
            target.chmod(0o400)

    try:
        original_before = {name: blob_sha1(data) for name, data in verify_inputs(original).items()}
        recipe = json.loads((ROOT / "tools/playbooks/recipe.json").read_bytes())
        require_recipe(recipe)
        if sha256(fixture.read_bytes()) != FIXTURE_SHA256 or sha256(sound.read_bytes()) != SOUND_SHA256:
            raise GateError("Require the exact qualified Contact272 fixture and unchanged SAFN.")
        inputs = set(input_files(fixture.parent, 835) + input_files(m1, 288) + [sound])
        input_before = hashes(inputs)
        dependencies = build_inputs()
        production = set((ROOT / "Assets/Scripts").rglob("*.cs"))
        observed = production | dependencies | {
            SUITE / name for name in ("run.py", "live_callers.py", "oracle.py", "support.py",
                                      "pins.json", "driver.c", "boundaries.c", "README.md", "test_runner.py")}
        observed.add(ROOT / "tools/playbooks/recipe.json")
        before = hashes(observed)
        contents = {p.relative_to(ROOT).as_posix(): p.read_bytes() for p in dependencies}
        if before != hashes(observed):
            raise GateError("Repository changed during actual compilation-closure capture.")
        report.update(repository_sha256_before={str(Path(k).relative_to(ROOT)): v for k, v in before.items()},
                      input_sha256_before=input_before, original_blob_sha1_before=original_before,
                      compiled_dependency_sha256={k: sha256(v) for k, v in contents.items()})
        candidate, mutant = work / "candidate", work / "mutant"
        copy_sources(candidate, contents)
        native, report["original_c"] = build_native(original, output / "native", recipe, environment)
        build = work / "candidate-build"
        dll = compile_checks("baseline", candidate, build)
        code, baseline = check("baseline", dll, native)
        require_finite(code, baseline)
        report["baseline"] = {"cases": len(baseline["cases"]), "wakes": baseline["nativeWakes"],
                              "assertions": baseline["assertions"], "exit_code": code}
        copy_sources(mutant, contents)
        name, old, new = CONTROL
        changed = replace_once(contents[name].decode("utf-8"), old, new).encode("utf-8")
        target = mutant / name
        target.chmod(0o600)
        target.write_bytes(changed)
        target.chmod(0o400)
        dll = compile_checks("producer-disconnect", mutant, work / "mutant-build")
        code, failure = check("producer-disconnect", dll, native)
        require_disconnect(code, failure)
        report["producer_disconnect"] = {"path": name, "before_sha256": sha256(contents[name]),
            "after_sha256": sha256(changed), "old": old, "new": new, "exit_code": code,
            "offending_field": CONTROL_FAILURE, "behaviorally_rejected": True, "canonical_source_modified": False}
        shutil.rmtree(build)
        dll = compile_checks("fresh-final", candidate, build)
        code, final = check("fresh-final", dll, native)
        require_finite(code, final)
        identities = {}
        for name in ("cases.json", "cases.json.original-C.txt"):
            left, right = output / "baseline" / name, output / "fresh-final" / name
            if left.read_bytes() != right.read_bytes():
                raise GateError("Fresh complete state/events/RNG/native transcript differs: " + name)
            identities[name] = sha256(right.read_bytes())
        if (output / "baseline.dll").read_bytes() != (output / "fresh-final.dll").read_bytes():
            raise GateError("Forced fresh same-source DLL differs.")
        identities["dll"] = sha256(dll.read_bytes())
        for directory, expected in ((candidate, contents), (mutant, {**contents, CONTROL[0]: changed})):
            actual = {p.relative_to(directory).as_posix(): p.read_bytes() for p in directory.rglob("*") if p.is_file()}
            if actual != expected:
                raise GateError("Captured actual build sources changed unexpectedly.")
        report.update(status="passed", fresh_final=report["baseline"], byte_identical_baseline_fresh=identities)
    except (OSError, ValueError, KeyError, GateError, subprocess.SubprocessError) as error:
        report["error"] = str(error)
    finally:
        try:
            if before is not None:
                if (production != set((ROOT / "Assets/Scripts").rglob("*.cs"))
                        or dependencies != build_inputs() or before != hashes(observed)):
                    raise GateError("Canonical repository source/harness changed during delivery verification.")
                report["repository_hashes_unchanged"] = True
            if input_before is not None:
                after_paths = set(input_files(fixture.parent, 835) + input_files(m1, 288) + [sound])
                if input_before != hashes(after_paths):
                    raise GateError("Authenticated input inventory/bytes changed.")
                report["input_hashes_unchanged"] = True
            if original_before is not None:
                if original_before != {n: blob_sha1(b) for n, b in verify_inputs(original).items()}:
                    raise GateError("Authenticated original source changed.")
                report["original_hashes_unchanged"] = True
        except (OSError, GateError) as error:
            report.update(status="failed", hash_error=str(error))
        report["seconds"] = round(time.monotonic() - started, 3)
        write_json(output / "result.json", report)
        if report["status"] == "passed":
            shutil.rmtree(work)
    print(json.dumps({"status": report["status"], "result": str(output / "result.json"),
                      "baseline": report.get("baseline"), "error": report.get("error")}), flush=True)
    return 0 if report["status"] == "passed" else 1
