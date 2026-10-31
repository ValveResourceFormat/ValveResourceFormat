#version 460

#ifndef VRF_INSTANCING_BASE_INCLUDE
#define VRF_INSTANCING_BASE_INCLUDE

struct InstanceDataStandard
{
    uint TintAlpha;
    uint TransformIndex;
    uint Identification; // the scene node, which also indexes its own entry
    uint MeshBoneData; // skinned flag in bit 31, bone count in bits 16-30, the mesh's first bone in 0-15
    int MorphVertexIdOffset;
    uint MorphAtlasOrigin;
    uint MorphAtlasStride;
    uint LightingData; // light probe volume in the low 16 bits, env map in the high 16
};

layout(std430, binding = 0) readonly buffer InstanceBuffer
{
    InstanceDataStandard g_instanceBuffer[];
};

layout(std430, binding = 1) readonly buffer TransformBuffer
{
    mat3x4 g_transformBuffer[];
};

#endif
