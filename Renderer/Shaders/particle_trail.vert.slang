#version 460

layout (location = 0) in vec3 aVertexPosition;

#include "common/ViewConstants.slang"
uniform mat4 uModelMatrix;

out vec2 uv;
out vec3 vFragPosition;

void main(void) {
    uv = aVertexPosition.xy * 0.5 + 0.5;
    vec4 worldPosition = uModelMatrix * vec4(aVertexPosition, 1.0);
    vFragPosition = worldPosition.xyz;
    gl_Position = g_matWorldToProjection * worldPosition;
}
