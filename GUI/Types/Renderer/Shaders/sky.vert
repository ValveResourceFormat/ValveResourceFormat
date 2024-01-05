#version 460

layout (location = 0) in vec3 aVertexPosition;
out vec3 vSkyLookupInterpolant;

#include "common/ViewConstants.glsl"

uniform mat4 g_matSkyRotation;

void main()
{
    vec4 vRotatedPosition = g_matSkyRotation * vec4(aVertexPosition, 1.0);
    mat4 matWorldToCenterView = mat4(mat3(g_matWorldToView));
    vec4 vPositionWs = (g_matWorldToProjection * matWorldToCenterView) * vRotatedPosition;

    gl_Position = vec4(vPositionWs.xy, 0.0, vPositionWs.w);
    vSkyLookupInterpolant = aVertexPosition;
}
