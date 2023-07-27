#version 460 core

in vec3 vPOSITION;
in vec2 vTEXCOORD;

out vec2 vTexCoordOut;

#include "common/ViewConstants.glsl"
uniform mat4 transform;

void main()
{
    vTexCoordOut = vTEXCOORD;
    gl_Position = g_matViewToProjection * transform * vec4(vPOSITION, 1.0);
}
