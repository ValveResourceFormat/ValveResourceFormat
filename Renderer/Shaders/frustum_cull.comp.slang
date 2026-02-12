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
    float padding1;
    vec3 max;
    float padding2;
};

struct PackedAABB
{
    uint m_nMin;
    uint m_nMax;
};


//For this shader we kinda only care about the packed AABB and the culling info
struct MeshletInfo
{
    PackedAABB m_PackedAABB;
    int packedCullingInfo; // unpack for vec3 and angle

    int m_nVertexOffset;
    int m_nTriangleOffset;
    uint m_nVertexCount;
    uint m_nTriangleCount;
    uint m_nParentDrawBoundsIndex;
};

layout(binding = 8, std140) uniform FrustumPlanes
{
    vec4 planes[6]; // left, right, top, bottom, near, far
};

layout(binding = 13, std430) buffer DrawCommands
{
    DrawCommand drawCommands[];
};

layout(binding = 14, std430) readonly buffer DrawCallBounds
{
    DrawBounds drawBounds[];
};

layout(binding = 15, std430) readonly buffer MeshletInfoBuffer
{
    MeshletInfo meshlets[];
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

    MeshletInfo meshlet = meshlets[drawId];

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
        vec4 coneCullInfo = unpackSByteVector(meshlet.packedCullingInfo);

        vec3 meshletCenter = (meshletMin + meshletMax) * 0.5;
        vec3 meshletSize = meshletMax - meshletMin;

        vec3 camToMeshlet = meshletCenter - g_vCameraPositionWs;
        float boundingSphereRadius = length(meshletSize) * 0.5;

        visible = dot(camToMeshlet, coneCullInfo.xyz) < (coneCullInfo.w * length(camToMeshlet) + boundingSphereRadius);
    }

    drawCommands[drawId].instanceCount = visible ? 1u : 0u;
}
