#version 460

in vec3 vPOSITION;
in vec4 vCOLOR;
out vec4 vtxColor;

#include "common/ViewConstants.slang"
#include "common/instancing.slang"

void main(void) {
    vtxColor = vCOLOR;
    gl_Position = g_matWorldToProjection * UnpackMatrix4(transform) * vec4(vPOSITION, 1.0);
}
