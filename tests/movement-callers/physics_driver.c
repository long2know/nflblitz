/* Authored host projection and observations; original active physics/bounds inserted at runtime. */
#include <stdint.h>
#include <stdio.h>
#include <math.h>
#define MODE_PUPPET 0x800000
#define MODE_UNINT 2
#define MODE_WPN 7
#define SEQ_SPIN 1
#define GM_IN_PLAY 2
#define PF_INBND 0x80
#define FIELDW 182.85f
#define FIELDHB 468.0f
#define GRAVITY -0.08f
#define TRUE 1
#define FALSE 0
#define TEAM_MASK(x) (1<<(x))
#define LESSER(a,b) ((a)<(b)?(a):(b))
#define GREATER(a,b) ((a)>(b)?(a):(b))
#define fsqrt sqrtf
typedef struct {int animode,seq_index;} anidata;
typedef struct {float x,y,z,vx,vy,vz,friction;int flags,plyrmode;anidata adata[1];} obj_3d;
typedef struct {obj_3d odata;int plyrnum,team;} fbplyr_data;
static struct {int game_mode;} game_info;
static int pup_runob,pup_field;
static float field_friction_mult_factor[1];
static void abort_drone_route(int p) {(void)p;}
static float frombits(uint32_t value){union{uint32_t u;float f;}v={.u=value};return v.f;}
static uint32_t bits(float value){union{uint32_t u;float f;}v={.f=value};return v.u;}
/* ORIGINAL_BOUNDS */
static void physics(fbplyr_data *ppdata) {
 obj_3d *pobj=&ppdata->odata;
 int needed_oob_correction;
 float mxz,frx,frz;
/* ORIGINAL_PLAYER_PHYSICS */
}
int main(void) {
 uint32_t v[22]; int cases=0;
 while(scanf("%u%u%u%u%u%u%u%u%u%u%u%u%u%u%u%u%u%u%u%u%u%u",
  v,v+1,v+2,v+3,v+4,v+5,v+6,v+7,v+8,v+9,v+10,v+11,v+12,v+13,v+14,v+15,v+16,v+17,v+18,v+19,v+20,v+21)==22) {
  fbplyr_data p={0};obj_3d *o=&p.odata;
  o->x=frombits(v[0]);o->y=frombits(v[1]);o->z=frombits(v[2]);
  o->vx=frombits(v[3]);o->vy=frombits(v[4]);o->vz=frombits(v[5]);o->friction=frombits(v[6]);
  o->adata[0].animode=v[7];o->flags=v[8];o->plyrmode=v[9];o->adata[0].seq_index=v[10]?SEQ_SPIN:0;
  game_info.game_mode=v[11]?GM_IN_PLAY:3;pup_runob=v[12];field_friction_mult_factor[0]=frombits(v[13]);
  physics(&p);
  uint32_t actual[8]={bits(o->x),bits(o->y),bits(o->z),bits(o->vx),bits(o->vy),bits(o->vz),bits(o->friction),(uint32_t)o->flags};
  for(int i=0;i<8;i++)if(actual[i]!=v[14+i]){
   fprintf(stderr,"actual Core post-VM case%d field%d C=%u CSharp=%u\n",cases,i,actual[i],v[14+i]);return 2;
  }
  cases++;
 }
 printf("Compared %d ACTUAL Core post-VM caller inputs / %d exact binary32/flag assertions against unchanged player_proc physics.\n",cases,cases*8);
 return cases?0:3;
}
