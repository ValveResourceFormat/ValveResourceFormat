#version 460

#ifndef VRF_SOURCE1_ENVMAP_PS_INCLUDE
#define VRF_SOURCE1_ENVMAP_PS_INCLUDE

#include "common_ps.slang"
#include "../common/lighting_constants.slang"
#include "../common/object_data.slang"
#include "../common/envmap_data.slang"

uniform samplerCube g_tEnvMap; // SrgbRead(true)
uniform samplerCubeArray g_tEnvironmentMap; // SrgbRead(true) reserved
uniform sampler2D g_tEnvMapMask; // Sampler(UserConfig)

uniform int F_ENVMAP;
// 1 when the material reflects env_cubemap, which is the environment map of the scene node, and 0 when it names a cube map
uniform int F_ENVMAP_SCENE;
uniform int F_ENVMAP_MASK;
uniform int F_NORMALMAP_ALPHA_ENVMAP_MASK;
// Linear, converted by the material for the shaders that read it in gamma space
uniform vec3 g_vEnvMapTint = vec3(1.0);
uniform float g_flEnvMapContrast = 0.0;
uniform float g_flEnvMapSaturation = 1.0;
uniform float g_flEnvMapFresnel = 0.0;
uniform float g_flFresnelReflection = 1.0;

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
        vec3 bitangent = cross(normal, tangentDirection) * -tangent.w;
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

// The environment map of the scene node alone, black for a node without one
vec3 SampleSource1SceneEnvmap(vec3 direction)
{
    uint envMapIndex = GetObjectLightingData() >> 16u;

    if (g_vEnvMapSizeConstants.y == 0.0 || envMapIndex == 0xFFFFu)
    {
        return vec3(0.0);
    }

    return texture(g_tEnvironmentMap, vec4(direction, float(envMaps[envMapIndex].ArrayIndex))).rgb;
}

// The cube map the material reflects, in the direction the reflection points to
vec3 SampleSource1Cube(vec3 direction)
{
    if (F_ENVMAP_SCENE != 0)
    {
        return SampleSource1SceneEnvmap(direction);
    }

    return texture(g_tEnvMap, direction).rgb;
}

// The cube map seen through the surface, not yet masked or tinted
vec3 SampleSource1Envmap(vec3 normal, vec3 position)
{
    vec3 eye = g_vCameraPositionWs - position;
    vec3 reflection = 2.0 * normal * dot(normal, eye) - eye * dot(normal, normal);

    return SampleSource1Cube(reflection);
}

#define renderMode_Cubemaps 0

// Replaces the color with the cube map at full reflectivity when the Cubemaps render mode is active
void ApplySource1CubemapRenderMode(inout vec4 color, vec3 normal, vec3 position)
{
    if (g_iRenderMode == renderMode_Cubemaps)
    {
        color = vec4(SrgbLinearToGamma(SampleSource1Envmap(normal, position)), 1.0);
    }
}

// The cube map seen through the surface, tinted and adjusted but not yet faded by a fresnel term
vec3 GetSource1EnvmapColor(vec3 normal, vec3 position, vec3 mask, vec3 tint)
{
    vec3 color = SampleSource1Envmap(normal, position) * mask * tint;

    color = mix(color, color * color, g_flEnvMapContrast);
    vec3 grey = vec3(dot(color, SOURCE1_LUMA_WEIGHTS));

    return mix(grey, color, g_flEnvMapSaturation);
}

#endif
