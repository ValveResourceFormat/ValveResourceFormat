layout(std140) uniform ViewConstants {
    mat4 g_matViewToProjection;
    mat4 g_matWorldToProjection;
    mat4 g_matWorldToView;
    vec3 g_vCameraPositionWs;
    float g_flTime;
    bvec4 g_bFogTypeEnabled;
    vec4 g_vGradientFogBiasAndScale;
    vec4 g_vGradientFogColor_Opacity;
    vec2 m_vGradientFogExponents;
    vec2 g_vGradientFogCullingParams;
    vec4 g_vCubeFog_Offset_Scale_Bias_Exponent;
    vec4 g_vCubeFog_Height_Offset_Scale_Exponent_Log2Mip;
    mat4 g_matvCubeFogSkyWsToOs;
    vec4 g_vCubeFogCullingParams_ExposureBias_MaxOpacity;
};

