#version 460

// Reconstructed from CS2 shaders/vfx/compute_tile_cullbits_vulkan_50_cs.vcs.
// 1 static combo, 2 dynamic variants, 2 unique bytecodes; D_SUBGROUP_SIZE_32 is the only combo.
//
// Producer half of Source 2's 2.5D cull-bit system. For every screen tile it writes a bitmask of the
// cull items whose screen footprint touches that tile. compute_depthbin_cullbits does the same over
// linear depth bins. A consumer ANDs one tile word with one bin word to get the items live at a pixel.
//
// Nothing feeds this yet - it is here as the reference for what the CPU side has to produce. The
// algorithm is faithful to the bytecode; only two backend details were changed, because they are
// artifacts of the Vulkan build rather than of Valve's source:
//   - `set = 0` descriptor qualifiers dropped, bindings renumbered off the Vulkan bind points
//     (t30/t31/u158) to free GL ones. The dump's own header notes bind points do not carry over.
//   - std140 is stated explicitly on CullParams_t. It happens to reproduce the DXBC packoffsets
//     exactly (c0/c1/c3/c4/c7), so the block is byte-compatible with what Source 2 uploads.
//
// Names for CullParams_t, g_CullPlanes, g_CullItems and g_OutputCullBits are read out of the DXBC
// reflection of the _pc_ build. Member names inside the two structured buffers are inferred from use
// - DXBC records them only as `$Element`. The byte offsets in the comments are exact.

#define D_SUBGROUP_SIZE_32 0

#if D_SUBGROUP_SIZE_32
    #extension GL_KHR_shader_subgroup_arithmetic : require
#endif

layout(local_size_x = 8, local_size_y = 4, local_size_z = 1) in;

struct CullBatch_t
{
    uint nOutputOffset;   // +0   first uint of this batch's region in g_OutputCullBits
    uint nOutputStride;
    uint nFirstItem;
    uint nItemEnd;
};

struct CullItem_t
{
    uint  nFirstPlane;      // +0   index into g_CullPlanes of hull 0's first vertex
    uint  nUnused04;
    uint  nNumPlanes0;
    uint  nNumPlanes1;
    vec2  vBoundsMin;
    vec2  vBoundsMax;
    float flDepthMin;
    float flDepthMax;
    vec2  vNdcDepthNear;    // +40  x is the item's nearest NDC z; see OCCLUSION_CULL below. Source 2 reads
    float flConicXX;
    float flConicYY;
    float flConicXY;
    float flConicX;
    float flConicY;
    float flConicConst;
    float flConicEnable;
    float flUnused76;
};

layout(binding = 5, std140) uniform CullParams_t
{
    uint  g_nTiles;
    uint  g_nTilesX;
    uint  g_nTilesY;
    float g_fTileEpsilon;
    vec2  g_vTileToCenterScale;
    vec2  g_vTileToCenterOffset;
    uint  g_nDepthBins;
    float g_fDepthBinWidth;
    float g_fBinEpsilon;
    float g_fNearPlane;
    uvec4 g_nFirstMaskForBatch;
    CullBatch_t g_TileBatches[3];
    CullBatch_t g_BinBatches[3];
};

layout(binding = 15, std430) readonly buffer CullPlanesBuffer
{
    vec2 g_CullPlanes[];
};

layout(binding = 14, std430) readonly buffer CullItemsBuffer
{
    CullItem_t g_CullItems[];
};

layout(binding = 13, std430) writeonly buffer OutputCullBitsBuffer
{
    uint g_OutputCullBits[];
};

#if !D_SUBGROUP_SIZE_32
shared uint g_nGroupMask;
#endif

const bool OCCLUSION_CULL = true;

layout(binding = 0) uniform sampler2D g_tDepthPyramid;
uniform int g_nDepthPyramidMaxMip;
uniform int g_nDepthPyramidWidth;
uniform int g_nDepthPyramidHeight;
uniform float g_flDepthRangeMin;
uniform float g_flDepthRangeMax;
uniform bool g_bOcclusionCullEnabled;

uniform vec2 g_vCullToPyramidScale;

bool IsItemOccluded(vec2 vPixelMin, vec2 vPixelMax, float flNdcDepthNear)
{
    vec2 vPyramidMin = vPixelMin * g_vCullToPyramidScale;
    vec2 vPyramidMax = vPixelMax * g_vCullToPyramidScale;
    vec2 vSpan = ceil(vPyramidMax) - floor(vPyramidMin);

    int nMip = min(g_nDepthPyramidMaxMip, int(ceil(log2(max(max(vSpan.x, vSpan.y), 1.0)))));

    ivec2 nTexelMin = ivec2(vPyramidMin) >> nMip;
    ivec2 nTexelMax = ivec2(vPyramidMax) >> nMip;

    if (all(equal(nTexelMin, nTexelMax)) && nMip > 0)
    {
        nMip--;
        nTexelMin = ivec2(vPyramidMin) >> nMip;
        nTexelMax = ivec2(vPyramidMax) >> nMip;
    }

    ivec2 nMaxTexel = ivec2(max(1, g_nDepthPyramidWidth >> nMip), max(1, g_nDepthPyramidHeight >> nMip)) - ivec2(1);
    nTexelMin = clamp(nTexelMin, ivec2(0), nMaxTexel);
    nTexelMax = clamp(nTexelMax, ivec2(0), nMaxTexel);

    float flDepth0 = texelFetch(g_tDepthPyramid, nTexelMin, nMip).r;
    float flDepth1 = texelFetch(g_tDepthPyramid, ivec2(nTexelMax.x, nTexelMin.y), nMip).r;
    float flDepth2 = texelFetch(g_tDepthPyramid, ivec2(nTexelMin.x, nTexelMax.y), nMip).r;
    float flDepth3 = texelFetch(g_tDepthPyramid, nTexelMax, nMip).r;

    float flOccluderDepth = min(min(flDepth0, flDepth1), min(flDepth2, flDepth3));

    if (flOccluderDepth > 0.5)
    {
        return false;
    }

    float flItemDepth = (flNdcDepthNear * (g_flDepthRangeMax - g_flDepthRangeMin)) + g_flDepthRangeMin;

    const float kSlop = 1e-3;

    float flItemZ = flItemDepth - g_flDepthRangeMin;
    float flOccluderZ = flOccluderDepth - g_flDepthRangeMin;

    return (flItemZ * (1.0 + kSlop)) < flOccluderZ;
}

bool TestHull(vec2 vPoint, uint nFirstVertex, uint nVertexCount)
{
    if (nVertexCount != 0u)
    {
        vec2 vPrev = g_CullPlanes[(nFirstVertex + nVertexCount) - 1u];

        for (uint i = 0u; i < nVertexCount; i++)
        {
            vec2 vCur = g_CullPlanes[nFirstVertex + i];
            vec2 vEdge = vCur - vPrev;
            vec2 vNormal = vec2(-vEdge.y, vEdge.x);

            if ((dot(vNormal, vPoint) + g_fTileEpsilon) < dot(vNormal, vCur))
            {
                return false;
            }

            vPrev = vCur;
        }
    }

    return true;
}

bool TestConic(vec2 vPoint, CullItem_t item)
{
    if (item.flConicEnable == 0.0)
    {
        return true;
    }

    float x = vPoint.x;
    float y = vPoint.y;

    return ((((((item.flConicXX * (x * x))
              + (item.flConicYY * (y * y)))
              + (item.flConicXY * (x * y)))
              + (item.flConicX * x))
              + (item.flConicY * y))
              + item.flConicConst) >= 0.0;
}

void main()
{
    uint nBatch = (gl_WorkGroupID.z >= g_nFirstMaskForBatch.z)
                ? 2u
                : uint(gl_WorkGroupID.z >= g_nFirstMaskForBatch.y);
    uint nMaskIndex = gl_WorkGroupID.z - g_nFirstMaskForBatch[nBatch];
    CullBatch_t batch = g_TileBatches[nBatch];

    vec2 vTileCenter = (vec2(gl_GlobalInvocationID.xy) * g_vTileToCenterScale) + g_vTileToCenterOffset;

    uvec2 nGroupBase = gl_WorkGroupID.xy * uvec2(8u, 4u);
    vec2 vGroupMin = (vec2(nGroupBase) * g_vTileToCenterScale) + g_vTileToCenterOffset;
    vec2 vGroupMax = (vec2(nGroupBase + uvec2(7u, 3u)) * g_vTileToCenterScale) + g_vTileToCenterOffset;
    vec2 vGroupCenter = (vGroupMax + vGroupMin) * 0.5;
    vec2 vGroupExtent = abs(vGroupMax - vGroupMin) * 0.5;

    vec2 vGroupPixelMin = vGroupMin - g_vTileToCenterOffset;
    vec2 vGroupPixelMax = vGroupMax + g_vTileToCenterOffset;

    uint nLane = (gl_LocalInvocationID.y * 8u) + gl_LocalInvocationID.x;

#if !D_SUBGROUP_SIZE_32
    if (all(equal(gl_LocalInvocationID, uvec3(0u))))
    {
        g_nGroupMask = 0u;
    }
#endif

    uint nFirstItem = batch.nFirstItem + (nMaskIndex * 32u);
    CullItem_t coarseItem = g_CullItems[nFirstItem + nLane];

    bvec2 bAbove = greaterThanEqual(vGroupCenter, coarseItem.vBoundsMin - vGroupExtent);
    bvec2 bBelow = greaterThanEqual(coarseItem.vBoundsMax + vGroupExtent, vGroupCenter);
    bool bOverlaps = all(bvec2(bAbove.x && bBelow.x, bAbove.y && bBelow.y));

    if (OCCLUSION_CULL && bOverlaps && g_bOcclusionCullEnabled)
    {
        bOverlaps = !IsItemOccluded(
            max(coarseItem.vBoundsMin, vGroupPixelMin),
            min(coarseItem.vBoundsMax, vGroupPixelMax),
            coarseItem.vNdcDepthNear.x);
    }

    uint nLaneBit = bOverlaps ? (1u << nLane) : 0u;

#if D_SUBGROUP_SIZE_32
    uint nGroupMask = subgroupOr(nLaneBit);
#else
    barrier();

    if (nLaneBit != 0u)
    {
        atomicOr(g_nGroupMask, nLaneBit);
    }

    barrier();

    uint nGroupMask = g_nGroupMask;
#endif

    uint nBits = 0u;

    for (uint nMask = nGroupMask; nMask != 0u; nMask &= nMask - 1u)
    {
        uint nBit = uint(findLSB(nMask));
        CullItem_t item = g_CullItems[nFirstItem + nBit];

        bool bVisible = TestConic(vTileCenter, item)
                     && TestHull(vTileCenter, item.nFirstPlane, item.nNumPlanes0)
                     && TestHull(vTileCenter, item.nFirstPlane + item.nNumPlanes0, item.nNumPlanes1);

        if (bVisible)
        {
            nBits |= 1u << nBit;
        }
    }

    if (gl_GlobalInvocationID.x < g_nTilesX && gl_GlobalInvocationID.y < g_nTilesY)
    {
        uint nTile = (gl_GlobalInvocationID.y * g_nTilesX) + gl_GlobalInvocationID.x;
        g_OutputCullBits[batch.nOutputOffset + (nTile * batch.nOutputStride) + nMaskIndex] = nBits;
    }
}
