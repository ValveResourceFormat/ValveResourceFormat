#version 460

layout(local_size_x = 64) in;

struct DrawElementsIndirectCommand
{
    uint count;
    uint instanceCount;
    uint firstIndex;
    int baseVertex;
    uint baseInstance;
};

layout(std430, binding = 0) readonly buffer SourceDraws
{
    DrawElementsIndirectCommand g_sourceDraws[];
};

layout(std430, binding = 1) writeonly buffer CompactedDraws
{
    DrawElementsIndirectCommand g_compactedDraws[];
};

// Atomic counter to track number of valid draws (increment by shader)
layout(std430, binding = 2) buffer DrawCounter
{
    uint g_compactedDrawCount;
};

void main()
{
    // First thread resets counter
    if (gl_LocalInvocationIndex == 0)
    {
        g_compactedDrawCount = 0;
    }
    
    barrier();
    
    uint sourceIndex = gl_GlobalInvocationID.x;
    
    // Read source draw command
    DrawElementsIndirectCommand draw = g_sourceDraws[sourceIndex];
    
    // Only write draws with non-zero instance count
    if (draw.instanceCount > 0)
    {
        // Atomically allocate space in compacted buffer and get output index
        uint compactedIndex = atomicAdd(g_compactedDrawCount, 1);
        g_compactedDraws[compactedIndex] = draw;
    }
}
