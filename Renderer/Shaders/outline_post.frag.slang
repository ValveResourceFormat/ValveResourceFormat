#version 460

#include "common/utils.slang"

// Where the selection is seen, and where the scene hides it
uniform vec4 g_vOutlineColor;
uniform vec4 g_vOccludedColor;

// How much of each pixel the selection covers. Red: the selection wherever it is. Green: where the scene does not hide it.
layout (binding = 0) uniform sampler2D g_tOutlineMask;

out vec4 outputColor;

// The line reaches two pixels out of the silhouette and one into it, read from the rings of those sizes around a pixel
const int OuterRadius = 2;
const float OuterRingSize = 24.0;
const float InnerRingSize = 8.0;

// How sharply the line rises with how mixed the ring around a pixel is
const float OutlineGain = 3.0;

// Nothing is selected outside the image, so the outline closes along its border
vec2 LoadOutlineMask(ivec2 pixelCoords, ivec2 maskTextureSize)
{
    if (any(lessThan(pixelCoords, ivec2(0))) || any(greaterThanEqual(pixelCoords, maskTextureSize)))
    {
        return vec2(0.0);
    }

    return texelFetch(g_tOutlineMask, pixelCoords, 0).rg;
}

void main()
{
    ivec2 maskTextureSize = textureSize(g_tOutlineMask, 0);
    ivec2 pixelCoords = ivec2(gl_FragCoord.xy);

    vec2 outerRing = vec2(0.0);
    float innerRing = 0.0;

    for (int y = -OuterRadius; y <= OuterRadius; y++)
    {
        for (int x = -OuterRadius; x <= OuterRadius; x++)
        {
            if (x == 0 && y == 0)
            {
                continue;
            }

            vec2 mask = LoadOutlineMask(pixelCoords + ivec2(x, y), maskTextureSize);
            outerRing += mask;

            if (abs(x) <= 1 && abs(y) <= 1)
            {
                innerRing += mask.r;
            }
        }
    }

    vec2 center = LoadOutlineMask(pixelCoords, maskTextureSize);

    // Outside the selection the line rises with how much of the wide ring is covered, inside it fades as the
    // narrow ring fills
    float outsideEdge = saturate(OutlineGain * outerRing.r / OuterRingSize);
    float insideEdge = saturate(OutlineGain * (1.0 - innerRing / InnerRingSize));
    float edge = mix(outsideEdge, insideEdge, center.r);

    if (edge < 0.001)
    {
        discard;
    }

    // How much of the selection around the pixel is seen
    vec2 coverage = outerRing + center;
    float visibility = coverage.g / max(coverage.r, 1e-4);

    vec4 color = mix(g_vOccludedColor, g_vOutlineColor, saturate(visibility));
    outputColor = vec4(color.rgb, color.a * edge);
}
