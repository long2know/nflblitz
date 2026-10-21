"""Authenticate/extract actual original C into private output, then execute bounded defined rows."""
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import sys

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[1]
sys.path.insert(0, str(ROOT / "tools/assets"))
sys.path.insert(0, str(ROOT / "tools/rosters"))
from c_source import closing_brace, without_comments, Expressions, Macro


def sha(raw):
    return hashlib.sha256(raw).hexdigest()


def authenticate(root):
    pins = json.loads((HERE / "source-inputs.json").read_bytes())
    result = {}
    for name, expected in pins["inputs"].items():
        path = Path(root).absolute() / name
        if path.resolve() != path or not path.is_file():
            raise ValueError("Missing/symlinked named original input: " + name)
        raw = path.read_bytes()
        if hashlib.sha1(b"blob " + str(len(raw)).encode() + b"\0" + raw).hexdigest() != expected:
            raise ValueError("Original source blob mismatch: " + name)
        result[name] = raw
    tree = json.loads((Path(root) / "tree.json").read_bytes())
    entries = {row["path"]: row for row in tree["tree"]}
    if tree["sha"] != pins["tree"] or tree.get("truncated") or any(
            entries[name]["sha"] != blob or entries[name]["type"] != "blob"
            for name, blob in pins["inputs"].items()):
        raise ValueError("Exact complete canonical tree and named source paths are mandatory.")
    return result


def macro(sources, name):
    for raw in sources.values():
        if raw is not None:
            rows = re.findall(r"^\s*#define\s+" + re.escape(name) + r"\s+(.+)$",
                              raw.decode("latin1"), re.M)
            if rows:
                return rows[0].split("//")[0].split("/*")[0].strip()
    raise ValueError("Missing exact original constant: " + name)


def function_body(raw, name):
    original=raw.decode("latin1")
    clean=without_comments(original)
    matches=list(re.finditer(r"\b"+re.escape(name)+r"\s*\([^;{}]*\)\s*\{",clean))
    if len(matches)!=1:raise ValueError("Expected one authenticated original definition: "+name)
    begin=matches[0].end()-1
    end=closing_brace(clean,begin)
    return original[begin:end+1]


def cases(constants):
    result = []
    dbps, db = constants["MODE_DBPS"], constants["MODE_DB"]
    for mask in range(16):
        for side in range(2):
            for captain in range(2):
                controls = [p if mask & (1 << s) else -1 for s, p in enumerate([3, 9, 3, 13])]
                result.append(dict(name="physical-mask-%d-side-%d-captain-%d" % (mask, side, captain),
                                   kind=0, layout=4, mask=mask, side=side, captain=captain, controls=controls))
    for side in range(2):
        for captain in range(2):
            result.append(dict(name="defined-two-%d-%d" % (side,captain), kind=0, layout=2, mask=3,
                               side=side,captain=captain,controls=[3,10,-1,-1]))
    for side in range(2):
        result.append(dict(name="duplicate-physical-order-%d" % side,kind=0,layout=4,mask=15,
                           side=side,captain=1,controls=[5,5,5,5]))
    threshold = 5 * 57 - 15
    def add(name, caller=3, **changes):
        row = dict(name=name, kind=1, layout=4,mask=1,side=1,captain=0,controls=[caller,-1,-1,-1],
                   caller=caller,fresh=1,counter=threshold-1,onside=0,mode=dbps,live=0,value=0,
                   modes=[dbps]*14)
        row.update(changes); result.append(row)
        return row
    add("cyclic-not-nearest")
    add("home-wrap",6)
    add("away-wrap",13,side=0)
    add("occupied-skip",mask=3,controls=[3,4,-1,-1])
    add("non-DBPS-skip")["modes"][4] = 0
    add("accept-DB")["modes"][4] = db
    add("strict-counter-boundary",counter=threshold)
    add("counter-above-boundary",counter=threshold+1)
    add("no-fresh-held-B",fresh=0)
    add("onside",onside=1)
    add("drone",mask=0,controls=[-1,-1,-1,-1])
    add("wrong-entered-mode",mode=constants["MODE_WPNPS"])
    add("first-LiveBall-DBPS-continuation",live=1)
    add("destination-uninterruptible")
    add("ten-loop-unconditional-assignment",mask=3,controls=[3,6,-1,-1],modes=[0]*14)
    add("earlier-destination",6)
    add("later-destination",3)
    add("defined-two-caller",layout=2,mask=3,controls=[3,10,-1,-1])
    for value in range(10):
        add("ordered-randrng10-value-%d" % value,value=value)
    for i,row in enumerate(result):
        row["id"] = i
        row.setdefault("modes",[dbps]*14)
        for name,default in dict(caller=3,fresh=0,counter=0,onside=0,mode=dbps,live=0,value=0).items():
            row.setdefault(name,default)
    if len(result) - 10 > 96:
        raise ValueError("The bounded96-row plus ten RNG-result budget was exceeded.")
    return result


def generate(source_root, profile, output):
    output = Path(output).absolute()
    output.mkdir(parents=True,exist_ok=True)
    work=output/"work";work.mkdir()
    env=dict(os.environ,TMPDIR=str(work),TMP=str(work),TEMP=str(work))
    executions=[]
    def execute(command,stdin=None):
        process=subprocess.run(command,input=stdin,cwd=ROOT,env=env,capture_output=True,text=True)
        executions.append(dict(command=command,exit=process.returncode,
            diagnostics=process.stderr.splitlines(),stdout_sha256=sha(process.stdout.encode()),
            stdin_sha256=None if stdin is None else sha(stdin.encode())))
        return process
    sources = authenticate(source_root)
    constants = {name:int(macro(sources,name),0) for name in ("MODE_DBPS","MODE_DB","MODE_WPNPS")}
    definitions = ["#define tsec 57"]
    names = ("MODE_DBPS","MODE_DB","MODE_UNINT","MODE_INTSTREAM","MODE_ANCHORY","MODE_ANCHORXZ",
             "MODE_SECONDARY","MODE_INTERP","MODE_PUPPET","MODE_PUPPETEER","MODE_TURNHACK","AH_COMPRESSED",
             "AA_TOEND","AA_TOGROUND","AA_VAR_FRAME","AA_VAR_COUNT","AA_PROPFRAME","RF_TURNHACK",
             "GF_ONSIDE_KICK","GM_IN_PLAY","MAX_HIKE_TIME","GRAVITY","VOLUME2","PRIORITY_LVL1","PRIORITY_LVL2")
    for name in names:
        definitions.append("#define " + name + " " + macro(sources,name))
    seq = "\n".join(line.split("#",1)[0] for line in sources["plyrseq.c"].decode("latin1").splitlines())
    def program(name):
        start = re.search(r"^"+re.escape(name)+r":\s*$",seq,re.M)
        if not start:raise ValueError("Missing exact original program label: "+name)
        tail=seq[start.end():]
        end=re.search(r"^[A-Za-z_]\w*:\s*$|^\s*\.globl\b",tail,re.M)
        return tail[:end.start()] if end else tail
    hop,stance = program("m_hopclp_anim"),program("stance_anim_int")
    streams = re.findall(r"ANI_INTSTREAM\s*,\s*(\d+)\s*,\s*(\d+)",hop)
    sound = re.search(r"attack_snd\s*\)\s*,\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)",hop)
    if sound is None:
        sound = re.search(r"attack_snd[^,]*,\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)",hop)
    mode = re.search(r"\.byte\s+ANI_SETMODE\s+\.word\s+([A-Za-z0-9_|]+)",hop)
    velocity = re.search(r"\.byte\s+ANI_SETYVEL\s+\.float\s+([0-9.]+)",hop)
    ticks = re.search(r"ANI_INTTOSEQ\s*,\s*(\d+)",stance)
    if len(streams)!=2 or not all((sound,mode,velocity,ticks)) or "stance_anim_int" not in hop:
        raise ValueError("Original hop/stance instruction grammar changed; no invented operands.")
    mode_value=Expressions({name:Macro(macro(sources,name)) for name in ("MODE_UNINT","MODE_ANCHORXZ")}).evaluate(mode[1])
    values = dict(HOP_MODE=str(mode_value), HOP_Y_VELOCITY=velocity[1]+"f", STANCE_TICKS=ticks[1],
                  HOP_FIRST_FRAMES=streams[0][0],HOP_FIRST_TICKS=streams[0][1],
                  HOP_SECOND_FRAMES=streams[1][0],HOP_SECOND_TICKS=streams[1][1],
                  HOP_SOUND_A=sound[1],HOP_SOUND_B=sound[2],HOP_SOUND_C=sound[3])
    definitions += ["#define "+name+" "+value for name,value in values.items()]
    (output / "original-defines.h").write_text("\n".join(definitions)+"\n")
    declarations = []
    for key,clip_name,frames in (("hop","M_HOPCLP",35),("rush","RUSH_C",75)):
        clip = json.loads((Path(profile)/"animation282/clips"/(clip_name+".json")).read_bytes())
        if len(clip["frames"])!=frames or len(clip["joints"])!=15:
            raise ValueError("Exact authentic selected-frame/joint inventory required: "+clip_name)
        roots = [v for row in clip["frames"] for v in row["root_translation"]["scaled_runtime_xyz"]]
        rotations = [v for row in clip["frames"] for q in row["rotations_xyzw"] for v in q]
        def literal(v):
            text=format(float(v),".17g")
            return text + (".0" if "." not in text and "e" not in text else "") + "f"
        declarations += ["#define "+key.upper()+"_FRAME_COUNT "+str(frames),
                         "static float "+key+"_roots[]={"+",".join(map(literal,roots))+"};",
                         "static float "+key+"_rotations[]={"+",".join(map(literal,rotations))+"};"]
    (output/"selected-poses.h").write_text("\n".join(declarations)+"\n")
    bodies=[]
    for path,name,signature in [
        ("lib/ani3d/pipe3D.c","qxq","void qxq(float *q1,float *q2,float *q)"),
        ("lib/ani3d/pipe3D.c","slerp","void slerp(float *p,float *q,float t,float *qt)"),
        ("lib/ani3d/ANIM.C","_next_frame","static void _next_frame(obj_3d *pobj,anidata *pani,int count)"),
        ("lib/ani3d/ANIM.C","_interpolate_frame","static void _interpolate_frame(obj_3d *pobj,anidata *pani)"),
        ("lib/ani3d/ANIM.C","_stream_frame","static void _stream_frame(obj_3d *pobj,anidata *pani)"),
        ("lib/ani3d/ANIM.C","_ani_intstream","static int _ani_intstream(obj_3d *pobj,anidata *pani)"),
        ("lib/ani3d/ANIM.C","_ani_inttoseq","static int _ani_inttoseq(obj_3d *pobj,anidata *pani)"),
        ("lib/ani3d/ANIM.C","_ani_runtoend","static int _ani_runtoend(obj_3d *pobj,anidata *pani)"),
        ("sounds.c","attack_snd","static void attack_snd(obj_3d *pobj,int snd1,int snd2,int snd3)"),
        ("pmisc.c","update_stations","static void original_update_stations(void)"),
    ]:
        body=function_body(sources[path],name)
        bodies.append((signature+body+"\n",path,name,sha(body.encode("latin1"))))
    sounds=without_comments(sources["sounds.c"].decode("latin1"))
    match=re.search(r"\b(?:static\s+)?int\s+plr_attack_snds\s*\[\s*\]\s*=\s*\{",sounds)
    if not match: raise ValueError("The original inactive attack pool declaration is required, not a synthetic pool.")
    end=closing_brace(sounds,match.end()-1)
    original=sounds[match.start():end+1]+";\n"
    for name in sorted(set(re.findall(r"\bP_[A-Z0-9_]+\b",original))):
        definitions.append("#define "+name+" "+macro(sources,name))
    (output/"original-defines.h").write_text("\n".join(definitions)+"\n")
    original+="".join(row[0] for row in bodies)
    original+="static void update_stations(void) { strcat(effects,\"U\"); original_update_stations(); }\n"
    body=function_body(sources["player.c"],"_mode_dbps")
    original+="static int _mode_dbps(fbplyr_data *ppdata)"+body+"\n"
    (output/"original-bodies.c").write_text(original,encoding="latin1")
    command=["cc","-std=c99","-O0","-ffp-contract=off","-Wall","-Wextra","-Werror",
             "-Wno-unused-parameter","-I",str(output),str(HERE/"oracle_shim.c"),"-lm","-o",str(output/"original-oracle")]
    compiled=execute(command)
    (output/"compile.log").write_text(compiled.stdout+compiled.stderr)
    if compiled.returncode or compiled.stdout or compiled.stderr:
        raise RuntimeError("Original-C oracle must compile with zero diagnostics: "+compiled.stdout+compiled.stderr)
    rows=cases(constants)
    encoded=[" ".join(map(str,[row[k] for k in ("id","kind","layout","mask","side","captain")]+
                                  row["controls"]+row["modes"]+
                                  [row[k] for k in ("caller","fresh","counter","onside","mode","live","value")])) for row in rows]
    p=execute([str(output/"original-oracle")],"\n".join(encoded)+"\n")
    if p.returncode: raise RuntimeError("Original-C defined-case execution failed: "+p.stderr)
    observed=[json.loads(line) for line in p.stdout.splitlines()]
    if len(observed)!=len(rows): raise RuntimeError("The actual original-C row inventory differs.")
    for row,value in zip(rows,observed):
        if row["id"]!=value["id"]:raise RuntimeError("Original-C row identity differs.")
        row["expected"]=value
    def vm_rows(interruption=None,secondary=False):
        command=[str(output/"original-oracle"),"vm"]+([] if interruption is None else [str(interruption)])
        if secondary:command=[str(output/"original-oracle"),"vm","-1","1"]
        process=execute(command)
        if process.returncode:raise RuntimeError("Original-C VM failed: "+process.stderr)
        values=[json.loads(line) for line in process.stdout.splitlines()]
        interpolating=int(macro(sources,"MODE_INTERP"),0)
        if len(values)!=116 or [row["offset"] for row in values]!=list(range(116)) or any(
                len(row["root"])!=3 or len(row["position"])!=3 or len(row["velocity"])!=3 or
                len(row["pose"])!=60 or len(row["fromPose"])!=(60 if row["mode"]&interpolating else 0)
                or row["hold"]!=1 for row in values):
            raise RuntimeError("The complete original-C VM/hold/root/velocity/fifteen-pose inventory is required.")
        return values
    vm=vm_rows()
    entry=int(streams[0][1])+int(streams[1][1])
    duration=int(ticks[1])
    interrupted=[dict(interrupt_offset=offset,rows=vm_rows(offset))
                 for offset in (entry+duration//2-2,entry+3*duration//4)]
    result=dict(schema="defensive-presnap-switch-original-C/v1",cases=rows,vm=vm,vm_interrupted=interrupted,
                vm_secondary=vm_rows(secondary=True),
                commands=[command],executions=executions,constants=constants,compiler_diagnostics=[],
                input_sha256={name:sha(raw) for name,raw in sources.items()},
                extracted_bodies=[dict(path=p,function=n,body_sha256=h) for _,p,n,h in bodies],
                boundary="Actual source bodies on host C with authoring selected poses/std math; no generated ANI ABI, native scheduler, DSP or fast-trig claim.")
    (output/"oracle.json").write_text(json.dumps(result,indent=2)+"\n")
    return result
