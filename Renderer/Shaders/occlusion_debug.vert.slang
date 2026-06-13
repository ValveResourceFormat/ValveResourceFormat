#version 460

#include "common/ViewConstants.slang"
#include "common/structs.slang"

layout(binding = 7, std430) readonly buffer OccludedBoundsDebugBuffer
{
    uint occludedCount;
    uint padding[3];
    DrawBounds occludedBounds[];
};

void main()
{
    // Each AABB has 24 vertices (12 lines * 2 vertices per line)
    uint boundIndex = gl_VertexID / 24;
    uint vertexInBound = gl_VertexID % 24;
    
    DrawBounds bound = occludedBounds[boundIndex];
    vec3 bmin = bound.min;
    vec3 bmax = bound.max;
    
    vec3 pos;
    
    // 12 edges encoded as pairs of corner indices (each corner = 3 bits: x|y|z, 0=min 1=max)
    // Bottom(0-3), Top(4-7), Vertical(8-11)
    const uint edges[24] = uint[](
        0,1, 1,3, 3,2, 2,0,  // Bottom: 000->001, 001->011, 011->010, 010->000
        4,5, 5,7, 7,6, 6,4,  // Top: 100->101, 101->111, 111->110, 110->100
        0,4, 1,5, 3,7, 2,6   // Verticals: connect bottom to top
    );
    
    uint cornerIdx = edges[vertexInBound];
    pos = vec3(
        (cornerIdx & 1u) != 0u ? bmax.x : bmin.x,
        (cornerIdx & 2u) != 0u ? bmax.y : bmin.y,
        (cornerIdx & 4u) != 0u ? bmax.z : bmin.z
    );
    
    gl_Position = g_matWorldToProjection * vec4(pos, 1.0);
}
