#version 460

#define D_EIGHT_BONE_BLENDING 0

#if (D_EIGHT_BONE_BLENDING == 0)
    layout (location = 1) in uvec4 vBLENDINDICES;
    layout (location = 2) in vec4 vBLENDWEIGHT;
#else 
    layout (location = 1) in uvec4 vBLENDINDICES;
    layout (location = 2) in uvec4 vBLENDWEIGHT;
#endif

uniform uvec4 uAnimationData;
uniform sampler2D animationTexture;

#define bAnimated (uAnimationData.x != 0u)
#define meshBoneOffset uAnimationData.y
#define meshBoneCount uAnimationData.z
#define numWeights uAnimationData.w

mat4 getMatrix(uint boneIndex)
{
    // Issue #705 out of bounds bone index (model needs ApplyVBIBDefaults)
    // Model:  hlvr/models/props/xen/xen_villi_medium.vmdl
    // In map: hlvr/maps/a3_distillery.vmap
    if (boneIndex >= meshBoneCount) {
        return mat4(1.0);
    }

    boneIndex += meshBoneOffset;

    return mat4(
        texelFetch(animationTexture, ivec2(0, boneIndex), 0),
        texelFetch(animationTexture, ivec2(1, boneIndex), 0),
        texelFetch(animationTexture, ivec2(2, boneIndex), 0),
        texelFetch(animationTexture, ivec2(3, boneIndex), 0)
    );
}

mat4 getSkinMatrix()
{
    if (!bAnimated)
    {
        return mat4(1.0);
    }

    if (numWeights == 1u)
    {
        return getMatrix(vBLENDINDICES.x);
    }

    mat4 skinMatrix = mat4(0.0);

    #if (D_EIGHT_BONE_BLENDING == 0)
        skinMatrix += vBLENDWEIGHT.x * getMatrix(vBLENDINDICES.x);
        skinMatrix += vBLENDWEIGHT.y * getMatrix(vBLENDINDICES.y);
        skinMatrix += vBLENDWEIGHT.z * getMatrix(vBLENDINDICES.z);
        skinMatrix += vBLENDWEIGHT.w * getMatrix(vBLENDINDICES.w);
    #else
        #if (D_EIGHT_BONE_BLENDING == 1)
            const uint intBoneIndexStride = 8u;
            const uint intBoneIndexMask = 255u;
        #elif (D_EIGHT_BONE_BLENDING == 2)
            const uint intBoneIndexStride = 16u;
            const uint intBoneIndexMask = 65535u;
        #else
            #error "Unhandled 8 bone blending mode"
        #endif

        uvec4 indices0 = uvec4(
            (vBLENDINDICES.x >> 0) & intBoneIndexMask,
            (vBLENDINDICES.x >> intBoneIndexStride) & intBoneIndexMask,
            (vBLENDINDICES.y >> 0) & intBoneIndexMask,
            (vBLENDINDICES.y >> intBoneIndexStride) & intBoneIndexMask
        );

        uvec4 indices1 = uvec4(
            (vBLENDINDICES.z >> 0) & intBoneIndexMask,
            (vBLENDINDICES.z >> intBoneIndexStride) & intBoneIndexMask,
            (vBLENDINDICES.w >> 0) & intBoneIndexMask,
            (vBLENDINDICES.w >> intBoneIndexStride) & intBoneIndexMask
        );

        vec4 weights0 = vec4(
            (vBLENDWEIGHT.x >> 0) & 255u,
            (vBLENDWEIGHT.x >> 8) & 255u,
            (vBLENDWEIGHT.y >> 0) & 255u,
            (vBLENDWEIGHT.y >> 8) & 255u
        ) / 255.0;

        vec4 weights1 = vec4(
            (vBLENDWEIGHT.z >> 0) & 255u,
            (vBLENDWEIGHT.z >> 8) & 255u,
            (vBLENDWEIGHT.w >> 0) & 255u,
            (vBLENDWEIGHT.w >> 8) & 255u
        ) / 255.0;

        skinMatrix += weights0[0] * getMatrix(indices0[0]);
        skinMatrix += weights0[1] * getMatrix(indices0[1]);
        skinMatrix += weights0[2] * getMatrix(indices0[2]);
        skinMatrix += weights0[3] * getMatrix(indices0[3]);

        skinMatrix += weights1[0] * getMatrix(indices1[0]);
        skinMatrix += weights1[1] * getMatrix(indices1[1]);
        skinMatrix += weights1[2] * getMatrix(indices1[2]);
        skinMatrix += weights1[3] * getMatrix(indices1[3]);
    #endif
    return skinMatrix;
}
