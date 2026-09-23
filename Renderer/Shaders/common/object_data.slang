#version 460

#ifndef VRF_OBJECT_DATA_INCLUDE
#define VRF_OBJECT_DATA_INCLUDE

#include "instancing_base.slang"

// The scene node a fragment belongs to, forwarded by the vertex stage
flat in uint g_nObjectIndex;

ObjectDataStandard GetObjectLightingData()
{
    return g_objectBuffer[g_nObjectIndex];
}

#endif
