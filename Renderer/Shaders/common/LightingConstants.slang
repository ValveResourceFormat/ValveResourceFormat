#version 460

#ifndef VRF_LIGHTINGCONSTANTS_INCLUDE
#define VRF_LIGHTINGCONSTANTS_INCLUDE

#define MAX_LIGHTS 256

layout(std140, binding = 1) uniform LightingConstants {
    vec2 g_vLightmapUvScale;
    bool g_bIsSkybox;
    uint g_nNumBarnLights;
    uvec4 g_nNumLights;
    vec4 g_vSunDirection;            // xyz: surface-to-sun, zero when the scene has no sun
    vec4 g_vSunColor;                // rgb: color * brightness, a: flags
    vec4 g_vSunLightBakedShadowMask; // V2: one-hot baked shadow channel mask. V1: baked light index in x (-1 none)
    vec4[MAX_LIGHTS] g_vLightPosition_Type;
    vec4[MAX_LIGHTS] g_vLightDirection_InvRange;
    mat4[MAX_LIGHTS] g_matLightToWorld;
    vec4[MAX_LIGHTS] g_vLightColor_Brightness;
    vec4[MAX_LIGHTS] g_vLightSpotInnerOuterConeCosines;
    vec4[MAX_LIGHTS] g_vLightFallOff;

    vec4 g_vEnvMapSizeConstants;
};

#endif
