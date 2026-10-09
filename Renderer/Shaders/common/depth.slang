#version 460

#ifndef VRF_DEPTH_INCLUDE
#define VRF_DEPTH_INCLUDE

#include "utils.slang"
#include "ViewConstants.slang"

uniform sampler2D g_tSceneDepth; // reserved

/// Reconstructs the world position a window space depth stands for.
/// flSceneDepth: the depth, as a depth buffer under the fragment holds it.
/// vCameraRay: any vector from the camera toward the fragment; only its direction matters, since the
/// division below rescales it to the given depth.
vec3 GetWorldPositionFromDepthValue(float flSceneDepth, vec3 vCameraRay)
{
    float flSceneDepthNormalized = RemapValClamped(flSceneDepth, g_flViewportMinZ, g_flViewportMaxZ, 0.0, 1.0);

    float invProjTerm = fma(flSceneDepthNormalized, g_vInvProjRow3.z, g_vInvProjRow3.w);

    // The ray is not the view direction, so its length along the view axis has to be divided out.
    float flPerspectiveCorrection = dot(g_vCameraDirWs, vCameraRay);

    return (g_vCameraPositionWs.xyz + (vCameraRay * (1.0 / (invProjTerm * flPerspectiveCorrection))));
}

/// A vector from the camera toward a pixel, for a pass that has no world position of its own to take one from.
/// vFragCoord: the pixel, in gl_FragCoord units.
vec3 GetCameraRay(vec2 vFragCoord)
{
    vec2 vNdc = vFragCoord * g_vInvViewportSize * 2.0 - 1.0;
    vec4 vViewRay = g_matProjectionToWorld * vec4(vNdc, 1.0, 1.0);

    return transpose(mat3(g_matWorldToView)) * (vViewRay.xyz / vViewRay.w);
}

/// Reconstructs the world position the scene depth holds under a pixel.
/// vScreenPosition: the pixel of the target being drawn, in gl_FragCoord units.
vec3 GetWorldPositionFromDepth(ivec2 vScreenPosition, vec3 vCameraRay)
{
    ivec2 vDepthTexel = ivec2(vec2(vScreenPosition) * g_vSceneDepthFetchScale);

    return GetWorldPositionFromDepthValue(texelFetch(g_tSceneDepth, vDepthTexel, 0).x, vCameraRay);
}

#endif
