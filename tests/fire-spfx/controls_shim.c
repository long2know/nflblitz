/* Source object/process functions on explicit owned-node fixtures, never target pointers/ABI. */
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#define NO_FREE_MEMORY 0
#define ALLOC_NUM 32
#define OF_HIDDEN 1
/* Only full(-1) kill masks are tested; OR with NODESTRUCT is then invariant for ANY target value. */
#define NODESTRUCT 0
#define PD_WIPEOUT 0
struct object_node {
    struct object_node *next,*prev;
    char object_name[12]; short object_id,object_flags; int draw_order;
    void (*object_draw_func)(void *); void *object_data;
};
static struct object_node *olist,*ofree;
static int resource_mode=NO_FREE_MEMORY;
static void insert_object(struct object_node *);
struct process_node { struct process_node *next,*prev; int process_id; short process_sleep_time; int serial; };
static struct process_node *plist,*cur_proc;
static struct process_node nodes[4];
static int object_owners[4]={1001,1002,1003,1004};
static void kill(struct process_node *p,int cause) {
    (void)cause;
    if(p->prev)p->prev->next=p->next; else plist=p->next;
    if(p->next)p->next->prev=p->prev;
}
/* ORIGINAL_OBJECT_FUNCTIONS */
/* ORIGINAL_PROCESS_FUNCTIONS */
static void new_process(int index,int group,int sleep) {
    struct process_node *p=&nodes[index]; p->serial=1001+index; p->process_id=group; p->process_sleep_time=(short)sleep;
    p->next=plist; if(plist)plist->prev=p; p->prev=NULL; plist=p;
}
static void draw_nothing(void *data) { (void)data; }
static void print_row(const char *stage,int comma) {
    printf("%s{\"stage\":\"%s\",\"processes\":[",comma?",":"",stage);
    int i=0;
    for(struct process_node *p=plist;p;p=p->next)
        printf("%s{\"serial\":%d,\"group\":%d,\"sleep\":%u}",i++?",":"",p->serial,p->process_id,(unsigned short)p->process_sleep_time);
    printf("],\"objects\":["); i=0;
    for(struct object_node *o=olist;o;o=o->next)
        printf("%s{\"owner\":%d,\"hidden\":%s}",i++?",":"",*(int *)o->object_data,(o->object_flags&OF_HIDDEN)?"true":"false");
    printf("]}");
}
int main(void) {
    new_process(0,0x0f98,1); new_process(1,0x0500,1); new_process(2,0x1300,3);
    create_object("puff",0x510,0,0x50001,&object_owners[1],draw_nothing);
    create_object("puff",0x510,0,0x50001,&object_owners[0],draw_nothing);
    printf("["); print_row("first-entries",0);
    hide_all_3d_objects(); print_row("hidden-existing",1);
    new_process(3,0x0f98,1); nodes[2].process_sleep_time=2;
    create_object("puff",0x510,0,0x50001,&object_owners[3],draw_nothing); print_row("new-equal-priority-not-hidden",1);
    suspend_multiple_processes(0x0f98,0); print_row("masked-pid-not-masked-id",1);
    unhide_all_3d_objects(); print_row("unhidden-existing",1);
    killall(0x0f98,-1); print_row("SPFX-kill-not-object-delete",1);
    killall(0x1300,-1); print_row("PFLASH-separate",1);
    delete_multiple_objects(0x510,-1); print_row("objects-deleted-PLAYER-remains",1);
    suspend_multiple_processes(0,0x8000); print_row("suspended-player-preserves-sleep",1);
    resume_multiple_processes(0x0f98,-1); print_row("SPFX-resume-does-not-resume-PLAYER",1);
    resume_all(); print_row("resume-all-preserves-sleep",1);
    cur_proc=&nodes[1]; killall(0x0500,-1); print_row("killall-excludes-caller",1);
    printf("]\n"); return 0;
}
