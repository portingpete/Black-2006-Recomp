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

/* Which pages hold the two volume sliders: the title menu's sound page and the pause menu's options. */
static int black_audio_sound_page(const char *page)
{
    char lower[64];
    size_t i;
    for (i = 0; page[i] && i + 1 < sizeof(lower); i++)
        lower[i] = (char)(page[i] >= 'A' && page[i] <= 'Z' ? page[i] - 'A' + 'a' : page[i]);
    lower[i] = 0;
    return strstr(lower, "sound") != NULL || !strcmp(lower, "pausemenuoptionxbox");
}

/* When a volume is written. Only a change made on a sound page is the player's: the game's own changes
 * elsewhere are not saved. A change is written once the sliders have been still for a moment, or when the
 * page is left, so holding a slider writes the file once, not at every step. */
#define BLACK_AUDIO_SETTLE_TICKS 15u

typedef struct BlackAudioSaver {
    int live_sfx, live_music;  /* the values at the last tick */
    int pend_sfx, pend_music;  /* the player's latest values, not yet written */
    int file_sfx, file_music;  /* what the file holds (or the starting values, before any save) */
    int pending;
    unsigned still;            /* ticks on the page since the last change */
} BlackAudioSaver;

static void black_audio_saver_start(BlackAudioSaver *s, int sfx, int music)
{
    memset(s, 0, sizeof(*s));
    s->live_sfx = s->pend_sfx = s->file_sfx = sfx;
    s->live_music = s->pend_music = s->file_music = music;
}

/* One menu tick with valid volumes. Nonzero: write pend_sfx and pend_music now (then set file_* if it worked). */
static int black_audio_saver_tick(BlackAudioSaver *s, int sfx, int music, int on_sound_page)
{
    const int changed = sfx != s->live_sfx || music != s->live_music;
    s->live_sfx = sfx;
    s->live_music = music;
    if (on_sound_page) {
        if (changed) {
            s->pend_sfx = sfx;
            s->pend_music = music;
            s->pending = 1;
            s->still = 0;
            return 0;
        }
        if (!s->pending || ++s->still < BLACK_AUDIO_SETTLE_TICKS) return 0;
    } else if (!s->pending) {
        return 0;
    }
    s->pending = 0;
    return s->pend_sfx != s->file_sfx || s->pend_music != s->file_music;
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
