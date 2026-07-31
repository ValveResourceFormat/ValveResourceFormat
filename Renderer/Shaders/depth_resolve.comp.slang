#version 460

layout (local_size_x = 8, local_size_y = 8) in;

#define D_MSAA_SAMPLES 0

layout(binding = 0) uniform sampler2DMS g_tSourceDepthMsaa;
layout(binding = 1, r32f) uniform writeonly image2D g_tDestDepth;

void main()
{
    ivec2 coord = ivec2(gl_GlobalInvocationID.xy);
    ivec2 destSize = imageSize(g_tDestDepth);

    if (coord.x >= destSize.x || coord.y >= destSize.y)
        return;

    float minDepth = 1.0;

    for (int i = 0; i < D_MSAA_SAMPLES; i++)
        minDepth = min(minDepth, texelFetch(g_tSourceDepthMsaa, coord, i).x);

    imageStore(g_tDestDepth, coord, vec4(minDepth));
}
