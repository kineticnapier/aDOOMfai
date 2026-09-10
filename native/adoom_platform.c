#include <stdint.h>
#include <stdlib.h>
#include <string.h>
#include <stdio.h>

#ifdef _WIN32
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#define ADOOM_EXPORT __declspec(dllexport)
#else
#include <time.h>
#include <unistd.h>
#define ADOOM_EXPORT __attribute__((visibility("default")))
#endif

#include "doomgeneric.h"

#define KEYQUEUE_SIZE 512
#define FRAME_PIXELS (DOOMGENERIC_RESX * DOOMGENERIC_RESY)
#define FRAME_BYTES (FRAME_PIXELS * 4)

static uint16_t g_key_queue[KEYQUEUE_SIZE];
static unsigned int g_key_read = 0;
static unsigned int g_key_write = 0;
static uint8_t *g_rgba = NULL;
static int64_t g_frame_id = 0;
static int g_initialized = 0;

static void queue_key(int pressed, uint8_t key)
{
    unsigned int next = (g_key_write + 1u) % KEYQUEUE_SIZE;
    if (next == g_key_read)
    {
        // Drop the oldest event rather than blocking the Unity main thread.
        g_key_read = (g_key_read + 1u) % KEYQUEUE_SIZE;
    }

    g_key_queue[g_key_write] = (uint16_t)(((pressed ? 1 : 0) << 8) | key);
    g_key_write = next;
}

void DG_Init(void)
{
    if (g_rgba == NULL)
        g_rgba = (uint8_t *)malloc(FRAME_BYTES);

    if (g_rgba != NULL)
        memset(g_rgba, 0, FRAME_BYTES);
}

void DG_DrawFrame(void)
{
    if (g_rgba == NULL || DG_ScreenBuffer == NULL)
        return;

    // doomgeneric's rgba8888 path writes 0x00RRGGBB.
    // Export explicit RGBA bytes with alpha=255 for Unity.
    const uint32_t *src = (const uint32_t *)DG_ScreenBuffer;
    uint8_t *dst = g_rgba;

    // DOOM's framebuffer is top-to-bottom. Unity Texture2D raw pixel data
    // is interpreted bottom-to-top, so flip the row order while converting.
    for (int y = 0; y < DOOMGENERIC_RESY; ++y)
    {
        const uint32_t *src_row = src + y * DOOMGENERIC_RESX;
        uint8_t *dst_row = dst + (DOOMGENERIC_RESY - 1 - y) * DOOMGENERIC_RESX * 4;

        for (int x = 0; x < DOOMGENERIC_RESX; ++x)
        {
            uint32_t p = src_row[x];
            dst_row[x * 4 + 0] = (uint8_t)((p >> 16) & 0xff); // R
            dst_row[x * 4 + 1] = (uint8_t)((p >> 8) & 0xff);  // G
            dst_row[x * 4 + 2] = (uint8_t)(p & 0xff);         // B
            dst_row[x * 4 + 3] = 255;                         // A
        }
    }

    ++g_frame_id;
}

void DG_SleepMs(uint32_t ms)
{
#ifdef _WIN32
    Sleep(ms);
#else
    struct timespec ts;
    ts.tv_sec = ms / 1000u;
    ts.tv_nsec = (long)(ms % 1000u) * 1000000L;
    nanosleep(&ts, NULL);
#endif
}

uint32_t DG_GetTicksMs(void)
{
#ifdef _WIN32
    return (uint32_t)GetTickCount();
#else
    struct timespec ts;
    clock_gettime(CLOCK_MONOTONIC, &ts);
    return (uint32_t)(ts.tv_sec * 1000u + ts.tv_nsec / 1000000u);
#endif
}

int DG_GetKey(int *pressed, unsigned char *doomKey)
{
    if (g_key_read == g_key_write)
        return 0;

    uint16_t data = g_key_queue[g_key_read];
    g_key_read = (g_key_read + 1u) % KEYQUEUE_SIZE;

    *pressed = (data >> 8) & 1;
    *doomKey = (unsigned char)(data & 0xff);
    return 1;
}

void DG_SetWindowTitle(const char *title)
{
    (void)title;
}

ADOOM_EXPORT int ADOOM_Init(const char *iwad_path)
{
    if (g_initialized)
        return 1;

    if (iwad_path == NULL || iwad_path[0] == '\0')
        return 0;

    FILE *f = fopen(iwad_path, "rb");
    if (f == NULL)
        return 0;
    fclose(f);

    // Keep argv storage alive for the lifetime of the engine.
    static char arg0[] = "adoom";
    static char arg_iwad[] = "-iwad";
    static char arg_nosound[] = "-nosound";
    static char arg_nomusic[] = "-nomusic";
    static char arg_nogui[] = "-nogui";
    static char *argv[7];

    size_t n = strlen(iwad_path) + 1;
    char *wad = (char *)malloc(n);
    if (wad == NULL)
        return 0;
    memcpy(wad, iwad_path, n);

    argv[0] = arg0;
    argv[1] = arg_iwad;
    argv[2] = wad;
    argv[3] = arg_nosound;
    argv[4] = arg_nomusic;
    argv[5] = arg_nogui;
    argv[6] = NULL;

    doomgeneric_Create(6, argv);
    g_initialized = 1;
    return 1;
}

ADOOM_EXPORT int ADOOM_Tick(void)
{
    if (!g_initialized)
        return 0;

    doomgeneric_Tick();
    return 1;
}

ADOOM_EXPORT void ADOOM_KeyEvent(int pressed, uint8_t doomKey)
{
    if (!g_initialized)
        return;
    queue_key(pressed, doomKey);
}

ADOOM_EXPORT const uint8_t *ADOOM_GetFrameRGBA(void)
{
    return g_rgba;
}

ADOOM_EXPORT int64_t ADOOM_GetFrameId(void)
{
    return g_frame_id;
}

ADOOM_EXPORT int ADOOM_GetWidth(void)
{
    return DOOMGENERIC_RESX;
}

ADOOM_EXPORT int ADOOM_GetHeight(void)
{
    return DOOMGENERIC_RESY;
}
