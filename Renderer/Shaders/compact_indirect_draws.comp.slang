#version 460

// Compacts indirect draw requests by removing invalid draws and merging consecutive compatible draws

const bool REMOVE_CULLED_MESHLETS = true;
const bool MERGE_MESHLETS = true;

// Draw commands
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

// Each request packs:
// low 8 bits  = drawCount
// high 24 bits = startIndex into input draw array
layout(std430, binding = 10) readonly buffer CompactionRequests
{
    uint compactRequests[];
};

const uint STRIDE = 128;
shared uint g_GroupValidCounts[4]; // one counter per X lane

layout (local_size_x = 4, local_size_y = STRIDE, local_size_z = 1) in;
void main()
{
    uint requestIndex = gl_GlobalInvocationID.x;

    if (requestIndex >= compactRequests.length() / 2)
        return;

    uint drawCount = compactRequests[requestIndex * 2 + 0];
    uint startIndex = compactRequests[requestIndex * 2 + 1];
    uint outputOffset = startIndex;

    if (REMOVE_CULLED_MESHLETS == false)
    {
        // Fast path: Just copy draws without compaction
        for (uint i = gl_LocalInvocationID.y; i < drawCount; i += STRIDE)
        {
            uint drawIndex = startIndex + i;
            if (drawIndex >= startIndex + drawCount)
                break;

            compactDraws[outputOffset + i] = draws[drawIndex];
        }

        if (gl_LocalInvocationID.y == 0)
        {
            compactDrawCounts[requestIndex] = drawCount;
        }
        return;
    }

     // One counter per X thread
    if (gl_LocalInvocationID.y == 0)
    {
        g_GroupValidCounts[gl_LocalInvocationID.x] = 0;
    }

    barrier();

    // Step 1: Compact by removing invalid draws
    // Each Y thread processes draws in a strided fashion

    for (uint i = gl_LocalInvocationID.y; i < drawCount; i += STRIDE)
    {
        uint drawIndex = startIndex + i;
        
        if (drawIndex >= startIndex + drawCount)
            break;

        DrawCommand cmd = draws[drawIndex];

        // Valid draw = indexCount * instanceCount != 0
        if (cmd.indexCount != 0 && cmd.instanceCount != 0)
        {
            uint localOffset = atomicAdd(g_GroupValidCounts[gl_LocalInvocationID.x], 1);
            uint dstIndex = outputOffset + localOffset;
            compactDraws[dstIndex] = cmd;
        }
    }

    barrier();
    memoryBarrierBuffer();

    // One thread sums per-X-lane counts and writes final compacted count
    if (gl_LocalInvocationID.y == 0 && drawCount != 0)
    {
        uint sum = g_GroupValidCounts[0] + g_GroupValidCounts[1] + g_GroupValidCounts[2] + g_GroupValidCounts[3];
        compactDrawCounts[requestIndex] = sum;
    }
}
