#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <math.h>

/* ORIGINAL_CONSTANTS */

#define snd_scall_bank(...) ((void)0)
#define crowd_cheer_snds(...) ((void)0)
#define showhide_pinfo(...) ((void)0)
#define qcreate(...) ((void)0)

typedef struct { unsigned int animode; } animation_data;
typedef struct {
    float x, y, z;
    float vx, vy, vz;
    unsigned int flags;
    int plyrmode;
    void *pobj_hit;
    animation_data adata[2];
} obj_3d;
typedef struct { obj_3d odata; int team, plyrnum; } fbplyr_data;
typedef struct { int identity; } play_t;
struct fbteam_data { int drone_base; };
struct diff_set { int panic, drone_min, human_max; };
static struct {
    int off_side, off_init, last_pos, ball_carrier, game_mode, game_flags;
    int down, los, first_down, play_type, last_play_type, play_result, play_result_flags, play_end_cause;
    int game_quarter, just_got_1st, off_turnover;
    int team[2], team_fire[2], team_handicap[2], scores[2][7];
    float catch_x, play_end_pos, fumble_x;
    play_t *team_play[2];
} game_info;
static struct { obj_3d odata; float tx, tz; int type, flags, catch_pcount, pitch; } ball_obj;
/* ORIGINAL_STATS_FIELDS */
static fbplyr_data player_blocks[14];
static play_t plays[4];
static int pcount, forward_catcher, fd_flash_time, update_status_box;
static int no_result_box, half_kickoff, fpend, four_plr_ver;
static int p_status, pup_offense, pup_defense, pup_drones, pup_noassist, pup_superdrones, pup_no1stdown;
static int scores[2];
static int quick_raw[2], quick_bounds[2], quick_draws;
static int randrng(int bound) { quick_bounds[quick_draws]=bound; return quick_raw[quick_draws++]%bound; }
static int compute_score(int side) { return scores[side]; }
static int over_first_down(obj_3d *pobj) { (void)pobj; return 0; }
static void reset_team_fire(int team);
static void check_receiver_fire(fbplyr_data *actor);

/* ORIGINAL_DATA */
/* ORIGINAL_FUNCTIONS */

static char *token;
static int integer(void) {
    token = strtok(NULL, " \r\n");
    if (!token) { fputs("missing integer\n", stderr); exit(3); }
    return (int)strtol(token, NULL, 10);
}
static float number(void) {
    token = strtok(NULL, " \r\n");
    if (!token) { fputs("missing float\n", stderr); exit(3); }
    return strtof(token, NULL);
}
static void read_fire(void) {
    memset(&game_info, 0, sizeof(game_info));
    memset(&ball_obj, 0, sizeof(ball_obj));
    memset(player_blocks, 0, sizeof(player_blocks));
    memset(tdata_blocks, 0, sizeof(tdata_blocks));
    game_info.off_side=integer(); game_info.off_init=integer(); game_info.last_pos=integer();
    game_info.down=integer(); game_info.los=integer(); game_info.first_down=integer();
    game_info.play_type=integer(); ball_obj.type=integer(); game_info.game_flags=integer();
    game_info.play_end_pos=number(); game_info.fumble_x=number(); forward_catcher=integer();
    game_info.team_fire[0]=integer(); game_info.team_fire[1]=integer();
    tdata_blocks[0].consecutive_comps=integer(); tdata_blocks[1].consecutive_comps=integer();
    tdata_blocks[0].last_receiver=integer(); tdata_blocks[1].last_receiver=integer();
    tdata_blocks[0].consecutive_sacks=integer(); tdata_blocks[1].consecutive_sacks=integer();
    game_info.ball_carrier=integer();
    fbplyr_data *actor=player_blocks+game_info.ball_carrier;
    actor->plyrnum=game_info.ball_carrier; actor->team=actor->plyrnum/7;
    actor->odata.x=number(); actor->odata.y=number(); actor->odata.z=number();
    actor->odata.flags=integer() ? PF_PEND_FCHECK : 0;
    pcount=integer(); ball_obj.catch_pcount=0; game_info.catch_x=number();
    game_info.game_mode=integer() ? GM_IN_PLAY : GM_PLAY_OVER;
    game_info.play_result=integer(); pup_no1stdown=integer(); ball_obj.flags=integer();
    tdata_blocks[0].interceptions=integer(); tdata_blocks[1].interceptions=integer();
    tdata_blocks[0].lost_fumbles=integer(); tdata_blocks[1].lost_fumbles=integer();
}
static void write_fire(void) {
    printf("%d %d %d %d %d %d %d %d %d %d %d %d %d %d %d %d %d %d %d %d %d %d\n",
        game_info.team_fire[0], game_info.team_fire[1],
        tdata_blocks[0].consecutive_comps, tdata_blocks[1].consecutive_comps,
        tdata_blocks[0].last_receiver, tdata_blocks[1].last_receiver,
        tdata_blocks[0].consecutive_sacks, tdata_blocks[1].consecutive_sacks,
        forward_catcher, game_info.last_pos, game_info.game_flags,
        tdata_blocks[0].interceptions, tdata_blocks[1].interceptions,
        tdata_blocks[0].lost_fumbles, tdata_blocks[1].lost_fumbles,
        !!(player_blocks[game_info.ball_carrier].odata.flags & PF_PEND_FCHECK),
        game_info.off_side, game_info.down, game_info.los, game_info.first_down,
        game_info.play_result, game_info.play_type);
}
static int mask(int logical) {
    return ((logical&1) ? TEAM_MASK(0) : 0) | ((logical&2) ? TEAM_MASK(1) : 0);
}
static void handicap(void) {
    scores[0]=integer(); scores[1]=integer(); game_info.game_quarter=integer();
    game_info.off_side=integer(); four_plr_ver=integer(); p_status=mask(integer());
    game_info.team[0]=integer(); game_info.team[1]=integer();
    user_base_adjust=integer(); human_winstreak=integer(); random_drones=integer();
    pup_offense=mask(integer()); pup_defense=mask(integer()); pup_drones=mask(integer());
    pup_noassist=integer(); pup_superdrones=integer(); drones_lose=integer(); drones_winning=integer();
    game_info.team_play[0]=game_info.team_play[1]=plays+integer();
    for(int i=0;i<5;i++) { int id=integer(); play_hist[i]=id<0 ? NULL : plays+id; }
    set_handicaps();
    printf("%d %d %d %d",game_info.team_handicap[0],game_info.team_handicap[1],drones_lose,drones_winning);
    for(int i=0;i<5;i++) printf(" %d",play_hist[i] ? (int)(play_hist[i]-plays) : -1);
    puts("");
}
static void quick_kick(void) {
    game_info.off_side=integer();
    game_info.team_handicap[game_info.off_side]=integer();
    quick_raw[0]=integer(); quick_raw[1]=integer(); quick_draws=0;
    original_quick_setup();
    obj_3d *actor=&player_blocks[3+7*game_info.off_side].odata;
    printf("%.9g %.9g %.9g %.9g %.9g %.9g %.9g %.9g %.9g %.9g %d %d\n",
        actor->x,actor->z,ball_obj.odata.x,ball_obj.odata.y,ball_obj.odata.z,
        ball_obj.odata.vx,ball_obj.odata.vy,ball_obj.odata.vz,ball_obj.tx,ball_obj.tz,
        quick_bounds[0],quick_bounds[1]);
}
int main(void) {
    char line[8192];
    while(fgets(line,sizeof(line),stdin)) {
        char command=*strtok(line," \r\n");
        if(command=='H') handicap();
        else if(command=='K') quick_kick();
        else {
            int cause=integer();
            read_fire();
            fbplyr_data *actor=player_blocks+game_info.ball_carrier;
            if(command=='C') original_carrier(actor);
            else if(command=='D') original_forced_control(actor);
            else if(command=='P') original_result(cause);
            else if(command=='X') original_result_dismissed();
            else if(command=='R') reset_onfire_values();
            else return 4;
            write_fire();
        }
        if(strtok(NULL," \r\n")!=NULL) { fputs("extra input\n",stderr); return 5; }
        fflush(stdout);
    }
    return 0;
}
