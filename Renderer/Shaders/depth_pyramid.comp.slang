#version 460

#include "common/ViewConstants.slang"
#include "common/utils.slang"

layout (local_size_x = 8, local_size_y = 8) in;

#define D_NPOT_DOWNSAMPLE 0

#if D_NPOT_DOWNSAMPLE == 1
    layout(binding = 0) uniform sampler2D g_tSourceDepthNpot;
    uniform int g_nSourceDepthWidth;
    uniform int g_nSourceDepthHeight;
#else
    layout(binding = 1, r32f) uniform readonly image2D g_tSourceDepth;
#endif

layout(binding = 2, r32f) uniform writeonly image2D g_tDestDepth;
uniform int g_nDestDepthWidth;
uniform int g_nDestDepthHeight;

void main()
{
    ivec2 destCoord = ivec2(gl_GlobalInvocationID.xy);
    ivec2 destSize = ivec2(g_nDestDepthWidth, g_nDestDepthHeight);

    if (destCoord.x >= destSize.x || destCoord.y >= destSize.y)
    {
        return;
    }

#if D_NPOT_DOWNSAMPLE == 1
    // Downsample from non power of two depth source
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
#else
    ivec2 sourceCoord = destCoord * 2;

    // Regular 2x2 downsample - take min (reverse Z: furthest point)
    float depth0 = imageLoad(g_tSourceDepth, sourceCoord + ivec2(0, 0)).r;
    float depth1 = imageLoad(g_tSourceDepth, sourceCoord + ivec2(1, 0)).r;
    float depth2 = imageLoad(g_tSourceDepth, sourceCoord + ivec2(0, 1)).r;
    float depth3 = imageLoad(g_tSourceDepth, sourceCoord + ivec2(1, 1)).r;

    float furthestDepth = min(min(depth0, depth1), min(depth2, depth3));
#endif    
    imageStore(g_tDestDepth, destCoord, vec4(furthestDepth, 0.0, 0.0, 0.0));
}
