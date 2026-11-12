"""Bounded nonempty CAP/audible production callers, separate from the stock matrix."""
import argparse
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys
import time

import live_callers
import oracle
from support import ROOT, SUITE, GateError, blob_sha1, require_recipe, sha256


EXTRA_BLOBS = {
    "cap.c": "42605264092a30f861f5390535bbe217aac13b66",
    "include/cap.h": "79e6a1f726f7e55ee35d7de9b1a58cf21027ba18",
    "PlayStuf.c": "12197b46ea73cb2563fcb80b8c61c4f1e819b314",
    "pmisc.c": "50426d40ecd03515cecd295fb80a63ffa0cb0683",
    "include/GAME.H": "09d65c92971c32c44b1dc49fb4e4e98b371b1cd3",
}
EXTRA_FUNCTIONS = (
    ("cap.c", "dist_from"), ("cap.c", "angle_from"),
    ("cap.c", "convert_route"), ("cap.c", "convert_play"),
    ("playsel.c", "PlaySel__MakeChoice"),
    ("PlayStuf.c", "PlayStuf__GetAudiblePlay"), ("PlayStuf.c", "PlayStuf__ResetAudibles"),
    ("pmisc.c", "off_captain"), ("pmisc.c", "update_stations"),
)
ENTRY_ANCHOR = "SourceAiFrame frame = BuildSourceAiFrame();\n            int active = 0;"
ENTRY_CAPTURE = ("SourceAiFrame frame = BuildSourceAiFrame();\n"
                 "            global::CustomCallerChecks.RecordEntry(this, actor, frame);\n"
                 "            int active = 0;")
CONTROL_PATH = "Assets/Scripts/Storage/SourceCustomPlayCodec.cs"
CONTROL_OLD = "control |= SourceFormationControl.SecondPlayer | SourceFormationControl.Motion;"
CONTROL_NEW = "control |= SourceFormationControl.Motion;"
CONTROL_FAILURE = "native CAP control5 actual=260 native=276"
CASE_NAMES = ["P1/custom/" + side + "/" + humans for side in ("Home", "Away")
              for humans in ("one-human", "cooperative")]
DECLARATIONS = r"""
typedef struct { unsigned char x,y,flags; } cap_point_t;
typedef struct { char name1[9],name2[9]; unsigned char motion,flags; cap_point_t wpn[3][9]; } cap_play_t;
typedef struct { short ids[2][3]; } audible_ids_t;
typedef struct { play_t *play_ptr; } PLAYSTUF__SLOT;
static fline_t custom_form[7];
static int custom_routes[512],cap_lengths[3],active_station,active_slot;
static char cap_ids[3][64],cap_play_id[64];
play_t custom_play,*original_play[2];
cap_play_t custom_plays[4][9],*convert_play_last,*original_custom;
audible_ids_t audible_ids[4];
int original_flip,audible_turbo_timer[2],pup_always_qb,pup_always_receiver;
extern int ds_none[],pr_QB_block[];
extern char center_set_anim[],stance3pt_anim[],shotgun_anim[],qbset_anim[],wr_set_anim[];
static int convert_route(cap_point_t *,int *);
void convert_play(cap_play_t *);
play_t *PlaySel__MakeChoice(int,int,int,int,int,int);
int off_captain(void);
void update_stations(void);
static PLAYSTUF__SLOT *PlayStuf__GetSlot(int side,int page,int slot) {
    fprintf(stderr,"noncustom/forced-empty page lookup outside finite CAP boundary\n");abort();
}
static void cap_tag(void) {
    int found=0,scratch[512];
    for(int p=0;p<4;p++)for(int s=0;s<9;s++)if(convert_play_last==&custom_plays[p][s]){
        active_station=p;active_slot=s;found=1;
    }
    if(!found)abort();
    snprintf(cap_play_id,sizeof(cap_play_id),"custom_p%d_s%d",active_station,active_slot);
    for(int r=0;r<3;r++){
        snprintf(cap_ids[r],sizeof(cap_ids[r]),"custom_p%d_s%d_r%d",active_station,active_slot,r);
        cap_lengths[r]=convert_route(convert_play_last->wpn[r],scratch);
    }
}
static int cap_select(const char *name) {
    int p,s,used=0;
    if(sscanf(name,"custom_p%d_s%d%n",&p,&s,&used)!=2 || name[used])return 0;
    if(p<0||p>=4||s<0||s>=9||!(custom_plays[p][s].flags&CPLT_EXISTS))abort();
    convert_play(&custom_plays[p][s]);cap_tag();return 1;
}
static int *cap_route(const char *name) {
    int p,s,r,used=0;char play[64];
    if(sscanf(name,"custom_p%d_s%d_r%d%n",&p,&s,&r,&used)!=3 || name[used])return NULL;
    if(r<0||r>=3)abort();
    snprintf(play,sizeof(play),"custom_p%d_s%d",p,s);
    if(!cap_select(play))abort();
    return custom_play.routes[r+4];
}
"""
PROTOCOL = r"""
static void cap_input(void) {
    int p=ni(),s=ni();char *hex=ns();
    if(p<0||p>=4||s<0||s>=9||strlen(hex)!=202||sizeof(cap_play_t)!=101)abort();
    unsigned char *data=(unsigned char*)&custom_plays[p][s];
    for(int i=0;i<101;i++){unsigned n;if(sscanf(hex+2*i,"%2x",&n)!=1)abort();data[i]=(unsigned char)n;}
    puts("CAP-INPUT");fflush(stdout);
}
static void cap_emit(play_t *play) {
    if(!play){puts("CAP-NULL");fflush(stdout);return;}
    if(play!=&custom_play)abort();
    cap_tag();
    printf("CAP %s %d %d",cap_play_id,play->flags,game_info.off_flip);
    for(int i=0;i<7;i++){
        fline_t *f=&play->formation[i];
        printf(" %.9g %.9g %d %d %s",f->x,f->z,f->mode,f->control,formation_script_id(f->seq));
    }
    for(int r=0;r<3;r++){
        printf(" %d",cap_lengths[r]);
        for(int i=0;i<cap_lengths[r];i++)printf(" %d",play->routes[r+4][i]);
    }
    printf(" %d %d %d %d",game_info.plyr_control[0],game_info.plyr_control[1],
        game_info.plyr_control[2],game_info.plyr_control[3]);
    puts("");fflush(stdout);
}
static void cap_controls_input(void) {
    game_info.off_side=ni();game_info.captain=ni();p_status=ni();four_plr_ver=1;
    pup_always_qb=pup_always_receiver=0;
}
static void cap_choice(void) {
    cap_controls_input();int p=ni(),slot=ni();
    game_info.off_flip=0;
    play_t *play=PlaySel__MakeChoice(OFFENSE,4,p,slot,game_info.off_side,0);
    game_info.team_play[game_info.off_side]=play;
    if(play){native_caller_controls();update_stations();PlayStuf__ResetAudibles();}
    cap_emit(play);
}
static void cap_audible(void) {
    int p=ni(),choice=ni();
    for(int i=0;i<3;i++)audible_ids[p].ids[OFFENSE][i]=ni();
    play_t *play=PlayStuf__GetAudiblePlay(p,OFFENSE,choice);
    game_info.team_play[game_info.off_side]=play;
    if(play){native_caller_controls();update_stations();}
    cap_emit(play);
}
"""


def extra_inputs(root):
    result = {}
    for name, pin in EXTRA_BLOBS.items():
        path = Path(root) / name
        if not path.is_file() or path.is_symlink() or path.resolve() != path:
            raise GateError("Missing regular authenticated CAP input: " + name)
        data = path.read_bytes()
        if blob_sha1(data) != pin:
            raise GateError("Original CAP/audible Git blob differs: " + name)
        result[name] = data
    return result


def function(text, name):
    match = oracle.one(r"\b(?:static\s+)?(?:float|int|void|play_t\s*\*)\s*" +
        re.escape(name) + r"\s*\([^;{}]*\)\s*\{", text, name)
    masked = oracle.strip_comments(text)
    start = text.index("{", match.start())
    depth, end = 1, start + 1
    while depth and end < len(text):
        depth += (masked[end] == "{") - (masked[end] == "}")
        end += 1
    if depth:
        raise GateError("Unterminated unchanged original CAP function: " + name)
    return text[match.start():end]


def build_native(root, output, recipe, environment):
    output = Path(output)
    protocol = output / "protocol"
    protocol.mkdir(parents=True)
    documents = dict(oracle.verify_inputs(root), **extra_inputs(root))
    preprocessed = {name: oracle.preprocess(documents[name], environment)
                    for name in {name for name, _ in EXTRA_FUNCTIONS} | {"nfl.c"}}
    selections, bodies = [], []
    for name, symbol in EXTRA_FUNCTIONS:
        body = function(preprocessed[name], symbol)
        bodies.append(body)
        selections.append({"path": name, "symbol": symbol, "git_blob_sha1": blob_sha1(documents[name]),
            "preprocessed_function_sha256": sha256(body.encode()), "expression_rewrites": 0})
    nfl = preprocessed["nfl.c"]
    start = nfl.index("game_info.plyr_control[0] = -1;", nfl.index("AUDIBLE_RESTART:"))
    end = nfl.index("update_stations();", start)
    controls = nfl[start:end]
    caller = "static void native_caller_controls(void){int i;\n" + controls + "\n}\n"
    macros = "\n".join(line for name in ("include/cap.h", "include/GAME.H", "include/PlayStuf.h")
        for line in documents[name].decode("latin1").splitlines()
        if re.match(r"\s*#define\s+(?:CPPT_|CPLT_|CP_COUNT\b|CP_NAMELEN\b|G[XY]2F[XZ]\b|OFFENSE\b|DEFENSE\b|TEAM[12]_MASK\b|PLAYSTUF__(?:PLAYS_PER_PAGE|OFF_PAGE_ID_CUSTOM)\b)", line))
    boundary = (SUITE / "boundaries.c").read_text()
    boundary = live_callers.replace_once(boundary, "captain_player[2],off_flip;",
        "captain_player[2],off_flip,captain,plyr_control[4],audible_num[2],audible_flags[2],audible_plyr[2];")
    (protocol / "boundaries.c").write_text(boundary + "\n" + macros + "\n" + DECLARATIONS)
    driver = live_callers.projected_driver((SUITE / "driver.c").read_text())
    driver = live_callers.replace_once(driver, "int main(void){",
        "\n".join(bodies) + "\n" + caller + PROTOCOL + "\nint main(void){")
    driver = live_callers.replace_once(driver, 'else if(!strcmp(token,"quit"))break;',
        'else if(!strcmp(token,"cap-input"))cap_input();\n'
        '  else if(!strcmp(token,"cap-choice"))cap_choice();\n'
        '  else if(!strcmp(token,"cap-audible"))cap_audible();\n'
        '  else if(!strcmp(token,"quit"))break;')
    (protocol / "driver.c").write_text(driver)
    original_maps, original_suite, original_functions = oracle.make_maps, oracle.SUITE, oracle.FUNCTIONS

    def maps(metadata, requested_recipe):
        result = original_maps(metadata, requested_recipe)
        for old, new in (
            ('static play_t *get_play(const char *name) {',
             'static play_t *get_play(const char *name) {if(cap_select(name))return &custom_play;'),
            ('static int *get_route(const char *name) {',
             'static int *get_route(const char *name) {int *custom=cap_route(name);if(custom)return custom;'),
            ('static const char *route_id(int *p,int *offset) { uintptr_t q=(uintptr_t)p;',
             'static const char *route_id(int *p,int *offset) { uintptr_t q=(uintptr_t)p;'
             'for(int r=0;r<3;r++)if(q>=(uintptr_t)custom_play.routes[r+4]&&'
             'q<(uintptr_t)(custom_play.routes[r+4]+cap_lengths[r])){'
             '*offset=(int)(p-custom_play.routes[r+4]);return cap_ids[r];}'),
            ('static const char *play_id(play_t *p) {',
             'static const char *play_id(play_t *p) {if(p==&custom_play)return cap_play_id;'),
        ):
            result = live_callers.replace_once(result, old, new)
        return result

    try:
        oracle.SUITE, oracle.make_maps = protocol, maps
        oracle.FUNCTIONS = [*original_functions, ("drone.c", "abort_drone_route")]
        receipt = oracle.build(root, output / "oracle", recipe, environment)
    finally:
        oracle.SUITE, oracle.make_maps, oracle.FUNCTIONS = original_suite, original_maps, original_functions
    receipt["selected_functions"].extend(selections)
    receipt["selected_function_count"] = len(receipt["selected_functions"])
    receipt.update(extra_original_blobs=EXTRA_BLOBS, control_caller={
        "path": "nfl.c", "parent_function": "game_proc", "active_label": "AUDIBLE_RESTART",
        "preprocessed_span_sha256": sha256(controls.encode()), "expression_rewrites": 0},
        boundary="Actual compact CAP input, original conversion/choice/audible/controller bodies; focused receiver entry-state projection. Noncustom/empty-forced page lookup aborts; no target ABI/RNG claim.",
        original_CAP_receiver_opcode_producers=[1, 2, 4, 5, 9, 10, 11, 12, 14],
        CAP_receiver_does_not_produce=[-3, -2, -1, 0, 3, 6, 7, 8, 13, 15, 16, 17, 18, 19, 20],
        shared_line_route="ds_none: DS_IMPROVISE", shared_QB_route="pr_QB_block: terminal DS_BLOCK")
    live_callers.write_json(output / "provenance.json", receipt)
    return output / "oracle/source-oracle", receipt


def compilation_inputs():
    paths = live_callers.build_inputs()
    paths.difference_update(SUITE / name for name in ("LiveCallerChecks.cs", "LiveCallerChecks.csproj"))
    paths.update(ROOT / name for name in ("tests/playbooks/CustomCallerChecks.cs",
        "tests/playbooks/CustomCallerChecks.csproj", "Assets/Scripts/Runtime/SourceCabinetInput.cs",
        "Assets/Scripts/Runtime/SourceCabinetInputPlaySelection.cs"))
    return paths


def require_cases(code, report, captured=False):
    if (code != 0 or not isinstance(report, dict) or report.get("status") != "passed"
            or report.get("original_C_compared") is not True
            or [item.get("Name") for item in report.get("cases", [])] != CASE_NAMES
            or [item.get("captain") for item in report["cases"]] != [0, 1, 2, 3]
            or [item.get("flip") for item in report["cases"]] != [False, True, False, True]):
        raise GateError("Require all four actual source custom direction/human caller cells.")
    if captured:
        guarded = sum(item.get("nativeExecuted") is False for item in report.get("nativeTrace", []))
        if (report.get("nativeRouteWakes", 0) <= 0
                or report.get("capturedEntries", 0) != report["nativeRouteWakes"] + guarded):
            raise GateError("Every actual entry must be native-compared or explicitly ABI-unqualified, never silently dropped.")


def arguments(argv):
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ("source-root", "fixture-manifest", "sound-functions", "presnap-inputs", "hud-inputs", "output"):
        parser.add_argument("--" + name, type=Path, required=True)
    parser.add_argument("--dotnet", default=os.environ.get("SOURCE_PLAYER_DOTNET", "dotnet"))
    return parser.parse_args(argv)


def main(argv=None):
    args = arguments(argv)
    original, fixture, sound, presnap, hud = (getattr(args, name).resolve() for name in
        ("source_root", "fixture_manifest", "sound_functions", "presnap_inputs", "hud_inputs"))
    try:
        output = live_callers.create_output(args.output, (original, fixture.parent, sound.parent, presnap, hud))
    except (OSError, GateError) as error:
        print(str(error), file=sys.stderr)
        return 2
    work = output / "work"
    work.mkdir()
    environment = dict(os.environ, TMPDIR=str(work), TMP=str(work), TEMP=str(work),
        DOTNET_CLI_HOME=str(work / "cli"), NUGET_PACKAGES=str(work / "nuget"),
        DOTNET_NOLOGO="1", DOTNET_CLI_TELEMETRY_OPTOUT="1", DOTNET_EnableDiagnostics="0",
        DOTNET_CLI_USE_MSBUILD_SERVER="0", MSBUILDDISABLENODEREUSE="1")
    report = {"schema": "source-nonempty-custom-callers/v1", "status": "failed", "commands": [],
        "production_modified": False, "old_stock_qualification_reminted": False,
        "profile": "Explicit Portable DefaultAudibles282 factory; authenticated pre-snap-switch563 HUD/impact, not AV306.",
        "native_boundary": "Original CAP/choice/audible/controller bodies; actual focused entry captured by ONE read-only copied-source hook. Full every-wake state/RNG/events must match canonical; original negative-index inputs remain explicitly unqualified."}
    started = time.monotonic()
    before = input_before = original_before = None

    def run(label, command):
        begin = time.monotonic()
        result = subprocess.run([str(x) for x in command], cwd=ROOT, env=environment,
                                capture_output=True, text=True, timeout=300)
        text = result.stdout + result.stderr
        (output / (label + ".log")).write_text(text)
        report["commands"].append({"label": label, "command": [str(x) for x in command],
            "exit_code": result.returncode, "seconds": round(time.monotonic() - begin, 3)})
        return result.returncode, text

    def compile_checks(label, source, build):
        common = ["-p:BaseIntermediateOutputPath=" + str(build / "obj") + "/",
                  "-p:MSBuildProjectExtensionsPath=" + str(build / "obj") + "/",
                  "-p:BaseOutputPath=" + str(build / "bin") + "/"]
        project = source / "tests/playbooks/CustomCallerChecks.csproj"
        command = [args.dotnet, "build", project, "--no-restore", "-c", "Release", "-v", "quiet", *common]
        code, text = run(label + "-build", command)
        if code and "NETSDK1004" in text:
            empty = work / "empty-feed"
            empty.mkdir(exist_ok=True)
            code, _ = run(label + "-restore", [args.dotnet, "restore", project, "--source", empty,
                                              "-p:NuGetAudit=false", "-v", "quiet", *common])
            if code:
                raise GateError("Restore failed after actual missing assets: " + label)
            code, text = run(label + "-rebuild", command)
        if code or "0 Warning(s)" not in text or "0 Error(s)" not in text:
            raise GateError("Compilation requires C#7.3 and zero warnings/errors: " + label)
        dll = build / "bin/Release/net10.0/CustomCallerChecks.dll"
        shutil.copyfile(dll, output / (label + ".dll"))
        return dll

    def check(label, dll, native, captured=False):
        target = output / label
        target.mkdir()
        code, _ = run(label, [args.dotnet, dll, fixture, sound, presnap, hud, target / "cases.json",
                              native, "captured" if captured else "canonical"])
        return code, json.loads((target / "cases.json").read_bytes())

    def copy_sources(destination, contents):
        for name, data in contents.items():
            path = destination / name
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(data)

    def compare_states(left, right, a, b):
        if a["cases"] != b["cases"] or a["trace"] != b["trace"]:
            raise GateError("Actual public observations/RNG/events differ: " + left + "/" + right)
        first = {p.name: sha256(p.read_bytes()) for p in (output / left).glob("*.state.jsonl")}
        second = {p.name: sha256(p.read_bytes()) for p in (output / right).glob("*.state.jsonl")}
        if len(first) != 4 or first != second:
            raise GateError("Every-source-wake actor/route/ball/control/state/RNG/event bytes differ: " + left + "/" + right)
        return first

    try:
        original_before = {n: blob_sha1(b) for n, b in
                           dict(oracle.verify_inputs(original), **extra_inputs(original)).items()}
        recipe = json.loads((ROOT / "tools/playbooks/recipe.json").read_bytes())
        require_recipe(recipe)
        if (sha256(fixture.read_bytes()) != live_callers.FIXTURE_SHA256
                or sha256(sound.read_bytes()) != live_callers.SOUND_SHA256):
            raise GateError("Exact authenticated Contact272 fixture and unchanged SAFN required.")
        descriptor = json.loads((hud / "manifest.json").read_bytes())
        hud_paths = [hud / "manifest.json"] + [hud / descriptor["entrypoints"][name]
                                              for name in ("hud", "bootstrap", "impact", "playbook")]
        input_paths = set(live_callers.input_files(fixture.parent, 835)
                          + live_callers.input_files(presnap, 289) + hud_paths + [sound])
        input_before = live_callers.hashes(input_paths)
        dependencies = compilation_inputs()
        production = set((ROOT / "Assets/Scripts").rglob("*.cs"))
        observed = production | dependencies | {SUITE / name for name in
            ("run.py", "custom_callers.py", "live_callers.py", "oracle.py", "support.py",
             "pins.json", "driver.c", "boundaries.c", "README.md", "test_runner.py")}
        before = live_callers.hashes(observed)
        contents = {p.relative_to(ROOT).as_posix(): p.read_bytes() for p in dependencies}
        if before != live_callers.hashes(observed):
            raise GateError("Source changed during coherent compilation capture.")
        report.update(repository_sha256={str(Path(k).relative_to(ROOT)): v for k, v in before.items()},
            compiled_source_sha256={n: sha256(b) for n, b in contents.items()},
            input_sha256=input_before, original_blob_sha1=original_before)
        candidate, captured, mutant = work / "canonical-source", work / "captured-source", work / "mutant-source"
        copy_sources(candidate, contents)
        native, report["original_c"] = build_native(original, output / "native", recipe, environment)
        build = work / "canonical-build"
        dll = compile_checks("baseline", candidate, build)
        code, baseline = check("baseline", dll, native)
        require_cases(code, baseline)
        capture_contents = dict(contents)
        game_source = "Assets/Scripts/Gameplay/GameSimulation.Source.cs"
        capture_contents[game_source] = live_callers.replace_once(contents[game_source].decode(),
            ENTRY_ANCHOR, ENTRY_CAPTURE).encode()
        copy_sources(captured, capture_contents)
        dll = compile_checks("captured", captured, work / "captured-build")
        code, capture = check("captured", dll, native, True)
        require_cases(code, capture, True)
        states = compare_states("baseline", "captured", baseline, capture)
        changed = live_callers.replace_once(capture_contents[CONTROL_PATH].decode(), CONTROL_OLD, CONTROL_NEW).encode()
        control_contents = {**capture_contents, CONTROL_PATH: changed}
        copy_sources(mutant, control_contents)
        dll = compile_checks("producer-disconnect", mutant, work / "mutant-build")
        code, rejected = check("producer-disconnect", dll, native, True)
        if code != 1 or rejected.get("status") != "failed" or CONTROL_FAILURE not in rejected.get("error", ""):
            raise GateError("Require the real CAP cooperative-control producer disconnect, not a crash/build error.")
        report["producer_disconnect"] = {"path": CONTROL_PATH, "old": CONTROL_OLD, "new": CONTROL_NEW,
            "before_sha256": sha256(contents[CONTROL_PATH]), "after_sha256": sha256(changed),
            "offending_field": CONTROL_FAILURE, "exit_code": code}
        shutil.rmtree(build)
        dll = compile_checks("fresh-final", candidate, build)
        code, final = check("fresh-final", dll, native)
        require_cases(code, final)
        compare_states("baseline", "fresh-final", baseline, final)
        if (output / "baseline.dll").read_bytes() != dll.read_bytes():
            raise GateError("Fresh canonical DLL differs.")
        for directory, expected in ((candidate, contents), (captured, capture_contents), (mutant, control_contents)):
            actual = {p.relative_to(directory).as_posix(): p.read_bytes() for p in directory.rglob("*") if p.is_file()}
            if actual != expected:
                raise GateError("Actual captured compiler inputs changed unexpectedly.")
        guarded = [item for item in capture["nativeTrace"] if item.get("nativeExecuted") is False]
        report.update(status="passed", cases=baseline["cases"], canonical_assertions=baseline["assertions"],
            captured_assertions=capture["assertions"], captured_entries=capture["capturedEntries"],
            native_route_wakes=capture["nativeRouteWakes"], native_route_draws=capture["nativeRouteDraws"],
            observed_receiver_opcodes=capture["executedReceiverOpcodes"], ABI_unqualified_entries=guarded,
            identical_canonical_capture_and_fresh_every_wake=states, fresh_dll_sha256=sha256(dll.read_bytes()),
            copied_source_readonly_hook={"path": game_source, "old": ENTRY_ANCHOR, "new": ENTRY_CAPTURE,
                "before_sha256": sha256(contents[game_source]), "after_sha256": sha256(capture_contents[game_source]),
                "canonical_production_modified": False})
    except (OSError, ValueError, KeyError, GateError, subprocess.SubprocessError) as error:
        report["error"] = str(error)
    finally:
        try:
            if before is not None:
                if (before != live_callers.hashes(observed)
                        or production != set((ROOT / "Assets/Scripts").rglob("*.cs"))
                        or dependencies != compilation_inputs()):
                    raise GateError("Canonical source/harness inventory/bytes changed.")
                report["production_and_harness_unchanged"] = True
            if input_before is not None:
                if input_before != live_callers.hashes(input_paths):
                    raise GateError("Authenticated actual inputs changed.")
                report["inputs_unchanged"] = True
            if original_before is not None:
                after = {n: blob_sha1(b) for n, b in
                         dict(oracle.verify_inputs(original), **extra_inputs(original)).items()}
                if original_before != after:
                    raise GateError("Original source changed.")
                report["originals_unchanged"] = True
        except (OSError, GateError) as error:
            report.update(status="failed", hash_error=str(error))
        report["seconds"] = round(time.monotonic() - started, 3)
        live_callers.write_json(output / "result.json", report)
        if report["status"] == "passed":
            shutil.rmtree(work)
    print(json.dumps({"status": report["status"], "result": str(output / "result.json"),
                      "native_route_wakes": report.get("native_route_wakes"), "error": report.get("error")}), flush=True)
    return 0 if report["status"] == "passed" else 1
