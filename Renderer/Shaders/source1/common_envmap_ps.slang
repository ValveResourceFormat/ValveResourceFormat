#version 460

#ifndef VRF_SOURCE1_ENVMAP_PS_INCLUDE
#define VRF_SOURCE1_ENVMAP_PS_INCLUDE

#include "common_ps.slang"

uniform samplerCube g_tEnvMap; // SrgbRead(true)
uniform sampler2D g_tEnvMapMask;

uniform int F_ENVMAP;
uniform int F_ENVMAP_MASK;
uniform int F_NORMALMAP_ALPHA_ENVMAP_MASK;
// Each shader reads the tint in the color space of the shader that draws it in the game
uniform vec3 g_vEnvMapTint = vec3(1.0);
uniform float g_flEnvMapContrast = 0.0;
uniform float g_flEnvMapSaturation = 1.0;
uniform float g_flEnvMapFresnel = 0.0;
uniform float g_flFresnelReflection = 1.0;

// -1 flips the bitangent of meshes that follow the Source 2 tangent convention
uniform float g_flBitangentSign = 1.0;

// The three directions the bumped lightmaps are lit from, in tangent space
const vec3 SOURCE1_BUMP_BASIS[3] = vec3[3](
    vec3(0.81649661064147949, 0.0, 0.57735025882720947),
    vec3(-0.40824833512306213, 0.70710676908493042, 0.57735025882720947),
    vec3(-0.40824821591377258, -0.7071068286895752, 0.57735025882720947)
);

// The geometric normal from a vertex normal, or from the screen space derivatives of the position for a mesh
// without normals
vec3 GetSource1SurfaceNormal(vec3 vertexNormal, vec3 position)
{
    if (dot(vertexNormal, vertexNormal) < 0.25)
    {
        return normalize(cross(dFdx(position), dFdy(position)));
    }

    return normalize(vertexNormal);
}

// The tangent frame of a surface: columns are tangent, bitangent and normal in world space
mat3 GetSource1TangentFrame(vec3 normal, vec4 tangent, vec3 position, vec2 texCoord)
{
    if (dot(tangent.xyz, tangent.xyz) >= 0.25)
    {
        vec3 tangentDirection = normalize(tangent.xyz - normal * dot(tangent.xyz, normal));
        vec3 bitangent = cross(normal, tangentDirection) * tangent.w * g_flBitangentSign;
        return mat3(tangentDirection, bitangent, normal);
    }

    vec3 dp1 = dFdx(position);
    vec3 dp2 = dFdy(position);
    vec2 duv1 = dFdx(texCoord);
    vec2 duv2 = dFdy(texCoord);

    vec3 dp2perp = cross(dp2, normal);
    vec3 dp1perp = cross(normal, dp1);
    vec3 tangentDirection = dp2perp * duv1.x + dp1perp * duv2.x;
    vec3 bitangent = dp2perp * duv1.y + dp1perp * duv2.y;

    float scale = inversesqrt(max(max(dot(tangentDirection, tangentDirection), dot(bitangent, bitangent)), 1e-20));

    return mat3(tangentDirection * scale, bitangent * scale, normal);
}

// How much of the cube map each channel shows
vec3 GetSource1EnvmapMask(vec2 texCoord, float baseAlpha, float normalAlpha)
{
    vec3 factor = vec3(1.0);

    if (F_ENVMAP_MASK != 0)
    {
        factor *= texture(g_tEnvMapMask, texCoord).rgb;
    }

    if (F_BASE_ALPHA_ENVMAP_MASK != 0)
    {
        factor *= 1.0 - baseAlpha;
    }

    if (F_NORMALMAP_ALPHA_ENVMAP_MASK != 0)
    {
        factor *= normalAlpha;
    }

    return factor;
}

// The cube map seen through the surface, not yet masked or tinted
vec3 SampleSource1Envmap(vec3 normal, vec3 position)
{
    vec3 eye = g_vCameraPositionWs - position;
    vec3 reflection = 2.0 * normal * dot(normal, eye) - eye * dot(normal, normal);

    return texture(g_tEnvMap, reflection).rgb;
}

// The cube map seen through the surface, tinted and adjusted but not yet faded by a fresnel term
vec3 GetSource1EnvmapColor(vec3 normal, vec3 position, vec3 mask, vec3 tint)
{
    vec3 color = SampleSource1Envmap(normal, position) * mask * tint;

    color = mix(color, color * color, g_flEnvMapContrast);
    vec3 grey = vec3(dot(color, vec3(0.299, 0.587, 0.114)));

    return mix(grey, color, g_flEnvMapSaturation);
}

#endif
