#version 460

import instancing;

uniform uvec4 uAnimationData;

#define bAnimated (uAnimationData.x != 0u)
#define meshBoneOffset uAnimationData.y
#define meshBoneCount uAnimationData.z
#define numWeights uAnimationData.w

public struct AnimationInput
{
    public uvec4 blendIndices;
    public vec4 blendWeight;
};

public static AnimationInput animInput;

public mat4 getMatrix(uint boneIndex)
{
    // Issue #705 out of bounds bone index (model needs ApplyVBIBDefaults)
    if (boneIndex >= meshBoneCount) {
        return mat4(1.0);
    }

    boneIndex += meshBoneOffset;

    return UnpackMatrix4(GetTransform(boneIndex));
}

public mat4 getSkinMatrix()
{
    if (!bAnimated)
    {
        return mat4(1.0);
    }

    if (numWeights == 1u)
    {
        return getMatrix(animInput.blendIndices.x);
    }

    mat4 skinMatrix = mat4(0.0);

    skinMatrix += animInput.blendWeight.x * getMatrix(animInput.blendIndices.x);
    skinMatrix += animInput.blendWeight.y * getMatrix(animInput.blendIndices.y);
    skinMatrix += animInput.blendWeight.z * getMatrix(animInput.blendIndices.z);
    skinMatrix += animInput.blendWeight.w * getMatrix(animInput.blendIndices.w);

    return skinMatrix;
}
