#version 460

#ifndef VRF_LIGHT_COOKIE_INCLUDE
#define VRF_LIGHT_COOKIE_INCLUDE

#include "lighting_constants.slang"

uniform sampler3D g_tLightCookieVolume; // ClampToBorder, reserved

// The slice of the light cookie volume that a spot light projects through a square frustum around its outer cone,
// black outside of it
vec3 CalculateDynamicLightCookie(uint nLightIndex, vec3 vLightPositionWs, vec3 vPositionWs, float flCookieSlice)
{
    mat3 mLightAxes = mat3(g_matLightToWorld[nLightIndex]);
    vec3 vDelta = vPositionWs - vLightPositionWs;

    float flDepth = dot(vDelta, normalize(mLightAxes[0]));

    if (flDepth <= 0.0)
    {
        return vec3(0.0);
    }

    // The light's +Y axis points left, so the horizontal coordinate runs against it
    vec2 vSide = vec2(dot(vDelta, normalize(mLightAxes[1])), dot(vDelta, normalize(mLightAxes[2])));
    float flInvTanOuterAngle = g_vLightShadowParams[nLightIndex].z;
    vec2 vCookieUv = vSide * (-0.5 * flInvTanOuterAngle / flDepth) + 0.5;

    float flSliceCoordinate = (flCookieSlice + 0.5) / float(textureSize(g_tLightCookieVolume, 0).z);

    return textureLod(g_tLightCookieVolume, vec3(vCookieUv, flSliceCoordinate), 0.0).rgb;
}

#endif
