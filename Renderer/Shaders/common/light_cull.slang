#version 460

#ifndef VRF_LIGHT_CULL_INCLUDE
#define VRF_LIGHT_CULL_INCLUDE

#include "ViewConstants.slang"

layout(binding = 6, std140) uniform LightCullConstants
{
    uint g_nLightTileBase;
    uint g_nLightSliceBase;
    uint g_nLightCullWords;
    uint g_nLightTileShift;

    uint g_nLightTileCols;
    uint g_nLightTileRows;
    uint g_nLightSliceCount;
    uint g_nEnvMapTileBase;

    uint g_nEnvMapBinBase;
    uint g_nEnvMapCullWords;
    uint _lightCullPadding0;
    uint _lightCullPadding1;

    vec4 g_vLightDepthSliceParams;
    vec4 g_vLightCullPixelRemap;

    mat4 g_matLightCullWorldToProjection;

    vec3 g_vLightCullCameraPositionWs;
    float _lightCullPadding2;

    vec3 g_vLightCullCameraDirWs;
    float _lightCullPadding3;
};

layout(std430, binding = 13) readonly buffer LightCullBitsBuffer
{
    uint g_LightCullBits[];
};

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
    uvec2 tile = uvec2(clamp(cullPixel, vec2(0.0), g_vViewportSize - 1.0)) >> uvec2(g_nLightTileShift);
    tile = min(tile, uvec2(g_nLightTileCols, g_nLightTileRows) - 1u);

    CullLookup_t lookup;
    lookup.m_nTileIndex = (tile.y * g_nLightTileCols) + tile.x;
    lookup.m_nTile = tile;
    lookup.m_nBin = GetLightCullDepthSlice(GetLightCullViewDepth(positionWs),
                                           g_vLightDepthSliceParams.xy, g_nLightSliceCount);
    lookup.m_bValid = inFront;

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

struct CullMasks_t
{
    uint m_nTileWord;
    uint m_nBinWord;
    uint m_nWords;
};

CullMasks_t MakeCullMasks(CullLookup_t lookup, uint tileBase, uint binBase, uint words)
{
    CullMasks_t masks;
    masks.m_nTileWord = tileBase + (lookup.m_nTileIndex * words);
    masks.m_nBinWord = binBase + (lookup.m_nBin * words);
    masks.m_nWords = words;

    return masks;
}

CullMasks_t GetBarnLightCullMasks(vec3 positionWs)
{
    return MakeCullMasks(GetCullLookupCached(positionWs),
                         g_nLightTileBase, g_nLightSliceBase, g_nLightCullWords);
}

CullMasks_t GetEnvMapCullMasks(vec3 positionWs)
{
    return MakeCullMasks(GetCullLookupCached(positionWs),
                         g_nEnvMapTileBase, g_nEnvMapBinBase, g_nEnvMapCullWords);
}

uint GetCullWordMask(uint count, uint word)
{
    uint first = word * 32u;
    uint live = count > first ? min(count - first, 32u) : 0u;

    return live >= 32u ? 0xFFFFFFFFu : ((1u << live) - 1u);
}

uint GetCullMaskWord(CullMasks_t masks, uint word)
{
    uint mask = g_LightCullBits[masks.m_nTileWord + word] & g_LightCullBits[masks.m_nBinWord + word];

#ifdef GL_KHR_shader_subgroup_arithmetic
    mask = subgroupOr(mask);
#endif

    return mask;
}

uint GetCullMaskWord(CullMasks_t masks, uint word, uint objectMask)
{
    return GetCullMaskWord(masks, word) & objectMask;
}

bool AnyLaneWantsMore(bool wantsMore)
{
#if defined(GL_KHR_shader_subgroup_vote)
    return subgroupAny(wantsMore);
#elif defined(GL_KHR_shader_subgroup_arithmetic)
    return true;
#else
    return wantsMore;
#endif
}

#define LIGHT_TILES_DEBUG_MODE 0

const bool LIGHT_TILES_DEBUG_TILE_INDEX = (LIGHT_TILES_DEBUG_MODE == 1);
const bool LIGHT_TILES_DEBUG_MASKS = (LIGHT_TILES_DEBUG_MODE == 2);

vec3 GetLightCullMaskDebug(vec3 positionWs)
{
    if (g_nLightCullWords == 0u)
    {
        return vec3(0.0);
    }

    CullLookup_t lookup = GetCullLookupCached(positionWs);

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

vec3 GetLightCullTileDebug(vec3 positionWs)
{
    CullLookup_t lookup = GetCullLookupCached(positionWs);

    bool bLastColumn = lookup.m_nTile.x == (g_nLightTileCols - 1u);
    bool bLastRow = lookup.m_nTile.y == (g_nLightTileRows - 1u);

    return vec3(float(lookup.m_nTile.x) / float(g_nLightTileCols),
                float(lookup.m_nTile.y) / float(g_nLightTileRows),
                (bLastColumn || bLastRow) ? 1.0 : 0.0);
}

float GetLightCullBinStripe(vec3 positionWs)
{
    CullLookup_t lookup = GetCullLookupCached(positionWs);

    float xyDim = (((lookup.m_nTile.x ^ lookup.m_nTile.y) & 1u) != 0u) ? 0.85 : 1.0;
    float zDim = (lookup.m_nBin & 1u) != 0u ? 0.6 : 1.0;

    return xyDim * zDim;
}

#endif
