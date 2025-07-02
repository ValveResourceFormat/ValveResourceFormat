#version 460 core
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

//--------------------------------------------------------------------------------------
// Helper Structs for Uniform Buffers
//--------------------------------------------------------------------------------------
struct Matrix3x4 { vec4 c0, c1, c2; };
struct Matrix4x4Array4 { mat4 matrices[4]; };

struct BarnLight {
    mat4 lightViewProjectionMatrix;
    mat4 lightWorldToShadowMatrix;
    vec4 lightPositionWs_Type;
    vec4 lightColor_Intensity;
    vec4 lightDirectionWs_ConeAngle;
    vec4 lightAttenuationParams;
    vec3 spotLightUpWs;
    int shadowMapArrayIndex;
    vec4 shadowBiasAndParams;
    vec4 cascadeSplitsOrExtraData1;
    vec4 extraData2;
    float effectRadius;
    float falloffExponent;
    uint lightFlags;
    uint lightCookieTextureIndexOrID;
    Matrix3x4 lightShapeTransform;
    vec4 barnDoorControls1;
    vec4 barnDoorControls2;
    vec4 barnDoorControls3;
    vec4 barnDoorControls4;
    vec4 lightIdentifierOrMoreData;
    vec3 additionalLightVectorData;
    float additionalLightFloatData;
};

//--------------------------------------------------------------------------------------
// Uniform Buffers
//--------------------------------------------------------------------------------------
layout(set = 0, binding = 0) uniform WaterGlobalParams { // ASSUMED BINDING 0
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
} WaterParams;

layout(set = 0, binding = 1) uniform PerViewCSGOParams { // ASSUMED BINDING 1
    ivec4 g_bOtherFxEnabled;
    ivec4 g_bOtherEnabled2;
    ivec4 g_bOtherEnabled3;
    ivec2 g_vBlueNoiseMask;
    mat4 g_matPrimaryViewWorldToProjection;
    vec4 g_vGradientFogBiasAndScale;
    vec4 m_vGradientFogExponents;
    vec4 g_vGradientFogColor_Opacity;
    vec4 g_vGradientFogCullingParams;
    vec4 g_vCubeFog_Offset_Scale_Bias_Exponent;
    vec4 g_vCubeFog_Height_Offset_Scale_Exponent_Log2Mip;
    mat4 g_matvCubeFogSkyWsToOs;
    vec4 g_vCubeFogCullingParams_MaxOpacity;
    vec4 g_vCubeFog_ExposureBias;
    vec4 g_vHighPrecisionLightingOffsetWs;
} CSGOViewParams;

layout(set = 0, binding = 2) uniform PerViewParams { // ASSUMED BINDING 2
    mat4 g_matWorldToProjection;
    mat4 g_matWorldToView;
    mat4 g_matViewToProjection;
    vec4 g_vInvProjRow3;
    vec3 g_vCameraPositionWs;
    float g_flViewportMinZ;
    vec3 g_vCameraDirWs;
    float g_flViewportMaxZ;
    vec3 g_vCameraUpDirWs;
    float g_flTime;
    vec3 g_vDepthPsToVsConversion;
    vec2 g_vInvViewportSize;
    vec2 g_vViewportToGBufferRatio;
    vec4 g_vInvGBufferSize;
    vec2 g_vViewportOffset;
    vec2 g_vViewportSize;
    vec4 g_vWorldToCameraOffset;
} ViewParams;


layout(std140) uniform PerViewLightingConstantBufferGpu_t {
    vec4 g_vShadow3x3PCFTermC0;                         // c0
    vec4 g_vShadow3x3PCFTermC1;                         // c1
    vec4 g_vShadow3x3PCFTermC2;                         // c2
    vec4 g_vShadow3x3PCFTermC3;                         // c3
    vec4 g_vLightmapParams;                             // c4
    vec4 g_vScreenSpaceDitherParams;                    // c5
    vec4 g_vCubemapNormalizationParams;                 // c6
    vec4 g_vAmbientLightingSH[3];                       // c7–c9
    vec4 g_vAmbientOcclusionProxyLightPositions[4];     // c10–c13
    vec4 g_vAmbientOcclusionProxyLightConeAngles;       // c14
    vec4 g_vAmbientOcclusionProxyLightStrengths;        // c15
    vec4 g_vAmbientOcclusionProxyAmbientStrength;       // c16
    vec4 g_vToolsAmbientLighting;                       // c17

    vec4 g_vFastPathSunLightDir;                        // c18
    vec4 g_vFastPathSunLightColor;                      // c19
    vec4 g_vFastPathSunLightBakedShadowMask;            // c20

    uvec4 g_vLightCullParams;                           // c21
    uvec4 g_vEnvMapCullParams;                          // c22
    uvec4 g_vLPVCullParams;                             // c23
    uvec4 g_vTileCullParams;                            // c24
    vec4 g_vDepthCullParams;                            // c25
    vec4 g_vVisibleEnvMapSizeConstants;                 // c26
    EnvMapData_t g_VisibleEnvMap[1152];                 // c27–c1178
    LPVData_t g_VisibleLPV[768];                        // c1179–c1946
    int g_nShadowCascadeCount;                          // c1947.x
    int g_nShadowCascadeRenderStaticObjects;            // c1947.y
    float g_flShadowCascadeReceiverDepthBias;           // c1947.z
    float g_flShadowCascadeReceiverDepthBiasTransmissiveBackface; // c1947.w
    vec4 g_vShadowCascadeSampleThreshold;               // c1948
    float g_flShadowCascadeSplitLerpFactorOffset;       // c1949.x
    float g_flShadowCascadeSplitLerpFactorScale;        // c1949.y
    float g_flShadowCascadeZLerpFactorOffset;           // c1949.z
    float g_flShadowCascadeZLerpFactorScale;            // c1949.w
    mat4 g_mWorldToShadowCascade[4];                    // c1950–c1965
    mat4 g_mShadowCascadeToWorld[4];                    // c1966–c1981
    vec4 g_vShadowCascadeAtlasOffset[4];                // c1982–c1985
    vec4 g_vShadowCascadePenumbra_DepthBias[4];         // c1986–c1989
    uint g_tShadowDepthBufferIndex;                     // c1990.x
    uint g_tLightCookieTextureIndex;                    // c1990.y
    uint g_tEnvironmentMapTextureIndex;                 // c1990.z
    uint g_tCachedShadowDepthBufferIndex;               // c1990.w
    uint g_nNumBarnLights;                              // c1991.x
    uint g_nNumEnvMaps;                                 // c1991.y
    uint g_nNumLPVs;                                    // c1991.z
    uint g_padding;                                     // c1991.w
    uvec4 g_nLightPropertyMasks[10];                    // c1992–c2001
    vec4 g_vShadowSampleRotations[16];                  // c2002–c2017
};



// !! VERIFY THIS UBO STRUCTURE AND BINDING !!
layout(set = 1, binding = 0) uniform PerMaterialParams { // ASSUMED BINDING 0
    Matrix3x4 transformMatrix;        // _m0
    vec4 colorAlbedo;                 // _m1: (Main Light Dir / Caustic Basis / Ambient Color)
    vec4 PBRParams;                   // _m2: (Caustic Basis if Triplanar)
    vec4 emissiveColor;               // _m3: (Main Light Color / Caustic Tint)
    uvec4 flagsAndIndices1;           // _m4: (Barn Light Grid Params)
    uvec4 flagsAndIndices2;           // _m5: (Barn Light Grid Params)
    vec4 customMaterialParam1;        // _m6: (Barn Light Depth Slice / Shadow Cascade Extents - needs verification)
    int materialTypeID;               // _m7: (Shadow Cascade Count)
    float alphaTestThreshold;         // _m8: (Main Shadow Bias)
    // Original _m9 (cascade extents) is now likely part of customMaterialParam1 or another member if it was an array.
    // For this cleanup, assuming customMaterialParam1.x/y/z are cascade extents if materialTypeID <=3.
    float customMaterialFloat1;       // _m10: (Shadow Cascade Fade Bias)
    float customMaterialFloat2;       // _m11: (Shadow Cascade Fade Scale)
    float customMaterialFloat3;       // _m12: (Shadow Distance Fade Bias)
    float customMaterialFloat4;       // _m13: (Shadow Distance Fade Scale)
    Matrix4x4Array4 skinningMatricesOrLayers; // _m14: (Shadow Cascade Matrices)
    Matrix3x4 secondaryTransformMatrix; // _m15: (Shadow Cascade UV Scale/Offset)
} MaterialParams;

//--------------------------------------------------------------------------------------
// Shader Storage Buffers (SSBOs)
//--------------------------------------------------------------------------------------
layout(set = 1, binding = 30, std430) readonly buffer CullBitsBuffer { uint g_CullBits[]; } cullBitsBuffer;
layout(set = 1, binding = 31, std430) readonly buffer BarnLightsBuffer { BarnLight g_BarnLights[]; } barnLightsBuffer;

//--------------------------------------------------------------------------------------
// Textures & Samplers
//--------------------------------------------------------------------------------------
layout(set = 0, binding = 116) uniform texture2D g_tSceneDepth;
layout(set = 0, binding = 115) uniform texture2D g_tRefractionMap;
layout(set = 0, binding = 117) uniform texture2D g_tZerothMoment;
layout(set = 0, binding = 118) uniform texture2D g_tMoitFinal;
layout(set = 0, binding = 114) uniform texture2D g_tWavesNormalHeight;
layout(set = 0, binding = 113) uniform texture2D g_tFoam;
layout(set = 0, binding = 111) uniform texture2D g_tDebris;
layout(set = 0, binding = 112) uniform texture2D g_tDebrisNormal;
layout(set = 0, binding = 119) uniform texture2D g_tWaterEffectsMap;
layout(set = 0, binding = 96) uniform texture2D g_tShadowDepthBufferDepth;
layout(set = 0, binding = 107) uniform texture2D g_tParticleShadowBuffer;
layout(set = 0, binding = 94) uniform texture3D g_tLightCookieTexture;
layout(set = 0, binding = 110) uniform textureCube g_tLowEndCubeMap;
layout(set = 0, binding = 102) uniform textureCube g_tFogCubeTexture;
layout(set = 0, binding = 90) uniform texture2D g_tBlueNoise;

layout(set = 0, binding = 47) uniform sampler s_SamplerRepeatLinear;
layout(set = 0, binding = 46) uniform sampler s_SamplerRepeatAniso;
layout(set = 0, binding = 48) uniform sampler s_SamplerDynamicAniso;
layout(set = 0, binding = 56) uniform sampler s_DefaultSampler;
layout(set = 0, binding = 57) uniform sampler s_DefaultSampler_variant2;
layout(set = 0, binding = 55) uniform sampler s_DefaultSampler_variant1;
layout(set = 0, binding = 51) uniform sampler s_SamplerClampBorderBlack;
layout(set = 0, binding = 45) uniform sampler s_SamplerClampLinear;
layout(set = 0, binding = 52) uniform samplerShadow s_ShadowSamplerComparison;

//--------------------------------------------------------------------------------------
// Fragment Shader Inputs
//--------------------------------------------------------------------------------------
layout(location = 0) flat in uint fsInput_instanceId;
layout(location = 1) in float fsInput_animationTime; // Was fsInput_unknownFloat
layout(location = 2) in vec4 fsInput_vertexPaintBlendFactors;
layout(location = 3) in vec3 fsInput_worldPositionCameraRelative;
layout(location = 4) in vec3 fsInput_normalWs;

//--------------------------------------------------------------------------------------
// Fragment Shader Output
//--------------------------------------------------------------------------------------
layout(location = 0) out vec4 outFragColor;

//--------------------------------------------------------------------------------------
// Main Fragment Shader Function
//--------------------------------------------------------------------------------------
void main()
{
    vec4 fragCoord = gl_FragCoord;

    

    // --- Normal Preparation ---
    bool shouldFlipNormal = false;
    if (WaterParams.g_bRenderBackfaceNormals != 0) {
        shouldFlipNormal = !(WaterParams.g_bDontFlipBackfaceNormals != 0);
    }
    vec3 geometricNormalWs = fsInput_normalWs;
    if (shouldFlipNormal) {
        geometricNormalWs = geometricNormalWs * (gl_FrontFacing ? 1.0 : -1.0);
    }
    geometricNormalWs = normalize(geometricNormalWs);

    // --- Position & View Vectors ---
    
    vec3 worldPositionAbs = fsInput_worldPositionCameraRelative + CSGOViewParams.g_vHighPrecisionLightingOffsetWs.xyz;
    //NO fucking clue what Gemini is thinking with these two here
    vec3 viewVectorWs = worldPositionAbs - ViewParams.g_vCameraPositionWs;
    vec3 toCameraVectorWs = -normalize(viewVectorWs);
    
    float distanceToFragment = length(viewVectorWs);
    vec2 gbufferUV = fragCoord.xy * ViewParams.g_vInvGBufferSize.xy;

    // --- Early Discard (OIT Occlusion) ---
    ivec2 momentTexelCoords = ivec2(fragCoord.xy * WaterParams.g_flForceMixResolutionScale);
    float visibilityFromMoment = exp(-texelFetch(g_tZerothMoment, momentTexelCoords, 0).x);
    float occlusionFactor = 1.0 - visibilityFromMoment;
    if (occlusionFactor > 0.9998999834060669) { discard; }

    // --- Skybox Scale Effect & Blue Noise ---
    bool isSkyboxScaleEffectEnabled = (CSGOViewParams.g_bOtherEnabled3.x != 0);
    float effectiveSkyboxScale = isSkyboxScaleEffectEnabled ? WaterParams.g_flSkyBoxScale : 1.0;
    vec4 blueNoiseSample = texelFetch(g_tBlueNoise, ivec2(fragCoord.xy) & CSGOViewParams.g_vBlueNoiseMask, 0);
    vec2 blueNoiseOffset = blueNoiseSample.xy - 0.5;
    float ditherFactor = (blueNoiseSample.x - 0.5) * 2.0;

    // --- Sky-Related Projection & Base Screen UV ---
    vec3 skyRelatedProjectionVector = mix(
        vec3(toCameraVectorWs.xy / max(0.001, toCameraVectorWs.z), sqrt(max(0.001, toCameraVectorWs.z))),
        vec3(0.0), bvec3(isSkyboxScaleEffectEnabled));
    vec2 baseScreenProjectedUV = ((worldPositionAbs * effectiveSkyboxScale) +
                                 (skyRelatedProjectionVector * (0.5 - WaterParams.g_flWaterPlaneOffset))).xy * (1.0 / 30.0);

    // --- Reflection LOD & Water Effects Map ---
    vec2 dUVdx_for_lod = dFdx(baseScreenProjectedUV);
    vec2 dUVdy_for_lod = dFdy(baseScreenProjectedUV);
    float reflectionLODFactor = clamp((0.5 * pow(max(dot(dUVdx_for_lod, dUVdx_for_lod), dot(dUVdy_for_lod, dUVdy_for_lod)), 0.1)) * WaterParams.g_flReflectionDistanceEffect, 0.0, 0.5);
    vec2 viewportUV_for_effects = (fragCoord.xy - ViewParams.g_vViewportOffset.xy) * ViewParams.g_vInvViewportSize.xy;
    vec2 waterEffectsMapUV = viewportUV_for_effects * ViewParams.g_vViewportToGBufferRatio.xy;
    vec4 waterEffectsSampleRaw = texture(sampler2D(g_tWaterEffectsMap, s_SamplerRepeatAniso), waterEffectsMapUV);


    vec2 waterDisturbanceXY_from_map = clamp((waterEffectsSampleRaw.yz - 0.5) * 2.0, 0.0, 1.0);
    float foamFromEffects_map = waterDisturbanceXY_from_map.y;
    float totalDisturbanceStrength = (waterDisturbanceXY_from_map.x + foamFromEffects_map) * WaterParams.g_flWaterEffectDisturbanceStrength;
    float disturbanceWeightedFoamAmount = totalDisturbanceStrength * 0.25;

    // --- Current Water Roughness ---
    float currentWaterRoughness = isSkyboxScaleEffectEnabled ? WaterParams.g_flWaterRoughnessMax :
                                  max(0.0, mix(WaterParams.g_flWaterRoughnessMin, WaterParams.g_flWaterRoughnessMax, fsInput_vertexPaintBlendFactors.x));
    vec2 projectedToCameraScreenXY = ((-toCameraVectorWs.xy) / max(0.001, (toCameraVectorWs.z + 0.25)));

    // --- Scene Depth & Initial Refraction Data (if not skybox) ---
    float sceneNormalizedDepth = 1.0;
    vec3 sceneHitPositionWs = vec3(0.0);
    vec4 refractionColorSample = vec4(0.0);
    float waterColumnOpticalDepthFactor = 1.0;
    float refractionDistortionFactor = 0.0;
    float sceneViewZLinear = -ViewParams.g_flViewportMaxZ;

    if (!isSkyboxScaleEffectEnabled) {
        float rawSceneDepth = textureLod(sampler2D(g_tSceneDepth, s_SamplerRepeatLinear), gbufferUV, 0.0).x;
        sceneNormalizedDepth = clamp((rawSceneDepth - ViewParams.g_flViewportMinZ) / (ViewParams.g_flViewportMaxZ - ViewParams.g_flViewportMinZ), 0.0, 1.0);
        refractionColorSample = texture(sampler2D(g_tRefractionMap, s_SamplerRepeatAniso), gbufferUV);
        float refractionLuminance = dot(refractionColorSample.rgb, vec3(0.2125, 0.7154, 0.0721));
        refractionDistortionFactor = clamp(refractionLuminance, 0.0, 0.4) * -0.03;

        // g_vDepthPsToVsConversion: x = near plane, y is weirdChamp, z = offset?
        sceneViewZLinear = -(ViewParams.g_vDepthPsToVsConversion.x / (ViewParams.g_vDepthPsToVsConversion.y * sceneNormalizedDepth + ViewParams.g_vDepthPsToVsConversion.z));
        sceneHitPositionWs = ViewParams.g_vCameraPositionWs + normalize(viewVectorWs) * sceneViewZLinear;
        float waterSurfaceViewZ = -(ViewParams.g_matWorldToView * vec4(worldPositionAbs, 1.0)).z;
        waterColumnOpticalDepthFactor = (refractionDistortionFactor * 1.0 + max(sceneViewZLinear - waterSurfaceViewZ, 0.0) * 0.01);
    }
    float adjustedWaterColumnDepth = max(0.0, waterColumnOpticalDepthFactor - 0.02);
    vec2 depthFactorFine = vec2(clamp(adjustedWaterColumnDepth * 10.0, 0.0, 1.0));
    vec2 depthFactorCoarse = vec2(clamp(adjustedWaterColumnDepth * 4.0, 0.0, 1.0));
    float sceneDepthWidth = fwidth(sceneNormalizedDepth);

    // === Procedural Wave Generation ===
    vec2 accumulatedScreenSpaceOffset = vec2(0.0);
    vec2 currentWaveOctaveScale = WaterParams.g_vWaveScale;
    vec3 accumulatedWaveNormal = vec3(0.0, 0.0, 1.0);
    float accumulatedWaveHeightOffset = 0.0;
    vec2 accumulatedPhaseOffset = vec2(0.0);
    float currentWaveAngle = WaterParams.g_flWaterInitialDirection;
    SPIRV_CROSS_LOOP
    for (uint i = 0u; i < WaterParams.g_nWaveIterations; ++i) {
        float iterationProgress = 0.0; if (WaterParams.g_nWaveIterations > 1u) iterationProgress = float(i) / (float(WaterParams.g_nWaveIterations - 1u));
        float lowMedBlend = clamp(iterationProgress * 2.0, 0.0, 1.0);
        float medHighBlend = clamp((iterationProgress * 2.0 - 1.0), 0.0, 1.0);
        float lowMedWeightedAmplitude = mix(
        (totalDisturbanceStrength * 0.05 + WaterParams.g_flLowFreqWeight),
        (totalDisturbanceStrength * 0.25 + WaterParams.g_flMedFreqWeight),
        lowMedBlend);

        float freqWeight = mix(
        lowMedWeightedAmplitude,
        (WaterParams.g_flHighFreqWeight * currentWaterRoughness + disturbanceWeightedFoamAmount), medHighBlend);

        vec2 waveAnimationOffset = vec2(sin(currentWaveAngle), cos(currentWaveAngle)) * ((fsInput_animationTime * WaterParams.g_flWavesSpeed) * 0.5);

        vec2 anisotropicUvScale = sqrt(vec2(1.0) / max(currentWaveOctaveScale, vec2(0.001)));
        vec2 waveOctaveUV = (waveAnimationOffset * anisotropicUvScale + ((baseScreenProjectedUV + (accumulatedScreenSpaceOffset * 3.0)) + accumulatedPhaseOffset) / max(currentWaveOctaveScale, vec2(0.001)));


        vec3 waveSample = texture(sampler2D(g_tWavesNormalHeight, s_DefaultSampler_variant2), waveOctaveUV, -1.0 * reflectionLODFactor).xyz - vec3(0.5);

        float waveHeightComponent = (waveSample.z * freqWeight) * length(currentWaveOctaveScale);
        vec2 waveNormalXY = waveSample.xy * 2.0;
        vec2 scaledWaveNormalXY = vec2(waveNormalXY.x * min(1.0, currentWaveOctaveScale.y / max(0.001, currentWaveOctaveScale.x)), waveNormalXY.y * min(1.0, currentWaveOctaveScale.x / max(0.001, currentWaveOctaveScale.y))) * (freqWeight * 0.1);
        accumulatedScreenSpaceOffset += (((-projectedToCameraScreenXY) * (waveHeightComponent * 0.01)) * WaterParams.g_flWavesHeightOffset) * currentWaterRoughness;
        accumulatedPhaseOffset += (((scaledWaveNormalXY * WaterParams.g_flWavesSharpness) * currentWaveOctaveScale) * WaterParams.g_flWavesPhaseOffset);
        accumulatedWaveHeightOffset = (waveHeightComponent * 0.01 + accumulatedWaveHeightOffset);
        accumulatedWaveNormal.xy += scaledWaveNormalXY;
        currentWaveOctaveScale *= WaterParams.g_flWavesPhaseOffset;
        currentWaveAngle += (3.5 / float(i + 1u));
    }
    vec2 finalWavePhaseOffset = mix(vec2(0.0), accumulatedPhaseOffset, vec2(0.1));
    vec3 finalWaveNormalFromProc = accumulatedWaveNormal;

    //HALLUCINATION? what the fuck is that 0.0001
    finalWaveNormalFromProc.z = sqrt(max(0.0001, 1.0 - dot(finalWaveNormalFromProc.xy, finalWaveNormalFromProc.xy)));
    finalWaveNormalFromProc = normalize(finalWaveNormalFromProc + vec3(0, 0, isSkyboxScaleEffectEnabled ? 0.0 : 0.001));
    float totalWaveHeightDisplacement = (accumulatedWaveHeightOffset * currentWaterRoughness) * 60.0;

    // --- Debris and Foam Amounts ---
    float debrisAmount = isSkyboxScaleEffectEnabled ? 0.0 : max(0.0, mix(WaterParams.g_flDebrisMin, WaterParams.g_flDebrisMax, fsInput_vertexPaintBlendFactors.z));
    float foamAmount = isSkyboxScaleEffectEnabled ? 0.0 : max(0.0, mix(WaterParams.g_flFoamMin, WaterParams.g_flFoamMax, fsInput_vertexPaintBlendFactors.y));

    // --- Shoreline and Refracted Scene Normal ---
    vec3 refractedGeometricNormalWs = -normalize(cross(dFdx(sceneHitPositionWs), dFdy(sceneHitPositionWs)));
    if (!isSkyboxScaleEffectEnabled) {
        vec3 perturbedScenePos = sceneHitPositionWs + (skyRelatedProjectionVector * clamp(dot(refractionColorSample.rgb, vec3(0.2125,0.7154,0.0721)),0.0,0.4));
        refractedGeometricNormalWs = -normalize(cross(dFdx(perturbedScenePos), dFdy(perturbedScenePos)));
    }
    vec2 ditheredRefractedNormalXY = refractedGeometricNormalWs.xy + (blueNoiseOffset * 0.05);

    // --- Iterative Effects Variables ---
    vec3 worldPosForSamplingEffects = worldPositionAbs;
    float finalFoamHeightContrib = totalWaveHeightDisplacement;
    vec2 foamEffectDisplacementUV = vec2(0.0);
    float foamSiltFactor = 0.0;
    vec2 debrisEffectNormalXY = vec2(0.0);
    float debrisFoamFromEffects = 0.0;
    float debrisDisturbanceForWaves = disturbanceWeightedFoamAmount;
    vec4 foamAndSiltEffectValues = vec4(0.0, 0.0, foamFromEffects_map, 0.0);
    vec2 foamSiltEffectNormalXY = vec2(0.0);
    float edgeShapeFactor = WaterParams.g_flEdgeShapeEffect;

    if (!isSkyboxScaleEffectEnabled) {
        vec3 procWaveNormalScaled = finalWaveNormalFromProc * currentWaterRoughness;
        procWaveNormalScaled.z = sqrt(max(0.0001, 1.0 - dot(procWaveNormalScaled.xy, procWaveNormalScaled.xy)));

        vec3 initialEffectSamplePosWs = (worldPositionAbs + (skyRelatedProjectionVector * (mix(0.0, totalWaveHeightDisplacement, edgeShapeFactor) - WaterParams.g_flWaterPlaneOffset))) + (vec3(procWaveNormalScaled.xy, 0.0) * -16.0);
        mat4 worldToProjMatrix = ViewParams.g_matWorldToProjection;
        vec4 effectSampleClipPos = (vec4(initialEffectSamplePosWs,1.0)+ViewParams.g_vWorldToCameraOffset)*worldToProjMatrix;
        vec2 effectSampleNdc = effectSampleClipPos.xy/effectSampleClipPos.w;
        vec2 effectGbufferUV_Iter1 = ((vec2(effectSampleNdc.x,-effectSampleNdc.y)*0.5)+0.5)*ViewParams.g_vViewportToGBufferRatio.xy;
        vec4 waterEffectsSample1 = texture(sampler2D(g_tWaterEffectsMap,s_SamplerRepeatAniso),effectGbufferUV_Iter1)-0.5;
        vec2 effectsDisturb1_clamped = clamp(waterEffectsSample1.yz*2.0,0.0,1.0);
        vec3 secondEffectSamplePosWs = initialEffectSamplePosWs+(skyRelatedProjectionVector*(20.0*waterEffectsSample1.x+2.0*effectsDisturb1_clamped.x));
        vec4 effectSampleClipPos2 = (vec4(secondEffectSamplePosWs,1.0)+ViewParams.g_vWorldToCameraOffset)*worldToProjMatrix;
        vec2 effectSampleNdc2 = effectSampleClipPos2.xy/effectSampleClipPos2.w;
        vec2 finalEffectGbufferUV = ((vec2(effectSampleNdc2.x,-effectSampleNdc2.y)*0.5)+0.5)*ViewParams.g_vViewportToGBufferRatio.xy;
        vec4 waterEffectsFinalSample = texture(sampler2D(g_tWaterEffectsMap,s_SamplerRepeatAniso),finalEffectGbufferUV)-0.5;

        vec2 rippleFoamFromEffectsMap = clamp(waterEffectsFinalSample.yz*2.0,0.0,1.0);
        float rippleBaseFromEffectsMap = rippleFoamFromEffectsMap.x;
        float foamBaseFromEffectsMap = rippleFoamFromEffectsMap.y;

        foamAndSiltEffectValues.z = foamBaseFromEffectsMap;

        vec4 offsetClipPosX = (vec4(secondEffectSamplePosWs+vec3(1,0,0),1.0)+ViewParams.g_vWorldToCameraOffset)*worldToProjMatrix;
        vec2 offsetNdcX = offsetClipPosX.xy/offsetClipPosX.w;
        vec2 duv_dx_approx = (((vec2(offsetNdcX.x,-offsetNdcX.y)*0.5)+0.5)*ViewParams.g_vViewportToGBufferRatio.xy)-finalEffectGbufferUV;
        vec4 offsetClipPosY = (vec4(secondEffectSamplePosWs+vec3(0,-1,0),1.0)+ViewParams.g_vWorldToCameraOffset)*worldToProjMatrix;
        vec2 offsetNdcY = offsetClipPosY.xy/offsetClipPosY.w;
        vec2 duv_dy_approx = (((vec2(offsetNdcY.x,-offsetNdcY.y)*0.5)+0.5)*ViewParams.g_vViewportToGBufferRatio.xy)-finalEffectGbufferUV;
        vec2 stepScale = vec2(0.0004)/vec2(length(duv_dx_approx)+1e-5,length(duv_dy_approx)+1e-5);
        vec4 waterEffectsSample_plusDX = texture(sampler2D(g_tWaterEffectsMap,s_SamplerRepeatAniso),finalEffectGbufferUV+normalize(duv_dx_approx)*0.005)-0.5;
        vec2 rippleFoam_plusDX = clamp(waterEffectsSample_plusDX.yz*2.0,0.0,1.0);
        vec4 waterEffectsSample_plusDY = texture(sampler2D(g_tWaterEffectsMap,s_SamplerRepeatAniso),finalEffectGbufferUV+normalize(duv_dy_approx)*0.005)-0.5;
        float effectRipple_Center = waterEffectsFinalSample.x;

        foamEffectDisplacementUV = (normalize(cross(vec3(stepScale.x,0,effectRipple_Center-waterEffectsSample_plusDX.x),vec3(0,stepScale.y,effectRipple_Center-waterEffectsSample_plusDY.x))).xy*vec2(-1,1))*(abs(effectRipple_Center)*4.0)*WaterParams.g_flWaterEffectsRippleStrength;
        finalFoamHeightContrib = (effectRipple_Center*WaterParams.g_flWaterEffectsRippleStrength*12.0+totalWaveHeightDisplacement);
        foamSiltFactor = foamBaseFromEffectsMap*WaterParams.g_flWaterEffectSiltStrength;

        debrisEffectNormalXY = normalize(cross(vec3(stepScale.x,0,rippleBaseFromEffectsMap-rippleFoam_plusDX.x),vec3(0,stepScale.y,rippleBaseFromEffectsMap-clamp(waterEffectsSample_plusDY.yz*2.0,0.0,1.0).x))).xy*vec2(-1,1); // Original used rippleFoam_plusDY.x
        debrisFoamFromEffects = rippleBaseFromEffectsMap*WaterParams.g_flWaterEffectFoamStrength;
        debrisDisturbanceForWaves = ((rippleBaseFromEffectsMap+foamBaseFromEffectsMap)*WaterParams.g_flWaterEffectDisturbanceStrength)*0.25;
        foamSiltEffectNormalXY = (normalize(cross(vec3(stepScale.x,0,foamBaseFromEffectsMap-rippleFoam_plusDX.y),vec3(0,stepScale.y,foamBaseFromEffectsMap-clamp(waterEffectsSample_plusDY.yz*2.0,0.0,1.0).y))).xy*vec2(-1,1))*pow(foamBaseFromEffectsMap,3.5); // Original used rippleFoam_plusDY.y
        worldPosForSamplingEffects = (worldPositionAbs+(skyRelatedProjectionVector*(mix(0.5,finalFoamHeightContrib,edgeShapeFactor)-WaterParams.g_flWaterPlaneOffset)))+(vec3(foamEffectDisplacementUV,0.0)*-4.0);
        float depthScaledViewZ = waterColumnOpticalDepthFactor * toCameraVectorWs.z;
        float edgeFalloff = 1.0 - clamp(depthScaledViewZ * 8.0, 0.0, 1.0);
        edgeShapeFactor *= clamp(((-refractedGeometricNormalWs.z * edgeFalloff) + 1.2), 0.0, 1.0);
    }

    vec3 rippleDisplacementAsVec3 = vec3(foamEffectDisplacementUV, 0.0);
    vec3 worldPosForFoamAndDebrisBase = (worldPositionAbs + (skyRelatedProjectionVector*(mix(0.5,finalFoamHeightContrib,edgeShapeFactor*0.5)-WaterParams.g_flWaterPlaneOffset))) + (rippleDisplacementAsVec3*-2.0);

    // --- Foam Texturing ---
    vec2 foamAnimDir = vec2(sin((worldPosForFoamAndDebrisBase.y*0.07+fsInput_animationTime)),cos((worldPosForFoamAndDebrisBase.x*0.07+fsInput_animationTime)));
    vec2 foamBaseUV = worldPosForFoamAndDebrisBase.xy/WaterParams.g_flFoamScale;
    vec2 foamWobbleOffset = ((finalWavePhaseOffset*WaterParams.g_flFoamWobble)*0.5)*(1.0-foamAmount);
    vec2 foamEffectNormalDisp = foamSiltEffectNormalXY/WaterParams.g_flFoamScale;
    vec2 foamDistortedUV = (foamBaseUV+foamWobbleOffset)-foamEffectNormalDisp;
    float foamNoiseStrength = 0.05+foamAndSiltEffectValues.z;
    vec2 foamFinalUV1 = mix(foamBaseUV,foamDistortedUV+((foamAnimDir*foamNoiseStrength)*0.03),depthFactorFine);
    vec4 foamSample1 = texture(sampler2D(g_tFoam,s_SamplerDynamicAniso),foamFinalUV1);
    vec2 foamAnimDir2 = vec2(sin((worldPosForFoamAndDebrisBase.y*0.06+fsInput_animationTime)),cos((worldPosForFoamAndDebrisBase.x*0.06+fsInput_animationTime)));
    vec2 foamFinalUV2 = mix(foamBaseUV.yx*0.731,(foamDistortedUV.yx*0.731)+((foamAnimDir2*foamNoiseStrength)*0.02),depthFactorFine);
    vec4 foamSample2 = texture(sampler2D(g_tFoam,s_SamplerDynamicAniso),foamFinalUV2);
    float combinedFoamTextureValue = (sin(blueNoiseSample.x)*0.125 + max(foamSample1.z,foamSample2.z));
    float foamAlpha = clamp(((foamAmount * (finalFoamHeightContrib * 0.008 + 1.0)) * (1.0 - clamp(debrisDisturbanceForWaves * 2.0, 0.0, 1.0))) + debrisFoamFromEffects, 0.0, 1.0);

    // --- Debris Texturing ---
    float debrisWobblePowerFromEffects = pow(foamAndSiltEffectValues.z,1.5);
    vec2 debrisBaseUV = worldPosForFoamAndDebrisBase.xy/WaterParams.g_flDebrisScale;
    vec2 debrisWobbleOffset = finalWavePhaseOffset*WaterParams.g_flDebrisWobble;
    float fsx=foamSiltEffectNormalXY.x; float fsy=foamSiltEffectNormalXY.y; float absfsx=abs(fsx); float absfsy=abs(fsy);
    vec2 dominantFoamSiltNorm = vec2(fsx*float(absfsx>absfsy),fsy*float(absfsy>absfsx));
    vec2 debrisEffectNormalDisp = (dominantFoamSiltNorm/WaterParams.g_flDebrisScale)*400.0;
    vec2 debrisDistortionTerm = (projectedToCameraScreenXY * ((sin(foamAndSiltEffectValues.z*50.0)*4.0 * clamp(0.1-debrisWobblePowerFromEffects,0.0,1.0) + 1.0)*debrisWobblePowerFromEffects*0.1));
    vec2 debrisAnimatedNoiseOff = (foamAnimDir*(0.1+foamAndSiltEffectValues.z))*0.02;
    vec2 debrisDistortedUV = (((debrisBaseUV+(debrisWobbleOffset*(1.0-debrisAmount)))+debrisDistortionTerm)+debrisAnimatedNoiseOff)-debrisEffectNormalDisp;
    vec2 debrisFinalUV = mix(debrisBaseUV,debrisDistortedUV,depthFactorCoarse);
    vec4 debrisTexSample = texture(sampler2D(g_tDebris,s_DefaultSampler),debrisFinalUV,debrisWobblePowerFromEffects*3.0);
    float debrisCoverageFactor = debrisTexSample.w-0.5;
    float debrisVisReduction = clamp(1.4-(foamAndSiltEffectValues.z/mix(1.0,0.4,debrisTexSample.w)),0.0,1.0);
    float debrisAlphaEdge = clamp((debrisTexSample.w-(debrisAmount*debrisVisReduction))*WaterParams.g_flDebrisEdgeSharpness,0.0,1.0);
    float debrisDepthFade = max(0.0,(2.0*debrisWobblePowerFromEffects + debrisCoverageFactor*-2.0));
    float finalDebrisAlpha = clamp((-debrisDepthFade*10.0+1.0),0.0,1.0)*debrisAlphaEdge;
    vec3 debrisNormalTex = texture(sampler2D(g_tDebrisNormal,s_SamplerDynamicAniso),debrisFinalUV).xyz-0.5;
    debrisNormalTex.y *= -1.0;
    vec2 debrisNormalFromTexXY = debrisNormalTex.xy*WaterParams.g_flDebrisNormalStrength;

    // --- Combine Foam & Debris Alpha ---
    float combinedFoamAlpha = clamp((foamAlpha*combinedFoamTextureValue*0.25 + clamp(foamAlpha-(1.0-combinedFoamTextureValue),0.0,1.0)*0.75),0.0,1.0);
    float finalCombinedFoamDebrisAlpha = clamp(((-finalDebrisAlpha * combinedFoamAlpha) + combinedFoamAlpha), 0.0, 1.0);
    finalCombinedFoamDebrisAlpha = clamp(finalCombinedFoamDebrisAlpha + finalDebrisAlpha, 0.0, 1.0);

    // --- Refraction & Caustics ---
    vec3 refractedSceneColor = vec3(0.0);
    float finalWaterColumnDepthForRefract = waterColumnOpticalDepthFactor;
    float waterOpticalDepthForTrueFog = WaterParams.g_flWaterFogStrength;
    float siltFromEffects = 0.0;

    if (!isSkyboxScaleEffectEnabled) {
        float heightWithDebris = mix(finalFoamHeightContrib, (finalFoamHeightContrib*0.5+debrisCoverageFactor*2.0), finalDebrisAlpha);
        vec3 worldPosForRefractionOrigin=(worldPositionAbs+(skyRelatedProjectionVector*(mix(0.5,heightWithDebris,edgeShapeFactor)-WaterParams.g_flWaterPlaneOffset)))+(rippleDisplacementAsVec3*-12.0);
        float waterSurfaceViewZ_refract=-(ViewParams.g_matWorldToView*vec4(worldPosForRefractionOrigin,1.0)).z;
        finalWaterColumnDepthForRefract = (max(sceneViewZLinear-waterSurfaceViewZ_refract,0.0)*0.01 + refractionDistortionFactor);

        vec3 RwaveN = finalWaveNormalFromProc * currentWaterRoughness; RwaveN.z = sqrt(max(0.0001,1.0-dot(RwaveN.xy,RwaveN.xy)));
        vec2 refractionUVOffsetRaw = (vec2(dot(RwaveN.xy,cross(toCameraVectorWs,vec3(0,0,-1)).xy),dot(RwaveN.xy,toCameraVectorWs.xy))+(blueNoiseOffset*0.002)*WaterParams.g_flWaterFogStrength)*min(WaterParams.g_flRefractionLimit,finalWaterColumnDepthForRefract);
        float sceneDepthAtOffsetUV_raw = textureLod(sampler2D(g_tSceneDepth,s_SamplerRepeatLinear),gbufferUV+refractionUVOffsetRaw,0.0).x;
        float sceneNormalizedDepthAtOffset = clamp((sceneDepthAtOffsetUV_raw-ViewParams.g_flViewportMinZ)/(ViewParams.g_flViewportMaxZ-ViewParams.g_flViewportMinZ),0.0,1.0);
        float sceneViewZAtOffset_refract = -(ViewParams.g_vDepthPsToVsConversion.x/(ViewParams.g_vDepthPsToVsConversion.y*sceneNormalizedDepthAtOffset+ViewParams.g_vDepthPsToVsConversion.z));
        float refractionOffsetAttenuation = clamp((max(sceneViewZAtOffset_refract-waterSurfaceViewZ_refract,0.0)*0.01+refractionDistortionFactor)*10.0,0.0,1.0);
        vec2 finalRefractionUVOffset = refractionUVOffsetRaw*refractionOffsetAttenuation;
        vec2 refractedLookupUV = clamp(gbufferUV+finalRefractionUVOffset,WaterParams.g_vViewportExtentsTs.xy,WaterParams.g_vViewportExtentsTs.zw);
        vec4 rawRefractedColor = texture(sampler2D(g_tRefractionMap,s_SamplerRepeatAniso),refractedLookupUV);
        refractedSceneColor = pow(rawRefractedColor.rgb,vec3(1.1))*WaterParams.g_flUnderwaterDarkening;
        waterOpticalDepthForTrueFog = (foamSiltFactor*2.0+WaterParams.g_flWaterFogStrength);
        siltFromEffects = foamAndSiltEffectValues.z;

        float refractedLuminance = dot(refractedSceneColor,vec3(0.2125,0.7154,0.0721));
        float causticVisibility = clamp((refractedLuminance-WaterParams.g_flCausticShadowCutOff)*(2.0+WaterParams.g_flCausticShadowCutOff),0.0,1.0);
        if (causticVisibility > 0.0) {
            vec3 refractedViewDir=-normalize((toCameraVectorWs+((ViewParams.g_vCameraUpDirWs*finalRefractionUVOffset.y)*2.0))+((cross(ViewParams.g_vCameraDirWs,ViewParams.g_vCameraUpDirWs)*(-finalRefractionUVOffset.x))*2.0));
            float invWClipRefracted=(ViewParams.g_vInvProjRow3.z*sceneNormalizedDepthAtOffset+ViewParams.g_vInvProjRow3.w);
            vec3 refractedSceneHitPosWs=ViewParams.g_vCameraPositionWs+(refractedViewDir*(1.0/(invWClipRefracted*dot(ViewParams.g_vCameraDirWs,refractedViewDir))));
            vec3 causticProjectionBasis=MaterialParams.colorAlbedo.rgb;
            if(WaterParams.g_bUseTriplanarCaustics!=0){vec3 absRGN=abs(refractedGeometricNormalWs);float nyltnx=float(absRGN.y<absRGN.x);causticProjectionBasis=mix(MaterialParams.colorAlbedo.rgb,mix(mix(vec3(0,1,1),vec3(1,0,1),nyltnx),vec3(0,0,1),float(absRGN.z>max(absRGN.x,absRGN.y))),vec3(0.65));}
            float waterDepthAtCausticPoint = worldPosForFoamAndDebrisBase.z-refractedSceneHitPosWs.z;

            vec3 causticRayTarget=mix(refractedSceneHitPosWs+((causticProjectionBasis*causticProjectionBasis.z)*waterDepthAtCausticPoint),worldPosForRefractionOrigin,vec3(clamp((pow(blueNoiseSample.x,2.0)*waterOpticalDepthForTrueFog)*0.0125,0.0,1.0)));

            float distToCausticTarget=distance(causticRayTarget,refractedSceneHitPosWs);

            float causticDepthFalloff=clamp(1.0-(distToCausticTarget/max(0.001,WaterParams.g_flCausticDepthFallOffDistance)),0.0,1.0);

            float causticBaseIntensity=(causticVisibility*clamp(distToCausticTarget*0.05,0.0,1.0))*causticDepthFalloff;

            if(WaterParams.g_bUseTriplanarCaustics==0)
            {
            causticBaseIntensity *= clamp(dot(refractedGeometricNormalWs,causticProjectionBasis),0.0,1.0);
            }

            vec2 causticWaveUVBase = (causticRayTarget.xy*(1.0/30.0))*WaterParams.g_flCausticUVScaleMultiple;

            vec2 cOctScaleUV=WaterParams.g_vWaveScale; vec2 cAccPhaseUV=vec2(0); float cAngleUV=WaterParams.g_flWaterInitialDirection;
            SPIRV_CROSS_UNROLL
            for(uint ci=0u;ci<3u;++ci)
            {
            float cSharpMip=(-WaterParams.g_flCausticSharpness*(1.0-clamp(causticDepthFalloff,0.0,1.0))+1.0)*6.0;

            vec2 aOff=vec2(sin(cAngleUV),cos(cAngleUV))*((fsInput_animationTime*WaterParams.g_flWavesSpeed)*0.5);
            vec2 aniS=sqrt(vec2(1)/max(cOctScaleUV,vec2(0.001)));
            vec2 oUV=(aOff*aniS+(causticWaveUVBase+cAccPhaseUV)/max(cOctScaleUV,vec2(0.001)));

            vec3 cWSample=texture(sampler2D(g_tWavesNormalHeight,s_DefaultSampler_variant2),oUV,cSharpMip).xyz-0.5;
            cAccPhaseUV+=(((cWSample.xy*0.5)*WaterParams.g_flCausticDistortion)*(vec2(1)+cOctScaleUV))*(0.25+causticDepthFalloff);
            cOctScaleUV*=WaterParams.g_flWavesPhaseOffset;
            cAngleUV+=(3.5/float(ci+1u));
            }


            vec3 accCBright=vec3(0);
            vec2 cOctScaleBR=WaterParams.g_vWaveScale;
            float cAngleBR=WaterParams.g_flWaterInitialDirection;

            SPIRV_CROSS_UNROLL for(uint cbi=0u;cbi<3u;++cbi){float ip=0;if(WaterParams.g_nWaveIterations>1u)ip=float(cbi)/(float(WaterParams.g_nWaveIterations-1u));float cSF=1.0-clamp(causticDepthFalloff,0.0,1.0);float cSMip=WaterParams.g_flCausticSharpness*cSF;float cMLvl=(-WaterParams.g_flCausticSharpness*cSF+1.0)*6.0;vec2 aOffB=vec2(sin(cAngleBR),cos(cAngleBR))*((fsInput_animationTime*WaterParams.g_flWavesSpeed)*0.5);vec2 aniSB=sqrt(vec2(1)/max(cOctScaleBR,vec2(0.001)));vec2 oUVB=(aOffB*aniSB+(causticWaveUVBase+cAccPhaseUV)/max(cOctScaleBR,vec2(0.001)));float cH=texture(sampler2D(g_tWavesNormalHeight,s_DefaultSampler_variant2),oUVB,cMLvl).z;float lmB_c=clamp(ip*2.0,0.0,1.0);float mhB_c=clamp((ip*2.0-1.0),0.0,1.0);float bA_c=mix((iisDisturbanceForWaves*0.1+WaterParams.g_flLowFreqWeight),WaterParams.g_flMedFreqWeight+debrisDisturbanceForWaves,lmB_c);float aW_c=clamp(mix(bA_c,(WaterParams.g_flHighFreqWeight*currentWaterRoughness+debrisDisturbanceForWaves),mhB_c),0.1,0.4);accCBright+=(((pow(vec3(cH),vec3(cSMip*5.0))*aW_c)*(vec3(1)+(accCBright*2.0)))*causticDepthFalloff*cSMip*2.0);cOctScaleBR*=WaterParams.g_flWavesPhaseOffset;cAngleBR+=(3.5/float(cbi+1u));}
            vec3 cAppPos=causticRayTarget+((vec3(cAccPhaseUV,0.0)*60.0)*accCBright.x);
            vec4 cClipPos=(vec4(cAppPos,1.0)+ViewParams.g_vWorldToCameraOffset)*ViewParams.g_matWorldToProjection;
            vec2 cNdc=cClipPos.xy/max(0.001,cClipPos.w);
            vec2 cScreenUV=((vec2(cNdc.x,-cNdc.y)*0.5)+0.5);

            vec4 cWESRaw=texture(g_tWaterEffectsMap,cScreenUV*ViewParams.g_vViewportToGBufferRatio.xy)-0.5;

            vec2 cEFS=clamp(cWESRaw.yz*2.0,0.0,1.0);

            vec4 fCES=vec4(cWESRaw.x,cEFS.x,cEFS.y,cWESRaw.w);

            float cSEFade=(((cScreenUV.y*(1.0-cScreenUV.y))*cScreenUV.x)*(1.0-cScreenUV.x))*40.0;

            vec4 sFCE= fCES * clamp(cSEFade,0.0,1.0);

            float cEfXFM=sFCE.x;

            float shCEfXFM=cEfXFM+(cEfXFM/(fwidth(cEfXFM)*1000.0+0.5));

            vec3 cMod=(accCBright+vec3(((clamp(shCEfXFM,0.0,1.0)*4.0*WaterParams.g_flWaterEffectCausticStrength) - (clamp(-shCEfXFM,0.0,1.0)*0.15*WaterParams.g_flWaterEffectCausticStrength))))*mix(1.0,0.0,clamp((debrisTexSample.w*2.0+sFCE.y*0.4),0.0,1.0));

            float cBXF=cMod.x;
            float dfdx_cBXF=clamp(dFdxFine(cBXF)*200.0,-1.0,1.0);
            float cEF=clamp((-cBXF*3.0+1.0),0.0,1.0);
            vec3 cBEnh=pow(max(cMod*(vec3(1)+(vec3(1.25,-0.25,-1.0)*(dfdx_cBXF*cEF))),vec3(0.001))*8.0,vec3(2.5));
            refractedSceneColor*=(vec3(1)+(((((cBEnh*causticBaseIntensity)*MaterialParams.emissiveColor.rgb)*WaterParams.g_vCausticsTint.rgb)*WaterParams.g_flCausticsStrength)*0.1));
            float dlumc=clamp(dFdxFine(length(refractedSceneColor)),-1.0,1.0)+clamp(dFdyFine(length(refractedSceneColor)),-1.0,1.0);
            float cEHF=float(int(sign(dlumc*clamp(abs(dlumc)-0.1,0.0,1.0))));
            refractedSceneColor=mix(refractedSceneColor,refractedSceneColor*(vec3(1)+(vec3(2.5,0,-2.0)*cEHF)),vec3(clamp(200.0/distanceToFragment,0.0,1.0)*0.1));
            siltFromEffects = sFCE.z;
        }
    }

    // --- Water Fog ---
    float effectiveWaterDepthForFog = min(WaterParams.g_flWaterMaxDepth, finalWaterColumnDepthForRefract);
    vec3 waterDecayColorFactor = exp((WaterParams.g_vWaterDecayColor - 1.0) * WaterParams.g_flWaterDecayStrength * effectiveWaterDepthForFog);
    float totalFogStrength = max(waterOpticalDepthForTrueFog, siltFromEffects);
    float foamDebrisForFogMix = finalCombinedFoamDebrisAlpha + clamp(siltFromEffects - 0.5, 0.0, 1.0);
    float waterFogAlpha = ((-clamp(blueNoiseSample.x,0.0,1.0)*0.25+foamDebrisForFogMix)*0.1 + (1.0-exp(-effectiveWaterDepthForFog*totalFogStrength)));
    vec3 baseFogColor = mix(WaterParams.g_vWaterFogColor,WaterParams.g_vFoamColor.rgb*(foamDebrisForFogMix*0.5+1.0),vec3(foamDebrisForFogMix*0.1));
    vec3 finalWaterFogColor = baseFogColor * mix(waterDecayColorFactor,vec3(1.0),vec3(clamp(totalFogStrength*0.04,0.0,1.0)));
    vec3 colorBelowSurface = mix(refractedSceneColor, finalWaterFogColor, waterFogAlpha);

    // --- Final Surface Normal for Lighting ---
    vec3 waveNormalForLighting = finalWaveNormalFromProc * currentWaterRoughness;
    waveNormalForLighting.z = sqrt(max(0.0001, 1.0 - dot(waveNormalForLighting.xy, waveNormalForLighting.xy)));

    vec3 finalCombinedNormalWs = waveNormalForLighting;
    vec3 specularNormalWs = waveNormalForLighting;
    float shorelineBlendFactor = 0.0;

    if (!isSkyboxScaleEffectEnabled) {
        float shorelineBlendDistFactor = mix(60.0, 120.0, refractedGeometricNormalWs.z);
        shorelineBlendFactor = (clamp(((-sceneDepthWidth*1000.0) + clamp(((1.0/shorelineBlendDistFactor)-finalWaterColumnDepthForRefract)*shorelineBlendDistFactor,0.0,1.0) + clamp((0.025-finalWaterColumnDepthForRefract)*8.0,0.0,1.0)),0.0,1.0) / (distanceToFragment*0.002+1.0)) * 0.6;
        finalCombinedNormalWs = normalize(mix(waveNormalForLighting, refractedGeometricNormalWs, shorelineBlendFactor));
        specularNormalWs = normalize(mix(waveNormalForLighting * vec3(3.0,3.0,1.0), refractedGeometricNormalWs, shorelineBlendFactor));
        specularNormalWs.z = sqrt(max(0.0001, 1.0 - dot(specularNormalWs.xy, specularNormalWs.xy)));
    } else {
        specularNormalWs = normalize(waveNormalForLighting * vec3(3.0,3.0,1.0));
        specularNormalWs.z = sqrt(max(0.0001, 1.0 - dot(specularNormalWs.xy, specularNormalWs.xy)));
    }
    
    vec3 transformedFinalNormal = normalize(vec3(dot(MaterialParams.transformMatrix.c0,vec4(finalCombinedNormalWs,1.0)),
                                         dot(MaterialParams.transformMatrix.c1,vec4(finalCombinedNormalWs,1.0)),
                                         dot(MaterialParams.transformMatrix.c2,vec4(finalCombinedNormalWs,1.0))));

    // --- Shadow Attenuation (Main Light) ---
    float shadowAttenuation = 1.0;
    vec3 lightingSamplePosWs = fsInput_worldPositionCameraRelative + (((-skyRelatedProjectionVector)*(vec3(((debrisCoverageFactor*2.0*finalDebrisAlpha)+finalFoamHeightContrib)*-1.0)+(((mix(blueNoiseSample.xxx,vec3(blueNoiseSample.xy,0),vec3(0.1))*90.0)*pow(clamp(((1.0-finalCombinedFoamDebrisAlpha)*4.0+1.0)*(1.0-waterFogAlpha),0.0,1.0),2.0))+vec3(WaterParams.g_flWaterPlaneOffset))))*(effectiveWaterDepthForFog*2.0*0.75+ (1.0-0.75)));
    if (MaterialParams.materialTypeID > 0) {
        vec4 lightSpacePos; float bestCascadeBlend=1.0; vec3 shadowMapUVz=vec3(0); int selCascadeIdx=-1;
        for(int ci=0;ci<MaterialParams.materialTypeID;++ci){
            mat4 smat=MaterialParams.skinningMatricesOrLayers.matrices[ci];
            lightSpacePos=vec4(lightingSamplePosWs,1.0)*smat;
            float cascadeExtent = (ci==0)?MaterialParams.customMaterialParam1.x:((ci==1)?MaterialParams.customMaterialParam1.y:MaterialParams.customMaterialParam1.z);
            if(max(abs(lightSpacePos.x),abs(lightSpacePos.y))<cascadeExtent){
                vec3 cLSPos=lightSpacePos.xyz;
                vec2 cFadeFactors=vec2(1)-clamp((abs(cLSPos.xy)*vec2(MaterialParams.customMaterialFloat2)+vec2(MaterialParams.customMaterialFloat1)),vec2(0),vec2(1));
                bestCascadeBlend=clamp(cFadeFactors.x*cFadeFactors.y,0.0,1.0);
                vec2 uvScale,uvOffset;
                if(ci==0){uvScale=MaterialParams.secondaryTransformMatrix.c0.zw;uvOffset=MaterialParams.secondaryTransformMatrix.c0.xy;}
                else if(ci==1){uvScale=MaterialParams.secondaryTransformMatrix.c1.zw;uvOffset=MaterialParams.secondaryTransformMatrix.c1.xy;}
                else{uvScale=MaterialParams.secondaryTransformMatrix.c2.zw;uvOffset=MaterialParams.secondaryTransformMatrix.c2.xy;}
                vec2 shUV=(cLSPos.xy*uvScale+uvOffset); shadowMapUVz=vec3(shUV,cLSPos.z); selCascadeIdx=ci; break;
            }
        }
        if(selCascadeIdx>=0){
            float sSample=textureLod(sampler2DShadow(g_tShadowDepthBufferDepth,s_ShadowSamplerComparison),vec3(shadowMapUVz.xy,clamp(shadowMapUVz.z+MaterialParams.alphaTestThreshold,0.0,1.0)),0.0);
            if(bestCascadeBlend<1.0&&selCascadeIdx<(MaterialParams.materialTypeID-1)){
                int nci=selCascadeIdx+1; mat4 nSmat=MaterialParams.skinningMatricesOrLayers.matrices[nci]; vec4 nLSPos=vec4(lightingSamplePosWs,1.0)*nSmat;
                vec2 nUvScale,nUvOffset;
                if(nci==0){nUvScale=MaterialParams.secondaryTransformMatrix.c0.zw;nUvOffset=MaterialParams.secondaryTransformMatrix.c0.xy;}
                else if(nci==1){nUvScale=MaterialParams.secondaryTransformMatrix.c1.zw;nUvOffset=MaterialParams.secondaryTransformMatrix.c1.xy;}
                else{nUvScale=MaterialParams.secondaryTransformMatrix.c2.zw;nUvOffset=MaterialParams.secondaryTransformMatrix.c2.xy;}
                vec2 nShUV=(nLSPos.xy*nUvScale+nUvOffset); float nSSample=textureLod(sampler2DShadow(g_tShadowDepthBufferDepth,s_ShadowSamplerComparison),vec3(nShUV,clamp(nLSPos.z+MaterialParams.alphaTestThreshold,0.0,1.0)),0.0);
                shadowAttenuation=mix(nSSample,sSample,bestCascadeBlend);
            } else { shadowAttenuation=sSample; }
        }
        shadowAttenuation=mix(shadowAttenuation,1.0,clamp((distance(worldPositionAbs,ViewParams.g_vCameraPositionWs)*MaterialParams.customMaterialFloat4+MaterialParams.customMaterialFloat3),0.0,1.0));
        if(CSGOViewParams.g_bOtherFxEnabled.y!=0){shadowAttenuation=min(shadowAttenuation,textureLod(sampler2D(g_tParticleShadowBuffer,s_SamplerRepeatAniso),gbufferUV,0.0).z);}
    }
    float waterClarityFactorForShadow = clamp(((1.0-finalCombinedFoamDebrisAlpha)*4.0+1.0)*(1.0-waterFogAlpha),0.0,1.0) * clamp((1.0-finalDebrisAlpha)+debrisDepthFade,0.0,1.0);
    shadowAttenuation = mix(shadowAttenuation, 1.0, waterClarityFactorForShadow * 0.5);

    // --- Lighting (Main Light + Barn Lights) ---
    vec3 diffuseLighting = MaterialParams.PBRParams.rgb;
    if((dot(MaterialParams.colorAlbedo.rgb,finalCombinedNormalWs)*shadowAttenuation)>0.0){
        diffuseLighting=((vec3(max(0.0,dot(finalCombinedNormalWs,MaterialParams.colorAlbedo.rgb)))*(MaterialParams.emissiveColor.rgb*shadowAttenuation))+MaterialParams.PBRParams.rgb);
    }
    vec4 effectiveBarnLightFragCoord=fragCoord; if(CSGOViewParams.g_bOtherEnabled2.x!=0){vec4 pwp=vec4(worldPositionAbs,1.0)*CSGOViewParams.g_matPrimaryViewWorldToProjection;float invW=1.0/pwp.w;vec2 ndcp=pwp.xy*invW;effectiveBarnLightFragCoord.x=clamp(((ndcp.x+1.0)*ViewParams.g_vViewportSize.x)*0.5,0.0,ViewParams.g_vViewportSize.x-1.0);effectiveBarnLightFragCoord.y=clamp(((1.0-ndcp.y)*ViewParams.g_vViewportSize.y)*0.5,0.0,ViewParams.g_vViewportSize.y-1.0);effectiveBarnLightFragCoord.w=pwp.w;}
    uvec2 tileCoords=uvec2(effectiveBarnLightFragCoord.xy-ViewParams.g_vViewportOffset.xy)>>MaterialParams.flagsAndIndices2.x;
    uint tileOffset=MaterialParams.flagsAndIndices1.y+((tileCoords.y*MaterialParams.flagsAndIndices2.y)+tileCoords.x)*MaterialParams.flagsAndIndices1.w; // .w is stride
    uint depthSlice=uint(clamp(effectiveBarnLightFragCoord.w*MaterialParams.customMaterialParam1.x,0.0,MaterialParams.customMaterialParam1.y)); // .x scale, .y bias
    uint depthSliceOffset=MaterialParams.flagsAndIndices1.x+(depthSlice*MaterialParams.flagsAndIndices1.w); // .x offset, .w stride
    SPIRV_CROSS_LOOP for(uint wi=0u;wi<MaterialParams.flagsAndIndices1.w;++wi){ // .w here is num_words_per_tile
        uint lightMask=subgroupOr(cullBitsBuffer.g_CullBits[tileOffset+wi]&cullBitsBuffer.g_CullBits[depthSliceOffset+wi]);
        uint baseLIdx=wi*32u; uint currentLMask=lightMask;
        SPIRV_CROSS_LOOP while(currentLMask!=0u){ int lBit=findLSB(currentLMask); int gLIdx=int(uint(lBit)+baseLIdx); currentLMask&=(currentLMask-1u);
            BarnLight light=barnLightsBuffer.g_BarnLights[gLIdx];
            vec4 cLSpacePos=mat4(light.lightViewProjectionMatrix)*vec4(lightingSamplePosWs,1.0); vec3 lsc=cLSpacePos.xyz/cLSpacePos.w;
            vec3 sc=lsc; if((light.lightFlags&4u)!=0u){sc.xy=lsc.yx*vec2(1,-1);}
            bool isInLV=all(greaterThan(sc,vec3(-1,-1,0)))&&all(lessThan(sc,vec3(1)));
            if(isInLV){vec3 ssp=mat3x4(light.lightShapeTransform.c0,light.lightShapeTransform.c1,light.lightShapeTransform.c2)*vec4(lightingSamplePosWs,1.0);isInLV=all(lessThanEqual(abs(ssp),vec3(1)));}
            if(!isInLV)continue;
            float lIntensity=1.0; vec3 lDirWs;
            if(light.lightPositionWs_Type.w!=0.0){vec3 tlVec=light.lightPositionWs_Type.xyz-lightingSamplePosWs;float dSq=dot(tlVec,tlVec);float d=sqrt(dSq);lDirWs=tlVec/d;lIntensity*=(light.lightPositionWs_Type.w/max(dSq,light.lightPositionWs_Type.w));lIntensity*=clamp((light.lightColor_Intensity.y*d+light.lightColor_Intensity.x),0.0,1.0);float NdotLspot=dot(-lDirWs,normalize(light.lightDirectionWs_ConeAngle.xyz));float spotF=smoothstep(light.lightDirectionWs_ConeAngle.w,light.lightDirectionWs_ConeAngle.w+light.lightAttenuationParams.x,NdotLspot);lIntensity*=spotF;}else{lDirWs=normalize(light.lightPositionWs_Type.xyz);}
            vec3 lColor=light.lightColor_Intensity.rgb*lIntensity;
            if(light.lightCookieTextureIndexOrID!=0u){vec3 cUVs;if(float(light.lightCookieTextureIndexOrID)<0.0){vec4 sCP=light.barnDoorControls1;mat3 rM=mat3(sCP.x,sCP.y,sCP.z,0,1,0,0,0,1); vec3 rLD=(-lDirWs*rM);cUVs=vec3(vec2(atan(rLD.y,-rLD.x)*0.15915,acos(rLD.z)*0.3183),-float(light.lightCookieTextureIndexOrID));cUVs.xy=(cUVs.xy*light.barnDoorControls2.zw+light.barnDoorControls2.xy);lColor*=textureLod(sampler3D(g_tLightCookieTexture,s_SamplerClampLinear),cUVs,0.0).rgb;}else{cUVs=vec3(((sc.xy*vec2(0.5,-0.5))+vec2(0.5)),float(light.lightCookieTextureIndexOrID));cUVs.xy=(cUVs.xy*light.barnDoorControls2.zw+light.barnDoorControls2.xy);lColor*=textureLod(sampler3D(g_tLightCookieTexture,s_SamplerClampBorderBlack),cUVs,0.0).rgb;}}
            float bLShad=1.0; if(light.shadowBiasAndParams.z>0.0&&CSGOViewParams.g_bOtherEnabled2.x==0){bLShad=textureLod(sampler2DShadow(g_tShadowDepthBufferDepth,s_ShadowSamplerComparison),vec3((sc.xy*light.shadowBiasAndParams.zw+light.shadowBiasAndParams.xy),clamp(sc.z+MaterialParams.alphaTestThreshold,0.0,1.0)),0.0);bLShad=mix(1.0,bLShad,light.effectRadius);} lColor*=bLShad;
            if(any(notEqual(lColor,vec3(0)))){diffuseLighting+=max(0.0,dot(finalCombinedNormalWs,lDirWs))*lColor;}
        }
    }

    // --- Reflections & Specular ---
    vec3 envReflDir = -reflect(viewVectorWs, finalCombinedNormalWs); // reflect view about normal
    float roughnessForCubemap = sqrt(dot(mix(WaterParams.g_vRoughness,vec2(1),vec2(clamp(reflectionLODFactor,0,0.35))),vec2(0.5)));
    vec3 lowEndCubemapReflection = textureLod(samplerCube(g_tLowEndCubeMap, s_DefaultSampler_variant1), envReflDir, roughnessForCubemap*6.0).rgb * (dot(transformedFinalNormal,vec3(0.2125,0.7154,0.0721))*WaterParams.g_flLowEndCubeMapIntensity) * WaterParams.g_flEnvironmentMapBrightness;
    vec3 ssrResult = lowEndCubemapReflection;
    uint ssrSteps = uint((float(WaterParams.g_nSSRMaxForwardSteps)*mix(1.0,0.5,float(isSkyboxScaleEffectEnabled)))*clamp((ViewParams.g_vCameraDirWs.z+0.75)*4.0,0.0,1.0));
    if(ssrSteps > 0u) {
        float ssrThickness=(ditherFactor*WaterParams.g_flSSRSampleJitter+WaterParams.g_flSSRMaxThickness);
        mat4 w2v=ViewParams.g_matWorldToView;
        vec3 viewReflDirForSSR = (vec4(normalize(vec3((specularNormalWs.xy*3.0)*mix(2.0,8.0,float(isSkyboxScaleEffectEnabled)),specularNormalWs.z)),0.0)*w2v).xyz;
        vec3 viewOrigin=(vec4(worldPosForRefractionOrigin,1.0)*w2v).xyz; mat4 v2p=ViewParams.g_matViewToProjection;
        vec4 pOrigin=(v2p*vec4(-viewOrigin,1.0)); vec2 screenOrigin=((vec2(pOrigin.x,-pOrigin.y)/pOrigin.w*0.5)+0.5);
        float stepSize=((ditherFactor*WaterParams.g_flSSRSampleJitter+WaterParams.g_flSSRStepSize)/(reflectionLODFactor*2.0+1.0))*mix(20.0,1.0,clamp(dot(toCameraVectorWs,specularNormalWs),0.0,1.0));
        if(isSkyboxScaleEffectEnabled)stepSize*=(distanceToFragment*0.002);
        vec3 currentRayPosVS=viewOrigin; vec4 currentRayScreen=vec4(screenOrigin,0,0); uint stepCount=1u;
        float prevHitDeltaZ=0.0; float hitMix=0.0;
        SPIRV_CROSS_LOOP for(;(stepCount<=ssrSteps);++stepCount){
            float currentStep=stepSize*1.15;
            currentRayPosVS+=normalize(reflect(viewOrigin,viewReflDirForSSR))*currentStep; // reflect source ray, not just direction
            vec4 projPos=v2p*vec4(-currentRayPosVS,1.0); vec2 screenPos=((vec2(projPos.x,-projPos.y)/projPos.w*0.5)+0.5);
            float sceneZ_vs_ssr=-(ViewParams.g_vDepthPsToVsConversion.x/((ViewParams.g_vDepthPsToVsConversion.y*clamp((textureLod(sampler2D(g_tSceneDepth,s_SamplerRepeatLinear),screenPos*ViewParams.g_vViewportToGBufferRatio.xy,0.0).x-ViewParams.g_flViewportMinZ)/(ViewParams.g_flViewportMaxZ-ViewParams.g_flViewportMinZ),0.0,1.0))+ViewParams.g_vDepthPsToVsConversion.z));
            float deltaZ=sceneZ_vs_ssr-currentRayPosVS.z;
            hitMix=clamp(deltaZ/(deltaZ-prevHitDeltaZ + 1e-6),0.0,1.0); // Add epsilon to avoid div by zero
            if(deltaZ>=0.0&&deltaZ<(ssrThickness*currentStep)){currentRayScreen=mix(vec4(screenPos,0,0),currentRayScreen,vec4(hitMix));break;}
            prevHitDeltaZ=deltaZ; currentRayScreen=vec4(screenPos,0,0); stepSize=currentStep;
        }
        float fadeSSR = (float(stepCount)-hitMix)/float(ssrSteps);
        if(!isSkyboxScaleEffectEnabled){
            vec2 ssrUV=(currentRayScreen.xy*ViewParams.g_vViewportToGBufferRatio.xy); float ssrOff=fadeSSR*-0.00390625;
            vec3 ssrCol=((texture(sampler2D(g_tRefractionMap,s_SamplerRepeatAniso),clamp(ssrUV-vec2(fadeSSR*0.00390625),vec2(0),vec2(1))).xyz*0.4444)+ (texture(sampler2D(g_tRefractionMap,s_SamplerRepeatAniso),clamp(ssrUV+vec2(0.00195,ssrOff),vec2(0),vec2(1))).xyz*0.2222)+ (texture(sampler2D(g_tRefractionMap,s_SamplerRepeatAniso),clamp(ssrUV+vec2(ssrOff,0.00195),vec2(0),vec2(1))).xyz*0.2222)+ (texture(sampler2D(g_tRefractionMap,s_SamplerRepeatAniso),clamp(ssrUV+vec2(0.00195),vec2(0),vec2(1))).xyz*0.1111));
            ssrResult=(ssrCol+((normalize(ssrCol+0.001)*max(0,dot(ssrCol,vec3(0.2125,0.7154,0.0721))-WaterParams.g_flSSRBoostThreshold))*WaterParams.g_flSSRBoost))*WaterParams.g_flSSRBrightness;
        } else { ssrResult=mix((colorBelowSurface+lowEndCubemapReflection)*0.5,lowEndCubemapReflection,fadeSSR); }
        ssrResult=mix(lowEndCubemapReflection,ssrResult,(clamp(1.0-pow(fadeSSR,4.0),0.0,1.0)*clamp(currentRayScreen.y*8.0,0.0,1.0))*clamp(clamp((ViewParams.g_vCameraDirWs.z+0.75)*4.0,0.0,1.0)*2.0,0.0,1.0));
    }

    // --- Final Color Composition ---
    float fresnelNdotV = clamp(dot(toCameraVectorWs, specularNormalWs), 0.0, 1.0);
    float fresnelTerm = pow(1.0 - fresnelNdotV, WaterParams.g_flFresnelExponent);
    float reflectionBlendFactor = ((fresnelTerm * (1.0 - WaterParams.g_flReflectance) + WaterParams.g_flReflectance) *
                                   (((1.0-finalCombinedFoamDebrisAlpha)*2.0) + ((-finalCombinedFoamDebrisAlpha*0.75)+1.0)))*1.5; // Original had fma(-finalCombinedFoamDebrisAlpha,0.75,1.0), this seems wrong. Should be like (1.0 - x*str)


    vec3 surfaceColorVisual = mix((finalWaterFogColor*waterFogAlpha)*WaterParams.g_flWaterFogShadowStrength, WaterParams.g_vFoamColor.rgb, finalCombinedFoamDebrisAlpha);
    surfaceColorVisual = mix(surfaceColorVisual, debrisTexSample.rgb * (finalDebrisAlpha*0.5+0.5) * WaterParams.g_vDebrisTint, clamp(finalDebrisAlpha-debrisDepthFade,0.0,1.0));
    vec3 finalLitColor = diffuseLighting * surfaceColorVisual; // Apply lighting to visual surface color
    finalLitColor = mix(finalLitColor, colorBelowSurface, waterClarityFactorForShadow);

    float specularPower = mix(WaterParams.g_flSpecularPower, WaterParams.g_flDebrisReflectance*8.0,finalDebrisAlpha)*mix(2.0,0.2,currentWaterRoughness);
    float NdotH_approx = fresnelNdotV; // Using NdotV as proxy for NdotH for simplicity here, common for water
    float specularBase = pow(NdotH_approx, specularPower);
    float specularIntensity = (specularBase*0.1 + pow(NdotH_approx,specularPower*10.0));
    specularIntensity = ((max(0.0,specularIntensity-(1.0-WaterParams.g_flSpecularBloomBoostThreshold))*WaterParams.g_flSpecularBloomBoostStrength)+specularIntensity) * mix(1.0,WaterParams.g_flDebrisReflectance*0.05,finalDebrisAlpha);
    finalLitColor += (diffuseLighting * specularIntensity * reflectionBlendFactor * MaterialParams.emissiveColor.rgb); // Assuming emissive is light color for spec

    float oilyTime=fract((ViewParams.g_flTime*0.1+(fresnelTerm*20.0+debrisCoverageFactor*8.0))); float oilyFloor=floor(oilyTime*6.0); float oilyFrac=(oilyTime*6.0-oilyFloor);
    float rb_a=0.75*(1.0-oilyFrac); float rb_b=0.75*oilyFrac; vec3 rainbowColor;
    if(oilyFloor==0.0)rainbowColor=vec3(0.75,rb_b,0); else if(oilyFloor==1.0)rainbowColor=vec3(rb_a,0.75,0); else if(oilyFloor==2.0)rainbowColor=vec3(0,0.75,rb_b);
    else if(oilyFloor==3.0)rainbowColor=vec3(0,rb_a,0.75); else if(oilyFloor==4.0)rainbowColor=vec3(rb_b,0,0.75); else rainbowColor=vec3(0.75,0,rb_a);
    vec3 oilyReflection = mix(ssrResult, ssrResult*rainbowColor, ((clamp(debrisWobblePowerFromEffects*20.0,0.0,1.0)*WaterParams.g_flDebrisOilyness)/(distanceToFragment*0.005+1.0))*clamp((-(waterColumnOpticalDepthFactor*toCameraVectorWs.z)*5.0+1.0),0.0,1.0) );

    finalLitColor = finalLitColor * mix(vec3(1.0), transformedFinalNormal * 0.75, vec3(clamp(debrisTexSample.w * 4.0, 0.0, 1.0) * waterClarityFactorForShadow));
    finalLitColor = mix(finalLitColor, oilyReflection, clamp(reflectionBlendFactor, 0.0, 1.0));

    // --- Scene Fog (Gradient/Cube) & Final Discard/Blend ---
    if(WaterParams.g_bFogEnabled!=0){
        vec3 vV=worldPositionAbs-ViewParams.g_vCameraPositionWs; bool applyGF=false; if(dot(vV,vV)>CSGOViewParams.g_vGradientFogCullingParams.x){applyGF=(worldPositionAbs.z*CSGOViewParams.g_vGradientFogCullingParams.z)<CSGOViewParams.g_vGradientFogCullingParams.y;}
        if(applyGF){vec2 fF=clamp(((CSGOViewParams.g_vGradientFogBiasAndScale.zw*vec2(length(vV),worldPositionAbs.z))+CSGOViewParams.g_vGradientFogBiasAndScale.xy),vec2(0),vec2(1));float gFA=(pow(fF.x,CSGOViewParams.m_vGradientFogExponents.x)*pow(fF.y,CSGOViewParams.m_vGradientFogExponents.y))*CSGOViewParams.g_vGradientFogColor_Opacity.w;finalLitColor=mix(finalLitColor,CSGOViewParams.g_vGradientFogColor_Opacity.rgb,gFA);}
        bool applyCF=false; if(dot(vV,vV)>CSGOViewParams.g_vCubeFogCullingParams_MaxOpacity.x){applyCF=(CSGOViewParams.g_vCubeFogCullingParams_MaxOpacity.z*worldPositionAbs.z)<CSGOViewParams.g_vCubeFogCullingParams_MaxOpacity.y;}
        if(applyCF){float df=clamp(pow(max(0,(length(vV)*CSGOViewParams.g_vCubeFog_Offset_Scale_Bias_Exponent.y+CSGOViewParams.g_vCubeFog_Offset_Scale_Bias_Exponent.x)),CSGOViewParams.g_vCubeFog_Offset_Scale_Bias_Exponent.w),0,1);float hf=clamp(pow(max(0,(worldPositionAbs.z*CSGOViewParams.g_vCubeFog_Height_Offset_Scale_Exponent_Log2Mip.y+CSGOViewParams.g_vCubeFog_Height_Offset_Scale_Exponent_Log2Mip.x)),CSGOViewParams.g_vCubeFog_Height_Offset_Scale_Exponent_Log2Mip.z),0,1);float cFV=clamp(df*hf,0,1);float cFMip=CSGOViewParams.g_vCubeFog_Height_Offset_Scale_Exponent_Log2Mip.w*clamp((-cFV*CSGOViewParams.g_vCubeFog_Offset_Scale_Bias_Exponent.z+1.0),0,1);vec3 fCDir=normalize(CSGOViewParams.g_matvCubeFogSkyWsToOs*vec4(vV,0)).xyz;vec3 cFC=textureLod(samplerCube(g_tFogCubeTexture,s_SamplerRepeatAniso),fCDir,cFMip).rgb*CSGOViewParams.g_vCubeFog_ExposureBias.x;float cFA=cFV*CSGOViewParams.g_vCubeFogCullingParams_MaxOpacity.w;finalLitColor=mix(finalLitColor,cFC,cFA);}
    }
    vec2 mapSpaceUV = (worldPositionAbs.xy-WaterParams.g_vMapUVMin)/(WaterParams.g_vMapUVMax-WaterParams.g_vMapUVMin); mapSpaceUV.y=1.0-mapSpaceUV.y;
    if(!isSkyboxScaleEffectEnabled){vec2 mEF=abs(vec2(0.5)-mapSpaceUV)*2.0; if((clamp(1.0-clamp((max(mEF.x,mEF.y)-(1.0-WaterParams.g_flSkyBoxFadeRange))/WaterParams.g_flSkyBoxFadeRange,0,1),0,1)-blueNoiseSample.x)<0.0){discard;}}

    // OIT Blend
    if(occlusionFactor>0.0){vec4 oitS=texelFetch(g_tMoitFinal,momentTexelCoords,0);vec3 oitC=oitS.rgb*(occlusionFactor/(oitS.w+1e-5));finalLitColor=oitC+(finalLitColor*visibilityFromMoment);}
    
    // Final Edge Blend
    if(!isSkyboxScaleEffectEnabled){
        float edgeBlendF = clamp(((WaterParams.g_flEdgeHardness*effectiveWaterDepthForFog)+finalCombinedFoamDebrisAlpha) + (debrisCoverageFactor*2.0-0.5),0.0,1.0);
        float sceneColEdgeAtten = mix(1.0,0.6,clamp((waterColumnOpticalDepthFactor*toCameraVectorWs.z)*60.0,0.0,1.0)/(distanceToFragment*0.002+1.0));
        finalLitColor=mix(refractionColorSample.rgb*sceneColEdgeAtten,finalLitColor,edgeBlendF);
    }

    outFragColor = vec4(finalLitColor, 1.0);
}
