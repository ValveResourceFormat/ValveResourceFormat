#version 460

#ifndef VRF_ORDER_INDEPENDENT_INCLUDE
#define VRF_ORDER_INDEPENDENT_INCLUDE

// A fragment packs into one uint that is larger the nearer it is, so atomic max keeps the nearest:
// 16 bits of inverted log distance, 2 bits of opacity from the fill's to opaque, and 5:5:4 bits of color.
// Zero is an empty layer.

// Distances up to two to this power, in units, are told apart
const float orderIndependentMaxDistanceLog2 = 16.0;

// Matches ShapeSceneNode.OrderIndependentFillAlpha
const float orderIndependentFillAlpha = 0.65;

// Lit colors exceed one, so they are stored as x / (1 + x) instead of clamped, which would shift their hue.
// The cap keeps the inverse finite.
const float orderIndependentMaxRange = 0.995;

// The top half of a packed fragment at this distance
uint PackOrderIndependentNearness(float distance)
{
    float distanceScale = clamp(log2(max(distance, 1.0)) / orderIndependentMaxDistanceLog2, 0.0, 1.0);

    return 65535u - uint(distanceScale * 65535.0);
}

uint PackOrderIndependentFragment(vec3 color, float alpha, float distance)
{
    uint nearness = PackOrderIndependentNearness(distance);
    float opacity = (alpha - orderIndependentFillAlpha) / (1.0 - orderIndependentFillAlpha);
    uint opacityLevel = uint(round(clamp(opacity, 0.0, 1.0) * 3.0));

    vec3 positive = max(color, 0.0);
    vec3 encoded = sqrt(positive / (1.0 + positive));
    uint red = uint(round(encoded.r * 31.0));
    uint green = uint(round(encoded.g * 31.0));
    uint blue = uint(round(encoded.b * 15.0));

    return (nearness << 16) | (opacityLevel << 14) | (red << 9) | (green << 4) | blue;
}

vec3 UnpackOrderIndependentColor(uint fragment)
{
    vec3 encoded = vec3((fragment >> 9) & 31u, (fragment >> 4) & 31u, fragment & 15u) / vec3(31.0, 31.0, 15.0);
    vec3 range = min(encoded * encoded, vec3(orderIndependentMaxRange));

    return range / (1.0 - range);
}

float UnpackOrderIndependentAlpha(uint fragment)
{
    float opacityLevel = float((fragment >> 14) & 3u);

    return mix(orderIndependentFillAlpha, 1.0, opacityLevel / 3.0);
}

// What the layers behind the nearest let through: the second with its own opacity, the rest with the fill's
float OrderIndependentBehindNearest(uint second, uint count)
{
    if (count < 2u)
    {
        return 1.0;
    }

    return (1.0 - UnpackOrderIndependentAlpha(second)) * pow(1.0 - orderIndependentFillAlpha, float(count - 2u));
}

#endif
