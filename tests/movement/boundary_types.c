/* Only fields touched by the extracted functions. No Unity or substitute gameplay. */
typedef struct { int flags; } header;
typedef struct {
    int animode,seq_index,anicnt,iframe,vframe,vcount,hackval;
    char *pscript,*pscrhdr;
    void *pframe0,*pframe,*pxlate;
    header *pahdr;
    float *pq,*adata;
} anidata;
typedef struct {
    float x,y,z,vx,vy,vz,friction;
    int flags,plyrmode,fwd,tgt_fwd,fwd2,tgt_fwd2,turn_rate,turn_rate2;
    anidata adata[4];
} obj_3d;
typedef struct { float x; } formation;
typedef struct { int flags; formation formation[7]; } play;
typedef struct { int at_spd,at_pwr,at_qb,at_wr,at_off,at_def; } static_player;
typedef struct {
    obj_3d odata;
    int plyrnum,team,station,position,job;
    int stick_cur,stick_old,stick_time,but_cur,but_old,but_new,but_hist[5];
    int bptarget,bp_expire_time,non_turbo_time,non_buta_time,non_butb_time;
    int attack_time,attack_mode,celebrate_time,override_time,no_catch_time,drone_face;
    float speed;
    char *drone_script;
    int throw_target;
    float jpos[15][3];
    static_player *static_data;
} fbplyr_data;
typedef struct { float turbo; } pdata_t;
typedef struct {
    int off_side,game_mode,game_flags,play_type,ball_carrier,los;
    int team_handicap[2],team_fire[2],spin_cnt[2],hurdle_cnt[2],plyr_control[4];
    float line_pos;
    play *team_play[2];
} gameinfo;
typedef struct { const char *name;int mode,seq,zero,has_velocity;float vx,vy,vz; } script_response;
typedef struct { int kind;const char *script;int spin_count,hurdle_count; } command_record;
typedef struct { int bound,raw,has_raw,line;const char *file; } draw_record;

static fbplyr_data player_blocks[14];
static pdata_t pdata_blocks[4];
static gameinfo game_info;
static struct { obj_3d odata; int type,int_receiver,total_time,flight_time;float tx; } ball_obj;
static struct { float theta,x,y,z,xform[12]; } cambot;
static play plays[2];
static header headers[14];
static static_player static_players[14];
static float pose_storage[14][4][60];
static float distxz[14][14];
static int p2p_angles[14][14];
static int bearings[14][14];
static int pup_field,pup_speed,pup_fastturborun,pup_runob,pup_violence,pup_infturbo;
static int p_status,four_plr_ver,did_catch,plowhd_snd,interpolate_hack,saved_backpedal;
static int camera_heading,station_switches,change_value;
static int active_actor;
static int control_returned;
static int active_tick,entered_control,field_index,handicap_level;
static int station_active,station_controlled,station_fire,station_infinite,station_held,station_defends;
static int backpedal_input,backpedal_heading,drone_full_body;
static float input_turbo,station_turbo,ball_distance,carrier_distance;
static int switch_stick,switch_buttons;
static const char *operation,*drone_identity;
static float leap_joint[3],leap_target_velocity[3],leap_offset[3],leap_caps[3];
static int leap_time,leap_victim_argument,leap_throw_target,leap_victim,leap_bytes,leap_ticks;
static char leap_script[36];
static int spin_count,hurdle_count;
static unsigned int breath_control;
static command_record command_log[256];
static int command_count;
static draw_record draw_log[1024];
static int draw_count,raw_count,raw_at;
static int raw_tape[1024];
static float motion_snap_x;

#define RD2GR(x) (camera_heading)
#define PLYRTEAM(x) (four_plr_ver ? ((x)/2) : (!!(x)))
#define fsqrt sqrtf
#define randrng(x) oracle_rng((x),__FILE__,__LINE__)

static int oracle_rng(int,const char *,int);
static void _animate_block(obj_3d *,anidata *);
static void log_command(int,const char *);
static unsigned int bits(float);
static void change_anim(obj_3d *,char *);
static void change_anim1(obj_3d *,char *);
static void change_anim2(obj_3d *,char *);
static void standard_run(obj_3d *);
static void qb_run(obj_3d *);
static void begin_twopart_mode(obj_3d *);
static int _mid_angle(int,int);
static int unique_random(int,int);
static int get_player_sw_current(void) { return station_switches; }
static int bearing(int me,int him) { return bearings[me][him]; }
static float odistxz(obj_3d *a,obj_3d *b) {
    float x=a->x-b->x,z=a->z-b->z;
    return sqrtf(x*x+z*z);
}
static int in_bounds(obj_3d *);
static void abort_drone_route(int player) { (void)player;log_command(4,NULL); }
static void spin_move_speech(void) { log_command(5,NULL); }
static void hurdle_speech(void) { log_command(6,NULL); }
static void stiff_arm_speech(void) { log_command(7,NULL); }
static int wpn_change_player(fbplyr_data *player) { (void)player;log_command(8,NULL);if(change_value)control_returned=1;return change_value; }
static void maybe_lateral(obj_3d *player) { (void)player; }
static void almost_touchdown_sp(obj_3d *player) { (void)player; }
