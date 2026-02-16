#version 460

// Compacts indirect draw requests

const bool PASSTHROUGH = false;
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

shared uint g_GroupValidCounts[4]; // one counter per X lane

layout (local_size_x = 4, local_size_y = 16, local_size_z = 1) in;
void main()
{
    uvec3 groupThreadID = gl_LocalInvocationID;
    uvec3 dispatchThreadID = gl_GlobalInvocationID;

    uint requestIndex = dispatchThreadID.x;

    if (requestIndex >= compactRequests.length() / 2)
        return;

    uint drawCount = compactRequests[requestIndex * 2 + 0];
    uint startIndex = compactRequests[requestIndex * 2 + 1];
    uint outputOffset = startIndex;

     // One counter per X thread
    if (groupThreadID.y == 0)
    {
        g_GroupValidCounts[groupThreadID.x] = 0;
    }

    barrier();

    // Each Y thread processes draws in a strided fashion (stride = 16)
    DrawCommand mergedCmd;
    bool hasMergedCmd = false;
    uint mergedDstIndex = 0;

    for (uint i = groupThreadID.y; i < drawCount; i += 16)
    {
        uint drawIndex = startIndex + i;
        DrawCommand cmd = draws[drawIndex];

        if (PASSTHROUGH)
        {
            // Passthrough mode: copy all commands without filtering
            uint dstIndex = outputOffset + i;
            compactDraws[dstIndex] = cmd;
        }
        else
        {
            // Valid draw = indexCount * instanceCount != 0
            if (cmd.indexCount != 0 && cmd.instanceCount != 0)
            {
                if (MERGE_MESHLETS && hasMergedCmd)
                {
                    // Check if we can merge with previous draw
                    uint expectedFirstIndex = mergedCmd.firstIndex + mergedCmd.indexCount;
                    if (cmd.baseInstance == mergedCmd.baseInstance &&
                        cmd.baseVertex == mergedCmd.baseVertex &&
                        cmd.instanceCount == mergedCmd.instanceCount &&
                        cmd.firstIndex == expectedFirstIndex)
                    {
                        // Merge by extending the index count
                        mergedCmd.indexCount += cmd.indexCount;
                        continue;
                    }
                    else
                    {
                        // Write out the previous merged command
                        compactDraws[mergedDstIndex] = mergedCmd;
                        hasMergedCmd = false;
                    }
                }

                // Start a new merged command
                uint localOffset = atomicAdd(g_GroupValidCounts[groupThreadID.x], 1);
                mergedDstIndex = outputOffset + localOffset;
                mergedCmd = cmd;
                hasMergedCmd = true;
            }
        }
    }

    // Write out any remaining merged command
    if (hasMergedCmd)
    {
        compactDraws[mergedDstIndex] = mergedCmd;
    }

    barrier();

    // One thread per X lane writes final compacted count
    if (groupThreadID.y == 0 && drawCount != 0)
    {
        compactDrawCounts[requestIndex] = PASSTHROUGH ? drawCount : g_GroupValidCounts[groupThreadID.x];
    }
}
