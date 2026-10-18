#define _XOPEN_SOURCE 700
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <stdint.h>
#include <math.h>
#include <ucontext.h>

/* ORIGINAL_CONSTANTS */

typedef struct { float w,h,ax,ay,s_start,s_end; } image_info_t;
typedef struct {
    image_info_t *ii;
    float x,y,w_scale,h_scale;
    int id,mode,hidden,deleted;
    struct { unsigned char color_combiner_function; unsigned int constant_color; } state;
} sprite_info_t;
struct process_node { void (*process_exit)(struct process_node *,int); };
typedef struct { float x,y,z,vx,vy,vz; unsigned int flags; int plyrmode; } obj_3d;
typedef struct { obj_3d odata; int team,plyrnum; float jpos[15][3]; } fbplyr_data;
static struct { float x,y,z,xform[12]; } cambot;
typedef struct { int flags; } play_t;
static play_t plays[2];
static struct {
    play_t *team_play[2];
    int off_side,team[2],game_state,game_mode,game_flags,auto_hike_cntr,los;
    int game_quarter,game_time,team_fire[2],team_handicap[2];
} game_info;
static struct {
    obj_3d odata;
    float ox,oy,oz,tx,ty,tz;
    int pitch_rate,flags,total_time,flight_time;
} ball_obj;
static struct { int attr[NUM_TM_ATTRIBS]; } teaminfo[2];
static struct process_node process, *cur_proc=&process, *pflasher_kick_meter, *pflasher_punt_meter;
static int pmsg[1],showkmtr_flag,showhide_flag=1,punt_strength,four_plr_ver=1,p_status;
static int pup_noassist,pup_perfect_fg,pup_showfgper,drones_lose,scores[2];
static float kmtr_per;
static sprite_info_t sprites[32], *ball_sprite, *marker_sprite;
static int sprite_count,raw_tape[32],raw_count,draws,bounds[32];
static long clock_tick,next_wake;
static int dead,kind,last_sleep,probe_pos,probe_vel,probe_limit,probe_left,probe_right,probe_xoff;
static float probe_wind,probe_accuracy;
static ucontext_t caller_context,meter_context;
static unsigned char meter_stack[262144];
/* AUTHORED_IMAGE_SEAM */

#define GR_COLORCOMBINE_DECAL_TEXTURE_ADD_CCALPHA 1
#define FLIP_TEX_H 1
#define SCR_PLATE_TID 0
#define OID_FG_METER 1
#define OID_PUNT_METER 2
#define SID_FG_METER 1
#define SID_PUNT_METER 2
#define TRANSPARENCY_ENABLE 1
#define FONTID_BAST18 1
#define LT_CYAN 0
#define HJUST_CENTER 0
#define VJUST_CENTER 0
#define NULL_PROCESS ((struct process_node *)0)
#define snd_scall_bank(...) ((void)0)
#define qcreate(...) NULL_PROCESS
#define kill(...) ((void)0)
#define delete_multiple_strings(...) ((void)0)
#define generate_sprite_verts(...) ((void)0)
#define set_text_transparency_mode(...) ((void)0)
#define set_text_font(...) ((void)0)
#define set_string_id(...) ((void)0)
#define set_text_position(...) ((void)0)
#define set_text_color(...) ((void)0)
#define oprintf(...) 1
#define fsqrt sqrtf

static int randrng(int bound) {
    if(bound<1 || draws>=raw_count) { fputs("unexpected original random call\n",stderr); exit(3); }
    bounds[draws]=bound;
    return raw_tape[draws++]%bound;
}
static int compute_score(int side) { return scores[side]; }
static sprite_info_t *beginobj(image_info_t *image,float x,float y,float z,int texture) {
    (void)z;(void)texture;
    if(sprite_count==32) exit(4);
    sprite_info_t *s=&sprites[sprite_count++];
    s->ii=image;s->x=x;s->y=y;s->w_scale=s->h_scale=1;
    if(image==&k_ball) ball_sprite=s;
    if(image==&k_marker) marker_sprite=s;
    return s;
}
static void hide_sprite(sprite_info_t *s) { s->hidden=1; }
static void unhide_sprite(sprite_info_t *s) { s->hidden=0; }
static void del1c(int id,int mask) {
    (void)mask;
    for(int i=0;i<sprite_count;i++) if(sprites[i].id==id) sprites[i].deleted=1;
}
static void km_probe(int k,int pos,int vel,int limit,float wind,int left,int right,int xoff) {
    (void)k;
    probe_pos=pos;probe_vel=vel;probe_limit=limit;probe_wind=wind;
    probe_left=left;probe_right=right;probe_xoff=xoff;probe_accuracy=kmtr_per;
}
static void source_sleep(int duration) {
    last_sleep=duration;next_wake=clock_tick+duration;
    swapcontext(&meter_context,&caller_context);
}
static void die(int cause) {
    if(process.process_exit) process.process_exit(&process,cause);
    dead=1;next_wake=-1;setcontext(&caller_context);
}
#define sleep source_sleep
static void show_fg_per(int *args) { (void)args; }
/* ORIGINAL_FUNCTIONS */

static char *token;
static int integer(void) {
    token=strtok(NULL," \r\n");
    if(!token) { fputs("missing integer\n",stderr);exit(5); }
    return (int)strtol(token,NULL,10);
}
static float number(void) {
    token=strtok(NULL," \r\n");
    if(!token) { fputs("missing float\n",stderr);exit(5); }
    return strtof(token,NULL);
}
static void trampoline(void) {
    if(kind==1) kick_meter_proc(NULL);else punt_meter_proc(NULL);
    die(0);
}
static void start_meter(void) {
    int side,attribute,fake,wind_raw,sign_raw,punt_raw,auto_raw,auto_sign;
    kind=integer();side=integer();attribute=integer();fake=integer();
    wind_raw=integer();sign_raw=integer();punt_raw=integer();auto_raw=integer();auto_sign=integer();
    memset(&game_info,0,sizeof(game_info));memset(&process,0,sizeof(process));
    memset(sprites,0,sizeof(sprites));sprite_count=0;ball_sprite=marker_sprite=NULL;
    showkmtr_flag=0;kmtr_per=0;punt_strength=0;clock_tick=0;next_wake=1;dead=0;
    probe_pos=probe_vel=probe_limit=probe_left=probe_right=probe_xoff=last_sleep=0;
    probe_wind=probe_accuracy=0;
    game_info.off_side=side;game_info.team_play[0]=plays;game_info.team_play[1]=plays+1;
    game_info.team[side]=side;teaminfo[side].attr[TA_SPTEAMS]=attribute;
    game_info.game_state=GS_GAME_MODE;game_info.game_mode=GM_PRE_SNAP;game_info.auto_hike_cntr=285;
    p_status=TEAM_MASK(side);
    plays[side].flags=kind==1?(fake?PLF_FAKE_FG:PLF_FIELD_GOAL):(fake?PLF_FAKE_PUNT:PLF_PUNT);
    draws=0;raw_count=1;raw_tape[0]=punt_raw;
    if(kind==1) {
        punt_meter_proc(NULL); /* Actual unconditional initial strength, non-punt guard returns. */
        draws=0;raw_count=0;raw_tape[raw_count++]=wind_raw;
        if(wind_raw%11)raw_tape[raw_count++]=sign_raw;
        raw_tape[raw_count++]=auto_raw;raw_tape[raw_count++]=auto_sign;
    }
    getcontext(&meter_context);
    meter_context.uc_stack.ss_sp=meter_stack;meter_context.uc_stack.ss_size=sizeof(meter_stack);
    meter_context.uc_link=&caller_context;
    makecontext(&meter_context,trampoline,0);
    puts("READY");
}
static void tick_meter(void) {
    clock_tick=integer();game_info.game_mode=integer();game_info.game_flags=integer();game_info.auto_hike_cntr=integer();
    if(!dead && clock_tick>=next_wake) swapcontext(&caller_context,&meter_context);
    float base=kind==1?(game_info.off_side?BARX2-20:BARX1+20):BARY+75;
    int offset=ball_sprite?(int)((kind==1?ball_sprite->x:ball_sprite->y)-base):0;
    int marker=marker_sprite?(int)(marker_sprite->x-(game_info.off_side?BARX2-20:BARX1+20)):0;
    printf("%d %d %d %d %d %d %d %d %.9g %.9g %d %d %d %ld %d %d\n",
        probe_pos,probe_vel,probe_limit,offset,marker,probe_left,probe_right,
        ball_sprite && ball_sprite->state.color_combiner_function==GR_COLORCOMBINE_DECAL_TEXTURE_ADD_CCALPHA,
        probe_wind,probe_accuracy,punt_strength,showkmtr_flag,dead,next_wake,draws,last_sleep);
}
static void write_ball(void) {
    printf("%.9g %.9g %.9g %.9g %.9g %.9g %.9g %.9g %d %d %d %d %.9g %.9g %.9g %d",
        ball_obj.odata.x,ball_obj.odata.y,ball_obj.odata.z,ball_obj.odata.vx,ball_obj.odata.vy,ball_obj.odata.vz,
        ball_obj.tx,ball_obj.tz,ball_obj.total_time,ball_obj.flight_time,ball_obj.pitch_rate,ball_obj.flags,
        ball_obj.ox,ball_obj.oy,ball_obj.oz,draws);
    for(int i=0;i<draws;i++)printf(" %d",bounds[i]);
    puts("");
}
static void physics(int field_goal) {
    memset(&ball_obj,0,sizeof(ball_obj));
    fbplyr_data actor;memset(&actor,0,sizeof(actor));
    game_info.off_side=integer();actor.team=game_info.off_side;actor.plyrnum=actor.team*7+(field_goal?6:3);
    game_info.los=integer();draws=0;raw_count=16;
    if(!field_goal) {
        punt_strength=integer();actor.odata.flags=integer()?PF_FLIP:0;
        int joint=actor.odata.flags?JOINT_LANKLE:JOINT_RANKLE;
        for(int i=0;i<3;i++)actor.jpos[joint][i]=number();
        for(int i=0;i<12;i++)cambot.xform[i]=number();
        cambot.x=number();cambot.y=number();cambot.z=number();
    } else {
        game_info.auto_hike_cntr=integer();kmtr_per=number();
        ball_obj.odata.x=number();ball_obj.odata.y=number();ball_obj.odata.z=number();
        ball_obj.tx=number();ball_obj.tz=number();game_info.team_handicap[actor.team]=integer();
        game_info.game_quarter=integer();game_info.game_time=integer();
        scores[actor.team]=integer();scores[!actor.team]=integer();
        game_info.team_fire[actor.team]=integer();pup_perfect_fg=integer();pup_noassist=integer();drones_lose=integer();
        p_status=integer()?TEAM_MASK(actor.team):0;
    }
    for(int i=0;i<16;i++)raw_tape[i]=integer();
    if(field_goal)set_field_goal_ball_vels(&actor.odata);else set_punt_ball_vels(&actor.odata);
    write_ball();
}
int main(void) {
    char line[8192];
    while(fgets(line,sizeof(line),stdin)) {
        char command=*strtok(line," \r\n");
        if(command=='M')start_meter();
        else if(command=='T')tick_meter();
        else if(command=='P')physics(0);
        else if(command=='F')physics(1);
        else if(command=='X') {
            if(process.process_exit)process.process_exit(&process,0);
            dead=1;next_wake=-1;puts("STOPPED");
        } else return 6;
        fflush(stdout);
    }
    return 0;
}
