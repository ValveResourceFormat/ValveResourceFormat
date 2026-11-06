#version 460

#include "studio_common.slang"
#include "../common/view_constants.slang"
#include "../common/animation.slang"

in vec3 vPOSITION;
in vec3 vNORMAL;
in vec2 vTEXCOORD;
// The rest position of the bone of the normal, in the space of the vertices
in vec3 vTEXCOORD1;

out vec3 vFragPosition;
out vec3 vNormalOut;
out vec2 vTexCoordOut;
out vec4 vTintOut;

// The vertices of a model are in model space at the rest pose and skinned by the bone of their position. The normal
// can be on another bone, which the second index of the vertex names.
mat3x4 GetNormalSkinMatrix()
{
#if (D_SKINNING > 0)
    if (IS_ANIMATED)
    {
        return GetMatrix(vBLENDINDICES.y, GetObjectBoneOffset());
    }
#endif

    return mat3x4(1.0);
}

void main()
{
    mat3x4 positionToWorld = CalculateSkinTransform();
    mat3x4 normalToWorld = ConcatTransforms(GetNormalSkinMatrix(), CalculateObjectToWorldMatrix());

    vFragPosition = vec4(vPOSITION, 1.0) * positionToWorld;
    vNormalOut = TransformDirectionToWorld(normalToWorld, vNORMAL);
    gl_Position = g_matWorldToProjection * vec4(vFragPosition, 1.0);

    vTexCoordOut = vTEXCOORD;
    vTintOut = GetObjectTintSrgb();

    if (F_CHROME != 0)
    {
        // The reflection is looked up by where the normal points as seen from the camera, with an up and a right
        // that depend on the direction from the camera to the bone of the normal
        vec3 boneOrigin = vec4(vTEXCOORD1, 1.0) * normalToWorld;
        vec3 toBone = normalize(boneOrigin - g_vCameraPositionWs);
        vec3 chromeRight = normalize(cross(toBone, g_vCameraUpDirWs));
        vec3 chromeUp = normalize(cross(chromeRight, toBone));

        // In texels of the texture, which the fragment shader divides by its size. Up gives the horizontal
        // coordinate and right the vertical one.
        vTexCoordOut = vec2(dot(vNormalOut, chromeUp) + 1.0, dot(vNormalOut, chromeRight) + 1.0) * ChromeTexelScale;
    }
}
