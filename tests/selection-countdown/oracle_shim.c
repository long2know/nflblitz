/* Authored bindings. Original bodies are extracted only into ignored test output. */
#define _GNU_SOURCE
#include <stdio.h>
#include <stdlib.h>
#include <stdint.h>
#include <string.h>
#include <ucontext.h>
#include <sys/mman.h>
/* ORIGINAL_DEFINES */
#define sleep source_sleep
#define FONTID_BAST18 "bast18"
#define HJUST_CENTER 1
#define VJUST_CENTER 2
#define X_VAL 0
#define Y_VAL 1
#define CPLT_EXISTS 1
#define TRUE 1
#define FALSE 0

typedef struct process_node {
    struct process_node *next, *prev;
    short process_sleep_time;
    int process_id, serial, birth, alive, args[4];
    void (*entry)(int *);
    ucontext_t context;
    unsigned char stack[65536];
} process_node_t;
typedef struct { struct { uint32_t color; } state; int number; } ostring_t;
static process_node_t *plist, *cur_proc, *pflasher, *nodes[256];
static ucontext_t dispatcher;
static ostring_t *pmsg[4], *text;
static int tick, serial, force_selection, show_plysel_idiot_box, tsec=57;
static int visible, current_number, string_id, font_ok;
static char *cursor_bnk_str="cursors";
static struct { const char *op; int id; int64_t value; } operations[128];
static int operation_count;
static void fail(const char *s) { fprintf(stderr,"Original selection binding: %s\n",s); exit(9); }
static void record(const char *op,int id,int64_t value) {
    if(operation_count>=128)fail("operation overflow");
    operations[operation_count].op=op;operations[operation_count].id=id;
    operations[operation_count++].value=value;
}
static void remove_node(process_node_t *p) {
    if(!p || !p->alive)return;
    if(p->prev)p->prev->next=p->next;else plist=p->next;
    if(p->next)p->next->prev=p->prev;
    p->alive=0;record("Die",p->process_id,0);
}
static void die(int unused) {
    if(cur_proc->process_id==TIMER_PID && force_selection)record("TimerForce",TIMER_PID,1);
    remove_node(cur_proc);setcontext(&dispatcher);abort();
}
static void shell(int id) { process_node_t *p=nodes[id];p->entry(p->args);die(0); }
static process_node_t *qcreate(char *name,int id,void (*entry)(int *),int a,int b,int c,int d) {
    process_node_t *p=calloc(1,sizeof(*p));
    if(!p || serial>=255)fail("process allocation");
    p->serial=++serial;p->alive=1;p->birth=tick;p->process_id=id;p->process_sleep_time=1;p->entry=entry;
    p->args[0]=a;p->args[1]=b;p->args[2]=c;p->args[3]=d;
    p->next=plist;if(plist)plist->prev=p;plist=p;nodes[serial]=p;
    getcontext(&p->context);p->context.uc_stack.ss_sp=p->stack;p->context.uc_stack.ss_size=sizeof(p->stack);
    p->context.uc_link=&dispatcher;makecontext(&p->context,(void (*)(void))shell,1,serial);
    record("Create",id,1);return p;
}
static void source_sleep(int count) {
    if(!cur_proc || count<1 || count>32767)fail("sleep contract");
    record("Sleep",cur_proc->process_id,count);cur_proc->process_sleep_time=count;
    if(swapcontext(&cur_proc->context,&dispatcher))fail("sleep context");
}
static void kill(process_node_t *p,int unused) { remove_node(p); }
static void killall(int id,int mask) {
    process_node_t *p=plist;
    while(p) { process_node_t *next=p->next;if((p->process_id&mask)==id)remove_node(p);p=next; }
}
/* ORIGINAL_PROCESS */
static void visit(process_node_t *p) {
    if(!p->alive || p->birth>=tick || p->process_sleep_time<=0)return;
    if(--p->process_sleep_time)return;
    cur_proc=p;
    if(swapcontext(&dispatcher,&p->context))fail("dispatch context");
    cur_proc=NULL;
}
static void set_string_id(int id) { if(id!=TIMER_MSG_ID)fail("string id");string_id=id; }
static void set_text_position(float x,float y,float z) { if(x!=123 || y!=234 || z!=1)fail("text position operands"); }
static void set_text_font(const char *id) { if(strcmp(id,"bast18"))fail("font operand");font_ok=1; }
static void set_text_color(uint32_t color) { if(color!=LT_RED)fail("initial color"); }
static ostring_t *oprintf(const char *format,int justify,int number) {
    if(strcmp(format,"\033%dj%d") || justify!=(HJUST_CENTER|VJUST_CENTER) || !font_ok || string_id!=TIMER_MSG_ID) {
        fprintf(stderr,"text operands format=%s justify=%d expected=%d font=%d id=%d expectedId=%d\n",
            format,justify,HJUST_CENTER|VJUST_CENTER,font_ok,string_id,TIMER_MSG_ID);
        fail("text display operands");
    }
    text=mmap(NULL,4096,PROT_READ|PROT_WRITE,MAP_PRIVATE|MAP_ANONYMOUS|MAP_32BIT,-1,0);
    if(text==MAP_FAILED || (uintptr_t)text>INT32_MAX)fail("logical int32 text handle");
    text->state.color=LT_RED;text->number=number;visible=1;current_number=number;
    record("TimerDisplay",TIMER_MSG_ID,number);return text;
}
static void change_string_cc(ostring_t *s,uint32_t color) {
    if(!visible || s!=text)fail("stale text child");
    record("TimerColor",TIMER_MSG_ID,(int32_t)color);
}
static void delete_multiple_strings(int id,int mask) {
    if(id!=TIMER_MSG_ID || mask!=-1)fail("timer teardown operands");
    visible=0;record("TimerDeleteStrings",id,mask);
}
static int choice_mode;
static void snd_scall_bank(char *bank,int playlist,int volume,int pan,int priority) {
    if(strcmp(bank,"cursors") || volume!=VOLUME4 || pan!=127)fail("sound operands");
    if(!choice_mode) {
        if(playlist!=CLOCK_LOW_SND || priority!=PRIORITY_LVL3)fail("timer sound operands");
        record("TimerBeep",playlist,current_number);
    } else if(priority!=PRIORITY_LVL2)fail("choice priority");
}
static void flash_text_proc(int *args);
/* ORIGINAL_TIMER */
/* ORIGINAL_FLASH */
static void clobber_playsel(void) { /* ORIGINAL_CLOBBER_PREFIX */ }
static void caller_cleanup(void) { /* ORIGINAL_CLEANUP */ }
static struct { int game_quarter,off_side; void *team_play[2]; } game_info;
static int four_plr_ver;
static float plysel_text_xys[6][2][2]={ [5]={{123,234},{123,234}} };
static void caller_spawn(void) { /* ORIGINAL_SPAWN */ }

typedef struct { int id; } play_t;
typedef struct { play_t *play_ptr; } PLAYSTUF__SLOT;
static play_t actual_play={11}, preserved_play={12}, play_extra_point={13}, custom_play={14};
static struct { int flags; } custom_plays[4][9];
static int exit_status,show_freept_idiot_box,slot_side,slot_page,slot_index,slot_calls,random_calls,converted,pat_slot;
static PLAYSTUF__SLOT actual_slot;
static PLAYSTUF__SLOT *PlayStuf__GetSlot(int side,int page,int index) {
    slot_side=side;slot_page=page;slot_index=index;slot_calls++;
    actual_slot.play_ptr=pat_slot?&play_extra_point:&actual_play;return &actual_slot;
}
static int randrng(int bound) { if(bound!=9)fail("custom fallback randrng bound");random_calls++;return 6; }
static void convert_play(void *play) { converted++; }
/* ORIGINAL_CHOICE */
static void forced_controller(int side,int page,int p_num,int cur_pos,int tm_num) {
    /* ORIGINAL_FORCED_CONTROLLER_SUFFIX */
}
static int p_status,pup_noplaysel,cpu_count,cpu_sides[2];
static void cpu_pick_play(int side) { if(cpu_count>=2)fail("duplicate CPU caller");cpu_sides[cpu_count++]=side; }
static void PlayStuf__SetupCurrentPlayPages(int a,int b) { if(a!=TRUE || b!=FALSE)fail("audible pages"); }
static void no_human_bypass(void) { /* ORIGINAL_NO_HUMAN_BYPASS */ }
static int caller_done;
static void caller_wait(int *args) {
    int rnd_wait=1;
    /* ORIGINAL_CALLER_WAIT */
    caller_done=tick;
}
static void caller_cases(void) {
    choice_mode=1;putchar('[');
    for(int c=0;c<9;c++) {
        int side=c==6?DEFENSE:OFFENSE,page=c<3?(c==2?3:1):PLAYSTUF__OFF_PAGE_ID_CUSTOM,slot=4;
        if(c==6 || c==7)page=0;
        memset(custom_plays,0,sizeof(custom_plays));
        game_info.team_play[0]=(play_t *)-1;exit_status=1;show_freept_idiot_box=0;
        slot_calls=random_calls=converted=0;pat_slot=c==7;
        if(c==1)game_info.team_play[0]=&preserved_play;
        if(c==3)custom_plays[0][4].flags=CPLT_EXISTS;
        if(c==4)custom_plays[0][2].flags=CPLT_EXISTS;
        if(c==8)PlaySel__ChoosePlay(side,page,0,slot,0,0);
        else forced_controller(side,page,0,slot,0);
        int ok=0;
        switch(c) {
            case 0: case 2: case 6: ok=game_info.team_play[0]==&actual_play && slot_calls==1 &&
                slot_side==side && slot_page==page && slot_index==slot && !random_calls && !exit_status;break;
            case 1: ok=game_info.team_play[0]==&preserved_play && !slot_calls && !random_calls;break;
            case 3: case 4: ok=game_info.team_play[0]==&custom_play && converted==1 && !random_calls;break;
            case 5: ok=game_info.team_play[0]==&actual_play && random_calls==1 && slot_page==0 && slot_index==6;break;
            case 7: ok=game_info.team_play[0]==&play_extra_point && !exit_status && show_freept_idiot_box==1;break;
            case 8: ok=game_info.team_play[0]==(play_t *)-1 && !slot_calls && !random_calls && exit_status==1;break;
        }
        printf("%s{\"case\":%d,\"passed\":%s,\"slotCalls\":%d,\"randomCalls\":%d,\"converted\":%d}",
            c?",":"",c,ok?"true":"false",slot_calls,random_calls,converted);
    }
    game_info.off_side=1;no_human_bypass();
    printf(",{\"case\":9,\"passed\":%s,\"cpuFirst\":%d,\"cpuSecond\":%d}",
        cpu_count==2 && cpu_sides[0]==1 && cpu_sides[1]==0 && serial==0?"true":"false",cpu_sides[0],cpu_sides[1]);
    cpu_count=0;force_selection=0;exit_status=1;
    game_info.team_play[0]=&actual_play;game_info.team_play[1]=(play_t *)-1;
    qcreate("declared caller",0x10,caller_wait,0,0,0,0);
    for(tick=1;tick<=7;tick++) {
        if(tick==5)force_selection=1;
        process_node_t *p=plist;
        while(p) { process_node_t *next=p->next;visit(p);p=next; }
    }
    printf(",{\"case\":10,\"passed\":%s,\"forceTick\":5,\"finishTick\":%d}]\n",
        caller_done==6 && cpu_count==1 && cpu_sides[0]==1?"true":"false",caller_done);
}
static int word(int id) {
    for(process_node_t *p=plist;p;p=p->next)if(p->process_id==id)return (unsigned short)p->process_sleep_time;
    return -1;
}
static void dump(void) {
    printf("%s{\"tick\":%d,\"number\":%d,\"visible\":%s,\"color\":%u,\"force\":%s,\"timerWord\":%d,\"flashWord\":%d,\"operations\":[",
        tick==1?"":",",tick,force_selection?-1:current_number,visible?"true":"false",
        text?text->state.color:(uint32_t)LT_RED,force_selection?"true":"false",word(TIMER_PID),word(MESSAGE_PID));
    for(int i=0;i<operation_count;i++)printf("%s[\"%s\",%d,%lld]",i?",":"",operations[i].op,operations[i].id,(long long)operations[i].value);
    printf("]}");
}
int main(int argc,char **argv) {
    if(argc==2 && !strcmp(argv[1],"callers")) { caller_cases();return 0; }
    if(argc!=10)return 2;
    int initial=atoi(argv[1]),ticks=atoi(argv[2]),hold_start=atoi(argv[3]),hold_end=atoi(argv[4]),
        hold_value=atoi(argv[5]),suspend_at=atoi(argv[6]),resume_at=atoi(argv[7]),
        both=atoi(argv[8]),stop_at=atoi(argv[9]);
    game_info.game_quarter=initial==9?0:1;current_number=initial;caller_spawn();putchar('[');
    for(tick=1;tick<=ticks;tick++) {
        operation_count=0;
        show_plysel_idiot_box=tick>=hold_start && tick<hold_end?hold_value:0;
        if(tick==suspend_at)suspend_multiple_processes(both?0:TIMER_PID,both?0:-1);
        if(tick==resume_at)resume_all();
        if(tick==stop_at)caller_cleanup();
        process_node_t *list[256];int count=0;
        for(process_node_t *p=plist;p;p=p->next)list[count++]=p;
        for(int i=0;i<count;i++)visit(list[i]);
        dump();
    }
    puts("]");return 0;
}
