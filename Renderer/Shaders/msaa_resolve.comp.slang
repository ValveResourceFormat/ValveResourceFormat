#version 460

#include "common/utils.slang"
#include "common/ViewConstants.slang"

layout (local_size_x = 8, local_size_y = 8) in;

#define D_DOF 0

#define D_MSAA_SAMPLES 1

uniform bool g_bFlipY = false;

layout(binding = 0) uniform sampler2DMS g_tSourceMsaa;
layout(binding = 1, rgba16f) uniform writeonly image2D g_tDest;

#if D_DOF == 1
uniform mat4 g_invViewProjMatrix;
uniform vec4 g_vLensPlane;
uniform vec4 g_vNearFarScaleBias;
layout(binding = 2) uniform sampler2DMS g_tSceneDepth;
#endif

void main()
{
    ivec2 coord = ivec2(gl_GlobalInvocationID.xy);
    ivec2 destSize = imageSize(g_tDest);

    if (coord.x >= destSize.x || coord.y >= destSize.y)
        return;

    ivec2 sampleCoord = coord;
    sampleCoord.y = g_bFlipY ? textureSize(g_tSourceMsaa).y - sampleCoord.y - 1 : sampleCoord.y;

    float invNumSamples = 1.0 / float(D_MSAA_SAMPLES);
    vec3 resolved = vec3(0.0);

#if D_DOF == 1
    float lowestDepth = 1.0;
#endif

    for (int i = 0; i < D_MSAA_SAMPLES; i++)
    {
        vec3 s = texelFetch(g_tSourceMsaa, sampleCoord, i).rgb;
        s = clamp(s, vec3(0.0), vec3(65504.0));
        // Karis average: weight by inverse luminance to reduce fireflies
        resolved += s * (invNumSamples / (max(s.r, max(s.g, s.b)) + 1.0));

#if D_DOF == 1
        lowestDepth = min(texelFetch(g_tSceneDepth, sampleCoord, i).x, lowestDepth);
#endif
    }

    // Inverse Karis tonemap
    resolved *= 1.0 / (1.0 - max(resolved.r, max(resolved.g, resolved.b)));

    float outputAlpha = 1.0;

#if D_DOF == 1
    vec2 sampleUV = (vec2(coord) + 0.5) * g_vInvViewportSize.xy;

    // Convert from screen coords to clipspace (normalised device coordinates)
    vec4 screenToClip = vec4(
        sampleUV.x * 2.0 - 1.0,
        sampleUV.y * 2.0 - 1.0,
        LinearRamp(g_flViewportMinZ, g_flViewportMaxZ, lowestDepth),
        1.0
    );

    // Undo view projection to get back 3d world coordinates
    vec4 worldPos = g_invViewProjMatrix * screenToClip;

    if (worldPos.w != 0.0)
    {
        vec4 worldPos3D = vec4(worldPos.xyz / worldPos.w, 1.0);

        // Projecting worldpos onto the lens plane gives us how far away the point is from the plane
        // g_vLensPlane.w is the plane offset, using dot between 4d vectors for this is clever
        float distanceFromLensPlane = dot(g_vLensPlane, worldPos3D);

        float nearScale = g_vNearFarScaleBias.x;
        float nearBias = g_vNearFarScaleBias.y;
        float farScale = g_vNearFarScaleBias.z;
        float farBias = g_vNearFarScaleBias.w;

        float closeCoC = clamp((distanceFromLensPlane * nearScale) + -nearBias, 0.0, 1.0);
        float farCoC = clamp((distanceFromLensPlane * farScale) + farBias, 0.0, 1.0);

        outputAlpha = max(closeCoC, farCoC);
    }
#endif

    imageStore(g_tDest, coord, vec4(resolved, outputAlpha));
}
