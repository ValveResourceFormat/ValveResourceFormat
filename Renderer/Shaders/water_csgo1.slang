import "common/ViewConstants.slang";

[__AttributeUsage(_AttributeTargets.Var)]
struct DefaultAttribute
{
    float value;
};

[__AttributeUsage(_AttributeTargets.Var)]
struct Default2Attribute
{
    float value0;
    float value1;
};

[__AttributeUsage(_AttributeTargets.Var)]
struct Default3Attribute
{
    float value0;
    float value1;
    float value2;
};
[__AttributeUsage(_AttributeTargets.Var)]
struct Default4Attribute
{
    float value0;
    float value1;
    float value2;
    float value3;
};

[__AttributeUsage(_AttributeTargets.Var)]
struct SrgbReadAttribute {};

[Default(0.0)] float g_flWaterPlaneOffset;
[Default(16.0)] float g_flSkyBoxScale;
[Default(0.05)] float g_flSkyBoxFadeRange;
[Default2(-10272, -10199)] float2 g_vMapUVMin;
[Default2(10241, 10241)] float2 g_vMapUVMax;
[Default(1.0)] float g_flLowEndCubeMapIntensity;
[Default(0.25)] float g_flWaterRoughnessMin;
[Default(1.0)] float g_flWaterRoughnessMax;
[Default(0.0)] float g_flFoamMin;
[Default(1.0)] float g_flFoamMax;
[Default(0.0)] float g_flDebrisMin;
[Default(1.0)] float g_flDebrisMax;
[Default3(0.7, 0.7, 0.7)] [SrgbRead] float3 g_vDebrisTint;
[Default(0.1)] float g_flDebrisReflectance;
[Default(0.1)] float g_flDebrisOilyness;
[Default(1.0)] float g_flDebrisNormalStrength;
[Default(10.0)] float g_flDebrisEdgeSharpness;
[Default(1.0)] float g_flDebrisScale;
[Default(1.0)] float g_flDebrisWobble;
[Default(1.0)] float g_flFoamScale;
[Default(1.0)] float g_flFoamWobble;
[Default4(0.7, 0.7, 0.7, 1.0)] [SrgbRead] float4 g_vFoamColor;
[Default(1.0)] float g_flWavesHeightOffset;
[Default(0.5)] float g_flWavesSharpness;
[Default(5.0)] float g_flFresnelExponent;
[Default(1.0)] float g_flWavesNormalStrength;
[Default(0.1)] float g_flWavesNormalJitter;
[Default2(15.0, 15.0)] float2 g_vWaveScale;
[Default(1.5)] float g_flWaterInitialDirection;
[Default(1.0)] float g_flWavesSpeed;
[Default(0.5)] float g_flLowFreqWeight;
[Default(0.5)] float g_flMedFreqWeight;
[Default(0.5)] float g_flHighFreqWeight;
[Default(0.4)] float g_flWavesPhaseOffset;
[Default(10.0)] float g_flEdgeHardness;
[Default(1.0)] float g_flEdgeShapeEffect;
[Default(3)] int g_nWaveIterations;
[Default3(0.5, 0.5, 0.5)] [SrgbRead] float3 g_vWaterFogColor;
[Default(0.1)] float g_flRefractionLimit;
[Default(0.5)] float g_flWaterFogStrength;
[Default(2.0)] float g_flRefractSampleOffset;
[Default(0.5)] float g_flRefractChromaticSeparation;
[Default3(1.0, 1.0, 1.0)] float3 g_vWaterDecayColor; // not converted to linear
[Default(8.0)] float g_flWaterDecayStrength;
[Default(100.0)] float g_flWaterMaxDepth;
[Default(0.5)] float g_flWaterFogShadowStrength;
[Default(0.6)] float g_flUnderwaterDarkening;
[Default(300.0)] float g_flSpecularPower;
[Default(2.0)] float g_flSpecularNormalMultiple;
[Default(100.0)] float g_flSpecularBloomBoostStrength;
[Default(0.7)] float g_flSpecularBloomBoostThreshold;
int g_bUseTriplanarCaustics;
[Default(2.5)] float g_flCausticUVScaleMultiple;
[Default(0.5)] float g_flCausticDistortion;
[Default(40.0)] float g_flCausticsStrength;
[Default(0.5)] float g_flCausticSharpness;
[Default(100.0)] float g_flCausticDepthFallOffDistance;
[Default(0.2)] float g_flCausticShadowCutOff;
[Default4(0.5, 0.5, 0.5, 1.0)] float4 g_vCausticsTint; // not converted to linear
[Default(0.2)] float g_flReflectance;
[Default(0.5)] float g_flReflectionDistanceEffect;
[Default(1.0)] float g_flForceMixResolutionScale;
[Default(1.0)] float g_flEnvironmentMapBrightness;
[Default(0.75)] float g_flGlossiness;
[Default(0.1)] float g_flSSRStepSize;
[Default(0.02)] float g_flSSRSampleJitter;
[Default(20)] int g_nSSRMaxForwardSteps;
[Default(1.0)] float g_flSSRBoostThreshold;
[Default(0.0)] float g_flSSRBoost;
[Default(1.0)] float g_flSSRBrightness;
[Default(4.0)] float g_flSSRMaxThickness;
[Default(1.0)] float g_flWaterEffectsRippleStrength;
[Default(1.0)] float g_flWaterEffectSiltStrength;
[Default(1.0)] float g_flWaterEffectFoamStrength;
[Default(1.0)] float g_flWaterEffectDisturbanceStrength;
[Default(1.0)] float g_flWaterEffectCausticStrength;
#define g_vRoughness float2(max(.01, 1.0 - g_flGlossiness))
float4 g_vViewportExtentsTs;

struct VertexOutput
{
    float4 vRasterPosition : SV_Position;
    float4 vBlendArguments;
}


[shader("vertex")]
VertexOutput vertexMain(
     float3 vPOSITION,
     float4 vTEXCOORD4
)
{
    VertexOutput vOut;
    vOut.vRasterPosition = mul(g_matWorldToProjection, float4(vPOSITION, 1.0));
    vOut.vBlendArguments = vTEXCOORD4;
    return vOut;
}



[shader("fragment")]
float4 fragmentMain(VertexOutput vOut) : SV_Target
{
    return float4(g_vWaterFogColor, 1.0);
}
