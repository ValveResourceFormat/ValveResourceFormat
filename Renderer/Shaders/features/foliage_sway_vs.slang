#version 460

#ifndef VRF_FOLIAGE_SWAY_VS_INCLUDE
#define VRF_FOLIAGE_SWAY_VS_INCLUDE

// Wind sway and flutter of foliage, driven by the per-vertex foliage params.

#include "foliage_wind_vs.slang"

uniform float g_flSwayAmount = 0.25;
uniform float g_flSwaySpeed = 1.0;
uniform float g_flSwayNoiseScale = 1.0;
uniform float g_flSwayFalloff = 1.0;
uniform float g_flFlutterAmount = 0.25;
uniform float g_flFlutterSpeed = 1.0;
uniform float g_flFlutterNoiseScale = 1.0;
uniform float g_flFlutterFalloff = 1.0;

struct FoliageAnimParams
{
    // Base animation strengths and falloffs
    float swayStrength;
    float flutterStrength;
    float swayFalloff;
    float flutterFalloff;

    // Time-based offsets
    vec3 swayTimeOffset;
    vec3 flutterTimeOffset;

    // Noise sampling scales
    vec3 swayNoiseScale;
    vec3 flutterNoiseScale;

    // Height-based parameters
    float heightBlend;
    vec3 heightOffset;
};

vec3 VertexAnimation(vec3 pos, FoliageAnimParams params)
{
    // Sample and calculate sway noise
    vec3 swayNoisePos = ((pos + params.heightOffset) * params.swayNoiseScale) + params.swayTimeOffset;
    float swayNoiseMask = saturate(pow2(textureLod(g_tNoiseMap, swayNoisePos * vec3(0.4, 0.4, 0.125), 0.0).z) * 1.5);
    vec3 swayNoise = SampleSignedWindNoise(swayNoisePos);
    vec3 swayDir = vec3(swayNoise.xy, swayNoise.y * 0.5) * 0.5;

    // Calculate final sway offset
    vec3 swayOffset = ((swayDir * swayNoiseMask + g_vWindDirection.xyz) * params.swayFalloff *
                    g_vWindStrengthFreqMulHighStrength.x * params.swayStrength) * swayNoiseMask;

    // Sample and calculate flutter noise
    vec3 flutterNoisePos = ((pos + vec3(params.heightBlend * 10.0, params.heightBlend * 5.0, params.heightBlend * 5.0)) *
                        params.flutterNoiseScale) + params.flutterTimeOffset;
    vec3 flutterNoise = SampleSignedWindNoise(flutterNoisePos);

    // Calculate final flutter offset
    vec3 flutterOffset = ((flutterNoise * g_vWindDirection.xyz + flutterNoise * swayDir) *
                        g_vWindStrengthFreqMulHighStrength.x * params.flutterStrength) * params.flutterFalloff;

    // Each offset drops by its own horizontal length to compensate for gravity
    vec3 swayGravity = vec3(0.0, 0.0, -length(swayOffset.xy) * 0.314);
    vec3 flutterGravity = vec3(0.0, 0.0, -length(flutterOffset.xy) * 0.314);
    return pos + swayOffset + swayGravity + flutterOffset + flutterGravity;
}

FoliageAnimParams GetFoliageAnimParams(vec4 foliageParams)
{
    FoliageAnimParams animParams;

    // Calculate strengths
    animParams.swayStrength = 150.0 * g_flSwayAmount;
    animParams.flutterStrength = 25.0 * g_flFlutterAmount;
    animParams.swayFalloff = pow(foliageParams.x, g_flSwayFalloff);
    animParams.flutterFalloff = pow(foliageParams.z, g_flFlutterFalloff);

    // Calculate height blend and offset
    animParams.heightBlend = (2.0 * foliageParams.y) - 1.0;
    animParams.heightOffset = vec3(animParams.heightBlend * 100.0, 0.0, animParams.heightBlend * 50.0);

    // Set time-based movement offsets
    animParams.swayTimeOffset = vec3(-0.3, 0.0, -0.03) * g_flSwaySpeed * g_flTime;
    animParams.flutterTimeOffset = vec3(-0.35, 0.0, 0.1) * g_flFlutterSpeed * g_flTime;

    // Set noise sampling scales
    animParams.swayNoiseScale = vec3(0.00075, 0.00075, 0.00005) * g_flSwayNoiseScale;
    animParams.flutterNoiseScale = vec3(0.0025, 0.0025, 0.0005) * g_flFlutterNoiseScale;

    return animParams;
}

// Moves a point by the wind, scaled by the foliage params
vec3 AnimateFoliageSway(vec3 position, vec4 vertexParams)
{
    vec4 foliageParams = ClampFoliageParams(vertexParams);
    return VertexAnimation(position, GetFoliageAnimParams(foliageParams));
}

#endif
