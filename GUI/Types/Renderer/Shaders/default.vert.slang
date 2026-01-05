#version 460

layout (location = 0) in vec3 aVertexPosition;
layout (location = 1) in vec4 aVertexColor;
out vec4 vtxColor;

#include "common/ViewConstants.glsl"
#include "common/instancing.glsl"

void main(void) {
    vtxColor = aVertexColor;
    gl_Position = g_matWorldToProjection * UnpackMatrix4(transform) * vec4(aVertexPosition, 1.0);
}
