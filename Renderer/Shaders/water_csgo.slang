import "common/ViewConstants.slang";



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
    return float4(vOut.vBlendArguments.z * fmod(g_flTime, 1.0));
}
