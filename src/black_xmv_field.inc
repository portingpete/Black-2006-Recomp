/* Display field status for the XMV decoder (guest sub_0026B610), included by recomp_manual.c.
 *
 * D3DDevice_GetDisplayFieldStatus: writes {Field, VBlankCount} through its argument. Only the XMV decoder
 * (GetNextFrame, 0x0023F45C) calls it, and it uses it to start a movie's clock: the clock is not set while
 * Field is 1, so a movie's first frame waits for an odd field. The field comes from a GPU semaphore at
 * device + 0x1DF8 that the pusher releases when it reaches a flip, so in this port it can stay 1 for tens of
 * vblanks (0.2 - 0.5 s, 3 to 31 vblanks in the logs) while the main thread spins in the decoder with the
 * menu frozen. Each background movie loop and each loading screen began that way.
 *
 * The function has a progressive-scan branch (the display flags at device + 0x1DDC without the interlace bits)
 * that reports Field = 3 and never waits. This port presents a progressive picture at 60 Hz, so report that:
 * the movie clock starts at once. VBlankCount is left as the generated code wrote it.
 *
 * RECOMP_BLACK_FIELD_WAIT=1 keeps the original behaviour. */
extern void sub_0026B610_gen(void);
extern unsigned black_vblank_hz(void);

void sub_0026B610(void)
{
    static int keep = -1;
    uint32_t sp = g_esp;
    sub_0026B610_gen();                                    /* ret 4: pops the argument */
    if (keep < 0) {
        const char *e = getenv("RECOMP_BLACK_FIELD_WAIT");
        keep = e && *e && *e != '0';
        fprintf(stderr, "[BLACK] movie start: %s\n", keep ? "waits for an odd display field (original)" : "no field wait (progressive)");
    }
    {
        uint32_t out = GUEST32(sp + 4);
        unsigned hz = black_vblank_hz();
        if (out && hz > 60) GUEST32(out + 4) = (uint32_t)((uint64_t)GUEST32(out + 4) * 60u / hz);   /* the movie clock counts 60 Hz vblanks */
        if (out && !keep) GUEST32(out) = 3;
    }
}
