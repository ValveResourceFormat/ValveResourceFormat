#version 460

#include "common/ViewConstants.slang"

layout (local_size_x = 64) in;

// Draw commands (read-write to modify instanceCount)
struct DrawCommand
{
    uint indexCount;
    uint instanceCount;
    uint firstIndex;
    int baseVertex;
    uint baseInstance;
};

// Input: Draw bounds (min.xyz, max.xyz)
struct DrawBounds
{
    vec3 min;
    uint padding1;
    vec3 max;
    uint padding2;
};

struct PackedAABB
{
    uint m_nMin;
    uint m_nMax;
};


struct MeshletCullInfo
{
    PackedAABB m_PackedAABB;
    uint m_PackedCone;
    uint m_nParentDrawBoundsIndex;
};

layout(binding = 4, std140) uniform FrustumPlanes
{
    vec4 planes[6]; // left, right, top, bottom, near, far
};

layout(binding = 4, std430) buffer DrawCommands
{
    DrawCommand drawCommands[];
};

layout(binding = 5, std430) readonly buffer DrawCallBounds
{
    DrawBounds drawBounds[];
};

layout(binding = 6, std430) readonly buffer MeshletInfoBuffer
{
    MeshletCullInfo meshlets[];
};


layout(binding = 7, std430) buffer OccludedBoundsDebugBuffer
{
    uint occludedCount;
    uint padding[3];
    DrawBounds occludedBounds[];
};

uniform bool g_bOcclusionDebugEnabled;

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

// Project AABB to screen space and test occlusion
bool testAABBOcclusion(vec3 bmin, vec3 bmax)
{
    // Offset camera forward to reduce false occlusions
    vec3 cameraOffset = g_vCameraDirWs * g_flDepthPyramidCameraOffset;

    // Project all 8 corners to clip space
    vec4 corners[8];
    for (int i = 0; i < 8; i++)
    {
        vec3 corner = vec3(
            (i & 1) != 0 ? bmax.x : bmin.x,
            (i & 2) != 0 ? bmax.y : bmin.y,
            (i & 4) != 0 ? bmax.z : bmin.z
        );

        corners[i] = g_matPrevWorldToProjection * vec4(corner, 1.0);
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

    // Less popping in when crossing geometry
    if (occluderDepth > 0.5)
    {
        // No occluder geometry, so not occluded
        return false;
    }

    // Apply depth range transformation to NDC Z to match depth buffer space
    // depthBuffer = ndcZ * (far - near) + near
    float aabbDepth = ndcMax.z * (g_flDepthRangeMax - g_flDepthRangeMin) + g_flDepthRangeMin;

    // Reverse Z: near=1, far=0. AABB occluded if its closest point is behind occluder
    // In reverse Z with depth range [0.05, 1.0]: higher values = closer
    return aabbDepth < (occluderDepth - 1e-5);
}

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

vec3 unpackAABB(vec3 min, vec3 max, uint packedVec)
{
    float x = float(packedVec & 0x3FF) / 1023.0;
    float y = float((packedVec >> 10) & 0x3FF) / 1023.0;
    float z = float((packedVec >> 20) & 0x3FF) / 1023.0;

    return min + vec3(x, y, z) * (max - min);
}

vec4 unpackSByteVector(uint packedData)
{
    // Extract each byte
    uint b0 = (packedData >> 0u) & 0xFFu;
    uint b1 = (packedData >> 8u) & 0xFFu;
    uint b2 = (packedData >> 16u) & 0xFFu;
    uint b3 = (packedData >> 24u) & 0xFFu;

    // Convert to signed bytes (range -128 to 127)
    int s0 = int(b0) - (b0 > 127u ? 256 : 0);
    int s1 = int(b1) - (b1 > 127u ? 256 : 0);
    int s2 = int(b2) - (b2 > 127u ? 256 : 0);
    int s3 = int(b3) - (b3 > 127u ? 256 : 0);

    return vec4(s0, s1, s2, s3) / 127.0;
}


void main()
{
    uint drawId = gl_GlobalInvocationID.x;
    
    if (drawId >= drawCommands.length())
    {
        return;
    }

    MeshletCullInfo meshlet = meshlets[drawId];

    uint boundIndex = meshlet.m_nParentDrawBoundsIndex;

    vec3 objectMin = drawBounds[boundIndex].min;
    vec3 objectMax = drawBounds[boundIndex].max;

    vec3 meshletMin = unpackAABB(objectMin, objectMax, meshlet.m_PackedAABB.m_nMin);
    vec3 meshletMax = unpackAABB(objectMin, objectMax, meshlet.m_PackedAABB.m_nMax);

    const bool FrustumCull = true;
    const bool BackfaceCull = true;

    bool visible = true;

    if (FrustumCull)
    {
        visible = testAABBFrustum(meshletMin, meshletMax);
    }

    if (visible && BackfaceCull)
    {
        vec4 coneCullInfo = unpackSByteVector(meshlet.m_PackedCone);

        vec3 meshletCenter = (meshletMin + meshletMax) * 0.5;
        vec3 meshletSize = meshletMax - meshletMin;

        vec3 camToMeshlet = meshletCenter - g_vCameraPositionWs;
        float boundingSphereRadius = length(meshletSize) * 0.5;

        visible = dot(camToMeshlet, coneCullInfo.xyz) < (coneCullInfo.w * length(camToMeshlet) + boundingSphereRadius);
    }

    if (visible && g_bOcclusionCullEnabled)
    {
        bool isOccluded = testAABBOcclusion(meshletMin, meshletMax);
        visible = isOccluded == false;

        // Store occluded bounds for debug visualization
        if (isOccluded && g_bOcclusionDebugEnabled)
        {
            uint index = atomicAdd(occludedCount, 1u);
            occludedBounds[index].min = meshletMin;
            occludedBounds[index].max = meshletMax;
        }
    }

    drawCommands[drawId].instanceCount = visible ? 1u : 0u;
}
