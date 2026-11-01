#version 460

#include "../hlvr/vr_energy_field.slang"

uniform sampler2D g_tSelfIllumMask; // SrgbRead(true)
uniform sampler2D g_tSelfIllumFlowPhase; // SrgbRead(true)
uniform sampler2D g_tSelfIllumFlowPhaseDetail01; // SrgbRead(true)
uniform sampler2D g_tSelfIllumFlowPhaseDetail02; // SrgbRead(true)

uniform float g_flDirectPlayValuePass1Offset = -0.5;
uniform float g_flDirectPlayValuePass1Scale = 2.0;

FlowSamples_t SampleFlow(vec2 texCoord, vec2 maskCoords, vec2 detail01Coords, vec2 detail02Coords, float flowDistance)
{
    FlowSamples_t flow;
    flow.SelfIllumMask = texture(g_tSelfIllumMask, maskCoords).r;
    flow.PhaseDetail01 = texture(g_tSelfIllumFlowPhaseDetail01, detail01Coords).r;
    flow.PhaseDetail02 = texture(g_tSelfIllumFlowPhaseDetail02, detail02Coords).r;
    flow.Phase = 1.0 - mix(texture(g_tSelfIllumFlowPhase, texCoord).r, pow2(flowDistance), g_flWorldSpaceFlowWeight);
    flow.Frequency = g_flSelfIllumFlowFrequency;
    flow.FrequencyPass2 = g_flSelfIllumFlowFrequencyPass2;
    flow.PlayFrequency = g_flSelfIllumFlowFrequency;
    flow.PlayOffset = g_flDirectPlayValuePass1 * g_flDirectPlayValuePass1Scale + g_flDirectPlayValuePass1Offset;
    return flow;
}

void GetFlowPasses(float wavePosition, float wavePositionPass2, out vec3 flowPass1, out vec3 flowPass2)
{
    vec4 wave = texture(g_tSelfIllumFlowWaveform, vec2(wavePosition, 0.0));
    if (wave.x > 0.5)
    {
        wave *= 10.0;
    }

#if (F_RGB_WAVEFORM == 1)
    flowPass1 = wave.rgb / 3.0 * g_flSelfIllumFlowAmount;
    flowPass2 = SampleWaveform(g_tSelfIllumFlowWaveformPass2, wavePositionPass2) * g_flSelfIllumFlowAmountPass2 * flowPass1;
#else
    flowPass1 = vec3(wave.x * g_flSelfIllumFlowAmount);
    flowPass2 = SampleWaveform(g_tSelfIllumFlowWaveformPass2, wavePositionPass2) * g_flSelfIllumFlowAmountPass2 * flowPass1 * 100.0;
#endif
}

MaterialProperties_t GetMaterial(vec2 texCoord, vec3 vertexNormals)
{
    return GetEnergyFieldMaterial(texCoord, vertexNormals);
}
