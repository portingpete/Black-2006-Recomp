#include "pc_movie_skip.h"
#include "pc_input.h"
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

static unsigned checks;
static int prompt;
static uint64_t clock_ms;
#define CHECK(x) do { ++checks; if (!(x)) { fprintf(stderr,"FAIL line %d: %s\n",__LINE__,#x); exit(1); } } while (0)
/* No game action-map bytes are needed: this fixture exercises movie ownership
 * and the input module's menu layer, never gameplay action resolution. */
static int no_gameplay_control(int slot) { (void)slot; return -1; }
void pc_menu_set_movie_skip_prompt(int visible) { prompt = visible; }
static void time_at(uint64_t now) { clock_ms = now; pc_movie_skip_test_time(now); pc_input_test_set_time(now); }
static void reset(void)
{
    _putenv_s("RECOMP_BLACK_INPUT_MODE", "auto");
    _putenv_s("RECOMP_INPUT_SCRIPT", "");
    _putenv_s("RECOMP_PAD_SCRIPT", "");
    pc_input_test_reset(); pc_input_set_control_resolver(no_gameplay_control);
    pc_input_set_filter_slot(0,NULL); pc_input_set_filter_slot(1,NULL);
    pc_movie_skip_test_reset(); pc_movie_skip_install();
    pc_input_set_movie_active_query(pc_movie_skip_active);
    time_at(100000);
}
static void test_enter_maps_to_xbox_a(void)
{
    uint8_t out[20] = {0,20};
    reset();
    pc_input_key_event(0x1C,0,1,0); /* physical Enter scancode */
    pc_input_apply_report(out,0);  /* no controller and no scripted presses */
    CHECK(out[4] == 0xFF);

    reset();
    memset(out,0,sizeof(out)); out[1]=20;
    pc_input_key_event(0x1C,1,1,0); /* extended keypad Enter */
    pc_input_apply_report(out,0);
    CHECK(out[4] == 0xFF);
    reset();
}
static void key(unsigned code,int down,int repeat) { pc_input_key_event(code,0,down,repeat); }
static void tap(unsigned code) { key(code,1,0); key(code,0,0); }
static void report(unsigned start,unsigned a,unsigned b)
{
    uint8_t out[20] = {0,20}; unsigned i;
    out[2]=(uint8_t)start; out[4]=(uint8_t)a; out[5]=(uint8_t)b;
    pc_input_apply_report(out,1);
    if(pc_movie_skip_active()) for(i=2;i<20;++i) CHECK(out[i]==0);
}
static int lower_keys;
static int lower_key(unsigned id,int down,int repeat) { (void)id;(void)down;(void)repeat;++lower_keys;return 1; }
static const PcInputFilter lower = { lower_key, NULL, NULL, NULL, NULL };

/* The movie consumes the release of a mouse button already held in an original
 * guest menu. Neither that hold nor its minimum-duration tap may return later. */
static void test_filtered_mouse_release(void)
{
    unsigned button, source, i;
    for (source = 0; source < 2; ++source) for (button = 0; button < 2; ++button) {
        uint8_t out[20] = {0,20};
        reset();
        if (source) {
            char script[96];
            snprintf(script,sizeof(script),"0:down:Mouse%u,100:up:Mouse%u",button+1,button+1);
            _putenv_s("RECOMP_INPUT_SCRIPT",script);
            pc_input_test_reset(); pc_movie_skip_install();
            pc_input_set_control_resolver(no_gameplay_control);
            pc_input_set_movie_active_query(pc_movie_skip_active);
            time_at(100000);
        } else pc_input_mouse_button_event((int)button,1);
        pc_input_apply_report(out,0);
        CHECK(out[4+button]==255); /* menu confirm/back before movie ownership */
        pc_movie_skip_update(1,PC_MOVIE_SKIP_CUTSCENE,1);
        if (source) {
            time_at(clock_ms+100);
            memset(out+2,0,18); pc_input_apply_report(out,0); /* scripted physical release */
        } else pc_input_mouse_button_event((int)button,0);
        pc_input_frame_complete(); pc_movie_skip_clear();
        time_at(clock_ms+1000);
        memset(out+2,0,18); pc_input_apply_report(out,0);
        for(i=2;i<20;++i) CHECK(out[i]==0);
        pc_input_mouse_button_event((int)button,1);
        memset(out+2,0,18); pc_input_apply_report(out,0);
        CHECK(out[4+button]==255); /* a fresh press belongs to the next menu */
    }
    reset();
}

/* A controller can continue to produce reports after the window loses focus.
 * Neither unfocused presses nor the held button on focus return may skip. */
static void test_unfocused_pad(void)
{
    unsigned kind, button;
    for (kind = PC_MOVIE_SKIP_INTRO; kind <= PC_MOVIE_SKIP_CUTSCENE; ++kind) {
        for (button = 0; button < 3; ++button) {
            const unsigned start = button == 0 ? 0x10u : 0u;
            const unsigned a = button == 1 ? 255u : 0u;
            const unsigned b = button == 2 ? 255u : 0u;
            reset(); pc_movie_skip_update(1, (int)kind, 1);
            pc_input_focus_event(0);
            report(0,0,0); report(start,a,b); report(0,0,0); report(start,a,b);
            CHECK(!prompt); CHECK(!pc_movie_skip_take_request(1));
            pc_input_focus_event(1); report(start,a,b);
            CHECK(!prompt); CHECK(!pc_movie_skip_take_request(1));
            report(0,0,0); report(start,a,b);
            if (kind == PC_MOVIE_SKIP_INTRO) CHECK(pc_movie_skip_take_request(1));
            else {
                CHECK(prompt); CHECK(!pc_movie_skip_take_request(1));
                report(0,0,0); report(start,a,b);
                CHECK(pc_movie_skip_take_request(1));
            }
        }
    }
}

int main(void)
{
    unsigned eligible[] = {0x01,0x1c,0x39}; unsigned i;
    for(i=0;i<3;++i) {
        reset(); pc_movie_skip_update(1,PC_MOVIE_SKIP_INTRO,1);
        tap(eligible[i]); CHECK(!prompt); CHECK(pc_movie_skip_take_request(1)); CHECK(!pc_movie_skip_take_request(1));
        reset(); pc_movie_skip_update(1,PC_MOVIE_SKIP_CUTSCENE,1);
        key(eligible[i],1,0); CHECK(prompt); CHECK(!pc_movie_skip_take_request(1));
        key(eligible[i],1,1); key(eligible[i],1,0); CHECK(prompt); CHECK(!pc_movie_skip_take_request(1));
        key(eligible[i],0,0); tap(eligible[i]); CHECK(!prompt); CHECK(pc_movie_skip_take_request(1));
    }
    reset(); pc_movie_skip_update(1,PC_MOVIE_SKIP_CUTSCENE,1); tap(0x1c); CHECK(prompt);
    time_at(clock_ms+3001); pc_movie_skip_update(1,PC_MOVIE_SKIP_CUTSCENE,1);
    CHECK(!prompt); tap(0x1c); CHECK(prompt); CHECK(!pc_movie_skip_take_request(1));
    /* Refresh the movie while the confirmation itself expires. */
    reset(); pc_movie_skip_update(1,PC_MOVIE_SKIP_CUTSCENE,1); tap(0x01);
    for(i=0;i<30;++i) { time_at(clock_ms+100); pc_movie_skip_update(1,PC_MOVIE_SKIP_CUTSCENE,1); }
    CHECK(!prompt); tap(0x01); CHECK(prompt); CHECK(!pc_movie_skip_take_request(1));
    reset(); pc_movie_skip_update(1,PC_MOVIE_SKIP_CUTSCENE,1); tap(0x01); CHECK(prompt);
    pc_movie_skip_update(2,PC_MOVIE_SKIP_CUTSCENE,1); CHECK(!prompt); tap(0x01); CHECK(prompt); CHECK(!pc_movie_skip_take_request(2));
    pc_movie_skip_update(2,PC_MOVIE_SKIP_CUTSCENE,0); CHECK(!prompt); CHECK(!pc_movie_skip_active());
    reset(); pc_movie_skip_update(1,PC_MOVIE_SKIP_INTRO,1); key(0x01,1,0); CHECK(pc_movie_skip_take_request(1));
    pc_movie_skip_update(2,PC_MOVIE_SKIP_INTRO,1); key(0x01,1,1); CHECK(!pc_movie_skip_take_request(2));
    key(0x01,0,0); tap(0x01); CHECK(pc_movie_skip_take_request(2));
    reset(); key(0x01,1,0); pc_movie_skip_update(1,PC_MOVIE_SKIP_INTRO,1);
    key(0x01,1,1); CHECK(!pc_movie_skip_take_request(1)); key(0x01,0,0); tap(0x01); CHECK(pc_movie_skip_take_request(1));
    reset(); pc_movie_skip_update(1,PC_MOVIE_SKIP_CUTSCENE,1); key(0x01,1,0); key(0x1c,1,0);
    CHECK(!pc_movie_skip_take_request(1)); key(0x01,0,0); key(0x1c,0,0); tap(0x1c); CHECK(pc_movie_skip_take_request(1));
    reset(); pc_movie_skip_update(1,PC_MOVIE_SKIP_CUTSCENE,1); tap(0x01);
    pc_input_focus_event(0); CHECK(!prompt); pc_input_focus_event(1);
    key(0x01,1,1); CHECK(!pc_movie_skip_take_request(1)); key(0x01,0,0); tap(0x01); CHECK(prompt); CHECK(!pc_movie_skip_take_request(1));
    reset(); pc_movie_skip_update(1,PC_MOVIE_SKIP_CUTSCENE,1); tap(0x01);
    time_at(clock_ms+501); CHECK(!pc_movie_skip_active()); CHECK(!prompt); CHECK(!pc_movie_skip_take_request(1));
    reset(); pc_movie_skip_update(1,PC_MOVIE_SKIP_NONE,1); tap(0x01); CHECK(!prompt); CHECK(!pc_movie_skip_active());
    reset(); pc_movie_skip_update(1,PC_MOVIE_SKIP_CUTSCENE,1); tap(0x13); CHECK(!prompt); CHECK(!pc_movie_skip_take_request(1));
    for(i=0;i<3;++i) {
        unsigned start=i==0?0x10:0,a=i==1?255:0,b=i==2?255:0;
        reset(); report(0,0,0); pc_movie_skip_update(1,PC_MOVIE_SKIP_CUTSCENE,1);
        report(start,a,b); CHECK(prompt); CHECK(!pc_movie_skip_take_request(1));
        report(start,a,b); CHECK(!pc_movie_skip_take_request(1));
        report(0,0,0); report(start,a,b); CHECK(pc_movie_skip_take_request(1));
        /* Held confirming input stays neutral on the next gameplay report. */
        { uint8_t out[20]={0,20}; out[2]=(uint8_t)start;out[4]=(uint8_t)a;out[5]=(uint8_t)b; pc_input_apply_report(out,1); CHECK(out[2]==0&&out[4]==0&&out[5]==0); }
        report(0,0,0); pc_movie_skip_update(2,PC_MOVIE_SKIP_INTRO,1); report(start,a,b); CHECK(pc_movie_skip_take_request(2));
    }
    reset(); report(0x10,0,0); pc_movie_skip_update(1,PC_MOVIE_SKIP_INTRO,1);
    report(0x10,0,0); CHECK(!pc_movie_skip_take_request(1)); report(0,0,0); report(0x10,0,0); CHECK(pc_movie_skip_take_request(1));
    reset(); pc_movie_skip_update(1,PC_MOVIE_SKIP_CUTSCENE,1);
    pc_input_mouse_button_event(0,1); CHECK(prompt); pc_input_mouse_button_event(0,1); CHECK(!pc_movie_skip_take_request(1));
    pc_input_mouse_button_event(0,0); pc_input_mouse_button_event(0,1); CHECK(pc_movie_skip_take_request(1));
    reset(); lower_keys=0; pc_input_set_filter_slot(0,&lower); pc_input_set_filter_slot(1,&lower);
    pc_movie_skip_update(1,PC_MOVIE_SKIP_CUTSCENE,1); tap(0x01); CHECK(prompt); CHECK(lower_keys==0);
    tap(0x57); CHECK(lower_keys==2); pc_movie_skip_clear(); tap(0x13); CHECK(lower_keys==4);
    reset(); pc_movie_skip_update(1,PC_MOVIE_SKIP_CUTSCENE,1); tap(0x01); tap(0x01);
    CHECK(!pc_movie_skip_take_request(2)); CHECK(pc_movie_skip_take_request(1));
    test_enter_maps_to_xbox_a();
    test_filtered_mouse_release();
    test_unfocused_pad();
    printf("PASS: %u movie skip checks\n",checks); return 0;
}
