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

public mat4 getMatrix(uint boneIndex)
{
    // Issue #705 out of bounds bone index (model needs ApplyVBIBDefaults)
    if (boneIndex >= meshBoneCount) {
        return mat4(1.0);
    }

    boneIndex += meshBoneOffset;

    return UnpackMatrix4(GetTransform(boneIndex));
}

public mat4 getSkinMatrix(AnimationInput input)
{
    if (!bAnimated)
    {
        return mat4(1.0);
    }

    if (numWeights == 1u)
    {
        return getMatrix(input.blendIndices.x);
    }

    mat4 skinMatrix = mat4(0.0);

    skinMatrix += input.blendWeight.x * getMatrix(input.blendIndices.x);
    skinMatrix += input.blendWeight.y * getMatrix(input.blendIndices.y);
    skinMatrix += input.blendWeight.z * getMatrix(input.blendIndices.z);
    skinMatrix += input.blendWeight.w * getMatrix(input.blendIndices.w);

    return skinMatrix;
}
