/* Authored prescribed-input harness, not upstream gameplay or target ABI. */
#include <stdio.h>
#include <stdlib.h>
#include <stdint.h>
#include <math.h>
#include <string.h>
#include <assert.h>
#define FALSE 0
#define TRUE 1
#define GM_IN_PLAY 3
#define PF_POP_HELMET 0x00800000
#define MODE_WPN 7
#define JOB_BLOCK 3
#define ISDRONE(p) ((p)->station == -1)
#define P_SWITCH_MASK 255
typedef struct { float x,y,z; int plyrmode,flags; } obj_3d;
typedef struct { obj_3d odata; int team,station,job,plyrnum; } fbplyr_data;
fbplyr_data player_blocks[14];
struct { int game_mode,ball_carrier,off_side,down; } game_info;
float pr_sort_mode_x;
uint32_t switches;
int raw,calls;
int randrng(int bound) { calls++;return raw%bound; }
uint32_t get_player_49way_current(void) { return switches; }
static int target_cmp(const void*,const void*);
/* RUNTIME_ORIGINAL_CALLERS */
static uint32_t fixture_seed=0x63981;
static unsigned fixture(void) { fixture_seed=fixture_seed*1664525u+1013904223u;return fixture_seed; }
int main(int argc,char **argv) {
 if(argc!=2)return 2;
 if(!strcmp(argv[1],"sentinel")) { printf("%d\n",original_touchdown_down());return 0; }
 if(!strcmp(argv[1],"gate")) {
  for(int play=0;play<2;play++)for(int helmet=0;helmet<2;helmet++)
  for(int carrier=0;carrier<2;carrier++)for(int roll=0;roll<100;roll++) {
   game_info.game_mode=play?GM_IN_PLAY:0;
   game_info.ball_carrier=carrier?4:3;
   fbplyr_data actor={0};actor.odata.flags=helmet?PF_POP_HELMET:0;actor.plyrnum=4;
   raw=roll;calls=0;
   int result=plyr_have_ball(&actor.odata);
   printf("%d\t%d\t%d\t%d\t%d\t%d\n",play,helmet,carrier,roll,result,calls);
  }
  return 0;
 }
 if(strcmp(argv[1],"receiver"))return 2;
 for(int side=0;side<2;side++)for(int layout=0;layout<128;layout++) {
  int qb=side*7+3;game_info.off_side=side;
  for(int i=0;i<14;i++) {
   player_blocks[i].team=i/7;player_blocks[i].station=-1;player_blocks[i].job=0;
   player_blocks[i].odata.plyrmode=0;player_blocks[i].plyrnum=i;
  }
  player_blocks[qb].station=3;player_blocks[qb].odata.z=((layout%5)-2)*8.0f;
  for(int j=0;j<3;j++) {
   fbplyr_data *p=player_blocks+qb+1+j;
   p->odata.x=((int)(fixture()%17)-8)*8.0f;p->odata.z=((int)(fixture()%9)-4)*16.0f;
   if(layout%7==0)p->odata.z=(j-1)*0.0625f;
   p->odata.plyrmode=fixture()%4?MODE_WPN:9;
   p->job=fixture()%3?0:JOB_BLOCK;p->station=fixture()%2?-1:j;
  }
  int ties=0;
  for(int j=0;j<3;j++)for(int k=j+1;k<3;k++) {
   fbplyr_data *a=player_blocks+qb+1+j,*b=player_blocks+qb+1+k;
   if(a->odata.plyrmode!=MODE_WPN || b->odata.plyrmode!=MODE_WPN ||
      (a->job==JOB_BLOCK && a->station<0) || (b->job==JOB_BLOCK && b->station<0))continue;
   float f=side?-1.0f:1.0f;
   if((int)(f*a->odata.z*10.0f)==(int)(f*b->odata.z*10.0f))ties=1;
  }
  for(int stick=0;stick<49;stick++) {
   switches=(uint32_t)stick<<24;
   int result=pick_receiver(&player_blocks[qb].odata);
   printf("%d\t%d\t%.9g",side,stick,player_blocks[qb].odata.z);
   for(int j=0;j<3;j++) {
    fbplyr_data *p=player_blocks+qb+1+j;
    printf("\t%.9g\t%.9g\t%d\t%d\t%d",p->odata.x,p->odata.z,p->odata.plyrmode,p->job,p->station);
   }
   printf("\t%d\t%d\n",result,ties);
  }
 }
 return 0;
}
