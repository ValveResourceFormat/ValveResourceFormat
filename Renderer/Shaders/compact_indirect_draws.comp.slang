#version 460

// Compacts indirect draw requests by removing invalid draws and merging consecutive compatible draws

const bool PASSTHROUGH = false;
const bool MERGE_MESHLETS = false;

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

    // Step 1: Compact by removing invalid draws
    // Each Y thread processes draws in a strided fashion (stride = 16)
    if (PASSTHROUGH)
    {
        // Passthrough mode: copy all commands without filtering
        for (uint i = groupThreadID.y; i < drawCount; i += 16)
        {
            uint drawIndex = startIndex + i;
            uint dstIndex = outputOffset + i;
            compactDraws[dstIndex] = draws[drawIndex];
        }
    }
    else
    {
        for (uint i = groupThreadID.y; i < drawCount; i += 16)
        {
            uint drawIndex = startIndex + i;
            DrawCommand cmd = draws[drawIndex];

            // Valid draw = indexCount * instanceCount != 0
            if (cmd.indexCount != 0 && cmd.instanceCount != 0)
            {
                uint localOffset = atomicAdd(g_GroupValidCounts[groupThreadID.x], 1);
                uint dstIndex = outputOffset + localOffset;
                compactDraws[dstIndex] = cmd;
            }
        }
    }

    barrier();
    
    if (MERGE_MESHLETS)
    {
        memoryBarrierBuffer(); // Ensure compacted draws are visible to merge step
    }

    // Step 2: Merge consecutive compatible draws (single thread per X lane)
    if (groupThreadID.y == 0 && drawCount != 0)
    {
        uint compactedCount = PASSTHROUGH ? drawCount : g_GroupValidCounts[groupThreadID.x];

        if (MERGE_MESHLETS && !PASSTHROUGH && compactedCount > 0)
        {
            // Read back the compacted draws and merge them
            DrawCommand mergedCmd;
            bool buildingMergedCmd = false;
            uint outputCount = 0;

            for (uint i = 0; i < compactedCount; i++)
            {
                uint compactedIndex = outputOffset + i;
                DrawCommand cmd = compactDraws[compactedIndex];

                // Sanity check - skip invalid commands that somehow got through
                if (cmd.indexCount == 0 || cmd.instanceCount == 0)
                    continue;

                if (buildingMergedCmd)
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
                        uint writeIndex = outputOffset + outputCount;
                        compactDraws[writeIndex] = mergedCmd;
                        outputCount++;
                        buildingMergedCmd = false;
                    }
                }

                // Start a new merged command
                mergedCmd = cmd;
                buildingMergedCmd = true;
            }

            // Write out any remaining merged command
            if (buildingMergedCmd)
            {
                uint writeIndex = outputOffset + outputCount;
                compactDraws[writeIndex] = mergedCmd;
                outputCount++;
            }

            compactDrawCounts[requestIndex] = outputCount;
        }
        else
        {
            compactDrawCounts[requestIndex] = compactedCount;
        }
    }
}
