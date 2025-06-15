#version 460

#include "common/animation.glsl"

layout (location = 0) in vec3 vPOSITION;

#include "common/ViewConstants.glsl"
#include "common/instancing.glsl"

void main(void) {
    gl_Position = g_matWorldToProjection * CalculateObjectToWorldMatrix() * getSkinMatrix() * vec4(vPOSITION, 1.0);
}
