#version 460

in vec3 vPOSITION;
out vec3 vtxPosWs;

#include "common/ViewConstants.slang"
#include "common/instancing.slang"

void main(void)
{
    mat3x4 objectToWorld = CalculateObjectToWorldMatrix();
    vtxPosWs = vec4(vPOSITION, 1.0) * objectToWorld;

    gl_Position = g_matWorldToProjection * vec4(vtxPosWs, 1.0);
}
