"""Execute unchanged active CAMBOT C on a declared host; original bytes stay in ignored output."""
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[3]
sys.path[:0] = [str(ROOT / "tools/rosters"), str(ROOT / "tools/assets")]
from c_source import CData, Macro, closing_brace
from export_rosters import REVISION, TREE, verify_tree
from prepare_teams import read_pinned
from prepare_cinematics import TABLES, convert as impact_catalog
from prepare_normal_camera import convert as normal_catalog
import prepare_player as files

HEADERS = ("include/GAME.H", "include/player.h", "include/ani3d.h", "include/camdefs.h", "include/PlayStuf.h")
INPUTS = HEADERS + ("cambot.c",
          "CamData.c", "lib/ani3d/ANIM.C", "lib/ani3d/pipe3D.c", "plyrseq.c", "detect.c", "player.c")


def declaration(data, name):
    match = re.search(r"(?:static\s+)?(?:float|SPLINE|CAMSEQINFO|CAMBOTINFO)\s+" + re.escape(name) +
                      r"\s*(?:\[[^]]*\])*\s*=\s*\{", data.text)
    if match is None:
        raise ValueError("Original camera declaration is unavailable: " + name)
    end = closing_brace(data.text, match.end() - 1)
    return data.text[match.start():end + 1] + ";\n"


def struct(data, tag):
    match = re.search(r"typedef\s+struct\s+" + tag + r"\s*\{", data.text)
    end = data.text.index(";", closing_brace(data.text, match.end() - 1))
    return data.text[match.start():end + 1] + "\n"


def harness(inputs):
    env = {"SEATTLE": Macro("1"), "DG2RD": Macro("(x)", ("x",))}
    for label in HEADERS:
        data = CData(inputs[label].decode("latin1"), env).enums()
        env = data.expressions.macros
        if label == "include/ani3d.h":
            ani = data
        if label == "include/camdefs.h":
            definitions = data
    camera = CData(inputs["cambot.c"].decode("latin1"), env).enums()
    env = camera.expressions.macros
    camdata = CData(inputs["CamData.c"].decode("latin1"), env)
    anim = CData(inputs["lib/ani3d/ANIM.C"].decode("latin1"), env)
    pipe = CData(inputs["lib/ani3d/pipe3D.c"].decode("latin1"), env)
    text = "#include <stdio.h>\n#include <stdlib.h>\n#include <string.h>\n#include <math.h>\n#include <setjmp.h>\n"
    for name, macro in env.items():
        if name in ("DG2RD", "NULL", "TRUE", "FALSE"):
            continue
        params = "" if macro.parameters is None else "(" + ",".join(macro.parameters) + ")"
        text += "#define " + name + params + " " + macro.text + "\n"
    text += r"""
#define DG2RD(x) ((float)((x)*M_PI/180.0))
#define GR2RD(x) ((x)*((float)(M_PI*2.0)/1024.0f))
#define M_TWOPI ((float)(M_PI*2.0))
#define fsin(x) ((float)sin(x))
#define fcos(x) ((float)cos(x))
#define fsqrt(x) ((float)sqrt(x))
#define rsqrt(x) (1.0f/(float)sqrt(x))
#define FALSE 0
#define TRUE 1
#define NODESTRUCT 0
#define PLYRINFO_PID 0
#define POST_PLAYER_PID 0
#define MSG_CAMBOT_PID 0
typedef struct {float x,y,z; int plyrmode,fwd; float vx,vy,vz;} obj_3d;
typedef struct {obj_3d odata; int team,flags; float tsx,tsy,tsz;} fbplyr_data;
typedef struct {obj_3d odata; int int_receiver,type,flags,flight_time,who_threw; float tx,tz,tsx,tsy,tsz,ox,oz;} ball_data;
typedef struct {int flags;} team_play_t;
typedef struct {int game_mode,game_flags,play_type,play_result,los,last_pos,off_side,game_state,ball_carrier;
 int game_quarter,auto_hike_cntr,off_turnover,last_carrier; team_play_t *team_play[2];} game_info_t;
static game_info_t game_info;
static ball_data ball_obj;
static fbplyr_data player_blocks[14];
static team_play_t teams[2];
static float hres=512.0f,vres=384.0f;
static int flg_hhteam,flg_pullback,idiot_boxes_shown=2,playback=0;
static int idiot_flag,show_sideline,shaker_amp,shaker_tot,shaker_cnt;
static float carrier_goal_dist,sin_alpha,sin_delta,mode_trgy_adjust;
static FILE *input;
static jmp_buf finished;
static int sample_tick,event_table,event_ordinal,event_actor,event_shake;
static int opening_choice,serial,phase,draw_count,draw_bounds[64],draw_values[64],normal_calls;
static int fixture_shaker_amplitude,fixture_shaker_ticks;
static void unavailable(const char *s){fprintf(stderr,"UNQUALIFIED active native service %s\n",s);exit(9);}
static int existp(int a,int b){unavailable("existp");return 0;}
static void resume_all(void){unavailable("resume_all");}
static void suspend_multiple_processes(int a,int b){unavailable("suspend_multiple_processes");}
static void resume_multiple_processes(int a,int b){unavailable("resume_multiple_processes");}
static void show_message_proc(int *a){unavailable("show_message_proc");}
static void *qcreate(const char *n,int p,void(*f)(int*),int a,int b,int c,int d){unavailable("qcreate");return NULL;}
static void sleep(int ticks);
static int randrng(int n){
 int value=(serial*5+2)%n;
 if(phase==0 && n==11 && opening_choice>=0)value=opening_choice;
 if(phase==1)value=event_ordinal;
 serial++;
 if(phase==0){draw_bounds[draw_count]=n;draw_values[draw_count++]=value;}
 if(value<0||value>=n)unavailable("invalid supplied RNG result");
 return value;
}
"""
    for tag in ("_vec3d", "_SPLINE", "_CAMSEQINFO", "_VIEWCOORD"):
        text += struct(ani, tag)
    text += "typedef struct {float x,y,z;} VERTEX;\n"
    text += struct(camera, "tagCAMBOTINFO") + struct(definitions, "tagKO_INFO") + struct(definitions, "tagPOP_INFO")
    text += "static VIEWCOORD cambot;\nstatic CAMBOTINFO cambotinfo;\n"
    for name in ("track_def", "track_p2p", "accel_def", "accel_def_nobc", "accel_p2p",
                 "cambotinfo_init", "mode_parms", "shaker_exp"):
        text += declaration(camera, name)
    signatures = {
        "ValidateTheta": "static float ValidateTheta(float theta)",
        "ValidatePhi": "static float ValidatePhi(float phi)",
        "BuffAccel": "static void BuffAccel(float *pf,float diff,float buff,float rate)",
        "TrackObjects": "static void TrackObjects(VIEWCOORD *pcam,CAMBOTINFO *pci)",
        "SetModeAngleTrgs": "static void SetModeAngleTrgs(VIEWCOORD *pcam,int mode)",
        "DoSpline": "static void DoSpline(VERTEX *pv,SPLINE *ps,int cur_tick,int tot_tick)",
        "DoAccelDecel": "static void DoAccelDecel(int cur_tick,int tot_tick)",
        "ModeRevert": "static int ModeRevert(VIEWCOORD *pcam,CAMBOTINFO *pci)",
        "cambot_normal": "static int original_normal(VIEWCOORD *pcam,CAMBOTINFO *pci)",
        "cambot_gopher": "static int cambot_gopher(VIEWCOORD *pcam,CAMBOTINFO *pci)",
        "cambot_resume": "static int cambot_resume(VIEWCOORD *pcam,CAMBOTINFO *pci)",
        "cambot_zoom": "static int cambot_zoom(VIEWCOORD *pcam,CAMBOTINFO *pci)",
        "cambot_spline": "static int cambot_spline(VIEWCOORD *pcam,CAMBOTINFO *pci)",
        "cambot_rail": "static int cambot_rail(VIEWCOORD *pcam,CAMBOTINFO *pci)",
        "cambot_p2p": "static int cambot_p2p(VIEWCOORD *pcam,CAMBOTINFO *pci)",
        "cambot_special": "static int cambot_special(VIEWCOORD *pcam,CAMBOTINFO *pci,int flag)",
    }
    for signature in signatures.values():
        text += signature + ";\n"
    text += r"""
static void set_cambot_theta(float theta);
static void set_cambot_phi(float phi);
static void update_cambot_xform(void);
static CAMSEQINFO *select_csi_ptr(CAMSEQINFO *pcsi);
static int select_csi_item(CAMSEQINFO *pcsi);
static int cambot_normal(VIEWCOORD *p,CAMBOTINFO *i){normal_calls++;return original_normal(p,i);}
static int cambot_side(VIEWCOORD *p,CAMBOTINFO *i){unavailable("unselected CAM_SIDE");return 0;}
static int cambot_top(VIEWCOORD *p,CAMBOTINFO *i){unavailable("unselected CAM_TOP");return 0;}
static int cambot_attract(VIEWCOORD *p,CAMBOTINFO *i){unavailable("unselected CAM_ATTRACT");return 0;}
static int cambot_path(VIEWCOORD *p,CAMBOTINFO *i){unavailable("unselected CAM_PATH");return 0;}
static int cambot_course(VIEWCOORD *p,CAMBOTINFO *i){unavailable("unselected CAM_COURSE");return 0;}
"""
    match = re.search(r"static int \(\*cambot_funcs\[\]\)\(VIEWCOORD \*, CAMBOTINFO \*\) = \{", camera.text)
    end = closing_brace(camera.text, match.end() - 1)
    text += camera.text[match.start():end + 1] + ";\n"
    tables = list(TABLES) + ["csi_kickoff", "csi_fs_fgtrk"]
    dependencies, starts = set(), {}
    for table in tables:
        rows, _ = camdata.array(table)
        for row in rows[:-1]:
            pointer = row[6].raw
            if pointer.startswith("&"):
                name = pointer[1:]
                if name.startswith("ki_"):
                    symbol = name[3:]
                    match = re.search(r"\bKI\s*\(\s*" + symbol + r"\s*,[^;]+\)\s*;", camdata.text)
                    starts[symbol] = match.group(0)
                    dependencies.add(symbol)
                else:
                    dependencies.add(name)
    for name in sorted(dependencies):
        text += declaration(camdata, name)
    for source in starts.values():
        text += source + "\n"
    for table in tables:
        text += declaration(camdata, table)
    text += "static CAMSEQINFO *fixture_tables[]={" + ",".join(TABLES) + "};\n"
    for name, signature in (("magv", "static float magv(vec3d *v)"), ("norm", "static void norm(vec3d *v)"),
                            ("vxv", "static void vxv(vec3d *v1,vec3d *v2,vec3d *v3)")):
        text += signature + pipe.function(name)[1] + "\n"
    for name, signature in (("set_cambot_theta", "static void set_cambot_theta(float theta)"),
                            ("set_cambot_phi", "static void set_cambot_phi(float phi)"),
                            ("update_cambot_xform", "static void update_cambot_xform(void)")):
        text += signature + camera.function(name)[1] + "\n"
    for name, signature in signatures.items():
        text += signature + camera.function(name)[1] + "\n"
    for name, signature in (("select_csi_ptr", "static CAMSEQINFO *select_csi_ptr(CAMSEQINFO *pcsi)"),
                            ("select_csi_item", "static int select_csi_item(CAMSEQINFO *pcsi)")):
        match = re.search(r"^(?:int|CAMSEQINFO\s*\*)\s*" + name + r"\s*\([^;{}]*\)\s*\{", anim.text, re.M)
        start = anim.text.index("{", match.start())
        text += signature + anim.text[start:closing_brace(anim.text, start) + 1] + "\n"
    text += "static void cambot_proc(int *parg)" + camera.function("cambot_proc")[1] + "\n"
    text += r"""
static int ri(void){int x;if(fscanf(input,"%d",&x)!=1)unavailable("truncated integer fixture");return x;}
static float rf(void){float x;if(fscanf(input,"%f",&x)!=1)unavailable("truncated float fixture");return x;}
static int read_sample(void){
 if(fscanf(input,"%d",&sample_tick)!=1)return 0;
 game_info.game_mode=ri();game_info.game_flags=ri();game_info.play_type=ri();game_info.play_result=ri();
 game_info.game_quarter=ri();game_info.off_side=ri();game_info.last_pos=ri();game_info.los=ri();
 game_info.ball_carrier=ri();flg_hhteam=ri();flg_pullback=ri();game_info.auto_hike_cntr=ri();
 teams[0].flags=teams[1].flags=ri();hres=rf();vres=rf();
 ball_obj.odata.x=rf();ball_obj.odata.y=rf();ball_obj.odata.z=rf();
 ball_obj.odata.vx=rf();ball_obj.odata.vy=rf();ball_obj.odata.vz=rf();
 ball_obj.ox=rf();(void)rf();ball_obj.oz=rf();ball_obj.tx=rf();(void)rf();ball_obj.tz=rf();
 ball_obj.tsx=rf();ball_obj.tsy=rf();ball_obj.tsz=rf();
 ball_obj.type=ri();ball_obj.flags=ri();ball_obj.flight_time=ri();ball_obj.who_threw=ri();ball_obj.int_receiver=ri();
 for(int id=0;id<14;id++){
  fbplyr_data *p=&player_blocks[id];p->team=id/7;
  p->odata.plyrmode=ri();p->flags=ri();p->odata.fwd=ri();
  p->odata.x=rf();p->odata.y=rf();p->odata.z=rf();p->tsx=rf();p->tsy=rf();p->tsz=rf();
 }
 event_table=ri();event_ordinal=ri();event_actor=ri();event_shake=ri();
 return 1;
}
static void sleep(int ticks){
 if(ticks!=1)unavailable("native message sleep/scheduler");
 printf("%d\t%d\t%d\t%d\t%d\t%d\t%d\t%d\t%d\t%d\t%d\t%d",
  sample_tick,cambot.csi.mode,cambot.was_playover,cambot.kickoff_flag,normal_calls,
  ball_obj.flags,game_info.auto_hike_cntr,cambotinfo.carrier_mode,cambotinfo.player_valid,
  cambotinfo.player_count,cambot.tick_cnt,draw_count);
 for(int k=0;k<draw_count;k++)printf("\t%d\t%d",draw_bounds[k],draw_values[k]);
 float v[]={cambot.x,cambot.y,cambot.z,cambot.a,cambot.b,cambot.c,cambot.d,cambot.theta,cambot.phi,
 cambot.theta_trg,cambot.phi_trg,cambot.xv,cambot.yv,cambot.zv,cambot.dv,cambot.tv,cambot.pv,
 cambot.xa,cambot.ya,cambot.za,cambot.da,cambot.ta,cambot.pa,cambot.sin_theta,cambot.cos_theta,cambot.sin_phi,cambot.cos_phi};
 for(unsigned k=0;k<sizeof(v)/sizeof(*v);k++)printf("\t%.9g",v[k]);
 for(int k=0;k<12;k++)printf("\t%.9g",cambot.xform[k]);
 printf("\t%.9g\t%.9g\t%.9g\t%.9g\t%.9g\t%.9g",cambotinfo.fa,cambotinfo.fb,cambotinfo.fc,cambotinfo.fd,cambotinfo.fdx,cambotinfo.fdz);
 int target_kind=cambot.pmodeobj?3:0,target_actor=-1;
 if(cambot.pmodeobj==&ball_obj.odata)target_kind=2;
 for(int id=0;id<14;id++)if(cambot.pmodeobj==&player_blocks[id].odata){target_kind=1;target_actor=id;}
 printf("\t%d\t%d\t%d\n",game_info.last_carrier,target_kind,target_actor);
 phase=1;
 if(event_table>=0){
  if(select_csi_item(fixture_tables[event_table]))cambot.pmodeobj=&player_blocks[event_actor].odata;
 }
 if(event_shake){shaker_amp=fixture_shaker_amplitude;shaker_tot=fixture_shaker_ticks;if(event_actor==game_info.ball_carrier)shaker_cnt=0;}
 phase=0;draw_count=0;
 if(!read_sample())longjmp(finished,1);
}
int main(int argc,char **argv){
 if(argc!=5)return 2;opening_choice=atoi(argv[2]);fixture_shaker_amplitude=atoi(argv[3]);fixture_shaker_ticks=atoi(argv[4]);
 input=fopen(argv[1],"r");if(!input)return 3;
 game_info.game_state=GS_GAME_MODE;game_info.off_turnover=1;
 game_info.team_play[0]=&teams[0];game_info.team_play[1]=&teams[1];
 if(!read_sample())return 4;if(!setjmp(finished))cambot_proc(NULL);
 fclose(input);return 0;
}
"""
    return text


def fixture_row(tick, *, mode=2, side=0, carrier=3, actor_mode=5, quarter=1, play=0, game_flags=0,
                result=0, flight=0, ball_type=0, ball_flags=0, hh=False, pb=False, event=-1, ordinal=0,
                shake=0, play_flags=0, low=False, gap=False, heading_offset=0):
    actors = []
    for actor in range(14):
        x = (-178 + (actor % 7 - 3) * 9) * (-1 if side else 1)
        z = (actor % 7 - 3) * 13
        if actor == carrier:
            x = (-185 + tick * .73) * (-1 if side else 1)
            z = tick % 83 - 41
        actors.extend([actor_mode if actor == carrier else 7, 0, ((147 + actor * 59) & 1023) + heading_offset,
                       x, 0, z, 94 if actor == (side * 7 + 1) else 0, 42, 100])
    ball = [(-158 + tick * 1.11) * (-1 if side else 1), (tick % 37) * .7, tick % 91 - 45,
            -3 if side else 3, 2, .4, -156 if not side else 156, 0, 0,
            -193 if side else 193, 0, 96, 91, 53, 100, ball_type, ball_flags, flight, side * 7 + 3, side * 7 + 1]
    label = tick + 200 if gap and tick >= 415 else tick
    return [label, mode, game_flags, play, result, quarter, side, side, 30, carrier, int(hh), int(pb), 0,
            play_flags, 512, 256 if low else 384] + ball + actors + [event, ordinal, side * 7 + 3, shake]


def make_cases(catalog, normal):
    cases = []
    for side in range(2):
        for dimensions in range(3):
            rows = []
            for t in range(280):
                mode = 1 if t < 17 else 2 if t < 240 else 3 if t < 262 else 1
                carrier = side * 7 + 3 if t < 67 else -1 if t < 137 else side * 7 + 1
                rows.append(fixture_row(t, mode=mode, side=side, carrier=carrier,
                    actor_mode=5 if t < 67 else 7, game_flags=0x400 if t >= 240 and t < 262 else 0,
                    result=5 if 240 <= t < 262 else 0, flight=max(0, t - 66),
                    ball_flags=0x100 if 67 <= t < 137 else 0, hh=dimensions == 1, pb=dimensions == 2,
                    low=dimensions == 2))
            cases.append((f"normal-side{side}-dimensions{dimensions}", -1, rows))
    for side in range(2):
        for ordinal in range(11):
            rows = []
            approach = normal["tables"]["csi_kickoff"][ordinal]["auto_hike_ticks"]
            launch = approach + 17
            for t in range(435):
                rows.append(fixture_row(t, mode=1 if t < approach else 5 if t < launch else 2 if t < 400 else 3 if t < 415 else 1,
                    side=side, carrier=-1 if t < 395 else side * 7 + 1, actor_mode=7, quarter=0,
                    play=1 if t < 415 else 0, ball_type=3, game_flags=0x200 if t >= launch else 0,
                    flight=max(0, t - launch + 1), ball_flags=0x20 if t >= 395 else 0, hh=side != 0, gap=True))
            cases.append((f"kickoff-side{side}-ordinal{ordinal}", ordinal, rows))
    for side in range(2):
        for kind, flags in (("quick-exact", 0x1000), ("quick-extra-flags", 0x1200),
                            ("onside", 0x10000), ("subsequent-full", 0)):
            rows = []
            for t in range(160):
                mode = 3 if t < 3 else 0 if t == 3 else 1 if t < 24 else 5 if t < 35 and "quick" not in kind else 2
                rows.append(fixture_row(t, mode=mode, side=side, carrier=-1 if t < 95 else side * 7 + 1,
                    actor_mode=7, quarter=1, play=1, ball_type=3, game_flags=flags,
                    flight=max(0, t - 34), ball_flags=0x80 if "quick" in kind and t < 35 else 0,
                    hh=side != 0, result=0))
            cases.append((f"kickoff-side{side}-{kind}-gopher-resume", -1, rows))
    for table_index, table in enumerate(catalog["tables"]):
        for entry in table["entries"]:
            if entry["mode"] >= 22:
                continue
            for forced in (False, True):
                total = entry["mode_ticks"]
                duration = min(total, 41) if forced else total
                rows = []
                for t in range(duration + 27):
                    mode = 1 if t < 4 else 3 if t >= 16 and t < duration + 10 else 1 if t >= duration + 10 else 2
                    rows.append(fixture_row(t, mode=mode, carrier=3, actor_mode=7, hh=table_index % 2 != 0,
                        game_flags=0x400 if mode == 3 else 0, event=table_index if t == 8 else -1,
                        ordinal=entry["ordinal"], shake=1 if t == 12 else 0, heading_offset=2048 if forced else -2048))
                cases.append((f"impact-{table_index}-{entry['ordinal']}-{'newplay' if forced else 'expiry'}", -1, rows))
    for side in range(2):
        for deflected in (False, True):
            rows = []
            for t in range(320):
                rows.append(fixture_row(t, mode=1 if t < 9 else 2 if t < 250 else 3,
                    side=side, carrier=side * 7 + 3 if t < 12 else -1, actor_mode=5,
                    game_flags=0x200 if t >= 12 else 0, play=2, play_flags=4,
                    ball_type=4, ball_flags=0x200 if deflected and t >= 30 else 0, flight=max(0, t - 11)))
            cases.append((f"field-goal-side{side}-{'deflected' if deflected else 'tracking'}", -1, rows))
    for side in range(2):
        tracking = next(rows for name, _, rows in cases if name == f"field-goal-side{side}-tracking")
        for menu in (False, True):
            for next_goal in (True, False):
                rows = list(tracking)
                for wake in range(320, 328):
                    row = fixture_row(wake - 320, mode=1, side=side, carrier=side * 7 + 3, actor_mode=5,
                                      play=2 if next_goal else 0, play_flags=4 if next_goal else 0,
                                      ball_type=4 if next_goal else 0)
                    row[0] = wake + (200 if menu else 0)
                    rows.append(row)
                cases.append((f"field-goal-side{side}-new-{'field-goal' if next_goal else 'normal'}-"
                              f"{'menu' if menu else 'continuous'}", -1, rows))
            rows = [list(row) for row in tracking[:24]]
            for row in rows[20:]:
                row[1] = 3
            for wake in range(24, 32):
                row = fixture_row(wake - 24, mode=1, side=side, carrier=side * 7 + 3, actor_mode=5,
                                  play=2, play_flags=4, ball_type=4)
                row[0] = wake + (200 if menu else 0)
                rows.append(row)
            cases.append((f"field-goal-side{side}-interrupted-tracking-{'menu' if menu else 'continuous'}", -1, rows))
    return cases


def generate(source_root, output):
    source_root, output = Path(source_root).resolve(strict=True), Path(output).absolute()
    output.mkdir(parents=True, exist_ok=True)
    tree = verify_tree(json.loads((source_root / "tree.json").read_text()))
    inputs = {label: read_pinned(source_root, tree, label) for label in INPUTS}
    text = harness(inputs)
    cfile, exe = output / "active-cambot.c", output / "active-cambot"
    cfile.write_text(text)
    env = dict(os.environ, TMPDIR=str(output), TMP=str(output), TEMP=str(output))
    command = ["cc", "-std=gnu11", "-O0", "-ffp-contract=off", str(cfile), "-lm", "-o", str(exe)]
    built = subprocess.run(command, env=env, capture_output=True, text=True)
    (output / "compile.log").write_text(built.stdout + built.stderr)
    if built.returncode:
        raise RuntimeError("Active original camera C compile failed:\n" + built.stderr)
    impact, normal = impact_catalog(source_root), normal_catalog(source_root)
    (output / "impact.json").write_bytes(files.json_bytes(impact))
    (output / "normal.json").write_bytes(files.json_bytes(normal))
    shaker = next(cue for cue in impact["animation_cues"] if cue["kind"] == "shaker")
    records = []
    for name, opening, rows in make_cases(impact, normal):
        path = output / (name + ".inputs.tsv")
        path.write_text("".join("\t".join(str(value) for value in row) + "\n" for row in rows))
        command = [str(exe), str(path), str(opening), *map(str, shaker["arguments"])]
        run = subprocess.run(command, env=env, capture_output=True, text=True)
        (output / (name + ".stderr.log")).write_text(run.stderr)
        if run.returncode:
            raise RuntimeError("Original active CAMBOT failed " + name + ":\n" + run.stderr)
        expected = output / (name + ".expected.tsv")
        expected.write_text(run.stdout)
        if len(run.stdout.splitlines()) != len(rows):
            raise RuntimeError("Original active CAMBOT wake inventory changed: " + name)
        records.append(dict(name=name, inputs=str(path), expected=str(expected), ticks=len(rows), opening=opening,
                            inputs_sha256=files.sha256(path.read_bytes()), expected_sha256=files.sha256(expected.read_bytes())))
    if (len(records) != 100 or sum(row["ticks"] for row in records[:88]) != 31332 or
            sum(row["ticks"] for row in records[88:]) != 2752):
        raise RuntimeError("Retain exactly88/31332 original cases and12/2752 coupled lifecycle cases.")
    receipt = dict(schema="original-active-cambot-host/v1", status="passed", revision=REVISION, tree=TREE,
                   original_inputs={name: dict(files.fingerprint(raw)) for name, raw in inputs.items()},
                   generated_sha256=files.sha256(text.encode()), executable_sha256=files.sha256(exe.read_bytes()),
                   compile=command, compiler="cc -std=gnu11 -O0 -ffp-contract=off; host libm float stores",
                   cases=records, source_wakes=sum(row["ticks"] for row in records),
                   retained_cases=88, retained_source_wakes=31332, lifecycle_cases=12, lifecycle_source_wakes=2752,
                   adapters="Unchanged active cambot_proc, normal, tracking, integrator, kickoff/special, "
                            "zoom/spline/rail/P2P/GOPHER/resume and original ANIM selector. Only the normal entry "
                            "has a counting wrapper. Source sleep1 emits a frame and reads the next authored "
                            "immutable observation, with read-only last_carrier and pmodeobj identity telemetry. "
                            "Native message/idiot-box/scheduler services abort if reached; "
                            "game_state=GS_GAME_MODE, idiot_boxes_shown=2, off_turnover=1. Unselected side/top/"
                            "attract/path/course abort. No replay/debug/SPECIAL_MODES. Actual C data is local.",
                   arithmetic="Binary32 stores; native fast trig, FMA, platform RNG, global qsort/clock/ABI/BSS/pixels unqualified.",
                   numeric_undefined_domains="Invalid receiver/actor indices, zero FG X velocity, zero spline/P2P horizontal "
                                             "or total length, zero point-stream unit endpoint are excluded, not clamped.")
    receipt["compile"] = ["cc", "-std=gnu11", "-O0", "-ffp-contract=off", str(cfile), "-lm", "-o", str(exe)]
    (output / "index.json").write_bytes(files.json_bytes(receipt))
    return output / "index.json"
