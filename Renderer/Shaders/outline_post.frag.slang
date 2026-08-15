#version 460

#include "common/utils.slang"

uniform int g_nNumSamplesMSAA = 1;

uniform float g_flOutlineSize = 1;
uniform float g_flOutlineSoftness = 0.8;
uniform float g_flOutlineIntensity = 2.25;

layout (binding = 0) uniform sampler2DMS g_tOutlineMask;

void SampleOutlineMaskMSAA(ivec2 pixelCoords, inout float insideCount, inout int totalSamples)
{
    for (int i = 0; i < g_nNumSamplesMSAA; i++)
    {
        float maskValue = texelFetch(g_tOutlineMask, pixelCoords, i).r;
        insideCount += maskValue;
    }

    totalSamples += g_nNumSamplesMSAA;
}

vec4 DrawOutline()
{
    // base kernel width (scaled by g_flOutlineSize)
    int width = max(1, int(floor(2.5 * g_flOutlineSize + 0.5)));
    const int shadingRate = 1;

    ivec2 pixelCoords = ivec2(gl_FragCoord.xy);

    int totalSamples = 0;
    float insideCountCenter = 0.0;

    SampleOutlineMaskMSAA(pixelCoords, insideCountCenter, totalSamples);

    if (insideCountCenter >= float(g_nNumSamplesMSAA))
    {
        // do not draw outline inside the object
        discard;
    }

    float insideCount = insideCountCenter;
    ivec2 maskTextureSize = textureSize(g_tOutlineMask);

    // Accumulate samples in a neighborhood and also collect positional info for inside-samples
    int insideSamplePixels = 0;
    vec2 insidePosSum = vec2(0.0);
    float minDistSq = 1e9;

    for(int x = -width; x <= width;  x += shadingRate)
    {
        for(int y = -width; y <= width; y += shadingRate)
        {
            ivec2 sampleCoords = pixelCoords + ivec2(x, y);

            if (sampleCoords.x < 0 || sampleCoords.x >= maskTextureSize.x ||
                sampleCoords.y < 0 || sampleCoords.y >= maskTextureSize.y)
            {
                continue;
            }

            // Update global inside/total accumulators used for edge strength
            float beforeInside = insideCount;
            SampleOutlineMaskMSAA(sampleCoords, insideCount, totalSamples);

            // Derive per-pixel coverage from the accumulator
            float localInside = insideCount - beforeInside;
            if (localInside > 0.0)
            {
                // weight by how many MSAA samples at this pixel are inside
                float w = localInside;
                insideSamplePixels += int(w);
                insidePosSum += vec2(sampleCoords) * w;

                vec2 d = vec2(pixelCoords) - vec2(sampleCoords);
                minDistSq = min(minDistSq, dot(d, d));
            }
        }
    }

    if (totalSamples == 0)
    {
        return vec4(0.0);
    }

    float covNeighborhood = insideCount / float(totalSamples); // 0..1 coverage in neighborhood
    float covCenter = insideCountCenter / float(g_nNumSamplesMSAA); // per-fragment coverage (0..1)

    // If there's almost no coverage nearby, skip outline
    if (covNeighborhood < 0.01)
    {
        discard;
    }

    // Edge strength: how much of the kernel is inside while the center is not.
    float edgeStrength = clamp(covNeighborhood - covCenter, 0.0, 1.0);
    // widen the smoothstep so weaker edges still contribute
    edgeStrength = smoothstep(0.001, 0.85 * g_flOutlineSoftness, edgeStrength);

    // Compute a smooth exterior falloff based on distance (in pixels) to the nearest inside sample
    float distPixels = sqrt(minDistSq);
    float fadeRadius = max(1.0, float(width)) * (2.6 * g_flOutlineSize) * max(0.6, g_flOutlineSoftness);
    float exteriorFade = clamp(1.0 - smoothstep(0.0, fadeRadius, distPixels), 0.0, 1.0);
    float outlineAlpha = edgeStrength * exteriorFade * g_flOutlineIntensity;
    outlineAlpha = pow(outlineAlpha, 0.75);

    // Ensure a small visible floor so very thin silhouettes remain noticeable
    outlineAlpha = max(outlineAlpha, 0.06 * edgeStrength * g_flOutlineIntensity);

    return vec4(vec3(1.0, 1.0, 0.2), outlineAlpha);
}

out vec4 outputColor;
void main()
{
    vec4 vOutline = DrawOutline();
    outputColor = vOutline;
}
