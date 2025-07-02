// SPIR-V source (81228 bytes), GLSL reflection with SPIRV-Cross by KhronosGroup
// Source 2 Viewer 12.0.0.0 - https://valveresourceformat.github.io

#version 460
#extension GL_EXT_samplerless_texture_functions : require
#if defined(GL_EXT_control_flow_attributes)
#extension GL_EXT_control_flow_attributes : require
#define SPIRV_CROSS_FLATTEN [[flatten]]
#define SPIRV_CROSS_BRANCH [[dont_flatten]]
#define SPIRV_CROSS_UNROLL [[unroll]]
#define SPIRV_CROSS_LOOP [[dont_unroll]]
#else
#define SPIRV_CROSS_FLATTEN
#define SPIRV_CROSS_BRANCH
#define SPIRV_CROSS_UNROLL
#define SPIRV_CROSS_LOOP
#endif
#extension GL_KHR_shader_subgroup_arithmetic : require

struct anon_g_matPrimaryViewWorldToProjection
{
    vec4 _m0[4];
};

struct _97
{
    vec4 _m0[3];
};

struct _308
{
    vec4 _m0[4];
};

struct _98
{
    vec4 _m0[3];
};

struct _2144
{
    anon_g_matPrimaryViewWorldToProjection _m0[4];
};

struct _2096
{
    anon_g_matPrimaryViewWorldToProjection _m0;
    anon_g_matPrimaryViewWorldToProjection _m1;
    vec4 _m2;
    vec4 _m3;
    vec4 _m4;
    vec4 _m5;
    vec3 _m6;
    int _m7;
    vec4 _m8;
    vec4 _m9;
    vec4 _m10;
    float _m11;
    float _m12;
    uint _m13;
    uint _m14;
    _98 _m15;
    vec4 _m16;
    vec4 _m17;
    vec4 _m18;
    vec4 _m19;
    vec4 _m20;
    vec3 _m21;
    float _m22;
};

float _21011;
vec4 _17208;
vec3 _20799;

struct _2643
{
    int g_bFogEnabled;
    int g_bDontFlipBackfaceNormals;
    int g_bRenderBackfaceNormals;
    float g_flWaterPlaneOffset;
    float g_flSkyBoxScale;
    float g_flSkyBoxFadeRange;
    vec2 g_vMapUVMin;
    vec2 g_vMapUVMax;
    float g_flLowEndCubeMapIntensity;
    float g_flWaterRoughnessMin;
    float g_flWaterRoughnessMax;
    float g_flFoamMin;
    float g_flFoamMax;
    float g_flDebrisMin;
    float g_flDebrisMax;
    vec3 g_vDebrisTint;
    float g_flDebrisReflectance;
    float g_flDebrisOilyness;
    float g_flDebrisNormalStrength;
    float g_flDebrisEdgeSharpness;
    float g_flDebrisScale;
    float g_flDebrisWobble;
    float g_flFoamScale;
    float g_flFoamWobble;
    vec4 g_vFoamColor;
    float g_flWavesHeightOffset;
    float g_flWavesSharpness;
    float g_flFresnelExponent;
    float g_flWavesNormalStrength;
    float g_flWavesNormalJitter;
    vec2 g_vWaveScale;
    float g_flWaterInitialDirection;
    float g_flWavesSpeed;
    float g_flLowFreqWeight;
    float g_flMedFreqWeight;
    float g_flHighFreqWeight;
    float g_flWavesPhaseOffset;
    float g_flEdgeHardness;
    float g_flEdgeShapeEffect;
    uint g_nWaveIterations;
    vec3 g_vWaterFogColor;
    float g_flRefractionLimit;
    float g_flWaterFogStrength;
    vec3 g_vWaterDecayColor;
    float g_flWaterDecayStrength;
    float g_flWaterMaxDepth;
    float g_flWaterFogShadowStrength;
    float g_flUnderwaterDarkening;
    float g_flSpecularPower;
    float g_flSpecularNormalMultiple;
    float g_flSpecularBloomBoostStrength;
    float g_flSpecularBloomBoostThreshold;
    int g_bUseTriplanarCaustics;
    float g_flCausticUVScaleMultiple;
    float g_flCausticDistortion;
    float g_flCausticsStrength;
    float g_flCausticSharpness;
    float g_flCausticDepthFallOffDistance;
    float g_flCausticShadowCutOff;
    vec4 g_vCausticsTint;
    vec4 g_vViewportExtentsTs;
    float g_flReflectance;
    float g_flReflectionDistanceEffect;
    float g_flForceMixResolutionScale;
    float g_flEnvironmentMapBrightness;
    vec2 g_vRoughness;
    float g_flSSRStepSize;
    float g_flSSRSampleJitter;
    uint g_nSSRMaxForwardSteps;
    float g_flSSRBoostThreshold;
    float g_flSSRBoost;
    float g_flSSRBrightness;
    float g_flSSRMaxThickness;
    float g_flWaterEffectsRippleStrength;
    float g_flWaterEffectSiltStrength;
    float g_flWaterEffectFoamStrength;
    float g_flWaterEffectDisturbanceStrength;
    float g_flWaterEffectCausticStrength;
};

layout(set = 0) uniform _2643 _Globals_;

struct _2988
{
    ivec4 g_bOtherFxEnabled;
    ivec4 g_bOtherEnabled2;
    ivec4 g_bOtherEnabled3;
    ivec2 g_vBlueNoiseMask;
    anon_g_matPrimaryViewWorldToProjection g_matPrimaryViewWorldToProjection;
    vec4 g_vGradientFogBiasAndScale;
    vec4 m_vGradientFogExponents;
    vec4 g_vGradientFogColor_Opacity;
    vec4 g_vGradientFogCullingParams;
    vec4 g_vCubeFog_Offset_Scale_Bias_Exponent;
    vec4 g_vCubeFog_Height_Offset_Scale_Exponent_Log2Mip;
    anon_g_matPrimaryViewWorldToProjection g_matvCubeFogSkyWsToOs;
    vec4 g_vCubeFogCullingParams_MaxOpacity;
    vec4 g_vCubeFog_ExposureBias;
    vec4 g_vHighPrecisionLightingOffsetWs;
};

layout(set = 0) uniform _2988 PerViewConstantBufferCsgo_t;

struct _2824
{
    anon_g_matPrimaryViewWorldToProjection g_matWorldToProjection;
    anon_g_matPrimaryViewWorldToProjection g_matWorldToView;
    anon_g_matPrimaryViewWorldToProjection g_matViewToProjection;
    vec4 g_vInvProjRow3;
    vec3 g_vCameraPositionWs;
    float g_flViewportMinZ;
    vec3 g_vCameraDirWs;
    float g_flViewportMaxZ;
    vec3 g_vCameraUpDirWs;
    float g_flTime;
    //x = near plane, 
    vec3 g_vDepthPsToVsConversion;
    vec2 g_vInvViewportSize;
    vec2 g_vViewportToGBufferRatio;
    vec4 g_vInvGBufferSize;
    vec2 g_vViewportOffset;
    vec2 g_vViewportSize;
    vec4 g_vWorldToCameraOffset;
};

layout(set = 0) uniform _2824 PerViewConstantBuffer_t;

struct _2471
{
    _97 _m0;
    vec4 _m1;
    vec4 _m2;
    vec4 _m3;
    uvec4 _m4;
    uvec4 _m5;
    vec4 _m6;
    int _m7;
    float _m8;
    vec4 _m9;
    float _m10;
    float _m11;
    float _m12;
    float _m13;
    _2144 _m14;
    _308 _m15;
};

layout(set = 1) uniform _2471 undetermined;

//layout(set = 1, binding = 0, std140) uniform _2692_5538
//{
//    layout(offset = 272) vec4 _m0;
//    layout(offset = 288) vec4 _m1;
//    layout(offset = 304) vec4 _m2;
//    layout(offset = 320) vec4 _m3;
//    layout(offset = 336) uvec4 _m4;
//    layout(offset = 384) uvec4 _m5;
//    layout(offset = 400) vec4 _m6;
//    layout(offset = 31152) int _m7;
//    layout(offset = 31160) float _m8;
//    layout(offset = 31168) vec4 _m9;
//    layout(offset = 31184) float _m10;
//    layout(offset = 31188) float _m11;
//    layout(offset = 31192) float _m12;
//    layout(offset = 31196) float _m13;
//    layout(offset = 31200) _3018 _m14;
//    layout(offset = 31712) _392 _m15;
//} _5538;

layout(set = 1, binding = 30, std430) readonly buffer g_CullBits
{
    uint _m0[];
} g_CullBits_1;

layout(set = 1, binding = 31, std430) readonly buffer g_BarnLights
{
    _2096 _m0[];
} g_BarnLights_1;

layout(set = 0, binding = 117) uniform texture2D g_tZerothMoment;
layout(set = 0, binding = 90) uniform texture2D g_tBlueNoise;
layout(set = 0, binding = 116) uniform texture2D g_tSceneDepth;
layout(set = 0, binding = 47) uniform sampler AllowGlobalMipBiasOverride_0_AddressU_2_AddressV_2_Filter_0_AddressW_2;
layout(set = 0, binding = 115) uniform texture2D g_tRefractionMap;
layout(set = 0, binding = 46) uniform sampler Filter_21_AllowGlobalMipBiasOverride_0_AddressU_2_AddressV_2;
layout(set = 0, binding = 119) uniform texture2D g_tWaterEffectsMap;
layout(set = 0, binding = 113) uniform texture2D g_tFoam;
layout(set = 0, binding = 48) uniform sampler AllowGlobalMipBiasOverride_0_Filter_255_MaxAniso_1_AddressU_dynamic_AddressV_dynamic;
layout(set = 0, binding = 111) uniform texture2D g_tDebris;
layout(set = 0, binding = 56) uniform sampler DefaultSamplerState_0;
layout(set = 0, binding = 112) uniform texture2D g_tDebrisNormal;
layout(set = 0, binding = 51) uniform sampler Filter_20_AddressU_3_AddressV_3_AddressW_3_BorderColor_0;
layout(set = 0, binding = 52) uniform samplerShadow AddressU_2_AddressV_2_Filter_149_ComparisonFunc_3;
layout(set = 0, binding = 96) uniform texture2D g_tShadowDepthBufferDepth;
layout(set = 0, binding = 107) uniform texture2D g_tParticleShadowBuffer;
layout(set = 0, binding = 94) uniform texture3D g_tLightCookieTexture;
layout(set = 0, binding = 45) uniform sampler Filter_21_AddressU_0_AddressV_0_AllowGlobalMipBiasOverride_0;
layout(set = 0, binding = 110) uniform textureCube g_tLowEndCubeMap;
layout(set = 0, binding = 55) uniform sampler DefaultSamplerState_0_1;
layout(set = 0, binding = 102) uniform textureCube g_tFogCubeTexture;
layout(set = 0, binding = 118) uniform texture2D g_tMoitFinal;
layout(set = 0, binding = 114) uniform texture2D g_tWavesNormalHeight;
layout(set = 0, binding = 57) uniform sampler DefaultSamplerState_0_2;

layout(location = 1) in float g_flTime; //g_flTime
layout(location = 2) in vec4 BLENDVALUES; // BLENDVALUES
layout(location = 3) in vec3 offsetWorldPos; // offset world position with a negative offset of highprecision lightingoffset
layout(location = 4) in vec3 vNormal; // Transformed Blend Indices
layout(location = 0) out vec4 outputColor; //color output

void main()
{
    vec4 FragCoord = gl_FragCoord;
    vec4 FragCoordWInverse = FragCoord;
    FragCoordWInverse.w = 1.0 / FragCoord.w;
    bool DontFlipBackfaceNormals;
    if (_Globals_.g_bRenderBackfaceNormals != 0)
    {
        DontFlipBackfaceNormals = !(_Globals_.g_bDontFlipBackfaceNormals != 0);
    }
    else
    {
        DontFlipBackfaceNormals = false;
    }

    vec3 usedvNormal;
    //This is wrong because vNormal is actually the normal!
    if (DontFlipBackfaceNormals)
    {
        usedvNormal = vNormal.xyz * (gl_FrontFacing ? 1.0 : (-1.0));
    }
    else
    {
        usedvNormal = vNormal.xyz;
    }

    vec3 trueWorldPos = offsetWorldPos + PerViewConstantBufferCsgo_t.g_vHighPrecisionLightingOffsetWs.xyz;

    // --- Early Discard (OIT Occlusion) ---

    ivec2 scaledFragCoord = ivec3(ivec2(FragCoord.xy * _Globals_.g_flForceMixResolutionScale), 0).xy;
    float e_to_the_zerothMoment = exp(-texelFetch(g_tZerothMoment, scaledFragCoord, 0).x);
    float one_minus_e_to_the_zeroth = 1.0 - e_to_the_zerothMoment;

    if (one_minus_e_to_the_zeroth > 0.9998999834060669)
    {
        discard;
    }

    // --- Skybox Scale Effect & Blue Noise ---

    bvec4 otherEnabledVec = notEqual(PerViewConstantBufferCsgo_t.g_bOtherEnabled3, ivec4(0));
    bool isSkybox = otherEnabledVec.x;
    float SkyboxScale;
    if (isSkybox)
    {
        SkyboxScale = _Globals_.g_flSkyBoxScale;
    }
    else
    {
        SkyboxScale = 1.0;
    }
    vec4 NoiseValue = texelFetch(g_tBlueNoise, ivec3(ivec2(FragCoord.xy) & PerViewConstantBufferCsgo_t.g_vBlueNoiseMask, 0).xy, 0);
    //gbufferUV for us
    vec2 gbufferUV = FragCoord.xy * PerViewConstantBuffer_t.g_vInvGBufferSize.xy;

    vec3 pixelRelativePos = trueWorldPos - PerViewConstantBuffer_t.g_vCameraPositionWs;
    vec3 pixelDir = normalize(pixelRelativePos);
    vec3 invPixelDir = -pixelDir;
    float scaledPixelRelativePos = length(pixelRelativePos) * SkyboxScale;
    vec2 horizontalInvPixelDir = invPixelDir.xy;
    float InvPixelDirZ = invPixelDir.z;

    vec3 viewDepOffsetFactor = mix(vec3(horizontalInvPixelDir / vec2(InvPixelDirZ), sqrt(InvPixelDirZ)), vec3(0.0), bvec3(isSkybox));

    float refractionDistortionFactor;
    float waterColumnOpticalDepthFactor;
    vec4 refractionColorSample;
    float localSceneDepth;
    vec3 refractionSamplePos;

    //FOR MANUAL REFORMAT)
    if (!isSkybox)
    {
        vec2 SceneDepthUV = gbufferUV.xy;
        float SceneDepth = clamp((textureLod(g_tSceneDepth, SceneDepthUV, 0.0).x - PerViewConstantBuffer_t.g_flViewportMinZ) / (PerViewConstantBuffer_t.g_flViewportMaxZ - PerViewConstantBuffer_t.g_flViewportMinZ), 0.0, 1.0);
        vec4 RefractionValue = texture(g_tRefractionMap, SceneDepthUV);
        float refractionLuminance = clamp(dot(RefractionValue.xyz, vec3(0.2125, 0.7154, 0.0721)), 0.0, 0.4);
        vec3 localPixelDir = pixelDir.xyz;
        refractionDistortionFactor = refractionLuminance * (-0.03);
        localSceneDepth = SceneDepth;
        refractionSamplePos = (g_vCameraPositionWs.xyz + (localPixelDir * (1.0 / (fma(SceneDepth, g_vInvProjRow3.z, g_vInvProjRow3.w) * dot(g_vCameraDirWs.xyz, localPixelDir))))).xyz;
        refractionColorSample = RefractionValue;
        waterColumnOpticalDepthFactor =
        fma(refractionLuminance,
        -0.03,
        max((-(PerViewConstantBuffer_t.g_vDepthPsToVsConversion.x / fma(PerViewConstantBuffer_t.g_vDepthPsToVsConversion.y, SceneDepth, PerViewConstantBuffer_t.g_vDepthPsToVsConversion.z))) - (-(vec4(trueWorldPos.xyz, 1.0).xyzw * mat4(vec4(PerViewConstantBuffer_t.g_matWorldToView._m0[0].x, PerViewConstantBuffer_t.g_matWorldToView._m0[1].x, PerViewConstantBuffer_t.g_matWorldToView._m0[2].x, PerViewConstantBuffer_t.g_matWorldToView._m0[3].x), vec4(PerViewConstantBuffer_t.g_matWorldToView._m0[0].y, PerViewConstantBuffer_t.g_matWorldToView._m0[1].y, PerViewConstantBuffer_t.g_matWorldToView._m0[2].y, PerViewConstantBuffer_t.g_matWorldToView._m0[3].y), vec4(PerViewConstantBuffer_t.g_matWorldToView._m0[0].z, PerViewConstantBuffer_t.g_matWorldToView._m0[1].z, PerViewConstantBuffer_t.g_matWorldToView._m0[2].z, PerViewConstantBuffer_t.g_matWorldToView._m0[3].z), vec4(PerViewConstantBuffer_t.g_matWorldToView._m0[0].w, PerViewConstantBuffer_t.g_matWorldToView._m0[1].w, PerViewConstantBuffer_t.g_matWorldToView._m0[2].w, PerViewConstantBuffer_t.g_matWorldToView._m0[3].w))).z), 0.0) * 0.01);
    }


    if (!isSkybox)
    {
        vec2 SceneDepthUV = gbufferUV.xy;
        float SceneDepth = clamp((textureLod(g_tSceneDepth, SceneDepthUV, 0.0).x - PerViewConstantBuffer_t.g_flViewportMinZ) / (PerViewConstantBuffer_t.g_flViewportMaxZ - PerViewConstantBuffer_t.g_flViewportMinZ), 0.0, 1.0);
        vec4 RefractionValue = texture(g_tRefractionMap, SceneDepthUV);
        float refractionLuminance = clamp(dot(RefractionValue.xyz, vec3(0.2125, 0.7154, 0.0721)), 0.0, 0.4);
        vec3 localPixelDir = pixelDir.xyz;
        refractionDistortionFactor = refractionLuminance * (-0.03);
        localSceneDepth = SceneDepth;
        refractionSamplePos = (g_vCameraPositionWs.xyz + (localPixelDir * (1.0 / (fma(SceneDepth, g_vInvProjRow3.z, g_vInvProjRow3.w) * dot(g_vCameraDirWs.xyz, localPixelDir))))).xyz;
        refractionColorSample = RefractionValue;
        waterColumnOpticalDepthFactor =
        fma(refractionLuminance,
        -0.03,
        max((-(PerViewConstantBuffer_t.g_vDepthPsToVsConversion.x / fma(PerViewConstantBuffer_t.g_vDepthPsToVsConversion.y, SceneDepth, PerViewConstantBuffer_t.g_vDepthPsToVsConversion.z))) - (-(vec4(trueWorldPos.xyz, 1.0).xyzw * mat4(vec4(PerViewConstantBuffer_t.g_matWorldToView._m0[0].x, PerViewConstantBuffer_t.g_matWorldToView._m0[1].x, PerViewConstantBuffer_t.g_matWorldToView._m0[2].x, PerViewConstantBuffer_t.g_matWorldToView._m0[3].x), vec4(PerViewConstantBuffer_t.g_matWorldToView._m0[0].y, PerViewConstantBuffer_t.g_matWorldToView._m0[1].y, PerViewConstantBuffer_t.g_matWorldToView._m0[2].y, PerViewConstantBuffer_t.g_matWorldToView._m0[3].y), vec4(PerViewConstantBuffer_t.g_matWorldToView._m0[0].z, PerViewConstantBuffer_t.g_matWorldToView._m0[1].z, PerViewConstantBuffer_t.g_matWorldToView._m0[2].z, PerViewConstantBuffer_t.g_matWorldToView._m0[3].z), vec4(PerViewConstantBuffer_t.g_matWorldToView._m0[0].w, PerViewConstantBuffer_t.g_matWorldToView._m0[1].w, PerViewConstantBuffer_t.g_matWorldToView._m0[2].w, PerViewConstantBuffer_t.g_matWorldToView._m0[3].w))).z), 0.0) * 0.01);
    }
    else
    {
        refractionDistortionFactor = 0.0;
        localSceneDepth = 1.0;
        refractionSamplePos = vec3(0.0);
        refractionColorSample = vec4(0.0);
        waterColumnOpticalDepthFactor = 1.0;
    }
    float adjustedWaterColumnDept = max(0.0, waterColumnOpticalDepthFactor - 0.02);
    float refractedVerticalFactor = waterColumnOpticalDepthFactor * InvPixelDirZ;

    vec2 unbiasedUV = (trueWorldPos.xy - _Globals_.g_vMapUVMin) / (_Globals_.g_vMapUVMax - _Globals_.g_vMapUVMin);
    unbiasedUV.y = 1.0 - unbiasedUV.y;
    float currentWaterRoughness;
    if (isSkybox)
    {
        currentWaterRoughness = _Globals_.g_flWaterRoughnessMax;
    }
    else
    {
        currentWaterRoughness = max(0.0, mix(_Globals_.g_flWaterRoughnessMin, _Globals_.g_flWaterRoughnessMax, BLENDVALUES.x));
    }
    float currentDebrisVisibility = isSkybox ? 0.0 : max(0.0, mix(_Globals_.g_flDebrisMin, _Globals_.g_flDebrisMax, BLENDVALUES.z));
    float currentFoamAmount = isSkybox ? 0.0 : max(0.0, mix(_Globals_.g_flFoamMin, _Globals_.g_flFoamMax, BLENDVALUES.y));


    vec2 baseWaveUV = ((trueWorldPos * SkyboxScale) + (viewDepOffsetFactor * (0.5 - _Globals_.g_flWaterPlaneOffset))).xy / 30.f;
    vec2 baseWaveUVDx = dFdx(baseWaveUV);
    vec2 baseWaveUVDy = dFdy(baseWaveUV);
    float reflectionsLodFactor = (0.5 * pow(max(dot(baseWaveUVDx, baseWaveUVDx), dot(baseWaveUVDy, baseWaveUVDy)), 0.1)) * _Globals_.g_flReflectionDistanceEffect;

    //vec2 watersample uv bla bla, for more readability
    vec4 waterEffectsSampleRaw = texture(sampler2D(g_tWaterEffectsMap, Filter_21_AllowGlobalMipBiasOverride_0_AddressU_2_AddressV_2), (((FragCoord.xy - PerViewConstantBuffer_t.g_vViewportOffset.xy).xy * PerViewConstantBuffer_t.g_vInvViewportSize.xy).xy * PerViewConstantBuffer_t.g_vViewportToGBufferRatio.xy).xy);
    vec2 waterEffectsDisturbanceXY = clamp((waterEffectsSampleRaw - vec4(0.5)).yz * 2.0, vec2(0.0), vec2(1.0));
    float waterEffectsFoam = waterEffectsDisturbanceXY.y;


    float _24505 = waterEffectsFoam;


    float totalDisturbanceStrength = (waterEffectsDisturbanceXY.x + waterEffectsFoam) * _Globals_.g_flWaterEffectDisturbanceStrength;
    float quarterDisturbanceStrength = totalDisturbanceStrength * 0.25;
    float clampedRefractLodFactor = clamp(reflectionsLodFactor, 0.0, 0.5);

    vec3 refractShiftedPos = refractionSamplePos + (viewDepOffsetFactor * clamp(dot(refractionColorSample.xyz, vec3(0.2125, 0.7154, 0.0721)), 0.0, 0.4));
    vec3 refractShiftedPosDdx = dFdx(refractShiftedPos);
    vec3 refractShiftedPosDdy = dFdy(refractShiftedPos);
    vec3 refractShiftNormal = -normalize(cross(refractShiftedPosDdx, refractShiftedPosDdy));

    //moved to NoiseValue assign for me, to keep them sensibly together
    vec2 blueNoiseOffset = NoiseValue.xy - vec2(0.5);

    float timeAnim = fma(g_flTime, 3.0, sin(g_flTime * 0.5) * 0.1);

    vec2 depthFactorFine = vec2(clamp(adjustedWaterColumnDept * 10.0, 0.0, 1.0));
    vec2 viewShiftedUV = ((-horizontalInvPixelDir) / vec2(InvPixelDirZ + 0.25)).xy;
    vec2 depthFactorCoarse = vec2(clamp(adjustedWaterColumnDept * 4.0, 0.0, 1.0));
    float sceneDepthChangeMagnitude = fwidth(localSceneDepth);

    float blueNoiseDitherFactor = (NoiseValue.x - 0.5) * 2.0;
    vec2 ditheredRefractShiftNormalXY = refractShiftNormal.xy + (blueNoiseOffset * 0.05);

    

    vec2 accumulatedWaveUVOffset = vec2(0.0);
    vec2 currentWaveOctaveScale = _Globals_.g_vWaveScale;
    vec3 scaledNormalSum = vec3(0.0, 0.0, 1.0);
    float waveHeightSum;
    vec2 phaseOffsetSum = vec2(0.0);
    waveHeightSum = 0.0;
    uint iter = 0u;
    float flWaterDirection = g_flWaterInitialDirection;
    for (;;)
    {
        if (!(iter < g_nWaveIterations))
        {
            break;
        }
        float iterProgress = float(iter) / (float(_19857) - 1.0);

        float lowMedBlend = clamp(iterProgress * 2.0, 0.0, 1.0);
        float medHighBlend = clamp(iterProgress * 2.0 - 1.0, 0.0, 1.0);

        float lowFreqWeight = fma(totalDisturbanceStrength, 0.05, _Globals_.g_flLowFreqWeight);
        float medFreqWeight = fma(totalDisturbanceStrength, 0.25, _Globals_.g_flMedFreqWeight);

        float lowMedWeighedAmplitude = mix(
        lowFreqWeight,
        medFreqWeight,
        lowMedBlend);

        float freqWeight = mix(
        lowMedWeighedAmplitude
        _Globals_.g_flHighFreqWeight * currentWaterRoughness + quarterDisturbanceStrength),
        medHighBlend);

        vec2 waveAnimOffset = vec2(sin(flWaterDirection), cos(flWaterDirection)) * ((g_flTime * _Globals_.g_flWavesSpeed) * 0.5);
        vec2 anisoUv = waveAnimOffset *  sqrt(vec2(1.0) / currentWaveOctaveScale)

        vec2 waveOctaveUV = anisoUv + (baseWaveUV.xy + accumulatedWaveUVOffset * 3.0 + phaseOffsetSum) / currentWaveOctaveScale

        vec3 waveSample = textureLod(g_tWavesNormalHeight, waveOctaveUV, (-1.0) * clampedRefractLodFactor).rgb - vec3(0.5);
        float waveSampleHeight = (waveSample.z * freqWeight) * length(currentWaveOctaveScale);
        vec2 waveNormalXY = waveSample.xy * 2.0;

        vec2 scaledWaveNormalXY = vec2(waveNormalXY.x * min(1.0, currentWaveOctaveScale.y / currentWaveOctaveScale.x), waveNormalXY.y * min(1.0, currentWaveOctaveScale.x / currentWaveOctaveScale.y)).xy * (freqWeight * 0.1);

        accumulatedWaveUVOffset.xy += ((((-viewShiftedUV) * (waveSampleHeight * 0.01)) * _Globals_.g_flWavesHeightOffset) * currentWaterRoughness);

        phaseOffsetSum.xy += (((scaledWaveNormalXY.xy * _Globals_.g_flWavesSharpness) * currentWaveOctaveScale) * _Globals_.g_flWavesPhaseOffset);
        waveHeightSum += waveSampleHeight * 0.01;
        scaledNormalSum.xy += scaledWaveNormalXY;

        currentWaveOctaveScale *= g_flWavesPhaseOffset;

        iter += 1u;
        flWaterDirection += (3.5 / float(iter));
        continue;
    }
    vec2 finalWavePhaseOffset = phaseOffsetSum * 0.1;
    vec3 roughedWaveNormal = scaledNormalSum * currentWaterRoughness;
    float scaledAccumulatedWaveHeight = (waveHeightSum * currentWaterRoughness) * 60.0;
    
    

    //-------EDGE FACTOR---------
    vec3 ditheredNormal;
    float edgeFactorQ;
    if (!isSkybox)
    {
        vec3 _23714 = refractShiftNormal;
        _23714.x = ditheredRefractShiftNormalXY.x;
        _23714.y = ditheredRefractShiftNormalXY.y;
        ditheredNormal = _23714;
        edgeFactorQ = _Globals_.g_flEdgeShapeEffect * clamp(fma(-refractShiftNormal.z, 1.0 - clamp(refractedVerticalFactor * 8.0, 0.0, 1.0), 1.20), 0.0, 1.0);
    }
    else
    {
        ditheredNormal = vec3(0.0, 0.0, 1.0);
        edgeFactorQ = _Globals_.g_flEdgeShapeEffect;
    }




    float finalFoamHeightContrib = scaledAccumulatedWaveHeight;
    float foamSiltFactor = 0.0;
    vec2 foamEffectDisplacementUV = vec2(0.0);
    vec2 debrisEffectsNormalXY = vec2(0.0);
    float foamFromEffects = 0.0;
    float disturbanceWeightedFoamAmount = quarterDisturbanceStrength;
    float finalFoam = waterEffectsFoam;
    vec2 foamSiltEffectNormalXY = vec2(0.0);
    vec3 effectsSamplePos = trueWorldPos.xyz + (viewDepOffsetFactor * (mix(0.5, scaledAccumulatedWaveHeight, edgeFactorQ) - _Globals_.g_flWaterPlaneOffset));


    if (!isSkybox)
    {
        vec3 localShiftedPos = (trueWorldPos + (viewDepOffsetFactor * (mix(0.0, scaledAccumulatedWaveHeight, edgeFactorQ) - _Globals_.g_flWaterPlaneOffset))) + (vec3(roughedWaveNormal.xy, 0.0) * (-16.0));
        //not used in final shader, instead -cameraPosition is used
        vec4 negCamPos = PerViewConstantBuffer_t.g_vWorldToCameraOffset * 1.0;
        mat4 transposedWorldToProj = transpose(PerViewConstantBuffer_t.g_matWorldToProjection);
        vec4 localTransformedPos = (vec4(localShiftedPos.xyz, 1.0) + negCamPos).xyzw * transposedWorldToProj;
        vec2 ncdCoords = localTransformedPos.xy / vec2(localTransformedPos.w);
        vec4 effectsSample = texture(g_tWaterEffectsMap, (((vec2(ncdCoords.x, -ncdCoords.y) * 0.5) + vec2(0.5)).xy * PerViewConstantBuffer_t.g_vViewportToGBufferRatio.xy).xy) - vec4(0.5);

        vec3 effectsPos1 = localShiftedPos + (viewDepOffsetFactor * fma(20.0, effectsSample.x, 2.0 * clamp(effectsSample.yz * 2.0, vec2(0.0), vec2(1.0)).x));
        vec4 localMoreShiftedTransformedPos = (vec4(effectsPos1.xyz, 1.0) + negCamPos).xyzw * transposedWorldToProj;
        vec2 localMoreShiftedNcdCoords = localMoreShiftedTransformedPos.xy / vec2(localMoreShiftedTransformedPos.w);
        vec2 effectsPos1UV = ((vec2(localMoreShiftedNcdCoords.x, -localMoreShiftedNcdCoords.y) * 0.5) + vec2(0.5)).xy * PerViewConstantBuffer_t.g_vViewportToGBufferRatio.xy;
        vec4 anotherEffectsSample = texture(sampler2D(g_tWaterEffectsMap, Filter_21_AllowGlobalMipBiasOverride_0_AddressU_2_AddressV_2), effectsPos1UV.xy) - vec4(0.5);
        vec2 clampedOtherEffectsSample = clamp(anotherEffectsSample.yz * 2.0, vec2(0.0), vec2(1.0));

        
        vec2 negEffectsPos1UV = -effectsPos1UV;

        vec4 effectsPos1Xshift = (vec4((effectsPos1 + vec3(1.0, 0.0, 0.0)).xyz, 1.0) + negCamPos).xyzw * transposedWorldToProj;
        vec4 effectsPos1Yshift = (vec4((effectsPos1 + vec3(0.0, -1.0, 0.0)).xyz, 1.0) + negCamPos).xyzw * transposedWorldToProj;

        vec2 effectsPos1XshiftNcd = effectsPos1Xshift.xy / vec2(effectsPos1Xshift.w);
        vec2 effectsPos1YshiftNcd = effectsPos1Yshift.xy / vec2(effectsPos1Yshift.w);

        
        vec2 duv_dx_approx = fma((vec2(effectsPos1XshiftNcd.x, -effectsPos1XshiftNcd.y) * 0.5) + vec2(0.5), PerViewConstantBuffer_t.g_vViewportToGBufferRatio.xy, negEffectsPos1UV);
        vec2 duv_dy_approx = fma((vec2(effectsPos1YshiftNcd.x, -effectsPos1YshiftNcd.y) * 0.5) + vec2(0.5), PerViewConstantBuffer_t.g_vViewportToGBufferRatio.xy, negEffectsPos1UV);
        vec2 stepScale = vec2(0.0004) / vec2(length(duv_dx_approx), length(duv_dy_approx));

        


        vec4 xOffsetEffectsSample = texture(g_tWaterEffectsMap, fma(effectsPos1UV, PerViewConstantBuffer_t.g_vViewportToGBufferRatio.xy, normalize(duv_dx_approx) * 0.005).xy) - vec4(0.5);
        vec4 yOffsetEffectsSample = texture(g_tWaterEffectsMap, fma(effectsPos1UV, PerViewConstantBuffer_t.g_vViewportToGBufferRatio.xy, normalize(duv_dy_approx) * 0.005).xy) - vec4(0.5);

        vec2 rippleFoamDX = clamp(xOffsetEffectsSample.yz * 2.0, vec2(0.0), vec2(1.0));
        vec2 rippleFoamDY = clamp(yOffsetEffectsSample.yz * 2.0, vec2(0.0), vec2(1.0));

        foamEffectDisplacementUV = ((normalize(cross(vec3(stepScale.x, 0.0, anotherEffectsSample.x - xOffsetEffectsSample.x), vec3(0.0, stepScale.y, anotherEffectsSample.x - yOffsetEffectsSample.x))).xy * vec2(-1.0, 1.0)) * (abs(anotherEffectsSample.x) * 4.0)) * _Globals_.g_flWaterEffectsRippleStrength;

        finalFoamHeightContrib = fma(anotherEffectsSample.x * _Globals_.g_flWaterEffectsRippleStrength, 12.0, scaledAccumulatedWaveHeight);
        foamSiltFactor = clampedOtherEffectsSample.y * _Globals_.g_flWaterEffectSiltStrength;

        debrisEffectsNormalXY = normalize(cross(vec3(stepScale.x, 0.0, clampedOtherEffectsSample.x - rippleFoamDX.x), vec3(0.0, stepScale.y, clampedOtherEffectsSample.x - rippleFoamDY.x))).xy * vec2(-1.0, 1.0);
        foamFromEffects = clampedOtherEffectsSample.x * _Globals_.g_flWaterEffectFoamStrength;
        finalFoam = clampedOtherEffectsSample.y;
        


        disturbanceWeightedFoamAmount = ((clampedOtherEffectsSample.x + clampedOtherEffectsSample.y) * _Globals_.g_flWaterEffectDisturbanceStrength) * 0.25;
        
        foamSiltEffectNormalXY = (normalize(cross(vec3(stepScale.x, 0.0, clampedOtherEffectsSample.y - rippleFoamDX.y), vec3(0.0, stepScale.y, clampedOtherEffectsSample.y - rippleFoamDY.y))).xy * vec2(-1.0, 1.0)) * pow(clampedOtherEffectsSample.y, 3.5);

        effectsSamplePos = (trueWorldPos + (viewDepOffsetFactor * (mix(0.5, finalFoamHeightContrib, edgeFactorQ) - _Globals_.g_flWaterPlaneOffset))) + (vec3(foamEffectDisplacementUV.xy, 0.0) * (-4.0));
    }
    vec3 rippleDisplacementAsVec3 = vec3(foamEffectDisplacementUV.xy, 0.0);

    

    vec3 worldPosForFoamAndDebrisBase = (trueWorldPos + (viewDepOffsetFactor * (mix(0.5, finalFoamHeightContrib, edgeFactorQ * 0.5) - _Globals_.g_flWaterPlaneOffset))) + (rippleDisplacementAsVec3 * (-2.0));

    vec2 foamWobbleAnim = vec2(sin(fma(effectsSamplePos.y, 0.07, timeAnim)), cos(fma(effectsSamplePos.x, 0.07, timeAnim)));

    
    vec2 foamBaseUV = worldPosForFoamAndDebrisBase.xy; / _Globals_.g_flFoamScale;
    vec2 foamWobbleEffect = (foamBaseUV + (finalWavePhaseOffset * _Globals_.g_flFoamWobble * 0.5) * (1.0 - currentFoamAmount)) - (foamSiltEffectNormalXY / _Globals_.g_flFoamScale);

    float foamNoiseStrength = 0.05 + finalFoam;
    vec4 foamSample1 = texture(g_tFoam, mix(foamBaseUV, foamWobbleEffect + ((foamWobbleAnim * foamNoiseStrength) * 0.03), depthFactorFine).xy);
    vec4 foamSample2 = texture(g_tFoam, mix(foamBaseUV.yx * 0.731, (foamWobbleEffect.yx * 0.731) + ((vec2(sin(fma(effectsSamplePos.y, 0.06, timeAnim)), cos(fma(effectsSamplePos.x, 0.06, timeAnim))) * foamNoiseStrength) * 0.02), depthFactorFine).xy);
    float combinedFoamTextureValue = fma(sin(NoiseValue.x), 0.125, max(foamSample1.z, foamSample2.z));
    float finalFoamIntensity = clamp(fma(currentFoamAmount * fma(finalFoamHeightContrib, 0.008, 1.0), 1.0 - clamp(disturbanceWeightedFoamAmount * 2.0, 0.0, 1.0), foamFromEffects), 0.0, 1.0);
    float finalFoamPow1_5 = pow(finalFoam, 1.5);


    vec2 debrisBaseUV = worldPosForFoamAndDebrisBase.xy / _Globals_.g_flDebrisScale;
    vec2 debrisWobbleOffset = finalWavePhaseOffset * _Globals_.g_flDebrisWobble;

    float absFoamSiltX = abs(foamSiltEffectNormalXY.x);
    float absFoamSiltY = abs(foamSiltEffectNormalXY.y);
    float _15937 = foamSiltEffectNormalXY.y * float(absFoamSiltY > absFoamSiltX);
    vec2 dominantFoamSiltNorm = (vec2(foamSiltEffectNormalXY.x * float(absFoamSiltX > abs(_15937)), _15937) / _Globals_.g_flDebrisScale) * 400.0;


    


    vec2 debrisFinalUV = mix(debrisBaseUV, (((debrisBaseUV + (debrisWobbleOffset * (1.0 - currentDebrisVisibility))) + ((viewShiftedUV * (fma(sin(finalFoam * 50.0) * 4.0, clamp(0.1 - finalFoamPow1_5, 0.0, 1.0), 1.0) * finalFoamPow1_5)) * 0.1)) + ((foamWobbleAnim * (0.1 + finalFoam)) * 0.02)) - dominantFoamSiltNorm, depthFactorCoarse).xy;



    vec4 debrisColorHeightSample = texture(g_tDebris, debrisFinalUV, finalFoamPow1_5 * 3.0);
    float debrisHeightVal = debrisColorHeightSample.w - 0.5;




    float finalDebrisVisibility = fma(-currentDebrisVisibility, clamp(1.4 - (finalFoam / mix(1.0, 0.4, debrisColorHeightSample.w)), 0.0, 1.0), 1.0);
    float debrisEdgeFactor = clamp((debrisColorHeightSample.w - finalDebrisVisibility) * _Globals_.g_flDebrisEdgeSharpness, 0.0, 1.0);

    float noClue = max(0.0, fma(2.0, finalFoamPow1_5, debrisHeightVal * (-2.0)));

    float debrisVisibilityMask = clamp(fma(-noClue, 10.0, 1.0), 0.0, 1.0);
    float finalDebrisFactor = debrisVisibilityMask * debrisEdgeFactor;

    vec3 debrisNormalSample = texture(g_tDebrisNormal, debrisFinalUV).xyz - vec3(0.5);
    debrisNormalSample.y = -debrisNormalSample.y;
    vec2 debrisNormalXY = debrisNormalSample.xy * _Globals_.g_flDebrisNormalStrength;

    



    

    float combinedfinalFoamIntensity = clamp(fma(-debrisVisibilityMask, debrisEdgeFactor, fma(finalFoamIntensity * combinedFoamTextureValue, 0.25, clamp(finalFoamIntensity - (1.0 - combinedFoamTextureValue), 0.0, 1.0) * 0.75)), 0.0, 1.0);
    float finalDebFoamHeightContrib = mix(finalFoamHeightContrib, fma(finalFoamHeightContrib, 0.5, debrisHeightVal * 2.0), finalDebrisFactor);


    vec3 finalSurfacePos = effectsSamplePos;
    float finalWaterColumnDepthForRefract = waterColumnOpticalDepthFactor;
    if (!isSkybox)
    {
        finalSurfacePos = (trueWorldPos + (viewDepOffsetFactor * (mix(0.5, finalDebFoamHeightContrib, edgeFactorQ) - _Globals_.g_flWaterPlaneOffset))) + (rippleDisplacementAsVec3 * (-12.0));
        finalWaterColumnDepthForRefract = fma(max((-(PerViewConstantBuffer_t.g_vDepthPsToVsConversion.x / fma(PerViewConstantBuffer_t.g_vDepthPsToVsConversion.y, localSceneDepth, PerViewConstantBuffer_t.g_vDepthPsToVsConversion.z))) - (-(vec4(finalSurfacePos.xyz, 1.0).xyzw * mat4(vec4(PerViewConstantBuffer_t.g_matWorldToView._m0[0].x, PerViewConstantBuffer_t.g_matWorldToView._m0[1].x, PerViewConstantBuffer_t.g_matWorldToView._m0[2].x, PerViewConstantBuffer_t.g_matWorldToView._m0[3].x), vec4(PerViewConstantBuffer_t.g_matWorldToView._m0[0].y, PerViewConstantBuffer_t.g_matWorldToView._m0[1].y, PerViewConstantBuffer_t.g_matWorldToView._m0[2].y, PerViewConstantBuffer_t.g_matWorldToView._m0[3].y), vec4(PerViewConstantBuffer_t.g_matWorldToView._m0[0].z, PerViewConstantBuffer_t.g_matWorldToView._m0[1].z, PerViewConstantBuffer_t.g_matWorldToView._m0[2].z, PerViewConstantBuffer_t.g_matWorldToView._m0[3].z), vec4(PerViewConstantBuffer_t.g_matWorldToView._m0[0].w, PerViewConstantBuffer_t.g_matWorldToView._m0[1].w, PerViewConstantBuffer_t.g_matWorldToView._m0[2].w, PerViewConstantBuffer_t.g_matWorldToView._m0[3].w))).z), 0.0), 0.01, refractionDistortionFactor);
    }

    float surfaceCoverageAlpha = clamp(debrisEdgeFactor + combinedfinalFoamIntensity, 0.0, 1.0);

    


    vec2 finalWaveNormalXY = (((roughedWaveNormal.xy * 2.0) * _Globals_.g_flWavesNormalStrength) * mix(1.0, 2.0, reflectionsLodFactor)) * 1.0;

    finalWaveNormalXY *= fma(clamp(0.2 - finalWaterColumnDepthForRefract, 0.0, 1.0), 8.0, 1.0);

    finalWaveNormalXY += ((debrisNormalXY * finalDebrisFactor) * 1.5);

    finalWaveNormalXY += (mix(foamSample1.xy - vec2(0.5), foamSample2.xy - vec2(0.5), vec2(float(foamSample2.z > foamSample1.z))).xy * combinedfinalFoamIntensity);


    finalWaveNormalXY += ((debrisEffectsNormalXY.xy * combinedfinalFoamIntensity) * 0.5);

    finalWaveNormalXY += ((foamEffectDisplacementUV.xy * (1.0 - clamp(fma(debrisVisibilityMask, debrisEdgeFactor, combinedfinalFoamIntensity), 0.0, 1.0))) * 2.0);

    finalWaveNormalXY *= (vec2(1.0) + ((blueNoiseOffset * 2.0) * _Globals_.g_flWavesNormalJitter));

    vec3 surfaceNormal = vec3(finalWaveNormalXY, sqrt(1.0 - clamp(dot(finalWaveNormalXY, finalWaveNormalXY), 0.0, 1.0)));

    vec2 perturbedNormalXY *= 3;

    vec3 perturbedSurfaceNormal = vec3(perturbedNormalXY, sqrt(1.0 - clamp(dot(perturbedNormalXY, perturbedNormalXY), 0.0, 1.0)));

    vec3 finalSurfaceNormal = surfaceNormal;
    vec3 finalPerturbedSurfaceNormal = perturbedSurfaceNormal;
    if (!isSkybox)
    {
        float _20589 = mix(60.0, 120.0, ditheredNormal.z);
        vec3 _15760 = vec3((clamp(fma(-sceneDepthChangeMagnitude, 1000.0, clamp(((1.0 / _20589) - finalWaterColumnDepthForRefract) * _20589, 0.0, 1.0) + clamp((0.025 - finalWaterColumnDepthForRefract) * 8.0, 0.0, 1.0)), 0.0, 1.0) / fma(scaledPixelRelativePos, 0.002, 1.0)) * 0.6);
        finalSurfaceNormal = normalize(mix(surfaceNormal, ditheredNormal, _15760));
        finalPerturbedSurfaceNormal = normalize(mix(perturbedSurfaceNormal, ditheredNormal, _15760));
    }



    float cosNormAng = clamp(dot(invPixelDir, finalPerturbedSurfaceNormal.xyz), 0.0, 1.0);
    float fresnel = pow(1.0 - cosNormAng, _Globals_.g_flFresnelExponent);
    vec3 finalFoamColor = _Globals_.g_vFoamColor.xyz * fma(combinedfinalFoamIntensity, 0.5, 1.0);


    float postCausticsWaterColumnDepth;

    vec4 causticsDebrisTotal;
    float foamSiltStrength;
    vec3 combinedRefractedColor;
    float causticsEffectsZ;
    if (!isSkybox)
    {
        vec2 refractionUVOffsetRaw = (vec2(dot(finalPerturbedSurfaceNormal.xy, cross(invPixelDir.xyz, vec3(0.0, 0.0, -1.0)).xy), dot(finalPerturbedSurfaceNormal.xy, horizontalInvPixelDir)) + ((blueNoiseOffset * 0.002) * _Globals_.g_flWaterFogStrength)).xy * min(_Globals_.g_flRefractionLimit, finalWaterColumnDepthForRefract);
        float depthBufferRange = PerViewConstantBuffer_t.g_flViewportMaxZ - PerViewConstantBuffer_t.g_flViewportMinZ;
        float surfaceDepth = -(vec4(finalSurfacePos.xyz, 1.0).xyzw * mat4(vec4(PerViewConstantBuffer_t.g_matWorldToView._m0[0].x, PerViewConstantBuffer_t.g_matWorldToView._m0[1].x, PerViewConstantBuffer_t.g_matWorldToView._m0[2].x, PerViewConstantBuffer_t.g_matWorldToView._m0[3].x), vec4(PerViewConstantBuffer_t.g_matWorldToView._m0[0].y, PerViewConstantBuffer_t.g_matWorldToView._m0[1].y, PerViewConstantBuffer_t.g_matWorldToView._m0[2].y, PerViewConstantBuffer_t.g_matWorldToView._m0[3].y), vec4(PerViewConstantBuffer_t.g_matWorldToView._m0[0].z, PerViewConstantBuffer_t.g_matWorldToView._m0[1].z, PerViewConstantBuffer_t.g_matWorldToView._m0[2].z, PerViewConstantBuffer_t.g_matWorldToView._m0[3].z), vec4(PerViewConstantBuffer_t.g_matWorldToView._m0[0].w, PerViewConstantBuffer_t.g_matWorldToView._m0[1].w, PerViewConstantBuffer_t.g_matWorldToView._m0[2].w, PerViewConstantBuffer_t.g_matWorldToView._m0[3].w))).z;

        vec2 finalRefractionUVOffset = refractionUVOffsetRaw * clamp(fma(max((-(PerViewConstantBuffer_t.g_vDepthPsToVsConversion.x / fma(PerViewConstantBuffer_t.g_vDepthPsToVsConversion.y, clamp((textureLod(g_tSceneDepth, gbufferUV + refractionUVOffsetRaw.xy, 0.0).x - PerViewConstantBuffer_t.g_flViewportMinZ) / depthBufferRange, 0.0, 1.0), PerViewConstantBuffer_t.g_vDepthPsToVsConversion.z))) - surfaceDepth, 0.0), 0.01, refractionDistortionFactor) * 10.0, 0.0, 1.0);
        float finalRefractedNormalizedDepth = clamp((textureLod(g_tSceneDepth, gbufferUV + finalRefractionUVOffset.xy, 0.0).x - PerViewConstantBuffer_t.g_flViewportMinZ) / depthBufferRange, 0.0, 1.0);
        vec4 finalRefractedColor = texture(g_tRefractionMap, clamp(gbufferUV.xy + finalRefractionUVOffset.xy, 1.0, 0.0).xy);

        

        vec3 darkenedRefractedColor = pow(finalRefractedColor.xyz, vec3(1.1)) * _Globals_.g_flUnderwaterDarkening;
        foamSiltStrength = fma(foamSiltFactor, 2.0, _Globals_.g_flWaterFogStrength);
        float causticVisibility = clamp((dot(darkenedRefractedColor.xyz, vec3(0.2125, 0.7154, 0.0721)) - _Globals_.g_flCausticShadowCutOff) * (2.0 + _Globals_.g_flCausticShadowCutOff), 0.0, 1.0);
        

        if (causticVisibility > 0.0)
        {
            vec3 refractedViewDir = (-normalize((invPixelDir + ((PerViewConstantBuffer_t.g_vCameraUpDirWs * finalRefractionUVOffset.y) * 2.0)) + ((cross(PerViewConstantBuffer_t.g_vCameraDirWs, PerViewConstantBuffer_t.g_vCameraUpDirWs) * (-finalRefractionUVOffset.x)) * 2.0))).xyz;
            vec3 refractedSceneHitPosWs = PerViewConstantBuffer_t.g_vCameraPositionWs.xyz + (refractedViewDir * (1.0 / (fma(finalRefractedNormalizedDepth, PerViewConstantBuffer_t.g_vInvProjRow3.z, PerViewConstantBuffer_t.g_vInvProjRow3.w) * dot(PerViewConstantBuffer_t.g_vCameraDirWs.xyz, refractedViewDir))));
            bool useTriplanarCaustics = _Globals_.g_bUseTriplanarCaustics != 0;
            vec3 causticsLightDir;
            if (useTriplanarCaustics)
            {
                vec3 ditheredNormalExtent = abs(ditheredNormal);
                causticsLightDir = mix(undetermined._m2.xyz, mix(mix(vec3(0.0, 1.0, 1.0), vec3(1.0, 0.0, 1.0), bvec3(ditheredNormalExtent.y < ditheredNormalExtent.x)), vec3(0.0, 0.0, 1.0), bvec3(ditheredNormalExtent.z > max(ditheredNormalExtent.x, ditheredNormalExtent.y))), vec3(0.64999997615814208984375));
            }
            else
            {
                causticsLightDir = undetermined._m2.xyz;
            }

            float causticsDepth = worldPosForFoamAndDebrisBase.z - refractedSceneHitPosWs.z;
            vec3 causticRayTarget = mix(refractedSceneHitPosWs + ((causticsLightDir.xyz * causticsLightDir.z) * causticsDepth), finalSurfacePos.xyz, vec3(clamp((pow(NoiseValue.x, 2.0) * foamSiltStrength) * 0.0125, 0.0, 1.0)));
            float distToCausticTarget = distance(causticRayTarget, refractedSceneHitPosWs);

            vec2 causticDebrisUV = causticRayTarget.xy / _Globals_.g_flDebrisScale;
            vec4 causticDebrisSample = texture(g_tDebris, mix(causticDebrisUV, (((causticDebrisUV + (debrisWobbleOffset * finalDebrisVisibility)) + ((viewShiftedUV * noClue) * 0.1)) + ((foamWobbleAnim * 0.1) * 0.04)) - dominantFoamSiltNorm, depthFactorCoarse).xy, causticsDepth * 0.05);
            float causticDebrisCoverage = clamp((fma(-finalDebrisVisibility, 0.9, causticDebrisSample.w) - debrisEdgeFactor) * 1.1, 0.0, 1.0);


            vec4 _16121;
            _16121.w = causticDebrisCoverage;
            float causticDepthFalloffPre = distToCausticTarget / _Globals_.g_flCausticDepthFallOffDistance;
            float causticDepthFalloff = clamp(1.0 - causticDepthFalloffPre, 0.0, 1.0);

            float causticBaseIntensity = (causticVisibility * clamp(distToCausticTarget * 0.05, 0.0, 1.0)) * causticDepthFalloff;

            

            if (!useTriplanarCaustics)
            {
                causticBaseIntensity *= clamp(dot(ditheredNormal, causticsLightDir.xyz), 0.0, 1.0);
            }

            

            vec2 causticWaveUVBase = (causticRayTarget.xy * vec2(1.0 / 30)) * _Globals_.g_flCausticUVScaleMultiple;
            vec2 currWaveScale = _Globals_.g_vWaveScale;
            vec2 currWaveNormalXY = vec2(0.0);
            float currWaveDir = _Globals_.g_flWaterInitialDirection;

            uint iter = 0u;
            SPIRV_CROSS_UNROLL
            for (;;)
            {
                if (!(iter < 3u))
                {
                    break;
                }
                currWaveNormalXY.xy += (((((texture(g_tWavesNormalHeight, fma(vec2(sin(currWaveDir), cos(currWaveDir)) * ((g_flTime * _Globals_.g_flWavesSpeed) * 0.5), sqrt(vec2(1.0) / currWaveScale), (causticWaveUVBase.xy + currWaveNormalXY) / currWaveScale).xy, fma(-_Globals_.g_flCausticSharpness, 1.0 - clamp(causticDepthFalloff, 0.0, 1.0), 1.0) * 6.0).xyz - vec3(0.5)).xy * 0.5) * _Globals_.g_flCausticDistortion) * (vec2(1.0) + currWaveScale)) * (0.25 + causticDepthFalloffPre));
                currWaveScale *= _Globals_.g_flWavesPhaseOffset;

                iter++;
                currWaveDir += (3.5 / float(iter));

                
                continue;
            }

            

            vec2 currWaveScale1 = _Globals_.g_vWaveScale;
            float currWaveDir1 = _Globals_.g_flWaterInitialDirection;
            vec3 currWaveSampleSum1 = vec3(0.0);
            uint _7276;
            vec2 _9860;
            float _10150;


            //I DON'T THINK THE DECOMP IS TRUSTWORTHY ON THIS ANYMORE, waveSampleCausticDepthFalloff needs to be compared to another clean decomp
            uint causticIter1 = 0u;
            SPIRV_CROSS_UNROLL
            for (;;)
            {
                if (!(causticIter1 < 3u))
                {
                    break;
                }
                float causticIterProgress = float(causticIter1) / (float(_19857) - 1.0);

                float waveSampleCausticDepthFalloff = causticDepthFalloff;

                float waveSampleCausticDepthFalloff = _Globals_.g_flCausticSharpness * waveSampleCausticDepthFalloff;
                currWaveSampleSum1 += (((((pow(vec3(texture(g_tWavesNormalHeight, fma(vec2(sin(currWaveDir1), cos(currWaveDir1)) * ((g_flTime * _Globals_.g_flWavesSpeed) * 0.5), sqrt(vec2(1.0) / currWaveScale1), (causticWaveUVBase.xy + currWaveNormalXY) / currWaveScale1).xy, fma(-_Globals_.g_flCausticSharpness, waveSampleCausticDepthFalloff, 1.0) * 6.0).z), vec3(waveSampleCausticDepthFalloff * 5.0)) * clamp(mix(mix(fma(disturbanceWeightedFoamAmount, 0.1, _Globals_.g_flLowFreqWeight), _Globals_.g_flMedFreqWeight + disturbanceWeightedFoamAmount, clamp(causticIterProgress * 2.0, 0.0, 1.0)), fma(_Globals_.g_flHighFreqWeight, currentWaterRoughness, disturbanceWeightedFoamAmount), clamp(fma(causticIterProgress, 2.0, -1.0), 0.0, 1.0)), 0.1, 0.4000000059604644775390625)) * (vec3(1.0) + (currWaveSampleSum1 * 2.0))) * causticDepthFalloff) * waveSampleCausticDepthFalloff) * 2.0);
                currWaveScale1 *= _Globals_.g_flWavesPhaseOffset;
                causticIter1 += 1u;
                currWaveDir1 += (3.5 / float(causticIter1));
                continue;
            }
            vec4 causticsClipPos = (vec4((causticRayTarget.xyz + ((vec3(currWaveNormalXY, 0.0) * 60.0) * currWaveSampleSum1.x)).xyz, 1.0) + (PerViewConstantBuffer_t.g_vWorldToCameraOffset * 1.0)).xyzw * transpose(g_matWorldToProjection);
            vec2 causticsNdc = causticsClipPos.xy / vec2(causticsClipPos.w);


            vec2 causticsUV = (vec2(causticsNdc.x, -causticsNdc.y) * 0.5) + vec2(0.5);
            vec4 causticsEffectsSampleRaw = texture(sampler2D(g_tWaterEffectsMap, Filter_21_AllowGlobalMipBiasOverride_0_AddressU_2_AddressV_2), (causticsUV.xy * PerViewConstantBuffer_t.g_vViewportToGBufferRatio.xy).xy) - vec4(0.5);

            vec2 _20390 = clamp(causticsEffectsSampleRaw.yz * 2.0, vec2(0.0), vec2(1.0));
            vec4 finalCausticsEffectsSample = causticsEffectsSampleRaw;
            finalCausticsEffectsSample.y = _20390.x;
            finalCausticsEffectsSample.z = _20390.y;

            

            vec4 fadedCausticsEffects = finalCausticsEffectsSample * clamp((((causticsUV.y * (1.0 - causticsUV.y)) * causticsUV.x) * (1.0 - causticsUV.x)) * 40.0, 0.0, 1.0);

            float causticsXOverChangerate = fadedCausticsEffects.x + (fadedCausticsEffects.x / fma(fwidth(fadedCausticsEffects.x), 1000.0, 0.5));
            vec3 causticsModifier = (currWaveSampleSum1 + vec3(fma(clamp(causticsXOverChangerate, 0.0, 1.0) * 4.0, _Globals_.g_flWaterEffectCausticStrength, -((clamp(-causticsXOverChangerate, 0.0, 1.0) * 0.15) * _Globals_.g_flWaterEffectCausticStrength)))) * mix(1.0, 0.0, clamp(fma(causticDebrisCoverage, 2.0, fadedCausticsEffects.y * 0.4), 0.0, 1.0));
            float causticsModifierX = causticsModifier.x;
            

            vec3 modifiedCausticsRefractColor = darkenedRefractedColor * (vec3(1.0) + (((((pow(max(causticsModifier * (vec3(1.0) + (vec3(1.25, -0.25, -1.0) * (clamp(dFdxFine(causticsModifierX) * 200.0, -1.0, 1.0) * clamp(fma(-causticsModifierX, 3.0, 1.0), 0.0, 1.0)))), vec3(0.001)) * 8.0, vec3(2.5)) * causticBaseIntensity) * undetermined._m3.xyz) * _Globals_.g_vCausticsTint.xyz) * _Globals_.g_flCausticsStrength) * 0.1));
            float _16517 = pow(dot(modifiedCausticsRefractColor.xyz, vec3(0.2125, 0.7154, 0.0721)), 0.2);
            float _14717 = clamp(dFdxFine(_16517), -1.0, 1.0) + clamp(dFdyFine(_16517), -1.0, 1.0);
            causticsDebrisTotal = _16121;
            combinedRefractedColor = mix(modifiedCausticsRefractColor, modifiedCausticsRefractColor * (vec3(1.0) + (vec3(2.5, 0.0, -2.0) * float(int(sign(_14717 * clamp(abs(_14717) - 0.1, 0.0, 1.0)))))), vec3(clamp(200.0 / scaledPixelRelativePos, 0.0, 1.0) * 0.1));
            causticsEffectsZ = fadedCausticsEffects.z;
        }
        else
        {
            causticsDebrisTotal = vec4(0.0);
            combinedRefractedColor = darkenedRefractedColor;
            causticsEffectsZ = 0.0;
        }
        postCausticsWaterColumnDepth = fma(max((-(PerViewConstantBuffer_t.g_vDepthPsToVsConversion.x / fma(PerViewConstantBuffer_t.g_vDepthPsToVsConversion.y, finalRefractedNormalizedDepth, PerViewConstantBuffer_t.g_vDepthPsToVsConversion.z))) - surfaceDepth, 0.0), 0.01, refractionDistortionFactor);
    }
    else
    {
        causticsDebrisTotal = vec4(0.0);
        combinedRefractedColor = vec3(0.0);
        causticsEffectsZ = 0.0;
        foamSiltStrength = _Globals_.g_flWaterFogStrength;
        postCausticsWaterColumnDepth = finalWaterColumnDepthForRefract;
    }


    float effectiveWaterDepthForFog = min(_Globals_.g_flWaterMaxDepth, postCausticsWaterColumnDepth);
    vec3 waterDecayColorFactor = exp(((_Globals_.g_vWaterDecayColor - vec3(1.0)) * vec3(_Globals_.g_flWaterDecayStrength)) * effectiveWaterDepthForFog);
    float totalFogStrength = max(foamSiltStrength, causticsEffectsZ);
    float foamDebrisForFogMix = finalFoamIntensity + clamp(causticsEffectsZ - 0.5, 0.0, 1.0);
    float waterFogAlpha = fma(fma(-clamp(NoiseValue.x, 0.0, 1.0), 0.25, foamDebrisForFogMix), 0.1, 1.0 - exp((-effectiveWaterDepthForFog) * totalFogStrength));
    vec3 baseFogColor = mix(_Globals_.g_vWaterFogColor, finalFoamColor, vec3(foamDebrisForFogMix * 0.1)) * mix(waterDecayColorFactor, vec3(1.0), vec3(clamp(totalFogStrength * 0.04, 0.0, 1.0)));

    

    vec3 finalDirToCam = -normalize(finalSurfacePos.xyz - PerViewConstantBuffer_t.g_vCameraPositionWs.xyz);
    float specularCosAlpha = clamp(dot(-undetermined._m2.xyz, reflect(finalDirToCam, normalize(mix(normalize(usedvNormal).xyz, finalPerturbedSurfaceNormal.xyz, vec3(_Globals_.g_flSpecularNormalMultiple * fma(scaledPixelRelativePos, 0.0005, 1.0)))))), 0.0, 1.0);
    float specularExponent = mix(_Globals_.g_flSpecularPower, _Globals_.g_flDebrisReflectance * 8.0, debrisEdgeFactor) * mix(2.0, 0.2, clamp(currentWaterRoughness, 0.0, 1.0));
    float specularFactor = fma(pow(specularCosAlpha, specularExponent), 0.1, pow(specularCosAlpha, specularExponent * 10.0));

    // ------ TRUE TO DECOMPILE UP TO THIS POINT (with minor differences)-------


    float inverseWaterFogAlpha = 1.0 - waterFogAlpha;
    float waterOpacity = (clamp((1.0 - debrisEdgeFactor) + noClue, 0.0, 1.0) * clamp(fma(-combinedfinalFoamIntensity, 4.0, 1.0), 0.0, 1.0)) * inverseWaterFogAlpha;
    vec3 lightingSamplePos = offsetWorldPos.xyz + (((-viewDepOffsetFactor) * (vec3(finalDebFoamHeightContrib * (-1.0)) + (((mix(NoiseValue.xxx, vec3(NoiseValue.xy, 0.0), vec3(0.1)) * 90.0) * pow(waterOpacity, 2.0)) + vec3(_Globals_.g_flWaterPlaneOffset)))) * mix(1.0, effectiveWaterDepthForFog * 2.0, 0.75));
    vec4 surfaceNormal4f = vec4(finalSurfaceNormal.xyz, 1.0);
    vec3 ambientTerm = vec3(dot(undetermined._m0._m0[0].xyzw, surfaceNormal4f), dot(undetermined._m0._m0[1].xyzw, surfaceNormal4f), dot(undetermined._m0._m0[2].xyzw, surfaceNormal4f));
    float finalShadowCoverage = 1.0;


    // ---DETERMINE CORRECT CASCADE TO SAMPLE---
    //undetermined._m7
    int NumOfCascades = 3;

    if (NumOfCascades != 0)
    {
        vec4 lightSamplePos4f = vec4(lightingSamplePos.xyz, 1.0);
        int finalCascadeIndex;
        float shadowCascadeLerpFactor;
        vec3 shadowSpaceFragCoord;
        int iterator = 0;
        for (;;)
        {
            if (!(iterator < NumOfCascades))
            {
                shadowCascadeLerpFactor = 1.0;
                shadowSpaceFragCoord = vec3(0.0);
                finalCascadeIndex = -1;
                break;
            }
            vec4 lightSpaceCoord = lightSamplePos4f * mat4(vec4(undetermined._m14._m0[iterator]._m0[0].x, undetermined._m14._m0[iterator]._m0[1].x, undetermined._m14._m0[iterator]._m0[2].x, undetermined._m14._m0[iterator]._m0[3].x), vec4(undetermined._m14._m0[iterator]._m0[0].y, undetermined._m14._m0[iterator]._m0[1].y, undetermined._m14._m0[iterator]._m0[2].y, undetermined._m14._m0[iterator]._m0[3].y), vec4(undetermined._m14._m0[iterator]._m0[0].z, undetermined._m14._m0[iterator]._m0[1].z, undetermined._m14._m0[iterator]._m0[2].z, undetermined._m14._m0[iterator]._m0[3].z), vec4(undetermined._m14._m0[iterator]._m0[0].w, undetermined._m14._m0[iterator]._m0[1].w, undetermined._m14._m0[iterator]._m0[2].w, undetermined._m14._m0[iterator]._m0[3].w));
            if (max(abs(lightSpaceCoord.x), abs(lightSpaceCoord.y)) < undetermined._m9[iterator])
            {
                shadowSpaceFragCoord = vec3(lightSpaceCoord.xyz);
                vec2 lerpXYComponents = vec2(1.0) - clamp(fma(abs(shadowSpaceFragCoord.xy), vec2(undetermined._m11), vec2(undetermined._m10)), vec2(0.0), vec2(1.0));
                shadowSpaceFragCoord.xy = fma(shadowSpaceFragCoord.xy, undetermined._m15._m0[iterator].zw, undetermined._m15._m0[iterator].xy);
                shadowCascadeLerpFactor = clamp(lerpXYComponents.x * lerpXYComponents.y, 0.0, 1.0);
                finalCascadeIndex = iterator;
                break;
            }
            iterator += 1;
            continue;
        }
        float finalCsmCoverage = 1.0;
        if (finalCascadeIndex >= 0)
        {
            float shadowCoverage = textureLod(sampler2DShadow(g_tShadowDepthBufferDepth,s_ShadowSamplerComparison), vec3(shadowSpaceFragCoord.xy, clamp(shadowSpaceFragCoord.z + g_flShadowCascadeReceiverDepthBias, 0.0, 1.0)), 0.0);
            if (shadowCascadeLerpFactor < 1.0)
            {
                float secondCascadeShadowCoverage;
                if (finalCascadeIndex < (undetermined._m7 - 1))
                {
                    int secondCascadeIndex = finalCascadeIndex + 1;
                    vec4 secondCascadeShadowSpaceFragCoord = lightSamplePos4f * mat4(vec4(undetermined._m14._m0[secondCascadeIndex]._m0[0].x, undetermined._m14._m0[secondCascadeIndex]._m0[1].x, undetermined._m14._m0[secondCascadeIndex]._m0[2].x, undetermined._m14._m0[secondCascadeIndex]._m0[3].x), vec4(undetermined._m14._m0[secondCascadeIndex]._m0[0].y, undetermined._m14._m0[secondCascadeIndex]._m0[1].y, undetermined._m14._m0[secondCascadeIndex]._m0[2].y, undetermined._m14._m0[secondCascadeIndex]._m0[3].y), vec4(undetermined._m14._m0[secondCascadeIndex]._m0[0].z, undetermined._m14._m0[secondCascadeIndex]._m0[1].z, undetermined._m14._m0[secondCascadeIndex]._m0[2].z, undetermined._m14._m0[secondCascadeIndex]._m0[3].z), vec4(undetermined._m14._m0[secondCascadeIndex]._m0[0].w, undetermined._m14._m0[secondCascadeIndex]._m0[1].w, undetermined._m14._m0[secondCascadeIndex]._m0[2].w, undetermined._m14._m0[secondCascadeIndex]._m0[3].w));
                    secondCascadeShadowSpaceFragCoord.xy = fma(secondCascadeShadowSpaceFragCoord.xy, undetermined._m15._m0[secondCascadeIndex].zw, undetermined._m15._m0[secondCascadeIndex].xy);
                    secondCascadeShadowCoverage = textureLod(g_tShadowDepthBufferDepth, vec3(secondCascadeShadowSpaceFragCoord.xy, clamp(secondCascadeShadowSpaceFragCoord.z + g_flShadowCascadeReceiverDepthBias, 0.0, 1.0)), 0.0);
                }
                else
                {
                    secondCascadeShadowCoverage = 1.0;
                }
                finalCsmCoverage = mix(secondCascadeShadowCoverage, shadowCoverage, shadowCascadeLerpFactor);
            }
            else
            {
                finalCsmCoverage = shadowCoverage;
            }
        }

        float finalFadedCsmCoverage = mix(finalCsmCoverage, 1.0, clamp(fma(distance(trueWorldPos, PerViewConstantBuffer_t.g_vCameraPositionWs), g_flShadowCascadeZLerpFactorScale, g_flShadowCascadeZLerpFactorOffset), 0.0, 1.0));
        finalShadowCoverage = finalFadedCsmCoverage;

        if (notEqual(PerViewConstantBufferCsgo_t.g_bOtherFxEnabled, ivec4(0)).y)
        {
            finalShadowCoverage = min(finalFadedCsmCoverage, textureLod(sampler2D(g_tParticleShadowBuffer, Filter_21_AllowGlobalMipBiasOverride_0_AddressU_2_AddressV_2), (FragCoordWInverse.xy * PerViewConstantBuffer_t.g_vInvGBufferSize.xy).xy, 0.0).z);
        }
    }

    float finalShadowingEffect = mix(finalShadowCoverage, 1.0, waterOpacity * 0.5);
    vec3 lightingFactor;
    if ((dot(g_vFastPathSunLightDir.xyz, finalSurfaceNormal.xyz) * finalShadowingEffect) > 0.0)
    {
        lightingFactor = fma(vec3(max(0.0, dot(finalSurfaceNormal.xyz, g_vFastPathSunLightDir.xyz))).xyz, (g_vFastPathSunLightColor.xyz * finalShadowingEffect).xyz, g_vToolsAmbientLighting.xyz);
    }
    else
    {
        lightingFactor = g_vToolsAmbientLighting.xyz;
    }
    bvec4 _24465 = notEqual(PerViewConstantBufferCsgo_t.g_bOtherEnabled2, ivec4(0));
    bool _20060 = _24465.x;
    vec4 pixelCoordInvW = FragCoordWInverse;
    //this "if" used _20060 before, I am guessing they are identical but I am leaving both here for good measure.
    if (PerViewConstantBufferCsgo_t.g_bOtherEnabled2.x)
    {
        //vec4 _24261 = vec4(trueWorldPos, 1.0).xyzw * mat4(vec4(PerViewConstantBufferCsgo_t.g_matPrimaryViewWorldToProjection._m0[0].x, PerViewConstantBufferCsgo_t.g_matPrimaryViewWorldToProjection._m0[1].x, PerViewConstantBufferCsgo_t.g_matPrimaryViewWorldToProjection._m0[2].x, PerViewConstantBufferCsgo_t.g_matPrimaryViewWorldToProjection._m0[3].x), vec4(PerViewConstantBufferCsgo_t.g_matPrimaryViewWorldToProjection._m0[0].y, PerViewConstantBufferCsgo_t.g_matPrimaryViewWorldToProjection._m0[1].y, PerViewConstantBufferCsgo_t.g_matPrimaryViewWorldToProjection._m0[2].y, PerViewConstantBufferCsgo_t.g_matPrimaryViewWorldToProjection._m0[3].y), vec4(PerViewConstantBufferCsgo_t.g_matPrimaryViewWorldToProjection._m0[0].z, PerViewConstantBufferCsgo_t.g_matPrimaryViewWorldToProjection._m0[1].z, PerViewConstantBufferCsgo_t.g_matPrimaryViewWorldToProjection._m0[2].z, PerViewConstantBufferCsgo_t.g_matPrimaryViewWorldToProjection._m0[3].z), vec4(PerViewConstantBufferCsgo_t.g_matPrimaryViewWorldToProjection._m0[0].w, PerViewConstantBufferCsgo_t.g_matPrimaryViewWorldToProjection._m0[1].w, PerViewConstantBufferCsgo_t.g_matPrimaryViewWorldToProjection._m0[2].w, PerViewConstantBufferCsgo_t.g_matPrimaryViewWorldToProjection._m0[3].w));
        vec4 _24261 = vec4(trueWorldPos, 1.0).xyzw * transpose(PerViewConstantBufferCsgo_t.g_matPrimaryViewWorldToProjection._m0);
        vec2 ndcXY = _24261.xy / _24261.w;
        vec4 _6654;
        _6654.x = clamp(((ndcXY.x + 1.0) * PerViewConstantBuffer_t.g_vViewportSize.x) * 0.5, 0.0, PerViewConstantBuffer_t.g_vViewportSize.x - 1.0);
        _6654.y = clamp(((1.0 - ndcXY.y) * PerViewConstantBuffer_t.g_vViewportSize.y) * 0.5, 0.0, PerViewConstantBuffer_t.g_vViewportSize.y - 1.0);
        _6654.w = _20181;
        pixelCoordInvW = _6654;
    }

    uvec2 _12083 = uvec2(pixelCoordInvW.xy - PerViewConstantBuffer_t.g_vViewportOffset.xy) >> uvec2(g_vTileCullParams.x);
    uint _10838 = g_vLightCullParams.y + (((_12083.y * g_vTileCullParams.y) + _12083.x) * g_vLightCullParams.z);
    uint _23393 = g_vLightCullParams.x + (uint(clamp(pixelCoordInvW.w * undetermined._m6.x, 0.0, undetermined._m6.y)) * g_vLightCullParams.z);
    vec3 _13155;
    _13155 = lightingFactor;
    uint _7172;
    vec3 _13156;
    uint _16208 = 0u;
    for (;;)
    {
        if (!(_16208 < g_vLightCullParams.z))
        {
            break;
        }
        uint _13365 = subgroupOr(g_CullBits_1._m0[_10838 + _16208] & g_CullBits_1._m0[_23393 + _16208]);
        uint _24597 = _16208 * 32u;
        _7172 = _16208 + 1u;
        _13156 = _13155;
        uint _20344;
        vec3 _12504;
        uint _16209 = _13365;
        for (;;)
        {
            if (!(_16209 != 0u))
            {
                break;
            }
            int _11281 = int(uint(findLSB(_16209)) + _24597);
            _20344 = _16209 & (_16209 - 1u);
            do
            {
                vec3 _14644 = lightingSamplePos.xyz;
                vec4 _15817 = mat4(vec4(g_BarnLights_1._m0[_11281]._m0._m0[0].x, g_BarnLights_1._m0[_11281]._m0._m0[1].x, g_BarnLights_1._m0[_11281]._m0._m0[2].x, g_BarnLights_1._m0[_11281]._m0._m0[3].x), vec4(g_BarnLights_1._m0[_11281]._m0._m0[0].y, g_BarnLights_1._m0[_11281]._m0._m0[1].y, g_BarnLights_1._m0[_11281]._m0._m0[2].y, g_BarnLights_1._m0[_11281]._m0._m0[3].y), vec4(g_BarnLights_1._m0[_11281]._m0._m0[0].z, g_BarnLights_1._m0[_11281]._m0._m0[1].z, g_BarnLights_1._m0[_11281]._m0._m0[2].z, g_BarnLights_1._m0[_11281]._m0._m0[3].z), vec4(g_BarnLights_1._m0[_11281]._m0._m0[0].w, g_BarnLights_1._m0[_11281]._m0._m0[1].w, g_BarnLights_1._m0[_11281]._m0._m0[2].w, g_BarnLights_1._m0[_11281]._m0._m0[3].w)) * vec4(lightingSamplePos.xyz, 1.0);
                vec3 _10521 = _15817.xyz / vec3(_15817.w);
                vec4 _22905;
                _22905.x = _10521.x;
                _22905.y = _10521.y;
                _22905.z = _10521.z;
                vec3 _21543 = _22905.xyz;
                vec3 _21662;
                if ((g_BarnLights_1._m0[_11281]._m14 & 4u) != 0u)
                {
                    vec2 _6281 = _22905.yx * vec2(1.0, -1.0);
                    vec3 _23716 = _21543;
                    _23716.x = _6281.x;
                    _23716.y = _6281.y;
                    _21662 = _23716;
                }
                else
                {
                    _21662 = _21543;
                }
                bool _7424;
                if (all(greaterThan(_21662.xyz, vec3(-1.0, -1.0, 0.0))))
                {
                    _7424 = all(lessThan(_21662.xyz, vec3(1.0)));
                }
                else
                {
                    _7424 = false;
                }
                bool _12886;
                if (!_7424)
                {
                    _12886 = true;
                }
                else
                {
                    _12886 = !all(lessThanEqual(abs((mat4x3(vec3(g_BarnLights_1._m0[_11281]._m15._m0[0].x, g_BarnLights_1._m0[_11281]._m15._m0[1].x, g_BarnLights_1._m0[_11281]._m15._m0[2].x), vec3(g_BarnLights_1._m0[_11281]._m15._m0[0].y, g_BarnLights_1._m0[_11281]._m15._m0[1].y, g_BarnLights_1._m0[_11281]._m15._m0[2].y), vec3(g_BarnLights_1._m0[_11281]._m15._m0[0].z, g_BarnLights_1._m0[_11281]._m15._m0[1].z, g_BarnLights_1._m0[_11281]._m15._m0[2].z), vec3(g_BarnLights_1._m0[_11281]._m15._m0[0].w, g_BarnLights_1._m0[_11281]._m15._m0[1].w, g_BarnLights_1._m0[_11281]._m15._m0[2].w)) * vec4(_14644, 1.0)).xyz), vec3(1.0)));
                }
                if (_12886)
                {
                    _12504 = _13156;
                    break;
                }
                float _12415 = g_BarnLights_1._m0[_11281]._m5.z * (-2.0);
                float _21996 = 2.0 * g_BarnLights_1._m0[_11281]._m5.x;
                float _15157 = 2.0 * g_BarnLights_1._m0[_11281]._m5.w;
                float _19536 = _15157 * g_BarnLights_1._m0[_11281]._m5.z;
                vec3 _16268 = vec3(fma(_21996, g_BarnLights_1._m0[_11281]._m5.y, -_19536), fma(_12415, g_BarnLights_1._m0[_11281]._m5.z, fma(g_BarnLights_1._m0[_11281]._m5.x * (-2.0), g_BarnLights_1._m0[_11281]._m5.x, 1.0)), fma(2.0 * g_BarnLights_1._m0[_11281]._m5.y, g_BarnLights_1._m0[_11281]._m5.z, _15157 * g_BarnLights_1._m0[_11281]._m5.x)) * g_BarnLights_1._m0[_11281]._m6.z;
                float _21316;
                if (g_BarnLights_1._m0[_11281]._m3.z > 0.0)
                {
                    _21316 = min(1.0, _21662.z * g_BarnLights_1._m0[_11281]._m3.z);
                }
                else
                {
                    _21316 = 1.0;
                }
                float _19667;
                if (g_BarnLights_1._m0[_11281]._m3.w > 0.0)
                {
                    _19667 = _21316 * min(1.0, (1.0 - _21662.z) * g_BarnLights_1._m0[_11281]._m3.w);
                }
                else
                {
                    _19667 = _21316;
                }
                vec3 _11179;
                float _11937;
                if (g_BarnLights_1._m0[_11281]._m2.w != 0.0)
                {
                    vec3 _10017 = g_BarnLights_1._m0[_11281]._m2.xyz - _14644;
                    float _18345 = dot(_10017, _10017);
                    float _17647 = sqrt(_18345);
                    vec3 _12302 = _10017 - _16268;
                    vec3 _10210;
                    do
                    {
                        vec3 _20229 = (_10017 + _16268) - _12302;
                        float _25105 = dot(-_12302, _20229);
                        if (_25105 <= 0.0)
                        {
                            _10210 = _12302;
                            break;
                        }
                        else
                        {
                            _10210 = _12302 + (_20229 * min(1.0, _25105 / dot(_20229, _20229)));
                            break;
                        }
                        break; // unreachable workaround
                    } while(false);
                    _11179 = _10017 / vec3(_17647);
                    _11937 = ((_19667 * (g_BarnLights_1._m0[_11281]._m2.w / max(_18345, g_BarnLights_1._m0[_11281]._m2.w))) * clamp(fma(g_BarnLights_1._m0[_11281]._m3.y, _17647, g_BarnLights_1._m0[_11281]._m3.x), 0.0, 1.0)) * clamp(fma(g_BarnLights_1._m0[_11281]._m6.y, dot(vec3(fma(_12415, g_BarnLights_1._m0[_11281]._m5.z, fma(g_BarnLights_1._m0[_11281]._m5.y * (-2.0), g_BarnLights_1._m0[_11281]._m5.y, 1.0)), fma(_21996, g_BarnLights_1._m0[_11281]._m5.y, _19536), fma(_21996, g_BarnLights_1._m0[_11281]._m5.z, -(_15157 * g_BarnLights_1._m0[_11281]._m5.y))), normalize(_10210)), g_BarnLights_1._m0[_11281]._m6.x), 0.0, 1.0);
                }
                else
                {
                    _11179 = g_BarnLights_1._m0[_11281]._m2.xyz;
                    _11937 = _19667;
                }
                vec3 _15440 = (g_BarnLights_1._m0[_11281]._m4.xyz * 1.0).xyz * _11937;
                bool _24419;
                if (g_BarnLights_1._m0[_11281]._m8.z > 0.0)
                {
                    _24419 = !_20060;
                }
                else
                {
                    _24419 = false;
                }
                vec3 _21548;
                if (g_BarnLights_1._m0[_11281]._m4.w == 0.0)
                {
                    float _10342;
                    do
                    {
                        vec2 _22154 = abs(_21662.xy);
                        if (g_BarnLights_1._m0[_11281]._m9.z == 0.0)
                        {
                            _10342 = smoothstep(1.0, g_BarnLights_1._m0[_11281]._m9.x, _22154.x) * smoothstep(1.0, g_BarnLights_1._m0[_11281]._m9.y, _22154.y);
                            break;
                        }
                        else
                        {
                            float _11473 = _22154.x;
                            float _15266 = 2.0 / g_BarnLights_1._m0[_11281]._m9.z;
                            float _15017 = _22154.y;
                            float _23041 = (-0.5) * g_BarnLights_1._m0[_11281]._m9.z;
                            float _11981 = (g_BarnLights_1._m0[_11281]._m9.x * g_BarnLights_1._m0[_11281]._m9.y) * pow(max(pow(g_BarnLights_1._m0[_11281]._m9.y * _11473, _15266) + pow(g_BarnLights_1._m0[_11281]._m9.x * _15017, _15266), 1.1754943508222875079687365372222e-38), _23041);
                            float _16524 = pow(max(pow(_11473, _15266) + pow(_15017, _15266), 1.1754943508222875079687365372222e-38), _23041);
                            if (_11981 < _16524)
                            {
                                _10342 = smoothstep(_16524, _11981, 1.0);
                                break;
                            }
                            else
                            {
                                _10342 = float(_16524 > 1.0);
                                break;
                            }
                            break; // unreachable workaround
                        }
                        break; // unreachable workaround
                    } while(false);
                    _21548 = _15440.xyz * _10342;
                }
                else
                {
                    vec3 _12503;
                    if (g_BarnLights_1._m0[_11281]._m4.w < 0.0)
                    {
                        vec4 _17795 = vec4(-g_BarnLights_1._m0[_11281]._m5.xyz, g_BarnLights_1._m0[_11281]._m5.w);
                        vec4 _19008 = _17795.xyzw * vec4(-1.0, -1.0, -1.0, 1.0);
                        vec3 _24989 = _19008.xyz;
                        vec3 _23629 = vec4((-_11179).xyz, 0.0).xyz;
                        float _15156 = -dot(_23629, _24989);
                        vec3 _20479 = vec4((_23629 * _19008.w) + cross(_23629, _24989), _15156).xyz;
                        vec3 _23592 = _17795.xyz;
                        vec3 _12170 = ((_20479 * g_BarnLights_1._m0[_11281]._m5.w) + (_23592 * _15156)) + cross(_23592, _20479);
                        vec3 _14385 = vec3(vec2(atan(_12170.y, -_12170.x) * 0.15915493667125701904296875, acos(_12170.z) * 0.3183098733425140380859375), -g_BarnLights_1._m0[_11281]._m4.w);
                        vec2 _13665 = fma(_14385.xy, g_BarnLights_1._m0[_11281]._m9.zw, g_BarnLights_1._m0[_11281]._m9.xy);
                        vec3 _19313 = _14385;
                        _19313.x = _13665.x;
                        _19313.y = _13665.y;
                        _12503 = _15440.xyz * textureLod(sampler3D(g_tLightCookieTexture, Filter_21_AddressU_0_AddressV_0_AllowGlobalMipBiasOverride_0), _19313.xyz, 0.0).xyz;
                    }
                    else
                    {
                        vec3 _14095 = vec3(fma(_22905.xy, vec2(0.5, -0.5), vec2(0.5)), g_BarnLights_1._m0[_11281]._m4.w);
                        vec2 _13664 = fma(_14095.xy, g_BarnLights_1._m0[_11281]._m9.zw, g_BarnLights_1._m0[_11281]._m9.xy);
                        vec3 _19312 = _14095;
                        _19312.x = _13664.x;
                        _19312.y = _13664.y;
                        _12503 = _15440.xyz * textureLod(sampler3D(g_tLightCookieTexture, Filter_20_AddressU_3_AddressV_3_AddressW_3_BorderColor_0), _19312.xyz, 0.0).xyz;
                    }
                    _21548 = _12503;
                }
                if (all(equal(_21548.xyz, vec3(0.0))))
                {
                    _12504 = _13156;
                    break;
                }
                vec3 _19629;
                if (_24419)
                {
                    vec3 _20482 = _21548.xyz * mix(1.0, textureLod(sampler2DShadow(g_tShadowDepthBufferDepth, AddressU_2_AddressV_2_Filter_149_ComparisonFunc_3), vec3(vec3(fma(_21662.xy, g_BarnLights_1._m0[_11281]._m8.zw, g_BarnLights_1._m0[_11281]._m8.xy), _21662.z).xy, clamp(_21662.z + undetermined._m8, 0.0, 1.0)), 0.0), g_BarnLights_1._m0[_11281]._m12);
                    if (all(equal(_20482.xyz, vec3(0.0))))
                    {
                        _12504 = _13156;
                        break;
                    }
                    _19629 = _20482;
                }
                else
                {
                    _19629 = _21548;
                }
                _12504 = fma(vec3(max(0.0, dot(finalSurfaceNormal.xyz, _11179.xyz))).xyz, _19629.xyz, _13156.xyz);
                break;
            } while(false);
            _13156 = _12504;
            _16209 = _20344;
            continue;
        }
        _13155 = _13156;
        _16208 = _7172;
        continue;
    }

    vec3 _22686 = (_13155.xyz + ambientTerm) * mix(mix((baseFogColor * waterFogAlpha) * _Globals_.g_flWaterFogShadowStrength, finalFoamColor.xyz, vec3(combinedfinalFoamIntensity)), vec4(debrisColorHeightSample.xyz * fma(finalDebrisFactor, 0.5, 0.5), debrisEdgeFactor).xyz * _Globals_.g_vDebrisTint, vec3(clamp(debrisEdgeFactor - noClue, 0.0, 1.0))).xyz;
    vec4 _11206 = vec4(_22686, _21011);
    _11206.xyz = _22686.xyz;

    //LETS GET HERE TODAY UURAAA
    vec3 returnColor0 = mix(_11206.xyz, combinedRefractedColor * waterDecayColorFactor, vec3(waterOpacity));

    vec4 returnColor1 = _11206;
    returnColor1.x = returnColor0.x;
    returnColor1.y = returnColor0.y;
    returnColor1.z = returnColor0.z;
    vec3 _10929 = mix(returnColor1.xyz, (baseFogColor * 4.0) * ambientTerm, vec3((waterFogAlpha * clamp((1.0 - surfaceCoverageAlpha) + noClue, 0.0, 1.0)) * (1.0 - _Globals_.g_flWaterFogShadowStrength)));
    vec4 returnColor2 = returnColor1;
    returnColor2.x = _10929.x;
    returnColor2.y = _10929.y;
    returnColor2.z = _10929.z;

    vec3 onePart = (dot(ambientTerm.xyz, vec3(0.2125000059604644775390625, 0.7153999805450439453125, 0.07209999859333038330078125)) * _Globals_.g_flLowEndCubeMapIntensity)
    float anotherPart = sqrt(dot(mix(_Globals_.g_vRoughness, vec2(1.0), vec2(clamp(reflectionsLodFactor, 0.0, 0.3499999940395355224609375))).xy, vec2(0.5))
    vec3 cubemapReflection = (textureLod(samplerCube(g_tLowEndCubeMap, DefaultSamplerState_0_1),       (-reflect(finalDirToCam, finalSurfaceNormal).xyz).xyz,        anotherPart ) * 6.0).xyz * onePart) * _Globals_.g_flEnvironmentMapBrightness;
    float _9473 = clamp((PerViewConstantBuffer_t.g_vCameraDirWs.z + 0.75) * 4.0, 0.0, 1.0);
    float _13437 = float(isSkybox);
    uint _4344 = uint((float(_Globals_.g_nSSRMaxForwardSteps) * mix(1.0, 0.5, _13437)) * _9473);
    vec3 finalReflectionColor;
    if (_4344 > 0u)
    {
        float SsrHitThickness = fma(blueNoiseDitherFactor, _Globals_.g_flSSRSampleJitter, _Globals_.g_flSSRMaxThickness);
        mat4 transWorldToView = mat4(vec4(PerViewConstantBuffer_t.g_matWorldToView._m0[0].x, PerViewConstantBuffer_t.g_matWorldToView._m0[1].x, PerViewConstantBuffer_t.g_matWorldToView._m0[2].x, PerViewConstantBuffer_t.g_matWorldToView._m0[3].x), vec4(PerViewConstantBuffer_t.g_matWorldToView._m0[0].y, PerViewConstantBuffer_t.g_matWorldToView._m0[1].y, PerViewConstantBuffer_t.g_matWorldToView._m0[2].y, PerViewConstantBuffer_t.g_matWorldToView._m0[3].y), vec4(PerViewConstantBuffer_t.g_matWorldToView._m0[0].z, PerViewConstantBuffer_t.g_matWorldToView._m0[1].z, PerViewConstantBuffer_t.g_matWorldToView._m0[2].z, PerViewConstantBuffer_t.g_matWorldToView._m0[3].z), vec4(PerViewConstantBuffer_t.g_matWorldToView._m0[0].w, PerViewConstantBuffer_t.g_matWorldToView._m0[1].w, PerViewConstantBuffer_t.g_matWorldToView._m0[2].w, PerViewConstantBuffer_t.g_matWorldToView._m0[3].w));
        vec4 SSNormal4f = vec4(normalize(vec3((finalSurfaceNormal.xy * 3.0) * mix(2.0, 8.0, _13437), finalSurfaceNormal.z)).xyz, 0.0).xyzw * transWorldToView;
        vec3 SSNormal = SSNormal4f.xyz;
        vec2 _23669 = SSNormal4f.yz * 2.0;
        SSNormal.y = _23669.x;
        SSNormal.z = _23669.y;
        vec3 viewSpacePos = (vec4(finalSurfacePos.xyz, 1.0).xyzw * transWorldToView).xyz;
        mat4 transViewToProj = mat4(vec4(PerViewConstantBuffer_t.g_matViewToProjection._m0[0].x, PerViewConstantBuffer_t.g_matViewToProjection._m0[1].x, PerViewConstantBuffer_t.g_matViewToProjection._m0[2].x, PerViewConstantBuffer_t.g_matViewToProjection._m0[3].x), vec4(PerViewConstantBuffer_t.g_matViewToProjection._m0[0].y, PerViewConstantBuffer_t.g_matViewToProjection._m0[1].y, PerViewConstantBuffer_t.g_matViewToProjection._m0[2].y, PerViewConstantBuffer_t.g_matViewToProjection._m0[3].y), vec4(PerViewConstantBuffer_t.g_matViewToProjection._m0[0].z, PerViewConstantBuffer_t.g_matViewToProjection._m0[1].z, PerViewConstantBuffer_t.g_matViewToProjection._m0[2].z, PerViewConstantBuffer_t.g_matViewToProjection._m0[3].z), vec4(PerViewConstantBuffer_t.g_matViewToProjection._m0[0].w, PerViewConstantBuffer_t.g_matViewToProjection._m0[1].w, PerViewConstantBuffer_t.g_matViewToProjection._m0[2].w, PerViewConstantBuffer_t.g_matViewToProjection._m0[3].w));
        vec4 _15818 = transViewToProj * vec4(-viewSpacePos, 1.0);
        vec3 SsrNdcCoords = _15818.xyz / vec3(_15818.w);
        vec2 _21671 = (vec2(SsrNdcCoords.x, -SsrNdcCoords.y) * 0.5) + vec2(0.5);
        vec4 _20492;
        _20492.x = _21671.x;
        _20492.y = _21671.y;
        float _9277 = (fma(blueNoiseDitherFactor, _Globals_.g_flSSRSampleJitter, _Globals_.g_flSSRStepSize) / fma(reflectionsLodFactor, 2.0, 1.0)) * mix(20.0, 1.0, cosNormAng);
        float SsrBaseStepsize;
        if (isSkybox)
        {
            SsrBaseStepsize = _9277 * (scaledPixelRelativePos * 0.002);
        }
        else
        {
            SsrBaseStepsize = _9277;
        }
        vec3 SSReflectDir = normalize(reflect(normalize(viewSpacePos), normalize(SSNormal))).xyz;
        vec3 prevSamplePos = viewSpacePos;
        vec4 _17127;
        uint iterator;
        _17127 = _20492;
        iterator = 1u;
        float currStepSize;
        float currSampleWorldDepth;
        vec3 currSamplePos;
        vec4 _19411;
        float currSampleWorldDepth;
        float _9985;
        vec4 _23824;
        float prevWorldDepth = 0.0;
        float finalPrevCurrFrac = 0.0;
        float prevStepSize = SsrBaseStepsize;
        for (;;)
        {
            if (!(iterator <= _4344))
            {
                _9985 = finalPrevCurrFrac;
                _23824 = _17127;
                break;
            }
            currStepSize = prevStepSize * 1.14999997615814208984375;
            currSamplePos = prevSamplePos + (SSReflectDir * currStepSize);
            vec4 currViewSpacePos = transViewToProj * vec4(-currSamplePos, 1.0);
            vec3 _10510 = currViewSpacePos.xyz / vec3(currViewSpacePos.w);
            vec2 currSsrUV = (vec2(_10510.x, -_10510.y) * 0.5) + vec2(0.5);
            _19411.xy = currSsrUV.xy;
            currSampleWorldDepth = (PerViewConstantBuffer_t.g_vDepthPsToVsConversion.x / fma(PerViewConstantBuffer_t.g_vDepthPsToVsConversion.y, clamp((textureLod(sampler2D(g_tSceneDepth, AllowGlobalMipBiasOverride_0_AddressU_2_AddressV_2_Filter_0_AddressW_2), _19411.xy * PerViewConstantBuffer_t.g_vViewportToGBufferRatio.xy, 0.0).x - PerViewConstantBuffer_t.g_flViewportMinZ) / (PerViewConstantBuffer_t.g_flViewportMaxZ - PerViewConstantBuffer_t.g_flViewportMinZ), 0.0, 1.0), PerViewConstantBuffer_t.g_vDepthPsToVsConversion.z)) - currSamplePos.z;
            prevCurrFrac = clamp(currSampleWorldDepth / (currSampleWorldDepth - prevWorldDepth), 0.0, 1.0);
            if (currSampleWorldDepth >= 0.0)
            {
                if(currSampleWorldDepth < (SsrHitThickness * currStepSize))
                {
                    _9985 = prevCurrFrac;
                    _23824 = mix(_19411, _17127, vec4(prevCurrFrac));
                break;
                }
            }  
            iterator += 1u;
            prevWorldDepth = currSampleWorldDepth;
            prevSamplePos = currSamplePos;
            _17127 = _19411;
            prevStepSize = currStepSize;
            finalPrevCurrFrac = prevCurrFrac;
            continue;
        }
        float fracOfTotalSteps = (float(iterator) - _9985) / float(_4344);
        vec3 SsrReflectionResult;
        if (!isSkybox)
        {
            vec2 scaledSsrUVs = (_23824.xy * PerViewConstantBuffer_t.g_vViewportToGBufferRatio.xy).xy;
            float _8505 = fracOfTotalSteps * (-0.00390625);
            vec3 _14528 = (((texture(sampler2D(g_tRefractionMap, Filter_21_AllowGlobalMipBiasOverride_0_AddressU_2_AddressV_2), clamp(scaledSsrUVs - vec2(fracOfTotalSteps * 0.00390625), vec2(0.0), vec2(1.0)).xy).xyz * 0.4444443881511688232421875) + (texture(sampler2D(g_tRefractionMap, Filter_21_AllowGlobalMipBiasOverride_0_AddressU_2_AddressV_2), clamp(scaledSsrUVs + vec2(0.001953125, _8505), vec2(0.0), vec2(1.0)).xy).xyz * 0.22222219407558441162109375)) + (texture(sampler2D(g_tRefractionMap, Filter_21_AllowGlobalMipBiasOverride_0_AddressU_2_AddressV_2), clamp(scaledSsrUVs + vec2(_8505, 0.001953125), vec2(0.0), vec2(1.0)).xy).xyz * 0.22222219407558441162109375)) + (texture(sampler2D(g_tRefractionMap, Filter_21_AllowGlobalMipBiasOverride_0_AddressU_2_AddressV_2), clamp(scaledSsrUVs + vec2(0.001953125), vec2(0.0), vec2(1.0)).xy).xyz * 0.111111097037792205810546875);
            SsrReflectionResult = (_14528 + ((normalize(_14528 + vec3(0.001)) * max(0.0, dot(_14528.xyz, vec3(0.2125, 0.7154, 0.0721)) - _Globals_.g_flSSRBoostThreshold)) * _Globals_.g_flSSRBoost)) * _Globals_.g_flSSRBrightness;
        }
        else
        {
            SsrReflectionResult = mix((returnColor2.xyz + cubemapReflection) * 0.5, cubemapReflection, vec3(fracOfTotalSteps));
        }
        finalReflectionColor = mix(cubemapReflection, SsrReflectionResult, vec3((clamp(1.0 - pow(fracOfTotalSteps, 4.0), 0.0, 1.0) * clamp(_23824.y * 8.0, 0.0, 1.0)) * clamp(_9473 * 2.0, 0.0, 1.0)));
    }
    else
    {
        finalReflectionColor = cubemapReflection;
    }
    float localReflectance = mix(_Globals_.g_flReflectance, _Globals_.g_flDebrisReflectance, finalDebrisFactor);
    float reflectionModulation = (fma(fresnel, 1.0 - localReflectance, localReflectance) * fma(-combinedfinalFoamIntensity, 2.0, fma(-surfaceCoverageAlpha, 0.75, 1.0))) * 1.5;
    vec3 finalSpecularReflection = fma((_13155.xyz * (fma(max(0.0, specularFactor - (1.0 - _Globals_.g_flSpecularBloomBoostThreshold)), _Globals_.g_flSpecularBloomBoostStrength, specularFactor) * mix(1.0, _Globals_.g_flDebrisReflectance * 0.05, debrisEdgeFactor))) * reflectionModulation, undetermined._m3.xyz, returnColor2.xyz);
    vec4 returnColor3 = returnColor2;
    returnColor3.x = finalSpecularReflection.x;
    returnColor3.y = finalSpecularReflection.y;
    returnColor3.z = finalSpecularReflection.z;
    float _8302 = fract(fma(PerViewConstantBuffer_t.g_flTime, 0.1, fma(fresnel, 20.0, debrisHeightVal * 8.0)));

    float _15999 = floor(_8302 * 6.0);
    float _22138 = fract(_8302 * 6.0);

    float _6700 = 0.75 * (1.0 - _22138);
    float _14751 = 0.75 * _22138;
    vec3 _11313;
    if (floor(_8302 * 6.0) == 0.0)
    {
        _11313 = vec3(0.75, _14751, 0.0);
    }
    else
    {
        if (floor(_8302 * 6.0) == 1.0)
        {
            _11313 = vec3(_6700, 0.75, 0.0);
        }
        else
        {
            if (floor(_8302 * 6.0) == 2.0)
            {
                _11313 = vec3(0.0, 0.75, _14751);
            }
            else
            {
                if (floor(_8302 * 6.0) == 3.0)
                {
                    _11313 = vec3(0.0, _6700, 0.75);
                }
                else
                {
                    if (floor(_8302 * 6.0) == 4.0)
                    {
                        _11313 = vec3(_14751, 0.0, 0.75);
                    }
                    else
                    {
                        _11313 = vec3(0.75, 0.0, _6700);
                    }
                }
            }
        }
    }

    vec3 _14119 = returnColor3.xyz * mix(vec3(1.0), ambientTerm * 0.75, vec3(clamp(causticsDebrisTotal.w * 4.0, 0.0, 1.0) * inverseWaterFogAlpha));
    vec4 returnColor4 = returnColor3;
    returnColor4.x = _14119.x;
    returnColor4.y = _14119.y;
    returnColor4.z = _14119.z;
    vec3 _19800 = mix(returnColor4.xyz, mix(finalReflectionColor, finalReflectionColor * _11313, vec3(((clamp(noClue * 20.0, 0.0, 1.0) * _Globals_.g_flDebrisOilyness) / fma(scaledPixelRelativePos, 0.005, 1.0)) * clamp(fma(-refractedVerticalFactor, 5.0, 1.0), 0.0, 1.0))), vec3(clamp(reflectionModulation, 0.0, 1.0)));
    vec4 returnColor5 = returnColor4;
    returnColor5.x = _19800.x;
    returnColor5.y = _19800.y;
    returnColor5.z = _19800.z;
    vec4 returnColor6;
    if (_Globals_.g_bFogEnabled != 0)
    {
        vec3 _21493;
        vec3 _23187 = trueWorldPos - PerViewConstantBuffer_t.g_vCameraPositionWs.xyz;
        vec3 _9057 = _23187.xyz;
        vec3 fogAppliedRetColor;
        do
        {
            _21493 = _23187.xyz;
            bool _12888;
            if (dot(_21493, _21493) > PerViewConstantBufferCsgo_t.g_vGradientFogCullingParams.x)
            {
                _12888 = (trueWorldPos.z * PerViewConstantBufferCsgo_t.g_vGradientFogCullingParams.z) < PerViewConstantBufferCsgo_t.g_vGradientFogCullingParams.y;
            }
            else
            {
                _12888 = false;
            }
            if (_12888)
            {
                vec2 _6354 = clamp(fma(PerViewConstantBufferCsgo_t.g_vGradientFogBiasAndScale.zw, vec2(length(_21493), trueWorldPos.z), PerViewConstantBufferCsgo_t.g_vGradientFogBiasAndScale.xy), vec2(0.0), vec2(1.0));
                float _12872 = (pow(_6354.x, PerViewConstantBufferCsgo_t.m_vGradientFogExponents.x) * pow(_6354.y, PerViewConstantBufferCsgo_t.m_vGradientFogExponents.y)) * PerViewConstantBufferCsgo_t.g_vGradientFogColor_Opacity.w;
                fogAppliedRetColor = mix(returnColor5.xyz, vec4(PerViewConstantBufferCsgo_t.g_vGradientFogColor_Opacity.xyz, _12872).xyz, vec3(_12872));
                break;
            }
            fogAppliedRetColor = returnColor5.xyz;
            break;
        } while(false);
        vec4 _23944 = returnColor5;
        _23944.x = fogAppliedRetColor.x;
        _23944.y = fogAppliedRetColor.y;
        _23944.z = fogAppliedRetColor.z;
        vec3 _19341;
        do
        {
            bool _12889;
            if (dot(_9057, _9057) > PerViewConstantBufferCsgo_t.g_vCubeFogCullingParams_MaxOpacity.x)
            {
                _12889 = (PerViewConstantBufferCsgo_t.g_vCubeFogCullingParams_MaxOpacity.z * trueWorldPos.z) < PerViewConstantBufferCsgo_t.g_vCubeFogCullingParams_MaxOpacity.y;
            }
            else
            {
                _12889 = false;
            }
            if (_12889)
            {
                float _14602 = clamp(pow(max(0.0, fma(length(_21493), PerViewConstantBufferCsgo_t.g_vCubeFog_Offset_Scale_Bias_Exponent.y, PerViewConstantBufferCsgo_t.g_vCubeFog_Offset_Scale_Bias_Exponent.x)), PerViewConstantBufferCsgo_t.g_vCubeFog_Offset_Scale_Bias_Exponent.w), 0.0, 1.0) * clamp(pow(max(0.0, fma(trueWorldPos.z, PerViewConstantBufferCsgo_t.g_vCubeFog_Height_Offset_Scale_Exponent_Log2Mip.y, PerViewConstantBufferCsgo_t.g_vCubeFog_Height_Offset_Scale_Exponent_Log2Mip.x)), PerViewConstantBufferCsgo_t.g_vCubeFog_Height_Offset_Scale_Exponent_Log2Mip.z), 0.0, 1.0);
                float _8892 = clamp(_14602, 0.0, 1.0) * PerViewConstantBufferCsgo_t.g_vCubeFogCullingParams_MaxOpacity.w;
                _19341 = mix(_23944.xyz, vec4((textureLod(samplerCube(g_tFogCubeTexture, Filter_21_AllowGlobalMipBiasOverride_0_AddressU_2_AddressV_2), normalize((mat4(vec4(PerViewConstantBufferCsgo_t.g_matvCubeFogSkyWsToOs._m0[0].x, PerViewConstantBufferCsgo_t.g_matvCubeFogSkyWsToOs._m0[1].x, PerViewConstantBufferCsgo_t.g_matvCubeFogSkyWsToOs._m0[2].x, PerViewConstantBufferCsgo_t.g_matvCubeFogSkyWsToOs._m0[3].x), vec4(PerViewConstantBufferCsgo_t.g_matvCubeFogSkyWsToOs._m0[0].y, PerViewConstantBufferCsgo_t.g_matvCubeFogSkyWsToOs._m0[1].y, PerViewConstantBufferCsgo_t.g_matvCubeFogSkyWsToOs._m0[2].y, PerViewConstantBufferCsgo_t.g_matvCubeFogSkyWsToOs._m0[3].y), vec4(PerViewConstantBufferCsgo_t.g_matvCubeFogSkyWsToOs._m0[0].z, PerViewConstantBufferCsgo_t.g_matvCubeFogSkyWsToOs._m0[1].z, PerViewConstantBufferCsgo_t.g_matvCubeFogSkyWsToOs._m0[2].z, PerViewConstantBufferCsgo_t.g_matvCubeFogSkyWsToOs._m0[3].z), vec4(PerViewConstantBufferCsgo_t.g_matvCubeFogSkyWsToOs._m0[0].w, PerViewConstantBufferCsgo_t.g_matvCubeFogSkyWsToOs._m0[1].w, PerViewConstantBufferCsgo_t.g_matvCubeFogSkyWsToOs._m0[2].w, PerViewConstantBufferCsgo_t.g_matvCubeFogSkyWsToOs._m0[3].w)) * vec4(_9057, 0.0)).xyz).xyz, PerViewConstantBufferCsgo_t.g_vCubeFog_Height_Offset_Scale_Exponent_Log2Mip.w * clamp(fma(-_14602, PerViewConstantBufferCsgo_t.g_vCubeFog_Offset_Scale_Bias_Exponent.z, 1.0), 0.0, 1.0)) * PerViewConstantBufferCsgo_t.g_vCubeFog_ExposureBias.x).xyz, _8892).xyz, vec3(_8892));
                break;
            }
            _19341 = _23944.xyz;
            break;
        } while(false);

        _23944.x = _19341.x;
        _23944.y = _19341.y;
        _23944.z = _19341.z;
        returnColor6 = _23944;
    }
    else
    {
        returnColor6 = returnColor5;
    }
    vec4 returnColor7 = returnColor6;
    returnColor7.w = 1.0;
    if (!isSkybox)
    {
        vec2 _3206 = abs(vec2(0.5) - unbiasedUV) * 2.0;
        if ((clamp(1.0 - clamp((max(_3206.x, _3206.y) - (1.0 - _Globals_.g_flSkyBoxFadeRange)) / _Globals_.g_flSkyBoxFadeRange, 0.0, 1.0), 0.0, 1.0) - NoiseValue.x) < 0.0)
        {
            discard;
        }
    }
    vec4 returnColor8;
    if (!isSkybox)
    {
        returnColor8 = vec4(mix((refractionColorSample.xyz * mix(1.0, 0.6, clamp(refractedVerticalFactor * 60.0, 0.0, 1.0) / fma(scaledPixelRelativePos, 0.002, 1.0))).xyz, returnColor7.xyz, vec3(clamp(fma(_Globals_.g_flEdgeHardness, effectiveWaterDepthForFog, clamp(combinedfinalFoamIntensity, 0.0, 1.0)) + fma(debrisHeightVal, 2.0, -0.5), 0.0, 1.0))), 1.0);
    }
    else
    {
        returnColor8 = returnColor7;
    }
//    vec4 _6805;
//    if (one_minus_e_to_the_zeroth > 0.0)
//    {
//        vec4 _3401 = texelFetch(g_tMoitFinal, scaledFragCoord, 0);
//        vec3 _8598 = _3401.xyz * (one_minus_e_to_the_zeroth / (_3401.w + 9.9999997473787516355514526367188e-06));
//        vec4 _8677;
//        _8677.x = _8598.x;
//        _8677.y = _8598.y;
//        _8677.z = _8598.z;
//        vec3 _24094 = _8677.xyz + (returnColor8.xyz * e_to_the_zerothMoment);
//        vec4 _20494 = returnColor8;
//        _20494.x = _24094.x;
//        _20494.y = _24094.y;
//        _20494.z = _24094.z;
//        _6805 = _20494;
//    }
//    else
//    {
//        _6805 = returnColor8;
//    }
    //outputColor = _6805;
    //skip the check for now
    outputColor = returnColor8;
}


