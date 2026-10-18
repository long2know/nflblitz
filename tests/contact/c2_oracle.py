#!/usr/bin/env python3
"""Compile pinned original C2 void bodies with the explicitly disclosed host seams."""
import argparse
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import re
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[2]
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("--original-root", type=Path, required=True)
parser.add_argument("--process-root", type=Path, required=True)
parser.add_argument("--output", type=Path, required=True)
args = parser.parse_args()
SRC = args.original_root.absolute()
MIDWAY = args.process_root.absolute()
OUT = args.output.absolute()
for root in (SRC, MIDWAY, OUT):
    if not root.is_relative_to(ROOT) or root.is_symlink():
        raise ValueError("C2 oracle inputs/outputs must be explicit regular local paths.")
OUT.mkdir(parents=True, exist_ok=True)
sys.path.insert(0, str(ROOT / "tests/contact"))
import source
import translate


def sha(b):
    return hashlib.sha256(b).hexdigest()


def extract(path, symbol, pointer=False):
    b = path.read_bytes()
    if pointer:
        match = re.search(rb"(?m)^(?:struct process_node \*|char \*\s*)" + symbol.encode() + rb"\s*\([^;{}]*\)\s*\{", b)
        if not match:
            raise ValueError("Missing original pointer function: " + symbol)
        start, end = match.start(), b.index(b"\n}", match.end()) + 2
        body = b[start:end]
    else:
        start, end, body = source.function(b, symbol)
    inputs[symbol] = {"path": str(path.relative_to(ROOT)), "source_line": b[:start].count(b"\n") + 1,
                      "source_sha256": sha(b), "unchanged_body_sha256": sha(body)}
    return '#line %d "%s"\n' % (inputs[symbol]["source_line"], path.name) + body.decode("latin1") + "\n"


inputs = {}
verified = source.read(SRC)
assert source.blob((SRC / "include/GAME.H").read_bytes()) == "09d65c92971c32c44b1dc49fb4e4e98b371b1cd3"
for n, blob in (("lib/goose/process.c", "967ca53910f24ddfa48a14485113a8fd0c989959"),
                ("lib/goose/mthread.c", "7b44637c3d59e2256ad099226b7ea17f108907ef")):
    assert source.blob((MIDWAY / n).read_bytes()) == blob
headers = "\n".join((SRC / n).read_text(encoding="latin1") for n in
                    ("include/GAME.H", "include/player.h", "include/sndcalls.h"))
needed = ("FIELDHB", "FIELDHZ", "FIELDW", "FOCFAC", "FIELDX", "WORLDX",
          "GM_IN_PLAY", "GM_PLAY_OVER", "PT_KICKOFF", "GF_PLAY_ENDING", "GF_POSS_CHANGE", "GF_PASS_THROWN",
          "PR_TOUCHDOWN", "PR_CONVERSION", "MODE_QB", "LB_PASS", "LB_BOBBLE", "LB_LATERAL", "LB_FUMBLE",
          "BF_BOUNCE", "PEC_CAR_TACKLED_IB", "PEC_CAR_TOUCHDOWN", "PEC_CAR_TACKLED_OWNEZ",
          "PF_CELEBRATE", "VOLUME4", "VOLUME3", "PRIORITY_LVL3", "PRIORITY_LVL4", "PRIORITY_LVL5",
          "WHISTLE1_SND", "INTERCEPT_SND")
definitions = []
for name in needed:
    match = re.search(r"(?m)^[ \t]*#define[ \t]+" + name + r"(?:\([^\r\n]*\)|[ \t]+[^\r\n]*)", headers)
    if not match:
        raise ValueError("Missing original macro: " + name)
    definitions.append(match[0])
native = r'''
#define _XOPEN_SOURCE 700
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <math.h>
#include <ucontext.h>
#define FALSE 0
#define TRUE 1
typedef struct obj_3d { void *pobj_hit; int plyrmode,fwd,flags; float x,y,z; } obj_3d;
typedef struct fbplyr_data { obj_3d odata; int plyrnum,team,celebrate_time; } fbplyr_data;
static fbplyr_data player_blocks[14];
static struct { int off_side,down,first_down,game_flags,game_mode,play_type,play_result,ball_carrier,last_pos,play_end_cause,game_time; int team_fire[2];float play_end_pos; } game_info;
static int clock_active,said_latehit_sp,said_30_sp,said_20_sp,said_10_sp,said_5_sp,no_result_box;
static struct {int int_receiver,type,flags;} ball_obj;
static struct {int interceptions,lost_fumbles;} tdata_blocks[2];
static int p2p_angles[14][14],taunted;
static char taunt_1_anim[]="taunt_1_anim",taunt_2_anim[]="taunt_2_anim",taunt_3_anim[]="taunt_3_anim",taunt_4_anim[]="taunt_4_anim",m_laugh1_anim[]="m_laugh1_anim";
static char m_qbwhat_anim[]="m_qbwhat_anim",m_qbfite_anim[]="m_qbfite_anim",m_mscat1_anim[]="m_mscat1_anim",m_mscat2_anim[]="m_mscat2_anim";
static char *taunt_bnk_str="taunts3",*plyr_bnk_str="players";
static int tape[8],cursor,bounds[8],values[8],changed,before_change=-1,selected=-1;
static char *selected_script;
static int call_id,call_volume,call_pan,call_priority,call_count,call_frame;
static char *call_bank;
static int source_frame,game_wakes,node_count,spawned_mode;
static ucontext_t host_context;
struct process_node {
 struct process_node *next,*prev;
 int process_sleep_time,process_flags,process_id,run_level,args[4],started,finished,id,first_frame,emit_frame;
 void (*entry_func)();
 ucontext_t context;
 char stack[65536];
};
static struct process_node nodes[4],game_node,*plist,*cur_proc;
static int randrng(int bound){int raw=tape[cursor];bounds[cursor]=bound;values[cursor]=bound?raw%bound:0;cursor++;return values[cursor-1];}
static void change_anim(obj_3d *p,char *s){(void)p;changed=1;selected_script=s;before_change=taunted;
 char *names[]={taunt_1_anim,taunt_2_anim,taunt_3_anim,taunt_4_anim,m_laugh1_anim,m_qbwhat_anim,m_qbfite_anim,m_mscat1_anim,m_mscat2_anim};
 for(int i=0;i<9;i++)if(s==names[i])selected=i;
}
static void snd_scall_bank(char *bank,int id,int volume,int pan,int priority){
 call_bank=bank;call_id=id;call_volume=volume;call_pan=pan;call_priority=priority;call_count++;call_frame=source_frame;
 if(cur_proc&&cur_proc!=&game_node)cur_proc->emit_frame=source_frame;
}
static void process_entry(void){
 cur_proc->first_frame=source_frame;
 cur_proc->entry_func(cur_proc->args);
 cur_proc->finished=1;
}
static void sleep(int ticks){cur_proc->process_sleep_time|=ticks;swapcontext(&cur_proc->context,&host_context);}
static struct process_node *create_process(char *name,int id,int *args,void (*entry_func)(),int astack);
static void whistle_proc(int *args);
static int in_bounds(obj_3d *pobj);
static int over_first_down(obj_3d *pobj);
static int bearing(int plyr1,int plyr2);
'''
native += "\n".join(definitions) + "\n"
player = (SRC / "player.c").read_text(encoding="latin1")
sound = (SRC / "sounds.c").read_text(encoding="latin1")
game = (SRC / "Game.c").read_text(encoding="latin1")
for name in ("mad_qb_anim_tbl", "miss_pass_reactions_tbl"):
    native += re.search(r"static char \*" + name + r"\[\]\s*=\s*\{.*?\};", player, re.S)[0] + "\n"
for name in ("short_taunt_sp", "long_taunt_sp", "laugh_taunt_sp", "histep_taunt_sp"):
    native += re.search(r"static int " + name + r"\[\]\s*=\s*\{.*?\};", sound, re.S)[0] + "\n"
for name in ("announcer_strs", "taunt_bnk_strs"):
    declaration = re.search(r"(?m)^.*\b" + name + r"\[\]\s*=\s*\{[^\r\n]+", game)[0]
    native += declaration + "\n"
native += extract(MIDWAY / "lib/goose/mthread.c", "qcreate", True)
for name in ("in_bounds", "over_first_down", "celebrate", "player_down", "whistle_proc",
             "maybe_taunt", "maybe_qb_mad", "maybe_receiver_mad"):
    native += extract(SRC / "player.c", name)
native += extract(SRC / "pmisc.c", "bearing")
native += extract(SRC / "sounds.c", "taunt_speech")
for symbol, line in (("original_qb_whistle", 1839), ("original_receiver_whistle", 2434)):
    statement = player.splitlines()[line - 1]
    native += '\nstatic void '+symbol+'(void){\n#line '+str(line)+' "player.c"\n'+statement+'\n}\n'
for name in ("get_ancr_str", "get_taunt_str"):
    native += extract(SRC / "Game.c", name, True)
nfl = (SRC / "nfl.c").read_bytes()
epoch_start = nfl.index(b"\t\t// wait for the play to end")
epoch_end = nfl.index(b"\t\tno_result_box = 0;", epoch_start) + len(b"\t\tno_result_box = 0;")
epoch_window = nfl[epoch_start:epoch_end]
inputs["game_proc_wait_reset_window"] = {"path": str((SRC / "nfl.c").relative_to(ROOT)),
    "source_line": nfl[:epoch_start].count(b"\n")+1, "source_sha256":sha(nfl),
    "unchanged_body_sha256":sha(epoch_window), "whole_game_proc_claimed":False}
native += "\n#define DBG_KO_BOOL TRUE\nstatic void epoch_proc(int *args){(void)args;\n"
native += epoch_window.decode("latin1") + "\n}\n"
process = (MIDWAY / "lib/goose/process.c").read_text(encoding="latin1")
front = process[process.index("\t/* Add node to beginning of active list */"):
                process.index("\t/* Initialize the data for the new process node */")]
native += r'''
#line 1 "explicit-native-execution-seams"
static struct process_node *create_process(char *name,int id,int *args,void (*entry_func)(),int astack){
 (void)name;(void)astack;
 struct process_node *pn=&nodes[node_count++];
 memset(pn,0,sizeof(*pn));
''' + front + r'''
 pn->process_sleep_time = 1;
 pn->process_flags = 0;
 pn->run_level = 0;
 pn->process_id=id;pn->entry_func=entry_func;pn->id=node_count;
 memcpy(pn->args,args,4*sizeof(int));spawned_mode=game_info.game_mode;
 getcontext(&pn->context);pn->context.uc_stack.ss_sp=pn->stack;pn->context.uc_stack.ss_size=sizeof(pn->stack);pn->context.uc_link=&host_context;
 makecontext(&pn->context,process_entry,0);
 return pn;
}
static void dispatch(void){
 cur_proc = plist;
 while(cur_proc){
  if(cur_proc->process_sleep_time > 0){
   cur_proc->process_sleep_time--;
   if(!cur_proc->process_sleep_time){
    if(cur_proc==&game_node){game_wakes++;cur_proc->process_sleep_time=1;}
    else if(!cur_proc->finished)swapcontext(&host_context,&cur_proc->context);
   }
  }
  cur_proc = cur_proc->next;
 }
}
static void reset(void){
 memset(player_blocks,0,sizeof(player_blocks));memset(&game_info,0,sizeof(game_info));
 memset(&ball_obj,0,sizeof(ball_obj));memset(tdata_blocks,0,sizeof(tdata_blocks));memset(p2p_angles,0,sizeof(p2p_angles));
 memset(tape,0,sizeof(tape));memset(bounds,0,sizeof(bounds));memset(values,0,sizeof(values));memset(&game_node,0,sizeof(game_node));
 plist=&game_node;game_node.process_sleep_time=1;cur_proc=NULL;node_count=game_wakes=source_frame=0;
 cursor=changed=call_count=0;before_change=selected=-1;selected_script=NULL;call_bank=NULL;call_frame=0;spawned_mode=-1;
 for(int i=0;i<14;i++){player_blocks[i].plyrnum=i;player_blocks[i].team=i/7;}
}
int main(void){
 char op;
 while(scanf(" %c",&op)==1){
  reset();
  if(op=='C'){
   int kind,actor,hit,off,down,first,type,result,carrier,receiver,facing,angle,mode,gm,ball,fire,chance,choice,flags,latch;float x,z;
   if(scanf("%d%d%d%d%d%d%d%d%d%d%d%d%d%d%d%d%d%d%d%d%d%f%f",&kind,&actor,&hit,&latch,&flags,&off,&down,&first,&type,&result,&carrier,&receiver,&facing,&angle,&mode,&gm,&ball,&fire,&chance,&choice,&taunted,&x,&z)!=23)return 3;
   taunted=latch;game_info.game_flags=flags;game_info.off_side=off;game_info.down=down;game_info.first_down=first;game_info.play_type=type;game_info.play_result=result;
   game_info.ball_carrier=carrier;game_info.game_mode=gm;game_info.team_fire[!off]=fire;ball_obj.int_receiver=receiver;ball_obj.type=ball;
   obj_3d *p=&player_blocks[actor].odata;p->plyrmode=mode;p->fwd=facing;p->pobj_hit=hit<0?NULL:&player_blocks[hit].odata;
   if(hit>=0){player_blocks[hit].odata.x=x;player_blocks[hit].odata.z=z;}
   for(int i=0;i<14;i++)p2p_angles[actor][i]=angle;
   tape[0]=chance;tape[1]=choice;
   if(kind==0)maybe_taunt(p);else if(kind==1)maybe_qb_mad(p);else maybe_receiver_mad(p);
   printf("{\"selected\":%d,\"changed\":%d,\"before\":%d,\"taunted\":%d,\"draws\":%d,\"bounds\":[",selected,changed,before_change,taunted,cursor);
   for(int i=0;i<cursor;i++)printf("%s%d",i?",":"",bounds[i]);puts("]}");
  }else if(op=='S'){
   int sp,chance,index,bank;scanf("%d%d%d%d",&sp,&chance,&index,&bank);tape[0]=chance;tape[1]=index;taunt_bnk_str=bank?"taunts4":"taunts3";
   taunt_speech(&player_blocks[0].odata,sp,999);
   printf("{\"calls\":%d,\"id\":%d,\"volume\":%d,\"priority\":%d,\"draws\":%d,\"bank\":\"%s\",\"bounds\":[",call_count,call_count?call_id:-1,call_count?call_volume:-1,call_count?call_priority:-1,cursor,call_bank?call_bank:"");
   for(int i=0;i<cursor;i++)printf("%s%d",i?",":"",bounds[i]);puts("]}");
  }else if(op=='I'){
   int mode,index;scanf("%d%d",&mode,&index);tape[0]=index;
   if(mode==5)original_qb_whistle();else original_receiver_whistle();
   printf("{\"id\":%d,\"volume\":%d,\"priority\":%d,\"draws\":%d,\"bank\":\"%s\"}\n",call_id,call_volume,call_priority,cursor,call_bank);
  }else if(op=='B'){
   int first,second;scanf("%d%d",&first,&second);tape[0]=first;tape[1]=second;char a[16],b[16];
   get_ancr_str(a);get_taunt_str(b);printf("{\"announcer\":\"%s\",\"taunt\":\"%s\",\"draws\":%d}\n",a,b,cursor);
  }else if(op=='W'){
   int count,chance,index;scanf("%d%d%d",&count,&chance,&index);
   for(int n=0;n<count;n++)qcreate("whistle",0,whistle_proc,0,0,0,0);
   for(int n=0;n<count;n++)tape[n]=chance;
   for(int n=count;n<2*count;n++)tape[n]=index;
   int first_id=plist->id;
   for(source_frame=1;source_frame<=32;source_frame++)dispatch();
   printf("{\"draws\":%d,\"calls\":%d,\"firstId\":%d,\"firstFrame\":%d,\"emitFrame\":%d,\"sleep\":%d,\"runLevel\":%d,\"pid\":%d,\"gameWakes\":%d,\"id\":%d,\"volume\":%d,\"priority\":%d}\n",
    cursor,call_count,first_id,nodes[count-1].first_frame,nodes[count-1].emit_frame,nodes[0].process_sleep_time,nodes[0].run_level,nodes[0].process_id,game_wakes,call_count?call_id:-1,call_count?call_volume:-1,call_count?call_priority:-1);
  }else if(op=='P'){
   int gm,carrier;float x,spot;scanf("%d%d%f%f",&gm,&carrier,&x,&spot);game_info.off_side=game_info.last_pos=0;game_info.game_mode=gm;game_info.ball_carrier=carrier;game_info.play_end_pos=spot;player_blocks[0].odata.x=x;
   player_down(&player_blocks[0].odata);
   printf("{\"mode\":%d,\"cause\":%d,\"spot\":%.9g,\"celebrate\":%d,\"flags\":%d,\"created\":%d,\"createdMode\":%d,\"draws\":%d}\n",
    game_info.game_mode,game_info.play_end_cause,game_info.play_end_pos,player_blocks[0].celebrate_time,player_blocks[0].odata.flags,node_count,spawned_mode,cursor);
  }else if(op=='E'){
   int after,before=0;scanf("%d",&after);game_info.game_mode=GM_IN_PLAY;game_info.game_flags=GF_PLAY_ENDING;
   taunted=clock_active=said_latehit_sp=said_30_sp=said_20_sp=said_10_sp=said_5_sp=no_result_box=1;
   qcreate("game_proc",0,epoch_proc,0,0,0,0);
   for(source_frame=1;source_frame<=after+1;source_frame++){dispatch();if(source_frame==after){before=taunted;game_info.game_mode=GM_PLAY_OVER;}}
   printf("{\"before\":%d,\"after\":%d,\"clock\":%d,\"resetFrame\":%d,\"finished\":%d,\"draws\":%d}\n",before,taunted,clock_active,after+1,nodes[0].finished,cursor);
  }else return 4;
 }
 return ferror(stdin)?2:0;
}
'''
(OUT / "oracle.c").write_text(native, encoding="latin1")
env = dict(os.environ, TMPDIR=str(OUT), TMP=str(OUT), TEMP=str(OUT))
command = ["gcc", "-std=c99", "-O0", "-fno-fast-math", "-ffp-contract=off", "-Wall", "-Wextra",
           str(OUT / "oracle.c"), "-lm", "-o", str(OUT / "oracle")]
build = subprocess.run(command, cwd=ROOT, capture_output=True, text=True, env=env, timeout=60)
(OUT / "build.log").write_text(build.stdout + build.stderr)
if build.returncode:
    raise ValueError("Original C build failed:\n" + build.stderr)

cases = []
def add(label, **changes):
    row = dict(kind=0,actor=7,hit=0,latch=0,flags=0x400,off=0,down=2,first=60,type=0,result=0,
               carrier=0,receiver=0,facing=0,angle=0,mode=3,gm=3,ball=0,fire=0,chance=0,choice=0,x=-156.0,z=0.0)
    row.update(changes);row["label"]=label;cases.append(row)
for fire in (0,1):
    for chance in range(100):
        for choice in range(5):add("carrier-probability",fire=fire,chance=chance,choice=choice)
for chance in range(100):
    for choice in range(5):add("receiver-probability",carrier=-1,flags=0x480,chance=chance,choice=choice)
for kind,choices in ((1,3),(2,2)):
    for chance in range(100):
        for choice in range(choices):
            add("mad-probability",kind=kind,actor=0,hit=1,carrier=0 if kind==1 else -1,
                receiver=0,mode=5 if kind==1 else 7,chance=chance,choice=choice)
for changes in (dict(hit=-1),dict(latch=1),dict(flags=0),dict(off=1),dict(type=1),dict(first=20),
                dict(down=1),dict(flags=0x408),dict(result=1),dict(result=11),dict(angle=257),
                dict(carrier=1,receiver=0),dict(carrier=-1,flags=0x400)):
    add("ordered-guard",**changes)
for kind in (1,2):
    for changes in (dict(latch=1),dict(gm=2),dict(gm=1),dict(mode=3),dict(carrier=1),dict(receiver=1),dict(ball=1)):
        row=dict(kind=kind,actor=0,hit=1,carrier=0 if kind==1 else -1,receiver=0,mode=5 if kind==1 else 7)
        row.update(changes);add("mad-guard",**row)
for facing in (-1023,-437,-1,0,256,512,1023):
    for angle in (0,1,256,257,512,768,1023):
        add("signed-bearing",facing=facing,angle=angle)
for x in (-468.00003,-468.0,-156.0,77.99999,78.0,468.0,468.00003):
    for z in (-182.85002,-182.85,0.0,182.85,182.85002):
        for first in (60,100):add("first-down-bounds",x=x,z=z,first=first)
keys = "kind actor hit latch flags off down first type result carrier receiver facing angle mode gm ball fire chance choice".split()
requests = ["C " + " ".join(str(row[k]) for k in keys) + " 0 %.9g %.9g"%(row["x"],row["z"]) for row in cases]
speech = []
for sp in (-5,-4,-3,-2,-1,0,16,47):
    for chance in range(100):
        for index in (0,1,2,7,9):
            for bank in (0,1):
                speech.append(dict(sp=sp,chance=chance,index=index,bank=bank))
requests += ["S %(sp)d %(chance)d %(index)d %(bank)d"%row for row in speech]
banks = [dict(first=a,second=b) for a in (0,1,2,2147483647) for b in (0,1,2,2147483647)]
requests += ["B %(first)d %(second)d"%row for row in banks]
whistles = [dict(count=n,chance=c,index=i) for n in (1,2) for c in (0,24,25,99) for i in (0,1)]
requests += ["W %(count)d %(chance)d %(index)d"%row for row in whistles]
downs = [dict(gm=gm,carrier=carrier,x=x,spot=spot) for gm,carrier in ((2,0),(2,1),(3,0))
         for x,spot in ((-156,-156),(-148.2,-156),(1,601),(389,390),(-400,-400))]
requests += ["P %(gm)d %(carrier)d %(x).9g %(spot).9g"%row for row in downs]
epochs = [dict(over_after=n) for n in (1,2,5)]
requests += ["E %(over_after)d"%row for row in epochs]
immediate = [dict(mode=m,index=i) for m in (5,7) for i in (0,1,2147483647)]
requests += ["I %(mode)d %(index)d"%row for row in immediate]
execution = subprocess.run([str(OUT / "oracle")], cwd=ROOT, input="\n".join(requests)+"\n",
                           text=True,capture_output=True,env=env,timeout=120)
if execution.returncode:
    raise ValueError("Original C execution failed: "+execution.stderr)
results = [json.loads(line) for line in execution.stdout.splitlines()]
assert len(results)==len(requests)
at=0
inventory={}
for name, rows in (("callbacks",cases),("speech",speech),("banks",banks),("whistles",whistles),("player_down",downs),("end_epochs",epochs),("immediate_whistles",immediate)):
    inventory[name]=[dict(input=row,expected=result) for row,result in zip(rows,results[at:at+len(rows)])];at+=len(rows)
(OUT / "cases.json").write_text(json.dumps(inventory,indent=2)+"\n")
names = ["taunt_1_anim","taunt_2_anim","taunt_3_anim","taunt_4_anim","m_laugh1_anim","m_qbwhat_anim","m_qbfite_anim","m_mscat1_anim","m_mscat2_anim"]
def selected(original):
    scripts=source.scripts(original["plyrseq.c"]);result={}
    for name in names:
        s=dict(scripts[name]);end=next(n for n,v in enumerate(s["rows"]) if v[1]=="byte" and v[2]=="ANI_END")
        s["rows"]=s["rows"][:end+1];result[name]=s
    return result
translate.selected=selected
programs=translate.translate(verified,SRC)
(OUT / "source-programs.json").write_text(json.dumps(programs,indent=2)+"\n")
receipt={"schema":"source-contact-c2-original-C/v1","compiler_command":command,"exit_code":0,
         "proper_original_void_functions_no_undefined_scalar_returns":True,"unchanged_source_bodies":inputs,
         "case_counts":{n:len(v) for n,v in inventory.items()},"source_programs":len(programs),
         "native_execution_seams":["Only target process stack/ASM transfer replaced by POSIX ucontext, retaining one original whistle_proc invocation across sleep(30)",
                                   "Full unchanged qcreate calls explicit allocation/stack boundary; exact original front-link fragment and sleep_time1/defaultlevel0 are retained",
                                   "Original dispatcher head/decrement-before-execute/next-pointer statements; actual sleep OR at awake0, no fabricated callback return"],
         "not_claimed":["MIPS ABI MODE_ZERO residue","Full target scheduler or qsort tie behavior","DSP/bank loading/audio playback"],
         "oracle_c_sha256":sha((OUT/"oracle.c").read_bytes()),"cases_sha256":sha((OUT/"cases.json").read_bytes()),
         "programs_sha256":sha((OUT/"source-programs.json").read_bytes())}
(OUT / "receipt.json").write_text(json.dumps(receipt,indent=2)+"\n")
print(json.dumps({"status":"passed","counts":receipt["case_counts"],"source_programs":len(programs)}))
