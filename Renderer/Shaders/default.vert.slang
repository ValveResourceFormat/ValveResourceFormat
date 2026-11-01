#version 460

in vec3 vPOSITION;
in vec4 vCOLOR;
out vec4 vtxColor;

#include "common/view_constants.slang"
#include "common/instancing.slang"

void main(void) {
    vtxColor = vCOLOR;
    gl_Position = g_matWorldToProjection * UnpackMatrix4(CalculateObjectToWorldMatrix()) * vec4(vPOSITION, 1.0);
}
