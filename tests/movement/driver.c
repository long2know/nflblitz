static unsigned int bits(float value) { unsigned int result;memcpy(&result,&value,4);return result; }

#define STATE_INTS(X) \
 X(PlayerId,p->plyrnum) X(Station,p->station) X(Mode,o->plyrmode) X(Job,p->job) X(Flags,o->flags) \
 X(LowerMode,o->adata[0].animode) X(UpperMode,o->adata[1].animode) \
 X(Heading,o->fwd) X(TargetHeading,o->tgt_fwd) X(UpperHeading,o->fwd2) X(UpperTargetHeading,o->tgt_fwd2) \
 X(TurnRate,o->turn_rate) X(UpperTurnRate,o->turn_rate2) X(UpperTurnHack,o->adata[1].hackval) \
 X(LowerVariableFrame,o->adata[0].vframe) X(LowerVariableCount,o->adata[0].vcount) \
 X(UpperVariableFrame,o->adata[1].vframe) X(UpperVariableCount,o->adata[1].vcount) \
 X(Stick,p->stick_cur) X(PreviousStick,p->stick_old) X(StickTime,p->stick_time) \
 X(Buttons,p->but_cur) X(OldButtons,p->but_old) X(NewButtons,p->but_new) \
 X(NonTurboTime,p->non_turbo_time) X(NonATime,p->non_buta_time) X(NonBTime,p->non_butb_time) \
 X(BackpedalExpireTime,p->bp_expire_time) X(AttackTime,p->attack_time) X(AttackMode,p->attack_mode) \
 X(CelebrateTime,p->celebrate_time) X(OverrideTime,p->override_time) X(NoCatchTime,p->no_catch_time) \
 X(DroneFace,p->drone_face) X(TeamSpinCount,game_info.spin_cnt[p->team]) X(TeamHurdleCount,game_info.hurdle_cnt[p->team])
#define STATE_FLOATS(APPLY) \
 APPLY(X,o->x) APPLY(Y,o->y) APPLY(Z,o->z) APPLY(VelocityX,o->vx) APPLY(VelocityY,o->vy) APPLY(VelocityZ,o->vz) \
 APPLY(Friction,o->friction) APPLY(OverrideSpeed,p->speed)
#define CONTEXT_INTS(X) \
 X(OffenseSide,game_info.off_side) X(Phase,game_info.game_mode) X(GameFlags,game_info.game_flags) \
 X(PlayType,game_info.play_type) X(OffensePlayFlags,plays[0].flags) X(DefensePlayFlags,plays[1].flags) \
 X(Carrier,game_info.ball_carrier) X(CarrierMode,player_blocks[game_info.ball_carrier>=0?game_info.ball_carrier:0].odata.plyrmode) \
 X(BallType,ball_obj.type) X(IntendedReceiver,ball_obj.int_receiver) X(BallTicksLeft,ball_obj.total_time) \
 X(CameraHeading,camera_heading) X(LineOfScrimmage,game_info.los) X(BackpedalTargetHeading,backpedal_heading) \
 X(DefenseFire,game_info.team_fire[1-game_info.off_side]) X(SpeedPowerup,pup_speed) \
 X(RunOutOfBoundsPowerup,pup_runob) X(ViolencePowerup,pup_violence) \
 X(Field,field_index) X(Handicap,handicap_level) X(Execute,active_tick) X(EnteredControlBlock,entered_control) \
 X(InputStick,switch_stick) X(InputButtons,switch_buttons) X(InputBackpedal,backpedal_input) \
 X(StationActive,station_active) X(StationControlled,station_controlled) X(StationFire,station_fire) \
 X(StationInfinite,station_infinite) X(StationHeld,station_held) X(StationDefends,station_defends) \
 X(ChangeValue,change_value) X(DroneScriptFullBody,drone_full_body)
#define CONTEXT_FLOATS(X) \
 X(BallX,ball_obj.odata.x) X(BallY,ball_obj.odata.y) X(BallDistance,ball_distance) \
 X(LineZ,game_info.line_pos) X(MotionSnapX,motion_snap_x) X(TeamIndexedTurbo,pdata_blocks[p->team].turbo) \
 X(CarrierDistance,carrier_distance) X(BallTargetX,ball_obj.tx) \
 X(InputTurbo,input_turbo) X(StationTurbo,station_turbo)

static int find_sequence(const char *name) {
    if(!strcmp(name,"-"))return 0;
    for(size_t i=0;i<sizeof(sequence_names)/sizeof(sequence_names[0]);i++)
        if(!strcmp(name,sequence_names[i]))return (int)i;
    fprintf(stderr,"Unknown source sequence fixture %s\n",name);exit(3);
}
static void reset(void) {
    memset(player_blocks,0,sizeof(player_blocks));memset(pdata_blocks,0,sizeof(pdata_blocks));
    memset(&game_info,0,sizeof(game_info));memset(&ball_obj,0,sizeof(ball_obj));
    memset(plays,0,sizeof(plays));
    for(int i=0;i<14;i++){
        fbplyr_data *p=&player_blocks[i];p->plyrnum=i;p->team=i/7;p->station=-1;p->stick_cur=p->stick_old=24;p->bptarget=-1;
        p->static_data=&static_players[i];
        for(int j=0;j<4;j++){p->odata.adata[j].pahdr=&headers[i];p->odata.adata[j].pq=pose_storage[i][j];p->odata.adata[j].adata=pose_storage[i][j];}
        for(int j=0;j<14;j++){distxz[i][j]=100.0f;p2p_angles[i][j]=0;bearings[i][j]=512;}
    }
    for(int i=0;i<4;i++){pdata_blocks[i].turbo=100;game_info.plyr_control[i]=-1;}
    game_info.team_play[0]=&plays[0];game_info.team_play[1]=&plays[1];
    game_info.ball_carrier=-1;ball_obj.int_receiver=-1;
    command_count=draw_count=raw_count=raw_at=0;
    camera_heading=station_switches=change_value=0;
    control_returned=0;
    pup_field=pup_speed=pup_fastturborun=pup_runob=pup_violence=pup_infturbo=p_status=0;
    four_plr_ver=1;did_catch=plowhd_snd=0;
    active_tick=entered_control=1;field_index=0;handicap_level=5;
    backpedal_input=0;backpedal_heading=-1;drone_full_body=1;
    input_turbo=station_turbo=100.0f;ball_distance=carrier_distance=100.0f;
    switch_stick=24;switch_buttons=0;breath_control=0;motion_snap_x=0;
    station_active=station_controlled=1;station_fire=station_infinite=station_held=station_defends=0;
    operation="standard";drone_identity="juke_c_anim";
    memset(&cambot,0,sizeof(cambot));cambot.xform[0]=cambot.xform[5]=cambot.xform[10]=1.0f;
    leap_time=22;leap_victim_argument=-2;leap_throw_target=7;leap_victim=leap_ticks=leap_bytes=0;
    leap_joint[0]=40.0f;leap_joint[1]=8.0f;leap_joint[2]=20.0f;
    leap_target_velocity[0]=leap_target_velocity[1]=leap_target_velocity[2]=0.0f;
    leap_offset[0]=leap_offset[2]=0.0f;leap_offset[1]=-4.4f;
    leap_caps[0]=0.7f;leap_caps[1]=2.2f;leap_caps[2]=1.65f;
}
static void assign(fbplyr_data *p,char *name,char *value) {
    obj_3d *o=&p->odata;
    if(!strcmp(name,"Op")){operation=value;return;}
    if(!strcmp(name,"s.LowerSequence")){o->adata[0].seq_index=find_sequence(value);return;}
    if(!strcmp(name,"s.UpperSequence")){o->adata[1].seq_index=find_sequence(value);return;}
    if(!strcmp(name,"c.DroneScript")){drone_identity=value;return;}
    if(!strcmp(name,"c.BreathControl")){breath_control=(unsigned int)strtoul(value,NULL,10);return;}
    if(!strcmp(name,"c.FastTurboPowerup")){pup_fastturborun=atoi(value)?1<<p->team:0;return;}
    if(!strcmp(name,"c.CarrierLateHittable")){
        if(game_info.ball_carrier>=0 && atoi(value))player_blocks[game_info.ball_carrier].odata.flags|=PF_LATEHITABLE;
        return;
    }
    if(!strcmp(name,"c.StaticSpeedAttribute")){p->static_data->at_spd=atoi(value);return;}
    if(!strcmp(name,"l.Time")){leap_time=atoi(value);return;}
    if(!strcmp(name,"l.VictimArgument")){leap_victim_argument=atoi(value);return;}
    if(!strcmp(name,"l.ThrowTarget")){leap_throw_target=atoi(value);return;}
    #define LF(n,v) if(!strcmp(name,"l."#n)){v=strtof(value,NULL);return;}
    LF(JointX,leap_joint[0]) LF(JointY,leap_joint[1]) LF(JointZ,leap_joint[2])
    LF(TargetVelocityX,leap_target_velocity[0]) LF(TargetVelocityY,leap_target_velocity[1]) LF(TargetVelocityZ,leap_target_velocity[2])
    LF(OffsetX,leap_offset[0]) LF(OffsetY,leap_offset[1]) LF(OffsetZ,leap_offset[2])
    LF(MaximumYVelocity,leap_caps[0]) LF(MaximumPlanarVelocity,leap_caps[1]) LF(MinimumPlanarVelocity,leap_caps[2])
    LF(CameraX,cambot.x) LF(CameraY,cambot.y) LF(CameraZ,cambot.z)
    #undef LF
    int matrix_index;
    if(sscanf(name,"l.Matrix%d",&matrix_index)==1){if(matrix_index<0||matrix_index>=12)exit(3);cambot.xform[matrix_index]=strtof(value,NULL);return;}
    if(!strcmp(name,"Raw")){
        char *at=value;
        while(*at){if(raw_count>=1024)exit(3);raw_tape[raw_count++]=(int)strtol(at,&at,10);if(*at==',')at++;else if(*at)exit(3);}
        return;
    }
    #define SI(n,v) if(!strcmp(name,"s."#n)){v=atoi(value);return;}
    STATE_INTS(SI)
    #undef SI
    #define SF(n,v) if(!strcmp(name,"s."#n)){v=strtof(value,NULL);return;}
    STATE_FLOATS(SF)
    #undef SF
    #define CI(n,v) if(!strcmp(name,"c."#n)){v=atoi(value);return;}
    CONTEXT_INTS(CI)
    #undef CI
    #define CF(n,v) if(!strcmp(name,"c."#n)){v=strtof(value,NULL);return;}
    CONTEXT_FLOATS(CF)
    #undef CF
    int neighbor;
    if(sscanf(name,"d%dMode",&neighbor)==1 && strstr(name,"Mode")){
        if(neighbor<0||neighbor>6)exit(3);
        player_blocks[(1-game_info.off_side)*7+neighbor].odata.plyrmode=atoi(value);return;
    }
    if(sscanf(name,"n%dDistance",&neighbor)==1 && strstr(name,"Distance")){
        if(neighbor<0||neighbor>13)exit(3);distxz[p->plyrnum][neighbor]=strtof(value,NULL);return;
    }
    if(sscanf(name,"n%dBearing",&neighbor)==1 && strstr(name,"Bearing")){
        if(neighbor<0||neighbor>13)exit(3);bearings[p->plyrnum][neighbor]=atoi(value);return;
    }
    fprintf(stderr,"Unknown fixture field %s\n",name);exit(3);
}
static void dump_state(fbplyr_data *p,int allowed,int human,float turbo) {
    obj_3d *o=&p->odata;
    printf("{\"State\":{");int first=1;
    #define PI(n,v) printf("%s\""#n"\":%d",first?"":",",(int)(v));first=0;
    STATE_INTS(PI)
    #undef PI
    #define PF(n,v) printf(",\""#n"\":%u",bits(v));
    STATE_FLOATS(PF)
    #undef PF
    printf(",\"LowerSequence\":\"%s\",\"UpperSequence\":\"%s\"},\"Allowed\":%d,\"Human\":%d,\"Turbo\":%u,\"BreathControl\":%u,\"Commands\":[",
           sequence_names[o->adata[0].seq_index],sequence_names[o->adata[1].seq_index],allowed,human,bits(turbo),breath_control);
    for(int i=0;i<command_count;i++){
        if(i)printf(",");printf("{\"Kind\":%d,\"Script\":",command_log[i].kind);
        if(command_log[i].script)printf("\"%s\"",command_log[i].script);else printf("null");
        printf(",\"TeamSpinCount\":%d,\"TeamHurdleCount\":%d}",command_log[i].spin_count,command_log[i].hurdle_count);
    }
    printf("],\"Draws\":[");
    for(int i=0;i<draw_count;i++){
        draw_record *d=&draw_log[i];if(i)printf(",");
        printf("{\"Bound\":%d,\"Raw\":",d->bound);if(d->has_raw)printf("%d",d->raw);else printf("null");
        printf(",\"Line\":%d,\"File\":\"%s\"}",d->line,d->file);
    }
    printf("],\"Defense\":[");
    if(!strcmp(operation,"defense") && active_tick){
        for(int i=(1-game_info.off_side)*7,j=0;j<7;i++,j++){
            if(j)printf(",");printf("{\"PlayerId\":%d,\"Speed\":%u,\"OverrideTime\":%d}",i,bits(player_blocks[i].speed),player_blocks[i].override_time);
        }
    }
    printf("],\"LeapVictim\":%d,\"LeapTicks\":%d,\"LeapBytes\":%d,\"ReturnFromMode\":%d}\n",
           leap_victim,leap_ticks,leap_bytes,control_returned);
}
int main(void) {
    char line[200000];
    while(fgets(line,sizeof(line),stdin)){
        if(!strncmp(line,"data",4)){dump_constants();continue;}
        reset();fbplyr_data *p=&player_blocks[3];obj_3d *o=&p->odata;
        for(char *token=strtok(line," \r\n");token;token=strtok(NULL," \r\n")){
            char *eq=strchr(token,'=');if(!eq){fputs("Malformed movement fixture\n",stderr);return 3;}
            *eq=0;
            if(!strcmp(token,"s.PlayerId")){p=&player_blocks[atoi(eq+1)];o=&p->odata;}
            assign(p,token,eq+1);
        }
        /* Play pointers use source side order, while fixture inputs name offense and defense. */
        if(game_info.off_side){play swap=plays[0];plays[0]=plays[1];plays[1]=swap;}
        plays[p->team].formation[p->position].x=p->team==0?motion_snap_x-WORLDX(game_info.los):WORLDX(game_info.los)-motion_snap_x;
        p->team=p->plyrnum/7;p->bptarget=backpedal_heading<0?-1:(p->plyrnum+1)%14;
        active_actor=p->plyrnum;
        if(p->bptarget>=0)p2p_angles[p->plyrnum][p->bptarget]=backpedal_heading;
        if(pup_runob)pup_runob=1<<p->team;
        if(game_info.ball_carrier>=0 && game_info.ball_carrier!=p->plyrnum)
            distxz[p->plyrnum][game_info.ball_carrier]=carrier_distance;
        ball_obj.odata.z=p->odata.z;
        if(ball_distance!=100.0f)ball_obj.odata.x=p->odata.x+ball_distance;
        game_info.team_handicap[0]=game_info.team_handicap[1]=handicap_level;
        pup_field=field_index;
        p->drone_script=NULL;
        for(size_t i=0;i<sizeof(script_symbols)/sizeof(script_symbols[0]);i++)
            if(!strcmp(drone_identity,script_symbols[i].name)){p->drone_script=script_symbols[i].script;break;}
        if(!p->drone_script){fputs("Missing tagged script boundary identity\n",stderr);return 3;}
        if(drone_full_body)p->drone_script=(char *)((uintptr_t)p->drone_script|0x80000000);
        int allowed=active_tick?mode_allows(p):0,human=human_controls(p);
        float resource=station_turbo;
        if(active_tick){
            if(!strcmp(operation,"input")||!strcmp(operation,"step")){
                input_head(p);p->stick_cur=switch_stick;p->but_cur=switch_buttons;
                if(backpedal_input)o->flags|=PF_BACKPEDAL;
                if(p->station>=0)pdata_blocks[p->station].turbo=input_turbo;
                input_tail(p);
            }
            if(!strcmp(operation,"turn"))turn_toward_tgt(o);
            if(!strcmp(operation,"standard"))standard_run(o);
            if(!strcmp(operation,"quarterback"))qb_run(o);
            if(!strcmp(operation,"general"))general_run(o);
            if(!strcmp(operation,"physics"))physics(p);
            if(!strcmp(operation,"maybe_dive"))maybe_dive(o);
            if(!strcmp(operation,"slower"))allowed=slower_spin(o);
            if(!strcmp(operation,"before")&&game_info.ball_carrier==p->plyrnum)weapon_before(p);
            if(!strcmp(operation,"weapon")&&entered_control)weapon_after(p);
            if(!strcmp(operation,"qb_action")&&entered_control)quarterback_after(p);
            if(!strcmp(operation,"db_jump")&&entered_control)defensive_jump(p);
            if(!strcmp(operation,"station")){
                int station=0;
                p_status=station_active?1:0;four_plr_ver=1;
                game_info.plyr_control[station]=station_controlled?p->plyrnum:-1;
                game_info.team_fire[0]=station_fire;pup_infturbo=station_infinite?1:0;
                station_switches=station_held?P_C:0;
                player_blocks[p->plyrnum].team=station_defends?1-game_info.off_side:game_info.off_side;
                pdata_blocks[station].turbo=station_turbo;station_resource(station);
                resource=pdata_blocks[station].turbo;
            }
            if(!strcmp(operation,"defense")){
                set_defense_speeds();
            }
            if(!strcmp(operation,"leap")){
                p->throw_target=leap_throw_target;leap_victim=leap_resolve_victim(o,leap_victim_argument);
                leap_ticks=leap_resolve_time(o,leap_time,leap_offset[1]);
                for(int i=0;i<3;i++)player_blocks[leap_victim].jpos[1][i]=leap_joint[i];
                obj_3d *target=&player_blocks[leap_victim].odata;
                if(leap_victim!=p->plyrnum){target->vx=leap_target_velocity[0];target->vy=leap_target_velocity[1];target->vz=leap_target_velocity[2];}
                int arguments[9]={leap_victim_argument,1,leap_time,0,0,0,0,0,0};
                for(int i=0;i<3;i++){arguments[3+i]=(int)bits(leap_offset[i]);arguments[6+i]=(int)bits(leap_caps[i]);}
                for(int i=0;i<9;i++)for(int j=0;j<4;j++)leap_script[i*4+j]=(char)((unsigned int)arguments[i]>>(j*8));
                o->adata[0].pscript=leap_script;
                _ani_leapatt(o,&o->adata[0]);leap_bytes=(int)(o->adata[0].pscript-leap_script);
            }
            if(!strcmp(operation,"step")){
                if(o->plyrmode==MODE_WPN||o->plyrmode==MODE_DB){o->turn_rate=64;o->turn_rate2=48;}
                turn_toward_tgt(o);
                if(o->plyrmode==MODE_WPN&&game_info.ball_carrier==p->plyrnum)weapon_before(p);
                allowed=mode_allows(p);
                if(allowed){
                    if(o->plyrmode==MODE_QB){qb_run(o);quarterback_after(p);}
                    else {standard_run(o);if(o->plyrmode==MODE_WPN)weapon_after(p);else if(o->plyrmode==MODE_DB)defensive_jump(p);}
                }
                physics(p);
            }
        }
        dump_state(p,allowed,human,resource);
    }
    return 0;
}
