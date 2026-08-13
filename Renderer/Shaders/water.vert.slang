#version 460

in vec3 vPOSITION;
in vec2 vTEXCOORD;
#include "common/compression.slang"

out vec3 vFragPosition;

out vec3 vNormalOut;
out vec4 vTangentOut;
out vec3 vBitangentOut;

out vec2 vTexCoordOut;

#include "common/ViewConstants.slang"
#include "common/instancing.slang"

void main()
{
    vec3 fragPosition = vec4(vPOSITION, 1.0) * CalculateObjectToWorldMatrix();
    gl_Position = g_matWorldToProjection * vec4(fragPosition, 1.0);
    vFragPosition = fragPosition;

    GetOptionallyCompressedNormalTangent(vNormalOut, vTangentOut);
    vTexCoordOut = vTEXCOORD;
}
