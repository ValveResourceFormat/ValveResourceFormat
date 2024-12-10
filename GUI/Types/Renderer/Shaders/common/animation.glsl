#version 460

#define D_EIGHT_BONE_BLENDING 0

layout (location = 1) in uvec4 vBLENDINDICES;

#if (D_EIGHT_BONE_BLENDING == 0)
    layout (location = 2) in vec4 vBLENDWEIGHT;
#else 
    layout (location = 2) in uvec4 vBLENDWEIGHT;
#endif

uniform bool bAnimated;
uniform sampler2D animationTexture;

int getNumBones()
{
    return textureSize(animationTexture, 0).y;
}

mat4 getMatrix(uint boneIndex, int numBones)
{
    // Issue #705 out of bounds bone index (model needs ApplyVBIBDefaults)
    // Model:  hlvr/models/props/xen/xen_villi_medium.vmdl
    // In map: hlvr/maps/a3_distillery.vmap
    if (boneIndex >= numBones) {
        return mat4(1.0);
    }

    return mat4(
        texelFetch(animationTexture, ivec2(0, boneIndex), 0),
        texelFetch(animationTexture, ivec2(1, boneIndex), 0),
        texelFetch(animationTexture, ivec2(2, boneIndex), 0),
        texelFetch(animationTexture, ivec2(3, boneIndex), 0)
    );
}

mat4 getMatrix(uint boneIndex)
{
    int numBones = getNumBones();
    return getMatrix(boneIndex, numBones);
}

mat4 getSkinMatrix()
{
    int numBones = getNumBones();

    //[branch]
    if (bAnimated)
    {
        mat4 skinMatrix = mat4(0.0);

        
        #if (D_EIGHT_BONE_BLENDING == 1) || (D_EIGHT_BONE_BLENDING == 2)
            #if (D_EIGHT_BONE_BLENDING == 1)
                const uint intBoneIndexStride = 8u;
                const uint intBoneIndexMask = 255u;
            #elif (D_EIGHT_BONE_BLENDING == 2)
                const uint intBoneIndexStride = 16u;
                const uint intBoneIndexMask = 65535u;
            #else
                #error "Unhandled 8 bone blending mode"
            #endif

            // Extract 8 bone incides
            ivec4 indices4 = ivec4(
                (vBLENDINDICES.x >> 0) & intBoneIndexMask,
                (vBLENDINDICES.x >> intBoneIndexStride) & intBoneIndexMask,
                (vBLENDINDICES.y >> 0) & intBoneIndexMask,
                (vBLENDINDICES.y >> intBoneIndexStride) & intBoneIndexMask
            );

            ivec4 indices8 = ivec4(
                (vBLENDINDICES.z >> 0) & intBoneIndexMask,
                (vBLENDINDICES.z >> intBoneIndexStride) & intBoneIndexMask,
                (vBLENDINDICES.w >> 0) & intBoneIndexMask,
                (vBLENDINDICES.w >> intBoneIndexStride) & intBoneIndexMask
            );

            vec4 weights4 = vec4(
                (vBLENDWEIGHT.x >> 0) & 255u,
                (vBLENDWEIGHT.x >> 8) & 255u,
                (vBLENDWEIGHT.y >> 0) & 255u,
                (vBLENDWEIGHT.y >> 8) & 255u
            ) / 255.0;

            vec4 weights8 = vec4(
                (vBLENDWEIGHT.z >> 0) & 255u,
                (vBLENDWEIGHT.z >> 8) & 255u,
                (vBLENDWEIGHT.w >> 0) & 255u,
                (vBLENDWEIGHT.w >> 8) & 255u
            ) / 255.0;

            skinMatrix += weights4.x * getMatrix(indices4.x, numBones);
            skinMatrix += weights4.y * getMatrix(indices4.y, numBones);
            skinMatrix += weights4.z * getMatrix(indices4.z, numBones);
            skinMatrix += weights4.w * getMatrix(indices4.w, numBones);

            skinMatrix += weights8.x * getMatrix(indices8.x, numBones);
            skinMatrix += weights8.y * getMatrix(indices8.y, numBones);
            skinMatrix += weights8.z * getMatrix(indices8.z, numBones);
            skinMatrix += weights8.w * getMatrix(indices8.w, numBones);
        #else 
            skinMatrix += vBLENDWEIGHT.x * getMatrix(vBLENDINDICES.x, numBones);
            skinMatrix += vBLENDWEIGHT.y * getMatrix(vBLENDINDICES.y, numBones);
            skinMatrix += vBLENDWEIGHT.z * getMatrix(vBLENDINDICES.z, numBones);
            skinMatrix += vBLENDWEIGHT.w * getMatrix(vBLENDINDICES.w, numBones);
        #endif
        return skinMatrix;
    }

    return mat4(1.0);
}
