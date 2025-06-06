#version 460

#include "common/animation.glsl"

layout (location = 0) in vec3 vPOSITION;

#include "common/ViewConstants.glsl"
uniform mat4 transform;

void main(void) {
    gl_Position = g_matWorldToProjection * transform * getSkinMatrix() * vec4(vPOSITION, 1.0);
}
