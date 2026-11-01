#version 460

#include "csgo_water_fancy_features.slang"

#include "../common/utils.slang"
#include "../common/shared_combos.slang"
#include "../common/view_constants.slang"
#include "../common/lighting_constants.slang"

in vec3 vFragPosition;
in vec2 vTexCoordOut;
in vec3 vNormalOut;
in vec4 vTangentOut;
in vec4 vColorBlendValues;

#include "../common/lighting_common.slang"
#include "../common/fullbright.slang"
#include "../common/texturing.slang"
#include "../common/fog.slang"
#include "../common/lighting.slang"
#include "../common/depth.slang"

out vec4 outputColor;

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

uniform vec4 g_vViewportExtentsTs;

uniform sampler2D g_tBlueNoise; // reserved
uniform sampler2D g_tFoam;
uniform sampler2D g_tDebris; // SrgbRead(true) RepresentativeTexture
uniform sampler2D g_tDebrisNormal;

uniform sampler2D g_tWaterEffectsMap; // reserved
uniform sampler2D g_tWavesNormalHeight;

uniform vec4 g_vSimpleSkyReflectionColor = vec4(1.0); // SrgbRead(true)

uniform sampler2D g_tSceneColor; // reserved

vec2 WaterEffectsUV(vec4 clipPos)
{
    return clipPos.w > 1e-4 ? PackToColor(clipPos.xy / clipPos.w) : vec2(-1.0);
}

// The view of the water surface. The skybox copy is scaled up and is not offset by the view depth.
struct WaterView_t
{
    vec3 Dir;
    vec3 DirInv; // equivalent to mat.ViewDir
    vec2 ParallaxFactor;
    float SkyboxScale;
    float DistanceToFrag;
    vec3 DepthOffsetFactor;
    vec2 GbufferUV;
};

WaterView_t GetWaterView(vec3 positionWs)
{
    vec3 positionViewSpace = positionWs - g_vCameraPositionWs;

    WaterView_t view;
    view.Dir = normalize(positionViewSpace);
    view.DirInv = -view.Dir;
    view.ParallaxFactor = view.Dir.xy / (view.DirInv.z + 0.25);
    view.SkyboxScale = g_bIsSkybox ? g_flSkyBoxScale : 1.0;
    view.DistanceToFrag = length(positionViewSpace) * view.SkyboxScale;
    view.DepthOffsetFactor = mix(vec3(view.Dir.xy / view.Dir.z, sqrt(ClampToPositive(view.DirInv.z))), vec3(0.0), vec3(g_bIsSkybox));
    view.GbufferUV = gl_FragCoord.xy / textureSize(g_tSceneColor, 0);
    return view;
}

// What is behind the water surface, as the scene color and depth buffers have it
struct SceneBehind_t
{
    float RefractionDistortion;
    float ColumnDepthFactor;
    vec4 Color;
    float NormalizedDepth;
    vec3 HitPositionWs;
};

SceneBehind_t SampleSceneBehind(WaterView_t view, vec3 positionWs)
{
    SceneBehind_t scene;
    scene.RefractionDistortion = 0.0;
    scene.ColumnDepthFactor = 1.0;
    scene.Color = vec4(0.0);
    scene.NormalizedDepth = 1.0;
    scene.HitPositionWs = vec3(0.0);

    if (g_bIsSkybox)
    {
        return scene;
    }

    float sceneDepth = textureLod(g_tSceneDepth, view.GbufferUV, 0.0).x;
    scene.NormalizedDepth = max(LinearRamp(g_flViewportMinZ, g_flViewportMaxZ, sceneDepth), 0.00001);
    scene.Color = textureLod(g_tSceneColor, view.GbufferUV, 0);

    float refractionLuminance = clamp(GetLuma(scene.Color.rgb), 0.0, 0.4);
    scene.RefractionDistortion = refractionLuminance * -0.03;

    scene.HitPositionWs = GetWorldPositionFromNormalizedDepth(scene.NormalizedDepth, view.Dir);

    float waterSurfaceViewZ = -(g_matWorldToView * vec4(positionWs, 1.0)).z;
    scene.ColumnDepthFactor = scene.RefractionDistortion + ClampToPositive((1.0 / scene.NormalizedDepth) - waterSurfaceViewZ) * 0.01;
    return scene;
}

// A value between its range ends by the vertex painted amount
float GetPaintedRange(float rangeMin, float rangeMax, float paintedAmount)
{
    return ClampToPositive(mix(rangeMin, rangeMax, paintedAmount));
}

float GetWaterRoughness()
{
    return g_bIsSkybox ? g_flWaterRoughnessMax : GetPaintedRange(g_flWaterRoughnessMin, g_flWaterRoughnessMax, vColorBlendValues.x);
}

float GetFoamAmount()
{
    return g_bIsSkybox ? 0.0 : GetPaintedRange(g_flFoamMin, g_flFoamMax, vColorBlendValues.y);
}

float GetDebrisVisibility()
{
    return g_bIsSkybox ? 0.0 : GetPaintedRange(g_flDebrisMin, g_flDebrisMax, vColorBlendValues.z);
}

vec3 WaterPlanePosition(WaterView_t view, vec3 positionWs, float baseHeight, float height, float edgeFactor)
{
    return positionWs + (view.DepthOffsetFactor * (mix(baseHeight, height, edgeFactor) - g_flWaterPlaneOffset));
}

// The unpacked normal of a wave texel, stretched to the wave scale and weighted by its octave
vec2 UnpackWaveNormalXY(vec2 sampledNormalXY, vec2 waveTexScale, float freqWeight)
{
    vec2 normalXY = sampledNormalXY * 2.0;
    normalXY.x *= min(1.0, waveTexScale.y / waveTexScale.x);
    normalXY.y *= min(1.0, waveTexScale.x / waveTexScale.y);
    return normalXY * (freqWeight * 0.1);
}

// The normal xy and the height of a wave texel, from RG=Normal, B=Height, all signed, centered at 0.5
vec3 SampleWaveOctave(vec2 waveSampleUV, float lod, vec2 waveTexScale, float freqWeight)
{
    vec3 sampledWaveNormalHeight = texture(g_tWavesNormalHeight, waveSampleUV, lod).xyz - vec3(0.5);

    float waveHeight = sampledWaveNormalHeight.z * freqWeight * length(waveTexScale) * 0.01;
    vec2 waveNormalXY = UnpackWaveNormalXY(sampledWaveNormalHeight.xy, waveTexScale, freqWeight);
    return vec3(waveNormalXY, waveHeight);
}

vec2 CausticWaveUV(float waveDir, vec2 waveScale, vec2 baseUV)
{
    return fma(
        vec2(sin(waveDir), cos(waveDir)) * ((g_flTime * g_flWavesSpeed) * 0.5),
        sqrt(vec2(1.0) / waveScale),
        baseUV / waveScale);
}

// Sums octaves of the wave texture, each smaller and rotated from the last
struct Waves_t
{
    vec3 Normal;
    float Height;
    vec2 PhaseOffset;
};

Waves_t GetWaves(WaterView_t view, vec2 baseWaveUV, float roughness, float totalDisturbanceStrength, float reflectionsLodFactor)
{
    float disturbanceWeightedFoamAmount = totalDisturbanceStrength * 0.25;
    float clampedLodFactor = clamp(reflectionsLodFactor, 0.0, 0.5);

    vec2 accumulatedWaveUVOffset = vec2(0.0);
    vec2 currentWaveTexScale = g_vWaveScale.xy;
    float currentWaveAngle = g_flWaterInitialDirection;

    Waves_t waves;
    waves.Normal = vec3(0.0, 0.0, 1.0);
    waves.Height = 0.0;
    waves.PhaseOffset = vec2(0.0);

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
            g_flHighFreqWeight * roughness + disturbanceWeightedFoamAmount, // Roughness makes high-freq waves stronger
            medHighBlend
        );

        // Sample wave texture: RG=Normal, B=Height (all signed, centered at 0.5)
        vec2 waveAnimOffset = vec2(sin(currentWaveAngle), cos(currentWaveAngle)) * (g_flTime * g_flWavesSpeed) * 0.5;

        vec2 anisoUV = waveAnimOffset * inversesqrt(currentWaveTexScale); // Anisotropic speed based on scale
        vec2 waveSampleUV1 = anisoUV + (baseWaveUV + accumulatedWaveUVOffset * 3.0 + waves.PhaseOffset) / currentWaveTexScale;
        vec3 waveComponent1 = SampleWaveOctave(waveSampleUV1, -clampedLodFactor, currentWaveTexScale, freqWeight);

        // The second sample is taken where the first one displaces the waves to
        vec2 gerstnerDisplacement = (-view.ParallaxFactor) * (waveComponent1.z) * g_flWavesHeightOffset * roughness;
        vec2 waveNormalDisplacement = ((waveComponent1.xy * g_flWavesSharpness) * currentWaveTexScale) * g_flWavesPhaseOffset;
        vec2 waveSampleUV2 = anisoUV + (baseWaveUV + (accumulatedWaveUVOffset + gerstnerDisplacement) * 3.0 + waves.PhaseOffset + waveNormalDisplacement) / currentWaveTexScale;

        vec3 waveComponent2 = SampleWaveOctave(waveSampleUV2, -clampedLodFactor, currentWaveTexScale, freqWeight);

        accumulatedWaveUVOffset += gerstnerDisplacement;
        waves.PhaseOffset += waveNormalDisplacement;
        waves.Normal.xy += waveComponent2.xy;
        waves.Height += waveComponent2.z;

        currentWaveTexScale *= g_flWavesPhaseOffset;

        currentWaveAngle += 3.5 / float(i + 1u); // Change angle to avoid repetition
    }

    return waves;
}

// The normal of the scene behind the water, rebuilt from the screen derivatives of the position it hits
vec3 ReconstructNormalFromDepth(SceneBehind_t scene, WaterView_t view)
{
    vec3 refractShiftedPos = scene.HitPositionWs + view.DepthOffsetFactor * clamp(GetLuma(scene.Color.rgb), 0.0, 0.4);

    vec3 refractShiftedPosDdx = dFdx(refractShiftedPos);
    vec3 refractShiftedPosDdy = -dFdy(refractShiftedPos);
    return -normalize(cross(refractShiftedPosDdx, refractShiftedPosDdy));
}

// The normal the surface blends towards where the water is shallow, and how much the edge shape affects the waves
vec3 GetRefractionNormal(SceneBehind_t scene, WaterView_t view, float refractedVerticalFactor, vec2 blueNoiseNegHalfToHalf,
    out float edgeFactor)
{
    vec3 reconstructedWorldNormal = ReconstructNormalFromDepth(scene, view);

    vec3 ditheredNormal = vec3(0.0, 0.0, 1.0);
    edgeFactor = g_flEdgeShapeEffect;

    if (F_REFRACTION == 1 && !g_bIsSkybox)
    {
        ditheredNormal = reconstructedWorldNormal;
        ditheredNormal.xy = reconstructedWorldNormal.xy + blueNoiseNegHalfToHalf * 0.05;
        edgeFactor = g_flEdgeShapeEffect * saturate(fma(-reconstructedWorldNormal.z, 1.0 - saturate(refractedVerticalFactor * 8.0), 1.2));
    }

    return ditheredNormal;
}

// Decal based effects of the effects map, like shots and people running through the water
struct WaterEffects_t
{
    float FoamHeight;
    float SiltFactor;
    vec2 DisplacementUV;
    vec2 DebrisNormalXY;
    float FoamFromEffects;
    float DebrisDisturbance;
    float Foam;
    vec2 SiltNormalXY;
    vec3 SamplePos;
};

WaterEffects_t SampleWaterEffects(WaterView_t view, vec3 positionWs, vec3 roughedWaveNormal, float waveHeight,
    float edgeFactor, float baseFoam)
{
    WaterEffects_t effects;
    effects.FoamHeight = waveHeight;
    effects.SiltFactor = 0.0;
    effects.DisplacementUV = vec2(0.0);
    effects.DebrisNormalXY = vec2(0.0);
    effects.FoamFromEffects = 0.0;
    effects.DebrisDisturbance = g_flWaterEffectDisturbanceStrength * 0.25;
    effects.Foam = baseFoam;
    effects.SiltNormalXY = vec2(0.0);
    effects.SamplePos = WaterPlanePosition(view, positionWs, 0.5, waveHeight, edgeFactor);

    if (g_bIsSkybox)
    {
        return effects;
    }

    mat4 transposedWorldToProj = transpose(g_matWorldToProjection);

    vec3 effectsPos0 = WaterPlanePosition(view, positionWs, 0.0, waveHeight, edgeFactor) + (vec3(roughedWaveNormal.xy, 0.0) * (-16.0));

    vec4 effectsPos0Transformed = (vec4(effectsPos0 - g_vCameraPositionWs, 1.0)) * transposedWorldToProj;
    vec4 effectsSample0 = texture(g_tWaterEffectsMap, WaterEffectsUV(effectsPos0Transformed)) - vec4(0.5);

    vec3 effectsPos1 = effectsPos0 + (view.DepthOffsetFactor * fma(20.0, effectsSample0.x, 2.0 * saturate(effectsSample0.yz * 2.0).x));
    vec4 effectsPos1Transformed = (vec4(effectsPos1.xyz, 1.0) - vec4(g_vCameraPositionWs, 1.0)).xyzw * transposedWorldToProj;
    vec2 effectsPos1UV = WaterEffectsUV(effectsPos1Transformed);

    vec4 effectsSample1 = texture(g_tWaterEffectsMap, effectsPos1UV) - vec4(0.5);

    vec2 rippleFoamFromEffectsMap = saturate(effectsSample1.yz * 2.0);

    vec4 offsetClipPosX = (vec4(effectsPos1 - g_vCameraPositionWs + vec3(1, 0, 0), 1.0)) * transposedWorldToProj;
    vec4 offsetClipPosY = (vec4(effectsPos1 - g_vCameraPositionWs + vec3(0, -1, 0), 1.0)) * transposedWorldToProj;

    vec2 duv_dx_approx = WaterEffectsUV(offsetClipPosX) - effectsPos1UV;
    vec2 duv_dy_approx = WaterEffectsUV(offsetClipPosY) - effectsPos1UV;

    vec2 stepScale = vec2(0.0004) / vec2(length(duv_dx_approx), length(duv_dy_approx));

    vec4 xOffsetEffectsSample = texture(g_tWaterEffectsMap, effectsPos1UV + normalize(duv_dx_approx) * 0.005) - 0.5;
    vec4 yOffsetEffectsSample = texture(g_tWaterEffectsMap, effectsPos1UV + normalize(duv_dy_approx) * 0.005) - 0.5;

    vec2 rippleFoamDX = saturate(xOffsetEffectsSample.yz * 2.0);
    vec2 rippleFoamDY = saturate(yOffsetEffectsSample.yz * 2.0);

    effects.DisplacementUV = (normalize(cross(vec3(stepScale.x, 0, effectsSample1.x - xOffsetEffectsSample.x), vec3(0, stepScale.y, effectsSample1.x - yOffsetEffectsSample.x))).xy * vec2(-1, 1)) * (abs(effectsSample1.x) * 4.0) * g_flWaterEffectsRippleStrength;
    effects.FoamHeight += effectsSample1.x * g_flWaterEffectsRippleStrength * 12;
    effects.SiltFactor = rippleFoamFromEffectsMap.y * g_flWaterEffectSiltStrength;

    effects.DebrisNormalXY = normalize(cross(vec3(stepScale.x, 0, rippleFoamFromEffectsMap.x - rippleFoamDX.x), vec3(0.0, stepScale.y, rippleFoamFromEffectsMap.x - rippleFoamDY.x))).xy * vec2(-1.0, 1.0);

    effects.FoamFromEffects = rippleFoamFromEffectsMap.x * g_flWaterEffectFoamStrength;

    effects.Foam = rippleFoamFromEffectsMap.y;
    effects.DebrisDisturbance = ((rippleFoamFromEffectsMap.x + rippleFoamFromEffectsMap.y) * g_flWaterEffectDisturbanceStrength) * 0.25;

    effects.SiltNormalXY = (normalize(cross(vec3(stepScale.x, 0, rippleFoamFromEffectsMap.y - rippleFoamDX.y), vec3(0, stepScale.y, rippleFoamFromEffectsMap.y - rippleFoamDY.y))).xy * vec2(-1, 1)) * pow(rippleFoamFromEffectsMap.y, 3.5);

    effects.SamplePos = WaterPlanePosition(view, positionWs, 0.5, effects.FoamHeight, edgeFactor) + (vec3(effects.DisplacementUV.xy, 0.0) * (-4.0));
    return effects;
}

// The foam and the debris that floats on the water. The suppression is how much the foam and a low debris height hide the debris.
struct FoamDebris_t
{
    vec4 FoamSample1;
    vec4 FoamSample2;
    float FoamIntensity;
    float CombinedFoamIntensity;
    vec4 DebrisSample; // RGB=color, A=height/mask
    float DebrisHeight;
    float DebrisVisibility;
    float DebrisEdgeFactor;
    float Suppression;
    float VisibilityMask;
    float DebrisFactor;
    vec2 DebrisNormalXY;
    float FoamHeight;
    vec3 WorldPos;
    vec2 WobbleOffset;
    vec2 SiltDirection;
    vec2 WobbleAnim;
    vec2 DepthFactorCoarse;
};

FoamDebris_t GetFoamAndDebris(WaterView_t view, vec3 positionWs, WaterEffects_t effects, float edgeFactor, float foamAmount,
    float debrisVisibility, vec2 wavePhaseOffset, float adjustedColumnDepth, float blueNoiseX)
{
    FoamDebris_t fd;

    vec3 rippleDisplacement = vec3(effects.DisplacementUV, 0.0);
    fd.WorldPos = WaterPlanePosition(view, positionWs, 0.5, effects.FoamHeight, edgeFactor * 0.5) + (rippleDisplacement * (-2.0));

    float timeAnim = g_flTime * 3.0 + sin(g_flTime * 0.5) * 0.1;

    vec2 depthFactorFine = vec2(saturate(adjustedColumnDepth * 10.0));
    fd.DepthFactorCoarse = vec2(saturate(adjustedColumnDepth * 4.0));

    fd.WobbleAnim = vec2(sin(effects.SamplePos.y * 0.07 + timeAnim), cos(effects.SamplePos.x * 0.07 + timeAnim));
    vec2 foamBaseUV = (fd.WorldPos.xy / g_flFoamScale);

    vec2 foamWobbleEffect = (foamBaseUV + wavePhaseOffset * g_flFoamWobble * 0.5 * (1.0 - foamAmount)) - (effects.SiltNormalXY / g_flFoamScale);

    float foamNoiseStrength = effects.Foam + 0.05;
    fd.FoamSample1 = texture(g_tFoam, mix(foamBaseUV, foamWobbleEffect + (fd.WobbleAnim * foamNoiseStrength * 0.03), depthFactorFine));
    vec2 sample2Mix1 = foamBaseUV.yx * 0.731;
    vec2 sample2Mix2 = (foamWobbleEffect.yx * 0.731) + ((vec2(sin(fma(effects.SamplePos.y, 0.06, timeAnim)), cos(fma(effects.SamplePos.x, 0.06, timeAnim))) * foamNoiseStrength) * 0.02);
    fd.FoamSample2 = texture(g_tFoam, mix(sample2Mix1, sample2Mix2, depthFactorFine)); // Second sample with different UVs/anim for variation

    float combinedFoamTextureValue = (sin(blueNoiseX) * 0.125 + max(fd.FoamSample1.z, fd.FoamSample2.z));

    fd.FoamIntensity = fma(foamAmount * fma(effects.FoamHeight, 0.008, 1.0), 1.0 - saturate(effects.DebrisDisturbance * 2.0), effects.FoamFromEffects);
    fd.FoamIntensity = saturate(fd.FoamIntensity);

    float foamPow1_5 = pow(effects.Foam, 1.5);

    vec2 debrisBaseUV = fd.WorldPos.xy / g_flDebrisScale;
    fd.WobbleOffset = wavePhaseOffset * g_flDebrisWobble;

    float absSiltX = abs(effects.SiltNormalXY.x);
    float absSiltY = abs(effects.SiltNormalXY.y);
    float dominantSiltY = effects.SiltNormalXY.y * float(absSiltY > absSiltX);
    fd.SiltDirection = (vec2(effects.SiltNormalXY.x * float(absSiltX > abs(dominantSiltY)), dominantSiltY) / g_flDebrisScale) * 400.0;

    vec2 debrisDistortedUV = ((debrisBaseUV + (fd.WobbleOffset * (1.0 - debrisVisibility))));
    debrisDistortedUV += ((view.ParallaxFactor * (fma(sin(effects.Foam * 50.0) * 4.0, saturate(0.1 - foamPow1_5), 1.0) * foamPow1_5)) * 0.1);
    debrisDistortedUV += ((fd.WobbleAnim * (0.1 + effects.Foam)) * 0.02);
    debrisDistortedUV -= fd.SiltDirection;
    vec2 debrisFinalUV = mix(debrisBaseUV, debrisDistortedUV, fd.DepthFactorCoarse).xy;

    fd.DebrisSample = texture(g_tDebris, debrisFinalUV, foamPow1_5 * 3.0);
    fd.DebrisHeight = fd.DebrisSample.a - 0.5; // Signed height
    fd.DebrisVisibility = fma(-debrisVisibility, saturate(1.4 - (effects.Foam / mix(1.0, 0.4, fd.DebrisSample.w))), 1.0);
    fd.DebrisEdgeFactor = saturate((fd.DebrisSample.a - fd.DebrisVisibility) * g_flDebrisEdgeSharpness);
    fd.Suppression = max(0.0, fma(2.0, foamPow1_5, fd.DebrisHeight * (-2.0)));
    fd.VisibilityMask = saturate(fma(-fd.Suppression, 10.0, 1.0));
    fd.DebrisFactor = fd.VisibilityMask * fd.DebrisEdgeFactor; // Final alpha for debris layer

    vec3 debrisNormalSample = texture(g_tDebrisNormal, debrisFinalUV).xyz - vec3(0.5);
    debrisNormalSample.y *= -1.0;
    fd.DebrisNormalXY = debrisNormalSample.xy * g_flDebrisNormalStrength;

    fd.CombinedFoamIntensity = saturate(fma(-fd.VisibilityMask, fd.DebrisEdgeFactor, fma(fd.FoamIntensity * combinedFoamTextureValue, 0.25, saturate(fd.FoamIntensity - (1.0 - combinedFoamTextureValue)) * 0.75)));
    fd.FoamHeight = mix(effects.FoamHeight, fma(effects.FoamHeight, 0.5, fd.DebrisHeight * 2.0), fd.DebrisFactor);
    return fd;
}

// Where the surface is, and how deep the water column is beneath it
void GetWaterSurfacePosition(WaterView_t view, SceneBehind_t scene, vec3 positionWs, WaterEffects_t effects, float surfaceHeight,
    float edgeFactor, out vec3 surfacePos, out float columnDepth)
{
    surfacePos = effects.SamplePos;
    columnDepth = scene.ColumnDepthFactor;

    if (!g_bIsSkybox)
    {
        surfacePos = WaterPlanePosition(view, positionWs, 0.5, surfaceHeight, edgeFactor) + vec3(effects.DisplacementUV * -12.0, 0.0);

        float surfaceViewDepth = -(g_matWorldToView * vec4(surfacePos, 1.0)).z;
        float waterExtent = ClampToPositive((1.0 / scene.NormalizedDepth) - surfaceViewDepth);
        columnDepth = fma(waterExtent, 0.01, scene.RefractionDistortion);
    }
}

// The perturbed normals of the waves, foam and debris. Where the water is shallow they blend towards the refraction normal.
void GetWaterNormals(inout MaterialProperties_t mat, SceneBehind_t scene, WaterView_t view, FoamDebris_t fd, WaterEffects_t effects,
    vec3 roughedWaveNormal, vec3 ditheredNormal, float reflectionsLodFactor, float columnDepth, vec2 blueNoiseNegHalfToHalf,
    out vec3 perturbedSurfaceNormal)
{
    float sceneDepthChangeMagnitude = fwidth(scene.NormalizedDepth);

    vec2 waveNormalXY = (((roughedWaveNormal.xy * 2.0) * g_flWavesNormalStrength) * mix(1.0, 2.0, reflectionsLodFactor)); // Stronger at glancing angles

    waveNormalXY *= fma(saturate(0.2 - columnDepth), 8.0, 1.0);
    waveNormalXY += ((fd.DebrisNormalXY * fd.DebrisFactor) * 1.5);
    waveNormalXY += (mix(fd.FoamSample1.xy - vec2(0.5), fd.FoamSample2.xy - vec2(0.5), vec2(float(fd.FoamSample2.z > fd.FoamSample1.z))).xy * fd.CombinedFoamIntensity);
    waveNormalXY += ((effects.DebrisNormalXY.xy * fd.CombinedFoamIntensity) * 0.5);
    waveNormalXY += ((effects.DisplacementUV.xy * (1.0 - saturate(fma(fd.VisibilityMask, fd.DebrisEdgeFactor, fd.CombinedFoamIntensity)))) * 2.0);
    waveNormalXY *= (vec2(1.0) + ((blueNoiseNegHalfToHalf * 2.0) * g_flWavesNormalJitter));

    mat.NormalMap = vec3(waveNormalXY, sqrt(1.0 - saturate(length_squared(waveNormalXY))));

    vec2 perturbedNormalXY = mat.NormalMap.xy * 3.0; // Stronger perturbation
    perturbedSurfaceNormal = vec3(perturbedNormalXY, sqrt(1.0 - saturate(length_squared(perturbedNormalXY))));

    if (F_REFRACTION == 1 && !g_bIsSkybox)
    {
        float shallowLimit = mix(60.0, 120.0, ditheredNormal.z);
        vec3 edgeLimitFactor = vec3((saturate(fma(-sceneDepthChangeMagnitude, 1000.0, saturate(((1.0 / shallowLimit) - columnDepth) * shallowLimit) + saturate((0.025 - columnDepth) * 8.0))) / fma(view.DistanceToFrag, 0.002, 1.0)) * 0.6);
        mat.NormalMap = normalize(mix(mat.NormalMap, ditheredNormal, edgeLimitFactor));
        perturbedSurfaceNormal = normalize(mix(perturbedSurfaceNormal, ditheredNormal, edgeLimitFactor));
    }
}

// Everything about the water surface that the shading needs
struct WaterSurface_t
{
    WaterView_t View;
    SceneBehind_t Scene;
    float Roughness;
    float ReflectionsLodFactor;
    float AdjustedColumnDepth;
    float RefractedVerticalFactor;
    float DebrisDisturbance;
    float SiltFactor;
    FoamDebris_t FoamDebris;
    vec3 DitheredNormal;
    vec3 PerturbedNormal;
    vec3 Position;
    float ColumnDepth;
    float CoverageAlpha;
};

WaterSurface_t GetWaterSurface(inout MaterialProperties_t mat, vec4 blueNoise)
{
    vec2 blueNoiseNegHalfToHalf = blueNoise.xy - 0.5;

    WaterSurface_t surface;
    surface.View = GetWaterView(mat.PositionWS);
    surface.Scene = SampleSceneBehind(surface.View, mat.PositionWS);
    surface.Roughness = GetWaterRoughness();

    surface.AdjustedColumnDepth = ClampToPositive(surface.Scene.ColumnDepthFactor - 0.02);
    surface.RefractedVerticalFactor = surface.Scene.ColumnDepthFactor * surface.View.DirInv.z;

    vec2 baseWaveUV = (mat.PositionWS.xy * surface.View.SkyboxScale + surface.View.DepthOffsetFactor.xy * (0.5 - g_flWaterPlaneOffset)) / 30.0; // Another arbitrary scale

    vec2 baseWaveUVDx = dFdx(baseWaveUV);
    vec2 baseWaveUVDy = -dFdy(baseWaveUV);

    surface.ReflectionsLodFactor = (0.5 * pow(max(length_squared(baseWaveUVDx), length_squared(baseWaveUVDy)), 0.1)) * g_flReflectionDistanceEffect;

    // Assumes the effects map is the size of the scene color
    vec4 waterEffectsSampleRaw = texture(g_tWaterEffectsMap, surface.View.GbufferUV);
    vec2 waterEffectsDisturbanceXY = saturate((waterEffectsSampleRaw.yz - 0.5) * 2.0);
    float totalDisturbanceStrength = (waterEffectsDisturbanceXY.x + waterEffectsDisturbanceXY.y) * g_flWaterEffectDisturbanceStrength;

    Waves_t waves = GetWaves(surface.View, baseWaveUV, surface.Roughness, totalDisturbanceStrength, surface.ReflectionsLodFactor);

    vec2 wavePhaseOffset = waves.PhaseOffset * 0.1;
    vec3 roughedWaveNormal = waves.Normal * surface.Roughness;
    float scaledWaveHeight = waves.Height * surface.Roughness * 60.0; // For stronger visual effect

    float edgeFactor;
    surface.DitheredNormal = GetRefractionNormal(surface.Scene, surface.View, surface.RefractedVerticalFactor, blueNoiseNegHalfToHalf, edgeFactor);

    WaterEffects_t effects = SampleWaterEffects(surface.View, mat.PositionWS, roughedWaveNormal, scaledWaveHeight, edgeFactor,
        waterEffectsDisturbanceXY.y);
    surface.DebrisDisturbance = effects.DebrisDisturbance;
    surface.SiltFactor = effects.SiltFactor;

    surface.FoamDebris = GetFoamAndDebris(surface.View, mat.PositionWS, effects, edgeFactor, GetFoamAmount(), GetDebrisVisibility(),
        wavePhaseOffset, surface.AdjustedColumnDepth, blueNoise.x);

    mat.Height = surface.FoamDebris.FoamHeight;

    GetWaterSurfacePosition(surface.View, surface.Scene, mat.PositionWS, effects, mat.Height, edgeFactor,
        surface.Position, surface.ColumnDepth);

    surface.CoverageAlpha = saturate(surface.FoamDebris.DebrisEdgeFactor + surface.FoamDebris.CombinedFoamIntensity);

    GetWaterNormals(mat, surface.Scene, surface.View, surface.FoamDebris, effects, roughedWaveNormal, surface.DitheredNormal,
        surface.ReflectionsLodFactor, surface.ColumnDepth, blueNoiseNegHalfToHalf, surface.PerturbedNormal);

    return surface;
}

// The scene color seen through the water. The blur shifts the samples along the offset and splits the color channels between them.
vec4 SampleRefractedSceneColor(vec2 gbufferUV, vec2 refractionUVOffset, float columnDepth)
{
#if F_BLUR_REFRACTION == 1
    float smallOffset = 0.001 * columnDepth;
    float sampleSpread = g_flRefractSampleOffset * columnDepth;
    vec4 sample1 = textureLod(g_tSceneColor, saturate(gbufferUV + refractionUVOffset), 0);
    vec4 sample2 = textureLod(g_tSceneColor, saturate(gbufferUV + (refractionUVOffset * (1.0 + sampleSpread)) + vec2(0.0, smallOffset)), 0);
    vec4 sample3 = textureLod(g_tSceneColor, saturate(gbufferUV + (refractionUVOffset * (1.0 + sampleSpread * 2.0)) - vec2(0.0, smallOffset)), 0);
    vec4 sample4 = textureLod(g_tSceneColor, saturate(gbufferUV + (refractionUVOffset * (1.0 + sampleSpread * 3.0)) + vec2(smallOffset, 0.0)), 0);
    vec4 sample5 = textureLod(g_tSceneColor, saturate(gbufferUV + (refractionUVOffset * (1.0 + sampleSpread * 4.0)) - vec2(smallOffset, 0.0)), 0);

    vec4 mixedColor =
        (sample5 * mix(vec4(1.0), vec4(2.0, 0.0, 0.0, 1.0), g_flRefractChromaticSeparation)) +
        (sample4 * mix(vec4(1.0), vec4(2.0, 2.0, 0.0, 1.0), g_flRefractChromaticSeparation)) +
        (sample3 * mix(vec4(1.0), vec4(0.0, 3.0, 0.0, 1.0), g_flRefractChromaticSeparation)) +
        (sample2 * mix(vec4(1.0), vec4(0.0, 0.0, 3.0, 1.0), g_flRefractChromaticSeparation)) +
        (sample1 * mix(vec4(1.0), vec4(1.0, 0.0, 2.0, 1.0), g_flRefractChromaticSeparation));

    return mixedColor * 0.2; // Average the 5 samples
#else
    return textureLod(g_tSceneColor, saturate(gbufferUV + refractionUVOffset), 0);
#endif
}

// Light that the waves focus onto the ground under the water
struct Caustics_t
{
    vec3 Color;
    float DebrisCoverage;
    float EffectsZ;
};

Caustics_t GetCaustics(WaterSurface_t surface, vec3 darkenedRefractedColor, float causticVisibility, vec2 refractionUVOffset,
    float siltStrength, float blueNoiseX, vec3 sunDir, vec3 sunColor)
{
    WaterView_t view = surface.View;
    FoamDebris_t fd = surface.FoamDebris;

    vec3 refractedViewDir = (-normalize((view.DirInv + ((g_vCameraUpDirWs * refractionUVOffset.y) * 2.0)) + ((cross(g_vCameraDirWs, g_vCameraUpDirWs) * (-refractionUVOffset.x)) * 2.0))).xyz;
    float sceneViewDistance = GetRayScaleFromNormalizedDepth(surface.Scene.NormalizedDepth, view.Dir);

    vec3 refractedSceneHitPosWs = g_vCameraPositionWs + normalize(refractedViewDir) * sceneViewDistance;

    vec3 causticsLightDir = sunDir;

    if (g_bUseTriplanarCaustics == 1)
    {
        vec3 ditheredNormalExtent = abs(surface.DitheredNormal);
        causticsLightDir = mix(sunDir, mix(mix(vec3(0.0, 1.0, 1.0), vec3(1.0, 0.0, 1.0), vec3(ditheredNormalExtent.y < ditheredNormalExtent.x)), vec3(0.0, 0.0, 1.0), bvec3(ditheredNormalExtent.z > max(ditheredNormalExtent.x, ditheredNormalExtent.y))), vec3(0.65));
    }

    float causticsDepth = fd.WorldPos.z - refractedSceneHitPosWs.z;
    vec3 causticRayTarget = mix(refractedSceneHitPosWs + ((causticsLightDir.xyz * causticsLightDir.z) * causticsDepth), surface.Position.xyz, vec3(saturate((pow2(blueNoiseX) * siltStrength) * 0.0125)));
    float distToCausticTarget = distance(causticRayTarget, refractedSceneHitPosWs);

    vec2 causticDebrisUV = causticRayTarget.xy / g_flDebrisScale;
    vec4 causticDebrisSample = texture(g_tDebris, mix(causticDebrisUV, (((causticDebrisUV + (fd.WobbleOffset * fd.DebrisVisibility)) + ((view.ParallaxFactor * fd.Suppression) * 0.1)) + ((fd.WobbleAnim * 0.1) * 0.04)) - fd.SiltDirection, fd.DepthFactorCoarse).xy, causticsDepth * 0.05);
    float causticDebrisCoverage = saturate((fma(-fd.DebrisVisibility, 0.9, causticDebrisSample.w) - fd.DebrisEdgeFactor) * 1.1);

    float causticDepthFalloffPre = distToCausticTarget / g_flCausticDepthFallOffDistance;
    float causticDepthFalloff = saturate(1.0 - causticDepthFalloffPre);
    float causticBaseIntensity = (causticVisibility * saturate(distToCausticTarget * 0.05)) * causticDepthFalloff;

    if (g_bUseTriplanarCaustics == 0)
    {
        causticBaseIntensity *= saturate(dot(surface.DitheredNormal, causticsLightDir.xyz));
    }

    vec2 causticWaveUVBase = (causticRayTarget.xy * vec2(1.0 / 30)) * g_flCausticUVScaleMultiple;

    vec2 currWaveScale = g_vWaveScale.xy;
    vec2 currWaveNormalXY = vec2(0);
    float currWaveDir = g_flWaterInitialDirection;

    float lodOffset = fma(-g_flCausticSharpness, causticDepthFalloff, 1.0) * 6.0;

    for (int i = 0; i < 3; i++)
    {
        vec2 localUV = CausticWaveUV(currWaveDir, currWaveScale, causticWaveUVBase + currWaveNormalXY);

        vec3 rawSample = texture(g_tWavesNormalHeight, localUV, lodOffset).xyz;

        currWaveNormalXY.xy += (((((rawSample - vec3(0.5)).xy * 0.5) * g_flCausticDistortion) * (vec2(1.0) + currWaveScale)) * (0.25 + causticDepthFalloffPre));

        currWaveScale *= g_flWavesPhaseOffset;
        currWaveDir += (3.5 / (i + 1));
    }

    vec2 currWaveScale1 = g_vWaveScale.xy;
    float currWaveDir1 = g_flWaterInitialDirection;
    vec3 currWaveSampleSum1 = vec3(0.0);

    for (int i = 0; i < 3; i++)
    {
        float causticIterProgress = float(i) / (float(g_nWaveIterations) - 1.0);

        vec2 uv = CausticWaveUV(currWaveDir1, currWaveScale1, causticWaveUVBase + currWaveNormalXY);

        vec3 rawSample = vec3(texture(g_tWavesNormalHeight, uv, lodOffset).z);
        vec3 exponent = vec3(causticDepthFalloff * g_flCausticSharpness * 5.0);
        float factor = clamp(mix(mix(fma(surface.DebrisDisturbance, 0.1, g_flLowFreqWeight), g_flMedFreqWeight + surface.DebrisDisturbance, saturate(causticIterProgress * 2.0)), fma(g_flHighFreqWeight, surface.Roughness, surface.DebrisDisturbance), saturate(fma(causticIterProgress, 2.0, -1.0))), 0.1, 0.4);

        float waveSampleCausticDepthFalloff = causticDepthFalloff * g_flCausticSharpness;
        currWaveSampleSum1 += (((((SafePow(rawSample, exponent) * factor) * (vec3(1.0) + (currWaveSampleSum1 * 2.0))) * causticDepthFalloff) * waveSampleCausticDepthFalloff) * 2.0);

        currWaveScale1 *= g_flWavesPhaseOffset;
        currWaveDir1 += (3.5 / (i + 1));
    }

    vec3 causticsPosition = (causticRayTarget.xyz + ((vec3(currWaveNormalXY, 0.0) * 60.0) * currWaveSampleSum1.x));
    causticsPosition -= g_vCameraPositionWs;
    vec4 causticsClipPos = vec4(causticsPosition, 1.0) * transpose(g_matWorldToProjection);

    vec2 causticsUV = WaterEffectsUV(causticsClipPos);
    vec4 causticsEffectsSampleRaw = texture(g_tWaterEffectsMap, causticsUV) - 0.5;
    vec2 causticsClampedEffectsXY = saturate(causticsEffectsSampleRaw.yz * 2.0);

    vec4 causticsEffectsSample = causticsEffectsSampleRaw;
    causticsEffectsSample.y = causticsClampedEffectsXY.x;
    causticsEffectsSample.z = causticsClampedEffectsXY.y;

    vec4 fadedCausticsEffects = causticsEffectsSample * saturate((((causticsUV.y * (1.0 - causticsUV.y)) * causticsUV.x) * (1.0 - causticsUV.x)) * 40.0);

    float causticsXOverChangerate = fadedCausticsEffects.x + (fadedCausticsEffects.x / fma(fwidth(fadedCausticsEffects.x), 1000.0, 0.5));

    vec3 causticsModifier = (currWaveSampleSum1 + vec3(fma(saturate(causticsXOverChangerate) * 4.0, g_flWaterEffectCausticStrength, -((saturate(-causticsXOverChangerate) * 0.15) * g_flWaterEffectCausticStrength)))) * mix(1.0, 0.0, saturate(fma(causticDebrisCoverage, 2.0, fadedCausticsEffects.y * 0.4)));
    float causticsModifierX = causticsModifier.x;

    vec3 causticsBase = max(causticsModifier * (vec3(1.0) + (vec3(1.25, -0.25, -1.0) * (clamp(dFdxFine(causticsModifierX) * 200.0, -1.0, 1.0) * saturate(fma(-causticsModifierX, 3.0, 1.0))))), vec3(0.001)) * 8.0;
    vec3 modifiedCausticsRefractColor = darkenedRefractedColor * (vec3(1.0) + (((((pow(causticsBase, vec3(2.5)) * causticBaseIntensity) * sunColor) * g_vCausticsTint.xyz) * g_flCausticsStrength) * 0.1));

    float causticsLuma = pow(GetLuma(modifiedCausticsRefractColor), 0.2);
    float causticsLumaGradient = clamp(dFdxFine(causticsLuma), -1.0, 1.0) + clamp(-dFdyFine(causticsLuma), -1.0, 1.0);

    Caustics_t caustics;
    caustics.DebrisCoverage = causticDebrisCoverage;
    caustics.Color = mix(modifiedCausticsRefractColor, modifiedCausticsRefractColor * (vec3(1.0) + (vec3(2.5, 0.0, -2.0) * float(int(sign(causticsLumaGradient * saturate(abs(causticsLumaGradient) - 0.1)))))), vec3(saturate(200.0 / view.DistanceToFrag) * 0.1));
    caustics.EffectsZ = fadedCausticsEffects.z;
    return caustics;
}

// The scene seen through the water, darkened and lit by the caustics. Empty for the skybox copy of the water.
struct Refraction_t
{
    vec3 Color;
    float DebrisCoverage;
    float CausticsEffectsZ;
    float ColumnDepth;
    float SiltStrength;
};

Refraction_t GetRefraction(WaterSurface_t surface, vec4 blueNoise, vec3 sunDir, vec3 sunColor)
{
    Refraction_t refraction;
    refraction.Color = vec3(0.0);
    refraction.DebrisCoverage = 0.0;
    refraction.CausticsEffectsZ = 0.0;
    refraction.ColumnDepth = surface.ColumnDepth;
    refraction.SiltStrength = g_flWaterFogStrength;

    if (g_bIsSkybox)
    {
        return refraction;
    }

    vec2 blueNoiseNegHalfToHalf = blueNoise.xy - 0.5;
    vec2 gbufferUV = surface.View.GbufferUV;

    vec2 refractionUVOffsetRaw = (vec2(dot(surface.PerturbedNormal.xy, cross(surface.View.DirInv, vec3(0.0, 0.0, -1.0)).xy), dot(surface.PerturbedNormal.xy, surface.View.DirInv.xy)) + ((blueNoiseNegHalfToHalf * 0.002) * g_flWaterFogStrength)).xy * min(g_flRefractionLimit, surface.ColumnDepth);
    float depthBufferRange = g_flViewportMaxZ - g_flViewportMinZ;
    float surfaceDepth = -(g_matWorldToView * vec4(surface.Position, 1.0)).z;

    float normalizedDepth = saturate((textureLod(g_tSceneDepth, gbufferUV + refractionUVOffsetRaw.xy, 0.0).x - g_flViewportMinZ) / depthBufferRange);

    normalizedDepth = max(normalizedDepth, 0.0000001);
    float groundDepth = (1.0 / normalizedDepth);

    float waterExtent = groundDepth - surfaceDepth;
    float refractionOffsetAttenuation = saturate(fma(ClampToPositive(waterExtent), 0.01, surface.Scene.RefractionDistortion) * 10.0);

    vec2 finalRefractionUVOffset = refractionUVOffsetRaw * refractionOffsetAttenuation;

    float finalRefractedNormalizedDepth = (textureLod(g_tSceneDepth, gbufferUV + finalRefractionUVOffset, 0).x - g_flViewportMinZ) / depthBufferRange;
    finalRefractedNormalizedDepth = max(finalRefractedNormalizedDepth, 0.0000001);

    refraction.ColumnDepth = fma(ClampToPositive((1.0 / finalRefractedNormalizedDepth) - surfaceDepth), 0.01, surface.Scene.RefractionDistortion);

    vec4 finalRefractedColor = SampleRefractedSceneColor(gbufferUV, finalRefractionUVOffset, refraction.ColumnDepth);

    vec3 darkenedRefractedColor = pow(finalRefractedColor.rgb, vec3(1.1)) * g_flUnderwaterDarkening;

    refraction.SiltStrength += surface.SiltFactor * 2.0;

    float causticVisibility = saturate((GetLuma(darkenedRefractedColor.xyz) - g_flCausticShadowCutOff) * (2.0 + g_flCausticShadowCutOff));

    refraction.Color = darkenedRefractedColor;

    if (F_CAUSTICS == 1 && causticVisibility > 0.0)
    {
        Caustics_t caustics = GetCaustics(surface, darkenedRefractedColor, causticVisibility, finalRefractionUVOffset,
            refraction.SiltStrength, blueNoise.x, sunDir, sunColor);

        refraction.Color = caustics.Color;
        refraction.DebrisCoverage = caustics.DebrisCoverage;
        refraction.CausticsEffectsZ = caustics.EffectsZ;
    }

    return refraction;
}

// Marches the reflected ray through the depth buffer and mixes in the color it hits
vec3 ApplyScreenSpaceReflection(vec3 reflectionColor, vec3 waterColor, WaterSurface_t surface, vec3 normalMap, float cosNormAngle,
    float blueNoiseNegOneToOne)
{
    // Fewer steps, and a fainter reflection, as the camera looks further down
    float SSRViewFade = saturate((g_vCameraDirWs.z + 0.75) * 4.0);
    int SSRStepCount = int(g_nSSRMaxForwardSteps * (0.5 + 0.5 * float(!g_bIsSkybox)) * SSRViewFade);

    if (F_REFLECTION_TYPE != 2 || SSRStepCount <= 0)
    {
        return reflectionColor;
    }

    float SsrHitThickness = fma(blueNoiseNegOneToOne, g_flSSRSampleJitter, g_flSSRMaxThickness);

    vec4 SSNormal4f = g_matWorldToView * vec4(normalize(vec3((normalMap.xy * 3.0) * mix(2.0, 8.0, float(g_bIsSkybox)), normalMap.z)), 0.0);

    vec3 viewSpacePos = (g_matWorldToView * vec4(surface.Position.xyz, 1.0)).xyz;

    vec3 SSNormal = SSNormal4f.xyz;
    SSNormal.yz *= 2;

    vec4 projectedPos = g_matViewToProjection * vec4(-viewSpacePos.xyz, 1.0);
    vec3 baseNdcCoords = projectedPos.xyz / vec3(projectedPos.w);

    vec2 baseSsrUV = PackToColor(baseNdcCoords.xy);
    float initialStepSize = (fma(blueNoiseNegOneToOne, g_flSSRSampleJitter, g_flSSRStepSize) / fma(surface.ReflectionsLodFactor, 2.0, 1.0)) * mix(20.0, 1.0, cosNormAngle);

    float baseStepSize = initialStepSize;
    if (g_bIsSkybox)
    {
        baseStepSize = initialStepSize * (surface.View.DistanceToFrag * 0.002);
    }

    vec3 SSReflectDir = normalize(reflect(normalize(viewSpacePos), normalize(SSNormal))).xyz;

    vec3 prevSamplePos = viewSpacePos;
    vec2 SsrUVCoords = baseSsrUV;
    vec2 finalSsrUVCoords = baseSsrUV;
    float currStepSize;
    float currSampleWorldDepth;
    float prevWorldDepth = 0.0;
    vec3 currSamplePos;
    float prevStepSize = baseStepSize;
    float fractionalSampleCount = 0.0;
    float prevCurrFrac;
    int i = 1;
    for (; i <= SSRStepCount; i++)
    {
        currStepSize = prevStepSize * 1.15;
        currSamplePos = prevSamplePos + SSReflectDir * currStepSize;
        vec4 currViewSpacePos = g_matViewToProjection * vec4(-currSamplePos, 1.0);
        vec3 currNdc = currViewSpacePos.xyz / vec3(currViewSpacePos.w);
        vec2 currSsrUV = PackToColor(currNdc.xy);
        float currNormalizedDepth = (textureLod(g_tSceneDepth, currSsrUV.xy, 0.0).x - g_flViewportMinZ) / (g_flViewportMaxZ - g_flViewportMinZ);

        currNormalizedDepth = max(currNormalizedDepth, 0.0000001);
        currSampleWorldDepth = (-1.0 / currNormalizedDepth - currSamplePos.z);

        prevCurrFrac = saturate(currSampleWorldDepth / (currSampleWorldDepth - prevWorldDepth));

        if (currSampleWorldDepth >= 0.0)
        {
            if (currSampleWorldDepth < (SsrHitThickness * currStepSize))
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

    if (!g_bIsSkybox)
    {
        vec2 scaledSsrUVs = finalSsrUVCoords;
        float sampleOffset = fracOfTotalSteps * (-0.00390625);
        vec3 SsrColorSample = ((
        (textureLod(g_tSceneColor, saturate(scaledSsrUVs + vec2(sampleOffset)).xy, 0).xyz * 0.444444) +
        (textureLod(g_tSceneColor, saturate(scaledSsrUVs + vec2(0.001953125, sampleOffset)).xy, 0).xyz * 0.222222)) +
        (textureLod(g_tSceneColor, saturate(scaledSsrUVs + vec2(sampleOffset, 0.001953125)).xy, 0).xyz * 0.222222)) +
        (textureLod(g_tSceneColor, saturate(scaledSsrUVs + vec2(0.001953125)).xy, 0).xyz * 0.111111);

        SsrReflectionResult = (SsrColorSample + ((normalize(SsrColorSample + vec3(0.001)) * ClampToPositive(GetLuma(SsrColorSample.xyz) - g_flSSRBoostThreshold)) * g_flSSRBoost)) * g_flSSRBrightness;
    }
    else
    {
        SsrReflectionResult = mix((waterColor.xyz + reflectionColor) * 0.5, reflectionColor, vec3(fracOfTotalSteps));
    }

    return mix(reflectionColor, SsrReflectionResult, vec3(saturate(1.0 - pow(fracOfTotalSteps, 4.0)) * saturate((1.0 - finalSsrUVCoords.y) * 8) * saturate(SSRViewFade * 2.0)));
}

// Discards the water more and more towards the edge of the map, where the skybox copy of it takes over
void DitherIntoSkybox(vec3 positionWs, float blueNoiseX)
{
    if (g_bIsSkybox)
    {
        return;
    }

    vec2 unbiasedUV = (positionWs.xy - g_vMapUVMin.xy) / (g_vMapUVMax.xy - g_vMapUVMin.xy);
    unbiasedUV.y = 1.0 - unbiasedUV.y;

    vec2 distanceFromCenter = abs(vec2(0.5) - unbiasedUV) * 2.0;
    float edgeFade = saturate(1.0 - saturate((max(distanceFromCenter.x, distanceFromCenter.y) - (1.0 - g_flSkyBoxFadeRange)) / g_flSkyBoxFadeRange));

    if (edgeFade - blueNoiseX < 0.0)
    {
        discard;
    }
}

float GetEdgeBlend(WaterSurface_t surface, float effectiveWaterDepth)
{
    FoamDebris_t fd = surface.FoamDebris;

    return saturate(fma(g_flEdgeHardness, effectiveWaterDepth, saturate(fd.CombinedFoamIntensity)) + fma(fd.DebrisHeight, 2.0, -0.5));
}

// Shades the water surface, and returns the color of the reflection and how much of the water is faded out at its edge
vec3 ShadeWater(inout MaterialProperties_t mat, WaterSurface_t surface, vec4 blueNoise, out LightingTerms_t lighting,
    out vec3 finalReflectionColor, out vec3 reflectionMix, out float edgeBlend)
{
    WaterView_t view = surface.View;
    FoamDebris_t fd = surface.FoamDebris;
    float blueNoiseNegOneToOne = (blueNoise.x - 0.5) * 2.0;
    vec3 surfacePositionWs = mat.PositionWS;

    vec3 sunColor = GetSunColor();
    vec3 sunDir = GetSunDirection();

    float cosNormAngle = saturate(dot(view.DirInv, surface.PerturbedNormal.xyz));
    float fresnel = SafePow(1.0 - cosNormAngle, g_flFresnelExponent);
    vec3 finalFoamColor = g_vFoamColor.rgb * fma(fd.CombinedFoamIntensity, 0.5, 1.0);

    Refraction_t refraction = GetRefraction(surface, blueNoise, sunDir, sunColor);

    float effectiveWaterDepthForFog = min(g_flWaterMaxDepth, refraction.ColumnDepth);
    vec3 waterDecayColorFactor = exp(((g_vWaterDecayColor.rgb - vec3(1.0)) * vec3(g_flWaterDecayStrength)) * effectiveWaterDepthForFog);
    float totalFogStrength = max(refraction.SiltStrength, refraction.CausticsEffectsZ);
    float foamDebrisForFogMix = fd.FoamIntensity + saturate(refraction.CausticsEffectsZ - 0.5);
    float waterFogAlpha = fma(fma(-saturate(blueNoise.x), 0.25, foamDebrisForFogMix), 0.1, 1.0 - exp((-effectiveWaterDepthForFog) * totalFogStrength));
    vec3 baseFogColor = mix(g_vWaterFogColor.rgb, finalFoamColor, vec3(foamDebrisForFogMix * 0.1)) * mix(waterDecayColorFactor, vec3(1.0), vec3(saturate(totalFogStrength * 0.04)));

    vec3 finalDirToCam = -normalize(surface.Position.xyz - g_vCameraPositionWs.xyz);
    float specularCosAlpha = clamp(dot(-sunDir, reflect(finalDirToCam, normalize(mix(normalize(mat.GeometricNormal).xyz, surface.PerturbedNormal.xyz, vec3(g_flSpecularNormalMultiple * fma(view.DistanceToFrag, 0.0005, 1.0)))))), 0.0001, 1.0);
    float specularExponent = mix(g_flSpecularPower, g_flDebrisReflectance * 8.0, fd.DebrisEdgeFactor) * mix(2.0, 0.2, saturate(surface.Roughness));
    float specularFactor = fma(pow(specularCosAlpha, specularExponent), 0.1, pow(specularCosAlpha, specularExponent * 10.0));

    float inverseWaterFogAlpha = 1.0 - waterFogAlpha;
    float waterOpacity = (saturate((1.0 - fd.DebrisEdgeFactor) + fd.Suppression) * saturate(fma(-fd.CombinedFoamIntensity, 4.0, 1.0))) * inverseWaterFogAlpha;

    mat.Albedo = mix(mix((baseFogColor * waterFogAlpha) * g_flWaterFogShadowStrength, finalFoamColor.xyz, vec3(fd.CombinedFoamIntensity)), vec4(fd.DebrisSample.xyz * fma(fd.DebrisFactor, 0.5, 0.5), fd.DebrisEdgeFactor).xyz * g_vDebrisTint.xyz, vec3(saturate(fd.DebrisEdgeFactor - fd.Suppression))).xyz;
    mat.Tangent = vTangentOut.xyz;
    mat.Bitangent = GetBitangent(vNormalOut, vTangentOut);
    mat.Normal = CalculateWorldNormal(mat.NormalMap, mat.GeometricNormal, mat.Tangent, mat.Bitangent);
    mat.AmbientNormal = mat.Normal;

    vec3 dynamicShadowsPositionOffset = (vec3(-fd.FoamHeight) + (((mix(blueNoise.xxx, vec3(blueNoise.xy, 0.0), vec3(0.1)) * 90.0) * pow2(waterOpacity)) + vec3(g_flWaterPlaneOffset)));
    dynamicShadowsPositionOffset *= -view.DepthOffsetFactor;
    float shadowOffsetScale = mix(1.0, effectiveWaterDepthForFog * 2.0, 0.75);
    dynamicShadowsPositionOffset *= shadowOffsetScale;

    mat.PositionWS += dynamicShadowsPositionOffset;

#if (D_BAKED_LIGHTING_FROM_LIGHTMAP == 1)
    float squaredWaterOpacity = pow2(waterOpacity);
    vec2 lightmapDither = (((((((fwidth(vLightmapUVScaled.xy) * 1200.0) / vec2(view.DistanceToFrag)) * cosNormAngle) * (-view.ParallaxFactor)) * vec2(-1.0, 1.0)) * (vec2(fd.FoamHeight * (-2.0)) + ((mix(blueNoise.yy, blueNoise.yx, vec2(0.1)) * 20.0) * squaredWaterOpacity))) * shadowOffsetScale);
    mat.LightmapUv = vec3(vLightmapUVScaled.xy, 0.0);
    mat.LightmapUv.xy += lightmapDither;
#endif

    lighting = InitLighting();

#if (D_BAKED_LIGHTING_FROM_LIGHTMAP == 1) && (BAKED_LIGHT_SHADOW_MASK)
    // The more opaque the water, the less of the realtime sun shadow it takes
    vec4 bakedShadows = SampleLightmapShadows(mat.LightmapUv.xy);
    float bakedSunVisibility = 1.0 - dot(bakedShadows, g_vSunLightBakedShadowMask);
    float sunVisibility = mix(bakedSunVisibility * CalculateSunShadowMapVisibility(mat.PositionWS), bakedSunVisibility, waterOpacity * 0.5);

    lighting.DiffuseDirect = ClampToPositive(dot(mat.NormalMap, sunDir)) * sunColor * sunVisibility;
    CalculateBarnLighting(lighting, mat, bakedShadows);
#else
    CalculateDirectLighting(lighting, mat);
#endif

    CalculateIndirectLighting(lighting, mat);

    vec3 litAlbedo = (lighting.DiffuseDirect + lighting.DiffuseIndirect) * mat.Albedo;

    vec3 returnColor = mix(litAlbedo, refraction.Color * waterDecayColorFactor, vec3(waterOpacity));
    returnColor = mix(returnColor, (baseFogColor * 4.0) * lighting.DiffuseIndirect, vec3((waterFogAlpha * saturate((1.0 - surface.CoverageAlpha) + fd.Suppression)) * (1.0 - g_flWaterFogShadowStrength)));

    mat.SpecularColor = vec3(1);

    vec2 waterRoughness = vec2(max(0.01, 1.0 - g_flGlossiness));
    float roughnessForCubemap = dot(mix(waterRoughness, vec2(1), vec2(clamp(surface.ReflectionsLodFactor, 0.0, 0.35))), vec2(0.5));
    mat.Roughness = vec2(roughnessForCubemap);
    mat.IsometricRoughness = roughnessForCubemap;

    vec3 reflectionNormal = mat.NormalMap;
    reflectionNormal.xy *= 6.0;
    reflectionNormal = mat3(g_matWorldToView) * normalize(reflectionNormal);
    reflectionNormal.yz *= 2;
    reflectionNormal = transpose(mat3(g_matWorldToView)) * normalize(reflectionNormal);

    mat.AmbientNormal = reflectionNormal;

    if (F_REFLECTION_TYPE > 0)
    {
        finalReflectionColor = GetEnvironmentNoBRDF(mat, 1.0) * g_flEnvironmentMapBrightness;

        // Scaled by the inverse of the environment maps' own brightness along the normal, not by the irradiance here
        float environmentLuma = dot(vec4(reflectionNormal, 1.0), g_vEnvMapNormalizationSH);
        finalReflectionColor *= GetEnvMapNormalization(roughnessForCubemap, environmentLuma, vec3(1.0));
    }
    else
    {
        finalReflectionColor = g_vSimpleSkyReflectionColor.rgb;
    }

    finalReflectionColor = ApplyScreenSpaceReflection(finalReflectionColor, returnColor, surface, mat.NormalMap, cosNormAngle, blueNoiseNegOneToOne);

    float localReflectance = mix(g_flReflectance, g_flDebrisReflectance, fd.DebrisFactor);
    float reflectionModulation = (fma(fresnel, 1.0 - localReflectance, localReflectance) * fma(-fd.CombinedFoamIntensity, 2.0, fma(-surface.CoverageAlpha, 0.75, 1.0))) * 1.5;
    lighting.SpecularDirect = (lighting.DiffuseDirect * (fma(ClampToPositive(specularFactor - (1.0 - g_flSpecularBloomBoostThreshold)), g_flSpecularBloomBoostStrength, specularFactor) * mix(1.0, g_flDebrisReflectance * 0.05, fd.DebrisEdgeFactor))) * reflectionModulation;
    lighting.SpecularDirect *= sunColor;
    returnColor += lighting.SpecularDirect;

    float oilIridHue = fract(fma(g_flTime, 0.1, fma(fresnel, 20.0, fd.DebrisHeight * 8.0)));
    vec3 oilIridescence = CalculateIridescence(oilIridHue, 0.75);

    returnColor.xyz *= mix(vec3(1.0), lighting.DiffuseIndirect * 0.75, vec3(saturate(refraction.DebrisCoverage * 4.0) * inverseWaterFogAlpha));

    reflectionMix = vec3(saturate(reflectionModulation));
    vec3 oilSpecularFactor = vec3(((saturate(fd.Suppression * 20.0) * g_flDebrisOilyness) / fma(view.DistanceToFrag, 0.005, 1.0)) * saturate(fma(-surface.RefractedVerticalFactor, 5.0, 1.0)));

    returnColor = mix(returnColor, mix(finalReflectionColor, finalReflectionColor * oilIridescence, oilSpecularFactor), reflectionMix);

    ApplyFog(returnColor, surface.Position);

    DitherIntoSkybox(surfacePositionWs, blueNoise.x);

    edgeBlend = GetEdgeBlend(surface, effectiveWaterDepthForFog);

    if (F_REFRACTION == 1 && !g_bIsSkybox)
    {
        vec3 sceneColor = surface.Scene.Color.xyz * mix(1.0, 0.6, saturate(surface.RefractedVerticalFactor * 60.0) / fma(view.DistanceToFrag, 0.002, 1.0));
        returnColor = mix(sceneColor, returnColor, vec3(edgeBlend));
    }

    return returnColor;
}

void HandleWaterRenderModes(inout vec4 color, inout MaterialProperties_t mat, WaterSurface_t surface, LightingTerms_t lighting,
    vec3 reflectionColor, float edgeBlend)
{
    mat.WaterEdgeBlend = 1.0 - edgeBlend;
    mat.WaterColumnDepth = surface.AdjustedColumnDepth;

    if (HandleMaterialRenderModes(color, mat)
    || HandleUVRenderModes(color, mat, g_tDebris, vTexCoordOut)
    || HandleLightingRenderModes(color, mat, lighting))
    {
        return;
    }

    if (g_iRenderMode == renderMode_Cubemaps)
    {
        color.rgb = SrgbLinearToGamma(reflectionColor);
    }
    else if (g_iRenderMode == renderMode_TerrainBlend)
    {
        color.rgb = vColorBlendValues.xyz;
    }
}

void main()
{
    MaterialProperties_t mat;
    InitProperties(mat, vNormalOut, vFragPosition, vTangentOut.xyz, GetBitangent(vNormalOut, vTangentOut));

    vec4 blueNoise = texelFetch(g_tBlueNoise, ivec2(mod(gl_FragCoord.xy, textureSize(g_tBlueNoise, 0))), 0);

    WaterSurface_t surface = GetWaterSurface(mat, blueNoise);

    LightingTerms_t lighting;
    vec3 reflectionColor;
    vec3 reflectionMix;
    float edgeBlend;
    vec3 color = ShadeWater(mat, surface, blueNoise, lighting, reflectionColor, reflectionMix, edgeBlend);

    outputColor = vec4(color, 1.0);

    if (g_iRenderMode != 0)
    {
        lighting.SpecularIndirect = reflectionColor * reflectionMix;
        HandleWaterRenderModes(outputColor, mat, surface, lighting, reflectionColor, edgeBlend);
    }
}
