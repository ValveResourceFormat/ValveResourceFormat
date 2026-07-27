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
    bool haveTile = inFront;

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

// 0 draws the heat map, 1 the tile a fragment resolves to, 2 which of the two masks it ANDs is empty.
#define LIGHT_TILES_DEBUG_MODE 2

const bool LIGHT_TILES_DEBUG_TILE_INDEX = (LIGHT_TILES_DEBUG_MODE == 1);
const bool LIGHT_TILES_DEBUG_MASKS = (LIGHT_TILES_DEBUG_MODE == 2);

// Red where the tile mask holds anything, green where the depth bin mask does, blue where their AND does,
// which is what the light loop actually walks. White is a working fragment. Yellow is the interesting one:
// both masks hold lights but not the same ones, so the two passes disagree about this fragment. Green
// alone means the tile pass never marked it, red alone means the bin pass did not.
vec3 GetLightCullMaskDebug(vec3 positionWs)
{
    if (!g_bLightTilesValid || g_nLightCullWords == 0u)
    {
        return vec3(0.0);
    }

    CullLookup_t lookup = GetCullLookupCached(positionWs);

    if (!lookup.m_bValid)
    {
        return vec3(1.0, 0.0, 1.0);
    }

    uint tileBase = g_nLightTileBase + (lookup.m_nTileIndex * g_nLightCullWords);
    uint sliceBase = g_nLightSliceBase + (lookup.m_nBin * g_nLightCullWords);

    uint nTileBits = 0u;
    uint nSliceBits = 0u;
    uint nBothBits = 0u;

    for (uint word = 0u; word < g_nLightCullWords; word++)
    {
        uint tileWord = g_LightCullBits[tileBase + word];
        uint sliceWord = g_LightCullBits[sliceBase + word];

        nTileBits += uint(bitCount(tileWord));
        nSliceBits += uint(bitCount(sliceWord));
        nBothBits += uint(bitCount(tileWord & sliceWord));
    }

    return vec3(nTileBits > 0u ? 1.0 : 0.0,
                nSliceBits > 0u ? 1.0 : 0.0,
                nBothBits > 0u ? 1.0 : 0.0);
}

// Magenta where the lookup reports no tile and the fragment falls back to iterating everything, black
// where the masks are not valid at all. Both are states the heat map cannot tell apart from a cold tile.
vec3 GetLightCullTileDebug(vec3 positionWs)
{
    if (!g_bLightTilesValid)
    {
        return vec3(0.0);
    }

    CullLookup_t lookup = GetCullLookupCached(positionWs);

    if (!lookup.m_bValid)
    {
        return vec3(1.0, 0.0, 1.0);
    }

    bool bLastColumn = lookup.m_nTile.x == (g_nLightTileCols - 1u);
    bool bLastRow = lookup.m_nTile.y == (g_nLightTileRows - 1u);

    return vec3(float(lookup.m_nTile.x) / float(g_nLightTileCols),
                float(lookup.m_nTile.y) / float(g_nLightTileRows),
                (bLastColumn || bLastRow) ? 1.0 : 0.0);
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
