// SPIR-V source (12464 bytes), HLSL reflection with SPIRV-Cross by KhronosGroup
// 

static const float _462[4] = { 0.0f, 0.0f, 0.0f, 0.0f };
static const uint2 _2013[4] = { uint2(0u, 0u), uint2(1u, 0u), uint2(0u, 1u), uint2(1u, 1u) };

cbuffer _Globals_ : register(b0, space0)
{
    float2 _Globals_1_g_vInvSrcSize : packoffset(c0);
    int _Globals_1_g_nSrcWidth : packoffset(c0.w);
    int _Globals_1_g_nSrcHeight : packoffset(c1);
};

Texture2DMS<float4> g_tSrcDepth_unexpectedTypeId438_1075 : register(t30, space0);
RWTexture2D<float4> _4304 : register(u159, space0);

static uint3 gl_LocalInvocationID;
static uint3 gl_GlobalInvocationID;
struct SPIRV_Cross_Input
{
    uint3 gl_LocalInvocationID : SV_GroupThreadID;
    uint3 gl_GlobalInvocationID : SV_DispatchThreadID;
};

groupshared float _5085[18][18];

void MainCs_inner()
{
    uint2 _3829 = gl_GlobalInvocationID.xy * uint2(2u, 2u);
    uint _13269 = gl_LocalInvocationID.x * 2u;
    uint _7357 = gl_LocalInvocationID.y * 2u;
    int2 _22105 = int2(_Globals_1_g_nSrcWidth - 1, _Globals_1_g_nSrcHeight - 1);
    float2 _20362 = float2(float(_Globals_1_g_nSrcWidth), float(_Globals_1_g_nSrcHeight));
    float _13155;
    _13155 = 0.0f;
    int _21227;
    float _24877;
    int _16208 = 0;
    for (;;)
    {
        if (!(_16208 < 2))
        {
            break;
        }
        _24877 = max(_13155, g_tSrcDepth_unexpectedTypeId438_1075.Load(int2(uint2((float2(min(uint2(_22105), _3829)) * _Globals_1_g_vInvSrcSize.xy) * _20362)), _16208).x);
        _21227 = _16208 + 1;
        _13155 = _24877;
        _16208 = _21227;
        continue;
    }
    _5085[_13269][_7357] = _13155;
    uint _22539 = _13269 + 1u;
    uint2 _16058 = _3829 + uint2(1u, 0u);
    float _13156;
    _13156 = 0.0f;
    int _21228;
    float _24878;
    int _16209 = 0;
    for (;;)
    {
        if (!(_16209 < 2))
        {
            break;
        }
        _24878 = max(_13156, g_tSrcDepth_unexpectedTypeId438_1075.Load(int2(uint2((float2(min(uint2(_22105), _16058)) * _Globals_1_g_vInvSrcSize.xy) * _20362)), _16209).x);
        _21228 = _16209 + 1;
        _13156 = _24878;
        _16209 = _21228;
        continue;
    }
    _5085[_22539][_7357] = _13156;
    uint _23736 = _7357 + 1u;
    uint2 _24897 = _3829 + uint2(0u, 1u);
    float _13157;
    _13157 = 0.0f;
    int _21229;
    float _24879;
    int _16210 = 0;
    for (;;)
    {
        if (!(_16210 < 2))
        {
            break;
        }
        _24879 = max(_13157, g_tSrcDepth_unexpectedTypeId438_1075.Load(int2(uint2((float2(min(uint2(_22105), _24897)) * _Globals_1_g_vInvSrcSize.xy) * _20362)), _16210).x);
        _21229 = _16210 + 1;
        _13157 = _24879;
        _16210 = _21229;
        continue;
    }
    _5085[_13269][_23736] = _13157;
    uint2 _16867 = _3829 + uint2(1u, 1u);
    float _13158;
    _13158 = 0.0f;
    int _21230;
    float _24880;
    int _16211 = 0;
    for (;;)
    {
        if (!(_16211 < 2))
        {
            break;
        }
        _24880 = max(_13158, g_tSrcDepth_unexpectedTypeId438_1075.Load(int2(uint2((float2(min(uint2(_22105), _16867)) * _Globals_1_g_vInvSrcSize.xy) * _20362)), _16211).x);
        _21230 = _16211 + 1;
        _13158 = _24880;
        _16211 = _21230;
        continue;
    }
    _5085[_22539][_23736] = _13158;
    bool _6354 = gl_LocalInvocationID.x == 7u;
    if (_6354)
    {
        uint _9975 = (gl_LocalInvocationID.x + 1u) * 2u;
        uint2 _16059 = _3829 + uint2(2u, 0u);
        float _13159;
        _13159 = 0.0f;
        int _21231;
        float _24881;
        int _16212 = 0;
        for (;;)
        {
            if (!(_16212 < 2))
            {
                break;
            }
            _24881 = max(_13159, g_tSrcDepth_unexpectedTypeId438_1075.Load(int2(uint2((float2(min(uint2(_22105), _16059)) * _Globals_1_g_vInvSrcSize.xy) * _20362)), _16212).x);
            _21231 = _16212 + 1;
            _13159 = _24881;
            _16212 = _21231;
            continue;
        }
        _5085[_9975][_7357] = _13159;
        uint _22540 = _9975 + 1u;
        uint2 _16060 = _3829 + uint2(3u, 0u);
        float _13160;
        _13160 = 0.0f;
        int _21232;
        float _24882;
        int _16213 = 0;
        for (;;)
        {
            if (!(_16213 < 2))
            {
                break;
            }
            _24882 = max(_13160, g_tSrcDepth_unexpectedTypeId438_1075.Load(int2(uint2((float2(min(uint2(_22105), _16060)) * _Globals_1_g_vInvSrcSize.xy) * _20362)), _16213).x);
            _21232 = _16213 + 1;
            _13160 = _24882;
            _16213 = _21232;
            continue;
        }
        _5085[_22540][_7357] = _13160;
        uint2 _16868 = _3829 + uint2(2u, 1u);
        float _13161;
        _13161 = 0.0f;
        int _21233;
        float _24883;
        int _16214 = 0;
        for (;;)
        {
            if (!(_16214 < 2))
            {
                break;
            }
            _24883 = max(_13161, g_tSrcDepth_unexpectedTypeId438_1075.Load(int2(uint2((float2(min(uint2(_22105), _16868)) * _Globals_1_g_vInvSrcSize.xy) * _20362)), _16214).x);
            _21233 = _16214 + 1;
            _13161 = _24883;
            _16214 = _21233;
            continue;
        }
        _5085[_9975][_23736] = _13161;
        uint2 _16869 = _3829 + uint2(3u, 1u);
        float _13162;
        _13162 = 0.0f;
        int _21234;
        float _24884;
        int _16215 = 0;
        for (;;)
        {
            if (!(_16215 < 2))
            {
                break;
            }
            _24884 = max(_13162, g_tSrcDepth_unexpectedTypeId438_1075.Load(int2(uint2((float2(min(uint2(_22105), _16869)) * _Globals_1_g_vInvSrcSize.xy) * _20362)), _16215).x);
            _21234 = _16215 + 1;
            _13162 = _24884;
            _16215 = _21234;
            continue;
        }
        _5085[_22540][_23736] = _13162;
    }
    bool _23545 = gl_LocalInvocationID.y == 7u;
    if (_23545)
    {
        uint _11172 = (gl_LocalInvocationID.y + 1u) * 2u;
        uint2 _9052 = _3829 + uint2(0u, 2u);
        float _13163;
        _13163 = 0.0f;
        int _21235;
        float _24885;
        int _16216 = 0;
        for (;;)
        {
            if (!(_16216 < 2))
            {
                break;
            }
            _24885 = max(_13163, g_tSrcDepth_unexpectedTypeId438_1075.Load(int2(uint2((float2(min(uint2(_22105), _9052)) * _Globals_1_g_vInvSrcSize.xy) * _20362)), _16216).x);
            _21235 = _16216 + 1;
            _13163 = _24885;
            _16216 = _21235;
            continue;
        }
        _5085[_13269][_11172] = _13163;
        uint2 _16870 = _3829 + uint2(1u, 2u);
        float _13164;
        _13164 = 0.0f;
        int _21236;
        float _24886;
        int _16217 = 0;
        for (;;)
        {
            if (!(_16217 < 2))
            {
                break;
            }
            _24886 = max(_13164, g_tSrcDepth_unexpectedTypeId438_1075.Load(int2(uint2((float2(min(uint2(_22105), _16870)) * _Globals_1_g_vInvSrcSize.xy) * _20362)), _16217).x);
            _21236 = _16217 + 1;
            _13164 = _24886;
            _16217 = _21236;
            continue;
        }
        _5085[_22539][_11172] = _13164;
        uint _23737 = _11172 + 1u;
        uint2 _24898 = _3829 + uint2(0u, 3u);
        float _13165;
        _13165 = 0.0f;
        int _21237;
        float _24887;
        int _16218 = 0;
        for (;;)
        {
            if (!(_16218 < 2))
            {
                break;
            }
            _24887 = max(_13165, g_tSrcDepth_unexpectedTypeId438_1075.Load(int2(uint2((float2(min(uint2(_22105), _24898)) * _Globals_1_g_vInvSrcSize.xy) * _20362)), _16218).x);
            _21237 = _16218 + 1;
            _13165 = _24887;
            _16218 = _21237;
            continue;
        }
        _5085[_13269][_23737] = _13165;
        uint2 _16871 = _3829 + uint2(1u, 3u);
        float _13166;
        _13166 = 0.0f;
        int _21238;
        float _24888;
        int _16219 = 0;
        for (;;)
        {
            if (!(_16219 < 2))
            {
                break;
            }
            _24888 = max(_13166, g_tSrcDepth_unexpectedTypeId438_1075.Load(int2(uint2((float2(min(uint2(_22105), _16871)) * _Globals_1_g_vInvSrcSize.xy) * _20362)), _16219).x);
            _21238 = _16219 + 1;
            _13166 = _24888;
            _16219 = _21238;
            continue;
        }
        _5085[_22539][_23737] = _13166;
    }
    if (_6354 ? _23545 : false)
    {
        uint _11173 = (gl_LocalInvocationID.x + 1u) * 2u;
        uint _21179 = (gl_LocalInvocationID.y + 1u) * 2u;
        uint2 _9053 = _3829 + uint2(2u, 2u);
        float _13167;
        _13167 = 0.0f;
        int _21239;
        float _24889;
        int _16220 = 0;
        for (;;)
        {
            if (!(_16220 < 2))
            {
                break;
            }
            _24889 = max(_13167, g_tSrcDepth_unexpectedTypeId438_1075.Load(int2(uint2((float2(min(uint2(_22105), _9053)) * _Globals_1_g_vInvSrcSize.xy) * _20362)), _16220).x);
            _21239 = _16220 + 1;
            _13167 = _24889;
            _16220 = _21239;
            continue;
        }
        _5085[_11173][_21179] = _13167;
        uint _22541 = _11173 + 1u;
        uint2 _16061 = _3829 + uint2(3u, 2u);
        float _13168;
        _13168 = 0.0f;
        int _21240;
        float _24890;
        int _16221 = 0;
        for (;;)
        {
            if (!(_16221 < 2))
            {
                break;
            }
            _24890 = max(_13168, g_tSrcDepth_unexpectedTypeId438_1075.Load(int2(uint2((float2(min(uint2(_22105), _16061)) * _Globals_1_g_vInvSrcSize.xy) * _20362)), _16221).x);
            _21240 = _16221 + 1;
            _13168 = _24890;
            _16221 = _21240;
            continue;
        }
        _5085[_22541][_21179] = _13168;
        uint _23738 = _21179 + 1u;
        uint2 _24899 = _3829 + uint2(2u, 3u);
        float _13169;
        _13169 = 0.0f;
        int _21241;
        float _24891;
        int _16222 = 0;
        for (;;)
        {
            if (!(_16222 < 2))
            {
                break;
            }
            _24891 = max(_13169, g_tSrcDepth_unexpectedTypeId438_1075.Load(int2(uint2((float2(min(uint2(_22105), _24899)) * _Globals_1_g_vInvSrcSize.xy) * _20362)), _16222).x);
            _21241 = _16222 + 1;
            _13169 = _24891;
            _16222 = _21241;
            continue;
        }
        _5085[_11173][_23738] = _13169;
        uint2 _16872 = _3829 + uint2(3u, 3u);
        float _13170;
        _13170 = 0.0f;
        int _21242;
        float _24892;
        int _16223 = 0;
        for (;;)
        {
            if (!(_16223 < 2))
            {
                break;
            }
            _24892 = max(_13170, g_tSrcDepth_unexpectedTypeId438_1075.Load(int2(uint2((float2(min(uint2(_22105), _16872)) * _Globals_1_g_vInvSrcSize.xy) * _20362)), _16223).x);
            _21242 = _16223 + 1;
            _13170 = _24892;
            _16223 = _21242;
            continue;
        }
        _5085[_22541][_23738] = _13170;
    }
    GroupMemoryBarrierWithGroupSync();
    float _5788[4] = _462;
    int _18377;
    int _13039 = 0;
    for (;;)
    {
        if (!(_13039 < 4))
        {
            break;
        }
        uint _7662 = (gl_LocalInvocationID.x + _2013[_13039].x) * 2u;
        uint _7282 = (gl_LocalInvocationID.y + _2013[_13039].y) * 2u;
        uint _13724 = _7662 + 1u;
        uint _13648 = _7282 + 1u;
        _5788[_13039] = max(max(_5085[_7662][_7282], _5085[_13724][_7282]), max(_5085[_7662][_13648], _5085[_13724][_13648]));
        _18377 = _13039 + 1;
        _13039 = _18377;
        continue;
    }
    _4304[gl_GlobalInvocationID.xy] = max(max(_5788[0], _5788[1]), max(_5788[2], _5788[3])).xxxx;
    GroupMemoryBarrierWithGroupSync();
    GroupMemoryBarrierWithGroupSync();
    GroupMemoryBarrierWithGroupSync();
}

[numthreads(8, 8, 1)]
void MainCs(SPIRV_Cross_Input stage_input)
{
    gl_LocalInvocationID = stage_input.gl_LocalInvocationID;
    gl_GlobalInvocationID = stage_input.gl_GlobalInvocationID;
    MainCs_inner();
}

