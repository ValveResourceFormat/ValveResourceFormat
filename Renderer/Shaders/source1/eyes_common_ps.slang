#version 460

#ifndef VRF_SOURCE1_EYES_COMMON_PS_INCLUDE
#define VRF_SOURCE1_EYES_COMMON_PS_INCLUDE

#include "common_ps.slang"
#include "common_model_lighting_ps.slang"
#include "common_rendermodes_lighting_ps.slang"

#define SOURCE1_EYE_MAX_LIGHTS 4

// The lights the model takes, as the direction to the light and its color with the falloff applied
int GatherSource1EyeLights(vec3 position, vec3 normal, out vec3 directions[SOURCE1_EYE_MAX_LIGHTS], out vec3 colors[SOURCE1_EYE_MAX_LIGHTS])
{
    int count = 0;
    uvec4 lightIndices;
    bvec4 lightUsed;

    if (GetSource1ProbeLights(lightIndices, lightUsed))
    {
        for (int i = 0; i < 4; i++)
        {
            if (lightUsed[i])
            {
                colors[count] = GetSource1LightColor(lightIndices[i], position, directions[count]);
                count++;
            }
        }
    }
    else if (g_vSunDirection.xyz != vec3(0.0))
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

        vec3 lightDirection;
        vec3 lightColor = GetSource1LightColor(i, position, lightDirection);

        // A light that does not reach the eye leaves its slot to the next one
        if (lightColor == vec3(0.0))
        {
            continue;
        }

        directions[count] = lightDirection;
        colors[count] = lightColor;
        count++;
    }

    return count;
}

#endif
