#version 460

#ifndef VRF_SOURCE1_LOCAL_LIGHTS_PS_INCLUDE
#define VRF_SOURCE1_LOCAL_LIGHTS_PS_INCLUDE

#include "common_ps.slang"
#include "../common/lighting_common.slang"
#include "../common/light_cookie.slang"
#include "../common/shadowmapping.slang"

// A point or spot light without a cookie is a dynamic light of the game, which adds to the lightmap of the surfaces
// it reaches instead of lighting them from its direction.
bool IsSource1DynamicLight(uint lightIndex)
{
    if (IsDirectionalLight(lightIndex))
    {
        return false;
    }

    if (GetLightType(lightIndex) != LIGHT_TYPE_SPOT)
    {
        return true;
    }

    return g_vLightShadowParams[lightIndex].y < 0.0;
}

// The color of a point or spot light at a position with its distance falloff applied, and the direction to it
vec3 GetSource1LightDistanceColor(uint lightIndex, vec3 position, out vec3 lightDirection)
{
    vec3 toLight = GetLightPositionWs(lightIndex) - position;
    float distanceSquared = dot(toLight, toLight);
    vec4 falloff = g_vLightFallOff[lightIndex];

    lightDirection = normalize(toLight);

    if (distanceSquared > falloff.z)
    {
        return vec3(0.0);
    }

    float clampedDistanceSquared = max(distanceSquared, 1.0);
    float attenuation = GetDistanceAttenuation(falloff, clampedDistanceSquared);

    return GetLightColor(lightIndex) * attenuation;
}

// The color of a stored light at a position with its falloff applied, and the direction to it.
// A spot light with a cookie projects it in place of the cone falloff.
vec3 GetSource1LightColor(uint lightIndex, vec3 position, out vec3 lightDirection)
{
    if (IsDirectionalLight(lightIndex))
    {
        lightDirection = GetEnvLightDirection(lightIndex);
        return GetLightColor(lightIndex);
    }

    vec3 color = GetSource1LightDistanceColor(lightIndex, position, lightDirection);

    if (GetLightType(lightIndex) != LIGHT_TYPE_SPOT)
    {
        return color;
    }

    float cookieSlice = g_vLightShadowParams[lightIndex].y;

    if (cookieSlice >= 0.0)
    {
        return color * CalculateDynamicLightCookie(lightIndex, GetLightPositionWs(lightIndex), position, cookieSlice);
    }

    vec2 coneCosines = g_vLightSpotInnerOuterConeCosines[lightIndex].xy;
    vec3 forward = g_vLightDirection_InvRange[lightIndex].xyz;
    float cosAngle = dot(forward, -lightDirection);

    return color * saturate((cosAngle - coneCosines.y) / max(coneCosines.x - coneCosines.y, 0.0001));
}

// The color a dynamic light adds to the lightmap of a surface, which has no cone falloff. A spot light does not reach
// a surface that faces along it, and no light reaches a surface that it is far behind.
vec3 GetSource1DynamicLightColor(uint lightIndex, vec3 position, vec3 surfaceNormal, out vec3 lightDirection)
{
    const float MaxDistanceBehindSurface = 15.0;

    vec3 color = GetSource1LightDistanceColor(lightIndex, position, lightDirection);
    float distanceAbove = dot(surfaceNormal, GetLightPositionWs(lightIndex) - position);

    if (distanceAbove < -MaxDistanceBehindSurface)
    {
        return vec3(0.0);
    }

    if (GetLightType(lightIndex) == LIGHT_TYPE_SPOT)
    {
        vec3 forward = g_vLightDirection_InvRange[lightIndex].xyz;

        if (dot(forward, surfaceNormal) >= 0.0)
        {
            return vec3(0.0);
        }
    }

    return color;
}

#endif
