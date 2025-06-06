#version 460

layout (location = 0) in vec3 aVertexPosition;
layout (location = 1) in vec3 aVertexNormal;
layout (location = 2) in vec4 aVertexColor;
out vec4 vtxColor;
out vec3 vtxNormal;
out vec3 vtxPos;

out vec3 camPos;

#include "common/ViewConstants.glsl"
uniform mat4 transform;

void main(void) {
    vtxColor = aVertexColor;
    vtxNormal = aVertexNormal;
    vtxPos = aVertexPosition;

    camPos = g_vCameraPositionWs;

    gl_Position = g_matWorldToProjection * transform * vec4(aVertexPosition, 1.0);
}
