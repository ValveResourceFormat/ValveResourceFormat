#version 460

#include "common/utils.glsl"
#include "common/ViewConstants.glsl"

out vec4 outputColor;

uniform sampler2D g_tSceneDepth;

#define D_OUTLINE_PASS 0

float GetDepthNormalized(ivec2 vScreenPosition)
{
    float flSceneDepth = texelFetch(g_tSceneDepth, vScreenPosition, 0).x;
    float flSceneDepthNormalized = RemapValClamped(flSceneDepth, g_flViewportMinZ, g_flViewportMaxZ, 0.0, 1.0);

    return flSceneDepthNormalized;
}

void main()
{
    float objectDepth = gl_FragCoord.z;

    ivec2 vScreenPosition = ivec2(gl_FragCoord.xy);
    float flDepthNormalized = GetDepthNormalized(vScreenPosition);

    float diff = objectDepth - flDepthNormalized;

    #if (D_OUTLINE_PASS == 0)
        outputColor = vec4(1.0, 0.0, 0.0, 1.0); // Object color
        return;
    #endif


    if (diff > 0.0)
    {
        outputColor = vec4(2.0, 2.0, flDepthNormalized, 0.5); // Visible outline color
    }
    else
    {
        outputColor = vec4(0.0, 1.0, 0.0, 0.2); // Obscured outline color
    }
}

