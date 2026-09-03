#version 460

#ifndef VRF_DEPTH_INCLUDE
#define VRF_DEPTH_INCLUDE

#include "utils.slang"
#include "ViewConstants.slang"

uniform sampler2D g_tSceneDepth; // reserved

/// Reconstructs the world position the depth buffer holds under a pixel.
/// vScreenPosition: the pixel to read, in gl_FragCoord units.
/// vCameraRay: any vector from the camera toward the fragment; only its direction matters, since the
/// division below rescales it to the depth the buffer actually holds.
vec3 GetWorldPositionFromDepth(ivec2 vScreenPosition, vec3 vCameraRay)
{
    float flSceneDepth = texelFetch(g_tSceneDepth, vScreenPosition, 0).x;
    float flSceneDepthNormalized = RemapValClamped(flSceneDepth, g_flViewportMinZ, g_flViewportMaxZ, 0.0, 1.0);

    float invProjTerm = fma(flSceneDepthNormalized, g_vInvProjRow3.z, g_vInvProjRow3.w);

    // The ray is not the view direction, so its length along the view axis has to be divided out.
    float flPerspectiveCorrection = dot(g_vCameraDirWs, vCameraRay);

    return (g_vCameraPositionWs.xyz + (vCameraRay * (1.0 / (invProjTerm * flPerspectiveCorrection))));
}

#endif
