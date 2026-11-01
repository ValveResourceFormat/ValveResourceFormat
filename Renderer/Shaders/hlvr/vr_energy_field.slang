#version 460

// The entry file declares the textures of the flow and defines SampleFlow() and GetFlowPasses() for them, whether
// the mask, the flow phase and its two details are textures of their own or the channels of one.

#include "vr_energy_field_features.slang"

#define IBL 1
#define SUBSURFACE 0
#define DIFFUSE_WRAP 0
#define ANISO_CUBEMAP_WARP_OPTIONAL 0

#include "../common/surface_base_ps.slang"
#include "../common/surface_material.slang"
#include "../common/surface_shading.slang"
#include "self_illum_flow_ps.slang"

#define PREMULTIPLIED ((F_TRANSLUCENT == 1) || (F_TRANSLUCENT == 2))
#define ADDITIVE (F_TRANSLUCENT == 3)

uniform sampler2D g_tColor; // SrgbRead(true) Sampler(UserConfig) RepresentativeTexture

uniform float g_flBaseAlpha = 0.0;
uniform float g_flBaseFresnel = 0.0;

uniform float g_flDissolveReferenceValue = 0.0;
uniform float g_flDissolveEdgeWidth = 0.0;
uniform float g_flDissolveEdgeIntensity = 0.0;
uniform vec3 g_vDissolveEdgeColor = vec3(1.0);
uniform float g_flDissolveEdgeDetailStrength = 0.0;
uniform float g_flDissolveEdgeDetailOffset = 0.0;
uniform float g_flDissolveEdgeSharpness = 50.0;

uniform vec2 g_vSelfIllumMaskUVScale = vec2(1.0);
uniform vec2 g_vSelfIllumMaskUVOffset = vec2(0.0);
uniform vec3 g_vWorldSpaceFlowOrigin = vec3(0.0);
uniform float g_flWorldSpaceFlowScale = 1.0;
uniform float g_flWorldSpaceFlowWeight = 0.0;
uniform vec2 g_vSelfIllumFlowPhaseDetail01UVScale = vec2(1.0);
uniform vec2 g_vSelfIllumFlowPhaseDetail01UVOffset = vec2(0.0);
uniform float g_flSelfIllumFlowPhaseDetail01Strength = 1.0;
uniform vec2 g_vSelfIllumFlowPhaseDetail02UVScale = vec2(1.0);
uniform vec2 g_vSelfIllumFlowPhaseDetail02UVOffset = vec2(0.0);
uniform float g_flSelfIllumFlowPhaseDetail02Strength = 1.0;
uniform float g_flSelfIllumFresnel = 0.0;

uniform sampler2D g_tSelfIllumFlowWaveform;
uniform float g_flSelfIllumFlowPhaseDetailStrength = 1.0;
uniform vec3 g_vSelfIllumTint = vec3(1.0); // SrgbRead(true)
uniform float g_flSelfIllumOverallScale = 1.0;
uniform float g_flSelfIllumBrightness = 0.0;
uniform float g_flSelfIllumFlowAmount = 0.0;
uniform float g_flSelfIllumFlowFrequency = 1.0;
uniform float g_flSelfIllumFlowSpeed = 0.0;
uniform float g_flSelfIllumCustomFlowTimer = 0.0;
uniform float g_flDirectPlayValuePass1 = 0.0;

uniform sampler2D g_tSelfIllumFlowWaveformPass2;
uniform float g_flSelfIllumFlowPhaseDetailStrengthPass2 = 1.0;
uniform vec3 g_vSelfIllumTintPass2 = vec3(1.0); // SrgbRead(true)
uniform float g_flSelfIllumOverallScalePass2 = 1.0;
uniform float g_flSelfIllumBrightnessPass2 = 0.0;
uniform float g_flSelfIllumFlowAmountPass2 = 0.0;
uniform float g_flSelfIllumFlowFrequencyPass2 = 1.0;
uniform float g_flSelfIllumFlowSpeedPass2 = 0.0;
uniform float g_flSelfIllumCustomFlowTimerPass2 = 0.0;

struct FlowSamples_t
{
    float SelfIllumMask;
    float PhaseDetail01;
    float PhaseDetail02;
    float Phase;
    float Frequency;
    float FrequencyPass2;
    float PlayFrequency;
    float PlayOffset;
};

FlowSamples_t SampleFlow(vec2 texCoord, vec2 maskCoords, vec2 detail01Coords, vec2 detail02Coords, float flowDistance);
void GetFlowPasses(float wavePosition, float wavePositionPass2, out vec3 flowPass1, out vec3 flowPass2);

// A positive exponent fades towards the silhouette, a negative one towards the center
float GetFresnelMask(float NdotV, float exponent)
{
    float biasedExponent = exponent + 0.00101;
    float facingExponent = max(biasedExponent, 0.0);
    float grazingExponent = max(-biasedExponent, 0.0);
    return pow(NdotV, facingExponent) * sign(facingExponent) + pow(1.0 - NdotV, grazingExponent) * sign(grazingExponent);
}

vec3 SampleWaveform(sampler2D waveform, float position)
{
    vec4 wave = texture(waveform, vec2(position, 0.0));

#if (F_RGB_WAVEFORM == 1)
    return wave.rgb / 3.0;
#else
    return wave.rrr;
#endif
}

float GetPhaseDetail(FlowSamples_t flowSamples)
{
    return (flowSamples.PhaseDetail01 - 0.5) * g_flSelfIllumFlowPhaseDetail01Strength + (flowSamples.PhaseDetail02 - 0.5) * g_flSelfIllumFlowPhaseDetail02Strength;
}

// The color of the two flow passes, tinted by the vertex color
vec3 GetEnergyFlow(FlowSamples_t flowSamples, float phaseDetail)
{
    float phasePass1 = flowSamples.Phase + phaseDetail * g_flSelfIllumFlowPhaseDetailStrength;
    float phasePass2 = flowSamples.Phase + phaseDetail * g_flSelfIllumFlowPhaseDetailStrengthPass2;

#if (F_PLAY_DIRECTLY_AND_CLAMP_PASS_1 == 1)
    float wavePosition = saturate(phasePass1 * flowSamples.PlayFrequency + flowSamples.PlayOffset);
#else
    float wavePosition = GetFlowTime(g_flSelfIllumFlowSpeed, g_flSelfIllumCustomFlowTimer) + flowSamples.Frequency * phasePass1;
#endif
    float wavePositionPass2 = GetFlowTime(g_flSelfIllumFlowSpeedPass2, g_flSelfIllumCustomFlowTimerPass2) + flowSamples.FrequencyPass2 * phasePass2;

    vec3 tint = GetSelfIllumTint(g_flSelfIllumOverallScale, g_flSelfIllumBrightness, g_vSelfIllumTint);
    vec3 tintPass2 = GetSelfIllumTint(g_flSelfIllumOverallScalePass2, g_flSelfIllumBrightnessPass2, g_vSelfIllumTintPass2);

    vec3 flowPass1;
    vec3 flowPass2;
    GetFlowPasses(wavePosition, wavePositionPass2, flowPass1, flowPass2);

    return (tint * flowPass1 + tintPass2 * flowPass2) * vVertexColorOut.rgb;
}

void ApplyEnergyFieldColor(inout MaterialProperties_t mat, vec3 color, float opacity, vec3 selfIllum)
{
#if (PREMULTIPLIED)
    // The self-illumination is added over what the opacity lets through, so it is divided by the alpha it is blended with.
    // The fog color is then scaled by that alpha too, the game adds it in full.
    float blendAlpha = max(opacity, 1.0 / 255.0);
    mat.Albedo = color;
    mat.IllumColor = selfIllum / blendAlpha;
    mat.Opacity = blendAlpha;
#else
    mat.Albedo = color * opacity;
    mat.IllumColor = selfIllum;
#endif
}

MaterialProperties_t GetEnergyFieldMaterial(vec2 texCoord, vec3 vertexNormals)
{
    MaterialProperties_t mat;
    InitProperties(mat, vertexNormals, vFragPosition, vTangentOut.xyz, GetBitangent(vNormalOut, vTangentOut));

    vec4 color = texture(g_tColor, texCoord);

    vec2 maskCoords = texCoord * g_vSelfIllumMaskUVScale + g_vSelfIllumMaskUVOffset;
    vec2 detail01Coords = texCoord * g_vSelfIllumFlowPhaseDetail01UVScale + g_vSelfIllumFlowPhaseDetail01UVOffset;
    vec2 detail02Coords = texCoord * g_vSelfIllumFlowPhaseDetail02UVScale + g_vSelfIllumFlowPhaseDetail02UVOffset;
    float flowDistance = length(mat.PositionWS - g_vWorldSpaceFlowOrigin) / g_flWorldSpaceFlowScale;

    FlowSamples_t flowSamples = SampleFlow(texCoord, maskCoords, detail01Coords, detail02Coords, flowDistance);

    float phaseDetail = GetPhaseDetail(flowSamples);
    vec3 flow = GetEnergyFlow(flowSamples, phaseDetail);

    float dissolveDistance = color.a - (g_flDissolveReferenceValue + g_flDissolveEdgeDetailStrength * (phaseDetail - g_flDissolveEdgeDetailOffset));
    float dissolve = saturate(dissolveDistance * g_flDissolveEdgeSharpness);
    float dissolveEdge = saturate((g_flDissolveEdgeWidth - abs(dissolveDistance)) / g_flDissolveEdgeWidth) * g_flDissolveEdgeIntensity;

    vec3 selfIllum = (flow + g_vDissolveEdgeColor * dissolveEdge) * flowSamples.SelfIllumMask;

    // Back faces have no defined fresnel in the game, they are shaded as if seen edge on
    float NdotV = saturate(dot(mat.GeometricNormal, mat.ViewDir));
    float opacity = saturate(g_flBaseAlpha * GetFresnelMask(NdotV, g_flBaseFresnel)) * dissolve;
    selfIllum *= dissolve * saturate(GetFresnelMask(NdotV, g_flSelfIllumFresnel));

    FinishUnlitMaterial(mat);
    ApplyEnergyFieldColor(mat, color.rgb, opacity, selfIllum);

    return mat;
}

// Not lit, the color and the energy flow are written as they are.
// Fog fades the additive color out instead of tinting it.
void main()
{
    SurfaceOptions_t options = DefaultSurfaceOptions();
    options.FogFadesAlpha = ADDITIVE;
    RenderUnlitSurface(g_tColor, options);
}
