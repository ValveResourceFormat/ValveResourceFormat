#version 460

// Compacts indirect draw requests

layout (local_size_x = 4, local_size_y = 16, local_size_z = 1) in;

// Draw commands
struct DrawCommand
{
    uint indexCount;
    uint instanceCount;
    uint firstIndex;
    int baseVertex;
    uint baseInstance;
};

// Each request packs:
// low 8 bits  = drawCount
// high 24 bits = startIndex into input draw array
layout(std430, binding = 30) readonly buffer CompactionRequests
{
    uint data[];
} g_CompactionRequests;

layout(std430, binding = 31) readonly buffer InputDraws
{
    DrawCommand draws[];
} g_InputDraws;

layout(std430, binding = 158) buffer OutputDraws
{
    DrawCommand draws[];
} g_OutputDraws;

layout(std430, binding = 159) buffer OutputCounts
{
    uint counts[];
} g_OutputCounts;

shared uint g_GroupValidCounts[4]; // one counter per X lane

void main()
{
    uvec3 groupThreadID = gl_LocalInvocationID;
    uvec3 dispatchThreadID = gl_GlobalInvocationID;

    uint requestIndex = dispatchThreadID.x;

    // Each request is 8 bytes (2 uints)
    uint packed = g_CompactionRequests.data[requestIndex * 2 + 0];
    uint outputOffset = g_CompactionRequests.data[requestIndex * 2 + 1];

    uint drawCount  = packed & 0xFF;      // low 8 bits
    uint startIndex = packed >> 8;        // high bits

    // One counter per X thread
    if (groupThreadID.y == 0)
    {
        g_GroupValidCounts[groupThreadID.x] = 0;
    }

    barrier();

    // Each Y thread processes draws in a strided fashion (stride = 16)
    for (uint i = groupThreadID.y; i < drawCount; i += 16)
    {
        uint drawIndex = startIndex + i;
        DrawCommand cmd = g_InputDraws.draws[drawIndex];

        // Valid draw = indexCount * instanceCount != 0
        if (cmd.indexCount != 0 && cmd.instanceCount != 0)
        {
            uint localOffset = atomicAdd(g_GroupValidCounts[groupThreadID.x], 1);

            uint dstIndex = outputOffset + localOffset;
            g_OutputDraws.draws[dstIndex] = cmd;
        }
    }

    barrier();

    // One thread per X lane writes final compacted count
    if (groupThreadID.y == 0 && drawCount != 0)
    {
        g_OutputCounts.counts[outputOffset] = g_GroupValidCounts[groupThreadID.x];
    }
}
