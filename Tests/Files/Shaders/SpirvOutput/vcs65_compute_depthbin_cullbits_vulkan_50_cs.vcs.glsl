// SPIR-V source (2540 bytes), HLSL reflection with SPIRV-Cross by KhronosGroup
// 

struct _1017
{
    uint _m0;
    uint _m1;
    uint _m2;
    uint _m3;
};

struct _1000
{
    float _m0;
    float _m1;
};

cbuffer _Globals_ : register(b0, space0)
{
    float _Globals_1_m0 : packoffset(c0.y);
    float _Globals_1_m1 : packoffset(c0.z);
    float _Globals_1_m2 : packoffset(c0.w);
    _1017 _Globals_1_m3[2] : packoffset(c1);
    _1000 _Globals_1_m4[448] : packoffset(c3);
};

RWByteAddressBuffer undetermined : register(u158, space0);

static uint3 gl_GlobalInvocationID;
struct SPIRV_Cross_Input
{
    uint3 gl_GlobalInvocationID : SV_DispatchThreadID;
};

void main_inner()
{
    float _15320 = (_Globals_1_m2 + (float(gl_GlobalInvocationID.x) * _Globals_1_m0)) - _Globals_1_m1;
    float _23639 = (_Globals_1_m2 + (float(gl_GlobalInvocationID.x + 1u) * _Globals_1_m0)) + _Globals_1_m1;
    for (uint _23131 = 0u; _23131 < 2u; _23131++)
    {
        uint _13033;
        for (uint _9864 = _Globals_1_m3[_23131]._m2, _13686 = 0u; _13686 < _Globals_1_m3[_23131]._m1; _9864 = _13033, _13686++)
        {
            uint _11175;
            _11175 = 0u;
            _13033 = _9864;
            uint _10540;
            for (uint _6708 = 0u; (_6708 < 32u) && (_13033 < _Globals_1_m3[_23131]._m3); _11175 = _10540, _13033++, _6708++)
            {
                if ((_Globals_1_m4[_13033]._m0 <= _23639) && (_Globals_1_m4[_13033]._m1 >= _15320))
                {
                    _10540 = _11175 | (1u << _6708);
                }
                else
                {
                    _10540 = _11175;
                }
            }
            undetermined.Store(((_Globals_1_m3[_23131]._m0 + (gl_GlobalInvocationID.x * _Globals_1_m3[_23131]._m1)) + _13686) * 4 + 0, _11175);
        }
    }
}

[numthreads(32, 1, 1)]
void main(SPIRV_Cross_Input stage_input)
{
    gl_GlobalInvocationID = stage_input.gl_GlobalInvocationID;
    main_inner();
}

