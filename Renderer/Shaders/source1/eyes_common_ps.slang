#version 460

#ifndef VRF_SOURCE1_EYES_COMMON_PS_INCLUDE
#define VRF_SOURCE1_EYES_COMMON_PS_INCLUDE

#include "common_ps.slang"
#include "common_model_lighting_ps.slang"

#define SOURCE1_EYE_MAX_LIGHTS 4

// Center of the eyeball in world space
uniform vec3 g_vEyeOrigin;
// Planar projection of the iris texture, as the dot product of these with the world position and 1
uniform vec4 g_vIrisProjectionU;
uniform vec4 g_vIrisProjectionV;

vec2 GetSource1IrisCoords(vec3 position)
{
    vec4 position4 = vec4(position, 1.0);
    return vec2(dot(g_vIrisProjectionU, position4), dot(g_vIrisProjectionV, position4));
}

// The sun and the nearest local lights, as the direction to the light and its color with the falloff applied
int GatherSource1EyeLights(vec3 position, out vec3 directions[SOURCE1_EYE_MAX_LIGHTS], out vec3 colors[SOURCE1_EYE_MAX_LIGHTS])
{
    int count = 0;

    if (g_vSunDirection.xyz != vec3(0.0))
    {
        directions[count] = g_vSunDirection.xyz;
        colors[count] = g_vSunColor.rgb;
        count++;
    }

    uint firstLight = g_nNumLights[0];
    uint lastLight = g_nNumLights[1];

    for (uint i = firstLight; i < lastLight && count < SOURCE1_EYE_MAX_LIGHTS; i++)
    {
        if (IsDirectionalLight(i))
        {
            continue;
        }

        vec3 toLight = GetLightPositionWs(i) - position;
        float distanceSquared = dot(toLight, toLight);
        vec4 falloff = g_vLightFallOff[i];

        if (distanceSquared > falloff.z)
        {
            continue;
        }

        float clampedDistanceSquared = max(distanceSquared, 1.0);
        float attenuation = saturate(1.0 / dot(vec2(sqrt(clampedDistanceSquared), clampedDistanceSquared), falloff.xy) - falloff.w);
        vec3 lightDirection = normalize(toLight);

        if (GetLightType(i) == LIGHT_TYPE_SPOT)
        {
            vec2 coneCosines = g_vLightSpotInnerOuterConeCosines[i].xy;
            vec3 forward = g_vLightDirection_InvRange[i].xyz;
            float cosAngle = dot(forward, -lightDirection);
            attenuation *= saturate((cosAngle - coneCosines.y) / max(coneCosines.x - coneCosines.y, 0.0001));
        }

        directions[count] = lightDirection;
        colors[count] = GetLightColor(i) * attenuation;
        count++;
    }

    return count;
}

#endif
