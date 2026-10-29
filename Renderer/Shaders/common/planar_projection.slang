#version 460

#ifndef VRF_PLANAR_PROJECTION_INCLUDE
#define VRF_PLANAR_PROJECTION_INCLUDE

// Biplanar projection along the world axes the normal faces most, and the overlay that modulates albedo with it.

// Axis permutations for the biggest and smallest components of the normal, plus the remaining one
struct PlanarAxes_t
{
    ivec3 Major;
    ivec3 Median;
    vec3 AbsNormal;
};

PlanarAxes_t GetPlanarAxes(vec3 normal)
{
    vec3 absNormal = abs(normal);

    ivec3 major = (absNormal.x > absNormal.y && absNormal.x > absNormal.z)
        ? ivec3(0, 1, 2)
        : ((absNormal.y > absNormal.z) ? ivec3(1, 2, 0) : ivec3(2, 0, 1));

    ivec3 minor = (absNormal.x < absNormal.y && absNormal.x < absNormal.z)
        ? ivec3(0, 1, 2)
        : ((absNormal.y < absNormal.z) ? ivec3(1, 2, 0) : ivec3(2, 0, 1));

    PlanarAxes_t axes;
    axes.Major = major;
    axes.Median = ivec3(3) - minor - major;
    axes.AbsNormal = absNormal;
    return axes;
}

vec2 GetPlanarTexCoord(vec3 position, ivec3 axis)
{
    vec2 planar = vec2(position[axis.y], position[axis.z]);

    if (axis.x == 1)
    {
        planar = planar.yx;
    }

    return vec2(planar.x, -planar.y);
}

// Blend between the two planes once either is more than ~35 degrees off its axis.
vec2 GetPlanarWeights(PlanarAxes_t axes)
{
    vec2 weights = saturate((vec2(axes.AbsNormal[axes.Major.x], axes.AbsNormal[axes.Median.x]) - 0.5773) * 2.3657441);
    return weights / (weights.x + weights.y);
}

// Samples the two planes the normal faces most, projected from the given position
vec4 SampleBiPlanar(sampler2D overlayTexture, vec3 position, vec3 normal, float tiling)
{
    PlanarAxes_t axes = GetPlanarAxes(normal);
    float invTiling = 1.0 / ((tiling == 0.0) ? 1.0 : tiling);

    vec2 majorUv = GetPlanarTexCoord(position, axes.Major) * invTiling;
    vec2 medianUv = GetPlanarTexCoord(position, axes.Median) * invTiling;

    vec3 positionDdx = dFdxFine(position) * invTiling;
    vec3 positionDdy = dFdyFine(position) * invTiling;

    vec4 major = textureGrad(overlayTexture, majorUv,
        vec2(positionDdx[axes.Major.y], positionDdx[axes.Major.z]),
        vec2(positionDdy[axes.Major.y], positionDdy[axes.Major.z]));
    vec4 median = textureGrad(overlayTexture, medianUv,
        vec2(positionDdx[axes.Median.y], positionDdx[axes.Median.z]),
        vec2(positionDdy[axes.Median.y], positionDdy[axes.Median.z]));

    return mix(major, median, GetPlanarWeights(axes).y);
}

// An overlay texel is a signed multiplier around 0.5. Values above brighten and values
// below darken, each side shaped by its own contrast exponent.
vec3 GetOverlayFactor(vec3 overlay, float darknessContrast, float brightnessContrast)
{
    vec3 signedOverlay = UnpackFromColor(overlay);

    vec3 brighten = (vec3(1.0) - SafePow(vec3(1.0) - max(vec3(0.0), signedOverlay), vec3(brightnessContrast))) * brightnessContrast;
    vec3 darken = (SafePow(vec3(1.0) + min(vec3(0.0), signedOverlay), vec3(darknessContrast)) - vec3(1.0)) * darknessContrast;

    return max(vec3(0.0), brighten + darken + vec3(1.0));
}

#endif
