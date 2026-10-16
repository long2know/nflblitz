"""Host functional oracle: unchanged pinned original special-camera routines.

This is not target-MIPS/fast-trig or a complete source scheduler oracle. Only the
surrounding graphics/global services are supplied by the small host fixture.
"""
import json
import os
from pathlib import Path
import re
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools/assets"))
sys.path.insert(0, str(ROOT / "tools/rosters"))
from prepare_cinematics import convert, INPUTS
from prepare_teams import read_pinned
from c_source import CData, closing_brace
from export_rosters import read_json, verify_tree
import prepare_player as files


def declaration(data, name):
    match = re.search(r"\b(?:float|SPLINE|CAMSEQINFO)\s+" + re.escape(name) +
                      r"\s*(?:\[[^]]*\])?\s*=\s*\{", data.text)
    if match is None:
        raise ValueError("Missing original camera declaration: " + name)
    end = closing_brace(data.text, match.end() - 1)
    return data.text[match.start():end + 1] + ";\n"


def generate(source_root, artifacts):
    source_root, artifacts = Path(source_root).resolve(strict=True), Path(artifacts).absolute()
    artifacts.mkdir(parents=True, exist_ok=True)
    tree = verify_tree(read_json((source_root / "tree.json").read_bytes()))
    inputs = {path: read_pinned(source_root, tree, path) for path in INPUTS}
    catalog = convert(source_root)
    data = CData(inputs["cambot.c"].decode("latin1"))
    pipe = CData(read_pinned(source_root, tree, "lib/ani3d/pipe3D.c").decode("latin1"))
    camera = CData(inputs["CamData.c"].decode("latin1"))
    prefix = r"""
#include <math.h>
#include <stdio.h>
#include <string.h>
#define M_TWOPI ((float)(M_PI*2.0))
#define DG2RD(x) ((float)((x)*M_PI/180.0))
#define GR2RD(x) ((x)*(M_TWOPI/1024.0f))
#define fsin(x) ((float)sin(x))
#define fcos(x) ((float)cos(x))
#define fsqrt(x) ((float)sqrt(x))
#define rsqrt(x) (1.0f/(float)sqrt(x))
#define THETA_MAX M_TWOPI
#define THETA_MIN 0.0f
#define PHI_MIN DG2RD(89.0f)
#define PHI_MAX DG2RD(-89.0f)
#define TRGY_ZOOM (5.0f*(390.0f/300.0f*2.0f))
#define TRGY_SPLINE (4.0f*(390.0f/300.0f*2.0f))
#define TRGY_RAIL TRGY_ZOOM
#define ACCEL_DEFAULT .08f
#define DECEL_DEFAULT .35f
#define SNAPTARG 2
#define ACCLTPOS 4
#define ACCLTANG 8
#define MOVETPOS 16
#define MOVETANG 32
#define MOVEVIEW 64
#define NEWTHETA 128
#define MOVETARG (MOVETPOS|MOVETANG)
#define CAM_ZOOM 4
#define CAM_ZO_NR 5
#define CAM_ZO_NFR 6
#define CAM_SPLINE 7
#define CAM_SP_NFR 8
#define CAM_SP_CNFR 9
#define CAM_RAIL 10
#define CAM_RA_NF 11
#define CAM_RA_NFT 12
#define NMODE_NOCSI 23
typedef struct { float x,y,z; } VERTEX;
typedef struct { float x,y,z,w; } vec3d;
typedef struct { float x1,y1,z1,x2,y2,z2,x3,y3,z3,x4,y4,z4; } SPLINE;
typedef struct { int mode,mode_cnt,trav_cnt; float theta,phi,d; void *pseqinfo; } CAMSEQINFO;
typedef struct {
 CAMSEQINFO csi; SPLINE s; void *pmodeobj; int tick_cnt, mode_prev;
 float theta_trg,phi_trg,theta,phi,d,x,y,z,a,b,c;
 float dv,xv,yv,zv,tv,pv,da,xa,ya,za,ta,pa;
 float sin_theta,cos_theta,sin_phi,cos_phi,e,f,g,xform[12];
} VIEWCOORD;
typedef struct { float fa,fb,fc,fd; float *p_track,*p_accel; } CAMBOTINFO;
typedef struct { float x,y,z; int fwd; } obj_3d;
static VIEWCOORD cambot;
static CAMBOTINFO cambotinfo;
static float mode_trgy_adjust;
static float track_def[]={2,2,2,2},accel_def[]={.08f,.08f,.08f,.08f};
static float track_p2p[]={.25f,.25f,.25f,.25f},accel_p2p[]={.25f,.25f,.25f,.25f};
#define P_XA (&cambot.xa)
#define P_YA (&cambot.ya)
#define P_ZA (&cambot.za)
#define P_DA (&cambot.da)
static int ModeRevert(VIEWCOORD *p, CAMBOTINFO *i) { (void)i; p->csi.mode=0; return 0; }
"""
    signatures = {
        "ValidateTheta": "static float ValidateTheta(float theta)",
        "ValidatePhi": "static float ValidatePhi(float phi)",
        "BuffAccel": "static void BuffAccel(float *pf,float diff,float buff,float rate)",
        "DoSpline": "static void DoSpline(VERTEX *pv,SPLINE *ps,int cur_tick,int tot_tick)",
        "DoAccelDecel": "static void DoAccelDecel(int cur_tick,int tot_tick)",
        "cambot_zoom": "static int cambot_zoom(VIEWCOORD *pcam,CAMBOTINFO *pci)",
        "cambot_spline": "static int cambot_spline(VIEWCOORD *pcam,CAMBOTINFO *pci)",
        "cambot_rail": "static int cambot_rail(VIEWCOORD *pcam,CAMBOTINFO *pci)",
    }
    text = prefix
    for name, signature in (("magv", "static float magv(vec3d *v)"),
                            ("norm", "static void norm(vec3d *v)"),
                            ("vxv", "static void vxv(vec3d *v1,vec3d *v2,vec3d *v3)")):
        text += signature + pipe.function(name)[1] + "\n"
    text += "static void update_cambot_xform(void)" + data.function("update_cambot_xform")[1] + "\n"
    for name in ("ValidateTheta", "ValidatePhi"):
        text += signatures[name] + data.function(name)[1] + "\n"
    text += r"""
static void set_cambot_theta(float t) {
 cambot.theta=cambot.theta_trg=ValidateTheta(t);
 cambot.sin_theta=fsin(cambot.theta);cambot.cos_theta=fcos(cambot.theta);
}
static void set_cambot_phi(float p) {
 cambot.phi=cambot.phi_trg=ValidatePhi(p);
 cambot.sin_phi=fsin(cambot.phi);cambot.cos_phi=fcos(cambot.phi);
}
"""
    for name in list(signatures)[2:]:
        text += signatures[name] + data.function(name)[1] + "\n"
    dependencies = set()
    for table in catalog["tables"]:
        rows, _ = camera.array(table["symbol"])
        for row in rows[:-1]:
            if row[6].raw.startswith("&"):
                dependencies.add(row[6].raw[1:])
    for name in sorted(dependencies):
        text += declaration(camera, name)
    for table in catalog["tables"]:
        text += declaration(camera, table["symbol"])
    text += "static CAMSEQINFO *tables[]={" + ",".join(t["symbol"] for t in catalog["tables"]) + "};\n"
    text += "static int counts[]={" + ",".join(str(len(t["entries"])) for t in catalog["tables"]) + "};\n"
    # This is the unchanged original generic post-mode integration block, excluding
    # unrotation/special-selector services and the separately tested random shaker.
    start = data.text.index("// Validate cambot angles") if "// Validate cambot angles" in data.text else -1
    # CData deliberately removes comments. Bound the actual source block by its executable statements.
    start = data.text.index("pcam->theta     = ValidateTheta(pcam->theta);")
    end = data.text.index("if (shaker_cnt < shaker_tot)", start)
    post = data.text[start:end]
    a = post.index("if (flag & UNROTATE)")
    b = post.index("if (flag) {", a)
    post = post[:a] + post[b:]
    text += "int main(void){ int table,ordinal,tick,flag; VIEWCOORD *pcam=&cambot; CAMBOTINFO *pci=&cambotinfo; obj_3d obj;\n"
    text += "for(table=0;table<7;table++)for(ordinal=0;ordinal<counts[table];ordinal++){\n"
    text += r"""
 CAMSEQINFO selected=tables[table][ordinal];
 if(selected.mode>=22)continue;
 memset(&cambot,0,sizeof cambot);memset(&cambotinfo,0,sizeof cambotinfo);
 cambot.csi=selected;cambot.pmodeobj=&obj;
 cambot.x=-120;cambot.y=100;cambot.z=-30;
 cambot.a=12;cambot.b=15;cambot.c=9;cambot.d=160;
 cambot.xv=.35f;cambot.yv=-.1f;cambot.zv=.22f;
 cambot.tv=.0004f;cambot.pv=-.0003f;
 set_cambot_theta(DG2RD(90));set_cambot_phi(DG2RD(-25));
 pci->p_track=track_def;pci->p_accel=accel_def;
 for(tick=0;tick<selected.mode_cnt;tick++){
  obj.x=20.0f+tick*.075f;obj.y=(tick%31)*.05f;obj.z=-8.0f+(tick%57)*.025f;obj.fwd=700;
  switch(cambot.csi.mode){
   case 4:case 5:case 6:flag=cambot_zoom(pcam,pci);break;
   case 7:case 8:case 9:flag=cambot_spline(pcam,pci);break;
   default:flag=cambot_rail(pcam,pci);break;
  }
"""
    text += post
    text += r"""
  cambot.e=cambot.a;cambot.f=cambot.b+10.0f;cambot.g=cambot.c;
  update_cambot_xform();
  printf("%d\t%d\t%d",table,ordinal,tick);
  printf("\t%.9g\t%.9g\t%.9g\t%.9g\t%.9g\t%.9g\t%.9g\t%.9g\t%.9g",
   cambot.x,cambot.y,cambot.z,cambot.a,cambot.b,cambot.c,cambot.d,cambot.theta,cambot.phi);
  printf("\t%.9g\t%.9g\t%.9g\t%.9g\t%.9g\t%.9g",
   cambot.xv,cambot.yv,cambot.zv,cambot.dv,cambot.tv,cambot.pv);
  {int k;for(k=0;k<12;k++)printf("\t%.9g",cambot.xform[k]);}
  printf("\n");
 }
}return 0;}
"""
    cfile = artifacts / "source-camera-oracle.c"
    exe = artifacts / "source-camera-oracle"
    cfile.write_text(text)
    env = dict(os.environ, TMPDIR=str(artifacts), TMP=str(artifacts), TEMP=str(artifacts))
    compiled = subprocess.run(["cc", "-std=gnu11", "-O0", "-ffp-contract=off", str(cfile), "-lm", "-o", str(exe)],
                              env=env, capture_output=True, text=True)
    if compiled.returncode:
        raise RuntimeError("Actual original camera oracle compile failed:\n" + compiled.stderr)
    run = subprocess.run([str(exe)], capture_output=True, text=True, env=env)
    if run.returncode:
        raise RuntimeError("Original camera oracle failed:\n" + run.stderr)
    output = artifacts / "source-camera-oracle.tsv"
    output.write_text(run.stdout)
    receipt = {
        "status": "passed", "source_revision": catalog["source_revision"],
        "functional_original_routines": list(signatures), "original_data_tables": [t["symbol"] for t in catalog["tables"]],
        "source_samples": len(run.stdout.splitlines()), "output_sha256": files.sha256(run.stdout.encode()),
        "shimmed_services": "fsin/fcos/fsqrt use host libm; ModeRevert excluded from numeric traces; "
                            "no original normal camera, source scheduler, RNG or MIPS bit-parity claim.",
        "generator_sha256": files.sha256(Path(__file__).read_bytes()),
        "generated_harness_sha256": files.sha256(text.encode()), "executable_sha256": files.sha256(exe.read_bytes()),
    }
    (artifacts / "source-camera-oracle.json").write_bytes(files.json_bytes(receipt))
    return output
