// SPIR-V source (904 bytes), HLSL reflection with SPIRV-Cross by KhronosGroup
// 

cbuffer _Globals_ : register(b0, space1)
{
    float4 _Globals_1_g_vInvTexDim : packoffset(c0);
};

Texture2D<float4> g_tInputBuffer : register(t30, space1);
SamplerState Filter_20_AddressU_2_AddressV_2 : register(s14, space1);

static float4 gl_FragCoord;
static float4 output_0;

struct PS_INPUT
{
    float4 gl_FragCoord : SV_Position;
};

struct PS_OUTPUT
{
    float4 output_0 : SV_Target0;
};

void MainPs_inner()
{
    output_0 = g_tInputBuffer.SampleLevel(Filter_20_AddressU_2_AddressV_2, (gl_FragCoord.xy * _Globals_1_g_vInvTexDim.xy).xy, 0.0f);
}

PS_OUTPUT MainPs(PS_INPUT stage_input)
{
    gl_FragCoord = stage_input.gl_FragCoord;
    gl_FragCoord.w = 1.0 / gl_FragCoord.w;
    MainPs_inner();
    PS_OUTPUT stage_output;
    stage_output.output_0 = output_0;
    return stage_output;
}

