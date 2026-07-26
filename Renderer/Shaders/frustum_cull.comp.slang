#version 460

#include "common/ViewConstants.slang"
#include "common/structs.slang"
#include "common/gpu_cull.slang"

layout (local_size_x = 64) in;

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
    uint indirectVertexCount;
    uint indirectInstanceCount;
    uint indirectFirst;
    uint indirectBaseInstance;
    DrawBounds occludedBounds[];
};

uniform bool g_bOcclusionDebugEnabled;

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
