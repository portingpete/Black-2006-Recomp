#include <stdint.h>
#include <stdio.h>
#include <string.h>
#include <windows.h>

#include "black_audio_settings.inc"

static int failures;
#define CHECK(condition)                                                     \
    do {                                                                     \
        if (!(condition)) {                                                  \
            fprintf(stderr, "%s:%d: %s\n", __FILE__, __LINE__, #condition);  \
            failures++;                                                      \
        }                                                                    \
    } while (0)

static const char *const k_path = "audio-settings-test.ini";

static void write_text(const char *text)
{
    FILE *f = fopen(k_path, "w");
    if (!f) { failures++; return; }
    fputs(text, f);
    fclose(f);
}

static uint32_t float_bits(float value)
{
    uint32_t bits;
    memcpy(&bits, &value, sizeof(bits));
    return bits;
}

int main(void)
{
    int percent, sfx, music;
    FILE *leftover;

    /* Every slider value survives the game's float, and what is not a volume is refused. */
    for (percent = 0; percent <= 100; percent++) CHECK(black_audio_percent(black_audio_bits(percent)) == percent);
    CHECK(black_audio_percent(float_bits(1.0f)) == 100);
    CHECK(black_audio_percent(float_bits(0.0f)) == 0);
    CHECK(black_audio_percent(float_bits(-0.5f)) == -1);
    CHECK(black_audio_percent(float_bits(2.0f)) == -1);
    CHECK(black_audio_percent(0x7FC00000u) == -1); /* not a number */

    /* No file: nothing found, nothing touched. */
    DeleteFileA(k_path);
    sfx = 11; music = 22;
    CHECK(!black_audio_load(k_path, &sfx, &music));
    CHECK(sfx == 11 && music == 22);

    /* Saving replaces the file whole and leaves nothing beside it. */
    CHECK(black_audio_save(k_path, 60, 80));
    CHECK(black_audio_save(k_path, 0, 100));
    CHECK(black_audio_load(k_path, &sfx, &music));
    CHECK(sfx == 0 && music == 100);
    leftover = fopen("audio-settings-test.ini.new", "r");
    CHECK(!leftover);
    if (leftover) fclose(leftover);

    /* One good value is used. */
    write_text("[audio]\nmusic_volume=44\n");
    sfx = 11; music = 22;
    CHECK(black_audio_load(k_path, &sfx, &music));
    CHECK(sfx == 11 && music == 44);

    /* Spaces and Windows line ends are fine. */
    write_text("[audio]\r\n  sfx_volume = 52\r\n");
    sfx = 11; music = 22;
    CHECK(black_audio_load(k_path, &sfx, &music));
    CHECK(sfx == 52 && music == 22);

    /* A damaged file changes nothing. */
    write_text("garbage\nsfx_volume=abc\nmusic_volume=999\nsfx_volume=-1\n");
    sfx = 11; music = 22;
    CHECK(!black_audio_load(k_path, &sfx, &music));
    CHECK(sfx == 11 && music == 22);

    /* Only the sound pages count as the player's choice. */
    CHECK(black_audio_sound_page("SoundSetupXbox") && black_audio_sound_page("soundsetupxbox"));
    CHECK(black_audio_sound_page("PauseMenuOptionXbox"));
    CHECK(!black_audio_sound_page("PauseMenu") && !black_audio_sound_page("OptionsMenuXbox") && !black_audio_sound_page(""));

    {
        BlackAudioSaver saver;
        int writes = 0;

        /* Each step on a sound page is written, with that step's values. */
        black_audio_saver_start(&saver, 100, 100);
        CHECK(!black_audio_saver_tick(&saver, 100, 100, 1));
        for (percent = 96; percent >= 60; percent -= 4) {
            CHECK(black_audio_saver_tick(&saver, percent, 100, 1) && saver.pend_sfx == percent);
            saver.file_sfx = saver.pend_sfx;
            writes += black_audio_saver_tick(&saver, percent, 100, 1); /* still: nothing more */
        }
        CHECK(writes == 0);

        /* The game changing them elsewhere is not saved, nor is the player merely visiting the page after. */
        CHECK(!black_audio_saver_tick(&saver, 100, 100, 0));
        CHECK(!black_audio_saver_tick(&saver, 100, 100, 1));
        CHECK(black_audio_saver_tick(&saver, 100, 72, 1) && saver.pend_sfx == 100 && saver.pend_music == 72);
        saver.file_sfx = 100;
        saver.file_music = 72;

        /* Back to what the file holds: nothing to write. */
        CHECK(black_audio_saver_tick(&saver, 100, 76, 1));
        CHECK(!black_audio_saver_tick(&saver, 100, 72, 1));

        /* A failed write is tried again at the next change, since file_* still holds the old values. */
        black_audio_saver_start(&saver, 50, 50);
        CHECK(black_audio_saver_tick(&saver, 54, 50, 1));
        CHECK(black_audio_saver_tick(&saver, 58, 50, 1) && saver.file_sfx == 50);
    }

    /* A path that cannot be written reports it. */
    CHECK(!black_audio_save("no-such-folder\\audio.ini", 60, 80));

    DeleteFileA(k_path);
    if (failures) fprintf(stderr, "%d failure(s)\n", failures);
    return failures ? 1 : 0;
}
