/* local\audio.ini: the SFX and music volumes, 0..100. Included by black_native_menu.inc, which applies them to the game,
 * and by tests/audio_settings. It needs stdio, stdint, string and windows. */

/* A volume as the game keeps it (a float, 0..1) in percent, or -1 when it is not one. */
static int black_audio_percent(uint32_t bits)
{
    float value;
    memcpy(&value, &bits, sizeof(value));
    if (!(value >= 0.0f && value <= 1.001f)) return -1;
    return (int)(value * 100.0f + 0.5f);
}

static uint32_t black_audio_bits(int percent)
{
    const float value = (float)percent * 0.01f;
    uint32_t bits;
    memcpy(&bits, &value, sizeof(bits));
    return bits;
}

/* Fills in the volumes the file has; anything else in it, or a value out of 0..100, is left alone. */
static int black_audio_load(const char *path, int *sfx, int *music)
{
    char line[128];
    int found = 0;
    FILE *f = fopen(path, "r");
    if (!f) return 0;
    while (fgets(line, sizeof(line), f)) {
        int value;
        if (sscanf(line, " sfx_volume = %d", &value) == 1 && value >= 0 && value <= 100) { *sfx = value; found = 1; }
        else if (sscanf(line, " music_volume = %d", &value) == 1 && value >= 0 && value <= 100) { *music = value; found = 1; }
    }
    fclose(f);
    return found;
}

/* A new file put in place whole, so a crash cannot leave half of one. */
static int black_audio_save(const char *path, int sfx, int music)
{
    char temporary[MAX_PATH + 8];
    FILE *f;
    snprintf(temporary, sizeof(temporary), "%s.new", path);
    f = fopen(temporary, "w");
    if (!f) return 0;
    fprintf(f, "; BLACK audio settings. Edited by the game's sound menus.\n[audio]\nsfx_volume=%d\nmusic_volume=%d\n", sfx, music);
    if (fclose(f) != 0) { DeleteFileA(temporary); return 0; }
    if (!MoveFileExA(temporary, path, MOVEFILE_REPLACE_EXISTING)) { DeleteFileA(temporary); return 0; }
    return 1;
}
