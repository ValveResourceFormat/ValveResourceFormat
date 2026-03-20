#version 460

// Compacts indirect draw requests by removing invalid draws.
// Each X lane processes one request, Y threads process draws in parallel.
// Requests are stored as pairs: [drawCount, startIndex].

struct DrawCommand
{
    uint indexCount;
    uint instanceCount;
    uint firstIndex;
    int baseVertex;
    uint baseInstance;
};

layout(std430, binding = 4) readonly buffer DrawCommands
{
    DrawCommand draws[];
};

layout(std430, binding = 8) buffer CompactDrawCommands
{
    DrawCommand compactDraws[];
};

layout(std430, binding = 9) buffer DrawCommandCounts
{
    uint compactDrawCounts[];
};

// Each request is a pair of uints: [drawCount, startIndex]
layout(std430, binding = 10) readonly buffer CompactionRequests
{
    uint compactRequests[];
};

shared uint g_GroupValidCounts[4];

layout (local_size_x = 4, local_size_y = 128, local_size_z = 1) in;
void main()
{
    uint requestIndex = gl_GlobalInvocationID.x;
    bool isActiveRequest = requestIndex < compactRequests.length() / 2;

    // Inactive lanes keep drawCount=0 so they harmlessly pass through barriers
    uint drawCount = 0;
    uint startIndex = 0;

    if (isActiveRequest)
    {
        drawCount = compactRequests[requestIndex * 2 + 0];
        startIndex = compactRequests[requestIndex * 2 + 1];
    }

    // Reset per-lane valid draw counter
    if (gl_LocalInvocationID.y == 0)
    {
        g_GroupValidCounts[gl_LocalInvocationID.x] = 0;
    }

    barrier();

    // Each Y thread copies valid draws to the output, claiming a slot via atomic increment
    for (uint i = gl_LocalInvocationID.y; i < drawCount; i += gl_WorkGroupSize.y)
    {
        DrawCommand cmd = draws[startIndex + i];

        if (cmd.indexCount != 0 && cmd.instanceCount != 0)
        {
            uint localOffset = atomicAdd(g_GroupValidCounts[gl_LocalInvocationID.x], 1);
            compactDraws[startIndex + localOffset] = cmd;
        }
    }

    barrier();

    // Write final compacted count for this request
    if (isActiveRequest && gl_LocalInvocationID.y == 0 && drawCount != 0)
    {
        compactDrawCounts[requestIndex] = g_GroupValidCounts[gl_LocalInvocationID.x];
    }
}
