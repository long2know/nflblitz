static uint32_t float_bits(float value)
{
    uint32_t bits;
    memcpy(&bits, &value, sizeof(bits));
    return bits;
}

static void reset_case(void)
{
    memset(&game_info, 0, sizeof(game_info));
    memset(tdata_blocks, 0, sizeof(tdata_blocks));
    ball_obj.type = 0;
    fpend = 0;
    clock_active = 0;
    adjustment_value = 2;
    adjustment_error = 0;
    events[0] = '\0';
    events_length = 0;
    wake_limit = 1;
    wake_count = 0;
    published_after_discard = -1;
    first_clock_snapshot[0] = '\0';
    raw_draw_count = 0;
    raw_draws[0] = raw_draws[1] = 0;
    supplied_scores[0] = supplied_scores[1] = 0;
}

static int integer(const char *text)
{
    char *end;
    long value = strtol(text, &end, 0);
    if (!text[0] || *end || value < INT_MIN || value > INT_MAX)
        exit(2);
    return (int)value;
}

static float real(const char *text)
{
    char *end;
    float value = strtof(text, &end);
    if (!text[0] || *end || !isfinite(value))
        exit(2);
    return value;
}

static void validate_dispatch(void)
{
    if (game_info.off_side < 0 || game_info.off_side > 1 ||
        game_info.play_type < 0 || game_info.play_type >= N_TYPES ||
        game_info.play_end_cause < 0 || game_info.play_end_cause >= N_CAUSES ||
        ball_obj.type < LB_PASS || ball_obj.type > LB_LATERAL)
        exit(2);
    /* Reject the original release-mode intentional infinite-loop domains. */
    if (game_info.play_end_cause == PEC_BALL_OOB &&
        ((game_info.play_type == PT_NORMAL && ball_obj.type == LB_KICKOFF) ||
         (game_info.play_type == PT_KICKOFF &&
          ball_obj.type != LB_KICKOFF && ball_obj.type != LB_FUMBLE && ball_obj.type != LB_LATERAL)))
        exit(2);
}

static void print_result(void)
{
    printf("{\"play_result\":%d,\"off_side\":%d,\"fpend\":%d,"
           "\"play_end_pos\":%.9g,\"play_end_pos_bits\":%u,"
           "\"last_play_type\":%d,\"play_result_flags\":%d}\n",
           game_info.play_result, game_info.off_side, fpend,
           (double)game_info.play_end_pos, float_bits(game_info.play_end_pos),
           game_info.last_play_type, game_info.play_result_flags);
}

static void print_meta(void)
{
    uint32_t endian = 1;
    int tsec = 57;
    printf("{\"int_bytes\":%zu,\"float_bytes\":%zu,\"pointer_bytes\":%zu,"
           "\"float_mantissa_bits\":%d,\"little_endian\":%s,"
           "\"game_info_t_bytes\":%zu,\"tdata_t_bytes\":%zu,"
           "\"offsets\":{\"game_time\":%zu,\"off_side\":%zu,\"play_end_pos\":%zu,"
           "\"game_flags\":%zu,\"fumble_x\":%zu},"
           "\"quarter_time\":%d,\"field_half_length_bits\":%u,"
           "\"source_units_per_yard_bits\":%u,\"types\":%d,\"causes\":%d,"
           "\"default_auto_hike_ticks\":%d,\"control_time_ticks\":%d}\n",
           sizeof(int), sizeof(float), sizeof(void *), FLT_MANT_DIG,
           *(unsigned char *)&endian ? "true" : "false",
           sizeof(game_info_t), sizeof(tdata_t),
           offsetof(game_info_t, game_time), offsetof(game_info_t, off_side),
           offsetof(game_info_t, play_end_pos), offsetof(game_info_t, game_flags),
           offsetof(game_info_t, fumble_x), QUARTER_TIME, float_bits(FIELDHZ),
           float_bits(FOCFAC * 6.0f), N_TYPES, N_CAUSES, MAX_HIKE_TIME, CONTROL_TIME);
}

int main(void)
{
    char line[2048];
    if (fesetround(FE_TONEAREST))
        return 3;
    while (fgets(line, sizeof(line), stdin))
    {
        char *args[20], *token;
        int count = 0;
        reset_case();
        for (token = strtok(line, " \t\r\n"); token; token = strtok(NULL, " \t\r\n"))
        {
            if (count == 20)
                return 2;
            args[count++] = token;
        }
        if (!count)
            return 2;
        if (!strcmp(args[0], "meta") && count == 1)
            print_meta();
        else if ((!strcmp(args[0], "classify") || !strcmp(args[0], "spot")) && count == 10)
        {
            game_info.play_type = integer(args[1]);
            game_info.play_end_cause = integer(args[2]);
            game_info.game_flags = integer(args[3]);
            game_info.off_side = integer(args[4]);
            ball_obj.type = integer(args[5]);
            fpend = integer(args[6]);
            game_info.play_end_pos = real(args[7]);
            game_info.fumble_x = real(args[8]);
            game_info.play_result = integer(args[9]);
            game_info.last_play_type = -91;
            game_info.play_result_flags = 0x1234;
            validate_dispatch();
            if (!strcmp(args[0], "spot"))
                original_spot_and_classify();
            else
                result_functions[game_info.play_type][game_info.play_end_cause]();
            print_result();
        }
        else if ((!strcmp(args[0], "clock") || !strcmp(args[0], "clock-init") ||
                  !strcmp(args[0], "clock-pair")) && count == 12)
        {
            char final_snapshot[4096];
            game_info.game_time = integer(args[1]);
            game_info.game_quarter = integer(args[2]);
            game_info.play_time = integer(args[3]);
            game_info.off_side = integer(args[4]);
            clock_active = integer(args[5]);
            game_info.game_state = integer(args[6]);
            adjustment_value = integer(args[7]);
            adjustment_error = integer(args[8]);
            tdata_blocks[0].time_of_poss = integer(args[9]);
            tdata_blocks[1].time_of_poss = integer(args[10]);
            wake_limit = integer(args[11]);
            if (game_info.off_side < 0 || game_info.off_side > 1 ||
                (wake_limit != 1 && wake_limit != 2) ||
                (!strcmp(args[0], "clock-pair") && wake_limit != 2))
                return 2;
            if (!setjmp(iteration_stop))
            {
                if (!strcmp(args[0], "clock-init"))
                    game_clock_proc(NULL);
                else
                    original_clock_iteration();
            }
            clock_snapshot(final_snapshot, sizeof(final_snapshot));
            if (wake_limit == 2)
            {
                if (!first_clock_snapshot[0])
                    return 2;
                printf("{\"first\":%s,\"published_after_discard\":%d,\"second\":%s,"
                       "\"completed_iterations\":%d}\n", first_clock_snapshot,
                       published_after_discard, final_snapshot, wake_count);
            }
            else
                puts(final_snapshot);
        }
        else if (!strcmp(args[0], "discard") && count == 2)
        {
            game_info.game_time = integer(args[1]);
            original_discard_fraction();
            printf("{\"packed\":%d}\n", game_info.game_time);
        }
        else if (!strcmp(args[0], "onside") && count == 8)
        {
            int decision, i;
            game_info.game_quarter = integer(args[1]);
            game_info.game_time = integer(args[2]);
            game_info.off_side = integer(args[3]);
            if (game_info.off_side < 0 || game_info.off_side > 1)
                return 2;
            supplied_scores[game_info.off_side] = integer(args[4]);
            supplied_scores[!game_info.off_side] = integer(args[5]);
            raw_draws[0] = integer(args[6]);
            raw_draws[1] = integer(args[7]);
            if (raw_draws[0] < 0 || raw_draws[1] < 0)
                return 2;
            decision = cpu_do_onside();
            printf("{\"onside\":%s,\"draws\":[", decision ? "true" : "false");
            for (i = 0; i < raw_draw_count; i++)
                printf("%s%d", i ? "," : "", raw_draws[i]);
            printf("],\"events\":[%s]}\n", events);
        }
        else if (!strcmp(args[0], "field") && count == 3)
        {
            game_info.off_side = integer(args[1]);
            game_info.play_end_pos = real(args[2]);
            printf("{\"yard\":%d,\"input_bits\":%u}\n",
                   FIELDX(game_info.play_end_pos), float_bits(game_info.play_end_pos));
        }
        else if (!strcmp(args[0], "world") && count == 3)
        {
            float value;
            game_info.off_side = integer(args[1]);
            value = WORLDX(integer(args[2]));
            printf("{\"value\":%.9g,\"bits\":%u}\n", (double)value, float_bits(value));
        }
        else if (!strcmp(args[0], "line") && count == 14)
        {
            float a[3], b[3], c[3], d[3], value;
            int i;
            for (i = 0; i < 3; ++i)
            {
                a[i] = real(args[1 + i]);
                b[i] = real(args[4 + i]);
                c[i] = real(args[7 + i]);
                d[i] = real(args[10 + i]);
            }
            value = line_int_check(a, b, c, d, real(args[13]));
            printf("{\"intersects\":%s,\"bits\":%u}\n",
                   value != 0.0f ? "true" : "false", float_bits(value));
        }
        else if (!strcmp(args[0], "point") && count == 10)
        {
            float a[3], b[3], p[3], value;
            int i;
            for (i = 0; i < 3; ++i)
            {
                a[i] = real(args[1 + i]);
                b[i] = real(args[4 + i]);
                p[i] = real(args[7 + i]);
            }
            value = point2line_d(a, b, p);
            if (isfinite(value))
                printf("{\"value\":%.9g,\"bits\":%u}\n", (double)value, float_bits(value));
            else
                printf("{\"value\":\"%s\",\"bits\":%u}\n",
                       isnan(value) ? "nan" : (value < 0 ? "-infinity" : "infinity"), float_bits(value));
        }
        else
            return 2;
    }
    return ferror(stdin) ? 2 : 0;
}
