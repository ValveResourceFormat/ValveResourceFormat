#version 460

#ifndef VRF_VR_SURFACE_LIGHTING_EXTRAS_INCLUDE
#define VRF_VR_SURFACE_LIGHTING_EXTRAS_INCLUDE

// The self illumination and the world lighting gradient that vr_simple and vr_standard share.
// Included after surface_material.slang, with the F_SELF_ILLUM and F_WORLD_LIGHTING_GRADIENT combos of the entry file.

#if (F_SELF_ILLUM == 1)
    uniform sampler2D g_tSelfIllumMask; // SrgbRead(true)
    uniform float g_flSelfIllumScale = 1.0;
    uniform vec2 g_vSelfIllumScrollSpeed = vec2(0.0);
    uniform vec3 g_vSelfIllumTint = vec3(1.0); // SrgbRead(true)
#endif

#if (F_WORLD_LIGHTING_GRADIENT == 1)
    uniform sampler2D g_tWorldLightingGradient; // SrgbRead(true)
    uniform float g_flWorldLightingGradientDistance = 1.0;
    uniform vec3 g_vWorldLightingGradientCenter = vec3(0.0);

    // Tints the indirect light by the distance from a center, over a texture that runs the same in both directions
    vec3 GetWorldLightingGradient(vec3 positionWs)
    {
        float gradientDistance = length(positionWs - g_vWorldLightingGradientCenter) / g_flWorldLightingGradientDistance;
        return texture(g_tWorldLightingGradient, vec2(gradientDistance)).rgb;
    }
#endif

vec3 GetSelfIllumScale(vec2 texCoord)
{
#if (F_SELF_ILLUM == 1)
    return SampleSelfIllumMask(g_tSelfIllumMask, texCoord, g_vSelfIllumScrollSpeed) * g_flSelfIllumScale * g_vSelfIllumTint;
#else
    return vec3(0.0);
#endif
}

#endif
