#version 460

#include "common/vertex_layout.slang"
layout (location = ATTRIBUTE_POSITION) in vec3 vPOSITION;
layout (location = ATTRIBUTE_COLOR) in vec4 vCOLOR;
out vec4 vtxColor;

#include "common/ViewConstants.slang"
#include "common/instancing.slang"

void main(void) {
    vtxColor = vCOLOR;
    gl_Position = g_matWorldToProjection * UnpackMatrix4(transform) * vec4(vPOSITION, 1.0);
}
