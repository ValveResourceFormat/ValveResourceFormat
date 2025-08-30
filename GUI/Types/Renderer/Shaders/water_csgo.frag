#version 460

#include "common/utils.glsl"
#include "common/features.glsl"
#include "common/ViewConstants.glsl"
#include "common/LightingConstants.glsl"

in vec3 vFragPosition;
in vec2 vTexCoordOut;
in vec3 vNormalOut;
in vec3 vTangentOut;
in vec3 vBitangentOut;
in vec4 vColorBlendValues;

#include "common/lighting_common.glsl"
#include "common/fullbright.glsl"
#include "common/texturing.glsl"
#include "common/fog.glsl"
#include "common/environment.glsl"
#include "common/lighting.glsl"

out vec4 outputColor;

#define F_REFLECTION_TYPE 0 // (0="Sky Color Only", 1="Environment Cube Map", 2="SSR over Environment Cube Map")
#define F_REFRACTION 0
#define F_CAUSTICS 0
#define F_BLUR_REFRACTION 0

uniform float g_flWaterPlaneOffset = 0.0;
uniform float g_flSkyBoxScale = 16.0;
uniform float g_flSkyBoxFadeRange = 0.05;
uniform vec2 g_vMapUVMin = vec2(-10272, -10199);
uniform vec2 g_vMapUVMax = vec2(10241, 10241);
uniform float g_flLowEndCubeMapIntensity = 1.0;
uniform float g_flWaterRoughnessMin = 0.25;
uniform float g_flWaterRoughnessMax = 1.0;
uniform float g_flFoamMin = 0.0;
uniform float g_flFoamMax = 1.0;
uniform float g_flDebrisMin = 0.0;
uniform float g_flDebrisMax = 1.0;
uniform vec3 g_vDebrisTint = vec3(0.7, 0.7, 0.7); // SrgbRead(true)
uniform float g_flDebrisReflectance = 0.1;
uniform float g_flDebrisOilyness = 0.1;
uniform float g_flDebrisNormalStrength = 1.0;
uniform float g_flDebrisEdgeSharpness = 10.0;
uniform float g_flDebrisScale = 1.0;
uniform float g_flDebrisWobble = 1.0;
uniform float g_flFoamScale = 1.0;
uniform float g_flFoamWobble = 1.0;
uniform vec4 g_vFoamColor = vec4(0.7, 0.7, 0.7, 1.0); // SrgbRead(true)
uniform float g_flWavesHeightOffset = 1.0;
uniform float g_flWavesSharpness = 0.5;
uniform float g_flFresnelExponent = 5.0;
uniform float g_flWavesNormalStrength = 1.0;
uniform float g_flWavesNormalJitter = 0.1;
uniform vec2 g_vWaveScale = vec2(15.0, 15.0);
uniform float g_flWaterInitialDirection = 1.5;
uniform float g_flWavesSpeed = 1.0;
uniform float g_flLowFreqWeight = 0.5;
uniform float g_flMedFreqWeight = 0.5;
uniform float g_flHighFreqWeight = 0.5;
uniform float g_flWavesPhaseOffset = 0.4;
uniform float g_flEdgeHardness = 10.0;
uniform float g_flEdgeShapeEffect = 1.0;
uniform int g_nWaveIterations = 3;
uniform vec3 g_vWaterFogColor = vec3(0.5, 0.5, 0.5); // SrgbRead(true)
uniform float g_flRefractionLimit = 0.1;
uniform float g_flWaterFogStrength = 0.5;
uniform float g_flRefractSampleOffset = 2.0;
uniform float g_flRefractChromaticSeparation = 0.5;
uniform vec3 g_vWaterDecayColor = vec3(1.0, 1.0, 1.0); // not converted to linear
uniform float g_flWaterDecayStrength = 8.0;
uniform float g_flWaterMaxDepth = 100.0;
uniform float g_flWaterFogShadowStrength = 0.5;
uniform float g_flUnderwaterDarkening = 0.6;
uniform float g_flSpecularPower = 300.0;
uniform float g_flSpecularNormalMultiple = 2.0;
uniform float g_flSpecularBloomBoostStrength = 100.0;
uniform float g_flSpecularBloomBoostThreshold = 0.7;
uniform int g_bUseTriplanarCaustics;
uniform float g_flCausticUVScaleMultiple = 2.5;
uniform float g_flCausticDistortion = 0.5;
uniform float g_flCausticsStrength = 40.0;
uniform float g_flCausticSharpness = 0.5;
uniform float g_flCausticDepthFallOffDistance = 100.0;
uniform float g_flCausticShadowCutOff = 0.2;
uniform vec4 g_vCausticsTint = vec4(0.5, 0.5, 0.5, 1.0); // not converted to linear
uniform float g_flReflectance = 0.2;
uniform float g_flReflectionDistanceEffect = 0.5;
uniform float g_flForceMixResolutionScale = 1.0;
uniform float g_flEnvironmentMapBrightness = 1.0;
uniform float g_flGlossiness = 0.75;
uniform float g_flSSRStepSize = 0.1;
uniform float g_flSSRSampleJitter = 0.02;
uniform int g_nSSRMaxForwardSteps = 20;
uniform float g_flSSRBoostThreshold = 1.0;
uniform float g_flSSRBoost = 0.0;
uniform float g_flSSRBrightness = 1.0;
uniform float g_flSSRMaxThickness = 4.0;
uniform float g_flWaterEffectsRippleStrength = 1.0;
uniform float g_flWaterEffectSiltStrength = 1.0;
uniform float g_flWaterEffectFoamStrength = 1.0;
uniform float g_flWaterEffectDisturbanceStrength = 1.0;
uniform float g_flWaterEffectCausticStrength = 1.0;

#define g_vRoughness vec2(max(.01, 1.0 - g_flGlossiness))
uniform vec4 g_vViewportExtentsTs;

uniform sampler2D g_tZerothMoment;
uniform sampler2D g_tBlueNoise;
uniform sampler2D g_tFoam;
uniform sampler2D g_tDebris; // SrgbRead(true)
uniform sampler2D g_tDebrisNormal;

uniform samplerCube g_tLowEndCubeMap; // SrgbRead(true)

uniform sampler2D g_tWaterEffectsMap;
uniform sampler2D g_tParticleShadowBuffer;
uniform sampler3D g_tLightCookieTexture;
uniform sampler2D g_tMoitFinal;
uniform sampler2D g_tWavesNormalHeight;

uniform vec4 g_vSimpleSkyReflectionColor = vec4(1.0);

#if (F_REFRACTION == 1)
    uniform sampler2D g_tSceneColor;
    uniform sampler2D g_tSceneDepth;
#endif

void main()
{
    vec4 fragCoord = gl_FragCoord;
    vec4 fragCoordWInverse = fragCoord;
    fragCoordWInverse.w = 1.0 / fragCoord.w;

    MaterialProperties_t mat;
    InitProperties(mat, vNormalOut);

    // --- Skybox Scale Effect & Blue Noise ---

    const float flSkyboxScale = 1.0;
    //float flSkyboxScale = g_bIsSkybox ? g_flSkyBoxScale : 1.0;

    //TODO: Whats up with this??
    //vec4 NoiseValue = texelFetch(g_tBlueNoise, ivec3(ivec2(FragCoord.xy) & PerViewConstantBufferCsgo_t.g_vBlueNoiseMask, 0).xy, 0);

    vec4 blueNoise = texelFetch(g_tBlueNoise, ivec2(mod(fragCoord.xy, textureSize(g_tBlueNoise, 0))), 0);
    //This came from later on, but was centralized up here.
    vec2 blueNoiseOffset = blueNoise.xy - 0.5;
    // around line 406 in decomp
    float blueNoiseDitherFactor = blueNoiseOffset.x * 2.0;

    // --- Position & View Vectors ---
    vec2 gbufferUV = fragCoord.xy / textureSize( g_tSceneColor, 0);

    vec2 unbiasedUV = (mat.PositionWS.xy - g_vMapUVMin.xy) / (g_vMapUVMax.xy - g_vMapUVMin.xy);
    unbiasedUV.y = 1.0 - unbiasedUV.y;

    vec3 relFragPos = mat.PositionWS - g_vCameraPositionWs;

    vec3 viewDir = normalize(relFragPos);
    vec3 invViewDir = -viewDir;
    float distanceToFrag = length(relFragPos) * flSkyboxScale;

    float fragDepth = gl_FragCoord.z;
    //^ my own addition, from distance and depth, you can get a multiplier for any depth sample taken at gbufferUV from depth to true distance

    vec2 viewParallaxFactor = (viewDir.xy) / (-viewDir.z + 0.25);
    //The following is not in the direct decompile either, but the inverse offset like this exists atleast once
    vec3 worldPosToCamera = g_vCameraPositionWs - mat.PositionWS;

    // ---- Skybox corrected projection stuff ---------
    //Gemini suggested this one below but its not even used here yet. Again TODO to check that
    //vec3 scaledRelFragPos = relFragPos * flSkyboxScale;
    //vec3 horChangerateSqrtZ = mix( vec3(invViewDir.xy / invViewDir.z, sqrt(invViewDir.z)), vec3(0.0), g_bIsSkybox);
    vec3 viewDepOffsetFactor = mix(vec3(viewDir.xy / viewDir.z, sqrt(-viewDir.z)), vec3(0.0), vec3(g_bIsSkybox));

    // ---- Something about Refraction (idk either) ------
    float refractionDistortionFactor = 0.0;
    float waterColumnOpticalDepthFactor = 1.0;
    vec4 refractionColorSample = vec4(0.0);
    float sceneNormalizedDepth = 1.0;
    vec3 sceneHitPositionWs = vec3(0.0);

    // ----- SOME PRE REFRACTION ???? ------
    //I have no fucking clue why they do this beforehand

    if (!g_bIsSkybox)
    {
        float sceneDepth = textureLod(g_tSceneDepth, gbufferUV, 0.0).x;
        sceneNormalizedDepth = LinearRamp(g_flViewportMinZ, g_flViewportMaxZ, sceneDepth);

        sceneNormalizedDepth = max(sceneNormalizedDepth, 0.00001);
        refractionColorSample = texture(g_tSceneColor, gbufferUV);

        float refractionLuminance = clamp(GetLuma(refractionColorSample.rgb), 0.0, 0.4);
        refractionDistortionFactor = refractionLuminance * -0.03;

        float invProjTerm = fma(sceneNormalizedDepth, g_vInvProjRow3.z, g_vInvProjRow3.w);
        float perspectiveCorrection = dot(g_vCameraDirWs, viewDir);
        float sceneViewDistance = (1.0 / (invProjTerm * perspectiveCorrection));

        sceneHitPositionWs = g_vCameraPositionWs + viewDir * sceneViewDistance;

        float waterSurfaceViewZ = -(g_matWorldToView * vec4(mat.PositionWS, 1.0)).z;
        waterColumnOpticalDepthFactor = (refractionDistortionFactor * 1.0 + ClampToPositive((1.0 / sceneNormalizedDepth) - waterSurfaceViewZ) * 0.01);
    }

    float waterSurfaceViewZ = -(g_matWorldToView * vec4(mat.PositionWS, 1.0) ).z;

    float adjustedWaterColumnDepth = ClampToPositive(waterColumnOpticalDepthFactor - 0.02);
    float refractedVerticalFactor = waterColumnOpticalDepthFactor * invViewDir.z;

    // --- Get Roughness, Foam and Debris ----
    float currentWaterRoughness = g_bIsSkybox
        ? g_flWaterRoughnessMax
        : ClampToPositive(mix(g_flWaterRoughnessMin, g_flWaterRoughnessMax, vColorBlendValues.x));

    float currentFoamAmount = g_bIsSkybox
        ? 0.0
        : ClampToPositive(mix(g_flFoamMin, g_flFoamMax, vColorBlendValues.y));

    float currentDebrisVisibility = g_bIsSkybox
        ? 0.0
        : ClampToPositive(mix(g_flDebrisMin, g_flDebrisMax, vColorBlendValues.z));

    vec2 baseWaveUV = (mat.PositionWS.xy * flSkyboxScale + viewDepOffsetFactor.xy * (0.5 - g_flWaterPlaneOffset)) / 30.0; // Another arbitrary scale

    vec2 baseWaveUVDx = dFdx(baseWaveUV);
    //TODO: same shit as earlier with dFdy: why is it flipped in CS?
    vec2 baseWaveUVDy = -dFdy(baseWaveUV);

    float reflectionsLodFactor = (0.5 * pow(max(dot(baseWaveUVDx, baseWaveUVDx), dot(baseWaveUVDy, baseWaveUVDy)), 0.1)) * g_flReflectionDistanceEffect;

    //(fragCoord.xy - g_vViewportOffset.xy) * g_vInvViewportSize.xy * g_vViewportToGBufferRatio.xy, just assuming a size of SceneColor and ratio of 1.0
    vec2 waterEffectsMapUV = gbufferUV;
    vec4 waterEffectsSampleRaw = vec4(vec3(0.5), 0.0); //texture(g_tWaterEffectsMap, waterEffectsMapUV);
    vec2 waterEffectsDisturbanceXY = saturate((waterEffectsSampleRaw.yz - 0.5) * 2.0);
    float waterEffectsFoam = waterEffectsDisturbanceXY.y;

    //TODO MISMATCH: this matches decompile, what the fuck is it?
    vec4 _24505;
    _24505.z = waterEffectsFoam;

    float totalDisturbanceStrength = (waterEffectsDisturbanceXY.x + waterEffectsDisturbanceXY.y) * g_flWaterEffectDisturbanceStrength;
    float disturbanceWeightedFoamAmount = totalDisturbanceStrength * 0.25;
    float clampedLodFactor = clamp(reflectionsLodFactor, 0.0, 0.5);

    //TODO: I have no idea what the fuck this does and I don't have the energy to find out rn
    vec3 refractShiftedPos = sceneHitPositionWs + viewDepOffsetFactor * clamp(GetLuma(refractionColorSample.rgb), 0.0, 0.4);

    vec3 refractShiftedPosDdx = dFdx(refractShiftedPos);

    vec3 refractShiftedPosDdy = -dFdy(refractShiftedPos);
    vec3 reconstructedWorldNormal = -normalize(cross(refractShiftedPosDdx, refractShiftedPosDdy));

    float timeAnim = g_flTime * 3.0 + sin(g_flTime * 0.5) * 0.1;

    vec2 depthFactorFine = vec2(saturate(adjustedWaterColumnDepth * 10.0));
    vec2 depthFactorCoarse = vec2(saturate(adjustedWaterColumnDepth * 4.0));

    float sceneDepthChangeMagnitude = fwidth(sceneNormalizedDepth);

    //TODO: this can just be reconstructedWorldNormal.xy += blueNoiseOffset * 0.05;
    vec2 ditheredRefractShiftNormalXY = reconstructedWorldNormal.xy + blueNoiseOffset * 0.05;


    // ------ WAVE LOGIC -------
    vec2 accumulatedWaveUVOffset = vec2(0.0); // For UV distortion by waves
    vec2 currentWaveTexScale = g_vWaveScale.xy;
    vec3 accumulatedWaveNormal = vec3(0.0, 0.0, 1.0); // Start with up vector
    float accumulatedWaveHeight = 0.0;
    vec2 accumulatedPhaseOffset = vec2(0.0);

    float currentWaveAngle = g_flWaterInitialDirection;

    for (uint i = 0; i < g_nWaveIterations; ++i)
    {
        float iterProgress = float(i) / (float(g_nWaveIterations) - 1.0);

        // Weight for this wave octave (low, med, high frequencies)
        float lowMedBlend = saturate(iterProgress * 2.0);
        float medHighBlend = saturate(iterProgress * 2.0 - 1.0);

        float lowFreqWeight = fma(totalDisturbanceStrength, 0.05, g_flLowFreqWeight);
        float medFreqWeight = fma(totalDisturbanceStrength, 0.25, g_flMedFreqWeight);

        float lowMedWeightedAmplitude = mix(
            lowFreqWeight,
            medFreqWeight,
            lowMedBlend
        );

        float freqWeight = mix(
            lowMedWeightedAmplitude,
            g_flHighFreqWeight * currentWaterRoughness + disturbanceWeightedFoamAmount, // Roughness makes high-freq waves stronger
            medHighBlend
        );

        // Sample wave texture: RG=Normal, B=Height (all signed, centered at 0.5)
        vec2 waveAnimOffset = vec2(sin(currentWaveAngle), cos(currentWaveAngle)) * (g_flTime * g_flWavesSpeed) * 0.5;

        vec2 anisoUV = waveAnimOffset * inversesqrt(currentWaveTexScale); // Anisotropic speed based on scale
        vec2 waveSampleUV1 = anisoUV + (baseWaveUV + accumulatedWaveUVOffset * 3.0 + accumulatedPhaseOffset) / currentWaveTexScale;
        vec3 sampledWaveNormalHeight1 = texture(g_tWavesNormalHeight, waveSampleUV1, -clampedLodFactor).xyz - vec3(0.5);

        float waveHeightComponent1 = sampledWaveNormalHeight1.z * freqWeight * length(currentWaveTexScale) * 0.01; // Height contribution
        vec2 waveNormalXYComponent1 = sampledWaveNormalHeight1.xy * 2.0; // Unpack normal

        waveNormalXYComponent1.x *= min(1.0, currentWaveTexScale.y / currentWaveTexScale.x);
        waveNormalXYComponent1.y *= min(1.0, currentWaveTexScale.x / currentWaveTexScale.y);
        waveNormalXYComponent1 *= (freqWeight * 0.1); // Scale normal contribution

        vec2 gerstnerDisplacement = (-viewParallaxFactor) * (waveHeightComponent1) * g_flWavesHeightOffset * currentWaterRoughness;
        vec2 waveNormalDisplacement = ((waveNormalXYComponent1 * g_flWavesSharpness) * currentWaveTexScale) * g_flWavesPhaseOffset;
        vec2 waveSampleUV2 = anisoUV + (baseWaveUV + (accumulatedWaveUVOffset + gerstnerDisplacement) * 3.0 + accumulatedPhaseOffset + waveNormalDisplacement) / currentWaveTexScale;

        vec3 sampledWaveNormalHeight2 = texture(g_tWavesNormalHeight, waveSampleUV2, -clampedLodFactor).xyz - vec3(0.5);

        vec2 waveNormalXYComponent2 = sampledWaveNormalHeight2.xy * 2.0;

        waveNormalXYComponent2.x *= min(1.0, currentWaveTexScale.y / currentWaveTexScale.x);
        waveNormalXYComponent2.y *= min(1.0, currentWaveTexScale.x / currentWaveTexScale.y);

        waveNormalXYComponent2 *= (freqWeight * 0.1);

        accumulatedWaveUVOffset += gerstnerDisplacement;

        // Accumulate normal
        accumulatedWaveNormal.xy += waveNormalXYComponent2;

        // Accumulate UV offset for next iteration (choppiness / parallax)
        // Parallax factor for wave displacement (view-dependent)

        currentWaveTexScale *= g_flWavesPhaseOffset; // e.g., smaller scale for higher frequency
        accumulatedPhaseOffset += waveNormalXYComponent1 * g_flWavesSharpness * currentWaveTexScale;

        float waveHeightComponent2 = sampledWaveNormalHeight2.z * freqWeight * length(currentWaveTexScale) * 0.01;
        // Accumulate height
        accumulatedWaveHeight += waveHeightComponent2; // Scale factor
        // Update parameters for next iteration

        currentWaveAngle += 3.5 / float(i + 1u); // Change angle to avoid repetition
    }

    vec2 finalWavePhaseOffset = accumulatedPhaseOffset * 0.1;
    vec3 roughedWaveNormal = accumulatedWaveNormal * currentWaterRoughness;
    float scaledAccumulatedWaveHeight = accumulatedWaveHeight * currentWaterRoughness * 60.0; // For stronger visual effect

    vec3 ditheredNormal = vec3(0.0, 0.0, 1.0);
    float edgeFactorQ = g_flEdgeShapeEffect;


    //---RECONSTRUCT WORLD NORMAL FROM DEPTH BUFFER
    if (F_REFRACTION == 1 && !g_bIsSkybox)
    {
        ditheredNormal = reconstructedWorldNormal;
        ditheredNormal.x = ditheredRefractShiftNormalXY.x;
        ditheredNormal.y = ditheredRefractShiftNormalXY.y;
        edgeFactorQ = g_flEdgeShapeEffect * saturate(fma(-reconstructedWorldNormal.z, 1.0 - saturate(refractedVerticalFactor * 8.0), 1.2));
    }
    vec3 waveDisplacedWorldPos = mat.PositionWS + viewDepOffsetFactor.xyz * (mix(0.5, scaledAccumulatedWaveHeight, g_flEdgeShapeEffect) - g_flWaterPlaneOffset) * 1;

    float finalFoamHeightContrib = scaledAccumulatedWaveHeight;
    float foamSiltFactor = 0.0;
    vec2 foamEffectDisplacementUV = vec2(0.0);
    vec2 debrisEffectsNormalXY = vec2(0.0);
    float foamFromEffects = 0.0;
    float debrisDisturbanceForWaves = g_flWaterEffectDisturbanceStrength * 0.25;
    float finalFoam = waterEffectsFoam;

    vec2 foamSiltEffectNormalXY = vec2(0.0);

    vec3 effectsSamplePos = mat.PositionWS.xyz + (viewDepOffsetFactor * (mix(0.5, scaledAccumulatedWaveHeight, edgeFactorQ) - g_flWaterPlaneOffset));

     // ----READ FROM EFFECTS MAP FOR DECAL BASED EFFECTS (shots, people running through water, etc...)
    if(!g_bIsSkybox)
    {
        mat4 transposedWorldToProj = transpose(g_matWorldToProjection);

        vec3 effectsPos0 = (mat.PositionWS + (viewDepOffsetFactor * (mix(0.0, scaledAccumulatedWaveHeight, edgeFactorQ) - g_flWaterPlaneOffset))) + (vec3(roughedWaveNormal.xy, 0.0) * (-16.0));

        vec4 effectsPos0Transformed = (vec4(effectsPos0 - g_vCameraPositionWs, 1.0)) * transposedWorldToProj;
        //TODO: Figure out what that shit before and if this is really ndc, I am using the naming straight from Gemini.
        vec2 effectsPos0NcdCoords = effectsPos0Transformed.xy / effectsPos0Transformed.w;
        //TODO: Do we need a GBuffer ratio? 1.0 is gbuffer ratio in decompile, but I am just setting 1 here.
        vec4 effectsSample0 = vec4(vec3(0.5), 0.0) - 0.5; //texture(g_tWaterEffectsMap,  ((vec2(effectsPos0NcdCoords.x, -effectsPos0NcdCoords.y) * 0.5) + vec2(0.5)).xy * 1.0    ) - vec4(0.5);

        vec3 effectsPos1 = effectsPos0 + (viewDepOffsetFactor * fma(20.0, effectsSample0.x, 2.0 * saturate(effectsSample0.yz * 2.0).x));
        vec4 effectsPos1Transformed = (vec4(effectsPos1.xyz, 1.0) - vec4(g_vCameraPositionWs, 1.0)).xyzw * transposedWorldToProj;
        vec2 effectsPos1NcdCoords = effectsPos1Transformed.xy / effectsPos1Transformed.w;
        //Same as before, gbuffer ratio??
        vec2 effectsPos1UV = ((vec2(effectsPos1NcdCoords.x, -effectsPos1NcdCoords.y) * 0.5) + vec2(0.5)).xy * 1.0;

        vec4 effectsSample1 = vec4(vec3(0.5), 0.0) - 0.5; //texture(g_tWaterEffectsMap, effectsPos1UV);


        vec2 rippleFoamFromEffectsMap = saturate(effectsSample1.yz*2.0);
        //float rippleBaseFromEffectsMap = rippleFoamFromEffectsMap.x;
        //float foamBaseFromEffectsMap = rippleFoamFromEffectsMap.y;

        vec4 _24771;
        _24771.z = rippleFoamFromEffectsMap.y;

        vec4 offsetClipPosX = (vec4(effectsPos1 - g_vCameraPositionWs +vec3(1,0,0) ,1.0))*transposedWorldToProj;
        vec4 offsetClipPosY = (vec4(effectsPos1 - g_vCameraPositionWs + vec3(0,-1,0),1.0))*transposedWorldToProj;

        vec2 offsetNdcX = offsetClipPosX.xy / offsetClipPosX.w;
        vec2 offsetNdcY = offsetClipPosY.xy / offsetClipPosY.w;
        //again gbuffer ratio
        vec2 duv_dx_approx = (( (vec2(offsetNdcX.x,-offsetNdcX.y) * 0.5 ) + 0.5 ) * 1.0 ) - effectsPos1UV;
        vec2 duv_dy_approx = (( (vec2(offsetNdcY.x,-offsetNdcY.y) * 0.5 ) + 0.5 ) * 1.0 ) - effectsPos1UV;
        vec2 stepScale = vec2(0.0004)/ vec2(length(duv_dx_approx),length(duv_dy_approx));

        vec4 xOffsetEffectsSample = vec4(vec3(0.5), 0.0) - 0.5; //texture(g_tWaterEffectsMap, effectsPos1UV * 1.0 + normalize(duv_dx_approx) * 0.005) - 0.5;
        vec4 yOffsetEffectsSample = vec4(vec3(0.5), 0.0) - 0.5; //texture(g_tWaterEffectsMap, effectsPos1UV * 1.0 + normalize(duv_dy_approx) * 0.005) - 0.5;

        vec2 rippleFoamDX = saturate(xOffsetEffectsSample.yz * 2.0);
        vec2 rippleFoamDY = saturate(yOffsetEffectsSample.yz * 2.0);


        foamEffectDisplacementUV = (normalize(cross(vec3(stepScale.x,0,effectsSample1.x - xOffsetEffectsSample.x),vec3(0,stepScale.y, effectsSample1.x - yOffsetEffectsSample.x))).xy*vec2(-1,1))*(abs(effectsSample1.x)*4.0) * g_flWaterEffectsRippleStrength;
        finalFoamHeightContrib += effectsSample1.x * g_flWaterEffectsRippleStrength * 12;
        foamSiltFactor = rippleFoamFromEffectsMap.y * g_flWaterEffectSiltStrength;

        debrisEffectsNormalXY = normalize(cross(vec3(stepScale.x,0,rippleFoamFromEffectsMap.x - rippleFoamDX.x),vec3(0.0, stepScale.y, rippleFoamFromEffectsMap.x - rippleFoamDY.x))).xy * vec2(-1.0, 1.0);

        foamFromEffects = rippleFoamFromEffectsMap.x * g_flWaterEffectFoamStrength;

        //outputColor.rgb = vec3(debrisEffectsNormalXY, 0);
        //return;

        //the decompile is so ridiculous here that I can't even put it into words
        //TODO: crosscheck with decompile again on _24771
        finalFoam = effectsSample1.z;
        debrisDisturbanceForWaves = ((effectsSample1.x + effectsSample1.y ) * g_flWaterEffectDisturbanceStrength) * 0.25;

        foamSiltEffectNormalXY = (normalize(cross(vec3(stepScale.x,0, effectsSample1.y -rippleFoamDX.y),vec3(0,stepScale.y, effectsSample1.y - rippleFoamDY.y))).xy*vec2(-1,1)) * pow(effectsSample1.y,3.5); // Original used rippleFoam_plusDY.y

        effectsSamplePos += (vec3(foamEffectDisplacementUV.xy, 0.0) * (-4.0));
    }

    vec3 rippleDisplacementAsVec3 = vec3(foamEffectDisplacementUV, 0.0);

    vec3 worldPosForFoamAndDebrisBase = (mat.PositionWS + (viewDepOffsetFactor * (mix(0.5, finalFoamHeightContrib, edgeFactorQ * 0.5) - g_flWaterPlaneOffset))) + (rippleDisplacementAsVec3 * (-2.0));

    vec2 foamWobbleAnim = vec2(sin(effectsSamplePos.y * 0.07 + timeAnim), cos(effectsSamplePos.x * 0.07 + timeAnim));
    vec2 foamBaseUV = (worldPosForFoamAndDebrisBase.xy / g_flFoamScale);

    vec2 foamWobbleEffect =  (foamBaseUV + finalWavePhaseOffset * g_flFoamWobble * 0.5 * (1.0 - currentFoamAmount))  - (foamSiltEffectNormalXY / g_flFoamScale);

    float foamNoiseStrength = finalFoam + 0.05;
    vec4 foamSample1 = texture(g_tFoam, mix(foamBaseUV, foamWobbleEffect + (foamWobbleAnim * foamNoiseStrength * 0.03), depthFactorFine) );
    vec2 sample2Mix1 = foamBaseUV.yx * 0.731;
    vec2 sample2Mix2 = (foamWobbleEffect.yx * 0.731) + ((vec2(sin(fma(effectsSamplePos.y, 0.06, timeAnim)), cos(fma(effectsSamplePos.x, 0.06, timeAnim))) * foamNoiseStrength) * 0.02);
    vec4 foamSample2 = texture(g_tFoam, mix(sample2Mix1, sample2Mix2, depthFactorFine)); // Second sample with different UVs/anim for variation

    float combinedFoamTextureValue = ( sin(blueNoise.x) * 0.125 + max(foamSample1.z, foamSample2.z) );

    float finalFoamIntensity = fma(    currentFoamAmount * fma(finalFoamHeightContrib, 0.008, 1.0),       1.0 - saturate(debrisDisturbanceForWaves * 2.0),       foamFromEffects   );
    finalFoamIntensity = saturate(finalFoamIntensity);

    float finalFoamPow1_5 = pow(finalFoam, 1.5);

    vec2 debrisBaseUV = worldPosForFoamAndDebrisBase.xy / g_flDebrisScale;
    vec2 debrisWobbleOffset = finalWavePhaseOffset * g_flDebrisWobble;

    float absFoamSiltX = abs(foamSiltEffectNormalXY.x);
    float absFoamSiltY = abs(foamSiltEffectNormalXY.y);
    float _15937 = foamSiltEffectNormalXY.y * float(absFoamSiltY > absFoamSiltX);
    vec2 dominantFoamSiltNorm = (vec2(foamSiltEffectNormalXY.x * float(absFoamSiltX > abs(_15937)), _15937) / g_flDebrisScale) * 400.0;
    vec2 debrisDistortedUV = ((debrisBaseUV + (debrisWobbleOffset * (1.0 - currentDebrisVisibility))));
    debrisDistortedUV += ((viewParallaxFactor * (fma(sin(finalFoam * 50.0) * 4.0, saturate(0.1 - finalFoamPow1_5), 1.0) * finalFoamPow1_5)) * 0.1);
    debrisDistortedUV +=  ((foamWobbleAnim * (0.1 + finalFoam)) * 0.02);
    debrisDistortedUV -=  dominantFoamSiltNorm;
    vec2 debrisFinalUV = mix(debrisBaseUV, debrisDistortedUV, depthFactorCoarse).xy;
    vec4 debrisColorHeightSample = texture(g_tDebris, debrisFinalUV, finalFoamPow1_5 * 3.0); // RGB=color, A=height/mask
    //outputColor.rgb = debrisColorHeightSample.rgb;
    float debrisHeightVal = debrisColorHeightSample.a - 0.5; // Signed height
    float finalDebrisVisibility = fma(-currentDebrisVisibility, clamp(1.4 - (finalFoam / mix(1.0, 0.4, debrisColorHeightSample.w)), 0.0, 1.0), 1.0);
    float debrisEdgeFactor = saturate((debrisColorHeightSample.a - finalDebrisVisibility) * g_flDebrisEdgeSharpness);
    float noClue = max(0.0, fma(2.0, finalFoamPow1_5, debrisHeightVal * (-2.0)));
    float debrisVisibilityMask = saturate(fma(-noClue, 10.0, 1.0));
    float finalDebrisFactor = debrisVisibilityMask * debrisEdgeFactor; // Final alpha for debris layer
    vec3 debrisNormalSample = texture(g_tDebrisNormal, debrisFinalUV).xyz - vec3(0.5); // Sample and un-pack
    debrisNormalSample.y *= -1.0;
    vec2 debrisNormalXY = debrisNormalSample.xy * g_flDebrisNormalStrength;
    float combinedfinalFoamIntensity = saturate(fma(-debrisVisibilityMask, debrisEdgeFactor, fma(finalFoamIntensity * combinedFoamTextureValue, 0.25, saturate(finalFoamIntensity - (1.0 - combinedFoamTextureValue)) * 0.75)));
    float finalDebrisFoamHeightContrib = mix(finalFoamHeightContrib, fma(finalFoamHeightContrib, 0.5, debrisHeightVal * 2.0), finalDebrisFactor);
    float weirdDebHeight = max(0.0, debrisHeightVal * (-2.0));
    float weirdMixVal = debrisEdgeFactor * saturate(fma(weirdDebHeight, 10.0, 1.0));

    mat.Height = mix(scaledAccumulatedWaveHeight, fma(scaledAccumulatedWaveHeight, 0.5, debrisHeightVal * 2.0), weirdMixVal);

    vec3 finalSurfacePos = mat.PositionWS.xyz + (viewDepOffsetFactor * (mix(0.5, scaledAccumulatedWaveHeight, edgeFactorQ) -g_flWavesHeightOffset));

    float finalWaterColumnDepthForRefract = waterColumnOpticalDepthFactor;

    if(!g_bIsSkybox)
    {
        finalSurfacePos = mat.PositionWS.xyz + (viewDepOffsetFactor * (mix(0.5, mat.Height, edgeFactorQ) - g_flWaterPlaneOffset)); // + (rippleDisplacementAsVec3) * (-12.0);

        float fmaM1 = ClampToPositive(   (   1.0 / fma(1.0, sceneNormalizedDepth, 0.0)   )     -  -(g_matWorldToView * vec4(finalSurfacePos.xyz, 1.0).xyzw).z);
        finalWaterColumnDepthForRefract = fma(fmaM1, 0.01, refractionDistortionFactor);
    }

    float surfaceCoverageAlpha = saturate(debrisEdgeFactor + combinedfinalFoamIntensity);

    vec2 finalWaveNormalXY = (((roughedWaveNormal.xy * 2.0) * g_flWavesNormalStrength) * mix(1.0, 2.0, reflectionsLodFactor)) * 1.0; // * mix(1.0, 2.0, reflectionMipBiasFactor); // Stronger at glancing angles

    finalWaveNormalXY *= fma(saturate(0.2 - finalWaterColumnDepthForRefract), 8.0, 1.0);
    finalWaveNormalXY += ((debrisNormalXY * finalDebrisFactor) * 1.5);
    finalWaveNormalXY += (mix(foamSample1.xy - vec2(0.5), foamSample2.xy - vec2(0.5), vec2(float(foamSample2.z > foamSample1.z))).xy * combinedfinalFoamIntensity);
    finalWaveNormalXY += ((debrisEffectsNormalXY.xy * combinedfinalFoamIntensity) * 0.5);
    finalWaveNormalXY += ((foamEffectDisplacementUV.xy * (1.0 - saturate(fma(debrisVisibilityMask, debrisEdgeFactor, combinedfinalFoamIntensity)))) * 2.0);
    finalWaveNormalXY *= (vec2(1.0) + ((blueNoiseOffset * 2.0) * g_flWavesNormalJitter));

    mat.NormalMap = vec3(finalWaveNormalXY, sqrt(1.0 - saturate(dot(finalWaveNormalXY, finalWaveNormalXY))));

    vec2 perturbedNormalXY = mat.NormalMap.xy * 3.0; // Stronger perturbation

    vec3 perturbedSurfaceNormal = vec3(perturbedNormalXY, sqrt(1.0 - saturate(dot(perturbedNormalXY, perturbedNormalXY))));

    vec3 finalPerturbedSurfaceNormal = perturbedSurfaceNormal;

    if (F_REFRACTION == 1 && !g_bIsSkybox)
    {
        float _20589 = mix(60.0, 120.0, ditheredNormal.z);
        vec3 edgeLimitFactor = vec3((clamp(fma(-sceneDepthChangeMagnitude, 1000.0, clamp(((1.0 / _20589) - finalWaterColumnDepthForRefract) * _20589, 0.0, 1.0) + saturate((0.025 - finalWaterColumnDepthForRefract) * 8.0)), 0.0, 1.0) / fma(distanceToFrag, 0.002, 1.0)) * 0.6);
        mat.NormalMap = normalize(mix(mat.NormalMap, ditheredNormal, edgeLimitFactor));
        finalPerturbedSurfaceNormal = normalize(mix(perturbedSurfaceNormal, ditheredNormal, edgeLimitFactor));

    }

    vec3 sunColor = GetLightColor(0);
    vec3 sunDir = GetEnvLightDirection(0);

    float cosNormAngle = saturate(dot(-viewDir, finalPerturbedSurfaceNormal.xyz));
    float fresnel = pow(1.0 - cosNormAngle, g_flFresnelExponent);
    vec3 finalFoamColor = g_vFoamColor.rgb * fma(combinedfinalFoamIntensity, 0.5, 1.0);

    vec3 combinedRefractedColor = vec3(0);
    vec4 causticsDebrisTotal = vec4(0.0);
    float causticsEffectsZ = 0.0;
    float postCausticsWaterColumnDepth = finalWaterColumnDepthForRefract;

    float foamSiltStrength = g_flWaterFogStrength;

    if(!g_bIsSkybox)
    {
        vec2 refractionUVOffsetRaw = (vec2(dot(finalPerturbedSurfaceNormal.xy, cross(-viewDir, vec3(0.0, 0.0, -1.0)).xy), dot(finalPerturbedSurfaceNormal.xy, -viewDir.xy)) + ((blueNoiseOffset * 0.002) * g_flWaterFogStrength)).xy * min(g_flRefractionLimit, finalWaterColumnDepthForRefract);
        float depthBufferRange = g_flViewportMaxZ - g_flViewportMinZ;
        float surfaceDepth = -(g_matWorldToView * vec4(finalSurfacePos, 1.0)).z;

        float normalizedDepth = clamp((textureLod(g_tSceneDepth, gbufferUV + refractionUVOffsetRaw.xy, 0.0).x - g_flViewportMinZ) / depthBufferRange, 0.0, 1.0);

        normalizedDepth = max(normalizedDepth, 0.0000001);
        float groundDepth = (1.0 / fma(1.0, normalizedDepth, 0.0 /*PsToVs*/));

        float waterExtent = groundDepth - surfaceDepth;
        //good ol DepthPsToVs
        float refractionOffsetAttenuation = clamp(fma(ClampToPositive(waterExtent), 0.01, refractionDistortionFactor) * 10.0, 0.0, 1.0);

        vec2 finalRefractionUVOffset = refractionUVOffsetRaw * refractionOffsetAttenuation;


        float finalRefractedNormalizedDepth = (texture(g_tSceneDepth, gbufferUV + finalRefractionUVOffset).x - g_flViewportMinZ) / depthBufferRange;
        finalRefractedNormalizedDepth = max(finalRefractedNormalizedDepth, 0.0000001);

        //vec4 finalRefractedColor = texture(g_tSceneColor, saturate(gbufferUV + finalRefractionUVOffset));

        #if F_BLUR_REFRACTION == 1
            float smallOffset = 0.001 * ClampToPositive(waterExtent) * 0.01;
            vec4 sample1 = texture(g_tSceneColor, clamp(gbufferUV + (finalRefractionUVOffset * (1.0 + g_flRefractSampleOffset * ClampToPositive(waterExtent) * 0.01 * 0.0)) + vec2(0.0, smallOffset), vec2(0.0), vec2(1.0)));
            vec4 sample2 = texture(g_tSceneColor, clamp(gbufferUV + (finalRefractionUVOffset * (1.0 + g_flRefractSampleOffset * ClampToPositive(waterExtent) * 0.01 * 1.0)), vec2(0.0), vec2(1.0)));
            vec4 sample3 = texture(g_tSceneColor, clamp(gbufferUV + (finalRefractionUVOffset * (1.0 + g_flRefractSampleOffset * ClampToPositive(waterExtent) * 0.01 * 2.0)) - vec2(0.0, smallOffset), vec2(0.0), vec2(1.0)));
            vec4 sample4 = texture(g_tSceneColor, clamp(gbufferUV + (finalRefractionUVOffset * (1.0 + g_flRefractSampleOffset * ClampToPositive(waterExtent) * 0.01 * 3.0)) + vec2(smallOffset, 0.0), vec2(0.0), vec2(1.0)));
            vec4 sample5 = texture(g_tSceneColor, clamp(gbufferUV + (finalRefractionUVOffset * (1.0 + g_flRefractSampleOffset * ClampToPositive(waterExtent) * 0.01 * 4.0)) - vec2(smallOffset, 0.0), vec2(0.0), vec2(1.0)));


            // 4. Mix the color channels from different samples to create the aberration
            // This is a direct translation of the fma chain in the original shader
            vec4 mixedColor =
            (sample5 * mix(vec4(1.0), vec4(2.0, 0.0, 0.0, 1.0), g_flRefractChromaticSeparation)) +
            (sample4 * mix(vec4(1.0), vec4(2.0, 2.0, 0.0, 1.0), g_flRefractChromaticSeparation)) +
            (sample3 * mix(vec4(1.0), vec4(0.0, 3.0, 0.0, 1.0), g_flRefractChromaticSeparation)) +
            (sample2 * mix(vec4(1.0), vec4(1.0, 0.0, 2.0, 1.0), g_flRefractChromaticSeparation)) +
            (sample1 * mix(vec4(1.0), vec4(0.0, 0.0, 3.0, 1.0), g_flRefractChromaticSeparation));

            vec4 finalRefractedColor = mixedColor * 0.2; // Average the 5 samples
        #else
            vec4 finalRefractedColor = texture(g_tSceneColor, saturate(gbufferUV + finalRefractionUVOffset));
        #endif // F_BLUR_REFRACTION == 1
        //finalRefractedColor = texture(g_tSceneColor, saturate(gbufferUV + finalRefractionUVOffset));

        vec3 darkenedRefractedColor = pow(finalRefractedColor.rgb, vec3(1.1)) * g_flUnderwaterDarkening;

        foamSiltStrength += foamSiltFactor * 2.0;

        float causticVisibility = clamp((GetLuma(darkenedRefractedColor.xyz) - g_flCausticShadowCutOff) * (2.0 + g_flCausticShadowCutOff), 0.0, 1.0);

        //VALIDATE: foamSiltStrength and causticVisibility. Make sure they match original!
        combinedRefractedColor = darkenedRefractedColor;

        if(F_CAUSTICS == 1 && causticVisibility > 0.0)
        {
            vec3 refractedViewDir = (-normalize((-viewDir + ((g_vCameraUpDirWs * finalRefractionUVOffset.y) * 2.0)) + ((cross(g_vCameraDirWs, g_vCameraUpDirWs) * (-finalRefractionUVOffset.x)) * 2.0))).xyz;
            //MATCH?: Replaced a whole couple of things here. Seems to be correct though.
            float invProjTerm = fma(sceneNormalizedDepth, g_vInvProjRow3.z, g_vInvProjRow3.w);
            float perspectiveCorrection = dot(g_vCameraDirWs, viewDir);
            float sceneViewDistance = (1.0 / (invProjTerm * perspectiveCorrection));

            vec3 refractedSceneHitPosWs = g_vCameraPositionWs + normalize(refractedViewDir) * sceneViewDistance;

            vec3 causticsLightDir = sunDir;

            if(g_bUseTriplanarCaustics == 1)
            {
                vec3 ditheredNormalExtent = abs(ditheredNormal);
                causticsLightDir = mix(sunDir, mix(mix(vec3(0.0, 1.0, 1.0), vec3(1.0, 0.0, 1.0), vec3(ditheredNormalExtent.y < ditheredNormalExtent.x)), vec3(0.0, 0.0, 1.0), bvec3(ditheredNormalExtent.z > max(ditheredNormalExtent.x, ditheredNormalExtent.y))), vec3(0.65));
            }

            float causticsDepth = worldPosForFoamAndDebrisBase.z - refractedSceneHitPosWs.z;
            vec3 causticRayTarget =  mix(refractedSceneHitPosWs + ((causticsLightDir.xyz * causticsLightDir.z) * causticsDepth), finalSurfacePos.xyz, vec3(clamp((pow(blueNoise.x, 2.0) * foamSiltStrength) * 0.0125, 0.0, 1.0)));
            float distToCausticTarget = distance(causticRayTarget, refractedSceneHitPosWs);

            vec2 causticDebrisUV = causticRayTarget.xy / g_flDebrisScale;
            vec4 causticDebrisSample = texture(g_tDebris, mix(causticDebrisUV, (((causticDebrisUV + (debrisWobbleOffset * finalDebrisVisibility)) + ((viewParallaxFactor * noClue) * 0.1)) + ((foamWobbleAnim * 0.1) * 0.04)) - dominantFoamSiltNorm, depthFactorCoarse).xy, causticsDepth * 0.05);
            float causticDebrisCoverage = clamp((fma(-finalDebrisVisibility, 0.9, causticDebrisSample.w) - debrisEdgeFactor) * 1.1, 0.0, 1.0);

            float causticDepthFalloffPre = distToCausticTarget / g_flCausticDepthFallOffDistance;
            float causticDepthFalloff = saturate(1.0 - causticDepthFalloffPre);
            float causticBaseIntensity = (causticVisibility * saturate(distToCausticTarget * 0.05)) * causticDepthFalloff;

            if (g_bUseTriplanarCaustics == 0)
            {
                causticBaseIntensity *= saturate(dot(ditheredNormal, causticsLightDir.xyz));
            }

            vec2 causticWaveUVBase = (causticRayTarget.xy * vec2(1.0 / 30)) * g_flCausticUVScaleMultiple;

            vec2 currWaveScale = g_vWaveScale.xy;
            vec2 currWaveNormalXY = vec2(0);
            float currWaveDir = g_flWaterInitialDirection;

            for(int i = 0; i < 3; i++)
            {
                vec2 localUV =
                fma(
                    vec2(sin(currWaveDir), cos(currWaveDir)) * ((g_flTime * g_flWavesSpeed) * 0.5),
                    sqrt(vec2(1.0) / currWaveScale),
                    (causticWaveUVBase.xy + currWaveNormalXY) / currWaveScale).xy;

                float lodOffset = fma(-g_flCausticSharpness, causticDepthFalloff, 1.0) * 6.0;

                vec3 rawSample = texture(g_tWavesNormalHeight, localUV, lodOffset).xyz;

                currWaveNormalXY.xy += (((((rawSample - vec3(0.5)).xy * 0.5) * g_flCausticDistortion) * (vec2(1.0) + currWaveScale)) * (0.25 + causticDepthFalloffPre));

                currWaveScale *= g_flWavesPhaseOffset;
                currWaveDir += (3.5 / (i + 1));
            }

            vec2 currWaveScale1 = g_vWaveScale.xy;
            float currWaveDir1 = g_flWaterInitialDirection;
            vec3 currWaveSampleSum1 = vec3(0.0);

            for(int i = 0; i < 3; i++)
            {
                float causticIterProgress = float(i) / (float(g_nWaveIterations) - 1.0);

                vec2 uv = fma(
                    vec2(sin(currWaveDir1), cos(currWaveDir1)) * ((g_flTime * g_flWavesSpeed) * 0.5),
                    sqrt(vec2(1.0) / currWaveScale1),
                    (causticWaveUVBase.xy + currWaveNormalXY) / currWaveScale1).xy;

                float lodOffset = fma(-g_flCausticSharpness, causticDepthFalloff, 1.0) * 6.0;
                vec3 rawSample = vec3(texture(g_tWavesNormalHeight, uv, lodOffset).z);
                vec3 exponent = vec3(causticDepthFalloff * g_flCausticSharpness * 5.0);
                float factor = clamp(mix(mix(fma(debrisDisturbanceForWaves, 0.1, g_flLowFreqWeight), g_flMedFreqWeight + debrisDisturbanceForWaves, saturate(causticIterProgress * 2.0)), fma(g_flHighFreqWeight, currentWaterRoughness, debrisDisturbanceForWaves), saturate(fma(causticIterProgress, 2.0, -1.0))), 0.1, 0.4);

                float waveSampleCausticDepthFalloff = causticDepthFalloff * g_flCausticSharpness;
                currWaveSampleSum1 += (((((pow(rawSample, exponent) * factor) * (vec3(1.0) + (currWaveSampleSum1 * 2.0))) * causticDepthFalloff) * waveSampleCausticDepthFalloff) * 2.0);

                currWaveScale1 *= g_flWavesPhaseOffset;
                currWaveDir += (3.5 / (i + 1));
            }


            vec3 subPart = (causticRayTarget.xyz + ((vec3(currWaveNormalXY, 0.0) * 60.0) * currWaveSampleSum1.x));
            subPart -= g_vCameraPositionWs * 1.0;
            vec4 causticsClipPos = vec4(subPart, 1.0) * transpose(g_matWorldToProjection);
            vec2 causticsNdc = causticsClipPos.xy / causticsClipPos.w;

            //TODO: should the y be flipped here? should depend on texture convention, no?
            vec2 causticsUV = vec2(causticsNdc.x, -causticsNdc.y) * 0.5 - 0.5;
            vec4 causticsEffectsSampleRaw = vec4(vec3(0.5), 0.0) - 0.5; //texture(g_tWaterEffectsMap, causticsUV) - 0.5;
            vec2 causticsClampedESampleXY = saturate(causticsEffectsSampleRaw.yz * 2.0);

            vec4 finalCausticsEffectsSample = causticsEffectsSampleRaw;
            finalCausticsEffectsSample.y = causticsClampedESampleXY.x;
            finalCausticsEffectsSample.z = causticsClampedESampleXY.y;

            vec4 fadedCausticsEffects = finalCausticsEffectsSample * clamp((((causticsUV.y * (1.0 - causticsUV.y)) * causticsUV.x) * (1.0 - causticsUV.x)) * 40.0, 0.0, 1.0);

            float causticsXOverChangerate = fadedCausticsEffects.x + (fadedCausticsEffects.x / fma(fwidth(fadedCausticsEffects.x), 1000.0, 0.5));

            vec3 causticsModifier = (currWaveSampleSum1 + vec3(fma(saturate(causticsXOverChangerate) * 4.0, g_flWaterEffectCausticStrength, -((saturate(-causticsXOverChangerate) * 0.15) * g_flWaterEffectCausticStrength)))) * mix(1.0, 0.0, saturate(fma(causticDebrisCoverage, 2.0, fadedCausticsEffects.y * 0.4)));
            float causticsModifierX = causticsModifier.x;

            //minor readability improvement
            vec3 powA = max(causticsModifier * (vec3(1.0) + (vec3(1.25, -0.25, -1.0) * (clamp(dFdxFine(causticsModifierX) * 200.0, -1.0, 1.0) * saturate(fma(-causticsModifierX, 3.0, 1.0))))), vec3(0.001)) * 8.0;
            vec3 modifiedCausticsRefractColor = darkenedRefractedColor * (vec3(1.0) + (((((pow(powA, vec3(2.5)) * causticBaseIntensity) * sunColor) * g_vCausticsTint.xyz) * g_flCausticsStrength) * 0.1));

            float _16517 = pow(GetLuma(modifiedCausticsRefractColor), 0.2);
            float _14717 = clamp(dFdxFine(_16517), -1.0, 1.0) + clamp(-dFdyFine(_16517), -1.0, 1.0);

            causticsDebrisTotal.w = causticDebrisCoverage;
            combinedRefractedColor = mix(modifiedCausticsRefractColor, modifiedCausticsRefractColor * (vec3(1.0) + (vec3(2.5, 0.0, -2.0) * float(int(sign(_14717 * saturate(abs(_14717) - 0.1)))))), vec3(saturate(200.0 / relFragPos) * 0.1));
            causticsEffectsZ = fadedCausticsEffects.z;
        }

        postCausticsWaterColumnDepth = fma(ClampToPositive( ( 1.0 / finalRefractedNormalizedDepth) - surfaceDepth), 0.01, refractionDistortionFactor);
    }

    float effectiveWaterDepthForFog = min(g_flWaterMaxDepth, postCausticsWaterColumnDepth);
    vec3 waterDecayColorFactor = exp(((g_vWaterDecayColor.rgb - vec3(1.0)) * vec3(g_flWaterDecayStrength)) * effectiveWaterDepthForFog);
    float totalFogStrength = max(foamSiltStrength, causticsEffectsZ);
    float foamDebrisForFogMix = finalFoamIntensity + saturate(causticsEffectsZ - 0.5);
    float waterFogAlpha = fma(fma(-saturate(blueNoise.x), 0.25, foamDebrisForFogMix), 0.1, 1.0 - exp((-effectiveWaterDepthForFog) * totalFogStrength));
    vec3 baseFogColor = mix(g_vWaterFogColor.rgb, finalFoamColor, vec3(foamDebrisForFogMix * 0.1)) * mix(waterDecayColorFactor, vec3(1.0), vec3(saturate(totalFogStrength * 0.04)));

    vec3 finalDirToCam = -normalize(finalSurfacePos.xyz - g_vCameraPositionWs.xyz);
    float specularCosAlpha = clamp(dot(-sunDir, reflect(finalDirToCam, normalize(mix(normalize(mat.GeometricNormal).xyz, finalPerturbedSurfaceNormal.xyz, vec3(g_flSpecularNormalMultiple * fma(distanceToFrag, 0.0005, 1.0)))))), 0.0, 1.0);
    float specularExponent = mix(g_flSpecularPower, g_flDebrisReflectance * 8.0, debrisEdgeFactor) * mix(2.0, 0.2, saturate(currentWaterRoughness));
    float specularFactor = fma(pow(specularCosAlpha, specularExponent), 0.1, pow(specularCosAlpha, specularExponent * 10.0));

    float inverseWaterFogAlpha = 1.0 - waterFogAlpha;
    float waterOpacity = (saturate((1.0 - debrisEdgeFactor) + noClue) * saturate(fma(-combinedfinalFoamIntensity, 4.0, 1.0))) * inverseWaterFogAlpha;

    vec3 lightingSamplePos = mat.PositionWS.xyz + (((-viewDepOffsetFactor) * (vec3(finalDebrisFoamHeightContrib * (-1.0)) + (((mix(blueNoise.xxx, vec3(blueNoise.xy, 0.0), vec3(0.1)) * 90.0) * pow(waterOpacity, 2.0)) + vec3(g_flWaterPlaneOffset)))) * mix(1.0, effectiveWaterDepthForFog * 2.0, 0.75));

    float squaredWaterOpacity = pow(waterOpacity, 2.0);
    float _12400 = mix(1.0, effectiveWaterDepthForFog * 2.0, 0.75);

    // todo: we should be using built-in baked lighting functions

    #if (D_BAKED_LIGHTING_FROM_LIGHTMAP > 0 && S_LIGHTMAP_VERSION_MINOR >= 2)

        vec3 ditheredLightmapUV = vec3(vLightmapUVScaled.xy + (((((((fwidth(vLightmapUVScaled.xy) * 1200.0) / vec2(distanceToFrag)) * cosNormAngle) * (-viewParallaxFactor)) * vec2(-1.0, 1.0)) * (vec2(finalDebrisFoamHeightContrib * (-2.0)) + ((mix(blueNoise.yy, blueNoise.yx, vec2(0.1)) * 20.0) * squaredWaterOpacity))) * _12400), 0.0).xyz;

        //Calculating baked lighting
        vec3 bakedShadow = texture(g_tDirectLightShadows, ditheredLightmapUV).rgb;
        vec3 bakedIrradiance = texture(g_tIrradiance, ditheredLightmapUV).rgb;

        if(true)
        {
            #if (S_LIGHTMAP_VERSION_MINOR >= 3)
            vec4 vAHDData = texture(g_tDirectionalIrradianceR, ditheredLightmapUV);
            #else
            vec4 vAHDData = texture(g_tDirectionalIrradiance, ditheredLightmapUV);
            #endif

            bakedIrradiance = ComputeLightmapShading(bakedIrradiance, vAHDData, mat.NormalMap);
        }
    #else
        vec3 bakedShadow = vec3(1.0);
        vec3 bakedIrradiance = vec3(0.5);
    #endif

    vec3 ambientTerm = bakedIrradiance;
    float finalShadowCoverage = CalculateSunShadowMapVisibility(lightingSamplePos);

    float lightmapShadowMulti = 1.0 - dot(bakedShadow, vec3(1.0, 0, 0));

    float finalShadowingEffect = mix(finalShadowCoverage * lightmapShadowMulti, lightmapShadowMulti, waterOpacity * 0.5);
    vec3 lightingFactor = vec3(0.0);

    if ((dot(sunDir, mat.NormalMap.xyz) * finalShadowingEffect) > 0.0)
    {
        lightingFactor = vec3(ClampToPositive(dot(mat.NormalMap, sunDir))) * (sunColor * finalShadowingEffect).xyz;
    }

    vec3 _22686 = (lightingFactor.xyz + bakedIrradiance) * mix(mix((baseFogColor * waterFogAlpha) * g_flWaterFogShadowStrength, finalFoamColor.xyz, vec3(combinedfinalFoamIntensity)), vec4(debrisColorHeightSample.xyz * fma(finalDebrisFactor, 0.5, 0.5), debrisEdgeFactor).xyz * g_vDebrisTint.xyz, vec3(saturate(debrisEdgeFactor - noClue))).xyz;

    outputColor.rgb = vec3(vec3(saturate(debrisEdgeFactor - noClue)));

    vec3 returnColor = mix(_22686, combinedRefractedColor * waterDecayColorFactor, vec3(waterOpacity)); // + float(specularFactor > 0.5) * sunColor;
    returnColor = mix(returnColor, (baseFogColor * 4.0) * bakedIrradiance, vec3((waterFogAlpha * saturate((1.0 - surfaceCoverageAlpha) + noClue)) * (1.0 - g_flWaterFogShadowStrength)));

    outputColor.rgb = returnColor;

    outputColor.rgb = vec3(saturate((1.0 - surfaceCoverageAlpha) + noClue));

    mat.SpecularColor = vec3(1);

    float roughnessForCubemap = dot(mix(g_vRoughness,vec2(1),vec2(clamp(reflectionsLodFactor, 0.0 ,0.35))), vec2(0.5) );
    mat.Roughness = vec2(sqrt(roughnessForCubemap));

    vec3 tempSurfNormal = finalPerturbedSurfaceNormal;
    tempSurfNormal = mat.NormalMap;

    if(true)
    {
        tempSurfNormal.xy *= 6.0;
        tempSurfNormal = mat3(g_matWorldToView) * normalize(tempSurfNormal);
        //tempSurfNormal = (g_matWorldToView * vec4(normalize(tempSurfNormal), 0.0)).xyz;
        tempSurfNormal.yz *= 2;
        tempSurfNormal = transpose(mat3(g_matWorldToView)) * normalize(tempSurfNormal);
    }
    else
    {
        tempSurfNormal.xy *= 6.0;
    }

    float reflectionBlendFactor = saturate(fma(-roughnessForCubemap, roughnessForCubemap, 1.0));

    mat.AmbientNormal = tempSurfNormal;

    vec3 reflectedRay = reflect(viewDir, tempSurfNormal);

    vec3 reflectedNormalDone = normalize(mix(tempSurfNormal, reflectedRay, vec3(reflectionBlendFactor * fma(roughnessForCubemap, roughnessForCubemap, sqrt(reflectionBlendFactor)))));

    vec3 finalReflectionColor = F_REFLECTION_TYPE > 0
        ? GetEnvironmentNoBRDF(mat, 0.0) * g_flEnvironmentMapBrightness
        : SrgbGammaToLinear(g_vSimpleSkyReflectionColor.rgb);

    float SSRStepCountMultiplier = saturate((g_vCameraDirWs.z + 0.75) * 4.0) * (0.5 + 0.5 * float(!g_bIsSkybox));
    int SSRStepCount = int(g_nSSRMaxForwardSteps * SSRStepCountMultiplier);

    if(F_REFLECTION_TYPE == 2 && SSRStepCount > 0)
    {
        float SsrHitThickness = fma(blueNoiseDitherFactor, g_flSSRSampleJitter, g_flSSRMaxThickness);

        mat4 transWorldToView = transpose(g_matWorldToView);
        mat4 transViewToProj = transpose(g_matViewToProjection);
        vec4 SSNormal4f = g_matWorldToView * vec4(normalize(vec3((mat.NormalMap.xy * 3.0) * mix(2.0, 8.0, float(g_bIsSkybox)), mat.NormalMap.z)), 0.0);

        vec3 viewSpacePos = (g_matWorldToView * vec4(finalSurfacePos.xyz, 1.0)).xyz;

        vec3 SSNormal = SSNormal4f.xyz;
        SSNormal.yz *= 2;

        vec4 _15818 = g_matViewToProjection * vec4(-viewSpacePos.xyz, 1.0); //transViewToProj * vec4(-viewSpacePos, 1.0);

        vec3 baseNdcCoords = _15818.xyz / vec3(_15818.w);

        vec2 baseSsrUV = baseNdcCoords.xy * 0.5 + 0.5;
        outputColor.rgb = vec3(baseSsrUV, 0.0);


        float initialStepSize = (fma(blueNoiseDitherFactor, g_flSSRSampleJitter, g_flSSRStepSize) / fma(reflectionsLodFactor, 2.0, 1.0)) * mix(20.0, 1.0, cosNormAngle);


        float baseStepSize = initialStepSize;
        if (g_bIsSkybox)
        {
            baseStepSize = initialStepSize * (distanceToFrag * 0.002);
        }

        vec3 SSReflectDir = normalize(reflect(normalize(viewSpacePos), normalize(SSNormal))).xyz;

        vec3 prevSamplePos = viewSpacePos;
        vec2 SsrUVCoords = baseSsrUV;
        vec2 finalSsrUVCoords;
        float currStepSize;
        float currSampleWorldDepth;
        float prevWorldDepth = 0.0;
        vec3 currSamplePos;
        float prevStepSize = baseStepSize;
        float fractionalSampleCount;
        float finalPrevCurrFrac = 0.0;
        float prevPrevCurrFrac = 0.0;
        float prevCurrFrac;
        int i = 1;
        for(;i <= SSRStepCount; i++)
        {
            currStepSize = prevStepSize * 1.15;
            currSamplePos = prevSamplePos + SSReflectDir * currStepSize;
            vec4 currViewSpacePos = g_matViewToProjection * vec4(-currSamplePos, 1.0);
            vec3 _10510 = currViewSpacePos.xyz / vec3(currViewSpacePos.w);
            vec2 currSsrUV = (vec2(_10510.x, _10510.y) * 0.5) + vec2(0.5);
            vec4 _20493;
            float currNormalizedDepth = (textureLod(g_tSceneDepth, currSsrUV.xy, 0.0).x - g_flViewportMinZ) / (g_flViewportMaxZ - g_flViewportMinZ);

            currNormalizedDepth = max(currNormalizedDepth, 0.0000001);

            currSampleWorldDepth = (-1.0 / currNormalizedDepth - currSamplePos.z);

            //outputColor.rgb = vec3(currSampleWorldDepth - 20);
            //return;

            prevCurrFrac = saturate(currSampleWorldDepth / (currSampleWorldDepth - prevWorldDepth));

            bool hasHit = false;
            if (currSampleWorldDepth >= 0.0)
            {
                outputColor.rgb = vec3(length(currSamplePos) - 300);
                //return;

                if(currSampleWorldDepth < (SsrHitThickness * currStepSize) )
                {
                    fractionalSampleCount = prevCurrFrac;
                    finalSsrUVCoords = mix(currSsrUV, SsrUVCoords, vec2(prevCurrFrac));


                    break;
                }
            }
            SsrUVCoords = currSsrUV;

            prevWorldDepth = currSampleWorldDepth;
            prevSamplePos = currSamplePos;
            prevStepSize = currStepSize;
        }
        float fracOfTotalSteps = (float(i) - fractionalSampleCount) / float(SSRStepCount);
        vec3 SsrReflectionResult;

        outputColor.rgb = vec3(10.0, 0.0, 0.0);
        //return;

        if (!g_bIsSkybox)
        {
            vec2 scaledSsrUVs = finalSsrUVCoords; // * PerViewConstantBuffer_t.g_vViewportToGBufferRatio.xy
            float _8505 = fracOfTotalSteps * (-0.00390625);
            vec3 SsrColorSample = ((
            (texture(g_tSceneColor, clamp(scaledSsrUVs + vec2(_8505), vec2(0.0), vec2(1.0)).xy).xyz * 0.444444) +
            (texture(g_tSceneColor, clamp(scaledSsrUVs + vec2(0.001953125, _8505), vec2(0.0), vec2(1.0)).xy).xyz * 0.222222)) +
            (texture(g_tSceneColor, clamp(scaledSsrUVs + vec2(_8505, 0.001953125), vec2(0.0), vec2(1.0)).xy).xyz * 0.222222)) +
            (texture(g_tSceneColor, clamp(scaledSsrUVs + vec2(0.001953125), vec2(0.0), vec2(1.0)).xy).xyz * 0.111111);

            SsrReflectionResult = (SsrColorSample + ((normalize(SsrColorSample + vec3(0.001)) * ClampToPositive(GetLuma(SsrColorSample.xyz) - g_flSSRBoostThreshold)) * g_flSSRBoost)) * g_flSSRBrightness;
        }
        else
        {
            SsrReflectionResult = mix((returnColor.xyz + finalReflectionColor) * 0.5, finalReflectionColor, vec3(fracOfTotalSteps));
        }

        finalReflectionColor = mix(finalReflectionColor, SsrReflectionResult, vec3(saturate(1.0 - pow(fracOfTotalSteps, 4.0)) * saturate((1.0 - finalSsrUVCoords.y) * 8) ));
    }

    float localReflectance = mix(g_flReflectance, g_flDebrisReflectance, finalDebrisFactor);
    float reflectionModulation = (fma(fresnel, 1.0 - localReflectance, localReflectance) * fma(-combinedfinalFoamIntensity, 2.0, fma(-surfaceCoverageAlpha, 0.75, 1.0))) * 1.5;
    returnColor = fma((lightingFactor.xyz * (fma(ClampToPositive(specularFactor - (1.0 - g_flSpecularBloomBoostThreshold)), g_flSpecularBloomBoostStrength, specularFactor) * mix(1.0, g_flDebrisReflectance * 0.05, debrisEdgeFactor))) * reflectionModulation, sunColor, returnColor.xyz);

    float oilIridHue = fract(fma(g_flTime, 0.1, fma(fresnel, 20.0, debrisHeightVal * 8.0)));
    vec3 oilIridescence = calculateIridescence(oilIridHue, 0.75);

    returnColor = returnColor.xyz * mix(vec3(1.0), ambientTerm * 0.75, vec3(saturate(causticsDebrisTotal.w * 4.0) * inverseWaterFogAlpha));

    vec3 returnColorMixFac = vec3(saturate(reflectionModulation));
    vec3 secondaryColorMixFac = vec3(((saturate(noClue * 20.0) * g_flDebrisOilyness) / fma(distanceToFrag, 0.005, 1.0)) * saturate(fma(-refractedVerticalFactor, 5.0, 1.0)));

    returnColor = mix(returnColor, mix(finalReflectionColor, finalReflectionColor * oilIridescence, secondaryColorMixFac), returnColorMixFac);

    ApplyFog(returnColor, finalSurfacePos);

    // --- DITHER INTO SKYBOX ---
    if (!g_bIsSkybox)
    {
        vec2 _3206 = abs(vec2(0.5) - unbiasedUV) * 2.0;
        if ((saturate(1.0 - saturate((max(_3206.x, _3206.y) - (1.0 - g_flSkyBoxFadeRange)) / g_flSkyBoxFadeRange)) - blueNoise.x) < 0.0)
        {
            discard;
        }
    }

    float flEdgeBlend = saturate(fma(g_flEdgeHardness, effectiveWaterDepthForFog, saturate(combinedfinalFoamIntensity)) + fma(debrisHeightVal, 2.0, -0.5));
    mat.ExtraParams.x = 1.0 - flEdgeBlend;

    // --- PERFORM EDGE BLEND ---
    if (F_REFRACTION == 1 && !g_bIsSkybox)
    {
        returnColor = vec3(mix((refractionColorSample.xyz * mix(1.0, 0.6, saturate(refractedVerticalFactor * 60.0) / fma(distanceToFrag, 0.002, 1.0))).xyz, returnColor.xyz, vec3(flEdgeBlend)));
    }

    outputColor.rgb = returnColor;

    if (!HandleMaterialRenderModes(outputColor, mat))
    {
        if (g_iRenderMode == renderMode_Cubemaps)
        {
            outputColor.rgb = finalReflectionColor;
        }
        else if (g_iRenderMode == renderMode_Height)
        {
            outputColor.rgb = SrgbGammaToLinear(mat.Height.xxx);
        }
        else if (g_iRenderMode == renderMode_TerrainBlend)
        {
            outputColor.rgb = SrgbGammaToLinear(vColorBlendValues.xyz);
        }
    }
}
