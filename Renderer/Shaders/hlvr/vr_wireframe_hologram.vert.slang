#version 460

#include "../common/utils.slang"
#include "../common/view_constants.slang"

#include "../common/instancing.slang"
#include "../common/animation.slang"
#include "../common/morph.slang"

in vec3 vPOSITION;
#include "../common/compression.slang"

out vec3 vFragPosition;
out vec3 vNormalOut;

void main()
{
    mat3x4 skinTransform = CalculateSkinTransform();
    vFragPosition = vec4(vPOSITION + GetMorphOffset(), 1.0) * skinTransform;

    vec3 normal;
    vec4 tangent;
    GetOptionallyCompressedNormalTangent(normal, tangent);

    vNormalOut = normalize(TransformDirectionToWorld(skinTransform, normal));

    // The fill is pulled toward the camera so that it stays behind the lines of the wireframe
    gl_Position = g_matWorldToProjection * vec4(vFragPosition, 1.0);
    gl_Position.z -= 0.01;
}
