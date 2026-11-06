#version 460

#ifndef VRF_UV_RENDER_MODES_INCLUDE
#define VRF_UV_RENDER_MODES_INCLUDE

#include "utils.slang"

// Darkens a checker of texels and a checker of 16 texel blocks of a texture with the given size
vec3 ApplyUvDensityChecker(vec3 color, vec2 uv, ivec2 dimensions)
{
    uint testVal = ((uv.x < 0) != (uv.y < 0)) ? 0 : 1;
    uvec2 vUVInPixels = uvec2(abs(uv) * dimensions.xy);
    if (((vUVInPixels.x + vUVInPixels.y) & 1) == testVal)
    {
        color *= 0.6;
    }

    uvec2 vUVIn16Pixels = vUVInPixels / 16;
    if (((vUVIn16Pixels.x + vUVIn16Pixels.y) & 1) == testVal)
    {
        color *= 0.5;
    }

    return color;
}

// Tints a checker of the texture by the mip level it is sampled at: green for the largest mip, red for the smallest
vec3 ApplyMipmapUsageChecker(vec3 color, sampler2D representativeTexture, vec2 flUVs)
{
    ivec2 vTexDimensions = textureSize(representativeTexture, 0);
    float flMipLevel = textureQueryLod(representativeTexture, flUVs).y;
    float flMipLevels = log2(max(vTexDimensions.x, vTexDimensions.y));

    uint testVal = ((flUVs.x < 0) != (flUVs.y < 0)) ? 0 : 1;
    uvec2 vUVInPixels = uvec2(abs(flUVs) * vTexDimensions.xy);
    uvec2 vUVIn16Pixels = vUVInPixels / uint(8 * round(1 + flMipLevel));

    float fIntensity = (((vUVIn16Pixels.x + vUVIn16Pixels.y) & 1) == testVal) ? .75 : .25f;
    return mix(color, vec3(1.0, flMipLevel / flMipLevels, 0.0), fIntensity);
}

#endif
