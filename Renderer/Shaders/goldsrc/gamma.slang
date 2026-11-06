#version 460

#ifndef VRF_GOLDSRC_GAMMA_INCLUDE
#define VRF_GOLDSRC_GAMMA_INCLUDE

#include "../common/utils.slang"

// Shading happens in gamma space and the result is drawn as it is. The curve below uses these exponents,
// and no extra brightness curve is applied.
uniform float g_flGamma = 2.2;
uniform float g_flLightGamma = 2.5;

// The curve of light before it multiplies a texture
vec3 GoldSrcLightGamma(vec3 light)
{
    return pow(saturate(light), vec3(g_flLightGamma / g_flGamma));
}

#endif
