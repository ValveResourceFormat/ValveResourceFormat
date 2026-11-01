#version 460

#include "common/order_independent.slang"

// Blends the layers over the scene front to back. Layers past the second take the fill's opacity and the second's color.

layout(binding = 0, r32ui) uniform readonly uimage2DArray g_tOrderIndependentLayers;

// Coplanar faces swap order from pixel to pixel, so layers this close share their color
const uint sameDistanceNearness = uint(log2(1.01) / orderIndependentMaxDistanceLog2 * 65535.0);

out vec4 outputColor;

void main()
{
    ivec2 pixel = ivec2(gl_FragCoord.xy);
    uint nearest = imageLoad(g_tOrderIndependentLayers, ivec3(pixel, 0)).r;

    if (nearest == 0u)
    {
        discard;
    }

    uint second = imageLoad(g_tOrderIndependentLayers, ivec3(pixel, 1)).r;
    uint count = imageLoad(g_tOrderIndependentLayers, ivec3(pixel, 2)).r;

    vec3 nearestColor = UnpackOrderIndependentColor(nearest);
    vec3 secondColor = UnpackOrderIndependentColor(second);

    if (count >= 2u && (nearest >> 16) - (second >> 16) <= sameDistanceNearness)
    {
        nearestColor = (nearestColor + secondColor) * 0.5;
        secondColor = nearestColor;
    }

    float nearestAlpha = UnpackOrderIndependentAlpha(nearest);
    float nearestTransmittance = 1.0 - nearestAlpha;
    float behindNearest = OrderIndependentBehindNearest(second, count);

    vec3 color = nearestColor * nearestAlpha;
    color += secondColor * nearestTransmittance * (1.0 - behindNearest);

    outputColor = vec4(color, nearestTransmittance * behindNearest);
}
