#version 460

layout (location = 0) in vec3 aVertexPosition;
layout (location = 3) in vec2 aTexCoords;
layout (location = 4) in vec4 aVertexColor;

#include "common/ViewConstants.slang"

out vec2 vTexCoordOut;
out vec4 vColor;
out vec3 vFragPosition;

void main(void) {
    vColor = aVertexColor;
    vTexCoordOut = aTexCoords;
    vFragPosition = aVertexPosition;
    gl_Position = g_matWorldToProjection * vec4(aVertexPosition, 1.0);
}
