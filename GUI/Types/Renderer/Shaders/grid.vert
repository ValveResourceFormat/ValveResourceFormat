#version 460

in vec3 aVertexPosition;
out vec3 vtxPosition;
out vec3 nearPoint;
out vec3 farPoint;

#include "common/ViewConstants.glsl"

vec3 UnprojectPoint(float x, float y, float z) {
    mat4 viewInv = inverse(g_matWorldToView);
    mat4 projInv = inverse(g_matWorldToProjection);
    vec4 unprojectedPoint = viewInv * projInv * vec4(x, y, 1-z, 1.0);
    return unprojectedPoint.xyz / unprojectedPoint.w;
}

void main(void) {
    vtxPosition = aVertexPosition;

    nearPoint = UnprojectPoint(aVertexPosition.x, aVertexPosition.y, 0.0).xyz; // unprojecting on the near plane
    farPoint = UnprojectPoint(aVertexPosition.x, aVertexPosition.y, 1.0).xyz; // unprojecting on the far plane
    gl_Position = vec4(aVertexPosition, 1.0); // using directly the clipped coordinates
}
