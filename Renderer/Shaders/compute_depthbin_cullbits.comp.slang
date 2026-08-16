#version 460

layout(local_size_x = 32, local_size_y = 1, local_size_z = 1) in;

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
    vec2  vUnused40;
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

layout(binding = 2, std430) readonly buffer CullItemsBuffer
{
    CullItem_t g_CullItems[];
};

layout(binding = 12, std430) writeonly buffer OutputCullBitsBuffer
{
    uint g_OutputCullBits[];
};

void main()
{
    uint nBatch = (gl_WorkGroupID.y >= g_nFirstMaskForBatch.z)
                ? 2u
                : uint(gl_WorkGroupID.y >= g_nFirstMaskForBatch.y);
    uint nMaskIndex = gl_WorkGroupID.y - g_nFirstMaskForBatch[nBatch];
    CullBatch_t batch = g_BinBatches[nBatch];

    uint nBin = gl_GlobalInvocationID.x;
    float flBinNear = (g_fNearPlane + (float(nBin) * g_fDepthBinWidth)) - g_fBinEpsilon;
    float flBinFar = (g_fNearPlane + (float(nBin + 1u) * g_fDepthBinWidth)) + g_fBinEpsilon;

    uint nFirstItem = batch.nFirstItem + (nMaskIndex * 32u);
    uint nLastItem = min(batch.nItemEnd, nFirstItem + 32u);

    uint nBits = 0u;

    for (uint i = nFirstItem; i < nLastItem; i++)
    {
        CullItem_t item = g_CullItems[i];

        if (item.flDepthMin <= flBinFar && item.flDepthMax >= flBinNear)
        {
            nBits |= 1u << (i - nFirstItem);
        }
    }

    g_OutputCullBits[batch.nOutputOffset + (nBin * batch.nOutputStride) + nMaskIndex] = nBits;
}
