#version 460

layout (local_size_x = 8, local_size_y = 8) in;

#ifdef D_MSAA_INPUT
layout(binding = 0) uniform sampler2DMS g_tSourceDepthMS;
#else
layout(binding = 0) uniform sampler2D g_tSourceDepth;
#endif

layout(binding = 0, r32f) uniform writeonly image2D g_tDestDepth;

void main()
{
    ivec2 destCoord = ivec2(gl_GlobalInvocationID.xy);
    ivec2 destSize = imageSize(g_tDestDepth);
    
    if (destCoord.x >= destSize.x || destCoord.y >= destSize.y)
    {
        return;
    }
    
    ivec2 sourceCoord = destCoord * 2;
    
#ifdef D_MSAA_INPUT
    // Resolve MSAA samples - take min across all samples in 2x2 quad (reverse Z: furthest)
    int numSamples = textureSamples(g_tSourceDepthMS);
    float minDepth = 1.0;
    
    for (int y = 0; y < 2; y++)
    {
        for (int x = 0; x < 2; x++)
        {
            ivec2 coord = sourceCoord + ivec2(x, y);
            for (int s = 0; s < numSamples; s++)
            {
                float depth = texelFetch(g_tSourceDepthMS, coord, s).r;
                minDepth = min(minDepth, depth);
            }
        }
    }
#else
    // Regular 2x2 downsample - take min (reverse Z: furthest point)
    float depth0 = texelFetch(g_tSourceDepth, sourceCoord + ivec2(0, 0), 0).r;
    float depth1 = texelFetch(g_tSourceDepth, sourceCoord + ivec2(1, 0), 0).r;
    float depth2 = texelFetch(g_tSourceDepth, sourceCoord + ivec2(0, 1), 0).r;
    float depth3 = texelFetch(g_tSourceDepth, sourceCoord + ivec2(1, 1), 0).r;
    
    float minDepth = min(min(depth0, depth1), min(depth2, depth3));
#endif
    
    imageStore(g_tDestDepth, destCoord, vec4(minDepth, 0.0, 0.0, 0.0));
}
