#version 460

#ifndef VRF_SOURCE1_RENDER_MODES_PS_INCLUDE
#define VRF_SOURCE1_RENDER_MODES_PS_INCLUDE

#include "common_ps.slang"
#include "../common/fullbright.slang"
#include "../common/uv_render_modes.slang"

#define renderMode_FullBright 0
#define renderMode_Color 0
#define renderMode_Tint 0
#define renderMode_UvDensity 0
#define renderMode_MipmapUsage 0

// Replaces the color with the view of the active render mode. A surface without a normal passes the view direction as it.
void ApplySource1RenderModes(inout vec4 color, vec3 albedo, vec4 tint, vec3 normal, vec3 viewDirection,
    sampler2D representativeTexture, vec2 texCoord)
{
    if (g_iRenderMode == renderMode_FullBright)
    {
        color = vec4(SrgbLinearToGamma(CalculateFullbrightLighting(albedo, normal, viewDirection)), 1.0);
    }
    else if (g_iRenderMode == renderMode_Color)
    {
        color = vec4(SrgbLinearToGamma(albedo), 1.0);
    }
    else if (g_iRenderMode == renderMode_Tint)
    {
        color = tint;
    }
    else if (g_iRenderMode == renderMode_UvDensity)
    {
        ivec2 dimensions = textureSize(representativeTexture, 0);
        color = vec4(ApplyUvDensityChecker(SrgbLinearToGamma(albedo), texCoord, dimensions), 1.0);
    }
    else if (g_iRenderMode == renderMode_MipmapUsage)
    {
        color = vec4(ApplyMipmapUsageChecker(SrgbLinearToGamma(albedo), representativeTexture, texCoord), 1.0);
    }
}

#endif
