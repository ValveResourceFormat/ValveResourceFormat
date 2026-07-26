#version 460

#ifndef VRF_LIGHT_CULL_INCLUDE
#define VRF_LIGHT_CULL_INCLUDE

#include "ViewConstants.slang"

#define LIGHT_CULL_MAX_LIGHTS 128
#define LIGHT_CULL_MAX_WORDS (LIGHT_CULL_MAX_LIGHTS / 32)

layout(std430, binding = 13) readonly buffer LightCullBitsBuffer
{
    uint g_LightCullBits[];
};

uint GetCullWordMask(uint count, uint word)
{
    uint first = word * 32u;
    uint live = count > first ? min(count - first, 32u) : 0u;

    return live >= 32u ? 0xFFFFFFFFu : ((1u << live) - 1u);
}

float GetLightCullViewDepth(vec3 positionWs)
{
    return dot(positionWs - g_vLightCullCameraPositionWs, g_vLightCullCameraDirWs);
}

uint GetLightCullDepthSlice(float viewDepth, vec2 sliceParams, uint sliceCount)
{
    float slice = log2(max(viewDepth, 1.0)) * sliceParams.x + sliceParams.y;
    return uint(clamp(slice, 0.0, float(sliceCount - 1u)));
}

struct CullLookup_t
{
    uint m_nTileIndex;

    uvec2 m_nTile;

    uint m_nBin;

    bool m_bValid;
};

CullLookup_t MakeCullLookup(vec2 cullPixel, bool inFront, vec3 positionWs)
{
    bool haveTile = inFront
                 && all(greaterThanEqual(cullPixel, vec2(0.0)))
                 && all(lessThan(cullPixel, g_vViewportSize));

    uvec2 tile = uvec2(clamp(cullPixel, vec2(0.0), g_vViewportSize - 1.0)) >> uvec2(g_nLightTileShift);
    tile = min(tile, uvec2(g_nLightTileCols, g_nLightTileRows) - 1u);

    CullLookup_t lookup;
    lookup.m_nTileIndex = (tile.y * g_nLightTileCols) + tile.x;
    lookup.m_nTile = tile;
    lookup.m_nBin = GetLightCullDepthSlice(GetLightCullViewDepth(positionWs),
                                           g_vLightDepthSliceParams.xy, g_nLightSliceCount);
    lookup.m_bValid = haveTile;

    return lookup;
}

CullLookup_t GetCullLookup(vec3 positionWs)
{
    vec2 cullPixel = (gl_FragCoord.xy * g_vLightCullPixelRemap.xy) + g_vLightCullPixelRemap.zw;

    return MakeCullLookup(cullPixel, true, positionWs);
}

CullLookup_t GetCullLookupWs(vec3 positionWs)
{
    vec4 cullClip = g_matLightCullWorldToProjection * vec4(positionWs, 1.0);

    bool inFront = cullClip.w > 1e-6;

    vec2 cullPixel = ((cullClip.xy / max(cullClip.w, 1e-6)) * 0.5 + 0.5) * g_vViewportSize;

    return MakeCullLookup(cullPixel, inFront, positionWs);
}

CullLookup_t g_CullLookup;
bool g_bCullLookupReady = false;

CullLookup_t GetCullLookupCached(vec3 positionWs)
{
    if (!g_bCullLookupReady)
    {
        g_CullLookup = GetCullLookup(positionWs);
        g_bCullLookupReady = true;
    }

    return g_CullLookup;
}

float GetLightCullBinStripe(vec3 positionWs)
{
    if (!g_bLightTilesValid)
    {
        return 1.0;
    }

    CullLookup_t lookup = GetCullLookupCached(positionWs);

    if (!lookup.m_bValid)
    {
        return 1.0;
    }

    float xyDim = (((lookup.m_nTile.x ^ lookup.m_nTile.y) & 1u) != 0u) ? 0.85 : 1.0;
    float zDim = (lookup.m_nBin & 1u) != 0u ? 0.6 : 1.0;

    return xyDim * zDim;
}

#endif
