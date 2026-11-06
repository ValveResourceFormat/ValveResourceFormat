#version 460

#ifndef VRF_SOURCE1_MODEL_LIGHTING_PS_INCLUDE
#define VRF_SOURCE1_MODEL_LIGHTING_PS_INCLUDE

#include "common_ps.slang"
#include "../common/lighting_common.slang"
#include "../common/fullbright.slang"
#include "../common/lighting_lpv.slang"
#include "../common/shadowmapping.slang"
#include "common_local_lights_ps.slang"

uniform sampler2D g_tLightWarp;
uniform sampler2D g_tPhongWarp;
uniform sampler2D g_tPhongExponent;

uniform int F_LIGHTWARP;
uniform int F_HALF_LAMBERT;
uniform int F_PHONG;
uniform int F_PHONG_EXPONENT_TEXTURE;
uniform int F_PHONG_WARP;
uniform int F_RIM_LIGHT;

uniform float g_flRimLightExponent = 4.0;

#define SOURCE1_MAX_LOCAL_LIGHTS 4

// Set for the materials of a model that boosts its ambient cube where local lights swamp it
uniform float g_flAmbientBoost = 0.0;

// The scalar diffuse term of a light, through the light warp texture when the material has one
vec3 GetSource1DiffuseTerm(float normalDotLight)
{
    float term = saturate(normalDotLight);

    if (F_HALF_LAMBERT != 0)
    {
        term = saturate(normalDotLight * 0.5 + 0.5);

        if (F_LIGHTWARP == 0)
        {
            term *= term;
        }
    }

    if (F_LIGHTWARP != 0)
    {
        return 2.0 * texture(g_tLightWarp, saturate(vec2(term, 0.5))).rgb;
    }

    return vec3(term);
}

// Adds one light to the diffuse, specular and rim sums. fresnel is the phong fresnel factor of the surface.
void AddSource1Light(inout vec3 diffuse, inout vec3 specular, inout vec3 rim,
    vec3 normal, vec3 lightDirection, vec3 lightColor, vec3 eyeDirection, float specularExponent, float fresnel)
{
    diffuse += lightColor * GetSource1DiffuseTerm(dot(normal, lightDirection));

    if (F_PHONG == 0 && F_RIM_LIGHT == 0)
    {
        return;
    }

    vec3 reflection = 2.0 * normal * dot(normal, eyeDirection) - eyeDirection;
    float lightDotReflection = saturate(dot(reflection, lightDirection));
    float normalDotLight = saturate(dot(normal, lightDirection));

    if (F_PHONG != 0)
    {
        vec3 specularTerm = vec3(pow(lightDotReflection, specularExponent));

        if (F_PHONG_WARP != 0)
        {
            specularTerm *= texture(g_tPhongWarp, saturate(vec2(specularTerm.x, fresnel))).rgb;
        }

        specular += specularTerm * normalDotLight * lightColor;
    }

    if (F_RIM_LIGHT != 0)
    {
        rim += pow(lightDotReflection, g_flRimLightExponent) * normalDotLight * lightColor;
    }
}

// How far a point of a surface that is parallel to the sun rays moves off it along the normal, in world units
#define SOURCE1_SUN_SHADOW_NORMAL_OFFSET 1.6

// The visibility of the sun at a point of a surface. The point is moved off the surface along the normal, more the
// closer the surface is to parallel to the sun rays, so that the surface does not shadow itself.
float GetSource1SunVisibility(vec3 position, vec3 normal)
{
    vec3 sunDirection = g_vSunDirection.xyz;
    float sinAngle = length(normal - sunDirection * dot(normal, sunDirection));

    return CalculateSunShadowMapVisibility(position + normal * (SOURCE1_SUN_SHADOW_NORMAL_OFFSET * sinAngle));
}

// The light baked into a vertex. Where the bake left out the sun, the alpha holds how much the sun lights the vertex,
// and the sun is added back darkened by the sun shadow cascades.
vec3 GetSource1BakedVertexLight(vec4 bakedLight, vec3 position, vec3 normal)
{
    if (bakedLight.a <= 0.0)
    {
        return bakedLight.rgb;
    }

    return bakedLight.rgb + bakedLight.a * g_vSunColor.rgb * GetSource1SunVisibility(position, normal);
}

// The direct lights the map picked for the lighting origin of the object, from the probe volume that covers it.
// A model takes the same lights all over, as the game lights it from one point. Returns false where no volume
// covers the object.
bool GetSource1ProbeLights(out uvec4 lightIndices, out bvec4 lightUsed)
{
    lightIndices = uvec4(0u);
    lightUsed = bvec4(false);

#if (BAKED_LIGHT_INDICES)
    uint volume = GetObjectLightingData() & 0xFFFFu;

    if (volume == NO_LIGHT_PROBE_VOLUME)
    {
        return false;
    }

    vec3 coords = CalculateProbeShadowCoords(g_LPV[volume], CalculateProbeSampleCoords(volume, GetObjectLightingOrigin()));

    lightIndices = uvec4(textureLod(g_tLPV_Indices, coords, 0.0) * 255.0 + 0.5);
    lightUsed = greaterThan(textureLod(g_tLPV_Scalars, coords, 0.0), vec4(0.5));

    return true;
#else
    return false;
#endif
}

// The end of the range of the first dynamic lights of the scene that light a model
uint GetSource1LastModelLight()
{
    return min(g_nNumLights[1], g_nNumLights[0] + uint(SOURCE1_MAX_LOCAL_LIGHTS));
}

// Adds the first dynamic lights of the scene, which light every model they reach
void AddSource1DynamicLights(inout vec3 diffuse, inout vec3 specular, inout vec3 rim,
    vec3 position, vec3 normal, vec3 eyeDirection, float specularExponent, float fresnel)
{
    uint lastLight = GetSource1LastModelLight();

    for (uint i = g_nNumLights[0]; i < lastLight; i++)
    {
        if (IsDirectionalLight(i))
        {
            continue;
        }

        vec3 lightDirection;
        vec3 lightColor = GetSource1LightColor(i, position, lightDirection);

        AddSource1Light(diffuse, specular, rim, normal, lightDirection, lightColor, eyeDirection, specularExponent, fresnel);
    }
}

// Adds the direct lights the map picked for the object, or the sun where the map picked none, and the dynamic lights
void AddSource1SceneLights(inout vec3 diffuse, inout vec3 specular, inout vec3 rim,
    vec3 position, vec3 normal, vec3 eyeDirection, float specularExponent, float fresnel)
{
    uvec4 lightIndices;
    bvec4 lightUsed;

    if (GetSource1ProbeLights(lightIndices, lightUsed))
    {
        for (int i = 0; i < 4; i++)
        {
            if (!lightUsed[i])
            {
                continue;
            }

            vec3 lightDirection;
            vec3 lightColor = GetSource1LightColor(lightIndices[i], position, lightDirection);

            AddSource1Light(diffuse, specular, rim, normal, lightDirection, lightColor, eyeDirection, specularExponent, fresnel);
        }
    }
    else if (g_vSunDirection.xyz != vec3(0.0))
    {
        AddSource1Light(diffuse, specular, rim, normal, g_vSunDirection.xyz, g_vSunColor.rgb, eyeDirection, specularExponent, fresnel);
    }

    AddSource1DynamicLights(diffuse, specular, rim, position, normal, eyeDirection, specularExponent, fresnel);
}

float GetSource1Luminance(vec3 color)
{
    return dot(color, vec3(0.3, 0.59, 0.11));
}

// The luminance of a light that reaches a point, with its falloff but not its shadow
float GetSource1LightLuminance(uint lightIndex, vec3 position)
{
    if (IsDirectionalLight(lightIndex))
    {
        return GetSource1Luminance(GetLightColor(lightIndex));
    }

    vec3 lightDirection;
    return GetSource1Luminance(GetSource1LightColor(lightIndex, position, lightDirection));
}

// The luminance of all the direct lights that reach the lighting origin of the object
float GetSource1DirectLightLuminance(vec3 position)
{
    float luminance = 0.0;
    uvec4 lightIndices;
    bvec4 lightUsed;

    if (GetSource1ProbeLights(lightIndices, lightUsed))
    {
        for (int i = 0; i < 4; i++)
        {
            if (lightUsed[i])
            {
                luminance += GetSource1LightLuminance(lightIndices[i], position);
            }
        }
    }
    else if (g_vSunDirection.xyz != vec3(0.0))
    {
        luminance += GetSource1Luminance(g_vSunColor.rgb);
    }

    uint lastLight = GetSource1LastModelLight();

    for (uint i = g_nNumLights[0]; i < lastLight; i++)
    {
        if (!IsDirectionalLight(i))
        {
            luminance += GetSource1LightLuminance(i, position);
        }
    }

    return luminance;
}

// How much the ambient cube is scaled for a model that asks for a boost: when the cube is dim and a fraction of the
// direct light is brighter than it, the cube is raised to that fraction, by at most a fixed factor.
float GetSource1AmbientBoost(vec3 position)
{
    const float ambientMin = 0.3;
    const float ambientFraction = 0.2;
    const float ambientFactor = 5.0;

    float sum = 0.0;
    float maxLuminance = 0.0;

    for (int i = 0; i < 6; i++)
    {
        vec3 axis = vec3(0.0);
        axis[i / 2] = (i % 2 == 0) ? 1.0 : -1.0;

        float luminance = GetSource1Luminance(max(SampleLightProbeIrradiance(position, axis), vec3(0.0)));
        sum += luminance;
        maxLuminance = max(maxLuminance, luminance);
    }

    float average = sum / 6.0;

    if (average >= ambientMin)
    {
        return 1.0;
    }

    float boosted = GetSource1DirectLightLuminance(position) * ambientFraction;

    if (average >= boosted)
    {
        return 1.0;
    }

    return min(boosted / max(maxLuminance, 0.00001), ambientFactor);
}

// The ambient cube the map baked at the lighting origin of the object, or the sky where no volume covers it.
// A model is lit by one cube, so every fragment of it gets the same ambient.
vec3 GetSource1Ambient(vec3 normal)
{
    vec3 position = GetObjectLightingOrigin();
    vec3 ambient = max(SampleLightProbeIrradiance(position, normal), vec3(0.0));

    if (g_flAmbientBoost > 0.0)
    {
        ambient *= GetSource1AmbientBoost(position);
    }

    return ambient;
}

// The phong fresnel, a piecewise curve of the traditional fresnel through the min, mid and max of the ranges
float GetSource1FresnelRanges(vec3 normal, vec3 eyeDirection, vec4 ranges)
{
    float lowSlope = (ranges.y - ranges.x) * 2.0;
    float highSlope = (ranges.z - ranges.y) * 2.0;

    float fresnel = saturate(1.0 - dot(normal, eyeDirection));
    fresnel = fresnel * fresnel - 0.5;

    if (fresnel >= 0.0)
    {
        return ranges.y + highSlope * fresnel;
    }

    return ranges.y + lowSlope * fresnel;
}

// The scene lit diffuse term of a surface, which never comes out black unless a volume of the map covers it and is that dark
vec3 GetSource1FullbrightFallback(vec3 lighting, vec3 normal)
{
    bool covered = GetLightProbeSample(GetObjectLightingOrigin(), normal).Coverage > 0.0;

    if (!covered && Max3(lighting) < 0.001)
    {
        return vec3(GetFullbrightAxisLight(normal));
    }

    return lighting;
}

#endif
