/* Native replacement for the XMV decoder's YUV 4:2:0 -> ARGB converter (guest
 * sub_002403D1, with its inner kernel sub_002401D2), included by recomp_manual.c.
 *
 * Why: BLACK's menu backgrounds and loading screens are looping XMV movies, decoded
 * on the main thread every frame. In the recompiled code this converter, MMX
 * instructions emulated lane by lane in C, is about two thirds of the decode time
 * and keeps menus well under 60 FPS. This authored SSE2 implementation uses
 * structured color-conversion loops, validated against locally generated code.
 * It preserves the converter's arithmetic: Y' = (Y - 16) * 38139 as a float, chroma
 * (C - 128) as floats multiplied by the constants the guest builds in xmm4..xmm7
 * (B: 0x47812700, R: 0x474C4A00, G: -0xC6D02000 from V and 0xC6483000 from U) and added in
 * the guest's order, then cvtps2dq with the thread's rounding mode, >> 15, and
 * packssdw / packuswb saturation, alpha 0xFF. The block walk, edge clipping and
 * strides follow sub_002403D1 exactly (16x16 macroblocks, 8x8 chroma, partial
 * right/bottom blocks converted into a scratch block and copied).
 *
 * RECOMP_BLACK_NATIVE_YUV=0 keeps the generated converter; =verify runs both on
 * the first calls and reports differing bytes (the generated one is the oracle). */
#include <emmintrin.h>
#include <stdbool.h>

extern void sub_002403D1_gen(void);

static int black_yuv_mode = -1;          /* 0 generated, 1 native, 2 verify */
static unsigned black_yuv_verified, black_yuv_bad;

static __forceinline __m128 yuv_f32(uint32_t bits) { return _mm_castsi128_ps(_mm_set1_epi32((int)bits)); }

static __forceinline void yuv_pixels4(const uint8_t *y, __m128 uf, __m128 vf, uint8_t *dst)
{
    const __m128 cb = yuv_f32(0x47812700u), cr = yuv_f32(0x474C4A00u);
    const __m128 cgv = yuv_f32(0xC6D02000u), cgu = yuv_f32(0xC6483000u);
    const __m128i zero = _mm_setzero_si128();
    uint32_t y4;
    __m128i yw, yd, bi, gi, ri, b8, g8, r8, bg, ra;
    __m128 yf, bf, gf, rf;
    memcpy(&y4, y, 4);
    yw = _mm_unpacklo_epi8(_mm_cvtsi32_si128((int)y4), zero);
    yw = _mm_add_epi16(yw, _mm_set1_epi16(-16));
    yd = _mm_madd_epi16(_mm_unpacklo_epi16(yw, yw), _mm_set1_epi32(0x400054FB));
    yf = _mm_cvtepi32_ps(yd);
    bf = _mm_add_ps(_mm_mul_ps(uf, cb), yf);
    rf = _mm_add_ps(_mm_mul_ps(vf, cr), yf);
    gf = _mm_add_ps(_mm_add_ps(yf, _mm_mul_ps(vf, cgv)), _mm_mul_ps(uf, cgu));
    bi = _mm_srai_epi32(_mm_cvtps_epi32(bf), 15);
    gi = _mm_srai_epi32(_mm_cvtps_epi32(gf), 15);
    ri = _mm_srai_epi32(_mm_cvtps_epi32(rf), 15);
    b8 = _mm_packus_epi16(_mm_packs_epi32(bi, bi), zero);
    g8 = _mm_packus_epi16(_mm_packs_epi32(gi, gi), zero);
    r8 = _mm_packus_epi16(_mm_packs_epi32(ri, ri), zero);
    bg = _mm_unpacklo_epi8(b8, g8);
    ra = _mm_unpacklo_epi8(r8, _mm_set1_epi8((char)0xFF));
    _mm_storeu_si128((__m128i *)dst, _mm_unpacklo_epi16(bg, ra));
}

/* One 16x16 macroblock: Y with stride ys, U and V with stride cs, 32-bit pixels at dst with stride ds. */
static void yuv_block16(const uint8_t *Y, size_t ys, const uint8_t *U, const uint8_t *V, size_t cs,
                        uint8_t *dst, size_t ds)
{
    const __m128i zero = _mm_setzero_si128();
    int pair, half, q;
    for (pair = 0; pair < 8; pair++) {
        const uint8_t *y0 = Y + (size_t)(2 * pair) * ys, *y1 = y0 + ys;
        const uint8_t *u = U + (size_t)pair * cs, *v = V + (size_t)pair * cs;
        uint8_t *d0 = dst + (size_t)(2 * pair) * ds, *d1 = d0 + ds;
        for (half = 0; half < 2; half++) {
            uint32_t u4, v4;
            __m128 uf, vf;
            memcpy(&u4, u + 4 * half, 4);
            memcpy(&v4, v + 4 * half, 4);
            uf = _mm_cvtepi32_ps(_mm_sub_epi32(_mm_unpacklo_epi16(_mm_unpacklo_epi8(_mm_cvtsi32_si128((int)u4), zero), zero),
                                               _mm_set1_epi32(128)));
            vf = _mm_cvtepi32_ps(_mm_sub_epi32(_mm_unpacklo_epi16(_mm_unpacklo_epi8(_mm_cvtsi32_si128((int)v4), zero), zero),
                                               _mm_set1_epi32(128)));
            for (q = 0; q < 2; q++) {
                __m128 uq = q ? _mm_shuffle_ps(uf, uf, _MM_SHUFFLE(3, 3, 2, 2)) : _mm_shuffle_ps(uf, uf, _MM_SHUFFLE(1, 1, 0, 0));
                __m128 vq = q ? _mm_shuffle_ps(vf, vf, _MM_SHUFFLE(3, 3, 2, 2)) : _mm_shuffle_ps(vf, vf, _MM_SHUFFLE(1, 1, 0, 0));
                size_t x = (size_t)(8 * half + 4 * q);
                yuv_pixels4(y0 + x, uq, vq, d0 + x * 4);
                yuv_pixels4(y1 + x, uq, vq, d1 + x * 4);
            }
        }
    }
}

/* sub_002403D1's walk. a0 macroblocks across, a1 down; width a5 and height a6 in pixels; planes Y a2,
 * U a3, V a4 (dense: Y stride a0*16, chroma stride a0*8); output a7 with pitch a8. */
static void yuv_convert(uint32_t a0, uint32_t a1, uint32_t a2, uint32_t a3, uint32_t a4,
                        uint32_t a5, uint32_t a6, uint32_t a7, uint32_t a8)
{
    uint8_t *base = (uint8_t *)(uintptr_t)g_xbox_mem_offset;
    size_t ys = (size_t)a0 * 16, cs = (size_t)a0 * 8;
    uint32_t right_pad = a0 * 16 - a5;       /* pixels of the last block column past the image */
    uint32_t bottom_pad = a1 * 16 - a6;      /* rows of the last block row past the image */
    uint32_t bx, by;
    uint8_t scratch[16 * 64];
    for (by = 0; by < a1; by++) {
        const uint8_t *Y = base + a2 + (size_t)by * ys * 16;
        const uint8_t *U = base + a3 + (size_t)by * cs * 8;
        const uint8_t *V = base + a4 + (size_t)by * cs * 8;
        uint8_t *dst = base + a7 + (size_t)by * a8 * 16;
        for (bx = 0; bx < a0; bx++, Y += 16, U += 8, V += 8, dst += 64) {
            bool last_col = bx == a0 - 1, last_row = by == a1 - 1;
            bool partial = (last_col && right_pad != 0) || (last_row && bottom_pad != 0);
            if (!partial) {
                yuv_block16(Y, ys, U, V, cs, dst, a8);
            } else {
                uint32_t cols = last_col ? 16 - right_pad : 16, rows = last_row ? 16 - bottom_pad : 16, r;
                yuv_block16(Y, ys, U, V, cs, scratch, 64);
                for (r = 0; r < rows; r++) memcpy(dst + (size_t)r * a8, scratch + (size_t)r * 64, (size_t)cols * 4);
            }
        }
    }
}

void sub_002403D1(void)
{
    uint32_t sp = g_esp;
    if (black_yuv_mode < 0) {
        const char *e = getenv("RECOMP_BLACK_NATIVE_YUV");
        black_yuv_mode = e && !strcmp(e, "verify") ? 2 : (e && *e == '0') ? 0 : 1;
        fprintf(stderr, "[BLACK] XMV YUV converter: %s\n",
                black_yuv_mode == 0 ? "generated code" : black_yuv_mode == 1 ? "native SSE2" : "native, verified against the generated code");
    }
    if (black_yuv_mode == 0) {
        sub_002403D1_gen();
        return;
    }
    {
        uint32_t a0 = GUEST32(sp + 4), a1 = GUEST32(sp + 8), a2 = GUEST32(sp + 12), a3 = GUEST32(sp + 16),
                 a4 = GUEST32(sp + 20), a5 = GUEST32(sp + 24), a6 = GUEST32(sp + 28), a7 = GUEST32(sp + 32),
                 a8 = GUEST32(sp + 36);
        static int hash_mode = -1;
        if (hash_mode < 0) { const char *h = getenv("RECOMP_BLACK_XMV_HASH"); hash_mode = h && *h && *h != '0'; }
        if (hash_mode) {
            /* RECOMP_BLACK_XMV_HASH=1: fingerprint the decoded planes of every converted frame, to compare
             * decoder builds (the decode is deterministic, so the k-th call must hash the same). */
            static unsigned long long calls;
            const uint8_t *mem = (const uint8_t *)(uintptr_t)g_xbox_mem_offset;
            unsigned long long hv = 1469598103934665603ULL;
            size_t ny = (size_t)a0 * a1 * 256, nc = (size_t)a0 * a1 * 64, i;
            const uint64_t *p;
            p = (const uint64_t *)(mem + a2); for (i = 0; i < ny / 8; i++) hv = (hv ^ p[i]) * 1099511628211ULL;
            p = (const uint64_t *)(mem + a3); for (i = 0; i < nc / 8; i++) hv = (hv ^ p[i]) * 1099511628211ULL;
            p = (const uint64_t *)(mem + a4); for (i = 0; i < nc / 8; i++) hv = (hv ^ p[i]) * 1099511628211ULL;
            fprintf(stderr, "[XMVH] %llu %ux%u %016llX\n", ++calls, a0, a1, hv);
        }
        if (black_yuv_mode == 2 && black_yuv_verified < 400) {
            /* the destination spans at most a1*16 rows of pitch a8 */
            size_t bytes = (size_t)a8 * (a1 * 16);
            uint8_t *dst = (uint8_t *)(uintptr_t)g_xbox_mem_offset + a7;
            uint8_t *mine = (uint8_t *)malloc(bytes ? bytes : 1), *orig = (uint8_t *)malloc(bytes ? bytes : 1);
            uint32_t saved_esp = g_esp, saved_ebx = g_ebx, saved_esi = g_esi, saved_edi = g_edi;
            if (mine && orig && a0 && a1 && bytes) {
                size_t i, diff = 0;
                memcpy(orig, dst, bytes);                      /* the bytes the call may leave alone */
                yuv_convert(a0, a1, a2, a3, a4, a5, a6, a7, a8);
                memcpy(mine, dst, bytes);
                memcpy(dst, orig, bytes);
                sub_002403D1_gen();                            /* sets esp past the arguments */
                for (i = 0; i < bytes; i++) if (dst[i] != mine[i]) diff++;
                black_yuv_verified++;
                if (diff) {
                    black_yuv_bad++;
                    if (black_yuv_bad <= 8)
                        fprintf(stderr, "[BLACK] XMV YUV verify: %zu of %zu bytes differ (blocks %ux%u, %ux%u px, pitch %u)\n",
                                diff, bytes, a0, a1, a5, a6, a8);
                }
                if (black_yuv_verified == 400 || black_yuv_verified == 1)
                    fprintf(stderr, "[BLACK] XMV YUV verify: %u calls, %u mismatching\n", black_yuv_verified, black_yuv_bad);
                (void)saved_esp; (void)saved_ebx; (void)saved_esi; (void)saved_edi;
                free(mine); free(orig);
                return;
            }
            free(mine); free(orig);
        }
        yuv_convert(a0, a1, a2, a3, a4, a5, a6, a7, a8);
        g_esp = sp + 40;                                       /* ret 0x24 */
    }
}
