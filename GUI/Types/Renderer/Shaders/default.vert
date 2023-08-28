#version 460

in vec3 aVertexPosition;
in vec4 aVertexColor;
out vec4 vtxColor;

#include "common/ViewConstants.glsl"
uniform mat4 transform;

void main(void) {
    vtxColor = aVertexColor;
    gl_Position = g_matViewToProjection * transform * vec4(aVertexPosition, 1.0);
}
