#version 460

layout (location = 0) in vec3 vPOSITION;
layout (location = 3) in vec2 vTEXCOORD;

out vec2 vTexCoordOut;

#include "common/ViewConstants.glsl"
uniform mat4 transform;

void main()
{
    vTexCoordOut = vTEXCOORD;
    gl_Position = g_matWorldToProjection * transform * vec4(vPOSITION, 1.0);
}
