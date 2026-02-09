#version 460

#include "common/ViewConstants.slang"
#include "common/utils.slang"


layout (local_size_x = 8, local_size_y = 8) in;

#define D_MSAA_INPUT 0

#if D_MSAA_INPUT == 1
layout(binding = 0) uniform sampler2DMS g_tSourceDepthMS;
#endif

layout(binding = 1, r32f) uniform readonly image2D g_tSourceDepth;
layout(binding = 2, r32f) uniform writeonly image2D g_tDestDepth;

void main()
{
    ivec2 destCoord = ivec2(gl_GlobalInvocationID.xy);
    ivec2 destSize = imageSize(g_tDestDepth);
    
    if (destCoord.x >= destSize.x || destCoord.y >= destSize.y)
    {
        return;
    }
       
#if D_MSAA_INPUT == 1
    // Resolve MSAA samples at same coordinate (reverse Z: furthest)
    int numSamples = textureSamples(g_tSourceDepthMS);
    float furthestDepth = 1.0;
    
    for (int s = 0; s < numSamples; s++)
    {
        float depth = texelFetch(g_tSourceDepthMS, destCoord, s).r;
        furthestDepth = min(furthestDepth, depth);
    }

    furthestDepth = LinearRamp(g_flViewportMinZ, g_flViewportMaxZ, furthestDepth);
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
