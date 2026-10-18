#!/usr/bin/env python3
"""Observe the original negative argument, STOP before its load, prove tail ineligibility."""
import argparse
from pathlib import Path
import hashlib
import json
import os
import re
import subprocess
import sys

R=Path(__file__).resolve().parents[2]
parser=argparse.ArgumentParser(description=__doc__)
parser.add_argument("--original-root",type=Path,required=True)
parser.add_argument("--oracle-root",type=Path,required=True)
parser.add_argument("--output",type=Path,required=True)
args=parser.parse_args()
original=args.original_root.absolute()
oracle_root=args.oracle_root.absolute()
OUT=args.output.absolute()
for root in (original,oracle_root,OUT):
    if not root.is_relative_to(R) or root.is_symlink():
        raise ValueError("Negative-target original oracle paths must be explicit and local.")
OUT.mkdir(parents=True,exist_ok=True)
sys.path.insert(0,str(R/"tests/contact"))
import source
player=(original/"player.c").read_bytes()
pmisc=(original/"pmisc.c").read_bytes()
assert source.blob(player)=="5744c51f30b625b9f80b92388ab49ff0ebc5bb2c"
assert source.blob(pmisc)=="50426d40ecd03515cecd295fb80a63ffa0cb0683"
_,_,bearing=source.function(pmisc,"bearing")
_,_,taunt=source.function(player,"maybe_taunt")
base=oracle_root/"oracle.c"
text=base.read_bytes().decode("latin1")
assert text.count(bearing.decode("latin1"))==1 and text.count(taunt.decode("latin1"))==1
assert text.count("int main(void){")==1
text="#include <setjmp.h>\n"+text
text=text.replace("int main(void){","static int unused_original_protocol_main(void){",1)
text=text.replace(bearing.decode("latin1"),r'''
static jmp_buf bearing_stop;
static int bearing_me,bearing_him,bearing_stopped;
int bearing(int me,int him){
 bearing_me=me;bearing_him=him;bearing_stopped=1;
 longjmp(bearing_stop,1);
}
''')
taunt_text=taunt.decode("latin1")
array=re.search(r"char\s+\*taunts\[\]\s*=\s*\{.*?\};",taunt_text,re.S)[0]
tail=taunt_text[taunt_text.index("\t// okay... we can taunt.."):taunt_text.rfind("\n}")]
text+='\nstatic void original_post_bearing_effects(obj_3d *pobj){\nfbplyr_data *pphit=(fbplyr_data *)pobj->pobj_hit;\n'+array+"\n"+tail+"\n}\n"
text+=r'''
int main(void){
 reset();taunted=0;
 game_info.game_flags=0x480;game_info.off_side=1;game_info.down=3;game_info.first_down=100;
 game_info.play_type=0;game_info.play_result=5;game_info.game_mode=3;game_info.ball_carrier=-1;
 ball_obj.int_receiver=11;ball_obj.type=0;
 obj_3d *p=&player_blocks[4].odata;p->fwd=228;p->plyrmode=9;p->pobj_hit=&player_blocks[7].odata;
 player_blocks[7].odata.x=-169.93988f;player_blocks[7].odata.z=28.273537f;
 if(setjmp(bearing_stop)==0)maybe_taunt(p);
 if(!bearing_stopped||bearing_me!=4||bearing_him!=-1||cursor||changed||taunted)return 10;
 int cases=0,total_draws=0,total_changed=0;
 for(int actor=0;actor<14;actor++)for(int hit=0;hit<14;hit++)for(int receiver=0;receiver<14;receiver++){
  if(hit==receiver)continue;
  reset();taunted=0;game_info.ball_carrier=-1;game_info.off_side=hit/7;ball_obj.int_receiver=receiver;game_info.game_flags=0x480;
  player_blocks[actor].odata.pobj_hit=&player_blocks[hit].odata;
  original_post_bearing_effects(&player_blocks[actor].odata);
  cases++;total_draws+=cursor;total_changed+=changed;
  if(taunted||cursor||changed)return 11;
 }
 printf("{\"bearingObservedMe\":%d,\"bearingObservedHim\":%d,\"stoppedBeforeAnyBearingRead\":true,\"postBearingNoncandidateCases\":%d,\"postBearingChanceDraws\":%d,\"postBearingAnimationChanges\":%d,\"postBearingLatchWrites\":0}\n",
  bearing_me,bearing_him,cases,total_draws,total_changed);
 return 0;
}
'''
p=OUT/"original-trap.c";p.write_text(text,encoding="latin1")
env=dict(os.environ,TMPDIR=str(OUT),TMP=str(OUT),TEMP=str(OUT))
cmd=["gcc","-std=c99","-O0","-fno-fast-math","-ffp-contract=off",str(p),"-lm","-o",str(OUT/"original-trap")]
compiled=subprocess.run(cmd,cwd=R,env=env,capture_output=True,text=True,timeout=60)
(OUT/"compile.log").write_text(compiled.stdout+compiled.stderr)
assert compiled.returncode==0,compiled.stderr
run=subprocess.run([str(OUT/"original-trap")],cwd=R,env=env,capture_output=True,text=True,timeout=60)
assert run.returncode==0,run.stdout+run.stderr
receipt=json.loads(run.stdout)
digest=lambda b:hashlib.sha256(b).hexdigest()
receipt.update(schema="source-c2-original-negative-target-observation/v1",
 originalUnchangedMaybeTauntSha256=digest(taunt),originalBearingBodyNotExecutedSha256=digest(bearing),
 originalPostBearingTailSha256=digest(tail.encode("latin1")),
 originalPrefixSourceSha256=digest(base.read_bytes()),compiledOracleSha256=digest(p.read_bytes()),
 command=cmd,
 scope="Unchanged original maybe_taunt stops at bearing observation via longjmp, no scalar return/read; unchanged final source predicates in separate defined void tail test. NOT a whole negative-target callback golden.",
 shippedTargetMemoryTrapOrABIOutcomeQualified=False,portabilityDecisionExplicitUserApproved=False)
(OUT/"trap-receipt.json").write_text(json.dumps(receipt,indent=2)+"\n")
print(json.dumps(receipt))
