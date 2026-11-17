#version 460

#include "common/view_constants.slang"
#include "common/utils.slang"

#define TILE_SIZE 16

layout (local_size_x = TILE_SIZE, local_size_y = TILE_SIZE) in;

#define D_NPOT_DOWNSAMPLE 0

#if D_NPOT_DOWNSAMPLE == 1
    layout(binding = 0) uniform sampler2D g_tSourceDepthNpot;
    uniform int g_nSourceDepthWidth;
    uniform int g_nSourceDepthHeight;
#else
    layout(binding = 1, r32f) uniform readonly image2D g_tSourceDepth;
#endif

// Consecutive levels starting at g_nDestDepthWidth x g_nDestDepthHeight
layout(binding = 2, r32f) uniform writeonly image2D g_tDestDepth0;
layout(binding = 3, r32f) uniform writeonly image2D g_tDestDepth1;
layout(binding = 4, r32f) uniform writeonly image2D g_tDestDepth2;
layout(binding = 5, r32f) uniform writeonly image2D g_tDestDepth3;
layout(binding = 6, r32f) uniform writeonly image2D g_tDestDepth4;
uniform int g_nDestDepthWidth;
uniform int g_nDestDepthHeight;
uniform int g_nDestLevelCount;

// Level n of the tile lives at multiples of 1 << n
shared float s_TileDepth[TILE_SIZE][TILE_SIZE];

float loadSourceDepth(ivec2 destCoord, ivec2 destSize)
{
#if D_NPOT_DOWNSAMPLE == 1
    ivec2 vSourceDepthSize = ivec2(g_nSourceDepthWidth, g_nSourceDepthHeight);

    // Conservative mapping: round down for min, round up for max
    ivec2 sourceMin = (destCoord * vSourceDepthSize) / destSize;
    ivec2 sourceMax = min(((destCoord + ivec2(1)) * vSourceDepthSize + destSize - ivec2(1)) / destSize, vSourceDepthSize);

    float furthestDepth = 1.0;

    for (int y = sourceMin.y; y < sourceMax.y; y++)
    {
        for (int x = sourceMin.x; x < sourceMax.x; x++)
        {
            float depth = texelFetch(g_tSourceDepthNpot, ivec2(x, y), 0).r;
            furthestDepth = min(furthestDepth, depth);
        }
    }

    return furthestDepth;
#else
    // Clamp so a source axis of one texel is not read out of bounds
    ivec2 sourceMax = imageSize(g_tSourceDepth) - ivec2(1);
    ivec2 sourceCoord = destCoord * 2;
    ivec2 sourceCoord1 = min(sourceCoord + ivec2(1), sourceMax);

    // Regular 2x2 downsample - take min (reverse Z: furthest point)
    float depth0 = imageLoad(g_tSourceDepth, sourceCoord).r;
    float depth1 = imageLoad(g_tSourceDepth, ivec2(sourceCoord1.x, sourceCoord.y)).r;
    float depth2 = imageLoad(g_tSourceDepth, ivec2(sourceCoord.x, sourceCoord1.y)).r;
    float depth3 = imageLoad(g_tSourceDepth, sourceCoord1).r;

    return min(min(depth0, depth1), min(depth2, depth3));
#endif
}

void storeDepth(int level, ivec2 coord, float depth)
{
    vec4 value = vec4(depth, 0.0, 0.0, 0.0);

    switch (level)
    {
        case 1: imageStore(g_tDestDepth1, coord, value); break;
        case 2: imageStore(g_tDestDepth2, coord, value); break;
        case 3: imageStore(g_tDestDepth3, coord, value); break;
        case 4: imageStore(g_tDestDepth4, coord, value); break;
    }
}

void main()
{
    ivec2 localCoord = ivec2(gl_LocalInvocationID.xy);
    ivec2 destCoord = ivec2(gl_GlobalInvocationID.xy);
    ivec2 destSize = ivec2(g_nDestDepthWidth, g_nDestDepthHeight);

    // Nearest depth for texels past the level edge, so they never win the min
    float depth = 1.0;

    if (all(lessThan(destCoord, destSize)))
    {
        depth = loadSourceDepth(destCoord, destSize);
        imageStore(g_tDestDepth0, destCoord, vec4(depth, 0.0, 0.0, 0.0));
    }

    s_TileDepth[localCoord.y][localCoord.x] = depth;

    // Keep every thread in the loop, as barrier() needs uniform control flow
    for (int level = 1; level < g_nDestLevelCount; level++)
    {
        barrier();

        int stride = 1 << level;

        if (all(equal(localCoord & (stride - 1), ivec2(0))))
        {
            ivec2 c = localCoord;
            int h = stride >> 1;

            depth = min(
                min(s_TileDepth[c.y][c.x], s_TileDepth[c.y][c.x + h]),
                min(s_TileDepth[c.y + h][c.x], s_TileDepth[c.y + h][c.x + h]));

            s_TileDepth[c.y][c.x] = depth;

            ivec2 levelCoord = destCoord >> level;

            if (all(lessThan(levelCoord, max(destSize >> level, ivec2(1)))))
            {
                storeDepth(level, levelCoord, depth);
            }
        }
    }
}
