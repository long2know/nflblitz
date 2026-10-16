/* Input/output protocol only; all expected decision/data values come from C. */
static char input[32768],*token;
static int ni(void){ token=strtok(NULL," \r\n"); if(!token)abort();return atoi(token); }
static float nf(void){ token=strtok(NULL," \r\n"); if(!token)abort();return strtof(token,NULL); }
static char *ns(void){ token=strtok(NULL," \r\n");if(!token)abort();return token; }
int bearing(int a,int b){ int x=(1024+player_blocks[a].odata.fwd-p2p_angles[a][b])%1024;return x>512?1024-x:x; }
static void reset(void) {
 memset(player_blocks,0,sizeof(player_blocks));memset(drone_blocks,0,sizeof(drone_blocks));
 memset(&game_info,0,sizeof(game_info));memset(&ball_obj,0,sizeof(ball_obj));memset(blocked,0,sizeof(blocked));
 rng_count=0;raw_length=0;raw_cursor=0;
 game_info.off_side=ni();game_info.off_init=game_info.off_side;
 game_info.los=ni();game_info.first_down=ni();game_info.down=ni();game_info.game_quarter=ni();
 game_info.play_type=ni();game_info.game_flags=ni();game_info.game_mode=ni();p_status=ni();
 game_info.team_handicap[0]=ni();game_info.team_handicap[1]=ni();game_info.team_fire[game_info.off_side]=ni();
 tdata_blocks[game_info.off_side].last_receiver=ni();game_info.line_pos=nf();
 game_info.team_play[game_info.off_side]=get_play(ns());game_info.team_play[!game_info.off_side]=get_play(ns());
 qb_dropback=nf();gain_sought=ni();roll_dir=ni();motion_dir=ni();qb_flags=ni();
 for(int i=0;i<14;i++){
  player_blocks[i].plyrnum=i;player_blocks[i].team=i/7;player_blocks[i].station=-1;player_blocks[i].static_data=&statics[i];
  player_blocks[i].drone_throw_target=-1;
  drone_blocks[i].pscript=game_info.team_play[i/7]->routes[i%7];drone_blocks[i].target=-1;
  drone_blocks[i].stick=drone_blocks[i].old_stick=24;drone_blocks[i].os_time=drone_blocks[i].ob_time=1000;
 }
}
static void tape(void) {
 int count=ni();
 if(count<0 || raw_length+count>8192)abort();
 for(int i=0;i<count;i++) {
  int raw=ni();if(raw<0)abort();raw_tape[raw_length++]=raw;
 }
}
static void actor(void) {
 int i=ni();if(i<0 || i>=14)abort();fbplyr_data *p=&player_blocks[i];
 p->odata.x=nf();p->odata.z=nf();p->odata.vx=nf();p->odata.vz=nf();p->odata.y=nf();
 p->odata.plyrmode=ni();p->job=ni();drone_blocks[i].target=ni();
 int flags=ni();p->odata.flags=0;p->odata.adata[0].animode=p->odata.adata[1].animode=0;
 if(flags&1)p->odata.adata[0].animode|=MODE_UNINT;
 if(flags&2)p->odata.adata[1].animode|=MODE_UNINT;
 if(flags&4)p->odata.adata[0].animode|=MODE_REACTING;
 if(flags&8)p->odata.adata[0].animode|=MODE_PUPPET;
 if(flags&16)p->odata.adata[0].animode|=MODE_PUPPETEER;
 if(flags&32)p->odata.adata[1].animode|=MODE_RUNNING_PUSH;
 if(flags&64)p->odata.flags|=PF_THROWING;
 if(flags&128)p->odata.flags|=PF_REVWPN;
 p->odata.adata[0].seq_index=flags&256?SEQ_T_CRZDIV:0;
 p->station=ni();p->odata.fwd=ni();p->odata.tgt_fwd=ni();p->stick_cur=ni();p->throw_target=ni();
}
static void route(void) {
 int i=ni();if(i<0 || i>=14)abort();drone_t *d=&drone_blocks[i];
 d->pscript=get_route(ns());d->flags=ni();d->blitz_time=ni();
}
static void ball(void) {
 ball_obj.odata.x=nf();ball_obj.odata.z=nf();ball_obj.odata.vx=nf();ball_obj.odata.vz=nf();
 ball_obj.tx=nf();ball_obj.tz=nf();ball_obj.odata.y=nf();ball_obj.odata.vy=nf();
 game_info.ball_carrier=ni();ball_obj.int_receiver=ni();ball_obj.who_threw=ni();
 ball_obj.type=ni();ball_obj.flight_time=ni();ball_obj.total_time=ni();ball_obj.flags=ni();
}
static void clock_input(void) {
 pcount=ni();game_info.play_pcount=ni();cambot.theta=nf();game_info.game_mode=ni();game_info.game_flags=ni();
}
static void geometry(void) {
 for(int i=0;i<14;i++){distxz[i][i]=0;p2p_angles[i][i]=0;
  for(int j=i+1;j<14;j++){
   distxz[i][j]=distxz[j][i]=pdistxz(&player_blocks[i].odata.x,&player_blocks[j].odata.x);
   p2p_angles[i][j]=ptangle(&player_blocks[i].odata.x,&player_blocks[j].odata.x);
   p2p_angles[j][i]=(p2p_angles[i][j]+512)%1024;
  }}
}
static void emit_draws(int base) {
 printf(" %d",rng_count-base);
 for(int i=base;i<rng_count;i++)printf(" %d %d %d",rng_bounds[i],rng_values[i],rng_raw[i]);
}
static void step(void) {
 int actor=ni(),stick_only=ni(),update=ni(),base=rng_count;
 if(actor<0 || actor>=14)abort();
 geometry();
 if(update){assign_blockers();update_receiver_data();}
 pdrone=&drone_blocks[actor];
 player_blocks[actor].drone_script=NULL;player_blocks[actor].drone_throw_target=-1;
 player_blocks[actor].non_buta_time=17;player_blocks[actor].non_turbo_time=17;
 get_drone_switches(&player_blocks[actor],stick_only);
 drone_t *d=&drone_blocks[actor];int word=0;const char *name=route_id(d->pscript,&word);
 printf("S %s",name);
 int ints[]={word,d->flags,d->stick,d->old_stick,d->os_time,d->but,d->old_but,d->ob_time,
 player_blocks[actor].job,d->target,d->pass_react_time,d->wait_time,d->block_time,d->fake_time,
 d->catch_time,d->evade_time,d->think_time,d->blitz_time,d->move_time,d->order_time,d->order_dir,player_blocks[actor].fake_zone_time};
 for(unsigned i=0;i<sizeof(ints)/sizeof(int);i++)printf(" %d",ints[i]);
 float floats[]={d->mx,d->mz,d->gx,d->gz,d->vgx,d->vgz,d->zxlow,d->zxhigh,d->zzlow,d->zzhigh,d->zpoint[0],d->zpoint[1],d->zpv[0],d->zpv[1]};
 for(unsigned i=0;i<sizeof(floats)/sizeof(float);i++)printf(" %.9g",floats[i]);
 printf(" %s %s %d %d %d %d %d",script_id(player_blocks[actor].drone_script),script_id(d->pending_catch),
 player_blocks[actor].drone_throw_target,player_blocks[actor].non_buta_time,player_blocks[actor].non_turbo_time,
 player_blocks[actor].stick_cur,player_blocks[actor].but_cur);
 emit_draws(base);
 for(int i=0;i<14;i++)printf(" %d %d %d",drone_blocks[i].fake_time,drone_blocks[i].order_time,drone_blocks[i].order_dir);
 puts("");fflush(stdout);
}
static void numeric(void){
 float p[3],t[3],v[3],spd,dadj;
 p[0]=nf();p[2]=nf();t[0]=nf();t[2]=nf();v[0]=nf();v[2]=nf();spd=nf();dadj=nf();
 player_blocks[0].odata.x=p[0];player_blocks[0].odata.z=p[2];player_blocks[1].odata.x=t[0];player_blocks[1].odata.z=t[2];
 player_blocks[1].odata.vx=v[0];player_blocks[1].odata.vz=v[2];
 printf("N %.9g %.9g %d %d %d\n",int_time(p,spd,t,v,dadj,0),int_time(p,spd,t,v,dadj,1),
 intercept_time(0,1,spd),ptangle(p,t),dir2stick(ptangle(p,t)));fflush(stdout);
}
static void receivers(void){
 geometry();update_receiver_data();printf("W %d",wcnt);
 for(int i=0;i<wcnt;i++)printf(" %d %d %d",weapons[i],coverage[i],likely_yards[i]);
 for(int cover=0;cover<=2;cover++)for(int gain=-1;gain<=30;gain++)printf(" %d",find_receiver(&player_blocks[game_info.ball_carrier],cover,gain));
 puts("");fflush(stdout);
}
static void assignments(void) {
 geometry();assign_blockers();printf("A %d",rcnt);for(int i=0;i<rcnt;i++)printf(" %d %d %d",rushers[i],rush_times[i],urgent[i]);
 int count=0;for(int i=0;i<7;i++)if(blockers[i]!=-1)count++;
 printf(" %d",count);for(int i=0;i<count;i++)printf(" %d %d",blockers[i],drone_blocks[blockers[i]].target);
 puts("");fflush(stdout);
}
static void lineup(void){
 for(int i=0;i<14;i++)drone_blocks[i].target=-1;
 game_info.off_flip=ni();game_info.line_pos=nf();lineup_players(-1);
 printf("L");
 for(int i=0;i<14;i++)printf(" %.9g %.9g %d %d %d %s",player_blocks[i].odata.x,player_blocks[i].odata.z,
  player_blocks[i].odata.plyrmode,player_blocks[i].odata.fwd,drone_blocks[i].target,formation_script_id(lineup_scripts[i]));
 puts("");fflush(stdout);
}
static void init_pre(void){
 int base=rng_count;init_drones_prelineup(-1);
 printf("I");
 for(int i=0;i<14;i++)printf(" %d %d",drone_blocks[i].flags,drone_blocks[i].blitz_time);
 emit_draws(base);puts("");fflush(stdout);
}
static void init_post(void){
 int base=rng_count;init_drones_postlineup(-1);
 printf("J %.9g %d %d %d %d",qb_dropback,gain_sought,roll_dir,motion_dir,qb_flags);
 for(int i=0;i<14;i++){
  drone_t *d=&drone_blocks[i];int pc=0;const char *name=route_id(d->pscript,&pc);
  printf(" %s %d %d %d %d %d %d %d %d %d %d %d %d %.9g %.9g %.9g %.9g %d",name,pc,d->flags,d->blitz_time,
   player_blocks[i].job,d->target,d->stick,d->old_stick,d->os_time,d->but,d->ob_time,d->think_time,player_blocks[i].fake_zone_time,
   d->zxlow,d->zxhigh,d->zzlow,d->zzhigh,(player_blocks[i].odata.flags&PF_UNBLOCKABLE)!=0);
 }
 emit_draws(base);puts("");fflush(stdout);
}
static void controls(void){
 four_plr_ver=ni();p_status=ni();
 audible_tap_action[game_info.off_side]=ni();audible_tap_action[!game_info.off_side]=ni();
 motion_dir=ni();player_blocks[game_info.off_side*7+4].stick_cur=ni();
 int captain=ni();if(captain<0 || captain>=14)abort();
 game_info.captain_player[game_info.off_side]=captain;player_blocks[captain].but_cur=ni();
}
static void buttons(void){
 int actor=ni();if(actor<0 || actor>=14)abort();player_blocks[actor].but_cur=ni();player_blocks[actor].but_new=ni();
}
static void timers(void) {
 int actor=ni();if(actor<0 || actor>=14)abort();drone_t *d=&drone_blocks[actor];
 d->think_time=ni();d->evade_time=ni();d->blitz_time=ni();
 d->stick=ni();d->old_stick=ni();d->os_time=ni();
 d->but=ni();d->old_but=ni();d->ob_time=ni();
}
int main(void){
 while(fgets(input,sizeof(input),stdin)){
  if(!strchr(input,'\n')){fprintf(stderr,"truncated oracle protocol line\n");return 2;}
  token=strtok(input," \r\n");if(!token)continue;
  if(!strcmp(token,"profile")){printf("HOST %lu %lu %d %d\n",sizeof(float),sizeof(int),FLT_RADIX,FLT_MANT_DIG);fflush(stdout);}
  else if(!strcmp(token,"data"))emit_catalog();
  else if(!strcmp(token,"reset"))reset();
  else if(!strcmp(token,"tape"))tape();
  else if(!strcmp(token,"actor"))actor();
  else if(!strcmp(token,"route"))route();
  else if(!strcmp(token,"ball"))ball();
  else if(!strcmp(token,"clock"))clock_input();
  else if(!strcmp(token,"step"))step();
  else if(!strcmp(token,"numeric"))numeric();
  else if(!strcmp(token,"receivers"))receivers();
  else if(!strcmp(token,"assignments"))assignments();
  else if(!strcmp(token,"lineup"))lineup();
  else if(!strcmp(token,"preinit"))init_pre();
  else if(!strcmp(token,"postinit"))init_post();
  else if(!strcmp(token,"controls"))controls();
  else if(!strcmp(token,"buttons"))buttons();
  else if(!strcmp(token,"timers"))timers();
  else if(!strcmp(token,"quit"))break;
  else{fprintf(stderr,"bad protocol %s\n",token);return 2;}
 }
 return ferror(stdin)?2:0;
}
