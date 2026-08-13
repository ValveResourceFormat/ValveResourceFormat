#version 460

#include "common/instancing.slang"
#include "common/animation.slang"

in vec3 vPOSITION;

out vec3 vFragPosition;

#include "common/ViewConstants.slang"

void main()
{
    vec3 fragPosition = vec4(vPOSITION, 1.0) * CalculateSkinTransform();
    gl_Position = g_matWorldToProjection * vec4(fragPosition, 1.0);
    vFragPosition = fragPosition;
}
