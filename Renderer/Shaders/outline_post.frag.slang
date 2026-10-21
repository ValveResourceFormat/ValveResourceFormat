#version 460

#include "common/utils.slang"

uniform bool g_bFlipY = false;
uniform int g_nNumSamplesMSAA = 1;

uniform float g_flOutlineSize = 1;
uniform float g_flOutlineSoftness = 0.8;
uniform float g_flOutlineIntensity = 2.25;

// Where the selection is seen, and where the scene hides it
uniform vec4 g_vOutlineColor;
uniform vec4 g_vOccludedColor;

// Red: the selection wherever it is. Green: where the scene does not hide it.
layout (binding = 0) uniform sampler2DMS g_tOutlineMask;

out vec4 outputColor;

// Adds how many samples of a pixel the selection covers, and how many of those the scene does not hide
void SampleOutlineMaskMSAA(ivec2 pixelCoords, inout vec2 coverage, inout int totalSamples)
{
    for (int i = 0; i < g_nNumSamplesMSAA; i++)
    {
        coverage += texelFetch(g_tOutlineMask, pixelCoords, i).rg;
    }

    totalSamples += g_nNumSamplesMSAA;
}

vec4 DrawOutline()
{
    // base kernel width (scaled by g_flOutlineSize)
    int width = max(1, int(floor(2.5 * g_flOutlineSize + 0.5)));
    const int shadingRate = 1;

    ivec2 maskTextureSize = textureSize(g_tOutlineMask);
    ivec2 pixelCoords = ivec2(gl_FragCoord.xy);

    if (g_bFlipY)
    {
        pixelCoords.y = maskTextureSize.y - pixelCoords.y - 1;
    }

    int totalSamples = 0;
    vec2 coverageCenter = vec2(0.0);

    SampleOutlineMaskMSAA(pixelCoords, coverageCenter, totalSamples);

    if (coverageCenter.r >= float(g_nNumSamplesMSAA))
    {
        // do not draw outline inside the object
        discard;
    }

    vec2 coverage = coverageCenter;

    // Accumulate samples in a neighborhood, and how far away the nearest inside sample is
    float minDistSq = 1e9;

    for (int x = -width; x <= width; x += shadingRate)
    {
        for (int y = -width; y <= width; y += shadingRate)
        {
            ivec2 sampleCoords = pixelCoords + ivec2(x, y);

            if (sampleCoords.x < 0 || sampleCoords.x >= maskTextureSize.x ||
                sampleCoords.y < 0 || sampleCoords.y >= maskTextureSize.y)
            {
                continue;
            }

            vec2 beforeCoverage = coverage;
            SampleOutlineMaskMSAA(sampleCoords, coverage, totalSamples);

            // Derive per-pixel coverage from the accumulator
            if (coverage.r > beforeCoverage.r)
            {
                vec2 d = vec2(pixelCoords) - vec2(sampleCoords);
                minDistSq = min(minDistSq, length_squared(d));
            }
        }
    }

    if (totalSamples == 0)
    {
        return vec4(0.0);
    }

    float covNeighborhood = coverage.r / float(totalSamples); // 0..1 coverage in neighborhood
    float covCenter = coverageCenter.r / float(g_nNumSamplesMSAA); // per-fragment coverage (0..1)

    // If there's almost no coverage nearby, skip outline
    if (covNeighborhood < 0.01)
    {
        discard;
    }

    // Edge strength: how much of the kernel is inside while the center is not.
    float edgeStrength = saturate(covNeighborhood - covCenter);
    // widen the smoothstep so weaker edges still contribute
    edgeStrength = smoothstep(0.001, 0.85 * g_flOutlineSoftness, edgeStrength);

    // Compute a smooth exterior falloff based on distance (in pixels) to the nearest inside sample
    float distPixels = sqrt(minDistSq);
    float fadeRadius = max(1.0, float(width)) * (2.6 * g_flOutlineSize) * max(0.6, g_flOutlineSoftness);
    float exteriorFade = saturate(1.0 - smoothstep(0.0, fadeRadius, distPixels));
    float outlineAlpha = edgeStrength * exteriorFade * g_flOutlineIntensity;
    outlineAlpha = pow(outlineAlpha, 0.75);

    // Ensure a small visible floor so very thin silhouettes remain noticeable
    outlineAlpha = max(outlineAlpha, 0.06 * edgeStrength * g_flOutlineIntensity);

    // Where the selection next to the edge is seen it is outlined brightly, and faintly where the scene hides it
    float visibility = coverage.g / coverage.r;
    vec4 color = mix(g_vOccludedColor, g_vOutlineColor, visibility);

    return vec4(color.rgb, saturate(color.a * outlineAlpha));
}

void main()
{
    outputColor = DrawOutline();
}
