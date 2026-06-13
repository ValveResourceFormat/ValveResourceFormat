// VRF-TEST
// SPIR-V source (2540 bytes), HLSL reflection with SPIRV-Cross by KhronosGroup

struct anon_g_Batches
{
    uint _m0;
    uint _m1;
    uint _m2;
    uint _m3;
};

struct anon_g_Items
{
    float _m0;
    float _m1;
};

cbuffer BinCullParams_t : register(b0, space0)
{
    float BinCullParams_t_1_g_fDepthBinWidth : packoffset(c0.y);
    float BinCullParams_t_1_g_fEpsilon : packoffset(c0.z);
    float BinCullParams_t_1_g_fNearPlane : packoffset(c0.w);
    anon_g_Batches BinCullParams_t_1_g_Batches[2] : packoffset(c1);
    anon_g_Items BinCullParams_t_1_g_Items[448] : packoffset(c3);
};

RWByteAddressBuffer undetermined : register(u158, space0);

static uint3 gl_GlobalInvocationID;
struct SPIRV_Cross_Input
{
    uint3 gl_GlobalInvocationID : SV_DispatchThreadID;
};

void main_inner()
{
    float _15320 = (BinCullParams_t_1_g_fNearPlane + (float(gl_GlobalInvocationID.x) * BinCullParams_t_1_g_fDepthBinWidth)) - BinCullParams_t_1_g_fEpsilon;
    float _23639 = (BinCullParams_t_1_g_fNearPlane + (float(gl_GlobalInvocationID.x + 1u) * BinCullParams_t_1_g_fDepthBinWidth)) + BinCullParams_t_1_g_fEpsilon;
    for (uint _23131 = 0u; _23131 < 2u; _23131++)
    {
        uint _13033;
        for (uint _9864 = BinCullParams_t_1_g_Batches[_23131]._m2, _13686 = 0u; _13686 < BinCullParams_t_1_g_Batches[_23131]._m1; _9864 = _13033, _13686++)
        {
            uint _11175;
            _11175 = 0u;
            _13033 = _9864;
            uint _10540;
            for (uint _6708 = 0u; (_6708 < 32u) && (_13033 < BinCullParams_t_1_g_Batches[_23131]._m3); _11175 = _10540, _13033++, _6708++)
            {
                if ((BinCullParams_t_1_g_Items[_13033]._m0 <= _23639) && (BinCullParams_t_1_g_Items[_13033]._m1 >= _15320))
                {
                    _10540 = _11175 | (1u << _6708);
                }
                else
                {
                    _10540 = _11175;
                }
            }
            undetermined.Store(((BinCullParams_t_1_g_Batches[_23131]._m0 + (gl_GlobalInvocationID.x * BinCullParams_t_1_g_Batches[_23131]._m1)) + _13686) * 4 + 0, _11175);
        }
    }
}

[numthreads(32, 1, 1)]
void main(SPIRV_Cross_Input stage_input)
{
    gl_GlobalInvocationID = stage_input.gl_GlobalInvocationID;
    main_inner();
}

