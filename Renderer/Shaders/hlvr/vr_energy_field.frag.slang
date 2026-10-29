#version 460

#include "vr_energy_field.slang"

// R: self-illum mask, G: flow phase, B and A: the two phase details
uniform sampler2D g_tFlowParameters; // SrgbRead(true)

FlowSamples_t SampleFlow(vec2 texCoord, vec2 maskCoords, vec2 detail01Coords, vec2 detail02Coords, float flowDistance)
{
    FlowSamples_t flow;
    flow.SelfIllumMask = texture(g_tFlowParameters, maskCoords).r;
    flow.PhaseDetail01 = texture(g_tFlowParameters, detail01Coords).b;
    flow.PhaseDetail02 = texture(g_tFlowParameters, detail02Coords).a;
    flow.Phase = 1.0 - mix(1.0 - texture(g_tFlowParameters, texCoord).g, flowDistance, g_flWorldSpaceFlowWeight);
    flow.Frequency = exp2(g_flSelfIllumFlowFrequency);
    flow.FrequencyPass2 = exp2(g_flSelfIllumFlowFrequencyPass2);
    flow.PlayFrequency = 1.0;
    flow.PlayOffset = g_flDirectPlayValuePass1 * 2.0 - 1.0;
    return flow;
}

void GetFlowPasses(float wavePosition, float wavePositionPass2, out vec3 flowPass1, out vec3 flowPass2)
{
    flowPass1 = SampleWaveform(g_tSelfIllumFlowWaveform, wavePosition) * g_flSelfIllumFlowAmount + (1.0 - g_flSelfIllumFlowAmount);
    flowPass2 = SampleWaveform(g_tSelfIllumFlowWaveformPass2, wavePositionPass2) * g_flSelfIllumFlowAmountPass2 + (1.0 - g_flSelfIllumFlowAmountPass2);
}

MaterialProperties_t GetMaterial(vec2 texCoord, vec3 vertexNormals)
{
    return GetEnergyFieldMaterial(texCoord, vertexNormals);
}
