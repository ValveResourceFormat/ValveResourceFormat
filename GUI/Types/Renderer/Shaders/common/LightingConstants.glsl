#version 460

#define MAX_LIGHTS 256
#define MAX_ENVMAPS 144

layout(std140, binding = 1) uniform LightingConstants
{
    vec2 g_vLightmapUvScale;
    float g_flSunShadowBias;
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
    vec4[MAX_ENVMAPS] g_vEnvMapEdgeFadeDistsInv;
    vec4[MAX_ENVMAPS] g_vEnvMapProxySphere;
    vec4[MAX_ENVMAPS] g_vEnvMapColorRotated;
    vec4[MAX_ENVMAPS] g_vEnvMapNormalizationSH;
};

struct LightProbeVolumeData
{
    mat4 WorldToLocalNormalizer;
    vec4 Min;
    vec4 Max;
    vec4 AtlasScale;
    vec4 AtlasOffset;
};

layout(std140, binding = 2) uniform LPVConstants
{
    LightProbeVolumeData[MAX_ENVMAPS] g_vLightProbeVolumeData;
};
