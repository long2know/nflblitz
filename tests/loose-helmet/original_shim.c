/* Declared component inputs only. Original functions/headers are inserted privately, never redistributed. */
#define _XOPEN_SOURCE 700
#include <ucontext.h>
#include <stdio.h>
#include <stdlib.h>
#include <stdint.h>
#include <string.h>
#include <math.h>

#define TRUE 1
#define FALSE 0
typedef struct { float x,y,z,vx,vy,vz; int flags,fwd,fwd2,pdecal[7]; } obj_3d;
typedef struct { obj_3d odata; int team,station,plyrnum; float jpos[15][3]; } fbplyr_data;
/* ORIGINAL_BALL_DATA */
/* ORIGINAL_MACROS */
static ball_data loose_helmet_obj, ball_obj;
static fbplyr_data player_blocks[14];
static struct { float x,y,z,xform[12]; } cambot;
static struct { int game_mode,ball_carrier,team_head[4],team_fire[2]; } game_info;
static int loose_helmet_decal,loose_helmet_ratio,limb_phelm_obj,pcount,current_tick;
static const char *plyr_bnk_str="plyr_bnk_str", *ancr_swaped_bnk_str="ancr_swaped_bnk_str";
struct process_node { struct process_node *next,*prev; int process_id; void (*process_exit)(struct process_node *,int); };
typedef struct process_node process_node_t;
static struct process_node process,caller,*plist,*cur_proc;
struct object_node { struct object_node *next,*prev; int object_id; void *data; };
static struct object_node object,*olist,*ofree;
#define NO_FREE_MEMORY 0
static int resource_mode=NO_FREE_MEMORY;
static ucontext_t system_context,process_context;
static unsigned char stack_memory[131072];
static int args[4]={5,0,0,0},alive=1,entry_value=45,rng_count,sound_count,operation_count,exit_alerts;
static int helper_mode,helper_second;
static struct { int tick,bound,value,line; } rng[8192];
static struct { int tick,call,volume,pan,line; const char *priority,*bank; } sounds[256];
static const char *operations[32];
static void loose_helmet_proc(int *);
static void draw_loose_helmet(void *);
static void killall(int,int);
static void delete_multiple_objects(int,int);
static void delete_process(struct process_node *p) {
    if(p->prev)p->prev->next=p->next;else plist=p->next;
    if(p->next)p->next->prev=p->prev;
    alive=0;
}
static void lockup(void) { exit(7); }
static int bits(float value) { union { float f; int32_t i; } v;v.f=value;return v.i; }
static int oracle_rng(int bound,int line) {
    int value=helper_mode ? (rng_count==0 ? entry_value : helper_second) :
        bound==100 ? entry_value : (rng_count*17+11)%bound;
    if(value<0 || value>=bound || rng_count>=8192)exit(3);
    rng[rng_count].tick=current_tick;rng[rng_count].bound=bound;rng[rng_count].value=value;rng[rng_count++].line=line;
    return value;
}
static void oracle_sleep(int delay) { if(delay!=1)exit(4);swapcontext(&process_context,&system_context); }
static void *oracle_create(const char *name,int group,void (*function)(int *),int a,int b,int c,int d) {
    if(strcmp(name,"loosehlm") || group!=LOOSE_HELMET_PID || function!=loose_helmet_proc || a!=5 || b || c || d)exit(5);
    operations[operation_count++]="CreateProcess";alive=1;return NULL;
}
static struct object_node *create_object(const char *name,int id,int flags,int order,void *data,void (*draw)(void *)) {
    if(strcmp(name,"losehmlt") || id!=OID_LOOSE_HELMET || flags!=OF_NONE || order!=DRAWORDER_BALL ||
        data!=&loose_helmet_obj || draw!=draw_loose_helmet)exit(6);
    memset(&object,0,sizeof(object));object.object_id=id;object.data=data;olist=&object;return &object;
}
static void oracle_sound(const char *bank,int call,int volume,int pan,const char *priority,int line) {
    if(sound_count>=256)exit(9);
    sounds[sound_count].bank=bank;sounds[sound_count].tick=current_tick;sounds[sound_count].call=call;
    sounds[sound_count].volume=volume;sounds[sound_count].pan=pan;sounds[sound_count].priority=priority;sounds[sound_count++].line=line;
}
/* ORIGINAL_LIFECYCLE */
static void traced_killall(int id,int mask) {
    if(id!=LOOSE_HELMET_PID || mask!=-1)exit(10);
    operations[operation_count++]="KillProcess";killall(id,mask);
}
static void traced_delete(int id,int mask) {
    if(id!=OID_LOOSE_HELMET || mask!=-1)exit(11);
    operations[operation_count++]="DeleteObjectGroup";delete_multiple_objects(id,mask);
}
#define killall(id,mask) traced_killall(id,mask)
#define delete_multiple_objects(id,mask) traced_delete(id,mask)
#define randrng(bound) oracle_rng(bound,__LINE__)
#define sleep(delay) oracle_sleep(delay)
#define qcreate(name,group,function,a,b,c,d) oracle_create(name,group,function,a,b,c,d)
#define snd_scall_bank(bank,call,volume,pan,priority) oracle_sound(bank,call,volume,pan,#priority,__LINE__)
#define fsqrt sqrtf
/* ORIGINAL_DIRECTION */
static uint32_t constant_color;
static const char *color_function,*alpha_source,*depth_function,*blend;
static int depth_write,captured_ratio,captured_decal,captured_limb;
static float captured_matrix[12];
#define grConstantColorValue(value) (constant_color=(uint32_t)(value))
#define guAlphaSource(value) (alpha_source=#value)
#define grTexCombineFunction(unit,value) ((void)0)
#define grTexClampMode(unit,s,t) ((void)0)
#define grDepthBufferMode(value) ((void)0)
#define grDepthBufferFunction(value) (depth_function=#value)
#define grDepthMask(value) (depth_write=(value))
#define grChromakeyMode(value) ((void)0)
#define guColorCombineFunction(value) (color_function=#value)
#define grAlphaBlendFunction(a,b,c,d) (blend=#a "/" #b "/" #c "/" #d)
#define FXTRUE 1
static void render_limb(void *limb,float *matrix,int *decal,int *ratio) {
    captured_limb=limb==&limb_phelm_obj;captured_decal=*decal;captured_ratio=*ratio;
    memcpy(captured_matrix,matrix,sizeof captured_matrix);
}
/* ORIGINAL_FUNCTIONS */
/* ORIGINAL_NEW_PLAY */
static void trampoline(void) { loose_helmet_proc(args);exit(12); }
static void initialize(float neck_y) {
    cambot.x=10;cambot.y=20;cambot.z=30;
    cambot.xform[0]=.6f;cambot.xform[2]=.8f;cambot.xform[5]=1;cambot.xform[8]=-.8f;cambot.xform[10]=.6f;
    for(int actor=0;actor<14;actor++) {
        player_blocks[actor].plyrnum=actor;player_blocks[actor].station=-1;player_blocks[actor].odata.pdecal[2]=4321;
        for(int joint=0;joint<15;joint++)for(int axis=0;axis<3;axis++)player_blocks[actor].jpos[joint][axis]=(float)(joint+axis+1);
    }
    player_blocks[5].jpos[8][1]=neck_y;
    memset(&process,0,sizeof(process));process.process_id=LOOSE_HELMET_PID;plist=&process;cur_proc=&process;alive=1;
    getcontext(&process_context);process_context.uc_stack.ss_sp=stack_memory;
    process_context.uc_stack.ss_size=sizeof(stack_memory);process_context.uc_link=&system_context;
    makecontext(&process_context,trampoline,0);
}
static void wake(void) { current_tick++;pcount=120+current_tick;if(alive)swapcontext(&system_context,&process_context); }
static void print_row(void) {
    ball_data *h=&loose_helmet_obj;
    printf("{\"position\":[%d,%d,%d],\"velocity\":[%d,%d,%d],\"target\":[%d,%d,%d],",
        bits(h->odata.x),bits(h->odata.y),bits(h->odata.z),bits(h->odata.vx),bits(h->odata.vy),bits(h->odata.vz),
        bits(h->tx),bits(h->ty),bits(h->tz));
    printf("\"heading\":%d,\"elevation\":%d,\"phi\":%d,\"pitch\":%d,\"pitchRate\":%d,\"alpha\":%d,\"scale\":%d,",
        h->odata.fwd,h->odata.fwd2,h->phi,h->pitch,h->pitch_rate,h->alpha,bits(h->scale));
    printf("\"flightTime\":%d,\"flags\":%d,\"bounces\":%d,\"bounceTime\":%d}",h->flight_time,h->flags,h->bounces,h->bounce_time);
}
static void print_random(void) {
    printf("[");for(int i=0;i<rng_count;i++)printf("%s{\"tick\":%d,\"bound\":%d,\"value\":%d,\"line\":%d}",
        i?",":"",rng[i].tick,rng[i].bound,rng[i].value,rng[i].line);printf("]");
}
int main(int argc,char **argv) {
    if(argc<2)return 2;
    if(!strcmp(argv[1],"helper")) {
        if(argc!=8 && argc!=9)return 2;
        helper_mode=1;initialize(10);current_tick=1;pcount=121;
        game_info.game_mode=atoi(argv[2]);game_info.team_head[0]=atoi(argv[3]);game_info.ball_carrier=atoi(argv[4]);
        game_info.team_fire[0]=atoi(argv[5]);entry_value=atoi(argv[6]);helper_second=atoi(argv[7]);
        player_blocks[5].station=argc==9?atoi(argv[8]):0;
        object.object_id=OID_LOOSE_HELMET;object.data=&loose_helmet_obj;olist=&object;cur_proc=&caller;
        maybe_pop_helmet(&player_blocks[5].odata);
        printf("{\"popped\":%s,\"objectAlive\":%s,\"processAlive\":%s,\"operations\":[",
            (player_blocks[5].odata.flags&PF_POP_HELMET)?"true":"false",olist?"true":"false",alive?"true":"false");
        for(int i=0;i<operation_count;i++)printf("%s\"%s\"",i?",":"",operations[i]);
        printf("],\"random\":");print_random();printf(",\"sounds\":[");
        for(int i=0;i<sound_count;i++)printf("%s{\"bank\":\"%s\",\"call\":%d,\"volume\":%d,\"pan\":%d,\"priority\":\"%s\"}",
            i?",":"",sounds[i].bank,sounds[i].call,sounds[i].volume,sounds[i].pan,sounds[i].priority);
        printf("]}\n");return 0;
    }
    if(!strcmp(argv[1],"draw")) {
        initialize(10);loose_helmet_obj.odata.x=7;loose_helmet_obj.odata.y=11;loose_helmet_obj.odata.z=13;
        loose_helmet_obj.odata.fwd=137;loose_helmet_obj.odata.fwd2=-333;loose_helmet_obj.phi=2051;
        loose_helmet_obj.alpha=127;loose_helmet_obj.scale=2;loose_helmet_ratio=4;loose_helmet_decal=4321;
        printf("[");
        for(int global=0;global<=1;global++)for(int own=0;own<=1;own++) {
            ball_obj.flags=global;loose_helmet_obj.flags=own;draw_loose_helmet(&loose_helmet_obj);
            printf("%s{\"globalFlags\":%d,\"ownFlags\":%d,\"matrix\":[",global||own?",":"",global,own);
            for(int i=0;i<12;i++)printf("%s%d",i?",":"",bits(captured_matrix[i]));
            printf("],\"constantColor\":%u,\"alphaSource\":\"%s\",\"colorFunction\":\"%s\",\"depthFunction\":\"%s\","
                "\"depthWrite\":%d,\"blend\":\"%s\",\"ratio\":%d,\"decal\":%d,\"phelm\":%s}",
                constant_color,alpha_source,color_function,depth_function,depth_write,blend,captured_ratio,captured_decal,captured_limb?"true":"false");
        }
        printf("]\n");return 0;
    }
    if(!strcmp(argv[1],"lifecycle")) {
        initialize(10);wake();cur_proc=&caller;killall(LOOSE_HELMET_PID,-1);
        int kill_object=olist!=NULL,kill_process=alive;
        delete_multiple_objects(OID_LOOSE_HELMET,-1);
        printf("{\"killLeavesObject\":%s,\"killLeavesProcess\":%s,\"deleteRemovesObject\":%s,\"exitAlerts\":%d}\n",
            kill_object?"true":"false",kill_process?"true":"false",!olist?"true":"false",exit_alerts);
        return 0;
    }
    if(!strcmp(argv[1],"carry")) {
        initialize(10);printf("{\"rows\":[");
        for(int i=0;i<120;i++)wake();print_row();
        cur_proc=&caller;source_new_play();initialize(10);wake();printf(",");print_row();
        printf("],\"random\":");print_random();printf("}\n");return 0;
    }
    if(strcmp(argv[1],"trace") || argc!=7)return 2;
    entry_value=atoi(argv[2]);initialize(strtof(argv[3],NULL));loose_helmet_obj.flags=atoi(argv[4]);loose_helmet_obj.phi=atoi(argv[5]);
    int ticks=atoi(argv[6]);printf("{\"rows\":[");
    for(int i=0;i<ticks;i++){wake();if(i)printf(",");print_row();}
    printf("],\"random\":");print_random();printf(",\"sounds\":[");
    for(int i=0;i<sound_count;i++)printf("%s{\"tick\":%d,\"Call\":%d,\"Volume\":%d,\"Pan\":%d,\"priority\":%d,\"line\":%d}",
        i?",":"",sounds[i].tick,sounds[i].call,sounds[i].volume,sounds[i].pan,PRIORITY_LVL2,sounds[i].line);
    printf("]}\n");return 0;
}
