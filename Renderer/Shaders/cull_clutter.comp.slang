#version 460

#include "common/structs.slang"

layout (local_size_x = 64) in;

struct ClutterInstance_t
{
    mat3x4 transform;
    vec3 center;
    float radius;
    uint indexInTile;
    uint tileInstanceCount;
    uint batch;
    uint padding;
};

struct ClutterBatch_t
{
    uint transformBase;
    uint firstCommand;
    uint commandCount;
    float endSize;
    float negativeHalfEndCullSize;
    float beginSize;
    uint padding0;
    uint padding1;
};

layout(std430, binding = 1) writeonly buffer TransformBuffer
{
    mat3x4 g_transformBuffer[];
};

layout(std430, binding = 6) readonly buffer ClutterInstances
{
    ClutterInstance_t instances[];
};

layout(std430, binding = 7) readonly buffer ClutterBatches
{
    ClutterBatch_t batches[];
};

layout(std430, binding = 8) buffer ClutterDrawCommands
{
    DrawCommand drawCommands[];
};

uniform vec3 g_vCameraPosition;
uniform float g_flProjectionScale; // 1 / tan(horizontal fov / 2)
uniform uint g_nInstanceCount;

uint HashUInt(uint value)
{
    uint hash = value + 374761393u;
    hash = 668265263u * ((hash << 17) | (hash >> 15));
    hash = 2246822519u * (hash ^ (hash >> 15));
    hash = 3266489917u * (hash ^ (hash >> 13));
    return hash ^ (hash >> 16);
}

void main()
{
    uint instanceIndex = gl_GlobalInvocationID.x;

    if (instanceIndex >= g_nInstanceCount)
    {
        return;
    }

    ClutterInstance_t instance = instances[instanceIndex];
    ClutterBatch_t batch = batches[instance.batch];

    float distance = length(instance.center - g_vCameraPosition);
    float screenSize = distance <= instance.radius ? 1.0 : clamp(instance.radius * g_flProjectionScale / distance, 0.0, 1.0);

    float hash = float(HashUInt(instance.indexInTile)) * 2.3283064e-10;
    float density = screenSize + hash * (float(instance.indexInTile) / float(instance.tileInstanceCount)) * batch.negativeHalfEndCullSize;

    if (density < batch.endSize)
    {
        return;
    }

    uint slot = atomicAdd(drawCommands[batch.firstCommand].instanceCount, 1u);

    for (uint command = 1u; command < batch.commandCount; command++)
    {
        atomicAdd(drawCommands[batch.firstCommand + command].instanceCount, 1u);
    }

    g_transformBuffer[batch.transformBase + slot] = instance.transform;

    // todo write instance alpha
}
