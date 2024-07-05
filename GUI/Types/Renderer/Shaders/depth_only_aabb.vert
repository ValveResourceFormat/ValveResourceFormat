#version 460

#include "common/ViewConstants.glsl"

layout(location = 0) in vec4 vAABBMin_Size;

void main()
{
    // assuming AABB 12 triangles
    // gl_VertexID is [0..35]

    vec3 vMins = vAABBMin_Size.xyz;
    vec3 vMaxs = vAABBMin_Size.xyz + vAABBMin_Size.www;

    vec3 v[8];
    v[0] = vec3(vMins.x, vMins.y, vMins.z);
    v[1] = vec3(vMaxs.x, vMins.y, vMins.z);
    v[2] = vec3(vMaxs.x, vMaxs.y, vMins.z);
    v[3] = vec3(vMins.x, vMaxs.y, vMins.z);
    v[4] = vec3(vMins.x, vMins.y, vMaxs.z);
    v[5] = vec3(vMaxs.x, vMins.y, vMaxs.z);
    v[6] = vec3(vMaxs.x, vMaxs.y, vMaxs.z);
    v[7] = vec3(vMins.x, vMaxs.y, vMaxs.z);

    vec4 fragPosition = vec4(v[gl_VertexID / 3], 1);

    gl_Position = g_matViewToProjection * fragPosition;
}
