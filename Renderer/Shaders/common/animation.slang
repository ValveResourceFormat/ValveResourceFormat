#version 460

#ifndef VRF_ANIMATION_INCLUDE
#define VRF_ANIMATION_INCLUDE

#include "instancing.slang"
#include "utils.slang"

#define D_SKINNING 0

#if (D_SKINNING > 0)
    in uvec4 vBLENDINDICES;
#endif
#if (D_SKINNING > 1)
    in vec4 vBLENDWEIGHT;
#endif
#if (D_SKINNING > 2)
    in uvec4 vBLENDINDICES2;
    in vec4 vBLENDWEIGHT2;
#endif

#define MESH_BONE_DATA (g_instanceBuffer[gl_BaseInstance].MeshBoneData)

#define IS_ANIMATED ((MESH_BONE_DATA & 0x80000000u) != 0u)
#define MESH_BONE_OFFSET (MESH_BONE_DATA & 0xFFFFu)
#define MESH_BONE_COUNT ((MESH_BONE_DATA >> 16u) & 0x7FFFu)

mat3x4 GetMatrix(uint boneIndex, uint objectBoneOffset)
{
    // Issue #705 out of bounds bone index (model needs ApplyVBIBDefaults)
    // Model:  hlvr/models/props/xen/xen_villi_medium.vmdl
    // In map: hlvr/maps/a3_distillery.vmap
    if (boneIndex >= MESH_BONE_COUNT) {
        return mat3x4(1.0);
    }

    boneIndex += objectBoneOffset + MESH_BONE_OFFSET;

    return g_transformBuffer[boneIndex];
}

#if (D_SKINNING > 0)
mat3x4 GetSkinMatrix()
{
    if (!IS_ANIMATED)
    {
        return mat3x4(1.0);
    }

    uint objectBoneOffset = GetObjectBoneOffset();

    #if (D_SKINNING == 1)
        return GetMatrix(vBLENDINDICES.x, objectBoneOffset);
    #else
        mat3x4 skinMatrix = mat3x4(0.0);

        skinMatrix += vBLENDWEIGHT.x * GetMatrix(vBLENDINDICES.x, objectBoneOffset);
        skinMatrix += vBLENDWEIGHT.y * GetMatrix(vBLENDINDICES.y, objectBoneOffset);
        skinMatrix += vBLENDWEIGHT.z * GetMatrix(vBLENDINDICES.z, objectBoneOffset);
        skinMatrix += vBLENDWEIGHT.w * GetMatrix(vBLENDINDICES.w, objectBoneOffset);

        #if (D_SKINNING == 3)
            skinMatrix += vBLENDWEIGHT2.x * GetMatrix(vBLENDINDICES2.x, objectBoneOffset);
            skinMatrix += vBLENDWEIGHT2.y * GetMatrix(vBLENDINDICES2.y, objectBoneOffset);
            skinMatrix += vBLENDWEIGHT2.z * GetMatrix(vBLENDINDICES2.z, objectBoneOffset);
            skinMatrix += vBLENDWEIGHT2.w * GetMatrix(vBLENDINDICES2.w, objectBoneOffset);
        #endif

        return skinMatrix;
    #endif
}
#endif

// The full object to world matrix for this vertex. The skinning matrices stay in object space, so the
// object transform still has to be applied on top of them here.
mat3x4 CalculateSkinTransform()
{
#if (D_SKINNING == 0)
    return CalculateObjectToWorldMatrix();
#else
    return ConcatTransforms(GetSkinMatrix(), CalculateObjectToWorldMatrix());
#endif
}

#endif
