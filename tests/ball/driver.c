/* Authored host projection, prescribed inputs and observations only.
   Original functions/tables are inserted at runtime from byte-pinned sources. */
#include <stdio.h>
#include <stdint.h>
#include <math.h>
#include <string.h>
#include <stdarg.h>
#define TRUE 1
#define FALSE 0
#define GREATER(a,b) ((a)>(b)?(a):(b))
#define LESSER(a,b) ((a)<(b)?(a):(b))
#define fsqrt sqrtf
#define FIELDHB 468.0f
#define FIELDHZ 390.0f
#define FIELDW 182.85f
#define GRAVITY -0.08f
#define FOCFAC (FIELDHZ / 300.0f)
#define FIELDX(x) ((int)((FIELDHZ + (game_info.off_side ? -1.0f*(x) : (x))) / (FOCFAC*6.0f)))
#define LB_PASS 0
#define LB_FUMBLE 1
#define LB_PUNT 2
#define LB_KICKOFF 3
#define LB_FIELD_GOAL 4
#define LB_BOBBLE 5
#define LB_LATERAL 6
#define BF_WHITE 1
#define BF_VEL_PITCH 2
#define BF_TURBO_PASS 4
#define BF_FIRE_PASS 8
#define BF_BOBBLE 16
#define BF_BOUNCE 32
#define BF_FREEZE 128
#define BF_DEFLECTED 512
#define BF_SPINING 1024
#define BF_PHANTOM 2048
#define BF_FG_BOUNCE 4096
#define BF_FG_HITPOST 8192
#define GF_REC_IN_EZ 1
#define GF_LEFT_EZ 2
#define GF_POSS_CHANGE 8
#define GF_BOBBLE 16
#define GF_FWD_PASS 64
#define GF_PASS_THROWN 128
#define GF_PLAY_ENDING 1024
#define GF_FUMBLE 2048
#define GF_QUICK_KICK 4096
#define GF_AIR_CATCH 8192
#define GF_BALL_CROSS_LOS 16384
#define GF_HIKING_BALL 32768
#define GF_ONSIDE_KICK 65536
#define GF_OPENING_KICK 262144
#define MODE_UNINT 2
#define MODE_REACTING 128
#define MODE_NOCATCH 2048
#define MODE_PUPPET 4194304
#define MODE_PUPPETEER 8388608
#define MODE_QB 5
#define MODE_WPN 7
#define PLF_FAKE_FG 16
#define CONTROL_TIME 18
#define CR_NONE 0
#define CR_CATCH 1
#define CR_MISS 2
#define CR_BOBBLE 3
#define CR_BATDOWN 4
#define CR_DEFLECT 5
#define JOINT_RWRIST 4
#define JOINT_LWRIST 7
#define GM_PRE_SNAP 0
#define GM_IN_PLAY 1
#define GM_PLAY_OVER 2
#define GM_PRE_KICKOFF 3
#define PEC_DEAD_BALL_IB 4
#define TEAM_MASK(side) (1<<(side))
#define PLYRH(p) ((p)->hcap)
#define ISDRONE(p) ((p)->station == -1)
#define ISHUMAN(p) ((p)->station != -1)
#define ISHUMANTEAM(p) ((p_status & TEAM_MASK((p)->team)) != 0)
#define ISDRONETEAM(p) (!ISHUMANTEAM(p))
#define MSG_FUMBLE_PID 0
typedef struct { unsigned animode; int seq_index; } anidata;
typedef struct { float x,y,z,vx,vy,vz,ay; unsigned flags; int plyrmode; anidata adata[2]; int fwd,fwd2; } obj_3d;
typedef struct { obj_3d odata; int team,plyrnum,station,no_catch_time,hcap; float jpos[15][3]; } fbplyr_data;
typedef struct { int flags; } play_t;
static play_t plays[2];
static struct {
 int off_side,last_pos,off_init,ball_carrier,game_mode,play_end_cause,down,los,game_quarter,play_type;
 int team_fire[2],spin_cnt[2],fumbles_on_play,play_time;
 unsigned game_flags;
 float play_end_pos,play_end_z,fumble_x;
 play_t *team_play[2];
} game_info;
static struct {
 obj_3d odata; int type,flags,flight_time,total_time,who_threw,int_receiver,catch_pcount,bounces,bounce_time;
 int pitch,pitch_rate,phi;
 float scale,tx,ty,tz,ox,oy,oz;
} ball_obj;
static struct { int deflections,bobbles,fumbles,lost_fumbles,interceptions,off_plays,last_receiver; } tdata_blocks[2];
static struct { float x,y,z,xform[12]; } cambot;
static fbplyr_data actors[14];
static int scores[2],pcount=1000,p_status,drones_lose,drones_winning,pup_noassist,pup_nofumbles,pup_superdrones;
static int pup_nointercept,abusing_spin,force_fumble,force_bobble,oracle_result,clock_active;
static float victim_y,mvx,mvz;
static const char *plyr_bnk_str="external-sound-boundary";
#define P_MISS_BALL_SND 0
#define VOLUME3 0
#define PRIORITY_LVL3 0
#define BALL_HIT_GRND_SND 0
#define VOLUME1 0
#define PRIORITY_LVL2 0
#define GOAL_X 460.0f
#define GOAL_Z 22.0f
#define GOAL_Y 27.0f
#define GOAL_TOP 103.5f
#define BALL_FLOOR 0.8f
#define GF_THROUGH_UPRIGHTS 4
#define PEC_BALL_OOB 5
#define PT_KICKOFF 1
static void snd_scall_bank(const char *bank,...) {(void)bank;}
static int tape[32],cursor,bounds[32];
static int randrng(int n) { bounds[cursor]=n; return n ? tape[cursor++] % n : (cursor++,0); }
static int compute_score(int side) { return scores[side]; }
static void deflected_ball_sp(void) {}
static void fumble_ball_sounds(void) {}
static void display_fumble_proc(void) {}
static void qcreate(const char *name, ...) {(void)name;}
static uint32_t seed=0x527ab113u;
static uint32_t next(void) { seed=seed*1664525u+1013904223u; return seed; }
static uint32_t bits(float f) { union {float f;uint32_t i;}u;u.f=f;return u.i; }
static float real(int low,int range) { return (float)(low+(int)(next()%(uint32_t)range)); }
static void draw_tape(int index) {
 cursor=0;
 for(int i=0;i<32;i++)tape[i]=(int)(next()>>8);
 if(index%4==0)tape[0]=0;
 if(index%4==1)tape[0]=99;
 if(index%4==2)tape[0]=9999;
}
static void print_tape(void) { for(int i=0;i<32;i++)printf("\t%d",tape[i]); }
static void print_bounds(void) { printf("\t%d",cursor);for(int i=0;i<cursor;i++)printf(",%d",bounds[i]); }
static void reset(void) {
 memset(&game_info,0,sizeof(game_info));memset(&ball_obj,0,sizeof(ball_obj));
 memset(tdata_blocks,0,sizeof(tdata_blocks));memset(actors,0,sizeof(actors));memset(plays,0,sizeof(plays));
 game_info.team_play[0]=plays;game_info.team_play[1]=plays+1;
 memset(&cambot,0,sizeof(cambot));cambot.xform[0]=cambot.xform[5]=cambot.xform[10]=1.0f;
 for(int i=0;i<14;i++){actors[i].team=i/7;actors[i].plyrnum=i;actors[i].station=-1;}
 force_fumble=force_bobble=0;oracle_result=0;
}
/* RUNTIME_ORIGINAL_SINE_TABLE */
static float isin(int h) { h&=1023;if(h<=256)return sine_table[h];if(h<=512)return sine_table[512-h];
 if(h<=768)return -sine_table[h-512];return -sine_table[1024-h]; }
static float icos(int h) { return isin(h+256); }
static int iasin(float v) { int negative=v<0.0f;float x=negative?-v:v;int lo=0,hi=255;
 while(lo<hi){int mid=(lo+hi)/2;if(sine_table[mid+1]<x)lo=mid+1;else hi=mid;}
 float ld=sine_table[lo]-x,hd=sine_table[lo+1]-x;
 int result=hd*hd<ld*ld?lo+1:lo;return negative?-result:result; }
#define FRELX(x,z,h) ((z)*isin(h)+(x)*icos(h))
#define FRELZ(x,z,h) ((z)*icos(h)-(x)*isin(h))
/* RUNTIME_ORIGINAL_FUNCTIONS */
static void orientation_defaults(void) {
 reset();cursor=0;pcount=1000;clock_active=0;
 game_info.ball_carrier=-1;game_info.los=30;game_info.play_time=123;
 ball_obj.type=LB_KICKOFF;ball_obj.scale=2.0f;ball_obj.odata.y=50.0f;
 ball_obj.odata.fwd=137;ball_obj.odata.fwd2=251;
 ball_obj.pitch=1001;ball_obj.pitch_rate=40;ball_obj.phi=44;
 ball_obj.tx=222.0f;ball_obj.tz=111.0f;
}
static void orientation_row(const char *family,int id,int step) {
 printf("H\t%d\t%d\t%s",id,step,family);
 int input[]={game_info.off_side,game_info.play_type,game_info.game_mode,
  game_info.ball_carrier,ball_obj.type,ball_obj.flags,ball_obj.odata.fwd,
  ball_obj.odata.fwd2,ball_obj.pitch,ball_obj.pitch_rate,ball_obj.phi,
  ball_obj.flight_time,(int)game_info.game_flags};
 for(int i=0;i<13;i++)printf("\t%d",input[i]);
 float before[]={ball_obj.odata.x,ball_obj.odata.y,ball_obj.odata.z,
  ball_obj.odata.vx,ball_obj.odata.vy,ball_obj.odata.vz,ball_obj.tx,ball_obj.tz};
 for(int i=0;i<8;i++)printf("\t%u",bits(before[i]));
 if(game_info.ball_carrier==-1 || (ball_obj.flags&BF_PHANTOM)) {
  advance_kernel();orientation_kernel();
 }
 int out[]={ball_obj.odata.fwd,ball_obj.odata.fwd2,ball_obj.pitch,ball_obj.pitch_rate,
  ball_obj.phi,ball_obj.flags,game_info.ball_carrier,ball_obj.flight_time,(int)game_info.game_flags};
 for(int i=0;i<9;i++)printf("\t%d",out[i]);
 float after[]={ball_obj.odata.x,ball_obj.odata.y,ball_obj.odata.z,
  ball_obj.odata.vx,ball_obj.odata.vy,ball_obj.odata.vz,ball_obj.tx,ball_obj.tz};
 for(int i=0;i<8;i++)printf("\t%u",bits(after[i]));
 printf("\t%d\n",cursor);
}
static int orientation_cases(void) {
 int id=0;
 orientation_defaults();
 game_info.game_mode=GM_PLAY_OVER;
 ball_obj.type=LB_FUMBLE;
 ball_obj.flags=BF_WHITE|BF_BOUNCE|BF_BOBBLE|BF_FIRE_PASS|BF_TURBO_PASS|BF_FREEZE|BF_FG_BOUNCE|BF_FG_HITPOST;
 actors[3].jpos[JOINT_RWRIST][0]=13.0f;
 actors[3].jpos[JOINT_RWRIST][1]=9.0f;
 actors[3].jpos[JOINT_RWRIST][2]=-7.0f;
 spin_ball(&actors[3].odata);
 for(int step=0;step<61;step++,pcount++)orientation_row("spin",id,step);
 id++;
 int modes[]={GM_PRE_SNAP,GM_PRE_KICKOFF,GM_IN_PLAY,GM_PLAY_OVER};
 int flags[]={0,BF_SPINING,BF_FREEZE,BF_SPINING|BF_FREEZE};
 int elevations[]={-5,0,250,251,255,256,260,384};
 for(int side=0;side<2;side++)for(int mode=0;mode<4;mode++)for(int play=0;play<5;play++)
  for(int flag=0;flag<4;flag++)for(int elevation=0;elevation<8;elevation++) {
   orientation_defaults();game_info.off_side=side;game_info.game_mode=modes[mode];
   game_info.play_type=play;ball_obj.flags=flags[flag];
   ball_obj.odata.fwd2=elevations[elevation];
   orientation_row("matrix",id++,0);
  }
 float velocities[][3]={{0,3,0},{0,-3,-0.0f},{1,3,0},{-1,-3,0},{0,0,1},
  {0,0,-1},{1,2,1},{1,-2,-1},{-1,2,1},{-1,-2,-1}};
 int moving_flags[]={0,BF_VEL_PITCH,BF_BOBBLE,BF_FREEZE,BF_PHANTOM,BF_SPINING};
 for(int velocity=0;velocity<10;velocity++)for(int kind=0;kind<7;kind++)for(int flag=0;flag<6;flag++) {
  orientation_defaults();game_info.off_side=velocity%2;game_info.game_mode=modes[velocity%4];
  ball_obj.type=kind;ball_obj.flags=moving_flags[flag];
  ball_obj.odata.vx=velocities[velocity][0];ball_obj.odata.vy=velocities[velocity][1];
  ball_obj.odata.vz=velocities[velocity][2];
  ball_obj.pitch=velocity%2?-1080:1001;ball_obj.pitch_rate=velocity%2?-45:40;
  orientation_row("motion",id++,0);
 }
 int held_flags[]={0,BF_PHANTOM,BF_SPINING,BF_SPINING|BF_PHANTOM};
 for(int held=0;held<2;held++)for(int flag=0;flag<4;flag++) {
  orientation_defaults();game_info.game_mode=GM_IN_PLAY;
  game_info.ball_carrier=held?3:-1;ball_obj.flags=held_flags[flag];ball_obj.odata.vx=2.0f;
  orientation_row("held",id++,0);
 }
 return 0;
}
int main(int argc,char **argv) {
 if(argc==2 && strcmp(argv[1],"orientation")==0)return orientation_cases();
 for(int n=0;n<24000;n++) {
  reset();draw_tape(n);
  int side=(n/2)%2,team=n%2,id=team*7+(int)(next()%7);
  fbplyr_data *p=actors+id;
  game_info.off_side=side;game_info.ball_carrier=-1;game_info.game_mode=GM_IN_PLAY;
  game_info.down=(n/64)%6-1;game_info.los=(int)(next()%100);
  game_info.game_flags=(unsigned)(next()&0x7ffffu);p_status=(n/4)%4;
  game_info.team_fire[0]=(n/16)%2;game_info.team_fire[1]=(n/32)%2;
  scores[0]=(int)(next()%35);scores[1]=(int)(next()%35);
  plays[side].flags=(next()%8==0)?PLF_FAKE_FG:0;
  p->hcap=n%12;p->no_catch_time=(int)(next()%5==0);
  p->odata.x=real(-100,201);p->odata.y=real(0,3);p->odata.z=real(-50,101);p->odata.ay=real(10,13);
  ball_obj.odata.x=p->odata.x+real(-7,15);ball_obj.odata.y=p->odata.y+p->odata.ay+real(-7,15);
  ball_obj.odata.z=p->odata.z+real(-7,15);
  for(int j=0;j<2;j++){int k=j?JOINT_RWRIST:JOINT_LWRIST;
   p->jpos[k][0]=p->odata.x+(j?8.0f:-8.0f);p->jpos[k][1]=p->odata.y+p->odata.ay+4.0f;p->jpos[k][2]=p->odata.z+4.0f;}
  int timed[]={0,4,10,11,15,16,29,30,31,45,60};ball_obj.flight_time=timed[next()%11];
  ball_obj.total_time=ball_obj.flight_time+(int)(next()%20)-5;
  ball_obj.who_threw=(next()%3==0)?id:(int)(next()%14);
  ball_obj.int_receiver=(next()%3==0)?id:(int)(next()%14);
  ball_obj.type=(int)(next()%7);ball_obj.flags=(int)(next()&0x3fffu);
  unsigned modes[]={0,MODE_REACTING,MODE_REACTING|MODE_NOCATCH,MODE_PUPPET,MODE_PUPPETEER};
  p->odata.adata[0].animode=modes[next()%5];pup_nointercept=(int)(next()%4);
  if(n%9==0) {
   p->odata.x=0;p->odata.y=0;p->odata.z=0;p->odata.ay=0;
   for(int j=0;j<15;j++)for(int k=0;k<3;k++)p->jpos[j][k]=100.0f;
   ball_obj.odata.x=n%27==9?14.0f:nextafterf(14.0f,n%27==0?0.0f:100.0f);ball_obj.odata.y=0;ball_obj.odata.z=0;
  }
  if(n%9==1) {
   p->odata.x=100.0f;p->odata.y=100.0f;p->odata.z=100.0f;p->odata.ay=0;
   ball_obj.odata.x=0.0f;ball_obj.odata.y=0.0f;ball_obj.odata.z=0.0f;
   for(int j=0;j<15;j++)for(int k=0;k<3;k++)p->jpos[j][k]=100.0f;
   p->jpos[JOINT_LWRIST][0]=n%27==10?5.0f:nextafterf(5.0f,n%27==1?0.0f:100.0f);
   p->jpos[JOINT_LWRIST][1]=0.0f;p->jpos[JOINT_LWRIST][2]=0.0f;
  }
  printf("C\t%d\t%d\t%d\t%d\t%d\t%d\t%d\t%d\t%d\t%d\t%d",n,ball_obj.type,side,team,id,
   ball_obj.who_threw,ball_obj.int_receiver,ball_obj.flight_time,ball_obj.total_time,p->no_catch_time,p->hcap);
  float f[]={p->odata.x,p->odata.y,p->odata.z,p->odata.ay,ball_obj.odata.x,ball_obj.odata.y,ball_obj.odata.z,
   p->jpos[JOINT_LWRIST][0],p->jpos[JOINT_LWRIST][1],p->jpos[JOINT_LWRIST][2],
   p->jpos[JOINT_RWRIST][0],p->jpos[JOINT_RWRIST][1],p->jpos[JOINT_RWRIST][2]};
  for(int j=0;j<13;j++)printf("\t%u",bits(f[j]));
  printf("\t%u\t%u\t%d\t%d\t%d\t%d\t%d\t%d\t%d\t%d\t%d",p->odata.adata[0].animode,game_info.game_flags,
   ball_obj.flags,game_info.down,game_info.los,scores[0],scores[1],p_status,
   game_info.team_fire[0]|(game_info.team_fire[1]<<1),pup_nointercept,plays[side].flags);
  print_tape();standard_catch((obj_3d*)p);
  int result=(ball_obj.type==LB_PUNT && game_info.game_mode==GM_PLAY_OVER && oracle_result==2)?6:oracle_result;
  printf("\t%d",result);print_bounds();putchar('\n');
 }
 for(int n=0;n<4096;n++) {
  reset();draw_tape(n);fbplyr_data *p=actors+(n%14);
  ball_obj.odata.x=real(-200,401);ball_obj.odata.y=real(0,201);ball_obj.odata.z=real(-180,361);
  ball_obj.odata.vx=real(-75,151)*0.1f;ball_obj.odata.vy=real(-50,101)*0.1f;ball_obj.odata.vz=real(-75,151)*0.1f;
  ball_obj.flags=(int)(next()&0x3fffu);ball_obj.tx=222.0f;ball_obj.tz=111.0f;
  if(n%32==0)ball_obj.odata.vx=ball_obj.odata.vy=ball_obj.odata.vz=0;
  if(n%32==1){ball_obj.odata.vx=0.0005f;ball_obj.odata.vy=ball_obj.odata.vz=0;}
  if(n%32==2){ball_obj.odata.vx=0.001f;ball_obj.odata.vy=ball_obj.odata.vz=0;}
  printf("D\t%d\t%d",n,p->plyrnum);
  float f[]={ball_obj.odata.x,ball_obj.odata.y,ball_obj.odata.z,ball_obj.odata.vx,ball_obj.odata.vy,ball_obj.odata.vz};
  for(int j=0;j<6;j++)printf("\t%u",bits(f[j]));
  printf("\t%d",ball_obj.flags);print_tape();deflect_pass((obj_3d*)p);
  float o[]={ball_obj.odata.vx,ball_obj.odata.vy,ball_obj.odata.vz,ball_obj.tx,ball_obj.tz};
  for(int j=0;j<5;j++)printf("\t%u",bits(o[j]));
  printf("\t%d\t%d",ball_obj.flags,tdata_blocks[p->team].deflections);print_bounds();putchar('\n');
 }
 for(int n=0;n<10000;n++) {
  reset();draw_tape(n);
  int vs=(n/2)%2,as=1-vs,vi=vs*7+3,ai=as*7+4;
  fbplyr_data *v=actors+vi,*a=actors+ai;
  game_info.off_side=vs;game_info.ball_carrier=vi;game_info.game_mode=GM_IN_PLAY;
  v->hcap=n%12;a->hcap=(n/12)%12;
  v->station=(n/144)%2?vs:-1;a->station=(n/288)%2?as:-1;
  p_status=(n/576)%4;if(v->station!=-1)p_status|=1<<vs;if(a->station!=-1)p_status|=1<<as;
  int held[]={0,1,17,18,19,50,100};ball_obj.catch_pcount=1000-held[n%7];
  game_info.game_quarter=n%7;game_info.los=n%100;
  scores[vs]=n%21;scores[as]=(n/21)%21;
  game_info.game_flags=next()&(GF_OPENING_KICK|GF_AIR_CATCH|GF_ONSIDE_KICK|GF_PLAY_ENDING);
  ball_obj.flags=(int)(next()&0x3fffu);ball_obj.type=n%7;
  game_info.team_fire[0]=(n/4)%2;game_info.team_fire[1]=(n/8)%2;
  victim_y=(n/3)%2?1.0f:0.0f;
  v->odata.x=real(-350,701);v->odata.y=victim_y;v->odata.z=real(-150,301);
  v->odata.plyrmode=n%2?MODE_QB:MODE_WPN;
  v->jpos[JOINT_LWRIST][0]=v->odata.x+2.5f;
  v->jpos[JOINT_LWRIST][1]=real(1,35);v->jpos[JOINT_LWRIST][2]=v->odata.z+1.25f;
  mvx=real(-30,61)*0.1f;mvz=real(-30,61)*0.1f;
  pup_noassist=(n/200)%2;pup_nofumbles=(n/400)%2;pup_superdrones=(n/800)%2;
  drones_lose=(n/1600)%2;drones_winning=(n/3200)%2;abusing_spin=(n/5)%2;
  game_info.spin_cnt[vs]=n%6;game_info.fumbles_on_play=n%4;
  tdata_blocks[vs].off_plays=n%6;tdata_blocks[vs].lost_fumbles=n%5;
  tdata_blocks[as].lost_fumbles=(n/5)%5;tdata_blocks[as].interceptions=(n/25)%5;
  tdata_blocks[vs].last_receiver=(n/100)%2?vi:-1;
  ball_obj.flight_time=9;ball_obj.total_time=55;ball_obj.who_threw=13;ball_obj.int_receiver=12;
  ball_obj.tx=222.0f;ball_obj.tz=111.0f;
  int bb[]={70,60,80,65,50},fb[]={430,560,430,560,290};
  int input[]={ball_obj.type,vs,vi,ai,v->hcap,a->hcap,v->station,a->station,
   held[n%7],game_info.game_quarter,game_info.los,scores[0],scores[1],p_status,
   (int)game_info.game_flags,ball_obj.flags,game_info.team_fire[0]|(game_info.team_fire[1]<<1),
   bb[n%5],fb[n%5],pup_noassist,pup_nofumbles,pup_superdrones,drones_lose,drones_winning,
   game_info.spin_cnt[vs],abusing_spin,tdata_blocks[vs].off_plays,tdata_blocks[vs].lost_fumbles,
   tdata_blocks[as].lost_fumbles,tdata_blocks[as].interceptions,tdata_blocks[vs].last_receiver,
   v->odata.plyrmode,game_info.fumbles_on_play};
  printf("F\t%d",n);for(int j=0;j<33;j++)printf("\t%d",input[j]);
  float fs[]={v->odata.x,v->odata.y,v->odata.z,v->jpos[JOINT_LWRIST][0],
   v->jpos[JOINT_LWRIST][1],v->jpos[JOINT_LWRIST][2],mvx,mvz};
  for(int j=0;j<8;j++)printf("\t%u",bits(fs[j]));
  print_tape();
  int lost=fumble_or_bobble(v,a,bb[n%5],fb[n%5]);
  int result=!lost?0:tdata_blocks[vs].bobbles?2:1;
  printf("\t%d",result);
  float out[]={ball_obj.odata.x,ball_obj.odata.y,ball_obj.odata.z,
   ball_obj.odata.vx,ball_obj.odata.vy,ball_obj.odata.vz,ball_obj.tx,ball_obj.tz,game_info.fumble_x};
  for(int j=0;j<9;j++)printf("\t%u",bits(out[j]));
  printf("\t%d\t%d\t%d\t%d\t%d\t%u\t%d\t%d\t%d",ball_obj.type,ball_obj.flags,
   ball_obj.flight_time,ball_obj.who_threw,ball_obj.int_receiver,game_info.game_flags,
   tdata_blocks[vs].fumbles,tdata_blocks[vs].bobbles,game_info.fumbles_on_play);
  print_bounds();putchar('\n');
 }
 for(int n=0;n<8000;n++) {
  reset();draw_tape(n);clock_active=0;
  game_info.off_side=n%2;game_info.play_type=(n/2)%5;game_info.los=n%100;
  game_info.game_mode=n%5==0?GM_PLAY_OVER:n%5==1?0:GM_IN_PLAY;
  game_info.game_flags=next()&(GF_ONSIDE_KICK|GF_PLAY_ENDING|GF_FWD_PASS|GF_BALL_CROSS_LOS|GF_HIKING_BALL);
  game_info.ball_carrier=-1;game_info.play_time=123;
  ball_obj.type=n%7;ball_obj.flags=(int)(next()&(BF_WHITE|BF_VEL_PITCH|BF_BOUNCE|BF_FG_BOUNCE|BF_FG_HITPOST));
  if(n%41==0)ball_obj.flags|=BF_FREEZE;
  if(n%43==0)ball_obj.flags|=BF_SPINING;
  if(n%47==0){ball_obj.flags|=BF_PHANTOM;game_info.ball_carrier=3;}
  ball_obj.odata.x=real(-470,941);ball_obj.odata.y=real(0,141);ball_obj.odata.z=real(-185,371);
  ball_obj.odata.vx=real(-75,151)*0.1f;ball_obj.odata.vy=real(-70,141)*0.1f;ball_obj.odata.vz=real(-30,61)*0.1f;
  if(n%3==0)ball_obj.odata.y=1.6f;
  if(n%3==1){ball_obj.odata.x=n%2?459.5f:-459.5f;ball_obj.odata.z=n%4<2?22.0f:0.0f;ball_obj.odata.y=27.0f;}
  ball_obj.scale=2.0f;ball_obj.bounces=n%5;ball_obj.bounce_time=1000-n%20;ball_obj.flight_time=n%60;
  ball_obj.tx=222.0f;ball_obj.tz=111.0f;
  printf("A\t%d",n);
  int input[]={game_info.off_side,game_info.play_type,game_info.los,game_info.game_mode,
   (int)game_info.game_flags,game_info.ball_carrier,ball_obj.type,ball_obj.flags,
   ball_obj.bounces,ball_obj.bounce_time,ball_obj.flight_time};
  for(int j=0;j<11;j++)printf("\t%d",input[j]);
  float fs[]={ball_obj.odata.x,ball_obj.odata.y,ball_obj.odata.z,ball_obj.odata.vx,ball_obj.odata.vy,ball_obj.odata.vz};
  for(int j=0;j<6;j++)printf("\t%u",bits(fs[j]));
  print_tape();advance_kernel();
  float out[]={ball_obj.odata.x,ball_obj.odata.y,ball_obj.odata.z,ball_obj.odata.vx,ball_obj.odata.vy,ball_obj.odata.vz,ball_obj.tx,ball_obj.tz};
  for(int j=0;j<8;j++)printf("\t%u",bits(out[j]));
  printf("\t%d\t%u\t%d\t%d\t%d\t%d\t%d\t%d\t%d",ball_obj.flags,game_info.game_flags,ball_obj.flight_time,
   ball_obj.bounces,ball_obj.bounce_time,game_info.game_mode,game_info.play_end_cause,clock_active,game_info.play_time);
  print_bounds();putchar('\n');
 }
 return 0;
}
