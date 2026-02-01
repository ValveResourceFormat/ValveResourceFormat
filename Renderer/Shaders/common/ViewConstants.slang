[vk::binding(0)]
cbuffer 
ViewConstants {
    float4x4 g_matWorldToProjection;
    float4x4 g_matProjectionToWorld;
    float4x4 g_matWorldToView;
    float4x4 g_matViewToProjection;
    float4 g_vInvProjRow3;
    float4 g_vInvViewportSize;
    float3 g_vCameraPositionWs;
    float g_flViewportMinZ;
    float3 g_vCameraDirWs;
    float g_flViewportMaxZ;
    float3 g_vCameraUpDirWs;
    float g_flTime;
    float4x4 g_matWorldToShadow;
    float2 _viewPadding1;
    float g_flSunShadowBias;
    bool g_bExperimentalLightsEnabled;

    bool3 g_bFogTypeEnabled;
    int g_iRenderMode;
    float4 g_vGradientFogBiasAndScale;
    float4 g_vGradientFogColor_Opacity;
    float2 m_vGradientFogExponents;
    float2 g_vGradientFogCullingParams;
    float4 g_vCubeFog_Offset_Scale_Bias_Exponent;
    float4 g_vCubeFog_Height_Offset_Scale_Exponent_Log2Mip;
    float4x4 g_matvCubeFogSkyWsToOs;
    float4 g_vCubeFogCullingParams_ExposureBias_MaxOpacity;
};

bool blink()
{
    const float phase = 0;
    return frac((frac(g_flTime * 0.5) + phase)) < 0.5;
};
