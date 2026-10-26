#version 460

#ifndef VRF_LIGHTINGCONSTANTS_INCLUDE
#define VRF_LIGHTINGCONSTANTS_INCLUDE

#include "utils.slang"

#define MAX_LIGHTS 256

layout(std140, binding = 1) uniform LightingConstants {
    vec2 g_vLightmapUvScale;
    uint g_nEnvMapDebugColors;       // reflections take each env map's debug colour from its data
    uint g_nNumBarnLights;
    uvec4 g_nNumLights;
    vec4 g_vSunDirection;            // xyz: surface-to-sun, zero when the scene has no sun. w: min roughness
    vec4 g_vSunColor;                // rgb: color * brightness, a: flags
    vec4 g_vSunLightBakedShadowMask; // V2: one-hot baked shadow channel mask. V1: baked light index in x (-1 none)
    vec4[MAX_LIGHTS] g_vLightPosition_Type;
    vec4[MAX_LIGHTS] g_vLightDirection_InvRange;
    mat4[MAX_LIGHTS] g_matLightToWorld;
    vec4[MAX_LIGHTS] g_vLightColor_Brightness;
    vec4[MAX_LIGHTS] g_vLightSpotInnerOuterConeCosines;
    vec4[MAX_LIGHTS] g_vLightFallOff;

    vec4 g_vEnvMapSizeConstants;

    vec4 g_vAmbientLightingSH[3];    // per color channel, xyz: against the normal, w: constant

    vec4 g_vUnbakedIndirect;         // rgb: indirect light where none is baked and no cube map gives it
};

// The lightmap atlas coordinates in xy, and a hash of the unscaled coordinates in zw. The hash only
// interpolates to a constant across a triangle whose vertices all share one lightmap position.
vec4 GetLightmapCoordinates(vec2 lightmapUV)
{
    uvec2 bits = floatBitsToUint(lightmapUV);
    bits ^= bits << 13u;
    bits ^= bits >> 17u;
    bits ^= bits << 5u;

    vec2 hash = (uintBitsToFloat(0x3F800000u | (bits >> 9u)) - 1.0) * 65535.0;

    return vec4(lightmapUV * g_vLightmapUvScale, hash);
}

// The sky's irradiance arriving around a direction
vec3 GetSkyAmbientLighting(vec3 direction)
{
    return vec4(direction, 1.0) * mat3x4(g_vAmbientLightingSH[0], g_vAmbientLightingSH[1], g_vAmbientLightingSH[2]);
}

// The luminance of GetSkyAmbientLighting, packed like an environment map's normalization harmonic
vec4 GetSkyAmbientLuminanceSH()
{
    vec4 luminance = GetLuma(g_vAmbientLightingSH[0], g_vAmbientLightingSH[1], g_vAmbientLightingSH[2]);

    // Kept from turning negative opposite the sky
    luminance.z = min(luminance.z, 0.95 * luminance.w);

    return luminance;
}

#endif
