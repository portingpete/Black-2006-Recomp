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

    /* A path that cannot be written reports it. */
    CHECK(!black_audio_save("no-such-folder\\audio.ini", 60, 80));

    DeleteFileA(k_path);
    if (failures) fprintf(stderr, "%d failure(s)\n", failures);
    return failures ? 1 : 0;
}
