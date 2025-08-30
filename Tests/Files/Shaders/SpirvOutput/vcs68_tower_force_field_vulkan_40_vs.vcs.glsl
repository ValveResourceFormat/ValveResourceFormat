// SPIR-V reflection failed for backend HLSL:
// cbuffer ID 5618 (name: undetermined), member index 0 (name: _m0) cannot be expressed with either HLSL packing layout or packoffset.
// 
// Re-attempting reflection with the GLSL backend.

// SPIR-V source (9832 bytes), GLSL reflection with SPIRV-Cross by KhronosGroup
// 

#version 460
#if defined(GL_EXT_control_flow_attributes)
#extension GL_EXT_control_flow_attributes : require
#define SPIRV_CROSS_FLATTEN [[flatten]]
#define SPIRV_CROSS_BRANCH [[dont_flatten]]
#define SPIRV_CROSS_UNROLL [[unroll]]
#define SPIRV_CROSS_LOOP [[dont_unroll]]
#else
#define SPIRV_CROSS_FLATTEN
#define SPIRV_CROSS_BRANCH
#define SPIRV_CROSS_UNROLL
#define SPIRV_CROSS_LOOP
#endif

struct _2415
{
    uint _m0[8];
};

struct _2778
{
    _2415 _m0;
};

struct _1890
{
    vec4 _m0[3];
};

struct _1728
{
    _1890 _m0;
};

struct anon_g_matWorldToProjection
{
    vec4 _m0[4];
};

vec4 _17208;

layout(set = 1, binding = 40, std430) readonly buffer g_instanceBuffer
{
    _2778 _m0[];
} g_instanceBuffer_1;

layout(set = 1, binding = 30, std430) readonly buffer g_transformBuffer
{
    _1728 _m0[];
} g_transformBuffer_1;

struct _1391
{
    vec2 _m0;
    vec4 _m1;
    vec2 _m2;
    vec4 _m3;
    float _m4;
    vec3 _m5;
    float _m6;
};

layout(set = 0) uniform _1391 undetermined;

struct _1055
{
    vec3 g_vGlobalLightAmbientColor1;
    vec3 g_vLocalPlayerHeroPosition;
};

layout(set = 1) uniform _1055 DotaGlobalParams_t;

struct _2211
{
    anon_g_matWorldToProjection g_matWorldToProjection;
    vec4 g_vWorldToCameraOffset;
};

layout(set = 1) uniform _2211 PerViewConstantBuffer_t;

layout(location = 0) in vec3 vPositionOs;
layout(location = 1) in vec2 vTexCoord;
layout(location = 2) in uint nPackedFrame;
layout(location = 3) in uvec4 vBlendIndices;
layout(location = 4) in vec4 vBlendWeight;
layout(location = 5) in uint nTransformBufferOffset;
layout(location = 0) out vec4 output_0;
layout(location = 1) out vec2 output_1;
layout(location = 2) out vec3 output_2;
layout(location = 3) out vec4 output_3;
layout(location = 4) out vec4 output_4;
layout(location = 5) out vec3 output_5;
layout(location = 6) out vec4 output_6;

void main()
{
    int _16512 = int(g_instanceBuffer_1._m0[nTransformBufferOffset]._m0._m0[1] + 2u);
    vec4 _20544 = vBlendWeight;
    uvec4 _23546 = vBlendIndices;
    mat3x4 _13155;
    _13155 = mat3x4(g_transformBuffer_1._m0[uint(_16512 + int(vBlendIndices.x))]._m0._m0[0] * vBlendWeight.x, g_transformBuffer_1._m0[uint(_16512 + int(vBlendIndices.x))]._m0._m0[1] * vBlendWeight.x, g_transformBuffer_1._m0[uint(_16512 + int(vBlendIndices.x))]._m0._m0[2] * vBlendWeight.x);
    int _11806;
    mat3x4 _23883;
    int _16208 = 1;
    SPIRV_CROSS_UNROLL
    for (;;)
    {
        _23883 = mat3x4(_13155[0] + (g_transformBuffer_1._m0[uint(_16512 + int(_23546[_16208]))]._m0._m0[0] * _20544[_16208]), _13155[1] + (g_transformBuffer_1._m0[uint(_16512 + int(_23546[_16208]))]._m0._m0[1] * _20544[_16208]), _13155[2] + (g_transformBuffer_1._m0[uint(_16512 + int(_23546[_16208]))]._m0._m0[2] * _20544[_16208]));
        _11806 = _16208 + 1;
        if (!(_11806 < 4))
        {
            break;
        }
        _13155 = _23883;
        _16208 = _11806;
        continue;
    }
    float _11041 = fma(float((nPackedFrame >> 12u) & 1023u), 0.00195503421127796173095703125, -1.0);
    float _17645 = fma(float((nPackedFrame >> 22u) & 1023u), 0.00195503421127796173095703125, -1.0);
    float _23404 = (1.0 - abs(_11041)) - abs(_17645);
    vec3 _8254 = vec3(_11041, _17645, _23404);
    float _24228 = clamp(-_23404, 0.0, 1.0);
    vec2 _7335 = vec2(_11041, _17645);
    vec2 _14348 = _7335 + mix(vec2(_24228), vec2(-_24228), greaterThanEqual(_7335, vec2(0.0)));
    _8254.x = _14348.x;
    _8254.y = _14348.y;
    vec3 _21001 = normalize(_8254);
    float _6384 = _21001.z;
    float _8220 = (_6384 >= 0.0) ? 1.0 : (-1.0);
    float _16417 = (-1.0) / (_8220 + _6384);
    float _14755 = _21001.x;
    vec3 _23176 = vec3(fma((_8220 * _14755) * _14755, _16417, 1.0), _8220 * ((_14755 * _21001.y) * _16417), (-_8220) * _14755);
    float _23966 = float((nPackedFrame >> uint(1)) & 2047u) * 0.003069460391998291015625;
    float _7938 = ((nPackedFrame & 1u) == 0u) ? (-1.0) : 1.0;
    vec3 _11198 = normalize(vec4(_21001.xyz, 0.0) * _23883);
    vec3 _12861 = (vec4(vec4((_23176 * cos(_23966)) + (cross(_21001, _23176) * sin(_23966)), _7938).xyz, 0.0) * _23883).xyz;
    vec3 _24912 = _11198.xyz;
    vec3 _14212 = vec4(vPositionOs.xyz * vec3(g_transformBuffer_1._m0[g_instanceBuffer_1._m0[nTransformBufferOffset]._m0._m0[1]]._m0._m0[0].z).xyz, 1.0) * _23883;
    vec4 _21393;
    _21393.x = dot(undetermined._m1.xy, vTexCoord.xy);
    _21393.y = dot(undetermined._m1.zw, vTexCoord.xy);
    vec4 _17131 = vec4(_21393.xy + undetermined._m0.xy, dot(undetermined._m3.xy, vTexCoord.xy), dot(undetermined._m3.zw, vTexCoord.xy));
    vec2 _7692 = _17131.zw + undetermined._m2.xy;
    vec4 _20489 = _17131;
    _20489.z = _7692.x;
    _20489.w = _7692.y;
    vec2 _13394 = _14212.xy;
    vec2 _15686 = _13394 - DotaGlobalParams_t.g_vLocalPlayerHeroPosition.xy;
    vec2 _9831 = _13394 + (normalize(_15686) * (25.0 * (1.0 - clamp(length(_15686) * 0.006666666828095912933349609375, 0.0, 1.0))));
    float _9979 = _9831.x;
    vec3 _20490 = _14212;
    _20490.x = _9979;
    _20490.y = _9831.y;
    vec4 _23913 = (vec4(_20490.xyz, 1.0) + (PerViewConstantBuffer_t.g_vWorldToCameraOffset * 1.0)).xyzw * mat4(vec4(PerViewConstantBuffer_t.g_matWorldToProjection._m0[0].x, PerViewConstantBuffer_t.g_matWorldToProjection._m0[1].x, PerViewConstantBuffer_t.g_matWorldToProjection._m0[2].x, PerViewConstantBuffer_t.g_matWorldToProjection._m0[3].x), vec4(PerViewConstantBuffer_t.g_matWorldToProjection._m0[0].y, PerViewConstantBuffer_t.g_matWorldToProjection._m0[1].y, PerViewConstantBuffer_t.g_matWorldToProjection._m0[2].y, PerViewConstantBuffer_t.g_matWorldToProjection._m0[3].y), vec4(PerViewConstantBuffer_t.g_matWorldToProjection._m0[0].z, PerViewConstantBuffer_t.g_matWorldToProjection._m0[1].z, PerViewConstantBuffer_t.g_matWorldToProjection._m0[2].z, PerViewConstantBuffer_t.g_matWorldToProjection._m0[3].z), vec4(PerViewConstantBuffer_t.g_matWorldToProjection._m0[0].w, PerViewConstantBuffer_t.g_matWorldToProjection._m0[1].w, PerViewConstantBuffer_t.g_matWorldToProjection._m0[2].w, PerViewConstantBuffer_t.g_matWorldToProjection._m0[3].w));
    vec4 _23812 = vec4(uvec4((g_instanceBuffer_1._m0[nTransformBufferOffset]._m0._m0[0] & 255u) >> uint(0), (g_instanceBuffer_1._m0[nTransformBufferOffset]._m0._m0[0] & 65280u) >> uint(8), (g_instanceBuffer_1._m0[nTransformBufferOffset]._m0._m0[0] & 16711680u) >> uint(16), (g_instanceBuffer_1._m0[nTransformBufferOffset]._m0._m0[0] & 4278190080u) >> uint(24))) * vec4(0.0039215688593685626983642578125);
    vec3 _23075 = _23812.xyz;
    vec3 _16034 = _23075 * vec3(0.077399380505084991455078125);
    vec3 _24533 = pow(fma(_23075, vec3(0.947867333889007568359375), vec3(0.052132703363895416259765625)), vec3(2.400000095367431640625));
    float _21354;
    if (_23812.x <= 0.040449999272823333740234375)
    {
        _21354 = _16034.x;
    }
    else
    {
        _21354 = _24533.x;
    }
    float _21355;
    if (_23812.y <= 0.040449999272823333740234375)
    {
        _21355 = _16034.y;
    }
    else
    {
        _21355 = _24533.y;
    }
    float _19167;
    if (_23812.z <= 0.040449999272823333740234375)
    {
        _19167 = _16034.z;
    }
    else
    {
        _19167 = _24533.z;
    }
    vec4 _16668;
    _16668.x = _21354;
    _16668.y = _21355;
    _16668.z = _19167;
    output_0 = _20489;
    output_1 = vec2(0.0);
    output_2 = _11198;
    output_3 = vec4(normalize(_12861 - (_24912 * dot(_12861, _24912))), _7938);
    output_4 = vec4(mix(vec3(1.0), _16668.xyz, vec3(undetermined._m6)), _23812.w);
    output_5 = vec3(_9979, _9831.y, _14212.z);
    output_6 = vec4((DotaGlobalParams_t.g_vGlobalLightAmbientColor1.xyz * undetermined._m5.xyz) * undetermined._m4, 0.0);
    _23913.y = -_23913.y;
    gl_Position = _23913;
}


