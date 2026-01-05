#version 460

const vec4 BOX[36] =
{
    // positions
    vec4(-1.0,  1.0, -1.0, 1.0),
    vec4(-1.0, -1.0, -1.0, 1.0),
    vec4( 1.0, -1.0, -1.0, 1.0),
    vec4( 1.0, -1.0, -1.0, 1.0),
    vec4( 1.0,  1.0, -1.0, 1.0),
    vec4(-1.0,  1.0, -1.0, 1.0),

    vec4(-1.0, -1.0,  1.0, 1.0),
    vec4(-1.0, -1.0, -1.0, 1.0),
    vec4(-1.0,  1.0, -1.0, 1.0),
    vec4(-1.0,  1.0, -1.0, 1.0),
    vec4(-1.0,  1.0,  1.0, 1.0),
    vec4(-1.0, -1.0,  1.0, 1.0),

    vec4(1.0, -1.0, -1.0, 1.0),
    vec4(1.0, -1.0,  1.0, 1.0),
    vec4(1.0,  1.0,  1.0, 1.0),
    vec4(1.0,  1.0,  1.0, 1.0),
    vec4(1.0,  1.0, -1.0, 1.0),
    vec4(1.0, -1.0, -1.0, 1.0),

    vec4(-1.0, -1.0,  1.0, 1.0),
    vec4(-1.0,  1.0,  1.0, 1.0),
    vec4( 1.0,  1.0,  1.0, 1.0),
    vec4( 1.0,  1.0,  1.0, 1.0),
    vec4( 1.0, -1.0,  1.0, 1.0),
    vec4(-1.0, -1.0,  1.0, 1.0),

    vec4(-1.0,  1.0, -1.0, 1.0),
    vec4( 1.0,  1.0, -1.0, 1.0),
    vec4( 1.0,  1.0,  1.0, 1.0),
    vec4( 1.0,  1.0,  1.0, 1.0),
    vec4(-1.0,  1.0,  1.0, 1.0),
    vec4(-1.0,  1.0, -1.0, 1.0),

    vec4(-1.0, -1.0, -1.0, 1.0),
    vec4(-1.0, -1.0,  1.0, 1.0),
    vec4( 1.0, -1.0, -1.0, 1.0),
    vec4( 1.0, -1.0, -1.0, 1.0),
    vec4(-1.0, -1.0,  1.0, 1.0),
    vec4( 1.0, -1.0,  1.0, 1.0)
};

out vec3 vSkyLookupInterpolant;

#include "common/ViewConstants.glsl"

uniform mat4 g_matSkyRotation;

void main()
{
    vec4 vPositionOs = BOX[gl_VertexID];
    vec4 vRotatedPosition = g_matSkyRotation * vPositionOs;
    mat4 matWorldToCenterView = mat4(mat3(g_matWorldToView));
    vec4 vPositionWs = (g_matViewToProjection * matWorldToCenterView) * vRotatedPosition;

    gl_Position = vPositionWs.xyww;
    gl_Position.z = 0.0;
    vSkyLookupInterpolant = vPositionOs.xyz;
}
