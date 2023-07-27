#version 460

layout (location = 0) in vec3 vPOSITION;
layout (location = 1) in vec2 vTEXCOORD;
layout (location = 2) in vec4 vNORMAL;
layout (location = 3) in vec4 vCOLOR;

#include "animation.incl"
#include "compression.incl"

out vec3 vFragPosition;

out vec3 vNormalOut;
out vec3 vTangentOut;
out vec3 vBitangentOut;

out vec2 vTexCoordOut;
out vec4 vColorOut;

#include "common/ViewConstants.glsl"
uniform mat4 transform;

void main()
{
    mat4 skinTransform = transform * getSkinMatrix();
    vec4 fragPosition = skinTransform * vec4(vPOSITION, 1.0);
    gl_Position = g_matViewToProjection * fragPosition;
    vFragPosition = fragPosition.xyz / fragPosition.w;

    mat3 normalTransform = transpose(inverse(mat3(skinTransform)));

    vec4 tangent = DecompressTangent(vNORMAL);
    vNormalOut = normalize(normalTransform * DecompressNormal(vNORMAL));
    vTangentOut = normalize(normalTransform * tangent.xyz);
    vBitangentOut = tangent.w * cross( vNormalOut, vTangentOut );

    vTexCoordOut = vTEXCOORD;
    vColorOut = vCOLOR;
}
