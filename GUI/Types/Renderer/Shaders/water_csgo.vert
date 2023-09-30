#version 460

layout (location = 0) in vec3 vPOSITION;
in vec2 vTEXCOORD;
//in vec2 vLightmapUV;
#include "common/compression.glsl"
in vec4 vCOLOR;

out vec3 vFragPosition;
out vec2 vTexCoordOut;
out vec3 vNormalOut;
out vec4 vTangentOut;
out vec3 vBitangentOut;
out vec4 vColorBlendValues;

#include "common/ViewConstants.glsl"
uniform mat4 transform;

void main()
{
    vec4 fragPosition = transform * vec4(vPOSITION, 1.0);
    gl_Position = g_matViewToProjection * fragPosition;
    vFragPosition = fragPosition.xyz / fragPosition.w;

    GetOptionallyCompressedNormalTangent(vNormalOut, vTangentOut);

    vTexCoordOut = vTEXCOORD;
    vColorBlendValues = vCOLOR / 255.0f;
}
