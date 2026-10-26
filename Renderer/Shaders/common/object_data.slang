#version 460

#ifndef VRF_OBJECT_DATA_INCLUDE
#define VRF_OBJECT_DATA_INCLUDE

#include "instancing_base.slang"

// The scene node a fragment belongs to, forwarded by the vertex stage. A vertex stage that lights
// through these includes defines VRF_VERTEX_STAGE first, and writes the index itself.
#ifdef VRF_VERTEX_STAGE
flat out uint g_nObjectIndex;
#else
flat in uint g_nObjectIndex;
#endif

ObjectDataStandard GetObjectLightingData()
{
    return g_objectBuffer[g_nObjectIndex];
}

#endif
