#version 460

#ifndef VRF_LIGHTING_COMMON_INCLUDE
#define VRF_LIGHTING_COMMON_INCLUDE

#include "LightingConstants.slang"
#include "utils.slang"

struct LightingTerms_t
{
    vec3 DiffuseDirect;
    vec3 DiffuseIndirect;
    vec3 SpecularDirect;
    vec3 SpecularIndirect;
    vec3 TransmissiveDirect;
    float SpecularOcclusion; // Lightmap AO
};

LightingTerms_t InitLighting()
{
    return LightingTerms_t(vec3(0), vec3(0), vec3(0), vec3(0), vec3(0), 1.0);
}

vec3 GetEnvLightDirection(uint nLightIndex)
{
    return normalize(mat3(g_matLightToWorld[nLightIndex]) * vec3(-1, 0, 0));
}

vec3 GetLightPositionWs(uint nLightIndex)
{
    return g_vLightPosition_Type[nLightIndex].xyz;
}

bool IsDirectionalLight(uint nLightIndex)
{
    return g_vLightPosition_Type[nLightIndex].a == 0.0;
}

vec3 GetLightDirection(vec3 vPositionWs, uint nLightIndex)
{
    if (IsDirectionalLight(nLightIndex))
    {
        return GetEnvLightDirection(nLightIndex);
    }

    vec3 lightPosition = GetLightPositionWs(nLightIndex);
    vec3 lightVector = normalize(lightPosition - vPositionWs);

    return lightVector;
}

vec3 GetLightColor(uint nLightIndex)
{
    vec3 vColor = g_vLightColor_Brightness[nLightIndex].rgb;
    float flBrightness = g_vLightColor_Brightness[nLightIndex].a;

    return vColor * flBrightness;
}

bool IsPointLight(uint nLightIndex)
{
    return g_vLightPosition_Type[nLightIndex].a == 1.0;
}

bool IsSpotLight(uint nLightIndex)
{
    return g_vLightPosition_Type[nLightIndex].a == 2.0;
}

// Get the light's forward/aim direction from its transform matrix.
// In GLSL column-major, mat3 * vec3(1,0,0) extracts column 0.
vec3 GetLightForwardDir(uint nLightIndex)
{
    return normalize(mat3(g_matLightToWorld[nLightIndex]) * vec3(1, 0, 0));
}

// Spotlight cone attenuation with smooth inner-to-outer falloff.
// positionToLightDir points FROM surface TO light.
float GetSpotAttenuation(vec3 positionToLightDir, uint nLightIndex)
{
    vec3 lightForward = GetLightForwardDir(nLightIndex);
    float cosAngle = dot(positionToLightDir, -lightForward);
    float outerCos = g_vLightSpotInnerOuterConeCosines[nLightIndex].y;
    float invConeRange = g_vLightSpotInnerOuterConeCosines[nLightIndex].z;
    return saturate((cosAngle - outerCos) * invConeRange);
}

// FalloffParams = (linear, quadratic, radiusSquared, truncation)
float CalculateDistanceFalloff(float flDistToLightSq, vec4 vFalloffParams)
{
    flDistToLightSq = max(flDistToLightSq, 1.0);
    vec2 vLightDistAndSq = vec2(sqrt(flDistToLightSq), flDistToLightSq);
    float flDot = dot(vLightDistAndSq, vFalloffParams.xy);
    return saturate(1.0 / flDot - vFalloffParams.w);
}

// Distance attenuation with smooth falloff
// https://lisyarus.github.io/blog/graphics/2022/07/30/point-light-attenuation.html
float GetDistanceAttenuation(float distanceToLight, uint nLightIndex)
{
    float invRange = g_vLightDirection_InvRange[nLightIndex].a * 0.5;
    float falloff = g_vLightFallOff[nLightIndex].x;

    float s = distanceToLight * invRange;
    if (s >= 1.0)
        return 0.0;

    float s2 = pow2(s);
    return pow2(1.0 - s2) / (1.0 + falloff * s);
}

#endif
