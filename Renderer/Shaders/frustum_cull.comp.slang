#version 460

#include "common/ViewConstants.slang"

layout (local_size_x = 64) in;

// Frustum planes (6 planes: left, right, top, bottom, near, far)
layout(binding = 16, std140) uniform FrustumPlanes
{
    vec4 planes[6];
};

// Draw commands (read-write to modify instanceCount)
struct DrawCommand
{
    uint indexCount;
    uint instanceCount;
    uint firstIndex;
    int baseVertex;
    uint baseInstance;
};

layout(binding = 13, std430) buffer DrawCommands
{
    DrawCommand drawCommands[];
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
    
    if (drawId >= drawCommands.length())
    {
        return;
    }
    
    DrawBounds bounds = drawBounds[drawId];
    bool isMeshInFrustum = testAABBFrustum(bounds.min, bounds.max);
    
    drawCommands[drawId].instanceCount = isMeshInFrustum ? 1u : 0u;
}
