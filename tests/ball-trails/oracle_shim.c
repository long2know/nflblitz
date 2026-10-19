/* Authored, bounded host adapters for the privately extracted original bodies.
 * No target ABI/layout, native scheduler, memory reuse or pixel claim.
 * Alpha is unsigned at the GL color-packing boundary to avoid the original
 * signed alpha<<24 overflow. No object_data is read after its stack returns. */
#define _XOPEN_SOURCE 700
#include <ucontext.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

typedef struct { float x,y,z; int fwd,fwd2; } obj_3d;
typedef struct {
    obj_3d odata;
    uint32_t alpha;
    float scale;
    int phi,flags,retained_sentinel;
} ball_data;
struct object_node { ball_data *data; int active,hidden; };
static ball_data ball_obj;
static struct { int ball_carrier; } game_info;
static struct { float x,y,z,xform[12]; } cambot;
static int pup_bigball, limb_medball_obj_big, ball_decal, ball_decal2=1, ball_ratio;
static struct object_node object;
static ucontext_t system_context, process_context;
static union { long double alignment; unsigned char bytes[131072]; } process_stack;
static uint16_t sleep_field=1;
static int current_tick, alive=1, samples, entry_tick;
static int drawn,selected,combiner,alpha_source,depth_mode,depth_function,depth_write;
static int blend[4];
static uint32_t constant_color;
static float captured[12];

#define GLIDE3 1
#define BF_WHITE 1
#define BF_ALT 64
#define BF_PHANTOM 2048
#define OID_BALL 0x400
#define DRAWORDER_BALL 0x50000
#define OF_NONE 0
#define GR_ALPHASOURCE_CC_ALPHA 101
#define GR_TEXTURECOMBINE_DECAL 102
#define GR_TEXTURECLAMP_WRAP 103
#define GR_DEPTHBUFFER_WBUFFER 104
#define GR_CMP_LESS 105
#define FXTRUE 1
#define GR_CHROMAKEY_DISABLE 106
#define GR_COLORCOMBINE_DECAL_TEXTURE_ADD_CCALPHA 107
#define GR_COLORCOMBINE_TEXTURE_TIMES_ALPHA 108
#define GR_BLEND_SRC_ALPHA 109
#define GR_BLEND_ONE_MINUS_SRC_ALPHA 110
#define GR_BLEND_ONE 111
#define GR_BLEND_ZERO 112

static void grConstantColorValue(uint32_t value) { constant_color=value; }
static void guAlphaSource(int value) { alpha_source=value; }
static void grTexCombineFunction(int unit,int value) {
    if(unit!=0 || value!=GR_TEXTURECOMBINE_DECAL) exit(20);
}
static void grTexClampMode(int unit,int s,int t) {
    if(unit!=0 || s!=GR_TEXTURECLAMP_WRAP || t!=GR_TEXTURECLAMP_WRAP) exit(21);
}
static void grDepthBufferMode(int value) { depth_mode=value; }
static void grDepthBufferFunction(int value) { depth_function=value; }
static void grDepthMask(int value) { depth_write=value; }
static void grChromakeyMode(int value) { if(value!=GR_CHROMAKEY_DISABLE) exit(22); }
static void guColorCombineFunction(int value) { combiner=value; }
static void grAlphaBlendFunction(int a,int b,int c,int d) {
    blend[0]=a; blend[1]=b; blend[2]=c; blend[3]=d;
}
static void render_limb(int *limb,float *matrix,int *decal,int *ratio) {
    if(limb!=&limb_medball_obj_big || ratio!=&ball_ratio) exit(23);
    drawn=1; selected=*decal; memcpy(captured,matrix,sizeof(captured));
}
static void oracle_sleep(int ticks) {
    if(ticks!=1) exit(24);
    sleep_field|=(uint16_t)ticks;
    if(swapcontext(&process_context,&system_context)!=0) exit(25);
}
#define sleep(ticks) oracle_sleep(ticks)
static struct object_node *create_object(const char *name,int id,int flags,int order,void *data,void (*draw)(void *)) {
    (void)draw;
    if(strcmp(name,"ball") || id!=OID_BALL || flags!=OF_NONE || order!=DRAWORDER_BALL || object.active) exit(26);
    object.data=data; object.active=1; object.hidden=0; samples++; entry_tick=current_tick;
    if(object.data->retained_sentinel!=ball_obj.retained_sentinel) exit(27);
    return &object;
}
static void delete_object(struct object_node *node) {
    if(node!=&object || !object.active) exit(28);
    object.active=0; object.data=NULL;
}

/* ORIGINAL_TRIGS */
/* ORIGINAL_MATH */
/* ORIGINAL_DRAW */
/* ORIGINAL_TRAIL */

static void trampoline(void) {
    int args[4]={0,0,0,0};
    trail_ball_proc(args);
    alive=0; sleep_field=0;
}
static void live_ball(int tick) {
    ball_obj.odata.x=(float)(7+tick*101); ball_obj.odata.y=(float)(11+tick*3); ball_obj.odata.z=(float)(13-tick*17);
    ball_obj.odata.fwd=137+tick*71; ball_obj.odata.fwd2=-333+tick*41; ball_obj.phi=1025+tick*60;
    ball_obj.scale=3.5f+(float)tick; ball_obj.flags=0x841; ball_obj.alpha=255;
    ball_obj.retained_sentinel=1000+tick;
}
static void trace_row(void) {
    ball_data empty={0}, *b=object.active ? object.data : &empty;
    printf("%s{\"tick\":%d,\"processes\":%d,\"sleep\":%u,\"objects\":%d,\"stage\":%d,"
           "\"alpha\":%u,\"scale\":%.9g,\"flags\":%d,\"hidden\":%s,"
           "\"position\":[%.9g,%.9g,%.9g],\"angles\":[%d,%d,%d],\"entryTick\":%d,\"samples\":%d}",
           current_tick?",":"",current_tick,alive,sleep_field,object.active,
           object.active ? (215-(int)b->alpha)/15 : -1,b->alpha,b->scale,b->flags,
           object.active && object.hidden ? "true":"false",b->odata.x,b->odata.y,b->odata.z,
           b->odata.fwd,b->odata.fwd2,b->phi,object.active ? entry_tick:0,samples);
}
static int trace(int suspended) {
    if(getcontext(&process_context)!=0) return 30;
    process_context.uc_stack.ss_sp=process_stack.bytes;
    process_context.uc_stack.ss_size=sizeof(process_stack.bytes);
    process_context.uc_link=&system_context;
    makecontext(&process_context,trampoline,0);
    putchar('['); live_ball(0); trace_row();
    for(current_tick=1;current_tick<(suspended ? 14:7);current_tick++) {
        live_ball(current_tick);
        if(suspended && current_tick==9) { sleep_field&=0x7fff; object.hidden=0; }
        if(alive && (int16_t)sleep_field>0 && --sleep_field==0)
            if(swapcontext(&system_context,&process_context)!=0) return 31;
        if(suspended && current_tick==1) { sleep_field|=0x8000; object.hidden=1; }
        trace_row();
    }
    puts("]"); return 0;
}
static int draws(void) {
    int hidden,global_flags,carrier,big,local_flags,heading,elevation,roll;
    unsigned alpha;
    float scale,x,y,z,cx,cy,cz;
    int count=0;
    putchar('[');
    while(scanf("%d %d %d %d %d %u %f %f %f %f %d %d %d %f %f %f",
          &carrier,&global_flags,&big,&hidden,&local_flags,&alpha,&scale,&x,&y,&z,&heading,&elevation,&roll,&cx,&cy,&cz)==16) {
        if(count>=512 || alpha>255 || hidden<0 || hidden>1) return 32;
        ball_data local={0};
        local.odata.x=x;local.odata.y=y;local.odata.z=z;local.odata.fwd=heading;local.odata.fwd2=elevation;
        local.phi=roll;local.scale=scale;local.flags=local_flags;local.alpha=alpha;
        ball_obj.flags=global_flags;game_info.ball_carrier=carrier;pup_bigball=big;
        cambot.x=cx;cambot.y=cy;cambot.z=cz;
        for(int i=0;i<12;i++) if(scanf("%f",&cambot.xform[i])!=1) return 33;
        drawn=selected=combiner=alpha_source=depth_mode=depth_function=depth_write=0;
        constant_color=0;memset(captured,0,sizeof(captured));memset(blend,0,sizeof(blend));
        if(!hidden) draw_ball(&local);
        int white=(global_flags&BF_WHITE)!=0,effective_alpha=white ? 255:(int)alpha;
        if(drawn && (alpha_source!=GR_ALPHASOURCE_CC_ALPHA || depth_mode!=GR_DEPTHBUFFER_WBUFFER ||
            depth_function!=GR_CMP_LESS || depth_write!=FXTRUE ||
            constant_color!=((uint32_t)effective_alpha<<24) ||
            combiner!=(white ? GR_COLORCOMBINE_DECAL_TEXTURE_ADD_CCALPHA:GR_COLORCOMBINE_TEXTURE_TIMES_ALPHA) ||
            blend[0]!=GR_BLEND_SRC_ALPHA || blend[1]!=GR_BLEND_ONE_MINUS_SRC_ALPHA ||
            blend[2]!=GR_BLEND_ONE || blend[3]!=GR_BLEND_ZERO ||
            selected!=((local_flags&BF_ALT)!=0))) return 34;
        printf("%s{\"drawn\":%s,\"alternate\":%s,\"white\":%s,\"localAlpha\":%u,\"effectiveAlpha\":%d,\"matrix\":[",
            count++?",":"",drawn?"true":"false",(local_flags&BF_ALT)?"true":"false",white?"true":"false",alpha,effective_alpha);
        for(int i=0;i<12;i++) printf("%s%.9g",i?",":"",captured[i]);
        printf("]}");
    }
    puts("]"); return 0;
}
int main(int argc,char **argv) {
    if(argc!=2) return 2;
    if(!strcmp(argv[1],"normal")) return trace(0);
    if(!strcmp(argv[1],"suspended")) return trace(1);
    if(!strcmp(argv[1],"draw")) return draws();
    return 2;
}
