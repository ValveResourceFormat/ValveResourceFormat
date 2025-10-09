#version 460

#define MAX_LIGHTS 256
#define MAX_ENVMAPS 128

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
    mat4 g_matEnvMapWorldToLocal[MAX_ENVMAPS];
    vec4[MAX_ENVMAPS] g_vEnvMapBoxMins;
    vec4[MAX_ENVMAPS] g_vEnvMapBoxMaxs;
    vec4[MAX_ENVMAPS] g_vEnvMapEdgeFadeDists;
    vec4[MAX_ENVMAPS] g_vEnvMapProxySphere;
    vec4[MAX_ENVMAPS] g_vEnvMapColorRotated;
    vec4[MAX_ENVMAPS] g_vEnvMapNormalizationSH;
};
