#version 460

#include "common/animation.glsl"

layout (location = 0) in vec3 vPOSITION;

#include "common/ViewConstants.glsl"
uniform mat3x4 transform;

void main(void) {
    vec4 worldPos = transform * vPOSITION;
    gl_Position = g_matWorldToProjection * worldPos;
}
