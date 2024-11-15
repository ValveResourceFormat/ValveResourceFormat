#version 460

layout(std140, binding = 0) uniform ViewConstants {
    mat4 g_matViewToProjection;
    mat4 g_matWorldToProjection;
    mat4 g_matWorldToView;
    vec3 g_vCameraPositionWs;
    float g_flTime;
    mat4 g_matWorldToShadow;
    vec3 _viewPadding1;
    float g_flSunShadowBias;

    bvec3 g_bFogTypeEnabled;
    int g_iRenderMode;
    bool g_bExperimentalLightsEnabled;
    vec3 _viewPadding2;
    vec4 g_vGradientFogBiasAndScale;
    vec4 g_vGradientFogColor_Opacity;
    vec2 m_vGradientFogExponents;
    vec2 g_vGradientFogCullingParams;
    vec4 g_vCubeFog_Offset_Scale_Bias_Exponent;
    vec4 g_vCubeFog_Height_Offset_Scale_Exponent_Log2Mip;
    mat4 g_matvCubeFogSkyWsToOs;
    vec4 g_vCubeFogCullingParams_ExposureBias_MaxOpacity;
};
