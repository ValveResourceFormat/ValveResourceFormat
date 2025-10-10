#version 460

#define MAX_LIGHTS 256

layout(std140, binding = 1) uniform LightingConstants {
    vec2 g_vLightmapUvScale;
    bool g_bIsSkybox;
    float _LightingPadding1;

    uvec4 g_nNumLights;
    uvec4 g_nNumLightsPerShadow;
    vec4[MAX_LIGHTS] g_vLightPosition_Type;
    vec4[MAX_LIGHTS] g_vLightDirection_InvRange;
    mat4[MAX_LIGHTS] g_matLightToWorld;
    vec4[MAX_LIGHTS] g_vLightColor_Brightness;
    vec4[MAX_LIGHTS] g_vLightSpotInnerOuterConeCosines;
    vec4[MAX_LIGHTS] g_vLightFallOff;

    vec4 g_vEnvMapSizeConstants;
};
