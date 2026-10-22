#version 460

#ifndef VRF_ENVMAP_DATA_INCLUDE
#define VRF_ENVMAP_DATA_INCLUDE

#include "features.slang"
#include "utils.slang"

struct EnvMapData
{
    mat4 WorldToLocal;
    vec3 BoxMins;
    uint ArrayIndex;
    vec3 BoxMaxs; // A sphere's radius on every axis
    uint _padding1;
    vec4 InvEdgeWidth; // A sphere's on every axis, from its single fade distance
    vec3 Origin;
    uint ProjectionType; // 0 sphere, 1 box
    vec3 Color;
    int AssociatedLPV; // The light probe volume sharing its handshake, -1 when none
    vec4 NormalizationSH;
};

// Maximum number of environment maps supported
#define MAX_ENVMAPS 320

layout(std140, binding = 2) uniform EnvMapArray
{
    EnvMapData envMaps[MAX_ENVMAPS];
};

// How far inside its volume a position is, 0 outside and rising to 1 over the edge fade distances
float GetEnvMapEdgeFade(EnvMapData envData, vec3 positionWs, vec3 envMapLocalPos)
{
    #if (ENVMAP_SPHERES)
        if (envData.ProjectionType == 0u)
        {
            // Spheres fade over a single distance inside their radius
            float distanceToEdge = length(positionWs - envData.Origin) - envData.BoxMaxs.x;
            return 1.0 - saturate(distanceToEdge * envData.InvEdgeWidth.x + 1.0);
        }
    #endif

    vec3 fadeFromMin = saturate((envMapLocalPos - envData.BoxMins) * envData.InvEdgeWidth.xyz);
    vec3 fadeFromMax = saturate((envData.BoxMaxs - envMapLocalPos) * envData.InvEdgeWidth.xyz);

    return min(min3(fadeFromMin), min3(fadeFromMax));
}

#endif
