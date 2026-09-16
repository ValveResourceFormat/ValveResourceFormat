#version 460

#ifndef VRF_LIGHT_CULL_STRUCTURES_INCLUDE
#define VRF_LIGHT_CULL_STRUCTURES_INCLUDE

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
    CullBatch_t g_TileBatches[4];
    CullBatch_t g_BinBatches[4];
};

layout(binding = 2, std430) readonly buffer CullItemsBuffer
{
    CullItem_t g_CullItems[];
};

layout(binding = 12, std430) writeonly buffer OutputCullBitsBuffer
{
    uint g_OutputCullBits[];
};

#endif
