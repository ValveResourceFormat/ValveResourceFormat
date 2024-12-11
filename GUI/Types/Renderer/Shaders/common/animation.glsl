#version 460

layout (location = 1) in uvec4 vBLENDINDICES;
layout (location = 2) in vec4 vBLENDWEIGHT;

uniform uvec4 iAnimationData;
uniform sampler2D animationTexture;

#define bAnimated iAnimationData.x != 0u
#define numBones iAnimationData.y
#define numWeights iAnimationData.z
#define animationTextureOffset iAnimationData.w

mat4 getMatrix(uint boneIndex)
{
    boneIndex += animationTextureOffset;

    // Issue #705 out of bounds bone index (model needs ApplyVBIBDefaults)
    // Model:  hlvr/models/props/xen/xen_villi_medium.vmdl
    // In map: hlvr/maps/a3_distillery.vmap
    // if (boneIndex >= numBones) {
    //     return mat4(1.0);
    // }

    return mat4(
        texelFetch(animationTexture, ivec2(0, boneIndex), 0),
        texelFetch(animationTexture, ivec2(1, boneIndex), 0),
        texelFetch(animationTexture, ivec2(2, boneIndex), 0),
        texelFetch(animationTexture, ivec2(3, boneIndex), 0)
    );
}

mat4 getSkinMatrix()
{
    //[branch]
    if (bAnimated)
    {
        mat4 skinMatrix = mat4(0.0);
        skinMatrix += vBLENDWEIGHT.x * getMatrix(vBLENDINDICES.x);
        skinMatrix += vBLENDWEIGHT.y * getMatrix(vBLENDINDICES.y);
        skinMatrix += vBLENDWEIGHT.z * getMatrix(vBLENDINDICES.z);
        skinMatrix += vBLENDWEIGHT.w * getMatrix(vBLENDINDICES.w);
        return skinMatrix;
    }

    return mat4(1.0);
}
