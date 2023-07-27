#version 460

//Includes - resolved by VRF
#include "animation.incl"
//End of includes

layout (location = 0) in vec3 vPOSITION;

#include "common/ViewConstants.glsl"
uniform mat4 transform;

void main(void) {
    gl_Position = g_matViewToProjection * transform * getSkinMatrix() * vec4(vPOSITION, 1.0);
}
