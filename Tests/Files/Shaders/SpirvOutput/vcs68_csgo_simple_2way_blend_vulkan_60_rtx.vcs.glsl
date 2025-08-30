// SPIR-V reflection failed for backend HLSL:
// Unsupported builtin in HLSL: 5326
// 
// Re-attempting reflection with the GLSL backend.

// SPIR-V source (53296 bytes), GLSL reflection with SPIRV-Cross by KhronosGroup
// 

#version 460
#extension GL_EXT_ray_tracing : require
#extension GL_EXT_nonuniform_qualifier : require
#extension GL_EXT_ray_query : require
#extension GL_EXT_spirv_intrinsics : require

struct GeometryInfo_t
{
    ivec4 m_nVertexAndIndexBuffers;
    ivec4 m_nBufferBindOffsetBytes;
    ivec4 m_nBufferStrideInBytes;
    ivec4 m_vNormalFieldInfo;
    ivec4 m_vTangentFieldInfo;
    ivec4 m_vTexCoordFieldInfo[8];
    ivec4 m_vColorFieldInfo[8];
};

struct PerInstancePackedShaderData_t
{
    uint m_Data[8];
};

struct MaterialDataMinimal_t
{
    vec3 vNormalWs;
    float flMetalness;
    vec3 vAlbedo;
    float flRoughness;
    vec3 vEmissive;
    float flReflectance;
    vec3 vSheenColor;
    float fSheenRoughness;
    vec3 vTransmissiveColor;
    float fClearCoat;
    float fClearCoatRoughness;
};

struct Payload_t
{
    vec3 vSmoothPositionWs;
    vec3 vFaceNormalWs;
    vec3 vInterpolatedNormalWs;
    float flHitT;
    float flOpacityRng;
    int nInstance;
    bool bFlippedNormal;
    MaterialDataMinimal_t material;
};

struct BuiltInTriangleIntersectionAttributes
{
    vec2 barycentrics;
};

struct PayloadShadow_t
{
    float m_flVisibility;
};

struct VS_INPUT
{
    vec3 vPositionOs;
    vec2 vTexCoord;
    vec3 vNormalOs;
    vec4 vTangentUOs_flTangentVSign;
    uint nTransformBufferOffset;
    vec4 vBlendColorTint;
    vec4 vBlendValues;
};

vec3 _447;
vec4 _448;
uint _469;
vec4 _470;
float _471;

layout(set = 1, binding = 31, std430) readonly buffer type_StructuredBuffer_GeometryInfo_t
{
    GeometryInfo_t _m0[];
} g_Geometries;

layout(set = 1, binding = 32, std430) readonly buffer type_ByteAddressBuffer
{
    uint _m0[];
} g_Vertices[];

layout(set = 1, binding = 33, std430) readonly buffer g_Indices
{
    uint _m0[];
} g_Indices_1[];

layout(set = 1, binding = 34, std430) readonly buffer g_MaterialData
{
    uint _m0[];
} g_MaterialData_1;

layout(set = 1, binding = 35, std430) readonly buffer type_StructuredBuffer_PerInstancePackedShaderData_t
{
    PerInstancePackedShaderData_t _m0[];
} g_instanceBuffer;

struct type_PerViewConstantBuffer_t
{
    mat4 g_matWorldToProjection;
    mat4 g_matProjectionToWorld;
    mat4 g_matWorldToView;
    mat4 g_matViewToProjection;
    vec4 g_vInvProjRow3;
    vec4 g_vClipPlane0;
    float g_flToneMapScalarLinear;
    float g_flInvToneMapScalarLinear;
    float g_fInvViewportZRange;
    float g_fMinViewportZScaled;
    vec3 g_vCameraPositionWs;
    float g_flViewportMinZ;
    vec3 g_vCameraDirWs;
    float g_flViewportMaxZ;
    vec3 g_vCameraUpDirWs;
    float g_flTime;
    vec3 g_vDepthPsToVsConversion;
    float g_flNearPlane;
    float g_flFarPlane;
    float g_flLightBinnerFarPlane;
    vec2 g_vInvViewportSize;
    vec2 g_vViewportToGBufferRatio;
    vec2 g_vMorphTextureAtlasSize;
    vec4 g_vInvGBufferSize;
    vec2 g_vViewportOffset;
    vec2 g_vViewportSize;
    vec2 g_vRenderTargetSize;
    float g_flFogBlendToBackground;
    float g_flHenyeyGreensteinCoeff;
    vec3 g_vFogColor;
    float g_flNegFogStartOverFogRange;
    float g_flInvFogRange;
    float g_flFogMaxDensity;
    float g_flFogExponent;
    float g_flMod2xIdentity;
    int g_nMSAASampleCount;
    float g_flInvMSAASampleCount;
    uint g_tCompositeMorphAtlasTextureIndex;
    uint _pad0;
    vec4 g_vFrameBufferCopyInvSizeAndUvScale;
    vec4 g_vCameraAngles;
    vec4 g_vWorldToCameraOffset;
    mat4 g_matPrevWorldToProjection;
    vec4 g_vPrevWorldToCameraOffset;
    vec4 g_vPerViewConstantExtraData0;
    vec4 g_vPerViewConstantExtraData1;
    vec4 g_vPerViewConstantExtraData2;
    vec4 g_vPerViewConstantExtraData3;
};

layout(set = 3) uniform type_PerViewConstantBuffer_t PerViewConstantBuffer_t;

struct type_PerViewConstantBufferCsgo_t
{
    uvec4 g_bFogTypeEnabled;
    uvec4 g_bOtherFxEnabled;
    uvec4 g_bOtherEnabled2;
    uvec4 g_bOtherEnabled3;
    uint g_tBlueNoiseTextureIndex;
    uint g_tBRDFLookupTextureIndex;
    uint g_tCubemapFogTextureIndex;
    uint g_tDynamicAmbientOcclusionTextureIndex;
    uint g_tDynamicAmbientOcclusionDepthIndex;
    uint g_tSSAOIndex;
    uint g_tParticleShadowBufferIndex;
    uint g_tZeroth_MomentIndex;
    uint g_tMomentsIndex;
    uint g_tExtra_MomentIndex;
    uint g_tLowShaderQualityFallbackCubemap;
    uint g_tUnused0;
    ivec2 g_vBlueNoiseMask;
    uint g_tUnused1;
    uint g_tUnused2;
    mat4 g_matPrimaryViewWorldToProjection;
    vec2 g_vAoProxyDepthInvTextureSize;
    float g_flAoProxyDownres;
    float g_flUnusedAfterAo;
    vec4 g_vWindDirection;
    vec4 g_vWindStrengthFreqMulHighStrength;
    vec4 g_vInteractionProjectionOrigin;
    vec4 g_vInteractionVolumeInvExtents;
    vec4 g_vInteractionTriggerVolumeInvMins;
    vec4 g_vInteractionTriggerVolumeWorldToVolumeScale;
    vec4 g_vGradientFogBiasAndScale;
    vec4 m_vGradientFogExponents;
    vec4 g_vGradientFogColor_Opacity;
    vec4 g_vGradientFogCullingParams;
    vec4 g_vCubeFog_Offset_Scale_Bias_Exponent;
    vec4 g_vCubeFog_Height_Offset_Scale_Exponent_Log2Mip;
    mat4 g_matvCubeFogSkyWsToOs;
    vec4 g_vCubeFogCullingParams_MaxOpacity;
    vec4 g_vCubeFog_ExposureBias;
    vec4 g_vHighPrecisionLightingOffsetWs;
    float g_flEnvMapPositionBias;
    float g_flEnvMapClampPlaneDistance;
    float g_flScopeMagnification;
    float g_flUnused;
    float g_flMixedResolutionViewportScale;
    float g_flMBOIT_Overestimation;
    float g_flMBOIT_Bias;
    float g_flMBOIT_Scale;
    float g_flToolsVisCubemapReflectionRoughness;
    float g_flCablePixelRadiusScale;
    float g_flRealTime;
    float g_flBeginMixingRoughness;
    vec4 g_vPlayerVisibilityParams;
};

layout(set = 0) uniform type_PerViewConstantBufferCsgo_t PerViewConstantBufferCsgo_t;

layout(set = 2, binding = 46) uniform texture2D g_bindless_Texture2D[];
layout(set = 2, binding = 29) uniform sampler g_bindless_Sampler[2048];
layout(location = 0) rayPayloadInEXT Payload_t payload;
hitAttributeEXT BuiltInTriangleIntersectionAttributes attrs;
layout(location = 1) rayPayloadInEXT PayloadShadow_t payload_1;

spirv_instruction(set = "GLSL.std.450", id = 79) float spvNMin(float, float);
spirv_instruction(set = "GLSL.std.450", id = 79) vec2 spvNMin(vec2, vec2);
spirv_instruction(set = "GLSL.std.450", id = 79) vec3 spvNMin(vec3, vec3);
spirv_instruction(set = "GLSL.std.450", id = 79) vec4 spvNMin(vec4, vec4);
spirv_instruction(set = "GLSL.std.450", id = 80) float spvNMax(float, float);
spirv_instruction(set = "GLSL.std.450", id = 80) vec2 spvNMax(vec2, vec2);
spirv_instruction(set = "GLSL.std.450", id = 80) vec3 spvNMax(vec3, vec3);
spirv_instruction(set = "GLSL.std.450", id = 80) vec4 spvNMax(vec4, vec4);

void main()
{
    uint _508 = g_instanceBuffer._m0[uint(gl_InstanceID)].m_Data[2] >> 2u;
    float _520 = uintBitsToFloat(g_MaterialData_1._m0[_508 + 31u]);
    float _533 = uintBitsToFloat(g_MaterialData_1._m0[_508 + 35u]);
    vec3 _556 = vec3(uintBitsToFloat(g_MaterialData_1._m0[_508 + 52u]), uintBitsToFloat(g_MaterialData_1._m0[_508 + 53u]), uintBitsToFloat(g_MaterialData_1._m0[_508 + 54u]));
    uvec3 _659;
    do
    {
        if (g_Geometries._m0[g_instanceBuffer._m0[uint(gl_InstanceID)].m_Data[1]].m_nBufferStrideInBytes.w == 4)
        {
            int _47 = g_Geometries._m0[g_instanceBuffer._m0[uint(gl_InstanceID)].m_Data[1]].m_nVertexAndIndexBuffers.w;
            uint _655 = ((12u * (uint(gl_PrimitiveID))) + uint(g_Geometries._m0[g_instanceBuffer._m0[uint(gl_InstanceID)].m_Data[1]].m_nBufferBindOffsetBytes.w)) >> 2u;
            _659 = uvec3(g_Indices_1[nonuniformEXT(_47)]._m0[_655], g_Indices_1[nonuniformEXT(_47)]._m0[_655 + 1u], g_Indices_1[nonuniformEXT(_47)]._m0[_655 + 2u]);
            break;
        }
        else
        {
            uint _633 = (6u * uint(gl_PrimitiveID)) + uint(g_Geometries._m0[g_instanceBuffer._m0[uint(gl_InstanceID)].m_Data[1]].m_nBufferBindOffsetBytes.w);
            uint _634 = _633 & 4294967292u;
            int _42 = g_Geometries._m0[g_instanceBuffer._m0[uint(gl_InstanceID)].m_Data[1]].m_nVertexAndIndexBuffers.w;
            uint _635 = _634 >> 2u;
            uint _636 = _635 + 1u;
            uvec4 _643 = uvec4(g_Indices_1[nonuniformEXT(_42)]._m0[_635] & 65535u, (g_Indices_1[nonuniformEXT(_42)]._m0[_635] & 4294901760u) >> 16u, g_Indices_1[nonuniformEXT(_42)]._m0[_636] & 65535u, (g_Indices_1[nonuniformEXT(_42)]._m0[_636] & 4294901760u) >> 16u);
            _659 = mix(_643.yzw, _643.xyz, bvec3(_633 == _634));
            break;
        }
        break; // unreachable workaround
    } while(false);
    uvec3 _674 = (uvec3(uint(g_Geometries._m0[g_instanceBuffer._m0[uint(gl_InstanceID)].m_Data[1]].m_nBufferStrideInBytes.x)) * _659) + uvec3(uint(g_Geometries._m0[g_instanceBuffer._m0[uint(gl_InstanceID)].m_Data[1]].m_nBufferBindOffsetBytes.x));
    uint _54 = uint(g_Geometries._m0[g_instanceBuffer._m0[uint(gl_InstanceID)].m_Data[1]].m_nVertexAndIndexBuffers.x);
    uint _676 = _674.x >> 2u;
    vec3 _492[3];
    _492[0] = vec3(uintBitsToFloat(g_Vertices[nonuniformEXT(_54)]._m0[_676]), uintBitsToFloat(g_Vertices[nonuniformEXT(_54)]._m0[_676 + 1u]), uintBitsToFloat(g_Vertices[nonuniformEXT(_54)]._m0[_676 + 2u]));
    uint _682 = _674.y >> 2u;
    _492[1] = vec3(uintBitsToFloat(g_Vertices[nonuniformEXT(_54)]._m0[_682]), uintBitsToFloat(g_Vertices[nonuniformEXT(_54)]._m0[_682 + 1u]), uintBitsToFloat(g_Vertices[nonuniformEXT(_54)]._m0[_682 + 2u]));
    uint _688 = _674.z >> 2u;
    _492[2] = vec3(uintBitsToFloat(g_Vertices[nonuniformEXT(_54)]._m0[_688]), uintBitsToFloat(g_Vertices[nonuniformEXT(_54)]._m0[_688 + 1u]), uintBitsToFloat(g_Vertices[nonuniformEXT(_54)]._m0[_688 + 2u]));
    VS_INPUT _498[3];
    for (int _694 = 0; _694 < 3; )
    {
        _498[_694].vPositionOs = _492[_694];
        _694++;
        continue;
    }
    vec2 _493[3];
    _493[0] = vec2(0.0);
    _493[1] = vec2(0.0);
    _493[2] = vec2(0.0);
    if (g_Geometries._m0[g_instanceBuffer._m0[uint(gl_InstanceID)].m_Data[1]].m_vTexCoordFieldInfo[0].y != 0)
    {
        ivec4 _473 = g_Geometries._m0[g_instanceBuffer._m0[uint(gl_InstanceID)].m_Data[1]].m_nVertexAndIndexBuffers;
        uint _713 = uint(g_Geometries._m0[g_instanceBuffer._m0[uint(gl_InstanceID)].m_Data[1]].m_vTexCoordFieldInfo[0].z);
        uint _716 = uint(_473[_713]);
        ivec4 _475 = g_Geometries._m0[g_instanceBuffer._m0[uint(gl_InstanceID)].m_Data[1]].m_nBufferBindOffsetBytes;
        ivec4 _474 = g_Geometries._m0[g_instanceBuffer._m0[uint(gl_InstanceID)].m_Data[1]].m_nBufferStrideInBytes;
        uvec3 _731 = (uvec3(uint(_474[_713])) * _659) + uvec3(uint(_475[_713]) + uint(g_Geometries._m0[g_instanceBuffer._m0[uint(gl_InstanceID)].m_Data[1]].m_vTexCoordFieldInfo[0].x));
        if (g_Geometries._m0[g_instanceBuffer._m0[uint(gl_InstanceID)].m_Data[1]].m_vTexCoordFieldInfo[0].y == 16)
        {
            uint _96 = _716;
            uint _849 = _731.x >> 2u;
            _493[0] = vec2(uintBitsToFloat(g_Vertices[nonuniformEXT(_96)]._m0[_849]), uintBitsToFloat(g_Vertices[nonuniformEXT(_96)]._m0[_849 + 1u]));
            uint _853 = _731.y >> 2u;
            _493[1] = vec2(uintBitsToFloat(g_Vertices[nonuniformEXT(_96)]._m0[_853]), uintBitsToFloat(g_Vertices[nonuniformEXT(_96)]._m0[_853 + 1u]));
            uint _857 = _731.z >> 2u;
            _493[2] = vec2(uintBitsToFloat(g_Vertices[nonuniformEXT(_96)]._m0[_857]), uintBitsToFloat(g_Vertices[nonuniformEXT(_96)]._m0[_857 + 1u]));
        }
        else
        {
            if (g_Geometries._m0[g_instanceBuffer._m0[uint(gl_InstanceID)].m_Data[1]].m_vTexCoordFieldInfo[0].y == 34)
            {
                uint _89 = _716;
                uint _813 = _731.x >> 2u;
                uvec2 _816 = uvec2(g_Vertices[nonuniformEXT(_89)]._m0[_813], g_Vertices[nonuniformEXT(_89)]._m0[_813] >> 16u) & uvec2(65535u);
                _493[0] = vec2(unpackHalf2x16(_816.x).x, unpackHalf2x16(_816.y).x);
                uint _825 = _731.y >> 2u;
                uvec2 _828 = uvec2(g_Vertices[nonuniformEXT(_89)]._m0[_825], g_Vertices[nonuniformEXT(_89)]._m0[_825] >> 16u) & uvec2(65535u);
                _493[1] = vec2(unpackHalf2x16(_828.x).x, unpackHalf2x16(_828.y).x);
                uint _837 = _731.z >> 2u;
                uvec2 _840 = uvec2(g_Vertices[nonuniformEXT(_89)]._m0[_837], g_Vertices[nonuniformEXT(_89)]._m0[_837] >> 16u) & uvec2(65535u);
                _493[2] = vec2(unpackHalf2x16(_840.x).x, unpackHalf2x16(_840.y).x);
            }
            else
            {
                if (g_Geometries._m0[g_instanceBuffer._m0[uint(gl_InstanceID)].m_Data[1]].m_vTexCoordFieldInfo[0].y == 37)
                {
                    uint _82 = _716;
                    uint _744 = _731.x >> 2u;
                    uint _746 = (g_Vertices[nonuniformEXT(_82)]._m0[_744] & 65535u) >> 0u;
                    uint _756 = (g_Vertices[nonuniformEXT(_82)]._m0[_744] & 4294901760u) >> 16u;
                    _493[0] = vec2(spvNMax(float((_746 > 32767u) ? int(4294901760u | _746) : int(_746)) * 3.0518509447574615478515625e-05, -1.0), spvNMax(float((_756 > 32767u) ? int(4294901760u | _756) : int(_756)) * 3.0518509447574615478515625e-05, -1.0));
                    uint _767 = _731.y >> 2u;
                    uint _769 = (g_Vertices[nonuniformEXT(_82)]._m0[_767] & 65535u) >> 0u;
                    uint _779 = (g_Vertices[nonuniformEXT(_82)]._m0[_767] & 4294901760u) >> 16u;
                    _493[1] = vec2(spvNMax(float((_769 > 32767u) ? int(4294901760u | _769) : int(_769)) * 3.0518509447574615478515625e-05, -1.0), spvNMax(float((_779 > 32767u) ? int(4294901760u | _779) : int(_779)) * 3.0518509447574615478515625e-05, -1.0));
                    uint _790 = _731.z >> 2u;
                    uint _792 = (g_Vertices[nonuniformEXT(_82)]._m0[_790] & 65535u) >> 0u;
                    uint _802 = (g_Vertices[nonuniformEXT(_82)]._m0[_790] & 4294901760u) >> 16u;
                    _493[2] = vec2(spvNMax(float((_792 > 32767u) ? int(4294901760u | _792) : int(_792)) * 3.0518509447574615478515625e-05, -1.0), spvNMax(float((_802 > 32767u) ? int(4294901760u | _802) : int(_802)) * 3.0518509447574615478515625e-05, -1.0));
                }
            }
        }
    }
    for (int _861 = 0; _861 < 3; )
    {
        _498[_861].vTexCoord = _493[_861];
        _861++;
        continue;
    }
    vec3 _494[3];
    vec4 _495[3];
    do
    {
        if (g_Geometries._m0[g_instanceBuffer._m0[uint(gl_InstanceID)].m_Data[1]].m_vNormalFieldInfo.y != 0)
        {
            ivec4 _477 = g_Geometries._m0[g_instanceBuffer._m0[uint(gl_InstanceID)].m_Data[1]].m_nVertexAndIndexBuffers;
            uint _883 = uint(g_Geometries._m0[g_instanceBuffer._m0[uint(gl_InstanceID)].m_Data[1]].m_vNormalFieldInfo.z);
            uint _886 = uint(_477[_883]);
            ivec4 _479 = g_Geometries._m0[g_instanceBuffer._m0[uint(gl_InstanceID)].m_Data[1]].m_nBufferBindOffsetBytes;
            ivec4 _478 = g_Geometries._m0[g_instanceBuffer._m0[uint(gl_InstanceID)].m_Data[1]].m_nBufferStrideInBytes;
            uvec3 _901 = (uvec3(uint(_478[_883])) * _659) + uvec3(uint(_479[_883]) + uint(g_Geometries._m0[g_instanceBuffer._m0[uint(gl_InstanceID)].m_Data[1]].m_vNormalFieldInfo.x));
            if (!(g_Geometries._m0[g_instanceBuffer._m0[uint(gl_InstanceID)].m_Data[1]].m_vNormalFieldInfo.y == 6))
            {
                uint _115 = _886;
                uint _906 = _901.x >> 2u;
                float _917 = fma(float((g_Vertices[nonuniformEXT(_115)]._m0[_906] >> 12u) & 1023u), 0.00195503421127796173095703125, -1.0);
                float _919 = fma(float((g_Vertices[nonuniformEXT(_115)]._m0[_906] >> 22u) & 1023u), 0.00195503421127796173095703125, -1.0);
                float _923 = (1.0 - abs(_917)) - abs(_919);
                vec3 _924 = vec3(_917, _919, _923);
                float _926 = clamp(-_923, 0.0, 1.0);
                vec2 _927 = _924.xy;
                vec2 _933 = _927 + mix(vec2(_926), vec2(-_926), greaterThanEqual(_927, vec2(0.0)));
                _494[0] = normalize(vec3(_933.x, _933.y, _924.z));
                float _939 = (_494[0].z >= 0.0) ? 1.0 : (-1.0);
                float _941 = (-1.0) / (_939 + _494[0].z);
                vec3 _952 = vec3(fma((_939 * _494[0].x) * _494[0].x, _941, 1.0), _939 * ((_494[0].x * _494[0].y) * _941), (-_939) * _494[0].x);
                float _954 = float((g_Vertices[nonuniformEXT(_115)]._m0[_906] >> 1u) & 2047u) * 0.003069460391998291015625;
                vec3 _961 = (_952 * cos(_954)) + (cross(_494[0], _952) * sin(_954));
                _495[0] = vec4(_961.x, _961.y, _961.z, _495[0].w);
                _495[0].w = ((g_Vertices[nonuniformEXT(_115)]._m0[_906] & 1u) == 0u) ? (-1.0) : 1.0;
                uint _968 = _901.y >> 2u;
                float _979 = fma(float((g_Vertices[nonuniformEXT(_115)]._m0[_968] >> 12u) & 1023u), 0.00195503421127796173095703125, -1.0);
                float _981 = fma(float((g_Vertices[nonuniformEXT(_115)]._m0[_968] >> 22u) & 1023u), 0.00195503421127796173095703125, -1.0);
                float _985 = (1.0 - abs(_979)) - abs(_981);
                vec3 _986 = vec3(_979, _981, _985);
                float _988 = clamp(-_985, 0.0, 1.0);
                vec2 _989 = _986.xy;
                vec2 _995 = _989 + mix(vec2(_988), vec2(-_988), greaterThanEqual(_989, vec2(0.0)));
                _494[1] = normalize(vec3(_995.x, _995.y, _986.z));
                float _1001 = (_494[1].z >= 0.0) ? 1.0 : (-1.0);
                float _1003 = (-1.0) / (_1001 + _494[1].z);
                vec3 _1014 = vec3(fma((_1001 * _494[1].x) * _494[1].x, _1003, 1.0), _1001 * ((_494[1].x * _494[1].y) * _1003), (-_1001) * _494[1].x);
                float _1016 = float((g_Vertices[nonuniformEXT(_115)]._m0[_968] >> 1u) & 2047u) * 0.003069460391998291015625;
                vec3 _1023 = (_1014 * cos(_1016)) + (cross(_494[1], _1014) * sin(_1016));
                _495[1] = vec4(_1023.x, _1023.y, _1023.z, _495[1].w);
                _495[1].w = ((g_Vertices[nonuniformEXT(_115)]._m0[_968] & 1u) == 0u) ? (-1.0) : 1.0;
                uint _1030 = _901.z >> 2u;
                float _1041 = fma(float((g_Vertices[nonuniformEXT(_115)]._m0[_1030] >> 12u) & 1023u), 0.00195503421127796173095703125, -1.0);
                float _1043 = fma(float((g_Vertices[nonuniformEXT(_115)]._m0[_1030] >> 22u) & 1023u), 0.00195503421127796173095703125, -1.0);
                float _1047 = (1.0 - abs(_1041)) - abs(_1043);
                vec3 _1048 = vec3(_1041, _1043, _1047);
                float _1050 = clamp(-_1047, 0.0, 1.0);
                vec2 _1051 = _1048.xy;
                vec2 _1057 = _1051 + mix(vec2(_1050), vec2(-_1050), greaterThanEqual(_1051, vec2(0.0)));
                _494[2] = normalize(vec3(_1057.x, _1057.y, _1048.z));
                float _1063 = (_494[2].z >= 0.0) ? 1.0 : (-1.0);
                float _1065 = (-1.0) / (_1063 + _494[2].z);
                vec3 _1076 = vec3(fma((_1063 * _494[2].x) * _494[2].x, _1065, 1.0), _1063 * ((_494[2].x * _494[2].y) * _1065), (-_1063) * _494[2].x);
                float _1078 = float((g_Vertices[nonuniformEXT(_115)]._m0[_1030] >> 1u) & 2047u) * 0.003069460391998291015625;
                vec3 _1085 = (_1076 * cos(_1078)) + (cross(_494[2], _1076) * sin(_1078));
                _495[2] = vec4(_1085.x, _1085.y, _1085.z, _495[2].w);
                _495[2].w = ((g_Vertices[nonuniformEXT(_115)]._m0[_1030] & 1u) == 0u) ? (-1.0) : 1.0;
                break;
            }
            uint _122 = _886;
            uint _1092 = _901.x >> 2u;
            _494[0] = vec3(uintBitsToFloat(g_Vertices[nonuniformEXT(_122)]._m0[_1092]), uintBitsToFloat(g_Vertices[nonuniformEXT(_122)]._m0[_1092 + 1u]), uintBitsToFloat(g_Vertices[nonuniformEXT(_122)]._m0[_1092 + 2u]));
            uint _1098 = _901.y >> 2u;
            _494[1] = vec3(uintBitsToFloat(g_Vertices[nonuniformEXT(_122)]._m0[_1098]), uintBitsToFloat(g_Vertices[nonuniformEXT(_122)]._m0[_1098 + 1u]), uintBitsToFloat(g_Vertices[nonuniformEXT(_122)]._m0[_1098 + 2u]));
            uint _1104 = _901.z >> 2u;
            _494[2] = vec3(uintBitsToFloat(g_Vertices[nonuniformEXT(_122)]._m0[_1104]), uintBitsToFloat(g_Vertices[nonuniformEXT(_122)]._m0[_1104 + 1u]), uintBitsToFloat(g_Vertices[nonuniformEXT(_122)]._m0[_1104 + 2u]));
        }
        else
        {
            _494[0] = vec3(0.0);
            _494[1] = vec3(0.0);
            _494[2] = vec3(0.0);
        }
        if (g_Geometries._m0[g_instanceBuffer._m0[uint(gl_InstanceID)].m_Data[1]].m_vTangentFieldInfo.y != 0)
        {
            ivec4 _476 = g_Geometries._m0[g_instanceBuffer._m0[uint(gl_InstanceID)].m_Data[1]].m_nVertexAndIndexBuffers;
            uint _1121 = uint(g_Geometries._m0[g_instanceBuffer._m0[uint(gl_InstanceID)].m_Data[1]].m_vTangentFieldInfo.z);
            ivec4 _481 = g_Geometries._m0[g_instanceBuffer._m0[uint(gl_InstanceID)].m_Data[1]].m_nBufferBindOffsetBytes;
            ivec4 _480 = g_Geometries._m0[g_instanceBuffer._m0[uint(gl_InstanceID)].m_Data[1]].m_nBufferStrideInBytes;
            uvec3 _1139 = (uvec3(uint(_480[_1121])) * _659) + uvec3(uint(_481[_1121]) + uint(g_Geometries._m0[g_instanceBuffer._m0[uint(gl_InstanceID)].m_Data[1]].m_vTangentFieldInfo.x));
            uint _150 = uint(_476[_1121]);
            uint _1141 = _1139.x >> 2u;
            _495[0] = vec4(uintBitsToFloat(g_Vertices[nonuniformEXT(_150)]._m0[_1141]), uintBitsToFloat(g_Vertices[nonuniformEXT(_150)]._m0[_1141 + 1u]), uintBitsToFloat(g_Vertices[nonuniformEXT(_150)]._m0[_1141 + 2u]), uintBitsToFloat(g_Vertices[nonuniformEXT(_150)]._m0[_1141 + 3u]));
            uint _1148 = _1139.y >> 2u;
            _495[1] = vec4(uintBitsToFloat(g_Vertices[nonuniformEXT(_150)]._m0[_1148]), uintBitsToFloat(g_Vertices[nonuniformEXT(_150)]._m0[_1148 + 1u]), uintBitsToFloat(g_Vertices[nonuniformEXT(_150)]._m0[_1148 + 2u]), uintBitsToFloat(g_Vertices[nonuniformEXT(_150)]._m0[_1148 + 3u]));
            uint _1155 = _1139.z >> 2u;
            _495[2] = vec4(uintBitsToFloat(g_Vertices[nonuniformEXT(_150)]._m0[_1155]), uintBitsToFloat(g_Vertices[nonuniformEXT(_150)]._m0[_1155 + 1u]), uintBitsToFloat(g_Vertices[nonuniformEXT(_150)]._m0[_1155 + 2u]), uintBitsToFloat(g_Vertices[nonuniformEXT(_150)]._m0[_1155 + 3u]));
        }
        else
        {
            _495[0] = vec4(0.0);
            _495[1] = vec4(0.0);
            _495[2] = vec4(0.0);
        }
        break;
    } while(false);
    for (int _1162 = 0; _1162 < 3; )
    {
        _498[_1162].vNormalOs = _494[_1162];
        _498[_1162].vTangentUOs_flTangentVSign = _495[_1162];
        _1162++;
        continue;
    }
    vec4 _496[3];
    if (g_Geometries._m0[g_instanceBuffer._m0[uint(gl_InstanceID)].m_Data[1]].m_vColorFieldInfo[0].y != 0)
    {
        ivec4 _482 = g_Geometries._m0[g_instanceBuffer._m0[uint(gl_InstanceID)].m_Data[1]].m_nVertexAndIndexBuffers;
        uint _1185 = uint(g_Geometries._m0[g_instanceBuffer._m0[uint(gl_InstanceID)].m_Data[1]].m_vColorFieldInfo[0].z);
        uint _1188 = uint(_482[_1185]);
        ivec4 _484 = g_Geometries._m0[g_instanceBuffer._m0[uint(gl_InstanceID)].m_Data[1]].m_nBufferBindOffsetBytes;
        ivec4 _483 = g_Geometries._m0[g_instanceBuffer._m0[uint(gl_InstanceID)].m_Data[1]].m_nBufferStrideInBytes;
        uvec3 _1203 = (uvec3(uint(_483[_1185])) * _659) + uvec3(uint(_484[_1185]) + uint(g_Geometries._m0[g_instanceBuffer._m0[uint(gl_InstanceID)].m_Data[1]].m_vColorFieldInfo[0].x));
        if (g_Geometries._m0[g_instanceBuffer._m0[uint(gl_InstanceID)].m_Data[1]].m_vColorFieldInfo[0].y == 2)
        {
            uint _194 = _1188;
            uint _1251 = _1203.x >> 2u;
            _496[0] = vec4(uintBitsToFloat(g_Vertices[nonuniformEXT(_194)]._m0[_1251]), uintBitsToFloat(g_Vertices[nonuniformEXT(_194)]._m0[_1251 + 1u]), uintBitsToFloat(g_Vertices[nonuniformEXT(_194)]._m0[_1251 + 2u]), uintBitsToFloat(g_Vertices[nonuniformEXT(_194)]._m0[_1251 + 3u]));
            uint _1258 = _1203.y >> 2u;
            _496[1] = vec4(uintBitsToFloat(g_Vertices[nonuniformEXT(_194)]._m0[_1258]), uintBitsToFloat(g_Vertices[nonuniformEXT(_194)]._m0[_1258 + 1u]), uintBitsToFloat(g_Vertices[nonuniformEXT(_194)]._m0[_1258 + 2u]), uintBitsToFloat(g_Vertices[nonuniformEXT(_194)]._m0[_1258 + 3u]));
            uint _1265 = _1203.z >> 2u;
            _496[2] = vec4(uintBitsToFloat(g_Vertices[nonuniformEXT(_194)]._m0[_1265]), uintBitsToFloat(g_Vertices[nonuniformEXT(_194)]._m0[_1265 + 1u]), uintBitsToFloat(g_Vertices[nonuniformEXT(_194)]._m0[_1265 + 2u]), uintBitsToFloat(g_Vertices[nonuniformEXT(_194)]._m0[_1265 + 3u]));
        }
        else
        {
            uint _187 = _1188;
            uint _1209 = _1203.x >> 2u;
            _496[0] = vec4(uvec4((g_Vertices[nonuniformEXT(_187)]._m0[_1209] & 255u) >> 0u, (g_Vertices[nonuniformEXT(_187)]._m0[_1209] & 65280u) >> 8u, (g_Vertices[nonuniformEXT(_187)]._m0[_1209] & 16711680u) >> 16u, (g_Vertices[nonuniformEXT(_187)]._m0[_1209] & 4278190080u) >> 24u)) * vec4(0.0039215688593685626983642578125);
            uint _1223 = _1203.y >> 2u;
            _496[1] = vec4(uvec4((g_Vertices[nonuniformEXT(_187)]._m0[_1223] & 255u) >> 0u, (g_Vertices[nonuniformEXT(_187)]._m0[_1223] & 65280u) >> 8u, (g_Vertices[nonuniformEXT(_187)]._m0[_1223] & 16711680u) >> 16u, (g_Vertices[nonuniformEXT(_187)]._m0[_1223] & 4278190080u) >> 24u)) * vec4(0.0039215688593685626983642578125);
            uint _1237 = _1203.z >> 2u;
            _496[2] = vec4(uvec4((g_Vertices[nonuniformEXT(_187)]._m0[_1237] & 255u) >> 0u, (g_Vertices[nonuniformEXT(_187)]._m0[_1237] & 65280u) >> 8u, (g_Vertices[nonuniformEXT(_187)]._m0[_1237] & 16711680u) >> 16u, (g_Vertices[nonuniformEXT(_187)]._m0[_1237] & 4278190080u) >> 24u)) * vec4(0.0039215688593685626983642578125);
        }
    }
    else
    {
        _496[0] = vec4(0.0);
        _496[1] = vec4(0.0);
        _496[2] = vec4(0.0);
    }
    for (int _1272 = 0; _1272 < 3; )
    {
        _498[_1272].vBlendColorTint = _496[_1272];
        _1272++;
        continue;
    }
    uvec3 _1328;
    do
    {
        if (g_Geometries._m0[g_instanceBuffer._m0[uint(gl_InstanceID)].m_Data[1]].m_nBufferStrideInBytes.w == 4)
        {
            int _236 = g_Geometries._m0[g_instanceBuffer._m0[uint(gl_InstanceID)].m_Data[1]].m_nVertexAndIndexBuffers.w;
            uint _1324 = ((12u * (uint(gl_PrimitiveID))) + uint(g_Geometries._m0[g_instanceBuffer._m0[uint(gl_InstanceID)].m_Data[1]].m_nBufferBindOffsetBytes.w)) >> 2u;
            _1328 = uvec3(g_Indices_1[nonuniformEXT(_236)]._m0[_1324], g_Indices_1[nonuniformEXT(_236)]._m0[_1324 + 1u], g_Indices_1[nonuniformEXT(_236)]._m0[_1324 + 2u]);
            break;
        }
        else
        {
            uint _1302 = (6u * uint(gl_PrimitiveID)) + uint(g_Geometries._m0[g_instanceBuffer._m0[uint(gl_InstanceID)].m_Data[1]].m_nBufferBindOffsetBytes.w);
            uint _1303 = _1302 & 4294967292u;
            int _231 = g_Geometries._m0[g_instanceBuffer._m0[uint(gl_InstanceID)].m_Data[1]].m_nVertexAndIndexBuffers.w;
            uint _1304 = _1303 >> 2u;
            uint _1305 = _1304 + 1u;
            uvec4 _1312 = uvec4(g_Indices_1[nonuniformEXT(_231)]._m0[_1304] & 65535u, (g_Indices_1[nonuniformEXT(_231)]._m0[_1304] & 4294901760u) >> 16u, g_Indices_1[nonuniformEXT(_231)]._m0[_1305] & 65535u, (g_Indices_1[nonuniformEXT(_231)]._m0[_1305] & 4294901760u) >> 16u);
            _1328 = mix(_1312.yzw, _1312.xyz, bvec3(_1302 == _1303));
            break;
        }
        break; // unreachable workaround
    } while(false);
    vec4 _497[3];
    _497[0] = vec4(0.0);
    _497[1] = vec4(0.0);
    _497[2] = vec4(0.0);
    if (g_Geometries._m0[g_instanceBuffer._m0[uint(gl_InstanceID)].m_Data[1]].m_vTexCoordFieldInfo[4].y != 0)
    {
        ivec4 _485 = g_Geometries._m0[g_instanceBuffer._m0[uint(gl_InstanceID)].m_Data[1]].m_nVertexAndIndexBuffers;
        uint _1340 = uint(g_Geometries._m0[g_instanceBuffer._m0[uint(gl_InstanceID)].m_Data[1]].m_vTexCoordFieldInfo[4].z);
        uint _1343 = uint(_485[_1340]);
        ivec4 _487 = g_Geometries._m0[g_instanceBuffer._m0[uint(gl_InstanceID)].m_Data[1]].m_nBufferBindOffsetBytes;
        ivec4 _486 = g_Geometries._m0[g_instanceBuffer._m0[uint(gl_InstanceID)].m_Data[1]].m_nBufferStrideInBytes;
        uvec3 _1358 = (uvec3(uint(_486[_1340])) * _1328) + uvec3(uint(_487[_1340]) + uint(g_Geometries._m0[g_instanceBuffer._m0[uint(gl_InstanceID)].m_Data[1]].m_vTexCoordFieldInfo[4].x));
        if (g_Geometries._m0[g_instanceBuffer._m0[uint(gl_InstanceID)].m_Data[1]].m_vTexCoordFieldInfo[4].y == 2)
        {
            uint _250 = _1343;
            uint _1406 = _1358.x >> 2u;
            _497[0] = vec4(uintBitsToFloat(g_Vertices[nonuniformEXT(_250)]._m0[_1406]), uintBitsToFloat(g_Vertices[nonuniformEXT(_250)]._m0[_1406 + 1u]), uintBitsToFloat(g_Vertices[nonuniformEXT(_250)]._m0[_1406 + 2u]), uintBitsToFloat(g_Vertices[nonuniformEXT(_250)]._m0[_1406 + 3u]));
            uint _1412 = _1358.y >> 2u;
            _497[1] = vec4(uintBitsToFloat(g_Vertices[nonuniformEXT(_250)]._m0[_1412]), uintBitsToFloat(g_Vertices[nonuniformEXT(_250)]._m0[_1412 + 1u]), uintBitsToFloat(g_Vertices[nonuniformEXT(_250)]._m0[_1412 + 2u]), uintBitsToFloat(g_Vertices[nonuniformEXT(_250)]._m0[_1412 + 3u]));
            uint _1418 = _1358.z >> 2u;
            _497[2] = vec4(uintBitsToFloat(g_Vertices[nonuniformEXT(_250)]._m0[_1418]), uintBitsToFloat(g_Vertices[nonuniformEXT(_250)]._m0[_1418 + 1u]), uintBitsToFloat(g_Vertices[nonuniformEXT(_250)]._m0[_1418 + 2u]), uintBitsToFloat(g_Vertices[nonuniformEXT(_250)]._m0[_1418 + 3u]));
        }
        else
        {
            if (g_Geometries._m0[g_instanceBuffer._m0[uint(gl_InstanceID)].m_Data[1]].m_vTexCoordFieldInfo[4].y == 28)
            {
                uint _243 = _1343;
                uint _1367 = _1358.x >> 2u;
                _497[0] = vec4(uvec4((g_Vertices[nonuniformEXT(_243)]._m0[_1367] & 255u) >> 0u, (g_Vertices[nonuniformEXT(_243)]._m0[_1367] & 65280u) >> 8u, (g_Vertices[nonuniformEXT(_243)]._m0[_1367] & 16711680u) >> 16u, (g_Vertices[nonuniformEXT(_243)]._m0[_1367] & 4278190080u) >> 24u)) * vec4(0.0039215688593685626983642578125);
                uint _1380 = _1358.y >> 2u;
                _497[1] = vec4(uvec4((g_Vertices[nonuniformEXT(_243)]._m0[_1380] & 255u) >> 0u, (g_Vertices[nonuniformEXT(_243)]._m0[_1380] & 65280u) >> 8u, (g_Vertices[nonuniformEXT(_243)]._m0[_1380] & 16711680u) >> 16u, (g_Vertices[nonuniformEXT(_243)]._m0[_1380] & 4278190080u) >> 24u)) * vec4(0.0039215688593685626983642578125);
                uint _1393 = _1358.z >> 2u;
                _497[2] = vec4(uvec4((g_Vertices[nonuniformEXT(_243)]._m0[_1393] & 255u) >> 0u, (g_Vertices[nonuniformEXT(_243)]._m0[_1393] & 65280u) >> 8u, (g_Vertices[nonuniformEXT(_243)]._m0[_1393] & 16711680u) >> 16u, (g_Vertices[nonuniformEXT(_243)]._m0[_1393] & 4278190080u) >> 24u)) * vec4(0.0039215688593685626983642578125);
            }
        }
    }
    for (int _1424 = 0; _1424 < 3; )
    {
        _498[_1424].vBlendValues = _497[_1424];
        _1424++;
        continue;
    }
    mat3x4 _1441 = transpose(gl_ObjectToWorldEXT);
    vec4 _1453 = vec4(uvec4((g_instanceBuffer._m0[uint(gl_InstanceID)].m_Data[0] & 255u) >> 0u, (g_instanceBuffer._m0[uint(gl_InstanceID)].m_Data[0] & 65280u) >> 8u, (g_instanceBuffer._m0[uint(gl_InstanceID)].m_Data[0] & 16711680u) >> 16u, _469)) * vec4(0.0039215688593685626983642578125);
    vec3 _1454 = _1453.xyz;
    vec3 _1455 = _1454 * vec3(0.077399380505084991455078125);
    vec3 _1457 = pow(fma(_1454, vec3(0.947867333889007568359375), vec3(0.0521326996386051177978515625)), vec3(2.400000095367431640625));
    mat3x4 _1475 = transpose(gl_WorldToObjectEXT);
    mat3 _1482 = mat3(_1475[0].xyz, _1475[1].xyz, _1475[2].xyz);
    vec3 _1484 = normalize(_1482 * _498[0].vNormalOs);
    vec3 _1489 = vec4(_498[0].vPositionOs, 1.0) * _1441;
    vec3 _1492 = normalize(_1482 * _498[0].vTangentUOs_flTangentVSign.xyz);
    vec4 _1493 = vec4(_1492.x, _1492.y, _1492.z, _470.w);
    _1493.w = _498[0].vTangentUOs_flTangentVSign.w;
    vec2 _1497 = vec4(uintBitsToFloat(g_MaterialData_1._m0[_508 + 28u]), uintBitsToFloat(g_MaterialData_1._m0[_508 + 29u]), _471, _520).xy;
    vec2 _1500 = vec4(uintBitsToFloat(g_MaterialData_1._m0[_508 + 32u]), uintBitsToFloat(g_MaterialData_1._m0[_508 + 33u]), _471, _533).xy;
    vec2 _1506 = vec2(uintBitsToFloat(g_MaterialData_1._m0[_508 + 36u]), uintBitsToFloat(g_MaterialData_1._m0[_508 + 37u])) * PerViewConstantBuffer_t.g_flTime;
    vec2 _1508 = vec2(dot(_498[0].vTexCoord.xy, _1497) + _520, dot(_498[0].vTexCoord.xy, _1500) + _533).xy + _1506;
    vec3 _1511 = vec3(uintBitsToFloat(g_MaterialData_1._m0[_508 + 55u]));
    vec3 _1514 = mix(vec3(1.0), vec3((_1453.x <= 0.040449999272823333740234375) ? _1455.x : _1457.x, (_1453.y <= 0.040449999272823333740234375) ? _1455.y : _1457.y, (_1453.z <= 0.040449999272823333740234375) ? _1455.z : _1457.z).xyz, _1511).xyz * _556;
    vec4 _1515 = vec4(_1514.x, _1514.y, _1514.z, _1453.w);
    vec4 _1531;
    if (!((((_498[0].vBlendColorTint.x == 0.0) && (_498[0].vBlendColorTint.y == 0.0)) && (_498[0].vBlendColorTint.z == 0.0)) && (_498[0].vBlendColorTint.w == 0.0)))
    {
        _1531 = _1515 * _498[0].vBlendColorTint;
    }
    else
    {
        _1531 = _1515;
    }
    vec4 _1532 = vec4(_498[0].vBlendValues.x, _498[0].vBlendValues.y, _498[0].vBlendValues.z, _448.w);
    _1532.w = spvNMax(_498[0].vBlendValues.w, 0.100000001490116119384765625);
    vec4 _1555 = vec4(uvec4((g_instanceBuffer._m0[uint(gl_InstanceID)].m_Data[0] & 255u) >> 0u, (g_instanceBuffer._m0[uint(gl_InstanceID)].m_Data[0] & 65280u) >> 8u, (g_instanceBuffer._m0[uint(gl_InstanceID)].m_Data[0] & 16711680u) >> 16u, _469)) * vec4(0.0039215688593685626983642578125);
    vec3 _1556 = _1555.xyz;
    vec3 _1557 = _1556 * vec3(0.077399380505084991455078125);
    vec3 _1559 = pow(fma(_1556, vec3(0.947867333889007568359375), vec3(0.0521326996386051177978515625)), vec3(2.400000095367431640625));
    vec3 _1577 = normalize(_1482 * _498[1].vNormalOs);
    vec3 _1582 = vec4(_498[1].vPositionOs, 1.0) * _1441;
    vec3 _1585 = normalize(_1482 * _498[1].vTangentUOs_flTangentVSign.xyz);
    vec4 _1586 = vec4(_1585.x, _1585.y, _1585.z, _470.w);
    _1586.w = _498[1].vTangentUOs_flTangentVSign.w;
    vec2 _1596 = vec2(dot(_498[1].vTexCoord.xy, _1497) + _520, dot(_498[1].vTexCoord.xy, _1500) + _533).xy + _1506;
    vec3 _1601 = mix(vec3(1.0), vec3((_1555.x <= 0.040449999272823333740234375) ? _1557.x : _1559.x, (_1555.y <= 0.040449999272823333740234375) ? _1557.y : _1559.y, (_1555.z <= 0.040449999272823333740234375) ? _1557.z : _1559.z).xyz, _1511).xyz * _556;
    vec4 _1602 = vec4(_1601.x, _1601.y, _1601.z, _1555.w);
    vec4 _1618;
    if (!((((_498[1].vBlendColorTint.x == 0.0) && (_498[1].vBlendColorTint.y == 0.0)) && (_498[1].vBlendColorTint.z == 0.0)) && (_498[1].vBlendColorTint.w == 0.0)))
    {
        _1618 = _1602 * _498[1].vBlendColorTint;
    }
    else
    {
        _1618 = _1602;
    }
    vec4 _1619 = vec4(_498[1].vBlendValues.x, _498[1].vBlendValues.y, _498[1].vBlendValues.z, _448.w);
    _1619.w = spvNMax(_498[1].vBlendValues.w, 0.100000001490116119384765625);
    vec4 _1642 = vec4(uvec4((g_instanceBuffer._m0[uint(gl_InstanceID)].m_Data[0] & 255u) >> 0u, (g_instanceBuffer._m0[uint(gl_InstanceID)].m_Data[0] & 65280u) >> 8u, (g_instanceBuffer._m0[uint(gl_InstanceID)].m_Data[0] & 16711680u) >> 16u, _469)) * vec4(0.0039215688593685626983642578125);
    vec3 _1643 = _1642.xyz;
    vec3 _1644 = _1643 * vec3(0.077399380505084991455078125);
    vec3 _1646 = pow(fma(_1643, vec3(0.947867333889007568359375), vec3(0.0521326996386051177978515625)), vec3(2.400000095367431640625));
    vec3 _1664 = normalize(_1482 * _498[2].vNormalOs);
    vec3 _1669 = vec4(_498[2].vPositionOs, 1.0) * _1441;
    vec3 _1672 = normalize(_1482 * _498[2].vTangentUOs_flTangentVSign.xyz);
    vec4 _1673 = vec4(_1672.x, _1672.y, _1672.z, _470.w);
    _1673.w = _498[2].vTangentUOs_flTangentVSign.w;
    vec2 _1683 = vec2(dot(_498[2].vTexCoord.xy, _1497) + _520, dot(_498[2].vTexCoord.xy, _1500) + _533).xy + _1506;
    vec3 _1688 = mix(vec3(1.0), vec3((_1642.x <= 0.040449999272823333740234375) ? _1644.x : _1646.x, (_1642.y <= 0.040449999272823333740234375) ? _1644.y : _1646.y, (_1642.z <= 0.040449999272823333740234375) ? _1644.z : _1646.z).xyz, _1511).xyz * _556;
    vec4 _1689 = vec4(_1688.x, _1688.y, _1688.z, _1642.w);
    vec4 _1705;
    if (!((((_498[2].vBlendColorTint.x == 0.0) && (_498[2].vBlendColorTint.y == 0.0)) && (_498[2].vBlendColorTint.z == 0.0)) && (_498[2].vBlendColorTint.w == 0.0)))
    {
        _1705 = _1689 * _498[2].vBlendColorTint;
    }
    else
    {
        _1705 = _1689;
    }
    vec4 _1706 = vec4(_498[2].vBlendValues.x, _498[2].vBlendValues.y, _498[2].vBlendValues.z, _448.w);
    _1706.w = spvNMax(_498[2].vBlendValues.w, 0.100000001490116119384765625);
    float _1713 = (1.0 - attrs.barycentrics.x) - attrs.barycentrics.y;
    vec3 _1718 = ((_1484 * _1713) + (_1577 * attrs.barycentrics.x)) + (_1664 * attrs.barycentrics.y);
    vec3 _1722 = normalize(cross(_1582 - _1489, _1669 - _1489));
    vec4 _1737 = ((_1493 * _1713) + (_1586 * attrs.barycentrics.x)) + (_1673 * attrs.barycentrics.y);
    vec4 _1742 = ((_1532 * _1713) + (_1619 * attrs.barycentrics.x)) + (_1706 * attrs.barycentrics.y);
    vec2 _1748 = (((vec4(_1508.x, _1508.y, _498[0].vTexCoord.x, _498[0].vTexCoord.y) * _1713) + (vec4(_1596.x, _1596.y, _498[1].vTexCoord.x, _498[1].vTexCoord.y) * attrs.barycentrics.x)) + (vec4(_1683.x, _1683.y, _498[2].vTexCoord.x, _498[2].vTexCoord.y) * attrs.barycentrics.y)).xy;
    vec2 _1749 = _1748 * vec2(uintBitsToFloat(g_MaterialData_1._m0[_508 + 74u]), uintBitsToFloat(g_MaterialData_1._m0[_508 + 75u]));
    uint _287 = g_MaterialData_1._m0[_508 + 76u];
    uint _290 = g_MaterialData_1._m0[_508 + 64u];
    uint _294 = g_MaterialData_1._m0[_508 + 77u];
    uint _298 = g_MaterialData_1._m0[_508 + 78u];
    uint _302 = g_MaterialData_1._m0[_508 + 79u];
    uint _306 = g_MaterialData_1._m0[_508 + 80u];
    uint _309 = g_MaterialData_1._m0[_508 + 59u];
    vec4 _1754 = textureLod(nonuniformEXT(sampler2D(g_bindless_Texture2D[_306], g_bindless_Sampler[_309])), _1749, 0.0);
    float _1755 = _1754.x;
    float _1756 = _1742.w;
    float _1762 = smoothstep(spvNMax(0.0, _1755 - _1756), spvNMin(1.0, _1755 + _1756), _1742.x);
    uint _313 = g_MaterialData_1._m0[_508 + 81u];
    vec4 _1763 = textureLod(nonuniformEXT(sampler2D(g_bindless_Texture2D[_313], g_bindless_Sampler[_309])), _1748, 0.0);
    vec3 _1764 = textureLod(nonuniformEXT(sampler2D(g_bindless_Texture2D[_287], g_bindless_Sampler[_290])), _1748, 0.0).xyz;
    vec3 _1765 = (((_1531 * _1713) + (_1618 * attrs.barycentrics.x)) + (_1705 * attrs.barycentrics.y)).xyz;
    vec3 _1770 = textureLod(nonuniformEXT(sampler2D(g_bindless_Texture2D[_294], g_bindless_Sampler[_290])), _1749, 0.0).xyz;
    vec4 _1780 = mix(textureLod(nonuniformEXT(sampler2D(g_bindless_Texture2D[_298], g_bindless_Sampler[_290])), _1748, 0.0), textureLod(nonuniformEXT(sampler2D(g_bindless_Texture2D[_302], g_bindless_Sampler[_290])), _1749, 0.0), vec4(_1762));
    float _1783 = _1780.x;
    float _1784 = _1780.y;
    float _1786 = (_1783 + _1784) - 1.00392162799835205078125;
    float _1787 = _1783 - _1784;
    vec3 _1793 = normalize(vec3(_1786, _1787, (1.0 - abs(_1786)) - abs(_1787)));
    vec3 _1794 = _1718 * 1.0;
    vec3 _1795 = _1737.xyz;
    vec3 _1800 = cross(_1794, _1795) * ((_1737.w > 0.0) ? 1.0 : (-1.0));
    vec3 _1807;
    if (PerViewConstantBufferCsgo_t.g_bOtherEnabled3.w != 0u)
    {
        _1807 = -_1800;
    }
    else
    {
        _1807 = _1800;
    }
    vec3 _490[3] = vec3[](_1489, _1582, _1669);
    vec3 _491[3] = vec3[](_1484, _1577, _1664);
    vec3 _1826 = ((_1489 * _1713) + (_1582 * attrs.barycentrics.x)) + (_1669 * attrs.barycentrics.y);
    vec3 _488[3];
    _488[0] = _1826 - (_491[0] * dot(_1826 - _490[0], _491[0]));
    _488[1] = _1826 - (_491[1] * dot(_1826 - _490[1], _491[1]));
    _488[2] = _1826 - (_491[2] * dot(_1826 - _490[2], _491[2]));
    vec3 _1862 = ((_488[0] * _1713) + (_488[1] * attrs.barycentrics.x)) + (_488[2] * attrs.barycentrics.y);
    vec3 _489[3];
    _489[0] = cross(_490[0] - _490[1], _1722);
    _489[1] = cross(_490[1] - _490[2], _1722);
    _489[2] = cross(_490[2] - _490[0], _1722);
    float _1881;
    int _1884;
    vec3 _1886;
    _1881 = dot(_1862 - _1826, _1722);
    _1884 = 0;
    _1886 = _1862;
    float _1882;
    vec3 _1887;
    for (; _1884 < 3; _1881 = _1882, _1884++, _1886 = _1887)
    {
        if (_1881 > 0.00999999977648258209228515625)
        {
            break;
        }
        if (dot(_489[_1884], _489[_1884]) == 0.0)
        {
            _1882 = _1881;
            _1887 = _1886;
            continue;
        }
        _489[_1884] = normalize(_489[_1884]);
        for (int _1904 = 0; _1904 < 3; )
        {
            vec3 _1916 = _491[_1904] - (_489[_1884] * dot(_489[_1884], _491[_1904]));
            _488[_1904] = _1826 - (_1916 * dot(_1826 - _490[_1904], _1916));
            _1904++;
            continue;
        }
        vec3 _1932 = ((_488[0] * _1713) + (_488[1] * attrs.barycentrics.x)) + (_488[2] * attrs.barycentrics.y);
        _1882 = dot(_1932 - _1826, _1722);
        _1887 = _1932;
    }
    payload = Payload_t(mix(vec3(3.4028234663852885981170418348452e+38), _1886, bvec3(_1881 > 0.00999999977648258209228515625)), _1722, normalize(mix(_1718, _447, bvec3(dot(_1718, _1718) >= 1.0099999904632568359375))), gl_RayTmaxEXT, payload.flOpacityRng, int(uint(gl_InstanceID)), false, MaterialDataMinimal_t(normalize(((_1795 * _1793.x) + (_1807 * (-_1793.y))) + (_1794 * _1793.z)), mix(uintBitsToFloat(g_MaterialData_1._m0[_508 + 71u]), uintBitsToFloat(g_MaterialData_1._m0[_508 + 72u]), _1762), mix(mix(_1764, _1764 * _1765, vec3(_1763.x)).xyz, mix(_1770, _1770 * _1765, vec3(_1763.y)).xyz, vec3(_1762)), dot(_1780.zz, vec2(0.5)), vec3(0.0), uintBitsToFloat(g_MaterialData_1._m0[_508 + 85u]), vec3(0.0), 0.5, vec3(0.0), 0.0, 0.0));
}


