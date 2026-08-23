#version 460

#ifndef VRF_SCENE_DEPTH_INCLUDE
#define VRF_SCENE_DEPTH_INCLUDE

#include "ViewConstants.slang"
#include "utils.slang"

uniform sampler2D g_tSceneDepth; // reserved

// View depth of the opaque surface at a pixel
float GetSceneViewDepth(ivec2 pixel)
{
    float sceneDepth = texelFetch(g_tSceneDepth, pixel, 0).x;
    float depthNormalized = RemapValClamped(sceneDepth, g_flViewportMinZ, g_flViewportMaxZ, 0.0, 1.0);

    return 1.0 / fma(depthNormalized, g_vInvProjRow3.z, g_vInvProjRow3.w);
}

#endif
