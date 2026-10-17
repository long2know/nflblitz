#include <limits.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

typedef struct obj_3d {
    int flags, plyrmode, plyrnum, station;
    struct obj_3d *pobj_hit;
} obj_3d;
typedef obj_3d fbplyr_data;
static struct { int ball_carrier; } game_info;
static struct { int int_receiver; } ball_obj;
static char plyr_bnk_str[] = "players";
static int raw[8], raw_count, random_count, sound_count;
static struct { int raw, bound, value, line; } draws[8];
static struct { int id, line, random_calls; unsigned volume, pan, priority; } calls[8];

static int record_random(int line, int bound)
{
    if (bound <= 0 || random_count >= raw_count || random_count >= 8) exit(30);
    int value = raw[random_count] % bound;
    draws[random_count].raw = raw[random_count];
    draws[random_count].bound = bound;
    draws[random_count].value = value;
    draws[random_count].line = line;
    random_count++;
    return value;
}

static int record_sound(int line, const char *bank, int id, unsigned volume,
                        unsigned pan, unsigned priority)
{
    if (strcmp(bank, "players") != 0 || sound_count >= 8) exit(31);
    calls[sound_count].id = id;
    calls[sound_count].line = line;
    calls[sound_count].random_calls = random_count;
    calls[sound_count].volume = volume;
    calls[sound_count].pan = pan;
    calls[sound_count].priority = priority;
    sound_count++;
    return 0;
}

#define randrng(bound) record_random(__LINE__, (bound))
#define snd_scall_bank(bank, id, volume, pan, priority) \
    record_sound(__LINE__, (bank), (id), (volume), (pan), (priority))
#include "original-functions.inc"

int main(void)
{
    int id, function, present, a, b, c, binding;
    obj_3d actor, partner, before_actor, before_partner;
    while (scanf("%d%d%d%d%d%d%d%d%d%d%d%d%d%d%d%d",
                 &id, &function, &actor.flags, &actor.plyrmode, &actor.plyrnum, &actor.station,
                 &present, &partner.plyrmode, &partner.station, &game_info.ball_carrier,
                 &ball_obj.int_receiver, &a, &b, &c, &raw_count, &binding) == 16)
    {
        if (raw_count < 0 || raw_count > 8) return 32;
        for (int i = 0; i < raw_count; i++)
            if (scanf("%d", &raw[i]) != 1 || raw[i] < 0) return 33;
        actor.pobj_hit = present ? &partner : NULL;
        partner.flags = 0; partner.plyrnum = 0; partner.pobj_hit = NULL;
        before_actor = actor; before_partner = partner;
        int before_carrier = game_info.ball_carrier, before_receiver = ball_obj.int_receiver;
        random_count = sound_count = 0;
        if (function == 0) get_hit_snd(&actor, a, b, c);
        else if (function == 1) grnd_hit_snd(&actor, a, b, c);
        else return 34;
        int unchanged = memcmp(&actor, &before_actor, sizeof(actor)) == 0 &&
            memcmp(&partner, &before_partner, sizeof(partner)) == 0 &&
            game_info.ball_carrier == before_carrier && ball_obj.int_receiver == before_receiver;
        printf("{\"id\":%d,\"stateUnchanged\":%s,\"rawConsumed\":%d,\"random\":[",
               id, unchanged ? "true" : "false", random_count);
        for (int i = 0; i < random_count; i++)
            printf("%s{\"raw\":%d,\"bound\":%d,\"value\":%d,\"line\":%d}",
                   i ? "," : "", draws[i].raw, draws[i].bound, draws[i].value, draws[i].line);
        printf("],\"calls\":[");
        for (int i = 0; i < sound_count; i++)
            printf("%s{\"id\":%d,\"line\":%d,\"randomCalls\":%d,\"volume\":%u,\"pan\":%u,\"priority\":%u}",
                   i ? "," : "", calls[i].id, calls[i].line, calls[i].random_calls,
                   calls[i].volume, calls[i].pan, calls[i].priority);
        printf("]}\n");
    }
    return ferror(stdin) ? 35 : 0;
}
