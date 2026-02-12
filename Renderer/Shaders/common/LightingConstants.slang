static const int MAX_LIGHTS = 256;

[vk::binding(1)]
cbuffer LightingConstants {
    float2 g_vLightmapUvScale;
    bool g_bIsSkybox;
    float _LightingPadding1;

    uint4 g_nNumLights;
    uint4 g_nNumLightsPerShadow;
    float4[MAX_LIGHTS] g_vLightPosition_Type;
    float4[MAX_LIGHTS] g_vLightDirection_InvRange;
    float4x4[MAX_LIGHTS] g_matLightToWorld;
    float4[MAX_LIGHTS] g_vLightColor_Brightness;
    float4[MAX_LIGHTS] g_vLightSpotInnerOuterConeCosines;
    float4[MAX_LIGHTS] g_vLightFallOff;

    float4 g_vEnvMapSizeConstants;
};


