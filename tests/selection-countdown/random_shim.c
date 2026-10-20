/* Authored logical service bindings, never an original controller copy. */
#define _GNU_SOURCE
#include <stdio.h>
#include <stdlib.h>
#include <stdint.h>
#include <string.h>
#include <ucontext.h>
#include <sys/mman.h>
/* ORIGINAL_DEFINES */
#define sleep source_sleep
#define TRUE 1
#define FALSE 0
#define X_VAL 0
#define Y_VAL 1
#define CPLT_EXISTS 1
/* Symbolic, unrendered text/object operands; no native graphics constants claim. */
#define FONTID_BAST18 0
#define OF_NONE 0
#define PF_FLASHWHITE 1
#define HJUST_CENTER 1
#define VJUST_CENTER 2
typedef struct { const char *name; } play_t;
typedef struct { char *name; play_t *play_ptr; intptr_t aud_num; void *limb_ptrs[2]; } PLAYSTUF__SLOT;
typedef struct { PLAYSTUF__SLOT *slot_ptrs[9]; } PLAYSTUF__PAGE;
typedef struct { float x,y; int flags; } object_data;
typedef struct { uintptr_t decal[1]; int ratio[1]; void *limb_obj; object_data odata; int move; } obj3d2_data_t;
typedef struct { float x,y; int id; } sprite_info_t;
typedef struct { struct { uint32_t color; } state; } ostring_t;
typedef struct process_node {
    struct process_node *next,*prev;
    short process_sleep_time;
    int process_id,serial,birth,alive,args[4];
    void (*entry)(int *);
    ucontext_t context;
    unsigned char stack[65536];
} process_node_t;
static struct { int off_side,off_flip,game_quarter,game_time,los,down,first_down,play_type,captain,team[2],has_custom[4]; play_t *team_play[2]; } game_info;
static int tick,serial,tsec=57,four_plr_ver,p_status,exit_status,force_selection,show_freept_idiot_box,show_plysel_idiot_box;
static int pup_always_qb,pup_always_receiver,pup_playbook,pup_secret_page,pup_superfg,pup_nopunt,hidden_off_cursor;
static int raw_input,end_tick,caller_done=-1,scores[2],suspend_at,resume_at,joy_input[1024][4],button_input[1024][4];
static int draws[4096][5],draw_count,sounds[4096][6],sound_count,updates[4096][5],update_count;
static struct { int tick,side,page,slot; const char *play; } choices[32],cpus[32];
static int choice_count,cpu_count;
static int flip_state[1024];
static process_node_t *plist,*cur_proc,*nodes[256];
static process_node_t *pflasher;
static ostring_t *pmsg[4];
static ucontext_t dispatcher;
static char *cursor_bnk_str="cursors";
static obj3d2_data_t cursor1,cursor2;
static float playsel_plays_xys[2][9][2],plysel_text_xys[6][2][2];
static int cursor_limb_storage,cursor_limb_ptrs[2],arrow_imgs[4];
typedef struct { uintptr_t texture_handle; struct { struct { int aspect_ratio; } header; } texture_info; } decal_t;
static decal_t decal,*psel_decals[16];
static PLAYSTUF__PAGE current_pages[2][5];
static struct { int flags; } custom_plays[4][9];
/* ORIGINAL_DECLARATIONS */
/* ORIGINAL_TABLES */
/* ORIGINAL_SOUNDS */
static void fail(const char *s) { fprintf(stderr,"Original cabinet binding: %s\n",s);exit(9); }
static int side(void) { return cur_proc->process_id==P1_PLAY_SEL_PID?0:cur_proc->process_id==P2_PLAY_SEL_PID?1:-1; }
static void remove_node(process_node_t *p) {
    if(!p||!p->alive)return;
    if(p->prev)p->prev->next=p->next;else plist=p->next;
    if(p->next)p->next->prev=p->prev;
    p->alive=0;
}
static void die(int unused) { remove_node(cur_proc);setcontext(&dispatcher);abort(); }
static void shell(int id) { nodes[id]->entry(nodes[id]->args);die(0); }
static process_node_t *create_process(char *name,int id,int *args,void (*entry)(int *),int extra_stack) {
    process_node_t *p=calloc(1,sizeof(*p));if(!p||serial>=255)fail("process capacity");
    if(extra_stack!=0)fail("unmodeled extra process stack");
    p->serial=++serial;p->alive=1;p->birth=tick;p->process_id=id;p->process_sleep_time=1;p->entry=entry;
    for(int i=0;i<4;i++)p->args[i]=args[i];
    p->next=plist;if(plist)plist->prev=p;plist=p;nodes[serial]=p;
    getcontext(&p->context);p->context.uc_stack.ss_sp=p->stack;p->context.uc_stack.ss_size=sizeof(p->stack);
    p->context.uc_link=&dispatcher;makecontext(&p->context,(void (*)(void))shell,1,serial);return p;
}
/* ORIGINAL_QCREATE */
static void source_sleep(int count) {
    if(!cur_proc||count<1||count>32767)fail("sleep operands");
    cur_proc->process_sleep_time=count;if(swapcontext(&cur_proc->context,&dispatcher))fail("sleep context");
}
static void kill(process_node_t *p,int unused) { remove_node(p); }
static void killall(int id,int mask) {
    for(process_node_t *p=plist,*next;p;p=next) { next=p->next;if((p->process_id&mask)==id)remove_node(p); }
}
/* ORIGINAL_PROCESS */
static void visit(process_node_t *p) {
    if(!p->alive||p->birth>=tick||p->process_sleep_time<=0||--p->process_sleep_time)return;
    cur_proc=p;if(swapcontext(&dispatcher,&p->context))fail("dispatch context");cur_proc=NULL;
}
static int randrng(int bound) {
    if(draw_count>=4096||bound<=0)fail("draw operands");
    int value=raw_input%bound;
    int *r=draws[draw_count++];r[0]=tick;r[1]=side();r[2]=bound;r[3]=raw_input;r[4]=value;return value;
}
static void snd_scall_bank(char *bank,int playlist,int volume,int pan,int priority) {
    if(strcmp(bank,"cursors")||volume!=VOLUME4||pan!=127)fail("sound operands");
    if(side()<0)return;
    if(sound_count>=4096)fail("sound capacity");
    int *r=sounds[sound_count++];r[0]=tick;r[1]=side();r[2]=playlist;r[3]=volume;r[4]=pan;r[5]=priority;
}
static int get_joy_val_cur(int player) { if(player<0||player>3)fail("captain player");return joy_input[tick][player]; }
static int get_but_val_down(int player) { if(player<0||player>3)fail("button player");return button_input[tick][player]; }
static void update_play_page(int team,int page,int flip,int slot) {
    if(update_count>=4096)fail("page capacity");
    int *r=updates[update_count++];r[0]=tick;r[1]=team;r[2]=page;r[3]=flip;r[4]=slot;
}
static void hide_multiple_objects(int id,int mask) {}
static void hide_sprite(sprite_info_t *sprite) {}
static void generate_sprite_verts(sprite_info_t *sprite) {}
static sprite_info_t *beginobj(int image,float x,float y,float z,int texture) { static sprite_info_t sprites[2];return &sprites[side()]; }
static void cursor_draw_function(void *unused) {}
static void create_object(char *name,int id,int flags,int order,void *object,void (*draw)(void *)) {}
static void convert_play(void *record) { fail("unavailable original custom record/decompressor"); }
static int compute_score(int team) { return scores[team]; }
static void set_string_id(int id) {}
static void set_text_position(float x,float y,float z) {}
static void set_text_font(int font) {}
static void set_text_color(int color) {}
static ostring_t *oprintf(const char *format,int justify,int number) {
    ostring_t *p=mmap(NULL,4096,PROT_READ|PROT_WRITE,MAP_PRIVATE|MAP_ANONYMOUS|MAP_32BIT,-1,0);
    if(p==MAP_FAILED||(uintptr_t)p>INT32_MAX)fail("logical text handle");p->state.color=LT_RED;return p;
}
static void change_string_cc(ostring_t *text,int color) {}
static void delete_multiple_strings(int id,int mask) {}
static int off_captain(void);
static void play_select_cntrl(int *args);
static void flash_text_proc(int *args);
static void timedown_play_select(int *args);
static play_t *PlaySel__MakeChoice(int,int,int,int,int,int);
static void PlaySel__ChoosePlay(int,int,int,int,int,int);
static PLAYSTUF__SLOT *original_get_slot(int,int,int);
static void original_cpu_pick_play(int);
static PLAYSTUF__SLOT *PlayStuf__GetSlot(int book,int page,int slot) {
    PLAYSTUF__SLOT *result=original_get_slot(book,page,slot);
    if(!result||!result->play_ptr)fail("unavailable original selected slot");
    if(choice_count>=32)fail("choice capacity");
    choices[choice_count].tick=tick;choices[choice_count].side=side();
    choices[choice_count].page=page;choices[choice_count].slot=slot;choices[choice_count++].play=result->play_ptr->name;return result;
}
static void cpu_pick_play(int team) {
    original_cpu_pick_play(team);
    cpus[cpu_count].tick=tick;cpus[cpu_count].side=team;cpus[cpu_count++].play=game_info.team_play[team]->name;
}
/* ORIGINAL_BODIES */
static void caller(int *args) {
    int rnd_wait=1;
    PlayStuf__SetupCurrentPlayPages(TRUE,TRUE);
    qcreate("timer",TIMER_PID,timedown_play_select,9,0,0,0);
    /* ORIGINAL_SPAWN */
    /* ORIGINAL_WAIT */
    caller_done=tick;
    killall(TIMER_PID,-1);
    /* ORIGINAL_TEARDOWN */
}
static void matrix(const char *name,int *rows,int count,int width) {
    printf("\"%s\":[",name);
    for(int i=0;i<count;i++) { if(i)putchar(',');putchar('[');
        for(int j=0;j<width;j++)printf("%s%d",j?",":"",rows[i*width+j]);putchar(']'); }
    putchar(']');
}
int main(void) {
    if(scanf("%d%d%d%d%d%d%d%d%d%d%d%d%d%d%d%d%d%d",&raw_input,&four_plr_ver,&game_info.captain,&p_status,
        &game_info.off_side,&game_info.team[0],&game_info.team[1],&game_info.los,&game_info.down,
        &game_info.game_quarter,&game_info.game_time,&game_info.play_type,&end_tick,
        &game_info.first_down,&scores[0],&scores[1],&suspend_at,&resume_at)!=18||end_tick>=1024)fail("input header");
    game_info.team_play[0]=game_info.team_play[1]=(play_t *)-1;
    for(int t=1;t<=end_tick;t++)for(int p=0;p<4;p++)
        if(scanf("%d%d",&joy_input[t][p],&button_input[t][p])!=2)fail("input frames");
    for(int i=0;i<16;i++)psel_decals[i]=&decal;
    for(int s=0;s<2;s++)for(int i=0;i<9;i++)playsel_plays_xys[s][i][0]=playsel_plays_xys[s][i][1]=i;
    tick=-1;process_node_t *parent=qcreate("game_proc",0x10,caller,0,0,0,0);tick=0;visit(parent);
    for(tick=1;tick<=end_tick;tick++) {
        if(tick==suspend_at||tick==resume_at)
            for(process_node_t *p=plist;p;p=p->next)
                if(p->process_id==0x10||p->process_id==P1_PLAY_SEL_PID||p->process_id==P2_PLAY_SEL_PID) {
                    if(tick==suspend_at)suspend_process(p);else resume_process(p);
                }
        for(process_node_t *p=plist,*next;p;p=next) { next=p->next;visit(p); }
        flip_state[tick]=game_info.off_flip;
    }
    putchar('{');matrix("draws",&draws[0][0],draw_count,5);putchar(',');
    matrix("sounds",&sounds[0][0],sound_count,6);putchar(',');
    matrix("updates",&updates[0][0],update_count,5);
    printf(",\"choices\":[");
    for(int i=0;i<choice_count;i++)printf("%s[%d,%d,%d,%d,\"%s\"]",i?",":"",
        choices[i].tick,choices[i].side,choices[i].page,choices[i].slot,choices[i].play);
    printf("],\"cpus\":[");
    for(int i=0;i<cpu_count;i++)printf("%s[%d,%d,\"%s\"]",i?",":"",cpus[i].tick,cpus[i].side,cpus[i].play);
    printf("],\"callerDone\":%d,\"offenseFlipped\":%d,\"flipState\":[",caller_done,game_info.off_flip);
    for(int t=1;t<=end_tick;t++)printf("%s%d",t>1?",":"",flip_state[t]);
    printf("]}\n");return 0;
}
