#version 460

#include "common/order_independent.slang"

// Blends the order independent layers over the scene as sorted layers of equal opacity would. Front to back,
// each layer takes that opacity of what the layers in front of it let through, so all but the nearest two
// take little, and they take the color of the second.

layout(binding = 0, r32ui) uniform readonly uimage2DArray g_tOrderIndependentLayers;

uniform float g_flLayerAlpha;

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

    // What all the layers let through, and what the layers behind the nearest take, zero when it is alone
    float transmittance = pow(1.0 - g_flLayerAlpha, float(count));
    float behindNearest = (1.0 - g_flLayerAlpha) - transmittance;

    vec3 color = UnpackOrderIndependentColor(nearest) * g_flLayerAlpha;
    color += UnpackOrderIndependentColor(second) * behindNearest;

    outputColor = vec4(color, transmittance);
}
