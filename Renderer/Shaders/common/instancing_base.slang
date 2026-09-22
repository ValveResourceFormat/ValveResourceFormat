#version 460

#ifndef VRF_INSTANCING_BASE_INCLUDE
#define VRF_INSTANCING_BASE_INCLUDE

struct InstanceDataStandard
{
    uint TintAlpha;
    uint TransformIndex;
    uint Identification; // the scene node, which also indexes g_objectBuffer[]
    uint MeshBoneData; // skinned flag in bit 31, bone count in bits 16-30, the mesh's first bone in 0-15
};

struct ObjectDataStandard
{
    uint VisibleLPV; // light probe volume in the low 16 bits, first env map in the high 16
    uint Padding0;
    uint Padding1;
    uint Padding2;
    uvec4 EnvMapVisibility;
};

layout(std430, binding = 0) readonly buffer InstanceBuffer
{
    InstanceDataStandard g_instanceBuffer[];
};

layout(std430, binding = 1) readonly buffer TransformBuffer
{
    mat3x4 g_transformBuffer[];
};

layout(std430, binding = 2) readonly buffer ObjectBuffer
{
    ObjectDataStandard g_objectBuffer[];
};

#endif
