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

layout (local_size_x = 4, local_size_y = 1, local_size_z = 1) in;
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
        // Passthrough mode: copy all commands without filtering
        for (uint i = 0; i < drawCount; i++)
        {
            compactDraws[outputOffset + i] = draws[startIndex + i];
        }
        compactDrawCounts[requestIndex] = drawCount;
        return;
    }

    // Process draws sequentially, compacting and merging in one pass
    DrawCommand mergedCmd;
    bool buildingMergedCmd = false;
    uint outputCount = 0;

    for (uint i = 0; i < drawCount; i++)
    {
        DrawCommand cmd = draws[startIndex + i];

        // Skip invalid draws
        if (cmd.indexCount == 0 || cmd.instanceCount == 0)
            continue;

        if (MERGE_MESHLETS && buildingMergedCmd)
        {
            // Try to merge with previous command
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
                // Can't merge, write out the previous command
                compactDraws[outputOffset + outputCount] = mergedCmd;
                outputCount++;
                buildingMergedCmd = false;
            }
        }

        // Start building a new command (or just pass through if not merging)
        if (MERGE_MESHLETS)
        {
            mergedCmd = cmd;
            buildingMergedCmd = true;
        }
        else
        {
            compactDraws[outputOffset + outputCount] = cmd;
            outputCount++;
        }
    }

    // Write out any remaining merged command
    if (buildingMergedCmd)
    {
        compactDraws[outputOffset + outputCount] = mergedCmd;
        outputCount++;
    }

    compactDrawCounts[requestIndex] = outputCount;
}
