/* Synthetic external-state/service boundary for unchanged extracted C functions.
 * These projected structs are not the target arcade ABI or player physics. */
#include <stdio.h>
#include <stdlib.h>
#include <stdint.h>
#include <string.h>
#include <math.h>
#include <float.h>
#include <limits.h>
#if FLT_RADIX != 2 || FLT_MANT_DIG != 24 || INT_MAX != 2147483647
#error Unsupported host numeric profile
#endif
typedef char require_binary32_float[(sizeof(float) == 4) ? 1 : -1];
#define TRUE 1
#define FALSE 0
#define NUM_PERTEAM 7
#define NUM_PLAYERS 14
#define tsec 57
#define P_A 1
#define P_B 2
#define P_C 4
#define P_D 8
#define P4_D 8
#define FIELDHZ 390.0f
#define FIELDHB 468.0f
#define FIELDW 182.85f
#define FOCFAC (FIELDHZ/300.0f)
#define YD2UN (FIELDHZ/50.0f)
#define YD2 (YD2UN*2.0f)
#define YD3 (YD2UN*3.0f)
#define YD5 (YD2UN*5.0f)
#define YD8 (YD2UN*8.0f)
#define YD10 (YD2UN*10.0f)
#define YD12 (YD2UN*12.0f)
#define YD15 (YD2UN*15.0f)
#define WORLDX(yd) (((FOCFAC*6.0f)*(float)(game_info.off_side ? (100-(yd)) : (yd)))-FIELDHZ)
#define FIELDX(x) ((int)((FIELDHZ+(game_info.off_side ? (-1.0f*(x)) : (x)))/(FOCFAC*6.0f)))
#define FRELX(x,z,t) ((z)*isin(t)+(x)*icos(t))
#define FRELZ(x,z,t) ((z)*icos(t)-(x)*isin(t))
#define RD2GR(x) (x)
#define fsqrt sqrtf
#define GREATER(a,b) ((a)>(b)?(a):(b))
#define LESSER(a,b) ((a)<(b)?(a):(b))
#define ABS abs
#define CAP(a,b,c) LESSER(GREATER((a),(b)),(c))
#define GRAVITY -0.08f
#define CONTROL_TIME 18
#define OFFENSE_FIRECNT 3
#define QB_HURDLE_PASS 1
#define IT_LOWEST 0
#define IT_HIGHEST 1
#define BLOCK_DIST (7.0f*YD2UN)
#define BLOCK_DIST2 (11.0f*YD2UN)
#define TEAM_MASK(side) (1<<(side))
#define ISHUMAN(p) ((p)->station!=-1)
#define ISHUMANTEAM(p) (p_status&TEAM_MASK((p)->team))
#define ISDRONETEAM(p) (!(p_status&TEAM_MASK((p)->team)))
#define PLYRH(p) (game_info.team_handicap[(p)->team])
#define DEFH (game_info.team_handicap[!game_info.off_side])
#define JERSEYNUM(i) (i)
#define GM_PLAY_OVER 0
#define GM_PRE_SNAP 1
#define GM_PRE_KICKOFF 2
#define GM_IN_PLAY 3
#define PT_KICKOFF 1
#define LB_PASS 0
#define LB_FUMBLE 1
#define LB_PUNT 2
#define LB_KICKOFF 3
#define LB_BOBBLE 5
#define BF_BOUNCE 1
#define GF_PLAY_ENDING 0x400
#define GF_BALL_CROSS_LOS 0x4000
#define GF_HIKING_BALL 0x8000
#define GF_ONSIDE_KICK 0x10000
#define SEQ_T_CRZDIV 111
#define SEQ_T_SPRHED 112
typedef struct { int animode,seq_index; } anim_data;
typedef struct { float x,y,z,vx,vy,vz,ax,az,ascale,default_ascale; int flags,plyrmode,fwd,tgt_fwd; anim_data adata[2]; } obj_3d;
typedef struct { int number; } static_player;
typedef struct { obj_3d odata; int plyrnum,team,station,job,stick_cur,but_cur,but_new,bptarget;
 int drone_throw_target,throw_target,non_buta_time,non_turbo_time,fake_zone_time,drone_face;
 int attack_time,attack_mode,celebrate_time,block_count,no_catch_time,override_time,position;float speed;
 void *puppet_link;char *drone_script; static_player *static_data; } fbplyr_data;
typedef struct { float x,z; char *seq; int mode,control; } fline_t;
typedef struct { fline_t *formation; int flags; int *routes[7]; } play_t;
static_player statics[14];
fbplyr_data player_blocks[14];
struct { play_t *team_play[2]; int off_side,off_init,los,first_down,down,play_type,game_mode,game_flags,
 ball_carrier,game_quarter,play_pcount,team_handicap[2],team_fire[2],hurdle_cnt[2],spin_cnt[2],captain_player[2],off_flip;
 float line_pos; } game_info;
struct { obj_3d odata; int type,flags,int_receiver,who_threw,flight_time,total_time; float tx,tz; } ball_obj;
struct { float theta; } cambot;
struct { int last_receiver,consecutive_comps; } tdata_blocks[2];
int p_status,pcount,did_catch,freeze,four_plr_ver,audible_tap_action[2],drones_lose,drones_winning;
int almost_fire_pflash,fire_pflash,db_pflash,qb_evaded;
char *lineup_scripts[14];
void clear_random_control_flags(void){}
void change_anim(obj_3d *p,char *s){for(int i=0;i<14;i++)if(p==&player_blocks[i].odata)lineup_scripts[i]=s;}
int p2p_angles[14][14]; float distxz[14][14];
int rush_times[7],blocked[7],int_times[7][7],weighted_int_times[7][7],urgent[7],rushers[7],blockers[7],rcnt;
int weapons[7],cover_times[7][7],open_times[7],defenders[7],coverage[7],likely_yards[7],wcnt,dcnt;
float qb_dropback; int roll_dir,sack_time,best_open,motion_dir,gain_sought,qb_flags,stick_drone;
static int raw_tape[8192],raw_length,raw_cursor;
static int rng_count,rng_bounds[4096],rng_values[4096],rng_raw[4096];
int randrng(int val) {
 if(rng_count>=4096) {fprintf(stderr,"oracle wrapper trace exhausted\n");abort();}
 int raw=-1,value=0;
 if(val) {
  if(raw_cursor>=raw_length) {fprintf(stderr,"explicit raw input tape exhausted\n");abort();}
  raw=raw_tape[raw_cursor++];value=raw%val;
 }
 rng_bounds[rng_count]=val;rng_values[rng_count]=value;rng_raw[rng_count++]=raw;
 return value;
}
