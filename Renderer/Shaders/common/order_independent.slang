#version 460

#ifndef VRF_ORDER_INDEPENDENT_INCLUDE
#define VRF_ORDER_INDEPENDENT_INCLUDE

// A fragment packs into one uint that is larger the nearer it is, so atomic max keeps the nearest. The top
// half is the distance on a log scale, inverted, and fragments at the same distance order by their color.
// The bottom half is the color in 5:6:5 bits, stored as its square root to keep dark shades apart.
// Zero is an empty layer.

// Distances up to two to this power, in units, are told apart
const float orderIndependentMaxDistanceLog2 = 16.0;

// The top half of a packed fragment at this distance
uint PackOrderIndependentNearness(float distance)
{
    float distanceScale = clamp(log2(max(distance, 1.0)) / orderIndependentMaxDistanceLog2, 0.0, 1.0);

    return 65535u - uint(distanceScale * 65535.0);
}

uint PackOrderIndependentFragment(vec3 color, float distance)
{
    uint nearness = PackOrderIndependentNearness(distance);

    vec3 encoded = sqrt(clamp(color, 0.0, 1.0));
    uint red = uint(round(encoded.r * 31.0));
    uint green = uint(round(encoded.g * 63.0));
    uint blue = uint(round(encoded.b * 31.0));

    return (nearness << 16) | (red << 11) | (green << 5) | blue;
}

vec3 UnpackOrderIndependentColor(uint fragment)
{
    vec3 encoded = vec3((fragment >> 11) & 31u, (fragment >> 5) & 63u, fragment & 31u) / vec3(31.0, 63.0, 31.0);

    return encoded * encoded;
}

#endif
