#version 460

layout (local_size_x = 1) in;

layout(binding = 7, std430) buffer OccludedBoundsDebugBuffer
{
    uint occludedCount;
    uint padding[3];
    uint indirectVertexCount;
    uint indirectInstanceCount;
    uint indirectFirst;
    uint indirectBaseInstance;
};

void main()
{
    // Each AABB is drawn as 24 line vertices (12 lines * 2 vertices per line)
    indirectVertexCount = occludedCount * 24u;
    indirectInstanceCount = 1u;
    indirectFirst = 0u;
    indirectBaseInstance = 0u;
}
