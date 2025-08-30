// SPIR-V reflection failed for backend HLSL:
// cbuffer ID 5618 (name: undetermined), member index 10 (name: _m10) cannot be expressed with either HLSL packing layout or packoffset.
// 
// Re-attempting reflection with the GLSL backend.

// SPIR-V source (24200 bytes), GLSL reflection with SPIRV-Cross by KhronosGroup
// 

#version 460
#extension GL_EXT_samplerless_texture_functions : require

struct _55
{
    vec4 _m0[4];
};

const vec2 _2464[25] = vec2[](vec2(-0.9786980152130126953125, -0.088412098586559295654296875), vec2(-0.841121017932891845703125, 0.52116501331329345703125), vec2(-0.717459976673126220703125, -0.503220021724700927734375), vec2(-0.702933013439178466796875, 0.90313398838043212890625), vec2(-0.663197994232177734375, 0.1548199951648712158203125), vec2(-0.495101988315582275390625, -0.2328869998455047607421875), vec2(-0.3642379939556121826171875, -0.961790978908538818359375), vec2(-0.3458659946918487548828125, -0.5643789768218994140625), vec2(-0.3256630003452301025390625, 0.64037001132965087890625), vec2(-0.18271400034427642822265625, 0.32132899761199951171875), vec2(-0.142612993717193603515625, -0.0227362997829914093017578125), vec2(-0.0564287006855010986328125, -0.3672899901866912841796875), vec2(-0.01858579926192760467529296875, 0.91888201236724853515625), vec2(0.0381787009537220001220703125, -0.728995978832244873046875), vec2(0.16598999500274658203125, 0.093111999332904815673828125), vec2(0.2536390125751495361328125, 0.71953499317169189453125), vec2(0.3695490062236785888671875, -0.655018985271453857421875), vec2(0.4236269891262054443359375, 0.4299750030040740966796875), vec2(0.530746996402740478515625, -0.3649710118770599365234375), vec2(0.566026985645294189453125, -0.940488994121551513671875), vec2(0.639331996440887451171875, 0.02841269969940185546875), vec2(0.65208899974822998046875, 0.669668018817901611328125), vec2(0.773796975612640380859375, 0.345012009143829345703125), vec2(0.96887099742889404296875, 0.84044897556304931640625), vec2(0.991882026195526123046875, -0.65733802318572998046875));
vec3 _10030;
vec4 _17208;

struct _1568
{
    vec4 g_vInvProjRow3;
    vec3 g_vCameraPositionWs;
    float g_flViewportMinZ;
    vec3 g_vCameraDirWs;
    float g_flViewportMaxZ;
    float g_flTime;
    vec3 g_vFogColor;
    float g_flNegFogStartOverFogRange;
    float g_flInvFogRange;
    float g_flFogMaxDensity;
    float g_flFogExponent;
};

layout(set = 1) uniform _1568 PerViewConstantBuffer_t;

struct _526
{
    float _m0;
    float _m1;
    float _m2;
    float _m3;
    float _m4;
    float _m5;
    float _m6;
    float _m7;
    float _m8;
    float _m9;
    vec3 _m10;
    float _m11;
    float _m12;
    vec3 _m13;
    float _m14;
    int _m15;
    int _m16;
    float _m17;
    int _m18;
    float _m19;
    float _m20;
    float _m21;
    float _m22;
    float _m23;
    float _m24;
    float _m25;
    float _m26;
    float _m27;
    float _m28;
};

layout(set = 0) uniform _526 undetermined;

struct _1000
{
    float g_flLightShadows;
    float g_flPerLayerConstantDotaExtraData0;
};

layout(set = 1) uniform _1000 PerLayerConstantBufferDota_t;

struct _383
{
    vec3 g_flAmbientScale;
    vec3 _m1;
    vec3 _m2;
    float g_flMetalnessBlendToFull;
    vec3 g_flSelfIllumBlendToFull;
    vec3 g_flSpecularExponentBlendToFull;
    float _m6;
    vec2 _m7;
    vec2 g_vSpecularColor;
    float g_vDetail1ColorTint;
    _55 _m10;
    vec4 g_flPortalRadiusScale;
    vec3 _m12;
    vec3 _m13;
    float _m14;
    vec4 _m15;
};

layout(set = 1) uniform _383 _Globals_;

layout(set = 0, binding = 91) uniform texture2D undetermined_1;
layout(set = 0, binding = 44) uniform sampler DefaultSamplerState_0;
layout(set = 0, binding = 92) uniform texture2D undetermined_2;
layout(set = 0, binding = 45) uniform sampler AddressU_2_AddressV_2_AddressW_2_Filter_149_AddressU_3_AddressV_3_BorderColor_0_ComparisonFunc_3;
layout(set = 0, binding = 93) uniform texture2D undetermined_3;
layout(set = 0, binding = 94) uniform texture2D undetermined_4;
layout(set = 0, binding = 90) uniform texture2D undetermined_5;
layout(set = 0, binding = 95) uniform texture2D undetermined_6;
layout(set = 1, binding = 32) uniform texture2D g_tFresnelWarp;
layout(set = 1, binding = 17) uniform samplerShadow undetermined_7;
layout(set = 1, binding = 31) uniform texture2D g_tNormal;
layout(set = 1, binding = 15) uniform sampler undetermined_8;
layout(set = 1, binding = 38) uniform texture2D g_tGBufferDepth;

layout(location = 2) in vec3 input_0;
layout(location = 3) in vec4 input_1;
layout(location = 0) in vec4 input_2;
layout(location = 5) in vec3 input_3;
layout(location = 4) in vec4 input_4;
layout(location = 6) in vec4 input_5;
layout(location = 0) out vec4 output_0;

void main()
{
    vec3 _7269 = normalize(input_0.xyz);
    vec4 _20877 = texture(sampler2D(undetermined_1, DefaultSamplerState_0), input_2.xy);
    vec2 _7707 = (_20877.wy * 2.0) - vec2(1.0);
    float _9481 = _7707.x;
    vec3 _22101;
    _22101.x = _9481;
    float _21775 = _7707.y;
    _22101.y = _21775;
    vec3 _13110 = normalize((((normalize(input_1.xyz).xyz * _9481).xyz + ((normalize(cross(_7269.xyz, input_1.xyz)) * input_1.w).xyz * (-_21775))).xyz + (_7269.xyz * sqrt(clamp(1.0 - dot(_22101.xy, _22101.xy), 0.0, 1.0)))).xyz);
    float _8729 = _13110.z;
    vec3 _21537 = input_3.xyz - PerViewConstantBuffer_t.g_vCameraPositionWs.xyz;
    vec3 _13353 = -normalize(_21537);
    vec3 _18460 = _13110.xyz;
    vec4 _11179 = textureLod(sampler2D(undetermined_2, AddressU_2_AddressV_2_AddressW_2_Filter_149_AddressU_3_AddressV_3_BorderColor_0_ComparisonFunc_3), vec2(clamp(dot(_13353.xyz, _18460), 0.0, 1.0), 0.5), 0.0);
    float _9136 = _11179.z;
    vec4 _19372 = texture(sampler2D(undetermined_3, DefaultSamplerState_0), input_2.xy);
    float _7433 = max(_19372.z, undetermined._m4);
    float _19248 = max(_19372.w, undetermined._m5);
    vec4 _19373 = texture(sampler2D(undetermined_4, DefaultSamplerState_0), input_2.xy);
    vec4 _19680 = texture(sampler2D(undetermined_5, DefaultSamplerState_0), input_2.xy);
    vec3 _13062 = texture(sampler2D(undetermined_6, DefaultSamplerState_0), input_2.zw).xyz * undetermined._m13.xyz;
    vec4 _23714;
    _23714.x = _13062.x;
    _23714.y = _13062.y;
    _23714.z = _13062.z;
    vec3 _22860 = _23714.xyz * 2.0;
    vec4 _8673;
    _8673.x = _22860.x;
    _8673.y = _22860.y;
    _8673.z = _22860.z;
    vec3 _25191 = mix(vec3(1.0), _8673.xyz, vec3(max(_19372.x, undetermined._m3) * undetermined._m2));
    vec4 _17842;
    _17842.x = _25191.x;
    _17842.y = _25191.y;
    _17842.z = _25191.z;
    vec3 _23491 = _19680.xyz * _17842.xyz;
    bool _9413 = undetermined._m16 != 0;
    vec3 _19017;
    if (!_9413)
    {
        _19017 = _23491.xyz * input_4.xyz;
    }
    else
    {
        _19017 = _23491;
    }
    float _8332 = _19680.w;
    float _10367;
    if (undetermined._m18 != 0)
    {
        _10367 = _8332 * pow(abs(dot(PerViewConstantBuffer_t.g_vCameraDirWs.xyz, _18460)), undetermined._m17);
    }
    else
    {
        _10367 = _8332;
    }
    float _13553 = _10367 * fma(undetermined._m1, _9136 - 1.0, 1.0);
    float _21709;
    if (PerLayerConstantBufferDota_t.g_flLightShadows > 0.0)
    {
        float _12501;
        if (_Globals_._m14 > 0.0)
        {
            vec4 _15818 = mat4(vec4(_Globals_._m10._m0[0].x, _Globals_._m10._m0[1].x, _Globals_._m10._m0[2].x, _Globals_._m10._m0[3].x), vec4(_Globals_._m10._m0[0].y, _Globals_._m10._m0[1].y, _Globals_._m10._m0[2].y, _Globals_._m10._m0[3].y), vec4(_Globals_._m10._m0[0].z, _Globals_._m10._m0[1].z, _Globals_._m10._m0[2].z, _Globals_._m10._m0[3].z), vec4(_Globals_._m10._m0[0].w, _Globals_._m10._m0[1].w, _Globals_._m10._m0[2].w, _Globals_._m10._m0[3].w)) * vec4(input_3.xyz, 1.0);
            vec3 _10522 = _15818.xyz / vec3(_15818.w);
            vec4 _22906;
            _22906.x = _10522.x;
            _22906.y = _10522.y;
            float _19733 = saturate(-_10522.z) - 0.00069999997504055500030517578125;
            vec2 _12679 = (vec2(5.0) * _Globals_.g_flPortalRadiusScale.x).xy;
            int _20524;
            float _13014;
            float _13155 = 0.0;
            int _16208 = 0;
            for (;;)
            {
                vec2 _22613 = (_2464[_16208].xy * _12679).xy + _22906.xy;
                float _23752 = _22613.x;
                bool _12885;
                if (_23752 >= 0.0)
                {
                    _12885 = _22613.y >= 0.0;
                }
                else
                {
                    _12885 = false;
                }
                bool _12886;
                if (_12885)
                {
                    _12886 = _23752 <= 1.0;
                }
                else
                {
                    _12886 = false;
                }
                bool _12887;
                if (_12886)
                {
                    _12887 = _22613.y <= 1.0;
                }
                else
                {
                    _12887 = false;
                }
                if (_12887)
                {
                    _13014 = _13155 + textureLod(sampler2DShadow(g_tFresnelWarp, undetermined_7), vec3(_22613.xy, _19733), 0.0);
                }
                else
                {
                    _13014 = _13155;
                }
                _20524 = _16208 + 1;
                if (!(_20524 < 25))
                {
                    break;
                }
                _13155 = _13014;
                _16208 = _20524;
                continue;
            }
            _12501 = _13014 * 0.039999999105930328369140625;
        }
        else
        {
            vec4 _15817 = mat4(vec4(_Globals_._m10._m0[0].x, _Globals_._m10._m0[1].x, _Globals_._m10._m0[2].x, _Globals_._m10._m0[3].x), vec4(_Globals_._m10._m0[0].y, _Globals_._m10._m0[1].y, _Globals_._m10._m0[2].y, _Globals_._m10._m0[3].y), vec4(_Globals_._m10._m0[0].z, _Globals_._m10._m0[1].z, _Globals_._m10._m0[2].z, _Globals_._m10._m0[3].z), vec4(_Globals_._m10._m0[0].w, _Globals_._m10._m0[1].w, _Globals_._m10._m0[2].w, _Globals_._m10._m0[3].w)) * vec4(input_3.xyz, 1.0);
            vec3 _10521 = _15817.xyz / vec3(_15817.w);
            vec4 _22905;
            _22905.x = _10521.x;
            _22905.y = _10521.y;
            _12501 = textureLod(sampler2DShadow(g_tFresnelWarp, undetermined_7), vec3(_22905.xy, saturate(-_10521.z)), 0.0);
        }
        _21709 = _12501;
    }
    else
    {
        _21709 = 1.0;
    }
    vec3 _13833 = -_Globals_.g_flAmbientScale.xyz;
    vec3 _13845 = _13110.xyz;
    float _17643 = dot(_13845, _13833.xyz);
    vec2 _21656 = (input_3.xyz + (_Globals_.g_flAmbientScale.xyz * input_3.y)).xy * (1.0 / _Globals_.g_vDetail1ColorTint);
    vec3 _21103 = (((vec3(fma(_17643, 0.5, 0.5) * _21709).xyz * _Globals_._m1.xyz).xyz + (((_Globals_.g_flSelfIllumBlendToFull.xyz * clamp(dot(_Globals_._m2.xyz, _18460), 0.0, 1.0)) * _Globals_.g_flMetalnessBlendToFull) * undetermined._m0)).xyz + (((mix(_Globals_._m12.xyz, _Globals_.g_flSpecularExponentBlendToFull.xyz, vec3(fma(_8729, 0.5, 0.5))) * _Globals_._m6) * max(1.0 - _21709, 1.0 - clamp(min(texture(sampler2D(g_tNormal, undetermined_8), (_21656 + _Globals_._m7.xy).xy).x, texture(sampler2D(g_tNormal, undetermined_8), (_21656 + _Globals_.g_vSpecularColor.xy).xy).y), 0.0, 1.0))) * undetermined._m0)).xyz * _19017.xyz;
    vec4 _11665 = vec4(_21103, _13553 * input_4.w);
    vec3 _14166 = (((((vec3(saturate(_17643) * pow(max(0.001000000047497451305389404296875, clamp(dot(_13833.xyz, -reflect(_13353.xyz, _13845).xyz), 0.0, 1.0)), max(_19373.w, undetermined._m9) * undetermined._m11)).xyz * _Globals_._m1.xyz).xyz * undetermined._m12).xyz * max(_19373.x, undetermined._m6)).xyz * mix(_19680.xyz, undetermined._m10.xyz, vec3(max(_19373.z, undetermined._m8)))).xyz * max(_9136, _7433)).xyz;
    vec3 _15752 = _11665.xyz + _14166;
    _11665.x = _15752.x;
    _11665.y = _15752.y;
    _11665.z = _15752.z;
    vec3 _25192 = mix(_11665.xyz, _14166, vec3(_7433));
    vec4 _17843 = _11665;
    _17843.x = _25192.x;
    _17843.y = _25192.y;
    _17843.z = _25192.z;
    vec3 _15753 = _17843.xyz + (((input_5.xyz * max(_19373.y, undetermined._m7)).xyz * max(0.0, _8729)).xyz * _11179.x).xyz;
    vec4 _20489 = _17843;
    _20489.x = _15753.x;
    _20489.y = _15753.y;
    _20489.z = _15753.z;
    vec4 _19363;
    if (undetermined._m15 != 0)
    {
        vec3 _19801 = mix(_20489.xyz, _19017.xyz, vec3(saturate(_19248)));
        vec4 _17845 = _20489;
        _17845.x = _19801.x;
        _17845.y = _19801.y;
        _17845.z = _19801.z;
        _19363 = _17845;
    }
    else
    {
        vec3 _19800 = mix(_20489.xyz, _23491.xyz, vec3(saturate(_19248)));
        vec4 _17844 = _20489;
        _17844.x = _19800.x;
        _17844.y = _19800.y;
        _17844.z = _19800.z;
        _19363 = _17844;
    }
    vec3 _11003 = _19363.xyz * undetermined._m19;
    vec4 _8674 = _19363;
    _8674.x = _11003.x;
    _8674.y = _11003.y;
    _8674.z = _11003.z;
    vec4 _12888;
    if (undetermined._m14 != 0.0)
    {
        vec3 _19802 = mix(_8674.xyz, PerViewConstantBuffer_t.g_vFogColor.xyz, vec3(clamp(min(PerViewConstantBuffer_t.g_flFogMaxDensity, pow(clamp(fma(distance(input_3.xyz, PerViewConstantBuffer_t.g_vCameraPositionWs.xyz), PerViewConstantBuffer_t.g_flInvFogRange, PerViewConstantBuffer_t.g_flNegFogStartOverFogRange), 0.0, 1.0), PerViewConstantBuffer_t.g_flFogExponent)), 0.0, 1.0)));
        vec4 _17846 = _8674;
        _17846.x = _19802.x;
        _17846.y = _19802.y;
        _17846.z = _19802.z;
        _12888 = _17846;
    }
    else
    {
        _12888 = _8674;
    }
    vec4 _19364;
    if (_9413)
    {
        vec3 _21122 = _12888.xyz * input_4.xyz;
        vec4 _23715 = _12888;
        _23715.x = _21122.x;
        _23715.y = _21122.y;
        _23715.z = _21122.z;
        _19364 = _23715;
    }
    else
    {
        _19364 = _12888;
    }
    vec3 _11028 = _19364.xyz * mix(1.0, _13553, undetermined._m20);
    vec4 _8675 = _19364;
    _8675.x = _11028.x;
    _8675.y = _11028.y;
    _8675.z = _11028.z;
    vec4 _21710;
    if (PerLayerConstantBufferDota_t.g_flPerLayerConstantDotaExtraData0 > 0.0)
    {
        vec3 _14959 = mix(_8675.xyz, vec3(sqrt(dot(_8675.xyz, vec3(0.2125000059604644775390625, 0.7153999805450439453125, 0.07209999859333038330078125)))) * 0.199999988079071044921875, vec3(PerLayerConstantBufferDota_t.g_flPerLayerConstantDotaExtraData0));
        vec4 _17847 = _8675;
        _17847.x = _14959.x;
        _17847.y = _14959.y;
        _17847.z = _14959.z;
        _21710 = _17847;
    }
    else
    {
        _21710 = _8675;
    }
    float _10763 = min(150.0, _Globals_._m15.x);
    vec3 _14277 = _Globals_._m13 - input_3;
    vec3 _12686 = _21537.xyz;
    float _16773 = fma(_10763, undetermined._m25, -undetermined._m24);
    float _19250 = clamp((length(_14277 * vec3(1.0, 1.0, undetermined._m27)) - _16773) / (fma(_10763, undetermined._m25, undetermined._m24) - _16773), 0.0, 1.0);
    vec2 _21711;
    if (input_3.x < input_3.y)
    {
        _21711 = input_3.yz;
    }
    else
    {
        _21711 = input_3.xz;
    }
    vec2 _12553 = (vec2(-PerViewConstantBuffer_t.g_flTime) * undetermined._m28) + _21711;
    float _8310 = saturate(1.0 - _19250);
    float _13533 = _12553.x * undetermined._m21;
    float _24284 = _12553.y * undetermined._m21;
    vec2 _7203 = vec2(_13533, _24284);
    vec2 _17044 = floor(_7203);
    vec2 _15270 = fract(_7203);
    vec2 _8402 = (_15270 * _15270) * (vec2(3.0) - (_15270 * 2.0));
    float _10196 = _8402.x;
    vec2 _16856 = vec2(_13533 * 0.5, _24284 * 0.5);
    vec2 _17045 = floor(_16856);
    vec2 _15271 = fract(_16856);
    vec2 _8403 = (_15271 * _15271) * (vec2(3.0) - (_15271 * 2.0));
    float _10197 = _8403.x;
    vec2 _16857 = vec2(_13533 * 0.25, _24284 * 0.25);
    vec2 _17046 = floor(_16857);
    vec2 _15272 = fract(_16857);
    vec2 _8405 = (_15272 * _15272) * (vec2(3.0) - (_15272 * 2.0));
    float _10198 = _8405.x;
    float _23053 = fma(mix(mix(fract(sin(dot(_17046, vec2(12.98980045318603515625, 78.233001708984375))) * 43758.546875), fract(sin(dot(_17046 + vec2(1.0, 0.0), vec2(12.98980045318603515625, 78.233001708984375))) * 43758.546875), _10198), mix(fract(sin(dot(_17046 + vec2(0.0, 1.0), vec2(12.98980045318603515625, 78.233001708984375))) * 43758.546875), fract(sin(dot(_17046 + vec2(1.0), vec2(12.98980045318603515625, 78.233001708984375))) * 43758.546875), _10198), _8405.y), 0.5, fma(mix(mix(fract(sin(dot(_17044, vec2(12.98980045318603515625, 78.233001708984375))) * 43758.546875), fract(sin(dot(_17044 + vec2(1.0, 0.0), vec2(12.98980045318603515625, 78.233001708984375))) * 43758.546875), _10196), mix(fract(sin(dot(_17044 + vec2(0.0, 1.0), vec2(12.98980045318603515625, 78.233001708984375))) * 43758.546875), fract(sin(dot(_17044 + vec2(1.0), vec2(12.98980045318603515625, 78.233001708984375))) * 43758.546875), _10196), _8402.y), 0.125, mix(mix(fract(sin(dot(_17045, vec2(12.98980045318603515625, 78.233001708984375))) * 43758.546875), fract(sin(dot(_17045 + vec2(1.0, 0.0), vec2(12.98980045318603515625, 78.233001708984375))) * 43758.546875), _10197), mix(fract(sin(dot(_17045 + vec2(0.0, 1.0), vec2(12.98980045318603515625, 78.233001708984375))) * 43758.546875), fract(sin(dot(_17045 + vec2(1.0), vec2(12.98980045318603515625, 78.233001708984375))) * 43758.546875), _10197), _8403.y) * 0.25));
    if ((_23053 - _8310) < 0.0)
    {
        discard;
    }
    output_0 = _21710.xyzw * ((fma(undetermined._m23, (_19250 >= 0.99989998340606689453125) ? 0.0 : (1.0 - smoothstep(_8310, _8310 + undetermined._m22, _23053)), mix(0.0, 0.699999988079071044921875, pow(1.0 - clamp(dot(_14277, _14277) / (undetermined._m26 * undetermined._m26), 0.0, 1.0), 13.0) * 2.0)) * _19250) * clamp((distance(input_3, (PerViewConstantBuffer_t.g_vCameraPositionWs.xyz + (_12686 * (1.0 / (fma(clamp((texelFetch(g_tGBufferDepth, ivec3(ivec2(gl_FragCoord.xy), 0).xy, 0).x - PerViewConstantBuffer_t.g_flViewportMinZ) / (PerViewConstantBuffer_t.g_flViewportMaxZ - PerViewConstantBuffer_t.g_flViewportMinZ), 0.0, 1.0), PerViewConstantBuffer_t.g_vInvProjRow3.z, PerViewConstantBuffer_t.g_vInvProjRow3.w) * dot(PerViewConstantBuffer_t.g_vCameraDirWs.xyz, _12686))))).xyz) - 10.0) * 0.0500000007450580596923828125, 0.0, 1.0));
}


