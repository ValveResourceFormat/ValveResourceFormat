#version 460

#ifndef VRF_SHADOWMAPPING_INCLUDE
#define VRF_SHADOWMAPPING_INCLUDE

#include "ViewConstants.slang"

#define SUN_CASCADE_COUNT 2

uniform sampler2DArrayShadow g_tShadowDepthBufferDepth; // reserved

float SampleSunCascadePCF(vec3 projCoords, int cascade)
{
    vec2 shadowCoords = clamp(projCoords.xy * 0.5 + 0.5, vec2(-1), vec2(2));

    // Positive bias moves the reference toward the light so surfaces pass their own comparison
    float currentDepth = saturate(projCoords.z) + g_flSunShadowBias;

    float visibility = 0.0;
    vec2 texelSize = 1.0 / vec2(textureSize(g_tShadowDepthBufferDepth, 0).xy);
    for (int x = -1; x <= 1; ++x)
    {
        for (int y = -1; y <= 1; ++y)
        {
            // Zero gradients pin the lod, like textureLod, which shadow array samplers do not offer
            visibility += textureGrad(g_tShadowDepthBufferDepth,
                vec4(shadowCoords + vec2(x, y) * texelSize, float(cascade), currentDepth), vec2(0.0), vec2(0.0));
        }
    }

    return visibility / 9.0;
}

float CalculateSunShadowMapVisibility(vec3 vPosition)
{
    for (int cascade = 0; cascade < SUN_CASCADE_COUNT; cascade++)
    {
        vec4 projCoordsW = g_matWorldToShadow[cascade] * vec4(vPosition, 1.0);
        vec3 projCoords = projCoordsW.xyz / projCoordsW.w;

        vec2 distFromEdge = vec2(1.0) - abs(projCoords.xy);
        float edgeFade = smoothstep(0.0, 0.08, min(distFromEdge.x, distFromEdge.y));

        if (edgeFade <= 0.0)
        {
            continue;
        }

        float visibility = SampleSunCascadePCF(projCoords, cascade);

        if (edgeFade >= 1.0)
        {
            return visibility;
        }

        // Inside the edge band, hand over to the next cascade out, or to fully lit past the last one
        float handover = 1.0;

        if (cascade + 1 < SUN_CASCADE_COUNT)
        {
            vec4 outerCoordsW = g_matWorldToShadow[cascade + 1] * vec4(vPosition, 1.0);
            handover = SampleSunCascadePCF(outerCoordsW.xyz / outerCoordsW.w, cascade + 1);
        }

        return mix(handover, visibility, edgeFade);
    }

    return 1.0;
}

// Should be a NoCmp depth read; the comparison sampler only answers "is the occluder nearer than this?".
float CalculateSunShadowThickness(vec3 vPosition)
{
    vec4 projCoordsW = g_matWorldToShadow[0] * vec4(vPosition, 1.0);
    vec3 projCoords = projCoordsW.xyz / projCoordsW.w;

    vec2 shadowCoords = clamp(projCoords.xy * 0.5 + 0.5, vec2(-1), vec2(2));
    float currentDepth = saturate(projCoords.z) + g_flSunShadowBias;

    const int nThicknessSteps = 4;
    const float flThicknessStep = 0.002;

    float flThickness = 0.0;

    for (int i = 1; i <= nThicknessSteps; ++i)
    {
        float flReference = currentDepth - flThicknessStep * float(i);
        flThickness += textureGrad(g_tShadowDepthBufferDepth,
            vec4(shadowCoords, 0.0, flReference), vec2(0.0), vec2(0.0));
    }

    return flThickness / float(nThicknessSteps);
}

#endif
