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
    uint g_nEnvMapCount;
    uint _lightCullPadding0;

    vec4 g_vLightDepthSliceParams;
    vec4 g_vLightCullPixelRemap;

    vec3 g_vLightCullCameraPositionWs;
    float _lightCullPadding1;

    vec3 g_vLightCullCameraDirWs;
    float _lightCullPadding2;
};

layout(std430, binding = 12) readonly buffer LightCullBitsBuffer
{
    uint g_CullBits[];
};

float GetLightCullViewDepth(vec3 positionWs)
{
    return dot(positionWs - g_vLightCullCameraPositionWs, g_vLightCullCameraDirWs);
}

// sliceParams: x slices per world unit, y the last slice index. Mirrors DepthBinWidth in TiledCullFeeder.
uint GetLightCullDepthSlice(float viewDepth, vec2 sliceParams)
{
    return uint(clamp(viewDepth * sliceParams.x, 0.0, sliceParams.y));
}

struct CullLookup_t
{
    uint m_nTileIndex;

    uvec2 m_nTile;

    uint m_nBin;
};

// Set by a pass whose vertices are displaced in clip space
bool g_bCullFromShadedPosition = false;

CullLookup_t GetCullLookup(vec3 positionWs)
{
    vec2 cullPixelVs = gl_FragCoord.xy;

    // A pass that displaces the vertex in clip space rasterizes somewhere other than where the world
    // position it shades with projects, so its gl_FragCoord names the wrong tile.
    if (g_bCullFromShadedPosition)
    {
        vec4 cullClip = g_matWorldToProjection * vec4(positionWs, 1.0);
        vec2 cullNdc = cullClip.xy / max(cullClip.w, 1e-6);
        cullPixelVs = ((cullNdc * 0.5) + 0.5) * g_vViewportSize;
    }

    // Carries the pixel into the view the masks were built for; identity for everything but the viewmodel.
    vec2 cullPixel = (cullPixelVs * g_vLightCullPixelRemap.xy) + g_vLightCullPixelRemap.zw;

    uvec2 tile = uvec2(clamp(cullPixel, vec2(0.0), g_vViewportSize - 1.0)) >> uvec2(g_nLightTileShift);
    tile = min(tile, uvec2(g_nLightTileCols, g_nLightTileRows) - 1u);

    CullLookup_t lookup;
    lookup.m_nTileIndex = (tile.y * g_nLightTileCols) + tile.x;
    lookup.m_nTile = tile;
    lookup.m_nBin = GetLightCullDepthSlice(GetLightCullViewDepth(positionWs), g_vLightDepthSliceParams.xy);

    return lookup;
}

CullLookup_t g_CullLookup = CullLookup_t(0u, uvec2(0u), 0u);
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

uint GetCullLiveMask(uint word, uint count)
{
    uint nFirst = word * 32u;
    uint nLive = count > nFirst ? min(count - nFirst, 32u) : 0u;

    return nLive >= 32u ? 0xFFFFFFFFu : ((1u << nLive) - 1u);
}

uint GetEnvMapLiveMask(uint word)
{
    return GetCullLiveMask(word, g_nEnvMapCount);
}

uint GetCullMaskWord(CullMasks_t masks, uint word)
{
    uint mask = g_CullBits[masks.m_nTileWord + word] & g_CullBits[masks.m_nBinWord + word];

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

float GetLightCullBinStripe(vec3 positionWs)
{
    CullLookup_t lookup = GetCullLookupCached(positionWs);

    float xyDim = (((lookup.m_nTile.x ^ lookup.m_nTile.y) & 1u) != 0u) ? 0.85 : 1.0;
    float zDim = (lookup.m_nBin & 1u) != 0u ? 0.6 : 1.0;

    return xyDim * zDim;
}

#endif
