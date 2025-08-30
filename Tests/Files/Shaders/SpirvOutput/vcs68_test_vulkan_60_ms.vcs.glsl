// SPIR-V source (2492 bytes), HLSL reflection with SPIRV-Cross by KhronosGroup
// 

static float _24362;

ByteAddressBuffer g_inputVB : register(t31, space3);

static uint3 gl_GlobalInvocationID;
struct SPIRV_Cross_Input
{
    uint3 gl_GlobalInvocationID : SV_DispatchThreadID;
};

struct gl_MeshPerVertexEXT
{
    float3 output_0 : TEXCOORD0;
    float2 output_1 : TEXCOORD1;
    float4 gl_Position : SV_Position;
};

struct gl_MeshPerPrimitiveEXT
{
};

void MainMs_inner(inout gl_MeshPerVertexEXT gl_MeshVerticesEXT[4], inout uint3 gl_PrimitiveTriangleIndicesEXT[2])
{
    SetMeshOutputCounts(4u, 2u);
    int _15567 = int(gl_GlobalInvocationID.x * 36u);
    gl_MeshVerticesEXT[gl_GlobalInvocationID.x].output_0.x = asfloat(g_inputVB.Load(((_15567 + 12) / 4) * 4 + 0));
    gl_MeshVerticesEXT[gl_GlobalInvocationID.x].output_0.y = asfloat(g_inputVB.Load(((_15567 + 16) / 4) * 4 + 0));
    gl_MeshVerticesEXT[gl_GlobalInvocationID.x].output_0.z = asfloat(g_inputVB.Load(((_15567 + 20) / 4) * 4 + 0));
    gl_MeshVerticesEXT[gl_GlobalInvocationID.x].output_1.x = asfloat(g_inputVB.Load(((_15567 + 28) / 4) * 4 + 0));
    gl_MeshVerticesEXT[gl_GlobalInvocationID.x].output_1.y = asfloat(g_inputVB.Load(((_15567 + 32) / 4) * 4 + 0));
    float4 _15926 = float4(asfloat(g_inputVB.Load((_15567 / 4) * 4 + 0)), _24362, asfloat(g_inputVB.Load(((_15567 + 8) / 4) * 4 + 0)), 1.0f);
    _15926.y = asfloat(g_inputVB.Load(((_15567 + 4) / 4) * 4 + 0));
    gl_MeshVerticesEXT[gl_GlobalInvocationID.x].gl_Position = _15926;
    if (gl_GlobalInvocationID.x == 0u)
    {
        gl_PrimitiveTriangleIndicesEXT[0u] = uint3(2u, 1u, 0u);
    }
    else
    {
        if (gl_GlobalInvocationID.x == 1u)
        {
            gl_PrimitiveTriangleIndicesEXT[1u] = uint3(2u, 0u, 3u);
        }
    }
}

[outputtopology("triangle")]
[numthreads(4, 1, 1)]
void MainMs(SPIRV_Cross_Input stage_input, out vertices gl_MeshPerVertexEXT gl_MeshVerticesEXT[4], out indices uint3 gl_PrimitiveTriangleIndicesEXT[2])
{
    gl_GlobalInvocationID = stage_input.gl_GlobalInvocationID;
    MainMs_inner(gl_MeshVerticesEXT, gl_PrimitiveTriangleIndicesEXT);
}

