#version 460

#ifndef VRF_SOURCE1_MODEL_LIGHTING_PS_INCLUDE
#define VRF_SOURCE1_MODEL_LIGHTING_PS_INCLUDE

#include "common_ps.slang"
#include "../common/lighting_common.slang"
#include "../common/fullbright.slang"

uniform sampler2D g_tLightWarp;
uniform sampler2D g_tPhongWarp;
uniform sampler2D g_tPhongExponent;

uniform int F_AMBIENT_CUBE;
uniform int F_LIGHTWARP;
uniform int F_HALF_LAMBERT;
uniform int F_PHONG;
uniform int F_PHONG_EXPONENT_TEXTURE;
uniform int F_PHONG_WARP;
uniform int F_RIM_LIGHT;

// Ambient cube faces: +X, -X, +Y, -Y, +Z, -Z in world space
uniform vec4 g_vAmbientCube0 = vec4(0.0);
uniform vec4 g_vAmbientCube1 = vec4(0.0);
uniform vec4 g_vAmbientCube2 = vec4(0.0);
uniform vec4 g_vAmbientCube3 = vec4(0.0);
uniform vec4 g_vAmbientCube4 = vec4(0.0);
uniform vec4 g_vAmbientCube5 = vec4(0.0);

uniform float g_flRimLightExponent = 4.0;

#define SOURCE1_MAX_LOCAL_LIGHTS 4

vec3 GetSource1Ambient(vec3 normal)
{
    if (F_AMBIENT_CUBE == 0)
    {
        return max(GetSkyAmbientLighting(normal), vec3(0.0));
    }

    vec3 squared = normal * normal;
    vec3 xColor = normal.x >= 0.0 ? g_vAmbientCube0.rgb : g_vAmbientCube1.rgb;
    vec3 yColor = normal.y >= 0.0 ? g_vAmbientCube2.rgb : g_vAmbientCube3.rgb;
    vec3 zColor = normal.z >= 0.0 ? g_vAmbientCube4.rgb : g_vAmbientCube5.rgb;

    return squared.x * xColor + squared.y * yColor + squared.z * zColor;
}

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
        return 2.0 * texture(g_tLightWarp, vec2(term, 0.5)).rgb;
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
            specularTerm *= texture(g_tPhongWarp, vec2(specularTerm.x, fresnel)).rgb;
        }

        specular += specularTerm * normalDotLight * lightColor;
    }

    if (F_RIM_LIGHT != 0)
    {
        rim += pow(lightDotReflection, g_flRimLightExponent) * normalDotLight * lightColor;
    }
}

// Adds the sun and the first local lights of the scene
void AddSource1SceneLights(inout vec3 diffuse, inout vec3 specular, inout vec3 rim,
    vec3 position, vec3 normal, vec3 eyeDirection, float specularExponent, float fresnel)
{
    if (g_vSunDirection.xyz != vec3(0.0))
    {
        AddSource1Light(diffuse, specular, rim, normal, g_vSunDirection.xyz, g_vSunColor.rgb, eyeDirection, specularExponent, fresnel);
    }

    uint firstLight = g_nNumLights[0];
    uint lastLight = min(g_nNumLights[1], firstLight + uint(SOURCE1_MAX_LOCAL_LIGHTS));

    for (uint i = firstLight; i < lastLight; i++)
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

        AddSource1Light(diffuse, specular, rim, normal, lightDirection, GetLightColor(i) * attenuation, eyeDirection, specularExponent, fresnel);
    }
}

// The scene lit diffuse term of a surface, which never comes out black unless the map gave it an ambient cube that is
vec3 GetSource1FullbrightFallback(vec3 lighting, vec3 normal)
{
    if (F_AMBIENT_CUBE == 0 && Max3(lighting) < 0.001)
    {
        return vec3(GetFullbrightAxisLight(normal));
    }

    return lighting;
}

#endif
