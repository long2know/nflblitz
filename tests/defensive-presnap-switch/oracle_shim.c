/* Authored host structures/recorders only. Original bodies/data are extracted privately. */
#include <assert.h>
#include <math.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include "original-defines.h"
#include "selected-poses.h"

typedef struct { int flags, nframes, nlimbs; } aniheader;
typedef struct {
    int animode, iframe, icnt, anicnt, scnt, seq_index, seq_index2, vframe, vcount, hackval;
    float fframe, sinc, *pq, *pxlate, adata[60], idata[60];
    unsigned char *pscript, *pframe, *pframe0;
    aniheader *pahdr;
} anidata;
typedef struct {
    float x, y, z, vx, vy, vz, ax, ay, az, ascale;
    int fwd, tgt_fwd, turn_rate, turn_rate2, plyrmode;
    anidata adata[4];
} obj_3d;
typedef struct fbplyr_data {
    obj_3d odata;
    int station, plyrnum, team, but_new, but_cur, stick_cur;
    struct fbplyr_data *puppet_link;
} fbplyr_data;
static fbplyr_data player_blocks[14];
static struct {
    int plyr_control[4], off_side, captain, captain_player[2], game_flags, auto_hike_cntr, game_mode, los;
    int team_fire[2];
} game_info;
static int four_plr_ver, p_status, db_pflash, rng_value, rng_count, rng_max, selected_actor, calling_station;
static int sound_count, sound_playlist, sound_volume, sound_pan, sound_priority, standard_calls;
static int rng_fake_b, rng_controls[4], rng_captains[2], rng_stations[14];
static char plyr_bnk_str[32], selected_program[32], sound_bank[32], effects[64];
static float lookup[256];
static int interpolate_hack;
static const char *seq_names[] = { "M_HOPCLP", "RUSH_C" };
static unsigned char m_hopclp_anim[1], m_hophop_anim[1];
static aniheader hop_header = { 0, HOP_FRAME_COUNT, 15 }, rush_header = { 0, RUSH_FRAME_COUNT, 15 };
#define P_B 2
#define ISHUMAN(p) ((p)->station != -1)
#define DEFENSE 1
#define FIELDX(x) (x)
#define WORLDX(x) (x)
#define qcreate(...) ((void)0)
#define X 0
#define Y 1
#define Z 2
#define W 3
#define EPSILON 0.00001f
#define PI_RAD 3.14159265358979323846f
#define HALFPI (PI_RAD * 0.5f)
#define fsqrt(x) sqrtf(x)
#define acos_rad(x) acosf(x)
#define sin_rad(x) sinf(x)
#define LESSER(a,b) ((a) < (b) ? (a) : (b))
static int getword(unsigned char *p) { int value; memcpy(&value, p, sizeof(value)); return value; }
#define GETWORD(offset) getword(pani->pscript + (offset))
static float isin(int angle) { return sinf((angle & 1023) * (2.0f * PI_RAD / 1024.0f)); }
static float icos(int angle) { return cosf((angle & 1023) * (2.0f * PI_RAD / 1024.0f)); }
static void set_puppet_pos(obj_3d *a, obj_3d *b) { (void)a; (void)b; abort(); }
static int off_captain(void) { return game_info.captain; }
static int audible_check(fbplyr_data *p, int side) { (void)p; (void)side; return 1; }
static void turn_toward_tgt(obj_3d *p) { (void)p; }
static void standard_run(obj_3d *p) { (void)p; standard_calls++; strcat(effects, "T"); }
static int randrng(int maximum) {
    rng_count++; rng_max = maximum;
    memcpy(rng_controls, game_info.plyr_control, sizeof(rng_controls));
    memcpy(rng_captains, game_info.captain_player, sizeof(rng_captains));
    for (int i = 0; i < 14; i++) rng_stations[i] = player_blocks[i].station;
    int destination = game_info.plyr_control[calling_station];
    rng_fake_b = player_blocks[destination].but_cur;
    strcat(effects, "R");
    return rng_value;
}
static void snd_scall_bank(const char *bank, int playlist, int volume, int pan, int priority) {
    sound_count++; sound_playlist = playlist; sound_volume = volume; sound_pan = pan; sound_priority = priority;
    strcpy(sound_bank, bank); strcat(effects, "S");
}
static void attack_snd(obj_3d *, int, int, int);
static void change_anim(obj_3d *p, unsigned char *program) {
    strcpy(selected_program, program == m_hopclp_anim ? "m_hopclp_anim" : "m_hophop_anim");
    strcat(effects, "A");
    if (program != m_hopclp_anim) abort();
    p->vx = p->vy = p->vz = 0;
    attack_snd(p, HOP_SOUND_A, HOP_SOUND_B, HOP_SOUND_C);
    p->vy = HOP_Y_VELOCITY;
}
static int _ani_newseq(obj_3d *p, anidata *a) {
    (void)p;
    a->pscript += 4; /* Private host handle only, not a generated native SEQ value. */
    a->pahdr = &rush_header; a->seq_index = 1; a->iframe = -1;
    a->pframe0 = (unsigned char *)rush_rotations;
    a->pframe = (unsigned char *)(rush_rotations + 60);
    a->pq = rush_rotations; a->pxlate = rush_roots;
    a->anicnt = 0; interpolate_hack = 0;
    return 0;
}

#include "original-bodies.c"

static void print_array(const int *values, int count) {
    putchar('[');
    for (int i = 0; i < count; i++) printf("%s%d", i ? "," : "", values[i]);
    putchar(']');
}
static void station_case(int id) {
    original_update_stations();
    int stations[14];
    for (int i = 0; i < 14; i++) stations[i] = player_blocks[i].station;
    printf("{\"id\":%d,\"stations\":", id); print_array(stations, 14);
    printf(",\"captains\":"); print_array(game_info.captain_player, 2); puts("}");
}
static void caller_case(int id, int caller, int fresh, int counter, int onside, int mode, int live, int value) {
    selected_actor = caller; rng_value = value;
    game_info.auto_hike_cntr = counter; game_info.game_flags = onside ? GF_ONSIDE_KICK : 0;
    game_info.game_mode = live ? GM_IN_PLAY : 0;
    rng_count = sound_count = standard_calls = 0; rng_max = 0; rng_fake_b = 0;
    selected_program[0] = sound_bank[0] = effects[0] = '\0';
    strcpy(plyr_bnk_str, "players");
    fbplyr_data *p = player_blocks + caller;
    p->but_cur = fresh ? P_B : 0; p->but_new = fresh ? P_B : 0; p->odata.plyrmode = mode;
    for (int i = 0; i < 14; i++) {
        player_blocks[i].odata.x = 1000000.0f; player_blocks[i].odata.ascale = 1.0f;
        player_blocks[i].stick_cur = 24; player_blocks[i].odata.adata[0].animode = MODE_UNINT;
    }
    original_update_stations();
    calling_station = p->station;
    if (mode == MODE_DBPS) _mode_dbps(p);
    int stations[14], buttons[14];
    for (int i = 0; i < 14; i++) { stations[i] = player_blocks[i].station; buttons[i] = player_blocks[i].but_cur; }
    printf("{\"id\":%d,\"controls\":", id); print_array(game_info.plyr_control, 4);
    printf(",\"stations\":"); print_array(stations, 14);
    printf(",\"captains\":"); print_array(game_info.captain_player, 2);
    printf(",\"buttons\":"); print_array(buttons, 14);
    printf(",\"rngCount\":%d,\"rngMaximum\":%d,\"rngFakeB\":%d,\"program\":\"%s\",", rng_count,rng_max,rng_fake_b,selected_program);
    printf("\"soundCount\":%d,\"playlist\":%d,\"volume\":%d,\"pan\":%d,\"priority\":%d,\"bank\":\"%s\",",
        sound_count,sound_playlist,sound_volume,sound_pan,sound_priority,sound_bank);
    printf("\"mode\":%d,\"turnRate\":%d,\"upperTurnRate\":%d,\"effects\":\"%s\"}\n",
        p->odata.plyrmode,p->odata.turn_rate,p->odata.turn_rate2,effects);
}
static void vm_rows(int interruption, int secondary) {
    fbplyr_data actor = {0}; obj_3d *p = &actor.odata; anidata *a = p->adata;
    p->ascale = 1.0f; p->x = p->z = 100.0f; p->vy = HOP_Y_VELOCITY;
    a->pahdr = &hop_header; a->pframe0 = (unsigned char *)hop_rotations;
    a->pframe = (unsigned char *)(hop_rotations + 60); a->pq = hop_rotations; a->pxlate = hop_roots;
    a->iframe = -1; a->animode = HOP_MODE | (secondary ? MODE_SECONDARY : 0);
    unsigned char first[] = { HOP_FIRST_FRAMES, HOP_FIRST_TICKS };
    unsigned char second[] = { HOP_SECOND_FRAMES, HOP_SECOND_TICKS };
    unsigned char stance[] = { STANCE_TICKS, 0,0,0,0, 0, 1 };
    a->pscript = first; _ani_intstream(p,a);
    int segment = 0;
    for (int offset = 0; offset <= 115; offset++) {
        if (offset) {
            if (offset == interruption) {
                interpolate_hack = !!(a->animode & MODE_INTERP);
                a->animode &= 0xffff0000u;
                a->animode &= ~MODE_INTERP;
                a->animode = (a->animode & 0xffff0000u) | MODE_ANCHORXZ;
                a->pscript = stance;
                _ani_inttoseq(p,a);
            } else if (offset < HOP_FIRST_TICKS) _stream_frame(p,a);
            else if (offset == HOP_FIRST_TICKS) {
                _stream_frame(p,a); a->pscript = second; _ani_intstream(p,a); segment = 1;
            } else if (offset < HOP_FIRST_TICKS + HOP_SECOND_TICKS) _stream_frame(p,a);
            else if (offset == HOP_FIRST_TICKS + HOP_SECOND_TICKS) {
                _stream_frame(p,a); a->animode = (a->animode & 0xffff0000u) | MODE_ANCHORXZ; a->pscript = stance;
                _ani_inttoseq(p,a); segment = 2;
            } else if (a->animode & MODE_INTERP) _interpolate_frame(p,a);
            else {
                p->vx = p->vy = p->vz = 0;
                a->pscript = stance + 6;
                if (a->iframe == RUSH_FRAME_COUNT - 1) {
                    a->animode &= 0xffff0000u; a->iframe = -1; _next_frame(p,a,1);
                } else _ani_runtoend(p,a);
            }
        }
        printf("{\"offset\":%d,\"segment\":%d,\"ordinal\":%d,\"floating\":%.9g,\"mode\":%d,",
            offset,segment,a->iframe,(double)a->fframe,a->animode);
        printf("\"targetOrdinal\":%d,", (a->animode & MODE_INTERP) ? a[2].iframe : a->iframe);
        printf("\"hold\":%d,\"interpolation\":%.9g,", a->anicnt,
            (double)((a->animode & MODE_INTERP) ? (float)a->icnt / (float)a[2].icnt : 1.0f));
        printf("\"velocity\":[%.9g,%.9g,%.9g],", (double)p->vx,(double)p->vy,(double)p->vz);
        printf("\"root\":[%.9g,%.9g,%.9g],\"position\":[%.9g,%.9g,%.9g],\"vy\":%.9g,\"pose\":[",
            (double)p->ax,(double)p->ay,(double)p->az,(double)p->x,(double)p->y,(double)p->z,(double)p->vy);
        for (int i = 0; i < 60; i++) printf("%s%.9g",i ? "," : "",(double)a->pq[i]);
        printf("],\"fromPose\":[");
        if (a->animode & MODE_INTERP)
            for (int i = 0; i < 60; i++) printf("%s%.9g",i ? "," : "",(double)a->idata[i]);
        puts("]}");
    }
}
int main(int argc, char **argv) {
    if ((argc == 2 || argc == 3 || argc == 4) && strcmp(argv[1],"vm") == 0) {
        vm_rows(argc >= 3 ? atoi(argv[2]) : -1,argc == 4 ? atoi(argv[3]) : 0); return 0;
    }
    int id, kind, layout, mask, side, captain, caller, fresh, counter, onside, mode, live, value;
    while (scanf("%d%d%d%d%d%d", &id,&kind,&layout,&mask,&side,&captain) == 6) {
        memset(player_blocks,0,sizeof(player_blocks)); memset(&game_info,0,sizeof(game_info));
        four_plr_ver = layout == 4; p_status = mask; game_info.off_side = side; game_info.captain = captain;
        for (int i = 0; i < 4; i++) if (scanf("%d",game_info.plyr_control+i) != 1) return 2;
        game_info.captain_player[0] = 4; game_info.captain_player[1] = 11;
        for (int i = 0; i < 14; i++) {
            player_blocks[i].plyrnum = i; player_blocks[i].team = i/7;
            if (scanf("%d",&player_blocks[i].odata.plyrmode) != 1) return 2;
        }
        if (scanf("%d%d%d%d%d%d%d",&caller,&fresh,&counter,&onside,&mode,&live,&value) != 7) return 2;
        if (kind == 0) station_case(id); else caller_case(id,caller,fresh,counter,onside,mode,live,value);
    }
    return 0;
}
