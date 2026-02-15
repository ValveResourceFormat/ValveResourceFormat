#version 460

#include "common/ViewConstants.slang"

struct DrawBounds
{
    vec3 min;
    uint padding1;
    vec3 max;
    uint padding2;
};

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
    
    // Generate line vertices procedurally based on vertex index
    uint lineIndex = vertexInBound / 2;
    uint vertexInLine = vertexInBound % 2;
    
    // 12 edges of the box
    if (lineIndex == 0) // Bottom front
        pos = vertexInLine == 0 ? vec3(bmin.x, bmin.y, bmin.z) : vec3(bmax.x, bmin.y, bmin.z);
    else if (lineIndex == 1) // Bottom right
        pos = vertexInLine == 0 ? vec3(bmax.x, bmin.y, bmin.z) : vec3(bmax.x, bmin.y, bmax.z);
    else if (lineIndex == 2) // Bottom back
        pos = vertexInLine == 0 ? vec3(bmax.x, bmin.y, bmax.z) : vec3(bmin.x, bmin.y, bmax.z);
    else if (lineIndex == 3) // Bottom left
        pos = vertexInLine == 0 ? vec3(bmin.x, bmin.y, bmax.z) : vec3(bmin.x, bmin.y, bmin.z);
    else if (lineIndex == 4) // Top front
        pos = vertexInLine == 0 ? vec3(bmin.x, bmax.y, bmin.z) : vec3(bmax.x, bmax.y, bmin.z);
    else if (lineIndex == 5) // Top right
        pos = vertexInLine == 0 ? vec3(bmax.x, bmax.y, bmin.z) : vec3(bmax.x, bmax.y, bmax.z);
    else if (lineIndex == 6) // Top back
        pos = vertexInLine == 0 ? vec3(bmax.x, bmax.y, bmax.z) : vec3(bmin.x, bmax.y, bmax.z);
    else if (lineIndex == 7) // Top left
        pos = vertexInLine == 0 ? vec3(bmin.x, bmax.y, bmax.z) : vec3(bmin.x, bmax.y, bmin.z);
    else if (lineIndex == 8) // Vertical front-left
        pos = vertexInLine == 0 ? vec3(bmin.x, bmin.y, bmin.z) : vec3(bmin.x, bmax.y, bmin.z);
    else if (lineIndex == 9) // Vertical front-right
        pos = vertexInLine == 0 ? vec3(bmax.x, bmin.y, bmin.z) : vec3(bmax.x, bmax.y, bmin.z);
    else if (lineIndex == 10) // Vertical back-right
        pos = vertexInLine == 0 ? vec3(bmax.x, bmin.y, bmax.z) : vec3(bmax.x, bmax.y, bmax.z);
    else // lineIndex == 11 // Vertical back-left
        pos = vertexInLine == 0 ? vec3(bmin.x, bmin.y, bmax.z) : vec3(bmin.x, bmax.y, bmax.z);
    
    gl_Position = g_matWorldToProjection * vec4(pos, 1.0);
}
