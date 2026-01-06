#version 460

#include "common/instancing.slang"
#include "common/animation.slang"

layout (location = 0) in vec3 vPOSITION;

out vec3 vFragPosition;

#include "common/ViewConstants.slang"

void main()
{
    vec4 fragPosition = CalculateObjectToWorldMatrix() * getSkinMatrix() * vec4(vPOSITION, 1.0);
    gl_Position = g_matWorldToProjection * fragPosition;
    vFragPosition = fragPosition.xyz;
}
