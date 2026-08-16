#version 460

#extension GL_KHR_shader_subgroup_arithmetic : enable
#extension GL_KHR_shader_subgroup_basic : enable

#define D_GROUP_TILES_X 8
#define D_GROUP_TILES_Y 4

layout(local_size_x = D_GROUP_TILES_X, local_size_y = D_GROUP_TILES_Y, local_size_z = 1) in;

struct CullBatch_t
{
    uint nOutputOffset;
    uint nOutputStride;
    uint nFirstItem;
    uint nItemEnd;
};

struct CullItem_t
{
    uint  nFirstPlane;
    uint  nUnused04;
    uint  nNumPlanes0;
    uint  nNumPlanes1;
    vec2  vBoundsMin;
    vec2  vBoundsMax;
    float flDepthMin;
    float flDepthMax;
    vec2  vNdcDepthNear;
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

layout(binding = 3, std430) readonly buffer CullPlanesBuffer
{
    vec2 g_CullPlanes[];
};

layout(binding = 2, std430) readonly buffer CullItemsBuffer
{
    CullItem_t g_CullItems[];
};

layout(binding = 12, std430) writeonly buffer OutputCullBitsBuffer
{
    uint g_OutputCullBits[];
};

// The coarse mask has to cover the whole group, and a subgroup is only guaranteed to be a part of one:
// Intel runs this at SIMD8 or SIMD16, so subgroupOr alone would leave each subgroup seeing only the
// items its own lanes loaded. Subgroup arithmetic still pre-reduces into this, it just cannot replace it.
shared uint g_nGroupMask;

const bool OCCLUSION_CULL = true;

uniform sampler2D g_tDepthPyramid; // bound above the reserved slots by Scene.SetOcclusionUniforms
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

    const uvec2 nGroupTiles = uvec2(D_GROUP_TILES_X, D_GROUP_TILES_Y);

    uvec2 nGroupBase = gl_WorkGroupID.xy * nGroupTiles;
    vec2 vGroupMin = (vec2(nGroupBase) * g_vTileToCenterScale) + g_vTileToCenterOffset;
    vec2 vGroupMax = (vec2(nGroupBase + nGroupTiles - 1u) * g_vTileToCenterScale) + g_vTileToCenterOffset;
    vec2 vGroupCenter = (vGroupMax + vGroupMin) * 0.5;
    vec2 vGroupExtent = abs(vGroupMax - vGroupMin) * 0.5;

    vec2 vGroupPixelMin = vGroupMin - g_vTileToCenterOffset;
    vec2 vGroupPixelMax = vGroupMax + g_vTileToCenterOffset;

    uint nLane = (gl_LocalInvocationID.y * uint(D_GROUP_TILES_X)) + gl_LocalInvocationID.x;

    if (gl_LocalInvocationIndex == 0u)
    {
        g_nGroupMask = 0u;
    }

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

    barrier();

#if defined(GL_KHR_shader_subgroup_arithmetic) && defined(GL_KHR_shader_subgroup_basic)
    // One atomic per subgroup rather than one per set lane. Correct at any subgroup size, including a
    // subgroup wider than the group, since the lanes outside it contribute a zero bit.
    uint nSubgroupMask = subgroupOr(nLaneBit);

    if (subgroupElect() && nSubgroupMask != 0u)
    {
        atomicOr(g_nGroupMask, nSubgroupMask);
    }
#else
    if (nLaneBit != 0u)
    {
        atomicOr(g_nGroupMask, nLaneBit);
    }
#endif

    barrier();

    uint nGroupMask = g_nGroupMask;

    uint nBits = 0u;

    vec2 vTilePixelMin = vTileCenter - g_vTileToCenterOffset;
    vec2 vTilePixelMax = vTileCenter + g_vTileToCenterOffset;

    for (uint nMask = nGroupMask; nMask != 0u; nMask &= nMask - 1u)
    {
        uint nBit = uint(findLSB(nMask));
        CullItem_t item = g_CullItems[nFirstItem + nBit];

        bool bVisible = TestConic(vTileCenter, item)
                     && TestHull(vTileCenter, item.nFirstPlane, item.nNumPlanes0)
                     && TestHull(vTileCenter, item.nFirstPlane + item.nNumPlanes0, item.nNumPlanes1);

        if (bVisible && OCCLUSION_CULL && g_bOcclusionCullEnabled)
        {
            bVisible = !IsItemOccluded(
                max(item.vBoundsMin, vTilePixelMin),
                min(item.vBoundsMax, vTilePixelMax),
                item.vNdcDepthNear.x);
        }

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
