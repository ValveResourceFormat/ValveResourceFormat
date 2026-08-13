#version 460

#include "common/vertex_layout.slang"
layout (location = ATTRIBUTE_POSITION) in vec3 vPOSITION;
layout (location = ATTRIBUTE_NORMAL) in vec3 vNORMAL;
layout (location = ATTRIBUTE_COLOR) in vec4 vCOLOR;
out vec4 vtxColor;
out vec3 vtxNormal;
out vec3 vtxPos;

out vec3 camPos;

#include "common/ViewConstants.slang"
#include "common/instancing.slang"

void main(void) {
    vtxColor = vCOLOR;
    vtxNormal = vNORMAL;
    vtxPos = vPOSITION;

    camPos = g_vCameraPositionWs;

    gl_Position = g_matWorldToProjection * vec4(vec4(vPOSITION, 1.0) * CalculateObjectToWorldMatrix(), 1.0);
}
