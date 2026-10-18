/* Authored host projection. Exact original C bodies/tables are inserted at runtime.
   Animation execution and audio are explicit external boundaries in this oracle,
   not a replacement for the separate actual-kernel/pose/VM campaigns. */
#include <stdio.h>
#include <stdint.h>
#include <string.h>
#include <stdlib.h>
#include <math.h>
#include <setjmp.h>
#include <stdarg.h>
#define TRUE 1
#define FALSE 0
#define NUM_PLAYERS 14
#define GREATER(a,b) ((a)>(b)?(a):(b))
#define LESSER(a,b) ((a)<(b)?(a):(b))
#define CLAMP(x,lo,hi) ((x<lo)?lo:((x>hi)?hi:x))
#define PASS1_DIST 24.0f
#define BODY_RADIUS 5.0f
#define fsqrt sqrtf
#define FIELDHZ 390.0f
#define FIELDX(x) ((int)((FIELDHZ+(game_info.off_side?-(x):(x)))/7.8f))
#define TEAM_MASK(side) (1<<(side))
#define ISHUMAN(p) ((p)->station!=-1)
#define ISDRONE(p) ((p)->station==-1)
#define ISHUMANTEAM(p) ((human_teams&TEAM_MASK((p)->team))!=0)
#define ISDRONETEAM(p) (!ISHUMANTEAM(p))
#define PLYRH(p) ((p)->hcap)
#define OFFH (player_blocks[7*game_info.off_side].hcap)
#define CONTROL_TIME 18
#define TDC_NOCONTROL 1
#define TDC_NOAIRCATCH 2
#define PEC_CAR_TOUCHDOWN 1
#define AF_INACTIVE 1
#define PRF_COLLIS 0
#define MSG_FUMBLE_PID 0
#define LOOSE_HELMET_PID 0
#define OID_LOOSE_HELMET 0
#define VOLUME1 1
#define VOLUME3 3
#define VOLUME4 4
#define ANCR_VOL2 2
#define PRIORITY_LVL1 15
#define PRIORITY_LVL3 5
#define PRIORITY_LVL4 1
#define LVL3 5
#define LVL4 1
#define P_HARD_HIT2_SND 96
#define P_HARD_HIT3_SND 94
#define BOWLING_PIN_SND 1
#define OUTTA_MY_FACE_SP 1
/* ORIGINAL_CONSTANTS */
typedef struct { unsigned animode; int seq_index; char *pscrhdr; float *pxlate; int iframe; } anidata;
typedef struct obj_3d {
 float x,y,z,vx,vy,vz,ay,ax,az,ascale; unsigned flags; int plyrmode,fwd,tgt_fwd;
 anidata adata[2]; struct obj_3d *pobj_hit;
} obj_3d;
typedef struct fbplyr_data {
 obj_3d odata; int plyrnum,team,station,hcap,job,attack_mode,attack_time,acj1,acj2;
 float acx1,acx2,acr,jpos[15][3];
 struct fbplyr_data *puppet_link; int puppet_rel_time,block_count;
} fbplyr_data;
typedef struct { float x,y,z,w; } vec3d;
typedef struct { int unused; } debris_config_t;
typedef struct { unsigned flags; } play_t;
static play_t plays[2];
static struct {
 int game_mode,off_side,last_pos,ball_carrier,play_type,play_time,play_end_cause,game_quarter,down;
 int spin_cnt[2],team_fire[2],fumbles_on_play,team_head[4];
 unsigned game_flags; float play_end_pos,play_end_z; play_t *team_play[2];
} game_info;
static struct { int type,flags,int_receiver,catch_pcount; } ball_obj;
static struct { int tackles,bobbles,fumbles,lost_fumbles,interceptions,off_plays,last_receiver; } tdata_blocks[2];
static fbplyr_data player_blocks[14];
static struct { int ai_flags; } arrowinfo[4];
static struct { obj_3d *pmodeobj; } cambot;
static float distxz[14][14],victim_y,mvx,mvz;
static int p2p_angles[14][14],victim_seq,abusing_spin,qb_evaded;
static int input_angles[14][14];
static int pcount,human_teams,pup_violence,pup_block,pup_blitzers,pup_noassist,pup_nofumbles,pup_superdrones;
static int drones_lose,drones_winning,plowhd_snd,scores[2],csi_fs_endplay[1],p_status,force_fumble,force_bobble;
static char *plyr_bnk_str="players",*ancr_swaped_bnk_str="unresolved-original";
static int tape[64],bounds[64],cursor,event_count;
static char events[256][128];
static jmp_buf wake_end;
static int sleeps;

static void event(const char *kind,int a,int d,const char *text) {
 if(event_count>=256)exit(95);
 snprintf(events[event_count++],128,"%s:%d:%d:%s",kind,a,d,text?text:"-");
}
static int randrng(int n) {
 if(cursor>=64)exit(96);
 bounds[cursor]=n;
 return n?tape[cursor++]%n:(cursor++,0);
}
static int compute_score(int side) { return scores[side]; }
static void snd_scall_bank(const char *bank,...) {(void)bank;}
static void crowd_tackle_snds(void) {}
static void crowd_cheer_snds(void) {}
static void late_hit_speech(void) {}
static void qb_duck_speech(void) {}
static void monkey_on_back_sp(void) {}
static void hit_intended_receiver_sp(void) {}
static void head_plow_speech(void) {}
static void fumble_ball_sounds(void) {}
static void display_fumble_proc(void) {}
static void loose_helmet_proc(int *args) {(void)args;}
static void qcreate(const char *name,...) {(void)name;}
static void killall(int id,unsigned mask) {(void)id;(void)mask;}
static void delete_multiple_objects(int id,unsigned mask) {(void)id;(void)mask;}
static void pftimer_start(int id) {(void)id;}
static void pftimer_end(int id) {(void)id;}
static void sleep(int count) {(void)count;if(++sleeps==2)longjmp(wake_end,1);}
static int td_check(int flags) {(void)flags;return 0;}
static int select_csi_item(int *table) {(void)table;randrng(8);return 1;}
static void fumble_ball(fbplyr_data *p,float x,float z) {(void)x;(void)z;game_info.ball_carrier=-1;event("fumble",p->plyrnum,-1,0);}
static void bobble_pass(fbplyr_data *p,float x,float y,float z) {(void)x;(void)y;(void)z;game_info.ball_carrier=-1;event("bobble",p->plyrnum,-1,0);}
static void change_anim(obj_3d *,char *);
static int ptangle(float *a,float *d) {
 int i=(int)(((char*)a-(char*)player_blocks)/sizeof(fbplyr_data));
 int j=(int)(((char*)d-(char*)player_blocks)/sizeof(fbplyr_data));
 return input_angles[i][j];
}
static void do_collision(fbplyr_data *,fbplyr_data *,int);
static void extend_line(float *,float *,float,float,float *,float *);
static float line_int_check(float *,float *,float *,float *,float);
static float point2line_d(float *,float *,float *);
static int evade_vs_tackle(fbplyr_data *,fbplyr_data *,int,int);
static int block_vs_rush(fbplyr_data *,fbplyr_data *,int,int);
static int fumble_or_bobble(fbplyr_data *,fbplyr_data *,int,int);
/* ORIGINAL_SCRIPTS */
/* ORIGINAL_TABLES */
/* ORIGINAL_FUNCTIONS */

static void change_anim(obj_3d *object,char *script) {
 fbplyr_data *p=(fbplyr_data *)object;
 event("anim",p->plyrnum,p->odata.pobj_hit?((fbplyr_data*)p->odata.pobj_hit)->plyrnum:-1,script);
 p->odata.adata[0].animode=(p->odata.adata[0].animode&0xffff0000u)|script_mode(script);
 p->odata.adata[1].animode&=0xffff0000u;
 p->odata.adata[0].pscrhdr=script;
}
static float as_float(unsigned u) { union {unsigned u;float f;}v;v.u=u;return v.f; }
static unsigned bits(float f) {union {unsigned u;float f;}v;v.f=f;return v.u;}
static int integer(void) {int n;if(scanf("%d",&n)!=1)exit(90);return n;}
static unsigned word(void) {unsigned n;if(scanf("%u",&n)!=1)exit(91);return n;}
static float scalar(void) {return as_float(word());}
int main(void) {
 char kind;
 while(scanf(" %c",&kind)==1) {
  int id=integer();
  if(kind=='L') {
   float v[13];for(int i=0;i<13;i++)v[i]=scalar();
   printf("L\t%d\t%d\n",id,(int)line_int_check(v,v+3,v+6,v+9,v[12]));continue;
  }
  if(kind=='P') {
   obj_3d a={0},v={0};float root[3];
   a.x=scalar();a.y=scalar();a.z=scalar();a.ax=scalar();a.ay=scalar();a.az=scalar();
   a.vx=scalar();a.vy=scalar();a.vz=scalar();a.fwd=integer();a.tgt_fwd=integer();
   v.ascale=scalar();for(int i=0;i<3;i++)root[i]=scalar();
   v.adata[0].pxlate=root;v.adata[0].iframe=0;
   set_puppet_pos(&a,&v);
   printf("P\t%d\t%u\t%u\t%u\t%u\t%u\t%u\t%u\t%u\t%u\t%d\t%d\n",id,
    bits(v.x),bits(v.y),bits(v.z),bits(v.ax),bits(v.ay),bits(v.az),
    bits(v.vx),bits(v.vy),bits(v.vz),v.fwd,v.tgt_fwd);continue;
  }
  if(kind!='D')return 92;
  memset(player_blocks,0,sizeof(player_blocks));memset(&game_info,0,sizeof(game_info));memset(tdata_blocks,0,sizeof(tdata_blocks));
  event_count=cursor=sleeps=0;abusing_spin=qb_evaded=plowhd_snd=0;
  game_info.game_mode=integer();game_info.off_side=integer();game_info.last_pos=integer();game_info.ball_carrier=integer();
  game_info.game_flags=word();game_info.play_type=integer();game_info.play_time=integer();pcount=integer();
  human_teams=integer();pup_violence=integer();pup_block=integer();pup_blitzers=integer();
  p_status=human_teams;force_fumble=force_bobble=0;
  pup_noassist=0;pup_nofumbles=1;pup_superdrones=drones_lose=drones_winning=0;scores[0]=scores[1]=0;
  game_info.game_quarter=0;game_info.down=1;
  game_info.team_fire[0]=integer();game_info.team_fire[1]=integer();
  game_info.spin_cnt[0]=integer();game_info.spin_cnt[1]=integer();qb_evaded=integer();
  for(int i=0;i<2;i++){tdata_blocks[i].last_receiver=integer();tdata_blocks[i].off_plays=10;}
  for(int i=0;i<4;i++)game_info.team_head[i]=-1;
  game_info.team_play[0]=plays;game_info.team_play[1]=plays+1;plays[0].flags=word();plays[1].flags=word();
  ball_obj.type=integer();ball_obj.flags=integer();ball_obj.catch_pcount=pcount-100;ball_obj.int_receiver=-1;
  for(int i=0;i<64;i++)tape[i]=integer();
  int links[14];
  for(int i=0;i<14;i++) {
   fbplyr_data *p=player_blocks+i;p->plyrnum=i;p->team=i/7;
   p->station=integer();p->hcap=integer();p->odata.plyrmode=integer();p->job=integer();
   p->odata.flags=word();p->odata.adata[0].animode=word();p->odata.adata[1].animode=word();
   p->odata.adata[0].seq_index=integer();p->odata.fwd=integer();
   p->odata.x=scalar();p->odata.y=scalar();p->odata.z=scalar();p->odata.ay=scalar();
   p->odata.vx=scalar();p->odata.vy=scalar();p->odata.vz=scalar();
   p->attack_mode=integer();p->attack_time=integer();p->acj1=integer();p->acj2=integer();
   p->acx1=scalar();p->acx2=scalar();p->acr=scalar();p->block_count=integer();links[i]=integer();
   for(int j=0;j<15;j++)for(int k=0;k<3;k++)p->jpos[j][k]=scalar();
  }
  for(int i=0;i<14;i++)player_blocks[i].puppet_link=links[i]<0?0:player_blocks+links[i];
  for(int i=0;i<14;i++)for(int j=0;j<14;j++)input_angles[i][j]=integer();
  if(!setjmp(wake_end))player_detect_proc(0);
  printf("D\t%d\t%d",id,event_count);
  for(int i=0;i<event_count;i++)printf("\t%s",events[i]);
  printf("\t%d",cursor);for(int i=0;i<cursor;i++)printf("\t%d",bounds[i]);
  printf("\t%d\t%d\t%u\t%d",qb_evaded,abusing_spin,game_info.game_flags,game_info.ball_carrier);
  for(int i=0;i<14;i++) {
   fbplyr_data *p=player_blocks+i;
   printf("\t%d\t%d\t%d\t%u\t%u\t%d\t%d\t%u\t%u\t%u\t%u\t%u\t%u",
    p->attack_mode,p->attack_time,p->block_count,p->odata.adata[0].animode,p->odata.flags,
    p->odata.pobj_hit?((fbplyr_data*)p->odata.pobj_hit)->plyrnum:-1,
    p->puppet_link?p->puppet_link->plyrnum:-1,bits(p->odata.x),bits(p->odata.y),bits(p->odata.z),
    bits(p->odata.vx),bits(p->odata.vy),bits(p->odata.vz));
  }
  putchar('\n');
 }
 return 0;
}
