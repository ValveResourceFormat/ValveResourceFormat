#version 460

#ifndef VRF_ANIMATION_INCLUDE
#define VRF_ANIMATION_INCLUDE

#include "instancing.slang"
#include "utils.slang"

in uvec4 vBLENDINDICES;
in vec4 vBLENDWEIGHT;

#define D_EIGHT_BONE_BLENDING 0

#if (D_EIGHT_BONE_BLENDING == 1)
    in uvec4 vBLENDINDICES2;
    in vec4 vBLENDWEIGHT2;
#endif

uniform uvec4 uAnimationData;

layout(std430, binding = 11) readonly buffer g_boneTransformBuffer
{
    mat3x4 boneTransforms[];
};

#define bAnimated (uAnimationData.x != 0u)
#define meshBoneOffset uAnimationData.y
#define meshBoneCount uAnimationData.z
#define numWeights uAnimationData.w

// Scale is assumed to be uniform.
vec3 TransformDirectionToWorld(mat3x4 objectToWorld, vec3 vDirectionOs)
{
    // Plain normalize would give us NaN for zero-scaled bones.
    return safeNormalize(vec4(vDirectionOs, 0.0) * objectToWorld);
}

mat3x4 getMatrix(uint boneIndex)
{
    // Issue #705 out of bounds bone index (model needs ApplyVBIBDefaults)
    // Model:  hlvr/models/props/xen/xen_villi_medium.vmdl
    // In map: hlvr/maps/a3_distillery.vmap
    if (boneIndex >= meshBoneCount) {
        return mat3x4(1.0);
    }

    boneIndex += meshBoneOffset;

    return boneTransforms[boneIndex];
}

mat3x4 getSkinMatrix()
{
    if (!bAnimated)
    {
        return mat3x4(1.0);
    }

    if (numWeights == 1u)
    {
        return getMatrix(vBLENDINDICES.x);
    }

    mat3x4 skinMatrix = mat3x4(0.0);


    skinMatrix += vBLENDWEIGHT.x * getMatrix(vBLENDINDICES.x);
    skinMatrix += vBLENDWEIGHT.y * getMatrix(vBLENDINDICES.y);
    skinMatrix += vBLENDWEIGHT.z * getMatrix(vBLENDINDICES.z);
    skinMatrix += vBLENDWEIGHT.w * getMatrix(vBLENDINDICES.w);

    #if (D_EIGHT_BONE_BLENDING == 1)
        skinMatrix += vBLENDWEIGHT2.x * getMatrix(vBLENDINDICES2.x);
        skinMatrix += vBLENDWEIGHT2.y * getMatrix(vBLENDINDICES2.y);
        skinMatrix += vBLENDWEIGHT2.z * getMatrix(vBLENDINDICES2.z);
        skinMatrix += vBLENDWEIGHT2.w * getMatrix(vBLENDINDICES2.w);
    #endif

    return skinMatrix;
}

// The full object to world matrix for this vertex. The skinning matrices stay in object space, so the
// object transform still has to be applied on top of them here.
mat3x4 CalculateSkinTransform()
{
    return ConcatTransforms(getSkinMatrix(), CalculateObjectToWorldMatrix());
}

#endif
