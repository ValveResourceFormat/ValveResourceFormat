#version 460

#ifndef VRF_SELF_ILLUM_PS_INCLUDE
#define VRF_SELF_ILLUM_PS_INCLUDE

// The self illumination of a material that has a mask for it. Include it after surface_material.slang.

uniform sampler2D g_tSelfIllumMask; // SrgbRead(true)
uniform float g_flSelfIllumAlbedoFactor = 1.0;
uniform float g_flSelfIllumBrightness = 0.0;
uniform float g_flSelfIllumScale = 1.0;
uniform vec2 g_vSelfIllumScrollSpeed = vec2(0.0);
uniform vec3 g_vSelfIllumTint = vec3(1.0); // SrgbRead(true)

// The light the mask emits, tinted by the albedo
vec3 SampleSelfIllumination(vec2 texCoord, vec3 albedo)
{
    vec3 illumMask = SampleSelfIllumMask(g_tSelfIllumMask, texCoord, g_vSelfIllumScrollSpeed);
    return GetSelfIllumination(illumMask, albedo, g_flSelfIllumAlbedoFactor, g_flSelfIllumBrightness, g_flSelfIllumScale, g_vSelfIllumTint);
}

#endif
