#version 460

#ifndef VRF_GPU_CULL_INCLUDE
#define VRF_GPU_CULL_INCLUDE

#include "ViewConstants.slang"

layout(binding = 4, std140) uniform FrustumPlanes
{
    vec4 planes[6];
};

layout(binding = 0) uniform sampler2D g_tDepthPyramid;

uniform int g_nDepthPyramidMaxMip;
uniform int g_nDepthPyramidWidth;
uniform int g_nDepthPyramidHeight;
uniform float g_flDepthRangeMin;
uniform float g_flDepthRangeMax;
uniform bool g_bOcclusionCullEnabled;

bool testAABBOcclusion(vec3 bmin, vec3 bmax)
{

    vec4 corners[8];
    for (int i = 0; i < 8; i++)
    {
        vec3 corner = vec3(
            (i & 1) != 0 ? bmax.x : bmin.x,
            (i & 2) != 0 ? bmax.y : bmin.y,
            (i & 4) != 0 ? bmax.z : bmin.z
        );

        corners[i] = g_matPrevWorldToProjection * vec4(corner, 1.0);
    }

    float minW = min(min(min(corners[0].w, corners[1].w), min(corners[2].w, corners[3].w)),
                     min(min(corners[4].w, corners[5].w), min(corners[6].w, corners[7].w)));

    if (minW <= 0.0)
    {
        return false;
    }

    vec3 ndc[8];
    for (int i = 0; i < 8; i++)
    {
        ndc[i] = corners[i].xyz / corners[i].w;
    }

    vec3 ndcMin = max(
        min(min(min(ndc[0], ndc[1]), min(ndc[2], ndc[3])),
            min(min(ndc[4], ndc[5]), min(ndc[6], ndc[7]))),
        vec3(-1.0)
    );
    vec3 ndcMax = min(
        max(max(max(ndc[0], ndc[1]), max(ndc[2], ndc[3])),
            max(max(ndc[4], ndc[5]), max(ndc[6], ndc[7]))),
        vec3(1.0)
    );

    vec2 uvMin = ndcMin.xy * 0.5 + 0.5;
    vec2 uvMax = ndcMax.xy * 0.5 + 0.5;
    vec2 uvSize = uvMax - uvMin;

    if (uvSize.x * uvSize.y <= 0.0 || ndcMax.z >= 1.0)
    {
        return false;
    }

    const bool SizeCull = true;
    const float minPixelSize = 0.2;
    vec2 screenSpaceSize = uvSize * g_vViewportSize;

    if (SizeCull && any(lessThan(screenSpaceSize, vec2(minPixelSize))))
    {
        bool atScreenEdge = any(equal(uvMin.xy, vec2(0.0))) || any(equal(uvMax.xy, vec2(1.0)));
        return atScreenEdge == false;
    }

    vec2 pixelMin = uvMin * vec2(g_nDepthPyramidWidth, g_nDepthPyramidHeight);
    vec2 pixelMax = uvMax * vec2(g_nDepthPyramidWidth, g_nDepthPyramidHeight);
    vec2 pixelSize = ceil(pixelMax) - floor(pixelMin);

    int mipLevel = min(g_nDepthPyramidMaxMip, int(ceil(log2(max(max(pixelSize.x, pixelSize.y), 1.0)))));

    ivec2 texelMin = ivec2(pixelMin) >> mipLevel;
    ivec2 texelMax = ivec2(pixelMax) >> mipLevel;

    if (all(equal(texelMin, texelMax)) && mipLevel > 0)
    {
        mipLevel--;
        texelMin = ivec2(pixelMin) >> mipLevel;
        texelMax = ivec2(pixelMax) >> mipLevel;
    }

    ivec2 mipSize = ivec2(max(1, g_nDepthPyramidWidth >> mipLevel), max(1, g_nDepthPyramidHeight >> mipLevel));
    ivec2 maxTexel = mipSize - ivec2(1);

    texelMin = clamp(texelMin, ivec2(0), maxTexel);
    texelMax = clamp(texelMax, ivec2(0), maxTexel);

    float depth0 = texelFetch(g_tDepthPyramid, texelMin, mipLevel).r;
    float depth1 = texelFetch(g_tDepthPyramid, ivec2(texelMax.x, texelMin.y), mipLevel).r;
    float depth2 = texelFetch(g_tDepthPyramid, ivec2(texelMin.x, texelMax.y), mipLevel).r;
    float depth3 = texelFetch(g_tDepthPyramid, texelMax, mipLevel).r;

    float occluderDepth = min(min(depth0, depth1), min(depth2, depth3));

    if (occluderDepth > 0.5)
    {
        return false;
    }

    float aabbDepth = ndcMax.z * (g_flDepthRangeMax - g_flDepthRangeMin) + g_flDepthRangeMin;

    return aabbDepth < (occluderDepth - 1e-5);
}

bool testAABBFrustum(vec3 bmin, vec3 bmax)
{
    for (int i = 0; i < 6; ++i)
    {
        vec4 plane = planes[i];
        vec3 normal = plane.xyz;
        float dist = plane.w;

        vec3 closest;
        closest.x = normal.x < 0.0 ? bmin.x : bmax.x;
        closest.y = normal.y < 0.0 ? bmin.y : bmax.y;
        closest.z = normal.z < 0.0 ? bmin.z : bmax.z;

        if (dot(normal, closest) + dist < 0.0)
        {
            return false;
        }
    }

    return true;
}

#endif
