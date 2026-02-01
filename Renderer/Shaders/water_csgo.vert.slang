#version 460
#include "common/utils.glSlang"
import "common/instancing.slang";

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

[Default(0.0)] uniform float g_flWaterPlaneOffset;
[Default(16.0)] uniform float g_flSkyBoxScale;
[Default(0.05)] uniform float g_flSkyBoxFadeRange;
[Default2(-10272, -10199)] uniform float2 g_vMapUVMin;
[Default2(10241, 10241)] uniform float2 g_vMapUVMax;
[Default(1.0)] uniform float g_flLowEndCubeMapIntensity;
[Default(0.25)] uniform float g_flWaterRoughnessMin;
[Default(1.0)] uniform float g_flWaterRoughnessMax;
[Default(0.0)] uniform float g_flFoamMin;
[Default(1.0)] uniform float g_flFoamMax;
[Default(0.0)] uniform float g_flDebrisMin;
[Default(1.0)] uniform float g_flDebrisMax;
[Default3(0.7, 0.7, 0.7)] [SrgbRead] uniform float3 g_vDebrisTint;
[Default(0.1)] uniform float g_flDebrisReflectance;
[Default(0.1)] uniform float g_flDebrisOilyness;
[Default(1.0)] uniform float g_flDebrisNormalStrength;
[Default(10.0)] uniform float g_flDebrisEdgeSharpness;
[Default(1.0)] uniform float g_flDebrisScale;
[Default(1.0)] uniform float g_flDebrisWobble;
[Default(1.0)] uniform float g_flFoamScale;
[Default(1.0)] uniform float g_flFoamWobble;
[Default4(0.7, 0.7, 0.7, 1.0)] [SrgbRead] uniform float4 g_vFoamColor;
[Default(1.0)] uniform float g_flWavesHeightOffset;
[Default(0.5)] uniform float g_flWavesSharpness;
[Default(5.0)] uniform float g_flFresnelExponent;
[Default(1.0)] uniform float g_flWavesNormalStrength;
[Default(0.1)] uniform float g_flWavesNormalJitter;
[Default2(15.0, 15.0)] uniform float2 g_vWaveScale;
[Default(1.5)] uniform float g_flWaterInitialDirection;
[Default(1.0)] uniform float g_flWavesSpeed;
[Default(0.5)] uniform float g_flLowFreqWeight;
[Default(0.5)] uniform float g_flMedFreqWeight;
[Default(0.5)] uniform float g_flHighFreqWeight;
[Default(0.4)] uniform float g_flWavesPhaseOffset;
[Default(10.0)] uniform float g_flEdgeHardness;
[Default(1.0)] uniform float g_flEdgeShapeEffect;
[Default(3)] int g_nWaveIterations;
[Default3(0.5, 0.5, 0.5)] [SrgbRead] uniform float3 g_vWaterFogColor;
[Default(0.1)] uniform float g_flRefractionLimit;
[Default(0.5)] uniform float g_flWaterFogStrength;
[Default(2.0)] uniform float g_flRefractSampleOffset;
[Default(0.5)] uniform float g_flRefractChromaticSeparation;
[Default3(1.0, 1.0, 1.0)] uniform float3 g_vWaterDecayColor; // not converted to linear
[Default(8.0)] uniform float g_flWaterDecayStrength;
[Default(100.0)] uniform float g_flWaterMaxDepth;
[Default(0.5)] uniform float g_flWaterFogShadowStrength;
[Default(0.6)] uniform float g_flUnderwaterDarkening;
[Default(300.0)] uniform float g_flSpecularPower;
[Default(2.0)] uniform float g_flSpecularNormalMultiple;
[Default(100.0)] uniform float g_flSpecularBloomBoostStrength;
[Default(0.7)] uniform float g_flSpecularBloomBoostThreshold;
uniform int g_bUseTriplanarCaustics;
[Default(2.5)] uniform float g_flCausticUVScaleMultiple;
[Default(0.5)] uniform float g_flCausticDistortion;
[Default(40.0)] uniform float g_flCausticsStrength;
[Default(0.5)] uniform float g_flCausticSharpness;
[Default(100.0)] uniform float g_flCausticDepthFallOffDistance;
[Default(0.2)] uniform float g_flCausticShadowCutOff;
[Default4(0.5, 0.5, 0.5, 1.0)] uniform float4 g_vCausticsTint; // not converted to linear
[Default(0.2)] uniform float g_flReflectance;
[Default(0.5)] uniform float g_flReflectionDistanceEffect;
[Default(1.0)] uniform float g_flForceMixResolutionScale;
[Default(1.0)] uniform float g_flEnvironmentMapBrightness;
[Default(0.75)] uniform float g_flGlossiness;
[Default(0.1)] uniform float g_flSSRStepSize;
[Default(0.02)] uniform float g_flSSRSampleJitter;
[Default(20)] uniform int g_nSSRMaxForwardSteps;
[Default(1.0)] uniform float g_flSSRBoostThreshold;
[Default(0.0)] uniform float g_flSSRBoost;
[Default(1.0)] uniform float g_flSSRBrightness;
[Default(4.0)] uniform float g_flSSRMaxThickness;
[Default(1.0)] uniform float g_flWaterEffectsRippleStrength;
[Default(1.0)] uniform float g_flWaterEffectSiltStrength;
[Default(1.0)] uniform float g_flWaterEffectFoamStrength;
[Default(1.0)] uniform float g_flWaterEffectDisturbanceStrength;
[Default(1.0)] uniform float g_flWaterEffectCausticStrength;
#define g_vRoughness float2(max(.01, 1.0 - g_flGlossiness))
float4 g_vViewportExtentsTs;

Sampler2D g_tBlueNoise;
Sampler2D g_tFoam;
[SrgbRead] uniform sampler2D g_tDebris;
Sampler2D g_tDebrisNormal;

// uniform sampler2D g_tWaterEffectsMap;
Sampler2D g_tWavesNormalHeight;

float4 g_vSimpleSkyReflectionColor = vec4(1.0);

Sampler2D g_tSceneColor;
Sampler2D g_tSceneDepth;

#include "common/compression.glSlang"

struct VertexOutput
{
    vec4 vRasterPosition : SV_Position;
    vec3 vFragPosition;
    vec2 vTexCoordOut;
    vec3 vNormalOut;
    vec3 vTangentOut;
    vec3 vBitangentOut;
    vec4 vColorBlendValues;
}

#include "common/features.glSlang"

import "common/LightingConstants.slang";

#if (D_BAKED_LIGHTING_FROM_LIGHTMAP == 1)
    in vec2 vLightmapUV;
    out vec3 vLightmapUVScaled;
#elif D_BAKED_LIGHTING_FROM_VERTEX_STREAM == 1
    in vec4 vPerVertexLighting;
    out vec3 vPerVertexLightingOut;
#endif

import "common/ViewConstants.slang";


float4 rasterPos : SV_Position;

[shader("vertex")]
VertexOutput vertexMain(
    vec3 vPOSITION,
    vec2 vTEXCOORD,
    vec4 vNORMAL,
    vec4 vTANGENT,
    vec4 vCOLOR,
    vec4 vTEXCOORD4
)
{
    VertexOutput vOut;
    vec4 tangent;
    vOut.vNormalOut = vNORMAL.xyz;
    vOut.vTangentOut = tangent.xyz;
    vOut.vBitangentOut = tangent.w * cross(vOut.vNormalOut, vOut.vTangentOut);

    vec4 vPositionWs = CalculateObjectToWorldMatrix() * vec4(vPOSITION, 1.0);

    //Here comes some of the greatest fucking bullshit I have ever seen
    vec3 vertDir = normalize(vPositionWs.xyz - g_vCameraPositionWs);
    vec2 parallaxOffset = vertDir.xy / -vertDir.z * 0.7;   // like ???? what the fuck
    //vPositionWs.xyz += (vOut.vNormalOut.xyz * g_flWaterPlaneOffset);
    //vPositionWs.xy += ((-(normalize(parallaxOffset) * clamp(length(parallaxOffset), 0.0, 20.0))) * g_flWaterPlaneOffset);

    vOut.vRasterPosition = float4(vPOSITION, 1.0) * g_matWorldToProjection;
    vOut.vFragPosition = vPositionWs.xyz;
    return vOut;

    #if (D_BAKED_LIGHTING_FROM_LIGHTMAP == 1)
        vLightmapUVScaled = vec3(vLightmapUV * g_vLightmapUvScale.xy, 0);
    #elif (D_BAKED_LIGHTING_FROM_VERTEX_STREAM == 1)
        vec3 Light = vPerVertexLighting.rgb * 6.0 * vPerVertexLighting.a;
        vPerVertexLightingOut = pow2(Light);
    #endif

    vOut.vTexCoordOut = vTEXCOORD;
    vOut.vColorBlendValues = vTEXCOORD4;

    return vOut;
}

