/* Declared host inputs for extracted original C functions. No target ABI/PRNG/scheduler claim. */
#define _XOPEN_SOURCE 700
#include <ucontext.h>
#include <stdio.h>
#include <stdlib.h>
#include <stdint.h>
#include <string.h>
#include <math.h>

#define NUM_SMOKE_TXTRS 17
#define SPFX_PID 0x0f98
#define PLAYER_PID 0x0500
#define PFLASH_PID 0x1300
#define OID_SMKPUFF 0x510
#define OF_NONE 0
#define DRAWORDER_BALL 0x50000
#define GM_IN_PLAY 2
#define PF_FLASHWHITE 0x400
#define JOINT_LWRIST 7
#define JOINT_RANKLE 11
#define JOINT_RWRIST 4
#define JOINT_RSHOULDER 2
#define JOINT_LSHOULDER 5
#define JOINT_LANKLE 14
#define JOINT_PELVIS 0
#define PF_DRAGGING_PLR 0x100000
#define PF_FLIP 1
#define MODE_QB 3
#define MODE_REACTING 1
#define ISHUMAN(p) ((p)->station != -1)
#define GM_PLAY_OVER 3
/* Named input bindings, deliberately not a cast of the port's SourceBallKind numbers. */
#define LB_PASS 1
#define LB_BOBBLE 2
#define LB_LATERAL 3
#define LB_FUMBLE 4
#define LB_KICKOFF 5
#define LB_PUNT 6
#define LB_FIELD_GOAL 7
#define BF_FIRE_PASS 1
#define BF_TURBO_PASS 2
#define BF_BOUNCE 4
#define EFFECT_NONE 0
#define EFFECT_FIRE 1
#define EFFECT_PCLOUD 2
#define EFFECT_BTRAIL 3
#define EFFECT_BLINK 4

typedef struct { int animode; } animode_t;
typedef struct { float x,y,z,vx,vy,vz; int flags,plyrmode; animode_t adata[2]; } obj_3d;
typedef struct { obj_3d odata; int team,station,plyrnum; float jpos[15][3]; } fbplyr_data;
typedef struct { obj_3d odata; int ratio,decal; void *limb_obj; } obj3d_data_t;
typedef struct { struct { struct { int aspect_ratio; } header; } texture_info; int texture_handle; } texture_node;
struct object_node { obj3d_data_t *data; int active; };
static fbplyr_data player_blocks[14];
static struct { obj_3d odata; int type,flags,flight_time; } ball_obj;
static struct { float x,y,z,xform[12]; } cambot;
static struct { int game_mode,team_fire[2],off_side; } game_info;
static texture_node texture_nodes[17], *smkpuf_decals[17];
static int limb_Polyset;
static struct object_node object;
static ucontext_t system_context, process_context;
static unsigned char stack_memory[131072];
static int original_args[4], current_tick, remaining=1, alive=1;
static void (*original_entry)(int *);
static int tape[256], tape_count=-1, tape_position, rng_count;
static struct { int tick,bound,value,line; } rng[256];
static int child_joints[256], child_lines[256], child_count;
static int child_groups[256], child_args[256][4];
static int pcount;

void plyr_fire_trail_proc(int *);
void ball_fire_trail_proc(int *);
void draw_spfx(void *);
static int oracle_rng(int bound, int line) {
    int value=tape_count<0 ? 0 : tape_position<tape_count ? tape[tape_position++] : -1;
    if (value<0 || value>=bound || rng_count>=256) { fprintf(stderr,"bad declared RNG input bound%d\n",bound); exit(3); }
    rng[rng_count].tick=current_tick; rng[rng_count].bound=bound; rng[rng_count].value=value; rng[rng_count++].line=line;
    return value;
}
static void oracle_sleep(int ticks, int line) {
    (void)line;
    remaining=ticks;
    if (swapcontext(&process_context,&system_context)!=0) exit(4);
}
static void *oracle_qcreate(const char *name,int group,void (*fn)(int *),int a,int b,int c,int d,int line) {
    (void)name; (void)group; (void)fn; (void)a; (void)c; (void)d;
    if (child_count>=256) exit(5);
    child_groups[child_count]=group;
    child_args[child_count][0]=a; child_args[child_count][1]=b; child_args[child_count][2]=c; child_args[child_count][3]=d;
    child_joints[child_count]=b; child_lines[child_count++]=line;
    return NULL;
}
static struct object_node *create_object(const char *name,int id,int flags,int priority,void *data,void (*draw)(void *)) {
    (void)name; (void)id; (void)flags; (void)priority; (void)draw;
    object.data=data; object.active=1; return &object;
}
static void delete_object(struct object_node *node) { node->active=0; }
#define randrng(bound) oracle_rng(bound,__LINE__)
#define sleep(ticks) oracle_sleep(ticks,__LINE__)
#define qcreate(name,group,fn,a,b,c,d) oracle_qcreate(name,group,fn,a,b,c,d,__LINE__)

/* The values below identify symbolic API calls only, not target Glide enum/ABI numbers. */
#define GR_ALPHASOURCE_TEXTURE_ALPHA 1
#define GR_TEXTURECOMBINE_DECAL 2
#define GR_TEXTURECLAMP_WRAP 3
#define GR_DEPTHBUFFER_WBUFFER 4
#define GR_CMP_ALWAYS 5
#define FXTRUE 1
#define GR_CHROMAKEY_DISABLE 6
#define GR_COLORCOMBINE_TEXTURE_TIMES_ALPHA 7
#define GR_BLEND_SRC_ALPHA 8
#define GR_BLEND_ONE_MINUS_SRC_ALPHA 9
static float captured_cm[12];
static int captured_states[18], state_count;
static void guAlphaSource(int a) { captured_states[state_count++]=a; }
static void grTexCombineFunction(int a,int b) { captured_states[state_count++]=a; captured_states[state_count++]=b; }
static void grTexClampMode(int a,int b,int c) { captured_states[state_count++]=a; captured_states[state_count++]=b; captured_states[state_count++]=c; }
static void grDepthBufferMode(int a) { captured_states[state_count++]=a; }
static void grDepthBufferFunction(int a) { captured_states[state_count++]=a; }
static void grAlphaTestFunction(int a) { captured_states[state_count++]=a; }
static void grDepthMask(int a) { captured_states[state_count++]=a; }
static void grChromakeyMode(int a) { captured_states[state_count++]=a; }
static void guColorCombineFunction(int a) { captured_states[state_count++]=a; }
static void grAlphaBlendFunction(int a,int b,int c,int d) {
    captured_states[state_count++]=a; captured_states[state_count++]=b; captured_states[state_count++]=c; captured_states[state_count++]=d;
}
static void render_limb(void *limb,float *cm,int *decal,int *ratio) {
    (void)limb; (void)decal; (void)ratio; memcpy(captured_cm,cm,sizeof captured_cm);
}

/* ORIGINAL_FUNCTIONS */
/* ORIGINAL_BALL_SELECTION_BODY */

static void trampoline(void) { original_entry(original_args); alive=0; remaining=0; }
static void print_ints(const int *values,int count) {
    putchar('['); for(int i=0;i<count;i++) printf("%s%d",i?",":"",values[i]); putchar(']');
}
static void print_requests(void) {
    putchar('[');
    for(int i=0;i<child_count;i++) {
        printf("%s{\"args\":",i?",":""); print_ints(child_args[i],4);
        printf(",\"group\":%d,\"line\":%d}",child_groups[i],child_lines[i]);
    }
    putchar(']');
}
int main(int argc,char **argv) {
    if(argc!=9) return 2;
    const int kind=atoi(argv[1]), ticks=atoi(argv[5]), mode_switch=atoi(argv[6]);
    original_args[0]=atoi(argv[2]); original_args[1]=atoi(argv[3]); original_args[2]=atoi(argv[4]);
    if(strcmp(argv[7],"constant-zero")!=0) {
        tape_count=0;
        if(strcmp(argv[7],"empty")!=0) {
            char copy[4096]; if(strlen(argv[7])>=sizeof copy) return 2; strcpy(copy,argv[7]);
            for(char *v=strtok(copy,",");v;v=strtok(NULL,",")) { if(tape_count>=256)return 2; tape[tape_count++]=atoi(v); }
        }
    }
    cambot.x=10; cambot.y=20; cambot.z=30;
    cambot.xform[2]=1; cambot.xform[5]=1; cambot.xform[8]=-1;
    ball_obj.odata.x=-100; ball_obj.odata.y=25; ball_obj.odata.z=35;
    for(int actor=0;actor<14;actor++) for(int joint=0;joint<15;joint++)
        for(int axis=0;axis<3;axis++) player_blocks[actor].jpos[joint][axis]=(float)(joint+axis+1);
    player_blocks[2].odata.flags=0x55;
    for(int i=0;i<17;i++) { texture_nodes[i].texture_info.header.aspect_ratio=3; texture_nodes[i].texture_handle=i; smkpuf_decals[i]=&texture_nodes[i]; }
    if(kind==5) {
        char copy[512]; if(strlen(argv[8])>=sizeof copy)return 2; strcpy(copy,argv[8]);
        int values[11],count=0;
        for(char *v=strtok(copy,",");v;v=strtok(NULL,",")) { if(count>=11)return 2; values[count++]=atoi(v); }
        if(count!=11)return 2;
        fbplyr_data *actor=&player_blocks[values[0]];
        actor->plyrnum=values[0]; actor->team=values[1]?0:1; actor->station=values[2]; actor->odata.flags=values[3];
        actor->odata.vx=(float)values[4]; actor->odata.vz=(float)values[5];
        actor->odata.adata[0].animode=values[6]?MODE_REACTING:0; actor->odata.plyrmode=values[7]?MODE_QB:-1;
        game_info.off_side=0; game_info.team_fire[actor->team]=values[8]; pcount=values[9];
        maybe_plyr_fire(actor);
        printf("{\"requests\":"); print_requests(); printf(",\"random\":[");
        for(int i=0;i<rng_count;i++) printf("%s{\"tick\":0,\"bound\":%d,\"value\":%d,\"line\":%d}",i?",":"",rng[i].bound,rng[i].value,rng[i].line);
        printf("]}\n"); return 0;
    }
    if(kind==6) {
        char copy[8192]; if(strlen(argv[8])>=sizeof copy)return 2; strcpy(copy,argv[8]);
        printf("["); int step=0; char *outer=NULL;
        for(char *row=strtok_r(copy,";",&outer);row;row=strtok_r(NULL,";",&outer)) {
            char *inner=NULL, *name=strtok_r(row,",",&inner); int values[6],count=0;
            for(char *v=strtok_r(NULL,",",&inner);v;v=strtok_r(NULL,",",&inner)) { if(count>=6)return 2; values[count++]=atoi(v); }
            if(count!=6)return 2;
            ball_obj.type=!strcmp(name,"Pass")?LB_PASS:!strcmp(name,"Bobble")?LB_BOBBLE:!strcmp(name,"Lateral")?LB_LATERAL:
                !strcmp(name,"Fumble")?LB_FUMBLE:!strcmp(name,"Kickoff")?LB_KICKOFF:!strcmp(name,"Punt")?LB_PUNT:LB_FIELD_GOAL;
            ball_obj.flags=(values[0]?BF_FIRE_PASS:0)|(values[1]?BF_TURBO_PASS:0)|(values[2]?BF_BOUNCE:0);
            game_info.off_side=0; game_info.team_fire[0]=values[3]; ball_obj.flight_time=values[4]; game_info.game_mode=values[5];
            child_count=0; int effect=original_ball_selection_body();
            const char *effect_name=effect==EFFECT_NONE?"None":effect==EFFECT_FIRE?"Fire":effect==EFFECT_PCLOUD?"PlayerCloud":effect==EFFECT_BTRAIL?"BallTrail":"Blink";
            printf("%s{\"effect\":\"%s\",\"requests\":",step++?",":"",effect_name); print_requests(); putchar('}');
        }
        printf("]\n"); return 0;
    }
    if(kind==4) {
        obj3d_data_t puff={0}; puff.odata.x=-100; puff.odata.y=25; puff.odata.z=35; puff.ratio=3;
        draw_spfx(&puff);
        FILE *geometry=fopen(argv[8],"rb"); if(!geometry)return 6;
        unsigned char header[20]; float points[12],uvs[8]; uint32_t vertices[6],uv_indices[6];
        if(fread(header,1,20,geometry)!=20 || fread(points,4,12,geometry)!=12 || fread(uvs,4,8,geometry)!=8 ||
            fread(vertices,4,6,geometry)!=6 || fread(uv_indices,4,6,geometry)!=6) return 6;
        fclose(geometry);
        printf("{\"cameraVertices\":[");
        for(int i=0;i<6;i++) {
            int v=(int)vertices[i]*3;
            float x=points[v]*captured_cm[0]+points[v+1]*captured_cm[1]+points[v+2]*captured_cm[2]+captured_cm[3];
            float y=points[v]*captured_cm[4]+points[v+1]*captured_cm[5]+points[v+2]*captured_cm[6]+captured_cm[7];
            float z=points[v]*captured_cm[8]+points[v+1]*captured_cm[9]+points[v+2]*captured_cm[10]+captured_cm[11];
            printf("%s[%.9g,%.9g,%.9g]",i?",":"",(double)x,(double)y,(double)z);
        }
        printf("],\"symbolicStates\":"); print_ints(captured_states,state_count); printf("}\n");
        return 0;
    }
    original_entry=kind==0 ? player_burst_into_flames : kind==1 ? plyr_fire_trail_proc :
        kind==2 ? ball_fire_trail_proc : flash_plyr_proc;
    getcontext(&process_context);
    process_context.uc_stack.ss_sp=stack_memory; process_context.uc_stack.ss_size=sizeof stack_memory;
    process_context.uc_link=&system_context; makecontext(&process_context,trampoline,0);
    printf("{\"rows\":[");
    for(current_tick=0;current_tick<=ticks;current_tick++) {
        game_info.game_mode=current_tick>=mode_switch ? GM_IN_PLAY : 1;
        if(current_tick>0 && alive && remaining>0 && --remaining==0)
            if(swapcontext(&system_context,&process_context)!=0) return 4;
        obj3d_data_t *p=object.active ? object.data : NULL;
        printf("%s{\"tick\":%d,\"alive\":%s,\"sleep\":%d,\"frame\":%d,\"x\":%.9g,\"y\":%.9g,\"z\":%.9g,\"vy\":%.9g,\"flags\":%d,\"childJoints\":",
            current_tick?",":"",current_tick,alive?"true":"false",alive?remaining:0,p?p->decal:-1,
            p?(double)p->odata.x:0,p?(double)p->odata.y:0,p?(double)p->odata.z:0,p?(double)p->odata.vy:0,player_blocks[2].odata.flags);
        print_ints(child_joints,child_count); printf(",\"rngCount\":%d}",rng_count);
    }
    printf("],\"random\":[");
    for(int i=0;i<rng_count;i++) printf("%s{\"tick\":%d,\"bound\":%d,\"value\":%d,\"line\":%d}",i?",":"",rng[i].tick,rng[i].bound,rng[i].value,rng[i].line);
    printf("],\"childLines\":"); print_ints(child_lines,child_count); printf(",\"returnCause\":%d}\n",alive?-1:1);
    return 0;
}
