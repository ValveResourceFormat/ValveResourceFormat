#version 460

#include "common/ViewConstants.slang"

layout (local_size_x = 64) in;

// Frustum planes (6 planes: left, right, top, bottom, near, far)
layout(binding = 16, std140) uniform FrustumPlanes
{
    vec4 planes[6];
};

// Input: All draw commands
struct DrawCommand
{
    uint indexCount;
    uint instanceCount;
    uint firstIndex;
    int baseVertex;
    uint baseInstance;
};

layout(binding = 13, std430) readonly buffer InputCommands
{
    DrawCommand inputCommands[];
};

// Input: Draw bounds (min.xyz, max.xyz)
struct DrawBounds
{
    vec3 min;
    float padding1;
    vec3 max;
    float padding2;
};

layout(binding = 14, std430) readonly buffer DrawBoundsBuffer
{
    DrawBounds drawBounds[];
};

// Output: Visible draw commands
layout(binding = 17, std430) writeonly buffer OutputCommands
{
    DrawCommand outputCommands[];
};

// Output: Count of visible draws
layout(binding = 15, std430) buffer DrawCount
{
    uint visibleCount;
};

bool testAABBFrustum(vec3 bmin, vec3 bmax)
{
    for (int i = 0; i < 6; ++i)
    {
        vec4 plane = planes[i];
        vec3 normal = plane.xyz;
        float dist = plane.w;
        
        // Get the closest vertex to the plane (matching C# Frustum.Intersects logic)
        vec3 closest;
        closest.x = normal.x < 0.0 ? bmin.x : bmax.x;
        closest.y = normal.y < 0.0 ? bmin.y : bmax.y;
        closest.z = normal.z < 0.0 ? bmin.z : bmax.z;
        
        // If closest vertex is outside, box is completely outside
        if (dot(normal, closest) + dist < 0.0)
        {
            return false;
        }
    }
    
    return true;
}

void main()
{
    uint drawId = gl_GlobalInvocationID.x;
    
    if (drawId >= inputCommands.length())
    {
        return;
    }
    
    DrawBounds bounds = drawBounds[drawId];
    
    if (testAABBFrustum(bounds.min, bounds.max))
    {
        uint outputIndex = atomicAdd(visibleCount, 1u);
        outputCommands[outputIndex] = inputCommands[drawId];
    }
}
