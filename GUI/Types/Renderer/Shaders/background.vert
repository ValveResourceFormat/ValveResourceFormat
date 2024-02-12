#version 460

in vec2 aVertexPosition;
out vec2 vtxPosition;

#include "common/ViewConstants.glsl"

void main(void) {
    vtxPosition = aVertexPosition;

    // vec4 vPositionWs = (g_matWorldToProjection * mat4(mat3(g_matWorldToView))) * vec4(aVertexPosition, 0.0, 1.0);
    gl_Position = vec4(aVertexPosition, 0.0, 1.0);
}
