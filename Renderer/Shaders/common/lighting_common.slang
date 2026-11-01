#version 460

#ifndef VRF_LIGHTING_COMMON_INCLUDE
#define VRF_LIGHTING_COMMON_INCLUDE

#include "lighting_constants.slang"
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

// Mirrors SceneLight.LightType
#define LIGHT_TYPE_DIRECTIONAL 0
#define LIGHT_TYPE_SPOT 2

vec3 GetSunDirection()
{
    return g_vSunDirection.xyz;
}

vec3 GetSunColor()
{
    return g_vSunColor.rgb;
}

vec3 GetEnvLightDirection(uint nLightIndex)
{
    return normalize(mat3(g_matLightToWorld[nLightIndex]) * vec3(-1, 0, 0));
}

vec3 GetLightPositionWs(uint nLightIndex)
{
    return g_vLightPosition_Type[nLightIndex].xyz;
}

uint GetLightType(uint nLightIndex)
{
    return uint(g_vLightPosition_Type[nLightIndex].a);
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

// Already premultiplied by brightness.
vec3 GetLightColor(uint nLightIndex)
{
    return g_vLightColor_Brightness[nLightIndex].rgb;
}

// Colors a tile by how many lights or environment maps it iterated, from blue through green and yellow to red at
// busyTile. Returns false for a tile that iterated none.
bool GetTileHeatmap(uint count, float busyTile, out vec3 color)
{
    if (count == 0u)
    {
        color = vec3(0.0);
        return false;
    }

    float t = saturate(float(count) / busyTile) * 3.0;

    color = mix(vec3(0.0, 0.0, 1.0), vec3(0.0, 1.0, 0.0), saturate(t));
    color = mix(color, vec3(1.0, 1.0, 0.0), saturate(t - 1.0));
    color = mix(color, vec3(1.0, 0.0, 0.0), saturate(t - 2.0));

    return true;
}

// Render gates: x = diffuse, y = specular, z = transmissive. Specular is the light color flag;
// the other two ride in the unused zw of the cone cosines.
vec3 GetLightRenderGates(uint nLightIndex)
{
    return vec3(
        g_vLightSpotInnerOuterConeCosines[nLightIndex].z,
        g_vLightColor_Brightness[nLightIndex].a != 0.0 ? 1.0 : 0.0,
        g_vLightSpotInnerOuterConeCosines[nLightIndex].w);
}

#endif
