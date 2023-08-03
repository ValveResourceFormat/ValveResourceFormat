#version 460

#include "common/compression.glsl"
#include "common/animation.glsl"

layout (location = 0) in vec3 vPOSITION;
in vec4 vNORMAL;
in vec2 vTEXCOORD;
#if (D_COMPRESSED_NORMALS_AND_TANGENTS == 0)
    in vec4 vTANGENT;
#endif

out vec3 vFragPosition;

out vec3 vNormalOut;
out vec3 vTangentOut;
out vec3 vBitangentOut;

out vec2 vTexCoordOut;

#include "common/ViewConstants.glsl"
uniform mat4 transform;

void main()
{
    mat4 skinTransformMatrix = transform * getSkinMatrix();
    vec4 fragPosition = skinTransformMatrix * vec4(vPOSITION, 1.0);
    gl_Position = g_matViewToProjection * fragPosition;
    vFragPosition = fragPosition.xyz / fragPosition.w;

    mat3 normalTransform = transpose(inverse(mat3(skinTransformMatrix)));

    //Unpack normals
#if (D_COMPRESSED_NORMALS_AND_TANGENTS == 0)
    vNormalOut = normalize(normalTransform * vNORMAL.xyz);
    vTangentOut = normalize(normalTransform * vTANGENT.xyz);
    vBitangentOut = cross(vNormalOut, vTangentOut);
#else
    vec4 tangent = DecompressTangent(vNORMAL);
    vNormalOut = normalize(normalTransform * DecompressNormal(vNORMAL));
    vTangentOut = normalize(normalTransform * tangent.xyz);
    vBitangentOut = tangent.w * cross(vNormalOut, vTangentOut);
#endif

    vTexCoordOut = vTEXCOORD;
}
