#version 460

#include "common/instancing.slang"
#include "common/animation.slang"

layout (location = 0) in vec3 vPOSITION;

#include "common/ViewConstants.slang"

void main(void) {
    gl_Position = g_matWorldToProjection * CalculateObjectToWorldMatrix() * getSkinMatrix() * vec4(vPOSITION, 1.0);
}
