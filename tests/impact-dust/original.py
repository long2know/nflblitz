#!/usr/bin/env python3
"""Private pinned-C dust component comparison; never a native scheduler/PRNG/pixel claim."""
import argparse
import hashlib
import importlib.util
import json
import math
import os
from pathlib import Path
import re
import struct
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[2]
HERE = Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location("dust_original_extractor", ROOT / "tests/fire-spfx/oracle.py")
oracle = importlib.util.module_from_spec(spec)
spec.loader.exec_module(oracle)
PINS = {
    "player.c": "61fb1be76029663294630e8633574ed065496da1ef6329071817e80622870b52",
    "include/player.h": "a86b1a5b52ba842b206cd7dd0bbb26521db6dcb8",
    "include/id.h": "d2cc50865dd105c4143ac6b769765bd859cc4ff4",
    "include/GAME.H": "09d65c92971c32c44b1dc49fb4e4e98b371b1cd3",
    "lib/ani3d/TRIGS.H": "a07d76798931f1240bdf7f3b8e753ebd7832763a",
    "lib/ani3d/pipe3D.c": "0966ecaf0d7eef7d9c635a97899a421ce1eeea41",
    "process.c": "967ca53910f24ddfa48a14485113a8fd0c989959",
    "object.c": "c99e75df81f4993e463a65362358b304e2bc5c6c",
    "midwayvideo/include/goose/PROCESS.H": "d372245175e31399855acfa2dccddbea90da8e10",
    "midwayvideo/include/goose/object.h": "729e842d2198f51a64725acaa9d2a73adf67eec3",
}


def sha(raw):
    return hashlib.sha256(raw).hexdigest()


def compare(actual, expected, name):
    if isinstance(expected, dict):
        assert set(actual) == set(expected), name + ": exact field inventory"
        for key in expected:
            compare(actual[key], expected[key], name + "." + key)
    elif isinstance(expected, list):
        assert len(actual) == len(expected), name + ": exact count"
        for i, (left, right) in enumerate(zip(actual, expected)):
            compare(left, right, name + "[" + str(i) + "]")
    elif isinstance(expected, float):
        assert math.isclose(actual, expected, abs_tol=.00003, rel_tol=2e-7), name + ": original float field"
    else:
        assert actual == expected, name + ": original semantic field"


MAIN = r'''
int main(int argc,char **argv) {
    if(argc!=6) return 2;
    int initial=atoi(argv[1]), delay=atoi(argv[2]), presnap=atoi(argv[3]), heading=atoi(argv[4]);
    cambot.x=10; cambot.y=20; cambot.z=30;
    cambot.xform[0]=.6f; cambot.xform[2]=.8f; cambot.xform[5]=1; cambot.xform[8]=-.8f; cambot.xform[10]=.6f;
    for(int a=0;a<14;a++) {
        player_blocks[a].plyrnum=a; player_blocks[a].odata.fwd=heading;
        for(int j=0;j<15;j++) for(int k=0;k<3;k++) player_blocks[a].jpos[j][k]=(float)(j+k+1);
    }
    for(int i=0;i<10;i++) {
        texture_nodes[i].texture_info.header.aspect_ratio=3; texture_nodes[i].texture_handle=i;
        dustcld_decals[i]=&texture_nodes[i];
    }
    cur_proc=&declared_process; cur_proc->process_data=&stored_object;
    declared_process.process_id=SPFX_PID; plist=&declared_process;
    if(!strcmp(argv[5],"emit")) { dust_cloud((obj_3d *)&player_blocks[5],initial); print_requests(); puts(""); return 0; }
    tape_count=1; tape[0]=initial; original_args[0]=5; original_args[1]=10; original_args[2]=delay;
    original_entry=dust_cloud_proc;
    getcontext(&process_context); process_context.uc_stack.ss_sp=stack_memory;
    process_context.uc_stack.ss_size=sizeof(stack_memory); process_context.uc_link=&system_context;
    makecontext(&process_context,trampoline,0);
    if(!strcmp(argv[5],"kill-live") || !strcmp(argv[5],"delete-then-kill") || !strcmp(argv[5],"kill-before-entry")) {
        game_info.game_mode=2;
        if(strcmp(argv[5],"kill-before-entry")) { remaining--; swapcontext(&system_context,&process_context); }
        if(!strcmp(argv[5],"delete-then-kill")) delete_object(&object);
        cur_proc=&declared_caller; killall(SPFX_PID,-1);
        printf("{\"name\":\"%s\",\"alive\":%s,\"deletes\":%d,\"entryRandom\":%d}\n",argv[5],object.active?"true":"false",delete_count,rng_count);
        return 0;
    }
    if(!strcmp(argv[5],"render")) {
        game_info.game_mode=2; remaining--; swapcontext(&system_context,&process_context);
        draw_spfx(object.data);
        printf("{\"cm\":[");
        for(int i=0;i<12;i++)printf("%s%.9g",i?",":"",captured_cm[i]);
        printf("],\"states\":[");for(int i=0;i<state_count;i++)printf("%s%d",i?",":"",captured_states[i]);
        printf("]}\n");return 0;
    }
    printf("{\"rows\":[");
    for(current_tick=0;current_tick<34;current_tick++) {
        game_info.game_mode=presnap>=0 && current_tick>=presnap ? GM_PRE_SNAP : GM_IN_PLAY;
        if(alive && --remaining==0)swapcontext(&system_context,&process_context);
        int live=object.active;
        printf("%s{\"tick\":%d,\"alive\":%s,\"frame\":%d,\"xyz\":[",
            current_tick?",":"",current_tick,live?"true":"false",live?object.data->decal:-1);
        if(live)printf("%.9g,%.9g,%.9g",object.data->odata.x,object.data->odata.y,object.data->odata.z);
        printf("],\"sleep\":%d}",alive?remaining:0);
    }
    printf("],\"random\":[");
    for(int i=0;i<rng_count;i++)printf("%s{\"tick\":%d,\"bound\":%d,\"value\":%d,\"line\":%d}",
        i?",":"",rng[i].tick,rng[i].bound,rng[i].value,rng[i].line);
    printf("],\"deletes\":%d}\n",delete_count);
    return 0;
}
'''


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--authority", type=Path, required=True)
    parser.add_argument("--bundle", type=Path, required=True)
    parser.add_argument("--assembly", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    output = args.output.resolve()
    assert output.is_relative_to(ROOT / ".squad/.scratch") and not output.exists()
    output.mkdir(parents=True)
    env = dict(os.environ, TMPDIR=str(output), TMP=str(output), TEMP=str(output), DOTNET_NOLOGO="1")
    report = {"schema": "source-impact-dust-original-C/v1", "status": "failed", "invocation": sys.argv,
              "originalCommit": "89f840d58d152a2a9f230cb8d0cdffbd554c8ded",
              "originalTree": "a7e0352125d46075b7d98e87253ab89b9160a4c7",
              "originalSources": {}, "nativeQualified": False, "PRNGSequenceQualified": False}
    try:
        authorities = {}
        for path, expected in PINS.items():
            raw = (args.authority / path).read_bytes()
            git_blob = hashlib.sha1(b"blob " + str(len(raw)).encode() + b"\0" + raw).hexdigest()
            assert (sha(raw) if len(expected) == 64 else git_blob) == expected, "Wrong pinned original " + path
            authorities[path] = raw.decode("ascii")
            report["originalSources"][path] = {"sha256": sha(raw), "gitBlobSha1": git_blob}
        quarter = [float(x) for x in re.findall(r"[-+]?\d+\.\d+", authorities["lib/ani3d/TRIGS.H"])]
        assert len(quarter) == 257
        trig = output / "actual-quarter-sines.json"; trig.write_text(json.dumps(quarter))
        prefix = (ROOT / "tests/fire-spfx/oracle_shim.c").read_text().split("int main(int argc,char **argv)")[0]
        prefix = prefix.replace("int flags,plyrmode;", "int flags,plyrmode,fwd;")
        prefix = prefix.replace("static int limb_Polyset;", """static int limb_Polyset;
static texture_node *dustcld_decals[10];
struct process_node { struct process_node *next,*prev; int process_id; void *process_data; void (*process_exit)(struct process_node *,int); };
typedef struct process_node process_node_t;
static struct process_node declared_process,declared_caller,*cur_proc,*plist;
static struct object_node *stored_object;
static int delete_count;
static struct object_node *olist;
static int object_exists(struct object_node *);
static void dust_cloud_proc(int *);
static void dust_cloud_die_alert(struct process_node *,int);
""")
        prefix = prefix.replace("node->active=0;", "node->active=0; delete_count++;")
        prefix = prefix.replace("struct object_node { obj3d_data_t *data; int active; };",
                                "struct object_node { obj3d_data_t *data; int active; struct object_node *next; };")
        prefix = prefix.replace("object.data=data; object.active=1;", "object.data=data; object.active=1; olist=&object;")
        prefix = prefix.replace("node->active=0; delete_count++;", "node->active=0; olist=NULL; delete_count++;")
        prefix = prefix.replace("original_entry(original_args); alive=0;", "original_entry(original_args); if(cur_proc->process_exit)cur_proc->process_exit(cur_proc,1); alive=0;")
        macros = []
        for path, names in (
            ("include/id.h", ("SPFX_PID", "OID_SMKPUFF", "DRAWORDER_BALL")),
            ("include/player.h", ("NUM_DUST_TXTRS", "JOINT_NECK", "JOINT_PELVIS", "JOINT_LKNEE", "JOINT_RKNEE", "JOINT_LANKLE", "JOINT_RANKLE")),
            ("include/GAME.H", ("GM_PRE_SNAP", "FRELX", "FRELZ")),
            ("midwayvideo/include/goose/PROCESS.H", ("PD_WIPEOUT", "NODESTRUCT")),
            ("midwayvideo/include/goose/object.h", ("OF_NONE",)),
        ):
            for name in names:
                found = re.search(r"^#define[ \t]+" + name + r"(?=[ \t(]).*$", authorities[path], re.M)
                assert found, "Exact original macro absent: " + name
                macros.append("#undef " + name + "\n" + found[0])
        lifecycle = """#define TRUE 1
#define FALSE 0
/* Only full mask -1 is exercised; actual header NODESTRUCT/PD_WIPEOUT are supplied above. */
static void delete_process(struct process_node *p) {
    if(p->prev)p->prev->next=p->next;else plist=p->next;
    if(p->next)p->next->prev=p->prev;alive=0;
}
""" + oracle.extract(authorities["object.c"], "object_exists", "object.c") + \
            oracle.extract(authorities["process.c"], "kill", "process.c") + \
            oracle.extract(authorities["process.c"], "killall", "process.c")
        functions = lifecycle + "\n".join(oracle.extract(authorities["player.c"], name) for name in
                              ("dust_cloud", "dust_cloud_proc", "dust_cloud_die_alert", "draw_spfx"))
        direction = authorities["lib/ani3d/TRIGS.H"] + "\n" + "\n".join(
            oracle.extract(authorities["lib/ani3d/pipe3D.c"], name, "pipe3D.c") for name in ("isin", "icos"))
        prefix = prefix.replace("/* ORIGINAL_FUNCTIONS */",
                                "\n".join(macros) + "\n" + direction + "\n" + functions)
        source = output / "extracted-original.c"; source.write_text(prefix + MAIN)
        executable = output / "original"
        command = ["gcc", "-std=c99", "-O0", "-ffp-contract=off", str(source), "-lm", "-o", str(executable)]
        built = subprocess.run(command, env=env, capture_output=True, text=True, timeout=120)
        (output / "compile.log").write_text(built.stdout + built.stderr)
        report["compileCommand"] = command; report["compileExit"] = built.returncode
        assert built.returncode == 0, "Original C extraction must compile: " + built.stderr
        managed = output / "managed.json"
        manifest_hash = sha((args.bundle / "manifest.json").read_bytes())
        command = ["dotnet", str(args.assembly.resolve()), str(args.bundle.resolve()), manifest_hash, str(trig), str(managed)]
        run = subprocess.run(command, env=env, capture_output=True, text=True, timeout=120)
        (output / "managed.log").write_text(run.stdout + run.stderr)
        assert run.returncode == 0, "Real managed DIRT original-data checks failed: " + run.stdout + run.stderr
        actual = json.loads(managed.read_text())
        prepare_spec = importlib.util.spec_from_file_location("dust_actual_preparer", ROOT / "tools/fire-spfx/prepare.py")
        prepare = importlib.util.module_from_spec(prepare_spec)
        prepare_spec.loader.exec_module(prepare)
        for texture in actual["textures"]:
            raw = (args.bundle / ("inputs/" + texture["SourcePath"] + ".bytes")).read_bytes()
            decoded = prepare.assets.decode_tga(prepare.tga_pixel_view(raw, True), texture["SourcePath"])
            assert sha(raw) == texture["SourceSha256"]
            assert sha(decoded["mips"][0]) == texture["rgbaSha256"], "Actual C# original DIRT channel/alpha/origin must match strict converter"

        def original(*values):
            run = subprocess.run([str(executable), *map(str, values)], env=env, capture_output=True, text=True, timeout=10)
            assert run.returncode == 0, "Original extracted process failed: " + run.stderr
            return json.loads(run.stdout)

        for emission in actual["emissions"]:
            expected = original(emission["argument"], 1, -1, 137, "emit")
            compare(emission["requests"], expected, "dust_cloud original qcreate order/args/line")
        for lifecycle in actual["lifecycleComparisons"]:
            compare(lifecycle, original(2, 1, -1, 137, lifecycle["name"]),
                    "original Goose killall/kill/object_exists and dust_cloud_die_alert")
        references = []
        for case in actual["comparisons"]:
            expected = original(case["initial"], case["sleep"], case["preSnap"], case["heading"], "run")
            compare(case["rows"], expected["rows"], "dust_cloud_proc original wake/texture/XYZ/sleep")
            compare(expected["random"], [dict(tick=0, bound=3, value=case["initial"], line=9624)],
                    "original process entry range3")
            assert expected["deletes"] == 1, "Original normal/pre-snap exit must delete once"
            references.append(dict(case, original=expected))
        render = original(0, 1, -1, 137, "render")
        raw = (args.bundle / "geometry.bytes").read_bytes()
        positions = [struct.unpack_from("<3f", raw, 20 + 12 * i) for i in range(4)]
        indices = struct.unpack_from("<6I", raw, 100)
        cm = render["cm"]
        expected_vertices = [[sum(cm[row * 4 + c] * p[c] for c in range(3)) + cm[row * 4 + 3]
                              for row in range(3)] for p in (positions[i] for i in indices)]
        compare(actual["renderComparison"]["cameraVertices"], expected_vertices, "actual draw_spfx fixed camera billboard")
        assert render["states"] == [1, 0, 2, 0, 3, 3, 4, 5, 5, 1, 6, 7, 8, 9, 8, 9], "Actual original draw_spfx API state sequence"
        (output / "original-comparisons.json").write_text(json.dumps(references, indent=2) + "\n")
        report.update(status="passed", comparedTimingCases=len(references), comparedEmissionCases=len(actual["emissions"]),
                      managedAssertions=actual["assertions"], actualOriginalDrawSpfxCompared=True,
                      comparedOriginalGooseDustExitCases=len(actual["lifecycleComparisons"]),
                      comparedOriginalDirtDecodedFrames=len(actual["textures"]),
                      manifestSha256=manifest_hash, assemblySha256=sha(args.assembly.read_bytes()),
                      limitation="Extracted pinned C and actual macros/isin/icos/table on declared component caches, not native ABI, WMS, Glide pixels, FMA or qsort.")
        print(json.dumps({k: report[k] for k in ("status", "comparedTimingCases", "comparedEmissionCases", "managedAssertions")}))
        return 0
    except (AssertionError, OSError, subprocess.SubprocessError, ValueError) as error:
        report["error"] = str(error); print(str(error), file=sys.stderr); return 1
    finally:
        (output / "result.json").write_text(json.dumps(report, indent=2) + "\n")


if __name__ == "__main__":
    raise SystemExit(main())
