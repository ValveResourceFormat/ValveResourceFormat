#version 460

#ifndef VRF_FOLIAGE_NOISE_VS_INCLUDE
#define VRF_FOLIAGE_NOISE_VS_INCLUDE

// Foliage moved by two layers of wind noise, each masked by a foliage param.

#include "foliage_wind_vs.slang"

uniform float g_flNoise1Strength = 0.0;
uniform vec3 g_vNoise1Speed = vec3(0.0);
uniform vec3 g_vNoise1Scale = vec3(0.0);
uniform float g_flNoise1MaskFalloff = 1.0;
uniform float g_flNoise2Strength = 0.0;
uniform vec3 g_vNoise2Speed = vec3(0.0);
uniform vec3 g_vNoise2Scale = vec3(0.0);
uniform float g_flNoise2MaskFalloff = 1.0;
uniform bool g_bUseEnvWind = false;

vec3 GetWindNoiseOffset(vec3 pos, vec3 scale, vec3 speed, float strength, float mask)
{
    vec3 noisePos = pos * scale * 0.001 + speed * g_flTime;
    vec3 noise = SampleSignedWindNoise(noisePos);

    if (g_bUseEnvWind)
    {
        noise *= WindAlignment(noise) * g_vWindStrengthFreqMulHighStrength.x;
    }

    return noise * strength * mask;
}

// Moves a point by the two noise layers, scaled by the foliage params
vec3 AnimateFoliageNoise(vec3 position, vec4 vertexParams)
{
    vec4 foliageParams = ClampFoliageParams(vertexParams);

    float mask1 = pow(foliageParams.x, g_flNoise1MaskFalloff);
    float mask2 = pow(foliageParams.z, g_flNoise2MaskFalloff);

    vec3 offset1 = GetWindNoiseOffset(position, g_vNoise1Scale, g_vNoise1Speed, g_flNoise1Strength, mask1);
    vec3 offset2 = GetWindNoiseOffset(position, g_vNoise2Scale, g_vNoise2Speed, g_flNoise2Strength, mask2);
    return position + offset1 + offset2;
}

#endif
