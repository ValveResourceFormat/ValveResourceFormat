#version 460

#ifndef VRF_VIEW_CONSTANTS_INCLUDE
#define VRF_VIEW_CONSTANTS_INCLUDE

layout(std140, binding = 0) uniform ViewConstants
{
    mat4 g_matWorldToProjection;
    mat4 g_matProjectionToWorld;
    mat4 g_matWorldToView;
    mat4 g_matViewToProjection;
    vec4 g_vInvProjRow3;
    vec2 g_vInvViewportSize;
    vec2 g_vViewportSize;
    vec3 g_vCameraPositionWs;
    float g_flViewportMinZ;
    vec3 g_vCameraDirWs;
    float g_flViewportMaxZ;
    vec3 g_vCameraUpDirWs;
    float g_flTime;
    mat4 g_matWorldToShadow[3]; // one per sun shadow cascade
    uint g_nSceneIndex; // among the renderer's scenes, for picking
    bool g_bIsSkybox;
    float g_flSunShadowBias;
    bool g_bSunShadowsEnabled;

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

    vec4 g_vEnvWetness;
    vec4 g_vEnvWetnessRipple;

    vec2 g_vSceneDepthFetchScale; // scene depth texels per pixel of the target being drawn
    vec2 g_vVolumetricFogNearRange;

    // metadata for the tile light culling system
    uint g_nLightTileBase;
    uint g_nLightSliceBase;
    uint g_nLightCullWords;
    uint g_nLightTileShift;

    uint g_nLightTileCols;
    uint g_nLightTileRows;
    uint g_nLightSliceCount;
    uint g_nEnvMapTileBase;

    uint g_nEnvMapBinBase;
    uint g_nEnvMapCullWords;
    uint g_nEnvMapCount;
    uint _lightCullPadding0;

    uint g_nLightProbeTileBase;
    uint g_nLightProbeBinBase;
    uint g_nLightProbeCullWords;
    uint g_nLightProbeCount;

    vec4 g_vLightDepthSliceParams;
    vec4 g_vLightCullPixelRemap;

    vec3 g_vLightCullCameraPositionWs;
    float _lightCullPadding1;

    vec3 g_vLightCullCameraDirWs;
    float _lightCullPadding2;
};

// The time snapped to a step of the quantize interval, so animations tick rather than slide.
// A zero interval means no stepping.
float GetQuantizedTime(float quantize)
{
    return (quantize != 0.0) ? (round(g_flTime / quantize) * quantize) : g_flTime;
}

bool Blink()
{
    const float phase = 0;
    return fract((fract(g_flTime * 0.5) + phase)) < 0.5;
}

#endif
