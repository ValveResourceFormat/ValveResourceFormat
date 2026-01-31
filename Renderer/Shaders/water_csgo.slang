import "common/ViewConstants.slang";

import "common/features.slang";
import "common/compression.slang";
import "common/instancing.slang";

struct VertexOutput
{
    float4 vRasterPosition : SV_Position;
    float3 vFragPosition;
    float2 vTexCoordOut;
    float3 vNormalOut;
    float3 vTangentOut;
    float3 vBitangentOut;
    float4 vColorBlendValues;
}

[shader("vertex")]
VertexOutput vertexMain(
    float3 vPOSITION,
    float2 vTEXCOORD,
    float4 vCOLOR,
    float4 vTEXCOORD4
)
{
    VertexOutput Out;
    float4 vPositionWs = float4(0); //mul(CalculateObjectToWorldMatrix(), float4(vPOSITION, 1.0));

    Out.vRasterPosition = vPositionWs; //mul(g_matWorldToProjection, vPositionWs);

    Out.vTexCoordOut = vTEXCOORD;
    Out.vColorBlendValues = vTEXCOORD4;

    return Out;
}

struct FragmentOutput
{
    float4 color : SV_Target;
};

[shader("fragment")]
FragmentOutput fragmentMain(VertexOutput vOut)
{
    FragmentOutput fOut;

    fOut.color = float4(1);
    return fOut;
}
