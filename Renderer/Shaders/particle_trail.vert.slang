#version 460

layout (location = 0) in vec3 aVertexPosition;

#include "common/ViewConstants.slang"
uniform mat4 uModelMatrix;

out vec2 uv;

void main(void) {
    uv = aVertexPosition.xy * 0.5 + 0.5;
    gl_Position = g_matWorldToProjection * uModelMatrix * vec4(aVertexPosition, 1.0);
}
