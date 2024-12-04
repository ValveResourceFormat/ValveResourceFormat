#version 460

#include "common/ViewConstants.glsl"

layout(location = 0) in vec4 vAABBMin_Size;
layout(location = 1) in ivec2 nIndices;

flat out vec3 color;

const vec3 BOX[36] =
{
    // positions
    vec3(0.0, 1.0, 0.0),
    vec3(0.0, 0.0, 0.0),
    vec3(1.0, 0.0, 0.0),
    vec3(1.0, 0.0, 0.0),
    vec3(1.0, 1.0, 0.0),
    vec3(0.0, 1.0, 0.0),

    vec3(0.0, 0.0, 1.0),
    vec3(0.0, 0.0, 0.0),
    vec3(0.0, 1.0, 0.0),
    vec3(0.0, 1.0, 0.0),
    vec3(0.0, 1.0, 1.0),
    vec3(0.0, 0.0, 1.0),

    vec3(1.0, 0.0, 0.0),
    vec3(1.0, 0.0, 1.0),
    vec3(1.0, 1.0, 1.0),
    vec3(1.0, 1.0, 1.0),
    vec3(1.0, 1.0, 0.0),
    vec3(1.0, 0.0, 0.0),

    vec3(0.0, 0.0, 1.0),
    vec3(0.0, 1.0, 1.0),
    vec3(1.0, 1.0, 1.0),
    vec3(1.0, 1.0, 1.0),
    vec3(1.0, 0.0, 1.0),
    vec3(0.0, 0.0, 1.0),

    vec3(0.0, 1.0, 0.0),
    vec3(1.0, 1.0, 0.0),
    vec3(1.0, 1.0, 1.0),
    vec3(1.0, 1.0, 1.0),
    vec3(0.0, 1.0, 1.0),
    vec3(0.0, 1.0, 0.0),

    vec3(0.0, 0.0, 0.0),
    vec3(0.0, 0.0, 1.0),
    vec3(1.0, 0.0, 0.0),
    vec3(1.0, 0.0, 0.0),
    vec3(0.0, 0.0, 1.0),
    vec3(1.0, 0.0, 1.0)
};

void main()
{
    // assuming AABB 12 triangles
    // gl_VertexID is [0..35]

    vec3 vMins = vAABBMin_Size.xyz;
    vec3 vMaxs = vAABBMin_Size.xyz + vAABBMin_Size.www;

    vec4 fragPosition = vec4(BOX[gl_VertexID] * vAABBMin_Size.www + vAABBMin_Size.xyz, 1.0);

    int depth = nIndices.x;
    int index = nIndices.y;

    color = vec3(float(index) / 255.0, float(depth) / 10.0, 0.0);

    gl_Position = g_matViewToProjection * fragPosition;
}
