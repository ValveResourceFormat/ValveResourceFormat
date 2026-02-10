#version 460

#include "common/ViewConstants.slang"
#include "common/utils.slang"

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

// Depth pyramid (hierarchical depth buffer)
layout(binding = 0) uniform sampler2D g_tDepthPyramid;

// Depth pyramid dimensions
uniform int g_nDepthPyramidMaxMip;
uniform int g_nDepthPyramidWidth;
uniform int g_nDepthPyramidHeight;
uniform float g_flDepthPyramidCameraOffset;
uniform float g_flDepthRangeMin;
uniform float g_flDepthRangeMax;
uniform bool g_bOcclusionCullEnabled;

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

// Project AABB to screen space and test occlusion (Source 2 style)
bool testAABBOcclusion(vec3 bmin, vec3 bmax)
{
    // Offset camera forward to reduce false occlusions
    vec3 cameraOffset = g_vCameraDirWs * g_flDepthPyramidCameraOffset;

    // enlarge aabb
    //bmin -= vec3(20);
    //bmax += vec3(20);
    
    // Project all 8 corners to clip space
    vec4 corners[8];
    for (int i = 0; i < 8; i++)
    {
        vec3 corner = vec3(
            (i & 1) != 0 ? bmax.x : bmin.x,
            (i & 2) != 0 ? bmax.y : bmin.y,
            (i & 4) != 0 ? bmax.z : bmin.z
        );

        corners[i] = g_matWorldToProjection * vec4(corner, 1.0);
    }
    
    float minW = min(min(min(corners[0].w, corners[1].w), min(corners[2].w, corners[3].w)),
                     min(min(corners[4].w, corners[5].w), min(corners[6].w, corners[7].w)));

    // If any corner is behind the camera, the perspective divide produces
    // reflected NDC values making the screen-space bounds unreliable.
    // Conservatively mark as not occluded.
    if (minW <= 0.0)
    {
        return false;
    }
    
    vec3 ndc[8];
    for (int i = 0; i < 8; i++)
    {
        ndc[i] = corners[i].xyz / corners[i].w;
    }
    
    // Find min/max bounds in NDC space, clamp to [-1, 1]
    vec3 ndcMin = max(
        min(min(min(ndc[0], ndc[1]), min(ndc[2], ndc[3])), 
            min(min(ndc[4], ndc[5]), min(ndc[6], ndc[7]))),
        vec3(-1.0)
    );
    vec3 ndcMax = min(
        max(max(max(ndc[0], ndc[1]), max(ndc[2], ndc[3])), 
            max(max(ndc[4], ndc[5]), max(ndc[6], ndc[7]))),
        vec3(1.0)
    );
    
    // Convert to UV space [0, 1]
    vec2 uvMin = ndcMin.xy * 0.5 + 0.5;
    vec2 uvMax = ndcMax.xy * 0.5 + 0.5;
    vec2 uvSize = uvMax - uvMin;

     // Early out: degenerate rect, or beyond far plane
    if (uvSize.x * uvSize.y <= 0.0 || ndcMax.z >= 1.0)
    {
        return false;
    }
    
    // Convert to pixel coordinates
    vec2 pixelMin = uvMin * vec2(g_nDepthPyramidWidth, g_nDepthPyramidHeight);
    vec2 pixelMax = uvMax * vec2(g_nDepthPyramidWidth, g_nDepthPyramidHeight);
    vec2 pixelSize = ceil(pixelMax) - floor(pixelMin);
    
    // Calculate mip level based on coverage
    int mipLevel = min(g_nDepthPyramidMaxMip, int(ceil(log2(max(max(pixelSize.x, pixelSize.y), 1.0)))));
    
    // Convert pixel coords to texel coords at chosen mip level
    ivec2 texelMin = ivec2(pixelMin) >> mipLevel;
    ivec2 texelMax = ivec2(pixelMax) >> mipLevel;
    
    // If AABB covers only one texel, drop down one mip level for better accuracy
    if (all(equal(texelMin, texelMax)) && mipLevel > 0)
    {
        mipLevel--;
        texelMin = ivec2(pixelMin) >> mipLevel;
        texelMax = ivec2(pixelMax) >> mipLevel;
    }
    
    // Clamp to valid texel range at this mip level
    ivec2 mipSize = ivec2(max(1, g_nDepthPyramidWidth >> mipLevel), max(1, g_nDepthPyramidHeight >> mipLevel));
    ivec2 maxTexel = mipSize - ivec2(1);
    
    // If the AABB projects outside the viewport, it can't be occluded
    if (any(lessThan(texelMin, ivec2(0))) || any(greaterThan(texelMax, maxTexel)))
    {
        return false;
    }
    
    // Sample 4 corners using texelFetch
    float depth0 = texelFetch(g_tDepthPyramid, texelMin, mipLevel).r;
    float depth1 = texelFetch(g_tDepthPyramid, ivec2(texelMax.x, texelMin.y), mipLevel).r;
    float depth2 = texelFetch(g_tDepthPyramid, ivec2(texelMin.x, texelMax.y), mipLevel).r;
    float depth3 = texelFetch(g_tDepthPyramid, texelMax, mipLevel).r;
    
    // Find minimum depth (furthest in reverse Z)
    float occluderDepth = min(min(depth0, depth1), min(depth2, depth3));
    
    // Apply depth range transformation to NDC Z to match depth buffer space
    // depthBuffer = ndcZ * (far - near) + near
    float aabbDepth = ndcMax.z * (g_flDepthRangeMax - g_flDepthRangeMin) + g_flDepthRangeMin;
    
    // Reverse Z: near=1, far=0. AABB occluded if its closest point is behind occluder
    // In reverse Z with depth range [0.05, 1.0]: higher values = closer
    return aabbDepth < (occluderDepth - 1e-5);
}

// Source 2 style occlusion test with camera offset
bool testAABBOcclusion2(vec3 bmin, vec3 bmax)
{
    // Apply camera offset directly as vector addition (from GLSL line 186)
    const vec3 g_vDepthPyramidCameraOffset = g_vCameraDirWs * g_flDepthPyramidCameraOffset;

    vec3 offsetMin = bmin + g_vDepthPyramidCameraOffset;
    vec3 aabbSize = (bmax + g_vDepthPyramidCameraOffset) - offsetMin;
    
    // Build basis vectors for AABB edges (X, Y, Z axes)
    vec4 basisY = vec4(0.0, aabbSize.y, 0.0, 0.0) * g_matWorldToProjection;
    vec4 basisZ = vec4(0.0, 0.0, aabbSize.z, 0.0) * g_matWorldToProjection;
    vec4 basisX = vec4(aabbSize.x, 0.0, 0.0, 0.0) * g_matWorldToProjection;
    
    // Project base corner
    vec4 corners[8];
    corners[0] = g_matWorldToProjection * vec4(offsetMin, 1.0);
    corners[1] = corners[0] + basisZ;
    corners[2] = corners[0] + basisY;
    corners[3] = corners[2] + basisZ;
    corners[4] = corners[0] + basisX;
    corners[5] = corners[4] + basisZ;
    corners[6] = corners[4] + basisY;
    corners[7] = corners[6] + basisZ;
    
    // Find minimum W (check if behind camera)
    float minW = min(min(min(corners[0].w, corners[1].w), min(corners[2].w, corners[3].w)),
                     min(min(corners[4].w, corners[5].w), min(corners[6].w, corners[7].w)));
    
    vec3 ndc[8];
    for (int i = 0; i < 8; i++)
    {
        vec3 divided = corners[i].xyz / corners[i].w;
        ndc[i] = vec3(divided.x, divided.y, divided.z);
    }
    
    // Compute NDC bounds
    vec3 ndcMin = min(min(min(ndc[0], ndc[1]), min(ndc[2], ndc[3])), 
                      min(min(ndc[4], ndc[5]), min(ndc[6], ndc[7])));
    vec3 ndcMax = max(max(max(ndc[0], ndc[1]), max(ndc[2], ndc[3])), 
                      max(max(ndc[4], ndc[5]), max(ndc[6], ndc[7])));
    
    // Clamp to NDC space
    vec2 ndcMinClamped = max(ndcMin.xy, vec2(-1.0));
    vec2 ndcMaxClamped = min(ndcMax.xy, vec2(1.0));
    
    // Early rejection checks (GLSL lines 240-248)
    vec2 ndcSize = ndcMaxClamped - ndcMinClamped;
    if (minW < g_flDepthRangeMin || ndcMax.z >= 1.0 || ndcSize.x * ndcSize.y <= 0.0)
    {
        return false;
    }
    
    // Convert NDC to UV space [0,1]
    vec2 uvMin = ndcMinClamped * 0.5 + 0.5;
    vec2 uvMax = ndcMaxClamped * 0.5 + 0.5;
    
    // Convert to pixel coordinates using actual pyramid dimensions
    vec2 pixelMin = uvMin * vec2(g_nDepthPyramidWidth, g_nDepthPyramidHeight);
    vec2 pixelMax = uvMax * vec2(g_nDepthPyramidWidth, g_nDepthPyramidHeight);
    vec2 pixelSize = ceil(pixelMax) - floor(pixelMin);
    
    // Calculate mip level
    int mipLevel = min(g_nDepthPyramidMaxMip, int(ceil(log2(max(max(pixelSize.x, pixelSize.y), 1.0)))));
    
    // Convert to texel coordinates at selected mip
    ivec2 texelMin = ivec2(pixelMin) >> mipLevel;
    ivec2 texelMax = ivec2(pixelMax) >> mipLevel;
    
    // If single texel, drop one mip for accuracy
    if (all(equal(texelMin, texelMax)) && mipLevel > 0)
    {
        mipLevel--;
        texelMin = ivec2(pixelMin) >> mipLevel;
        texelMax = ivec2(pixelMax) >> mipLevel;
    }
    
    // Clamp to valid texel range at this mip level
    ivec2 mipSize = ivec2(max(1, g_nDepthPyramidWidth >> mipLevel), max(1, g_nDepthPyramidHeight >> mipLevel));
    ivec2 maxTexel = mipSize - ivec2(1);
    
    // If AABB projects outside the viewport, it can't be occluded
    if (any(lessThan(texelMin, ivec2(0))) || any(greaterThan(texelMax, maxTexel)))
    {
        return false;
    }
    
    // Sample 4 corner texels
    float depth0 = texelFetch(g_tDepthPyramid, texelMin, mipLevel).r;
    float depth1 = texelFetch(g_tDepthPyramid, ivec2(texelMax.x, texelMin.y), mipLevel).r;
    float depth2 = texelFetch(g_tDepthPyramid, ivec2(texelMin.x, texelMax.y), mipLevel).r;
    float depth3 = texelFetch(g_tDepthPyramid, texelMax, mipLevel).r;
    
    // Find minimum depth (furthest in reverse-Z)
    float occluderDepth = min(min(depth0, depth1), min(depth2, depth3));
    
    // Apply depth range transformation (same as testAABBOcclusion)
    float aabbDepth = ndcMax.z * (g_flDepthRangeMax - g_flDepthRangeMin) + g_flDepthRangeMin;
    
    return aabbDepth < (occluderDepth - 1e-5);
}

void main()
{
    uint drawId = gl_GlobalInvocationID.x;
    
    if (drawId >= drawCommands.length())
    {
        return;
    }
    
    DrawBounds bounds = drawBounds[drawId];
    
    // First, test frustum culling
    bool isMeshInFrustum = testAABBFrustum(bounds.min, bounds.max);
    
    // If frustum cull failed, mark as culled
    if (!isMeshInFrustum)
    {
        drawCommands[drawId].instanceCount = 0u;
        return;
    }
    
    // If occlusion culling is enabled, test it
    if (g_bOcclusionCullEnabled)
    {
        bool isOccluded = false // blink() 
            ? testAABBOcclusion2(bounds.min, bounds.max)
            : testAABBOcclusion(bounds.min, bounds.max);
        if (isOccluded)
        {
            drawCommands[drawId].instanceCount = 0u;
            return;
        }
    }

    drawCommands[drawId].instanceCount = 1u;
}
