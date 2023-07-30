#version 460

layout (location = 0) in vec3 aVertexPosition;
out vec3 vSkyLookupInterpolant;

uniform mat4 g_matWorldToProjection;
uniform mat4 g_matWorldToView;
uniform mat4 g_matViewToProjection;

uniform vec3 g_vCameraPositionWs;

uniform mat4 g_matSkyRotation;

void main()
{
    vec4 vRotatedPosition = g_matSkyRotation * vec4(aVertexPosition, 1.0);
    mat4 matWorldToCenterView = mat4(mat3(g_matWorldToView));
    vec4 vPositionWs = (g_matWorldToProjection * matWorldToCenterView) * vRotatedPosition;

    gl_Position = vPositionWs.xyww;
    vSkyLookupInterpolant = aVertexPosition;
}
