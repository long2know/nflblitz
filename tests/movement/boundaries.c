static int oracle_rng(int bound,const char *file,int line) {
    if(draw_count>=1024) { fputs("RNG draw trace overflow\n",stderr);exit(3); }
    int raw=0;
    if(bound!=0) {
        if(raw_at>=raw_count) { fprintf(stderr,"Explicit original draw tape exhausted at %s:%d bound=%d\n",file,line,bound);exit(3); }
        raw=raw_tape[raw_at++];
    }
    draw_log[draw_count++]=(draw_record){bound,raw,bound!=0,line,file};
    return bound==0 ? 0 : raw%bound;
}
static void log_command(int kind,const char *script) {
    if(command_count>=256) { fputs("Animation command trace overflow\n",stderr);exit(3); }
    int side=player_blocks[active_actor].team;
    command_log[command_count++]=(command_record){kind,script,game_info.spin_cnt[side],game_info.hurdle_cnt[side]};
}
static const char *script_name(char *script) {
    /* The original D-button code tags a 32-bit pointer. This fixture never dereferences that pointer. */
    uintptr_t value=(uintptr_t)script;
    if((value&0xffffffff00000000ULL)==0) value&=0x7fffffff;
    return (const char *)value;
}
static void _animate_block(obj_3d *pobj,anidata *ani) {
    const char *name=script_name(ani->pscript);
    int part=ani==pobj->adata+1 ? 2 : 0;
    if(part==0 && ani->pscript==(pobj->adata+1)->pscript) part=1;
    /* change_anim1 is identified explicitly by the driver around that original function. */
    extern int animation_call_kind;
    log_command(animation_call_kind,name);
    for(size_t i=0;i<sizeof(responses)/sizeof(responses[0]);i++) {
        script_response *response=&responses[i];
        if(strcmp(name,response->name)) continue;
        if(response->mode>=0) ani->animode=(ani->animode&0xffff0000)|(response->mode&0xffff)|(response->mode&0xffff0000);
        if(response->seq) ani->seq_index=response->seq;
        if(response->zero) pobj->vx=pobj->vy=pobj->vz=0;
        if(response->has_velocity) {
            pobj->vx=FRELX(response->vx,response->vz,pobj->fwd);
            pobj->vy=response->vy;
            pobj->vz=FRELZ(response->vx,response->vz,pobj->fwd);
        }
        return;
    }
}
int animation_call_kind;
