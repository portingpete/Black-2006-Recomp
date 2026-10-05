#ifndef BLACK_PC_BRANDING_H
#define BLACK_PC_BRANDING_H

#include <stddef.h>
#include <stdint.h>
#include <string.h>

/* Bounded UTF-8 display text only. Internal page names, guest paths and symbols
 * never enter this transform. A complete source NUL must occur within src_bytes.
 * Returns output bytes excluding NUL, or BLACK_PC_BRAND_ERROR for invalid UTF-8,
 * missing NUL, invalid arguments or insufficient space. The output is never
 * longer than the input; exact in-place transformation is supported.
 */
#define BLACK_PC_BRAND_ERROR ((size_t)-1)

static unsigned char black_pc_brand_lower(unsigned char c)
{
    return c >= 'A' && c <= 'Z' ? (unsigned char)(c + ('a' - 'A')) : c;
}

static int black_pc_brand_match(const char *source, size_t left, const char *phrase)
{
    size_t i;
    for (i = 0; phrase[i]; ++i)
        if (i >= left || black_pc_brand_lower((unsigned char)source[i]) != (unsigned char)phrase[i]) return 0;
    return 1;
}

static int black_pc_brand_uppercase(const char *source, size_t bytes)
{
    size_t i;
    for (i = 0; i < bytes; ++i)
        if (source[i] >= 'a' && source[i] <= 'z') return 0;
    return 1;
}

static int black_pc_brand_utf8(const char *source, size_t bytes)
{
    size_t i = 0;
    while (i < bytes) {
        const unsigned char c = (unsigned char)source[i];
        size_t continuation, j;
        unsigned char low = 0x80u, high = 0xBFu;
        if (c < 0x80u) { ++i; continue; }
        if (c >= 0xC2u && c <= 0xDFu) continuation = 1;
        else if (c >= 0xE0u && c <= 0xEFu) {
            continuation = 2;
            if (c == 0xE0u) low = 0xA0u;
            if (c == 0xEDu) high = 0x9Fu;
        } else if (c >= 0xF0u && c <= 0xF4u) {
            continuation = 3;
            if (c == 0xF0u) low = 0x90u;
            if (c == 0xF4u) high = 0x8Fu;
        } else return 0;
        if (continuation >= bytes - i) return 0;
        if ((unsigned char)source[i + 1u] < low || (unsigned char)source[i + 1u] > high) return 0;
        for (j = 2u; j <= continuation; ++j)
            if ((unsigned char)source[i + j] < 0x80u || (unsigned char)source[i + j] > 0xBFu) return 0;
        i += continuation + 1u;
    }
    return 1;
}

static size_t black_pc_brand_piece(const char *source, size_t left, const char **replacement, size_t *replacement_bytes)
{
    if (black_pc_brand_match(source, left, "xbox console")) {
        *replacement = "PC"; *replacement_bytes = 2u; return 12u;
    }
    if (black_pc_brand_match(source, left, "xbox hard disk")) {
        *replacement = black_pc_brand_uppercase(source, 14u) ? "PC STORAGE" : "PC storage";
        *replacement_bytes = 10u; return 14u;
    }
    if (black_pc_brand_match(source, left, "xbox")) {
        *replacement = "PC"; *replacement_bytes = 2u; return 4u;
    }
    *replacement = NULL; *replacement_bytes = 1u; return 1u;
}

static size_t black_pc_brand_text(char *destination, size_t destination_capacity, const char *source, size_t source_bytes)
{
    size_t length, in, out = 0;
    if (!source || !destination || !destination_capacity) return BLACK_PC_BRAND_ERROR;
    for (length = 0; length < source_bytes && source[length]; ++length) {}
    if (length == source_bytes || !black_pc_brand_utf8(source, length)) return BLACK_PC_BRAND_ERROR;
    /* Measure first: errors never leave a partially transformed message. */
    for (in = 0; in < length;) {
        const char *replacement;
        size_t replacement_bytes;
        in += black_pc_brand_piece(source + in, length - in, &replacement, &replacement_bytes);
        out += replacement_bytes;
    }
    if (out >= destination_capacity) return BLACK_PC_BRAND_ERROR;
    out = 0;
    for (in = 0; in < length;) {
        const char *replacement;
        size_t replacement_bytes;
        const size_t consumed = black_pc_brand_piece(source + in, length - in, &replacement, &replacement_bytes);
        if (replacement) memcpy(destination + out, replacement, replacement_bytes);
        else destination[out] = source[in];
        out += replacement_bytes;
        in += consumed;
    }
    destination[out] = 0;
    return out;
}

static int black_pc_brand_match16(const uint16_t *source, size_t left, const char *phrase)
{
    size_t i;
    for (i = 0; phrase[i]; ++i)
        if (i >= left || source[i] > 127u || black_pc_brand_lower((unsigned char)source[i]) != (unsigned char)phrase[i]) return 0;
    return 1;
}

/* Shrink a complete bounded native UTF-16 display buffer in place. Capacity is
 * in code units, including its existing terminator. Invalid/unterminated input
 * is untouched and returns BLACK_PC_BRAND_ERROR. Other text and surrogates are
 * preserved exactly. This is for display buffers, never bound variable names.
 */
static size_t black_pc_brand_text16(uint16_t *text, size_t capacity)
{
    size_t length, in, out = 0;
    if (!text || !capacity) return BLACK_PC_BRAND_ERROR;
    for (length = 0; length < capacity && text[length]; ++length) {}
    if (length == capacity) return BLACK_PC_BRAND_ERROR;
    for (in = 0; in < length; ++in) {
        if (text[in] >= 0xD800u && text[in] <= 0xDBFFu) {
            if (in + 1u >= length || text[in + 1u] < 0xDC00u || text[in + 1u] > 0xDFFFu) return BLACK_PC_BRAND_ERROR;
            ++in;
        } else if (text[in] >= 0xDC00u && text[in] <= 0xDFFFu) return BLACK_PC_BRAND_ERROR;
    }
    for (in = 0; in < length;) {
        size_t consumed = 1u, j;
        const char *replacement = NULL;
        if (black_pc_brand_match16(text + in, length - in, "xbox console")) {
            replacement = "PC"; consumed = 12u;
        } else if (black_pc_brand_match16(text + in, length - in, "xbox hard disk")) {
            int upper = 1;
            consumed = 14u;
            for (j = 0; j < consumed; ++j)
                if (text[in + j] >= 'a' && text[in + j] <= 'z') { upper = 0; break; }
            replacement = upper ? "PC STORAGE" : "PC storage";
        } else if (black_pc_brand_match16(text + in, length - in, "xbox")) {
            replacement = "PC"; consumed = 4u;
        }
        if (replacement) {
            for (j = 0; replacement[j]; ++j) text[out++] = (unsigned char)replacement[j];
        } else text[out++] = text[in];
        in += consumed;
    }
    text[out] = 0;
    return out;
}

#endif
