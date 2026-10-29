/* Executes the authored production wrapper, without game assets or generated code.
 * The _gen below is a deliberately limited retail toggle/latch branch stub;
 * it is not a native gameplay oracle. */
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <math.h>
#include <fenv.h>
#include <xmmintrin.h>
#include "pc_video.h"

#define RAM_SIZE 0x10000u
static unsigned char ram[RAM_SIZE];
static uintptr_t g_xbox_mem_offset;
static uint32_t g_eax, g_ebx, g_ecx, g_edx, g_esi, g_edi, g_esp, g_ebp;
#define GUEST8(a) (*(uint8_t *)(g_xbox_mem_offset+(a)))
#define GUEST32(a) (*(uint32_t *)(g_xbox_mem_offset+(a)))
#define CHECK(x) do { ++checks; if (!(x)) { fprintf(stderr,"FAIL %d: %s\n",__LINE__,#x); exit(1); } } while(0)
static unsigned checks, calls, entry_csr, mode_published;
static int selected_control, setting_hold, resolver_calls, entry_round;
static uint8_t entry_intent, entry_latch;
static uint32_t entry_regs[8];
static fenv_t expected_env, entry_env;
static int black_pc_ram_span(uint32_t address, uint32_t bytes) {
    return address >= 0x1000u && (uint64_t)address + bytes <= RAM_SIZE;
}
static int black_pc_input_resolver(int slot) {
    CHECK(slot == 16); ++resolver_calls; return selected_control;
}
void pc_video_get(PcVideoSettings *v) { memset(v,0,sizeof(*v)); v->aim_hold=(unsigned)setting_hold; fesetround(FE_DOWNWARD); _mm_setcsr(0x3f80u); }
void pc_input_set_aim_hold_mode(int enabled) { mode_published=(unsigned)enabled; }
void black_pc_movement(uint32_t self);
void black_pc_movement(uint32_t self) { (void)self; }
#include "aim.extracted.inc"

/* Model only the documented aim latch and toggle edge, not weapon/zoom/animation. */
void sub_001EC450_gen(void) {
    float value=0.0f;
    uint32_t input, decoded;
    ++calls; entry_csr=_mm_getcsr(); entry_round=fegetround();
    memset(&entry_env,0,sizeof(entry_env)); fegetenv(&entry_env);
    entry_regs[0]=g_eax; entry_regs[1]=g_ebx; entry_regs[2]=g_ecx; entry_regs[3]=g_edx;
    entry_regs[4]=g_esi; entry_regs[5]=g_edi; entry_regs[6]=g_esp; entry_regs[7]=g_ebp;
    if (!black_pc_ram_span(g_ecx,0x105u)) return;
    entry_intent=GUEST8(g_ecx+0x45u); entry_latch=GUEST8(g_ecx+0x104u);
    input=GUEST32(g_ecx+0xa8u);
    if (!black_pc_ram_span(input,0x10u)) return;
    decoded=GUEST32(input+0xcu);
    if (!black_pc_ram_span(decoded,0xb4u)) return;
    if (selected_control >= 0 && selected_control < 28)
        memcpy(&value,ram+decoded+0x44u+4u*(unsigned)selected_control,4);
    if (value >= 0.1f && value > 0.0f) {
        if (!GUEST8(g_ecx+0x104u)) GUEST8(g_ecx+0x45u)^=1;
        GUEST8(g_ecx+0x104u)=1;
    } else GUEST8(g_ecx+0x104u)=0;
}
static void init(void) {
    memset(ram,0,sizeof(ram)); g_xbox_mem_offset=(uintptr_t)ram;
    g_eax=1; g_ebx=2; g_ecx=0x2000; g_edx=4; g_esi=5; g_edi=6; g_esp=7; g_ebp=8;
    GUEST32(0x20a8)=0x3000; GUEST32(0x300c)=0x4000;
    selected_control=3; setting_hold=1; calls=resolver_calls=0; mode_published=99;
}
static void set_level(int control,float value) { memcpy(ram+0x4044+4*control,&value,4); }
static void run(void) {
    const uint32_t expected[]={1,2,g_ecx,4,5,6,7,8};
    unsigned i; int before_calls=(int)calls;
    fesetenv(FE_DFL_ENV); fesetround(FE_UPWARD); _mm_setcsr(0x5fa0u);
    memset(&expected_env,0,sizeof(expected_env)); fegetenv(&expected_env);
    sub_001EC450();
    CHECK(memcmp(&expected_env,&entry_env,sizeof(expected_env))==0);
    CHECK(calls==(unsigned)before_calls+1); CHECK(entry_csr==0x5fa0u); CHECK(entry_round==FE_UPWARD);
    for(i=0;i<8;i++) CHECK(entry_regs[i]==expected[i]);
    CHECK(mode_published==(unsigned)setting_hold);
    fesetenv(FE_DFL_ENV); _mm_setcsr(0x1f80u);
}
int main(void) {
    unsigned i; float boundary[]={0.0f,0.09999999f,0.1f,1.0f,-1.0f,NAN};
    int want[]={0,0,1,1,0,0};
    init(); setting_hold=0; GUEST8(0x2045)=1; GUEST8(0x2104)=0; set_level(3,1);
    run(); CHECK(entry_intent==1 && entry_latch==0 && resolver_calls==0);
    CHECK(GUEST8(0x2045)==0 && GUEST8(0x2104)==1); /* stub still receives untouched state */
    init(); set_level(3,1); run(); CHECK(entry_intent==1 && entry_latch==1 && GUEST8(0x2045)==1);
    run(); CHECK(GUEST8(0x2045)==1 && GUEST8(0x2104)==1);
    set_level(3,0); run(); CHECK(entry_intent==0 && entry_latch==0 && GUEST8(0x2045)==0);
    selected_control=7; set_level(3,1); set_level(7,0); run(); CHECK(GUEST8(0x2045)==0);
    set_level(7,1); run(); CHECK(GUEST8(0x2045)==1);
    selected_control=-1; run(); CHECK(GUEST8(0x2045)==0);
    selected_control=28; run(); CHECK(GUEST8(0x2045)==0);
    for(i=0;i<sizeof(boundary)/sizeof(boundary[0]);i++) {
        selected_control=3; set_level(3,boundary[i]); run();
        CHECK(entry_intent==want[i] && entry_latch==want[i]);
    }
    init(); GUEST8(0x2045)=9; GUEST8(0x2104)=8; GUEST32(0x20a8)=0; run(); CHECK(entry_intent==9 && entry_latch==8);
    GUEST32(0x20a8)=RAM_SIZE-15; run(); CHECK(entry_intent==9 && entry_latch==8);
    GUEST32(0x20a8)=0x3000; GUEST32(0x300c)=0xffffffffu; run(); CHECK(entry_intent==9 && entry_latch==8);
    GUEST32(0x300c)=RAM_SIZE-0xb3; run(); CHECK(entry_intent==9 && entry_latch==8);
    g_ecx=0xfffffff0u; run(); CHECK(resolver_calls==4); /* no resolver for invalid self */
    printf("aim wrapper: %u checks passed (retail branch stub only)\n",checks); return 0;
}
