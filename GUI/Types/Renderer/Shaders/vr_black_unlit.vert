#version 460

#include "common/animation.glsl"

layout (location = 0) in vec3 vPOSITION;

out vec3 vFragPosition;

#include "common/ViewConstants.glsl"
uniform mat4 transform;

void main()
{
    vec4 fragPosition = transform * getSkinMatrix() * vec4(vPOSITION, 1.0);
    gl_Position = g_matWorldToProjection * fragPosition;
    vFragPosition = fragPosition.xyz / fragPosition.w;
}
