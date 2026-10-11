module ViewConstants;

public struct ViewConstantsData
{
    public float4x4 g_matWorldToProjection;
    public float4x4 g_matProjectionToWorld;
    public float4x4 g_matWorldToView;
    public float4x4 g_matViewToProjection;
    public float4 g_vInvProjRow3;
    public float2 g_vInvViewportSize;
    public float2 g_vViewportSize;
    public float3 g_vCameraPositionWs;
    public float g_flViewportMinZ;
    public float3 g_vCameraDirWs;
    public float g_flViewportMaxZ;
    public float3 g_vCameraUpDirWs;
    public float g_flTime;
    public float4x4 g_matWorldToShadow[2]; // one per sun shadow cascade
    public float2 _viewPadding1;
    public float g_flSunShadowBias;
    public float _viewPadding2;

    public bool3 g_bFogTypeEnabled;
    public int g_iRenderMode;
    public float4 g_vGradientFogBiasAndScale;
    public float4 g_vGradientFogColor_Opacity;
    public float2 m_vGradientFogExponents;
    public float2 g_vGradientFogCullingParams;
    public float4 g_vCubeFog_Offset_Scale_Bias_Exponent;
    public float4 g_vCubeFog_Height_Offset_Scale_Exponent_Log2Mip;
    public float4x4 g_matvCubeFogSkyWsToOs;
    public float4 g_vCubeFogCullingParams_ExposureBias_MaxOpacity;

    public float4x4 g_matPrevWorldToProjection;

    public float4 g_vEnvWetness;
    public float4 g_vEnvWetnessRipple;
};

// bool blink()
//{
//     const float phase = 0;
//     return fract((fract(g_flTime * 0.5) + phase)) < 0.5;
// }
