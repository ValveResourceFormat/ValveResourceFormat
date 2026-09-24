// SPIR-V reflection failed for backend HLSL:
// Only Logical addressing model can be used with HLSL.
//
// Re-attempting reflection with the GLSL backend.

// VRF-TEST
// SPIR-V source, GLSL reflection with SPIRV-Cross by KhronosGroup

#version 460
#extension GL_EXT_buffer_reference2 : require

layout(constant_id = 0) const uint g_nGPUVendorID = 0u;
const bool _2 = (g_nGPUVendorID == 4318u);
layout(constant_id = 1) const uint g_nGPUDeviceID = 0u;
const bool _3 = (g_nGPUDeviceID == 9988u);

struct _1022
{
    vec3 g_vModulationColor3;
    float g_flPinch;
};

layout(set = 1) uniform _1022 _Globals_;

struct _1048
{
    float _m0;
    vec4 _m1;
};

layout(set = 1) uniform _1048 ExternalTestCB1;

layout(push_constant, std430) uniform _997_4179
{
    vec2 _m0;
} _4179;

layout(set = 1, binding = 30) uniform texture2D g_tColor;
layout(set = 1, binding = 14) uniform sampler Filter_MinMagMipLinear_MaxAniso_1_MipBias_0;

layout(location = 1) in vec2 input_1;
layout(location = 0) out vec4 output_0;

void main()
{
    vec2 _10937 = (input_1.xy * 2.0) - vec2(1.0);
    vec2 _4360 = _10937;
    vec2 _15983 = _10937.xy;
    vec2 _14526 = normalize(_15983) * pow(length(_15983), _Globals_.g_flPinch);
    _4360.x = _14526.x;
    _4360.y = _14526.y;
    vec2 _14835 = _4360;
    vec2 _23713 = (_14835.xy * 0.5) + vec2(0.5);
    _4360.x = _23713.x;
    _4360.y = _23713.y;
    vec4 _3300 = texture(sampler2D(g_tColor, Filter_MinMagMipLinear_MaxAniso_1_MipBias_0), _4360.xy);
    vec4 _20506 = _3300;
    vec3 _14381 = _3300.xyz * ((ExternalTestCB1._m0 * _4179._m0.x) + _4179._m0.y);
    _20506.x = _14381.x;
    _20506.y = _14381.y;
    _20506.z = _14381.z;
    vec4 _13372 = _20506;
    vec3 _25167 = _13372.xyz * (ExternalTestCB1._m1.xyz * _Globals_.g_vModulationColor3.xyz);
    _20506.x = _25167.x;
    _20506.y = _25167.y;
    _20506.z = _25167.z;
    if (_2)
    {
        vec3 _5319 = vec3(1.0, 0.0, 0.0);
        if (_3)
        {
            _5319.z = 1.0;
        }
        vec4 _14033 = _20506;
        vec3 _12910 = _14033.xyz * _5319;
        _20506.x = _12910.x;
        _20506.y = _12910.y;
        _20506.z = _12910.z;
    }
    else
    {
        vec4 _15401 = _20506;
        vec3 _12682 = _15401.xyz * vec3(0.0, 1.0, 0.0);
        _20506.x = _12682.x;
        _20506.y = _12682.y;
        _20506.z = _12682.z;
    }
    output_0 = _20506;
}


