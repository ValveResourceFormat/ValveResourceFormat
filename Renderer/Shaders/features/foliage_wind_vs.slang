#version 460

#ifndef VRF_FOLIAGE_WIND_VS_INCLUDE
#define VRF_FOLIAGE_WIND_VS_INCLUDE

// The noise volume and wind that the wind driven foliage animations (foliage_sway_vs, foliage_noise_vs,
// foliage_deflection_vs and foliage_vr_vs) share.

uniform sampler3D g_tNoiseMap;

// Fixed wind, not driven by the environment
const vec4 g_vWindDirection = vec4(0.0, -1.0, 0.0, 0.0);
const vec4 g_vWindStrengthFreqMulHighStrength = vec4(0.5);

vec4 ClampFoliageParams(vec4 foliageParams)
{
    return clamp(foliageParams, vec4(0.001), vec4(1.0));
}

// Noise in [-1, 1] at the given coordinates
vec3 SampleSignedWindNoise(vec3 noiseCoords)
{
    return (textureLod(g_tNoiseMap, noiseCoords, 0.0).xyz - vec3(0.5)) * 2.0;
}

// Rotates by twice the angle whose sine scales q.xyz and whose cosine is q.w
vec3 RotateByFoliageQuaternion(vec3 v, vec4 q)
{
    vec3 conjugateAxis = -q.xyz;
    vec3 halfXyz = v * q.w + cross(v, conjugateAxis);
    float halfW = -dot(v, conjugateAxis);
    return halfXyz * q.w + q.xyz * halfW + cross(q.xyz, halfXyz);
}

// Noise along the wind is stronger than across it
float WindAlignment(vec3 noise)
{
    return mix(0.33, 1.0, dot(noise, g_vWindDirection.xyz));
}

#endif
