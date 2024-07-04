#version 460

#include "common/ViewConstants.glsl"

uniform vec3 g_vMins;
uniform vec3 g_vMaxs;

void main()
{
    // assuming AABB 12 triangles
    // gl_VertexID is [0..35]

    vec3 v[8];
    v[0] = vec3(g_vMins.x, g_vMins.y, g_vMins.z);
    v[1] = vec3(g_vMaxs.x, g_vMins.y, g_vMins.z);
    v[2] = vec3(g_vMaxs.x, g_vMaxs.y, g_vMins.z);
    v[3] = vec3(g_vMins.x, g_vMaxs.y, g_vMins.z);
    v[4] = vec3(g_vMins.x, g_vMins.y, g_vMaxs.z);
    v[5] = vec3(g_vMaxs.x, g_vMins.y, g_vMaxs.z);
    v[6] = vec3(g_vMaxs.x, g_vMaxs.y, g_vMaxs.z);
    v[7] = vec3(g_vMins.x, g_vMaxs.y, g_vMaxs.z);

    vec4 fragPosition = vec4(v[gl_VertexID / 3], 1);

    gl_Position = g_matViewToProjection * fragPosition;
}
