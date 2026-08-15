#version 460

#ifndef VRF_SHADOWMAPPING_INCLUDE
#define VRF_SHADOWMAPPING_INCLUDE

#include "ViewConstants.slang"

uniform sampler2DShadow g_tShadowDepthBufferDepth; // reserved

float CalculateSunShadowMapVisibility(vec3 vPosition)
{
    vec4 projCoords = g_matWorldToShadow * vec4(vPosition, 1.0);
    projCoords.xyz /= projCoords.w;

    vec2 shadowCoords = clamp(projCoords.xy * 0.5 + 0.5, vec2(-1), vec2(2));

    // Positive bias moves the reference toward the light so surfaces pass their own comparison
    float currentDepth = saturate(projCoords.z) + g_flSunShadowBias;

    // To skip PCF
    // float visibility = textureLod(g_tShadowDepthBufferDepth, vec3(shadowCoords, currentDepth), 0).r;

    float visibility = 0.0;
    vec2 texelSize = 1.0 / textureSize(g_tShadowDepthBufferDepth, 0);
    for(int x = -1; x <= 1; ++x)
    {
        for(int y = -1; y <= 1; ++y)
        {
            visibility += textureLod(g_tShadowDepthBufferDepth, vec3(shadowCoords + vec2(x, y) * texelSize, currentDepth), 0).r;
        }
    }

    visibility /= 9.0;

    vec2 distFromEdge = min(vec2(1.0) - abs(projCoords.xy), vec2(1.0));
    float edgeDistance = min(distFromEdge.x, distFromEdge.y);
    float edgeFadeOut = smoothstep(0.0, 0.08, edgeDistance);

    return mix(1.0, visibility, edgeFadeOut);
}

#endif
