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
    if (volume != VOLUME4 || pan != 127 || priority != PRIORITY_LVL5)
        exit(3);
    if (bank == cursor_bnk_str && sound == CLOCK_LOW_SND)
        event("sound:cursor:CLOCK_LOW_SND:VOLUME4:127:PRIORITY_LVL5");
    else if (bank == plyr_bnk_str && sound == GUN_SND)
        event("sound:player:GUN_SND:VOLUME4:127:PRIORITY_LVL5");
    else
        exit(3);
}

static void sleep(int ticks)
{
    char text[32];
    snprintf(text, sizeof(text), "sleep:%d", ticks);
    event(text);
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
