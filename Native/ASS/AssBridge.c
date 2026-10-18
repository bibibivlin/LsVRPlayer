/* libass adapter. No file/network access, Unity or graphics calls on this thread. */
#include <ass.h>
#include <stdint.h>
#include <stdlib.h>
#include <string.h>
#include <limits.h>

#ifdef _WIN32
#define API __declspec(dllexport)
#else
#define API __attribute__((visibility("default")))
#endif

typedef struct {
    ASS_Library *library;
    ASS_Renderer *renderer;
    ASS_Track *track;
    unsigned char *pixels;
    int width, height;
} Context;

static void quiet(int level, const char *format, va_list args, void *data) {
    /* Subtitle text, embedded fonts and file paths must not enter diagnostics. */
    (void)level; (void)format; (void)args; (void)data;
}

API void AssDestroy(Context *context) {
    if (!context) return;
    if (context->track) ass_free_track(context->track);
    if (context->renderer) ass_renderer_done(context->renderer);
    if (context->library) ass_library_done(context->library);
    free(context->pixels);
    free(context);
}

static Context *parse(const unsigned char *text, int length) {
    if (!text || length <= 0 || length > 8 * 1024 * 1024) return NULL;
    Context *context = calloc(1, sizeof(Context));
    if (!context) return NULL;
    context->library = ass_library_init();
    if (!context->library) { AssDestroy(context); return NULL; }
    ass_set_message_cb(context->library, quiet, NULL);
    ass_set_extract_fonts(context->library, 0);
    char *copy = malloc((size_t)length + 1);
    if (!copy) { AssDestroy(context); return NULL; }
    memcpy(copy, text, length); copy[length] = 0;
    /* The managed/file bridge has already decoded to UTF-8. NULL bypasses
     * iconv, whose state-flush call crashes on some PICO Android firmware. */
    context->track = ass_read_memory(context->library, copy, length, NULL);
    free(copy);
    if (!context->track || context->track->n_events == 0) { AssDestroy(context); return NULL; }
    return context;
}

API int AssValidate(const unsigned char *text, int length) {
    Context *context = parse(text, length);
    if (!context) return 0;
    int events = context->track->n_events;
    AssDestroy(context);
    return events;
}

API Context *AssCreate(const unsigned char *text, int length, const unsigned char *font, int fontLength) {
    Context *context = parse(text, length);
    if (!context) return NULL;
    if (font && fontLength > 0) ass_add_font(context->library, "NotoSansCJKsc-Regular.otf", (char *)font, fontLength);
    context->renderer = ass_renderer_init(context->library);
    if (!context->renderer) { AssDestroy(context); return NULL; }
    ass_set_fonts(context->renderer, NULL, "Noto Sans CJK SC", ASS_FONTPROVIDER_NONE, NULL, 1);
    ass_set_use_margins(context->renderer, 0);
    ass_set_cache_limits(context->renderer, 1000, 32);
    return context;
}

API int AssUpdate(Context *context, const unsigned char *text, int length) {
    if (!context || !text || length <= 0 || length > 8 * 1024 * 1024) return 0;
    char *copy = malloc((size_t)length + 1);
    if (!copy) return 0;
    memcpy(copy, text, length); copy[length] = 0;
    ASS_Track *track = ass_read_memory(context->library, copy, length, NULL);
    free(copy);
    if (!track || !track->n_events) { if (track) ass_free_track(track); return 0; }
    ass_free_track(context->track); context->track = track;
    return 1;
}

/* Returns -1 on error, 0 for unchanged, 1 for a new premultiplied RGBA frame.
 * Output rows are bottom-up, matching Unity texture UVs. Images are source-over
 * composited in libass order, clipped again defensively to the video rectangle.
 * Only the caller owning this context may access it, including destruction. */
API int AssRender(Context *context, int64_t milliseconds, int width, int height, int force) {
    if (!context || width < 1 || height < 1 || width > 2048 || height > 2048 || width * height > 2097152) return -1;
    int resized = context->width != width || context->height != height;
    if (resized) {
        unsigned char *pixels = calloc((size_t)width * height, 4);
        if (!pixels) return -1;
        free(context->pixels); context->pixels = pixels;
        context->width = width; context->height = height;
        ass_set_frame_size(context->renderer, width, height);
    }
    int changed = 0;
    ASS_Image *images = ass_render_frame(context->renderer, context->track, milliseconds, &changed);
    if (!resized && !changed && !force) return 0;
    memset(context->pixels, 0, (size_t)width * height * 4);
    for (ASS_Image *image = images; image; image = image->next) {
        if (!image->bitmap || image->w <= 0 || image->h <= 0 || image->stride < image->w) continue;
        int left = image->dst_x < 0 ? 0 : image->dst_x;
        int top = image->dst_y < 0 ? 0 : image->dst_y;
        int64_t right64 = (int64_t)image->dst_x + image->w;
        int64_t bottom64 = (int64_t)image->dst_y + image->h;
        int right = right64 > width ? width : (int)right64;
        int bottom = bottom64 > height ? height : (int)bottom64;
        unsigned int opacity = 255 - (image->color & 255);
        unsigned int red = image->color >> 24, green = (image->color >> 16) & 255, blue = (image->color >> 8) & 255;
        for (int y = top; y < bottom; ++y) {
            const unsigned char *src = image->bitmap + (size_t)(y - image->dst_y) * image->stride + left - image->dst_x;
            unsigned char *dst = context->pixels + ((size_t)(height - 1 - y) * width + left) * 4;
            for (int x = left; x < right; ++x, ++src, dst += 4) {
                unsigned int alpha = (*src * opacity + 127) / 255, inverse = 255 - alpha;
                dst[0] = (red * alpha + dst[0] * inverse + 127) / 255;
                dst[1] = (green * alpha + dst[1] * inverse + 127) / 255;
                dst[2] = (blue * alpha + dst[2] * inverse + 127) / 255;
                dst[3] = alpha + (dst[3] * inverse + 127) / 255;
            }
        }
    }
    return 1;
}

API const unsigned char *AssPixels(Context *context) { return context ? context->pixels : NULL; }
API int AssVersion(void) { return ass_library_version(); }
