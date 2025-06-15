#version 460

#include "common/animation.glsl"

layout (location = 0) in vec3 vPOSITION;
layout (location = 3) in vec2 vTEXCOORD;

out vec2 vTexCoordOut;

#include "common/ViewConstants.glsl"
#include "common/instancing.glsl"

void main(void) {
    vTexCoordOut = vTEXCOORD;
    mat4 skinTransform = CalculateObjectToWorldMatrix() * getSkinMatrix();
    vec4 fragPosition = skinTransform * vec4(vPOSITION, 1.0);
    gl_Position = g_matWorldToProjection * fragPosition;
}
