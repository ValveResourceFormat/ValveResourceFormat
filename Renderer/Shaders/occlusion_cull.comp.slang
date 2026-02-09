#version 460

#include "common/ViewConstants.slang"
#include "common/utils.slang"

layout (local_size_x = 64) in;

// Frustum planes
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

// Draw bounds
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

// Depth pyramid (hierarchical depth buffer)
layout(binding = 0) uniform sampler2D g_tDepthPyramid;

// Check if camera is inside AABB
bool isCameraInsideAABB(vec3 bmin, vec3 bmax)
{
    vec3 cameraPos = g_vCameraPositionWs.xyz;
    return all(greaterThanEqual(cameraPos, bmin)) && all(lessThanEqual(cameraPos, bmax));
}

// Project AABB to screen space and test occlusion
bool testAABBOcclusion(vec3 bmin, vec3 bmax)
{
    // Get all 8 corners of the AABB
    vec3 corners[8];
    corners[0] = vec3(bmin.x, bmin.y, bmin.z);
    corners[1] = vec3(bmax.x, bmin.y, bmin.z);
    corners[2] = vec3(bmin.x, bmax.y, bmin.z);
    corners[3] = vec3(bmax.x, bmax.y, bmin.z);
    corners[4] = vec3(bmin.x, bmin.y, bmax.z);
    corners[5] = vec3(bmax.x, bmin.y, bmax.z);
    corners[6] = vec3(bmin.x, bmax.y, bmax.z);
    corners[7] = vec3(bmax.x, bmax.y, bmax.z);
    
    vec2 minXY = vec2(1.0);
    vec2 maxXY = vec2(0.0);
    float closestDepth = 0; // track closest point
    bool behindCamera = false;
    
    // Project all corners to clip space
    for (int i = 0; i < 8; i++)
    {
        vec4 clip = g_matWorldToProjection * vec4(corners[i], 1.0);
        
        // Skip corners behind camera
        if (clip.w <= 0.0)
        {
            behindCamera = true;
            continue;
        }
        
        // Perspective divide
        vec3 ndc = clip.xyz / clip.w;
        
        // Convert to UV coordinates [0, 1]
        vec2 uv = ndc.xy * 0.5 + 0.5;
        
        minXY = min(minXY, uv);
        maxXY = max(maxXY, uv);
        
        closestDepth = min(closestDepth, ndc.z);
    }
    
    // If any point is behind the camera, treat as visible
    if (behindCamera)
    {
        return false;
    }
    
    // Clamp to screen bounds
    minXY = clamp(minXY, vec2(0.0), vec2(1.0));
    maxXY = clamp(maxXY, vec2(0.0), vec2(1.0));
    
    // Calculate screen space size
    vec2 size = (maxXY - minXY) * vec2(textureSize(g_tDepthPyramid, 0));
    float maxSize = max(size.x, size.y);
    
    // Choose mip level based on screen size
    float mipLevel = max(0.0, ceil(log2(maxSize)));
    mipLevel = min(mipLevel, float(textureQueryLevels(g_tDepthPyramid) - 1));
    
    // Sample depth pyramid at the chosen mip level (contains furthest/max depth of occluders)
    vec2 sampleUV = (minXY + maxXY) * 0.5;
    float occluderDepth = textureLod(g_tDepthPyramid, sampleUV, mipLevel).r;

    closestDepth = closestDepth * 0.5 + 0.5;
    closestDepth = LinearRamp(g_flViewportMinZ, g_flViewportMaxZ, closestDepth);

    return closestDepth < occluderDepth;
}

void main()
{
    uint drawId = gl_GlobalInvocationID.x;
    
    if (drawId >= drawCommands.length())
    {
        return;
    }
    
    // Skip if already culled by frustum
    if (drawCommands[drawId].instanceCount == 0u)
    {
        return;
    }
    
    DrawBounds bounds = drawBounds[drawId];
    
    // Skip occlusion test if camera is inside the AABB
    if (isCameraInsideAABB(bounds.min, bounds.max))
    {
        //return; // Keep visible
    }
    
    // Test occlusion
    bool isOccluded = testAABBOcclusion(bounds.min, bounds.max);
    
    // Set instance count to 0 if occluded
    if (isOccluded && blink())
    {
        drawCommands[drawId].instanceCount = 0u;
    }
}
