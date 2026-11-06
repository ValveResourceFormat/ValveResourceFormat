#version 460

#include "eyes_features.slang"
#include "common_vs.slang"
#include "../common/morph.slang"

in vec3 vNORMAL;

// The direction the mouth faces in the space of the vertices, which its bone carries into the world
uniform int g_nMouthBoneIndex;
uniform vec3 g_vMouthBindForward = vec3(1.0, 0.0, 0.0);

out vec3 vFragPosition;
out vec3 vNormalOut;
out vec2 vTexCoordOut;
flat out vec3 vMouthForward;

mat3x4 GetMouthToWorldMatrix()
{
#if (D_SKINNING == 0)
    return CalculateObjectToWorldMatrix();
#else
    return ConcatTransforms(GetMatrix(uint(g_nMouthBoneIndex), GetObjectBoneOffset()), CalculateObjectToWorldMatrix());
#endif
}

void main()
{
    mat3x4 objectToWorld = CalculateSkinTransform();

    vFragPosition = vec4(vPOSITION + GetMorphOffset(), 1.0) * objectToWorld;
    vNormalOut = TransformDirectionToWorld(objectToWorld, ApplyMorphNormal(vNORMAL));
    gl_Position = g_matWorldToProjection * vec4(vFragPosition, 1.0);

    vTexCoordOut = GetSource1BaseTexCoord(vTEXCOORD);

    // The teeth are lit by the light that faces the inside of the mouth
    vMouthForward = -normalize(TransformDirectionToWorld(GetMouthToWorldMatrix(), g_vMouthBindForward));
}
