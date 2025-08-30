// SPIR-V source (2632 bytes), HLSL reflection with SPIRV-Cross by KhronosGroup
// 

struct _1125
{
    uint _m0;
    uint _m1;
    uint _m2;
    uint _m3;
    uint _m4;
    uint _m5;
    uint _m6;
    float _m7;
};

struct _730
{
    float4 _m0[3];
};

struct _2419
{
    _730 _m0;
};

struct anon_g_matWorldToProjection
{
    float4 _m0[4];
};

ByteAddressBuffer g_instanceBuffer : register(t32, space0);
ByteAddressBuffer g_transformBuffer : register(t30, space0);
cbuffer PerViewConstantBuffer_t : register(b1, space0)
{
    anon_g_matWorldToProjection PerViewConstantBuffer_t_1_g_matWorldToProjection : packoffset(c0);
    float4 PerViewConstantBuffer_t_1_g_vWorldToCameraOffset : packoffset(c33);
};


static float4 gl_Position;
static float3 vPositionOs;
static uint nInstanceIdx;

struct VS_INPUT
{
    float3 vPositionOs : TEXCOORD0;
    uint nInstanceIdx : TEXCOORD1;
};

struct PS_INPUT
{
    float4 gl_Position : SV_Position;
};

void MainVs_inner()
{
    _730 _25207;
    [unroll]
    for (int _1ident = 0; _1ident < 3; _1ident++)
    {
        _25207._m0[_1ident] = asfloat(g_transformBuffer.Load4(_1ident * 16 + g_instanceBuffer.Load(nInstanceIdx * 32 + 4) * 48 + 0));
    }
    float4 _24787 = mul(float4x4(float4(PerViewConstantBuffer_t_1_g_matWorldToProjection._m0[0].x, PerViewConstantBuffer_t_1_g_matWorldToProjection._m0[1].x, PerViewConstantBuffer_t_1_g_matWorldToProjection._m0[2].x, PerViewConstantBuffer_t_1_g_matWorldToProjection._m0[3].x), float4(PerViewConstantBuffer_t_1_g_matWorldToProjection._m0[0].y, PerViewConstantBuffer_t_1_g_matWorldToProjection._m0[1].y, PerViewConstantBuffer_t_1_g_matWorldToProjection._m0[2].y, PerViewConstantBuffer_t_1_g_matWorldToProjection._m0[3].y), float4(PerViewConstantBuffer_t_1_g_matWorldToProjection._m0[0].z, PerViewConstantBuffer_t_1_g_matWorldToProjection._m0[1].z, PerViewConstantBuffer_t_1_g_matWorldToProjection._m0[2].z, PerViewConstantBuffer_t_1_g_matWorldToProjection._m0[3].z), float4(PerViewConstantBuffer_t_1_g_matWorldToProjection._m0[0].w, PerViewConstantBuffer_t_1_g_matWorldToProjection._m0[1].w, PerViewConstantBuffer_t_1_g_matWorldToProjection._m0[2].w, PerViewConstantBuffer_t_1_g_matWorldToProjection._m0[3].w)), (float4(mul(float3x4(_25207._m0[0], _25207._m0[1], _25207._m0[2]), float4(vPositionOs.xyz, 1.0f)).xyz, 1.0f) + (PerViewConstantBuffer_t_1_g_vWorldToCameraOffset * 1.0f)).xyzw);
    _24787.y = -_24787.y;
    gl_Position = _24787;
}

PS_INPUT MainVs(VS_INPUT stage_input)
{
    vPositionOs = stage_input.vPositionOs;
    nInstanceIdx = stage_input.nInstanceIdx;
    MainVs_inner();
    PS_INPUT stage_output;
    stage_output.gl_Position = gl_Position;
    return stage_output;
}

