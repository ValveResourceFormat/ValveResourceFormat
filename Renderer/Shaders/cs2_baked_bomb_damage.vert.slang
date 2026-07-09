#version 460

layout (location = 0) in vec3 aVertexPosition;
layout (location = 1) in vec4 aVertexColor;
layout (location = 2) in vec2 aTexCoords;
out vec4 vtxColor;
out vec2 vtxUv;
out vec3 vtxPos;

#include "common/ViewConstants.slang"
#include "common/instancing.slang"

void main(void) {
    vtxColor = aVertexColor;
    vtxUv = aTexCoords;
    vtxPos = aVertexPosition;

    gl_Position = g_matWorldToProjection * CalculateObjectToWorldMatrix() * vec4(aVertexPosition, 1.0);
}
