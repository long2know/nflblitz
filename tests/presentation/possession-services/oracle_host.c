/* Authored host bindings only. Original functions/branch/table are injected in ignored output. */
#define _GNU_SOURCE
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <stdint.h>
#include <ucontext.h>
#include <sys/mman.h>

/* ORIGINAL_DEFINES */
#undef SPRITE_HRES
#undef SPRITE_VRES
#define SPRITE_HRES 512
#define SPRITE_VRES 384
#define GR_COLORCOMBINE_ONE 1
#define GR_CHROMAKEY_ENABLE 1
#define GR_CHROMAKEY_DISABLE 0
#define DIO_MODE_NO_BLOCK 1
#define DIO_MODE_BLOCK 0
#define CREATE_NORMAL_TEXTURE 0
#define GR_TEXTURECLAMP_CLAMP 0
#define GR_TEXTUREFILTER_BILINEAR 0
#define TEX_LOAD_FAIL_AUD 0
#define sleep source_sleep

typedef struct process_node {
    struct process_node *next, *prev;
    short process_sleep_time;
    int process_id, serial, birth, alive;
    void (*entry)(int *);
    int args[4];
    ucontext_t context;
    unsigned char stack[65536];
} process_node_t;
static process_node_t *plist, *cur_proc, *nodes[512];
static ucontext_t dispatcher;
static int source_tick, next_serial=32, completions, *timeout_witness;
typedef struct { const char *name; } image_info_t;
typedef struct { int color_combiner_function, chroma_key_mode, constant_color; } sprite_state;
typedef struct { int id, alive, serial; sprite_state state; } sprite_info_t;
typedef struct { int unused; } texture_node_t;
static sprite_info_t *sprites[32];
static int sprite_count, current_message;
static char *message_tex[16], *ancr_swaped_bnk_str="declared-actual-Core-bank";
static struct { int off_prev; } cam;
typedef struct { int game_mode,last_pos,off_side,game_flags,game_state,play_result,off_turnover; } game_info_t;
typedef struct { int type,flags; } ball_data;
static game_info_t game_info;
static ball_data ball_obj;
static int speech_ordinal;
static struct { const char *operation; int id,value; } operations[256];
static int operation_count;
static void unavailable(const char *reason) { fprintf(stderr,"Unsupported selected C operation: %s\n",reason); exit(9); }
static void record(const char *operation,int id,int value) {
    if(source_tick && operation_count<256) {
        operations[operation_count].operation=operation;
        operations[operation_count].id=id;operations[operation_count++].value=value;
    }
}
static void source_sleep(int ticks);
static void die(int cause);
static void show_message_proc(int *args);
static void flash_obj_proc(int *args);
static void flash_obj_white(sprite_info_t *obj,int count,int delay);
static void intercepted_ball_sp(void);
static process_node_t *qcreate(char *name,int id,void (*entry)(int *),int a,int b,int c,int d);

/* ORIGINAL_PROCESS */
static void suspend_multiple_processes(int id,int mask) {
    record("SuspendMask",id,mask);original_suspend_multiple_processes(id,mask);
}
static void resume_multiple_processes(int id,int mask) {
    record("ResumeMask",id,mask);original_resume_multiple_processes(id,mask);
}
static void resume_all(void) { record("ResumeAllCall",0,0);original_resume_all(); }
static process_node_t *existp(int id,int mask) { return original_existp(id,mask); }

static void remove_node(process_node_t *p) {
    if(!p || !p->alive)return;
    if(p->prev)p->prev->next=p->next;else plist=p->next;
    if(p->next)p->next->prev=p->prev;
    p->alive=0;
}
static void die(int cause) {
    remove_node(cur_proc);
    if(setcontext(&dispatcher))unavailable("setcontext-die");
    abort();
}
static void shell(int serial) {
    process_node_t *p=nodes[serial];p->entry(p->args);die(0);
}
static process_node_t *create_node(int serial,int id,void (*entry)(int *),int a,int b,int c,int d,int delay) {
    process_node_t *p=calloc(1,sizeof(*p));
    if(!p || serial>=512)unavailable("declared process allocation");
    p->serial=serial;p->birth=source_tick;p->alive=1;p->process_id=id;p->entry=entry;p->process_sleep_time=delay;
    p->args[0]=a;p->args[1]=b;p->args[2]=c;p->args[3]=d;
    p->next=plist;if(plist)plist->prev=p;plist=p;nodes[serial]=p;
    if(getcontext(&p->context))unavailable("getcontext");
    p->context.uc_stack.ss_sp=p->stack;p->context.uc_stack.ss_size=sizeof(p->stack);p->context.uc_link=&dispatcher;
    makecontext(&p->context,(void (*)(void))shell,1,serial);
    record("Create",id,delay);
    return p;
}
static process_node_t *qcreate(char *name,int id,void (*entry)(int *),int a,int b,int c,int d) {
    return create_node(++next_serial,id,entry,a,b,c,d,1);
}
static void source_sleep(int ticks) {
    if(!cur_proc || ticks<1 || ticks>32767)unavailable("sleep caller/range");
    if(ticks!=1)record("Sleep",cur_proc->process_id,ticks);
    cur_proc->process_sleep_time=(short)ticks;
    if(swapcontext(&cur_proc->context,&dispatcher))unavailable("swapcontext-sleep");
}
static int sprite_exists(sprite_info_t *p) { return p && p->alive; }
static sprite_info_t *beginobj(image_info_t *image,float x,float y,float z,int tid) {
    sprite_info_t *p=mmap(NULL,4096,PROT_READ|PROT_WRITE,MAP_PRIVATE|MAP_ANONYMOUS|MAP_32BIT,-1,0);
    if(p==MAP_FAILED || (uintptr_t)p>INT32_MAX || sprite_count>=32)unavailable("low-address logical sprite handle");
    p->alive=1;p->serial=++next_serial;p->state.constant_color=-1;sprites[sprite_count++]=p;
    record("BeginSprite",current_message==MS_INTERCEPTION ? 0x5678FACE:OID_MESSAGE,91);
    return p;
}
static void del1c(int id,int mask) {
    for(int i=0;i<sprite_count;i++)if((sprites[i]->id&mask)==id)sprites[i]->alive=0;
    record("DeleteSprites",id,mask);
}
static int texture_alive=1;
static void delete_multiple_textures(int id,int mask) { texture_alive=0;record("DeleteTextures",id,mask); }
static texture_node_t *create_texture(char *name,...) { unavailable("unselected disk-load message");return NULL; }
static void dio_mode(int mode) { unavailable("unselected disk mode"); }
static void increment_audit(int which) { unavailable("unselected disk audit"); }
static int randrng(int count) {
    if(count!=5 || speech_ordinal<0 || speech_ordinal>=count)unavailable("actual supplied randrng(5)");
    return speech_ordinal;
}
static void snd_scall_bank(char *bank,int id,int volume,int pan,int priority) {
    if(!bank || volume!=255 || pan!=127)unavailable("speech parameters");
    record("Speech",id,priority);
}

/* ORIGINAL_IMAGES */
/* ORIGINAL_SPEECH */
/* ORIGINAL_MESSAGE */
static void show_message_proc(int *args) {
    current_message=args[0];texture_alive=1;record("Preloaded",MESSAGE_TID,current_message);
    original_show_message_proc(args);
}
/* ORIGINAL_FLASH */

static void camera_proc(int *args) {
    for(;;) {
        game_info_t *pgi=&game_info;
        ball_data *pball=&ball_obj;
        typeof(cam) *pcam=&cam;
        /* ORIGINAL_POSSESSION_BRANCH */
        completions++;
        sleep(1);
    }
}
static void counter_proc(int *args) { for(;;)sleep(1); }
static void visit(int serial) {
    process_node_t *p=nodes[serial];
    if(!p || !p->alive || p->birth>=source_tick || p->process_sleep_time<=0)return;
    p->process_sleep_time--;
    if(p->process_sleep_time)return;
    cur_proc=p;
    if(swapcontext(&dispatcher,&p->context))unavailable("dispatch");
    cur_proc=NULL;
}
static void dump(int comma) {
    printf("%s{\"tick\":%d,\"previous\":%d,\"timeout\":%d,\"completions\":%d,\"words\":[",
        comma?",":"",source_tick,cam.off_prev,timeout_witness?*timeout_witness:0,completions);
    int n=0;
    for(int i=1;i<512;i++)if(nodes[i]&&nodes[i]->alive)
        printf("%s[%d,%d,%u]",n++?",":"",i,nodes[i]->process_id,(unsigned short)nodes[i]->process_sleep_time);
    printf("],\"sprites\":[");n=0;
    for(int i=0;i<sprite_count;i++)if(sprites[i]->alive) {
        sprite_info_t *p=sprites[i];
        printf("%s[%d,%d,%d,%d]",n++?",":"",p->serial,p->id,p->state.color_combiner_function==GR_COLORCOMBINE_ONE,
            p->state.chroma_key_mode==GR_CHROMAKEY_ENABLE);
    }
    printf("],\"operations\":[");
    for(int i=0;i<operation_count;i++)printf("%s[\"%s\",%d,%d]",i?",":"",operations[i].operation,operations[i].id,operations[i].value);
    printf("]}");
}
int main(int argc,char **argv) {
    if(argc!=17)return 2;
    int mode=atoi(argv[1]),type=atoi(argv[2]),bf=atoi(argv[3]),gf=atoi(argv[4]),result=atoi(argv[5]),
        off=atoi(argv[6]),last=atoi(argv[7]),turn=atoi(argv[8]),state=atoi(argv[9]),ticks=atoi(argv[10]);
    speech_ordinal=atoi(argv[11]);
    int kill_at=atoi(argv[12]),suspend_at=atoi(argv[13]),menu_at=atoi(argv[14]),resume_at=atoi(argv[15]),second_at=atoi(argv[16]);
    int ids[]={0,0x10,0x500,0x500,0x500,0x500,0x500,0x500,0x500,0x500,0x500,0x500,0x500,0x500,0x500,0x500,
        0x900,0x1400,0xf00,0x501,0x502,0xf80,0xf81,0x700,0xa00,0xf98,0x8001,0xf82,0xf99};
    for(int i=1;i<=28;i++)create_node(i,ids[i],i==17?camera_proc:counter_proc,0,0,0,0,i==28?7:1);
    static int before[]={25,26,27,28,1,18,21,23};
    static int after[]={19,2,3,4,5,6,7,8,9,10,11,12,13,14,15,20,22,24,16};
    putchar('[');
    for(source_tick=1;source_tick<=ticks;source_tick++) {
        operation_count=0;
        game_info.game_mode=source_tick==1?GM_PRE_SNAP:mode;
        if(menu_at && source_tick>=menu_at)game_info.game_mode=GM_PRE_SNAP;
        game_info.last_pos=source_tick==1?0:last;game_info.off_side=source_tick==1?0:off;
        if(second_at && source_tick>=second_at) { game_info.last_pos=!last;game_info.off_side=!off; }
        game_info.game_flags=source_tick==1?0:gf;game_info.play_result=source_tick==1?0:result;
        game_info.off_turnover=source_tick==1?0:turn;game_info.game_state=state;
        ball_obj.type=type;ball_obj.flags=bf;
        if(source_tick==kill_at)remove_node(existp(MSG_CAMBOT_PID,-1));
        if(source_tick==suspend_at)original_suspend_multiple_processes(MSG_CAMBOT_PID,-1);
        if(source_tick==resume_at)original_resume_all();
        int dynamic[64],count=0;
        for(process_node_t *p=plist;p;p=p->next)if(p->serial>32)dynamic[count++]=p->serial;
        for(int i=0;i<count;i++)visit(dynamic[i]);
        for(unsigned i=0;i<sizeof(before)/sizeof(*before);i++)visit(before[i]);
        visit(17);
        for(unsigned i=0;i<sizeof(after)/sizeof(*after);i++)visit(after[i]);
        dump(source_tick!=1);
    }
    puts("]");
    return 0;
}
