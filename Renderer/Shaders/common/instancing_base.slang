#version 460

#ifndef VRF_INSTANCING_BASE_INCLUDE
#define VRF_INSTANCING_BASE_INCLUDE

struct ObjectDataStandard
{
    uint TintAlpha;
    uint TransformIndex;
    uint VisibleLPV;
    uint Identification;
    uvec4 EnvMapVisibility;
    uint MeshBoneData; // skinned flag in bit 31, bone count in bits 16-30, the mesh's first bone in 0-15
    uint Padding0;
    uint Padding1;
    uint Padding2;
};

layout(std430, binding = 0) readonly buffer g_objectBuffer
{
    ObjectDataStandard objects[];
};

layout(std430, binding = 1) readonly buffer g_transformBuffer
{
    mat3x4 transforms[];
};

#endif
