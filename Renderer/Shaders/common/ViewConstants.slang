#version 460

#ifndef VRF_VIEWCONSTANTS_INCLUDE
#define VRF_VIEWCONSTANTS_INCLUDE

layout(std140, binding = 0) uniform ViewConstants
{
    mat4 g_matWorldToProjection;
    mat4 g_matProjectionToWorld;
    mat4 g_matWorldToView;
    mat4 g_matViewToProjection;
    vec4 g_vInvProjRow3;
    vec4 g_vInvViewportSize;
    vec3 g_vCameraPositionWs;
    float g_flViewportMinZ;
    vec3 g_vCameraDirWs;
    float g_flViewportMaxZ;
    vec3 g_vCameraUpDirWs;
    float g_flTime;
    mat4 g_matWorldToShadow;
    vec2 _viewPadding1;
    float g_flSunShadowBias;
    bool g_bExperimentalLightsEnabled;

    bvec3 g_bFogTypeEnabled;
    int g_iRenderMode;
    vec4 g_vGradientFogBiasAndScale;
    vec4 g_vGradientFogColor_Opacity;
    vec2 m_vGradientFogExponents;
    vec2 g_vGradientFogCullingParams;
    vec4 g_vCubeFog_Offset_Scale_Bias_Exponent;
    vec4 g_vCubeFog_Height_Offset_Scale_Exponent_Log2Mip;
    mat4 g_matvCubeFogSkyWsToOs;
    vec4 g_vCubeFogCullingParams_ExposureBias_MaxOpacity;

    mat4 g_matPrevWorldToProjection;
};

bool blink()
{
    const float phase = 0;
    return fract((fract(g_flTime * 0.5) + phase)) < 0.5;
}

#endif
