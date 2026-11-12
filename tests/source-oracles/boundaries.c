#include <fenv.h>
#include <float.h>
#include <limits.h>
#include <math.h>
#include <setjmp.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

_Static_assert(sizeof(int) == 4, "Original rule inputs require 32-bit int");
_Static_assert(sizeof(float) == 4 && FLT_RADIX == 2 && FLT_MANT_DIG == 24,
               "Original numeric seams require binary32 float");

/* These external-service handles are deliberately symbolic, not audio/CMOS data. */
enum { TIME_ADJ = 1001, CLOCK_LOW_SND, GUN_SND, VOLUME4, PRIORITY_LVL5 };
#ifdef SOURCE_RULES_CALLER_PROFILE
enum { LOST_FIRE_SND = 1101, PRIORITY_LVL4 };
static int pup_no1stdown, dont_update_los, update_status_box, half_kickoff;
enum { HJUST_CENTER = 1201, VJUST_CENTER, LT_RED, LT_GREEN, NO_GOOD_SP,
       VOLUME3, FANFARE_VOL, LVL4, TWO_PT_SND, VOLUME1, RICOCHET_SND };
enum { SND_TRACK_0 = 1, SND_TRACK_1 = 2, SND_TRACK_5 = 32, DELAY_PID = 0x0f9d };
static int p_status, four_plr_ver, miss_kick, pat_running, pat_delay, pat_wait_min, pat_wait_polls;
static int range_go, range_free, range_count, range_bounds[2], range_values[2];
static int tsec = 57;
static char ancr_swaped_bnk_str[] = "swapped";
#endif
static char cursor_bnk_str[] = "cursor";
static char plyr_bnk_str[] = "player";
static int adjustment_value, adjustment_error;
static jmp_buf iteration_stop;
static char events[2048];
static size_t events_length;
static int wake_limit, wake_count, published_after_discard;
static char first_clock_snapshot[4096];
static void original_discard_fraction(void);
static int raw_draws[2], raw_draw_count, supplied_scores[2];

static void clock_snapshot(char *destination, size_t capacity)
{
    int count = snprintf(destination, capacity,
                         "{\"packed\":%d,\"play_time\":%d,\"time_of_possession\":[%d,%d],"
                         "\"events\":[%s]}", game_info.game_time, game_info.play_time,
                         tdata_blocks[0].time_of_poss, tdata_blocks[1].time_of_poss, events);
    if (count < 0 || (size_t)count >= capacity)
        exit(3);
}

static void event(const char *text)
{
    int count = snprintf(events + events_length, sizeof(events) - events_length,
                         "%s\"%s\"", events_length ? "," : "", text);
    if (count < 0 || (size_t)count >= sizeof(events) - events_length)
        exit(3);
    events_length += (size_t)count;
}

#ifdef SOURCE_RULES_CALLER_PROFILE
/* Logged external boundaries; the selected oracle does not qualify fire/HUD. */
static void reset_team_fire(int side)
{
    char text[48];
    if (side < 0 || side > 1)
        exit(3);
    game_info.team_fire[side] = 0;
    snprintf(text, sizeof(text), "reset_team_fire:%d", side);
    event(text);
}

static void showhide_pinfo(int show)
{
    char text[48];
    snprintf(text, sizeof(text), "showhide_pinfo:%d", show);
    event(text);
}

static int randrng(int bound)
{
    char text[64];
    int value = bound == 10 ? range_go : bound == 100 ? range_free : -1;
    if (range_count >= 2 || value < 0 || value >= bound)
        exit(3);
    range_bounds[range_count] = bound;
    range_values[range_count++] = value;
    snprintf(text, sizeof(text), "randrng:%d:%d", bound, value);
    event(text);
    return value;
}

static void oprintf(const char *format, int justify, int color)
{
    (void)format;
    (void)justify;
    event(color == LT_RED ? "pat-message:red" : "pat-message:green");
}

static void snd_stop_track(int mask)
{
    char text[48];
    snprintf(text, sizeof(text), "stop-track:%d", mask); event(text);
}

static void snd_set_reserved(int mask)
{
    char text[48];
    snprintf(text, sizeof(text), "reserve:%d", mask); event(text);
}

static void killall(int id, unsigned int mask)
{
    char text[64];
    snprintf(text, sizeof(text), "killall:%d:%u", id, mask); event(text);
}

static void extra_point_speech(void) { event("extra_point_speech"); }

static void wait_for_any_butn_press(int minimum, int polls)
{
    char text[64];
    pat_wait_min = minimum; pat_wait_polls = polls;
    snprintf(text, sizeof(text), "wait-any:%d:%d", minimum, polls); event(text);
}
#endif

static int oracle_rand(void)
{
    char text[48];
    int value;
    if (raw_draw_count >= 2)
        exit(3);
    value = raw_draws[raw_draw_count++];
    snprintf(text, sizeof(text), "rand:%d", value);
    event(text);
    return value;
}

static int compute_score(int side)
{
    char text[32];
    if (side < 0 || side > 1)
        exit(3);
    snprintf(text, sizeof(text), "compute_score:%d", side);
    event(text);
    return supplied_scores[side];
}

static int get_adjustment(int which, int *value)
{
    if (which != TIME_ADJ)
        exit(3);
    event("get_adjustment:TIME_ADJ");
    *value = adjustment_value;
    return adjustment_error;
}

static void snd_scall_bank(const char *bank, int sound, int volume, int pan, int priority)
{
#ifdef SOURCE_RULES_CALLER_PROFILE
    if (pat_running)
    {
        char text[128];
        snprintf(text, sizeof(text), "pat-sound:%s:%d:%d:%d:%d", bank, sound, volume, pan, priority);
        event(text);
        return;
    }
#endif
    if (volume != VOLUME4 || pan != 127 || priority != PRIORITY_LVL5)
        exit(3);
    if (bank == cursor_bnk_str && sound == CLOCK_LOW_SND)
        event("sound:cursor:CLOCK_LOW_SND:VOLUME4:127:PRIORITY_LVL5");
    else if (bank == plyr_bnk_str && sound == GUN_SND)
        event("sound:player:GUN_SND:VOLUME4:127:PRIORITY_LVL5");
#ifdef SOURCE_RULES_CALLER_PROFILE
    else if (bank == plyr_bnk_str && sound == LOST_FIRE_SND)
        event("sound:player:LOST_FIRE_SND");
#endif
    else
        exit(3);
}

static void sleep(int ticks)
{
    char text[32];
    snprintf(text, sizeof(text), "sleep:%d", ticks);
    event(text);
#ifdef SOURCE_RULES_CALLER_PROFILE
    if (pat_running)
    {
        pat_delay += ticks;
        return;
    }
#endif
    if (ticks == 1)
    {
        wake_count++;
        if (wake_count < wake_limit)
        {
            clock_snapshot(first_clock_snapshot, sizeof(first_clock_snapshot));
            original_discard_fraction();
            published_after_discard = game_info.game_time;
            events[0] = '\0';
            events_length = 0;
        }
        else
            longjmp(iteration_stop, 1);
    }
}

static void die(int reason)
{
    char text[32];
    snprintf(text, sizeof(text), "die:%d", reason);
    event(text);
    longjmp(iteration_stop, 2);
}
