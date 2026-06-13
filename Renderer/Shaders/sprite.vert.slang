#version 460

layout (location = 0) in vec3 vPOSITION;
layout (location = 3) in vec2 vTEXCOORD;

out vec2 vTexCoordOut;

#include "common/ViewConstants.slang"
#include "common/instancing.slang"

void main()
{
    vTexCoordOut = vTEXCOORD;
    gl_Position = g_matWorldToProjection * CalculateObjectToWorldMatrix() * vec4(vPOSITION, 1.0);
}
